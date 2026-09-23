using Godot;
using System;

namespace NavalSim;

/// <summary>
/// LE MÉMENTO DES COMMANDES, sous F1 — le pendant du <c>#keysMenu</c> de la
/// page, avec ses sections et son esprit : on ne cherche pas une touche dans un
/// paragraphe, on la trouve dans sa colonne.
///
/// Il existe parce que le tableau de bord les portait toutes, sur deux lignes
/// qui couraient d'un bord à l'autre de l'image. Un instrument qu'on lit d'un
/// coup d'œil ne peut pas être aussi la notice : les instruments restent en
/// haut à gauche, la notice s'ouvre quand on la demande.
///
/// Les intitulés prennent l'anglaise du projet, le reste la chasse fixe : une
/// cursive ne se BALAIE pas, et une touche se cherche du regard.
/// </summary>
public partial class ShipDemo : Node3D
{
    PanelContainer? _keys;

    void BuildKeys(CanvasLayer layer)
    {
        _keys = new PanelContainer
        {
            AnchorLeft = 0.5f, AnchorRight = 0.5f, AnchorTop = 0.5f, AnchorBottom = 0.5f,
            OffsetLeft = -560, OffsetRight = 560, OffsetTop = -270, OffsetBottom = 270,
            Visible = false
        };
        _keys.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.03f, 0.05f, 0.08f, 0.93f),
            CornerRadiusTopLeft = 10, CornerRadiusTopRight = 10,
            CornerRadiusBottomLeft = 10, CornerRadiusBottomRight = 10,
            ContentMarginLeft = 26, ContentMarginRight = 26, ContentMarginTop = 18, ContentMarginBottom = 18
        });
        layer.AddChild(_keys);

        var page = new VBoxContainer();
        page.AddThemeConstantOverride("separation", 10);
        _keys.AddChild(page);

        var font = Cursive();
        var head = new HBoxContainer();
        var title = new Label { Text = "Commandes", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        if (font != null) title.AddThemeFontOverride("font", font);
        title.AddThemeFontSizeOverride("font_size", 42);
        title.AddThemeColorOverride("font_color", new Color(0.96f, 0.96f, 0.94f));
        head.AddChild(title);
        var close = new Label
        {
            Text = "F1 ou Échap pour fermer",
            VerticalAlignment = VerticalAlignment.Bottom
        };
        close.AddThemeFontSizeOverride("font_size", 13);
        close.AddThemeColorOverride("font_color", new Color(0.62f, 0.70f, 0.78f));
        head.AddChild(close);
        page.AddChild(head);

        var cols = new HBoxContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        cols.AddThemeConstantOverride("separation", 34);
        page.AddChild(cols);

        VBoxContainer Column()
        {
            var c = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            c.AddThemeConstantOverride("separation", 4);
            cols.AddChild(c);
            return c;
        }
        void Section(VBoxContainer c, string name)
        {
            var l = new Label { Text = name };
            if (font != null) l.AddThemeFontOverride("font", font);
            l.AddThemeFontSizeOverride("font_size", 26);
            l.AddThemeColorOverride("font_color", new Color(0.86f, 0.80f, 0.58f));
            if (c.GetChildCount() > 0) l.AddThemeConstantOverride("line_spacing", 16);
            c.AddChild(l);
        }
        void Key(VBoxContainer c, string keys, string what)
        {
            var row = new HBoxContainer();
            var k = new Label { Text = keys, CustomMinimumSize = new Vector2(112, 0) };
            k.AddThemeFontSizeOverride("font_size", 14);
            k.AddThemeColorOverride("font_color", new Color(0.99f, 0.93f, 0.74f));
            var d = new Label { Text = what, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            d.AddThemeFontSizeOverride("font_size", 14);
            d.AddThemeColorOverride("font_color", new Color(0.88f, 0.91f, 0.94f));
            row.AddChild(k);
            row.AddChild(d);
            c.AddChild(row);
        }

        var a = Column();
        Section(a, "Manœuvre");
        Key(a, "W  S", "machine, en avant et en arrière");
        Key(a, "A  D", "barre à bâbord, à tribord");
        Key(a, "Q  E", "choquer, border les écoutes");
        Key(a, "V", "établir ou ferler");
        Key(a, "⇧V", "prendre un ris : huniers, bas ris, tout serré");
        Key(a, "B", "l'élan : en route d'un coup, ou stop (B B : vitesse doublée)");
        Key(a, "M", "mouiller · virer au cabestan");
        Section(a, "Artillerie");
        Key(a, "G", "un coup de la pièce suivante");
        Key(a, "G tenu", "la bordée entière");
        Key(a, "⇧G", "tirer de l'autre bord");
        Key(a, "Tab", "changer le bord en batterie");
        Section(a, "Avaries");
        Key(a, "R", "réparer et renflouer");
        Key(a, "Y", "faire sauter la soute");

        var b = Column();
        Section(b, "Le temps qu'il fait");
        Key(b, "T", "météo d'elle-même, ou à la main");
        Key(b, "↑  ↓", "force du vent");
        Key(b, "←  →", "d'où il souffle");
        Key(b, "PgUp PgDn", "creux de la houle");
        Key(b, "J", "aller au gros temps");
        Section(b, "Rencontres");
        Key(b, "U", "une voile sous pavillon noir");
        Key(b, "K", "le kraken, tout de suite");
        Key(b, "⇧K", "une baleine, qui vient charger");
        Key(b, "⇧J", "une averse, et le serpent de mer");
        Key(b, "⇧T", "la brume de surface, tout de suite");
        Key(b, "P", "la flotte fantôme");
        Key(b, "⇧P", "amener ou hisser le pavillon");
        Key(b, "⇧N", "la flotte : mettre à l'eau, retirer");

        var c2 = Column();
        Section(c2, "Vues");
        Key(c2, "C", "changer de vue");
        Key(c2, "X", "replanter la caméra ici");
        Key(c2, "F", "la caméra suit, ou non");
        Key(c2, "L", "la lunette");
        Key(c2, "molette", "ouvrir ou fermer la focale");
        Section(c2, "Affichage");
        Key(c2, "H", "le pavé de débogage : tirant, fond, déplacement");
        Key(c2, "⇧H", "masquer les instruments");
        Key(c2, "⇧M", "la mer : mise au point");
        Key(c2, "O", "occlusion ambiante");
        Key(c2, "I", "la carte du capitaine");
        Key(c2, StowKeyName, "le plan d'arrimage");
        Key(c2, "F1", "ce mémento");
        Key(c2, "Échap", "les options");
    }

    /// <summary>Ouvrir ou fermer le mémento. Rend vrai s'il était ouvert et qu'on vient de le fermer.</summary>
    bool CloseKeys()
    {
        if (_keys == null || !_keys.Visible) return false;
        Show(false);
        return true;
    }

    void ToggleKeys()
    {
        if (_keys == null) return;
        Show(!_keys.Visible);
    }

    /* LES INSTRUMENTS S'EFFACENT PENDANT QU'ON LIT. Ils sont dessinés par-dessus
       ce panneau — l'ordre des enfants du calque en décide, et le tableau de bord
       y est depuis le début —, et leurs chiffres tombaient en travers des
       colonnes. Plutôt que de jouer avec cet ordre, on range ce qu'on ne lit pas
       pendant qu'on lit autre chose ; leur état d'avant est rendu à la
       fermeture, sinon H perdrait son effet. */
    bool _hidWhenKeys;

    void Show(bool on)
    {
        if (_keys == null) return;
        if (on && !_keys.Visible)
        {
            _hidWhenKeys = _hudOn;
            _hudOn = false;
            _sunPanel.Visible = false;
            _menu.Visible = false;              // un panneau à la fois
        }
        else if (!on && _keys.Visible && _hidWhenKeys)
        {
            _hudOn = true;
            _sunPanel.Visible = true;
        }
        _keys.Visible = on;
    }
}
