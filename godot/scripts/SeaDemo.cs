using Godot;
using System;
using System.Collections.Generic;
using NavalSim.Core;

namespace NavalSim;

/// <summary>
/// LA PARITÉ CPU/GPU, RENDUE VISIBLE.
///
/// Le banc de parité compare le C# au JavaScript et les trouve d'accord à 10⁻¹⁶.
/// Il ne dit RIEN du shader — qui est une seconde transcription de la même
/// arithmétique, dans un autre langage, et que rien dans le code ne lie au
/// noyau. C'est très exactement l'endroit où une divergence serait silencieuse.
///
/// D'où les flotteurs. Chacun est posé à la hauteur que l'ÉCHANTILLONNEUR CPU
/// rend, sur une mer que le GPU dessine. S'ils s'accordent, les flotteurs
/// collent à la surface et suivent la houle. S'ils divergent, ils planent ou
/// s'enfoncent, et l'écart se voit en mètres sans qu'on ait rien à mesurer.
///
/// Ils sont inclinés sur la NORMALE rendue par le même appel, ce qui vérifie la
/// seconde moitié : une hauteur juste avec une pente fausse ferait flotter la
/// coque à plat sur une mer en pente.
/// </summary>
public partial class SeaDemo : Node3D
{
    OceanNode _sea = null!;
    Camera3D _cam = null!;
    Label _info = null!;
    readonly List<Node3D> _floats = new();

    double _t;
    double _force = 4, _windDeg = 210;
    float _orbit = 0.4f, _pitch = 0.18f, _dist = 60f;
    bool _dragging;
    double _eyeAcc;

    const int GRID = 5;
    const float SPACING = 14f;

    public override void _Ready()
    {
        BuildScene();

        _sea = new OceanNode();
        AddChild(_sea);
        _sea.Core.SetSeaState(_force, _windDeg);

        BuildFloats();
        MeasureHs();
        UpdateInfo();
        SetupCapture();
    }

    void BuildScene()
    {
        var env = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Sky,
            Sky = new Godot.Sky { SkyMaterial = new ProceduralSkyMaterial() },
            AmbientLightSource = Godot.Environment.AmbientSource.Sky,
            TonemapMode = Godot.Environment.ToneMapper.Aces,
            SsrEnabled = false
        };
        AddChild(new WorldEnvironment { Environment = env });

        var sun = new DirectionalLight3D { LightEnergy = 1.1f };
        sun.RotationDegrees = new Vector3(-32, 128, 0);
        AddChild(sun);

