namespace NavalSim.Core;

/// <summary>
/// Ce qu'une main sur la roue écrit, et RIEN D'AUTRE.
///
/// La barre automatique écrit dans le même objet : c'est le point, une barre
/// capable de pousser la coque tricherait, et cesserait du même coup d'être une
/// épreuve de la navigabilité du navire.
///
/// Passé à <c>Step</c> à CHAQUE image plutôt que retenu. Ce n'est pas un détail
/// de style : une barre automatique construite avec une référence capturée à
/// l'armement continuait d'écrire dans la console du joueur après un échange de
/// navires, soixante fois par seconde. L'appelant tient l'entrée, l'entrée tient
/// ses commandes du moment, et il ne reste rien qui puisse se périmer.
/// </summary>
public sealed class Controls
{
    public double Throttle;      // -1 à 1
    public double Rudder;        // -1 à 1
    public double Sheet;         // l'angle d'écoute, en radians
    public bool SailsSet = true; // voiles établies ou ferlées

    public Controls Clone() => new()
    {
        Throttle = Throttle, Rudder = Rudder, Sheet = Sheet, SailsSet = SailsSet
    };
}

/// <summary>Le corps rigide à six degrés de liberté.</summary>
public sealed class Body
{
    public Vec3d Pos;
    public Vec3d Vel;
    public Quatd Quat = Quatd.Identity;
    public Vec3d AngVel;            // en repère MONDE
    public double Mass;
    public Vec3d Com;               // centre de gravité, en repère propre
    public Vec3d Ib;                // inertie principale, en repère propre
}

/// <summary>Une cellule de la grille de sondes.</summary>
public struct Probe
{
    public Vec3d Local;
    public double Vol;
    public double Frac;     // à quel point elle est pleine, retenu d'une image à l'autre
}

/// <summary>Un compartiment étanche, découpé SUR LES SONDES.</summary>
public sealed class Compartment
{
    public double Vol;              // ce qu'il contient, en m³
    public double Cap;              // ce qu'il peut contenir : du vrai volume de coque
    public Vec3d Mid;
    public double HalfB;
    public double DeckY = double.NegativeInfinity;
    public double KeelY = double.PositiveInfinity;
}

/// <summary>Une voie d'eau.</summary>
public sealed class Breach
{
    public int Comp;
    public double Area;     // en m²
    public double Y, Z;     // où elle est, dans le repère du navire
}

/// <summary>Un colis arrimé. Du poids MORT : il n'apporte aucune carène liquide.</summary>
public sealed class Parcel
{
    public int Hold;
    public double Level, Side;
    public string Kind = "lest";
    public double Kg;
    public Vec3d At;
}

/// <summary>
/// Une amarre, ou une défense — LE MÊME RESSORT AVEC LE SIGNE RETOURNÉ.
/// Un bout tire quand il a filé son mou et ne fait rien tant qu'il ne l'a pas ;
/// une défense pousse quand on entame son épaisseur et ne fait rien sinon.
/// </summary>
public sealed class Mooring
{
    public double Lx, Ly, Lz;       // l'écubier, dans le repère du navire
    public double Wx, Wy, Wz;       // la bitte, en mètres MONDE VRAIS
    public double Len;
    public bool Push;               // vrai = défense
}

/// <summary>Ce que le solveur demande à la terre. Sans elle, elle ne touche jamais.</summary>
public interface IGround
{
    /// <summary>La hauteur du fond en mètres MONDE VRAIS. Négative au large.</summary>
    double HeightAt(double worldX, double worldZ);
}

/// <summary>
/// LE PROFIL D'AÉROFOIL DONT TOUTE VOILE EST TAILLÉE.
///
/// Ces trois nombres sont lus DEUX FOIS — pour fabriquer la force, et pour en
/// déduire le bordage optimal en forme fermée. Réécrits en dur aux deux endroits
/// ils finiraient par diverger, et le repère vert de la console désignerait un
/// réglage que les voiles ne veulent pas. Une définition, deux usagers.
/// </summary>
public static class SailFoil
{
    public const double KL = 1.5, CD0 = 0.08, KD = 1.2;
}

