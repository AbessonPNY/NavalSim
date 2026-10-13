using Godot;
using System;
using System.Collections.Generic;

namespace NavalSim;

/// <summary>
/// LA TOILE SERRÉE, RECOPIÉE (demandé : les navires à quai « avec les voiles au repos —
/// la Frégate a l'air bien nue »). Un navire au mouillage n'est qu'un modèle posé sur
/// l'eau (MooredNode) : il n'a pas de gréement, et les voiles d'un navire du jeu sont
/// tissées par le code, pas peintes dans le .glb. Celui-ci les tisse une fois, serrées
/// sous leurs vergues, et en rend des maillages FIXES dans le repère de son modèle :
/// la rade les accroche à chaque coque, sans un calcul de plus par image.
/// </summary>
public partial class ShipNode
{
    /// <summary>Les voiles serrées, en maillages fixes, dans le repère du modèle (ou du navire s'il n'en a pas).</summary>
    public Node3D FurledSails(List<ShaderMaterial>? mats = null)
    {
        var root = new Node3D { Name = "voiles-serrees" };
        var frame = (ModelRoot ?? this).GlobalTransform.AffineInverse();
        foreach (var c in _canvases)
        {
            if (c.Split) continue;
            /* dessinée dans le modèle : la copie du .glb posée en rade la porte déjà, à sa
               place — on rend seulement sa matière de toile, que DressFurled lui passe */
            if (c.Furled.Count > 0)
            {
                if (mats != null && c.Mat is ShaderMaterial fm && !mats.Contains(fm)) mats.Add(fm);
                continue;
            }
            // au repos : pas de vent, pas de toile établie — la bune pend en festons
            c.Cloth.Shape(Spec.Rig.Belly, 0, false, 0, 0);
            var p = c.Cloth.Positions; var q = c.Cloth.Normals;
            int n = c.Uv.Length;
            var v = new Vector3[n]; var nm = new Vector3[n];
            for (int k = 0; k < n; k++)
            {
                v[k] = new Vector3(p[k * 3], p[k * 3 + 1], p[k * 3 + 2]);
                nm[k] = new Vector3(q[k * 3], q[k * 3 + 1], q[k * 3 + 2]);
            }
            var arr = new Godot.Collections.Array();
            arr.Resize((int)Mesh.ArrayType.Max);
            arr[(int)Mesh.ArrayType.Vertex] = v;
            arr[(int)Mesh.ArrayType.Normal] = nm;
            arr[(int)Mesh.ArrayType.TexUV] = c.Uv;
            arr[(int)Mesh.ArrayType.Index] = c.Idx;
            var mesh = new ArrayMesh();
            mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arr);
            root.AddChild(new MeshInstance3D
            {
                Mesh = mesh, MaterialOverride = c.Mat,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.On,
                Transform = frame * c.Node.GlobalTransform
            });
            if (mats != null && c.Mat is ShaderMaterial sm && !mats.Contains(sm)) mats.Add(sm);
        }
        return root;
    }

    // ------------------------------------------------------------------
    //  LA TOILE SERRÉE DESSINÉE DANS LE MODÈLE
    // ------------------------------------------------------------------

    /* Demandé : « je fais la voile repliée dans Blender, plus réaliste ; lors de la
       descente de la voile, tu masques le mesh et tu affiches la voile normalement ».
       La toile tissée, à zéro de toile établie, pend en festons sous sa vergue ; c'est
       juste en forme et pauvre en matière — une voile serrée est un boudin roulé et
       amarré de rabans, ce que seul un modéliste dessine bien. Seul l'état serré
       change : dès que la toile descend, c'est la voile vivante qui se montre.

       ELLE PORTE LA MATIÈRE DE SA VOILE (demandé : « même texture et translucidité »).
       Pas une copie réglée à l'œil dans Blender : LA matière de la toile, partagée,
       donc sa teinte, sa carte peinte (canvasMap), sa translucidité, l'heure, la
       neige et la brume, sans rien recopier — une définition, deux usagers. Seuls
       les rabans, le bois et le cordage dessinés avec elle gardent la leur. */

    /// <summary>Ce qui nomme une voile serrée dans un modèle : « voile_serree_misaine », « ferlee », « furled ».</summary>
    internal static readonly System.Text.RegularExpressions.Regex FurledNames = new(
        "serr[ée]e|ferl[ée]e|furled", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    /// <summary>Les matières d'une voile serrée qui ne sont pas de la toile : elles restent celles du modèle.</summary>
    internal static readonly System.Text.RegularExpressions.Regex FurledOwnMats = new(
        "raban|gasket|cord|rope|bois|wood|fer|iron|metal", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    /// <summary>Les matières qui DISENT être la toile : quand une pièce en nomme une, elles seules la reçoivent.</summary>
    internal static readonly System.Text.RegularExpressions.Regex FurledCanvasMats = new(
        "toile|voile|sail|canvas", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    /* QUI REÇOIT LA TOILE. Si une matière de la pièce se nomme toile, voile, sail ou
       canvas, elle seule — le reste est rabans, bois, ferrures, quel que soit leur
       nom (« marroon » sur la frégate). Sinon, tout sauf ce qui se dit raban, cordage
       ou bois : une pièce d'une seule matière n'a ainsi rien à nommer. */
    static Func<Material?, bool> CanvasRule(IEnumerable<Material?> all)
    {
        bool named = false;
        foreach (var m in all) if (m != null && FurledCanvasMats.IsMatch(m.ResourceName ?? "")) { named = true; break; }
        return m => named ? m != null && FurledCanvasMats.IsMatch(m.ResourceName ?? "")
                          : m == null || !FurledOwnMats.IsMatch(m.ResourceName ?? "");
    }

    /* LE FONDU SUIT LA TOILE, il n'a pas d'horloge à lui : sur les quinze premiers
       centièmes de toile établie, la forme dessinée cède à la vivante. La toile
       allant d'un tiers par seconde (ShipPhysics.SetRate), c'est près d'une
       demi-seconde, et la même chose à l'envers quand on serre — un ordre
       contremandé à mi-chemin repart d'où il en était. */
    const double FurledFadeSpan = 0.15;

    /// <summary>
    /// Détacher les voiles serrées du modèle, LA PLUS HAUTE de chaque branche
    /// seulement. Pas Detach, qui descend exprès sous un anneau trouvé : ici,
    /// SplitPrimitives a déjà fait de chaque primitive un enfant « voile_serree_0 »,
    /// « _1 »… qui répond au même nom, et chacun serait parti de son côté (relevé sur
    /// la frégate). Une voile et ce qu'elle porte vont ensemble. Rend, comme Detach,
    /// chaque nœud et le chemin de son parent jusqu'au modèle.
    /// </summary>
    static List<(Node3D Node, Transform3D Parent)> DetachFurled(Node3D obj)
    {
        var found = new List<(Node3D Node, Transform3D Parent)>();
        void Walk(Node n, Transform3D acc)
        {
            foreach (var kid in n.GetChildren())
            {
                if (kid is Node3D k3 && FurledNames.IsMatch(k3.Name.ToString())) { found.Add((k3, acc)); continue; }
                Walk(kid, kid is Node3D c3 ? acc * c3.Transform : acc);
            }
        }
        Walk(obj, Transform3D.Identity);
        foreach (var (nd, _) in found)
        {
            nd.GetParent()?.RemoveChild(nd);
            // hors de la scène du .glb : sans quoi la recoller ailleurs la rend incohérente (avertissement de Godot)
            ClearOwner(nd);
        }
        return found;
    }

    static void ClearOwner(Node n)
    {
        n.Owner = null;
        foreach (var c in n.GetChildren()) ClearOwner(c);
    }

    /// <summary>
    /// Remettre les voiles serrées détachées du modèle, chacune SOUS LE PIVOT de la
    /// voile la plus proche : elle brasse avec sa vergue et tombe avec son mât sans
    /// rien de plus. La plus proche se juge sur la toile ELLE-MÊME serrée — sous sa
    /// vergue, le long de sa bôme ou de son étai —, ce qui vaut pour les carrées, la
    /// brigantine, la latine et le foc sans les distinguer.
    /// </summary>
    void MountFurled(List<(Node3D Node, Transform3D Parent)> found, Node3D obj)
    {
        if (found.Count == 0) return;
        var near = new List<Vector3[]>();
        foreach (var c in _canvases)
        {
            c.Cloth.Shape(Spec.Rig.Belly, 0, false, 0, 0);
            var p = c.Cloth.Positions;
            var g = new Vector3[p.Length / 3];
            var to = c.Node.GlobalTransform;
            for (int k = 0; k < g.Length; k++) g[k] = to * new Vector3(p[k * 3], p[k * 3 + 1], p[k * 3 + 2]);
            near.Add(g);
        }
        foreach (var (nd, parent) in found)
        {
            // à sa place dans le modèle : Detach a rendu le chemin de son parent jusqu'au modèle
            var local = nd.Transform;
            obj.AddChild(nd);
            nd.Transform = parent * local;
            var at = nd.GlobalTransform * PieceBox(nd).GetCenter();
            int best = -1;
            float bestD = float.MaxValue;
            for (int i = 0; i < near.Count; i++)
                foreach (var q in near[i])
                {
                    float d = at.DistanceSquaredTo(q);
                    if (d < bestD) { bestD = d; best = i; }
                }
            float dist = Mathf.Sqrt(bestD);
            if (best < 0 || dist > 0.15 * Spec.L)
            {
                GD.PushWarning($"[{Spec.Id}] voile serrée « {nd.Name} » : aucune voile à moins de "
                             + $"{0.15 * Spec.L:F1} m ({dist:F1}) — laissée dans le modèle, toujours visible.");
                continue;
            }
            var c = _canvases[best];
            nd.Reparent(c.Node.GetParent(), true);
            nd.Visible = false;            // la toile vivante tient jusqu'au premier réglage
            c.Furled.Add(nd);
            var pieces = new List<MeshInstance3D>();
            foreach (var (mi, _) in Meshes(nd)) pieces.Add(mi);
            var isCanvas = CanvasRule(pieces.ConvertAll(mi => mi.GetActiveMaterial(0)));
            foreach (var mi in pieces)
            {
                if (!isCanvas(mi.GetActiveMaterial(0))) { c.FurledOwn.Add(mi); continue; }
                mi.MaterialOverride = c.Mat;
                mi.CastShadow = GeometryInstance3D.ShadowCastingSetting.On;
                c.FurledSkin.Add(mi);
            }
            GD.Print(FormattableString.Invariant(
                $"[{Spec.Id}] voile serrée « {nd.Name} » → voile {best} ({c.Cloth.Kind}, à {dist:F2} m)"));
        }
    }

    /// <summary>Pour la rade : le nom de chaque voile serrée dessinée, et la matière de la toile qu'elle doit porter.</summary>
    public Dictionary<string, Material> FurledMaterials()
    {
        var d = new Dictionary<string, Material>();
        foreach (var c in _canvases)
            foreach (var n in c.Furled) if (IsInstanceValid(n)) d[n.Name.ToString()] = c.Mat;
        return d;
    }

    /// <summary>
    /// Habiller les voiles serrées d'une copie brute du .glb (la rade) de la matière
    /// de leur toile, rabans exceptés. Par surface : la copie n'a pas été découpée en
    /// une pièce par primitive, une voile et ses rabans y partagent un maillage.
    /// </summary>
    public static void DressFurled(Node3D copie, Dictionary<string, Material> mats)
    {
        void Walk(Node n)
        {
            foreach (var ch in n.GetChildren())
            {
                if (ch is Node3D n3 && FurledNames.IsMatch(n3.Name.ToString())
                    && mats.TryGetValue(n3.Name.ToString(), out var mat))
                {
                    var all = new List<Material?>();
                    foreach (var mi in NodeWalk.Meshes(n3))
                        for (int s = 0; s < mi.Mesh.GetSurfaceCount(); s++) all.Add(mi.GetActiveMaterial(s));
                    var isCanvas = CanvasRule(all);
                    foreach (var mi in NodeWalk.Meshes(n3))
                        for (int s = 0; s < mi.Mesh.GetSurfaceCount(); s++)
                            if (isCanvas(mi.GetActiveMaterial(s))) mi.SetSurfaceOverrideMaterial(s, mat);
                    continue;
                }
                Walk(ch);
            }
        }
        Walk(copie);
    }

    static readonly StringName UFade = "u_fade";

    /// <summary>
    /// Serrée, sa toile dessinée ; établie, la voile vivante ; entre les deux, l'une
    /// cède à l'autre point à point (u_fade, sail.gdshader). Une voile partie ou
    /// brûlée n'a plus de forme serrée : la toile tissée garde son dessin d'avarie.
    /// </summary>
    static void ShowFurled(Canvas c, double set)
    {
        if (c.Furled.Count == 0) return;
        float f = c.Split || c.Burn >= 0.3 ? 0f : (float)Math.Clamp(1 - set / FurledFadeSpan, 0, 1);
        // au centième près : réécrire les paramètres d'instance à chaque image pour rien coûte
        f = MathF.Round(f * 100) / 100;
        if (f == c.FurledFade) return;
        c.FurledFade = f;
        foreach (var n in c.Furled) if (IsInstanceValid(n)) n.Visible = f > 0;
        foreach (var g in c.FurledSkin) if (IsInstanceValid(g)) g.SetInstanceShaderParameter(UFade, f);
        // les rabans n'ont pas notre shader : le fondu du moteur, qui les passe en transparence le temps qu'il dure
        foreach (var g in c.FurledOwn) if (IsInstanceValid(g)) g.Transparency = 1 - f;
        c.Node.Visible = f < 1;
        c.Node.SetInstanceShaderParameter(UFade, -(1 - f));
    }
}
