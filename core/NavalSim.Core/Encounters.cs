using System.Text.Json;

namespace NavalSim.Core;

/// <summary>
/// LES VOILES QU'ON CROISE — settings.json → encounters, les mêmes chiffres que
/// la page lit déjà.
///
/// La mer est grande et vide ; ce qui la remplit, ce sont les voiles qu'on ne
/// s'attendait pas à voir. Une paraît de temps en temps, loin, hors de vue de
/// toute côte : un marchand qui fait route vers un port, ou un pirate qui vient
/// sur vous sous un pavillon d'emprunt. Elle disparaît quand on l'a semée.
/// </summary>
public sealed class EncounterSettings
{
    public bool Enabled = true;
    /// <summary>Le temps entre deux voiles, en secondes de jeu : tiré entre les deux.</summary>
    public double IntervalMin = 55, IntervalMax = 720;
    /// <summary>Combien de voiles de rencontre en même temps.</summary>
    public int MaxAtOnce = 2;
    /// <summary>Où elle paraît, de vous : assez loin pour n'être qu'une tache.</summary>
    public double SpawnMin = 4000, SpawnMax = 5000;
    /// <summary>Semée au-delà : elle est rendue à la mer.</summary>
    public double Despawn = 9000;
    /// <summary>La part des voiles qui sont des pirates.</summary>
    public double PirateChance = 0.25;
    /// <summary>En deçà, la vigie l'annonce sans lunette.</summary>
    public double NoticeDistance = 600;
    public double SpyglassRange = 7000, SpyglassField = 0.8;
    /// <summary>Rien ne paraît tant qu'on est à moins de cela d'une côte : on croise des voiles AU LARGE.</summary>
    public double LandDistance = 2500;
    /// <summary>Et elle ne naît pas non plus contre une terre.</summary>
    public double SpawnFromLand = 1500;
    /// <summary>La marge d'eau claire le long de la route qu'elle va suivre.</summary>
    public double ClearWater = 400;

    /* UNE BATAILLE DÉJÀ COMMENCÉE, et c'est ce que la page n'avait pas : une part
       des rencontres est une PAIRE — un pirate qui canonne un marchand, trouvés
       au milieu de l'affaire. On arrive dessus et on choisit : passer au large,
       secourir, ou attendre que l'un ait fini l'autre pour prendre le reste. */
    /// <summary>La part des rencontres qui sont deux navires aux prises.</summary>
    public double BattleChance = 0.18;
    /// <summary>Ce qui les sépare quand on les trouve : la portée où l'on se battait.</summary>
    public double BattleGap = 160;

    /// <summary>Les fiches qu'on ne croise pas en mer (une bouée, une chaloupe…).</summary>
    public string[] Exclude = { "bouee-canard", "chaloupe", "cotre", "chaland", "schooner" };

    public static EncounterSettings FromJson(JsonElement k)
    {
        var s = new EncounterSettings();
        double D(string n, double v) => k.TryGetProperty(n, out var e) && e.ValueKind == JsonValueKind.Number ? e.GetDouble() : v;
        if (k.TryGetProperty("enabled", out var en) && (en.ValueKind == JsonValueKind.True || en.ValueKind == JsonValueKind.False))
            s.Enabled = en.GetBoolean();
        s.IntervalMin = D("intervalMin", s.IntervalMin);
        s.IntervalMax = D("intervalMax", s.IntervalMax);
        s.MaxAtOnce = (int)D("maxAtOnce", s.MaxAtOnce);
        s.SpawnMin = D("spawnMin", s.SpawnMin);
        s.SpawnMax = D("spawnMax", s.SpawnMax);
        s.Despawn = D("despawn", s.Despawn);
        s.PirateChance = D("pirateChance", s.PirateChance);
        s.NoticeDistance = D("noticeDistance", s.NoticeDistance);
        s.SpyglassRange = D("spyglassRange", s.SpyglassRange);
        s.SpyglassField = D("spyglassField", s.SpyglassField);
        s.LandDistance = D("landDistance", s.LandDistance);
        s.SpawnFromLand = D("spawnFromLand", s.SpawnFromLand);
        s.ClearWater = D("clearWater", s.ClearWater);
        s.BattleChance = D("battleChance", s.BattleChance);
        s.BattleGap = D("battleGap", s.BattleGap);
        if (k.TryGetProperty("exclude", out var ex) && ex.ValueKind == JsonValueKind.Array)
        {
            var list = new System.Collections.Generic.List<string>();
            foreach (var e in ex.EnumerateArray()) if (e.GetString() is string id) list.Add(id);
            s.Exclude = list.ToArray();
        }
        return s;
    }
}
