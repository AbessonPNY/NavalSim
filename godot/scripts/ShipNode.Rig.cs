using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using NavalSim.Core;

namespace NavalSim;

/// <summary>
/// Le gréement et la toile — _buildRig, _gaffRig, _squareRig, _jibRig,
/// _rigModel et setTrim de ship-model.js.
///
/// La toile elle-même (tissage, forme, normales, brassage) vit dans le noyau,
/// dans <see cref="SailCloth"/> et <see cref="BraceTrim"/>, que le banc de
/// parité compare à l'original. Ce qui reste ici est ce que seul le moteur sait
/// faire : les pivots, les espars, et reconnaître ceux d'un modèle importé.
/// </summary>
public partial class ShipNode
{
    /// <summary>Une voile à l'écran : sa toile (noyau) et son maillage (moteur).</summary>
    sealed class Canvas
    {
        public SailCloth Cloth = null!;
        public MeshInstance3D Node = null!;
        public ArrayMesh Mesh = null!;
        public Material Mat = null!;
        public Vector3[] V = null!, N = null!;
        public Vector2[] Uv = null!;
        public int[] Idx = null!;
        // un seul tableau de surface par voile, rempli à chaque image : en créer un
        // neuf soixante fois par seconde et par voile, c'est de la mémoire à ramasser
        public readonly Godot.Collections.Array Arrays = NewArrays();
    }

    static Godot.Collections.Array NewArrays()
    {
        var a = new Godot.Collections.Array();
        a.Resize((int)Mesh.ArrayType.Max);
        return a;
    }

    readonly List<Canvas> _canvases = new();
    // les pivots que le brassage fait tourner ; le foc, à part, aux trois quarts
    readonly List<Node3D> _rigs = new();
    Node3D? _jibRig;
    readonly BraceTrim _trim = new();
    ShaderMaterial? _canvasMat;
    readonly Dictionary<string, ShaderMaterial> _painted = new();

    /// <summary>
    /// Un mât tel qu'il a été dressé, dans le repère du nœud qui le porte : de quoi
    /// y pendre un feu. Pour un modèle, ce nœud est le groupe qui tomberait avec lui.
    /// </summary>
    readonly record struct MastAt(Node3D Parent, double Z, double Foot, double Height, double Radius);
    readonly List<MastAt> _masts = new();

    /// <summary>« 0xece4d2 », tel que la page le lit : une couleur sRGB.</summary>
    static Color Hex(string s)
    {
        int v = Convert.ToInt32(s.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? s[2..] : s, 16);
        return Color.Color8((byte)(v >> 16), (byte)(v >> 8), (byte)v);
    }

    static string RepoRoot =>
        System.IO.Path.GetDirectoryName(ShipLibrary.Folder.TrimEnd('/', '\\')) ?? "";

    void ClearRig()
    {
        _canvases.Clear();
        _rigs.Clear();
        _masts.Clear();
        _jibRig = null;
    }

    // ------------------------------------------------------------------
    //  LE GRÉEMENT PROCÉDURAL — _buildRig
    // ------------------------------------------------------------------

    /// <summary>
    /// Les mâts de la fiche et leur gréement. Ils ne sont pas décoratifs : une
    /// coque seule ne montre pas son roulis, l'œil n'ayant aucune verticale à quoi
    /// comparer l'inclinaison.
    /// </summary>
    void BuildRig()
    {
        _rig = new Node3D();
        AddChild(_rig);
        var spec = Spec;
        if (spec.Masts.Count == 0) return;              // un bâtiment à la seule machine
        double sc = spec.L / 24;                          // les espars grossissent avec elle
        var spar = MakeHullMaterial(Hex(spec.Appearance.Spar), 0.62f);
        foreach (var m in spec.Masts)
        {
            _rig.AddChild(new MeshInstance3D
            {
                Mesh = Cylinder(0.10 * sc, 0.17 * sc, m.Height, 10),
                MaterialOverride = spar,
                Position = new Vector3(0, (float)(spec.DeckMid + m.Height / 2), (float)m.Z)
            });
            _masts.Add(new MastAt(_rig, m.Z, spec.DeckMid, m.Height, 0.17 * sc));
            _rigs.Add(spec.Rig.Type == "square" ? SquareRig(m, sc, spar) : GaffRig(m, sc, spar));
        }
        if (spec.Rig.Jib != null)
        {
            _jibRig = JibRig();
            _rigs.Add(_jibRig);
        }
    }

