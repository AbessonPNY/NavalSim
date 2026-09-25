using System;
using System.Collections.Generic;
using System.Text.Json;

namespace NavalSim.Core;

/// <summary>
/// LES DAUPHINS — la règle, portée de <c>js/dolphins.js</c>, réglages compris
/// (settings.json → dolphins, le MÊME bloc que la page lit : clés en anglais).
/// </summary>
public sealed class DolphinRules
{
    public bool Enabled = true;
    /// <summary>Bandes par journée de jeu, en moyenne, tant que la mer le permet.</summary>
    public double PerDay = 4;
    /// <summary>L'état de mer au-dessus duquel ils ne viennent pas — et s'en vont.</summary>
    public double CalmBelow = 3.5;
    public int PodMin = 3, PodMax = 7;
    /// <summary>Combien de temps ils restent, en minutes de jeu.</summary>
    public double MinutesMin = 3, MinutesMax = 8;
    /// <summary>Pas dans un port : il leur faut de l'eau libre.</summary>
    public double AwayFromShore = 250;
    public string? Glb;

    public static DolphinRules FromJson(JsonElement k)
    {
        var d = new DolphinRules();
        double D(string n, double v) => k.TryGetProperty(n, out var e) && e.ValueKind == JsonValueKind.Number ? e.GetDouble() : v;
        if (k.TryGetProperty("enabled", out var on) && (on.ValueKind == JsonValueKind.False || on.ValueKind == JsonValueKind.True))
            d.Enabled = on.GetBoolean();
        d.PerDay = Math.Max(0, D("perDay", d.PerDay));
        d.CalmBelow = D("calmBelow", d.CalmBelow);
        d.AwayFromShore = Math.Max(0, D("awayFromShore", d.AwayFromShore));
        if (k.TryGetProperty("pod", out var p) && p.ValueKind == JsonValueKind.Array && p.GetArrayLength() >= 2)
        { d.PodMin = p[0].GetInt32(); d.PodMax = p[1].GetInt32(); }
        if (k.TryGetProperty("minutes", out var m) && m.ValueKind == JsonValueKind.Array && m.GetArrayLength() >= 2)
        { d.MinutesMin = m[0].GetDouble(); d.MinutesMax = m[1].GetDouble(); }
        if (k.TryGetProperty("glb", out var g) && g.ValueKind == JsonValueKind.String) d.Glb = g.GetString();
        d.PodMin = Math.Clamp(d.PodMin, 1, Dolphins.Max);
        d.PodMax = Math.Clamp(d.PodMax, d.PodMin, Dolphins.Max);
        return d;
    }
}

/// <summary>Un animal, et ce qu'il faut pour le poser.</summary>
public sealed class Dolphin
{
    public bool On;
    public Vec3d Pos, Vel;
    public double Y = -2, Vy;
    public double Phase, Rate = 0.3, Leap = 1, Seed, Scale = 1;
    public int Slot, Side;
    public bool WasUp;
    /// <summary>Ce que le dessin en fait : son cap, son assiette, l'angle de sa queue.</summary>
    public double Yaw, Pitch, Tail;
}

/// <summary>
/// UNE BANDE DE DAUPHINS, de temps en temps, quand la mer est tranquille : trois
/// à sept animaux qui trouvent le navire, prennent sa lame d'étrave s'il a de
/// l'erre — ils le font, pour la poussée gratuite de l'onde de pression — ou
/// tournent autour de lui s'il est stoppé, puis restent en arrière et s'en vont.
///
/// CE QUE L'ŒIL LIT, C'EST L'ARC : la plupart du temps un dauphin est une forme
/// sous la surface, et toutes les quelques secondes il en sort, le corps courbé
/// le long de son chemin, et y rentre la tête la première. Chacun garde donc une
/// place par rapport au navire, la rejoint avec la vitesse et le virage d'un
/// nageur, et déroule son propre cycle de respiration — de loin en loin un vrai
/// saut —, jetant de l'eau en sortant et encore en rentrant.
///
/// Les positions sont en mètres LOCAUX et glissent avec l'origine flottante.
/// </summary>
public sealed class Dolphins
{
    public const int Max = 7;

    public enum Mood { Away, Here, Leaving }

    public readonly DolphinRules K;
    public readonly Dolphin[] Pod = new Dolphin[Max];
    public Mood State { get; private set; } = Mood.Away;
    /// <summary>Ce qu'il leur reste à rester, en heures de jeu.</summary>
    public double Left { get; private set; }

    readonly Random _rng;

