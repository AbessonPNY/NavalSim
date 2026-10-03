using Godot;
using System;
using NavalSim.Core;

namespace NavalSim;

/// <summary>
/// LA CHALOUPE — portage de la manœuvre de la page.
///
/// UNE CHALOUPE EST UN NAVIRE, et c'est tout ce qui rend ceci court. Sa fiche
/// (<c>ships/chaloupe.json</c>) est une fiche comme les autres, le navire qui la
/// porte la nomme (<c>"boat": "chaloupe"</c>), elle est mise à l'eau par le même
/// <see cref="SpawnFleet"/> que les conserves, et l'on en prend la barre par un
/// simple échange de références. Rien de ce qui la fait flotter, talonner,
/// nager à l'aviron ou repêcher une bouteille n'a été écrit pour elle.
///
/// LE NAVIRE MÈRE MOUILLE : toile ferlée, machine stoppée, et l'ancre tombe s'il
/// n'est ni à quai ni déjà dessus. Il reste où on l'a laissé ; on revient le long
/// de son bord pour hisser la chaloupe, ce qui rend la barre. L'ancre, elle,
/// reste au fond : on appareille avec M.
///
/// POURQUOI C'EST ARRIVÉ MAINTENANT : l'îlot aux cocotiers a un platier à
/// quatre-vingt-huit centimètres, où seule la chaloupe passe. Sans elle, il était
/// simplement inaccessible — un lieu qu'on voit et qu'on ne peut pas atteindre,
/// ce qui est pire que pas de lieu du tout.
/// </summary>
public partial class ShipDemo : Node3D
{
    ShipNode? _mother;                 // le navire quitté, quand on mène la chaloupe
    AutoHelm? _motherHelm;             // sa barre, mise au repos le temps de l'absence
    bool _boatBusy;

    /// <summary>
    /// ÉCHANGER LA BARRE avec une coque de la flotte. Tout le jeu lit
    /// <c>_ship</c> à chaque image et n'en garde jamais de copie — trois cent
    /// soixante-deux lectures, toutes par le champ —, si bien qu'un échange de
    /// références suffit : la caméra, les instruments, les pièces et les sondes
    /// suivent d'eux-mêmes. Les profils de coque se réécrivent à l'image
    /// suivante, la flotte étant reconstruite à chaque tour.
    /// </summary>
    void TakeHelm(ShipNode s)
    {
        if (s == _ship) return;
        int i = _others.IndexOf(s);
        if (i < 0) return;
        _others[i] = _ship;
        _ship = s;
        _gunSide = 0;
        _reef = 0;
    }

    /// <summary>Mettre la chaloupe à l'eau, ou la hisser. Rend ce qu'on en dit.</summary>
    string BoatSwing()
    {
        if (_boatBusy) return "On affale déjà la chaloupe";
        return _mother != null ? Hoist() : Lower();
    }

    string Hoist()
    {
        var m = _mother!;
        if (m.Physics.Foundered) return "Plus de navire où hisser la chaloupe";
        var b = _ship.Physics.Body;
        double d = Math.Sqrt((b.Pos.X - m.Physics.Body.Pos.X) * (b.Pos.X - m.Physics.Body.Pos.X)
                           + (b.Pos.Z - m.Physics.Body.Pos.Z) * (b.Pos.Z - m.Physics.Body.Pos.Z));
        if (d > m.Spec.L * 0.55 + m.Spec.B + 6)
            return _ship.Physics.Aground > 0.02 || _shove != null ? Shove() : "Trop loin du navire pour crocher les palans";
        if (b.Vel.LengthXZ > 1.2) return "Trop d'erre pour crocher les palans";

        var boat = _ship;
        TakeHelm(m);
        RemoveShip(boat);
        _mother = null;
        // et la revoilà sur ses chantiers
        m.ShowDeckBoat(true);
        /* SA BARRE LUI EST RENDUE — laissée en place pendant l'absence, elle
           chassait le navire à la barre, c'est-à-dire la chaloupe. */
        if (_motherHelm != null) { _helms[m] = _motherHelm; _motherHelm = null; }
        JournalLog("Chaloupe hissée à bord.");
        return "Chaloupe hissée à bord"
             + (_anchor2 != null && _anchor2.IsDown(m) ? " · M pour lever l'ancre" : "");
    }

