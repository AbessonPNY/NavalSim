using Godot;
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using NavalSim.Core;

namespace NavalSim;

/// <summary>
/// Ce que l'artillerie demande au navire : ses pièces, lues sur le modèle, son
/// flanc tel que l'ŒIL le voit, et ses mâts debout.
/// </summary>
public partial class ShipNode
{
    /// <summary>Sa batterie ; vide pour une coque procédurale, qui n'en porte pas.</summary>
    public readonly Battery Battery = new();
    (double Half, double Deck, double Keel, double Z0, double Z1)[]? _shell;
    readonly List<(double Heel, double Height, double Z, double Long, int Fall)> _mastBoxes = new();

    static readonly Regex GunNames = new("canon|cannon|gun", RegexOptions.IgnoreCase);

    /* OÙ SONT SES BOUCHES, lues sur le modèle plutôt qu'écrites dans sa fiche —
       _findGuns de ship-model.js. Par NOM DE MATIÈRE, et c'est un écart qu'il faut
       défendre : un tube de canon n'a pas de forme qu'une règle puisse énoncer (un
       cylindre court et épais, la moitié du mobilier du pont), et une batterie est
       presque toujours un seul maillage. Ce qu'il y a, c'est un modéliste qui l'a
       déjà dit : les tubes du pirate portent une matière « black_canon ». Un mot
       dans un nom de matière, et un navire qui ne dit rien n'a pas de batterie.

       GROUPER D'ABORD, en trois dimensions : des cellules qui se touchent sont une
       pièce, un écart est la suivante. Puis demander à chaque pièce dans quel sens
       elle est LONGUE, la seule chose qu'un canon ne peut cacher : de travers, c'est
       une pièce de bordée ; de long, une pièce de chasse par l'étrave ou le tableau.
       Et chacune porte son CALIBRE, relatif à celui de la bordée. */
    void FindGuns()
    {
        Battery.Guns.Clear();
        if (ModelRoot == null) return;              // une coque procédurale ne porte pas de batterie
        // un modèle qui NOMME ses pièces dit tout : on ne devine plus rien
        if (NamedGuns()) return;
        const float cell = 0.45f;
        var grid = new Dictionary<(int, int, int), List<Vector3>>();
        foreach (var (mi, rel) in Meshes(ModelRoot))
            for (int s = 0; s < mi.Mesh.GetSurfaceCount(); s++)
            {
                var mat = mi.GetActiveMaterial(s);
                if (mat == null || !GunNames.IsMatch(mat.ResourceName ?? "")) continue;
                GunMat ??= mat;
                foreach (var v0 in mi.Mesh.SurfaceGetArrays(s)[(int)Mesh.ArrayType.Vertex].AsVector3Array())
                {
                    var v = rel * v0;
                    var key = ((int)Math.Floor(v.X / cell), (int)Math.Floor(v.Y / cell), (int)Math.Floor(v.Z / cell));
                    if (!grid.TryGetValue(key, out var c)) grid[key] = c = new List<Vector3>();
                    c.Add(v);
                }
            }
        if (grid.Count == 0) return;

        // remplir les cellules occupées : celles qui se touchent sont une pièce
        var seen = new HashSet<(int, int, int)>();
        var pieces = new List<(Vector3 Lo, Vector3 Hi)>();
        foreach (var start in grid.Keys)
        {
            if (!seen.Add(start)) continue;
            var stack = new Stack<(int, int, int)>();
            stack.Push(start);
            Vector3 lo = new(float.MaxValue, float.MaxValue, float.MaxValue), hi = -lo;
            while (stack.Count > 0)
            {
                var k = stack.Pop();
                foreach (var p in grid[k]) { lo = lo.Min(p); hi = hi.Max(p); }
                for (int dx = -1; dx <= 1; dx++)
                    for (int dy = -1; dy <= 1; dy++)
                        for (int dz = -1; dz <= 1; dz++)
                        {
                            var n = (k.Item1 + dx, k.Item2 + dy, k.Item3 + dz);
                            if (grid.ContainsKey(n) && seen.Add(n)) stack.Push(n);
                        }
            }
            pieces.Add((lo, hi));
        }

        // l'âme de la bordée donne l'échelle à laquelle une pièce de chasse se mesure
        /* L'ÂME EST LA PLUS PETITE MESURE EN TRAVERS DU TUBE, et non la plus
           grande : un canon modelé AVEC SON AFFÛT est large d'un côté et pas de
           l'autre, et prendre la plus grande donnait au canon de chasse de la
           frégate un calibre de 1,45 là où il est plus petit que la bordée
           (signalé). La plus petite ne voit que le tube. */
        var bores = new List<float>();
        foreach (var (lo, hi) in pieces)
        {
            var e = hi - lo;
            if (e.X >= e.Z) bores.Add(Math.Min(e.Y, e.Z));
        }
        bores.Sort();
        float refBore = bores.Count > 0 ? bores[bores.Count >> 1] : 0;

        foreach (var (lo, hi) in pieces)
        {
            var e = hi - lo;
            var c = (lo + hi) * 0.5f;
            bool across = e.X >= e.Z;
            float bore = across ? Math.Min(e.Y, e.Z) : Math.Min(e.Y, e.X);
            double cal = refBore > 0 ? Math.Max(0.5, Math.Min(1.5, bore / refBore)) : 1;
            var g = new Gun { Cal = cal };
            if (across)
            {
                g.Side = c.X < 0 ? 1 : -1;                         // tribord est −x
                // la bouche : là où elle porte le plus au large, sur l'axe du tube
                g.P = new Vec3d(g.Side > 0 ? lo.X : hi.X, c.Y, c.Z);
                g.Dir = new Vec3d(-g.Side, 0, 0);
            }
            else
            {
                /* UNE PIÈCE DE CHASSE SE SERT PAR BORD. Deux canons de proue ne
                   pointent pas ensemble — on vise avec CELUI qui porte —, et les
                   servir d'un même ordre est ce qui faisait partir les deux pour
                   une seule cible (signalé). Le bord suit la position : tribord
                   est −x, et une pièce dans l'axe reste sans bord (±2). */
                bool fore = c.Z > 0;
                bool axial = Math.Abs(c.X) < 0.35f * Math.Max(0.5f, hi.X - lo.X);
                int board = axial ? 0 : (c.X < 0 ? 1 : -1);      // +1 tribord, −1 bâbord
                g.Side = fore ? (board >= 0 ? -2 : -3) : (board >= 0 ? 2 : 3);
                g.P = new Vec3d(c.X, c.Y, fore ? hi.Z : lo.Z);
                g.Dir = new Vec3d(0, 0, fore ? 1 : -1);
                g.Chase = cal < 0.9;
            }
            Battery.Guns.Add(g);
        }
        // la pièce de l'avant d'abord, dans l'ordre où elles tirent
        Battery.Guns.Sort((a, b) => b.P.Z.CompareTo(a.P.Z));
        // et chacune détachée du bordé, pour pouvoir reculer (ShipNode.Recoil.cs)
        pieces.Sort((a, b) => ((b.Lo.Z + b.Hi.Z) * 0.5f).CompareTo((a.Lo.Z + a.Hi.Z) * 0.5f));
        SplitGuns(pieces);
    }

