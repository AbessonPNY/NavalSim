using Godot;
using System;
using System.Collections.Generic;
using NavalSim.Core;

namespace NavalSim;

/// <summary>
/// LES NAVIRES AU MOUILLAGE — le décor d'un port habité.
///
/// PAS DES ShipNode, ET C'EST TOUT LE SUJET. Un navire du jeu porte un solveur à
/// six degrés de liberté, deux cents sondes de carène, un gréement, des pièces et
/// une barre ; seize coques coûtent déjà 16,8 ms l'image, ce qui est le plafond
/// mesuré. Quinze ports à trois navires en feraient quarante-cinq — et aucun
/// d'eux ne navigue.
///
/// Un navire AMARRÉ n'a pas besoin d'un solveur : il lui faut se tenir à la
/// hauteur de l'eau et rouler un peu. Deux sondes de houle par coque et par image
/// suffisent, contre deux cents — et le reste du budget reste à la flotte qui,
/// elle, navigue vraiment.
///
/// UN SEUL PORT À LA FOIS, celui dont on approche. Les autres ne sont pas dessinés
/// du tout : leurs nœuds existent mais restent cachés, comme les villes et les
/// badauds, et pour la même raison — ce qui coûte n'est pas de les tenir, c'est de
/// les peindre.
///
/// POSÉS À <c>at − origine</c> comme tout ce qui est « du monde » : l'origine
/// flottante glisse sous eux, et leurs coordonnées d'instance restent petites.
/// </summary>
public partial class MooredNode : Node3D
{
    readonly World _world;

    sealed class Mouillage
    {
        public Vec3d At;                 // le port, en mètres monde vrais
        public Node3D Group = null!;
        public bool Told;
        public readonly List<(Node3D Node, Vec3d At, double Cap, bool Ancre, double L)> Ships = new();
    }

    readonly List<Mouillage> _ports = new();

    /// <summary>Debug : dire ou sont les postes, une fois par port.</summary>
    public static bool Debug;

    /// <summary>Les matières que <see cref="SkyNode.PushTo"/> doit tenir à jour.</summary>
    public readonly List<ShaderMaterial> Hazed = new();

    /// <summary>Au-delà, on ne les montre pas : une rade à trois milles n'a pas de mâts lisibles.</summary>
    public double Range = 3200;

    public MooredNode(World world) { _world = world; }

    ShaderMaterial? _haze;

    /// <summary>
    /// LE CIEL SUR UN MOUILLAGE. La version courte de <c>ShipNode.AttachHaze</c> :
    /// une seule passe, sans neige ni blessures — un navire amarre ne se bat pas,
    /// et la neige sur un decor qui ne bouge pas se verrait moins que son cout.
    ///
    /// SANS ELLE ILS TRAVERSERAIENT LA BRUME : une rade a deux milles se lirait
    /// nette au milieu d une cote effacee, ce qui est exactement le defaut que la
    /// brume est la pour corriger.
    /// </summary>
    void Haze(Node3D obj)
    {
        _haze ??= new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/hull_haze.gdshader"), RenderPriority = -4 };
        if (!Hazed.Contains(_haze)) Hazed.Add(_haze);
        var done = new HashSet<Material>();
        foreach (var mi in Meshes(obj))
            for (int i = 0; i < mi.Mesh.GetSurfaceCount(); i++)
            {
                var mat = mi.GetSurfaceOverrideMaterial(i) ?? mi.Mesh.SurfaceGetMaterial(i);
                if (mat is ShaderMaterial || mi.MaterialOverride is ShaderMaterial) continue;
                if (mat == null) { mat = new StandardMaterial3D(); mi.SetSurfaceOverrideMaterial(i, mat); }
                if (!done.Add(mat)) continue;
                var last = mat;
                while (last.NextPass != null && last.NextPass != _haze) last = last.NextPass;
                last.NextPass = _haze;
            }
    }

    static IEnumerable<MeshInstance3D> Meshes(Node n)
    {
        if (n is MeshInstance3D mi && mi.Mesh != null) yield return mi;
        foreach (var c in n.GetChildren()) foreach (var m in Meshes(c)) yield return m;
    }

