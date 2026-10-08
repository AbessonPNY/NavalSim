using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using NavalSim.Core;

namespace NavalSim;

/// <summary>
/// LE GRÉEMENT D'UN MODÈLE IMPORTÉ — la moitié de ShipNode.Rig.cs qui ne
/// DESSINE pas un gréement mais le RECONNAÎT.
///
/// Les deux moitiés ne font pas le même métier. L'autre bâtit des mâts et des
/// vergues d'après une fiche, et sait donc exactement où tout se trouve ; ici,
/// on reçoit un maillage fait par quelqu'un d'autre et il faut y LIRE le
/// gréement — quel îlot de triangles est un mât, lequel une vergue, lequel un
/// beaupré, et lesquels n'en sont pas malgré leur forme. C'est un travail de
/// reconnaissance, avec ses épreuves et ses refus, et il tenait plus de place
/// que tout le reste du fichier réuni.
///
/// Rien n'a changé en sortant : le même code, sous la même classe partielle.
/// </summary>
public partial class ShipNode
{
    // ------------------------------------------------------------------
    //  LE GRÉEMENT D'UN MODÈLE — _rigModel
    // ------------------------------------------------------------------

    /* LES NOMS QUI DISENT « CECI ÉCLAIRE » : vitrage, fanal, lampe — GLOW_NAMES de
       la page. Ils servent deux fois : à allumer la nuit, et à REFUSER ces pièces
       au gréement, une vergue étant en bois, jamais en verre. */
    /// <summary>Ce qui PEND plutôt que ce qui tient : à écarter des espars.</summary>
    static readonly Regex Cordage = new(
        "b[ée]zier|curve|courbe|cordage|rope|stay|[ée]tai|hauban|shroud|drisse|line",
        RegexOptions.IgnoreCase);

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

