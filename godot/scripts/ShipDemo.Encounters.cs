using Godot;
using System;
using System.Collections.Generic;
using NavalSim.Core;

namespace NavalSim;

/// <summary>
/// LES VOILES QU'ON CROISE — ce que la page avait et que le portage n'avait pas
/// encore : la mer se peuple.
///
/// De temps en temps, loin et hors de vue de toute côte, une voile paraît. Un
/// marchand qui fait route vers un port, ou — une fois sur quatre, réglable — un
/// pirate qui vient sur vous. Semée au-delà de l'horizon utile, elle est rendue à
/// la mer : on ne traîne pas une flotte derrière soi.
///
/// ET PARFOIS DEUX, AUX PRISES. C'est l'ajout : une part des rencontres n'est pas
/// une voile mais une AFFAIRE DÉJÀ COMMENCÉE, un pirate qui canonne un navire
/// d'une nation. On arrive dessus et l'on choisit — passer au large, secourir, ou
/// attendre que l'un ait fini l'autre pour prendre ce qui flotte. Rien n'a été
/// inventé pour cela : c'est l'hostilité d'une PAIRE, celle qui existe depuis
/// qu'un boulet reçu fait un ennemi, posée d'avance dans les deux sens.
///
/// Les chiffres sont dans settings.json → encounters, ceux que la page lit déjà.
/// </summary>
public partial class ShipDemo
{
    EncounterSettings _metRules = new();

    /// <summary>Les voiles de rencontre : elles se retirent seules, les autres non.</summary>
    readonly HashSet<ShipNode> _met = new();
    /// <summary>Celles que la vigie a déjà annoncées.</summary>
    readonly HashSet<ShipNode> _noticed = new();
    /// <summary>Un marchand va quelque part : son but, en mètres VRAIS du monde.</summary>
    readonly Dictionary<ShipNode, Vec3d> _bound = new();
    /// <summary>L'heure de jeu de la prochaine voile ; négative tant qu'on n'a pas commencé à compter.</summary>
    double _nextSail = -1;
    readonly Random _metRng = new();

    // ------------------------------------------------------------------
    //  LA PENDULE
    // ------------------------------------------------------------------

    void EncounterTick(double dt)
    {
        if (!_metRules.Enabled || _ship == null || _world == null || _inTitle) return;
        // une escarmouche est déjà pleine de monde, et les spectres ont leur nuit
        if (_skirmish || _ghosts.Active) return;

        var me = _ship.Physics.Body.Pos;
        var o = _sea.Core.Origin;

        // ce qu'on a semé s'en va ; ce qui approche se dit
        foreach (var s in new List<ShipNode>(_met))
        {
            if (!IsInstanceValid(s) || !_others.Contains(s)) { _met.Remove(s); _noticed.Remove(s); _bound.Remove(s); continue; }
            double d = Math.Sqrt((s.Physics.Body.Pos.X - me.X) * (s.Physics.Body.Pos.X - me.X)
                              + (s.Physics.Body.Pos.Z - me.Z) * (s.Physics.Body.Pos.Z - me.Z));
            if (d > _metRules.Despawn || (s.Physics.Foundered && d > 3000))
            {
                _met.Remove(s); _noticed.Remove(s); _bound.Remove(s);
                RemoveShip(s);
                continue;
            }
            if (!_noticed.Contains(s) && !s.Physics.Foundered
                && (d < _metRules.NoticeDistance || InGlass(s, d))) Notice(s, d);
        }

        if (_nextSail < 0) _nextSail = _t + Wait();
        if (_t < _nextSail) return;
        _nextSail = _t + Wait();

        /* RIEN NE PARAÎT PRÈS DE LA TERRE : au port, au mouillage ou dans une
           passe. On croise des voiles AU LARGE, et la pendule attend sans se
           vider — la première vient peu après qu'on a gagné la mer. */
        if (_portHere != null) return;
        if (_world.ShoreDistance(o.X + me.X, o.Z + me.Z) < _metRules.LandDistance) return;
        if (_met.Count >= _metRules.MaxAtOnce) return;
        if (_fleet.Count >= Config.MaxShips) return;

        // deux aux prises demandent deux places : on ne commence pas ce qu'on ne peut pas finir
        bool pair = _metRng.NextDouble() < _metRules.BattleChance
                 && _met.Count + 2 <= _metRules.MaxAtOnce + 1
                 && _fleet.Count + 2 <= Config.MaxShips;
        if (pair) Affair(); else Sail();
    }

    double Wait() => _metRules.IntervalMin + _metRng.NextDouble() * (_metRules.IntervalMax - _metRules.IntervalMin);

    // ------------------------------------------------------------------
    //  OÙ LA POSER
    // ------------------------------------------------------------------

