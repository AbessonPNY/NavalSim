namespace NavalSim.Core;

/// <summary>
/// LES PETITES FORMULES QUE TOUT LE MONDE RÉÉCRIVAIT : la rampe douce, l'hypoténuse,
/// le tirage gaussien. Une seule écriture, pour que deux usagers ne divergent pas
/// d'un signe ou d'un ordre d'opérations (la parité avec la page compare au bit).
/// </summary>
public static class MathX
{
    /// <summary>La rampe douce sur [0, 1] : 0 avant, 1 après, en S entre les deux (smoothstep).</summary>
    public static double Smooth01(double u)
    {
        u = u < 0 ? 0 : (u > 1 ? 1 : u);
        return u * u * (3 - 2 * u);
    }

    /// <summary>La même rampe, de <paramref name="a"/> à <paramref name="b"/> (a &gt; b la renverse, comme en GLSL).</summary>
    public static double SmoothStep(double a, double b, double x)
    {
        double u = Math.Clamp((x - a) / (b - a), 0, 1);
        return u * u * (3 - 2 * u);
    }

    public static double Hyp(double a, double b) => Math.Sqrt(a * a + b * b);

    /// <summary>
    /// Un tirage normal centré réduit (Box-Muller) depuis deux tirages uniformes sur
    /// [0, 1[, pris dans cet ordre — l'ordre des arguments est celui des tirages.
    /// </summary>
    public static double Gauss(double u1, double u2) => Math.Sqrt(-2 * Math.Log(1 - u1)) * Math.Cos(2 * Math.PI * u2);
}
