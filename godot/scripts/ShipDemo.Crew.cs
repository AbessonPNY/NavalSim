using Godot;
using System;
using System.Collections.Generic;
using NavalSim.Core;

namespace NavalSim;

/// <summary>
/// LA VIE DU BORD, PAR L'OREILLE — et c'est le moyen le moins cher qu'on ait de
/// peupler un pont.
///
/// Dessiner un équipage coûte une passe de peau par homme, et il y a jusqu'à
/// seize coques à l'eau : c'est ce qui a fait ranger `crew.js`. Mais ce n'est pas
/// la vue qui dit qu'un navire est habité, c'est le BRUIT — un ordre crié quand
/// on brasse, une cloche au quart, un rire dans le gaillard la nuit. Rien de tout
/// cela ne coûte une image : un échantillon part, et c'est tout.
///
/// Rien n'est écrit en dur ici que les MOMENTS. Les sons sont nommés dans
/// medias/sound/crew.json, plusieurs par clé, et une clé vide reste muette — on
/// peut donc n'en remplir qu'une et commencer.
/// </summary>
public partial class ShipDemo
{
    double _crewGain = 0.9, _crewHold = 2.5, _crewLo = 45, _crewHi = 180;
    double _nextLife = -1;
    readonly Random _crewRng = new();

    /* CE QUE L'ON SURVEILLE POUR SAVOIR QU'IL S'EST PASSÉ QUELQUE CHOSE. Le jeu
       n'a pas d'événements pour la plupart de ces gestes — la toile est un booléen,
       l'ancre un état —, et en ajouter partout pour un son serait payer cher un
       cri. On garde donc l'état d'avant, et on compare. */
    bool _wasSails, _wasColours = true, _wasAnchor;
    int _wasReef;
    double _wasBell = -1;

    // ------------------------------------------------------------------
    //  LE MANIFESTE
    // ------------------------------------------------------------------

