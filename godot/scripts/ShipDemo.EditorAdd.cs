using Godot;
using System;
using System.Collections.Generic;
using NavalSim.Core;

namespace NavalSim;

/// <summary>
/// LE MODE CRÉATION, étape deux : POSER DE PLUS. Copier un objet du monde et le
/// coller ailleurs (Ctrl+C, Ctrl+V sous la souris), ou en prendre un dans la
/// palette (Tab) — un de chaque famille que le monde contient, et les modèles
/// bruts de world/models et props.
///
/// UNE COPIE EST UN RENVOI, PAS UN DOUBLE : le fichier dit « ajout:7, copie de
/// maison:Port-Royal:37, ici, tourné ainsi », et c'est l'objet source qui sait se
/// refaire (<see cref="Editable.MakeVisual"/>). Le modèle, sa taille, son crépi
/// suivent donc la source ; un ajout relu attend que sa source soit bâtie — la
/// terre se bâtit à la première image, les figurants plus tard — avant de naître.
/// Un modèle BRUT, lui, n'a pas de source : il est lu de son fichier.
///
/// Les ajouts ne sont dans aucun MultiMesh : un nœud chacun, posé à lieu −
/// origine à chaque image. Quelques dizaines coûtent ce que coûtent quelques
/// dizaines de nœuds, et c'est le prix de les poser sans rebâtir une ville.
/// </summary>
public partial class ShipDemo
{
    Node3D? _addedRoot;
    readonly PlacedSet _addedHolds = new();
    readonly List<ShaderMaterial> _editHazed = new();
    List<Edit>? _pendingAdds;
    (Editable? Src, string Glb, string Label, double Yaw, double Scale, double Dy, string Sheet)? _clip;
    PanelContainer? _edPalette;
    ItemList? _edList;
    readonly List<(string Label, Editable? Src, string Glb, string Sheet)> _paletteItems = new();

    /// <summary>--poser-navire fiche : un navire posé à la main devant le port de départ, SANS enregistrer — pour l'essai.</summary>
    string _hullTest = "";
    /// <summary>--poser-pavillon nation : un mât de pavillon sur un fort, SANS enregistrer — pour l'essai.</summary>
    string _flagTest = "";

    /// <summary>Chaque image, dans les deux modes : faire naître les ajouts relus, et les poser contre l'origine.</summary>
    void EditFrame(Vec3d origin, double dt)
    {
        if (_editReg == null) return;
        if (_hullTest.Length > 0 && _world?.StartPort is { } home)
        {
            var (qx, qz) = (home.Port.Hx + Math.Cos(home.Port.Ang) * 260, home.Port.Hz + Math.Sin(home.Port.Ang) * 260);
            _clip = (null, "", "essai", 0.6, 1.0, 0.0, _hullTest);
            if (EditPaste(new Vec3d(qx, 0, qz)) is { } placed)
            {
                GD.Print(FormattableString.Invariant($"[éditeur] essai : {placed.Label} posé en ({qx:F0}, {qz:F0}), non enregistré"));
                var o = _sea.Core.Origin;
                _fixLook = new Vector3((float)(qx - o.X), 2, (float)(qz - o.Z));
                _fixEye = _fixLook + new Vector3(35, 14, 40);
                _planted = true;
                _hullTest = "";
            }
        }
        /* --poser-pavillon nation : un mât sur le fort ajout:52, monté de huit mètres, NON
           enregistré — pour le voir flotter sans toucher aux retouches */
        if (_flagTest.Length > 0 && _editReg.ById.TryGetValue("ajout:52", out var fort))
        {
            _clip = (null, ShoreFlagKey + _flagTest, "essai", 0.0, 1.0, 8.0, "");
            if (EditPaste(new Vec3d(fort.X, 0, fort.Z)) is { } placed)
            {
                GD.Print(FormattableString.Invariant($"[éditeur] essai : pavillon {_flagTest} posé sur le fort, non enregistré"));
                var o = _sea.Core.Origin;
                _fixLook = new Vector3((float)(fort.X - o.X), (float)(placed.GroundY + 9), (float)(fort.Z - o.Z));
                _fixEye = _fixLook + new Vector3(30, 4, 36);
                _planted = true;
            }
            _flagTest = "";
        }
        SpawnPending();
        Chimneys(origin, dt);
        ShoreFlagsTick();
        _addedHolds.Update(origin);
        foreach (var m in _editHazed) { _sky.PushTo(m); _sky.SetCloud(m, _cloud, _t); }
    }

