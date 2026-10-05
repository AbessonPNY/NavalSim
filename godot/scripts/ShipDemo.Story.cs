using Godot;
using System;
using System.Linq;
using NavalSim.Core;

namespace NavalSim;

/// <summary>
/// L'HISTOIRE EN CHAPITRES — leur cinématique, leurs objectifs sous Tab, et la
/// question qui les clôt.
///
/// LA CINÉMATIQUE (quests/histoire.json → « cinematique ») se joue en trois temps,
/// le jeu en mode cinéma, sans instruments :
///  1. LE NAUFRAGE : de nuit, par gros temps, le navire nommé (la Roter Löwe)
///     sombre — la foudre sur sa mâture, puis la mer qui entre. Quand il descend,
///     ses pièces s'en vont en voltigeant (la nuée du registre des épaves) et une
///     SPHÈRE DE LUMIÈRE de trente centimètres descend au milieu d'elles ; la
///     caméra passe sous l'eau et la suit.
///  2. L'ÉTABLISSEMENT : au matin, un plan d'ensemble sur le port, la caméra en
///     arc de cercle au-dessus de la ville et de sa rade, qui vit.
///  3. En fondu, le ponton de départ, et la fenêtre du chapitre.
/// Échap, Entrée ou Espace la passent.
///
/// LE NAVIRE QUI SOMBRE EST LE NAVIRE MENÉ PENDANT LE FILM, comme le galion de
/// l'affiche au titre : la mer, l'écume, la caméra et le registre des épaves le
/// suivent sans rien leur apprendre. Son épave reste au fond, inscrite — elle
/// appartient à l'histoire. Le navire du chapitre lui succède à l'aube.
/// </summary>
public partial class ShipDemo
{
    sealed class Film
    {
        public QuestSpec Q = null!;
        public CinematicSpec C = null!;
        /// <summary>0 le naufrage, 1 l'établissement, 2 fini.</summary>
        public int Phase;
        public double T, PhaseT, NextBolt, OrbT = -1;
        /// <summary>L'indice de la fiche du chapitre, rendue à l'aube.</summary>
        public int Ship = -1;
        public bool Breached;
        public Node3D? Orb;
        public Vector3 OrbAt;
        public double OrbBed;
        public Vector3 EyeVel;
    }

    Film? _film;
    QuestSpec? _filmLater;
    /// <summary>--sans-film : un chapitre commence sans sa cinématique, pour l'essai.</summary>
    bool _noFilm;
    CanvasLayer? _filmLayer;
    ColorRect? _filmFade;
    QuestSpec? _pendingEnd;

    /// <summary>Le noir du film : 0 transparent, 1 noir.</summary>
    double _filmBlack;

    /* ------------------------------------------------------------------ */
    /*  LANCER                                                             */
    /* ------------------------------------------------------------------ */

    /// <summary>
    /// UN CHAPITRE QUI A SA CINÉMATIQUE : la partie est déjà remise à neuf
    /// (StartQuest → Home), le navire du chapitre est retenu ; on joue le film, et
    /// la quête ne commence qu'à sa fin.
    /// </summary>
    void BeginFilm(QuestSpec q)
    {
        /* PAS AVANT QUE LE JEU SOIT MONTÉ : lancé par la ligne de commande (--quete),
           le film commençait pendant le démarrage, qui remettait ensuite le navire
           mené à son ponton — la Roter Löwe sombrait à Port-Royal (mesuré). */
        if (!_booted) { _filmLater = q; return; }
        var c = q.Cinematic!;
        _film = new Film { Q = q, C = c, Ship = _index };
        _inTitle = false;
        if (_titleLayer != null) _titleLayer.Visible = false;
        CineHide();
        _note.Visible = false;
        EnsureFilmLayer();
        if (c.Wreck is { } w) StartWreck(w);
        else StartEstablish();
        ApplySettings();
    }

    void EnsureFilmLayer()
    {
        if (_filmLayer != null) return;
        _filmLayer = new CanvasLayer { Layer = 60 };
        AddChild(_filmLayer);
        _filmFade = new ColorRect { Color = new Color(0, 0, 0, 0), MouseFilter = Control.MouseFilterEnum.Ignore };
        _filmFade.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _filmLayer.AddChild(_filmFade);
    }

