using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using NavalSim.Core;

namespace NavalSim;

/// <summary>Les réglages (reglages.ini) et le menu d'options (sorti de ShipDemo.cs).</summary>
public partial class ShipDemo
{
    // ------------------------------------------------------------------
    //  LES RÉGLAGES — reglages.ini et le menu d'options (Échap)
    // ------------------------------------------------------------------

    Settings _settings = null!;
    CameraAttributesPractical _camAttr = null!;
    MotionBlurEffect _motionBlur = null!;
    UnderwaterEffect _under = null!;
    static readonly StringName USeaSilt = "u_silt";
    float _seaSilt = -1;

    /// <summary>
    /// La vase d'une rade, LUE SUR LA MER (u_silt, ocean.gdshader) et non recopiée : la
    /// passe sous-marine trouble son eau de la même quantité que la mer vue de dessus.
    /// </summary>
    double SeaSilt()
    {
        if (_seaSilt < 0 && _sea.Material is ShaderMaterial m)
        {
            Variant v = m.GetShaderParameter(USeaSilt);
            if (v.VariantType == Variant.Type.Nil && m.Shader != null)
                v = RenderingServer.ShaderGetParameterDefault(m.Shader.GetRid(), USeaSilt);
            _seaSilt = v.VariantType == Variant.Type.Nil ? 0f : (float)v;
        }
        return Math.Max(0, _seaSilt);
    }
    /// <summary>L'œil était-il sous l'eau à l'image d'avant ? (avec hystérésis : voir plus bas)</summary>
    bool _wasWet;
    /// <summary>Sa hauteur à l'image d'avant, pour savoir à quelle vitesse il descend.</summary>
    double _camWasY;
    AnamorphicDofEffect _anamorphic = null!;
    DofMarker _dofMarker = null!;
    readonly ColorRect[] _maskBars = { new() { Color = Colors.Black }, new() { Color = Colors.Black } };
    const float FilmAspect = 2.35f;

    void LayoutMask()
    {
        Vector2 s = GetViewport().GetVisibleRect().Size;
        bool on = FilmMaskOn;
        foreach (var r in _maskBars) r.Visible = on;
        if (!on || s.X <= 0 || s.Y <= 0) return;
        if (s.X / s.Y < FilmAspect)
        {
            float bar = (s.Y - s.X / FilmAspect) * 0.5f;
            _maskBars[0].Position = Vector2.Zero; _maskBars[0].Size = new Vector2(s.X, bar);
            _maskBars[1].Position = new Vector2(0, s.Y - bar); _maskBars[1].Size = new Vector2(s.X, bar);
        }
        else
        {
            float bar = (s.X - s.Y * FilmAspect) * 0.5f;
            _maskBars[0].Position = Vector2.Zero; _maskBars[0].Size = new Vector2(bar, s.Y);
            _maskBars[1].Position = new Vector2(s.X - bar, 0); _maskBars[1].Size = new Vector2(bar, s.Y);
        }
    }
    // le repère reste affiché menu fermé, pour régler en regardant la mer
    bool _dofMarkerKeep;
    // la lueur tourne à l'armement : son programme compilé au démarrage, pas au crépuscule
    int _glowPrime = 3;

    /// <summary>--reflet : 0 aucun, 1 normal, 2 sans masque (diagnostic).</summary>
    int _refletMode = 1;

    /// <summary>--anneaux : la charge imposee, ou -1 pour laisser faire la rampe.</summary>
    float _forceRings = -1;

    /// <summary>Le seuil de la lueur, en linéaire — voir ApplySettings pour d'où il sort.</summary>
    const float GlowThreshold = 0.319f;
    /// <summary>Et celui qu'on lui impose de JOUR quand les anneaux brûlent : voir plus bas.</summary>
    const float GlowDayThreshold = 1.6f;
    PanelContainer _menu = null!;
    // O et G changent ces deux-là au clavier : le menu les relit à l'ouverture
    CheckBox _chkOcclusion = null!, _chkIndirect = null!;

