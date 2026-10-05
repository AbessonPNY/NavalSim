using Godot;
using System;
using System.Collections.Generic;
using NavalSim.Core;

namespace NavalSim;

/// <summary>
/// À L'EAU — nager à la première personne, en surface et en apnée, pour explorer
/// les hauts-fonds et ramasser ce qu'ils donnent (demandé).
///
/// ON SAUTE DU BORD, navire stoppé (4) : celui qui n'est pas mouillé dérive pendant
/// qu'on nage, comme en vrai ; un équipage serre la toile en vous voyant passer
/// par-dessus bord, un dériveur en solitaire choque sa voile et part au gré du
/// vent. On REMONTE par le flanc (E), en nageant jusqu'à lui.
///
/// ZQSD mènent l'homme, la souris bouton tenu (ou les flèches) tourne sa tête ; Espace remonte, Ctrl (ou C) descend — en
/// surface, c'est le canard ; ⇧ force l'allure, et le souffle part plus vite.
/// F ramasse ce qui est à portée de main. La physique et le souffle sont au
/// noyau (Swimmer.cs, chiffres et raisons) ; ce qui se ramasse aussi (Finds.cs).
///
/// LA SYNCOPE : à bout de souffle, la vue se voile et l'on perd connaissance. On
/// est repêché — par l'équipage, ou par la chance si l'on était seul — et le sac
/// retombe au fond, où ce qu'il contenait se retrouvera.
/// </summary>
public partial class ShipDemo
{
    const int CamSwim = 7;
    /// <summary>De quoi saisir une chose au fond : la longueur d'un bras, et un peu.</summary>
    const double SwimReach = 1.6;
    /// <summary>On se hisse par le flanc depuis cette distance du bordé.</summary>
    const double SwimBoard = 2.5;
    /// <summary>Les choses du fond apparaissent dans ce rayon ; au-delà, l'eau les cache.</summary>
    const double SwimSee = 64;

    Swimmer? _swim;
    bool _swimming;
    int _swimCamWas;
    double _swimLook;              // le temps depuis le dernier regard sur les cases
    double _swimFade;              // le voile de la syncope, 0 à 1
    bool _swimRescue, _swimSaidCramp;
    Label? _swimLabel;
    ColorRect? _swimVeil;
    ShaderMaterial? _swimVeilMat;
    /// <summary>Ce que porte le sac : des choses du fond, avec leur id.</summary>
    readonly List<Find> _sack = new();
    /// <summary>Ce qui a été ramassé, et quel jour (calendrier) : les coquillages repoussent, les pièces non.</summary>
    readonly Dictionary<string, int> _picked = new();
    /// <summary>Les cases tirées, et ce qui y est posé.</summary>
    readonly Dictionary<(int, int), List<(Find F, Placed P)>> _swimCells = new();
    readonly HashSet<string> _swimWrecks = new();
    readonly PlacedSet _swimPlaced = new();
    Node3D? _swimRoot;
    readonly Dictionary<string, Node3D?> _swimModels = new();
    readonly List<RockField.Rock> _swimRocks = new();
    readonly List<Find> _swimBuf = new();
    Find? _swimNear;

    /// <summary>Un coquillage ramassé repousse ; une pièce, jamais.</summary>
    bool StillPicked(Find f) =>
        _picked.TryGetValue(f.Id, out int day) && (f.Kind == "ecus" || _calendar.Day - day < Finds.RegrowDays);

