using System;
using System.Collections.Generic;

namespace NavalSim.Core;

/// <summary>Une maison posée : où elle est, comment elle est tournée, et sa taille.</summary>
public readonly record struct House(double X, double Y, double Z, double Yaw,
                                    double W, double D, double H, double RoofH, int Kind);

/// <summary>
/// UNE VILLE, SEMÉE SUR LE RELIEF — ce que la page n'a pas : elle n'avait que des
/// modèles posés un à un.
///
/// Rien n'est dessiné ici : ce fichier ne fait que DIRE où sont les maisons, et
/// c'est ce qui permet de les semer une fois pour toutes, de les retrouver
/// identiques d'une partie à l'autre, et à la carte marine de les connaître
/// aussi. Le semis est une fonction pure de la graine et du relief.
///
/// Les règles sont celles d'un lieu habité de cette côte : on bâtit sur le PLAT,
/// à portée de l'eau mais hors d'atteinte d'une lame, et les façades regardent la
/// rade — les rues d'une ville de rivage suivent le rivage.
/// </summary>
public static class Town
{
    /// <summary>La maille du semis : une maison par case, placée au hasard dedans.</summary>
    public const double Cell = 17;

    /// <summary>
    /// Semer autour d'un point du monde. <paramref name="radius"/> est la portée
    /// de la ville, <paramref name="want"/> le nombre de maisons souhaité — on en
    /// rend moins si le terrain ne s'y prête pas, jamais plus.
    /// </summary>
    public static List<House> Plant(World world, double cx, double cz,
                                    double radius, int want, uint seed)
    {
        var outp = new List<House>(want);
        uint s = seed | 1;
        double Rnd() { s ^= s << 13; s ^= s >> 17; s ^= s << 5; return (s & 0xFFFFFF) / 16777216.0; }

        /* En SPIRALE depuis le centre, case par case : une ville est dense au
           port et s'effiloche vers ses bords, ce qu'un tirage uniforme dans un
           disque ne donne jamais. On s'arrête quand on a le compte ou quand on
           est sorti de la portée. */
        int ring = 0;
        while (outp.Count < want && ring * Cell < radius)
        {
            ring++;
            int steps = Math.Max(8, (int)(2 * Math.PI * ring));
            for (int k = 0; k < steps && outp.Count < want; k++)
            {
                double a = 2 * Math.PI * k / steps;
                double r = ring * Cell;
                double x = cx + Math.Cos(a) * r + (Rnd() - 0.5) * Cell * 0.9;
                double z = cz + Math.Sin(a) * r + (Rnd() - 0.5) * Cell * 0.9;

                // plus on s'éloigne du port, plus les parcelles se vident
                if (Rnd() > 1.02 - 0.75 * (r / radius)) continue;

                double h = world.IslandHeight(x, z);
                // sur la terre, au-dessus de la lame, et pas à flanc de montagne
                if (h < 1.2 || h > 45) continue;
                double sd = world.ShoreDistance(x, z);
                if (sd > -11 || sd < -700) continue;        // négatif à terre : du bord, sans aller au loin

                /* LA PENTE, mesurée sur douze mètres — la largeur d'une maison.
                   On ne bâtissait pas à flanc : un terrain à plus d'un sur quatre
                   demande des terrasses, et ce n'est pas cette côte-là. */
                double hx = world.IslandHeight(x + 6, z) - world.IslandHeight(x - 6, z);
                double hz = world.IslandHeight(x, z + 6) - world.IslandHeight(x, z - 6);
                double slope = Math.Sqrt(hx * hx + hz * hz) / 12;
                if (slope > 0.28) continue;

                /* LA FAÇADE REGARDE L'EAU. Le gradient du champ de distance
                   pointe vers le large ; la rue lui est perpendiculaire, et les
                   maisons s'alignent dessus à quelques degrés près. Une maison
                   sur six se met en travers : une ville n'est pas un régiment. */
                double gx = world.ShoreDistance(x + 20, z) - world.ShoreDistance(x - 20, z);
                double gz = world.ShoreDistance(x, z + 20) - world.ShoreDistance(x, z - 20);
                double yaw = Math.Atan2(gx, gz) + (Rnd() - 0.5) * 0.28;
                if (Rnd() < 0.17) yaw += Math.PI / 2;

                int kind = Rnd() < 0.12 ? 1 : 0;            // 1 : un grand bâtiment, entrepôt ou halle
                double w = kind == 1 ? 9 + Rnd() * 7 : 5 + Rnd() * 4;
                double d = kind == 1 ? 12 + Rnd() * 9 : 6 + Rnd() * 5;
                double wall = kind == 1 ? 6.5 + Rnd() * 3 : 3.2 + Rnd() * 2.2;
                /* LE TOIT SE MESURE SUR LA LARGEUR, jamais en mètres absolus :
                   une pente de toit est un RAPPORT, et le même nombre de mètres
                   sur une halle et sur une masure donne à l'une une casquette et
                   à l'autre un clocher. Un demi de la demi-largeur fait 45°,
                   la pente des tuiles sous ces pluies-là. */
                double roof = w * (0.40 + Rnd() * 0.16);

                /* POSÉE SUR SON POINT LE PLUS BAS. La hauteur lue au centre
                   laisse une maison en porte-à-faux dès que le terrain penche un
                   peu — vu à la capture, des maisons du bord semblaient flotter
                   sur l'eau. */
                double hw = w * 0.5, hd = d * 0.5;
                double ca = Math.Cos(yaw), sa2 = Math.Sin(yaw);
                double y = h;
                for (int c = 0; c < 4; c++)
                {
                    double ox = (c < 2 ? -hw : hw), oz = (c % 2 == 0 ? -hd : hd);
                    double cx2 = x + ox * ca + oz * sa2, cz2 = z - ox * sa2 + oz * ca;
                    y = Math.Min(y, world.IslandHeight(cx2, cz2));
                }
                outp.Add(new House(x, y, z, yaw, w, d, wall, roof, kind));
            }
        }
        return outp;
    }
}
