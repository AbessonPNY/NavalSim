using Godot;
using System;
using System.Collections.Generic;
using NavalSim.Core;

namespace NavalSim;

/// <summary>
/// LE BOULET QUI SIFFLE — comme une balle qui passe : un boulet tiré d'un AUTRE
/// navire, qui a déjà volé ses cinq premiers mètres (sinon c'est le coup de
/// départ qu'on entend, pas le passage), et qui passe à portée d'oreille de la
/// caméra. Un sur vingt seulement : le sifflement est un accident du boulet — sa
/// rotation, un défaut de fonte, une écaille —, pas sa règle ; et vingt
/// sifflements par bordée feraient d'un combat un concert.
///
/// Chaque boulet est jugé UNE FOIS, à son entrée dans la portée : il siffle ou non,
/// et l'on n'y revient pas. settings.json → whistle (oneIn, range, minFlight) ;
/// le son, medias/sound/sons.json → boulet.siffle — absent, le boulet se tait.
/// </summary>
public partial class ShipDemo
{
    int _whistleOneIn = 20;
    double _whistleRange = 40, _whistleMinFlight = 5;
    readonly HashSet<Gunnery.Shot> _whistleJudged = new();
    readonly HashSet<Gunnery.Shot> _whistleLive = new();

    // --- l'essai : --sifflet N — N boulets d'un autre navire, qui passent près de l'œil ---
    int _whistleTest;

    void WhistleTest()
    {
        int n = _whistleTest; _whistleTest = 0;
        var from = _others.Count > 0 ? _others[0].Physics : null;
        if (from == null) { GD.Print("[sifflet] pas d'autre navire pour tirer"); return; }
        var e = _cam.GlobalPosition;
        for (int i = 0; i < n; i++)
        {
            // de cent mètres sur le côté, à deux cents mètres par seconde, passant à quelques mètres
            double off = (i % 7) - 3;
            _gunnery.Shots.Add(new Gunnery.Shot
            {
                P = new Vec3d(e.X - 100, e.Y + off, e.Z + off), V = new Vec3d(200, 0, 0),
                C = 0.00097, K = 1, T = 0.5, From = from
            });
        }
        GD.Print($"[sifflet] {n} boulets d'essai lancés");
    }

    void WhistleTick()
    {
        var shots = _gunnery.Shots;
        if (shots.Count == 0) { _whistleJudged.Clear(); return; }
        var ear = _cam.GlobalPosition;
        _whistleLive.Clear();
        foreach (var b in shots)
        {
            _whistleLive.Add(b);
            if (b.From == _ship.Physics || _whistleJudged.Contains(b)) continue;
            // le chemin déjà fait : sa vitesse par son âge suffit, sur cinq mètres
            if (b.T * b.V.Length < _whistleMinFlight) continue;
            double dx = b.P.X - ear.X, dy = b.P.Y - ear.Y, dz = b.P.Z - ear.Z;
            if (dx * dx + dy * dy + dz * dz > _whistleRange * _whistleRange) continue;
            _whistleJudged.Add(b);
            if (GD.Randf() * _whistleOneIn < 1) _sound.Whistle(b.P);
        }
        // oublier ceux qui sont tombés
        _whistleJudged.IntersectWith(_whistleLive);
    }
}
