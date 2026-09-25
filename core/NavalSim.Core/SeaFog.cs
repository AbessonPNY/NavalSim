using System;
using System.Text.Json;

namespace NavalSim.Core;

/// <summary>Les règles de la brume de surface, celles de settings.json → fog.</summary>
public sealed class SeaFogSettings
{
    public bool Enabled = true;
    public double Chance = 0.35;       // la part des nuits qui en ont
    public double From = 21;           // l'heure où elle monte
    public double AfterDawn = 1.5;     // les heures qu'elle tient après le lever (6 h)
    public double MaxWind = 4.5;       // au-delà de cette force, le vent la chasse (une brume d'advection tient par brise modérée)
    public double Density = 0.02;      // par mètre, au ras de l'eau : on voit à 150 m
    public double Height = 25;         // la HAUTEUR D ÉCHELLE de la couche, m — voir settings.json
    public double Rate = 1.0;          // de rien à pleine, en heures de jeu

    public static SeaFogSettings FromJson(JsonElement k)
    {
        var s = new SeaFogSettings();
        double D(string n, double v) => k.TryGetProperty(n, out var e) && e.ValueKind == JsonValueKind.Number ? e.GetDouble() : v;
        if (k.TryGetProperty("enabled", out var en) && (en.ValueKind == JsonValueKind.True || en.ValueKind == JsonValueKind.False))
            s.Enabled = en.GetBoolean();
        s.Chance = D("chance", s.Chance); s.From = D("from", s.From); s.AfterDawn = D("afterDawn", s.AfterDawn);
        s.MaxWind = D("maxWind", s.MaxWind); s.Density = D("density", s.Density);
        s.Height = D("height", s.Height); s.Rate = D("rate", s.Rate);
        return s;
    }
}

/// <summary>
/// LA BRUME DE SURFACE, LA NUIT.
///
/// Par nuit calme sous les tropiques, l'air qui se refroidit au-dessus d'une
/// mer restée tiède se charge d'humidité et la condense au ras de l'eau : une
/// couche de dix ou quinze mètres, si dense qu'on ne voit plus l'étrave d'un
/// navire à portée de voix — et, au-dessus, les étoiles, et la pomme des mâts
/// qui en sortent. Elle monte le soir, s'épaissit en une heure, et le soleil la
/// boit dans l'heure et demie qui suit son lever. Le vent la chasse.
///
/// Ce n'est qu'une QUANTITÉ, de 0 à 1 : le ciel en fait une couche de brume
/// basse et dense (<see cref="Sky.Fog"/>), et tout ce qui lit déjà la brume —
/// la mer, les coques, la terre, les feux qui s'éteignent au loin — la voit
/// sans une ligne de plus.
/// </summary>
public sealed class SeaFog
{
    public readonly SeaFogSettings S;
    /// <summary>De 0 (rien) à 1 (la couche entière).</summary>
    public double Amount { get; private set; }
    /// <summary>Cette nuit en a-t-elle ? Tiré au soir.</summary>
    public bool Tonight { get; private set; }

    readonly Random _rng;
    double _forced;

    public SeaFog(SeaFogSettings s, int seed = 0)
    {
        S = s;
        _rng = seed == 0 ? new Random() : new Random(seed);
    }

    /// <summary>La brume tout de suite, pour tant d'heures de jeu (débogage).</summary>
    public void Force(double hours) { _forced = hours; Tonight = true; }

    /// <summary>
    /// <paramref name="hours"/> : les heures de jeu passées depuis l'image
    /// d'avant ; <paramref name="dayTime"/> : l'heure qu'il est ;
    /// <paramref name="wind"/> : la force du vent ; <paramref name="dawn"/> :
    /// l'heure du lever du soleil.
    /// </summary>
    public void Update(double hours, double dayTime, double wind, double dawn)
    {
        if (!S.Enabled) { Amount = 0; return; }
        // le tirage, au soir : une nuit sur trois environ
        double before = (dayTime - hours + 24) % 24;
        if (before < S.From - 2 && dayTime >= S.From - 2) Tonight = _rng.NextDouble() < S.Chance;
        bool night = dayTime >= S.From || dayTime < dawn + S.AfterDawn;
        // forcée (débogage), elle vient quel que soit le vent
        bool want = _forced > 0 || (Tonight && night && wind < S.MaxWind);
        if (_forced > 0) _forced -= hours;
        double step = hours / Math.Max(0.05, S.Rate);
        // le vent la déchire deux fois plus vite qu'elle ne monte
        Amount = want ? Math.Min(1, Amount + step) : Math.Max(0, Amount - step * (wind >= S.MaxWind ? 2 : 1));
    }
}
