namespace NavalSim.Core;

/// <summary>Une composante du spectre, telle que les trois calculateurs la lisent.</summary>
public struct Wave
{
    public double Dx, Dz;     // direction de propagation, unitaire
    public double Amp;        // amplitude, en metres
    public double K;          // nombre d'onde, rad/m
    public double Omega;      // pulsation, rad/s
    public double Q;          // raideur de Gerstner, le terme horizontal
    public double L;          // longueur d'onde, en metres
    public int Band;          // sa bande spectrale -- PAS son rang, voir SetSeaState
    public double Phase;      // ce que l'origine flottante lui doit
}

/// <summary>
/// La bande spectrale, et la correction qu'elle porte.
///
/// Gardee par BANDE et non par entree de <c>Waves</c>, qui est trie par energie
/// et se reordonne des que le vent change : une correction rangee par rang
/// suivrait le tri au lieu de suivre la vague.
/// </summary>
public struct Band
{
    public double K, Dx, Dz, Omega, Corr;
}

/// <summary>
/// La mer, en arithmetique pure : le spectre, la phase, l'origine flottante et
/// l'echantillonneur que la grille de sondes lit. Aucun rendu, aucun Godot.
///
/// TROIS CALCULATEURS DOIVENT LIRE LA MEME HOULE -- le vertex shader de la mer,
/// cet echantillonneur, et la passe d'ecume. En oublier un les desynchronise
/// SILENCIEUSEMENT : la coque flotte alors sur une mer que l'oeil ne voit pas.
/// C'est pourquoi le profil de Gerstner est ecrit une seule fois cote GPU, dans
/// <c>shaders/gerstner.gdshaderinc</c>, inclus par les deux shaders -- et
/// pourquoi ce fichier-ci porte, en tete de <see cref="Sample"/>, le rappel de
/// ce qu'il doit repliquer au bit pres.
/// </summary>
public sealed class Ocean
{
    public readonly Wave[] Waves = new Wave[Config.NWaves];
    readonly Band[] _band = new Band[Config.NWaves];
    readonly (double Amp, double W, double Dir)[] _raw = new (double, double, double)[Config.NWaves];

    /// <summary>
    /// OU SE TROUVE REELLEMENT LE ZERO LOCAL.
    ///
    /// Tout est calcule pres de zero et ceci retient ou ce zero se trouve dans le
    /// monde, de sorte qu'un batiment a mille kilometres est encore dessine a des
    /// coordonnees de quelques centaines de metres. Sans cela la mer meurt bien
    /// avant : la phase de Gerstner vaut k.x, et avec k jusqu'a 3 rad/m une
    /// position de quelques kilometres consomme deja la quasi-totalite des sept
    /// chiffres d'un flottant 32 bits. Les vagues ne tremblent pas, elles
    /// DISPARAISSENT.
    /// </summary>
    public Vec3d Origin;

    /// <summary>Le creux : multiplie la hauteur significative au-dela de la table Beaufort.</summary>
    public double Swell = 1.35;

    /// <summary>L'exposant d'aiguisage des cretes. 1 = sinusoide pure.</summary>
    public double Sharp = 1.0;

    public double SeaState { get; private set; }
    public double WindDeg { get; private set; }
    public double WindSpeed { get; private set; }
    public Vec3d WindVec { get; private set; }

    /// <summary>L'horloge de la houle, en secondes. Avancee par l'appelant.</summary>
    public double Time;

    /// <summary>Hauteur de reference pour la lueur de crete, a l'echelle de la mer.</summary>
    public double AmpMax { get; private set; } = 0.05;

    /// <summary>
    /// L'abri du havre, en metres MONDE. Optionnel : sans havre, la mer est
    /// partout la mer. C'est le troisieme lecteur de <c>SHELTER</c>, et l'oublier
    /// ferait flotter la coque sur une mer que l'oeil ne voit pas.
    /// </summary>
    public Func<double, double, double>? Shelter;

