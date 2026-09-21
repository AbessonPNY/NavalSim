using Godot;
using System;
using System.Collections.Generic;
using NavalSim.Core;

namespace NavalSim;

/// <summary>
/// LA BALEINE DESSINÉE — le modèle de <c>creatures/whale.glb</c> (écrit par
/// <c>tools/whale-glb.js</c>, à retoucher dans Blender), posé là où le noyau
/// (<see cref="Whale"/>) la met.
///
/// Rien de plus qu'une pose : son axe à la profondeur voulue sous la surface
/// qu'on voit, son cap, son assiette — piquer, c'est la queue qui sort —, et la
/// queue qui bat autour de sa charnière, plus vite quand elle force. Sous l'eau
/// on la voit à travers la mer : c'est la mer qui en fait une ombre, comme elle
/// le fait de la carène.
/// </summary>
public partial class WhaleNode : Node3D
{
    Node3D? _body, _fluke;
    Basis _flukeRest = Basis.Identity;
    double _phase;
    bool _white;
    readonly List<(MeshInstance3D Mi, int S, Material Own, Material White)> _skins = new();
    public readonly List<ShaderMaterial> Hazed = new();

    /// <summary>Lire le modèle ; faux s'il manque — la baleine reste alors invisible.</summary>
    public bool Build(string? path)
    {
        Visible = false;
        if (path == null || !System.IO.File.Exists(path))
        {
            GD.PushWarning($"baleine : modèle introuvable ({path})");
            return false;
        }
        var doc = new GltfDocument();
        var state = new GltfState();
        if (doc.AppendFromFile(path, state) != Error.Ok || doc.GenerateScene(state) is not Node3D root) return false;
        _body = new Node3D();
        AddChild(_body);
        _body.AddChild(root);
        var haze = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/hull_haze.gdshader") };
        Hazed.Add(haze);
        var stack = new Stack<Node>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var n = stack.Pop();
            if (n is Node3D n3 && n.Name.ToString().Equals("queue", StringComparison.OrdinalIgnoreCase))
            {
                _fluke = n3;
                _flukeRest = n3.Basis;
            }
            if (n is MeshInstance3D mi && mi.Mesh != null)
                for (int s = 0; s < mi.Mesh.GetSurfaceCount(); s++)
                    if (mi.GetActiveMaterial(s) is BaseMaterial3D bm)
                    {
                        var own = (BaseMaterial3D)bm.Duplicate();
                        own.NextPass = haze;
                        /* LA BALEINE BLANCHE : la même bête, la peau sans ses
                           couleurs de sommet — un blanc cassé, jaunâtre, marqué. */
                        var white = (BaseMaterial3D)own.Duplicate();
                        white.VertexColorUseAsAlbedo = false;
                        white.AlbedoColor = new Color(0.80f, 0.78f, 0.72f);
                        white.NextPass = haze;
                        mi.SetSurfaceOverrideMaterial(s, own);
                        _skins.Add((mi, s, own, white));
                    }
            foreach (var c in n.GetChildren()) stack.Push(c);
        }
        return true;
    }

    /// <summary>À chaque image : où elle est, et sa queue.</summary>
    public void Sync(Whale w, Ocean sea, double t, double dt)
    {
        if (_body == null) return;
        Visible = w.State != WhaleState.Absent;
        if (!Visible) return;
        if (w.White != _white)
        {
            _white = w.White;
            foreach (var (mi, s, own, white) in _skins) mi.SetSurfaceOverrideMaterial(s, _white ? white : own);
        }
        double surf = sea.Sample(w.Pos.X, w.Pos.Z, t);
        Position = new Vector3((float)w.Pos.X, (float)(surf - w.Y), (float)w.Pos.Z);
        // le lacet, puis l'assiette autour de son propre travers
        var b = new Basis(Vector3.Up, (float)w.Heading) * new Basis(Vector3.Right, (float)-w.Pitch);
        Basis = b;
        if (_fluke != null)
        {
            // un battement de trois à six secondes, plus ample à la charge
            double rate = 0.18 + 0.05 * w.Speed;
            _phase += dt * rate * Math.Tau;
            double amp = 0.18 + 0.05 * w.Speed;
            _fluke.Basis = _flukeRest * new Basis(Vector3.Right, (float)(amp * Math.Sin(_phase)));
        }
    }
}
