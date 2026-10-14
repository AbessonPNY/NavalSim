using Godot;
using System;

namespace NavalSim;

/// <summary>
/// LE NOCTURLABE, tenu à bout de bras devant le ciel du nord (ShipDemo.Nocturnal.cs).
///
/// Trois pièces sur un même pivot creux : la PLAQUE, fixe, son manche tenu en bas, qui
/// porte l'anneau des dates ; le DISQUE DES HEURES, qu'on tourne pour mettre sa dent de
/// minuit sur la date du jour ; le BRAS (l'alidade), qu'on fait pivoter. On vise la
/// Polaire par le trou du pivot, on amène le bras sur la Garde, et l'heure se lit où le
/// bras croise le disque — au toucher aussi, par les dents, quand il fait trop noir.
///
/// La géométrie vient d'instruments/instruments.json (SightInstrument.Book) ; chaque pièce
/// est dessinée tant que son image est vide. Les angles sont des angles d'écran.
/// </summary>
public partial class NocturnalPlate : Control
{
    public SightInstrument.Book? Geo;
    public Texture2D? DiskPicture, HoursPicture, ArmPicture;
    public float PxPerDeg = 20;
    /// <summary>Où la dent de minuit est posée (l'anneau des dates, au jour de la partie), degrés d'écran.</summary>
    public float MidnightDeg;
    /// <summary>Le bras, degrés d'écran.</summary>
    public float ArmDeg = 90;
    /// <summary>Le jour de l'année : son trait est marqué sur l'anneau.</summary>
    public double DayOfYear;
    /// <summary>La Polaire et la Garde à l'écran, quand le ciel n'a pas les vraies.</summary>
    public bool DrawStars;
    public Vector2 PolarisAt, GuardAt;
    public string Top = "", Readout = "", Hint = "";

    static readonly Color Brass = new(0.80f, 0.62f, 0.30f, 0.97f), BrassDark = new(0.42f, 0.29f, 0.11f, 1f);
    static readonly Color Wood = new(0.36f, 0.22f, 0.11f, 0.97f), Bone = new(0.92f, 0.87f, 0.72f, 0.97f);
    static readonly Color Ink = new(0.12f, 0.08f, 0.04f, 0.95f);
    static readonly string[] Months = { "JANV", "FÉVR", "MARS", "AVRIL", "MAI", "JUIN", "JUIL", "AOÛT", "SEPT", "OCT", "NOV", "DÉC" };
    static readonly int[] MonthDays = { 31, 28, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31 };
    static readonly string[] Roman = { "XII", "I", "II", "III", "IV", "V", "VI", "VII", "VIII", "IX", "X", "XI" };

    static Vector2 U(float deg) => new(MathF.Cos(deg * MathF.PI / 180), MathF.Sin(deg * MathF.PI / 180));

