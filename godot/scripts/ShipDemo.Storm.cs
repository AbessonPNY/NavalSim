using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using NavalSim.Core;

namespace NavalSim;

/// <summary>Ce que la tempête apporte, et ce que porter de la toile coûte (sorti de ShipDemo.cs).</summary>
public partial class ShipDemo
{
    // ------------------------------------------------------------------
    //  CE QUI VIENT AVEC LA TEMPÊTE — la foudre qui tombe sur une tête de
    //  mât, et le kraken qui vit au cœur des dépressions
    // ------------------------------------------------------------------

    LightningNode _lightning = null!;
    CordageNode _cordage = null!;
    SplinterNode _splinters = null!;
    readonly List<ShipNode> _allShips = new();
    KrakenNode _krakenNode = null!;
    Kraken _kraken = null!;
    LightningSettings _lightRules = new();
    GunnerySettings _gunRules = new();
    KrakenSettings _krakenRules = new();
    readonly Random _stormRng = new();
    // chaque coque dans une dépression, et combien elle y est enfoncée — relue à chaque image
    readonly List<(KrakenPrey Prey, double Inten)> _orage = new(), _orageKraken = new();
    readonly Dictionary<ShipNode, KrakenPrey> _preys = new();
    readonly Dictionary<KrakenPrey, ShipNode> _preyShip = new();

    // gardé : une lambda neuve à chaque image, ce sont des octets pour le ramasse-miettes
    Func<KrakenPrey, bool>? _alive;
    bool PreyAlive(KrakenPrey p) =>
        _preyShip.TryGetValue(p, out var s) && (s == _ship || _others.Contains(s)) && !p.Physics.Foundered;

    Label _note = null!;
    double _noteLeft;

    /// <summary>Dire une phrase, et la laisser s'effacer — dire() de la page.</summary>
    /* « ENNEMI TOUCHÉ ! » — une bordée porte plusieurs coups, et autant de lignes
       serait illisible : on les compte et on ne parle qu une fois le tir retombé
       (un demi-quart de seconde sans touche), avec le compte s il y en a eu
       plusieurs. */
    int _hits;
    /* ET LES COUPS DANS UN NAVIRE DE SON PROPRE PAVILLON, comptés à part. On
       annonçait « Ennemi touché ! » en canonnant un ami (signalé) — ce qui n'est
       pas seulement faux, c'est le contraire de ce qu'il faut entendre. On ne le
       tait pas pour autant : un boulet dans son consort est une faute, et le
       capitaine doit l'apprendre de son bord avant de l'apprendre du sien. */
    int _friendly;
    double _hitsAt = double.NegativeInfinity;

    /* LES COUPS QU'ON REÇOIT, dits comme ceux qu'on porte : une bordée de dix
       boulets ferait dix messages, on compte et l'on parle quand le tir retombe.
       Le cri de l'équipage partait seul, et ne disait pas OÙ (signalé). */
    int _takenHull, _takenSail, _takenMast;
    double _takenAt = double.NegativeInfinity;

    void TakenTick()
    {
        if (_takenHull + _takenSail + _takenMast == 0 || _t - _takenAt < 0.5) return;
        var parts = new List<string>();
        if (_takenHull > 0) parts.Add(_takenHull == 1 ? "un boulet dans la coque" : $"{_takenHull} boulets dans la coque");
        if (_takenSail > 0) parts.Add(_takenSail == 1 ? "un dans la toile" : $"{_takenSail} dans la toile");
        if (_takenMast > 0) parts.Add(_takenMast == 1 ? "un dans la mâture" : $"{_takenMast} dans la mâture");
        string quoi = string.Join(", ", parts);
        // une voie d'eau est ce qui demande une réponse : on la nomme
        Say("Nous sommes touchés ! " + char.ToUpper(quoi[0]) + quoi[1..]
            + (_takenHull > 0 ? " — voies d'eau, aux pompes" : ""));
        _takenHull = _takenSail = _takenMast = 0;
    }

