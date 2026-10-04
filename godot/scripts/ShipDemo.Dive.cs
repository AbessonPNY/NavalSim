using Godot;
using System;
using System.Collections.Generic;
using NavalSim.Core;

namespace NavalSim;

/// <summary>
/// L'EXPLORATION SOUS-MARINE — les épaves qui restent, et la cloche de Halley
/// (1691) pour descendre jusqu'à elles.
///
/// TOUTE ÉPAVE EST INSCRITE (demandé) : un navire qui sombre entre au registre,
/// avec sa place, sa profondeur et ce que son coffre renferme. Le registre vit
/// avec la partie et se sauvegarde avec elle ; il est STATIQUE, pour survivre au
/// rechargement de la scène qu'impose une traversée.
///
/// LA CLOCHE ne se met à l'eau que navire stoppé. On descend au treuil, on la
/// porte de quelques mètres de part et d'autre — les plongeurs de Halley
/// sortaient la toucher au bout d'un tuyau ; ici, c'est la cloche qu'on mène —,
/// et l'on saisit un coffre quand il est à portée. L'air comprimé est la seule
/// limite : sa réserve fond d'autant plus vite qu'on est bas, et quand elle est
/// à bout, on la hisse d'office, coffre ou pas (demandé).
/// </summary>
public partial class ShipDemo
{
    /// <summary>Le registre de la partie : statique, une traversée recharge la scène.</summary>
    static readonly WreckRegistry Wrecks = new();
    WreckSiteNode? _sites;
    readonly HashSet<ShipNode> _recorded = new();
    readonly Dictionary<string, ShipNode> _liveWreck = new();
    string? _playerWreck;
    /// <summary>Le manifeste des trésors (treasure/tresor.json).</summary>
    TreasureBook _tresors = new();
    /// <summary>Les trésors À BORD, à la pièce : ce qui ne va pas à la bourse et se vend au comptoir.</summary>
    readonly Dictionary<string, int> _treasureHold = new();

    const int CamBell = 6;
    BellNode? _bell;
    Label? _bellLabel;
    bool _bellOut, _bellHoist, _bellForced, _bellSaidReach, _bellSaidCable, _bellPosInit;
    double _bellRope, _bellAir, _bellAirMax = 420, _bellCable = 40;
    int _bellWarned, _bellCamWas;
    Vector2 _bellOff;
    Vector3 _bellPos;
    Wreck? _bellReach;

    void DiveBoot(string shipsDir)
    {
        try { _tresors = TreasureBook.FromJson(System.IO.File.ReadAllText(Assets.Path("treasure/tresor.json"))); }
        catch (Exception e) { GD.PushWarning($"treasure/tresor.json illisible ({e.Message}) : les coffres ne renfermeront que des écus"); }
        if (_world == null) return;
        _sites = new WreckSiteNode(_world, shipsDir)
        {
            StillThere = id => _liveWreck.TryGetValue(id, out var s) && IsInstanceValid(s) && (s == _ship || _others.Contains(s)),
            Book = _tresors
        };
        AddChild(_sites);
        _bell = new BellNode();
        AddChild(_bell);
    }

    // ------------------------------------------------------------------
    //  LE REGISTRE
    // ------------------------------------------------------------------

    void FounderTick()
    {
        if (_world == null || _inTitle) return;
        Record(_ship);
        foreach (var s in _others) Record(s);
    }

