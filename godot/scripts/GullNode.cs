using Godot;
using System;
using System.Collections.Generic;
using NavalSim.Core;

namespace NavalSim;

/// <summary>
/// LES MOUETTES — le portage de <c>js/gulls.js</c>, en deux appels de dessin.
///
/// DEUX BANDEAUX, parce qu'il y a deux cercles. Un tiers des oiseaux sort au
/// navire — ce sont ceux qui comptent pour l'œil, un oiseau qui vire au-dessus
/// de la poupe étant un oiseau, quand celui de l'île est trois pixels — et les
/// autres restent sur leur perchoir, pour que le lieu garde sa vie propre quand
/// rien ne passe. Chaque groupe est un <see cref="MultiMesh"/> dont le NŒUD est
/// posé au centre du cercle ; le shader fait le reste. Aucun calcul par oiseau.
///
/// Le maillage est DESSINÉ et non chargé, comme dans la page : deux ailes
/// effilées, un corps, une queue qui ne bat pas — c'est elle qui empêche
/// l'oiseau de se lire comme une fléchette. Les couleurs sont dans les sommets.
/// </summary>
public partial class GullNode : Node3D
{
    public GullRules Rules = new();

    /// <summary>Le matériau, pour que la démo lui pousse l'horloge.</summary>
    public ShaderMaterial? Material { get; private set; }

    readonly Gulls _flock;
    MultiMeshInstance3D _near = null!, _far = null!;
    readonly Random _rng = new();

    public GullNode(GullRules? k = null)
    {
        Rules = k ?? new GullRules();
        _flock = new Gulls(Rules);
    }

    public override void _Ready()
    {
        Material = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/gull.gdshader") };
        Material.SetShaderParameter("u_gull_span", (float)Rules.Span);
        var mesh = Build();

        int suiveuses = Math.Clamp(Rules.Followers, 0, Rules.Count);
        _near = Flock(mesh, suiveuses, true);
        _far = Flock(mesh, Rules.Count - suiveuses, false);
    }

