using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace NavalSim.Core;

/// <summary>Un trait de plume : une polyligne, en mètres MONDE VRAIS.</summary>
public sealed class Stroke
{
    public readonly List<(double X, double Z)> Pts = new();
    /// <summary>L'encre : 0 la sépia du bord, 1 le rouge du danger, 2 le bleu d'une route.</summary>
    public int Ink;
}

/// <summary>
/// UNE CROIX, portée d'après ce qu'on a APPRIS et non d'après ce qu'on a vu :
/// la carte d'une bouteille, un lieu qu'un mourant a griffonné. Elle porte la
/// clé de la chose qu'elle désigne, pour qu'on puisse la rayer le jour où on
/// l'a ramassée.
/// </summary>
public sealed class Cross
{
    public double X, Z;
    public string Text = "", Key = "";
}

/// <summary>Un mot écrit sur la carte, à l'endroit qu'il désigne.</summary>
public sealed class Note
{
    public double X, Z;
    public string Text = "";
}

/// <summary>
/// LE CARNET DU CAPITAINE — ce que ce bord a VU, et ce que sa main a écrit.
///
/// La carte de sa chambre est vierge au premier jour, et c'est le point : elle
/// ne se remplit que de ce qu'on est allé voir. Rien ici ne connaît le monde —
/// on n'y garde que des positions en mètres vrais, si bien que le même carnet
/// vaudrait pour une autre région, et que la carte peut être redessinée à
/// n'importe quelle échelle sans rien perdre.
///
/// ON GARDE LES TRAITS, PAS UNE IMAGE. Une image de carte annotée serait
/// lourde, liée à sa résolution, et impossible à relire ; une liste de
/// polylignes se redessine nette à tout moment, se corrige, et tient dans un
/// fichier qu'un humain peut ouvrir.
/// </summary>
public sealed class Logbook
{
    /// <summary>Tous les combien on pose un point de route, en mètres.</summary>
    public const double Step = 220;
    /// <summary>Ce qu'un point de route découvre autour de lui : l'horizon d'une vigie.</summary>
    public const double Sight = 2600;

    public readonly List<(double X, double Z)> Track = new();
    public readonly List<string> Ports = new();
    public readonly List<Stroke> Strokes = new();
    public readonly List<Note> Notes = new();
    public readonly List<Cross> Crosses = new();

    /// <summary>Vrai si ce relevé a ajouté un point — donc si la carte a changé.</summary>
    public bool Sail(double x, double z)
    {
        if (Track.Count > 0)
        {
            var (lx, lz) = Track[^1];
            if ((x - lx) * (x - lx) + (z - lz) * (z - lz) < Step * Step) return false;
        }
        Track.Add((x, z));
        return true;
    }

    /// <summary>Un port touché : il se nomme sur la carte, et une fois seulement.</summary>
    public bool Touch(string key)
    {
        if (string.IsNullOrEmpty(key) || Ports.Contains(key)) return false;
        Ports.Add(key);
        return true;
    }

    /// <summary>Ce point a-t-il été vu depuis la route ? C'est ce qui perce le voile.</summary>
    public bool Seen(double x, double z)
    {
        foreach (var (tx, tz) in Track)
            if ((x - tx) * (x - tx) + (z - tz) * (z - tz) < Sight * Sight) return true;
        return false;
    }

    /// <summary>Porter une croix. La même clé deux fois ne la porte qu'une.</summary>
    public bool Mark(double x, double z, string text, string key)
    {
        if (key.Length > 0 && Crosses.Exists(c => c.Key == key)) return false;
        Crosses.Add(new Cross { X = x, Z = z, Text = text, Key = key });
        return true;
    }

    /// <summary>La rayer : ce qu'elle désignait a été trouvé.</summary>
    public bool Strike(string key)
    {
        int i = Crosses.FindIndex(c => c.Key == key);
        if (i < 0) return false;
        Crosses.RemoveAt(i);
        return true;
    }

    public Stroke Begin(int ink)
    {
        var s = new Stroke { Ink = ink };
        Strokes.Add(s);
        return s;
    }

    /// <summary>Le dernier trait, effacé — la gomme d'un carnet est un coup de canif.</summary>
    public bool Undo()
    {
        if (Strokes.Count == 0) return false;
        Strokes.RemoveAt(Strokes.Count - 1);
        return true;
    }

    // ------------------------------------------------------------------
    /* EN JSON, ET LISIBLE. Un carnet qu'on ne peut pas ouvrir dans un éditeur
       est un carnet dont on ne sait pas s'il a bien retenu : le projet écrit
       déjà ses réglages, ses fiches et son monde en clair, et celui-ci ne fera
       pas exception. Les nombres sont arrondis au décimètre — une carte au
       millième de millimètre ne dit rien de plus et pèse trois fois. */

