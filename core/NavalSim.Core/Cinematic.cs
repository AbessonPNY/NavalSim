using System.Text.Json;

namespace NavalSim.Core;

/// <summary>
/// LA CINÉMATIQUE D'UN CHAPITRE, telle que <c>quests/histoire.json</c> l'écrit
/// (« cinematique ») : un naufrage, puis un plan d'ensemble sur un port. Le moteur
/// la met en scène (Godot : ShipDemo.Story.cs) ; ici, seulement ce qu'elle dit.
/// </summary>
public sealed class CinematicSpec
{
    public WreckShot? Wreck;
    public EstablishShot? Establish;

    public static CinematicSpec? FromJson(JsonElement e)
    {
        if (e.ValueKind != JsonValueKind.Object) return null;
        var c = new CinematicSpec();
        if (e.TryGetProperty("naufrage", out var w) && w.ValueKind == JsonValueKind.Object)
            c.Wreck = new WreckShot
            {
                /* LE NAVIRE : son numéro dans ships/index.json (7 : la Roter Löwe),
                   ou le nom de sa fiche — l'un ou l'autre, ce qu'on a sous la main. */
                Ship = w.TryGetProperty("navire", out var n)
                    ? n.ValueKind == JsonValueKind.Number ? n.GetInt32().ToString(System.Globalization.CultureInfo.InvariantCulture)
                    : n.GetString() ?? "" : "",
                Lat = Js.Num(w, "lat", 17.80), Lon = Js.Num(w, "lon", -76.88),
                Hour = Js.Num(w, "heure", 23.5), Force = Js.Num(w, "force", 10),
                Duration = Js.Num(w, "duree", 70),
                Music = Js.Str(w, "musique"), Volume = Js.Num(w, "volume", 2)
            };
        if (e.TryGetProperty("etablissement", out var p) && p.ValueKind == JsonValueKind.Object)
            c.Establish = new EstablishShot
            {
                Port = Js.Str(p, "port"), Hour = Js.Num(p, "heure", 6.5),
                Duration = Js.Num(p, "duree", 24), Radius = Js.Num(p, "rayon", 420),
                Height = Js.Num(p, "hauteur", 90), Sweep = Js.Num(p, "balayage", 110),
                Music = Js.Str(p, "musique"), Volume = Js.Num(p, "volume", 1)
            };
        return c.Wreck == null && c.Establish == null ? null : c;
    }
}

/// <summary>Le naufrage : quel navire, où, à quelle heure, par quel temps, combien de temps au plus, sur quelle musique.</summary>
public sealed class WreckShot
{
    public string Ship = "";
    public double Lat, Lon, Hour, Force, Duration, Volume;
    public string Music = "";
}

/// <summary>Le plan d'ensemble : un port vu du ciel en arc de cercle, à son heure.</summary>
public sealed class EstablishShot
{
    public string Port = "";
    public double Hour, Duration, Radius, Height, Sweep, Volume;
    public string Music = "";
}
