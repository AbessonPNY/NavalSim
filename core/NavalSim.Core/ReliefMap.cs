using System;

namespace NavalSim.Core;

/// <summary>
/// UNE CARTE NORMALE TIRÉE DE LA RUGOSITÉ — <c>Naval.normalFromHeight</c>.
///
/// glTF n'a pas de carte de relief en niveaux de gris. Quand la même image sert
/// de rugosité ET de relief, la fiche le déclare (<c>model.relief</c>, un gain de
/// pente) et la carte normale en est dérivée au chargement : blanc = en relief.
/// 6 ne se voit pas, 16 marque les préceintes et le fil du bois, au-delà le bordé
/// paraît sculpté. À ne pas mettre sur une rugosité peinte en aplats, dont chaque
/// bord deviendrait une arête.
///
/// En octets comme le canvas de la page : lecture en /255, écriture arrondie et
/// bornée comme un Uint8ClampedArray, pour que les deux cartes soient les mêmes.
/// </summary>
public static class ReliefMap
{
    /// <summary>
    /// <paramref name="rgba"/> : l'image source, 4 octets par pixel, lignes de haut
    /// en bas. <paramref name="channel"/> : celui qui porte la hauteur — le vert,
    /// pour une rugosité glTF. Rend la carte normale, même format.
    /// </summary>
    public static byte[] FromHeight(byte[] rgba, int w, int h, double strength, int channel = 1)
    {
        var hgt = new float[w * h];
        for (int i = 0; i < w * h; i++) hgt[i] = rgba[i * 4 + channel] / 255f;

        var o = new byte[w * h * 4];
        // les poids de Sobel font 8 par côté : k/8 laisse `strength` un simple gain de pente
        double k = strength / 8;
        for (int y = 0; y < h; y++)
        {
            int ym = (y > 0 ? y - 1 : y) * w, y0 = y * w, yp = (y < h - 1 ? y + 1 : y) * w;
            for (int x = 0; x < w; x++)
            {
                int xm = x > 0 ? x - 1 : x, xp = x < w - 1 ? x + 1 : x;
                double dx = ((double)hgt[ym + xp] + 2 * hgt[y0 + xp] + hgt[yp + xp])
                          - ((double)hgt[ym + xm] + 2 * hgt[y0 + xm] + hgt[yp + xm]);
                double dy = ((double)hgt[yp + xm] + 2 * hgt[yp + x] + hgt[yp + xp])
                          - ((double)hgt[ym + xm] + 2 * hgt[ym + x] + hgt[ym + xp]);
                double nx = -dx * k, ny = dy * k;
                double inv = 1 / Math.Sqrt(nx * nx + ny * ny + 1);
                int p = (y0 + x) * 4;
                o[p] = Clamped((nx * inv * 0.5 + 0.5) * 255);
                o[p + 1] = Clamped((ny * inv * 0.5 + 0.5) * 255);
                o[p + 2] = Clamped((inv * 0.5 + 0.5) * 255);
                o[p + 3] = 255;
            }
        }
        return o;
    }

    // Uint8ClampedArray : borné à [0, 255], arrondi au plus proche, égalités au pair
    static byte Clamped(double v) =>
        (byte)Math.Max(0, Math.Min(255, Math.Round(v, MidpointRounding.ToEven)));
}
