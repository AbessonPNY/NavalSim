using Godot;
using System;
using NavalSim.Core;

namespace NavalSim;

/// <summary>
/// L'HEURE LA NUIT, AU NOCTURLABE (demandé) — comme en 1690, où la nuit n'avait d'autre
/// montre que le sablier qu'on retourne et la Petite Ourse qui tourne.
///
/// On tient l'instrument à bout de bras, son manche en bas, face au nord. La dent de
/// minuit du disque des heures est posée sur la date ; on vise la POLAIRE par le trou du
/// pivot, et l'on fait tourner le bras jusqu'à ce qu'il passe par la GARDE (Kochab, β de
/// la Petite Ourse). L'heure se lit où le bras croise le disque. Le pivot vise la Polaire,
/// pas le pôle : l'instrument a l'erreur du vrai, quelques minutes.
///
/// Rien n'est porté ailleurs : on compare l'heure lue à celle du sablier (la montre du jeu).
/// Bouton tenu : le bras suit la souris ; molette : un degré (⇧ : un quart) ; ← → : un
/// demi ; Entrée : lire ; Échap : ranger.
/// </summary>
public partial class ShipDemo
{
    bool _nocturnal;
    NocturnalPlate? _noct;
    double _noctArm;                      // the arm, screen degrees (0 right, positive down)
    string _noctTest = "";
    double _noctTestT;

    /// <summary>Le nocturlabe se tient-il cette nuit ?</summary>
    bool NocturnalOpen(out string why)
    {
        why = "";
        if (_sky.Core.SunElevDeg > -8) { why = "Le nocturlabe se tient la nuit, quand on voit les Gardes"; return false; }
        if (_sky.Core.Storm > 0.45 || _cloud > 0.6) { why = "Le ciel est couvert : ni Polaire ni Gardes"; return false; }
        return true;
    }

    /// <summary>La direction d'un astre, dans le monde (le nord +z, l'est −x).</summary>
    Vector3 StarDir(double ra, double dec)
    {
        var (lat, lon) = TrueLatLon();
        double lha = Sights.HourAngle(_calendar.Date, _sky.Core.DayTime, lon, ra);
        double alt = Sights.Altitude(lat, dec, lha) * Math.PI / 180, az = Sights.Azimuth(lat, dec, lha) * Math.PI / 180;
        return new Vector3((float)(-Math.Sin(az) * Math.Cos(alt)), (float)Math.Sin(alt), (float)(Math.Cos(az) * Math.Cos(alt)));
    }

    double StarYear => _calendar.Date.Year + _calendar.DayOfYear(_sky.Core.DayTime) / 365.25;

    void OpenNocturnal()
    {
        if (_nocturnal || _sighting || _world == null) return;
        if (!NocturnalOpen(out string why)) { Say(why); return; }
        var geo = Instruments();
        if (geo == null || geo.NRadius <= 0) { Say("Le nocturlabe n'est pas dans le coffre (instruments/instruments.json)"); return; }
        if (_chartOpen) ToggleChart();
        ClosePilotBook();
        _nocturnal = true;
        _sightCamWas = _camMode;
        _camMode = CamSight;
        SetLens(44, 0.05f);
        if (_noct == null)
        {
            _noct = new NocturnalPlate
            {
                MouseFilter = Control.MouseFilterEnum.Ignore, Geo = geo,
                DiskPicture = InventoryImage(geo.NDisk), HoursPicture = InventoryImage(geo.NHours), ArmPicture = InventoryImage(geo.NArm)
            };
            _noct.SetAnchorsPreset(Control.LayoutPreset.FullRect);
            _hud.AddChild(_noct);
        }
        _noct.Visible = true;
        // the arm starts where the hand left it: somewhere near the Guard, not on it
        _noctArm = GuardScreenDeg() + (_sightRng.NextDouble() - 0.5) * 70;
        Say($"Le nocturlabe : la dent de minuit sur le {_calendar.Date.Day} {Months[_calendar.Date.Month - 1]}, la Polaire dans le trou, le bras sur la Garde");
    }

