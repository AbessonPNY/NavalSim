using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using NavalSim.Core;

namespace NavalSim;

/// <summary>Le clavier, et la ligne d'information (sorti de ShipDemo.cs).</summary>
public partial class ShipDemo
{
    /// <summary>
    /// La barre, la machine et les écoutes. Ce sont des touches TENUES, donc
    /// lues ici et non dans les événements : une commande de barre qui compte les
    /// événements de répétition du clavier dépendrait de la vitesse de répétition
    /// du système.
    /// </summary>
    void ReadKeys(double dt)
    {
        // à l'eau : les touches mènent le nageur, la barre est lâchée (ShipDemo.Swim.cs)
        if (_swimming) { SwimKeys(dt); return; }
        // à l'instrument, l'homme de barre tient le cap : la barre ne répond plus
        if (_sighting) return;
        // la cloche à l'eau : le navire est stoppé, les mêmes touches mènent la cloche
        if (_bellOut) { BellKeys(dt); return; }
        var c = _ship.Ctrl;

        double rud = 0;
        if (Physical(Key.A)) rud -= 1;
        if (Physical(Key.D)) rud += 1;
        // la barre revient d'elle-même quand on la lâche, comme une roue qu'on rend
        c.Rudder = rud != 0
            ? Math.Clamp(c.Rudder + rud * dt * 1.6, -1, 1)
            : _heldRudder ?? c.Rudder * (1 - Math.Min(1, dt * 2.5));

        if (Physical(Key.W)) c.Throttle = Math.Min(1, c.Throttle + dt * 0.8);
        if (Physical(Key.S)) c.Throttle = Math.Max(-1, c.Throttle - dt * 0.8);

        /* LES DEUX BANCS D'AVIRONS, tirés des MÊMES touches. Une embarcation n'a
           pas de machine ni de barre : W et S sont les nageurs qui nagent ou qui
           scient, A et D font nager d'un bord et scier de l'autre, et elle pivote
           sur place — ce qu'aucune règle n'a besoin de dire, le bras de levier
           s'en charge. Ainsi la chaloupe se conduit aux mêmes touches que le
           navire, sans qu'on ait à réapprendre à bord d'un canot.

           Elle était INGOUVERNABLE : sa fiche donne au gouvernail une puissance
           nulle — ce qui est juste, une chaloupe se gouverne à l'aviron — et les
           avirons n'étaient pas portés (signalé). */
        if (_ship.Spec.Oars != null)
        {
            c.OarL = Math.Clamp(c.Throttle + c.Rudder, -1, 1);
            c.OarR = Math.Clamp(c.Throttle - c.Rudder, -1, 1);
        }

        /* ⇧E TENUE : LA BÔME À CONTRE, poussée du bord où elle pend déjà — le geste
           qu'on fait d'instinct, vent debout (ShipPhysics.BackSail). L'angle dessiné
           vaut −amure × écoute : elle pend à tribord (+1) quand l'amure vaut −1.
           (⇧Q, ⇧A, ⇧D sont pris : l'abordage d'essai, le saut, la hache.) */
        bool shift = Input.IsKeyPressed(Key.Shift);
        c.Backed = shift && Physical(Key.E) ? (_ship.Physics.Tack < 0 ? 1 : -1) : 0;
        if (Physical(Key.Q) && !shift) c.Sheet = Math.Max(0, c.Sheet - dt * 0.8);
        if (Physical(Key.E) && !shift) c.Sheet = Math.Min(_ship.Spec.MaxSheet, c.Sheet + dt * 0.8);
    }

