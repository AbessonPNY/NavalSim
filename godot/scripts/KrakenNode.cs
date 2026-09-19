using Godot;
using System;
using System.Collections.Generic;
using NavalSim.Core;

namespace NavalSim;

/// <summary>
/// LE KRAKEN, DESSINÉ — ce que kraken.js pose dans la scène. Le comportement vit
/// dans le noyau (<see cref="Kraken"/>) ; ce nœud ne fait que le montrer.
///
/// S'il porte un modèle (creatures/kraken.glb, nommé par settings.json) : tout
/// objet nommé « bras… » est UN bras, modelé droit sur +Y depuis sa racine ; le
/// reste est le corps, centré, tourné vers +Z. Sinon le kraken dessiné de la
/// page : un dôme sombre, deux yeux pâles, et des tubes effilés. Dans les deux
/// cas chaque bras est courbé par kraken_arm.gdshader le long de la ligne que le
/// noyau calcule. Les yeux LUISENT un peu — la seule chose sur lui qui ne suit pas
/// la lumière.
/// </summary>
public partial class KrakenNode : Node3D
{
    Node3D _head = null!;
    Mesh _armMesh = null!;
    float _armY0, _armLen = 16, _armR = 0.95f;
    readonly MeshInstance3D[] _arms = new MeshInstance3D[Kraken.MaxArms];
    readonly ShaderMaterial[] _armMats = new ShaderMaterial[Kraken.MaxArms];
    // par anneau : la ligne médiane, la tangente, l'intérieur — voir u_ctn dans kraken_arm.gdshader
    readonly Vector3[][] _ctn = new Vector3[Kraken.MaxArms][];

    /// <summary>Ce qui respire le même air que la mer : à pousser par <see cref="SkyNode.PushTo"/>.</summary>
    public readonly List<ShaderMaterial> Hazed = new();
    /// <summary>Le rayon de touche du manteau, pris sur le modèle s'il y en a un.</summary>
    public double BodyR = 5.5;

    static readonly StringName UCtn = "u_ctn", UY0 = "u_y0", ULen = "u_len", UK = "u_k",
        UMetallic = "u_metallic";

    public override void _Ready()
    {
        Visible = false;
        _head = new Node3D();
        AddChild(_head);
        for (int i = 0; i < Kraken.MaxArms; i++)
        {
            _ctn[i] = new Vector3[KrakenArm.Rings * 3];
        }
    }

    /// <summary>
    /// Le modèle, s'il y en a un et s'il se lit ; sinon le kraken dessiné. Un
    /// modèle illisible laisse le dessiné en place, comme dans la page.
    /// </summary>
    public void Build(string? glbPath)
    {
        bool worn = false;
        if (glbPath != null && System.IO.File.Exists(glbPath))
        {
            try { worn = Wear(glbPath); }
            catch (Exception e) { GD.PushWarning($"[kraken] modèle illisible, le kraken reste dessiné : {e.Message}"); }
        }
        if (!worn) Draw();

        var shader = GD.Load<Shader>("res://shaders/kraken_arm.gdshader");
        var big = new Aabb(new Vector3(-1e5f, -1e4f, -1e5f), new Vector3(2e5f, 2e4f, 2e5f));
        for (int i = 0; i < Kraken.MaxArms; i++)
        {
            var m = _armMats[i] ?? new ShaderMaterial { Shader = shader };
            _armMats[i] = m;
            m.SetShaderParameter(UY0, _armY0);
            m.SetShaderParameter(ULen, _armLen);
            Hazed.Add(m);
            var mi = new MeshInstance3D { Mesh = _armMesh, MaterialOverride = m, CustomAabb = big, Visible = false };
            AddChild(mi);
            _arms[i] = mi;
        }
    }

