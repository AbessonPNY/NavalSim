using Godot;
using System;
using System.Collections.Generic;
using NavalSim.Core;

namespace NavalSim;

/// <summary>
/// CE QU'ON LIT EN MER : un navire parlé, une bouteille repêchée.
///
/// LE MÊME ENCART POUR LES DEUX, comme dans la page, et pas le cadre du milieu
/// des quêtes : il ne met pas le navire en panne et ne prend pas la barre — il
/// se pose dans un coin, et c'est un moment à décider avec de l'erre sous la
/// quille. Un clic le ferme ; sinon il s'efface de lui-même.
///
/// UN NAVIRE PARLÉ AU LARGE dit ce qu'il a vu d'un port : frais, VRAI, et vague.
/// Pas de chiffre — un chiffre se compare, une appréciation se pèse — et
/// seulement EN MER : à quai, le comptoir dit mieux et pour rien. Le risque de
/// dérouter sur la foi d'une rumeur n'a rien demandé à écrire : les dépressions
/// sont une fonction pure de la position et de l'heure.
/// </summary>
public partial class ShipDemo : Node3D
{
    PanelContainer? _encart;
    Label? _encartTitle, _encartText;
    double _encartLeft;
    readonly RandomNumberGenerator _rumourRng = new();
    double _rumourIn = -1;

    /// <summary>Le temps qu'un encart reste ouvert si personne ne le ferme.</summary>
    const double EncartLife = 40;

    void BuildEncart(CanvasLayer layer)
    {
        _encart = new PanelContainer
        {
            /* en bas à GAUCHE, au ras du bord : le comptoir tient la droite, les
               instruments le haut, et il grandit vers le haut sans les rejoindre */
            AnchorLeft = 0, AnchorRight = 0, AnchorTop = 1, AnchorBottom = 1,
            OffsetLeft = 14, OffsetRight = 430, OffsetTop = -80, OffsetBottom = -24,
            Visible = false, MouseFilter = Control.MouseFilterEnum.Stop,
            GrowVertical = Control.GrowDirection.Begin
        };
        _encart.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.05f, 0.06f, 0.08f, 0.90f),
            BorderColor = new Color(0.40f, 0.52f, 0.58f),
            BorderWidthLeft = 3,
            CornerRadiusTopLeft = 4, CornerRadiusTopRight = 4,
            CornerRadiusBottomLeft = 4, CornerRadiusBottomRight = 4,
            ContentMarginLeft = 16, ContentMarginRight = 14, ContentMarginTop = 10, ContentMarginBottom = 12
        });
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 6);
        _encartTitle = new Label();
        _encartTitle.AddThemeFontSizeOverride("font_size", 15);
        _encartTitle.AddThemeColorOverride("font_color", new Color(0.92f, 0.78f, 0.36f));
        _encartText = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        _encartText.AddThemeFontSizeOverride("font_size", 14);
        var hint = new Label { Text = "clic pour fermer" };
        hint.AddThemeFontSizeOverride("font_size", 11);
        hint.AddThemeColorOverride("font_color", new Color(0.55f, 0.58f, 0.62f));
        box.AddChild(_encartTitle); box.AddChild(_encartText); box.AddChild(hint);
        _encart.AddChild(box);
        /* LE CLIC EST PRIS PAR LE CADRE LUI-MÊME, par son gui_input : c'est un
           Control, il a la souris AVANT tout le reste — la leçon de la carte. */
        _encart.GuiInput += e =>
        {
            if (e is InputEventMouseButton mb && mb.Pressed && mb.ButtonIndex == MouseButton.Left)
                _encart.Visible = false;
        };
        layer.AddChild(_encart);
    }

    /// <summary>
    /// Ouvrir l'encart. <paramref name="tone"/> : +1 une bonne nouvelle (liseré
    /// doré), −1 une mauvaise (rouille), 0 neutre.
    /// </summary>
    public void ShowEncart(string title, string text, int tone = 0)
    {
        if (_encart == null) { ShowNotice(title, text); return; }
        _encartTitle!.Text = title;
        _encartText!.Text = text;
        var col = tone > 0 ? new Color(0.92f, 0.74f, 0.30f)
                : tone < 0 ? new Color(0.72f, 0.36f, 0.24f)
                : new Color(0.40f, 0.52f, 0.58f);
        _encartText.AddThemeColorOverride("font_color", tone > 0 ? new Color(0.98f, 0.92f, 0.76f)
                                                     : tone < 0 ? new Color(0.92f, 0.82f, 0.78f)
                                                     : new Color(0.90f, 0.92f, 0.94f));
        ((StyleBoxFlat)_encart.GetThemeStylebox("panel")).BorderColor = col;
        _encart.Visible = true;
        _encartLeft = EncartLife;
    }

    /// <summary>À chaque image : l'encart qui s'efface, et le prochain navire qu'on croise.</summary>
    void RumourTick(double dt)
    {
        if (_encart != null && _encart.Visible && (_encartLeft -= dt) <= 0) _encart.Visible = false;
        if (_world == null || _inTitle) return;
        if (_rumourIn < 0) _rumourIn = 120 + _rumourRng.Randf() * 180;       // le premier : deux à cinq minutes

        /* Seulement sous voiles ou à la machine, et loin d'un port : à quai on a
           le comptoir, qui dit mieux et pour rien. */
        var b = _ship.Physics.Body;
        if (_portHere != null || Math.Sqrt(b.Vel.X * b.Vel.X + b.Vel.Z * b.Vel.Z) < 0.7) return;
        if ((_rumourIn -= dt) > 0) return;
        _rumourIn = 240 + _rumourRng.Randf() * 240;                           // puis quatre à huit minutes
        Speak();
    }

    /// <summary>Un navire parlé : ce qu'il dit d'un port qu'on n'a pas sous le nez.</summary>
    bool Speak()
    {
        if (_world == null) return false;
        var o = _sea.Core.Origin;
        var b = _ship.Physics.Body;
        double wx = o.X + b.Pos.X, wz = o.Z + b.Pos.Z;
        var others = new List<Isle>();
        foreach (var i in _world.Isles)
            if ((i.X - wx) * (i.X - wx) + (i.Z - wz) * (i.Z - wz) > 2500 * 2500) others.Add(i);
        if (others.Count == 0) return false;
        var target = others[(int)(_rumourRng.Randf() * others.Count) % others.Count];
        var r = _market.RumourOf(target, _sea.Core.Time, _rumourRng.Randf());
        ShowEncart("Navire parlé",
            $"Nous avons parlé {r.Voile} venant {DeNom(r.Port)}. Il dit {r.Lie}{r.Mot} là-bas.",
            r.Fort ? 1 : r.Faible ? -1 : 0);
        return true;
    }
}
