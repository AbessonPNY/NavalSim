using Godot;
using System;
using System.Collections.Generic;
using NavalSim.Core;

namespace NavalSim;

/// <summary>
/// LA TERRE, DESSINÉE.
///
/// EN CARREAUX, parce que la terre est une côte et non quatre îles : une grille
/// carrée en mètres VRAIS du monde, chaque carreau bâti la première fois qu'il
/// arrive à portée et gardé ensuite — le relief ne change jamais. Seuls les
/// carreaux qui portent de la terre ou un haut-fond sont bâtis ; la haute mer
/// est le shader de la mer. Près d'elle un carreau est dessiné à la résolution
/// de l'image, plus loin au quart, et rien ne pend sous leurs bords : voir plus
/// bas pourquoi la jupe de la page a été retirée des deux côtés.
///
/// L'essentiel reste le PLACEMENT. Chaque carreau est bâti dans SON PROPRE
/// repère et simplement POSÉ à <c>carreau − origine</c> à chaque image :
/// l'origine flottante peut glisser tant qu'elle veut, déplacer un carreau est
/// une affectation de vecteur. Rien ici n'a donc de Rebase — c'est le monde qui
/// est fixe et le repère local qui bouge.
/// </summary>
public partial class LandNode : Node3D
{
    public readonly World World;

    /// <summary>Jusqu'où la terre est dessinée, en mètres.</summary>
    public double Range = 14000;
    /// <summary>En deçà, le plein détail.</summary>
    public double NearRange = 4500;
    /// <summary>Le plus fin qu'un carreau se bâtit : 1440 m en 288 vaut cinq mètres.</summary>
    public int FineMax = 288;
    /// <summary>Le côté d'un carreau : 32 pixels du relief au départ.</summary>
    public double Tile = 1440;
    /// <summary>La lumière de la houle rassemblée sur le fond (settings.json → caustics).</summary>
    public CausticSettings CausticRules = new();

    /// <summary>Les matériaux que <see cref="SkyNode.PushTo"/> doit tenir à jour.</summary>
    public readonly List<ShaderMaterial> Hazed = new();
    /// <summary>
    /// Ce qui ondule sur le fond (seaweed.gdshader) : la démo leur pousse la houle et
    /// l'abri à chaque image, comme au fond lui-même — le ressac doit être celui de
    /// la mer qu'on voit.
    /// </summary>
    public readonly List<ShaderMaterial> Swaying = new();

    /// <summary>
    /// LA MATIÈRE DU RELIEF — et, avec elle, les caustiques du fond. Elle lit la
    /// MÊME mer que la surface : la démo lui pousse le spectre à chaque image
    /// (<see cref="OceanNode.PushWaves"/>), le soleil, les rides et le havre.
    /// </summary>
    public ShaderMaterial Ground { get; private set; } = null!;

    sealed class Patch
    {
        public bool Has;
        // un carreau terrassé est un nœud de morceaux (LandNode.Terrace.cs), les autres un seul maillage
        public Node3D? Near, Far;
    }

    readonly Dictionary<(int, int), Patch> _tiles = new();
    readonly HashSet<Node3D> _seen = new();
    bool _assetsBuilt;
    ShaderMaterial _mat = null!;

    public LandNode(World world)
    {
        World = world;
    }