    void SwimJump()
    {
        if (_swimming || _bellOut || _mother != null || _world == null || _inTitle || _editing || _film != null) return;
        var b = _ship.Physics.Body;
        if (b.Vel.LengthXZ > 0.8) { Say("On ne saute pas d'un navire qui court — stoppez d'abord"); return; }
        var spec = _ship.Spec;
        var o = _sea.Core.Origin;
        // par-dessus le bord de bâbord (+x), au milieu, à deux pas du bordé
        var fwd = b.Quat.Rotate(new Vec3d(0, 0, 1));
        var side = b.Quat.Rotate(new Vec3d(1, 0, 0));
        Vec3d at = b.Pos + side * (spec.B * 0.5 + 1.2);
        double sea = _sea.Core.Sample(at.X, at.Z, _t);
        _swim = new Swimmer
        {
            Pos = new Vec3d(o.X + at.X, sea - 0.4, o.Z + at.Z),
            Yaw = Math.Atan2(side.X, side.Z), Pitch = -0.1,
            Under = true, DiveTime = 0, Vel = side * 0.8 + new Vec3d(0, -0.6, 0)
        };
        _swimming = true;
        _swimCamWas = _camMode;
        _camMode = CamSwim;
        SetLens(70, 0.03f);
        _sack.Clear();
        _swimSaidCramp = false;
        _swimFade = 0; _swimRescue = false;
        // le navire qu'on quitte : un équipage serre la toile ; seul à bord, on choque en grand
        var c = _ship.Ctrl;
        c.Throttle = 0;
        bool anchored = _anchor2?.IsDown(_ship) == true;
        if ((spec.Equipage ?? 2) > 1) { c.SailsSet = false; Say(anchored ? "À l'eau ! Le navire est au mouillage" : "À l'eau ! L'équipage serre la toile et vous attend"); }
        else { c.Sheet = spec.MaxSheet; Say(anchored ? "À l'eau ! Le bateau est mouillé" : "À l'eau ! Seul à bord, le bateau dérive — ne le perdez pas"); }
        if (_swimRoot == null) { _swimRoot = new Node3D { Name = "Nage" }; AddChild(_swimRoot); }
        GD.Print($"[nage] à l'eau depuis {spec.Name}, {(anchored ? "mouillé" : "non mouillé")}");
    }

    /// <summary>La remontée à bord : par le flanc, à portée du bordé, en surface.</summary>
    bool SwimCanBoard(out string why)
    {
        why = "";
        if (_swim == null) return false;
        if (_swim.Under) { why = "remontez d'abord à la surface"; return false; }
        var b = _ship.Physics.Body;
        var o = _sea.Core.Origin;
        var l = b.Quat.Inverted().Rotate(new Vec3d(_swim.Pos.X - o.X, 0, _swim.Pos.Z - o.Z) - new Vec3d(b.Pos.X, 0, b.Pos.Z));
        double half = _ship.Spec.L * 0.5;
        if (Math.Abs(l.Z) < half * 0.85 && Math.Abs(l.X) < _ship.Spec.B * 0.5 + SwimBoard) return true;
        double dx = Math.Max(0, Math.Abs(l.X) - _ship.Spec.B * 0.5), dz = Math.Max(0, Math.Abs(l.Z) - half * 0.85);
        why = FormattableString.Invariant($"le bord est à {Math.Sqrt(dx * dx + dz * dz):F0} m");
        return false;
    }

    void SwimBoardShip()
    {
        if (!SwimCanBoard(out string why)) { Say("Trop loin pour se hisser — " + why); return; }
        // ce que rapporte le sac : à bord, au comptoir ; les huîtres s'ouvrent ici
        int lambis = 0, huitres = 0, perles = 0, ecus = 0;
        foreach (var f in _sack)
        {
            switch (f.Kind)
            {
                case "lambi": lambis++; break;
                case "ecus": ecus++; break;
                case "huitre":
                    huitres++;
                    if (Finds.Seed(f.Id, 7, 7, 7) < 1.0 / Finds.PearlOdds) perles++;
                    break;
            }
        }
        if (lambis > 0) _treasureHold["lambi"] = _treasureHold.GetValueOrDefault("lambi") + lambis;
        if (perles > 0) _treasureHold["perle"] = _treasureHold.GetValueOrDefault("perle") + perles;
        if (ecus > 0) _purse.Add(ecus * (_tresors.ByKey("ecus")?.Value ?? 1) * Market.SousParEcu);
        var said = new List<string>();
        if (lambis > 0) said.Add(_tresors.Say("lambi", lambis));
        if (huitres > 0) said.Add($"{huitres} huître{(huitres > 1 ? "s" : "")} ouverte{(huitres > 1 ? "s" : "")} : " + (perles > 0 ? _tresors.Say("perle", perles) + " !" : "pas de perle"));
        if (ecus > 0) said.Add(_tresors.Say("ecus", ecus));
        Say(said.Count > 0 ? "À bord. " + string.Join(" · ", said) : "À bord.");
        if (said.Count > 0) JournalLog("Plongé aux hauts-fonds : " + string.Join(", ", said) + ".");
        _sack.Clear();
        SwimEnd();
    }

