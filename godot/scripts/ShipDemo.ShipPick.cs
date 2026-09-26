using Godot;
using System;
using System.Collections.Generic;

namespace NavalSim;

/// <summary>
/// LE CHOIX DU NAVIRE, avant de prendre la mer en jeu libre.
///
/// On tombait jusqu'ici dans une partie avec le bateau qui se trouvait là —
/// celui de l'affiche, ou celui de la dernière fois. Or choisir sa coque EST le
/// premier acte d'une sortie libre : une goélette de 115 t et un galion de 300
/// ne font pas le même jeu, et on doit le décider en les regardant.
///
/// Le panneau s'ouvre À GAUCHE et le menu reste à DROITE (maquette) : on n'a
/// pas quitté le titre, on l'a déplié. Derrière, la mer continue de rouler et
/// le navire de l'affiche de tourner — rien ne s'arrête, c'est le principe de
/// cet écran depuis le premier jour.
///
/// Ce qu'on propose est une DONNÉE, `ships/libre.json`, et non la liste de ce
/// qui existe : le dossier fait foi pour les fiches, ce fichier pour ce que le
/// joueur peut prendre. Voir <see cref="ShipLibrary.FreeRoster"/>.
/// </summary>
public partial class ShipDemo : Node3D
{
    /* LA CARTE EST EN 16:9, comme une photo d'écran, et c'est voulu : le visuel
       d'un navire est une CAPTURE du jeu, pas une vignette carrée. Fournir plus
       grand ne coûte rien — l'image est ramenée à la carte en couvrant, donc un
       512×288 comme un 1920×1080 tombent juste. */
    const int CardW = 208, CardH = 117;
    const int CardCols = 3;

    Control? _titleRoot;
    PanelContainer? _pickPanel;
    GridContainer? _pickGrid;
    Label? _pickHint;
    readonly List<ShipLibrary.FreeShip> _roster = new();
    readonly List<PanelContainer> _cards = new();
    int _pickShip;
    bool _pickOpen;
    /// <summary>Le navire d'avant le panneau : « Retour » le rend.</summary>
    int _pickBefore = -1;
    ImageTexture? _cardBlank;

    /// <summary>
    /// JEU LIBRE : le panneau des navires, et le menu qui va avec.
    ///
    /// « Charger une partie » ne paraît que s'il y a quelque chose à charger, et
    /// ne regarde pas le choix : une partie enregistrée porte SON navire.
    /// </summary>
    void FreeItems()
    {
        _eraseId = "";
        BuildPick();
        OpenPick();
        ClearItems();
        Item("Prendre la mer", TakeSea, 34);
        if (Saves("libre").Count > 0)
            Item("Charger une partie", () => { ClosePick(); LoadItems("libre", "Nouvelle partie", NewFree); }, 34);
        Item("Retour", () => { ClosePick(); MainItems(); }, 34);
        ShowPick();
    }

    /// <summary>La coque choisie prend la mer — et c'est la seule porte du jeu libre.</summary>
    void TakeSea()
    {
        if (_roster.Count == 0) { ClosePick(); NewFree(); return; }
        var f = _roster[_pickShip];
        if (f.Locked.Length > 0) { Say(f.Locked); return; }
        int k = _paths.IndexOf(f.Path);
        if (k >= 0 && k != _index) Launch(k);
        /* ET C'EST ELLE QUE LE JEU GARDE. Home() rend le navire d'avant l'affiche
           (le galion y est monté tout seul pour poser) : on lui dit que celui
           d'avant est désormais celui qu'on vient de choisir, sinon le choix
           serait défait à la seconde même où il sert. */
        _pickBefore = _beforeTitle = k >= 0 ? k : _index;
        ClosePick();
        NewFree();
    }

