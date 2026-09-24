using Godot;
using System;

namespace NavalSim;

/// <summary>
/// LE MENU D'ÉCHAP — ce qu'on demande à un jeu quand on lève les yeux : reprendre,
/// enregistrer, régler, rentrer au port d'attache.
///
/// Il ne met RIEN en pause : la mer continue de courir derrière lui, et c'est
/// voulu — une partie qu'on enregistre est celle qu'on joue, à la seconde où on
/// l'enregistre, pas une image figée d'avant.
/// </summary>
public partial class ShipDemo
{
    PanelContainer? _pause;
    Label? _pauseNote;

    void BuildPause(CanvasLayer layer)
    {
        _pause = new PanelContainer
        {
            AnchorLeft = 0.5f, AnchorRight = 0.5f, AnchorTop = 0.5f, AnchorBottom = 0.5f,
            OffsetLeft = -170, OffsetRight = 170, OffsetTop = -150,
            Visible = false
        };
        _pause.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.04f, 0.06f, 0.09f, 0.92f),
            BorderColor = new Color(0.55f, 0.44f, 0.20f, 0.9f),
            BorderWidthLeft = 1, BorderWidthRight = 1, BorderWidthTop = 1, BorderWidthBottom = 1,
            CornerRadiusTopLeft = 8, CornerRadiusTopRight = 8,
            CornerRadiusBottomLeft = 8, CornerRadiusBottomRight = 8,
            ContentMarginLeft = 18, ContentMarginRight = 18, ContentMarginTop = 14, ContentMarginBottom = 16
        });
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 8);
        _pause.AddChild(box);
        layer.AddChild(_pause);

        var title = new Label { Text = "Au repos", HorizontalAlignment = HorizontalAlignment.Center };
        title.AddThemeFontSizeOverride("font_size", 24);
        title.AddThemeColorOverride("font_color", new Color(0.92f, 0.78f, 0.36f));
        if (HandFont.Get() is { } hand) title.AddThemeFontOverride("font", hand);
        box.AddChild(title);

        void Entry(string text, Action go)
        {
            var b = new Button { Text = text, FocusMode = Control.FocusModeEnum.None, Flat = true };
            b.AddThemeFontSizeOverride("font_size", 18);
            b.Pressed += go;
            box.AddChild(b);
        }

        Entry("Reprendre la mer", () => TogglePause());
        Entry("Enregistrer la partie", () => { SaveGame(); PauseNote(); });
        Entry("Réglages", () => { _pause!.Visible = false; _menu.Visible = true; });
        // en passant, et non sur demande : une partie libre neuve n'est pas écrite
        Entry("Menu principal", () => { _pause!.Visible = false; SaveGame(false); Open(); MainItems(); });
        Entry("Quitter le jeu", () => { SaveGame(false); GetTree().Quit(); });

        _pauseNote = new Label { HorizontalAlignment = HorizontalAlignment.Center };
        _pauseNote.AddThemeFontSizeOverride("font_size", 12);
        _pauseNote.AddThemeColorOverride("font_color", new Color(0.70f, 0.74f, 0.78f));
        box.AddChild(_pauseNote);
    }

    /// <summary>Ce que le menu dit de la partie : son nom, et quand elle a été écrite.</summary>
    void PauseNote()
    {
        if (_pauseNote == null) return;
        _pauseNote.Text = _gameId.Length == 0
            ? "Partie non enregistrée"
            : $"Partie « {Where()} », {_calendar.Date:dd/MM/yyyy}";
    }

    void TogglePause()
    {
        if (_pause == null || _inTitle) return;
        _pause.Visible = !_pause.Visible;
        if (_pause.Visible) { _menu.Visible = false; PauseNote(); }
    }
}
