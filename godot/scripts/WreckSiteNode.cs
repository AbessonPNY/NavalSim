using Godot;
using System;
using System.Collections.Generic;
using NavalSim.Core;

namespace NavalSim;

/// <summary>
/// LES ÉPAVES AU FOND — ce qu'on retrouve en revenant là où un navire a sombré.
///
/// PAS UN NAVIRE DU JEU : un modèle posé, comme les navires au mouillage. Il gît
/// dans l'axe où il allait, couché sur un bord, la quille dans le sable ; ses mâts
/// n'y sont plus — à vingt mètres de fond un grand mât de trente-cinq percerait
/// la surface, et une épave qu'on voit dépasser de l'eau n'est plus une épave.
/// Rompue par sa soute, elle gît en deux tronçons écartés.
///
/// SON COFFRE est posé par le travers, sur le sable : une forme simple en
/// attendant un modèle, l'or qui luit un peu dedans pour qu'on le trouve dans la
/// pénombre. Pillé, il reste là, ouvert et vide.
///
/// SEULES LES PROCHES SONT BÂTIES, et défaites quand on s'éloigne : les modèles
/// lus une fois restent en réserve, les copies posées ne coûtent qu'à portée.
/// </summary>
public partial class WreckSiteNode : Node3D
{
    readonly World _world;
    readonly string _shipsDir;
    /// <summary>Les épaves bâties : leur nœud, et le coffre dont il faut tenir l'or à jour.</summary>
    readonly Dictionary<string, (Node3D Node, Node3D Gold, Node3D Lid, Wreck W)> _built = new();
    /// <summary>Le manifeste des trésors : ce qu'on pose dans le coffre, et ses modèles.</summary>
    public TreasureBook? Book;
    readonly Dictionary<string, Node3D?> _glbs = new();
    /// <summary>Les coffres d'une seule pièce : sans couvercle, le contenu paraît DEVANT eux quand la cloche arrive.</summary>
    readonly HashSet<string> _noLid = new();
    /// <summary>La longueur d'un coffre, en mètres : un modèle y est ramené quelle que soit son échelle.</summary>
    const float ChestLength = 1.1f;
    readonly Dictionary<string, (Node3D? Root, ShipSpec? Spec)> _models = new();
    public readonly List<ShaderMaterial> Hazed = new();
    ShaderMaterial? _haze;

    /// <summary>Jusqu'où on les bâtit, en mètres.</summary>
    public double Range = 500;

    /// <summary>Les épaves qu'un navire VIVANT occupe encore — il gît lui-même au fond, inutile de le doubler.</summary>
    public Func<string, bool>? StillThere;

    public WreckSiteNode(World world, string shipsDir) { _world = world; _shipsDir = shipsDir; }

    /// <summary>
    /// Une image. <paramref name="openId"/> : l'épave dont la cloche est à portée
    /// du coffre — le couvercle se lève sur ce qu'il renferme.
    /// </summary>
    public void Update(WreckRegistry reg, Vec3d centreTrue, Vec3d origin, string? openId = null, double dt = 0)
    {
        string region = _world.Region.Key;
        var keep = new HashSet<string>();
        foreach (var w in reg.Near(region, centreTrue.X, centreTrue.Z, Range))
        {
            if (StillThere?.Invoke(w.Id) == true) continue;
            keep.Add(w.Id);
            if (!_built.ContainsKey(w.Id)) Build(w);
        }
        var gone = new List<string>();
        foreach (var (id, b) in _built)
            if (!keep.Contains(id)) { b.Node.QueueFree(); gone.Add(id); }
        foreach (var id in gone) _built.Remove(id);

        foreach (var (_, b) in _built)
        {
            var p = b.Node.Position;
            b.Node.Position = new Vector3((float)(b.W.X - origin.X), p.Y, (float)(b.W.Z - origin.Z));
            /* LE COUVERCLE : fermé tant qu'on n'y est pas, LEVÉ quand la cloche arrive
               dessus — on voit ce qu'il renferme avant de le saisir —, et rabattu en
               grand, vide, une fois pillé. Il pivote en une seconde environ. */
            if (b.W.Looted && b.Gold.Visible) b.Gold.Visible = false;
            // sans couvercle à lever, ce qu'il renferme paraît quand la cloche arrive
            else if (!b.W.Looted && _noLid.Contains(b.W.Id)) b.Gold.Visible = b.W.Id == openId;
            float want = b.W.Looted ? -110 : b.W.Id == openId ? -105 : 0;
            var r = b.Lid.RotationDegrees;
            b.Lid.RotationDegrees = new Vector3(r.X + (want - r.X) * (float)Math.Min(1, dt * 3.0), r.Y, r.Z);
        }
    }

