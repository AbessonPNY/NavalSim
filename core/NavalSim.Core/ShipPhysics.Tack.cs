using System;

namespace NavalSim.Core;

/// <summary>
/// LE VIREMENT DE BORD — ce que fait l'équipage, et que le seul jeu des voiles ne
/// fait pas (Godot : <see cref="Config.CrewTacks"/>).
///
/// Sans lui, dès que les vergues touchent leur butée, les voiles carrées
/// faseyent et ne poussent plus rien : rien n'aide l'étrave à franchir le lit du
/// vent que l'élan et le gouvernail, et ni le galion (trente-cinq degrés du vent
/// au plus près) ni même le sloop (dix-huit) ne passaient — relevé au banc
/// « virement ». Or un trois-mâts carré virait, et un sloop vire franchement ;
/// c'est l'équipage qui fait la différence, en deux temps :
///
///  1. « ADIEU-VA » — l'étrave vient au vent : on BORDE L'ARTIMON AU VENT (sur un
///     sloop, la grand-voile à plat). Le vent sur sa face pousse la poupe sous le
///     vent, donc l'étrave au vent ;
///  2. « CHANGE DERRIÈRE » — le vent passé, on brasse le grand mât et l'artimon,
///     mais on LAISSE LA MISAINE à contre (sur un sloop, le foc) : brassée pour
///     l'ancien bord, elle prend le vent du nouveau par sa face avant et pousse
///     l'étrave sur le nouveau bord. Elle freine aussi, de son vrai poids.
///
/// Les forces sont celles du vent réel sur ces surfaces (une plaque, Cd 1,1), à
/// leur bras de levier réel : rien n'est inventé que le geste. La manœuvre naît
/// de la barre mise du côté du vent, au près ; elle finit quand le navire
/// est à soixante degrés du vent sur le nouveau bord, s'il abat avant d'y
/// arriver, ou si l'on rend la barre plus de deux secondes.
/// </summary>
public sealed partial class ShipPhysics
{
    int _tackDir, _tackFrom, _tackRudder;
    double _tackIdle;

    /// <summary>0 : pas de virement ; 1 : l'étrave vient au vent ; 2 : le vent passé, la misaine à contre.</summary>
    public int TackPhase => _tackDir == 0 ? 0 : (Tack == _tackFrom ? 1 : 2);

    const double PlateCd = 1.1;

    void CrewTack(double dt, Controls ctrl, Ocean ocean, in Vec3d cog,
                  ref Vec3d force, ref Vec3d torque, in Vec3d fwd, in Vec3d right)
    {
        if (!Config.CrewTacks) { _tackDir = 0; return; }
        var S = Spec; var b = Body;
        double sails = SetFrac * Standing * Whole;
        Vec3d app = ocean.WindVec - b.Vel;
        app.Y = 0;
        double vApp = app.Length;
        if (sails < 0.2 || vApp < 0.5 || S.SailArea <= 0) { _tackDir = 0; return; }
        double fromFwd = -(app.X * fwd.X + app.Z * fwd.Z) / vApp;
        double fromRight = -(app.X * right.X + app.Z * right.Z) / vApp;
        double beta = Math.Atan2(Math.Abs(fromRight), fromFwd);
        int side = fromRight >= 0 ? 1 : -1;                  // +1 : le vent sur la joue tribord
        double vFwd = b.Vel.X * fwd.X + b.Vel.Z * fwd.Z;
        /* LE SENS QUE DEMANDE LA BARRE, en lacet (ω.Y) : barre positive en marche
           avant, la poupe part à bâbord et l'étrave à tribord — ω.Y négatif. */
        int helm = Math.Abs(ctrl.Rudder) > 0.3 ? -Math.Sign(ctrl.Rudder) * (vFwd >= 0 ? 1 : -1) : 0;
        int toward = -side;                                  // le lacet qui porte l'étrave au vent

        if (_tackDir == 0)
        {
            if (Math.Abs(ctrl.Rudder) > 0.5 && helm == toward && beta < 75 * Math.PI / 180 && vFwd > 0.3)
            {
                _tackDir = toward; _tackFrom = side; _tackIdle = 0; _tackRudder = Math.Sign(ctrl.Rudder);
            }
            else return;
        }
        /* LA BARRE DOIT RESTER MISE, du même bord — c'est elle qu'on lit, et non le
           sens où elle fait tourner : quand le navire cule, vent debout, le
           gouvernail agit à rebours, et l'équipage ne lâche pas sa misaine pour
           autant (relevé au banc : le galion passait le vent, puis la manœuvre
           s'arrêtait dès qu'il culait). Rendue ou renversée plus de deux secondes,
           on renonce. */
        if (Math.Abs(ctrl.Rudder) < 0.3 || Math.Sign(ctrl.Rudder) != _tackRudder) { _tackIdle += dt; if (_tackIdle > 2) { _tackDir = 0; return; } }
        else _tackIdle = 0;
        bool crossed = side != _tackFrom;
        if (!crossed && beta > 85 * Math.PI / 180) { _tackDir = 0; return; }   // elle a abattu
        if (crossed && beta > 60 * Math.PI / 180) { _tackDir = 0; return; }     // pleine sur le nouveau bord

        double q = 0.5 * Config.RhoAir * vApp * vApp;
        Vec3d leeward = right * (-side);                     // sous le vent, à l'horizontale
        Vec3d F, at;
        if (!crossed)
        {
            /* L'ARTIMON BORDÉ AU VENT, ou la grand-voile à plat sur un sloop : son
               centre est sur l'arrière du centre de gravité. */
            bool lat = S.LateenArea > 0;
            double A = (lat ? S.LateenArea : 0.4 * S.SailArea) * sails;
            var where = lat ? _ceL : new Vec3d(0, _ce.Y, Math.Min(_ce.Z, 0) - 0.15 * S.L);
            F = leeward * (PlateCd * q * A * Math.Sin(Math.Max(beta, 0.3)));
            at = where;
        }
        else
        {
            /* LA MISAINE À CONTRE, brassée pour l'ancien bord : le vent du nouveau la
               prend par sa face avant, à l'incidence de son angle plus celui de la
               butée. Sur un navire à un mât, le foc à contre, au beaupré. */
            double A; Vec3d where;
            if (S.Masts.Count >= 2)
            {
                double tot = 0, foreH = 0, foreZ = double.MinValue;
                foreach (var m in S.Masts)
                {
                    tot += m.Height * m.Height;
                    if (m.Z > foreZ) { foreZ = m.Z; foreH = m.Height; }
                }
                A = S.SquareArea * (tot > 0 ? foreH * foreH / tot : 0.3);
                where = new Vec3d(0, _ce.Y, foreZ);
            }
            else
            {
                A = 0.3 * S.SailArea;
                where = new Vec3d(0, 0.7 * _ce.Y, 0.42 * S.L);
            }
            A *= sails;
            double inc = Math.Min(Math.PI / 2, beta + S.MinSheet);
            double fn = PlateCd * q * A * Math.Sin(inc);
            F = leeward * (fn * Math.Cos(S.MinSheet)) - fwd * (fn * Math.Sin(S.MinSheet));
            at = where;
        }
        /* LE GAIN DE VENT S'Y APPLIQUE, comme à la poussée : c'est du vent dans la
           toile. Sans lui, à gain 8, le navire filait onze nœuds mais la misaine à
           contre ne le tournait qu'avec la force du vent réel — il restait planté
           vent debout (relevé au banc : 0,1°/s). À gain 1, rien ne change. */
        F *= Config.WindGain;
        force += F;
        Vec3d arm = b.Quat.Rotate(at) + b.Pos - cog;
        torque += arm.Cross(F);
    }

