using Godot;
using System;
using System.Collections.Generic;
using NavalSim.Core;

namespace NavalSim;

/// <summary>
/// Une coque à l'écran, et le pont entre le solveur et le graphe de scène.
///
/// ELLE NE DÉCIDE DE RIEN. Le solveur vit dans le noyau, en doubles, sans
/// connaître Godot ; ce nœud lit <c>Body.Pos</c> et <c>Body.Quat</c> une fois par
/// image et les recopie dans une transformée. C'est la FRONTIÈRE dont il a été
/// question depuis le début : le seul endroit où l'on passe du double au
/// flottant 32 bits, une fois par image et par objet, là où cela ne coûte rien.
///
/// Le maillage sort du même <see cref="HullLines"/> que la grille de sondes, ce
/// qui est l'invariant le plus important du projet : ce qu'on voit est ce qui
/// flotte.
/// </summary>
public partial class ShipNode : Node3D
{
    public ShipSpec Spec { get; private set; } = null!;
    public HullLines Lines { get; private set; } = null!;
    public ShipPhysics Physics { get; private set; } = null!;
    public Controls Ctrl { get; } = new();

    MeshInstance3D _hull = null!;
    Node3D _rig = null!;

    /// <summary>
    /// Les matériaux qui doivent respirer le même air que la mer. Rendus à
    /// l'appelant pour que <see cref="SkyNode.PushTo"/> leur pousse la brume et le
    /// gros temps — un navire qui resterait net contre une mer délavée ferait
    /// s'effondrer l'illusion.
    /// </summary>
    public readonly List<ShaderMaterial> Hazed = new();

    ShaderMaterial MakeHullMaterial(Color albedo, float roughness)
    {
        var m = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/hull.gdshader") };
        m.SetShaderParameter(U.Albedo, albedo);
        m.SetShaderParameter(U.Roughness, roughness);
        Hazed.Add(m);
        return m;
    }

    public void Build(ShipSpec spec)
    {
        Spec = spec;
        Lines = new HullLines(spec);
        Physics = new ShipPhysics(spec, Lines);

        _hull = new MeshInstance3D
        {
            Mesh = ToArrayMesh(Lines.BuildGeometry()),
            // la coque est une nappe fermée par deux culs : on voit son intérieur
            // quand elle gîte, donc le shader dessine les deux faces
            MaterialOverride = MakeHullMaterial(Hex(spec.Appearance.Hull), 0.72f)
        };
        AddChild(_hull);

        BuildRig();
        LoadModel();
        // les feux après le gréement : la lanterne du grand mât se pend à SON mât
        BuildLanterns();
        FindNightGlow();
    }

    // ------------------------------------------------------------------
    //  LE MODÈLE .glb — ship-model.js, loadModel
    // ------------------------------------------------------------------

    /// <summary>Le modèle adopté, ou <c>null</c> si elle garde sa coque procédurale.</summary>
    public Node3D? ModelRoot { get; private set; }

