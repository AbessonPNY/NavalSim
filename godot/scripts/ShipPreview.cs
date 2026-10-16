using Godot;
using System;
using System.Collections.Generic;
using NavalSim.Core;

namespace NavalSim;

/// <summary>
/// L'APERÇU D'UN NAVIRE (demandé : « charger les bateaux en aperçu sur fond noir en mode
/// nuit, que je voie le rendu du jeu sans lancer une partie » — <c>-- --testship 1 --ship 8</c>).
///
/// Le navire seul, bâti par le MÊME ShipNode qu'en jeu — modèle, gréement, voiles serrées
/// dessinées, fanaux, blocs lumineux, lampes de chambre, pavillon — sous le même ciel de
/// nuit (SkyNode, à 23 h), mais sans monde : ni mer, ni terre, ni flotte. Rien à attendre :
/// on juge un modèle qu'on vient d'exporter en quelques secondes.
///
/// Le ciel garde son DÔME pour l'ambiante et les reflets — c'est lui qui éclaire le bordé
/// la nuit, et sans lui le navire ne serait pas celui du jeu ; seul le FOND est noir.
/// Le navire est posé à sa flottaison (le solveur, sur une mer plate), donc la ligne d'eau
/// est à y = 0, comme le plan de grille qu'on peut afficher (G).
///
/// Souris : bouton gauche tenu pour tourner, molette pour s'approcher, bouton droit tenu
/// pour monter ou descendre l'œil. ← → : navire précédent / suivant ; V : voiles
/// établies ou serrées ; J : nuit ou jour ; F : fanaux ; G : la ligne d'eau ; Échap : quitter.
/// </summary>
public partial class ShipPreview : Node3D
{
    /// <summary>L'aperçu est-il demandé sur la ligne de commande ?</summary>
    public static bool Wanted()
    {
        var args = OS.GetCmdlineUserArgs();
        for (int i = 0; i < args.Length - 1; i++)
            if (args[i] == "--testship" && args[i + 1] != "0") return true;
        return false;
    }

    const double NightHour = 23, DayHour = 11;

    SkyNode _sky = null!;
    Camera3D _cam = null!;
    Label _info = null!;
    MeshInstance3D _waterline = null!;
    ShipNode? _ship;
    readonly Ocean _sea = new();
    List<string> _paths = new();
    Settings _settings = null!;
    int _index;
    double _t, _set, _wantSet;
    bool _night = true;

    // l'œil : autour du navire, à cette distance, ce cap et cette hauteur
    float _yaw = 2.3f, _pitch = 0.18f, _dist = 60, _lift;
    bool _turning, _lifting;

    public override void _Ready()
    {
        _settings = Settings.Load();
        ReadNightSettings();
        _sea.SetSeaState(0, 0);         // une mer plate : la flottaison sans la houle

        _sky = new SkyNode();
        AddChild(_sky);
        _sky.DayRate = 0;
        SetHour(NightHour);
        /* LE FOND NOIR, et rien d'autre : l'ambiante et les reflets restent ceux du ciel
           (AmbientSource.Sky lit la Sky même quand le fond est une couleur). */
        _sky.Env.BackgroundMode = Godot.Environment.BGMode.Color;
        _sky.Env.BackgroundColor = Colors.Black;
        // la lueur de nuit, réglée comme en jeu (ShipDemo.Settings, ApplySettings)
        var env = _sky.Env;
        env.GlowHdrThreshold = 0.319f;
        env.GlowHdrScale = 0.355f;
        env.GlowBloom = 0;
        env.GlowBlendMode = Godot.Environment.GlowBlendModeEnum.Additive;
        env.GlowNormalized = true;
        for (int i = 0; i < 7; i++) env.SetGlowLevel(i, i == 2 || i == 3 ? 1 : 0);
        env.GlowIntensity = _settings.GlowStrength;

        _cam = new Camera3D { Current = true, Fov = 50, Near = 0.1f, Far = 4000 };
        AddChild(_cam);

        _waterline = new MeshInstance3D
        {
            Mesh = new PlaneMesh { Size = new Vector2(400, 400), SubdivideWidth = 0, SubdivideDepth = 0 },
            MaterialOverride = new StandardMaterial3D
            {
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                AlbedoColor = new Color(0.05f, 0.09f, 0.13f, 0.55f),
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                CullMode = BaseMaterial3D.CullModeEnum.Disabled
            },
            Visible = false
        };
        AddChild(_waterline);

        var hud = new CanvasLayer();
        AddChild(hud);
        _info = new Label { Position = new Vector2(16, 12) };
        _info.AddThemeFontSizeOverride("font_size", 18);
        _info.AddThemeColorOverride("font_color", new Color(0.92f, 0.86f, 0.70f));
        hud.AddChild(_info);

        _paths = ShipLibrary.Discover();
        int want = 0;
        var args = OS.GetCmdlineUserArgs();
        for (int i = 0; i < args.Length - 1; i++)
            switch (args[i])
            {
                case "--ship": want = args[i + 1].ToInt(); break;
                case "--heure": SetHour(args[i + 1].ToFloat()); _night = _sky.Core.Night > 0.3; break;
                case "--sails": _wantSet = _set = args[i + 1] == "1" ? 1 : 0; break;
            }
        Show(want);
    }