    void HitsTick()
    {
        TakenTick();
        if ((_hits == 0 && _friendly == 0) || _t - _hitsAt < 0.5) return;
        string ennemi = _hits > 1 ? $"Ennemi touché ! {_hits} coups au but"
                      : _hits == 1 ? "Ennemi touché !" : "";
        string ami = _friendly > 1 ? $"{_friendly} coups dans un navire de votre pavillon !"
                   : _friendly == 1 ? "Un boulet dans un navire de votre pavillon !" : "";
        Say(ennemi.Length > 0 && ami.Length > 0
            ? ennemi + " — et " + char.ToLower(ami[0]) + ami[1..]
            : ennemi.Length > 0 ? ennemi : ami);
        _hits = 0;
        _friendly = 0;
    }

    /* LA PORTE DE LA CHAMBRE — et elle ne claque pas : un huitième de seconde
       pour aller de l'un à l'autre, sans quoi le changement de vue fait un à-coup
       dans le son. Une vue enfermée est celle que la fiche dit `closed` ; servir
       une pièce de chasse est un poste de PONT, donc dehors quoi qu'en dise le
       pont d'où l'on regarde. */
    double _indoors;

    /* LA PLONGÉE DE DÉMONSTRATION (--plongee 1.5, en m/s).
       Juger un passage de surface à la main est impossible : on n'y descend jamais
       deux fois à la même vitesse, et c'est justement la vitesse qui décide si le
       son tombe juste. Celle-ci descend à une allure DONNÉE, tient trois secondes
       dessous, remonte et rend la caméra — le même geste à chaque essai, donc deux
       réglages comparables. Elle a servi à trouver les 212 ms d'écart entre l'œil
       et l'oreille, et elle reste pour la prochaine fois. */
    double _diveSpeed;
    double _diveT0 = -1, _diveY0;
    int _dive;

    void DiveShow(double dt)
    {
        if (_diveSpeed <= 0 || _cam == null) return;
        if (_diveT0 < 0)
        {
            if (_t < 4) return;                       // le temps que la mer se pose
            _diveT0 = _t;
            var c0 = _cam.GlobalPosition;
            // on part de trois mètres sur l eau : au-delà, la descente serait un voyage
            _diveY0 = _sea.Core.Sample(c0.X, c0.Z, _t) + 3.0;
            _cam.GlobalPosition = new Vector3(c0.X, (float)_diveY0, c0.Z);
            _dive = 1;
            GD.Print($"— plongée de démonstration à {_diveSpeed:F1} m/s —");
        }
        var c = _cam.GlobalPosition;
        double u = _t - _diveT0;
        /* QUATRE MÈTRES SOUS LA MER, et non sous le point de départ : en vue orbit
           la caméra est à quinze mètres, et descendre de quatre mètres la laissait
           en plein ciel — la démonstration ne montrait rien. */
        double mer = _sea.Core.Sample(c.X, c.Z, _t);
        double bas = mer - 4.0;
        if (_dive == 1)
        {
            double y = _diveY0 - u * _diveSpeed;
            if (y <= bas) { _dive = 2; _diveT0 = _t; y = bas; GD.Print("— sous l'eau —"); }
            _cam.GlobalPosition = new Vector3(c.X, (float)y, c.Z);
        }
        else if (_dive == 2)
        {
            _cam.GlobalPosition = new Vector3(c.X, (float)bas, c.Z);
            if (u > 3) { _dive = 3; _diveT0 = _t; GD.Print("— on remonte —"); }
        }
        else if (_dive == 3)
        {
            double y = bas + u * _diveSpeed;
            if (y >= _diveY0) { _dive = 4; _diveSpeed = 0; GD.Print("— fini, la caméra vous est rendue —"); return; }
            _cam.GlobalPosition = new Vector3(c.X, (float)y, c.Z);
        }
    }

