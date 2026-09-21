using System;
using System.Text.Json;

namespace NavalSim.Core;

/// <summary>Les règles de la baleine, celles de settings.json → whale.</summary>
public sealed class WhaleSettings
{
    public bool Enabled = true;
    public double PerHour = 2.0;            // rencontres par heure de jeu, au large
    public double AwayFromShore = 1000;     // mètres de jeu de la côte, au moins
    public double MinDepth = 60;            // l'eau qu'il lui faut sous elle
    public double SightNear = 700, SightFar = 1400;   // où on l'aperçoit, en mètres
    public double Cruise = 2.0;             // sa marche, en m/s (quatre nœuds)
    public double SurfaceMin = 35, SurfaceMax = 60;   // secondes à respirer en surface
    public double SpoutMin = 7, SpoutMax = 12;        // secondes entre deux souffles
    public double DiveMin = 45, DiveMax = 90;         // secondes sous l'eau
    public double DiveDepth = 25;           // mètres
    public double Curious = 0.45;           // la part des rencontres où elle vient voir
    public double Hostile = 0.20;           // la part où elle charge
    public double Charge = 6.0;             // sa vitesse de charge, m/s (douze nœuds)
    public double RamAgain = 0.5;           // la chance qu'elle revienne frapper
    public double MassTonnes = 40;
    public double Length = 15;
    public double WhiteChance = 0.04;       // la baleine blanche
    public double Cooldown = 480;           // secondes avant une autre
    public string? Glb = "creatures/whale.glb";

    public static WhaleSettings FromJson(JsonElement k)
    {
        var s = new WhaleSettings();
        double D(string n, double v) => k.TryGetProperty(n, out var e) && e.ValueKind == JsonValueKind.Number ? e.GetDouble() : v;
        if (k.TryGetProperty("enabled", out var en) && (en.ValueKind == JsonValueKind.True || en.ValueKind == JsonValueKind.False))
            s.Enabled = en.GetBoolean();
        s.PerHour = D("perHour", s.PerHour); s.AwayFromShore = D("awayFromShore", s.AwayFromShore);
        s.MinDepth = D("minDepth", s.MinDepth);
        if (k.TryGetProperty("sight", out var sg) && sg.ValueKind == JsonValueKind.Array && sg.GetArrayLength() == 2)
        { s.SightNear = sg[0].GetDouble(); s.SightFar = sg[1].GetDouble(); }
        if (k.TryGetProperty("surface", out var su) && su.ValueKind == JsonValueKind.Array && su.GetArrayLength() == 2)
        { s.SurfaceMin = su[0].GetDouble(); s.SurfaceMax = su[1].GetDouble(); }
        if (k.TryGetProperty("spout", out var sp) && sp.ValueKind == JsonValueKind.Array && sp.GetArrayLength() == 2)
        { s.SpoutMin = sp[0].GetDouble(); s.SpoutMax = sp[1].GetDouble(); }
        if (k.TryGetProperty("dive", out var dv) && dv.ValueKind == JsonValueKind.Array && dv.GetArrayLength() == 2)
        { s.DiveMin = dv[0].GetDouble(); s.DiveMax = dv[1].GetDouble(); }
        s.Cruise = D("cruise", s.Cruise); s.DiveDepth = D("diveDepth", s.DiveDepth);
        s.Curious = D("curious", s.Curious); s.Hostile = D("hostile", s.Hostile);
        s.Charge = D("charge", s.Charge); s.RamAgain = D("ramAgain", s.RamAgain);
        s.MassTonnes = D("massTonnes", s.MassTonnes); s.Length = D("length", s.Length);
        s.WhiteChance = D("whiteChance", s.WhiteChance); s.Cooldown = D("cooldown", s.Cooldown);
        if (k.TryGetProperty("glb", out var g) && g.ValueKind == JsonValueKind.String) s.Glb = g.GetString();
        return s;
    }
}

public enum WhaleState { Absent, Surface, Dive, Under, Charge, Away }
public enum WhaleMood { Indifferent, Curious, Hostile }

/// <summary>Ce que le coup de boutoir a fait : où, dans quel sens, et combien.</summary>
public readonly record struct WhaleRam(Vec3d At, Vec3d Dir, double Speed, double DeltaV, int Comp, double Area);

