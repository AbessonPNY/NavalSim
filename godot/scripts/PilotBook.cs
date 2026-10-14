using Godot;
using System;

namespace NavalSim;

/// <summary>
/// LE LIVRE DU PILOTE, ouvert (ShipDemo.PilotBook.cs) : à gauche la table qu'on consulte,
/// à droite le calcul de la dernière hauteur.
///
/// Deux tables, comme dans les « régiments » imprimés depuis le XVIᵉ siècle : le RÉGIMENT
/// DU SOLEIL, la déclinaison jour par jour du mois ; et la RÈGLE DE L'ÉTOILE DU NORD, ce
/// qu'il faut ajouter ou ôter à la hauteur de la Polaire selon où est la Garde, en huit
/// places nommées comme dans les régiments ibériques (la tête, les bras, les lignes, les
/// pieds) — sans la figure humaine qu'ils dessinaient au milieu : seulement les mots.
///
/// Dessiné tant que livre.image est vide (instruments/instruments.json) ; les rectangles
/// gauche et droite disent où l'on écrit sur l'image.
/// </summary>
public partial class PilotBook : Control
{
    public SightInstrument.Book? Geo;
    public Texture2D? Picture;
    /// <summary>0 : le régiment du soleil ; 1 : la règle de l'étoile du Nord.</summary>
    public int Page;

    public string RegTitle = "";
    public (string Day, string Decl)[] RegRows = Array.Empty<(string, string)>();
    public int RegToday = -1;

    public (string Where, string Short, string Corr)[] Guards = Array.Empty<(string, string, string)>();
    /// <summary>La Garde et la Polaire autour du pôle, en angle d'écran, à cette heure.</summary>
    public float GuardDeg, PolarisDeg;
    public string GuardNow = "", PoleNote = "";

    public string RightTitle = "", RightText = "";

    static readonly Color Leather = new(0.27f, 0.13f, 0.07f, 1f), Paper = new(0.93f, 0.88f, 0.75f, 1f);
    static readonly Color Ink = new(0.16f, 0.10f, 0.05f, 1f), Red = new(0.62f, 0.10f, 0.06f, 1f), Faint = new(0.16f, 0.10f, 0.05f, 0.35f);

    static Vector2 U(float deg) => new(MathF.Cos(deg * MathF.PI / 180), MathF.Sin(deg * MathF.PI / 180));

    public override void _Draw()
    {
        var g = Geo;
        if (g == null || g.LSize.X <= 0) return;
        var sz = Size;
        DrawRect(new Rect2(Vector2.Zero, sz), new Color(0, 0, 0, 0.78f));
        float w = MathF.Min(sz.X * 0.9f, sz.Y * 0.86f * g.LSize.X / g.LSize.Y);
        float k = w / g.LSize.X;
        var o = (sz - g.LSize * k) * 0.5f - new Vector2(0, 10);
        Rect2 R(Rect2 r) => new(o + r.Position * k, r.Size * k);
        var left = R(g.LLeft); var right = R(g.LRight);
        if (Picture != null) DrawTextureRect(Picture, new Rect2(o, g.LSize * k), false);
        else
        {
            DrawRect(new Rect2(o, g.LSize * k), Leather);
            foreach (var p in new[] { left, right })
                DrawRect(p.Grow(40 * k), Paper);
            // the gutter: the pages curve down into the sewing
            float gx = (left.End.X + right.Position.X) * 0.5f;
            for (int i = 0; i < 8; i++)
                DrawRect(new Rect2(gx - (8 - i) * 9 * k, left.Position.Y - 40 * k, 2 * (8 - i) * 9 * k, left.Size.Y + 80 * k), new Color(0.3f, 0.2f, 0.1f, 0.05f));
        }
        if (Page == 0) Regiment(left, k); else Guard(left, k);
        Right(right, k);
        var font = ThemeDB.FallbackFont;
        DrawString(font, new Vector2(0, o.Y + g.LSize.Y * k + 30), "← → : l'autre table  ·  Échap, Entrée ou clic : fermer le livre",
            HorizontalAlignment.Center, sz.X, 16, new Color(0.95f, 0.92f, 0.85f));
    }

    void Title(Rect2 p, float k, string title, string sub)
    {
        var font = ThemeDB.FallbackFont;
        DrawString(font, p.Position + new Vector2(0, 46 * k), title, HorizontalAlignment.Center, p.Size.X, (int)MathF.Max(14, 48 * k), Red);
        DrawMultilineString(font, p.Position + new Vector2(0, 92 * k), sub, HorizontalAlignment.Center, p.Size.X, (int)MathF.Max(11, 26 * k), -1, Ink);
        DrawLine(p.Position + new Vector2(p.Size.X * 0.2f, 150 * k), p.Position + new Vector2(p.Size.X * 0.8f, 150 * k), Faint, MathF.Max(1, 2 * k));
    }

