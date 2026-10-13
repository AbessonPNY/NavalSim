using Godot;
using System;

namespace NavalSim;

/// <summary>
/// LE COFFRE DE L'INVENTAIRE, ouvert et vu de dessus : le couvercle rabattu au
/// fond, la caisse devant, et dedans trois rangées de huit cases.
///
/// DESSINÉ, en attendant une image (`coffre.dessus` dans treasure/tresor.json) :
/// quand elle viendra, elle remplacera tout le bois et le fer, et les cases
/// resteront posées aux mêmes places — c'est la seule chose que l'image devra
/// respecter, et <see cref="SlotRect"/> la dit.
/// </summary>
public partial class InventoryChest : Control
{
    public const int Cols = 8, Rows = 3;

    public struct Slot
    {
        public string Name, Tip;
        public long Count;
        /// <summary>De la monnaie : son nombre se lit toujours, même seul.</summary>
        public bool Money;
        public Texture2D? Icon;
    }

    /// <summary>Ce que porte chaque case ; null : vide.</summary>
    public readonly Slot?[] Slots = new Slot?[Cols * Rows];
    /// <summary>Le coffre en image, s'il y en a une.</summary>
    public Texture2D? Top;
    public string Title = "", Footer = "";

    readonly Control[] _tips = new Control[Cols * Rows];

    // la géométrie, en unités d'une case (s) : jeu entre cases, bord de la caisse, couvercle
    const float Gap = 0.14f, Rim = 0.45f, LidH = 1.9f, LidNarrow = 0.9f;
    static float BoxW => Cols + (Cols + 1) * Gap + 2 * Rim;
    static float BoxH => Rows + (Rows + 1) * Gap + 2 * Rim;

    static readonly Color Wood = new(0.42f, 0.24f, 0.12f), Seam = new(0.16f, 0.09f, 0.045f),
        Divider = new(0.50f, 0.31f, 0.16f), Velvet = new(0.27f, 0.06f, 0.07f),
        Iron = new(0.16f, 0.16f, 0.17f), IronLit = new(0.34f, 0.33f, 0.32f),
        Ink = new(0.93f, 0.86f, 0.68f);

