using System;
using System.Text.Json;

namespace NavalSim.Core;

/// <summary>Les règles de l'estime, celles de settings.json → reckoning.</summary>
public sealed class ReckoningSettings
{
    public bool Enabled = true;
    public double GlassMinutes = 30;       // un sablier : une lecture du loch et du compas, en minutes de jeu
    public double LogBias = 0.06;          // l'erreur propre du loch (écart-type, en part de la vitesse)
    public double LogNoise = 0.03;         // et celle de chaque lecture
    public double CompassBias = 2.0;       // l'erreur propre du compas, degrés (variation mal connue)
    public double CompassNoise = 1.0;      // et celle de chaque lecture (le navire embarde)
    public double Growth = 0.07;           // l'incertitude gagnée, en part de la distance courue
    public double NoonError = 3.0;         // la hauteur de midi au quadrant de Davis, minutes d'arc
    public double PassageError = 0.04;     // l'incertitude à l'atterrage, en part de la traversée

    public static ReckoningSettings FromJson(JsonElement k)
    {
        var s = new ReckoningSettings();
        double D(string n, double v) => k.TryGetProperty(n, out var e) && e.ValueKind == JsonValueKind.Number ? e.GetDouble() : v;
        if (k.TryGetProperty("enabled", out var en) && (en.ValueKind == JsonValueKind.True || en.ValueKind == JsonValueKind.False))
            s.Enabled = en.GetBoolean();
        s.GlassMinutes = D("glassMinutes", s.GlassMinutes);
        s.LogBias = D("logBias", s.LogBias); s.LogNoise = D("logNoise", s.LogNoise);
        s.CompassBias = D("compassBias", s.CompassBias); s.CompassNoise = D("compassNoise", s.CompassNoise);
        s.Growth = D("growth", s.Growth); s.NoonError = D("noonError", s.NoonError);
        s.PassageError = D("passageError", s.PassageError);
        return s;
    }
}

/// <summary>
/// L'ESTIME — où le capitaine CROIT être, et à combien près.
///
/// En 1690 on ne connaît pas sa position, on la tient. À chaque sablier, une
/// demi-heure, on file le loch (la vitesse) et on lit le compas (le cap), et
/// l'on porte sur la carte la distance courue dans cette direction. Tout y ment
/// un peu, et c'est ce qu'on reproduit, sans rien inventer :
/// - le LOCH a son erreur à lui (la ligne mal nouée, le sablier qui coule trop
///   vite), tirée une fois par navire, et chaque lecture la sienne ;
/// - le COMPAS a la sienne (la variation magnétique, mal connue) et chaque
///   lecture, sur un navire qui embarde, une autre ;
/// - la DÉRIVE n'y est pas du tout : le loch mesure la vitesse et le compas
///   le CAP, mais au près le navire glisse sous le vent de sa route, et cette
///   erreur-là sort d'elle-même de la physique.
/// L'incertitude grandit avec la distance courue ; elle se resserre à la
/// HAUTEUR DE MIDI (la latitude seulement — la longitude est hors de portée
/// avant le chronomètre, 1760) et s'annule au port.
///
/// Tout en mètres du MONDE (vrais), comme le carnet : la position estimée n'est
/// pas une position locale, elle survit au glissement de l'origine.
/// </summary>
public sealed class Reckoning
{
    public readonly ReckoningSettings S;
    /// <summary>Le point estimé, mètres du monde.</summary>
    public double X, Z;
    /// <summary>L'incertitude, un écart-type, nord-sud et est-ouest, en mètres de jeu.</summary>
    public double SigN, SigE;
    public bool Known { get; private set; }

    readonly Random _rng;
    double _logBias, _compassBias;
    double _glassLeft, _readSpeed, _readBearing;
    bool _reading;

    public Reckoning(ReckoningSettings s, int seed = 0)
    {
        S = s;
        _rng = seed == 0 ? new Random() : new Random(seed);
        Reroll();
    }

