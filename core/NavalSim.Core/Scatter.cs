using System;
using System.Collections.Generic;

namespace NavalSim.Core;

/// <summary>
/// UN SEMIS : un même modèle répandu au hasard sur une zone — les rochers d'une
/// plage. world/*.json → semis. Le hasard est TENU par une graine : on retrouve
/// les mêmes rochers aux mêmes places d'une partie à l'autre, sans avoir à les
/// écrire un à un.
/// </summary>
public sealed class ScatterSpec
{
    public string Name = "", Glb = "";
    /// <summary>La zone : la clé d'un carreau de relief (patches), ou un cercle (lat, lon, rayon).</summary>
    public string Patch = "";
    public double Lat, Lon, Radius = 300;
    public int Count = 30;
    /// <summary>Sa plus grande dimension, en mètres, tirée entre les deux : le .glb y est ramené quelle que soit son échelle.</summary>
    public double SizeMin = 0.5, SizeMax = 2;
    /// <summary>La frange où il se pose, en mètres d'altitude : la plage, de l'eau à la laisse de haute mer.</summary>
    public double HMin = -0.4, HMax = 1.8;
    /// <summary>De combien il penche au plus, en degrés : un rocher ne repose jamais d'aplomb.</summary>
    public double Tilt = 18;
    public int Seed = 1;
    /// <summary>L'écart minimal entre deux, en mètres ; nul, il va comme leur taille (des rochers). Un arbre a sa couronne, pas sa hauteur, à loger.</summary>
    public double Gap;
    /// <summary>Jusqu'où on le dessine, en mètres : un rocher se perd à neuf cents, un cocotier se voit du large.</summary>
    public double Visible = 900;
    /// <summary>De combien il s'enfonce, en part de sa demi-hauteur : 0,4 pour un rocher qui sort du sable, presque rien pour un arbre.</summary>
    public double Sink = 0.4;
    /// <summary>
    /// L'ABRI où il vit, de 0 (le fond d'une rade) à 1 (le large) — World.Shelter, le
    /// même que celui de la mer. Un corail ne pousse pas dans la vase d'un port ; un
    /// herbier n'aime pas la houle du large.
    /// </summary>
    public double ShelterMin = 0, ShelterMax = 1;
    /// <summary>
    /// EN AMAS : <see cref="Clusters"/> centres tirés dans la zone, et chaque pièce à
    /// moins de <see cref="ClusterR"/> mètres de l'un d'eux — un récif est fait de
    /// pâtés, pas d'un semis égal. Nul : partout.
    /// </summary>
    public int Clusters;
    public double ClusterR = 25;
    /// <summary>
    /// UNE FOULE : des milliers de petites pièces, dessinées par paquets (MultiMesh)
    /// et non une à une, et donc pas retouchables pièce par pièce en mode création.
    /// </summary>
    public bool Crowd;
    /// <summary>SUR LES PRÉS DE L'HERBIER seulement, ceux que le fond peint (Seabed.Meadow) : la touffe pousse sur sa plaque verte.</summary>
    public bool Meadow;
    /// <summary>
    /// IL ONDULE avec le ressac de la houle (Godot : seaweed.gdshader) — sa
    /// souplesse : 1 pour l'herbe, moins pour une gorgone cornée ; nul, il est de
    /// pierre. Une foule seulement.
    /// </summary>
    public double Sway;
    /// <summary>UN ÉCUEIL : la coque le heurte et s'y ouvre (World.Rocks) — un rocher, une tête de corail.</summary>
    public bool Hazard;
    /// <summary>SUR LES RÉCIFS seulement (World.ReefAt) : le corail des cayes.</summary>
    public bool OnReef;
}

/// <summary>Une place tirée : où, comment tourné, comment penché, de quelle taille.</summary>
public readonly record struct ScatterPlace(double X, double Z, double Yaw, double TiltX, double TiltZ, double Size);

