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
    // la barre tenue par la ligne de commande (--barre), pour un essai en virage
    double? _heldRudder;

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
        LoadNations();
        BuildScene();

        _sea = new OceanNode();
        AddChild(_sea);
        _sea.Core.SetSeaState(_force, _windDeg);

        _foam = new FoamField();
        AddChild(_foam);
        _sea.AttachFoam(_foam);

        _spray = new SprayNode();
        AddChild(_spray);
        _precip = new PrecipNode();
        AddChild(_precip);
        LoadClimate();
        _lightning = new LightningNode();
        AddChild(_lightning);
        _krakenNode = new KrakenNode();
        AddChild(_krakenNode);
        _cordage = new CordageNode();
        AddChild(_cordage);
        _splinters = new SplinterNode();
        AddChild(_splinters);
        _gunFx = new GunFxNode { Timber = _splinters };
        AddChild(_gunFx);
        _gunnery = new Gunnery(_gunRules);
        WireGuns();
        _flotsam = new FlotsamNode();
        AddChild(_flotsam);
        _bubbles = new BubbleNode();
        AddChild(_bubbles);
        _coins = new CoinNode();
        AddChild(_coins);
        WireWreck();
        _krakenNode.Build(_krakenRules.Glb == null ? null
            : System.IO.Path.GetFullPath(System.IO.Path.Combine(ProjectSettings.GlobalizePath("res://"), "..", _krakenRules.Glb)));
        _kraken = new Kraken(_krakenRules) { BodyR = _krakenNode.BodyR };
        WireKraken();

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
        _under = new UnderwaterEffect { Enabled = false };
        // l'eau d'abord : les flous viennent sur l'image qu'elle a déjà teinte
        _cam.Compositor = new Compositor { CompositorEffects = new Godot.Collections.Array<CompositorEffect> { _under, _anamorphic, _motionBlur } };
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
        // UN SEUL CANAL DE MESSAGES, comme dans la page : une phrase, qui s'efface
        _note = new Label
        {
            AnchorLeft = 0, AnchorRight = 1, AnchorTop = 1, AnchorBottom = 1, OffsetTop = -120, OffsetBottom = -80,
            HorizontalAlignment = HorizontalAlignment.Center, Visible = false
        };
        _note.AddThemeFontSizeOverride("font_size", 22);
        _note.AddThemeColorOverride("font_color", new Color(0.98f, 0.95f, 0.86f));
        _note.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.9f));
        _note.AddThemeConstantOverride("outline_size", 6);
        layer.AddChild(_note);
        BuildSunPanel(layer);
        _settings = Settings.Load();
        BuildMenu(layer);
        BuildSeaPanel(layer);
        BuildSpyglass();
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

    void SpawnFleet(int n, int specIndex, bool arm = true)
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
            var col = s.MakeCollider(prof);
            _spray.Pool.Colliders.Add(col);
            _hulls[s] = (prof, col, y);
            _fleet.Add(s.Physics);
            s.Physics.OnSlam = QueueSlam;
            _others.Add(s);
            if (arm) Arm(s);
            Colours(s);
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
            s.Sea = _sea.Core;
            s.StreamFlags(_t);
            s.SwingLanterns(frame);
        }
    }

    // ------------------------------------------------------------------
    //  LES RÉGLAGES — reglages.ini et le menu d'options (Échap)
    // ------------------------------------------------------------------

    Settings _settings = null!;
    CameraAttributesPractical _camAttr = null!;
    MotionBlurEffect _motionBlur = null!;
    UnderwaterEffect _under = null!;
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
            sm.SetShaderParameter("u_cap_gain", s.SeaCapGain);
            sm.SetShaderParameter("u_foam_gain", s.SeaFoamGain);
            sm.SetShaderParameter("u_jac_foam", s.SeaJacobian);
            sm.SetShaderParameter("u_streak_gain", s.SeaStreaks);
            sm.SetShaderParameter("u_kelvin_gain", s.SeaKelvin);
            _under.Shafts = s.SeaShafts;
            _under.Density = s.SeaDensity;
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
        RefitFleet();
        HoistNation();
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
        // le temps AVANT le solveur : la coque et le shader liront la même mer
        WeatherTick(frame, _t - frame);

        // --- le solveur, en sous-pas ---
        int sub = Math.Max(MinSub, (int)Math.Ceiling(frame / MaxSubDt));
        double dt = frame / sub;
        double t = _t - frame;
        // ce que chaque étage alloue, pour --frametimes : le solveur doit rester à zéro
        long a0 = GC.GetAllocatedBytesForCurrentThread();
        /* UN MÂT QUI S'EN VA EMPORTE SA TOILE, et AVANT qu'elle soit poussée : lu
           après, elle serait menée une image de plus par des voiles déjà dans l'eau. */
        Rigging(_ship, frame);
        foreach (var s in _others) { Rigging(s, frame); Steer(s, frame); }
        StepSolvers(sub, dt, t);
        // porter de la toile coûte de la toile, et au-delà, l'espar
        TearCanvas(_ship, frame, true); StrainMast(_ship, frame, true);
        foreach (var s in _others) { TearCanvas(s, frame, false); StrainMast(s, frame, false); }
        long a1 = GC.GetAllocatedBytesForCurrentThread();
        _ship.SyncTransform();

        /* LA TOILE SUIT LE SOLVEUR : l'écoute, le bord, la toile établie, le
           faseyement et la charge sont lus à chaque image, jamais retenus — la
           voile se gonfle parce qu'on la borde, avec la même pression qui pousse
           le navire, sans seconde règle à tenir d'accord. */
        var ph = _ship.Physics;
        _ship.SetTrim(_ship.Ctrl.Sheet, ph.Tack, ph.SetFrac, ph.Luffing, _t, ph.SailLoad);
        _ship.Sea = _sea.Core;
        _ship.StreamFlags(_t);
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
            _sea.ShiftWakes((float)dx, (float)dz);
            _foam.Rebase((float)-dx, (float)-dz);
            _spray.Pool.Rebase(-dx, -dz);
            _kraken.Rebase(-dx, -dz);
            _lightning.Rebase(-dx, -dz);
            _cordage.Rebase(-dx, -dz);
            _splinters.Rebase(-dx, -dz);
            _gunnery.Rebase(-dx, -dz);
            foreach (var pr in _pirates.Values) pr.Rebase(-dx, -dz);
            _gunFx.Rebase(-dx, -dz);
            _wreckAir.Rebase(-dx, -dz);
            _bubbles.Rebase(dx, dz);
            _coins.Rebase(dx, dz);
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
        AimSpyglass(frame);                     // après la vue : sa position, la visée de la lunette
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
        double dayBefore = _sky.Core.DayTime;
        _sky.UpdateWeather(frame, _sea.Core.SeaState);
        FallTick(frame, dayBefore);
        StormTick(frame);
        GunTick(frame);
        WreckTick(frame);
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
        // après les feux : la scène lit la nuit par leur règle
        GhostTick(frame);
        // et la mer les voit : leur reflet et leur lumière sur l'eau
        int lamps = _ship.FillLamps(_sea.Lamps, _sea.LampRange, 0);
        foreach (var s in _others) lamps += s.FillLamps(_sea.Lamps, _sea.LampRange, lamps);
        _sea.PushLamps(lamps);
        /* L'ŒIL SOUS LA SURFACE : la mer le dit à son shader, qui dessine alors sa
           face de dessous — la fenêtre de Snell. */
        var ce = _cam.GlobalPosition;
        double seaY = _sea.Core.Sample(ce.X, ce.Z, _t);
        /* À CHEVAL SUR LA SURFACE : quand l'œil est à moins d'un demi-mètre de
           l'eau, ce n'est plus dessus ou dessous mais les deux à la fois, et
           c'est le PIXEL qui décide — la passe sous-marine regarde alors le sens
           du rayon. */
        float straddle = (float)Math.Max(0, 1 - Math.Abs(seaY - ce.Y) / 0.5);
        bool under = seaY > ce.Y || straddle > 0.01f;
        _sea.Material?.SetShaderParameter("u_submerged", under ? 1f : 0f);
        // et la passe sous-marine, qui n'existe que là : l'eau qui éteint, les rais qui descendent
        _under.Enabled = under;
        if (under)
        {
            _under.SeaY = (float)seaY;
            var sd = _sky.Core.SunDir;
            _under.Sun = new Vector3((float)sd.X, (float)sd.Y, (float)sd.Z);
            _under.SunLight = (float)Math.Max(0.05, _sky.Core.SunIntensity);
            /* La couleur de l'eau profonde est celle de la MER (u_deep), pas celle
               du ciel : prise au zénith, tout sortait délavé. Elle s'éteint avec la
               lumière qui est dans l'eau, comme la mer vue de dessus. */
            double wl = Math.Max(0.05, _sky.Core.WaterLight);
            // un tiers de l'eau vue de dessus : sous la surface on regarde DANS elle,
            // et ce qu'on y voit est ce qui a traversé, non ce qu'elle renvoie
            _under.Water = new Color((float)(0.0015 * wl), (float)(0.0120 * wl), (float)(0.0240 * wl));
            _under.Time = (float)_t;
            _under.Straddle = straddle;
        }
        DropletTick(frame, under);
        _sky.PushTo(_sea.Material);
        _sky.SetCloud(_sea.Material, _cloud, _t);
        foreach (var m in _ship.Hazed) { _sky.PushTo(m); _sky.SetCloud(m, _cloud, _t); }
        if (_krakenNode.Visible) foreach (var m in _krakenNode.Hazed) { _sky.PushTo(m); _sky.SetCloud(m, _cloud, _t); }
        foreach (var m in _cordage.Hazed) _sky.PushTo(m);
        foreach (var m in _splinters.Hazed) _sky.PushTo(m);
        foreach (var m in _gunFx.Hazed) _sky.PushTo(m);
        foreach (var m in _flotsam.Hazed) _sky.PushTo(m);
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
    /// <summary>De combien l'œil de la vue mi-eau est relevé au-dessus de la houle locale.</summary>
    float _splitLift = 0.05f;
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
        3 => "Mi-eau",
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
        else if (_camMode == 2) { _camMode = 3; SetLens(OutsideFov, OutsideNear); }
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

        /* MI-EAU : l'œil POSÉ SUR LA SURFACE, moitié dedans moitié dehors — la
           vue en coupe des photographes sous-marins. La hauteur n'est pas
           choisie, elle est LUE sur la houle à l'endroit de l'œil, si bien que la
           ligne de partage reste au milieu de l'image quand la mer respire ; et
           l'on regarde à l'horizontale, sans quoi la ligne file hors du cadre. */
        if (_camMode == 3)
        {
            float r3 = Math.Max(12f, _dist * 0.35f);
            var at = new Vector3(_ship.Position.X + Mathf.Sin(_orbit) * r3, 0, _ship.Position.Z + Mathf.Cos(_orbit) * r3);
            /* UN QUART DE MÈTRE AU-DESSUS de la houle locale, et non dessus
               exactement : à fleur d'eau, la surface vue de l'œil même s'étale en une
               bande sombre au milieu de l'image et mange les deux moitiés. Relevée
               d'un rien, elle redevient une LIGNE. */
            at.Y = (float)_sea.Core.Sample(at.X, at.Z, _t) + _splitLift;
            _cam.Position = at;
            var look = _ship.Position;
            _cam.LookAt(new Vector3(look.X, at.Y, look.Z), Vector3.Up);
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
        /* SOUS LA QUILLE, SI ON LE DEMANDE. L'œil était tenu à deux mètres au
           moins au-dessus d'elle : on ne pouvait donc jamais passer dessous ni
           regarder le ciel à travers la surface. Le plancher ne vaut plus que
           lorsqu'on la regarde d'en haut ; l'inclinaison négative descend sous la
           quille, et c'est là qu'est la fenêtre de Snell. */
        float eyeY = _pitch >= 0 ? Mathf.Max(2f, h) : h;
        var eye = target + new Vector3(Mathf.Sin(_orbit) * r, eyeY, Mathf.Cos(_orbit) * r);
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
            : _heldRudder ?? c.Rudder * (1 - Math.Min(1, dt * 2.5));

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
        int bf = Mathf.Clamp((int)Math.Round(_sea.Core.SeaState), 0, 9);

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
            $"vent       {_windNowDeg,6:F0}°      force     {_sea.Core.SeaState:F1} · {Config.Beaufort[bf].Name}{(_seaMaster != null ? " · " + _seaMaster : "")}\n" +
            GunLine() +
            $"air        {_climate.Word()}{(_fall.Amount > 0.004 ? (_fall.Snow ? " · il neige" : " · il pleut") : "")}   {_calendar.Date:dd/MM/yyyy}{(_ship.SnowCover > 0.01 ? $"   neige sur le pont {_ship.SnowCover * 100:F0} %" : "")}\n" +
            (_inSquall ? $"dépression {_squall.Dist / 1852,6:F1} mille(s) du centre · au cœur force {_squall.Storm.Peak:F1} · ici {_squall.Force:F1}\n" : "") +
            $"\n" +
            $"W S machine   B élan   A D barre   Q E écoutes   V voiles\n" +
            $"↑↓ force   ←→ vent   T météo {(_weather.On ? "auto" : "à la main")}   J gros temps   K kraken   R radoub   G feu (tenu : bordée, ⇧ : autre bord)   Tab bord   Y soute   U pirate   P fantômes   L lunette   ⇧M mer   PgUp/PgDn creux   N navire   F suivre   C vues ({CamName()})   X replanter   H masquer   Échap options\n" +
            $"O occlusion {(_sky.Env.SsaoEnabled ? "oui" : "non")}   lumière indirecte au menu";
    }

    public override void _UnhandledInput(InputEvent e)
    {
        /* G : UN APPUI, une pièce ; TENU, la bordée entière — la répétition du clavier
           le dit, et un loquet empêche un long appui de lâcher bordée sur bordée.
           ⇧ : l'autre bord, une fois. */
        if (e is InputEventKey gk && (gk.PhysicalKeycode != Key.None ? gk.PhysicalKeycode : gk.Keycode) == Key.G)
        {
            if (!gk.Pressed) _salvo = false;
            else if (!gk.Echo) Fire(gk.ShiftPressed, false);
            else if (!_salvo) { _salvo = true; Fire(gk.ShiftPressed, true); }
            GetViewport().SetInputAsHandled();
            return;
        }
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
            /* ⇧M, PAR SA LETTRE et non par son emplacement : sur un AZERTY le M est
               là où un QWERTY a son point-virgule, et l'emplacement « M » est la
               virgule — le panneau ne s'ouvrait pas. Les autres commandes tombent
               aux mêmes places sur les deux claviers. */
            if (k.ShiftPressed && k.Keycode == Key.M) { ToggleSeaPanel(); GetViewport().SetInputAsHandled(); return; }
            switch (key)
            {
                // une main sur la console reprend la main à la météo, comme le curseur de la page
                case Key.Up: _weather.On = false; _force = Math.Min(9.9, _force + 0.5); Restate(); break;
                case Key.Down: _weather.On = false; _force = Math.Max(0, _force - 0.5); Restate(); break;
                case Key.Left: _weather.On = false; _windDeg = (_windDeg - 15 + 360) % 360; Restate(); break;
                case Key.Right: _weather.On = false; _windDeg = (_windDeg + 15) % 360; Restate(); break;
                case Key.T: SetAutoWeather(!_weather.On); break;
                case Key.J: GoToStorm(0); break;
                // le radoub : mâts replantés, toile renverguée — pour recommencer un essai
                case Key.R: _ship.RestoreMasts(); Say("Radoub : la mâture est remise en état."); break;
                // le kraken, tout de suite contre elle — pour le voir sans attendre une minute au cœur d'un grain
                case Key.K:
                    // il ne vit qu'au cœur des dépressions : hors d'elles, il replongerait à l'image suivante
                    if (!_inSquall || _squall.Inten < _krakenRules.MinInten) GoToStorm(0.2);
                    _kraken.Summon(true, PreyOf(_ship));
                    break;
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
                case Key.Tab: CycleGunSide(); break;
                case Key.Y: BlowUp(_ship); break;
                case Key.U: SpawnPirate(900); break;
                case Key.P: GoToGhosts(true); break;
                case Key.L: ToggleSpyglass(); break;
                /* L'ÉLAN : l'équivalent de `Naval.app.controls.state.throttle = 45`
                   dans la console d'origine. Le solveur ne borne pas la machine,
                   donc c'est quarante-cinq fois la poussée — de quoi voir une coque
                   lancée dans la houle sans attendre qu'elle prenne son erre. Un
                   second appui coupe, sans quoi elle filerait sans fin ; W et S
                   la ramènent aussi dans leur plage en la touchant. */
                case Key.B: _ship.Ctrl.Throttle = _ship.Ctrl.Throttle > 1 ? 0 : 45; break;
                /* L'IMAGE SEULE : les commandes et le panneau du soleil s'effacent,
                   pour regarder ou filmer. Le menu reste sur Échap, et le masque de
                   cinéma, qui fait partie de l'image, reste en place. */
                case Key.H: _info.Visible = _sunPanel.Visible = !_info.Visible; break;
                // le menu d'options ; « Quitter » y est désormais
                case Key.Escape:
                    _chkOcclusion.SetPressedNoSignal(_settings.Occlusion);
                    _chkIndirect.SetPressedNoSignal(_settings.IndirectLight);
                    _menu.Visible = !_menu.Visible;
                    break;
            }
        }
        // la lunette à l'œil prend le glisser et la molette
        if (GlassMouse(e)) return;
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
                // sous zéro, elle plonge : la mer se referme au-dessus et l'on voit le ciel par la fenêtre
                _pitch = Mathf.Clamp(_pitch + mm.Relative.Y * 0.004f, -1.2f, 1.3f);
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
        _lagForce = _force; _lagDir = _windDeg;
        UpdateInfo();
    }

    // ------------------------------------------------------------------
    //  LE TEMPS QU'IL FAIT — la météo qui se conduit seule, et les
    //  dépressions, qui ont un lieu (noyau : Weather, Storms)
    // ------------------------------------------------------------------

    readonly Weather _weather = new();
    readonly Storms _storms = new();
    // la mer qui court après le vent, au plus à Weather.SeaRate
    double _lagForce = 4, _lagDir = 210, _windNowDeg = 210;
    bool _inSquall;
    Squall _squall;
    string? _seaMaster;

    // ------------------------------------------------------------------
    //  CE QUI TOMBE — le climat (température, averses), la pluie, la neige,
    //  et la neige qui tient sur les ponts
    // ------------------------------------------------------------------

    PrecipNode _precip = null!;
    Calendar _calendar = new();
    Climate _climate = new();
    (double Amount, bool Snow) _fall;

    /* LE CLIMAT DE LA PAGE, lu dans SON settings.json : le départ du calendrier et
       la section « climate ». Un fichier absent ou abîmé laisse les valeurs par
       défaut, qui sont celles de climate.js. */
    void LoadClimate()
    {
        string path = System.IO.Path.GetFullPath(System.IO.Path.Combine(ProjectSettings.GlobalizePath("res://"), "..", "settings.json"));
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(System.IO.File.ReadAllText(path));
            var root = doc.RootElement;
            if (root.TryGetProperty("calendar", out var c) && c.TryGetProperty("start", out var s))
                _calendar = new Calendar(s.GetString());
            if (root.TryGetProperty("ghosts", out var gh)) _ghosts.Rules = GhostRules.FromJson(gh);
            if (root.TryGetProperty("wreck", out var wr) && wr.TryGetProperty("bottleOneIn", out var bo))
                _bottleOneIn = bo.GetInt32();
            if (root.TryGetProperty("gunnery", out var gu)) _gunRules = GunnerySettings.FromJson(gu);
            if (root.TryGetProperty("storm", out var st))
            {
                if (st.TryGetProperty("lightning", out var li)) _lightRules = LightningSettings.FromJson(li);
                if (st.TryGetProperty("kraken", out var kr)) _krakenRules = KrakenSettings.FromJson(kr);
            }
            if (root.TryGetProperty("climate", out var k))
                _climate = new Climate(ClimateSettings.FromJson(k));
        }
        catch (Exception ex) { GD.PushWarning($"settings.json illisible ({ex.Message}) : climat par défaut"); }
    }

    /// <summary>
    /// Une image de ce qui tombe. Le climat avance en heures de JEU — l'horloge du
    /// jour, pas la montre : presser le temps amène autant d'averses par jour —, et
    /// minuit passé tourne la page du calendrier. La pluie d'un coup de vent (à
    /// partir de force 5,5) et l'averse se composent ; au froid, c'est de la neige.
    /// </summary>
    void FallTick(double dt, double dayBefore)
    {
        double dayAfter = _sky.Core.DayTime;
        if (dayAfter < dayBefore) _calendar.NextDay();          // minuit passé
        double hours = (dayAfter - dayBefore + 24) % 24;
        _climate.Update(hours, _calendar, dayAfter, _sky.Core.Storm);

        double wet = Math.Clamp((_sea.Core.SeaState - 5.5) / 2.8, 0, 1);
        _fall = _climate.Precipitation(wet);

        // ce qui tombe réfléchit : la clarté de l'horizon, qui porte l'heure
        var h = _sky.Core.Horizon;
        double light = Math.Min(1, (h.R + h.G + h.B) / 2.3);
        var w = _sea.Core.WindVec;
        _precip.Step(_cam.GlobalPosition, _t, new Vector3((float)w.X, (float)w.Y, (float)w.Z),
            _fall.Snow ? 0 : _fall.Amount, _fall.Snow ? _fall.Amount : 0, light,
            GetViewport().GetVisibleRect().Size.Y);

        /* LA NEIGE TIENT SUR LES PONTS : un manteau qui s'épaissit en dix minutes
           sous une forte chute, plafonné à 0,85 — un manteau, pas une congère —,
           et qui fond d'autant plus vite qu'il fait doux. À chaque coque la
           sienne : un navire sorti de la neige la garde jusqu'à ce qu'elle fonde. */
        double melt = Math.Max(0, _climate.Temp - _climate.K.SnowBelow);
        Settle(_ship);
        foreach (var s in _others) Settle(s);
        void Settle(ShipNode s)
        {
            double c = s.SnowCover;
            c = _fall.Snow ? Math.Min(0.85, c + dt * _fall.Amount / 600)
                           : Math.Max(0, c - dt * (0.5 + melt) / 1800);
            s.SetSnowCover(c);
        }
    }

    // ------------------------------------------------------------------
    //  CE QUI VIENT AVEC LA TEMPÊTE — la foudre qui tombe sur une tête de
    //  mât, et le kraken qui vit au cœur des dépressions
    // ------------------------------------------------------------------

    LightningNode _lightning = null!;
    CordageNode _cordage = null!;
    SplinterNode _splinters = null!;
    readonly List<ShipNode> _allShips = new();
    KrakenNode _krakenNode = null!;
    Kraken _kraken = null!;
    LightningSettings _lightRules = new();
    GunnerySettings _gunRules = new();
    KrakenSettings _krakenRules = new();
    readonly Random _stormRng = new();
    // chaque coque dans une dépression, et combien elle y est enfoncée — relue à chaque image
    readonly List<(KrakenPrey Prey, double Inten)> _orage = new(), _orageKraken = new();
    readonly Dictionary<ShipNode, KrakenPrey> _preys = new();
    readonly Dictionary<KrakenPrey, ShipNode> _preyShip = new();

    // gardé : une lambda neuve à chaque image, ce sont des octets pour le ramasse-miettes
    Func<KrakenPrey, bool>? _alive;
    bool PreyAlive(KrakenPrey p) =>
        _preyShip.TryGetValue(p, out var s) && (s == _ship || _others.Contains(s)) && !p.Physics.Foundered;

    Label _note = null!;
    double _noteLeft;

    /// <summary>Dire une phrase, et la laisser s'effacer — dire() de la page.</summary>
    void Say(string text)
    {
        _note.Text = text;
        _note.Visible = true;
        _noteLeft = 2.6;
        GD.Print(text);
    }

    KrakenPrey PreyOf(ShipNode s)
    {
        if (_preys.TryGetValue(s, out var p)) return p;
        p = new KrakenPrey { Physics = s.Physics, Tops = s.MastTops, DeckNear = s.DeckNear, Name = s.Spec.Name };
        _preys[s] = p;
        _preyShip[p] = s;
        return p;
    }

    /* CE QUE LE KRAKEN FAIT, DIT À QUI COMMANDE. Quand c'est le vôtre, la phrase
       est la vôtre ; quand c'est un autre, elle le nomme — et seulement s'il est
       assez près pour qu'on le voie, à moins de trois kilomètres. */
    static readonly Dictionary<string, (string Mine, string Other)> KrakenSays = new()
    {
        ["appear"] = ("Quelque chose d’énorme remue sous la houle…", "Quelque chose d’énorme remue sous la houle, près du {n}…"),
        ["approach"] = ("Le kraken se rapproche !", "Le kraken se rapproche du {n} !"),
        ["grip"] = ("Le kraken enlace le navire !", "Le kraken enlace le {n} !"),
        ["beaten"] = ("Touché ! Le kraken lâche prise et sombre dans les profondeurs.", "Le kraken lâche le {n} et sombre dans les profondeurs."),
        ["flee"] = ("Le kraken renonce : vous l’avez distancé.", "Le {n} a distancé le kraken."),
        ["storm"] = ("Le kraken regagne les profondeurs.", "Le kraken regagne les profondeurs.")
    };

    void KrakenSay((string Mine, string Other) t, KrakenPrey prey)
    {
        if (_preyShip.TryGetValue(prey, out var s) && s == _ship) { Say(t.Mine); return; }
        if ((prey.Physics.Body.Pos - _ship.Physics.Body.Pos).Length <= 3000) Say(t.Other.Replace("{n}", prey.Name));
    }

    void WireKraken()
    {
        // les gerbes de ses bras qui crèvent la surface, et de ce qu'ils arrachent
        _kraken.Splash = (at, water, speed, jet) => _spray.Pool.Burst(at, water, speed, jet);
        _kraken.Event = (kind, prey) =>
        {
            if (prey != null && KrakenSays.TryGetValue(kind, out var t)) KrakenSay(t, prey);
        };
        /* LE DÉGÂT N'EST PAS ENCORE PORTÉ : un mât qui se tord ou tombe, une voile
           arrachée de ses ralingues viendront avec la mâture qui tombe et la toile
           qui se déchire (les canons en ont besoin aussi). Dit à la console, et
           pas au joueur : annoncer un dégât qui n'a pas lieu serait mentir. */
        _kraken.OnMast = (prey, fall) =>
        {
            if (!_preyShip.TryGetValue(prey, out var s)) return;
            KrakenSay(s.WoundMast(fall) ? ("Le kraken arrache un mât !", "Le kraken arrache un mât du {n} !")
                                        : ("Le kraken tord un mât dans son étreinte !", "Le kraken tord un mât du {n} !"), prey);
        };
        _kraken.OnSail = prey =>
        {
            if (_preyShip.TryGetValue(prey, out var s) && s.SplitSail(false) >= 0)
                KrakenSay(("Un bras du kraken déchire une voile !", "Le kraken déchire une voile du {n} !"), prey);
        };
    }

    /// <summary>
    /// Une image de ce qui vient avec la tempête : qui est dans une dépression et
    /// de combien, la foudre tirée navire par navire, le kraken.
    /// </summary>
    void StormTick(double dt)
    {
        if (_noteLeft > 0 && (_noteLeft -= dt) <= 0) _note.Visible = false;

        var o = _sea.Core.Origin;
        _orage.Clear();
        Gather(_ship);
        foreach (var s in _others) Gather(s);
        void Gather(ShipNode s)
        {
            var b = s.Physics.Body;
            if (_storms.At(o.X + b.Pos.X, o.Z + b.Pos.Z, _t, out var q)) _orage.Add((PreyOf(s), q.Inten));
        }

        // le kraken ne prend pas les spectres ; la foudre, si
        _orageKraken.Clear();
        foreach (var it in _orage) if (!_preyShip[it.Prey].IsGhost) _orageKraken.Add(it);
        _kraken.Update(dt, _orageKraken, _sea.Core, _t, _alive ??= PreyAlive);
        _krakenNode.Sync(_kraken);

        foreach (var (prey, inten) in _orage)
            if (_stormRng.NextDouble() < _lightRules.StrikeChance(inten, dt)) Strike(_preyShip[prey]);
        _lightning.Step(dt);

        // les bouts rompus de toute la flotte, et les éclats en l'air
        _allShips.Clear();
        _allShips.Add(_ship);
        _allShips.AddRange(_others);
        var h = _sky.Core.Horizon;
        _cordage.Step(dt, _allShips, _sea.Core, _t, _cam, _sea.Core.WindVec, new Vec3d(h.R, h.G, h.B));
        _splinters.Step(dt, _sea.Core, _t, _spray.Pool);
    }

    /* LA FOUDRE TOMBE SUR LA PLUS HAUTE TÊTE DE MÂT encore debout. L'éclair du
       ciel ne s'allume que si c'est assez près pour éclairer notre pont. Ce
       qu'elle coûte : une voile, une blessure de mât — les mêmes que les boulets —,
       et parfois le mât d'un coup. */
    void Strike(ShipNode s)
    {
        if (s.Physics.Foundered || !s.HighestMasthead(out var w, out int fall)) return;
        _lightning.Strike(w);
        if (w.DistanceTo(_cam.GlobalPosition) < 3000) _sky.Strike();
        // la pomme du mât vole en éclats, vers le bas
        _splinters.Splinters(w, Vector3.Down, 0.6);
        string what = "La foudre frappe la mâture !";
        if (fall >= 0)
        {
            if (_stormRng.NextDouble() < _lightRules.DismastChance)
            {
                if (s.DropMast(fall)) what = "La foudre fend le mât, qui s’abat !";
            }
            else if (_stormRng.NextDouble() < _lightRules.WoundChance)
                what = s.WoundMast(fall) ? "La foudre achève le mât, qui s’abat !" : "La foudre frappe le mât et le blesse !";
        }
        if (_stormRng.NextDouble() < _lightRules.SplitChance && s.SplitSail(false) >= 0 && fall < 0)
            what = "La foudre met une voile en lambeaux !";
        if (s == _ship) Say(what);
    }

    // ------------------------------------------------------------------
    //  PORTER DE LA TOILE COÛTE DE LA TOILE — tearCanvas et strainMast
    // ------------------------------------------------------------------

    void Rigging(ShipNode s, double dt)
    {
        s.StepRigging(dt);
        s.Physics.Standing = s.Standing();
        s.Physics.Whole = s.Whole();
    }

    /* Le risque est un TAUX, pas un seuil qui claque : une couture lâche d'autant
       plus vite qu'on insiste, comme le carré de l'excès, pour que force 7
       pardonne et que force 9 ne pardonne pas. LA SURFACE ÉTABLIE MULTIPLIE LE
       DANGER, ELLE NE BAISSE PAS LE SEUIL : la toile cède à une PRESSION, donc un
       ris ne soulage pas d'un newton ce qui reste dehors ; il achète moins de
       tissu exposé. Ferler tout à fait est la seule chose qui mette à l'abri.
       Relevé dans la page sur le galion à pleine voilure : force 7 pardonne une
       demi-heure, force 9 coûte une voile en une vingtaine de secondes. */
    const double TearRate = 0.045;            // par seconde, à deux fois le seuil
    /* LE MÂT A SA PROPRE CAUSE, bien plus rare, et une barre plus haute que la
       toile — 2,6 fois ce qu'elle tient —, sans quoi il partait AVANT la première
       déchirure, et un fusible qui saute après le circuit ne sert à rien. C'est
       la rafale qui casse le mât, pas le coup de vent. */
    const double MastRate = 1.0, MastLoad = 2.6;

    void TearCanvas(ShipNode s, double dt, bool mine)
    {
        var ph = s.Physics;
        if (ph.Foundered || ph.SetFrac < 0.02) return;
        double excess = ph.SailLoad / Config.CanvasStrength - 1;
        if (excess <= 0) return;
        if (_stormRng.NextDouble() > TearRate * excess * excess * ph.SetFrac * dt) return;
        // au DOUBLE de ce que la toile tient, la ferrure part avec le tissu et le mât prend une blessure
        bool hard = ph.SailLoad > 2 * Config.CanvasStrength;
        int r = s.SplitSail(hard);
        if (r == -1 || !mine) return;         // une conserve ne commente pas ses avaries
        Say(r <= -2 ? "Le mât est parti par-dessus bord !"
          : hard ? "Une voile éclate — le gréement souffre !"
                 : "Une voile se déchire dans la rafale !");
    }

    void StrainMast(ShipNode s, double dt, bool mine)
    {
        var ph = s.Physics;
        if (ph.Foundered || ph.SetFrac < 0.02) return;
        if (ph.SailLoad <= MastLoad * Config.CanvasStrength) return;   // en deçà, la toile suffit à payer
        double excess = ph.SailLoad / Config.CanvasStrength - 1;
        var (i, part) = s.HeaviestMast();
        if (i < 0 || part <= 0) return;
        if (_stormRng.NextDouble() > TearRate * MastRate * excess * excess * ph.SetFrac * part * dt) return;
        if (!s.DropMast(i)) return;
        if (mine) Say("Le mât est parti par-dessus bord !");
    }

    // ------------------------------------------------------------------
    //  LES GROSSES PIÈCES — la bordée, les coups reçus, la riposte, la soute
    // ------------------------------------------------------------------

    Gunnery _gunnery = null!;
    GunFxNode _gunFx = null!;
    /// <summary>Une bouteille sur combien de naufrages (settings.json → wreck).</summary>
    int _bottleOneIn = 6;
    readonly Dictionary<ShipNode, ShotTarget> _targets = new();
    // les coques qu'on a provoquées : elles se retournent contre qui les a touchées
    readonly Dictionary<ShipNode, (ShipNode Foe, double Rearm)> _hostile = new();
    readonly Random _gunRng = new();
    /* LE BORD EN BATTERIE, un ÉTAT qu'on voit et non une touche dont il faut se
       souvenir : +1 tribord, −1 bâbord, +2 poupe, −2 proue — « l'autre » est le négatif. */
    int _gunSide = 1;
    bool _salvo;
    static readonly Dictionary<int, string> GunNames = new() { [1] = "tribord", [-1] = "bâbord", [2] = "poupe", [-2] = "proue" };

    ShotTarget TargetOf(ShipNode s)
    {
        if (_targets.TryGetValue(s, out var t)) return t;
        t = new ShotTarget { Physics = s.Physics, Battery = s.Battery, Shell = s.HullShell(), Masts = s.MastBoxes, Tag = s };
        _targets[s] = t;
        // armée à sa première apparition : quarante charges par pièce, comme la page
        s.Physics.PowderMax = s.Battery.Guns.Count * 40;
        s.Physics.Powder = s.Physics.PowderMax;
        return t;
    }

    void WireGuns()
    {
        _gunnery.OnFire = (at, dir, k, floor, ph) =>
            _gunFx.Gun(new Vector3((float)at.X, (float)at.Y, (float)at.Z), new Vector3((float)dir.X, (float)dir.Y, (float)dir.Z), k, floor);
        /* Un boulet fait un trou ÉTROIT dans l'eau très vite : une colonne haute et
           mince, pas un dôme — le volume est borné par la réserve d'embrun, pour
           qu'une bordée de six y tienne sans que les dernières volent les premières. */
        _gunnery.OnSplash = (at, water, speed, jet) => _spray.Pool.Burst(at, water, speed, jet);
        _gunnery.OnCreature = (a, b, shot) =>
        {
            if (!_kraken.HitShot(a, b, out double u, out var arm)) return false;
            var hit = a + (b - a) * u;
            _spray.Pool.Burst(hit, 10, 7, 1.4);
            _kraken.Wound(arm, shot.K);
            return true;
        };
        _gunnery.OnStrike = Struck;
        // un spectre ne se touche que par un spectre, ou par celui qui s'est retourné contre vous
        _gunnery.CanHit = (from, t) => _ghosts.CanTouch(from, t.Physics);
    }

    /* CE QU'UN BOULET LUI COÛTE, par une mécanique qui existait déjà : un trou dans
       son flanc est la même voie d'eau que les autres — Torricelli, la carène
       liquide et l'envahissement prennent la suite sans une ligne pour l'artillerie ;
       un mât touché est la même blessure que la foudre. Rien du fait d'être canonné
       n'est un cas à part. */
    void Struck(ShotTarget t, string kind, int index, double frac, double speed, double k, ShipPhysics from, Vec3d world, Vec3d dir)
    {
        if (t.Tag is not ShipNode s) return;
        // qui a tiré : c'est ce qui permet à un navire de savoir contre qui se retourner
        ShipNode? shooter = null;
        foreach (var (node, tt) in _targets) if (tt.Physics == from) { shooter = node; break; }
        // les spectres ne provoquent pas les vivants et n'en sont pas provoqués
        if (shooter != null && s != _ship && shooter != s && !_pirates.ContainsKey(s) && !_hostile.ContainsKey(s)
            && !s.IsGhost && !shooter.IsGhost) _hostile[s] = (shooter, 0);

        var w = new Vector3((float)world.X, (float)world.Y, (float)world.Z);
        // le bois d'abord, quoi qu'on ait touché : le même événement vu du dehors
        _splinters.Splinters(w, new Vector3((float)dir.X, (float)dir.Y, (float)dir.Z), k);
        if (kind == "mast")
        {
            // un bas mât faisait un pied de chêne : il en faut plusieurs, c'est la récompense du feu soutenu
            s.WoundMast(index);
            return;
        }
        /* Un dixième de mètre carré pour une pièce de plein calibre, et comme le CARRÉ
           du calibre — un trou est une surface. Petit devant les pompes : un navire
           n'est pas perdu sur un coup heureux, il l'est d'être percé encore et encore. */
        s.Physics.MakeBreach(index, 0.10 * k * k, Math.Clamp(frac, 0, 1));
        // la marque dans le bordé, qui s'aggrave si l'on retape au même endroit
        s.Scar(w, k);
        // et les pièces qui étaient derrière le bordé
        var b = s.Physics.Body;
        var local = b.Quat.Inverted().Rotate(world - b.Pos);
        var down = s.Battery.Wound(local, k, s.Spec.L);
        if (down.Count > 0 && s == _ship)
        {
            int side = down[0].Side;
            var (ok, all) = s.Battery.Count(side);
            string where = Math.Abs(side) == 2 ? "en " + GunNames[side] : "à " + GunNames[side];
            Say((down.Count > 1 ? down.Count + " pièces démontées " : "Pièce démontée ") + where
                + (ok > 0 ? $" · {ok}/{all} en état" : " · plus une pièce en état"));
        }
    }

    /* TIRER — un appui, une pièce : la batterie parcourue de l'avant à l'arrière ;
       la touche TENUE, la bordée entière, une seule fois par appui. La poudre se
       consomme sur ce que les pièces ont RÉELLEMENT tiré. */
    void Fire(bool other, bool held)
    {
        var ph = _ship.Physics;
        var bat = _ship.Battery;
        TargetOf(_ship);
        if (bat.Guns.Count == 0) { Say("Ce navire ne porte pas de batterie"); return; }
        if (ph.Powder <= 0) { Say("Plus une charge en soute"); return; }
        int side = other ? -_gunSide : _gunSide;
        if (!bat.Has(side)) { Say("Aucune pièce en " + GunNames[side]); return; }
        if (bat.Count(side).Ok == 0) { Say("Plus une pièce en état · " + GunNames[side]); return; }
        int fired = held ? _gunnery.Broadside(bat, side, ph, ph.Powder) : _gunnery.FireOne(bat, side, ph);
        if (fired == 0)
        {
            var L = _gunnery.Loaded(bat, side);
            Say("Pièces en rechargement · " + GunNames[side]
                + (double.IsFinite(L.Next) ? $" — la première dans {Math.Ceiling(L.Next)} s" : ""));
            return;
        }
        ph.Powder = Math.Max(0, ph.Powder - fired);
    }

    /* LA COLONNE ANNONCE SON BORD, et ce qu'il en reste dès qu'il en manque :
       « tribord 4/6 · 3 prêtes sur 6 ». Un compte plein ne se lit pas. */
    string GunLine()
    {
        var bat = _ship.Battery;
        if (bat.Guns.Count == 0) return "";
        var (ok, all) = bat.Count(_gunSide);
        var L = _gunnery.Loaded(bat, _gunSide);
        return $"pièces     {GunNames[_gunSide]}" + (all > 0 && ok < all ? $" {ok}/{all}" : "")
             + (L.All > 0 && L.Ready < L.All ? $" · {L.Ready} prête{(L.Ready > 1 ? "s" : "")} sur {L.All}" : "")
             + $" · {_ship.Physics.Powder} charges\n";
    }

    void CycleGunSide()
    {
        int[] order = { 1, -1, 2, -2 };
        int i = Array.IndexOf(order, _gunSide);
        for (int n = 1; n <= 4; n++)
        {
            int s = order[(i + n) % 4];
            if (_ship.Battery.Has(s)) { _gunSide = s; break; }
        }
        Say("En batterie : " + GunNames[_gunSide]);
    }

    /* SERVIR LES PIÈCES d'une coque provoquée. Le bord est choisi sur le relèvement :
       elle ne tire que si la cible relève franchement par le travers — une pièce
       pointe en travers, et cela la fait manœuvrer au lieu de mitrailler. Trois cent
       quarante mètres, là où s'arrête le plein fouet. Un capitaine attend que la
       plus grande part de son bord soit prête. */
    void ServeGuns(ShipNode s, double dt)
    {
        var ph = s.Physics;
        if (ph.Foundered || ph.Powder <= 0 || s.Battery.Guns.Count == 0) return;
        _rearm.TryGetValue(s, out double rearm);
        rearm = Math.Max(0, rearm - dt);
        _rearm[s] = rearm;
        var foe = EnemyOf(s);
        if (foe == null || rearm > 0) return;
        var b = ph.Body;
        var to = foe.Body.Pos - b.Pos;
        to = new Vec3d(to.X, 0, to.Z);
        double range = to.Length;
        if (range > 340 || range < 12) return;
        var fwd = b.Quat.Rotate(new Vec3d(0, 0, 1));
        var starboard = fwd.Cross(new Vec3d(0, 1, 0));          // tribord vrai
        double abeam = to.Normalized().Dot(starboard);
        if (Math.Abs(abeam) < 0.62) return;                    // elle n'a pas le bord
        int side = abeam > 0 ? 1 : -1;
        var L = _gunnery.Loaded(s.Battery, side);
        if (L.Ready < Math.Max(1, (int)Math.Ceiling(0.6 * L.All))) return;
        int n = _gunnery.Broadside(s.Battery, side, ph, ph.Powder);
        if (n > 0) { ph.Powder -= n; _rearm[s] = 2 + _gunRng.NextDouble() * 3; }
    }
    readonly Dictionary<ShipNode, double> _rearm = new();
    void GunTick(double dt)
    {
        _gunnery.Targets.Clear();
        _gunnery.Targets.Add(TargetOf(_ship));
        foreach (var s in _others) { _gunnery.Targets.Add(TargetOf(s)); ServeGuns(s, dt); }
        _gunnery.Update(dt, _sea.Core, _t);
        var h = _sky.Core.Horizon;
        _gunFx.Step(dt, _sea.Core.WindVec, new Vec3d(h.R, h.G, h.B), _gunnery.Shots);
    }

    /* LA SOUTE. Trois charges, pas une, à quelques mètres et quelques dixièmes de
       seconde l'une de l'autre — au milieu d'abord, puis vers l'avant, puis bien
       à l'arrière —, posées dans SON repère. Puis elle s'ouvre à la mer, et ses mâts
       partent : une soute qui l'ouvre d'un bout à l'autre ne laisse pas trois
       bâtons debout. */
    void BlowUp(ShipNode s)
    {
        var ph = s.Physics;
        var b = ph.Body;
        double h = s.Spec.CeHeight, along = Math.Min(9, s.Spec.L * 0.15);
        (double Delay, Vec3d P, double S)[] shots =
        {
            (0.00, new Vec3d(0.0, h * 0.30, 0.0), 1.00),
            (0.32, new Vec3d(1.9, h * 0.55, along), 0.78),
            (0.66, new Vec3d(-1.6, h * 0.42, -along * 0.8), 0.90)
        };
        foreach (var (delay, p, sz) in shots)
        {
            var w = b.Quat.Rotate(p) + b.Pos;
            _gunFx.BlastIn(delay, new Vector3((float)w.X, (float)w.Y, (float)w.Z), s.Spec.L * sz);
        }
        ph.BlowUp();
        s.DropAllMasts();
        if (s == _ship) Say("La soute saute !");
    }

    // ------------------------------------------------------------------
    //  QUI SE BAT, ET POURQUOI — le pavillon noir, la barre des autres,
    //  le pirate qui chasse puis aborde
    // ------------------------------------------------------------------

    /* LE PAVILLON NOIR EST UNE DÉCLARATION, PAS UNE DÉCORATION : un navire est
       hostile parce qu'il arbore la tête de mort (appearance.ensign = « jolly »),
       et non parce qu'une fiche porte un drapeau booléen quelque part. Tous les
       autres sont pacifiques jusqu'à ce qu'on les touche. */
    static bool IsJolly(ShipNode s) => s.Spec.Appearance.Ensign == "jolly";

    readonly Dictionary<ShipNode, AutoHelm> _helms = new();
    readonly Dictionary<ShipNode, Pirate> _pirates = new();
    readonly List<Pirate.Sail> _sails = new();

    AutoHelm HelmOf(ShipNode s)
    {
        if (_helms.TryGetValue(s, out var h)) return h;
        h = new AutoHelm(s.Physics);
        _helms[s] = h;
        return h;
    }

    /* SON HUMEUR DE PIRATE, sur son entrée et nulle part ailleurs ; et la garde
       suit le pavillon : une barre réglée à une longueur et demie amène à quarante
       mètres, ce qui est un abordage et non un duel. Un navire qui vient canonner
       se tient au plein fouet. */
    void Arm(ShipNode s)
    {
        if (!IsJolly(s) || _pirates.ContainsKey(s)) return;
        var h = HelmOf(s);
        h.Standoff = Math.Max(h.Standoff, 185);
        _pirates[s] = new Pirate(h.Standoff);
    }

    /// <summary>La coque qu'un navire a pour ennemi, ou nulle : le pirate pendant sa chasse, un navire provoqué contre qui l'a touché.</summary>
    ShipPhysics? EnemyOf(ShipNode s)
    {
        if (_pirates.TryGetValue(s, out var p)) return p.Enemy;
        if (s.IsGhost) return _ghosts.Of(s.Physics)?.Foe;
        if (_hostile.TryGetValue(s, out var h) && IsInstanceValid(h.Foe) && !h.Foe.Physics.Foundered) return h.Foe.Physics;
        return null;
    }

    /* LA BARRE DES AUTRES, avant le solveur : elle écrit dans les mêmes commandes
       qu'une main, jamais dans la coque. Le pirate choisit où aller ; un navire
       provoqué va vers qui l'a touché ; les autres ne bougent pas de leur route. */
    void Steer(ShipNode s, double dt)
    {
        if (_pirates.TryGetValue(s, out var p))
        {
            var h = HelmOf(s);
            var prey = p.Cible;
            _sails.Clear();
            _sails.Add(new Pirate.Sail(_ship.Physics, _ship.Battery, IsJolly(_ship)));
            // un vrai pirate ne chasse pas les spectres
            foreach (var o in _others) if (!o.IsGhost) _sails.Add(new Pirate.Sail(o.Physics, o.Battery, IsJolly(o)));
            string? ev = p.Pilot(dt, _t, s.Physics, h, _sails, _ship.Physics);
            if (ev != null && prey != null)
            {
                bool mine = prey == _ship.Physics;
                if (ev == "abordage" && mine) Say("Le pirate cesse le feu — il vient vous aborder par l’arrière");
                else if (ev == "pillage")
                {
                    if (mine) Say("Abordés ! Le pirate vous pille et s’éloigne");
                    else if ((prey.Body.Pos - _ship.Physics.Body.Pos).Length < 3000)
                    {
                        string name = "un navire";
                        foreach (var o in _others) if (o.Physics == prey) { name = o.Spec.Name; break; }
                        Say("Le pirate aborde et pille " + name);
                    }
                }
            }
            h.Update(dt, _sea.Core, s.Ctrl);
            return;
        }
        if (s.IsGhost)
        {
            // droit sur l'ennemi que la scène lui donne ; la bataille finie, il garde sa route
            var foe = _ghosts.Of(s.Physics)?.Foe;
            var h = HelmOf(s);
            if (foe != null) h.Target = foe.Body.Pos;
            h.Update(dt, _sea.Core, s.Ctrl);
            return;
        }
        if (_hostile.TryGetValue(s, out var hs) && IsInstanceValid(hs.Foe))
        {
            var h = HelmOf(s);
            h.Target = hs.Foe.Physics.Body.Pos;
            h.Update(dt, _sea.Core, s.Ctrl);
        }
    }

    /* IL PARAÎT AU VENT, ET CAP SUR VOUS : un pirate tient l'AVANTAGE DU VENT —
       c'est lui qui choisit d'engager ou non, et vous qui devez remonter vers lui
       pour lui échapper ou le combattre. Paru sous le vent, il mettrait une
       demi-heure à louvoyer jusqu'à vous, gagnant sept dixièmes de nœud au vent. */
    /// <summary>Faire paraître un pirate à <paramref name="dist"/> mètres, au vent de vous, sa soute pleine.</summary>
    void SpawnPirate(double dist)
    {
        int idx = _paths.FindIndex(p => System.IO.Path.GetFileName(p) == "pirate.json");
        if (idx < 0) { GD.PushWarning("ships/pirate.json introuvable"); return; }
        int before = _others.Count;
        SpawnFleet(1, idx);
        if (_others.Count == before) return;
        var s = _others[^1];
        var b = s.Physics.Body;
        var me = _ship.Physics.Body.Pos;
        // d'où vient le vent, en relèvement : l'avant est +z, l'est −x
        double wf = _sea.Core.WindDeg * Math.PI / 180;
        b.Pos = new Vec3d(me.X - Math.Sin(wf) * dist, b.Pos.Y, me.Z + Math.Cos(wf) * dist);
        // cap sur vous : le vent dans le dos, ou presque
        double toMe = Math.Atan2(-(me.X - b.Pos.X), me.Z - b.Pos.Z);
        b.Quat = Quatd.FromAxisAngle(new Vec3d(0, 1, 0), -toMe);
        s.SyncTransform();
        Arm(s);
        Say("Une voile sous pavillon noir !");
    }

    void SetAutoWeather(bool on)
    {
        _weather.On = on;
        // l'allumer part de ce que montre la console : pas de mer téléportée à un autre jour
        if (on) _weather.Sync(_force, _windDeg);
        UpdateInfo();
    }

    /// <summary>
    /// LA MER OÙ ELLE EST VRAIMENT — trois choses la décident et se composent
    /// ICI : la console, qui fait le jour ; la météo, quand elle tourne seule ;
    /// et la dépression où elle se trouve, qui l'emporte sur les deux, une
    /// dépression ne négociant pas. UNE cible, que le spectre poursuit à vitesse
    /// bornée — ce qui compte surtout en entrant dans un grain, où un saut sans
    /// borne rebattrait toute la mer. Le vent, lui, est celui de l'instant : la
    /// toile sent la risée quand elle arrive, la houle met des minutes à suivre.
    /// </summary>
    void WeatherTick(double dt, double tNow)
    {
        _weather.Update(dt);
        double tgtF = _weather.On ? _weather.Force : _force;
        double tgtD = _weather.On ? _weather.Dir : _windDeg;
        string? master = _weather.On ? "météo auto" : null;

        // sa position VRAIE : les dépressions vivent en mètres monde, pas autour de l'origine flottante
        var b = _ship.Physics.Body;
        var o = _sea.Core.Origin;
        _inSquall = _storms.At(o.X + b.Pos.X, o.Z + b.Pos.Z, tNow, out _squall);
        if (_inSquall && _squall.Force > tgtF)
        {
            master = "dépression";
            tgtF = _squall.Force;
            /* le vent tourne autour du centre, par le plus court et à proportion de
               l'enfoncement : il refuse régulièrement à l'approche, et c'est ainsi
               qu'on trouve le milieu sans baromètre */
            double dd = (_squall.WindDeg - tgtD + 540) % 360 - 180;
            tgtD += dd * _squall.Inten;
        }

        // laissée à elle-même, la console EST la mer
        if (!_weather.On && !_inSquall) { _lagForce = tgtF; _lagDir = tgtD; }
        if (_weather.ChaseSea(ref _lagForce, ref _lagDir, dt, tgtF, tgtD))
        {
            _sea.Core.Time = tNow;          // la correction de bande se compte contre l'horloge
            _sea.Core.SetSeaState(_lagForce, _lagDir);
        }
        // le dernier mot est à la risée : la toile la sent, la houle suit plus tard
        _sea.Core.SetWind(tgtF, tgtD);
        _windNowDeg = (tgtD % 360 + 360) % 360;

        /* LA CONSOLE SUIT CE QUI SOUFFLE tant que la météo ou le grain décident,
           comme les curseurs de la page : sans quoi, à la sortie d'un grain, la
           mer retomberait d'un coup à ce qu'on avait réglé avant d'y entrer. */
        if (_weather.On || _inSquall) { _force = _lagForce; _windDeg = _windNowDeg; }
        _seaMaster = master;

        // le quart de ciel noir vers le centre, et ses éclairs
        _sky.SetSquall(_inSquall ? new Vector2((float)_squall.ToX, (float)_squall.ToZ) : Vector2.Zero,
                       _inSquall ? _squall.Loom : 0);
    }

    /// <summary>
    /// EMMÈNE-MOI DANS LE GROS TEMPS — tempete() de la page : la dépression la plus
    /// proche, et la coque posée à <paramref name="fraction"/> de son rayon (0 : au
    /// centre). Pas de terre dans cette démo, donc pas d'île où s'échouer : la
    /// page, elle, cherche de l'eau sous le grain. Le transport déplace l'ORIGINE
    /// — la coque reste où elle est, près de zéro, et le monde glisse sous elle —,
    /// et elle arrive droite et sans erre.
    /// </summary>
    void GoToStorm(double fraction)
    {
        var b = _ship.Physics.Body;
        var o = _sea.Core.Origin;
        double x = o.X + b.Pos.X, z = o.Z + b.Pos.Z;
        if (!_storms.Nearest(x, z, _t, 12, null, out var s, out double dist))
        {
            GD.Print("pas une dépression à portée");
            return;
        }
        double k = Math.Clamp(fraction, 0, 0.95);
        double tx = s.X + s.R * k, tz = s.Z;
        _sea.Core.Time = _t;
        _sea.Core.Rebase(tx - x, tz - z);
        _foam.Rebase((float)(tx - x), (float)(tz - z));
        _spray.Pool.Rebase(tx - x, tz - z);
        if (_kraken.State == KrakenState.Lurk || _kraken.State == KrakenState.Grip) _kraken.Dive("storm");
        if (_fixed) Plant();
        b.Vel = new Vec3d(0, 0, 0);
        b.AngVel = new Vec3d(0, 0, 0);
        _storms.At(tx, tz, _t, out var q);
        GD.Print(FormattableString.Invariant(
            $"dépression à {dist / 1852:F1} milles (rayon {s.R:F0} m, force {s.Peak:F1} au cœur) — force {q.Force:F1} ici"));
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
                case "--date": _calendar = new Calendar(args[i + 1]); break;
                case "--averse": _climate.StartShower(args[i + 1].ToFloat(), 1.0); break;
                case "--kraken": _kraken.Summon(args[i + 1] == "1", PreyOf(_ship)); break;
                case "--foudre": Strike(_ship); break;
                case "--bordee": _gunSide = args[i + 1].ToInt(); Fire(false, true); break;
                case "--soute": BlowUp(_ship); break;
                case "--pirate": SpawnPirate(args[i + 1].ToFloat()); break;
                case "--fantomes": GoToGhosts(true); break;
                case "--lunette": ToggleSpyglass(); break;
                // l'objectif mouillé d'emblée, pour juger les gouttes sans plonger
                case "--gouttes": _wet = Math.Clamp(args[i + 1].ToFloat(), 0, 1); break;
                // semer des pièces sous la coque, pour juger leur chute sans couler
                case "--tresor":
                {
                    var pb = _ship.Physics.Body;
                    _coins.Spill(new Vector3((float)pb.Pos.X, (float)pb.Pos.Y, (float)pb.Pos.Z),
                        _ship.Spec.L, _ship.Spec.B, args[i + 1].ToInt());
                    break;
                }
                case "--nappe": _sheet = Math.Clamp(args[i + 1].ToFloat(), 0, 1); break;
                // l'œil tourné vers le soleil, un peu au-dessus de l'eau : pour juger sa route
                case "--vers-soleil":
                    var sdir = _sky.Core.SunDir;
                    _fixEye = new Vector3(30, 6, 30);
                    _fixLook = _fixEye.Value + new Vector3((float)sdir.X, 0, (float)sdir.Z).Normalized() * 300 + new Vector3(0, -6, 0);
                    break;
                // les instruments masqués, comme H : pour une capture de la scène seule
                case "--masquer": _info.Visible = _sunPanel.Visible = args[i + 1] != "1"; break;
                case "--panneau-mer": _seaPanel.Visible = args[i + 1] == "1"; break;
                // la mer aux valeurs par défaut, sans toucher au fichier : pour comparer
                case "--mer-defaut":
                    var dm = new Settings();
                    _settings.SeaRoughBase = dm.SeaRoughBase; _settings.SeaRoughWind = dm.SeaRoughWind;
                    _settings.SeaSkyBlur = dm.SeaSkyBlur; _settings.SeaRideGain = dm.SeaRideGain;
                    _settings.SeaCapGain = dm.SeaCapGain; _settings.SeaFoamGain = dm.SeaFoamGain;
                    _settings.SeaJacobian = dm.SeaJacobian; _settings.SeaStreaks = dm.SeaStreaks; _settings.SeaKelvin = dm.SeaKelvin;
                    _settings.SeaShafts = dm.SeaShafts; _settings.SeaDensity = dm.SeaDensity;
                    ApplySettings();
                    break;
                // une cible par le travers tribord, à cette distance : le premier navire de --flotte
                case "--cible":
                    if (_others.Count > 0)
                    {
                        var cb = _others[0].Physics.Body;
                        cb.Pos = new Vec3d(-args[i + 1].ToFloat(), cb.Pos.Y, 0);
                        _others[0].SyncTransform();
                    }
                    break;
                case "--demater": _ship.DropMast(args[i + 1].ToInt()); break;
                case "--meteo": SetAutoWeather(args[i + 1] == "1"); break;
                case "--tempete": GoToStorm(args[i + 1].ToFloat()); break;
                case "--swell": _swell = args[i + 1].ToFloat(); Restate(); break;
                case "--pitch": _pitch = args[i + 1].ToFloat(); break;
                case "--dist": _dist = args[i + 1].ToFloat(); break;
                case "--orbit": _orbit = args[i + 1].ToFloat(); break;
                case "--ship": Launch(args[i + 1].ToInt()); break;
                case "--sails": _ship.Ctrl.SailsSet = args[i + 1] == "1"; break;
                // la machine en ligne de commande : une capture « en route » ne
                // peut pas dependre du clavier, et un banc non plus
                case "--throttle": _ship.Ctrl.Throttle = args[i + 1].ToFloat(); _drive = true; break;
                case "--barre": _heldRudder = Math.Clamp(args[i + 1].ToFloat(), -1, 1); break;
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
                // par les réglages (sans les enregistrer) : une autre option qui les réapplique ne la défait pas
                case "--vsync": _settings.VSync = args[i + 1] != "0"; ApplySettings(); break;
                case "--ssao": _sky.Env.SsaoEnabled = args[i + 1] == "1"; break;
                case "--ssil": _sky.Env.SsilEnabled = args[i + 1] == "1"; break;
                case "--frametimes": _ftLeft = args[i + 1].ToInt(); _ftGc0 = GC.GetTotalPauseDuration(); break;
                // le soleil figé à cette hauteur : pour éprouver la nuit sans attendre
                case "--sun": _sky.DayRate = 0; _sky.Core.SetSun(args[i + 1].ToFloat(), _sky.Core.SunBearingDeg); _sky.Apply();
                    GD.Print(FormattableString.Invariant($"nuit {_sky.Core.Night:F2}, lune {(_sky.Core.MoonOn ? "oui" : "non")} phase {_sky.Core.MoonPhase:F2} levée {_sky.Core.MoonUp:F2}, lumière de l'eau {_sky.Core.WaterLight:F3}, lumière directe {_sky.Core.SunIntensity:F3}"));
                    break;
                // ouvrir directement une vue à bord de la fiche
                // l'œil posé sur la surface, moitié dedans moitié dehors
                case "--mi-eau": _camMode = 3; SetLens(OutsideFov, OutsideNear); break;
                case "--mi-eau-haut": _splitLift = args[i + 1].ToFloat(); break;
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
        var cv = _ship.Physics.Body.Vel;
        GD.Print(err == Error.Ok ? FormattableString.Invariant($"capture écrite : {_capturePath} (erre {Math.Sqrt(cv.X * cv.X + cv.Z * cv.Z):F1} m/s)") : $"capture ratée : {err}");
        GetTree().Quit(err == Error.Ok ? 0 : 1);
    }
}
