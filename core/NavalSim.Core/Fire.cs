using System;
using System.Collections.Generic;
using System.Text.Json;

namespace NavalSim.Core;

/// <summary>
/// L'INCENDIE À BORD — settings.json → fire.
///
/// Un navire de bois, de chanvre, de toile et de goudron, avec de la poudre dans
/// le ventre : le feu est ce qu'on craint avant l'eau. Un boulet à bout portant,
/// une gargousse crevée, la foudre dans la mâture, un brûlot lâché en travers de
/// la route — et le bord passe de la bataille au sauvetage.
/// </summary>
public sealed class FireSettings
{
    public bool Enabled = true;

    /// <summary>La chance qu'un boulet de PLEIN calibre mette le feu, à bout portant.</summary>
    public double ShotChance = 0.06;
    /// <summary>Celle d'un coup de foudre dans la mâture, qui est bien plus sûre.</summary>
    public double BoltChance = 0.35;

    /// <summary>Ce qu'un foyer gagne par seconde, laissé à lui-même.</summary>
    public double Grow = 0.12;
    /// <summary>
    /// La chance par seconde qu'un foyer bien pris en allume un autre. C'est elle
    /// qui décide si un incendie est une avarie ou une fin : à zéro on éteint
    /// toujours, trop haut on ne peut plus rien.
    /// </summary>
    public double Spread = 0.05;
    /// <summary>
    /// CE QUE L'ÉQUIPAGE ÉTEINT PAR SECONDE — un TOTAL, réparti entre les foyers.
    /// Là est tout le jeu : un seul départ est noyé en quelques secondes, trois
    /// se combattent, six débordent le monde. Aucun seuil n'a été écrit pour
    /// cela ; la division s'en charge.
    /// </summary>
    public double Crew = 0.06;
    /// <summary>Ce que la pluie battante ajoute à leur peine, par seconde.</summary>
    public double Rain = 0.05;

    /// <summary>La somme des foyers au-delà de laquelle la soute prend.</summary>
    public double Magazine = 2.6;
    /// <summary>Combien de foyers au plus : au-delà, un départ de plus ravive le plus proche.</summary>
    public int MaxSeats = 6;
    /// <summary>Sous cette ardeur, un foyer est mort et disparaît.</summary>
    public double Out = 0.02;

    public static FireSettings FromJson(JsonElement j)
    {
        var s = new FireSettings();
        double D(string n, double v) => j.TryGetProperty(n, out var e) && e.ValueKind == JsonValueKind.Number ? e.GetDouble() : v;
        if (j.TryGetProperty("enabled", out var en) && (en.ValueKind == JsonValueKind.True || en.ValueKind == JsonValueKind.False))
            s.Enabled = en.GetBoolean();
        s.ShotChance = Math.Clamp(D("shotChance", s.ShotChance), 0, 1);
        s.BoltChance = Math.Clamp(D("boltChance", s.BoltChance), 0, 1);
        s.Grow = Math.Max(0, D("grow", s.Grow));
        s.Spread = Math.Max(0, D("spread", s.Spread));
        s.Crew = Math.Max(0, D("crew", s.Crew));
        s.Rain = Math.Max(0, D("rain", s.Rain));
        s.Magazine = Math.Max(0.2, D("magazine", s.Magazine));
        s.MaxSeats = Math.Max(1, (int)D("maxSeats", s.MaxSeats));
        s.Out = Math.Max(0.001, D("out", s.Out));
        return s;
    }
}

/// <summary>
/// LE FEU D'UN NAVIRE — un par coque, et rien de global.
///
/// Des FOYERS, chacun à sa place dans le repère du bord, chacun avec son ardeur
/// de 0 à 1. Ils grandissent seuls, ils en allument d'autres, l'équipage les
/// combat, la pluie l'aide. Ce qui se passe ensuite n'est écrit nulle part : si
/// l'équipage gagne, le feu meurt ; s'il perd, la somme monte, et passé
/// <see cref="FireSettings.Magazine"/> la soute prend.
///
/// LA LUTTE EST UN BUDGET, PAS UN SEUIL. L'équipage a tant de seaux par seconde,
/// répartis entre les foyers : un départ est noyé, trois se combattent, six
/// débordent le monde. C'est la division qui fait le drame, et non une règle
/// écrite pour lui.
/// </summary>
public sealed class Fire
{
    public sealed class Seat
    {
        /// <summary>Où il brûle, dans le repère de SA coque.</summary>
        public Vec3d P;
        /// <summary>Son ardeur, de 0 (mort) à 1 (tout est pris).</summary>
        public double Heat;
        /// <summary>Depuis quand il brûle, en secondes — pour ce qui palpite.</summary>
        public double Age;
    }

    public readonly FireSettings K;
    readonly Func<double> _rng;
    public readonly List<Seat> Seats = new();

    /// <summary>La somme des ardeurs : ce qui décide de la soute, et ce qu'on montre.</summary>
    public double Total { get; private set; }
    /// <summary>Le pire foyer, de 0 à 1 : la lumière et le bruit le suivent.</summary>
    public double Worst { get; private set; }
    /// <summary>Vrai une seule image, quand la soute prend : à l'appelant de la faire sauter.</summary>
    public bool Doomed { get; private set; }

