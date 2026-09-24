using System;
using System.Text.Json;

namespace NavalSim.Core;

/// <summary>
/// LES CAUSTIQUES — la lumière du soleil rassemblée par la houle sur le fond des
/// hauts-fonds (settings.json → caustics).
///
/// Le calcul n'est pas ici : il appartient tout entier au GPU, et il est écrit
/// une seule fois dans <c>gerstner.gdshaderinc</c>, à côté de la houle dont il
/// n'est qu'une lecture de plus. Ce qui vit ici, ce sont les cadrans — ce que le
/// joueur peut tourner sans toucher un shader.
///
/// Le seul chiffre qui n'ait pas de sens physique est <see cref="Floor"/> :
/// au foyer exact d'une nervure, l'aire d'arrivée est nulle et la clarté
/// infinie. La nature s'en tire par la largeur du soleil (un demi-degré) ; nous,
/// par un plancher.
/// </summary>
public sealed class CausticSettings
{
    /// <summary>Les peindre.</summary>
    public bool Enabled = true;
    /// <summary>La force du contraste. 1 : ce que la réfraction donne, sans rien y ajouter.</summary>
    public double Gain = 1.0;
    /// <summary>La profondeur où elles s'éteignent, en mètres : le soleil n'est pas un point.</summary>
    public double Depth = 14;
    /// <summary>La distance à l'œil où elles s'éteignent, en mètres — au-delà, un moiré.</summary>
    public double Far = 700;
    /// <summary>Le plancher du déterminant : plus bas, des nervures plus brillantes et plus fines.</summary>
    public double Floor = 0.12;
    /// <summary>L'écart des couleurs au foyer. 1 : celui de l'eau de mer ; 0 : une lumière blanche.</summary>
    public double Spread = 1.0;

    public static CausticSettings FromJson(JsonElement k)
    {
        var c = new CausticSettings();
        double D(string n, double v) => k.TryGetProperty(n, out var e) && e.ValueKind == JsonValueKind.Number ? e.GetDouble() : v;
        if (k.TryGetProperty("enabled", out var on) && (on.ValueKind == JsonValueKind.False || on.ValueKind == JsonValueKind.True))
            c.Enabled = on.GetBoolean();
        c.Gain = Math.Clamp(D("force", c.Gain), 0, 4);
        c.Depth = Math.Max(0.5, D("profondeur", c.Depth));
        c.Far = Math.Max(20, D("portee", c.Far));
        c.Floor = Math.Clamp(D("plancher", c.Floor), 0.02, 1);
        c.Spread = Math.Clamp(D("dispersion", c.Spread), 0, 4);
        return c;
    }
}
