using Godot;
using System;
using System.Collections.Generic;
using NavalSim.Core;

namespace NavalSim;

/// <summary>
/// LES VILLES, BÂTIES — Port-Royal sur sa langue de sable, Kingston sur la rive
/// d'en face.
///
/// À l'échelle du navire, et c'est tout l'intérêt : une maison fait six mètres
/// au faîtage, un entrepôt dix, quand la coque en fait vingt-huit de long. On
/// voit donc d'un coup d'œil ce qu'est un port de cette époque — quelques
/// centaines de toits serrés contre l'eau, pas une capitale.
///
/// Deux maillages seulement, multipliés : une boîte pour les murs, un prisme
/// pour le toit, chacun mis à l'échelle par instance et teinté par sa couleur
/// d'instance. Trois cents maisons coûtent alors deux appels de dessin.
///
/// Comme les carreaux de terre, chaque ville est bâtie dans SON PROPRE repère et
/// posée à <c>centre − origine</c> : les coordonnées d'instance restent petites,
/// et l'origine flottante peut glisser tant qu'elle veut.
/// </summary>
public partial class TownNode : Node3D
{
    readonly World _world;
    readonly List<(Vec3d At, Node3D Node)> _towns = new();

    /// <summary>Les matériaux que <see cref="SkyNode.PushTo"/> doit tenir à jour.</summary>
    public readonly List<ShaderMaterial> Hazed = new();

    /// <summary>Jusqu'où une ville est dessinée.</summary>
    public double Range = 9000;

    StandardMaterial3D _mat = null!;
    /// <summary>Le mur, qui sait s'allumer ; le toit garde le matériau ordinaire.</summary>
    ShaderMaterial _walls = null!;
    Mesh _wall = null!, _roof = null!;
    bool _modelled;

    public TownNode(World world) { _world = world; }

    public override void _Ready()
    {
        _mat = new StandardMaterial3D
        {
            VertexColorUseAsAlbedo = true,      // la couleur d'instance du MultiMesh
            Roughness = 0.92f,
            Metallic = 0f,
            NextPass = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/hull_haze.gdshader") }
        };
        Hazed.Add((ShaderMaterial)_mat.NextPass);

        /* LES MURS ONT LEUR PROPRE MATIÈRE parce qu'ils ont des fenêtres, et les
           fenêtres s'allument (shaders/town.gdshader). Le toit, lui, n'a rien à
           éclairer : il garde le matériau ordinaire, qui coûte moins. */
        _walls = new ShaderMaterial
        {
            Shader = GD.Load<Shader>("res://shaders/town.gdshader"),
            NextPass = _mat.NextPass
        };
        _wall = new BoxMesh { Size = Vector3.One };
        _roof = Roof();
        // les bâtiments modelés, s ils sont là ; sinon la ville garde ses boîtes
        _modelled = LoadModels();
    }

