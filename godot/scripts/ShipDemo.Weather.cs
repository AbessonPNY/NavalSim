using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using NavalSim.Core;

namespace NavalSim;

/// <summary>Le temps qu'il fait, et ce qui tombe (sorti de ShipDemo.cs).</summary>
public partial class ShipDemo
{
    // ------------------------------------------------------------------
    //  LE TEMPS QU'IL FAIT — la météo qui se conduit seule, et les
    //  dépressions, qui ont un lieu (noyau : Weather, Storms)
    // ------------------------------------------------------------------

    readonly Weather _weather = new();
    readonly Storms _storms = new();
    // la mer qui court après le vent, au plus à Weather.SeaRate
    double _lagForce = 4, _lagDir = 210, _windNowDeg = 210;
    /// <summary>--vent-journal : le vent, toutes les deux secondes, pour l'essai.</summary>
    int _windLog;
    bool _inSquall;
    Squall _squall;
    string? _seaMaster;

    // ------------------------------------------------------------------
    //  CE QUI TOMBE — le climat (température, averses), la pluie, la neige,
    //  et la neige qui tient sur les ponts
    // ------------------------------------------------------------------

    PrecipNode _precip = null!;
    /// <summary>Les traînées basses du petit matin (settings.json → fog.rasante).</summary>
    MistNode? _mist;
    double _mistAmount = 1.0, _mistTop = 2.0, _mistPatch = 90, _mistForce = -1, _mistGain = 0.45;
    Calendar _calendar = new();
    SeaFogSettings _fogRules = new();
    /// <summary>Le manteau de neige : settings.json → snow, les mêmes chiffres que la page.</summary>
    SnowSettings _snowRules = new();
    /// <summary>La lumière de la houle sur le fond : settings.json → caustics.</summary>
    CausticSettings _causticRules = new();
    /// <summary>
    /// LES CADRANS DES CAUSTIQUES SUR LA PASSE DES CARÈNES — les mêmes qu'au
    /// fond, lus une seule fois. La passe est créée par la première coque à
    /// naître, donc après ce point : on la règle aussi à sa naissance, et ici
    /// pour celles qui suivent.
    /// </summary>
    void CausticDials()
    {
        if (ShipNode.Caustic is not ShaderMaterial m) return;
        m.SetShaderParameter("u_caustic_gain", _causticRules.Enabled ? (float)_causticRules.Gain : 0f);
        m.SetShaderParameter("u_caustic_depth", (float)_causticRules.Depth);
        m.SetShaderParameter("u_caustic_far", (float)_causticRules.Far);
        m.SetShaderParameter("u_caustic_floor", (float)_causticRules.Floor);
        m.SetShaderParameter("u_caustic_spread", (float)_causticRules.Spread);
    }

    /// <summary>Les bancs des hauts-fonds : settings.json → fish.</summary>
    bool _causticDialed;
    FishSettings _fishRules = new();
    FishNode? _fishNode;
    /// <summary>Les mouettes : settings.json → gulls, s'il existe.</summary>
    static GullRules GullsJson(System.Text.Json.JsonElement k)
    {
        var g = new GullRules();
        double D(string n, double v) => k.Num(n, v);
        if (k.TryGetProperty("enabled", out var on) && (on.ValueKind == System.Text.Json.JsonValueKind.False || on.ValueKind == System.Text.Json.JsonValueKind.True))
            g.Enabled = on.GetBoolean();
        g.Count = (int)Math.Clamp(D("nombre", g.Count), 0, 200);
        g.Followers = (int)Math.Clamp(D("suiveuses", g.Followers), 0, g.Count);
        g.Span = Math.Clamp(D("envergure", g.Span), 0.2, 6);
        g.ShoreRange = Math.Max(100, D("portee", g.ShoreRange));
        g.Reach = Math.Max(50, D("rayon", g.Reach));
        return g;
    }

