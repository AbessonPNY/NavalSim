using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using NavalSim.Core;

namespace NavalSim;

/// <summary>Le soleil à la main : les curseurs du panneau (sorti de ShipDemo.cs).</summary>
public partial class ShipDemo
{
    // ------------------------------------------------------------------
    //  LE SOLEIL À LA MAIN — les curseurs « Hauteur du soleil » et
    //  « Défilement du jour » de la console d'origine
    // ------------------------------------------------------------------

    HSlider _sunElev = null!, _sunSpeed = null!;
    PanelContainer _sunPanel = null!;
    Label _sunVal = null!, _sunSpeedVal = null!;
    double _sunTick;

    void BuildSunPanel(CanvasLayer layer)
    {
        var panel = new PanelContainer
        {
            AnchorLeft = 1, AnchorRight = 1, OffsetLeft = -300, OffsetRight = -14, OffsetTop = 14
        };
        panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.04f, 0.06f, 0.09f, 0.55f),
            CornerRadiusTopLeft = 6, CornerRadiusTopRight = 6,
            CornerRadiusBottomLeft = 6, CornerRadiusBottomRight = 6,
            ContentMarginLeft = 12, ContentMarginRight = 12, ContentMarginTop = 8, ContentMarginBottom = 10
        });
        var box = new VBoxContainer();
        panel.AddChild(box);
        layer.AddChild(panel);
        _sunPanel = panel;

        _sunVal = Row(box, "Heure");
        /* L'HEURE, ET NON LA HAUTEUR. Le curseur réglait la hauteur du soleil à
           relèvement fixe : il ne pouvait pas le faire passer du lever au coucher,
           et le toucher arrêtait le jour — un soleil posé le soir ne se relevait
           plus (signalé). L'heure, elle, donne la vraie course : l'est au lever,
           le sud à midi, l'ouest au coucher, la lune et les feux la nuit. */
        _sunElev = Slider(box, 0, 24, 0.05, _sky.Core.DayTime);
        _sunSpeedVal = Row(box, "Défilement du jour");
        _sunSpeed = Slider(box, 0, 16, 0.5, _sky.DayRate);

        /* Le jour REPART de l'heure choisie : on ne l'arrête plus en y touchant
           (« Défilement du jour » à zéro le fait). Pendant qu'on tient le
           curseur, l'horloge ne le réécrit pas sous la main. */
        _sunElev.DragStarted += () => _sunHeld = true;
        _sunElev.DragEnded += _ => _sunHeld = false;
        _sunElev.ValueChanged += v =>
        {
            _sky.Core.SetTimeOfDay(v % 24, _sky.Latitude);
            _sky.Apply();
            ShowSun(_sky.Core.SunElevDeg);
            ShowSunSpeed();
        };
        _sunSpeed.ValueChanged += v => { _sky.DayRate = v; ShowSunSpeed(); };
        ShowSun(_sky.Core.SunElevDeg);
        ShowSunSpeed();
    }

    Label Row(VBoxContainer box, string name)
    {
        var row = new HBoxContainer();
        var n = new Label { Text = name, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        var v = new Label { HorizontalAlignment = HorizontalAlignment.Right };
        foreach (var l in new[] { n, v })
        {
            l.AddThemeFontSizeOverride("font_size", 14);
            l.AddThemeColorOverride("font_color", new Color(0.94f, 0.96f, 0.98f));
        }
        row.AddChild(n);
        row.AddChild(v);
        box.AddChild(row);
        return v;
    }

    static HSlider Slider(VBoxContainer box, double min, double max, double step, double value)
    {
        var s = new HSlider
        {
            MinValue = min, MaxValue = max, Step = step, Value = value,
            // pas de focus : les flèches restent à la force et au vent
            FocusMode = Control.FocusModeEnum.None,
            CustomMinimumSize = new Vector2(0, 20)
        };
        box.AddChild(s);
        return s;
    }

    bool _sunHeld;

    /// <summary>L'heure, et ce qu'elle fait du soleil : sa hauteur, ou la nuit.</summary>
    void ShowSun(double e)
    {
        double h = _sky.Core.DayTime;
        string clock = $"{(int)h:00}:{(int)((h - (int)h) * 60):00}";
        _sunVal.Text = e < -6 ? $"{clock} · nuit" : e < 0 ? $"{clock} · crépuscule" : $"{clock} · soleil {Math.Round(e)}°";
    }

    /// <summary>
    /// Le taux ÉNONCÉ : « ×2 » ne veut rien dire si « ×1 » ne dit pas quoi —
    /// une minute réelle pour une heure. L'heure du ciel à côté.
    /// </summary>
    /// <summary>
    /// POSER LE DÉFILEMENT DU JOUR, curseur compris. Le régler sur le ciel seul
    /// laissait la console montrer l'ancien taux, et le premier frôlement du
    /// curseur le rendait — une valeur affichée qui n'est pas la valeur vraie est
    /// pire que pas de valeur du tout.
    /// </summary>
    void SetDayRate(double v)
    {
        _sky.DayRate = Math.Clamp(v, 0, 16);
        if (_sunSpeed != null) _sunSpeed.SetValueNoSignal(_sky.DayRate);
        if (_sunSpeedVal != null) ShowSunSpeed();
    }

    void ShowSunSpeed()
    {
        double v = _sky.DayRate;
        _sunSpeedVal.Text = v <= 0 ? "arrêt"
            : $"×{(v % 1 != 0 ? v.ToString("F1") : v.ToString("F0"))} · {Math.Round(24 / v)} min/jour";
    }

    /// <summary>Le curseur suit le soleil quand le jour tourne, quatre fois par seconde.</summary>
    void TickSunPanel(double dt)
    {
        _sunTick += dt;
        if (_sunTick < 0.25) return;
        _sunTick = 0;
        if (_sky.DayRunning && !_sunHeld) _sunElev.SetValueNoSignal(_sky.Core.DayTime);
        ShowSun(_sky.Core.SunElevDeg);
        ShowSunSpeed();
    }
}
