using Godot;
using System;
using System.Collections.Generic;
using NavalSim.Core;

namespace NavalSim;

/// <summary>
/// LA CARTE DE LA CHAMBRE DU CAPITAINE, et la main qui l'annote.
///
/// Le modèle du navire porte une carte VIERGE sur le bureau — son matériau
/// s'appelle <c>map_free</c>, et c'est par ce nom qu'on la retrouve. On ne
/// dessine donc pas un objet de plus : on remplace ce que cette feuille montre.
///
/// Elle se remplit de ce que ce bord a VU. La route perce le voile à mesure ;
/// le reste demeure blanc, ce qui est la seule chose honnête qu'une carte de
/// 1690 puisse faire. Par-dessus viennent les traits de plume et les mots du
/// capitaine, qui sont les siens et que rien d'autre ne connaît.
///
/// Tout est dessiné dans un <see cref="SubViewport"/> : la même texture sert à
/// la feuille posée sur le bureau ET à la carte qu'on ouvre en grand, si bien
/// qu'il n'y a jamais deux cartes à tenir d'accord.
/// </summary>
public partial class ChartNode : Node
{
    /// <summary>Le nom du matériau que l'artiste a donné à la feuille vierge.</summary>
    public const string MapMaterial = "map_free";

    /* LE CÔTÉ DE LA FEUILLE, en pixels. Deux mille contre mille : à la loupe,
       une carte de mille pixels sur cent trente kilomètres donne un pixel pour
       cent trente mètres, et la côte de la Jamaïque y devient une bouillie. Le
       relief lui-même en fait trois mille cent ; deux mille en garde l'essentiel
       pour un quart de la mémoire. */
    const int Side = 2048;
    /* De combien les TRAITS grossissent avec la feuille. Les lettres, elles, ne
       le suivent PAS : un nom de port a sa taille sur le papier, et doubler la
       finesse de la carte doit le rendre plus fin, pas plus gros — écrit d'abord
       avec ce facteur, deux notes couvraient la Jamaïque entière. */
    const float Q = Side / 1024f;
    /// <summary>Le voile de découverte, plus grossier : il n'a pas à être net.</summary>
    const int MaskSide = 256;

    readonly World _world;
    public readonly Logbook Book;

    SubViewport _vp = null!;
    TextureRect _land = null!;
    Control _ink = null!;
    ShaderMaterial _mat = null!;
    Image _mask = null!;
    ImageTexture _maskTex = null!;
    Font _font = null!;

    /// <summary>La texture à poser sur la feuille — et sur la grande carte.</summary>
    public Texture2D Texture => _vp.GetTexture();

    /* LE LIEU DE L ÉTAPE EN COURS, si une quête est en train. La carte ne
       connaît pas les quêtes : elle DEMANDE, et n'en garde rien. C'est ce qui
       permet de jouer sans aucune quête sans qu'une ligne d'ici ne s'en doute. */
    public Func<(double X, double Z, double R, string Name)?>? Aim;

    /* CE QUI FLOTTE ET QUI VAUT QU'ON Y AILLE — une bouteille à la dérive. Même
       règle que pour l'objectif : la carte DEMANDE, elle ne garde rien. Les
       cargaisons, elles, ne passent pas par là : elles ont une croix dans le
       carnet, parce qu'on les a APPRISES et qu'elles doivent survivre à la
       fermeture du jeu. */
    public Func<System.Collections.Generic.IEnumerable<(double X, double Z, bool Cargo)>>? Marks;

    /// <summary>Le coin haut-gauche et l'étendue de la carte, en mètres monde.</summary>
    /// <summary>La largeur de la région en mètres de jeu : l'image, pixel pour pixel.</summary>
    double _w;

    public ChartNode(World world, Logbook book) { _world = world; Book = book; }

