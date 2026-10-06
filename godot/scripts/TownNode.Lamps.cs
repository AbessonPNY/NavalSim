using Godot;
using System;
using System.Collections.Generic;
using NavalSim.Core;

namespace NavalSim;

/// <summary>
/// LES LANTERNES DES RUES (demandé : « qui dessineraient un peu les contours de la
/// ville, jusqu'à deux heures du matin »). Une par pâté du centre, pendue à sa
/// façade côté rue ; une maison sur quatre des faubourgs en a une à sa porte.
/// Vues de la rade, ce sont elles qui tracent les rues — les fenêtres éclairent
/// les murs, pas le pavé.
///
/// RÉALISME : l'éclairage PUBLIC est alors une affaire de capitales — Paris a ses
/// lanternes de rue depuis 1667, Londres les siennes à partir des années 1680.
/// Port-Royal n'a pas d'ordonnance connue : ce sont ici les lanternes des portes,
/// des tavernes et des boutiques, que chacun pend et souffle quand il se couche.
///
/// Rien n'est une lumière (shaders/street_lantern.gdshader) : trois panneaux par
/// lanterne dans trois MultiMesh par groupe de maisons, et le shader seul décide
/// qui brûle. Chaque lanterne suit SA maison dans l'éditeur (<see cref="LampSet.Pose"/>).
/// </summary>
public partial class TownNode
{
    /* trois matières partagées par toutes les villes : la démo n'y pousse que la
       nuit, l'heure et l'horloge, une fois par image */
    ShaderMaterial? _lampHalo, _lampMark, _lampPool;

    /// <summary>Hauteur de la flamme au-dessus du seuil, en mètres : la potence d'une façade.</summary>
    const float LampHigh = 3.1f;

    /// <summary>Faux : toutes soufflées (-- --lanternes 0, pour les comparer aux fenêtres).</summary>
    public bool LampsOn = true;

    /// <summary>Les lanternes d'un groupe de maisons, rangées par maison.</summary>
    sealed class LampSet
    {
        public readonly List<int>[] ByHouse;
        public readonly List<Vector3> Local = new();
        public readonly List<Color> Custom = new();
        public MultiMesh[] Mms = Array.Empty<MultiMesh>();
        public LampSet(int houses)
        {
            ByHouse = new List<int>[houses];
            for (int i = 0; i < houses; i++) ByHouse[i] = new List<int>(1);
        }

        /// <summary>Poser les lanternes de la maison <paramref name="k"/> où l'éditeur l'a mise (<paramref name="at"/> : son pied).</summary>
        public void Pose(int k, Editable ed, Vector3 at)
        {
            var rot = new Basis(Vector3.Up, (float)ed.Yaw);
            float s = ed.Removed ? 0f : (float)ed.Scale;
            foreach (int i in ByHouse[k])
            {
                var tr = new Transform3D(Basis.Identity.Scaled(Vector3.One * s), at + rot * (Local[i] * s));
                foreach (var mm in Mms) mm.SetInstanceTransform(i, tr);
            }
        }
    }

    void LampMaterials()
    {
        if (_lampHalo != null) return;
        var shader = GD.Load<Shader>("res://shaders/street_lantern.gdshader");
        ShaderMaterial M(float size, bool fixedSize, bool ground, float gain)
        {
            var m = new ShaderMaterial { Shader = shader };
            m.SetShaderParameter(U.Size, size);
            m.SetShaderParameter(U.Fixed, fixedSize ? 1f : 0f);
            m.SetShaderParameter("u_ground", ground ? 1f : 0f);
            // la flaque à quinze centimètres au-dessus du seuil : le pavé bouge un peu sous la rue
            m.SetShaderParameter("u_drop", LampHigh - 0.05f);
            m.SetShaderParameter("u_gain", gain);
            return m;
        }
        _lampHalo = M(3.0f, false, false, 1.8f);   // la corne et sa flamme, en mètres
        _lampMark = M(0.009f, true, false, 1f);     // le point qui se voit de la rade
        _lampPool = M(13f, false, true, 0.6f);     // six mètres et demi de pavé éclairé
    }

