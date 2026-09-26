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
        /* SUR LE TANGAGE ET NON SUR LE ROULIS : un mât passe par-dessus la lisse,
           un beaupré plonge devant l'étrave. Le seul nombre qui les sépare. */
        public bool Pitch;
        /* CE QU'IL MONTE SUR SA LONGUEUR — sa quête. Nulle pour un mât, qui est
           debout ; c'est la hauteur de la boîte que les boulets rencontrent. */
        public double Rise;
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
            Side = d.Pitch ? 1 : side != 0 ? side : (_dmgRng.NextDouble() < 0.5 ? -1 : 1),
            Wait = delay,
            /* UN BEAUPRÉ NE VA PAS À LA VERTICALE : ses sous-barbes le tiennent par
               en dessous, et il pend en travers de l'étrave à une soixantaine de
               degrés. Un mât, lui, passe par-dessus bord et bute dans ses haubans
               à quatre-vingts. */
            Stop = d.Pitch ? 1.05 : 1.40
        };
        f.W = 0.30 * f.Rate;                     // le coup ne le pousse pas
        d.Down = f;
        // passer par-dessus bord, c'est là que tout lâche à la fois
        CutRigging(i, 4);
        SnapLines(i, true);          // et les cordages de ce mât s'en vont avec lui
        return true;
    }

    /// <summary>
    /// LE BEAUPRÉ, S IL EN A UN. Il est un espar comme les autres pour le reste
    /// du code — une entrée d avarie, une boîte pour les boulets, une chute —,
    /// mais personne ne sait son indice : ceci le trouve.
    /// </summary>
    public bool DropSprit()
    {
        for (int i = 0; i < _damage.Count; i++) if (_damage[i].Pitch) return DropMast(i);
        return false;
    }

    /* LA SOUTE LES PREND TOUS — mais pas ensemble : à quelques dixièmes de seconde
       et de bords alternés, pour la raison des trois charges. Au même instant cela
       se lit comme un objet qui casse ; décalé, comme un navire qui part en morceaux. */
    public int DropAllMasts()
    {
        int n = 0, side = _dmgRng.NextDouble() < 0.5 ? -1 : 1;
        for (int i = 0; i < _damage.Count; i++)
            if (DropMast(i, side * (i % 2 == 1 ? -1 : 1), n * 0.45)) n++;
        return n;
    }

    static readonly StringName UBurn = "u_burn";
    /* TROIS vec4 POUR SIX TROUS, deux par vec4 : les uniformes d'instance sont
       comptés, et la matière de toile est PARTAGÉE — la régler percerait toute
       la voilure d'un coup. Six suffisent : au septième la voile s'ouvre. */
    static readonly StringName UHole0 = "u_hole0", UHole1 = "u_hole1", UHole2 = "u_hole2", UHoleN = "u_hole_n";

    /// <summary>
    /// UN BOULET TRAVERSE UNE VOILE ET LUI LAISSE SON TROU — il ne s'y arrête
    /// pas, c'est pourquoi on ne s'abrite pas derrière sa voilure.
    ///
    /// Le trou est posé LÀ OÙ LE COUP A PORTÉ, et non au hasard : on cherche le
    /// sommet de la toile le plus proche du point d'impact et on prend SON u,v.
    /// La grille est assez fine pour que l'erreur se mesure en dizaines de
    /// centimètres, et le tableau des sommets est déjà rempli à chaque image
    /// pour le maillage — rien à calculer de plus.
    ///
    /// Au-delà de ce qu'elle encaisse, elle ne se troue plus : elle S'OUVRE.
    /// Une toile percée de partout finit par se fendre d'une ralingue à l'autre,
    /// et le navire la perd tout entière.
    ///
    /// Rend le nombre de trous qu'elle porte, −1 si le coup n'a rien trouvé, et
    /// −2 si la voile vient de s'ouvrir.
    /// </summary>
    public int HoleSail(int sail, Vector3 world, Vector3 dir, int max)
    {
        if (sail < 0 || sail >= _canvases.Count) return -1;
        var c = _canvases[sail];
        if (c.Split || c.V == null || c.Uv == null || c.V.Length == 0) return -1;

        /* LA TRAJECTOIRE ET NON LE POINT D'ENTRÉE.
         *
         * Ce que l'artillerie donne est l'endroit où le boulet est entré dans la
         * BOÎTE de la voile, qui est un parallélépipède autour d'une toile
         * gonflée : le point peut être à deux ou trois mètres du tissu, et les
         * trous se collaient alors aux bords (relevé : écart 2,56 m, v = 1,00,
         * c'est-à-dire sur la têtière).
         *
         * Le boulet, lui, va tout droit : on cherche donc le sommet le plus
         * proche de sa DROITE DE VOL, et celui-là est sur le tissu, là où il l'a
         * percé. Le repère est celui de la voile, où les sommets vivent. */
        var inv = c.Node.GlobalTransform.AffineInverse();
        var loc = inv * world;
        var ray = (inv.Basis * dir).Normalized();
        int best = -1;
        float bd = float.MaxValue;
        int n = Math.Min(c.V.Length, c.Uv.Length);
        for (int i = 0; i < n; i++)
        {
            var w = c.V[i] - loc;
            // sa distance à la droite : ce qu'il en reste une fois ôté le long du tir
            float d = (w - ray * w.Dot(ray)).LengthSquared();
            if (d < bd) { bd = d; best = i; }
        }
        if (best < 0) return -1;
        c.Holes.Add(c.Uv[best]);
        if (c.Holes.Count > max)
        {
            /* ELLE S'OUVRE, et ses bouts partent avec elle : le même sort que la
               voile qui a fini de brûler, par la même porte. */
            c.Split = true;
            c.Node.Visible = false;
            if (c.Mast >= 0) CutRigging(c.Mast, 1);
            return -2;
        }
        ShowHoles(c);
        return c.Holes.Count;
    }

    static void ShowHoles(Canvas c)
    {
        Vector2 H(int i) => i < c.Holes.Count ? c.Holes[i] : Vector2.Zero;
        c.Node.SetInstanceShaderParameter(UHole0, new Vector4(H(0).X, H(0).Y, H(1).X, H(1).Y));
        c.Node.SetInstanceShaderParameter(UHole1, new Vector4(H(2).X, H(2).Y, H(3).X, H(3).Y));
        c.Node.SetInstanceShaderParameter(UHole2, new Vector4(H(4).X, H(4).Y, H(5).X, H(5).Y));
        c.Node.SetInstanceShaderParameter(UHoleN, (float)c.Holes.Count);
    }

    /// <summary>Ce qu'une voile trouée porte encore, de 1 (entière) à 0 — voir GunnerySettings.SailHoleLoss.</summary>
    public double SailHoleLoss = 0.06;

    /// <summary>
    /// LE FEU MANGE LA TOILE D'UN MÂT — et on le VOIT : la voile se consume du
    /// pied vers la têtière, une lisière de braise devant, du roussi devant elle,
    /// et quand il ne reste plus rien elle est rayée des ralingues.
    ///
    /// <paramref name="rate"/> est ce qu'elle perd par seconde. Une seule voile à
    /// la fois par mât — celle qui a le moins brûlé, donc la plus basse encore
    /// entière n'est pas privilégiée : c'est celle qui a DÉJÀ commencé qui finit,
    /// puis la suivante. Un mât qui brûlerait ses six voiles ensemble se lirait
    /// comme un effet, pas comme un feu.
    ///
    /// Rend vrai tant qu'il reste de la toile à manger — faux quand le mât est nu.
    /// </summary>
    public bool BurnSails(int mast, double dt, double rate)
    {
        Canvas? pick = null;
        foreach (var c in _canvases)
        {
            if (c.Split || c.Mast != mast) continue;
            if (c.Burn > 0.001) { pick = c; break; }     // celle qui a commencé finit
            pick ??= c;
        }
        if (pick == null) return false;
        pick.Burn = Math.Min(1, pick.Burn + rate * dt);
        /* PAR INSTANCE ET NON PAR MATIÈRE : les voiles d'une même sorte partagent
           la leur, et la régler brûlerait toute la voilure d'un coup. */
        pick.Node.SetInstanceShaderParameter(UBurn, (float)pick.Burn);
        if (pick.Burn >= 1)
        {
            pick.Split = true;
            pick.Node.Visible = false;
            CutRigging(mast, 1);                        // ses bouts partent avec elle
        }
        return true;
    }

    /// <summary>
    /// Où en est la voile qui brûle sur ce mât, et où elle est dans le repère du
    /// bord : la flamme et la fumée s'y posent. Nul si rien n'y brûle.
    /// </summary>
    public (NavalSim.Core.Vec3d P, double Span, double Burn)? BurningSail(int mast)
    {
        foreach (var c in _canvases)
        {
            if (c.Split || c.Mast != mast || c.Burn <= 0.001) continue;
            /* LA LISIÈRE, ET NON LE MILIEU DE LA VOILE. Ce qui doit fumer et
               cracher ses escarbilles est le FRONT qui ronge, et il MONTE : la
               braise partait du centre et n'y bougeait pas, si bien qu'on voyait
               une torche accrochée derrière la toile au lieu d'un feu qui la
               remonte (demandé). Tirée de la boîte du maillage, qui est refait à
               chaque image et suit donc le ventre de la voile. */
            var box = c.Node.GetAabb();
            var here = new Vector3(box.Position.X + box.Size.X * 0.5f,
                                   box.Position.Y + box.Size.Y * (float)c.Burn,
                                   box.Position.Z + box.Size.Z * 0.5f);
            var g = c.Node.GlobalTransform * here;
            var loc = GlobalTransform.AffineInverse() * g;
            // la demi-laize, pour semer la braise sur toute sa largeur
            double span = Math.Max(0.5, c.Node.GlobalTransform.Basis.Scale.X * box.Size.X * 0.45);
            return (new NavalSim.Core.Vec3d(loc.X, loc.Y, loc.Z), span, c.Burn);
        }
        return null;
    }

    /// <summary>
    /// N'IMPORTE QUELLE TOILE QUI RESTE, quand celle du mât d'à côté a fini de
    /// brûler : un brasier établi ne s'arrête pas là. Rend le mât qu'il a pris,
    /// −1 s'il ne reste plus rien à manger.
    /// </summary>
    public int BurnAnySail(double dt, double rate)
    {
        // celle qui a déjà commencé d'abord, pour ne pas en entamer six à la fois
        foreach (var c in _canvases)
            if (!c.Split && c.Burn > 0.001 && c.Mast >= 0) { BurnSails(c.Mast, dt, rate); return c.Mast; }
        foreach (var c in _canvases)
            if (!c.Split && c.Mast >= 0) { BurnSails(c.Mast, dt, rate); return c.Mast; }
        return -1;
    }

    /// <summary>Le mât le plus proche de cette station, pour ce qui n'en connaît pas le numéro.</summary>
    public int MastNear(double z) => NearestMast(z);

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

    /* CE QU'UN MÂT PORTE ENCORE, ET CE QU'IL PORTAIT — en FRACTIONS de voile et
       non en voiles comptées. Une toile n'est plus entière ou rien : elle brûle
       par le pied, elle se troue au boulet, et chacun de ces états lui prend une
       PART de sa poussée. Compter les voiles debout faisait qu'une voile aux
       trois quarts consumée tirait comme une neuve jusqu'à sa dernière seconde. */
    (double Total, double Alive) CanvasOf(int mast)
    {
        double all = 0, alive = 0;
        foreach (var c in _canvases)
            if (c.Mast == mast) { all++; alive += Intact(c); }
        return (all, alive);
    }

    /// <summary>Ce qu'une voile porte encore, de 1 à 0 : ce qui n'a pas brûlé, moins ses trous.</summary>
    double Intact(Canvas c)
        => c.Split ? 0 : Math.Max(0, (1 - c.Burn) * (1 - c.Holes.Count * SailHoleLoss));

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
        Battery.Restore();
        ClearScars();               // et un bordé neuf : un radoub ne laisse pas de cicatrice          // et remonte ses pièces : le même radoub
        foreach (var c in _canvases)
        {
            c.Split = false; c.Node.Visible = true; c.Burn = 0;
            c.Node.SetInstanceShaderParameter(UBurn, 0f);
            c.Holes.Clear(); ShowHoles(c);            // une toile renverguée est une toile neuve
        }
        foreach (var d in _damage)
        {
            d.Wounds = 0;
            d.Down = null;
            d.Fall.Rotation = Vector3.Zero;
            d.Fall.Position = new Vector3(0, (float)d.Heel, d.Fall.Position.Z);   // elle revient où elle était plantée
            d.Fall.Visible = true;
        }
        // un radoub renvergue aussi ses cordages
        for (int i = 0; i < _damage.Count; i++) SnapLines(i, false);
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
                /* UNE ROTATION POSITIVE AUTOUR DE X COUCHE +Z VERS LE BAS : la
                   pointe du beaupré plonge devant l'étrave, ce qu'on veut. Son
                   bord est donc toujours +1 — il n'y a pas deux façons de tomber
                   en avant. */
                f.Rotation = d.Pitch ? new Vector3((float)s.A, 0, 0)
                                     : new Vector3(0, 0, (float)(s.Side * s.A));
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
            if (d.Pitch)
            {
                /* IL S'EN VA PAR L'AVANT, et le navire lui passe dessus : pas de
                   dérive latérale, il tombe droit et on le laisse derrière. */
                f.Rotation = new Vector3((float)(s.Stop + 0.9 * e), 0, 0);
                f.Position = new Vector3(f.Position.X,
                    (float)(d.Heel - (1.8 * e + 4.2 * s.Sink * s.Sink)), f.Position.Z);
            }
            else
            {
                f.Rotation = new Vector3(0, 0, (float)(s.Side * (s.Stop + 1.10 * e)));
                f.Position = new Vector3((float)(-s.Side * 3.6 * e),
                    (float)(d.Heel - (2.6 * e + 4.2 * s.Sink * s.Sink)), f.Position.Z);
            }
            if (s.Sink > 2.6) f.Visible = false;
        }
    }

    /// <summary>A-t-elle au moins un mât qui peut souffrir (un modèle aux espars reconnus) ?</summary>
    public bool CanBeDismasted
    {
        get { foreach (var d in _damage) if (d.HasPole) return true; return false; }
    }
}