    static readonly string[] Months = { "janvier", "février", "mars", "avril", "mai", "juin", "juillet", "août", "septembre", "octobre", "novembre", "décembre" };

    void CloseNocturnal()
    {
        if (!_nocturnal) return;
        _nocturnal = false;
        _camMode = _sightCamWas == CamSight ? 0 : _sightCamWas;
        SetLens(OutsideFov, OutsideNear);
        if (_noct != null) _noct.Visible = false;
    }

    /// <summary>Où la Garde est vraiment, en angle d'écran autour du pôle (0 à droite, positif vers le bas).</summary>
    double GuardScreenDeg()
    {
        var (lat, lon) = TrueLatLon();
        var (ra, _) = Sights.Kochab(StarYear);
        double lha = Sights.HourAngle(_calendar.Date, _sky.Core.DayTime, lon, ra);
        return -90 - lha;
    }

    /// <summary>La vue : plein nord, la Polaire au milieu, la main tenue (ni roulis ni tangage, un tremblement).</summary>
    void NocturnalCamera()
    {
        var (ra, dec) = Sights.Polaris(StarYear);
        var dir = StarDir(ra, dec);
        var eye = SightEye(new Vector3(dir.X, 0, dir.Z).Normalized());
        double shake = (0.03 + 0.015 * _force) * Math.PI / 180;
        var right = dir.Cross(Vector3.Up).Normalized();
        dir = (dir + Vector3.Up * (float)(shake * Math.Sin(_t * 1.3)) + right * (float)(shake * Math.Sin(_t * 1.9 + 0.7))).Normalized();
        _cam.Position = eye;
        _cam.LookAt(eye + dir * 100, Vector3.Up);
    }

    int _bookTest = -1;

    void NocturnalTick(double dt)
    {
        if (_bookTest >= 0 && _booted) { OpenPilotBook(_bookTest); _bookTest = -1; }
        if (_noctTest.Length > 0 && _booted)
        {
            if (!_nocturnal) { OpenNocturnal(); _noctTestT = 1.0; if (!_nocturnal) _noctTest = ""; }
            else if ((_noctTestT -= dt) <= 0)
            {
                if (_noctTest == "auto") { _noctArm = GuardScreenDeg(); ReadNocturnal(); }
                _noctTest = "";
            }
        }
        if (!_nocturnal || _noct == null || _noct.Geo == null) return;
        var geo = _noct.Geo;
        float focal = (float)(_noct.Size.Y * 0.5 / Math.Tan(_cam.Fov * 0.5 * Math.PI / 180));
        _noct.PxPerDeg = focal * (float)(Math.PI / 180);
        double doy = Math.Floor(_calendar.DayOfYear(0));
        _noct.DayOfYear = doy;
        _noct.MidnightDeg = geo.DateAngle(doy);
        _noct.ArmDeg = (float)_noctArm;
        _noct.DrawStars = _stars == null;
        var eye = _cam.GlobalPosition;
        var (pra, pdec) = Sights.Polaris(StarYear);
        var (kra, kdec) = Sights.Kochab(StarYear);
        _noct.PolarisAt = _cam.UnprojectPosition(eye + StarDir(pra, pdec) * 1000);
        _noct.GuardAt = _cam.UnprojectPosition(eye + StarDir(kra, kdec) * 1000);
        var (lat, lon) = TrueLatLon();
        double guardAlt = Sights.Altitude(lat, kdec, Sights.HourAngle(_calendar.Date, _sky.Core.DayTime, lon, kra));
        _noct.Top = "Nocturlabe — la Polaire dans le trou, le BRAS sur la GARDE (la plus brillante des deux étoiles du bout de la Petite Ourse)"
                  + (guardAlt < 6 ? " · la Garde est basse, dans la brume de l'horizon" : "");
        _noct.Readout = "Le bras sur la Garde, puis Entrée : l'heure se lit sur le disque";
        _noct.Hint = "Bouton tenu : le bras suit la main · molette : 1° (⇧ : ¼°) · ← → : ½° · Entrée : lire · Échap : ranger";
        _noct.QueueRedraw();
    }

