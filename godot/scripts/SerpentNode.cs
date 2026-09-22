using Godot;
using System;
using System.Collections.Generic;
using NavalSim.Core;

namespace NavalSim;

/// <summary>
/// LE SERPENT DESSINÉ — un tube qui suit les points du corps que le noyau tient
/// (<see cref="SeaSerpent"/>), refait à chaque image : quarante-huit anneaux de
/// quatorze côtés, sept cents sommets, rien. Une crête sur le dos, le dos sombre
/// et le ventre pâle, la peau mouillée qui prend le ciel. La tête à part, plus
/// massive que le cou, et deux yeux qui luisent un peu — assez pour qu'on les
/// voie dans la pluie, pas assez pour éclairer quoi que ce soit.
/// </summary>
public partial class SerpentNode : Node3D
{
    const int Sides = 14;
    readonly ArrayMesh _mesh = new();
    MeshInstance3D _body = null!, _head = null!;
    StandardMaterial3D _skin = null!;
    public readonly List<ShaderMaterial> Hazed = new();

    readonly Vector3[] _v = new Vector3[SeaSerpent.N * (Sides + 1)];
    readonly Vector3[] _n = new Vector3[SeaSerpent.N * (Sides + 1)];
    readonly Color[] _c = new Color[SeaSerpent.N * (Sides + 1)];
    int[] _idx = null!;

    public override void _Ready()
    {
        var haze = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/hull_haze.gdshader") };
        Hazed.Add(haze);
        _skin = new StandardMaterial3D
        {
            VertexColorUseAsAlbedo = true,
            Roughness = 0.32f, Metallic = 0.05f,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            NextPass = haze
        };
        _body = new MeshInstance3D { Mesh = _mesh, MaterialOverride = _skin, ExtraCullMargin = 60 };
        AddChild(_body);

        var idx = new List<int>();
        int ring = Sides + 1;
        for (int i = 0; i + 1 < SeaSerpent.N; i++)
            for (int k = 0; k < Sides; k++)
            {
                int a = i * ring + k, b = a + ring;
                idx.Add(a); idx.Add(a + 1); idx.Add(b);
                idx.Add(b); idx.Add(a + 1); idx.Add(b + 1);
            }
        _idx = idx.ToArray();
        _head = BuildHead(haze);
        AddChild(_head);
        Visible = false;
    }

