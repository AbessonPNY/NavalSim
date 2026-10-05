using Godot;
using System;
using NavalSim.Core;

namespace NavalSim;

/// <summary>
/// PRENDRE LA HAUTEUR, INSTRUMENT EN MAIN (demandé) — la latitude, comme en 1690.
///
/// À MIDI, LE QUARTIER DE DAVIS : on tourne le dos au soleil, on vise l'horizon par la
/// fente, et l'on fait glisser le viseur de soleil (le marteau) jusqu'à ce que son ombre
/// tombe sur l'horizon. La mer roule : l'horizon et l'ombre montent et descendent ensemble,
/// et c'est l'œil qui juge quand ils se rejoignent. Il faut le prendre quand le soleil
/// CULMINE — pris trop tôt ou trop tard, il est plus bas, et la latitude fausse.
///
/// LA NUIT, LA POLAIRE À L'ARBALESTRILLE : le bout bas du marteau sur l'horizon, le bout
/// haut sur l'étoile. Moins précis (le marteau se tient à bout de bras, de nuit), et la
/// Polaire n'est pas au pôle : la correction des Gardes, calculée (Sights).
///
/// Le jeu POSE LE CALCUL sous les yeux — hauteur, déclinaison du jour ou correction des
/// Gardes, latitude — et la porte sur la carte (l'estime) : l'erreur est celle de la
/// main, plus celle de l'instrument. La longitude reste à l'estime : en 1690, personne ne
/// sait la prendre en mer.
///
/// Molette : le marteau, de dix minutes d'arc (⇧ : d'une) ; ↑ ↓ : d'une ; bouton tenu :
/// viser ; Entrée : lire ; Échap : ranger.
/// </summary>
public partial class ShipDemo
{
    const int CamSight = 8;
    bool _sighting, _sightSun;
    double _sightSet, _sightAim;       // le réglage de l'arc, et la visée de l'œil (degrés au-dessus de l'horizon)
    int _sightCamWas, _polarisNight = int.MinValue;
    SightPlate? _plate;
    Label? _sightCalc;
    double _sightCalcT;
    double _sightAlt;                  // la hauteur vraie de l'astre, cette image
    readonly Random _sightRng = new();

    /// <summary>La nuit en cours, comptée par le jour où elle a commencé.</summary>
    int NightIndex => _sky.Core.DayTime >= 12 ? _calendar.Day : _calendar.Day - 1;

    /// <summary>Peut-on prendre une hauteur maintenant, et laquelle ?</summary>
    bool SightOpen(out bool sun, out string why)
    {
        sun = false; why = "";
        double h = _sky.Core.DayTime;
        bool clear = _sky.Core.Storm < 0.45 && _cloud < 0.8;
        if (_sky.Core.SunElevDeg > 5)
        {
            sun = true;
            if (h < 11 || h > 13) { why = "Le soleil se prend quand il culmine, autour de midi (11 h – 13 h)"; return false; }
            if (_noonDay == _calendar.Day) { why = "La hauteur de midi est déjà prise aujourd'hui"; return false; }
            if (!clear) { why = "Le soleil ne se montre pas : pas de hauteur aujourd'hui"; return false; }
            return true;
        }
        if (_sky.Core.SunElevDeg < -8)
        {
            if (_polarisNight == NightIndex) { why = "La Polaire est déjà prise cette nuit"; return false; }
            if (!clear || _cloud > 0.6) { why = "Le ciel est couvert : pas d'étoile"; return false; }
            return true;
        }
        why = "Le jour tombe ou se lève : ni soleil à midi, ni étoiles";
        return false;
    }

    void OpenSight()
    {
        if (_sighting || _world == null || _reck == null) return;
        if (!SightOpen(out bool sun, out string why)) { Say(why); return; }
        if (_chartOpen) ToggleChart();
        _sighting = true;
        _sightSun = sun;
        _sightCamWas = _camMode;
        _camMode = CamSight;
        SetLens(sun ? 12 : 34, 0.05f);
        SightTrue();
        // le pilote prérègle l'arc à peu près — à deux degrés près
        _sightSet = Math.Round((_sightAlt + (_sightRng.NextDouble() - 0.5) * 4) * 60) / 60;
        // le quartier vise l'horizon ; l'arbalestrille vise à mi-chemin de l'horizon et de l'étoile
        _sightAim = sun ? 0 : _sightSet * 0.5;
        if (_plate == null)
        {
            _plate = new SightPlate { MouseFilter = Control.MouseFilterEnum.Ignore };
            _plate.SetAnchorsPreset(Control.LayoutPreset.FullRect);
            _hud.AddChild(_plate);
        }
        _plate.Visible = true;
        _plate.Sun = sun;
        Say(sun ? "Le quartier de Davis : dos au soleil, l'ombre sur l'horizon" : "L'arbalestrille : un bout sur l'horizon, l'autre sur la Polaire");
    }

