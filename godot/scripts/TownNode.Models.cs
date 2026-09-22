using Godot;
using System;
using System.Collections.Generic;
using NavalSim.Core;

namespace NavalSim;

/// <summary>
/// LES BÂTIMENTS MODELÉS — une église et deux maisons, lues dans world/models
/// (écrites par tools/town-glb.js, modifiables dans Blender).
///
/// Une ville reste un MULTIMESH par matière et non une scène par maison : trois
/// cents maisons en nœuds à part coûteraient trois cents dessins là où il en
/// faut cinq. Chaque .glb apporte donc ses surfaces, et chacune devient un
/// MultiMesh que toutes les maisons de ce modèle partagent.
///
/// Ce qui compte est le NOM des matières : « fenetre » s'allume la nuit,
/// « mur » prend la teinte de crépi de la maison, le reste garde la sienne.
/// Modèles absents ou illisibles : la ville retombe sur ses boîtes, et le dit.
/// </summary>
public partial class TownNode
{
    /// <summary>Une surface d'un modèle : son maillage, sa matière, son nom.</summary>
    public readonly record struct Face(Mesh Mesh, Material Mat, string Name);

    /// <summary>Un bâtiment lu : ses surfaces et son emprise au sol, en mètres.</summary>
    public sealed class Model
    {
        public readonly List<Face> Faces = new();
        public double W, D;                       // emprise, sans les débords de toit
    }

    Model? _church, _houseA, _houseB;
    ShaderMaterial? _glass;

    static string RepoRoot =>
        System.IO.Path.GetDirectoryName(ProjectSettings.GlobalizePath("res://").TrimEnd('/', '\\')) ?? "";

    /// <summary>Lire les trois bâtiments. Faux si l'un manque : la ville garde ses boîtes.</summary>
    bool LoadModels()
    {
        _glass = new ShaderMaterial
        {
            Shader = GD.Load<Shader>("res://shaders/town_glass.gdshader"),
            NextPass = _mat.NextPass
        };
        _church = LoadBuilding("world/models/eglise.glb");
        _houseA = LoadBuilding("world/models/maison-a.glb");
        _houseB = LoadBuilding("world/models/maison-b.glb");
        return _church != null && _houseA != null && _houseB != null;
    }

    Model? LoadBuilding(string rel)
    {
        string path = System.IO.Path.Combine(RepoRoot, rel);
        if (!System.IO.File.Exists(path)) { GD.PushWarning($"[ville] {rel} introuvable — maisons en boîtes."); return null; }
        var doc = new GltfDocument();
        var state = new GltfState();
        if (doc.AppendFromFile(path, state) != Error.Ok || doc.GenerateScene(state) is not Node3D root)
        {
            GD.PushWarning($"[ville] {rel} illisible — maisons en boîtes.");
            return null;
        }
        var m = new Model();
        var box = new Aabb();
        bool first = true;
        var stack = new Stack<(Node N, Transform3D Xf)>();
        stack.Push((root, root.Transform));
        while (stack.Count > 0)
        {
            var (n, xf) = stack.Pop();
            if (n is MeshInstance3D mi && mi.Mesh != null)
                for (int s = 0; s < mi.Mesh.GetSurfaceCount(); s++)
                {
                    var mat = mi.GetActiveMaterial(s);
                    string name = (mat?.ResourceName ?? "").ToLowerInvariant();
                    if (name.Length == 0) name = n.Name.ToString().ToLowerInvariant();
                    var one = OneSurface(mi.Mesh, s, xf);
                    if (one == null) continue;
                    Material use = name.Contains("fenetre") || name.Contains("vitre")
                        ? _glass!
                        : Dressed(mat, name.Contains("mur"));
                    m.Faces.Add(new Face(one, use, name));
                    var bb = xf * mi.Mesh.GetAabb();
                    box = first ? bb : box.Merge(bb); first = false;
                }
            var kids = n.GetChildren();
            for (int i = kids.Count - 1; i >= 0; i--) stack.Push((kids[i], kids[i] is Node3D c ? xf * c.Transform : xf));
        }
        root.QueueFree();
        if (m.Faces.Count == 0) return null;
        /* L'EMPRISE SANS LES DÉBORDS : le toit déborde des murs et le clocher
           sort de la nef ; prendre la boîte entière ferait rentrer les bâtiments
           d'un bon mètre dans leur parcelle. Neuf dixièmes, mesuré sur les trois. */
        m.W = Math.Max(1, box.Size.X * 0.9);
        m.D = Math.Max(1, box.Size.Z * 0.9);
        return m;
    }

