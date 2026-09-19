using System.Text.Json;

namespace NavalSim.Core;

/// <summary>Les règles du kraken, celles de settings.json → storm.kraken.</summary>
public sealed class KrakenSettings
{
    public bool Enabled = true;
    public double MinInten = 0.3;          // l'enfoncement dans la dépression avant qu'il remue
    public double AppearAfter = 60;        // secondes passées si profond avant qu'il se montre
    public double AppearPerMinute = 0.6;   // chance par minute, ce temps écoulé
    public double LurkFar = 450;           // où il se montre, en mètres
    public double LingerBefore = 45;       // secondes qu'il garde ses distances
    public double ApproachRate = 1.2;      // mètres par seconde qu'il gagne ensuite…
    public double ApproachGrow = 0.03;     // …et plus vite à mesure qu'elle reste (m/s par s)
    public double FleeKnots = 7;           // plus vite que ceci, il reste en arrière
    public double RetreatRate = 4;         // mètres par seconde qu'il perd quand elle file
    public double GiveUpAt = 800;          // et au-delà, il plonge
    public double GripAt = 45;             // mètres auxquels il la saisit
    public int Arms = 6;                   // bras qui la saisissent
    public double GripHold = 0.03;         // ce que tient un bras, en part de son poids
    public double GripStretch = 2.5;       // mètres de jeu avant qu'un bras tienne tout son poids
    public double GripBrake = 0.25;        // par bras, par seconde : ce qu'il ôte à son erre
    public double DamageEvery = 9;         // secondes entre deux déchirures
    public double Hp = 14;                 // les boulets qu'il encaisse (le manteau compte double)
    public double Cooldown = 900;          // secondes avant qu'un autre puisse venir
    public string? Glb;                    // un modèle à porter à la place du dessiné

    public static KrakenSettings FromJson(JsonElement k)
    {
        var s = new KrakenSettings();
        double D(string n, double v) => k.TryGetProperty(n, out var e) && e.ValueKind == JsonValueKind.Number ? e.GetDouble() : v;
        if (k.TryGetProperty("enabled", out var en) && (en.ValueKind == JsonValueKind.True || en.ValueKind == JsonValueKind.False))
            s.Enabled = en.GetBoolean();
        s.MinInten = D("minInten", s.MinInten); s.AppearAfter = D("appearAfter", s.AppearAfter);
        s.AppearPerMinute = D("appearPerMinute", s.AppearPerMinute); s.LurkFar = D("lurkFar", s.LurkFar);
        s.LingerBefore = D("lingerBefore", s.LingerBefore); s.ApproachRate = D("approachRate", s.ApproachRate);
        s.ApproachGrow = D("approachGrow", s.ApproachGrow); s.FleeKnots = D("fleeKnots", s.FleeKnots);
        s.RetreatRate = D("retreatRate", s.RetreatRate); s.GiveUpAt = D("giveUpAt", s.GiveUpAt);
        s.GripAt = D("gripAt", s.GripAt); s.Arms = (int)D("arms", s.Arms); s.GripHold = D("gripHold", s.GripHold);
        s.GripStretch = D("gripStretch", s.GripStretch); s.GripBrake = D("gripBrake", s.GripBrake);
        s.DamageEvery = D("damageEvery", s.DamageEvery); s.Hp = D("hp", s.Hp); s.Cooldown = D("cooldown", s.Cooldown);
        if (k.TryGetProperty("glb", out var g) && g.ValueKind == JsonValueKind.String) s.Glb = g.GetString();
        return s;
    }
}

/// <summary>Une tête de mât dans le repère du navire, le mât qui la porte (−1 : aucun qui tombe), et s'il est tombé.</summary>
public readonly record struct MastTop(double Y, double Z, int Fall, bool Gone);

/// <summary>
/// Ce que le kraken sait d'une coque : sa physique, ses têtes de mât, la hauteur
/// de son pont le long d'elle. Posé par la démo, un par navire.
/// </summary>
public sealed class KrakenPrey
{
    public required ShipPhysics Physics;
    public required Func<IReadOnlyList<MastTop>> Tops;
    public required Func<double, double> DeckNear;
    public string Name = "navire";
}

