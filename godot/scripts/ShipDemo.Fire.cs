using Godot;
using System;
using System.Collections.Generic;
using NavalSim.Core;

namespace NavalSim;

/// <summary>
/// L'INCENDIE À BORD — un feu par coque, et rien de global.
///
/// Le noyau (<see cref="Fire"/>) tient les foyers et décide de tout ; ce fichier
/// ne fait que trois choses : le brancher sur ce qui allume (le boulet, la
/// foudre), montrer ce qui brûle, et faire sauter la soute quand il l'annonce.
///
/// LA LUTTE EST UN BUDGET. L'équipage donne tant de seaux par seconde, répartis
/// entre les foyers : un départ est noyé, trois se combattent, six débordent le
/// monde. Personne n'a écrit « à six foyers on perd » — c'est la division qui le
/// dit, et c'est pourquoi une bordée bien placée est autre chose qu'une avarie.
/// </summary>
public partial class ShipDemo : Node3D
{
    FireSettings _fireRules = new();
    readonly Dictionary<ShipNode, Fire> _fires = new();

    /// <summary>Son feu, créé au premier départ — une coque qui n'a jamais brûlé n'en a pas.</summary>
    Fire FireOf(ShipNode s)
    {
        if (_fires.TryGetValue(s, out var f)) return f;
        f = new Fire(_fireRules, _stormRng.NextDouble);
        f.Event = kind =>
        {
            if (s != _ship) return;                       // ce qui brûle ailleurs se voit, ne se dit pas
            switch (kind)
            {
                case "depart": Say("Le feu ! Aux seaux !"); JournalLog("Un départ de feu à bord."); break;
                case "encore": Say("Un autre foyer !"); break;
                case "gagne": Say("Le feu gagne — il faut le noyer !"); break;
                /* SAUF SI ELLE VIENT DE SAUTER. Le feu s'éteint bel et bien quand
                   la mer entre — mais « le feu est maîtrisé » après « LA SOUTE ! »
                   se lit comme une plaisanterie. */
                case "maitrise":
                    if (s.Physics.Foundered) break;
                    Say("Le feu est maîtrisé."); JournalLog("Le feu maîtrisé."); break;
                case "soute": Say("LA SOUTE !"); break;
            }
        };
        _fires[s] = f;
        return f;
    }

    /// <summary>
    /// UN DÉPART DE FEU, au point du bord où l'on a touché (repère LOCAL). C'est
    /// la seule porte : le boulet, la foudre et le brûlot y passent tous, si bien
    /// qu'aucun d'eux n'a sa propre idée de ce qu'est un incendie.
    /// </summary>
    void LightFire(ShipNode s, Vec3d local, double force)
    {
        if (!_fireRules.Enabled || s.Physics.Foundered) return;
        FireOf(s).Light(local, force);
    }

    /// <summary>
    /// Une image, pour toute la flotte. L'indice du navire dans la flotte EST le
    /// numéro de sa lampe : une seule par coque, et elle ne bouge jamais de place
    /// dans le tableau.
    /// </summary>
    void FireTick(double dt)
    {
        if (_fires.Count == 0) return;
        double pluie = _fall.Amount;
        _fireDone.Clear();

        /* L'INDICE DANS LA FLOTTE EST LE NUMÉRO DE SA LAMPE, et l'entrée 0 est le
           navire commandé — la même convention que partout ailleurs. */
        for (int i = 0; i <= _others.Count; i++)
        {
            var s = i == 0 ? _ship : _others[i - 1];
            if (!_fires.TryGetValue(s, out var f)) continue;
            _fireDone.Add(s);

            /* CE QUE L'ÉQUIPAGE PEUT ENCORE DONNER. Une coque qui coule n'a plus
               personne aux seaux — mais l'eau qui entre fait leur travail, ce que
               le noyau sait déjà. */
            double bras = s.Physics.Foundered ? 0 : 1;
            f.Step(dt, pluie, bras, s.Physics.SubmergedFrac > 0.62);

            if (!f.Burning) { _gunFx.BurnOut(i); continue; }

            /* CHAQUE FOYER FUME À SA PLACE, portée dans le monde par la coque :
               elle roule, et le feu roule avec elle. La lumière, elle, n'est posée
               que sur le PIRE — une lampe par coque, et c'est assez : deux brasiers
               à dix mètres ne font pas deux ombres qu'on distingue. */
            var b = s.Physics.Body;
            Fire.Seat? pire = null;
            foreach (var seat in f.Seats)
            {
                var w = b.Quat.Rotate(seat.P) + b.Pos;
                _gunFx.Burn(new Vector3((float)w.X, (float)w.Y, (float)w.Z), seat.Heat, dt, s.Spec.L / 30);
                if (pire == null || seat.Heat > pire.Heat) pire = seat;
            }
            if (pire != null)
            {
                var w = b.Quat.Rotate(pire.P) + b.Pos;
                _gunFx.BurnLight(i, new Vector3((float)w.X, (float)w.Y + 1.2f, (float)w.Z), f.Worst, s.Spec.L / 30);
            }

            /* LA TOILE PART LA PREMIÈRE. Un feu établi sous un mât mange sa voilure
               avant de descendre à la soute : c'est ce qui rend l'incendie
               différent d'une voie d'eau — on perd sa vitesse d'abord, donc sa
               chance de fuir, et ensuite seulement le navire. */
            if (f.Worst > 0.55 && _stormRng.NextDouble() < 0.10 * dt) s.SplitSail(false);

            if (f.Doomed) BlowUp(s);
        }

        /* ET LES COQUES QUI ONT QUITTÉ LA FLOTTE emportent leur feu : sans cela
           leurs foyers brûleraient à l'endroit où elles n'étaient plus, et leur
           lampe resterait allumée sur la mer vide. */
        if (_fireDone.Count != _fires.Count)
            foreach (var s in new List<ShipNode>(_fires.Keys))
                if (!_fireDone.Contains(s)) _fires.Remove(s);
    }
    readonly HashSet<ShipNode> _fireDone = new();

    /// <summary>Tout éteindre sur cette coque — au radoub, ou quand elle disparaît.</summary>
    void Douse(ShipNode s)
    {
        if (_fires.TryGetValue(s, out var f)) f.Clear();
        int i = s == _ship ? 0 : _others.IndexOf(s) + 1;
        if (i > 0 || s == _ship) _gunFx.BurnOut(i);
    }
}
