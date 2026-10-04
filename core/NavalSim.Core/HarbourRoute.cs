using System;
using System.Collections.Generic;

namespace NavalSim.Core;

/// <summary>
/// UNE ROUTE QUI CONTOURNE LA TERRE — pour les navires qui vont et viennent dans une
/// rade (Godot : ShipDemo.Traffic.cs).
///
/// La barre automatique va droit à sa marque ; au large c'est juste, dans une rade
/// elle s'échouerait sur la première pointe. On cherche donc un chemin sur une
/// grille de quelques dizaines de mètres, qui ne passe que là où la quille a son
/// eau (<paramref name="need"/>, en mètres de fond) et qui se tient à distance de
/// la côte — un pilote ne rase pas les hauts-fonds sans raison. Puis on le tend :
/// de chaque marque, on va droit à la plus lointaine qu'on voit sans toucher, ce qui
/// rend quelques marques, comme un pilote les donnerait.
///
/// Tout en mètres VRAIS du monde : la route ne se périme pas quand l'origine glisse.
/// </summary>
public static class HarbourRoute
{
    sealed class Grid
    {
        public double X0, Z0, Cell;
        public int N, M;
        public bool[] Wet = null!;
        /// <summary>La distance à la terre (ou au haut-fond), en mailles, plafonnée.</summary>
        public byte[] Clear = null!;

        public int Index(double x, double z)
        {
            int i = (int)Math.Floor((x - X0) / Cell), j = (int)Math.Floor((z - Z0) / Cell);
            return i < 0 || j < 0 || i >= N || j >= M ? -1 : j * N + i;
        }
        public (double X, double Z) Centre(int k) => (X0 + (k % N + 0.5) * Cell, Z0 + (k / N + 0.5) * Cell);
    }

    const int ClearCap = 6;

    static Grid Build(World w, double xMin, double zMin, double xMax, double zMax, double need, double cell)
    {
        var g = new Grid { X0 = xMin, Z0 = zMin, Cell = cell };
        g.N = Math.Max(2, (int)Math.Ceiling((xMax - xMin) / cell));
        g.M = Math.Max(2, (int)Math.Ceiling((zMax - zMin) / cell));
        int n = g.N * g.M;
        g.Wet = new bool[n];
        g.Clear = new byte[n];
        var q = new Queue<int>();
        for (int k = 0; k < n; k++)
        {
            var (x, z) = g.Centre(k);
            g.Wet[k] = -w.HeightAt(x, z) >= need;
            if (!g.Wet[k]) q.Enqueue(k);
            else g.Clear[k] = ClearCap;
        }
        // la distance à la terre, par vagues depuis tout ce qui ne porte pas
        while (q.Count > 0)
        {
            int k = q.Dequeue();
            int i = k % g.N, j = k / g.N;
            byte d = (byte)(g.Wet[k] ? g.Clear[k] : 0);
            for (int dj = -1; dj <= 1; dj++)
                for (int di = -1; di <= 1; di++)
                {
                    int ii = i + di, jj = j + dj;
                    if ((di == 0 && dj == 0) || ii < 0 || jj < 0 || ii >= g.N || jj >= g.M) continue;
                    int kk = jj * g.N + ii;
                    if (!g.Wet[kk] || g.Clear[kk] <= d + 1) continue;
                    g.Clear[kk] = (byte)(d + 1);
                    if (d + 1 < ClearCap) q.Enqueue(kk);
                }
        }
        return g;
    }

    /// <summary>Le surcoût d'une maille selon sa distance à la terre : on évite de raser.</summary>
    static double Penalty(byte clear) => clear switch { <= 1 => 8, 2 => 3, 3 => 1, _ => 0 };

    /// <summary>La maille d'eau la plus proche d'un point (un ponton est souvent dans trop peu d'eau).</summary>
    static int Snap(Grid g, double x, double z)
    {
        int k0 = g.Index(x, z);
        if (k0 >= 0 && g.Wet[k0] && g.Clear[k0] >= 2) return k0;
        int best = -1; double bd = double.MaxValue;
        for (int k = 0; k < g.Wet.Length; k++)
        {
            if (!g.Wet[k] || g.Clear[k] < 2) continue;
            var (cx, cz) = g.Centre(k);
            double d = (cx - x) * (cx - x) + (cz - z) * (cz - z);
            if (d < bd) { bd = d; best = k; }
        }
        return best;
    }

    /// <summary>
    /// D'UN POINT À UN AUTRE, par l'eau. Nulle si rien ne les relie. Le premier et
    /// le dernier point sont ceux qu'on a donnés ; entre les deux, les marques.
    /// </summary>
    public static List<(double X, double Z)>? Plan(World w, double x0, double z0, double x1, double z1,
                                                   double need, double cell = 30, double margin = 1500)
    {
        var g = Build(w, Math.Min(x0, x1) - margin, Math.Min(z0, z1) - margin,
                         Math.Max(x0, x1) + margin, Math.Max(z0, z1) + margin, need, cell);
        int s = Snap(g, x0, z0), e = Snap(g, x1, z1);
        if (s < 0 || e < 0) return null;
        var (ex, ez) = g.Centre(e);
        var path = Search(g, s, k => k == e, k => { var (cx, cz) = g.Centre(k); return Math.Sqrt((cx - ex) * (cx - ex) + (cz - ez) * (cz - ez)) / g.Cell; });
        if (path == null) return null;
        return Tighten(g, path, (x0, z0), (x1, z1));
    }