/// <summary>
/// LA BALEINE — un cachalot, dans l'esprit de Moby Dick.
///
/// On la voit d'abord au loin, à son SOUFFLE : une gerbe de brume de quatre ou
/// cinq mètres, et celle d'un cachalot part en avant et à gauche, son évent
/// étant à gauche du bout de la tête — le seul souffle qu'un baleinier
/// reconnaissait à la vue. Elle respire en surface une minute, puis sonde, la
/// queue haute, pour une ou deux minutes (vingt vraies, qu'on abrège).
///
/// Selon son HUMEUR, tirée à la rencontre : elle passe son chemin ; ou elle
/// vient voir, et passe SOUS la quille — une ombre sous l'eau, qui ne touche
/// rien ; ou elle charge. L'Essex a été frappé deux fois, en 1820, par un
/// cachalot qui l'a défoncé à l'avant ; elle frappe à douze nœuds, de la tête,
/// et peut revenir.
///
/// LE COUP EST UNE IMPULSION, pas un réglage : quarante tonnes lancées contre
/// la coque, le choc réparti entre les deux masses (restitution faible — de la
/// chair et du chêne), appliqué au point touché, donc il pousse ET fait tourner
/// et gîter. Et une voie d'eau dans le compartiment touché, sous la flottaison,
/// d'autant plus large qu'elle frappait vite. Ce qui suit — l'envahissement,
/// les pompes, le naufrage — est celui de tous les autres trous.
///
/// Tout est en repère LOCAL (origine flottante) ; la profondeur <see cref="Y"/>
/// est celle de son axe sous la surface, en mètres positifs.
/// </summary>
public sealed class Whale
{
    public readonly WhaleSettings S;
    public WhaleState State { get; private set; } = WhaleState.Absent;
    public WhaleMood Mood { get; private set; }
    public bool White { get; private set; }
    public Vec3d Pos;                 // x, z locaux ; y inutilisé
    public double Y;                  // profondeur de l'axe, m
    public double Heading;            // lacet : l'avant est (sin, 0, cos)
    public double Speed, Pitch;
    public int Rams { get; private set; }

    /// <summary>Un souffle : l'évent (local, surface comprise) et sa direction.</summary>
    public Action<Vec3d, Vec3d>? Spout;
    /// <summary>Ce qu'elle jette en crevant la surface : où, combien d'eau, à quelle vitesse.</summary>
    public Action<Vec3d, double, double>? Splash;
    /// <summary>Un moment à dire : « seen », « sounds », « under », « charge », « outrun », « gone ».</summary>
    public Action<string>? Event;
    /// <summary>Le coup de boutoir, une fois appliqué à la coque.</summary>
    public Action<WhaleRam>? OnRam;

    readonly Random _rng;
    double _timer, _spoutIn, _cool, _stateT;
    int _cycles;
    bool _passed, _saidUnder, _saidCharge;
    double _closest = 1e9;

    public Whale(WhaleSettings s, int seed = 0)
    {
        S = s;
        _rng = seed == 0 ? new Random() : new Random(seed);
    }

    double U(double a, double b) => a + _rng.NextDouble() * (b - a);
    Vec3d Fwd => new(Math.Sin(Heading) * Math.Cos(Pitch), Math.Sin(Pitch), Math.Cos(Heading) * Math.Cos(Pitch));

    /// <summary>La pointe de la tête, en local, surface comprise — c'est elle qui frappe.</summary>
    public Vec3d Nose(double surface) =>
        new Vec3d(Pos.X, surface - Y, Pos.Z) + Fwd * (S.Length * 0.47);

    public void Rebase(double dx, double dz) => Pos = new Vec3d(Pos.X - dx, 0, Pos.Z - dz);

    /// <summary>
    /// La faire venir tout de suite (débogage, ⇧K) : à <paramref name="dist"/>
    /// mètres, dans l'humeur donnée.
    /// </summary>
    public bool Summon(ShipPhysics prey, WhaleMood mood, double dist, Func<double, double, double>? bed)
    {
        _cool = 0;
        return Spawn(prey, mood, dist, bed);
    }

    bool Spawn(ShipPhysics prey, WhaleMood mood, double dist, Func<double, double, double>? bed)
    {
        var p = prey.Body.Pos;
        for (int k = 0; k < 16; k++)
        {
            double a = _rng.NextDouble() * 2 * Math.PI;
            double x = p.X + Math.Sin(a) * dist, z = p.Z + Math.Cos(a) * dist;
            if (bed != null && bed(x, z) > -S.MinDepth) continue;
            Pos = new Vec3d(x, 0, z);
            // elle croise la route de l'œil plutôt qu'elle ne vient droit dessus
            double toShip = Math.Atan2(p.X - x, p.Z - z);
            Heading = toShip + (_rng.NextDouble() < 0.5 ? 1 : -1) * U(1.0, 1.6);
            Mood = mood;
            White = _rng.NextDouble() < S.WhiteChance;
            Speed = S.Cruise;
            Y = 0.9; Pitch = 0;
            Rams = 0; _cycles = 0;
            _passed = _saidUnder = _saidCharge = false;
            _closest = 1e9;
            Go(WhaleState.Surface);
            _spoutIn = 1.0;
            Event?.Invoke("seen");
            return true;
        }
        return false;
    }