/// <summary>
/// Corps rigide à six degrés de liberté flottant par le principe d'Archimède.
///
/// Le volume de coque est rempli d'une grille uniforme de sondes. Chaque sonde
/// sous la surface locale apporte ρ·g·V vers le haut, à sa propre position ; la
/// somme de ces forces et de leurs moments autour du centre de gravité donne le
/// pilonnement, le tangage, le roulis et la stabilité métacentrique GRATUITEMENT
/// — rien de tout cela n'est scripté.
///
/// Par-dessus viennent la machine, le gouvernail (une force à l'étambot, jamais
/// un couple pur) et les voiles (des aérofoils dans le vent apparent).
///
/// Toutes les dimensions et tous les coefficients viennent de la fiche, donc le
/// même solveur porte une goélette de 115 t et une frégate de 1 600 sans
/// retouche.
/// </summary>
public sealed partial class ShipPhysics
{
    public ShipSpec Spec { get; }
    public HullLines Lines { get; }
    public Body Body { get; } = new();

    public Probe[] Probes = Array.Empty<Probe>();
    public double HullVolume { get; private set; }
    public double ProbeH { get; private set; }

    public Compartment[] Comps = Array.Empty<Compartment>();
    public readonly List<Breach> Breaches = new();
    public readonly List<Parcel> Cargo = new();
    public readonly List<Mooring> Moorings = new();

    /// <summary>La terre. Posée par la page ; sans elle elle ne touche jamais.</summary>
    public IGround? World;

    // ---- état d'avarie ----
    public double FloodVol, FloodTonnes, FloodRate, FreeSurfaceRise;
    public double Aground;          // mètres dont sa quille est DANS le fond
    public double Touching;         // mètres dont son bordé est DANS une autre coque
    public bool Foundered;
    public bool PumpOn = true;
    public double PumpRate;
    /// <summary>
    /// Force de l'effet de carène liquide, 1 étant la correction du manuel.
    /// Nommée plutôt qu'enfouie, parce que c'est le seul terme qui décide si
    /// elle chavire ou si elle se contente de sombrer — et la mettre à 0 est la
    /// seule façon de prouver lequel des deux l'a tuée sur un essai donné.
    /// </summary>
    public double FreeSurface = 1.0;

    // ---- chargement ----
    public double CargoTonnes, CargoCapacity;
    public int Powder, PowderMax;

    // ---- télémétrie ----
    public double SubmergedFrac, Draft, DepthBelow, Afloat = 1;
    public double SlamRate, SlamSpeed;
    public double AppWindAngle, AppWindSpeed;
    public int Tack = 1;
    public double SailDrive, SailLoad;
    public bool Luffing;
    public double? OptSheet;

    /// <summary>
    /// La part de sa toile réellement établie, de zéro à un.
    ///
    /// Elle vit ICI et non dans le modèle, et c'est tout le point : une voile
    /// qu'on rentre n'est pas une animation avec une force posée à côté, c'est
    /// MOINS DE SURFACE EN L'AIR. Portée comme une fraction, la pression
    /// aérodynamique est simplement multipliée par elle — moitié de toile,
    /// moitié de poussée — et l'image ne peut pas diverger de la physique
    /// puisqu'il n'y a qu'un seul nombre.
    /// </summary>
    public double SetFrac = 1;
    /// <summary>La part de gréement encore debout, écrite par le modèle.</summary>
    public double Standing = 1;
    /// <summary>La part de toile encore ENTIÈRE. Un mât debout dont la voile a
    /// éclaté n'est pas un mât tombé, et seul le second se répare en replantant
    /// un espar.</summary>
    public double Whole = 1;
    public double SetRate = 1 / 3.0;

    // ---- la gerbe ----
    public double SlamTrigger = 1.9;    // m/s de vitesse de passage moyenne
    public double SlamPause = 5.0;
    public Action<Vec3d, double, double>? OnSlam;

