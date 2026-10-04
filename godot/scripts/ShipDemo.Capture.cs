using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using NavalSim.Core;

namespace NavalSim;

/// <summary>La capture en ligne de commande, --frametimes et le contrôle d'écume (sorti de ShipDemo.cs).</summary>
public partial class ShipDemo
{
    /// <summary>--escarmouche, tenu jusqu apres la mise a quai.</summary>
    bool _wantMelee;

    /// <summary>Le cap que --cap impose, s'il y en a un.</summary>
    double? _askHeading;

    // --- la capture en ligne de commande, comme dans SeaDemo ---
    string _capturePath = "";
    int _captureIn = -1;

    void SetupCapture()
    {
        var args = OS.GetCmdlineUserArgs();
        for (int i = 0; i < args.Length - 1; i++)
            switch (args[i])
            {
                case "--capture": _capturePath = args[i + 1]; if (_captureIn < 0) _captureIn = 60; _askTitle ??= false; break;
                // --titre 0 entre droit dans le jeu, --titre 1 le force même en capture
                case "--titre": _askTitle = args[i + 1] != "0"; break;
                // combien d images la laisser vivre avant de la photographier : une
                // coque met du temps a prendre son erre, et une seconde de
                // simulation ne montre qu une voilure a moitie etablie
                case "--after": _captureIn = args[i + 1].ToInt(); break;
                case "--force": _force = args[i + 1].ToFloat(); Restate(); break;
                case "--date": _calendar = new Calendar(args[i + 1]); break;
                // une traversée tout de suite, une seule fois : l'arrivée relit la même ligne de commande
                case "--traversee":
                    if (!_cliVoyaged)
                    {
                        _cliVoyaged = true;
                        // de quoi voir la malle passer : des épices au fond, du lest sur le pont
                        _ship.Physics.LoadCargo(2, HoldFloor, 0, 12, "epice");
                        _ship.Physics.LoadCargo(3, 0.85, 1, 5);
                        GD.Print(FormattableString.Invariant($"départ : bourse {_purse.Sous} sous, cale {_ship.Physics.CargoTonnes:F1} t, poudre {_ship.Physics.Powder}, {_calendar.Date:yyyy-MM-dd} {_sky.Core.DayTime:F1} h"));
                        SailTo(args[i + 1]);
                    }
                    break;
                case "--averse": _climate.StartShower(args[i + 1].ToFloat(), 1.0); break;
                case "--kraken": _kraken.Summon(args[i + 1] == "1", PreyOf(_ship)); break;
                // --baleine 0 : indifférente, 1 : curieuse, 2 : hostile
                case "--baleine": SummonWhale((WhaleMood)Math.Clamp(args[i + 1].ToInt(), 0, 2)); break;
                // une seconde après la mise à l'eau : une traversée pose le navire APRÈS la ligne de commande
                // LA BRUME RASANTE, à la volée : 0 à 1, sans toucher à celle de l'air
                case "--rasante": _mistForce = args[i + 1].ToFloat(); break;
                // ce qui reste de la toile sous les étoiles : pour juger à l'œil
                case "--toile-nuit":
                    ShipNode.CanvasFloor = Math.Clamp(args[i + 1].ToFloat(), 0, 1);
                    _ship.RefreshCanvasFloor();
                    foreach (var sn in _others) sn.RefreshCanvasFloor();
                    break;
                // ce qu'UNE nappe arrête : pour juger le voile sans toucher au fichier
                case "--voile":
                    _mistGain = Math.Clamp(args[i + 1].ToFloat(), 0, 1);
                    _mist?.Material.SetShaderParameter("u_mist_gain", (float)_mistGain);
                    break;
                case "--brume": if (args[i + 1] != "0") (_seaFog ??= new SeaFog(_fogRules)).Force(args[i + 1].ToFloat() > 1 ? args[i + 1].ToFloat() : 6); break;
                case "--serpent": _serpentIn = args[i + 1] != "0" ? 1.0 : -1; break;
                /* L HEURE, PAS LE SOLEIL : le cycle du jour réécrit le soleil depuis
                   l heure à chaque image, et un soleil posé à la main revenait au jour.
                   --sun fige le soleil, --heure pose la NUIT. */
                case "--heure": _sky.Core.SetTimeOfDay(args[i + 1].ToFloat(), _sky.Latitude); _sky.Apply(); break;
                // les feux couverts tout de suite, comme ⇧L : pour juger ce qui reste visible
                // comme la touche : un ordre, et le bord choisit comment l exécuter
                case "--feux": _ship.OrderLanterns(args[i + 1] != "0", _t, _sky.Core.Night); break;
                case "--foudre": StrikeSomewhere(_ship); break;
                // le coup à l'eau seul, pour le régler : --pres 1
                case "--pres": if (args[i + 1] != "0") StrikeAlongside(_ship); break;
                case "--bordee": _gunSide = args[i + 1].ToInt(); Fire(false, true); break;
                // --soute 1 : tout de suite ; --soute 4 : dans quatre secondes, après un --large par exemple
                case "--soute": if (args[i + 1].ToFloat() > 1) _souteIn = args[i + 1].ToFloat(); else BlowUp(_ship); break;
                /* DES DÉPARTS DE FEU, tout de suite : pour le régler sans se faire
                   canonner. Le nombre compte plus que la force — c'est lui qui
                   décide, puisque l'effort de l'équipage se DIVISE. */
                case "--incendie":
                    for (int n = Math.Max(1, args[i + 1].ToInt()); n > 0; n--)
                        LightFire(_ship, new Vec3d((_stormRng.NextDouble() - 0.5) * _ship.Spec.B * 0.6,
                            _ship.Spec.DeckMid, (n - 2.0) * _ship.Spec.L * 0.16), 0.15);
                    break;
                case "--pirate": SpawnPirate(args[i + 1].ToFloat()); break;
                case "--fantomes": GoToGhosts(true); break;
                /* La bataille montee d un bloc, sans passer par le menu. APRES la
                   mise a quai et non ici : le mouillage de depart passe apres la
                   ligne de commande et reposerait le joueur a Port-Royal pendant
                   que sa ligne attend au large. */
                case "--escarmouche": _wantMelee = args[i + 1] != "0"; break;
                // DÉMONSTRATION : plonger la caméra à tant de mètres par seconde
                case "--plongee": _diveSpeed = args[i + 1].ToFloat(); break;
                /* LA LUMIÈRE DU FOND, à la volée : pour comparer deux images de la
                   même vue, ce qui est la seule façon d'en connaître le prix. */
                // LES DAUPHINS : les faire venir tout de suite
                // LE VAISSEAU FANTÔME : le faire paraître tout de suite
                case "--fantome": if (args[i + 1] != "0") SummonWraith(); break;
                // gagner le large, puis faire venir une voile ou deux aux prises
                // le large d'abord, la voile une seconde après : elle doit naître
                // d'un navire DÉJÀ au large, sinon la règle des 2500 m la refuse
                case "--large": if (args[i + 1] != "0") _largeIn = 1.5; break;
                // le retour au ponton, comme le bouton du menu
                case "--ponton": if (args[i + 1] != "0") _pontonIn = args[i + 1].ToFloat(); break;
                case "--rencontre": _metPair = args[i + 1].StartsWith("p"); _metIn = 2.5; break;
                // LA CHALOUPE : l affaler tout de suite
                case "--chaloupe": if (args[i + 1] != "0") GD.Print("chaloupe : " + BoatSwing()); break;
                case "--dauphins":
                    if (args[i + 1] != "0") _dolphins?.Summon(_ship.Physics.Body.Pos, _ship.Physics.Body.Quat);
                    break;
                // LES MOUETTES, à la volée
                case "--mouettes":
                    if (_gulls != null) _gulls.Visible = args[i + 1] != "0";
                    break;
                // LES BANCS, à la volée : pour comparer deux images de la même vue
                case "--poissons":
                    if (_fishNode != null) _fishNode.Visible = args[i + 1] != "0";
                    break;
                case "--caustiques":
                    ShipNode.Caustic?.SetShaderParameter("u_caustic_gain",
                        args[i + 1] == "0" ? 0f : args[i + 1].ToFloat());
                    _land?.Ground.SetShaderParameter("u_caustic_gain",
                        args[i + 1] == "0" ? 0f : args[i + 1].ToFloat());
                    break;
                case "--lunette": ToggleSpyglass(); break;
                case "--feu": for (int n = args[i + 1].ToInt(); n > 0; n--) Fire(false, true); break;
                // le mémento ouvert d emblee, pour le juger a la capture
                case "--commandes": if (args[i + 1] != "0") ToggleKeys(); break;
                // la musique tout de suite, pour l'entendre sans chercher la bagarre
                case "--musique": _settings.Music = args[i + 1] != "0"; break;
                // une vue de pont d emblee : 0 la passerelle, 1 la chambre
                // regarder autour depuis une vue de pont : relèvement, site
                case "--regard":
                {
                    var rv = ParseVec(args[i + 1] + ",0");
                    _bridgeYaw = rv.X * Math.PI / 180; _bridgePitch = rv.Y * Math.PI / 180;
                    break;
                }
                case "--carte": _overChart = args[i + 1] != "0"; break;
                // la carte ouverte d emblee, pour la juger
                case "--carte-ouverte": if (args[i + 1] != "0") ToggleChart(); break;
                // une quete lancee d emblee, par son id : --quete apprendre-la-mer
                // PAR LA MEME PORTE QUE LE MENU, sinon le levier n eprouve pas ce que le joueur fait :
                // StartQuest arme le navire que la fiche impose, Start ne le fait pas.
                // une quête demandée se joue tout de suite : pas d affiche par-dessus
                case "--quete": _askTitle ??= false; StartQuest(args[i + 1]); break;
                /* un navire parlé tout de suite : une chose qu on ne peut éprouver
                   qu en attendant huit minutes est une chose qu on n éprouve pas */
                case "--parler": if (args[i + 1] != "0") Speak(); break;
                // et sauter droit au lieu de l etape, comme allerQuete() dans la console de la page
                case "--etape": if (args[i + 1] != "0") GoToStep(); break;
                case "--pont":
                    _camMode = 1; _deck = Math.Clamp(args[i + 1].ToInt(), 0, Math.Max(0, _ship.Spec.Decks.Count - 1));
                    EnterDeck();
                    break;
                // mouiller d emblee, pour juger l ancre a la capture
                case "--ancre": if (args[i + 1] != "0") _anchor2?.Toggle(_ship, _t); break;
                // l'objectif mouillé d'emblée, pour juger les gouttes sans plonger
                case "--gouttes": _wet = Math.Clamp(args[i + 1].ToFloat(), 0, 1); break;
                // semer des pièces sous la coque, pour juger leur chute sans couler
                case "--tresor":
                {
                    var pb = _ship.Physics.Body;
                    _coins.Spill(pb.Pos.ToGodot(),
                        _ship.Spec.L, _ship.Spec.B, args[i + 1].ToInt());
                    break;
                }
                case "--nappe": _sheet = Math.Clamp(args[i + 1].ToFloat(), 0, 1); break;
                // la couverture nuageuse imposee, pour juger le ciel sans attendre la meteo
                case "--nuages": _cloud = Math.Clamp(args[i + 1].ToFloat(), 0, 1); break;
                // l'œil tourné vers le soleil, un peu au-dessus de l'eau : pour juger sa route
                case "--vers-soleil":
                    var sdir = _sky.Core.SunDir;
                    _fixEye = new Vector3(30, 6, 30);
                    _fixLook = _fixEye.Value + new Vector3((float)sdir.X, 0, (float)sdir.Z).Normalized() * 300 + new Vector3(0, -6, 0);
                    break;
                // les instruments masqués, comme H : pour une capture de la scène seule
                /* TOUS les instruments, et non les deux étiquettes de gauche : une
                   capture de la mer ou de la toile n a que faire du comptoir, de la
                   rose et de la barre de pièces. */
                // ce que la mer renvoie vers le haut : 0 rend l'image d'avant
                /* PAR LE RÉGLAGE et non par le ciel seul : ApplySettings repasse
                   derrière et rendrait au ciel la valeur du menu — deux captures
                   de comparaison seraient sorties identiques sans qu on le voie. */
                case "--rebond":
                    _settings.SeaBounce = args[i + 1].ToFloat();
                    _sky.SeaBounce = _settings.SeaBounce; _sky.Apply(); break;
                case "--masquer": _hudOn = args[i + 1] != "1"; _info.Visible = _sunPanel.Visible = _hudOn; break;
                case "--panneau-mer": _seaPanel.Visible = args[i + 1] == "1"; break;
                // la mer aux valeurs par défaut, sans toucher au fichier : pour comparer
                case "--mer-defaut":
                    var dm = new Settings();
                    _settings.SeaRoughBase = dm.SeaRoughBase; _settings.SeaRoughWind = dm.SeaRoughWind;
                    _settings.SeaSkyBlur = dm.SeaSkyBlur; _settings.SeaRideGain = dm.SeaRideGain;
                    _settings.SeaCapGain = dm.SeaCapGain; _settings.SeaFoamGain = dm.SeaFoamGain;
                    _settings.SeaJacobian = dm.SeaJacobian; _settings.SeaStreaks = dm.SeaStreaks; _settings.SeaKelvin = dm.SeaKelvin;
                    _settings.SeaShafts = dm.SeaShafts; _settings.SeaDensity = dm.SeaDensity;
                    ApplySettings();
                    break;
                // une cible par le travers tribord, à cette distance : le premier navire de --flotte
                case "--cible":
                    if (_others.Count > 0)
                    {
                        var cb = _others[0].Physics.Body;
                        cb.Pos = new Vec3d(-args[i + 1].ToFloat(), cb.Pos.Y, 0);
                        _others[0].SyncTransform();
                    }
                    break;
                // la coque retournée, pour éprouver le chavirage et R
                case "--chavirer": _flipIn = args[i + 1] != "0" ? 1.0 : -1; break;
                case "--demater": _ship.DropMast(args[i + 1].ToInt()); break;
                /* DES TROUS DANS SA PROPRE TOILE, sans attendre qu un ennemi les y mette :
                   au CENTRE de chaque voile hissee, l une apres l autre, en repassant
                   sur les memes quand on en demande plus qu il n y a de voiles. */
                /* UN BOULET VENU D UN BORD, a la hauteur qu on lui donne : de quoi
                   eprouver ce qu une toile fait d un coup qui la traverse, sans
                   attendre qu une bataille veuille bien en tirer un au bon endroit.
                   L argument est sa hauteur sur l eau, en metres. */
                case "--boulet": _shotY = args[i + 1].ToFloat(); _shotIn = 4.0; break;
                case "--trous": _holesWanted = args[i + 1].ToInt(); _holesIn = 4.0; break;
                // LE BEAUPRÉ, tout de suite : pour le voir tomber sans le canonner
                case "--beaupre": if (args[i + 1] != "0") GD.Print("beaupré : " + (_ship.DropSprit() ? "il part" : "aucun reconnu")); break;
                case "--meteo": SetAutoWeather(args[i + 1] == "1"); break;
                case "--tempete": GoToStorm(args[i + 1].ToFloat()); break;
                case "--swell": _swell = args[i + 1].ToFloat(); Restate(); break;
                case "--pitch": _pitch = args[i + 1].ToFloat(); break;
                case "--dist": _dist = args[i + 1].ToFloat(); break;
                case "--orbit": _orbit = args[i + 1].ToFloat(); break;
                case "--ship": Launch(args[i + 1].ToInt()); break;
                case "--sails": _ship.Ctrl.SailsSet = args[i + 1] == "1"; break;
                // la machine en ligne de commande : une capture « en route » ne
                // peut pas dependre du clavier, et un banc non plus
                case "--throttle": _ship.Ctrl.Throttle = args[i + 1].ToFloat(); _drive = true; break;
                case "--barre": _heldRudder = Math.Clamp(args[i + 1].ToFloat(), -1, 1); break;
                // un cap impose, en relevement vrai : pour jeter une coque a la cote
                case "--cap":
                    _askHeading = args[i + 1].ToFloat() * Math.PI / 180;
                    break;
                // un oeil FIXE dans le monde, pour comparer au pixel avec la page
                // d'origine : une camera qui suit une coque soulevee de quarante
                // metres se retrouve dans la vague, et la comparaison ne vaut rien
                // la caméra suit le premier navire de la rade (ShipDemo.Traffic.cs)
                case "--poser-navire": _hullTest = args[i + 1]; _askTitle ??= false; break;
                case "--rade-vue": _tripCam = true; _askTitle ??= false; break;
                // un mouvement des horaires échu dès l'ouverture (« depart:3 », « arrivee:1 »)
                case "--horaire": _forceSailing = args[i + 1]; _askTitle ??= false; break;
                // l'œil sur un poste du port (ShipDemo.Traffic.cs)
                case "--horaire-vue": _berthView = args[i + 1].ToInt(); _askTitle ??= false; break;
                case "--eye": _fixEye = ParseVec(args[i + 1]); _planted = true; break;
                case "--look": _fixLook = ParseVec(args[i + 1]); break;
                case "--foamcheck": _foamCheckIn = args[i + 1].ToInt(); break;
                // le profil de flottaison, station par station, à poser à côté de
                // ship.hullProfile(64, -eq) dans la page
                // la régularité des images, mesurée : voir FrameStats
                // sans synchro verticale : pour mesurer ce que la machine tient vraiment
                // par les réglages (sans les enregistrer) : une autre option qui les réapplique ne la défait pas
                case "--plein": _settings.Fullscreen = args[i + 1] != "0"; ApplySettings(); break;
                case "--vsync": _settings.VSync = args[i + 1] != "0"; ApplySettings(); break;
                // la charge forcee, pour un banc : la rampe met vingt secondes
                case "--anneaux": _forceRings = args[i + 1].ToFloat(); break;
                case "--rade": MooredNode.Debug = args[i + 1] != "0";
                    GD.Print($"[rade] joueur a ({_ship.Physics.Body.Pos.X:F0}, {_ship.Physics.Body.Pos.Z:F0}) local, origine ({_sea.Core.Origin.X:F0}, {_sea.Core.Origin.Z:F0})"); break;
                // les ombres des fanaux : une omni qui porte ombre rend un CUBE par image
                case "--ombres":
                    _settings.LanternShadows = args[i + 1] == "1";
                    // le navire commandé SEUL, comme le réglage : voir SpawnFleet
                    _ship.SetLanternShadows(_settings.LanternShadows);
                    break;
                /* PAR LE RÉGLAGE, ET NON PAR L'ENVIRONNEMENT.

                   Ces deux-là écrivaient droit sur _sky.Env, que ApplySettings
                   reprend ensuite sur _settings (voir plus haut, SsaoEnabled et
                   SsilEnabled). Le levier tenait donc jusqu'au prochain appel —
                   l'ouverture du menu, un changement de réglage — et pas au-delà :
                   qui mesurait avec lui mesurait la valeur du fichier de réglages,
                   pas la sienne, sans que rien ne le dise.

                   C'est la faute exacte du rebond de la mer, qui avait failli
                   rendre deux captures de comparaison identiques. Un levier de banc
                   doit emprunter le chemin du joueur, sinon il n'éprouve pas ce que
                   le joueur obtient — et ApplySettings EST ce chemin. */
                case "--ssao": _settings.Occlusion = args[i + 1] == "1"; ApplySettings(); break;
                case "--ssil": _settings.IndirectLight = args[i + 1] == "1"; ApplySettings(); break;
                case "--frametimes": _ftLeft = args[i + 1].ToInt(); _ftGc0 = GC.GetTotalPauseDuration(); break;
                // le soleil figé à cette hauteur : pour éprouver la nuit sans attendre
                case "--sun": _sky.DayRate = 0; _sky.Core.SetSun(args[i + 1].ToFloat(), _sky.Core.SunBearingDeg); _sky.Apply();
                    GD.Print(FormattableString.Invariant($"nuit {_sky.Core.Night:F2}, lune {(_sky.Core.MoonOn ? "oui" : "non")} phase {_sky.Core.MoonPhase:F2} levée {_sky.Core.MoonUp:F2}, lumière de l'eau {_sky.Core.WaterLight:F3}, lumière directe {_sky.Core.SunIntensity:F3}"));
                    break;
                // ouvrir directement une vue à bord de la fiche
                // l'œil posé sur la surface, moitié dedans moitié dehors
                case "--mi-eau": _camMode = 3; SetLens(OutsideFov, OutsideNear); break;
                case "--flyby": if (args[i + 1] != "0") EnterFlyBy(); break;
                case "--cinema": if (args[i + 1] != "0") ToggleCinema(); break;
                // des débris à flot devant l'étrave, pour voir leurs modèles — différé comme la plongée
                case "--debris": _debrisTest = args[i + 1].ToInt(); _diveTestIn = 1.0; break;
                // l'œil sous l'eau sur un pâté du fond (« corail », « herbier », « rochers »)
                case "--fond": _bedTest = args[i + 1]; _diveTestIn = 1.0; _askTitle ??= false; break;
                // DIAGNOSTIC : les pièces que chaque fiche porte, lues sur son modèle — elles ne sont écrites nulle part ailleurs
                case "--batteries":
                    if (args[i + 1] != "0")
                        foreach (var path in _paths)
                        {
                            if (ShipLibrary.Load(path) is not { } sp) continue;
                            var probe = new ShipNode();
                            AddChild(probe);
                            probe.Build(sp);
                            GD.Print(FormattableString.Invariant($"batterie {sp.Id} : {probe.Battery.Guns.Count} pièce(s), {sp.Tonnes:F0} t, cale {probe.Physics.CargoCapacity:F0} t"));
                            RemoveChild(probe); probe.QueueFree();
                        }
                    break;
                /* L'ESSAI DE PLONGÉE, une seconde APRÈS le départ : le navire n'est mis à
                   son poste qu'après la ligne de commande, et une épave posée avant
                   l'aurait été là où il n'est plus. */
                case "--cloche": if (args[i + 1] != "0") _diveTest.Bell = true; _diveTestIn = 1.0; break;
                case "--descendre": _diveTest.Rope = args[i + 1].ToFloat(); _diveTestIn = 1.0; break;
                case "--saisir": _diveTest.Take = args[i + 1] != "0"; break;
                // où l'on est, une seconde après le départ (l'origine bouge à la mise à quai)
                case "--ou": _whereTest = args[i + 1] != "0"; _diveTestIn = 1.0; break;
                // la chaloupe échouée sur la grève la plus proche, nageant ; poussée à l'eau tant de secondes après
                case "--echouer": _beachTest = args[i + 1].ToFloat(); _diveTestIn = 1.0; break;
                // le mode création : prendre ce qui est au milieu de l'écran, le pousser de tant de mètres, le tourner, enregistrer
                case "--creation": _edTest = args[i + 1].ToFloat(); _diveTestIn = 1.0; break;
                case "--sifflet": _whistleTest = args[i + 1].ToInt(); _diveTestIn = 1.0; break;
                // poser des modèles bruts (séparés par des virgules) près de l'œil, SANS les enregistrer : pour les regarder
                case "--voir": _seeModels = args[i + 1]; _diveTestIn = 1.0; break;
                // l'ancre oubliée : en route machine avant toute à N s, on vire à M s ; relevé chaque seconde
                case "--ancre-oubliee": _anchorTest = ParseVec(args[i + 1]); break;
                // la pêche de bout en bout, au point vrai (x, z), les lignes accélérées : ShipDemo.Fishing.cs
                case "--vent-journal": _windLog = 1; break;
                case "--peche": _fishTest = ParseVec(args[i + 1]); _askTitle ??= false; break;
                case "--coller": _edPasteTest = args[i + 1].ToInt(); _edTest = 1; _diveTestIn = 1.0; break;
                // le pinceau, sans rien enregistrer : trois disques et une rue autour de (x, z) vrais
                case "--peindre": _paintTest = ParseVec(args[i + 1]); _diveTestIn = 1.0; break;
                // une épave d'essai, à tant de mètres par le travers : son coffre avec
                case "--epave": _diveTest.Wreck = args[i + 1].ToFloat(); _diveTestIn = 1.0; break;
                // l'âge de l'épave d'essai, en jours : sa vase
                case "--epave-age": _wreckAge = args[i + 1].ToFloat(); break;
                case "--mi-eau-haut": _splitLift = args[i + 1].ToFloat(); break;
                case "--vue": _camMode = 1; _deck = Math.Clamp(args[i + 1].ToInt(), 0, _ship.Spec.Decks.Count - 1); EnterDeck(); break;
                case "--msaa": _settings.Msaa = args[i + 1].ToInt(); ApplySettings(); break;
                case "--dofn": _settings.DofNear = args[i + 1].ToFloat(); ApplySettings(); break;
                case "--dofd": _settings.DofDistance = args[i + 1].ToFloat(); ApplySettings(); break;
                case "--anamorph": _settings.DofAnamorphic = args[i + 1] == "1"; ApplySettings(); break;
                case "--dofq": _settings.DofQuality = args[i + 1].ToInt(); ApplySettings(); break;
                case "--aa": _settings.ScreenAA = args[i + 1]; ApplySettings(); break;
                case "--masque": _settings.FilmMask = args[i + 1] == "1"; ApplySettings(); break;
                case "--expo": _settings.AutoExposure = args[i + 1] == "1"; ApplySettings(); break;
                case "--lueur": _settings.Glow = args[i + 1] == "1"; ApplySettings(); break;
                /* LE REFLET DU TELEPORTEUR : 0 aucun, 1 normal, 2 SANS SON MASQUE.
                   Un masque qui refuse tout et une geometrie absente donnent la meme
                   image — rien. Ce cadran separe les deux en un lancement, ce qui vaut
                   mieux qu une hypothese de plus. */
                case "--reflet":
                    _refletMode = args[i + 1].ToInt();
                    break;
                case "--dof": _settings.Dof = args[i + 1] == "1"; ApplySettings(); break;
                case "--flou": _settings.MotionBlur = args[i + 1] == "1"; ApplySettings(); break;
                case "--parallele": _settings.ParallelSolvers = args[i + 1] == "1"; break;
                case "--flotte":
                    SpawnFleet(args[i + 1].ToInt(), _flotteShip >= 0 ? _flotteShip
                        : Math.Max(0, _paths.FindIndex(p => System.IO.Path.GetFileName(p) == "frigate17e.json")));
                    break;
                case "--flotte-navire": _flotteShip = args[i + 1].ToInt(); break;
                case "--dumprig":
                    foreach (var l in _ship.RigLog) GD.Print("gréement " + l);
                    break;
                case "--dumpprofile":
                    GD.Print("profil " + string.Join(" ", Array.ConvertAll(_prof.Fractions,
                        f => f.ToString("F5", System.Globalization.CultureInfo.InvariantCulture))));
                    break;
            }
    }