    WhaleMood Roll()
    {
        double r = _rng.NextDouble();
        return r < S.Hostile ? WhaleMood.Hostile : r < S.Hostile + S.Curious ? WhaleMood.Curious : WhaleMood.Indifferent;
    }

    void Go(WhaleState s)
    {
        State = s;
        _stateT = 0;
        _timer = s switch
        {
            WhaleState.Surface => U(S.SurfaceMin, S.SurfaceMax),
            WhaleState.Dive => U(S.DiveMin, S.DiveMax),
            WhaleState.Away => 1e9,
            _ => 180
        };
    }

    /// <summary>
    /// Une image. <paramref name="bed"/> et <paramref name="shore"/> lisent le
    /// monde en coordonnées LOCALES (hauteur du fond ; distance à la côte).
    /// </summary>
    public void Update(double dt, ShipPhysics prey, Ocean sea, double t,
                       Func<double, double, double>? bed, Func<double, double, double>? shore)
    {
        if (!S.Enabled) return;
        var ship = prey.Body.Pos;

        if (State == WhaleState.Absent)
        {
            if (_cool > 0) { _cool -= dt; return; }
            if (prey.Foundered) return;
            if (shore != null && shore(ship.X, ship.Z) < S.AwayFromShore) return;
            if (_rng.NextDouble() < S.PerHour / 3600 * dt) Spawn(prey, Roll(), U(S.SightNear, S.SightFar), bed);
            return;
        }

        _stateT += dt;
        _timer -= dt;
        double dx = ship.X - Pos.X, dz = ship.Z - Pos.Z;
        double dist = Math.Sqrt(dx * dx + dz * dz);
        double toShip = Math.Atan2(dx, dz);
        double want = Heading, wantV = S.Cruise, wantY = 0.9, turn = 0.10;

        switch (State)
        {
            case WhaleState.Surface:
                if ((_spoutIn -= dt) <= 0)
                {
                    _spoutIn = U(S.SpoutMin, S.SpoutMax);
                    double surf = sea.Sample(Pos.X, Pos.Z, t);
                    // l'évent : au bout de la tête, sur sa GAUCHE (+x du cachalot)
                    var right = new Vec3d(Math.Cos(Heading), 0, -Math.Sin(Heading));
                    var hole = new Vec3d(Pos.X, surf - Y + 1.3, Pos.Z) + Fwd * (S.Length * 0.42) - right * 0.45;
                    var dir = (Fwd * 0.55 - right * 0.45 + new Vec3d(0, 0.75, 0)).Normalized();
                    Spout?.Invoke(hole, dir);
                }
                if (_timer <= 0)
                {
                    if (Mood == WhaleMood.Hostile && dist < 1400 && Rams < 2) { Go(WhaleState.Charge); _saidCharge = false; _closest = 1e9; }
                    else if (Mood == WhaleMood.Curious && !_passed && dist < 1200) Go(WhaleState.Under);
                    else if (_cycles >= 2 || _passed || Rams > 0) Go(WhaleState.Away);
                    else { Go(WhaleState.Dive); if (_cycles == 0) Event?.Invoke("sounds"); }
                }
                break;

            case WhaleState.Dive:
                wantY = S.DiveDepth;
                wantV = S.Cruise * 1.1;
                if (_timer <= 0) { _cycles++; Go(WhaleState.Surface); _spoutIn = 0.5; }
                break;

            case WhaleState.Under:
            {
                /* VENIR VOIR : sous la quille, pas contre elle. Elle vise le point
                   où sera le navire, passe à quatre mètres sous son fond, et file
                   au-delà — on la voit passer, sombre, à travers l'eau. */
                // son dos à deux mètres sous la quille : assez près pour qu'on la voie
                double keel = prey.Draft + 2 + 1.6;
                wantY = dist < 120 ? Math.Max(4, keel) : 12;
                wantV = 4.0;
                var v = prey.Body.Vel;
                double lead = Math.Min(20, dist / 4.0);
                double lx = ship.X + v.X * lead - Pos.X, lz = ship.Z + v.Z * lead - Pos.Z;
                /* Dans les soixante derniers mètres elle TIENT son cap : à force de
                   corriger, elle tournait autour de la coque sans jamais passer
                   dessous (relevé au labo : 20 m au plus près, quatre minutes). */
                if (!_passed && dist > 60) want = Math.Atan2(lx, lz);
                if (dist < 40 && !_saidUnder) { _saidUnder = true; Event?.Invoke("under"); }
                _closest = Math.Min(_closest, dist);
                if (_closest < 60 && dist > _closest + 20) _passed = true;
                if (_passed && dist > 250) Go(WhaleState.Surface);
                if (_stateT > 240) { _passed = true; Go(WhaleState.Away); }
                break;
            }

            case WhaleState.Charge:
            {
                /* LA CHARGE, en surface — la tête hors de l'eau, une vague devant
                   elle. Elle vise un peu devant le navire, comme un chasseur. */
                wantY = 0.6;
                // l'élan final : un cachalot pousse à quinze nœuds sur ses derniers mètres
                wantV = dist < 150 ? S.Charge * 1.25 : S.Charge;
                turn = 0.22;
                var v = prey.Body.Vel;
                double lead = Math.Min(15, dist / Math.Max(1, S.Charge));
                want = Math.Atan2(ship.X + v.X * lead - Pos.X, ship.Z + v.Z * lead - Pos.Z);
                if (!_saidCharge && dist < 450) { _saidCharge = true; Event?.Invoke("charge"); }
                /* ELLE RENONCE si l'on file : quand la distance a CRÛ de cent mètres
                   depuis le plus près qu'elle ait été, ou après cinq minutes. Un
                   délai fixe coupait des charges qui gagnaient encore du terrain. */
                _closest = Math.Min(_closest, dist);
                if (_stateT > 300 || dist > _closest + 100)
                {
                    Event?.Invoke("outrun");
                    Go(WhaleState.Away);
                    break;
                }
                if (TryRam(prey, sea, t)) break;
                break;
            }

            case WhaleState.Away:
            {
                want = toShip + Math.PI;
                wantV = 2.6;
                // un revenant, parfois : elle s'éloigne, se retourne et recharge
                if (Mood == WhaleMood.Hostile && Rams == 1 && _stateT > 45 && dist > 250)
                {
                    if (_rng.NextDouble() < S.RamAgain) { Go(WhaleState.Charge); _saidCharge = false; _closest = 1e9; break; }
                    Mood = WhaleMood.Indifferent;
                }
                wantY = (_stateT % 90) < 40 ? 0.9 : S.DiveDepth * 0.6;
                if (dist > 3000)
                {
                    State = WhaleState.Absent;
                    _cool = S.Cooldown;
                    Event?.Invoke("gone");
                }
                break;
            }
        }

        /* LE FOND : elle ne se jette pas à la côte. Si l'eau devant est courte,
           elle vire du côté où il y en a plus. */
        if (bed != null)
        {
            double ax = Pos.X + Math.Sin(Heading) * 150, az = Pos.Z + Math.Cos(Heading) * 150;
            /* Quand elle poursuit le navire — pour le frapper ou passer dessous —,
               elle le suit sur le plateau et n'évite que ce qui la ferait toucher :
               le seuil de 36 m la faisait se détourner à chaque approche d'un
               navire mouillé au bord du tombant (relevé : 450 → 531 m). */
            double need = State is WhaleState.Charge or WhaleState.Under ? 6 : S.MinDepth * 0.6;
            if (bed(ax, az) > -need && State is WhaleState.Charge or WhaleState.Under)
            {
                /* Le navire est sur des hauts-fonds : elle ne l'y suivra pas. Elle
                   tournait au bord à le guetter, et l'on croyait l'avoir distancée. */
                if (State == WhaleState.Charge) Event?.Invoke("shoal");
                _passed = true;
                Go(WhaleState.Away);
            }
            else if (bed(ax, az) > -need)
            {
                double l = bed(Pos.X + Math.Sin(Heading + 0.8) * 150, Pos.Z + Math.Cos(Heading + 0.8) * 150);
                double r = bed(Pos.X + Math.Sin(Heading - 0.8) * 150, Pos.Z + Math.Cos(Heading - 0.8) * 150);
                want = Heading + (l < r ? 1.2 : -1.2);
                turn = Math.Max(turn, 0.25);
            }
            // et jamais plus bas que le fond
            double floor = -bed(Pos.X, Pos.Z) - 3;
            wantY = Math.Min(wantY, Math.Max(0.6, floor));
        }

        // virer, accélérer, monter ou descendre — tout à des allures de baleine
        /* L'ÉCART DE CAP, ramené dans ±π par un reste IEEE : le « % » de C# garde
           le signe du dividende, et passé un tour le virage partait à l'envers —
           la baleine chargeait en s'éloignant (relevé : 450 → 536 m, navire stoppé). */
        double dh = Math.IEEERemainder(want - Heading, 2 * Math.PI);
        Heading = Math.IEEERemainder(Heading + Math.Clamp(dh, -turn * dt, turn * dt), 2 * Math.PI);
        Speed += Math.Clamp(wantV - Speed, -0.6 * dt, 0.5 * dt);
        double vy = Math.Clamp((wantY - Y) * 0.4, -1.4, 1.4);
        Y = Math.Max(0.4, Y + vy * dt);
        // le nez suit la pente de sa route : piquer, c'est la queue qui sort
        double wantPitch = Math.Atan2(-vy, Math.Max(0.5, Speed));
        Pitch += (wantPitch - Pitch) * Math.Min(1, 1.5 * dt);

        double wasY = Y;
        Pos = new Vec3d(Pos.X + Math.Sin(Heading) * Speed * dt, 0, Pos.Z + Math.Cos(Heading) * Speed * dt);
        // crever la surface en remontant : un bouillon blanc
        if (vy < -0.6 && wasY < 1.5 && _stateT > 1 && _rng.NextDouble() < dt * 2)
            Splash?.Invoke(new Vec3d(Pos.X, sea.Sample(Pos.X, Pos.Z, t), Pos.Z), 6, 2.5);
        if (State == WhaleState.Charge && Y < 1.0 && _rng.NextDouble() < dt * 3)
            Splash?.Invoke(new Vec3d(Pos.X, sea.Sample(Pos.X, Pos.Z, t), Pos.Z) + Fwd * (S.Length * 0.5), 3 + Speed, Speed);
    }