    void Record(ShipNode s)
    {
        if (s.IsGhost || !s.Physics.Foundered || _recorded.Contains(s)) return;
        _recorded.Add(s);
        var b = s.Physics.Body;
        var o = _sea.Core.Origin;
        double x = o.X + b.Pos.X, z = o.Z + b.Pos.Z;
        var f = b.Quat.Rotate(new Vec3d(0, 0, 1));
        string id = Guid.NewGuid().ToString("N")[..8];
        var w = Wrecks.Add(new Wreck
        {
            Id = id, Ship = s.Spec.Id, Name = s.Spec.Name, Region = _world!.Region.Key,
            X = x, Z = z, Cap = Math.Atan2(f.X, f.Z),
            Depth = Math.Max(0, -_world.HeightAt(x, z)), Beam = s.Spec.B,
            Date = $"{_calendar.Date:dd/MM/yyyy}", Hour = _sky.Core.DayTime,
            Ecus = WreckRegistry.Value(s.Spec.MassKg, s.Physics.CargoTonnes, _pirates.ContainsKey(s), id),
            Broken = s.Broken, ZCut = s.BreakZ
        });
        w.Contents = _tresors.Draw(id, w.Ecus, _pirates.ContainsKey(s));
        _liveWreck[id] = s;
        if (s == _ship) _playerWreck = id;
        GD.Print(FormattableString.Invariant($"épave inscrite : {w.Name} par {w.Depth:F0} m de fond, {w.Ecus} écus"));
    }

    /// <summary>
    /// RENFLOUÉE, ELLE N'EST PLUS UNE ÉPAVE : R remet le navire à flot sur place,
    /// et l'inscription faite à son naufrage tomberait sur un fond vide.
    /// </summary>
    void Refloated()
    {
        if (_playerWreck != null) { Wrecks.Remove(_playerWreck); _liveWreck.Remove(_playerWreck); _playerWreck = null; }
        _recorded.Remove(_ship);
    }

    /// <summary>Ce que la carte marque : les épaves de cette région, pillées ou non.</summary>
    IEnumerable<(double X, double Z, bool Looted)> WreckMarks()
    {
        if (_world == null) yield break;
        foreach (var w in Wrecks.All)
            if (w.Region == _world.Region.Key) yield return (w.X, w.Z, w.Looted);
    }

    // ------------------------------------------------------------------
    //  LA CLOCHE
    // ------------------------------------------------------------------

    /// <summary>Le bord d'où pend le câble : +1 tribord (−x local), −1 bâbord.</summary>
    int _bellSide = 1;

    /// <summary>Le point d'où pend le câble : au bout d'un bossoir, au-dessus du plat-bord.</summary>
    Vector3 BellHang() => _ship.GlobalTransform *
        new Vector3(-_bellSide * (float)(_ship.Spec.B * 0.5 + 1.2), (float)(_ship.Spec.Hull.FreeboardMid + 3), 0);

    /* DU BORD QUI A LE PLUS D'EAU DESSOUS : à quai, l'un des deux donne sur le
       sable, et un équipage ne descend pas sa cloche sur la plage. */
    void PickBellSide()
    {
        var o = _sea.Core.Origin;
        double Depth(int side)
        {
            _bellSide = side;
            var h = BellHang();
            return -_world!.HeightAt(o.X + h.X, o.Z + h.Z);
        }
        double star = Depth(1), port = Depth(-1);
        _bellSide = star >= port ? 1 : -1;
    }

    void ToggleBell()
    {
        if (_inTitle || _bell == null || _world == null) return;
        if (!_bellOut)
        {
            if (_glassUp) { Say("Baissez d abord la lunette"); return; }
            if (_ship.Physics.Foundered) return;
            var v = _ship.Physics.Body.Vel;
            if (v.LengthXZ > 0.8)
            {
                Say("Pour mettre la cloche à l'eau, il faut être stoppé : en panne ou au mouillage");
                return;
            }
            PickBellSide();
            _bellOut = true; _bellHoist = false; _bellForced = false;
            _bellRope = 0; _bellAir = _bellAirMax; _bellOff = Vector2.Zero; _bellPosInit = false;
            _bellWarned = 0; _bellSaidReach = false; _bellSaidCable = false; _bellReach = null;
            LeaveCinema();
            SetGunPost(null);
            _bellCamWas = _camMode;
            _camMode = CamBell;
            SetLens(70, OutsideNear);
            Say("La cloche à l'eau — S : descendre, Z : remonter, Q/D et A/E : la porter, Entrée : saisir, \" : la hisser");
        }
        else if (!_bellForced && !_bellHoist)
        {
            _bellHoist = true;
            Say("On hisse la cloche");
        }
    }

