using System;
using System.Collections.Generic;
using System.Text.Json;

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

/// <summary>Une marchandise : le mot que porte la cale, ce que le joueur lit, et son cours moyen.</summary>
public sealed class Good
{
    public string Key = "", Name = "", Note = "";
    /// <summary>Le cours MOYEN de la tonne, en pièces d'argent.</summary>
    public double Price;
}

/// <summary>
/// CE QU'ON CHARGE ET CE QU'ON REVEND, lu dans <c>market/marchandises.json</c>.
///
/// EN FICHE ET NON EN DUR, demandé, et c'est la bonne place : ajouter une
/// denrée ou changer un cours est une décision de jeu, pas de programme, et
/// elle doit se prendre sans recompiler. Les deux moteurs lisent la même fiche,
/// comme pour les navires, le monde et les quêtes.
///
/// Une marchandise ABSENTE de la liste se porte mais ne se vend nulle part —
/// c'est le cas des vivres et de la viande d'un fret sous contrat, qu'on livre
/// et qu'on ne brade pas. La cale, elle, ne connaît que des mots : elle porte
/// ce qu'on lui donne.
/// </summary>
public sealed class Goods
{
    public readonly List<Good> List = new();

    /// <summary>
    /// La marge du négociant, prise sur le prix affiché des deux côtés : un port
    /// n'achète pas ce qu'il vend, donc l'aller-retour à vide entre deux ports au
    /// même cours PERD de l'argent.
    /// </summary>
    public double Marge = 0.12;

    public Good? ByKey(string key)
    {
        foreach (var g in List) if (g.Key == key) return g;
        return null;
    }

    public static Goods FromJson(string text)
    {
        var g = new Goods();
        using var doc = JsonDocument.Parse(text);
        var r = doc.RootElement;
        if (r.TryGetProperty("marge", out var m) && m.ValueKind == JsonValueKind.Number) g.Marge = m.GetDouble();
        if (r.TryGetProperty("marchandises", out var arr) && arr.ValueKind == JsonValueKind.Array)
            foreach (var e in arr.EnumerateArray())
                g.List.Add(new Good
                {
                    Key = Js.Str(e, "key"), Name = Js.Str(e, "nom"), Note = Js.Str(e, "note"),
                    Price = e.TryGetProperty("prix", out var p) && p.ValueKind == JsonValueKind.Number ? p.GetDouble() : 0
                });
        return g;
    }

}

/// <summary>Ce qu'un comptoir sait d'un autre port : la denrée qui s'y paie le mieux, son chiffre, et son âge.</summary>
public readonly record struct News(string Key, string Name, double Lag, string Good, string GoodName, double Sell);

