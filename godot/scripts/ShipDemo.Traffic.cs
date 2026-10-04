using Godot;
using System;
using System.Collections.Generic;
using System.Text.Json;
using NavalSim.Core;

namespace NavalSim;

/// <summary>
/// LA VIE D'UNE RADE — des navires qui vont et viennent : de Port-Royal à Passage
/// Fort, de Port-Royal au large, et retour (fiche du monde : « trafic » d'un port).
///
/// Ce sont des navires DU JEU — solveur, gréement, barre automatique —, et non le
/// décor des navires au mouillage : ils remontent au vent, tirent des bords, virent,
/// et s'échouent s'ils se trompent. La route qui contourne la terre vient du noyau
/// (HarbourRoute), le pilote qui la suit aussi (HarbourPilot) ; tout se rejoue au
/// banc (« rade »).
///
/// On ne les voit ni naître ni partir : un départ se fait d'un bout de route loin de
/// l'œil, une arrivée se range et attend qu'on ne la regarde plus pour s'en aller.
/// À l'ouverture de la scène, la rade est déjà vivante : des navires en route, à mi-
/// chemin. PAR GROS TEMPS, ON RESTE AU PORT : au-delà de force 5, plus de départ —
/// mesuré au banc, un sloop poussé par le gain de vent sombre par force 6.
/// </summary>
public partial class ShipDemo
{
    sealed class Trip
    {
        public HarbourPilot Pilot = null!;
        public double Born, Limit, Aground;
        public string From = "", To = "";
        public bool Said;
    }

    readonly Dictionary<ShipNode, Trip> _trips = new();
    readonly Dictionary<string, List<(double X, double Z)>?> _routeCache = new();
    readonly Dictionary<string, ShipSpec?> _tripSpecs = new();
    readonly Random _tripRng = new();
    double _nextTrip = -1, _tripAcc;
    bool _tripSeeded;
    /// <summary>--rade-vue : la caméra suit le premier navire de la rade, pour l'essai.</summary>
    bool _tripCam;

    // settings.json → trafic
    bool _trafficOn = true;
    int _trafficN = 3;
    string[] _trafficShips = { "sloop", "schooner" };
    double _trafficForceMax = 5, _trafficRange = 5000, _trafficWaitMin = 60, _trafficWaitMax = 180;

    void ReadTraffic(JsonElement root)
    {
        if (!root.TryGetProperty("trafic", out var tr)) return;
        if (tr.TryGetProperty("enabled", out var en) && en.ValueKind == JsonValueKind.False) _trafficOn = false;
        if (tr.Opt("navires") is double n) _trafficN = Math.Clamp((int)n, 0, 8);
        if (tr.Opt("forceMax") is double fm) _trafficForceMax = fm;
        if (tr.Opt("portee") is double pr) _trafficRange = pr;
        if (tr.Opt("attenteMin") is double a0) _trafficWaitMin = a0;
        if (tr.Opt("attenteMax") is double a1) _trafficWaitMax = Math.Max(_trafficWaitMin, a1);
        if (tr.TryGetProperty("fiches", out var fi) && fi.ValueKind == JsonValueKind.Array)
        {
            var l = new List<string>();
            foreach (var e in fi.EnumerateArray()) if (e.GetString() is { Length: > 0 } k) l.Add(k);
            if (l.Count > 0) _trafficShips = l.ToArray();
        }
    }

    /// <summary>Une partie neuve : la rade se repeuple à mi-chemin.</summary>
    void TrafficReset() { _tripSeeded = false; _nextTrip = -1; }

    /// <summary>Devant le ponton d'un port, dans l'eau : là où l'on part et où l'on arrive.</summary>
    (double X, double Z) Quay(Isle isl) =>
        (isl.Port.Hx + Math.Cos(isl.Port.Ang) * 200, isl.Port.Hz + Math.Sin(isl.Port.Ang) * 200);

