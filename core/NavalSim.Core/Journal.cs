using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace NavalSim.Core;

/* Les accents sortent TELS QUELS et non en é : ce fichier vit dans
   l enregistrement de la partie, et le projet tient à ce qu un humain puisse
   l ouvrir — c est la même raison qui fait garder des traits et non une image. */

/// <summary>Un trait tracé DANS LA PAGE : ses points vont de 0 à 1 sur ses côtés.</summary>
public sealed class PageStroke
{
    public readonly List<(double X, double Y)> Pts = new();
    /// <summary>L'encre : la même échelle que le carnet de carte — 0 sépia, 1 rouge, 2 bleu.</summary>
    public int Ink;
    /// <summary>La demi-largeur du bec, en part de la largeur de page.</summary>
    public double W = 0.0015;
}

/// <summary>
/// UN BLOC D'UNE PAGE : du texte qui coule, ou une zone de dessin.
///
/// Deux choses et pas une : un journal de bord n'est pas un traitement de texte
/// avec des images, c'est une page où la main passe de l'écriture au croquis et
/// revient dessous. Le bloc est donc l'unité, et l'ordre des blocs EST la mise
/// en page — rien à positionner, rien à faire flotter.
/// </summary>
public sealed class PageBlock
{
    /// <summary>Vrai : une zone de dessin. Faux : du texte.</summary>
    public bool Drawing;
    public string Text = "";
    public readonly List<PageStroke> Strokes = new();
    /// <summary>La hauteur d'une zone de dessin, en part de la hauteur de page.</summary>
    public double Height = 0.22;
}

/// <summary>Une feuille, et le jour qu'elle porte.</summary>
public sealed class JournalPage
{
    /// <summary>La date de jeu, telle qu'on l'écrit en tête : « 8 octobre 1690 ».</summary>
    public string Date = "";
    /// <summary>Et sous forme comparable, pour ranger : aaaa-mm-jj.</summary>
    public string Key = "";
    public readonly List<PageBlock> Blocks = new();

    /// <summary>Le dernier bloc de texte, ou un neuf s'il n'y en a pas.</summary>
    public PageBlock Pen()
    {
        if (Blocks.Count > 0 && !Blocks[^1].Drawing) return Blocks[^1];
        var b = new PageBlock();
        Blocks.Add(b);
        return b;
    }
}

/// <summary>
/// LE JOURNAL DE BORD — ce que le capitaine écrit, et le peu que le bord écrit
/// pour lui.
///
/// CE N'EST PAS LE CARNET DE LA CARTE, et les deux ne doivent pas être confondus.
/// Le carnet (<see cref="Logbook"/>) est un PLAN : des traits et des mots posés à
/// des coordonnées du monde, qui valent quelle que soit l'échelle à laquelle on
/// redessine la carte. Le journal est une SUITE DE FEUILLES : le texte y coule,
/// les croquis s'y intercalent, et rien n'y désigne un lieu. Deux besoins, deux
/// modèles ; les mêler aurait donné un objet qui ne sait faire ni l'un ni l'autre.
///
/// DES PAGES LIBRES, datées à l'ouverture. Un vrai livre de bord tenait une page
/// par jour parce qu'on l'écrivait tous les jours ; ici c'est une plume qu'on
/// prend quand on veut, et forcer une page par journée de jeu aurait surtout
/// fabriqué des pages vides.
///
/// ET IL S'ÉCRIT UN PEU TOUT SEUL : une ligne par escale — appareillage,
/// mouillage, traversée d'une région à l'autre. Juste assez pour qu'un journal
/// négligé garde la trace du voyage, et assez peu pour qu'il reste le vôtre.
/// </summary>
public sealed class Journal
{
    public readonly List<JournalPage> Pages = new();

    /// <summary>La page ouverte, ou −1.</summary>
    public int At = -1;

    public JournalPage? Current => At >= 0 && At < Pages.Count ? Pages[At] : null;

    /// <summary>
    /// Ouvrir une page neuve à cette date, et s'y placer. <paramref name="date"/>
    /// est la date écrite en tête, <paramref name="key"/> sa forme rangeable.
    /// </summary>
    public JournalPage NewPage(string date, string key)
    {
        var p = new JournalPage { Date = date, Key = key };
        Pages.Add(p);
        At = Pages.Count - 1;
        return p;
    }

    /// <summary>
    /// LA PAGE OÙ ÉCRIRE MAINTENANT : la dernière si elle porte ce jour-ci, une
    /// neuve sinon. C'est par là que passe ce que le bord écrit de lui-même, de
    /// sorte qu'une escale ne vienne jamais salir la page d'hier.
    /// </summary>
    public JournalPage Today(string date, string key)
    {
        if (Pages.Count > 0 && Pages[^1].Key == key) { At = Pages.Count - 1; return Pages[^1]; }
        return NewPage(date, key);
    }

