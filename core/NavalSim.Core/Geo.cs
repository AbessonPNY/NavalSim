using System;

namespace NavalSim.Core;

/// <summary>
/// OÙ ELLE EST, dans les termes d'un navigateur — et l'inverse. Naval.Geo.
///
/// Un mille marin EST une minute de latitude, donc le nord se convertit
/// exactement ; l'est se resserre avec le cosinus de la latitude, ce qui est
/// vrai. Le monde est le vrai, réduit par <see cref="Scale"/> : la carte lit la
/// latitude et la longitude réelles de Port-Royal pendant que la traversée est
/// dix fois plus courte. Le soleil prend sa latitude de <see cref="Lat0"/> —
/// midi près du zénith, crépuscules brefs : les tropiques.
///
/// PAS un singleton, contrairement à la page : le monde porte le sien, et deux
/// régions peuvent donc coexister (un banc, une carte, un essai) sans qu'une
/// variable globale décide pour tout le monde.
/// </summary>
public sealed class Geo
{
    /// <summary>Mètres dans une minute de latitude.</summary>
    public const double MPerMin = 1852;

    public double Lat0 { get; }
    public double Lon0 { get; }
    public double Scale { get; }

    /// <param name="lat0">Port-Royal, tant que la région ne dit pas autre chose.</param>
    public Geo(double lat0 = 17.9375, double lon0 = -76.8411, double scale = 0.1)
    {
        Lat0 = lat0; Lon0 = lon0; Scale = scale;
    }

    /// <summary>Le point, en latitude et longitude.</summary>
    public (double Lat, double Lon) Fix(double x, double z)
    {
        double m = MPerMin * 60 * Scale;
        double lat = Lat0 + z / m;
        double c = Math.Max(0.02, Math.Cos(lat * Math.PI / 180));
        return (lat, Lon0 - x / (m * c));
    }

    /// <summary>Et l'inverse : des mètres VRAIS du monde, jamais les coordonnées locales.</summary>
    public (double X, double Z) ToXZ(double lat, double lon)
    {
        double m = MPerMin * 60 * Scale;
        return (-(lon - Lon0) * m * Math.Max(0.02, Math.Cos(lat * Math.PI / 180)), (lat - Lat0) * m);
    }

    /// <summary>Degrés et minutes décimales, comme s'écrivent une carte et un journal de bord.</summary>
    public static string Format(double deg, bool isLat)
    {
        string hemi = isLat ? (deg >= 0 ? "N" : "S") : (deg >= 0 ? "E" : "O");
        double a = Math.Abs(deg);
        int d = (int)Math.Floor(a);
        double m = (a - d) * 60;
        return d.ToString(isLat ? "00" : "000", System.Globalization.CultureInfo.InvariantCulture) + "°"
             + (m < 10 ? "0" : "")
             // toFixed de JS arrondit au PLUS LOIN de zero ; « F1 » de .NET arrondit au
             // pair, et 56,25 minutes sortait a 56,2 la ou la page ecrit 56,3
             + Math.Round(m, 1, MidpointRounding.AwayFromZero)
                   .ToString("F1", System.Globalization.CultureInfo.InvariantCulture) + "' " + hemi;
    }
}
