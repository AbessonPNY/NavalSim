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
    /// <summary>
    /// La demi-largeur du bec, en unités de carte : réglée au menu (Carte →
    /// Épaisseur de la plume), 1,5 par défaut. Plein d'un trait : le double ;
    /// délié : le filet de dessous.
    /// </summary>
    public float PenWidth = 1.5f;

    /// <summary>La main qui écrit les notes ; la police du moteur si le fichier manque.</summary>
    Font? _hand;

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

    /// <summary>Le point estimé et son incertitude (un écart-type, nord-sud et est-ouest) — nul sans estime.</summary>
    public Func<(double X, double Z, double SN, double SE)?>? Where;
    /// <summary>La position vraie : dessinée au débogage seulement, quand tout est dévoilé.</summary>
    public Func<(double X, double Z)?>? Truth;

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
        // l'écriture du capitaine, lue une fois pour tous ses usagers (HandFont)
        _hand = HandFont.Get();
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
        _screenInk?.QueueRedraw();
    }

    Pen? _screenInk;

    /// <summary>
    /// L'encre de la carte OUVERTE, tracée à l'écran : un calque à poser sur la
    /// vue, que <paramref name="map"/> et <paramref name="k"/> tiennent à la loupe.
    /// Tant qu'il est là, la feuille se dessine sans encre — elle serait sinon
    /// dessous, pixelisée, et doublerait chaque trait.
    /// </summary>
    public Control ScreenInk()
    {
        _screenInk ??= new Pen(this) { AnchorRight = 1, AnchorBottom = 1 };
        return _screenInk;
    }

    /// <summary>
    /// La demi-largeur du bec en mètres de carte, pour un trait commencé à la
    /// loupe <paramref name="k"/> : ce que la plume donne à l'écran à ce moment.
    /// </summary>
    public double NibMetres(float k) => PenWidth * Q * Math.Min(k, 1.5f) / Math.Max(k, 1e-3f) * MetresPerPixel;

    public void SetScreenInk(Func<Vector2, Vector2> map, float k, bool on)
    {
        if (_screenInk == null) return;
        _screenInk.Map = map;
        _screenInk.K = k;
        _screenInk.QueueRedraw();
        if (_ink.Visible == on) _ink.Visible = !on;
    }

    /* LA PLUME. Tout ce que le capitaine ajoute est dessiné ici, par-dessus la
       carte : sa route, ses traits et ses mots. Un nœud à part, pour que le
       fond de carte — qui ne bouge jamais — n'ait pas à être refait. */
    sealed partial class Pen : Control
    {
        readonly ChartNode _c;
        public Pen(ChartNode c) { _c = c; MouseFilter = MouseFilterEnum.Ignore; }

        /* LA MÊME ENCRE À DEUX ÉCHELLES. Sur la feuille (le bureau, la texture),
           rien ne change : Map est l'identité. Sur la carte OUVERTE, elle est
           retracée à la résolution de l'écran — agrandie depuis une feuille de
           2 048 pixels, où un pixel vaut soixante mètres, elle se lisait en
           escaliers à la loupe (signalé). Map porte les pixels de feuille vers
           ceux de l'écran ; K est leur rapport. Les traits grossissent avec la
           loupe, mais pas au-delà d'une fois et demie : une plume n'est pas un
           pinceau. Les lettres de même, entre 0,7 et 1,3. */
        public Func<Vector2, Vector2> Map = p => p;
        public float K = 1;
        float Wk => Math.Min(K, 1.5f);
        int Fs(int n) => Math.Max(10, (int)Math.Round(n * Math.Clamp(K, 0.7f, 1.3f)));
        Vector2 At(double x, double z) => Map(_c.ToChart(x, z));

        static readonly Color[] Inks =
        {
            new(0.28f, 0.19f, 0.12f),      // la sépia du bord
            new(0.62f, 0.14f, 0.10f),      // le rouge d'un danger
            new(0.16f, 0.26f, 0.45f)       // le bleu d'une route
        };

        public override void _Draw()
        {
            var b = _c.Book;
            /* LA ROUTE PORTÉE À LA PLUME, au trait fin : la route ESTIMÉE. La vraie
               (Track) ne se dessine pas — elle ne sert qu'à percer le voile. */
            var route = b.Estim.Count > 1 ? b.Estim : null;
            if (route != null)
            {
                var pts = new Vector2[route.Count];
                for (int i = 0; i < pts.Length; i++) pts[i] = At(route[i].X, route[i].Z);
                DrawPolyline(pts, new Color(0.30f, 0.22f, 0.16f, 0.55f), 1.5f * Q * Wk, true);
            }
            // les ports touchés, d'un rond et de leur nom
            foreach (string key in b.Ports)
            {
                var isl = _c._world.ByKey(key);
                if (isl == null) continue;
                var p = At(isl.X, isl.Z);
                DrawCircle(p, 4.5f * Q * Wk, new Color(0.28f, 0.19f, 0.12f), false, 1.6f * Q * Wk);
                DrawString(_c._font, p + new Vector2(7 * Q * Wk, 4 * Q * Wk), isl.Name,
                    HorizontalAlignment.Left, -1, Fs(15), new Color(0.26f, 0.18f, 0.11f));
            }
            /* LES CROIX : ce qu'une carte de bouteille a appris. Une croix, pas
               un rond — un rond est un lieu qu'on a relevé soi-même, une croix
               est un lieu qu'on tient de quelqu'un d'autre. */
            foreach (var x in b.Crosses)
            {
                var p = At(x.X, x.Z);
                var gold = new Color(0.86f, 0.62f, 0.16f);
                float r = 5f * Q * Wk;
                DrawLine(p - new Vector2(r, r), p + new Vector2(r, r), gold, 2.2f * Q * Wk);
                DrawLine(p + new Vector2(r, -r), p + new Vector2(-r, r), gold, 2.2f * Q * Wk);
                if (x.Text.Length > 0)
                    DrawString(_c._font, p + new Vector2(r + 4 * Q * Wk, 4 * Q * Wk), x.Text,
                        HorizontalAlignment.Left, -1, Fs(14), gold);
            }

            // une bouteille à la dérive : un point pâle et son cercle, car on ne
            // sait jamais qu'à peu près où elle flotte
            if (_c.Marks != null)
                foreach (var m in _c.Marks())
                {
                    if (m.Cargo) continue;
                    var p = At(m.X, m.Z);
                    var pale = new Color(0.85f, 0.94f, 0.92f);
                    DrawCircle(p, 2f * Q * Wk, pale);
                    DrawCircle(p, 5f * Q * Wk, new Color(pale, 0.55f), false, 1f * Q * Wk);
                }

            // les traits de plume
            foreach (var s in b.Strokes)
            {
                if (s.Pts.Count < 2)
                {
                    if (s.Pts.Count == 1)
                        DrawCircle(At(s.Pts[0].X, s.Pts[0].Z), 2f * Q * Wk, Inks[Math.Clamp(s.Ink, 0, 2)]);
                    continue;
                }
                var pts = new Vector2[s.Pts.Count];
                for (int i = 0; i < pts.Length; i++) pts[i] = At(s.Pts[i].X, s.Pts[i].Z);
                Nib(pts, Inks[Math.Clamp(s.Ink, 0, 2)], s.W);
            }
            /* LE CERCLE DORÉ de l'étape en cours, par-dessus tout le reste : ce
               qu'on cherche sur une carte doit se voir avant ce qu'on y a déjà
               écrit. Son rayon est celui de l'objectif, en vraies toises. */
            if (_c.Aim?.Invoke() is { } aim)
            {
                var p = At(aim.X, aim.Z);
                float r = (float)Math.Max(6 * Q * Wk, aim.R / _c.MetresPerPixel * K);
                var gold = new Color(0.86f, 0.70f, 0.28f);
                DrawCircle(p, r, gold, false, 2.0f * Q * Wk);
                DrawCircle(p, 2.4f * Q * Wk, gold);
                if (aim.Name.Length > 0)
                    DrawString(_c._font, p + new Vector2(r + 5 * Q * Wk, 4 * Q * Wk), aim.Name,
                        HorizontalAlignment.Left, -1, Fs(15), gold);
            }
            /* OÙ L'ON CROIT ÊTRE : une petite croix de plume, et l'ellipse de ce
               qu'on n'en sait pas — un écart-type, plus large en longitude qu'en
               latitude dès qu'on a pris la hauteur. */
            if (_c.Where?.Invoke() is { } w)
            {
                var p = At(w.X, w.Z);
                var ink = new Color(0.28f, 0.19f, 0.12f);
                float rx = (float)(w.SE / _c.MetresPerPixel * K), ry = (float)(w.SN / _c.MetresPerPixel * K);
                if (rx > 3 || ry > 3)
                {
                    var ring = new Vector2[49];
                    for (int i = 0; i < ring.Length; i++)
                    {
                        float a = i * Mathf.Tau / 48;
                        ring[i] = p + new Vector2(Mathf.Cos(a) * Math.Max(rx, 1), Mathf.Sin(a) * Math.Max(ry, 1));
                    }
                    DrawPolyline(ring, new Color(ink, 0.55f), 1.2f * Q * Wk, true);
                }
                float c = 4.5f * Q * Wk;
                DrawLine(p - new Vector2(c, 0), p + new Vector2(c, 0), ink, 1.6f * Q * Wk);
                DrawLine(p - new Vector2(0, c), p + new Vector2(0, c), ink, 1.6f * Q * Wk);
                DrawCircle(p, 1.6f * Q * Wk, ink);
            }
            // la vérité, au débogage : un point rouge
            if (_c.Unveiled && _c.Truth?.Invoke() is { } t)
                DrawCircle(At(t.X, t.Z), 3f * Q * Wk, new Color(0.85f, 0.12f, 0.08f));

            // et ses mots, à l'endroit qu'ils désignent
            foreach (var n in b.Notes)
            {
                var p = At(n.X, n.Z);
                DrawString(_c._hand ?? _c._font, p + new Vector2(6 * Q * Wk, 6 * Q * Wk), n.Text,
                    // les notes à TAILLE FIXE : lisibles à toute loupe, elles ne rétrécissent pas au dézoom
                    HorizontalAlignment.Left, -1, _c._hand != null ? 28 : 20, new Color(0.24f, 0.16f, 0.10f));
                DrawLine(p, p + new Vector2(4 * Q * Wk, 3 * Q * Wk), new Color(0.24f, 0.16f, 0.10f), 1.4f * Q * Wk);
            }
        }

        /* LA PLUME BISEAUTÉE : un bec large tenu à 45°, qui ne tourne pas avec
           la main. Chaque segment est le parallélogramme que balaie ce bec — plein
           quand on trace en travers du biseau, un cheveu quand on trace dans son
           fil. C'est tout le secret de la calligraphie, et il n'y a rien d'autre à
           calculer. Un filet dessous, pour que le trait ne se rompe jamais. */
        Color[]? _one4;

        void Nib(Vector2[] pts, Color ink, double w)
        {
            /* UN TRAIT GARDE SA PLUME : sa largeur est celle du tracé, en mètres de
               carte, et suit la loupe comme ses lettres. Tracée à la loupe du
               moment, une écriture fine faite de près tournait au pâté de loin
               (signalé). */
            float h = w > 0 ? (float)(w / _c.MetresPerPixel * K) : _c.PenWidth * Q * Wk;
            var half = new Vector2(1, -1).Normalized() * h;
            var quad = new Vector2[4];
            for (int i = 0; i + 1 < pts.Length; i++)
            {
                var a = pts[i];
                var c = pts[i + 1];
                if (a.DistanceSquaredTo(c) < 1e-6f) continue;
                quad[0] = a - half; quad[1] = a + half; quad[2] = c + half; quad[3] = c - half;
                /* En primitive à quatre sommets, qui ne triangule pas : un
                   parallélogramme presque plat (tracé dans le fil du bec) faisait
                   échouer la triangulation de DrawColoredPolygon. */
                _one4 ??= new Color[4];
                _one4[0] = _one4[1] = _one4[2] = _one4[3] = ink;
                DrawPrimitive(quad, _one4, null);
            }
            DrawPolyline(pts, ink, w > 0 ? Math.Max(0.35f, 0.3f * h) : 0.45f * Q * Wk, true);
        }
    }
}
