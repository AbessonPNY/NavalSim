namespace NavalSim.Core;

/// <summary>Une couleur linéaire, en arithmétique pure — le noyau ne connaît pas
/// le <c>Color</c> de Godot, qui est en flottant 32 bits comme le reste.</summary>
public struct Rgb
{
    public double R, G, B;
    public Rgb(double r, double g, double b) { R = r; G = g; B = b; }
    public readonly override string ToString() => $"({R:F3}, {G:F3}, {B:F3})";
}

/// <summary>
/// LE CIEL COMME LUMIÈRE, en arithmétique pure : où est le soleil, de quelle
/// couleur, et ce que le gros temps fait aux trois.
///
/// Rien ici ne dessine. Le dessin vit dans <c>shaders/sky.gdshaderinc</c>, qui
/// est la SEULE définition du ciel côté GPU — le dôme et le reflet de la mer
/// l'appellent tous les deux, car si l'eau miroitait un autre ciel que celui du
/// dessus, l'horizon montrerait une couture et l'image entière se lirait comme
/// fausse.
/// </summary>
public sealed class Sky
{
    // ---- ce qu'on lui donne ----
    public double SunElevDeg { get; private set; }
    public double SunBearingDeg { get; private set; }
    /// <summary>La déclinaison, c'est-à-dire la saison. 12° = fin de printemps.</summary>
    public double Declination = 12;
    /// <summary>L'heure, de 0 à 24.</summary>
    public double DayTime { get; private set; } = 12;

    // ---- ce qu'il en sort ----
    public Vec3d SunDir { get; private set; } = new(0, 1, 0);
    public Rgb SunColor { get; private set; }
    public Rgb Horizon { get; private set; }
    public Rgb Zenith { get; private set; }
    public double SunIntensity { get; private set; }
    public double HemiIntensity { get; private set; }
    /// <summary>Combien il fait nuit, de 0 à 1.</summary>
    public double Night { get; private set; }

    /// <summary>Le gros temps, de 0 à 1. Écrit par <see cref="SetSeaState"/>.</summary>
    public double Storm { get; private set; }

    /// <summary>L'éclair proche : il éclaire tout le ciel et tout le pont.</summary>
    public double Flash;

    // ---- la brume ----
    public double Haze { get; private set; } = 0.00085;
    public double HazeHeight { get; private set; } = 150.0;

    double _sunBase, _hemiBase;

    // ---- la nuit : la lune, et la lumière de l'eau ----
    /// <summary>La latitude, pour le pôle autour duquel tourne la lune.</summary>
    public double Latitude = 13.6;
    /// <summary>La chance qu'une nuit ait une lune (Naval.MOON.chance).</summary>
    public double MoonChance = 0.6;
    public bool MoonOn { get; private set; }
    /// <summary>La part éclairée, du fin croissant (0,2) à la pleine (1).</summary>
    public double MoonPhase { get; private set; } = 1;
    /// <summary>Son avance ou son retard sur l'opposé du soleil, en heures.</summary>
    public double MoonLag { get; private set; }
    /// <summary>Combien elle est levée, de 0 sous l'horizon à 1 bien au-dessus.</summary>
    public double MoonUp { get; private set; }
    public Vec3d MoonDir { get; private set; } = new(0, 1, 0);
    /// <summary>Ce qui arrive de lumière de lune, de 0 à 1.</summary>
    public double MoonLight { get; private set; }
    /// <summary>D'où vient la lumière directe : le soleil, ou la lune la nuit.</summary>
    public Vec3d LightDir { get; private set; } = new(0, 1, 0);
    /// <summary>La lumière qui ressort de l'eau : 1 le jour, 0,07 à 0,23 la nuit.</summary>
    public double WaterLight { get; private set; } = 1;
    /// <summary>La route de lune sur l'eau : sa lumière, la nuit, sous un ciel ouvert.</summary>
    public double SeaMoonLit => MoonLight * Night * (1 - Storm);
    /// <summary>Le disque au dôme : levée et de nuit (le couvercle l'efface dans le shader).</summary>
    public double DomeMoonLit => (MoonOn ? MoonUp : 0) * Night;
    /// <summary>La phase comme le shader du dôme la lit : 1 nouvelle, −1 pleine.</summary>
    public double MoonPhaseU => 1 - 2 * MoonPhase;

    bool _wasNight;
    readonly Random _rng = new();

    /// <summary>
    /// La lune de cette nuit : s'il y en a une, sa part éclairée, et — plus elle
    /// est mince — de combien elle s'écarte de l'opposé du soleil : un croissant
    /// suit le soleil qui descend, une pleine se lève quand il se couche.
    /// </summary>
    public void NewMoon()
    {
        MoonOn = _rng.NextDouble() < MoonChance;
        MoonPhase = 0.2 + 0.8 * _rng.NextDouble();
        MoonLag = (_rng.NextDouble() < 0.5 ? -1 : 1) * (1 - MoonPhase) * 9;
    }

