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

    public Sky() { SetSun(45, 135); }

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
           propre bâtiment. Le soleil tient lieu de lune : faible, bleu, et jamais
           tout à fait parti. */
        double night = Math.Max(0, Math.Min(1, -elevDeg / 10));
        Night = night;

        SunColor = new Rgb(
            1.0 - 0.55 * night,
            (0.72 + 0.23 * t) - 0.30 * night,
            (0.45 + 0.42 * t) + 0.28 * night);

        _sunBase = (1.1 + 1.0 * t) * (1 - night) + 0.28 * night;
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
    }

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
}
