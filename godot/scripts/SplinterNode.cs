using Godot;
using System;
using System.Collections.Generic;
using NavalSim.Core;

namespace NavalSim;

/// <summary>
/// LES ÉCLATS — splinters() d'explosion.js, là où la foudre (et demain un
/// boulet) entre dans le bois.
///
/// Du CHÊNE BRUT, et clair : le dehors d'un navire est patiné et goudronné,
/// l'intérieur d'un bordage ne l'est pas. C'est vrai, et cela règle la seule vraie
/// difficulté à les dessiner : des éclats sombres contre une coque sombre à une
/// encablure, ce n'est rien du tout. Une seule planche d'un mètre, étirée
/// différemment par chaque éclat, et tous dans UN MultiMesh — un appel de rendu.
/// Leur VOL est honnête, leur section non : dessinés à leur taille, ce seraient
/// trois pixels à la distance où l'on regarde.
///
/// Dans la mer, et hors du récit : un éclat qui y entre lève sa gerbe
/// (<see cref="SprayPool.Burst"/>) et s'en va.
/// </summary>
public partial class SplinterNode : Node3D
{
    const int Max = 256;

    struct Piece
    {
        public Vector3 P, V, W, Rot, Scale;
        public double T, Life, K;
    }

    readonly Piece[] _live = new Piece[Max];
    int _n;
    readonly Random _rng = new();
    MultiMesh _mm = null!;
    public readonly List<ShaderMaterial> Hazed = new();

    public override void _Ready()
    {
        var wood = new StandardMaterial3D
        {
            AlbedoColor = new Color(0xc9 / 255f, 0xa8 / 255f, 0x75 / 255f), Roughness = 0.92f,
            // elles respirent le même air que la coque
            NextPass = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/hull_haze.gdshader") }
        };
        Hazed.Add((ShaderMaterial)wood.NextPass);
        _mm = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            InstanceCount = Max, VisibleInstanceCount = 0,
            Mesh = new BoxMesh { Size = Vector3.One, Material = wood }
        };
        AddChild(new MultiMeshInstance3D
        {
            Multimesh = _mm, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            CustomAabb = new Aabb(new Vector3(-1e5f, -1e4f, -1e5f), new Vector3(2e5f, 2e4f, 2e5f))
        });
    }

    float R() => (float)_rng.NextDouble();

    /* LES ÉCLATS, où quelque chose entre dans son bois. Le boulet lui-même tuait
       peu ; ce qui vidait une batterie, c'était le BOIS — un boulet ne perce pas
       proprement soixante centimètres de chêne, il fait éclater le bordé vers
       l'intérieur en une nuée de poignards. La plupart partent DANS le sens du
       coup, cachés par son propre bordé ; un peu moins de la moitié ressortent par
       le trou, et ce sont ceux qu'on voit. Nés un peu hors du bois, pour que ceux
       qui sortent ne naissent pas dans le maillage qu'ils quittent. */
    public int Splinters(Vector3 at, Vector3 dir, double k)
    {
        float sk = (float)(0.45 + 0.55 * Math.Max(0.2, k));
        int n = (int)Math.Round(16 + 9 * sk);
        var up = Math.Abs(dir.Y) > 0.9f ? Vector3.Right : Vector3.Up;
        var sx = dir.Cross(up).Normalized();
        var sy = dir.Cross(sx).Normalized();
        var born = at - dir * (0.35f * sk);
        for (int i = 0; i < n && _n < Max; i++)
        {
            float a = R() * Mathf.Tau, r = R() * 0.95f;
            // le long du coup pour la plupart, ressortant par le trou pour le reste
            float along = R() < 0.42f ? -0.55f - R() * 0.5f : 0.35f + R() * 0.9f;
            var v = (dir * along + sx * (Mathf.Cos(a) * r) + sy * (Mathf.Sin(a) * r)).Normalized() * ((7 + R() * 17) * sk);
            v.Y += 1.5f + R() * 3.0f;            // l'éclatement soulève un peu
            _live[_n++] = new Piece
            {
                // long et mince : un éclat est un poignard, pas une planche
                Scale = new Vector3((0.055f + R() * 0.075f) * sk, (0.040f + R() * 0.055f) * sk, (0.55f + R() * 1.60f) * sk),
                Rot = new Vector3(R() * 6.28f, R() * 6.28f, R() * 6.28f),
                P = born + new Vector3((R() - 0.5f) * 0.5f * sk, (R() - 0.5f) * 0.5f * sk, (R() - 0.5f) * 0.5f * sk),
                V = v,
                // bout sur bout, et plus vite qu'une planche : ils sont plus légers
                W = new Vector3((R() - 0.5f) * 26, (R() - 0.5f) * 26, (R() - 0.5f) * 26),
                Life = 1.6 + R() * 1.9, K = 0.25 * sk
            };
        }
        return n;
    }

    public void Step(double dt, Ocean sea, double t, SprayPool spray)
    {
        float fdt = (float)dt;
        int w = 0;
        for (int i = 0; i < _n; i++)
        {
            var p = _live[i];
            p.T += dt;
            if (p.T >= p.Life) continue;
            p.V.Y -= 9.81f * fdt;                // le bois seul est lourd
            p.P += p.V * fdt;
            double s = sea.Sample(p.P.X, p.P.Z, t);
            if (p.P.Y < s && p.V.Y < 0)
            {
                // un éclat fait la gerbe d'un éclat
                spray.Burst(new Vec3d(p.P.X, p.P.Y, p.P.Z), 4.5 * p.K, -p.V.Y, 2.2);
                continue;
            }
            p.Rot += p.W * fdt;
            _live[w] = p;
            _mm.SetInstanceTransform(w, new Transform3D(Basis.FromEuler(p.Rot) * Basis.FromScale(p.Scale), p.P));
            w++;
        }
        _n = w;
        _mm.VisibleInstanceCount = w;
    }

    public void Rebase(double dx, double dz)
    {
        var d = new Vector3((float)dx, 0, (float)dz);
        for (int i = 0; i < _n; i++) _live[i].P -= d;
    }
}
