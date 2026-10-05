using Godot;
using System.Collections.Generic;

namespace NavalSim;

/// <summary>
/// LA PASSE DE BRUME (hull_haze.gdshader), posée sur ce qu'un .glb apporte : l'air
/// devant un modèle, la même loi que pour la mer et la coque. Elle était créée et
/// accrochée à la main dans une vingtaine d'endroits ; ce qui suit est la seule
/// façon de le faire. Le ciel qu'elle lit lui vient en global (SkyNode.PushGlobals) :
/// l'enregistrer dans une liste Hazed reste l'affaire de l'appelant.
/// </summary>
public static class HazePass
{
    /// <summary>
    /// Une passe neuve. <paramref name="priority"/> −4 pour ce qui doit passer avant
    /// la mer (coques, épaves, navires au mouillage), qui est transparente.
    /// </summary>
    public static ShaderMaterial New(int priority = 0) =>
        new() { Shader = GD.Load<Shader>("res://shaders/hull_haze.gdshader"), RenderPriority = priority };

    /// <summary>
    /// Chaque matière standard du maillage DUPLIQUÉE puis coiffée de la passe : le
    /// modèle peut être partagé, sa matière d'origine ne doit pas porter la brume
    /// d'un autre. <paramref name="vertexColour"/> pour les modèles peints aux
    /// sommets (les modèles posés sur la terre).
    /// </summary>
    public static void Wear(MeshInstance3D mi, ShaderMaterial haze, bool vertexColour = false)
    {
        if (mi.Mesh == null) return;
        for (int i = 0; i < mi.Mesh.GetSurfaceCount(); i++)
            if (mi.GetActiveMaterial(i) is BaseMaterial3D bm)
            {
                var own = (BaseMaterial3D)bm.Duplicate();
                own.NextPass = haze;
                if (vertexColour) own.VertexColorUseAsAlbedo = true;
                mi.SetSurfaceOverrideMaterial(i, own);
            }
    }

    /// <summary>
    /// La passe accrochée AU BOUT de la chaîne de chaque matière du modèle, une
    /// fois par matière (une matière partagée entre deux surfaces ne la reçoit pas
    /// deux fois). Les ShaderMaterial sont laissés : ils font leur brume eux-mêmes.
    /// </summary>
    public static void Chain(Node3D root, ShaderMaterial haze)
    {
        var done = new HashSet<Material>();
        var stack = new Stack<Node>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var n = stack.Pop();
            foreach (var c in n.GetChildren()) stack.Push(c);
            if (n is not MeshInstance3D mi || mi.Mesh == null) continue;
            for (int i = 0; i < mi.Mesh.GetSurfaceCount(); i++)
            {
                // the override is what gets drawn when there is one (ShipNode.AttachHaze, same reason)
                var mat = mi.MaterialOverride ?? mi.GetSurfaceOverrideMaterial(i) ?? mi.Mesh.SurfaceGetMaterial(i);
                if (mat == null || mat is ShaderMaterial || !done.Add(mat)) continue;
                var last = mat;
                while (last.NextPass != null && last.NextPass != haze) last = last.NextPass;
                last.NextPass = haze;
            }
        }
    }
}