public static class Scatter
{
    /// <summary>
    /// LES PLACES D'UN SEMIS, en mètres vrais. On tire dans la zone et l'on ne
    /// garde que ce qui tombe dans la FRANGE (une plage : au bord de l'eau, pas sur
    /// la colline ni au fond), assez loin d'un ponton pour ne pas barrer un
    /// mouillage, et assez loin d'un voisin pour que deux rochers ne s'entrent pas
    /// l'un dans l'autre. Ce qui ne trouve pas de place après bien des essais est
    /// abandonné : un semis clairsemé vaut mieux qu'un rocher dans l'eau du port.
    /// </summary>
    public static List<ScatterPlace> Place(World w, ScatterSpec s)
    {
        var outp = new List<ScatterPlace>();
        uint st = (uint)(s.Seed * 2654435761u) | 1;
        foreach (char c in s.Name) st = (st ^ c) * 16777619 | 1;
        double R() { st ^= st << 13; st ^= st >> 17; st ^= st << 5; return (st & 0xFFFFFF) / 16777216.0; }

        // la zone, en mètres vrais
        double x0, x1, z0, z1, cx = 0, cz = 0, r2 = double.MaxValue;
        var patch = s.Patch.Length > 0 ? w.Region.Patches.Find(p => p.Key == s.Patch) : null;
        if (patch != null)
        {
            var (ax, az) = w.Geo.ToXZ(patch.South, patch.West);
            var (bx, bz) = w.Geo.ToXZ(patch.North, patch.East);
            x0 = Math.Min(ax, bx); x1 = Math.Max(ax, bx); z0 = Math.Min(az, bz); z1 = Math.Max(az, bz);
        }
        else
        {
            (cx, cz) = w.Geo.ToXZ(s.Lat, s.Lon);
            x0 = cx - s.Radius; x1 = cx + s.Radius; z0 = cz - s.Radius; z1 = cz + s.Radius;
            r2 = s.Radius * s.Radius;
        }

        bool Fits(double x, double z)
        {
            if ((x - cx) * (x - cx) + (z - cz) * (z - cz) > r2) return false;
            double h = w.HeightAt(x, z);
            if (h < s.HMin || h > s.HMax) return false;
            if (s.ShelterMin > 0 || s.ShelterMax < 1)
            {
                double sh = w.Shelter(x, z);
                if (sh < s.ShelterMin || sh > s.ShelterMax) return false;
            }
            if (s.Meadow && Seabed.Meadow(w, x, z) < 0.5) return false;
            if (s.OnReef && w.ReefAt(x, z) < 0.6) return false;
            return !NearJetty(w, x, z, 35);
        }

        /* LES PÂTÉS d'abord, s'il en faut : des centres qui tombent eux-mêmes où il
           faut. Tirés sur la GRAINE SEULE, pas sur le nom : deux espèces de même
           graine dans la même zone se retrouvent sur les mêmes récifs — le corail,
           la gorgone et l'éponge vivent ensemble. */
        uint sc = (uint)(s.Seed * 2654435761u) | 1;
        double RC() { sc ^= sc << 13; sc ^= sc >> 17; sc ^= sc << 5; return (sc & 0xFFFFFF) / 16777216.0; }
        var centres = new List<(double X, double Z)>();
        for (int tries = 0; tries < s.Clusters * 400 && centres.Count < s.Clusters; tries++)
        {
            double x = x0 + (x1 - x0) * RC(), z = z0 + (z1 - z0) * RC();
            if (Fits(x, z)) centres.Add((x, z));
        }
        if (s.Clusters > 0 && centres.Count == 0) return outp;

        /* LES VOISINS PAR CASES : une foule compte des milliers de pièces, et les
           comparer toutes deux à deux à chaque essai prendrait des secondes. */
        double cell = Math.Max(0.5, s.Gap > 0 ? s.Gap : s.SizeMax * 1.2);
        var grid = new Dictionary<(int, int), List<int>>();
        (int, int) Key(double x, double z) => ((int)Math.Floor(x / cell), (int)Math.Floor(z / cell));

        /* UNE FOULE SANS PÂTÉS SE TRIE D'ABORD SUR UNE GRILLE de douze mètres : seules
           les cases dont le centre convient sont gardées, et l'on ne tire plus qu'en
           elles. Tirer au hasard sur toute la zone demandait des millions d'essais pour
           trouver les prés d'herbier (deux sur mille tombaient juste) — 6,8 s au
           chargement. Le semis pièce à pièce garde son tirage : ses retouches en
           dépendent. */
        const double Coarse = 12;
        List<(double X, double Z)>? kept = null;
        if (s.Crowd && centres.Count == 0)
        {
            kept = new List<(double X, double Z)>();
            for (double gz = z0; gz < z1; gz += Coarse)
                for (double gx = x0; gx < x1; gx += Coarse)
                    if (Fits(gx + Coarse / 2, gz + Coarse / 2)) kept.Add((gx, gz));
            if (kept.Count == 0) return outp;
        }

        for (int tries = 0; tries < s.Count * 400 && outp.Count < s.Count; tries++)
        {
            double x, z;
            if (centres.Count > 0)
            {
                // autour d'un pâté : serré au cœur, clairsemé au bord
                var c = centres[(int)(R() * centres.Count) % centres.Count];
                double a = R() * Math.PI * 2, d = s.ClusterR * Math.Sqrt(R()) * (0.4 + 0.6 * R());
                x = c.X + Math.Cos(a) * d; z = c.Z + Math.Sin(a) * d;
            }
            else if (kept != null)
            {
                var c = kept[(int)(R() * kept.Count) % kept.Count];
                x = c.X + Coarse * R(); z = c.Z + Coarse * R();
            }
            else { x = x0 + (x1 - x0) * R(); z = z0 + (z1 - z0) * R(); }
            double size = s.SizeMin + (s.SizeMax - s.SizeMin) * R() * R();     // les petits sont les plus nombreux
            double yaw = R() * Math.PI * 2, ta = R() * Math.PI * 2, tm = R() * s.Tilt * Math.PI / 180;
            if (!Fits(x, z)) continue;
            // sous l'eau, rien ne crève la surface : la pièce tient dans l'eau qui la couvre
            if (s.HMax <= 0 && size > -w.HeightAt(x, z) * 0.85) continue;
            bool free = true;
            var (ki, kj) = Key(x, z);
            for (int dj = -1; dj <= 1 && free; dj++)
                for (int di = -1; di <= 1 && free; di++)
                {
                    if (!grid.TryGetValue((ki + di, kj + dj), out var near)) continue;
                    foreach (int q in near)
                    {
                        var p = outp[q];
                        double need = s.Gap > 0 ? s.Gap : (p.Size + size) * 0.6;
                        if ((p.X - x) * (p.X - x) + (p.Z - z) * (p.Z - z) < need * need) { free = false; break; }
                    }
                }
            if (!free) continue;
            if (!grid.TryGetValue((ki, kj), out var mine)) grid[(ki, kj)] = mine = new List<int>();
            mine.Add(outp.Count);
            outp.Add(new ScatterPlace(x, z, yaw, Math.Cos(ta) * tm, Math.Sin(ta) * tm, size));
        }
        return outp;
    }

    internal static bool NearJetty(World w, double x, double z, double d)
    {
        foreach (var isl in w.Isles)
        {
            var p = isl.Port;
            if (p.Hx == 0 && p.Hz == 0) continue;
            double ex = p.Hx - p.Sx, ez = p.Hz - p.Sz, ee = Math.Max(1e-9, ex * ex + ez * ez);
            double u = Math.Clamp(((x - p.Sx) * ex + (z - p.Sz) * ez) / ee, 0, 1);
            double dx = x - (p.Sx + ex * u), dz = z - (p.Sz + ez * u);
            if (dx * dx + dz * dz < d * d) return true;
        }
        return false;
    }
}