    /// <summary>Les ajouts du fichier, nés dès que leur source l'est.</summary>
    void SpawnPending()
    {
        if (_pendingAdds == null)
        {
            _pendingAdds = new List<Edit>();
            foreach (var e in _editReg!.Edits.All) if (e.Added) _pendingAdds.Add(e);
        }
        for (int i = _pendingAdds.Count - 1; i >= 0; i--)
        {
            var a = _pendingAdds[i];
            if (SpawnAdded(a.Id, a.From, a.Glb, a.X, a.Z, a.Yaw * Math.PI / 180, a.Scale, a.Dy, a.Sheet) != null)
            {
                _pendingAdds.RemoveAt(i);
                GD.Print($"[éditeur] {a.Id} posé ({(a.Sheet.Length > 0 ? "navire " + a.Sheet : a.From.Length > 0 ? "copie de " + a.From : a.Glb)})");
            }
        }
    }

    /// <summary>Faire naître un ajout. Nul si sa source n'est pas (encore) là.</summary>
    Editable? SpawnAdded(string id, string from, string glb, double x, double z, double yaw, double scale, double dy, string sheet = "")
    {
        if (sheet.Length > 0) return SpawnHull(id, sheet, x, z, yaw);
        Func<Node3D>? make;
        double vyaw = 0, lift = 0, radius = 1, height = 1, hazR = 0, hazH = 0;
        bool smoke = false;
        string family, flabel;
        if (from.Length > 0)
        {
            if (!_editReg!.ById.TryGetValue(from, out var src) || src.MakeVisual == null) return null;
            make = src.MakeVisual; vyaw = src.VisualYaw; lift = src.Lift; smoke = src.Smoke;
            radius = src.Radius; height = src.Height; family = src.Family; flabel = src.FamilyLabel;
            hazR = src.HazardR; hazH = src.HazardH;          // la copie d'un écueil en est un
        }
        else
        {
            if (RawModel(glb) is not { } raw) return null;
            make = raw.Make; radius = raw.Radius; height = raw.Height;
            family = "brut:" + glb; flabel = System.IO.Path.GetFileNameWithoutExtension(glb);
        }
        _addedRoot ??= AddRoot();
        var hold = new Node3D { Name = id };
        hold.AddChild(make());
        _addedRoot.AddChild(hold);
        var pl = _addedHolds.Add(hold, x, 0, z);
        var e = new Editable
        {
            Id = id, From = from, Glb = glb, Label = "ajout : " + flabel,
            BaseX = x, BaseZ = z, BaseYaw = yaw, X = x, Z = z, Yaw = yaw, Scale = scale, Dy = dy,
            Radius = radius, Height = height, MakeVisual = make, VisualYaw = vyaw, Lift = lift,
            Family = family, FamilyLabel = flabel, Smoke = smoke, HazardR = hazR, HazardH = hazH
        };
        e.Push = ed =>
        {
            /* LE PIED : le sol sous son centre, ou pour ce qui est large, le plus
               bas sous son emprise — une maison ne se pose pas en porte-à-faux. */
            double g = _world!.HeightAt(ed.X, ed.Z), r = ed.Radius * ed.Scale * 0.7;
            if (r > 3)
                for (int c = 0; c < 4; c++)
                    g = Math.Min(g, _world.HeightAt(ed.X + (c < 2 ? -r : r), ed.Z + (c % 2 == 0 ? -r : r)));
            pl.X = ed.X; pl.Z = ed.Z;
            // un émetteur n'a pas de corps : son échelle est sa force, pas sa taille
            pl.Y = g + ed.Lift * (ed.Smoke ? 1 : ed.Scale) + ed.Dy;
            _addedHolds.Moved(pl);
            hold.Rotation = new Vector3(0, (float)(ed.Yaw - ed.VisualYaw), 0);
            hold.Scale = Vector3.One * (float)ed.Scale;
            hold.Visible = !ed.Removed;
            ed.GroundY = g + ed.Dy;
            LandNode.PushRock(_world, ed);
        };
        _editReg!.AddNew(e);
        // la copie d'une maison emporte ses cheminées, qui la suivent comme les siennes
        if (from.Length > 0 && _editReg.ById.TryGetValue(from, out var host0) && host0.Chimneys != null && _town != null)
        {
            e.Chimneys = host0.Chimneys;
            _town.AddChimneys(_editReg, e, id);
        }
        return e;
    }