    void LoadCrewVoices()
    {
        if (_sound == null) return;
        string dir = System.IO.Path.Combine(WorldLoad.Folder, "medias", "sound");
        string manifest = System.IO.Path.Combine(dir, "crew.json");
        if (!System.IO.File.Exists(manifest)) return;
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(System.IO.File.ReadAllText(manifest));
            var root = doc.RootElement;
            if (root.TryGetProperty("reglages", out var r))
            {
                if (r.TryGetProperty("gain", out var g)) _crewGain = g.GetDouble();
                if (r.TryGetProperty("attente", out var a)) _crewHold = a.GetDouble();
                if (r.TryGetProperty("vie", out var v) && v.ValueKind == System.Text.Json.JsonValueKind.Array && v.GetArrayLength() == 2)
                { _crewLo = v[0].GetDouble(); _crewHi = v[1].GetDouble(); }
            }
            if (!root.TryGetProperty("voix", out var voix)) return;
            int n = 0, keys = 0;
            foreach (var k in voix.EnumerateObject())
            {
                if (k.Value.ValueKind != System.Text.Json.JsonValueKind.Array) continue;
                bool any = false;
                foreach (var f in k.Value.EnumerateArray())
                {
                    if (f.GetString() is not string file || file.Length == 0) continue;
                    // nommé tout court : dans crew/ ; nommé avec un chemin : tel quel, depuis medias/sound
                    string path = System.IO.Path.Combine(dir, file.Contains('/') ? file : System.IO.Path.Combine("crew", file));
                    if (!System.IO.File.Exists(path)) { GD.PushWarning($"voix absente : {file}"); continue; }
                    _sound.AddCrew(k.Name, path);
                    n++; any = true;
                }
                if (any) keys++;
            }
            if (n > 0) GD.Print($"voix du bord : {n} échantillon(s) sur {keys} clé(s)");
        }
        catch (Exception e) { GD.PushWarning($"crew.json illisible ({e.Message}) : le bord restera muet"); }
    }

    // ------------------------------------------------------------------
    //  OÙ LA VOIX SORT
    // ------------------------------------------------------------------

    /// <summary>Le pont, un peu en avant de la barre : d'où l'on crie les ordres.</summary>
    Vec3d CrewAt(double zFrac = 0.1)
    {
        var b = _ship.Physics.Body;
        return b.Quat.Rotate(new Vec3d(0, _ship.Spec.DeckMid + 1.4, _ship.Spec.L * zFrac)) + b.Pos;
    }

    /// <summary>Un ordre, s'il y a une voix pour le crier.</summary>
    public void Shout(string key, double zFrac = 0.1, double hold = -1)
        => _sound?.Crew(key, CrewAt(zFrac), _crewGain, hold < 0 ? _crewHold : hold);

    // ------------------------------------------------------------------
    //  LES MOMENTS
    // ------------------------------------------------------------------

    void CrewTick(double dt)
    {
        if (_sound == null || _ship == null || _inTitle) return;
        var ctrl = _ship.Ctrl;

        // la toile : établie, serrée, et les ris qu'on prend ou qu'on largue
        if (ctrl.SailsSet != _wasSails) { Shout(ctrl.SailsSet ? "voiles" : "ferle", 0.18); _wasSails = ctrl.SailsSet; }
        if (_reef != _wasReef) { Shout("ris", 0.18); _wasReef = _reef; }

        // le pavillon
        if (_colours != _wasColours) { Shout(_colours ? "pavillon-haut" : "pavillon-bas", -0.35); _wasColours = _colours; }

        // l'ancre : le fracas de la chaîne dans l'écubier, puis le cabestan
        bool down = _anchor2 != null && _anchor2.IsDown(_ship);
        if (down != _wasAnchor) { Shout(down ? "mouille" : "leve", 0.42); _wasAnchor = down; }

        // la barre toute d'un bord : on ne crie qu'une fois par coup de barre
        if (Math.Abs(ctrl.Rudder) > 0.85) Shout("barre", -0.3, 6);

        Bell();
        Life(dt);
    }

    /* LA CLOCHE DU QUART, et c'est l'horloge d'un navire. Un quart dure quatre
       heures ; on pique un coup par demi-heure écoulée, de un à huit, et les coups
       vont PAR PAIRES — deux, deux, deux, un — ce qui est ce qui les rend
       comptables à l'oreille. Un seul échantillon suffit : le rythme est ici. */
    void Bell()
    {
        if (_sound == null || _sound.CrewCount("cloche") == 0) return;
        double h = _sky.Core.DayTime;
        double demi = Math.Floor(h * 2);                 // le numéro de la demi-heure du jour
        if (_wasBell < 0) { _wasBell = demi; return; }
        if (demi == _wasBell) return;
        _wasBell = demi;
        int piques = (int)(((int)demi % 8) == 0 ? 8 : (int)demi % 8);
        for (int i = 0; i < piques; i++)
        {
            // deux coups serrés, puis un souffle avant la paire suivante
            double t = (i / 2) * 1.25 + (i % 2) * 0.32;
            _bellQueue.Add(_t + t);
        }
    }
    readonly List<double> _bellQueue = new();

    void Life(double dt)
    {
        // les coups de cloche en attente
        for (int i = _bellQueue.Count - 1; i >= 0; i--)
            if (_bellQueue[i] <= _t)
            {
                _sound?.Crew("cloche", CrewAt(-0.2), _crewGain, 0);
                _bellQueue.RemoveAt(i);
            }

        /* LA VIE DU BORD — ce qui n'est l'ordre de personne : un rire, une toux, un
           seau, un bout qu'on love. C'est elle qui fait la différence entre un
           navire et une maquette, et elle ne coûte qu'un échantillon de temps en
           temps. Rien au port : on l'entend déjà de la ville. */
        if (_nextLife < 0) _nextLife = _t + _crewLo + _crewRng.NextDouble() * (_crewHi - _crewLo);
        if (_t < _nextLife) return;
        _nextLife = _t + _crewLo + _crewRng.NextDouble() * (_crewHi - _crewLo);
        if (_portHere != null) return;
        Shout("vie", (_crewRng.NextDouble() - 0.5) * 0.7, 0);
    }
}