    /// <summary>Ce qu'ils jettent en perçant la surface : (où, eau, vitesse, gerbe).</summary>
    public Action<Vec3d, double, double, double>? Splash;
    /// <summary>Ce qu'on en dit au joueur, une fois, à leur arrivée.</summary>
    public Action<string>? Say;

    public Dolphins(DolphinRules? k = null, int seed = 0)
    {
        K = k ?? new DolphinRules();
        _rng = seed == 0 ? new Random() : new Random(seed);
        for (int i = 0; i < Max; i++) Pod[i] = new Dolphin { Slot = i, Side = i % 2 == 0 ? -1 : 1 };
    }

    double R(double a, double b) => a + _rng.NextDouble() * (b - a);

    /// <summary>Faire venir une bande maintenant — pour l'essai, ou par le sort.</summary>
    public bool Summon(Vec3d shipPos, Quatd shipQuat)
    {
        int n = K.PodMin + (int)Math.Floor(_rng.NextDouble() * (K.PodMax - K.PodMin + 1));
        var back = shipQuat.Rotate(new Vec3d(0, 0, -1));
        for (int i = 0; i < Max; i++)
        {
            var a = Pod[i];
            a.On = i < n;
            if (!a.On) continue;
            // ils arrivent de l'arrière et d'un bord
            a.Pos = shipPos + back * (60 + _rng.NextDouble() * 40);
            a.Pos = new Vec3d(a.Pos.X + (_rng.NextDouble() - 0.5) * 40, a.Pos.Y, a.Pos.Z + (_rng.NextDouble() - 0.5) * 40);
            a.Vel = Vec3d.Zero;
            a.Y = -2; a.Vy = 0;
            a.Phase = _rng.NextDouble();
            a.Rate = 0.28 + _rng.NextDouble() * 0.12;      // remontées par seconde
            a.Scale = 0.85 + _rng.NextDouble() * 0.35;
            a.Seed = _rng.NextDouble() * 100;
            a.WasUp = false;
            a.Leap = 1;
        }
        State = Mood.Here;
        Left = (K.MinutesMin + _rng.NextDouble() * (K.MinutesMax - K.MinutesMin)) / 60;
        Say?.Invoke("Des dauphins viennent jouer à l'étrave !");
        return true;
    }