    /// <summary>Les mouettes des côtes : settings.json → gulls.</summary>
    GullRules _gullRules = new();
    GullNode? _gulls;
    /// <summary>Les dauphins de l'étrave : settings.json → dolphins, le bloc de la page.</summary>
    DolphinRules _dolphinRules = new();
    Dolphins? _dolphins;
    DolphinNode? _dolphinNode;
    SeaFog? _seaFog;
    bool _saidFog;

    /* LA BRUME DE SURFACE : tirée au soir, montée et levée en heures de jeu, et
       posée sur le ciel, qui la mêle à la brume de tout le monde. Dite quand elle
       monte et quand elle se lève — une nappe qui vous prend sans un mot se lit
       comme un défaut d'affichage. */
    void FogTick(double hours, double dayTime)
    {
        _seaFog ??= new SeaFog(_fogRules);
        _seaFog.Update(hours, dayTime, _sea.Core.SeaState, 6.0);
        _sky.Core.Fog = _seaFog.Amount;
        _sky.Core.FogDensity = _fogRules.Density;
        _sky.Core.FogHeight = _fogRules.Height;
        if (!_saidFog && _seaFog.Amount > 0.3) { _saidFog = true; if (!_inTitle) Say("La brume monte sur l'eau"); }
        else if (_saidFog && _seaFog.Amount < 0.1) { _saidFog = false; if (!_inTitle) Say("La brume se lève"); }
    }
    /// <summary>La rade des ports : tenue, portée, et les fiches qu on y mouille.</summary>
    bool _mooredOn = true;
    double _mooredRange = 3200;
    string[] _mooredShips = { "sloop.json", "frigate17e.json", "frigate.json" };

    Climate _climate = new();
    (double Amount, bool Snow) _fall;

