using Godot;
using System;
using System.Text.Json;
using NavalSim.Core;

namespace NavalSim;

/// <summary>
/// L'INSTRUMENT EN MAIN, montré dans un coin pendant qu'on vise (ShipDemo.Sight.cs) :
/// ce que la main règle, vu de côté, quand <see cref="SightPlate"/> montre ce que l'œil voit.
///
/// LE QUARTIER DE DAVIS : un bâton, deux arcs sur un même centre. Le petit (60°) porte la
/// pinnule d'ombre, posée sur un degré rond ; le grand (30°) porte la pinnule de visée, qui
/// glisse. La hauteur est la somme des deux lectures. L'ARBALESTRILLE : un bâton gradué et
/// le marteau qui court dessus ; ses graduations se serrent vers l'œil, où sont les grands angles.
///
/// DESSINÉ, en attendant une image (instruments/instruments.json) : quand elle viendra,
/// elle remplacera le bois et le laiton, et les pièces mobiles resteront posées aux places
/// que la fiche dit — la même fiche d'où l'outil tire les gabarits à peindre
/// (tools/instrument-templates.js). Une définition, deux usagers.
/// </summary>
public partial class SightInstrument : Control
{
    /// <summary>La géométrie d'un instrument, en pixels de son image.</summary>
    public sealed class Book
    {
        public string QImage = "", QView = "", XImage = "";
        public Vector2 QSize, QCentre, XSize, XEye, XEnd;
        public float SmallR, SmallZero, SmallFull, SmallDeg, LargeR, LargeZero, LargeFull, LargeDeg;
        public float XFrom, XTo;
        // le nocturlabe (NocturnalPlate) et le livre du pilote (PilotBook)
        public string NDisk = "", NHours = "", NArm = "", LImage = "";
        public Vector2 NSize, NCentre, LSize;
        public float NRadius, NField = 11, NDatesIn, NDatesOut, NHoursR, NArmLen, NJanuary;
        public Rect2 LLeft, LRight;

        /// <summary>
        /// L'anneau des dates : l'angle d'écran (degrés, 0 à droite, positif vers le bas) de ce
        /// jour de l'année. Les jours tournent en sens inverse des aiguilles d'une montre : la
        /// Garde, au même minuit, a avancé d'un jour sidéral — 360° par an.
        /// </summary>
        public float DateAngle(double dayOfYear) => NJanuary - (float)(dayOfYear * 360 / 365.2422);

        /// <summary>Le demi-marteau, en pixels : celui qui met <see cref="XFrom"/> au bout du bâton.</summary>
        public float HalfTransom => (XEnd - XEye).Length() * MathF.Tan(XFrom * 0.5f * MathF.PI / 180);
        /// <summary>Où tombe le marteau pour cet angle : tan(θ/2) = demi-marteau ÷ distance à l'œil.</summary>
        public Vector2 StaffAt(float deg)
        {
            var d = (XEnd - XEye).Normalized();
            return XEye + d * (HalfTransom / MathF.Tan(Math.Clamp(deg, 0.5f, 179) * 0.5f * MathF.PI / 180));
        }
        /// <summary>Le point d'un arc du quartier à cette lecture (0 → zéro de l'arc).</summary>
        public Vector2 ArcAt(bool small, float read, float atRadius = -1)
        {
            float r = atRadius >= 0 ? atRadius : small ? SmallR : LargeR;
            float a = ArcAngle(small, read);
            return QCentre + new Vector2(MathF.Cos(a), MathF.Sin(a)) * r;
        }
        public float ArcAngle(bool small, float read)
        {
            float z = small ? SmallZero : LargeZero, f = small ? SmallFull : LargeFull, n = small ? SmallDeg : LargeDeg;
            return (z + (f - z) * read / n) * MathF.PI / 180;
        }

