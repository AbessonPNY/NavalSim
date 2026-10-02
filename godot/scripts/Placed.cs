using Godot;
using NavalSim.Core;

namespace NavalSim;

/// <summary>
/// UN OBJET POSÉ À TERRE, et sa place VRAIE gardée en C# — un rocher, un cocotier,
/// un modèle posé, un ajout de l'éditeur.
///
/// Elle vivait dans les métadonnées du nœud (SetMeta / GetMeta) et se relisait à
/// chaque image : trois lectures par objet, chacune fabriquant un nom Godot, pour
/// trois cent trente objets — 179 Ko alloués par image et 1,6 ms, mesurés. Et on
/// les REPLAÇAIT à chaque image alors qu'ils ne bougent que quand l'origine glisse
/// ou que l'éditeur les déplace : <see cref="Place"/> n'est appelé que là.
/// </summary>
public sealed class Placed
{
    public Node3D Node = null!;
    public double X, Y, Z;

    public void Place(Vec3d origin) =>
        Node.Position = new Vector3((float)(X - origin.X), (float)Y, (float)(Z - origin.Z));
}

/// <summary>Une liste d'objets posés, replacés seulement quand l'origine change.</summary>
public sealed class PlacedSet
{
    public readonly System.Collections.Generic.List<Placed> Items = new();
    Vec3d _origin;
    bool _known;

    public int Count => Items.Count;

    public Placed Add(Node3D node, double x, double y, double z)
    {
        var p = new Placed { Node = node, X = x, Y = y, Z = z };
        Items.Add(p);
        if (_known) p.Place(_origin);
        return p;
    }

    /// <summary>Un objet a bougé (l'éditeur) : le reposer tout de suite, contre l'origine connue.</summary>
    public void Moved(Placed p) { if (_known) p.Place(_origin); }

    /// <summary>Chaque image : rien, sauf si l'origine a glissé depuis la dernière fois.</summary>
    public void Update(Vec3d origin)
    {
        if (_known && origin.X == _origin.X && origin.Z == _origin.Z) return;
        _origin = origin; _known = true;
        foreach (var p in Items) p.Place(origin);
    }
}
