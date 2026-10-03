using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NavalSim.Core;

/// <summary>Une sorte de trésor : ce qu'on trouve dans un coffre, et ce qu'il vaut.</summary>
public sealed class TreasureKind
{
    public string Key = "", Name = "", Unit = "", Units = "", Note = "", Glb = "";
    /// <summary>« un » ou « une » : une pierre, un bijou.</summary>
    public string Article = "un";
    /// <summary>Le cours moyen d'une pièce, en écus.</summary>
    public double Value;
    /// <summary>À la bourse tout de suite (les écus d'or) plutôt qu'à bord, à vendre.</summary>
    public bool ToPurse;
    /// <summary>Combien un coffre en renferme, au plus et au moins ; un pirate en a davantage.</summary>
    public int Min, Max;
    public double Pirate = 1;
}

/// <summary>Ce qu'un coffre renferme d'une sorte.</summary>
public sealed class Loot
{
    [JsonPropertyName("sorte")] public string Kind { get; set; } = "";
    [JsonPropertyName("nombre")] public int Count { get; set; }
}

/// <summary>
/// LES TRÉSORS DES ÉPAVES — treasure/tresor.json. Des écus d'or, qui vont à la
/// bourse ; des pierres précieuses, des bijoux et des objets anciens en or, qui
/// restent à bord et se VENDENT au comptoir (demandé) : un rubis ne se dépense
/// pas, il se négocie, et mieux dans un port que dans l'autre.
///
/// PAS AU COMPTOIR DES DENRÉES, et ce n'est pas un détail : celui-là compte à la
/// tonne et se partage avec la page, que son banc de parité tient au bit près.
/// Les trésors se comptent À LA PIÈCE, ont leur propre manifeste, et leur cours
/// suit la MÊME courbe que les denrées — une fonction pure du couple
/// trésor/port et de l'heure, oscillant de 0,55 à 1,45 —, sans rien toucher de
/// ce que la page partage.
/// </summary>
public sealed class TreasureBook
{
    public readonly List<TreasureKind> Kinds = new();
    /// <summary>Le modèle du coffre, s'il y en a un ; sinon il est dessiné.</summary>
    public string ChestGlb = "";
    /// <summary>La marge du joaillier sur ce qu'il rachète.</summary>
    public double Marge = 0.15;

    public TreasureKind? ByKey(string key) => Kinds.Find(k => k.Key == key);

    public static TreasureBook FromJson(string text)
    {
        var book = new TreasureBook();
        using var doc = JsonDocument.Parse(text, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        var root = doc.RootElement;
        if (root.TryGetProperty("coffre", out var c) && c.TryGetProperty("glb", out var cg)) book.ChestGlb = cg.GetString() ?? "";
        if (root.TryGetProperty("marge", out var m) && m.ValueKind == JsonValueKind.Number) book.Marge = m.GetDouble();
        if (root.TryGetProperty("contenus", out var list) && list.ValueKind == JsonValueKind.Array)
            foreach (var e in list.EnumerateArray())
            {
                string S(string n) => e.Str(n);
                double D(string n, double d) => e.Num(n, d);
                var k = new TreasureKind
                {
                    Key = S("key"), Name = S("nom"), Unit = S("unite"), Units = S("unites"), Note = S("note"), Glb = S("glb"),
                    Article = S("article") is { Length: > 0 } art ? art : "un",
                    Value = D("valeur", 1), Min = (int)D("min", 0), Max = (int)D("max", 0), Pirate = D("pirate", 1),
                    ToPurse = e.TryGetProperty("bourse", out var b) && b.ValueKind == JsonValueKind.True
                };
                if (k.Key.Length > 0) book.Kinds.Add(k);
            }
        return book;
    }

    static double Seed(string s)
    {
        uint g = 2166136261;
        foreach (char ch in s) g = (g ^ ch) * 16777619;
        return (g & 0xFFFFFF) / 16777215.0;
    }

    /// <summary>
    /// CE QUE RENFERME UN COFFRE, tiré une fois pour toutes sur l'identité de
    /// l'épave : la même épave rend le même coffre d'une partie à l'autre. Les écus
    /// sont ceux que vaut l'épave (WreckRegistry.Value) ; le reste se tire entre son
    /// minimum et son maximum, un pirate portant davantage de ce qu'il a pris.
    /// </summary>
    public List<Loot> Draw(string wreckId, long ecus, bool pirate)
    {
        var loot = new List<Loot>();
        foreach (var k in Kinds)
        {
            int n;
            if (k.ToPurse) n = (int)Math.Min(int.MaxValue, ecus);
            else
            {
                double u = Seed(wreckId + "/" + k.Key);
                double max = k.Max * (pirate ? k.Pirate : 1);
                n = (int)Math.Floor(k.Min + (max - k.Min + 1) * u);
            }
            if (n > 0) loot.Add(new Loot { Kind = k.Key, Count = n });
        }
        return loot;
    }

    /// <summary>Le cours d'une pièce à ce port, en pièces d'argent : la courbe des denrées, à la pièce.</summary>
    public double Price(string kind, string port, double t, double palier)
    {
        var k = ByKey(kind);
        if (k == null) return 0;
        double T = palier, n = Math.Floor(t / T), u = t / T - n;
        double e = MathX.Smooth01(u);
        string salt = "tresor/" + kind + "/" + port;
        double a = Market.Hash(salt, n), b = Market.Hash(salt, n + 1);
        return Math.Round(k.Value * Market.SousParEcu * (0.55 + 0.90 * (a + (b - a) * e)));
    }

    /// <summary>Ce que le joaillier en donne : le cours, moins sa marge.</summary>
    public double SellPrice(string kind, string port, double t, double palier) =>
        Math.Round(Price(kind, port, t, palier) * (1 - Marge));

    /// <summary>« 3 pierres précieuses », « un bijou » : le nombre et le bon mot.</summary>
    public string Say(string kind, int n)
    {
        var k = ByKey(kind);
        if (k == null) return $"{n} {kind}";
        if (k.ToPurse) return $"{n} {k.Name.ToLowerInvariant()}";
        string one = k.Unit.Length > 0 ? k.Unit : k.Name.ToLowerInvariant();
        string many = k.Units.Length > 0 ? k.Units : one + "s";
        return n == 1 ? k.Article + " " + one : $"{n} {many}";
    }
}
