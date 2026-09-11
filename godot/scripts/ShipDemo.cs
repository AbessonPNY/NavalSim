using Godot;
using System;
using System.Collections.Generic;
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
    ShipNode _ship = null!;
    Camera3D _cam = null!;
    Label _info = null!;

    double _t;
    double _force = 4, _windDeg = 210;
    float _orbit = 0.9f, _pitch = 0.16f, _dist = 55f;
    bool _dragging, _follow = true;
    double _hudAcc;
    // la machine imposee en ligne de commande ne doit pas etre effacee par la
    // lecture du clavier, qui la laisse ou elle est faute de touche pressee
    bool _drive;

    List<string> _paths = new();
    int _index;

    /* LE SOUS-PAS NE DÉPASSE JAMAIS 1/15 s, et le compte en découle.
       C'est la règle énoncée plutôt que réglée : quatre sous-pas à soixante
       images, davantage si l'image est lente, ce qui redonne exactement le
       comportement d'origine à vitesse normale. */
    const int MinSub = 4;
    const double MaxSubDt = 1.0 / 15;

    public override void _Ready()
    {
        BuildScene();

        _sea = new OceanNode();
        AddChild(_sea);
        _sea.Core.SetSeaState(_force, _windDeg);

        _paths = ShipLibrary.Discover();
        GD.Print($"{_paths.Count} fiche(s) lue(s) dans {ShipLibrary.Folder}");
        Launch(0);
        SetupCapture();
    }

    void BuildScene()
    {
        var env = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Sky,
            Sky = new Sky { SkyMaterial = new ProceduralSkyMaterial() },
            AmbientLightSource = Godot.Environment.AmbientSource.Sky,
            TonemapMode = Godot.Environment.ToneMapper.Aces
        };
        AddChild(new WorldEnvironment { Environment = env });

        var sun = new DirectionalLight3D { LightEnergy = 1.15f, ShadowEnabled = true };
        sun.RotationDegrees = new Vector3(-34, 122, 0);
        AddChild(sun);

        _cam = new Camera3D { Current = true, Fov = 55, Far = 9000 };
        AddChild(_cam);

        var layer = new CanvasLayer();
        AddChild(layer);
        _info = new Label { Position = new Vector2(18, 14) };
        _info.AddThemeFontSizeOverride("font_size", 15);
        _info.AddThemeColorOverride("font_color", new Color(0.94f, 0.96f, 0.98f));
        _info.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.85f));
        _info.AddThemeConstantOverride("outline_size", 5);
        layer.AddChild(_info);
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
        _ship.Build(spec);
        _ship.Ctrl.SailsSet = false;
        _ship.Ctrl.Sheet = 0.6;

        double y = _ship.Physics.Settle(_sea.Core, _ship.Ctrl);
        _ship.SyncTransform();
        GD.Print($"{spec.Name} : assise à y = {y:F3} m, tirant {_ship.Physics.Draft:F2} m, "
               + $"immersion {_ship.Physics.SubmergedFrac * 100:F1} %");

        _dist = (float)spec.L * 1.8f;
        UpdateInfo();
    }

    public override void _Process(double delta)
    {
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
        for (int s = 0; s < sub; s++)
        {
            _ship.Physics.Step(dt, _sea.Core, _ship.Ctrl, t);
            t += dt;
        }
        _ship.SyncTransform();

        /* L'ORIGINE FLOTTANTE. Au-delà de REBASE_RADIUS le monde entier glisse
           sous la flotte, et TOUT CE QUI TIENT UNE POSITION doit se décaler dans
           la MÊME image — sans quoi il se retrouve en désaccord avec la mer
           d'exactement ce décalage. Ici : la coque et la mer. Le champ d'écume et
           la caméra fixe s'ajouteront à cette liste, et c'est la liste sur
           laquelle ce projet a déjà oublié quelque chose deux fois. */
        var b = _ship.Physics.Body;
        if (Math.Abs(b.Pos.X) > Config.RebaseRadius || Math.Abs(b.Pos.Z) > Config.RebaseRadius)
        {
            double dx = -b.Pos.X, dz = -b.Pos.Z;
            _sea.Core.Rebase(-dx, -dz);
            b.Pos = new Vec3d(b.Pos.X + dx, b.Pos.Y, b.Pos.Z + dz);
            _ship.SyncTransform();
            GD.Print($"recentrage : origine désormais ({_sea.Core.Origin.X:F0}, {_sea.Core.Origin.Z:F0}) m");
        }

        UpdateCamera(frame);
        _sea.UpdateFrom(_cam.GlobalPosition, _t);

        _hudAcc += frame;
        if (_hudAcc > 0.15) { _hudAcc = 0; UpdateInfo(); }

        TickCapture();
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
            $"W S machine   A D barre   Q E écoutes   V voiles\n" +
            $"↑↓ force   ←→ vent   N navire   F suivre   Échap quitter";
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
                case Key.V: _ship.Ctrl.SailsSet = !_ship.Ctrl.SailsSet; break;
                case Key.N: Launch(_index + 1); break;
                case Key.F: _follow = !_follow; break;
                case Key.Escape: GetTree().Quit(); break;
            }
        }
        if (e is InputEventMouseButton mb)
        {
            if (mb.ButtonIndex == MouseButton.Left) _dragging = mb.Pressed;
            else if (mb.ButtonIndex == MouseButton.WheelUp) _dist = Mathf.Max(10f, _dist * 0.9f);
            else if (mb.ButtonIndex == MouseButton.WheelDown) _dist = Mathf.Min(900f, _dist * 1.11f);
        }
        if (e is InputEventMouseMotion mm && _dragging)
        {
            _orbit -= mm.Relative.X * 0.008f;
            _pitch = Mathf.Clamp(_pitch + mm.Relative.Y * 0.004f, 0.01f, 1.3f);
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
                case "--pitch": _pitch = args[i + 1].ToFloat(); break;
                case "--dist": _dist = args[i + 1].ToFloat(); break;
                case "--orbit": _orbit = args[i + 1].ToFloat(); break;
                case "--ship": Launch(args[i + 1].ToInt()); break;
                case "--sails": _ship.Ctrl.SailsSet = args[i + 1] == "1"; break;
                // la machine en ligne de commande : une capture « en route » ne
                // peut pas dependre du clavier, et un banc non plus
                case "--throttle": _ship.Ctrl.Throttle = args[i + 1].ToFloat(); _drive = true; break;
            }
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