    void CabinTick(double dt)
    {
        if (_sound == null || _ship == null) return;
        bool inside = _gunPost == null && _camMode == 1
                   && _deck >= 0 && _deck < _ship.Spec.Decks.Count
                   && _ship.Spec.Decks[_deck].Closed;
        double want = inside ? 1 : 0;
        double step = dt / 0.125;
        _indoors += Math.Clamp(want - _indoors, -step, step);
        _sound.Indoors(_indoors);
    }

    void Say(string text)
    {
        if (_film != null) return;                 // un film ne parle pas : il montre
        _note.Text = text;
        _note.Visible = true;
        _noteLeft = 2.6;
        GD.Print(text);
    }

    KrakenPrey PreyOf(ShipNode s)
    {
        if (_preys.TryGetValue(s, out var p)) return p;
        p = new KrakenPrey { Physics = s.Physics, Tops = s.MastTops, DeckNear = s.DeckNear, Name = s.Spec.Name };
        _preys[s] = p;
        _preyShip[p] = s;
        return p;
    }

    /* CE QUE LE KRAKEN FAIT, DIT À QUI COMMANDE. Quand c'est le vôtre, la phrase
       est la vôtre ; quand c'est un autre, elle le nomme — et seulement s'il est
       assez près pour qu'on le voie, à moins de trois kilomètres. */
    static readonly Dictionary<string, (string Mine, string Other)> KrakenSays = new()
    {
        ["appear"] = ("Quelque chose d’énorme remue sous la houle…", "Quelque chose d’énorme remue sous la houle, près du {n}…"),
        ["approach"] = ("Le kraken se rapproche !", "Le kraken se rapproche du {n} !"),
        ["grip"] = ("Le kraken enlace le navire !", "Le kraken enlace le {n} !"),
        ["beaten"] = ("Touché ! Le kraken lâche prise et sombre dans les profondeurs.", "Le kraken lâche le {n} et sombre dans les profondeurs."),
        ["flee"] = ("Le kraken renonce : vous l’avez distancé.", "Le {n} a distancé le kraken."),
        ["storm"] = ("Le kraken regagne les profondeurs.", "Le kraken regagne les profondeurs.")
    };

    void KrakenSay((string Mine, string Other) t, KrakenPrey prey)
    {
        if (_preyShip.TryGetValue(prey, out var s) && s == _ship) { Say(t.Mine); return; }
        if ((prey.Physics.Body.Pos - _ship.Physics.Body.Pos).Length <= 3000) Say(t.Other.Replace("{n}", prey.Name));
    }

    void WireKraken()
    {
        // les gerbes de ses bras qui crèvent la surface, et de ce qu'ils arrachent
        _kraken.Splash = (at, water, speed, jet) => _spray.Pool.Burst(at, water, speed, jet);
        _kraken.Growl = at => _sound?.Growl(at);
        _kraken.Event = (kind, prey) =>
        {
            if (prey != null && KrakenSays.TryGetValue(kind, out var t)) KrakenSay(t, prey);
        };
        /* LE DÉGÂT N'EST PAS ENCORE PORTÉ : un mât qui se tord ou tombe, une voile
           arrachée de ses ralingues viendront avec la mâture qui tombe et la toile
           qui se déchire (les canons en ont besoin aussi). Dit à la console, et
           pas au joueur : annoncer un dégât qui n'a pas lieu serait mentir. */
        _kraken.OnMast = (prey, fall) =>
        {
            if (!_preyShip.TryGetValue(prey, out var s)) return;
            KrakenSay(s.WoundMast(fall) ? ("Le kraken arrache un mât !", "Le kraken arrache un mât du {n} !")
                                        : ("Le kraken tord un mât dans son étreinte !", "Le kraken tord un mât du {n} !"), prey);
        };
        _kraken.OnSail = prey =>
        {
            if (_preyShip.TryGetValue(prey, out var s) && s.SplitSail(false) >= 0)
                KrakenSay(("Un bras du kraken déchire une voile !", "Le kraken déchire une voile du {n} !"), prey);
        };
    }

