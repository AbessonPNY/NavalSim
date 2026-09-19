using Godot;
using System;
using System.Collections.Generic;

namespace NavalSim;

/// <summary>
/// CE QUI ABÎME LA MÂTURE ET LA TOILE — dropMast, splitSail, heaviestMast,
/// whole, standing et stepRigging de ship-model.js. La même blessure de mât quel
/// que soit ce qui la fait — foudre, bras de kraken, et demain boulets —, et deux
/// nombres que le solveur multiplie dans la poussée de la toile
/// (<see cref="NavalSim.Core.ShipPhysics.Standing"/>, <see cref="NavalSim.Core.ShipPhysics.Whole"/>) :
/// ce qu'on voit et ce qui pousse ne peuvent pas diverger.
/// </summary>
public partial class ShipNode
{
    /// <summary>Un mât d'un modèle, en tant qu'il peut souffrir : sa part de toile, ses blessures, sa chute.</summary>
    sealed class MastDamage
    {
        public Node3D Fall = null!;              // le groupe qui tombe, à son pied
        public double Heel, Height, Share;       // son pied, sa hauteur, sa part de la surface de toile
        public bool HasPole;                     // un espar à lui : lui seul peut tomber
        public int Wounds;
        public Falling? Down;                    // en train de tomber, ou tombé
        public List<CordAnchor> Cords = new();   // d'où un bout coupé peut pendre
    }

    /// <summary>D'où un bout coupé peut pendre : un point dans le repère d'un nœud du gréement, et sa longueur.</summary>
    public readonly record struct CordAnchor(Node3D Obj, Vector3 At, double Len);

    /// <summary>Change à chaque radoub : les bouts qui pendaient ne pendent plus.</summary>
    public int RigEpoch { get; private set; }

    /// <summary>Les points d'attache du mât <paramref name="i"/>, et s'il est encore là (pas coulé).</summary>
    public IReadOnlyList<CordAnchor> CordAnchors(int i) =>
        i >= 0 && i < _damage.Count ? _damage[i].Cords : Array.Empty<CordAnchor>();

    public bool MastVisible(int i) => i < 0 || i >= _damage.Count || _damage[i].Fall.Visible;

    sealed class Falling
    {
        public double A = 0.03, W, Stop = 1.40, Drag = 0.5, Sink, Rate, Wait;
        public int Side;
    }

    readonly List<MastDamage> _damage = new();
    readonly Random _dmgRng = new();

    double ShareTot { get { double s = 0; foreach (var d in _damage) s += d.Share; return s; } }

    /// <summary>
    /// UNE BLESSURE DE MÂT — blesserMat de la page. Un mât était SOLIDE : un seul
    /// boulet dans un bas mât ne l'abattait pas, il en faut trois. Vrai si le mât s'en va.
    /// </summary>
    public bool WoundMast(int i, int n = 1)
    {
        if (i < 0 || i >= _damage.Count || _damage[i].Down != null) return false;
        var d = _damage[i];
        d.Wounds += n;
        CutRigging(i, 2);
        if (d.Wounds >= 3) { DropMast(i); return true; }
        return false;
    }

    /* ------------------------------------------------------------------------
       UN MÂT TOMBE, et c'est un pendule plutôt qu'une animation.

       Un espar articulé à son pied est une tige uniforme sur un axe, et elle a
       une équation — a" = (3g/2L)·sin a — qui vaut mieux qu'une courbe dessinée à
       la main pour une raison : elle porte la TAILLE du navire. La cadence va comme
       l'inverse de la racine de la longueur, donc un bâton court bascule d'un coup
       quand un lourd penche longtemps d'abord, sans aucun nombre réglé pour l'un
       ni l'autre. Mesuré sur le grand mât du pirate, 38,7 m : 2 degrés, puis 8,
       14, 21, 30, 42, 59 et par-dessus à 3,5 s.

       Elle s'arrête à quatre-vingts degrés plutôt qu'à plat : un vrai mât passe
       par-dessus bord et vient buter dans ses propres haubans, ce qui est
       pourquoi un navire démâté est traîné par son épave au lieu d'en être quitte. */
    public bool DropMast(int i, int side = 0, double delay = 0)
    {
        if (i < 0 || i >= _damage.Count) return false;
        var d = _damage[i];
        if (!d.HasPole || d.Down != null) return false;
        double L = Math.Max(4, d.Height);
        var f = new Falling
        {
            Rate = Math.Sqrt(3 * 9.81 / (2 * L)),
            Side = side != 0 ? side : (_dmgRng.NextDouble() < 0.5 ? -1 : 1),
            Wait = delay
        };
        f.W = 0.30 * f.Rate;                     // le coup ne le pousse pas
        d.Down = f;
        // passer par-dessus bord, c'est là que tout lâche à la fois
        CutRigging(i, 4);
        return true;
    }

