using System;
using System.Collections.Generic;

namespace NavalSim.Core;

/// <summary>
/// LE PILOTE D'UNE RADE : il mène un navire le long d'une route (HarbourRoute),
/// marque après marque, par la barre automatique — qui tient le cap, tire des
/// bords au près et vire lof pour lof. Lui ne fait que deux choses de plus :
///
///  - il donne la marque suivante quand on arrive sur la précédente ;
///  - au près, il SONDE devant l'étrave : si le fond monte à moins de ce que la
///    quille demande, il change de bord avant d'y être, comme un pilote qui
///    connaît sa rade — la barre automatique, faite pour le large, tirerait ses
///    bords de trois cents mètres jusque sur la plage.
///
/// Arrivé, il ferle et laisse la barre au milieu : le navire est rendu.
/// </summary>
public sealed class HarbourPilot
{
    readonly ShipPhysics _ph;
    readonly World _w;
    readonly double _need;
    readonly List<(double X, double Z)> _route;
    int _i = 1;
    double _sinceTack, _irons, _wearUntil;
    readonly bool _foreAft;

    public readonly AutoHelm Helm;
    public bool Arrived { get; private set; }
    /// <summary>La marque visée (index dans la route).</summary>
    public int Leg => _i;
    public IReadOnlyList<(double X, double Z)> Route => _route;

    public HarbourPilot(ShipPhysics ph, World w, List<(double X, double Z)> route, double need)
    {
        _ph = ph; _w = w; _route = route; _need = need;
        // des bords courts : une rade n'a pas la place de ceux du large
        Helm = new AutoHelm(ph, standoff: 0) { MinLeg = 60 };
        // un gréement aurique vire vent devant ; un carré abat, il n'a pas le choix
        _foreAft = ph.Spec.Rig.Type is "gaff" or "lateen" or "sloop";
    }

    /// <summary>Partir d'une marque plus loin : un navire déjà en route quand la scène s'ouvre.</summary>
    public void SkipTo(int leg) => _i = Math.Clamp(leg, 1, _route.Count - 1);

    public void Update(double dt, Ocean ocean, Controls c)
    {
        var b = _ph.Body;
        double x = b.Pos.X + ocean.Origin.X, z = b.Pos.Z + ocean.Origin.Z;
        if (Arrived || _ph.Foundered)
        {
            c.SailsSet = false; c.Rudder = 0; c.Throttle = 0;
            return;
        }
        /* LA MARQUE PASSÉE : à moins d'une longueur et demie, ou quand on l'a
           dépassée dans le sens de la route (au près, on la double rarement au
           ras). La dernière se prend plus large : on y est rendu. */
        double reach = Math.Max(45, 1.5 * _ph.Spec.L);
        while (_i < _route.Count)
        {
            var (mx, mz) = _route[_i];
            double d = Math.Sqrt((mx - x) * (mx - x) + (mz - z) * (mz - z));
            bool last = _i == _route.Count - 1;
            if (d < (last ? 2 * reach : reach)) { if (last) { Arrived = true; Update(dt, ocean, c); return; } _i++; continue; }
            if (!last)
            {
                // dépassée : on est au-delà de la perpendiculaire en elle, vers la suivante
                var (nx, nz) = _route[_i + 1];
                double ux = nx - mx, uz = nz - mz;
                if ((x - mx) * ux + (z - mz) * uz > 0 && d < 6 * reach) { _i++; continue; }
            }
            break;
        }
        /* VENT DEVANT, ET LOF POUR LOF SI ELLE MANQUE. Une rade n'a pas la place de
           faire le tour par le vent arrière : sur une côte sous le vent, abattre,
           c'est s'échouer (relevé au banc : le sloop au départ de Port-Royal par
           force 6 de nord-nord-est). Mais sans assez d'élan elle reste prise vent
           debout : passé quarante secondes, elle abat pour cette fois. */
        _wearUntil -= dt;
        Helm.Tacks = _foreAft && Config.CrewTacks && _wearUntil <= 0;
        if (Helm.Tacks && _ph.TackPhase == 1) { _irons += dt; if (_irons > 40) { _wearUntil = 120; _irons = 0; } }
        else _irons = 0;
        var (tx, tz) = _route[Math.Min(_i, _route.Count - 1)];
        Helm.Target = new Vec3d(tx - ocean.Origin.X, 0, tz - ocean.Origin.Z);
        Helm.Update(dt, ocean, c);

        /* LA SONDE, au près seulement : au portant la route est droite et la route
           est en eau ; c'est en tirant des bords qu'on sort du couloir. Trois coups
           de sonde devant l'étrave, jusqu'à trois longueurs. */
        _sinceTack += dt;
        if (Helm.Beating && !Helm.Wearing && _sinceTack > 25)
        {
            var f = b.Quat.Rotate(new Vec3d(0, 0, 1));
            double look = Math.Max(60, 3 * _ph.Spec.L);
            for (int k = 1; k <= 3; k++)
            {
                double px = x + f.X * look * k / 3, pz = z + f.Z * look * k / 3;
                if (-_w.HeightAt(px, pz) < _need)
                {
                    Helm.BeatSide = -Helm.BeatSide;
                    _sinceTack = 0;
                    break;
                }
            }
        }
    }
}
