using System;
using System.Collections.Generic;

namespace NavalSim.Core;

/// <summary>Une chose à ramasser au fond, en mètres vrais.</summary>
public sealed class Find
{
    /// <summary>Stable d'une partie à l'autre : ce qui est ramassé le reste (la sauvegarde le retient).</summary>
    public string Id = "";
    /// <summary>« lambi », « huitre », « ecus » — une sorte de treasure/tresor.json, sauf l'huître, qu'on ouvre à bord.</summary>
    public string Kind = "";
    public double X, Y, Z, Yaw;
    /// <summary>Penché sur la pente d'un rocher : son appui, vers où il regarde.</summary>
    public Vec3d Up = new(0, 1, 0);
}

/// <summary>
/// CE QUI SE RAMASSE EN APNÉE — tiré par CASES de trente-deux mètres, sur une graine
/// qui ne dépend que de la case : la même case rend les mêmes coquillages d'une
/// partie à l'autre, et on n'en tire que là où nage quelqu'un.
///
/// LE LAMBI (Aliger gigas, le « queen conch ») vit sur l'HERBIER et le sable blanc
/// qui le borde, d'un à vingt mètres — il broute les algues des feuilles. Il n'aime
/// ni la vase ni le corail : on ne l'y met pas.
///
/// L'HUÎTRE PERLIÈRE des Caraïbes (Pinctada imbricata, celle des pêcheries de
/// Margarita et de Cubagua) s'accroche au DUR : les récifs, les rochers du fond, de
/// deux à quinze mètres. On l'ouvre à bord ; une sur douze rend une perle, ce qui est
/// GÉNÉREUX — les pêcheries en ouvraient des centaines pour une perle marchande.
///
/// LES PIÈCES ÉPARSES d'une épave peu profonde (vingt-cinq mètres au plus) : un
/// navire qui sombre sème sa bourse autour de lui. Elles ne repoussent pas.
/// </summary>
public static class Finds
{
    public const double Cell = 32;
    /// <summary>Ce qu'on tente de poser par case ; la nature du fond décide du reste.</summary>
    const int Tries = 6;
    /// <summary>Un coquillage ramassé repousse au bout de tant de jours (un autre vient brouter là).</summary>
    public const int RegrowDays = 30;
    /// <summary>Les pièces : jusqu'à cette profondeur, un nageur peut les atteindre.</summary>
    public const double CoinDepth = 25;
    /// <summary>Une huître sur tant rend une perle.</summary>
    public const int PearlOdds = 12;

    public static (int, int) CellOf(double x, double z) => ((int)Math.Floor(x / Cell), (int)Math.Floor(z / Cell));

    /// <summary>Un tirage de 0 à 1, stable : SplitMix64 sur la case et le rang.</summary>
    public static double Seed(string region, int i, int j, int k)
    {
        ulong h = 0x9E3779B97F4A7C15UL;
        foreach (char c in region) h = (h ^ c) * 0x100000001B3UL;
        h ^= (ulong)(uint)i * 0xBF58476D1CE4E5B9UL;
        h ^= (ulong)(uint)j * 0x94D049BB133111EBUL;
        h += (ulong)k * 0x9E3779B97F4A7C15UL;
        h = (h ^ (h >> 30)) * 0xBF58476D1CE4E5B9UL;
        h = (h ^ (h >> 27)) * 0x94D049BB133111EBUL;
        h ^= h >> 31;
        return (h >> 11) * (1.0 / (1UL << 53));
    }

    /// <summary>Les coquillages de cette case.</summary>
    public static void InCell(World w, string region, int i, int j, List<Find> into, List<RockField.Rock> rocks)
    {
        for (int k = 0; k < Tries; k++)
        {
            double x = (i + Seed(region, i, j, k * 4)) * Cell, z = (j + Seed(region, i, j, k * 4 + 1)) * Cell;
            double u = Seed(region, i, j, k * 4 + 2), yaw = Seed(region, i, j, k * 4 + 3) * Math.PI * 2;
            double bed = w.HeightAt(x, z), depth = -bed;
            if (depth < 1) continue;
            string id = $"{region}:{i}:{j}:{k}";
            if (depth <= 15 && w.ReefAt(x, z) > 0.25)
            {
                if (u < 0.5) into.Add(new Find { Id = "huitre:" + id, Kind = "huitre", X = x, Y = bed, Z = z, Yaw = yaw });
                continue;
            }
            if (depth > 20) continue;
            double meadow = Seabed.Meadow(w, x, z), mud = Seabed.Mud(w, x, z);
            double p = meadow > 0.4 ? 0.45 : mud < 0.3 ? 0.12 : 0;   // l'herbier, son sable ; jamais la vase
            if (u < p) into.Add(new Find { Id = "lambi:" + id, Kind = "lambi", X = x, Y = bed, Z = z, Yaw = yaw });
        }
        /* LES ROCHERS DU FOND : une huître sur un sur trois, collée à son flanc, à mi-pente,
           du côté que le tirage choisit */
        w.Rocks.Near((i + 0.5) * Cell, (j + 0.5) * Cell, Cell * 0.71, rocks);
        foreach (var rk in rocks)
        {
            if (CellOf(rk.X, rk.Z) != (i, j)) continue;          // la case du rocher seule : pas de doublon
            if (-rk.Base < 2 || -rk.Base > 15) continue;
            double v = Seed(region, (int)Math.Round(rk.X * 4), (int)Math.Round(rk.Z * 4), 99);
            if (v > 0.33) continue;
            double a = v * 3 * Math.PI * 2, d = rk.R * 0.72;
            double x = rk.X + Math.Sin(a) * d, z = rk.Z + Math.Cos(a) * d;
            double top = RockField.TopAt(rk, x, z);
            if (double.IsNegativeInfinity(top)) continue;
            // la pente de la demi-ellipsoïde à cet endroit : la coquille s'y couche
            double h = rk.Top - rk.Base, q = 0.72 * 0.72, s = h / rk.R * 0.72 / Math.Sqrt(1 - q);
            var up = new Vec3d(Math.Sin(a) * s, 1, Math.Cos(a) * s);
            into.Add(new Find
            {
                // l id va dans la sauvegarde : jamais la virgule d une langue
                Id = FormattableString.Invariant($"huitre:{region}:r{Math.Round(rk.X, 1)}:{Math.Round(rk.Z, 1)}"), Kind = "huitre",
                X = x, Y = top, Z = z, Yaw = a, Up = up * (1 / up.Length)
            });
        }
    }

    /// <summary>Les pièces semées autour d'une épave assez peu profonde pour un nageur.</summary>
    public static void AroundWreck(World w, Wreck wr, List<Find> into)
    {
        if (wr.Depth > CoinDepth) return;
        int n = 8 + (int)(Seed(wr.Id, 0, 0, 0) * 10);
        double reach = Math.Max(4, wr.Beam) * 1.5 + 6;
        for (int k = 0; k < n; k++)
        {
            double a = Seed(wr.Id, 1, k, 1) * Math.PI * 2, r = Math.Sqrt(Seed(wr.Id, 1, k, 2)) * reach;
            double x = wr.X + Math.Sin(a) * r, z = wr.Z + Math.Cos(a) * r;
            into.Add(new Find { Id = $"ecus:{wr.Id}:{k}", Kind = "ecus", X = x, Y = w.HeightAt(x, z), Z = z, Yaw = a * 7 });
        }
    }
}
