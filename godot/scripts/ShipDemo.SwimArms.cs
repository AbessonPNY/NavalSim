using Godot;
using System;
using System.Collections.Generic;
using NavalSim.Core;

namespace NavalSim;

/// <summary>
/// LES BRAS DU NAGEUR — à la première personne, on voit ses mains (demandé) : la
/// brasse, la main qui va prendre le lambi, les deux mains qui agrippent le flanc
/// et vous hissent à bord.
///
/// Le modèle est <c>creatures/bras.glb</c> (tools/arms-glb.js, à refaire dans
/// Blender si l'on veut) : par bras trois pièces sans armature — bras, avant-bras,
/// main ouverte ou fermée — chacune son origine à l'articulation. Le jeu ne lit
/// AUCUNE animation : il décide où va chaque MAIN (poignet, sens des doigts, sens
/// de la paume), et l'épaule et le coude se résolvent seuls, deux os, le coude
/// tourné vers un pôle. Les mains suivent leur but par un amorti, ce qui lie les
/// gestes entre eux sans courbes à écrire.
///
/// Tout est dans le repère de la CAMÉRA (−Z devant, +Y en haut, +X à droite), le
/// modèle étant son enfant : il suit l'œil, la secousse comprise. Ce qui tient au
/// monde (le flanc, la chose prise) y est ramené à chaque image.
/// </summary>
public partial class ShipDemo
{
    const float ArmUpper = 0.31f, ArmFore = 0.26f;
    /// <summary>L'épaule droite sous l'œil : un nageur à plat ventre lève la tête, les épaules sont dessous et un peu derrière.</summary>
    static readonly Vector3 ArmShoulder = new(0.19f, -0.21f, 0.10f);
    /// <summary>Ce que serre la main fermée, dans son repère (tools/arms-glb.js).</summary>
    static readonly Vector3 ArmGrip = new(0, 0.075f, 0.03f);

    sealed class SwimArm
    {
        public MeshInstance3D? Upper, Fore, Open, Fist;
        public int Side;               // +1 la droite, −1 la gauche (sur l'écran)
        public Vector3 Wrist, Fingers, Palm, Pole;
        public bool Grip, Init;
    }

    /// <summary>Où une main veut aller, dans le repère de la caméra.</summary>
    struct HandGoal
    {
        public Vector3 Wrist, Fingers, Palm, Pole;
        public bool Grip;
        public HandGoal(Vector3 w, Vector3 f, Vector3 p, Vector3 pole, bool grip = false)
        { Wrist = w; Fingers = f; Palm = p; Pole = pole; Grip = grip; }
        /// <summary>La même, pour l'autre main : le miroir en X.</summary>
        public readonly HandGoal Mirror() => new(M(Wrist), M(Fingers), M(Palm), M(Pole), Grip);
        static Vector3 M(Vector3 v) => new(-v.X, v.Y, v.Z);
        public static HandGoal Lerp(in HandGoal a, in HandGoal b, float t) => new(
            a.Wrist.Lerp(b.Wrist, t), a.Fingers.Lerp(b.Fingers, t), a.Palm.Lerp(b.Palm, t), a.Pole.Lerp(b.Pole, t), t < 0.5f ? a.Grip : b.Grip);
    }

    Node3D? _arms;
    bool _armsTried;
    /// <summary>Ce que les bras occupent devant l'œil, pour le flou de mouvement (repère de la caméra).</summary>
    static readonly Aabb ArmsBox = new(new Vector3(-0.7f, -0.9f, -1.1f), new Vector3(1.4f, 1.25f, 1.4f));
    readonly SwimArm _armR = new() { Side = 1 }, _armL = new() { Side = -1 };

