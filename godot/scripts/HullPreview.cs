using Godot;
using System.Collections.Generic;
using NavalSim.Core;

namespace NavalSim;

/// <summary>
/// Le plan de formes, a l'oeil.
///
/// Rien ici ne flotte : ni houle, ni sondes, ni solveur. C'est deliberement la
/// premiere chose qu'on regarde, parce que c'est la premiere chose qui devait
/// traverser -- la coque dessinee par le HullLines porte en C# est-elle la coque
/// que le JavaScript dessinait ? Le banc de parite repond au nombre ; ceci y
/// repond a l'oeil, et les deux reponses valent mieux qu'une.
///
/// La ligne de flottaison est posee a y = 0 parce que c'est la que le zero du
/// plan de formes se trouve. Elle ne dit PAS ou la coque flottera : ca, c'est
/// l'affaire du solveur et de la fraction immergee, qui n'est pas encore portee.
/// </summary>
public partial class HullPreview : Node3D
{
    List<string> _paths = new();
    int _index;
    ShipSpec? _spec;

    MeshInstance3D _hull = null!;
    Node3D _yaw = null!;
    Camera3D _cam = null!;
    Label _info = null!;

    float _orbit = 0.6f, _pitch = 0.35f, _dist = 1f;
    bool _dragging;

    public override void _Ready()
    {
        BuildScene();

        _paths = ShipLibrary.Discover();
        GD.Print($"{_paths.Count} fiche(s) lue(s) dans {ShipLibrary.Folder}");
        if (_paths.Count == 0)
        {
            _info.Text = "Aucune fiche trouvée dans " + ShipLibrary.Folder;
            return;
        }
        Show(0);
    }

    void BuildScene()
    {
        // --- lumiere et ciel, au plus simple : on regarde une FORME, et un
        //     eclairage elabore masquerait justement ce qu'on vient verifier ---
        var env = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Color,
            BackgroundColor = new Color(0.16f, 0.20f, 0.25f),
            AmbientLightSource = Godot.Environment.AmbientSource.Color,
            AmbientLightColor = new Color(0.45f, 0.52f, 0.60f),
            AmbientLightEnergy = 1.0f
        };
        AddChild(new WorldEnvironment { Environment = env });

        var sun = new DirectionalLight3D { ShadowEnabled = true };
        sun.RotationDegrees = new Vector3(-42, -128, 0);
        AddChild(sun);

        // --- la coque ---
        _hull = new MeshInstance3D();
        AddChild(_hull);

        // --- la flottaison, en repere seulement ---
        var water = new MeshInstance3D
        {
            Mesh = new PlaneMesh { Size = new Vector2(400, 400) },
            MaterialOverride = new StandardMaterial3D
            {
                AlbedoColor = new Color(0.20f, 0.42f, 0.52f, 0.55f),
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                CullMode = BaseMaterial3D.CullModeEnum.Disabled
            }
        };
        AddChild(water);

        // --- la camera, sur un bras orientable ---
        _yaw = new Node3D();
        AddChild(_yaw);
        _cam = new Camera3D { Current = true, Fov = 50 };
        _yaw.AddChild(_cam);

        // --- le bandeau ---
        var layer = new CanvasLayer();
        AddChild(layer);
        _info = new Label
        {
            Position = new Vector2(18, 14),
            Theme = new Theme()
        };
        _info.AddThemeFontSizeOverride("font_size", 15);
        _info.AddThemeColorOverride("font_color", new Color(0.92f, 0.94f, 0.96f));
        _info.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.8f));
        _info.AddThemeConstantOverride("outline_size", 5);
        layer.AddChild(_info);
    }

    void Show(int i)
    {
        _index = (i % _paths.Count + _paths.Count) % _paths.Count;
        _spec = ShipLibrary.Load(_paths[_index]);
        if (_spec == null) return;

        var lines = new HullLines(_spec);
        HullMesh hm = lines.BuildGeometry();
        _hull.Mesh = ToArrayMesh(hm);
        _hull.MaterialOverride = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.30f, 0.35f, 0.42f),
            Roughness = 0.75f,
            /* La coque est batie en une seule nappe, bordee puis refermee par
               deux culs -- son interieur est donc visible sous la flottaison.
               On dessine les deux faces plutot que de faire semblant qu'elle est
               pleine : c'est une VERIFICATION, pas une mise en scene. */
            CullMode = BaseMaterial3D.CullModeEnum.Disabled
        };

        _dist = (float)_spec.L * 1.5f;
        UpdateInfo();
    }

    /// <summary>
    /// Les donnees pures du noyau deviennent un maillage Godot. C'est TOUT ce que
    /// la couche moteur a le droit de faire de HullLines -- la forme est decidee
    /// dans le noyau, ici on ne fait que la porter au GPU.
    /// </summary>
    static ArrayMesh ToArrayMesh(in HullMesh hm)
    {
        var verts = new Vector3[hm.Positions.Length / 3];
        for (int i = 0; i < verts.Length; i++)
            verts[i] = new Vector3(hm.Positions[i * 3], hm.Positions[i * 3 + 1], hm.Positions[i * 3 + 2]);

        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        foreach (int idx in hm.Indices) st.AddVertex(verts[idx]);
        st.GenerateNormals();     // le plan de formes ne porte pas de normales
        return st.Commit();
    }

    void UpdateInfo()
    {
        if (_spec == null) return;
        var s = _spec;
        _info.Text =
            $"{s.Name}   ({_index + 1}/{_paths.Count})\n" +
            $"\n" +
            $"longueur     {s.L,7:F1} m\n" +
            $"bau          {s.B,7:F1} m\n" +
            $"creux quille {s.Keel,7:F2} m\n" +
            $"déplacement  {s.Tonnes,7:F0} t\n" +
            $"voilure      {s.SailArea,7:F0} m²\n" +
            $"machine      {s.TopSpeed * Config.MsToKn,7:F1} nds\n" +
            $"\n" +
            $"← →  navire      glisser  tourner      molette  approcher\n" +
            $"coque bâtie par le HullLines porté — {s.Masts.Count} mât(s)";
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (e is InputEventKey k && k.Pressed && !k.Echo)
        {
            if (k.Keycode == Key.Right) { Show(_index + 1); GetViewport().SetInputAsHandled(); }
            else if (k.Keycode == Key.Left) { Show(_index - 1); GetViewport().SetInputAsHandled(); }
            else if (k.Keycode == Key.Escape) GetTree().Quit();
        }
        if (e is InputEventMouseButton mb)
        {
            if (mb.ButtonIndex == MouseButton.Left) _dragging = mb.Pressed;
            else if (mb.ButtonIndex == MouseButton.WheelUp) _dist *= 0.9f;
            else if (mb.ButtonIndex == MouseButton.WheelDown) _dist *= 1.11f;
        }
        if (e is InputEventMouseMotion mm && _dragging)
        {
            _orbit -= mm.Relative.X * 0.008f;
            _pitch = Mathf.Clamp(_pitch + mm.Relative.Y * 0.006f, -0.25f, 1.35f);
        }
    }

    public override void _Process(double delta)
    {
        if (_spec == null) return;
        // pas d'entree : elle tourne doucement, pour qu'on voie sa forme changer
        if (!_dragging) _orbit += (float)delta * 0.12f;

        float h = _dist * Mathf.Sin(_pitch);
        float r = _dist * Mathf.Cos(_pitch);
        _cam.Position = new Vector3(Mathf.Sin(_orbit) * r, h, Mathf.Cos(_orbit) * r);
        _cam.LookAt(Vector3.Zero, Vector3.Up);
    }
}
