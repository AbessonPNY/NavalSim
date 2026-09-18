using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using NavalSim.Core;

namespace NavalSim;

/// <summary>
/// ELLE FLOTTE.
///
/// La mer du GPU, le solveur du noyau, et une coque qui trouve sa flottaison
/// toute seule — rien ici ne pose son tirant d'eau ni ne l'incline : elle
/// s'assoit là où la poussée équilibre son poids, et elle roule parce que la
/// surface bouge sous ses cinq cent trente-neuf sondes.
/// </summary>
public partial class ShipDemo : Node3D
{
    OceanNode _sea = null!;
    FoamField _foam = null!;
    SprayNode _spray = null!;
    SkyNode _sky = null!;
    ShipNode _ship = null!;
    Camera3D _cam = null!;
    Label _info = null!;

    double _t;
    double _force = 4, _windDeg = 210, _cloud = 0.06;   // 6 %, le reglage par defaut de la page d'origine
    /// <summary>
    /// LE CREUX, et il manquait — ce qui a coute une fausse piste entiere.
    ///
    /// C est un multiplicateur de la hauteur significative au-dela de la table
    /// Beaufort, et la console d origine le laisse aller de 0,4 a 2,6 pour 1,35
    /// par defaut. Je l avais laisse a sa valeur par defaut sans jamais
    /// l exposer, si bien qu on comparait une tempete a une AUTRE tempete :
    /// releve, force 9,9 passe de 13,5 m de Hs a 26,6 entre 1,35 et 2,6, et la
    /// coque de « 6 a 66 % d immersion » a « 0 a 100 ».
    /// </summary>
    double _swell = 1.35;
    float _orbit = 0.9f, _pitch = 0.16f, _dist = 55f;
    bool _dragging, _follow = true;
    double _hudAcc;
    // la machine imposee en ligne de commande ne doit pas etre effacee par la
    // lecture du clavier, qui la laisse ou elle est faute de touche pressee
    bool _drive;

    List<string> _paths = new();
    // la flotte telle que la mer la voit : l'entrée 0 est le navire commandé
    readonly List<ShipPhysics> _fleet = new();
    // le contour que la mer lit pour le navire commandé
    HullProfile _prof = null!;
    HullCollider? _hullCollider;
    int _index;

    /* LE SOUS-PAS NE DÉPASSE JAMAIS 1/15 s, et le compte en découle.
       C'est la règle énoncée plutôt que réglée : quatre sous-pas à soixante
       images, davantage si l'image est lente, ce qui redonne exactement le
       comportement d'origine à vitesse normale. */
    const int MinSub = 4;
    const double MaxSubDt = 1.0 / 15;

    public override void _ExitTree() { _motionBlur?.Release(); _anamorphic?.Release(); }

    public override void _Ready()
    {
        BuildScene();

        _sea = new OceanNode();
        AddChild(_sea);
        _sea.Core.SetSeaState(_force, _windDeg);

        _foam = new FoamField();
        AddChild(_foam);
        _sea.AttachFoam(_foam);

        _spray = new SprayNode();
        AddChild(_spray);

        _paths = ShipLibrary.Discover();
        GD.Print($"{_paths.Count} fiche(s) lue(s) dans {ShipLibrary.Folder}");
        Launch(0);
        // les réglages du fichier d'abord ; la ligne de commande, lue ensuite, a le dernier mot
        ApplySettings();
        SetupCapture();
    }