    /// <summary>L'Y du fond en un point vrai.</summary>
    float Bed(double x, double z) => (float)_world.HeightAt(x, z);

    void Build(Wreck w)
    {
        var (root, spec) = Model(w.Ship);
        if (spec == null) return;
        uint g = 2166136261;
        foreach (char c in w.Id) g = (g ^ c) * 16777619;
        double R(int k) => ((g >> (k * 5)) & 0x1F) / 31.0;

        var node = new Node3D { Name = "epave_" + w.Id };
        AddChild(node);
        float bed = Bed(w.X, w.Z);
        // la quille dans le sable : de soixante centimètres sous le fond qu'elle a creusé
        float keel = (float)(spec.Hull.KeelDepth + spec.Hull.KeelExtra);
        node.Position = new Vector3(0, bed + keel - 0.6f, 0);

        /* COUCHÉE SUR UN BORD, DE QUINZE À TRENTE-CINQ DEGRÉS, et un peu sur le nez :
           un navire qui touche le fond s'assied sur sa quille et bascule du côté
           de sa gîte. La graine décide du bord. */
        float heel = (float)((15 + 20 * R(1)) * (R(2) < 0.5 ? -1 : 1) * Math.PI / 180);
        float trim = (float)((R(3) - 0.5) * 8 * Math.PI / 180);
        var hull = new Node3D();
        node.AddChild(hull);
        hull.Basis = new Basis(Vector3.Up, (float)w.Cap) * new Basis(Vector3.Right, trim) * new Basis(Vector3.Back, heel);

        if (root != null)
        {
            if (w.Broken)
            {
                // deux tronçons : chacun coupé de son côté, l'avant écarté et tourné
                var aft = (Node3D)root.Duplicate(); hull.AddChild(aft); aft.Transform = root.Transform;
                HullCut.Cut(aft, root.Transform, (float)w.ZCut, keepFront: false);
                var bowHolder = new Node3D();
                hull.AddChild(bowHolder);
                bowHolder.Transform = new Transform3D(new Basis(Vector3.Up, (float)(0.35 * (R(4) - 0.5))) * new Basis(Vector3.Back, -heel * 0.6f),
                                                      new Vector3((float)(R(5) - 0.5) * 3, -0.4f, 7f));
                var bow = (Node3D)root.Duplicate(); bowHolder.AddChild(bow); bow.Transform = root.Transform;
                HullCut.Cut(bow, root.Transform, (float)w.ZCut, keepFront: true);
            }
            else
            {
                var copy = (Node3D)root.Duplicate();
                hull.AddChild(copy);
                copy.Transform = root.Transform;
            }
        }
        else
        {
            // une coque sans modèle : ses lignes, comme le jeu la dessine
            var lines = new HullLines(spec);
            hull.AddChild(new MeshInstance3D
            {
                Mesh = ShipNode.ToArrayMesh(lines.BuildGeometry()),
                MaterialOverride = Plank()
            });
        }

        // --- le coffre ---
        var (cx, cz) = WreckRegistry.Chest(w, spec.B);
        var chest = new Node3D { Name = "coffre" };
        node.AddChild(chest);
        // un peu enfoui : un coffre qui tombe s'enfonce dans la vase
        chest.Position = new Vector3((float)(cx - w.X), Bed(cx, cz) - node.Position.Y - 0.08f, (float)(cz - w.Z));
        chest.Rotation = new Vector3(0, (float)(w.Cap + Math.PI / 2 + R(6)), (float)((R(7) - 0.5) * 0.3));
        var (gold, lid) = Chest(chest, w);
        if (w.Looted) { gold.Visible = false; lid.RotationDegrees = new Vector3(-110, 0, 0); }
        _built[w.Id] = (node, gold, lid, w);
    }

