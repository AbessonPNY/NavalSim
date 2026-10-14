using Godot;
using System;
using System.Collections.Generic;
using NavalSim.Core;

namespace NavalSim;

/// <summary>
/// LE NAUFRAGE : l'air qui remonte, et ce qui reste à flot. Le noyau tient les
/// deux mécaniques (WreckAir) ; ici on leur prête la mer, l'embrun, le champ
/// d'écume et les mots.
/// </summary>
public partial class ShipDemo
{
    readonly WreckAir _wreckAir = new();
    readonly List<WreckHull> _wrecks = new();
    FlotsamNode _flotsam = null!;
    BubbleNode _bubbles = null!;
    CoinNode _coins = null!;

    void WireWreck()
    {
        // la gerbe basse que chaque poche soulève en crevant, et le bouillon qui reste
        _wreckAir.OnBurst = (at, water, speed, jet) => _spray.Pool.Burst(at, water, speed, jet);
        // ce qu'on voit du trajet : la poche elle-même, qui monte en chapelet
        _wreckAir.OnSlug = (at, v, rise, travel, spread) =>
            _bubbles.Slug(at.ToGodot(), v, rise, travel, spread);
        _flotsam.BottleOneIn = _bottleOneIn;
        // ce qui crève la surface en remontant jette son peu d'eau, par la même réserve
        _flotsam.OnBreak = (at, water, speed) => _spray.Pool.Burst(at, water, speed);
        /* CE QU'ELLE EMPORTE. Une cale qui s'ouvre lâche sa bourse : les pièces
           descendent en voltigeant et l'eau les avale. Le compte suit sa taille —
           un galion emporte plus qu'une chaloupe. */
        _flotsam.OnWreck = s =>
        {
            var b = s.Physics.Body;
            _coins.Spill(b.Pos.ToGodot(),
                s.Spec.L, s.Spec.B, (int)Math.Clamp(s.Spec.L * 10, 120, 500));
        };
        // ce qu'elle contient : une page de journal, ou une carte (ShipDemo.Bottle.cs)
        _flotsam.OnBottle = OpenBottle;
        _flotsam.OnCargo = ClaimCargo;
        _flotsam.World = _world;
        _wreckAir.OnLastBreath = (at, v) =>
        {
            /* LE DERNIER BOUILLON : tout l'air de ses hauts part d'un coup, et la mer
               bout là où elle a disparu — plus large et plus long qu'elle en avait */
            _wreckAir.Churn(at.X, at.Z, Math.Clamp(3 + 0.8 * Math.Sqrt(v), 5, 16), 20, Math.Clamp(v / 8, 0.6, 1.5));
            var b = _ship.Physics.Body;
            if ((at - b.Pos).Length < 3000)
                Say(FormattableString.Invariant($"Elle disparaît dans un grand bouillon — {v:F0} m³ d'air d'un coup."));
        };
    }

    /* ------------------------------------------------------------------ */
    /*  ELLE REPOSE PAR LE FOND                                            */
    /* ------------------------------------------------------------------ */

    bool _lost;
    PanelContainer? _lostBox;

    /// <summary>La hauteur d'équilibre trouvée au lancement : celle où on la remet.</summary>
    double _eqY;