    /* LA TOILE EST LE FUSIBLE DU MÂT. Fait éclater UNE voile, tirée au sort parmi
       celles qui tiennent encore — pondérée par le tissu, donc un grand mât en perd
       plus souvent qu'un artimon, sans qu'aucune probabilité ait été écrite par
       navire. `hard` : la ferrure part avec le tissu, et le mât prend une blessure.
       Rend le mât dont la voile est partie, −1 s'il n'y en avait plus, −2−i si le
       mât i est passé par-dessus bord. */
    public int SplitSail(bool hard)
    {
        var alive = new List<Canvas>();
        foreach (var c in _canvases)
            if (!c.Split && c.Mast >= 0 && c.Mast < _damage.Count && _damage[c.Mast].Down == null) alive.Add(c);
        if (alive.Count == 0) return -1;
        var pick = alive[(int)Math.Floor(_dmgRng.NextDouble() * alive.Count)];
        pick.Split = true;
        pick.Node.Visible = false;
        int i = pick.Mast;
        CutRigging(i, 2);
        if (hard)
        {
            var d = _damage[i];
            d.Wounds++;
            if (d.Wounds >= 3) { DropMast(i); return -2 - i; }
        }
        return i;
    }

    (int Total, int Alive) CanvasOf(int mast)
    {
        int all = 0, alive = 0;
        foreach (var c in _canvases)
            if (c.Mast == mast) { all++; if (!c.Split) alive++; }
        return (all, alive);
    }

    /// <summary>
    /// LE MÂT QUI PORTE LE PLUS, et c'est lui qui casse : ce qui est encore envergué
    /// dessus tire plus fort que lui. Celui dont le fusible a sauté ne risque plus
    /// rien. Rend son indice et sa part de la toile de tout le navire.
    /// </summary>
    public (int I, double Part) HeaviestMast()
    {
        int best = -1; double bestShare = 0;
        for (int i = 0; i < _damage.Count; i++)
        {
            var d = _damage[i];
            if (d.Down != null || !d.HasPole) continue;
            var (all, alive) = CanvasOf(i);
            if (all == 0) continue;
            double part = d.Share * alive / all;
            if (part > bestShare) { bestShare = part; best = i; }
        }
        double tot = ShareTot;
        return (best, tot > 0 ? bestShare / tot : 0);
    }

    /// <summary>La part de toile encore ENTIÈRE, pondérée par la surface — multipliée dans la pression par le solveur.</summary>
    public double Whole()
    {
        double tot = ShareTot;
        if (!(tot > 0)) return 1;
        double s = 0;
        for (int i = 0; i < _damage.Count; i++)
        {
            var (all, alive) = CanvasOf(i);
            s += all == 0 ? _damage[i].Share : _damage[i].Share * alive / all;
        }
        return s / tot;
    }

    /// <summary>
    /// La part de toile encore EN L'AIR, pondérée par ce que chaque mât porte — un
    /// artimon n'est pas un grand mât. Elle tombe avec le cosinus de l'inclinaison,
    /// REMAPPÉ pour atteindre zéro là où le mât bute, pas à quatre-vingt-dix : un
    /// cosinus brut lui laissait huit pour cent de sa poussée avec les voiles déjà
    /// dans l'eau, halées à travers.
    /// </summary>
    public double Standing()
    {
        double tot = ShareTot;
        if (!(tot > 0)) return 1;
        double s = 0;
        foreach (var d in _damage)
        {
            var st = d.Down;
            double k = st != null ? (Math.Cos(st.A) - Math.Cos(st.Stop)) / (1 - Math.Cos(st.Stop)) : 1;
            s += d.Share * Math.Max(0, k);
        }
        return s / tot;
    }

