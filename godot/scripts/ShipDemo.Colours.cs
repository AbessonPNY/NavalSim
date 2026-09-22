using Godot;
using System;
using System.Collections.Generic;
using NavalSim.Core;

namespace NavalSim;

/// <summary>
/// LE PAVILLON, AMENÉ OU HISSÉ (⇧P) — et ce n'est pas qu'une étoffe.
///
/// Un navire qui ne montre pas ses couleurs ne se laisse pas identifier : c'est
/// la ruse de guerre la plus vieille du métier, et elle coûte ce qu'elle
/// rapporte.
///
/// - LES PIRATES ne vous prennent plus pour proie tant que vos couleurs sont
///   basses : ils ne savent pas s'ils abordent un marchand ou une frégate, et
///   un pirate prudent va voir ailleurs. Celui qui vous chasse DÉJÀ ne s'en
///   laisse pas conter : amener le pavillon ne le fait pas lâcher.
/// - LES HONNÊTES GENS s'écartent : un inconnu sans pavillon qui s'approche est
///   une menace, et ils font porter au large plutôt que de l'attendre.
/// </summary>
public partial class ShipDemo
{
    /// <summary>Les couleurs sont-elles hissées ?</summary>
    bool _colours = true;
    /// <summary>Jusqu'où une coque se méfie d'un navire sans pavillon, en mètres.</summary>
    const double WaryRange = 1200;
    readonly HashSet<ShipNode> _waried = new();

    void ToggleColours()
    {
        _colours = !_colours;
        _ship.ShowColours(_colours);
        if (_colours) _waried.Clear();
        Say(_colours ? "Pavillon hissé" : "Pavillon amené — on ne saura pas qui vous êtes");
    }

    /// <summary>
    /// La conduite d'une coque paisible devant un navire sans pavillon : elle
    /// fait porter au large. Vrai tant qu'elle ne vous a pas déjà pris en
    /// chasse, et seulement à vue.
    /// </summary>
    bool Wary(ShipNode s, double dt)
    {
        if (_colours || s.IsGhost || _pirates.ContainsKey(s) || _hostile.ContainsKey(s)) return false;
        var me = _ship.Physics.Body.Pos;
        var her = s.Physics.Body.Pos;
        var away = new Vec3d(her.X - me.X, 0, her.Z - me.Z);
        double d = away.Length;
        if (d > WaryRange || d < 1e-3) return false;
        var h = HelmOf(s);
        h.Standoff = 0;
        h.Target = her + away * (2500 / d);          // droit à l'opposé de vous
        h.Update(dt, _sea.Core, s.Ctrl);
        if (_waried.Add(s)) Say(s.Spec.Name + " fait porter au large : vous ne montrez pas vos couleurs");
        return true;
    }

    /// <summary>Ce qu'un pirate voit de vous : rien, tant que le pavillon est bas.</summary>
    void Unknown(Pirate p)
    {
        // celui qui vous chasse déjà garde sa proie ; les autres vont voir ailleurs
        if (!_colours && p.Cible != _ship.Physics) p.Ignore[_ship.Physics] = _t + 20;
    }
}
