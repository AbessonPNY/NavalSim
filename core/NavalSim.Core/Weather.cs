namespace NavalSim.Core;

/// <summary>
/// LE VENT QUI SE CONDUIT SEUL — weather.js, porté tel quel.
///
/// Un vent réglé une fois et jamais touché est ce qu'il reste de plus artificiel
/// sur l'eau. Ce qui fait un vent vivant n'est pas le hasard, c'est la MÉMOIRE :
/// la rafale suivante ressemble à la précédente. D'où un processus
/// d'Ornstein-Uhlenbeck sur deux échelles de temps :
///   · le SYSTÈME, force et relèvement moyens, sur une dizaine de minutes — le
///     front qui passe ;
///   · le VENT autour de lui, sur une vingtaine de secondes — rafales, accalmies,
///     adonnantes et refusantes.
/// La force revient vers une moyenne de climat ; le relèvement moyen, lui, marche
/// au hasard, aucun point du compas n'étant plus naturel qu'un autre. Un vent fort
/// est plus rafaleux en absolu ; un vent faible est plus capricieux en direction.
///
/// Le tirage est INJECTÉ : le banc de parité fait tirer les deux côtés dans le
/// même générateur, et le jeu dans celui de .NET.
/// </summary>
public sealed class Weather
{
    public bool On;

    // --- le système, sur une bonne partie d'un après-midi ---
    public double SysTau = 540;       // s : le temps que le front met à changer d'avis
    public double Climate = 4.0;      // le Beaufort vers lequel il revient
    public double SysSpread = 1.7;    // Beaufort : combien il s'en écarte
    public double Veer = 1.15;        // degrés par √s de dérive du relèvement moyen

    /* --- le vent autour de lui ---
       Force et relèvement ont des constantes DIFFÉRENTES : une rafale passe en
       moins d'une minute ; une saute de vent est plus lente, et qui oscille au
       rythme des rafales se lit comme un instrument cassé. */
    public double GustTau = 22;
    public double VeerTau = 70;
    /* Le processus est la CIBLE, et le vent qui souffle la suit par un court
       retard : un chemin d'O-U est continu mais nulle part dérivable, tiré à
       chaque image il secoue voiles et instruments à trente hertz. */
    public double SmoothTau = 1.1;

    public double MeanForce, MeanDir = 45, TgtForce, TgtDir = 45, Force, Dir = 45;
    public double LoForce = 0.4, HiForce = 9.0;

    /* LA VITESSE À LAQUELLE LA MER SUIT LE VENT, et c'est une mesure : un pas
       d'un Beaufort déplace la surface de 9,8 m efficaces sur 400 m, un dixième
       rebat toute la mer. Plafonné à 0,0025 Beaufort par reconstruction, soit
       2 cm efficaces — écrit PAR SECONDE, jamais par image. */
    public double SeaRate = 0.0125;   // Beaufort par seconde
    public double VeerRate = 0.30;    // degrés par seconde

    readonly Func<double> _random;

    /// <param name="random">un tirage uniforme dans [0, 1[, comme Math.random</param>
    public Weather(Func<double>? random = null)
    {
        var r = new Random();
        _random = random ?? r.NextDouble;
        MeanForce = TgtForce = Force = Climate;
    }

    /// <summary>
    /// Pousser une mer en retard vers le vent (<paramref name="force"/>,
    /// <paramref name="dir"/>) au plus à <see cref="SeaRate"/> et
    /// <see cref="VeerRate"/>. Vrai si elle a bougé — sans quoi rien à reconstruire.
    /// </summary>
    public bool ChaseSea(ref double seaForce, ref double seaDir, double dt, double force, double dir)
    {
        double mf = SeaRate * dt, mv = VeerRate * dt;
        double df = force - seaForce;
        double dd = (dir - seaDir + 540) % 360 - 180;           // le plus court
        if (Math.Abs(df) < 2e-5 && Math.Abs(dd) < 5e-4) return false;
        seaForce += Math.Max(-mf, Math.Min(mf, df));
        seaDir += Math.Max(-mv, Math.Min(mv, dd));
        seaDir = (seaDir + 360) % 360;
        return true;
    }

    /// <summary>Adopter ce que montre la console : allumer la météo ne téléporte pas la mer.</summary>
    public void Sync(double force, double dir)
    {
        Force = TgtForce = MeanForce = force;
        Dir = TgtDir = MeanDir = dir;
    }

    // Box-Muller, un écart normal par appel : les mêmes tirages, dans le même ordre, que la page
    double Gauss()
    {
        double u = 1 - _random();
        return Math.Sqrt(-2 * Math.Log(u)) * Math.Cos(2 * Math.PI * _random());
    }

    // un pas EXACT d'Ornstein-Uhlenbeck : les mêmes statistiques quel que soit dt
    double Ou(double x, double mean, double sd, double tau, double dt)
    {
        double a = Math.Exp(-dt / tau);
        return mean + (x - mean) * a + sd * Math.Sqrt(1 - a * a) * Gauss();
    }

    public void Update(double dt)
    {
        if (!On || !(dt > 0)) return;
        dt = Math.Min(dt, 0.25);            // une image gelée ne doit pas faire embardée au temps

        // le front : la force revient, le relèvement erre
        MeanForce = Ou(MeanForce, Climate, SysSpread, SysTau, dt);
        MeanForce = Math.Max(1.0, Math.Min(8.2, MeanForce));
        MeanDir += Veer * Math.Sqrt(dt) * Gauss();

        // les rafales, dont la taille suit la force
        double gust = 0.10 + 0.085 * MeanForce;
        TgtForce = Ou(TgtForce, MeanForce, gust, GustTau, dt);
        TgtForce = Math.Max(LoForce, Math.Min(HiForce, TgtForce));

        // les sautes, qui font l'inverse : un coup de vent tient son cap, une petite brise court le compas
        double swing = 9 / (0.7 + 0.5 * MeanForce);
        TgtDir = Ou(TgtDir, MeanDir, swing, VeerTau, dt);

        // et le vent lui-même, qui suit sa cible en douceur
        double lag = 1 - Math.Exp(-dt / SmoothTau);
        Force += (TgtForce - Force) * lag;
        double dd = (TgtDir - Dir + 540) % 360 - 180;
        Dir += dd * lag;

        // tous les relèvements sur la rose, ensemble, pour que leurs écarts survivent
        while (Dir < 0) { Dir += 360; TgtDir += 360; MeanDir += 360; }
        while (Dir >= 360) { Dir -= 360; TgtDir -= 360; MeanDir -= 360; }
    }

}
