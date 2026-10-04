using System;

namespace NavalSim.Core;

/// <summary>
/// LE FOND DE LUI-MÊME, côté processeur — la jumelle de
/// godot/shaders/seabed.gdshaderinc, qui le PEINT. On n'en garde ici que ce dont
/// un semis a besoin : où sont les prés de l'herbier, pour que les touffes poussent
/// SUR les plaques vertes que la carte de couleur dessine, et pas à côté.
///
/// MÊME BRUIT, MÊMES SEUILS, en simple précision comme le GPU : changer l'un sans
/// l'autre décolle l'herbe de son pré, en silence.
/// </summary>
public static class Seabed
{
    // paint_hash22 de ground_paint.gdshaderinc, composante x
    static float Hash(float px, float py)
    {
        float ax = Fract(px * 0.1031f), ay = Fract(py * 0.1030f), az = Fract(px * 0.0973f);
        float d = ax * (ay + 33.33f) + ay * (az + 33.33f) + az * (ax + 33.33f);
        ax += d; ay += d; az += d;
        return Fract((ax + ay) * az);
    }

    static float Fract(float v) => v - MathF.Floor(v);

    // paint_noise
    static float Noise(float x, float y)
    {
        float ix = MathF.Floor(x), iy = MathF.Floor(y), fx = x - ix, fy = y - iy;
        float ux = fx * fx * (3 - 2 * fx), uy = fy * fy * (3 - 2 * fy);
        float a = Hash(ix, iy), b = Hash(ix + 1, iy), c = Hash(ix, iy + 1), d = Hash(ix + 1, iy + 1);
        return Lerp(Lerp(a, b, ux), Lerp(c, d, ux), uy);
    }

    static float Lerp(float a, float b, float t) => a + (b - a) * t;

    static double Smooth(double e0, double e1, double x)
    {
        double t = Math.Clamp((x - e0) / (e1 - e0), 0, 1);
        return t * t * (3 - 2 * t);
    }

    /// <summary>La vase en ce point, 0 à 1 : l'eau calme d'une rade, et le grand fond — par nappes.</summary>
    public static double Mud(World w, double x, double z)
    {
        double d = -w.HeightAt(x, z);
        double calm = Smooth(0.8, 0.35, w.Shelter(x, z)) * Smooth(1.0, 4.0, d);
        double deep = Smooth(16, 40, d) * 0.85;
        double blot = Noise((float)(x * 0.03), (float)(z * 0.03)) * 0.7 + Noise((float)(x * 0.2), (float)(z * 0.2)) * 0.3;
        double m = Math.Max(calm, deep);
        return Math.Clamp(m * Smooth(0.2, 0.55, blot + m * 0.35), 0, 1);
    }

    /// <summary>
    /// L'HERBIER en ce point, 0 à 1 : une plaque de pré, sur le plat, d'un à douze
    /// mètres, moins là où la vase le recouvre. La pente se lit sur cinq mètres,
    /// comme le GPU la lit sur un carreau du relief.
    /// </summary>
    public static double Meadow(World w, double x, double z)
    {
        double d = -w.HeightAt(x, z);
        double band = Smooth(0.8, 2.0, d) * (1 - Smooth(8, 12, d));
        if (band <= 0) return 0;
        double meadow = Noise((float)(x * 0.018), (float)(z * 0.018)) * 0.65 + Noise((float)(x * 0.07), (float)(z * 0.07)) * 0.35;
        double gx = (w.HeightAt(x + 2.5, z) - w.HeightAt(x - 2.5, z)) / 5, gz = (w.HeightAt(x, z + 2.5) - w.HeightAt(x, z - 2.5)) / 5;
        double slope = 1 - 1 / Math.Sqrt(1 + gx * gx + gz * gz);
        return band * Smooth(0.52, 0.62, meadow) * (1 - Smooth(0.12, 0.25, slope)) * (1 - Mud(w, x, z) * 0.7);
    }
}