    void SwimEnd()
    {
        _swimming = false;
        _swim = null;
        _camMode = _swimCamWas == CamSwim ? 0 : _swimCamWas;
        SetLens(OutsideFov, OutsideNear);
        if (_swimLabel != null) _swimLabel.Visible = false;
        foreach (var cell in _swimCells.Values) foreach (var (_, p) in cell) p.Node.QueueFree();
        _swimCells.Clear();
        _swimWrecks.Clear();
        _swimPlaced.Items.Clear();
    }

    /// <summary>Les touches tenues, et le pas du nageur — à la place de la barre.</summary>
    void SwimKeys(double dt)
    {
        var c = _ship.Ctrl;
        c.Throttle = 0;
        c.Rudder *= Math.Max(0, 1 - dt * 2.5);     // la barre lâchée revient d'elle-même
        if (_swim == null || _world == null) return;
        var inp = new SwimInput();
        bool free = !_swimRescue && _pause?.Visible != true && !_menu.Visible;
        if (free)
        {
            inp.Fwd = (Physical(Key.W) ? 1 : 0) - (Physical(Key.S) ? 1 : 0);
            inp.Side = (Physical(Key.D) ? 1 : 0) - (Physical(Key.A) ? 1 : 0);
            inp.Up = (Physical(Key.Space) ? 1 : 0) - (Physical(Key.Ctrl) || Physical(Key.C) ? 1 : 0);
            inp.Hard = Input.IsKeyPressed(Key.Shift);
            // les flèches tournent aussi la tête : pour qui n'a pas de souris sous la main
            _swim.Yaw -= ((Physical(Key.Right) ? 1 : 0) - (Physical(Key.Left) ? 1 : 0)) * 1.6 * dt;
            _swim.Pitch = Math.Clamp(_swim.Pitch + ((Physical(Key.Up) ? 1 : 0) - (Physical(Key.Down) ? 1 : 0)) * 1.2 * dt, -1.45, 1.45);
        }

        var o = _sea.Core.Origin;
        var w = _world;
        Func<double, double, double> floorAt = (x, z) => SwimFloor(w, x, z);
        int n = Math.Max(1, (int)Math.Ceiling(dt / (1.0 / 30)));
        double h = dt / n;
        for (int i = 0; i < n; i++)
        {
            var p = _swim.Pos;
            double sea = _sea.Core.Sample(p.X - o.X, p.Z - o.Z, _t);
            _swim.Step(h, inp, sea, floorAt(p.X, p.Z), floorAt);
            // le bordé : on ne traverse pas une coque
            if (_hullCollider != null)
            {
                var lp = new Vec3d(_swim.Pos.X - o.X, _swim.Pos.Y, _swim.Pos.Z - o.Z);
                var v = _swim.Vel;
                _hullCollider.Collide(ref lp, ref v);
                _swim.Pos = new Vec3d(lp.X + o.X, lp.Y, lp.Z + o.Z);
                _swim.Vel = v;
            }
        }
        if (!_swimSaidCramp && _swim.Under && _swim.Breath < Swimmer.Contractions)
        { _swimSaidCramp = true; Say("Le diaphragme se contracte — il est temps de remonter"); }
        if (!_swim.Under) _swimSaidCramp = false;
    }

    /// <summary>Le fond sous le nageur : le relief, ou le dessus d'un rocher.</summary>
    double SwimFloor(World w, double x, double z)
    {
        double f = w.HeightAt(x, z);
        w.Rocks.Near(x, z, 0.3, _swimRocks);
        foreach (var rk in _swimRocks) f = Math.Max(f, RockField.TopAt(rk, x, z));
        return f;
    }