public enum KrakenState { Absent, Lurk, Grip, Dive }

/// <summary>Un bras : sa ligne médiane et ses repères, anneau par anneau, et ce qu'il tient.</summary>
public sealed class KrakenArm
{
    public const int Rings = 30;
    public readonly int I;
    public readonly Vec3d[] C = new Vec3d[Rings];
    // le repère de chaque anneau, transporté parallèlement : T la tangente, N l'intérieur
    public readonly Vec3d[] T = new Vec3d[Rings], N = new Vec3d[Rings];
    public Vec3d N0 = new(0, 1, 0);      // vers où l'intérieur fait face à la racine : là vont les ventouses
    public bool On;
    public double Grow, Want, Wait, Recoil;
    public readonly double Seed, Rad;
    public double Ang;
    public KrakenAnchor? Anchor;
    public Vec3d Base;                   // sa racine dans l'eau, LOCALE
    public Mooring? Line;                // la prise, en mètres monde vrais, dans ShipPhysics.Grips
    public bool Splashed;

    public KrakenArm(int i, Func<double> random)
    {
        I = i;
        Seed = random() * 100;
        Rad = 0.85 + random() * 0.25;
    }
}

/// <summary>Où un bras saisit : sur elle (son repère), à un mât ou à la lisse, et de quel bord.</summary>
public readonly record struct KrakenAnchor(double X, double Y, double Z, bool Mast, int Fall, int Side, double Rail);

/// <summary>
/// LE KRAKEN — kraken.js, porté ligne à ligne, sans Godot.
///
/// Il vit dans les dépressions et nulle part ailleurs. Une coque qui s'attarde au
/// cœur de l'une est trouvée : d'abord quelque chose de sombre qui roule dans la
/// houle à un quart de mille, des bras qui crèvent la surface et replongent ;
/// puis, si elle reste, il approche ; et à une encablure il la saisit. La vitesse
/// le tient à distance, quitter la tempête le renvoie au fond, et assez de boulets
/// aussi. Il ne la coule pas : il la tient, la couche, déchire sa toile et ses mâts.
///
/// AUCUNE PHYSIQUE NOUVELLE. Un bras qui la tient est une amarre dont le bout
/// d'en face est un point en eau profonde (<see cref="ShipPhysics.Grips"/>) : un
/// ressort plafonné à ce que l'animal peut tenir, et quand elle tire plus fort le
/// point cède et la bête est traînée. La traînée, la gîte, la barre molle sortent
/// toutes du solveur tel qu'il est.
///
/// Tout en mètres LOCAUX sauf les prises, qui sont des points monde comme toute
/// amarre : un recentrage déplace le dessin et laisse les bouts tranquilles. Le
/// hasard est injecté, comme celui du vent.
/// </summary>
public sealed class Kraken
{
    public const int MaxArms = 8;
    public KrakenSettings K;
    public KrakenState State { get; private set; } = KrakenState.Absent;
    public Vec3d Pos;                          // le manteau, local
    public double Bearing, Dist, Hp, StateT, Cool, DmgT, Roll;
    public int Hits;
    public double BodyR = 5.5;                 // ce qu'un boulet doit atteindre pour le manteau
    public KrakenPrey? Victim { get; private set; }
    public readonly KrakenArm[] ArmsList = new KrakenArm[MaxArms];

    // ce qu'il fait, dit à qui commande : appear · approach · grip · beaten · flee · storm
    public Action<string, KrakenPrey?>? Event;
    public Action<Vec3d, double, double, double>? Splash;   // où, eau, vitesse, jet
    public Action<Vec3d>? Growl;
    public Action<KrakenPrey, int>? OnMast;                  // un mât tordu ou arraché
    public Action<KrakenPrey>? OnSail;                       // une voile déchirée

    readonly Func<double> _random;
    readonly Dictionary<KrakenPrey, double> _lingered = new();
    readonly List<KrakenPrey> _forget = new();
    bool _warned;
    Ocean? _ocean;
    double _t;