    /* LE KRAKEN DESSINÉ : le manteau, un dôme sombre de 11 × 7 × 17 m, deux yeux
       qui luisent, et des bras en tubes effilés — sept côtés, trente anneaux, du
       rayon 0,95 à la racine à presque rien à la pointe. */
    void Draw()
    {
        var skin = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/hull.gdshader") };
        skin.SetShaderParameter(U.Albedo, new Color(0x4a / 255f, 0x1b / 255f, 0x24 / 255f));
        skin.SetShaderParameter(U.Roughness, 0.34f);
        Hazed.Add(skin);
        _head.AddChild(new MeshInstance3D
        {
            Mesh = new SphereMesh { Radius = 1, Height = 2, RadialSegments = 22, Rings = 14 },
            MaterialOverride = skin, Scale = new Vector3(5.5f, 3.4f, 8.5f)
        });
        var eye = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            AlbedoColor = new Color(0xd9 / 255f, 0xb4 / 255f, 0x4a / 255f), DisableFog = true
        };
        foreach (int sx in new[] { -1, 1 })
            _head.AddChild(new MeshInstance3D
            {
                Mesh = new SphereMesh { Radius = 0.5f, Height = 1, RadialSegments = 10, Rings = 8 },
                MaterialOverride = eye, Position = new Vector3(sx * 3.4f, 1.1f, 5.9f)
            });