    // --- l'état interne du détecteur de gerbe ---
    int _slamWarm = 3;
    double _slamLast, _slamCool;
    // --- les anti-rebonds d'avarie ---
    double _hardAgo, _hardHit;
    double _underFor;

    Vec3d _dryCom;
    readonly Vec3d _ce;

    public ShipPhysics(ShipSpec spec, HullLines lines)
    {
        Spec = spec;
        Lines = lines;
        _ce = new Vec3d(0, spec.CeHeight, spec.CeZ);

        BuildProbes();
        spec.CheckFlotation(HullVolume, Config.Rho);
        BuildCompartments();

        /* Ce qu'elle peut charger avant d'être à ses marques. Pas un chiffre
           d'équilibrage : c'est le poids qui l'amène à 85 % de coque immergée,
           ce qui est déjà bas sur l'eau. Rien n'empêche de la charger au-delà —
           pouvoir ruiner un navire en le surchargeant est précisément l'intérêt
           — mais la console vire au rouge bien avant qu'elle ne s'en aille.

           PAS initialisée plus haut : sa capacité a besoin du volume de coque,
           qui n'est connu qu'une fois les sondes bâties. Un « = 0 » d'apparence
           inoffensive écrivait par-dessus la vraie valeur, et la console
           annonçait une capacité de zéro. */
        CargoCapacity = Math.Max(0, (0.85 * HullVolume * Config.Rho - spec.MassKg) / 1000);

        /* Les pompes sont dosées pour qu'UNE voie d'eau modeste soit tout juste
           rattrapable et deux non — c'est toute la tension, et cela se mesure
           plutôt que se calcule, l'entrée d'eau dépendant de la profondeur à
           laquelle elle finit par s'asseoir sur son trou.

           Franchement généreux au regard de l'histoire : une pompe à chaîne
           faisait de l'ordre d'une tonne par minute, et les navires coulaient
           précisément parce qu'on ne suivait pas. Ceci en vaut environ cinq, ce
           qui fait du contrôle des avaries une décision plutôt qu'une
           formalité. */
        PumpRate = HullVolume * 6.0e-5;

        Body.Mass = spec.MassKg;
        // CdG bas dans le lest → raide et se redressant seule ; posé sur
        // l'arrière pour qu'elle flotte sur son assiette de dessin
        Body.Com = spec.Cog;
        _dryCom = spec.Cog;
        Body.Pos = new Vec3d(0, 0, 0);
        UpdateInertia();
    }

    void UpdateInertia()
    {
        double m = Body.Mass, L = Spec.L, B = Spec.B, D = Spec.D;
        Body.Ib = new Vec3d(
            m / 12 * (D * D + L * L),     // autour de x (tangage)
            m / 12 * (B * B + L * L),     // autour de y (lacet)
            m / 12 * (B * B + D * D));    // autour de z (roulis)
    }

    /* ------------------------------------------------------------------ */
    /*  LA GRILLE DE SONDES                                                */
    /* ------------------------------------------------------------------ */

    /// <summary>
    /// Remplit l'enveloppe de coque d'une grille 3-D UNIFORME et garde les
    /// cellules qui tombent dedans. Chaque cellule porte le même volume, donc le
    /// nuage reproduit la vraie répartition de volume de la coque — la flottaison
    /// découle alors de la FORME et non de la façon dont les échantillons se
    /// trouvaient espacés.
    /// </summary>
    void BuildProbes()
    {
        double L = Spec.L, B = Spec.B, D = Spec.D;
        int nz = Config.PnZ, nx = Config.PnX, ny = Config.PnY;
        double cellVol = (L / nz) * (B / nx) * (D / ny);
        double gridBase = -(Spec.Hull.KeelDepth + Spec.Hull.KeelExtra);
        ProbeH = D / ny;              // l'échelle de lissage de l'immersion partielle

        var list = new List<Probe>(Config.ProbeCount);
        HullVolume = 0;

        for (int iz = 0; iz < nz; iz++)
        {
            double tz = (iz + 0.5) / nz;
            double z = -L / 2 + tz * L;
            double halfB = Lines.HalfB(tz), deckY = Lines.DeckY(tz), keelY = Lines.KeelY(tz);
            if (halfB < 0.05 * B / 5.8) continue;      // une station sans largeur
            for (int iy = 0; iy < ny; iy++)
            {
                double y = gridBase + ((iy + 0.5) / ny) * D;
                if (y < keelY || y > deckY) continue;
                double s = (deckY - y) / (deckY - keelY);
                double beam = halfB * Lines.BeamFactor(s);
                for (int ix = 0; ix < nx; ix++)
                {
                    double x = -B / 2 + ((ix + 0.5) / nx) * B;
                    if (Math.Abs(x) > beam) continue;
                    list.Add(new Probe { Local = new Vec3d(x, y, z), Vol = cellVol, Frac = 0 });
                    HullVolume += cellVol;
                }
            }
        }
        Probes = list.ToArray();
    }

