using Godot;
using System;
using System.Collections.Generic;
using System.Text.Json;
using NavalSim.Core;

namespace NavalSim;

/// <summary>
/// LA VIE D'UNE RADE, À HEURE FIXE — les horaires de world/horaires/&lt;région&gt;.json
/// (core/Timetable.cs ; format dans world/README.md) : des postes numérotés au bout
/// des pontons, des départs et des arrivées à une heure du ciel, chacun avec ses
/// navires candidats.
///
/// Ce sont des navires DU JEU — solveur, gréement, barre automatique —, et non le
/// décor des navires au mouillage : ils remontent au vent, tirent des bords, virent,
/// et s'échouent s'ils se trompent. La route qui contourne la terre vient du noyau
/// (HarbourRoute), le pilote qui la suit aussi (HarbourPilot) ; tout se rejoue au
/// banc (« rade »).
///
///  - UN DÉPART est à quai avant son heure (« avance », une heure par défaut),
///    amarré à son poste, voiles ferlées ; à l'heure, il largue ses amarres.
///  - UNE ARRIVÉE se met en route à son heure, de l'autre bout (un port, ou le
///    large), et vient s'amarrer devant son poste ; elle s'en va quand elle a fait
///    escale et qu'on ne la regarde plus.
///
/// On ne voit rien naître : un navire paraît là où l'œil n'est pas (loin, ou dans
/// son dos), et sinon on attend — une heure du ciel, après quoi le mouvement est
/// manqué pour ce jour. À l'ouverture de la scène, la rade est déjà vivante : ce
/// qui est parti depuis peu est en chemin, à la distance qu'il a pu faire.
/// PAR GROS TEMPS, ON RESTE AU PORT : au-delà de force 5, ni départ ni arrivée —
/// mesuré au banc, un sloop poussé par le gain de vent sombre par force 6.
/// </summary>
public partial class ShipDemo
{
    sealed class Trip
    {
        public HarbourPilot Pilot = null!;
        public Sailing? Sg;
        public double Born, Limit, Aground;
        public string From = "", To = "";
        public bool Said, SaidBeyond;
        /// <summary>À quai, à attendre son heure (heure du ciel) ; négatif : en route.</summary>
        public double Due = -1;
        /// <summary>Amarré : tenu à sa place par ses aussières, voiles ferlées.</summary>
        public bool Pinned;
        public (double X, double Z) Pin;
        /// <summary>Rendu : l'instant (réel) de l'arrivée, et l'heure du ciel où il s'est amarré.</summary>
        public double ArrivedAt = -1, MooredHour = -1;
    }

    readonly Dictionary<ShipNode, Trip> _trips = new();
    readonly Dictionary<string, List<(double X, double Z)>?> _routeCache = new();
    readonly Dictionary<string, ShipSpec?> _tripSpecs = new();
    readonly Random _tripRng = new();
    /// <summary>Les mouvements dont l'heure est venue et qui attendent que l'œil soit ailleurs : l'heure du ciel où ils sont échus.</summary>
    readonly List<(Sailing Sg, double At)> _dueSailings = new();
    readonly HashSet<string> _berthWarned = new();
    Timetable? _timetable;
    bool _timetableRead, _tripSeeded;
    double _tripAcc, _prevHour = -1;
    Isle? _tripPort;
    /// <summary>--rade-vue : la caméra suit le premier navire de la rade, pour l'essai.</summary>
    bool _tripCam;
    /// <summary>--horaire : un mouvement (« depart:2 », « arrivee:1 ») échu dès l'ouverture, pour l'essai.</summary>
    string _forceSailing = "";
    /// <summary>--horaire-vue : l'œil sur ce poste du port, pour l'essai.</summary>
    int _berthView;

    // settings.json → trafic
    bool _trafficOn = true;
    int _trafficN = 6;
    string[] _trafficShips = { "sloop", "schooner" };
    double _trafficForceMax = 5, _trafficRange = 5000;

    /// <summary>Le pas d'un navire de rade, pour dire où en est celui qui est parti depuis peu (m/s, mesuré au banc).</summary>
    const double TripSpeed = 1.3;
    /// <summary>Une escale : les heures du ciel qu'un navire rendu reste amarré.</summary>
    const double Stay = 2;

