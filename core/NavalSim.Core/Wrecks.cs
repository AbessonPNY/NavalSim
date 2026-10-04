using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace NavalSim.Core;

/// <summary>
/// UNE ÉPAVE — ce qu'il reste d'un navire, là où il est descendu. En mètres
/// VRAIS de sa région, comme tout ce qui est « du monde » : l'origine flottante
/// glisse, l'épave ne bouge pas.
/// </summary>
public sealed class Wreck
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    /// <summary>La fiche du navire (ships/*.json, sans l'extension) : c'est son modèle qui gît au fond.</summary>
    [JsonPropertyName("navire")] public string Ship { get; set; } = "";
    [JsonPropertyName("nom")] public string Name { get; set; } = "";
    [JsonPropertyName("region")] public string Region { get; set; } = "caraibes";
    [JsonPropertyName("x")] public double X { get; set; }
    [JsonPropertyName("z")] public double Z { get; set; }
    /// <summary>Son cap en coulant, en radians (atan2 de l'étrave) : elle se couche dans l'axe où elle allait.</summary>
    [JsonPropertyName("cap")] public double Cap { get; set; }
    /// <summary>Le fond sous elle, en mètres sous la mer (positif).</summary>
    [JsonPropertyName("fond")] public double Depth { get; set; }
    /// <summary>Son bau, en mètres : le coffre se pose par le travers, à quelques mètres du bordé.</summary>
    [JsonPropertyName("bau")] public double Beam { get; set; }
    [JsonPropertyName("date")] public string Date { get; set; } = "";
    /// <summary>L'heure du ciel où elle a sombré ; négative pour une épave inscrite avant qu'on la retienne (midi).</summary>
    [JsonPropertyName("heure")] public double Hour { get; set; } = -1;
    /// <summary>Ce que son coffre renferme, en écus.</summary>
    [JsonPropertyName("ecus")] public long Ecus { get; set; }
    [JsonPropertyName("pillee")] public bool Looted { get; set; }
    /// <summary>Ce que renferme son coffre, tiré à son inscription (treasure/tresor.json).</summary>
    [JsonPropertyName("contenu")] public List<Loot> Contents { get; set; } = new();
    /// <summary>Rompue par sa soute, et où.</summary>
    [JsonPropertyName("rompue")] public bool Broken { get; set; }
    [JsonPropertyName("coupe")] public double ZCut { get; set; }
}

/// <summary>
/// LE REGISTRE DES ÉPAVES — toutes, demandé tel quel : un navire qui sombre est
/// inscrit, qu'on l'ait coulé, qu'il ait sombré seul ou que ce soit le sien.
///
/// IL VIT AVEC LA PARTIE et se sauvegarde avec elle : une épave est un LIEU, on
/// doit la retrouver où on l'a laissée d'une session à l'autre. Il ne dépend
/// d'aucune région chargée — chaque épave porte la sienne —, si bien qu'une
/// traversée n'en perd rien.
/// </summary>
public sealed class WreckRegistry
{
    public readonly List<Wreck> All = new();

    /// <summary>Le premier voile de vase, au bout d'un jour (part de la couche pleine).</summary>
    public const double SiltFirst = 0.25;
    /// <summary>Le temps propre de la vase, en jours : passé trois fois ce temps (deux semaines), la couche est presque pleine.</summary>
    public const double SiltDays = 5;

    /// <summary>
    /// LA VASE SUR UNE ÉPAVE, de 0 à 1, selon le temps qu'elle a passé au fond :
    /// rien le premier jour, un voile ensuite, qui s'épaissit vite puis de moins en
    /// moins. UN ÉCART ASSUMÉ avec le vrai : une rade dépose quelques millimètres
    /// par an ; ce qui se voit en quelques jours sur un bois noyé est la pellicule
    /// de limon et d'algues que l'eau trouble y laisse — on en a fait une couche.
    /// <paramref name="now"/> : le jour ET l'heure du ciel.
    /// </summary>
    public static double SiltCover(Wreck w, DateTime now)
    {
        if (!DateTime.TryParseExact(w.Date, "dd/MM/yyyy", System.Globalization.CultureInfo.InvariantCulture,
                                    System.Globalization.DateTimeStyles.AdjustToUniversal, out var sunk)) return 0;
        sunk = DateTime.SpecifyKind(sunk, DateTimeKind.Utc).AddHours(w.Hour >= 0 ? w.Hour : 12);
        double days = (now - sunk).TotalDays;
        if (days < 1) return 0;
        return SiltFirst + (1 - SiltFirst) * (1 - Math.Exp(-(days - 1) / SiltDays));
    }

    public void Clear() => All.Clear();

    public Wreck Add(Wreck w) { All.Add(w); return w; }

    public void Remove(string id) => All.RemoveAll(w => w.Id == id);

