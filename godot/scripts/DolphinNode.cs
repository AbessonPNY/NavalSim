using Godot;
using System;
using System.Collections.Generic;
using NavalSim.Core;

namespace NavalSim;

/// <summary>
/// LES DAUPHINS, DESSINÉS — le modèle de <c>creatures/dolphin.glb</c> (écrit par
/// <c>tools/dolphin-glb.js</c>, à retoucher dans Blender), posé là où le noyau
/// (<see cref="Dolphins"/>) met chaque animal.
///
/// Rien de plus qu'une pose : sa place, son cap, son assiette le long de l'arc
/// qu'il décrit, sa taille, et la queue qui bat autour de sa charnière. Tout le
/// comportement est dans le noyau, partagé avec la page.
///
/// SEPT AU PLUS, et c'est pourquoi ils ne sont PAS un MultiMesh comme les
/// mouettes ou les bancs : chacun poursuit le navire avec un ressort et une
/// vitesse propres, ce qu'un shader ne peut pas faire — il ne sait rien de
/// l'erre du bord. Sept nœuds et sept poses par image ne coûtent rien.
///
/// Le jeu le lit PAR LE NOM : un nœud <c>queue</c> est la nageoire caudale, et
/// son origine est la charnière. Sans lui, le dauphin nage sans battre.
/// </summary>
public partial class DolphinNode : Node3D
{
    public readonly List<ShaderMaterial> Hazed = new();

    sealed class Skin
    {
        public Node3D Root = null!;
        public Node3D? Tail;
        public Basis TailRest = Basis.Identity;
    }

    static readonly System.Text.RegularExpressions.Regex TailNames =
        new("^(queue|tail|caudale)$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    readonly List<Skin> _skins = new();

    /// <summary>Lire le modèle et en faire sept. Faux s'il manque : rien ne nagera.</summary>
    public bool Build(string? path)
    {
        Visible = false;
        if (path == null || !System.IO.File.Exists(path))
        {
            GD.PushWarning($"dauphins : modèle introuvable ({path})");
            return false;
        }
        var haze = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/hull_haze.gdshader") };
        Hazed.Add(haze);

        for (int i = 0; i < Dolphins.Max; i++)
        {
            var doc = new GltfDocument();
            var state = new GltfState();
            if (doc.AppendFromFile(path, state) != Error.Ok || doc.GenerateScene(state) is not Node3D root)
            {
                GD.PushWarning($"dauphins : {path} illisible");
                return false;
            }
            var skin = new Skin { Root = root };
            var stack = new Stack<Node>();
            stack.Push(root);
            while (stack.Count > 0)
            {
                var n = stack.Pop();
                // les trois noms que la fiche du modèle annonce (creatures/README.md)
                if (n is Node3D n3 && TailNames.IsMatch(n3.Name.ToString()))
                {
                    skin.Tail = n3;
                    skin.TailRest = n3.Basis;
                }
                /* LA BRUME PAR-DESSUS, comme tout ce qui réfléchit : un dauphin à
                   deux cents mètres doit se fondre dans l'air comme une coque, et
                   la matière du modèle est celle de l'artiste — on ne la réécrit
                   pas, on lui ajoute une passe. */
                if (n is MeshInstance3D mi && mi.Mesh != null)
                    for (int s = 0; s < mi.Mesh.GetSurfaceCount(); s++)
                        if (mi.GetActiveMaterial(s) is BaseMaterial3D bm)
                        {
                            var own = (BaseMaterial3D)bm.Duplicate();
                            own.NextPass = haze;
                            mi.SetSurfaceOverrideMaterial(s, own);
                        }
                foreach (var ch in n.GetChildren()) stack.Push(ch);
            }
            root.Visible = false;
            AddChild(root);
            _skins.Add(skin);
        }
        GD.Print($"[dauphins] modèle chargé : {_skins.Count} animaux" +
                 (_skins[0].Tail == null ? " (pas de nœud « queue » : elle ne battra pas)" : ""));
        return true;
    }

    /// <summary>
    /// Poser la bande. Les positions du noyau sont en mètres LOCAUX — les mêmes
    /// que la coque —, donc rien à convertir ici.
    /// </summary>
    public void Sync(Dolphins pod)
    {
        if (_skins.Count == 0) return;
        bool any = false;
        for (int i = 0; i < _skins.Count && i < Dolphins.Max; i++)
        {
            var a = pod.Pod[i];
            var sk = _skins[i];
            sk.Root.Visible = a.On;
            if (!a.On) continue;
            any = true;
            sk.Root.Position = new Vector3((float)a.Pos.X, (float)a.Y, (float)a.Pos.Z);
            sk.Root.Basis = new Basis(Vector3.Up, (float)a.Yaw)
                          * new Basis(Vector3.Right, (float)a.Pitch)
                          * Basis.Identity.Scaled(Vector3.One * (float)a.Scale);
            if (sk.Tail != null)
                sk.Tail.Basis = sk.TailRest * new Basis(Vector3.Right, (float)a.Tail);
        }
        Visible = any;
    }
}
