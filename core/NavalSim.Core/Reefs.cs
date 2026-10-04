using System;
using System.Collections.Generic;

namespace NavalSim.Core;

/// <summary>
/// UN RÉCIF, tel que la fiche l'écrit (world/*.json → « recifs », world/README.md) :
/// un plateau de corail dont la crête affleure à <see cref="Top"/> mètres sous la
/// surface, en ellipse de <see cref="Length"/> sur <see cref="Width"/> mètres (du
/// monde réduit) couchée au cap <see cref="Cap"/>, et qui tombe sur le fond qui
/// l'entoure par un tombant de <see cref="Edge"/> mètres. <see cref="Cay"/> : une
/// caye de sable émerge en son milieu — Lime Cay, Maiden Cay.
/// </summary>
public sealed class ReefSpec
{
    public string Name = "";
    public double Lat, Lon, Length = 200, Width = 100, Cap, Top = 0.5, Edge = 40;
    public bool Cay;
}

/// <summary>Un récif posé, en mètres VRAIS : son centre, son grand axe, ses demi-axes.</summary>
public sealed class Reef
{
    public ReefSpec Spec = null!;
    public double X, Z, Ux, Uz, HalfL, HalfW, Reach;
    /// <summary>La phase de son ourlet, tirée de son nom : deux récifs n'ont pas le même contour.</summary>
    public double Phase;

    public Reef(ReefSpec s, Geo geo)
    {
        Spec = s;
        (X, Z) = geo.ToXZ(s.Lat, s.Lon);
        // le grand axe, au cap boussole (0 nord, 90 est ; est = −x)
        double a = s.Cap * Math.PI / 180;
        Ux = -Math.Sin(a); Uz = Math.Cos(a);
        HalfL = Math.Max(1, s.Length / 2); HalfW = Math.Max(1, s.Width / 2);
        Reach = (Math.Max(HalfL, HalfW) * 1.25) + s.Edge * 2;
        uint h = 2166136261;
        foreach (char c in s.Name) h = (h ^ c) * 16777619;
        Phase = (h % 6283) / 1000.0;
    }

    /* L'OURLET : un récif n'est pas une ellipse. Son bord ondule de trois lobes
       larges et de quelques festons, à la phase de son nom. ÉCRIT DEUX FOIS, ici et
       dans seabed.gdshaderinc (reef_at) — en sinus, pour que les deux tombent sur
       le même contour au centimètre : le corail peint et le haut-fond doivent
       coïncider. */
    public static double Wobble(double theta, double phase) =>
        1 + 0.12 * Math.Sin(3 * theta + phase) + 0.07 * Math.Sin(5 * theta + 1.7 * phase) + 0.04 * Math.Sin(9 * theta + 2.3 * phase);

    /// <summary>
    /// Où l'on est par rapport à lui : <c>e</c>, le rayon de l'ellipse ramené à un
    /// (1 au bord du plateau), et <c>t</c>, de 1 sur le plateau à 0 au pied du
    /// tombant.
    /// </summary>
    public bool Locate(double x, double z, out double e, out double t)
    {
        e = 9; t = 0;
        double dx = x - X, dz = z - Z;
        if (Math.Abs(dx) > Reach || Math.Abs(dz) > Reach) return false;
        double l = dx * Ux + dz * Uz, w = -dx * Uz + dz * Ux;
        e = Math.Sqrt((l / HalfL) * (l / HalfL) + (w / HalfW) * (w / HalfW)) / Wobble(Math.Atan2(w / HalfW, l / HalfL), Phase);
        double past = Math.Max(0, e - 1) * Math.Min(HalfL, HalfW);
        t = Math.Clamp(1 - past / Math.Max(1, Spec.Edge), 0, 1);
        return t > 0;
    }
}

/// <summary>
/// LES RÉCIFS DANS LE RELIEF — appliqués par <see cref="World.HeightAt"/>, donc
/// lus par tout ce qui sonde : la coque qui s'échoue, le pilote de rade qui les
/// contourne, le relief dessiné, la couleur de l'eau. Ils ne font que RELEVER le
/// fond, jamais le creuser.
/// </summary>
public static class Reefs
{
    // un bruit de valeur en double, ancré au monde : les éperons et les sillons du plateau
    static double Hash(int i, int j)
    {
        uint h = (uint)(i * 374761393 + j * 668265263);
        h = (h ^ (h >> 13)) * 1274126177;
        return ((h ^ (h >> 16)) & 0xFFFF) / 65535.0;
    }

    static double Noise(double x, double y)
    {
        int i = (int)Math.Floor(x), j = (int)Math.Floor(y);
        double fx = x - i, fy = y - j;
        fx = fx * fx * (3 - 2 * fx); fy = fy * fy * (3 - 2 * fy);
        double a = Hash(i, j), b = Hash(i + 1, j), c = Hash(i, j + 1), d = Hash(i + 1, j + 1);
        return a + (b - a) * fx + (c - a) * fy + (a - b - c + d) * fx * fy;
    }

