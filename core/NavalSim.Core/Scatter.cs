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

        for (int tries = 0; tries < s.Count * 400 && outp.Count < s.Count; tries++)
        {
            double x = x0 + (x1 - x0) * R(), z = z0 + (z1 - z0) * R();
            double size = s.SizeMin + (s.SizeMax - s.SizeMin) * R() * R();     // les petits sont les plus nombreux
            double yaw = R() * Math.PI * 2, ta = R() * Math.PI * 2, tm = R() * s.Tilt * Math.PI / 180;
            if ((x - cx) * (x - cx) + (z - cz) * (z - cz) > r2) continue;
            double h = w.HeightAt(x, z);
            if (h < s.HMin || h > s.HMax) continue;
            if (NearJetty(w, x, z, 35)) continue;
            bool free = true;
            foreach (var p in outp)
            {
                double need = (p.Size + size) * 0.6;
                if ((p.X - x) * (p.X - x) + (p.Z - z) * (p.Z - z) < need * need) { free = false; break; }
            }
            if (!free) continue;
            outp.Add(new ScatterPlace(x, z, yaw, Math.Cos(ta) * tm, Math.Sin(ta) * tm, size));
        }
        return outp;
    }

    static bool NearJetty(World w, double x, double z, double d)
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
