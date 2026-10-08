using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using NavalSim.Core;

namespace NavalSim;

/// <summary>Les grosses pièces : la bordée, les coups reçus, la soute (sorti de ShipDemo.cs).</summary>
public partial class ShipDemo
{
    // ------------------------------------------------------------------
    //  LES GROSSES PIÈCES — la bordée, les coups reçus, la riposte, la soute
    // ------------------------------------------------------------------

    Gunnery _gunnery = null!;
    GunFxNode _gunFx = null!;
    /// <summary>Une bouteille sur combien de naufrages (settings.json → wreck).</summary>
    int _bottleOneIn = 6;
    readonly Dictionary<ShipNode, ShotTarget> _targets = new();
    // les coques qu'on a provoquées : elles se retournent contre qui les a touchées
    readonly Dictionary<ShipNode, (ShipNode Foe, double Rearm)> _hostile = new();
    readonly Random _gunRng = new();
    /* LE BORD EN BATTERIE, un ÉTAT qu'on voit et non une touche dont il faut se
       souvenir : +1 tribord, −1 bâbord, +2 poupe, −2 proue — « l'autre » est le négatif. */
    int _gunSide = 1;
    bool _salvo;
    /* LES GROUPES : les deux bords, puis la chasse, qui se sert par bord elle
       aussi — une pièce de proue à tribord ne pointe pas là où celle de bâbord
       pointe. Une pièce dans l'axe garde le groupe sans bord (±2). */
    static readonly Dictionary<int, string> GunNames = new()
    {
        [1] = "tribord", [-1] = "bâbord",
        [-2] = "proue tribord", [-3] = "proue bâbord",
        [2] = "poupe tribord", [3] = "poupe bâbord"
    };

    ShotTarget TargetOf(ShipNode s)
    {
        if (_targets.TryGetValue(s, out var t)) return t;
        t = new ShotTarget { Physics = s.Physics, Battery = s.Battery, Shell = s.HullShell(),
                             Masts = s.MastBoxes, Sails = s.SailBoxes, Tag = s };
        // ce qu'un trou coûte à une voile est une règle d'artillerie : elle vient d'ici
        s.SailHoleLoss = _gunnery.Rules.SailHoleLoss;
        _targets[s] = t;
        // armée à sa première apparition : quarante charges par pièce, comme la page
        s.Physics.PowderMax = s.Battery.Guns.Count * 40;
        s.Physics.Powder = s.Physics.PowderMax;
        return t;
    }

    void WireGuns()
    {
        _gunnery.OnFire = (at, dir, k, floor, ph, gun) =>
        {
            _gunFx.Gun(at.ToGodot(), dir.ToGodot(), k, floor);
            // la flamme ici, le bruit quand il arrive : c'est le même événement
            // votre propre bordee part de sous vos pieds : aucune cloison entre elle et vous
            _sound?.Boom(at, k, ph == _ship.Physics);
            /* ET LA PIÈCE PART EN ARRIÈRE AU COUP, pas à l'ordre : une bordée
               s'égrène le long du bord, et c'est la mèche qui fait reculer, pas
               la main sur G (signalé). */
            if (_ship.Physics == ph) _ship.Recoil(gun, _gunnery.Clock);
            else foreach (var s in _others) if (s.Physics == ph) { s.Recoil(gun, _gunnery.Clock); break; }
            // et le cinéma, s'il tourne, se retourne vers le coup (ShipDemo.CineBattle.cs)
            CineShot(ph, at, dir);
            // qui a tiré tient ses sabords ouverts un moment (ShipDemo.Ports.cs)
            NoteFired(ph);
            // et qui l'entend fait branle-bas (ShipDemo.Readiness.cs)
            AlarmHeard(ph);
        };
        /* Un boulet fait un trou ÉTROIT dans l'eau très vite : une colonne haute et
           mince, pas un dôme — le volume est borné par la réserve d'embrun, pour
           qu'une bordée de six y tienne sans que les dernières volent les premières. */
        _gunnery.OnSplash = (at, water, speed, jet) => { _spray.Pool.Burst(at, water, speed, jet); AlarmSplash(at); };
        _gunnery.OnCreature = (a, b, shot) =>
        {
            if (_kraken.HitShot(a, b, out double u, out var arm))
            {
                var hit = a + (b - a) * u;
                _spray.Pool.Burst(hit, 10, 7, 1.4);
                _kraken.Wound(arm, shot.K);
                return true;
            }
            // le serpent aussi se touche : sur toute la longueur de son corps
            if (_serpent != null && _serpent.HitShot(a, b, out double us))
            {
                _spray.Pool.Burst(a + (b - a) * us, 10, 7, 1.4);
                _serpent.Wound(shot.K);
                return true;
            }
            return false;
        };
        _gunnery.OnStrike = Struck;
        // un spectre ne se touche que par un spectre, ou par celui qui s'est retourné contre vous
        _gunnery.CanHit = (from, t) => _ghosts.CanTouch(from, t.Physics);
    }