    void CloseSight()
    {
        if (!_sighting) return;
        _sighting = false;
        _camMode = _sightCamWas == CamSight ? 0 : _sightCamWas;
        SetLens(OutsideFov, OutsideNear);
        if (_plate != null) _plate.Visible = false;
    }

    /// <summary>La latitude et la longitude vraies du navire.</summary>
    (double Lat, double Lon) TrueLatLon()
    {
        var (tx, tz) = TruePos();
        return _world!.Geo.Fix(tx, tz);
    }

    /// <summary>La hauteur vraie de l'astre visé, à cette heure.</summary>
    void SightTrue()
    {
        var (lat, lon) = TrueLatLon();
        double t = _sky.Core.DayTime;
        if (_sightSun) _sightAlt = Sights.SunAltitude(lat, _calendar.Declination(), t);
        else
        {
            var (ra, dec) = Sights.Polaris(_calendar.Date.Year + _calendar.DayOfYear(t) / 365.25);
            _sightAlt = Sights.Altitude(lat, dec, Sights.HourAngle(_calendar.Date, t, lon, ra));
        }
    }

    /// <summary>La vue de l'instrument : l'œil sur la dunette, le cap de la visée, la mer qui roule.</summary>
    void SightCamera()
    {
        var sp = _ship.Spec;
        var xf = _ship.GlobalTransform;
        // dos au soleil pour le quartier ; plein nord pour la Polaire (le nord est +z, l'est −x)
        double b = (_sightSun ? _sky.Core.SunBearingDeg + 180 : 0) * Math.PI / 180;
        var dir = new Vector3((float)-Math.Sin(b), 0, (float)Math.Cos(b));
        /* AU BORD, DU CÔTÉ OÙ L'ON VISE : sur la dunette d'un galion l'œil se trouvait
           derrière le château, et la fente ne montrait que du bois. Le pilote va au
           plat-bord qui regarde l'horizon voulu — le bord de la flottaison (une ellipse de
           la longueur et du bau) dans cette direction. */
        var dl = xf.Basis.Inverse() * dir; dl.Y = 0;
        if (dl.LengthSquared() < 1e-6f) dl = Vector3.Back;
        dl = dl.Normalized();
        float ha = (float)(sp.B * 0.5), hl = (float)(sp.L * 0.5);
        float k = 1.02f / MathF.Sqrt(dl.X * dl.X / (ha * ha) + dl.Z * dl.Z / (hl * hl));
        var eye = xf * new Vector3(dl.X * k, (float)(sp.DeckMid + 1.8), dl.Z * k);
        // la main tremble, d'autant plus que la mer est grosse : toute la vue bouge, horizon et astres ensemble
        double shake = (0.04 + 0.025 * _force) * (_sightSun ? 1 : 1.6);
        double aim = (_sightAim + shake * (0.6 * Math.Sin(_t * 1.7) + 0.4 * Math.Sin(_t * 2.9 + 1.1))) * Math.PI / 180;
        dir = (dir * (float)Math.Cos(aim) + Vector3.Up * (float)Math.Sin(aim)).Normalized();
        /* LA MER ROULE : la main suit l'horizon, mais pas tout à fait — six dixièmes du
           mouvement du pont passent dans la visée. */
        var up = Vector3.Up;
        var shipUp = xf.Basis.Y.Normalized();
        var axis = up.Cross(shipUp);
        if (axis.LengthSquared() > 1e-8f)
        {
            float ang = up.AngleTo(shipUp) * 0.6f;
            var q = new Basis(axis.Normalized(), ang);
            dir = q * dir; up = q * up;
        }
        _cam.Position = eye;
        _cam.LookAt(eye + dir * 100, up);
    }