    /// <summary>
    /// LES TROIS NAVIRES QU'ON MOUILLE, et rien d'autre pour l'instant. Le sloop, le
    /// galion et le vaisseau de ligne : trois tailles franchement différentes, ce
    /// qui suffit à ce qu'une rade ne se lise pas comme une rangée de copies.
    /// </summary>
    public string[] Flotte = { "sloop.json", "frigate17e.json", "frigate.json" };

    /// <summary>
    /// Bâtir les rades. Une fois, au chargement du monde : les navires amarrés ne
    /// changent pas de place, et les mettre à l'eau en cours de partie ferait de la
    /// géométrie au pire moment.
    /// </summary>
    readonly Dictionary<string, (ShipSpec Spec, Node3D Root, double K, double Y)?> _models = new();

    /// <summary>Un modèle de la flotte au mouillage, lu une fois par fiche — les ports et la fiche du monde le partagent.</summary>
    (ShipSpec Spec, Node3D Root, double K, double Y)? Model(string shipsDir, string rel, Ocean sea)
    {
        string name = System.IO.Path.GetFileName(rel);
        if (_models.TryGetValue(name, out var have)) return have;
        return _models[name] = LoadModel(shipsDir, name, sea);
    }

    (ShipSpec Spec, Node3D Root, double K, double Y)? LoadModel(string shipsDir, string rel, Ocean sea)
    {
        {
            string sheet = System.IO.Path.Combine(shipsDir, System.IO.Path.GetFileName(rel));
            if (!System.IO.File.Exists(sheet)) { GD.PushWarning($"[rade] {rel} introuvable"); return null; }
            var spec = ShipSpec.FromJson(System.IO.File.ReadAllText(sheet));
            if (spec.Model == null || spec.Model.Glb.Length == 0) return null;
            string path = Assets.Path(spec.Model.Glb);
            if (!System.IO.File.Exists(path)) { GD.PushWarning($"[rade] {spec.Model.Glb} introuvable"); return null; }
            var doc = new GltfDocument();
            var state = new GltfState();
            if (doc.AppendFromFile(path, state) != Error.Ok || doc.GenerateScene(state) is not Node3D root) return null;
            /* LA MÊME ÉCHELLE QUE LA FLOTTE, par la même fonction : un galion au
               mouillage qui ne ferait pas la taille d'un galion sous voiles se
               verrait au premier coup d'œil, et deux calculs d'échelle finiraient
               par dériver. */
            double k = spec.Model.Scale ?? ShipNode.HullScale(root, spec.Model.LengthAxis, spec.Hull.Length);
            /* LA BRUME UNE FOIS PAR MODELE, ET NON PAR COQUE. Duplicate() partage
               les ressources : les trois Roter Lowe de trois ports differents ont
               LES MEMES matieres, donc la passe posee ici les habille toutes. */
            /* ET SANS SON TÉLÉPORTEUR : les anneaux et la sphère vivent dans le .glb
               de la Roter Löwe, mais ils sont à SON capitaine. Une coque au mouillage
               qui les porterait ferait croire à quinze téléporteurs en Jamaïque. */
            ShipNode.StripRings(root);
            // ses cordages en rubans, comme sous voiles : un galion à quai ne scintille pas plus qu'en mer
            if (spec.Model.Ribbons) ShipNode.ConvertRopes(root, spec, Hazed, $"rade {spec.Id}");
            Haze(root);
            /* SON ASSISE, DEMANDÉE AU SOLVEUR ET NON DEVINÉE. Un modèle ne porte pas
               sa flottaison : l origine d un .glb est là où Blender l a laissée. Poser
               la coque à la hauteur de l eau la fait donc flotter trop haut ou trop
               bas d autant, et l œil le voit tout de suite. Settle rend la MÊME assise
               que celle du navire du joueur — trois fois au chargement, jamais après. */
            var phys = new ShipPhysics(spec, new HullLines(spec));
            double y0 = phys.Settle(sea, new Controls());
            return (spec, root, k, y0);
        }
    }

