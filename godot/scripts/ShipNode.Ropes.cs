using Godot;
using System;
using System.Collections.Generic;
using NavalSim.Core;

namespace NavalSim;

/// <summary>
/// LES CORDAGES D'UN MODÈLE, REDESSINÉS EN RUBANS (fiche : model.rubans).
///
/// Rien à préparer dans Blender : on part de ce que le modèle contient déjà, et
/// la pièce garde son nœud — son parent, son mât, son sort quand le mât tombe —,
/// seul son maillage change (rope_ribbon.gdshader).
///
/// Deux sortes de cordages s'y trouvent :
///  - LE PLAN TEXTURÉ (les haubans de la Roter Löwe) : un trapèze du ton au
///    porte-hauban, dont la texture dessine les haubans en colonnes et les
///    enfléchures en rangées. On lit la texture une fois pour y trouver ses
///    lignes, puis on suit chaque ligne À TRAVERS LES TRIANGLES du plan, dans
///    l'espace de la texture : chaque morceau donne un tronçon en trois
///    dimensions, et son épaisseur est celle de la ligne, rapportée à la
///    dimension du plan à cet endroit ;
///  - LE TUBE (une courbe de Bézier à épaisseur) : ses pièces connexes sont des
///    cordes, coupées en tranches le long de leur plus grande longueur — le
///    centre de chaque tranche est un point de l'axe, sa distance aux sommets le
///    rayon. Une corde qui pend reste une courbe.
/// </summary>
public partial class ShipNode
{
    readonly struct RopeSeg
    {
        public readonly Vector3 A, B;
        public readonly float R;
        public RopeSeg(Vector3 a, Vector3 b, float r) { A = a; B = b; R = r; }
    }

    /// <summary>Une ligne de la texture : son centre et sa largeur (en coordonnée de texture), et l'étendue où elle est dessinée.</summary>
    readonly record struct TexLine(double C, double W, double Lo, double Hi);

    static Shader? _ropeShader, _ropeShaderTaa;
    static bool _ropeHashed;
    /// <summary>Les matières de ruban vivantes, pour changer de variante quand l'anticrénelage change.</summary>
    static readonly List<WeakReference<ShaderMaterial>> _ropeMats = new();

    static Shader RopeShader() => _ropeHashed
        ? (_ropeShaderTaa ??= GD.Load<Shader>("res://shaders/rope_ribbon_taa.gdshader"))
        : (_ropeShader ??= GD.Load<Shader>("res://shaders/rope_ribbon.gdshader"));

    /// <summary>
    /// SOUS TAA, LE RUBAN OPAQUE ET TRAMÉ, sinon mélangé (voir rope_ribbon_taa.gdshader) :
    /// appelé par les réglages, et il bascule aussi les navires déjà à flot.
    /// </summary>
    public static void RibbonMode(bool taa)
    {
        if (taa == _ropeHashed && (_ropeShader != null || _ropeShaderTaa != null)) return;
        _ropeHashed = taa;
        _ropeMats.RemoveAll(w => !w.TryGetTarget(out var m) || !IsInstanceValid(m));
        foreach (var w in _ropeMats) if (w.TryGetTarget(out var m)) m.Shader = RopeShader();
    }