    /// <summary>Le navire nommé par le film : un numéro de ships/index.json, ou le nom d'une fiche.</summary>
    int FilmShipIndex(string ship)
    {
        if (int.TryParse(ship, out int n) && n >= 0 && n < _paths.Count) return n;
        int i = _paths.FindIndex(p => System.IO.Path.GetFileNameWithoutExtension(p) == ship);
        return i >= 0 ? i : _paths.FindIndex(p => System.IO.Path.GetFileNameWithoutExtension(p) == TitleShip);
    }

    void StartWreck(WreckShot w)
    {
        var f = _film!;
        f.Phase = 0; f.PhaseT = 0; f.NextBolt = 3;
        int idx = FilmShipIndex(w.Ship);
        if (idx >= 0 && idx != _index) Launch(idx);
        var (x, z) = _world!.Geo.ToXZ(w.Lat, w.Lon);
        RecentreOn(x, z);
        var b = _ship.Physics.Body;
        b.Pos = new Vec3d(0, b.Pos.Y, 0);
        // le travers au vent : c'est ainsi qu'une mer de cette force la roule
        double hd = (_windDeg + 90) * Math.PI / 180;
        b.Quat = Quatd.FromAxisAngle(new Vec3d(0, 1, 0), -hd);
        b.Vel = Vec3d.Zero; b.AngVel = Vec3d.Zero;
        _ship.Ctrl.SailsSet = true;
        _ship.Ctrl.Throttle = 0;
        _ship.Ctrl.Rudder = 0.2;
        _ship.LanternsOrdered = true;      // un galion dans la nuit porte ses feux
        _ship.SyncTransform();
        _sky.Core.SetTimeOfDay(w.Hour, _sky.Latitude);
        _sky.Apply();
        _force = w.Force;
        _cloud = 1;
        Restate();
        var o0 = _sea.Core.Origin;
        GD.Print(FormattableString.Invariant($"[film] naufrage : {_ship.Spec.Name}, force {w.Force}, {w.Hour:F1} h, en ({o0.X + b.Pos.X:F0}, {o0.Z + b.Pos.Z:F0}) par {-_world.HeightAt(o0.X + b.Pos.X, o0.Z + b.Pos.Z):F0} m (voulu ({x:F0}, {z:F0}))"));
    }

    void StartEstablish()
    {
        var f = _film!;
        f.Phase = 1; f.PhaseT = 0;
        RemoveOrb();
        /* LE NAVIRE DU CHAPITRE succède au navire du film, à son ponton, et
           l'épave reste au fond : elle n'est plus « la sienne » (R ne la
           renflouerait pas). */
        if (f.Ship >= 0 && f.Ship != _index) Launch(f.Ship);
        _playerWreck = null;
        Found();
        _ship.Physics.Salvage();
        _ship.Ctrl.SailsSet = false;
        _ship.Ctrl.Throttle = 0;
        _ship.Ctrl.Rudder = 0;
        _ship.LanternsOrdered = false;
        var e = f.C.Establish;
        _sky.Core.SetTimeOfDay(e?.Hour ?? 7, _sky.Latitude);
        _sky.Apply();
        _force = 4; _windDeg = 105; _cloud = 0.2;
        Restate();
        Moor();
        // la rade se repeuple pour le plan : on y voit des voiles aller et venir
        TrafficReset();
        // l'orage est fini : l'objectif est sec
        DryLens();
        if (e == null) { f.Phase = 2; return; }
        GD.Print(FormattableString.Invariant($"[film] établissement : {e.Port}, {e.Hour:F1} h"));
    }

    /* ------------------------------------------------------------------ */
    /*  À CHAQUE IMAGE                                                     */
    /* ------------------------------------------------------------------ */

    void FilmTick(double dt)
    {
        var f = _film!;
        f.T += dt; f.PhaseT += dt;
        if (f.Phase == 0) WreckTick(f, dt);
        else if (f.Phase == 1) EstablishTick(f, dt);
        if (f.Phase == 2) { EndFilm(); return; }
        if (_filmFade != null) _filmFade.Color = new Color(0, 0, 0, (float)Math.Clamp(_filmBlack, 0, 1));
    }

