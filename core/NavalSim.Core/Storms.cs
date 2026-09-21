namespace NavalSim.Core;

/// <summary>Une dépression : son centre (mètres monde VRAIS), son rayon, sa force au cœur, son sens.</summary>
public readonly record struct Storm(int I, int J, double X, double Z, double R, double Peak, int Spin);

/// <summary>Le temps qu'il fait en un point pris dans une dépression — ce que rend <see cref="Storms.At"/>.</summary>
public readonly record struct Squall(Storm Storm, double Dist, double Inten, double Force, double WindDeg,
                                     double Loom, double ToX, double ToZ);

/// <summary>
/// LE MAUVAIS TEMPS QUI A UN LIEU — storms.js, porté tel quel.
///
/// Écrit comme les îles : un hachage sur une grille grossière, pas de fichier,
/// pas d'état. Revenir aux mêmes eaux, c'est retrouver la même dépression ; mais
/// une dépression a aussi une HEURE — un système voyage —, c'est donc une fonction
/// pure de la position ET de l'horloge.
///
/// La dérive est une onde en TRIANGLE : bornée (une recherche des cellules
/// voisines la trouve toujours) ET continue (un modulo la ferait sauter, et un
/// saut, c'est quatre Beaufort d'une image à l'autre). Et elle porte à plus d'une
/// cellule : confinée à la sienne, une dépression allait et venait sur un navire
/// en cape, sans jamais le quitter.
///
/// Tout en mètres monde VRAIS : l'origine flottante fait glisser le zéro local.
/// </summary>
public sealed class Storms
{
    public int Seed = 76314529;
    /* Onze kilomètres entre dépressions candidates, deux à quatre de large :
       taillées pour un navire et non pour une carte météo. On les traverse en dix
       ou vingt minutes — assez pour une épreuve, assez peu pour en sortir. */
    public double Cell = 11000;
    public double Chance = 0.40;
    public double Drift = 5.5;          // m/s, la vitesse du système

    /* LE HACHAGE DE LA PAGE, AU BIT PRÈS. `(n ^ (n >>> 13)) * 195736897 | 0` y
       multiplie en DOUBLE un entier de 32 bits par un de 28 : le produit dépasse
       2^53 et s'arrondit AVANT d'être ramené à 32 bits. Le calculer en entiers
       serait plus juste — et placerait les dépressions ailleurs. On refait donc
       l'arrondi du double, puis le ToInt32 de JavaScript. */
    public double Hash(int i, int j, int k)
    {
        int n = ToInt32((double)i * 920419823 + (double)j * 195736897 + (double)k * 611879891 + Seed);
        n = ToInt32((double)(n ^ (int)((uint)n >> 13)) * 195736897);
        uint u = (uint)(n ^ (int)((uint)n >> 16));
        return u / 4294967296.0;
    }

    static int ToInt32(double v) => Js.ToInt32(v);     // une seule définition : Js.cs

    // de −1 à 1 et retour, continûment : la dérive qui ne saute jamais
    static double Tri(double u)
    {
        double f = u - Math.Floor(u);
        return f < 0.5 ? f * 4 - 1 : 3 - f * 4;
    }

    /// <summary>La dépression de la cellule (i, j) à l'instant t, s'il y en a une.</summary>
    public bool CellStorm(int i, int j, double t, out Storm s)
    {
        s = default;
        if (Hash(i, j, 0) > Chance) return false;
        double c = Cell;
        double r = 1800 + Hash(i, j, 1) * 2400;
        /* De la place pour voyager, NETTEMENT plus qu'une cellule : confinée à la
           sienne, l'intensité mesurée sur un navire en cape allait 1 · 0,74 ·
           0,01 · 0,82 · 0,99 sur une demi-heure — des accalmies, jamais de
           délivrance. */
        double room = c * 1.35 - r;
        double spd = Drift * (0.55 + Hash(i, j, 2));
        double per = Math.Max(60, 4 * room / spd);          // un aller-retour, à son allure
        double px = Hash(i, j, 3), pz = Hash(i, j, 4);
        s = new Storm(i, j,
            (i + 0.5) * c + room * Tri(t / per + px),
            (j + 0.5) * c + room * Tri(t / per * 0.83 + pz),
            r,
            7.2 + Hash(i, j, 5) * 2.3,                       // Beaufort au cœur
            Hash(i, j, 6) < 0.5 ? -1 : 1);                   // son sens de rotation
        return true;
    }