    void RibbonRopes()
    {
        if (Spec.Model is not { Ribbons: true } || ModelRoot == null) return;
        var ropes = new List<MeshInstance3D>();
        foreach (var (mi, _) in Meshes(ModelRoot))
        {
            var mat = mi.GetActiveMaterial(0);
            if (Cordage.IsMatch(mi.Name.ToString()) || Cordage.IsMatch(mat?.ResourceName ?? "")) ropes.Add(mi);
        }
        var lineCache = new Dictionary<Texture2D, (List<TexLine> Cols, List<TexLine> Rows)?>();
        int total = 0;
        foreach (var mi in ropes)
        {
            var mat = mi.GetActiveMaterial(0) as BaseMaterial3D;
            var segs = new List<RopeSeg>();
            Color col = new(0.07f, 0.055f, 0.04f);         // chanvre goudronné
            bool drawn = mat != null && mat.AlbedoTexture != null && mat.Transparency != BaseMaterial3D.TransparencyEnum.Disabled;
            string kind;
            if (drawn)
            {
                if (!lineCache.TryGetValue(mat!.AlbedoTexture, out var lines))
                    lineCache[mat.AlbedoTexture] = lines = ReadTexLines(mat.AlbedoTexture);
                if (lines is not { } ln) continue;
                FromTexture(mi.Mesh, ln.Cols, ln.Rows, segs);
                kind = $"plan texturé, {ln.Cols.Count} haubans et {ln.Rows.Count} enfléchures par panneau";
            }
            else
            {
                FromTubes(mi.Mesh, segs);
                if (mat != null) col = MeanColour(mat);
                kind = "tube";
            }
            if (segs.Count == 0) continue;
            var sm = new ShaderMaterial { Shader = RopeShader() };
            _ropeMats.Add(new WeakReference<ShaderMaterial>(sm));
            sm.SetShaderParameter("u_color", col);
            Hazed.Add(sm);
            mi.Mesh = Ribbon(segs);
            mi.MaterialOverride = sm;
            mi.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
            total += segs.Count;
            RigLog.Add($"rubans « {mi.Name} » ({kind}) : {segs.Count} tronçons");
        }
        if (total > 0) GD.Print($"[{Spec.Id}] gréement en rubans : {ropes.Count} pièce(s), {total} tronçons");
    }