    /// <summary>Les touches tenues, à la place de la barre : le navire est stoppé, on mène la cloche.</summary>
    void BellKeys(double dt)
    {
        if (_bellHoist) return;
        if (Physical(Key.S)) _bellRope += 0.7 * dt;
        if (Physical(Key.W)) _bellRope = Math.Max(0, _bellRope - 0.7 * dt);
        float lat = (Physical(Key.D) ? 1 : 0) - (Physical(Key.A) ? 1 : 0);
        float lon = (Physical(Key.E) ? 1 : 0) - (Physical(Key.Q) ? 1 : 0);
        _bellOff += new Vector2(lat, lon) * (float)(1.2 * dt);
        // on la porte de douze mètres au plus : au-delà, le câble ne pend plus, il tire
        if (_bellOff.Length() > 12) _bellOff = _bellOff.Normalized() * 12;
    }

    void StowBell()
    {
        _bellOut = false; _bellHoist = false;
        _bell?.Stow();
        if (_camMode == CamBell)
        {
            _camMode = _bellCamWas == CamBell ? 0 : _bellCamWas;
            if (_camMode == 1) EnterDeck();
            else if (_camMode == CamFlyBy) EnterFlyBy();
            else SetLens(OutsideFov, OutsideNear);
        }
        Say(_bellForced ? "La cloche est à bord — les plongeurs respirent" : "La cloche est à bord");
        UpdateInfo();
    }

    /// <summary>
    /// ⇧" : RENTRÉE D'UN COUP, sans les quarante secondes du treuil — ce qu'on fait
    /// quand on a vu ce qu'on voulait voir. Le treuil reste pour qui veut le temps
    /// de la remontée (et l'air qui s'épuise, c'est lui qui la fait d'office).
    /// </summary>
    void StowBellNow()
    {
        if (!_bellOut || _bellForced) return;
        _bellRope = 0;
        StowBell();
    }

    void TakeChest()
    {
        if (!_bellOut || _bellReach is not { } w || w.Looted) return;
        w.Looted = true;
        _bellReach = null;
        /* LES ÉCUS À LA BOURSE, le reste À BORD : un rubis ne se dépense pas, il se
           vend — au comptoir, à la pièce, mieux dans un port que dans l'autre. */
        if (w.Contents.Count == 0) w.Contents.Add(new Loot { Kind = "ecus", Count = (int)w.Ecus });
        var said = new List<string>();
        foreach (var l in w.Contents)
        {
            var k = _tresors.ByKey(l.Kind);
            if (k == null || k.ToPurse) _purse.Add(l.Count * (k?.Value ?? 1) * Market.SousParEcu);
            else _treasureHold[l.Kind] = _treasureHold.GetValueOrDefault(l.Kind) + l.Count;
            said.Add(_tresors.Say(l.Kind, l.Count));
        }
        string list = said.Count switch { 0 => "rien", 1 => said[0], _ => string.Join(", ", said.GetRange(0, said.Count - 1)) + " et " + said[^1] };
        Say($"Le coffre de la {w.Name} : {list} !");
        GD.Print($"coffre saisi : {w.Name} — {list}");
    }

