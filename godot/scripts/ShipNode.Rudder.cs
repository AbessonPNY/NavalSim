using Godot;
using System;
using System.Text.RegularExpressions;

namespace NavalSim;

/// <summary>
/// LE SAFRAN QUI TOURNE AVEC LA BARRE — _buildRudder de la page, porté. Un objet du
/// modèle nommé « gouvernail » (ou rudder, safran) est ferré sur l'étambot à son BORD
/// AVANT et pivote là, autour de l'axe vertical du navire. Il restait figé sous Godot
/// (signalé) : la Boussole, la frégate et le Roebuck en ont un.
///
/// L'angle est celui avec lequel le solveur gouverne, Ctrl.Rudder × RudderMax, au
/// même signe : barre positive, le bord de fuite part à tribord (−x).
/// </summary>
public partial class ShipNode
{
    static readonly Regex RudderNames = new("^(gouvernail|rudder|safran)", RegexOptions.IgnoreCase);

    Node3D? _rudderPivot;
    /// <summary>L'axe vertical du navire, dans le repère du parent du safran.</summary>
    Vector3 _rudderAxis = Vector3.Up;
    float _rudderShown = float.NaN;

    /// <summary>Le safran du modèle, pendu à sa charnière dans son propre parent (il reste du modèle).</summary>
    void FindRudder()
    {
        _rudderPivot = null;
        _rudderShown = float.NaN;
        if (ModelRoot == null || !(Spec.RudderK > 0)) return;
        Node3D? found = null;
        void Look(Node n)
        {
            foreach (var ch in n.GetChildren())
            {
                if (found != null) return;
                if (ch is Node3D n3 && RudderNames.IsMatch(n3.Name.ToString()) && HasMesh(n3)) { found = n3; return; }
                Look(ch);
            }
        }
        Look(ModelRoot);
        if (found?.GetParent() is not Node3D parent) return;

        // la boîte de la lame dans le repère du navire : la charnière au milieu, au bord avant (+z)
        var toShip = GlobalTransform.AffineInverse();
        var rel = toShip * found.GlobalTransform;
        var bb = PieceBox(found);
        Vector3 lo = Vector3.Inf, hi = -Vector3.Inf;
        for (int i = 0; i < 8; i++)
        {
            var p = rel * bb.GetEndpoint(i);
            lo = lo.Min(p); hi = hi.Max(p);
        }
        var hinge = new Vector3((lo.X + hi.X) * 0.5f, (lo.Y + hi.Y) * 0.5f, hi.Z);
        var axis = Vector3.Up;
        /* L'ÉTAMBOT EST INCLINÉ, la tête en arrière, et le safran tourne autour de lui :
           pivoté sur la verticale, sa lame sortait de la ligne de la coque (signalé).
           L'axe est pris sur le BORD AVANT lui-même — le point le plus en avant du
           cinquième haut de la lame et celui du cinquième bas. */
        if (RudderEdge(found, toShip, lo.Y, hi.Y) is var (bot, top) && (top - bot).Y > 0.2f * (hi.Y - lo.Y))
        {
            float mx = (lo.X + hi.X) * 0.5f;
            bot.X = mx; top.X = mx;
            axis = (top - bot).Normalized();
            hinge = bot;
        }

        var parentFromShip = parent.GlobalTransform.AffineInverse() * GlobalTransform;
        _rudderAxis = (parentFromShip.Basis * axis).Normalized();
        var pivot = new Node3D { Name = "ferrure_" + found.Name, Position = parentFromShip * hinge };
        parent.AddChild(pivot);
        found.Reparent(pivot, true);
        _rudderPivot = pivot;
        RigLog.Add($"safran : pivoté sur son bord avant, incliné de {Mathf.RadToDeg(Mathf.Acos(Math.Clamp(axis.Y, -1f, 1f))):F0}°");

        static bool HasMesh(Node n)
        {
            if (n is MeshInstance3D { Mesh: not null }) return true;
            foreach (var c in n.GetChildren()) if (HasMesh(c)) return true;
            return false;
        }
    }

    /// <summary>
    /// Le bord avant de la lame, en repère du navire : le sommet le plus en avant (+z)
    /// de sa bande basse et celui de sa bande haute (un cinquième de sa hauteur chacune).
    /// </summary>
    static (Vector3 bot, Vector3 top)? RudderEdge(Node3D blade, Transform3D toShip, float y0, float y1)
    {
        float band = 0.2f * (y1 - y0);
        Vector3? bot = null, top = null;
        void Walk(Node n)
        {
            if (n is MeshInstance3D { Mesh: { } mesh } mi)
            {
                var xf = toShip * mi.GlobalTransform;
                for (int s = 0; s < mesh.GetSurfaceCount(); s++)
                {
                    var arr = mesh.SurfaceGetArrays(s);
                    if (arr.Count == 0 || arr[(int)Mesh.ArrayType.Vertex].AsVector3Array() is not { } vs) continue;
                    foreach (var v in vs)
                    {
                        var p = xf * v;
                        if (p.Y <= y0 + band) { if (bot is not { } b || p.Z > b.Z) bot = p; }
                        else if (p.Y >= y1 - band) { if (top is not { } t || p.Z > t.Z) top = p; }
                    }
                }
            }
            foreach (var c in n.GetChildren()) Walk(c);
        }
        Walk(blade);
        return bot is { } b2 && top is { } t2 ? (b2, t2) : null;
    }

    /// <summary>Une image : le safran à l'angle de la barre.</summary>
    public void RudderTick()
    {
        if (_rudderPivot == null || !IsInstanceValid(_rudderPivot)) return;
        float a = (float)(Math.Clamp(Ctrl.Rudder, -1, 1) * Spec.RudderMax);
        if (a == _rudderShown) return;
        _rudderShown = a;
        _rudderPivot.Basis = new Basis(_rudderAxis, a);
    }
}