    /// <summary>
    /// D'UN PORT VERS LE LARGE : le point d'eau profonde (au moins
    /// <paramref name="seaDepth"/> mètres) le plus proche PAR L'EAU parmi ceux qui
    /// sont à plus de <paramref name="minRadius"/> du départ — la sortie naturelle de
    /// la rade, quelle que soit la forme de la côte.
    /// </summary>
    public static List<(double X, double Z)>? ToSea(World w, double x0, double z0, double need,
                                                    double minRadius = 3500, double seaDepth = 30, double cell = 40)
    {
        double r = minRadius + 1500;
        var g = Build(w, x0 - r, z0 - r, x0 + r, z0 + r, need, cell);
        int s = Snap(g, x0, z0);
        if (s < 0) return null;
        bool Goal(int k)
        {
            var (cx, cz) = g.Centre(k);
            double d2 = (cx - x0) * (cx - x0) + (cz - z0) * (cz - z0);
            return d2 >= minRadius * minRadius && -w.HeightAt(cx, cz) >= seaDepth && g.Clear[k] >= ClearCap;
        }
        var path = Search(g, s, Goal, _ => 0);
        if (path == null) return null;
        var end = g.Centre(path[^1]);
        return Tighten(g, path, (x0, z0), end);
    }

    /// <summary>Dijkstra (A* si l'heuristique n'est pas nulle) sur les huit voisins, au coût de la distance et des abords.</summary>
    static List<int>? Search(Grid g, int start, Func<int, bool> goal, Func<int, double> h)
    {
        int n = g.Wet.Length;
        var cost = new double[n];
        Array.Fill(cost, double.MaxValue);
        var from = new int[n];
        Array.Fill(from, -1);
        var open = new PriorityQueue<int, double>();
        cost[start] = 0;
        open.Enqueue(start, h(start));
        int found = -1;
        while (open.TryDequeue(out int k, out double pri))
        {
            if (pri - h(k) > cost[k] + 1e-9) continue;          // une entrée périmée
            if (goal(k)) { found = k; break; }
            int i = k % g.N, j = k / g.N;
            for (int dj = -1; dj <= 1; dj++)
                for (int di = -1; di <= 1; di++)
                {
                    int ii = i + di, jj = j + dj;
                    if ((di == 0 && dj == 0) || ii < 0 || jj < 0 || ii >= g.N || jj >= g.M) continue;
                    int kk = jj * g.N + ii;
                    if (!g.Wet[kk]) continue;
                    double step = (di != 0 && dj != 0 ? 1.41421356 : 1) * (1 + Penalty(g.Clear[kk]));
                    double c = cost[k] + step;
                    if (c >= cost[kk]) continue;
                    cost[kk] = c;
                    from[kk] = k;
                    open.Enqueue(kk, c + h(kk));
                }
        }
        if (found < 0) return null;
        var path = new List<int>();
        for (int k = found; k >= 0; k = from[k]) path.Add(k);
        path.Reverse();
        return path;
    }

    /// <summary>
    /// TENDRE LE CHEMIN : de chaque marque, droit sur la plus lointaine qu'on voit
    /// sans passer à moins de deux mailles de la terre — les mailles du chemin
    /// deviennent quelques bords droits.
    /// </summary>
    static List<(double X, double Z)> Tighten(Grid g, List<int> path, (double X, double Z) a, (double X, double Z) b)
    {
        bool Sight((double X, double Z) p, (double X, double Z) q)
        {
            double dx = q.X - p.X, dz = q.Z - p.Z, len = Math.Sqrt(dx * dx + dz * dz);
            int steps = Math.Max(1, (int)(len / (g.Cell * 0.5)));
            for (int s = 1; s < steps; s++)
            {
                int k = g.Index(p.X + dx * s / steps, p.Z + dz * s / steps);
                if (k < 0 || !g.Wet[k] || g.Clear[k] < 2) return false;
            }
            return true;
        }
        var pts = new List<(double X, double Z)>(path.Count);
        foreach (int k in path) pts.Add(g.Centre(k));
        var outp = new List<(double X, double Z)> { a };
        var cur = pts[0];
        outp.Add(cur);
        int at = 0;
        while (at < pts.Count - 1)
        {
            int far = at + 1;
            for (int t = pts.Count - 1; t > at + 1; t--)
                if (Sight(cur, pts[t])) { far = t; break; }
            at = far;
            cur = pts[at];
            outp.Add(cur);
        }
        outp.Add(b);
        // les doublons des bouts (un départ déjà dans une maille d'eau)
        for (int i = outp.Count - 1; i > 0; i--)
            if (Math.Abs(outp[i].X - outp[i - 1].X) + Math.Abs(outp[i].Z - outp[i - 1].Z) < 1) outp.RemoveAt(i);
        return outp;
    }
}