    /* LE CLIMAT DE LA PAGE, lu dans SON settings.json : le départ du calendrier et
       la section « climate ». Un fichier absent ou abîmé laisse les valeurs par
       défaut, qui sont celles de climate.js. */
    void LoadClimate()
    {
        string path = Assets.Path("settings.json");
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(System.IO.File.ReadAllText(path));
            var root = doc.RootElement;
            if (root.TryGetProperty("calendar", out var c) && c.TryGetProperty("start", out var s))
                _calendar = new Calendar(s.GetString());
            if (root.TryGetProperty("ghosts", out var gh)) _ghosts.Rules = GhostRules.FromJson(gh);
            if (root.TryGetProperty("wraith", out var vf)) _wraithRules = WraithRules.FromJson(vf);
            if (root.TryGetProperty("fire", out var feu))
            {
                _fireRules = FireSettings.FromJson(feu);
                /* RETENU, PAS POUSSÉ. Les réglages sont lus AVANT que le nœud des
                   effets existe, et le lui pousser ici levait une exception qui
                   emportait TOUT LE RESTE de la lecture en silence — la seule
                   trace était « settings.json illisible », et le climat, la
                   baleine, le serpent et la brume repassaient aux valeurs par
                   défaut. Un try qui couvre trente réglages ne pardonne pas qu'on
                   touche à un objet pas encore né. */
                if (feu.Opt("light") is double fl)
                    _fireLight = Math.Max(0, fl);
            }
            /* CE QUI RESTE DE LA TOILE SOUS LES ÉTOILES. La lumière qui traverse
               le tissage est celle du CIEL : sans jour derrière, une voile ne
               donne rien. Le plancher laisse de quoi deviner la mâture. */
            if (root.TryGetProperty("night", out var nt)
                && nt.Opt("canvas") is double cv)
                ShipNode.CanvasFloor = Math.Clamp(cv, 0, 1);
            if (root.TryGetProperty("whale", out var wh)) _whaleRules = WhaleSettings.FromJson(wh);
            if (root.TryGetProperty("serpent", out var sp)) _serpentRules = SerpentSettings.FromJson(sp);
            if (root.TryGetProperty("fog", out var fg))
            {
                _fogRules = SeaFogSettings.FromJson(fg);
                if (fg.Opt("rasante") is double ra)
                    _mistAmount = Math.Clamp(ra, 0, 3);
                if (fg.Opt("rasanteHaut") is double rh)
                    _mistTop = Math.Clamp(rh, 0.2, 30);
                if (fg.Opt("rasanteBancs") is double rb)
                    _mistPatch = Math.Clamp(rb, 5, 600);
                if (fg.Opt("rasanteVoile") is double rv)
                    _mistGain = Math.Clamp(rv, 0, 1);
            }
            if (root.TryGetProperty("reckoning", out var rk)) _reckRules = ReckoningSettings.FromJson(rk);
            if (root.TryGetProperty("wreck", out var wr) && wr.TryGetProperty("bottleOneIn", out var bo))
                _bottleOneIn = bo.GetInt32();
            if (root.TryGetProperty("gunnery", out var gu)) _gunRules = GunnerySettings.FromJson(gu);
            if (root.TryGetProperty("encounters", out var ec)) _metRules = EncounterSettings.FromJson(ec);
            if (root.TryGetProperty("snow", out var sw)) _snowRules = SnowSettings.FromJson(sw);
            if (root.TryGetProperty("caustics", out var ca)) _causticRules = CausticSettings.FromJson(ca);
            if (root.TryGetProperty("fish", out var fi)) _fishRules = FishSettings.FromJson(fi);
            if (root.TryGetProperty("gulls", out var mo)) _gullRules = GullsJson(mo);
            if (root.TryGetProperty("chimneys", out var chm) && chm.TryGetProperty("enabled", out var che))
                _chimneyRules = che.ValueKind != System.Text.Json.JsonValueKind.False;
            if (root.TryGetProperty("chimneys", out var chd) && chd.Opt("density") is double cdn)
                _chimneyDensity = Math.Clamp(cdn, 0, 1);
            if (root.TryGetProperty("whistle", out var whs))
            {
                if (whs.TryGetProperty("oneIn", out var w1) && w1.ValueKind == System.Text.Json.JsonValueKind.Number) _whistleOneIn = Math.Max(1, w1.GetInt32());
                if (whs.Opt("range") is double w2) _whistleRange = Math.Max(1, w2);
                if (whs.Opt("minFlight") is double w3) _whistleMinFlight = Math.Max(0, w3);
            }
            if (root.TryGetProperty("camera", out var cmr) && cmr.Opt("handheld") is double hhd)
                _handheld = Math.Clamp(hhd, 0, 4);
            if (root.TryGetProperty("dolphins", out var da)) _dolphinRules = DolphinRules.FromJson(da);
            if (root.TryGetProperty("storm", out var st))
            {
                if (st.TryGetProperty("lightning", out var li)) _lightRules = LightningSettings.FromJson(li);
                if (st.TryGetProperty("kraken", out var kr)) _krakenRules = KrakenSettings.FromJson(kr);
            }
            /* LE VENT : un facteur de poussée, 1 étant la physique. Borné — un zéro
               ou un négatif ferait pousser les voiles à rebours, et au-delà de huit
               la gîte ne laisse plus rien à jouer. */
            if (root.TryGetProperty("wind", out var wi) && wi.Opt("gain") is double wg)
                Config.WindGain = Math.Clamp(wg, 0.1, 8);
            if (root.TryGetProperty("wind", out var wi2) && wi2.Opt("heel") is double wh2)
                Config.WindHeel = Math.Clamp(wh2, 0, 8);
            // le virement de bord de l'équipage (ShipPhysics.Tack.cs) : allumé sauf si la fiche dit non
            Config.CrewTacks = !(root.TryGetProperty("wind", out var wi3) && wi3.TryGetProperty("virement", out var wv3) && wv3.ValueKind == System.Text.Json.JsonValueKind.False);
            ReadTraffic(root);
            if (root.TryGetProperty("cloche", out var cl))
            {
                if (cl.Opt("air") is double cla)
                    _bellAirMax = Math.Clamp(cla, 30, 7200);
                if (cl.Opt("cable") is double clc)
                    _bellCable = Math.Clamp(clc, 5, 200);
            }
            if (root.TryGetProperty("flyby", out var fb))
            {
                // bornés : un drone arrêté ou un plan de zéro seconde ne filment plus rien
                if (fb.Opt("vitesse") is double fv)
                    _flySpeed = Math.Clamp(fv, 0.1, 10);
                if (fb.Opt("plan") is double fp)
                    _flyHold = Math.Clamp(fp, 1, 120);
                if (fb.TryGetProperty("bataille", out var fbb))
                    _flyBattleOn = fbb.ValueKind != System.Text.Json.JsonValueKind.False;
            }
            if (root.TryGetProperty("mouillage", out var mo2))
            {
                if (mo2.TryGetProperty("enabled", out var me)) _mooredOn = me.GetBoolean();
                if (mo2.TryGetProperty("portee", out var mp)) _mooredRange = mp.GetDouble();
                if (mo2.TryGetProperty("navires", out var mn) && mn.ValueKind == System.Text.Json.JsonValueKind.Array)
                {
                    var l = new List<string>();
                    foreach (var e in mn.EnumerateArray()) if (e.GetString() is { } q) l.Add(q);
                    if (l.Count > 0) _mooredShips = l.ToArray();
                }
            }
            if (root.TryGetProperty("climate", out var k))
                _climate = new Climate(ClimateSettings.FromJson(k));
        }
        catch (Exception ex) { GD.PushWarning($"settings.json illisible ({ex.Message}) : climat par défaut"); }
    }

    /// <summary>
    /// Une image de ce qui tombe. Le climat avance en heures de JEU — l'horloge du
    /// jour, pas la montre : presser le temps amène autant d'averses par jour —, et
    /// minuit passé tourne la page du calendrier. La pluie d'un coup de vent (à
    /// partir de force 5,5) et l'averse se composent ; au froid, c'est de la neige.
    /// </summary>
    void FallTick(double dt, double dayBefore)
    {
        double dayAfter = _sky.Core.DayTime;
        if (dayAfter < dayBefore) _calendar.NextDay();          // minuit passé
        double hours = (dayAfter - dayBefore + 24) % 24;
        _climate.Update(hours, _calendar, dayAfter, _sky.Core.Storm);
        FogTick(hours, dayAfter);

        double wet = Math.Clamp((_sea.Core.SeaState - 5.5) / 2.8, 0, 1);
        _fall = _climate.Precipitation(wet);

        // ce qui tombe réfléchit : la clarté de l'horizon, qui porte l'heure
        var h = _sky.Core.Horizon;
        double light = Math.Min(1, (h.R + h.G + h.B) / 2.3);
        var w = _sea.Core.WindVec;
        /* IL NE NEIGE PAS DANS LA CHAMBRE DU CAPITAINE (signalé). Le rideau de ce
           qui tombe est replié autour de l'ŒIL dans son shader — c'est ce qui le
           rend gratuit —, si bien qu'il suit la caméra partout, y compris sous un
           pont. Une vue que la fiche dit « closed » a un toit : rien n'y tombe.

           On se sert de la MÊME valeur qui ferme le son (_indoors), et non d'un
           second test : une seule notion d'« être enfermé », deux usagers, et le
           jour où une fiche déclarera une soute, la neige le saura sans qu'on y
           touche. Elle glisse en un huitième de seconde, donc le ciel ne s'éteint
           pas d'un coup au changement de vue.

           Ce qui TIENT sur les ponts n'est pas touché : ce manteau-là est dehors,
           et c'est ce qu'on voit par les fenêtres de poupe. */
        double dehors = 1 - _indoors;
        /* LA BRUME RASANTE monte avec celle de l'air, parce que c'est le même
           matin : l'une ôte la vue, l'autre se voit. Sa part propre est dans les
           réglages, si bien qu'on peut avoir l'une sans l'autre. Elle ne rentre
           pas dans la chambre du capitaine — même raison que la neige. */
        _mist?.Step(_cam.GlobalPosition,
            (_mistForce >= 0 ? _mistForce : _sky.Core.Fog * _mistAmount) * dehors);
        _precip.Step(_cam.GlobalPosition, _t, w.ToGodot(),
            (_fall.Snow ? 0 : _fall.Amount) * dehors, (_fall.Snow ? _fall.Amount : 0) * dehors, light,
            GetViewport().GetVisibleRect().Size.Y);

        /* LA NEIGE TIENT SUR LES PONTS : un manteau qui s'épaissit en dix minutes
           sous une forte chute, plafonné à 0,85 — un manteau, pas une congère —,
           et qui fond d'autant plus vite qu'il fait doux. À chaque coque la
           sienne : un navire sorti de la neige la garde jusqu'à ce qu'elle fonde. */
        double degel = Math.Max(0, _climate.Temp - _climate.K.SnowBelow);
        Settle(_ship);
        foreach (var s in _others) Settle(s);
        void Settle(ShipNode s) =>
            s.SetSnowCover(_snowRules.Step(s.SnowCover, dt, _fall.Snow ? _fall.Amount : 0, degel));
    }

    void SetAutoWeather(bool on)
    {
        _weather.On = on;
        // l'allumer part de ce que montre la console : pas de mer téléportée à un autre jour
        if (on) _weather.Sync(_force, _windDeg);
        UpdateInfo();
    }

    /// <summary>
    /// LA MER OÙ ELLE EST VRAIMENT — trois choses la décident et se composent
    /// ICI : la console, qui fait le jour ; la météo, quand elle tourne seule ;
    /// et la dépression où elle se trouve, qui l'emporte sur les deux, une
    /// dépression ne négociant pas. UNE cible, que le spectre poursuit à vitesse
    /// bornée — ce qui compte surtout en entrant dans un grain, où un saut sans
    /// borne rebattrait toute la mer. Le vent, lui, est celui de l'instant : la
    /// toile sent la risée quand elle arrive, la houle met des minutes à suivre.
    /// </summary>
    void WeatherTick(double dt, double tNow)
    {
        _weather.Update(dt);
        double tgtF = _weather.On ? _weather.Force : _force;
        double tgtD = _weather.On ? _weather.Dir : _windDeg;
        string? master = _weather.On ? "météo auto" : null;

        // sa position VRAIE : les dépressions vivent en mètres monde, pas autour de l'origine flottante
        var b = _ship.Physics.Body;
        var o = _sea.Core.Origin;
        _inSquall = _storms.At(o.X + b.Pos.X, o.Z + b.Pos.Z, tNow, out _squall);
        if (_inSquall && _squall.Force > tgtF)
        {
            master = "dépression";
            tgtF = _squall.Force;
            /* le vent tourne autour du centre, par le plus court et à proportion de
               l'enfoncement : il refuse régulièrement à l'approche, et c'est ainsi
               qu'on trouve le milieu sans baromètre */
            double dd = (_squall.WindDeg - tgtD + 540) % 360 - 180;
            tgtD += dd * _squall.Inten;
        }

        // laissée à elle-même, la console EST la mer
        if (!_weather.On && !_inSquall) { _lagForce = tgtF; _lagDir = tgtD; }
        if (_weather.ChaseSea(ref _lagForce, ref _lagDir, dt, tgtF, tgtD))
        {
            _sea.Core.Time = tNow;          // la correction de bande se compte contre l'horloge
            _sea.Core.SetSeaState(_lagForce, _lagDir);
        }
        // le dernier mot est à la risée : la toile la sent, la houle suit plus tard
        _sea.Core.SetWind(tgtF, tgtD);
        _windNowDeg = (tgtD % 360 + 360) % 360;

        /* LA CONSOLE SUIT CE QUI SOUFFLE tant que la météo ou le grain décident,
           comme les curseurs de la page : sans quoi, à la sortie d'un grain, la
           mer retomberait d'un coup à ce qu'on avait réglé avant d'y entrer. */
        if (_weather.On || _inSquall) { _force = _lagForce; _windDeg = _windNowDeg; }
        _seaMaster = master;
        if (_windLog > 0 && Math.Floor(tNow / 2) != Math.Floor((tNow - dt) / 2)) GD.Print(FormattableString.Invariant($"[vent] t {tNow:F0}  {_windNowDeg:F0}°  force {tgtF:F1}  maître {master ?? "console"}  grain {_inSquall}  centre {(_inSquall ? _squall.Dist : -1):F0} m  intensité {(_inSquall ? _squall.Inten : 0):F2}  météo {_weather.On} {_weather.Dir:F0}° {_weather.Force:F1}  cap {(Math.Atan2(-_ship.Physics.Body.Quat.Rotate(new Vec3d(0, 0, 1)).X, _ship.Physics.Body.Quat.Rotate(new Vec3d(0, 0, 1)).Z) * 180 / Math.PI + 360) % 360:F0}°  amure {_ship.Physics.Tack}  faseye {_ship.Physics.Luffing}"));

        // le quart de ciel noir vers le centre, et ses éclairs
        _sky.SetSquall(_inSquall ? new Vector2((float)_squall.ToX, (float)_squall.ToZ) : Vector2.Zero,
                       _inSquall ? _squall.Loom : 0);
    }

    /// <summary>
    /// EMMÈNE-MOI DANS LE GROS TEMPS — tempete() de la page : la dépression la plus
    /// proche, et la coque posée à <paramref name="fraction"/> de son rayon (0 : au
    /// centre). Le transport déplace l'ORIGINE — la coque reste où elle est, près
    /// de zéro, et le monde glisse sous elle —, et elle arrive droite et sans erre.
    ///
    /// UN CENTRE DE DÉPRESSION N'EST PAS FORCÉMENT DE L'EAU : cette démo l'avait
    /// oublié quand elle n'avait pas de terre, et depuis la Jamaïque la coque
    /// arrivait au milieu de l'île. Comme la page : on CHOISIT le grain sur la
    /// mer qu'il a sous lui (dans sa moitié intérieure, sinon on retient un grain
    /// dont la seule eau est au bord, c'est-à-dire du calme), puis on l'y pose.
    /// </summary>
    void GoToStorm(double fraction)
    {
        var b = _ship.Physics.Body;
        var o = _sea.Core.Origin;
        double x = o.X + b.Pos.X, z = o.Z + b.Pos.Z;
        double k0 = Math.Clamp(fraction, 0, 1);
        double a0 = _stormRng.NextDouble() * 2 * Math.PI;
        (double X, double Z)? WaterIn(Storm st, double lim)
        {
            if (_world == null) return (st.X + st.R * Math.Min(k0, 0.95), st.Z);
            for (int step = 0; step < 12; step++)
            {
                double d = Math.Min(st.R * lim, k0 * st.R + step * Math.Max(120, st.R * 0.06));
                for (int n = 0; n < 24; n++)
                {
                    double ang = a0 + n * 2 * Math.PI / 24;
                    double wx = st.X + Math.Cos(ang) * d, wz = st.Z + Math.Sin(ang) * d;
                    // vingt-cinq mètres sous la quille et pas une côte à six cents
                    if (_world.HeightAt(wx, wz) < -25 && _world.ShoreDistance(wx, wz) > 600) return (wx, wz);
                }
            }
            return null;
        }
        if (!_storms.Nearest(x, z, _t, 12, st => WaterIn(st, 0.55) != null, out var s, out double dist)
            || WaterIn(s, 0.95) is not { } at)
        {
            Say("Pas une dépression à portée qui ait de la mer sous elle");
            return;
        }
        double tx = at.X, tz = at.Z;
        _anchor2?.Weigh(_ship);
        JumpBy(tx - x, tz - z);
        _storms.At(tx, tz, _t, out var q);
        GD.Print(FormattableString.Invariant(
            $"dépression à {dist / Config.Mile:F1} milles (rayon {s.R:F0} m, force {s.Peak:F1} au cœur) — force {q.Force:F1} ici, fond {(_world == null ? double.NaN : -_world.HeightAt(tx, tz)):F0} m"));
        UpdateInfo();
    }
}