/// <summary>Ce qu'un navire croisé au large dit d'un port : vrai, frais, et vague.</summary>
public readonly record struct Rumour(string Voile, string Port, string Good, string Mot, string Lie, bool Fort, bool Faible);

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

    /// <summary>
    /// LES MARCHANDISES, posées par l'hôte au chargement. Vide tant que personne
    /// ne les a lues : le comptoir ne montre alors rien, ce qui est le bon défaut
    /// — mieux vaut un comptoir muet qu'un cours inventé.
    /// </summary>
    public Goods Wares = new();

    /// <summary>Une charge de poudre : ce que consomme un coup de canon.</summary>
    public const double Poudre = 26;

    /// <summary>
    /// Ce que la bourse contient au premier armement : il faut qu'il achète une
    /// PREMIÈRE CARGAISON. Un jeu qui commence par « bourse trop courte »
    /// n'explique rien à personne. Quatre cents écus.
    /// </summary>
    public const long Depart = 24000;

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

    /// <summary>
    /// LE CLÉ DU HACHAGE : la marchandise ET le port. C'est tout ce qu'il fallait
    /// pour que le commerce devienne un métier — le sucre peut être haut à
    /// Montego Bay quand l'indigo y est bas, et c'est cet écart-là, et non le
    /// niveau général d'un port, qui fait choisir une route.
    ///
    /// La règle est UNIFORME : aucune denrée n'est le cas particulier d'une
    /// autre, pas même l'épice qui fut longtemps la seule. Ses cours ne sont donc
    /// plus ceux d'avant, et c'est sans conséquence : un cours est une fonction
    /// pure du lieu et de l'heure, rien ne le retient d'une partie à l'autre.
    /// </summary>
    static string Salt(string good, string port) => good + "/" + port;

    /// <summary>Le cours d'une denrée à ce port, en pièces la tonne : de 0,55 à 1,45 de son cours moyen.</summary>
    public double Price(string good, string port, double t)
    {
        var w = Wares.ByKey(good);
        if (w == null) return 0;
        double T = Palier, n = Math.Floor(t / T), u = t / T - n;
        double e = MathX.Smooth01(u);
        string k = Salt(good, port);
        double a = Hash(k, n), b = Hash(k, n + 1);
        double f = a + (b - a) * e;
        return Js.Round(w.Price * (0.55 + 0.90 * f));
    }

    /// <summary>Le rapport du cours à sa moyenne : 1 est le cours ordinaire, 1,45 le plus haut.</summary>
    public double Ratio(string good, string port, double t)
    {
        var w = Wares.ByKey(good);
        return w == null || w.Price <= 0 ? 0 : Price(good, port, t) / w.Price;
    }

    /// <summary>Ce que le négociant demande.</summary>
    public double BuyPrice(string good, string port, double t) => Js.Round(Price(good, port, t) * (1 + Wares.Marge));
    /// <summary>Ce qu'il consent à payer.</summary>
    public double SellPrice(string good, string port, double t) => Js.Round(Price(good, port, t) * (1 - Wares.Marge));

    /// <summary>
    /// LA DENRÉE QUI S'Y PAIE LE MIEUX — au RAPPORT et non au chiffre. Comparer
    /// les prix bruts rendrait toujours l'indigo, qui vaut six fois le sucre par
    /// nature et non par occasion ; le rapport dit ce qui est cher POUR ELLE,
    /// c'est-à-dire ce qu'il y a à y gagner.
    /// </summary>
    public Good? Best(string port, double t)
    {
        Good? best = null;
        double top = -1;
        foreach (var w in Wares.List)
        {
            double r = Ratio(w.Key, port, t);
            if (r > top) { top = r; best = w; }
        }
        return best;
    }

    /// <summary>
    /// AU COMPTOIR : exact, et VIEUX. La nouvelle a voyagé par la mer, à la
    /// vitesse de la mer : le port le plus lointain donne la nouvelle la plus
    /// alléchante ET la plus périmée. Encore une fonction pure.
    /// </summary>
    public News NewsOf(Isle from, Isle to, double t)
    {
        double dx = from.X - to.X, dz = from.Z - to.Z;
        double lag = Math.Sqrt(dx * dx + dz * dz) / Nouvelle;
        /* LA NOUVELLE PORTE UNE DENRÉE, et une seule. Quatorze ports fois douze
           denrées font cent soixante-huit chiffres : un tableau que personne ne
           lit. Le négociant dit donc ce qui se paie le mieux là-bas — c'est la
           phrase qu'un homme du métier dirait, et c'est celle qui décide. */
        var w = Best(to.Key, t - lag);
        return new News(to.Key, to.Name, lag,
                        w?.Key ?? "", w?.Name ?? "",
                        w == null ? 0 : SellPrice(w.Key, to.Key, t - lag));
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
        /* LA DENRÉE DONT IL PARLE est tirée sur le même nombre que sa voile : un
           capitaine ne parle pas de tout, il parle de ce qu'il vient de vendre.
           Sans marchandise chargée, il n'a rien à dire. */
        if (Wares.List.Count == 0) return new Rumour("", isl.Name, "", "", "", false, false);
        var w = Wares.List[(int)Math.Floor(rnd * Wares.List.Count) % Wares.List.Count];
        double r = Ratio(w.Key, isl.Key, t);
        string mot = Bandes[^1].Texte;
        foreach (var (seuil, texte) in Bandes) if (r >= seuil) { mot = texte; break; }
        string v = Voiles[(int)Math.Floor(rnd * Voiles.Length) % Voiles.Length];
        // « qu'on y paie » mais « que les cours » : on n'élide que devant une voyelle
        string lie = "aeiouyàâéèêëîïôöûù".IndexOf(char.ToLowerInvariant(mot[0])) >= 0 ? "qu’" : "que ";
        return new Rumour(v, isl.Name, w.Name, mot, lie, r >= 1.10, r < 0.92);
    }
}