    /// <summary>
    /// Elle tourne avec le ciel — autour du pôle, comme tout ce qui est là-haut —,
    /// donc elle se lève, passe et se couche dans la nuit.
    /// </summary>
    void PlaceMoon()
    {
        double phi = Latitude * Math.PI / 180;
        // le pôle céleste : plein nord (+z), à la hauteur de la latitude
        var k = new Vec3d(0, Math.Sin(phi), Math.Cos(phi));
        var v = -SunDir;
        double a = MoonLag * Math.PI / 12, c = Math.Cos(a), s = Math.Sin(a);
        // Rodrigues : v·cos + (k×v)·sin + k·(k·v)·(1 − cos)
        var kxv = new Vec3d(k.Y * v.Z - k.Z * v.Y, k.Z * v.X - k.X * v.Z, k.X * v.Y - k.Y * v.X);
        double kv = k.X * v.X + k.Y * v.Y + k.Z * v.Z;
        MoonDir = (v * c + kxv * s + k * (kv * (1 - c))).Normalized();
        MoonUp = Math.Max(0, Math.Min(1, (MoonDir.Y + 0.02) / 0.12));
    }

    public Sky() { SetSun(45, 135); }

    /* UNE LUMIÈRE QUI NE MONTE PAS DE LA MER. L'astre choisi peut être sous
       l'horizon — la lune qui se lève compte dès −1°, le soleil au crépuscule, et
       sans lune la lueur du ciel prenait la direction d'un soleil à −60° — et une
       lumière directionnelle venue d'en dessous éclairait les coques par la
       quille, à travers l'eau. Elle vient donc au plus bas d'un rien au-dessus de
       l'horizon, du côté de l'astre ; et ce qui n'est plus que la lueur du ciel,
       à mesure que la nuit se fait sans lune, tombe d'en haut. */
    static Vec3d AboveSea(Vec3d d, double toZenith)
    {
        const double MinY = 0.05;
        double h = Math.Sqrt(d.X * d.X + d.Z * d.Z);
        double k = Math.Sqrt(1 - MinY * MinY) / Math.Max(h, 1e-9);
        var v = d.Y >= MinY || h < 1e-9 ? d : new Vec3d(d.X * k, MinY, d.Z * k);
        if (toZenith <= 0) return v;
        return (v * (1 - toZenith) + new Vec3d(0, toZenith, 0)).Normalized();
    }