    /// <summary>
    /// Le fond relevé par les récifs en ce point. Sur le plateau, la crête à
    /// <c>Top</c> sous la surface, rompue d'éperons et de têtes de corail (± 0,3 m) mais
    /// jamais à moins de 15 cm — le corail ne pousse pas plus haut que la basse mer ;
    /// sur le tombant, une pente qui rejoint le fond d'alentour ; au milieu d'une
    /// caye, du sable sec.
    /// </summary>
    public static double Apply(IReadOnlyList<Reef> reefs, double x, double z, double h)
    {
        for (int i = 0; i < reefs.Count; i++)
        {
            var r = reefs[i];
            if (!r.Locate(x, z, out double e, out double t)) continue;
            double rough = (Noise(x * 0.09, z * 0.09) - 0.5) * 0.4 + (Noise(x * 0.4, z * 0.4) - 0.5) * 0.25;
            // le corail ne pousse pas plus haut que la basse mer : la crête reste mouillée
            double crest = Math.Min(-0.15, -r.Spec.Top + rough);
            // le tombant : la crête qui plonge de vingt-cinq mètres au pied, adoucie
            double s = t * t * (3 - 2 * t);
            double reef = crest - (1 - s) * 25;
            if (r.Spec.Cay && e < 0.4) reef = Math.Max(reef, 1.4 * (1 - e / 0.4) - 0.2);
            if (reef > h) h = reef;
        }
        return h;
    }

    /// <summary>Sur un récif en ce point, de 0 à 1 (1 sur le plateau) : le fond y est DUR.</summary>
    public static double At(IReadOnlyList<Reef> reefs, double x, double z)
    {
        double best = 0;
        for (int i = 0; i < reefs.Count; i++)
            if (reefs[i].Locate(x, z, out _, out double t)) best = Math.Max(best, t);
        return best;
    }
}

/// <summary>
/// LES ROCHERS QUI COMPTENT — les semis marqués « ecueil » (world/README.md),
/// inscrits par le moteur qui les pose (Godot : LandNode) à leur place VUE, après
/// les retouches : un rocher déplacé en mode création emporte son danger avec lui.
/// Chacun est une demi-ellipsoïde : un rayon au sol, un pied, un sommet.
/// En mètres VRAIS, rangés par cases pour qu'une coque ne demande que ses voisins.
/// </summary>
public sealed class RockField
{
    public sealed class Rock
    {
        public double X, Z, R, Base, Top;
        internal (int, int) Cell;
    }

    const double CellSize = 16;
    readonly Dictionary<(int, int), List<Rock>> _cells = new();
    double _maxR;
    public int Count { get; private set; }

    static (int, int) Key(double x, double z) => ((int)Math.Floor(x / CellSize), (int)Math.Floor(z / CellSize));

    public Rock Add(double x, double z, double r, double baseY, double top)
    {
        var rk = new Rock { X = x, Z = z, R = r, Base = baseY, Top = top, Cell = Key(x, z) };
        if (!_cells.TryGetValue(rk.Cell, out var l)) _cells[rk.Cell] = l = new List<Rock>();
        l.Add(rk);
        _maxR = Math.Max(_maxR, r);
        Count++;
        return rk;
    }

    public void Remove(Rock rk)
    {
        if (_cells.TryGetValue(rk.Cell, out var l) && l.Remove(rk)) Count--;
    }

    /// <summary>Déplacé (le mode création) : à sa nouvelle place, son pied sur le fond d'ici.</summary>
    public void Move(Rock rk, double x, double z, double baseY, double top)
    {
        Remove(rk);
        rk.X = x; rk.Z = z; rk.Base = baseY; rk.Top = top; rk.Cell = Key(x, z);
        _maxR = Math.Max(_maxR, rk.R);
        if (!_cells.TryGetValue(rk.Cell, out var l)) _cells[rk.Cell] = l = new List<Rock>();
        l.Add(rk);
        Count++;
    }

    /// <summary>Les rochers qui peuvent toucher un disque de ce rayon autour de ce point.</summary>
    public void Near(double x, double z, double radius, List<Rock> into)
    {
        into.Clear();
        double reach = radius + _maxR;
        var (i0, j0) = Key(x - reach, z - reach);
        var (i1, j1) = Key(x + reach, z + reach);
        for (int j = j0; j <= j1; j++)
            for (int i = i0; i <= i1; i++)
            {
                if (!_cells.TryGetValue((i, j), out var l)) continue;
                for (int k = 0; k < l.Count; k++)
                {
                    var rk = l[k];
                    double dx = rk.X - x, dz = rk.Z - z, d = radius + rk.R;
                    if (dx * dx + dz * dz < d * d) into.Add(rk);
                }
            }
    }

    /// <summary>Le sommet du rocher au-dessus de ce point (une demi-ellipsoïde), ou −∞ s'il n'y est pas.</summary>
    public static double TopAt(Rock rk, double x, double z)
    {
        double dx = x - rk.X, dz = z - rk.Z, q = (dx * dx + dz * dz) / (rk.R * rk.R);
        return q >= 1 ? double.NegativeInfinity : rk.Base + (rk.Top - rk.Base) * Math.Sqrt(1 - q);
    }
}