    /// <summary>
    /// Combien de composantes le solveur integre. Pas les plus LONGUES mais les
    /// plus ENERGETIQUES : en tempete les plus longues atteignent des centaines
    /// de metres et soulevent le navire en bloc sans le travailler. C'est la
    /// bande autour du pic qui compte, et le tri par amplitude la met en tete.
    /// </summary>
    public int CpuWaveCount { get; private set; }

    /// <param name="seaState">le nombre de Beaufort</param>
    /// <param name="windDeg">le relevement d'ou le vent souffle, comme un marin l'enonce</param>
    public void SetSeaState(double seaState, double windDeg)
    {
        double wr = windDeg * Math.PI / 180.0;
        double s = Math.Max(0, seaState);
        // retenus pour qu'un appelant qui doit l'aplatir puisse la remettre exactement
        SeaState = seaState; WindDeg = windDeg;

        // le vent qui va avec cette mer ; un appelant qui veut une risee que la
        // mer n'a pas rattrapee le dit apres, par SetWind
        SetWind(seaState, windDeg);

        /* --- LE SPECTRE ---
           Une vraie mer du vent suit un spectre mesure. JONSWAP est le standard :
           une forme de Pierson-Moskowitz aiguisee par un facteur de pic gamma,
           une mer limitee par le fetch concentrant plus d'energie pres du pic
           qu'un ocean completement developpe.

           JONSWAP fixe la FORME -- quelles frequences portent l'energie et
           comment elles s'etalent. La table Beaufort fixe l'ECHELLE, de sorte
           que la hauteur significative annoncee est celle qu'on obtient. */
        double U = Math.Max(0.6, WindSpeed);

        /* Frequence de pic. Pierson-Moskowitz suppose un ocean completement
           developpe, ce qui met le pic beaucoup trop bas -- cela produisait des
           houles de 800 m en tempete, plus longues que toute mer reelle et bien
           trop longues pour travailler un navire. Les mers cotieres sont limitees
           par le fetch, donc le pic est plus haut : cette correction pose la
           periode de pic vers 5 s par belle brise et 11 s en tempete. */
        double wp = (0.855 * Config.G / U) * (1.05 + 0.015 * U);
        const double gamma = 3.3, alpha = 0.0081;

        int N = Config.NWaves;
        double wLo = wp * 0.62, wHi = wp * 3.4;   // la ou l'energie vit reellement
        double m0 = 0;

        for (int i = 0; i < N; i++)
        {
            // espacement geometrique : les basses frequences meritent la resolution
            double f0 = Math.Pow(wHi / wLo, (double)i / N);
            double f1 = Math.Pow(wHi / wLo, (double)(i + 1) / N);
            double w0 = wLo * f0, w1 = wLo * f1;
            double w = 0.5 * (w0 + w1), dw = w1 - w0;

            double sig = w <= wp ? 0.07 : 0.09;
            double r = Math.Exp(-Math.Pow(w - wp, 2) / (2 * sig * sig * wp * wp));
            double S = (alpha * Config.G * Config.G / Math.Pow(w, 5))
                     * Math.Exp(-1.25 * Math.Pow(wp / w, 4)) * Math.Pow(gamma, r);

            double amp = Math.Sqrt(Math.Max(0, 2 * S * dw));
            m0 += 0.5 * amp * amp;

            /* Etalement directionnel : les vagues courtes s'eventent bien plus que
               la houle longue, ce qui est pourquoi une vraie mer parait confuse de
               pres et ordonnee a l'horizon. Ecarts DETERMINISTES, pour que le CPU
               et le GPU ne puissent jamais differer. */
            double spread = (0.16 + 0.55 * Math.Min(1, w / wp - 0.4)) * (0.34 + 0.045 * s);
            double u = (double)((i * 7) % N) / (N - 1) * 2 - 1;
            _raw[i] = (amp, w, wr + spread * u);
        }

        // caler l'echelle sur la hauteur significative que cet etat annonce
        int lo = Math.Max(0, Math.Min(9, (int)Math.Floor(s)));
        int hi = Math.Max(0, Math.Min(9, (int)Math.Ceiling(s)));
        /* Le creux multiplie la hauteur significative au-dela de la table. Il
           existe parce qu'un spectre etale sur dix-huit composantes et un
           eventail de directions parait plus plat que six harmoniques alignees,
           a hauteur egale : les cretes ne se superposent plus. Au-dela de ~1,6
           un navire peut reellement chavirer, ce qui est voulu. */
        double hsTarget = (Config.Beaufort[lo].Hs
                        + (Config.Beaufort[hi].Hs - Config.Beaufort[lo].Hs) * (s - lo)) * Swell;
        double hsRaw = 4 * Math.Sqrt(Math.Max(m0, 1e-9));
        double scale = hsRaw > 1e-6 ? hsTarget / hsRaw : 0;

        /* Budget de raideur. Il doit monter avec le creux autant qu'avec l'etat de
           mer : une vague plus grosse a raideur egale n'est qu'une bosse ronde
           plus grosse, ce qui est exactement a quoi ressemblaient les creux. */
        double chop = (0.35 + s * 0.075) * (0.75 + 0.42 * Swell);

        /* AIGUISAGE DES CRETES. Gerstner seul ne peut pas le porter : son
           cuspage vient du terme horizontal Q, et a dix-huit composantes le
           budget se divise si loin que chacune reste quasi sinusoidale. Le profil
           est donc pique directement -- sign(s).|s|^p. La symetrie IMPAIRE fait
           que la moyenne reste exactement nulle, ce qui compte : le moindre
           decalage continu ici deplacerait silencieusement le niveau moyen de la
           mer, et la flottaison de toute la flotte avec. */
        Sharp = 1 + Math.Min(0.95, (0.06 + 0.055 * s) * (0.55 + 0.62 * Swell));

        /* --- GARDER LA MER CONTINUE PENDANT QUE LE VENT CHANGE ---

           La console etait seule a appeler ceci, et une console sert quelques fois
           par minute. La meteo automatique appelle plusieurs fois par SECONDE, et
           cela transforme un detail en tout le probleme.

           La phase d'une composante vaut k.(d.r) - omega.t + correction.
           Reconstruisez le spectre avec un omega legerement different et le terme
           omega.t saute de delta-omega fois t -- or t est l'horloge courante, des
           milliers de secondes. Un milliemme de radian par seconde de derive de
           frequence fait un radian entier de saut. De meme pour la direction :
           k.(d.origine) est evalue contre une origine qui peut etre a des
           centaines de kilometres, si bien qu'un centieme de degre deplace la
           phase au navire d'une bonne fraction de longueur d'onde.

           Ni l'un ni l'autre n'est une erreur d'arrondi avec laquelle on vit :
           ensemble elles rebrassent la mer a chaque rafale, ce qui se lit comme
           un bouillonnement. Les deux sont absorbees EXACTEMENT, et pour la
           raison qui fait marcher l'origine flottante : ce qui change est une
           phase, et une phase ne compte que modulo 2pi. La correction est posee
           pour que le total soit inchange A L'ORIGINE LOCALE, la ou est la flotte
           et donc la ou la continuite vaut d'etre tenue ; loin de la, les deux
           spectres se separent lentement, comme ils le doivent, deux spectres
           differents etant reellement deux mers differentes. */
        const double TAU = Math.PI * 2;
        double ox = Origin.X, oz = Origin.Z, tNow = Time;

        double ampSum = 0;
        for (int i = 0; i < N; i++)
        {
            var raw = _raw[i];

            // l'aiguisage abaisse la valeur efficace du profil ; on la rend, pour
            // que la hauteur significative annoncee tienne encore
            double amp = raw.Amp * scale * (1 + 0.42 * (Sharp - 1));
            double k = raw.W * raw.W / Config.G;        // dispersion en eau profonde, k = omega2/g
            // raideur normalisee pour que la crete ne boucle jamais (somme Q.k.A < 1)
            double Q = Math.Min(0.85, chop / (k * amp * N + 1e-4));
            // les mers courent AVEC le vent, donc a l'oppose du relevement d'ou il souffle
            double dx = -Math.Sin(raw.Dir), dz = -Math.Cos(raw.Dir);

            ref Band B = ref _band[i];
            if (B.K > 0)
                B.Corr = (B.Corr
                        + (B.K * (B.Dx * ox + B.Dz * oz) - k * (dx * ox + dz * oz))
                        + (raw.W - B.Omega) * tNow) % TAU;
            B.K = k; B.Dx = dx; B.Dz = dz; B.Omega = raw.W;

            Waves[i] = new Wave
            {
                Dx = dx, Dz = dz, Amp = amp, K = k,
                Omega = raw.W, Q = Q, L = 2 * Math.PI / k, Band = i
            };
            ampSum += amp;
        }

        /* Trier par ENERGIE, et le solveur comme la passe d'ecume prennent la tete
           de cette liste. Prendre les plus longues etait une erreur : en tempete
           elles courent a des centaines de metres, et une houle bien plus longue
           que le navire le souleve en bloc sans le travailler.

           LE DEPARTAGE PAR BANDE N'EST PAS UNE COQUETTERIE, et le banc de parite
           l'a trouve. `Array.Sort` de .NET est un tri INSTABLE, la ou celui de
           JavaScript est stable depuis ES2019 : a amplitudes egales les deux
           rendaient des ordres differents. A mer calme c'est sans consequence,
           toutes les amplitudes etant nulles -- mais l'ordre decide QUELLES
           composantes le solveur integre et dans quel emplacement d'uniforme le
           shader les recoit. Deux composantes de meme amplitude a mer formee
           feraient donc diverger le CPU du GPU, silencieusement, ce que la regle
           des ecarts deterministes existe precisement pour empecher.

           Departager par bande croissante rend le tri TOTAL, donc independant de
           son implementation -- et redonne exactement l'ordre stable du JS,
           puisque les composantes y entrent dans l'ordre des bandes. */
        Array.Sort(Waves, static (a, b) =>
        {
            int byEnergy = b.Amp.CompareTo(a.Amp);
            return byEnergy != 0 ? byEnergy : a.Band.CompareTo(b.Band);
        });
        CpuWaveCount = Math.Min(Config.NWavesCpu, Waves.Length);

        AmpMax = Math.Max(0.05, ampSum * 0.8);
        SyncPhase();
    }

