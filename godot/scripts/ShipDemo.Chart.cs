using Godot;
using System;
using NavalSim.Core;

namespace NavalSim;

/// <summary>
/// LA CARTE OUVERTE, et la main du capitaine dessus.
///
/// La même texture que la feuille du bureau — il n'y a qu'une carte à bord, et
/// c'est celle-là. Ouverte, elle occupe l'écran : on ne dessine pas à la plume
/// sur une feuille vue de biais à quarante centimètres.
///
/// CE QU'ON TRACE EST GARDÉ EN MÈTRES DU MONDE, pas en pixels : un trait posé
/// sur un haut-fond y reste quelle que soit la taille de la fenêtre, et la
/// feuille du bureau le montre au même endroit. C'est la même règle que partout
/// ici — une définition, plusieurs usagers.
/// </summary>
public partial class ShipDemo : Node3D
{
    CanvasLayer? _chartLayer;
    TextureRect? _chartView;
    Label? _chartHint;
    LineEdit? _chartEntry;
    Stroke? _drawing;
    bool _chartOpen;
    int _ink;
    /* LE ZOOM, et il n'est pas décoratif : la feuille couvre toute la mer des
       Caraïbes, où l'horizon d'une vigie fait vingt pixels. Sans loupe, une
       carte de bord ne montre rien de ce qu'on vient de relever. La largeur
       regardée, en mètres, et le point qu'on regarde. */
    double _chartSpan = 42000;
    Vector2 _chartAt;
    AtlasTexture? _atlas;

    static readonly string[] InkNames = { "sépia", "rouge", "bleu" };

    void BuildChartView(CanvasLayer layer)
    {
        _chartLayer = new CanvasLayer { Layer = 2, Visible = false };
        AddChild(_chartLayer);

        var root = new Control { AnchorRight = 1, AnchorBottom = 1 };
        _chartLayer.AddChild(root);
        root.AddChild(new ColorRect { AnchorRight = 1, AnchorBottom = 1, Color = new Color(0.03f, 0.03f, 0.04f, 0.92f) });

        /* La carte garde ses proportions : une carte étirée ment sur les
           relèvements, ce qui est la seule chose qu'on lui demande de ne pas
           faire. */
        _chartView = new TextureRect
        {
            AnchorLeft = 0.5f, AnchorRight = 0.5f, AnchorTop = 0.5f, AnchorBottom = 0.5f,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            MouseFilter = Control.MouseFilterEnum.Stop
        };
        root.AddChild(_chartView);

        _chartHint = new Label
        {
            AnchorTop = 1, AnchorBottom = 1, AnchorRight = 1,
            OffsetTop = -34, OffsetLeft = 24, OffsetBottom = -10
        };
        _chartHint.AddThemeFontSizeOverride("font_size", 14);
        _chartHint.AddThemeColorOverride("font_color", new Color(0.86f, 0.84f, 0.76f));
        root.AddChild(_chartHint);

        // le champ d'une note : il ne paraît que lorsqu'on écrit
        _chartEntry = new LineEdit
        {
            AnchorLeft = 0.5f, AnchorRight = 0.5f, AnchorTop = 1, AnchorBottom = 1,
            OffsetLeft = -220, OffsetRight = 220, OffsetTop = -78, OffsetBottom = -44,
            PlaceholderText = "une note, puis Entrée", Visible = false
        };
        _chartEntry.TextSubmitted += WriteNote;
        root.AddChild(_chartEntry);
    }

