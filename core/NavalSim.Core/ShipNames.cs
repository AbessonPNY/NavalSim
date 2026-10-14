using System;
using System.Collections.Generic;
using System.Text.Json;

namespace NavalSim.Core;

/// <summary>
/// LES NOMS DE BAPTÊME — ships/noms.json.
///
/// Une fiche décrit un TYPE de bâtiment, pas un bâtiment : dix sloops des Antilles
/// sortent du même plan, et aucun ne s'appelait « Sloop Experiment 1677 ». Le nom se
/// donne donc à la mise à l'eau, tiré dans la liste de la nation sous laquelle la
/// coque paraît — un galion espagnol s'appelle Nuestra Señora del Rosario, pas
/// Speedwell. Une fiche marquée <c>"unique"</c> est au contraire UN navire (le
/// Roebuck de Dampier) et garde le nom de sa fiche.
///
/// Le tirage ne choisit jamais un nom déjà à flot (<c>taken</c>) tant que la liste
/// en a d'autres : deux Speedwell dans la même rade se liraient comme une faute.
/// </summary>
public sealed class ShipNames
{
    /// <summary>La clé de la liste des forbans, quel que soit le pavillon noir de flags.json.</summary>
    public const string Pirate = "pirate";

    readonly Dictionary<string, List<string>> _lists = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>La liste prise quand la nation est inconnue ou n'a pas de liste.</summary>
    public string Fallback = "angleterre";
    /// <summary>La couleur des lettres peintes (0xrrggbb), quand la fiche ne dit pas la sienne.</summary>
    public string Ink = "0xd8b25a";
    /// <summary>Les lettres en capitales, comme on les peignait.</summary>
    public bool Capitals = true;
    /// <summary>Les polices du système à essayer, dans l'ordre.</summary>
    public readonly List<string> Fonts = new();

    public IReadOnlyCollection<string> Nations => _lists.Keys;
    public int Count(string nation) => _lists.TryGetValue(nation, out var l) ? l.Count : 0;

    public static ShipNames FromJson(JsonElement root)
    {
        var n = new ShipNames();
        if (root.TryGetProperty("nations", out var nat) && nat.ValueKind == JsonValueKind.Object)
            foreach (var p in nat.EnumerateObject())
            {
                if (p.Value.ValueKind != JsonValueKind.Array) continue;
                var list = new List<string>();
                foreach (var v in p.Value.EnumerateArray())
                    if (v.ValueKind == JsonValueKind.String && v.GetString()!.Trim() is { Length: > 0 } s && !list.Contains(s))
                        list.Add(s);
                if (list.Count > 0) n._lists[p.Name] = list;
            }
        if (root.Str("defaut") is { Length: > 0 } d) n.Fallback = d;
        if (root.TryGetProperty("lettrage", out var l) && l.ValueKind == JsonValueKind.Object)
        {
            if (l.Str("encre") is { Length: > 0 } ink) n.Ink = ink;
            if (l.TryGetProperty("capitales", out var c) && c.ValueKind is JsonValueKind.True or JsonValueKind.False)
                n.Capitals = c.GetBoolean();
            if (l.TryGetProperty("polices", out var f) && f.ValueKind == JsonValueKind.Array)
                foreach (var v in f.EnumerateArray())
                    if (v.ValueKind == JsonValueKind.String) n.Fonts.Add(v.GetString()!);
        }
        return n;
    }

    /// <summary>
    /// Un nom pour une coque de <paramref name="nation"/> (un identifiant de flags.json,
    /// ou <see cref="Pirate"/>). Rien si aucune liste n'existe. Tous pris : le premier tiré,
    /// qu'on accepte en double plutôt que de laisser un navire sans nom.
    /// </summary>
    public string? Draw(string? nation, Func<double> random, Func<string, bool>? taken = null)
    {
        if (!(nation != null && _lists.TryGetValue(nation, out var list))
            && !_lists.TryGetValue(Fallback, out list))
        {
            // ni la nation ni le repli : n'importe quelle liste honnête
            list = null;
            foreach (var (k, l) in _lists) if (k != Pirate) { list = l; break; }
            if (list == null) return null;
        }
        int start = Math.Min((int)(random() * list.Count), list.Count - 1);
        if (taken != null)
            for (int i = 0; i < list.Count; i++)
            {
                var s = list[(start + i) % list.Count];
                if (!taken(s)) return s;
            }
        return list[start];
    }

    // ------------------------------------------------------------------
    //  LE NOM DANS UNE PHRASE
    // ------------------------------------------------------------------
    /* L'usage français : un nom qui porte son article le garde et le contracte
       (« le Saint-Louis », « du Saint-Louis », « de la Bonne Espérance ») ; un nom
       étranger prend le masculin (« le Speedwell », « du San José »), comme on
       écrit « le Victory ». */

    static bool Starts(string s, string art) => s.StartsWith(art, StringComparison.OrdinalIgnoreCase);

    /// <summary>Sujet ou complément direct : « le Speedwell », « la Bonne Espérance ».</summary>
    public static string Le(string name)
    {
        if (Starts(name, "Le ") || Starts(name, "La ") || Starts(name, "Les ") || Starts(name, "L'") || Starts(name, "L’"))
            return char.ToLowerInvariant(name[0]) + name[1..];
        return "le " + name;
    }

    /// <summary>Complément en « de » : « du Speedwell », « de la Bonne Espérance », « des Trois Frères ».</summary>
    public static string Du(string name)
    {
        if (Starts(name, "Le ")) return "du " + name[3..];
        if (Starts(name, "Les ")) return "des " + name[4..];
        if (Starts(name, "La ") || Starts(name, "L'") || Starts(name, "L’")) return "de " + Le(name);
        return "du " + name;
    }

    /// <summary>Complément en « à » : « au Speedwell », « à la Bonne Espérance ».</summary>
    public static string Au(string name)
    {
        if (Starts(name, "Le ")) return "au " + name[3..];
        if (Starts(name, "Les ")) return "aux " + name[4..];
        if (Starts(name, "La ") || Starts(name, "L'") || Starts(name, "L’")) return "à " + Le(name);
        return "au " + name;
    }

    /// <summary>En tête de phrase.</summary>
    public static string Cap(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];

    /// <summary>
    /// Le nom d'une fiche sans son millésime : « La Boussole 1597 » donne « La Boussole ».
    /// Le millésime dit QUEL navire c'est dans un menu ; en jeu, en 1690, il ne se dit pas.
    /// </summary>
    public static (string Label, int Year) SplitYear(string name)
    {
        if (name.Length > 5 && int.TryParse(name.AsSpan(name.Length - 4), out int fin)
            && fin > 1000 && fin < 2100 && name[^5] == ' ')
            return (name[..^5], fin);
        return (name, 0);
    }
}
