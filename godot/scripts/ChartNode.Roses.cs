using Godot;
using System;
using System.Collections.Generic;
using NavalSim.Core;

namespace NavalSim;

/// <summary>
/// LES ROSES DES VENTS ET LEUR RÉSEAU — ce que porte toute carte marine du temps,
/// et ce dont la règle parallèle a besoin : on la fait « marcher » jusqu'à la rose la
/// plus proche pour y lire le rumb. Deux ou trois roses en pleine eau, et de chacune
/// les trente-deux rumbs tirés jusqu'au bord de la feuille, aux couleurs d'usage :
/// à l'encre noire les huit vents, en vert les demi-vents, en rouge les quarts. La
/// fleur de lys marque le nord ; la croix, l'est — vers la Terre sainte, comme sur les
/// roses de la Renaissance qu'on recopiait encore.
///
/// Imprimées : elles sont sur le papier, pas sous le voile de découverte. Posées une
/// fois au chargement, là où la mer est la plus loin de toute terre.
/// </summary>
public partial class ChartNode
{
    readonly List<(double X, double Z)> _roses = new();
    /// <summary>Les centres des roses, en mètres du monde.</summary>
    public IReadOnlyList<(double X, double Z)> Roses => _roses;
    /// <summary>Le rayon d'une rose, en pixels de feuille.</summary>
    public float RoseRadius => 62 * Q;

    void PlaceRoses()
    {
        _roses.Clear();
        var size = _vp.Size;
        var cand = new List<(Vector2 P, double D)>();
        // une grille de candidats, à l'écart des bords de la feuille
        for (int j = 1; j < 8; j++)
            for (int i = 1; i < 10; i++)
            {
                var p = new Vector2(size.X * i / 10f, size.Y * j / 8f);
                var (x, z) = ToWorld(p);
                if (_world.HeightAt(x, z) > -30) continue;
                cand.Add((p, _world.ShoreDistance(x, z)));
            }
        cand.Sort((a, b) => b.D.CompareTo(a.D));
        // la plus au large d'abord, puis d'autres, chacune à un bon tiers de feuille des précédentes
        var kept = new List<Vector2>();
        foreach (var c in cand)
        {
            bool far = true;
            foreach (var k in kept) if (k.DistanceTo(c.P) < size.X * 0.36f) { far = false; break; }
            if (!far) continue;
            kept.Add(c.P);
            if (kept.Count == 3) break;
        }
        foreach (var p in kept) _roses.Add(ToWorld(p));
    }

    /// <summary>Le papier imprimé : les rumbs sous les roses, les roses par-dessus. Sous la plume.</summary>
    sealed partial class RosePrint : Control
    {
        readonly ChartNode _c;
        public RosePrint(ChartNode c) { _c = c; MouseFilter = MouseFilterEnum.Ignore; }

        static readonly Color Black = new(0.20f, 0.15f, 0.11f), Green = new(0.20f, 0.42f, 0.24f), Red = new(0.62f, 0.16f, 0.12f);

        static Color Of(int point) => point % 4 == 0 ? Black : point % 2 == 0 ? Green : Red;

        public override void _Draw()
        {
            float span = Size.Length();
            // LE RÉSEAU : chaque rumb tiré d'une rose jusqu'au bord, très pâle — la feuille doit rester lisible
            foreach (var (x, z) in _c._roses)
            {
                var o = _c.ToChart(x, z);
                for (int k = 0; k < 32; k++)
                {
                    var d = Dir(k);
                    var col = Of(k);
                    DrawLine(o + d * _c.RoseRadius, o + d * span, new Color(col, k % 4 == 0 ? 0.20f : 0.13f), (k % 4 == 0 ? 1.0f : 0.7f) * Q);
                }
            }
            foreach (var (x, z) in _c._roses) Rose(_c.ToChart(x, z), _c.RoseRadius);
        }

        /// <summary>La direction d'un quart sur la feuille : le nord en haut, l'est à droite.</summary>
        static Vector2 Dir(int point)
        {
            double a = point * Math.PI / 16;
            return new Vector2((float)Math.Sin(a), -(float)Math.Cos(a));
        }

        void Rose(Vector2 o, float r)
        {
            var paper = new Color(0.93f, 0.88f, 0.76f, 0.92f);
            DrawCircle(o, r * 1.02f, paper);
            DrawArc(o, r, 0, Mathf.Tau, 96, Black, 1.4f * Q);
            DrawArc(o, r * 0.93f, 0, Mathf.Tau, 96, Black, 0.6f * Q);
            // les graduations : un trait par quart, plus long aux vents
            for (int k = 0; k < 32; k++)
            {
                var d = Dir(k);
                float l = k % 4 == 0 ? 0.12f : k % 2 == 0 ? 0.08f : 0.05f;
                DrawLine(o + d * r * (0.93f - l), o + d * r * 0.93f, Black, 0.8f * Q);
            }
            // les pointes : les quarts d'abord (rouges, courtes), puis les demi-vents, puis les vents
            foreach (int pass in new[] { 1, 2, 4 })
                for (int k = 0; k < 32; k++)
                {
                    if (pass == 1 && k % 2 == 0) continue;
                    if (pass == 2 && (k % 2 != 0 || k % 4 == 0)) continue;
                    if (pass == 4 && k % 4 != 0) continue;
                    float len = (pass == 4 ? (k % 8 == 0 ? 0.88f : 0.70f) : pass == 2 ? 0.55f : 0.40f) * r;
                    float w = (pass == 4 ? 0.13f : pass == 2 ? 0.09f : 0.06f) * r;
                    var d = Dir(k);
                    var side = new Vector2(-d.Y, d.X);
                    var tip = o + d * len;
                    var col = Of(k);
                    // deux moitiés, l'une pleine, l'autre plus claire : le relief gravé d'une pointe
                    DrawColoredPolygon(new[] { o, tip, o + side * w }, col);
                    DrawColoredPolygon(new[] { o, tip, o - side * w }, col.Lerp(paper, 0.55f));
                }
            DrawCircle(o, r * 0.05f, Black);
            // la fleur de lys au nord, la croix à l'est
            var n = o + Dir(0) * r * 1.0f;
            Lys(n + new Vector2(0, -r * 0.10f), r * 0.10f);
            var e = o + Dir(8) * r * 1.08f;
            float c = r * 0.06f;
            DrawLine(e - new Vector2(c, 0), e + new Vector2(c, 0), Red, 1.4f * Q);
            DrawLine(e - new Vector2(0, c * 1.5f), e + new Vector2(0, c), Red, 1.4f * Q);
        }

        void Lys(Vector2 p, float s)
        {
            // trois pétales et un lien : assez pour qu'on la reconnaisse, pas une gravure
            DrawColoredPolygon(new[] { p + new Vector2(0, -s * 1.6f), p + new Vector2(s * 0.35f, 0), p + new Vector2(-s * 0.35f, 0) }, Black);
            DrawColoredPolygon(new[] { p + new Vector2(-s * 0.2f, 0), p + new Vector2(-s * 1.1f, -s * 0.9f), p + new Vector2(-s * 0.9f, s * 0.2f) }, Black);
            DrawColoredPolygon(new[] { p + new Vector2(s * 0.2f, 0), p + new Vector2(s * 1.1f, -s * 0.9f), p + new Vector2(s * 0.9f, s * 0.2f) }, Black);
            DrawLine(p + new Vector2(-s * 0.6f, s * 0.25f), p + new Vector2(s * 0.6f, s * 0.25f), Black, 1.2f * Q);
        }
    }
}