    Vector3? _fixEye, _fixLook;

    /* LA RÉGULARITÉ DES IMAGES, en nombres. Un à-coup « quasi imperceptible »
       ne se juge pas à l'œil sur une capture : il se lit dans la distribution
       des temps d'image, et dans ce que le ramasse-miettes a pris pendant ce
       temps. On relève le pas brut de Godot, sans le plafond de 50 ms. */
    int _slams, _sprayMax;
    long _allocPhys, _allocSails, _allocFrames, _allocRun0 = -1, _allocProc;
    int _ftLeft = -1;
    readonly List<double> _ft = new();
    TimeSpan _ftGc0;
    int _ftCol0 = -1, _ftCol1, _ftCol2;

    readonly System.Diagnostics.Stopwatch _ftWatch = new();
    double _ftCpuSum, _ftGpuSum, _ftRsCpuSum;
    Rid _ftVp;

    void FrameStats(double delta)
    {
        if (_ftLeft < 0) return;
        if (_ftCol0 < 0)
        {
            _ftCol0 = GC.CollectionCount(0); _ftCol1 = GC.CollectionCount(1); _ftCol2 = GC.CollectionCount(2);
            _ftVp = GetViewport().GetViewportRid();
            RenderingServer.ViewportSetMeasureRenderTime(_ftVp, true);
        }
        else
        {
            // l'image d'avant : notre _Process, puis ce que le rendu a pris
            _ftCpuSum += _ftWatch.Elapsed.TotalMilliseconds;
            _ftGpuSum += RenderingServer.ViewportGetMeasuredRenderTimeGpu(_ftVp);
            _ftRsCpuSum += RenderingServer.ViewportGetMeasuredRenderTimeCpu(_ftVp);
        }
        if (_ft.Count == 30) _allocRun0 = GC.GetTotalAllocatedBytes();
        _ft.Add(delta * 1000);
        if (--_ftLeft > 0) return;
        var s = _ft.Skip(30).OrderBy(x => x).ToArray();      // les premières images chargent encore
        double P(double q) => s[(int)Math.Min(s.Length - 1, Math.Floor(q * (s.Length - 1)))];
        double med = P(0.5);
        int hitches = s.Count(x => x > 1.5 * med);
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        GD.Print(string.Format(inv,
            "images {0} : moyenne {1:F2} ms, médiane {2:F2}, p95 {3:F2}, p99 {4:F2}, max {5:F2} ; "
          + "au-delà de 1,5×médiane : {6} ; ramasse-miettes : gen0 {7}, gen1 {8}, gen2 {9}, "
          + "pause totale {10:F1} ms, allouées {11:F1} Mo",
            s.Length, s.Average(), med, P(0.95), P(0.99), s[^1], hitches,
            GC.CollectionCount(0) - _ftCol0, GC.CollectionCount(1) - _ftCol1, GC.CollectionCount(2) - _ftCol2,
            (GC.GetTotalPauseDuration() - _ftGc0).TotalMilliseconds, GC.GetTotalAllocatedBytes() / 1048576.0));
        int m = _ft.Count - 1;
        GD.Print(string.Format(inv, "par image, en moyenne : notre _Process {0:F2} ms, rendu côté processeur {1:F2} ms, carte graphique {2:F2} ms",
            _ftCpuSum / m, _ftRsCpuSum / m, _ftGpuSum / m));
        GD.Print($"gerbes : {_slams}, paquets d'embrun en l'air au plus : {_sprayMax}");
        GD.Print(string.Format(inv, "alloué par image : solveur {0:F0} o, toile {1:F0} o, tout _Process {2:F0} o, tout le programme pendant la course {3:F0} o",
            (double)_allocPhys / _allocFrames, (double)_allocSails / _allocFrames, (double)_allocProc / _allocFrames,
            (GC.GetTotalAllocatedBytes() - _allocRun0) / (double)(_ft.Count - 30)));
        GetTree().Quit();
    }