    /* LA POUSSER À L'EAU. Échouée sur une grève, une chaloupe ne repart pas à
       l'aviron — la pelle ne trouve que le sable. Les nageurs sautent dedans
       l'eau aux genoux, la pivotent l'étrave au large et la poussent jusqu'à ce
       qu'elle flotte, puis embarquent par l'arrière. C'est ce que fait N quand
       on est loin du navire et qu'elle touche : on cherche l'eau qui la porte au
       plus près, et on l'y mène au pas d'un homme qui pousse. */
    (double Dx, double Dz, double Left)? _shove;

    const double ShoveSpeed = 0.9;       // m/s : des hommes dans l'eau jusqu'aux cuisses
    const double ShoveTurn = 0.45;       // rad/s : le temps de la faire pivoter à bras

    string Shove()
    {
        if (_shove != null) { _shove = null; return "On cesse de pousser"; }
        var b = _ship.Physics.Body; var o = _sea.Core.Origin;
        // assez d'eau pour sa quille et une main en dessous
        double need = -(_ship.Spec.Hull.KeelDepth + _ship.Spec.Hull.KeelExtra) - 0.4;
        double best = double.MaxValue, bx = 0, bz = 0;
        for (int k = 0; k < 32; k++)
        {
            double a = k * Math.PI * 2 / 32, cx = Math.Sin(a), cz = Math.Cos(a);
            for (double r = 1; r <= 80 && r < best; r += 1)
                if (_world!.HeightAt(o.X + b.Pos.X + cx * r, o.Z + b.Pos.Z + cz * r) < need)
                { best = r; bx = cx; bz = cz; break; }
        }
        if (best == double.MaxValue) return "Pas d'eau qui la porte à portée de bras";
        // jusqu'à ce que sa coque entière y soit : sa demi-longueur en plus
        _shove = (bx, bz, best + _ship.Spec.L * 0.5 + 1);
        var c = _ship.Ctrl;
        c.Throttle = 0; c.Rudder = 0; c.OarL = 0; c.OarR = 0;
        return "Les nageurs la poussent à l'eau · N pour cesser";
    }

    /// <summary>Après les solveurs : la pousser d'un pas, l'étrave tournée au large.</summary>
    void ShoveTick(double dt)
    {
        BeachTestTick();
        if (_shove is not { } sh) return;
        var (dx, dz, left) = sh;
        var b = _ship.Physics.Body; var o = _sea.Core.Origin;
        // elle flotte quand plus rien d'elle ne touche et que l'eau sous elle porte sa quille
        bool afloat = _ship.Physics.Aground <= 0
            && _world!.HeightAt(o.X + b.Pos.X, o.Z + b.Pos.Z) < -(_ship.Spec.Hull.KeelDepth + _ship.Spec.Hull.KeelExtra) - 0.4;
        if (_mother == null || left <= 0 || afloat)
        {
            // la dernière poussée, qu'elle garde en erre
            if (_mother != null) b.Vel = new Vec3d(dx * ShoveSpeed * 0.6, b.Vel.Y, dz * ShoveSpeed * 0.6);
            if (_mother != null) { Say("Chaloupe à flot · Q E nager"); JournalLog("Chaloupe remise à l'eau."); }
            if (_beachLog >= 0) { _ship.Ctrl.Throttle = 1; GD.Print("[grève] à flot, on nage"); }
            _shove = null;
            return;
        }
        double step = Math.Min(left, ShoveSpeed * dt);
        /* MENÉE À LA MAIN, SANS ERRE PROPRE : lui donner aussi la vitesse la ferait
           avancer deux fois, une par les bras et une par le solveur. */
        b.Pos = new Vec3d(b.Pos.X + dx * step, b.Pos.Y, b.Pos.Z + dz * step);
        b.Vel = new Vec3d(0, b.Vel.Y, 0);
        // le cap se lit sur le vecteur d'étrave ; on le tourne autour de la verticale
        var fw = b.Quat.Rotate(new Vec3d(0, 0, 1));
        double err = Math.Atan2(fw.X * dz - fw.Z * dx, fw.X * dx + fw.Z * dz);
        double turn = Math.Clamp(-err, -ShoveTurn * dt, ShoveTurn * dt);
        if (Math.Abs(turn) > 1e-6) b.Quat = (Quatd.FromAxisAngle(new Vec3d(0, 1, 0), turn) * b.Quat).Normalized();
        b.AngVel = new Vec3d(b.AngVel.X, 0, b.AngVel.Z);
        _shove = (dx, dz, left - step);
    }