    public string ToJson()
    {
        var sb = new StringBuilder();
        var ci = CultureInfo.InvariantCulture;
        sb.Append("{\n  \"track\": [");
        for (int i = 0; i < Track.Count; i++)
            sb.Append(i > 0 ? ", " : "").Append('[').Append(Track[i].X.ToString("F1", ci))
              .Append(", ").Append(Track[i].Z.ToString("F1", ci)).Append(']');
        sb.Append("],\n  \"ports\": [");
        for (int i = 0; i < Ports.Count; i++)
            sb.Append(i > 0 ? ", " : "").Append(JsonSerializer.Serialize(Ports[i]));
        sb.Append("],\n  \"strokes\": [");
        for (int i = 0; i < Strokes.Count; i++)
        {
            sb.Append(i > 0 ? ",\n    " : "\n    ").Append("{ \"ink\": ").Append(Strokes[i].Ink).Append(", \"pts\": [");
            var p = Strokes[i].Pts;
            for (int k = 0; k < p.Count; k++)
                sb.Append(k > 0 ? ", " : "").Append('[').Append(p[k].X.ToString("F1", ci))
                  .Append(", ").Append(p[k].Z.ToString("F1", ci)).Append(']');
            sb.Append("] }");
        }
        sb.Append("],\n  \"crosses\": [");
        for (int i = 0; i < Crosses.Count; i++)
            sb.Append(i > 0 ? ",\n    " : "\n    ")
              .Append("{ \"x\": ").Append(Crosses[i].X.ToString("F1", ci))
              .Append(", \"z\": ").Append(Crosses[i].Z.ToString("F1", ci))
              .Append(", \"key\": ").Append(JsonSerializer.Serialize(Crosses[i].Key))
              .Append(", \"text\": ").Append(JsonSerializer.Serialize(Crosses[i].Text)).Append(" }");
        sb.Append("],\n  \"notes\": [");
        for (int i = 0; i < Notes.Count; i++)
            sb.Append(i > 0 ? ",\n    " : "\n    ")
              .Append("{ \"x\": ").Append(Notes[i].X.ToString("F1", ci))
              .Append(", \"z\": ").Append(Notes[i].Z.ToString("F1", ci))
              .Append(", \"text\": ").Append(JsonSerializer.Serialize(Notes[i].Text)).Append(" }");
        sb.Append("]\n}\n");
        return sb.ToString();
    }

    public static Logbook FromJson(string json)
    {
        var b = new Logbook();
        try
        {
            using var doc = JsonDocument.Parse(json);
            var r = doc.RootElement;
            if (r.TryGetProperty("track", out var tr))
                foreach (var p in tr.EnumerateArray())
                    b.Track.Add((p[0].GetDouble(), p[1].GetDouble()));
            if (r.TryGetProperty("ports", out var po))
                foreach (var p in po.EnumerateArray())
                    if (p.GetString() is string k) b.Ports.Add(k);
            if (r.TryGetProperty("strokes", out var st))
                foreach (var s in st.EnumerateArray())
                {
                    var one = new Stroke { Ink = s.TryGetProperty("ink", out var ik) ? ik.GetInt32() : 0 };
                    if (s.TryGetProperty("pts", out var pts))
                        foreach (var p in pts.EnumerateArray()) one.Pts.Add((p[0].GetDouble(), p[1].GetDouble()));
                    if (one.Pts.Count > 0) b.Strokes.Add(one);
                }
            if (r.TryGetProperty("crosses", out var cr))
                foreach (var c in cr.EnumerateArray())
                    b.Crosses.Add(new Cross
                    {
                        X = c.GetProperty("x").GetDouble(),
                        Z = c.GetProperty("z").GetDouble(),
                        Key = c.TryGetProperty("key", out var ck) ? ck.GetString() ?? "" : "",
                        Text = c.TryGetProperty("text", out var ct) ? ct.GetString() ?? "" : ""
                    });
            if (r.TryGetProperty("notes", out var no))
                foreach (var n in no.EnumerateArray())
                    b.Notes.Add(new Note
                    {
                        X = n.GetProperty("x").GetDouble(),
                        Z = n.GetProperty("z").GetDouble(),
                        Text = n.TryGetProperty("text", out var t) ? t.GetString() ?? "" : ""
                    });
        }
        catch (JsonException)
        {
            /* UN CARNET ILLISIBLE N'EST PAS UNE PANNE : on repart d'une carte
               vierge plutôt que d'empêcher le jeu de démarrer. */
        }
        return b;
    }
}
