using System.Text.Json;

namespace NavalSim.Core;

/// <summary>Les réglages du climat, ceux de settings.json (section « climate »).</summary>
public sealed class ClimateSettings
{
    public double Mean = 9.5;             // °C, la moyenne de l'année
    public double Seasonal = 9;           // ± sur l'année : −2..3 °C fin janvier
    public double Daily = 3;              // ± sur la journée
    public double Wander = 3;             // ± d'un jour à l'autre
    public double ColdestDay = 30;        // jour de l'année (30 janvier)
    public double ShowersPerDay = 3;      // averses par jour, en moyenne
    public double ShowerMinLo = 20, ShowerMinHi = 90;   // durée d'une averse, en minutes de jeu
    public double SnowBelow = 1.5;        // °C : plus froid, il neige

    public static readonly (double Top, string Word)[] Words =
    {
        (-5, "Froid glacial"), (0, "Gel"), (5, "Froid mordant"), (10, "Frais"),
        (16, "Doux"), (22, "Tiède"), (27, "Chaud"), (99, "Chaleur lourde")
    };

    /// <summary>Lire la section « climate » d'un settings.json ; une clé absente garde sa valeur.</summary>
    public static ClimateSettings FromJson(JsonElement c)
    {
        var s = new ClimateSettings();
        double D(string k, double v) => c.TryGetProperty(k, out var e) && e.ValueKind == JsonValueKind.Number ? e.GetDouble() : v;
        s.Mean = D("mean", s.Mean); s.Seasonal = D("seasonal", s.Seasonal); s.Daily = D("daily", s.Daily);
        s.Wander = D("wander", s.Wander); s.ColdestDay = D("coldestDay", s.ColdestDay);
        s.ShowersPerDay = D("showersPerDay", s.ShowersPerDay); s.SnowBelow = D("snowBelow", s.SnowBelow);
        if (c.TryGetProperty("showerMinutes", out var m) && m.ValueKind == JsonValueKind.Array && m.GetArrayLength() == 2)
        { s.ShowerMinLo = m[0].GetDouble(); s.ShowerMinHi = m[1].GetDouble(); }
        return s;
    }
}

/// <summary>
/// LE TEMPS QU'ON SENT — climate.js : le froid qu'il fait, et ce qui tombe.
///
/// La TEMPÉRATURE a son propre climat, pas celui de la latitude du soleil : les
/// îles sont aux Caraïbes pour le soleil, mais on voulait un hiver avec de la
/// neige. Une moyenne, une saison la plus froide fin janvier, une journée la plus
/// chaude en milieu d'après-midi, une errance d'un jour à l'autre — la même pour
/// la même date —, et le froid d'un coup de vent ou d'une averse par-dessus.
///
/// Les AVERSES vont et viennent au hasard, en heures de JEU : presser le temps
/// en amène autant par jour. Neige sous SnowBelow, pluie au-dessus. Le tirage est
/// injecté, comme celui du vent, pour le banc de parité.
///
/// C'est dit, pas mesuré : pas de thermomètre en mer en 1598.
/// </summary>
public sealed class Climate
{
    public readonly ClimateSettings K;
    // l'averse en cours : son âge, sa durée et son plus fort, en heures de jeu
    public bool Showering { get; private set; }
    double _showerT, _showerLen, _showerPeak;
    /// <summary>Ce que l'averse fait tomber en ce moment, 0 à 1.</summary>
    public double Amount { get; private set; }
    public double Temp { get; private set; } = 10;

    readonly Func<double> _random;

    public Climate(ClimateSettings? settings = null, Func<double>? random = null)
    {
        K = settings ?? new ClimateSettings();
        var r = new Random();
        _random = random ?? r.NextDouble;
    }

    /* UN NOMBRE STABLE POUR UN JOUR DONNÉ, au bit près de la page, dans [0 ; 1[ —
       SANS signe : la page le rendait signé (`x ^= x >>> 15` y laisse un int32),
       et l'errance d'un jour à l'autre allait de −6 à 0 °C ; corrigé des deux
       côtés le 2026-09-19. n peut être négatif (un départ avant 1970), et
       `(n*2654435761) >>> 0` y prend le reste modulo 2^32 d'un produit calculé
       en double — exact ici, |n| restant sous le million. */
    static double DayHash(double n)
    {
        uint x = ToUint32(n * 2654435761.0);
        x ^= x >> 13;
        x = unchecked(x * 0x5bd1e995u);
        x ^= x >> 15;
        return x / 4294967296.0;
    }

    static uint ToUint32(double v)
    {
        double m = Math.Truncate(v) % 4294967296.0;
        if (m < 0) m += 4294967296.0;
        return (uint)m;
    }

    /// <param name="dtHours">de combien l'horloge du jour a avancé</param>
    /// <param name="hour">l'heure du jour</param>
    /// <param name="storm">0 à 1 : la dureté du coup de vent ou de la dépression (le ciel)</param>
    public void Update(double dtHours, Calendar calendar, double hour, double storm)
    {
        // --- les averses ---
        if (Showering)
        {
            _showerT += dtHours;
            double u = _showerT / _showerLen;
            if (u >= 1) { Showering = false; Amount = 0; }
            else Amount = _showerPeak * Math.Sin(Math.PI * u);      // elle vient, et repart
        }
        else if (dtHours > 0 && _random() < K.ShowersPerDay * dtHours / 24)
        {
            Showering = true;
            _showerT = 0;
            _showerLen = (K.ShowerMinLo + _random() * (K.ShowerMinHi - K.ShowerMinLo)) / 60;
            _showerPeak = 0.35 + _random() * 0.65;
        }

        // --- la température ---
        double doy = calendar.DayOfYear(hour);
        double season = -K.Seasonal * Math.Cos(2 * Math.PI * (doy - K.ColdestDay) / 365);
        double day = K.Daily * Math.Sin(2 * Math.PI * (hour - 9) / 24);          // le plus chaud à 15 h
        double n = calendar.T0Ms / 86400000 + calendar.Day;
        double wander = K.Wander * (DayHash(n) * 2 - 1);
        Temp = K.Mean + season + day + wander - 3 * storm - 2 * Amount;
    }

    /// <summary>
    /// Une averse tout de suite, de <paramref name="minutes"/> minutes de jeu et de
    /// plus fort <paramref name="peak"/> — pour l'essayer sans attendre le hasard.
    /// Un ajout du portage : la page n'en a pas.
    /// </summary>
    public void StartShower(double minutes, double peak)
    {
        Showering = true;
        _showerT = 0;
        _showerLen = Math.Max(1, minutes) / 60;
        _showerPeak = Math.Clamp(peak, 0, 1);
    }

    /// <summary>Ce qui tombe, et combien, compte tenu de ce qu'apporte aussi le gros temps.</summary>
    public (double Amount, bool Snow) Precipitation(double stormWet) =>
        (Math.Max(stormWet, Amount), Temp < K.SnowBelow);

    public string Word()
    {
        foreach (var (top, w) in ClimateSettings.Words) if (Temp < top) return w;
        return ClimateSettings.Words[^1].Word;
    }
}