    /* LES PIÈCES QUE LE MODÈLE NOMME — et qui disent leur ORIENTATION.

       Un canon de chasse n'est pas parallèle à la quille : celui de la frégate
       ouvre de 38° vers le dehors, pivot compris, et une boîte englobante ne
       peut pas le dire (signalé). Quand les pièces sont des objets nommés, tout
       se lit donc sur elles : l'AXE DU TUBE est leur plus longue dimension dans
       LEUR repère, tournée par leur pose ; la bouche est au bout de cet axe ;
       l'âme est la plus petite des deux autres. Le groupe suit la direction —
       en travers c'est une pièce de bordée, vers l'avant ou l'arrière une pièce
       de chasse, servie par bord.

       Faux s'il n'y en a pas : le regroupement par matière reprend la main. */
    bool NamedGuns()
    {
        var found = new List<(MeshInstance3D Mi, Transform3D Rel)>();
        foreach (var p in NamedPieces())
            if (Tube(p) is { } t) found.Add((t.Mi, t.Rel));
        if (found.Count == 0) return false;

        var read = new List<(Vector3 At, Vector3 Dir, float Bore, float Half)>();
        foreach (var (mi, rel) in found)
        {
            var box = mi.Mesh.GetAabb();
            var e = box.Size;
            // l'axe du tube : la plus longue dimension DANS SON REPÈRE
            Vector3 axis = e.X >= e.Y && e.X >= e.Z ? Vector3.Right : e.Y >= e.Z ? Vector3.Up : Vector3.Back;
            float len = Math.Max(e.X, Math.Max(e.Y, e.Z));
            // l'âme : la plus petite des deux mesures en travers — l'affût élargit l'autre
            float bore = Math.Min(axis == Vector3.Right ? Math.Min(e.Y, e.Z) : axis == Vector3.Up ? Math.Min(e.X, e.Z) : Math.Min(e.X, e.Y), len);
            var at = rel * box.GetCenter();
            var dir = (rel.Basis * axis);
            dir = new Vector3(dir.X, 0, dir.Z).Normalized();
            if (dir.LengthSquared() < 0.5f) dir = new Vector3(at.X < 0 ? -1 : 1, 0, 0);
            // la bouche regarde DEHORS : le sens qui s'éloigne du milieu du navire
            if (dir.X * at.X + dir.Z * at.Z < 0) dir = -dir;
            float scale = rel.Basis.Scale.X;
            read.Add((at, dir, bore * scale, len * 0.5f * scale));
        }

        // l'âme de la bordée donne l'échelle des calibres
        var bores = new List<float>();
        foreach (var (at, dir, bore, _) in read) if (Math.Abs(dir.X) >= Math.Abs(dir.Z)) bores.Add(bore);
        bores.Sort();
        float refBore = bores.Count > 0 ? bores[bores.Count >> 1] : 0;

        foreach (var (at, dir, bore, half) in read)
        {
            bool across = Math.Abs(dir.X) >= Math.Abs(dir.Z);
            double cal = refBore > 0 ? Math.Clamp(bore / refBore, 0.5, 1.5) : 1;
            var g = new Gun { Cal = cal };
            if (across) g.Side = dir.X < 0 ? 1 : -1;                 // tribord est −x
            else
            {
                bool fore = dir.Z > 0;
                bool axial = Math.Abs(at.X) < 0.35f;
                int board = axial ? 0 : (at.X < 0 ? 1 : -1);
                g.Side = fore ? (board >= 0 ? -2 : -3) : (board >= 0 ? 2 : 3);
                g.Chase = cal < 0.9;
            }
            var muzzle = at + dir * half;
            g.P = new Vec3d(muzzle.X, muzzle.Y, muzzle.Z);
            g.Dir = new Vec3d(dir.X, 0, dir.Z);
            Battery.Guns.Add(g);
        }
        Battery.Guns.Sort((a, b) => b.P.Z.CompareTo(a.P.Z));
        SplitGuns(new List<(Vector3 Lo, Vector3 Hi)>());
        RigLog.Add($"batterie : {Battery.Guns.Count} pièce(s) lues sur le modèle, orientation comprise");
        return true;
    }

