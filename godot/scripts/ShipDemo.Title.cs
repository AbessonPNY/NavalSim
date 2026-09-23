using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using NavalSim.Core;

namespace NavalSim;

/// <summary>
/// L'ÉCRAN DE TITRE — ce que la page n'a jamais eu : on n'y tombait pas dans un
/// jeu, on y tombait dans une simulation déjà lancée.
///
/// Son fond n'est pas une image : c'est la simulation elle-même — le navire
/// sous voiles, par beau temps, au milieu de l'océan, vu de loin et au ras de
/// l'eau, la mise au point à deux mètres de l'objectif : l'eau toute proche est
/// nette, le navire flou dans le lointain. Rien n'est arrêté derrière, si bien
/// qu'entrer dans le jeu n'a qu'à rendre la caméra.
///
/// JEU LIBRE : le port de départ, sans quête. HISTOIRE : le premier chapitre
/// qu'on n'a pas fini (les fiches `kind: story`, dans l'ordre de `chapter`).
/// MISSIONS : les autres quêtes, à choisir. Chacun des trois garde ses propres
/// parties enregistrées : une mission ne va pas dans la liste de l'Histoire.
///
/// Il ne redouble RIEN : « Options » ouvre le menu d'Échap, qui existe et qui
/// est complet. Un second jeu de réglages aurait dérivé du premier en trois
/// semaines.
/// </summary>
public partial class ShipDemo : Node3D
{
    // PROVISOIRE, en attendant qu'il soit baptisé : deux constantes à changer.
    const string GameTitle = "NavalSim";
    const string GameSub = "une simulation à la voile";

    /// <summary>Le navire de l'affiche : sa fiche dans ships/.</summary>
    const string TitleShip = "frigate17e";
    int _beforeTitle = -1;
    CanvasLayer? _titleLayer;
    readonly List<Button> _titleItems = new();
    VBoxContainer? _titleBox;
    FontFile? _titleFont;
    Label? _credits;
    int _titlePick;
    bool _inTitle;
    double _titleAng, _titleSow;
    /// <summary>La ligne de commande tient la caméra (--eye) : « Jouer » ne la lui reprend pas.</summary>
    bool _planted;
    /// <summary>Ce que --titre demande ; rien, et c'est l'écran de titre, sauf capture ou caméra imposée.</summary>
    bool? _askTitle;

    /// <summary>
    /// L'anglaise du projet, la MÊME que celle de la page — celle du menu de F1.
    /// Chargée depuis css/fonts, hors du projet Godot : une fonte, deux versions.
    /// Le journal dit pourquoi elle ne sert qu'aux titres ; trois intitulés de
    /// trois mots en sont, une liste de réglages n'en est pas.
    /// </summary>
    static FontFile? Cursive()
    {
        string p = System.IO.Path.GetFullPath(System.IO.Path.Combine(
            ProjectSettings.GlobalizePath("res://"), "..", "css", "fonts", "estonia-latin.woff2"));
        if (!System.IO.File.Exists(p)) return null;
        var f = new FontFile();
        return f.LoadDynamicFont(p) == Error.Ok ? f : null;
    }

