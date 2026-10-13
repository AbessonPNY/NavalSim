using Godot;
using System;
using System.Collections.Generic;
using NavalSim.Core;

namespace NavalSim;

/// <summary>
/// À L'EAU, CE QUE LE NAGEUR FAIT DE SON CORPS — sans qu'on le voie : aucun humain à l'écran
/// (demandé, 13/10 : les bras à la première personne sont partis vers un autre projet ; ils
/// sont dans l'historique, commit cd1d593, ShipDemo.SwimArms.cs et creatures/bras.glb).
///
/// PRENDRE (F) : le corps glisse jusqu'à la chose, et elle quitte le fond au moment où une
/// main l'aurait saisie. SE HISSER (E) : l'œil suit le chemin d'un homme qui monte, prise
/// après prise, par l'échelle du modèle ou par le flanc mesuré, puis se relève sur le pont.
/// </summary>
public partial class ShipDemo
{
    /* ------------------------------------------------------------------ */
    /* PRENDRE : on va à la chose, elle quitte le fond.                    */
    /* ------------------------------------------------------------------ */

    sealed class SwimGrab
    {
        public Find F = null!;
        public double T, Reach;
        public bool Held;              // la main s'est fermée dessus : la chose ne repose plus au fond
    }
    SwimGrab? _grab;
    /// <summary>De l'œil, la distance où il faut être pour prendre une chose : le bras, à peu près tendu.</summary>
    const double GrabStandOff = 0.55;
    /// <summary>Le temps de la ramener vers le sac, avant qu'on puisse en prendre une autre.</summary>
    const double GrabBack = 0.6;

    void GrabStart(Find f)
    {
        var d = new Vec3d(f.X - _swim!.Pos.X, f.Y - _swim.Pos.Y, f.Z - _swim.Pos.Z).Length;
        // plus elle est loin, plus il faut d'un coup de jambes pour aller la chercher
        _grab = new SwimGrab { F = f, Reach = 0.35 + 0.3 * Math.Max(0, d - GrabStandOff) };
    }

    /// <summary>Le corps glisse vers la chose pendant que le bras s'y tend — un bras ne fait pas 1,6 m.</summary>
    void GrabGlide(double dt)
    {
        if (_grab == null || _swim == null || _grab.T >= _grab.Reach) return;
        var f = _grab.F;
        var item = new Vec3d(f.X, f.Y + 0.05, f.Z);
        var away = _swim.Pos - item;
        double L = away.Length;
        if (L <= GrabStandOff) return;
        var target = item + away * (GrabStandOff / L);
        double k = Math.Min(1, dt * 6);
        _swim.Pos = _swim.Pos + (target - _swim.Pos) * k;
        _swim.Vel = _swim.Vel * (1 - k);
    }

    void GrabTick(double dt)
    {
        if (_grab == null) return;
        var g = _grab;
        g.T += dt;
        if (!g.Held && g.T >= g.Reach)
        {
            g.Held = true;
            foreach (var cell in _swimCells.Values)
                foreach (var (ff, p) in cell)
                    if (ff == g.F) p.Node.Visible = false;
        }
        if (g.T >= g.Reach + GrabBack) _grab = null;
    }

    /* ------------------------------------------------------------------ */
    /* SE HISSER : prise après prise le long du flanc, puis par-dessus.    */
    /* ------------------------------------------------------------------ */

    sealed class SwimClimb
    {
        public double T;
        public int Sx;                 // le flanc : +1 bâbord (+x du navire), −1 tribord
        public double Z, Water, Deck;
        public readonly List<double> Rungs = new();
        /// <summary>Le dehors du flanc (ou de l'échelle) à chaque prise : le visage s'y tient à ClimbFace.</summary>
        public readonly List<double> Outs = new();
        public ShipNode.Ladder? By;    // l'échelle prise, s'il y en a une
        public Vec3d EyeFrom;          // l'œil au départ, dans le repère du navire
        public double Approach;        // le temps de venir au flanc
        public double Done;            // la durée entière
    }
    SwimClimb? _climb;
    const double ClimbRung = 0.42, LadderRung = 0.35, ClimbStep = 0.65, ClimbReachPart = 0.45, ClimbMatch = 0.3, ClimbHeave = 0.75, ClimbOver = 0.9;
    /// <summary>L'œil d'un homme debout sur le pont.</summary>
    const double StandEye = 1.55;

