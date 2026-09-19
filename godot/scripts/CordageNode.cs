using Godot;
using System;
using System.Collections.Generic;
using NavalSim.Core;

namespace NavalSim;

/// <summary>
/// LES BOUTS ROMPUS, À L'ÉCRAN — cordage.js. La simulation est dans le noyau
/// (<see cref="Cordage"/>) ; ce nœud pend les bouts que les navires demandent,
/// relit leurs attaches sur le gréement VIVANT — son roulis, sa gîte, un mât qui
/// passe par-dessus bord, sans qu'aucun d'eux sache que ceci existe —, et les
/// dessine en un seul appel (cordage.gdshader).
///
/// AU-DELÀ D'UNE CERTAINE DISTANCE, ELLES NE SONT PLUS DESSINÉES : à trois cents
/// mètres une corde est un cheveu qui se lit comme du bruit sur le gréement, et le
/// plancher en pixels qui la sauve de près est exactement ce qui la fait
/// scintiller de loin. La simulation, elle, continue — elle ne coûte rien, et
/// l'arrêter la ferait sauter à la pose de repos dès qu'on se rapproche.
/// </summary>
public partial class CordageNode : Node3D
{
    const double DrawFrom = 190, DrawTo = 300;
    const int P = Cordage.P, Max = Cordage.Max;

    public readonly Cordage Core = new();
    public readonly List<ShaderMaterial> Hazed = new();

    sealed class Tag
    {
        public ShipNode Ship = null!;
        public int Mast, Epoch;
        public Node3D Obj = null!;
        public Vector3 At;
        public double D;
    }

    readonly Random _rng = new();
    readonly float[] _data = new float[P * Max * 4];
    readonly byte[] _bytes = new byte[P * Max * 16];
    Image _img = null!;
    ImageTexture _tex = null!;
    ShaderMaterial _mat = null!;
    MeshInstance3D _mesh = null!;

    // chanvre goudronné, et il RÉFLÉCHIT : jamais plus clair que ce qui l'éclaire
    static readonly Vector3 Base = new(0x6d / 255f, 0x5c / 255f, 0x48 / 255f);
    static readonly StringName UPts = "u_pts", UP = "u_p", UPxScale = "u_px_scale";

    public override void _Ready()
    {
        _img = Image.CreateEmpty(P, Max, false, Image.Format.Rgbaf);
        _tex = ImageTexture.CreateFromImage(_img);

        /* Le ruban a la même forme dans chaque place : le maillage est construit une
           fois et jamais retouché ; seules les données changent. */
        var v = new Vector3[Max * P * 2];
        var idx = new int[Max * (P - 1) * 6];
        for (int r = 0; r < Max; r++)
        {
            for (int j = 0; j < P; j++)
            {
                v[(r * P + j) * 2] = new Vector3(j, r, -1);
                v[(r * P + j) * 2 + 1] = new Vector3(j, r, 1);
            }
            int b = r * P * 2, o = r * (P - 1) * 6;
            for (int j = 0; j < P - 1; j++)
            {
                int a = b + j * 2, k = o + j * 6;
                idx[k] = a; idx[k + 1] = a + 1; idx[k + 2] = a + 2;
                idx[k + 3] = a + 1; idx[k + 4] = a + 3; idx[k + 5] = a + 2;
            }
        }
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = v;
        arrays[(int)Mesh.ArrayType.Index] = idx;
        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);