    /// <summary>
    /// LES PIÈCES QUE LE MODÈLE NOMME — le nœud le plus HAUT dont le nom dit canon,
    /// et TOUT ce qu'il porte.
    ///
    /// Le plus haut, et non chaque maillage. Un affût de bois modelé avec sa pièce
    /// arrive de deux façons : en seconde surface du même objet — et
    /// <see cref="SplitPrimitives"/> en fait un enfant « canon..._1 », qui porte
    /// donc le même nom —, ou en enfant nommé autrement. Compter les maillages
    /// donnait alors DEUX pièces au même endroit : une batterie de dix-sept canons
    /// sur un navire qui en porte seize, et l'affût laissé en arrière quand le tube
    /// reculait (signalé). Une pièce est un OBJET, pas une surface.
    /// </summary>
    List<Node3D> NamedPieces()
    {
        var found = new List<Node3D>();
        void Gather(Node n)
        {
            foreach (var ch in n.GetChildren())
            {
                // trouvé : on ne descend pas, ses enfants sont à elle
                if (ch is Node3D n3 && GunNames.IsMatch(n3.Name.ToString())) { found.Add(n3); continue; }
                Gather(ch);
            }
        }
        if (ModelRoot != null) Gather(ModelRoot);
        return found;
    }

    /// <summary>
    /// LE TUBE D'UNE PIÈCE : le maillage le plus LONG qu'elle porte, et sa pose.
    /// C'est lui qui dit l'axe, l'âme et la bouche — un affût est toujours plus
    /// court que le canon qu'il porte, et le prendre dans la mesure doublait le
    /// calibre lu (l'âme est la plus petite mesure en travers, et les roues sont
    /// larges). La pièce entière, elle, sert au recul.
    /// </summary>
    (MeshInstance3D Mi, Transform3D Rel)? Tube(Node3D piece)
    {
        MeshInstance3D? best = null;
        Transform3D bestRel = Transform3D.Identity;
        float longest = -1;
        void Walk(Node n, Transform3D rel)
        {
            if (n is MeshInstance3D mi && mi.Mesh != null)
            {
                var e = mi.Mesh.GetAabb().Size;
                float len = Math.Max(e.X, Math.Max(e.Y, e.Z));
                if (len > longest) { longest = len; best = mi; bestRel = rel; }
            }
            foreach (var c in n.GetChildren())
                Walk(c, c is Node3D c3 ? rel * c3.Transform : rel);
        }
        Walk(piece, RelToRoot(piece));
        return best == null ? null : (best, bestRel);
    }