    /// <summary>
    /// Remplacer la coque procédurale par le modèle de la fiche. Rend <c>true</c>
    /// s'il est adopté, <c>false</c> si elle garde la sienne — et ne lève JAMAIS :
    /// un modèle absent ne doit pas emporter la simulation avec lui.
    ///
    /// LE .glb NE PORTE QUE L'APPARENCE. La flottaison vient toujours des cotes
    /// de la fiche : on ne tire pas un volume de carène fiable d'un maillage
    /// quelconque sans une voxélisation coûteuse. Les sondes ne changent donc pas
    /// d'un iota ; seul change ce qu'on voit.
    ///
    /// Chargé à l'EXÉCUTION par GltfDocument, et non importé par l'éditeur : les
    /// fiches et leurs modèles vivent hors du projet Godot, dans ships/, où la
    /// page d'origine les lit aussi. Un seul dossier pour les deux versions.
    /// </summary>
    bool LoadModel()
    {
        var m = Spec.Model;
        if (m == null || string.IsNullOrEmpty(m.Glb)) return false;
        string root = System.IO.Path.GetDirectoryName(
            ShipLibrary.Folder.TrimEnd('/', '\\')) ?? "";
        string path = System.IO.Path.Combine(root, m.Glb);
        try
        {
            var doc = new GltfDocument();
            var state = new GltfState();
            Error err = doc.AppendFromFile(path, state);
            if (err != Error.Ok || doc.GenerateScene(state) is not Node3D obj)
            {
                GD.PushWarning($"[{Spec.Id}] impossible de charger {m.Glb} ({err}) — "
                             + "elle garde sa coque procédurale.");
                return false;
            }

            SplitPrimitives(obj);
            ReliefFromRoughness(obj, m.Relief ?? 0);

            // sa COQUE ramenée à la longueur que le solveur fait flotter
            double k = m.Scale ?? HullScale(obj, m.LengthAxis);
            obj.Scale = Vector3.One * (float)k;
            obj.Rotation = new Vector3(0, (float)m.RotationY, 0);
            var off = m.Offset;
            obj.Position = new Vector3((float)off[0], (float)off[1], (float)off[2]);

            /* La coque et le gréement procéduraux s'en vont, avec leur brume et
               leur toile. RETIRÉS de l'arbre et pas seulement libérés : un nœud
               libéré reste enfant jusqu'à la fin de l'image, et le parcours qui
               pose la brume ci-dessous l'aurait encore trouvé. */
            RemoveChild(_hull); _hull.QueueFree();
            RemoveChild(_rig); _rig.QueueFree();
            Hazed.Clear();
            ClearRig();

            AddChild(obj);
            ModelRoot = obj;
            RigModel();
            // sur tout ce qui est à bord, y compris les espars que RigModel vient
            // de sortir du modèle pour les pendre dans leurs pivots
            AttachHaze(this);
            return true;
        }
        catch (Exception e)
        {
            GD.PushWarning($"[{Spec.Id}] {m.Glb} : {e.Message} — elle garde sa coque procédurale.");
            return false;
        }
    }

    /// <summary>
    /// L'échelle qui ramène la COQUE du modèle à sa longueur annoncée.
    ///
    /// Mesurer l'objet entier comptait son beaupré et ses vergues comme du
    /// navire : le Roter Löwe sortait avec 47,5 m de coque là où le solveur en
    /// faisait flotter 60, et tout ce qui dérive de ses cotes débordait du bois —
    /// le collier d'écume surtout, qui dépassait son étrave et sa poupe de plus
    /// de six mètres. La coque est le maillage le plus VOLUMINEUX : les espars
    /// sont longs mais n'enferment presque rien. Appelée avant toute
    /// transformation posée sur le modèle, donc dans son propre repère.
    /// </summary>
    double HullScale(Node3D obj, string lengthAxis)
    {
        double best = -1, along = 0;
        foreach (var (mi, rel) in Meshes(obj))
        {
            var box = rel * mi.Mesh.GetAabb();
            double vol = (double)box.Size.X * box.Size.Y * box.Size.Z;
            if (vol > best) { best = vol; along = lengthAxis == "x" ? box.Size.X : box.Size.Z; }
        }
        return along > 1e-6 ? Spec.L / along : 1;
    }

    /// <summary>
    /// Chaque maillage sous <paramref name="root"/>, avec la transformée qui le
    /// ramène dans le repère de <paramref name="root"/> (sa propre transformée
    /// comprise). Calculée à la main : le modèle n'est pas encore dans l'arbre,
    /// et une transformée globale n'y voudrait rien dire.
    /// </summary>
    static IEnumerable<(MeshInstance3D, Transform3D)> Meshes(Node3D root)
    {
        var stack = new Stack<(Node, Transform3D)>();
        stack.Push((root, root.Transform));
        while (stack.Count > 0)
        {
            var (n, t) = stack.Pop();
            if (n is MeshInstance3D mi && mi.Mesh != null) yield return (mi, t);
            // enfants empilés à l'envers, pour être rendus DANS L'ORDRE, comme
            // traverse() de three : sur une égalité, le choix de la coque ou d'un
            // mât se fait au premier trouvé
            var kids = n.GetChildren();
            for (int i = kids.Count - 1; i >= 0; i--)
                stack.Push((kids[i], kids[i] is Node3D c3 ? t * c3.Transform : t));
        }
    }

    ShaderMaterial? _hazePass;