    /// <summary>
    /// La découpe en compartiments, faite SUR LES SONDES elles-mêmes — donc la
    /// capacité d'un compartiment est du vrai volume de coque, mesuré sur le même
    /// plan de formes que tout le reste. Numérotés DEPUIS L'ÉTAMBOT : le 0 est à
    /// l'arrière, le 4 à l'étrave.
    /// </summary>
    void BuildCompartments()
    {
        int n = Config.NComp;
        double L = Spec.L;
        Comps = new Compartment[n];
        for (int i = 0; i < n; i++) Comps[i] = new Compartment();

        for (int p = 0; p < Probes.Length; p++)
        {
            ref Probe pr = ref Probes[p];
            int i = Math.Min(n - 1, Math.Max(0, (int)Math.Floor(((pr.Local.Z + L / 2) / L) * n)));
            var c = Comps[i];
            c.Cap += pr.Vol;
            c.Mid += pr.Local * pr.Vol;
            c.HalfB = Math.Max(c.HalfB, Math.Abs(pr.Local.X));
            c.DeckY = Math.Max(c.DeckY, pr.Local.Y);
            c.KeelY = Math.Min(c.KeelY, pr.Local.Y);
        }
        foreach (var c in Comps) if (c.Cap > 0) c.Mid = c.Mid / c.Cap;
    }

    /* ------------------------------------------------------------------ */
    /*  LE CHARGEMENT                                                      */
    /* ------------------------------------------------------------------ */

    /// <summary>
    /// Arrimer, ou débarquer. <paramref name="hold"/> va de 0 à l'arrière à
    /// NCOMP-1 à l'avant, <paramref name="level"/> est la hauteur dans la cale en
    /// fraction de son creux, <paramref name="side"/> va de -1 à +1 en travers.
    /// Le tonnage peut être négatif, ce qui le repose sur le quai.
    ///
    /// <paramref name="kind"/> distingue le LEST des marchandises, et la
    /// distinction porte de l'argent : le plan d'arrimage charge et décharge à
    /// volonté, donc si les épices étaient du fret ordinaire on en fabriquerait
    /// gratuitement et le commerce ne voudrait plus rien dire.
    /// </summary>
    public double LoadCargo(int hold, double level, double side, double tonnes, string kind = "lest")
    {
        var c = Comps[Math.Max(0, Math.Min(Comps.Length - 1, hold))];
        if (c.Cap <= 0) return 0;

        var slot = Cargo.Find(p => p.Hold == hold && p.Level == level
                                && p.Side == side && p.Kind == kind);
        if (slot == null)
        {
            slot = new Parcel { Hold = hold, Level = level, Side = side, Kind = kind };
            /* L'endroit est calculé UNE FOIS et gardé. Il est fixe dans son
               propre repère — le fret ne se promène pas dans la cale quand elle
               roule, ce qui est exactement ce qui le sépare de l'eau des fonds. */
            slot.At = new Vec3d(-side * 0.55 * c.HalfB,
                                c.KeelY + level * (c.DeckY - c.KeelY),
                                c.Mid.Z);
            Cargo.Add(slot);
        }
        slot.Kg = Math.Max(0, slot.Kg + tonnes * 1000);
        if (slot.Kg < 1) Cargo.Remove(slot);
        UpdateMass();
        return CargoTonnes;
    }

