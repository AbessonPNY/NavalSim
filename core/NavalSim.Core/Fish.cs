using System;
using System.Text.Json;

namespace NavalSim.Core;

/// <summary>
/// LES BANCS DE POISSONS DES HAUTS-FONDS (settings.json → fish).
///
/// Ce qui n'est PAS ici : la nage. Un banc ne coûte pas une ligne de calcul par
/// image — chaque poisson porte quatre nombres et le shader en tire sa ronde,
/// son cap et la flexion de son corps (voir fish.gdshader). Le C# ne fait que
/// choisir un fond où les poser et les retirer quand on s'en éloigne.
///
/// Ce qui est ici, ce sont les cadrans, et la règle qui décide d'un endroit :
/// un banc de récif veut du fond clair et peu d'eau. Au-delà de
/// <see cref="MaxDepth"/> on ne verrait plus rien de toute façon — la lumière
/// n'y descend plus, et c'est la même raison qui éteint les caustiques.
/// </summary>
public sealed class FishSettings
{
    /// <summary>Les peupler.</summary>
    public bool Enabled = true;
    /// <summary>Combien de bancs à la fois autour du navire.</summary>
    public int Shoals = 3;
    /// <summary>Combien de poissons par banc.</summary>
    public int PerShoal = 26;
    /// <summary>L'eau la plus mince où ils tiennent, en mètres.</summary>
    public double MinDepth = 1.2;
    /// <summary>Et la plus profonde : au-delà, on ne les verrait plus.</summary>
    public double MaxDepth = 11;
    /// <summary>Jusqu'où on en pose, en mètres depuis le navire.</summary>
    public double Range = 220;
    /// <summary>La longueur d'un poisson, en mètres — le modèle y est ramené.</summary>
    public double Size = 0.26;
    /// <summary>Le rayon du banc, en mètres.</summary>
    public double Spread = 3.2;
    /// <summary>Sa vitesse, en LONGUEURS DE CORPS par seconde : un gros va plus vite.</summary>
    public double Speed = 1.1;
    /// <summary>Les battements de queue par seconde.</summary>
    public double Beat = 2.6;
    /// <summary>Le balayage de la queue, en part de la longueur du corps.</summary>
    public double Sway = 0.11;
    /// <summary>Le sens du modèle : 1 si son nez va vers +Z, −1 s'il regarde l'autre bord.</summary>
    public double Face = 1;
    /// <summary>Leur teinte, et de combien elle varie d'un poisson à l'autre.</summary>
    public string Tint = "#dc9e42";
    public double Vary = 0.25;

    public static FishSettings FromJson(JsonElement k)
    {
        var f = new FishSettings();
        double D(string n, double v) => k.TryGetProperty(n, out var e) && e.ValueKind == JsonValueKind.Number ? e.GetDouble() : v;
        if (k.TryGetProperty("enabled", out var on) && (on.ValueKind == JsonValueKind.False || on.ValueKind == JsonValueKind.True))
            f.Enabled = on.GetBoolean();
        f.Shoals = (int)Math.Clamp(D("bancs", f.Shoals), 0, 12);
        f.PerShoal = (int)Math.Clamp(D("poissons", f.PerShoal), 1, 200);
        f.MinDepth = Math.Max(0.3, D("fond_min", f.MinDepth));
        f.MaxDepth = Math.Max(f.MinDepth + 0.5, D("fond_max", f.MaxDepth));
        f.Range = Math.Max(20, D("portee", f.Range));
        f.Size = Math.Clamp(D("taille", f.Size), 0.02, 3);
        f.Spread = Math.Clamp(D("rayon", f.Spread), 0.5, 40);
        f.Speed = Math.Clamp(D("vitesse", f.Speed), 0.05, 12);
        f.Beat = Math.Clamp(D("battement", f.Beat), 0.1, 20);
        f.Sway = Math.Clamp(D("balancement", f.Sway), 0, 0.5);
        f.Vary = Math.Clamp(D("variete", f.Vary), 0, 1);
        f.Face = D("sens", f.Face) < 0 ? -1 : 1;
        if (k.TryGetProperty("teinte", out var t) && t.ValueKind == JsonValueKind.String)
            f.Tint = t.GetString() ?? f.Tint;
        return f;
    }
}
