using Godot;
using System;
using System.Collections.Generic;
using NavalSim.Core;

namespace NavalSim;

/// <summary>
/// Ce que la tempête demande au navire : ses têtes de mât (la foudre y tombe, le
/// kraken y enroule ses bras) et la hauteur de son pont le long de lui.
/// </summary>
public partial class ShipNode
{
    List<MastTop>? _tops;
    readonly List<MastTop> _topsLive = new();
    Func<double, double>? _deckNear;

    /// <summary>La hauteur de son pont à la station z, dans son repère — celle des feux, lue une fois.</summary>
    public double DeckNear(double z) => (_deckNear ??= DeckStations().DeckNear)(z);

    /// <summary>
    /// LES TÊTES DE MÂT, LUES SUR LES ESPARS TELS QU'ILS SONT DESSINÉS — _sparScan
    /// et mastTops de ship-model.js.
    ///
    /// Le repérage du gréement répond à « quels mâts portent des vergues », qui
    /// n'est pas la même question : sur le Roter Löwe il trouvait le grand mât, un
    /// mât de misaine sans espar à lui et la vergue de civadière — et pas
    /// d'artimon du tout, le sien ne portant aucune vergue carrée. Donc : toute
    /// pièce FINE debout près de l'axe et assez haute pour être un mât est rangée
    /// par stations le long d'elle, et une suite de stations qui monte au-dessus
    /// du tiers de sa longueur est UN mât, coiffé à son sommet le plus haut — un
    /// mât quêté s'étale sur plusieurs stations et reste un. Les espars que le
    /// gréement a déjà pris ne sont plus dans le modèle ; ils reviennent par leurs
    /// pieds. Mesuré une fois par modèle.
    /// </summary>
    public IReadOnlyList<MastTop> MastTops()
    {
        // mesurées une fois ; « tombé » se relit à chaque appel, sur sa chute
        var baseTops = _tops ??= ScanTops();
        _topsLive.Clear();
        foreach (var m in baseTops)
            _topsLive.Add(m with { Gone = m.Fall >= 0 && m.Fall < _damage.Count && _damage[m.Fall].Down != null });
        return _topsLive;
    }

    List<MastTop> ScanTops()
    {
        var tops = new List<MastTop>();
        var spec = Spec;
        if (ModelRoot == null)
        {
            foreach (var m in spec.Masts) tops.Add(new MastTop(spec.DeckMid + m.Height, m.Z, -1, false));
            return tops;
        }

        double bin = 0.06 * spec.L, minTop = 0.35 * spec.L;
        var cols = new SortedDictionary<int, (double Y, double Z)>();
        foreach (var p in ModelParts())
        {
            if (p.Size.X > 0.08 * spec.B || Math.Abs(p.Mid.X) > 0.08 * spec.B) continue;
            bool tall = p.Size.Y > 0.3 * spec.L;
            if (!tall) continue;
            foreach (var v in p.Verts)
            {
                if (v.Y < minTop) continue;
                // Math.round de la page : le demi va vers le haut
                int k = (int)Math.Floor(v.Z / bin + 0.5);
                if (!cols.TryGetValue(k, out var c) || v.Y > c.Y) cols[k] = (v.Y, v.Z);
            }
        }
        // des stations voisines sont un seul mât
        var runs = new List<(int K, double Y, double Z)>();
        foreach (var (k, c) in cols)
        {
            if (runs.Count > 0 && k - runs[^1].K <= 1)
            {
                var last = runs[^1];
                runs[^1] = c.Y > last.Y ? (k, c.Y, c.Z) : (k, last.Y, last.Z);
            }
            else runs.Add((k, c.Y, c.Z));
        }
        var found = new List<(double Y, double Z)>();
        foreach (var r in runs) found.Add((r.Y, r.Z));
        // les espars que le gréement a sortis du modèle, rendus par leurs pieds
        foreach (var m in _masts)
        {
            if (m.Parent == _rig) continue;
            double z = m.Parent.Position.Z, y = m.Parent.Position.Y + m.Height;
            int dup = found.FindIndex(t => Math.Abs(t.Z - z) < 0.06 * spec.L);
            if (dup >= 0) { if (y > found[dup].Y) found[dup] = (y, z); }
            else found.Add((y, z));
        }
        // et le mât qui porte chaque tête : celui dont le pied est le plus près le long d'elle
        foreach (var (y, z) in found)
        {
            int fall = -1;
            double near = 0.06 * spec.L;
            for (int i = 0; i < _masts.Count; i++)
            {
                double d = Math.Abs(_masts[i].Parent.Position.Z - z);
                if (_masts[i].Parent != _rig && d < near) { near = d; fall = i; }
            }
            tops.Add(new MastTop(y, z, fall, false));
        }
        return tops;
    }

    /// <summary>La plus haute tête de mât debout, dans le monde local — là où tombe la foudre.</summary>
    public bool HighestMasthead(out Vector3 world, out int fall)
    {
        world = default; fall = -1;
        MastTop? best = null;
        foreach (var m in MastTops()) if (!m.Gone && (best == null || m.Y > best.Value.Y)) best = m;
        if (best == null) return false;
        fall = best.Value.Fall;
        var b = Physics.Body;
        Vec3d w = b.Quat.Rotate(new Vec3d(0, best.Value.Y, best.Value.Z)) + b.Pos;
        world = new Vector3((float)w.X, (float)w.Y, (float)w.Z);
        return true;
    }
}
