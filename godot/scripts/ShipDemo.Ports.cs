using Godot;
using System;
using System.Collections.Generic;
using NavalSim.Core;

namespace NavalSim;

/// <summary>
/// LE BRANLE-BAS : qui veut ses sabords ouverts (ShipNode.Ports.cs fait le reste).
///
/// Un navire ouvre ses sabords quand il a un ennemi à moins de 1 500 m, ou qu'il a
/// tiré depuis moins de deux minutes ; le nôtre aussi quand on sert une pièce, ou
/// sur l'ordre (⇧ et la touche du plan d'arrimage — le W d'un AZERTY), qui les
/// tient ouverts jusqu'à l'ordre contraire. Hors de cela, on les ferme : par grosse
/// mer, un sabord ouvert embarque.
/// </summary>
public partial class ShipDemo
{
    /// <summary>
    /// L'ordre du capitaine : ouverts, fermés, ou rien (le combat décide). ⇧W le pose
    /// d'après ce qu'on VOIT — ouverts, il les ferme ; fermés, il les ouvre (signalé :
    /// ouverts seuls au combat, la touche croyait les ouvrir et ne faisait rien).
    /// </summary>
    bool? _portsOrder;
    /// <summary>
    /// UN FEU COMMANDÉ SABORDS FERMÉS ATTEND QU'ILS S'OUVRENT : on ne tire pas à
    /// travers un mantelet (signalé : le coup partait avant l'ouverture). L'ordre est
    /// retenu — l'autre bord ou non, la bordée ou une pièce — et donné dès que les
    /// pièces de ce bord sont en batterie.
    /// </summary>
    (bool Other, bool Held)? _fireWhenOpen;
    /// <summary>Le dernier coup de chaque navire, en temps de jeu.</summary>
    readonly Dictionary<ShipPhysics, double> _firedAt = new();

    const double ActionRange = 1500, ActionAfterShot = 120;

    void NoteFired(ShipPhysics from) => _firedAt[from] = _t;

    bool FiredLately(ShipPhysics p) => _firedAt.TryGetValue(p, out double at) && _t - at < ActionAfterShot;

    /// <summary>Un autre navire est-il en action contre celui-ci, ou celui-ci contre un autre, à portée ?</summary>
    bool InAction(ShipNode s)
    {
        if (FiredLately(s.Physics)) return true;
        var me = s.Physics.Body.Pos;
        if (EnemyOf(s) is { } foe && !foe.Foundered && (foe.Body.Pos - me).LengthXZ < ActionRange) return true;
        // le nôtre n'a pas d'« ennemi » à lui : il est en action si quelqu'un l'a pour ennemi
        if (s == _ship)
            foreach (var o in _others)
                if (!o.Physics.Foundered && EnemyOf(o) == _ship.Physics && (o.Physics.Body.Pos - me).LengthXZ < ActionRange)
                    return true;
        return false;
    }

    void PortsTick(double frame)
    {
        if (_ship.HasPortLids)
        {
            _ship.PortsWanted = _fireWhenOpen != null || (_portsOrder ?? (_gunPost != null || InAction(_ship)));
            _ship.PortsTick(frame);
            if (_fireWhenOpen is { } f && _ship.PortsReady(f.Other ? -_gunSide : _gunSide))
            {
                _fireWhenOpen = null;
                Fire(f.Other, f.Held);
            }
        }
        foreach (var s in _others)
        {
            if (!s.HasPortLids) continue;
            // les mantelets s'ouvrent à la fin du branle-bas, pas au premier coup reçu
            s.PortsWanted = !s.IsGhost && !s.Physics.Foundered && InAction(s) && ClearingLids(s);
            s.PortsTick(frame);
        }
    }

    void TogglePorts()
    {
        if (!_ship.HasPortLids) { Say("Ce navire n'a pas de mantelets à ses sabords"); return; }
        // d'après ce qu'on voit, et non d'après le dernier ordre : le combat a pu les ouvrir seul
        bool open = !_ship.PortsWanted;
        _portsOrder = open;
        if (!open) _fireWhenOpen = null;
        Say(open ? "Branle-bas de combat ! Ouvrez les sabords, en batterie !"
                 : "Rentrez les pièces, fermez les sabords");
    }
}
