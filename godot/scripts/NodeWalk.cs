using Godot;
using System.Collections.Generic;

namespace NavalSim;

/// <summary>
/// CE QU'ON CHERCHE DANS UN MODÈLE CHARGÉ : ses maillages, le premier d'entre eux,
/// sa boîte. Ces parcours étaient réécrits dans chaque nœud qui ouvre un .glb.
/// </summary>
public static class NodeWalk
{
    /// <summary>
    /// La boîte de tous les maillages sous <paramref name="root"/>, dans SON repère
    /// (les transformations des enfants composées, pas la sienne) ; rien s'il n'y
    /// a aucun maillage.
    /// </summary>
    public static Aabb? Bounds(Node3D root)
    {
        Aabb? box = null;
        var stack = new Stack<(Node, Transform3D)>();
        stack.Push((root, Transform3D.Identity));
        while (stack.Count > 0)
        {
            var (n, t) = stack.Pop();
            foreach (var c in n.GetChildren()) stack.Push((c, c is Node3D c3 ? t * c3.Transform : t));
            if (n is MeshInstance3D m && m.Mesh != null) { var b = t * m.Mesh.GetAabb(); box = box is Aabb a0 ? a0.Merge(b) : b; }
        }
        return box;
    }

    /// <summary>Tous les MeshInstance3D qui portent un maillage, en profondeur.</summary>
    public static IEnumerable<MeshInstance3D> Meshes(Node n)
    {
        if (n is MeshInstance3D mi && mi.Mesh != null) yield return mi;
        foreach (var c in n.GetChildren())
            foreach (var m in Meshes(c)) yield return m;
    }

    /// <summary>
    /// Le premier MeshInstance3D en profondeur ; <paramref name="withMesh"/> exige
    /// qu'il porte un maillage (sans quoi un nœud vide peut être rendu).
    /// </summary>
    public static MeshInstance3D? FirstMesh(Node n, bool withMesh = true)
    {
        if (n is MeshInstance3D m && (!withMesh || m.Mesh != null)) return m;
        foreach (var c in n.GetChildren())
            if (FirstMesh(c, withMesh) is MeshInstance3D f) return f;
        return null;
    }
}