        public static Book FromJson(string text)
        {
            var b = new Book();
            using var doc = JsonDocument.Parse(text, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            var root = doc.RootElement;
            static Vector2 V(JsonElement e, string k)
            {
                if (!e.TryGetProperty(k, out var a) || a.ValueKind != JsonValueKind.Array || a.GetArrayLength() < 2) return Vector2.Zero;
                return new Vector2((float)a[0].GetDouble(), (float)a[1].GetDouble());
            }
            if (root.TryGetProperty("quartier", out var q))
            {
                b.QImage = q.Str("image"); b.QView = q.Str("vue");
                b.QSize = V(q, "taille"); b.QCentre = V(q, "centre");
                if (q.TryGetProperty("petitArc", out var p))
                { b.SmallR = (float)p.Num("rayon"); b.SmallZero = (float)p.Num("zero"); b.SmallFull = (float)p.Num("plein"); b.SmallDeg = (float)p.Num("degres", 60); }
                if (q.TryGetProperty("grandArc", out var g))
                { b.LargeR = (float)g.Num("rayon"); b.LargeZero = (float)g.Num("zero"); b.LargeFull = (float)g.Num("plein"); b.LargeDeg = (float)g.Num("degres", 30); }
            }
            if (root.TryGetProperty("arbalestrille", out var x))
            {
                b.XImage = x.Str("image");
                b.XSize = V(x, "taille"); b.XEye = V(x, "oeil"); b.XEnd = V(x, "bout");
                b.XFrom = (float)x.Num("de", 10); b.XTo = (float)x.Num("a", 60);
            }
            if (root.TryGetProperty("nocturlabe", out var n))
            {
                b.NDisk = n.Str("disque"); b.NHours = n.Str("heures"); b.NArm = n.Str("bras");
                b.NSize = V(n, "taille"); b.NCentre = V(n, "centre");
                b.NRadius = (float)n.Num("rayon"); b.NField = (float)n.Num("champ", 11);
                var dr = V(n, "dates"); b.NDatesIn = dr.X; b.NDatesOut = dr.Y;
                b.NHoursR = (float)n.Num("rayonHeures"); b.NArmLen = (float)n.Num("longueurBras");
                b.NJanuary = (float)n.Num("janvier");
            }
            if (root.TryGetProperty("livre", out var l))
            {
                b.LImage = l.Str("image"); b.LSize = V(l, "taille");
                static Rect2 R(JsonElement e, string k)
                {
                    if (!e.TryGetProperty(k, out var a) || a.ValueKind != JsonValueKind.Array || a.GetArrayLength() < 4) return new Rect2();
                    return new Rect2((float)a[0].GetDouble(), (float)a[1].GetDouble(), (float)a[2].GetDouble(), (float)a[3].GetDouble());
                }
                b.LLeft = R(l, "gauche"); b.LRight = R(l, "droite");
            }
            return b;
        }
    }

    public Book? Geo;
    /// <summary>Vrai : le quartier de Davis ; faux : l'arbalestrille.</summary>
    public bool Sun = true;
    /// <summary>Le quartier : la pinnule d'ombre sur le petit arc, la pinnule de visée sur le grand (degrés lus).</summary>
    public float SmallRead, LargeRead;
    /// <summary>L'arbalestrille : l'angle où le marteau est posé.</summary>
    public float StaffDeg;
    public Texture2D? QuadrantPicture, StaffPicture;

    static readonly Color Wood = new(0.45f, 0.29f, 0.15f, 0.96f), WoodDark = new(0.26f, 0.16f, 0.08f, 0.98f);
    static readonly Color Bone = new(0.93f, 0.88f, 0.74f, 0.97f), Ink = new(0.12f, 0.08f, 0.04f, 0.95f);
    static readonly Color Brass = new(0.88f, 0.70f, 0.36f, 1f), BrassDark = new(0.50f, 0.36f, 0.14f, 1f);

    // where the picture sits on screen: image pixel → screen
    Vector2 _o; float _k;
    Vector2 P(Vector2 img) => _o + img * _k;

    public override void _Draw()
    {
        if (Geo == null) return;
        var sz = Size;
        var imgSize = Sun ? Geo.QSize : Geo.XSize;
        if (imgSize.X <= 0 || imgSize.Y <= 0) return;
        // bottom right, a corner of the view the eye does not need
        float w = MathF.Min(sz.X * (Sun ? 0.36f : 0.5f), sz.Y * 0.5f * imgSize.X / imgSize.Y);
        _k = w / imgSize.X;
        _o = new Vector2(sz.X - w - 24, sz.Y - imgSize.Y * _k - 24);
        if (Sun) Quadrant(); else Staff();
    }

