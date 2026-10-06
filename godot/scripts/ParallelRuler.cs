using Godot;
using System;

namespace NavalSim;

/// <summary>
/// LA RÈGLE PARALLÈLE sur la carte ouverte (ShipDemo.Ruler.cs la pose) : deux lattes
/// d'ébène liées par deux bras de laiton, qui restent parallèles quoi qu'on fasse —
/// c'est tout l'instrument. Posée sur le trait de crayon du départ à l'arrivée, elle
/// MARCHE jusqu'à la rose la plus proche, une latte après l'autre (on tient l'une
/// pendant qu'on pousse l'autre), et une fois sur la rose, son bord passe par le
/// centre : le rumb se lit là où il coupe la couronne.
///
/// Dessinée à l'écran, dans le repère de la feuille montrée (At : mètres du monde →
/// pixels de ce contrôle), comme l'encre de la carte ouverte.
/// </summary>
public partial class ParallelRuler : Control
{
    public Func<double, double, Vector2>? At;
    public (double X, double Z) A, B, Rose;
    /// <summary>Le rayon de la rose à l'écran, en pixels.</summary>
    public float RoseR = 60;
    /// <summary>Ce qu'on lit sur la rose (« NE ¼ E · N 61° E »).</summary>
    public string Reading = "";
    public Font? Hand;
    public bool Laid { get; private set; }

    double _t;
    const double Settle = 0.35, Walk = 1.6;

    static readonly Color Ebony = new(0.13f, 0.09f, 0.06f, 0.92f), Edge = new(0.55f, 0.42f, 0.24f, 0.95f),
        Brass = new(0.80f, 0.62f, 0.26f), Graphite = new(0.22f, 0.22f, 0.24f, 0.85f), RedPencil = new(0.62f, 0.14f, 0.10f, 0.9f);

    public ParallelRuler() { MouseFilter = MouseFilterEnum.Ignore; AnchorRight = 1; AnchorBottom = 1; }

    /// <summary>La poser sur un trait neuf : elle repart du trait.</summary>
    public void Lay((double X, double Z) a, (double X, double Z) b, (double X, double Z) rose, string reading)
    {
        A = a; B = b; Rose = rose; Reading = reading;
        _t = 0; Laid = true;
        QueueRedraw();
    }

    public void Lift() { Laid = false; QueueRedraw(); }

    public override void _Process(double delta)
    {
        if (!Laid || !IsVisibleInTree() || _t > Settle + Walk) return;
        _t += delta;
        QueueRedraw();
    }

    static float Smooth(double u) { float x = (float)Math.Clamp(u, 0, 1); return x * x * (3 - 2 * x); }

    public override void _Draw()
    {
        if (!Laid || At == null) return;
        var pa = At(A.X, A.Z);
        var pb = At(B.X, B.Z);
        var pr = At(Rose.X, Rose.Z);
        var ab = pb - pa;
        if (ab.LengthSquared() < 1) return;
        var d = ab.Normalized();
        var n = new Vector2(-d.Y, d.X);

        // LE TRAIT DE CRAYON, du départ à l'arrivée : une croix au départ, un rond à l'arrivée
        DrawLine(pa, pb, Graphite, 1.6f);
        float c = 5;
        DrawLine(pa - new Vector2(c, c), pa + new Vector2(c, c), Graphite, 1.6f);
        DrawLine(pa + new Vector2(c, -c), pa - new Vector2(c, -c), Graphite, 1.6f);
        DrawArc(pb, 5, 0, Mathf.Tau, 20, Graphite, 1.6f);

        // LA RÈGLE : longueur bornée, une règle a sa taille ; l'écart des lattes, celui de ses bras
        float L = Math.Clamp(ab.Length(), 240, 520), W = 15, gap = 34;
        var mid = (pa + pb) * 0.5f;
        // la latte du dessous posée SUR le trait (son bord), l'autre au-dessus
        var c1 = mid + n * (W * 0.5f);
        var c2 = c1 + n * (gap + W);
        // la marche : quatre pas, une latte puis l'autre, jusqu'à ce que le bord de la première passe par le centre de la rose
        var D = (pr + n * (W * 0.5f)) - c1;
        double w = (_t - Settle) / Walk * 4;
        float u0 = Smooth(w), u1 = Smooth(w - 1), u2 = Smooth(w - 2), u3 = Smooth(w - 3);
        var o1 = c1 + D * 0.5f * (u0 + u2);
        var o2 = c2 + D * 0.5f * (u1 + u3);
        // posée d'abord : elle descend sur le trait en se fondant
        float fade = Smooth(_t / Settle);
        Bar(o1, d, n, L, W, fade);
        Bar(o2, d, n, L, W, fade);
        foreach (float s in new[] { -0.3f, 0.3f })
        {
            var p1 = o1 + d * (L * s) + n * (W * 0.5f - 2);
            var p2 = o2 + d * (L * s) - n * (W * 0.5f - 2);
            DrawLine(p1, p2, new Color(Brass, fade), 4);
            DrawCircle(p1, 3.2f, new Color(Brass.Lightened(0.2f), fade));
            DrawCircle(p2, 3.2f, new Color(Brass.Lightened(0.2f), fade));
        }

        // ARRIVÉE SUR LA ROSE : le rumb tiré par le centre, au crayon rouge, et sa lecture
        if (_t >= Settle + Walk)
        {
            DrawLine(pr - d * RoseR * 1.25f, pr + d * RoseR * 1.25f, RedPencil, 2);
            DrawCircle(pr + d * RoseR, 4, RedPencil);
            var font = Hand ?? ThemeDB.FallbackFont;
            // sous la rose, au crayon rouge : la couronne et la fleur de lys restent lisibles
            const int fs = 30;
            float tw = font.GetStringSize(Reading, HorizontalAlignment.Left, -1, fs).X;
            var at = pr + new Vector2(-tw * 0.5f, RoseR * 1.12f + fs);
            // pas de place dessous (la rose au bas de la feuille) : au-dessus, par-delà la fleur de lys ; et toujours dans le cadre
            if (at.Y > Size.Y - 10) at.Y = pr.Y - RoseR * 1.32f;
            at.X = Math.Clamp(at.X, 10, Math.Max(10, Size.X - tw - 10));
            at.Y = Math.Clamp(at.Y, fs + 6, Math.Max(fs + 6, Size.Y - 10));
            DrawString(font, at + new Vector2(1.5f, 1.5f), Reading, HorizontalAlignment.Left, -1, fs, new Color(1, 0.97f, 0.9f, 0.8f));
            DrawString(font, at, Reading, HorizontalAlignment.Left, -1, fs, RedPencil);
        }
    }

    void Bar(Vector2 o, Vector2 d, Vector2 n, float L, float W, float alpha)
    {
        var hl = d * (L * 0.5f); var hw = n * (W * 0.5f);
        var pts = new[] { o - hl - hw, o + hl - hw, o + hl + hw, o - hl + hw };
        DrawColoredPolygon(pts, new Color(Ebony, Ebony.A * alpha));
        DrawPolyline(new[] { pts[0], pts[1], pts[2], pts[3], pts[0] }, new Color(Edge, Edge.A * alpha), 1.5f);
    }
}