    /// <summary>
    /// LE COUP DE BOUTOIR : la tête dans la coque ? Le repère du navire dit si
    /// elle y est — dans sa largeur, sa longueur, entre sa quille et son pont.
    /// </summary>
    bool TryRam(ShipPhysics prey, Ocean sea, double t)
    {
        var b = prey.Body;
        var sp = prey.Spec;
        double surf = sea.Sample(Pos.X, Pos.Z, t);
        var nose = Nose(surf);
        var local = b.Quat.Inverted().Rotate(nose - b.Pos);
        double halfL = sp.L * 0.5;
        if (Math.Abs(local.Z) > halfL) return false;
        // l'avant et l'arrière s'affinent : une coque en fuseau plutôt qu'une boîte
        double u = local.Z / halfL;
        double half = sp.B * 0.5 * Math.Max(0.25, 1 - 0.6 * u * u) + 0.4;
        if (Math.Abs(local.X) > half || local.Y < -sp.D - 0.5 || local.Y > 3) return false;

        var dir = new Vec3d(Math.Sin(Heading), 0, Math.Cos(Heading));
        var vShip = b.Vel + b.AngVel.Cross(nose - b.Pos);
        double vRel = (dir * Speed - vShip).Dot(dir);
        if (vRel < 0.8) return false;                       // un frôlement

        /* L'IMPULSION, partagée entre les deux masses, restitution 0,1 — de la
           chair contre du chêne. L'inertie de rotation du navire est ignorée
           dans le partage (elle ne ferait que l'adoucir d'un peu). */
        double mw = S.MassTonnes * 1000, ms = b.Mass;
        double J = 1.1 * vRel / (1 / mw + 1 / ms);
        var imp = dir * J;
        b.Vel += imp * (1 / ms);
        // le moment du choc, au point touché : il fait virer et gîter
        var r = nose - (b.Pos + b.Quat.Rotate(b.Com));
        var tb = b.Quat.Inverted().Rotate(r.Cross(imp));
        var wb = new Vec3d(tb.X / b.Ib.X, tb.Y / b.Ib.Y, tb.Z / b.Ib.Z);
        b.AngVel += b.Quat.Rotate(wb);
        Speed = Math.Max(0, Speed - J / mw);

        // la voie d'eau : dans le compartiment touché, bas, d'autant plus large qu'elle allait vite
        int comp = Math.Clamp((int)Math.Floor((local.Z + halfL) / sp.L * Config.NComp), 0, Config.NComp - 1);
        double area = Math.Clamp(0.05 * vRel, 0.08, 0.5);
        prey.MakeBreach(comp, area, 0.18);
        Rams++;
        OnRam?.Invoke(new WhaleRam(nose, dir, vRel, J / ms, comp, area));
        // sonnée, elle plonge et s'écarte
        Go(WhaleState.Away);
        Y = 3;
        return true;
    }
}
