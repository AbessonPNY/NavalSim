using System;

namespace NavalSim.Core;

/// <summary>
/// LA BOURSE — portée de <c>Naval.Purse</c> (purse.js).
///
/// UN SEUL NOMBRE, DEUX LECTURES. La bourse est un entier de pièces d'argent et
/// rien d'autre ; les écus d'or sont une manière de le lire, pas une seconde
/// réserve. Tenir deux compteurs, c'est garantir qu'ils divergeront le jour où
/// une transaction franchit la retenue. Soixante pièces pour un écu : un écu
/// valait trois livres, une livre vingt sous.
/// </summary>
public sealed class Purse
{
    public long Sous { get; private set; }

    public Purse(long sous) { Sous = Math.Max(0, sous); }

    public long Ecus => Sous / Market.SousParEcu;
    public long Pieces => Sous % Market.SousParEcu;

    public bool Has(long n) => Sous >= n;

    /// <summary>
    /// Rend faux et NE PRÉLÈVE RIEN si la bourse est courte : un débit partiel
    /// est la manière classique de perdre de l'argent sans rien recevoir.
    /// </summary>
    public bool Take(double n)
    {
        long k = (long)Math.Max(0, Js.Round(n));
        if (Sous < k) return false;
        Sous -= k;
        return true;
    }

    public void Add(double n) => Sous += (long)Math.Max(0, Js.Round(n));
}

/// <summary>Ce qu'un comptoir sait d'un autre port : un chiffre ferme, et son âge.</summary>
public readonly record struct News(string Key, string Name, double Lag, double Sell);

/// <summary>Ce qu'un navire croisé au large dit d'un port : vrai, frais, et vague.</summary>
public readonly record struct Rumour(string Voile, string Port, string Mot, string Lie, bool Fort, bool Faible);

/// <summary>
/// LE COURS DES ÉPICES — porté de <c>Naval.Market</c> (purse.js), au bit près.
///
/// LE COURS EST UNE FONCTION PURE DU PORT ET DE L'HEURE, comme les îles et les
/// dépressions, et pour les mêmes raisons : aucun état à tenir, aucun fichier à
/// charger, la même chose sur toutes les machines et d'une session à l'autre.
/// Et il est CONTINU : on interpole entre deux paliers d'un smoothstep, sans
/// quoi l'on apprendrait à attendre devant le comptoir que le chiffre saute.
///
/// Le banc de parité le compare à la page au bit près : un cours qui diffère
/// d'une pièce entre les deux versions est un cours dont l'une ment.
/// </summary>
public sealed class Market
{
    public const long SousParEcu = 60;

    /// <summary>Le cours moyen d'une tonne d'épices, en pièces — la cargaison la plus chère qu'on pût porter.</summary>
    public const double Epice = 620;

    /// <summary>Une charge de poudre : ce que consomme un coup de canon.</summary>
    public const double Poudre = 26;

    /// <summary>
    /// Ce que la bourse contient au premier armement : il faut qu'il achète une
    /// PREMIÈRE CARGAISON. Un jeu qui commence par « bourse trop courte »
    /// n'explique rien à personne. Quatre cents écus.
    /// </summary>
    public const long Depart = 24000;

    /// <summary>
    /// La marge du négociant, prise sur le prix affiché : un port n'achète pas
    /// ce qu'il vend, donc l'aller-retour à vide entre deux ports au même cours
    /// PERD de l'argent.
    /// </summary>
    public const double Marge = 0.12;

    /// <summary>Cinq nœuds, en m/s : l'allure à laquelle une traversée se compte.</summary>
    public const double Allure = 2.572;

    /// <summary>Cinq nœuds encore : l'allure d'un aviso porteur de nouvelles.</summary>
    public const double Nouvelle = 2.6;

    /// <summary>
    /// UN COURS DOIT TENIR LE TEMPS D'UNE TRAVERSÉE. Trois heures pour l'ancien
    /// archipel — mais ce qui compte est le RAPPORT du palier à la plus longue
    /// traversée, donc <see cref="Tune"/> le recale sur le monde.
    /// </summary>
    public double Palier = 10800;

