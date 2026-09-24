using System;
using System.Text.Json;

namespace NavalSim.Core;

/// <summary>
/// LE MANTEAU DE NEIGE SUR UN PONT — settings.json → snow, les mêmes chiffres
/// pour la page et pour Godot.
///
/// Ce n'est pas une épaisseur en centimètres mais une COUVERTURE, de zéro (un
/// pont nu) à un (blanc partout où le ciel se voit). Elle monte à proportion de
/// ce qui tombe, plafonnée : un manteau, pas une congère. Et elle fond toujours
/// un peu — un pont qu'on foule et une mer qui l'arrose ne gardent pas la neige
/// comme un champ —, d'autant plus vite qu'il fait doux.
///
/// À CHAQUE COQUE LA SIENNE : un navire sorti de la neige la garde jusqu'à ce
/// qu'elle fonde, même s'il fait route vers le sud.
/// </summary>
public sealed class SnowSettings
{
    /// <summary>Les secondes de chute PLEINE pour un manteau complet.</summary>
    public double Coat = 600;
    /// <summary>Ce qu'elle ne dépasse pas : au-delà, ce ne serait plus un pont.</summary>
    public double Max = 0.85;
    /// <summary>Les secondes pour tout fondre au dégel nul — le froid ne l'éternise pas.</summary>
    public double Melt = 1800;
    /// <summary>La fonte qui va de soi, avant celle du thermomètre : embruns et semelles.</summary>
    public double Always = 0.5;

    public static SnowSettings FromJson(JsonElement k)
    {
        var s = new SnowSettings();
        double D(string n, double v) => k.TryGetProperty(n, out var e) && e.ValueKind == JsonValueKind.Number ? e.GetDouble() : v;
        s.Coat = Math.Max(1, D("manteau", s.Coat));
        s.Max = Math.Clamp(D("max", s.Max), 0, 1);
        s.Melt = Math.Max(1, D("fonte", s.Melt));
        s.Always = Math.Max(0, D("toujours", s.Always));
        return s;
    }

    /// <summary>
    /// La couverture à l'image suivante. <paramref name="tombe"/> : ce qui tombe,
    /// zéro s'il ne neige pas ; <paramref name="degel"/> : de combien on est
    /// au-dessus du seuil de neige.
    /// </summary>
    public double Step(double cover, double dt, double tombe, double degel) =>
        tombe > 0 ? Math.Min(Max, cover + dt * tombe / Coat)
                  : Math.Max(0, cover - dt * (Always + degel) / Melt);
}