    /* CE QU'UN BOULET LUI COÛTE, par une mécanique qui existait déjà : un trou dans
       son flanc est la même voie d'eau que les autres — Torricelli, la carène
       liquide et l'envahissement prennent la suite sans une ligne pour l'artillerie ;
       un mât touché est la même blessure que la foudre. Rien du fait d'être canonné
       n'est un cas à part. */
    /// <summary>
    /// UNE GERBE DE BOULETS PAR LE TRAVERS, à la hauteur qu'on lui donne — de
    /// quoi éprouver ce qu'une toile fait d'un coup qui la traverse, sans
    /// attendre qu'une bataille veuille bien en tirer un au bon endroit.
    ///
    /// Cinq et non un : la toile roule avec la houle, et un boulet seul dit
    /// surtout si le navire penchait au bon moment. Échelonnés en station, ils
    /// disent ce que la toile fait, elle.
    /// </summary>
    void TestShot(double height)
    {
        var bb = _ship.Physics.Body;
        // le tireur doit être un AUTRE : un navire ne se tire pas dessus
        var from = _others.Count > 0 ? _others[0].Physics : _ship.Physics;
        for (int hz = -2; hz <= 2; hz++)
            _gunnery.Shots.Add(new Gunnery.Shot
            {
                P = new Vec3d(bb.Pos.X + 160, bb.Pos.Y + height, bb.Pos.Z + hz * 5),
                V = new Vec3d(-Ball.Muzzle, 0, 0), K = 1, C = 0.00097, From = from
            });
        GD.Print($"cinq boulets lâchés à {height:F1} m sur l'eau, 160 m par le travers");
    }

    /// <summary>
    /// DES TROUS DANS SA PROPRE TOILE, sans attendre qu un ennemi les y mette : au
    /// centre de chaque voile hissee, l une apres l autre, en repassant sur les
    /// memes quand on en demande plus qu il n y a de voiles.
    ///
    /// Comme le boulet d epreuve, il ATTEND que la toile soit etablie : au
    /// premier instant les voiles sont encore roulees sur leurs vergues, et les
    /// trous se seraient poses sur le rouleau.
    /// </summary>
    void TestHoles(int n)
    {
        for (int j = 0; j < n; j++)
        {
            var bs = _ship.SailBoxes();
            if (bs.Count == 0) { GD.Print("trous : aucune voile hissee"); break; }
            var bx = bs[j % bs.Count];
            /* SEMES et non tous au centre : vises au meme point, ils tombaient sur
               le meme sommet et ne faisaient qu un trou pour dix. */
            double fx = _stormRng.NextDouble(), fy = _stormRng.NextDouble(), fz = _stormRng.NextDouble();
            var mid = new Vector3((float)(bx.Min.X + (bx.Max.X - bx.Min.X) * (0.2 + 0.6 * fx)),
                                  (float)(bx.Min.Y + (bx.Max.Y - bx.Min.Y) * (0.2 + 0.6 * fy)),
                                  (float)(bx.Min.Z + (bx.Max.Z - bx.Min.Z) * (0.2 + 0.6 * fz)));
            int rr = _ship.HoleSail(bx.Sail, _ship.GlobalTransform * mid,
                                    _ship.GlobalTransform.Basis * new Vector3(1, 0, 0),
                                    _gunnery.Rules.SailHolesMax);
            GD.Print($"trou dans la voile {bx.Sail} : {(rr == -2 ? "elle s ouvre" : rr < 0 ? "manque" : rr + " trou(s)")}, toile entiere {_ship.Whole():F3}");
        }
    }

