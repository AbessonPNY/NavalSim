using Godot;
using System;
using System.Collections.Generic;
using NavalSim.Core;

namespace NavalSim;

/// <summary>
/// LA TRAVERSÉE : quitter une région par le large et arriver en vue d'une autre.
///
/// Les règles sont dans le noyau (<see cref="Passage"/>) ; ici, ce qu'il faut
/// pour que le navire qui arrive soit CELUI qui est parti. Le monde est lu une
/// fois et rien après ne change — c'est ce qui le rend sûr, et on ne le casse
/// pas : changer de région RECHARGE la scène, comme au lancement, et l'état du
/// bord (la coque, la bourse, la cale, la poudre, l'heure, le vent) passe la
/// frontière dans un objet statique, le seul qui survive au rechargement.
///
/// Ce qui NE passe PAS, et c'est voulu : les avaries (on a eu des jours pour
/// les réparer en route), l'ancre (on ne traverse pas mouillé), les débris et
/// les autres navires (ils sont restés où ils étaient).
/// </summary>
public partial class ShipDemo : Node3D
{
    /// <summary>Ce que le bord emporte d'une carte à l'autre.</summary>
    sealed class Voyage
    {
        public string Sheet = "", From = "";
        public Plan Plan = null!;
        public int Ship;
        public long Sous;
        public int Powder;
        public readonly List<(int Hold, double Level, double Side, double Kg, string Kind)> Cargo = new();
        public double T, Hour;
        public DateTime CalStart;
        public int CalDay;
        public double Force, WindDeg;
        public bool WeatherOn, SailsSet;
        public double SheetTrim;
    }

    /// <summary>La traversée en cours, entre le départ et l'arrivée : le rechargement la laisse en place.</summary>
    static Voyage? _voyage;
    /// <summary>Celle qu'on termine dans CETTE scène : prise à la malle au chargement, et à lui seul.</summary>
    Voyage? _arriving;

    /// <summary>La fiche de la région chargée, relative au dossier du projet.</summary>
    string _regionSheet = "world/caraibes.json";
    List<RegionSpec>? _regions;
    bool _offing;
    PanelContainer? _routePanel;
    VBoxContainer? _routeList;
    Label? _routeHead;
    Button? _routeButton;

    /// <summary>
    /// QUELLE RÉGION LIRE, avant tout le reste : celle où l'on arrive, sinon
    /// celle que demande --region, sinon la Jamaïque.
    /// </summary>
    string PickRegion()
    {
        _arriving = _voyage;
        _voyage = null;
        if (_arriving != null) return _arriving.Sheet;
        var args = OS.GetCmdlineUserArgs();
        for (int i = 0; i < args.Length - 1; i++)
            if (args[i] == "--region") return $"world/{args[i + 1]}.json";
        return "world/caraibes.json";
    }

    List<RegionSpec> Regions() => _regions ??= WorldLoad.Regions();

    /// <summary>Le nom d'une région d'après sa clé : « caraibes » → « La Jamaïque ».</summary>
    string RegionName(string key)
    {
        foreach (var r in Regions()) if (r.Key == key) return r.Name;
        return key;
    }

    /// <summary>La distance de la côte la plus proche, en mètres de jeu, là où est la coque.</summary>
    double OffingNow()
    {
        if (_world == null) return 0;
        var o = _sea.Core.Origin;
        var b = _ship.Physics.Body;
        return _world.ShoreDistance(o.X + b.Pos.X, o.Z + b.Pos.Z);
    }

    /// <summary>
    /// À chaque image : on vient de passer au large ? On le dit une fois, et la
    /// carte le sait — c'est là qu'on choisit une route, comme à bord.
    /// </summary>
    void PassageTick()
    {
        if (_world == null || _inTitle) return;
        bool now = OffingNow() >= Passage.Offing;
        if (now && !_offing && Regions().Count > 1)
            Say($"Au large {DeNom(_world.Region.Name)} — la carte (I) propose une traversée");
        _offing = now;
    }

