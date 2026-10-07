using Godot;
using System;
using NavalSim.Core;

namespace NavalSim;

/// <summary>
/// L'ESSAI DU HEURT À QUAI (-- --heurter fiche) : une coque de la rade posée droit devant
/// l'étrave, à une longueur et demie, et le navire lancé dessus à quatre mètres par
/// seconde ; la distance, la vitesse et l'enfoncement relevés chaque demi-seconde.
/// Rien n'est enregistré.
/// </summary>
public partial class ShipDemo
{
    string? _bumpTest;
    double _bumpT = -1, _bumpNext;
    Vec3d _bumpAt;

    void BumpTestTick()
    {
        if (_bumpTest == null || _moored == null || !_booted) return;
        var b = _ship.Physics.Body;
        if (_bumpT < 0)
        {
            var o = _sea.Core.Origin;
            var fwd = b.Quat.Rotate(new Vec3d(0, 0, 1)); fwd.Y = 0; fwd = fwd.Normalized();
            double ahead = _ship.Spec.L * 0.5 + 40;
            _bumpAt = new Vec3d(o.X + b.Pos.X + fwd.X * ahead, 0, o.Z + b.Pos.Z + fwd.Z * ahead);
            // en travers de la route : on l'aborde par le flanc
            double yaw = Math.Atan2(fwd.X, fwd.Z) + Math.PI / 2;
            if (_moored.AddHull(_bumpTest, _bumpAt.X, _bumpAt.Z, yaw) == null) { GD.Print($"[heurt] fiche {_bumpTest} introuvable"); _bumpTest = null; return; }
            b.Vel = fwd * 4.0;
            _ship.Ctrl.Throttle = 0.6;
            _bumpT = 0; _bumpNext = 0;
            GD.Print(FormattableString.Invariant($"[heurt] {_bumpTest} posé à {ahead:F0} m devant l'étrave, en travers ; lancé à 4 m/s"));
            return;
        }
        _bumpT += GetProcessDeltaTime();
        if (_bumpT < _bumpNext) return;
        _bumpNext += 0.5;
        var oo = _sea.Core.Origin;
        double d = Math.Sqrt(Math.Pow(oo.X + b.Pos.X - _bumpAt.X, 2) + Math.Pow(oo.Z + b.Pos.Z - _bumpAt.Z, 2));
        GD.Print(FormattableString.Invariant($"[heurt] t {_bumpT:F1} s  centres à {d:F1} m  vitesse {b.Vel.LengthXZ:F2} m/s  enfoncé {_ship.Physics.Touching:F2} m  voisins {_moored.Near.Count}"));
        if (_bumpT > 25) _bumpTest = null;
    }
}