    public override void _Ready()
    {
        /* la carte couvre toute la région : c'est la feuille du bord, pas un
           détail — et elle en a les PROPORTIONS DE L'IMAGE, dont les pixels sont
           carrés au milieu de la région, pour que le relief n'y soit pas étiré */
        _w = _world.Px * _world.ImgW;
        int hgt = (int)Math.Round((double)Side * _world.ImgH / _world.ImgW);
        _vp = new SubViewport
        {
            Size = new Vector2I(Side, hgt),
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
            TransparentBg = false
        };
        AddChild(_vp);

        _mask = Image.CreateEmpty(MaskSide, Math.Max(1, MaskSide * hgt / Side), false, Image.Format.L8);
        _mask.Fill(new Color(0, 0, 0));
        _maskTex = ImageTexture.CreateFromImage(_mask);

        _mat = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/chart.gdshader") };
        _mat.SetShaderParameter("u_mask", _maskTex);
        _land = new TextureRect
        {
            Texture = Relief(Side, hgt),
            Material = _mat,
            Size = new Vector2(Side, hgt),
            StretchMode = TextureRect.StretchModeEnum.Scale
        };
        _vp.AddChild(_land);

        _font = ThemeDB.FallbackFont;
        _ink = new Pen(this) { Size = new Vector2(Side, hgt) };
        _vp.AddChild(_ink);

        foreach (var (x, z) in Book.Track) Reveal(x, z);
        Refresh();
    }

    /// <summary>
    /// LE RELIEF EN TEINTES DE CARTE — le portage de <c>chartImage</c> : la terre
    /// dans le chamois des cartes, ombrée du nord-ouest, et les hauts-fonds en
    /// bande pâle. Le grand fond reste TRANSPARENT : c'est le papier qu'on voit
    /// dessous, et une carte marine ne colorie pas l'océan.
    /// </summary>
    ImageTexture Relief(int w, int h)
    {
        var img = Image.CreateEmpty(w, h, false, Image.Format.Rgba8);
        double px = _world.Px;
        double Hgt(int i, int j)
        {
            int pi = Math.Clamp(i * _world.ImgW / w, 0, _world.ImgW - 1);
            int pj = Math.Clamp(j * _world.ImgH / h, 0, _world.ImgH - 1);
            return _world.Lut(GreyAt(pi, pj));
        }
        for (int j = 0; j < h; j++)
            for (int i = 0; i < w; i++)
            {
                double y = Hgt(i, j);
                if (y >= 0)
                {
                    // l'ombre portée du relief, prise sur la diagonale nord-ouest
                    double sh = Math.Clamp((Hgt(i - 1, j - 1) - Hgt(i + 1, j + 1)) / (px * _world.ImgW / w) * 6, -1, 1);
                    double f = 1 + 0.35 * sh - Math.Min(0.25, y / 2400);
                    img.SetPixel(i, j, new Color(
                        (float)(184 / 255.0 * f), (float)(162 / 255.0 * f), (float)(113 / 255.0 * f), 1));
                }
                else if (y > -20)
                    img.SetPixel(i, j, new Color(90 / 255f, 150 / 255f, 175 / 255f,
                        (float)((80 * (1 + y / 20) + 20) / 255.0)));
            }
        return ImageTexture.CreateFromImage(img);
    }

    byte GreyAt(int i, int j) => _world.GreyByte(i, j);

    /// <summary>Combien de mètres du monde vaut un pixel de la feuille.</summary>
    public double MetresPerPixel => _w / _vp.Size.X;

    /* MONDE → FEUILLE PAR LA CONVERSION MÊME DU RELIEF, et non par une seconde.
       La carte avait la sienne, un rectangle de mètres pris entre deux coins —
       avec l'est à GAUCHE (l'est est −x, et le plus petit x était pris pour le
       bord gauche) et le sud en HAUT. Tout ce qu'elle posait — la route, les
       ports, le voile percé, la plume, les croix — tournait donc d'un demi-tour
       par rapport à l'île qu'elle dessinait, et restait d'accord avec lui-même :
       rien ne clochait tant qu'on ne comparait pas une marque au rivage (signalé :
       « Port-Royal et Passage Fort au mauvais endroit »). Le relief, lui, est
       peint pixel à pixel par World.PixelAt ; les marques passent maintenant par
       elle, si bien qu'elles ne peuvent plus se séparer de la côte. */