    /// <summary>La pose d'un nœud par rapport à la racine du modèle.</summary>
    Transform3D RelToRoot(Node3D node)
    {
        var t = Transform3D.Identity;
        for (Node? n = node; n != null && n != ModelRoot; n = n.GetParent())
            if (n is Node3D n3) t = n3.Transform * t;
        return t;
    }

    /// <summary>La boîte d'une pièce ET de tout ce qu'elle porte, dans SON repère.</summary>
    static Aabb PieceBox(Node3D piece)
    {
        bool first = true;
        var box = new Aabb();
        void Walk(Node n, Transform3D rel)
        {
            if (n is MeshInstance3D mi && mi.Mesh != null)
            {
                var b = mi.Mesh.GetAabb();
                for (int i = 0; i < 8; i++)
                {
                    var p = rel * b.GetEndpoint(i);
                    if (first) { box = new Aabb(p, Vector3.Zero); first = false; }
                    else box = box.Expand(p);
                }
            }
            foreach (var c in n.GetChildren())
                Walk(c, c is Node3D c3 ? rel * c3.Transform : rel);
        }
        Walk(piece, Transform3D.Identity);
        return box;
    }

    /* LE FLANC QU'UN BOULET DOIT TRAVERSER, et c'est celui du MODÈLE, pas celui du
       solveur — _hullShell de la page. La grille de sondes sort du plan de formes ;
       le .glb est un autre objet, mis à l'échelle sur sa seule LONGUEUR. Sur le
       pirate le solveur met son pont à 2,8 m et le modèle ses sabords à 6,0 : visez
       ce qu'on voit et le boulet passe au-dessus d'une coque que la physique croit
       plus basse. La FORME vient donc du modèle, et ce qui passe à l'envahissement
       est une PART de son creux, qui veut dire la même chose dans les deux repères.
       Découpé dans les mêmes compartiments que l'envahissement. */
    public (double Half, double Deck, double Keel, double Z0, double Z1)[]? HullShell()
    {
        if (_shell != null || ModelRoot == null) return _shell;
        if (HullPart() is not { } hull) return null;
        int N = Config.NComp;
        double L = Spec.L, half = L * 0.5;
        var box = new (double Half, double Deck, double Keel, double Z0, double Z1)[N];
        for (int i = 0; i < N; i++) box[i] = (0, double.NegativeInfinity, double.PositiveInfinity, -half + i * L / N, -half + (i + 1) * L / N);
        foreach (var v in hull.Verts)
        {
            int i = Math.Min(N - 1, Math.Max(0, (int)Math.Floor((v.Z + half) / L * N)));
            var b = box[i];
            box[i] = (Math.Max(b.Half, Math.Abs(v.X)), Math.Max(b.Deck, v.Y), Math.Min(b.Keel, v.Y), b.Z0, b.Z1);
        }
        foreach (var b in box) if (!(b.Half > 0) || b.Deck <= b.Keel) return null;   // inutilisable
        return _shell = box;
    }

