using System;
using System.Collections.Generic;
using System.Text.Json;

namespace NavalSim.Core;

/// <summary>Les règles du serpent de mer, celles de settings.json → serpent.</summary>
public sealed class SerpentSettings
{
    public bool Enabled = true;
    public double PerHour = 3.0;           // rencontres par heure de pluie, au large
    public double MinRain = 0.15;          // la pluie qu'il lui faut (0 à 1)
    public double AwayFromShore = 1000;    // mètres de jeu de la côte, au moins
    public double MinDepth = 40;           // l'eau qu'il lui faut sous lui
    public double Length = 40;             // mètres, de la tête à la queue
    public double Girth = 0.9;             // le rayon du corps, au plus gros
    public double Circle = 180;            // le rayon où il rôde, en mètres
    public double Swim = 5;                // sa nage en rôdant, m/s
    public double Dash = 11;               // sa charge, m/s
    public double StalkMin = 15, StalkMax = 30;   // secondes à rôder avant de frapper
    public double DiveMin = 10, DiveMax = 25;     // secondes sous l'eau après un coup
    public double Rise = 7;                // la hauteur où il dresse la tête, m
    public double Hp = 10;                 // les boulets qu'il encaisse
    public double Cooldown = 600;          // secondes avant un autre
    public double MassTonnes = 12;         // pour le choc

    public static SerpentSettings FromJson(JsonElement k)
    {
        var s = new SerpentSettings();
        double D(string n, double v) => k.TryGetProperty(n, out var e) && e.ValueKind == JsonValueKind.Number ? e.GetDouble() : v;
        if (k.TryGetProperty("enabled", out var en) && (en.ValueKind == JsonValueKind.True || en.ValueKind == JsonValueKind.False))
            s.Enabled = en.GetBoolean();
        s.PerHour = D("perHour", s.PerHour); s.MinRain = D("minRain", s.MinRain);
        s.AwayFromShore = D("awayFromShore", s.AwayFromShore); s.MinDepth = D("minDepth", s.MinDepth);
        s.Length = D("length", s.Length); s.Girth = D("girth", s.Girth); s.Circle = D("circle", s.Circle);
        s.Swim = D("swim", s.Swim); s.Dash = D("dash", s.Dash);
        s.Rise = D("rise", s.Rise); s.Hp = D("hp", s.Hp); s.Cooldown = D("cooldown", s.Cooldown);
        s.MassTonnes = D("massTonnes", s.MassTonnes);
        if (k.TryGetProperty("stalk", out var st) && st.ValueKind == JsonValueKind.Array && st.GetArrayLength() == 2)
        { s.StalkMin = st[0].GetDouble(); s.StalkMax = st[1].GetDouble(); }
        if (k.TryGetProperty("dive", out var dv) && dv.ValueKind == JsonValueKind.Array && dv.GetArrayLength() == 2)
        { s.DiveMin = dv[0].GetDouble(); s.DiveMax = dv[1].GetDouble(); }
        return s;
    }
}

public enum SerpentState { Absent, Stalk, Charge, Rear, Dive, Flee }

/// <summary>Un coup : lequel, où, dans quel sens, et ce qu'il a ouvert.</summary>
public readonly record struct SerpentStrike(string Kind, Vec3d At, Vec3d Dir, double DeltaV, double Area);

