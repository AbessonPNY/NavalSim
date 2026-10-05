using Godot;
using System;
using System.Collections.Generic;
using NavalSim.Core;

namespace NavalSim;

/// <summary>
/// LES PIÈCES QUI S'ARRACHENT (demandé) — quand un navire sombre, ses canons ne coulent
/// pas avec lui comme s'ils y étaient vissés : deux tonnes de fonte sur un affût tenu par
/// une brague et deux palans. Le navire qui chavire, qui se rompt ou qui s'enfonce les
/// arrache une à une, et chacune tombe à part.
///
/// QUAND : à la rupture, celles collées à la tranche, tout de suite ; celles de l'avant partent
/// AVEC LUI (ShipNode.HandOver) et s'en arrachent quand il sombre (le souffle
/// les a jetées) ; quand la gîte passe 50°, toutes, de quelques secondes en quelques
/// secondes (les bragues cassent sous un affût qui roule) ; sombrée, les dernières, sur
/// huit secondes. Chacune à son heure, tirée au hasard.
///
/// COMMENT ELLE TOMBE : elle part avec la vitesse du point de coque où elle était, plus
/// un élan vers le dehors ; dans l'air, la pesanteur ; dans l'eau, son poids moins la
/// poussée — la fonte (7,2) dans l'eau de mer (1,025) : 8,4 m/s² —, freinée par une
/// traînée qui la tient vers six mètres par seconde. Elle culbute lentement, et se pose
/// sur le fond, où elle reste. En mètres VRAIS : l'origine peut glisser pendant qu'elle
/// tombe, et elle reste où elle est.
/// </summary>
public partial class ShipDemo
{
    sealed class LooseGun
    {
        public Node3D Node = null!;
        public Vec3d Pos, Vel, Spin;     // Pos en mètres vrais ; Spin : l'axe × la vitesse de culbute
        public bool Wet, Rest;
        public double Born;
        /// <summary>Le navire d'où elle vient (pour son épave), et le milieu de la pièce dans son repère.</summary>
        public ShipNode? Owner;
        public Vector3 Home;
        /// <summary>Inscrite au registre de son épave : celle-ci la montrera quand le navire aura quitté la scène.</summary>
        public string? WreckId;
    }

    readonly List<LooseGun> _loose = new();
    /// <summary>L'heure à laquelle chaque pièce encore en place doit s'arracher, par navire.</summary>
    readonly Dictionary<ShipNode, double[]> _looseAt = new();
    readonly Random _looseRng = new();
    Node3D? _looseRoot;
    const int LooseMax = 96;

    void LooseGunsTick(double dt)
    {
        CheckLoose(_ship);
        foreach (var s in _others) CheckLoose(s);
        foreach (var h in _halves) CheckHalf(h);
        StepLoose(dt);
    }

    static double HeelOf(ShipPhysics ph) =>
        Math.Acos(Math.Clamp(ph.Body.Quat.Rotate(new Vec3d(0, 1, 0)).Y, -1, 1)) * 180 / Math.PI;

    /// <summary>Le tronçon avant : ses pièces s'en arrachent quand il sombre ou chavire.</summary>
    void CheckHalf(Half h)
    {
        if (h.Pieces.Count == 0) return;
        bool sunk = h.Phys.Foundered, rolled = HeelOf(h.Phys) > 50;
        if (!sunk && !rolled) return;
        if (h.PieceAt == null)
        {
            h.PieceAt = new double[h.Pieces.Count];
            for (int i = 0; i < h.PieceAt.Length; i++)
                h.PieceAt[i] = _t + (rolled ? 0.5 + _looseRng.NextDouble() * 3.5 : _looseRng.NextDouble() * 8);
        }
        _looseRoot ??= MakeLooseRoot();
        for (int i = 0; i < h.Pieces.Count; i++)
        {
            if (h.PieceAt[i] > _t || h.PieceAt[i] < 0) continue;
            h.PieceAt[i] = -1;
            var (node, g, home) = h.Pieces[i];
            if (!IsInstanceValid(node) || _loose.Count >= LooseMax) continue;
            node.Reparent(_looseRoot, true);
            Release(node, h.Phys.Body, g, h.From, home);
        }
    }

