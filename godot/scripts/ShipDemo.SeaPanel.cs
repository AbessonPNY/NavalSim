using Godot;
using System;

namespace NavalSim;

/// <summary>
/// LE PANNEAU DE MISE AU POINT DE LA MER, sur ⇧M. Sur le côté et sans rien
/// arrêter : on règle en regardant la mer bouger, ce que le menu d'options,
/// posé au milieu de l'image, ne permettait pas. Chaque curseur s'applique à
/// l'instant et s'écrit dans reglages.ini, section [mer]. Le M seul reste à
/// l'ancre de la page, quand elle sera portée.
/// </summary>
public partial class ShipDemo
{
    PanelContainer _seaPanel = null!;

    void BuildSeaPanel(CanvasLayer layer)
    {
        _seaPanel = new PanelContainer
        {
            AnchorLeft = 1, AnchorRight = 1, AnchorTop = 0, AnchorBottom = 0,
            OffsetLeft = -330, OffsetRight = -12, OffsetTop = 60, OffsetBottom = 60,
            Visible = false
        };
        _seaPanel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.04f, 0.06f, 0.09f, 0.72f),
            CornerRadiusTopLeft = 8, CornerRadiusTopRight = 8, CornerRadiusBottomLeft = 8, CornerRadiusBottomRight = 8,
            ContentMarginLeft = 14, ContentMarginRight = 14, ContentMarginTop = 10, ContentMarginBottom = 10
        });
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 4);
        _seaPanel.AddChild(box);
        layer.AddChild(_seaPanel);

        var title = new Label { Text = "Mer · mise au point (⇧M)" };
        title.AddThemeFontSizeOverride("font_size", 15);
        title.AddThemeColorOverride("font_color", new Color(0.94f, 0.96f, 0.98f));
        box.AddChild(title);

        var st = _settings;
        var d = new Settings();                      // les valeurs par défaut, pour le bouton
        var sliders = new System.Collections.Generic.List<(HSlider S, Func<float> Def)>();
        void Slide(string text, double min, double max, double step, float value, Func<Settings, float> def, Action<float> set)
        {
            var v = Row(box, text);
            v.Text = value.ToString("F3");
            var s = Slider(box, min, max, step, value);
            s.ValueChanged += x => { v.Text = x.ToString("F3"); set((float)x); Changed(); };
            sliders.Add((s, () => def(d)));
        }
        Slide("Rugosité de base", 0.01, 0.4, 0.005, st.SeaRoughBase, x => x.SeaRoughBase, x => st.SeaRoughBase = x);
        Slide("Rugosité ajoutée par le vent", 0, 0.5, 0.01, st.SeaRoughWind, x => x.SeaRoughWind, x => st.SeaRoughWind = x);
        Slide("Flou du ciel dans l'eau", 0, 1.5, 0.05, st.SeaSkyBlur, x => x.SeaSkyBlur, x => st.SeaSkyBlur = x);
        Slide("Rides", 0, 3, 0.05, st.SeaRideGain, x => x.SeaRideGain, x => st.SeaRideGain = x);
        Slide("Seuil d'écume (jacobien)", 0.4, 1.0, 0.01, st.SeaJacobian, x => x.SeaJacobian, x => st.SeaJacobian = x);
        Slide("Moutons", 0, 2, 0.05, st.SeaCapGain, x => x.SeaCapGain, x => st.SeaCapGain = x);
        Slide("Écume en traits", 0, 2, 0.05, st.SeaFoamGain, x => x.SeaFoamGain, x => st.SeaFoamGain = x);
        Slide("Stries de pente", 0, 3, 0.05, st.SeaStreaks, x => x.SeaStreaks, x => st.SeaStreaks = x);
        Slide("Sillage de Kelvin", 0, 4, 0.05, st.SeaKelvin, x => x.SeaKelvin, x => st.SeaKelvin = x);
        Slide("Rais sous l'eau", 0, 3, 0.05, st.SeaShafts, x => x.SeaShafts, x => st.SeaShafts = x);
        Slide("Densité de l'eau", 0.2, 3, 0.05, st.SeaDensity, x => x.SeaDensity, x => st.SeaDensity = x);

        var reset = new Button { Text = "Valeurs par défaut", FocusMode = Control.FocusModeEnum.None };
        reset.Pressed += () => { foreach (var (s, def) in sliders) s.Value = def(); };
        box.AddChild(reset);
    }

    void ToggleSeaPanel() => _seaPanel.Visible = !_seaPanel.Visible;
}