    /// <summary>Ce qu'elle porte d'une nature donnée, en tonnes. C'est ce qui est vendable.</summary>
    public double CargoOf(string kind)
    {
        double kg = 0;
        foreach (var p in Cargo) if (p.Kind == kind) kg += p.Kg;
        return kg / 1000;
    }

    /// <summary>
    /// Débarquer d'une nature, en prenant dans les cales où elle se trouve. On
    /// vide le plus chargé d'abord : c'est ce qu'on fait à quai, et cela évite de
    /// laisser des fonds de cale partout.
    /// </summary>
    public double UnloadKind(string kind, double tonnes)
    {
        double left = tonnes * 1000, moved = 0;
        var mine = Cargo.FindAll(p => p.Kind == kind);
        mine.Sort((a, b) => b.Kg.CompareTo(a.Kg));
        foreach (var p in mine)
        {
            if (left <= 0) break;
            double take = Math.Min(p.Kg, left);
            p.Kg -= take; left -= take; moved += take;
            if (p.Kg < 1) Cargo.Remove(p);
        }
        UpdateMass();
        return moved / 1000;
    }

    public void ClearCargo() { Cargo.Clear(); UpdateMass(); }

    /* ------------------------------------------------------------------ */
    /*  L'AVARIE                                                           */
    /* ------------------------------------------------------------------ */

    /// <summary>
    /// Ouvrir un trou. L'aire en m², à une hauteur donnée en fraction du creux du
    /// compartiment — 0 à la quille, 1 au livet. Sous la flottaison est ce qui
    /// compte : un trou au-dessus n'admet rien tant qu'elle ne s'assoit pas
    /// dessus.
    /// </summary>
    public Breach? MakeBreach(int index, double area = 0.35, double heightFrac = 0.25)
    {
        if (index < 0 || index >= Comps.Length) return null;
        var c = Comps[index];
        if (c.Cap <= 0) return null;
        var br = new Breach
        {
            Comp = index,
            Area = area,
            Y = c.KeelY + (c.DeckY - c.KeelY) * heightFrac,
            Z = c.Mid.Z
        };
        Breaches.Add(br);
        return br;
    }

    /// <summary>
    /// UN TROU, ET IL TRAVAILLE.
    ///
    /// Une coque ne perce pas en cinq endroits nets à la fois : une couture cède,
    /// et la mer l'ouvre. Chaque appel élargit la MÊME blessure, et passée une
    /// largeur de bordé elle gagne le compartiment voisin — ce qui est ce qui
    /// finit par la porter au-delà de ce qu'un seul compartiment pouvait tenir.
    /// </summary>
    public Breach? WorsenBreach()
    {
        if (Breaches.Count == 0) return MakeBreach(1, 0.05, 0.30);
        var br = Breaches[0];
        br.Area *= 2.0;
        int spread = (int)Math.Floor(Math.Log2(br.Area / 0.05));
        for (int k = 1; k <= spread && k < Comps.Length; k++)
        {
            int i = br.Comp + (k % 2 != 0 ? k : -k);
            if (i < 0 || i >= Comps.Length) continue;
            if (Breaches.Exists(o => o.Comp == i)) continue;
            MakeBreach(i, br.Area * 0.4, 0.30);
        }
        return br;
    }

    /// <summary>
    /// LA SOUTE SAUTE : son fond est ouvert d'un bout à l'autre d'un coup.
    ///
    /// Pas un chemin de naufrage particulier — le même envahissement que
    /// n'importe quel autre, avec chaque compartiment percé bas et large et les
    /// pompes soufflées avec le reste. Elle descend en moins d'une minute parce
    /// que l'arithmétique le dit, pas parce que quelque chose ici le décide.
    /// </summary>
    public void BlowUp()
    {
        Breaches.Clear();
        for (int i = 0; i < Comps.Length; i++) MakeBreach(i, 2.4, 0.05);
        PumpOn = false;
        /* Et elle est déjà ouverte à la mer : une explosion ne la fait pas
           poliment commencer à embarquer depuis zéro. Un cinquième de son volume
           entre avec le souffle. */
        foreach (var c in Comps) c.Vol = Math.Max(c.Vol, c.Cap * 0.20);
        UpdateMass();
    }