    /// <summary>
    /// Un toit à deux pentes, unitaire : base d'un mètre de côté, faîtage à un
    /// mètre, le faîte courant selon z. Mis à l'échelle par instance, il garde sa
    /// forme quelle que soit la maison.
    /// </summary>
    static Mesh Roof()
    {
        var v = new List<Vector3>();
        var n = new List<Vector3>();
        void Tri(Vector3 a, Vector3 b, Vector3 c)
        {
            var nn = (b - a).Cross(c - a).Normalized();
            v.Add(a); v.Add(b); v.Add(c);
            n.Add(nn); n.Add(nn); n.Add(nn);
        }
        Vector3 A = new(-0.5f, 0, -0.5f), B = new(0.5f, 0, -0.5f), C = new(0.5f, 0, 0.5f), D = new(-0.5f, 0, 0.5f);
        Vector3 R0 = new(0, 1, -0.5f), R1 = new(0, 1, 0.5f);
        Tri(A, R0, R1); Tri(A, R1, D);          // le pan ouest
        Tri(B, C, R1); Tri(B, R1, R0);          // le pan est
        Tri(A, B, R0);                          // le pignon nord
        Tri(D, R1, C);                          // le pignon sud
        var arr = new Godot.Collections.Array();
        arr.Resize((int)Mesh.ArrayType.Max);
        arr[(int)Mesh.ArrayType.Vertex] = v.ToArray();
        arr[(int)Mesh.ArrayType.Normal] = n.ToArray();
        var m = new ArrayMesh();
        m.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arr);
        return m;
    }

    /* Le crépi de la côte — chaux, ocre, rose passé — et les toits, tuile ou
       bardeau goudronné. En sRGB comme les teintes de la terre, converties : une
       couleur d'instance est prise pour linéaire. */
    static readonly Color[] Walls =
    {
        Color.Color8(0xe8, 0xdf, 0xcb).SrgbToLinear(), Color.Color8(0xd9, 0xc7, 0xa4).SrgbToLinear(),
        Color.Color8(0xc9, 0xa8, 0x8d).SrgbToLinear(), Color.Color8(0xbf, 0xc2, 0xb4).SrgbToLinear(),
        Color.Color8(0xd8, 0xb6, 0x86).SrgbToLinear()
    };
    static readonly Color[] Roofs =
    {
        Color.Color8(0x9b, 0x4f, 0x36).SrgbToLinear(), Color.Color8(0x86, 0x45, 0x30).SrgbToLinear(),
        Color.Color8(0x6b, 0x53, 0x3f).SrgbToLinear(), Color.Color8(0x5a, 0x51, 0x45).SrgbToLinear()
    };

    /// <summary>Bâtir une ville autour d'un point du monde, une fois.</summary>
    public void Build(string name, double cx, double cz, double radius, int want, uint seed)
    {
        var houses = Town.Plant(_world, cx, cz, radius, want, seed);
        if (houses.Count == 0)
        {
            GD.PushWarning($"{name} : aucun terrain à bâtir — ville ignorée.");
            return;
        }
        var holder = new Node3D();
        AddChild(holder);
        if (_modelled)
        {
            BuildFromModels(holder, houses, cx, cz);
            _towns.Add((new Vec3d(cx, 0, cz), holder));
            GD.Print($"{name} : {houses.Count} bâtiment(s), une église");
            return;
        }

        var mmWall = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            UseColors = true, Mesh = _wall, InstanceCount = houses.Count
        };
        var mmRoof = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            UseColors = true, Mesh = _roof, InstanceCount = houses.Count
        };
        uint s = seed | 3;
        double Rnd() { s ^= s << 13; s ^= s >> 17; s ^= s << 5; return (s & 0xFFFFFF) / 16777216.0; }

        for (int i = 0; i < houses.Count; i++)
        {
            var h = houses[i];
            var basis = new Basis(Vector3.Up, (float)h.Yaw);
            var at = new Vector3((float)(h.X - cx), (float)h.Y, (float)(h.Z - cz));
            /* Les fondations RENTRENT d'un demi-mètre dans le sol : le relief est
               lu au centre de la maison, et un terrain qui bouge d'un rien sous
               elle la laisserait sinon sur pilotis d'un côté. */
            mmWall.SetInstanceTransform(i, new Transform3D(
                basis.Scaled(new Vector3((float)h.W, (float)(h.H + 0.5), (float)h.D)),
                at + new Vector3(0, (float)(h.H * 0.5 - 0.25), 0)));
            mmRoof.SetInstanceTransform(i, new Transform3D(
                // le toit déborde des murs : c'est ce débord qui fait l'ombre d'un toit
                basis.Scaled(new Vector3((float)(h.W * 1.06), (float)h.RoofH, (float)(h.D * 1.05))),
                at + new Vector3(0, (float)h.H, 0)));
            mmWall.SetInstanceColor(i, Walls[(int)(Rnd() * Walls.Length) % Walls.Length]);
            mmRoof.SetInstanceColor(i, Roofs[(int)(Rnd() * Roofs.Length) % Roofs.Length]);
        }

        holder.AddChild(new MultiMeshInstance3D
        {
            Multimesh = mmWall, MaterialOverride = _walls,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.On,
            ExtraCullMargin = 400
        });
        holder.AddChild(new MultiMeshInstance3D
        {
            Multimesh = mmRoof, MaterialOverride = _mat,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.On,
            ExtraCullMargin = 400
        });

        _towns.Add((new Vec3d(cx, 0, cz), holder));
        GD.Print($"{name} : {houses.Count} maison(s) bâtie(s)");
    }

    /// <summary>
    /// LA NUIT TOMBE SUR LA VILLE : le même chiffre que les fanaux du bord, si
    /// bien que les fenêtres s'allument quand les lanternes s'allument et non
    /// quand un second seuil en décide.
    /// </summary>
    public void SetNight(double night)
    {
        float n = (float)Math.Clamp(night, 0, 1);
        _walls?.SetShaderParameter("u_night", n);
        _glass?.SetShaderParameter("u_night", n);
    }

    /// <summary>Poser les villes contre l'origine du moment, et cacher celles qui sont loin.</summary>
    public void Update(Vec3d centre, Vec3d origin)
    {
        foreach (var (at, node) in _towns)
        {
            double dx = at.X - centre.X, dz = at.Z - centre.Z;
            node.Visible = dx * dx + dz * dz < Range * Range;
            if (node.Visible)
                node.Position = new Vector3((float)(at.X - origin.X), 0, (float)(at.Z - origin.Z));
        }
    }
}