    /* LA TÊTE : un museau long et plat comme celui d'un congre, la mâchoire
       dessous, deux yeux sur les côtés. En repère de tête : l'avant +z. */
    MeshInstance3D BuildHead(ShaderMaterial haze)
    {
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        var dark = new Color(0.10f, 0.13f, 0.12f);
        var pale = new Color(0.46f, 0.48f, 0.40f);
        // un fuseau aplati : sections en ellipse le long de z, de la nuque au museau
        (float z, float w, float h, float lo)[] sec =
        {
            (-0.6f, 0.80f, 0.70f, 0.62f), (0.2f, 1.00f, 0.78f, 0.70f), (1.2f, 0.92f, 0.62f, 0.62f),
            (2.2f, 0.70f, 0.46f, 0.50f), (3.0f, 0.42f, 0.30f, 0.34f), (3.4f, 0.16f, 0.14f, 0.16f)
        };
        const int S = 16;
        var pts = new List<(Vector3 P, Color C, Vector3 N)[]>();
        foreach (var (z, w, h, lo) in sec)
        {
            var row = new (Vector3, Color, Vector3)[S + 1];
            for (int k = 0; k <= S; k++)
            {
                float a = k * Mathf.Tau / S;
                float y = Mathf.Cos(a);
                var p = new Vector3(Mathf.Sin(a) * w, y > 0 ? y * h : y * lo, z);
                // la normale de l'ellipse, à la main : le sens des triangles n'en décide pas
                var nn = new Vector3(Mathf.Sin(a) / w, y > 0 ? y / h : y / lo, 0).Normalized();
                row[k] = (p, y > -0.2f ? dark : pale, nn);
            }
            pts.Add(row);
        }
        for (int i = 0; i + 1 < pts.Count; i++)
            for (int k = 0; k < S; k++)
            {
                var a = pts[i][k]; var b = pts[i][k + 1]; var c = pts[i + 1][k]; var d = pts[i + 1][k + 1];
                foreach (var (p, col, nn) in new[] { a, c, b, b, c, d }) { st.SetColor(col); st.SetNormal(nn); st.AddVertex(p); }
            }
        var mesh = st.Commit();
        var head = new MeshInstance3D
        {
            Mesh = mesh,
            MaterialOverride = new StandardMaterial3D { VertexColorUseAsAlbedo = true, Roughness = 0.30f, Metallic = 0.05f, CullMode = BaseMaterial3D.CullModeEnum.Disabled, NextPass = haze }
        };
        // les yeux : un vert jaune qui luit à peine
        var eyeMat = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.8f, 0.85f, 0.3f),
            EmissionEnabled = true, Emission = new Color(0.75f, 0.85f, 0.25f), EmissionEnergyMultiplier = 1.6f
        };
        foreach (int sx in new[] { -1, 1 })
            head.AddChild(new MeshInstance3D
            {
                Mesh = new SphereMesh { Radius = 0.13f, Height = 0.26f, RadialSegments = 8, Rings = 4 },
                MaterialOverride = eyeMat,
                Position = new Vector3(sx * 0.62f, 0.28f, 1.55f)
            });
        return head;
    }

    /// <summary>À chaque image : le corps refait sur ses points, la tête posée à leur bout.</summary>
    public void Sync(SeaSerpent s)
    {
        Visible = s.State != SerpentState.Absent;
        if (!Visible) return;
        int ring = Sides + 1;
        for (int i = 0; i < SeaSerpent.N; i++)
        {
            var p = V(s.Spine[i]);
            // la tangente, vers la tête ; le haut du corps, perpendiculaire
            var t = V(s.Spine[Math.Max(0, i - 1)]) - V(s.Spine[Math.Min(SeaSerpent.N - 1, i + 1)]);
            if (t.LengthSquared() < 1e-6f) t = Vector3.Forward;
            t = t.Normalized();
            var side = t.Cross(Vector3.Up);
            if (side.LengthSquared() < 1e-4f) side = t.Cross(Vector3.Right);
            side = side.Normalized();
            var up = side.Cross(t).Normalized();
            float r = (float)s.Radius[i];
            for (int k = 0; k <= Sides; k++)
            {
                float a = k * Mathf.Tau / Sides;
                float ca = Mathf.Cos(a), sa = Mathf.Sin(a);
                // la crête : le dos se relève en arête sur un sixième du tour
                float crest = ca > 0.86f ? 1 + 0.35f * (ca - 0.86f) / 0.14f : 1;
                var dir = up * ca + side * sa;
                _v[i * ring + k] = p + dir * (r * crest);
                _n[i * ring + k] = dir;
                float back = Mathf.Clamp(0.5f + ca * 0.9f, 0, 1);
                _c[i * ring + k] = new Color(0.44f, 0.46f, 0.38f).Lerp(new Color(0.07f, 0.11f, 0.10f), back);
            }
        }
        var arr = new Godot.Collections.Array();
        arr.Resize((int)Mesh.ArrayType.Max);
        arr[(int)Mesh.ArrayType.Vertex] = _v;
        arr[(int)Mesh.ArrayType.Normal] = _n;
        arr[(int)Mesh.ArrayType.Color] = _c;
        arr[(int)Mesh.ArrayType.Index] = _idx;
        _mesh.ClearSurfaces();
        _mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arr);

        // la tête, au bout du cou, dans le prolongement du corps — penchée vers le bord quand il se dresse
        var h = V(s.Spine[0]);
        var f = (h - V(s.Spine[2])).Normalized();
        if (s.State == SerpentState.Rear)
        {
            var flat = new Vector3(Mathf.Sin((float)s.Heading), 0, Mathf.Cos((float)s.Heading));
            f = (flat - Vector3.Up * 0.35f).Normalized();
        }
        var hs = f.Cross(Vector3.Up);
        if (hs.LengthSquared() < 1e-4f) hs = Vector3.Right;
        hs = hs.Normalized();
        var hu = hs.Cross(f).Normalized();
        _head.Transform = new Transform3D(new Basis(-hs, hu, f), h - f * 0.4f);
    }

    static Vector3 V(Vec3d v) => new((float)v.X, (float)v.Y, (float)v.Z);
}
