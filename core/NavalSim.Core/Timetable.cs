using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;

namespace NavalSim.Core;

/// <summary>
/// LES HORAIRES D'UNE RADE — world/horaires/&lt;région&gt;.json, format dans
/// world/README.md (« Les horaires d'une rade »). Pour chaque port : des POSTES
/// numérotés, chacun au bout d'un ponton de la fiche ; des DÉPARTS, qui quittent
/// un poste à une heure du ciel pour un autre port ou le large ; des ARRIVÉES,
/// qui se mettent en route de l'autre bout à leur heure et viennent à un poste.
/// Chaque ligne nomme ses navires candidats : on en tire un.
///
/// Un fichier à part, et non la fiche du monde : c'est une liste qu'on retouche
/// souvent, ligne à ligne, et qui n'a rien à voir avec la terre.
/// </summary>
public sealed class Timetable
{
    public readonly Dictionary<string, PortTimetable> Ports = new();

    public static Timetable FromJson(string json)
    {
        var t = new Timetable();
        using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        foreach (var p in doc.RootElement.EnumerateObject())
        {
            if (p.Name.StartsWith('_') || p.Value.ValueKind != JsonValueKind.Object) continue;
            var pt = new PortTimetable { Key = p.Name };
            if (p.Value.TryGetProperty("postes", out var po) && po.ValueKind == JsonValueKind.Array)
                foreach (var b in po.EnumerateArray())
                    pt.Berths.Add(new TripBerth
                    {
                        N = (int)b.Num("n"), Pier = (int)b.Num("ponton", -1), Name = b.Str("nom"),
                        X = b.Opt("x"), Z = b.Opt("z"), Gap = b.Num("ecart", 15)
                    });
            Read(p.Value, "departs", pt, arrival: false);
            Read(p.Value, "arrivees", pt, arrival: true);
            t.Ports[p.Name] = pt;
        }
        return t;
    }

    static void Read(JsonElement port, string key, PortTimetable pt, bool arrival)
    {
        if (!port.TryGetProperty(key, out var a) || a.ValueKind != JsonValueKind.Array) return;
        int i = 0;
        foreach (var e in a.EnumerateArray())
        {
            i++;
            var s = new Sailing
            {
                Arrival = arrival, Index = i,
                Hour = e.TryGetProperty("heure", out var h) ? Hour(h) : double.NaN,
                Berth = (int)e.Num("poste", -1),
                Other = arrival ? e.Str("de") : e.Str("vers"),
                Beyond = arrival ? e.Str("origine") : e.Str("puis"),
                Lat = e.Opt("lat"), Lon = e.Opt("lon"),
                Lead = e.Num("avance", 1)
            };
            if (s.Other.Length == 0) s.Other = "large";
            if (e.TryGetProperty("fiches", out var f) && f.ValueKind == JsonValueKind.Array)
                foreach (var x in f.EnumerateArray()) if (x.GetString() is { Length: > 0 } k) s.Ships.Add(k);
            if (!double.IsNaN(s.Hour)) pt.Sailings.Add(s);
        }
    }

    /// <summary>« 06:30 » ou 6.5 : une heure du ciel, de 0 à 24.</summary>
    public static double Hour(JsonElement h)
    {
        if (h.ValueKind == JsonValueKind.Number) return ((h.GetDouble() % 24) + 24) % 24;
        var s = h.GetString() ?? "";
        var parts = s.Split(':', 'h', 'H');
        if (parts.Length == 0 || !double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double hh)) return double.NaN;
        double mm = parts.Length > 1 && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double m) ? m : 0;
        return (((hh + mm / 60) % 24) + 24) % 24;
    }

    /// <summary>L'heure écrite comme on la lit : « 06:30 ».</summary>
    public static string Clock(double h)
    {
        int m = (int)Math.Round(h * 60) % (24 * 60);
        return $"{m / 60:00}:{m % 60:00}";
    }

    /// <summary>De combien d'heures <paramref name="now"/> a passé <paramref name="h"/>, sur le cadran (0 ≤ … &lt; 24).</summary>
    public static double Since(double h, double now) => ((now - h) % 24 + 24) % 24;
}

/// <summary>Les postes et les mouvements d'un port.</summary>
public sealed class PortTimetable
{
    public string Key = "";
    public readonly List<TripBerth> Berths = new();
    public readonly List<Sailing> Sailings = new();
    public TripBerth? BerthN(int n) => Berths.Find(b => b.N == n);
}

/// <summary>
/// UN POSTE : la place d'un navire au bout d'un ponton de la fiche (son numéro dans
/// « pontons », celui que le mode création affiche au-dessus de lui), à
/// <see cref="Gap"/> mètres au-delà du musoir — ou en un point écrit (x, z).
/// </summary>
public sealed class TripBerth
{
    public int N, Pier = -1;
    public string Name = "";
    public double? X, Z;
    public double Gap = 15;
}

/// <summary>Un départ ou une arrivée, à heure fixe.</summary>
public sealed class Sailing
{
    public bool Arrival;
    /// <summary>Son rang dans sa liste (1, 2…) : son nom dans le journal.</summary>
    public int Index;
    public double Hour;
    public int Berth = -1;
    /// <summary>L'autre bout : une clé de port, ou « large ».</summary>
    public string Other = "large";
    /// <summary>Au-delà du large : où l'on va (départ, « puis ») ou d'où l'on vient (arrivée, « origine »).</summary>
    public string Beyond = "";
    /// <summary>Le lieu réel de <see cref="Beyond"/>, vers lequel un départ continue de faire route.</summary>
    public double? Lat, Lon;
    /// <summary>Les navires candidats ; vide : ceux du réglage « trafic ».</summary>
    public readonly List<string> Ships = new();
    /// <summary>Un départ : combien d'heures avant il est à quai, à attendre son heure.</summary>
    public double Lead = 1;

    public string Name => (Arrival ? "arrivée " : "départ ") + Index;
}

/// <summary>
/// LES PAVILLONS ADMIS DANS UN PORT — world/pavillons-des-ports.json, format dans world/README.md
/// (« Les pavillons d'un port »). Par clé de port, les nations (identifiants de flags.json) qui
/// peuvent en partir et y venir ; un port qui n'y est pas les admet toutes. Demandé : « aucun
/// navire espagnol ou français ne doit s'approcher de Port Royal ou n'en partir ».
///
/// Un fichier à part et non les horaires : c'est une règle du PORT, pas d'une ligne, et elle vaut
/// pour tout ce qui y touche, quelle que soit la région qui l'écrit.
/// </summary>
public sealed class PortNations
{
    public readonly Dictionary<string, HashSet<string>> Ports = new();

    public static PortNations FromJson(string json)
    {
        var r = new PortNations();
        using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        foreach (var p in doc.RootElement.EnumerateObject())
        {
            if (p.Name.StartsWith('_') || p.Value.ValueKind != JsonValueKind.Array) continue;
            var set = new HashSet<string>();
            foreach (var x in p.Value.EnumerateArray()) if (x.GetString() is { Length: > 0 } id) set.Add(id);
            r.Ports[p.Name] = set;
        }
        return r;
    }

    /// <summary>Ce port admet-il ce pavillon ? Un port sans règle (ou « large ») admet tout.</summary>
    public bool Admits(string port, string nation) => !Ports.TryGetValue(port, out var set) || set.Contains(nation);

    /// <summary>Le pavillon d'un trajet doit être admis À SES DEUX BOUTS : on part de l'un pour entrer dans l'autre.</summary>
    public bool Admits(string from, string to, string nation) => Admits(from, nation) && Admits(to, nation);
}
