using Godot;
using System;
using System.Collections.Generic;
using NavalSim.Core;

namespace NavalSim;

/// <summary>
/// LE JOURNAL DE BORD, DESSINÉ — une feuille rendue dans un SubViewport, comme la
/// carte de la chambre, et pour la même raison : la MÊME texture sert au livre
/// posé sur le bureau et à la page qu'on ouvre en grand, si bien qu'il n'y a
/// jamais deux journaux à tenir d'accord.
///
/// LA PAGE COULE. Un bloc de texte est enroulé à la largeur de la feuille, un
/// bloc de dessin prend sa hauteur, et le suivant reprend dessous : l'ORDRE DES
/// BLOCS EST LA MISE EN PAGE. Rien à positionner, rien à faire flotter — ce qui
/// est la seule façon qu'une main qui écrit puisse passer au croquis et revenir
/// sans avoir à ranger quoi que ce soit.
///
/// Ce qui déborde la feuille n'est pas perdu : la page défile sous le regard, et
/// le curseur y reste visible. Une vraie page de livre s'arrêterait et il
/// faudrait tourner — on le fera le jour où le livre aura des feuilles ; pour
/// l'instant une page est une page, aussi longue qu'on l'écrit.
/// </summary>
public partial class JournalNode : Node
{
    /// <summary>Le côté de la feuille, en pixels. Deux mille : la plume y est nette à la loupe.</summary>
    const int W = 1100, H = 1500;
    /// <summary>La marge, en pixels : une page de journal en a une large.</summary>
    const float Margin = 90;

    public readonly Journal Book;

    SubViewport _vp = null!;
    Sheet _sheet = null!;
    Font _font = null!;
    Font? _hand;

    /// <summary>La texture à poser sur le livre — et sur la page ouverte.</summary>
    public Texture2D Texture => _vp.GetTexture();

    /// <summary>L'encre courante : 0 sépia, 1 rouge, 2 bleu — la même échelle que la carte.</summary>
    public int Ink;

    /// <summary>Où en est le défilement, en pixels de page.</summary>
    public float Scroll;

    /// <summary>La hauteur qu'a prise la page à la dernière peinture.</summary>
    public float Written { get; private set; }

    public JournalNode(Journal book) { Book = book; }

    public override void _Ready()
    {
        _vp = new SubViewport
        {
            Size = new Vector2I(W, H),
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
            TransparentBg = false,
        };
        AddChild(_vp);
        _font = ThemeDB.FallbackFont;
        _hand = HandFont.Get();
        _sheet = new Sheet { _j = this, Size = new Vector2(W, H) };
        _vp.AddChild(_sheet);
    }

    public void Refresh() => _sheet?.QueueRedraw();

    // ------------------------------------------------------------------
    //  CE QUE LA MAIN FAIT
    // ------------------------------------------------------------------

    /// <summary>Écrire un caractère à la fin de la page.</summary>
    public void Type(string s)
    {
        if (Book.Current is not { } p) return;
        p.Pen().Text += s;
        Refresh();
    }

    /// <summary>Effacer le dernier caractère — ou le dernier trait d'un croquis.</summary>
    public void Back()
    {
        if (Book.Current is not { } p || p.Blocks.Count == 0) return;
        var b = p.Blocks[^1];
        if (b.Drawing)
        {
            if (b.Strokes.Count > 0) b.Strokes.RemoveAt(b.Strokes.Count - 1);
            else p.Blocks.RemoveAt(p.Blocks.Count - 1);
        }
        else if (b.Text.Length > 0) b.Text = b.Text[..^1];
        else p.Blocks.RemoveAt(p.Blocks.Count - 1);
        Refresh();
    }

    /// <summary>
    /// Ouvrir une zone de dessin sous ce qui est écrit, ou la refermer si l'on y
    /// est déjà — le texte reprend alors dessous.
    /// </summary>
    public bool Sketch()
    {
        if (Book.Current is not { } p) return false;
        if (p.Blocks.Count > 0 && p.Blocks[^1].Drawing)
        {
            p.Blocks.Add(new PageBlock());          // on referme : du texte à la suite
            Refresh();
            return false;
        }
        p.Blocks.Add(new PageBlock { Drawing = true });
        Refresh();
        return true;
    }

    /// <summary>La zone de dessin ouverte, s'il y en a une.</summary>
    public PageBlock? OpenSketch =>
        Book.Current is { } p && p.Blocks.Count > 0 && p.Blocks[^1].Drawing ? p.Blocks[^1] : null;

    /// <summary>
    /// Où tombe un point de la feuille dans la zone de dessin ouverte, de 0 à 1 —
    /// nul s'il tombe ailleurs. <paramref name="uv"/> est le point sur la page
    /// entière, défilement compris.
    /// </summary>
    public (double X, double Y)? InSketch(Vector2 uv)
    {
        if (OpenSketch is not { } b) return null;
        var (top, height) = _sheet.SketchRect(b);
        if (height <= 0) return null;
        float y = uv.Y * H;
        if (y < top || y > top + height) return null;
        double x = (uv.X * W - Margin) / (W - 2 * Margin);
        return (Math.Clamp(x, 0, 1), Math.Clamp((y - top) / height, 0, 1));
    }

