using System;
using System.Collections.Generic;
using System.Text.Json;

namespace NavalSim.Core;

/// <summary>Une étoile à l'année de la partie : où elle est sur la sphère céleste, son éclat, sa couleur, son nom.</summary>
public readonly record struct Star(double Ra, double Dec, double Mag, double BV, string Name);

/// <summary>
/// LE VRAI CIEL — world/etoiles.json (tools/stars.js, le Yale Bright Star Catalogue),
/// porté à l'année de la partie : chaque étoile avance de son mouvement propre depuis
/// 2000, puis la précession des équinoxes la ramène au ciel de ce temps-là (Sights.Precess).
/// En 1690, la Polaire est à près de deux degrés et demi du pôle, et Arcturus à dix minutes
/// d'arc de sa place d'aujourd'hui. Ce qu'en fait le jeu : le dessiner (Godot, StarMap.cs),
/// et le faire tourner selon l'heure, la date et le lieu (Sights.HourAngle).
/// </summary>
public sealed class StarCatalog
{
    public readonly List<Star> Stars = new();
    public double Year { get; private set; }

    public static StarCatalog FromJson(string json, double year)
    {
        var cat = new StarCatalog { Year = year };
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("etoiles", out var arr)) return cat;
        double dt = year - 2000;
        foreach (var e in arr.EnumerateArray())
        {
            double ra = e[0].GetDouble(), dec = e[1].GetDouble();
            double pma = e[2].GetDouble(), pmd = e[3].GetDouble();
            // le mouvement propre d'abord : en ascension droite il est donné × cos δ
            double cd = Math.Cos(dec * Math.PI / 180);
            if (Math.Abs(cd) > 1e-6) ra += pma * dt / 3600 / cd;
            dec = Math.Clamp(dec + pmd * dt / 3600, -90, 90);
            var (r, d) = Sights.Precess(ra, dec, year);
            cat.Stars.Add(new Star(r, d, e[4].GetDouble(), e[5].GetDouble(), e[6].GetString() ?? ""));
        }
        return cat;
    }

    /// <summary>L'étoile de ce nom (« la Polaire », « Sirius », « α Cru »…), ou null.</summary>
    public Star? Find(string name)
    {
        foreach (var s in Stars) if (s.Name == name) return s;
        return null;
    }
}
