using System;

namespace NavalSim.Core;

/// <summary>
/// L'ABRI QUE DONNE LA FORME DU RIVAGE — ce que faisait le môle, mais sans môle :
/// une baie fermée calme la houle parce que la terre l'entoure, et c'est la terre
/// elle-même qui le dit.
///
/// Pour chaque point d'eau autour d'un port, on lance des rayons dans toutes les
/// directions et l'on compte ceux qui atteignent le LARGE — huit cents mètres
/// d'eau sans toucher terre — : la part de ciel marin qu'il voit. Une rive droite
/// en voit la moitié et reste exposée ; le fond d'un bassin n'en voit presque
/// rien. Calculé une fois, sur une grille de huit mètres, puis adouci : la houle
/// ne s'arrête pas net à l'angle d'une pointe, elle s'y tord et s'y épuise.
///
/// C'est une GRILLE, et non un calcul à la demande, parce que trois calculateurs
/// doivent lire le même abri (CLAUDE.md) : le shader de la mer la reçoit comme
/// une texture, l'écume aussi, et l'échantillonneur du noyau la lit ici. Un abri
/// que la coque sentirait sans que l'œil le voie est la faute qu'on a déjà payée
/// une fois avec le môle.
/// </summary>
public sealed class ShelterMap
{
    public const double Cell = 8;
    /// <summary>Jusqu'où un rayon doit filer sans terre pour avoir trouvé le large.</summary>
    public const double Reach = 800;
    const int Rays = 40;
    /// <summary>Le fond d'un bassin : ce qu'il reste de houle là où la terre ferme tout.</summary>
    public const double Floor = 0.12;

    public readonly double X0, Z0;
    public readonly int N;
    public readonly float[] V;

    ShelterMap(double x0, double z0, int n, float[] v) { X0 = x0; Z0 = z0; N = n; V = v; }

    public double Size => N * Cell;

    public bool Covers(double x, double z) => x >= X0 && z >= Z0 && x <= X0 + (N - 1) * Cell && z <= Z0 + (N - 1) * Cell;
    /// <summary>Does the map's square come within r metres of (x, z)?</summary>
    public bool Covers(double x, double z, double r) => x >= X0 - r && z >= Z0 - r && x <= X0 + (N - 1) * Cell + r && z <= Z0 + (N - 1) * Cell + r;

    /// <summary>L'abri en un point (mètres vrais), lu entre les nœuds de la grille ; 1 dehors.</summary>
    public double Sample(double x, double z)
    {
        double fi = (x - X0) / Cell, fj = (z - Z0) / Cell;
        if (fi < 0 || fj < 0 || fi > N - 1 || fj > N - 1) return 1;
        int i = Math.Min(N - 2, (int)fi), j = Math.Min(N - 2, (int)fj);
        double a = fi - i, b = fj - j;
        int k = j * N + i;
        return (V[k] * (1 - a) + V[k + 1] * a) * (1 - b) + (V[k + N] * (1 - a) + V[k + N + 1] * a) * b;
    }

    /// <summary>
    /// Bâtir l'abri d'un port : un carré de 2 <paramref name="radius"/> de côté
    /// autour de (<paramref name="cx"/>, <paramref name="cz"/>), en mètres vrais.
    /// </summary>
    public static ShelterMap Build(World w, double cx, double cz, double radius)
    {
        int n = (int)Math.Ceiling(2 * radius / Cell) + 1;
        double x0 = cx - radius, z0 = cz - radius;
        int margin = (int)Math.Ceiling(Reach / Cell);
        int m = n + 2 * margin;
        // la terre, une fois, sur la grille élargie de la portée d'un rayon
        var land = new bool[m * m];
        for (int j = 0; j < m; j++)
            for (int i = 0; i < m; i++)
                land[j * m + i] = w.HeightAt(x0 + (i - margin) * Cell, z0 + (j - margin) * Cell) > 0;

        var dirs = new (double X, double Z)[Rays];
        for (int r = 0; r < Rays; r++) dirs[r] = (Math.Cos(r * Math.Tau / Rays), Math.Sin(r * Math.Tau / Rays));

        var v = new float[n * n];
        System.Threading.Tasks.Parallel.For(0, n, j =>
        {
            for (int i = 0; i < n; i++)
            {
                int open = 0;
                foreach (var (dx, dz) in dirs)
                {
                    bool blocked = false;
                    for (int s = 1; s <= margin; s++)
                    {
                        int ii = i + margin + (int)Math.Round(dx * s), jj = j + margin + (int)Math.Round(dz * s);
                        if (land[jj * m + ii]) { blocked = true; break; }
                    }
                    if (!blocked) open++;
                }
                /* LA PART DU LARGE QU'IL VOIT, ramenée à l'abri : une rive droite voit
                   la moitié du ciel marin et doit rester battue ; c'est sous un
                   tiers qu'on entre dans une baie, et sous un huitième qu'on est au
                   fond d'un bassin. */
                double f = (double)open / Rays;
                v[j * n + i] = (float)(Floor + (1 - Floor) * MathX.SmoothStep(0.12, 0.42, f));
            }
        });

        // adoucir : deux passes d'une moyenne sur cinq mailles (quarante mètres)
        var t = new float[v.Length];
        for (int pass = 0; pass < 2; pass++)
        {
            for (int j = 0; j < n; j++)
                for (int i = 0; i < n; i++)
                {
                    double s = 0; int c = 0;
                    for (int dj = -2; dj <= 2; dj++)
                        for (int di = -2; di <= 2; di++)
                        {
                            int ii = i + di, jj = j + dj;
                            if (ii < 0 || jj < 0 || ii >= n || jj >= n) continue;
                            s += v[jj * n + ii]; c++;
                        }
                    t[j * n + i] = (float)(s / c);
                }
            Array.Copy(t, v, v.Length);
        }
        // et le bord du carré rejoint le large, pour qu'on n'y voie pas de couture
        for (int j = 0; j < n; j++)
            for (int i = 0; i < n; i++)
            {
                int e = Math.Min(Math.Min(i, j), Math.Min(n - 1 - i, n - 1 - j));
                double k = Math.Clamp(e / 10.0, 0, 1);
                v[j * n + i] = (float)(1 - (1 - v[j * n + i]) * k);
            }
        return new ShelterMap(x0, z0, n, v);
    }
}
