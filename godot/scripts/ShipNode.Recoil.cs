using Godot;
using System;
using System.Collections.Generic;
using NavalSim.Core;

namespace NavalSim;

/// <summary>
/// LE RECUL DES PIÈCES — une bordée qui part et douze tonnes qui reculent.
///
/// Les canons d'un modèle vivent dans UN seul maillage : pour qu'une pièce
/// bouge seule, il faut d'abord la détacher. C'est fait une fois à la mise à
/// l'eau : les triangles de chaque groupe (ceux que <see cref="FindGuns"/> a
/// déjà reconnus) passent dans leur propre maillage, posé sur un pivot au
/// milieu de la pièce ; ce qui reste du bordé garde le sien. Le prix est en
/// APPELS DE DESSIN — un par pièce —, jamais en calcul : reculer est une
/// translation par image, et seulement pour celles qui ont tiré.
///
/// Le mouvement suit ce que le rechargement dit déjà : elle part en arrière de
/// ce que la braguier lui laisse, reste là pendant qu'on l'écouvillonne et la
/// charge, puis revient à la batterie dans les dernières secondes.
/// </summary>
public partial class ShipNode
{
    sealed class GunPiece
    {
        public Node3D Pivot = null!;
        public Vector3 Home;          // sa place, à la batterie
        public Vector3 Back;          // le sens du recul, unitaire
        public double Was = -1;       // l'heure de disponibilité vue au tour d'avant
        public double Fired = -1, Until = -1;
    }

    readonly List<GunPiece> _gunPieces = new();

    /// <summary>Ce que la braguier laisse filer, en mètres : le calibre du navire, borné.</summary>
    double Kick => Math.Clamp(0.05 * Spec.L, 0.4, 1.4);
    /* LE TEMPS DU MOUVEMENT, à l'unité de vitesse — settings.json → gunnery →
       recoilSpeed les divise. Ces deux-là sont la première mise au point, celle
       qu'on a trouvée trop molle (signalé) ; le réglage vaut 2 par défaut. */
    const double OutIn = 0.35;        // le recul lui-même, en secondes
    const double RunOut = 1.8;        // le retour à la batterie, à la fin du rechargement