    void WreckTick(Film f, double dt)
    {
        var w = f.C.Wreck!;
        var s = _ship;
        double t = f.PhaseT;
        // l'image monte du noir en deux secondes
        _filmBlack = Math.Max(0, 1 - t / 2);

        /* LA FOUDRE sur sa mâture, toutes les trois ou quatre secondes, tant
           qu'elle est debout : c'est elle qui éclaire la scène de nuit. */
        if (t > f.NextBolt && f.OrbT < 0 && !s.Physics.Foundered)
        {
            StrikeSomewhere(s);
            f.NextBolt = t + 2.5 + _stormRng.NextDouble() * 2.5;
        }
        /* LA MER ENTRE : la coque s'ouvre au quinzième de la durée, assez grand
           pour qu'elle descende en une demi-minute — l'arithmétique de
           l'envahissement fait le reste. */
        if (!f.Breached && t > 14)
        {
            f.Breached = true;
            var p = s.Physics;
            for (int i = 0; i < p.Comps.Length; i++) p.MakeBreach(i, 1.4, 0.08, i % 2 == 0 ? 1 : -1);
            p.PumpOn = false;
            GD.Print(FormattableString.Invariant($"[film] la mer entre, à {t:F1} s"));
        }

        var P = s.GlobalPosition;
        var fw = s.GlobalTransform.Basis.Z; fw.Y = 0; fw = fw.Normalized();
        var side = new Vector3(fw.Z, 0, -fw.X);
        Vector3 eye, look;
        /* ELLE NE COULE PAS D'ELLE-MÊME : pleine aux quatre cinquièmes, ses châteaux
           et ses hauts la tiennent à fleur d'eau (mesuré : 98 % d'eau, 9 % de coque
           hors de l'eau, jamais « sombrée »). C'est vrai d'une coque de bois noyée —
           mais elle porte ses pièces, son lest et ses ancres : on les lui rend, un
           tiers de sa masse, et elle descend. */
        double water = 0, room = 0;
        foreach (var c in s.Physics.Comps) { water += c.Vol; room += c.Cap; }
        if (f.OrbT < 0 && !s.Physics.Foundered && f.Breached && (water > 0.8 * room || t > w.Duration))
        {
            s.Physics.LoadCargo(Config.NComp / 2, HoldFloor, 0, s.Spec.MassKg / 1000 * 0.35, "lest");
            s.Physics.Foundered = true;
        }
        if (f.OrbT < 0 && s.Physics.Foundered)
        {
            /* ELLE DESCEND : la sphère paraît au milieu de la nuée de pièces que
               le naufrage vient de lâcher (FlotsamNode → CoinNode), et descend
               avec elles. */
            f.OrbT = t;
            SpawnOrb(P + new Vector3(0, -0.5f, 0));
            var o1 = _sea.Core.Origin; var b1 = s.Physics.Body.Pos;
            GD.Print(FormattableString.Invariant($"[film] elle sombre à {t:F1} s : la sphère descend, en ({o1.X + b1.X:F0}, {o1.Z + b1.Z:F0}) par {-_world!.HeightAt(o1.X + b1.X, o1.Z + b1.Z):F0} m"));
        }
        if (f.OrbT >= 0)
        {
            double u = t - f.OrbT;
            SinkOrb(f, dt);
            // sous l'eau, tout près, et tournant lentement autour d'elle
            double a = 0.6 + u * 0.12;
            eye = f.OrbAt + new Vector3((float)Math.Cos(a) * 4.6f, -0.6f, (float)Math.Sin(a) * 4.6f);
            look = f.OrbAt;
            if (u > 16) _filmBlack = Math.Min(1, (u - 16) / 1.5);
            if (u > 17.6) StartEstablish();
        }
        else if (t < 14)
        {
            // de loin, trois quarts arrière, au-dessus des lames
            eye = P - fw * (75 - (float)t * 1.5f) + side * 55 + new Vector3(0, 24, 0);
            look = P + new Vector3(0, 8, 0);
        }
        else
        {
            // par le travers, au ras de l'eau : on la voit s'enfoncer
            eye = P + side * 44 + fw * 10 + new Vector3(0, 7, 0);
            look = P + new Vector3(0, 2, 0);
        }
        Aim(f, eye, look, dt, cut: f.OrbT >= 0 && t - f.OrbT < dt * 1.5);
    }

