using Godot;
using System;

namespace NavalSim;

/// <summary>
/// CE QUI TOMBE — rain.js et snow.js : deux réseaux de graines repliés autour de
/// l'œil dans leur shader (rain.gdshader, snow.gdshader), si bien que ni l'un ni
/// l'autre ne coûte un octet de travail processeur par image. Ce nœud ne fait que
/// tenir les deux maillages et leur passer, à chaque image, l'œil, l'heure, le
/// vent, la quantité et la lumière.
///
/// Les deux maillages sont posés à l'origine, transformée identité : leur
/// géométrie ne veut rien dire sans le shader, donc leur boîte non plus, et ils
/// ne doivent jamais être écartés du rendu — d'où une boîte immense.
/// </summary>
public partial class PrecipNode : Node3D
{
    const int RainCount = 6200, SnowCount = 5200;
    const float RainBox = 74, SnowBox = 60;
    const float RainFall = 7.4f, SnowFall = 1.1f;     // m/s

    // les couleurs de la page, en valeurs d'écran : 0xb9c8d4 et 0xe8eef2
    static readonly Vector3 RainBase = new(0xb9 / 255f, 0xc8 / 255f, 0xd4 / 255f);
    static readonly Vector3 SnowBase = new(0xe8 / 255f, 0xee / 255f, 0xf2 / 255f);

    MeshInstance3D _rain = null!, _snow = null!;
    ShaderMaterial _rainMat = null!, _snowMat = null!;

    public override void _Ready()
    {
        var rng = new Random();
        var big = new Aabb(new Vector3(-1e5f, -1e4f, -1e5f), new Vector3(2e5f, 2e4f, 2e5f));

        // la pluie : deux sommets par goutte sur la même graine, UV.x 0 la tête, 1 la queue
        var rv = new Vector3[RainCount * 2];
        var ru = new Vector2[RainCount * 2];
        for (int i = 0; i < RainCount; i++)
        {
            var p = new Vector3(rng.NextSingle() * RainBox, rng.NextSingle() * RainBox, rng.NextSingle() * RainBox);
            rv[i * 2] = p; rv[i * 2 + 1] = p;
            ru[i * 2] = new Vector2(0, 0); ru[i * 2 + 1] = new Vector2(1, 0);
        }
        _rainMat = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/rain.gdshader") };
        _rainMat.SetShaderParameter(U.Box, RainBox);
        _rain = Build(rv, ru, Mesh.PrimitiveType.Lines, _rainMat, big);

        // la neige : un point par flocon, sa graine dans UV.x
        var sv = new Vector3[SnowCount];
        var su = new Vector2[SnowCount];
        for (int i = 0; i < SnowCount; i++)
        {
            sv[i] = new Vector3(rng.NextSingle() * SnowBox, rng.NextSingle() * SnowBox, rng.NextSingle() * SnowBox);
            su[i] = new Vector2(rng.NextSingle() * 100, 0);
        }
        _snowMat = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/snow.gdshader") };
        _snowMat.SetShaderParameter(U.Box, SnowBox);
        _snow = Build(sv, su, Mesh.PrimitiveType.Points, _snowMat, big);
    }

    MeshInstance3D Build(Vector3[] v, Vector2[] uv, Mesh.PrimitiveType prim, ShaderMaterial mat, Aabb box)
    {
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = v;
        arrays[(int)Mesh.ArrayType.TexUV] = uv;
        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(prim, arrays);
        var mi = new MeshInstance3D
        {
            Mesh = mesh, MaterialOverride = mat, CustomAabb = box, Visible = false,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off
        };
        AddChild(mi);
        return mi;
    }

    /// <summary>
    /// Une image. <paramref name="rain"/> et <paramref name="snow"/> vont de 0 à 1 ;
    /// <paramref name="wind"/> est le vent VRAI (m/s) ; <paramref name="light"/>,
    /// de 0 à 1, la clarté de l'horizon — ce qui tombe RÉFLÉCHIT, il n'est jamais
    /// plus clair que lui-même au jour, et une pluie de couleur fixe se voyait à
    /// minuit ; <paramref name="height"/> la hauteur de l'image, qui taille les flocons.
    /// </summary>
    public void Step(Vector3 eye, double t, Vector3 wind, double rain, double snow, double light, float height)
    {
        float k = (float)(0.12 + 0.88 * light);

        _rain.Visible = rain > 0.004;
        if (_rain.Visible)
        {
            /* L'inclinaison n'est pas un décor : c'est LE signe de la force du vent.
               La pluie tombe à peu près à la même vitesse par tous les temps, donc
               dans un coup de vent elle se couche presque à l'horizontale, et cet
               angle se lit d'instinct. Aux six dixièmes du vent : une goutte met un
               instant à prendre la vitesse de l'air. */
            var slant = new Vector3(wind.X * 0.62f, -RainFall, wind.Z * 0.62f);
            _rainMat.SetShaderParameter(U.Amount, (float)rain);
            _rainMat.SetShaderParameter(U.Time, (float)t);
            _rainMat.SetShaderParameter(U.Eye, eye);
            _rainMat.SetShaderParameter(U.Slant, slant);
            // une goutte plus rapide trace une traînée plus longue, comme sur une photographie
            _rainMat.SetShaderParameter(U.Len, 0.8f + slant.Length() * 0.055f);
            _rainMat.SetShaderParameter(U.Color, RainBase * k);
        }

        _snow.Visible = snow > 0.004;
        if (_snow.Visible)
        {
            _snowMat.SetShaderParameter(U.Amount, (float)snow);
            _snowMat.SetShaderParameter(U.Time, (float)t);
            _snowMat.SetShaderParameter(U.Eye, eye);
            // la neige est légère : le vent l'emporte presque à sa propre vitesse
            _snowMat.SetShaderParameter(U.Drift, new Vector3(wind.X * 0.85f, -SnowFall, wind.Z * 0.85f));
            _snowMat.SetShaderParameter(U.Color, SnowBase * k);
            _snowMat.SetShaderParameter(U.Scale, height);
        }
    }
}