    /// <summary>Monde → pixels de la carte.</summary>
    public Vector2 ToChart(double x, double z)
    {
        var (pi, pj) = _world.PixelAt(x, z);
        return new((float)(pi / _world.ImgW * _vp.Size.X), (float)(pj / _world.ImgH * _vp.Size.Y));
    }

    /// <summary>Et l'inverse : où la plume a touché la feuille — l'inverse exact de PixelAt, puis de Geo.Fix.</summary>
    public (double X, double Z) ToWorld(Vector2 p)
    {
        var E = _world.Relief;
        double lon = E.West + p.X / _vp.Size.X * (E.East - E.West);
        double lat = E.North - p.Y / _vp.Size.Y * (E.North - E.South);
        return _world.Geo.ToXZ(lat, lon);
    }

    /// <summary>
    /// DÉBOGAGE : toute l'île dévoilée, ou le voile rendu à ce que ce bord a vu.
    /// Rien n'est écrit dans le carnet — c'est un regard de l'auteur, pas une
    /// découverte du capitaine, et le rouvrir le lendemain rend la carte honnête.
    /// </summary>
    public bool Unveiled { get; private set; }

    public void Unveil(bool on)
    {
        Unveiled = on;
        _mask.Fill(on ? new Color(1, 1, 1) : new Color(0, 0, 0));
        if (!on) foreach (var (x, z) in Book.Track) Reveal(x, z);
        Refresh();
    }

    /// <summary>Percer le voile autour d'un point de route.</summary>
    void Reveal(double x, double z)
    {
        var c = ToChart(x, z);
        float mx = c.X * MaskSide / _vp.Size.X, my = c.Y * _mask.GetHeight() / _vp.Size.Y;
        float r = (float)(Logbook.Sight / _w * MaskSide);
        int r0 = Mathf.CeilToInt(r);
        for (int j = -r0; j <= r0; j++)
            for (int i = -r0; i <= r0; i++)
            {
                int px = (int)mx + i, py = (int)my + j;
                if (px < 0 || py < 0 || px >= MaskSide || py >= _mask.GetHeight()) continue;
                float d = Mathf.Sqrt(i * i + j * j) / Math.Max(1e-3f, r);
                // le regard porte moins loin sur les bords : un dégradé, pas un disque
                float v = Mathf.Clamp(1.25f - d * 1.25f, 0, 1);
                if (v > _mask.GetPixel(px, py).R) _mask.SetPixel(px, py, new Color(v, v, v));
            }
    }

    /// <summary>Un relevé de position : si la carte gagne un point, elle se redessine.</summary>
    public void Sail(double x, double z)
    {
        if (!Book.Sail(x, z)) return;
        if (!Unveiled) Reveal(x, z);
        Refresh();
    }

    public void Refresh()
    {
        _maskTex.Update(_mask);
        _ink.QueueRedraw();
    }

    /* LA PLUME. Tout ce que le capitaine ajoute est dessiné ici, par-dessus la
       carte : sa route, ses traits et ses mots. Un nœud à part, pour que le
       fond de carte — qui ne bouge jamais — n'ait pas à être refait. */
    sealed partial class Pen : Control
    {
        readonly ChartNode _c;
        public Pen(ChartNode c) { _c = c; MouseFilter = MouseFilterEnum.Ignore; }

        static readonly Color[] Inks =
        {
            new(0.28f, 0.19f, 0.12f),      // la sépia du bord
            new(0.62f, 0.14f, 0.10f),      // le rouge d'un danger
            new(0.16f, 0.26f, 0.45f)       // le bleu d'une route
        };