    public override void _Draw()
    {
        var g = Geo;
        if (g == null || g.NRadius <= 0) return;
        var sz = Size;
        var c = sz * 0.5f;
        float k = g.NField * PxPerDeg / g.NRadius;              // screen pixels per image pixel
        var font = ThemeDB.FallbackFont;
        float hole = 26 * k;

        if (DrawStars)
        {
            DrawCircle(PolarisAt, 3.2f, new Color(0.95f, 0.97f, 1f));
            DrawCircle(GuardAt, 3.0f, new Color(1f, 0.85f, 0.7f));
            DrawCircle(GuardAt, 7f, new Color(1f, 0.85f, 0.7f, 0.18f));
        }

        // LA PLAQUE : le manche en bas, puis l'anneau des dates
        if (DiskPicture != null) DrawTextureRect(DiskPicture, new Rect2(c - g.NCentre * k, g.NSize * k), false);
        else
        {
            float hl = (g.NSize.Y - g.NCentre.Y - 30) * k, hw = 70 * k;
            DrawRect(new Rect2(c.X - hw, c.Y + g.NRadius * k * 0.8f, 2 * hw, hl - g.NRadius * k * 0.8f), Wood);
            DrawRect(new Rect2(c.X - hw, c.Y + g.NRadius * k * 0.8f, 2 * hw, hl - g.NRadius * k * 0.8f), BrassDark, false, MathF.Max(1, 4 * k));
            Ring(c, hole, g.NRadius * k, Brass);
            DrawArc(c, g.NRadius * k, 0, MathF.Tau, 160, BrassDark, MathF.Max(1, 5 * k), true);
            Ring(c, g.NDatesIn * k, g.NDatesOut * k, Bone);
            DrawArc(c, g.NDatesIn * k, 0, MathF.Tau, 160, Ink, MathF.Max(1, 2 * k), true);
            float r0 = g.NDatesIn * k, r1 = g.NDatesOut * k;
            int doy = 0;
            for (int m = 0; m < 12; m++)
            {
                for (int d = 0; d < MonthDays[m]; d++, doy++)
                {
                    var u = U(g.DateAngle(doy));
                    float len = d == 0 ? 1f : (d + 1) % 10 == 0 ? 0.42f : (d + 1) % 5 == 0 ? 0.3f : 0.16f;
                    DrawLine(c + u * r1, c + u * (r1 - (r1 - r0) * len), Ink, MathF.Max(1, (d == 0 ? 3 : 1.5f) * k));
                }
                // the month's name halfway round its days, read from the centre outward
                var mu = U(g.DateAngle(doy - MonthDays[m] * 0.5));
                float fs = MathF.Max(9, 30 * k);
                DrawSetTransform(c + mu * (r0 + (r1 - r0) * 0.35f), mu.Angle() + MathF.PI / 2, Vector2.One);
                DrawString(font, new Vector2(-fs * 2, fs * 0.35f), Months[m], HorizontalAlignment.Center, fs * 4, (int)fs, Ink);
                DrawSetTransform(Vector2.Zero, 0, Vector2.One);
            }
        }
        // today, a red mark on the ring: where the midnight tooth has to go
        var tu = U(g.DateAngle(DayOfYear));
        DrawLine(c + tu * g.NDatesOut * k, c + tu * (g.NDatesOut + 30) * k, new Color(0.75f, 0.1f, 0.05f), MathF.Max(1.5f, 4 * k));

        // LE DISQUE DES HEURES, tourné sur la date
        float rot = (MidnightDeg + 90) * MathF.PI / 180;
        if (HoursPicture != null)
        {
            var hs = HoursPicture.GetSize() * k;
            DrawSetTransform(c, rot, Vector2.One);
            DrawTextureRect(HoursPicture, new Rect2(-hs * 0.5f, hs), false);
            DrawSetTransform(Vector2.Zero, 0, Vector2.One);
        }
        else
        {
            float rh = g.NHoursR * k;
            Ring(c, hole, rh, new Color(0.88f, 0.72f, 0.40f, 0.98f));
            DrawArc(c, rh, 0, MathF.Tau, 160, BrassDark, MathF.Max(1, 3 * k), true);
            float fs = MathF.Max(9, 34 * k);
            for (int q = 0; q < 96; q++)
            {
                var u = U(MidnightDeg - q * 3.75f);
                float len = q % 4 == 0 ? 0.13f : q % 2 == 0 ? 0.07f : 0.04f;
                DrawLine(c + u * rh, c + u * rh * (1 - len), Ink, MathF.Max(1, (q % 4 == 0 ? 2.5f : 1.2f) * k));
                if (q % 4 != 0) continue;
                int h = q / 4;
                // the teeth, to count the hours by feel in the dark; midnight's is the long one, the pointer
                float tooth = h == 0 ? g.NDatesOut - g.NHoursR - 4 : 14;
                var t = new Vector2(-u.Y, u.X);
                var a = c + u * rh;
                DrawColoredPolygon(new[] { a - t * 9 * k, a + u * tooth * k, a + t * 9 * k }, BrassDark);
                var at = c + u * rh * 0.76f;
                DrawSetTransform(at, u.Angle() + MathF.PI / 2, Vector2.One);
                DrawString(font, new Vector2(-fs * 2, fs * 0.35f), Roman[h % 12], HorizontalAlignment.Center, fs * 4, (int)fs, Ink);
                DrawSetTransform(Vector2.Zero, 0, Vector2.One);
            }
            DrawArc(c, rh * 0.55f, 0, MathF.Tau, 96, new Color(BrassDark, 0.6f), MathF.Max(1, 2 * k), true);
        }

        // LE BRAS : son bord de lecture passe par le pivot
        var au = U(ArmDeg); var an = new Vector2(-au.Y, au.X);
        float len2 = g.NArmLen * k;
        if (ArmPicture != null)
        {
            var asz = ArmPicture.GetSize() * k;
            DrawSetTransform(c, (ArmDeg + 90) * MathF.PI / 180, Vector2.One);
            DrawTextureRect(ArmPicture, new Rect2(-asz * 0.5f, asz), false);
            DrawSetTransform(Vector2.Zero, 0, Vector2.One);
        }
        else
        {
            float w0 = 46 * k, w1 = 14 * k;
            var pts = new[] { c - au * 60 * k, c + au * len2, c + au * len2 + an * w1, c + an * w0 - au * 60 * k };
            DrawColoredPolygon(pts, new Color(0.62f, 0.44f, 0.20f, 0.97f));
            DrawPolyline(new[] { pts[0], pts[1], pts[2], pts[3], pts[0] }, BrassDark, MathF.Max(1, 2.5f * k));
            DrawLine(c, c + au * len2, new Color(0.08f, 0.05f, 0.02f, 1f), MathF.Max(1, 2 * k));
        }
        // the hollow pivot: the Pole Star is seen through it
        DrawArc(c, hole, 0, MathF.Tau, 48, BrassDark, MathF.Max(2, 8 * k), true);

        DrawString(font, new Vector2(24, 40), Top, HorizontalAlignment.Left, sz.X - 48, 18, new Color(1, 0.96f, 0.86f));
        DrawString(font, new Vector2(24, sz.Y - 80), Readout, HorizontalAlignment.Left, sz.X * 0.5f, 26, new Color(1, 0.92f, 0.7f));
        DrawMultilineString(font, new Vector2(24, sz.Y - 48), Hint, HorizontalAlignment.Left, sz.X * 0.5f, 16, -1, new Color(0.9f, 0.9f, 0.85f));
    }

    /// <summary>Un anneau plein entre deux rayons.</summary>
    void Ring(Vector2 c, float r0, float r1, Color col) =>
        DrawArc(c, (r0 + r1) * 0.5f, 0, MathF.Tau, 160, col, r1 - r0, true);
}
