using Godot;
using System;
using System.Collections.Generic;
using NavalSim.Core;

namespace NavalSim;

/// <summary>
/// LES PAVILLONS À TERRE (demandé : « en mettre sur les forts posés dans Port-Royal »).
/// Un mât de pavillon est un objet du mode création comme un autre : la palette (Tab)
/// en offre un par nation de flags.json (« pavillon · Angleterre »), on le pose, on le
/// monte sur le fort (↑, ⇧↑ d'un mètre), on le copie. Son fichier ne retient que
/// « glb » : "pavillon:angleterre".
///
/// L'étamine est celle des navires (FlagCloth, la matière des voiles) — mêmes ondes,
/// mêmes images —, mais elle flotte au vent VRAI : un fort ne fait pas route. Une
/// grande pièce, comme on en hissait sur un bastion : trois mètres de guindant.
/// </summary>
public partial class ShipDemo
{
    /// <summary>La clé d'un mât de pavillon dans le fichier des retouches : "pavillon:" + la nation.</summary>
    internal const string ShoreFlagKey = "pavillon:";
    const double ShoreStaff = 12, ShoreHoist = 3.0;

    sealed class ShoreFlag
    {
        public FlagCloth Cloth = null!;
        public Node3D Pivot = null!;
        public MeshInstance3D Node = null!;
        public ArrayMesh Mesh = null!;
        public Material Mat = null!;
        public Vector3[] V = null!, N = null!;
        public readonly Godot.Collections.Array Arrays = ShipNode.NewArrays();
        LiveCloth? _live;
        public LiveCloth Live => _live ??= new LiveCloth(Arrays);
    }

    readonly List<ShoreFlag> _shoreFlags = new();
    /// <summary>-- --pavillon-noye : les pavillons à terre traités comme dans l'eau, pour voir la torche.</summary>
    double _flagDrownTest;
    readonly Dictionary<string, ShaderMaterial> _shoreFlagMats = new();
    StandardMaterial3D? _staffMat;

    /// <summary>Un mât de pavillon pour la palette : null si la nation n'existe pas.</summary>
    Raw? ShoreFlagRaw(string rel)
    {
        string id = rel[ShoreFlagKey.Length..];
        var nation = _nations.All.Find(n => n.Id == id);
        if (nation == null) return null;
        return new Raw { Radius = 0.8, Height = ShoreStaff + 0.5, Make = () => ShoreFlagNode(nation) };
    }

    Node3D ShoreFlagNode(Nation nation)
    {
        var root = new Node3D { Name = "mat-de-pavillon" };
        if (_staffMat == null)
        {
            _staffMat = new StandardMaterial3D { AlbedoColor = new Color(0.36f, 0.27f, 0.18f), Roughness = 0.85f, NextPass = HazePass.New() };
            _editHazed.Add((ShaderMaterial)_staffMat.NextPass);
        }
        // le mât, sa pomme
        root.AddChild(new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = 0.07f, BottomRadius = 0.13f, Height = (float)ShoreStaff, RadialSegments = 8 },
            MaterialOverride = _staffMat, Position = new Vector3(0, (float)(ShoreStaff * 0.5), 0)
        });
        root.AddChild(new MeshInstance3D
        {
            Mesh = new SphereMesh { Radius = 0.14f, Height = 0.28f, RadialSegments = 8, Rings = 4 },
            MaterialOverride = _staffMat, Position = new Vector3(0, (float)ShoreStaff + 0.1f, 0)
        });

        // l'étamine : FlagCloth taille le guindant à 0,045 × la longueur d'un navire ; on la lui donne
        var cloth = new FlagCloth(1 / 0.045, new FlagSpec { Size = ShoreHoist }, GD.Randf() * 6.28) { Hangs = true };
        int n = cloth.U.Length;
        var f = new ShoreFlag { Cloth = cloth, Mesh = new ArrayMesh(), V = new Vector3[n], N = new Vector3[n] };
        if (!_shoreFlagMats.TryGetValue(nation.Image, out var mat))
        {
            mat = ShipNode.NewFlagMat();
            ShipNode.Dress(mat, ShipNode.FlagImage(nation.Image), Colors.White, true);
            _editHazed.Add(mat);
            _shoreFlagMats[nation.Image] = mat;
        }
        f.Mat = mat;
        var uv = new Vector2[n];
        for (int k = 0; k < n; k++) uv[k] = new Vector2(cloth.Uvs[k * 2], 1 - cloth.Uvs[k * 2 + 1]);
        var idx = new int[cloth.Indices.Length];
        // le sens des triangles retourné, comme sur les navires
        for (int t = 0; t < idx.Length; t += 3) { idx[t] = cloth.Indices[t]; idx[t + 1] = cloth.Indices[t + 2]; idx[t + 2] = cloth.Indices[t + 1]; }
        f.Arrays.SetNow((int)Mesh.ArrayType.TexUV, uv);
        f.Arrays.SetNow((int)Mesh.ArrayType.Index, idx);
        // la têtière du pavillon sous la pomme, frappée sur la drisse
        f.Pivot = new Node3D { Position = new Vector3(0, (float)(ShoreStaff - 0.25), 0) };
        f.Node = new MeshInstance3D { Mesh = f.Mesh, CastShadow = GeometryInstance3D.ShadowCastingSetting.On };
        f.Pivot.AddChild(f.Node);
        root.AddChild(f.Pivot);
        f.Live.Upload(f.Mesh, f.Mat, cloth.Positions, cloth.Normals, f.V, f.N);
        _shoreFlags.Add(f);
        return root;
    }

    /// <summary>Les faire flotter, une fois par image : au vent vrai, ceux qu'on voit.</summary>
    void ShoreFlagsTick()
    {
        if (_shoreFlags.Count == 0) return;
        var w = _sea.Core.WindVec;
        double speed = Math.Sqrt(w.X * w.X + w.Z * w.Z);
        // le battant sous le vent : là où va l'air (WindVec), en lacet du monde
        float downwind = (float)Math.Atan2(w.X, w.Z);
        var eye = _cam.GlobalPosition;
        for (int i = _shoreFlags.Count - 1; i >= 0; i--)
        {
            var f = _shoreFlags[i];
            if (!IsInstanceValid(f.Node)) { _shoreFlags.RemoveAt(i); continue; }
            if (!f.Node.IsVisibleInTree()) continue;
            var at = f.Pivot.GlobalPosition;
            // au-delà de deux kilomètres il n'est qu'un point : on ne le fait plus danser
            if (at.DistanceSquaredTo(eye) > 2000f * 2000f) continue;
            float parentYaw = f.Pivot.GetParent<Node3D>().GlobalRotation.Y;
            f.Pivot.Rotation = new Vector3(0, downwind - parentYaw, 0);
            // vent de l'arrière pour l'étamine : elle flotte droit le long de son pivot
            f.Cloth.Stream(Math.PI, 1, speed, _t, _flagDrownTest);
            f.Live.Upload(f.Mesh, f.Mat, f.Cloth.Positions, f.Cloth.Normals, f.V, f.N);
        }
    }
}