    /// <summary>Poser les réglages sur le ciel, la mer, l'affichage et le navire.</summary>
    void ApplySettings()
    {
        var s = _settings;
        /* LE SON SUIT SES RÉGLAGES. Le volume passe par le bus maître : c'est
           l'endroit qui vaut pour tout ce qui sonne, bruitages comme musique, et
           il n'y a donc pas deux volumes à tenir en accord. */
        if (_sound != null) _sound.On = s.Sound;
        AudioServer.SetBusVolumeDb(0, Mathf.LinearToDb(Math.Clamp(s.Volume, 0.001f, 1f)));
        _sky.Env.SsaoEnabled = s.Occlusion;
        _sky.SunStrength = s.SunStrength; _sky.SunWarmth = s.SunWarmth; _sky.SkyShade = s.SkyShade;
        _sky.SeaBounce = s.SeaBounce;
        _sky.Apply();
        _sky.Env.SsilEnabled = s.IndirectLight;
        /* LE PLEIN ÉCRAN SANS BORDURE (WindowMode.Fullscreen), pas l'exclusif : il
           couvre la barre des tâches sans changer la résolution du bureau, et laisse
           Alt+Tab et les écrans virtuels tranquilles. Seulement sur un changement —
           réaffirmer le mode à chaque réglage ferait clignoter la fenêtre. */
        var wantMode = s.Fullscreen ? DisplayServer.WindowMode.Fullscreen : DisplayServer.WindowMode.Windowed;
        if (DisplayServer.WindowGetMode() != wantMode) DisplayServer.WindowSetMode(wantMode);
        DisplayServer.WindowSetVsyncMode(s.VSync ? DisplayServer.VSyncMode.Enabled : DisplayServer.VSyncMode.Disabled);
        /* Le lointain devient flou passé cette distance, sur une transition de la
           moitié : c'est la mer vers l'horizon, pas le navire qu'on regarde. */
        /* NET ENTRE DEUX DISTANCES. Le flou de près monte vers l'œil sur la même
           part de sa distance que celui du lointain s'étend au-delà de la sienne :
           un fondu en mètres ne vaut pas aux deux bouts à la fois — trois cents
           mètres n'ont pas de sens pour une mise au point à deux. */
        bool far = s.DofDistance < DofRange.Infinity;
        // l'un OU l'autre : le flou anamorphique remplace celui de Godot
        bool godotDof = s.Dof && !s.DofAnamorphic;
        // à la lunette, ni l'un ni l'autre : voir ShipDemo.Spyglass
        godotDof &= !_glassUp;
        _camAttr.DofBlurFarEnabled = godotDof && far;
        _camAttr.DofBlurFarDistance = far ? s.DofDistance : 8192;
        _camAttr.DofBlurFarTransition = Math.Max(0.01f, s.DofDistance * s.DofFade);
        _camAttr.DofBlurNearEnabled = godotDof && s.DofNear > 0;
        _camAttr.DofBlurNearDistance = s.DofNear;
        _camAttr.DofBlurNearTransition = Math.Max(0.01f, s.DofNear * s.DofFade);
        _camAttr.DofBlurAmount = s.DofAmount;
        _anamorphic.Enabled = s.Dof && s.DofAnamorphic && (far || s.DofNear > 0) && !_glassUp;
        _anamorphic.Near = s.DofNear;
        _anamorphic.Far = s.DofDistance;
        _anamorphic.Fade = s.DofFade;
        _anamorphic.Amount = s.DofAmount;
        _anamorphic.Quality = s.DofQuality;
        // la qualité du bokeh est un réglage du SERVEUR, pas de la caméra
        RenderingServer.CameraAttributesSetDofBlurQuality(
            (RenderingServer.DofBlurQuality)Math.Clamp(s.DofQuality, 0, 3), false);

        /* L'ANTICRÉNELAGE. Le MSAA lisse les arêtes — coque, mâture, cordages — ;
           ce qui passe sur l'image finie lisse le reste. Le TAA lisse le mieux les
           cordages fins, mais il accumule les images passées : sur une mer qui
           bouge partout il laisse une traîne, et il fait double emploi avec le
           flou de mouvement. */
        var vp = GetViewport();
        vp.Msaa3D = s.Msaa switch { 0 => Viewport.Msaa.Disabled, 2 => Viewport.Msaa.Msaa2X, 8 => Viewport.Msaa.Msaa8X, _ => Viewport.Msaa.Msaa4X };
        vp.ScreenSpaceAA = s.ScreenAA switch { "fxaa" => Viewport.ScreenSpaceAAEnum.Fxaa, "smaa" => Viewport.ScreenSpaceAAEnum.Smaa, _ => Viewport.ScreenSpaceAAEnum.Disabled };
        vp.UseTaa = s.ScreenAA == "taa";
        // les cordages en rubans changent de variante avec lui (rope_ribbon_taa.gdshader)
        ShipNode.RibbonMode(vp.UseTaa);

        /* LA LUEUR de bloom.js. Son seuil et son genou sont lus sur l'image
           AFFICHÉE (0,72 ± 0,12) ; la lueur de Godot lit l'image LINÉAIRE, avant
           l'encodage : les mêmes bornes, décodées, font 0,319 à 0,674. Godot fait
           lui aussi une bascule douce (smoothstep du seuil au seuil + échelle), mais
           sur le plus fort des trois canaux et non sur la luminance de l'œil. Le
           flou de la page (quart de résolution, deux passes séparables) s'étale
           sur ≈ 25 px en 1080p : les niveaux 2 et 3 de Godot, au quart et au
           huitième, couvrent la même largeur. */
        var env = _sky.Env;
        env.GlowHdrThreshold = GlowThreshold;
        env.GlowHdrScale = 0.355f;
        env.GlowBloom = 0;
        env.GlowBlendMode = Godot.Environment.GlowBlendModeEnum.Additive;
        env.GlowNormalized = true;
        for (int i = 0; i < 7; i++) env.SetGlowLevel(i, i == 2 || i == 3 ? 1 : 0);
        env.GlowIntensity = s.GlowStrength;

        /* L'EXPOSITION QUI S'ADAPTE — un ajout, la page n'en a pas, coupée par
           défaut. Godot règle l'exposition à échelle / luminance moyenne, la
           moyenne bornée entre deux sensibilités (ISO × 0,125 / 100 en luminance,
           CameraAttributesPractical). Bornée en bas AU SEUIL, elle ne dépasse
           jamais 1 : l'image calibrée comme la page est le maximum, et la nuit
           noire le reste. Elle ne fait que baisser, quand la moyenne passe le
           seuil — ce qui n'arrive qu'au soleil éblouissant, allumé avec elle. */
        const float IsoToLum = 0.125f / 100f;
        _camAttr.AutoExposureEnabled = s.AutoExposure;
        _camAttr.AutoExposureScale = s.AutoExposureThreshold;
        _camAttr.AutoExposureMinSensitivity = s.AutoExposureThreshold / IsoToLum;
        _camAttr.AutoExposureMaxSensitivity = 20f / IsoToLum;
        _camAttr.AutoExposureSpeed = s.AutoExposureSpeed;
        _sky.SetDazzle(s.AutoExposure);
        LayoutMask();
        _motionBlur.Enabled = s.MotionBlur && !_glassUp;
        _motionBlur.Shutter = s.Shutter;
        _motionBlur.MainProjection = _cam.GetCameraProjection();
        _sea.Material?.SetShaderParameter(U.LampReflection, s.LampReflection);
        _sea.Material?.SetShaderParameter(U.LampWater, s.LampWater);
        _foam?.SetJacobianFoam(s.SeaJacobian);
        if (_sea.Material is ShaderMaterial sm)
        {
            sm.SetShaderParameter("u_rough_base", s.SeaRoughBase);
            sm.SetShaderParameter("u_rough_wind", s.SeaRoughWind);
            sm.SetShaderParameter("u_sky_blur", s.SeaSkyBlur);
            sm.SetShaderParameter("u_ride_gain", s.SeaRideGain);
            _land?.Ground.SetShaderParameter("u_ride_gain", s.SeaRideGain);
            _fishNode?.Material?.SetShaderParameter("u_ride_gain", s.SeaRideGain);
            ShipNode.Caustic?.SetShaderParameter("u_ride_gain", s.SeaRideGain);
            sm.SetShaderParameter("u_cap_gain", s.SeaCapGain);
            sm.SetShaderParameter("u_foam_gain", s.SeaFoamGain);
            sm.SetShaderParameter("u_jac_foam", s.SeaJacobian);
            sm.SetShaderParameter("u_streak_gain", s.SeaStreaks);
            sm.SetShaderParameter("u_kelvin_gain", s.SeaKelvin);
            _under.Shafts = s.SeaShafts;
            _under.Density = s.SeaDensity;
            _under.Blur = s.SeaBlur;
            _under.Motes = s.SeaMotes;
        }
        if (_ship != null)
        {
            _ship.SetLanternShadows(s.LanternShadows);
            if (_ship.WithMastLantern != s.MastLantern)
            {
                _ship.WithMastLantern = s.MastLantern;
                _ship.RebuildLanterns();
            }
        }
    }