    /// <summary>
    /// Une image de ce qui vient avec la tempête : qui est dans une dépression et
    /// de combien, la foudre tirée navire par navire, le kraken.
    /// </summary>
    void StormTick(double dt)
    {
        if (_noteLeft > 0 && (_noteLeft -= dt) <= 0) _note.Visible = false;

        var o = _sea.Core.Origin;
        _orage.Clear();
        Gather(_ship);
        foreach (var s in _others) Gather(s);
        void Gather(ShipNode s)
        {
            var b = s.Physics.Body;
            if (_storms.At(o.X + b.Pos.X, o.Z + b.Pos.Z, _t, out var q)) _orage.Add((PreyOf(s), q.Inten));
        }

        /* PAS DE MONSTRES EN ESCARMOUCHE (demandé). Une escarmouche est une
           BATAILLE NAVALE : douze coques qui se cherchent sous leurs pavillons,
           et l'on a déjà fort à faire. Le kraken qui enlace, la baleine qui
           charge et le serpent qui sort de la pluie sont l'affaire du jeu libre
           et de l'Histoire, où l'on navigue seul et où une rencontre est un
           événement — pas un troisième camp dans une mêlée.

           LE TEMPS, LUI, RESTE : le gros temps, la foudre et l'incendie ne sont
           pas du bestiaire, ce sont les conditions de la bataille, et une
           escarmouche sous un grain vaut mieux qu'une escarmouche par calme. */
        // ni dans une escarmouche, ni dans le film d'un chapitre : la tempête y suffit
        if (!_skirmish && _film == null)
        {
            // le kraken ne prend pas les spectres ; la foudre, si
            _orageKraken.Clear();
            foreach (var it in _orage) if (!_preyShip[it.Prey].IsGhost) _orageKraken.Add(it);
            _kraken.Update(dt, _orageKraken, _sea.Core, _t, _alive ??= PreyAlive);
            _krakenNode.Sync(_kraken);
            WhaleTick(dt);
            SerpentTick(dt);
        }

        foreach (var (prey, inten) in _orage)
            if (_stormRng.NextDouble() < _lightRules.StrikeChance(inten, dt)) StrikeSomewhere(_preyShip[prey]);
        _lightning.Step(dt);
        // ce qui brûle à bord : après la foudre, qui peut l'allumer
        FireTick(dt);

        // les bouts rompus de toute la flotte, et les éclats en l'air
        _allShips.Clear();
        _allShips.Add(_ship);
        _allShips.AddRange(_others);
        var h = _sky.Core.Horizon;
        _cordage.Step(dt, _allShips, _sea.Core, _t, _cam, _sea.Core.WindVec, new Vec3d(h.R, h.G, h.B));
        _splinters.Step(dt, _sea.Core, _t, _spray.Pool);
    }

    /// <summary>
    /// LE BESTIAIRE SE TAIT EN ESCARMOUCHE, et il le DIT. Les touches qui
    /// l'appellent ne pouvaient plus rien faire une fois son pas coupé — un
    /// kraken convoqué serait resté figé sous la mer, jamais mis à jour ni
    /// dessiné. Une commande qui ne fait rien SANS RIEN DIRE est le pire des
    /// deux : on la retape, on croit le clavier mort.
    /// </summary>
    bool BestiaireMuet()
    {
        if (!_skirmish) return false;
        Say("Pas de monstres en escarmouche : c'est une bataille navale");
        return true;
    }

