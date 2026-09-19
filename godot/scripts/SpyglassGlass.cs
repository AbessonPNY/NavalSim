using Godot;
using System;
using System.Collections.Generic;

namespace NavalSim;

/// <summary>
/// UN VERRE QUI A VU LA MER — Spyglass.glassTexture de la page, dessiné une fois.
/// Quatre calques dans quatre canaux, chacun lu autrement par l'oculaire : les
/// rayures (r) accrochent la lumière, la trace de pouce (g) blanchit l'image, la
/// poussière (b) l'ombre, et la fêlure (a) brille ET plie l'image.
///
/// La page les dessine sur un canvas ; ici un petit traceur fait les mêmes gestes
/// — arcs, courbes, ellipses, dégradés, points, lignes brisées —, avec le même
/// générateur (Lehmer, graine 1597) appelé dans le même ordre : c'est le même
/// verre, rayure pour rayure. Chaque trait est composé comme le canvas le fait,
/// blanc sur noir à l'alpha du geste, sa couverture prise d'une seule pièce pour
/// qu'une ligne brisée ne s'épaississe pas à ses coudes.
/// </summary>
public static class SpyglassGlass
{
    const int S = 1024;
    static ImageTexture? _tex;

    public static ImageTexture Texture => _tex ??= Build();

    static ImageTexture Build()
    {
        double seed = 1597;
        double Rnd() => (seed = (seed * 16807) % 2147483647) / 2147483647;

        // --- les rayures ---
        var scratches = new float[S * S];
        for (int i = 0; i < 220; i++)
        {
            // tournantes de polissage : de courts arcs autour du centre, tous dans le même sens
            double rad = S * 0.05 + Rnd() * S * 0.47, a0 = Rnd() * Math.PI * 2;
            double alpha = 0.05 + Rnd() * 0.12, w = 0.6 + Rnd() * 0.8;
            double a1 = a0 + 0.05 + Rnd() * 0.35;
            Stroke(scratches, Arc(S / 2.0, S / 2.0, rad, a0, a1), w, alpha);
        }
        for (int i = 0; i < 38; i++)
        {
            // et les franches, droites et n'importe où
            double x = Rnd() * S, y = Rnd() * S, a = Rnd() * Math.PI, l = S * (0.03 + Rnd() * Rnd() * 0.5);
            double alpha = 0.12 + Rnd() * 0.35, w = 0.7 + Rnd() * 1.3;
            double cx = x + Math.Cos(a) * l * 0.5 + (Rnd() - 0.5) * 20;
            double cy = y + Math.Sin(a) * l * 0.5 + (Rnd() - 0.5) * 20;
            Stroke(scratches, Quad(x, y, cx, cy, x + Math.Cos(a) * l, y + Math.Sin(a) * l), w, alpha);
        }

        // --- la trace de pouce ---
        var smudge = new float[S * S];
        for (int i = 0; i < 7; i++)
        {
            double x = S * (0.2 + Rnd() * 0.6), y = S * (0.2 + Rnd() * 0.6), rad = S * (0.08 + Rnd() * 0.18);
            double a0 = 0.25 + Rnd() * 0.35;
            Radial(smudge, x, y, rad, a0);
        }
        // les crêtes d'un pouce, d'un côté, là où un pouce se pose
        for (int i = 0; i < 16; i++)
            Stroke(smudge, Ellipse(S * 0.72, S * 0.30, 20 + i * 7, 14 + i * 5, 0.6), 3, 0.10);

        // --- la poussière ---
        var dust = new float[S * S];
        for (int i = 0; i < 90; i++)
        {
            double alpha = 0.25 + Rnd() * 0.6;
            double x = Rnd() * S, y = Rnd() * S, r = 0.8 + Rnd() * Rnd() * 4;
            Disc(dust, x, y, r, alpha);
        }

        // --- la fêlure : partie d'un éclat du bord, elle entre et fourche une fois ---
        var crack = new float[S * S];
        (double, double, double) Run(double x, double y, double a, double len, double w)
        {
            var pts = new List<(double, double)> { (x, y) };
            for (double s = 0; s < len; s += 14)
            {
                a += (Rnd() - 0.5) * 0.5;
                x += Math.Cos(a) * 14; y += Math.Sin(a) * 14;
                pts.Add((x, y));
            }
            Stroke(crack, pts, w, 0.9);
            return (x, y, a);
        }
        double c0 = 3.75, rx = S / 2.0 + Math.Cos(c0) * S * 0.49, ry = S / 2.0 + Math.Sin(c0) * S * 0.49;
        var (x1, y1, a1c) = Run(rx, ry, c0 + Math.PI + 0.25, S * 0.16, 1.6);
        Run(x1, y1, a1c + 0.6, S * 0.10, 1.1);
        Run(x1, y1, a1c - 0.4, S * 0.07, 0.9);
        Disc(crack, rx, ry, 9, 1);                                  // l'éclat

        /* En octets bruts, comme la page : la fêlure vit dans l'alpha comme une
           DONNÉE, et une image prémultipliée rendrait les trois autres canaux à
           zéro partout où le verre est intact. Arrondis comme getImageData. */
        var d = new byte[S * S * 4];
        static byte B(float v) => (byte)Math.Clamp((int)Math.Round(v * 255), 0, 255);
        for (int i = 0; i < S * S; i++)
        {
            d[i * 4] = B(scratches[i]); d[i * 4 + 1] = B(smudge[i]); d[i * 4 + 2] = B(dust[i]); d[i * 4 + 3] = B(crack[i]);
        }
        var img = Image.CreateFromData(S, S, false, Image.Format.Rgba8, d);
        img.GenerateMipmaps();
        return ImageTexture.CreateFromImage(img);
    }

