using Godot;
using System;
using System.Collections.Generic;

namespace NavalSim;

/// <summary>
/// COUPER UN MAILLAGE PAR UN PLAN, à l'exécution — ce que Blender ferait avec
/// un « bisect », sans Blender : la soute qui saute rompt le navire là où elle
/// était, et ni l'endroit ni le navire ne sont connus d'avance.
///
/// Le plan est TRANSVERSAL, z = constante dans le repère du navire. Chaque
/// triangle est rangé d'un côté, ou recoupé quand il le traverse : ses sommets
/// neufs prennent position, normale, coordonnées de texture et tangente par
/// interpolation le long de l'arête coupée, si bien que le bois coupé garde sa
/// texture jusqu'à la tranche. Le maillage d'origine n'est pas touché : chaque
/// côté reçoit un ArrayMesh neuf, et ses matières sont celles de l'artiste.
///
/// CE QUE LA COUPE LAISSE OUVERT, elle ne le ferme pas : un maillage de coque
/// n'est pas un volume fermé (pas de pont sous le pont, pas de dessous aux
/// canons), donc on ne saurait chaîner ses arêtes en un contour sûr. La tranche
/// est bouchée à part, d'après le plan de formes (voir ShipNode.Break).
/// </summary>
public static class HullCut
{
    /// <summary>
    /// Couper tout ce qui pend sous <paramref name="root"/>. <paramref name="rootToShip"/>
    /// mène du repère de <paramref name="root"/> à celui du navire. On garde
    /// l'avant (z ≥ zCut) si <paramref name="keepFront"/>, l'arrière sinon. Ce qui
    /// est tout entier de l'autre côté est caché. <paramref name="remap"/> remplace
    /// au passage les matières (null : les garder).
    ///
    /// Rend les points de la coupe, dans le repère du navire, du maillage le plus
    /// VOLUMINEUX touché — la coque, comme partout ici —, ce qui sert à ajuster la
    /// tranche sur le bois qu'on voit.
    /// </summary>
    public static List<Vector3> Cut(Node3D root, Transform3D rootToShip, float zCut, bool keepFront,
                                    Func<Material?, Material?>? remap = null)
    {
        var best = new List<Vector3>();
        float bestVol = -1;
        var stack = new Stack<(Node, Transform3D)>();
        stack.Push((root, rootToShip));
        while (stack.Count > 0)
        {
            var (n, t) = stack.Pop();
            foreach (var kid in n.GetChildren())
                stack.Push((kid, kid is Node3D k3 ? t * k3.Transform : t));
            if (n is not MeshInstance3D mi || mi.Mesh == null || !mi.Visible) continue;

            var box = t * mi.Mesh.GetAabb();
            float lo = box.Position.Z, hi = box.End.Z;
            bool keepAll = keepFront ? lo >= zCut : hi <= zCut;
            bool dropAll = keepFront ? hi <= zCut : lo >= zCut;
            if (dropAll) { mi.Visible = false; continue; }
            if (keepAll) { Remap(mi, remap); continue; }

            var pts = new List<Vector3>();
            var cut = CutMesh(mi.Mesh, t, zCut, keepFront, pts);
            if (cut == null) { mi.Visible = false; continue; }
            var overrides = new List<Material?>();
            for (int i = 0; i < mi.Mesh.GetSurfaceCount(); i++) overrides.Add(mi.GetSurfaceOverrideMaterial(i));
            mi.Mesh = cut.Value.Mesh;
            for (int k = 0; k < cut.Value.Kept.Count; k++)
            {
                var ov = overrides[cut.Value.Kept[k]];
                if (ov != null) mi.SetSurfaceOverrideMaterial(k, ov);
            }
            Remap(mi, remap);
            float vol = box.Size.X * box.Size.Y * box.Size.Z;
            if (vol > bestVol && pts.Count > 0) { bestVol = vol; best = pts; }
        }
        return best;
    }

    static void Remap(MeshInstance3D mi, Func<Material?, Material?>? remap)
    {
        if (remap == null) return;
        if (mi.MaterialOverride != null) mi.MaterialOverride = remap(mi.MaterialOverride);
        for (int i = 0; i < mi.Mesh.GetSurfaceCount(); i++)
        {
            var ov = mi.GetSurfaceOverrideMaterial(i);
            if (ov != null) mi.SetSurfaceOverrideMaterial(i, remap(ov));
            else if (mi.Mesh is ArrayMesh am) am.SurfaceSetMaterial(i, remap(am.SurfaceGetMaterial(i)));
        }
    }

