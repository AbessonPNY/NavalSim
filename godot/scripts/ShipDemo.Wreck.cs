using Godot;
using System;
using System.Collections.Generic;
using NavalSim.Core;

namespace NavalSim;

/// <summary>
/// LE NAUFRAGE : l'air qui remonte, et ce qui reste à flot. Le noyau tient les
/// deux mécaniques (WreckAir) ; ici on leur prête la mer, l'embrun, le champ
/// d'écume et les mots.
/// </summary>
public partial class ShipDemo
{
    readonly WreckAir _wreckAir = new();
    readonly List<WreckHull> _wrecks = new();
    FlotsamNode _flotsam = null!;

    void WireWreck()
    {
        // la gerbe basse que chaque poche soulève en crevant, et le bouillon qui reste
        _wreckAir.OnBurst = (at, water, speed, jet) => _spray.Pool.Burst(at, water, speed, jet);
        _flotsam.BottleOneIn = _bottleOneIn;
        // ce qu'elle contient regarde la page ; ici elle se nomme, en attendant la carte
        _flotsam.OnBottle = from => Say(from != null
            ? $"Une bouteille repêchée — elle vient du {from}"
            : "Une bouteille repêchée");
        _wreckAir.OnLastBreath = (at, v) =>
        {
            var b = _ship.Physics.Body;
            if ((at - b.Pos).Length < 3000)
                Say(FormattableString.Invariant($"Elle disparaît dans un grand bouillon — {v:F0} m³ d'air d'un coup."));
        };
    }

    /// <summary>Une image de naufrage : l'air de chaque coque, les bouillons dans l'écume, les débris.</summary>
    void WreckTick(double dt)
    {
        _wrecks.Clear();
        _wrecks.Add(new WreckHull(_ship.Physics, _ship.HullShell()));
        foreach (var s in _others) _wrecks.Add(new WreckHull(s.Physics, s.HullShell()));
        _wreckAir.Update(dt, _wrecks, _sea.Core, _t);
        _foam.SetBoils(_wreckAir.Boils);
        _flotsam.Step(dt, _sea.Core, _allShipsForFlotsam(), _ship, _cam);
    }

    readonly List<ShipNode> _flotsamFleet = new();
    IReadOnlyList<ShipNode> _allShipsForFlotsam()
    {
        _flotsamFleet.Clear();
        _flotsamFleet.Add(_ship);
        _flotsamFleet.AddRange(_others);
        return _flotsamFleet;
    }
}