    void EstablishTick(Film f, double dt)
    {
        var e = f.C.Establish!;
        double t = f.PhaseT, d = Math.Max(4, e.Duration);
        // du noir en une seconde et demie, au noir à la fin
        _filmBlack = t < 1.5 ? 1 - t / 1.5 : t > d - 1.5 ? (t - (d - 1.5)) / 1.5 : 0;
        if (t > d) { f.Phase = 2; return; }
        var isl = _world!.ByKey(e.Port) ?? _world.StartPort;
        if (isl == null) { f.Phase = 2; return; }
        var o = _sea.Core.Origin;
        var C = new Vector3((float)(isl.X - o.X), 0, (float)(isl.Z - o.Z));
        /* UN ARC AU-DESSUS DE LA VILLE, qui part du large (le côté de la passe) et
           tourne de « balayage » degrés en descendant d'un tiers : la ville se
           découvre, la rade derrière elle. */
        double u = MathX.Smooth01(t / d);
        double a = isl.Port.Ang - e.Sweep * Math.PI / 360 + u * e.Sweep * Math.PI / 180;
        float h = (float)(e.Height * (1 - 0.35 * u));
        var eye = C + new Vector3((float)(Math.Cos(a) * e.Radius), h, (float)(Math.Sin(a) * e.Radius));
        var look = C + new Vector3(0, 6, 0);
        Aim(f, eye, look, dt, cut: t < dt * 1.5);
    }

    /// <summary>La caméra du film : posée, et menée en douceur d'un point à l'autre — une coupe la pose net.</summary>
    void Aim(Film f, Vector3 eye, Vector3 look, double dt, bool cut)
    {
        if (cut || _fixEye is not Vector3 cur) { _fixEye = eye; _fixLook = look; return; }
        float k = (float)Math.Min(1, dt * 2.2);
        _fixEye = cur.Lerp(eye, k);
        _fixLook = (_fixLook ?? look).Lerp(look, k);
    }

    /* ------------------------------------------------------------------ */
    /*  LA SPHÈRE                                                          */
    /* ------------------------------------------------------------------ */

    void SpawnOrb(Vector3 at)
    {
        var f = _film!;
        RemoveOrb();
        var orb = new Node3D { Name = "sphere_du_film" };
        /* TRENTE CENTIMÈTRES DE DIAMÈTRE, qui émettent : c'est une lumière, elle ne
           suit pas celle du ciel (ce qui émet ne suit pas la lumière). Une lampe
           courte autour d'elle allume les pièces qui passent. */
        orb.AddChild(new MeshInstance3D
        {
            Mesh = new SphereMesh { Radius = 0.15f, Height = 0.3f, RadialSegments = 24, Rings = 12 },
            MaterialOverride = new StandardMaterial3D
            {
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                AlbedoColor = new Color(0.78f, 0.90f, 1.0f),
                EmissionEnabled = true, Emission = new Color(0.62f, 0.82f, 1.0f), EmissionEnergyMultiplier = 6
            }
        });
        orb.AddChild(new OmniLight3D
        {
            LightColor = new Color(0.55f, 0.75f, 1.0f), LightEnergy = 3.5f, OmniRange = 7, ShadowEnabled = false
        });
        AddChild(orb);
        f.Orb = orb;
        f.OrbAt = at;
        var o = _sea.Core.Origin;
        f.OrbBed = _world!.HeightAt(o.X + at.X, o.Z + at.Z) + 0.15;
        orb.Position = at;
    }

    /* ELLE DESCEND AVEC LES PIÈCES, droit : une sphère n'a pas de face pour
       voltiger, elle oscille à peine. */
    void SinkOrb(Film f, double dt)
    {
        if (f.Orb == null) return;
        var a = f.OrbAt;
        // au pas des pièces (CoinNode : 0,20 à 0,45 m/s), pour rester au milieu de la nuée
        a.Y = (float)Math.Max(f.OrbBed, a.Y - 0.32 * dt);
        f.OrbAt = a;
        double w = f.T * 1.3;
        f.Orb.Position = a + new Vector3((float)Math.Sin(w) * 0.12f, 0, (float)Math.Cos(w * 0.8) * 0.12f);
    }