    /// <summary>
    /// LES TANGENTES QU'UNE NORMAL MAP EXIGE. Godot lit le relief dans le repère
    /// de la surface, et ce repère vient des TANGENTES : sans elles, une carte
    /// livrée par le .glb ne fait rien du tout — ni erreur, ni relief. Le
    /// chargement à l'exécution (GltfDocument) ne les fabrique pas, alors on les
    /// fabrique ici, pour toute surface qui porte un relief et n'en a pas.
    /// </summary>
    static void TangentsForRelief(Node3D root)
    {
        foreach (var (mi, _) in Meshes(root))
        {
            var mesh = mi.Mesh;
            if (mesh.GetSurfaceCount() != 1) continue;
            if ((mi.GetSurfaceOverrideMaterial(0) ?? mesh.SurfaceGetMaterial(0)) is not BaseMaterial3D mat) continue;
            if (!mat.NormalEnabled || mat.NormalTexture == null) continue;
            var arrays = mesh.SurfaceGetArrays(0);
            if (arrays[(int)Mesh.ArrayType.Tangent].VariantType != Variant.Type.Nil) continue;
            if (arrays[(int)Mesh.ArrayType.TexUV].VariantType == Variant.Type.Nil) continue;
            var st = new SurfaceTool();
            st.CreateFromArrays(arrays);
            st.GenerateTangents();
            var am = st.Commit();
            am.SurfaceSetMaterial(0, mat);
            mi.Mesh = am;
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
        public Transform3D Rel;
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
            parts.Add(new Part { Mi = mi, Min = lo, Max = hi, Size = hi - lo, Mid = (lo + hi) * 0.5f, Verts = v, Rel = rel });
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
    /// trois-mâts carré déposé dans le dossier d un navire sort gréé, sans une ligne de
    /// donnée par navire.
    ///
    /// Les vergues sont ensuite reparentées DANS les pivots qui portent les
    /// voiles, si bien que brasser fait tourner espar et toile d'un bloc. Tourner
    /// la toile seule la ferait glisser hors de sa propre vergue.
    /// </summary>
    /// <summary>
    /// Couper un maillage en îlots de triangles — reliés par leurs indices OU par
    /// des sommets à la même place (un cylindre exporté a ses sommets doublés aux
    /// arêtes vives, pour les normales, et ne doit pas partir en lanières). Les
    /// îlots remplacent l'original, même matière, même place, si l'un au moins
    /// passe <paramref name="keep"/> ; sinon rien n'est touché.
    /// </summary>
    bool SplitIslands(MeshInstance3D mi, Func<Part, bool> keep)
    {
        var mesh = mi.Mesh;
        var rel = RelOf(mi);
        var made = new List<(ArrayMesh Mesh, Material? Mat, Part Box)>();
        for (int s = 0; s < mesh.GetSurfaceCount(); s++)
        {
            var a = mesh.SurfaceGetArrays(s);
            var v = a[(int)Mesh.ArrayType.Vertex].AsVector3Array();
            var idxV = a[(int)Mesh.ArrayType.Index];
            int[] idx = idxV.VariantType == Variant.Type.Nil ? Enumerable.Range(0, v.Length).ToArray() : idxV.AsInt32Array();
            var parent = Enumerable.Range(0, v.Length).ToArray();
            int Find(int x) { while (parent[x] != x) x = parent[x] = parent[parent[x]]; return x; }
            void Join(int x, int y) { x = Find(x); y = Find(y); if (x != y) parent[x] = y; }
            var at = new Dictionary<(int, int, int), int>();
            for (int i = 0; i < v.Length; i++)
            {
                var key = ((int)Math.Round(v[i].X * 1000), (int)Math.Round(v[i].Y * 1000), (int)Math.Round(v[i].Z * 1000));
                if (at.TryGetValue(key, out int j)) Join(i, j); else at[key] = i;
            }
            for (int t = 0; t + 2 < idx.Length; t += 3) { Join(idx[t], idx[t + 1]); Join(idx[t], idx[t + 2]); }
            var groups = new Dictionary<int, List<int>>();
            for (int t = 0; t + 2 < idx.Length; t += 3)
            {
                int g = Find(idx[t]);
                if (!groups.TryGetValue(g, out var list)) groups[g] = list = new List<int>();
                list.Add(t);
            }
            var mat = mi.GetActiveMaterial(s);
            foreach (var tris in groups.Values)
            {
                // ses sommets, renumérotés, avec tout ce qu'ils portent
                var map = new Dictionary<int, int>();
                var order = new List<int>();
                var ni = new int[tris.Count * 3];
                for (int k = 0; k < tris.Count; k++)
                    for (int c = 0; c < 3; c++)
                    {
                        int o = idx[tris[k] + c];
                        if (!map.TryGetValue(o, out int n)) { n = map[o] = order.Count; order.Add(o); }
                        ni[k * 3 + c] = n;
                    }
                var outA = new Godot.Collections.Array();
                outA.Resize((int)Mesh.ArrayType.Max);
                var vv = new Vector3[order.Count];
                for (int k = 0; k < vv.Length; k++) vv[k] = v[order[k]];
                outA[(int)Mesh.ArrayType.Vertex] = vv;
                if (a[(int)Mesh.ArrayType.Normal].VariantType != Variant.Type.Nil)
                {
                    var src = a[(int)Mesh.ArrayType.Normal].AsVector3Array();
                    var nn = new Vector3[order.Count];
                    for (int k = 0; k < nn.Length; k++) nn[k] = src[order[k]];
                    outA[(int)Mesh.ArrayType.Normal] = nn;
                }
                if (a[(int)Mesh.ArrayType.TexUV].VariantType != Variant.Type.Nil)
                {
                    var src = a[(int)Mesh.ArrayType.TexUV].AsVector2Array();
                    var uu = new Vector2[order.Count];
                    for (int k = 0; k < uu.Length; k++) uu[k] = src[order[k]];
                    outA[(int)Mesh.ArrayType.TexUV] = uu;
                }
                if (a[(int)Mesh.ArrayType.Color].VariantType != Variant.Type.Nil)
                {
                    var src = a[(int)Mesh.ArrayType.Color].AsColorArray();
                    var cc = new Color[order.Count];
                    for (int k = 0; k < cc.Length; k++) cc[k] = src[order[k]];
                    outA[(int)Mesh.ArrayType.Color] = cc;
                }
                outA[(int)Mesh.ArrayType.Index] = ni;
                var am = new ArrayMesh();
                am.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, outA);
                // sa boîte, dans le repère du navire, pour l'épreuve
                Vector3 lo = new(float.MaxValue, float.MaxValue, float.MaxValue), hi = -lo;
                foreach (var x in vv) { var w = rel * x; lo = lo.Min(w); hi = hi.Max(w); }
                made.Add((am, mat, new Part { Mi = mi, Min = lo, Max = hi, Size = hi - lo, Mid = (lo + hi) * 0.5f, Verts = Array.Empty<Vector3>(), Rel = rel }));
            }
        }
        if (made.Count < 2 || !made.Any(m => keep(m.Box))) return false;
        var host = mi.GetParent();
        int n0 = 0;
        foreach (var (am, mat, _) in made)
        {
            var piece = new MeshInstance3D { Mesh = am, Transform = mi.Transform, Name = $"{mi.Name}_ilot{n0++}" };
            if (mat != null) piece.MaterialOverride = mat;
            host.AddChild(piece);
        }
        host.RemoveChild(mi);
        mi.QueueFree();
        RigLog.Add($"{mi.Name} coupé en {made.Count} îlots");
        return true;
    }

    double _latZ, _latModelled;
    MeshInstance3D? _latYard;

    /// <summary>
    /// LA TOILE DE LA LATINE, pendue à l'antenne du modèle. L'antenne est ce qui,
    /// dans la pièce de l'artimon, s'écarte de l'axe du mât : son bout le plus
    /// haut est le PIC, l'autre bout l'AMURE ; le point d'écoute est sous le pic,
    /// un peu au-dessus du pont. Un triangle, taillé comme un foc.
    /// </summary>
    void LateenOn(Part pole, Node3D fall, double heel, double z0, Func<double, double> deckAt, List<Part> parts)
    {
        // l'axe du mât, pris au pied
        double foot = pole.Min.Y + 0.2 * pole.Size.Y, cx = 0, cz = 0; int n = 0;
        foreach (var v in pole.Verts) if (v.Y <= foot) { cx += v.X; cz += v.Z; n++; }
        if (n == 0) return;
        cx /= n; cz /= n;
        double r = Math.Max(0.35, 0.02 * Spec.L);
        Vector3? peak = null, tack = null;
        foreach (var v in pole.Verts)
        {
            double d2 = (v.X - cx) * (v.X - cx) + (v.Z - cz) * (v.Z - cz);
            if (d2 < r * r) continue;                                  // le mât, pas l'antenne
            if (peak == null || v.Y > peak.Value.Y) peak = v;
        }
        if (peak != null)
            foreach (var v in pole.Verts)
            {
                double d2 = (v.X - cx) * (v.X - cx) + (v.Z - cz) * (v.Z - cz);
                if (d2 < r * r) continue;
                if (tack == null || (v - peak.Value).LengthSquared() > (tack.Value - peak.Value).LengthSquared()) tack = v;
            }
        /* L'ANTENNE À PART : sur d'autres modèles le mât est seul, et l'antenne
           est sa propre pièce, en biais — ni mât ni vergue pour les épreuves de
           forme. On la cherche près de l'artimon : longue, mince en travers, sur
           l'axe ; ses deux sommets les plus éloignés sont ses deux bouts. */
        if (peak == null || tack == null || (tack.Value - peak.Value).Length() < 1)
        {
            Part? yard = null;
            foreach (var q in parts)
            {
                // l'antenne du modèle est déjà ÉCARTÉE autour du mât (25 à 30°) : large en travers, pas mince
                if (q == pole || q.Taken || Math.Abs(q.Mid.X) > 1.5) continue;
                /* PAS UN CORDAGE : une corde tendue de l'artimon au grand mât est longue, mince
                   et en biais comme une antenne — et plus grande qu'elle. Elle était prise
                   pour l'antenne, la latine y pendait et tournait au vent (relevé :
                   cordage_artimon.001). Les autres recherches d'espars les écartent déjà. */
                if (Cordage.IsMatch(q.Mi.Name.ToString())) continue;
                // au-dessus du pont, et jusqu'à mi-hauteur du mât au moins : le safran est long, mince et en biais lui aussi
                if (q.Min.Y < deckAt(q.Mid.Z) - 0.5 || q.Max.Y < heel + 0.5 * pole.Size.Y) continue;
                if (Math.Max(q.Size.Y, q.Size.Z) < 0.15 * Spec.L || Math.Abs(q.Mid.Z - cz) > 0.2 * Spec.L) continue;
                if (q.Size.Y < 0.2 * Math.Max(q.Size.Y, q.Size.Z) || q.Size.Z < 0.2 * Math.Max(q.Size.Y, q.Size.Z)) continue;   // en biais
                if (yard == null || q.Size.Y + q.Size.Z > yard.Size.Y + yard.Size.Z) yard = q;
            }
            if (yard == null || yard.Verts.Length < 2)
            {
                return;       // pas d'antenne : pas de latine dessinée
            }
            Vector3 a0 = yard.Verts[0], b0 = yard.Verts[0];
            float best = -1;
            foreach (var u in yard.Verts)
                foreach (var w in yard.Verts)
                {
                    float d = (u - w).LengthSquared();
                    if (d > best) { best = d; a0 = u; b0 = w; }
                }
            peak = a0.Y >= b0.Y ? a0 : b0;
            tack = a0.Y >= b0.Y ? b0 : a0;
            yard.Taken = true;
            _latYard = yard.Mi;
        }
        if (tack == null || (tack.Value - peak.Value).Length() < 1) return;
        var P = peak.Value; var T = tack.Value;
        /* L'ANGLE MODELÉ : l'antenne pend déjà écartée autour du mât. On pend la
           toile dessus telle qu'elle est, puis on fait tourner antenne et toile
           d'un bloc pour que cet angle devienne celui que l'équipage donne. */
        double ux = T.X - P.X, uz = T.Z - P.Z;
        if (uz < 0) { ux = -ux; uz = -uz; }                          // l'antenne vers l'avant
        _latModelled = Math.Atan2(ux, uz);
        double clewY = Math.Max(deckAt(P.Z) + 1.6, Math.Min(P.Y, T.Y) - 0.2 * Math.Abs(P.Y - T.Y));
        _latPivot = new Node3D { Position = new Vector3((float)cx, 0, (float)(cz - z0)) };
        fall.AddChild(_latPivot);
        Vec3d L(Vector3 v, double y) => new(v.X - cx, y - heel, v.Z - cz);
        // la normale de son plan : horizontale, en travers de l'antenne
        double un = Math.Sqrt(ux * ux + uz * uz);
        var side = new Vec3d(uz / un, 0, -ux / un);
        _latPivot.AddChild(SailSurface(new[] { L(T, T.Y), L(P, clewY), L(P, P.Y) },
            /* SA PROPRE SORTE, et c'est ce qui lui donne droit à sa propre texture.
               Elle partageait « jib » avec les focs : une image posée sous cette
               clé serait arrivée sur les trois. Rien d'autre ne dépend du nom —
               seul « square » branche quelque part (seize colonnes et des festons
               de ferlage) —, donc la toile est taillée exactement comme avant. */
            side, new SailCut { Kind = "lateen", UPeak = 0.40, VPeak = 0.34, VPin0 = true, Crown = 0.80 }));
        // l'antenne tourne avec sa toile, et tombe avec son mât
        _latYard?.Reparent(_latPivot, true);
        _latMast = _damage.Count - 1;
        _canvases[^1].Mast = _latMast;
        /* ET SON MÂT REÇOIT SA PART, qu'il n'avait pas.
         *
         * Un mât sans vergue carrée naît avec une part NULLE, ce qui est juste à
         * la seconde où il est planté : il ne porte rien. Puis on lui grée une
         * antenne, et personne ne revenait le lui dire — si bien que l'artimon
         * d'un galion comptait pour zéro dans tout ce qui pèse la toile. Sa
         * latine pouvait brûler, se trouer, partir avec le kraken : la poussée du
         * navire n'en savait rien (relevé : m3 part 0).
         *
         * L'aire du triangle qu'on vient de tendre, dans la même unité que les
         * voiles carrées — leur part est aussi une surface prise sur le modèle,
         * et non sur la fiche : deux mesures du même monde. */
        var lt = L(T, T.Y); var lc = L(P, clewY); var lp = L(P, P.Y);
        var e1 = lc - lt; var e2 = lp - lt;
        double aire = 0.5 * new Vec3d(e1.Y * e2.Z - e1.Z * e2.Y,
                                      e1.Z * e2.X - e1.X * e2.Z,
                                      e1.X * e2.Y - e1.Y * e2.X).Length;
        _damage[_latMast].Share += aire;
        RigLog.Add(FormattableString.Invariant($"latine : part {aire:F0} m² au mât {_latMast}"));
        _latZ = z0;
        RigLog.Add(FormattableString.Invariant($"latine : pic y {P.Y:F2} z {P.Z:F2}, amure y {T.Y:F2} z {T.Z:F2}, écoute y {clewY:F2}"));
    }

    /// <summary>La place d'un maillage dans le repère du navire.</summary>
    Transform3D RelOf(Node3D n)
    {
        var t = n.Transform;
        for (var q = n.GetParent() as Node3D; q != null && q != ModelRoot; q = q.GetParent() as Node3D) t = q.Transform * t;
        return ModelRoot!.Transform * t;
    }

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
        /* L'ÉPREUVE DU BEAUPRÉ : long vers l'AVANT, mince dans les deux autres
           sens, sur l'axe, et qui DÉBORDE l'étrave. Le dernier point est celui
           qui compte — une lisse de pont est longue, mince et sur l'axe elle
           aussi, mais elle s'arrête au bordé. */
        bool FormeBeaupre(Part p)
        {
            double along = p.Size.Z;
            /* SON ÉPAISSEUR EST EN TRAVERS, ET SEULEMENT LÀ. Mesurée aussi en
               hauteur, comme pour un mât, elle valait la QUÊTE et non le bois :
               le beaupré de la frégate monte de 5,17 m sur 11,22 de long, et
               l'épreuve le refusait parce qu'il était « épais » de cinq mètres
               (relevé — Cylinder_002, x 0,33 y 5,17 z 11,22). C'est bien un
               cylindre de trente-trois centimètres, simplement en pente.

               Huit fois plus long que large ET moins d'un huitième de bau : deux
               gardes plutôt qu'une, parce que la COQUE passe la première toute
               seule — trente mètres de long sur six de large, sur l'axe, et qui
               déborde l'étrave comme lui. La lui faire tomber serait mémorable. */
            /* ET CE N'EST PAS UN BOUT. Un étai qui monte du bâton de foc à la hune
               est long, mince, sur l'axe et déborde l'étrave tout comme lui : sur
               la Belliqueuse une « BézierCurve » allait à 49,61 m et donnait un
               beaupré de trente et un mètres (relevé). On les écarte par le NOM,
               comme le verre et les fanaux le sont déjà — un modéliste qui nomme
               ses courbes autrement les ajoutera à model.rigIgnore. */
            if (Cordage.IsMatch(p.Mi.Name.ToString())) return false;
            return along > 8 * p.Size.X
                && p.Size.X < 0.12 * spec.B
                && along > p.Size.Y                       // plus couché que debout
                && along > 0.15 * spec.L
                && Math.Abs(p.Mid.X) < 0.10 * spec.B
                && p.Max.Z > 0.46 * spec.L;               // il déborde l'étrave
        }

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
        /* LES PIÈCES SOUDÉES, SÉPARÉES. Un modèle importé range souvent
           plusieurs espars dans UN objet : sur la frégate, le mât de misaine et
           le beaupré ne font qu'un « Cylinder_001 » de dix-neuf mètres sur dix-neuf,
           que ni l'épreuve du mât ni celle de la vergue ne reconnaissent — elle
           n'avait qu'un mât sur trois qui pût tomber (relevé). Ce qui est un seul
           objet n'est pas un seul morceau de bois : on le coupe en ÎLOTS de
           triangles, et l'on ne garde la coupe que si l'un d'eux a la forme d'un
           mât ou d'une vergue — le reste du modèle ne bouge pas d'un sommet. */
        bool cut = false;
        foreach (var q in parts)
            if (Bois(q) && !FormeMat(q) && !FormeVergue(q) && q.Size.Y > 0.15 * spec.L
                && SplitIslands(q.Mi, i => FormeMat(i) || FormeVergue(i))) cut = true;
        if (cut) parts = ModelParts();

        /* UN MÂT SOUDÉ À SA VERGUE LATINE : l'artimon du galion est un seul îlot,
           mât et antenne en biais, 2,4 m sur 4,8 de large — ni mince ni vergue.
           On le reconnaît à son ÂME : des sommets alignés sur une verticale, sur
           la plus grande part de sa hauteur. Il tombe alors en entier, antenne
           comprise, ce qui est exactement ce que fait un artimon. */
        bool FormeMatSoude(Part p)
        {
            if (p.Size.Y < 0.20 * spec.L || Math.Abs(p.Mid.X) > 0.12 * spec.B || p.Verts.Length < 8) return false;
            // la verticale : prise au pied, sur le cinquième le plus bas
            double foot = p.Min.Y + 0.2 * p.Size.Y, cx = 0, cz = 0; int n = 0;
            foreach (var v in p.Verts) if (v.Y <= foot) { cx += v.X; cz += v.Z; n++; }
            if (n == 0) return false;
            cx /= n; cz /= n;
            double r = Math.Max(0.35, 0.02 * spec.L), lo = double.MaxValue, hi = double.MinValue;
            foreach (var v in p.Verts)
                if ((v.X - cx) * (v.X - cx) + (v.Z - cz) * (v.Z - cz) < r * r) { lo = Math.Min(lo, v.Y); hi = Math.Max(hi, v.Y); }
            return hi - lo > 0.6 * p.Size.Y;
        }
        var yards = parts.Where(p => FormeVergue(p) && Bois(p)).ToList();
        var poles = parts.Where(p => (FormeMat(p) || FormeMatSoude(p)) && Bois(p)).ToList();
        var refused = parts.Where(p => !Bois(p) && (FormeVergue(p) || FormeMat(p))).ToList();
        if (refused.Count > 0)
            GD.PushWarning($"[{spec.Id}] pièces de la forme d'un espar tenues hors du gréement "
                         + $"(vitrage, fanal ou model.rigIgnore) : {string.Join(", ", refused.Select(p => p.Mi.Name))}");
        var deckAt = DeckProfile(parts);

        /* L'ÉPREUVE D'UN ESPAR DANS L'AXE — une bôme, une corne.
         *
         * L'exacte symétrique de celle d'une vergue, et c'est tout ce qui les
         * sépare : une vergue est longue EN TRAVERS, un espar aurique est long
         * DANS L'AXE. Une bôme ne pouvait donc être reconnue par aucune épreuve
         * du fichier, quoi qu'on modelât, et un modèle aurique naviguait à sec de
         * toile en le disant.
         *
         * INBOARD, et c'est la seule garde qui demande réflexion : ce qui déborde
         * l'étrave est un BEAUPRÉ, qui a son épreuve et son sort — il tombe vers
         * l'avant sur ses jottereaux quand un mât passe par-dessus bord. Un bout
         * de bôme qui dépasse le tableau ne gêne pas : on mesure son extrémité
         * AVANT, laquelle est au mât. */
        bool FormeEspardAxe(Part p)
        {
            if (Cordage.IsMatch(p.Mi.Name.ToString())) return false;
            /* SON ÉPAISSEUR EST EN TRAVERS, ET SEULEMENT LÀ — la leçon du beaupré,
               vingt lignes plus haut, et je l ai refaite. Mesurée aussi en hauteur,
               elle vaut la PENTE et non le bois : la bôme du sloop monte de 86 cm
               vers l arrière sur 3,84 de long, et l épreuve la refusait parce qu elle
               était « épaisse » de 86 centimètres (relevé — Cylinder_007, x 0,12
               y 0,86 z 3,84). C est un cylindre de douze centimètres, en pente.
               D où la garde « plus couché que debout », qui écarte le mât sans rien
               dire de son embonpoint. */
            double along = p.Size.Z;
            return along > 5 * p.Size.X
                && p.Size.X < 0.12 * spec.B
                && along > p.Size.Y
                && along > 0.18 * spec.L
                && Math.Abs(p.Mid.X) < 0.12 * spec.B
                && p.Max.Z < 0.46 * spec.L;
        }

        /* LE GRÉEMENT DANS L'AXE. Rend vrai s'il a su gréer quelque chose.
         *
         * Ce qu'on cherche est un mât et ce qui pend dessus : la BÔME, toujours,
         * et la CORNE s'il y en a une. C'est le modèle qui décide de la coupe,
         * et non la fiche — avec une corne la toile a quatre côtés, sans elle
         * elle monte en pointe au capelage, ce qui est la voile à livarde des
         * sloops des Antilles. Le modéliste essaie les deux sans qu'on touche à
         * rien.
         *
         * Les points sont donnés dans l'ordre du gréement dessiné — amure,
         * écoute, pic, gorge —, si bien que la même coupe les reçoit : une
         * définition, deux usagers. Trois points suffisent pour la pointe, la
         * toile refermant alors son pic sur lui-même, exactement comme la latine. */
        bool GreerDansAxe()
        {
            if (poles.Count == 0) return false;
            var axes = parts.Where(q => !q.Taken && Bois(q) && !FormeBeaupre(q) && FormeEspardAxe(q))
                            .OrderBy(q => q.Mid.Y).ToList();
            if (axes.Count == 0) return false;

            var pole = poles[0];
            pole.Taken = true;
            double z0 = pole.Mid.Z, heel = pole.Min.Y, top = pole.Max.Y, hauteur = top - heel;

            var fall = new Node3D { Position = new Vector3(0, (float)heel, (float)z0) };
            AddChild(fall);
            pole.Mi.Reparent(fall, true);
            var pivot = new Node3D { Position = new Vector3(0, (float)-heel, 0) };
            fall.AddChild(pivot);

            /* LA PLUS BASSE EST LA BÔME. Une corne se tient à mi-mât au moins ;
               ce qui traîne au ras du pont et n'est pas la bôme est une lisse, un
               râtelier, une pièce d'accastillage — on ne lui pend rien. */
            var boom = axes[0];
            boom.Taken = true; boom.Mi.Reparent(pivot, true);
            Part? gaff = null;
            foreach (var q in axes.Skip(1))
                if (q.Mid.Y > boom.Mid.Y + 0.25 * hauteur) { gaff = q; break; }
            if (gaff != null) { gaff.Taken = true; gaff.Mi.Reparent(pivot, true); }

            double boomY = boom.Mid.Y;
            double tackZ = -0.03 * spec.L;                    // l'amure, juste en arrière du mât
            double clewZ = (boom.Min.Z - z0) * 0.97;          // l'écoute, au bout de la bôme
            var coins = new List<Vec3d>
            {
                new Vec3d(0, boomY + 0.02 * hauteur, tackZ),
                new Vec3d(0, boomY, clewZ)
            };
            if (gaff != null)
            {
                // une corne monte VERS L'ARRIÈRE : sa gorge est au mât, son pic au bout
                coins.Add(new Vec3d(0, gaff.Max.Y, (gaff.Min.Z - z0) * 0.97));
                coins.Add(new Vec3d(0, gaff.Min.Y, tackZ));
            }
            else coins.Add(new Vec3d(0, top - 0.04 * hauteur, tackZ * 0.5));

            pivot.AddChild(SailSurface(coins.ToArray(), new Vec3d(1, 0, 0),
                new SailCut { Kind = "gaff", UPeak = 0.42, Crown = 0.80 }));
            _canvases[^1].Mast = _masts.Count;
            double share = Math.Abs(clewZ - tackZ) * (gaff != null ? gaff.Max.Y - boomY : top - boomY) * 0.5;

            /* ET LE FOC, du capelage au bout du beaupré. Il a son propre pivot —
               celui des focs, qui ne brasse qu'aux trois quarts : un foc est
               bordé à part, et le voir suivre la bôme au degré près serait le
               seul détail qu'un marin verrait de loin. */
            var sprit = parts.FirstOrDefault(q => !q.Taken && Bois(q) && FormeBeaupre(q));
            if (sprit != null && Spec.Rig.Jib != null)
            {
                var jr = new Node3D { Position = new Vector3(0, 0, (float)(spec.L / 2)) };
                AddChild(jr);
                double back = z0 - spec.L / 2;
                jr.AddChild(SailSurface(new[]
                {
                    new Vec3d(0, sprit.Max.Y, sprit.Max.Z - spec.L / 2),
                    new Vec3d(0, boomY + 0.06 * hauteur, back + 0.15 * hauteur),
                    new Vec3d(0, top - 0.10 * hauteur, back + 0.04 * hauteur)
                }, new Vec3d(1, 0, 0),
                   new SailCut { Kind = "jib", UPeak = 0.40, VPeak = 0.34, VPin0 = false, Crown = 0.80 }));
                _canvases[^1].Mast = _masts.Count;
                _jibRig = jr;
                _rigs.Add(jr);
                share += (sprit.Max.Z - z0) * (top - boomY) * 0.35;
            }

            var cords = new List<CordAnchor>();
            foreach (int sx in new[] { -1, 1 })
                cords.Add(new CordAnchor(fall, new Vector3((float)(sx * Math.Min(1.4, 0.05 * hauteur)), (float)(hauteur * 0.78), 0),
                    Math.Max(4, Math.Min(10, 0.26 * hauteur))));
            _masts.Add(new MastAt(fall, 0, 0, hauteur, Math.Max(pole.Size.X, pole.Size.Z) * 0.5));
            _damage.Add(new MastDamage
            {
                Fall = fall, Heel = heel, Share = share, HasPole = true, Height = hauteur, Cords = cords
            });
            _rigs.Add(pivot);
            string corne = gaff != null ? "corne " + gaff.Size.Z.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)
                                        : "sans corne, toile en pointe";
            string foc = sprit != null ? "foc" : "sans foc";
            RigLog.Add(FormattableString.Invariant(
                $"axe : mat z {z0:F2} pied {heel:F2} haut {hauteur:F2} ; bome {boom.Size.Z:F2} ; {corne} ; {foc}"));
            return true;
        }