    static CylinderMesh Cylinder(double top, double bottom, double h, int seg) => new()
    {
        TopRadius = (float)top, BottomRadius = (float)bottom, Height = (float)h, RadialSegments = seg
    };

    /// <summary>
    /// Voile aurique sur une bôme qui pivote. Le groupe tourne autour du mât :
    /// border ou choquer fait tourner la bôme et sa toile ensemble.
    /// </summary>
    Node3D GaffRig(MastSpec m, double sc, Material spar)
    {
        var spec = Spec;
        double tackY = spec.DeckMid + m.TackAbove;
        var rig = new Node3D { Position = new Vector3(0, 0, (float)m.Z) };
        rig.AddChild(new MeshInstance3D
        {
            Mesh = Cylinder(0.07 * sc, 0.09 * sc, m.Boom, 8),
            MaterialOverride = spar,
            Rotation = new Vector3(Mathf.Pi / 2, 0, 0),
            Position = new Vector3(0, (float)tackY, (float)(-m.Boom * 0.45))
        });
        // taillée plate, dans l'axe ; le creux vient de la forme, sous le vent
        rig.AddChild(SailSurface(new[]
        {
            new Vec3d(0, tackY + 0.05, -0.2 * sc),
            new Vec3d(0, tackY, -m.Boom * 0.93),
            new Vec3d(0, spec.DeckMid + m.Height - 1.0 * sc, -m.Boom * 0.70),
            new Vec3d(0, spec.DeckMid + m.Height - 0.6 * sc, -0.2 * sc)
            // lacée sur trois côtés ; le plus creux aux quatre dixièmes derrière le guindant
        }, new Vec3d(1, 0, 0), new SailCut { Kind = "gaff", UPeak = 0.42, Crown = 0.80 }));
        _rig.AddChild(rig);
        return rig;
    }

    /// <summary>
    /// Gréement carré : des vergues croisées sur le mât, chacune portant sa
    /// voile. Le groupe entier brasse d'un bloc, comme on règle un carré — on
    /// brasse les vergues, on ne choque pas une bôme.
    /// </summary>
    Node3D SquareRig(MastSpec m, double sc, Material spar)
    {
        var spec = Spec;
        var rig = new Node3D { Position = new Vector3(0, 0, (float)m.Z) };
        double halfSpan = spec.B * m.YardSpan * 0.5;
        for (int i = 0; i < m.Yards.Count; i++)
        {
            double frac = m.Yards[i];
            double y = spec.DeckMid + m.Height * frac;
            double span = halfSpan * (1 - i * 0.16);            // plus étroites en montant
            double drop = m.Height * (i + 1 < m.Yards.Count ? (m.Yards[i + 1] - frac) * 0.80 : 0.16);
            rig.AddChild(new MeshInstance3D
            {
                Mesh = Cylinder(0.07 * sc, 0.05 * sc, span * 2, 8),
                MaterialOverride = spar,
                Rotation = new Vector3(0, 0, Mathf.Pi / 2),    // en travers
                Position = new Vector3(0, (float)y, 0)
            });
            /* La plus creuse EN HAUT, juste sous sa vergue, et presque plate à la
               bordure : ce sont les écoutes qui en décident, tirant ses points
               d'écoute vers les bouts de la vergue du dessous. */
            rig.AddChild(SailSurface(new[]
            {
                new Vec3d(-span, y, 0), new Vec3d(span, y, 0),
                new Vec3d(span * 0.86, y - drop, 0), new Vec3d(-span * 0.86, y - drop, 0)
            }, new Vec3d(0, 0, 1), SailCut.Square()));
        }
        _rig.AddChild(rig);
        return rig;
    }