    void LayoutChart()
    {
        if (_chartView == null || _chart == null) return;
        var vp = GetViewport().GetVisibleRect().Size;
        var tex = _chart.Texture;

        /* On ne montre qu'un MORCEAU de la feuille : celui qu'on regarde. Les
           traits, eux, sont dessinés dans la feuille entière, donc ils suivent
           la loupe sans qu'on ait à les toucher. */
        float side = (float)(_chartSpan / _chart.MetresPerPixel);
        var half = new Vector2(side * 0.5f, side * 0.5f * vp.Y / Math.Max(1, vp.X));
        var region = new Rect2(_chartAt - half, half * 2);
        _atlas ??= new AtlasTexture();
        _atlas.Atlas = tex;
        _atlas.Region = region;
        float k = Math.Min(vp.X * 0.94f / Math.Max(1, region.Size.X), vp.Y * 0.88f / Math.Max(1, region.Size.Y));
        var size = region.Size * k;
        _chartView.Texture = _atlas;
        _chartView.OffsetLeft = -size.X * 0.5f; _chartView.OffsetRight = size.X * 0.5f;
        _chartView.OffsetTop = -size.Y * 0.5f; _chartView.OffsetBottom = size.Y * 0.5f;
    }

    void ToggleChart()
    {
        if (_chartLayer == null || _chart == null) return;
        _chartOpen = !_chartLayer.Visible;
        _chartLayer.Visible = _chartOpen;
        if (_chartOpen)
        {
            // elle s'ouvre sur SA position : c'est ce qu'un capitaine regarde d'abord
            var wo = _sea.Core.Origin;
            var b = _ship.Physics.Body;
            _chartAt = _chart.ToChart(wo.X + b.Pos.X, wo.Z + b.Pos.Z);
            LayoutChart();
            Hint();
            /* La souris DOIT être visible pour dessiner : la barre la capture
               pour regarder autour, et on ne tient pas une plume à l'aveugle. */
            Input.MouseMode = Input.MouseModeEnum.Visible;
        }
        else
        {
            _drawing = null;
            if (_chartEntry != null) { _chartEntry.Visible = false; _chartEntry.ReleaseFocus(); }
            SaveBook();                 // ce qu'on vient d'écrire ne se perd pas
        }
    }

    void Hint()
    {
        if (_chartHint == null) return;
        _chartHint.Text = $"clic : tracer à l'encre {InkNames[_ink]}   ·   E : changer d'encre   ·   "
                        + "clic droit : une note   ·   molette : la loupe   ·   clic milieu : déplacer   ·   "
                        + "Retour arrière : effacer   ·   I ou Échap : fermer";
    }

    /// <summary>Où la souris touche la carte, en mètres du monde — ou rien si elle est à côté.</summary>
    (double X, double Z)? Under(Vector2 mouse)
    {
        if (_chartView == null || _chart == null) return null;
        var r = _chartView.GetGlobalRect();
        if (!r.HasPoint(mouse)) return null;
        var region = _atlas?.Region ?? new Rect2(Vector2.Zero, _chart.Texture.GetSize());
        var local = region.Position + (mouse - r.Position) / r.Size * region.Size;
        return _chart.ToWorld(local);
    }

    void WriteNote(string text)
    {
        if (_chart == null || _book == null || _chartEntry == null) return;
        _chartEntry.Visible = false;
        _chartEntry.ReleaseFocus();
        text = text.Trim();
        if (text.Length == 0 || _noteAt == null) return;
        _book.Notes.Add(new Note { X = _noteAt.Value.X, Z = _noteAt.Value.Z, Text = text });
        _chartEntry.Text = "";
        _chart.Refresh();
        Say("Portée sur la carte.");
    }

    (double X, double Z)? _noteAt;
    bool _panning;
    Vector2 _panFrom;

    /* LA CARTE EST SERVIE LA PREMIÈRE, et c'est la seule façon qu'elle marche.
       Godot donne la souris à l'INTERFACE avant `_UnhandledInput` : le fond
       sombre et la feuille sont des Control qui l'arrêtent, si bien que pas un
       clic n'arrivait jusqu'à la plume. `_Input` passe avant tout le monde.

       Le champ d'une note garde ses touches : `ChartInput` rend la main dès
       qu'il a le foyer, et l'événement poursuit sa route jusqu'à lui. */
    public override void _Input(InputEvent e)
    {
        if (ChartInput(e)) GetViewport().SetInputAsHandled();
    }

