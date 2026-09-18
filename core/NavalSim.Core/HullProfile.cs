using System;

namespace NavalSim.Core;

/// <summary>
/// Ce que la mer sait d'une coque pour écumer le long de son bordé : ses
/// demi-largeurs à la flottaison, de l'arrière à l'étrave, en fractions de la
/// plus grande — porté de <c>ShipModel.hullProfile</c> (ship-model.js).
///
/// Ce fut d'abord une ELLIPSE de L × B, et aucune coque n'en est une : sur le
/// Roter Löwe le collier courait un mètre et demi à l'intérieur du bordé sur la
/// moitié de sa longueur, et près de quatre mètres à côté à sa poupe — là où
/// l'ellipse s'est effilée en pointe et où elle fait encore 3,8 m de large.
///
/// Une coque PROCÉDURALE se lit dans <see cref="HullLines"/>, l'unique plan de
/// formes ; un modèle .glb se mesurera sur son propre maillage, puisque c'est
/// la forme que l'œil voit (à venir avec le chargement des modèles).
/// </summary>
public sealed class HullProfile
{
    /// <summary>Demi-largeurs par station, en fractions de <see cref="MaxHalfB"/>.</summary>
    public float[] Fractions { get; }
    /// <summary>La plus grande demi-largeur MESURÉE, qui met le profil à l'échelle.</summary>
    public double MaxHalfB { get; }
    public double HalfLen { get; }
    /// <summary>Où le corps à la flottaison commence et finit, en mètres le long d'elle.</summary>
    public double EndAft { get; }
    public double EndFwd { get; }

    HullProfile(float[] f, double maxHalfB, double halfLen, double aft, double fwd)
    {
        Fractions = f; MaxHalfB = maxHalfB; HalfLen = halfLen; EndAft = aft; EndFwd = fwd;
    }

    /// <summary>
    /// Une coque procédurale : le plan de formes, station par station. En
    /// <c>float</c> comme les <c>Float32Array</c> de l'original — arrondi à
    /// chaque passe de lissage, calcul en double entre deux — pour que le banc
    /// de parité puisse le comparer au bit près.
    /// </summary>
    public static HullProfile Procedural(ShipSpec spec, HullLines lines, int n = 64)
    {
        var raw = new float[n];
        for (int i = 0; i < n; i++) raw[i] = (float)lines.HalfB((i + 0.5) / n);
        return Finish(raw, spec);
    }

    /// <summary>
    /// Lisser, normaliser, trouver les extrémités du corps : commun aux deux
    /// sources, mesurée ou procédurale.
    /// </summary>
    static HullProfile Finish(float[] outp, ShipSpec spec)
    {
        int n = outp.Length;
        double halfLen = spec.L * 0.5;

        /* LISSER LA LIGNE. Un .glb ne porte que quelques centaines de sommets :
           étalés sur soixante-quatre stations, la plupart en reçoivent un ou deux
           et la règle du plus grand x les rend en facettes — le collier en dents
           de scie. Trois passes d'un noyau 1-2-1 l'ôtent et laissent le galbe du
           bordé ; les stations d'extrémité sont tenues, elle garde ses pointes. */
        for (int pass = 0; pass < 3; pass++)
        {
            var prev = (float[])outp.Clone();
            for (int i = 1; i < n - 1; i++)
                outp[i] = (float)(((double)prev[i - 1] + 2.0 * prev[i] + prev[i + 1]) * 0.25);
        }

        double max = 0;
        for (int i = 0; i < n; i++) if (outp[i] > max) max = outp[i];
        if (max <= 1e-4)
        {
            // rien de mesurable : un rectangle, faux mais jamais absent
            Array.Fill(outp, 1f);
            return new HullProfile(outp, spec.B * 0.5, halfLen, -halfLen, halfLen);
        }
        for (int i = 0; i < n; i++) outp[i] = (float)(outp[i] / max);

        /* Où son corps à la flottaison commence et finit, pour que la mer ne
           trace pas un contour jusqu'à sa longueur hors tout là où elle est déjà
           finie : l'étrave s'élance au-dessus de l'eau, la voûte la surplombe. */
        int aft = 0, fwd = n - 1;
        while (aft < n - 1 && outp[aft] < 0.12) aft++;
        while (fwd > 0 && outp[fwd] < 0.12) fwd--;
        double StationZ(int i) => -halfLen + ((i + 0.5) / n) * 2 * halfLen;
        return fwd > aft
            ? new HullProfile(outp, max, halfLen, StationZ(aft), StationZ(fwd))
            : new HullProfile(outp, max, halfLen, -halfLen, halfLen);
    }
}