    // ------------------------------------------------------------------
    //  LA FEUILLE
    // ------------------------------------------------------------------

    /* LES TROIS ENCRES, les mêmes que la carte : la sépia du bord, le rouge du
       danger, le bleu d'une route. */
    static readonly Color[] Inks =
    {
        new(0.28f, 0.20f, 0.14f), new(0.55f, 0.16f, 0.12f), new(0.17f, 0.24f, 0.44f),
    };

    sealed partial class Sheet : Control
    {
        public JournalNode _j = null!;
        readonly Dictionary<PageBlock, (float Top, float H)> _boxes = new();

        public (float Top, float H) SketchRect(PageBlock b) =>
            _boxes.TryGetValue(b, out var r) ? r : (0, 0);

        public override void _Draw()
        {
            _boxes.Clear();
            /* LE PAPIER. Un blanc cassé qui tire sur le chamois, et non du blanc :
               une page de 1690 est du chiffon, et le blanc pur d'un écran la
               trahirait plus sûrement que n'importe quel détail manquant. */
            DrawRect(new Rect2(0, 0, W, H), new Color(0.90f, 0.86f, 0.77f));

            var font = _j._hand ?? _j._font;
            float x0 = Margin, wide = W - 2 * Margin;
            float y = Margin - _j.Scroll;

            if (_j.Book.Current is not { } page)
            {
                DrawString(font, new Vector2(x0, Margin + 40), "Aucune page ouverte",
                    HorizontalAlignment.Left, wide, 34, new Color(0.45f, 0.40f, 0.34f, 0.7f));
                return;
            }

            /* LA DATE EN TÊTE, et c'est la seule chose que la page écrit d'elle-même
               avant qu'on y touche. Soulignée d'un filet, comme on le faisait. */
            DrawString(font, new Vector2(x0, y + 34), page.Date,
                HorizontalAlignment.Left, wide, 40, Inks[0]);
            y += 54;
            DrawLine(new Vector2(x0, y), new Vector2(W - Margin, y), new Color(Inks[0], 0.45f), 2);
            y += 26;

            foreach (var b in page.Blocks)
            {
                if (b.Drawing)
                {
                    float h = (float)(b.Height * H);
                    _boxes[b] = (y, h);
                    /* LE CADRE D'UN CROQUIS est un filet léger et non un trait : il
                       dit où la main peut aller, il ne doit pas se lire comme une
                       vignette collée dans le cahier. */
                    DrawRect(new Rect2(x0, y, wide, h), new Color(Inks[0], 0.16f), false, 1.5f);
                    foreach (var s in b.Strokes)
                    {
                        if (s.Pts.Count < 2) continue;
                        var pts = new Vector2[s.Pts.Count];
                        for (int i = 0; i < s.Pts.Count; i++)
                            pts[i] = new Vector2(x0 + (float)s.Pts[i].X * wide, y + (float)s.Pts[i].Y * h);
                        DrawPolyline(pts, Inks[Math.Clamp(s.Ink, 0, Inks.Length - 1)],
                            Math.Max(1f, (float)(s.W * W)), true);
                    }
                    y += h + 22;
                }
                else
                {
                    foreach (string line in Wrap(font, b.Text, wide, 30))
                    {
                        if (line.Length > 0)
                            DrawString(font, new Vector2(x0, y + 24), line,
                                HorizontalAlignment.Left, wide, 30, Inks[0]);
                        y += 38;
                    }
                }
            }
            _j.Written = y + _j.Scroll;

            /* LE CURSEUR, un trait de plume qui attend. Il ne clignote pas : une
               plume posée sur le papier ne clignote pas, et le clignotement est un
               tic d'écran qui n'a rien à faire ici. */
            if (_j.OpenSketch == null)
            {
                var last = page.Blocks.Count > 0 && !page.Blocks[^1].Drawing ? page.Blocks[^1] : null;
                float cx = x0, cy = y - 38;
                if (last != null)
                {
                    var lines = Wrap(font, last.Text, wide, 30);
                    string tail = lines.Count > 0 ? lines[^1] : "";
                    cx = x0 + font.GetStringSize(tail, HorizontalAlignment.Left, -1, 30).X;
                    cy = y - 38;
                }
                DrawLine(new Vector2(cx + 3, cy + 2), new Vector2(cx + 3, cy + 28), new Color(Inks[0], 0.75f), 2);
            }
        }

        /* ENROULER À LA LARGEUR DE LA FEUILLE, mot à mot. Godot sait le faire dans
           un Label, mais il faut ici connaître les LIGNES — pour poser le curseur
           au bout de la dernière et pour savoir de combien la page a grandi. */
        static List<string> Wrap(Font f, string text, float wide, int size)
        {
            var outp = new List<string>();
            foreach (string para in text.Split('\n'))
            {
                if (para.Length == 0) { outp.Add(""); continue; }
                string line = "";
                foreach (string word in para.Split(' '))
                {
                    string essai = line.Length == 0 ? word : line + " " + word;
                    if (f.GetStringSize(essai, HorizontalAlignment.Left, -1, size).X > wide && line.Length > 0)
                    {
                        outp.Add(line);
                        line = word;
                    }
                    else line = essai;
                }
                outp.Add(line);
            }
            if (outp.Count == 0) outp.Add("");
            return outp;
        }
    }
}