    /// <summary>Le foc, établi volant : il pivote sur l'étai, à l'étrave.</summary>
    Node3D JibRig()
    {
        var spec = Spec;
        var j = spec.Rig.Jib!;
        var rig = new Node3D { Position = new Vector3(0, 0, (float)(spec.L / 2)) };
        var fore = spec.Masts[0];
        double back = fore.Z - spec.L / 2;               // le mât le plus avant, dans ce repère
        rig.AddChild(SailSurface(new[]
        {
            new Vec3d(0, Lines.DeckY(1) + j.TackAbove, spec.JibFootZ),
            new Vec3d(0, spec.DeckMid + j.ClewAbove, back + 0.6),
            new Vec3d(0, spec.DeckMid + fore.Height - j.HeadDrop, back + 0.15)
            // mailloché sur son étai le long du guindant, mais la bordure vole libre
        }, new Vec3d(1, 0, 0), new SailCut { Kind = "jib", UPeak = 0.40, VPeak = 0.34, VPin0 = false, Crown = 0.80 }));
        _rig.AddChild(rig);
        return rig;
    }

    // ------------------------------------------------------------------
    //  LA TOILE À L'ÉCRAN
    // ------------------------------------------------------------------

    /// <summary>
    /// UNE MATIÈRE PAR SORTE de voile et non par voile : un motif appartient aux
    /// basses voiles et aux huniers, pas au foc ; et un galion porte neuf carrés,
    /// qui seraient neuf programmes là où un suffit.
    /// </summary>
    Material CanvasMat(string kind)
    {
        var a = Spec.Appearance;
        string? src = null;
        if (a.CanvasMap != null && a.CanvasMap.TryGetValue(kind, out var s)) src = s;
        if (src == null)
        {
            _canvasMat ??= NewCanvas(null);
            return _canvasMat;
        }
        if (!_painted.TryGetValue(kind, out var m))
        {
            m = NewCanvas(src);
            _painted[kind] = m;
        }
        return m;
    }

    ShaderMaterial NewCanvas(string? mapSrc)
    {
        var m = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/sail.gdshader") };
        m.SetShaderParameter(U.Canvas, Hex(Spec.Appearance.Canvas));
        m.SetShaderParameter(U.Emissive, Hex("0x8d866f"));
        if (mapSrc != null)
        {
            var img = Image.LoadFromFile(System.IO.Path.Combine(RepoRoot, mapSrc));
            if (img != null && !img.IsEmpty())
            {
                img.GenerateMipmaps();
                m.SetShaderParameter(U.Map, ImageTexture.CreateFromImage(img));
                m.SetShaderParameter(U.HasMap, 1.0f);
            }
            else GD.PushWarning($"[voile] texture introuvable : {mapSrc} — la toile reste unie");
        }
        Hazed.Add(m);
        return m;
    }

    MeshInstance3D SailSurface(Vec3d[] corners, Vec3d dir, SailCut cut)
    {
        var cloth = new SailCloth(corners, dir, cut);
        int n = cloth.U.Length;
        var c = new Canvas
        {
            Cloth = cloth,
            Mesh = new ArrayMesh(),
            Mat = CanvasMat(cut.Kind),
            V = new Vector3[n], N = new Vector3[n], Uv = new Vector2[n],
            Idx = new int[cloth.Indices.Length]
        };
        for (int k = 0; k < n; k++) c.Uv[k] = new Vector2(cloth.Uvs[k * 2], cloth.Uvs[k * 2 + 1]);
        /* LE SENS DES TRIANGLES EST RETOURNÉ. three.js tient pour face avant les
           triangles tournant en sens direct, Godot en sens horaire ; les normales
           venant du noyau, garder l'ordre d'origine ferait éclairer la toile par
           la face qui n'est pas au soleil. */
        for (int t = 0; t < c.Idx.Length; t += 3)
        {
            c.Idx[t] = cloth.Indices[t];
            c.Idx[t + 1] = cloth.Indices[t + 2];
            c.Idx[t + 2] = cloth.Indices[t + 1];
        }
        c.Node = new MeshInstance3D { Mesh = c.Mesh, CastShadow = GeometryInstance3D.ShadowCastingSetting.On };
        Upload(c);
        _canvases.Add(c);
        return c.Node;
    }

