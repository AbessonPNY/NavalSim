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
    /// <summary>La profondeur de champ : net de DofNear à DofDistance mètres, flou en deçà et au-delà.</summary>
    public bool Dof = true;
    /// <summary>Net à partir d'ici ; 0 : pas de flou de près.</summary>
    public float DofNear = 0f;
    /// <summary>Flou au-delà ; DofRange.Infinity (1e6) : pas de flou au loin.</summary>
    public float DofDistance = 600f;
    /// <summary>Le fondu jusqu'au plein flou, en PART de la distance, des deux côtés : 0,5 à 600 m, c'est 300 m.</summary>
    public float DofFade = 0.5f;
    /// <summary>Le diamètre du flou, en part de l'image (Godot : dof_blur_amount).</summary>
    public float DofAmount = 0.08f;
    /// <summary>La qualité du bokeh : 0 très basse, 1 basse (celle de Godot), 2 moyenne, 3 haute. Mesuré en 1080p : +0,5 / +0,7 / +1,7 / +3,0 ms.</summary>
    public int DofQuality = 1;
    /// <summary>Le flou en ovale deux fois plus haut que large d'un objectif de scope, à la place de celui de Godot.</summary>
    public bool DofAnamorphic = false;
    /// <summary>Le flou de mouvement de la caméra, et la part d'obturateur (0,5 = 180°).</summary>
    public bool MotionBlur = true;
    public float Shutter = 0.5f;
    /// <summary>L'anticrénelage géométrique : 0, 2, 4 ou 8 échantillons par pixel.</summary>
    public int Msaa = 4;
    /// <summary>L'anticrénelage sur l'image : aucun, fxaa, smaa ou taa.</summary>
    public string ScreenAA = "fxaa";
    /// <summary>La lueur des lumières trop vives (bloom.js), la nuit seulement.</summary>
    public bool Glow = true;
    public float GlowStrength = 0.9f;
    /// <summary>L'exposition qui s'adapte, comme l'œil : absente de la page, coupée par défaut.
    /// Elle ne fait que BAISSER, passé le seuil de luminance moyenne ; et elle rend le soleil éblouissant.</summary>
    public bool AutoExposure = false;
    public float AutoExposureThreshold = 1.0f;
    /// <summary>La vitesse d'adaptation (Godot : auto_exposure_speed).</summary>
    public float AutoExposureSpeed = 0.5f;
    /// <summary>Le masque de cinéma : l'image recadrée au format 2,35:1 par deux bandes noires.</summary>
    public bool FilmMask = false;

    // [mer] — réglages de mise au point du shader de la mer
    public float SeaRoughBase = 0.055f, SeaRoughWind = 0.15f, SeaSkyBlur = 0.6f;
    public float SeaRideGain = 1f, SeaCapGain = 1f, SeaFoamGain = 1f;

    // [navire]
    /// <summary>Le pavillon hissé sur le navire à la barre : l'id d'une nation de flags.json, vide pour celui de la fiche.</summary>
    public string Nation = "";

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
        s.Dof = (bool)cf.GetValue("rendu", "profondeur_de_champ", s.Dof);
        s.DofDistance = (float)cf.GetValue("rendu", "profondeur_de_champ_distance", s.DofDistance);
        s.DofNear = (float)cf.GetValue("rendu", "profondeur_de_champ_net_a_partir_de", s.DofNear);
        s.DofFade = (float)cf.GetValue("rendu", "profondeur_de_champ_fondu_part", s.DofFade);
        s.DofAmount = (float)cf.GetValue("rendu", "profondeur_de_champ_intensite", s.DofAmount);
        s.DofQuality = (int)cf.GetValue("rendu", "profondeur_de_champ_qualite", s.DofQuality);
        s.DofAnamorphic = (bool)cf.GetValue("rendu", "profondeur_de_champ_anamorphique", s.DofAnamorphic);
        s.MotionBlur = (bool)cf.GetValue("rendu", "flou_de_mouvement", s.MotionBlur);
        s.Shutter = (float)cf.GetValue("rendu", "flou_de_mouvement_obturateur", s.Shutter);
        s.Msaa = (int)cf.GetValue("rendu", "anticrenelage_msaa", s.Msaa);
        s.ScreenAA = (string)cf.GetValue("rendu", "anticrenelage_image", s.ScreenAA);
        s.Glow = (bool)cf.GetValue("rendu", "lueur", s.Glow);
        s.GlowStrength = (float)cf.GetValue("rendu", "lueur_intensite", s.GlowStrength);
        s.AutoExposure = (bool)cf.GetValue("rendu", "exposition_auto", s.AutoExposure);
        s.AutoExposureThreshold = (float)cf.GetValue("rendu", "exposition_auto_seuil", s.AutoExposureThreshold);
        s.AutoExposureSpeed = (float)cf.GetValue("rendu", "exposition_auto_vitesse", s.AutoExposureSpeed);
        s.FilmMask = (bool)cf.GetValue("rendu", "masque_cinema", s.FilmMask);
        s.Nation = (string)cf.GetValue("navire", "pavillon", s.Nation);
        s.SeaRoughBase = (float)cf.GetValue("mer", "rugosite_base", s.SeaRoughBase);
        s.SeaRoughWind = (float)cf.GetValue("mer", "rugosite_vent", s.SeaRoughWind);
        s.SeaSkyBlur = (float)cf.GetValue("mer", "flou_du_reflet", s.SeaSkyBlur);
        s.SeaRideGain = (float)cf.GetValue("mer", "rides", s.SeaRideGain);
        s.SeaCapGain = (float)cf.GetValue("mer", "moutons", s.SeaCapGain);
        s.SeaFoamGain = (float)cf.GetValue("mer", "ecume", s.SeaFoamGain);
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
        cf.SetValue("rendu", "profondeur_de_champ", Dof);
        cf.SetValue("rendu", "profondeur_de_champ_distance", DofDistance);
        cf.SetValue("rendu", "profondeur_de_champ_net_a_partir_de", DofNear);
        cf.SetValue("rendu", "profondeur_de_champ_fondu_part", DofFade);
        cf.SetValue("rendu", "profondeur_de_champ_intensite", DofAmount);
        cf.SetValue("rendu", "profondeur_de_champ_qualite", DofQuality);
        cf.SetValue("rendu", "profondeur_de_champ_anamorphique", DofAnamorphic);
        cf.SetValue("rendu", "flou_de_mouvement", MotionBlur);
        cf.SetValue("rendu", "flou_de_mouvement_obturateur", Shutter);
        cf.SetValue("rendu", "anticrenelage_msaa", Msaa);
        cf.SetValue("rendu", "anticrenelage_image", ScreenAA);
        cf.SetValue("rendu", "lueur", Glow);
        cf.SetValue("rendu", "lueur_intensite", GlowStrength);
        cf.SetValue("rendu", "exposition_auto", AutoExposure);
        cf.SetValue("rendu", "exposition_auto_seuil", AutoExposureThreshold);
        cf.SetValue("rendu", "exposition_auto_vitesse", AutoExposureSpeed);
        cf.SetValue("rendu", "masque_cinema", FilmMask);
        cf.SetValue("navire", "pavillon", Nation);
        cf.SetValue("mer", "rugosite_base", SeaRoughBase);
        cf.SetValue("mer", "rugosite_vent", SeaRoughWind);
        cf.SetValue("mer", "flou_du_reflet", SeaSkyBlur);
        cf.SetValue("mer", "rides", SeaRideGain);
        cf.SetValue("mer", "moutons", SeaCapGain);
        cf.SetValue("mer", "ecume", SeaFoamGain);
        cf.SetValue("performance", "solveurs_paralleles", ParallelSolvers);
        Error e = cf.Save(Path);
        if (e != Error.Ok) GD.PushWarning($"réglages non enregistrés dans {Path} : {e}");
    }
}
