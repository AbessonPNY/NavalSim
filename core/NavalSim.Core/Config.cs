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
    /// <summary>Le mille marin : une minute de latitude, telle que <see cref="Geo"/> la compte.</summary>
    public const double Mile = Geo.MPerMin;

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
    /// Huit est où la mesure a mis le plafond côté JavaScript (la page le garde).
    /// Sous Godot, mesuré avant de le relever à seize (journal, « Seize coques ») :
    /// 2,75 ms par image pour une frégate, 8,6 pour huit, 16,8 pour seize — la
    /// carte graphique n'en prend que 5,8 ms, la mer ne boucle que sur les coques
    /// présentes ; c'est le processeur qui paie, à peu près une milliseconde par
    /// coque. Relevé aussi dans hull_gap.gdshaderinc (NSHIP) et motion_blur.glsl.
    /// </summary>
    public const int MaxShips = 16;

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
    /// LE DEGRÉ DE RÉALISME DU VENT : le facteur dont on multiplie ce que le vent
    /// pousse dans les voiles VERS L'AVANT. 1 est la physique ; 2 double la poussée,
    /// carré comme latine — la poussée en travers, elle, reste celle du vent réel,
    /// sans quoi le navire dérivait en crabe (ShipPhysics.Boost).
    ///
    /// UNE FORCE, PAS UNE VITESSE : la traînée de coque croît comme le carré de
    /// la vitesse, donc doubler la poussée ne double PAS l'erre — elle monte d'un
    /// facteur √2 environ. Pour doubler la vitesse il faudrait le quadruple ; on
    /// laisse le joueur choisir, et le banc (« vent ») mesure ce que chaque
    /// facteur rend réellement.
    ///
    /// Un réglage de JEU et non une constante du monde : il se lit dans
    /// settings.json (« wind.gain ») et reste à 1 partout ailleurs — le banc de
    /// parité compare donc toujours la même physique que la page.
    /// </summary>
    public static double WindGain = 1.0;

    /// <summary>
    /// CE QUE LE VENT FAIT GÎTER, à part de ce qu'il POUSSE. Le facteur par lequel
    /// on multiplie le COUPLE des voiles — gîte et moment d'embardée — et non leur
    /// force : 1 laisse la coque gîter comme sous le vent réel quel que soit
    /// <see cref="WindGain"/>, qui n'agit alors que sur la marche. Mettre ici la
    /// même valeur que WindGain rend la voilure « plus grande » en tout point.
    ///
    /// Un écart assumé avec la physique : une vraie voile ne pousse pas plus sans
    /// coucher davantage. C'est le prix d'un réglage de confort, et il se règle.
    /// </summary>
    public static double WindHeel = 1.0;

    /// <summary>
    /// LE VIREMENT DE BORD DE L'ÉQUIPAGE (ShipPhysics.Tack.cs) : l'artimon bordé au
    /// vent, puis la misaine à contre. Éteint dans le noyau — la page n'en sait rien,
    /// et le banc de parité la compare —, allumé par Godot (settings.json → wind.virement).
    /// </summary>
    public static bool CrewTacks = false;

    /// <summary>
    /// LES RÉCIFS DE LA FICHE dans le relief (World.HeightAt) et sous la coque. Éteints
    /// dans le noyau — la page ne les connaît pas, et le banc de parité compare des
    /// fonds —, allumés par Godot et par les bancs qui en parlent.
    /// </summary>
    public static bool Reefs = false;

    /// <summary>
    /// LA BARRE AUTOMATIQUE SELON L'ERRE (AutoHelm) : moins de barre quand le navire
    /// court. Éteinte dans le noyau — la page n'en sait rien et le banc de parité
    /// compare les deux barres —, allumée par Godot, où le gain de vent fait filer
    /// un sloop à quatorze nœuds et où la barre de la page s'emballait.
    /// </summary>
    public static bool HelmBySpeed = false;

    /// <summary>
    /// LA BARRE AUTOMATIQUE SONDE (AutoHelm.Sound) : elle dévie devant un haut-fond, un
    /// récif ou un écueil au lieu d'y courir. Éteinte dans le noyau, allumée par Godot.
    /// </summary>
    public static bool HelmSounds = false;

    /// <summary>
    /// LA GODILLE AU SAFRAN (ShipPhysics, « LA GODILLE ») : une lame qu'on balaie
    /// pousse l'eau même navire arrêté. Éteinte dans le noyau — la page n'en sait
    /// rien, le banc de parité compare les coques —, allumée par Godot.
    /// </summary>
    public static bool RudderScull = false;

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