    ShipSpec? TripSpec(string id)
    {
        if (_tripSpecs.TryGetValue(id, out var s)) return s;
        int idx = _paths.FindIndex(p => System.IO.Path.GetFileNameWithoutExtension(p) == id);
        s = idx >= 0 ? ShipLibrary.Load(_paths[idx]) : null;
        _tripSpecs[id] = s;
        return s;
    }

    /// <summary>Une route, calculée une fois par paire de bouts et par tirant d'eau.</summary>
    List<(double X, double Z)>? TripRoute(Isle port, string dest, double need, bool outbound)
    {
        string key = $"{port.Key}|{dest}|{need:F1}";
        if (!_routeCache.TryGetValue(key, out var r))
        {
            var a = Quay(port);
            if (dest == "large") r = HarbourRoute.ToSea(_world!, a.X, a.Z, need);
            else if (_world!.ByKey(dest) is { } d)
            {
                var b = Quay(d);
                r = HarbourRoute.Plan(_world, a.X, a.Z, b.X, b.Z, need);
            }
            _routeCache[key] = r;
            GD.Print(r == null ? $"[rade] pas de route de {port.Name} vers {dest}" : $"[rade] route de {port.Name} vers {dest} : {r.Count} marques");
        }
        if (r == null) return null;
        var copy = new List<(double X, double Z)>(r);
        if (!outbound) copy.Reverse();
        return copy;
    }

    float CamDist(Vector3 at) => _cam.GlobalPosition.DistanceTo(at);

    bool _tripCamKeep;

    void TrafficTick(double dt)
    {
        if (_tripCamKeep) _tripCam = true;
        if (!_trafficOn || _world == null || _inTitle || _skirmish || _ship == null || _trafficN <= 0) return;
        _tripAcc += dt;
        if (_tripAcc < 1) return;
        _tripAcc = 0;

        // le port qui a une rade vivante, s'il est à portée
        var (px, pz) = TruePos();
        Isle? port = null;
        double best = _trafficRange * _trafficRange;
        foreach (var isl in _world.Isles)
        {
            if (isl.Traffic.Count == 0) continue;
            double d2 = (isl.X - px) * (isl.X - px) + (isl.Z - pz) * (isl.Z - pz);
            if (d2 < best) { best = d2; port = isl; }
        }

        /* CE QUI A FINI S'EN VA — rendu, échoué, sombré, trop long à venir, ou la rade
           laissée loin derrière : quand on ne le regarde plus. */
        foreach (var (s, trip) in new List<KeyValuePair<ShipNode, Trip>>(_trips))
        {
            if (!IsInstanceValid(s) || !_others.Contains(s)) { _trips.Remove(s); continue; }
            trip.Aground = s.Physics.Aground > 0.05 ? trip.Aground + 1 : 0;
            bool done = trip.Pilot.Arrived || s.Physics.Foundered || _t - trip.Born > trip.Limit || trip.Aground > 60 || port == null;
            if (!done) continue;
            if (!trip.Said)
            {
                trip.Said = true;
                GD.Print($"[rade] {s.Spec.Name} ({trip.From} → {trip.To}) : "
                    + (trip.Pilot.Arrived ? "rendu" : s.Physics.Foundered ? "sombré" : trip.Aground > 60 ? "échoué" : port == null ? "rade laissée" : "trop long"));
            }
            if (CamDist(s.Position) > 700) RemoveShip(s);
        }
        if (port == null) return;

        // par gros temps, on reste au port
        if (_sea.Core.SeaState > _trafficForceMax) return;
        if (!_tripSeeded)
        {
            _tripSeeded = true;
            for (int k = 0; k < _trafficN; k++) SpawnTrip(port, midway: true);
            return;
        }
        if (_trips.Count >= _trafficN) return;
        if (_nextTrip < 0) _nextTrip = _t + _trafficWaitMin + _tripRng.NextDouble() * (_trafficWaitMax - _trafficWaitMin);
        if (_t < _nextTrip) return;
        _nextTrip = _t + _trafficWaitMin + _tripRng.NextDouble() * (_trafficWaitMax - _trafficWaitMin);
        SpawnTrip(port, midway: false);
    }

