using Godot;
using System;
using NavalSim.Core;

namespace NavalSim;

/// <summary>
/// LIRE LE MONDE : la fiche de région et son image de relief.
///
/// Même convention que les fiches navires et leurs modèles : les deux fichiers
/// vivent HORS du projet Godot, dans <c>world/</c>, où la page les lit aussi. Un
/// seul dossier pour les deux versions, et rien à réimporter quand le relief est
/// repeint.
///
/// Le PNG est décodé par <see cref="GreyPng"/>, celui du noyau, et non par
/// l'Image de Godot : c'est le décodeur que le banc de parité vérifie, et une
/// terre que le moteur lirait autrement que le banc serait une terre dont
/// personne ne garantit rien.
/// </summary>
public static class WorldLoad
{
    /// <summary>Le dossier du projet, au-dessus de res://.</summary>
    public static string Folder =>
        System.IO.Path.GetFullPath(System.IO.Path.Combine(ProjectSettings.GlobalizePath("res://"), ".."));

    /// <summary>
    /// Rend le monde, ou <c>null</c> si la région manque — le jeu sait encore
    /// naviguer sur une mer sans terre, et c'est ce qu'il a fait jusqu'ici.
    /// </summary>
    public static World? Load(string sheet = "world/caraibes.json")
    {
        string path = System.IO.Path.Combine(Folder, sheet);
        if (!System.IO.File.Exists(path))
        {
            GD.PushWarning($"région introuvable : {sheet} — la mer reste sans terre.");
            return null;
        }
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var region = RegionSpec.FromJson(System.IO.File.ReadAllText(path));
        string img = System.IO.Path.Combine(Folder, region.Relief.Image);
        if (!System.IO.File.Exists(img))
        {
            GD.PushWarning($"relief introuvable : {region.Relief.Image} — la mer reste sans terre.");
            return null;
        }
        var (w, h, grey) = GreyPng.Decode(System.IO.File.ReadAllBytes(img));
        double read = watch.Elapsed.TotalMilliseconds;
        var world = new World(region, w, h, grey, GD.PushWarning);
        double make = watch.Elapsed.TotalMilliseconds - read;
        GD.Print(FormattableString.Invariant($"monde : {region.Name}, relief {w}×{h} lu en {read:F0} ms, {world.Isles.Count} port(s) en {make:F0} ms — un pixel vaut {world.Px:F0} m"));
        return world;
    }
}