    /// <summary>L'échelle la plus proche de ce point (repère du navire), et la distance de son pied.</summary>
    static ShipNode.Ladder? NearestLadder(ShipNode ship, Vec3d l, out double dist)
    {
        dist = double.MaxValue;
        ShipNode.Ladder? best = null;
        foreach (var lad in ship.Ladders())
        {
            double dx = l.X - lad.Side * lad.Out, dz = l.Z - lad.Z;
            double d = Math.Sqrt(dx * dx + dz * dz);
            if (d < dist) { dist = d; best = lad; }
        }
        return best;
    }

    /// <summary>Le flanc à cette hauteur, mesuré sur le modèle ; le profil de flottaison à défaut.</summary>
    double ClimbOut(int sx, double z, double y)
    {
        double o = _ship.SideOut(sx, z, y);
        return double.IsNaN(o) ? _hullCollider?.SideAt(z) ?? _ship.Spec.B * 0.5 : o;
    }
    /// <summary>Le visage contre le bordé : à cette distance de lui.</summary>
    const double ClimbFace = 0.40;

    void ClimbStart()
    {
        if (_swim == null || _hullCollider == null) { SwimBoarded(); return; }
        var b = _ship.Physics.Body;
        var o = _sea.Core.Origin;
        var inv = b.Quat.Inverted();
        var eye = inv.Rotate(new Vec3d(_swim.Pos.X - o.X, _swim.Pos.Y, _swim.Pos.Z - o.Z) - b.Pos);
        var hc = _hullCollider;
        var c = new SwimClimb { Water = eye.Y - Swimmer.EyeAbove, EyeFrom = eye };
        var lad = NearestLadder(_ship, eye, out _);
        double top, rung;
        if (lad != null)
        {
            // PAR L'ÉCHELLE : jusqu'à son haut, et au plat-bord s'il n'est guère plus haut
            // qu'elle — sinon elle mène à une coupée, et l'on entre par là
            c.By = lad; c.Sx = lad.Side; c.Z = lad.Z;
            double rail = _ship.SideTop(lad.Side, lad.Z);
            top = !double.IsNaN(rail) && rail > lad.Top && rail - lad.Top < 0.6 ? rail : lad.Top;
            rung = LadderRung;
        }
        else
        {
            c.Sx = eye.X >= 0 ? 1 : -1;
            c.Z = Math.Clamp(eye.Z, hc.EndAft * 0.75, hc.EndFwd * 0.75);
            double rail = _ship.SideTop(c.Sx, c.Z);
            top = double.IsNaN(rail) ? hc.TopAt(c.Z) : rail;
            rung = ClimbRung;
        }
        for (double y = top; y > c.Water + 0.35; y -= rung) c.Rungs.Insert(0, y);
        if (c.Rungs.Count == 0) c.Rungs.Add(top);
        for (int i = 0; i < c.Rungs.Count; i++)
        {
            bool last = i == c.Rungs.Count - 1;
            // sur l'échelle, ses barreaux ; en haut, le plat-bord lui-même
            c.Outs.Add(lad != null && !(last && top > lad.Top) ? lad.Out : ClimbOut(c.Sx, c.Z, c.Rungs[i] - 0.05));
        }
        c.Deck = _ship.DeckTop(c.Sx * (c.Outs[^1] - 0.75), c.Z);
        double gap = Math.Sqrt(Math.Pow(Math.Abs(eye.X) - (c.Outs[0] + ClimbFace), 2) + Math.Pow(eye.Z - c.Z, 2));
        c.Approach = 0.35 + gap / 1.1;
        c.Done = c.Approach + c.Rungs.Count * ClimbStep + (c.Rungs.Count > 1 ? ClimbMatch : 0) + ClimbHeave + ClimbOver;
        _climb = c;
        _grab = null;
        GD.Print(FormattableString.Invariant($"[nage] se hisse {(lad != null ? "par « " + lad.Name + " »" : "par le flanc")} : {c.Rungs.Count} prise(s), {top - c.Water:F1} m de franc-bord, flanc à {c.Outs[0]:F2}-{c.Outs[^1]:F2} m de l'axe, pont à {c.Deck:F2}, {c.Done:F1} s"));
    }