    public Kraken(KrakenSettings? settings = null, Func<double>? random = null)
    {
        K = settings ?? new KrakenSettings();
        var r = new Random();
        _random = random ?? r.NextDouble;
        for (int i = 0; i < MaxArms; i++) ArmsList[i] = new KrakenArm(i, _random);
    }

    /* ------------------------------------------------------------ comportement */

    /// <summary>
    /// TOUTE COQUE DE LA DÉPRESSION PEUT ÊTRE SA PROIE, pas seulement celle qu'on
    /// barre. Chacune tient son propre temps passé au cœur ; la première à s'y
    /// être attardée assez est celle près de qui il remonte, et dès lors c'est la
    /// VICTIME qu'il guette, tient et déchire, quoi qu'on barre.
    /// </summary>
    /// <param name="list">chaque coque dans une dépression, et combien elle y est enfoncée</param>
    /// <param name="alive">est-elle encore dans la flotte et à flot</param>
    public void Update(double dt, IReadOnlyList<(KrakenPrey Prey, double Inten)> list, Ocean ocean, double t,
                       Func<KrakenPrey, bool> alive)
    {
        _ocean = ocean; _t = t;
        Cool = Math.Max(0, Cool - dt);

        if (State == KrakenState.Absent)
        {
            if (!K.Enabled || Cool > 0) { _lingered.Clear(); return; }
            KrakenPrey? ripe = null;
            for (int li = 0; li < list.Count; li++)
            {
                var (e, inten) = list[li];
                if (e.Physics.Foundered) continue;
                _lingered.TryGetValue(e, out double tm);
                tm = inten > K.MinInten ? tm + dt : Math.Max(0, tm - 2 * dt);
                _lingered[e] = tm;
                if (tm > K.AppearAfter && (ripe == null || tm > _lingered[ripe])) ripe = e;
            }
            // une coque sortie de toute dépression, ou de la flotte, oublie
            _forget.Clear();
            foreach (var e in _lingered.Keys)
            {
                bool seen = false;
                for (int li = 0; li < list.Count; li++) if (list[li].Prey == e) { seen = true; break; }
                if (!seen) _forget.Add(e);
            }
            foreach (var e in _forget) _lingered.Remove(e);
            if (ripe != null && _random() < K.AppearPerMinute * dt / 60) Summon(false, ripe);
            if (State == KrakenState.Absent) return;
        }

        var v = Victim;
        // elle n'est plus là — coulée, rayée de la carte, ou laissée par la flotte
        if (State != KrakenState.Dive && (v == null || !alive(v))) Dive("storm");
        double intenV = 0;
        for (int li = 0; li < list.Count; li++) if (list[li].Prey == v) { intenV = list[li].Inten; break; }
        var ph = v!.Physics;
        var b = ph.Body;
        double kn = Math.Sqrt(b.Vel.X * b.Vel.X + b.Vel.Z * b.Vel.Z) * 1.944;

        switch (State)
        {
            case KrakenState.Lurk:
                StateT += dt;
                if (intenV < K.MinInten * 0.5) { Dive("storm"); break; }
                if (kn > K.FleeKnots) Dist += K.RetreatRate * dt;
                else if (StateT > K.LingerBefore)
                {
                    Dist -= (K.ApproachRate + K.ApproachGrow * (StateT - K.LingerBefore)) * dt;
                    if (!_warned) { _warned = true; Event?.Invoke("approach", v); }
                }
                if (Dist > K.GiveUpAt) { Dive("flee"); break; }
                Bearing += dt * 0.025;
                if (Dist <= K.GripAt) Grip();
                break;

            case KrakenState.Grip:
                StateT += dt;
                if (intenV < K.MinInten * 0.5 || ph.Foundered) { Dive("storm"); break; }
                DmgT += dt;
                if (DmgT > K.DamageEvery) { DmgT = 0; Tear(); }
                break;

            case KrakenState.Dive:
                StateT += dt;
                if (StateT > 7)
                {
                    State = KrakenState.Absent;
                    foreach (var a in ArmsList) a.On = false;
                    return;
                }
                break;
        }
        Pose(dt, ocean, t);
    }