    public override void _Ready()
    {
        /* Du sable au bord de l'eau qui monte en herbe, en forêt et en roche.
           Couleurs de SOMMET plutôt qu'une texture : la bande à laquelle un
           point appartient est une fonction de sa hauteur, et rien à charger.
           Color8 et non Color : le constructeur de Godot prend des FLOTTANTS, et
           0xc9 y valait 201, donc une côte blanche à souhait.

           SON PROPRE SHADER, ET NON UNE StandardMaterial3D, depuis que le fond
           porte les caustiques : la lumière rassemblée par la houle MULTIPLIE ce
           qui repart du sable, et une seconde passe multiplicative n'est pas
           dessinée sous Forward+ (mesuré ; voir land.gdshader). Il ne fait rien
           d'autre que ce que faisait la matière standard — l'albédo des sommets,
           mat, éclairé par le moteur. */
        _mat = new ShaderMaterial
        {
            Shader = GD.Load<Shader>("res://shaders/land.gdshader"),
            // l'air devant tout le reste, la MÊME passe que la coque porte
            NextPass = HazePass.New()
        };
        Ground = _mat;
        Hazed.Add((ShaderMaterial)_mat.NextPass);

        // les cadrans une fois pour toutes ; le spectre et le soleil, à chaque image
        _mat.SetShaderParameter("u_caustic_gain", CausticRules.Enabled ? (float)CausticRules.Gain : 0f);
        _mat.SetShaderParameter("u_caustic_depth", (float)CausticRules.Depth);
        _mat.SetShaderParameter("u_caustic_far", (float)CausticRules.Far);
        _mat.SetShaderParameter("u_caustic_floor", (float)CausticRules.Floor);
        _mat.SetShaderParameter("u_caustic_spread", (float)CausticRules.Spread);
        /* LES MATIÈRES (world/materiaux.json) : leurs teintes, celles des sommets que les
           carreaux vont prendre, et leurs textures s'il y en a — le sol peint, les fonds et
           la terre de elle-même les lisent toutes. Une seule définition, le fichier. */
        Sand = GroundMaterials.Sand; Grass = GroundMaterials.Grass; Wood = GroundMaterials.Wood;
        Rock = GroundMaterials.Rock; Cobble = GroundMaterials.Cobble;
        Paved = new Color(Cobble.R, Cobble.G, Cobble.B, 0.5f);
        GroundMaterials.Apply(_mat);
        // le partage de la terre par la hauteur, le même que Tint
        _mat.SetShaderParameter("u_sand_top", (float)SandTop);
        _mat.SetShaderParameter("u_grass_from", (float)GrassFrom);
        /* LES RÉCIFS, les mêmes ellipses que le noyau relève (Reef) : le fond les
           peint de corail. Seize au plus, ceux de la région chargée. */
        if (Config.Reefs && World.ReefList.Count > 0)
        {
            int n = Math.Min(16, World.ReefList.Count);
            var ra = new Vector4[16]; var rb = new Vector4[16];
            for (int i = 0; i < n; i++)
            {
                var r = World.ReefList[i];
                ra[i] = new Vector4((float)r.X, (float)r.Z, (float)r.Ux, (float)r.Uz);
                rb[i] = new Vector4((float)r.HalfL, (float)r.HalfW, (float)r.Spec.Edge, (float)r.Phase);
            }
            _mat.SetShaderParameter("u_reef_a", ra);
            _mat.SetShaderParameter("u_reef_b", rb);
            _mat.SetShaderParameter("u_reef_n", n);
            if (World.ReefList.Count > 16) GD.PushWarning($"[monde] {World.ReefList.Count} récifs : seuls les seize premiers sont peints");
        }

    }

    /* Les teintes de la terre, LINÉAIRES (les couleurs de sommet de Godot le sont : sans la
       conversion, la côte sortirait délavée). Elles viennent de world/materiaux.json
       (GroundMaterials), posées à _Ready avant le premier carreau. Le pavé des rues porte
       un alpha de 0,5 : la marque que lit land.gdshader quand les matières sont texturées. */
    static Color Sand, Grass, Wood, Rock, Cobble, Paved;

    const double SandTop = 0.6, GrassFrom = 2.0;

    static Color Tint(double h)
    {
        /* LA GRÈVE S'ARRÊTE À LA LAISSE : du sable jusqu'à 0,6 m, l'herbe franche
           à 2. Le fondu montait jusqu'à 18 m, et Port-Royal restait une ville posée
           sur une plage (signalé). Le sable d'une côte caraïbe tient la bande que la
           mer remue ; au-dessus, la végétation prend. La grève de Port-Royal monte
           de 0 à 4,5 m en soixante mètres : 3,5 laissait encore son premier rang de
           maisons dans le jaune, 2 lui donne vingt-cinq mètres de sable. */
        if (h < SandTop) return Sand;
        if (h < GrassFrom) return Sand.Lerp(Grass, (float)((h - SandTop) / (GrassFrom - SandTop)));
        if (h < 18) return Grass;
        if (h < 120) return Grass.Lerp(Wood, (float)((h - 18) / 102));
        if (h < 700) return Wood.Lerp(Rock, (float)(Math.Min(1, (h - 120) / 580) * 0.7));
        return Wood.Lerp(Rock, (float)(0.7 + 0.3 * Math.Min(1, (h - 700) / 600)));
    }

    /// <summary>Ce carreau porte-t-il quelque chose qui vaille d'être dessiné ? Un regard grossier, une fois.</summary>
    bool Worth(int i, int j)
    {
        for (int a = 0; a <= 8; a++)
            for (int b = 0; b <= 8; b++)
                if (World.HeightAt((i + a / 8.0) * Tile, (j + b / 8.0) * Tile) > -20) return true;
        return false;
    }

