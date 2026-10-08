using Godot;
using System;
using NavalSim.Core;

namespace NavalSim;

/// <summary>
/// LE NAUFRAGE FILMÉ (demandé : « quand le navire sombre, la caméra passe en mode cinéma »).
///
/// Dès que le navire est PERDU — plus des trois quarts de ce que ses compartiments contiennent
/// d'eau, coupé en deux par la soute, ou sombré —, l'interface s'efface, les bandes du cinéma
/// descendent, et quatre plans le racontent : de loin et de trois quarts arrière au-dessus des
/// lames ; par le travers au ras de l'eau, à le regarder s'enfoncer ; puis, la coque passée sous
/// la surface, sous l'eau à le suivre qui descend, et le noir. On rend alors la vue d'avant, et
/// le panneau du naufrage (R pour renflouer). Échap, Entrée ou Espace le passent.
/// settings.json → wreck.film (vrai par défaut).
/// </summary>
public partial class ShipDemo
{
    bool _sinkFilmOn = true;
    double _sinkT = -1;               // le temps du film ; < 0 : pas de film
    double _sinkUnder = -1;           // l'heure du film où l'on passe sous l'eau
    bool _sinkDone;                   // ce naufrage-ci a eu son film
    bool _sinkWasCine;                // le cinéma tournait déjà : l'interface est déjà cachée
    Vector3 _sinkEye, _sinkLook;
    bool _sinkCut;

    const double SinkWide = 7, SinkUnderFor = 12, SinkFade = 2, SinkAboveMax = 60;

    /// <summary>La part de sa contenance que le navire a embarquée.</summary>
    static double Flooded(ShipNode s)
    {
        double water = 0, room = 0;
        foreach (var c in s.Physics.Comps) { water += c.Vol; room += c.Cap; }
        return room > 0 ? water / room : 0;
    }

    bool Doomed()
    {
        var p = _ship.Physics;
        if (p.Foundered || Flooded(_ship) > 0.75) return true;
        foreach (var h in _halves) if (h.From == _ship) return true;
        return false;
    }