    static void Upload(Canvas c)
    {
        var p = c.Cloth.Positions;
        var nr = c.Cloth.Normals;
        for (int k = 0; k < c.V.Length; k++)
        {
            c.V[k] = new Vector3(p[k * 3], p[k * 3 + 1], p[k * 3 + 2]);
            c.N[k] = new Vector3(nr[k * 3], nr[k * 3 + 1], nr[k * 3 + 2]);
        }
        var arrays = c.Arrays;
        arrays.SetNow((int)Mesh.ArrayType.Vertex, c.V);
        arrays.SetNow((int)Mesh.ArrayType.Normal, c.N);
        arrays.SetNow((int)Mesh.ArrayType.TexUV, c.Uv);
        arrays.SetNow((int)Mesh.ArrayType.Index, c.Idx);
        c.Mesh.ClearSurfaces();
        c.Mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        c.Mesh.SurfaceSetMaterial(0, c.Mat);
    }

    /// <summary>
    /// Brasser et former — <c>setTrim</c>. L'écoute, le bord, la toile établie, le
    /// faseyement et la charge viennent du solveur à chaque image ; aucun n'est
    /// retenu ici, sinon l'angle des vergues qui, lui, vient de loin.
    /// </summary>
    public void SetTrim(double sheet, int tack, double set, bool luffing, double t, double load)
    {
        if (_rigs.Count == 0) return;                   // pas de toile à régler
        double angle = _trim.Update(sheet, tack, luffing, t);
        /* Ferler rentre la toile, pas les espars — un bâtiment à sec de toile a
           toujours ses vergues croisées. Et pour un modèle importé, dont les
           vergues pendent dans ces pivots, cacher le groupe dépouillerait ses
           mâts. */
        foreach (var rig in _rigs)
            rig.Rotation = new Vector3(0, (float)(rig == _jibRig ? angle * 0.75 : angle), 0);
        foreach (var c in _canvases)
        {
            c.Cloth.Shape(Spec.Rig.Belly, load, luffing, t, set);
            Upload(c);
        }
    }

    // ------------------------------------------------------------------
    //  LE GRÉEMENT D'UN MODÈLE — _rigModel
    // ------------------------------------------------------------------

    /* LES NOMS QUI DISENT « CECI ÉCLAIRE » : vitrage, fanal, lampe — GLOW_NAMES de
       la page. Ils servent deux fois : à allumer la nuit, et à REFUSER ces pièces
       au gréement, une vergue étant en bois, jamais en verre. */
    static readonly Regex GlowNames = new(
        "fenetre|fenêtre|window|vitre|hublot|glass|verre|lamp|lanterne|lantern|glow",
        RegexOptions.IgnoreCase);