    /* ------------------------------------------------------------------ */
    /*  LES CHEMINÉES                                                      */
    /* ------------------------------------------------------------------ */

    MultiMeshInstance3D? _smokeMarks;
    /// <summary>settings.json → chimneys.enabled</summary>
    bool _chimneyRules = true;
    /// <summary>settings.json → chimneys.density : la part des cheminées AUTOMATIQUES qui fument.</summary>
    double _chimneyDensity = 0.15;

    /// <summary>
    /// CELLE-CI FUME-T-ELLE ? Une cheminée que la main a posée ou retouchée, toujours :
    /// on l'a mise là pour la voir. Les autres, selon la densité, par un tirage
    /// sur leur NOM — le même d'une partie à l'autre, et qui ne change pas quand on
    /// bouge la caméra.
    /// </summary>
    bool Smokes(Editable e)
    {
        if (!e.Pristine || _chimneyDensity >= 1) return true;
        // le tirage, une fois par cheminée : refait à chaque image, il allouait un énumérateur par nom
        if (e.Roll < 0)
        {
            uint h = 2166136261;
            for (int i = 0; i < e.Id.Length; i++) h = (h ^ e.Id[i]) * 16777619;
            e.Roll = (h % 10000) / 10000.0;
        }
        return e.Roll < _chimneyDensity;
    }

    /// <summary>Les distances : on ne fait fumer que ce qu'on peut voir fumer, et la ville au loin se tait.</summary>
    const double ChimneyRange = 900;

    /// <summary>
    /// FAIRE FUMER LES CHEMINÉES PROCHES de l'œil, et en mode création, MONTRER
    /// les émetteurs — une petite boule à leur bouche, qu'on vise et qu'on déplace.
    /// </summary>
    void Chimneys(Vec3d origin, double dt)
    {
        if (!_chimneyRules) { if (_smokeMarks != null) _smokeMarks.Visible = false; return; }
        var cp = _cam.GlobalPosition;
        double ex = cp.X + origin.X, ez = cp.Z + origin.Z;
        int marks = 0;
        _smokeMarks ??= SmokeMarks();
        var mm = _smokeMarks.Multimesh;
        foreach (var e in _editReg!.Items)
        {
            if (!e.Smoke) continue;
            // sur sa maison, à chaque image : elle a pu bouger, tourner, grandir
            if (e.Host != null) e.PlaceEmitter(_world!.HeightAt);
            if (!e.Live) continue;
            // en mode création, on montre aussi celles que la densité fait taire : on doit pouvoir les prendre
            bool smokes = Smokes(e);
            if (!smokes && !_editing) continue;
            double dx = e.X - ex, dz = e.Z - ez;
            if (dx * dx + dz * dz > ChimneyRange * ChimneyRange) continue;
            var at = new Vector3((float)(e.X - origin.X), (float)e.AimY, (float)(e.Z - origin.Z));
            if (smokes) _gunFx.Hearth(at, e.Scale, dt, ref e.SmokeDue);
            if (_editing && marks < mm.InstanceCount)
                mm.SetInstanceTransform(marks++, new Transform3D(Basis.Identity.Scaled(Vector3.One * 0.9f), at));
        }
        mm.VisibleInstanceCount = marks;
        _smokeMarks.Visible = _editing;
    }

