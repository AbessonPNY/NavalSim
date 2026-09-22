using Godot;
using System;

namespace NavalSim;

/// <summary>
/// LA BOUSSOLE, en bas à droite : une rose des vents posée sur un disque de la
/// carte du capitaine, en transparence.
///
/// NORD EN HAUT, comme la carte — une rose qui tournerait avec le navire serait
/// un GPS de voiture, pas une boussole de 1690. Le navire est au centre, tourné
/// à son cap ; le vent est une flèche sur le bord, d'où il vient. Et le disque
/// est centré sur le point ESTIMÉ, pas sur la vérité : c'est la même carte que
/// celle qu'on ouvre (I), avec sa route à la plume et ses notes, et elle ne sait
/// pas mieux où l'on est.
///
/// La molette sur la boussole change la loupe, d'un demi-mille à dix milles de
/// rayon (en mètres de jeu).
/// </summary>
public partial class CompassNode : Control
{
    const float Size0 = 230;
    ColorRect _map = null!;
    ShaderMaterial _mat = null!;
    Rose _rose = null!;

    /// <summary>Le rayon du disque, en mètres de jeu.</summary>
    public double Radius = 3000;

    public override void _Ready()
    {
        CustomMinimumSize = new Vector2(Size0, Size0);
        Size = new Vector2(Size0, Size0);
        MouseFilter = MouseFilterEnum.Stop;
        _mat = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/compass_map.gdshader") };
        _map = new ColorRect { Size = Size, Material = _mat, MouseFilter = MouseFilterEnum.Ignore };
        AddChild(_map);
        _rose = new Rose { Size = Size, MouseFilter = MouseFilterEnum.Ignore };
        AddChild(_rose);
        GuiInput += e =>
        {
            if (e is not InputEventMouseButton mb || !mb.Pressed) return;
            if (mb.ButtonIndex == MouseButton.WheelUp) Radius = Math.Max(900, Radius / 1.25);
            else if (mb.ButtonIndex == MouseButton.WheelDown) Radius = Math.Min(18000, Radius * 1.25);
            else return;
            AcceptEvent();
        };
    }

    /// <summary>
    /// Une image : la carte (sa texture, et où tombe le point estimé), le cap du
    /// navire et le vent, en degrés vrais.
    /// </summary>
    public void Show(ChartNode chart, double x, double z, double heading, double windFrom, double miles)
    {
        var tex = chart.Texture;
        var size = tex.GetSize();
        var c = chart.ToChart(x, z);
        double px = Radius / chart.MetresPerPixel;
        _mat.SetShaderParameter("u_chart", tex);
        _mat.SetShaderParameter("u_center", new Vector2(c.X / size.X, c.Y / size.Y));
        _mat.SetShaderParameter("u_span", new Vector2((float)(px / size.X), (float)(px / size.Y)));
        _rose.Heading = heading;
        _rose.WindFrom = windFrom;
        _rose.RadiusMiles = miles;
        _rose.QueueRedraw();
    }

    sealed partial class Rose : Control
    {
        public double Heading, WindFrom, RadiusMiles;

        static readonly Color Ink = new(0.95f, 0.92f, 0.84f);
        static readonly Color Shade = new(0, 0, 0, 0.55f);

        // une direction vraie à l'écran : nord en haut, est à droite
        static Vector2 Dir(double deg)
        {
            double a = deg * Math.PI / 180;
            return new Vector2((float)Math.Sin(a), (float)-Math.Cos(a));
        }

        public override void _Draw()
        {
            var c = Size * 0.5f;
            float R = Size.X * 0.5f - 2;
            var font = ThemeDB.FallbackFont;

            // le cercle et ses seize aires : les quatre cardinales longues, les demi-vents courts
            DrawArc(c, R, 0, Mathf.Tau, 96, Shade, 4f, true);
            DrawArc(c, R, 0, Mathf.Tau, 96, Ink, 1.6f, true);
            for (int i = 0; i < 32; i++)
            {
                double deg = i * 11.25;
                float len = i % 8 == 0 ? 16 : i % 4 == 0 ? 11 : i % 2 == 0 ? 7 : 4;
                var d = Dir(deg);
                DrawLine(c + d * R, c + d * (R - len), Ink, i % 8 == 0 ? 2f : 1.2f, true);
            }
            string[] card = { "N", "E", "S", "O" };
            for (int i = 0; i < 4; i++)
            {
                var d = Dir(i * 90);
                var at = c + d * (R - 28) + new Vector2(-6, 7);
                DrawString(font, at + new Vector2(1, 1), card[i], HorizontalAlignment.Left, -1, i == 0 ? 20 : 16, Shade);
                DrawString(font, at, card[i], HorizontalAlignment.Left, -1, i == 0 ? 20 : 16,
                    Ink);
            }

            // le vent, sur le bord, d'où il souffle : une flèche qui rentre
            var w = Dir(WindFrom);
            var tip = c + w * (R - 22);
            var bw = c + w * (R + 2);
            var side = new Vector2(-w.Y, w.X) * 6;
            DrawColoredPolygon(new[] { tip, bw + side, bw - side }, new Color(0.55f, 0.78f, 0.95f, 0.9f));

            // le navire, à son cap : une étrave pointue, une poupe carrée
            var f = Dir(Heading);
            var s = new Vector2(-f.Y, f.X);
            var hull = new[] { c + f * 13, c + s * 5 + f * 2, c + s * 4 - f * 10, c - s * 4 - f * 10, c - s * 5 + f * 2 };
            DrawColoredPolygon(hull, new Color(0.10f, 0.08f, 0.06f, 0.85f));
            var outline = new Vector2[hull.Length + 1];
            for (int i = 0; i < hull.Length; i++) outline[i] = hull[i];
            outline[^1] = hull[0];
            DrawPolyline(outline, Ink, 1.4f, true);

            // l'échelle : le rayon, en milles
            string scale = RadiusMiles >= 1 ? $"rayon {RadiusMiles:0.#} M".Replace('.', ',') : $"rayon {RadiusMiles * 1852:0} m";
            DrawString(font, new Vector2(c.X - 34, Size.Y - 6) + new Vector2(1, 1), scale, HorizontalAlignment.Left, -1, 11, Shade);
            DrawString(font, new Vector2(c.X - 34, Size.Y - 6), scale, HorizontalAlignment.Left, -1, 11, Ink);
        }
    }
}
