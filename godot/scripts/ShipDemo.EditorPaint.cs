using Godot;
using System;
using NavalSim.Core;

namespace NavalSim;

/// <summary>
/// LE PINCEAU DU MODE CRÉATION — peindre le sol d'un port : de l'herbe, des pavés,
/// du sable, et la gomme qui rend le sol au relief. P bascule le pinceau, 1 2 3 4
/// choisissent, la molette règle sa taille (⇧ : sa force), le clic gauche peint.
///
/// L'image est celle de <see cref="GroundPaint"/>, lue par le shader de la terre ;
/// on l'enregistre avec les retouches (Ctrl+S, ou en quittant le mode), en PNG à
/// côté de la fiche du monde (world/*.json → peinture).
/// </summary>
public partial class ShipDemo
{
    GroundPaint? _paint;
    bool _painting, _paintDown;
    GroundPaint.Brush _brush = GroundPaint.Brush.Cobble;
    double _brushR = 3, _brushFlow = 0.6;

    static readonly string[] BrushNames = { "herbe", "pavés", "sable", "gomme", "sable blanc (fonds)", "vase (fonds)", "herbier (fonds)" };
    static readonly Color[] BrushColors =
    {
        new(0.45f, 0.85f, 0.3f), new(0.75f, 0.75f, 0.8f), new(1f, 0.85f, 0.45f), new(1f, 0.35f, 0.35f),
        new(0.55f, 1f, 0.95f), new(0.55f, 0.5f, 0.35f), new(0.2f, 0.6f, 0.35f)
    };

    /// <summary>Le sol peint de la région, s'il y en a un : le premier de la fiche dont le port existe.</summary>
    void PaintSetup()
    {
        if (_world == null || _land == null) return;
        foreach (var p in _world.Region.Paints)
        {
            if (_world.ByKey(p.Port) is not { } isl) { GD.PushWarning($"[peinture] port inconnu : {p.Port}"); continue; }
            _paint = new GroundPaint(p, isl.X, isl.Z, System.IO.Path.Combine(Assets.Root, p.Image));
            _land.Paint = _paint;
            GD.Print($"[peinture] {p.Image} : {_paint.N}×{_paint.N}, {p.Side:F0} m de côté autour de {isl.Name}");
            break;
        }
    }

    void TogglePaintMode()
    {
        if (_paint == null) { Say("Pas de sol à peindre dans cette région (world/*.json → peinture)"); return; }
        _painting = !_painting;
        _paintDown = false;
        _sel = null;
        Say(_painting ? $"Pinceau : {BrushNames[(int)_brush]} · 1 herbe, 2 pavés, 3 sable, 4 gomme · fonds : 5 6 7" : "Pinceau rangé");
    }

    /// <summary>Les touches et la souris du pinceau. Vrai s'il a pris l'événement.</summary>
    bool PaintInput(InputEvent e)
    {
        if (!_painting || _paint == null) return false;
        if (e is InputEventKey k && k.Pressed && !k.Echo)
        {
            Key key = k.PhysicalKeycode != Key.None ? k.PhysicalKeycode : k.Keycode;
            int pick = key switch
            {
                Key.Key1 => 0, Key.Key2 => 1, Key.Key3 => 2, Key.Key4 => 3,
                Key.Key5 => 4, Key.Key6 => 5, Key.Key7 => 6, _ => -1
            };
            if (pick >= 0) { _brush = (GroundPaint.Brush)pick; Say($"Pinceau : {BrushNames[pick]}"); return true; }
            if (k.CtrlPressed && key == Key.Z)
            {
                Say(_paint.Undo() ? "Coup de pinceau annulé" : "Rien à annuler");
                return true;
            }
            return false;
        }
        if (e is InputEventMouseButton mb)
        {
            if (mb.ButtonIndex == MouseButton.Left)
            {
                if (mb.Pressed && !_paintDown) _paint.BeginStroke();
                _paintDown = mb.Pressed;
                if (!mb.Pressed) _paint.Flush(0, true);
                return true;
            }
            if (mb.Pressed && mb.ButtonIndex is MouseButton.WheelUp or MouseButton.WheelDown)
            {
                bool up = mb.ButtonIndex == MouseButton.WheelUp;
                if (mb.ShiftPressed) _brushFlow = Math.Clamp(_brushFlow * (up ? 1.25 : 0.8), 0.05, 1);
                else _brushR = Math.Clamp(_brushR * (up ? 1.15 : 1 / 1.15), 0.5, 40);
                return true;
            }
        }
        return false;
    }

