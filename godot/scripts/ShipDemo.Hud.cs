using Godot;
using System;
using NavalSim.Core;

namespace NavalSim;

/// <summary>
/// LES INSTRUMENTS, CHACUN OÙ ON LE CHERCHE — et non plus un pavé de chiffres
/// dans le coin haut gauche. Le cap et la vitesse se lisent au-dessus de la
/// boussole, puisque c'est là qu'on regarde pour gouverner ; la bourse en bas à
/// gauche, derrière son écu ; le temps qu'il fait, la date et l'heure en haut,
/// à côté du panneau de défilement du jour, qui est ce qui les fait bouger. Ce
/// qui reste — tirant, immersion, fond, déplacement, commandes — est du
/// débogage, et attend la touche H.
/// </summary>
public partial class ShipDemo
{
    Label? _navLine;          // cap et vitesse, au-dessus de la boussole
    Label? _skyLine;          // le temps, la date, l'heure
    Label? _purseLine;        // la bourse
    Control? _purseIcon;      // son écu
    HBoxContainer? _purseBox;

    /// <summary>Les instruments sont-ils montrés ? (⇧H les tait tous ; H n'ouvre que le débogage.)</summary>
    bool _hudOn = true;

    /// <summary>Le haut de la colonne de gauche : sous le pavé de débogage quand il est ouvert.</summary>
    float HudTop => _info.Visible ? _info.Position.Y + _info.Size.Y + 8 : 14;

    static readonly Color HudInk = new(0.92f, 0.94f, 0.96f);
    static readonly Color HudGold = new(0.92f, 0.78f, 0.36f);
    static readonly Color HudDim = new(0.72f, 0.76f, 0.80f);