    /// <summary>
    /// UN NAVIRE DE PLUS DANS LA RADE : une destination, un sens, un navire, sa route.
    /// Au départ d'un bout de route qu'on ne regarde pas ; ou, à l'ouverture, déjà en
    /// chemin quelque part entre deux marques.
    /// </summary>
    void SpawnTrip(Isle port, bool midway)
    {
        // de la place pour les rencontres du large et les pirates : la rade n'en prend pas trop
        if (_others.Count >= Config.MaxShips - 4) return;
        string dest = port.Traffic[_tripRng.Next(port.Traffic.Count)];
        string id = _trafficShips[_tripRng.Next(_trafficShips.Length)];
        if (TripSpec(id) is not { } spec) return;
        double need = spec.Hull.KeelDepth + spec.Hull.KeelExtra + 1.5;
        bool outbound = _tripRng.NextDouble() < 0.5;
        var route = TripRoute(port, dest, need, outbound);
        if (route == null || route.Count < 2) return;
        var o = _sea.Core.Origin;
        Vector3 Local((double X, double Z) p) => new((float)(p.X - o.X), 0, (float)(p.Z - o.Z));

        int leg = 1;
        (double X, double Z) at = route[0], next = route[1];
        if (midway)
        {
            int k = _tripRng.Next(route.Count - 1);
            double f = 0.15 + 0.7 * _tripRng.NextDouble();
            at = (route[k].X + (route[k + 1].X - route[k].X) * f, route[k].Z + (route[k + 1].Z - route[k].Z) * f);
            next = route[k + 1];
            leg = k + 1;
        }
        else if (CamDist(Local(at)) < 700)
        {
            // le départ est sous nos yeux : on part de l'autre bout, ou on attend
            route.Reverse();
            outbound = !outbound;
            at = route[0]; next = route[1];
            if (CamDist(Local(at)) < 700) return;
        }

        int idx = _paths.FindIndex(p => System.IO.Path.GetFileNameWithoutExtension(p) == id);
        int before = _others.Count;
        SpawnFleet(1, idx, arm: false);
        if (_others.Count <= before) return;
        var s = _others[^1];
        var b = s.Physics.Body;
        double hd = Math.Atan2(next.X - at.X, next.Z - at.Z);
        b.Pos = new Vec3d(at.X - o.X, b.Pos.Y, at.Z - o.Z);
        b.Quat = Quatd.FromAxisAngle(new Vec3d(0, 1, 0), hd);
        var f0 = b.Quat.Rotate(new Vec3d(0, 0, 1));
        b.Vel = new Vec3d(f0.X * 2.0, 0, f0.Z * 2.0);           // déjà sur son erre
        b.AngVel = Vec3d.Zero;
        s.Ctrl.SailsSet = true;
        s.SyncTransform();
        _helms.Remove(s);

        var pilot = new HarbourPilot(s.Physics, _world!, route, need);
        if (midway) pilot.SkipTo(leg);
        double len = 0;
        for (int k = 0; k + 1 < route.Count; k++)
            len += Math.Sqrt((route[k + 1].X - route[k].X) * (route[k + 1].X - route[k].X) + (route[k + 1].Z - route[k].Z) * (route[k + 1].Z - route[k].Z));
        string here = port.Name, there = dest == "large" ? "le large" : _world!.ByKey(dest)?.Name ?? dest;
        _trips[s] = new Trip
        {
            Pilot = pilot, Born = _t,
            // trois fois le temps d'aller droit à deux nœuds : au-delà, il s'est perdu
            Limit = 3 * len / 1.0,
            From = outbound ? here : there, To = outbound ? there : here
        };
        GD.Print($"[rade] {spec.Name} : {_trips[s].From} → {_trips[s].To}{(midway ? ", déjà en route" : "")}");
    }

    /// <summary>La barre d'un navire de la rade : son pilote. Faux s'il n'en est pas un.</summary>
    bool SteerTrip(ShipNode s, double dt)
    {
        if (!_trips.TryGetValue(s, out var trip)) return false;
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