        if (yards.Count == 0)
        {
            /* AVANT DE RENONCER, ESSAYER L'AXE. Le refus était juste tant que rien
               ne savait lire une bôme ; il ne l'est plus. */
            if (GreerDansAxe()) { NameParts(parts); return; }
            GD.PushWarning($"[{spec.Id}] ni vergue ni bôme dans le modèle — elle navigue à sec de toile.");
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

            double share = 0;
            /* OÙ UN BOUT COUPÉ PEUT PENDRE, relevé ici parce qu'ici seulement on le
               sait : les vergues viennent d'être lues et rangées sur leur mât. Rien
               par navire : un modèle de trois-mâts carré reçoit ses bras et ses
               haubans pour rien, et une coque sans mât à elle n'en traîne aucun. */
            var cords = new List<CordAnchor>();
            for (int i = 0; i < mast.Count; i++)
            {
                var y = mast[i];
                double half = y.Size.X * 0.5 * 0.97;
                y.Mi.Reparent(pivot, true);              // brasse avec le mât, toile ou non
                /* Les deux bouts de vergue portent les LONGS bouts : un bras court du
                   bout à la lisse arrière et une balancine monte au chouquet, donc
                   l'un ou l'autre coupé laisse plusieurs mètres se balancer au bout —
                   le morceau que l'œil attrape, le plus loin du mât et qui bouge le
                   plus. Tenus dans le PIVOT : un bras coupé brasse avec la vergue. */
                double armLen = Math.Max(2.5, Math.Min(7, 0.22 * (y.Mid.Y - heel)));
                cords.Add(new CordAnchor(pivot, new Vector3((float)(-half * 0.98), y.Mid.Y, y.Mid.Z - (float)z0), armLen));
                cords.Add(new CordAnchor(pivot, new Vector3((float)(half * 0.98), y.Mid.Y, y.Mid.Z - (float)z0), armLen));

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
                /* UNE VERGUE AU-DELÀ DE L'ÉTRAVE EST UNE CIVADIÈRE, et son plancher
                   n'est pas le pont mais la MER. La règle « aucune voile ne descend
                   sous le pont » est juste au-dessus de la coque — une basse voile
                   est bordée au pavois, pas à travers — et fausse sous un beaupré,
                   où la toile pend au-dessus de l'eau et s'y mouille. Elle bornait
                   la civadière à trois centimètres de chute NÉGATIVE, donc à rien
                   (mesuré sur le Roebuck avant de le voir). */
                bool overhang = Math.Abs(y.Mid.Z) > spec.L * 0.5;
                double floorY = overhang ? 0.4 : deckAt(y.Mid.Z) + 0.02 * spec.L;
                double drop = Math.Min(Math.Min(0.82 * gap, 1.1 * half), yy - floorY);
                if (drop < 0.2 * half) continue;         // trop près du pont pour être une vergue

                pivot.AddChild(SailSurface(new[]
                {
                    new Vec3d(-half, yy, dz), new Vec3d(half, yy, dz),
                    new Vec3d(half * 0.86, yy - drop, dz), new Vec3d(-half * 0.86, yy - drop, dz)
                }, new Vec3d(0, 0, 1), SailCut.Square()));
                _canvases[^1].Mast = _masts.Count;
                share += half * 2 * drop;                // à peu près sa surface, pour ce qu'elle pousse
                RigLog.Add(FormattableString.Invariant($"voile {half:F2} {yy:F2} {drop:F2}"));
            }
            RigLog.Add(FormattableString.Invariant(
                $"mat z0 {z0:F2} pied {heel:F2} vergues {mast.Count} {(pole != null ? "espar" : "sans espar")}"));
            _masts.Add(new MastAt(fall,
                pole != null ? pole.Mid.Z - z0 : 0, 0,
                pole != null ? pole.Size.Y : mast[0].Mid.Y - heel,
                pole != null ? Math.Max(pole.Size.X, pole.Size.Z) * 0.5 : 0.3));
            /* Seul un mât qui est SON PROPRE maillage peut tomber. Sans lui, la toile
               tomberait d'un espar resté debout en l'air, ce qui est pire que rien :
               elle garde alors sa mâture, et le dit. */
            /* Et les haubans, qui partent des JOTTEREAUX et non de la pomme : un bout
               pendu à la pointe se lit comme une drisse de pavillon. COURTS, et c'est
               mesuré à l'écran dans la page : seize mètres de corde pendent droit, et
               une droite si longue se lit comme du FIL DE FER. Ce qui reste est ce qu'un
               hauban laisse après avoir filé à travers tout ce qui le passait. */
            double mh = pole != null ? pole.Size.Y : mast[0].Mid.Y - heel;
            foreach (int sx in new[] { -1, 1 })
                cords.Add(new CordAnchor(fall, new Vector3((float)(sx * Math.Min(1.4, 0.05 * mh)), (float)(mh * 0.78), 0),
                    Math.Max(4, Math.Min(10, 0.26 * mh))));
            _damage.Add(new MastDamage
            {
                Fall = fall, Heel = heel, Share = share, HasPole = pole != null, Height = mh, Cords = cords
            });
            _rigs.Add(pivot);

            /* UN ARTIMON PEUT PORTER LES DEUX. La latine n'était essayée que sur
               les mâts SANS vergue carrée, ce qui suppose qu'une antenne exclut
               un hunier — vrai d'une caravelle, faux d'un navire de 1690, dont
               l'artimon porte une latine ET un hunier d'artimon au-dessus (vu sur
               le Roebuck : la latine portait sans être dessinée, et l'artimon
               paraissait n'avoir qu'un carré). On l'essaie donc aussi ici, sur le
               plus en arrière, et LateenOn ne trouve d'antenne que s'il y en a
               une. */
            if (pole != null && spec.LateenArea > 0 && z0 < 0 && (_latPivot == null || z0 < _latZ))
                LateenOn(pole, fall, heel, z0, deckAt, parts);
        }

