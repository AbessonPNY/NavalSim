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
    readonly List<(double Heel, double Height, double Z, int Fall)> _mastBoxes = new();

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
        const float cell = 0.45f;
        var grid = new Dictionary<(int, int, int), List<Vector3>>();
        foreach (var (mi, rel) in Meshes(ModelRoot))
            for (int s = 0; s < mi.Mesh.GetSurfaceCount(); s++)
            {
                var mat = mi.GetActiveMaterial(s);
                if (mat == null || !GunNames.IsMatch(mat.ResourceName ?? "")) continue;
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
        var bores = new List<float>();
        foreach (var (lo, hi) in pieces)
        {
            var e = hi - lo;
            if (e.X >= e.Z) bores.Add(Math.Max(e.Y, e.Z));
        }
        bores.Sort();
        float refBore = bores.Count > 0 ? bores[bores.Count >> 1] : 0;

        foreach (var (lo, hi) in pieces)
        {
            var e = hi - lo;
            var c = (lo + hi) * 0.5f;
            bool across = e.X >= e.Z;
            float bore = across ? Math.Max(e.Y, e.Z) : Math.Max(e.Y, e.X);
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
                bool fore = c.Z > 0;
                g.Side = fore ? -2 : 2;
                g.P = new Vec3d(c.X, c.Y, fore ? hi.Z : lo.Z);
                g.Dir = new Vec3d(0, 0, fore ? 1 : -1);
                g.Chase = cal < 0.9;
            }
            Battery.Guns.Add(g);
        }
        // la pièce de l'avant d'abord, dans l'ordre où elles tirent
        Battery.Guns.Sort((a, b) => b.P.Z.CompareTo(a.P.Z));
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
        var parts = ModelParts();
        if (parts.Count == 0) return null;
        Part hull = parts[0];
        double best = -1;
        foreach (var p in parts)
        {
            double v = (double)p.Size.X * p.Size.Y * p.Size.Z;
            if (v > best) { best = v; hull = p; }
        }
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

    /// <summary>Ses mâts debout, pour les boulets : pied, hauteur, station, indice. Seul un espar à lui peut être touché.</summary>
    public IReadOnlyList<(double Heel, double Height, double Z, int Fall)> MastBoxes()
    {
        _mastBoxes.Clear();
        for (int i = 0; i < _damage.Count; i++)
        {
            var d = _damage[i];
            if (!d.HasPole || d.Down != null) continue;          // déjà parti
            _mastBoxes.Add((d.Fall.Position.Y, d.Height, d.Fall.Position.Z, i));
        }
        return _mastBoxes;
    }
}