    /// <summary>
    /// UNE LIGNE DU BORD : ce que le journal écrit sans qu'on le lui demande. Elle
    /// va toujours à la fin de la page du jour, sur sa propre ligne, et jamais au
    /// milieu d'une phrase qu'on était en train d'écrire.
    /// </summary>
    public void Log(string date, string key, string line)
    {
        var p = Today(date, key);
        var b = p.Pen();
        if (b.Text.Length > 0 && !b.Text.EndsWith("\n")) b.Text += "\n";
        b.Text += line + "\n";
    }

    // ------------------------------------------------------------------
    //  ÉCRIT, RELU
    // ------------------------------------------------------------------

    public string ToJson()
    {
        using var ms = new System.IO.MemoryStream();
        using (var jw = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            jw.WriteStartObject();
            jw.WriteNumber("at", At);
            jw.WriteStartArray("pages");
            foreach (var p in Pages)
            {
                jw.WriteStartObject();
                jw.WriteString("date", p.Date);
                jw.WriteString("key", p.Key);
                jw.WriteStartArray("blocs");
                foreach (var b in p.Blocks)
                {
                    jw.WriteStartObject();
                    if (b.Drawing)
                    {
                        jw.WriteBoolean("dessin", true);
                        jw.WriteNumber("h", Math.Round(b.Height, 4));
                        jw.WriteStartArray("traits");
                        foreach (var s in b.Strokes)
                        {
                            jw.WriteStartObject();
                            jw.WriteNumber("encre", s.Ink);
                            jw.WriteNumber("w", Math.Round(s.W, 5));
                            jw.WriteStartArray("p");
                            foreach (var (x, y) in s.Pts)
                            {
                                jw.WriteNumberValue(Math.Round(x, 4));
                                jw.WriteNumberValue(Math.Round(y, 4));
                            }
                            jw.WriteEndArray();
                            jw.WriteEndObject();
                        }
                        jw.WriteEndArray();
                    }
                    else jw.WriteString("texte", b.Text);
                    jw.WriteEndObject();
                }
                jw.WriteEndArray();
                jw.WriteEndObject();
            }
            jw.WriteEndArray();
            jw.WriteEndObject();
        }
        return Encoding.UTF8.GetString(ms.ToArray());
    }

    /// <summary>Relire. Un fichier illisible laisse le journal vide plutôt que de lever.</summary>
    public bool FromJson(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var r = doc.RootElement;
            Pages.Clear();
            if (r.TryGetProperty("pages", out var ps) && ps.ValueKind == JsonValueKind.Array)
                foreach (var pe in ps.EnumerateArray())
                {
                    var p = new JournalPage
                    {
                        Date = pe.TryGetProperty("date", out var d) ? d.GetString() ?? "" : "",
                        Key = pe.TryGetProperty("key", out var k) ? k.GetString() ?? "" : "",
                    };
                    if (pe.TryGetProperty("blocs", out var bs) && bs.ValueKind == JsonValueKind.Array)
                        foreach (var be in bs.EnumerateArray())
                        {
                            var b = new PageBlock();
                            if (be.TryGetProperty("dessin", out var dr) && dr.ValueKind == JsonValueKind.True)
                            {
                                b.Drawing = true;
                                if (be.TryGetProperty("h", out var hh) && hh.ValueKind == JsonValueKind.Number)
                                    b.Height = hh.GetDouble();
                                if (be.TryGetProperty("traits", out var ts) && ts.ValueKind == JsonValueKind.Array)
                                    foreach (var te in ts.EnumerateArray())
                                    {
                                        var s = new PageStroke();
                                        if (te.TryGetProperty("encre", out var en) && en.ValueKind == JsonValueKind.Number) s.Ink = en.GetInt32();
                                        if (te.TryGetProperty("w", out var ww) && ww.ValueKind == JsonValueKind.Number) s.W = ww.GetDouble();
                                        if (te.TryGetProperty("p", out var pp) && pp.ValueKind == JsonValueKind.Array)
                                        {
                                            var flat = new List<double>();
                                            foreach (var v in pp.EnumerateArray()) flat.Add(v.GetDouble());
                                            for (int i = 0; i + 1 < flat.Count; i += 2) s.Pts.Add((flat[i], flat[i + 1]));
                                        }
                                        b.Strokes.Add(s);
                                    }
                            }
                            else b.Text = be.TryGetProperty("texte", out var tx) ? tx.GetString() ?? "" : "";
                            p.Blocks.Add(b);
                        }
                    Pages.Add(p);
                }
            At = r.TryGetProperty("at", out var a) && a.ValueKind == JsonValueKind.Number
                ? Math.Clamp(a.GetInt32(), -1, Pages.Count - 1) : Pages.Count - 1;
            return true;
        }
        catch (JsonException) { return false; }
    }
}