    void BuildRoute(Control root)
    {
        _routeButton = new Button
        {
            Text = "Faire route…", FocusMode = Control.FocusModeEnum.None,
            AnchorLeft = 1, AnchorRight = 1, OffsetLeft = -230, OffsetRight = -12, OffsetTop = 40, OffsetBottom = 68
        };
        _routeButton.Pressed += () => { if (_routePanel!.Visible) CloseRoute(); else OpenRoute(); };
        root.AddChild(_routeButton);

        _routePanel = new PanelContainer
        {
            AnchorLeft = 1, AnchorRight = 1, OffsetLeft = -470, OffsetRight = -12, OffsetTop = 74,
            Visible = false, MouseFilter = Control.MouseFilterEnum.Stop
        };
        _routePanel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.05f, 0.06f, 0.08f, 0.94f),
            BorderColor = new Color(0.92f, 0.74f, 0.30f), BorderWidthLeft = 3,
            CornerRadiusTopLeft = 4, CornerRadiusTopRight = 4, CornerRadiusBottomLeft = 4, CornerRadiusBottomRight = 4,
            ContentMarginLeft = 14, ContentMarginRight = 12, ContentMarginTop = 10, ContentMarginBottom = 12
        });
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 8);
        _routeHead = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        _routeHead.AddThemeFontSizeOverride("font_size", 14);
        _routeHead.AddThemeColorOverride("font_color", new Color(0.90f, 0.92f, 0.94f));
        box.AddChild(_routeHead);
        _routeList = new VBoxContainer();
        _routeList.AddThemeConstantOverride("separation", 6);
        box.AddChild(_routeList);
        _routePanel.AddChild(box);
        root.AddChild(_routePanel);
    }

    bool OverRoute(Vector2 p) =>
        (_routePanel != null && _routePanel.Visible && _routePanel.GetGlobalRect().HasPoint(p))
        || (_routeButton != null && _routeButton.GetGlobalRect().HasPoint(p));

    static bool _cliVoyaged;

    /// <summary>--traversee tortue : faire route d'ici vers cette région, avec le vent d'ici.</summary>
    void SailTo(string key)
    {
        if (_world == null) return;
        var o = _sea.Core.Origin;
        var b = _ship.Physics.Body;
        var fix = _world.Geo.Fix(o.X + b.Pos.X, o.Z + b.Pos.Z);
        foreach (var r in Regions())
            if (r.Key == key && Passage.PlanTo(r, fix.Lat, fix.Lon, _windNowDeg) is { } plan) { SetSail(plan); return; }
        GD.PushWarning($"--traversee : région inconnue ou sans atterrage : {key}");
    }

    void CloseRoute() { if (_routePanel != null) _routePanel.Visible = false; }

    /// <summary>
    /// LES ROUTES POSSIBLES, d'ici et avec le vent d'ici. Au large, c'est une
    /// décision de capitaine ; près de terre, c'est un raccourci de débogage, et
    /// le panneau le dit plutôt que de le cacher.
    /// </summary>
    void OpenRoute()
    {
        if (_routePanel == null || _routeList == null || _world == null) return;
        foreach (var c in _routeList.GetChildren()) c.QueueFree();
        var o = _sea.Core.Origin;
        var b = _ship.Physics.Body;
        var fix = _world.Geo.Fix(o.X + b.Pos.X, o.Z + b.Pos.Z);
        double off = OffingNow();
        bool free = off >= Passage.Offing;
        _routeHead!.Text = free
            ? $"Au large {DeNom(_world.Region.Name)}. Vent {Passage.FromPoint(_windNowDeg)} : il fera la durée."
            : FormattableString.Invariant(
                $"La côte est à {off / 1000:F1} km : il faut en être à {Passage.Offing / 1000:F0} km pour faire route. Traversée de débogage :");
        int n = 0;
        foreach (var r in Regions())
        {
            if (r.Key == _world.Region.Key) continue;
            if (Passage.PlanTo(r, fix.Lat, fix.Lon, _windNowDeg) is not { } plan) continue;
            string allure = plan.Knots < 3 ? "au près, en louvoyant" : plan.Knots < 4.8 ? "au vent de travers" : "vent portant";
            var go = new Button
            {
                Text = FormattableString.Invariant(
                    $"{r.Name} — {plan.Miles:F0} milles {Passage.ToPoint(plan.Course)}, {allure} : {Passage.Say(plan.Hours)}"),
                FocusMode = Control.FocusModeEnum.None, Alignment = HorizontalAlignment.Left,
                AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(420, 0)
            };
            go.Pressed += () => SetSail(plan);
            _routeList.AddChild(go);
            n++;
        }
        if (n == 0) _routeList.AddChild(new Label { Text = "Aucune autre région n'a d'atterrage." });
        _routePanel.Visible = true;
    }

    /// <summary>
    /// ON PART. Le bord est mis dans la malle, un rideau dit la traversée, et la
    /// scène est rechargée sur l'autre carte une image plus tard — le temps que
    /// le rideau soit peint.
    /// </summary>
    void SetSail(Plan plan)
    {
        var p = _ship.Physics;
        var v = new Voyage
        {
            Sheet = $"world/{plan.To.Key}.json", From = _world?.Region.Name ?? "", Plan = plan,
            Ship = _index, Sous = _purse.Sous, Powder = p.Powder,
            T = _t, Hour = _sky.Core.DayTime, CalStart = _calendar.Start, CalDay = _calendar.Day,
            Force = _force, WindDeg = _windNowDeg, WeatherOn = _weather.On,
            SailsSet = _ship.Ctrl.SailsSet, SheetTrim = _ship.Ctrl.Sheet
        };
        foreach (var c in p.Cargo) v.Cargo.Add((c.Hold, c.Level, c.Side, c.Kg, c.Kind));
        _voyage = v;
        SaveBook();
        SaveQuests();

        var veil = new CanvasLayer { Layer = 10 };
        AddChild(veil);
        veil.AddChild(new ColorRect { AnchorRight = 1, AnchorBottom = 1, Color = new Color(0.02f, 0.03f, 0.04f) });
        var say = new Label
        {
            AnchorRight = 1, AnchorBottom = 1, HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center, AutowrapMode = TextServer.AutowrapMode.WordSmart,
            Text = FormattableString.Invariant(
                $"Traversée vers {plan.To.Name}\n\n{plan.Miles:F0} milles {Passage.ToPoint(plan.Course)}, {plan.Knots:F1} nœuds sur la route\n{Passage.Say(plan.Hours)} de mer")
        };
        say.AddThemeFontSizeOverride("font_size", 26);
        say.AddThemeColorOverride("font_color", new Color(0.92f, 0.86f, 0.70f));
        veil.AddChild(say);
        GetTree().CreateTimer(0.25).Timeout += () => GetTree().ReloadCurrentScene();
    }

    /// <summary>
    /// ON ARRIVE : à la place de la mise à quai du lancement. Le bord ressort de
    /// la malle, l'horloge avance de la traversée, et la coque est posée à
    /// l'atterrage, sur l'erre, cap sur la terre.
    /// </summary>
    bool Arrive()
    {
        if (_arriving is not { } v || _world == null) return false;
        _arriving = null;
        var plan = v.Plan;
        var p = _ship.Physics;

        // la bourse, la poudre, la cale — à leurs places exactes
        _purse = new Purse(v.Sous);
        p.Powder = Math.Min(v.Powder, p.PowderMax);
        p.ClearCargo();
        foreach (var c in v.Cargo) p.LoadCargo(c.Hold, c.Level, c.Side, c.Kg / 1000, c.Kind);

        /* L'HORLOGE AVANCE DE LA TRAVERSÉE, au pas du ciel : DayRate heures de
           ciel par minute de jeu. Le cours des épices suit l'horloge de jeu, et
           le calendrier tourne ses pages. */
        double rate = _sky.DayRate > 0 ? _sky.DayRate : 2.0;
        _t = v.T + plan.Hours * 60 / rate;
        double hour = v.Hour + plan.Hours;
        _calendar.SetStart(v.CalStart.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
        for (int d = 0; d < v.CalDay + (int)Math.Floor(hour / 24); d++) _calendar.NextDay();
        _sky.Core.SetTimeOfDay(hour % 24, _sky.Latitude);

        // le vent du départ : la météo le reprend d'où il était, s'il la laissait faire
        _force = v.Force; _windDeg = v.WindDeg;
        _weather.On = v.WeatherOn;
        if (v.WeatherOn) _weather.Sync(_force, _windDeg);
        Restate();

        // à l'atterrage : l'origine y glisse, la coque reste près de zéro
        var at = _world.Geo.ToXZ(plan.At.Lat, plan.At.Lon);
        var o = _sea.Core.Origin;
        _sea.Core.Rebase(at.X - o.X, at.Z - o.Z);
        var b = p.Body;
        double yaw = -plan.At.Heading * Math.PI / 180;     // vrai → le lacet du jeu : l'est est −x
        b.Pos = new Vec3d(0, _eqY, 0);
        b.Quat = Quatd.FromAxisAngle(new Vec3d(0, 1, 0), yaw);
        b.AngVel = new Vec3d(0, 0, 0);
        b.Vel = b.Quat.Rotate(new Vec3d(0, 0, 2.5));        // cinq nœuds d'erre : on arrive en route
        _ship.Ctrl.SailsSet = v.SailsSet;
        _ship.Ctrl.Sheet = v.SheetTrim;
        _ship.SyncTransform();
        _askTitle = false;
        ReckonArrive(at.X, at.Z, plan.Miles);

        GD.Print(FormattableString.Invariant(
            $"traversée : {v.From} → {plan.To.Name}, {plan.Miles:F0} M en {plan.Hours:F1} h ; atterrage « {plan.At.Name} », fond {-_world.HeightAt(at.X, at.Z):F0} m ; ")
            + FormattableString.Invariant($"bourse {_purse.Sous} sous, cale {p.CargoTonnes:F1} t dont {p.CargoOf("epice"):F1} t d'épices, poudre {p.Powder}, {_calendar.Date:yyyy-MM-dd} {hour % 24:F1} h, t {v.T:F0} → {_t:F0} s"));
        _offing = true;     // l'arrivée le dit déjà
        ShowNotice(plan.To.Name,
            $"Après {Passage.Say(plan.Hours)} de mer, la terre est en vue : vous voici à {plan.At.Name}.\n\n"
            + _calendar.Date.ToString("d MMMM yyyy", new System.Globalization.CultureInfo("fr-FR")) + $", {(int)(hour % 24)} h.");
        return true;
    }
}