    void Quadrant()
    {
        var g = Geo!;
        var font = ThemeDB.FallbackFont;
        float lw = MathF.Max(1, 70 * _k);
        if (QuadrantPicture != null) DrawTextureRect(QuadrantPicture, new Rect2(_o, g.QSize * _k), false);
        else
        {
            // the frame: the staff along the large arc's zero, the struts to the arcs' ends
            DrawLine(P(g.QCentre), P(g.ArcAt(false, 0, g.LargeR + 40)), WoodDark, 34 * _k);
            DrawLine(P(g.QCentre), P(g.ArcAt(false, g.LargeDeg, g.LargeR - 20)), WoodDark, 22 * _k);
            DrawLine(P(g.QCentre), P(g.ArcAt(true, g.SmallDeg, g.SmallR - 10)), WoodDark, 18 * _k);
            DrawLine(P(g.QCentre), P(g.ArcAt(true, 0, g.SmallR)), WoodDark, 18 * _k);
            Arc(true, lw * 0.8f);
            Arc(false, lw);
            // the horizon vane at the centre, its slit dark
            var c = P(g.QCentre);
            DrawRect(new Rect2(c - new Vector2(14, 36) * _k * 1.6f, new Vector2(28, 72) * _k * 1.6f), BrassDark);
            DrawLine(c - new Vector2(0, 30 * _k * 1.6f), c + new Vector2(0, 30 * _k * 1.6f), Ink, MathF.Max(1, 5 * _k));
        }
        // the two rays, faint: the sun's through the shadow vane, the eye's through the sight vane
        var cen = P(g.QCentre);
        var sv = P(g.ArcAt(true, SmallRead)); var ev = P(g.ArcAt(false, LargeRead));
        DrawDashedLine(sv, cen + (cen - sv).Normalized() * 30 * _k, new Color(1f, 0.85f, 0.4f, 0.55f), MathF.Max(1, 3 * _k), 10);
        DrawDashedLine(ev, cen + (cen - ev).Normalized() * 120 * _k, new Color(0.75f, 0.88f, 1f, 0.5f), MathF.Max(1, 3 * _k), 10);
        Vane(true, SmallRead, lw * 0.8f);
        Vane(false, LargeRead, lw);
        float fs = MathF.Max(13, 40 * _k);
        DrawString(font, _o + new Vector2(0, -8), $"{SmallRead:0}°  +  {Sights.Dm(LargeRead)}  =  {Sights.Dm(SmallRead + LargeRead)}",
            HorizontalAlignment.Right, g.QSize.X * _k, (int)fs, Bone);
    }

    /// <summary>Un arc du quartier : la bande de bois clair, ses degrés, ses dizaines.</summary>
    void Arc(bool small, float width)
    {
        var g = Geo!;
        float r = (small ? g.SmallR : g.LargeR) * _k, n = small ? g.SmallDeg : g.LargeDeg;
        float a0 = g.ArcAngle(small, 0), a1 = g.ArcAngle(small, n);
        var c = P(g.QCentre);
        DrawArc(c, r, MathF.Min(a0, a1), MathF.Max(a0, a1), 96, Bone, width, true);
        DrawArc(c, r - width * 0.5f, MathF.Min(a0, a1), MathF.Max(a0, a1), 96, WoodDark, MathF.Max(1, 4 * _k), true);
        DrawArc(c, r + width * 0.5f, MathF.Min(a0, a1), MathF.Max(a0, a1), 96, WoodDark, MathF.Max(1, 4 * _k), true);
        var font = ThemeDB.FallbackFont;
        for (int d = 0; d <= (int)n; d++)
        {
            float a = g.ArcAngle(small, d);
            var u = new Vector2(MathF.Cos(a), MathF.Sin(a));
            float len = d % 10 == 0 ? 0.9f : d % 5 == 0 ? 0.6f : 0.35f;
            DrawLine(c + u * (r + width * 0.5f), c + u * (r + width * 0.5f - width * len), Ink, MathF.Max(1, (d % 5 == 0 ? 4 : 2.5f) * _k));
            if (d % (small ? 10 : 5) == 0)
            {
                float fs = MathF.Max(11, 34 * _k);
                var at = c + u * (r + width * 0.5f + fs * 0.9f);
                DrawString(font, at + new Vector2(-fs, fs * 0.35f), d.ToString(), HorizontalAlignment.Center, fs * 2, (int)fs, Bone);
            }
        }
    }

