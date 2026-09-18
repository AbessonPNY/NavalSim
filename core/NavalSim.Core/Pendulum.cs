using System;

namespace NavalSim.Core;

/// <summary>
/// UNE LANTERNE PENDUE EST UN VRAI PENDULE — swingLanterns (ship-model.js).
///
/// La lampe est un poids au bout d'une ligne de longueur fixe, intégré dans le
/// monde, et ce qui le mène est la gravité MOINS l'accélération du crochet. Le
/// crochet est à des mètres au-dessus du centre de roulis, donc chaque coup de
/// roulis le jette de côté ; la lampe reste en arrière, rattrape, et passe
/// au-delà. C'est la danse, et aucun angle n'y est écrit à la main.
///
/// Le poids est tenu RELATIVEMENT au crochet (<c>O</c>, axes monde) avec sa
/// vitesse relative (<c>U</c>), et la vitesse du crochet vient de celle du corps
/// (v + ω × r) : les sauts de l'origine flottante ne l'atteignent donc jamais.
/// Une image pressée (plus d'un quart de seconde) la laisse simplement pendre.
/// </summary>
public sealed class Pendulum
{
    public readonly double Len;
    Vec3d _o, _u, _vh, _ah;
    bool _live;

    public Pendulum(double len) { Len = Math.Max(0.15, len); }

    /// <summary>
    /// Un pas. <paramref name="hookVel"/> est la vitesse monde du crochet. Rend la
    /// direction monde du crochet vers la lampe, unitaire.
    /// </summary>
    public Vec3d Step(Vec3d hookVel, double dt)
    {
        if (!_live || dt > 0.25)
        {
            _o = new Vec3d(0, -Len, 0); _u = Vec3d.Zero;
            _vh = hookVel; _ah = Vec3d.Zero; _live = true;
        }
        else if (dt > 0)
        {
            Vec3d a = (hookVel - _vh) * (1 / dt);
            _vh = hookVel;
            // les sous-pas du solveur la rendent granuleuse : un lissage de 40 ms
            double s = 1 - Math.Exp(-dt / 0.04);
            _ah = _ah + (a - _ah) * s;
            // une lampe au bout d'une ligne n'amortit presque rien
            double w = Math.Sqrt(9.81 / Len), c = 2 * 0.05 * w;
            int n = (int)Math.Ceiling(dt / 0.008);
            double h = dt / n;
            for (int i = 0; i < n; i++)
            {
                _u = new Vec3d(_u.X - _ah.X * h, _u.Y + (-9.81 - _ah.Y) * h, _u.Z - _ah.Z * h);
                _u = _u * Math.Exp(-c * h);
                _o = _o + _u * h;
                double l = _o.Length;
                if (l > 0) _o = _o * (Len / l);
                Vec3d d = _o * (1 / Len);
                // la ligne reprend le reste : pas de vitesse le long d'elle
                double along = _u.X * d.X + _u.Y * d.Y + _u.Z * d.Z;
                _u = _u - d * along;
            }
        }
        return _o.Normalized();
    }
}