    /// <summary>Le modèle d'une fiche, lu une fois : sans téléporteur, sans mâts, sous la brume.</summary>
    (Node3D? Root, ShipSpec? Spec) Model(string ship)
    {
        if (_models.TryGetValue(ship, out var m)) return m;
        string sheet = System.IO.Path.Combine(_shipsDir, ship + ".json");
        if (!System.IO.File.Exists(sheet)) return _models[ship] = (null, null);
        var spec = ShipSpec.FromJson(System.IO.File.ReadAllText(sheet));
        Node3D? root = null;
        if (spec.Model is { } mm && mm.Glb.Length > 0 && System.IO.File.Exists(Assets.Path(mm.Glb)))
        {
            if (Assets.LoadGlb(Assets.Path(mm.Glb)) is Node3D r)
            {
                ShipNode.StripRings(r);
                double k = mm.Scale ?? ShipNode.HullScale(r, mm.LengthAxis, spec.Hull.Length);
                r.Scale = Vector3.One * (float)k;
                r.Rotation = new Vector3(0, (float)mm.RotationY, 0);
                var off = mm.Offset;
                r.Position = new Vector3((float)off[0], (float)off[1], (float)off[2]);
                Unmast(r, spec);
                Haze(r);
                root = r;
            }
        }
        return _models[ship] = (root, spec);
    }

    /* LES MÂTS S'EN VONT : tout ce qui est haut ET mince — une boîte plus élevée
       que le pont de trois mètres, et étroite en largeur comme en longueur. La
       coque, ses châteaux et son beaupré (long mais bas) restent. */
    static void Unmast(Node3D root, ShipSpec spec)
    {
        float deck = (float)spec.Hull.FreeboardMid;
        var stack = new Stack<(Node, Transform3D)>();
        stack.Push((root, root.Transform));
        while (stack.Count > 0)
        {
            var (n, t) = stack.Pop();
            foreach (var kid in n.GetChildren()) stack.Push((kid, kid is Node3D k3 ? t * k3.Transform : t));
            if (n is not MeshInstance3D mi || mi.Mesh == null) continue;
            var box = t * mi.Mesh.GetAabb();
            bool tall = box.End.Y > deck + 3;
            bool thin = box.Size.X < 0.3 * spec.B && box.Size.Z < 0.3 * spec.L;
            if (tall && thin) mi.Visible = false;
        }
    }

    void Haze(Node3D obj)
    {
        _haze ??= HazePass.New(-4);
        if (!Hazed.Contains(_haze)) Hazed.Add(_haze);
        HazePass.Chain(obj, _haze);
    }

    static StandardMaterial3D Plank() => new() { AlbedoColor = new Color(0.30f, 0.22f, 0.15f), Roughness = 0.95f };

    /// <summary>Un .glb lu une fois (null s'il manque) : on en pose des copies.</summary>
    Node3D? Glb(string rel)
    {
        if (string.IsNullOrEmpty(rel)) return null;
        if (_glbs.TryGetValue(rel, out var n)) return n;
        Node3D? root = null;
        string path = Assets.Path(rel);
        if (System.IO.File.Exists(path))
        {
            if (Assets.LoadGlb(path) is Node3D r) root = r;
            else GD.PushWarning($"[trésor] {rel} illisible : forme simple à la place");
        }
        else GD.PushWarning($"[trésor] {rel} introuvable : forme simple à la place");
        return _glbs[rel] = root;
    }