    /// <summary>
    /// Les lanternes d'un groupe : <paramref name="facade"/> est l'angle (radians) dont
    /// la façade du modèle est tournée par rapport à son +z — la grille l'ajoute au
    /// cap des pâtés (<see cref="StreetGrid.Face"/>). Null si aucune maison n'en porte.
    /// </summary>
    LampSet? Lamps(Node3D holder, Model model, List<House> houses, List<int> who, bool block, double facade)
    {
        var set = new LampSet(who.Count);
        var turn = new Basis(Vector3.Up, (float)-facade);
        for (int k = 0; k < who.Count; k++)
        {
            var h = houses[who[k]];
            double k2 = Math.Min(h.W / model.W, h.D / model.D);
            // tiré sur la place AUTOMATIQUE : une maison déplacée garde sa lanterne
            double r1 = Frac(Math.Sin(h.X * 12.9898 + h.Z * 78.233) * 43758.5453);
            double r2 = Frac(Math.Sin(h.X * 39.346 + h.Z * 11.135) * 24634.6345);
            double r3 = Frac(Math.Sin(h.X * 73.156 + h.Z * 52.235) * 13758.937);
            // les faubourgs : une maison sur quatre, la taverne, la boutique, celui qui rentre tard
            if (!block && r1 > 0.25) continue;
            /* À LA PORTE, côté rue : un pâté en a deux, vers ses coins — la rue se lit
               d'une lanterne à l'autre —, une maison une, au milieu de sa façade */
            double z = 0.5 * model.D * k2 + 0.35;
            int n = block ? 2 : 1;
            for (int j = 0; j < n; j++)
            {
                double x = block ? (j == 0 ? -1 : 1) * (0.36 + 0.08 * r2) * model.W * k2 : 0;
                set.ByHouse[k].Add(set.Local.Count);
                set.Local.Add(turn * new Vector3((float)x, block ? LampHigh : LampHigh - 0.5f, (float)z));
                // chacune ses tirages : deux lanternes d'un même pâté ne s'éteignent pas ensemble
                set.Custom.Add(LampCustom(Frac(r2 + 0.37 * j), Frac(r3 + 0.53 * j), block ? Frac(r1 + 0.29 * j) : r1 / 0.25));
            }
        }
        if (set.Local.Count == 0) return null;

        LampMaterials();
        var quad = new QuadMesh { Size = Vector2.One };
        var plane = new PlaneMesh { Size = Vector2.One };
        var mms = new List<MultiMesh>(3);
        foreach (var (mesh, mat) in new (Mesh, ShaderMaterial)[] { (quad, _lampHalo!), (quad, _lampMark!), (plane, _lampPool!) })
        {
            var mm = new MultiMesh
            {
                TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
                UseCustomData = true, Mesh = mesh, InstanceCount = set.Local.Count
            };
            for (int i = 0; i < set.Custom.Count; i++)
            {
                mm.SetInstanceTransform(i, new Transform3D(Basis.Identity.Scaled(Vector3.Zero), Vector3.Zero));
                mm.SetInstanceCustomData(i, set.Custom[i]);
            }
            holder.AddChild(new MultiMeshInstance3D
            {
                Multimesh = mm, MaterialOverride = mat,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                // les panneaux sont déplacés dans le shader : la boîte des instances ne suffit pas
                ExtraCullMargin = 400
            });
            mms.Add(mm);
        }
        set.Mms = mms.ToArray();
        GD.Print($"[lanternes] {set.Local.Count} sur {who.Count} {(block ? "pâté(s)" : "maison(s)")}");
        return set;
    }

    /* r : la phase de sa flamme ; g : l'heure après minuit où on la souffle, d'une
       heure et demie à deux ; b : la nuit qu'il faut pour qu'on l'allume. Trois tirages
       dans [0, 1). */
    static Color LampCustom(double phase, double late, double early)
        => new((float)phase, (float)(1.5 + 0.5 * late), (float)(0.32 + 0.22 * early), 0);

    /// <summary>
    /// DES LANTERNES HORS DES MULTIMESH DES VILLES — celles d'un ajout de l'éditeur
    /// (une copie de maison, un modèle brut de la palette) : un petit MultiMesh à
    /// lui, dans le repère du modèle, qui suit son nœud. Positions en mètres de ce repère.
    /// </summary>
    public Node3D LampNode(IReadOnlyList<Vector3> at, int seed)
    {
        LampMaterials();
        var node = new Node3D { Name = "lanternes" };
        var rng = new Random(seed);
        var custom = new Color[at.Count];
        for (int i = 0; i < at.Count; i++) custom[i] = LampCustom(rng.NextDouble(), rng.NextDouble(), rng.NextDouble());
        var quad = new QuadMesh { Size = Vector2.One };
        foreach (var (mesh, mat) in new (Mesh, ShaderMaterial)[] { (quad, _lampHalo!), (quad, _lampMark!), (new PlaneMesh { Size = Vector2.One }, _lampPool!) })
        {
            var mm = new MultiMesh
            {
                TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
                UseCustomData = true, Mesh = mesh, InstanceCount = at.Count
            };
            for (int i = 0; i < at.Count; i++)
            {
                mm.SetInstanceTransform(i, new Transform3D(Basis.Identity, at[i]));
                mm.SetInstanceCustomData(i, custom[i]);
            }
            node.AddChild(new MultiMeshInstance3D
            {
                Multimesh = mm, MaterialOverride = mat,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                ExtraCullMargin = 50
            });
        }
        return node;
    }

    /// <summary>Les lanternes d'un modèle brut de la palette (« lanternes » : combien), le long de sa face +z.</summary>
    public Node3D? LampsOnFront(int count, float width, float halfDepth, int seed)
    {
        if (count <= 0) return null;
        var at = new List<Vector3>(count);
        for (int i = 0; i < count; i++)
            at.Add(new Vector3(((i + 0.5f) / count - 0.5f) * width * 0.8f, LampHigh, halfDepth + 0.35f));
        return LampNode(at, seed);
    }

    /// <summary>
    /// La nuit, l'heure (0 à 24) et l'horloge, aux lanternes : le shader décide seul
    /// laquelle brûle — allumées au crépuscule, soufflées une à une avant deux heures.
    /// </summary>
    public void SetLamps(double night, double hour, double clock)
    {
        if (_lampHalo == null) return;
        if (!LampsOn) night = 0;
        void Push(ShaderMaterial m)
        {
            m.SetShaderParameter(U.Night, (float)night);
            m.SetShaderParameter(U.Hour, (float)hour);
            m.SetShaderParameter(U.Time, (float)(clock % 3600));
        }
        Push(_lampHalo); Push(_lampMark!); Push(_lampPool!);
    }
}