    /// <summary>
    /// UNE PRIMITIVE, UN MAILLAGE, comme le fait GLTFLoader de three.js. Godot
    /// réunit les primitives d'un nœud en surfaces d'un seul MeshInstance3D ;
    /// three en fait des maillages frères. Or tout ce qui suit raisonne PIÈCE par
    /// pièce — la plus volumineuse est la coque, une longue pièce mince en travers
    /// est une vergue —, et une vergue soudée à une autre primitive ne serait
    /// jamais reconnue. Chaque surface devient donc son propre enfant.
    /// </summary>
    static void SplitPrimitives(Node3D root)
    {
        var multi = new List<MeshInstance3D>();
        foreach (var (mi, _) in Meshes(root))
            if (mi.Mesh.GetSurfaceCount() > 1) multi.Add(mi);
        foreach (var mi in multi)
        {
            var mesh = mi.Mesh;
            for (int s = 0; s < mesh.GetSurfaceCount(); s++)
            {
                var am = new ArrayMesh();
                am.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, mesh.SurfaceGetArrays(s));
                am.SurfaceSetMaterial(0, mi.GetSurfaceOverrideMaterial(s) ?? mesh.SurfaceGetMaterial(s));
                mi.AddChild(new MeshInstance3D { Mesh = am, Name = $"{mi.Name}_{s}" });
            }
            mi.Mesh = null;
        }
    }

    /// <summary>
    /// LE RELIEF TIRÉ DE LA RUGOSITÉ — _reliefFromRoughness. Chaque matière qui
    /// porte une rugosité et pas de carte normale reçoit le relief peint dans ce
    /// même gris (ReliefMap, dans le noyau). Une matière qui a déjà une vraie
    /// carte normale est laissée telle quelle.
    ///
    /// La carte sort dans la convention glTF — vert vers le haut de l'image —, qui
    /// est celle que Godot attend : pas de retournement ici. La page en posait un
    /// (normalScale (1, −1)) parce que three, sans tangentes, en recalcule un repère
    /// retourné ; Godot, lui, veut des TANGENTES, que ce modèle n'a pas forcément :
    /// elles sont générées pour chaque surface qui reçoit un relief.
    /// </summary>
    static void ReliefFromRoughness(Node3D root, double strength)
    {
        if (!(strength > 0)) return;
        var made = new Dictionary<Texture2D, ImageTexture?>();   // une carte par image source
        foreach (var (mi, _) in Meshes(root))
        {
            var mesh = mi.Mesh;
            if (mesh.GetSurfaceCount() != 1) continue;           // SplitPrimitives est passé
            if ((mi.GetSurfaceOverrideMaterial(0) ?? mesh.SurfaceGetMaterial(0)) is not BaseMaterial3D mat) continue;
            if (mat.RoughnessTexture == null || mat.NormalEnabled) continue;
            if (!made.TryGetValue(mat.RoughnessTexture, out var nt))
            {
                nt = MakeRelief(mat.RoughnessTexture, strength, mat.RoughnessTextureChannel);
                made[mat.RoughnessTexture] = nt;
            }
            if (nt == null) continue;
            mat.NormalEnabled = true;
            mat.NormalTexture = nt;
            mat.NormalScale = 1;

            var arrays = mesh.SurfaceGetArrays(0);
            if (arrays[(int)Mesh.ArrayType.Tangent].VariantType == Variant.Type.Nil
                && arrays[(int)Mesh.ArrayType.TexUV].VariantType != Variant.Type.Nil)
            {
                var st = new SurfaceTool();
                st.CreateFromArrays(arrays);
                st.GenerateTangents();
                var am = st.Commit();
                am.SurfaceSetMaterial(0, mat);
                mi.Mesh = am;
            }
        }
    }

    static ImageTexture? MakeRelief(Texture2D src, double strength, BaseMaterial3D.TextureChannel ch)
    {
        var img = src.GetImage();
        if (img == null || img.IsEmpty()) return null;
        img = (Image)img.Duplicate();
        if (img.IsCompressed()) img.Decompress();
        img.Convert(Image.Format.Rgba8);
        int channel = ch switch
        {
            BaseMaterial3D.TextureChannel.Red => 0,
            BaseMaterial3D.TextureChannel.Blue => 2,
            BaseMaterial3D.TextureChannel.Alpha => 3,
            _ => 1                                      // la rugosité glTF est dans le vert
        };
        int w = img.GetWidth(), h = img.GetHeight();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var outp = ReliefMap.FromHeight(img.GetData(), w, h, strength, channel);
        long sum = 0;
        foreach (var b in outp) sum += b;
        GD.Print($"relief {w}x{h} canal {channel} : somme {sum}, {sw.ElapsedMilliseconds} ms");
        var nimg = Image.CreateFromData(w, h, false, Image.Format.Rgba8, outp);
        nimg.GenerateMipmaps();
        return ImageTexture.CreateFromImage(nimg);
    }

    /// <summary>
    /// L'obstacle qu'elle oppose à l'embrun : son contour de flottaison, le dessus
    /// de sa coque — lu sur le modèle, châteaux compris, ou sur le plan de formes —
    /// et sa quille. Voir HullCollider, dans le noyau.
    /// </summary>
    public HullCollider MakeCollider(HullProfile prof)
    {
        Func<double, double> keel = z => Lines.KeelY(Math.Clamp(z / Spec.L + 0.5, 0, 1));
        if (ModelRoot != null)
        {
            var parts = ModelParts();
            if (parts.Count > 0) return new HullCollider(Physics, prof, DeckProfile(parts), keel);
        }
        return new HullCollider(Physics, prof, z => Lines.DeckY(Math.Clamp(z / Spec.L + 0.5, 0, 1)), keel);
    }

    /// <summary>Une pièce du modèle, mesurée dans le repère du navire.</summary>
    sealed class Part
    {
        public MeshInstance3D Mi = null!;
        public Vector3 Min, Max, Size, Mid;
        public Vector3[] Verts = null!;
        public bool Taken;
    }

    /// <summary>
    /// Chaque pièce du modèle, ses sommets ramenés dans le repère du navire et sa
    /// boîte prise sur eux — _modelParts. Une fois par mise à l'eau, sur quelques
    /// maillages : moins cher que de promener une seconde description du modèle.
    /// </summary>
    List<Part> ModelParts()
    {
        var parts = new List<Part>();
        foreach (var (mi, rel) in Meshes(ModelRoot!))
        {
            var src = mi.Mesh.SurfaceGetArrays(0)[(int)Mesh.ArrayType.Vertex].AsVector3Array();
            if (src.Length == 0) continue;
            var v = new Vector3[src.Length];
            Vector3 lo = new(float.MaxValue, float.MaxValue, float.MaxValue), hi = -lo;
            for (int i = 0; i < src.Length; i++)
            {
                v[i] = rel * src[i];
                lo = lo.Min(v[i]); hi = hi.Max(v[i]);
            }
            parts.Add(new Part { Mi = mi, Min = lo, Max = hi, Size = hi - lo, Mid = (lo + hi) * 0.5f, Verts = v });
        }
        return parts;
    }

    /// <summary>
    /// La hauteur de son pont le long d'elle, lue sur la coque du modèle —
    /// _deckProfile. Aucune voile ne doit pendre à travers le navire, et seul le
    /// modèle sait où est le pont ; un chiffre pour tout le bâtiment ne ferait pas
    /// l'affaire, une dunette montant parfois de dix mètres au-dessus du passavant.
    /// </summary>
    static Func<double, double> DeckProfile(List<Part> parts)
    {
        Part hull = parts[0];
        double best = -1;
        foreach (var p in parts)
        {
            double v = (double)p.Size.X * p.Size.Y * p.Size.Z;   // la coque est de loin la plus grosse
            if (v > best) { best = v; hull = p; }
        }
        const int N = 24;
        double z0 = hull.Min.Z;
        double step = (hull.Max.Z - z0) / N;
        if (step == 0) step = 1;
        var top = Enumerable.Repeat(double.NegativeInfinity, N).ToArray();
        int Bin(double z) => Math.Min(N - 1, Math.Max(0, (int)Math.Floor((z - z0) / step)));
        foreach (var v in hull.Verts)
        {
            int b = Bin(v.Z);
            if (v.Y > top[b]) top[b] = v.Y;
        }
        // hors des bouts de la coque — sous le beaupré — la station la plus proche qui ait du navire
        return z =>
        {
            int b = Bin(z);
            for (int d = 0; d < N; d++)
            {
                if (b - d >= 0 && top[b - d] > double.NegativeInfinity) return top[b - d];
                if (b + d < N && top[b + d] > double.NegativeInfinity) return top[b + d];
            }
            return 0;
        };
    }

    /// <summary>
    /// Pendre de la toile aux espars du modèle lui-même.
    ///
    /// Un .glb arrive avec une coque et des espars nus mais, en règle générale,
    /// sans voiles, et ses nœuds portent les noms par défaut de Blender : rien par
    /// quoi les chercher. Ce qu'il a, ce sont des vergues, et une vergue se
    /// reconnaît à sa FORME : bien plus large en travers qu'épaisse, posée
    /// d'équerre sur l'axe. Les lire sur la géométrie veut dire que tout
    /// trois-mâts carré déposé dans ships/models sort gréé, sans une ligne de
    /// donnée par navire.
    ///
    /// Les vergues sont ensuite reparentées DANS les pivots qui portent les
    /// voiles, si bien que brasser fait tourner espar et toile d'un bloc. Tourner
    /// la toile seule la ferait glisser hors de sa propre vergue.
    /// </summary>
    void RigModel()
    {
        var spec = Spec;
        if (!(spec.SailArea > 0)) return;                 // elle n'est pas faite pour porter de la toile
        var parts = ModelParts();
        if (parts.Count == 0) return;

        /* CE QUI N'EST PAS DU BOIS N'EST PAS UN ESPAR : une fenêtre de château
           arrière, 4,2 m de large pour 0,44 d'épaisseur, en travers et sur l'axe,
           passe toutes les épreuves de forme d'une vergue. La forme ne tranche pas
           ce cas ; la MATIÈRE, si. Et la fiche peut nommer ce qu'elle tient à
           l'écart (`model.rigIgnore`). */
        var ignore = spec.Model?.RigIgnore ?? new List<string>();
        bool Bois(Part p)
        {
            var mat = p.Mi.GetSurfaceOverrideMaterial(0) ?? p.Mi.Mesh.SurfaceGetMaterial(0);
            if (mat != null && GlowNames.IsMatch(mat.ResourceName ?? "")) return false;
            string name = p.Mi.Name.ToString().ToLowerInvariant();
            return !ignore.Any(s => name.Contains(s.ToLowerInvariant()));
        }
        bool FormeVergue(Part p)
        {
            double across = p.Size.X, thick = Math.Max(p.Size.Y, p.Size.Z);
            return across > 4 * thick                    // longue et mince, et mince dans le bon sens
                && across > 0.25 * spec.B                // un espar, pas un morceau d'accastillage
                && Math.Abs(p.Mid.X) < 0.15 * across;    // bien d'équerre sur l'axe
        }
        /* Et les MÂTS, par la même épreuve debout : hauts, minces DANS LES DEUX
           SENS, sur l'axe. C'est « minces dans les deux sens » qui travaille : cela
           écarte tout ce qui est soudé à ses voisins, l'état habituel d'un modèle
           importé. */
        bool FormeMat(Part p)
        {
            double tall = p.Size.Y, thick = Math.Max(p.Size.X, p.Size.Z);
            return tall > 4 * thick && tall > 0.20 * spec.L && Math.Abs(p.Mid.X) < 0.12 * spec.B;
        }
        var yards = parts.Where(p => FormeVergue(p) && Bois(p)).ToList();
        var poles = parts.Where(p => FormeMat(p) && Bois(p)).ToList();
        var refused = parts.Where(p => !Bois(p) && (FormeVergue(p) || FormeMat(p))).ToList();
        if (refused.Count > 0)
            GD.PushWarning($"[{spec.Id}] pièces de la forme d'un espar tenues hors du gréement "
                         + $"(vitrage, fanal ou model.rigIgnore) : {string.Join(", ", refused.Select(p => p.Mi.Name))}");
        var deckAt = DeckProfile(parts);

        if (yards.Count == 0)
        {
            GD.PushWarning($"[{spec.Id}] aucune vergue dans le modèle — elle navigue à sec de toile. "
                         + "Un modèle aurique ou latin demande son propre chemin de gréement.");
            return;
        }

        /* Les ranger par mât. Les vergues se serrent en z autour de leur mât, et
           l'écart entre mâts est d'un ordre de grandeur au-dessus : un seul test
           d'écart les sépare. Tri stable, comme celui de la page. */
        yards = yards.OrderBy(p => p.Mid.Z).ToList();
        var masts = new List<List<Part>>();
        double together = 0.06 * spec.L;
        foreach (var y in yards)
        {
            var last = masts.Count > 0 ? masts[^1] : null;
            if (last != null && Math.Abs(y.Mid.Z - last[0].Mid.Z) < together) last.Add(y);
            else masts.Add(new List<Part> { y });
        }

        foreach (var mastYards in masts)
        {
            var mast = mastYards.OrderByDescending(p => p.Mid.Y).ToList();   // la plus haute d'abord
            double z0 = mast.Sum(y => (double)y.Mid.Z) / mast.Count;

            /* DEUX GROUPES EMBOÎTÉS. Un mât tombe sur son PIED, donc ce qui tourne
               doit avoir son origine au pied ; le brassage, lui, est une rotation
               autour de la verticale, qui est la même où que soit l'origine sur cet
               axe. Le groupe extérieur descend donc au pied sans rien coûter, et le
               brassage tourne l'intérieur exactement comme avant. */
            Part? pole = null;
            double near = 0.06 * spec.L;
            foreach (var p in poles)
            {
                double d = Math.Abs(p.Mid.Z - z0);
                if (!p.Taken && d < near) { near = d; pole = p; }
            }
            if (pole != null) pole.Taken = true;
            double heel = pole != null ? pole.Min.Y : deckAt(z0);

            var fall = new Node3D { Position = new Vector3(0, (float)heel, (float)z0) };
            AddChild(fall);
            // reparenter en GARDANT sa place : l'espar reste exactement où le modéliste l'a mis
            pole?.Mi.Reparent(fall, true);

            var pivot = new Node3D { Position = new Vector3(0, (float)-heel, 0) };
            fall.AddChild(pivot);

            for (int i = 0; i < mast.Count; i++)
            {
                var y = mast[i];
                double half = y.Size.X * 0.5 * 0.97;
                y.Mi.Reparent(pivot, true);              // brasse avec le mât, toile ou non

                /* Une voile pend jusqu'un peu avant la vergue du dessous. La plus
                   basse emprunte l'écart du dessus ; aucune ne dépasse la moitié de
                   sa largeur en profondeur ; et aucune ne descend sous le pont — une
                   basse voile est bordée au pavois, pas à travers. */
                var above = i > 0 ? mast[i - 1] : null;
                var below = i + 1 < mast.Count ? mast[i + 1] : null;
                double gap = below != null ? y.Mid.Y - below.Mid.Y
                           : above != null ? above.Mid.Y - y.Mid.Y
                           : half * 2;
                double dz = y.Mid.Z - z0, yy = y.Mid.Y;
                double drop = Math.Min(Math.Min(0.82 * gap, 1.1 * half), yy - deckAt(y.Mid.Z) - 0.02 * spec.L);
                if (drop < 0.2 * half) continue;         // trop près du pont pour être une vergue

                pivot.AddChild(SailSurface(new[]
                {
                    new Vec3d(-half, yy, dz), new Vec3d(half, yy, dz),
                    new Vec3d(half * 0.86, yy - drop, dz), new Vec3d(-half * 0.86, yy - drop, dz)
                }, new Vec3d(0, 0, 1), SailCut.Square()));
                RigLog.Add(FormattableString.Invariant($"voile {half:F2} {yy:F2} {drop:F2}"));
            }
            RigLog.Add(FormattableString.Invariant(
                $"mat z0 {z0:F2} pied {heel:F2} vergues {mast.Count} {(pole != null ? "espar" : "sans espar")}"));
            _masts.Add(new MastAt(fall,
                pole != null ? pole.Mid.Z - z0 : 0, 0,
                pole != null ? pole.Size.Y : mast[0].Mid.Y - heel,
                pole != null ? Math.Max(pole.Size.X, pole.Size.Z) * 0.5 : 0.3));
            _rigs.Add(pivot);
        }
    }

    /// <summary>
    /// Ce que RigModel a reconnu, ligne à ligne : à poser à côté de rigs et
    /// canvases dans la page, pour vérifier qu'on a lu le MÊME gréement.
    /// </summary>
    public readonly List<string> RigLog = new();
}