    void ReadTraffic(JsonElement root)
    {
        if (!root.TryGetProperty("trafic", out var tr)) return;
        if (tr.TryGetProperty("enabled", out var en) && en.ValueKind == JsonValueKind.False) _trafficOn = false;
        if (tr.Opt("navires") is double n) _trafficN = Math.Clamp((int)n, 0, 10);
        if (tr.Opt("forceMax") is double fm) _trafficForceMax = fm;
        if (tr.Opt("portee") is double pr) _trafficRange = pr;
        if (tr.TryGetProperty("fiches", out var fi) && fi.ValueKind == JsonValueKind.Array)
        {
            var l = new List<string>();
            foreach (var e in fi.EnumerateArray()) if (e.GetString() is { Length: > 0 } k) l.Add(k);
            if (l.Count > 0) _trafficShips = l.ToArray();
        }
    }

    /// <summary>Une partie neuve : la rade se repeuple, à la distance que chacun a pu faire.</summary>
    void TrafficReset() { _tripSeeded = false; _prevHour = -1; _dueSailings.Clear(); }

    Timetable? LoadTimetable()
    {
        _timetableRead = true;
        string path = System.IO.Path.Combine(Assets.Root, "world", "horaires", _world!.Region.Key + ".json");
        if (!System.IO.File.Exists(path)) return null;
        try
        {
            var t = Timetable.FromJson(System.IO.File.ReadAllText(path));
            foreach (var (key, pt) in t.Ports)
            {
                int dep = pt.Sailings.FindAll(s => !s.Arrival).Count;
                GD.Print($"[horaires] {_world.ByKey(key)?.Name ?? key + " (port inconnu)"} : {pt.Berths.Count} poste(s), {dep} départ(s), {pt.Sailings.Count - dep} arrivée(s)");
            }
            return t;
        }
        catch (Exception ex)
        {
            GD.PushWarning($"[horaires] {path} illisible : {ex.Message}");
            return null;
        }
    }

    /// <summary>Devant le ponton d'un port, dans l'eau : là où l'on part et où l'on arrive quand le port n'a pas de poste écrit.</summary>
    (double X, double Z) Quay(Isle isl) =>
        (isl.Port.Hx + Math.Cos(isl.Port.Ang) * 200, isl.Port.Hz + Math.Sin(isl.Port.Ang) * 200);

    /// <summary>
    /// LA PLACE D'UN POSTE : au-delà du musoir de son ponton, tel que l'éditeur l'a
    /// laissé — ou au point écrit. S'il n'y a pas l'eau que la quille demande, la
    /// place recule le long du ponton jusqu'à l'eau qui la porte (dit une fois).
    /// </summary>
    (double X, double Z)? BerthAt(PortTimetable pt, TripBerth b, double need)
    {
        if (b.X is double bx && b.Z is double bz) return (bx, bz);
        if (_jetty?.PierHead(b.Pier) is not { } head)
        {
            if (_berthWarned.Add($"{pt.Key}|{b.N}")) GD.PushWarning($"[horaires] {pt.Key}, poste {b.N} : pas de ponton {b.Pier}");
            return null;
        }
        double dx = Math.Sin(head.Yaw), dz = Math.Cos(head.Yaw);
        for (double d = b.Gap; d <= b.Gap + 200; d += 5)
        {
            double x = head.X + dx * d, z = head.Z + dz * d;
            if (-_world!.HeightAt(x, z) < need) continue;
            if (d > b.Gap && _berthWarned.Add($"{pt.Key}|{b.N}|{need:F1}"))
                GD.Print(FormattableString.Invariant($"[horaires] {pt.Key}, poste {b.N} : trop peu d'eau au musoir pour {need:F1} m de tirant, la place recule à {d:F0} m"));
            return (x, z);
        }
        if (_berthWarned.Add($"{pt.Key}|{b.N}|{need:F1}"))
            GD.PushWarning(FormattableString.Invariant($"[horaires] {pt.Key}, poste {b.N} : nulle part {need:F1} m d'eau devant le ponton {b.Pier}"));
        return null;
    }

    ShipSpec? TripSpec(string id)
    {
        if (_tripSpecs.TryGetValue(id, out var s)) return s;
        int idx = ShipLibrary.IndexOf(_paths, id);
        s = idx >= 0 ? ShipLibrary.Load(_paths[idx]) : null;
        _tripSpecs[id] = s;
        return s;
    }