/// <summary>
/// LE SERPENT DE MER — il frappe et plonge, dans la pluie.
///
/// ON L'ENTEND AVANT DE LE VOIR : un sifflement dans l'averse, puis des anneaux
/// qui crèvent la surface à deux cents mètres et tournent autour du navire. Il
/// rôde, fonce sous l'eau, DRESSE la tête et le cou à sept mètres le long du
/// bord, frappe, et replonge pour resurgir ailleurs. Trois coups : la TÊTE dans
/// le bordé (une voie d'eau à la flottaison, et le choc), la QUEUE qui balaie
/// (plus bas, plus large), la MORSURE à un mât (la blessure des mâts : trois
/// l'abattent). Les boulets le blessent ; assez blessé, il fuit, et la pluie
/// finie, il s'en va. S'il n'est pas repoussé, ses voies d'eau suivent
/// l'envahissement de toutes les autres — et le navire peut sombrer.
///
/// LE CORPS SUIT LE CHEMIN DE LA TÊTE : c'est ce qui fait un serpent et non un
/// tuyau. La tête laisse une trace ; les points du corps sont pris le long de
/// cette trace, à pas égal, si bien que chaque anneau passe exactement où la
/// tête est passée. En rôdant, une ondulation verticale fait crever la surface
/// à ses bosses — les anneaux qu'on voit dans la pluie.
///
/// Tout en repère LOCAL (origine flottante).
/// </summary>
public sealed class SeaSerpent
{
    public const int N = 48;               // points du corps
    public readonly SerpentSettings S;
    public SerpentState State { get; private set; } = SerpentState.Absent;
    /// <summary>La tête : x, z locaux, et sa hauteur ABSOLUE (y local).</summary>
    public Vec3d Head;
    public double Heading;                 // l'avant de la tête : (sin, 0, cos)
    public double Speed;
    public double Hp;
    /// <summary>Les points du corps, de la tête (0) à la queue, en repère local.</summary>
    public readonly Vec3d[] Spine = new Vec3d[N];
    /// <summary>Le rayon du corps à chaque point.</summary>
    public readonly double[] Radius = new double[N];

    public Action<Vec3d, double, double>? Splash;
    /// <summary>« heard », « seen », « charge », « wounded », « flees », « gone ».</summary>
    public Action<string>? Event;
    /// <summary>Un coup porté : « head », « tail » ou « bite » (la mâture : c'est à l'appelant de la blesser).</summary>
    public Action<SerpentStrike>? OnStrike;

    readonly Random _rng;
    readonly List<Vec3d> _trail = new();   // la trace de la tête, la plus récente en dernier
    double _timer, _cool, _stateT, _rise, _dry, _phase, _orbitDir = 1;
    bool _saidSeen, _struck;
    string _next = "head";
    Vec3d _aim;

    public SeaSerpent(SerpentSettings s, int seed = 0)
    {
        S = s;
        _rng = seed == 0 ? new Random() : new Random(seed);
        for (int i = 0; i < N; i++)
        {
            double u = (double)i / (N - 1);
            // gros au premier tiers, effilé à la queue ; le cou un peu plus mince que le corps
            Radius[i] = S.Girth * (u < 0.12 ? 0.72 + 2.3 * u : Math.Max(0.12, 1 - Math.Pow((u - 0.12) / 0.88, 1.6)));
        }
    }

    double U(double a, double b) => a + _rng.NextDouble() * (b - a);

    public void Rebase(double dx, double dz)
    {
        Head = new Vec3d(Head.X - dx, Head.Y, Head.Z - dz);
        _aim = new Vec3d(_aim.X - dx, _aim.Y, _aim.Z - dz);
        for (int i = 0; i < _trail.Count; i++) _trail[i] = new Vec3d(_trail[i].X - dx, _trail[i].Y, _trail[i].Z - dz);
        for (int i = 0; i < N; i++) Spine[i] = new Vec3d(Spine[i].X - dx, Spine[i].Y, Spine[i].Z - dz);
    }

    /// <summary>Le faire venir tout de suite (débogage) : à 250 m, rôdant.</summary>
    public bool Summon(ShipPhysics prey, Func<double, double, double>? bed)
    {
        _cool = 0;
        return Spawn(prey, bed);
    }

    bool Spawn(ShipPhysics prey, Func<double, double, double>? bed)
    {
        var p = prey.Body.Pos;
        for (int k = 0; k < 16; k++)
        {
            double a = _rng.NextDouble() * 2 * Math.PI;
            double x = p.X + Math.Sin(a) * 250, z = p.Z + Math.Cos(a) * 250;
            if (bed != null && bed(x, z) > -S.MinDepth) continue;
            Head = new Vec3d(x, -3, z);
            Heading = a + Math.PI / 2;
            _trail.Clear();
            // une trace droite derrière lui, pour qu'il ait un corps dès la première image
            for (int i = 0; i < 60; i++)
                _trail.Insert(0, new Vec3d(x - Math.Sin(Heading) * i, -3, z - Math.Cos(Heading) * i));
            Hp = S.Hp;
            Speed = S.Swim;
            _orbitDir = _rng.NextDouble() < 0.5 ? 1 : -1;
            _saidSeen = false;
            _dry = 0;
            Go(SerpentState.Stalk);
            Event?.Invoke("heard");
            return true;
        }
        return false;
    }

