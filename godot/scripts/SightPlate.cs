using Godot;
using System;

namespace NavalSim;

/// <summary>
/// CE QU'ON VOIT À L'INSTRUMENT, dessiné par-dessus la vue (ShipDemo.Sight.cs).
///
/// LE QUARTIER DE DAVIS (le soleil) : on lui tourne le dos ; par la fente du viseur
/// d'horizon on voit la mer, et sur sa plaque tombe l'OMBRE du viseur de soleil — une
/// barre de lumière floue, large comme le disque du soleil. Quand l'ombre et l'horizon
/// sont ensemble dans la fente, l'arc donne la hauteur.
///
/// L'ARBALESTRILLE (la Polaire) : on vise entre l'horizon et l'étoile ; le marteau, le
/// bâton en travers, a son bout bas sur l'horizon et son bout haut sur l'étoile.
///
/// Tout s'écrit en DEGRÉS au-dessus de la ligne de visée, convertis ici en pixels par le
/// champ de la caméra : ce que la plaque montre est ce que l'œil verrait.
/// </summary>
public partial class SightPlate : Control
{
    /// <summary>Vrai : le quartier de Davis (le soleil) ; faux : l'arbalestrille (la Polaire).</summary>
    public bool Sun = true;
    /// <summary>Les pixels d'un degré, au centre de l'écran.</summary>
    public float PxPerDeg = 90;
    /// <summary>Le soleil (son ombre) ou l'étoile, en degrés au-dessus de la ligne de visée — ce qui bouge.</summary>
    public float MarkDeg;
    /// <summary>L'arbalestrille : le bout haut du marteau, en degrés au-dessus du bout bas (le réglage).</summary>
    public float SetDeg;
    public string Top = "", Readout = "", Hint = "";
    /// <summary>Dessiner l'étoile : seulement quand le ciel n'a pas les vraies (StarMap) — sinon c'est la Polaire du ciel qu'on vise.</summary>
    public bool DrawStar = true;

    public override void _Draw()
    {
        var sz = Size;
        var c = sz * 0.5f;
        var ink = new Color(0.05f, 0.04f, 0.03f, 0.62f);
        var wood = new Color(0.32f, 0.21f, 0.12f, 0.97f);
        var brass = new Color(0.86f, 0.70f, 0.38f, 0.95f);
        if (Sun)
        {
            // les deux joues du viseur d'horizon, et la fente entre elles
            float slit = 70;
            DrawRect(new Rect2(0, 0, c.X - slit, sz.Y), ink);
            DrawRect(new Rect2(c.X + slit, 0, sz.X - c.X - slit, sz.Y), ink);
            DrawRect(new Rect2(c.X - slit - 14, 0, 14, sz.Y), wood);
            DrawRect(new Rect2(c.X + slit, 0, 14, sz.Y), wood);
            // l'OMBRE du soleil sur la plaque : une bande lumineuse, floue sur ses bords (le disque fait un demi-degré)
            float y = c.Y - MarkDeg * PxPerDeg;
            float half = 0.25f * PxPerDeg;
            for (int k = 0; k < 6; k++)
            {
                float w = half * (1 + k * 0.35f);
                DrawRect(new Rect2(c.X - slit - 14, y - w, 2 * slit + 28, 2 * w), new Color(1f, 0.86f, 0.55f, 0.16f));
            }
            DrawRect(new Rect2(c.X - slit - 14, y - 1, 2 * slit + 28, 2), new Color(1f, 0.95f, 0.8f, 0.9f));
            // le repère du milieu de la fente
            DrawLine(new Vector2(c.X - slit - 26, c.Y), new Vector2(c.X - slit - 14, c.Y), brass, 2);
            DrawLine(new Vector2(c.X + slit + 14, c.Y), new Vector2(c.X + slit + 26, c.Y), brass, 2);
        }
        else
        {
            // le MARTEAU de l'arbalestrille : un bâton en travers, son bout bas au milieu (l'horizon), son bout haut au réglage
            // le marteau est tenu en travers de la visée : ses deux bouts à ± la moitié du réglage
            float f = PxPerDeg * 57.29578f;
            float y0 = c.Y + f * MathF.Tan(SetDeg * 0.5f * MathF.PI / 180), y1 = c.Y - f * MathF.Tan(SetDeg * 0.5f * MathF.PI / 180);
            DrawLine(new Vector2(c.X, y0 + 10), new Vector2(c.X, y1 - 10), wood, 9);
            DrawLine(new Vector2(c.X - 18, y0), new Vector2(c.X + 18, y0), brass, 3);
            DrawLine(new Vector2(c.X - 18, y1), new Vector2(c.X + 18, y1), brass, 3);
            // l'étoile, si le ciel n'a pas les vraies : un point à sa place
            if (DrawStar)
            {
                float ys = c.Y - PxPerDeg * 57.29578f * MathF.Tan(MarkDeg * MathF.PI / 180);
                DrawCircle(new Vector2(c.X, ys), 3.2f, new Color(0.95f, 0.97f, 1f, 1f));
                DrawCircle(new Vector2(c.X, ys), 7f, new Color(0.8f, 0.85f, 1f, 0.18f));
            }
            // ce qu'on vise : l'étoile doit venir au bout haut
            DrawLine(new Vector2(c.X + 18, y1), new Vector2(c.X + 30, y1), new Color(brass, 0.5f), 1);
        }
        var font = ThemeDB.FallbackFont;
        DrawString(font, new Vector2(24, 40), Top, HorizontalAlignment.Left, sz.X - 48, 18, new Color(1, 0.96f, 0.86f));
        DrawString(font, new Vector2(24, sz.Y - 60), Readout, HorizontalAlignment.Left, sz.X - 48, 26, new Color(1, 0.92f, 0.7f));
        DrawString(font, new Vector2(24, sz.Y - 28), Hint, HorizontalAlignment.Left, sz.X - 48, 16, new Color(0.9f, 0.9f, 0.85f));
    }
}