    void BuildHud(CanvasLayer layer)
    {
        Label Plate(int size, Color col, HorizontalAlignment align)
        {
            var l = new Label { HorizontalAlignment = align };
            l.AddThemeFontSizeOverride("font_size", size);
            l.AddThemeColorOverride("font_color", col);
            // le même liseré noir que le reste : lisible sur un ciel blanc comme sur l'eau
            l.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.85f));
            l.AddThemeConstantOverride("outline_size", 5);
            layer.AddChild(l);
            return l;
        }

        _navLine = Plate(20, HudInk, HorizontalAlignment.Center);
        /* LE TEMPS QU'IL FAIT, DE LA MAIN DU CAPITAINE : la même Estonia que ses
           notes sur la carte — c'est ce qu'il écrirait sur son livre de bord, pas
           un instrument. Plus grande, l'anglaise étant fine ; sans elle, la police
           du moteur et la taille des autres plaques. */
        var hand = HandFont.Get();
        _skyLine = Plate(hand != null ? 26 : 15, HudInk, HorizontalAlignment.Right);
        if (hand != null) _skyLine.AddThemeFontOverride("font", hand);
        // sans liseré : l'anglaise est une écriture, pas un instrument (demandé)
        if (hand != null) _skyLine.AddThemeConstantOverride("outline_size", 0);
        _skyLine.AddThemeConstantOverride("line_spacing", hand != null ? -4 : 0);

        /* LA BOURSE derrière un écu dessiné : une pièce de monnaie ne se trouve
           dans aucune police qu'on puisse supposer présente, et le projet dessine
           ce qu'il peut dessiner plutôt que d'embarquer une image. */
        _purseBox = new HBoxContainer();
        _purseBox.AddThemeConstantOverride("separation", 8);
        layer.AddChild(_purseBox);
        _purseIcon = new EcuIcon { CustomMinimumSize = new Vector2(20, 24), MouseFilter = Control.MouseFilterEnum.Ignore };
        _purseBox.AddChild(_purseIcon);
        _purseLine = new Label { VerticalAlignment = VerticalAlignment.Center };
        _purseLine.AddThemeFontSizeOverride("font_size", 18);
        _purseLine.AddThemeColorOverride("font_color", HudGold);
        _purseLine.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.85f));
        _purseLine.AddThemeConstantOverride("outline_size", 5);
        _purseBox.AddChild(_purseLine);
    }

    /// <summary>Chaque plaque à sa place, sur l'écran tel qu'il est — le masque de cinéma compris.</summary>
    void HudTick()
    {
        if (_navLine == null || _skyLine == null || _purseBox == null || _purseLine == null) return;
        bool on = _hudOn && !_inTitle;
        _navLine.Visible = on && _compass != null && _compass.Visible;
        _skyLine.Visible = on;
        _purseBox.Visible = on && !_chartOpen;
        if (!on) return;

        var s = GetViewport().GetVisibleRect().Size;
        float bottom = 0, right = 0, left = 0, top = 0;
        if (_settings?.FilmMask == true)
        {
            if (s.X / s.Y < FilmAspect) { bottom = top = (s.Y - s.X / FilmAspect) * 0.5f; }
            else { right = left = (s.X - s.Y * FilmAspect) * 0.5f; }
        }

        // le cap et la vitesse, posés sur la boussole
        if (_navLine.Visible && _compass != null)
        {
            var b = _ship.Physics.Body;
            var f = b.Quat.Rotate(new Vec3d(0, 0, 1));
            double hdg = (Math.Atan2(-f.X, f.Z) * 180 / Math.PI + 360) % 360;      // l'est est −x
            double kn = Math.Sqrt(b.Vel.X * b.Vel.X + b.Vel.Z * b.Vel.Z) * Config.MsToKn;
            _navLine.Text = $"cap {hdg:F0}°    {kn:F1} nds";
            _navLine.Size = new Vector2(_compass.Size.X, 0);
            _navLine.Position = new Vector2(_compass.Position.X, _compass.Position.Y - _navLine.Size.Y - 4);
        }

        // le temps qu'il fait, la date et l'heure — à gauche du panneau de défilement
        int bf = Mathf.Clamp((int)Math.Round(_sea.Core.SeaState), 0, 9);
        double h = _sky.Core.DayTime;
        string tombe = _fall.Amount > 0.004 ? (_fall.Snow ? " · il neige" : " · il pleut") : "";
        if (_seaFog != null && _seaFog.Amount > 0.3) tombe += " · brume";
        _skyLine.Text =
            $"vent {_windNowDeg:F0}°   force {_sea.Core.SeaState:F1} · {Config.Beaufort[bf].Name}"
            + (_seaMaster != null ? " · " + _seaMaster : "") + "\n" +
            $"{_climate.Word()}{tombe}\n" +
            (_inSquall ? $"dépression à {_squall.Dist / 1852:F1} mille(s) du centre\n" : "") +
            $"{_calendar.Date:dd/MM/yyyy}   {(int)h:00}:{(int)((h - Math.Floor(h)) * 60):00}";
        _skyLine.Size = new Vector2(320, 0);
        float skyRight = s.X - right - 14 - (_sunPanel != null && _sunPanel.Visible ? _sunPanel.Size.X + 12 : 0);
        _skyLine.Position = new Vector2(skyRight - _skyLine.Size.X, top + 14);

        // la bourse, en bas à gauche — la cale se lit ailleurs (Z)
        _purseLine.Text = $"{_purse.Ecus} écus   {_purse.Pieces} pièces";
        _purseBox.Position = new Vector2(left + 18, s.Y - bottom - _purseBox.Size.Y - 18);
    }

    /// <summary>
    /// L'ÉCU, dessiné : un blason à pointe ronde, bordé d'or, avec la croix de
    /// Saint-André en creux. Vingt lignes valent mieux qu'une image à embarquer.
    /// </summary>
    sealed partial class EcuIcon : Control
    {
        public override void _Draw()
        {
            float w = Size.X, h = Size.Y;
            var pts = new Vector2[]
            {
                new(0.08f * w, 0.10f * h), new(0.92f * w, 0.10f * h), new(0.92f * w, 0.55f * h),
                new(0.72f * w, 0.86f * h), new(0.50f * w, 0.95f * h), new(0.28f * w, 0.86f * h),
                new(0.08f * w, 0.55f * h)
            };
            DrawColoredPolygon(pts, new Color(0.55f, 0.42f, 0.16f, 0.92f));
            DrawPolyline(new[] { pts[0], pts[1], pts[2], pts[3], pts[4], pts[5], pts[6], pts[0] },
                new Color(0.95f, 0.82f, 0.42f), 1.6f, true);
            var gold = new Color(0.95f, 0.82f, 0.42f, 0.9f);
            DrawLine(new Vector2(0.28f * w, 0.28f * h), new Vector2(0.72f * w, 0.66f * h), gold, 1.4f, true);
            DrawLine(new Vector2(0.72f * w, 0.28f * h), new Vector2(0.28f * w, 0.66f * h), gold, 1.4f, true);
        }
    }
}