    void Go(SerpentState s)
    {
        State = s;
        _stateT = 0;
        _timer = s switch
        {
            SerpentState.Stalk => U(S.StalkMin, S.StalkMax),
            SerpentState.Dive => U(S.DiveMin, S.DiveMax),
            _ => 60
        };
        _struck = false;
    }

    /// <summary>Un boulet l'a touché : vrai s'il s'en va.</summary>
    public bool Wound(double k)
    {
        if (State is SerpentState.Absent or SerpentState.Flee) return false;
        Hp -= Math.Max(0.5, k);
        if (Hp <= 0) { Go(SerpentState.Flee); Event?.Invoke("flees"); return true; }
        Event?.Invoke("wounded");
        // touché, il plonge et reprend ailleurs
        if (State is SerpentState.Stalk or SerpentState.Charge) Go(SerpentState.Dive);
        return false;
    }

    /// <summary>
    /// Une image. <paramref name="wet"/> : la pluie qui tombe (0 à 1) ;
    /// <paramref name="bed"/>, <paramref name="shore"/> : le fond et la côte en
    /// coordonnées LOCALES.
    /// </summary>
    public void Update(double dt, ShipPhysics prey, Ocean sea, double t, double wet,
                       Func<double, double, double>? bed, Func<double, double, double>? shore)
    {
        if (!S.Enabled) return;
        var ship = prey.Body.Pos;
        if (State == SerpentState.Absent)
        {
            if (_cool > 0) { _cool -= dt; return; }
            if (prey.Foundered || wet < S.MinRain) return;
            if (shore != null && shore(ship.X, ship.Z) < S.AwayFromShore) return;
            if (bed != null && bed(ship.X, ship.Z) > -S.MinDepth) return;
            if (_rng.NextDouble() < S.PerHour / 3600 * dt) Spawn(prey, bed);
            return;
        }

        _stateT += dt;
        _timer -= dt;
        _phase += dt;
        double dx = ship.X - Head.X, dz = ship.Z - Head.Z;
        double dist = Math.Sqrt(dx * dx + dz * dz);
        double toShip = Math.Atan2(dx, dz);
        double want = Heading, wantV = S.Swim, wantY = -1.2, turn = 0.9;
        double surf = sea.Sample(Head.X, Head.Z, t);

        // la pluie finie, il ne reste pas : trente secondes de sec et il s'en va
        _dry = wet < S.MinRain * 0.5 ? _dry + dt : 0;
        if (_dry > 30 && State != SerpentState.Flee) { Go(SerpentState.Flee); Event?.Invoke("flees"); }
        if (!_saidSeen && _stateT > 6 && State == SerpentState.Stalk) { _saidSeen = true; Event?.Invoke("seen"); }

        switch (State)
        {
            case SerpentState.Stalk:
            {
                /* IL RÔDE : sur un cercle autour du navire, ses bosses crevant la
                   surface. On vise le point du cercle un peu en avant de lui. */
                double a = Math.Atan2(Head.X - ship.X, Head.Z - ship.Z) + _orbitDir * 0.35;
                double tx = ship.X + Math.Sin(a) * S.Circle, tz = ship.Z + Math.Cos(a) * S.Circle;
                want = Math.Atan2(tx - Head.X, tz - Head.Z);
                wantY = surf - 0.5;
                if (_timer <= 0)
                {
                    Go(SerpentState.Charge);
                    // le coup, tiré à l'avance : la tête deux fois sur quatre
                    double r = _rng.NextDouble();
                    _next = r < 0.5 ? "head" : r < 0.75 ? "tail" : "bite";
                    Event?.Invoke("charge");
                }
                break;
            }
            case SerpentState.Charge:
            {
                /* IL FONCE SOUS L'EAU vers un point à côté du navire, par le travers :
                   il y dressera la tête. Le point suit le navire. */
                var b = prey.Body;
                var right = b.Quat.Rotate(new Vec3d(-1, 0, 0));      // tribord : −x local
                double side = Math.Sign((Head - ship).Dot(right));
                if (side == 0) side = 1;
                double off = prey.Spec.B * 0.5 + 5;
                _aim = ship + right * (side * off);
                want = Math.Atan2(_aim.X - Head.X, _aim.Z - Head.Z);
                wantV = S.Dash;
                // sous la quille quand il passe près : à 2,5 m il traversait le bordé d'une frégate (tirant 2,9)
                wantY = surf - (dist < 30 ? prey.Draft + 2.5 : 2.5);
                turn = 1.4;
                double da = Math.Sqrt((_aim.X - Head.X) * (_aim.X - Head.X) + (_aim.Z - Head.Z) * (_aim.Z - Head.Z));
                if (da < 8) { Go(SerpentState.Rear); _rise = 0; }
                if (_stateT > 40) Go(SerpentState.Dive);
                break;
            }
            case SerpentState.Rear:
            {
                /* IL SE DRESSE : la tête monte hors de l'eau, tournée vers le bord, et
                   frappe au sommet de sa course. Une gerbe quand elle crève. */
                wantV = 0.5;
                want = toShip;
                turn = 2.5;
                if (_rise == 0) Splash?.Invoke(new Vec3d(Head.X, surf, Head.Z), 14, 6);
                _rise = Math.Min(1, _rise + dt / 1.2);
                wantY = surf + S.Rise * _rise;
                if (_rise >= 1 && !_struck) { _struck = true; Strike(prey, sea, t); }
                if (_rise >= 1 && _stateT > 2.4) Go(SerpentState.Dive);
                break;
            }
            case SerpentState.Dive:
            {
                // il replonge et s'écarte, puis reprend sa ronde ailleurs
                want = toShip + Math.PI + _orbitDir * 0.6;
                wantV = S.Swim * 1.4;
                wantY = surf - 8;
                if (_timer <= 0) { _orbitDir = -_orbitDir; Go(SerpentState.Stalk); }
                break;
            }
            case SerpentState.Flee:
            {
                want = toShip + Math.PI;
                wantV = S.Dash * 0.8;
                wantY = surf - 14;
                if (dist > 1500 || _stateT > 60)
                {
                    State = SerpentState.Absent;
                    _cool = S.Cooldown;
                    Event?.Invoke("gone");
                    return;
                }
                break;
            }
        }

        // le fond : il ne va pas s'échouer — il vire vers le plus profond
        if (bed != null && State is SerpentState.Stalk or SerpentState.Dive or SerpentState.Flee)
        {
            double ax = Head.X + Math.Sin(Heading) * 60, az = Head.Z + Math.Cos(Heading) * 60;
            if (bed(ax, az) > -12) { want = Heading + 1.4; turn = Math.Max(turn, 1.6); }
            wantY = Math.Max(wantY, bed(Head.X, Head.Z) + 3);
        }

        double dh = Math.IEEERemainder(want - Heading, 2 * Math.PI);
        Heading = Math.IEEERemainder(Heading + Math.Clamp(dh, -turn * dt, turn * dt), 2 * Math.PI);
        Speed += Math.Clamp(wantV - Speed, -6 * dt, 4 * dt);
        double vy = Math.Clamp((wantY - Head.Y) * 1.5, -6, 9);
        Head = new Vec3d(Head.X + Math.Sin(Heading) * Speed * dt, Head.Y + vy * dt, Head.Z + Math.Cos(Heading) * Speed * dt);

        // la trace : un point tous les quarante centimètres, assez pour un corps de quarante mètres
        if (_trail.Count == 0 || (_trail[^1] - Head).Length > 0.4) _trail.Add(Head);
        int keep = (int)(S.Length / 0.4) + 20;
        if (_trail.Count > keep) _trail.RemoveRange(0, _trail.Count - keep);
        Body(sea, t);
    }