    /// <summary>Elle part : la vitesse du point de coque où elle était, un élan vers le dehors, une culbute.</summary>
    void Release(Node3D node, NavalSim.Core.Body b, Gun g, ShipNode? owner, Vector3 home)
    {
        var o = _sea.Core.Origin;
        var local = node.GlobalPosition.ToCore();
        var v = b.Vel + b.AngVel.Cross(local - b.Pos);
        var outward = b.Quat.Rotate(new Vec3d(g.Dir.X, 0, g.Dir.Z));
        v += outward * (0.4 + 0.8 * _looseRng.NextDouble()) + new Vec3d(0, 0.3 * _looseRng.NextDouble(), 0);
        var spin = new Vec3d(_looseRng.NextDouble() - 0.5, _looseRng.NextDouble() - 0.5, _looseRng.NextDouble() - 0.5) * 1.2;
        _loose.Add(new LooseGun { Node = node, Pos = new Vec3d(local.X + o.X, local.Y, local.Z + o.Z), Vel = v, Spin = spin, Born = _t, Owner = owner, Home = home });
    }

    /// <summary>Décider qui s'arrache, et quand ; puis arracher celles dont l'heure est venue.</summary>
    void CheckLoose(ShipNode s)
    {
        var ph = s.Physics;
        int n = Math.Min(s.PieceCount, s.Battery.Guns.Count);
        if (n == 0) return;
        var b = ph.Body;
        bool sunk = ph.Foundered, rolled = HeelOf(ph) > 50, broken = ph.Broken;
        if (!sunk && !rolled && !broken)
        {
            _looseAt.Remove(s);
            return;
        }
        if (!_looseAt.TryGetValue(s, out var at))
        {
            at = new double[n];
            for (int i = 0; i < n; i++) at[i] = double.MaxValue;
            _looseAt[s] = at;
        }
        for (int i = 0; i < n; i++)
        {
            if (at[i] != double.MaxValue) continue;
            var g = s.Battery.Guns[i];
            // collées à la tranche d'une rupture : le souffle les a jetées (celles de l'avant sont parties avec lui)
            if (broken && g.P.Z <= ph.ZHi && g.P.Z > ph.ZHi - 3) at[i] = _t + _looseRng.NextDouble() * 1.2;
            else if (rolled) at[i] = _t + 0.5 + _looseRng.NextDouble() * 3.5;
            else if (sunk) at[i] = _t + _looseRng.NextDouble() * 8;
        }
        _looseRoot ??= MakeLooseRoot();
        var o = _sea.Core.Origin;
        for (int i = 0; i < n; i++)
        {
            if (at[i] > _t || at[i] < 0) continue;
            at[i] = -1;                                   // partie, ou sans pièce : on n'y revient pas
            if (_loose.Count >= LooseMax) continue;
            if (s.DetachPiece(i, _looseRoot) is not { } det) continue;
            var g = s.Battery.Guns[i];
            g.Out = true;                                 // elle ne tire plus
            Release(det.Pivot, b, g, s, det.Home);
        }
    }

    Node3D MakeLooseRoot()
    {
        var n = new Node3D { Name = "PiecesArrachees" };
        AddChild(n);
        return n;
    }