    void RemoveOrb()
    {
        if (_film?.Orb is { } o && IsInstanceValid(o)) { RemoveChild(o); o.QueueFree(); }
        if (_film != null) _film.Orb = null;
    }

    /* ------------------------------------------------------------------ */
    /*  FINIR, OU PASSER                                                   */
    /* ------------------------------------------------------------------ */

    void EndFilm()
    {
        var f = _film!;
        RemoveOrb();
        _film = null;
        _filmBlack = 0;
        if (_filmFade != null) _filmFade.Color = new Color(0, 0, 0, 0);
        if (!_planted) { _fixEye = null; _fixLook = null; }
        CineShow();
        Play();
        _quests?.Start(f.Q.Id);
        SaveQuests();
        GD.Print($"[film] fin : {f.Q.Title}");
    }

    /// <summary>Passer le film : l'aube à Port-Royal, tout de suite.</summary>
    void SkipFilm()
    {
        if (_film == null) return;
        if (_film.Phase == 0) StartEstablish();
        _film.Phase = 2;
        EndFilm();
    }

    /// <summary>Le film prend toutes les touches ; Échap, Entrée et Espace le passent.</summary>
    bool FilmInput(InputEvent e)
    {
        if (_film == null) return false;
        if (e is InputEventKey k && k.Pressed && !k.Echo
            && (k.PhysicalKeycode != Key.None ? k.PhysicalKeycode : k.Keycode) is Key.Escape or Key.Enter or Key.KpEnter or Key.Space)
            SkipFilm();
        return e is InputEventKey or InputEventMouseButton;
    }

    /* ------------------------------------------------------------------ */
    /*  LA QUESTION DE FIN                                                 */
    /* ------------------------------------------------------------------ */

    PanelContainer? _endBox;
    CanvasLayer? _questLayer;