    /// <summary>
    /// Élévation et relèvement, en degrés. Un soleil BAS est ce qui étire son
    /// reflet en la longue route de scintillement qu'on voit sur une vraie mer ;
    /// haut sur l'horizon ce reflet se réduit à une tache à quelques mètres de la
    /// coque, et l'effet est invisible.
    /// </summary>
    public void SetSun(double elevDeg, double bearingDeg)
    {
        SunElevDeg = elevDeg;
        SunBearingDeg = bearingDeg;
        double e = elevDeg * Math.PI / 180, b = bearingDeg * Math.PI / 180;

        /* L'EST EST LE −X DU MONDE, par la conséquence qui met déjà tribord en
           −x local : un relèvement doit CROÎTRE quand elle abat sur tribord, elle
           tourne alors vers −x, donc le cap ne monte que si l'est est en −x. Le
           soleil se lève donc où la carte dit qu'est l'est. */
        SunDir = new Vec3d(-Math.Sin(b) * Math.Cos(e), Math.Sin(e), Math.Cos(b) * Math.Cos(e))
                 .Normalized();

        // un soleil bas rougit et faiblit ; la brume du ciel se réchauffe avec lui
        double t = Math.Max(0, Math.Min(1, elevDeg / 30));

        /* Sous l'horizon il fait nuit. Pas noir — une vraie mer garde de nuit un
           éclat froid pris au ciel, et il faut encore pouvoir distinguer son
           propre bâtiment. La lumière directe passe alors à la LUNE, quand il y en
           a une et qu'elle est levée ; sans elle il reste le ciel, et presque rien. */
        double night = Math.Max(0, Math.Min(1, -elevDeg / 10));
        Night = night;

        // une nouvelle nuit, une nouvelle lune — ou aucune
        if (night > 0 && !_wasNight) NewMoon();
        _wasNight = night > 0;
        PlaceMoon();
        /* Ce qui arrive de lumière de lune : levée, et aussi pleine qu'elle l'est.
           Une nouvelle lune ne donne presque rien, et c'est juste. */
        MoonLight = MoonOn ? MoonUp * (0.15 + 0.85 * MoonPhase) : 0;
        // la seule lumière directe de la scène : le soleil le jour, la lune la nuit quand elle est levée
        bool byMoon = night > 0.5 && MoonOn && MoonUp > 0.05;
        LightDir = AboveSea(byMoon ? MoonDir : SunDir, byMoon ? 0 : night);

        SunColor = new Rgb(
            1.0 - 0.55 * night,
            (0.72 + 0.23 * t) - 0.30 * night,
            (0.45 + 0.42 * t) + 0.28 * night);

        /* La nuit, la lumière directe est celle de la lune : 0,12 sous un ciel
           vide, jusqu'à 0,40 sous une pleine. Elle valait 0,28 fixe — une lune
           chaque nuit. */
        _sunBase = (1.1 + 1.0 * t) * (1 - night) + (0.12 + 0.28 * MoonLight) * night;

        /* LA LUMIÈRE QUI EST DANS L'EAU pour en ressortir : 1 le jour, un peu moins
           soleil bas, très peu la nuit, un peu plus sous une lune. La couleur de
           l'eau est de la lumière rediffusée vers le haut, et elle avait été écrite
           comme un pigment constant : à minuit, là où le reflet du ciel est au plus
           faible et où l'on ne voit que le corps de l'eau, la mer luisait turquoise
           comme éclairée par-dessous. */
        WaterLight = (0.75 + 0.25 * t) * (1 - night) + (0.07 + 0.16 * MoonLight) * night;
        SunIntensity = _sunBase * (1 - 0.62 * Storm);

        Horizon = new Rgb(
            (0.62 + 0.20 * t) * (1 - night) + 0.055 * night,
            (0.70 + 0.19 * t) * (1 - night) + 0.075 * night,
            (0.76 + 0.17 * t) * (1 - night) + 0.115 * night);

        Zenith = new Rgb(
            (0.08 + 0.04 * t) * (1 - night) + 0.012 * night,
            (0.24 + 0.12 * t) * (1 - night) + 0.020 * night,
            (0.42 + 0.10 * t) * (1 - night) + 0.045 * night);

        /* Bien rabattue, l'ambiante hémisphérique : c'est l'ENVIRONNEMENT qui
           porte désormais la lumière du ciel. À son ancienne force l'hémisphère
           comptait cette lumière une seconde fois et aplatissait justement
           l'ombrage directionnel que l'environnement apporte. Ce qui reste est
           surtout le porteur de l'éclair. */
        _hemiBase = ((0.14 + 0.14 * t) * (1 - night) + 0.04 * night) * (1 - 0.55 * Storm);
        HemiIntensity = _hemiBase + Flash * 2.6;
    }

    /// <summary>
    /// LE GROS TEMPS NE FAIT PAS DESCENDRE LE SOLEIL.
    ///
    /// C'était la première lecture et elle était fausse. Un coup de vent à midi
    /// est sombre parce que le ciel s'est FERMÉ, pas parce que le soleil s'est
    /// couché : la lumière reste où elle est dans le ciel et cesse simplement
    /// d'arriver. L'élévation n'est donc pas touchée, et ce qui change est le
    /// couvercle au-dessus, la crasse entre, et ce qui passe du soleil.
    /// </summary>
    public void SetStorm(double x)
    {
        x = Math.Max(0, Math.Min(1, x));
        if (Math.Abs(x - Storm) < 1e-4) return;
        Storm = x;
        SetSun(SunElevDeg, SunBearingDeg);   // les couleurs et l'ambiante suivent
    }

    /// <summary>
    /// LE TEMPS QU'IL FAIT DANS LE CIEL, TIRÉ DU TEMPS QU'IL FAIT SUR L'EAU.
    ///
    /// Il vient TARD et RAIDE : une belle brise est une belle journée avec une
    /// mer vive, et il n'y a aucune raison de gâcher la vue pour elle. À partir
    /// d'environ force 5, le couvercle descend.
    ///
    /// Et LA BRUME EST LIÉE À L'ÉTAT DE MER, ce qu'elle n'était délibérément pas.
    /// La règle d'origine — un coup de vent ne doit pas fermer l'horizon, puisque
    /// cela arrivait exactement quand les grosses lames devenaient intéressantes
    /// — était juste tant qu'un coup de vent était la seule météo qui existait.
    /// Elle cesse de l'être dès qu'il apporte un ciel à lui : alors la crasse ne
    /// cache plus le spectacle, elle EST le spectacle, et une tempête à travers
    /// laquelle on voit à huit kilomètres n'est pas une tempête.
    /// </summary>
    public void SetSeaState(double seaState)
    {
        SetStorm((seaState - 5.0) / 3.2);

        double st = Math.Max(0, Math.Min(1, (seaState - 5.0) / 3.2));
        Haze = 0.00085 * (1 + 6.4 * st * st);
        // et elle remplit la hauteur aussi, ou le ciel reste clair au-dessus de la crasse
        HazeHeight = 150.0 * (1 + 2.4 * st);

        /* LA BRUME DE SURFACE (SeaFog) : la même loi, une couche basse et dense.
           La densité au ras de l'eau monte vers celle de la brume, la hauteur
           descend vers la sienne — si bien qu'à pleine brume on ne voit plus à
           deux cents mètres sur l'eau, et qu'au-dessus de quinze mètres l'air est
           clair : la mâture sort, les étoiles restent. Un seul couple densité–
           hauteur, et donc tous les lecteurs de la brume la voient. */
        if (Fog > 0)
        {
            double f = Fog * Fog * (3 - 2 * Fog);
            Haze += (Math.Max(Haze, FogDensity) - Haze) * f;
            HazeHeight += (FogHeight - HazeHeight) * f;
        }
    }