    /* LE CONTRÔLE DU CHAMP D'ÉCUME, en nombres et sans image. Il relit la cible
       que le GPU vient de rendre et recalcule la déferlante au processeur, en
       doubles, aux mêmes points du monde : là où une crête déferle franchement,
       le champ doit valoir AU MOINS autant, puisqu'il ne fait que garder le
       maximum. Un axe retourné ou une ancre décalée d'un pas s'y compteraient
       par centaines. */
    int _foamCheckIn = -1;
    double _foamT;
    Vector2 _foamOrigin;
    Vector3 _foamShip;

    void FoamCheck()
    {
        var img = _foam.Texture.GetImage();
        var rng = new Random(7);
        int strong = 0, bad = 0, lit = 0, n = 4000;
        double sum = 0, jacDrop = 0;
        for (int s = 0; s < n; s++)
        {
            int px = rng.Next(FoamField.Res), py = rng.Next(FoamField.Res);
            float v = img.GetPixel(px, py).R;
            sum += v;
            if (v > 0.05f) lit++;
            double x = _foamOrigin.X + (px + 0.5) / FoamField.Res * FoamField.Size;
            double z = _foamOrigin.Y + (py + 0.5) / FoamField.Res * FoamField.Size;
            /* THE SAME TEST AS foam_field.gdshader: the Jacobian of the Gerstner
               displacement under its threshold, shelter included. This used to be
               the older steepness rule (Q·k·A·sin against 0.66 and 1.05), which the
               shader had left behind: the check measured a breaking the field no
               longer drew. */
            double sh = _sea.Core.Shelter?.Invoke(x + _sea.Core.Origin.X, z + _sea.Core.Origin.Z) ?? 1.0;
            double jx = 0, jz = 0, jxz = 0;
            for (int i = 0; i < Config.NWavesFoam; i++)
            {
                ref Wave w = ref _sea.Core.Waves[i];
                double f = w.K * (w.Dx * x + w.Dz * z) - w.Omega * _foamT + w.Phase;
                double wq = w.Q * w.Amp * sh * w.K * Math.Sin(f);
                jx -= w.Dx * w.Dx * wq; jz -= w.Dz * w.Dz * wq; jxz -= w.Dx * w.Dz * wq;
            }
            double jac = (1 + jx) * (1 + jz) - jxz * jxz;
            jacDrop = Math.Max(jacDrop, 1 - jac);
            double jf = _settings.SeaJacobian;
            double e = Math.Clamp((jac - (jf - 0.06)) / ((jf - 0.3) - (jf - 0.06)), 0, 1);
            double brk = e * e * (3 - 2 * e) * 0.9;
            if (brk > 0.3) { strong++; if (v < brk - 0.05) bad++; }
        }
        /* L'ANNEAU DE LA COQUE, recalculé au processeur avec le MÊME profil :
           partout où un texel tombe sur sa flottaison, le champ doit valoir au
           moins ce que l'anneau y dépose. Cela prouve qu'il est POSÉ sur elle,
           et c'est tout : essayé avec l'étrave volontairement inversée, sur la
           goélette, il ne voit rien — l'anneau fait 2,2 m de large en dedans et
           les deux contours ne s'écartent guère de plus d'un mètre. L'avant et
           l'arrière tiennent aux conventions, pas à ce contrôle : station 0 à
           la voûte (halfB, t = 0), colonne 0 en u = 0, u = 1 à l'étrave (+z). */
        var prof = _prof;
        var bb = _ship.Physics.Body;
        Vec3d fw3 = bb.Quat.Rotate(new Vec3d(0, 0, 1));
        double fl = fw3.LengthXZ;
        double fx = fw3.X / fl, fz = fw3.Z / fl, rx = fz, rz = -fx;
        double halfL = _ship.Spec.L * 0.5;
        double way = Math.Clamp(bb.Vel.LengthXZ / 3.0, 0, 1);
        int ring = 0, ringBad = 0;
        double ringSum = 0;
        for (int px = 0; px < FoamField.Res; px++)
            for (int py = 0; py < FoamField.Res; py++)
            {
                double x = _foamOrigin.X + (px + 0.5) / FoamField.Res * FoamField.Size - _foamShip.X;
                double z = _foamOrigin.Y + (py + 0.5) / FoamField.Res * FoamField.Size - _foamShip.Z;
                if (Math.Abs(x) > halfL + 5 || Math.Abs(z) > halfL + 5) continue;
                double along = x * fx + z * fz, athw = x * rx + z * rz;
                double ta = along / halfL;
                double u = Math.Clamp(ta * 0.5 + 0.5, 0, 1) * prof.Fractions.Length - 0.5;
                int i0 = Math.Clamp((int)Math.Floor(u), 0, prof.Fractions.Length - 1);
                int i1 = Math.Min(i0 + 1, prof.Fractions.Length - 1);
                double fr = Math.Clamp(u - Math.Floor(u), 0, 1);
                double hb = (prof.Fractions[i0] * (1 - fr) + prof.Fractions[i1] * fr) * prof.MaxHalfB;
                double mid = (prof.EndAft + prof.EndFwd) * 0.5, hbody = (prof.EndFwd - prof.EndAft) * 0.5;
                double dx = Math.Abs(athw) - hb, dz = Math.Abs(along - mid) - hbody;
                double gap = Math.Sqrt(Math.Pow(Math.Max(dx, 0), 2) + Math.Pow(Math.Max(dz, 0), 2))
                           + Math.Min(Math.Max(dx, dz), 0);
                if (Math.Abs(gap) > 0.25) continue;
                ring++;
                float v = img.GetPixel(px, py).R;
                ringSum += v;
                // la bande vaut ~1 sur la ligne ; un peu de marge pour l'étalement
                if (v < 0.7 * (0.06 + way)) ringBad++;
            }
        GD.Print($"anneau de coque : {ring} texels sur sa flottaison, moyenne {ringSum / Math.Max(1, ring):F3} "
               + $"(attendu ≥ {0.06 + way:F3} à l'erre {way:F2}), en défaut {ringBad}");

        GD.Print($"champ d'écume : {img.GetFormat()}, moyenne {sum / n:F3}, "
               + $"texels écumeux {100.0 * lit / n:F1} %, déferlantes fortes {strong}, "
               + $"en défaut {bad}, jacobien le plus bas {1 - jacDrop:F3} (seuil {_settings.SeaJacobian:F2})");
        for (int i = 0; i < Config.NWavesFoam; i++)
        {
            ref Wave w = ref _sea.Core.Waves[i];
            GD.Print($"  vague {i} : amp {w.Amp:F2} m, lambda {2 * Math.PI / w.K:F0} m, Q {w.Q:F3}, Q.k.amp {w.Q * w.K * w.Amp:F3}");
        }
        GetTree().Quit();
    }

    static Vector3 ParseVec(string s)
    {
        var p = s.Split(',');
        return new Vector3(p[0].ToFloat(), p[1].ToFloat(), p[2].ToFloat());
    }

    void TickCapture()
    {
        if (_captureIn < 0) return;
        if (--_captureIn > 0) return;
        var img = GetViewport().GetTexture().GetImage();
        if (img == null || img.GetWidth() < 8) { GD.PushError("capture vide"); GetTree().Quit(1); return; }
        Error err = img.SavePng(_capturePath);
        var cv = _ship.Physics.Body.Vel;
        double ag = _ship.Physics.Aground;
        string ago = ag > 0 ? FormattableString.Invariant($", ÉCHOUÉE de {ag:F2} m") : "";
        GD.Print(err == Error.Ok
            ? FormattableString.Invariant(
                $"capture écrite : {_capturePath} (erre {cv.LengthXZ:F1} m/s{ago})")
            : $"capture ratée : {err}");
        GetTree().Quit(err == Error.Ok ? 0 : 1);
    }
}
