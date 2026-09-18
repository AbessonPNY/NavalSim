using Godot;

namespace NavalSim;

/// <summary>
/// LES RÉGLAGES DU JOUEUR, dans un fichier .ini — le pendant de settings.json de
/// la page, pour ce que le portage a ajouté ou rendu réglable.
///
/// Écrit par ConfigFile, le format .ini natif de Godot, dans <c>user://</c> : le
/// seul dossier où un jeu exporté a le droit d'écrire. Créé au premier lancement
/// avec les valeurs par défaut, relu à chaque démarrage, réécrit à chaque
/// changement fait dans le menu (Échap). Une clé absente ou illisible reprend sa
/// valeur par défaut : un fichier abîmé ne doit pas empêcher de naviguer.
/// </summary>
public sealed class Settings
{
    public const string Path = "user://reglages.ini";

    // [lanternes]
    /// <summary>Les lampes des feux projettent des ombres (cubes d'ombre, un par feu).</summary>
    public bool LanternShadows = true;
    /// <summary>La lanterne à mi-hauteur du grand mât — un ajout, absente de la page.</summary>
    public bool MastLantern = true;
    /// <summary>Le reflet des feux sur la mer : 2,4 est le gain du reflet du soleil.</summary>
    public float LampReflection = 2.4f;
    /// <summary>La lumière des feux entrée dans l'eau et qui en ressort.</summary>
    public float LampWater = 0.6f;

    // [rendu]
    /// <summary>L'occlusion ambiante (touche O).</summary>
    public bool Occlusion = true;
    /// <summary>La lumière indirecte en espace écran (touche G).</summary>
    public bool IndirectLight = true;
    /// <summary>La synchro verticale : voir la mémoire sur les écrans virtuels.</summary>
    public bool VSync = true;

    // [performance]
    /// <summary>Les solveurs des navires sur plusieurs cœurs : résultats identiques au bit près.</summary>
    public bool ParallelSolvers = true;

    public static Settings Load()
    {
        var s = new Settings();
        var cf = new ConfigFile();
        if (cf.Load(Path) != Error.Ok)
        {
            s.Save();                                   // premier lancement : le fichier par défaut
            return s;
        }
        s.LanternShadows = (bool)cf.GetValue("lanternes", "ombres", s.LanternShadows);
        s.MastLantern = (bool)cf.GetValue("lanternes", "lanterne_grand_mat", s.MastLantern);
        s.LampReflection = (float)cf.GetValue("lanternes", "reflet_sur_la_mer", s.LampReflection);
        s.LampWater = (float)cf.GetValue("lanternes", "lumiere_dans_l_eau", s.LampWater);
        s.Occlusion = (bool)cf.GetValue("rendu", "occlusion_ambiante", s.Occlusion);
        s.IndirectLight = (bool)cf.GetValue("rendu", "lumiere_indirecte", s.IndirectLight);
        s.VSync = (bool)cf.GetValue("rendu", "synchro_verticale", s.VSync);
        s.ParallelSolvers = (bool)cf.GetValue("performance", "solveurs_paralleles", s.ParallelSolvers);
        return s;
    }

    public void Save()
    {
        var cf = new ConfigFile();
        cf.SetValue("lanternes", "ombres", LanternShadows);
        cf.SetValue("lanternes", "lanterne_grand_mat", MastLantern);
        cf.SetValue("lanternes", "reflet_sur_la_mer", LampReflection);
        cf.SetValue("lanternes", "lumiere_dans_l_eau", LampWater);
        cf.SetValue("rendu", "occlusion_ambiante", Occlusion);
        cf.SetValue("rendu", "lumiere_indirecte", IndirectLight);
        cf.SetValue("rendu", "synchro_verticale", VSync);
        cf.SetValue("performance", "solveurs_paralleles", ParallelSolvers);
        Error e = cf.Save(Path);
        if (e != Error.Ok) GD.PushWarning($"réglages non enregistrés dans {Path} : {e}");
    }
}