    void Struck(ShotTarget t, string kind, int index, double frac, double speed, double k, ShipPhysics from, Vec3d world, Vec3d dir)
    {
        if (t.Tag is not ShipNode s) return;
        CountHit(s, kind);
        // un coup reçu, toile comprise, met l'équipage aux postes (ShipDemo.Readiness.cs)
        if (s != _ship) Alarm(s, say: true);
        // un coup au but PORTÉ PAR NOUS : compté ici, dit une fois la bordée finie
        if (s == _ship)
        {
            Shout("touche", 0, 4);
            // et le dire : compté comme les nôtres, annoncé une fois la bordée retombée
            if (kind == "sail") _takenSail++; else if (kind == "mast") _takenMast++; else _takenHull++;
            _takenAt = _t;
        }
        if (from == _ship.Physics && s != _ship)
        {
            if (Allied(_ship, s)) _friendly++; else _hits++;
            _hitsAt = _t;
        }
        /* LA TOILE NE FAIT NI ÉCLAT NI FRACAS, et le boulet ne s'y arrête pas.
           Elle claque, et c'est tout : pas de bois arraché, pas de brèche, pas de
           pièce démontée — un trou, et la voile qui crache son vent par là. C'est
           pourquoi ce cas passe AVANT tout le reste, jusqu'au bruit du choc :
           faire craquer du chêne parce qu'un boulet a traversé un hunier serait
           un contresens qu'on entendrait. */
        if (kind == "sail")
        {
            int r = s.HoleSail(index, world.ToGodot(),
                               dir.ToGodot(),
                               _gunnery.Rules.SailHolesMax);
            if (r == -2)
            {
                Say(s == _ship ? "Une voile s'ouvre d'une ralingue à l'autre !"
                               : $"Une voile du {s.Spec.Name} s'ouvre en deux !");
            }
            // et ce qui est derrière la toile : la vergue, la hune, les haubans (GunnerySettings.SailMastWound)
            if (r != -1 && _stormRng.NextDouble() < _gunnery.Rules.SailMastWound * Math.Min(1, k) * Ball.Bite(speed, k))
            {
                bool tombe = s.WoundMast(s.MastOfSail(index));
                if (tombe && s == _ship) Shout("mat", 0.1, 5);
            }
            return;
        }
        // le choc s'entend de là où le boulet a porté, donc plus tard que la pièce
        // touchée chez SOI : le bois qui éclate est celui de la chambre où l'on est
        _sound?.Crash(world, k, speed, kind, s == _ship);
        // qui a tiré : c'est ce qui permet à un navire de savoir contre qui se retourner
        ShipNode? shooter = null;
        foreach (var (node, tt) in _targets) if (tt.Physics == from) { shooter = node; break; }
        // les spectres ne provoquent pas les vivants et n'en sont pas provoqués
        /* UN BOULET D'UN NAVIRE DE SON PAVILLON NE FAIT PAS UN ENNEMI. C'est la
           règle de l'escarmouche, et elle vaut partout : entre gens du même bord,
           un coup au but est une maladresse, pas une déclaration. */
        /* ET CELUI QUI TIRE LE DERNIER DEVIENT L'ENNEMI, même si l'on en avait
           déjà un. La condition portait « pas déjà hostile », ce qui paraissait
           prudent et ne l'était pas : un navire engagé contre un autre encaissait
           les bordées d'un TIERS sans jamais lever la tête. C'est exactement ce
           qu'on voit d'une paire aux prises — on canonne le pirate qui s'acharne
           sur un marchand, et il continue comme si de rien n'était (signalé).

           Le compte à rebours de la bordée est CONSERVÉ quand l'ennemi change :
           sans cela, changer de cible offrirait un tir gratuit à chaque coup
           reçu. Et l'on n'écrit rien quand c'est le même — inutile de remettre
           la pendule à zéro soixante fois par seconde.

           Le risque assumé : deux agresseurs qui frappent tour à tour se
           renvoient sa cible comme une balle. Cela se verra en escarmouche avant
           de se voir en mer, où l'on est rarement deux à canonner le même. */
        if (shooter != null && s != _ship && shooter != s && !_pirates.ContainsKey(s)
            && !s.IsGhost && !shooter.IsGhost && !Allied(s, shooter))
        {
            _hostile.TryGetValue(s, out var deja);
            if (deja.Foe != shooter) _hostile[s] = (shooter, deja.Rearm);
        }
        /* ET UN PIRATE QU'ON CANONNE SE RETOURNE. Il n'est pas au registre des
           hostiles — on le croyait capable de se défendre seul —, mais il ne
           lisait rien de ce qui lui tombait dessus : sa proie se choisit à la
           proximité et se garde jusqu'au naufrage. On l'attaquait donc sans qu'il
           lève la tête (signalé). Entre gens du même pavillon noir, en revanche,
           un coup au but reste une maladresse. */
        if (shooter != null && s != shooter && !s.IsGhost && !shooter.IsGhost
            && !Allied(s, shooter) && _pirates.TryGetValue(s, out var corsaire))
            corsaire.Provoke(from, _t);

        var w = world.ToGodot();
        // le bois d'abord, quoi qu'on ait touché : le même événement vu du dehors
        _splinters.Splinters(w, dir.ToGodot(), k);
        /* CE QU'IL LUI RESTE DE SON ÉLAN — toute la règle de la distance est là
           (Ball.Bite), et rien d'autre dans ce fichier n'en sait quoi que ce soit. */
        double bite = Ball.Bite(speed, k);

        if (kind == "mast")
        {
            // un bas mât faisait un pied de chêne : il en faut plusieurs, c'est la récompense du feu soutenu
            bool tombe = s.WoundMast(index);
            // et s'il tombe chez nous, le pont l'apprend avant le capitaine
            if (tombe && s == _ship) Shout("mat", 0.1, 5);
            return;
        }
        /* LE TROU D'UN BOULET, et comme le CARRÉ du calibre — un trou est une
           surface. La section du boulet au plein calibre (GunnerySettings.ShotHole,
           0,012 m²) : il valait 0,1 m², puis 0,025, et les navires coulaient encore
           avant de perdre un mât (signalé deux fois). Au bordé du côté TOUCHÉ, et le
           charpentier le bouchera (ShipPhysics.Plug). */
        var b = s.Physics.Body;
        var local = b.Quat.Inverted().Rotate(world - b.Pos);
        /* Et le trou va comme l'énergie QUI RESTE : entier à bout portant, les
           deux tiers à deux cents mètres, la moitié au bout du plein fouet. */
        s.Physics.MakeBreach(index, _gunnery.Rules.ShotHole * k * k * bite, Math.Clamp(frac, 0, 1), local.X);
        /* ET PARFOIS LE FEU. Un boulet froid n'allume rien par lui-même ; ce qui
           prend est ce qu'il CREVE en passant — une gargousse qu'on portait, une
           lanterne de batterie, un baril de brai. D'où la chance, faible, et
           d'autant plus faible que le coup vient de loin : un boulet mourant
           traverse le bordé sans rien renverser derrière. */
        if (_stormRng.NextDouble() < _fireRules.ShotChance * k * bite)
            LightFire(s, local, 0.10);
        // la marque dans le bordé, qui s'aggrave si l'on retape au même endroit
        s.Scar(w, k);
        // et les pièces qui étaient derrière le bordé
        var down = s.Battery.Wound(local, k, s.Spec.L, bite);
        if (down.Count > 0 && s == _ship)
        {
            int side = down[0].Side;
            var (ok, all) = s.Battery.Count(side);
            string where = Math.Abs(side) == 2 ? "en " + GunNames[side] : "à " + GunNames[side];
            Say((down.Count > 1 ? down.Count + " pièces démontées " : "Pièce démontée ") + where
                + (ok > 0 ? $" · {ok}/{all} en état" : " · plus une pièce en état"));
        }
    }