    /// <summary>
    /// Il se montre — au loin, par son travers à peu près. À la main pour
    /// l'essayer : <paramref name="close"/> le met tout de suite contre elle.
    /// </summary>
    public bool Summon(bool close, KrakenPrey prey)
    {
        if (State != KrakenState.Absent && State != KrakenState.Dive) return false;
        ReleaseAll();
        Victim = prey;
        State = KrakenState.Lurk; StateT = 0; _warned = false;
        Hp = K.Hp; Hits = 0;
        Roll = _random() * Math.PI * 2;             // quand son dos remonte, cette fois
        Dist = close ? K.GripAt : K.LurkFar;
        Bearing = _random() * Math.PI * 2;
        var b = prey.Physics.Body;
        Pos = new Vec3d(b.Pos.X + Math.Sin(Bearing) * Dist, -8, b.Pos.Z + Math.Cos(Bearing) * Dist);
        int n = Math.Max(2, Math.Min(4, (int)Math.Round(K.Arms / 2.0, MidpointRounding.AwayFromZero)));
        for (int i = 0; i < MaxArms; i++)
        {
            var a = ArmsList[i];
            a.On = i < n; a.Anchor = null; a.Line = null;
            a.Ang = Bearing + Math.PI + (i - (n - 1) / 2.0) * 0.9;
            a.Grow = 0; a.Want = 0;
        }
        Event?.Invoke("appear", prey);
        Growl?.Invoke(Pos);
        if (close) Grip();
        return true;
    }

    /* Il la saisit : un bras à chaque mât encore debout, le reste à la lisse,
       chacun montant de sous son flanc, l'un après l'autre. */
    void Grip()
    {
        var v = Victim!;
        var spec = v.Physics.Spec;
        var b = v.Physics.Body;
        State = KrakenState.Grip; StateT = 0; DmgT = 0;
        var anchors = new List<KrakenAnchor>();
        int side = _random() < 0.5 ? 1 : -1;
        foreach (var m in v.Tops())
        {
            if (m.Gone) continue;
            double deck = v.DeckNear(m.Z);
            anchors.Add(new KrakenAnchor(0, deck + (m.Y - deck) * 0.3, m.Z, true, m.Fall, side, deck + 0.6));
            side = -side;
        }
        double[] rails = { 0.28, -0.05, -0.32, 0.12, -0.2, 0.4 };
        for (int k = 0; anchors.Count < K.Arms && k < rails.Length * 2; k++)
        {
            double z = rails[k % rails.Length] * spec.L;
            double y = v.DeckNear(z) + 0.6;
            anchors.Add(new KrakenAnchor(side * spec.B * 0.42, y, z, false, -1, side, y));
            side = -side;
        }
        for (int i = 0; i < MaxArms; i++)
        {
            var a = ArmsList[i];
            a.On = i < Math.Min(K.Arms, anchors.Count);
            a.Anchor = a.On ? anchors[i] : null;
            a.Line = null; a.Recoil = 0; a.Splashed = false;
            a.Grow = 0; a.Want = a.On ? 1 : 0; a.Wait = i * 0.45;
            if (!a.On) continue;
            var an = a.Anchor!.Value;
            Vec3d w = b.Quat.Rotate(new Vec3d(an.Side * (spec.B / 2 + 3.5), 0, an.Z + (_random() - 0.5) * 4)) + b.Pos;
            a.Base = new Vec3d(w.X, -7, w.Z);
        }
        Dist = K.GripAt;
        Event?.Invoke("grip", v);
        Growl?.Invoke(Pos);
    }

