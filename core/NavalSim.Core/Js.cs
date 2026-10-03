using System;
using System.Text.Json;

namespace NavalSim.Core;

/// <summary>
/// L'ARITHMÉTIQUE DE LA PAGE, là où elle n'est pas celle de C#.
///
/// Les hachages de la page écrivent <c>a * b | 0</c> : JavaScript y multiplie en
/// DOUBLE, le produit dépasse 2^53 et s'arrondit AVANT d'être ramené à 32 bits.
/// Le calculer en entiers serait plus juste — et placerait les dépressions et
/// les cours ailleurs. Une seule définition pour tous ceux qui en ont besoin :
/// les dépressions, le marché.
///
/// ET LIRE UNE FICHE JSON SANS SE FÂCHER, comme la page le fait : une clé absente
/// ou d'un autre type rend la valeur par défaut, jamais une exception — une fiche
/// à moitié écrite doit encore charger. Chaque lecteur de réglages réécrivait ces
/// quatre lignes.
/// </summary>
public static class Js
{
    /// <summary>ToInt32 de JavaScript : troncature, puis le reste modulo 2^32, lu comme signé.</summary>
    public static int ToInt32(double v)
    {
        double m = Math.Truncate(v) % 4294967296.0;
        if (m < 0) m += 4294967296.0;
        return unchecked((int)(uint)m);
    }

    /// <summary><c>Math.round</c> de JavaScript : la demie monte TOUJOURS, même sous zéro.</summary>
    public static double Round(double v) => Math.Floor(v + 0.5);

    /// <summary>Le texte sous <paramref name="k"/>, ou "".</summary>
    public static string Str(this JsonElement e, string k) =>
        e.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()! : "";

    /// <summary>Le nombre sous <paramref name="k"/>, ou <paramref name="def"/>.</summary>
    public static double Num(this JsonElement e, string k, double def = 0) =>
        e.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : def;

    /// <summary>Le nombre sous <paramref name="k"/>, ou rien : pour distinguer « absent » de « zéro ».</summary>
    public static double? Opt(this JsonElement e, string k) =>
        e.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : null;

    /// <summary>Vrai seulement si <paramref name="k"/> vaut true.</summary>
    public static bool True(this JsonElement e, string k) =>
        e.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.True;
}
