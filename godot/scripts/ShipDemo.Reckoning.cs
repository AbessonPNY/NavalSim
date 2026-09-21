using Godot;
using System;
using NavalSim.Core;

namespace NavalSim;

/// <summary>
/// L'ESTIME À BORD : le noyau tient le point (<see cref="Reckoning"/>) ; ici,
/// ce qui le nourrit et ce qui le montre.
///
/// La carte ne montre plus où l'on EST, mais où l'on CROIT être : un point
/// estimé et l'ellipse de ce qu'on n'en sait pas. La route à la plume est la
/// route estimée ; le voile, lui, se perce là où l'on passe vraiment — la vigie
/// voit la vraie côte, et c'est en la voyant ailleurs que prévu qu'on apprend
/// qu'on s'est trompé. La ligne d'objectif compte depuis le point estimé, comme
/// le ferait le pilote.
///
/// Trois façons de savoir mieux : à moins de trois cents mètres d'un port (on
/// le reconnaît), la hauteur de midi (la latitude), et l'atterrage d'une
/// traversée, qui arrive avec l'incertitude de toute sa longueur.
/// </summary>
public partial class ShipDemo : Node3D
{
    ReckoningSettings _reckRules = new();
    Reckoning _reck = null!;
    int _noonDay = -1;
    bool _saidNoon;
    Button? _noonButton;

    void BuildReckoning() => _reck = new Reckoning(_reckRules);

    /// <summary>La position VRAIE, en mètres du monde.</summary>
    (double X, double Z) TruePos()
    {
        var o = _sea.Core.Origin;
        var b = _ship.Physics.Body;
        return (o.X + b.Pos.X, o.Z + b.Pos.Z);
    }

    /// <summary>Où l'on croit être : l'estime, ou la vérité quand elle est coupée.</summary>
    (double X, double Z) Believed()
    {
        if (!_reckRules.Enabled || _reck == null || !_reck.Known) return TruePos();
        return (_reck.X, _reck.Z);
    }

    /// <summary>Une minute d'arc de latitude, en mètres de jeu.</summary>
    double MetresPerMinute => 1852 * (_world?.Region.Scale ?? 1);

    void ReckonTick(double dt)
    {
        if (_world == null || _inTitle || _reck == null) return;
        var (tx, tz) = TruePos();
        if (!_reck.Known)
        {
            // le carnet se souvient du point où on l'a laissé ; sinon, on part d'un quai
            if (_book?.Estimate is { } e) _reck.Set(e.X, e.Z, e.SN, e.SE);
            else _reck.Fix(tx, tz);
        }
        /* ON RECONNAÎT UN PORT à trois cents mètres : on sait alors exactement où
           l'on est. Et l'estime coupée, la vérité seule. */
        if (!_reckRules.Enabled || _world.Near(tx, tz, 300).Count > 0) _reck.Fix(tx, tz);
        else
        {
            var b = _ship.Physics.Body;
            var f = b.Quat.Rotate(new Vec3d(0, 0, 1));
            double bearing = (Math.Atan2(-f.X, f.Z) * 180 / Math.PI + 360) % 360;   // l'est est −x
            double rate = _sky.DayRate > 0 ? _sky.DayRate : 2.0;
            _reck.Step(dt, b.Vel.X, b.Vel.Z, bearing, _reckRules.GlassMinutes / rate);
        }
        if (_book != null && _book.Plot(_reck.X, _reck.Z)) _chart?.Refresh();
        NoonTick();
    }

    /// <summary>Le point retenu dans le carnet, pour le reprendre au chargement.</summary>
    void KeepEstimate()
    {
        if (_book != null && _reck != null && _reck.Known)
            _book.Estimate = (_reck.X, _reck.Z, _reck.SigN, _reck.SigE);
    }

    /// <summary>
    /// L'ATTERRAGE : on croit être à l'atterrage, et l'on y est à l'incertitude
    /// de toute la traversée près — quatre pour cent de sa longueur. Le point
    /// estimé est tiré autour du vrai, pas l'inverse : c'est le navire qui est
    /// quelque part, et le capitaine qui se trompe.
    /// </summary>
    void ReckonArrive(double x, double z, double miles)
    {
        if (_reck == null) return;
        double sig = _reckRules.PassageError * miles * MetresPerMinute;
        double a = GD.Randf() * Math.Tau, r = sig * Math.Sqrt(-2 * Math.Log(1 - GD.Randf() * 0.999));
        _reck.Set(x + Math.Cos(a) * r, z + Math.Sin(a) * r, sig, sig);
    }

    /* LA HAUTEUR DE MIDI se prend quand le soleil est à son plus haut — de onze
       heures et demie à midi et demi — et s'il se montre : ni la nuit, ni sous
       un ciel couvert, ni dans un grain. Une fois par jour. */
    bool NoonOpen(out string why)
    {
        double h = _sky.Core.DayTime;
        why = "";
        if (_noonDay == _calendar.Day) { why = "La hauteur est déjà prise aujourd'hui"; return false; }
        if (h < 11.5 || h > 12.5) { why = "La hauteur se prend à midi, quand le soleil culmine (11 h 30 – 12 h 30)"; return false; }
        if (_sky.Core.SunElevDeg < 5 || _sky.Core.Storm > 0.45 || _cloud > 0.8) { why = "Le soleil ne se montre pas : pas de hauteur aujourd'hui"; return false; }
        return true;
    }

    void NoonTick()
    {
        if (!_reckRules.Enabled) return;
        bool open = NoonOpen(out _);
        if (open && !_saidNoon) { _saidNoon = true; Say("Midi approche — la hauteur du soleil se prend sur la carte (I)"); }
        if (!open) _saidNoon = false;
        if (_noonButton != null) _noonButton.Disabled = !open;
    }

    void TakeNoonSight()
    {
        if (_world == null || _reck == null) return;
        if (!NoonOpen(out string why)) { Say(why); return; }
        var (tx, tz) = TruePos();
        _reck.NoonSight(tz, MetresPerMinute);
        _noonDay = _calendar.Day;
        var fix = _world.Geo.Fix(_reck.X, _reck.Z);
        Say($"Hauteur méridienne : latitude {Geo.Format(fix.Lat, true)} — portée sur la carte");
        GD.Print(FormattableString.Invariant($"hauteur de midi : erreur {(_reck.Z - tz):F0} m, incertitude nord-sud {_reck.SigN:F0} m"));
        _chart?.Refresh();
    }

    /// <summary>Le bouton de la carte, sous la flèche retour.</summary>
    void BuildNoonButton(Control root)
    {
        _noonButton = new Button
        {
            Text = "☼  Prendre la hauteur de midi", FocusMode = Control.FocusModeEnum.None,
            OffsetLeft = 12, OffsetRight = 230, OffsetTop = 40, OffsetBottom = 68, Disabled = true,
            TooltipText = "De 11 h 30 à 12 h 30, par temps clair : la latitude du navire"
        };
        _noonButton.Pressed += TakeNoonSight;
        root.AddChild(_noonButton);
    }

    bool OverNoon(Vector2 p) => _noonButton != null && _noonButton.GetGlobalRect().HasPoint(p);
}