    /// <summary>La boîte d'un modèle dans son propre repère.</summary>
    static Aabb Box(Node3D root) => NodeWalk.Bounds(root) ?? new Aabb(Vector3.Zero, Vector3.One);

    static Node? FindNamed(Node n, string name)
    {
        if (n.Name.ToString().Contains(name, StringComparison.OrdinalIgnoreCase)) return n;
        foreach (var c in n.GetChildren()) if (FindNamed(c, name) is { } f) return f;
        return null;
    }

    /// <summary>
    /// LE COFFRE ET CE QU'IL RENFERME. Le modèle du manifeste s'il y en a un — son
    /// couvercle est le nœud nommé « couvercle » —, sinon une caisse ferrée
    /// dessinée. Dedans, chaque sorte du coffre de CETTE épave, en nombre (borné :
    /// au-delà d'une douzaine, on ne compte plus, on voit un tas). Ce qui brille
    /// ÉMET un peu : à vingt mètres il fait sombre, et c'est l'éclat qui fait
    /// qu'on le cherche. Rend le contenu et le couvercle, que le pillage change.
    /// </summary>
    (Node3D Gold, Node3D Lid) Chest(Node3D at, Wreck w)
    {
        var wood = new StandardMaterial3D { AlbedoColor = new Color(0.26f, 0.17f, 0.10f), Roughness = 0.9f };
        var iron = new StandardMaterial3D { AlbedoColor = new Color(0.12f, 0.12f, 0.13f), Metallic = 0.7f, Roughness = 0.6f };
        Node3D lid;
        float top = 0.55f;
        Vector3 heapAt = new(0, top, 0);
        if (Book != null && Glb(Book.ChestGlb) is { } model)
        {
            /* RAMENÉ À LA LONGUEUR D'UN COFFRE et posé sur le sable : un modèle arrive
               à l'échelle où il a été fait (celui-ci, près de cent unités de long), et
               son origine n'est pas forcément sous lui. */
            var c = (Node3D)model.Duplicate();
            var box = Box(c);
            float ext = Math.Max(box.Size.X, Math.Max(box.Size.Y, box.Size.Z));
            float k = ext > 1e-4f ? ChestLength / ext : 1;
            var mid = box.GetCenter();
            c.Scale = Vector3.One * k;
            c.Position = new Vector3(-mid.X * k, -box.Position.Y * k, -mid.Z * k);
            at.AddChild(c);
            top = box.Size.Y * k * 0.85f;
            heapAt = new Vector3(0, top, 0);
            if (FindNamed(c, "couvercle") is Node3D named) lid = named;
            else
            {
                /* D'UNE SEULE PIÈCE, rien ne s'ouvre : le contenu est posé devant lui,
                   sur le sable, et ne paraît qu'à l'arrivée de la cloche. */
                lid = new Node3D();
                at.AddChild(lid);
                _noLid.Add(w.Id);
                heapAt = new Vector3(0, 0.05f, box.Size.Z * k * 0.5f + 0.45f);
            }
        }
        else
        {
            at.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(1.1f, 0.6f, 0.7f) }, MaterialOverride = wood, Position = new Vector3(0, 0.3f, 0) });
            foreach (float x in new[] { -0.4f, 0.4f })
                at.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(0.06f, 0.62f, 0.72f) }, MaterialOverride = iron, Position = new Vector3(x, 0.3f, 0) });
            lid = new Node3D { Position = new Vector3(0, 0.6f, -0.35f) };
            at.AddChild(lid);
            lid.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(1.12f, 0.12f, 0.72f) }, MaterialOverride = wood, Position = new Vector3(0, 0.06f, 0.36f) });
        }

        var heap = new Node3D { Position = heapAt, Visible = !_noLid.Contains(w.Id) };
        at.AddChild(heap);
        var rng = new RandomNumberGenerator { Seed = (ulong)(w.Id.GetHashCode() & 0x7fffffff) };
        Vector3 Spot(float lift) => new(rng.RandfRange(-0.42f, 0.42f), rng.RandfRange(-0.03f, 0.03f) + lift, rng.RandfRange(-0.24f, 0.24f));
        foreach (var loot in w.Contents)
        {
            var kind = Book?.ByKey(loot.Kind);
            int shown = loot.Kind == "ecus" ? Math.Min(18, 4 + loot.Count / 15) : Math.Min(12, loot.Count);
            var glb = kind != null ? Glb(kind.Glb) : null;
            for (int i = 0; i < shown; i++)
            {
                Node3D piece = glb != null ? (Node3D)glb.Duplicate() : Placeholder(loot.Kind, i);
                piece.Position = Spot(loot.Kind == "ecus" ? 0 : 0.04f);
                piece.Rotation = new Vector3(rng.RandfRange(-0.4f, 0.4f), rng.RandfRange(0, Mathf.Tau), rng.RandfRange(-0.4f, 0.4f));
                heap.AddChild(piece);
            }
        }
        return (heap, lid);
    }

    static readonly StandardMaterial3D GoldMat = new()
    {
        AlbedoColor = new Color(0.95f, 0.72f, 0.25f), Metallic = 1f, Roughness = 0.3f,
        EmissionEnabled = true, Emission = new Color(1.0f, 0.75f, 0.30f), EmissionEnergyMultiplier = 0.6f
    };
    static readonly Color[] Gems = { new(0.10f, 0.75f, 0.30f), new(0.85f, 0.08f, 0.15f), new(0.15f, 0.30f, 0.90f), new(0.92f, 0.90f, 0.85f) };

    /// <summary>
    /// LES FORMES SIMPLES, en attendant les modèles : un écu est un disque, une
    /// pierre une gemme taillée à huit facettes (émeraude, rubis, saphir, perle),
    /// un bijou un anneau d'or, une pièce d'orfèvrerie une coupe sur son pied.
    /// </summary>
    static Node3D Placeholder(string kind, int i)
    {
        var n = new Node3D();
        switch (kind)
        {
            case "pierres":
            {
                var c = Gems[i % Gems.Length];
                n.AddChild(new MeshInstance3D
                {
                    Mesh = new SphereMesh { Radius = 0.035f, Height = 0.06f, RadialSegments = 8, Rings = 2 },
                    MaterialOverride = new StandardMaterial3D
                    {
                        AlbedoColor = c, Roughness = 0.05f, Metallic = 0.1f,
                        EmissionEnabled = true, Emission = c, EmissionEnergyMultiplier = 0.5f
                    }
                });
                break;
            }
            case "bijoux":
                n.AddChild(new MeshInstance3D { Mesh = new TorusMesh { InnerRadius = 0.035f, OuterRadius = 0.05f, Rings = 16, RingSegments = 6 }, MaterialOverride = GoldMat });
                break;
            case "orfevrerie":
                n.AddChild(new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = 0.08f, BottomRadius = 0.03f, Height = 0.10f }, MaterialOverride = GoldMat, Position = new Vector3(0, 0.12f, 0) });
                n.AddChild(new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = 0.015f, BottomRadius = 0.015f, Height = 0.08f }, MaterialOverride = GoldMat, Position = new Vector3(0, 0.05f, 0) });
                n.AddChild(new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = 0.05f, BottomRadius = 0.06f, Height = 0.015f }, MaterialOverride = GoldMat });
                break;
            default:      // les écus
                n.AddChild(new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = 0.05f, BottomRadius = 0.05f, Height = 0.012f, RadialSegments = 10 }, MaterialOverride = GoldMat });
                break;
        }
        return n;
    }
}