    /// <summary>Une route d'un poste vers l'autre bout, calculée une fois par paire de bouts et par tirant d'eau.</summary>
    List<(double X, double Z)>? TripRoute((double X, double Z) a, string other, double need)
    {
        string key = FormattableString.Invariant($"{a.X:F0},{a.Z:F0}|{other}|{need:F1}");
        if (!_routeCache.TryGetValue(key, out var r))
        {
            if (other == "large") r = HarbourRoute.ToSea(_world!, a.X, a.Z, need);
            else if (_world!.ByKey(other) is { } d)
            {
                var b = Quay(d);
                r = HarbourRoute.Plan(_world, a.X, a.Z, b.X, b.Z, need);
            }
            _routeCache[key] = r;
            GD.Print(r == null ? $"[horaires] pas de route vers {other}" : $"[horaires] route vers {other} : {r.Count} marques");
        }
        return r == null ? null : new List<(double X, double Z)>(r);
    }

    float CamDist(Vector3 at) => _cam.GlobalPosition.DistanceTo(at);

    /// <summary>Hors de la vue : loin, ou dans le dos de l'œil. C'est là seulement qu'un navire paraît ou s'en va.</summary>
    bool Unseen(Vector3 at) => CamDist(at) > 700 || (CamDist(at) > 60 && !_cam.IsPositionInFrustum(at + new Vector3(0, 6, 0)));

    static string OtherName(World w, Sailing sg) =>
        sg.Beyond.Length > 0 ? sg.Beyond : sg.Other == "large" ? "le large" : w.ByKey(sg.Other)?.Name ?? sg.Other;

    bool _tripCamKeep;