    /// <summary>L'angle de la bôme qu'on tient à contre, en radians depuis l'axe.</summary>
    public const double BackedAngle = 0.9;

    /// <summary>
    /// LA VOILE À CONTRE — sortir d'un face au vent. Coincé vent debout, la toile
    /// faseye et ne pousse rien, et sans erre la barre ne mord pas. On POUSSE LA
    /// BÔME d'un bord, à la main : le vent prend la voile par sa face avant, la
    /// pousse vers l'arrière et de ce bord — le navire CULE, et sa poupe part du
    /// côté où l'on pousse, l'étrave de l'autre. Quand le vent est passé sur la
    /// joue voulue, on lâche, on borde, et l'on repart sur ce bord. (En culant, la
    /// barre agit à rebours.)
    ///
    /// Une plaque (Cd 1,1) de la grand-voile, à l'angle de la bôme, frappée par le
    /// vent apparent à son incidence réelle, à l'arrière du mât où est son centre :
    /// le même vent que dans la toile, gain compris. Rien d'inventé que le geste.
    /// </summary>
    void BackSail(Controls ctrl, Ocean ocean, in Vec3d cog,
                  ref Vec3d force, ref Vec3d torque, in Vec3d fwd, in Vec3d right)
    {
        if (ctrl.Backed == 0) return;
        var S = Spec; var b = Body;
        double sails = SetFrac * Standing * Whole;
        if (sails < 0.2 || S.SailArea <= 0) return;
        Vec3d app = ocean.WindVec - b.Vel;
        app.Y = 0;
        double vApp = app.Length;
        if (vApp < 0.3) return;
        Vec3d dir = app * (1 / vApp);                        // où va le vent
        int s = Math.Sign(ctrl.Backed);
        // la bôme, du mât vers l'arrière, ouverte du bord où on la pousse
        Vec3d boom = fwd * -Math.Cos(BackedAngle) + right * (s * Math.Sin(BackedAngle));
        // sa normale, du côté où le vent la pousse
        Vec3d n = new Vec3d(boom.Z, 0, -boom.X);
        if (n.Dot(dir) < 0) n = n * -1;
        double inc = Math.Abs(n.Dot(dir));                  // sin de l'incidence
        double A = (S.LateenArea > 0 ? S.LateenArea : 0.6 * S.SailArea) * sails;
        double q = 0.5 * Config.RhoAir * vApp * vApp;
        Vec3d F = n * (PlateCd * q * A * inc * Config.WindGain);
        // son centre : à mi-bôme, à mi-hauteur de toile, sur le bord poussé
        // tribord est −x : la bôme ouverte à tribord va vers −x, vers l'arrière
        Vec3d at = new Vec3d(-s * Math.Sin(BackedAngle) * 0.3 * S.L, _ce.Y, _ce.Z - Math.Cos(BackedAngle) * 0.3 * S.L);
        force += F;
        Vec3d arm = b.Quat.Rotate(at) + b.Pos - cog;
        torque += arm.Cross(F);
    }
}