    /// <summary>
    /// LA MISSION EST FINIE : passer au chapitre suivant, ou continuer à jouer
    /// librement — le monde reste tel qu'on l'a laissé, la bourse et le navire
    /// avec.
    /// </summary>
    void AskNext(QuestSpec q)
    {
        if (q.Kind != "story" || _questLayer == null || _quests == null) return;
        var next = _quests.List.Where(x => x.Kind == "story" && x.Chapter > q.Chapter).OrderBy(x => x.Chapter).FirstOrDefault();
        _endBox?.QueueFree();
        var centre = new CenterContainer { AnchorRight = 1, AnchorBottom = 1, MouseFilter = Control.MouseFilterEnum.Ignore };
        _questLayer.AddChild(centre);
        _endBox = new PanelContainer { CustomMinimumSize = new Vector2(640, 0) };
        _endBox.AddThemeStyleboxOverride("panel", NoticeStyle());
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 14);
        var title = new Label { Text = "Mission accomplie", HorizontalAlignment = HorizontalAlignment.Center };
        title.AddThemeFontSizeOverride("font_size", 21);
        title.AddThemeColorOverride("font_color", new Color(0.92f, 0.78f, 0.36f));
        var text = new Label
        {
            Text = Keyed(q.End.Length > 0 ? q.End : "Votre mission est terminée. Vous pouvez passer au chapitre suivant, ou continuer à jouer librement."),
            AutowrapMode = TextServer.AutowrapMode.WordSmart, HorizontalAlignment = HorizontalAlignment.Center
        };
        text.AddThemeFontSizeOverride("font_size", 16);
        box.AddChild(title); box.AddChild(text);
        var row = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        row.AddThemeConstantOverride("separation", 18);
        void Close() { centre.QueueFree(); _endBox = null; }
        if (next != null)
        {
            var go = new Button { Text = $"Chapitre suivant : {next.Title}" };
            go.Pressed += () => { Close(); StartQuest(next.Id); };
            row.AddChild(go);
        }
        else
        {
            var later = new Label { Text = "La suite de l'histoire est à venir." };
            later.AddThemeColorOverride("font_color", new Color(0.7f, 0.68f, 0.62f));
            box.AddChild(later);
        }
        var free = new Button { Text = "Continuer librement" };
        free.Pressed += () => { Close(); SaveQuests(); Say("Le monde est à vous."); };
        row.AddChild(free);
        box.AddChild(row);
        _endBox.AddChild(box);
        centre.AddChild(_endBox);
        free.GrabFocus();
    }

    StyleBoxFlat NoticeStyle() => new()
    {
        BgColor = new Color(0.05f, 0.06f, 0.08f, 0.93f),
        BorderColor = new Color(0.55f, 0.44f, 0.20f),
        BorderWidthLeft = 1, BorderWidthRight = 1, BorderWidthTop = 1, BorderWidthBottom = 1,
        CornerRadiusTopLeft = 6, CornerRadiusTopRight = 6, CornerRadiusBottomLeft = 6, CornerRadiusBottomRight = 6,
        ContentMarginLeft = 26, ContentMarginRight = 26, ContentMarginTop = 20, ContentMarginBottom = 20
    };

    /* ------------------------------------------------------------------ */
    /*  LES OBJECTIFS, SOUS TAB                                            */
    /* ------------------------------------------------------------------ */

    PanelContainer? _goalsBox;
    Label? _goalsText;
    double _goalsAcc;

    /// <summary>Tab : ce qu'il reste à faire, à tout moment ; Tab le referme.</summary>
    void ToggleObjectives()
    {
        if (_questLayer == null) return;
        if (_goalsBox == null)
        {
            // sous les instruments de gauche, qu'il ne recouvre pas
            _goalsBox = new PanelContainer { Position = new Vector2(18, 390), CustomMinimumSize = new Vector2(460, 0), Visible = false };
            _goalsBox.AddThemeStyleboxOverride("panel", NoticeStyle());
            _goalsText = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(420, 0) };
            _goalsText.AddThemeFontSizeOverride("font_size", 15);
            _goalsText.AddThemeColorOverride("font_color", new Color(0.93f, 0.92f, 0.88f));
            _goalsBox.AddChild(_goalsText);
            _questLayer.AddChild(_goalsBox);
        }
        _goalsBox.Visible = !_goalsBox.Visible;
        if (_goalsBox.Visible) FillObjectives();
    }

    bool _openGoals;
    /// <summary>--fin-chapitre : la question de fin, ouverte tout de suite, pour l'essai.</summary>
    bool _askEndTest;

    void ObjectivesTick(double dt)
    {
        if (_openGoals && _questLayer != null && !_inTitle && _film == null) { _openGoals = false; ToggleObjectives(); }
        if (_askEndTest && _quests?.Active is { } aq && !_inTitle && _film == null) { _askEndTest = false; _msgs.Clear(); NextMessage(); AskNext(aq); }
        if (_goalsBox is not { Visible: true }) return;
        if ((_goalsAcc += dt) < 0.25) return;
        _goalsAcc = 0;
        FillObjectives();
    }

    void FillObjectives()
    {
        if (_goalsText == null) return;
        var q = _quests?.Active;
        if (q == null) { _goalsText.Text = "Aucun objectif — vous naviguez librement.\n\n(Tab pour fermer)"; return; }
        var sb = new System.Text.StringBuilder();
        sb.Append(q.Title).Append("\n\n");
        for (int i = 0; i < q.Steps.Count; i++)
        {
            var s = q.Steps[i];
            string name = s.Title.Length > 0 ? s.Title : $"Étape {i + 1}";
            if (i < _quests!.Step) sb.Append("✓  ").Append(name).Append('\n');
            else if (i == _quests.Step)
            {
                string count = CountedOf(s, _quests.Counted);
                sb.Append("▸  ").Append(name).Append(count.Length > 0 ? " — " + count : "").Append(Deadline(s)).Append('\n');
            }
            else sb.Append("·  ").Append(name).Append('\n');
        }
        if (_quests!.Current is QuestStep cur && cur.Brief.Length > 0)
            sb.Append('\n').Append(Keyed(cur.Brief));
        sb.Append("\n\n(Tab pour fermer)");
        _goalsText.Text = sb.ToString();
    }
}