    MeshInstance3D Build(int i, int j, int n)
    {
        // chaque carreau compte, ceux de l'arrivée comme ceux qui se bâtissent en route
        using var cost = MapCost.Time("terre");
        var pos = new List<Vector3>((n + 1) * (n + 1));
        var col = new List<Color>(pos.Capacity);
        var idx = new List<int>(n * n * 6);
        double x0 = i * Tile, z0 = j * Tile;
        for (int b = 0; b <= n; b++)
            for (int a = 0; a <= n; a++)
            {
                double x = (double)a / n * Tile, z = (double)b / n * Tile;
                double h = World.HeightAt(x0 + x, z0 + z);
                pos.Add(new Vector3((float)x, (float)h, (float)z));
                // une rue pavée, au-dessus de la laisse : la grille du centre-ville
                col.Add(h >= SandTop && World.PavedAt(x0 + x, z0 + z) ? Paved : Tint(h));
            }
        for (int b = 0; b < n; b++)
            for (int a = 0; a < n; a++)
            {
                int k = b * (n + 1) + a;
                idx.Add(k); idx.Add(k + n + 1); idx.Add(k + n + 2);
                idx.Add(k); idx.Add(k + n + 2); idx.Add(k + 1);
            }

        /* PAS DE JUPE, et c'est un écart ASSUMÉ avec la page — qui en avait une,
           et qui vient d'en être débarrassée elle aussi.

           Un rideau qui pend sous l'arête d'un carreau ne peut pas ne pas se
           voir : vu depuis le bas d'une pente, il masque le pied du versant d'en
           face sur toute sa hauteur. Trente mètres à trois kilomètres font six
           pixels, et cela donnait un réseau de rubans en travers du paysage —
           signalé à l'écran, puis identifié en peignant la jupe en rouge.

           Elle ne servait d'ailleurs qu'aux fentes entre un carreau fin et un
           grossier, or cette frontière est à 4,5 km, où la brume éteint déjà
           98 % du contraste (1 − exp(−0,00085 × 4500)). Rien ne s'y voit, pas
           même une fente. Deux carreaux de MÊME finesse, eux, partagent leur
           arête au bit près : il n'y a jamais eu de fente entre eux. */

        /* Les NORMALES calculées ici, et par la fonction du noyau : SurfaceTool
           les aurait faites aussi, mais son aller-retour perdait les couleurs de
           sommet en chemin — une côte entièrement blanche, vue à la capture. Un
           maillage livré complet d'un coup ne peut pas perdre un attribut. */
        var vert = new float[pos.Count * 3];
        for (int k = 0; k < pos.Count; k++)
        {
            vert[k * 3] = pos[k].X; vert[k * 3 + 1] = pos[k].Y; vert[k * 3 + 2] = pos[k].Z;
        }
        var nrm = new float[vert.Length];
        var tri = idx.ToArray();
        NavalSim.Core.SailCloth.ComputeNormals(vert, nrm, tri);

        /* LE SENS DES TRIANGLES, RETOURNÉ — la même règle que la toile et les
           pavillons, et la côte était la seule à l'avoir manquée : three.js tient
           pour face avant le sens direct, Godot le sens HORAIRE. Godot voyait
           donc tout le relief par l'envers et le supprimait dès qu'on le
           regardait d'en haut : il ne restait qu'une mer plate avec des maisons
           posées dessus, ce qui se lisait comme une terre noyée. Les normales
           sont calculées AVANT l'échange — c'est la même surface, on ne fait que
           dire à Godot de quel côté elle regarde. */
        for (int t = 0; t + 2 < tri.Length; t += 3) (tri[t + 1], tri[t + 2]) = (tri[t + 2], tri[t + 1]);
        var normals = new Vector3[pos.Count];
        for (int k = 0; k < pos.Count; k++)
            normals[k] = new Vector3(nrm[k * 3], nrm[k * 3 + 1], nrm[k * 3 + 2]);

        var arr = new Godot.Collections.Array();
        arr.Resize((int)Mesh.ArrayType.Max);
        arr[(int)Mesh.ArrayType.Vertex] = pos.ToArray();
        arr[(int)Mesh.ArrayType.Normal] = normals;
        arr[(int)Mesh.ArrayType.Color] = col.ToArray();
        arr[(int)Mesh.ArrayType.Index] = tri;
        var final = new ArrayMesh();
        final.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arr);