    void TrafficTick(double dt)
    {
        if (_tripCamKeep) _tripCam = true;
        if (!_trafficOn || _world == null || _inTitle || _skirmish || _ship == null || _trafficN <= 0) return;
        // pendant le naufrage du film, la rade dort ; au plan d'ensemble, elle vit
        if (_film is { Phase: 0 }) return;
        _tripAcc += dt;
        if (_tripAcc < 1) return;
        _tripAcc = 0;
        if (!_timetableRead) _timetable = LoadTimetable();
        if (_timetable == null) return;
        double now = _sky.Core.DayTime;

        // le port qui a des horaires, s'il est à portée
        var (px, pz) = TruePos();
        Isle? port = null;
        double best = _trafficRange * _trafficRange;
        foreach (var isl in _world.Isles)
        {
            if (!_timetable.Ports.ContainsKey(isl.Key)) continue;
            double d2 = (isl.X - px) * (isl.X - px) + (isl.Z - pz) * (isl.Z - pz);
            if (d2 < best) { best = d2; port = isl; }
        }
        if (port != _tripPort) { _tripPort = port; _tripSeeded = false; _dueSailings.Clear(); }

        Retire(port, now);
        if (port == null) { _prevHour = now; return; }
        var pt = _timetable.Ports[port.Key];
        if (_berthView > 0 && pt.BerthN(_berthView) is { } bv && BerthAt(pt, bv, 0) is { } bp)
        {
            _berthView = 0;
            var o = _sea.Core.Origin;
            var look = new Vector3((float)(bp.X - o.X), 2, (float)(bp.Z - o.Z));
            var eye = look + new Vector3(55, 22, 60);
            _fixLook = look; _fixEye = eye;
            _planted = true;
            if (_editing)
            {
                // en mode création, c'est l'œil de l'éditeur qu'on pose
                _edEye = new Vec3d(eye.X + o.X, eye.Y, eye.Z + o.Z);
                var d = look - eye;
                _edYaw = Math.Atan2(d.X, d.Z);
                _edPitch = Math.Asin(d.Normalized().Y);
            }
        }
        bool calm = _sea.Core.SeaState <= _trafficForceMax;

        // l'heure a sauté (une traversée, une partie rechargée, le cadran du ciel) : on repeuple
        if (_prevHour >= 0 && Timetable.Since(_prevHour, now) > 1.5) { _tripSeeded = false; _dueSailings.Clear(); }
        if (!_tripSeeded)
        {
            _tripSeeded = true;
            if (_forceSailing.Length > 0)
            {
                foreach (var sg in pt.Sailings)
                    if ((sg.Arrival ? "arrivee:" : "depart:") + sg.Index == _forceSailing) { sg.Lead = 0; sg.Hour = now; _dueSailings.Add((sg, now)); }
                _forceSailing = "";
            }
            else if (calm) Seed(port, pt, now);
            _prevHour = now;
            return;
        }

        // ce dont l'heure vient de sonner : à quai pour un départ, en route pour une arrivée
        double elapsed = Timetable.Since(_prevHour, now);
        _prevHour = now;
        if (elapsed > 0)
            foreach (var sg in pt.Sailings)
            {
                double at = sg.Arrival ? sg.Hour : sg.Hour - sg.Lead;
                if (Timetable.Since(at, now) < elapsed && !_dueSailings.Exists(d => d.Sg == sg)) _dueSailings.Add((sg, now));
            }

        // les amarres larguées : l'heure est venue, et le temps le permet
        foreach (var (s, trip) in _trips)
        {
            if (trip.Due < 0 || Timetable.Since(trip.Due, now) > 12 || !calm) continue;
            trip.Due = -1; trip.Pinned = false; trip.Born = _t;
            s.Ctrl.SailsSet = true;
            GD.Print($"[horaires] {trip.Sg?.Name} : {s.Spec.Name} largue ses amarres pour {trip.To}");
        }

        // l'échu, s'il peut paraître ; manqué une heure après
        for (int i = _dueSailings.Count - 1; i >= 0; i--)
        {
            var (sg, at) = _dueSailings[i];
            if (Timetable.Since(at, now) > 1)
            {
                GD.Print($"[horaires] {sg.Name} ({Timetable.Clock(sg.Hour)}) manqué : {(calm ? "l'œil était sur sa place, ou la rade pleine" : "gros temps")}");
                _dueSailings.RemoveAt(i);
                continue;
            }
            if (!calm) continue;
            if (Launch(port, pt, sg, -1, now)) _dueSailings.RemoveAt(i);
        }
    }

    /// <summary>
    /// CE QUI A FINI S'EN VA — sombré, échoué, perdu, passé l'horizon, ou rendu et
    /// son escale faite ; la rade laissée loin derrière aussi. Quand on ne le
    /// regarde plus. Et ce qui arrive s'amarre, son erre tombée.
    /// </summary>
    void Retire(Isle? port, double now)
    {
        foreach (var (s, trip) in new List<KeyValuePair<ShipNode, Trip>>(_trips))
        {
            if (!IsInstanceValid(s) || !_others.Contains(s)) { _trips.Remove(s); continue; }
            trip.Aground = !trip.Pinned && s.Physics.Aground > 0.05 ? trip.Aground + 1 : 0;
            /* PASSÉ L'HORIZON : celui qui continue vers Carthagène s'en va quand il est
               loin, pas quand il est rendu — il ne le sera jamais ici. */
            if (trip.Pilot.Beyond)
            {
                if (!trip.SaidBeyond && trip.Pilot.Onward is { } on)
                {
                    trip.SaidBeyond = true;
                    var (sx, sz) = (s.Physics.Body.Pos.X + _sea.Core.Origin.X, s.Physics.Body.Pos.Z + _sea.Core.Origin.Z);
                    GD.Print(FormattableString.Invariant($"[horaires] {s.Spec.Name} a gagné le large, cap au {Compass.HeadingDeg(new Vec3d(on.X - sx, 0, on.Z - sz)):F0}° sur {trip.To}"));
                }
                if (CamDist(s.Position) > 2500)
                {
                    GD.Print($"[horaires] {s.Spec.Name} a passé l'horizon, cap sur {trip.To}");
                    RemoveShip(s);
                }
                continue;
            }
            // RENDU : il ferle, court sur son erre, et s'amarre où elle tombe
            if (trip.Pilot.Arrived && !trip.Pinned)
            {
                if (trip.ArrivedAt < 0) trip.ArrivedAt = _t;
                if (s.Physics.Body.Vel.LengthXZ < 0.4 || _t - trip.ArrivedAt > 60)
                {
                    trip.Pinned = true; trip.MooredHour = now;
                    trip.Pin = (s.Physics.Body.Pos.X + _sea.Core.Origin.X, s.Physics.Body.Pos.Z + _sea.Core.Origin.Z);
                    GD.Print($"[horaires] {s.Spec.Name} ({trip.From} → {trip.To}) amarré");
                }
                continue;
            }
            bool moored = trip.MooredHour >= 0;
            bool done = (moored && Timetable.Since(trip.MooredHour, now) > Stay)
                || s.Physics.Foundered || (trip.Due < 0 && !moored && _t - trip.Born > trip.Limit) || trip.Aground > 60 || port == null;
            if (!done) continue;
            if (!trip.Said)
            {
                trip.Said = true;
                GD.Print($"[horaires] {s.Spec.Name} ({trip.From} → {trip.To}) : "
                    + (moored ? "escale faite" : s.Physics.Foundered ? "sombré" : trip.Aground > 60 ? "échoué" : port == null ? "rade laissée" : "trop long"));
            }
            if (Unseen(s.Position)) RemoveShip(s);
        }
    }

    /// <summary>
    /// LA RADE DÉJÀ VIVANTE, à l'ouverture : les départs dont l'heure approche sont à
    /// quai ; ce qui est parti depuis peu — le dernier de chaque ligne — est en
    /// chemin, à la distance qu'il a pu faire au pas d'un navire de rade.
    /// </summary>
    void Seed(Isle port, PortTimetable pt, double now)
    {
        double rate = _sky.DayRate;
        var moving = new List<(Sailing Sg, double Dist)>();
        foreach (var sg in pt.Sailings)
        {
            if (!sg.Arrival && Timetable.Since(sg.Hour - sg.Lead, now) < sg.Lead)
            {
                // à quai d'abord : c'est lui qui partira tout à l'heure
                if (!Launch(port, pt, sg, -1, now)) _dueSailings.Add((sg, now));
                continue;
            }
            if (rate <= 0) continue;
            // heures du ciel → secondes réelles : DayRate heures par minute
            double dist = Timetable.Since(sg.Hour, now) * 60 / rate * TripSpeed;
            moving.Add((sg, dist));
        }
        moving.Sort((a, b) => a.Dist.CompareTo(b.Dist));
        /* EN CHEMIN, LA MOITIÉ DES PLACES AU PLUS : le reste attend les heures qui
           viennent — une rade remplie à l'ouverture manquerait ses premiers départs. */
        int room = Math.Max(1, _trafficN / 2);
        foreach (var (sg, dist) in moving)
        {
            if (_trips.Count >= _trafficN || room-- <= 0) break;
            Launch(port, pt, sg, dist, now);
        }
    }

    /// <summary>
    /// UN NAVIRE DE PLUS DANS LA RADE, pour ce mouvement : un départ à quai, une
    /// arrivée à l'autre bout ; ou, avec <paramref name="dist"/> ≥ 0, déjà en chemin
    /// de tant de mètres. Faux s'il n'a pas pu paraître (l'œil sur sa place, la rade
    /// pleine, pas de route) — on réessaie au prochain coup.
    /// </summary>
    bool Launch(Isle port, PortTimetable pt, Sailing sg, double dist, double now)
    {
        // de la place pour les rencontres du large et les pirates : la rade n'en prend pas trop
        if (_trips.Count >= _trafficN || _others.Count >= Config.MaxShips - 4) return false;
        var ships = sg.Ships.Count > 0 ? sg.Ships.ToArray() : _trafficShips;
        string id = ships[_tripRng.Next(ships.Length)];
        if (TripSpec(id) is not { } spec) { GD.PushWarning($"[horaires] {sg.Name} : pas de fiche « {id} »"); return true; }
        double need = spec.Hull.KeelDepth + spec.Hull.KeelExtra + 1.5;
        (double X, double Z)? home = Quay(port);
        if (sg.Berth >= 0)
        {
            if (pt.BerthN(sg.Berth) is not { } berth) { GD.PushWarning($"[horaires] {sg.Name} : pas de poste {sg.Berth} à {port.Name}"); return true; }
            home = BerthAt(pt, berth, need);
            if (home == null) return true;
        }
        var route = TripRoute(home.Value, sg.Other, need);
        if (route == null || route.Count < 2) return true;
        if (sg.Arrival) route.Reverse();
        var o = _sea.Core.Origin;
        Vector3 Local((double X, double Z) p) => new((float)(p.X - o.X), 0, (float)(p.Z - o.Z));

        // où il paraît : au bout de sa route, ou à la distance qu'il a faite
        int legAt = 1;
        (double X, double Z) at = route[0], next = route[1];
        if (dist > 0)
        {
            double left = dist;
            int k = 0;
            for (; k + 1 < route.Count; k++)
            {
                double seg = Math.Sqrt((route[k + 1].X - route[k].X) * (route[k + 1].X - route[k].X) + (route[k + 1].Z - route[k].Z) * (route[k + 1].Z - route[k].Z));
                if (left < seg) { double f = left / seg; at = (route[k].X + (route[k + 1].X - route[k].X) * f, route[k].Z + (route[k + 1].Z - route[k].Z) * f); break; }
                left -= seg;
            }
            if (k + 1 >= route.Count) return true;     // il serait déjà rendu : rien à montrer
            next = route[k + 1];
            legAt = k + 1;
        }
        if (!Unseen(Local(at))) return false;

        int idx = ShipLibrary.IndexOf(_paths, id);
        int before = _others.Count;
        SpawnFleet(1, idx, arm: false);
        if (_others.Count <= before) return false;
        var s = _others[^1];
        var b = s.Physics.Body;
        bool atQuay = !sg.Arrival && dist < 0;
        double hd = Math.Atan2(next.X - at.X, next.Z - at.Z);
        b.Pos = new Vec3d(at.X - o.X, b.Pos.Y, at.Z - o.Z);
        b.Quat = Quatd.FromAxisAngle(new Vec3d(0, 1, 0), hd);
        var f0 = b.Quat.Rotate(new Vec3d(0, 0, 1));
        double v0 = atQuay ? 0 : 2.0;                       // en chemin : déjà sur son erre
        b.Vel = new Vec3d(f0.X * v0, 0, f0.Z * v0);
        b.AngVel = Vec3d.Zero;
        s.Ctrl.SailsSet = !atQuay;
        s.SyncTransform();
        _helms.Remove(s);

        var pilot = new HarbourPilot(s.Physics, _world!, route, need);
        if (dist > 0) pilot.SkipTo(legAt);
        // une arrivée vient à SON poste : elle s'y rend de plus près qu'au bout d'une route du large
        if (sg.Arrival && sg.Berth >= 0) pilot.LastReach = Math.Max(30, 1.2 * spec.Hull.Length);
        // au-delà du large : un lieu réel, vers lequel on continue de faire route
        if (!sg.Arrival && sg.Beyond.Length > 0 && sg.Lat is double la && sg.Lon is double lo)
            pilot.Onward = _world!.Geo.ToXZ(la, lo);
        double len = 0;
        for (int k = 0; k + 1 < route.Count; k++)
            len += Math.Sqrt((route[k + 1].X - route[k].X) * (route[k + 1].X - route[k].X) + (route[k + 1].Z - route[k].Z) * (route[k + 1].Z - route[k].Z));
        string here = sg.Berth >= 0 ? $"{port.Name}, poste {sg.Berth}" : port.Name, there = OtherName(_world!, sg);
        var trip = new Trip
        {
            Pilot = pilot, Sg = sg, Born = _t,
            // trois fois le temps d'aller droit à un mètre par seconde : au-delà, il s'est perdu
            Limit = 3 * len / 1.0,
            From = sg.Arrival ? there : here, To = sg.Arrival ? here : there
        };
        if (atQuay) { trip.Due = sg.Hour; trip.Pinned = true; trip.Pin = at; }
        _trips[s] = trip;
        GD.Print($"[horaires] {sg.Name} ({Timetable.Clock(sg.Hour)}) : {spec.Name}, {trip.From} → {trip.To}"
            + (atQuay ? ", à quai" : dist > 0 ? FormattableString.Invariant($", déjà en route ({dist:F0} m)") : ", en route"));
        return true;
    }

    /// <summary>La barre d'un navire de la rade : son pilote, ou ses amarres. Faux s'il n'en est pas un.</summary>
    bool SteerTrip(ShipNode s, double dt)
    {
        if (!_trips.TryGetValue(s, out var trip)) return false;
        if (trip.Pinned)
        {
            /* AMARRÉ : ses aussières le tiennent à sa place et à son cap ; la houle le
               soulève et le fait rouler, rien d'autre. */
            s.Ctrl.SailsSet = false; s.Ctrl.Rudder = 0; s.Ctrl.Throttle = 0;
            var b = s.Physics.Body; var o = _sea.Core.Origin;
            b.Pos = new Vec3d(trip.Pin.X - o.X, b.Pos.Y, trip.Pin.Z - o.Z);
            b.Vel = new Vec3d(0, b.Vel.Y, 0);
            b.AngVel = new Vec3d(b.AngVel.X, 0, b.AngVel.Z);
            return true;
        }
        trip.Pilot.Update(dt, _sea.Core, s.Ctrl);
        if (_tripCam && !trip.Pilot.Arrived)
        {
            _tripCam = false;              // un seul navire suivi par image : le premier venu
            var p = s.Position; var f = s.GlobalTransform.Basis.Z;
            _fixEye = p + f * -45f + new Vector3(f.Z, 0, -f.X) * 30f + new Vector3(0, 14, 0);
            _fixLook = p + new Vector3(0, 4, 0);
            _planted = true;
            _tripCamKeep = true;
        }
        return true;
    }
}