    /// <summary>Où est l'œil, et où regarde-t-il, à ce temps de la montée (repère du navire).</summary>
    (Vec3d Eye, double Pitch) ClimbEye(SwimClimb c, double t)
    {
        double sx = c.Sx;
        double FaceX(int k) => sx * (c.Outs[k] + ClimbFace);
        double EyeAt(int k) => Math.Max(c.Water + Swimmer.EyeAbove, c.Rungs[k] - 0.28);
        if (t < c.Approach)
        {
            double u = MathX.Smooth01(t / c.Approach);
            var to = new Vec3d(FaceX(0), c.Water + Swimmer.EyeAbove, c.Z);
            return (c.EyeFrom + (to - c.EyeFrom) * u, 0.15 + 0.2 * u);
        }
        t -= c.Approach;
        int n = c.Rungs.Count;
        if (t < n * ClimbStep)
        {
            int k = (int)(t / ClimbStep);
            double inStep = t - k * ClimbStep;
            double pull = MathX.Smooth01((inStep - ClimbStep * ClimbReachPart) / (ClimbStep * (1 - ClimbReachPart)));
            double y0 = k == 0 ? c.Water + Swimmer.EyeAbove : EyeAt(k - 1);
            double x0 = k == 0 ? FaceX(0) : FaceX(k - 1);
            return (new Vec3d(x0 + (FaceX(k) - x0) * pull, y0 + (EyeAt(k) - y0) * pull, c.Z), 0.45);
        }
        t -= n * ClimbStep;
        if (n > 1) t -= ClimbMatch;
        double top = EyeAt(n - 1), rail = c.Rungs[n - 1], edge = c.Outs[n - 1];
        if (t < 0) return (new Vec3d(FaceX(n - 1), top, c.Z), 0.45);
        if (t < ClimbHeave)
        {
            // la poitrine au-dessus du plat-bord, bras tendus en appui
            double u = MathX.Smooth01(t / ClimbHeave);
            return (new Vec3d(sx * (edge + ClimbFace * (1 - u) - 0.05 * u), top + (rail + 0.35 - top) * u, c.Z), 0.45 - 0.85 * u);
        }
        t -= ClimbHeave;
        {
            // par-dessus : une jambe passe, on se relève sur le pont, le regard revient à l'horizon
            double u = MathX.Smooth01(t / ClimbOver);
            double y1 = Math.Max(c.Deck + StandEye, rail + 0.35);
            return (new Vec3d(sx * (edge - 0.05 - 0.7 * u), rail + 0.35 + (y1 - rail - 0.35) * u, c.Z), -0.4 + 0.3 * u);
        }
    }

    void ClimbTick(double dt)
    {
        if (_climb == null || _swim == null) return;
        var c = _climb;
        c.T += dt;
        if (c.T >= c.Done)
        {
            // la montée finie, l'œil reste où elle l'a mis : sur le pont, à bord de ce navire
            var (end, endPitch) = ClimbEye(c, c.Done);
            _climb = null;
            SwimBoarded();
            EnterAboard(end, c.Sx, endPitch);
            return;
        }
        var b = _ship.Physics.Body;
        var o = _sea.Core.Origin;
        var (eye, pitch) = ClimbEye(c, c.T);
        var w = b.Pos + b.Quat.Rotate(eye);
        _swim.Pos = new Vec3d(w.X + o.X, w.Y, w.Z + o.Z);
        _swim.Vel = new Vec3d(0, 0, 0);
        _swim.Stroke = 99;
        // le regard va au bordé, d'où que l'on vienne
        var face = b.Quat.Rotate(new Vec3d(-c.Sx, 0, 0));
        double yaw = Math.Atan2(face.X, face.Z);
        double dy = Math.IEEERemainder(yaw - _swim.Yaw, Math.PI * 2);
        double k = Math.Min(1, dt * 5);
        _swim.Yaw += dy * k;
        _swim.Pitch += (pitch - _swim.Pitch) * k;
    }
}
