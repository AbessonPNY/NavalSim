using Godot;
using System;
using System.Collections.Generic;
using NavalSim.Core;

namespace NavalSim;

/// <summary>
/// LE BOIS QUI TRAVAILLE (demandé) — sons.json → bois.craking, à bord et près du bord
/// des navires de trois cents tonneaux et plus (la Boussole, la Roter Löwe, le galion
/// pirate, le Speedwell, la frégate) : un sloop de vingt tonnes est trop raide pour
/// qu'on l'entende.
///
/// UNE COQUE DE CHÊNE CRAQUE QUAND ELLE TRAVAILLE : la houle la tord, le roulis et le
/// tangage font jouer ses membrures, ses barrots, son vaigrage. Le craquement suit donc
/// sa VITESSE DE ROTATION (roulis, et tangage qui la tord davantage) — rare au mouillage
/// par calme, une fois toutes les vingt secondes ; fréquent et plus franc quand elle
/// roule dans une mer formée. Tiré au hasard (un processus de Poisson), jamais deux à la
/// fois, et d'un point de la coque à chaque fois. Sous le pont, dans une chambre fermée,
/// il est plus fort : là, c'est le bruit principal.
/// </summary>
public partial class ShipDemo
{
    /// <summary>Le seuil, en tonneaux de déplacement : en dessous, la coque se tait.</summary>
    const double CreakTonnes = 300;
    /// <summary>Le gain de sons.json (bois.craking_gain) : 1 réglé à l'oreille, 0 muet.</summary>
    double _creakGain = 1;
    readonly Dictionary<ShipNode, double> _creakNext = new();
    readonly RandomNumberGenerator _creakRng = new();

    void CreakTick()
    {
        if (_sound == null || _sound.CrewCount("craquement") == 0 || _inTitle || _creakGain <= 0) return;
        var cam = _cam.GlobalPosition;
        CreakShip(_ship, cam, true);
        foreach (var s in _others) CreakShip(s, cam, false);
        if (_creakNext.Count > 32)
            foreach (var k in new List<ShipNode>(_creakNext.Keys)) if (!IsInstanceValid(k)) _creakNext.Remove(k);
    }

    void CreakShip(ShipNode s, Vector3 cam, bool mine)
    {
        var sp = s.Spec;
        if (sp.Tonnes < CreakTonnes || s.IsGhost) return;
        var ph = s.Physics;
        if (ph.Foundered || ph.Broken) return;
        var b = ph.Body;
        // à bord ou près du bord : la demi-longueur, et vingt-cinq mètres d'eau
        double dx = cam.X - b.Pos.X, dy = cam.Y - b.Pos.Y, dz = cam.Z - b.Pos.Z;
        double reach = sp.L * 0.5 + 25;
        if (dx * dx + dy * dy + dz * dz > reach * reach) return;

        // ce qui la fait travailler : son roulis et son tangage, dans son propre repère
        var w = b.Quat.Inverted().Rotate(b.AngVel);
        double work = Math.Abs(w.Z) + 1.5 * Math.Abs(w.X);
        double rate = 0.05 + 2.0 * work;                      // craquements par seconde
        if (!_creakNext.TryGetValue(s, out double next))
        {
            _creakNext[s] = _t + 1 + 4 * _creakRng.Randf();
            return;
        }
        if (_t < next) return;
        _creakNext[s] = _t + Math.Max(1.5, -Math.Log(Math.Max(1e-4, _creakRng.Randf())) / rate);

        // d'un point de la coque : une membrure, un barrot, quelque part entre les deux bouts
        var local = new Vector3(
            (float)((_creakRng.Randf() - 0.5) * sp.B * 0.8),
            (float)(sp.DeckMid - 0.6 - _creakRng.Randf() * 1.5),
            (float)((_creakRng.Randf() - 0.5) * sp.L * 0.7));
        var at = s.GlobalTransform * local;
        bool inside = mine && _indoors > 0.5;
        double gain = Math.Clamp(0.12 + 1.4 * work, 0.12, 0.45) * (inside ? 1.6 : 1) * _creakGain;
        _sound.Crew("craquement", at.ToCore(), gain, 1.2, inside);
    }
}