    /// <summary>Le palier réglé sur la plus longue traversée du monde, à cinq nœuds.</summary>
    public double Tune(double longestLegM)
    {
        Palier = Math.Max(600, Js.Round(longestLegM / Allure));
        return Palier;
    }

    /// <summary>Le hachage de la page, arrondis du double compris.</summary>
    public static double Hash(string key, double n)
    {
        int s = Js.ToInt32(n * 374761393.0);
        foreach (char ch in key) s = Js.ToInt32((double)s * 31 + ch);
        s = Js.ToInt32((double)(s ^ (int)((uint)s >> 13)) * 1274126177.0);
        return (uint)(s ^ (int)((uint)s >> 16)) / 4294967296.0;
    }

    /// <summary>Le cours des épices à ce port, en pièces la tonne : de 0,55 à 1,45 du cours moyen.</summary>
    public double Spice(string key, double t)
    {
        double T = Palier, n = Math.Floor(t / T), u = t / T - n;
        double e = u * u * (3 - 2 * u);
        double a = Hash(key, n), b = Hash(key, n + 1);
        double f = a + (b - a) * e;
        return Js.Round(Epice * (0.55 + 0.90 * f));
    }

    /// <summary>Ce que le négociant demande.</summary>
    public double BuyPrice(string key, double t) => Js.Round(Spice(key, t) * (1 + Marge));
    /// <summary>Ce qu'il consent à payer.</summary>
    public double SellPrice(string key, double t) => Js.Round(Spice(key, t) * (1 - Marge));

    /// <summary>
    /// AU COMPTOIR : exact, et VIEUX. La nouvelle a voyagé par la mer, à la
    /// vitesse de la mer : le port le plus lointain donne la nouvelle la plus
    /// alléchante ET la plus périmée. Encore une fonction pure.
    /// </summary>
    public News NewsOf(Isle from, Isle to, double t)
    {
        double dx = from.X - to.X, dz = from.Z - to.Z;
        double lag = Math.Sqrt(dx * dx + dz * dz) / Nouvelle;
        return new News(to.Key, to.Name, lag, SellPrice(to.Key, t - lag));
    }

    /// <summary>
    /// L'âge d'une nouvelle, dit comme on le dirait. Un chiffre périmé SANS son
    /// âge est un mensonge ; avec son âge, c'est un renseignement.
    /// </summary>
    public static string Age(double lag)
    {
        long m = (long)Js.Round(lag / 60);
        if (m < 60) return $"il y a {m} min";
        return $"il y a {m / 60} h {(m % 60):00}";
    }

    static readonly (double Seuil, string Texte)[] Bandes =
    {
        (1.28, "on y paie des prix d’or"),
        (1.10, "les cours y sont hauts"),
        (0.92, "les cours y sont ordinaires"),
        (0.76, "les cours y sont mous"),
        (0.00, "on n’y achète presque plus")
    };

    static readonly string[] Voiles =
        { "un brick", "une flûte", "une barque de pêche", "un aviso", "une caravelle", "un sloop", "une galiote" };

    /// <summary>
    /// EN MER : frais, et VAGUE. Un capitaine croisé au large ne récite pas une
    /// mercuriale : pas de chiffre, donc, et c'est délibéré — un chiffre se
    /// compare, une appréciation se pèse. <paramref name="rnd"/> dans [0, 1).
    /// </summary>
    public Rumour RumourOf(Isle isl, double t, double rnd)
    {
        double r = Spice(isl.Key, t) / Epice;
        string mot = Bandes[^1].Texte;
        foreach (var (seuil, texte) in Bandes) if (r >= seuil) { mot = texte; break; }
        string v = Voiles[(int)Math.Floor(rnd * Voiles.Length) % Voiles.Length];
        // « qu'on y paie » mais « que les cours » : on n'élide que devant une voyelle
        string lie = "aeiouyàâéèêëîïôöûù".IndexOf(char.ToLowerInvariant(mot[0])) >= 0 ? "qu’" : "que ";
        return new Rumour(v, isl.Name, mot, lie, r >= 1.10, r < 0.92);
    }
}