    // --- l'essai : --echouer <secondes avant de pousser> ---
    double _beachTest, _beachLog = -1, _beachShoveAt;

    void BeachTest()
    {
        if (_mother == null) GD.Print("[grève] " + Lower());
        var b = _ship.Physics.Body; var o = _sea.Core.Origin;
        // la grève la plus proche, un mètre au-dessus de l'eau
        for (double r = 10; r < 1500; r += 5)
            for (int k = 0; k < 48; k++)
            {
                double a = k * Math.PI * 2 / 48, x = b.Pos.X + Math.Sin(a) * r, z = b.Pos.Z + Math.Cos(a) * r;
                double h = _world!.HeightAt(o.X + x, o.Z + z);
                if (h < 0.8 || h > 1.3) continue;
                b.Pos = new Vec3d(x, h + 0.6, z); b.Vel = new Vec3d(0, 0, 0); b.AngVel = new Vec3d(0, 0, 0);
                b.Quat = (Quatd.FromAxisAngle(new Vec3d(0, 1, 0), 1.9) * b.Quat).Normalized();   // de travers à la grève
                _ship.SyncTransform();
                _fixEye = new Vector3((float)(x + 9), (float)(h + 5), (float)(z + 9));
                _fixLook = new Vector3((float)x, (float)h, (float)z);
                _ship.Ctrl.Throttle = 1;
                _beachLog = 0; _beachShoveAt = _t + _beachTest;
                GD.Print(FormattableString.Invariant($"[grève] posée en ({x:F0}, {z:F0}), sol {h:F2} m"));
                return;
            }
        GD.Print("[grève] pas de grève à portée");
    }

    void BeachTestTick()
    {
        if (_beachLog < 0) return;
        if (_beachShoveAt > 0 && _t >= _beachShoveAt) { _beachShoveAt = 0; GD.Print("[grève] N : " + BoatSwing()); }
        // l'œil d'essai la suit, de trois quarts arrière
        { var bp = _ship.Physics.Body.Pos; _fixEye = new Vector3((float)bp.X + 7, 3.5f, (float)bp.Z - 8); _fixLook = new Vector3((float)bp.X, 0.3f, (float)bp.Z); }
        if (_t < _beachLog) return;
        _beachLog = _t + 2;
        var p = _ship.Physics; var b = p.Body; var o = _sea.Core.Origin;
        GD.Print(FormattableString.Invariant(
            $"[grève] t {_t:F0}  cap {Math.Atan2(b.Quat.Rotate(new Vec3d(0, 0, 1)).X, b.Quat.Rotate(new Vec3d(0, 0, 1)).Z) * 180 / Math.PI:F0}°  ({b.Pos.X:F1}, {b.Pos.Z:F1})  sol {_world!.HeightAt(o.X + b.Pos.X, o.Z + b.Pos.Z):F2}  touche {p.Aground:F2}  pelles {p.OarInput[0]:F1}/{p.OarInput[1]:F1}  vitesse {b.Vel.LengthXZ:F2}"));
    }