    double Gauss()
    {
        double u = 1 - _rng.NextDouble();
        return Math.Sqrt(-2 * Math.Log(u)) * Math.Cos(2 * Math.PI * _rng.NextDouble());
    }

    /// <summary>Un autre navire, d'autres instruments : leurs erreurs propres tirées à neuf.</summary>
    public void Reroll()
    {
        _logBias = Math.Clamp(Gauss() * S.LogBias, -2 * S.LogBias, 2 * S.LogBias);
        _compassBias = Math.Clamp(Gauss() * S.CompassBias, -2 * S.CompassBias, 2 * S.CompassBias) * Math.PI / 180;
    }

    /// <summary>On sait exactement où l'on est : à quai, ou en le lisant sur la côte.</summary>
    public void Fix(double x, double z)
    {
        X = x; Z = z;
        SigN = SigE = 0;
        Known = true;
        _reading = false;
    }

    /// <summary>Tenir un point donné, à tant près (une traversée, un carnet relu).</summary>
    public void Set(double x, double z, double sigN, double sigE)
    {
        X = x; Z = z; SigN = sigN; SigE = sigE;
        Known = true;
        _reading = false;
    }

    /// <summary>
    /// Une image. <paramref name="vx"/>, <paramref name="vz"/> : la vitesse vraie
    /// du navire (le loch la lit) ; <paramref name="bearing"/> : son cap vrai en
    /// degrés (le compas le lit) ; <paramref name="glassSeconds"/> : ce que dure
    /// un sablier en secondes de simulation, à l'allure du ciel.
    /// </summary>
    public void Step(double dt, double vx, double vz, double bearing, double glassSeconds)
    {
        if (!Known) return;
        if (!_reading || (_glassLeft -= dt) <= 0)
        {
            /* LE SABLIER EST RETOURNÉ : on file le loch, on lit le compas, et
               l'on tient ces deux nombres jusqu'au suivant — un changement
               d'allure entre deux lectures est une erreur de plus. */
            _glassLeft = Math.Max(1, glassSeconds);
            double speed = Math.Sqrt(vx * vx + vz * vz);
            _readSpeed = Math.Max(0, speed * (1 + _logBias + Gauss() * S.LogNoise));
            _readBearing = bearing * Math.PI / 180 + _compassBias + Gauss() * S.CompassNoise * Math.PI / 180;
            _reading = true;
        }
        // la distance courue, dans la direction lue : l'est est −x
        double d = _readSpeed * dt;
        X += -Math.Sin(_readBearing) * d;
        Z += Math.Cos(_readBearing) * d;
        /* L'incertitude croît AVEC la distance, pas comme sa racine : une erreur
           de loch ou de compas est la même d'un sablier à l'autre, elle
           s'additionne. En somme quadratique pas à pas, elle annonçait 9 m après
           20 km quand l'erreur vraie en faisait 1 800 (relevé au labo). */
        double g = S.Growth * d;
        SigN += g;
        SigE += g;
    }

    /// <summary>
    /// LA HAUTEUR DE MIDI : la latitude vraie, à l'erreur du quadrant près. Le
    /// nord est +z, et z ne dépend que de la latitude — la ligne qu'elle donne
    /// est une droite est-ouest sur la carte. <paramref name="metresPerMinute"/>
    /// est une minute d'arc de latitude en mètres de jeu (1 852 × l'échelle).
    /// </summary>
    public double NoonSight(double trueZ, double metresPerMinute)
    {
        double err = Gauss() * S.NoonError * metresPerMinute;
        Z = trueZ + err;
        SigN = S.NoonError * metresPerMinute;
        return err;
    }

    public string ToJson() => FormattableString.Invariant(
        $"{{ \"x\": {X:F1}, \"z\": {Z:F1}, \"sn\": {SigN:F1}, \"se\": {SigE:F1} }}");

    public void FromJson(JsonElement e)
    {
        double D(string n) => e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : 0;
        Set(D("x"), D("z"), D("sn"), D("se"));
    }
}
