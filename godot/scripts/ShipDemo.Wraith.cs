using Godot;
using System;
using NavalSim.Core;

namespace NavalSim;

/// <summary>
/// LE VAISSEAU FANTÔME QUI RÔDE — la rencontre, par opposition au LIEU qu'est le
/// Cimetière des Galions.
///
/// Une nuit de brume, au large de toute côte, une voile pâle paraît. Elle ne
/// porte rien : pas de vent dans sa toile, pas de gîte, une erre constante de
/// huit à dix nœuds. **Elle vient à la lumière** — et c'est toute la règle du
/// jeu : feux couverts (⇧L), elle garde son cap et passe ; feux allumés, elle
/// vient. Rien ne l'arrête, rien ne la blesse ; il n'y a qu'à n'être pas vu.
///
/// La coque est empruntée à la flotte comme celles de la bataille fantôme —
/// <c>Ghostify</c> la pâlit —, mais elle est GLISSÉE et non simulée : le solveur
/// ne la touche pas (<c>IsGhost</c>), sa place est écrite à chaque image par la
/// règle du noyau, et sa flottaison est tenue par <c>Physics.Glide</c>, si bien
/// que la houle passe au travers d'elle au lieu de la faire rouler.
/// </summary>
public partial class ShipDemo : Node3D
{
    WraithRules _wraithRules = new();
    Wraith? _wraith;
    ShipNode? _wraithShip;

    void BuildWraith()
    {
        _wraith = new Wraith(_wraithRules) { Say = Say };
    }

    void WraithTick(double dt, double hours)
    {
        if (_wraith == null || _world == null) return;
        var b = _ship.Physics.Body;
        var o = _sea.Core.Origin;
        bool avant = _wraith.State != Wraith.Mood.Away;

        _wraith.Step(dt, hours, b.Pos.X, b.Pos.Z,
            _ship.Lit && !_ship.Dark,
            _sky.Core.Night, _sky.Core.Fog,
            _world.ShoreDistance(o.X + b.Pos.X, o.Z + b.Pos.Z));

        bool apres = _wraith.State != Wraith.Mood.Away;
        /* SUR L'ABSENCE DE COQUE, ET NON SUR LA BASCULE D'ÉTAT. On guettait le
           passage de Away à Abroad, ce qui ne valait que s'il venait de lui-même :
           appelé à la touche, il paraissait AVANT cette image, la bascule était
           déjà faite et aucune coque n'était mise à l'eau — on le voyait errer
           dans le journal sans jamais rien voir sur la mer. L'absence de coque dit
           la vérité ; la bascule n'en donnait qu'un indice. */
        if (apres && _wraithShip == null) RaiseWraith();
        if (!apres && avant) { if (_wraithShip != null) RemoveShip(_wraithShip); _wraithShip = null; }
        if (_wraithShip == null) return;

        /* SA PLACE EST ÉCRITE, pas calculée : il flotte. On lui pose son cap et sa
           flottaison, et le solveur ne le voit pas passer. */
        var wb = _wraithShip.Physics.Body;
        double y = _wraithShip.Physics.Glide ?? wb.Pos.Y;
        wb.Pos = new Vec3d(_wraith.Pos.X, y, _wraith.Pos.Z);
        wb.Quat = Quatd.FromAxisAngle(new Vec3d(0, 1, 0), _wraith.Heading);
        wb.Vel = Vec3d.Zero;
        wb.AngVel = Vec3d.Zero;
        _wraithShip.SyncTransform();
        _wraithShip.GhostShow(_wraith.Fade, _t, dt, 0);

        if (_wraith.Passed && !_wraithLogged)
        {
            _wraithLogged = true;
            JournalLog("Un vaisseau sans équipage a passé à toucher le bord, dans la brume.");
        }
    }
    bool _wraithLogged;

    /// <summary>
    /// ⇧U — LE FAIRE VENIR TOUT DE SUITE, comme K fait venir le kraken. Et comme
    /// lui, on lui amène d'abord ce dont il a besoin : il ne paraît que la nuit
    /// et dans la brume, et une voile pâle en plein midi sur une mer claire ne
    /// serait qu'une coque de plus. L'HEURE, pas le soleil — le cycle du jour
    /// réécrit le soleil depuis l'heure à chaque image.
    ///
    /// Le large, lui, ne s'amène pas : on ne va pas déplacer le navire pour un
    /// essai. Il est donc appelé de FORCE, ce qui le dispense des conditions le
    /// temps de sa ronde ; à deux milles d'une côte il viendra tout de même, ce
    /// qui n'arriverait jamais de lui-même.
    /// </summary>
    void SummonWraith()
    {
        if (_wraith == null) return;
        if (_sky.Core.Night < _wraithRules.NightMin) { _sky.Core.SetTimeOfDay(22, _sky.Latitude); _sky.Apply(); }
        if (_sky.Core.Fog < _wraithRules.FogMin) (_seaFog ??= new SeaFog(_fogRules)).Force(4);
        var b = _ship.Physics.Body;
        _wraith.Summon(b.Pos.X, b.Pos.Z, force: true);
    }

    void RaiseWraith()
    {
        int idx = _paths.FindIndex(p => System.IO.Path.GetFileNameWithoutExtension(p) == _wraithRules.Hull);
        if (idx < 0) { GD.PushWarning($"vaisseau fantôme : ships/{_wraithRules.Hull}.json introuvable"); return; }
        int before = _others.Count;
        SpawnFleet(1, idx, arm: false);
        if (_others.Count == before) return;
        var s = _others[^1];
        _helms.Remove(s);                       // il ne se gouverne pas : il dérive
        s.Ctrl.Throttle = 0;
        /* IL PORTE TOUTE SA TOILE, ET ELLE NE PORTE RIEN.
         *
         * Il naviguait à sec de toile, ce qui était le raisonnement juste et le
         * spectacle faux : on lisait un navire aux voiles SERRÉES, c'est-à-dire un
         * navire ordinaire au mouillage, et rien de plus (signalé — « ça doit être
         * un spectre comme l'un de ceux de la bataille d'un autre temps »).
         *
         * Un vaisseau fantôme porte au contraire tout ce qu'il a, et c'est
         * justement l'écart qui glace : de la toile pleine, en loques, et pas un
         * degré de gîte, pas une lame à l'étrave, une erre égale — ce qu'aucun
         * navire ne peut faire. La toile ne lui donne pas un nœud puisqu'il est
         * GLISSÉ et que le solveur ne le touche pas ; elle n'est là que pour l'œil.
         *
         * Ghostify la déchire par ailleurs, une carte de plaies tirée par voile :
         * ce sont les mêmes loques que celles de la bataille du Cimetière. */
        s.Ctrl.SailsSet = true;
        s.Ctrl.Canvas = 1;
        s.ShowColours(false);                   // pas de pavillon : on ne sait pas d'où il vient
        s.Ghostify(_ghosts.Rules.Opacity, _gunFx.Smoke);
        if (_hulls.TryGetValue(s, out var h)) s.Physics.Glide = h.Y + 0.25;
        _wraithShip = s;
        _wraithLogged = false;
        JournalLog("Une voile pâle dans la brume, au large.");
    }
}