    public void Build(string shipsDir, Ocean sea, double playerL, double playerB)
    {
        var modeles = new List<(ShipSpec Spec, Node3D Root, double K, double Y)>();
        foreach (string rel in Flotte)
            if (Model(shipsDir, rel, sea) is { } m0) modeles.Add(m0);
        if (modeles.Count == 0) { GD.PushWarning("[rade] aucun modèle : les ports resteront vides."); return; }

        var dims = new List<(double, double, double)>();
        foreach (var m in modeles)
            dims.Add((m.Spec.Hull.Length, m.Spec.Hull.Beam, m.Spec.Hull.KeelDepth + m.Spec.Hull.KeelExtra));

        int total = 0;
        foreach (var isl in _world.Isles)
        {
            if (isl.Port.Hx == 0 && isl.Port.Hz == 0) continue;
            uint graine = 0;
            foreach (char c in isl.Key) graine = graine * 131 + c;
            var postes = Moored.Postes(_world, isl, dims, playerL, playerB, graine);
            if (postes.Count == 0) continue;

            var m2 = new Mouillage { At = new Vec3d(isl.Port.Hx, 0, isl.Port.Hz), Group = new Node3D { Visible = false } };
            AddChild(m2.Group);
            /* L'ORDRE DES POSTES SUIT CELUI DES NAVIRES TRIÉS PAR TAILLE — c'est la
               convention de Moored.Postes, et la relire ici plutôt que de la
               supposer éviterait un sloop amarré à la place d'un vaisseau. */
            var ordre = new List<int>();
            for (int i = 0; i < dims.Count; i++) ordre.Add(i);
            ordre.Sort((a, b) => dims[a].Item1.CompareTo(dims[b].Item1));

            for (int i = 0; i < postes.Count && i < ordre.Count; i++)
            {
                var mdl = modeles[ordre[i]];
                var copie = (Node3D)mdl.Root.Duplicate();
                copie.Scale = Vector3.One * (float)mdl.K;
                copie.Rotation = new Vector3(0, (float)mdl.Spec.Model!.RotationY, 0);
                var holder = new Node3D();
                holder.AddChild(copie);
                m2.Group.AddChild(holder);
                m2.Ships.Add((holder, new Vec3d(postes[i].X, mdl.Y, postes[i].Z), postes[i].Cap,
                              postes[i].Mouille, mdl.Spec.Hull.Length));
                total++;
            }
            _ports.Add(m2);
        }
        GD.Print($"{total} navire(s) au mouillage dans {_ports.Count} port(s)");
    }

    /// <summary>Le registre de l'éditeur : les navires que la fiche pose s'y inscrivent.</summary>
    public EditRegistry? Editor;