    /* Les bouts rompus demandés et pas encore pendus : un COMPTE plutôt qu'une
       liste de cordes, tirées au hasard parmi les attaches du mât par
       CordageNode, qui vide cette liste — rien dehors n'a à se souvenir qu'il existe. */
    public readonly List<(int Mast, int N)> RigCuts = new();
    void CutRigging(int i, int n)
    {
        if (i >= 0 && i < _damage.Count && _damage[i].Cords.Count > 0) RigCuts.Add((i, Math.Max(1, n)));
    }

    /// <summary>Un radoub : mâts replantés, toile renverguée, blessures effacées.</summary>
    public void RestoreMasts()
    {
        RigCuts.Clear();
        RigEpoch++;
        foreach (var c in _canvases) { c.Split = false; c.Node.Visible = true; }
        foreach (var d in _damage)
        {
            d.Wounds = 0;
            d.Down = null;
            d.Fall.Rotation = Vector3.Zero;
            d.Fall.Position = new Vector3(0, (float)d.Heel, d.Fall.Position.Z);   // elle revient où elle était plantée
            d.Fall.Visible = true;
        }
    }

    /// <summary>
    /// Faire tomber ce qui tombe — AVANT que le solveur ne la pousse : lue après,
    /// elle serait menée une image de plus par des voiles déjà dans l'eau.
    /// </summary>
    public void StepRigging(double dt)
    {
        foreach (var d in _damage)
        {
            var s = d.Down;
            if (s == null) continue;
            var f = d.Fall;

            if (s.A < s.Stop)
            {
                if (s.Wait > 0) { s.Wait -= dt; continue; }
                s.W += s.Rate * s.Rate * Math.Sin(s.A) * dt;
                /* ET LES HAUBANS LE RETIENNENT sur la fin : un mât ne rencontre pas un
                   mur, ses rides prennent la charge sur le dernier quart et le
                   freinent — comme le CARRÉ de ce qu'il a parcouru dans ce quart, et
                   par seconde, jamais par image. */
                double pris = Math.Max(0, (s.A - 0.72 * s.Stop) / (0.28 * s.Stop));
                if (pris > 0) s.W -= s.W * pris * pris * 7.0 * dt;
                s.A = Math.Min(s.Stop, s.A + s.W * dt);
                f.Rotation = new Vector3(0, 0, (float)(s.Side * s.A));
                continue;
            }

            /* ELLE S'EN DÉBARRASSE. Un mât abattu tient d'abord dans ses haubans et
               TRAÎNE ; puis on coupe les rides, et le tout ROULE par-dessus la lisse
               et coule. Il s'enfonce dans SON repère à elle : rien à recentrer, rien
               à sortir du graphe, et la mer opaque le cache en passant dessus. Le
               roulé en douceur aux deux bouts (deux états de repos), la descente
               comme le carré du temps, ce que fait un corps qui coule. */
            if (s.Drag > 0) { s.Drag -= dt; continue; }
            s.Sink += dt;
            double u = Math.Min(1, s.Sink / 2.4);
            double e = u * u * (3 - 2 * u);
            f.Rotation = new Vector3(0, 0, (float)(s.Side * (s.Stop + 1.10 * e)));
            f.Position = new Vector3((float)(-s.Side * 3.6 * e),
                (float)(d.Heel - (2.6 * e + 4.2 * s.Sink * s.Sink)), f.Position.Z);
            if (s.Sink > 2.6) f.Visible = false;
        }
    }

    /// <summary>A-t-elle au moins un mât qui peut souffrir (un modèle aux espars reconnus) ?</summary>
    public bool CanBeDismasted
    {
        get { foreach (var d in _damage) if (d.HasPole) return true; return false; }
    }
}