        /* LES MÂTS SANS VERGUE CARRÉE — un artimon à antenne, un mât de flèche
           nu : ils ne portent pas de toile ici (le gréement ne sait pendre que
           du carré), mais ce sont des mâts, et ils tombent comme les autres. */
        foreach (var pole in poles)
        {
            if (pole.Taken) continue;
            /* un mât a son pied au-dessus de l'eau — le modèle a son origine sur la
               flottaison : le safran, debout sur l'axe lui aussi, plonge dessous. Le
               pont n'est pas le bon repère : l'artimon traverse une dunette haute. */
            if (pole.Min.Y < 0.3) continue;
            pole.Taken = true;
            double z0 = pole.Mid.Z, heel = pole.Min.Y, mh = pole.Size.Y;
            var fall = new Node3D { Position = new Vector3(0, (float)heel, (float)z0) };
            AddChild(fall);
            pole.Mi.Reparent(fall, true);
            _masts.Add(new MastAt(fall, 0, 0, mh, Math.Min(pole.Size.X, pole.Size.Z) * 0.5));
            var cords = new List<CordAnchor>();
            foreach (int sx in new[] { -1, 1 })
                cords.Add(new CordAnchor(fall, new Vector3((float)(sx * Math.Min(1.4, 0.05 * mh)), (float)(mh * 0.78), 0),
                    Math.Max(4, Math.Min(10, 0.26 * mh))));
            _damage.Add(new MastDamage { Fall = fall, Heel = heel, Share = 0, HasPole = true, Height = mh, Cords = cords });
            RigLog.Add(FormattableString.Invariant($"mat z0 {z0:F2} pied {heel:F2} sans vergue carrée"));
            // l'artimon, le plus en arrière : s'il porte une antenne et que la fiche a une latine, on la grée
            if (spec.LateenArea > 0 && z0 < 0 && (_latPivot == null || z0 < _latZ)) LateenOn(pole, fall, heel, z0, deckAt, parts);
        }