    void BuildTitle()
    {
        /* SOUS le masque de cinéma (couche 0) et au-dessus de la mer : le titre
           tient dans le cadre du scope, il ne passe pas par-dessus les bandes. */
        _titleLayer = new CanvasLayer { Layer = -1, Visible = false };
        AddChild(_titleLayer);
        var root = new Control { AnchorRight = 1, AnchorBottom = 1, MouseFilter = Control.MouseFilterEnum.Ignore };
        _titleLayer.AddChild(root);

        var font = Cursive();
        // un voile très mince : le fond doit rester lisible, le texte aussi
        var veil = new ColorRect
        {
            AnchorRight = 1, AnchorBottom = 1, Color = new Color(0.02f, 0.04f, 0.07f, 0.22f),
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        root.AddChild(veil);

        var title = new Label { Text = GameTitle, Position = new Vector2(64, 96) };
        if (font != null) title.AddThemeFontOverride("font", font);
        title.AddThemeFontSizeOverride("font_size", 78);
        title.AddThemeColorOverride("font_color", new Color(0.97f, 0.97f, 0.95f));
        title.AddThemeColorOverride("font_outline_color", new Color(0, 0.02f, 0.04f, 0.9f));
        title.AddThemeConstantOverride("outline_size", 10);
        root.AddChild(title);

        var sub = new Label { Text = GameSub, Position = new Vector2(70, 186) };
        sub.AddThemeFontSizeOverride("font_size", 15);
        sub.AddThemeColorOverride("font_color", new Color(0.80f, 0.86f, 0.90f, 0.85f));
        sub.AddThemeColorOverride("font_outline_color", new Color(0, 0.02f, 0.04f, 0.9f));
        sub.AddThemeConstantOverride("outline_size", 5);
        root.AddChild(sub);

        // les entrées, à droite comme sur la maquette
        var box = _titleBox = new VBoxContainer
        {
            AnchorLeft = 1, AnchorRight = 1, AnchorTop = 0.5f, AnchorBottom = 0.5f,
            OffsetLeft = -420, OffsetRight = -110, OffsetTop = -40, OffsetBottom = 160,
            Alignment = BoxContainer.AlignmentMode.Center
        };
        box.AddThemeConstantOverride("separation", 4);
        root.AddChild(box);

        _titleFont = font;
        MainItems();

        /* La licence de l'anglaise VOYAGE AVEC ELLE : c'est la condition de la
           SIL OFL, et l'oublier est la manière discrète de ne pas la respecter.
           Le texte complet est dans css/fonts/OFL.txt. */
        _credits = new Label
        {
            Text = "Portage Godot d'une page three.js — physique en C# pur, vérifiée\n"
                 + "contre le JS à chaque commit.\n\n"
                 + "Godot Engine · licence MIT\n"
                 + "Estonia, l'anglaise des titres · SIL Open Font License 1.1\n"
                 + "(Copyright 2010-2021 The Estonia Project Authors)",
            Position = new Vector2(64, 260), Visible = false
        };
        _credits.AddThemeFontSizeOverride("font_size", 14);
        _credits.AddThemeColorOverride("font_color", new Color(0.88f, 0.92f, 0.95f));
        _credits.AddThemeColorOverride("font_outline_color", new Color(0, 0.02f, 0.04f, 0.9f));
        _credits.AddThemeConstantOverride("outline_size", 5);
        root.AddChild(_credits);

        /* IL NE S'OUVRE PAS TOUJOURS. Une capture, un essai à caméra imposée, un
           banc : tous veulent le jeu, pas son affiche. Et comme ceci est construit
           APRÈS la ligne de commande, l'ouverture ne peut plus rallumer un panneau
           que --masquer vient d'éteindre — ce qu'elle faisait. */
        if (_askTitle ?? !_planted) Open();
    }

    /// <summary>Une entrée du titre : grande, en anglaise, à droite.</summary>
    void Item(string text, Action go, int size = 44)
    {
        var box = _titleBox!;
        var font = _titleFont;
        {
            var b = new Button
            {
                Text = text, Flat = true, FocusMode = Control.FocusModeEnum.None,
                Alignment = HorizontalAlignment.Right
            };
            if (font != null) b.AddThemeFontOverride("font", font);
            b.AddThemeFontSizeOverride("font_size", size);
            foreach (var s in new[] { "font_color", "font_hover_color", "font_pressed_color", "font_focus_color" })
                b.AddThemeColorOverride(s, new Color(0.96f, 0.96f, 0.94f));
            b.AddThemeColorOverride("font_outline_color", new Color(0, 0.02f, 0.04f, 0.9f));
            b.AddThemeConstantOverride("outline_size", 8);
            int me = _titleItems.Count;
            // la souris DÉPLACE le choix au lieu d'avoir son propre survol : un
            // seul état sélectionné, que la souris et les flèches partagent
            b.MouseEntered += () => { _titlePick = me; ShowPick(); };
            b.Pressed += go;
            box.AddChild(b);
            _titleItems.Add(b);
        }
    }

    void ClearItems()
    {
        foreach (var b in _titleItems) b.QueueFree();
        _titleItems.Clear();
        _titlePick = 0;
    }

    void MainItems()
    {
        ClearItems();
        Item("Jeu libre", FreeItems);
        Item("Histoire", StoryItems);
        Item("Missions", MissionItems);
        Item("Options", () => { _menu.Visible = true; });
        Item("Crédits", () => { if (_credits != null) _credits.Visible = !_credits.Visible; });
        Item("Quitter", () => GetTree().Quit());
        ShowPick();
    }

    /// <summary>
    /// LES MISSIONS ont leurs parties comme les deux autres modes : on reprend
    /// celle qu'on avait laissée, ou on en commence une neuve. Sans partie
    /// enregistrée, la liste des missions s'ouvre tout de suite — il n'y a rien
    /// à choisir avant.
    /// </summary>
    void MissionItems()
    {
        if (Saves("mission").Count == 0) { MissionPick(); return; }
        GameItems("mission", "Nouvelle mission", MissionPick);
    }

    /// <summary>Les missions qu'on peut commencer, et le retour.</summary>
    void MissionPick()
    {
        ClearItems();
        if (_quests != null)
            foreach (var q in _quests.List)
            {
                if (q.Kind == "story") continue;
                var id = q.Id;
                string done = _quests.Done.Contains(id) ? "  ✓" : "";
                Item((q.Title.Length > 0 ? q.Title : q.Id) + done, () => StartQuest(id), 30);
            }
        if (_titleItems.Count == 0) Item("Aucune mission", () => { }, 30);
        // on revient d'où l'on venait : la liste des parties s'il y en a
        Item("Retour", () => { if (Saves("mission").Count > 0) MissionItems(); else MainItems(); }, 34);
        ShowPick();
    }

    /// <summary>Jeu libre : au port de départ, sans quête.</summary>
    void FreePlay()
    {
        _quests?.Stop();
        SaveQuests();
        Home();
        Play();
    }

    /// <summary>L'histoire : le premier chapitre qu'on n'a pas fini, sinon le dernier.</summary>
    void Story()
    {
        if (_quests == null) { FreePlay(); return; }
        QuestSpec? next = null, last = null;
        foreach (var q in _quests.List.OrderBy(q => q.Chapter))
        {
            if (q.Kind != "story") continue;
            last = q;
            if (next == null && !_quests.Done.Contains(q.Id)) next = q;
        }
        var pick = next ?? last;
        if (pick == null) { FreePlay(); return; }
        StartQuest(pick.Id);
    }

    void StartQuest(string id)
    {
        if (_quests == null) return;
        _msgs.Clear();
        if (_msgBox != null) _msgBox.Visible = false;
        Home();
        _quests.Start(id);
        SaveQuests();
        Play();
    }

    /* RETOUR AU PORT : l'affiche a posé le navire au large ; on le remet à son
       poste, droit et sans erre, avec le temps qu'il fait. */
    void Home()
    {
        // le navire du joueur, si l'affiche en avait mis un autre
        if (_beforeTitle >= 0 && _beforeTitle != _index) Launch(_beforeTitle);
        _beforeTitle = -1;
        /* UNE PARTIE NEUVE PART SEULE. Revenir au menu principal depuis une partie
           laissait les coques à flot, et le jeu libre les retrouvait — un pirate au
           mouillage dès le premier jour (signalé). On rend donc le bord à son état
           du premier matin : personne autour, la bourse pleine, dix heures, la
           coque saine, la toile serrée et les couleurs hautes. */
        foreach (var other in new List<ShipNode>(_others)) RemoveShip(other);
        _purse = new Purse(Market.Depart);
        _gameId = "";
        var b = _ship.Physics.Body;
        b.Vel = Vec3d.Zero;
        b.AngVel = Vec3d.Zero;
        b.Pos = new Vec3d(b.Pos.X, _eqY, b.Pos.Z);
        _ship.Physics.Salvage();
        _ship.Physics.Powder = _ship.Physics.PowderMax;
        _ship.Physics.ClearCargo();
        _ship.Ctrl.SailsSet = false;
        _ship.Ctrl.Throttle = 0;
        _ship.Ctrl.Canvas = 1;
        _reef = 0;
        _colours = true;
        _ship.ShowColours(true, true);
        _sky.Core.SetTimeOfDay(10, _sky.Latitude);
        /* LE VENT DU DÉPART, TOUJOURS LE MÊME : belle brise (force 4) par 105°,
           ce qui donne du vent pour sortir du môle sans que ce soit une leçon de
           louvoyage à chaque partie. La météo d'elle-même reprend ensuite. */
        _force = 4; _windDeg = 105;
        Restate();
        Moor();
        _reck?.Fix(TruePos().X, TruePos().Z);
    }

    /// <summary>Le titre paraît : le tableau de bord s'efface, le navire est au large.</summary>
    void Open()
    {
        _inTitle = true;
        /* LE ROTER LÖWE POUR L'AFFICHE, quel que soit le navire du joueur : le
           galion de 1597, sa toile carrée et ses châteaux. Le navire du joueur lui
           est rendu à l'entrée dans le jeu. */
        _beforeTitle = _index;
        int lion = _paths.FindIndex(p => System.IO.Path.GetFileNameWithoutExtension(p) == TitleShip);
        if (lion >= 0 && lion != _index) Launch(lion);
        Offshore();
        TitleDof();
        _titleLayer!.Visible = true;
        // rien du tableau de bord : le titre n'est pas une partie en cours
        _info.Visible = false;
        _sunPanel.Visible = false;
        _note.Visible = false;
        _titleAng = 0.7;
        ShowPick();
    }

    /// <summary>Entrer dans le jeu : la caméra est rendue, et le monde tourne déjà.</summary>
    void Play()
    {
        _inTitle = false;
        ApplySettings();               // la mise au point du joueur, pas celle de l'affiche
        if (_titleLayer != null) _titleLayer.Visible = false;
        _info.Visible = true;
        _sunPanel.Visible = true;
        // la caméra imposée est relâchée — sauf si la ligne de commande en tenait une
        if (!_planted) { _fixEye = null; _fixLook = null; }
    }


    void ShowPick()
    {
        for (int i = 0; i < _titleItems.Count; i++)
        {
            bool on = i == _titlePick;
            var b = _titleItems[i];
            // le choix ne change pas de couleur mais d'ÉCLAT et de place : une
            // anglaise soulignée ou encadrée perd ses liaisons
            b.Modulate = on ? new Color(1f, 0.93f, 0.72f) : new Color(0.82f, 0.86f, 0.89f, 0.72f);
            b.AddThemeConstantOverride("outline_size", on ? 10 : 6);
        }
    }

    /// <summary>Les flèches et l'entrée, tant que le titre est là. Rend vrai s'il a pris la touche.</summary>
    bool TitleKey(Key key)
    {
        switch (key)
        {
            case Key.Up:   _titlePick = (_titlePick + _titleItems.Count - 1) % _titleItems.Count; ShowPick(); return true;
            case Key.Down: _titlePick = (_titlePick + 1) % _titleItems.Count; ShowPick(); return true;
            case Key.Enter or Key.KpEnter or Key.Space: _titleItems[_titlePick].EmitSignal(BaseButton.SignalName.Pressed); return true;
            // Échap referme ce qui est ouvert par-dessus, sinon il ne fait rien :
            // on ne quitte pas un jeu par mégarde depuis son écran de titre
            case Key.Escape:
                if (_menu.Visible) _menu.Visible = false;
                else if (_credits != null && _credits.Visible) _credits.Visible = false;
                else if (_titleItems.Count > 0 && _titleItems[^1].Text == "Retour") MainItems();
                return true;
        }
        return false;
    }

    /* AU LARGE, PAR BEAU TEMPS : l'atterrage de la région le plus proche du
       port de départ — de l'eau libre, loin des côtes, choisie pour cela. La
       mer tombe à force 3, le ciel se dégage, il est dix heures. */
    void Offshore()
    {
        if (_world != null && _world.StartPort is NavalSim.Core.Isle home)
        {
            NavalSim.Core.ApproachSpec? best = null;
            double bd = double.MaxValue;
            foreach (var a in _world.Region.Approaches)
            {
                var g = _world.Geo.ToXZ(a.Lat, a.Lon);
                double d = (g.X - home.X) * (g.X - home.X) + (g.Z - home.Z) * (g.Z - home.Z);
                if (d < bd) { bd = d; best = a; }
            }
            if (best != null)
            {
                var at = _world.Geo.ToXZ(best.Lat, best.Lon);
                var o = _sea.Core.Origin;
                _sea.Core.Rebase(at.X - o.X, at.Z - o.Z);
                var b = _ship.Physics.Body;
                b.Pos = new Vec3d(0, _eqY, 0);
                b.Vel = Vec3d.Zero;
                b.AngVel = Vec3d.Zero;
                // le vent à cent dix degrés de l'étrave, par bâbord : une allure portante, qui avance
                b.Quat = Quatd.FromAxisAngle(new Vec3d(0, 1, 0), -(75 - 110) * Math.PI / 180);
                _ship.SyncTransform();
            }
        }
        _weather.On = false;
        _force = 3; _windDeg = 75;
        _cloud = 0.06;
        Restate();
        _sky.Core.SetTimeOfDay(10, _sky.Latitude);
        _sky.Apply();
        /* il fait route, lentement : sous voiles bordées, ou à la machine pour une
           coque qui n'a pas de toile — le chaland restait planté (relevé : 0,0 m/s) */
        bool sails = _ship.Spec.SailArea > 0;
        _ship.Ctrl.SailsSet = sails;
        _ship.Ctrl.Sheet = 0.6;
        _ship.Ctrl.Throttle = sails ? 0 : 0.5;
    }

    /* LA MISE AU POINT DE L'AFFICHE : à deux mètres de l'objectif, et tout ce qui
       est au-delà s'estompe — le navire, à cent mètres, n'est plus qu'une forme.
       Posée sur la caméra le temps du titre ; les réglages du joueur la
       reprennent à l'entrée dans le jeu. */
    void TitleDof()
    {
        _camAttr.DofBlurNearEnabled = false;
        _camAttr.DofBlurFarEnabled = true;
        _camAttr.DofBlurFarDistance = 2.0f;
        _camAttr.DofBlurFarTransition = 6.0f;
        _camAttr.DofBlurAmount = 0.12f;
        _anamorphic.Enabled = false;
    }

    /// <summary>
    /// Une image de titre : l'œil, au ras de l'eau à cent mètres du navire,
    /// en fait lentement le tour — un tour en cinq minutes.
    /// </summary>
    void TitleTick(double dt)
    {
        var b = _ship.Physics.Body;
        _titleAng += dt * 0.021;
        double r = 95;
        float sea = (float)_sea.Core.Sample(b.Pos.X + Math.Cos(_titleAng) * r, b.Pos.Z + Math.Sin(_titleAng) * r, _t);
        var eye = new Vector3(
            (float)(b.Pos.X + Math.Cos(_titleAng) * r),
            sea + 1.6f,
            (float)(b.Pos.Z + Math.Sin(_titleAng) * r));
        _fixEye = eye;
        // le regard sur la coque, un peu au-dessus de la flottaison : la mâture dans le cadre
        _fixLook = new Vector3((float)b.Pos.X, (float)(b.Pos.Y + 0.25 * _ship.Spec.L), (float)b.Pos.Z);
    }
}