        var mi = new MeshInstance3D
        {
            Mesh = final,
            MaterialOverride = _mat,
            // l'ombre portée d'une côte ne vaut pas sa carte d'ombres
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off
        };
        AddChild(mi);
        return mi;
    }

    /// <summary>
    /// Montrer ce qui est à portée, cacher le reste, et tout poser contre
    /// l'origine du moment. <paramref name="centre"/> est sa position VRAIE.
    /// </summary>
    /// <summary>
    /// LES MODÈLES QUE LA FICHE POSE — world/caraibes.json → assets : un fort, un
    /// phare, un cocotier. La page les plaçait depuis toujours (land.js →
    /// loadAssets) ; le portage ne les voyait pas, si bien qu'un objet ajouté au
    /// monde n'existait que d'un côté. C'était le genre de trou qu'on ne trouve
    /// qu'en s'en servant.
    ///
    /// Bâtis UNE FOIS, et replacés à chaque image comme les carreaux : ils vivent
    /// en mètres VRAIS et sont posés à <c>lieu − origine</c>, donc rien ici n'a
    /// de Rebase. Leur hauteur est celle du relief sous eux, sauf si la fiche la
    /// donne — un fort se pose sur sa colline, un feu sur son rocher.
    /// </summary>
    readonly PlacedSet _assets = new();
    static readonly StringName PaintRect = "u_paint_rect";
    static readonly StringName UTrue = "u_true";

    /// <summary>Le registre de l'éditeur : chaque modèle posé et chaque copie d'un semis s'y inscrit.</summary>
    public EditRegistry? Editor;

    /// <summary>Le sol peint à la main, s'il y en a un (GroundPaint, ground_paint.gdshaderinc).</summary>
    public GroundPaint? Paint
    {
        get => _paint;
        set
        {
            _paint = value;
            _mat.SetShaderParameter("u_paint_on", value != null ? 1f : 0f);
            if (value != null)
            {
                _mat.SetShaderParameter("u_paint", value.Textures[0]);
                _mat.SetShaderParameter("u_paint2", value.Textures[1]);
            }
        }
    }
    GroundPaint? _paint;

    /// <summary>
    /// INSCRIRE UN OBJET DE LA TERRE à l'éditeur. Il vit sur son nœud <c>hold</c>,
    /// posé d'après sa place vraie (<see cref="Placed"/>) : on change la place, le
    /// cap (par rapport à celui de sa pose) et l'échelle. Sa hauteur suit le sol à
    /// sa nouvelle place, à ce qu'il était levé ou enfoncé près.
    /// </summary>
    /// <summary>
    /// LE ROCHER D'UN ÉCUEIL, tenu à sa place vue : posé, déplacé, remis à l'échelle,
    /// retiré, rendu — chaque geste de l'éditeur le dit au solveur. Son pied est
    /// celui de l'objet (GroundY), son sommet à sa hauteur de rocher au-dessus.
    /// </summary>
    public static void PushRock(World w, Editable ed)
    {
        if (ed.HazardR <= 0) return;
        if (ed.Removed) { if (ed.Rock != null) w.Rocks.Remove(ed.Rock); return; }
        double r = ed.HazardR * ed.Scale, top = ed.GroundY + ed.HazardH * ed.Scale;
        if (ed.Rock == null) ed.Rock = w.Rocks.Add(ed.X, ed.Z, r, ed.GroundY - 0.3, top);
        else { ed.Rock.R = r; w.Rocks.Move(ed.Rock, ed.X, ed.Z, ed.GroundY - 0.3, top); }
    }

    void Register(Placed pl, string id, string label, string family, double x, double z, double yaw, double wy, double radius, double height,
                  double hazardR = 0, double hazardH = 0)
    {
        var hold = pl.Node;
        double lift = wy - World.HeightAt(x, z);
        var e = new Editable
        {
            Id = id, Label = label, BaseX = x, BaseZ = z, BaseYaw = yaw, X = x, Z = z, Yaw = yaw,
            Radius = radius, Height = height, HazardR = hazardR, HazardH = hazardH,
            Family = family, FamilyLabel = label,
            // la copie : ce que porte le nœud, déjà tourné de son cap et mis à sa taille
            MakeVisual = () => (Node3D)hold.GetChild(0).Duplicate(), VisualYaw = yaw, Lift = lift
        };
        e.Push = ed =>
        {
            double ground = World.HeightAt(ed.X, ed.Z);
            pl.X = ed.X; pl.Z = ed.Z;
            pl.Y = (ed.Moved ? ground + lift : wy) + ed.Dy;
            _assets.Moved(pl);
            hold.Rotation = new Vector3(0, (float)(ed.Yaw - ed.BaseYaw), 0);
            hold.Scale = Vector3.One * (float)ed.Scale;
            hold.Visible = !ed.Removed;
            ed.GroundY = (ed.Moved ? ground : wy - lift) + ed.Dy;
            PushRock(World, ed);
        };
        if (Editor != null) Editor.Add(e); else e.Push(e);
    }

    /// <summary>La boîte d'un modèle, dans son propre repère.</summary>
    void BuildAssets()
    {
        int ai = 0;
        foreach (var a in World.Region.Assets)
        {
            int index = ai++;
            string path = Assets.Path(a.Glb);
            if (!System.IO.File.Exists(path)) { GD.PushWarning($"[monde] modèle introuvable : {a.Glb}"); continue; }
            if (Assets.LoadGlb(path) is not Node3D root)
            { GD.PushWarning($"[monde] {a.Glb} illisible"); continue; }

            {
                // sa version lointaine, s'il en a une : au-delà de quatre fois sa taille
                var bb0 = NodeWalk.Bounds(root) ?? new Aabb();
                ShipDemo.LodSay(a.Glb, root, 4 * Math.Max(bb0.Size.X, Math.Max(bb0.Size.Y, bb0.Size.Z)) * a.Scale);
            }
            var g = World.Geo.ToXZ(a.Lat, a.Lon);
            var hold = new Node3D { Name = a.Name.Length > 0 ? a.Name : "asset" };
            hold.AddChild(root);
            root.Scale = Vector3.One * (float)a.Scale;
            root.Rotation = new Vector3(0, (float)(-a.Yaw * Math.PI / 180), 0);
            AddChild(hold);
            /* SA PLACE VRAIE est gardée à côté du nœud : on ne peut pas la relire du
               monde à chaque image sans repayer la lecture du relief. */
            var pl = _assets.Add(hold, g.X, a.Y ?? World.HeightAt(g.X, g.Z), g.Z);
            // l'air devant tout le reste, la MÊME passe que la coque porte
            var haze = HazePass.New();
            Hazed.Add(haze);
            foreach (var mi in NodeWalk.Meshes(root)) HazePass.Wear(mi, haze, vertexColour: true);
            var ab = NodeWalk.Bounds(root) ?? new Aabb();
            double sc = a.Scale;
            Register(pl, $"modele:{index}:{a.Name}", a.Name.Length > 0 ? a.Name : "un modèle posé", "modele:" + a.Glb,
                g.X, g.Z, -a.Yaw * Math.PI / 180, pl.Y,
                0.5 * Math.Max(ab.Size.X, ab.Size.Z) * sc, ab.End.Y * sc);
        }
        if (_assets.Count > 0) GD.Print($"monde : {_assets.Count} modèle(s) posé(s)");
    }

    /// <summary>
    /// LES SEMIS — world/*.json → semis : un modèle répandu sur une zone (les
    /// rochers d'une plage). Les places viennent du noyau (Scatter.Place), tirées
    /// sur une graine ; chaque copie est RAMENÉE à sa taille en mètres quelle que
    /// soit l'échelle du .glb, recentrée sur sa boîte (on ne sait pas où Blender a
    /// laissé l'origine), tournée, penchée, et enfoncée d'une part de sa
    /// demi-hauteur : un rocher sort du sable, il n'y est pas posé.
    ///
    /// Placées comme les modèles posés — en mètres vrais, posées à lieu − origine
    /// à chaque image —, et plus dessinées au-delà de neuf cents mètres : un rocher
    /// de deux mètres y tient en trois pixels.
    /// </summary>
    void BuildScatter()
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        int total = 0;
        foreach (var sp in World.Region.Scatters)
        {
            string path = Assets.Path(sp.Glb);
            if (!System.IO.File.Exists(path)) { GD.PushWarning($"[monde] semis « {sp.Name} » : {sp.Glb} introuvable"); continue; }
            if (Assets.LoadGlb(path) is not Node3D root)
            { GD.PushWarning($"[monde] semis « {sp.Name} » : {sp.Glb} illisible"); continue; }

            // sa boîte, dans son propre repère
            Aabb? box = NodeWalk.Bounds(root);
            if (box is not Aabb bb || bb.Size.Length() < 1e-5f) continue;
            float ext = Math.Max(bb.Size.X, Math.Max(bb.Size.Y, bb.Size.Z));
            var centre = bb.GetCenter();

            // la brume, une passe pour toutes les copies (elles partagent leurs matières)
            var haze = HazePass.New();
            Hazed.Add(haze);
            foreach (var mi in NodeWalk.Meshes(root)) HazePass.Wear(mi, haze);

            /* PAS DE CAUSTIQUES SUR LE RÉCIF, et c'est mesuré : la passe des carènes
               (ShipNode.CausticPass) accrochée aux foules du fond faisait passer une vue
               de récif de 7,4 à 13,6 ms par image — elle recalcule la houle à chaque
               pixel d'un corail, et un récif en couvre l'écran. Sans elle : 7,5 ms. */
            if (sp.Crowd) { total += BuildCrowd(sp, root, bb, ext); continue; }

            int pi = 0;
            foreach (var p in Scatter.Place(World, sp))
            {
                int index = pi++;
                float k = (float)(p.Size / ext);
                var hold = new Node3D { Name = sp.Name };
                var copy = (Node3D)root.Duplicate();
                copy.Scale = Vector3.One * k;
                copy.Position = -centre * k;
                var tilt = new Node3D
                {
                    Basis = new Basis(Vector3.Up, (float)p.Yaw) * new Basis(Vector3.Right, (float)p.TiltX) * new Basis(Vector3.Back, (float)p.TiltZ)
                };
                tilt.AddChild(copy);
                hold.AddChild(tilt);
                foreach (var mi in NodeWalk.Meshes(copy)) { mi.VisibilityRangeEnd = (float)sp.Visible; mi.VisibilityRangeEndMargin = (float)(sp.Visible * 0.07); }
                AddChild(hold);
                // enfoncé d'une part de sa demi-hauteur (« enfonce » : 0,4 pour un rocher)
                var pl = _assets.Add(hold, p.X, World.HeightAt(p.X, p.Z) + bb.Size.Y * k * 0.5 * (1 - sp.Sink), p.Z);
                // le centre de la copie est à pl.Y : son pied, une demi-hauteur plus bas
                // un écueil : un peu en dedans de sa boîte (un rocher n'est pas un pavé), et
                // haut de ce qui sort du fond — sa hauteur, moins la part enfoncée
                Register(pl, $"semis:{sp.Name}:{index}", sp.Name, "semis:" + sp.Name, p.X, p.Z, p.Yaw, pl.Y,
                    p.Size * 0.5, bb.Size.Y * k,
                    sp.Hazard ? Math.Max(bb.Size.X, bb.Size.Z) * k * 0.5 * 0.85 : 0, bb.Size.Y * k * (1 - 0.5 * sp.Sink));
                total++;
            }
        }
        if (total > 0) GD.Print($"monde : {total} élément(s) semé(s) en {clock.ElapsedMilliseconds} ms, dont {World.Rocks.Count} écueil(s)");
    }

    /// <summary>La case d'une foule, en mètres : chacune est un paquet, dessiné ou non selon sa distance.</summary>
    const double CrowdCell = 48;

    /// <summary>Pour l'essai (--fond) : la case la plus peuplée de chaque foule, en mètres vrais.</summary>
    readonly Dictionary<string, (double X, double Z, int N)> _crowdBest = new();

    /// <summary>La case la plus peuplée d'une foule dont le nom contient ce mot, en mètres vrais ; nulle sinon.</summary>
    public (double X, double Z)? CrowdSpot(string word)
    {
        foreach (var (name, b) in _crowdBest)
            if (name.Contains(word, StringComparison.OrdinalIgnoreCase)) return (b.X, b.Z);
        return null;
    }

    /// <summary>
    /// UNE FOULE — des milliers de petites pièces (le corail d'un récif, l'herbier) :
    /// une pièce par nœud coûterait un nœud et un appel de dessin chacune. On les
    /// range par cases de quarante-huit mètres, une MultiMesh par case et par
    /// maillage du modèle, chaque case posée en mètres vrais comme un modèle et
    /// effacée au-delà de « visible ». Mêmes tailles, mêmes penchés, même
    /// enfoncement que le semis pièce à pièce ; mais rien à reprendre en mode
    /// création, pièce par pièce.
    /// </summary>
    string? _regionPrint;

    int BuildCrowd(ScatterSpec sp, Node3D root, Aabb bb, float ext)
    {
        var centre = bb.GetCenter();
        // chaque maillage du modèle, et où il est dans le modèle
        var parts = new List<(Mesh Mesh, Transform3D Local)>();
        foreach (var mi in NodeWalk.Meshes(root))
        {
            var t = Transform3D.Identity;
            for (Node? up = mi; up != null && up != root; up = up.GetParent()) if (up is Node3D n3) t = n3.Transform * t;
            var mesh = (Mesh)mi.Mesh.Duplicate();
            if (mesh is ArrayMesh am)
                for (int s = 0; s < am.GetSurfaceCount(); s++)
                    am.SurfaceSetMaterial(s, sp.Sway > 0 ? Swayer(mi.GetActiveMaterial(s), mesh.GetAabb(), sp.Sway) : mi.GetActiveMaterial(s));
            parts.Add((mesh, t));
        }
        if (parts.Count == 0) return 0;

        /* LA CASE VA COMME LA PORTÉE : chaque case est un appel de dessin par matière, et
           des arbres vus à deux kilomètres en cases de quarante-huit mètres en feraient des
           milliers. Un sixième de la portée, entre 48 et 400 m. */
        double cellSz = Math.Clamp(sp.Visible / 6, CrowdCell, 400);
        var clock = System.Diagnostics.Stopwatch.StartNew();
        // relues du disque quand rien n'a changé (ScatterCache)
        _regionPrint ??= ScatterCache.RegionPrint(World);
        var placed = ScatterCache.Places(World, sp, _regionPrint, out bool cached);
        long placeMs = clock.ElapsedMilliseconds;
        /* ET D'AU MOINS UNE DOUZAINE DE PIÈCES PAR CASE en moyenne : semée sur toute une île,
           une espèce clairsemée occupait une case par arbre — des dizaines de milliers de
           nœuds. On grossit la case (jusqu'à 700 m) tant qu'elle reste trop vide. */
        for (int guard = 0; guard < 6 && placed.Count > 0 && cellSz < 700; guard++)
        {
            var occ = new HashSet<(int, int)>();
            foreach (var p in placed) occ.Add(((int)Math.Floor(p.X / cellSz), (int)Math.Floor(p.Z / cellSz)));
            if (placed.Count >= 12 * occ.Count) break;
            cellSz = Math.Min(700, cellSz * 1.5);
        }
        var cells = new Dictionary<(int, int), List<Transform3D>>();
        // et chaque pièce retenue, pour la reposer si le sol bouge sous elle (Reseat)
        var seats = new Dictionary<(int, int), List<(double X, double Z, double Lift)>>();
        int n = 0;
        foreach (var p in placed)
        {
            float k = (float)(p.Size / ext);
            double lift = bb.Size.Y * k * 0.5 * (1 - sp.Sink);
            double y = World.HeightAt(p.X, p.Z) + lift;
            var key = ((int)Math.Floor(p.X / cellSz), (int)Math.Floor(p.Z / cellSz));
            double cx = (key.Item1 + 0.5) * cellSz, cz = (key.Item2 + 0.5) * cellSz;
            var basis = new Basis(Vector3.Up, (float)p.Yaw) * new Basis(Vector3.Right, (float)p.TiltX) * new Basis(Vector3.Back, (float)p.TiltZ);
            // comme le semis pièce à pièce : ramené à sa taille, recentré sur sa boîte
            var t = new Transform3D(basis, new Vector3((float)(p.X - cx), (float)y, (float)(p.Z - cz)))
                  * new Transform3D(Basis.FromScale(Vector3.One * k), -centre * k);
            if (!cells.TryGetValue(key, out var list)) cells[key] = list = new List<Transform3D>();
            list.Add(t);
            if (!seats.TryGetValue(key, out var sl)) seats[key] = sl = new();
            sl.Add((p.X, p.Z, lift));
            n++;
            // un écueil de la foule : pas de retouche, donc inscrit une fois pour toutes
            if (sp.Hazard)
            {
                double bed = World.HeightAt(p.X, p.Z);
                World.Rocks.Add(p.X, p.Z, Math.Max(bb.Size.X, bb.Size.Z) * k * 0.5 * 0.85, bed - 0.3, bed + bb.Size.Y * k * (1 - 0.5 * sp.Sink));
            }
        }
        foreach (var (key, list) in cells)
        {
            if (!_crowdBest.TryGetValue(sp.Name, out var best) || list.Count > best.N)
                _crowdBest[sp.Name] = ((key.Item1 + 0.5) * cellSz, (key.Item2 + 0.5) * cellSz, list.Count);
            var hold = new Node3D { Name = sp.Name };
            var crowd = new CrowdPatch
            {
                X0 = key.Item1 * cellSz, Z0 = key.Item2 * cellSz, Size = cellSz, Seats = seats[key], Ts = list
            };
            _crowds.Add(crowd);
            foreach (var (mesh, local) in parts)
            {
                var mm = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, Mesh = mesh, InstanceCount = list.Count };
                crowd.Parts.Add((mm, local));
                for (int i = 0; i < list.Count; i++) mm.SetInstanceTransform(i, list[i] * local);
                hold.AddChild(new MultiMeshInstance3D
                {
                    Multimesh = mm,
                    // le fond n'a pas d'ombre à porter qui se verrait (la lumière y est déjà diffuse) ; un arbre, si : « ombre »
                    CastShadow = sp.Shadow ? GeometryInstance3D.ShadowCastingSetting.On : GeometryInstance3D.ShadowCastingSetting.Off,
                    VisibilityRangeEnd = (float)(sp.Visible + cellSz * 0.71),
                    VisibilityRangeEndMargin = (float)(sp.Visible * 0.1)
                });
            }
            AddChild(hold);
            _assets.Add(hold, (key.Item1 + 0.5) * cellSz, 0, (key.Item2 + 0.5) * cellSz);
        }
        GD.Print($"monde : « {sp.Name} », {n} pièce(s) en {cells.Count} case(s) — {(cached ? "relues du cache" : "semées")} en {placeMs} ms, bâties en {clock.ElapsedMilliseconds - placeMs} ms");
        return n;
    }

    /// <summary>
    /// La matière qui ondule, à la couleur et au grain de celle du modèle. La brume
    /// est dedans : une passe redessinerait la géométrie immobile par-dessus.
    /// </summary>
    ShaderMaterial Swayer(Material? from, Aabb box, double bend)
    {
        var col = from is BaseMaterial3D bm ? bm.AlbedoColor : new Color(0.2f, 0.3f, 0.1f);
        var m = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/seaweed.gdshader") };
        m.SetShaderParameter("u_albedo", new Vector3(col.R, col.G, col.B));
        m.SetShaderParameter("u_roughness", from is BaseMaterial3D b2 ? b2.Roughness : 0.8f);
        m.SetShaderParameter("u_base_y", box.Position.Y);
        m.SetShaderParameter("u_height", Math.Max(box.Size.Y, 1e-3f));
        m.SetShaderParameter("u_bend", (float)bend);
        Swaying.Add(m);
        return m;
    }

    public void Update(Vec3d centre, Vec3d origin, bool eager = false)
    {
        if (!_assetsBuilt)
        {
            _assetsBuilt = true;
            using (MapCost.Time("modèles")) BuildAssets();
            using (MapCost.Time("végétation")) BuildScatter();
        }
        // le fond ancré au monde : ses motifs se calculent en mètres vrais
        _mat.SetShaderParameter(UTrue, new Vector2((float)origin.X, (float)origin.Z));
        // le carré peint, contre l'origine du moment : son coin est en mètres vrais
        if (_paint != null)
            _mat.SetShaderParameter(PaintRect, new Vector4((float)(_paint.X0 - origin.X), (float)(_paint.Z0 - origin.Z),
                                                                (float)(1 / _paint.Side), (float)(1 / _paint.Side)));
        // replacés seulement quand l'origine a glissé : ils ne bougent pas d'eux-mêmes
        _assets.Update(origin);

        int i0 = (int)Math.Floor((centre.X - Range) / Tile), i1 = (int)Math.Floor((centre.X + Range) / Tile);
        int j0 = (int)Math.Floor((centre.Z - Range) / Tile), j1 = (int)Math.Floor((centre.Z + Range) / Tile);
        _seen.Clear();
        int built = 0;
        for (int j = j0; j <= j1; j++)
            for (int i = i0; i <= i1; i++)
            {
                double cx = (i + 0.5) * Tile, cz = (j + 0.5) * Tile;
                double d = Math.Sqrt((cx - centre.X) * (cx - centre.X) + (cz - centre.Z) * (cz - centre.Z));
                if (d > Range + Tile * 0.71) continue;
                if (!_tiles.TryGetValue((i, j), out var t))
                    _tiles[(i, j)] = t = new Patch { Has = Worth(i, j) };
                if (!t.Has) continue;

                bool fine = d < NearRange + Tile * 0.71;
                /* Quelques-uns par image au plus — vingt carreaux dans la même
                   image font un à-coup que l'œil attrape —, sauf quand on demande
                   d'être PRESSÉ, au départ, pour qu'elle ne sorte pas d'un port
                   dont la rive d'en face est encore à venir. */
                if (fine && t.Near == null)
                {
                    if (!eager && built++ > 3) continue;
                    /* AUSSI FIN QUE LA SOURCE, et pas plus : un carreau bâti plus
                       fin que le relief qui le nourrit n'ajoute que des triangles
                       qui interpolent. Un relief LOCAL (un port modelé à la main)
                       vaut donc des carreaux serrés, bornés à FineMax pour qu'une
                       rade ne coûte pas un million de sommets. */
                    double px = World.ReliefPx(i * Tile, j * Tile, (i + 1) * Tile, (j + 1) * Tile);
                    int n = (int)Math.Round(Tile / px);
                    // terrassé : deux fois plus fin, et en morceaux qu'un coup de pinceau rebâtit seuls
                    t.Near = Terraced(i, j) ? BuildTerraced(i, j, FineMax * 2) : Build(i, j, Math.Clamp(n, 8, FineMax));
                }
                if (!fine && t.Far == null)
                {
                    if (!eager && built++ > 3) continue;
                    t.Far = Build(i, j, Math.Max(4, (int)Math.Round(Tile / World.Px / 4)));
                }
                var show = fine ? t.Near : t.Far;
                var hide = fine ? t.Far : t.Near;
                if (hide != null) hide.Visible = false;
                if (show == null) continue;
                show.Visible = true;
                show.Position = new Vector3((float)(i * Tile - origin.X), 0, (float)(j * Tile - origin.Z));
                _seen.Add(show);
            }
        foreach (var t in _tiles.Values)
        {
            if (t.Near != null && !_seen.Contains(t.Near)) t.Near.Visible = false;
            if (t.Far != null && !_seen.Contains(t.Far)) t.Far.Visible = false;
        }
    }
}