    /// <summary>Chaque image : le voile, ce qui est à portée, les cases autour, la jauge.</summary>
    void SwimTick(double dt)
    {
        if (!_swimming || _swim == null || _world == null) return;
        var o = _sea.Core.Origin;
        _swimPlaced.Update(new Vec3d(o.X, 0, o.Z));

        // LA SYNCOPE : le voile tombe, puis on est repêché
        if (_swim.Blackout && !_swimRescue) { _swimRescue = true; GD.Print("[nage] syncope"); }
        if (_swimRescue)
        {
            _swimFade = Math.Min(1, _swimFade + dt / 2.5);
            if (_swimFade >= 1) SwimRescued();
        }
        SwimVeil();

        // les cases autour de lui, tirées une fois ; celles qu'il a quittées, rendues
        if ((_swimLook -= dt) <= 0)
        {
            _swimLook = 0.5;
            SwimCells();
        }

        // ce qui est à portée de main : le plus proche, devant lui
        _swimNear = null;
        double best = SwimReach;
        var look = _swim.Look;
        foreach (var cell in _swimCells.Values)
            foreach (var (f, p) in cell)
            {
                if (!p.Node.Visible) continue;
                var d = new Vec3d(f.X - _swim.Pos.X, f.Y + 0.05 - _swim.Pos.Y, f.Z - _swim.Pos.Z);
                double L = d.Length;
                if (L < best && (L < 0.6 || d.Dot(look) / L > 0.3)) { best = L; _swimNear = f; }
            }

        _swimLabel ??= BellLabel();
        _swimLabel.Visible = true;
        double depth = Math.Max(0, _sea.Core.Sample(_swim.Pos.X - o.X, _swim.Pos.Z - o.Z, _t) - _swim.Pos.Y);
        double bed = -_world.HeightAt(_swim.Pos.X, _swim.Pos.Z);
        string near = _swimNear == null ? "" : _swimNear.Kind switch
        {
            "lambi" => " · un lambi à portée (F)",
            "huitre" => " · une huître à portée (F)",
            _ => " · une pièce à portée (F)"
        };
        string board = !_swim.Under && SwimCanBoard(out _) ? " · E : se hisser à bord" : "";
        _swimLabel.Text = FormattableString.Invariant(
            $"{(_swim.Under ? $"Sous l'eau · {depth:F1} m" : "En surface")} · fond {bed:F0} m · souffle {_swim.Breath * 100:F0} % · sac {_sack.Count}")
            + near + board;
        _swimLabel.Modulate = _swim.Breath < 0.2 ? new Color(1, 0.55f, 0.45f) : Colors.White;
    }

    void SwimCells()
    {
        if (_swim == null || _world == null || _swimRoot == null) return;
        string region = _world.Region.Key;
        var (ci, cj) = Finds.CellOf(_swim.Pos.X, _swim.Pos.Z);
        int span = (int)Math.Ceiling(SwimSee / Finds.Cell);
        for (int j = cj - span; j <= cj + span; j++)
            for (int i = ci - span; i <= ci + span; i++)
            {
                if (_swimCells.ContainsKey((i, j))) continue;
                double cx = (i + 0.5) * Finds.Cell - _swim.Pos.X, cz = (j + 0.5) * Finds.Cell - _swim.Pos.Z;
                if (cx * cx + cz * cz > (SwimSee + Finds.Cell) * (SwimSee + Finds.Cell)) continue;
                _swimBuf.Clear();
                Finds.InCell(_world, region, i, j, _swimBuf, _swimRocks);
                _swimCells[(i, j)] = SwimPose(_swimBuf);
            }
        // les pièces d'une épave proche, une fois
        foreach (var wr in Wrecks.Near(region, _swim.Pos.X, _swim.Pos.Z, SwimSee + 40))
        {
            if (!_swimWrecks.Add(wr.Id)) continue;
            _swimBuf.Clear();
            Finds.AroundWreck(_world, wr, _swimBuf);
            _swimCells[(int.MinValue + _swimWrecks.Count, 0)] = SwimPose(_swimBuf);
        }
        // les cases lointaines : rendues (pas celles des épaves, qu'on ne retire jamais en plongée)
        var drop = new List<(int, int)>();
        foreach (var key in _swimCells.Keys)
        {
            if (key.Item1 < int.MinValue / 2) continue;
            double cx = (key.Item1 + 0.5) * Finds.Cell - _swim.Pos.X, cz = (key.Item2 + 0.5) * Finds.Cell - _swim.Pos.Z;
            if (cx * cx + cz * cz > (SwimSee + 2.5 * Finds.Cell) * (SwimSee + 2.5 * Finds.Cell)) drop.Add(key);
        }
        foreach (var key in drop)
        {
            foreach (var (_, p) in _swimCells[key]) { _swimPlaced.Items.Remove(p); p.Node.QueueFree(); }
            _swimCells.Remove(key);
        }
    }

    List<(Find, Placed)> SwimPose(List<Find> finds)
    {
        var list = new List<(Find, Placed)>();
        foreach (var f in finds)
        {
            if (StillPicked(f)) continue;
            var node = SwimModel(f.Kind);
            if (node == null) continue;
            // couché sur son appui (la pente d'un rocher), tourné sur lui-même
            var up = new Vector3((float)f.Up.X, (float)f.Up.Y, (float)f.Up.Z).Normalized();
            var axis = Vector3.Up.Cross(up);
            var tilt = axis.LengthSquared() > 1e-6f ? new Basis(axis.Normalized(), Vector3.Up.AngleTo(up)) : Basis.Identity;
            node.Basis = tilt * new Basis(Vector3.Up, (float)f.Yaw);
            var hold = new Node3D { Name = f.Kind };
            hold.AddChild(node);
            _swimRoot!.AddChild(hold);
            list.Add((f, _swimPlaced.Add(hold, f.X, f.Y, f.Z)));
        }
        return list;
    }

    /// <summary>Le modèle d'une sorte : le lambi et l'huître dessinés (tools/reef-glb.js), la pièce un disque d'or.</summary>
    Node3D? SwimModel(string kind)
    {
        if (kind == "ecus")
        {
            var coin = new MeshInstance3D
            {
                Mesh = new CylinderMesh { TopRadius = 0.019f, BottomRadius = 0.019f, Height = 0.003f, RadialSegments = 16, Rings = 1 },
                MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.86f, 0.68f, 0.32f), Metallic = 1, Roughness = 0.35f },
                Position = new Vector3(0, 0.0015f, 0),
                Rotation = new Vector3(0.2f, 0, 0.1f)
            };
            return coin;
        }
        if (!_swimModels.TryGetValue(kind, out var tmpl))
        {
            string path = Assets.Path($"props/fond/{kind}.glb");
            tmpl = System.IO.File.Exists(path) ? Assets.LoadGlb(path) : null;
            if (tmpl == null) GD.PushWarning($"[nage] props/fond/{kind}.glb introuvable");
            _swimModels[kind] = tmpl;
        }
        return tmpl?.Duplicate() as Node3D;
    }

    void SwimTake()
    {
        if (_swim == null || _swimNear is not { } f) return;
        if (_sack.Count >= 10) { Say("Le sac est plein — remontez à bord le vider"); return; }
        _sack.Add(f);
        _picked[f.Id] = _calendar.Day;
        foreach (var cell in _swimCells.Values)
            foreach (var (ff, p) in cell)
                if (ff == f) p.Node.Visible = false;
        _swimNear = null;
        Say(f.Kind switch { "lambi" => "Un lambi dans le sac", "huitre" => "Une huître dans le sac", _ => "Une pièce d'or !" });
    }

    /// <summary>Repêché : à bord, sans le sac — ce qu'il portait est retombé au fond.</summary>
    void SwimRescued()
    {
        foreach (var f in _sack) _picked.Remove(f.Id);
        int lost = _sack.Count;
        _sack.Clear();
        bool crew = (_ship.Spec.Equipage ?? 2) > 1;
        Say(crew ? "Repêché par l'équipage, à demi noyé" + (lost > 0 ? " — le sac est resté au fond" : "")
                 : "Revenu à vous en surface, vous regagnez le bord à grand-peine" + (lost > 0 ? " — sans le sac" : ""));
        JournalLog(crew ? "Perdu connaissance en plongée ; repêché par l'équipage." : "Perdu connaissance en plongée ; regagné le bord de justesse.");
        SwimEnd();
        _swimFade = 1;                 // le voile se lève à bord
    }

    /// <summary>Le voile : les bords qui noircissent quand l'air manque, puis le noir.</summary>
    void SwimVeil()
    {
        if (_swimVeil == null)
        {
            var sh = new Shader
            {
                Code = @"shader_type canvas_item;
uniform float u_edge;
uniform float u_black;
void fragment() {
    float r = length((UV - 0.5) * vec2(1.6, 1.0));
    float a = u_edge * smoothstep(0.25, 0.85, r) + u_black;
    COLOR = vec4(0.0, 0.0, 0.0, clamp(a, 0.0, 1.0));
}"
            };
            _swimVeilMat = new ShaderMaterial { Shader = sh };
            _swimVeil = new ColorRect { Material = _swimVeilMat, MouseFilter = Control.MouseFilterEnum.Ignore };
            _swimVeil.SetAnchorsPreset(Control.LayoutPreset.FullRect);
            _hud.AddChild(_swimVeil);
        }
        double edge = _swim != null && _swim.Under ? Math.Clamp((0.25 - _swim.Breath) / 0.25, 0, 1) * 0.9 : 0;
        _swimVeilMat!.SetShaderParameter("u_edge", (float)edge);
        _swimVeilMat.SetShaderParameter("u_black", (float)_swimFade);
        _swimVeil.Visible = edge > 0 || _swimFade > 0;
    }

    double _swimTestIn = -1, _swimTestStep = -1;
    string _swimTestKind = "";
    bool _swimTestTake;
    int _swimTestPhase;

    /// <summary>
    /// L'ESSAI, sans clavier : sauter, être posé au-dessus de la chose la plus proche
    /// de cette sorte (dans un mille et demi), la prendre, revenir au flanc et
    /// monter à bord — chaque étape dite à la console.
    /// </summary>
    void SwimTest(double dt)
    {
        if (_swimTestIn > 0 && _booted && (_swimTestIn -= dt) <= 0)
        {
            SwimJump();
            if (_swim == null || _world == null || _swimTestKind.Length == 0) return;
            var o = _sea.Core.Origin; var b = _ship.Physics.Body;
            double sx = o.X + b.Pos.X, sz = o.Z + b.Pos.Z;
            Find? best = null; double bd = double.MaxValue;
            var buf = new List<Find>();
            var (ci, cj) = Finds.CellOf(sx, sz);
            for (int j = cj - 47; j <= cj + 47; j++)
                for (int i = ci - 47; i <= ci + 47; i++)
                {
                    buf.Clear();
                    Finds.InCell(_world, _world.Region.Key, i, j, buf, _swimRocks);
                    foreach (var f in buf)
                    {
                        if (f.Kind != _swimTestKind || StillPicked(f)) continue;
                        double d = (f.X - sx) * (f.X - sx) + (f.Z - sz) * (f.Z - sz);
                        if (d < bd) { bd = d; best = f; }
                    }
                }
            if (best == null) { GD.Print($"[nage] aucun « {_swimTestKind} » dans un mille et demi"); return; }
            _swim.Pos = new Vec3d(best.X + 0.55, best.Y + 0.75, best.Z + 0.55);
            _swim.Yaw = Math.Atan2(-0.55, -0.55); _swim.Pitch = -0.75;
            _swim.Under = true; _swim.DiveTime = 1; _swim.Vel = new Vec3d(0, 0, 0);
            GD.Print(FormattableString.Invariant($"[nage] posé au-dessus de {best.Id}, à {Math.Sqrt(bd):F0} m du navire, fond {-best.Y:F1} m"));
            _swimTestStep = 1.5; _swimTestPhase = 0;
        }
        if (_swimTestStep > 0 && (_swimTestStep -= dt) <= 0 && _swim != null)
        {
            if (_swimTestPhase == 0)
            {
                GD.Print($"[nage] à portée : {(_swimNear?.Id ?? "rien")}, souffle {_swim.Breath:F2}, sous l'eau {_swim.Under}");
                if (_swimTestTake) { SwimTake(); GD.Print($"[nage] sac : {_sack.Count}"); _swimTestStep = 1.0; _swimTestPhase = 1; }
            }
            else if (_swimTestPhase == 1)
            {
                // au flanc, en surface, et à bord
                var o = _sea.Core.Origin; var b = _ship.Physics.Body;
                var side = b.Quat.Rotate(new Vec3d(1, 0, 0));
                var at = b.Pos + side * (_ship.Spec.B * 0.5 + 1.0);
                _swim.Pos = new Vec3d(o.X + at.X, _sea.Core.Sample(at.X, at.Z, _t) + Swimmer.EyeAbove, o.Z + at.Z);
                _swim.Under = false;
                long before = _purse.Sous;
                SwimBoardShip();
                GD.Print($"[nage] à bord : {string.Join(", ", _treasureHold)} ; bourse +{_purse.Sous - before} sous");
            }
        }
    }

    /// <summary>Hors de l'eau, le voile d'une syncope se lève sur le pont.</summary>
    void SwimAfter(double dt)
    {
        SwimTest(dt);
        if (_swimming || _swimFade <= 0) return;
        _swimFade = Math.Max(0, _swimFade - dt / 2.0);
        SwimVeil();
    }

    void SwimCamera()
    {
        if (_swim == null) { _camMode = 0; SetLens(OutsideFov, OutsideNear); return; }
        var o = _sea.Core.Origin;
        var eye = new Vector3((float)(_swim.Pos.X - o.X), (float)_swim.Pos.Y, (float)(_swim.Pos.Z - o.Z));
        // la traction lance la tête en avant et un peu vers le haut, puis elle revient dans la glisse
        double pull = _swim.Stroke < Swimmer.Pull ? Math.Sin(Math.PI * _swim.Stroke / Swimmer.Pull) * _swim.Vigor : 0;
        _cam.Position = eye + _swim.Look.ToGodot() * (float)(0.05 * pull) + new Vector3(0, (float)(0.03 * pull), 0);
        _cam.LookAt(eye + _swim.Look.ToGodot(), Vector3.Up);
    }

    /// <summary>Les touches à un cran du nageur ; vrai si l'événement est pris.</summary>
    bool SwimInputEvent(InputEvent e)
    {
        if (!_swimming || _swim == null) return false;
        /* LE REGARD EN GLISSANT, bouton tenu, comme partout ailleurs dans le jeu — et non
           souris capturée : sur ses écrans virtuels (vorpX, Virtual Desktop) une souris
           capturée n envoie aucun mouvement, la tête ne tournait pas (signalé). */
        if (e is InputEventMouseMotion mm && (mm.ButtonMask & (MouseButtonMask.Left | MouseButtonMask.Right)) != 0)
        {
            _swim.Yaw -= mm.Relative.X * 0.0035;
            _swim.Pitch = Math.Clamp(_swim.Pitch - mm.Relative.Y * 0.0035, -1.45, 1.45);
            return true;
        }
        if (e is InputEventKey k && k.Pressed && !k.Echo)
        {
            Key key = k.PhysicalKeycode != Key.None ? k.PhysicalKeycode : k.Keycode;
            switch (key)
            {
                case Key.F: SwimTake(); return true;
                case Key.E: SwimBoardShip(); return true;
                // ce qui garde son sens à l'eau : la pause, les objectifs, le mémento
                case Key.Escape: case Key.Tab: case Key.F1: return false;
            }
        }
        // le reste ne commande rien à l'eau : ni canons, ni barre, ni caméra
        return e is InputEventKey or InputEventMouseButton;
    }
}
