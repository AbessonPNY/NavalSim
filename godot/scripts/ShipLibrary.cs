using Godot;
using System.Collections.Generic;
using NavalSim.Core;

namespace NavalSim;

/// <summary>
/// Ou vivent les fiches navires.
///
/// LE DOSSIER FAIT FOI, et c'est la regle du projet qu'on ne casse pas en
/// portant : deposer un fichier dans ships/ doit suffire. Le dossier reste donc
/// a la RACINE du depot, partage avec la simulation JavaScript, plutot que
/// recopie sous godot/ -- deux copies d'une fiche, c'est deux fiches qui
/// divergeront, exactement la faute que l'invariant du plan de formes unique
/// existe pour empecher.
///
/// En developpement on y accede en remontant d'un cran depuis res://. Le jour de
/// l'export il faudra les embarquer, puisqu'un binaire exporte n'a plus de depot
/// autour de lui -- c'est le pendant de ce que build.js faisait en les inlinant
/// dans Naval.SHIP_DATA, et c'est la meme contrainte pour la meme raison.
/// </summary>
public static class ShipLibrary
{
    /// <summary>Le dossier ships/, a la racine du depot.</summary>
    public static string Folder
    {
        get
        {
            string proj = ProjectSettings.GlobalizePath("res://");
            return System.IO.Path.GetFullPath(System.IO.Path.Combine(proj, "..", "ships"));
        }
    }

    /// <summary>
    /// Les fiches presentes, triees. index.json est ecarte : c'est la LISTE des
    /// fiches, pas une fiche.
    /// </summary>
    public static List<string> Discover()
    {
        var found = new List<string>();
        if (!System.IO.Directory.Exists(Folder))
        {
            GD.PushWarning($"dossier des fiches introuvable : {Folder}");
            return found;
        }
        foreach (string f in System.IO.Directory.GetFiles(Folder, "*.json"))
            if (System.IO.Path.GetFileName(f) != "index.json")
                found.Add(f);
        found.Sort();
        return found;
    }

    /// <summary>
    /// Charge une fiche. Une fiche illisible n'interrompt jamais rien -- meme
    /// contrat que le modele .glb manquant cote JavaScript, ou la coque
    /// procedurale est conservee avec un avertissement.
    /// </summary>
    public static ShipSpec? Load(string path)
    {
        try
        {
            return ShipSpec.FromJson(System.IO.File.ReadAllText(path));
        }
        catch (System.Exception e)
        {
            GD.PushWarning($"fiche illisible {System.IO.Path.GetFileName(path)} : {e.Message}");
            return null;
        }
    }
}