    void Changed()
    {
        ApplySettings();
        if (_inTitle) TitleDof();
        _settings.Save();
        UpdateInfo();
    }

    /// <summary>
    /// Le menu d'options, sur Échap. Chaque réglage s'applique à l'instant et
    /// s'écrit dans le fichier ; rien à valider, rien à perdre en le fermant.
    /// </summary>
    void BuildMenu(CanvasLayer layer)
    {
        _menu = new PanelContainer
        {
            AnchorLeft = 0.5f, AnchorRight = 0.5f, AnchorTop = 0.5f, AnchorBottom = 0.5f,
            OffsetLeft = -220, OffsetRight = 220, OffsetTop = -300, OffsetBottom = 300,
            Visible = false
        };
        _menu.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.04f, 0.06f, 0.09f, 0.88f),
            CornerRadiusTopLeft = 8, CornerRadiusTopRight = 8,
            CornerRadiusBottomLeft = 8, CornerRadiusBottomRight = 8,
            ContentMarginLeft = 18, ContentMarginRight = 18, ContentMarginTop = 14, ContentMarginBottom = 14
        });
        /* Le menu a grandi plus que l'écran : il défile. */
        var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        var box = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        box.AddThemeConstantOverride("separation", 6);
        scroll.AddChild(box);
        _menu.AddChild(scroll);
        layer.AddChild(_menu);

        Label Title(string text, int size)
        {
            var l = MkLabel(text, size, new Color(0.94f, 0.96f, 0.98f));
            box.AddChild(l);
            return l;
        }
        /* UN BOUTON QUI FAIT, là où tout le reste RÈGLE. Le menu n'avait que des
           cases et des curseurs : il ne savait pas porter un ordre. */
        Button Action(string text, Action go)
        {
            var bt = new Button { Text = text, FocusMode = Control.FocusModeEnum.None };
            bt.Pressed += go;
            box.AddChild(bt);
            return bt;
        }
        CheckBox Check(string text, bool value, Action<bool> set)
        {
            var c = new CheckBox { Text = text, ButtonPressed = value, FocusMode = Control.FocusModeEnum.None };
            c.Toggled += on => { set(on); Changed(); };
            box.AddChild(c);
            return c;
        }
        void Slide(string text, double min, double max, double step, double value, Action<float> set)
        {
            var v = Row(box, text);
            v.Text = value.ToString("F2");
            var s = Slider(box, min, max, step, value);
            s.ValueChanged += x => { v.Text = x.ToString("F2"); set((float)x); Changed(); };
        }

        void Choice(string text, string[] labels, int selected, Action<int> set)
        {
            var row = new HBoxContainer();
            var n = new Label { Text = text, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            n.AddThemeFontSizeOverride("font_size", 14);
            n.AddThemeColorOverride("font_color", new Color(0.94f, 0.96f, 0.98f));
            var o = new OptionButton { FocusMode = Control.FocusModeEnum.None };
            foreach (var l in labels) o.AddItem(l);
            o.Selected = Math.Max(0, selected);
            o.ItemSelected += i => { set((int)i); Changed(); };
            row.AddChild(n);
            row.AddChild(o);
            box.AddChild(row);
        }

        var st = _settings;
        Title("Options", 20);
        /* LE FIL QU'ON SUIT, en tête : le monde reste ouvert, une quête ne fait
           que le traverser. Le menu ne paraît que s'il y a des fiches à lire. */
        if (_quests != null && _quests.List.Count > 0)
        {
            Title("Quête", 15);
            var qlabels = new List<string> { "Mode libre" };
            foreach (var q in _quests.List) qlabels.Add(q.Title.Length > 0 ? q.Title : q.Id);
            Choice("Scénario", qlabels.ToArray(),
                _quests.Active == null ? 0 : _quests.List.IndexOf(_quests.Active) + 1, PickQuest);
        }
        Title("Le bord", 15);
        /* EN TÊTE, parce que c'est le seul ORDRE du menu et qu'on le cherche : le
           reste se règle une fois, celui-ci se donne en cours de route. */
        Action("Revenir au ponton", BackToBerth);
        Title("Lanternes", 15);
        Check("Ombres des lanternes", st.LanternShadows, on => st.LanternShadows = on);
        Check("Lanterne du grand mât", st.MastLantern, on => st.MastLantern = on);
        Slide("Reflet sur la mer", 0, 6, 0.1, st.LampReflection, x => st.LampReflection = x);
        Slide("Lumière dans l'eau", 0, 2, 0.05, st.LampWater, x => st.LampWater = x);
        Title("Lumière", 15);
        Slide("Force du soleil", 0.5, 5, 0.05, st.SunStrength, x => st.SunStrength = x);
        Slide("Chaleur du soleil", 0, 1, 0.05, st.SunWarmth, x => st.SunWarmth = x);
        Slide("Éclairage ambiant", 0, 1.5, 0.05, st.SkyShade, x => st.SkyShade = x);
        Slide("Rebond de la mer", 0, 1, 0.05, st.SeaBounce, x => st.SeaBounce = x);
        Slide("Reflet de lentille", 0, 2, 0.05, st.LensFlare, x => st.LensFlare = x);
        Title("Carte", 15);
        Slide("Épaisseur de la plume", 0.5, 4, 0.1, st.PenWidth, x =>
        {
            st.PenWidth = x;
            if (_chart != null) { _chart.PenWidth = x; _chart.Refresh(); }
        });
        Title("Son", 15);
        Check("Bruitages", st.Sound, on => { st.Sound = on; if (_sound != null) _sound.On = on; });
        Check("Musique d'ambiance", st.Music, on => st.Music = on);
        Slide("Volume", 0, 1, 0.05, st.Volume, x =>
        {
            st.Volume = x;
            AudioServer.SetBusVolumeDb(0, Mathf.LinearToDb(Math.Clamp(x, 0.001f, 1f)));
        });
        Title("Rendu", 15);
        _chkOcclusion = Check("Occlusion ambiante", st.Occlusion, on => st.Occlusion = on);
        _chkIndirect = Check("Lumière indirecte", st.IndirectLight, on => st.IndirectLight = on);
        _chkFullscreen = Check("Plein écran (Alt+Entrée)", st.Fullscreen, on => st.Fullscreen = on);
        Check("Synchro verticale", st.VSync, on => st.VSync = on);
        Check("Profondeur de champ", st.Dof, on => st.Dof = on);
        // la zone nette : deux repères sur un curseur de 0 à l'infini
        var zone = Row(box, "Net");
        var range = new DofRange { Near = st.DofNear, Far = st.DofDistance };
        void ShowZone() => zone.Text = $"de {DofRange.Format(range.Near)} à {DofRange.Format(range.Far)}";
        ShowZone();
        range.Changed += () => { st.DofNear = range.Near; st.DofDistance = range.Far; ShowZone(); Changed(); };
        box.AddChild(range);
        Slide("Fondu (part de la distance)", 0.05, 2, 0.05, st.DofFade, x => st.DofFade = x);
        Slide("Intensité du flou", 0.01, 0.3, 0.01, st.DofAmount, x => st.DofAmount = x);
        Choice("Forme du flou", new[] { "Sphérique (Godot)", "Anamorphique 2:1" }, st.DofAnamorphic ? 1 : 0, i => st.DofAnamorphic = i == 1);
        Choice("Qualité du flou", new[] { "Très basse", "Basse", "Moyenne", "Haute" }, st.DofQuality, i => st.DofQuality = i);
        // pas enregistré : un outil de réglage, pas une préférence
        var keep = new CheckBox { Text = "Garder le repère sur la mer", FocusMode = Control.FocusModeEnum.None };
        keep.Toggled += on => _dofMarkerKeep = on;
        box.AddChild(keep);
        Check("Flou de mouvement", st.MotionBlur, on => st.MotionBlur = on);
        Slide("Obturateur", 0.05, 1, 0.05, st.Shutter, x => st.Shutter = x);
        Choice("Anticrénelage MSAA", new[] { "Aucun", "2×", "4×", "8×" },
            st.Msaa switch { 0 => 0, 2 => 1, 8 => 3, _ => 2 }, i => st.Msaa = i == 0 ? 0 : 1 << i);
        var aa = new[] { "aucun", "fxaa", "smaa", "taa" };
        Choice("Anticrénelage de l'image", new[] { "Aucun", "FXAA", "SMAA", "TAA" },
            Array.IndexOf(aa, st.ScreenAA), i => st.ScreenAA = aa[i]);
        Check("Lueur des lumières (la nuit)", st.Glow, on => st.Glow = on);
        Slide("Intensité de la lueur", 0, 3, 0.05, st.GlowStrength, x => st.GlowStrength = x);
        Check("Masque de cinéma 2,35:1", st.FilmMask, on => st.FilmMask = on);
        Check("Exposition automatique", st.AutoExposure, on => st.AutoExposure = on);
        Slide("Seuil d'exposition", 0.2, 3, 0.05, st.AutoExposureThreshold, x => st.AutoExposureThreshold = x);
        Slide("Vitesse d'adaptation", 0.1, 5, 0.1, st.AutoExposureSpeed, x => st.AutoExposureSpeed = x);
        Title("Mer", 15);
        var seaHint = new Label { Text = "Mise au point de la mer : ⇧M, un panneau sur le côté" };
        seaHint.AddThemeFontSizeOverride("font_size", 13);
        seaHint.AddThemeColorOverride("font_color", new Color(0.8f, 0.84f, 0.88f));
        box.AddChild(seaHint);
        Title("Navire", 15);
        // le pavillon à la barre : celui de la fiche, ou une nation de flags.json
        var nlabels = new List<string> { "Pavillon de la fiche" };
        foreach (var n in _nations.All) nlabels.Add(n.Pirate ? "Pavillon noir" : "Pavillon · " + (n.Pays ?? n.Id));
        Choice("Pavillon", nlabels.ToArray(), _nations.All.FindIndex(n => n.Id == st.Nation) + 1,
            i => { st.Nation = i == 0 ? "" : _nations.All[i - 1].Id; HoistNation(); });
        Title("Performance", 15);
        Check("Solveurs sur plusieurs cœurs", st.ParallelSolvers, on => st.ParallelSolvers = on);

        var path = new Label { Text = ProjectSettings.GlobalizePath(Settings.Path), AutowrapMode = TextServer.AutowrapMode.Arbitrary };
        path.AddThemeFontSizeOverride("font_size", 11);
        path.AddThemeColorOverride("font_color", new Color(0.7f, 0.74f, 0.78f));
        box.AddChild(path);

        var buttons = new HBoxContainer();
        var back = new Button { Text = "Reprendre", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        var quit = new Button { Text = "Quitter", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        back.Pressed += () => _menu.Visible = false;
        quit.Pressed += () => GetTree().Quit();
        buttons.AddChild(back);
        buttons.AddChild(quit);
        box.AddChild(buttons);
    }
}