        const int rings = KrakenArm.Rings, sides = 7;
        _armY0 = 0; _armLen = 16; _armR = 0.95f;
        var v = new Vector3[rings * sides];
        var nrm = new Vector3[rings * sides];
        for (int r = 0; r < rings; r++)
        {
            float u = r / (float)(rings - 1);
            float rad = _armR * (1 - 0.93f * Mathf.Pow(u, 0.85f));
            for (int s = 0; s < sides; s++)
            {
                float a = s / (float)sides * Mathf.Tau;
                v[r * sides + s] = new Vector3(Mathf.Cos(a) * rad, u * _armLen, Mathf.Sin(a) * rad);
                nrm[r * sides + s] = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
            }
        }
        var idx = new List<int>();
        for (int r = 0; r < rings - 1; r++)
            for (int s = 0; s < sides; s++)
            {
                int a = r * sides + s, b = r * sides + (s + 1) % sides, c = a + sides, d = b + sides;
                idx.AddRange(new[] { a, c, b, b, c, d });
            }
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = v;
        arrays[(int)Mesh.ArrayType.Normal] = nrm;
        arrays[(int)Mesh.ArrayType.Index] = idx.ToArray();
        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        _armMesh = mesh;
    }

    /* PORTER UN MODÈLE : lu par le NOM. Les sommets de chaque pièce sont ramenés
       dans le repère du modèle ; le premier bras trouvé sert de gabarit à tous
       (la page distribue plusieurs bras à tour de rôle ; celui-ci n'en a qu'un). */
    bool Wear(string path)
    {
        var doc = new GltfDocument();
        var state = new GltfState();
        if (doc.AppendFromFile(path, state) != Error.Ok || doc.GenerateScene(state) is not Node3D root) return false;

        var body = new Node3D();
        ArrayMesh? arm = null;
        Material? armMat = null;
        ShaderMaterial? haze = null;
        var stack = new Stack<(Node, Transform3D, bool)>();
        stack.Push((root, root.Transform, false));
        while (stack.Count > 0)
        {
            var (n, xf, inArm) = stack.Pop();
            bool armHere = inArm || n.Name.ToString().StartsWith("bras", StringComparison.OrdinalIgnoreCase);
            if (n is MeshInstance3D mi && mi.Mesh != null)
            {
                if (armHere && arm == null)
                {
                    arm = new ArrayMesh();
                    for (int s = 0; s < mi.Mesh.GetSurfaceCount(); s++)
                    {
                        var a = mi.Mesh.SurfaceGetArrays(s);
                        var vv = a[(int)Mesh.ArrayType.Vertex].AsVector3Array();
                        var nn = a[(int)Mesh.ArrayType.Normal].AsVector3Array();
                        for (int i = 0; i < vv.Length; i++) vv[i] = xf * vv[i];
                        for (int i = 0; i < nn.Length; i++) nn[i] = (xf.Basis * nn[i]).Normalized();
                        var outA = new Godot.Collections.Array();
                        outA.Resize((int)Mesh.ArrayType.Max);
                        outA[(int)Mesh.ArrayType.Vertex] = vv;
                        if (nn.Length == vv.Length) outA[(int)Mesh.ArrayType.Normal] = nn;
                        outA[(int)Mesh.ArrayType.Index] = a[(int)Mesh.ArrayType.Index];
                        arm.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, outA);
                        armMat ??= mi.GetActiveMaterial(s);
                    }
                }
                else if (!armHere)
                {
                    var copy = new MeshInstance3D { Mesh = mi.Mesh, Transform = xf };
                    // la brume par-dessus ses matières, comme sur les navires
                    haze ??= new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/hull_haze.gdshader") };
                    for (int s = 0; s < mi.Mesh.GetSurfaceCount(); s++)
                        if (mi.GetActiveMaterial(s) is BaseMaterial3D bm && bm.NextPass == null)
                        {
                            var own = (BaseMaterial3D)bm.Duplicate();
                            own.NextPass = haze;
                            copy.SetSurfaceOverrideMaterial(s, own);
                        }
                    body.AddChild(copy);
                }
            }
            var kids = n.GetChildren();
            for (int i = kids.Count - 1; i >= 0; i--)
                stack.Push((kids[i], kids[i] is Node3D c3 ? xf * c3.Transform : xf, armHere));
        }
        root.QueueFree();
        if (haze != null) Hazed.Add(haze);

        if (body.GetChildCount() > 0)
        {
            _head.AddChild(body);
            // la taille de touche suit le modèle : 5,5 pour le dessiné de 17 m
            var box = new Aabb();
            bool first = true;
            foreach (var c in body.GetChildren())
                if (c is MeshInstance3D m)
                {
                    var bb = m.Transform * m.Mesh.GetAabb();
                    box = first ? bb : box.Merge(bb);
                    first = false;
                }
            BodyR = Math.Max(2, 0.32 * Math.Max(box.Size.X, box.Size.Z));
        }
        else body.QueueFree();

        if (arm == null) return false;
        var ab = arm.GetAabb();
        if (ab.Size.Y < 0.1f) return false;
        _armMesh = arm;
        _armY0 = ab.Position.Y; _armLen = ab.Size.Y;
        _armR = 0.95f;
        // sa matière, reportée sur le shader qui le courbe
        var sm = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/kraken_arm.gdshader") };
        if (armMat is BaseMaterial3D abm)
        {
            sm.SetShaderParameter(U.Albedo, abm.AlbedoColor);
            sm.SetShaderParameter(U.Roughness, abm.Roughness);
            sm.SetShaderParameter(UMetallic, abm.Metallic);
        }
        for (int i = 0; i < Kraken.MaxArms; i++) _armMats[i] = (ShaderMaterial)sm.Duplicate();
        GD.Print($"[kraken] modèle chargé : corps {body.GetChildCount()} pièce(s), bras de {_armLen:F1} m");
        return true;
    }

    /// <summary>Montrer le kraken tel que le noyau vient de le poser.</summary>
    public void Sync(Kraken k)
    {
        Visible = k.State != KrakenState.Absent;
        if (!Visible) return;
        _head.Position = new Vector3((float)k.Pos.X, (float)k.Pos.Y, (float)k.Pos.Z);
        var to = new Vector3((float)k.HeadTarget.X, (float)k.HeadTarget.Y, (float)k.HeadTarget.Z) - _head.Position;
        // le corps regarde vers +Z, comme un objet de three qu'on tourne par lookAt
        if (to.LengthSquared() > 1e-4f) _head.Basis = Basis.LookingAt(to, Vector3.Up, true);

        for (int i = 0; i < Kraken.MaxArms; i++)
        {
            var a = k.ArmsList[i];
            _arms[i].Visible = a.On;
            if (!a.On) continue;
            var ctn = _ctn[i];
            for (int r = 0; r < KrakenArm.Rings; r++)
            {
                ctn[r * 3] = new Vector3((float)a.C[r].X, (float)a.C[r].Y, (float)a.C[r].Z);
                ctn[r * 3 + 1] = new Vector3((float)a.T[r].X, (float)a.T[r].Y, (float)a.T[r].Z);
                ctn[r * 3 + 2] = new Vector3((float)a.N[r].X, (float)a.N[r].Y, (float)a.N[r].Z);
            }
            var m = _armMats[i];
            m.SetNow(UCtn, ctn);
            m.SetShaderParameter(UK, (float)(a.Rad / _armR));
        }
    }
}
