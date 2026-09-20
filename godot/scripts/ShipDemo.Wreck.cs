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
    BubbleNode _bubbles = null!;
    CoinNode _coins = null!;

    void WireWreck()
    {
        // la gerbe basse que chaque poche soulève en crevant, et le bouillon qui reste
        _wreckAir.OnBurst = (at, water, speed, jet) => _spray.Pool.Burst(at, water, speed, jet);
        // ce qu'on voit du trajet : la poche elle-même, qui monte en chapelet
        _wreckAir.OnSlug = (at, v, rise, travel) =>
            _bubbles.Slug(new Vector3((float)at.X, (float)at.Y, (float)at.Z), v, rise, travel);
        _flotsam.BottleOneIn = _bottleOneIn;
        // ce qui crève la surface en remontant jette son peu d'eau, par la même réserve
        _flotsam.OnBreak = (at, water, speed) => _spray.Pool.Burst(at, water, speed);
        /* CE QU'ELLE EMPORTE. Une cale qui s'ouvre lâche sa bourse : les pièces
           descendent en voltigeant et l'eau les avale. Le compte suit sa taille —
           un galion emporte plus qu'une chaloupe. */
        _flotsam.OnWreck = s =>
        {
            var b = s.Physics.Body;
            _coins.Spill(new Vector3((float)b.Pos.X, (float)b.Pos.Y, (float)b.Pos.Z),
                s.Spec.L, s.Spec.B, (int)Math.Clamp(s.Spec.L * 10, 120, 500));
        };
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
        _bubbles.Step(dt, _sea.Core, _t);
        // l'or ne s'allume pas tout seul : il lui faut la course du soleil et sa couleur
        _sky.PushTo(_coins.Material);
        _coins.Step(dt);
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
