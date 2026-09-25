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
        if (d > m.Spec.L * 0.55 + m.Spec.B + 6) return "Trop loin du navire pour crocher les palans";
        if (Math.Sqrt(b.Vel.X * b.Vel.X + b.Vel.Z * b.Vel.Z) > 1.2) return "Trop d'erre pour crocher les palans";

        var boat = _ship;
        TakeHelm(m);
        RemoveShip(boat);
        _mother = null;
        /* SA BARRE LUI EST RENDUE — laissée en place pendant l'absence, elle
           chassait le navire à la barre, c'est-à-dire la chaloupe. */
        if (_motherHelm != null) { _helms[m] = _motherHelm; _motherHelm = null; }
        JournalLog("Chaloupe hissée à bord.");
        return "Chaloupe hissée à bord"
             + (_anchor2 != null && _anchor2.IsDown(m) ? " · M pour lever l'ancre" : "");
    }

    string Lower()
    {
        if (_ship.Spec.Boat.Length == 0) return "Ce navire ne porte pas de chaloupe";
        var b0 = _ship.Physics.Body;
        if (Math.Sqrt(b0.Vel.X * b0.Vel.X + b0.Vel.Z * b0.Vel.Z) > 1.5)
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
               l'affalerait sur le ponton. */
            Vec3d Side(double sg) => b0.Quat.Rotate(
                new Vec3d(sg * (mother.Spec.B * 0.5 + boat.Spec.B * 0.5 + 1.5), 0, -mother.Spec.L * 0.08));
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