    /* LA BRASSE, vue de l'intérieur (main droite ; la gauche en miroir). Les temps
       sont ceux du coup de Swimmer : la traction (Swimmer.Pull, 0,3 s) est l'écart
       des mains puis leur retour sous la poitrine, c'est elle qui propulse ; puis
       les mains filent devant, et l'on glisse bras tendus jusqu'au coup suivant. */
    static readonly HandGoal StrokeGlide = new(new(0.09f, -0.16f, -0.47f), new(0.06f, 0.06f, -1), new(-0.15f, -1, 0), new(1, -0.3f, 0.3f));
    static readonly HandGoal StrokeOut = new(new(0.36f, -0.22f, -0.32f), new(0.55f, -0.05f, -0.8f), new(0.75f, -0.25f, 0.6f), new(1, -0.2f, 0.4f));
    static readonly HandGoal StrokeIn = new(new(0.08f, -0.36f, -0.17f), new(-0.25f, 0.25f, -0.9f), new(-0.6f, 0.2f, 0.75f), new(1, -0.6f, 0));
    static readonly HandGoal StrokeShoot = new(new(0.05f, -0.24f, -0.30f), new(0, 0.25f, -1), new(-1, -0.2f, 0), new(1, -0.5f, 0.2f));
    static readonly (float T, HandGoal G)[] StrokeKeys =
    {
        (0f, StrokeGlide), (0.16f, StrokeOut), ((float)Swimmer.Pull, StrokeIn), (0.42f, StrokeShoot), (0.58f, StrokeGlide)
    };

    void ArmsLoad()
    {
        if (_armsTried) return;
        _armsTried = true;
        string path = Assets.Path("creatures/bras.glb");
        var root = System.IO.File.Exists(path) ? Assets.LoadGlb(path) : null;
        if (root == null) { GD.PushWarning("[nage] creatures/bras.glb introuvable — node tools/arms-glb.js"); return; }
        root.Name = "Bras";
        foreach (var mi in NodeWalk.Meshes(root))
        {
            // si près de l'œil, leur ombre tomberait sur tout le champ ; et le soleil passe à travers une main
            mi.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
            mi.ExtraCullMargin = 1;
            string n = mi.Name.ToString();
            var arm = n.EndsWith("_g") ? _armL : _armR;
            if (n.StartsWith("avant_bras")) arm.Fore = mi;
            else if (n.StartsWith("bras")) arm.Upper = mi;
            else if (n.StartsWith("poing")) arm.Fist = mi;
            else if (n.StartsWith("main")) arm.Open = mi;
        }
        _cam.AddChild(root);
        _arms = root;
        _arms.Visible = false;
    }

    /// <summary>Chaque image, une fois l'œil posé : où vont les mains, puis les bras qui les y portent.</summary>
    void ArmsPose(double dt)
    {
        if (_arms == null && !_swimming) return;     // chargé à la première plongée seulement
        ArmsLoad();
        if (_arms == null) return;
        bool show = _swimming && _swim != null && _camMode == CamSwim && _fixEye == null;
        _arms.Visible = show;
        if (!show) { _armR.Init = _armL.Init = false; return; }
        var s = _swim!;

        HandGoal right, left;
        float rate = 16;
        if (_climb != null) { ClimbHands(out right, out left); rate = 22; }
        else if (s.Blackout || _swimRescue)
        {
            // les bras lâchent, et tombent hors de la vue
            right = new HandGoal(new(0.24f, -0.62f, -0.12f), new(0, -1, -0.2f), new(-1, 0, 0), new(1, 0, 0.5f));
            left = right.Mirror();
            rate = 3;
        }
        else
        {
            right = s.Stroke < 90 ? StrokeGoal(s.Stroke) : ScullGoal(1);
            left = s.Stroke < 90 ? right.Mirror() : ScullGoal(-1);
            if (_grab != null) right = GrabGoal(_grab, right);
        }
        ArmFollow(_armR, right, rate, dt);
        ArmFollow(_armL, left, rate, dt);
        ArmSolve(_armR);
        ArmSolve(_armL);
    }

    /// <summary>Le coup de brasse à ce temps-là : entre deux clés, lissé.</summary>
    static HandGoal StrokeGoal(double t)
    {
        for (int i = 0; i < StrokeKeys.Length - 1; i++)
        {
            var (t0, g0) = StrokeKeys[i];
            var (t1, g1) = StrokeKeys[i + 1];
            if (t < t1)
            {
                float u = Mathf.Clamp((float)(t - t0) / (t1 - t0), 0, 1);
                return HandGoal.Lerp(g0, g1, u * u * (3 - 2 * u));
            }
        }
        return StrokeGlide;
    }

    /// <summary>À l'arrêt, les mains godillent devant la poitrine : un huit lent, paume qui bascule à chaque aller.</summary>
    HandGoal ScullGoal(int side)
    {
        float ph = (float)(_t * Math.PI * 2 * 0.8) + (side > 0 ? 0 : 0.9f);
        float c = Mathf.Cos(ph), sn = Mathf.Sin(ph);
        var g = new HandGoal(
            new(0.21f + 0.06f * sn, -0.27f + 0.015f * c, -0.37f),
            new(0.2f, -0.12f, -1),
            new(-0.55f * c, -0.85f, 0.1f),
            new(1, -0.6f, 0.2f));
        return side > 0 ? g : g.Mirror();
    }

