using Godot;
using System;

namespace NavalSim;

/// <summary>
/// LA BRUME RASANTE — les traînées basses qui stagnent sur l'eau au petit matin.
///
/// Quelques NAPPES horizontales superposées, la plus haute à deux mètres, posées
/// en disque autour de l'œil. Comme la pluie et la neige, tout se passe dans le
/// shader (mist.gdshader) : ce nœud ne fait que tenir le maillage et lui donner,
/// à chaque image, l'œil, l'heure, le vent et ce qu'il y en a.
///
/// Le maillage est posé à l'origine, transformation identité : sa géométrie ne
/// veut rien dire sans le shader — VERTEX.y y porte le NUMÉRO de la nappe et non
/// une hauteur —, donc sa boîte non plus, d'où une boîte immense pour qu'il ne
/// soit jamais écarté du rendu.
/// </summary>
public partial class MistNode : Node3D
{
    /// <summary>Le nombre de nappes, et la finesse du disque.</summary>
    const int Layers = 4, Rings = 14, Segs = 36;
    /// <summary>Le rayon du disque, en mètres : au-delà, la brume de l'air prend le relais.</summary>
    const float Radius = 250;

    MeshInstance3D _mesh = null!;
    ShaderMaterial _mat = null!;

    public ShaderMaterial Material => _mat;

    public override void _Ready()
    {
        var P = new System.Collections.Generic.List<Vector3>();
        var I = new System.Collections.Generic.List<int>();

        for (int l = 0; l < Layers; l++)
        {
            float couche = Layers == 1 ? 0 : (float)l / (Layers - 1);
            int b0 = P.Count;
            /* LES ANNEAUX SONT SERRÉS PRÈS DE L'ŒIL et lâches au loin : c'est là
               qu'on regarde la brume de biais, donc là qu'il faut des sommets pour
               que la nappe épouse la houle. Le carré suffit à les répartir. */
            for (int r = 0; r <= Rings; r++)
            {
                float u = (float)r / Rings;
                float rad = Radius * u * u;
                for (int s = 0; s <= Segs; s++)
                {
                    double a = Math.Tau * s / Segs;
                    // y porte le NUMÉRO de la nappe : le shader en fait une hauteur
                    P.Add(new Vector3((float)(Math.Cos(a) * rad), couche, (float)(Math.Sin(a) * rad)));
                }
            }
            for (int r = 0; r < Rings; r++)
                for (int s = 0; s < Segs; s++)
                {
                    int k = b0 + r * (Segs + 1) + s;
                    I.Add(k); I.Add(k + Segs + 1); I.Add(k + Segs + 2);
                    I.Add(k); I.Add(k + Segs + 2); I.Add(k + 1);
                }
        }

        var arr = new Godot.Collections.Array();
        arr.Resize((int)Mesh.ArrayType.Max);
        arr[(int)Mesh.ArrayType.Vertex] = P.ToArray();
        arr[(int)Mesh.ArrayType.Index] = I.ToArray();
        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arr);

        _mat = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/mist.gdshader") };
        _mesh = new MeshInstance3D
        {
            Mesh = mesh,
            MaterialOverride = _mat,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Visible = false,
            CustomAabb = new Aabb(new Vector3(-1e5f, -1e4f, -1e5f), new Vector3(2e5f, 2e4f, 2e5f)),
        };
        AddChild(_mesh);
    }

    /// <summary>
    /// Une image. <paramref name="amount"/> de 0 à 1 ; l'œil est en repère LOCAL,
    /// comme tout ce qui est près de la coque.
    /// </summary>
    public void Step(Vector3 eye, double amount)
    {
        if (_mesh == null) return;
        _mesh.Visible = amount > 0.004;
        if (!_mesh.Visible) return;
        _mat.SetShaderParameter("u_eye", eye);
        _mat.SetShaderParameter("u_mist", (float)amount);
    }
}