    /* TIRER — un appui, une pièce : la batterie parcourue de l'avant à l'arrière ;
       la touche TENUE, la bordée entière, une seule fois par appui. La poudre se
       consomme sur ce que les pièces ont RÉELLEMENT tiré. */
    void Fire(bool other, bool held)
    {
        var ph = _ship.Physics;
        var bat = _ship.Battery;
        TargetOf(_ship);
        if (bat.Guns.Count == 0) { Say("Ce navire ne porte pas de batterie"); return; }
        // la hausse réglée à la pièce : un navire neuf a des pièces neuves, à zéro
        foreach (var g in bat.Guns) { g.Hausse = _hausse.GetValueOrDefault(g.Side); g.Train = _train.GetValueOrDefault(g.Side); _ship.AimPiece(g); }
        if (ph.Powder <= 0) { Say("Plus une charge en soute"); return; }
        int side = other ? -_gunSide : _gunSide;
        if (!bat.Has(side)) { Say("Aucune pièce en " + GunNames[side]); return; }
        if (bat.Count(side).Ok == 0) { Say("Plus une pièce en état · " + GunNames[side]); return; }
        // sabords fermés : l'ordre attend qu'ils s'ouvrent, et on rend le reste au combat (ShipDemo.Ports.cs)
        if (!_ship.PortsReady(side))
        {
            bool already = _fireWhenOpen != null;
            _fireWhenOpen = (other, held || (_fireWhenOpen?.Held ?? false));
            _portsOrder = null;
            if (!already) Say("Ouvrez les sabords ! Le feu à la mise en batterie");
            return;
        }
        /* L'ORDRE DE PARER RETIENT LE FEU. Tant que tout le bord n'est pas
           chargé, rien ne part — c'est cela qu'on a demandé aux servants, et
           c'est ce qui coûte : on attend la plus lente. Une pièce démontée ne
           compte pas dans le total, sinon un bord amoindri ne tirerait jamais. */
        if (_layOrder)
        {
            var L0 = _gunnery.Loaded(bat, side);
            if (L0.Ready < L0.All)
            {
                double wait = _gunnery.AllReadyIn(bat, side);
                Say($"Les servants parent leurs pièces · {L0.Ready}/{L0.All}"
                    + (wait > 0.5 ? $" — parées dans {Math.Ceiling(wait)} s" : ""));
                return;
            }
        }
        // parée, la bordée part d'un coup, qu'on ait tenu la touche ou non
        int fired = held || _layOrder
            ? _gunnery.Broadside(bat, side, ph, ph.Powder, _layOrder)
            : _gunnery.FireOne(bat, side, ph);
        if (fired == 0)
        {
            var L = _gunnery.Loaded(bat, side);
            Say("Pièces en rechargement · " + GunNames[side]
                + (double.IsFinite(L.Next) ? $" — la première dans {Math.Ceiling(L.Next)} s" : ""));
            return;
        }
        ph.Powder = Math.Max(0, ph.Powder - fired);
        Shout("feu", 0, 1.5);
    }