    /// <summary>« depart », « encore », « gagne », « maitrise », « soute ».</summary>
    public Action<string>? Event;

    public Fire(FireSettings? k = null, Func<double>? rng = null)
    {
        K = k ?? new FireSettings();
        _rng = rng ?? new Random().NextDouble;
    }

    public bool Burning => Seats.Count > 0;

    /// <summary>
    /// UN DÉPART DE FEU à cet endroit du bord. <paramref name="force"/> est son
    /// ardeur initiale. Trop de foyers déjà : il ravive le plus proche plutôt que
    /// d'en ouvrir un de plus — un navire ne brûle pas en vingt endroits, il brûle
    /// de plus en plus fort aux mêmes.
    /// </summary>
    public void Light(Vec3d local, double force = 0.12)
    {
        if (!K.Enabled || force <= 0) return;
        Seat? near = null;
        double best = double.MaxValue;
        foreach (var s in Seats)
        {
            double d = (s.P - local).Length;
            if (d < best) { best = d; near = s; }
        }
        /* DEUX FOYERS À TROIS MÈTRES N'EN FONT QU'UN. Sans cela une bordée en
           ouvrait dix d'un coup, et l'équipage — dont l'effort se DIVISE — était
           débordé par ce qui n'était qu'un seul brasier compté dix fois. */
        if (near != null && (best < 3 || Seats.Count >= K.MaxSeats))
        {
            near.Heat = Math.Min(1, near.Heat + force);
            return;
        }
        bool first = Seats.Count == 0;
        Seats.Add(new Seat { P = local, Heat = Math.Min(1, force) });
        Event?.Invoke(first ? "depart" : "encore");
    }

    /// <summary>
    /// Une image. <paramref name="rain"/> de 0 à 1 ; <paramref name="crew"/> est
    /// ce que le bord peut encore donner (0 si personne ne combat, 1 pleinement) ;
    /// <paramref name="drowned"/> : elle coule, et l'eau fait le reste du travail.
    /// </summary>
    public void Step(double dt, double rain, double crew, bool drowned)
    {
        Doomed = false;
        if (Seats.Count == 0) { Total = 0; Worst = 0; return; }

        /* L'EFFORT SE DIVISE. C'est la seule règle du combat, et elle suffit :
           l'équipage donne tant par seconde, et chaque foyer n'en reçoit qu'une
           part. Un navire qui brûle en six endroits ne peut plus être sauvé sans
           qu'on l'ait écrit nulle part. */
        double lutte = (K.Crew * Math.Clamp(crew, 0, 1) + K.Rain * Math.Clamp(rain, 0, 1)) / Seats.Count;
        if (drowned) lutte += 0.30;

        double total = 0, worst = 0;
        for (int i = Seats.Count - 1; i >= 0; i--)
        {
            var s = Seats[i];
            s.Age += dt;
            /* IL GRANDIT COMME CE QU'IL A DÉJÀ PRIS, et non d'un pas constant : un
               feu qui vient de naître est un feu qu'on éteint, un feu établi tire
               son propre tirage. Le facteur (0,25 + ardeur)(1 − ardeur) donne la
               courbe en S qu'on attend — lent à partir, brutal au milieu, plafonné
               à la fin, quand tout ce qui pouvait prendre a pris. */
            s.Heat += (K.Grow * (0.25 + s.Heat) * (1 - s.Heat) - lutte) * dt;
            if (s.Heat <= K.Out) { Seats.RemoveAt(i); continue; }
            s.Heat = Math.Min(1, s.Heat);
            total += s.Heat;
            worst = Math.Max(worst, s.Heat);
        }

        if (Seats.Count == 0)
        {
            Total = 0; Worst = 0;
            Event?.Invoke("maitrise");
            return;
        }

        /* IL SE PROPAGE — un foyer bien pris en allume un autre, à quelques mètres
           de lui et jamais au hasard du bord : le feu court le long du pont et des
           cloisons, il ne saute pas d'un bout à l'autre. Un seul par image : il
           court, il ne se multiplie pas. */
        if (Seats.Count < K.MaxSeats)
            for (int i = 0; i < Seats.Count; i++)
            {
                var s = Seats[i];
                if (s.Heat < 0.45) continue;
                if (_rng() > K.Spread * s.Heat * dt) continue;
                double a = _rng() * Math.Tau, r = 3 + _rng() * 5;
                Light(new Vec3d(s.P.X + Math.Sin(a) * r * 0.35, s.P.Y, s.P.Z + Math.Cos(a) * r), 0.10);
                break;
            }

        double avant = Total;
        Total = total;
        Worst = worst;
        if (avant < K.Magazine && Total >= K.Magazine)
        {
            Doomed = true;
            Event?.Invoke("soute");
        }
        else if (avant < 1.0 && Total >= 1.0) Event?.Invoke("gagne");
    }

    /// <summary>Tout éteint — au radoub, ou quand la coque s'en va.</summary>
    public void Clear() { Seats.Clear(); Total = 0; Worst = 0; Doomed = false; }
}