    /* LA FOUDRE TOMBE SUR LA PLUS HAUTE TÊTE DE MÂT encore debout. L'éclair du
       ciel ne s'allume que si c'est assez près pour éclairer notre pont. Ce
       qu'elle coûte : une voile, une blessure de mât — les mêmes que les boulets —,
       et parfois le mât d'un coup. */
    /// <summary>
    /// UN COUP, ET LE SORT DÉCIDE OÙ IL TOMBE : sur sa mâture, ou dans la mer le
    /// long d'elle. C'est la même porte pour l'orage et pour ⇧F, sans quoi la
    /// touche montrerait autre chose que ce qu'on verra en jeu.
    /// </summary>
    void StrikeSomewhere(ShipNode s)
    {
        if (_stormRng.NextDouble() < _lightRules.NearChance) StrikeAlongside(s);
        else Strike(s);
    }

    /// <summary>
    /// LE COUP QUI DÉCHIRE LA NUIT LE LONG DU BORD — il tombe à l'eau, à quelques
    /// mètres du bordé, et ne casse rien.
    ///
    /// Un orage ne frappe pas que les mâts, et un bord qui ne reçoit que des
    /// coups sur sa mâture est un bord où la foudre est une avarie et jamais un
    /// spectacle. Celui-ci ne coûte rien : il éclaire, il tonne aussitôt — on est
    /// dedans —, et il lève une gerbe là où il entre dans l'eau.
    ///
    /// La distance se compte DU BORDÉ (demi-bau plus <c>nearMin…nearMax</c>) et
    /// non du centre, pour que quatre mètres soient les mêmes quatre mètres sur
    /// une chaloupe et sur un vaisseau. Quatre à dix : assez près pour qu'on le
    /// prenne pour soi, assez loin pour que ce soit un coup MANQUÉ.
    /// </summary>
    void StrikeAlongside(ShipNode s)
    {
        if (s.Physics.Foundered) return;
        var b = s.Physics.Body;
        double a2 = _stormRng.NextDouble() * Math.Tau;
        double d = s.Spec.B * 0.5 + _lightRules.NearMin
                 + _stormRng.NextDouble() * (_lightRules.NearMax - _lightRules.NearMin);
        double x = b.Pos.X + Math.Sin(a2) * d, z = b.Pos.Z + Math.Cos(a2) * d;
        float y = (float)_sea.Core.Sample(x, z, _t);
        var w = new Vector3((float)x, y, (float)z);

        _lightning.Strike(w);
        if (w.DistanceTo(_cam.GlobalPosition) < 3000) _sky.Strike();
        // il tombe à toucher : le tonnerre part avec, l'acoustique fait le reste
        _sound?.Thunder(w.ToCore());
        // et l'eau se soulève là où il entre
        _spray.Pool.Burst(new Vec3d(x, y, z), y, 9, 1.6);
        if (s == _ship) Say("La foudre tombe à toucher le bord !");
    }

    void Strike(ShipNode s)
    {
        if (s.Physics.Foundered || !s.HighestMasthead(out var w, out int fall)) return;
        _lightning.Strike(w);
        if (w.DistanceTo(_cam.GlobalPosition) < 3000) _sky.Strike();
        // on voit l'éclair, on compte, puis on entend : l'acoustique s'en charge
        _sound?.Thunder(w.ToCore());
        // la pomme du mât vole en éclats, vers le bas
        _splinters.Splinters(w, Vector3.Down, 0.6);
        /* ET LE FEU PREND, souvent : c'est le goudron des haubans et la toile
           sèche qui s'allument, pas le bois. Le foyer naît au PIED du mât frappé
           et non à sa pomme — ce qui brûle est ce qui tombe en flammes sur le
           pont, et un feu de tête de mât s'éteint tout seul. */
        if (_stormRng.NextDouble() < _fireRules.BoltChance)
        {
            var lb = s.Physics.Body;
            var lloc = lb.Quat.Inverted().Rotate(w.ToCore() - lb.Pos);
            LightFire(s, new Vec3d(lloc.X, s.Spec.DeckMid, lloc.Z), 0.16);
        }
        string what = "La foudre frappe la mâture !";
        if (fall >= 0)
        {
            if (_stormRng.NextDouble() < _lightRules.DismastChance)
            {
                if (s.DropMast(fall)) what = "La foudre fend le mât, qui s’abat !";
            }
            else if (_stormRng.NextDouble() < _lightRules.WoundChance)
                what = s.WoundMast(fall) ? "La foudre achève le mât, qui s’abat !" : "La foudre frappe le mât et le blesse !";
        }
        if (_stormRng.NextDouble() < _lightRules.SplitChance && s.SplitSail(false) >= 0 && fall < 0)
            what = "La foudre met une voile en lambeaux !";
        if (s == _ship) Say(what);
    }

