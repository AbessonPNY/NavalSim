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
        public double H;                          // hauteur, cheminées comprises
        public string Name = "";                  // son fichier, qui fait sa famille dans la palette
    }

    Model? _church, _houseA, _houseB;
    ShaderMaterial? _glass;
    /// <summary>
    /// UN VERRE PAR GROUPE DE FENÊTRES : « fenetre », « fenetre_002 », « fenetre_003 »
    /// d'un même modèle s'allument chacun selon son propre tirage — une maison a
    /// alors une, deux ou trois rangées éclairées, et la rue cesse d'être faite de
    /// maisons toutes allumées ou toutes noires. La clé est le numéro du groupe.
    /// </summary>
    readonly Dictionary<int, ShaderMaterial> _glassSet = new();

    /// <summary>Le verre du groupe que nomme la matière (le numéro qui la termine, 0 sans numéro).</summary>
    ShaderMaterial GlassFor(string name)
    {
        int g = 0;
        var m = System.Text.RegularExpressions.Regex.Match(name, @"(\d+)\s*$");
        if (m.Success) g = int.Parse(m.Groups[1].Value);
        if (_glassSet.TryGetValue(g, out var mat)) return mat;
        mat = g == 0 ? _glass! : (ShaderMaterial)_glass!.Duplicate();
        mat.SetShaderParameter("u_group", (float)g);
        _glassSet[g] = mat;
        return mat;
    }

    bool IsGlass(Material m) => m is ShaderMaterial sm && _glassSet.ContainsValue(sm);

    static string RepoRoot => Assets.Root;

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
        string path = Assets.Path(rel);
        if (!System.IO.File.Exists(path)) { GD.PushWarning($"[ville] {rel} introuvable — maisons en boîtes."); return null; }
        if (Assets.LoadGlb(path) is not Node3D root)
        {
            GD.PushWarning($"[ville] {rel} illisible — maisons en boîtes.");
            return null;
        }
        var m = new Model { Name = rel };
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
                        ? GlassFor(name)
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
        m.H = Math.Max(1, box.End.Y);
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
    void BuildFromModels(Node3D holder, List<House> houses, double cx, double cz, string name)
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
            AddGroup(holder, model, houses, who, cx, cz, $"maison:{name}:", ReferenceEquals(model, _church) ? $"l'église de {name}" : $"une maison de {name}");
    }

    /// <summary>Les maisons d'un même modèle : un MultiMesh par surface.</summary>
    void AddGroup(Node3D holder, Model model, List<House> houses, List<int> who, double cx, double cz,
                  string idPrefix, string label)
    {
            if (who.Count == 0) return;
            var mms = new List<MultiMesh>(model.Faces.Count);
            foreach (var face in model.Faces)
            {
                var mm = new MultiMesh
                {
                    TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
                    UseColors = true, Mesh = face.Mesh, InstanceCount = who.Count
                };
                mms.Add(mm);
                for (int k = 0; k < who.Count; k++)
                {
                    var h = houses[who[k]];
                    // le crépi suit la place AUTOMATIQUE : une maison déplacée garde sa couleur
                    mm.SetInstanceColor(k, Walls[(int)(Frac(Math.Sin(h.X * 3.17 + h.Z * 7.31) * 9187.71) * Walls.Length) % Walls.Length]);
                }
                /* LE VERRE NE JETTE PAS D'OMBRE. Il est transparent le jour et ne
                   doit rien laisser de lui ; une ombre portée serait la seule
                   trace d'un panneau qu'on a fait disparaître exprès. */
                bool vitre = IsGlass(face.Mat);
                holder.AddChild(new MultiMeshInstance3D
                {
                    Multimesh = mm, MaterialOverride = face.Mat,
                    CastShadow = vitre ? GeometryInstance3D.ShadowCastingSetting.Off
                                       : GeometryInstance3D.ShadowCastingSetting.On,
                    ExtraCullMargin = 400
                });
            }

            /* CHAQUE MAISON EST UN OBJET RETOUCHABLE, posé par la même fonction qu'il
               ait bougé ou non : l'éditeur ne connaît que son état voulu, et c'est
               ici qu'on en tire les transformées de toutes ses surfaces. */
            for (int k = 0; k < who.Count; k++)
            {
                var h = houses[who[k]];
                /* À L'ÉCHELLE DE SA PARCELLE, sans la déformer : un bâtiment
                   étiré en largeur seulement se lit tout de suite comme un
                   décor. On prend donc le plus petit des deux rapports. */
                double k2 = Math.Min(h.W / model.W, h.D / model.D);
                int idx = k;
                var wall = Walls[(int)(Frac(Math.Sin(h.X * 3.17 + h.Z * 7.31) * 9187.71) * Walls.Length) % Walls.Length];
                var e = new Editable
                {
                    Id = idPrefix + who[k], Label = label,
                    BaseX = h.X, BaseZ = h.Z, BaseYaw = h.Yaw, X = h.X, Z = h.Z, Yaw = h.Yaw,
                    Radius = 0.5 * Math.Sqrt(h.W * h.W + h.D * h.D), Height = model.H * k2,
                    Family = "bati:" + model.Name, FamilyLabel = System.IO.Path.GetFileNameWithoutExtension(model.Name),
                    Lift = -0.1,
                    /* LA COPIE, HORS DU MULTIMESH : un nœud par surface, à l'échelle de
                       la maison. Le crépi y devient la couleur de la matière, la copie
                       n'ayant pas de couleur d'instance pour le porter. */
                    MakeVisual = () =>
                    {
                        var n = new Node3D();
                        foreach (var face in model.Faces)
                        {
                            Material m = face.Mat;
                            if (m is BaseMaterial3D bm && bm.VertexColorUseAsAlbedo)
                            {
                                var dup = (BaseMaterial3D)bm.Duplicate();
                                dup.VertexColorUseAsAlbedo = false;
                                dup.AlbedoColor = wall;
                                m = dup;
                            }
                            n.AddChild(new MeshInstance3D
                            {
                                Mesh = face.Mesh, MaterialOverride = m, Scale = Vector3.One * (float)k2,
                                CastShadow = IsGlass(face.Mat) ? GeometryInstance3D.ShadowCastingSetting.Off
                                                                               : GeometryInstance3D.ShadowCastingSetting.On
                            });
                        }
                        return n;
                    }
                };
                e.Push = ed =>
                {
                    // à sa place, le pied que le semis a trouvé ; ailleurs, le point le plus bas sous l'emprise
                    double y = ed.Moved || Math.Abs(ed.Yaw - ed.BaseYaw) > 1e-4 ? Footing(ed.X, ed.Z, h.W * ed.Scale, h.D * ed.Scale, ed.Yaw) : h.Y;
                    ed.GroundY = y + ed.Dy;
                    var basis = new Basis(Vector3.Up, (float)ed.Yaw).Scaled(Vector3.One * (float)(ed.Removed ? 0 : k2 * ed.Scale));
                    // dix centimètres dans le sol : le relief bouge un peu sous l'emprise
                    var at = new Vector3((float)(ed.X - cx), (float)(ed.GroundY - 0.1), (float)(ed.Z - cz));
                    foreach (var mm in mms) mm.SetInstanceTransform(idx, new Transform3D(basis, at));
                };
                /* LES CHEMINÉES QUI FUMENT : une maison sur trois — on ne fait pas du
                   feu partout à toute heure —, deux par pâté, aucune à l'église. Au
                   faîte, à peu près où le modèle a ses souches : on ne les connaît pas,
                   et l'éditeur est là pour les y mettre. Posées AVANT la maison dans le
                   registre, pour qu'elle les connaisse quand une copie les emporte. */
                if (!ReferenceEquals(model, _church))
                {
                    bool block = idPrefix.StartsWith("pate:", StringComparison.Ordinal);
                    int count = block ? 2 : Frac(Math.Sin(h.X * 7.71 + h.Z * 3.13) * 43758.5453) < 0.33 ? 1 : 0;
                    if (count > 0)
                    {
                        e.Chimneys = new List<(double, double)>();
                        for (int c = 0; c < count; c++)
                            e.Chimneys.Add((count == 2 ? (c == 0 ? -0.3 : 0.3) * h.W : 0, model.H * k2 * (block ? 0.97 : 0.9)));
                    }
                }
                if (Editor != null) Editor.Add(e); else e.Push(e);
                if (Editor != null) AddChimneys(Editor, e, e.Id);
            }
    }

    /// <summary>Les cheminées d'une maison, inscrites comme des objets : elles la suivent tant qu'on ne les déplace pas.</summary>
    public void AddChimneys(EditRegistry reg, Editable host, string hostId)
    {
        if (host.Chimneys == null) return;
        for (int c = 0; c < host.Chimneys.Count; c++)
        {
            var (ox, top) = host.Chimneys[c];
            var sm = new Editable
            {
                Id = $"fumee:{hostId}:{c}", Label = "une fumée de cheminée", Smoke = true,
                Host = host, HostOx = ox, HostTop = top, Radius = 1.2, Height = 2,
                Family = "fumee", FamilyLabel = "Fumée de cheminée",
                MakeVisual = () => new Node3D()
            };
            // sa place de départ : là où sa maison la met
            sm.PlaceEmitter(_world.HeightAt);
            sm.BaseYaw = sm.Yaw = 0;
            sm.Push = ed => ed.PlaceEmitter(_world.HeightAt);
            reg.Add(sm);
        }
    }

    /// <summary>Le point le plus bas sous une emprise : une maison se pose sur lui, jamais en porte-à-faux.</summary>
    double Footing(double x, double z, double w, double d, double yaw)
    {
        double ca = Math.Cos(yaw), sa = Math.Sin(yaw), y = _world.HeightAt(x, z);
        for (int c = 0; c < 4; c++)
        {
            double ox = (c < 2 ? -w : w) * 0.5, oz = (c % 2 == 0 ? -d : d) * 0.5;
            y = Math.Min(y, _world.HeightAt(x + ox * ca + oz * sa, z - ox * sa + oz * ca));
        }
        return y;
    }

    /// <summary>
    /// LE CENTRE-VILLE D'UN PORT : ses pâtés, là où la grille du noyau les a posés
    /// (<see cref="Town.Streets"/>). Le pavé des rues, lui, est dans la terre
    /// (<see cref="LandNode"/>) : la même grille, lue des deux côtés.
    /// </summary>
    public void BuildCentre(Isle isl)
    {
        StreetGrid? g = null;
        foreach (var q in _world.Grids) if (q.Key == isl.Key) g = q;
        if (g == null) return;
        var model = LoadBuilding(g.Glb);
        if (model == null) return;
        var holder = new Node3D();
        AddChild(holder);
        var who = new List<int>(g.Blocks.Count);
        for (int i = 0; i < g.Blocks.Count; i++) who.Add(i);
        AddGroup(holder, model, g.Blocks, who, g.Cx, g.Cz, $"pate:{isl.Key}:", $"un pâté du centre de {isl.Name}");
        _towns.Add((new Vec3d(g.Cx, 0, g.Cz), holder));
        GD.Print($"{isl.Name} : centre-ville, {g.Blocks.Count} pâté(s) en rangées");
    }

    static double Frac(double x) => x - Math.Floor(x);
}
