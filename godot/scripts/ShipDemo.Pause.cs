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
    /// <summary>La question avant de partir, et ce qu'on fera ensuite.</summary>
    VBoxContainer? _pauseMain, _leaveAsk;
    Action? _leaveGo;

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
        var stack = new VBoxContainer();
        _pause.AddChild(stack);
        var box = _pauseMain = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 8);
        stack.AddChild(box);
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
        // une partie qui a un nom s'enregistre en passant ; une neuve DEMANDE (Leave)
        Entry("Menu principal", () => Leave(() => { _pause!.Visible = false; Open(); MainItems(); }));
        Entry("Quitter le jeu", () => Leave(() => GetTree().Quit()));

        _pauseNote = new Label { HorizontalAlignment = HorizontalAlignment.Center };
        _pauseNote.AddThemeFontSizeOverride("font_size", 12);
        _pauseNote.AddThemeColorOverride("font_color", new Color(0.70f, 0.74f, 0.78f));
        box.AddChild(_pauseNote);

        /* LA QUESTION, à la place des entrées : on ne part pas d'une partie neuve sans
           qu'on vous ait demandé si elle valait d'être gardée. */
        var ask = _leaveAsk = new VBoxContainer { Visible = false };
        ask.AddThemeConstantOverride("separation", 8);
        stack.AddChild(ask);
        var q = new Label { Text = "Enregistrer la partie avant de partir ?", HorizontalAlignment = HorizontalAlignment.Center, AutowrapMode = TextServer.AutowrapMode.WordSmart };
        q.AddThemeFontSizeOverride("font_size", 18);
        q.AddThemeColorOverride("font_color", new Color(0.92f, 0.78f, 0.36f));
        ask.AddChild(q);
        void Answer(string text, Action go)
        {
            var b = new Button { Text = text, FocusMode = Control.FocusModeEnum.None, Flat = true };
            b.AddThemeFontSizeOverride("font_size", 18);
            b.Pressed += go;
            ask.AddChild(b);
        }
        Answer("Enregistrer", () => { var go = _leaveGo; CloseAsk(); SaveGame(); go?.Invoke(); });
        Answer("Ne pas enregistrer", () => { var go = _leaveGo; CloseAsk(); go?.Invoke(); });
        Answer("Annuler", CloseAsk);
    }

    /// <summary>
    /// PARTIR D'UNE PARTIE (demandé : « le jeu doit me proposer de sauvegarder, pas le faire
    /// systématiquement quand c'est une nouvelle partie »). Une partie qui a déjà un nom —
    /// enregistrée une fois, ou reprise — s'enregistre en passant, comme avant. Une neuve,
    /// mission ou jeu libre, pose la question. Une escarmouche ne s'enregistre jamais.
    /// </summary>
    void Leave(Action go)
    {
        if (_skirmish || _gameId.Length > 0) { SaveGame(false); go(); return; }
        _leaveGo = go;
        _pauseMain!.Visible = false;
        _leaveAsk!.Visible = true;
    }

    void CloseAsk()
    {
        _leaveGo = null;
        if (_leaveAsk != null) _leaveAsk.Visible = false;
        if (_pauseMain != null) _pauseMain.Visible = true;
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
        CloseAsk();
        if (_pause.Visible) { _menu.Visible = false; PauseNote(); }
    }
}
