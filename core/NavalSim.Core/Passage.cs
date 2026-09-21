using System;
using System.Collections.Generic;

namespace NavalSim.Core;

/// <summary>
/// LA TRAVERSÉE D'UNE RÉGION À L'AUTRE — ce qui se passe entre deux cartes.
///
/// Chaque région est un monde à part, à son échelle (×0,4), et la mer qui les
/// sépare n'est pas dessinée : Port-Royal – la Tortue fait deux cents milles,
/// seize heures de jeu à six nœuds. On ne la navigue donc pas, on la COMPTE :
/// les milles réels entre là où l'on quitte une carte et l'atterrage où l'on
/// arrive sur l'autre, et le temps qu'il faut pour les faire avec le vent du
/// départ. Le calendrier avance d'autant.
///
/// LE VENT DÉCIDE, et c'est la seule chose de la traversée qui se joue : les
/// alizés soufflent de l'est, la Tortue est à l'est-nord-est de la Jamaïque — on
/// y va au près, en louvoyant, cinq jours ; on en revient vent arrière en deux.
/// C'était vrai des navires de l'époque, et c'est ce qui rendait Port-Royal
/// plus facile à quitter qu'à rejoindre.
/// </summary>
public static class Passage
{
    /// <summary>
    /// Au large : à cette distance de toute terre (mètres de jeu), on peut faire
    /// route vers une autre région. Trois kilomètres de jeu, sept et demi réels —
    /// hors de vue des détails de la côte, pas encore hors de vue de ses sommets.
    /// </summary>
    public const double Offing = 3000;

    /// <summary>La distance orthodromique, en milles, entre deux points.</summary>
    public static double Miles(double lat1, double lon1, double lat2, double lon2)
    {
        double r = Math.PI / 180;
        double p1 = lat1 * r, p2 = lat2 * r, dp = (lat2 - lat1) * r, dl = (lon2 - lon1) * r;
        double a = Math.Sin(dp / 2) * Math.Sin(dp / 2) + Math.Cos(p1) * Math.Cos(p2) * Math.Sin(dl / 2) * Math.Sin(dl / 2);
        // un rayon terrestre de 60 milles par degré, comme la carte
        return 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a)) / r * 60;
    }

    /// <summary>Le relèvement initial, en degrés vrais (0 nord, 90 est).</summary>
    public static double Bearing(double lat1, double lon1, double lat2, double lon2)
    {
        double r = Math.PI / 180;
        double p1 = lat1 * r, p2 = lat2 * r, dl = (lon2 - lon1) * r;
        double y = Math.Sin(dl) * Math.Cos(p2);
        double x = Math.Cos(p1) * Math.Sin(p2) - Math.Sin(p1) * Math.Cos(p2) * Math.Cos(dl);
        return (Math.Atan2(y, x) / r + 360) % 360;
    }

    /* LA VITESSE QU'ON FAIT SUR LA ROUTE, en nœuds, selon l'angle entre la
       route et le lit du vent. Pas une polaire de coque : une moyenne de
       journées de mer, quarts de nuit et petit temps compris. Au près on
       louvoie — on fait quatre nœuds sur l'eau à soixante degrés du vent, deux
       sur la route ; grand largue est l'allure reine ; plein vent arrière, la
       misaine est déventée par la grand-voile et l'on perd un peu. */
    static readonly (double Off, double Knots)[] Made =
    {
        (0, 2.0), (45, 2.2), (60, 3.6), (90, 5.0), (135, 5.5), (180, 4.6)
    };

    /// <summary>
    /// Les nœuds faits sur la route <paramref name="course"/> par un vent qui
    /// vient de <paramref name="windFrom"/> (degrés vrais tous deux).
    /// </summary>
    public static double Knots(double course, double windFrom)
    {
        double off = Math.Abs((course - windFrom + 540) % 360 - 180);
        for (int k = 1; k < Made.Length; k++)
            if (off <= Made[k].Off)
            {
                var (a, b) = (Made[k - 1], Made[k]);
                return a.Knots + (b.Knots - a.Knots) * (off - a.Off) / (b.Off - a.Off);
            }
        return Made[^1].Knots;
    }

    /// <summary>
    /// Le plan d'une traversée vers <paramref name="to"/> depuis le point
    /// (<paramref name="lat"/>, <paramref name="lon"/>) : l'atterrage le plus
    /// proche, les milles, la route et les heures. Nul si la région n'a pas
    /// d'atterrage — elle ne s'atteint alors pas par mer.
    /// </summary>
    public static Plan? PlanTo(RegionSpec to, double lat, double lon, double windFrom)
    {
        ApproachSpec? best = null;
        double miles = double.MaxValue;
        foreach (var a in to.Approaches)
        {
            double d = Miles(lat, lon, a.Lat, a.Lon);
            if (d < miles) { miles = d; best = a; }
        }
        if (best == null) return null;
        double course = Bearing(lat, lon, best.Lat, best.Lon);
        double knots = Knots(course, windFrom);
        return new Plan(to, best, miles, course, knots, miles / knots);
    }

    /// <summary>« 4 jours et 9 heures », « 17 heures » : une durée dite comme au bord.</summary>
    public static string Say(double hours)
    {
        int h = (int)Math.Round(hours);
        int d = h / 24; h %= 24;
        string H(int n) => n == 1 ? "1 heure" : $"{n} heures";
        if (d == 0) return H(Math.Max(1, h));
        string D = d == 1 ? "1 jour" : $"{d} jours";
        return h == 0 ? D : $"{D} et {H(h)}";
    }

    /// <summary>La rose en seize aires, pour dire une route sans chiffre.</summary>
    public static string Point(double deg)
    {
        string[] r =
        {
            "nord", "nord-nord-est", "nord-est", "est-nord-est", "est", "est-sud-est", "sud-est", "sud-sud-est",
            "sud", "sud-sud-ouest", "sud-ouest", "ouest-sud-ouest", "ouest", "ouest-nord-ouest", "nord-ouest", "nord-nord-ouest"
        };
        return r[(int)Math.Round(((deg % 360) + 360) % 360 / 22.5) % 16];
    }

    /// <summary>« au nord-est », « à l'ouest » : la route, avec son article.</summary>
    public static string ToPoint(double deg)
    {
        string p = Point(deg);
        return p[0] == 'e' || p[0] == 'o' ? "à l'" + p : "au " + p;
    }

    /// <summary>« du nord », « d'est » : d'où vient le vent.</summary>
    public static string FromPoint(double deg)
    {
        string p = Point(deg);
        return p[0] == 'e' || p[0] == 'o' ? "d'" + p : "du " + p;
    }
}

/// <summary>Une traversée calculée : où, combien de milles, à quelle route, combien de temps.</summary>
public sealed record Plan(RegionSpec To, ApproachSpec At, double Miles, double Course, double Knots, double Hours);