        public override void _Draw()
        {
            var b = _c.Book;
            // la route parcourue, au trait fin : c'est elle qui justifie le reste
            if (b.Track.Count > 1)
            {
                var pts = new Vector2[b.Track.Count];
                for (int i = 0; i < pts.Length; i++) pts[i] = _c.ToChart(b.Track[i].X, b.Track[i].Z);
                DrawPolyline(pts, new Color(0.30f, 0.22f, 0.16f, 0.55f), 1.5f * Q, true);
            }
            // les ports touchés, d'un rond et de leur nom
            foreach (string key in b.Ports)
            {
                var isl = _c._world.ByKey(key);
                if (isl == null) continue;
                var p = _c.ToChart(isl.X, isl.Z);
                DrawCircle(p, 4.5f * Q, new Color(0.28f, 0.19f, 0.12f), false, 1.6f * Q);
                DrawString(_c._font, p + new Vector2(7 * Q, 4 * Q), isl.Name,
                    HorizontalAlignment.Left, -1, 15, new Color(0.26f, 0.18f, 0.11f));
            }
            /* LES CROIX : ce qu'une carte de bouteille a appris. Une croix, pas
               un rond — un rond est un lieu qu'on a relevé soi-même, une croix
               est un lieu qu'on tient de quelqu'un d'autre. */
            foreach (var x in b.Crosses)
            {
                var p = _c.ToChart(x.X, x.Z);
                var gold = new Color(0.86f, 0.62f, 0.16f);
                float r = 5f * Q;
                DrawLine(p - new Vector2(r, r), p + new Vector2(r, r), gold, 2.2f * Q);
                DrawLine(p + new Vector2(r, -r), p + new Vector2(-r, r), gold, 2.2f * Q);
                if (x.Text.Length > 0)
                    DrawString(_c._font, p + new Vector2(r + 4 * Q, 4 * Q), x.Text,
                        HorizontalAlignment.Left, -1, 14, gold);
            }

            // une bouteille à la dérive : un point pâle et son cercle, car on ne
            // sait jamais qu'à peu près où elle flotte
            if (_c.Marks != null)
                foreach (var m in _c.Marks())
                {
                    if (m.Cargo) continue;
                    var p = _c.ToChart(m.X, m.Z);
                    var pale = new Color(0.85f, 0.94f, 0.92f);
                    DrawCircle(p, 2f * Q, pale);
                    DrawCircle(p, 5f * Q, new Color(pale, 0.55f), false, 1f * Q);
                }

            // les traits de plume
            foreach (var s in b.Strokes)
            {
                if (s.Pts.Count < 2)
                {
                    if (s.Pts.Count == 1)
                        DrawCircle(_c.ToChart(s.Pts[0].X, s.Pts[0].Z), 2f * Q, Inks[Math.Clamp(s.Ink, 0, 2)]);
                    continue;
                }
                var pts = new Vector2[s.Pts.Count];
                for (int i = 0; i < pts.Length; i++) pts[i] = _c.ToChart(s.Pts[i].X, s.Pts[i].Z);
                DrawPolyline(pts, Inks[Math.Clamp(s.Ink, 0, 2)], 2.2f * Q, true);
            }
            /* LE CERCLE DORÉ de l'étape en cours, par-dessus tout le reste : ce
               qu'on cherche sur une carte doit se voir avant ce qu'on y a déjà
               écrit. Son rayon est celui de l'objectif, en vraies toises. */
            if (_c.Aim?.Invoke() is { } aim)
            {
                var p = _c.ToChart(aim.X, aim.Z);
                float r = (float)Math.Max(6 * Q, aim.R / _c.MetresPerPixel);
                var gold = new Color(0.86f, 0.70f, 0.28f);
                DrawCircle(p, r, gold, false, 2.0f * Q);
                DrawCircle(p, 2.4f * Q, gold);
                if (aim.Name.Length > 0)
                    DrawString(_c._font, p + new Vector2(r + 5 * Q, 4 * Q), aim.Name,
                        HorizontalAlignment.Left, -1, 15, gold);
            }
            // et ses mots, à l'endroit qu'ils désignent
            foreach (var n in b.Notes)
            {
                var p = _c.ToChart(n.X, n.Z);
                DrawString(_c._font, p + new Vector2(6 * Q, 5 * Q), n.Text,
                    HorizontalAlignment.Left, -1, 16, new Color(0.24f, 0.16f, 0.10f));
                DrawLine(p, p + new Vector2(4 * Q, 3 * Q), new Color(0.24f, 0.16f, 0.10f), 1.4f * Q);
            }
        }
    }
}