    /// <summary>La coque du modèle : sa plus grosse pièce.</summary>
    Part? HullPart()
    {
        var parts = ModelParts();
        if (parts.Count == 0) return null;
        Part hull = parts[0];
        double best = -1;
        foreach (var p in parts)
        {
            double v = (double)p.Size.X * p.Size.Y * p.Size.Z;
            if (v > best) { best = v; hull = p; }
        }
        return hull;
    }

    /// <summary>
    /// La demi-largeur du bordé du modèle, côté tribord, à la station
    /// <paramref name="z"/> et à la hauteur <paramref name="y"/> : un rayon tiré
    /// en travers, contre les TRIANGLES de la coque. Pour poser ce qui doit
    /// toucher le bordé — un écubier. Les sommets ne suffisaient pas : un modèle
    /// léger n'en a aucun dans un mètre carré de son avant, et l'on retombait sur
    /// le plan de formes, plus large que le modèle de près d'un mètre. Nul sans
    /// modèle, ou si le rayon passe à côté.
    /// </summary>
    public double? HalfAt(double z, double y)
    {
        if (ModelRoot == null || HullPart() is not { } hull) return null;
        var arrays = hull.Mi.Mesh.SurfaceGetArrays(0);
        var src = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
        var idxV = arrays[(int)Mesh.ArrayType.Index];
        int[] idx = idxV.VariantType == Variant.Type.Nil ? Array.Empty<int>() : idxV.AsInt32Array();
        int n = idx.Length > 0 ? idx.Length : src.Length;
        float Y = (float)y, Z = (float)z;
        double best = -1;
        for (int t = 0; t + 2 < n; t += 3)
        {
            var a = hull.Rel * src[idx.Length > 0 ? idx[t] : t];
            var b = hull.Rel * src[idx.Length > 0 ? idx[t + 1] : t + 1];
            var c = hull.Rel * src[idx.Length > 0 ? idx[t + 2] : t + 2];
            // le triangle vu de côté, dans le plan (y, z) : le rayon le perce-t-il ?
            float d = (b.Y - a.Y) * (c.Z - a.Z) - (c.Y - a.Y) * (b.Z - a.Z);
            if (Math.Abs(d) < 1e-9f) continue;
            float u = ((Y - a.Y) * (c.Z - a.Z) - (c.Y - a.Y) * (Z - a.Z)) / d;
            float v = ((b.Y - a.Y) * (Z - a.Z) - (Y - a.Y) * (b.Z - a.Z)) / d;
            if (u < 0 || v < 0 || u + v > 1) continue;
            float x = a.X + u * (b.X - a.X) + v * (c.X - a.X);
            if (x < 0) best = Math.Max(best, -x);           // tribord : −x
        }
        return best > 0 ? best : null;
    }

    /// <summary>
    /// Ses espars, pour les boulets : pied, hauteur, station, longueur vers
    /// l'avant, indice. Seul un espar à lui peut être touché.
    ///
    /// UN MÂT EST DEBOUT ET UN BEAUPRÉ EST COUCHÉ, et la même boîte ne peut pas
    /// les décrire tous deux : le beaupré donnait une colonne de onze mètres
    /// plantée à son talon, donc partout où il n'est pas. Sa hauteur est sa
    /// QUÊTE, et sa longueur porte vers l'avant.
    /// </summary>
    public IReadOnlyList<(double Heel, double Height, double Z, double Long, int Fall)> MastBoxes()
    {
        _mastBoxes.Clear();
        for (int i = 0; i < _damage.Count; i++)
        {
            var d = _damage[i];
            if (!d.HasPole || d.Down != null) continue;          // déjà parti
            _mastBoxes.Add(d.Pitch
                ? (d.Fall.Position.Y, d.Rise + 1.0, d.Fall.Position.Z, d.Height, i)
                : (d.Fall.Position.Y, d.Height, d.Fall.Position.Z, 0.0, i));
        }
        return _mastBoxes;
    }
}