    MultiMeshInstance3D Flock(Mesh mesh, int n, bool follow)
    {
        var mm = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            UseCustomData = true,
            Mesh = mesh,
            InstanceCount = Math.Max(n, 1),
        };
        double R(double a, double b) => a + _rng.NextDouble() * (b - a);
        for (int i = 0; i < mm.InstanceCount; i++)
        {
            mm.SetInstanceTransform(i, Transform3D.Identity);
            /* TOUT CE QUI FAIT QU'UN OISEAU N'EST PAS UN AUTRE, fixé À LA
               NAISSANCE : un bandeau dont les membres seraient retirés au hasard
               à chaque image scintillerait au lieu de voler. */
            double rayon = follow ? R(22, 78) : Rules.Reach * R(0.30, 0.95);
            double alt = follow ? R(11, 34) : R(14, 46);
            double omega = (follow ? R(0.055, 0.135) : R(0.028, 0.062)) * (_rng.NextDouble() < 0.5 ? -1 : 1);
            mm.SetInstanceCustomData(i, new Color(
                (float)_rng.NextDouble(), (float)rayon, (float)alt, (float)omega));
        }
        if (n <= 0) mm.VisibleInstanceCount = 0;
        var node = new MultiMeshInstance3D
        {
            Multimesh = mm,
            MaterialOverride = Material,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Visible = false,
            /* SA BOÎTE À LA MAIN : les instances sont toutes à l'origine et c'est
               le shader qui les disperse — sans elle, le bandeau disparaît dès
               que son centre sort du champ. */
            CustomAabb = new Aabb(new Vector3(-800, 0, -800), new Vector3(1600, 60, 1600)),
        };
        AddChild(node);
        return node;
    }

    /// <summary>
    /// Une mouette, en UN maillage : deux ailes, un corps, une queue. Le
    /// MultiMesh instancie un maillage, pas une scène — tout est donc fondu.
    /// </summary>
    Mesh Build()
    {
        var P = new List<Vector3>();
        var C = new List<Color>();
        var I = new List<int>();

        void Tri(int a, int b, int c) { I.Add(a); I.Add(b); I.Add(c); }

        /* UNE AILE : longue et mince. Le premier jet de la page avait une corde
           de 0,36 m sur un demi-envergure de 0,9 — un allongement de cinq, qui
           est un pigeon qui plane. Une mouette est près de dix, et c'est cette
           finesse que l'œil reconnaît à toute distance. */
        float s = (float)Rules.Span * 0.5f;
        var gris = new Color(0.60f, 0.63f, 0.66f);
        var clair = new Color(0.93f, 0.925f, 0.90f);
        var noir = new Color(0.14f, 0.145f, 0.165f);
        void Aile(int side)
        {
            int b0 = P.Count;
            var pts = new[]
            {
                new Vector3(0, 0, 0.13f), new Vector3(0, 0, -0.13f),
                new Vector3(s * 0.52f * side, 0, 0.08f), new Vector3(s * 0.52f * side, 0, -0.10f),
                new Vector3(s * side, 0, -0.06f), new Vector3(s * 0.90f * side, 0, -0.17f),
            };
            foreach (var p in pts)
            {
                P.Add(p);
                float u = Math.Abs(p.X) / s;                 // 0 à l'épaule, 1 au bout
                C.Add(u < 0.62f ? clair.Lerp(gris, u / 0.62f) : gris.Lerp(noir, (u - 0.62f) / 0.38f));
            }
            if (side > 0) { Tri(b0, b0+2, b0+3); Tri(b0, b0+3, b0+1); Tri(b0+2, b0+4, b0+5); Tri(b0+2, b0+5, b0+3); }
            else          { Tri(b0, b0+3, b0+2); Tri(b0, b0+1, b0+3); Tri(b0+2, b0+5, b0+4); Tri(b0+2, b0+3, b0+5); }
        }
        Aile(+1);
        Aile(-1);

        // la queue, qui ne bat pas : c'est la seule chose fixe de la silhouette
        {
            int b0 = P.Count;
            foreach (var p in new[]
            {
                new Vector3(0, 0, -0.22f), new Vector3(-0.11f, 0, -0.46f),
                new Vector3(0.11f, 0, -0.46f), new Vector3(0, 0, -0.40f)
            })
            { P.Add(p); C.Add(clair); }
            Tri(b0, b0+1, b0+3); Tri(b0, b0+3, b0+2);
        }

        // le corps : une petite sphère, aplatie et étirée vers l'avant
        {
            int b0 = P.Count;
            const int U = 7, V = 5;
            for (int v = 0; v <= V; v++)
                for (int u = 0; u <= U; u++)
                {
                    double th = Math.PI * v / V, ph = Math.Tau * u / U;
                    P.Add(new Vector3(
                        (float)(Math.Sin(th) * Math.Cos(ph) * 0.075),
                        (float)(Math.Cos(th) * 0.085),
                        (float)(Math.Sin(th) * Math.Sin(ph) * 0.32)));
                    C.Add(clair);
                }
            for (int v = 0; v < V; v++)
                for (int u = 0; u < U; u++)
                {
                    int k = b0 + v * (U + 1) + u;
                    Tri(k, k + U + 1, k + U + 2);
                    Tri(k, k + U + 2, k + 1);
                }
        }

        var pos = P.ToArray();
        var idx = I.ToArray();
        var nrm = new Vector3[pos.Length];
        for (int t = 0; t + 2 < idx.Length; t += 3)
        {
            var n = (pos[idx[t + 1]] - pos[idx[t]]).Cross(pos[idx[t + 2]] - pos[idx[t]]);
            nrm[idx[t]] += n; nrm[idx[t + 1]] += n; nrm[idx[t + 2]] += n;
        }
        for (int k = 0; k < nrm.Length; k++)
            nrm[k] = nrm[k].LengthSquared() > 1e-12f ? nrm[k].Normalized() : Vector3.Up;

        var arr = new Godot.Collections.Array();
        arr.Resize((int)Mesh.ArrayType.Max);
        arr[(int)Mesh.ArrayType.Vertex] = pos;
        arr[(int)Mesh.ArrayType.Normal] = nrm;
        arr[(int)Mesh.ArrayType.Color] = C.ToArray();
        arr[(int)Mesh.ArrayType.Index] = idx;
        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arr);
        return mesh;
    }

    /// <summary>
    /// Les poser contre l'origine du moment. <paramref name="here"/> et
    /// <paramref name="origin"/> sont en mètres VRAIS, comme pour la terre : les
    /// deux centres sont gardés en monde et simplement DÉPOSÉS à
    /// <c>centre − origine</c>, si bien que rien ici n'a de Rebase.
    /// </summary>
    public void Update(World world, Vec3d here, Vec3d origin, double dt, double clock)
    {
        if (_near == null) return;
        double shore = world.ShoreDistance(here.X, here.Z);
        _flock.Step(dt, here.X, here.Z, shore, () => world.NearestShore(here.X, here.Z));

        _near.Visible = _far.Visible = _flock.Flying;
        if (!_flock.Flying) return;

        Material?.SetShaderParameter("u_time", (float)clock);
        var c = _flock.Centre;
        _near.Position = new Vector3((float)(c.X - origin.X), 0, (float)(c.Z - origin.Z));
        if (_flock.Roost is { } r)
            _far.Position = new Vector3((float)(r.X - origin.X), 0, (float)(r.Z - origin.Z));
    }
}