    /// <summary>La force des fenêtres et le plancher de la toile, lus dans settings.json comme en jeu.</summary>
    static void ReadNightSettings()
    {
        try
        {
            string path = System.IO.Path.Combine(Assets.Root, "settings.json");
            using var doc = System.Text.Json.JsonDocument.Parse(System.IO.File.ReadAllText(path));
            if (!doc.RootElement.TryGetProperty("night", out var nt)) return;
            if (nt.Opt("glow") is double gl) ShipNode.NightGlowGain = Math.Max(0, gl);
            if (nt.Opt("canvas") is double cv) ShipNode.CanvasFloor = Math.Clamp(cv, 0, 1);
        }
        catch (Exception e) { GD.PushWarning($"[aperçu] settings.json : {e.Message}"); }
    }

    void SetHour(double h)
    {
        _sky.Core.SetTimeOfDay(h, _sky.Latitude);
        _sky.Apply();
    }

    void Show(int i)
    {
        if (_paths.Count == 0) { _info.Text = "aucune fiche trouvée"; return; }
        _index = (i % _paths.Count + _paths.Count) % _paths.Count;
        var spec = ShipLibrary.Load(_paths[_index]);
        if (spec == null) { _info.Text = $"fiche illisible : {_paths[_index]}"; return; }

        if (_ship != null) { RemoveChild(_ship); _ship.QueueFree(); }
        _ship = new ShipNode
        {
            LanternShadows = _settings.LanternShadows,
            WithMastLantern = _settings.MastLantern
        };
        AddChild(_ship);
        _ship.Build(spec);
        _ship.Sea = _sea;
        _ship.Ctrl.Sheet = 0.6;
        double y = _ship.Physics.Settle(_sea, _ship.Ctrl);
        _ship.SyncTransform();
        if (_ship.HasFlag) _ship.ShowColours(true, true);
        _dist = (float)spec.L * 1.8f;
        _lift = 0;
        GD.Print(FormattableString.Invariant($"[aperçu] {_index} : {spec.Name} ({_paths[_index]}), assise y = {y:F2} m"));
        UpdateInfo();
    }

    void UpdateInfo()
    {
        if (_ship == null) return;
        _info.Text = $"{_index} · {_ship.Spec.Name}   —   {(_night ? "nuit" : "jour")}, voiles {(_wantSet > 0 ? "établies" : "serrées")}"
                   + $"{(_ship.LanternsOrdered ? ", fanaux sur ordre" : "")}\n"
                   + "← → navire   V voiles   J nuit/jour   F fanaux   G ligne d'eau   souris : tourner, molette, clic droit : hauteur   Échap";
    }

    public override void _Process(double delta)
    {
        if (_ship == null) return;
        _t += delta;
        // la toile, au même train qu'en jeu (ShipPhysics.SetRate : un tiers par seconde)
        _set += Math.Clamp(_wantSet - _set, -delta / 3, delta / 3);
        _ship.SetTrim(_ship.Ctrl.Sheet, 1, _set, false, _t, 0.5 * _set);
        _ship.StreamFlags(_t);

        // l'œil, autour du centre du navire
        var spec = _ship.Spec;
        var target = new Vector3(0, (float)(0.25 * spec.L * 0.5) + _lift, 0);
        var off = new Vector3(Mathf.Sin(_yaw) * Mathf.Cos(_pitch), Mathf.Sin(_pitch), Mathf.Cos(_yaw) * Mathf.Cos(_pitch)) * _dist;
        _cam.Position = target + off;
        _cam.LookAt(target, Vector3.Up);

        _ship.SetLantern(_sky.Core.Night, _t, _cam.GlobalPosition, _sky.Core);
        _sky.Env.GlowEnabled = _settings.Glow && _sky.Core.Night > 0.02;
        _sky.PushGlobals(0, _t);
        foreach (var m in _ship.Hazed) { _sky.PushTo(m); _sky.SetCloud(m, 0, _t); }
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (e is InputEventMouseButton mb)
        {
            if (mb.ButtonIndex == MouseButton.Left) _turning = mb.Pressed;
            if (mb.ButtonIndex == MouseButton.Right) _lifting = mb.Pressed;
            if (mb.Pressed && mb.ButtonIndex == MouseButton.WheelUp) _dist = Math.Max(2, _dist * 0.9f);
            if (mb.Pressed && mb.ButtonIndex == MouseButton.WheelDown) _dist = Math.Min(2000, _dist * 1.1f);
            return;
        }
        if (e is InputEventMouseMotion mm)
        {
            if (_turning)
            {
                _yaw -= mm.Relative.X * 0.006f;
                _pitch = Math.Clamp(_pitch + mm.Relative.Y * 0.006f, -1.2f, 1.5f);
            }
            if (_lifting) _lift += mm.Relative.Y * 0.02f * _dist / 30;
            return;
        }
        if (e is not InputEventKey { Pressed: true, Echo: false } k) return;
        // AZERTY : la touche physique d'abord (comme en jeu), la lettre en repli
        var key = k.PhysicalKeycode != Key.None ? k.PhysicalKeycode : k.Keycode;
        switch (key)
        {
            case Key.Left: Show(_index - 1); break;
            case Key.Right: Show(_index + 1); break;
            case Key.V: _wantSet = _wantSet > 0 ? 0 : 1; break;
            case Key.J:
                _night = !_night;
                SetHour(_night ? NightHour : DayHour);
                break;
            case Key.F:
                if (_ship != null) _ship.OrderLanterns(!_ship.LanternsOrdered, _t, _sky.Core.Night);
                break;
            case Key.G: _waterline.Visible = !_waterline.Visible; break;
            case Key.Escape: GetTree().Quit(); break;
            default: return;
        }
        UpdateInfo();
    }
}