    /* LA COLONNE ANNONCE SON BORD, et ce qu'il en reste dès qu'il en manque :
       « tribord 4/6 · 3 prêtes sur 6 ». Un compte plein ne se lit pas. */
    string GunLine()
    {
        var bat = _ship.Battery;
        if (bat.Guns.Count == 0) return "";
        var (ok, all) = bat.Count(_gunSide);
        var L = _gunnery.Loaded(bat, _gunSide);
        string hausse = HausseTag(_gunSide);
        return $"pièces     {GunNames[_gunSide]}{hausse}" + (all > 0 && ok < all ? $" {ok}/{all}" : "")
             + (L.All > 0 && L.Ready < L.All ? $" · {L.Ready} prête{(L.Ready > 1 ? "s" : "")} sur {L.All}" : "")
             + $" · {_ship.Physics.Powder} charges\n";
    }

    void CycleGunSide()
    {
        int[] order = { 1, -1, -2, -3, 2, 3 };
        int i = Array.IndexOf(order, _gunSide);
        for (int n = 1; n <= 4; n++)
        {
            int s = order[(i + n) % 4];
            if (_ship.Battery.Has(s)) { _gunSide = s; break; }
        }
        Say("En batterie : " + GunNames[_gunSide]);
        // la proue se sert à l œil ; la poupe ne déplace pas la caméra
        SetGunPost(Served(_gunSide) ? _gunSide : (int?)null);
        GunSideTick();
        HudTick();
    }

