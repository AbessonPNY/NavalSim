using Godot;
using System;
using System.Collections.Generic;

namespace NavalSim;

/// <summary>
/// LE BORD EN BATTERIE, AU DOIGT — ce que Tab fait déjà en tournant, montré et
/// choisi d'un coup d'œil : un bouton par groupe que la coque porte réellement
/// (tribord, bâbord, poupe, proue), avec ce qui y est prêt. Le bord reste un
/// ÉTAT unique (_gunSide) : ces boutons l'écrivent, ils n'en gardent pas un
/// second — sans quoi Tab et eux divergeraient en silence.
/// </summary>
public partial class ShipDemo
{
    HBoxContainer? _gunBar;
    readonly Dictionary<int, Button> _gunButtons = new();

    static readonly Color GunPicked = new(0.92f, 0.78f, 0.36f);   // l'or du bord choisi
    static readonly Color GunIdle = new(0.78f, 0.82f, 0.86f);
    static readonly Color GunSpent = new(0.72f, 0.45f, 0.35f);    // plus une pièce prête

    void BuildGunSide(CanvasLayer layer)
    {
        _gunBar = new HBoxContainer { Visible = false };
        _gunBar.AddThemeConstantOverride("separation", 6);
        layer.AddChild(_gunBar);
        // l'ordre de Tab, pour que le doigt et la touche parcourent la même batterie
        foreach (int side in new[] { -1, 1, 2, -2 })
        {
            int s = side;
            var b = new Button { FocusMode = Control.FocusModeEnum.None, Visible = false };
            b.AddThemeFontSizeOverride("font_size", 12);
            b.TooltipText = "Mettre en batterie : " + GunNames[s];
            b.Pressed += () => PickGunSide(s);
            _gunBar.AddChild(b);
            _gunButtons[s] = b;
        }
    }

    /// <summary>Choisir un bord au doigt. Le même chemin que Tab, jusqu'au mot dit.</summary>
    void PickGunSide(int side)
    {
        if (!_ship.Battery.Has(side) || _gunSide == side) return;
        _gunSide = side;
        Say("En batterie : " + GunNames[side]);
        GunSideTick();
    }

    /// <summary>Sous le curseur d'écoute, et muet quand les instruments le sont ou qu'elle n'a pas de batterie.</summary>
    void GunSideTick()
    {
        if (_gunBar == null) return;
        var bat = _ship.Battery;
        _gunBar.Visible = _info.Visible && !_inTitle && bat.Guns.Count > 0;
        if (!_gunBar.Visible) return;
        _gunBar.Position = new Vector2(18, _info.Position.Y + _info.Size.Y + 8 + TrimHeight);
        foreach (var (side, b) in _gunButtons)
        {
            bool has = bat.Has(side);
            b.Visible = has;
            if (!has) continue;
            var (ok, all) = bat.Count(side);
            var l = _gunnery.Loaded(bat, side);
            // ce qui est prêt sur ce qui tient encore debout ; démontées, elles ne comptent plus
            b.Text = $"{GunNames[side]} {l.Ready}/{Math.Max(ok, l.All)}"
                   + (ok < all ? $" ({all - ok} démontée{(all - ok > 1 ? "s" : "")})" : "");
            b.AddThemeColorOverride("font_color", side == _gunSide ? GunPicked : l.Ready > 0 ? GunIdle : GunSpent);
        }
    }
}
