using System;
using System.Collections.Generic;

namespace NavalSim.Core;

/// <summary>
/// LA SOUTE QUI LA COUPE EN DEUX. Une moitié est un navire comme un autre pour le
/// solveur : des sondes, des compartiments, une masse, un corps — simplement
/// moins de tout cela, et une tranche ouverte à la mer.
///
/// ON COUPE À UNE CLOISON. Les compartiments sont tranchés sur les sondes (cinq,
/// de l'étambot à l'étrave), et leurs limites tombent entre deux rangées de
/// sondes — onze rangées contre cinq compartiments, aucune ne tombe dessus. Chaque
/// moitié emporte donc ses compartiments ENTIERS, leur eau, leurs voies d'eau, et
/// rien n'est à réinventer : un compartiment de l'autre bord y reste, vide et sans
/// capacité, et tout le solveur sait déjà l'ignorer.
///
/// LA MASSE SE PARTAGE COMME LE VOLUME de carène, et le centre de gravité de
/// chaque moitié au centroïde de la sienne, décalé du même écart pour que les
/// deux moments refassent celui du navire entier — sans quoi la coupe créerait
/// ou détruirait un couple.
/// </summary>
public sealed partial class ShipPhysics
{
    /// <summary>Coupée en deux : elle n'est plus qu'une moitié.</summary>
    public bool Broken { get; private set; }
    /// <summary>Ce qu'il reste d'elle le long de son axe, en mètres de son repère.</summary>
    public double ZLo = double.NegativeInfinity, ZHi = double.PositiveInfinity;
    /// <summary>La part de carène qui la retient en route et en travers : 1 entière.</summary>
    public double HydroScale = 1;
    /// <summary>Tourner autour de son centre de gravité plutôt que de l'origine de son repère.</summary>
    public bool RotateAboutCom;

    /// <summary>Ce qui reste, loin de la tranche, des voies d'eau du souffle (en part) et de l'eau embarquée d'un coup (en part de la capacité).</summary>
    public const double EndLeak = 0.06, EndFill = 0.04;

    /// <summary>
    /// CE QUE LE PONT LAISSE ENTRER quand son livet est sous l'eau, en part du
    /// navire intact (1). Un navire entier qui s'enfonce embarque par toutes ses
    /// écoutilles à la fois ; un tronçon qui se DRESSE présente son pont presque
    /// debout, et l'eau n'y entre que par ce qui est en bas. Sans cela le bout
    /// intact s'emplissait à l'instant où il basculait, et la moitié se dressait
    /// sous l'eau, déjà à vingt mètres (banc « soute »).
    /// </summary>
    public double DeckLeak = 1;

    /// <summary>Le même, pour une moitié — réglé au banc pour qu'elle se dresse au-dessus de l'eau puis parte.</summary>
    public static double HalfDeckLeak = 0.15;

    /// <summary>
    /// LA PART D'UN COMPARTIMENT QUE L'EAU PEUT PRENDRE — le reste est du chêne :
    /// membrures, vaigrage, ponts, cloisons, qui flottent. Le solveur compte toute
    /// l'enveloppe comme de l'air ; c'est ce qui coulait une moitié en trois
    /// secondes, à fleur d'eau dès son compartiment ouvert plein (banc « soute »).
    /// </summary>
    public static double HalfFloodable = 0.75;

    int CompOf(double z) => Math.Min(Comps.Length - 1, Math.Max(0,
        (int)Math.Floor(((z + Spec.L / 2) / Spec.L) * Comps.Length)));

    /// <summary>La cote de la cloison <paramref name="k"/>, à l'avant du compartiment k−1.</summary>
    public double Bulkhead(int k) => -Spec.L / 2 + k * Spec.L / Comps.Length;

