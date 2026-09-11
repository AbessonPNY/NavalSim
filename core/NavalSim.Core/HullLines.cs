

namespace NavalSim.Core;

/// <summary>Le maillage sorti du plan de formes, en données pures — la couche Godot
/// en fait un ArrayMesh, et c'est la seule chose qu'elle ait à en faire.</summary>
public readonly record struct HullMesh(float[] Positions, int[] Indices);

/// <summary>
/// Le plan de formes de la coque, en fonctions pures de la position de station.
/// Bâti sur un <see cref="ShipSpec"/>, et PARTAGÉ par le maillage visible et la
/// grille de sondes — donc les deux décrivent exactement une seule forme.
///
/// C'est l'invariant le plus important de cette simulation : ce qu'on voit est
/// ce qui flotte. Si les deux divergent, l'image cesse de rendre compte de la
/// physique, et rien dans l'affichage ne le signale.
/// </summary>
public sealed class HullLines
{
    private readonly HullShape _h;
    public ShipSpec Spec { get; }

    public HullLines(ShipSpec spec)
    {
        Spec = spec;
        _h = spec.Hull;
    }

    public static double Smooth01(double u)
    {
        u = u < 0 ? 0 : (u > 1 ? 1 : u);
        return u * u * (3 - 2 * u);
    }

    /// <summary>La tonture. t = 0 au tableau arrière → 1 à l'étrave.</summary>
    public double DeckY(double t)
    {
        double fwd = Math.Max(0, (t - 0.42) / 0.58);
        double aft = Math.Max(0, (0.42 - t) / 0.42);
        return _h.FreeboardMid + _h.SheerBow * fwd * fwd + _h.SheerStern * aft * aft;
    }

    /// <summary>Dessous de quille / râblure.</summary>
    public double KeelY(double t)
    {
        double d = _h.KeelDepth + _h.DragAft * (1 - t);      // elle s'assoit plus bas sur l'arrière
        d *= 1 - _h.ForefootLift * Smooth01((t - 0.80) / 0.20);  // le brion se relève vers l'étrave
        d *= 1 - _h.CounterLift * Smooth01((0.12 - t) / 0.12);   // la voûte se relève à l'arrière
        return -d;
    }

    /// <summary>Demi-largeur à la flottaison.</summary>
    public double HalfB(double t)
    {
        double sh = Math.Pow(
            Math.Max(0, Math.Sin(Math.PI * Math.Pow(t, _h.WaterlinePower))),
            _h.WaterlineFull);
        // le tableau arrière garde sa largeur sur l'arrière
        sh = Math.Max(sh, _h.TransomWidth * Math.Exp(-t * 14));
        return (_h.Beam / 2) * Math.Min(1, sh);
    }

    /// <summary>
    /// Forme de section. s = 0 au livet → 1 à la quille. Les hauts et le bouchain
    /// gardent presque toute la largeur — c'est là que vit la poussée — puis le
    /// galbord rentre dans la quille. Un <c>sectionTuck</c> bas donne un verre à
    /// pied ; un haut donne le corps plus plein d'un navire de guerre portant ses
    /// pièces haut.
    /// </summary>
    public double BeamFactor(double s)
        => Math.Pow(1 - Smooth01((s - _h.SectionTuck) / (1 - _h.SectionTuck)), _h.SectionPower);

    /// <summary>Élève les stations en une surface de coque fermée.</summary>
    public HullMesh BuildGeometry()
    {
        double L = Spec.L;
        const int stations = 14, sideSteps = 6;

        // Les stations sont elevees en DOUBLE et arrondies une seule fois, a
        // l ecriture -- exactement ce que fait le JS, ou THREE.Vector3 porte des
        // doubles et ou seul Float32BufferAttribute arrondit. Passer par un
        // vecteur float32 intermediaire arrondirait deux fois.
        var rows = new Vec3d[stations + 1][];
        for (int i = 0; i <= stations; i++)
        {
            double t = (double)i / stations;
            double z = -L / 2 + t * L;
            double hb = HalfB(t), dY = DeckY(t), kY = KeelY(t);
            var sec = new Vec3d[sideSteps + 1];
            for (int j = 0; j <= sideSteps; j++)
            {
                double s = (double)j / sideSteps;
                sec[j] = new Vec3d(hb * BeamFactor(s), dY + (kY - dY) * s, z);
            }
            rows[i] = sec;
        }

        var pos = new List<float>();
        var idx = new List<int>();
        var rowBase = new int[rows.Length];
        int vi = 0;
        for (int i = 0; i < rows.Length; i++)
        {
            rowBase[i] = vi;
            var sec = rows[i];
            for (int j = 0; j < sec.Length; j++) { pos.Add((float)sec[j].X); pos.Add((float)sec[j].Y); pos.Add((float)sec[j].Z); vi++; }
            // bâbord en miroir, en sautant le point de quille dupliqué
            for (int j = sec.Length - 2; j >= 0; j--) { pos.Add((float)-sec[j].X); pos.Add((float)sec[j].Y); pos.Add((float)sec[j].Z); vi++; }
        }

        int ring = rows[0].Length * 2 - 1;
        for (int i = 0; i < rows.Length - 1; i++)
        {
            int a = rowBase[i], b = rowBase[i + 1];
            for (int j = 0; j < ring - 1; j++)
            {
                idx.Add(a + j); idx.Add(b + j); idx.Add(a + j + 1);
                idx.Add(a + j + 1); idx.Add(b + j); idx.Add(b + j + 1);
            }
            int aS = a, aP = a + ring - 1, bS = b, bP = b + ring - 1;   // le pont suit la tonture
            idx.Add(aS); idx.Add(aP); idx.Add(bS);
            idx.Add(bS); idx.Add(aP); idx.Add(bP);
        }

        void Cap(int b, bool reverse)
        {
            for (int j = 1; j < ring - 1; j++)
            {
                if (reverse) { idx.Add(b); idx.Add(b + j); idx.Add(b + j + 1); }
                else { idx.Add(b); idx.Add(b + j + 1); idx.Add(b + j); }
            }
        }
        Cap(rowBase[0], false);                  // le tableau arrière
        Cap(rowBase[rows.Length - 1], true);     // l'étrave

        return new HullMesh(pos.ToArray(), idx.ToArray());
    }
}