    /// <summary>
    /// LA PLUS PROCHE, AUSSI LOIN QU'IL FAUT : des anneaux de cellules de plus en
    /// plus larges jusqu'à trouver — pour « emmène-moi dans le gros temps ».
    /// <paramref name="ok"/> écarte celles qui ne conviennent pas (posées sur la
    /// terre) : la météo n'a pas à connaître les îles.
    /// </summary>
    public bool Nearest(double x, double z, double t, int ringsMax, Func<Storm, bool>? ok,
                        out Storm best, out double dist)
    {
        double c = Cell;
        int i0 = (int)Math.Floor(x / c), j0 = (int)Math.Floor(z / c);
        int R = Math.Max(1, ringsMax);
        best = default; dist = double.PositiveInfinity;
        for (int ring = 0; ring <= R; ring++)
        {
            bool found = false;
            for (int i = i0 - ring; i <= i0 + ring; i++)
                for (int j = j0 - ring; j <= j0 + ring; j++)
                {
                    // l'anneau seul : l'intérieur a été vu au tour précédent
                    if (ring > 0 && Math.Abs(i - i0) != ring && Math.Abs(j - j0) != ring) continue;
                    if (!CellStorm(i, j, t, out var s)) continue;
                    if (ok != null && !ok(s)) continue;
                    double d = Hypot(s.X - x, s.Z - z);
                    if (d < dist) { dist = d; best = s; found = true; }
                }
            if (found) return true;
        }
        return false;
    }

    /// <summary>
    /// Le temps en un point du monde : la dépression la plus proche, combien on y
    /// est enfoncé, et ce que fait le vent — ou faux, par temps clair. Deux
    /// anneaux de cellules, puisqu'un centre erre de plus d'une cellule.
    /// </summary>
    public bool At(double x, double z, double t, out Squall q)
    {
        q = default;
        double c = Cell;
        int i0 = (int)Math.Floor(x / c), j0 = (int)Math.Floor(z / c);
        Storm best = default;
        double bd = double.PositiveInfinity;
        bool any = false;
        for (int i = i0 - 2; i <= i0 + 2; i++)
            for (int j = j0 - 2; j <= j0 + 2; j++)
            {
                if (!CellStorm(i, j, t, out var s)) continue;
                double d = Hypot(s.X - x, s.Z - z);
                if (d < bd) { bd = d; best = s; any = true; }
            }
        if (!any || bd > best.R * 3.2) return false;

        /* Plus profond vers le milieu, et pas linéairement : une large épaule et
           un cœur dur — l'essentiel de la traversée n'est que du sale temps, et le
           dernier tiers est celui dont on se souvient. */
        double u = Math.Max(0, 1 - bd / best.R);
        double inten = u * u * (3 - 2 * u);

        /* Le vent TOURNE autour du centre, surtout tangent avec un peu de rentrant,
           comme une vraie dépression : on trouve le milieu au seul toucher du vent. */
        double nx = bd > 1 ? (x - best.X) / bd : 0, nz = bd > 1 ? (z - best.Z) / bd : 1;
        double tx = -nz * best.Spin, tz = nx * best.Spin;
        double wx = tx * 0.88 - nx * 0.34, wz = tz * 0.88 - nz * 0.34;
        // le relèvement D'OÙ il souffle, comme le dit un marin et comme le veut SetWind
        double windDeg = (Math.Atan2(wx, -wz) * 180 / Math.PI + 360) % 360;

        q = new Squall(best, bd, inten, best.Peak * inten, windDeg,
            /* Combien du ciel devant doit être noir : monte à l'approche, puis
               s'efface dans la grisaille générale une fois dedans. */
            Math.Max(0, Math.Min(1, (best.R * 2.6 - bd) / (best.R * 1.6))) * (1 - inten * 0.85),
            bd > 1 ? -nx : 0, bd > 1 ? -nz : 1);
        return true;
    }

    // Math.hypot de JavaScript : la racine de la somme des carrés, sans débordement ici
    static double Hypot(double a, double b) => Math.Sqrt(a * a + b * b);
}
