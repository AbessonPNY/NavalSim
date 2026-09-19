using Godot;
using System;
using System.Collections.Generic;
using NavalSim.Core;

namespace NavalSim;

/// <summary>
/// LA FLOTTE FANTÔME, prêtée à sa scène : des coques de la flotte habillées en
/// spectres. Le noyau garde la scène (GhostScene) ; ici on lui prête la flotte,
/// les mots et l'horloge — ce que ghostCtx fait dans la page.
/// </summary>
public partial class ShipDemo : IGhostHost
{
    readonly GhostScene _ghosts = new();

    /* CE QU'IL FAUT POUR RENDRE SA RANGÉE À UNE COQUE : son contour à la mer, son
       bordé contre l'embrun, et son assise, d'où l'on tire la hauteur où un
       spectre glisse. */
    readonly Dictionary<ShipNode, (HullProfile Prof, HullCollider Col, double Y)> _hulls = new();

    /// <summary>Une image de la scène, après les feux : elle lit la nuit par leur règle.</summary>
    void GhostTick(double dt)
    {
        var b = _ship.Physics.Body;
        var o = _sea.Core.Origin;
        _ghosts.Update(dt, _ship.Physics, o.X + b.Pos.X, o.Z + b.Pos.Z, _ship.Lit, o, _t, this);
        foreach (var g in _ghosts.Entries) ((ShipNode)g.Tag!).GhostShow(g.Fade, _t, dt, g.Side);
    }

    int IGhostHost.Room => Config.MaxShips - _fleet.Count;

    /* Deux nations, une par camp, tirées sans remise parmi les pavillons
       nationaux — le hasard franc de la page, pas le tirage au poids des
       rencontres. */
    readonly Nation?[] _ghostColours = new Nation?[2];

    void IGhostHost.Rising()
    {
        var pool = _nations.All.FindAll(x => !x.Pirate);
        for (int k = 0; k < 2; k++)
        {
            if (pool.Count == 0) { _ghostColours[k] = null; continue; }
            int i = (int)(_ghostRng.NextDouble() * pool.Count);
            _ghostColours[k] = pool[i];
            pool.RemoveAt(i);
        }
    }
    readonly Random _ghostRng = new();

    GhostHull? IGhostHost.Launch(string id, int side)
    {
        int idx = _paths.FindIndex(p => System.IO.Path.GetFileNameWithoutExtension(p) == id);
        if (idx < 0) { GD.PushWarning($"ships/{id}.json introuvable"); return null; }
        int before = _others.Count;
        // sans humeur de pirate : un spectre se bat pour son camp, pas pour le butin
        SpawnFleet(1, idx, arm: false);
        if (_others.Count == before) return null;
        var s = _others[^1];
        // une barre qui vient canonner se tient au plein fouet, pas à portée d'abordage
        var h = HelmOf(s);
        h.Standoff = Math.Max(h.Standoff, 185);
        TargetOf(s);                                    // armée : ses charges en soute
        // elle se bat sous le pavillon de son camp, pâli avec elle
        if (_ghostColours[side] is { } flag && s.HasFlag) { s.SetEnsign(flag.Image, flag); s.ShowColours(true); }
        s.Ghostify(_ghosts.Rules.Opacity, _gunFx.Smoke);
        return new GhostHull(s.Physics, s, _hulls[s].Y);
    }

    void IGhostHost.Scuttle(Ghost g) { if (g.Tag is ShipNode s) RemoveShip(s); }

    void IGhostHost.Say(string msg) => Say(msg);

    /* ------------------------------------------------------------------ */
    /*  RETIRER UNE COQUE — scuttle() de la page                            */
    /* ------------------------------------------------------------------ */

    /// <summary>
    /// La sortir de la flotte, et de tout ce qui tenait quelque chose d'elle. L'indice
    /// dans la flotte EST la rangée de profil : celles qui la suivent remontent d'un
    /// cran, et leurs profils sont réécrits.
    /// </summary>
    void RemoveShip(ShipNode s)
    {
        if (s == _ship || !_others.Remove(s)) return;
        if (_hulls.Remove(s, out var hull)) _spray.Pool.Colliders.Remove(hull.Col);
        _helms.Remove(s);
        _pirates.Remove(s);
        _targets.Remove(s);
        _rearm.Remove(s);
        _hostile.Remove(s);
        var angry = new List<ShipNode>();
        foreach (var (k, v) in _hostile) if (v.Foe == s) angry.Add(k);
        foreach (var k in angry) _hostile.Remove(k);
        if (_preys.Remove(s, out var prey)) _preyShip.Remove(prey);
        RefitFleet();
        RemoveChild(s);
        s.QueueFree();
    }

    /// <summary>La flotte dans son ordre — le navire commandé, puis les autres — et chaque rangée de profil à sa place.</summary>
    void RefitFleet()
    {
        _fleet.Clear();
        _fleet.Add(_ship.Physics);
        foreach (var o in _others)
        {
            int row = _fleet.Count;
            _fleet.Add(o.Physics);
            if (row < Config.MaxShips && _hulls.TryGetValue(o, out var h)) _sea.SetHullProfile(row, h.Prof);
        }
    }

    /* ------------------------------------------------------------------ */
    /*  LE CIMETIÈRE DES GALIONS, pour l'essai — fantomes() de la page       */
    /* ------------------------------------------------------------------ */

    /// <summary>
    /// Toute la flotte transportée à douze cents mètres du milieu, la nuit tombée
    /// d'un coup (21 h) ; <paramref name="now"/> fait lever la bataille sans attendre.
    /// Le monde glisse sous elle, comme pour une dépression : la mer, l'écume et
    /// l'embrun changent d'origine, les coques restent où elles sont.
    /// </summary>
    void GoToGhosts(bool now)
    {
        var G = _ghosts.Rules;
        var b = _ship.Physics.Body;
        var o = _sea.Core.Origin;
        double x = o.X + b.Pos.X, z = o.Z + b.Pos.Z;
        double dx = x - G.X, dz = z - G.Z, dl = Math.Sqrt(dx * dx + dz * dz);
        if (dl == 0) { dx = 1; dl = 1; }
        double tx = G.X + dx / dl * 1200, tz = G.Z + dz / dl * 1200;
        _sea.Core.Time = _t;
        _sea.Core.Rebase(tx - x, tz - z);
        _foam.Rebase((float)(tx - x), (float)(tz - z));
        _spray.Pool.Rebase(tx - x, tz - z);
        if (_kraken.State == KrakenState.Lurk || _kraken.State == KrakenState.Grip) _kraken.Dive("storm");
        if (_fixed) Plant();
        b.Vel = Vec3d.Zero;
        b.AngVel = Vec3d.Zero;
        foreach (var s in _others) { s.Physics.Body.Vel = Vec3d.Zero; s.Physics.Body.AngVel = Vec3d.Zero; }
        /* L'HEURE, pas le soleil : le cycle du jour réécrit le soleil depuis l'heure
           à chaque image, et un soleil posé à la main revenait au jour. */
        _sky.Core.SetTimeOfDay(21, _sky.Latitude);
        _sky.Apply();
        _ghosts.Spent = false;
        if (now) _ghosts.Linger = G.LingerBefore;
        Say("Vous voici au " + G.Name + ".");
    }
}