    /// <summary>
    /// LES NAVIRES QUE LA FICHE POSE ELLE-MÊME (world/*.json → mouilles) : un groupe
    /// de plus, qui paraît et s'efface comme une rade, et dont chaque coque suit la
    /// houle de la même façon. Chacun est un objet du mode création — on le déplace,
    /// on le tourne, on le retire ; il ne se copie pas (une copie posée sur le sol de
    /// l'éditeur n'aurait pas d'eau pour la porter).
    /// </summary>
    public void BuildAnchored(string shipsDir, Ocean sea)
    {
        var specs = _world.Region.Anchored;
        if (specs.Count == 0) return;
        double cx = 0, cz = 0;
        foreach (var a in specs) { cx += a.X; cz += a.Z; }
        var m2 = new Mouillage { At = new Vec3d(cx / specs.Count, 0, cz / specs.Count), Group = new Node3D { Visible = false } };
        AddChild(m2.Group);
        _ports.Add(m2);
        int i = 0;
        foreach (var a in specs)
        {
            if (Model(shipsDir, a.Sheet, sea) is not { } mdl) continue;
            var copie = (Node3D)mdl.Root.Duplicate();
            copie.Scale = Vector3.One * (float)mdl.K;
            copie.Rotation = new Vector3(0, (float)mdl.Spec.Model!.RotationY, 0);
            var holder = new Node3D();
            holder.AddChild(copie);
            m2.Group.AddChild(holder);
            double yaw = -a.Cap * Math.PI / 180;
            int slot = m2.Ships.Count;
            m2.Ships.Add((holder, new Vec3d(a.X, mdl.Y, a.Z), yaw, true, mdl.Spec.Hull.Length));
            var e = new Editable
            {
                Id = $"mouille:{i++}", Label = mdl.Spec.Name.Length > 0 ? mdl.Spec.Name : "un navire au mouillage",
                BaseX = a.X, BaseZ = a.Z, BaseYaw = yaw, X = a.X, Z = a.Z, Yaw = yaw,
                Radius = mdl.Spec.Hull.Length * 0.5, Height = mdl.Spec.Hull.Length * 0.4,
                Family = "mouille", FamilyLabel = "Navire au mouillage"
            };
            double y0 = mdl.Y, L = mdl.Spec.Hull.Length;
            e.Push = ed =>
            {
                m2.Ships[slot] = (holder, new Vec3d(ed.X, y0, ed.Z), ed.Yaw, true, L);
                holder.Visible = !ed.Removed;
                ed.GroundY = -2;
            };
            if (Editor != null) Editor.Add(e); else e.Push(e);
        }
        GD.Print($"{m2.Ships.Count} navire(s) au mouillage posé(s) par la fiche");
    }

    /// <summary>
    /// Une image : le port le plus proche paraît, les autres s'effacent, et chaque
    /// coque suit la houle.
    ///
    /// LA GÎTE SE LIT SUR LA PENTE DE L'EAU, par deux sondes de part et d'autre.
    /// Un navire amarré qui ne roulerait pas se lirait comme une maquette posée sur
    /// un miroir — c'est le seul mouvement qu'il ait, et il doit l'avoir.
    /// </summary>
    public void Update(Vec3d centre, Vec3d origin, Ocean sea, double t)
    {
        foreach (var p in _ports)
        {
            double dx = p.At.X - centre.X, dz = p.At.Z - centre.Z;
            bool vu = dx * dx + dz * dz < Range * Range;
            if (vu != p.Group.Visible) p.Group.Visible = vu;
            if (!vu) continue;
            if (Debug && !p.Told) { p.Told = true;
                foreach (var (nd, at2, cp, an, LL) in p.Ships)
                    GD.Print($"[rade] {LL:F0} m a ({at2.X - origin.X:F0}, {at2.Z - origin.Z:F0}) local, {(an ? "sur ancre" : "a quai")}");
            }

            foreach (var (node, at, cap, ancre, L) in p.Ships)
            {
                double lx = at.X - origin.X, lz = at.Z - origin.Z;
                double h = sea.Sample(lx, lz, t);
                /* LA PENTE SUR UNE DEMI-LONGUEUR : prise sur un mètre, une coque de
                   soixante-dix mètres suivrait la moindre ride au lieu de la
                   traverser. Un navire est un filtre passe-bas sur la houle, et sa
                   longueur EST le filtre. */
                double r = Math.Max(4, L * 0.5);
                double hx = sea.Sample(lx + r, lz, t) - sea.Sample(lx - r, lz, t);
                double hz = sea.Sample(lx, lz + r, t) - sea.Sample(lx, lz - r, t);
                node.Position = new Vector3((float)lx, (float)(h + at.Y), (float)lz);
                /* LE CAP D'ABORD, LA GÎTE ENSUITE, et dans cet ordre : appliquer la
                   pente avant le cap la ferait tourner avec lui, et le navire
                   roulerait dans le mauvais sens sur un bord. */
                var b = Basis.Identity
                    .Rotated(Vector3.Up, (float)cap)
                    .Rotated(Vector3.Right, (float)Math.Atan2(-hz, 2 * r))
                    .Rotated(Vector3.Forward, (float)Math.Atan2(-hx, 2 * r));
                node.Basis = b;
            }
        }
    }
}
