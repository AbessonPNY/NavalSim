using Godot;
using System;
using System.Collections.Generic;
using NavalSim.Core;

namespace NavalSim;

/// <summary>
/// L'ÉTRAVE QUI FEND L'EAU (demandé, avec un croquis : une moustache de chaque bord).
/// En marche, la coque écarte l'eau devant elle ; l'eau monte le long de l'étrave en
/// vague d'étrave et, passé une certaine allure, s'en détache en nappe d'embrun
/// rejetée de chaque bord, vers l'extérieur et un peu vers le haut.
///
/// L'ALLURE QUI COMPTE est celle de la coque PAR RAPPORT À SA LONGUEUR — le nombre de
/// Froude, v/√(gL) : une vague d'étrave grandit avec lui, et un galion de quarante
/// mètres à six noeuds la porte bien moins haut qu'une chaloupe lancée. Rien sous
/// 0,12 ; puis la nappe croît comme le carré de l'excès, et redouble quand l'étrave
/// PLONGE dans une lame (sa vitesse verticale contre la mer). Chaque bord a son
/// éventail (SprayPool.Fan), qui garde une part de la vitesse de la coque : la nappe
/// naît à son pas et tombe en arrière d'elle.
/// </summary>
public partial class ShipDemo
{
    readonly Dictionary<ShipNode, double> _bowNext = new();
    readonly Random _bowRng = new();

    /// <summary>La demi-largeur de la coque à la flottaison (y = 0) à la station t (0 l'étambot, 1 l'étrave).</summary>
    static double WaterHalf(HullLines lines, double t)
    {
        double deck = lines.DeckY(t), keel = lines.KeelY(t);
        if (keel >= 0 || deck <= keel) return 0;
        return lines.HalfB(t) * lines.BeamFactor(Math.Clamp(deck / (deck - keel), 0, 1));
    }

    void BowSprayTick()
    {
        var cam = _cam.GlobalPosition;
        BowSpray(_ship, cam);
        foreach (var s in _others) BowSpray(s, cam);
        if (_bowNext.Count > 32)
            foreach (var k in new List<ShipNode>(_bowNext.Keys)) if (!IsInstanceValid(k)) _bowNext.Remove(k);
    }

    void BowSpray(ShipNode s, Vector3 cam)
    {
        var ph = s.Physics;
        if (ph.Broken || ph.Foundered || ph.Afloat < 0.5 || s.IsGhost) return;
        var b = ph.Body;
        // loin de l'œil, personne ne la verrait : des gouttes épargnées
        double cx = cam.X - b.Pos.X, cz = cam.Z - b.Pos.Z;
        if (cx * cx + cz * cz > 450.0 * 450.0) return;
        var sp = s.Spec;
        var fwd = b.Quat.Rotate(new Vec3d(0, 0, 1));
        double v = b.Vel.Dot(fwd);
        double fr = v / Math.Sqrt(Config.G * Math.Max(2, sp.L));
        /* L'ÉTRAVE À LA FLOTTAISON, LUE SUR LA COQUE (signalé : la moustache ne touchait pas
           la coque). Une fraction fixe de la longueur tombait devant l'étrave d'un galion,
           dont l'éperon déborde loin au-dessus de l'eau. Le profil de flottaison mesuré sur
           le modèle (le même que le collier et le sillage) dit où le bordé entre dans l'eau
           et sa demi-largeur là : la première station, depuis l'avant, où la coque a pris
           le tiers de son bau — c'est là qu'elle pousse l'eau. L'éventail part DU BORDÉ. */
        double zStem = sp.L * 0.44, half = Math.Max(0.3, sp.B * 0.08);
        {
            /* lue sur le PLAN DE FORMES, l'unique de la fiche (et non sur le profil mesuré du
               modèle, qui pour la Roter Löwe ne voit qu'un bout de coque) : sa demi-largeur à
               la flottaison, station par station, depuis l'étrave */
            var lines = s.Lines;
            double best = 0;
            for (int k = 0; k <= 40; k++) best = Math.Max(best, WaterHalf(lines, k / 40.0));
            for (int k = 40; k >= 0; k--)
            {
                double tt = k / 40.0, hw = WaterHalf(lines, tt);
                if (hw < 0.3 * best) continue;
                zStem = (tt - 0.5) * sp.L; half = hw;
                break;
            }
        }
        var stem = b.Quat.Rotate(new Vec3d(0, 0, zStem)) + b.Pos;
        double sea = _sea.Core.Sample(stem.X, stem.Z, _t);
        // l'étrave qui plonge dans la lame : sa vitesse verticale, contre celle de la mer (lue sur un pas)
        double dip = Math.Max(0, -(b.Vel.Y + b.AngVel.Cross(stem - b.Pos).Y));
        double drive = Math.Max(0, fr - 0.12) + 0.08 * dip;
        if (drive <= 0) return;
        if (_bowNext.TryGetValue(s, out double next) && _t < next) return;
        _bowNext[s] = _t + 0.07 + 0.08 * _bowRng.NextDouble();
        // la nappe : comme le carré de l'excès, à l'échelle de la coque
        double water = Math.Min(1.4, 40 * drive * drive * Math.Max(0.4, sp.B / 6));
        double speed = 1.5 + 2.2 * Math.Max(0, v) * (0.6 + 0.6 * drive);
        var right = b.Quat.Rotate(new Vec3d(-1, 0, 0));          // tribord est −x
        var carry = b.Vel * 0.7;
        foreach (int side in new[] { -1, 1 })
        {
            // de chaque joue de l'étrave, vers l'extérieur, et un peu vers l'avant
            var at = stem + right * (side * half * 1.02);
            at = new Vec3d(at.X, sea + 0.1, at.Z);
            var dir = right * side + fwd * 0.35;
            _spray.Pool.Fan(at, dir, water * (0.8 + 0.4 * _bowRng.NextDouble()), speed, carry);
        }
    }
}