    /// <summary>
    /// Détacher les pièces du maillage qui les porte. Les boîtes sont celles des
    /// groupes reconnus ; un triangle va à la pièce qui contient son milieu.
    /// </summary>
    void SplitGuns(List<(Vector3 Lo, Vector3 Hi)> pieces)
    {
        _gunPieces.Clear();
        if (ModelRoot == null) return;

        /* SI LE MODÈLE LES A DÉJÀ SÉPARÉES, on ne découpe rien : un OBJET dont le
           NOM dit canon est une pièce, avec tout ce qu'il porte. C'est la bonne
           façon de modeler une batterie — rien à deviner, et l'affût vient avec. */
        var named = NamedPieces();
        if (named.Count > 0 && (pieces.Count == 0 || named.Count >= pieces.Count))
        {
            named.Sort((a, b) => (b.GlobalTransform.Origin.Z).CompareTo(a.GlobalTransform.Origin.Z));
            foreach (var nd in named)
            {
                /* TOUT CE QUE LA PIÈCE PORTE RECULE AVEC ELLE — l'affût, les roues,
                   ce qu'on lui ajoutera demain. Reparenter le NŒUD et non son
                   maillage emmène ses enfants sans qu'on ait à les connaître. */
                var bb = PieceBox(nd);
                var centre = nd.Transform * bb.GetCenter();
                var pivot = new Node3D { Position = centre };
                AddChild(pivot);
                nd.Reparent(pivot, true);
                _gunPieces.Add(new GunPiece { Pivot = pivot, Home = centre });
            }
            PairGuns();
            RigLog.Add($"batterie : {named.Count} pièce(s) déjà séparées dans le modèle");
            return;
        }

        // une pièce par groupe, dans l'ordre où Battery les a rangées
        var box = new List<(Vector3 Lo, Vector3 Hi)>(pieces);
        var tris = new List<List<int>>();              // indices, par pièce
        var mine = new List<(List<Vector3> P, List<Vector3> N, List<Vector2> U)>();
        for (int i = 0; i < box.Count; i++) { tris.Add(new List<int>()); mine.Add((new(), new(), new())); }

        foreach (var (mi, rel) in new List<(MeshInstance3D, Transform3D)>(Meshes(ModelRoot)))
        {
            if (mi.Mesh is not ArrayMesh && mi.Mesh == null) continue;
            var src = mi.Mesh;
            bool touched = false;
            var kept = new List<(Godot.Collections.Array Arrays, Material? Mat)>();
            for (int s = 0; s < src.GetSurfaceCount(); s++)
            {
                var mat = mi.GetActiveMaterial(s);
                var arr = src.SurfaceGetArrays(s);
                var V = arr[(int)Mesh.ArrayType.Vertex].AsVector3Array();
                var idxv = arr[(int)Mesh.ArrayType.Index];
                if (mat == null || !GunNames.IsMatch(mat.ResourceName ?? "") || V.Length == 0 || idxv.VariantType == Variant.Type.Nil)
                {
                    kept.Add((arr, mat));
                    continue;
                }
                var N = arr[(int)Mesh.ArrayType.Normal].AsVector3Array();
                var U = arr[(int)Mesh.ArrayType.TexUV].AsVector2Array();
                var I = idxv.AsInt32Array();
                var left = new List<int>();
                for (int t = 0; t + 2 < I.Length; t += 3)
                {
                    var a = rel * V[I[t]]; var b = rel * V[I[t + 1]]; var c = rel * V[I[t + 2]];
                    var mid = (a + b + c) / 3;
                    int who = -1;
                    for (int k = 0; k < box.Count; k++)
                    {
                        var (lo, hi) = box[k];
                        if (mid.X < lo.X - 0.1f || mid.X > hi.X + 0.1f) continue;
                        if (mid.Y < lo.Y - 0.1f || mid.Y > hi.Y + 0.1f) continue;
                        if (mid.Z < lo.Z - 0.1f || mid.Z > hi.Z + 0.1f) continue;
                        who = k; break;
                    }
                    if (who < 0) { left.Add(I[t]); left.Add(I[t + 1]); left.Add(I[t + 2]); continue; }
                    touched = true;
                    var (P, NN, UU) = mine[who];
                    for (int e = 0; e < 3; e++)
                    {
                        int k2 = I[t + e];
                        P.Add(rel * V[k2]);
                        NN.Add(N.Length == V.Length ? (rel.Basis * N[k2]).Normalized() : Vector3.Up);
                        UU.Add(U.Length == V.Length ? U[k2] : Vector2.Zero);
                    }
                    tris[who].Add(mat != null ? 1 : 0);
                }
                if (left.Count > 0)
                {
                    var outA = new Godot.Collections.Array();
                    outA.Resize((int)Mesh.ArrayType.Max);
                    outA[(int)Mesh.ArrayType.Vertex] = V;
                    if (N.Length == V.Length) outA[(int)Mesh.ArrayType.Normal] = N;
                    if (U.Length == V.Length) outA[(int)Mesh.ArrayType.TexUV] = U;
                    outA[(int)Mesh.ArrayType.Index] = left.ToArray();
                    kept.Add((outA, mat));
                }
            }
            if (!touched) continue;
            /* TOUT EST PARTI DANS LES PIÈCES : le maillage d'origine n'a plus une
               seule surface, et un ArrayMesh vide rendu ensuite jette « surface 0
               hors bornes » — ce qui faisait échouer la mise à l'eau en plein
               milieu et laissait une coque orpheline, immobile et intouchable,
               plantée à l'origine locale (signalé). */
            if (kept.Count == 0) { mi.Mesh = null; mi.Visible = false; continue; }
            // le maillage d'origine, refait sans les pièces
            var rebuilt = new ArrayMesh();
            for (int k = 0; k < kept.Count; k++)
            {
                rebuilt.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, kept[k].Arrays);
                if (kept[k].Mat != null) rebuilt.SurfaceSetMaterial(rebuilt.GetSurfaceCount() - 1, kept[k].Mat);
            }
            mi.Mesh = rebuilt;
        }

