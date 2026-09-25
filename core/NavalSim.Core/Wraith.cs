using System;
using System.Text.Json;

namespace NavalSim.Core;

/// <summary>
/// LE VAISSEAU FANTÔME QUI RÔDE — settings.json → wraith.
///
/// À ne pas confondre avec la flotte du Cimetière des Galions
/// (<see cref="GhostScene"/>) : celle-là est un LIEU, une bataille rejouée
/// toujours au même endroit, qu'on va voir. Celui-ci est une RENCONTRE — il peut
/// paraître n'importe où au large, une nuit de brume, et c'est lui qui vient.
/// </summary>
public sealed class WraithRules
{
    public bool Enabled = true;
    /// <summary>Apparitions par heure de jeu, quand la nuit, la brume et le large s'y prêtent.</summary>
    public double PerHour = 0.8;
    /// <summary>Il ne hante pas les atterrages : deux milles de toute côte.</summary>
    public double ShoreMin = 3704;
    /// <summary>Il lui faut de la brume, et de la nuit.</summary>
    public double FogMin = 0.30, NightMin = 0.45;
    /// <summary>Où il paraît, en mètres : assez loin pour n'être qu'une silhouette.</summary>
    public double Sight = 900;
    /// <summary>Son erre, en mètres par seconde : huit à dix nœuds, sans une voile qui porte.</summary>
    public double SpeedMin = 4.12, SpeedMax = 5.14;
    /// <summary>Ce qu'il tourne au plus, en degrés par seconde : il ne manœuvre pas, il dérive sur vous.</summary>
    public double Turn = 2.5;
    /// <summary>Ce qu'il reste avant de s'effacer, et la distance au-delà de laquelle il s'en va.</summary>
    public double Linger = 210, Gone = 1800;
    /// <summary>La coque qu'il emprunte.</summary>
    public string Hull = "frigate17e";

    public static WraithRules FromJson(JsonElement j)
    {
        var r = new WraithRules();
        double D(string k, double v) => j.TryGetProperty(k, out var e) && e.ValueKind == JsonValueKind.Number ? e.GetDouble() : v;
        if (j.TryGetProperty("enabled", out var en) && (en.ValueKind == JsonValueKind.True || en.ValueKind == JsonValueKind.False))
            r.Enabled = en.GetBoolean();
        r.PerHour = Math.Max(0, D("perHour", r.PerHour));
        r.ShoreMin = Math.Max(0, D("shoreMin", r.ShoreMin));
        r.FogMin = D("fogMin", r.FogMin);
        r.NightMin = D("nightMin", r.NightMin);
        r.Sight = Math.Max(100, D("sight", r.Sight));
        r.SpeedMin = Math.Max(0.5, D("speedMin", r.SpeedMin));
        r.SpeedMax = Math.Max(r.SpeedMin, D("speedMax", r.SpeedMax));
        r.Turn = Math.Max(0, D("turn", r.Turn));
        r.Linger = Math.Max(10, D("linger", r.Linger));
        r.Gone = Math.Max(200, D("gone", r.Gone));
        if (j.TryGetProperty("hull", out var h) && h.ValueKind == JsonValueKind.String) r.Hull = h.GetString()!;
        return r;
    }
}

/// <summary>
/// UN VAISSEAU FANTÔME, ET CE QUI LE FAIT VENIR.
///
/// IL NE NAVIGUE PAS, IL FLOTTE. Pas de voiles qui portent, pas de vent, pas de
/// bordées à prendre : une erre constante de huit à dix nœuds, et un cap qu'il
/// change de deux degrés et demi par seconde au plus. C'est ce qui le rend
/// effrayant — rien de ce qu'on sait faire d'un navire ne s'applique à lui, et
/// l'on ne peut ni le distancer au près ni le semer sous le vent.
///
/// CE QUI L'ATTIRE EST LA LUMIÈRE, et c'est la seule règle du jeu. Feux allumés,
/// il vient ; feux couverts, il garde sa route et passe. Rien ne l'arrête, rien
/// ne le blesse — il n'y a qu'à disparaître.
///
/// Les positions sont en mètres LOCAUX, comme la coque qu'il poursuit.
/// </summary>
public sealed class Wraith
{
    public enum Mood { Away, Abroad, Leaving }

    public readonly WraithRules K;
    readonly Random _rng;

    public Mood State { get; private set; } = Mood.Away;
    /// <summary>Sa place et son cap (radians, 0 au nord), en repère local.</summary>
    public Vec3d Pos;
    public double Heading;
    public double Speed { get; private set; }
    /// <summary>De 0 (absent) à 1 (entier).</summary>
    public double Fade { get; private set; }
    /// <summary>Ce qu'il lui reste à rôder, en secondes.</summary>
    public double Left { get; private set; }
    /// <summary>A-t-il déjà passé le bord ? Pour ne le dire qu'une fois.</summary>
    public bool Passed;
    /// <summary>
    /// APPELÉ À LA MAIN. Le sort ne le fait paraître que la nuit, dans la brume
    /// et au large ; appelé, il tient sa ronde jusqu'au bout même si le ciel se
    /// découvre. Sans quoi la touche qui le convoque ne montrerait rien — il
    /// s'effacerait à l'image suivante, ce qui est exactement ce qui arrivait au
    /// kraken avant qu'on lui amène son grain.
    /// </summary>
    public bool Forced { get; private set; }