    /* Un bras qui est monté la prend : un bout de sa racine à sa prise, un peu
       plus court que ce qu'il a, pour qu'il soit tout de suite tendu.

       LA TRACTION SE FAIT À LA LISSE, là où le bras la croise, même quand il est
       dessiné enroulé autour d'un mât. Prise au mât, au tiers de sa hauteur, la
       traction vers l'extérieur avait un bras de levier de plusieurs mètres et
       couchait le galion à 36–48° tant qu'elle tenait — le chavirage que cette
       créature n'est pas censée causer. À la lisse elle la traîne et la gîte, pas
       plus. */
    void Hold(KrakenArm a)
    {
        var v = Victim!;
        var b = v.Physics.Body;
        var O = _ocean!.Origin;
        var an = a.Anchor!.Value;
        Vec3d rail = an.Mast ? new Vec3d(an.Side * v.Physics.Spec.B * 0.42, an.Rail, an.Z) : new Vec3d(an.X, an.Y, an.Z);
        Vec3d A = b.Quat.Rotate(rail) + b.Pos;
        double d = (A - a.Base).Length;
        a.Line = new Mooring
        {
            Lx = rail.X, Ly = rail.Y, Lz = rail.Z,
            Wx = a.Base.X + O.X, Wy = a.Base.Y, Wz = a.Base.Z + O.Z,
            Len = d * 0.85, Stretch = K.GripStretch,
            Hold = K.GripHold * b.Mass * Config.G, Brake = K.GripBrake
        };
        v.Physics.Grips.Add(a.Line);
    }

    void Let(KrakenArm a)
    {
        if (a.Line == null) return;
        Victim?.Physics.Grips.Remove(a.Line);
        // la racine reste où la prise avait été traînée
        var O = _ocean!.Origin;
        a.Base = new Vec3d(a.Line.Wx - O.X, a.Base.Y, a.Line.Wz - O.Z);
        a.Line = null;
    }

    void ReleaseAll()
    {
        foreach (var a in ArmsList) if (a.Line != null) Let(a);
    }

    /* Un bras fait son dégât : un mât qu'il tient est tordu, les bras de la lisse
       déchirent la toile. Un mât déjà tombé laisse son bras aller à la lisse. */
    void Tear()
    {
        var holding = new List<KrakenArm>();
        foreach (var a in ArmsList) if (a.On && a.Line != null) holding.Add(a);
        if (holding.Count == 0) return;
        var h = holding[(int)Math.Floor(_random() * holding.Count)];
        var an = h.Anchor!.Value;
        var v = Victim!;
        bool mastStanding = false;
        if (an.Mast && an.Fall >= 0)
            foreach (var m in v.Tops()) if (m.Fall == an.Fall && !m.Gone) { mastStanding = true; break; }
        if (mastStanding) OnMast?.Invoke(v, an.Fall);
        else OnSail?.Invoke(v);
        var b = v.Physics.Body;
        Splash?.Invoke(b.Quat.Rotate(new Vec3d(an.X, an.Y, an.Z)) + b.Pos, 6, 5, 1.2);
    }

    /// <summary>Il plonge : il lâche tout, et ne reviendra pas avant <see cref="KrakenSettings.Cooldown"/>.</summary>
    public void Dive(string why)
    {
        ReleaseAll();
        State = KrakenState.Dive; StateT = 0;
        foreach (var a in ArmsList) a.Want = 0;
        Cool = K.Cooldown;
        Event?.Invoke(why, Victim);
    }

    /* ------------------------------------------------------------- la pose */

    /// <summary>Vers où le manteau regarde : elle, à sa hauteur à lui.</summary>
    public Vec3d HeadTarget { get; private set; }

