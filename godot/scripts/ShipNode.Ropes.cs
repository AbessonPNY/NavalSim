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

    /// <summary>
    /// LE NAVIRE DU JEU : ses cordages en rubans, puis LEURS BOUTS comme attaches de
    /// bouts rompus. Quand un mât tombe, chacun de ses haubans et de ses étais casse :
    /// un bout pend du mât qui s'en va, l'autre de son point d'attache sur la coque,
    /// par-dessus bord — au lieu des deux bouts génériques d'avant, posés aux trois
    /// quarts du mât sans rapport avec ce qu'il portait.
    /// </summary>
    void RibbonRopes()
    {
        if (Spec.Model is not { Ribbons: true } || ModelRoot == null) return;
        int added = 0;
        var converted = ConvertRopes(ModelRoot, Spec, Hazed, Spec.Id);
        FindMovers(converted);
        foreach (var (mi, lines, _, _) in converted)
        {
            int si = _snapped.FindIndex(x => x.Mi == mi);
            if (si < 0) continue;
            int m = _snapped[si].Mast;
            if (m < 0 || m >= _damage.Count) continue;
            var fall = _damage[m].Fall;
            // dans le repère du navire, par les parents : rien n'oblige à être déjà dans l'arbre
            var miToShip = RelTo(mi, this);
            var shipToFall = RelTo(fall, this).AffineInverse();
            foreach (var line in lines)
            {
                double len = 0;
                for (int k = 0; k + 1 < line.Count; k++) len += line[k].DistanceTo(line[k + 1]);
                // une enfléchure ou un bout de quelques décimètres ne pend pas : seuls haubans et étais
                if (len < 2) continue;
                var p0 = miToShip * line[0]; var p1 = miToShip * line[^1];
                var (top, foot) = p0.Y >= p1.Y ? (p0, p1) : (p1, p0);
                _damage[m].Cords.Add(new CordAnchor(fall, shipToFall * top, Math.Clamp(0.45 * len, 2, 9)));
                _damage[m].Cords.Add(new CordAnchor(this, foot, Math.Clamp(0.35 * len, 1.5, 6)));
                added += 2;
            }
        }
        if (added > 0) { RigLog.Add($"bouts rompus tirés des cordages : {added} attaches"); GD.Print($"[{Spec.Id}] bouts rompus tirés des cordages : {added} attaches"); }
        FindDependencies(converted);
    }

    /// <summary>Chaque pièce de cordage et les mâts qui la tiennent : qu'un seul tombe, elle casse.</summary>
    readonly List<(MeshInstance3D Mi, HashSet<int> Masts)> _ropeDeps = new();

    /// <summary>
    /// CE QUE TOUCHENT LES DEUX BOUTS DE CHAQUE CORDE — un mât, un espar qu'il porte,
    /// ou une AUTRE corde, rattachée à un mât. Une pièce n'était rangée qu'au mât le
    /// plus proche de son milieu : une corde tendue de la tête de misaine à l'étai du
    /// grand mât restait en l'air quand le grand mât tombait (signalé :
    /// cordage_misaine.004). Désormais elle dépend des deux mâts ; celui qui tombe la
    /// casse, et elle pend de l'attache qui tient encore.
    /// </summary>
    void FindDependencies(List<(MeshInstance3D Mi, List<List<Vector3>> Lines, List<float> Radii, bool Tube)> converted)
    {
        _ropeDeps.Clear();
        // les mâts : l'axe de chaque pièce qui tombe avec eux (le mât, ses vergues, sa hune)
        var spars = new List<(int Mast, Vector3 A, Vector3 B)>();
        for (int m = 0; m < _damage.Count; m++)
            foreach (var (smi, _) in Meshes(_damage[m].Fall))
            {
                if (Cordage.IsMatch(smi.Name.ToString())) continue;
                var v = smi.Mesh.SurfaceGetArrays(0)[(int)Mesh.ArrayType.Vertex].AsVector3Array();
                if (v.Length < 2) continue;
                var toShip = RelTo(smi, this);
                var w = new Vector3[v.Length];
                for (int i = 0; i < v.Length; i++) w[i] = toShip * v[i];
                var (a, b) = AxisOf(w);
                spars.Add((m, a, b));
            }
        // les cordes, dans le repère du navire, avec le mât de leur pièce
        var ropes = new List<(MeshInstance3D Mi, int Mast, List<Vector3> P)>();
        foreach (var (mi, lines, _, _) in converted)
        {
            int si = _snapped.FindIndex(x => x.Mi == mi);
            var xf = RelTo(mi, this);
            foreach (var line in lines)
            {
                var p = new List<Vector3>(line.Count);
                foreach (var q in line) p.Add(xf * q);
                ropes.Add((mi, si >= 0 ? _snapped[si].Mast : -1, p));
            }
        }
        // à quels mâts touche ce point par un ESPAR (le mât, ses vergues, sa hune) : à moins d'un demi-mètre de l'axe
        HashSet<int> OnSpars(Vector3 p)
        {
            var hs = new HashSet<int>();
            foreach (var (m, a, b) in spars) if (DistToSegment(p, a, b) < 0.5f) hs.Add(m);
            return hs;
        }
        // premier passage : les bouts posés sur un espar
        var endA = new List<HashSet<int>>(); var endB = new List<HashSet<int>>();
        foreach (var (_, _, pts) in ropes) { endA.Add(OnSpars(pts[0])); endB.Add(OnSpars(pts[^1])); }
        /* second passage : un bout posé sur rien d'autre qu'une AUTRE CORDE hérite de
           tous les mâts qu'elle touche — un étai tient à ses deux mâts, et ce qui s'y
           amarre tombe avec l'un comme avec l'autre. */
        HashSet<int> OnRopes(Vector3 p, MeshInstance3D self)
        {
            var hs = new HashSet<int>();
            for (int j = 0; j < ropes.Count; j++)
            {
                var (mj, own, pts) = ropes[j];
                if (mj == self) continue;
                bool near = false;
                for (int k = 0; k + 1 < pts.Count && !near; k++) near = DistToSegment(p, pts[k], pts[k + 1]) < 0.3f;
                if (!near) continue;
                hs.UnionWith(endA[j]); hs.UnionWith(endB[j]);
                if (own >= 0) hs.Add(own);
            }
            return hs;
        }
        int dangling = 0;
        for (int i = 0; i < ropes.Count; i++)
        {
            var (mi, own, pts) = ropes[i];
            var sa = endA[i].Count > 0 ? endA[i] : OnRopes(pts[0], mi);
            var sb = endB[i].Count > 0 ? endB[i] : OnRopes(pts[^1], mi);
            var masts = new HashSet<int>(sa);
            masts.UnionWith(sb);
            if (own >= 0) masts.Add(own);
            if (masts.Count == 0) continue;
            int di = _ropeDeps.FindIndex(x => x.Mi == mi);
            if (di < 0) _ropeDeps.Add((mi, masts)); else _ropeDeps[di].Masts.UnionWith(masts);
            /* UN BOUT QUI PEND DE CE QUI TIENT : si un mât d'un bout tombe, la corde pend
               de l'autre bout — de sa tête de mât, là où elle est amarrée. Les cordes
               qui vont au bordé ont déjà leurs deux bouts rompus (RibbonRopes). */
            double len = 0;
            for (int k = 0; k + 1 < pts.Count; k++) len += pts[k].DistanceTo(pts[k + 1]);
            if (len < 2) continue;
            void Hang(HashSet<int> falls, HashSet<int> holds, Vector3 at)
            {
                foreach (int h in holds)
                {
                    if (falls.Contains(h)) continue;
                    var holder = _damage[h].Fall;
                    foreach (int fm in falls)
                    {
                        _damage[fm].Cords.Add(new CordAnchor(holder, RelTo(holder, this).AffineInverse() * at, Math.Clamp(0.5 * len, 2, 9)));
                        dangling++;
                    }
                    break;
                }
            }
            Hang(sa, sb, pts[^1]);
            Hang(sb, sa, pts[0]);
        }
        if (dangling > 0) RigLog.Add($"cordes d'un mât à l'autre : {dangling} bouts qui pendront");
        GD.Print($"[{Spec.Id}] cordages et mâts qui les tiennent : {_ropeDeps.Count} pièce(s), {dangling} bouts d'un mât à l'autre");
    }

    /* ------------------------------------------------------------------ */

    /// <summary>Un bout de corde accroché à un espar qui bouge : l'espar, et le point dans son repère.</summary>
    sealed class RopeEnd
    {
        public Node3D Spar = null!;
        public Vector3 OnSpar;
        /// <summary>Où ce bout était, dans le repère de la pièce, quand le modèle a été lu.</summary>
        public Vector3 Rest;
    }

    sealed class RopeMover
    {
        public MeshInstance3D Mi = null!;
        public List<List<Vector3>> Lines = null!;
        public List<float> Radii = null!;
        /// <summary>Les deux bouts de chaque corde : nuls quand le bout est fixe.</summary>
        public List<(RopeEnd? A, RopeEnd? B)> Ends = null!;
        public Vector3 LastSig = new(float.NaN, 0, 0);
    }

    readonly List<RopeMover> _movers = new();

    /// <summary>
    /// LES CORDES ACCROCHÉES À UN ESPAR QUI PIVOTE — l'antenne latine, les vergues
    /// que l'on brasse au vent. Une corde modelée entre l'antenne d'artimon et le
    /// grand mât restait où Blender l'avait mise quand l'antenne tournait : la
    /// jonction se défaisait (signalé). Un bout à moins d'un demi-mètre de l'axe
    /// d'un espar mobile lui est désormais rattaché, et la corde se tend entre ses
    /// deux attaches quand il bouge.
    /// Seules les cordes en TUBE : un plan de haubans ne va que du ton au
    /// porte-hauban, et il porte des enfléchures qu'on ne saurait pas redessiner.
    /// </summary>
    void FindMovers(List<(MeshInstance3D Mi, List<List<Vector3>> Lines, List<float> Radii, bool Tube)> converted)
    {
        _movers.Clear();
        // les espars mobiles : ce qui pend dans les pivots des vergues et de la latine
        var spars = new List<(MeshInstance3D Mi, Vector3 A, Vector3 B)>();
        var pivots = new List<Node3D>(_rigs);
        if (_latPivot != null) pivots.Add(_latPivot);
        foreach (var pv in pivots)
            foreach (var (smi, _) in Meshes(pv))
            {
                if (Cordage.IsMatch(smi.Name.ToString())) continue;
                var v = smi.Mesh.SurfaceGetArrays(0)[(int)Mesh.ArrayType.Vertex].AsVector3Array();
                if (v.Length < 2) continue;
                var toShip = RelTo(smi, this);
                var w = new Vector3[v.Length];
                for (int i = 0; i < v.Length; i++) w[i] = toShip * v[i];
                var (a, b) = AxisOf(w);
                if (a.DistanceTo(b) > 1) spars.Add((smi, a, b));
            }
        if (spars.Count == 0) return;
        foreach (var (mi, lines, radii, tube) in converted)
        {
            if (!tube || lines.Count != radii.Count) continue;
            var miToShip = RelTo(mi, this);
            var ends = new List<(RopeEnd?, RopeEnd?)>();
            bool any = false;
            RopeEnd? Hook(Vector3 pMi)
            {
                var p = miToShip * pMi;
                MeshInstance3D? best = null; float bd = 0.5f;
                foreach (var (smi, a, b) in spars)
                {
                    float d = DistToSegment(p, a, b);
                    if (d < bd) { bd = d; best = smi; }
                }
                if (best == null) return null;
                return new RopeEnd { Spar = best, OnSpar = RelTo(best, this).AffineInverse() * p, Rest = pMi };
            }
            foreach (var line in lines)
            {
                var ea = Hook(line[0]); var eb = Hook(line[^1]);
                any |= ea != null || eb != null;
                ends.Add((ea, eb));
            }
            if (any)
            {
                _movers.Add(new RopeMover { Mi = mi, Lines = lines, Radii = radii, Ends = ends });
                RigLog.Add($"« {mi.Name} » suit l'espar où elle est accrochée");
            }
        }
        if (_movers.Count > 0) GD.Print($"[{Spec.Id}] {_movers.Count} cordage(s) suivent un espar mobile");
    }

    /// <summary>
    /// À chaque réglage de la toile : les cordes accrochées à un espar qui a bougé
    /// sont redessinées entre leurs deux attaches. Le déplacement de chaque bout se
    /// répartit le long de la corde, de rien au bout fixe à tout au bout accroché —
    /// une corde tendue, qui ne se déforme pas autrement. Rien n'est refait tant que
    /// les espars ne bougent pas.
    /// </summary>
    void MoveRopes()
    {
        foreach (var m in _movers)
        {
            if (!IsInstanceValid(m.Mi) || !m.Mi.Visible) continue;
            var toMi = RelTo(m.Mi, this).AffineInverse();
            Vector3 Now(RopeEnd e) => toMi * (RelTo(e.Spar, this) * e.OnSpar);
            // la signature : la somme des bouts mobiles, pour savoir s'il y a quoi refaire
            var sig = Vector3.Zero;
            foreach (var (a, b) in m.Ends) { if (a != null) sig += Now(a); if (b != null) sig += Now(b) * 1.37f; }
            if (!float.IsNaN(m.LastSig.X) && sig.DistanceSquaredTo(m.LastSig) < 1e-6f) continue;
            m.LastSig = sig;
            var segs = new List<RopeSeg>();
            for (int i = 0; i < m.Lines.Count; i++)
            {
                var line = m.Lines[i];
                var (ea, eb) = m.Ends[i];
                var da = ea != null ? Now(ea) - ea.Rest : Vector3.Zero;
                var db = eb != null ? Now(eb) - eb.Rest : Vector3.Zero;
                float total = 0;
                for (int k = 0; k + 1 < line.Count; k++) total += line[k].DistanceTo(line[k + 1]);
                float run = 0;
                Vector3 prev = line[0] + da;
                for (int k = 1; k < line.Count; k++)
                {
                    run += line[k - 1].DistanceTo(line[k]);
                    float u = total > 0 ? run / total : 1;
                    var p = line[k] + da * (1 - u) + db * u;
                    segs.Add(new RopeSeg(prev, p, m.Radii[i]));
                    prev = p;
                }
            }
            if (segs.Count > 0) m.Mi.Mesh = Ribbon(segs);
        }
    }

    /// <summary>L'axe d'un nuage de points : ses deux bouts le long de sa plus grande longueur.</summary>
    static (Vector3 A, Vector3 B) AxisOf(Vector3[] v)
    {
        var c = Vector3.Zero;
        foreach (var p in v) c += p;
        c /= v.Length;
        double xx = 0, xy = 0, xz = 0, yy = 0, yz = 0, zz = 0;
        foreach (var p in v)
        {
            var d = p - c;
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
        foreach (var p in v) { float t = (p - c).Dot(ax); t0 = Math.Min(t0, t); t1 = Math.Max(t1, t); }
        return (c + ax * t0, c + ax * t1);
    }

    static float DistToSegment(Vector3 p, Vector3 a, Vector3 b)
    {
        var ab = b - a;
        float t = Math.Clamp((p - a).Dot(ab) / Math.Max(1e-9f, ab.LengthSquared()), 0, 1);
        return p.DistanceTo(a + ab * t);
    }

    /// <summary>La transformation de <paramref name="n"/> dans le repère de <paramref name="ancestor"/>, par ses parents.</summary>
    static Transform3D RelTo(Node3D n, Node3D ancestor)
    {
        var xf = Transform3D.Identity;
        for (Node? p = n; p != null && p != ancestor; p = p.GetParent())
            if (p is Node3D p3) xf = p3.Transform * xf;
        return xf;
    }

    /// <summary>
    /// LA CONVERSION, PARTAGÉE : le navire du jeu et ceux du mouillage (MooredNode)
    /// passent par ici. Rend, pour chaque pièce convertie, ses cordes en lignes
    /// continues (repère de la pièce) — les haubans et les tubes, pas les enfléchures.
    /// </summary>
    public static List<(MeshInstance3D Mi, List<List<Vector3>> Lines, List<float> Radii, bool Tube)> ConvertRopes(Node3D root, ShipSpec spec, List<ShaderMaterial> hazed, string who)
    {
        var done = new List<(MeshInstance3D, List<List<Vector3>>, List<float>, bool)>();
        var ropes = new List<MeshInstance3D>();
        foreach (var (mi, _) in Meshes(root))
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
            var lines = new List<List<Vector3>>();
            var radii = new List<float>();
            /* LE CHANVRE GOUDRONNÉ, BRUN ET NON NOIR : le goudron de Norvège dont on
               enduisait le gréement dormant fonçait la corde en brun profond, que le
               soleil et le sel ternissaient encore — le noir d'encre d'un dessin de
               lignes n'en est pas la couleur (demandé). La fiche peut la changer. */
            Color col = spec.Model?.RopeColour is { Length: > 0 } hex ? new Color(hex) : new Color(0.29f, 0.22f, 0.16f);
            bool drawn = mat != null && mat.AlbedoTexture != null && mat.Transparency != BaseMaterial3D.TransparencyEnum.Disabled;
            string kind;
            if (drawn)
            {
                if (!lineCache.TryGetValue(mat!.AlbedoTexture, out var tl))
                    lineCache[mat.AlbedoTexture] = tl = ReadTexLines(mat.AlbedoTexture);
                if (tl is not { } ln) continue;
                var shrouds = new List<RopeSeg>();
                FromTexture(mi.Mesh, ln.Cols, ln.Rows, shrouds, segs);
                segs.AddRange(shrouds);
                lines.AddRange(Chain(shrouds));
                kind = $"plan texturé, {ln.Cols.Count} haubans et {ln.Rows.Count} enfléchures par panneau";
            }
            else
            {
                FromTubes(mi.Mesh, segs, lines, radii);
                if (mat != null) col = MeanColour(mat);
                kind = "tube";
            }
            if (segs.Count == 0) continue;
            var sm = new ShaderMaterial { Shader = RopeShader() };
            _ropeMats.Add(new WeakReference<ShaderMaterial>(sm));
            sm.SetShaderParameter("u_color", col);
            hazed.Add(sm);
            mi.Mesh = Ribbon(segs);
            mi.MaterialOverride = sm;
            mi.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
            total += segs.Count;
            done.Add((mi, lines, radii, !drawn));
        }
        if (total > 0) GD.Print($"[{who}] gréement en rubans : {done.Count} pièce(s), {total} tronçons");
        return done;
    }

    /// <summary>
    /// LES MORCEAUX D'UNE MÊME CORDE, REMIS BOUT À BOUT : deux triangles voisins
    /// donnent deux morceaux qui se touchent sur leur arête commune (au millimètre).
    /// </summary>
    static List<List<Vector3>> Chain(List<RopeSeg> segs)
    {
        static (long, long, long) Key(Vector3 v) => ((long)Math.Round(v.X * 1000), (long)Math.Round(v.Y * 1000), (long)Math.Round(v.Z * 1000));
        var at = new Dictionary<(long, long, long), List<int>>();
        for (int i = 0; i < segs.Count; i++)
            foreach (var p in new[] { segs[i].A, segs[i].B })
            {
                var k = Key(p);
                if (!at.TryGetValue(k, out var l)) at[k] = l = new List<int>();
                l.Add(i);
            }
        var used = new bool[segs.Count];
        var outp = new List<List<Vector3>>();
        int Next(Vector3 end)
        {
            foreach (int j in at[Key(end)]) if (!used[j]) return j;
            return -1;
        }
        for (int i = 0; i < segs.Count; i++)
        {
            if (used[i]) continue;
            used[i] = true;
            var line = new List<Vector3> { segs[i].A, segs[i].B };
            for (int j = Next(line[^1]); j >= 0; j = Next(line[^1]))
            {
                used[j] = true;
                line.Add(Key(segs[j].A) == Key(line[^1]) ? segs[j].B : segs[j].A);
            }
            for (int j = Next(line[0]); j >= 0; j = Next(line[0]))
            {
                used[j] = true;
                line.Insert(0, Key(segs[j].A) == Key(line[0]) ? segs[j].B : segs[j].A);
            }
            outp.Add(line);
        }
        return outp;
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
    static void FromTexture(Mesh mesh, List<TexLine> cols, List<TexLine> rows, List<RopeSeg> shrouds, List<RopeSeg> segs)
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
            foreach (var c in cols) Cross(P, T, 0, c, Pu.Length(), shrouds);
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
    static void FromTubes(Mesh mesh, List<RopeSeg> segs, List<List<Vector3>> lines, List<float> radii)
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
        foreach (var g in groups.Values) if (g.Count >= 6) TubeAxis(v, g, segs, lines, radii);
    }

    static void TubeAxis(Vector3[] v, List<int> g, List<RopeSeg> segs, List<List<Vector3>> lines, List<float> radii)
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
        if (pts.Count == 1) { segs.Add(new RopeSeg(c + ax * t0, c + ax * t1, r)); lines.Add(new List<Vector3> { c + ax * t0, c + ax * t1 }); radii.Add(r); return; }
        /* LES BOUTS : le centre de la première et de la dernière tranche est à une
           demi-tranche de l'extrémité du tube ; on l'y ramène le long de l'axe. */
        pts[0] += ax * (t0 - (pts[0] - c).Dot(ax));
        pts[^1] += ax * (t1 - (pts[^1] - c).Dot(ax));
        for (int k = 0; k + 1 < pts.Count; k++) segs.Add(new RopeSeg(pts[k], pts[k + 1], r));
        lines.Add(pts);
        radii.Add(r);
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
