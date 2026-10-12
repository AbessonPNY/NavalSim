using Godot;
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace NavalSim;

/// <summary>
/// PAR OÙ L'ON MONTE À BORD depuis l'eau, lu sur le modèle.
///
/// UNE ÉCHELLE est un objet dont le nom contient <c>echelle</c>, <c>échelle</c>,
/// <c>escalier</c> ou <c>ladder</c> (<c>echelleBabord</c>, <c>escalier.001</c>),
/// posée contre le flanc : sa boîte dit le bord (le signe de son x), la station,
/// d'où à où elle monte, et jusqu'où elle sort du bordé. Un navire qui en a une ne
/// se monte que par elle — sur un flanc de quatre mètres, on ne grimpe pas à
/// mains nues.
///
/// LE FLANC lui-même est pris sur les sommets de la coque du modèle, à la
/// hauteur de chaque prise : le profil de flottaison que lit la mer ne connaît
/// ni l'évasement des hauts ni leur rentrée, et un grimpeur placé d'après lui se
/// retrouvait dans les planches.
/// </summary>
public partial class ShipNode
{
    internal static readonly Regex LadderNames = new("echelle|échelle|escalier|ladder", RegexOptions.IgnoreCase);

    /// <summary>Une échelle, dans le repère du navire.</summary>
    public sealed class Ladder
    {
        public string Name = "";
        public int Side;               // +1 bâbord (+x), −1 tribord
        public double Z, Bottom, Top;
        /// <summary>Son dehors : la distance de l'axe à la face où l'on pose les mains.</summary>
        public double Out;
    }

    List<Ladder>? _ladders;
    Node3D? _laddersOf;
    Vector3[]? _sideVerts;
    Node3D? _sideVertsOf;

    /// <summary>Les échelles du modèle (aucune sans modèle, ou si personne n'en a posé).</summary>
    public IReadOnlyList<Ladder> Ladders()
    {
        if (_ladders != null && _laddersOf == ModelRoot) return _ladders;
        _ladders = new List<Ladder>();
        _laddersOf = ModelRoot;
        if (ModelRoot == null || !ModelRoot.IsInsideTree()) return _ladders;
        var found = new List<Node3D>();
        void Gather(Node n)
        {
            foreach (var ch in n.GetChildren())
            {
                if (ch is Node3D n3 && LadderNames.IsMatch(n3.Name.ToString())) { found.Add(n3); continue; }
                Gather(ch);
            }
        }
        Gather(ModelRoot);
        if (found.Count == 0 && TestLadder) found.Add(MakeTestLadder());
        var toShip = GlobalTransform.AffineInverse();
        foreach (var nd in found)
        {
            var bb = PieceBox(nd);
            var rel = toShip * nd.GlobalTransform;
            Vector3 lo = Vector3.Inf, hi = -Vector3.Inf;
            for (int i = 0; i < 8; i++)
            {
                var p = rel * bb.GetEndpoint(i);
                lo = lo.Min(p); hi = hi.Max(p);
            }
            double mx = (lo.X + hi.X) * 0.5;
            if (Math.Abs(mx) < 0.05) continue;            // dans l'axe : une échelle de descente, pas de flanc
            int side = Math.Sign(mx);
            _ladders.Add(new Ladder
            {
                Name = nd.Name.ToString(), Side = side, Z = (lo.Z + hi.Z) * 0.5,
                Bottom = lo.Y, Top = hi.Y, Out = side > 0 ? hi.X : -lo.X
            });
        }
        if (_ladders.Count > 0)
            GD.Print($"[nage] {Spec.Name} : {_ladders.Count} échelle(s) — " + string.Join(", ", _ladders.ConvertAll(l => FormattableString.Invariant(
                $"{l.Name} ({(l.Side > 0 ? "bâbord" : "tribord")}, z {l.Z:F2}, de {l.Bottom:F2} à {l.Top:F2} m, dehors {l.Out:F2})"))));
        return _ladders;
    }

    /// <summary>
    /// Le dehors du flanc sur le bord <paramref name="side"/>, à la station z et à la
    /// hauteur y (repère du navire) : le plus loin de l'axe qu'aille la coque du modèle
    /// à ±25 cm de là. NaN sans modèle, ou si la coque n'y passe pas.
    /// </summary>
    public double SideOut(int side, double z, double y)
    {
        var v = SideVerts();
        if (v == null) return double.NaN;
        double best = double.NaN;
        for (int pass = 0; pass < 2 && double.IsNaN(best); pass++)
        {
            // d'abord à cette hauteur ; sinon le plus large de la station, quelle que soit la hauteur
            double dy = pass == 0 ? 0.2 : 1e9;
            for (int i = 0; i < v.Length; i++)
            {
                var p = v[i];
                if (Math.Sign(p.X) != side || Math.Abs(p.Z - z) > 0.25 || Math.Abs(p.Y - y) > dy) continue;
                double o = Math.Abs(p.X);
                if (double.IsNaN(best) || o > best) best = o;
            }
        }
        return best;
    }

    /// <summary>Le haut du flanc à cette station, sur ce bord : le plat-bord.</summary>
    public double SideTop(int side, double z)
    {
        var v = SideVerts();
        if (v == null) return double.NaN;
        // seulement la peau : à 35 cm du dehors au plus — un mât, une bôme passent aussi à cette station
        double skin = double.NaN;
        foreach (var p in v)
            if (Math.Sign(p.X) == side && Math.Abs(p.Z - z) < 0.25 && (double.IsNaN(skin) || Math.Abs(p.X) > skin)) skin = Math.Abs(p.X);
        if (double.IsNaN(skin)) return double.NaN;
        skin -= 0.35;
        double top = double.NaN;
        foreach (var p in v)
            if (Math.Sign(p.X) == side && Math.Abs(p.Z - z) < 0.25 && Math.Abs(p.X) >= skin && (double.IsNaN(top) || p.Y > top)) top = p.Y;
        return top;
    }

    /// <summary>
    /// ESSAI (-- --echelle-essai 1) : faute d'échelle modelée, une planche posée contre
    /// le flanc de bâbord, au milieu, et nommée comme le serait une échelle.
    /// </summary>
    public static bool TestLadder;

    Node3D MakeTestLadder()
    {
        double rail = SideTop(1, 0);
        double top = double.IsNaN(rail) ? Spec.DeckMid : rail - 0.1, bottom = -0.8;
        double outX = SideOut(1, 0, (top + bottom) * 0.5);
        if (double.IsNaN(outX)) outX = Spec.B * 0.5;
        var box = new MeshInstance3D
        {
            Name = "echelleEssai",
            Mesh = new BoxMesh { Size = new Vector3(0.08f, (float)(top - bottom), 0.45f) },
            MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.55f, 0.12f, 0.08f) }
        };
        ModelRoot!.AddChild(box);
        box.GlobalTransform = GlobalTransform * new Transform3D(Basis.Identity, new Vector3((float)outX + 0.04f, (float)((top + bottom) * 0.5), 0));
        return box;
    }

    /// <summary>Les sommets de la coque (la plus grosse pièce), gardés une fois par modèle.</summary>
    Vector3[]? SideVerts()
    {
        if (_sideVertsOf == ModelRoot && _sideVerts != null) return _sideVerts;
        _sideVertsOf = ModelRoot;
        _sideVerts = ModelRoot != null && HullPart() is { } hull ? hull.Verts : null;
        return _sideVerts;
    }
}