        _cam = new Camera3D { Current = true, Fov = 55, Far = 8000 };
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
    /// Des flotteurs plats et LARGES plutôt que des sphères : une sphère posée au
    /// bon endroit a l'air posée au bon endroit quelle que soit la pente, alors
    /// qu'un disque incliné trahit tout de suite une normale fausse. C'est le même
    /// raisonnement que la silhouette d'une voile — ce que l'œil lit d'abord est
    /// le contour, pas l'ombrage.
    /// </summary>
    void BuildFloats()
    {
        var disc = new CylinderMesh
        {
            TopRadius = 1.5f, BottomRadius = 1.5f, Height = 0.28f,
            RadialSegments = 16, Rings = 1
        };
        var mat = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.92f, 0.32f, 0.16f),
            Roughness = 0.55f
        };
        var post = new CylinderMesh { TopRadius = 0.16f, BottomRadius = 0.16f, Height = 3.2f, RadialSegments = 8 };
        var postMat = new StandardMaterial3D { AlbedoColor = new Color(0.95f, 0.95f, 0.92f) };

        for (int i = 0; i < GRID * GRID; i++)
        {
            var node = new Node3D();
            node.AddChild(new MeshInstance3D { Mesh = disc, MaterialOverride = mat });
            // un mât court, pour que l'inclinaison se lise de loin
            node.AddChild(new MeshInstance3D
            {
                Mesh = post, MaterialOverride = postMat,
                Position = new Vector3(0, 1.6f, 0)
            });
            AddChild(node);
            _floats.Add(node);
        }
    }

    /* --- LA CAPTURE EN LIGNE DE COMMANDE ---
       `--capture <fichier>` laisse la scène s'installer, écrit une image et
       rend la main. C'est le pendant de la touche I du projet JavaScript, et
       elle existe pour la même raison : une page — ici une fenêtre — ne se
       vérifie pas en la décrivant.

       Le délai n'est pas de la superstition. Un shader se compile à sa première
       utilisation, le ciel procédural se construit, et les flotteurs ne sont
       posés qu'à la première image : capturer tout de suite donnerait une image
       vide ou à moitié bâtie, et un outil qui ment sur ce qu'il vient d'écrire
       est pire que pas d'outil. */
    string _capturePath = "";
    int _captureIn = -1;

    void SetupCapture()
    {
        var args = OS.GetCmdlineUserArgs();
        for (int i = 0; i < args.Length - 1; i++)
        {
            switch (args[i])
            {
                case "--capture": _capturePath = args[i + 1]; _captureIn = 45; break;
                case "--force": _force = args[i + 1].ToFloat(); Restate(); break;
                case "--pitch": _pitch = args[i + 1].ToFloat(); break;
                case "--dist": _dist = args[i + 1].ToFloat(); break;
                case "--orbit": _orbit = args[i + 1].ToFloat(); break;
            }
        }
    }

    void TickCapture()
    {
        if (_captureIn < 0) return;
        if (--_captureIn > 0) return;

        var img = GetViewport().GetTexture().GetImage();
        if (img == null || img.GetWidth() < 8 || img.GetHeight() < 8)
        {
            GD.PushError("capture refusée : image vide");
            GetTree().Quit(1);
            return;
        }
        Error err = img.SavePng(_capturePath);
        GD.Print(err == Error.Ok
            ? $"capture écrite : {_capturePath}  ({img.GetWidth()}x{img.GetHeight()})"
            : $"capture ratée : {err}");
        GetTree().Quit(err == Error.Ok ? 0 : 1);
    }

    public override void _Process(double delta)
    {
        _t += delta;

        // --- la caméra ---
        if (!_dragging) _orbit += (float)delta * 0.05f;
        float h = _dist * Mathf.Sin(_pitch);
        float r = _dist * Mathf.Cos(_pitch);
        var eye = new Vector3(Mathf.Sin(_orbit) * r, Mathf.Max(1.5f, h), Mathf.Cos(_orbit) * r);
        _cam.Position = eye;
        _cam.LookAt(new Vector3(0, 1, 0), Vector3.Up);

        _sea.UpdateFrom(eye, _t);

        /* --- LES FLOTTEURS, posés par le CPU sur la mer du GPU ---
           Chacun demande sa hauteur ET sa normale au même appel : c'est
           exactement ce que fera la grille de sondes du solveur, et c'est donc
           exactement l'accord qu'il faut vérifier. */
        int n = 0;
        for (int ix = 0; ix < GRID; ix++)
            for (int iz = 0; iz < GRID; iz++, n++)
            {
                float x = (ix - (GRID - 1) * 0.5f) * SPACING;
                float z = (iz - (GRID - 1) * 0.5f) * SPACING;
                double y = _sea.Core.Sample(x, z, _t, out Vec3d nrm);

                var node = _floats[n];
                node.Position = new Vector3(x, (float)y, z);

                // incliner sur la pente réelle de l'eau
                var up = new Vector3((float)nrm.X, (float)nrm.Y, (float)nrm.Z).Normalized();
                if (up.LengthSquared() > 0.5f && Mathf.Abs(up.Dot(Vector3.Up)) < 0.9999f)
                {
                    var axis = Vector3.Up.Cross(up).Normalized();
                    float ang = Mathf.Acos(Mathf.Clamp(Vector3.Up.Dot(up), -1f, 1f));
                    node.Basis = new Basis(axis, ang);
                }
                else node.Basis = Basis.Identity;
            }

        // la télémétrie coûte une chaîne par image : cinq fois par seconde suffit
        _eyeAcc += delta;
        if (_eyeAcc > 0.2) { _eyeAcc = 0; UpdateInfo(); }

        TickCapture();
    }

    double _hsMeasured, _hsTarget;

    /// <summary>
    /// LA HAUTEUR SIGNIFICATIVE, MESURÉE SUR LA SURFACE ET NON DÉDUITE DU SPECTRE.
    ///
    /// La somme des amplitudes ne la donne pas, et s'en servir m'a fait afficher
    /// 5,80 m là où la table Beaufort en promet 4,73 : les amplitudes portent la
    /// compensation d'aiguisage — le facteur (1 + 0,42·(sharp−1)) rendu pour que
    /// le profil piqué retrouve la bonne valeur efficace — donc elles sont plus
    /// grandes que la mer qui en sort.
    ///
    /// Hs vaut quatre fois l'écart-type de l'élévation, alors on échantillonne
    /// l'élévation. C'est en prime la vérification qui compte : elle dit si le
    /// spectre rend réellement ce que la console annonce, ce qu'aucun calcul sur
    /// les amplitudes ne peut dire une fois le profil déformé.
    /// </summary>
    void MeasureHs()
    {
        var o = _sea.Core;
        const int N = 40;
        const double SPAN = 600.0;    // large devant la plus longue composante
        double sum = 0, sum2 = 0;
        int count = 0;
        for (int i = 0; i < N; i++)
            for (int j = 0; j < N; j++)
            {
                double x = (i / (double)(N - 1) - 0.5) * SPAN;
                double z = (j / (double)(N - 1) - 0.5) * SPAN;
                double y = o.Sample(x, z, _t);
                sum += y; sum2 += y * y; count++;
            }
        double mean = sum / count;
        double var = Math.Max(0, sum2 / count - mean * mean);
        _hsMeasured = 4 * Math.Sqrt(var);

        int lo = Mathf.Clamp((int)Math.Floor(_force), 0, 9);
        int hi = Mathf.Clamp((int)Math.Ceiling(_force), 0, 9);
        _hsTarget = (Config.Beaufort[lo].Hs
                  + (Config.Beaufort[hi].Hs - Config.Beaufort[lo].Hs) * (_force - lo)) * o.Swell;
    }

    void UpdateInfo()
    {
        var o = _sea.Core;
        double hs = _hsMeasured;
        int b = Mathf.Clamp((int)Mathf.Round((float)_force), 0, 9);
        _info.Text =
            $"force {_force:F1}  ·  {Config.Beaufort[b].Name}\n" +
            $"vent          {_windDeg,5:F0}°   {o.WindSpeed,5:F1} m/s\n" +
            $"creux         {o.Swell,5:F2}\n" +
            $"Hs            {hs,5:F2} m\n" +
            $"aiguisage     {o.Sharp,5:F3}\n" +
            $"origine       {o.Origin.X,8:F0}, {o.Origin.Z,8:F0} m\n" +
            $"\n" +
            $"Les flotteurs sont posés par l'ÉCHANTILLONNEUR CPU\n" +
            $"sur une mer dessinée par le GPU. S'ils collent à\n" +
            $"la surface, les deux calculateurs sont d'accord.\n" +
            $"\n" +
            $"↑ ↓  force      ← →  vent      R  recentrer de 1500 m\n" +
            $"glisser  tourner      molette  approcher      Échap  quitter";
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (e is InputEventKey k && k.Pressed && !k.Echo)
        {
            switch (k.Keycode)
            {
                case Key.Up: _force = Mathf.Min(9.9, _force + 0.5); Restate(); break;
                case Key.Down: _force = Mathf.Max(0, _force - 0.5); Restate(); break;
                case Key.Left: _windDeg = (_windDeg - 15 + 360) % 360; Restate(); break;
                case Key.Right: _windDeg = (_windDeg + 15) % 360; Restate(); break;

                /* R : LE RECENTRAGE, à la demande.
                   C'est l'invariant le plus dur du projet, et c'est aussi celui
                   qu'on ne peut pas voir en regardant une image fixe : la mer
                   doit être parfaitement continue à l'instant où le monde glisse
                   de 1 500 m sous la flotte. Si la phase n'était pas rendue, la
                   houle sauterait de côté ici, franchement. */
                case Key.R: _sea.Core.Rebase(1500, 0); UpdateInfo(); break;

                case Key.Escape: GetTree().Quit(); break;
            }
        }
        if (e is InputEventMouseButton mb)
        {
            if (mb.ButtonIndex == MouseButton.Left) _dragging = mb.Pressed;
            else if (mb.ButtonIndex == MouseButton.WheelUp) _dist = Mathf.Max(8f, _dist * 0.9f);
            else if (mb.ButtonIndex == MouseButton.WheelDown) _dist = Mathf.Min(600f, _dist * 1.11f);
        }
        if (e is InputEventMouseMotion mm && _dragging)
        {
            _orbit -= mm.Relative.X * 0.008f;
            _pitch = Mathf.Clamp(_pitch + mm.Relative.Y * 0.004f, 0.01f, 1.3f);
        }
    }

    /// <summary>
    /// Repose le spectre. Il faut lui rendre l'heure courante : la correction de
    /// bande est posée pour que la phase soit inchangée à l'origine locale, et
    /// elle se calcule contre l'horloge — reconstruire avec un t faux ferait
    /// sauter la mer d'un radian entier.
    /// </summary>
    void Restate()
    {
        _sea.Core.Time = _t;
        _sea.Core.SetSeaState(_force, _windDeg);
        MeasureHs();
        UpdateInfo();
    }
}