    /// <summary>
    /// Poser la brume sur chaque matériau du modèle, en DERNIER de sa chaîne de
    /// passes : c'est l'air devant tout le reste. Une seule passe partagée, que
    /// <see cref="SkyNode.PushTo"/> tient à jour comme les autres.
    ///
    /// Pas sur un matériau à shader : la toile et les espars procéduraux portent
    /// DÉJÀ la brume dans le leur, et une seconde passe la compterait deux fois.
    /// </summary>
    void AttachHaze(Node3D obj)
    {
        _hazePass ??= new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/hull_haze.gdshader") };
        Hazed.Add(_hazePass);
        var done = new HashSet<Material>();
        foreach (var (mi, _) in Meshes(obj))
            for (int s = 0; s < mi.Mesh.GetSurfaceCount(); s++)
            {
                var mat = mi.GetSurfaceOverrideMaterial(s) ?? mi.Mesh.SurfaceGetMaterial(s);
                if (mat is ShaderMaterial || mi.MaterialOverride is ShaderMaterial) continue;
                if (mat == null)
                {
                    mat = new StandardMaterial3D();
                    mi.SetSurfaceOverrideMaterial(s, mat);
                }
                if (!done.Add(mat)) continue;
                var last = mat;
                while (last.NextPass != null && last.NextPass != _hazePass) last = last.NextPass;
                last.NextPass = _hazePass;
            }
    }

    /// <summary>
    /// Son profil de flottaison. Un modèle se mesure sur SON maillage de coque,
    /// puisque c'est la forme que l'œil voit — le lire dans le plan de formes
    /// ferait écumer autour d'un navire qui n'est pas celui qu'on dessine.
    /// <paramref name="waterlineY"/> est la flottaison dans son repère : moins
    /// l'assise que <c>Settle()</c> a trouvée.
    /// </summary>
    public HullProfile MakeProfile(double waterlineY, int n = 64)
    {
        if (ModelRoot == null) return HullProfile.Procedural(Spec, Lines, n);

        // la coque : le plus volumineux, mesuré cette fois dans SON repère à elle
        MeshInstance3D? hull = null;
        Transform3D hullT = Transform3D.Identity;
        double best = -1;
        foreach (var (mi, rel) in Meshes(ModelRoot))
        {
            var box = rel * mi.Mesh.GetAabb();
            double vol = (double)box.Size.X * box.Size.Y * box.Size.Z;
            if (vol > best) { best = vol; hull = mi; hullT = rel; }
        }
        var raw = new float[n];
        if (hull != null)
        {
            double lo = HullProfile.BandLo(Spec, waterlineY), hi = HullProfile.BandHi(Spec, waterlineY);
            for (int s = 0; s < hull.Mesh.GetSurfaceCount(); s++)
            {
                var arrays = hull.Mesh.SurfaceGetArrays(s);
                var verts = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
                foreach (var v in verts)
                {
                    var p = hullT * v;
                    if (p.Y < lo || p.Y > hi) continue;
                    int b = HullProfile.Station(Spec, p.Z, n);
                    float x = Math.Abs(p.X);
                    /* UN SOMMET SUR L'AXE N'APPORTE AUCUNE LARGEUR. Sous three.js son
                       |x| vaut exactement zéro et la station reste vide, donc
                       comblée entre ses voisines ; ici la chaîne de transformées de
                       l'import lui laisse un bruit de flottant d'un centième de
                       millimètre, qui suffisait à la déclarer MESURÉE et à couper
                       le comblement. Relevé sur la bouée canard : l'arrière du
                       corps tombait de −1,14 à −0,89 m. */
                    if (x < 1e-4f) continue;
                    if (x > raw[b]) raw[b] = x;
                }
            }
        }
        return HullProfile.Measured(raw, Spec);
    }

    static ArrayMesh ToArrayMesh(in HullMesh hm)
    {
        var verts = new Vector3[hm.Positions.Length / 3];
        for (int i = 0; i < verts.Length; i++)
            verts[i] = new Vector3(hm.Positions[i * 3], hm.Positions[i * 3 + 1], hm.Positions[i * 3 + 2]);

        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        foreach (int idx in hm.Indices) st.AddVertex(verts[idx]);
        st.GenerateNormals();
        return st.Commit();
    }

    Aabb? _bounds;

