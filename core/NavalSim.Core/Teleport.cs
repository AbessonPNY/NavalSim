using System;

namespace NavalSim.Core;

/// <summary>
/// LE SAUT PAR LES ANNEAUX — ce qu'on a le droit de faire, et en combien de temps.
///
/// Ici plutot que dans le moteur parce que c'est une REGLE DE JEU, et qu'une
/// regle de jeu qui s'ecrirait deux fois finirait par ne plus dire la meme chose
/// des deux cotes. Le moteur fournit les anneaux, la carte et le deplacement ;
/// ce fichier dit seulement si un point est sautable, et pourquoi il ne l'est
/// pas quand il ne l'est pas.
///
/// LA DUREE EST UNE REGLE, PAS UN REGLAGE D'AMBIANCE. Vingt secondes pendant
/// lesquelles les anneaux montent en regime : c'est long, et c'est voulu — un
/// saut instantane serait une touche de triche, vingt secondes sont un
/// ENGAGEMENT. On les passe a decouvert, sans pouvoir fuir, et un pirate qui
/// approche a le temps d'arriver. C'est ce delai qui fait du teleporteur une
/// decision plutot qu'un raccourci.
/// </summary>
public static class Teleport
{
    /// <summary>Secondes de charge avant le saut.</summary>
    public const double Charge = 20;

    /// <summary>
    /// Le rond d'eau exige a l'arrivee, en LONGUEURS de navire. On ne verifie pas
    /// le seul point vise : un navire n'est pas un point, et arriver le nez dans
    /// une falaise parce que le pixel sous la souris etait bleu serait un defaut
    /// que le joueur imputerait au jeu, avec raison.
    /// </summary>
    public const double Clearance = 1.0;

    public enum Verdict
    {
        /// <summary>Le saut peut se faire.</summary>
        Bon,
        /// <summary>Le point vise est a terre.</summary>
        Terre,
        /// <summary>Il y a de l'eau, mais pas assez pour la quille.</summary>
        Basses,
        /// <summary>Assez d'eau au point vise, pas autour : le navire toucherait.</summary>
        Serre,
        /// <summary>Aucune cible posee.</summary>
        SansCible,
    }

    /// <summary>
    /// Peut-on sauter la ? <paramref name="draft"/> est le tirant d'eau,
    /// <paramref name="length"/> la longueur de la coque.
    /// </summary>
    public static Verdict Check(World w, double x, double z, double draft, double length)
    {
        if (w == null) return Verdict.Terre;
        // a terre : le relief au-dessus de l'eau, et c'est le refus que le joueur attend
        if (w.HeightAt(x, z) >= 0) return Verdict.Terre;
        if (!w.Navigable(x, z, draft)) return Verdict.Basses;

        /* LE ROND D'EAU. Huit sondes sur un cercle d'une longueur de navire : de
           quoi tenir la coque entiere quel que soit le cap d'arrivee, ce qu'on ne
           connait pas encore au moment ou l'on juge le point. */
        double r = Math.Max(10, length * Clearance);
        for (int i = 0; i < 8; i++)
        {
            double a = i * Math.PI / 4;
            if (!w.Navigable(x + Math.Cos(a) * r, z + Math.Sin(a) * r, draft)) return Verdict.Serre;
        }
        return Verdict.Bon;
    }

    /// <summary>Ce qu'on en dit au joueur — en francais, c'est de l'interface.</summary>
    public static string Say(Verdict v) => v switch
    {
        Verdict.Bon => "Les anneaux s'éveillent",
        Verdict.Terre => "Les anneaux refusent : la cible est à terre",
        Verdict.Basses => "Les anneaux refusent : pas assez d'eau pour la quille",
        Verdict.Serre => "Les anneaux refusent : la côte est trop près de la cible",
        _ => "Aucune cible sur la carte — ⇧clic pour en poser une",
    };
}