    /// <summary>La main suit son but, amortie : c'est ce qui lie un geste au suivant.</summary>
    static void ArmFollow(SwimArm a, in HandGoal g, float rate, double dt)
    {
        if (!a.Init)
        {
            a.Wrist = g.Wrist; a.Fingers = g.Fingers.Normalized(); a.Palm = g.Palm.Normalized(); a.Pole = g.Pole.Normalized();
            a.Init = true;
        }
        float k = 1 - Mathf.Exp(-rate * (float)dt);
        a.Wrist = a.Wrist.Lerp(g.Wrist, k);
        a.Fingers = a.Fingers.Lerp(g.Fingers.Normalized(), k).Normalized();
        a.Palm = a.Palm.Lerp(g.Palm.Normalized(), k).Normalized();
        a.Pole = a.Pole.Lerp(g.Pole.Normalized(), k).Normalized();
        a.Grip = g.Grip;
    }

    /// <summary>Un repère dont l'axe Y est donné, et Z tiré vers un souhait.</summary>
    static Basis ArmBasis(Vector3 y, Vector3 zWish)
    {
        y = y.Normalized();
        var z = zWish - y * y.Dot(zWish);
        if (z.LengthSquared() < 1e-6f) z = y.Cross(Vector3.Right).LengthSquared() > 1e-4f ? y.Cross(Vector3.Right) : y.Cross(Vector3.Forward);
        z = z.Normalized();
        return new Basis(y.Cross(z), y, z);
    }

    /// <summary>
    /// DEUX OS : de l'épaule au poignet voulu, le coude sur le cercle que les deux
    /// longueurs permettent, du côté de son pôle. Trop loin, le bras se tend vers
    /// la main sans l'atteindre — il ne s'allonge pas.
    /// </summary>
    static void ArmSolve(SwimArm a)
    {
        if (a.Upper == null || a.Fore == null) return;
        /* LE POIGNET NE CASSE PAS (signalé : « le poignet cassé vers l'avant ») : la main
           voulait ses doigts là où on les lui disait, l'avant-bras ailleurs, et sur une
           prise le poignet pliait à angle droit. Les doigts restent à WristBend au plus
           de l'axe de l'avant-bras ; ce que la main voulait serrer reste où il était, et
           le bras se résout une seconde fois vers le poignet qui en découle. */
        var grip = a.Wrist + a.Fingers * ArmGrip.Y + a.Palm * ArmGrip.Z;
        ArmReach(a, a.Wrist, out var E, out var W);
        var (f, palm) = WristLimit(a, W - E);
        ArmReach(a, grip - f * ArmGrip.Y - palm * ArmGrip.Z, out E, out W);
        (f, palm) = WristLimit(a, W - E);
        ArmPlace(a, E, W, f, palm);
    }

    /// <summary>Le pli du poignet permis, en tout sens : une main serre droit dans l'axe, ou presque.</summary>
    const float WristBend = 0.55f;

    static (Vector3 Fingers, Vector3 Palm) WristLimit(SwimArm a, Vector3 fore)
    {
        var axis = fore.Normalized();
        var f = a.Fingers;
        float ang = axis.AngleTo(f);
        if (ang > WristBend)
        {
            var n = axis.Cross(f);
            if (n.LengthSquared() < 1e-8f) n = axis.Cross(a.Palm);
            f = axis.Rotated(n.Normalized(), WristBend);
        }
        var palm = a.Palm - f * f.Dot(a.Palm);
        return (f, palm.LengthSquared() > 1e-6f ? palm.Normalized() : a.Palm);
    }