    void SinkFilmTick(double dt)
    {
        if (_sinkT < 0)
        {
            // renfloué, ou un autre navire : le prochain naufrage aura son film
            if (_sinkDone && !_ship.Physics.Foundered && Flooded(_ship) < 0.5 && _halves.Count == 0) _sinkDone = false;
            if (!_sinkFilmOn || _sinkDone || _inTitle || _editing || _film != null
                || _camMode == CamSwim || _camMode == CamBell || !Doomed()) return;
            BeginSinkFilm();
        }
        _sinkT += dt;
        var s = _ship;
        var P = s.GlobalPosition;
        /* COUPÉ EN DEUX : le cadre prend l'avant aussi, mais seulement tant qu'il est près — à
           moins de deux longueurs, et pas plus d'une longueur sous l'eau. Une moitié qui dérive ou
           qui a coulé tirait le cadre au loin, et le navire n'était plus qu'un point (vu). */
        float L = (float)s.Spec.L, span = L;
        foreach (var h in _halves)
            if (h.From == s && IsInstanceValid(h.Node))
            {
                var q = h.Node.GlobalPosition;
                if (P.DistanceTo(q) > 2 * L || q.Y < P.Y - L) continue;
                span = Math.Min(2.5f * L, Math.Max(span, P.DistanceTo(q) + L * 0.5f));
                P = (P + q) * 0.5f;
            }
        var fw = s.GlobalTransform.Basis.Z; fw.Y = 0;
        fw = fw.LengthSquared() > 1e-4f ? fw.Normalized() : Vector3.Back;
        var side = new Vector3(fw.Z, 0, -fw.X);
        var b = s.Physics.Body;
        float sea = (float)_sea.Core.Sample(b.Pos.X, b.Pos.Z, _t);
        double top = b.Pos.Y + s.LocalBounds().End.Y;

        // sous l'eau dès que la coque y est passée tout entière
        if (_sinkUnder < 0 && s.Physics.Foundered && top - sea < -1) { _sinkUnder = _sinkT; _sinkCut = true; }
        /* une coque qui met trop longtemps à partir — l'arrière d'un navire coupé, que ses hauts
           tiennent dressé — : au bout d'une minute, on la regarde d'en dessous */
        if (_sinkUnder < 0 && _sinkT > SinkAboveMax) { _sinkUnder = _sinkT; _sinkCut = true; }

        Vector3 eye, look;
        if (_sinkUnder >= 0)
        {
            double u = _sinkT - _sinkUnder;
            // tout près, sous la surface, tournant lentement autour d'elle qui descend
            double a = 0.4 + u * 0.1;
            float r = span * 0.55f + 6;
            eye = P + new Vector3((float)Math.Cos(a) * r, 0, (float)Math.Sin(a) * r);
            eye.Y = Math.Min(sea - 2.5f, P.Y + 3);
            look = P;
            float black = u > SinkUnderFor - SinkFade ? (float)Math.Min(1, (u - (SinkUnderFor - SinkFade)) / SinkFade) : 0;
            // le voile du drone, sous l'interface (ShipDemo.FlyBy.cs)
            FadeRect().Color = new Color(0, 0, 0, black * black * (3 - 2 * black));
            if (u > SinkUnderFor) { EndSinkFilm(); return; }
        }
        else if (_sinkT < SinkWide)
        {
            // de loin, trois quarts arrière, au-dessus des lames ; on s'approche à peine
            float d = span * 2.2f + 30 - (float)_sinkT * 1.5f;
            eye = P - fw * d + side * (d * 0.7f) + new Vector3(0, span * 0.6f + 8, 0);
            look = P + new Vector3(0, span * 0.15f, 0);
        }
        else
        {
            // par le travers, au ras de l'eau, en glissant le long d'elle : on la voit s'enfoncer
            if (!_sinkCut && _sinkT - dt < SinkWide) _sinkCut = true;
            float d = span * 0.85f + 10;
            float glide = (float)Math.Sin((_sinkT - SinkWide) * 0.08) * span * 0.4f;
            eye = P + side * d + fw * glide;
            // au ras de l'eau, mais au-dessus de la plus haute crête : la houle passe dessous (AboveSeaAndLand)
            eye.Y = (float)(HighCrest() + 2.5);
            look = new Vector3(P.X, Math.Max(P.Y, sea) + 1.5f, P.Z);
        }
        // un plan suit en douceur ; un changement de plan est franc
        if (_sinkCut) { _sinkEye = eye; _sinkLook = look; _sinkCut = false; }
        else
        {
            float k = 1 - (float)Math.Exp(-dt * 1.6);
            _sinkEye = _sinkEye.Lerp(eye, k);
            _sinkLook = _sinkLook.Lerp(look, k);
        }
        _fixEye = _sinkEye; _fixLook = _sinkLook;
    }

    void BeginSinkFilm()
    {
        _sinkT = 0; _sinkUnder = -1; _sinkDone = true; _sinkCut = true;
        _sinkWasCine = _cine;
        if (!_sinkWasCine) CineHide();
        ApplySettings();                // les bandes du cinéma (FilmMaskOn)
        GD.Print(FormattableString.Invariant($"[naufrage] le film commence : {Flooded(_ship) * 100:F0} % d'eau, {(_ship.Physics.Foundered ? "sombré" : "à flot")}{(_halves.Count > 0 ? ", coupé en deux" : "")}"));
    }

    void EndSinkFilm()
    {
        if (_sinkT < 0) return;
        GD.Print(FormattableString.Invariant($"[naufrage] fin du film à {_sinkT:F1} s"));
        _sinkT = -1; _sinkUnder = -1;
        _fixEye = null; _fixLook = null;
        FadeRect().Color = new Color(0, 0, 0, 0);
        if (!_sinkWasCine) CineShow();
        ApplySettings();
    }

    /// <summary>Le film prend les touches ; Échap, Entrée et Espace le passent.</summary>
    bool SinkFilmInput(InputEvent e)
    {
        if (_sinkT < 0) return false;
        if (e is InputEventKey k && k.Pressed && !k.Echo
            && (k.PhysicalKeycode != Key.None ? k.PhysicalKeycode : k.Keycode) is Key.Escape or Key.Enter or Key.KpEnter or Key.Space)
            EndSinkFilm();
        return e is InputEventKey or InputEventMouseButton;
    }
}