    /// <summary>
    /// LES LIGNES D'UNE TEXTURE DE GRÉEMENT : les colonnes opaques sur plus de la
    /// moitié de la hauteur sont des haubans, les rangées opaques sur plus de la
    /// moitié de la largeur des enfléchures. Chacune est un paquet de colonnes
    /// (ou de rangées) voisines, dont on garde le centre, la largeur et
    /// l'étendue où elle est vraiment dessinée.
    /// </summary>
    static (List<TexLine> Cols, List<TexLine> Rows)? ReadTexLines(Texture2D tex)
    {
        var src = tex.GetImage();
        if (src == null) return null;
        var img = (Image)src.Duplicate();
        if (img.IsCompressed()) img.Decompress();
        img.ClearMipmaps();
        img.Convert(Image.Format.Rgba8);
        int w = img.GetWidth(), h = img.GetHeight();
        var d = img.GetData();
        bool Op(int x, int y) => d[(y * w + x) * 4 + 3] > 128;
        var colN = new int[w];
        var rowN = new int[h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                if (Op(x, y)) { colN[x]++; rowN[y]++; }

        List<TexLine> Lines(int[] count, int n, int across, bool cols)
        {
            var outp = new List<TexLine>();
            int i = 0;
            while (i < n)
            {
                if (count[i] <= across / 2) { i++; continue; }
                int a = i;
                while (i < n && count[i] > across / 2) i++;
                int b = i - 1;
                // l'étendue : du premier au dernier pixel opaque du paquet, en travers
                int lo = across, hi = -1;
                for (int k = 0; k < across; k++)
                    for (int j = a; j <= b; j++)
                        if (cols ? Op(j, k) : Op(k, j)) { lo = Math.Min(lo, k); hi = Math.Max(hi, k); break; }
                outp.Add(new TexLine((a + b + 1) * 0.5 / n, (b - a + 1) / (double)n, lo / (double)across, (hi + 1) / (double)across));
            }
            return outp;
        }
        return (Lines(colN, w, h, true), Lines(rowN, h, w, false));
    }

    /// <summary>Chaque ligne de la texture, suivie à travers les triangles du plan.</summary>
    static void FromTexture(Mesh mesh, List<TexLine> cols, List<TexLine> rows, List<RopeSeg> segs)
    {
        var arr = mesh.SurfaceGetArrays(0);
        var v = arr[(int)Mesh.ArrayType.Vertex].AsVector3Array();
        if (arr[(int)Mesh.ArrayType.TexUV].VariantType == Variant.Type.Nil) return;
        var uv = arr[(int)Mesh.ArrayType.TexUV].AsVector2Array();
        var idx = arr[(int)Mesh.ArrayType.Index].VariantType == Variant.Type.Nil ? null : arr[(int)Mesh.ArrayType.Index].AsInt32Array();
        int n = idx?.Length ?? v.Length;
        Span<Vector3> P = stackalloc Vector3[3];
        Span<Vector2> T = stackalloc Vector2[3];
        for (int t = 0; t + 2 < n; t += 3)
        {
            for (int k = 0; k < 3; k++) { int q = idx?[t + k] ?? t + k; P[k] = v[q]; T[k] = uv[q]; }
            var du1 = T[1] - T[0]; var du2 = T[2] - T[0];
            float det = du1.X * du2.Y - du2.X * du1.Y;
            if (Math.Abs(det) < 1e-9f) continue;
            var e1 = P[1] - P[0]; var e2 = P[2] - P[0];
            // ce que vaut, en mètres, un pas de texture en u et en v sur ce triangle
            var Pu = (e1 * du2.Y - e2 * du1.Y) / det;
            var Pv = (e2 * du1.X - e1 * du2.X) / det;
            foreach (var c in cols) Cross(P, T, 0, c, Pu.Length(), segs);
            foreach (var r in rows) Cross(P, T, 1, r, Pv.Length(), segs);
        }
    }

    /// <summary>
    /// Le morceau de la ligne (u = C si axis 0, v = C si axis 1) qui traverse ce
    /// triangle, rogné à l'étendue où la texture la dessine.
    /// </summary>
    static void Cross(Span<Vector3> P, Span<Vector2> T, int axis, TexLine line, float metresPerUnit, List<RopeSeg> segs)
    {
        Vector3 a = default, b = default;
        float sa = 0, sb = 0;
        int found = 0;
        for (int k = 0; k < 3 && found < 2; k++)
        {
            int k2 = (k + 1) % 3;
            float x0 = (axis == 0 ? T[k].X : T[k].Y) - (float)line.C, x1 = (axis == 0 ? T[k2].X : T[k2].Y) - (float)line.C;
            if ((x0 <= 0) == (x1 <= 0)) continue;
            float f = x0 / (x0 - x1);
            var p = P[k].Lerp(P[k2], f);
            // la coordonnée LE LONG de la ligne, pour la rogner
            float s = Mathf.Lerp(axis == 0 ? T[k].Y : T[k].X, axis == 0 ? T[k2].Y : T[k2].X, f);
            if (found == 0) { a = p; sa = s; } else { b = p; sb = s; }
            found++;
        }
        if (found < 2) return;
        float lo = (float)line.Lo, hi = (float)line.Hi;
        if ((sa < lo && sb < lo) || (sa > hi && sb > hi) || Math.Abs(sb - sa) < 1e-7f) return;
        float fa = Math.Clamp((lo - sa) / (sb - sa), 0, 1), fb = Math.Clamp((hi - sa) / (sb - sa), 0, 1);
        var pa = a.Lerp(b, Math.Min(fa, fb)); var pb = a.Lerp(b, Math.Max(fa, fb));
        if ((pb - pa).LengthSquared() < 1e-8f) return;
        segs.Add(new RopeSeg(pa, pb, 0.5f * (float)line.W * metresPerUnit));
    }

    /// <summary>
    /// LES TUBES : les pièces connexes (sommets soudés par leurs triangles, et par
    /// leur position là où une couture de texture les a dédoublés), puis, pour
    /// chacune, des tranches le long de sa plus grande longueur.
    /// </summary>
    static void FromTubes(Mesh mesh, List<RopeSeg> segs)
    {
        var arr = mesh.SurfaceGetArrays(0);
        var v = arr[(int)Mesh.ArrayType.Vertex].AsVector3Array();
        var idx = arr[(int)Mesh.ArrayType.Index].VariantType == Variant.Type.Nil ? null : arr[(int)Mesh.ArrayType.Index].AsInt32Array();
        int nv = v.Length;
        if (nv < 6) return;
        var parent = new int[nv];
        for (int i = 0; i < nv; i++) parent[i] = i;
        int Find(int x) { while (parent[x] != x) { parent[x] = parent[parent[x]]; x = parent[x]; } return x; }
        void Union(int a, int b) { a = Find(a); b = Find(b); if (a != b) parent[a] = b; }
        int n = idx?.Length ?? nv;
        for (int t = 0; t + 2 < n; t += 3)
        {
            int a = idx?[t] ?? t, b = idx?[t + 1] ?? t + 1, c = idx?[t + 2] ?? t + 2;
            Union(a, b); Union(b, c);
        }
        var byPos = new Dictionary<(int, int, int), int>();
        for (int i = 0; i < nv; i++)
        {
            var key = ((int)Math.Round(v[i].X * 1e4), (int)Math.Round(v[i].Y * 1e4), (int)Math.Round(v[i].Z * 1e4));
            if (byPos.TryGetValue(key, out int j)) Union(i, j); else byPos[key] = i;
        }
        var groups = new Dictionary<int, List<int>>();
        for (int i = 0; i < nv; i++)
        {
            int r = Find(i);
            if (!groups.TryGetValue(r, out var g)) groups[r] = g = new List<int>();
            g.Add(i);
        }
        foreach (var g in groups.Values) if (g.Count >= 6) TubeAxis(v, g, segs);
    }

    static void TubeAxis(Vector3[] v, List<int> g, List<RopeSeg> segs)
    {
        var c = Vector3.Zero;
        foreach (int i in g) c += v[i];
        c /= g.Count;
        // l'axe principal, par itération de puissance sur la covariance
        double xx = 0, xy = 0, xz = 0, yy = 0, yz = 0, zz = 0;
        foreach (int i in g)
        {
            var d = v[i] - c;
            xx += d.X * d.X; xy += d.X * d.Y; xz += d.X * d.Z; yy += d.Y * d.Y; yz += d.Y * d.Z; zz += d.Z * d.Z;
        }
        var ax = new Vector3(1, 0.7f, 0.3f).Normalized();
        for (int it = 0; it < 30; it++)
        {
            var nx = new Vector3((float)(xx * ax.X + xy * ax.Y + xz * ax.Z), (float)(xy * ax.X + yy * ax.Y + yz * ax.Z), (float)(xz * ax.X + yz * ax.Y + zz * ax.Z));
            float l = nx.Length();
            if (l < 1e-12f) break;
            ax = nx / l;
        }
        float t0 = float.MaxValue, t1 = float.MinValue;
        foreach (int i in g) { float t = (v[i] - c).Dot(ax); t0 = Math.Min(t0, t); t1 = Math.Max(t1, t); }
        float len = t1 - t0;
        if (len < 1e-4f) return;
        int ns = Math.Clamp((int)Math.Round(len / 0.3f), 1, 64);
        int Slice(Vector3 p) => Math.Clamp((int)(((p - c).Dot(ax) - t0) / len * ns), 0, ns - 1);
        var sum = new Vector3[ns];
        var cnt = new int[ns];
        foreach (int i in g) { int k = Slice(v[i]); sum[k] += v[i]; cnt[k]++; }
        var pts = new List<Vector3>();
        for (int k = 0; k < ns; k++) if (cnt[k] > 0) pts.Add(sum[k] / cnt[k]);
        // le rayon : la distance moyenne des sommets au centre de leur tranche, en travers de l'axe
        double rs = 0; int rn = 0;
        foreach (int i in g)
        {
            int k = Slice(v[i]);
            var off = v[i] - sum[k] / cnt[k];
            off -= ax * off.Dot(ax);
            rs += off.Length(); rn++;
        }
        float r = rn > 0 ? (float)(rs / rn) : 0.01f;
        if (pts.Count == 1) { segs.Add(new RopeSeg(c + ax * t0, c + ax * t1, r)); return; }
        /* LES BOUTS : le centre de la première et de la dernière tranche est à une
           demi-tranche de l'extrémité du tube ; on l'y ramène le long de l'axe. */
        pts[0] += ax * (t0 - (pts[0] - c).Dot(ax));
        pts[^1] += ax * (t1 - (pts[^1] - c).Dot(ax));
        for (int k = 0; k + 1 < pts.Count; k++) segs.Add(new RopeSeg(pts[k], pts[k + 1], r));
    }

    /// <summary>La teinte moyenne de la matière (sa texture réduite, par sa couleur) : la corde garde la sienne.</summary>
    static Color MeanColour(BaseMaterial3D mat)
    {
        var col = mat.AlbedoColor;
        if (mat.AlbedoTexture?.GetImage() is { } src)
        {
            var img = (Image)src.Duplicate();
            if (img.IsCompressed()) img.Decompress();
            img.ClearMipmaps();
            img.Convert(Image.Format.Rgba8);
            img.Resize(8, 8, Image.Interpolation.Bilinear);
            float r = 0, gg = 0, b = 0;
            for (int y = 0; y < 8; y++) for (int x = 0; x < 8; x++) { var p = img.GetPixel(x, y); r += p.R; gg += p.G; b += p.B; }
            col = new Color(col.R * r / 64, col.G * gg / 64, col.B * b / 64);
        }
        return col;
    }

    /// <summary>Un ruban par tronçon : quatre sommets (deux à chaque bout, un par bord), deux triangles.</summary>
    static ArrayMesh Ribbon(List<RopeSeg> segs)
    {
        int n = segs.Count;
        var vx = new Vector3[n * 4];
        var nm = new Vector3[n * 4];
        var uv = new Vector2[n * 4];
        var ix = new int[n * 6];
        Vector3 lo = new(float.MaxValue, float.MaxValue, float.MaxValue), hi = -lo;
        for (int i = 0; i < n; i++)
        {
            var s = segs[i];
            var dir = (s.B - s.A).Normalized();
            int k = i * 4;
            vx[k] = vx[k + 1] = s.A;
            vx[k + 2] = vx[k + 3] = s.B;
            for (int j = 0; j < 4; j++) nm[k + j] = dir;
            uv[k] = new Vector2(-1, s.R); uv[k + 1] = new Vector2(1, s.R);
            uv[k + 2] = new Vector2(-1, s.R); uv[k + 3] = new Vector2(1, s.R);
            int q = i * 6;
            ix[q] = k; ix[q + 1] = k + 1; ix[q + 2] = k + 2;
            ix[q + 3] = k + 2; ix[q + 4] = k + 1; ix[q + 5] = k + 3;
            lo = lo.Min(s.A).Min(s.B); hi = hi.Max(s.A).Max(s.B);
        }
        var arr = new Godot.Collections.Array();
        arr.Resize((int)Mesh.ArrayType.Max);
        arr[(int)Mesh.ArrayType.Vertex] = vx;
        arr[(int)Mesh.ArrayType.Normal] = nm;
        arr[(int)Mesh.ArrayType.TexUV] = uv;
        arr[(int)Mesh.ArrayType.Index] = ix;
        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arr);
        // le ruban s'élargit dans le shader : la boîte doit le contenir, sans quoi il serait éliminé trop tôt
        mesh.CustomAabb = new Aabb(lo - Vector3.One * 0.5f, hi - lo + Vector3.One);
        return mesh;
    }
}