    /* ------------------------------------------------------------------ */
    /*  LE TRACEUR : les gestes du canvas, blanc sur noir                  */
    /* ------------------------------------------------------------------ */

    // le canvas pose le blanc à l'alpha du geste : v ← v + (1 − v)·a
    static void Over(float[] buf, int i, double a) => buf[i] = (float)(buf[i] + (1 - buf[i]) * a);

    /* Un trait : chaque segment porte sa couverture dans un tampon du trait (le
       plus fort l'emporte), puis le trait se compose d'une pièce. Bouts ronds. */
    static void Stroke(float[] buf, List<(double X, double Y)> pts, double w, double alpha)
    {
        double half = w * 0.5, reach = half + 1;
        double x0 = double.MaxValue, y0 = double.MaxValue, x1 = double.MinValue, y1 = double.MinValue;
        foreach (var (x, y) in pts) { x0 = Math.Min(x0, x); y0 = Math.Min(y0, y); x1 = Math.Max(x1, x); y1 = Math.Max(y1, y); }
        int bx0 = Math.Max(0, (int)Math.Floor(x0 - reach)), by0 = Math.Max(0, (int)Math.Floor(y0 - reach));
        int bx1 = Math.Min(S - 1, (int)Math.Ceiling(x1 + reach)), by1 = Math.Min(S - 1, (int)Math.Ceiling(y1 + reach));
        if (bx1 < bx0 || by1 < by0) return;
        int bw = bx1 - bx0 + 1, bh = by1 - by0 + 1;
        var cov = new float[bw * bh];
        // un trait plus fin qu'un pixel en couvre une part
        double thin = Math.Min(1, w);
        for (int k = 0; k + 1 < pts.Count || (pts.Count == 1 && k == 0); k++)
        {
            var (ax, ay) = pts[k];
            var (qx, qy) = pts.Count == 1 ? pts[0] : pts[k + 1];
            int sx0 = Math.Max(bx0, (int)Math.Floor(Math.Min(ax, qx) - reach)), sx1 = Math.Min(bx1, (int)Math.Ceiling(Math.Max(ax, qx) + reach));
            int sy0 = Math.Max(by0, (int)Math.Floor(Math.Min(ay, qy) - reach)), sy1 = Math.Min(by1, (int)Math.Ceiling(Math.Max(ay, qy) + reach));
            double dx = qx - ax, dy = qy - ay, ll = dx * dx + dy * dy;
            for (int y = sy0; y <= sy1; y++)
                for (int x = sx0; x <= sx1; x++)
                {
                    double px = x + 0.5, py = y + 0.5;
                    double t = ll > 0 ? Math.Clamp(((px - ax) * dx + (py - ay) * dy) / ll, 0, 1) : 0;
                    double ex = px - ax - t * dx, ey = py - ay - t * dy;
                    double c = Math.Clamp(Math.Max(half, 0.5) + 0.5 - Math.Sqrt(ex * ex + ey * ey), 0, 1) * thin;
                    int j = (y - by0) * bw + (x - bx0);
                    if (c > cov[j]) cov[j] = (float)c;
                }
        }
        for (int y = 0; y < bh; y++)
            for (int x = 0; x < bw; x++)
            {
                float c = cov[y * bw + x];
                if (c > 0) Over(buf, (y + by0) * S + x + bx0, alpha * c);
            }
    }