    /* LE CORPS : les points pris le long de la trace, à pas égal depuis la tête.
       En rôdant, une onde verticale fait sortir ses bosses ; dressé, le cou
       descend de la tête à l'eau sur ses douze premiers mètres. */
    void Body(Ocean sea, double t)
    {
        double step = S.Length / (N - 1);
        // la trace à rebours depuis la tête, et un point tous les « step » mètres le long d'elle
        var pts = new Vec3d[N];
        pts[0] = Head;
        int i = 1;
        double c = 0;
        var a = Head;
        for (int j = _trail.Count - 1; j >= 0 && i < N; j--)
        {
            var q = _trail[j];
            double seg = (q - a).Length;
            if (seg < 1e-6) continue;
            while (i < N && i * step <= c + seg)
            {
                pts[i] = a + (q - a) * ((i * step - c) / seg);
                i++;
            }
            c += seg;
            a = q;
        }
        // une trace trop courte : le reste du corps droit derrière
        for (; i < N; i++)
        {
            double more = i * step - c;
            pts[i] = new Vec3d(a.X - Math.Sin(Heading) * more, a.Y, a.Z - Math.Cos(Heading) * more);
        }
        Spine[0] = Head;
        for (i = 1; i < N; i++)
        {
            var p = pts[i];
            double s = i * step;
            double surf = sea.Sample(p.X, p.Z, t);
            double y = p.Y;
            if (State == SerpentState.Stalk)
                y = surf - 0.55 + 0.75 * Math.Sin(s / 7.5 - _phase * 2.6);      // les anneaux qui crèvent
            else if (State == SerpentState.Rear && s < 12)
                y = surf + (Head.Y - surf) * (1 - s / 12) * (1 - s / 12);        // le cou dressé
            else
                y = Math.Min(y, surf - 0.4);
            Spine[i] = new Vec3d(p.X, y, p.Z);
        }
    }