    /// <summary>Chaque image du pinceau : peindre sous la souris tant qu'on appuie, et l'anneau qui le montre.</summary>
    void PaintTick(double dt)
    {
        if (_paint == null) return;
        // le pinceau des fonds VOIT À TRAVERS L'EAU : il vise le sable du fond, non la surface
        var under = GroundUnder(GetViewport().GetMousePosition(), _painting && GroundPaint.Seabed(_brush));
        if (_painting && _paintDown && under is { } g && _paint.Covers(g.X, g.Z))
            // la force est PAR SECONDE : une touche tenue couvre, un passage rapide effleure
            _paint.Dab(g.X, g.Z, _brushR, _brush, Math.Min(1, _brushFlow * dt * 8));
        _paint.Flush(dt);

        if (_edRing == null) return;
        if (_painting && under is { } u)
        {
            var o = _sea.Core.Origin;
            _edRing.Visible = true;
            float r = (float)_brushR;
            double gy = _world!.HeightAt(u.X, u.Z);
            _edRing.Position = new Vector3((float)(u.X - o.X), (float)(GroundPaint.Seabed(_brush) ? gy : Math.Max(0, gy)) + 0.1f, (float)(u.Z - o.Z));
            _edRing.Scale = new Vector3(r, 1, r);
            ((StandardMaterial3D)_edRing.MaterialOverride).AlbedoColor =
                _paint.Covers(u.X, u.Z) ? BrushColors[(int)_brush] : new Color(0.5f, 0.5f, 0.5f);
        }
    }

    // --- l'essai : --peindre x,0,z (mètres vrais) — n'enregistre RIEN ---
    Vector3? _paintTest;

    void PaintTest(double x, double z, bool seabed = false)
    {
        if (_paint == null) { GD.Print("[peinture] pas de sol peint"); return; }
        if (!_editing) ToggleEdit();
        _painting = true;
        _paint.BeginStroke();
        // y = 1 : les fonds — sable blanc, vase, herbier ; sinon la terre
        var (b1, b2, b3) = seabed ? (GroundPaint.Brush.WhiteSand, GroundPaint.Brush.Mud, GroundPaint.Brush.Seagrass)
                                  : (GroundPaint.Brush.Cobble, GroundPaint.Brush.Grass, GroundPaint.Brush.Sand);
        _paint.Dab(x - 14, z, 6, b1, 1);
        _paint.Dab(x, z, 6, b2, 1);
        _paint.Dab(x + 14, z, 6, b3, 1);
        // une rue : des touches serrées le long d'une ligne, comme un glisser
        for (double t = -30; t <= 30; t += 0.5) _paint.Dab(x + t, z + 14, 3, b1, 0.5);
        _paint.Flush(0, true);
        GD.Print(FormattableString.Invariant($"[peinture] essai autour de ({x:F0}, {z:F0}) ; dans le carré : {_paint.Covers(x, z)}"));
    }

    /// <summary>La ligne d'état du pinceau.</summary>
    string PaintStatus() => _paint == null ? "" :
        FormattableString.Invariant(
            $"PINCEAU — {BrushNames[(int)_brush]} · rayon {_brushR:F1} m · force {_brushFlow:F2}{(_paint.Dirty ? " · non enregistré" : "")}\n") +
        "1 herbe · 2 pavés · 3 sable · 4 gomme · fonds : 5 sable blanc, 6 vase, 7 herbier · clic gauche peindre · molette rayon (⇧ force) · Ctrl+Z annuler le coup · P ranger le pinceau";
}
