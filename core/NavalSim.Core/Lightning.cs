using System.Text.Json;

namespace NavalSim.Core;

/// <summary>
/// LA FOUDRE QUI TROUVE UN NAVIRE — les règles de lightning.js (settings.json →
/// storm.lightning). Le ciel scintille déjà dans un grain et sur les bancs
/// lointains, sans jamais rien toucher ; celle-ci DESCEND, sur la chose la plus
/// haute qui sorte de la mer, et en mer c'est une tête de mât. Choisie navire par
/// navire, d'autant plus souvent qu'il est enfoncé dans la dépression.
/// </summary>
public sealed class LightningSettings
{
    public bool Enabled = true;
    public double MinInten = 0.4;          // enfoncement sous lequel rien ne tombe
    public double PerMinuteAtCore = 0.5;   // coups par minute sur un navire au centre même
    public double SplitChance = 0.5;       // une voile arrachée de ses ralingues
    public double WoundChance = 0.6;       // le mât blessé (trois blessures et il tombe)
    public double DismastChance = 0.12;    // le mât fendu d'un coup

    public static LightningSettings FromJson(JsonElement j)
    {
        var s = new LightningSettings();
        double D(string n, double v) => j.TryGetProperty(n, out var e) && e.ValueKind == JsonValueKind.Number ? e.GetDouble() : v;
        if (j.TryGetProperty("enabled", out var en) && (en.ValueKind == JsonValueKind.True || en.ValueKind == JsonValueKind.False))
            s.Enabled = en.GetBoolean();
        s.MinInten = D("minInten", s.MinInten); s.PerMinuteAtCore = D("perMinuteAtCore", s.PerMinuteAtCore);
        s.SplitChance = D("splitChance", s.SplitChance); s.WoundChance = D("woundChance", s.WoundChance);
        s.DismastChance = D("dismastChance", s.DismastChance);
        return s;
    }

    /// <summary>La chance qu'un navire enfoncé de <paramref name="inten"/> soit frappé pendant <paramref name="dt"/> secondes.</summary>
    public double StrikeChance(double inten, double dt)
    {
        if (!Enabled || !(inten > MinInten)) return 0;
        double k = (inten - MinInten) / (1 - MinInten);
        return PerMinuteAtCore * k * dt / 60;
    }
}
