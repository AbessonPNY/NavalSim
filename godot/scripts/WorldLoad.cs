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
        region.Key = System.IO.Path.GetFileNameWithoutExtension(path);
        string img = System.IO.Path.Combine(Folder, region.Relief.Image);
        if (!System.IO.File.Exists(img))
        {
            GD.PushWarning($"relief introuvable : {region.Relief.Image} — la mer reste sans terre.");
            return null;
        }
        var (w, h, grey) = GreyPng.Decode(System.IO.File.ReadAllBytes(img));
        /* LES RELIEFS LOCAUX (world/README.md, « un relief local ») : des images
           plus fines sur un bout de la région. Une qui manque est simplement
           absente — la côte reste celle du grand relief. */
        var patches = new System.Collections.Generic.List<World.PatchImage>();
        foreach (var p in region.Patches)
        {
            string pi = System.IO.Path.Combine(Folder, p.Image);
            if (!System.IO.File.Exists(pi)) { GD.PushWarning($"relief local introuvable : {p.Image}"); continue; }
            var (pw, ph, pg) = GreyPng.Decode(System.IO.File.ReadAllBytes(pi));
            patches.Add(new World.PatchImage(p, pw, ph, pg));
        }
        double read = watch.Elapsed.TotalMilliseconds;
        var world = new World(region, w, h, grey, GD.PushWarning, patches);
        double make = watch.Elapsed.TotalMilliseconds - read;
        GD.Print(FormattableString.Invariant($"monde : {region.Name}, relief {w}×{h} lu en {read:F0} ms, {world.Isles.Count} port(s) en {make:F0} ms — un pixel vaut {world.Px:F0} m"));
        foreach (var p in patches)
            GD.Print(FormattableString.Invariant(
                $"  relief local {p.Spec.Key} : {p.W}×{p.H} px, fondu {p.Spec.Feather:F0} m"));
        return world;
    }

    /// <summary>
    /// TOUTES LES RÉGIONS de <c>world/</c>, fiches seules — ni image ni champ de
    /// distance : de quoi calculer une traversée sans charger une terre qu'on
    /// ne verra pas. Une fiche qui ne se lit pas est passée, avec un mot.
    /// </summary>
    public static System.Collections.Generic.List<RegionSpec> Regions()
    {
        var all = new System.Collections.Generic.List<RegionSpec>();
        string dir = System.IO.Path.Combine(Folder, "world");
        if (!System.IO.Directory.Exists(dir)) return all;
        foreach (var f in System.IO.Directory.GetFiles(dir, "*.json"))
        {
            try
            {
                var r = RegionSpec.FromJson(System.IO.File.ReadAllText(f));
                r.Key = System.IO.Path.GetFileNameWithoutExtension(f);
                all.Add(r);
            }
            catch (Exception ex) { GD.PushWarning($"fiche de région illisible : {f} ({ex.Message})"); }
        }
        all.Sort((a, b) => string.CompareOrdinal(a.Key, b.Key));
        return all;
    }
}
