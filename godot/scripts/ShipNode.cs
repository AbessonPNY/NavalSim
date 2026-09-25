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

    /// <summary>
    /// Les matériaux qui portent SA neige : coque et espars procéduraux, toile, et
    /// la passe posée sur un modèle. À elle seule — un navire sorti de la neige la
    /// garde jusqu'à ce qu'elle fonde —, réglée par <see cref="SetSnowCover"/>.
    /// </summary>
    public readonly List<ShaderMaterial> Snowed = new();

    /// <summary>La couche de neige sur elle : 0 nue, vers 1 un manteau sur tout ce qui regarde le ciel.</summary>
    public double SnowCover { get; private set; }
    float _snowPushed = -1;

    public void SetSnowCover(double cover)
    {
        SnowCover = cover;
        // écrite quand elle a bougé d'un rien qu'on puisse voir, pas à chaque image
        if (Math.Abs(cover - _snowPushed) < 1e-4) return;
        _snowPushed = (float)cover;
        foreach (var m in Snowed) m.SetShaderParameter(U.Snow, _snowPushed);
    }

    ShaderMaterial MakeHullMaterial(Color albedo, float roughness)
    {
        var m = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/hull.gdshader") };
        m.SetShaderParameter(U.Albedo, albedo);
        m.SetShaderParameter(U.Roughness, roughness);
        Hazed.Add(m);
        AddSnowed(m);
        AddScarred(m);
        // ce qui est mouillé prend la lumière de l'eau, coque dessinée comprise
        m.NextPass = CausticPass();
        return m;
    }

    // né avec la couche qu'elle porte déjà : une voile refaite sous la neige est blanche aussi
    internal void AddSnowed(ShaderMaterial m)
    {
        Snowed.Add(m);
        if (_snowPushed > 0) m.SetShaderParameter(U.Snow, _snowPushed);
    }

    public void Build(ShipSpec spec)
    {
        Spec = spec;
        Lines = new HullLines(spec);
        Physics = new ShipPhysics(spec, Lines);
        // ses impacts peints, avant qu'aucune matière ne naisse : chacune les reçoit à sa création
        LoadImpactAtlas();

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
        // ses couleurs, une fois ses mâts trouvés : elles se pendent dans leurs chutes
        BuildFlags();
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
    /// page d'origine les lit aussi — et <see cref="Assets.Path"/> laisse Godot
    /// prendre au passage une version plus lourde s'il en existe une.
    /// </summary>
    bool LoadModel()
    {
        var m = Spec.Model;
        if (m == null || string.IsNullOrEmpty(m.Glb)) return false;
        string path = Assets.Path(m.Glb);
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
            // et les tangentes des reliefs que le modèle apporte lui-même
            TangentsForRelief(obj);

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
            Snowed.Clear();
            _scarred.Clear();
            _snowPushed = -1;
            ClearRig();

            AddChild(obj);
            ModelRoot = obj;
            RigModel();
            FindGuns();
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
    /// <summary>
    /// HABILLER LA CARTE VIERGE que le modèle porte sur le bureau du capitaine.
    /// On ne la reconnaît ni à sa forme ni à sa place, mais au NOM que l'artiste
    /// a donné à son matériau — « map_free » —, ce qui la rend indépendante de la
    /// coque : un autre modèle qui nomme ainsi sa feuille l'aura aussi.
    ///
    /// La surface reçoit un override plutôt qu'une retouche du matériau du .glb :
    /// celui-ci reste celui de l'artiste, et rien n'est perdu si la carte est
    /// retirée.
    /// </summary>
    /// <summary>La feuille, une fois trouvée : ce qui permet de s'y pencher dessus.</summary>
    public MeshInstance3D? ChartSurface { get; private set; }

    public bool DressChart(Texture2D tex) => Dress(tex, ChartNode.MapMaterial, s => ChartSurface = s);

    /// <summary>
    /// ET LE JOURNAL DE BORD, par la même règle et pour la même raison : le modèle
    /// qui nomme une surface « journal » (ou « logbook ») porte le livre ouvert du
    /// capitaine sur son bureau. Aucun modèle ne le fait encore ; le jour où l'un
    /// le fera, il n'y aura rien à changer ici.
    /// </summary>
    public bool DressJournal(Texture2D tex) => Dress(tex, "journal", _ => { })
                                            || Dress(tex, "logbook", _ => { });

    bool Dress(Texture2D tex, string nom, Action<MeshInstance3D> keep)
    {
        if (ModelRoot == null) return false;
        bool any = false;
        foreach (var (mi, _) in Meshes(ModelRoot))
            for (int i = 0; i < mi.Mesh.GetSurfaceCount(); i++)
            {
                var m = mi.Mesh.SurfaceGetMaterial(i);
                if (m == null || !m.ResourceName.Contains(nom, StringComparison.OrdinalIgnoreCase))
                    continue;
                mi.SetSurfaceOverrideMaterial(i, new StandardMaterial3D
                {
                    AlbedoTexture = tex,
                    Roughness = 0.92f,
                    // une feuille de papier ne reçoit pas d'ombre portée en relief
                    SpecularMode = BaseMaterial3D.SpecularModeEnum.Disabled
                });
                any = true;
                keep(mi);
            }
        return any;
    }

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

    ShaderMaterial? _hazePass, _snowPass;
    /// <summary>
    /// LA LUMIÈRE DE L'EAU SUR LES CARÈNES, une seule pour TOUTE la flotte —
    /// coques procédurales et modèles .glb —, parce qu'elle ne dépend que de la
    /// houle et de l'endroit du monde, jamais du navire. La démo lui pousse la
    /// mer comme elle la pousse au fond, une fois par image et non seize.
    /// </summary>
    public static ShaderMaterial? Caustic;

    /// <summary>Elle, en la faisant naître au besoin.</summary>
    public static ShaderMaterial CausticPass() =>
        Caustic ??= new ShaderMaterial
        {
            Shader = GD.Load<Shader>("res://shaders/ship_caustic.gdshader"),
            RenderPriority = -7,
        };

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
        /* CES TROIS PASSES SONT LA COQUE, PAS DE L'AIR ENTRE ELLE ET L'ŒIL.
           Elles mélangent, donc Godot les range dans la file transparente, où
           l'ordre se décide par la PRIORITÉ d'abord et la distance ensuite. À la
           priorité commune, une marque d'impact se retrouvait devant la fumée de
           sa propre bordée (signalé) : le nuage est une autre transparence, et
           deux transparences au même rang se départagent sur le centre de leur
           objet — celui d'un navire de soixante-dix mètres n'est pas là où sa
           joue est peinte. On les recule donc avant tout ce qui flotte dans
           l'air, en gardant leur ordre entre elles : la coque est peinte, PUIS
           la mer, PUIS la fumée. C'est l'ordre de la coque opaque qu'elles
           habillent, et c'était le seul qu'elles n'avaient pas.
           (Repères : sea_far −2, la mer −1, tout le reste 0.) */
        _hazePass ??= new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/hull_haze.gdshader"), RenderPriority = -4 };
        Hazed.Add(_hazePass);
        // la neige passe AVANT la brume : l'air est devant elle aussi
        _snowPass ??= new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/ship_snow.gdshader"), NextPass = _hazePass, RenderPriority = -5 };
        Snowed.Add(_snowPass);
        // et les blessures avant la neige : ses plaies se couvrent de blanc comme le reste
        _scarPass ??= new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/ship_scar.gdshader"), NextPass = _snowPass, RenderPriority = -6 };
        AddScarred(_scarPass);
        /* ET LA LUMIÈRE DE L'EAU EN TÊTE, sur ce qui est mouillé. Elle est
           PARTAGÉE PAR TOUTES LES COQUES — un seul matériau statique — parce
           qu'elle ne dépend de rien du navire : la nervure qui court sur un bordé
           ne tient qu'à la houle et à l'endroit du monde. Une par navire aurait
           demandé de pousser le spectre seize fois par image pour le même
           résultat. */
        /* ELLE EST EN QUEUE DE CHAÎNE ET NON AU MILIEU, et ce n'est pas un détail :
           partagée par toute la flotte, elle ne peut porter aucun maillon PROPRE à
           un navire. Accrochée avant la passe des blessures, elle aurait donné à
           chaque coque les plaies et la neige de la dernière construite.

           L'ORDRE DE DESSIN, lui, ne suit pas la chaîne mais les PRIORITÉS : à −7
           elle est peinte juste après la coque opaque, donc sous les blessures,
           sous la neige et sous la brume — ce qui est l'ordre juste, l'air étant
           devant tout. La chaîne n'est qu'une liste de « peindre aussi ceci ». */
        _hazePass.NextPass = CausticPass();
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
                while (last.NextPass != null && last.NextPass != _hazePass && last.NextPass != _snowPass && last.NextPass != _scarPass)
                    last = last.NextPass;
                last.NextPass = _scarPass;
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
        if (ModelRoot == null)
        {
            var proc = HullProfile.Procedural(Spec, Lines, n);
            var decks = new float[n];
            for (int k = 0; k < n; k++) decks[k] = (float)Math.Max(0, Lines.DeckY((k + 0.5) / n) - waterlineY);
            SetHeights(proc, decks);
            return proc;
        }

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
        var tops = new float[n];
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
                    // ses hauts, à toute hauteur : ce qui porte l'ombre
                    int st = HullProfile.Station(Spec, p.Z, n);
                    tops[st] = Math.Max(tops[st], (float)(p.Y - waterlineY));
                    if (p.Y < lo || p.Y > hi) continue;
                    int b = st;
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
        var prof = HullProfile.Measured(raw, Spec);
        SetHeights(prof, tops);
        return prof;
    }

    /* SES HAUTS, pour l'ombre, STATION PAR STATION : la coque traitée en bloc de
       la hauteur de son château sur toute sa longueur jetait une ombre en
       dalle, trop longue et trop carrée (signalé). Les stations vides — au-delà
       des extrémités — restent à zéro : rien n'y porte d'ombre. */
    static void SetHeights(HullProfile prof, float[] raw)
    {
        /* LISSÉS, sans quoi l'ombre se peigne : un modèle léger n'a pas de sommet
           au plus haut de chaque tranche, et une station relevée trop basse
           entre deux hautes laisse passer un rai de soleil sur toute la longueur
           de l'ombre — des dents, dans le sens du soleil (signalé). On comble
           les trous entre voisines, on garde la plus haute à deux stations près
           (un pavois reste un pavois), puis on moyenne. */
        int n = raw.Length;
        var fill = (float[])raw.Clone();
        for (int i = 0; i < n; i++)
        {
            if (raw[i] > 0) continue;
            int j = i - 1; while (j >= 0 && raw[j] <= 0) j--;
            int k = i + 1; while (k < n && raw[k] <= 0) k++;
            if (j >= 0 && k < n) fill[i] = raw[j] + (raw[k] - raw[j]) * (i - j) / (float)(k - j);
        }
        var peak = new float[n];
        for (int i = 0; i < n; i++)
            for (int d = -2; d <= 2; d++)
                if (i + d >= 0 && i + d < n) peak[i] = Math.Max(peak[i], fill[i + d]);
        var tops = new float[n];
        for (int i = 0; i < n; i++)
        {
            float s = 0; int c = 0;
            for (int d = -2; d <= 2; d++)
                if (i + d >= 0 && i + d < n) { s += peak[i + d]; c++; }
            // les bouts sans rien restent sans rien : pas d'ombre au-delà de l'étrave
            tops[i] = fill[i] > 0 ? s / c : 0;
        }
        float max = 0;
        foreach (var t in tops) max = Math.Max(max, t);
        prof.Top = max;
        if (max <= 0) return;
        var f = new float[tops.Length];
        for (int i = 0; i < f.Length; i++) f[i] = Math.Max(0, tops[i]) / max;
        prof.Heights = f;
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
        PushShipInverse();          // ses blessures la suivent
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
