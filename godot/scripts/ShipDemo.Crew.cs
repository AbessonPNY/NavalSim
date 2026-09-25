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

    /* L'AMBIANCE DE LA MER, par bandes d'état de mer. La mer ne fait pas le même
       bruit à force 2 et à force 8, et une seule boucle sonnerait faux la moitié
       du temps. On range donc des bandes, de la plus calme à la plus grosse, et
       la première dont le plafond dépasse la force courante l'emporte. Une seule
       suffit pour commencer : elle couvre alors jusqu'à son plafond, et au-delà
       la mer se tait plutôt que de mentir. */
    readonly List<(string File, double MaxForce, double Gain)> _seaBeds = new();
    double _seaCheck;
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

    /// <summary>Les deux musiques, nommées par le manifeste et non par le code.</summary>
    string _ambCalme = "", _ambChaud = "";

    /* TOUS LES SONS EN UN SEUL ENDROIT — medias/sound/sons.json (demandé).
       Ils étaient dispersés : quatre noms de fichiers en dur dans SoundNode, deux
       constantes de musique dans ShipDemo, et les bandes de mer dans
       settings.json, qui est le fichier des RÉGLAGES et non celui du décor. Un
       manifeste absent ne casse rien : chaque morceau garde ce qu'il avait. */
    void LoadSounds()
    {
        if (_sound == null) return;
        string path = System.IO.Path.Combine(WorldLoad.Folder, "medias", "sound", "sons.json");
        if (!System.IO.File.Exists(path)) return;
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(System.IO.File.ReadAllText(path));
            var root = doc.RootElement;

            // les échantillons : une clé du manifeste REMPLACE ce que le code avait mis
            void Bag(string bloc, string champ, string key)
            {
                if (!root.TryGetProperty(bloc, out var b) || !b.TryGetProperty(champ, out var arr)
                    || arr.ValueKind != System.Text.Json.JsonValueKind.Array || arr.GetArrayLength() == 0) return;
                _sound.Forget(key);
                foreach (var f in arr.EnumerateArray())
                    if (f.GetString() is string file && file.Length > 0) _sound.Load(key, file);
            }
            Bag("canon", "pres", "pres");
            Bag("canon", "loin", "loin");
            Bag("bois", "choc", "bois");
            /* LE TONNERRE A SES PROPRES CLÉS et non « pres »/« loin », qui sont
               celles du canon : deux blocs du manifeste qui se rangeraient sous le
               même nom se remplaceraient l'un l'autre, et le dernier lu gagnerait
               en silence. */
            Bag("feu", "explosion", "explosion");
            Bag("foudre", "pres", "tonnerre-pres");
            Bag("foudre", "loin", "tonnerre-loin");
            /* LA TOILE VA DANS LA RÉSERVE DU BORD, pas dans celle des coups : elle
               se fait à vingt mètres, sans retard de trajet ni filtre de l'air. */
            void Board(string bloc, string champ, string key)
            {
                if (!root.TryGetProperty(bloc, out var b) || !b.TryGetProperty(champ, out var arr)
                    || arr.ValueKind != System.Text.Json.JsonValueKind.Array || arr.GetArrayLength() == 0) return;
                _sound.ForgetCrew(key);
                string dir = System.IO.Path.Combine(WorldLoad.Folder, "medias", "sound");
                foreach (var f in arr.EnumerateArray())
                {
                    if (f.GetString() is not string file || file.Length == 0) continue;
                    string full = System.IO.Path.Combine(dir, file);
                    if (!System.IO.File.Exists(full)) { GD.PushWarning($"son absent : {file}"); continue; }
                    _sound.AddCrew(key, full);
                }
            }
            Board("voiles", "monter", "voile-monte");
            Board("voiles", "descendre", "voile-descend");
            Board("eau", "plonge", "eau-plonge");
            Board("eau", "sort", "eau-sort");

            // les bandes de mer
            if (root.TryGetProperty("mer", out var mer) && mer.TryGetProperty("bandes", out var bandes)
                && bandes.ValueKind == System.Text.Json.JsonValueKind.Array)
            {
                _seaBeds.Clear();
                foreach (var e in bandes.EnumerateArray())
                {
                    if (e.ValueKind != System.Text.Json.JsonValueKind.Object) continue;
                    string? f = e.TryGetProperty("fichier", out var fv) ? fv.GetString() : null;
                    if (string.IsNullOrEmpty(f)) continue;
                    double max = e.TryGetProperty("maxForce", out var mv) ? mv.GetDouble() : 99;
                    double gain = e.TryGetProperty("gain", out var gv) ? gv.GetDouble() : 0.7;
                    _seaBeds.Add((f, max, gain));
                }
                _seaBeds.Sort((a, b) => a.MaxForce.CompareTo(b.MaxForce));
            }

            // ce qui étouffe, et de combien : cela s'écoute, donc cela se règle
            if (root.TryGetProperty("etouffe", out var et))
            {
                float F(string n, float d) => et.TryGetProperty(n, out var v) && v.ValueKind == System.Text.Json.JsonValueKind.Number ? (float)v.GetDouble() : d;
                _sound.HzCabine = F("cabineHz", _sound.HzCabine);
                _sound.HzEau = F("eauHz", _sound.HzEau);
                _sound.DbCabine = F("cabineDb", _sound.DbCabine);
                _sound.DbEau = F("eauDb", _sound.DbEau);
                GD.Print(FormattableString.Invariant(
                    $"etouffe : cabine {_sound.HzCabine:F0} Hz {_sound.DbCabine:F0} dB, eau {_sound.HzEau:F0} Hz {_sound.DbEau:F0} dB"));
            }

            // les musiques
            if (root.TryGetProperty("musique", out var mus))
            {
                if (mus.TryGetProperty("navigation", out var n) && n.GetString() is string sn) _ambCalme = sn;
                if (mus.TryGetProperty("action", out var a) && a.GetString() is string sa) _ambChaud = sa;
            }
            GD.Print($"sons : {_seaBeds.Count} bande(s) de mer, musiques "
                   + (_ambCalme.Length > 0 || _ambChaud.Length > 0 ? "nommées" : "par défaut"));
        }
        catch (Exception e) { GD.PushWarning($"sons.json illisible ({e.Message}) : les sons d'origine sont gardés"); }
    }

    /* CE QUE LA MER DIT, À CETTE FORCE-LÀ. Repris toutes les deux secondes et non
       à chaque image : le choix ne change qu'avec le temps qu'il fait, et
       SoundNode.Sea ne fait rien quand on lui redonne ce qu'il joue déjà. */
    void SeaBedTick()
    {
        if (_sound == null || _seaBeds.Count == 0 || _t < _seaCheck) return;
        _seaCheck = _t + 2;
        double force = _sea.Core.SeaState;
        foreach (var b in _seaBeds)
            if (force <= b.MaxForce) { _sound.Sea(b.File, b.Gain); return; }
        // au-delà de la dernière bande, elle se tait : mieux que de sonner faux
        _sound.Sea(null);
    }

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

    /// <summary>
    /// LA TOILE, ET ELLE PARLE D'EN HAUT. Une voix sort de la dunette ; le chanvre
    /// qui file dans ses poulies et la toile qui s'abat viennent du gréement, à
    /// mi-hauteur des mâts — c'est ce qui les distingue à l'oreille quand les deux
    /// partent ensemble.
    /// </summary>
    void Canvas(string key)
    {
        if (_sound == null) return;
        var b = _ship.Physics.Body;
        var at = b.Quat.Rotate(new Vec3d(0, _ship.Spec.DeckMid + 6, 0)) + b.Pos;
        _sound.Crew(key, at, _crewGain, 1.5);
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
        if (ctrl.SailsSet != _wasSails)
        {
            Shout(ctrl.SailsSet ? "voiles" : "ferle", 0.18);
            /* ET LA TOILE ELLE-MÊME. L'ordre crié et le bruit du chanvre sont deux
               choses : l'un vient de la dunette, l'autre de partout à la fois, et
               on peut vouloir l'un sans l'autre. La toile parle depuis le
               gréement — en l'air, au milieu du navire. */
            Canvas(ctrl.SailsSet ? "voile-monte" : "voile-descend");
            _wasSails = ctrl.SailsSet;
        }
        if (_reef != _wasReef) { Shout("ris", 0.18); _wasReef = _reef; }

        // le pavillon
        if (_colours != _wasColours) { Shout(_colours ? "pavillon-haut" : "pavillon-bas", -0.35); _wasColours = _colours; }

        // l'ancre : le fracas de la chaîne dans l'écubier, puis le cabestan
        bool down = _anchor2 != null && _anchor2.IsDown(_ship);
        if (down != _wasAnchor) { Shout(down ? "mouille" : "leve", 0.42); _wasAnchor = down; }

        // la barre toute d'un bord : on ne crie qu'une fois par coup de barre
        if (Math.Abs(ctrl.Rudder) > 0.85) Shout("barre", -0.3, 6);

        SeaBedTick();
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

        /* ELLE NE SONNE QUE LE TEMPS QUI PASSE DE LUI-MÊME. Le curseur de l'heure,
           une partie qu'on reprend, une traversée d'une région à l'autre : l'horloge
           SAUTE, et piquer tous les coups de chaque demi-heure franchie donnait un
           carillon de plusieurs minutes (signalé). UNE demi-heure d'écart, et une
           seule, est du temps vécu ; tout le reste est un saut, qu'on rattrape en
           silence, la file des coups en attente comprise.

           Le tour de minuit compte comme un pas (47 → 0), sans quoi on perdrait les
           huit coups du changement de jour, qui sont justement ceux qu'on écoute. */
        double pas = (demi - _wasBell + 48) % 48;
        _wasBell = demi;
        if (pas != 1) { _bellQueue.Clear(); return; }
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
