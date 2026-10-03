using Godot;
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace NavalSim;

/// <summary>
/// LA CHALOUPE SUR LE PONT — celle du modèle, qu'on ne voit plus quand elle est à
/// l'eau (signalé : on en voyait deux, l'une sur ses chantiers, l'autre qui nage).
///
/// La chaloupe à l'eau est un NAVIRE à part (ShipDemo.Boat.cs) ; celle du pont
/// n'est qu'une pièce du modèle, reconnue à son nom. On la cache à la mise à
/// l'eau, on la rend au hissage — et c'est par son travers que la chaloupe est
/// affalée : elle descend d'où elle était posée.
/// </summary>
public partial class ShipNode
{
    static readonly Regex BoatNames = new("chaloupe|canot|yole|longboat", RegexOptions.IgnoreCase);

    readonly List<Node3D> _deckBoats = new();

    /// <summary>La station (z, repère du navire) de la chaloupe du pont, ou nulle s'il n'y en a pas.</summary>
    public double? DeckBoatZ { get; private set; }

    void FindDeckBoat()
    {
        _deckBoats.Clear();
        DeckBoatZ = null;
        // seul un navire qui PORTE une chaloupe en a une sur son pont ; la chaloupe elle-même n'en cherche pas
        if (ModelRoot == null || Spec.Boat.Length == 0) return;
        double zs = 0; int n = 0;
        foreach (var (mi, _) in Meshes(ModelRoot))
        {
            string name = mi.Name.ToString();
            if (!BoatNames.IsMatch(name) || Cordage.IsMatch(name)) continue;
            _deckBoats.Add(mi);
            var box = mi.Mesh.GetAabb();
            zs += (RelTo(mi, this) * (box.Position + box.Size * 0.5f)).Z;
            n++;
        }
        if (n > 0)
        {
            DeckBoatZ = zs / n;
            GD.Print(FormattableString.Invariant($"[{Spec.Id}] chaloupe du pont : {string.Join(", ", _deckBoats.ConvertAll(b => b.Name.ToString()))}, station {DeckBoatZ:F1} m"));
        }
    }

    /// <summary>Montrer ou cacher la chaloupe posée sur le pont.</summary>
    public void ShowDeckBoat(bool on)
    {
        foreach (var b in _deckBoats) if (IsInstanceValid(b)) b.Visible = on;
    }
}