    void Regiment(Rect2 p, float k)
    {
        var font = ThemeDB.FallbackFont;
        Title(p, k, "Régiment du soleil", RegTitle + " — la déclinaison à midi, jour par jour.\nN : au nord de l'équateur ; S : au sud.");
        int n = RegRows.Length, half = (n + 1) / 2;
        float top = p.Position.Y + 200 * k, rowH = (p.End.Y - top - 20 * k) / (half + 1);
        int fs = (int)MathF.Max(11, 28 * k);
        for (int col = 0; col < 2; col++)
        {
            float x0 = p.Position.X + col * p.Size.X * 0.5f, cw = p.Size.X * 0.5f;
            DrawString(font, new Vector2(x0, top), "Jours", HorizontalAlignment.Center, cw * 0.3f, fs, Red);
            DrawString(font, new Vector2(x0 + cw * 0.3f, top), "Déclinaison", HorizontalAlignment.Center, cw * 0.62f, fs, Red);
            for (int i = 0; i < half; i++)
            {
                int r = col * half + i;
                if (r >= n) break;
                float y = top + (i + 1) * rowH;
                if (r == RegToday)
                {
                    DrawRect(new Rect2(x0 + cw * 0.04f, y - rowH * 0.78f, cw * 0.9f, rowH * 0.95f), new Color(Red, 0.13f));
                    DrawString(font, new Vector2(x0 - fs * 0.2f, y), "☞", HorizontalAlignment.Left, -1, fs, Red);
                }
                DrawString(font, new Vector2(x0, y), RegRows[r].Day, HorizontalAlignment.Center, cw * 0.3f, fs, Ink);
                DrawString(font, new Vector2(x0 + cw * 0.3f, y), RegRows[r].Decl, HorizontalAlignment.Center, cw * 0.62f, fs, Ink);
            }
        }
        DrawLine(new Vector2(p.Position.X + p.Size.X * 0.5f, top + rowH * 0.3f), new Vector2(p.Position.X + p.Size.X * 0.5f, p.End.Y - 20 * k), Faint, MathF.Max(1, 2 * k));
    }

    void Guard(Rect2 p, float k)
    {
        var font = ThemeDB.FallbackFont;
        Title(p, k, "Règle de l'étoile du Nord", PoleNote);
        var c = new Vector2(p.Position.X + p.Size.X * 0.5f, p.Position.Y + 420 * k);
        float r = MathF.Min(p.Size.X * 0.27f, 230 * k);
        int fs = (int)MathF.Max(10, 23 * k);
        DrawArc(c, r, 0, MathF.Tau, 96, Ink, MathF.Max(1, 2.5f * k), true);
        DrawArc(c, r * 0.22f, 0, MathF.Tau, 48, Faint, MathF.Max(1, 1.5f * k), true);
        // the pole, a small cross; the Pole Star off it, its distance drawn larger than life
        DrawLine(c - new Vector2(10, 0) * k, c + new Vector2(10, 0) * k, Ink, MathF.Max(1, 2 * k));
        DrawLine(c - new Vector2(0, 10) * k, c + new Vector2(0, 10) * k, Ink, MathF.Max(1, 2 * k));
        var pp = c + U(PolarisDeg) * r * 0.22f;
        Star(pp, 7 * k, Ink);
        int small = (int)MathF.Max(9, 18 * k);
        DrawString(font, c + new Vector2(-60 * k, 32 * k), "le pôle", HorizontalAlignment.Center, 120 * k, small, Faint * 2.4f);
        DrawString(font, pp + U(PolarisDeg) * 16 * k + new Vector2(-60 * k, -6 * k), "la Polaire", HorizontalAlignment.Center, 120 * k, small, Ink);
        for (int i = 0; i < Guards.Length; i++)
        {
            var u = U(-90 - 45 * i);
            DrawLine(c + u * r * 0.93f, c + u * r * 1.07f, Ink, MathF.Max(1, 3 * k));
            var at = c + u * (r + 34 * k + fs * 2.8f * MathF.Abs(u.X));
            DrawString(font, at + new Vector2(-120 * k, fs * 0.35f), Guards[i].Short, HorizontalAlignment.Center, 240 * k, fs, Ink);
        }
        // the Guard, where it stands tonight
        var gp = c + U(GuardDeg) * r;
        Star(gp, 11 * k, Red);
        DrawString(font, new Vector2(p.Position.X, c.Y + r + 90 * k), GuardNow, HorizontalAlignment.Center, p.Size.X, (int)MathF.Max(11, 26 * k), Red);
        // the eight rules, written out
        float y = c.Y + r + 150 * k, rowH = (p.End.Y - y) / (Guards.Length + 0.5f);
        for (int i = 0; i < Guards.Length; i++)
        {
            DrawString(font, new Vector2(p.Position.X + 20 * k, y + i * rowH + rowH * 0.6f), Guards[i].Where, HorizontalAlignment.Left, p.Size.X * 0.62f, fs, Ink);
            DrawString(font, new Vector2(p.Position.X + p.Size.X * 0.62f, y + i * rowH + rowH * 0.6f), Guards[i].Corr, HorizontalAlignment.Right, p.Size.X * 0.36f, fs, Ink);
        }
    }

    /// <summary>Une étoile à huit branches, comme on les gravait.</summary>
    void Star(Vector2 at, float r, Color col)
    {
        var pts = new Vector2[16];
        for (int i = 0; i < 16; i++) pts[i] = at + U(i * 22.5f - 90) * (i % 2 == 0 ? r : r * 0.4f);
        DrawColoredPolygon(pts, col);
    }

    void Right(Rect2 p, float k)
    {
        var font = ThemeDB.FallbackFont;
        DrawString(font, p.Position + new Vector2(0, 46 * k), RightTitle, HorizontalAlignment.Center, p.Size.X, (int)MathF.Max(14, 44 * k), Red);
        DrawLine(p.Position + new Vector2(p.Size.X * 0.2f, 80 * k), p.Position + new Vector2(p.Size.X * 0.8f, 80 * k), Faint, MathF.Max(1, 2 * k));
        DrawMultilineString(font, p.Position + new Vector2(10 * k, 140 * k), RightText, HorizontalAlignment.Left, p.Size.X - 20 * k,
            (int)MathF.Max(12, 29 * k), -1, Ink);
    }
}