    /// <summary>LIRE : l'heure où le bras croise le disque, au quart d'heure, et ce que dit le sablier.</summary>
    void ReadNocturnal()
    {
        if (!_nocturnal || _noct?.Geo == null) return;
        double armL = -90 - _noctArm, midL = -90 - _noct.MidnightDeg;
        double h = Sights.NocturnalHour(armL, midL);
        double q = Math.Round(h * 4) / 4 % 24;
        double glass = _sky.Core.DayTime;
        double err = ((h - glass) % 24 + 36) % 24 - 12;
        var seen = _noct.GuardAt - _noct.PolarisAt;
        GD.Print(FormattableString.Invariant($"[nocturlabe] bras {_noctArm:F2}°, Garde {GuardScreenDeg():F2}° (vue à l'écran autour de la Polaire : {Math.Atan2(seen.Y, seen.X) * 180 / Math.PI:F2}°) : heure lue {h:F3}, sablier {glass:F3}, écart {err * 60:F1} min"));
        string said = HourWords(q);
        string calc = $"Le nocturlabe dit {said}.\nLe sablier dit {Timetable.Clock(glass)}"
                    + (Math.Abs(err) < 0.2 ? " : ils s'accordent." : $" : {Math.Abs(err) * 60:F0} minutes d'écart — la main, ou le sablier.");
        JournalLog($"Au nocturlabe : {said}.");
        CloseNocturnal();
        ShowSightCalc(calc);
    }

    /// <summary>« dix heures et quart du soir » : l'heure comme un pilote la dit, au quart.</summary>
    static string HourWords(double h)
    {
        int quarters = (int)Math.Round(h * 4) % 96;
        int hh = quarters / 4, qq = quarters % 4;
        if (qq == 3) hh = (hh + 1) % 24;                  // « moins le quart » se dit de l'heure qui vient
        string name = hh == 0 ? "minuit" : hh == 12 ? "midi" : hh % 12 == 1 ? "une heure" : $"{hh % 12} heures";
        string quarter = qq switch { 1 => " et quart", 2 => " et demie", 3 => " moins le quart", _ => "" };
        string part = hh is 0 or 12 ? "" : hh < 12 ? " du matin" : hh < 18 ? " de l'après-midi" : " du soir";
        return name + quarter + part;
    }

    /// <summary>Les touches et la souris au nocturlabe ; vrai si l'événement est pris.</summary>
    bool NocturnalInput(InputEvent e)
    {
        if (!_nocturnal) return false;
        if (e is InputEventMouseMotion mm && (mm.ButtonMask & MouseButtonMask.Left) != 0 && _noct != null)
        {
            var d = mm.Position - _noct.Size * 0.5f;
            if (d.LengthSquared() > 400) _noctArm = Math.Atan2(d.Y, d.X) * 180 / Math.PI;
            return true;
        }
        if (e is InputEventMouseButton mb && mb.Pressed)
        {
            double step = Input.IsKeyPressed(Key.Shift) ? 0.25 : 1;
            if (mb.ButtonIndex == MouseButton.WheelUp) { _noctArm -= step; return true; }
            if (mb.ButtonIndex == MouseButton.WheelDown) { _noctArm += step; return true; }
            if (mb.ButtonIndex == MouseButton.Left && _noct != null)
            {
                var d = mb.Position - _noct.Size * 0.5f;
                if (d.LengthSquared() > 400) _noctArm = Math.Atan2(d.Y, d.X) * 180 / Math.PI;
                return true;
            }
        }
        if (e is InputEventKey k && k.Pressed)
        {
            Key key = k.PhysicalKeycode != Key.None ? k.PhysicalKeycode : k.Keycode;
            switch (key)
            {
                case Key.Left: _noctArm -= 0.5; return true;
                case Key.Right: _noctArm += 0.5; return true;
                case Key.Enter or Key.KpEnter when !k.Echo: ReadNocturnal(); return true;
                case Key.Escape when !k.Echo: CloseNocturnal(); return true;
            }
        }
        return e is InputEventKey or InputEventMouseButton;
    }
}
