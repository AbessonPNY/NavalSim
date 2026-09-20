using Godot;
using System;
using System.Collections.Generic;

namespace NavalSim;

/// <summary>
/// L'ÉCRAN DE TITRE — ce que la page n'a jamais eu : on n'y tombait pas dans un
/// jeu, on y tombait dans une simulation déjà lancée.
///
/// Son fond n'est pas une image : c'est la simulation elle-même, vue de sous la
/// quille, avec le trésor qui descend en travers du cadre. Rien n'est arrêté
/// derrière — la mer travaille, la coque roule, les pièces voltigent —, si bien
/// que « Jouer » n'a qu'à rendre la caméra et le monde est déjà chaud.
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

    CanvasLayer? _titleLayer;
    readonly List<Button> _titleItems = new();
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
        var box = new VBoxContainer
        {
            AnchorLeft = 1, AnchorRight = 1, AnchorTop = 0.5f, AnchorBottom = 0.5f,
            OffsetLeft = -420, OffsetRight = -110, OffsetTop = -40, OffsetBottom = 160,
            Alignment = BoxContainer.AlignmentMode.Center
        };
        box.AddThemeConstantOverride("separation", 4);
        root.AddChild(box);

        void Item(string text, Action go)
        {
            var b = new Button
            {
                Text = text, Flat = true, FocusMode = Control.FocusModeEnum.None,
                Alignment = HorizontalAlignment.Right
            };
            if (font != null) b.AddThemeFontOverride("font", font);
            b.AddThemeFontSizeOverride("font_size", 44);
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

        Item("Jouer", Play);
        Item("Options", () => { _menu.Visible = true; });
        Item("Crédits", () => { if (_credits != null) _credits.Visible = !_credits.Visible; });
        Item("Quitter", () => GetTree().Quit());

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

    /// <summary>Le titre paraît : le tableau de bord s'efface et l'œil plonge sous la quille.</summary>
    void Open()
    {
        _inTitle = true;
        _titleLayer!.Visible = true;
        // rien du tableau de bord : le titre n'est pas une partie en cours
        _info.Visible = false;
        _sunPanel.Visible = false;
        _note.Visible = false;
        _titleAng = 0.7;
        ShowPick();
    }

    /// <summary>« Jouer » : la caméra est rendue, et le monde tourne déjà.</summary>
    void Play()
    {
        _inTitle = false;
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
                return true;
        }
        return false;
    }

    /// <summary>
    /// Une image de titre : l'œil fait lentement le tour de l'étrave par en
    /// dessous, et la cale lâche son or par poignées. Le semis est réglé pour
    /// tenir sous le plafond de CoinNode — huit pièces par seconde contre une
    /// vie moyenne de trente-sept, soit trois cents en vol.
    /// </summary>
    void TitleTick(double dt)
    {
        var b = _ship.Physics.Body;
        _titleAng += dt * 0.055;                       // un tour en moins de deux minutes
/* PRÈS de la coque, et c'est tout le réglage : à onze mètres, les pièces
           qui tombent de la cale sont déjà mangées par l'absorption avant d'avoir
           traversé le cadre. À six, elles passent grandes et dorées entre l'œil
           et le bordé. */
        double r = 6.5, depth = 2.2;
        var eye = new Vector3(
            (float)(b.Pos.X + Math.Cos(_titleAng) * r),
            (float)(b.Pos.Y - depth),
            (float)(b.Pos.Z + Math.Sin(_titleAng) * r));
        // le regard porte sur la coque, un peu au-dessus de l'œil : on voit la
        // quille se découper sur la fenêtre de Snell
        _fixEye = eye;
        // un peu plus bas que l'œil : la quille se découpe en haut du cadre et
        // laisse toute la moitié basse à la colonne de pièces qui descend
        _fixLook = new Vector3((float)b.Pos.X, (float)(b.Pos.Y - 3.6), (float)b.Pos.Z);

        _titleSow += dt;
        if (_titleSow > 1.6)
        {
            _titleSow = 0;
            _coins.Spill(new Vector3((float)b.Pos.X, (float)b.Pos.Y, (float)b.Pos.Z),
                _ship.Spec.L, _ship.Spec.B, 26);
        }
    }
}