    /// <summary>
    /// LA TOUCHE PHYSIQUE, ET NON SON ÉTIQUETTE — et l'avoir écrit autrement a
    /// rendu le navire incommandable sur un clavier français.
    ///
    /// <c>Input.IsKeyPressed</c> teste le code LOGIQUE, c'est-à-dire ce que la
    /// disposition inscrit sur la touche. Sur AZERTY la touche qui occupe la
    /// position du W de QWERTY est étiquetée Z, celle du A est étiquetée Q, et
    /// celle du Q est étiquetée A. Une commande écrite en W-A-S-D y devient donc
    /// muette sur trois de ses quatre touches — et pire que muette : la touche
    /// sous l'index gauche rend « Q », si bien qu'en cherchant la barre on borde
    /// les écoutes. Signalé à l'usage sous la forme « je n'arrive pas à la faire
    /// avancer », ce qui est exactement le symptôme.
    ///
    /// <c>IsPhysicalKeyPressed</c> désigne l'EMPLACEMENT, donc le même doigt sur
    /// la même touche quel que soit le pays. C'est le pendant exact du choix de
    /// <c>e.code</c> plutôt que <c>e.key</c> côté navigateur, que ce projet a
    /// déjà payé une fois pour la rangée des chiffres.
    /// </summary>
    static bool Physical(Key k) => Input.IsPhysicalKeyPressed(k);

    void UpdateInfo()
    {
        if (_ship == null) return;
        var p = _ship.Physics;
        var b = p.Body;
        var (heel, trim, hdg) = _ship.Attitude();

        double speedKn = b.Vel.LengthXZ * Config.MsToKn;
        int bf = Mathf.Clamp((int)Math.Round(_sea.Core.SeaState), 0, 9);

        /* LE FOND, qu'un marin regarde avant tout le reste près d'une côte : ce
           qui lui reste d'eau sous la quille, et ce qu'elle a dans le fond quand
           elle talonne. La page le dit dans son bandeau d'avarie ; ici, à sa
           place parmi les instruments. */
        string fond = "";
        if (_world != null)
        {
            var wo = _sea.Core.Origin;
            double bed = _world.HeightAt(wo.X + b.Pos.X, wo.Z + b.Pos.Z);
            fond = p.Aground > 0
                ? "fond       ÉCHOUÉE · " + p.Aground.ToString("F1") + " m dans le fond\n"
                : "fond       " + (-bed).ToString("F1").PadLeft(6) + " m    sous quille "
                  + (-bed - p.Draft).ToString("F1").PadLeft(5) + " m\n";
        }
        string voiles = p.SetFrac > 0.99 ? "établies"
                      : p.SetFrac < 0.01 ? "ferlées"
                      : $"{p.SetFrac * 100:F0} %";

        _info.Text =
            $"{_ship.Spec.Name}   ({_index + 1}/{_paths.Count})\n" +
            // la moyenne du moteur sur la dernière seconde, et le temps qu'elle vaut
            $"{Engine.GetFramesPerSecond(),4:F0} images/s   {1000.0 / Math.Max(1, Engine.GetFramesPerSecond()),5:F1} ms\n" +
            $"\n" +
            $"cap        {hdg,6:F0}°      vitesse   {speedKn,5:F1} nds\n" +
            $"gîte       {heel,6:F1}°      assiette  {trim,5:F1}°\n" +
            $"tirant     {p.Draft,6:F2} m    immersion {p.SubmergedFrac * 100,5:F1} %\n" +
            fond +
            $"déplacement{b.Mass / 1000,6:F0} t\n" +
            $"\n" +
            $"machine    {_ship.Ctrl.Throttle,6:F2}      barre     {_ship.Ctrl.Rudder,5:F2}\n" +
            $"écoutes    {_ship.Ctrl.Sheet,6:F2}      voiles    {voiles} · {ReefName()}\n" +
            (_ship.SnowCover > 0.01 ? $"neige sur le pont {_ship.SnowCover * 100:F0} %\n" : "") +
            $"\n" +
            /* CE QUI RESTE DE LA NOTICE : une ligne. Les deux qui couraient ici
               d'un bord à l'autre de l'image sont passées sous F1 — un instrument
               qu'on lit d'un coup d'œil ne peut pas être aussi le mode d'emploi.
               Ne restent que les deux états qu'on veut voir SANS ouvrir quoi que
               ce soit : la vue où l'on est, et si la météo se conduit seule. */
            $"F1 commandes      vue {CamName()}      ⇧H instruments";
    }