    /* SERVIR LES PIÈCES d'une coque provoquée. Le bord est choisi sur le relèvement :
       elle ne tire que si la cible relève franchement par le travers — une pièce
       pointe en travers, et cela la fait manœuvrer au lieu de mitrailler. Trois cent
       quarante mètres, là où s'arrête le plein fouet. Un capitaine attend que la
       plus grande part de son bord soit prête. */
    void ServeGuns(ShipNode s, double dt)
    {
        var ph = s.Physics;
        if (ph.Foundered || ph.Powder <= 0 || s.Battery.Guns.Count == 0) return;
        _rearm.TryGetValue(s, out double rearm);
        rearm = Math.Max(0, rearm - dt);
        _rearm[s] = rearm;
        var foe = EnemyOf(s);
        if (foe == null || rearm > 0) return;
        // pris par surprise, il fait d'abord son branle-bas (ShipDemo.Readiness.cs)
        if (!Cleared(s)) return;
        var b = ph.Body;
        var to = foe.Body.Pos - b.Pos;
        to = new Vec3d(to.X, 0, to.Z);
        double range = to.Length;
        if (range > 340 || range < 12) return;
        var fwd = b.Quat.Rotate(new Vec3d(0, 0, 1));
        var starboard = fwd.Cross(new Vec3d(0, 1, 0));          // tribord vrai
        double abeam = to.Normalized().Dot(starboard);
        if (Math.Abs(abeam) < 0.62) return;                    // elle n'a pas le bord
        int side = abeam > 0 ? 1 : -1;
        // et l'on ne tire pas à travers un mantelet fermé
        if (s.HasPortLids && !s.PortsReady(side)) return;
        var L = _gunnery.Loaded(s.Battery, side);
        if (L.Ready < Math.Max(1, (int)Math.Ceiling(0.6 * L.All))) return;
        int n = _gunnery.Broadside(s.Battery, side, ph, ph.Powder);
        if (n > 0) { ph.Powder -= n; _rearm[s] = 2 + _gunRng.NextDouble() * 3; }
    }
    readonly Dictionary<ShipNode, double> _rearm = new();
    void GunTick(double dt)
    {
        _gunnery.Targets.Clear();
        _gunnery.Targets.Add(TargetOf(_ship));
        foreach (var s in _others) { _gunnery.Targets.Add(TargetOf(s)); ServeGuns(s, dt); }
        _gunnery.Update(dt, _sea.Core, _t);
        var h = _sky.Core.Horizon;
        _gunFx.Step(dt, _sea.Core.WindVec, new Vec3d(h.R, h.G, h.B), _gunnery.Shots);
    }

    /* LA SOUTE. Trois charges, pas une, à quelques mètres et quelques dixièmes de
       seconde l'une de l'autre — au milieu d'abord, puis vers l'avant, puis bien
       à l'arrière —, posées dans SON repère. Puis elle s'ouvre à la mer, et ses mâts
       partent : une soute qui l'ouvre d'un bout à l'autre ne laisse pas trois
       bâtons debout. */
    void BlowUp(ShipNode s)
    {
        var ph = s.Physics;
        var b = ph.Body;
        double h = s.Spec.CeHeight, along = Math.Min(9, s.Spec.L * 0.15);
        (double Delay, Vec3d P, double S)[] shots =
        {
            (0.00, new Vec3d(0.0, h * 0.30, 0.0), 1.00),
            (0.32, new Vec3d(1.9, h * 0.55, along), 0.78),
            (0.66, new Vec3d(-1.6, h * 0.42, -along * 0.8), 0.90)
        };
        foreach (var (delay, p, sz) in shots)
        {
            var w = b.Quat.Rotate(p) + b.Pos;
            _gunFx.BlastIn(delay, w.ToGodot(), s.Spec.L * sz);
            /* ET ON L'ENTEND, au même décalage que la flamme : elle sautait en
               SILENCE, ce que personne n'avait relevé parce qu'on la regarde. */
            _sound?.Blast(w, s.Spec.L / 40, delay);
            // et le coup de bélier, au même retard que le fracas : la taille du navire fait la charge
            Shock(w, s.Spec.L / 30 * sz, delay);
        }
        ph.BlowUp();
        s.DropAllMasts();
        if (s == _ship) Say("La soute saute !");
        // et le navire se rompt là où elle était (ShipDemo.Breakup.cs)
        BreakUp(s);
    }
}