    /// <summary>Une image de cloche : sa place, l'eau dedans, son air, ce qui est à portée.</summary>
    void BellTick(double dt)
    {
        if (_bell == null) return;
        if (_bellLabel != null) _bellLabel.Visible = _bellOut && _hudOn;
        if (!_bellOut) return;

        var hang = BellHang();
        var xf = _ship.GlobalTransform;
        // le travers et l'avant DU NAVIRE, à plat : on la porte dans son repère
        var side = new Vector3(xf.Basis.X.X, 0, xf.Basis.X.Z).Normalized();
        var fwd = new Vector3(xf.Basis.Z.X, 0, xf.Basis.Z.Z).Normalized();
        var target = new Vector3(hang.X, 0, hang.Z) - side * (_bellOff.X * _bellSide) + fwd * _bellOff.Y;
        var o = _sea.Core.Origin;
        double tx = o.X + target.X, tz = o.Z + target.Z;
        double surf = _sea.Core.Sample(target.X, target.Z, _sea.Core.Time);
        double bed = _world!.HeightAt(tx, tz);

        if (_bellHoist)
        {
            // le treuil, au pas des hommes au cabestan — plus vite quand l'air manque
            _bellRope -= (_bellForced ? 1.3 : 0.9) * dt;
            if (_bellRope <= 0) { StowBell(); return; }
        }
        double reach = Math.Min(_bellCable, -(bed + 0.15));
        if (_bellRope > reach)
        {
            if (!_bellSaidCable && reach >= _bellCable - 0.01) { Say($"Le câble est à bout : {_bellCable:F0} m"); _bellSaidCable = true; }
            _bellRope = Math.Max(0, reach);
        }
        // près de la surface la cloche suit la houle ; en bas, plus rien ne la bouge
        double bottomY = -_bellRope + surf * Math.Exp(-_bellRope / 6);
        var want = new Vector3(target.X, (float)bottomY, target.Z);
        if (!_bellPosInit) { _bellPos = want; _bellPosInit = true; }
        // elle traîne derrière son point de suspente, comme un pendule lesté dans l'eau
        var lag = _bellPos.Lerp(want, (float)(1 - Math.Exp(-dt / 1.6)));
        _bellPos = new Vector3(lag.X, (float)bottomY, lag.Z);
        var off = want - _bellPos;
        var tilt = new Vector3(off.Z, 0, -off.X) * 0.05f;
        var swing = tilt.Length() > 1e-4f ? new Basis(tilt.Normalized(), Math.Min(0.2f, tilt.Length())) : Basis.Identity;

        double depth = Math.Max(0, surf - bottomY);
        // à l'air, la cloche se renouvelle ; dessous, on respire un air de plus en plus comprimé
        if (depth < 0.4) _bellAir = _bellAirMax;
        else _bellAir -= dt * DivingBell.Burn(depth);
        double frac = _bellAir / _bellAirMax;
        if (_bellWarned < 1 && frac < 0.5) { _bellWarned = 1; Say("Air : la moitié de la réserve"); }
        if (_bellWarned < 2 && frac < 0.25) { _bellWarned = 2; Say("Air : un quart — il faut penser à remonter"); }
        if (_bellWarned < 3 && frac < 0.1) { _bellWarned = 3; Say("L'air s'épaissit — remontez !"); }
        if (_bellAir <= 0 && !_bellForced)
        {
            _bellForced = true; _bellHoist = true; _bellReach = null;
            Say("Plus d'air ! On hisse la cloche d'office");
        }

        // le coffre le plus proche, et s'il est à portée
        _bellReach = null;
        Wreck? nearest = null; double nd = double.MaxValue;
        if (!_bellHoist)
            foreach (var w in Wrecks.Near(_world.Region.Key, tx, tz, 120))
            {
                if (w.Looted) continue;
                var (cx, cz) = WreckRegistry.Chest(w, w.Beam);
                double d = Math.Sqrt((cx - tx) * (cx - tx) + (cz - tz) * (cz - tz));
                if (d < nd) { nd = d; nearest = w; }
                if (d < 3.5 && bottomY - _world.HeightAt(cx, cz) < 1.6) _bellReach = w;
            }
        if (_bellReach != null && !_bellSaidReach) { Say("Un coffre à portée de la cloche — Entrée pour le saisir"); _bellSaidReach = true; }
        // l'essai saisit de lui-même (--saisir 1)
        if (_diveTest.Take && _bellReach != null) { _diveTest.Take = false; TakeChest(); }
        if (_bellReach == null) _bellSaidReach = false;

        _bell.Pose(_bellPos, swing, depth, hang, depth > 3);

        // la jauge : ce qu'il faut savoir sans quitter la vue
        _bellLabel ??= BellLabel();
        int s = (int)Math.Max(0, _bellAir);
        _bellLabel.Text = FormattableString.Invariant(
            $"Cloche · fond {-bed:F0} m · câble {_bellRope:F1}/{_bellCable:F0} m · air {s / 60}:{s % 60:00}")
            + (_bellReach != null ? " · COFFRE À PORTÉE (Entrée)" : nearest != null ? $" · coffre à {nd:F0} m" : "");
        _bellLabel.Modulate = frac < 0.25 ? new Color(1, 0.55f, 0.45f) : Colors.White;
    }