    void BuildScene()
    {
        /* LE CIEL EST UN NŒUD, et il porte tout : le dôme, le soleil, l'ambiante,
           la brume, le gros temps et les éclairs. La scène ne pose plus de
           lumière à la main — c'est ce qui garantit que le ciel qu'on voit, celui
           que la mer réfléchit et celui qui éclaire le pont sont le même. */
        _sky = new SkyNode();
        AddChild(_sky);

        _cam = new Camera3D { Current = true, Fov = 55, Far = 9000 };
        AddChild(_cam);
        /* LA PROFONDEUR DE CHAMP, native (CameraAttributesPractical), et LE FLOU DE
           MOUVEMENT, qui ne l'est pas : un calcul inséré dans le rendu
           (MotionBlurEffect). Tous deux se règlent dans reglages.ini et le menu. */
        _camAttr = new CameraAttributesPractical();
        _cam.Attributes = _camAttr;
        _motionBlur = new MotionBlurEffect();
        _anamorphic = new AnamorphicDofEffect();
        // la profondeur de champ avant le flou de mouvement, comme dans l'objectif puis l'obturateur
        _cam.Compositor = new Compositor { CompositorEffects = new Godot.Collections.Array<CompositorEffect> { _anamorphic, _motionBlur } };
        _dofMarker = new DofMarker();
        AddChild(_dofMarker);

        /* LE MASQUE DE CINÉMA, sous le texte du tableau de bord : deux bandes noires
           qui ramènent l'image au 2,35:1 du scope. Sur un écran plus large que ce
           format (rare), des bandes de côté. */
        var mask = new CanvasLayer { Layer = 0 };
        AddChild(mask);
        foreach (var r in _maskBars) { r.MouseFilter = Control.MouseFilterEnum.Ignore; mask.AddChild(r); }
        GetViewport().SizeChanged += LayoutMask;

        var layer = new CanvasLayer();
        AddChild(layer);
        _info = new Label { Position = new Vector2(18, 14) };
        _info.AddThemeFontSizeOverride("font_size", 15);
        _info.AddThemeColorOverride("font_color", new Color(0.94f, 0.96f, 0.98f));
        _info.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.85f));
        _info.AddThemeConstantOverride("outline_size", 5);
        layer.AddChild(_info);
        BuildSunPanel(layer);
        _settings = Settings.Load();
        BuildMenu(layer);
    }

    // ------------------------------------------------------------------
    //  LA FLOTTE D'ESSAI — --flotte N, pour le test de charge
    // ------------------------------------------------------------------

    /* D'AUTRES COQUES À FLOT, chacune entière : son solveur, sa toile, ses feux et
       son pendule, sa rangée dans la texture de profils, ses gerbes. Rangées par
       six, à 140 m par le travers et 160 m devant la nôtre, pour qu'elles soient
       dans l'image sans se toucher. La règle de la page tient : tout état d'un
       navire vit sur SON instance, et l'indice dans la flotte est la rangée de
       profil — au-delà de Config.MaxShips la mer ne les voit plus, mais elles
       flottent et se dessinent. */
    readonly List<ShipNode> _others = new();
    int _flotteShip = 5;    // la frégate du XVIIe : modèle, toile, feux, lanterne pendue

    /* LE MÉNAGE AU CHARGEMENT. Mettre une coque à l'eau laisse derrière soi des
       dizaines de mégaoctets morts — sommets relus, images de rugosité, tableaux
       de mesure : 394 Mo pour neuf frégates. Laissés au ramasse-miettes, ils
       partaient en pleine course, dans une collecte complète qui gelait une image
       de 38 à 42 ms. On la fait ici, pendant que rien ne bouge. */
    static void Sweep()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    void SpawnFleet(int n, int specIndex)
    {
        var spec = ShipLibrary.Load(_paths[(specIndex % _paths.Count + _paths.Count) % _paths.Count]);
        if (spec == null) return;
        for (int i = 0; i < n; i++)
        {
            var s = new ShipNode { LanternShadows = _settings.LanternShadows, WithMastLantern = _settings.MastLantern };
            AddChild(s);
            s.Build(spec);
            s.Ctrl.SailsSet = true;
            s.Ctrl.Sheet = 0.6;
            double y = s.Physics.Settle(_sea.Core, s.Ctrl);
            var b = s.Physics.Body;
            b.Pos = new Vec3d((i % 6 - 2.5) * 140, b.Pos.Y, 160 + (i / 6) * 180);
            s.SyncTransform();
            int row = _fleet.Count;
            var prof = s.MakeProfile(-y);
            if (row < Config.MaxShips) _sea.SetHullProfile(row, prof);
            _spray.Pool.Colliders.Add(s.MakeCollider(prof));
            _fleet.Add(s.Physics);
            s.Physics.OnSlam = QueueSlam;
            _others.Add(s);
        }
        GD.Print($"flotte d'essai : {n} × {spec.Name}");
        Sweep();
    }

    // ------------------------------------------------------------------
    //  LES SOLVEURS SUR PLUSIEURS CŒURS
    // ------------------------------------------------------------------

    /* CHAQUE SOLVEUR NE TOUCHE QU'À SON PROPRE ÉTAT et ne fait que LIRE la mer —
       vérifié dans le noyau : aucun champ statique modifiable, Sample n'écrit
       rien, et seul Settle modifie la mer, à la mise à l'eau. L'ordre des calculs
       ne peut donc rien changer : le labo (mode « parallele ») mène deux flottes
       identiques, l'une en série, l'autre en parallèle, et les trouve égales AU
       BIT PRÈS, de 1 à 32 galions ; ×3,1 à quatre navires, ×5,6 à trente-deux, sur
       vingt cœurs logiques. À un seul navire le parallélisme coûte (×0,9) : il ne
       joue qu'à partir de deux.

       Une seule chose sortait du navire pendant son pas : le choc qui jette une
       gerbe dans la réserve COMMUNE d'embrun. Il est mis en file pendant le calcul
       et versé ensuite, sur le fil principal. */
    readonly List<ShipNode> _stepping = new();
    readonly System.Collections.Concurrent.ConcurrentQueue<(Vec3d At, double Rate, double Speed)> _slamQueue = new();
    int _stepSub;
    double _stepDt, _stepT0;
    Action<int>? _stepOne;

    void QueueSlam(Vec3d at, double rate, double speed) => _slamQueue.Enqueue((at, rate, speed));

    void StepSolvers(int sub, double dt, double t0)
    {
        _stepping.Clear();
        _stepping.Add(_ship);
        _stepping.AddRange(_others);
        _stepSub = sub; _stepDt = dt; _stepT0 = t0;
        // un délégué gardé : en recréer un à chaque image serait de la mémoire à ramasser
        _stepOne ??= i =>
        {
            var s = _stepping[i];
            double t = _stepT0;
            for (int k = 0; k < _stepSub; k++) { s.Physics.Step(_stepDt, _sea.Core, s.Ctrl, t); t += _stepDt; }
        };
        if (_settings.ParallelSolvers && _stepping.Count > 1)
            System.Threading.Tasks.Parallel.For(0, _stepping.Count, _stepOne);
        else
            for (int i = 0; i < _stepping.Count; i++) _stepOne(i);

        // les gerbes, versées dans la réserve commune par un seul fil
        while (_slamQueue.TryDequeue(out var e))
        {
            _slams++;
            _spray.Pool.Burst(e.At, e.Rate * 0.12, e.Speed);
        }
    }

    void StepOthers(double frame)
    {
        foreach (var s in _others)
        {
            s.SyncTransform();
            var p = s.Physics;
            s.SetTrim(s.Ctrl.Sheet, p.Tack, p.SetFrac, p.Luffing, _t, p.SailLoad);
            s.SwingLanterns(frame);
        }
    }

    // ------------------------------------------------------------------
    //  LES RÉGLAGES — reglages.ini et le menu d'options (Échap)
    // ------------------------------------------------------------------

    Settings _settings = null!;
    CameraAttributesPractical _camAttr = null!;
    MotionBlurEffect _motionBlur = null!;
    AnamorphicDofEffect _anamorphic = null!;
    DofMarker _dofMarker = null!;
    readonly ColorRect[] _maskBars = { new() { Color = Colors.Black }, new() { Color = Colors.Black } };
    const float FilmAspect = 2.35f;

    void LayoutMask()
    {
        Vector2 s = GetViewport().GetVisibleRect().Size;
        bool on = _settings?.FilmMask == true;
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
    PanelContainer _menu = null!;
    // O et G changent ces deux-là au clavier : le menu les relit à l'ouverture
    CheckBox _chkOcclusion = null!, _chkIndirect = null!;

    /// <summary>Poser les réglages sur le ciel, la mer, l'affichage et le navire.</summary>
    void ApplySettings()
    {
        var s = _settings;
        _sky.Env.SsaoEnabled = s.Occlusion;
        _sky.Env.SsilEnabled = s.IndirectLight;
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
        _camAttr.DofBlurFarEnabled = godotDof && far;
        _camAttr.DofBlurFarDistance = far ? s.DofDistance : 8192;
        _camAttr.DofBlurFarTransition = Math.Max(0.01f, s.DofDistance * s.DofFade);
        _camAttr.DofBlurNearEnabled = godotDof && s.DofNear > 0;
        _camAttr.DofBlurNearDistance = s.DofNear;
        _camAttr.DofBlurNearTransition = Math.Max(0.01f, s.DofNear * s.DofFade);
        _camAttr.DofBlurAmount = s.DofAmount;
        _anamorphic.Enabled = s.Dof && s.DofAnamorphic && (far || s.DofNear > 0);
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

        /* LA LUEUR de bloom.js. Son seuil et son genou sont lus sur l'image
           AFFICHÉE (0,72 ± 0,12) ; la lueur de Godot lit l'image LINÉAIRE, avant
           l'encodage : les mêmes bornes, décodées, font 0,319 à 0,674. Godot fait
           lui aussi une bascule douce (smoothstep du seuil au seuil + échelle), mais
           sur le plus fort des trois canaux et non sur la luminance de l'œil. Le
           flou de la page (quart de résolution, deux passes séparables) s'étale
           sur ≈ 25 px en 1080p : les niveaux 2 et 3 de Godot, au quart et au
           huitième, couvrent la même largeur. */
        var env = _sky.Env;
        env.GlowHdrThreshold = 0.319f;
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
        _motionBlur.Enabled = s.MotionBlur;
        _motionBlur.Shutter = s.Shutter;
        _motionBlur.MainProjection = _cam.GetCameraProjection();
        _sea.Material?.SetShaderParameter(U.LampReflection, s.LampReflection);
        _sea.Material?.SetShaderParameter(U.LampWater, s.LampWater);
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
            var l = new Label { Text = text };
            l.AddThemeFontSizeOverride("font_size", size);
            l.AddThemeColorOverride("font_color", new Color(0.94f, 0.96f, 0.98f));
            box.AddChild(l);
            return l;
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
        Title("Lanternes", 15);
        Check("Ombres des lanternes", st.LanternShadows, on => st.LanternShadows = on);
        Check("Lanterne du grand mât", st.MastLantern, on => st.MastLantern = on);
        Slide("Reflet sur la mer", 0, 6, 0.1, st.LampReflection, x => st.LampReflection = x);
        Slide("Lumière dans l'eau", 0, 2, 0.05, st.LampWater, x => st.LampWater = x);
        Title("Rendu", 15);
        _chkOcclusion = Check("Occlusion ambiante", st.Occlusion, on => st.Occlusion = on);
        _chkIndirect = Check("Lumière indirecte", st.IndirectLight, on => st.IndirectLight = on);
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

    // ------------------------------------------------------------------
    //  LE SOLEIL À LA MAIN — les curseurs « Hauteur du soleil » et
    //  « Défilement du jour » de la console d'origine
    // ------------------------------------------------------------------

    HSlider _sunElev = null!, _sunSpeed = null!;
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

        _sunVal = Row(box, "Hauteur du soleil");
        /* Jusqu'à −40 : à cette latitude le soleil descend vraiment aussi bas, et
           un curseur arrêté à −10 montrerait une nuit figée au crépuscule. */
        _sunElev = Slider(box, -40, 80, 1, _sky.Core.SunElevDeg);
        _sunSpeedVal = Row(box, "Défilement du jour");
        _sunSpeed = Slider(box, 0, 16, 0.5, _sky.DayRate);

        /* PRENDRE LE SOLEIL EN MAIN ARRÊTE L'HORLOGE, pour la même raison que la
           mer : être réécrit un cinquième de seconde plus tard n'est pas une
           interface. La hauteur change, le relèvement reste le sien. */
        _sunElev.DragStarted += () => { _sky.DayRate = 0; _sunSpeed.SetValueNoSignal(0); ShowSunSpeed(); };
        _sunElev.ValueChanged += v =>
        {
            _sky.DayRate = 0; _sunSpeed.SetValueNoSignal(0); ShowSunSpeed();
            _sky.Core.SetSun(v, _sky.Core.SunBearingDeg);
            _sky.Apply();
            ShowSun(v);
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

    void ShowSun(double e) =>
        _sunVal.Text = e < 0 ? $"nuit {Math.Round(e)}°" : $"{Math.Round(e)}°";

    /// <summary>
    /// Le taux ÉNONCÉ : « ×2 » ne veut rien dire si « ×1 » ne dit pas quoi —
    /// une minute réelle pour une heure. L'heure du ciel à côté.
    /// </summary>
    void ShowSunSpeed()
    {
        double v = _sky.DayRate;
        double h = _sky.Core.DayTime;
        string clock = $"{(int)h:00}:{(int)((h - (int)h) * 60):00}";
        _sunSpeedVal.Text = v <= 0 ? $"arrêt · {clock}"
            : $"×{(v % 1 != 0 ? v.ToString("F1") : v.ToString("F0"))} · {Math.Round(24 / v)} min/jour · {clock}";
    }

    /// <summary>Le curseur suit le soleil quand le jour tourne, quatre fois par seconde.</summary>
    void TickSunPanel(double dt)
    {
        _sunTick += dt;
        if (_sunTick < 0.25) return;
        _sunTick = 0;
        if (_sky.DayRunning)
        {
            _sunElev.SetValueNoSignal(Math.Round(_sky.Core.SunElevDeg));
            ShowSun(_sky.Core.SunElevDeg);
        }
        ShowSunSpeed();
    }

    /// <summary>
    /// METTRE UNE COQUE À L'EAU, et l'ordre compte.
    ///
    /// <c>Settle()</c> la laisse trouver sa flottaison en eau PLATE — sans quoi
    /// elle arrive en plein ciel et rebondit — puis remet la mer comme elle
    /// était, ce que le noyau fait lui-même pour qu'aucun appelant n'ait à le
    /// savoir.
    /// </summary>
    void Launch(int i)
    {
        if (_paths.Count == 0) { _info.Text = "aucune fiche trouvée"; return; }
        _index = (i % _paths.Count + _paths.Count) % _paths.Count;
        var spec = ShipLibrary.Load(_paths[_index]);
        if (spec == null) return;

        if (_ship != null) { RemoveChild(_ship); _ship.QueueFree(); }

        _ship = new ShipNode();
        AddChild(_ship);
        _ship.LanternShadows = _settings.LanternShadows;
        _ship.WithMastLantern = _settings.MastLantern;
        _ship.Build(spec);
        _ship.Ctrl.SailsSet = false;
        _ship.Ctrl.Sheet = 0.6;

        double y = _ship.Physics.Settle(_sea.Core, _ship.Ctrl);
        _ship.SyncTransform();
        GD.Print($"{spec.Name} : assise à y = {y:F3} m, tirant {_ship.Physics.Draft:F2} m, "
               + $"immersion {_ship.Physics.SubmergedFrac * 100:F1} %");

        /* SON CONTOUR À LA MER, dans sa rangée : le collier suit le bordé qu'on
           VOIT — mesuré sur le modèle s'il en porte un, lu dans le plan de formes
           sinon —, à la flottaison qu'elle vient de trouver. */
        _prof = _ship.MakeProfile(-y);
        _sea.SetHullProfile(0, _prof);
        // et l'embrun ne la traverse plus : il rebondit sur son bordé ou reste sur son pont
        if (_hullCollider != null) _spray.Pool.Colliders.Remove(_hullCollider);
        _hullCollider = _ship.MakeCollider(_prof);
        _spray.Pool.Colliders.Add(_hullCollider);
        string what = _ship.ModelRoot != null
            ? $"modèle {spec.Model!.Glb}, échelle {_ship.ModelRoot.Scale.X:F4}"
            : "coque procédurale";
        GD.Print($"  {what} ; profil : demi-largeur {_prof.MaxHalfB:F3} m (fiche {spec.B * 0.5:F3}), "
               + $"corps {_prof.EndAft:F2} à {_prof.EndFwd:F2} m");
        _fleet.Clear();
        _fleet.Add(_ship.Physics);
        /* LA COQUE QUI TAPE JETTE DE L'EAU — wireSplash : le solveur dit combien
           d'eau elle vient de chasser et à quelle vitesse, la réserve en fait une
           gerbe. Toute coque le fait, pas seulement la nôtre. */
        _ship.Physics.OnSlam = QueueSlam;

        _dist = (float)spec.L * 1.8f;
        // un nouveau navire n'est pas là où était l'ancien : reprendre la station
        if (_fixed) Plant();
        // et ses vues à bord sont les SIENNES : la même, si elle en a autant, sinon la première
        if (_camMode == 1) { if (_deck >= spec.Decks.Count) _deck = 0; EnterDeck(); }
        Sweep();
        UpdateInfo();
    }

    public override void _Process(double delta)
    {
        FrameStats(delta);
        _ftWatch.Restart();
        long ap0 = GC.GetAllocatedBytesForCurrentThread();

        /* Le pas d'image est PLAFONNÉ à cinquante millisecondes, pour protéger le
           solveur d'une saccade : une image d'une seconde ferait faire à la
           coque un bond d'une seconde, ressorts raides compris. */
        double frame = Math.Min(0.05, delta);
        _t += frame;

        ReadKeys(frame);

        // --- le solveur, en sous-pas ---
        int sub = Math.Max(MinSub, (int)Math.Ceiling(frame / MaxSubDt));
        double dt = frame / sub;
        double t = _t - frame;
        // ce que chaque étage alloue, pour --frametimes : le solveur doit rester à zéro
        long a0 = GC.GetAllocatedBytesForCurrentThread();
        StepSolvers(sub, dt, t);
        long a1 = GC.GetAllocatedBytesForCurrentThread();
        _ship.SyncTransform();

        /* LA TOILE SUIT LE SOLVEUR : l'écoute, le bord, la toile établie, le
           faseyement et la charge sont lus à chaque image, jamais retenus — la
           voile se gonfle parce qu'on la borde, avec la même pression qui pousse
           le navire, sans seconde règle à tenir d'accord. */
        var ph = _ship.Physics;
        _ship.SetTrim(_ship.Ctrl.Sheet, ph.Tack, ph.SetFrac, ph.Luffing, _t, ph.SailLoad);
        // les lanternes pendues suivent le roulis en vrais pendules
        _ship.SwingLanterns(frame);
        StepOthers(frame);
        long a2 = GC.GetAllocatedBytesForCurrentThread();
        _allocPhys += a1 - a0; _allocSails += a2 - a1; _allocFrames++;

        /* L'ORIGINE FLOTTANTE. Au-delà de REBASE_RADIUS le monde entier glisse
           sous la flotte, et TOUT CE QUI TIENT UNE POSITION doit se décaler dans
           la MÊME image — sans quoi il se retrouve en désaccord avec la mer
           d'exactement ce décalage. Ici : la coque, la mer, le champ d'écume et
           la caméra fixe — et c'est la liste sur laquelle ce projet a déjà
           oublié quelque chose deux fois. */
        var b = _ship.Physics.Body;
        if (Math.Abs(b.Pos.X) > Config.RebaseRadius || Math.Abs(b.Pos.Z) > Config.RebaseRadius)
        {
            double dx = -b.Pos.X, dz = -b.Pos.Z;
            _sea.Core.Rebase(-dx, -dz);
            _foam.Rebase((float)-dx, (float)-dz);
            _spray.Pool.Rebase(-dx, -dz);
            // la seule chose qui ne suit PAS le navire : sans ceci elle resterait
            // à quinze cents mètres, à filmer de l'eau vide
            _anchor = new Vec3d(_anchor.X + dx, _anchor.Y, _anchor.Z + dz);
            b.Pos = new Vec3d(b.Pos.X + dx, b.Pos.Y, b.Pos.Z + dz);
            foreach (var s in _others)
            {
                var ob = s.Physics.Body;
                ob.Pos = new Vec3d(ob.Pos.X + dx, ob.Pos.Y, ob.Pos.Z + dz);
                s.SyncTransform();
            }
            _ship.SyncTransform();
            GD.Print($"recentrage : origine désormais ({_sea.Core.Origin.X:F0}, {_sea.Core.Origin.Z:F0}) m");
        }

        UpdateCamera(frame);
        // les navires et la caméra de la MÊME image : le flou compare les deux
        _motionBlur.BeginShips();
        _motionBlur.AddShip(_ship.GlobalTransform, _ship.LocalBounds());
        foreach (var s in _others) _motionBlur.AddShip(s.GlobalTransform, s.LocalBounds());
        _motionBlur.EndShips();
        // les vues à bord ont leur propre champ : la vérification lit celui d'ICI
        _motionBlur.MainProjection = _cam.GetCameraProjection();
        _sea.UpdateFrom(_cam.GlobalPosition, _t);
        // après le recentrage : la mer et le champ lisent la coque où elle EST
        _sea.TrackShips(_fleet);
        // la cible rendue à l'image d'avant, avec l'heure et l'ancre de CETTE passe
        if (_foamCheckIn > 0 && --_foamCheckIn == 0) { FoamCheck(); return; }
        // le champ suit la coque commandée, et passe AVANT la mer qui le lit
        _foam.Step(frame, _sea, _ship.Position);
        _foamT = _t;
        _foamOrigin = _foam.Origin;
        _foamShip = _ship.Position;
        _sea.SyncFoam();

        /* LE MÊME CIEL PARTOUT, une fois par image. La mer le réfléchit, la
           coque respire sa brume, le dôme le dessine — et c est SkyNode qui
           écrit les trois, faute de quoi ils dériveraient en silence. */
        _sky.UpdateWeather(frame, _force);
        TickSunPanel(frame);
        /* La lueur n'existe pas le jour — bloom.js saute sa passe tant que la nuit
           n'a pas passé 0,02 : le soleil sur la houle déborderait le seuil et
           voilerait la mer. Allumée aux premières images pour être compilée. */
        if (_glowPrime > 0) _glowPrime--;
        _sky.Env.GlowEnabled = _settings.Glow && (_glowPrime > 0 || _sky.Core.Night > 0.02);
        _dofMarker.Visible = _settings.Dof && (_menu.Visible || _dofMarkerKeep);
        if (_dofMarker.Visible)
            _dofMarker.Draw(_cam, _sea.Core, _t, _settings.DofNear, _settings.DofDistance, _settings.DofFade);
        // les feux et les fenêtres suivent la nuit du ciel, et s'effacent au loin
        _ship.SetLantern(_sky.Core.Night, _t, _cam.GlobalPosition, _sky.Core);
        foreach (var s in _others) s.SetLantern(_sky.Core.Night, _t, _cam.GlobalPosition, _sky.Core);
        // et la mer les voit : leur reflet et leur lumière sur l'eau
        int lamps = _ship.FillLamps(_sea.Lamps, _sea.LampRange, 0);
        foreach (var s in _others) lamps += s.FillLamps(_sea.Lamps, _sea.LampRange, lamps);
        _sea.PushLamps(lamps);
        _sky.PushTo(_sea.Material);
        _sky.SetCloud(_sea.Material, _cloud, _t);
        foreach (var m in _ship.Hazed) { _sky.PushTo(m); _sky.SetCloud(m, _cloud, _t); }
        foreach (var s in _others)
            foreach (var m in s.Hazed) { _sky.PushTo(m); _sky.SetCloud(m, _cloud, _t); }
        // l'embrun est aussi clair que ce qui l'éclaire : l'horizon, qui porte l'heure
        _sky.PushTo(_spray.Material);
        _spray.Step(frame);
        if (_spray.Pool.Count > _sprayMax) _sprayMax = _spray.Pool.Count;
        _sea.Material?.SetShaderParameter(U.Ripple,
            (float)Math.Min(2.6, 0.40 + _sea.Core.WindSpeed * 0.105));
        var wv = _sea.Core.WindVec;
        double ws = Math.Sqrt(wv.X * wv.X + wv.Z * wv.Z);
        if (ws > 1e-4)
            _sea.Material?.SetShaderParameter(U.Wind,
                new Vector2((float)(wv.X / ws), (float)(wv.Z / ws)));

        _hudAcc += frame;
        if (_hudAcc > 0.15) { _hudAcc = 0; UpdateInfo(); }

        TickCapture();
        _ftWatch.Stop();
        _allocProc += GC.GetAllocatedBytesForCurrentThread() - ap0;
    }

    /* LA VUE FIXE de camera-rig.js (mode 3). Plantée dans le monde, position et
       relèvement verrouillés, pointée sur elle UNE fois puis laissée tranquille :
       elle s'éloigne et sort du champ, et c'est tout l'intérêt — c'est la seule
       vue où l'on voit le navire AVANCER par rapport à une mer immobile, au
       lieu d'une mer qui défile sous une coque clouée au centre de l'image. */
    // 0 orbite, 1 à bord (les vues de la fiche), 2 fixe — l'ordre du bouton de la page
    int _camMode;
    bool _fixed => _camMode == 2;
    Vec3d _anchor;
    double _fixYaw, _fixPitch;

    /// <summary>
    /// Prendre la station : sur sa hanche, à 26 m par le travers et 34 m en
    /// arrière pour un navire de 24 m, en proportion pour les autres — un
    /// bâtiment qui s'éloigne en diagonale rapetisse au lieu de traverser le
    /// cadre et d'en sortir tout droit.
    ///
    /// La hauteur est prise sur la MER sous la station, pas sur la coque, puis
    /// n'en bouge plus : c'est un pied posé, pas une bouée. Par creux extrême une
    /// crête peut donc passer devant l'objectif, comme dans l'original.
    /// </summary>
    void Plant()
    {
        var b = _ship.Physics.Body;
        double k = _ship.Spec.L / 24;
        /* (1,0,0) local, comme l'original. Son commentaire dit « tribord », mais
           tribord est −x local dans ce projet : c'est donc la hanche BÂBORD.
           Porté tel quel, signalé plutôt que corrigé en silence. */
        Vec3d r = b.Quat.Rotate(new Vec3d(1, 0, 0));
        Vec3d f = b.Quat.Rotate(new Vec3d(0, 0, 1));
        double ax = b.Pos.X + r.X * 26 * k - f.X * 34 * k;
        double az = b.Pos.Z + r.Z * 26 * k - f.Z * 34 * k;
        double ay = _sea.Core.Sample(ax, az, _t) + 12 * k;
        _anchor = new Vec3d(ax, ay, az);

        double dx = b.Pos.X - ax, dz = b.Pos.Z - az;
        double dy = b.Pos.Y + 2 * k - ay;
        _fixYaw = Math.Atan2(dx, dz);
        _fixPitch = Math.Atan2(dy, Math.Sqrt(dx * dx + dz * dz));
    }

    // ------------------------------------------------------------------
    //  LES VUES À BORD — camera.decks de la fiche, mode 2 de camera-rig.js
    // ------------------------------------------------------------------

    /* SES PROPRES POINTS DE VUE, dans SA fiche : la passerelle, la chambre du
       capitaine, ce que le modéliste a prévu de montrer. Chaque vue donne l'œil
       dans le repère du navire et où il regarde ; le regard est pris dans ce
       repère, donc la vue suit le pont. Le glisser regarde autour À PARTIR de ce
       regard, la molette change la focale, qui est rendue en sortant. */
    int _deck;
    double _bridgeYaw, _bridgePitch;
    const float OutsideFov = 55, OutsideNear = 0.05f;

    string CamName() => _camMode switch
    {
        1 => _ship.Spec.Decks[_deck].Name,
        2 => "Fixe",
        _ => "Orbite"
    };

    /// <summary>
    /// C : Orbite, puis chaque vue à bord de la fiche dans l'ordre, puis Fixe —
    /// le cycle du bouton caméra de la page, qui parcourt ses vues à bord avant de
    /// passer au mode suivant. La Proue de la page n'est pas portée.
    /// </summary>
    void CycleCamera()
    {
        if (_camMode == 0) { _camMode = 1; _deck = 0; EnterDeck(); }
        else if (_camMode == 1 && _deck + 1 < _ship.Spec.Decks.Count) { _deck++; EnterDeck(); }
        else if (_camMode == 1) { _camMode = 2; SetLens(OutsideFov, OutsideNear); Plant(); }
        else _camMode = 0;
        UpdateInfo();
    }

    /* Entrer dans une vue : regard remis droit devant ELLE, sa focale et son plan
       proche. Un intérieur en demande un bien plus court que la mer : une
       cloison à portée de main serait coupée net. */
    void EnterDeck()
    {
        var v = _ship.Spec.Decks[_deck];
        _bridgeYaw = 0; _bridgePitch = 0;
        SetLens((float)(v.Fov ?? OutsideFov), (float)(v.Near ?? OutsideNear));
    }

    void SetLens(float fov, float near) { _cam.Fov = fov; _cam.Near = near; }

    void DeckCamera()
    {
        var spec = _ship.Spec;
        var v = spec.Decks[_deck];
        double k = spec.L / 24;
        // l'œil : hauteur au-dessus de la flottaison ; absente, l'ancienne règle de la passerelle
        double x = v.X ?? (v.XFrac ?? 0) * spec.B;
        double z = v.Z ?? (v.ZFrac ?? spec.Camera.HelmZFrac ?? -0.4) * spec.L;
        double y = v.Y ?? spec.DeckMid + 2.1 * k;
        var eye = new Vector3((float)x, (float)y, (float)z);
        double yaw = v.Yaw * Math.PI / 180 + _bridgeYaw, pitch = v.Pitch * Math.PI / 180 + _bridgePitch;
        double cp = Math.Cos(pitch);
        var dir = new Vector3((float)(Math.Sin(yaw) * cp), (float)Math.Sin(pitch), (float)(Math.Cos(yaw) * cp));
        var xf = _ship.GlobalTransform;
        _cam.Position = xf * eye;
        _cam.LookAt(xf * (eye + dir * (float)(120 * k)), Vector3.Up);
    }

    void UpdateCamera(double delta)
    {
        var target = _follow
            ? _ship.Position + new Vector3(0, (float)(_ship.Spec.D * 0.3), 0)
            : Vector3.Zero;

        /* PAS DE ROTATION AUTOMATIQUE ICI, et c'est un retrait plutôt qu'un
           oubli. L'aperçu du plan de formes en a une, à juste titre : la coque y
           est immobile et tourner lentement autour d'elle est ce qui montre sa
           forme. Recopiée dans une scène où l'objet regardé peut tourner
           lui-même, elle devient ambiguë PAR CONSTRUCTION — elle attribue au
           navire un mouvement qui est celui de la caméra, et cela s'est signalé
           à l'usage sous la forme « le navire de 2 000 t tourne sur lui-même ».

           Vérifié au solveur après coup : deux minutes de calme plat, barre au
           milieu, cap immobile et vitesse angulaire résiduelle de 5e-9 rad/s.
           Elle ne tournait pas. Une caméra qui bouge toute seule autour d'une
           chose capable de bouger est un instrument qui ment. */
        if (_fixEye is Vector3 fe)
        {
            _cam.Position = fe;
            _cam.LookAt(_fixLook ?? Vector3.Zero, Vector3.Up);
            return;
        }

        if (_camMode == 1)
        {
            DeckCamera();
            return;
        }

        if (_fixed)
        {
            // la position ET le relèvement sont verrouillés : rien ne suit
            double cp = Math.Cos(_fixPitch), reach = 200 * _ship.Spec.L / 24;
            var a = _anchor;
            _cam.Position = new Vector3((float)a.X, (float)a.Y, (float)a.Z);
            _cam.LookAt(new Vector3(
                (float)(a.X + Math.Sin(_fixYaw) * cp * reach),
                (float)(a.Y + Math.Sin(_fixPitch) * reach),
                (float)(a.Z + Math.Cos(_fixYaw) * cp * reach)), Vector3.Up);
            return;
        }

        float h = _dist * Mathf.Sin(_pitch);
        float r = _dist * Mathf.Cos(_pitch);
        var eye = target + new Vector3(Mathf.Sin(_orbit) * r, Mathf.Max(2f, h), Mathf.Cos(_orbit) * r);
        _cam.Position = eye;
        _cam.LookAt(target, Vector3.Up);
    }

    /// <summary>
    /// La barre, la machine et les écoutes. Ce sont des touches TENUES, donc
    /// lues ici et non dans les événements : une commande de barre qui compte les
    /// événements de répétition du clavier dépendrait de la vitesse de répétition
    /// du système.
    /// </summary>
    void ReadKeys(double dt)
    {
        var c = _ship.Ctrl;

        double rud = 0;
        if (Physical(Key.A)) rud -= 1;
        if (Physical(Key.D)) rud += 1;
        // la barre revient d'elle-même quand on la lâche, comme une roue qu'on rend
        c.Rudder = rud != 0
            ? Math.Clamp(c.Rudder + rud * dt * 1.6, -1, 1)
            : c.Rudder * (1 - Math.Min(1, dt * 2.5));

        if (Physical(Key.W)) c.Throttle = Math.Min(1, c.Throttle + dt * 0.8);
        if (Physical(Key.S)) c.Throttle = Math.Max(-1, c.Throttle - dt * 0.8);

        if (Physical(Key.Q)) c.Sheet = Math.Max(0, c.Sheet - dt * 0.8);
        if (Physical(Key.E)) c.Sheet = Math.Min(_ship.Spec.MaxSheet, c.Sheet + dt * 0.8);
    }

    /// <summary>
    /// LA TOUCHE PHYSIQUE, ET NON SON ÉTIQUETTE — et l'avoir écrit autrement a
    /// rendu le navire incommandable sur un clavier français.
    ///
    /// <c>Input.IsKeyPressed</c> teste le code LOGIQUE, c'est-à-dire ce que la
    /// disposition inscrit sur la touche. Sur AZERTY la touche qui occupe la
    /// position du W de QWERTY est étiquetée Z, celle du A est étiquetée Q, et
    /// celle du Q est étiquetée A. Une commande écrite en W-A-S-D y devient donc
    /// muette sur trois de ses quatre touches — et pire que muette : la touche
    /// sous l'index gauche rend « Q », si bien qu'en cherchant la barre on borde
    /// les écoutes. Signalé à l'usage sous la forme « je n'arrive pas à la faire
    /// avancer », ce qui est exactement le symptôme.
    ///
    /// <c>IsPhysicalKeyPressed</c> désigne l'EMPLACEMENT, donc le même doigt sur
    /// la même touche quel que soit le pays. C'est le pendant exact du choix de
    /// <c>e.code</c> plutôt que <c>e.key</c> côté navigateur, que ce projet a
    /// déjà payé une fois pour la rangée des chiffres.
    /// </summary>
    static bool Physical(Key k) => Input.IsPhysicalKeyPressed(k);

    void UpdateInfo()
    {
        if (_ship == null) return;
        var p = _ship.Physics;
        var b = p.Body;
        var (heel, trim, hdg) = _ship.Attitude();

        double speedKn = Math.Sqrt(b.Vel.X * b.Vel.X + b.Vel.Z * b.Vel.Z) * Config.MsToKn;
        int bf = Mathf.Clamp((int)Math.Round(_force), 0, 9);

        string voiles = p.SetFrac > 0.99 ? "établies"
                      : p.SetFrac < 0.01 ? "ferlées"
                      : $"{p.SetFrac * 100:F0} %";

        _info.Text =
            $"{_ship.Spec.Name}   ({_index + 1}/{_paths.Count})\n" +
            // la moyenne du moteur sur la dernière seconde, et le temps qu'elle vaut
            $"{Engine.GetFramesPerSecond(),4:F0} images/s   {1000.0 / Math.Max(1, Engine.GetFramesPerSecond()),5:F1} ms\n" +
            $"\n" +
            $"cap        {hdg,6:F0}°      vitesse   {speedKn,5:F1} nds\n" +
            $"gîte       {heel,6:F1}°      assiette  {trim,5:F1}°\n" +
            $"tirant     {p.Draft,6:F2} m    immersion {p.SubmergedFrac * 100,5:F1} %\n" +
            $"déplacement{b.Mass / 1000,6:F0} t\n" +
            $"\n" +
            $"machine    {_ship.Ctrl.Throttle,6:F2}      barre     {_ship.Ctrl.Rudder,5:F2}\n" +
            $"écoutes    {_ship.Ctrl.Sheet,6:F2}      voiles    {voiles}\n" +
            $"vent       {_windDeg,6:F0}°      force     {_force:F1} · {Config.Beaufort[bf].Name}\n" +
            $"\n" +
            $"W S machine   B élan   A D barre   Q E écoutes   V voiles\n" +
            $"↑↓ force   ←→ vent   PgUp/PgDn creux   N navire   F suivre   C vues ({CamName()})   X replanter   Échap options\n" +
            $"O occlusion {(_sky.Env.SsaoEnabled ? "oui" : "non")}   G lumière indirecte {(_sky.Env.SsilEnabled ? "oui" : "non")}";
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (e is InputEventKey k && k.Pressed && !k.Echo)
        {
            /* Même raison que ci-dessus : l'emplacement, pas l'étiquette. Les
               flèches et Échap sont au même endroit partout, mais V, N et F ne le
               sont pas sur toutes les dispositions, et un seul jeu de règles vaut
               mieux que deux.

               Avec un REPLI sur le code logique quand l'emplacement est vide —
               ce n'est pas de la superstition : les événements fabriqués par un
               outil d'automatisation n'ont pas toujours de code physique, et le
               projet a déjà rencontré exactement ce cas côté navigateur. */
            Key key = k.PhysicalKeycode != Key.None ? k.PhysicalKeycode : k.Keycode;
            switch (key)
            {
                case Key.Up: _force = Math.Min(9.9, _force + 0.5); Restate(); break;
                case Key.Down: _force = Math.Max(0, _force - 0.5); Restate(); break;
                case Key.Left: _windDeg = (_windDeg - 15 + 360) % 360; Restate(); break;
                case Key.Right: _windDeg = (_windDeg + 15) % 360; Restate(); break;
                // Page Haut / Page Bas : ces deux-la portent le meme nom et occupent
                // la meme place sur toute disposition, ce qui evite la question
                // AZERTY entierement.
                case Key.Pageup: _swell = Math.Min(2.6, _swell + 0.15); Restate(); break;
                case Key.Pagedown: _swell = Math.Max(0.4, _swell - 0.15); Restate(); break;
                case Key.V: _ship.Ctrl.SailsSet = !_ship.Ctrl.SailsSet; break;
                case Key.N: Launch(_index + 1); break;
                case Key.F: _follow = !_follow; break;
                case Key.C: CycleCamera(); break;
                case Key.X: if (_fixed) Plant(); break;
                // l'occlusion ambiante et l'illumination globale, pour juger à l'œil
                case Key.O: _settings.Occlusion = !_settings.Occlusion; Changed(); break;
                case Key.G: _settings.IndirectLight = !_settings.IndirectLight; Changed(); break;
                /* L'ÉLAN : l'équivalent de `Naval.app.controls.state.throttle = 45`
                   dans la console d'origine. Le solveur ne borne pas la machine,
                   donc c'est quarante-cinq fois la poussée — de quoi voir une coque
                   lancée dans la houle sans attendre qu'elle prenne son erre. Un
                   second appui coupe, sans quoi elle filerait sans fin ; W et S
                   la ramènent aussi dans leur plage en la touchant. */
                case Key.B: _ship.Ctrl.Throttle = _ship.Ctrl.Throttle > 1 ? 0 : 45; break;
                // le menu d'options ; « Quitter » y est désormais
                case Key.Escape:
                    _chkOcclusion.SetPressedNoSignal(_settings.Occlusion);
                    _chkIndirect.SetPressedNoSignal(_settings.IndirectLight);
                    _menu.Visible = !_menu.Visible;
                    break;
            }
        }
        if (e is InputEventMouseButton mb)
        {
            if (mb.ButtonIndex == MouseButton.Left) _dragging = mb.Pressed;
            // à bord la molette change la focale, comme dans la page ; dehors, la distance
            else if (mb.ButtonIndex == MouseButton.WheelUp)
            {
                if (_camMode == 1) _cam.Fov = Mathf.Clamp(_cam.Fov - 2, 12, 75);
                else _dist = Mathf.Max(10f, _dist * 0.9f);
            }
            else if (mb.ButtonIndex == MouseButton.WheelDown)
            {
                if (_camMode == 1) _cam.Fov = Mathf.Clamp(_cam.Fov + 2, 12, 75);
                else _dist = Mathf.Min(900f, _dist * 1.11f);
            }
        }
        if (e is InputEventMouseMotion mm && _dragging)
        {
            if (_camMode == 1)
            {
                // regarder autour À PARTIR du regard de la vue
                _bridgeYaw -= mm.Relative.X * 0.004;
                _bridgePitch = Math.Clamp(_bridgePitch - mm.Relative.Y * 0.004, -0.7, 0.7);
            }
            else if (_fixed)
            {
                // pointer une caméra plantée à la main, comme dans l'original
                _fixYaw -= mm.Relative.X * 0.004;
                _fixPitch = Math.Clamp(_fixPitch - mm.Relative.Y * 0.004, -0.9, 0.9);
            }
            else
            {
                _orbit -= mm.Relative.X * 0.008f;
                _pitch = Mathf.Clamp(_pitch + mm.Relative.Y * 0.004f, 0.01f, 1.3f);
            }
        }
    }

    /// <summary>
    /// Reposer le spectre en lui rendant l'heure courante : la correction de
    /// bande est calculée contre l'horloge, et reconstruire avec un t faux ferait
    /// sauter la mer d'un radian entier.
    /// </summary>
    void Restate()
    {
        _sea.Core.Time = _t;
        _sea.Core.Swell = _swell;
        _sea.Core.SetSeaState(_force, _windDeg);
        UpdateInfo();
    }

    // --- la capture en ligne de commande, comme dans SeaDemo ---
    string _capturePath = "";
    int _captureIn = -1;

    void SetupCapture()
    {
        var args = OS.GetCmdlineUserArgs();
        for (int i = 0; i < args.Length - 1; i++)
            switch (args[i])
            {
                case "--capture": _capturePath = args[i + 1]; if (_captureIn < 0) _captureIn = 60; break;
                // combien d images la laisser vivre avant de la photographier : une
                // coque met du temps a prendre son erre, et une seconde de
                // simulation ne montre qu une voilure a moitie etablie
                case "--after": _captureIn = args[i + 1].ToInt(); break;
                case "--force": _force = args[i + 1].ToFloat(); Restate(); break;
                case "--swell": _swell = args[i + 1].ToFloat(); Restate(); break;
                case "--pitch": _pitch = args[i + 1].ToFloat(); break;
                case "--dist": _dist = args[i + 1].ToFloat(); break;
                case "--orbit": _orbit = args[i + 1].ToFloat(); break;
                case "--ship": Launch(args[i + 1].ToInt()); break;
                case "--sails": _ship.Ctrl.SailsSet = args[i + 1] == "1"; break;
                // la machine en ligne de commande : une capture « en route » ne
                // peut pas dependre du clavier, et un banc non plus
                case "--throttle": _ship.Ctrl.Throttle = args[i + 1].ToFloat(); _drive = true; break;
                // un oeil FIXE dans le monde, pour comparer au pixel avec la page
                // d'origine : une camera qui suit une coque soulevee de quarante
                // metres se retrouve dans la vague, et la comparaison ne vaut rien
                case "--eye": _fixEye = ParseVec(args[i + 1]); break;
                case "--look": _fixLook = ParseVec(args[i + 1]); break;
                case "--foamcheck": _foamCheckIn = args[i + 1].ToInt(); break;
                // le profil de flottaison, station par station, à poser à côté de
                // ship.hullProfile(64, -eq) dans la page
                // la régularité des images, mesurée : voir FrameStats
                // sans synchro verticale : pour mesurer ce que la machine tient vraiment
                case "--vsync": DisplayServer.WindowSetVsyncMode(args[i + 1] == "0" ? DisplayServer.VSyncMode.Disabled : DisplayServer.VSyncMode.Enabled); break;
                case "--ssao": _sky.Env.SsaoEnabled = args[i + 1] == "1"; break;
                case "--ssil": _sky.Env.SsilEnabled = args[i + 1] == "1"; break;
                case "--frametimes": _ftLeft = args[i + 1].ToInt(); _ftGc0 = GC.GetTotalPauseDuration(); break;
                // le soleil figé à cette hauteur : pour éprouver la nuit sans attendre
                case "--sun": _sky.DayRate = 0; _sky.Core.SetSun(args[i + 1].ToFloat(), _sky.Core.SunBearingDeg); _sky.Apply();
                    GD.Print(FormattableString.Invariant($"nuit {_sky.Core.Night:F2}, lune {(_sky.Core.MoonOn ? "oui" : "non")} phase {_sky.Core.MoonPhase:F2} levée {_sky.Core.MoonUp:F2}, lumière de l'eau {_sky.Core.WaterLight:F3}, lumière directe {_sky.Core.SunIntensity:F3}"));
                    break;
                // ouvrir directement une vue à bord de la fiche
                case "--vue": _camMode = 1; _deck = Math.Clamp(args[i + 1].ToInt(), 0, _ship.Spec.Decks.Count - 1); EnterDeck(); break;
                case "--msaa": _settings.Msaa = args[i + 1].ToInt(); ApplySettings(); break;
                case "--dofn": _settings.DofNear = args[i + 1].ToFloat(); ApplySettings(); break;
                case "--dofd": _settings.DofDistance = args[i + 1].ToFloat(); ApplySettings(); break;
                case "--anamorph": _settings.DofAnamorphic = args[i + 1] == "1"; ApplySettings(); break;
                case "--dofq": _settings.DofQuality = args[i + 1].ToInt(); ApplySettings(); break;
                case "--aa": _settings.ScreenAA = args[i + 1]; ApplySettings(); break;
                case "--masque": _settings.FilmMask = args[i + 1] == "1"; ApplySettings(); break;
                case "--expo": _settings.AutoExposure = args[i + 1] == "1"; ApplySettings(); break;
                case "--lueur": _settings.Glow = args[i + 1] == "1"; ApplySettings(); break;
                case "--dof": _settings.Dof = args[i + 1] == "1"; ApplySettings(); break;
                case "--flou": _settings.MotionBlur = args[i + 1] == "1"; ApplySettings(); break;
                case "--parallele": _settings.ParallelSolvers = args[i + 1] == "1"; break;
                case "--flotte": SpawnFleet(args[i + 1].ToInt(), _flotteShip); break;
                case "--flotte-navire": _flotteShip = args[i + 1].ToInt(); break;
                case "--dumprig":
                    foreach (var l in _ship.RigLog) GD.Print("gréement " + l);
                    break;
                case "--dumpprofile":
                    GD.Print("profil " + string.Join(" ", Array.ConvertAll(_prof.Fractions,
                        f => f.ToString("F5", System.Globalization.CultureInfo.InvariantCulture))));
                    break;
            }
    }

    Vector3? _fixEye, _fixLook;

    /* LA RÉGULARITÉ DES IMAGES, en nombres. Un à-coup « quasi imperceptible »
       ne se juge pas à l'œil sur une capture : il se lit dans la distribution
       des temps d'image, et dans ce que le ramasse-miettes a pris pendant ce
       temps. On relève le pas brut de Godot, sans le plafond de 50 ms. */
    int _slams, _sprayMax;
    long _allocPhys, _allocSails, _allocFrames, _allocRun0 = -1, _allocProc;
    int _ftLeft = -1;
    readonly List<double> _ft = new();
    TimeSpan _ftGc0;
    int _ftCol0 = -1, _ftCol1, _ftCol2;

    readonly System.Diagnostics.Stopwatch _ftWatch = new();
    double _ftCpuSum, _ftGpuSum, _ftRsCpuSum;
    Rid _ftVp;

    void FrameStats(double delta)
    {
        if (_ftLeft < 0) return;
        if (_ftCol0 < 0)
        {
            _ftCol0 = GC.CollectionCount(0); _ftCol1 = GC.CollectionCount(1); _ftCol2 = GC.CollectionCount(2);
            _ftVp = GetViewport().GetViewportRid();
            RenderingServer.ViewportSetMeasureRenderTime(_ftVp, true);
        }
        else
        {
            // l'image d'avant : notre _Process, puis ce que le rendu a pris
            _ftCpuSum += _ftWatch.Elapsed.TotalMilliseconds;
            _ftGpuSum += RenderingServer.ViewportGetMeasuredRenderTimeGpu(_ftVp);
            _ftRsCpuSum += RenderingServer.ViewportGetMeasuredRenderTimeCpu(_ftVp);
        }
        if (_ft.Count == 30) _allocRun0 = GC.GetTotalAllocatedBytes();
        _ft.Add(delta * 1000);
        if (--_ftLeft > 0) return;
        var s = _ft.Skip(30).OrderBy(x => x).ToArray();      // les premières images chargent encore
        double P(double q) => s[(int)Math.Min(s.Length - 1, Math.Floor(q * (s.Length - 1)))];
        double med = P(0.5);
        int hitches = s.Count(x => x > 1.5 * med);
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        GD.Print(string.Format(inv,
            "images {0} : moyenne {1:F2} ms, médiane {2:F2}, p95 {3:F2}, p99 {4:F2}, max {5:F2} ; "
          + "au-delà de 1,5×médiane : {6} ; ramasse-miettes : gen0 {7}, gen1 {8}, gen2 {9}, "
          + "pause totale {10:F1} ms, allouées {11:F1} Mo",
            s.Length, s.Average(), med, P(0.95), P(0.99), s[^1], hitches,
            GC.CollectionCount(0) - _ftCol0, GC.CollectionCount(1) - _ftCol1, GC.CollectionCount(2) - _ftCol2,
            (GC.GetTotalPauseDuration() - _ftGc0).TotalMilliseconds, GC.GetTotalAllocatedBytes() / 1048576.0));
        int m = _ft.Count - 1;
        GD.Print(string.Format(inv, "par image, en moyenne : notre _Process {0:F2} ms, rendu côté processeur {1:F2} ms, carte graphique {2:F2} ms",
            _ftCpuSum / m, _ftRsCpuSum / m, _ftGpuSum / m));
        GD.Print($"gerbes : {_slams}, paquets d'embrun en l'air au plus : {_sprayMax}");
        GD.Print(string.Format(inv, "alloué par image : solveur {0:F0} o, toile {1:F0} o, tout _Process {2:F0} o, tout le programme pendant la course {3:F0} o",
            (double)_allocPhys / _allocFrames, (double)_allocSails / _allocFrames, (double)_allocProc / _allocFrames,
            (GC.GetTotalAllocatedBytes() - _allocRun0) / (double)(_ft.Count - 30)));
        GetTree().Quit();
    }

    /* LE CONTRÔLE DU CHAMP D'ÉCUME, en nombres et sans image. Il relit la cible
       que le GPU vient de rendre et recalcule la déferlante au processeur, en
       doubles, aux mêmes points du monde : là où une crête déferle franchement,
       le champ doit valoir AU MOINS autant, puisqu'il ne fait que garder le
       maximum. Un axe retourné ou une ancre décalée d'un pas s'y compteraient
       par centaines. */
    int _foamCheckIn = -1;
    double _foamT;
    Vector2 _foamOrigin;
    Vector3 _foamShip;

    void FoamCheck()
    {
        var img = _foam.Texture.GetImage();
        var rng = new Random(7);
        int strong = 0, bad = 0, lit = 0, n = 4000;
        double sum = 0, maxSteep = 0;
        for (int s = 0; s < n; s++)
        {
            int px = rng.Next(FoamField.Res), py = rng.Next(FoamField.Res);
            float v = img.GetPixel(px, py).R;
            sum += v;
            if (v > 0.05f) lit++;
            double x = _foamOrigin.X + (px + 0.5) / FoamField.Res * FoamField.Size;
            double z = _foamOrigin.Y + (py + 0.5) / FoamField.Res * FoamField.Size;
            double steep = 0;
            for (int i = 0; i < Config.NWavesFoam; i++)
            {
                ref Wave w = ref _sea.Core.Waves[i];
                double f = w.K * (w.Dx * x + w.Dz * z) - w.Omega * _foamT + w.Phase;
                steep += w.Q * w.K * w.Amp * Math.Max(Math.Sin(f), 0);
            }
            maxSteep = Math.Max(maxSteep, steep);
            double e = Math.Clamp((steep - 0.66) / (1.05 - 0.66), 0, 1);
            double brk = e * e * (3 - 2 * e) * 0.9;
            if (brk > 0.3) { strong++; if (v < brk - 0.05) bad++; }
        }
        /* L'ANNEAU DE LA COQUE, recalculé au processeur avec le MÊME profil :
           partout où un texel tombe sur sa flottaison, le champ doit valoir au
           moins ce que l'anneau y dépose. Cela prouve qu'il est POSÉ sur elle,
           et c'est tout : essayé avec l'étrave volontairement inversée, sur la
           goélette, il ne voit rien — l'anneau fait 2,2 m de large en dedans et
           les deux contours ne s'écartent guère de plus d'un mètre. L'avant et
           l'arrière tiennent aux conventions, pas à ce contrôle : station 0 à
           la voûte (halfB, t = 0), colonne 0 en u = 0, u = 1 à l'étrave (+z). */
        var prof = _prof;
        var bb = _ship.Physics.Body;
        Vec3d fw3 = bb.Quat.Rotate(new Vec3d(0, 0, 1));
        double fl = Math.Sqrt(fw3.X * fw3.X + fw3.Z * fw3.Z);
        double fx = fw3.X / fl, fz = fw3.Z / fl, rx = fz, rz = -fx;
        double halfL = _ship.Spec.L * 0.5;
        double way = Math.Clamp(Math.Sqrt(bb.Vel.X * bb.Vel.X + bb.Vel.Z * bb.Vel.Z) / 3.0, 0, 1);
        int ring = 0, ringBad = 0;
        double ringSum = 0;
        for (int px = 0; px < FoamField.Res; px++)
            for (int py = 0; py < FoamField.Res; py++)
            {
                double x = _foamOrigin.X + (px + 0.5) / FoamField.Res * FoamField.Size - _foamShip.X;
                double z = _foamOrigin.Y + (py + 0.5) / FoamField.Res * FoamField.Size - _foamShip.Z;
                if (Math.Abs(x) > halfL + 5 || Math.Abs(z) > halfL + 5) continue;
                double along = x * fx + z * fz, athw = x * rx + z * rz;
                double ta = along / halfL;
                double u = Math.Clamp(ta * 0.5 + 0.5, 0, 1) * prof.Fractions.Length - 0.5;
                int i0 = Math.Clamp((int)Math.Floor(u), 0, prof.Fractions.Length - 1);
                int i1 = Math.Min(i0 + 1, prof.Fractions.Length - 1);
                double fr = Math.Clamp(u - Math.Floor(u), 0, 1);
                double hb = (prof.Fractions[i0] * (1 - fr) + prof.Fractions[i1] * fr) * prof.MaxHalfB;
                double mid = (prof.EndAft + prof.EndFwd) * 0.5, hbody = (prof.EndFwd - prof.EndAft) * 0.5;
                double dx = Math.Abs(athw) - hb, dz = Math.Abs(along - mid) - hbody;
                double gap = Math.Sqrt(Math.Pow(Math.Max(dx, 0), 2) + Math.Pow(Math.Max(dz, 0), 2))
                           + Math.Min(Math.Max(dx, dz), 0);
                if (Math.Abs(gap) > 0.25) continue;
                ring++;
                float v = img.GetPixel(px, py).R;
                ringSum += v;
                // la bande vaut ~1 sur la ligne ; un peu de marge pour l'étalement
                if (v < 0.7 * (0.06 + way)) ringBad++;
            }
        GD.Print($"anneau de coque : {ring} texels sur sa flottaison, moyenne {ringSum / Math.Max(1, ring):F3} "
               + $"(attendu ≥ {0.06 + way:F3} à l'erre {way:F2}), en défaut {ringBad}");

        GD.Print($"champ d'écume : {img.GetFormat()}, moyenne {sum / n:F3}, "
               + $"texels écumeux {100.0 * lit / n:F1} %, déferlantes fortes {strong}, "
               + $"en défaut {bad}, raideur max {maxSteep:F3}");
        for (int i = 0; i < Config.NWavesFoam; i++)
        {
            ref Wave w = ref _sea.Core.Waves[i];
            GD.Print($"  vague {i} : amp {w.Amp:F2} m, lambda {2 * Math.PI / w.K:F0} m, Q {w.Q:F3}, Q.k.amp {w.Q * w.K * w.Amp:F3}");
        }
        GetTree().Quit();
    }

    static Vector3 ParseVec(string s)
    {
        var p = s.Split(',');
        return new Vector3(p[0].ToFloat(), p[1].ToFloat(), p[2].ToFloat());
    }

    void TickCapture()
    {
        if (_captureIn < 0) return;
        if (--_captureIn > 0) return;
        var img = GetViewport().GetTexture().GetImage();
        if (img == null || img.GetWidth() < 8) { GD.PushError("capture vide"); GetTree().Quit(1); return; }
        Error err = img.SavePng(_capturePath);
        GD.Print(err == Error.Ok ? $"capture écrite : {_capturePath}" : $"capture ratée : {err}");
        GetTree().Quit(err == Error.Ok ? 0 : 1);
    }
}