    string _sightTest = "";
    double _sightTestT;

    void SightTick(double dt)
    {
        if (_sightTest.Length > 0 && _booted)
        {
            if (!_sighting) { OpenSight(); _sightTestT = 1.0; if (!_sighting) _sightTest = ""; }
            else if ((_sightTestT -= dt) <= 0)
            {
                if (_sightTest == "auto") { _sightSet = _sightAlt + 1 / 60.0; ReadSight(); }
                _sightTest = "";
            }
        }
        if (_sightCalc != null && _sightCalc.Visible && (_sightCalcT -= dt) <= 0) _sightCalc.Visible = false;
        if (!_sighting || _plate == null) return;
        SightTrue();
        // la visée effective : ce que la ligne de visée fait avec l'horizon, roulis compris
        var fwd = -_cam.GlobalTransform.Basis.Z;
        double eff = Math.Asin(Math.Clamp(fwd.Y, -1, 1)) * 180 / Math.PI;
        float focal = (float)(_plate.Size.Y * 0.5 / Math.Tan(_cam.Fov * 0.5 * Math.PI / 180));
        _plate.PxPerDeg = focal * (float)(Math.PI / 180);
        _plate.DrawStar = _stars == null;
        if (_sightSun) _plate.MarkDeg = (float)(_sightAlt - _sightSet - eff);
        else
        {
            _plate.MarkDeg = (float)(_sightAlt - eff);
            _plate.SetDeg = (float)_sightSet;
        }
        string rate = "";
        if (_sightSun)
        {
            var (lat, _) = TrueLatLon();
            double later = Sights.SunAltitude(lat, _calendar.Declination(), _sky.Core.DayTime + 0.05);
            rate = later > _sightAlt + 0.002 ? "le soleil monte encore" : later < _sightAlt - 0.002 ? "le soleil redescend" : "le soleil culmine";
        }
        _plate.Top = _sightSun
            ? "Quartier de Davis — dos au soleil : amenez l'OMBRE sur l'HORIZON, dans la fente"
            : "Arbalestrille — le bout bas du marteau sur l'horizon, le bout haut sur la Polaire";
        _plate.Readout = "Arc : " + Sights.Dm(_sightSet) + (rate.Length > 0 ? "   ·   " + rate : "");
        _plate.Hint = "Molette : 10′ (⇧ : 1′) · ↑ ↓ : 1′ · bouton tenu : viser · Entrée : lire · Échap : ranger";
        _plate.QueueRedraw();
    }