    /// <summary>
    /// LE VENT SEUL, laissant le spectre ou il est.
    ///
    /// Pour une main sur la console les deux sont la meme chose et
    /// <see cref="SetSeaState"/> regle les deux. Sous une meteo qui tourne seule,
    /// NON : une risee se sent dans la toile a l'instant ou elle arrive, alors que
    /// la mer qu'elle leve met des minutes a se former et des minutes a se
    /// coucher. Les separer n'est pas une licence, c'est le recit honnete.
    /// </summary>
    public void SetWind(double force, double windDeg)
    {
        double wr = windDeg * Math.PI / 180.0;
        double s = Math.Max(0, force);
        // le vent vrai, tire du nombre de Beaufort : v = 0,836.B^1,5
        WindSpeed = 0.836 * Math.Pow(s, 1.5) + 0.8;
        WindVec = new Vec3d(Math.Sin(wr) * WindSpeed, 0, -Math.Cos(wr) * WindSpeed);
        WindDeg = windDeg;
    }

    /// <summary>
    /// Rend a chaque composante la phase que le decalage d'origine represente.
    ///
    /// Decaler l'origine ferait glisser toute la mer de cote, SAUF si on rend
    /// cette phase, k.(d.origine). Or elle croit sans borne et ramenerait le
    /// probleme de precision -- sauf qu'une phase ne compte que MODULO 2PI.
    /// Reduite ici, en doubles, elle reste un petit nombre que le shader tient
    /// exactement. Mesure cote JavaScript : saut NUL a chaque recentrage, jusqu'a
    /// une origine de 2 300 km.
    /// </summary>
    public void SyncPhase()
    {
        const double TAU = Math.PI * 2;
        for (int i = 0; i < Waves.Length; i++)
        {
            ref Wave w = ref Waves[i];
            double corr = (w.Band >= 0 && w.Band < _band.Length) ? _band[w.Band].Corr : 0;
            w.Phase = (w.K * (w.Dx * Origin.X + w.Dz * Origin.Z) + corr) % TAU;
        }
    }

