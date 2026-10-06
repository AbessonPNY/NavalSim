using Godot;
using System;
using System.Collections.Generic;
using NavalSim.Core;

namespace NavalSim;

/// <summary>
/// CE QUE LA MAIN A BÂTI, dit au monde (World.Occupied) pour que la végétation n'y
/// pousse pas : les maisons et pâtés déplacés en mode création, les pâtés, l'église,
/// les forts et les maisons qu'on a ajoutés. Un cercle par objet, à la mesure de ce
/// qu'il est ; les petits objets (un tonneau, un coffre, une chaloupe) n'écartent rien.
/// Rangés dans une grille de cases : un semis interroge des centaines de milliers de
/// points au chargement.
/// </summary>
public partial class ShipDemo
{
    const double OccCell = 32;

    void OccupiedSetup()
    {
        if (_world == null || _editReg == null) return;
        var cells = new Dictionary<(int, int), List<(double X, double Z, double R)>>();
        int n = 0;
        foreach (var e in _editReg.Edits.All)
        {
            if (e.Removed) continue;
            double r = FootprintOf(e);
            if (r <= 0) continue;
            int i0 = (int)Math.Floor((e.X - r) / OccCell), i1 = (int)Math.Floor((e.X + r) / OccCell);
            int j0 = (int)Math.Floor((e.Z - r) / OccCell), j1 = (int)Math.Floor((e.Z + r) / OccCell);
            for (int j = j0; j <= j1; j++)
                for (int i = i0; i <= i1; i++)
                {
                    if (!cells.TryGetValue((i, j), out var l)) cells[(i, j)] = l = new List<(double, double, double)>();
                    l.Add((e.X, e.Z, r));
                }
            n++;
        }
        if (n == 0) return;
        _world.Occupied = (x, z, margin) =>
        {
            // la marge peut déborder d'une case : on regarde les voisines
            int ci = (int)Math.Floor(x / OccCell), cj = (int)Math.Floor(z / OccCell);
            int reach = 1 + (int)(margin / OccCell);
            for (int j = cj - reach; j <= cj + reach; j++)
                for (int i = ci - reach; i <= ci + reach; i++)
                {
                    if (!cells.TryGetValue((i, j), out var l)) continue;
                    foreach (var (ox, oz, r) in l)
                    {
                        double rr = r + margin;
                        if ((x - ox) * (x - ox) + (z - oz) * (z - oz) < rr * rr) return true;
                    }
                }
            return false;
        };
        GD.Print($"[végétation] {n} bâti(s) posé(s) à la main tenus à l'écart");
    }

    /// <summary>Le rayon de ce qu'une retouche pose, en mètres ; zéro pour ce qui n'écarte rien.</summary>
    static double FootprintOf(Edit e)
    {
        string what = (e.Glb.Length > 0 ? e.Glb : e.From.Length > 0 ? e.From : e.Id).ToLowerInvariant();
        double k = Math.Max(0.3, e.Scale);
        if (what.StartsWith("pate:") || what.Contains("bloc")) return 18 * Math.Min(k, 2);
        if (what.Contains("eglise") || what.Contains("fort")) return 16 * Math.Min(k, 2);
        if (what.StartsWith("maison:") || what.Contains("cottage") || what.Contains("maison")) return 8 * Math.Min(k, 2);
        return 0;
    }
}