    string Lower()
    {
        if (_ship.Spec.Boat.Length == 0) return "Ce navire ne porte pas de chaloupe";
        var b0 = _ship.Physics.Body;
        if (b0.Vel.LengthXZ > 1.5)
            return "Trop d'erre pour mettre la chaloupe à l'eau";
        if (_fleet.Count >= Config.MaxShips) return "Plus de place à flot pour la chaloupe";
        int idx = _paths.FindIndex(p => System.IO.Path.GetFileNameWithoutExtension(p) == _ship.Spec.Boat
                                     || System.IO.Path.GetFileNameWithoutExtension(p).Contains(_ship.Spec.Boat));
        if (idx < 0) return $"Chaloupe introuvable : {_ship.Spec.Boat}";

        _boatBusy = true;
        try
        {
            int before = _others.Count;
            SpawnFleet(1, idx, arm: false);
            if (_others.Count <= before) return "Plus de place à flot pour la chaloupe";
            var boat = _others[^1];
            var mother = _ship;

            /* PAR LE TRAVERS BÂBORD, au pied de sa muraille : l'ancre pend au
               bossoir de tribord, et la chaloupe ne doit pas tomber dessus. À
               quai, le bord du LARGE — le plus loin de ses bittes —, sans quoi on
               l'affalerait sur le ponton. Et À LA HAUTEUR DE SES CHANTIERS : elle descend
               d'où elle était posée sur le pont (ShipNode.DeckBoatZ), qu'on ne montre plus. */
            Vec3d Side(double sg) => b0.Quat.Rotate(
                new Vec3d(sg * (mother.Spec.B * 0.5 + boat.Spec.B * 0.5 + 1.5), 0, mother.DeckBoatZ ?? -mother.Spec.L * 0.08));
            var off = Side(1);
            if (mother.Physics.Moorings.Count > 0)
            {
                var o = _sea.Core.Origin;
                double Far(Vec3d v)
                {
                    double best = double.MaxValue;
                    foreach (var l in mother.Physics.Moorings)
                        best = Math.Min(best, Math.Sqrt(
                            (b0.Pos.X + v.X + o.X - l.Wx) * (b0.Pos.X + v.X + o.X - l.Wx)
                          + (b0.Pos.Z + v.Z + o.Z - l.Wz) * (b0.Pos.Z + v.Z + o.Z - l.Wz)));
                    return best;
                }
                var autre = Side(-1);
                if (Far(autre) > Far(off)) off = autre;
            }
            var bb = boat.Physics.Body;
            bb.Pos = new Vec3d(b0.Pos.X + off.X, bb.Pos.Y, b0.Pos.Z + off.Z);
            bb.Quat = b0.Quat;
            bb.Vel = new Vec3d(b0.Vel.X, 0, b0.Vel.Z);
            boat.SyncTransform();
            boat.Ctrl.SailsSet = false;
            boat.Ctrl.Sheet = 0;
            _helms.Remove(boat);                       // c'est vous qui la menez

            bool aQuai = mother.Physics.Moorings.Count > 0;
            bool mouille = !aQuai && _anchor2 != null && !_anchor2.IsDown(mother);
            if (mouille) _anchor2!.Toggle(mother, _t);

            TakeHelm(boat);
            _mother = mother;
            // elle a quitté ses chantiers : le pont n'en montre plus
            mother.ShowDeckBoat(false);
            /* CELUI QU'ON QUITTE NE GARDE PAS SES ORDRES : il attend. Sa barre de
               réserve est mise de côté — laissée là, elle chassait le navire à la
               barre, c'est-à-dire la chaloupe, ancre ou pas. */
            if (_helms.TryGetValue(mother, out var h)) { _motherHelm = h; _helms.Remove(mother); }
            mother.Ctrl.SailsSet = false;
            mother.Ctrl.Throttle = 0;
            mother.Ctrl.Rudder = 0;

            JournalLog("Chaloupe à l'eau.");
            return "Chaloupe à l'eau" + (mouille ? ", le navire mouille" : "")
                 + " · Q E nager, A D scier, W S les deux bords";
        }
        finally { _boatBusy = false; }
    }
}
