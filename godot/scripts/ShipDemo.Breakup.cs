using Godot;
using System;
using System.Collections.Generic;
using NavalSim.Core;

namespace NavalSim;

/// <summary>
/// LA SOUTE QUI ROMPT LE NAVIRE — L'Orient à Aboukir, 1798 : le feu gagne la
/// soute, et ce qui remonte n'est plus un navire mais deux tronçons qui s'en vont
/// chacun de son côté.
///
/// L'ARRIÈRE RESTE LE NAVIRE, dans la flotte, avec sa rangée de profil : la mer
/// continue de le voir, l'écume de l'entourer, les autres de le heurter. L'AVANT
/// devient une ÉPAVE à part, menée par le même solveur — ses sondes, ses
/// compartiments, son eau — sans barre ni toile, et sans rangée de profil : elle
/// s'emplit par sa tranche, l'eau dedans est la vraie, et il n'y a pas d'écume à
/// border autour d'un tronçon qui sombre. Elle n'occupe donc pas une place de la
/// flotte (seize, mesurées) pour une minute de vie.
/// </summary>
public partial class ShipDemo
{
    sealed class Half
    {
        public ShipPhysics Phys = null!;
        public Node3D Node = null!;
        /// <summary>L'arrière dont elle s'est rompue : il doit oublier ce qu'elle emporte quand elle part.</summary>
        public ShipNode? From;
        public StandardMaterial3D? Char;
        public double Born;
        /// <summary>Les pièces qu'elle emporte, et l'heure où chacune s'en arrachera (ShipDemo.LooseGuns).</summary>
        public List<(Node3D Pivot, Gun G, Vector3 Home)> Pieces = new();
        public double[]? PieceAt;
    }

    readonly List<Half> _halves = new();
    /// <summary>Ce qu'on donne à une épave : rien. Ni barre, ni machine, ni toile.</summary>
    readonly Controls _adrift = new() { Throttle = 0, Rudder = 0, Sheet = 0, SailsSet = false };

    /// <summary>
    /// La rompre. À la cloison du milieu, l'une ou l'autre : la soute est sous le
    /// grand mât, et ce n'est pas toujours l'avant qui emporte le plus.
    /// </summary>
    void BreakUp(ShipNode s)
    {
        var ph = s.Physics;
        if (ph.Broken || s.IsGhost || ph.Probes.Length < 40) return;
        int k = _stormRng.NextDouble() < 0.5 ? 2 : 3;
        float zCut = (float)ph.Bulkhead(k);

        var holder = new Node3D { Name = s.Name + "_avant" };
        AddChild(holder);
        holder.GlobalTransform = s.GlobalTransform;
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var ember = s.Break(zCut, holder);
        double cutMs = clock.Elapsed.TotalMilliseconds;
        if (ember == null) { holder.QueueFree(); return; }

        /* L'ARRIÈRE PEND PLUS OU MOINS LONGTEMPS, et la proue part d'ordinaire la première :
           ce que le souffle a disjoint dans son château n'est jamais deux fois le même —
           de la moitié à une fois et demie la règle —, et une mer forte qui balaie ses
           hauts l'emplit plus vite (12 % de plus par degré au-delà de force 3). */
        double leak = ShipPhysics.AftLeak * (0.5 + _stormRng.NextDouble()) * (1 + 0.12 * Math.Max(0, _force - 3));
        var bow = ph.SplitOff(k, leak);
        bow.OnSlam = QueueSlam;
        /* LE SOUFFLE LES ÉCARTE, et les soulève : la soute est entre les deux.
           Deux mètres par seconde à elles deux — de quoi ouvrir la brèche à
           l'œil dans la seconde, pas de quoi les jeter en l'air. */
        var f = ph.Body.Quat.Rotate(new Vec3d(0, 0, 1));
        double total = ph.Body.Mass + bow.Body.Mass;
        bow.Body.Vel += f * (2.0 * ph.Body.Mass / total) + new Vec3d(0, 0.6, 0);
        ph.Body.Vel += f * (-2.0 * bow.Body.Mass / total) + new Vec3d(0, 0.6, 0);

        _halves.Add(new Half { Phys = bow, Node = holder, Char = ember, Born = _t, From = s, Pieces = s.HandOver(zCut, holder) });
        // la mer bout là où elle s'est rompue, sur la moitié de sa longueur, trois quarts de minute
        var cut = ph.Body.Quat.Rotate(new Vec3d(0, 0, zCut)) + ph.Body.Pos;
        _wreckAir.Churn(cut.X, cut.Z, Math.Max(8, s.Spec.L * 0.5), 45, 1.5);
        Reprofile(s, double.NegativeInfinity, zCut);
        Say(s == _ship ? "La soute nous coupe en deux !" : $"Le {s.Spec.Name} se rompt en deux !");
        GD.Print(FormattableString.Invariant($"rupture de {s.Spec.Id} a la cloison {k} (z {zCut:F1} m) : coupe du bois {cutMs:F1} ms, en tout {clock.Elapsed.TotalMilliseconds:F1} ms"));
    }

