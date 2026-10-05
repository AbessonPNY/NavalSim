using Godot;
using System;
using NavalSim.Core;

namespace NavalSim;

/// <summary>
/// LE VRAI CIEL DE NUIT (demandé : « on peut se repérer aux étoiles pour de vrai ? ») —
/// les étoiles du catalogue (world/etoiles.json) à l'année de la partie, peintes une fois
/// (StarMap), et la sphère céleste tournée chaque image selon le lieu VRAI du navire et
/// l'heure : on retrouve la Polaire par les deux étoiles du bout de la Grande Ourse, on la
/// voit tourner autour du pôle avec ses Gardes, et le ciel d'octobre n'est pas celui d'avril.
/// </summary>
public partial class ShipDemo
{
    StarCatalog? _stars;
    bool _starsTried;

    void StarsTick()
    {
        if (_world == null) return;
        if (!_starsTried)
        {
            _starsTried = true;
            string path = Assets.Path("world/etoiles.json");
            if (!System.IO.File.Exists(path)) { GD.PushWarning("world/etoiles.json absent : le ciel garde son semis"); return; }
            var clock = System.Diagnostics.Stopwatch.StartNew();
            _stars = StarCatalog.FromJson(System.IO.File.ReadAllText(path), _calendar.Date.Year + 0.5);
            _sky.SetStarMap(StarMap.Build(_stars));
            GD.Print($"ciel : {_stars.Stars.Count} étoiles de {_calendar.Date.Year}, peintes en {clock.ElapsedMilliseconds} ms");
        }
        var (lat, lon) = TrueLatLon();
        /* The drawn sun used a fixed latitude (13.6°) and a fixed late-spring declination (12°):
           in October it stood 18° too high above Port-Royal, at odds with the stars around it and
           with the quadrant's reading. Same place and same date for both, applied at the next
           SetTimeOfDay. */
        _sky.Latitude = lat;
        _sky.Core.Declination = _calendar.Declination();
        if (_stars == null) return;
        double phi = lat * Math.PI / 180;
        // le nord est +z, l'est −x, le haut +y
        var N = new Vector3(0, 0, 1); var U = Vector3.Up; var Wst = new Vector3(1, 0, 0);
        var pole = N * (float)Math.Cos(phi) + U * (float)Math.Sin(phi);
        var meridian = -N * (float)Math.Sin(phi) + U * (float)Math.Cos(phi);
        // le temps sidéral local : l'angle horaire du point vernal (ascension droite nulle)
        double lst = Sights.HourAngle(_calendar.Date, _sky.Core.DayTime, lon, 0) * Math.PI / 180;
        _sky.SetCelestial(pole, meridian, Wst, (float)lst);
    }
}