        // chaque pièce, sur son pivot, dans le repère du navire
        for (int k = 0; k < box.Count; k++)
        {
            var (P, N, U) = mine[k];
            if (P.Count < 3) { _gunPieces.Add(new GunPiece { Pivot = new Node3D() }); continue; }
            var centre = (box[k].Lo + box[k].Hi) * 0.5f;
            var pos = new Vector3[P.Count];
            var idx = new int[P.Count];
            for (int i = 0; i < P.Count; i++) { pos[i] = P[i] - centre; idx[i] = i; }
            var arrays = new Godot.Collections.Array();
            arrays.Resize((int)Mesh.ArrayType.Max);
            arrays[(int)Mesh.ArrayType.Vertex] = pos;
            arrays[(int)Mesh.ArrayType.Normal] = N.ToArray();
            arrays[(int)Mesh.ArrayType.TexUV] = U.ToArray();
            arrays[(int)Mesh.ArrayType.Index] = idx;
            var mesh = new ArrayMesh();
            mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
            var pivot = new Node3D { Position = centre };
            AddChild(pivot);
            pivot.AddChild(new MeshInstance3D { Mesh = mesh, MaterialOverride = GunMat });
            _gunPieces.Add(new GunPiece { Pivot = pivot, Home = centre });
        }

        PairGuns();
    }

    /* CHAQUE PIÈCE À SON CANON, par la DISTANCE et non par l'ordre de tri : deux
       pièces se font face à la même station, et un tri qui les départage au
       hasard fait reculer le bord d'en face à chaque bordée. */
    void PairGuns()
    {
        var free = new List<GunPiece>(_gunPieces);
        var paired = new List<GunPiece>();
        foreach (var g in Battery.Guns)
        {
            var at = new Vector3((float)g.P.X, (float)g.P.Y, (float)g.P.Z);
            GunPiece? best = null; float near = float.MaxValue;
            foreach (var p in free)
            {
                float d = p.Home.DistanceTo(at);
                if (d < near) { near = d; best = p; }
            }
            if (best == null) break;
            free.Remove(best);
            best.Back = new Vector3((float)-g.Dir.X, 0, (float)-g.Dir.Z).Normalized();
            paired.Add(best);
        }
        foreach (var p in free) { p.Back = Vector3.Zero; paired.Add(p); }
        _gunPieces.Clear();
        _gunPieces.AddRange(paired);
    }

    /// <summary>La matière des tubes, retenue au passage pour les pièces détachées.</summary>
    Material? GunMat;

    /// <summary>
    /// Une image de recul. Rien ne bouge tant qu'aucune pièce n'a tiré : on ne
    /// touche qu'à celles dont le rechargement court.
    /// </summary>
    /// <summary>
    /// CETTE PIÈCE VIENT DE PARLER. Dit par l'artillerie au COUP (Gunnery.OnFire),
    /// et non quand l'ordre est donné : une bordée s'égrène le long du bord, et
    /// c'est la mèche qui fait reculer, pas la main sur la touche.
    /// </summary>
    public void Recoil(Gun g, double clock)
    {
        int i = Battery.Guns.IndexOf(g);
        if (i < 0 || i >= _gunPieces.Count) return;
        _gunPieces[i].Fired = clock;
        _gunPieces[i].Until = g.ReadyAt;
    }

    /// <summary>
    /// Où en sont les pièces qui ont tiré, à cette seconde de jeu. <paramref
    /// name="speed"/> est la vivacité voulue : elle divise le temps du recul et
    /// celui du retour, sans toucher à ce que le rechargement dure.
    /// </summary>
    public void RecoilTick(double clock, double speed = 2)
    {
        double outIn = OutIn / Math.Max(0.1, speed), runOut = RunOut / Math.Max(0.1, speed);
        for (int i = 0; i < _gunPieces.Count && i < Battery.Guns.Count; i++)
        {
            var g = Battery.Guns[i];
            var p = _gunPieces[i];
            if (p.Pivot == null || !IsInstanceValid(p.Pivot)) continue;
            if (p.Fired < 0) continue;
            double t = clock - p.Fired, all = Math.Max(0.5, p.Until - p.Fired);
            double back;
            if (t < 0 || t > all) { back = 0; p.Fired = -1; }
            else if (t < outIn) { double u = t / outIn; back = u * u * (3 - 2 * u); }
            else if (t > all - runOut) { double u = Math.Clamp((all - t) / runOut, 0, 1); back = u * u * (3 - 2 * u); }
            else back = 1;
            p.Pivot.Position = p.Home + p.Back * (float)(back * Kick);
        }
    }
}