    /// <summary>De l'eau d'un bout à l'autre du trait, avec sa marge. Mètres VRAIS.</summary>
    bool ClearWater(double ax, double az, double bx, double bz, double slack)
    {
        if (_world == null) return true;
        double len = Math.Sqrt((bx - ax) * (bx - ax) + (bz - az) * (bz - az));
        int n = Math.Max(2, (int)(len / 500));
        for (int i = 0; i <= n; i++)
        {
            double u = (double)i / n;
            // la fin d'une approche exceptée : un port est par définition au bord de la terre
            double need = _metRules.ClearWater - slack * u;
            if (need <= 0) continue;
            if (_world.ShoreDistance(ax + (bx - ax) * u, az + (bz - az) * u) < need) return false;
        }
        return true;
    }

    /// <summary>Un point du large où poser une voile, et le but qu'elle suivra. Nul si on n'en trouve pas.</summary>
    (Vec3d At, Vec3d To)? Offing(bool toward)
    {
        if (_world == null) return null;
        var b = _ship.Physics.Body.Pos;
        var o = _sea.Core.Origin;
        var ports = _world.Isles.FindAll(i => i.Port.Hx != 0 || i.Port.Hz != 0);
        for (int k = 0; k < 60; k++)
        {
            double a = _metRng.NextDouble() * Math.PI * 2;
            double d = _metRules.SpawnMin + _metRng.NextDouble() * (_metRules.SpawnMax - _metRules.SpawnMin);
            double lx = b.X + Math.Sin(a) * d, lz = b.Z + Math.Cos(a) * d;
            double wx = lx + o.X, wz = lz + o.Z;
            if (_world.ShoreDistance(wx, wz) < _metRules.SpawnFromLand) continue;

            // un pirate vient sur VOUS ; un marchand va vers un port dont la route est claire
            if (toward)
            {
                if (ClearWater(wx, wz, b.X + o.X, b.Z + o.Z, 0))
                    return (new Vec3d(lx, 0, lz), new Vec3d(b.X + o.X, 0, b.Z + o.Z));
                continue;
            }
            var open = ports.FindAll(p => ClearWater(wx, wz, p.Port.Hx, p.Port.Hz, _metRules.ClearWater));
            if (open.Count > 0)
            {
                var p = open[_metRng.Next(open.Count)];
                return (new Vec3d(lx, 0, lz), new Vec3d(p.Port.Hx, 0, p.Port.Hz));
            }
            // aucun port ralliable : un point du large, droit devant
            double c = _metRng.NextDouble() * Math.PI * 2;
            double fx = wx + Math.Sin(c) * 15000, fz = wz + Math.Cos(c) * 15000;
            if (ClearWater(wx, wz, fx, fz, 0)) return (new Vec3d(lx, 0, lz), new Vec3d(fx, 0, fz));
        }
        return null;
    }

    /// <summary>Les fiches qu'on peut croiser, pirates d'un côté, pavillon national de l'autre.</summary>
    (List<int> Pirates, List<int> Honest) MetPool()
    {
        var pirates = new List<int>();
        var honest = new List<int>();
        for (int i = 0; i < _paths.Count; i++)
        {
            var spec = ShipLibrary.Load(_paths[i]);
            if (spec == null) continue;
            if (Array.IndexOf(_metRules.Exclude, spec.Id) >= 0) continue;
            if (spec.Appearance?.Ensign == "jolly") pirates.Add(i); else honest.Add(i);
        }
        return (pirates, honest);
    }

    /// <summary>Poser une coque à un point local, l'étrave sur un but donné en mètres vrais.</summary>
    ShipNode? Put(int specIndex, Vec3d at, Vec3d toWorld, bool arm)
    {
        int before = _others.Count;
        SpawnFleet(1, specIndex, arm);
        if (_others.Count == before) return null;
        var s = _others[^1];
        var b = s.Physics.Body;
        var o = _sea.Core.Origin;
        double now = _stepT0 + _stepSub * _stepDt;
        b.Pos = new Vec3d(at.X, b.Pos.Y + _sea.Core.Sample(at.X, at.Z, now), at.Z);
        b.AngVel = Vec3d.Zero;
        double dx = (toWorld.X - o.X) - at.X, dz = (toWorld.Z - o.Z) - at.Z;
        b.Quat = Quatd.FromAxisAngle(new Vec3d(0, 1, 0), -Math.Atan2(-dx, dz));
        b.Vel = b.Quat.Rotate(new Vec3d(0, 0, 2.5));
        s.Ctrl.SailsSet = true;
        s.Ctrl.Sheet = 0.6;
        s.SyncTransform();
        _met.Add(s);
        return s;
    }

    // ------------------------------------------------------------------
    //  UNE VOILE
    // ------------------------------------------------------------------