    /// <summary>LIRE : la hauteur est l'arc ; le calcul posé, la latitude portée sur la carte.</summary>
    void ReadSight()
    {
        if (!_sighting || _reck == null || _world == null) return;
        var (lat, lon) = TrueLatLon();
        double t = _sky.Core.DayTime;
        double h = _sightSet;
        string calc;
        double found, sigma;
        if (_sightSun)
        {
            double decl = _calendar.Declination();
            bool south = Sights.SunSouth(lat, decl);
            double zen = 90 - h;
            found = Sights.LatitudeFromSun(h, decl, south);
            string dName = decl >= 0 ? "N" : "S";
            string op = south
                ? (decl >= 0 ? $"{Sights.Dm(zen)} + {Sights.Dm(decl)}" : $"{Sights.Dm(zen)} − {Sights.Dm(-decl)}")
                : $"{Sights.Dm(decl)} − {Sights.Dm(zen)}";
            calc = $"Hauteur méridienne du soleil : {Sights.Dm(h)}\n"
                 + $"Déclinaison du jour (la table) : {Sights.Dm(Math.Abs(decl))} {dName}\n"
                 + $"Distance au zénith : 90° − {Sights.Dm(h)} = {Sights.Dm(zen)}, le soleil au {(south ? "sud" : "nord")}\n"
                 + $"Latitude : {op} = {Sights.Dm(Math.Abs(found))} {(found >= 0 ? "N" : "S")}";
            sigma = 4;                                     // le quartier de Davis : quelques minutes d'arc
            _noonDay = _calendar.Day;
        }
        else
        {
            var (ra, dec) = Sights.Polaris(_calendar.Date.Year + _calendar.DayOfYear(t) / 365.25);
            double lha = Sights.HourAngle(_calendar.Date, t, lon, ra);
            double corr = Sights.GuardsCorrection(dec, lha);
            found = h + corr;
            calc = $"Hauteur de la Polaire : {Sights.Dm(h)}\n"
                 + $"Correction des Gardes (la Polaire à {Sights.Dm(90 - dec)} du pôle) : {(corr >= 0 ? "+ " : "− ")}{Sights.Dm(Math.Abs(corr))}\n"
                 + $"Latitude : {Sights.Dm(Math.Abs(found))} {(found >= 0 ? "N" : "S")}";
            sigma = 12;                                    // l'arbalestrille, de nuit : une dizaine de minutes
            _polarisNight = NightIndex;
        }
        // l'erreur de la main, et un rien d'instrument
        double errArc = (found - lat) * 60 + (_sightRng.NextDouble() - 0.5) * (_sightSun ? 2 : 6);
        var (tx, tz) = TruePos();
        var before = _world.Geo.Fix(_reck.X, _reck.Z);
        _reck.LatitudeSight(tz, MetresPerMinute, errArc, sigma);
        var after = _world.Geo.Fix(_reck.X, _reck.Z);
        calc += $"\nL'estime vous mettait par {Geo.Format(before.Lat, true)} : la latitude est portée sur la carte, {Geo.Format(after.Lat, true)}.";
        GD.Print(FormattableString.Invariant($"[hauteur] {(_sightSun ? "soleil" : "Polaire")} : arc {h:F3}°, vraie {_sightAlt:F3}°, latitude trouvée {found:F3}° (vraie {lat:F3}°), erreur {errArc:F1}′"));
        JournalLog(_sightSun ? $"Hauteur de midi : latitude {Geo.Format(after.Lat, true)}." : $"La Polaire : latitude {Geo.Format(after.Lat, true)}.");
        CloseSight();
        ShowSightCalc(calc);
        _chart?.Refresh();
    }

    void ShowSightCalc(string text)
    {
        if (_sightCalc == null)
        {
            _sightCalc = new Label
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                AnchorLeft = 0, AnchorRight = 1, AnchorTop = 0.3f, AnchorBottom = 0.3f, OffsetBottom = 200,
                MouseFilter = Control.MouseFilterEnum.Ignore
            };
            _sightCalc.AddThemeFontSizeOverride("font_size", 22);
            _sightCalc.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.85f));
            _sightCalc.AddThemeConstantOverride("outline_size", 7);
            _hud.AddChild(_sightCalc);
        }
        _sightCalc.Text = text;
        _sightCalc.Visible = true;
        _sightCalcT = 14;
    }

    /// <summary>Les touches et la souris à l'instrument ; vrai si l'événement est pris.</summary>
    bool SightInputEvent(InputEvent e)
    {
        if (!_sighting) return false;
        if (e is InputEventMouseMotion mm && (mm.ButtonMask & (MouseButtonMask.Left | MouseButtonMask.Right)) != 0)
        {
            float ppd = _plate?.PxPerDeg ?? 90;
            _sightAim = Math.Clamp(_sightAim - mm.Relative.Y / ppd * 0.5, -10, 40);
            return true;
        }
        if (e is InputEventMouseButton mb && mb.Pressed)
        {
            double step = Input.IsKeyPressed(Key.Shift) ? 1 / 60.0 : 10 / 60.0;
            if (mb.ButtonIndex == MouseButton.WheelUp) { _sightSet += step; return true; }
            if (mb.ButtonIndex == MouseButton.WheelDown) { _sightSet -= step; return true; }
        }
        if (e is InputEventKey k && k.Pressed)
        {
            Key key = k.PhysicalKeycode != Key.None ? k.PhysicalKeycode : k.Keycode;
            switch (key)
            {
                case Key.Up: _sightSet += 1 / 60.0; return true;
                case Key.Down: _sightSet -= 1 / 60.0; return true;
                case Key.Pageup: _sightSet += 10 / 60.0; return true;
                case Key.Pagedown: _sightSet -= 10 / 60.0; return true;
                case Key.Enter or Key.KpEnter when !k.Echo: ReadSight(); return true;
                case Key.Escape when !k.Echo: CloseSight(); return true;
            }
        }
        return e is InputEventKey or InputEventMouseButton;
    }
}
