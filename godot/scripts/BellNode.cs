using Godot;
using System;
using NavalSim.Core;

namespace NavalSim;

/// <summary>
/// LA CLOCHE, dessinée en formes simples en attendant un modèle : un tronc de
/// cône ouvert en dessous, aux cotes de celle de Halley (DivingBell), son anneau
/// de suspente, son câble jusqu'à la vergue, et l'EAU QUI MONTE DEDANS.
///
/// Cette eau est ce que la loi de Boyle donne à voir : un disque à la hauteur où
/// l'air comprimé la retient, qui monte quand on descend. Vu de l'intérieur, on
/// la regarde venir.
///
/// UNE LANTERNE dans la cloche : à vingt mètres il fait sombre, et le coffre doit
/// se voir à quelques mètres. Elle est créée une fois, et éteinte à bord.
/// </summary>
public partial class BellNode : Node3D
{
    MeshInstance3D _water = null!, _cable = null!;
    OmniLight3D _lamp = null!;
    public readonly System.Collections.Generic.List<ShaderMaterial> Hazed = new();

    public override void _Ready()
    {
        var hull = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.20f, 0.19f, 0.17f), Metallic = 0.35f, Roughness = 0.7f,
            // on la voit de l'intérieur : ses deux faces
            CullMode = BaseMaterial3D.CullModeEnum.Disabled
        };
        AddChild(new MeshInstance3D
        {
            Mesh = new CylinderMesh
            {
                TopRadius = (float)DivingBell.TopR, BottomRadius = (float)DivingBell.BottomR,
                Height = (float)DivingBell.Height, RadialSegments = 28, Rings = 2, CapBottom = false
            },
            MaterialOverride = hull,
            Position = new Vector3(0, (float)(DivingBell.Height / 2), 0)
        });
        // les cercles de plomb, au bord et à mi-hauteur
        var lead = new StandardMaterial3D { AlbedoColor = new Color(0.10f, 0.10f, 0.11f), Metallic = 0.6f, Roughness = 0.5f };
        foreach (var (y, r) in new[] { (0.08, DivingBell.BottomR + 0.02), (DivingBell.Height * 0.5, (DivingBell.BottomR + DivingBell.TopR) / 2 + 0.02) })
            AddChild(new MeshInstance3D
            {
                Mesh = new TorusMesh { InnerRadius = (float)r - 0.04f, OuterRadius = (float)r + 0.03f, Rings = 24, RingSegments = 8 },
                MaterialOverride = lead, Position = new Vector3(0, (float)y, 0)
            });
        AddChild(new MeshInstance3D
        {
            Mesh = new TorusMesh { InnerRadius = 0.10f, OuterRadius = 0.16f },
            MaterialOverride = lead,
            Position = new Vector3(0, (float)DivingBell.Height + 0.12f, 0),
            Rotation = new Vector3(Mathf.Pi / 2, 0, 0)
        });

        // l'eau dedans : un disque translucide que la pression fait monter
        _water = new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = 1, BottomRadius = 1, Height = 0.01f, RadialSegments = 28 },
            MaterialOverride = new StandardMaterial3D
            {
                AlbedoColor = new Color(0.10f, 0.32f, 0.36f, 0.55f), Roughness = 0.05f, Metallic = 0.2f,
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                CullMode = BaseMaterial3D.CullModeEnum.Disabled
            }
        };
        AddChild(_water);

        // le câble : un cylindre d'un mètre, mis à l'échelle de sa longueur
        _cable = new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = 1, BottomRadius = 1, Height = 1, RadialSegments = 6 },
            MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.32f, 0.26f, 0.18f), Roughness = 0.95f },
            TopLevel = true
        };
        AddChild(_cable);

        _lamp = new OmniLight3D
        {
            LightColor = new Color(1.0f, 0.78f, 0.45f), LightEnergy = 0, OmniRange = 14,
            Position = new Vector3(0, (float)(DivingBell.Height * 0.75), 0), ShadowEnabled = false
        };
        AddChild(_lamp);
        Visible = false;
    }

    /// <summary>
    /// Une image : où est son bord (le bas de la cloche), à quelle profondeur, et
    /// d'où pend le câble. <paramref name="depth"/> fait monter l'eau dedans.
    /// </summary>
    public void Pose(Vector3 bottom, Basis swing, double depth, Vector3 hang, bool lit)
    {
        Visible = true;
        Position = bottom;
        Basis = swing;
        double h = DivingBell.WaterInside(depth);
        double r = DivingBell.BottomR + (DivingBell.TopR - DivingBell.BottomR) * (h / DivingBell.Height);
        _water.Visible = depth > 0.3;
        _water.Position = new Vector3(0, (float)h, 0);
        _water.Scale = new Vector3((float)r * 0.98f, 1, (float)r * 0.98f);
        _lamp.LightEnergy = lit ? 1.6f : 0;

        var top = GlobalTransform * new Vector3(0, (float)DivingBell.Height + 0.15f, 0);
        float len = top.DistanceTo(hang);
        if (len < 0.05f) { _cable.Visible = false; return; }
        _cable.Visible = true;
        var mid = (top + hang) * 0.5f;
        _cable.GlobalPosition = mid;
        _cable.LookAt(hang, Mathf.Abs((hang - top).Normalized().Y) > 0.98f ? Vector3.Forward : Vector3.Up);
        _cable.RotateObjectLocal(Vector3.Right, Mathf.Pi * 0.5f);
        _cable.Scale = new Vector3(0.03f, len, 0.03f);
    }

    public void Stow() { Visible = false; _lamp.LightEnergy = 0; }
}