    /// <summary>
    /// Fait glisser le monde sous la flotte.
    ///
    /// TOUT CE QUI TIENT UNE POSITION doit se decaler dans la MEME image : les
    /// coques, le champ d'ecume (ses DEUX ancres), les cameras -- la camera fixe
    /// surtout, seule chose deliberement immobile, donc seule a se retrouver a
    /// mille metres de la. En oublier un le met en desaccord avec la mer d'exactement
    /// ce decalage.
    /// </summary>
    public void Rebase(double dx, double dz)
    {
        Origin.X += dx;
        Origin.Z += dz;
        SyncPhase();
    }

    /// <summary>
    /// La hauteur de la surface au point monde (x, z), et sa normale si demandee.
    ///
    /// LE TROISIEME CALCULATEUR, et celui qui compte le plus : c'est cette
    /// hauteur-la que la grille de sondes lit. Son jumeau GPU est la boucle de
    /// <c>gerstner.gdshaderinc</c>, et les deux doivent rester d'accord au bit
    /// pres -- un desaccord ne leve rien, il fait flotter la coque sur une mer que
    /// l'oeil ne voit pas.
    ///
    /// Seules les composantes ENERGETIQUES sont integrees ici. Un spectre JONSWAP
    /// est fortement pique, donc elles portent la quasi-totalite de l'energie ; et
    /// une ride de deux metres ne souleve pas une coque de cent tonnes -- elle se
    /// brise dessus et se moyenne sur sa longueur. La houle sur laquelle elle
    /// s'assoit visiblement est celle que le solveur sent, ce qui est la part de
    /// l'invariant qui compte.
    ///
    /// L'abri se prend en metres MONDE, puisque c'est la que le havre est defini.
    /// </summary>
    public double Sample(double x, double z, double t, out Vec3d normal)
    {
        double y = 0, nx = 0, nz = 0, ny = 0;
        double sh = Shelter != null ? Shelter(x + Origin.X, z + Origin.Z) : 1.0;

        int n = CpuWaveCount > 0 ? CpuWaveCount : Waves.Length;
        for (int i = 0; i < n; i++)
        {
            ref Wave w = ref Waves[i];
            double f = w.K * (w.Dx * x + w.Dz * z) - w.Omega * t + w.Phase;
            double c = Math.Cos(f), sn = Math.Sin(f);
            // le meme profil aiguise que le vertex shader dessine
            double as_ = Math.Abs(sn);
            double sp = Math.Pow(Math.Max(as_, 1e-4), Sharp - 1);
            double amp = w.Amp * sh;
            y += amp * Math.Sign(sn) * as_ * sp;
            double WA = w.K * amp * Sharp * sp;
            nx -= w.Dx * WA * c;
            nz -= w.Dz * WA * c;
            ny -= w.Q * WA * sn;
        }

        normal = new Vec3d(nx, 1.0 - ny, nz).Normalized();
        return y;
    }

    /// <summary>La hauteur seule, sans construire de normale.</summary>
    public double Sample(double x, double z, double t) => Sample(x, z, t, out _);
}
