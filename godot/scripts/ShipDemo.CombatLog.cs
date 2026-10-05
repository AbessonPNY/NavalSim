using Godot;
using System;
using System.Collections.Generic;
using NavalSim.Core;

namespace NavalSim;

/// <summary>
/// MESURE : le journal d'un combat, sans image (-- --combat-journal 20). Toutes les N
/// secondes, pour chaque navire : les coups reçus (coque, toile, mâts), ses voies d'eau
/// et ce qu'elles ont d'ouvert sous la flottaison, l'eau embarquée, ses mâts tombés,
/// et l'heure de son naufrage. C'est ce qui dit lequel arrive le premier, du mât qui
/// tombe ou de la cale qui s'emplit.
/// </summary>
public partial class ShipDemo
{
    double _combatLogEvery = -1, _combatLogAt;
    readonly Dictionary<ShipNode, (int Hull, int Sail, int Mast)> _hitsTaken = new();
    readonly HashSet<ShipNode> _sunkLogged = new();

    void CountHit(ShipNode s, string kind)
    {
        if (_combatLogEvery <= 0) return;
        _hitsTaken.TryGetValue(s, out var h);
        if (kind == "sail") h.Sail++; else if (kind == "mast") h.Mast++; else h.Hull++;
        _hitsTaken[s] = h;
    }

    void CombatLogTick()
    {
        if (_combatLogEvery <= 0) return;
        var all = new List<ShipNode> { _ship };
        foreach (var (node, _) in _targets) if (node != _ship && !all.Contains(node)) all.Add(node);
        foreach (var s in all)
            if (s.Physics.Foundered && _sunkLogged.Add(s))
                GD.Print(FormattableString.Invariant($"[combat] {_t:F0} s : {s.Spec.Name} COULE — {Line(s)}"));
        if (_t < _combatLogAt) return;
        _combatLogAt = _t + _combatLogEvery;
        GD.Print(FormattableString.Invariant($"[combat] --- {_t:F0} s"));
        foreach (var s in all)
            if (!s.Physics.Foundered) GD.Print("[combat]   " + s.Spec.Name + " : " + Line(s));
    }

    string Line(ShipNode s)
    {
        var p = s.Physics;
        _hitsTaken.TryGetValue(s, out var h);
        double under = 0;
        foreach (var br in p.Breaches) if (br.Head > 0) under += br.Area;
        return FormattableString.Invariant(
            $"coque {h.Hull}, toile {h.Sail}, mâts {h.Mast} · {p.Breaches.Count} brèches dont {under:F3} m² sous l'eau · eau {p.FloodTonnes:F0} t · mâts tombés {s.MastsDown} · toile entière {s.Whole():F2}");
    }
}