    /// <summary>
    /// SA RANGÉE DE PROFIL RACCOURCIE à ce qu'il reste d'elle : la mer creuse son
    /// sillage et efface l'eau DANS la coque d'après ce profil, et elle aurait
    /// gardé un trou d'eau là où l'avant n'est plus.
    /// </summary>
    void Reprofile(ShipNode s, double lo, double hi)
    {
        if (s == _ship)
        {
            _prof = _prof.Cut(s.Spec, lo, hi);
            _sea.SetHullProfile(0, _prof);
            return;
        }
        if (_hulls.TryGetValue(s, out var h))
        {
            _hulls[s] = (h.Prof.Cut(s.Spec, lo, hi), h.Col, h.Y);
            RefitFleet();
        }
    }

    /// <summary>Les épaves, en sous-pas comme la flotte, et ce qui s'en va.</summary>
    void StepHalves(int sub, double dt, double t0)
    {
        for (int i = _halves.Count - 1; i >= 0; i--)
        {
            var h = _halves[i];
            double t = t0;
            for (int k = 0; k < sub; k++) { h.Phys.Step(dt, _sea.Core, _adrift, t); t += dt; }
            var b = h.Phys.Body;
            h.Node.Position = b.Pos.ToGodot();
            h.Node.Quaternion = new Quaternion((float)b.Quat.X, (float)b.Quat.Y, (float)b.Quat.Z, (float)b.Quat.W).Normalized();
            /* LES BRAISES S'ÉTEIGNENT, en une vingtaine de secondes, et sous l'eau
               d'un coup : la matière est aux deux tranches à la fois. */
            if (h.Char != null)
                h.Char.EmissionEnergyMultiplier = (float)(3.0 * Math.Exp(-(_t - h.Born) / 9.0));
            /* PARTIE QUAND ON NE PEUT PLUS LA VOIR : quarante mètres sous la mer,
               ou trop loin. L'arrière, lui, suit la règle de la flotte. */
            double dx = b.Pos.X - _ship.Physics.Body.Pos.X, dz = b.Pos.Z - _ship.Physics.Body.Pos.Z;
            if ((h.Phys.Foundered && h.Phys.DepthBelow > 40) || dx * dx + dz * dz > 4000.0 * 4000.0)
            {
                // l'arrière oublie d'abord ce qui part avec elle (ShipNode.ForgetUnder)
                if (h.From != null && IsInstanceValid(h.From)) h.From.ForgetUnder(h.Node);
                GD.Print(FormattableString.Invariant($"[épave] l'avant rompu est parti, {h.Phys.DepthBelow:F0} m sous la mer"));
                h.Node.QueueFree();
                _halves.RemoveAt(i);
            }
        }
    }

    /// <summary>L'origine glisse : les épaves avec elle, comme tout ce qui tient une position.</summary>
    void RebaseHalves(double dx, double dz)
    {
        foreach (var h in _halves)
        {
            var b = h.Phys.Body;
            b.Pos = new Vec3d(b.Pos.X + dx, b.Pos.Y, b.Pos.Z + dz);
        }
    }

    /// <summary>
    /// UN NAVIRE ROMPU NE SE RADOUBE PAS : il se REMPLACE, entier, à la même
    /// place et au même cap. On ne recoud pas une coque dont l'avant est au fond.
    /// </summary>
    void Rebuild()
    {
        var b0 = _ship.Physics.Body;
        var f = b0.Quat.Rotate(new Vec3d(0, 0, 1));
        double yaw = Math.Atan2(f.X, f.Z), x = b0.Pos.X, z = b0.Pos.Z;
        Launch(_index);
        var b = _ship.Physics.Body;
        b.Pos = new Vec3d(x, b.Pos.Y, z);
        b.Quat = Quatd.FromAxisAngle(new Vec3d(0, 1, 0), yaw);
        _ship.SyncTransform();
    }
}
