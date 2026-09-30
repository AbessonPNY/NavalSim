using System;
using System.Collections.Generic;

namespace NavalSim.Core;

/// <summary>
/// LES NAVIRES AU MOUILLAGE — où les poser dans un port, et pourquoi pas tous au
/// même endroit.
///
/// Un port vide se lit comme un décor. Mais y ranger cinq navires le long du quai
/// ne marche pas non plus, et le chiffre le dit : le ponton de Port-Royal fait
/// VINGT-HUIT MÈTRES. Un vaisseau de ligne en fait soixante-dix. Il n'y tient pas,
/// et la question n'est pas de trouver de la place — c'est qu'un vaisseau de ligne
/// ne s'amarre PAS à un ponton de bois. Il mouille en rade, et l'on va à son bord
/// en chaloupe.
///
/// D'où deux sortes de postes, et c'est la géographie qui tranche :
///
///   · À QUAI, le long du tablier, ce qui tient dans la longueur restante une fois
///     le poste du joueur retiré. En pratique : les petits.
///   · AU MOUILLAGE, dans la rade, à distance du ponton, sur un fond qui porte.
///     Les gros, et les petits quand le quai est pris.
///
/// LE SEMIS EST DÉTERMINISTE, tiré d'une graine prise sur le port : une rade doit
/// retrouver ses navires à la même place d'une partie à l'autre, sans qu'on ait à
/// les écrire quelque part. C'est la même règle que les badauds de la plage.
/// </summary>
public static class Moored
{
    /// <summary>Ce qu'un navire laisse entre lui et son voisin, à quai, en mètres.</summary>
    public const double Ecart = 8;
    /// <summary>Ce qu'il faut sous la quille pour mouiller : une marge de marée et de houle.</summary>
    public const double Sous = 2.5;
    /// <summary>La rade : entre ces deux distances du musoir, en mètres.</summary>
    public const double RadeMin = 130, RadeMax = 340;
    /// <summary>Et ce que deux navires au mouillage gardent entre eux, en longueurs.</summary>
    public const double Evitage = 1.6;

    /// <summary>Un poste. <paramref name="Mouille"/> : sur son ancre plutôt qu'à quai.</summary>
    public readonly record struct Poste(double X, double Z, double Cap, bool Mouille);

    /// <summary>
    /// Placer une flotte dans un port. Rend un poste par navire PLACÉ — un navire
    /// qui ne trouve ni quai ni fond n'est pas rendu, et il vaut mieux un port qui
    /// en porte deux qu'un navire posé sur un caillou.
    ///
    /// L'ORDRE COMPTE : on sert le quai d'abord, et du plus PETIT au plus grand.
    /// Servir dans l'ordre d'arrivée ferait qu'un vaisseau de ligne présenté le
    /// premier occuperait tout le quai en n'y tenant pas, et les deux sloops
    /// derrière iraient mouiller au large pendant que le quai resterait vide.
    /// </summary>
    public static List<Poste> Postes(World w, Isle isl,
                                     IReadOnlyList<(double L, double B, double Draft)> flotte,
                                     double playerL, double playerB, uint graine)
    {
        var sorti = new List<Poste>();
        var p = isl.Port;
        if (p.Hx == 0 && p.Hz == 0) return sorti;

        uint s = graine | 1;
        double Rnd() { s ^= s << 13; s ^= s >> 17; s ^= s << 5; return (s & 0xFFFFFF) / 16777216.0; }

        double ca = Math.Cos(p.Ang), sa = Math.Sin(p.Ang);
        /* LE BORD LIBRE. Berth.At pose le joueur du côté (−sa, ca) ; les autres vont
           donc en face, où l'on est sûr de ne gêner personne. Le poste du joueur
           n'est ainsi pas seulement évité, il est INATTEIGNABLE — ce qui vaut mieux
           qu'une comparaison de distances qu'un navire un peu long ferait mentir. */
        double cap = Math.Atan2(ca, sa);

        // du musoir vers la racine, sur le bord libre
        double reste = p.Reach - 4;
        var ordre = new List<int>();
        for (int i = 0; i < flotte.Count; i++) ordre.Add(i);
        ordre.Sort((a, b) => flotte[a].L.CompareTo(flotte[b].L));

        var pris = new List<(double X, double Z, double R)>();
        foreach (int i in ordre)
        {
            var (L, B, draft) = flotte[i];
            if (L + Ecart <= reste)
            {
                double centre = p.ShoreR + reste - L * 0.5;
                double off = Berth.Width * 0.5 + B * 0.5 + 2.5;
                double x = isl.X + ca * centre + sa * off;
                double z = isl.Z + sa * centre - ca * off;
                /* ON VÉRIFIE QUAND MÊME LE FOND. Un ponton court plonge vite, et la
                   racine d'un quai n'a pas toujours l'eau d'un navire — le poste du
                   joueur porte déjà cette cicatrice (« la Roter Löwe démarre dans le
                   sable »). */
                if (w.Navigable(x, z, draft + Sous))
                {
                    sorti.Add(new Poste(x, z, cap, false));
                    pris.Add((x, z, L * 0.5));
                    reste -= L + Ecart;
                    continue;
                }
            }

            // AU MOUILLAGE : un relèvement et une distance, tirés et éprouvés
            bool pose = false;
            for (int essai = 0; essai < 120 && !pose; essai++)
            {
                double a = Rnd() * Math.PI * 2;
                double d = RadeMin + Rnd() * (RadeMax - RadeMin);
                double x = p.Hx + Math.Cos(a) * d, z = p.Hz + Math.Sin(a) * d;
                if (!w.Navigable(x, z, draft + Sous)) continue;
                /* SON ÉVITAGE : un navire sur son ancre tourne autour d'elle, donc il
                   lui faut un rond libre et non un point. C'est la faute qu'on ne voit
                   qu'au premier changement de vent, quand deux coques se traversent. */
                double rond = L * Evitage;
                bool libre = true;
                foreach (var (ox, oz, or) in pris)
                {
                    double dx = x - ox, dz = z - oz, need = rond + or;
                    if (dx * dx + dz * dz < need * need) { libre = false; break; }
                }
                if (!libre) continue;
                // et pas dans l'axe du ponton, où le joueur doit passer
                double vx = x - p.Sx, vz = z - p.Sz, ex = p.Hx - p.Sx, ez = p.Hz - p.Sz;
                double ee = Math.Max(1e-9, ex * ex + ez * ez);
                double u = (vx * ex + vz * ez) / ee;
                if (u > 0 && u < 1.6)
                {
                    double px = vx - ex * u, pz = vz - ez * u;
                    if (px * px + pz * pz < 60 * 60) continue;     // il barrerait la passe
                }
                sorti.Add(new Poste(x, z, Rnd() * Math.PI * 2, true));
                pris.Add((x, z, rond));
                pose = true;
            }
        }
        return sorti;
    }
}