    /// <summary>L'épaule, le coude et le poignet atteint pour ce poignet voulu.</summary>
    static void ArmReach(SwimArm a, Vector3 wrist, out Vector3 E, out Vector3 W)
    {
        var S = new Vector3(ArmShoulder.X * a.Side, ArmShoulder.Y, ArmShoulder.Z);
        var d = wrist - S;
        float L = Mathf.Clamp(d.Length(), 0.08f, ArmUpper + ArmFore - 0.002f);
        var dir = d.LengthSquared() > 1e-8f ? d.Normalized() : Vector3.Forward;
        float x = (ArmUpper * ArmUpper - ArmFore * ArmFore + L * L) / (2 * L);
        float y = Mathf.Sqrt(Mathf.Max(0, ArmUpper * ArmUpper - x * x));
        var perp = a.Pole - dir * dir.Dot(a.Pole);
        perp = perp.LengthSquared() > 1e-6f ? perp.Normalized() : new Vector3(a.Side, -0.5f, 0).Normalized();
        E = S + dir * x + perp * y;
        W = S + dir * L;
    }

    static void ArmPlace(SwimArm a, Vector3 E, Vector3 W, Vector3 fingers, Vector3 palm)
    {
        var S = new Vector3(ArmShoulder.X * a.Side, ArmShoulder.Y, ArmShoulder.Z);
        // le biceps du côté du pli, donc à l'opposé du coude ; l'avant-bras tourne avec la paume
        var bend = (E - S) - (W - S).Normalized() * (E - S).Dot((W - S).Normalized());
        a.Upper!.Transform = new Transform3D(ArmBasis(E - S, bend.LengthSquared() > 1e-8f ? -bend : -a.Pole), S);
        a.Fore!.Transform = new Transform3D(ArmBasis(W - E, palm), E);
        var hand = new Transform3D(ArmBasis(fingers, palm), W);
        if (a.Open != null) { a.Open.Transform = hand; a.Open.Visible = !a.Grip || a.Fist == null; }
        if (a.Fist != null) { a.Fist.Transform = hand; a.Fist.Visible = a.Grip; }
    }

    /// <summary>Un point du monde (coordonnées Godot) dans le repère de la caméra.</summary>
    Vector3 ArmsLocal(Vector3 world) => _cam.GlobalTransform.AffineInverse() * world;
    Vector3 ArmsLocalDir(Vector3 world) => (_cam.GlobalTransform.Basis.Inverse() * world).Normalized();

    /* ------------------------------------------------------------------ */
    /* PRENDRE : la main va à la chose, la serre, la ramène vers le sac.  */
    /* ------------------------------------------------------------------ */

    sealed class SwimGrab
    {
        public Find F = null!;
        public double T, Reach;
        public bool Held;              // la main s'est fermée dessus : la chose ne repose plus au fond
        public Node3D? Copy;           // ce que la main tient, le temps de la ramener
    }
    SwimGrab? _grab;
    /// <summary>De l'œil, la distance où il faut être pour prendre une chose : le bras, à peu près tendu.</summary>
    const double GrabStandOff = 0.55;
    const double GrabHold = 0.15, GrabBack = 0.45;

    void GrabStart(Find f)
    {
        var d = new Vec3d(f.X - _swim!.Pos.X, f.Y - _swim.Pos.Y, f.Z - _swim.Pos.Z).Length;
        // plus elle est loin, plus il faut d'un coup de jambes pour aller la chercher
        _grab = new SwimGrab { F = f, Reach = 0.35 + 0.3 * Math.Max(0, d - GrabStandOff) };
    }