    /// <summary>
    /// Une image. <paramref name="hours"/> est le temps de jeu écoulé, en heures ;
    /// <paramref name="sea"/> l'état de mer ; <paramref name="shore"/> la distance
    /// à la côte ; <paramref name="surface"/> la hauteur d'eau en un point.
    /// </summary>
    public void Step(double dt, Body b, ShipSpec spec, double sea, double hours, double shore,
                     double t, Func<double, double, double> surface)
    {
        if (State == Mood.Away)
        {
            if (!K.Enabled || sea > K.CalmBelow || shore < K.AwayFromShore) return;
            if (hours > 0 && _rng.NextDouble() < K.PerDay * hours / 24) Summon(b.Pos, b.Quat);
            if (State == Mood.Away) return;
        }
        if (State == Mood.Here)
        {
            Left -= hours;
            if (Left <= 0 || sea > K.CalmBelow + 0.5) State = Mood.Leaving;
        }

        double L = spec.L, B = spec.B;
        var fwd = b.Quat.Rotate(new Vec3d(0, 0, 1));
        fwd = new Vec3d(fwd.X, 0, fwd.Z);
        double fl = Math.Sqrt(fwd.X * fwd.X + fwd.Z * fwd.Z);
        if (fl > 1e-9) fwd = new Vec3d(fwd.X / fl, 0, fwd.Z / fl);
        var right = new Vec3d(-fwd.Z, 0, fwd.X);
        double way = Math.Sqrt(b.Vel.X * b.Vel.X + b.Vel.Z * b.Vel.Z);
        int seen = 0;

        foreach (var a in Pod)
        {
            if (!a.On) continue;
            int k = a.Slot;

            // --- où il veut être ---
            double tx, tz;
            if (State == Mood.Leaving)
            {
                // il reste en arrière, au large, et s'en va
                tx = a.Pos.X - fwd.X * 8 + right.X * a.Side * 4;
                tz = a.Pos.Z - fwd.Z * 8 + right.Z * a.Side * 4;
            }
            else if (way > 1.5)
            {
                // dans l'onde de pression de l'étrave, en louvoyant
                double ahead = L * 0.5 + 2 + (k % 3) * 3 + Math.Sin(t * 0.4 + a.Seed) * 2.5;
                double abeam = B * 0.5 + 1.5 + ((k >> 1) % 3) * 1.8 + Math.Sin(t * 0.33 + a.Seed * 2) * 1.2;
                tx = b.Pos.X + fwd.X * ahead + right.X * a.Side * abeam;
                tz = b.Pos.Z + fwd.Z * ahead + right.Z * a.Side * abeam;
            }
            else
            {
                // autour d'elle, sur un cercle lent
                double ang = t * 0.12 * a.Side + k * 1.1;
                double rad = L * 0.8 + 8 + (k % 3) * 5;
                tx = b.Pos.X + Math.Cos(ang) * rad;
                tz = b.Pos.Z + Math.Sin(ang) * rad;
            }

            /* IL Y NAGE : un ressort vers la place, borné à la vitesse d'un
               dauphin. Et il EMPORTE l'erre du navire, si bien que la poursuite
               ne porte que sur l'écart et non sur toute la route. */
            double vmax = Math.Max(6, way + 4);
            double vx = a.Vel.X + ((tx - a.Pos.X) * 0.8 - a.Vel.X * 1.2) * dt;
            double vz = a.Vel.Z + ((tz - a.Pos.Z) * 0.8 - a.Vel.Z * 1.2) * dt;
            double carry = State == Mood.Leaving ? 0 : Math.Min(1, dt * 0.6);
            vx += (b.Vel.X - vx) * carry;
            vz += (b.Vel.Z - vz) * carry;
            double hv = Math.Sqrt(vx * vx + vz * vz);
            if (hv > vmax) { vx *= vmax / hv; vz *= vmax / hv; }
            a.Vel = new Vec3d(vx, 0, vz);
            a.Pos = new Vec3d(a.Pos.X + vx * dt, 0, a.Pos.Z + vz * dt);

            // --- le cycle de la remontée ---
            a.Phase += dt * a.Rate;
            if (a.Phase >= 1)
            {
                a.Phase -= 1;
                a.Leap = _rng.NextDouble() < 0.18 ? 2.2 : 1;      // de loin en loin, un vrai saut
            }
            double s = surface(a.Pos.X, a.Pos.Z);
            double deep = State == Mood.Leaving ? -3.5 : -1.4;
            // hors de l'eau le premier quart du cycle, en arc ; dessous, il plane
            double up = a.Phase < 0.28 ? Math.Sin(Math.PI * a.Phase / 0.28) : 0;
            double yWant = s + deep + up * (-deep + 0.9 + (a.Leap > 1 ? 1.4 : 0));
            double yNew = a.Y + (yWant - a.Y) * Math.Min(1, dt * 12);
            a.Vy = (yNew - a.Y) / Math.Max(dt, 1e-3);
            a.Y = yNew;

            // son cap suit sa route, son assiette suit son arc
            double hvel = Math.Max(0.5, Math.Sqrt(vx * vx + vz * vz));
            a.Pitch = -Math.Atan2(a.Vy, hvel) * 0.9;
            a.Yaw = Math.Atan2(vx, vz);
            a.Tail = Math.Sin(t * (5 + hv * 0.4) + a.Seed) * 0.35;

            /* DE L'EAU DES DEUX CÔTÉS : une nappe jetée en perçant la surface, et
               un panache plus lourd en y rentrant la tête la première. Un saut en
               jette davantage. */
            bool above = a.Y > s + 0.1;
            if (above != a.WasUp)
            {
                if (above) Splash?.Invoke(new Vec3d(a.Pos.X, s, a.Pos.Z), 1.8 * a.Leap, 3 + a.Leap, 1.5);
                else Splash?.Invoke(new Vec3d(a.Pos.X, s, a.Pos.Z), 3.2 * a.Leap, 3.5 + a.Leap, 1.3);
            }
            a.WasUp = above;

            if (State == Mood.Leaving &&
                Math.Sqrt((a.Pos.X - b.Pos.X) * (a.Pos.X - b.Pos.X) + (a.Pos.Z - b.Pos.Z) * (a.Pos.Z - b.Pos.Z)) > 160)
                a.On = false;
            if (a.On) seen++;
        }
        if (seen == 0) State = Mood.Away;
    }

    /// <summary>L'origine a glissé : ils sont en mètres LOCAUX.</summary>
    public void Rebase(double dx, double dz)
    {
        foreach (var a in Pod) a.Pos = new Vec3d(a.Pos.X + dx, a.Pos.Y, a.Pos.Z + dz);
    }
}