    public Action<string>? Say;

    public Wraith(WraithRules? k = null, int seed = 0)
    {
        K = k ?? new WraithRules();
        _rng = seed == 0 ? new Random() : new Random(seed);
    }

    double R(double a, double b) => a + _rng.NextDouble() * (b - a);

    /// <summary>
    /// Le faire paraître maintenant, autour de ce point — par le sort, ou appelé.
    /// <paramref name="force"/> : il tient sa ronde même si la nuit, la brume ou
    /// le large viennent à manquer.
    /// </summary>
    public void Summon(double x, double z, bool force = false)
    {
        /* IL PARAÎT SUR L'AVANT D'UN BORD OU DE L'AUTRE, jamais droit derrière :
           un fantôme qu'on découvre dans son sillage n'est qu'une surprise, un
           fantôme qui vient de l'avant est une rencontre. */
        double a = R(-2.2, 2.2);
        Pos = new Vec3d(x + Math.Sin(a) * K.Sight, 0, z + Math.Cos(a) * K.Sight);
        // il fait route en travers, pas droit sur nous : il faut qu'on ait le temps de voir
        Heading = a + Math.PI + R(-0.9, 0.9);
        Speed = R(K.SpeedMin, K.SpeedMax);
        Fade = 0;
        Left = K.Linger;
        Passed = false;
        Forced = force;
        State = Mood.Abroad;
        Say?.Invoke("Une voile pâle, sur la brume… elle ne porte rien.");
    }

    /// <summary>
    /// Une image. <paramref name="lit"/> : les feux du bord sont-ils allumés ;
    /// <paramref name="hours"/> : le temps de jeu écoulé ; le reste est l'état du
    /// ciel et de la mer autour du joueur.
    /// </summary>
    public void Step(double dt, double hours, double x, double z, bool lit,
                     double night, double fog, double shore)
    {
        bool propice = K.Enabled && night >= K.NightMin && fog >= K.FogMin && shore >= K.ShoreMin;

        if (State == Mood.Away)
        {
            if (propice && hours > 0 && _rng.NextDouble() < K.PerHour * hours) Summon(x, z);
            if (State == Mood.Away) return;
        }

        double dx = x - Pos.X, dz = z - Pos.Z;
        double d = Math.Sqrt(dx * dx + dz * dz);

        if (State == Mood.Abroad)
        {
            Left -= dt;
            /* IL S'EN VA quand le jour vient, quand la brume tombe, quand on a
               gagné la côte, quand il a rôdé son temps — ou quand il est trop
               loin pour qu'on le voie encore. */
            if ((!propice && !Forced) || Left <= 0 || d > K.Gone) State = Mood.Leaving;
        }

        /* CE QUI L'ATTIRE : la lumière, et rien d'autre. Feux couverts, il garde
           son cap et passe son chemin ; feux allumés, il vient — lentement, parce
           qu'un cap qui se colle instantanément se lit comme un missile et non
           comme une chose qui dérive. */
        if (State == Mood.Abroad && lit)
        {
            double want = Math.Atan2(dx, dz);
            double err = ((want - Heading + Math.PI * 3) % (Math.PI * 2)) - Math.PI;
            double max = K.Turn * Math.PI / 180 * dt;
            Heading += Math.Clamp(err, -max, max);
        }

        Pos = new Vec3d(Pos.X + Math.Sin(Heading) * Speed * dt, 0, Pos.Z + Math.Cos(Heading) * Speed * dt);

        /* IL PASSE, ET NE FAIT RIEN. Rien ne l'arrête et rien ne le blesse ; il
           n'y a qu'à n'être pas vu. Ce qu'on y perd est ce qu'on y a eu peur. */
        if (State == Mood.Abroad && !Passed && d < 60)
        {
            Passed = true;
            Say?.Invoke("Il passe à toucher le bord. Personne, sur son pont.");
        }

        double want2 = State == Mood.Leaving ? 0 : 1;
        Fade += Math.Clamp(want2 - Fade, -dt / 6, dt / 6);
        if (State == Mood.Leaving && Fade <= 0.001)
        {
            State = Mood.Away;
            Forced = false;
            Say?.Invoke("La brume se referme sur lui.");
        }
    }

    /// <summary>L'origine a glissé : il est en mètres LOCAUX.</summary>
    public void Rebase(double dx, double dz) => Pos = new Vec3d(Pos.X + dx, Pos.Y, Pos.Z + dz);
}