    void BuildLost(CanvasLayer layer)
    {
        _lostBox = new PanelContainer
        {
            AnchorLeft = 0.5f, AnchorRight = 0.5f, AnchorTop = 0.32f, AnchorBottom = 0.32f,
            OffsetLeft = -260, OffsetRight = 260, OffsetTop = -52, OffsetBottom = 52,
            Visible = false, MouseFilter = Control.MouseFilterEnum.Ignore
        };
        _lostBox.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.10f, 0.03f, 0.03f, 0.90f),
            BorderColor = new Color(0.55f, 0.18f, 0.14f),
            BorderWidthLeft = 1, BorderWidthRight = 1, BorderWidthTop = 1, BorderWidthBottom = 1,
            CornerRadiusTopLeft = 6, CornerRadiusTopRight = 6,
            CornerRadiusBottomLeft = 6, CornerRadiusBottomRight = 6,
            ContentMarginLeft = 20, ContentMarginRight = 20, ContentMarginTop = 14, ContentMarginBottom = 14
        });
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 8);
        var title = _lostTitle = new Label { Text = "Votre navire a sombré !", HorizontalAlignment = HorizontalAlignment.Center };
        title.AddThemeFontSizeOverride("font_size", 22);
        title.AddThemeColorOverride("font_color", new Color(0.95f, 0.82f, 0.78f));
        var how = _lostHow = new Label
        {
            Text = "Elle repose par le fond.   R pour la renflouer.",
            HorizontalAlignment = HorizontalAlignment.Center
        };
        how.AddThemeFontSizeOverride("font_size", 15);
        how.AddThemeColorOverride("font_color", new Color(0.88f, 0.84f, 0.82f));
        box.AddChild(title); box.AddChild(how);
        _lostBox.AddChild(box);
        layer.AddChild(_lostBox);
    }

    /// <summary>
    /// PERDUE QUAND IL N'EN DÉPASSE PLUS RIEN — la règle de la page : tant qu'une
    /// pomme de mât perce encore la surface, on ne le lui dit pas. La page exige
    /// dix mètres d'eau au-dessus du point le plus haut ; deux suffisent ici, et
    /// il le FAUT : dans une rade de onze mètres une coque posée sur le sable n'a
    /// jamais dix mètres sur la tête, et le bandeau ne venait pas là où l'on
    /// s'échoue le plus souvent. Deux mètres, c'est déjà « on ne la voit plus »,
    /// et c'est au-dessus du creux d'une houle ordinaire.
    ///
    /// Elle reste VISIBLE, contrairement à la page qui l'escamote : ici l'eau se
    /// traverse du regard, et voir sa propre coque posée sur le sable est ce que
    /// le naufrage a de plus juste.
    /// </summary>
    Label? _lostTitle, _lostHow;
    double _capsizedFor;

    /* CHAVIRÉE : la tête en bas, ou couchée au-delà de quatre-vingts degrés,
       sans avoir coulé — une coque retournée flotte sur l'air de ses fonds, et
       le jeu ne le voyait pas : le bandeau attendait un naufrage, et R ne
       redressait qu'une épave (signalé). Trois secondes, pour ne pas crier au
       chavirage sur un coup de roulis. */
    bool Capsized() => _ship.Physics.Body.Quat.Rotate(new Vec3d(0, 1, 0)).Y < 0.17;

    /// <summary>
    /// CAPTURÉE, ET BRÛLÉE. Le pirate qu'on a canonné ne se contente pas de
    /// piller : il met le feu aux poudres avant de partir. C'est la seule fin
    /// dont on ne revient pas — <see cref="Salvage"/> n'y peut rien, et la barre
    /// ne répond plus.
    ///
    /// La soute saute pour de bon (<c>BlowUp</c>) plutôt qu'un écran posé sur une
    /// coque intacte : ce qu'on voit doit être ce qui est arrivé, et le joueur a
    /// le droit de regarder son navire brûler pendant qu'il lit la sentence.
    /// </summary>
    bool _captured;

    void Captured()
    {
        if (_captured || _lostBox == null) return;
        _captured = true;
        BlowUp(_ship);
        _lostTitle!.Text = "Votre navire est capturé — les pirates ne font pas de quartier.";
        _lostHow!.Text = LastSave() != null
            ? "⏎ pour reprendre au dernier point de sauvegarde.        Échap : le titre."
            : "Aucune partie enregistrée.        Échap pour revenir au titre.";
        _lostBox.Visible = true;
        JournalLog("Capturés. Les pirates n'ont pas fait de quartier.");
    }

    /// <summary>
    /// REPRENDRE — la partie enregistrée la plus fraîche du mode où l'on est.
    /// <c>LoadGame</c> recharge la scène entière, donc la coque en feu ne survit
    /// pas à la reprise : il n'y a rien à nettoyer ici.
    /// </summary>
    void ResumeSave()
    {
        var s = LastSave();
        _captured = false;
        Found();
        if (s != null) LoadGame(s);
        else { Open(); MainItems(); }
    }

    void LostTick()
    {
        // le naufrage du film n'est pas le vôtre
        if (Filming) return;
        // le nôtre se raconte d'abord ; le bandeau vient après (ShipDemo.SinkFilm.cs)
        if (_sinkT >= 0) return;
        if (_lostBox == null || _captured) return;   // capturée : le bandeau est à elle
        _capsizedFor = Capsized() && !_ship.Physics.Foundered ? _capsizedFor + (double)GetProcessDeltaTime() : 0;
        if (_capsizedFor > 3)
        {
            if (!_lost)
            {
                _lost = true;
                _lostTitle!.Text = "Votre navire a chaviré !";
                _lostHow!.Text = "Il flotte la quille en l'air.   R pour le redresser.";
                _lostBox.Visible = true;
            }
            return;
        }
        if (!_ship.Physics.Foundered) { if (_lost) Found(); return; }
        if (_lost) return;
        _lostTitle!.Text = "Votre navire a sombré !";
        _lostHow!.Text = "Elle repose par le fond.   R pour la renflouer.";
        var b = _ship.Physics.Body;
        var box = _ship.LocalBounds();
        double top = b.Pos.Y + box.End.Y;
        double sea = _sea.Core.Sample(b.Pos.X, b.Pos.Z, _t);
        if (top - sea > -2) return;
        _lost = true;
        _lostBox.Visible = true;
    }

    void Found()
    {
        _lost = false;
        if (_lostBox != null) _lostBox.Visible = false;
    }

    /// <summary>
    /// RÉPARER ET RENFLOUER, la touche R de la page : les trous bouchés, l'eau
    /// pompée, la mâture replantée — et si elle était au fond, elle retrouve sa
    /// flottaison sur place, sans être renvoyée au port.
    /// </summary>
    void Salvage()
    {
        if (_captured) return;                 // on ne radoube pas un navire qu'on n'a plus
        // rompue, elle se remplace entière : on ne recoud pas une coque dont l'avant est au fond
        if (_ship.Physics.Broken) { ClearCarcass(_ship); Rebuild(); }
        // remise à flot : ce n'est plus une épave
        Refloated();
        // le radoub éteint ce qui brûlait : on ne répare pas un navire en feu
        Douse(_ship);
        bool sunk = _lost || _ship.Physics.Foundered;
        bool capsized = Capsized();
        bool aground = Aground();
        _ship.Physics.Salvage();
        _ship.RestoreMasts();
        if (!sunk && !capsized && !aground) { Say("Radoub : la mâture est remise en état."); return; }
        var b = _ship.Physics.Body;
        /* REDRESSÉE À SON CAP : la gîte et l'assiette effacées, le lacet gardé —
           elle était posée face à l'identité (cap au nord), ce qui la faisait
           pivoter d'un bloc. L'étrave projetée à plat, ou le travers si elle
           pointait droit en l'air. */
        var f = b.Quat.Rotate(new Vec3d(0, 0, 1));
        var flat = new Vec3d(f.X, 0, f.Z);
        if (flat.Length < 0.2) { var x = b.Quat.Rotate(new Vec3d(1, 0, 0)); flat = new Vec3d(-x.Z, 0, x.X); }
        double yaw = Math.Atan2(flat.X, flat.Z);
        /* À FLOT, ET NON SUR PLACE quand la place est à sec : remise à sa flottaison dans le
           sable, la coque était chassée par le fond et bondissait jusqu'aux maisons (une ILCA
           échouée sur la plage de Port-Royal). On la porte à l'eau libre la plus proche,
           l'étrave vers le large. */
        double px = b.Pos.X, pz = b.Pos.Z;
        bool moved = false;
        if (aground || !WaterUnder(px, pz))
        {
            if (NearestWater(px, pz) is { } w)
            {
                if (Math.Abs(w.X - px) + Math.Abs(w.Z - pz) > 0.5) yaw = Math.Atan2(w.X - px, w.Z - pz);
                px = w.X; pz = w.Z; moved = true;
            }
        }
        b.Pos = new Vec3d(px, _eqY, pz);
        b.Vel = Vec3d.Zero;
        b.AngVel = Vec3d.Zero;
        b.Quat = Quatd.FromAxisAngle(new Vec3d(0, 1, 0), yaw);
        _ship.SyncTransform();
        _capsizedFor = 0;
        Found();
        Say(sunk ? "Renflouée : les pompes ont eu raison de l'eau, la mâture est replantée."
                 : capsized ? "Redressée : l'équipage a remis la coque sur sa quille, la mâture est replantée."
                 : "Déséchouée : l'équipage l'a halée jusqu'à l'eau libre.");
        if (moved && (sunk || capsized)) Say("Remise à flot en eau libre : il n'y avait plus assez de fond où elle était.");
    }

    /// <summary>
    /// -- --desechouer 1 : le navire posé à terre près du port de départ, puis R, et ce qu'il en
    /// advient dans la console — l'essai du déséchouage, sans rien écrire.
    /// </summary>
    double _strandTest = -1;
    void StrandTestTick(double dt)
    {
        if (_strandTest < 0 || _world?.StartPort is not { } home) return;
        double t0 = _strandTest;
        _strandTest += dt;
        var b = _ship.Physics.Body;
        var o = _sea.Core.Origin;
        string Where() => FormattableString.Invariant(
            $"({b.Pos.X + o.X:F1}, {b.Pos.Z + o.Z:F1}) sol {_world.HeightAt(b.Pos.X + o.X, b.Pos.Z + o.Z):F2} m, y {b.Pos.Y:F2}, touche {_ship.Physics.Aground:F2} m");
        if (t0 < 4 && _strandTest >= 4)
        {
            // un point à terre, à moins de 400 m du port, à plus d'un mètre au-dessus de l'eau
            for (double r = 30; r < 400; r += 10)
                for (int i = 0; i < 24; i++)
                {
                    double a = i * Math.PI / 12, x = home.X + Math.Cos(a) * r, z = home.Z + Math.Sin(a) * r;
                    if (_world.HeightAt(x, z) < 1.0) continue;
                    b.Pos = new Vec3d(x - o.X, _world.HeightAt(x, z) + 0.5, z - o.Z);
                    b.Vel = Vec3d.Zero; b.AngVel = Vec3d.Zero;
                    _ship.SyncTransform();
                    _fixLook = new Vector3((float)b.Pos.X, (float)b.Pos.Y, (float)b.Pos.Z);
                    _fixEye = _fixLook + new Vector3(40, 25, 40); _planted = true;
                    GD.Print("[échouage] posé à terre en " + Where());
                    goto done;
                }
            done:;
        }
        if (t0 < 8 && _strandTest >= 8) { GD.Print("[échouage] avant R : " + Where() + $", échoué {Aground()}"); Salvage(); GD.Print("[échouage] juste après R : " + Where()); }
        if (t0 < 14 && _strandTest >= 14)
        {
            GD.Print("[échouage] six secondes après R : " + Where() + FormattableString.Invariant($", vitesse {b.Vel.Length:F2} m/s, échoué {Aground()}, tirant d'eau {DraftOf():F2} m"));
            GetTree().Quit(0);
        }
    }

    /// <summary>Son tirant d'eau, d'après sa flottaison de repos et le bas de sa coque.</summary>
    double DraftOf() => Math.Max(0.3, -(_eqY + _ship.LocalBounds().Position.Y));

    /// <summary>Touche-t-elle le fond ? Le solveur le dit (Aground) ; sinon, l'eau sous l'étrave, le milieu et l'étambot.</summary>
    bool Aground()
    {
        if (_ship.Physics.Aground > 0.01) return true;
        var b = _ship.Physics.Body;
        var f = b.Quat.Rotate(new Vec3d(0, 0, _ship.Spec.L * 0.45));
        return !WaterUnder(b.Pos.X, b.Pos.Z) || !WaterUnder(b.Pos.X + f.X, b.Pos.Z + f.Z) || !WaterUnder(b.Pos.X - f.X, b.Pos.Z - f.Z);
    }

    /// <summary>Assez d'eau pour sa quille en ce point (repère local, l'origine flottante ajoutée).</summary>
    bool WaterUnder(double x, double z)
    {
        if (_world == null) return true;
        var o = _sea.Core.Origin;
        return _world.HeightAt(x + o.X, z + o.Z) < -(DraftOf() + 0.3);
    }

    /// <summary>
    /// L'EAU LIBRE LA PLUS PROCHE, en repère local : la règle des anneaux (Teleport.Check),
    /// assez de fond pour la quille sur un rond d'une longueur de coque. Par cercles de
    /// plus en plus grands, jusqu'à trois kilomètres ; nul s'il n'y en a pas.
    /// </summary>
    Vec3d? NearestWater(double x, double z)
    {
        if (_world == null) return null;
        var o = _sea.Core.Origin;
        double draft = DraftOf(), len = _ship.Spec.L;
        double step = Math.Max(4, len * 0.5);
        for (double r = 0; r <= 3000; r += step)
        {
            int n = r < 1 ? 1 : Math.Max(12, (int)(2 * Math.PI * r / step));
            for (int i = 0; i < n; i++)
            {
                double a = i * 2 * Math.PI / n;
                double wx = x + Math.Cos(a) * r, wz = z + Math.Sin(a) * r;
                if (Teleport.Check(_world, wx + o.X, wz + o.Z, draft, len) == Teleport.Verdict.Bon) return new Vec3d(wx, 0, wz);
            }
        }
        return null;
    }

    /// <summary>
    /// CE QUI RESTE DE L'ANCIEN, quand R en met un neuf à l'eau : sa moitié avant qui
    /// sombrait et les pièces qui s'en étaient arrachées. Laissées, elles flottaient à
    /// côté du navire neuf — la carcasse du précédent.
    /// </summary>
    void ClearCarcass(ShipNode s)
    {
        for (int i = _halves.Count - 1; i >= 0; i--)
        {
            var h = _halves[i];
            if (h.From != s) continue;
            if (IsInstanceValid(s)) s.ForgetUnder(h.Node);
            h.Node.QueueFree();
            _halves.RemoveAt(i);
        }
        for (int i = _loose.Count - 1; i >= 0; i--)
        {
            var L = _loose[i];
            if (L.Owner != s) continue;
            if (IsInstanceValid(L.Node)) L.Node.QueueFree();
            _loose.RemoveAt(i);
        }
        _looseAt.Remove(s);
    }

    readonly List<(Waterline, double)> _rings = new();

    /// <summary>
    /// LE COLLIER D'ÉCUME DES PIÈCES QUI COULENT (demandé) : une moitié d'un navire
    /// rompu, ou une coque qui a embarqué plus du tiers de son volume, tant qu'elle
    /// perce la surface. Plus elle s'enfonce vite et plus la mer est forte, plus il est
    /// franc ; il ne tombe pas sous la moitié — l'eau qui se rue dans une coque bat
    /// toujours. Celles qui avancent ont déjà le leur (u_collar, la mer).
    /// </summary>
    /// <summary>L'air qu'une coque qui coule doit encore chasser, en m³, avant sa prochaine gerbe.</summary>
    readonly Dictionary<ShipPhysics, double> _blowDebt = new();
    readonly Random _blowRng = new();
    readonly Dictionary<ShipPhysics, double> _blowLast = new();

    void SinkRings(double dt)
    {
        _rings.Clear();
        void Add(ShipPhysics p)
        {
            if (!(p.Broken || p.FloodVol > 0.33 * p.HullVolume) || !p.Water.Pierces) { _blowDebt.Remove(p); _blowLast.Remove(p); return; }
            double sink = Math.Max(0, -p.Body.Vel.Y);
            _rings.Add((p.Water, Math.Clamp(0.55 + 0.6 * sink + 0.05 * _force, 0.55, 1)));
            Blow(p, dt);
        }
        Add(_ship.Physics);
        foreach (var s in _others) Add(s.Physics);
        foreach (var h in _halves) Add(h.Phys);
        _foam.SetRings(_rings);
    }

    /* ELLE SOUFFLE (demandé : des gerbes presque verticales près de la coque). Chaque
       mètre cube que la mer y fait entrer en chasse un d'air, et tant que ses ouvertures
       sont à fleur d'eau il sort EN FORCE par les écoutilles et les sabords : des
       geysers étroits, d'eau et d'écume, qui jaillissent le long de sa ligne d'eau. Leur
       cadence est son débit d'envahissement (FloodRate) — une gerbe pour deux mètres
       cubes, à peu près —, leur taille aussi ; un jet relevé (3) les fait monter droit,
       en colonne, au lieu d'une couronne. L'air qui part de DESSOUS est l'affaire de
       WreckAir ; celui-ci part de la ligne d'eau. */
    void Blow(ShipPhysics p, double dt)
    {
        double rate = Math.Max(0, p.FloodRate);
        double debt = _blowDebt.GetValueOrDefault(p) + rate * dt;
        double need = 1.2 + 1.6 * _blowRng.NextDouble();
        // pas plus de six par seconde : à cent mètres cubes par seconde, chaque gerbe en porte davantage
        double since = _t - _blowLast.GetValueOrDefault(p, -9);
        if (debt < need || since < 0.15) { _blowDebt[p] = debt; return; }
        _blowDebt[p] = 0;
        _blowLast[p] = _t;
        var w = p.Water;
        double u = _blowRng.NextDouble() * Math.PI * 2;
        double lx = w.A * Math.Cos(u) * 1.02, lz = w.B * Math.Sin(u) * 1.02;
        double c = Math.Cos(w.Angle), s = Math.Sin(w.Angle);
        double x = w.X + c * lx - s * lz, z = w.Z + s * lx + c * lz;
        var at = new Vec3d(x, _sea.Core.Sample(x, z, _t), z);
        double water = Math.Clamp(0.25 + 0.15 * debt, 0.3, 2.5);
        _spray.Pool.Burst(at, water, 8 + 5 * _blowRng.NextDouble(), 3);
    }

    /// <summary>Une image de naufrage : l'air de chaque coque, les bouillons dans l'écume, les débris.</summary>
    void WreckTick(double dt)
    {
        _wrecks.Clear();
        _wrecks.Add(new WreckHull(_ship.Physics, _ship.HullShell()));
        foreach (var s in _others) _wrecks.Add(new WreckHull(s.Physics, s.HullShell()));
        // la moitié avant d'un navire rompu : son air marque où elle sombre (sans coque visible à elle : ses compartiments)
        foreach (var h in _halves) _wrecks.Add(new WreckHull(h.Phys, null));
        _wreckAir.Update(dt, _wrecks, _sea.Core, _t);
        _foam.SetBoils(_wreckAir.Boils);
        LooseGunsTick(dt);
        SinkRings(dt);
        _flotsam.Step(dt, _sea.Core, _allShipsForFlotsam(), _ship, _cam);
        LostTick();
        _bubbles.Step(dt, _sea.Core, _t);
        // l'or ne s'allume pas tout seul : il lui faut la course du soleil et sa couleur
        _sky.PushTo(_coins.Material);
        _coins.Step(dt);
    }

    readonly List<ShipNode> _flotsamFleet = new();
    IReadOnlyList<ShipNode> _allShipsForFlotsam()
    {
        _flotsamFleet.Clear();
        _flotsamFleet.Add(_ship);
        _flotsamFleet.AddRange(_others);
        return _flotsamFleet;
    }
}