    /// <summary>L'assécher et boucher chaque trou — ce que « réparer » veut dire.</summary>
    public void Salvage()
    {
        Breaches.Clear();
        foreach (var c in Comps) c.Vol = 0;
        Foundered = false;
        Standing = 1;      // et ses mâts sont replantés
        Whole = 1;         // et sa toile renvergée
        UpdateMass();
    }

    /// <summary>Larguer les amarres : elle est de nouveau à elle.</summary>
    public int CastOff()
    {
        int n = Moorings.Count;
        Moorings.Clear();
        return n;
    }

    /// <summary>
    /// L'eau qui entre, celle qui sort, et là où elle repose.
    ///
    /// L'ENTRÉE SUIT TORRICELLI, v = √(2gh), donc un trou profond emplit bien
    /// plus vite qu'un trou près de la flottaison — et surtout la charge h
    /// GRANDIT à mesure qu'elle s'enfonce : c'est l'emballement qui noie
    /// réellement un navire, et il est gratuit.
    ///
    /// Dès qu'un livet passe sous l'eau, l'ENVAHISSEMENT PAR LE PONT prend le
    /// relais, par les écoutilles et les sabords plutôt que par le trou — et
    /// c'est presque toujours lui qui achève, pas la voie d'eau initiale.
    /// </summary>
    void Flooding(double dt, Ocean ocean, double t)
    {
        FloodRate = 0;
        if (Breaches.Count == 0 && FloodVol <= 1e-6) return;
        double before = FloodVol;

        foreach (var br in Breaches)
        {
            var c = Comps[br.Comp];
            if (c.Vol >= c.Cap) continue;
            Vec3d pw = Body.Quat.Rotate(new Vec3d(0, br.Y, br.Z)) + Body.Pos;
            double head = ocean.Sample(pw.X, pw.Z, t) - pw.Y;
            if (head <= 0) continue;
            c.Vol = Math.Min(c.Cap, c.Vol + 0.62 * br.Area * Math.Sqrt(2 * Config.G * head) * dt);
        }

        foreach (var c in Comps)
        {
            if (c.Vol >= c.Cap) continue;
            // livet sous l'eau : elle l'embarque en grand, par toutes les ouvertures
            Vec3d pw = Body.Quat.Rotate(new Vec3d(0, c.DeckY, c.Mid.Z)) + Body.Pos;
            double over = ocean.Sample(pw.X, pw.Z, t) - pw.Y;
            if (over > 0)
                c.Vol = Math.Min(c.Cap, c.Vol + 0.25 * c.HalfB * Math.Sqrt(2 * Config.G * over) * dt);
        }

        if (PumpOn)
        {
            // les pompes tirent sur le compartiment le plus plein d'abord,
            // comme un équipage le ferait
            double left = PumpRate * dt;
            while (left > 1e-9)
            {
                Compartment? worst = null;
                foreach (var c in Comps)
                    if (c.Vol > 1e-9 && (worst == null || c.Vol > worst.Vol)) worst = c;
                if (worst == null) break;
                double take = Math.Min(left, worst.Vol);
                worst.Vol -= take; left -= take;
            }
        }

        UpdateMass();
        // le débit net : positif veut dire que la mer gagne, et c'est le seul
        // nombre qui dise si la situation est sous contrôle
        FloodRate = dt > 0 ? (FloodVol - before) / dt : 0;
    }