    static void Disc(float[] buf, double cx, double cy, double r, double alpha)
    {
        int x0 = Math.Max(0, (int)Math.Floor(cx - r - 1)), x1 = Math.Min(S - 1, (int)Math.Ceiling(cx + r + 1));
        int y0 = Math.Max(0, (int)Math.Floor(cy - r - 1)), y1 = Math.Min(S - 1, (int)Math.Ceiling(cy + r + 1));
        for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                double dx = x + 0.5 - cx, dy = y + 0.5 - cy;
                double c = Math.Clamp(r + 0.5 - Math.Sqrt(dx * dx + dy * dy), 0, 1);
                if (c > 0) Over(buf, y * S + x, alpha * c);
            }
    }

    // un dégradé radial, blanc à a0 au centre jusqu'à rien au rayon
    static void Radial(float[] buf, double cx, double cy, double rad, double a0)
    {
        int x0 = Math.Max(0, (int)Math.Floor(cx - rad)), x1 = Math.Min(S - 1, (int)Math.Ceiling(cx + rad));
        int y0 = Math.Max(0, (int)Math.Floor(cy - rad)), y1 = Math.Min(S - 1, (int)Math.Ceiling(cy + rad));
        for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                double dx = x + 0.5 - cx, dy = y + 0.5 - cy, f = Math.Sqrt(dx * dx + dy * dy) / rad;
                if (f < 1) Over(buf, y * S + x, a0 * (1 - f));
            }
    }

    static List<(double, double)> Arc(double cx, double cy, double r, double a0, double a1)
    {
        int n = Math.Max(2, (int)Math.Ceiling(Math.Abs(a1 - a0) * r / 3));
        var p = new List<(double, double)>(n + 1);
        for (int i = 0; i <= n; i++)
        {
            double a = a0 + (a1 - a0) * i / n;
            p.Add((cx + Math.Cos(a) * r, cy + Math.Sin(a) * r));
        }
        return p;
    }

    static List<(double, double)> Quad(double x0, double y0, double cx, double cy, double x1, double y1)
    {
        const int n = 24;
        var p = new List<(double, double)>(n + 1);
        for (int i = 0; i <= n; i++)
        {
            double t = (double)i / n, u = 1 - t;
            p.Add((u * u * x0 + 2 * u * t * cx + t * t * x1, u * u * y0 + 2 * u * t * cy + t * t * y1));
        }
        return p;
    }

    static List<(double, double)> Ellipse(double cx, double cy, double rx, double ry, double rot)
    {
        int n = Math.Max(24, (int)Math.Ceiling(Math.PI * (rx + ry) / 3));
        var p = new List<(double, double)>(n + 1);
        double c = Math.Cos(rot), s = Math.Sin(rot);
        for (int i = 0; i <= n; i++)
        {
            double a = 2 * Math.PI * i / n, ex = Math.Cos(a) * rx, ey = Math.Sin(a) * ry;
            p.Add((cx + ex * c - ey * s, cy + ex * s + ey * c));
        }
        return p;
    }
}