    /// <summary>Le coup, au sommet de la course : impulsion et voie d'eau, ou la mâture.</summary>
    void Strike(ShipPhysics prey, Ocean sea, double t)
    {
        var b = prey.Body;
        var sp = prey.Spec;
        var toHull = new Vec3d(b.Pos.X - Head.X, 0, b.Pos.Z - Head.Z).Normalized();
        if (_next == "bite")
        {
            OnStrike?.Invoke(new SerpentStrike("bite", Head, toHull, 0, 0));
            return;
        }
        /* LE CHOC : douze tonnes lancées à quelques mètres par seconde, partagées
           entre les deux masses, au point du bordé qu'il touche — la tête à la
           flottaison, la queue plus bas et plus large. */
        double v = _next == "tail" ? 7 : 5;
        double mw = S.MassTonnes * 1000, ms = b.Mass;
        double J = 1.2 * v / (1 / mw + 1 / ms);
        var at = b.Pos + toHull * -(sp.B * 0.5);
        at = new Vec3d(at.X, sea.Sample(at.X, at.Z, t) + (_next == "tail" ? -1.0 : 0.3), at.Z);
        var imp = toHull * J;
        b.Vel += imp * (1 / ms);
        var r = at - (b.Pos + b.Quat.Rotate(b.Com));
        var tb = b.Quat.Inverted().Rotate(r.Cross(imp));
        b.AngVel += b.Quat.Rotate(new Vec3d(tb.X / b.Ib.X, tb.Y / b.Ib.Y, tb.Z / b.Ib.Z));
        var local = b.Quat.Inverted().Rotate(at - b.Pos);
        int comp = Math.Clamp((int)Math.Floor((local.Z + sp.L * 0.5) / sp.L * Config.NComp), 0, Config.NComp - 1);
        double area = _next == "tail" ? U(0.18, 0.32) : U(0.10, 0.22);
        prey.MakeBreach(comp, area, _next == "tail" ? 0.2 : 0.45);
        OnStrike?.Invoke(new SerpentStrike(_next, at, toHull, J / ms, area));
    }

    /// <summary>Un boulet entre a et b : touche-t-il le corps ? u : où, sur le segment.</summary>
    public bool HitShot(Vec3d a, Vec3d b, out double u)
    {
        u = 0;
        if (State is SerpentState.Absent) return false;
        var d = b - a;
        double dd = Math.Max(1e-9, d.Dot(d));
        for (int i = 0; i < N; i += 2)
        {
            double k = Math.Clamp((Spine[i] - a).Dot(d) / dd, 0, 1);
            var p = a + d * k;
            double r = Radius[i] + 0.35;
            if ((p - Spine[i]).Dot(p - Spine[i]) < r * r) { u = k; return true; }
        }
        return false;
    }
}