    /// <summary>
    /// Masse, centre de gravité et inertie, avec l'eau qu'elle a embarquée.
    ///
    /// Deux choses en découlent qu'il vaut mieux ne pas défaire. L'eau repose au
    /// FOND d'un compartiment, donc son centre MONTE à mesure qu'il se remplit —
    /// ce qui veut dire qu'un fond d'eau est du lest et la raidit, tandis qu'une
    /// masse haute la chavire. Et un compartiment À MOITIÉ plein a une carène
    /// liquide : l'eau court à la bande basse et y reste, donc elle combat tout
    /// effort de redressement. Un compartiment plein ne peut pas faire ça, ce qui
    /// est pourquoi le remplir complètement est un vrai remède.
    /// </summary>
    void UpdateMass()
    {
        double vol = 0;
        foreach (var c in Comps) vol += c.Vol;
        FloodVol = vol;
        FloodTonnes = vol * Config.Rho / 1000;

        double wm = vol * Config.Rho;
        double cargoKg = 0;
        foreach (var p in Cargo) cargoKg += p.Kg;
        CargoTonnes = cargoKg / 1000;
        Body.Mass = Spec.MassKg + wm + cargoKg;

        if (wm < 1e-6 && cargoKg < 1e-6)
        {
            Body.Com = _dryCom;
            FreeSurfaceRise = 0;
        }
        else
        {
            // où est le bas, dans son propre repère — ceci porte la gîte ET l'assiette
            Vec3d down = Body.Quat.Inverted().Rotate(new Vec3d(0, -1, 0));
            double lat = down.X, lon = down.Z;

            Vec3d com = _dryCom * Spec.MassKg;
            // le fret d'abord : fixe dans son repère, et sans carène liquide à lui
            foreach (var p in Cargo) com += p.At * p.Kg;

            foreach (var c in Comps)
            {
                if (c.Vol <= 1e-9) continue;
                double f = c.Vol / c.Cap;
                double free = 4 * f * (1 - f) * FreeSurface;   // nulle à vide comme à plein
                var wc = new Vec3d(
                    c.Mid.X + lat * free * c.HalfB,
                    c.KeelY + 0.5 * f * (c.DeckY - c.KeelY),   // elle repose au fond
                    c.Mid.Z + lon * free * (c.DeckY - c.KeelY) * 0.67);
                com += wc * (c.Vol * Config.Rho);
            }
            Body.Com = com / Body.Mass;

            /* LA CARÈNE LIQUIDE, correctement cette fois.
               De l'eau libre dans un compartiment à moitié plein ne se contente
               pas de pencher sous le vent : elle détruit la stabilité, et la
               correction classique est une REMONTÉE VIRTUELLE du centre de
               gravité de Σ(ρ·i)/Δ, où i = l·b³/12 est le moment quadratique de
               la surface libre.

               Elle ne dépend PAS de l'angle de gîte, ce qui est exactement ce
               qui la rend mortelle : le navire est déjà instable avant d'avoir
               donné de la bande. Et elle va comme le CUBE de la largeur, d'où le
               cloisonnement longitudinal des vrais navires — un compartiment
               large est pire que trois étroits contenant la même eau.

               Écrite d'abord comme un déplacement du centroïde vers la bande
               basse. C'est réel, mais du second ordre : mesuré sur la goélette,
               cela déplaçait le centre de gravité de deux centimètres et
               produisait cinq tonnes-mètres, contre un moment redresseur deux
               ordres plus grand — tandis que l'eau de fond abaissait G de 23 cm
               et la RAIDISSAIT. Elle s'envahissait, devenait plus stable, et
               sombrait bolt upright. Le déplacement de centroïde est gardé, mais
               c'est ce terme-ci qui décide si elle passe par-dessus. */
            double fsm = 0;
            double cl = Spec.L / Comps.Length;
            foreach (var c in Comps)
            {
                if (c.Vol <= 1e-9 || c.Vol >= c.Cap * 0.995) continue;   // plein : pas de surface
                double bw = 2 * c.HalfB;
                fsm += Config.Rho * cl * bw * bw * bw / 12;
            }
            FreeSurfaceRise = FreeSurface * fsm / Body.Mass;
            Body.Com = new Vec3d(Body.Com.X, Body.Com.Y + FreeSurfaceRise, Body.Com.Z);
        }

        UpdateInertia();
    }
}
