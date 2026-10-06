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
    /// <summary>
    /// À L'ÉCART DU BÂTI, de tant de mètres (World.Built : villes, rues, ce qu'on a posé) :
    /// la végétation ne pousse ni dans une rue ni dans une maison. Nul : partout.
    /// </summary>
    public double Clear;
    /// <summary>
    /// EN EAU CALME seulement : à moins de ce nombre de mètres d'eau libre dans presque
    /// toutes les directions (Scatter.Calm) — la mangrove ne tient que là où la houle
    /// n'arrive pas : une rade, une baie fermée, l'envers d'un cordon. Nul : partout.
    /// </summary>
    public double Calm;
    /// <summary>
    /// HORS DE CE CERCLE (lat, lon, rayon en mètres) : une zone déjà semée plus finement
    /// par un autre semis — la rade de Port-Royal dans le semis de toute l'île.
    /// </summary>
    public double OutLat, OutLon, OutRadius;
    /// <summary>IL PORTE OMBRE (une foule de terre : un arbre sans ombre flotte au-dessus du sol) ; le fond et les touffes s'en passent.</summary>
    public bool Shadow;
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

        double ox = 0, oz = 0, or2 = -1;
        if (s.OutRadius > 0) { (ox, oz) = w.Geo.ToXZ(s.OutLat, s.OutLon); or2 = s.OutRadius * s.OutRadius; }

        bool Fits(double x, double z) => FitsBand(x, z, 0, double.NaN);
        bool FitsBand(double x, double z, double slack, double known)
        {
            if ((x - cx) * (x - cx) + (z - cz) * (z - cz) > r2) return false;
            if (or2 > 0 && (x - ox) * (x - ox) + (z - oz) * (z - oz) < or2) return false;
            double h = double.IsNaN(known) ? w.HeightAt(x, z) : known;
            if (h < s.HMin - slack || h > s.HMax + slack) return false;
            if (s.ShelterMin > 0 || s.ShelterMax < 1)
            {
                double sh = w.Shelter(x, z);
                if (sh < s.ShelterMin || sh > s.ShelterMax) return false;
            }
            if (s.Meadow && Seabed.Meadow(w, x, z) < 0.5) return false;
            if (s.OnReef && w.ReefAt(x, z) < 0.6) return false;
            if (s.Clear > 0 && w.Built(x, z, s.Clear)) return false;
            if (s.Calm > 0 && !CalmAt(x, z)) return false;
            return !NearJetty(w, x, z, 35);
        }

        /* L'EAU CALME, gardée par cases de 150 m : elle coûte seize rayons de quelques
           kilomètres, et ne change pas d'un mètre à l'autre. */
        var calmMemo = new Dictionary<(int, int), bool>();
        bool CalmAt(double x, double z)
        {
            var k = ((int)Math.Floor(x / 150), (int)Math.Floor(z / 150));
            if (calmMemo.TryGetValue(k, out bool c)) return c;
            return calmMemo[k] = Calm(w, (k.Item1 + 0.5) * 150, (k.Item2 + 0.5) * 150, s.Calm);
        }

        /* LES PÂTÉS d'abord, s'il en faut : des centres qui tombent eux-mêmes où il
           faut. Tirés sur la GRAINE SEULE, pas sur le nom : deux espèces de même
           graine dans la même zone se retrouvent sur les mêmes récifs — le corail,
           la gorgone et l'éponge vivent ensemble. */
        uint sc = (uint)(s.Seed * 2654435761u) | 1;
        double RC() { sc ^= sc << 13; sc ^= sc >> 17; sc ^= sc << 5; return (sc & 0xFFFFFF) / 16777216.0; }
        var centres = new List<(double X, double Z)>();
        bool wide = Math.Max(x1 - x0, z1 - z0) > 30000;
        if (wide && s.Clusters > 0)
        {
            /* SUR TOUTE UNE ÎLE, les centres sont tirés parmi les cases qui conviennent (les
               altitudes partagées, HeightGrid) : au hasard sur la zone, neuf essais sur dix
               tombaient en mer, chacun une lecture du relief. */
            const double Cs = 48;
            var hg = HeightGrid(w, x0, z0, x1, z1, Cs);
            int nx = (int)Math.Ceiling((x1 - x0) / Cs), nz = (int)Math.Ceiling((z1 - z0) / Cs);
            var cand = new List<int>();
            for (int i = 0; i < hg.Length; i++)
                if (hg[i] >= s.HMin && hg[i] <= s.HMax) cand.Add(i);
            for (int tries = 0; tries < s.Clusters * 40 && centres.Count < s.Clusters && cand.Count > 0; tries++)
            {
                int c = cand[(int)(RC() * cand.Count) % cand.Count];
                double x = x0 + (c % nx + RC()) * Cs, z = z0 + (c / nx + RC()) * Cs;
                if (Fits(x, z)) centres.Add((x, z));
            }
        }
        else
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
        /* SUR TOUTE UNE ÎLE, des cases de 48 m : à douze, cent kilomètres de côté feraient
           soixante millions d'essais au chargement. Le relief ne vaut que 45 m au pixel ;
           la bande d'altitude est élargie d'un mètre et demi pour ce tri grossier, sans
           quoi une grève étroite tomberait entre deux centres de case — chaque pièce, elle,
           est encore éprouvée à la lettre. */
        double Coarse = wide ? 48 : 12;
        List<(double X, double Z)>? kept = null;
        if (s.Crowd && centres.Count == 0)
        {
            kept = new List<(double X, double Z)>();
            // les altitudes d'une grande zone, lues une fois et partagées par toutes ses espèces
            float[]? hg = wide ? HeightGrid(w, x0, z0, x1, z1, Coarse) : null;
            int nx = (int)Math.Ceiling((x1 - x0) / Coarse);
            int iz = 0;
            for (double gz = z0; gz < z1; gz += Coarse, iz++)
            {
                int ix = 0;
                for (double gx = x0; gx < x1; gx += Coarse, ix++)
                    if (FitsBand(gx + Coarse / 2, gz + Coarse / 2, wide ? 1.5 : 0, hg != null ? hg[iz * nx + ix] : double.NaN)) kept.Add((gx, gz));
            }
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

    /* LES ALTITUDES D'UNE GRANDE ZONE, aux centres de ses cases grossières : lues une
       fois, gardées pour toutes les espèces qui la partagent (toute l'île en compte
       neuf) — c'était des millions de lectures du relief, recommencées neuf fois. */
    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<World, Dictionary<(long, long, long, long, int), float[]>> _heightGrids = new();

    static float[] HeightGrid(World w, double x0, double z0, double x1, double z1, double step)
    {
        var byZone = _heightGrids.GetOrCreateValue(w);
        var key = ((long)Math.Round(x0), (long)Math.Round(z0), (long)Math.Round(x1), (long)Math.Round(z1), (int)step);
        lock (byZone)
        {
            if (byZone.TryGetValue(key, out var had)) return had;
            int nx = (int)Math.Ceiling((x1 - x0) / step), nz = (int)Math.Ceiling((z1 - z0) / step);
            var g = new float[nx * nz];
            System.Threading.Tasks.Parallel.For(0, nz, iz =>
            {
                double gz = z0 + iz * step + step / 2;
                for (int ix = 0; ix < nx; ix++) g[iz * nx + ix] = (float)w.HeightAt(x0 + ix * step + step / 2, gz);
            });
            byZone[key] = g;
            return g;
        }
    }

    /// <summary>
    /// UNE EAU OÙ LA HOULE N'ENTRE PAS : de ce point, sur seize directions, combien
    /// filent sur <paramref name="fetch"/> mètres d'eau sans toucher terre ? Un rivage
    /// ouvert sur le large en a sept ou huit (le demi-cercle de la mer) ; le fond d'une
    /// rade, l'envers d'un cordon, une ou deux (le goulet). Calme : trois au plus. On
    /// part de cent mètres au large, pour ne pas compter le rivage même.
    /// </summary>
    public static bool Calm(World w, double x, double z, double fetch)
    {
        int open = 0;
        for (int k = 0; k < 16; k++)
        {
            double a = k * Math.PI / 8, dx = Math.Sin(a), dz = Math.Cos(a);
            bool land = false;
            for (double d = 100; d <= fetch; d += 100)
                if (w.HeightAt(x + dx * d, z + dz * d) > 0) { land = true; break; }
            if (!land && ++open > 3) return false;
        }
        return true;
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
