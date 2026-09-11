namespace NavalSim.Core;

/// <summary>
/// Les constantes du monde, et d'aucun navire en particulier. Tout ce qui varie
/// d'un bâtiment à l'autre vit dans ships/*.json et se lit par <see cref="ShipSpec"/>.
/// Traduction de js/config.js — les commentaires qui portent un RAISONNEMENT
/// voyagent avec le code, ceux qui décrivent JavaScript restent là-bas.
/// </summary>
public static class Config
{
    // ---- constantes physiques ----
    public const double Rho = 1025.0;      // masse volumique de l'eau de mer (kg/m³)
    public const double G = 9.81;
    public const double RhoAir = 1.225;
    public const double MsToKn = 1.94384;

    // ---- grille de sondes (cellules dans l'enveloppe de carène) ----
    public const int PnZ = 11, PnX = 7, PnY = 7;
    public const int ProbeCount = PnZ * PnX * PnY;   // 539

    /// <summary>
    /// Combien de coques la mer et l'écume portent à la fois.
    ///
    /// Pas un nombre libre : il dimensionne les tableaux d'uniformes que les deux
    /// shaders indexent, le NSHIP contre lequel ils sont compilés, et le nombre de
    /// lignes de la texture de profils de coque. L'augmenter coûte un peu de travail
    /// dans CHAQUE fragment de mer, que les coques soient là ou non.
    ///
    /// Huit est où la mesure a mis le plafond côté JavaScript. Le portage en C#
    /// rend de la marge — 0,534 ms par coque contre 0,882 — mais le chiffre reste
    /// à re-mesurer sous Godot avant d'être relevé : c'est le rendu qui décidera,
    /// et il n'a pas encore été mesuré une seule fois.
    /// </summary>
    public const int MaxShips = 8;

    /// <summary>
    /// CE QUE LA TOILE SUPPORTE, en newtons par mètre carré. Une mesure et non un
    /// réglage : relevé sur le galion pirate à pleine voilure, on ferlait à force 7
    /// et c'est très exactement là que la pression franchit deux cents.
    ///
    /// Une PRESSION et non une force, ce qui est le point : de la toile cède au
    /// newton par mètre carré. C'est pour cela que prendre un ris ne protège pas ce
    /// qui reste dehors — le ris réduit la surface exposée, donc le NOMBRE
    /// d'occasions de déchirer, jamais la tension sur ce qui porte encore.
    /// </summary>
    public const double CanvasStrength = 220.0;

    /// <summary>
    /// Jusqu'où elle s'éloigne du zéro local avant qu'on ne fasse glisser le monde
    /// sous elle. Assez petit pour que la phase de Gerstner garde sa précision,
    /// assez grand pour que le recentrage soit rare — 1500 m, cinq minutes environ
    /// à la vitesse de carène.
    /// </summary>
    public const double RebaseRadius = 1500.0;

    // ---- compartiments étanches, sur sa longueur ----
    public const int NComp = 5;

    // ---- maillage de mer ----
    // Le GPU porte le spectre entier ; le solveur et la passe d'écume ne prennent
    // que les composantes les plus ÉNERGÉTIQUES — pas les plus longues, qui en
    // tempête atteignent des centaines de mètres et soulèvent le navire en bloc
    // sans le travailler. C'est la bande autour du pic qui compte.
    public const int NWaves = 18;
    public const int NWavesCpu = 10;
    public const int NWavesFoam = 7;
    public const double OceanSize = 7000.0;
    public const int OceanSeg = 384;

    public static readonly string[] CamNames = { "Proue", "Orbite", "Passerelle", "Fixe" };

    /// <summary>Échelle de Beaufort : force, nom, hauteur significative en mètres.</summary>
    public static readonly (int Force, string Name, double Hs)[] Beaufort =
    {
        (0, "Calme", 0.0), (1, "Très légère", 0.1), (2, "Belle vaguelette", 0.3),
        (3, "Petites vagues", 0.8), (4, "Belle brise", 1.6), (5, "Vagues modérées", 2.5),
        (6, "Grosse mer", 3.5), (7, "Mer très grosse", 5.0), (8, "Coup de vent", 7.0),
        (9, "Tempête", 9.0)
    };
}
