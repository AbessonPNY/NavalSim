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
    readonly List<Node3D> _addedHolds = new();
    readonly List<ShaderMaterial> _editHazed = new();
    List<Edit>? _pendingAdds;
    (Editable? Src, string Glb, string Label, double Yaw, double Scale, double Dy)? _clip;
    PanelContainer? _edPalette;
    ItemList? _edList;
    readonly List<(string Label, Editable? Src, string Glb)> _paletteItems = new();

    /// <summary>Chaque image, dans les deux modes : faire naître les ajouts relus, et les poser contre l'origine.</summary>
    void EditFrame(Vec3d origin)
    {
        if (_editReg == null) return;
        SpawnPending();
        foreach (var h in _addedHolds)
            h.Position = new Vector3((float)((double)h.GetMeta("wx") - origin.X), (float)(double)h.GetMeta("wy"),
                                     (float)((double)h.GetMeta("wz") - origin.Z));
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
            if (SpawnAdded(a.Id, a.From, a.Glb, a.X, a.Z, a.Yaw * Math.PI / 180, a.Scale, a.Dy) != null)
            {
                _pendingAdds.RemoveAt(i);
                GD.Print($"[éditeur] {a.Id} posé ({(a.From.Length > 0 ? "copie de " + a.From : a.Glb)})");
            }
        }
    }

    /// <summary>Faire naître un ajout. Nul si sa source n'est pas (encore) là.</summary>
    Editable? SpawnAdded(string id, string from, string glb, double x, double z, double yaw, double scale, double dy)
    {
        Func<Node3D>? make;
        double vyaw = 0, lift = 0, radius = 1, height = 1;
        string family, flabel;
        if (from.Length > 0)
        {
            if (!_editReg!.ById.TryGetValue(from, out var src) || src.MakeVisual == null) return null;
            make = src.MakeVisual; vyaw = src.VisualYaw; lift = src.Lift;
            radius = src.Radius; height = src.Height; family = src.Family; flabel = src.FamilyLabel;
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
        _addedHolds.Add(hold);
        var e = new Editable
        {
            Id = id, From = from, Glb = glb, Label = "ajout : " + flabel,
            BaseX = x, BaseZ = z, BaseYaw = yaw, X = x, Z = z, Yaw = yaw, Scale = scale, Dy = dy,
            Radius = radius, Height = height, MakeVisual = make, VisualYaw = vyaw, Lift = lift,
            Family = family, FamilyLabel = flabel
        };
        e.Push = ed =>
        {
            /* LE PIED : le sol sous son centre, ou pour ce qui est large, le plus
               bas sous son emprise — une maison ne se pose pas en porte-à-faux. */
            double g = _world!.HeightAt(ed.X, ed.Z), r = ed.Radius * ed.Scale * 0.7;
            if (r > 3)
                for (int c = 0; c < 4; c++)
                    g = Math.Min(g, _world.HeightAt(ed.X + (c < 2 ? -r : r), ed.Z + (c % 2 == 0 ? -r : r)));
            hold.SetMeta("wx", ed.X);
            hold.SetMeta("wz", ed.Z);
            hold.SetMeta("wy", g + ed.Lift * ed.Scale + ed.Dy);
            hold.Rotation = new Vector3(0, (float)(ed.Yaw - ed.VisualYaw), 0);
            hold.Scale = Vector3.One * (float)ed.Scale;
            hold.Visible = !ed.Removed;
            ed.GroundY = g + ed.Dy;
        };
        _editReg!.AddNew(e);
        return e;
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
    /// UN MODÈLE BRUT, lu une fois. On ne sait ni son échelle ni où Blender a
    /// laissé son origine : il est recentré sur sa boîte et posé sur son point le
    /// plus bas. Son unité est prise pour le MÈTRE, sauf s'il passe quarante
    /// unités — un modèle exporté en centimètres, comme le tonneau et le coffre —,
    /// qu'on ramène alors à quatre mètres ; PgUp PgDn font le reste.
    /// </summary>
    Raw? RawModel(string rel)
    {
        if (_raws.TryGetValue(rel, out var have)) return have;
        Raw? outp = null;
        string path = Assets.Path(rel);
        if (System.IO.File.Exists(path))
        {
            var doc = new GltfDocument();
            var state = new GltfState();
            if (doc.AppendFromFile(path, state) == Error.Ok && doc.GenerateScene(state) is Node3D root)
            {
                Aabb? box = null;
                var stack = new Stack<(Node, Transform3D)>();
                stack.Push((root, Transform3D.Identity));
                var haze = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/hull_haze.gdshader") };
                _editHazed.Add(haze);
                while (stack.Count > 0)
                {
                    var (n, t) = stack.Pop();
                    foreach (var c in n.GetChildren()) stack.Push((c, c is Node3D c3 ? t * c3.Transform : t));
                    if (n is not MeshInstance3D mi || mi.Mesh == null) continue;
                    var b = t * mi.Mesh.GetAabb();
                    box = box is Aabb a0 ? a0.Merge(b) : b;
                    for (int i = 0; i < mi.Mesh.GetSurfaceCount(); i++)
                        if (mi.GetActiveMaterial(i) is BaseMaterial3D bm)
                        {
                            var own = (BaseMaterial3D)bm.Duplicate();
                            own.NextPass = haze;           // l'air devant, comme tout ce qui est à terre
                            mi.SetSurfaceOverrideMaterial(i, own);
                        }
                }
                if (box is Aabb bb && bb.Size.Length() > 1e-5f)
                {
                    float ext = Math.Max(bb.Size.X, Math.Max(bb.Size.Y, bb.Size.Z));
                    float k = ext > 40 ? 4f / ext : 1f;
                    var ctr = bb.GetCenter();
                    var tmpl = root;
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

    void EditCopy()
    {
        if (_sel == null) { Say("Rien de pris à copier"); return; }
        _clip = (_sel, "", _sel.FamilyLabel, _sel.Yaw, _sel.Scale, _sel.Dy);
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
        var e = SpawnAdded(_editReg!.NextAddId(), from, glb, g.X, g.Z, c.Yaw, c.Scale, c.Dy);
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
            _paletteItems.Add((e.FamilyLabel, e, ""));
        }
        _paletteItems.Sort((a, b) => string.Compare(a.Label, b.Label, StringComparison.CurrentCulture));
        foreach (var dir in new[] { "world/models", "props" })
        {
            var names = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var root in new[] { Assets.Root, Assets.Heavy })
            {
                string d = System.IO.Path.Combine(root, dir);
                if (!System.IO.Directory.Exists(d)) continue;
                foreach (var f in System.IO.Directory.GetFiles(d, "*.glb"))
                    // un relief entier ou une scène de trente mégaoctets n'est pas un objet à poser
                    if (new System.IO.FileInfo(f).Length < 25_000_000) names.Add(System.IO.Path.GetFileName(f));
            }
            foreach (var n in names) _paletteItems.Add(($"modèle brut · {dir}/{n}", null, $"{dir}/{n}"));
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
        var (label, src, glb) = _paletteItems[i];
        _clip = src != null ? (src, "", src.FamilyLabel, src.Yaw, 1.0, 0.0) : (null, glb, label, 0.0, 1.0, 0.0);
        if (_edPalette != null) _edPalette.Visible = false;
        Say($"{(src != null ? src.FamilyLabel : label)} · Ctrl+V pour le poser sous la souris");
    }

    /// <summary>La souris est-elle sur la palette ? Elle est alors à la palette, pas au monde.</summary>
    bool OverPalette(InputEvent e) =>
        _edPalette is { Visible: true } && e is InputEventMouse m && _edPalette.GetGlobalRect().HasPoint(m.Position);
}