    // ------------------------------------------------------------------
    //  PORTER DE LA TOILE COÛTE DE LA TOILE — tearCanvas et strainMast
    // ------------------------------------------------------------------

    void Rigging(ShipNode s, double dt)
    {
        s.StepRigging(dt);
        s.Physics.Standing = s.Standing();
        s.Physics.Whole = s.Whole();
    }

    /* Le risque est un TAUX, pas un seuil qui claque : une couture lâche d'autant
       plus vite qu'on insiste, comme le carré de l'excès, pour que force 7
       pardonne et que force 9 ne pardonne pas. LA SURFACE ÉTABLIE MULTIPLIE LE
       DANGER, ELLE NE BAISSE PAS LE SEUIL : la toile cède à une PRESSION, donc un
       ris ne soulage pas d'un newton ce qui reste dehors ; il achète moins de
       tissu exposé. Ferler tout à fait est la seule chose qui mette à l'abri.
       Relevé dans la page sur le galion à pleine voilure : force 7 pardonne une
       demi-heure, force 9 coûte une voile en une vingtaine de secondes. */
    const double TearRate = 0.045;            // par seconde, à deux fois le seuil
    /* LE MÂT A SA PROPRE CAUSE, bien plus rare, et une barre plus haute que la
       toile — 2,6 fois ce qu'elle tient —, sans quoi il partait AVANT la première
       déchirure, et un fusible qui saute après le circuit ne sert à rien. C'est
       la rafale qui casse le mât, pas le coup de vent. */
    const double MastRate = 1.0, MastLoad = 2.6;

    void TearCanvas(ShipNode s, double dt, bool mine)
    {
        var ph = s.Physics;
        if (ph.Foundered || ph.SetFrac < 0.02) return;
        double excess = ph.SailLoad / Config.CanvasStrength - 1;
        if (excess <= 0) return;
        if (_stormRng.NextDouble() > TearRate * excess * excess * ph.SetFrac * dt) return;
        // au DOUBLE de ce que la toile tient, la ferrure part avec le tissu et le mât prend une blessure
        bool hard = ph.SailLoad > 2 * Config.CanvasStrength;
        int r = s.SplitSail(hard);
        if (r == -1 || !mine) return;         // une conserve ne commente pas ses avaries
        Say(r <= -2 ? "Le mât est parti par-dessus bord !"
          : hard ? "Une voile éclate — le gréement souffre !"
                 : "Une voile se déchire dans la rafale !");
    }

    void StrainMast(ShipNode s, double dt, bool mine)
    {
        var ph = s.Physics;
        if (ph.Foundered || ph.SetFrac < 0.02) return;
        if (ph.SailLoad <= MastLoad * Config.CanvasStrength) return;   // en deçà, la toile suffit à payer
        double excess = ph.SailLoad / Config.CanvasStrength - 1;
        var (i, part) = s.HeaviestMast();
        if (i < 0 || part <= 0) return;
        if (_stormRng.NextDouble() > TearRate * MastRate * excess * excess * ph.SetFrac * part * dt) return;
        if (!s.DropMast(i)) return;
        if (mine) Say("Le mât est parti par-dessus bord !");
    }
}
