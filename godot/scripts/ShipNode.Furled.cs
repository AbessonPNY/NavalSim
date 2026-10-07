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
}