    void BuildPick()
    {
        if (_pickPanel != null) return;

        _roster.Clear();
        _roster.AddRange(ShipLibrary.FreeRoster(_paths));

        var fond = new StyleBoxFlat
        {
            BgColor = new Color(0.06f, 0.09f, 0.13f, 0.72f),
            BorderColor = new Color(0.70f, 0.78f, 0.84f, 0.18f)
        };
        fond.SetBorderWidthAll(1);
        fond.SetCornerRadiusAll(3);
        fond.SetContentMarginAll(22);

        var panel = _pickPanel = new PanelContainer
        {
            AnchorLeft = 0.045f, AnchorRight = 0.78f, AnchorTop = 0.33f, AnchorBottom = 0.96f,
            Visible = false
        };
        panel.AddThemeStyleboxOverride("panel", fond);
        _titleRoot!.AddChild(panel);

        var col = new VBoxContainer();
        col.AddThemeConstantOverride("separation", 14);
        panel.AddChild(col);

        var titre = new Label { Text = "Choisissez votre navire :" };
        if (_titleFont != null) titre.AddThemeFontOverride("font", _titleFont);
        titre.AddThemeFontSizeOverride("font_size", 30);
        titre.AddThemeColorOverride("font_color", new Color(0.96f, 0.96f, 0.94f));
        titre.AddThemeColorOverride("font_outline_color", new Color(0, 0.02f, 0.04f, 0.9f));
        titre.AddThemeConstantOverride("outline_size", 6);
        col.AddChild(titre);

        var scroll = new ScrollContainer
        {
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled
        };
        col.AddChild(scroll);

        var grid = _pickGrid = new GridContainer { Columns = CardCols, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        grid.AddThemeConstantOverride("h_separation", 14);
        grid.AddThemeConstantOverride("v_separation", 16);
        scroll.AddChild(grid);

        for (int i = 0; i < _roster.Count; i++) grid.AddChild(Card(_roster[i], i));

        _pickHint = new Label
        {
            Text = "← → ↑ ↓ pour choisir   ·   Entrée pour prendre la mer   ·   Échap pour revenir"
        };
        _pickHint.AddThemeFontSizeOverride("font_size", 13);
        _pickHint.AddThemeColorOverride("font_color", new Color(0.78f, 0.84f, 0.89f, 0.75f));
        _pickHint.AddThemeColorOverride("font_outline_color", new Color(0, 0.02f, 0.04f, 0.9f));
        _pickHint.AddThemeConstantOverride("outline_size", 4);
        col.AddChild(_pickHint);

    }

    /// <summary>Une carte : le visuel, son nom dessous, et un cadre qui dit qu'elle est choisie.</summary>
    PanelContainer Card(ShipLibrary.FreeShip f, int i)
    {
        var box = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Stop };
        box.AddThemeStyleboxOverride("panel", CardStyle(false));

        var col = new VBoxContainer();
        col.AddThemeConstantOverride("separation", 7);
        box.AddChild(col);

        var img = new TextureRect
        {
            CustomMinimumSize = new Vector2(CardW, CardH),
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            Texture = CardImage(f),
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        /* Le visuel DÉBORDE de sa carte sans elle : une capture 16:9 couvre en
           rognant, et sans cela elle déborde sur ses voisines. */
        img.ClipContents = true;
        col.AddChild(img);

        var nom = new Label
        {
            Text = f.Caption, HorizontalAlignment = HorizontalAlignment.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        nom.AddThemeFontSizeOverride("font_size", 15);
        nom.AddThemeColorOverride("font_color", new Color(0.93f, 0.94f, 0.95f));
        nom.AddThemeColorOverride("font_outline_color", new Color(0, 0.02f, 0.04f, 0.9f));
        nom.AddThemeConstantOverride("outline_size", 4);
        col.AddChild(nom);

        /* CE QU'ON NE PEUT PAS ENCORE PRENDRE SE VOIT QUAND MÊME, et c'est le
           point : une coque cachée ne se convoite pas. Elle pâlit, et la raison
           s'écrit sous le nom. */
        if (f.Locked.Length > 0)
        {
            img.Modulate = new Color(0.45f, 0.48f, 0.52f, 0.85f);
            nom.Modulate = new Color(0.72f, 0.74f, 0.76f);
            var raison = new Label
            {
                Text = f.Locked, HorizontalAlignment = HorizontalAlignment.Center,
                AutowrapMode = TextServer.AutowrapMode.WordSmart,
                CustomMinimumSize = new Vector2(CardW, 0),
                MouseFilter = Control.MouseFilterEnum.Ignore
            };
            raison.AddThemeFontSizeOverride("font_size", 12);
            raison.AddThemeColorOverride("font_color", new Color(0.86f, 0.74f, 0.52f, 0.9f));
            raison.AddThemeColorOverride("font_outline_color", new Color(0, 0.02f, 0.04f, 0.9f));
            raison.AddThemeConstantOverride("outline_size", 4);
            col.AddChild(raison);
        }

        int me = i;
        // la souris DÉPLACE le choix, comme les entrées du titre : un seul état
        // sélectionné, que la souris et les flèches se partagent
        box.MouseEntered += () => { _pickShip = me; ShowCards(); };
        box.GuiInput += e =>
        {
            if (e is InputEventMouseButton mb && mb.Pressed && mb.ButtonIndex == MouseButton.Left)
            {
                _pickShip = me; ShowCards();
                // le second clic sur la même carte vaut « je la prends » : un
                // premier clic qui lancerait la partie serait un piège
                if (mb.DoubleClick) TakeSea();
            }
        };
        _cards.Add(box);
        return box;
    }

    static StyleBoxFlat CardStyle(bool on)
    {
        var s = new StyleBoxFlat
        {
            BgColor = on ? new Color(0.16f, 0.22f, 0.28f, 0.85f) : new Color(0.08f, 0.11f, 0.15f, 0.55f),
            BorderColor = on ? new Color(1f, 0.93f, 0.72f, 0.95f) : new Color(0.70f, 0.78f, 0.84f, 0.22f)
        };
        s.SetBorderWidthAll(on ? 2 : 1);
        s.SetCornerRadiusAll(2);
        s.SetContentMarginAll(6);
        return s;
    }

    /// <summary>Le visuel d'un navire, ou la carte peinte quand il n'y en a pas encore.</summary>
    Texture2D? CardImage(ShipLibrary.FreeShip f)
    {
        if (f.Image.Length > 0)
        {
            var img = Image.LoadFromFile(f.Image);
            if (img != null) return ImageTexture.CreateFromImage(img);
            GD.PushWarning($"visuel illisible : {f.Image}");
        }
        /* RIEN N'EST CASSÉ TANT QUE LE VISUEL MANQUE : une carte peinte, ciel sur
           mer, la même pour toutes — le nom est dessous, il suffit à choisir. Le
           jour où l'image arrive, elle la remplace sans qu'on touche au code. */
        if (_cardBlank == null)
        {
            var img = Image.CreateEmpty(CardW, CardH, false, Image.Format.Rgb8);
            for (int y = 0; y < CardH; y++)
            {
                float t = y / (float)(CardH - 1);
                // l'horizon aux deux tiers, comme sur une capture au ras de l'eau
                var c = t < 0.62f
                    ? new Color(0.42f, 0.55f, 0.66f).Lerp(new Color(0.68f, 0.76f, 0.82f), t / 0.62f)
                    : new Color(0.14f, 0.22f, 0.30f).Lerp(new Color(0.07f, 0.12f, 0.18f), (t - 0.62f) / 0.38f);
                for (int x = 0; x < CardW; x++) img.SetPixel(x, y, c);
            }
            _cardBlank = ImageTexture.CreateFromImage(img);
        }
        return _cardBlank;
    }

    void ShowCards()
    {
        for (int i = 0; i < _cards.Count; i++)
            _cards[i].AddThemeStyleboxOverride("panel", CardStyle(i == _pickShip));
        if (_pickHint != null && _roster.Count > 0)
        {
            var f = _roster[_pickShip];
            _pickHint.Text = f.Locked.Length > 0
                ? "Celle-là ne se prend pas encore   ·   ← → ↑ ↓ pour choisir   ·   Échap pour revenir"
                : "← → ↑ ↓ pour choisir   ·   Entrée pour prendre la mer   ·   Échap pour revenir";
        }
    }

    void OpenPick()
    {
        if (_pickPanel == null) return;
        /* LE NAVIRE D'AVANT EST LE VÔTRE, PAS CELUI DE L'AFFICHE. Le galion monte
           à bord tout seul pour poser (voir Open), et c'est _beforeTitle qui
           garde le vrai : prendre _index ici ferait que « Retour » vous rendrait
           le galion de la couverture au lieu de votre bord. */
        if (!_pickOpen) _pickBefore = _beforeTitle >= 0 ? _beforeTitle : _index;
        _pickOpen = true;
        _pickPanel.Visible = true;
        // et on arrive SUR lui : le panneau s'ouvre là où l'on est
        int mien = _pickBefore >= 0 && _pickBefore < _paths.Count
            ? _roster.FindIndex(f => f.Path == _paths[_pickBefore]) : -1;
        if (mien >= 0) _pickShip = mien;
        else _pickShip = Math.Min(_pickShip, Math.Max(0, _roster.Count - 1));
        ShowCards();
    }

    void ClosePick()
    {
        if (_pickPanel != null) _pickPanel.Visible = false;
        _pickOpen = false;
    }

    /// <summary>
    /// LES FLÈCHES VONT AU PANNEAU tant qu'il est ouvert, le menu de droite
    /// restant à la souris : deux navigations au clavier dans le même écran se
    /// disputeraient la même touche. Rend vrai s'il a pris la touche.
    /// </summary>
    bool PickKey(Key key)
    {
        if (!_pickOpen || _roster.Count == 0) return false;
        int n = _roster.Count;
        switch (key)
        {
            case Key.Left:  _pickShip = (_pickShip + n - 1) % n; ShowCards(); return true;
            case Key.Right: _pickShip = (_pickShip + 1) % n; ShowCards(); return true;
            case Key.Up:    _pickShip = (_pickShip + n - CardCols) % n; ShowCards(); return true;
            case Key.Down:  _pickShip = (_pickShip + CardCols) % n; ShowCards(); return true;
            case Key.Enter or Key.KpEnter: TakeSea(); return true;
            case Key.Escape:
                // on rend le navire d'avant : le panneau n'a rien décidé
                if (_pickBefore >= 0) _beforeTitle = _pickBefore;
                ClosePick(); MainItems(); return true;
        }
        return false;
    }
}
