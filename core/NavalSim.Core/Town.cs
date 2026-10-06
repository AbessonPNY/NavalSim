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
                // le centre-ville a ses rues : on n'y sème rien
                bool inCentre = false;
                foreach (var g in world.Grids) if (g.Covers(x, z, 6)) { inCentre = true; break; }
                if (inCentre) continue;
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

    /// <summary>
    /// LES RUES D'UN CENTRE-VILLE. On cherche d'abord dans quel sens court la
    /// terre — l'axe principal des points d'herbe autour du port, ce qui donne la
    /// longueur d'une presqu'île sans qu'on ait à l'écrire —, puis on y aligne des
    /// rangées de pâtés : deux rangées DOS À DOS autour d'une cour, une rue de
    /// chaque côté, et la paire suivante de l'autre côté de la rue. Le long de
    /// l'axe, les pâtés se suivent à une ruelle près, et une rue de traverse coupe
    /// la rangée tous les quelques pâtés. Un pâté qui ne tient pas tout entier sur
    /// l'herbe, sur le plat, loin du ponton, n'est pas bâti : la ville s'arrête où
    /// la terre s'arrête, comme les vraies.
    /// </summary>
    public static StreetGrid? Streets(World w, Isle isl)
    {
        var c = isl.Centre!;
        double reach = c.Length * 0.75;

        // l'axe : la direction où les points d'herbe s'étalent le plus
        double sx = 0, sz = 0; int n = 0;
        var pts = new List<(double X, double Z)>();
        for (double x = isl.X - reach; x <= isl.X + reach; x += 6)
            for (double z = isl.Z - reach; z <= isl.Z + reach; z += 6)
            {
                if ((x - isl.X) * (x - isl.X) + (z - isl.Z) * (z - isl.Z) > reach * reach) continue;
                if (w.HeightAt(x, z) < c.HMin) continue;
                pts.Add((x, z)); sx += x; sz += z; n++;
            }
        if (n < 20) return null;
        double mx = sx / n, mz = sz / n, cxx = 0, czz = 0, cxz = 0;
        foreach (var (x, z) in pts) { cxx += (x - mx) * (x - mx); czz += (z - mz) * (z - mz); cxz += (x - mx) * (z - mz); }
        double th = 0.5 * Math.Atan2(2 * cxz, cxx - czz);
        double ux = Math.Cos(th), uz = Math.Sin(th);       // le long de la terre
        double vx = -uz, vz = ux;                           // en travers

        var g = new StreetGrid { Key = isl.Key, Glb = c.Glb, Cx = mx, Cz = mz, Ux = ux, Uz = uz, Street = c.Street, Face = c.Face };
        double pairPitch = 2 * c.Deep + c.Yard + c.Street;
        double rowOff = (c.Yard + c.Deep) * 0.5;
        int along = (int)(c.Length / (c.Long + c.Alley)) + 2;
        double hl = c.Long * 0.5, hd = c.Deep * 0.5;
        for (int p = 0; p < c.Pairs; p++)
            for (int side = -1; side <= 1; side += 2)
            {
                // les paires centrées sur l'axe : la rue du milieu y passe quand leur nombre est pair
                double v = (p - (c.Pairs - 1) * 0.5) * pairPitch + side * rowOff;
                // la façade regarde SA rue : celle qui est du côté opposé à la cour
                double fx = vx * side, fz = vz * side;
                double yaw = Math.Atan2(fx, fz) + c.Face * Math.PI / 180;
                for (int k = -along / 2; k <= along / 2; k++)
                {
                    // une traverse tous les « Cross » pâtés : la ruelle s'y élargit en rue
                    int blk = (int)Math.Floor((double)k / Math.Max(1, c.Cross));
                    double u = k * (c.Long + c.Alley) + blk * (c.Street - c.Alley);
                    if (Math.Abs(u) > c.Length * 0.5) continue;
                    double x = mx + ux * u + vx * v, z = mz + uz * u + vz * v;
                    double lo = double.MaxValue, hi = double.MinValue;
                    for (int q = 0; q < 9; q++)
                    {
                        double a = (q % 3 - 1) * hl, b = (q / 3 - 1) * hd;
                        double h = w.HeightAt(x + ux * a + vx * b, z + uz * a + vz * b);
                        lo = Math.Min(lo, h); hi = Math.Max(hi, h);
                    }
                    if (lo < c.HMin || hi > 45 || hi - lo > 1.5) continue;
                    if (Scatter.NearJetty(w, x, z, hl + 20)) continue;
                    // W et D aux neuf dixièmes : la règle des modèles, qui les mesure sans leurs débords
                    g.Blocks.Add(new House(x, lo, z, yaw, c.Long * 0.9, c.Deep * 0.9, 0, 0, 2));
                    g.Add(u, v, hl, hd);
                }
            }
        return g.Blocks.Count > 0 ? g : null;
    }
}

/// <summary>
/// La grille d'un centre-ville, dans son propre repère : u le long de la terre,
/// v en travers. Ses pâtés, et l'étendue qu'ils couvrent — rues comprises.
/// </summary>
public sealed class StreetGrid
{
    public string Key = "", Glb = "";
    public double Cx, Cz, Ux, Uz, Street;
    /// <summary>De combien la façade du modèle est tournée par rapport à son +z, en degrés (la fiche : « facade »).</summary>
    public double Face;
    public readonly List<House> Blocks = new();
    readonly List<(double U, double V, double Hl, double Hd)> _cells = new();
    double _u0 = double.MaxValue, _u1 = double.MinValue, _v0 = double.MaxValue, _v1 = double.MinValue;

    internal void Add(double u, double v, double hl, double hd)
    {
        _cells.Add((u, v, hl, hd));
        _u0 = Math.Min(_u0, u - hl); _u1 = Math.Max(_u1, u + hl);
        _v0 = Math.Min(_v0, v - hd); _v1 = Math.Max(_v1, v + hd);
    }

    /// <summary>
    /// À une rue près d'un de ses pâtés, à <paramref name="margin"/> près ? L'UNION
    /// des pâtés élargis d'une rue, et non leur boîte : là où la terre a refusé un
    /// pâté, il n'y a ni maison ni pavé — une boîte pavait des bandes vides.
    /// </summary>
    public bool Covers(double x, double z, double margin)
    {
        double dx = x - Cx, dz = z - Cz;
        double u = dx * Ux + dz * Uz, v = -dx * Uz + dz * Ux;
        double m = Street + margin;
        if (u < _u0 - m || u > _u1 + m || v < _v0 - m || v > _v1 + m) return false;
        foreach (var (cu, cv, hl, hd) in _cells)
            if (Math.Abs(u - cu) < hl + m && Math.Abs(v - cv) < hd + m) return true;
        return false;
    }

    /// <summary>Une rue pavée : dans l'emprise, la rue qui la borde comprise.</summary>
    public bool Paved(double x, double z) => Covers(x, z, 0);
}