    /// <summary>
    /// La couper à la cloison <paramref name="k"/> : elle garde l'arrière
    /// (compartiments 0 à k−1) et rend l'avant, posé exactement où il était, à la
    /// même vitesse. Les deux tournent désormais autour de leur centre de gravité.
    /// </summary>
    public ShipPhysics SplitOff(int k)
    {
        int n = Comps.Length;
        k = Math.Clamp(k, 1, n - 1);
        double zCut = Bulkhead(k);
        var bow = new ShipPhysics(Spec, Lines);

        // --- les sondes, avec ce qu'elles savaient de leur immersion ---
        var aftP = new List<Probe>(); var bowP = new List<Probe>();
        double va = 0, vb = 0, ma = 0, mb = 0;
        foreach (var p in Probes)
        {
            if (CompOf(p.Local.Z) < k) { aftP.Add(p); va += p.Vol; ma += p.Vol * p.Local.Z; }
            else { bowP.Add(p); vb += p.Vol; mb += p.Vol * p.Local.Z; }
        }
        double whole = va + vb;

        // --- la masse et son centre, partagés comme le volume ---
        double ca = va > 0 ? ma / va : zCut, cb = vb > 0 ? mb / vb : zCut;
        double shift = _dryCom.Z - (va * ca + vb * cb) / whole;
        double mass = _dryMass;
        bow._dryMass = mass * vb / whole;
        bow._dryCom = new Vec3d(_dryCom.X, _dryCom.Y, cb + shift);
        _dryMass = mass * va / whole;
        _dryCom = new Vec3d(_dryCom.X, _dryCom.Y, ca + shift);

        Probes = aftP.ToArray(); HullVolume = va;
        bow.Probes = bowP.ToArray(); bow.HullVolume = vb;
        _len = zCut + Spec.L / 2; bow._len = Spec.L / 2 - zCut;
        ZLo = -Spec.L / 2; ZHi = zCut; bow.ZLo = zCut; bow.ZHi = Spec.L / 2;
        HydroScale = va / whole; bow.HydroScale = vb / whole;

        // --- les compartiments : chacun à sa moitié, avec son eau ---
        for (int i = 0; i < n; i++)
        {
            Compartment mine = Comps[i], theirs = bow.Comps[i];
            if (i >= k)
            {
                theirs.Vol = mine.Vol; theirs.Air = mine.Air; theirs.Over = mine.Over; theirs.Vent = mine.Vent;
                mine.Cap = 0; mine.Vol = 0; mine.Air = 0;
            }
            else { theirs.Cap = 0; theirs.Vol = 0; }
        }
        for (int i = Breaches.Count - 1; i >= 0; i--)
            if (Breaches[i].Comp >= k) { bow.Breaches.Add(Breaches[i]); Breaches.RemoveAt(i); }
        for (int i = Cargo.Count - 1; i >= 0; i--)
            if (Cargo[i].At.Z >= zCut) { bow.Cargo.Add(Cargo[i]); Cargo.RemoveAt(i); }

        /* LA TRANCHE EST OUVERTE À LA MER — et c'est elle qui décide de tout ce
           qui suit. Une voie d'eau de la taille de la section, au bas de la
           tranche : le compartiment qui la borde s'emplit en quelques secondes,
           et la moitié s'enfonce par là, l'autre bout se dressant. */
        double tz = (zCut + Spec.L / 2) / Spec.L;
        double keel = Lines.KeelY(tz), deck = Lines.DeckY(tz);
        double area = 2 * Lines.HalfB(tz) * (deck - keel) * 0.6;
        double y = keel + 0.25 * (deck - keel);
        Breaches.Add(new Breach { Comp = k - 1, Area = area, X = 0, Y = y, Z = zCut - 0.5 });
        bow.Breaches.Add(new Breach { Comp = k, Area = area, X = 0, Y = y, Z = zCut + 0.5 });

        /* LES BOUTS GARDENT LEUR AIR. L'explosion de la soute ouvre tout le navire
           (BlowUp), ce qui est juste tant qu'il est d'un seul tenant. Coupé, le
           souffle a pris le MILIEU : les compartiments loin de la tranche ne sont
           que disjoints — de petites fentes, peu d'eau d'un coup. Sans cela les
           deux moitiés s'emplissaient ensemble et coulaient à plat en quatre
           secondes (banc « soute ») ; avec, le bout ouvert s'enfonce et l'autre se
           dresse, ce qu'on a toujours vu d'un navire rompu. */
        static void Ends(ShipPhysics s, int open)
        {
            foreach (var br in s.Breaches) if (br.Comp != open) br.Area *= EndLeak;
            for (int i = 0; i < s.Comps.Length; i++)
                if (i != open) s.Comps[i].Vol = Math.Min(s.Comps[i].Vol, s.Comps[i].Cap * EndFill);
        }
        Ends(this, k - 1);
        Ends(bow, k);
        foreach (var s in new[] { this, bow })
            foreach (var c in s.Comps) { c.Cap *= HalfFloodable; c.Vol = Math.Min(c.Vol, c.Cap); }

        // --- le reste de son état ---
        bow.Body.Pos = Body.Pos; bow.Body.Quat = Body.Quat;
        bow.Body.Vel = Body.Vel; bow.Body.AngVel = Body.AngVel;
        bow.World = World; bow.GroundLift = GroundLift;
        bow.Standing = Standing; bow.Whole = Whole; bow.SetFrac = SetFrac;
        bow.PumpOn = false; PumpOn = false;
        bow.Powder = 0; Powder = 0;
        RotateAboutCom = bow.RotateAboutCom = true;
        DeckLeak = bow.DeckLeak = HalfDeckLeak;
        Broken = bow.Broken = true;
        UpdateMass(); bow.UpdateMass();
        return bow;
    }
}