    MultiMeshInstance3D SmokeMarks()
    {
        var mm = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, InstanceCount = 512, VisibleInstanceCount = 0,
            Mesh = new SphereMesh
            {
                Radius = 0.5f, Height = 1f, RadialSegments = 8, Rings = 4,
                Material = new StandardMaterial3D
                {
                    ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                    AlbedoColor = new Color(0.55f, 0.75f, 1f), NoDepthTest = true
                }
            }
        };
        var n = new MultiMeshInstance3D
        {
            Multimesh = mm, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            CustomAabb = new Aabb(new Vector3(-1e5f, -1e3f, -1e5f), new Vector3(2e5f, 2e3f, 2e5f))
        };
        AddChild(n);
        return n;
    }

    // --- l'essai : --voir a.glb,b.glb — posés en ligne sous le milieu de l'écran, rien d'enregistré ---
    string _seeModels = "";

    void SeeModels()
    {
        var list = _seeModels.Split(','); _seeModels = "";
        if (GroundUnder(GetViewport().GetVisibleRect().Size * 0.5f) is not { } g) { GD.Print("[voir] pas de sol au milieu de l'écran"); return; }
        for (int i = 0; i < list.Length; i++)
        {
            var e = SpawnAdded("essai:" + i, "", list[i], g.X + i * 12, g.Z, 0, 1, 0);
            GD.Print(FormattableString.Invariant($"[voir] {list[i]} : {(e == null ? "illisible" : $"{e.Radius * 2:F2} m de large, {e.Height:F2} m de haut")}"));
        }
    }

    Node3D AddRoot()
    {
        var n = new Node3D { Name = "Ajouts" };
        AddChild(n);
        return n;
    }

    /* ------------------------------------------------------------------ */
    /*  LES MODÈLES BRUTS                                                  */
    /* ------------------------------------------------------------------ */

    sealed class Raw
    {
        public Func<Node3D> Make = null!;
        public double Radius, Height;
    }
    readonly Dictionary<string, Raw?> _raws = new();

    /// <summary>
    /// CE QUE LA PALETTE SAIT D'UN MODÈLE BRUT — &lt;dossier&gt;/palette.json : le nom
    /// qu'on y lit et sa taille en mètres (sa plus grande dimension). Lu une fois
    /// par dossier ; un modèle absent n'a ni l'un ni l'autre.
    /// </summary>
    (string? Name, double Size, double Far, int Lamps) PaletteInfo(string rel)
    {
        string dir = System.IO.Path.GetDirectoryName(rel)?.Replace('\\', '/') ?? "";
        if (!_paletteDirs.TryGetValue(dir, out var map))
        {
            map = new Dictionary<string, (string?, double, double, int)>();
            string file = System.IO.Path.Combine(Assets.Root, dir, "palette.json");
            if (System.IO.File.Exists(file))
                try
                {
                    using var doc = System.Text.Json.JsonDocument.Parse(System.IO.File.ReadAllText(file));
                    if (doc.RootElement.TryGetProperty("modeles", out var ms))
                        foreach (var m in ms.EnumerateObject())
                            map[m.Name] = (m.Value.TryGetProperty("nom", out var n) ? n.GetString() : null,
                                           m.Value.TryGetProperty("taille", out var t) ? t.GetDouble() : 0,
                                           m.Value.TryGetProperty("loin", out var lo) ? lo.GetDouble() : 0,
                                           m.Value.TryGetProperty("lanternes", out var la) ? la.GetInt32() : 0);
                }
                catch (Exception ex) { GD.PushWarning($"[éditeur] {file} illisible : {ex.Message}"); }
            _paletteDirs[dir] = map;
        }
        return map.TryGetValue(System.IO.Path.GetFileName(rel), out var info) ? info : (null, 0, 0, 0);
    }
    readonly Dictionary<string, Dictionary<string, (string?, double, double, int)>> _paletteDirs = new();

    /// <summary>
    /// UN MODÈLE BRUT, lu une fois. On ne sait ni son échelle ni où Blender a
    /// laissé son origine : il est recentré sur sa boîte et posé sur son point le
    /// plus bas. Sa TAILLE vient de palette.json quand elle y est écrite ; sinon son
    /// unité est prise pour le mètre, sauf s'il passe quarante unités — un export
    /// en centimètres, comme le tonneau et le coffre —, ramené alors à quatre
    /// mètres. L'unité ne se devine pas : un fort de cent unités et un tonneau de
    /// quatre-vingt-dix-huit se ressemblent dans leur fichier.
    /// </summary>
    /// <summary>Simplifier au loin un modèle lourd (Assets.AddLods), et dire ce que ça a pris.</summary>
    public static void LodSay(string rel, Node root, double far)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        int lows = Assets.SplitLodLow(root, far);
        if (lows > 0) GD.Print(FormattableString.Invariant($"[détail] {rel} : sa version lointaine (…{Assets.LodLowSuffix}) au-delà de {far:F0} m"));
        var (tris, cached) = Assets.AddLods(root, Assets.Path(rel));
        if (tris > 0) GD.Print(cached == tris
            ? $"[détail] {rel} : {tris} triangles, simplifiés relus du cache en {clock.ElapsedMilliseconds} ms"
            : $"[détail] {rel} : {tris} triangles simplifiés au loin en {clock.ElapsedMilliseconds} ms, mis en cache");
    }

    Raw? RawModel(string rel)
    {
        if (_raws.TryGetValue(rel, out var have)) return have;
        // un mât de pavillon n'est pas un fichier : il est bâti ici (ShipDemo.ShoreFlags.cs)
        if (rel.StartsWith(ShoreFlagKey, StringComparison.Ordinal)) return _raws[rel] = ShoreFlagRaw(rel);
        Raw? outp = null;
        string path = Assets.Path(rel);
        if (System.IO.File.Exists(path))
        {
            if (Assets.LoadGlb(path) is Node3D root)
            {
                Aabb? box = NodeWalk.Bounds(root);
                {
                    // « loin » dans la palette, sinon quatre fois sa taille : 256 m pour un bloc de 64
                    var pi = PaletteInfo(rel);
                    double size = pi.Size > 0 ? pi.Size : box is Aabb b0 ? Math.Max(b0.Size.X, Math.Max(b0.Size.Y, b0.Size.Z)) : 20;
                    LodSay(rel, root, pi.Far > 0 ? pi.Far : 4 * size);
                }
                var haze = HazePass.New();
                _editHazed.Add(haze);
                foreach (var mi in NodeWalk.Meshes(root)) HazePass.Wear(mi, haze);   // l'air devant, comme tout ce qui est à terre
                if (box is Aabb bb && bb.Size.Length() > 1e-5f)
                {
                    float ext = Math.Max(bb.Size.X, Math.Max(bb.Size.Y, bb.Size.Z));
                    double declared = PaletteInfo(rel).Size;
                    float k = declared > 0 ? (float)(declared / ext) : ext > 40 ? 4f / ext : 1f;
                    var ctr = bb.GetCenter();
                    var tmpl = root;
                    int lampCount = PaletteInfo(rel).Lamps;
                    int made = 0;
                    outp = new Raw
                    {
                        Radius = 0.5 * Math.Max(bb.Size.X, bb.Size.Z) * k,
                        Height = bb.Size.Y * k,
                        Make = () =>
                        {
                            var w = new Node3D();
                            var c = (Node3D)tmpl.Duplicate();
                            c.Scale = Vector3.One * k;
                            c.Position = new Vector3(-ctr.X * k, -bb.Position.Y * k, -ctr.Z * k);
                            w.AddChild(c);
                            // ses lanternes de nuit, sur sa façade +z (palette : « lanternes »)
                            if (_town?.LampsOnFront(lampCount, bb.Size.X * k, (bb.End.Z - ctr.Z) * k, rel.Length * 7919 + 31 * made++) is { } lamps)
                                w.AddChild(lamps);
                            return w;
                        }
                    };
                }
            }
        }
        if (outp == null) GD.PushWarning($"[éditeur] modèle brut illisible : {rel}");
        return _raws[rel] = outp;
    }

    /* ------------------------------------------------------------------ */
    /*  COPIER, COLLER                                                     */
    /* ------------------------------------------------------------------ */

    /// <summary>
    /// UN NAVIRE AU MOUILLAGE POSÉ À LA MAIN : un décor de rade (MooredNode.AddHull),
    /// qui suit la houle et gîte sur sa pente, à la place et au cap qu'on lui donne.
    /// Ni échelle ni hauteur : un navire a sa taille, et c'est la mer qui le porte.
    /// </summary>
    Editable? SpawnHull(string id, string sheet, double x, double z, double yaw)
    {
        if (_moored?.AddHull(sheet, x, z, yaw) is not { } h) return null;
        var e = new Editable
        {
            Id = id, Sheet = sheet, Label = "navire : " + h.Name,
            BaseX = x, BaseZ = z, BaseYaw = yaw, X = x, Z = z, Yaw = yaw,
            Radius = h.L * 0.5, Height = h.L * 0.35,
            Family = "navire:" + sheet, FamilyLabel = h.Name
        };
        e.Push = ed =>
        {
            _moored.MoveHull(h, ed.X, ed.Z, ed.Yaw, ed.Removed);
            ed.Scale = 1; ed.Dy = 0;
            ed.GroundY = 0;                 // il est sur l'eau, pas sur le fond
        };
        _editReg!.AddNew(e);
        return e;
    }

    void EditCopy()
    {
        if (_sel == null) { Say("Rien de pris à copier"); return; }
        _clip = (_sel, "", _sel.FamilyLabel, _sel.Yaw, _sel.Scale, _sel.Dy, "");
        Say($"Copié : {_sel.FamilyLabel} · Ctrl+V pour le coller sous la souris");
    }

    /// <summary>Coller sous la souris (ou au milieu de l'écran si elle est au ciel), et prendre la copie.</summary>
    Editable? EditPaste(Vec3d? at = null)
    {
        if (_clip is not { } c) { Say("Rien à coller · Ctrl+C sur un objet, ou Tab pour la palette"); return null; }
        var p = at ?? GroundUnder(GetViewport().GetMousePosition())
                   ?? GroundUnder(GetViewport().GetVisibleRect().Size * 0.5f);
        if (p is not { } g) { Say("Pas de sol sous la souris"); return null; }
        // la copie d'une copie renvoie à la SOURCE : le fichier ne fait jamais de chaîne
        string from = c.Src == null ? "" : c.Src.Added ? c.Src.From : c.Src.Id;
        string glb = c.Src == null ? c.Glb : c.Src.Added ? c.Src.Glb : "";
        string sheet = c.Src == null ? c.Sheet : c.Src.Sheet;
        var e = SpawnAdded(_editReg!.NextAddId(), from, glb, g.X, g.Z, c.Yaw, c.Scale, c.Dy, sheet);
        if (e == null) { Say("Ce modèle ne se refait pas"); return null; }
        _editReg.Commit(e);
        // l'annuler, c'est le retirer
        _edUndo.Push((e, (e.X, e.Z, e.Yaw, e.Scale, e.Dy, true)));
        _sel = e;
        Say($"Collé : {c.Label}");
        return e;
    }

    /* ------------------------------------------------------------------ */
    /*  LA PALETTE                                                         */
    /* ------------------------------------------------------------------ */

    /// <summary>Un objet par famille que le monde contient, puis les modèles bruts des dossiers.</summary>
    void FillPalette()
    {
        _paletteItems.Clear();
        var seen = new HashSet<string>();
        foreach (var e in _editReg!.Items)
        {
            if (e.MakeVisual == null || e.Family.Length == 0 || e.Family.StartsWith("brut:") || !seen.Add(e.Family)) continue;
            _paletteItems.Add((e.FamilyLabel, e, "", ""));
        }
        _paletteItems.Sort((a, b) => string.Compare(a.Label, b.Label, StringComparison.CurrentCulture));
        /* LES MÂTS DE PAVILLON, un par nation, EN TÊTE des modèles (demandé : ils se
           perdaient au bas d'une longue liste) : sur un fort, une batterie, un ponton */
        foreach (var nat in _nations.All)
            _paletteItems.Add(($"pavillon · {nat.Pays ?? nat.Nationalite ?? nat.Id} · mât de {ShoreStaff:0} m", null, ShoreFlagKey + nat.Id, ""));
        foreach (var dir in new[] { "world/models", "props" })
        {
            var names = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var root in new[] { Assets.Root, Assets.Heavy })
            {
                string d = System.IO.Path.Combine(root, dir);
                if (!System.IO.Directory.Exists(d)) continue;
                foreach (var f in System.IO.Directory.GetFiles(d, "*.glb"))
                {
                    /* un relief entier ou une scène de trente mégaoctets n'est pas un
                       objet à poser — SAUF s'il est nommé dans palette.json : un pâté de
                       maisons de 27 Mo est un objet, et l'avoir écrit le dit (signalé). */
                    string n = System.IO.Path.GetFileName(f);
                    if (new System.IO.FileInfo(f).Length < 25_000_000 || PaletteInfo($"{dir}/{n}").Item1 != null) names.Add(n);
                }
            }
            foreach (var n in names)
            {
                var (nom, size, _, _) = PaletteInfo($"{dir}/{n}");
                string label = nom != null ? FormattableString.Invariant($"{nom} · {dir}/{n}{(size > 0 ? $" · {size:0.#} m" : "")}") : $"modèle brut · {dir}/{n}";
                _paletteItems.Add((label, null, $"{dir}/{n}", ""));
            }
        }
        /* LES NAVIRES, À MOUILLER : chaque fiche qui a un modèle, posée en décor de
           rade (MooredNode.AddHull) — un navire qu'on place n'a ni solveur ni barre. */
        foreach (var path in _paths)
        {
            if (ShipLibrary.Load(path) is not { Model: { Glb.Length: > 0 } } spec) continue;
            string sheet = System.IO.Path.GetFileNameWithoutExtension(path);
            _paletteItems.Add((FormattableString.Invariant($"navire · {spec.Name} · {spec.Hull.Length:0} m"), null, "", sheet));
        }
        _edList!.Clear();
        foreach (var it in _paletteItems) _edList.AddItem(it.Label);
    }

    void TogglePalette()
    {
        if (_edLayer == null) return;
        if (_edPalette == null)
        {
            _edPalette = new PanelContainer
            {
                Position = new Vector2(24, 220),
                CustomMinimumSize = new Vector2(420, 460)
            };
            _edList = new ItemList { CustomMinimumSize = new Vector2(400, 440) };
            _edList.AddThemeFontSizeOverride("font_size", 16);
            _edList.ItemClicked += (idx, _, _) => ChoosePalette((int)idx);
            _edPalette.AddChild(_edList);
            _edLayer.AddChild(_edPalette);
            _edPalette.Visible = false;
        }
        _edPalette.Visible = !_edPalette.Visible;
        if (_edPalette.Visible) FillPalette();
    }

    void ChoosePalette(int i)
    {
        if (i < 0 || i >= _paletteItems.Count) return;
        var (label, src, glb, sheet) = _paletteItems[i];
        _clip = src != null ? (src, "", src.FamilyLabel, src.Yaw, 1.0, 0.0, "") : (null, glb, label, 0.0, 1.0, 0.0, sheet);
        if (_edPalette != null) _edPalette.Visible = false;
        Say($"{(src != null ? src.FamilyLabel : label)} · Ctrl+V pour le poser sous la souris");
    }

    /// <summary>La souris est-elle sur la palette ? Elle est alors à la palette, pas au monde.</summary>
    bool OverPalette(InputEvent e) =>
        _edPalette is { Visible: true } && e is InputEventMouse m && _edPalette.GetGlobalRect().HasPoint(m.Position);
}