    Label BellLabel()
    {
        var l = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            AnchorLeft = 0, AnchorRight = 1, OffsetTop = 18, OffsetBottom = 48,
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        l.AddThemeFontSizeOverride("font_size", 20);
        l.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.8f));
        l.AddThemeConstantOverride("outline_size", 6);
        _hud.AddChild(l);
        return l;
    }

    /// <summary>
    /// LA VUE DE LA CLOCHE : dans la poche d'air, sous le sommet, le regard vers
    /// l'ouverture et le fond. On tourne la tête au glisser de la souris.
    /// </summary>
    void BellCamera()
    {
        if (_bell == null || !_bellOut) { _camMode = 0; SetLens(OutsideFov, OutsideNear); return; }
        var eye = _bell.GlobalTransform * new Vector3(0, (float)(DivingBell.Height - 0.55), 0);
        float pitch = Mathf.Clamp(-0.55f - _pitch * 0.6f, -1.3f, 0.3f);
        var dir = new Vector3(Mathf.Sin(_orbit) * Mathf.Cos(pitch), Mathf.Sin(pitch), Mathf.Cos(_orbit) * Mathf.Cos(pitch));
        _cam.Position = eye;
        _cam.LookAt(eye + dir, Vector3.Up);
    }

    /// <summary>
    /// UNE ÉPAVE D'ESSAI (--epave 25) : un galion couché à tant de mètres par le
    /// travers tribord, son coffre plein — pour essayer la cloche sans avoir à
    /// couler qui que ce soit.
    /// </summary>
    void TestWreck(double dist)
    {
        if (_world == null) return;
        var b = _ship.Physics.Body;
        var o = _sea.Core.Origin;
        var side = b.Quat.Rotate(new Vec3d(-1, 0, 0));
        // c'est le COFFRE qu'on pose à cette distance : l'épave se place autour
        double px = o.X + b.Pos.X + side.X * dist, pz = o.Z + b.Pos.Z + side.Z * dist;
        string id = "essai" + Wrecks.All.Count;
        var w = new Wreck
        {
            Id = id, Ship = "frigate17e", Name = "Roter Löwe", Region = _world.Region.Key,
            Cap = 0.7, Beam = 7.25, Ecus = 240
        };
        // sombrée il y a tant de jours (--epave-age), à cette heure-ci
        var sunk = _calendar.Date.AddHours(_sky.Core.DayTime).AddDays(-_wreckAge);
        w.Date = $"{sunk:dd/MM/yyyy}"; w.Hour = sunk.TimeOfDay.TotalHours;
        var (cx, cz) = WreckRegistry.Chest(w, w.Beam);
        w.X = px - cx; w.Z = pz - cz;
        double x = w.X, z = w.Z;
        w.Depth = Math.Max(0, -_world.HeightAt(x, z));
        // le coffre d'un pirate, pour en voir de toutes les sortes
        w.Contents = _tresors.Draw(id, w.Ecus, pirate: true);
        Wrecks.Add(w);
        GD.Print(FormattableString.Invariant($"épave d'essai à {dist:F0} m, par {-_world.HeightAt(x, z):F1} m de fond"));
        if (_wreckAge > 0)
        {
            // l'œil sous l'eau, de trois quarts au-dessus du pont : la vase se juge là
            float bed = (float)_world.HeightAt(x, z);
            _fixLook = new Vector3((float)(x - o.X), bed + 2, (float)(z - o.Z));
            _fixEye = _fixLook + new Vector3(12, Math.Max(2f, -4.5f - bed), 14);
            _planted = true;
            GD.Print(FormattableString.Invariant($"[vase] épave de {_wreckAge:F0} jour(s) : couche {WreckRegistry.SiltCover(w, _calendar.Date.AddHours(_sky.Core.DayTime)):F2}"));
        }
    }

    double _diveTestIn = -1;
    /// <summary>--fond : l'œil sur le pâté le plus peuplé d'une foule du fond (« corail », « herbier »).</summary>
    string _bedTest = "";
    /// <summary>--epave-age : l'âge de l'épave d'essai, en jours (sa vase).</summary>
    double _wreckAge;
    (double? Wreck, bool Bell, double Rope, bool Take) _diveTest;

    int _debrisTest;
    bool _whereTest;

    void DiveTest()
    {
        if (_whereTest)
        {
            var o = _sea.Core.Origin; var p = _ship.Physics.Body.Pos;
            GD.Print(FormattableString.Invariant($"[ou] origine ({o.X:F0}, {o.Z:F0}), navire ({p.X:F0}, {p.Z:F0}) local, ({o.X + p.X:F0}, {o.Z + p.Z:F0}) vrai"));
        }
        // « x,z » en mètres vrais vise un point ; un mot, le pâté d'une foule
        var xz = _bedTest.Split(',');
        (double X, double Z)? at = xz.Length >= 2 && double.TryParse(xz[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var bx)
            && double.TryParse(xz[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var bz)
            ? (bx, bz) : _bedTest.Length > 0 ? _land?.CrowdSpot(_bedTest) : null;
        if (_bedTest.Length > 0 && at is { } spot)
        {
            // l'œil sous l'eau, au-dessus du pâté le plus peuplé de cette foule
            var o = _sea.Core.Origin;
            float bed = (float)_world!.HeightAt(spot.X, spot.Z);
            _fixLook = new Vector3((float)(spot.X - o.X), bed + 0.5f, (float)(spot.Z - o.Z));
            _fixEye = _fixLook + new Vector3(9, Math.Max(2.5f, Math.Min(5f, -bed - 1.5f)), 11);
            // « x,z,h » : l'œil haut de h mètres, en recul — le récif vu du ciel
            if (xz.Length == 3 && float.TryParse(xz[2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var eh))
                _fixEye = _fixLook + new Vector3(eh * 0.6f, eh, eh * 0.8f);
            _planted = true;
            GD.Print(FormattableString.Invariant($"[fond] « {_bedTest} » : pâté en ({spot.X:F0}, {spot.Z:F0}), par {-bed:F1} m"));
            _bedTest = "";
        }
        if (_debrisTest > 0)
        {
            var db = _ship.Physics.Body; var fw = db.Quat.Rotate(new Vec3d(0, 0, 1)); var o = _sea.Core.Origin;
            _flotsam.Scatter(o.X + db.Pos.X + fw.X * 40, o.Z + db.Pos.Z + fw.Z * 40, _debrisTest);
            GD.Print(FormattableString.Invariant($"[débris] {_debrisTest} jetés autour de ({db.Pos.X + fw.X * 40:F0}, {db.Pos.Z + fw.Z * 40:F0}) local, navire ({db.Pos.X:F0}, {db.Pos.Z:F0})"));
            _debrisTest = 0;
        }
        if (_beachTest > 0) BeachTest();
        if (_edTest != 0) EditTest();
        if (_whistleTest > 0) WhistleTest();
        if (_seeModels.Length > 0) SeeModels();
        if (_paintTest is Vector3 pt) { _paintTest = null; PaintTest(pt.X, pt.Z, pt.Y > 0.5); }
        if (_diveTest.Wreck is double d) TestWreck(d);
        if (_diveTest.Bell && !_bellOut) ToggleBell();
        if (_diveTest.Rope > 0) _bellRope = _diveTest.Rope;
    }

    void DiveTick(double dt, Vec3d here, Vec3d origin)
    {
        FounderTick();
        // à chaque image : l'origine glisse, et une épave doit glisser avec elle dans la même
        // le couvercle se lève sur le coffre que la cloche atteint
        if (_sites != null) _sites.Today = _calendar.Date.AddHours(_sky.Core.DayTime);
        _sites?.Update(Wrecks, here, origin, _bellReach?.Id, dt);
        if (_sites != null) foreach (var m in _sites.Hazed) { _sky.PushTo(m); _sky.SetCloud(m, _cloud, _t); }
        BellTick(dt);
    }
}