    CheckBox? _chkFullscreen;

    public override void _UnhandledInput(InputEvent e)
    {
        /* ALT+ENTRÉE, AVANT TOUT : le titre, la carte et le navire capturé prennent
           chacun les touches, et le plein écran doit se basculer de partout. */
        if (e is InputEventKey fk && fk.Pressed && !fk.Echo && fk.AltPressed && fk.Keycode is Key.Enter or Key.KpEnter)
        {
            _settings.Fullscreen = !_settings.Fullscreen;
            _chkFullscreen?.SetPressedNoSignal(_settings.Fullscreen);
            Changed();
            GetViewport().SetInputAsHandled();
            return;
        }
        if (!_booted) return;                       // rien à commander sous le rideau
        /* G : UN APPUI, une pièce ; TENU, la bordée entière — la répétition du clavier
           le dit, et un loquet empêche un long appui de lâcher bordée sur bordée.
           ⇧ : l'autre bord, une fois. */
        /* L ÉCRAN DE TITRE PREND TOUT : sans cela, la barre et les canons
           répondraient derrière lui. Les boutons, eux, sont des Control : ils ont
           déjà eu la souris avant qu on arrive ici. */
        if (_inTitle)
        {
            if (e is InputEventKey tk && tk.Pressed && !tk.Echo)
                TitleKey(tk.PhysicalKeycode != Key.None ? tk.PhysicalKeycode : tk.Keycode);
            GetViewport().SetInputAsHandled();
            return;
        }
        /* NAVIRE CAPTURÉ : la partie est finie et la console ne répond plus. Tout
           est pris, comme au titre, sinon le joueur barrerait une épave en feu et
           R la radouberait — ce qui rendrait la sentence sans objet. Il ne reste
           que ⏎, qui reprend, et Échap, qui rend le titre. */
        if (_captured)
        {
            if (e is InputEventKey ck && ck.Pressed && !ck.Echo)
            {
                Key ckey = ck.PhysicalKeycode != Key.None ? ck.PhysicalKeycode : ck.Keycode;
                if (ckey is Key.Enter or Key.KpEnter) ResumeSave();
                else if (ckey == Key.Escape) { _captured = false; Found(); Open(); MainItems(); }
            }
            GetViewport().SetInputAsHandled();
            return;
        }
        // à l'eau, le nageur prend ce qui le concerne et rien d'autre ne répond
        if (SwimInputEvent(e)) { GetViewport().SetInputAsHandled(); return; }
        if (e is InputEventKey gk && (gk.PhysicalKeycode != Key.None ? gk.PhysicalKeycode : gk.Keycode) == Key.G)
        {
            if (!gk.Pressed) _salvo = false;
            else if (!gk.Echo) Fire(gk.ShiftPressed, false);
            else if (!_salvo) { _salvo = true; Fire(gk.ShiftPressed, true); }
            GetViewport().SetInputAsHandled();
            return;
        }
        if (e is InputEventKey k && k.Pressed && !k.Echo)
        {
            /* Même raison que ci-dessus : l'emplacement, pas l'étiquette. Les
               flèches et Échap sont au même endroit partout, mais V, N et F ne le
               sont pas sur toutes les dispositions, et un seul jeu de règles vaut
               mieux que deux.

               Avec un REPLI sur le code logique quand l'emplacement est vide —
               ce n'est pas de la superstition : les événements fabriqués par un
               outil d'automatisation n'ont pas toujours de code physique, et le
               projet a déjà rencontré exactement ce cas côté navigateur. */
            Key key = k.PhysicalKeycode != Key.None ? k.PhysicalKeycode : k.Keycode;
            /* ⇧M, PAR SA LETTRE et non par son emplacement : sur un AZERTY le M est
               là où un QWERTY a son point-virgule, et l'emplacement « M » est la
               virgule — le panneau ne s'ouvrait pas. Les autres commandes tombent
               aux mêmes places sur les deux claviers. */
            if (k.ShiftPressed && k.Keycode == Key.M) { ToggleSeaPanel(); GetViewport().SetInputAsHandled(); return; }
            // M seul : mouiller, ou virer au cabestan — par sa LETTRE, même raison
            if (!k.ShiftPressed && k.Keycode == Key.M) { _anchor2?.Toggle(_ship, _t); GetViewport().SetInputAsHandled(); return; }
            switch (key)
            {
                // une main sur la console reprend la main à la météo, comme le curseur de la page
                case Key.Up: _weather.On = false; _force = Math.Min(9.9, _force + 0.5); Restate(); break;
                case Key.Down: _weather.On = false; _force = Math.Max(0, _force - 0.5); Restate(); break;
                case Key.Left: _weather.On = false; _windDeg = (_windDeg - 15 + 360) % 360; Restate(); break;
                case Key.Right: _weather.On = false; _windDeg = (_windDeg + 15) % 360; Restate(); break;
                // ⇧T : la brume de surface, tout de suite, pour trois heures de jeu
                case Key.T when k.ShiftPressed: (_seaFog ??= new SeaFog(_fogRules)).Force(3); Say("La brume monte sur l'eau"); break;
                case Key.T: SetAutoWeather(!_weather.On); break;
                // Espace : lancer les lignes, ferrer, relever (ShipDemo.Fishing.cs)
                case Key.Space: FishKey(); break;
                // ⇧J : une averse et le serpent de mer, qui ne vit que dans la pluie
                case Key.J when k.ShiftPressed: if (!BestiaireMuet()) SummonSerpent(); break;
                case Key.J: GoToStorm(0); break;
                // le radoub : mâts replantés, toile renverguée — pour recommencer un essai
                /* ⇧R : UNE RENCONTRE TOUT DE SUITE — R relève les avaries, ⇧R fait
                   venir la voile qu on aurait attendue douze minutes. */
                case Key.R when k.ShiftPressed: ForceEncounter(false); break;
                case Key.R: Salvage(); break;
                // le kraken, tout de suite contre elle — pour le voir sans attendre une minute au cœur d'un grain
                // ⇧K : la baleine, qui vient charger — pour la voir sans attendre au large
                case Key.K when k.ShiftPressed: if (!BestiaireMuet()) SummonWhale(); break;
                case Key.K:
                    if (BestiaireMuet()) break;
                    // il ne vit qu'au cœur des dépressions : hors d'elles, il replongerait à l'image suivante
                    if (!_inSquall || _squall.Inten < _krakenRules.MinInten) GoToStorm(0.2);
                    _kraken.Summon(true, PreyOf(_ship));
                    break;
                // Page Haut / Page Bas : ces deux-la portent le meme nom et occupent
                // la meme place sur toute disposition, ce qui evite la question
                // AZERTY entierement.
                case Key.Pageup: _swell = Math.Min(2.6, _swell + 0.15); Restate(); break;
                case Key.Pagedown: _swell = Math.Max(0.4, _swell - 0.15); Restate(); break;
                // parer le bord, ou rendre le feu à volonté
                case Key.B when k.ShiftPressed: ToggleLayOrder(); break;
                case Key.V when k.ShiftPressed: ReefStep(); break;
                case Key.V: _ship.Ctrl.SailsSet = !_ship.Ctrl.SailsSet; _ship.Ctrl.Canvas = 1; _reef = 0; break;
                /* N SEUL NE SERT PLUS (demandé). Elle passait à la fiche suivante,
                   ce qui était bon quand le jeu était un banc d essai de coques : on
                   choisit désormais son navire au menu, et changer de monture en
                   pleine mer n était plus qu un moyen de se perdre. ⇧N garde le
                   panneau de flotte, qui est autre chose. */
                case Key.N when k.ShiftPressed: ToggleFleetPanel(); break;
                /* N REND LA CHALOUPE, et la touche retrouve son emploi : elle avait
                   été libérée parce que changer de monture en pleine mer n était
                   qu un moyen de se perdre. Mettre une chaloupe à l eau n est pas
                   changer de monture — c est la quitter pour y revenir. */
                case Key.N: Say(BoatSwing()); break;
                /* ⇧F : UN COUP DE FOUDRE, TOUT DE SUITE — F comme foudre. K est
                   le kraken et le reste. Le sort décide s'il frappe la mâture ou
                   s'il tombe à l'eau le long du bord, par la même règle que
                   l'orage : la touche montre ce qu'on verra en jeu. */
                case Key.F when k.ShiftPressed: StrikeSomewhere(_ship); break;
                case Key.F: _follow = !_follow; break;
                // ⇧C : la chambre seule — le capitaine dort parfois
                case Key.C when k.ShiftPressed: DouseCabin(); break;
                case Key.C: CycleCamera(); break;
                // ⇧X : DEUX NAVIRES AUX PRISES — deux sabres en croix, et la seule
                // rencontre qu on ne voyait pour ainsi dire jamais (une sur cinq)
                case Key.X when k.ShiftPressed: ForceEncounter(true); break;
                /* ⇧Q : LE PIRATE PASSE À L ABORDAGE SUR-LE-CHAMP. Il n y vient
                   normalement qu après avoir démâté ou troué sa proie, ce qui prend
                   un combat entier — trop long pour juger des filins. */
                case Key.Q when k.ShiftPressed:
                {
                    int n = 0;
                    foreach (var pr in _pirates.Values)
                        if (pr.Cible != null) { pr.State = Pirate.Phase.Abordage; pr.Tenu = 0; n++; }
                    Say(n > 0 ? $"{n} pirate(s) à l abordage" : "Aucun pirate en chasse");
                    break;
                }
                case Key.X: if (_fixed) Plant(); break;
                /* LE PLAN D'ARRIMAGE, sur la seule place de lettre qui ne servait à
                   rien : le Z d'un QWERTY, le W d'un AZERTY. Par son EMPLACEMENT,
                   pour ne jamais tomber sur la machine ; le mémento et le panneau
                   disent la lettre du clavier qu'on a sous les doigts. */
                // ⇧ et la même place : le branle-bas, sabords ouverts et pièces en batterie (ShipDemo.Ports.cs)
                case Key.Z when k.ShiftPressed: TogglePorts(); break;
                case Key.Z: ToggleStow(); break;
                // l'occlusion ambiante et l'illumination globale, pour juger à l'œil
                // ⇧O : GAGNER LE LARGE, là où l on croise des voiles
                case Key.O when k.ShiftPressed: GoOffshore(); break;
                case Key.O: _settings.Occlusion = !_settings.Occlusion; Changed(); break;
                /* TAB : LES OBJECTIFS, à tout moment (demandé) ; le bord en batterie
                   passe à ⇧Tab, et reste au doigt sur son panneau. */
                case Key.Tab when k.ShiftPressed: CycleGunSide(); break;
                case Key.Tab: ToggleObjectives(); break;
                /* ⇧Y : LE FEU À BORD — Y fait sauter la soute, ⇧Y allume ce qui
                   l'y mènera si personne ne s'en occupe. */
                case Key.Y when k.ShiftPressed:
                    LightFire(_ship, new Vec3d((_stormRng.NextDouble() - 0.5) * _ship.Spec.B * 0.6,
                        _ship.Spec.DeckMid, (_stormRng.NextDouble() - 0.5) * _ship.Spec.L * 0.5), 0.14);
                    break;
                case Key.Y: BlowUp(_ship); break;
                // ⇧U : le vaisseau fantôme, tout de suite — U appelle une voile, ⇧U celle qui n'en est pas une
                case Key.U when k.ShiftPressed: if (!BestiaireMuet()) SummonWraith(); break;
                case Key.U: SpawnPirate(900); break;
                case Key.P when k.ShiftPressed: ToggleColours(); break;
                case Key.P: if (!BestiaireMuet()) GoToGhosts(true); break;
                /* ⇧L COUVRE LES FEUX — L comme lunette, ⇧L comme lanternes. Un
                   navire qui veut ne pas être vu la nuit éteint, et c est la seule
                   chose qu il puisse faire. */
                /* LE SAUT. ⇧A comme « anneaux ». Ce n'est PAS ⇧G, qui serait la
                   lettre de « gyroscope » : G est intercepté trente lignes plus
                   haut par la bordée, avec son propre SetInputAsHandled, et rien
                   de ce qui porte G n'atteint jamais ce switch. Signalé en jeu. */
                case Key.A when k.ShiftPressed: ArmJump(); break;
                // ⇧D comme « détacher » : la hache sur les filins d'abordage
                case Key.D when k.ShiftPressed:
                    Say(_grapples.Cut(_ship.Physics)
                        ? (_grapples.Holds(_ship.Physics) ? "Un filin tranché — il en tient encore" : "Le dernier filin est tranché !")
                        : "Aucun filin à trancher");
                    break;
                case Key.L when k.ShiftPressed: Douse(); break;
                case Key.L: ToggleSpyglass(); break;
                // la carte du capitaine : I comme « inscrire »
                case Key.I when k.ShiftPressed: ToggleJournal(); break;
                case Key.I: ToggleChart(); break;
                // M par sa LETTRE comme ⇧M : sur un AZERTY il n'est pas à la place du QWERTY

                /* L'ÉLAN : l'équivalent de `Naval.app.controls.state.throttle = 45`
                   dans la console d'origine. Le solveur ne borne pas la machine,
                   donc c'est quarante-cinq fois la poussée — de quoi voir une coque
                   lancée dans la houle sans attendre qu'elle prenne son erre. Un
                   second appui coupe, sans quoi elle filerait sans fin ; W et S
                   la ramènent aussi dans leur plage en la touchant.

                   DEUX APPUIS RAPPROCHÉS doublent la VITESSE de l'élan, pas sa
                   poussée : la résistance d'une coque croît au moins comme le
                   carré de sa vitesse, donc il faut quatre fois la poussée
                   (180). Le premier des deux appuis a pu couper l'élan — le
                   second le reprend, doublé. */
                case Key.B:
                {
                    ulong now = Time.GetTicksMsec();
                    bool twice = now - _boostTap < BoostTwice;
                    _boostTap = now;
                    _ship.Ctrl.Throttle = twice ? BoostThrust * 4 : _ship.Ctrl.Throttle > 1 ? 0 : BoostThrust;
                    if (twice) Say("Élan doublé");
                    break;
                }
                /* L'IMAGE SEULE : les commandes et le panneau du soleil s'effacent,
                   pour regarder ou filmer. Le menu reste sur Échap, et le masque de
                   cinéma, qui fait partie de l'image, reste en place. */
                case Key.H when k.ShiftPressed: _hudOn = !_hudOn; _sunPanel.Visible = _hudOn; break;
                // é en AZERTY (le 2 de la rangée du haut), ou le 2 du pavé : le mode cinéma, et retour
                case Key.Key2 or Key.Kp2 when !k.ShiftPressed && !k.Echo: ToggleCinema(); break;
                // " en AZERTY (le 3 du haut), ou le 3 du pavé : la cloche à l'eau, ou hissée
                // ⇧" : la cloche rentrée d'un coup, sans attendre le treuil
                case Key.Key3 or Key.Kp3 when k.ShiftPressed && !k.Echo: StowBellNow(); break;
                case Key.Key3 or Key.Kp3 when !k.ShiftPressed && !k.Echo: ToggleBell(); break;
                // 4 : par-dessus bord, navire stoppé (ShipDemo.Swim.cs)
                case Key.Key4 or Key.Kp4 when !k.ShiftPressed && !k.Echo: SwimJump(); break;
                case Key.Enter or Key.KpEnter when _bellOut && !k.Echo: TakeChest(); break;
                case Key.H: _info.Visible = !_info.Visible; break;
                // le menu d'options ; « Quitter » y est désormais
                case Key.F1: ToggleKeys(); break;
                case Key.Escape:
                    if (CloseKeys()) break;
                    // les réglages se ferment d abord ; sinon c est le menu d Échap
                    if (_menu.Visible) { _menu.Visible = false; break; }
                    _chkOcclusion.SetPressedNoSignal(_settings.Occlusion);
                    _chkIndirect.SetPressedNoSignal(_settings.IndirectLight);
                    TogglePause();
                    break;
            }
        }
        // la lunette à l'œil prend le glisser et la molette
        if (GlassMouse(e)) return;
        if (e is InputEventMouseButton mb)
        {
            if (mb.ButtonIndex == MouseButton.Left) _dragging = mb.Pressed;
            // à bord la molette change la focale, comme dans la page ; dehors, la distance
            else if (mb.ButtonIndex == MouseButton.WheelUp)
            {
                // du ponton comme à bord : la molette est l'œil qui se plisse, pas un pas en arrière
                if (_camMode == 1 || _camMode == 4) _cam.Fov = Mathf.Clamp(_cam.Fov - 2, 12, 75);
                else _dist = Mathf.Max(10f, _dist * 0.9f);
            }
            else if (mb.ButtonIndex == MouseButton.WheelDown)
            {
                if (_camMode == 1 || _camMode == 4) _cam.Fov = Mathf.Clamp(_cam.Fov + 2, 12, 75);
                else _dist = Mathf.Min(900f, _dist * 1.11f);
            }
        }
        if (e is InputEventMouseMotion mm && _dragging)
        {
            /* À LA PIÈCE : le glisser de côté regarde le long du bord, celui de haut en
               bas LÈVE OU BAISSE LA PIÈCE — la hausse, au coin, fine : un degré pour
               une douzaine de pixels. La mire suit, contre l'horizon. */
            if (_gunPost is int gp)
            {
                // de côté, la pièce tourne sur son affût (l'œil avec elle) ; de haut en bas, la hausse
                NudgeTrain(gp, -mm.Relative.X * 0.0025);
                NudgeHausse(gp, -mm.Relative.Y * 0.0015);
            }
            else if (_camMode == 1)
            {
                // regarder autour À PARTIR du regard de la vue
                _bridgeYaw -= mm.Relative.X * 0.004;
                _bridgePitch = Math.Clamp(_bridgePitch - mm.Relative.Y * 0.004, -0.7, 0.7);
            }
            else if (_camMode == 4)
            {
                // sur le musoir, on tourne la tête comme on veut
                _jettyYaw -= mm.Relative.X * 0.004;
                _jettyPitch = Math.Clamp(_jettyPitch - mm.Relative.Y * 0.004, -0.9, 0.9);
            }
            else if (_fixed)
            {
                // pointer une caméra plantée à la main, comme dans l'original
                _fixYaw -= mm.Relative.X * 0.004;
                _fixPitch = Math.Clamp(_fixPitch - mm.Relative.Y * 0.004, -0.9, 0.9);
            }
            else
            {
                _orbit -= mm.Relative.X * 0.008f;
                // sous zéro, elle plonge : la mer se referme au-dessus et l'on voit le ciel par la fenêtre
                _pitch = Mathf.Clamp(_pitch + mm.Relative.Y * 0.004f, -1.2f, 1.3f);
            }
        }
    }
}