    public InventoryChest()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        for (int i = 0; i < _tips.Length; i++)
        {
            // une case invisible par emplacement, pour la bulle d'aide au survol
            _tips[i] = new Control { MouseFilter = MouseFilterEnum.Pass };
            AddChild(_tips[i]);
        }
    }

    /// <summary>La taille d'une case et le coin haut gauche de la CAISSE (sous le couvercle).</summary>
    (float s, Vector2 box) Frame()
    {
        var r = Size;
        float top = r.Y * 0.12f, bottom = r.Y * 0.08f;
        float s = Math.Min(r.X * 0.86f / BoxW, (r.Y - top - bottom) / (BoxH + LidH));
        float total = (BoxH + LidH) * s;
        float y0 = top + (r.Y - top - bottom - total) * 0.5f + LidH * s;
        return (s, new Vector2((r.X - BoxW * s) * 0.5f, y0));
    }

    /// <summary>La case (colonne, rangée) à l'écran, rangée 0 au fond.</summary>
    public Rect2 SlotRect(int col, int row)
    {
        var (s, box) = Frame();
        float x = box.X + (Rim + Gap + col * (1 + Gap)) * s;
        float y = box.Y + (Rim + Gap + row * (1 + Gap)) * s;
        return new Rect2(x, y, s, s);
    }

    public void Refresh()
    {
        for (int i = 0; i < _tips.Length; i++)
        {
            var r = SlotRect(i % Cols, i / Cols);
            _tips[i].Position = r.Position;
            _tips[i].Size = r.Size;
            _tips[i].TooltipText = Slots[i] is { } sl ? sl.Tip : "";
        }
        QueueRedraw();
    }

    public override void _Notification(int what)
    {
        if (what == NotificationResized) Refresh();
    }

    /// <summary>Un tirage stable de 0 à 1 : le fil du bois ne doit pas bouger d'une image à l'autre.</summary>
    static float Hash(int a, int b)
    {
        uint h = (uint)(a * 73856093) ^ (uint)(b * 19349663);
        h ^= h >> 13; h *= 0x5bd1e995; h ^= h >> 15;
        return (h & 0xFFFF) / 65535f;
    }

    static Color Shade(Color c, float k) => new(c.R * k, c.G * k, c.B * k, c.A);

    public override void _Draw()
    {
        var font = HandFont.Get() ?? ThemeDB.FallbackFont;
        var (s, box) = Frame();
        float bw = BoxW * s, bh = BoxH * s;

        if (Title.Length > 0)
            DrawString(font, new Vector2(0, Size.Y * 0.06f), Title, HorizontalAlignment.Center, Size.X, (int)(Size.Y * 0.04f), Ink);
        if (Footer.Length > 0)
            DrawString(font, new Vector2(0, Size.Y * 0.965f), Footer, HorizontalAlignment.Center, Size.X, (int)(Size.Y * 0.022f), Shade(Ink, 0.8f));

        if (Top != null)
            // l'image couvre le couvercle et la caisse ; les cases restent où le dessin les met
            DrawTextureRect(Top, new Rect2(box.X, box.Y - LidH * s, bw, bh + LidH * s), false);
        else
            DrawChest(s, box, bw, bh);

        for (int i = 0; i < Slots.Length; i++)
        {
            if (Slots[i] is not { } sl) continue;
            var r = SlotRect(i % Cols, i / Cols);
            if (sl.Icon != null)
                DrawTextureRect(sl.Icon, r.Grow(-s * 0.08f), false);
            else
            {
                // pas encore d'image : le nom, à la main, comme sur une étiquette
                int fs = (int)(s * 0.19f);
                float w = s * 0.9f;
                var sz = font.GetMultilineStringSize(sl.Name, HorizontalAlignment.Center, w, fs);
                var at = new Vector2(r.Position.X + (s - w) * 0.5f, r.GetCenter().Y - sz.Y * 0.5f + font.GetAscent(fs));
                DrawMultilineString(font, at, sl.Name, HorizontalAlignment.Center, w, fs, 3, Ink);
            }
            if (sl.Count > 1 || sl.Money)
            {
                int fc = (int)(s * 0.2f);
                DrawString(font, new Vector2(r.Position.X + s * 0.05f + 1, r.End.Y - s * 0.07f + 1), "×" + sl.Count,
                    HorizontalAlignment.Right, s * 0.9f, fc, new Color(0, 0, 0, 0.8f));
                DrawString(font, new Vector2(r.Position.X + s * 0.05f, r.End.Y - s * 0.07f), "×" + sl.Count,
                    HorizontalAlignment.Right, s * 0.9f, fc, Ink);
            }
        }
    }

    void DrawChest(float s, Vector2 box, float bw, float bh)
    {
        float lh = LidH * s, tw = bw * LidNarrow;
        float cx = box.X + bw * 0.5f;
        // u en travers (0 à 1), v du pied du couvercle (0) à son bord lointain (1)
        Vector2 Lid(float u, float v) => new(cx + (u - 0.5f) * Mathf.Lerp(bw, tw, v), box.Y - v * lh);

        // l'ombre portée sur le pont, adoucie en trois passes
        for (int k = 4; k >= 1; k--)
        {
            float g = s * 0.07f * k;
            DrawRect(new Rect2(box.X + s * 0.3f - g, box.Y + s * 0.35f - g, bw + 2 * g, bh + 2 * g), new Color(0, 0, 0, 0.09f));
        }

        // ---- LE COUVERCLE, rabattu au fond : on en voit l'intérieur ----
        DrawColoredPolygon(new[] { Lid(0, 0), Lid(1, 0), Lid(1, 1), Lid(0, 1) }, Seam);
        const int planks = 6;
        float fu = Rim * s / bw;                                   // le cadre du couvercle, en part de largeur
        for (int p = 0; p < planks; p++)
        {
            float v0 = 0.07f + p * (0.86f / planks), v1 = v0 + 0.86f / planks - 0.008f;
            // plus sombre vers le fond : la face s'éloigne et se détourne de la lumière
            var c = Shade(Wood, (0.78f + 0.14f * Hash(p, 7)) * (1 - 0.25f * v0));
            DrawColoredPolygon(new[] { Lid(fu, v0), Lid(1 - fu, v0), Lid(1 - fu, v1), Lid(fu, v1) }, c);
            for (int l = 0; l < 3; l++)
            {
                float vv = Mathf.Lerp(v0, v1, 0.25f + 0.25f * l + 0.1f * (Hash(p, l) - 0.5f));
                DrawLine(Lid(fu + 0.02f * Hash(l, p), vv), Lid(1 - fu - 0.03f * Hash(p, l + 9), vv), Shade(c, 0.82f), 1);
            }
        }
        // le cadre : deux montants et deux traverses
        var frame = Shade(Wood, 0.92f);
        DrawColoredPolygon(new[] { Lid(0, 0), Lid(fu, 0), Lid(fu, 1), Lid(0, 1) }, frame);
        DrawColoredPolygon(new[] { Lid(1 - fu, 0), Lid(1, 0), Lid(1, 1), Lid(1 - fu, 1) }, Shade(frame, 0.85f));
        DrawColoredPolygon(new[] { Lid(0, 0.93f), Lid(1, 0.93f), Lid(1, 1), Lid(0, 1) }, Shade(frame, 0.8f));
        DrawColoredPolygon(new[] { Lid(0, 0), Lid(1, 0), Lid(1, 0.07f), Lid(0, 0.07f) }, Shade(frame, 1.05f));
        // les pentures de fer, dans l'axe de celles de la caisse
        foreach (float u in new[] { 0.22f, 0.78f })
        {
            float du = 0.028f;
            DrawColoredPolygon(new[] { Lid(u - du, 0), Lid(u + du, 0), Lid(u + du, 1), Lid(u - du, 1) }, Iron);
            DrawLine(Lid(u - du, 0), Lid(u - du, 1), IronLit, 1);
            for (int r = 0; r < 5; r++)
                DrawCircle(Lid(u, 0.04f + r * 0.23f), s * 0.045f * (1 - 0.15f * r * 0.23f), IronLit);
        }

        // ---- LA CAISSE ----
        DrawRect(new Rect2(box, new Vector2(bw, bh)), Seam);
        float t = Rim * s;
        var o = box; var e = box + new Vector2(bw, bh);
        // les quatre bords, d'onglet aux coins ; la lumière vient de haut à gauche
        var rims = new (Vector2[] poly, float k, bool along)[]
        {
            (new[] { o, new Vector2(e.X, o.Y), new Vector2(e.X - t, o.Y + t), new Vector2(o.X + t, o.Y + t) }, 1.10f, true),
            (new[] { new Vector2(o.X, e.Y), new Vector2(o.X + t, e.Y - t), new Vector2(e.X - t, e.Y - t), e }, 0.95f, true),
            (new[] { o, new Vector2(o.X + t, o.Y + t), new Vector2(o.X + t, e.Y - t), new Vector2(o.X, e.Y) }, 1.02f, false),
            (new[] { new Vector2(e.X, o.Y), e, new Vector2(e.X - t, e.Y - t), new Vector2(e.X - t, o.Y + t) }, 0.85f, false),
        };
        for (int i = 0; i < rims.Length; i++)
        {
            var (poly, k, along) = rims[i];
            var c = Shade(Wood, k * (0.92f + 0.1f * Hash(i, 3)));
            DrawColoredPolygon(poly, c);
            // le fil du bois, dans le sens de la planche
            for (int l = 0; l < 3; l++)
            {
                float f = t * (0.25f + 0.25f * l) + t * 0.08f * (Hash(i, l) - 0.5f);
                if (along)
                {
                    float y = i == 0 ? o.Y + f : e.Y - f;
                    DrawLine(new Vector2(o.X + f + s * 0.1f, y), new Vector2(e.X - f - s * 0.1f, y), Shade(c, 0.8f), 1);
                }
                else
                {
                    float x = i == 2 ? o.X + f : e.X - f;
                    DrawLine(new Vector2(x, o.Y + f + s * 0.1f), new Vector2(x, e.Y - f - s * 0.1f), Shade(c, 0.8f), 1);
                }
            }
            DrawPolyline(new[] { poly[0], poly[1], poly[2], poly[3], poly[0] }, Seam, 1.2f);
        }

        // le fond : du bois clair entre les cases, chaque case garnie de velours
        var inner = new Rect2(o + new Vector2(t, t), new Vector2(bw - 2 * t, bh - 2 * t));
        DrawRect(inner, Divider);
        for (int row = 0; row < Rows; row++)
            for (int col = 0; col < Cols; col++)
            {
                var r = SlotRect(col, row);
                DrawRect(r, Velvet);
                // la paroi de haut et de gauche fait de l'ombre dans la case
                DrawRect(new Rect2(r.Position, new Vector2(s, s * 0.1f)), new Color(0, 0, 0, 0.35f));
                DrawRect(new Rect2(r.Position, new Vector2(s * 0.08f, s)), new Color(0, 0, 0, 0.25f));
                DrawLine(new Vector2(r.Position.X, r.End.Y), r.End, Shade(Divider, 1.25f), 1);
                DrawLine(new Vector2(r.End.X, r.Position.Y), r.End, Shade(Divider, 1.15f), 1);
            }
        // l'ombre du bord lointain sur tout le fond
        DrawRect(new Rect2(inner.Position, new Vector2(inner.Size.X, s * 0.12f)), new Color(0, 0, 0, 0.3f));

        // ---- LE FER : équerres aux coins, pentures sur les bords ----
        float a = s * 0.85f, w = s * 0.17f;
        foreach (var (p, dx, dy) in new[] { (o, 1f, 1f), (new Vector2(e.X, o.Y), -1f, 1f), (new Vector2(o.X, e.Y), 1f, -1f), (e, -1f, -1f) })
        {
            DrawColoredPolygon(new[] { p, p + new Vector2(dx * a, 0), p + new Vector2(dx * a, dy * w), p + new Vector2(dx * w, dy * w),
                p + new Vector2(dx * w, dy * a), p + new Vector2(0, dy * a) }, Iron);
            DrawLine(p, p + new Vector2(dx * a, 0), IronLit, 1);
            DrawCircle(p + new Vector2(dx * a * 0.7f, dy * w * 0.5f), s * 0.035f, IronLit);
            DrawCircle(p + new Vector2(dx * w * 0.5f, dy * a * 0.7f), s * 0.035f, IronLit);
        }
        foreach (float u in new[] { 0.22f, 0.78f })
        {
            float x = o.X + u * bw, du = 0.028f * bw;
            foreach (float y in new[] { o.Y, e.Y - t })
            {
                DrawRect(new Rect2(x - du, y, 2 * du, t), Iron);
                DrawLine(new Vector2(x - du, y), new Vector2(x - du, y + t), IronLit, 1);
                DrawCircle(new Vector2(x, y + t * 0.5f), s * 0.045f, IronLit);
            }
            // la charnière, à la jointure du couvercle
            DrawRect(new Rect2(x - du * 1.2f, o.Y - s * 0.07f, du * 2.4f, s * 0.14f), Shade(Iron, 0.8f));
            DrawLine(new Vector2(x - du * 1.2f, o.Y), new Vector2(x + du * 1.2f, o.Y), IronLit, 1.5f);
        }
        /* LE MORAILLON tient au COUVERCLE, pas à la caisse : couvercle rabattu, il pend
           à son bord lointain, et la serrure, sur la face avant, ne se voit pas d'en haut. */
        var hp = Lid(0.5f, 1);
        var hasp = new Rect2(hp.X - s * 0.18f, hp.Y - s * 0.3f, s * 0.36f, s * 0.42f);
        DrawRect(hasp, Iron);
        DrawLine(hasp.Position, new Vector2(hasp.Position.X, hasp.End.Y), IronLit, 1);
        DrawRect(new Rect2(hp.X - s * 0.05f, hasp.Position.Y + s * 0.07f, s * 0.1f, s * 0.16f), new Color(0, 0, 0, 0.7f));
    }
}