    void StepLoose(double dt)
    {
        if (_loose.Count == 0 || _world == null) return;
        var o = _sea.Core.Origin;
        for (int i = _loose.Count - 1; i >= 0; i--)
        {
            var L = _loose[i];
            if (!IsInstanceValid(L.Node)) { _loose.RemoveAt(i); continue; }
            if (!L.Rest)
            {
                double sea = _sea.Core.Sample(L.Pos.X - o.X, L.Pos.Z - o.Z, _t);
                bool wet = L.Pos.Y < sea;
                if (wet && !L.Wet)
                {
                    // elle crève la surface : une gerbe à sa mesure
                    _spray.Pool.Burst(new Vec3d(L.Pos.X - o.X, sea, L.Pos.Z - o.Z), 0.6 + 0.2 * L.Vel.Length, 2 + Math.Abs(L.Vel.Y), 1.3);
                }
                L.Wet = wet;
                double sp = L.Vel.Length;
                Vec3d acc = wet
                    ? new Vec3d(0, -Config.G * (1 - 1025.0 / 7200.0), 0) - L.Vel * (0.233 * sp)   // ~6 m/s au plus
                    : new Vec3d(0, -Config.G, 0) - L.Vel * (0.002 * sp);
                L.Vel += acc * dt;
                L.Pos += L.Vel * dt;
                // elle culbute, et l'eau la freine
                if (wet) L.Spin *= Math.Max(0, 1 - dt * 0.4);
                double w = L.Spin.Length;
                if (w > 1e-4)
                {
                    var ax = new Vector3((float)(L.Spin.X / w), (float)(L.Spin.Y / w), (float)(L.Spin.Z / w));
                    L.Node.Basis = new Basis(ax, (float)(w * dt)) * L.Node.Basis;
                }
                // le fond : elle s'y pose, et y reste
                double bed = _world.HeightAt(L.Pos.X, L.Pos.Z) + 0.25;
                if (L.Pos.Y <= bed)
                {
                    L.Pos = new Vec3d(L.Pos.X, bed, L.Pos.Z);
                    L.Rest = true;
                    // couchée : son axe garde son cap, à plat sur le fond
                    var f = L.Node.Basis.Z; f.Y = 0;
                    if (f.LengthSquared() > 1e-4f)
                        L.Node.Basis = Basis.LookingAt(-f.Normalized(), Vector3.Up).Scaled(L.Node.Basis.Scale);
                }
            }
            L.Node.Position = new Vector3((float)(L.Pos.X - o.X), (float)L.Pos.Y, (float)(L.Pos.Z - o.Z));
            if (L.Rest) Settle(L);
            /* INSCRITE, ELLE CÈDE LA PLACE À SON ÉPAVE : tant que le navire coulé est dans
               la scène (il gît lui-même au fond), elle reste telle quelle ; parti, l'épave
               est rebâtie d'après le registre — et ses pièces avec elle (WreckSiteNode). */
            if (L.WreckId != null && !(_liveWreck.TryGetValue(L.WreckId, out var live) && IsInstanceValid(live) && (live == _ship || _others.Contains(live))))
            { L.Node.QueueFree(); _loose.RemoveAt(i); continue; }
            // partie trop loin pour qu'on la revoie : rendue (inscrite, son épave la garde)
            double dx = L.Pos.X - o.X - _ship.Physics.Body.Pos.X, dz = L.Pos.Z - o.Z - _ship.Physics.Body.Pos.Z;
            if (dx * dx + dz * dz > 3000.0 * 3000.0) { L.Node.QueueFree(); _loose.RemoveAt(i); }
        }
    }

    /// <summary>
    /// POSÉE : l'inscrire au registre de son épave — d'où elle vient, où elle gît, sa pose.
    /// L'épave n'est inscrite qu'en sombrant : une pièce posée avant attend, image après image.
    /// </summary>
    void Settle(LooseGun L)
    {
        if (L.WreckId != null || L.Owner == null) return;
        string? id = null;
        foreach (var (wid, s) in _liveWreck) if (s == L.Owner) { id = wid; break; }
        if (id == null) return;
        Wreck? w = null;
        foreach (var x in Wrecks.All) if (x.Id == id) { w = x; break; }
        if (w == null) return;
        var bs = L.Node.Basis;
        // le milieu de ce qu'on voit d'elle, en mètres vrais : l'épave la reposera par lui
        var o = _sea.Core.Origin;
        var mid = L.Node.Transform * ((NodeWalk.Bounds(L.Node) ?? new Aabb()).GetCenter());
        w.Guns.Add(new WreckGun
        {
            Home = new double[] { L.Home.X, L.Home.Y, L.Home.Z },
            X = mid.X + o.X, Y = mid.Y, Z = mid.Z + o.Z,
            Basis = new double[] { bs.X.X, bs.Y.X, bs.Z.X, bs.X.Y, bs.Y.Y, bs.Z.Y, bs.X.Z, bs.Y.Z, bs.Z.Z }
        });
        L.WreckId = id;
    }

    /// <summary>Une partie neuve : les pièces au fond appartenaient à l'autre.</summary>
    void LooseReset()
    {
        foreach (var L in _loose) if (IsInstanceValid(L.Node)) L.Node.QueueFree();
        _loose.Clear();
        _looseAt.Clear();
    }
}
