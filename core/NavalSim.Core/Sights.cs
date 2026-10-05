using System;

namespace NavalSim.Core;

/// <summary>
/// LA HAUTEUR DES ASTRES — ce qu'un pilote de 1690 savait en tirer : la LATITUDE, et
/// elle seule (la longitude attendra les montres marines et les distances lunaires,
/// vers 1760 ; on la tient à l'estime).
///
/// LE SOLEIL À MIDI, au quartier de Davis (1594) : sa hauteur quand il culmine, et sa
/// déclinaison du jour lue dans les tables (le « régiment du soleil »), donnent la
/// latitude — φ = δ ± (90° − h), selon qu'il passe au sud ou au nord.
///
/// LA POLAIRE LA NUIT, à l'arbalestrille : en 1690 elle n'est pas au pôle mais à près de
/// DEUX DEGRÉS ET DEMI de lui, et tourne autour en un jour sidéral. Sa hauteur vaut la
/// latitude à cette distance près, que les pilotes corrigeaient d'après la position des
/// « Gardes » (β et γ de la Petite Ourse) — ici, la même correction, calculée :
/// φ ≈ h − p·cos(angle horaire). Sa position de 1690 se tire de celle de 2000 par la
/// PRÉCESSION des équinoxes (les angles de l'UAI 1976), sans rien d'écrit à la main.
/// </summary>
public static class Sights
{
    const double Rad = Math.PI / 180;

    /// <summary>La hauteur du centre du soleil, en degrés, à cette latitude, cette déclinaison et cette heure solaire locale.</summary>
    public static double SunAltitude(double lat, double decl, double dayTimeHours)
    {
        double H = (dayTimeHours - 12) * 15 * Rad;
        double s = Math.Sin(lat * Rad) * Math.Sin(decl * Rad) + Math.Cos(lat * Rad) * Math.Cos(decl * Rad) * Math.Cos(H);
        return Math.Asin(Math.Clamp(s, -1, 1)) / Rad;
    }

    /// <summary>Le soleil culmine-t-il au sud de l'observateur (vrai quand la latitude passe la déclinaison).</summary>
    public static bool SunSouth(double lat, double decl) => lat > decl;

    /// <summary>La latitude d'une hauteur méridienne du soleil : φ = δ + (90° − h) s'il passe au sud, δ − (90° − h) s'il passe au nord.</summary>
    public static double LatitudeFromSun(double h, double decl, bool south) => south ? decl + (90 - h) : decl - (90 - h);

    /// <summary>
    /// La Polaire, ascension droite et déclinaison en degrés, pour cette année : celle de
    /// J2000 (α 37,9529°, δ 89,2641°) portée à l'époque par la précession (UAI 1976).
    /// Le mouvement propre (quelques secondes d'arc par siècle) est négligé.
    /// </summary>
    public static (double Ra, double Dec) Polaris(double year) => Precess(37.95290, 89.26410, year);

    /// <summary>
    /// Un astre de J2000 (ascension droite et déclinaison en degrés) porté à cette année par
    /// la précession des équinoxes (UAI 1976). Le mouvement propre, s'il compte, s'ajoute avant.
    /// </summary>
    public static (double Ra, double Dec) Precess(double raDeg, double decDeg, double year)
    {
        double a0 = raDeg * Rad, d0 = decDeg * Rad;
        double T = (year - 2000) / 100.0;
        double As = Math.PI / (180 * 3600);
        double zeta = (2306.2181 * T + 0.30188 * T * T + 0.017998 * T * T * T) * As;
        double z = (2306.2181 * T + 1.09468 * T * T + 0.018203 * T * T * T) * As;
        double th = (2004.3109 * T - 0.42665 * T * T - 0.041833 * T * T * T) * As;
        double A = Math.Cos(d0) * Math.Sin(a0 + zeta);
        double B = Math.Cos(th) * Math.Cos(d0) * Math.Cos(a0 + zeta) - Math.Sin(th) * Math.Sin(d0);
        double C = Math.Sin(th) * Math.Cos(d0) * Math.Cos(a0 + zeta) + Math.Cos(th) * Math.Sin(d0);
        double ra = (Math.Atan2(A, B) + z) / Rad;
        double dec = Math.Asin(Math.Clamp(C, -1, 1)) / Rad;
        return ((ra % 360 + 360) % 360, dec);
    }

    /// <summary>
    /// L'angle horaire local d'un astre d'ascension droite <paramref name="ra"/>, en degrés : le
    /// temps sidéral du lieu moins son ascension droite. <paramref name="date"/> est le jour
    /// (sa date seule compte), <paramref name="solarHours"/> l'heure solaire locale, et
    /// <paramref name="lon"/> la longitude (est positive).
    /// </summary>
    public static double HourAngle(DateTime date, double solarHours, double lon, double ra)
    {
        double ut = solarHours - lon / 15.0;                                  // l'heure de Greenwich
        var j2000 = new DateTime(2000, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        double d = (date.Date - j2000.Date).TotalDays - 0.5 + ut / 24.0;      // jours depuis J2000,0
        double gmst = 280.46061837 + 360.98564736629 * d;
        double lha = gmst + lon - ra;
        return (lha % 360 + 360) % 360;
    }

    /// <summary>La hauteur d'un astre de déclinaison <paramref name="dec"/> à cet angle horaire, en degrés.</summary>
    public static double Altitude(double lat, double dec, double lha)
    {
        double s = Math.Sin(lat * Rad) * Math.Sin(dec * Rad) + Math.Cos(lat * Rad) * Math.Cos(dec * Rad) * Math.Cos(lha * Rad);
        return Math.Asin(Math.Clamp(s, -1, 1)) / Rad;
    }

    /// <summary>
    /// LA CORRECTION DES GARDES : ce qu'il faut ajouter à la hauteur de la Polaire pour avoir la
    /// latitude — −p·cos(angle horaire), p sa distance au pôle. Au-dessus du pôle (angle horaire
    /// nul) elle est trop haute de p ; au-dessous, trop basse ; à l'est ou à l'ouest, juste.
    /// </summary>
    public static double GuardsCorrection(double dec, double lha) => -(90 - dec) * Math.Cos(lha * Rad);

    /// <summary>Degrés en « 18° 04′ » — signé, sans point cardinal.</summary>
    public static string Dm(double deg)
    {
        double a = Math.Abs(deg);
        int d = (int)Math.Floor(a);
        int m = (int)Math.Round((a - d) * 60);
        if (m == 60) { d++; m = 0; }
        return $"{(deg < 0 ? "−" : "")}{d}° {m:00}′";
    }
}