    void Pose(double dt, Ocean ocean, double t)
    {
        var v = Victim!;
        var b = v.Physics.Body;
        var spec = v.Physics.Spec;

        // --- le manteau ---
        double tx, tz, ty;
        if (State == KrakenState.Grip)
        {
            // contre elle, bas, du bord que la plupart de ses bras tiennent
            double sx = 0, sz = 0; int n = 0;
            foreach (var a in ArmsList)
            {
                if (!a.On) continue;
                sx += a.Line != null ? a.Line.Wx - ocean.Origin.X : a.Base.X;
                sz += a.Line != null ? a.Line.Wz - ocean.Origin.Z : a.Base.Z;
                n++;
            }
            tx = n > 0 ? sx / n : b.Pos.X; tz = n > 0 ? sz / n : b.Pos.Z;
            double dx = tx - b.Pos.X, dz = tz - b.Pos.Z, d = Math.Sqrt(dx * dx + dz * dz);
            if (d == 0) d = 1;
            tx = b.Pos.X + dx / d * Math.Max(d, spec.B * 0.5 + 9);
            tz = b.Pos.Z + dz / d * Math.Max(d, spec.B * 0.5 + 9);
            ty = ocean.Sample(tx, tz, t) - 3.0;      // une bosse, pas un plat qui flotte
        }
        else if (State == KrakenState.Dive)
        {
            tx = Pos.X; tz = Pos.Z;
            ty = Pos.Y - 3 * dt;                     // il descend, trois mètres par seconde
        }
        else
        {
            tx = b.Pos.X + Math.Sin(Bearing) * Dist;
            tz = b.Pos.Z + Math.Cos(Bearing) * Dist;
            // il roule vers le haut de temps en temps, jamais tout à fait hors de l'eau
            double surf = Math.Max(0, Math.Sin(t * 0.21 + Roll));
            ty = ocean.Sample(tx, tz, t) - 1.8 - 4.5 * (1 - surf);
        }
        double k = Math.Min(1, dt * (State == KrakenState.Lurk ? 0.6 : 1.5));
        double px = Pos.X + (tx - Pos.X) * k, pz = Pos.Z + (tz - Pos.Z) * k;
        double py = State == KrakenState.Dive ? ty : Pos.Y + (ty - Pos.Y) * Math.Min(1, dt * 1.2);
        Pos = new Vec3d(px, py, pz);
        HeadTarget = new Vec3d(b.Pos.X, Pos.Y, b.Pos.Z);

        // --- les bras ---
        foreach (var a in ArmsList)
        {
            if (!a.On) continue;
            if (State == KrakenState.Lurk)
            {
                a.Ang += dt * 0.04;
                LurkLine(a, t, ocean);
                Frames(a);
                continue;
            }
            if (a.Anchor == null)                // des bras qui rôdaient, pris par une plongée
            {
                LurkLine(a, t, ocean);
                for (int r = 0; r < KrakenArm.Rings; r++) a.C[r] = a.C[r] - new Vec3d(0, StateT * 2, 0);
                Frames(a);
                continue;
            }
            if (a.Wait > 0) a.Wait -= dt;
            else
            {
                if (a.Recoil > 0)
                {
                    a.Recoil -= dt;
                    a.Want = 0;
                    if (a.Recoil <= 0 && State == KrakenState.Grip) a.Want = 1;
                }
                double rate = a.Want > a.Grow ? 0.55 : 1.1;
                a.Grow += Math.Max(-rate * dt, Math.Min(rate * dt, a.Want - a.Grow));
            }
            if (a.Grow > 0.05 && !a.Splashed)
            {
                a.Splashed = true;
                Splash?.Invoke(new Vec3d(a.Base.X, ocean.Sample(a.Base.X, a.Base.Z, t), a.Base.Z), 14, 6, 1.6);
            }
            if (a.Grow < 0.02) a.Splashed = false;
            if (State == KrakenState.Grip && a.Line == null && a.Grow > 0.9 && a.Recoil <= 0)
            {
                // le mât qu'il visait a pu passer par-dessus bord entre-temps
                var an = a.Anchor.Value;
                if (an.Mast && an.Fall >= 0)
                {
                    bool gone = false;
                    foreach (var m in v.Tops()) if (m.Fall == an.Fall) { gone = m.Gone; break; }
                    if (gone) a.Anchor = new KrakenAnchor(an.Side * spec.B * 0.42, an.Y * 0.4, an.Z, false, -1, an.Side, an.Rail);
                }
                Hold(a);
            }
            if (a.Line != null && (a.Grow < 0.8 || State != KrakenState.Grip)) Let(a);
            GripLine(a, t, ocean, b.Quat, b.Pos);
            if (State == KrakenState.Dive)
                for (int r = 0; r < KrakenArm.Rings; r++) a.C[r] = a.C[r] - new Vec3d(0, StateT * 1.5 * (1 - a.Grow), 0);
            Frames(a);
        }
    }