        /* LE BEAUPRÉ — un espar COUCHÉ, que ni l'épreuve du mât ni celle de la
           vergue ne pouvaient reconnaître : la première cherche du haut et mince,
           la seconde du large en travers, et lui est long vers l'AVANT. Il était
           donc le seul espar du bord qu'aucun boulet ne pouvait abattre, alors
           qu'il est le premier qu'on touche en chasse (signalé).

           Après les deux boucles, sur ce qui reste : les mâts ont pris ce qui est
           à eux, et ce qui déborde encore de l'étrave ne peut être que lui — lui
           et son bâton de foc, qui partent ensemble puisqu'ils sont frappés l'un
           sur l'autre. */
        var sprits = parts.Where(q => !q.Taken && Bois(q) && FormeBeaupre(q)).ToList();
        if (sprits.Count > 0) SpritOn(sprits, spec, deckAt);

        // et ce que le NOM désigne : les vigies et les cordages, à leur mât
        NameParts(parts);
    }

    /// <summary>
    /// LE BEAUPRÉ MONTÉ POUR TOMBER — il bascule VERS L'AVANT, sur ses
    /// jottereaux, et non par-dessus le bord comme un mât.
    ///
    /// C'est la seule différence avec un mât, et elle est dans l'AXE : un mât
    /// articulé à son pied tourne sur le roulis (z local) et passe par-dessus la
    /// lisse ; un beaupré est articulé à son talon et tourne sur le tangage (x
    /// local), sa pointe plongeant dans la mer devant l'étrave. Tout le reste —
    /// l'équation du pendule, les haubans qui le retiennent sur la fin, le
    /// naufrage ensuite — est celui des mâts, sans un nombre de plus.
    ///
    /// LA CIVADIÈRE PART AVEC. Le gréement voit déjà une position de mât au-delà
    /// de l'étrave (elle porte sa vergue) mais sans espar à elle : elle ne
    /// pouvait donc pas tomber, et sa toile serait restée en l'air. On la
    /// REPARENTE dans le groupe qui tombe, en gardant sa place — elle suit alors
    /// sans qu'une seule ligne parle d'elle.
    /// </summary>
    /// <summary>
    /// LE GROUPE DU BEAUPRÉ, s'il en a un : tout ce qui doit partir avec lui s'y
    /// pend. La civadière y entre d'elle-même ; le pavillon de beaupré, qui est
    /// bâti APRÈS le gréement, s'y pend par ici.
    /// </summary>
    public Node3D? SpritFall { get; private set; }

    void SpritOn(List<Part> sprits, ShipSpec spec, Func<double, double> deckAt)
    {
        // le talon : le point le plus en arrière de tout ce qui le compose
        Part heelPart = sprits[0];
        foreach (var q in sprits) if (q.Min.Z < heelPart.Min.Z) heelPart = q;
        double z0 = heelPart.Min.Z;
        /* SA HAUTEUR AU TALON, prise sur les sommets qui y sont : un beaupré est
           EN PENTE (la quête, 0,22 radian sur la frégate), et son milieu est donc
           bien plus haut que son talon. Le pivoter sur son milieu le ferait
           s'enfoncer dans le gaillard. */
        double y0 = 0; int n = 0;
        foreach (var v in heelPart.Verts)
            if (v.Z < z0 + Math.Max(0.3, 0.02 * spec.L)) { y0 += v.Y; n++; }
        y0 = n > 0 ? y0 / n : heelPart.Min.Y;

        double tip = z0, tipY = y0;
        foreach (var q in sprits) if (q.Max.Z > tip) { tip = q.Max.Z; tipY = q.Max.Y; }
        double len = Math.Max(2, tip - z0);

        var fall = new Node3D { Position = new Vector3(0, (float)y0, (float)z0) };
        AddChild(fall);
        SpritFall = fall;
        foreach (var q in sprits) { q.Taken = true; q.Mi.Reparent(fall, true); }

        /* ET LA CIVADIÈRE, qui pend au-delà de l'étrave : son groupe entre dans
           celui-ci. Son entrée d'avarie reste, sans espar donc incapable de
           tomber seule — ce qui est juste : elle tombe AVEC le beaupré. */
        foreach (var d0 in _damage)
            if (!d0.HasPole && d0.Fall.GetParent() == this && d0.Fall.Position.Z > 0.42 * spec.L)
                d0.Fall.Reparent(fall, true);

        var cords = new List<CordAnchor>();
        foreach (int sx in new[] { -1, 1 })
            cords.Add(new CordAnchor(fall, new Vector3((float)(sx * Math.Min(1.0, 0.04 * len)), 0, (float)(len * 0.72)),
                Math.Max(3, Math.Min(8, 0.30 * len))));
        _damage.Add(new MastDamage
        {
            Fall = fall, Heel = y0, Share = 0, HasPole = true, Height = len, Cords = cords,
            Pitch = true, Rise = Math.Max(0, tipY - y0)
        });
        RigLog.Add(FormattableString.Invariant(
            $"beaupré talon z {z0:F2} y {y0:F2}, pointe z {tip:F2}, longueur {len:F2} — {string.Join(" + ", sprits.Select(q => q.Mi.Name))}"));
    }

    /// <summary>
    /// Ce que RigModel a reconnu, ligne à ligne : à poser à côté de rigs et
    /// canvases dans la page, pour vérifier qu'on a lu le MÊME gréement.
    /// </summary>
    public readonly List<string> RigLog = new();
}