    void Sail()
    {
        var (pirates, honest) = MetPool();
        bool black = pirates.Count > 0 && _metRng.NextDouble() < _metRules.PirateChance;
        var pool = black ? pirates : honest;
        if (pool.Count == 0) return;
        if (Offing(black) is not { } spot) return;

        /* UN PIRATE EST ARMÉ, et c'est tout ce qu'il faut : le reste — choisir sa
           proie, la chasser, cesser le feu pour venir à couple — est son métier et
           il le sait déjà. Un marchand, lui, ne sait que sa route. */
        var s = Put(pool[_metRng.Next(pool.Count)], spot.At, spot.To, black);
        if (s == null) return;
        if (black) HelmOf(s).Standoff = Math.Max(HelmOf(s).Standoff, 120);
        else _bound[s] = spot.To;
        Colours(s);
        GD.Print(FormattableString.Invariant(
            $"rencontre : {s.Spec.Name}{(black ? " (pavillon noir)" : "")} a {(spot.At - _ship.Physics.Body.Pos).Length:F0} m"));
    }

    // ------------------------------------------------------------------
    //  DEUX AUX PRISES
    // ------------------------------------------------------------------

    /// <summary>
    /// UN PIRATE QUI EN CANONNE UN AUTRE, trouvé au milieu de l'affaire. Les deux
    /// se tiennent pour ennemis d'emblée, dans les DEUX SENS — sans quoi le
    /// marchand se laisserait battre sans riposter —, et à la distance où l'on se
    /// bat pour de bon. Le pirate n'est pas « armé » au sens du chasseur : il a
    /// déjà sa proie, et lui en laisser choisir une autre le ferait quitter sa
    /// prise pour courir après vous.
    /// </summary>
    void Affair()
    {
        var (pirates, honest) = MetPool();
        if (pirates.Count == 0 || honest.Count == 0) return;
        if (Offing(false) is not { } spot) return;

        var prey = Put(honest[_metRng.Next(honest.Count)], spot.At, spot.To, false);
        if (prey == null) return;
        Colours(prey);

        // le pirate par son travers, à la distance du combat
        double side = _metRng.Next(2) == 0 ? 1 : -1;
        var pb = prey.Physics.Body;
        var beam = pb.Quat.Rotate(new Vec3d(side, 0, 0));
        var at = new Vec3d(pb.Pos.X + beam.X * _metRules.BattleGap, 0, pb.Pos.Z + beam.Z * _metRules.BattleGap);
        var o = _sea.Core.Origin;
        var raider = Put(pirates[_metRng.Next(pirates.Count)], at,
                         new Vec3d(o.X + pb.Pos.X, 0, o.Z + pb.Pos.Z), false);
        if (raider == null) { _bound[prey] = spot.To; return; }

        _hostile[raider] = (prey, 0);
        _hostile[prey] = (raider, 0);
        HelmOf(raider).Standoff = 120;
        HelmOf(prey).Standoff = 120;

        string nat = prey.Ensign?.Nationalite ?? "marchand";
        Say($"Le canon au loin — un pirate s'acharne sur un navire {nat}");
        GD.Print(FormattableString.Invariant(
            $"rencontre : {raider.Spec.Name} aux prises avec {prey.Spec.Name} ({nat}) a {(spot.At - _ship.Physics.Body.Pos).Length:F0} m"));
    }

    // ------------------------------------------------------------------
    //  LA VIGIE
    // ------------------------------------------------------------------

    /// <summary>Vue à la lunette : loin devant, dans le champ de l'objectif.</summary>
    bool InGlass(ShipNode s, double d)
    {
        if (!_glassUp || d > _metRules.SpyglassRange) return false;
        var to = s.Physics.Body.Pos - _ship.Physics.Body.Pos;
        var eye = -_cam.GlobalTransform.Basis.Z;
        var f = new Vec3d(eye.X, 0, eye.Z);
        double fl = f.Length, tl = Math.Sqrt(to.X * to.X + to.Z * to.Z);
        if (fl < 1e-6 || tl < 1e-6) return false;
        return (to.X * f.X + to.Z * f.Z) / (fl * tl) > _metRules.SpyglassField;
    }

    void Notice(ShipNode s, double d)
    {
        _noticed.Add(s);
        // la vigie, et elle est dans la hune : le cri vient de l'avant et d'en haut
        Shout("voile-en-vue", 0.3, 8);
        string quoi = _hostile.ContainsKey(s) || _pirates.ContainsKey(s)
            ? "Une voile !"
            : s.Ensign?.Nationalite is string n ? $"Une voile — un navire {n}" : "Une voile !";
        Say($"{quoi} à {d / 1852:F1} mille{(d / 1852 >= 2 ? "s" : "")}");
    }
}