    /* Une arche qui roule hors de la mer à côté du manteau, et replonge. */
    void LurkLine(KrakenArm a, double t, Ocean ocean)
    {
        double e = Math.Pow(Math.Max(0, Math.Sin(t * 0.33 + a.Seed)), 0.7);
        double ca = Math.Cos(a.Ang), sa = Math.Sin(a.Ang);
        a.N0 = new Vec3d(ca, 0, sa);             // le dessous de l'arche regarde le long d'elle
        double x0 = Pos.X + ca * 5, z0 = Pos.Z + sa * 5;
        const double span = 13, H = 7.5;
        for (int r = 0; r < KrakenArm.Rings; r++)
        {
            double s = r / (double)(KrakenArm.Rings - 1);
            double wig = Math.Sin(s * 5 + t * 1.3 + a.Seed) * 0.9 * e;
            double x = x0 + ca * span * s - sa * wig, z = z0 + sa * span * s + ca * wig;
            double sea = ocean.Sample(x, z, t);
            a.C[r] = new Vec3d(x, sea - 3 + (H + 3) * Math.Sin(Math.PI * s) * e, z);
        }
    }

    /* De la racine sous son flanc, par-dessus la lisse, et quelques tours autour
       de la prise. `Grow` déroule le bras le long de ce chemin : il TEND le bras. */
    void GripLine(KrakenArm a, double t, Ocean ocean, Quatd q, Vec3d shipPos)
    {
        var O = ocean.Origin;
        var an = a.Anchor!.Value;
        Vec3d bas = a.Line != null ? new Vec3d(a.Line.Wx - O.X, a.Base.Y, a.Line.Wz - O.Z) : a.Base;
        Vec3d A = q.Rotate(new Vec3d(an.X, an.Y, an.Z)) + shipPos;
        Vec3d up = q.Rotate(new Vec3d(0, 1, 0));
        // vers l'extérieur depuis son axe, du bord de la prise
        Vec3d outw = q.Rotate(new Vec3d(an.Side, 0, 0));
        a.N0 = outw * -1;                        // l'intérieur fait face à elle
        Vec3d fwd = q.Rotate(new Vec3d(0, 0, 1));
        Vec3d axis = an.Mast ? up : fwd;
        double cr = an.Mast ? 0.75 : 0.55;
        Vec3d P0 = bas, P3 = A + outw * cr;
        // largement arqué vers l'extérieur et par-dessus : un membre, pas une perche
        Vec3d P1 = P0 + up * 5 + outw * 4.5;
        Vec3d P2 = P3 + outw * 4.5 + up * 4;
        double g = a.Grow;
        const double SPLIT = 0.68;
        Vec3d side = axis.Cross(outw).Normalized();
        for (int r = 0; r < KrakenArm.Rings; r++)
        {
            double s = r / (double)(KrakenArm.Rings - 1) * g;
            if (s <= SPLIT)
            {
                double u = s / SPLIT, m = 1 - u;
                Vec3d p = P0 * (m * m * m) + P1 * (3 * m * m * u) + P2 * (3 * m * u * u) + P3 * (u * u * u);
                // vivant : une lente torsion qui s'éteint vers la prise
                double w = Math.Sin(u * 6 + t * 1.7 + a.Seed) * 0.6 * (1 - u);
                a.C[r] = p + fwd * w;
            }
            else
            {
                double phi = (s - SPLIT) / (1 - SPLIT) * Math.PI * 3;
                a.C[r] = A + outw * (Math.Cos(phi) * cr) + side * (Math.Sin(phi) * cr)
                       + axis * ((an.Mast ? 0.35 : 0.25) * phi);
            }
        }
    }

