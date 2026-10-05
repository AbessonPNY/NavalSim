using System;

namespace NavalSim.Core;

/// <summary>Où une coque perce la surface : une ellipse, son centre, ses demi-axes, son orientation.</summary>
public readonly record struct Waterline(double X, double Z, double A, double B, double Angle, bool Pierces);

public sealed partial class ShipPhysics
{
    /// <summary>
    /// LA LIGNE D'EAU, telle qu'elle est cette image — et non telle que le plan de
    /// formes la dessine à l'équilibre. Les cellules qui TRAVERSENT la surface la
    /// tapissent : leurs moments donnent un centre et des axes, ceux d'une ellipse
    /// pleine (σ² = a²/4). Une coque à flot la trouve allongée comme elle ; un tronçon
    /// dressé, ronde comme sa section ; une épave qui s'enfonce, de plus en plus
    /// petite, jusqu'à rien. Relevée dans la boucle des sondes, elle ne coûte rien.
    /// C'est là que se pose le collier d'écume d'une coque qui coule (le champ
    /// d'écume, Godot) : celle qui avance a déjà le sien, dans le shader de la mer.
    /// </summary>
    public Waterline Water { get; private set; }

    void WaterlineFrom(double n, double sx, double sz, double sxx, double szz, double sxz)
    {
        if (n <= 1e-9) { Water = default; return; }
        double mx = sx / n, mz = sz / n;
        double cxx = sxx / n - mx * mx, czz = szz / n - mz * mz, cxz = sxz / n - mx * mz;
        // les axes principaux de la tache : les valeurs propres de sa covariance
        double tr = cxx + czz, det = cxx * czz - cxz * cxz;
        double disc = Math.Sqrt(Math.Max(0, tr * tr / 4 - det));
        double l1 = Math.Max(0, tr / 2 + disc), l2 = Math.Max(0, tr / 2 - disc);
        double ang = 0.5 * Math.Atan2(2 * cxz, cxx - czz);
        // la moitié d'une cellule de plus : les sondes sont AU MILIEU de leur cellule
        double cell = 0.5 * ProbeH;
        Water = new Waterline(mx, mz, 2 * Math.Sqrt(l1) + cell, 2 * Math.Sqrt(l2) + cell, ang, true);
    }
}