    /// <summary>Une seule surface d'un maillage, figée dans le repère du modèle.</summary>
    static Mesh? OneSurface(Mesh src, int s, Transform3D xf)
    {
        var a = src.SurfaceGetArrays(s);
        var v = a[(int)Mesh.ArrayType.Vertex].AsVector3Array();
        if (v.Length == 0) return null;
        var nrm = a[(int)Mesh.ArrayType.Normal].AsVector3Array();
        for (int i = 0; i < v.Length; i++) v[i] = xf * v[i];
        for (int i = 0; i < nrm.Length; i++) nrm[i] = (xf.Basis * nrm[i]).Normalized();
        var outA = new Godot.Collections.Array();
        outA.Resize((int)Mesh.ArrayType.Max);
        outA[(int)Mesh.ArrayType.Vertex] = v;
        if (nrm.Length == v.Length) outA[(int)Mesh.ArrayType.Normal] = nrm;
        outA[(int)Mesh.ArrayType.TexUV] = a[(int)Mesh.ArrayType.TexUV];
        outA[(int)Mesh.ArrayType.Index] = a[(int)Mesh.ArrayType.Index];
        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, outA);
        return mesh;
    }

    /// <summary>La matière du modèle, avec la brume par-dessus ; les murs prennent la teinte de l'instance.</summary>
    Material Dressed(Material? src, bool wall)
    {
        var bm = src as BaseMaterial3D;
        var own = bm != null ? (BaseMaterial3D)bm.Duplicate() : new StandardMaterial3D();
        own.VertexColorUseAsAlbedo = wall;        // le crépi varie d'une maison à l'autre
        own.NextPass = _mat.NextPass;             // la brume, comme tout ce qui est à terre
        return own;
    }

    /// <summary>
    /// Bâtir une ville avec les modèles : une église, prise sur la plus grande
    /// parcelle près du centre, et des maisons tirées au sort par leur position —
    /// la même ville d'une partie à l'autre.
    /// </summary>
    void BuildFromModels(Node3D holder, List<House> houses, double cx, double cz)
    {
        int church = 0;
        double best = -1;
        for (int i = 0; i < houses.Count; i++)
        {
            var h = houses[i];
            double d = Math.Sqrt((h.X - cx) * (h.X - cx) + (h.Z - cz) * (h.Z - cz));
            // grande parcelle et près du centre : une église se voit et se rejoint
            double score = h.W * h.D - 0.02 * d;
            if (score > best) { best = score; church = i; }
        }

        var groups = new List<(Model M, List<int> Who)>
        {
            (_church!, new List<int> { church }),
            (_houseA!, new List<int>()),
            (_houseB!, new List<int>())
        };
        for (int i = 0; i < houses.Count; i++)
        {
            if (i == church) continue;
            var h = houses[i];
            // le tirage vient de SA position : la même maison au même endroit, toujours
            double r = Frac(Math.Sin(h.X * 12.9898 + h.Z * 78.233) * 43758.5453);
            groups[r < 0.62 ? 1 : 2].Who.Add(i);
        }

        foreach (var (model, who) in groups)
        {
            if (who.Count == 0) continue;
            foreach (var face in model.Faces)
            {
                var mm = new MultiMesh
                {
                    TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
                    UseColors = true, Mesh = face.Mesh, InstanceCount = who.Count
                };
                for (int k = 0; k < who.Count; k++)
                {
                    var h = houses[who[k]];
                    /* À L'ÉCHELLE DE SA PARCELLE, sans la déformer : un bâtiment
                       étiré en largeur seulement se lit tout de suite comme un
                       décor. On prend donc le plus petit des deux rapports. */
                    double k2 = Math.Min(h.W / model.W, h.D / model.D);
                    var basis = new Basis(Vector3.Up, (float)h.Yaw).Scaled(Vector3.One * (float)k2);
                    // dix centimètres dans le sol : le relief bouge un peu sous l'emprise
                    var at = new Vector3((float)(h.X - cx), (float)(h.Y - 0.1), (float)(h.Z - cz));
                    mm.SetInstanceTransform(k, new Transform3D(basis, at));
                    mm.SetInstanceColor(k, Walls[(int)(Frac(Math.Sin(h.X * 3.17 + h.Z * 7.31) * 9187.71) * Walls.Length) % Walls.Length]);
                }
                holder.AddChild(new MultiMeshInstance3D
                {
                    Multimesh = mm, MaterialOverride = face.Mat,
                    CastShadow = GeometryInstance3D.ShadowCastingSetting.On,
                    ExtraCullMargin = 400
                });
            }
        }
    }

    static double Frac(double x) => x - Math.Floor(x);
}