    /// <summary>Une pinnule sur son arc : un bloc de laiton, tourné vers le centre, et son trait de lecture.</summary>
    void Vane(bool small, float read, float width)
    {
        var g = Geo!;
        float a = g.ArcAngle(small, read);
        var u = new Vector2(MathF.Cos(a), MathF.Sin(a)); var t = new Vector2(-u.Y, u.X);
        var c = P(g.QCentre) + u * (small ? g.SmallR : g.LargeR) * _k;
        float h = width * 0.85f, b = MathF.Max(3, 16 * _k);
        var pts = new[] { c - u * h - t * b, c - u * h + t * b, c + u * h + t * b, c + u * h - t * b };
        DrawColoredPolygon(pts, Brass);
        DrawPolyline(new[] { pts[0], pts[1], pts[2], pts[3], pts[0] }, BrassDark, MathF.Max(1, 3 * _k));
        DrawLine(c - u * h * 1.25f, c + u * h * 1.25f, new Color(0.7f, 0.1f, 0.05f, 1), MathF.Max(1, 2.5f * _k));
    }

    void Staff()
    {
        var g = Geo!;
        var font = ThemeDB.FallbackFont;
        var e = P(g.XEye); var f = P(g.XEnd);
        var d = (f - e).Normalized(); var n = new Vector2(-d.Y, d.X);
        float th = MathF.Max(6, 44 * _k);
        if (StaffPicture != null) DrawTextureRect(StaffPicture, new Rect2(_o, g.XSize * _k), false);
        else
        {
            // the staff, square in section, its graduated face toward us
            DrawColoredPolygon(new[] { e - n * th * 0.5f, f - n * th * 0.5f, f + n * th * 0.5f, e + n * th * 0.5f }, Bone);
            DrawPolyline(new[] { e - n * th * 0.5f, f - n * th * 0.5f, f + n * th * 0.5f, e + n * th * 0.5f, e - n * th * 0.5f }, WoodDark, MathF.Max(1, 4 * _k));
            // the eye end, capped in brass
            DrawRect(new Rect2(e - n * th * 0.6f - d * 4, new Vector2(10 * _k + 6, th * 1.2f)), BrassDark);
            float fs = MathF.Max(11, 30 * _k);
            for (int deg = (int)MathF.Ceiling(g.XFrom); deg <= (int)g.XTo; deg++)
            {
                var at = P(g.StaffAt(deg));
                float len = deg % 10 == 0 ? 1f : deg % 5 == 0 ? 0.65f : 0.35f;
                DrawLine(at - n * th * 0.5f, at - n * th * (0.5f - len * 0.8f), Ink, MathF.Max(1, (deg % 5 == 0 ? 3.5f : 2f) * _k));
                if (deg % 10 == 0 || (deg % 5 == 0 && deg < 30))
                    DrawString(font, at + n * th * 0.45f + new Vector2(-fs, 0), deg.ToString(), HorizontalAlignment.Center, fs * 2, (int)fs, Ink);
            }
        }
        // the transom, square across the staff, its half-length the one the template uses
        var tp = P(g.StaffAt(Math.Clamp(StaffDeg, g.XFrom, g.XTo)));
        float half = g.HalfTransom * _k;
        DrawLine(tp - n * half, tp + n * half, WoodDark, MathF.Max(5, 26 * _k));
        DrawLine(tp - n * half, tp + n * half, Wood, MathF.Max(3, 16 * _k));
        DrawRect(new Rect2(tp - new Vector2(1, 1) * th * 0.75f, new Vector2(1, 1) * th * 1.5f), Brass);
        DrawRect(new Rect2(tp - new Vector2(1, 1) * th * 0.75f, new Vector2(1, 1) * th * 1.5f), BrassDark, false, MathF.Max(1, 3 * _k));
        // the two sight lines, faint: eye to each end of the transom — the horizon and the star
        DrawDashedLine(e, tp + n * half, new Color(0.75f, 0.88f, 1f, 0.45f), MathF.Max(1, 2.5f * _k), 10);
        DrawDashedLine(e, tp - n * half, new Color(1f, 0.95f, 0.8f, 0.45f), MathF.Max(1, 2.5f * _k), 10);
        float fr = MathF.Max(13, 40 * _k);
        DrawString(font, _o + new Vector2(0, -8), "Marteau sur " + Sights.Dm(StaffDeg) + (StaffDeg < g.XFrom || StaffDeg > g.XTo ? " — hors du bâton" : ""),
            HorizontalAlignment.Right, g.XSize.X * _k, (int)fr, Bone);
    }
}