    /// <summary>Rend vrai si la carte a pris l'événement.</summary>
    bool ChartInput(InputEvent e)
    {
        if (!_chartOpen || _chart == null || _book == null) return false;
        /* PENDANT QU ON ÉCRIT, le champ a la parole : la carte lui laisse ses
           touches. Échap y annule la note — sans cette ligne il tomberait
           jusqu au jeu et ouvrirait le menu par-dessus la carte. */
        if (_chartEntry != null && _chartEntry.Visible && _chartEntry.HasFocus())
        {
            if (e is InputEventKey ek && ek.Pressed && ek.Keycode == Key.Escape)
            {
                _chartEntry.Visible = false;
                _chartEntry.ReleaseFocus();
                _chartEntry.Text = "";
                return true;
            }
            return false;
        }

        if (e is InputEventMouseButton mb)
        {
            var at = Under(mb.Position);
            if (mb.ButtonIndex == MouseButton.Left)
            {
                if (mb.Pressed && at != null) { _drawing = _book.Begin(_ink); _drawing.Pts.Add(at.Value); }
                else if (!mb.Pressed)
                {
                    // un trait d'un seul point est une croix, pas une erreur : on le garde
                    _drawing = null;
                    SaveBook();
                }
                return true;
            }
            if (mb.ButtonIndex == MouseButton.WheelUp || mb.ButtonIndex == MouseButton.WheelDown)
            {
                if (!mb.Pressed) return true;
                double f = mb.ButtonIndex == MouseButton.WheelUp ? 1 / 1.25 : 1.25;
                // on zoome SOUS la souris, comme toute loupe qu'on a déjà tenue
                var before = Under(mb.Position);
                _chartSpan = Math.Clamp(_chartSpan * f, 3000, 260000);
                LayoutChart();
                var after = Under(mb.Position);
                if (before != null && after != null && _chart != null)
                {
                    var d = _chart.ToChart(before.Value.X, before.Value.Z) - _chart.ToChart(after.Value.X, after.Value.Z);
                    _chartAt += d;
                    LayoutChart();
                }
                return true;
            }
            if (mb.ButtonIndex == MouseButton.Middle)
            {
                _panning = mb.Pressed;
                _panFrom = mb.Position;
                return true;
            }
            if (mb.ButtonIndex == MouseButton.Right && mb.Pressed && at != null && _chartEntry != null)
            {
                _noteAt = at;
                _chartEntry.Visible = true;
                _chartEntry.GrabFocus();
                return true;
            }
            return false;
        }

        if (e is InputEventMouseMotion pan && _panning && _chartView != null)
        {
            var r2 = _chartView.GetGlobalRect();
            var region = _atlas?.Region ?? new Rect2();
            _chartAt -= (pan.Position - _panFrom) / r2.Size * region.Size;
            _panFrom = pan.Position;
            LayoutChart();
            return true;
        }

        if (e is InputEventMouseMotion mm && _drawing != null)
        {
            var at = Under(mm.Position);
            if (at != null)
            {
                /* Un point tous les quelques mètres : la plume suit la main sans
                   noyer le carnet sous dix mille points pour un seul trait. */
                var last = _drawing.Pts[^1];
                double dx = at.Value.X - last.X, dz = at.Value.Z - last.Z;
                if (dx * dx + dz * dz > 400) { _drawing.Pts.Add(at.Value); _chart.Refresh(); }
            }
            return true;
        }

        if (e is InputEventKey k && k.Pressed && !k.Echo)
        {
            switch (k.PhysicalKeycode != Key.None ? k.PhysicalKeycode : k.Keycode)
            {
                case Key.Escape: ToggleChart(); return true;
                case Key.E: _ink = (_ink + 1) % InkNames.Length; Hint(); return true;
                case Key.Backspace:
                    if (_book.Undo()) { _chart.Refresh(); SaveBook(); }
                    return true;
            }
        }
        return false;
    }
}