    /// <summary>La brume de surface, de 0 à 1 — posée par l'appelant avant SetSeaState.</summary>
    public double Fog;
    public double FogDensity = 0.02, FogHeight = 12;

    /// <summary>
    /// OÙ SE TROUVE LE SOLEIL À UNE HEURE DONNÉE.
    ///
    /// Pas une sinusoïde déguisée en soleil. La vraie chose tient en trois lignes
    /// de trigonométrie sphérique, elle demande une latitude — que ce monde a
    /// déjà — et elle donne GRATUITEMENT tout ce qu'un arc dessiné à la main doit
    /// se faire dire : le soleil se lève à l'est, se couche à l'ouest, passe au
    /// méridien, monte plus haut en été, et reste sous l'horizon pour la bonne
    /// part de la journée.
    ///
    /// Une sinusoïde en hauteur avec un relèvement fixe fait lever et coucher le
    /// soleil au même endroit, ce dont on s'aperçoit sans pouvoir dire pourquoi.
    /// </summary>
    public void SetTimeOfDay(double hours, double latitudeDeg)
    {
        const double rad = Math.PI / 180;
        DayTime = ((hours % 24) + 24) % 24;
        Latitude = latitudeDeg;
        double phi = latitudeDeg * rad;
        double decl = Declination * rad;                   // la saison
        double H = (DayTime - 12) / 24 * 2 * Math.PI;      // angle horaire : nul à midi

        double sinAlt = Math.Sin(decl) * Math.Sin(phi)
                      + Math.Cos(decl) * Math.Cos(phi) * Math.Cos(H);
        double alt = Math.Asin(Math.Max(-1, Math.Min(1, sinAlt)));
        double cosA = (Math.Sin(decl) - Math.Sin(alt) * Math.Sin(phi))
                    / Math.Max(1e-4, Math.Cos(alt) * Math.Cos(phi));
        double A = Math.Acos(Math.Max(-1, Math.Min(1, cosA)));
        if (Math.Sin(H) > 0) A = 2 * Math.PI - A;          // l'après-midi : elle est dans l'ouest

        SetSun(alt / rad, A / rad);
    }

    /// <summary>
    /// Ce que l'éclair ajoute à l'ambiante. Appelé chaque image après avoir
    /// posé <see cref="Flash"/>, parce que le pont et la toile doivent l'attraper
    /// aussi — un éclair qui n'éclairerait que le ciel se lit comme un fond
    /// d'écran qui clignote.
    /// </summary>
    public void RefreshFlash() => HemiIntensity = _hemiBase + Flash * 2.6;

    /// <summary>
    /// La même loi que haze_along, au processeur, pour décider de ce que la brume
    /// a déjà caché — <c>Naval.hazeTransmit</c>. Elle doit rester la JUMELLE du
    /// shader : même intégrale de profondeur, mêmes paramètres, sans quoi un feu
    /// s'éteindrait pendant que le shader en montrerait encore la trace. Rend la
    /// fraction de sa propre lumière qui arrive encore.
    /// </summary>
    public double HazeTransmit(Vec3d from, Vec3d to)
    {
        double dx = to.X - from.X, dy0 = to.Y - from.Y, dz = to.Z - from.Z;
        double dist = Math.Sqrt(dx * dx + dy0 * dy0 + dz * dz);
        if (dist < 0.001) return 1;
        double H = HazeHeight;
        double y0 = Math.Max(from.Y, 0), y1 = Math.Max(to.Y, 0), dy = y1 - y0;
        double depth = Math.Abs(dy) < 0.01
            ? Math.Exp(-y0 / H) * dist
            : dist * (H / dy) * (Math.Exp(-y0 / H) - Math.Exp(-y1 / H));
        return Math.Exp(-Haze * Math.Abs(depth));
    }
}