    /// <summary>Un sommet et tout ce qu'il porte, pour qu'on puisse l'interpoler.</summary>
    struct V
    {
        public Vector3 P, N; public Vector4 T; public Vector2 Uv, Uv2; public Color C;
        public static V Lerp(in V a, in V b, float s) => new()
        {
            P = a.P.Lerp(b.P, s),
            N = a.N.Lerp(b.N, s).Normalized(),
            T = new Vector4(a.T.X + (b.T.X - a.T.X) * s, a.T.Y + (b.T.Y - a.T.Y) * s, a.T.Z + (b.T.Z - a.T.Z) * s, a.T.W),
            Uv = a.Uv.Lerp(b.Uv, s), Uv2 = a.Uv2.Lerp(b.Uv2, s), C = a.C.Lerp(b.C, s)
        };
    }

    /// <summary>
    /// Un maillage recoupé, ou null s'il ne reste rien. <c>Kept</c> : pour chaque
    /// surface rendue, l'indice de la surface d'origine — les surcharges de
    /// matière se recollent par lui.
    /// </summary>
    static (ArrayMesh Mesh, List<int> Kept)? CutMesh(Mesh mesh, Transform3D toShip, float zCut, bool keepFront, List<Vector3> section)
    {
        var outMesh = new ArrayMesh();
        var kept = new List<int>();
        for (int si = 0; si < mesh.GetSurfaceCount(); si++)
        {
            var arrays = mesh.SurfaceGetArrays(si);
            var mat = mesh.SurfaceGetMaterial(si);
            if (mesh is ArrayMesh src && src.SurfaceGetPrimitiveType(si) is var prim && prim != Mesh.PrimitiveType.Triangles)
            {
                // ni ligne ni point ne se coupent : la surface va où va son milieu
                if (KeepWhole(arrays, toShip, zCut, keepFront)) { Add(outMesh, arrays, prim, mat); kept.Add(si); }
                continue;
            }
            var pos = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
            if (pos.Length == 0) continue;
            // un maillage animé par des os ne se recoupe pas sans eux : il suit son milieu
            if (arrays[(int)Mesh.ArrayType.Bones].VariantType != Variant.Type.Nil)
            {
                if (KeepWhole(arrays, toShip, zCut, keepFront)) { outMesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays); outMesh.SurfaceSetMaterial(outMesh.GetSurfaceCount() - 1, mat); kept.Add(si); }
                continue;
            }
            var nrm = Arr<Vector3>(arrays, Mesh.ArrayType.Normal);
            var tan = arrays[(int)Mesh.ArrayType.Tangent].VariantType != Variant.Type.Nil ? arrays[(int)Mesh.ArrayType.Tangent].AsFloat32Array() : null;
            var uv = Arr<Vector2>(arrays, Mesh.ArrayType.TexUV);
            var uv2 = Arr<Vector2>(arrays, Mesh.ArrayType.TexUV2);
            var col = arrays[(int)Mesh.ArrayType.Color].VariantType != Variant.Type.Nil ? arrays[(int)Mesh.ArrayType.Color].AsColorArray() : null;
            var idx = arrays[(int)Mesh.ArrayType.Index].VariantType != Variant.Type.Nil ? arrays[(int)Mesh.ArrayType.Index].AsInt32Array() : null;

            V Get(int i) => new()
            {
                P = pos[i],
                N = nrm != null ? nrm[i] : Vector3.Up,
                T = tan != null ? new Vector4(tan[i * 4], tan[i * 4 + 1], tan[i * 4 + 2], tan[i * 4 + 3]) : new Vector4(1, 0, 0, 1),
                Uv = uv != null ? uv[i] : Vector2.Zero,
                Uv2 = uv2 != null ? uv2[i] : Vector2.Zero,
                C = col != null ? col[i] : Colors.White
            };
            // la distance signée au plan, du côté qu'on garde
            float Side(Vector3 p) { float z = (toShip * p).Z - zCut; return keepFront ? z : -z; }

            var outV = new List<V>();
            int triCount = idx != null ? idx.Length / 3 : pos.Length / 3;
            Span<V> poly = stackalloc V[4];
            for (int tri = 0; tri < triCount; tri++)
            {
                int i0 = idx != null ? idx[tri * 3] : tri * 3, i1 = idx != null ? idx[tri * 3 + 1] : tri * 3 + 1, i2 = idx != null ? idx[tri * 3 + 2] : tri * 3 + 2;
                float d0 = Side(pos[i0]), d1 = Side(pos[i1]), d2 = Side(pos[i2]);
                if (d0 >= 0 && d1 >= 0 && d2 >= 0) { outV.Add(Get(i0)); outV.Add(Get(i1)); outV.Add(Get(i2)); continue; }
                if (d0 < 0 && d1 < 0 && d2 < 0) continue;
                /* SUTHERLAND–HODGMAN sur un seul plan : on parcourt le triangle et
                   l'on garde les sommets du bon côté, plus le point où chaque arête
                   franchit le plan. Trois sommets en entrée, trois ou quatre en
                   sortie, rendus en éventail dans le même sens de rotation. */
                int m = 0;
                V a = Get(i0), b = Get(i1), c = Get(i2);
                Clip(a, d0, b, d1, poly, ref m, section, toShip);
                Clip(b, d1, c, d2, poly, ref m, section, toShip);
                Clip(c, d2, a, d0, poly, ref m, section, toShip);
                for (int k = 1; k + 1 < m; k++) { outV.Add(poly[0]); outV.Add(poly[k]); outV.Add(poly[k + 1]); }
            }
            if (outV.Count == 0) continue;

            var na = new Godot.Collections.Array();
            na.Resize((int)Mesh.ArrayType.Max);
            var np = new Vector3[outV.Count];
            for (int i = 0; i < np.Length; i++) np[i] = outV[i].P;
            na[(int)Mesh.ArrayType.Vertex] = np;
            if (nrm != null) { var a = new Vector3[outV.Count]; for (int i = 0; i < a.Length; i++) a[i] = outV[i].N; na[(int)Mesh.ArrayType.Normal] = a; }
            if (tan != null)
            {
                var a = new float[outV.Count * 4];
                for (int i = 0; i < outV.Count; i++) { var t = outV[i].T; a[i * 4] = t.X; a[i * 4 + 1] = t.Y; a[i * 4 + 2] = t.Z; a[i * 4 + 3] = t.W; }
                na[(int)Mesh.ArrayType.Tangent] = a;
            }
            if (uv != null) { var a = new Vector2[outV.Count]; for (int i = 0; i < a.Length; i++) a[i] = outV[i].Uv; na[(int)Mesh.ArrayType.TexUV] = a; }
            if (uv2 != null) { var a = new Vector2[outV.Count]; for (int i = 0; i < a.Length; i++) a[i] = outV[i].Uv2; na[(int)Mesh.ArrayType.TexUV2] = a; }
            if (col != null) { var a = new Color[outV.Count]; for (int i = 0; i < a.Length; i++) a[i] = outV[i].C; na[(int)Mesh.ArrayType.Color] = a; }
            outMesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, na);
            outMesh.SurfaceSetMaterial(outMesh.GetSurfaceCount() - 1, mat);
            kept.Add(si);
        }
        return outMesh.GetSurfaceCount() > 0 ? (outMesh, kept) : null;
    }

    /// <summary>Une arête de A vers B : B s'il est gardé, et le point de passage si elle traverse.</summary>
    static void Clip(in V a, float da, in V b, float db, Span<V> poly, ref int m, List<Vector3> section, Transform3D toShip)
    {
        if ((da >= 0) != (db >= 0))
        {
            var x = V.Lerp(a, b, da / (da - db));
            poly[m++] = x;
            section.Add(toShip * x.P);
        }
        if (db >= 0) poly[m++] = b;
    }

    static T[]? Arr<T>(Godot.Collections.Array arrays, Mesh.ArrayType k)
    {
        var v = arrays[(int)k];
        if (v.VariantType == Variant.Type.Nil) return null;
        if (typeof(T) == typeof(Vector3)) return (T[])(object)v.AsVector3Array();
        if (typeof(T) == typeof(Vector2)) return (T[])(object)v.AsVector2Array();
        return null;
    }

    static bool KeepWhole(Godot.Collections.Array arrays, Transform3D toShip, float zCut, bool keepFront)
    {
        var pos = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
        if (pos.Length == 0) return false;
        Vector3 sum = Vector3.Zero;
        foreach (var p in pos) sum += p;
        float z = (toShip * (sum / pos.Length)).Z;
        return keepFront ? z >= zCut : z < zCut;
    }

    static void Add(ArrayMesh m, Godot.Collections.Array arrays, Mesh.PrimitiveType pt, Material? mat)
    {
        m.AddSurfaceFromArrays(pt, arrays);
        m.SurfaceSetMaterial(m.GetSurfaceCount() - 1, mat);
    }
}
