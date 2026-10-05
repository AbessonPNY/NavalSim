using Godot;
using System;
using NavalSim.Core;

namespace NavalSim;

/// <summary>
/// LA CARTE DU CIEL, peinte une fois : chaque étoile du catalogue (StarCatalog, à
/// l'année de la partie) en tache sur une image en COORDONNÉES CÉLESTES — ascension droite
/// en largeur, déclinaison en hauteur (une équirectangulaire). Le dôme la lit en tournant
/// chaque direction du ciel en ascension droite et déclinaison (sky.gdshaderinc,
/// NAVAL_STAR_MAP) : le ciel tourne d'un produit matriciel par pixel, rien à repeindre.
///
/// L'ÉCLAT : une étoile de première grandeur est un point vif et un peu plus large, une de
/// cinquième un grain à peine là — l'œil ne voit pas les magnitudes, il voit des tailles et
/// des éclats. LA COULEUR suit l'indice B−V : Rigel bleutée, Bételgeuse et Antarès orangées.
/// Une tache s'étire en largeur vers les pôles comme la projection l'étire, pour rester ronde
/// dans le ciel.
/// </summary>
public static class StarMap
{
    public const int W = 4096, H = 2048;

    public static ImageTexture Build(StarCatalog cat)
    {
        var img = Image.CreateEmpty(W, H, false, Image.Format.Rgb8);
        img.Fill(Colors.Black);
        var buf = new float[W * H * 3];
        foreach (var s in cat.Stars)
        {
            // l'éclat au centre, et la largeur : les brillantes plus vives ET un peu plus grosses
            double m = s.Mag;
            // le flux vrai : une magnitude, c'est 2,512 fois moins de lumière (borné : Sirius ne doit pas aveugler)
            float peak = (float)Math.Clamp(Math.Pow(10, -0.4 * (m - 1.5)), 0.02, 3.0);
            float sig = (float)(0.75 + 0.35 * Math.Clamp((2.5 - m) / 4, 0, 1));
            var col = Tint(s.BV);
            double u = s.Ra / 360.0 * W, v = (90 - s.Dec) / 180.0 * H;
            double stretch = 1 / Math.Max(0.05, Math.Cos(s.Dec * Math.PI / 180));
            int ru = (int)Math.Ceiling(3 * sig * stretch), rv = (int)Math.Ceiling(3 * sig);
            for (int dy = -rv; dy <= rv; dy++)
            {
                int y = (int)Math.Floor(v) + dy;
                if (y < 0 || y >= H) continue;
                double fy = (y + 0.5 - v) / sig;
                for (int dx = -ru; dx <= ru; dx++)
                {
                    int x = (int)Math.Floor(u) + dx;
                    double fx = (x + 0.5 - u) / (sig * stretch);
                    float g = peak * (float)Math.Exp(-0.5 * (fx * fx + fy * fy));
                    if (g < 0.004f) continue;
                    int xx = ((x % W) + W) % W;
                    int i = (y * W + xx) * 3;
                    buf[i] += col.R * g; buf[i + 1] += col.G * g; buf[i + 2] += col.B * g;
                }
            }
        }
        var bytes = new byte[W * H * 3];
        /* ENCODÉ COMME UNE COULEUR (sRGB) : le dôme la relit linéaire (source_color), ce qui rend aux
           faibles leur peu de lumière sans les écraser à zéro sur huit bits. L'échelle : 3, Sirius. */
        for (int i = 0; i < bytes.Length; i++) bytes[i] = (byte)Math.Clamp((int)(255 * Math.Pow(Math.Clamp(buf[i] / 3.0, 0, 1), 1 / 2.2)), 0, 255);
        img.SetData(W, H, false, Image.Format.Rgb8, bytes);
        return ImageTexture.CreateFromImage(img);
    }

    /// <summary>La couleur d'une étoile par son indice B−V, adoucie : l'œil voit des teintes, pas des spectres.</summary>
    static Color Tint(double bv)
    {
        float t = (float)Math.Clamp((bv + 0.3) / 2.0, 0, 1);       // −0,3 bleutée … 1,7 orangée
        var blue = new Color(0.78f, 0.86f, 1.0f);
        var white = new Color(1.0f, 0.98f, 0.95f);
        var orange = new Color(1.0f, 0.78f, 0.55f);
        return t < 0.3f ? blue.Lerp(white, t / 0.3f) : white.Lerp(orange, (t - 0.3f) / 0.7f);
    }
}