    /// <summary>Le corps glisse vers la chose pendant que la main y va — un bras ne fait pas 1,6 m.</summary>
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
            // ce que la main emporte : une copie du modèle, serrée dans le poing
            g.Copy = SwimModel(g.F.Kind);
            if (g.Copy != null && _armR.Fist != null)
            {
                _armR.Fist.AddChild(g.Copy);
                g.Copy.Transform = HeldPose(g.F.Kind);
            }
        }
        if (g.T >= g.Reach + GrabHold + GrabBack)
        {
            g.Copy?.QueueFree();
            _grab = null;
        }
    }

    /* CE QUE LE POING TIENT, dans le repère de la main (doigts +Y, paume +Z, pouce +X).
       Le lambi (tools/reef-glb.js) a sa spire vers +X, son dos épineux vers +Y et sa
       lèvre dessous : on le prend par le corps, en travers de la paume comme une
       bouteille — la spire sort du côté du pouce, le dos tourné vers l'extérieur,
       la lèvre contre la paume. L'huître, plate dans son XZ, se tient de même : à
       plat contre la paume. */
    static readonly Basis HeldBasis = new(new Vector3(1, 0, 0), new Vector3(0, 0, 1), new Vector3(0, -1, 0));

    Transform3D HeldPose(string kind)
    {
        var (s, mid, size) = SwimFitOf(kind);
        var b = HeldBasis * Basis.FromScale(Vector3.One * s);
        /* Son milieu devant la paume de la moitié de son épaisseur — mais pas plus loin
           que les doigts repliés ne vont (6 cm) : un lambi est plus gros qu'un poing,
           la main le tient par le flanc, elle n'en fait pas le tour. */
        float depth = Mathf.Min(0.015f + 0.5f * size.Y * s, 0.06f);
        var at = new Vector3(0, ArmGrip.Y, depth);
        return new Transform3D(b, at - b * mid);
    }

    HandGoal GrabGoal(SwimGrab g, HandGoal rest)
    {
        if (g.T >= g.Reach + GrabHold)
        {
            // vers la hanche, où pend le sac : la main sort de la vue par le bas
            return new HandGoal(new(0.24f, -0.55f, 0.02f), new(0.1f, -1, 0.3f), new(-1, 0, 0), new(1, 0, 0.5f), true);
        }
        var f = g.F;
        var o = _sea.Core.Origin;
        var item = ArmsLocal(new Vector3((float)(f.X - o.X), (float)(f.Y + 0.05), (float)(f.Z - o.Z)));
        var S = ArmShoulder;
        var fingers = (item - S).Normalized();
        // la paume vient sur la chose par-dessus, comme on cueille
        var palm = ArmsLocalDir(Vector3.Down);
        palm = (palm - fingers * fingers.Dot(palm)).Normalized();
        var wrist = item - fingers * ArmGrip.Y - palm * ArmGrip.Z;
        bool closed = g.T >= g.Reach;
        var reach = new HandGoal(wrist, fingers, palm, new(1, -0.8f, 0.2f), closed);
        // la première part du geste quitte la brasse en douceur
        float u = Mathf.Clamp((float)(g.T / Math.Max(0.1, g.Reach * 0.4)), 0, 1);
        return HandGoal.Lerp(rest, reach, u * u * (3 - 2 * u));
    }

    /* ------------------------------------------------------------------ */
    /* SE HISSER : main sur main le long du flanc, puis par-dessus le bord. */
    /* ------------------------------------------------------------------ */

    /// <summary>
    /// LA MONTÉE, dans le repère du NAVIRE (il roule et dérive pendant qu'on y
    /// grimpe : les mains restent où elles tiennent). Par l'ÉCHELLE du modèle s'il en
    /// a une (ShipNode.Ladders, un barreau tous les 35 cm) ; sinon par les prises du
    /// flanc, de la flottaison au plat-bord, tous les 42 cm — les préceintes. Une main
    /// monte, le corps se hisse, l'autre main passe au-dessus. Arrivé en haut, les deux
    /// mains s'y posent, la poitrine passe, et l'on se relève sur le pont. Le flanc à
    /// chaque prise est MESURÉ sur la coque du modèle (ShipNode.SideOut). Un dériveur
    /// n'a qu'une prise : son bord.
    /// </summary>
    sealed class SwimClimb
    {
        public double T;
        public int Sx;                 // le flanc : +1 bâbord (+x du navire), −1 tribord
        public double Z, Water, Deck;
        public readonly List<double> Rungs = new();
        /// <summary>Le dehors du flanc (ou de l'échelle) à chaque prise : où vont les mains.</summary>
        public readonly List<double> Outs = new();
        public ShipNode.Ladder? By;    // l'échelle prise, s'il y en a une
        public Vec3d EyeFrom;          // l'œil au départ, dans le repère du navire
        public double Approach;        // le temps de venir au flanc
        public double ZRight;          // le décalage le long du navire de la main droite (±)
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
        // face au bordé de bâbord (+x), la main droite est vers l'arrière ; à tribord, vers l'avant ;
        // une échelle est plus étroite qu'un flanc
        c.ZRight = -(lad != null ? 0.17 : 0.22) * c.Sx;
        c.Done = c.Approach + c.Rungs.Count * ClimbStep + (c.Rungs.Count > 1 ? ClimbMatch : 0) + ClimbHeave + ClimbOver;
        _climb = c;
        _grab?.Copy?.QueueFree();
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

    /// <summary>Les deux mains de la montée : sur leur prise, ou en chemin vers la suivante.</summary>
    void ClimbHands(out HandGoal right, out HandGoal left)
    {
        var c = _climb!;
        var b = _ship.Physics.Body;
        int n = c.Rungs.Count;
        double t = c.T - c.Approach;

        // la prise de chaque main, et d'où elle vient : la droite prend les prises paires
        int hiR, hiL; double moveR = 1, moveL = 1; int fromR, fromL;
        if (t < 0)
        {
            // en venant au flanc, les mains godillent puis se tendent vers la première prise
            var sr = ScullGoal(1); var sl = ScullGoal(-1);
            double u = MathX.Smooth01((t + 0.35) / 0.35);
            right = HandGoal.Lerp(sr, ClimbHold(c, b, 0, +1), (float)u);
            left = HandGoal.Lerp(sl, ClimbHold(c, b, 0, -1), (float)u);
            return;
        }
        int k = Math.Min(n - 1, (int)(t / ClimbStep));
        double inStep = t - k * ClimbStep;
        double reach = Math.Clamp(inStep / (ClimbStep * ClimbReachPart), 0, 1);
        if (t >= n * ClimbStep) { k = n - 1; reach = 1; }
        // la main qui monte à la prise k, et l'autre qui tient la prise k−1
        bool rightMoves = k % 2 == 0;
        int other = Math.Max(0, k - 1);
        if (rightMoves) { hiR = k; fromR = Math.Max(0, k - 2); moveR = reach; hiL = other; fromL = other; }
        else { hiL = k; fromL = Math.Max(0, k - 2); moveL = reach; hiR = other; fromR = other; }
        if (k == 0) { fromR = fromL = 0; moveR = moveL = reach; hiR = hiL = 0; }
        // la dernière prise atteinte, l'autre main vient la rejoindre sur le plat-bord
        if (n > 1 && t >= n * ClimbStep)
        {
            double m = Math.Clamp((t - n * ClimbStep) / ClimbMatch, 0, 1);
            if (rightMoves) { hiL = n - 1; fromL = n - 2; moveL = m; }
            else { hiR = n - 1; fromR = n - 2; moveR = m; }
        }
        right = ClimbMove(c, b, fromR, hiR, moveR, +1);
        left = ClimbMove(c, b, fromL, hiL, moveL, -1);
    }

    HandGoal ClimbMove(SwimClimb c, Body b, int from, int to, double u, int hand)
    {
        var a = ClimbHold(c, b, from, hand);
        if (from == to || u >= 1) return to == from ? a : ClimbHold(c, b, to, hand);
        var z = ClimbHold(c, b, to, hand);
        float e = (float)MathX.Smooth01(u);
        var g = HandGoal.Lerp(a, z, e);
        // la main quitte le bois en s'en écartant un peu, ouverte, et revient serrer plus haut
        var outw = ArmsLocalDir(b.Quat.Rotate(new Vec3d(c.Sx, 0.4, 0)).ToGodot());
        g.Wrist += outw * (0.10f * Mathf.Sin(Mathf.Pi * e));
        g.Grip = u > 0.9;
        return g;
    }

    /// <summary>Une main serrée sur la prise k : sur le flanc, ou sur le plat-bord pour la dernière.</summary>
    HandGoal ClimbHold(SwimClimb c, Body b, int k, int hand)
    {
        double sx = c.Sx;
        bool rail = k == c.Rungs.Count - 1;
        double y = c.Rungs[k];
        double x = rail ? sx * (c.Outs[k] - 0.02) : sx * (c.Outs[k] + 0.03);
        var grip = new Vec3d(x, y + 0.02, c.Z + c.ZRight * hand);
        var w = b.Pos + b.Quat.Rotate(grip);
        var gripCam = ArmsLocal(w.ToGodot());
        // les doigts passent par-dessus vers le bordé, la paume en dessous : on tient par le haut
        var fingers = ArmsLocalDir(b.Quat.Rotate(new Vec3d(-sx, rail ? -0.25 : -0.6, 0)).ToGodot());
        var palm = ArmsLocalDir(b.Quat.Rotate(new Vec3d(rail ? 0 : -sx * 0.3, -1, 0)).ToGodot());
        palm = (palm - fingers * fingers.Dot(palm)).Normalized();
        var wrist = gripCam - fingers * ArmGrip.Y - palm * ArmGrip.Z;
        var pole = new Vector3(hand, -1, 0.3f);
        return new HandGoal(wrist, fingers, palm, pole, true);
    }
}