    /// <summary>Les épaves de cette région à moins de <paramref name="r"/> mètres d'un point (mètres vrais).</summary>
    public IEnumerable<Wreck> Near(string region, double x, double z, double r)
    {
        foreach (var w in All)
        {
            if (w.Region != region) continue;
            double dx = w.X - x, dz = w.Z - z;
            if (dx * dx + dz * dz < r * r) yield return w;
        }
    }

    /// <summary>
    /// CE QUE RENFERME SON COFFRE, en écus. Une règle et non un tirage libre :
    /// un navire porte de quoi payer son équipage et ses relâches, à proportion de
    /// sa taille ; sa cargaison s'y ajoute (une tonne de marchandise vaut
    /// quelques écus remontés) ; et un pirate porte ce qu'il a pris aux autres.
    /// La graine, prise sur son identité, fait varier d'un tiers sans que la même
    /// épave change de valeur d'une partie à l'autre.
    /// </summary>
    public static long Value(double massKg, double cargoTonnes, bool pirate, string id)
    {
        uint g = 2166136261;
        foreach (char c in id) g = (g ^ c) * 16777619;
        double spread = 0.7 + 0.6 * ((g & 0xFFFF) / 65535.0);
        double v = (massKg / 1000 * 0.4 + cargoTonnes * 6 + (pirate ? 220 : 0)) * spread;
        return Math.Max(15, (long)Math.Round(v));
    }

    /// <summary>
    /// OÙ EST LE COFFRE : par le travers de l'épave, à quelques mètres de son
    /// bordé, du côté que sa graine choisit. Pas DANS la coque — la cloche ne peut
    /// pas y entrer, et un trésor qu'on voit sans pouvoir l'atteindre est pire
    /// qu'aucun trésor.
    /// </summary>
    public static (double X, double Z) Chest(Wreck w, double beam)
    {
        uint g = 2166136261;
        foreach (char c in w.Id) g = (g ^ c) * 16777619;
        int side = (g & 1) == 0 ? 1 : -1;
        double along = (((g >> 8) & 0xFF) / 255.0 - 0.5) * 6;
        double off = beam * 0.5 + 3.5;
        double fx = Math.Sin(w.Cap), fz = Math.Cos(w.Cap);       // l'étrave
        double sx = -fz, sz = fx;                                 // le travers
        return (w.X + fx * along + sx * off * side, w.Z + fz * along + sz * off * side);
    }
}

/// <summary>
/// LA CLOCHE DE HALLEY (1691), en nombres. Un cône tronqué de bois cerclé de
/// plomb, ouvert en dessous, qu'on descend au bout d'un câble : l'air qu'elle
/// emprisonne à la surface est tout ce que les plongeurs ont.
///
/// LOI DE BOYLE : à la profondeur d, la pression vaut 1 + d / 10,3 atmosphères,
/// et l'air n'occupe plus que l'inverse — à dix mètres, la moitié de la cloche.
/// L'eau monte DEDANS d'autant, et c'est ce qu'on voit depuis l'intérieur.
///
/// ET L'ON RESPIRE PLUS VITE EN BAS : chaque bouffée prend le même volume, mais
/// d'un air comprimé, donc plus de molécules. La réserve, comptée en secondes à
/// la surface, s'épuise à proportion de la pression. Halley tenait une heure et
/// demie à dix-huit mètres parce qu'on lui descendait des tonneaux d'air ; sans
/// eux, ce sont quelques minutes.
/// </summary>
public static class DivingBell
{
    public const double Height = 2.4, TopR = 0.5, BottomR = 0.8;

    public static double Pressure(double depth) => 1 + Math.Max(0, depth) / 10.3;

    /// <summary>La hauteur d'eau DANS la cloche, depuis son bord, à cette profondeur (en mètres).</summary>
    public static double WaterInside(double depth)
    {
        /* Le volume d'air est la fraction 1/P du volume de la cloche ; on cherche la
           hauteur d'eau h qui laisse ce volume au-dessus d'elle dans le cône
           tronqué. Le volume d'un tronc de cône au-dessus d'une coupe à la hauteur
           h se calcule directement ; on résout par dichotomie — dix pas, au
           millimètre près. */
        double frac = 1 / Pressure(depth);
        double Above(double h)
        {
            double r = BottomR + (TopR - BottomR) * (h / Height), H = Height - h;
            return Math.PI * H / 3 * (r * r + r * TopR + TopR * TopR);
        }
        double total = Above(0), lo = 0, hi = Height;
        for (int i = 0; i < 30; i++)
        {
            double m = (lo + hi) / 2;
            if (Above(m) > frac * total) lo = m; else hi = m;
        }
        return (lo + hi) / 2;
    }

    /// <summary>Ce que coûte une seconde passée à cette profondeur, en secondes de réserve de surface.</summary>
    public static double Burn(double depth) => Pressure(depth);
}