    /// <summary>
    /// Tout ce qui est à bord — coque, mâture, toile, feux —, dans le repère du
    /// navire, mesuré une fois. Pour le flou de mouvement, qui doit savoir quels
    /// points bougent AVEC le navire. Un mètre de marge : la toile se gonfle au-delà
    /// de ses vergues, et un point du bord laissé dehors serait flouté comme de
    /// l'eau.
    /// </summary>
    public Aabb LocalBounds()
    {
        if (_bounds is Aabb b) return b;
        Transform3D toLocal = GlobalTransform.AffineInverse();
        Aabb box = default;
        bool any = false;
        var stack = new Stack<Node>();
        stack.Push(this);
        while (stack.Count > 0)
        {
            var n = stack.Pop();
            // pas les lumières : leur « boîte » est leur portée
            if (n is GeometryInstance3D g && g.Visible)
            {
                var gb = toLocal * g.GlobalTransform * g.GetAabb();
                box = any ? box.Merge(gb) : gb;
                any = true;
            }
            foreach (var c in n.GetChildren()) stack.Push(c);
        }
        if (!any) box = new Aabb(new Vector3((float)(-Spec.B), -5, (float)(-Spec.L * 0.5)),
                                 new Vector3((float)(Spec.B * 2), 30, (float)Spec.L));
        /* SYMÉTRIQUE EN TRAVERS : mesurée vergues brassées d'un bord, elle allait de
           −4,8 à +10 m sur le navire 5 ; les vergues passent de l'autre bord au
           virement, et la toile avec. */
        float half = Math.Max(-box.Position.X, box.End.X);
        box = new Aabb(new Vector3(-half, box.Position.Y, box.Position.Z),
                       new Vector3(2 * half, box.Size.Y, box.Size.Z)).Grow(1f);
        _bounds = box;
        return box;
    }

    /// <summary>
    /// LA FRONTIÈRE. Double vers flottant, une fois par image.
    /// </summary>
    public void SyncTransform()
    {
        var b = Physics.Body;
        Position = new Vector3((float)b.Pos.X, (float)b.Pos.Y, (float)b.Pos.Z);
        Quaternion = new Quaternion(
            (float)b.Quat.X, (float)b.Quat.Y, (float)b.Quat.Z, (float)b.Quat.W).Normalized();
    }

    /// <summary>Sa gîte et son assiette, en degrés, lues sur ses propres axes.</summary>
    public (double Heel, double Trim, double Heading) Attitude()
    {
        var q = Physics.Body.Quat;
        Vec3d up = q.Rotate(new Vec3d(0, 1, 0));
        Vec3d fwd = q.Rotate(new Vec3d(0, 0, 1));
        Vec3d right = q.Rotate(new Vec3d(-1, 0, 0));

        /* LA GÎTE SE MESURE CONTRE LA VERTICALE DU MONDE, jamais contre ses
           propres axes — et l'avoir écrite autrement donnait un instrument qui
           affichait zéro quoi qu'elle fasse.

           La faute : `up · right`. Les deux sont tournés par le MÊME quaternion,
           donc ils restent orthonormés et leur produit scalaire vaut zéro par
           construction, pour toute orientation. Le cadran était donc
           mathématiquement incapable d'afficher autre chose, ce qui est pire
           qu'un cadran faux : il a l'air de fonctionner.

           Ce qu'il faut lire est la composante VERTICALE de son vecteur tribord.
           Elle descend quand elle donne de la bande à tribord, et c'est cela que
           l'œil appelle gîter. `atan2` plutôt qu'`asin` pour que la lecture reste
           juste au-delà de quatre-vingt-dix degrés, une coque chavirée n'étant
           pas un cas qu'on peut écarter dans ce projet. */
        double heel = Math.Atan2(-right.Y, up.Y) * 180 / Math.PI;

        // L'assiette, elle, était juste : `fwd.Y` est DÉJÀ pris contre la
        // verticale du monde, puisque c'est sa composante en y.
        double trim = Math.Asin(Math.Clamp(fwd.Y, -1, 1)) * 180 / Math.PI;

        /* LE CAP SE LIT SUR LE VECTEUR D'ÉTRAVE, pas sur l'angle d'Euler. Le
           prendre sur Euler donnait un signe inverse, qui annulait exactement une
           erreur de sens du gouvernail : les deux fautes se masquaient
           mutuellement. Et l'EST EST LE −X du monde, par la même conséquence qui
           met tribord en −x local. */
        double heading = Math.Atan2(-fwd.X, fwd.Z) * 180 / Math.PI;
        if (heading < 0) heading += 360;

        return (heel, trim, heading);
    }
}
