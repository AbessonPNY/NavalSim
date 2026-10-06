using Godot;
using System;
using NavalSim.Core;

namespace NavalSim;

/// <summary>
/// LA RÈGLE PARALLÈLE (demandé) — R sur la carte ouverte. Le cap à donner se trouve
/// comme en 1690 : un trait au crayon du point où l'on se croit au point où l'on va,
/// la règle posée dessus, « marchée » jusqu'à la rose la plus proche, et le rumb lu où
/// son bord coupe la couronne (ParallelRuler, ChartNode.Roses). La distance se prend
/// aux pointes sèches sur l'échelle des latitudes : une minute, un mille ; trois
/// milles, une lieue marine.
///
/// Clic : l'arrivée — le départ est le POINT ESTIMÉ, celui de la plume, pas la vérité.
/// Clic droit : un autre départ. Entrée : la route portée à l'encre bleue (celle des
/// routes, ChartNode.Pen). Le cap lu est le cap VRAI : la carte est tracée au nord du
/// monde, et c'est celui que montre le compas du jeu (sa variation, mal connue, est
/// une erreur de l'estime — Reckoning.CompassBias — et non de la règle).
/// </summary>
public partial class ShipDemo
{
    bool _ruling;
    ParallelRuler? _ruler;
    Label? _rulerLine;
    (double X, double Z)? _ruleFrom;
    (double X, double Z)? _ruleA, _ruleB;

    void BuildRuler(Control root)
    {
        _rulerLine = new Label
        {
            AnchorLeft = 0, AnchorRight = 1, AnchorTop = 1, AnchorBottom = 1,
            OffsetTop = -66, OffsetBottom = -38, HorizontalAlignment = HorizontalAlignment.Center,
            Visible = false, MouseFilter = Control.MouseFilterEnum.Ignore
        };
        _rulerLine.AddThemeFontSizeOverride("font_size", 18);
        _rulerLine.AddThemeColorOverride("font_color", new Color(0.97f, 0.92f, 0.80f));
        _rulerLine.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.8f));
        _rulerLine.AddThemeConstantOverride("outline_size", 4);
        root.AddChild(_rulerLine);
    }

    /// <summary>La règle, dans la vue de la carte ouverte, avec la correspondance monde → écran du moment.</summary>
    void LayoutRuler(Func<Vector2, Vector2> map)
    {
        if (_chartView == null || _chart == null) return;
        if (_ruler == null)
        {
            _ruler = new ParallelRuler { Hand = HandFont.Get() };
            _chartView.AddChild(_ruler);
        }
        var chart = _chart;
        _ruler.At = (x, z) => map(chart.ToChart(x, z));
        _ruler.RoseR = chart.RoseRadius * _chartK;
        _ruler.QueueRedraw();
    }

    void ToggleRuler()
    {
        _ruling = !_ruling;
        if (!_ruling) LiftRuler();
        else Say("La règle parallèle en main : cliquez l'arrivée (clic droit : un autre départ)");
        Hint();
    }

    void LiftRuler()
    {
        _ruler?.Lift();
        _ruleA = _ruleB = null;
        _ruleFrom = null;
        if (_rulerLine != null) _rulerLine.Visible = false;
    }

    /// <summary>Le trait du départ à l'arrivée, la règle dessus, et ce qu'on y lit.</summary>
    void RuleTo(double x, double z)
    {
        if (_chart == null || _ruler == null) return;
        var a = _ruleFrom ?? Believed();
        var b = (x, z);
        double dx = b.Item1 - a.Item1, dz = b.Item2 - a.Item2;
        if (dx * dx + dz * dz < 1) return;
        // la rose la plus proche du milieu du trait : c'est là qu'on la fait marcher
        double mx = (a.Item1 + b.Item1) * 0.5, mz = (a.Item2 + b.Item2) * 0.5;
        (double X, double Z) rose = (mx, mz);
        double best = double.MaxValue;
        foreach (var r in _chart.Roses)
        {
            double d = (r.X - mx) * (r.X - mx) + (r.Z - mz) * (r.Z - mz);
            if (d < best) { best = d; rose = r; }
        }
        double cap = Compass.BearingDeg(a.Item1, a.Item2, b.Item1, b.Item2);
        double miles = Math.Sqrt(dx * dx + dz * dz) / MetresPerMinute;
        _ruleA = a; _ruleB = b;
        FrameRuler(a, b, rose);
        _ruler.Lay(a, b, rose, $"{Compass.RumbShort(cap)}  ·  {Compass.Quadrantal(cap)}");
        if (_rulerLine != null)
        {
            _rulerLine.Text = FormattableString.Invariant(
                $"Route vraie : {Compass.Rumb(cap)}  ({Compass.Quadrantal(cap)}, {cap:F0}°)   ·   {miles:F1} milles, {miles / 3:F1} lieues")
                + "   ·   Entrée : la porter à l'encre bleue";
            _rulerLine.Visible = true;
        }
    }

    /* LE TRAIT ET LA ROSE DANS LE MÊME CADRE : sinon la règle marche hors de l'écran
       et l'on ne voit ni le geste ni la lecture (à la loupe d'un port, la rose est à
       vingt milles). La carte recule juste assez, sans jamais grossir ce qu'on regardait. */
    void FrameRuler((double X, double Z) a, (double X, double Z) b, (double X, double Z) rose)
    {
        if (_chart == null) return;
        var pa = _chart.ToChart(a.X, a.Z); var pb = _chart.ToChart(b.X, b.Z); var pr = _chart.ToChart(rose.X, rose.Z);
        float rr = _chart.RoseRadius * 1.6f;
        var lo = pa.Min(pb).Min(pr - new Vector2(rr, rr));
        var hi = pa.Max(pb).Max(pr + new Vector2(rr, rr));
        var vp = GetViewport().GetVisibleRect().Size;
        double wantPx = Math.Max(hi.X - lo.X, (hi.Y - lo.Y) * vp.X / Math.Max(1, vp.Y)) * 1.15;
        _chartSpan = Math.Clamp(Math.Max(_chartSpan, wantPx * _chart.MetresPerPixel), 3000, 260000);
        _chartAt = (lo + hi) * 0.5f;
        LayoutChart();
    }

    /// <summary>La route lue, portée au carnet à l'encre bleue — celle des routes.</summary>
    void InkRuledRoute()
    {
        if (_ruleA is not { } a || _ruleB is not { } b || _book == null || _chart == null) return;
        var s = _book.Begin(2);
        s.W = _chart.NibMetres(_chartK);
        s.Pts.Add(a);
        s.Pts.Add(b);
        _chart.Refresh();
        SaveBook();
        Say("Route portée à l'encre bleue");
    }

    /// <summary>La règle prend ses clics et ses touches ; vrai si elle a pris l'événement.</summary>
    bool RulerInput(InputEvent e)
    {
        if (!_ruling) return false;
        if (e is InputEventMouseButton mb && mb.Pressed && !mb.ShiftPressed)
        {
            var at = Under(mb.Position);
            if (at == null) return false;
            if (mb.ButtonIndex == MouseButton.Left) { RuleTo(at.Value.X, at.Value.Z); return true; }
            if (mb.ButtonIndex == MouseButton.Right)
            {
                _ruleFrom = at;
                Say("Départ posé ; cliquez l'arrivée");
                if (_ruleB is { } b) RuleTo(b.X, b.Z);
                return true;
            }
        }
        if (e is InputEventKey k && k.Pressed && !k.Echo && (k.Keycode == Key.Enter || k.Keycode == Key.KpEnter))
        {
            InkRuledRoute();
            return true;
        }
        return false;
    }
}