        _mat = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/cordage.gdshader") };
        _mat.SetShaderParameter(UPts, _tex);
        _mat.SetShaderParameter(UP, P);
        Hazed.Add(_mat);
        _mesh = new MeshInstance3D
        {
            Mesh = mesh, MaterialOverride = _mat, Visible = false,
            CustomAabb = new Aabb(new Vector3(-1e5f, -1e4f, -1e5f), new Vector3(2e5f, 2e4f, 2e5f)),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off
        };
        AddChild(_mesh);
    }

    /* VIDER LES DEMANDES des navires plutôt qu'être appelé au moment du coup :
       rien dehors n'a à se souvenir de ceci. */
    void Drain(ShipNode s)
    {
        if (s.RigCuts.Count == 0) return;
        foreach (var (mast, n) in s.RigCuts)
        {
            var pool = s.CordAnchors(mast);
            if (pool.Count == 0) continue;
            for (int k = 0; k < n; k++)
            {
                var a = pool[(int)Math.Floor(_rng.NextDouble() * pool.Count)];
                var w = a.Obj.GlobalTransform * a.At;
                Core.Hang(new Vec3d(w.X, w.Y, w.Z), a.Len * (0.75 + _rng.NextDouble() * 0.5),
                    new Tag { Ship = s, Mast = mast, Epoch = s.RigEpoch, Obj = a.Obj, At = a.At });
            }
        }
        s.RigCuts.Clear();
    }

    /// <summary>
    /// Une image : les demandes de <paramref name="ships"/>, les attaches relues, la
    /// mer dessous, la simulation, et la texture des points.
    /// </summary>
    public void Step(double dt, IReadOnlyList<ShipNode> ships, Ocean sea, double t, Camera3D cam,
                     Vec3d wind, Vec3d horizon)
    {
        for (int i = 0; i < ships.Count; i++) Drain(ships[i]);

        // qui est mort, à quelle distance, où est la mer
        var eye = cam.GlobalPosition;
        int live = 0;
        for (int s = 0; s < Core.N; s++)
        {
            var r = Core.Ropes[s];
            if (!r.On) continue;
            var tag = (Tag)r.Tag!;
            /* Partie avec elle : une coque retirée de l'arbre, un gréement radoubé,
               un mât qui a fini de couler. Trois lectures bon marché plutôt que trois
               appels à ne pas oublier. */
            if (!IsInstanceValid(tag.Ship) || !tag.Ship.IsInsideTree() || tag.Ship.RigEpoch != tag.Epoch
                || !tag.Ship.MastVisible(tag.Mast))
            {
                Core.Drop(s); continue;
            }
            var w = tag.Obj.GlobalTransform * tag.At;
            r.Hx = w.X; r.Hy = w.Y; r.Hz = w.Z;
            /* La mer sous une corde, UNE fois par corde et par image et non par point
               et par sous-pas : cinquante questions à la mer pour un bout de corde. */
            r.Sea = sea.Sample(r.Hx, r.Hz, t);
            tag.D = w.DistanceTo(eye);
            live++;
        }
        Core.Trim();
        if (live == 0) { _mesh.Visible = false; return; }

        Core.Step(dt, wind.X, wind.Z);

        /* Réfléchir, pas émettre — normalisé sur la luminance de l'horizon, pour que
           le plein jour ne bouge pas et que la nuit, si. Comme la fumée des canons. */
        double l = Math.Max(1e-3, 0.2126 * horizon.X + 0.7152 * horizon.Y + 0.0722 * horizon.Z);
        double kk = Math.Max(0.14, l / 0.85);
        _mat.SetShaderParameter(U.Color, new Vector3(
            (float)(Base.X * horizon.X / l * kk), (float)(Base.Y * horizon.Y / l * kk), (float)(Base.Z * horizon.Z / l * kk)));
        float height = Math.Max(1, GetViewport().GetVisibleRect().Size.Y);
        _mat.SetShaderParameter(UPxScale, 2 * Mathf.Tan(Mathf.DegToRad(cam.Fov) * 0.5f) / height);

        // la texture : une rangée par place, (x, y, z, opacité) ; 0 = pas dessinée
        Array.Clear(_data);
        bool any = false;
        for (int s = 0; s < Core.N; s++)
        {
            var r = Core.Ropes[s];
            if (!r.On) continue;
            double fade = 1 - Math.Min(1, Math.Max(0, (((Tag)r.Tag!).D - DrawFrom) / (DrawTo - DrawFrom)));
            if (fade <= 0.01) continue;          // toujours simulée, simplement pas dessinée
            any = true;
            int b = s * P;
            for (int j = 0; j < P; j++)
            {
                int o = (b + j) * 4;
                _data[o] = Core.Px[b + j]; _data[o + 1] = Core.Py[b + j]; _data[o + 2] = Core.Pz[b + j];
                // un bout coupé est effiloché plutôt que scié : la dernière portée s'amincit
                _data[o + 3] = (float)(fade * (j == P - 1 ? 0.55 : 1));
            }
        }
        _mesh.Visible = any;
        if (!any) return;
        Buffer.BlockCopy(_data, 0, _bytes, 0, _bytes.Length);
        _img.SetData(P, Max, false, Image.Format.Rgbaf, _bytes);
        _tex.Update(_img);
    }

    public void Rebase(double dx, double dz) => Core.Rebase(dx, dz);
}
