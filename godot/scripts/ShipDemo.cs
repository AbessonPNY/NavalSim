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
    // le vent du départ à Port-Royal : belle brise par 105°, de quoi sortir du môle
    double _force = 4, _windDeg = 105, _cloud = 0.06;   // nuages : 6 %, le reglage par defaut de la page
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
    /// <summary>Set on the 0.15 s beat: the instruments rebuild their text then, not every frame.</summary>
    bool _hudBeat = true;
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

    public override void _ExitTree()
    {
        if (!_booted) return;                       // fermé pendant le chargement
        SaveBook(); SaveQuests(); _motionBlur?.Release(); _anamorphic?.Release(); _under?.Release(); _flare?.Release();
    }

    /// <summary>
    /// LE RIDEAU D'ABORD, LE MONDE ENSUITE.
    ///
    /// Bâtir la partie prend du temps — le relief est une image de neuf millions
    /// de pixels, les modèles pèsent des mégaoctets, et les shaders se compilent
    /// à la première image. Mesuré sur une machine RAPIDE : 2,2 s de démarrage du
    /// moteur, 1,4 s pour ce _Ready, 0,4 s de compilation, soit quatre secondes
    /// avant la première image. Sur un disque lent, davantage.
    ///
    /// Or rien ne peut être peint pendant qu'une méthode travaille : tant que
    /// _Ready n'est pas rendu, aucune image n'est dessinée, et un rideau posé au
    /// début serait affiché… après. Le travail est donc REPORTÉ d'une image :
    /// on pose le rideau, on attend qu'il soit VRAIMENT dessiné (FramePostDraw,
    /// et non un simple tour de boucle), et le monde se bâtit derrière lui.
    ///
    /// C'est le même geste que la traversée d'une région à l'autre, qui peint son
    /// avis puis attend un quart de seconde avant de recharger la scène — et
    /// comme ce rechargement repasse par ici, les deux rideaux se donnent la main
    /// au lieu de laisser un trou noir entre eux.
    ///
    /// Tant que le monde n'est pas là, _Process n'a rien à faire : <see
    /// cref="_booted"/> le tient à distance, et c'est la seule dette de ce
    /// report — tout ce qui tourne à chaque image doit savoir attendre.
    /// </summary>
    public override async void _Ready()
    {
        /* SANS ÉCRAN, PERSONNE NE DESSINE — et FramePostDraw n'arrive jamais. Le
           jeu ne démarrait plus du tout en mode sans-tête, c'est-à-dire dans tout
           ce qui le mesure et le vérifie : la panne muette du genre le plus bête,
           parce qu'elle ne se voit QUE là où l'on ne regarde pas. On n'attend donc
           une image que s'il y a quelqu'un pour la voir. */
        bool aVoir = DisplayServer.GetName() != "headless";
        Curtain();
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        if (aVoir) await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        Boot();
        _booted = true;
        /* ET IL RESTE EN PLACE UNE IMAGE DE PLUS : celle-là compile les shaders de
           la mer, du ciel et des coques, et c'est la plus longue de la partie. La
           lever avant elle rendrait l'écran à un monde qui n'est pas encore
           dessiné. */
        if (aVoir) await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        DropCurtain();
    }

    /// <summary>Vrai dès que le monde est bâti : avant, il n'y a rien à faire tourner.</summary>
    bool _booted;
    CanvasLayer? _curtain;

    /// <summary>
    /// « Chargement », sur le même noir que l'avis de traversée. Un rideau, et non
    /// une image : il n'y a rien à charger pour l'afficher, ce qui est bien le
    /// moins pour un écran dont le métier est de couvrir un chargement.
    /// </summary>
    void Curtain()
    {
        _curtain = new CanvasLayer { Layer = 20 };
        AddChild(_curtain);
        _curtain.AddChild(new ColorRect
        {
            AnchorRight = 1, AnchorBottom = 1, Color = new Color(0.02f, 0.03f, 0.04f)
        });
        var say = new Label
        {
            AnchorRight = 1, AnchorBottom = 1,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Text = "Chargement…"
        };
        say.AddThemeFontSizeOverride("font_size", 28);
        say.AddThemeColorOverride("font_color", new Color(0.92f, 0.86f, 0.70f));
        if (HandFont.Get() is { } hand) say.AddThemeFontOverride("font", hand);
        _curtain.AddChild(say);
    }

    void DropCurtain()
    {
        _curtain?.QueueFree();
        _curtain = null;
    }

    void Boot()
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
        BuildJournal();
        BuildWraith();
        _mist = new MistNode();
        AddChild(_mist);
        _mist.Material.SetShaderParameter("u_mist_top", (float)_mistTop);
        _mist.Material.SetShaderParameter("u_mist_patch", (float)_mistPatch);
        _mist.Material.SetShaderParameter("u_mist_gain", (float)_mistGain);
        _precip = new PrecipNode();
        AddChild(_precip);
        Assets.Say();
        LoadClimate();
        _lightning = new LightningNode
        {
            FlashEnergy = _lightRules.FlashEnergy,
            FlashRange = _lightRules.FlashRange,
            FlashShadow = _lightRules.FlashShadow,
            FlashLife = _lightRules.FlashLife,
            FlashColour = _lightRules.FlashColour
        };
        AddChild(_lightning);
        // et ce que le CIEL en prend : le reste du coup est la lampe du trait
        _sky.SkyFlash = _lightRules.SkyFlash;
        _sky.FarFlash = _lightRules.FarFlash;
        _sky.FarPerSecond = _lightRules.FarPerSecond;
        _krakenNode = new KrakenNode();
        AddChild(_krakenNode);
        _cordage = new CordageNode();
        AddChild(_cordage);
        _splinters = new SplinterNode();
        AddChild(_splinters);
        _gunFx = new GunFxNode { Timber = _splinters, FireLight = _fireLight };
        AddChild(_gunFx);
        _gunnery = new Gunnery(_gunRules);
        // ce que le coup éclaire de son propre bord : réglé à l'œil, pas mesuré
        _gunFx.FlashEnergy = _gunRules.FlashEnergy;
        _gunFx.FlashRange = _gunRules.FlashRange;
        _gunFx.FlashLife = _gunRules.FlashLife;
        WireGuns();
        _flotsam = new FlotsamNode();
        AddChild(_flotsam);
        _bubbles = new BubbleNode();
        AddChild(_bubbles);
        _coins = new CoinNode();
        AddChild(_coins);
        _sound = new SoundNode();
        AddChild(_sound);
        // la ville entendue : ses lecteurs à elle, sur le bus du dehors
        _city = new CitySound();
        _sound.AddChild(_city);
        // et l'averse, qui tombe partout à la fois
        _rainSound = new RainSound();
        _sound.AddChild(_rainSound);
        // les manifestes APRÈS lui : ils rangent leurs échantillons dans le nœud
        LoadSounds();
        LoadCrewVoices();
        /* LA TERRE. Le monde est lu une fois — une image de neuf millions de
           pixels et le champ de distance qui en sort — et rien après ne change :
           c'est une fonction pure de la position, en mètres VRAIS. */
        // les récifs de la fiche, dans le relief et sous la coque : Godot seul (Config.Reefs)
        Config.Reefs = true;
        // la barre des navires du jeu : réglée sur leur erre (le gain de vent les fait courir), et elle sonde
        Config.HelmBySpeed = true;
        Config.HelmSounds = true;
        // la godille au safran : des coups de barre secs poussent l eau même navire arrêté
        Config.RudderScull = true;
        // ce que coûte la carte (MapCost) : le monde lu, son terrassement, ses retouches
        using (MapCost.Time("monde"))
        {
            _world = WorldLoad.Load(_regionSheet = PickRegion());
            // le relief retouché à la main, AVANT que rien n'y lise (villes, abris, semis)
            TerraSetup();
            // les retouches de l'éditeur AVANT que la ville et la terre se bâtissent : elles s'y appliquent en se posant
            EditSetup();
        }
        /* LE CIMETIÈRE DES GALIONS est un lieu de la Jamaïque, donné en mètres de
           SA carte : ailleurs les mêmes chiffres tomberaient n'importe où. */
        if (_world != null && _world.Region.Key != "caraibes") _ghosts.Rules.Enabled = false;
        if (_world != null)
        {
            /* LE TROISIÈME CALCULATEUR. La coque flotte sur la mer que le shader
               dessine, abri compris : sans cette ligne, elle roulerait dans un
               bassin que l'œil voit calme. */
            _sea.Core.Shelter = (x, z) => _world.Shelter(x, z);
            _sea.Core.ShelterNear = (x, z, r) => _world.ShelterNear(x, z, r);
            _land = new LandNode(_world) { CausticRules = _causticRules, Editor = _editReg };
            AddChild(_land);
            PaintSetup();
            CausticDials();
            /* LES MOUETTES, qui disent la terre de plus loin que la terre : leur
               perchoir est le rivage le plus proche, que le monde sait rendre. */
            _gulls = new GullNode(_gullRules);
            AddChild(_gulls);
            /* LES DAUPHINS DE L'ÉTRAVE. Le noyau tient la bande, le nœud la pose ;
               le modèle peut venir de godot-models/ comme tout le reste. */
            _dolphins = new Dolphins(_dolphinRules)
            {
                Splash = (at, water, speed, jet) => _spray.Pool.Burst(at, water, speed, jet),
                Say = Say,
            };
            _dolphinNode = new DolphinNode();
            AddChild(_dolphinNode);
            if (!_dolphinNode.Build(Assets.Path(_dolphinRules.Glb ?? "creatures/dolphin.glb")))
            { _dolphinNode.QueueFree(); _dolphinNode = null; _dolphins = null; }
            /* LES BANCS DES HAUTS-FONDS, après la terre : c'est le fond qu'elle
               dessine qui décide où ils tiennent. */
            _fishNode = new FishNode { Rules = _fishRules };
            AddChild(_fishNode);
            if (!_fishNode.Build(Assets.Path("creatures/fish.glb")))
            { _fishNode.QueueFree(); _fishNode = null; }
            _town = new TownNode(_world) { Editor = _editReg };
            AddChild(_town);
            _folk = new FolkNode(_world) { Editor = _editReg };
            AddChild(_folk);
            _jetty = new JettyNode(_world) { Editor = _editReg };
            AddChild(_jetty);
            _grappleNode = new GrappleNode();
            AddChild(_grappleNode);
            // les épaves au fond, et la cloche pour y descendre (ShipDemo.Dive.cs)
            DiveBoot(System.IO.Path.Combine(Assets.Root, "ships"));
            /* LES RADES, bâties une fois avec le monde : des navires amarrés ne
               changent pas de place, et les mettre à l eau en cours de partie ferait
               de la géométrie au pire moment. Le nœud naît ici avec la terre, mais
               ses coques attendent Launch : leurs postes se mesurent au bau du
               joueur, qui n a pas encore de navire. */
            if (_mooredOn)
            {
                _moored = new MooredNode(_world) { Range = _mooredRange, Flotte = _mooredShips, Editor = _editReg };
                AddChild(_moored);
            }
            /* ET LE SOLVEUR APPREND OÙ ILS SONT. Une fois : un ponton ne bouge pas.
               En mètres MONDE VRAIS, comme tout ce qui est « du monde » — le solveur
               retranche l'origine lui-même, à chaque sous-pas. */
            using (MapCost.Time("pontons")) _jetty?.BuildPiers();
            if (_jetty != null) _jetty.PiersChanged += RefreshJetties;
            RefreshJetties();
            _book = LoadBook();
            _chart = new ChartNode(_world, _book)
            {
                PenWidth = _settings.PenWidth,
                Aim = QuestPlace, Jump = JumpPlace, Marks = () => _flotsam.Marks(), Wrecks = WreckMarks,
                // où l'on croit être, et — au débogage seulement — où l'on est
                Where = () => _reck != null && _reck.Known && _reckRules.Enabled ? (_reck.X, _reck.Z, _reck.SigN, _reck.SigE) : null,
                Truth = () => _ship == null ? null : TruePos()
            };
            AddChild(_chart);
            _compass = new CompassNode();
            _hud.AddChild(_compass);
            LoadQuests();
            LoadFishing();
            PlantCrosses();
            _anchor2 = new AnchorNode(_world, _sea)
            {
                Splash = (at, water, speed, jet) => _spray.Pool.Burst(at, water, speed, jet),
                OnSay = Say
            };
            AddChild(_anchor2);
            BuildTowns();
        }
        WireWreck();
        _krakenNode.Build(_krakenRules.Glb == null ? null
            : Assets.Path(_krakenRules.Glb));
        _kraken = new Kraken(_krakenRules) { BodyR = _krakenNode.BodyR };
        WireKraken();
        BuildWhale();
        BuildSerpent();
        BuildReckoning();

        _paths = ShipLibrary.Discover();
        GD.Print($"{_paths.Count} fiche(s) lue(s) dans {ShipLibrary.Folder}");
        // le navire : celui d une traversée, celui d une partie reprise, sinon le premier
        int first = _arriving?.Ship ?? 0;
        if (_loading is { } sv && sv.Navire.Length > 0)
        {
            int k = ShipLibrary.IndexOf(_paths, sv.Navire);
            if (k >= 0) first = k;
        }
        Launch(first);
        // et les rades, maintenant qu on connaît le navire dont il faut laisser le poste
        // la rade est du décor de la carte : elle entre dans son coût (MapCost), voiles serrées comprises
        using (MapCost.Time("rade"))
        {
            _moored?.Build(System.IO.Path.Combine(Assets.Root, "ships"), _sea.Core, _ship.Spec.L, _ship.Spec.B);
            _moored?.BuildAnchored(System.IO.Path.Combine(Assets.Root, "ships"), _sea.Core);
        }
        // les réglages du fichier d'abord ; la ligne de commande, lue ensuite, a le dernier mot
        ApplySettings();
        SetupCapture();
        // à son poste en DERNIER : --ship a pu changer la coque, et le dégagement
        // du quai se mesure sur SON bau
        // ou à l'atterrage, si l'on arrive d'une traversée
        if (!Resume() && !Arrive()) Moor();
        if (_wantMelee) Skirmish();
        // en DERNIER : il ne s'ouvre que si la ligne de commande ne demande pas
        // autre chose, et il ne touche donc jamais à ce qu'elle vient de régler
        BuildTitle();
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
        // le reflet de lentille en dernier : il naît dans l'objectif, après tout ce que la scène a fait
        _flare = new LensFlareEffect { Enabled = false };
        // l'eau d'abord : les flous viennent sur l'image qu'elle a déjà teinte
        _cam.Compositor = new Compositor { CompositorEffects = new Godot.Collections.Array<CompositorEffect> { _under, _anamorphic, _motionBlur, _flare } };
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
        _hud = layer;
        // LE PAVÉ DE DÉBOGAGE : fermé au départ, ouvert par H (voir ShipDemo.Hud.cs)
        _info = new Label { Position = new Vector2(18, 14), Visible = false };
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
        BuildKeys(layer);
        BuildChartView(layer);
        BuildTrim(layer);
        BuildMarket(layer);
        BuildFleetPanel(layer);
        BuildGunSide(layer);
        BuildHud(layer);
        BuildPause(layer);
        BuildMelee(layer);
        BuildStow(layer);
        BuildEncart(layer);
        BuildQuestView(layer);
        BuildLost(layer);
        BuildSpyglass();
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
    /* QUI D'AUTRE EST À L'EAU — refait à chaque image et jamais retenu par une
       coque, pour la raison qui a déjà coûté un bogue à la page : une référence
       gardée se périme dès que la flotte change. Les spectres ont la leur : ils
       ne se cognent qu'entre eux, et une étrave passe au travers. */
    readonly List<ShipPhysics> _afloat = new(), _wraiths = new(), _withMoored = new();
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
        _afloat.Clear(); _wraiths.Clear();
        foreach (var s in _stepping) (s.IsGhost ? _wraiths : _afloat).Add(s.Physics);
        /* ET LES COQUES AU MOUILLAGE, pour le navire du joueur seulement : figées, elles
           le repoussent ; les navires du jeu, eux, ne les voient pas — leurs routes de
           rade n'ont pas été tracées pour les éviter. */
        _withMoored.Clear();
        _withMoored.AddRange(_afloat);
        if (_moored != null)
        {
            _moored.Colliders(_ship.Position, _ship.Spec.L * 0.5 + 10);
            _withMoored.AddRange(_moored.Near);
        }
        _stepSub = sub; _stepDt = dt; _stepT0 = t0;
        // un délégué gardé : en recréer un à chaque image serait de la mémoire à ramasser
        _stepOne ??= i =>
        {
            var s = _stepping[i];
            double t = _stepT0;
            var near = s.IsGhost ? _wraiths : s == _ship ? _withMoored : _afloat;
            for (int k = 0; k < _stepSub; k++) { s.Physics.Step(_stepDt, _sea.Core, s.Ctrl, t, near); t += _stepDt; }
        };
        /* EN PARALLÈLE, UNE COQUE LIT LA POSE D'UNE AUTRE PENDANT QU'ELLE S'ÉCRIT.
           Ce sont des doubles alignés, donc jamais un nombre à moitié écrit : au
           pire une pose d'un sous-pas de retard, soit quelques millimètres. Le
           ressort de contact y perd un cheveu de sa symétrie et rien d'autre.
           Séquentiel, la page fait déjà pareil — elle avance ses coques l'une
           après l'autre, et la seconde voit la première déjà partie. */
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

    // the sea under a point, now: one delegate kept, not one made per frame
    Func<double, double, double>? _seaHere;

    void StepOthers(double frame)
    {
        foreach (var s in _others)
        {
            s.SyncTransform();
            var p = s.Physics;
            s.SetTrim(s.Ctrl.Sheet, p.Tack, p.SetFrac, p.Luffing, _t, p.SailLoad);
            s.Sea = _sea.Core;
            s.StreamFlags(_t);
            s.RecoilTick(_gunnery.Clock, _gunRules.RecoilSpeed);
            s.SwingLanterns(frame);
            s.SpinRings(frame);
        }
    }

    /// <summary>
    /// METTRE UNE COQUE À L'EAU, et l'ordre compte.
    ///
    /// <c>Settle()</c> la laisse trouver sa flottaison en eau PLATE — sans quoi
    /// elle arrive en plein ciel et rebondit — puis remet la mer comme elle
    /// était, ce que le noyau fait lui-même pour qu'aucun appelant n'ait à le
    /// savoir.
    /// </summary>
    /// <summary>
    /// ELLE S'ASSIED SUR L'EAU, PAS SUR LE SABLE.
    ///
    /// <c>Settle</c> repose la coque au zéro LOCAL et y fait tourner le vrai
    /// solveur, échouage compris. Or au départ le zéro local est Port-Royal —
    /// c'est-à-dire la LANGUE DE SABLE : la coque s'y posait sur le sol, trouvait
    /// un tirant d'eau de zéro et rendait comme « flottaison » la hauteur du
    /// terrain. Tout ce qui en découlait était faux, et silencieusement : le
    /// contour à la flottaison était relevé dix mètres SOUS le bordé, donc vide,
    /// donc remplacé par le rectangle de secours — d'où le collier d'écume
    /// rectangulaire autour de chaque coque. La preuve par le chiffre : la même
    /// coque s'asseyait à 7,92 m avant que la berge soit relevée et à 11,27 m
    /// après, sans que rien de la coque ait changé.
    ///
    /// Le fond est donc décroché le temps qu'elle trouve ses lignes. Le solveur
    /// le lui rend à l'image suivante, là où elle est vraiment.
    /// </summary>
    static double SettleAfloat(ShipNode s, NavalSim.Core.Ocean sea)
    {
        var ground = s.Physics.World;
        s.Physics.World = null;
        double y = s.Physics.Settle(sea, s.Ctrl);
        s.Physics.World = ground;
        return y;
    }

    void Launch(int i)
    {
        if (_paths.Count == 0) { _info.Text = "aucune fiche trouvée"; return; }
        _index = (i % _paths.Count + _paths.Count) % _paths.Count;
        var spec = ShipLibrary.Load(_paths[_index]);
        if (spec == null) return;

        if (_ship != null) { RemoveChild(_ship); _ship.QueueFree(); }
        _dressed = false;             // la nouvelle coque a sa propre feuille

        _ship = new ShipNode();
        AddChild(_ship);
        _ship.LanternShadows = _settings.LanternShadows;
        _ship.WithMastLantern = _settings.MastLantern;
        _ship.Build(spec);
        // LE FOND, sans quoi elle ne touche jamais : le monde EST l'IGround du solveur
        _ship.Physics.World = _world;
        _ship.Physics.Jetties = _jetties;
        _ship.Ctrl.SailsSet = false;
        _ship.Ctrl.Sheet = 0.6;

        double y = _eqY = SettleAfloat(_ship, _sea.Core);
        _ship.SyncTransform();
        GD.Print($"{spec.Name} : assise à y = {y:F3} m, tirant {_ship.Physics.Draft:F2} m, "
               + $"immersion {_ship.Physics.SubmergedFrac * 100:F1} %");

        /* SON CONTOUR À LA MER, dans sa rangée : le collier suit le bordé qu'on
           VOIT — mesuré sur le modèle s'il en porte un, lu dans le plan de formes
           sinon —, à la flottaison qu'elle vient de trouver. */
        _prof = _ship.MakeProfile(-y);
        _sea.SetHullProfile(0, _prof);
        // le modèle pèse-t-il ce que dit sa fiche ? un mot si non (ShipNode.VolumeCheck.cs)
        _ship.CheckModelVolume(-y);
        // et l'embrun ne la traverse plus : il rebondit sur son bordé ou reste sur son pont
        if (_hullCollider != null) _spray.Pool.Colliders.Remove(_hullCollider);
        _hullCollider = _ship.MakeCollider(_prof);
        _spray.Pool.Colliders.Add(_hullCollider);
        string what = _ship.ModelRoot != null
            ? $"modèle {spec.Model!.Glb}, échelle {_ship.ModelRoot.Scale.X:F4}"
            : "coque procédurale";
        GD.Print($"  {what} ; profil : demi-largeur {_prof.MaxHalfB:F3} m (fiche {spec.B * 0.5:F3}), "
               + $"corps {_prof.EndAft:F2} à {_prof.EndFwd:F2} m, hauts à {_prof.Top:F1} m (milieu {(_prof.Heights is { } hh ? hh[hh.Length / 2] * _prof.Top : _prof.Top):F1}) sur l'eau");
        RefitFleet();
        HoistNation();
        Found();   // une coque neuve n est pas celle qu on vient de perdre
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

    /// <summary>--chavirer : la coque retournée une seconde après la mise à quai, qui la redresserait.</summary>
    double _flipIn = -1, _serpentIn = -1;
    /* L'ÉPREUVE DU BOULET ATTEND QUE LA TOILE SOIT DEHORS. Établir prend du
       temps, et au premier instant les voiles sont encore roulées sur leurs
       vergues — un mètre de haut au lieu de cinq (relevé). Un boulet lâché à la
       ligne de commande traversait donc un navire à sec de toile et ne trouvait
       rien. */
    /* CES DEUX-LÀ ATTENDENT, COMME LE BOULET D'ÉPREUVE, ET POUR LA MÊME RAISON —
       mais elle est ici plus vicieuse. La ligne de commande est lue AVANT que le
       jeu ait fini de se mettre en place, et Home() passe derrière : il vide la
       flotte et recentre la coque. Une rencontre forcée au démarrage naissait
       donc bel et bien — le message le disait, « aux prises à 4543 m » — puis
       disparaissait dans la seconde, sans un mot. J'ai cru l'essai réussi sur ce
       message, et il ne prouvait rien : il faut compter la flotte APRÈS. */
    double _largeIn = -1, _metIn = -1, _pontonIn = -1, _souteIn = -1;
    bool _metPair;
    double _shotIn = -1, _shotY, _holesIn = -1;
    int _holesWanted;

    public override void _Process(double delta)
    {
        // le monde n'est pas encore bâti : le rideau est seul à l'écran
        if (!_booted) return;
        if (_serpentIn > 0 && (_serpentIn -= delta) <= 0) SummonSerpent();
        if (_largeIn > 0 && (_largeIn -= delta) <= 0) GoOffshore();
        if (_souteIn > 0 && (_souteIn -= delta) <= 0) BlowUp(_ship);
        if (_diveTestIn > 0 && (_diveTestIn -= delta) <= 0) DiveTest();
        FishTestTick(delta);
        if (_anchorTest is Vector3 at && _anchor2 != null && !_inTitle)
        {
            if (_t >= at.X && _t - delta < at.X)
            {
                _ship.Ctrl.Throttle = 1;
                // lancé d'un coup, comme la touche B : c'est le cas où on l'oublie
                var qb = _ship.Physics.Body; var f0 = qb.Quat.Rotate(new Vec3d(0, 0, 1));
                qb.Vel = new Vec3d(f0.X * 4, qb.Vel.Y, f0.Z * 4);
                GD.Print("[ancre] en route, lancé à 4 m/s");
            }
            if (_t >= at.Y && _t - delta < at.Y) { _anchor2.Toggle(_ship, _t); GD.Print("[ancre] on vire"); }
            // la photo à l'heure dite (la troisième valeur), l'œil par le travers arrière, vers l'ancre
            if (at.Z > 0)
            {
                var bp = _ship.Physics.Body.Pos; var fw = _ship.Physics.Body.Quat.Rotate(new Vec3d(0, 0, 1));
                // l'œil par le travers, à hauteur d'homme, sur l'avant : là où l'ancre sort de l'eau et monte au bossoir
                _fixEye = new Vector3((float)(bp.X + fw.X * 16 - fw.Z * 16), 4f, (float)(bp.Z + fw.Z * 16 + fw.X * 16));
                _fixLook = new Vector3((float)(bp.X + fw.X * 12), 0f, (float)(bp.Z + fw.Z * 12));
                if (_t >= at.Z && _t - delta < at.Z) { _captureIn = 1; GD.Print($"[ancre] photo à t {_t:F0}"); }
            }
            if (Math.Floor(_t) != Math.Floor(_t - delta))
            {
                var bv = _ship.Physics.Body.Vel;
                GD.Print(FormattableString.Invariant($"[ancre] t {_t:F0}  {bv.LengthXZ:F2} m/s  {_anchor2.Probe(_ship)}"));
            }
        }
        // le départ écrit dans la fiche se fait à l'ancre : on la mouille dès que la coque est posée
        if (_anchorAtStart && !_inTitle && _anchor2 != null)
        {
            _anchorAtStart = false;
            if (!_anchor2.IsDown(_ship)) _anchor2.Toggle(_ship, _t);
        }
        if (_pontonIn > 0 && (_pontonIn -= delta) <= 0) BackToBerth();
        if (_metIn > 0 && (_metIn -= delta) <= 0) ForceEncounter(_metPair);
        if (_shotIn > 0 && (_shotIn -= delta) <= 0) TestShot(_shotY);
        if (_holesIn > 0 && (_holesIn -= delta) <= 0) TestHoles(_holesWanted);
        if (_flipIn > 0 && (_flipIn -= delta) <= 0)
        {
            _ship.Physics.CastOff();
            _ship.Physics.Body.Quat = Quatd.FromAxisAngle(new Vec3d(0, 0, 1), Math.PI) * _ship.Physics.Body.Quat;
            _ship.SyncTransform();
        }
        FrameStats(delta);
        _ftWatch.Restart();
        long ap0 = GC.GetAllocatedBytesForCurrentThread();

        /* Le pas d'image est PLAFONNÉ à cinquante millisecondes, pour protéger le
           solveur d'une saccade : une image d'une seconde ferait faire à la
           coque un bond d'une seconde, ressorts raides compris. */
        double frame = Math.Min(0.05, delta);
        _t += frame;

        // au titre, la barre et les voiles ne répondent pas : l'œil fait sa ronde
        // en mode création la barre ne répond plus : les touches sont à l'éditeur
        if (_filmLater != null) { var fl = _filmLater; _filmLater = null; BeginFilm(fl); }
        if (_inTitle) TitleTick(frame); else if (_editing) EditTick(frame); else if (_film != null) FilmTick(frame); else ReadKeys(frame);
        ObjectivesTick(frame);
        SwimTick(frame);
        SightTick(frame);
        CombatLogTick();
        StarsTick();
        SwimAfter(frame);
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
        ShoveTick(frame);
        StepHalves(sub, dt, t);
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
        /* la bôme tenue à contre se voit de ce bord : l'angle dessiné vaut −amure ×
           écoute, d'où l'amure contraire au bord poussé */
        if (_ship.Ctrl.Backed != 0)
            _ship.SetTrim(ShipPhysics.BackedAngle, -_ship.Ctrl.Backed, ph.SetFrac, false, _t, ph.SailLoad);
        else
            _ship.SetTrim(_ship.Ctrl.Sheet, ph.Tack, ph.SetFrac, ph.Luffing, _t, ph.SailLoad);
        _ship.Sea = _sea.Core;
        _ship.StreamFlags(_t);
        _ship.RecoilTick(_gunnery.Clock, _gunRules.RecoilSpeed);
        // le branle-bas : les sabords de chacun, avant que le recul de l'image suivante ne lise ses pièces
        PortsTick(frame);
        // les avirons balancent EN MESURE avec le coup que le solveur vient de donner
        _ship.SetOars(frame);
        foreach (var s2 in _others) s2.SetOars(frame);
        // les lanternes pendues suivent le roulis en vrais pendules
        _ship.SwingLanterns(frame);
        /* LES FILINS APRÈS LES SOLVEURS : une contrainte corrige ce que
           l'intégration vient de faire. La poser avant reviendrait à corriger
           l'image d'avant, et le bout paraîtrait élastique d'un pas de temps. */
        /* LES FILINS QUI NE TIENNENT PLUS À RIEN partent avant d'être halés : un bout
           sur une coque coulée, retirée, ou quittée par le joueur au chantier. */
        if (_grapples.Lines.Count > 0) _grapples.Prune(GrappleLive);
        _grapples.Step(frame);
        _ship.SyncTransform();
        _grappleNode?.Sync(_grapples);

        // et ce qui tourne sur elle sans rien devoir a la houle
        _ship.SpinRings(frame);
        /* SON REFLET, retourné autour du plan d eau SOUS LE NAVIRE — pas autour du
           zéro : un miroir calé sur le zéro hydrographique décollerait de l eau à
           chaque lame. */
        {
            var sp = _ship.GlobalPosition;
            _ship.ForcedLevel = _forceRings;
            _ship.MirrorOn = _refletMode != 0;
            _ship.MirrorMask = _refletMode != 2;
            _ship.MirrorFade = _refletMode != 2 && _refletMode != 3;
            _ship.MirrorDebug = _refletMode == 4;
            _ship.SyncMirror(_sea.Core.Sample(sp.X, sp.Z, _t), _cam);
            foreach (var m in _ship.MirrorMaterials) PushSeaTo(m);
        }
        JumpTick(frame);
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
            _whale?.Rebase(-dx, -dz);
            _serpent?.Rebase(-dx, -dz);
            _lightning.Rebase(-dx, -dz);
            _cordage.Rebase(-dx, -dz);
            _dolphins?.Rebase(-dx, -dz);
            _wraith?.Rebase(-dx, -dz);
            _splinters.Rebase(-dx, -dz);
            _gunnery.Rebase(-dx, -dz);
            foreach (var pr in _pirates.Values) pr.Rebase(-dx, -dz);
            _gunFx.Rebase(-dx, -dz);
            _wreckAir.Rebase(-dx, -dz);
            _bubbles.Rebase(dx, dz);
            _coins.Rebase(dx, dz);
            _anchor2?.Rebase(-dx, -dz);
            RebaseHalves(dx, dz);
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

        /* La terre, contre l'origine du MOMENT — après le recentrage, sans quoi
           elle serait en désaccord avec la mer d'exactement ce décalage. */
        if (_land != null)
        {
            var wo = _sea.Core.Origin;
            var here = new Vec3d(wo.X + b.Pos.X, 0, wo.Z + b.Pos.Z);
            _land.Update(ViewCentre(here), new Vec3d(wo.X, 0, wo.Z), _landEager);
            // les ajouts de l'éditeur, contre la même origine
            EditFrame(new Vec3d(wo.X, 0, wo.Z), frame);
            _landEager = false;
            // le niveau de la mer sous le navire décide de la hauteur d'eau
            _fishNode?.Update(_world, here, new Vec3d(wo.X, 0, wo.Z), frame,
                _sea.Core.Sample(b.Pos.X, b.Pos.Z, _t));
            if (_gulls != null) { _gulls.Night = _sky.Core.Night; _gulls.Rain = _fall.Amount; }
            _gulls?.Update(_world, here, new Vec3d(wo.X, 0, wo.Z), frame, _t);
            _city?.Update(_world, here, new Vec3d(wo.X, 0, wo.Z), _sky.Core.Night, _gulls?.Port, _sound?.On == true, frame);
            if (_dolphins != null)
            {
                /* L'ÉTAT DE MER ET LA CÔTE décident s'ils viennent ; le temps de
                   jeu écoulé, à quelle fréquence. Les heures sont celles de
                   l'horloge du ciel, comme dans la page. */
                _dolphins.Step(frame, b, _ship.Spec, _force, frame * _sky.DayRate / 60.0,
                    _world.ShoreDistance(here.X, here.Z), _t,
                    _seaHere ??= (x, z) => _sea.Core.Sample(x, z, _t));
                _dolphinNode?.Sync(_dolphins);
            }
            foreach (var m in _land.Hazed) { _sky.PushTo(m); _sky.SetCloud(m, _cloud, _t); }
            if (_town != null)
            {
                _town.Update(ViewCentre(here), new Vec3d(wo.X, 0, wo.Z));
                foreach (var m in _town.Hazed) { _sky.PushTo(m); _sky.SetCloud(m, _cloud, _t); }
            }
            if (_folk != null)
            {
                _folk.Update(ViewCentre(here), new Vec3d(wo.X, 0, wo.Z));
                foreach (var m in _folk.Hazed) { _sky.PushTo(m); _sky.SetCloud(m, _cloud, _t); }
            }
            if (_anchor2 != null)
            {
                _anchor2.Step(frame, _t);
                // et à poste sur le flanc, celles qui ne sont pas mouillées (AnchorNode.PoseStowed)
                _anchor2.PoseStowed(_ship);
                foreach (var s in _others) _anchor2.PoseStowed(s);
                _anchor2.Prune();
                foreach (var m in _anchor2.Hazed) { _sky.PushTo(m); _sky.SetCloud(m, _cloud, _t); }
            }
            PushHarbour(here);
            if (_chart != null)
            {
                // la feuille du bureau porte la carte dès que le modèle est là
                if (!_dressed && _chart.Texture != null)
                {
                    _dressed = _ship.DressChart(_chart.Texture);
                    // et le livre du bureau, si le modèle en porte un
                    if (_journalNode != null) _ship.DressJournal(_journalNode.Texture);
                    // se PENCHER dessus : la vue se pose depuis la feuille elle-même
                    if (_dressed && _overChart && _ship.ChartSurface is MeshInstance3D ms)
                    {
                        var ab = ms.GetAabb();
                        var c = ms.GlobalTransform * (ab.Position + ab.Size * 0.5f);
                        var up = ms.GlobalTransform.Basis.Y.Normalized();
                        _fixLook = c;
                        _fixEye = c + up * 0.42f - ms.GlobalTransform.Basis.Z.Normalized() * 0.12f;
                        _planted = true;
                    }
                }
                // pas pendant le titre : le navire y est posé au large pour l'affiche
                if (!_inTitle) _chart.Sail(here.X, here.Z);
                /* Un port touché quand on vient à moins de trois cents mètres de
                   son quai : c'est la distance à laquelle on l'a vraiment vu. */
                if (!_inTitle) foreach (var isl in _world!.Near(here.X, here.Z, 300))
                    if (_book!.Touch(isl.Key))
                    {
                        _chart.Refresh();
                        Say($"{isl.Name} portée sur la carte.");
                    }
            }
            QuestTick(frame);
            FishTick(frame);
            PassageTick();
            CompassTick();
            ReckonTick(frame);
            RumourTick(frame);
            DiveTick(frame, here, new Vec3d(wo.X, 0, wo.Z));
            if (_moored != null)
            {
                _moored.Update(ViewCentre(here), new Vec3d(wo.X, 0, wo.Z), _sea.Core, _t);
                foreach (var m in _moored.Hazed) { _sky.PushTo(m); _sky.SetCloud(m, _cloud, _t); }
            }
            if (_jetty != null)
            {
                _jetty.ShowNumbers = _editing;
                _jetty.Update(ViewCentre(here), new Vec3d(wo.X, 0, wo.Z));
                foreach (var m in _jetty.Hazed) { _sky.PushTo(m); _sky.SetCloud(m, _cloud, _t); }
            }
        }

        UpdateCamera(frame);
        AimSpyglass(frame);                     // après la vue : sa position, la visée de la lunette
        ShakeCamera(frame);                     // et la secousse par-dessus, une fois l'œil posé
        WhistleTick();                          // l'oreille est posée : les boulets qui passent près d'elle
        CineFocus();                            // la mise au point du cinéma, sur l'œil posé
        // les navires et la caméra de la MÊME image : le flou compare les deux
        _motionBlur.BeginShips();
        _motionBlur.AddShip(_ship.GlobalTransform, _ship.LocalBounds());
        foreach (var s in _others) _motionBlur.AddShip(s.GlobalTransform, s.LocalBounds());
        _motionBlur.EndShips();
        // les vues à bord ont leur propre champ : la vérification lit celui d'ICI
        _motionBlur.MainProjection = _cam.GetCameraProjection();
        _sea.UpdateFrom(_cam.GlobalPosition, _t);
        /* LA MÊME MER SUR LE FOND. Le spectre que la surface vient de recevoir
           est poussé tel quel à la matière du fond — APRÈS UpdateFrom, qui
           remplit les tableaux de cette image. Deux copies du spectre finiraient
           par ne plus l'être, et le fond scintillerait sur une houle que l'œil
           ne voit pas : c'est la panne silencieuse habituelle. */
        if (ShipNode.Caustic != null && !_causticDialed) { CausticDials(); _causticDialed = true; }
        if (_land != null) { PushSea(_land.Ground); foreach (var m in _land.Swaying) PushSea(m); }
        if (_fishNode?.Material is ShaderMaterial fm) PushSea(fm);
        // la MÊME lumière sur les carènes, et une seule passe pour toute la flotte
        if (ShipNode.Caustic is ShaderMaterial hc) PushSea(hc);
        // la brume rasante se pose sur la MÊME houle, et prend la couleur du ciel
        if (_mist != null) PushSea(_mist.Material);
        void PushSea(ShaderMaterial m) => PushSeaTo(m);
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
        // ce qui ferme le ciel en plus de l'orage : l'averse, le grain, la brume, la couverture
        _sky.Overcast = Math.Max(Wet(), 0.7 * Math.Clamp((_cloud - 0.55) / 0.45, 0, 1));
        // ce qui tombe ferme aussi l'horizon et le ciel, même par petite brise (Sky.Rain)
        _sky.Core.Rain = _fall.Amount;
        _sky.UpdateWeather(frame, _sea.Core.SeaState);
        FallTick(frame, dayBefore);
        StormTick(frame);
        GunTick(frame);
        EncounterTick(frame);
        TrafficTick(frame);
        CabinTick(frame);
        CreakTick();
        BowSprayTick();
        DiveShow(frame);
        CrewTick(frame);
        TackCalls();

        SinkFilmTick(frame);
        WreckTick(frame);
        TickSunPanel(frame);
        /* La lueur n'existe pas le jour — bloom.js saute sa passe tant que la nuit
           n'a pas passé 0,02 : le soleil sur la houle déborderait le seuil et
           voilerait la mer. Allumée aux premières images pour être compilée. */
        if (_glowPrime > 0) _glowPrime--;
        /* LES ANNEAUX ALLUMENT LA LUEUR EN PLEIN JOUR, ce que rien d'autre ne fait.
           Le seuil de nuit (0,319 en linéaire) est SOUS le soleil sur la houle :
           l'ouvrir de jour voilerait la mer entière, et c'est pour cela qu'elle
           est coupée le jour. On le relève donc pendant la charge, au-dessus de
           tout ce qu'une mer ensoleillée atteint et sous ce qu'un anneau émet
           (u_energy = 6) : il ne reste alors qu'eux au-dessus du seuil.

           1,6 est un premier chiffre, à juger à l'œil contre un soleil au zénith.
           La nuit, rien à faire : le seuil ordinaire suffit et les anneaux le
           passent de très loin. */
        bool ringsLit = _ship != null && _ship.RingsLit;
        bool day = _sky.Core.Night <= 0.02;
        _sky.Env.GlowEnabled = _settings.Glow && (_glowPrime > 0 || !day || ringsLit);
        float want = ringsLit && day ? GlowDayThreshold : GlowThreshold;
        if (_sky.Env.GlowHdrThreshold != want) _sky.Env.GlowHdrThreshold = want;
        _dofMarker.Visible = _settings.Dof && (_menu.Visible || _dofMarkerKeep);
        if (_dofMarker.Visible)
            _dofMarker.Draw(_cam, _sea.Core, _t, _settings.DofNear, _settings.DofDistance, _settings.DofFade);
        // les feux et les fenêtres suivent la nuit du ciel, et s'effacent au loin
        _ship.SetLantern(_sky.Core.Night, _t, _cam.GlobalPosition, _sky.Core);
        _town?.SetNight(_windowsOff ? 0 : _sky.Core.Night);     // les villes s allument avec les fanaux
        _town?.SetLamps(_sky.Core.Night, _sky.Core.DayTime, _t);
        foreach (var s in _others) s.SetLantern(_sky.Core.Night, _t, _cam.GlobalPosition, _sky.Core);
        // après les feux : la scène lit la nuit par leur règle
        GhostTick(frame);
        /* LE VAISSEAU QUI RÔDE, après les feux comme la bataille fantôme : il vient
           à la LUMIÈRE, et il lui faut donc savoir ce que le bord montre. Muet en
           escarmouche, comme le reste du bestiaire : une voile de plus dans une
           mêlée de douze, et qu'on ne peut ni couler ni éviter, ne serait qu'une
           confusion. */
        if (!_skirmish) WraithTick(frame, frame * _sky.DayRate / 60.0);
        // et la mer les voit : leur reflet et leur lumière sur l'eau
        /* LE TELEPORTEUR EN PREMIER : les huit places sont partagees par la flotte
           et les fanaux les prennent volontiers toutes. Une sphere de vingt metres
           qui ne se refleterait pas parce qu un feu de poupe avait pris le dernier
           creneau serait une panne difficile a comprendre. */
        Array.Clear(_sea.LampAim);      // tout feu rayonne partout, sauf une fenêtre qui dit où elle regarde
        int lamps = _ship.FillRingLamp(_sea.Lamps, _sea.LampRange, _sea.LampCol, _sea.LampSize, 0);
        lamps += _ship.FillLamps(_sea.Lamps, _sea.LampRange, _sea.LampCol, _sea.LampSize, _sea.LampAim, lamps);
        foreach (var s in _others) lamps += s.FillLamps(_sea.Lamps, _sea.LampRange, _sea.LampCol, _sea.LampSize, _sea.LampAim, lamps);
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
        _sea.Material?.SetShaderParameter(U.Submerged, under ? 1f : 0f);
        /* LA CÔTE LA PLUS PROCHE, pour l'écume de déferlement : elle se décide en
           lisant l'image, qui ne sait pas distinguer une plage d'une carène, et
           il faut donc lui dire la seule chose qu'elle ne peut pas deviner. */
        if (_world != null)
        {
            var bp = _ship.Physics.Body.Pos;
            _sea.Material?.SetShaderParameter(U.ShoreDist,
                (float)_world.ShoreDistance(_sea.Core.Origin.X + bp.X, _sea.Core.Origin.Z + bp.Z));
        }
        /* ET LES BOUFFÉES AVEC : de dessous, le feu des canons ne traverse la
           fenêtre de Snell que s'il est dessiné avant la mer, qui lit l'écran. */
        _gunFx.Submerged(under);
        // et la passe sous-marine, qui n'existe que là : l'eau qui éteint, les rais qui descendent
        _under.Enabled = under;
        /* ET L OREILLE AVEC L OEIL : la bande d un demi-metre autour de la surface
           sert aussi au son, pour qu il ne bascule pas d un coup quand une vague
           passe devant l objectif. */
        /* L'OREILLE A SA PROPRE BANDE, ET BIEN PLUS ÉTROITE QUE L'ŒIL. L'image a
           besoin du demi-mètre : à cheval sur la surface, c'est le PIXEL qui
           décide, et il lui faut de quoi fondre. L'oreille n'a rien à fondre —
           une tête est dans l'eau ou elle est dehors, et l'épaisseur du passage
           est celle d'une oreille. Sur le demi-mètre de l'œil, une plongée à
           vitesse moyenne étalait l'étouffement sur une demi-seconde, ce qui
           s'entend comme une mollesse et non comme un passage (signalé). Dix
           centimètres : un dixième de seconde au même train. */
        double wet = Math.Clamp((seaY - ce.Y) / 0.10 + 0.5, 0, 1);
        _sound?.Underwater(wet);
        /* LE PASSAGE, ET NON L'ÉTAT. Ce qu'on entend en traversant la surface est
           un événement : il ne se joue qu'au franchissement, une fois, et à PLEIN
           VOLUME SANS FILTRE — c'est ce bruit-là qui dit qu'on a changé de monde,
           il ne doit pas être étouffé par celui dans lequel on entre.

           ET IL PART AVANT LE FRANCHISSEMENT, PARCE QUE L'ŒIL EST EN AVANCE. La
           passe sous-marine s'allume dès que la caméra est à un DEMI-MÈTRE de la
           surface (straddle > 0,01, quelques lignes plus haut) : l'eau envahit
           l'image bien avant que le point de vue ait traversé. Le clapotis, lui,
           attendait la traversée franche — d'où un son en retard sur ce qu'on
           voit, quoi qu'on fasse aux seuils. Signalé trois fois, et cherché trois
           fois au mauvais endroit : le retard n'était pas dans le son, il était
           dans l'écart entre les deux sens.

           On ne devine pas non plus une avance en centimètres, qui ne serait
           juste qu'à une seule vitesse de plongée : on regarde OÙ LA CAMÉRA SERA
           dans un dixième de seconde, et le bruit part quand ce point-là est sous
           l'eau. Lente ou vive, la plongée sonne au même moment de l'image. */
        double vy = frame > 1e-5 ? (ce.Y - _camWasY) / frame : 0;
        _camWasY = ce.Y;
        /* BORNÉE À HUIT MÈTRES PAR SECONDE : un changement de vue, une reprise de
           partie ou un glissement d'origine TÉLÉPORTENT la caméra, et la vitesse
           apparente part à des centaines de mètres par seconde — le clapotis
           sonnait alors en plein ciel (vu à la sonde : −841 m/s). Aucune plongée
           ne descend plus vite que huit. */
        double yBientot = ce.Y + Math.Max(-8, Math.Min(0, vy)) * 0.10;

        /* LE BRUIT PART QUAND L'IMAGE BASCULE — la MÊME condition, et non un seuil
           à soi qu'on rapprocherait par tâtonnements. On a essayé la surface, puis
           le milieu de la bande : chaque fois le son restait en retard (signalé
           trois fois), parce qu'un seuil choisi à part de celui de l'œil ne peut
           être juste qu'à une vitesse de plongée. La condition de l'image est
           reprise telle quelle, appliquée à la position que la caméra aura dans un
           dixième de seconde : les deux sens basculent alors ensemble par
           construction, et l'oreille est un souffle en avance sur l'œil, ce qui
           est le bon sens — l'eau s'entend arriver.

           Le retour, lui, garde une large marge : la bande de l'œil bascule au
           même endroit dans les deux sens, et une vague qui oscille autour ferait
           clapoter à chaque crête. */
        bool commeImage = seaY > yBientot || Math.Abs(seaY - yBientot) < 0.495;
        bool sous = _wasWet ? seaY - ce.Y > -0.80 : commeImage;
        if (sous != _wasWet)
        {
            _sound?.Crew(sous ? "eau-plonge" : "eau-sort",
                ce.ToCore(), 1, 0.3, true);
            _wasWet = sous;
        }
        if (under)
        {
            _under.SeaY = (float)seaY;
            var sd = _sky.Core.SunDir;
            _under.Sun = sd.ToGodot();
            _under.SunLight = (float)Math.Max(0.05, _sky.Core.SunIntensity);
            /* La couleur de l'eau profonde est celle de la MER (u_deep), pas celle
               du ciel : prise au zénith, tout sortait délavé. Elle s'éteint avec la
               lumière qui est dans l'eau, comme la mer vue de dessus. */
            double wl = Math.Max(0.05, _sky.Core.WaterLight);
            // un tiers de l'eau vue de dessus : sous la surface on regarde DANS elle,
            // et ce qu'on y voit est ce qui a traversé, non ce qu'elle renvoie
            var deep = GroundMaterials.Deep;
            _under.Water = new Color((float)(0.35 * deep.R * wl), (float)(0.35 * deep.G * wl), (float)(0.35 * deep.B * wl));
            _under.Time = (float)_t;
            _under.Straddle = straddle;
            /* LES GRAINS ET LE FLOU, du lieu : le fond sous l'œil (ils s'épaississent près de
               lui), l'eau dormante d'une rade (la même vase que la mer vue de dessus : u_silt
               sur l'abri), et le ressac qui les berce — l'orbite de la houle, éteinte avec la
               profondeur et derrière un môle. */
            var og = _sea.Core.Origin;
            double tx = og.X + ce.X, tz = og.Z + ce.Z;
            _under.BedY = _world != null ? (float)_world.HeightAt(tx, tz) : -1000f;
            double shelter = _world != null ? _world.Shelter(tx, tz) : 1;
            _under.Trouble = (float)Math.Clamp((1 - shelter) * SeaSilt(), 0, 1);
            _under.Surge = (float)(Math.Clamp(0.04 + 0.03 * _sea.Core.SeaState, 0, 0.35)
                * Math.Exp(-Math.Max(0, seaY - ce.Y) / 6.0) * shelter);
            // the mote grid repeats every 4096 cells: the origin is folded on that period so
            // a floating-origin shift leaves every mote where it was
            const double cell = 0.40, period = cell * 4096;
            _under.GridOrigin = new Vector2((float)((og.X % period + period) % period / cell),
                                            (float)((og.Z % period + period) % period / cell));
        }
        DropletTick(frame, under);
        FlareTick(under);
        _sky.PushGlobals(_cloud, _t);
        _sky.PushTo(_sea.Material);
        _sea.Material.SetShaderParameter(U.Sunlit, (float)_sky.Sunlit);
        // la pluie sur l'eau (pas la neige) ; et le gros temps de la mer seule, pour le remous
        _sea.Material.SetShaderParameter(U.Rain, (float)(_fall.Snow ? 0 : _fall.Amount));
        _sea.Material.SetShaderParameter(U.SeaStorm, (float)_sky.Core.SeaStorm);
        _sky.PushTo(_sea.FarMaterial);      // l horizon se noie dans le meme ciel
        _sky.SetCloud(_sea.Material, _cloud, _t);
        foreach (var m in _ship.Hazed) { _sky.PushTo(m); _sky.SetCloud(m, _cloud, _t); }
        if (_krakenNode.Visible) foreach (var m in _krakenNode.Hazed) { _sky.PushTo(m); _sky.SetCloud(m, _cloud, _t); }
        if (_whaleNode != null && _whaleNode.Visible) foreach (var m in _whaleNode.Hazed) { _sky.PushTo(m); _sky.SetCloud(m, _cloud, _t); }
        if (_serpentNode != null && _serpentNode.Visible) foreach (var m in _serpentNode.Hazed) { _sky.PushTo(m); _sky.SetCloud(m, _cloud, _t); }
        foreach (var m in _cordage.Hazed) _sky.PushTo(m);
        if (_dolphinNode != null) foreach (var m in _dolphinNode.Hazed) { _sky.PushTo(m); _sky.SetCloud(m, _cloud, _t); }
        foreach (var m in _splinters.Hazed) _sky.PushTo(m);
        foreach (var m in _gunFx.Hazed) _sky.PushTo(m);
        foreach (var m in _flotsam.Hazed) _sky.PushTo(m);
        foreach (var s in _others)
            foreach (var m in s.Hazed) { _sky.PushTo(m); _sky.SetCloud(m, _cloud, _t); }
        // l'embrun est aussi clair que ce qui l'éclaire : l'horizon, qui porte l'heure
        _sky.PushTo(_spray.Material);
        _spray.Step(frame);
        if (_spray.Pool.Count > _sprayMax) _sprayMax = _spray.Pool.Count;
        /* LES RIDES — à la mer ET au fond. C'est le clapot qui fait les nœuds de
           lumière sur le sable, pas la houle ; les deux shaders lisent le même
           champ (ride.gdshaderinc), il leur faut donc la même force et le même
           vent, sous peine d'une mer qui luit d'une ride et d'un fond qui en
           dessine une autre. */
        float ripple = (float)Math.Min(2.6, 0.40 + _sea.Core.WindSpeed * 0.105);
        _sea.Material?.SetShaderParameter(U.Ripple, ripple);
        _land?.Ground.SetShaderParameter(U.Ripple, ripple);
        _fishNode?.Material?.SetShaderParameter(U.Ripple, ripple);
        ShipNode.Caustic?.SetShaderParameter(U.Ripple, ripple);
        _mist?.Material.SetShaderParameter(U.Ripple, ripple);
        var wv = _sea.Core.WindVec;
        double ws = wv.LengthXZ;
        if (ws > 1e-4)
        {
            var wu = new Vector2((float)(wv.X / ws), (float)(wv.Z / ws));
            _sea.Material?.SetShaderParameter(U.Wind, wu);
            _land?.Ground.SetShaderParameter(U.Wind, wu);
            _fishNode?.Material?.SetShaderParameter(U.Wind, wu);
            ShipNode.Caustic?.SetShaderParameter(U.Wind, wu);
            _mist?.Material.SetShaderParameter(U.Wind, wu);
        }

        /* LE TEMPS OU LE COMPTOIR EST FERME, compte ICI et non au tour du HUD :
           celui-ci ne bat que toutes les 0,15 s et jette son reste, ce qui ferait
           deriver l horloge des cours de quelques pour cent par jour. */
        if (!MarketOpen) _mkShut += frame;

        _hudAcc += frame;
        if (_hudAcc > 0.15) { _hudAcc = 0; _hudBeat = true; UpdateInfo(); AmbianceTick(); MarketTick(); JournalPortTick(); StowTick(); FleetTick(); }
        TrimTick();
        HitsTick();
        GunSideTick();
        HudTick();

        TickCapture();
        _ftWatch.Stop();
        _allocProc += GC.GetAllocatedBytesForCurrentThread() - ap0;
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
}