    /* LES REPÈRES LE LONG DU BRAS, transportés parallèlement pour qu'il ne se
       torde jamais : le repère part de N0, le côté tourné vers ce que le bras
       tient, et le transport le garde tourné par là à chaque courbe — c'est ce qui
       garde les ventouses d'un modéliste à l'intérieur de l'enroulement. */
    static void Frames(KrakenArm a)
    {
        const int R = KrakenArm.Rings;
        Vec3d n = a.N0;
        for (int r = 0; r < R; r++)
        {
            Vec3d tg = a.C[Math.Min(R - 1, r + 1)] - a.C[Math.Max(0, r - 1)];
            double l2 = tg.X * tg.X + tg.Y * tg.Y + tg.Z * tg.Z;
            tg = l2 < 1e-8 ? new Vec3d(0, 1, 0) : tg / Math.Sqrt(l2);
            if (r == 0)
            {
                n = a.N0;
                if (Math.Abs(n.Dot(tg)) > 0.95) n = new Vec3d(tg.Z, 0, -tg.X);
            }
            n = (n - tg * n.Dot(tg)).Normalized();
            a.T[r] = tg;
            a.N[r] = n;
        }
    }

    /* ------------------------------------------------------------ canonnade */

    /// <summary>
    /// Le segment qu'un boulet a balayé, contre les bras (une chaîne de sphères)
    /// et le manteau. La première chose qu'il rencontre est ce qu'il touche.
    /// Écrit pour les canons, qui ne sont pas encore portés.
    /// </summary>
    public bool HitShot(Vec3d p0, Vec3d p1, out double u, out KrakenArm? arm)
    {
        double best = double.PositiveInfinity;
        KrakenArm? which = null;
        bool hit = false;
        if (State != KrakenState.Absent && State != KrakenState.Dive)
        {
            double s = SegSphere(p0, p1, Pos, BodyR);
            if (s >= 0) { best = s; hit = true; }
            foreach (var a in ArmsList)
            {
                if (!a.On) continue;
                for (int r = 0; r < KrakenArm.Rings; r += 3)
                {
                    s = SegSphere(p0, p1, a.C[r], a.Rad * (1 - 0.9 * r / KrakenArm.Rings) + 0.6);
                    if (s >= 0 && s < best) { best = s; which = a; hit = true; }
                }
            }
        }
        u = best; arm = which;
        return hit;
    }

    /// <summary>Ce que coûte un coup : <paramref name="k"/> le calibre, ≈ 1 pour une pièce de plein calibre.</summary>
    public void Wound(KrakenArm? arm, double k)
    {
        if (State == KrakenState.Absent || State == KrakenState.Dive) return;
        Hp -= (arm == null ? 2 : 1) * k;
        Hits++;
        // le bras touché lâche et revient la chercher un peu plus tard
        if (arm != null) arm.Recoil = 5 + _random() * 3;
        else if (State == KrakenState.Lurk) Dist += 40;       // il recule
        if (Hp <= 0) Dive("beaten");
    }

    public void Rebase(double dx, double dz)
    {
        Pos = new Vec3d(Pos.X - dx, Pos.Y, Pos.Z - dz);
        var d = new Vec3d(dx, 0, dz);
        foreach (var a in ArmsList)
        {
            a.Base = a.Base - d;
            for (int r = 0; r < KrakenArm.Rings; r++) a.C[r] = a.C[r] - d;
        }
    }

    // où le long de p0→p1 (0..1) le segment entre d'abord dans la sphère, ou −1
    static double SegSphere(Vec3d p0, Vec3d p1, Vec3d c, double r)
    {
        double dx = p1.X - p0.X, dy = p1.Y - p0.Y, dz = p1.Z - p0.Z;
        double fx = p0.X - c.X, fy = p0.Y - c.Y, fz = p0.Z - c.Z;
        double A = dx * dx + dy * dy + dz * dz;
        if (A < 1e-12) return -1;
        double B = 2 * (fx * dx + fy * dy + fz * dz), Cc = fx * fx + fy * fy + fz * fz - r * r;
        if (Cc <= 0) return 0;
        double D = B * B - 4 * A * Cc;
        if (D < 0) return -1;
        double s = (-B - Math.Sqrt(D)) / (2 * A);
        return s >= 0 && s <= 1 ? s : -1;
    }
}
