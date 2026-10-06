using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using NavalSim.Core;

namespace NavalSim;

/// <summary>Les caméras : la vue fixe, les vues à bord, la pièce, le ponton (sorti de ShipDemo.cs).</summary>
public partial class ShipDemo
{
    /* LA VUE FIXE de camera-rig.js (mode 3). Plantée dans le monde, position et
       relèvement verrouillés, pointée sur elle UNE fois puis laissée tranquille :
       elle s'éloigne et sort du champ, et c'est tout l'intérêt — c'est la seule
       vue où l'on voit le navire AVANCER par rapport à une mer immobile, au
       lieu d'une mer qui défile sous une coque clouée au centre de l'image. */
    // 0 orbite, 1 à bord (les vues de la fiche), 2 fixe — l'ordre du bouton de la page
    int _camMode;
    bool _fixed => _camMode == 2;
    /// <summary>De combien l'œil de la vue mi-eau est relevé au-dessus de la houle locale.</summary>
    float _splitLift = 0.05f;
    Vec3d _anchor;
    double _fixYaw, _fixPitch;

    /// <summary>
    /// Prendre la station : sur sa hanche, à 26 m par le travers et 34 m en
    /// arrière pour un navire de 24 m, en proportion pour les autres — un
    /// bâtiment qui s'éloigne en diagonale rapetisse au lieu de traverser le
    /// cadre et d'en sortir tout droit.
    ///
    /// La hauteur est prise sur la MER sous la station, pas sur la coque, puis
    /// n'en bouge plus : c'est un pied posé, pas une bouée. Par creux extrême une
    /// crête peut donc passer devant l'objectif, comme dans l'original.
    /// </summary>
    void Plant()
    {
        var b = _ship.Physics.Body;
        double k = _ship.Spec.L / 24;
        /* (1,0,0) local, comme l'original. Son commentaire dit « tribord », mais
           tribord est −x local dans ce projet : c'est donc la hanche BÂBORD.
           Porté tel quel, signalé plutôt que corrigé en silence. */
        Vec3d r = b.Quat.Rotate(new Vec3d(1, 0, 0));
        Vec3d f = b.Quat.Rotate(new Vec3d(0, 0, 1));
        double ax = b.Pos.X + r.X * 26 * k - f.X * 34 * k;
        double az = b.Pos.Z + r.Z * 26 * k - f.Z * 34 * k;
        double ay = _sea.Core.Sample(ax, az, _t) + 12 * k;
        _anchor = new Vec3d(ax, ay, az);

        double dx = b.Pos.X - ax, dz = b.Pos.Z - az;
        double dy = b.Pos.Y + 2 * k - ay;
        _fixYaw = Math.Atan2(dx, dz);
        _fixPitch = Math.Atan2(dy, Math.Sqrt(dx * dx + dz * dz));
    }

    // ------------------------------------------------------------------
    //  LES VUES À BORD — camera.decks de la fiche, mode 2 de camera-rig.js
    // ------------------------------------------------------------------

    /* SES PROPRES POINTS DE VUE, dans SA fiche : la passerelle, la chambre du
       capitaine, ce que le modéliste a prévu de montrer. Chaque vue donne l'œil
       dans le repère du navire et où il regarde ; le regard est pris dans ce
       repère, donc la vue suit le pont. Le glisser regarde autour À PARTIR de ce
       regard, la molette change la focale, qui est rendue en sortant. */
    int _deck;
    double _bridgeYaw, _bridgePitch;
    const float OutsideFov = 55, OutsideNear = 0.05f;

    /* SERVIR UNE PIÈCE DE CHASSE : la choisir dans le panneau des bordées MET
       L'ŒIL DERRIÈRE ELLE, dans son axe — on ne pointe pas un canon de chasse
       sans le voir. Ce n'est pas une vue de la fiche : elle n'a rien à y faire,
       puisqu'elle suit la pièce. Changer de vue (C) ou reprendre un bord la
       rend. */
    int? _gunPost;
    static readonly NavalSim.Core.DeckView PostView = new()
    { Y = 0.55, Z = 2.2, Pitch = -2, Fov = 45, Near = 0.08 };

    /// <summary>La hausse de chaque groupe de pièces, en radians au-dessus du but en blanc : elle reste réglée quand on quitte la pièce.</summary>
    readonly Dictionary<int, double> _hausse = new();
    /// <summary>Ce que le coin sous la culasse permet : de cinq degrés sous le but en blanc à douze au-dessus.</summary>
    const double HausseMin = -5 * Math.PI / 180, HausseMax = 12 * Math.PI / 180;
    /// <summary>Le pointage en direction de chaque groupe (positif : vers bâbord), et ce que l'embrasure en laisse.</summary>
    readonly Dictionary<int, double> _train = new();
    /// <summary>Une pièce de chasse se tourne de quinze degrés dans son embrasure ; une pièce de bordée, de huit à l'anspect.</summary>
    static double TrainMax(int side) => (Math.Abs(side) >= 2 ? 15 : 8) * Math.PI / 180;

    /// <summary>Les groupes qu'on sert à l'œil : les deux bordées et la proue — pas la poupe.</summary>
    static bool Served(int side) => side is 1 or -1 || side <= -2;

    /// <summary>Tourner la pièce servie sur son affût, et la dire. L'œil du servant tourne avec elle.</summary>
    void NudgeTrain(int side, double d)
    {
        double a = Math.Clamp(_train.GetValueOrDefault(side) + d, -TrainMax(side), TrainMax(side));
        _train[side] = a;
        if (_gunPost == side) _bridgeYaw = a;
        foreach (var g in _ship.Battery.Guns) if (g.Side == side) { g.Train = a; _ship.AimPiece(g); }
        UpdateInfo();
    }

    /// <summary>Le site de la ligne de mire de ce groupe contre l'horizon : son but en blanc, plus sa hausse.</summary>
    double SightOf(int side)
    {
        double over = 3;
        var b = _ship.Physics.Body;
        foreach (var g in _ship.Battery.Guns)
            if (g.Side == side && !g.Out) { over = Math.Max(0, g.P.Y + b.Pos.Y); break; }
        return Gunnery.PointBlank(over) + _hausse.GetValueOrDefault(side);
    }

    /// <summary>« · +4,0° » sur le bouton d'un groupe de chasse levé ou servi ; rien sinon.</summary>
    string HausseTag(int side)
    {
        if (!Served(side)) return "";
        double hz = _hausse.GetValueOrDefault(side) * 180 / Math.PI;
        double tr = _train.GetValueOrDefault(side) * 180 / Math.PI;
        if (_gunPost != side && Math.Abs(hz) < 0.05 && Math.Abs(tr) < 0.5) return "";
        string s = FormattableString.Invariant($" · {(hz >= 0 ? "+" : "−")}{Math.Abs(hz):F1}°");
        if (Math.Abs(tr) >= 0.5) s += FormattableString.Invariant($" · {Math.Abs(tr):F0}° {(tr > 0 ? "bâbord" : "tribord")}");
        return s.Replace('.', ',');
    }

    /// <summary>Lever ou baisser la pièce servie, et la dire.</summary>
    void NudgeHausse(int side, double d)
    {
        double h = Math.Clamp(_hausse.GetValueOrDefault(side) + d, HausseMin, HausseMax);
        _hausse[side] = h;
        foreach (var g in _ship.Battery.Guns) if (g.Side == side) { g.Hausse = h; _ship.AimPiece(g); }
        UpdateInfo();
    }

    void SetGunPost(int? side)
    {
        if (_gunPost == side) return;
        _gunPost = side;
        if (side is int s && GunEye(s, PostView) != null)
        {
            SetLens((float)(PostView.Fov ?? OutsideFov), (float)(PostView.Near ?? OutsideNear));
            // l'œil derrière la pièce comme elle est pointée
            _bridgeYaw = _train.GetValueOrDefault(s); _bridgePitch = 0;
            Say("À la pièce de " + GunNames[s]);
        }
        else
        {
            _gunPost = null;
            if (_camMode == 1) EnterDeck();
            else SetLens(OutsideFov, OutsideNear);
        }
        UpdateInfo();
    }

    string CamName() => _gunPost is int post ? "Pièce de " + GunNames[post] : _camMode switch
    {
        1 => _ship.Spec.Decks[_deck].Name,
        2 => "Fixe",
        3 => "Mi-eau",
        4 => "Ponton",
        CamFlyBy => "Fly-By",
        CamBell => "Cloche",
        _ => "Orbite"
    };

    /* ------------------------------------------------------------------ */
    /*  LA VUE DU PONTON                                                   */
    /* ------------------------------------------------------------------ */

    /// <summary>À quelle distance d'un ponton la vue est offerte, en mètres.</summary>
    const double JettyView = 800;

    /// <summary>
    /// Hauteur de l'œil au-dessus du tablier. Le tablier lui-même est à
    /// <see cref="NavalSim.Core.Berth.DeckY"/> : la somme fait un homme debout sur
    /// le musoir, et c'est le seul point de vue du jeu qui ne soit pas à bord.
    /// </summary>
    const double JettyEye = 1.65;

    /// <summary>
    /// SON REGARD, QUI EST LE SIEN. La vue du ponton suivait le navire à chaque
    /// image : on ne pouvait ni regarder le port, ni voir arriver autre chose, ni
    /// simplement laisser le navire sortir du cadre — et un homme debout sur un
    /// musoir ne tourne pas la tête au millimètre pour ne pas lâcher un bateau des
    /// yeux. Elle a donc son cap et son inclinaison, que la souris mène.
    ///
    /// ELLE S'OUVRE SUR LE NAVIRE et s'en détache ensuite : entrer dans une vue
    /// qui regarde une direction quelconque oblige à chercher où l'on est avant de
    /// pouvoir s'en servir. On vise au premier instant, puis on lâche.
    /// </summary>
    double _jettyYaw, _jettyPitch;

    /// <summary>
    /// LE PONTON OÙ L'ON EST MONTÉ, et dont on ne bouge plus tant qu'on y reste.
    ///
    /// Relire le plus proche à chaque image était juste tant que la vue suivait le
    /// navire : le point de vue avait beau sauter d'un musoir à l'autre, il
    /// montrait toujours la même chose. Le regard devenu libre, le saut se voit —
    /// on se retrouve ailleurs en regardant ailleurs, sans avoir rien fait. On
    /// retient donc le ponton choisi en entrant.
    ///
    /// EN MÈTRES MONDE VRAIS, comme tout ce qui est « du monde » : un point local
    /// se périmerait au premier glissement de l'origine.
    /// </summary>
    (Vec3d Head, string Name)? _jettyHeld;

    /// <summary>
    /// LE PONTON LE PLUS PROCHE, s'il est à portée — sa tête, en mètres MONDE
    /// VRAIS, et le nom de son port.
    ///
    /// Un port n'a pas forcément de ponton (Kingston est sur la rive d'en face,
    /// et n'en a pas) : ceux-là ont une tête à l'origine, et c'est ainsi que le
    /// reste du jeu les écarte déjà — on lit la même condition ici plutôt que
    /// d'en inventer une seconde.
    /// </summary>
    /// <summary>Ce ponton-là est-il encore à portée du navire ?</summary>
    bool InJettyView(Vec3d head)
    {
        var (tx, tz) = TruePos();
        double dx = head.X - tx, dz = head.Z - tz;
        return dx * dx + dz * dz < JettyView * JettyView;
    }

    (Vec3d Head, string Name)? NearJetty()
    {
        if (_world == null) return null;
        var (tx, tz) = TruePos();
        double best = JettyView * JettyView;
        (Vec3d, string)? found = null;
        foreach (var i in _world.Isles)
        {
            var w = i.Port;
            if (w.Hx == 0 && w.Hz == 0) continue;
            double dx = w.Hx - tx, dz = w.Hz - tz;
            double d2 = dx * dx + dz * dz;
            if (d2 < best) { best = d2; found = (new Vec3d(w.Hx, 0, w.Hz), i.Name); }
        }
        return found;
    }

    /// <summary>
    /// C : Orbite, puis chaque vue à bord de la fiche dans l'ordre, puis Fixe —
    /// le cycle du bouton caméra de la page, qui parcourt ses vues à bord avant de
    /// passer au mode suivant. La Proue de la page n'est pas portée.
    /// </summary>
    void CycleCamera()
    {
        /* LA LUNETTE TIENT LA VUE : elle est à l œil, et changer de poste sous la
           lunette reviendrait à se téléporter le verre collé à l œil (signalé). */
        if (_glassUp) { Say("Baissez d abord la lunette"); return; }
        LeaveCinema();                    // C choisit une autre vue : le cinéma s'arrête là
        SetGunPost(null);                 // changer de vue quitte la pièce
        DryLens();
        // depuis la cloche, on revient à l'orbite ; la cloche reste au bout de son câble
        if (_camMode == CamBell) { _camMode = 0; SetLens(OutsideFov, OutsideNear); }
        else if (_camMode == 0) { _camMode = 1; _deck = 0; EnterDeck(); }
        else if (_camMode == 1 && _deck + 1 < _ship.Spec.Decks.Count) { _deck++; EnterDeck(); }
        else if (_camMode == 1) { _camMode = 2; SetLens(OutsideFov, OutsideNear); Plant(); }
        else if (_camMode == 2) { _camMode = 3; SetLens(OutsideFov, OutsideNear); }
        /* LE PONTON N'EST DANS LE CYCLE QUE S'IL EST LÀ. Une vue qu'on propose au
           large montrerait la mer vide depuis un musoir à vingt milles ; et la
           sauter en silence vaut mieux qu'un refus, parce qu'une touche qui ne
           fait rien se lit comme une panne. */
        else if (_camMode == 3 && NearJetty() is { } j)
        {
            _camMode = 4; SetLens(OutsideFov, OutsideNear);
            _jettyHeld = j;                 // et l'on n'en bougera plus
            AimJetty(j.Head);
            Say("Du ponton de " + j.Name);
        }
        // le drone ferme le cycle : après le ponton, ou après la vue mi-eau quand il n'y a pas de ponton
        else if (_camMode != CamFlyBy) { EnterFlyBy(); Say("Fly-By"); }
        // la cloche ferme le cycle, quand elle est à l'eau
        else if (_bellOut) { _camMode = CamBell; SetLens(70, OutsideNear); }
        else _camMode = 0;
        UpdateInfo();
    }

    /* Entrer dans une vue : regard remis droit devant ELLE, sa focale et son plan
       proche. Un intérieur en demande un bien plus court que la mer : une
       cloison à portée de main serait coupée net. */
    void EnterDeck()
    {
        var v = _ship.Spec.Decks[_deck];
        _bridgeYaw = 0; _bridgePitch = 0;
        DryLens();
        SetLens((float)(v.Fov ?? OutsideFov), (float)(v.Near ?? OutsideNear));
    }

    void SetLens(float fov, float near) { _cam.Fov = fov; _cam.Near = near; }

    /// <summary>
    /// Poser le regard du ponton sur le navire — une fois, en y entrant. Le cap
    /// se prend sur le vecteur qui va de la tête du ponton à la coque, comme tout
    /// cap dans ce projet, et non sur un angle d'Euler.
    /// </summary>
    void AimJetty(Vec3d head)
    {
        var o = _sea.Core.Origin;
        double dx = _ship.Position.X - (head.X - o.X);
        double dz = _ship.Position.Z - (head.Z - o.Z);
        double dy = _ship.Position.Y - (NavalSim.Core.Berth.DeckY + JettyEye);
        _jettyYaw = Math.Atan2(dx, dz);
        _jettyPitch = Math.Clamp(Math.Atan2(dy, Math.Sqrt(dx * dx + dz * dz)), -0.9, 0.9);
    }

    /* UN PAS DE CÔTÉ, LA TÊTE QUI SE PENCHE (demandé : la vue de la dunette souvent
       cachée par le fanal arrière, celle de la vigie par le mât). On ne reste pas
       planté derrière ce qui bouche la vue : on se penche. L'œil suit la souris de
       gauche à droite — sa POSITION sur l'écran, sans bouton, la capture étant muette
       sur ces écrans —, de part et d'autre de la place écrite dans la fiche, en
       travers du REGARD (on s'écarte de ce qu'on a devant soi, pas du bord du navire),
       et avec le temps d'une tête qui bouge. Soixante centimètres de chaque côté, ce
       que donne un corps qui se penche sans lâcher prise ; reglages.ini le règle
       (« Pas de côté de l'œil », 0 l'ôte). */
    const float LeanReach = 0.6f;
    float _lean, _leanTest = float.NaN;

    Vector3 DeckLean(double yaw)
    {
        var vp = GetViewport();
        float w = vp.GetVisibleRect().Size.X;
        float u = !float.IsNaN(_leanTest) ? _leanTest : w > 1 ? Mathf.Clamp(vp.GetMousePosition().X / w * 2 - 1, -1, 1) : 0;
        // un rien au milieu de l'écran ne bouge pas : la souris posée au centre tient l'œil à sa place
        u = Mathf.Sign(u) * Mathf.Clamp((Mathf.Abs(u) - 0.08f) / 0.92f, 0, 1);
        float want = u * LeanReach * _settings.DeckLean;
        _lean += (want - _lean) * (1 - Mathf.Exp(-(float)GetProcessDeltaTime() * 6));
        // la droite de l'écran, dans le repère du navire : tribord est −x
        return new Vector3((float)-Math.Cos(yaw), 0, (float)Math.Sin(yaw)) * _lean;
    }

    void DeckCamera()
    {
        var spec = _ship.Spec;
        var v = spec.Decks[_deck];
        /* DERRIÈRE LA PIÈCE, DANS SON AXE : la vue d'une pièce se pose sur elle
           et non sur des coordonnées écrites à la main — un canon de chasse
           ouvert de 38° emmène sa vue avec lui. */
        if (v.Gun is int side && GunEye(side, v) is { } poste)
        {
            var xf0 = _ship.GlobalTransform;
            _cam.Position = xf0 * poste.At;
            _cam.LookAt(xf0 * (poste.At + poste.Dir * 120f), Vector3.Up);
            return;
        }
        double k = spec.L / 24;
        // l'œil : hauteur au-dessus de la flottaison ; absente, l'ancienne règle de la passerelle
        double x = v.X ?? (v.XFrac ?? 0) * spec.B;
        double z = v.Z ?? (v.ZFrac ?? spec.Camera.HelmZFrac ?? -0.4) * spec.L;
        double y = v.Y ?? spec.DeckMid + 2.1 * k;
        var eye = new Vector3((float)x, (float)y, (float)z);
        double yaw = v.Yaw * Math.PI / 180 + _bridgeYaw, pitch = v.Pitch * Math.PI / 180 + _bridgePitch;
        double cp = Math.Cos(pitch);
        var dir = new Vector3((float)(Math.Sin(yaw) * cp), (float)Math.Sin(pitch), (float)(Math.Cos(yaw) * cp));
        eye += DeckLean(yaw);
        var xf = _ship.GlobalTransform;
        _cam.Position = xf * eye;
        _cam.LookAt(xf * (eye + dir * (float)(120 * k)), Vector3.Up);
    }

    /// <summary>
    /// L'ŒIL D'UN SERVANT : sur l'axe du tube, deux mètres derrière la bouche et
    /// un demi-mètre au-dessus, regard le long de la pièce (plus le débattement
    /// que la souris a donné). Nulle si la coque n'a pas cette pièce.
    /// </summary>
    (Vector3 At, Vector3 Dir)? GunEye(int side, NavalSim.Core.DeckView v)
    {
        // DERRIÈRE LA PIÈCE DU MILIEU de la batterie : une bordée se pointe depuis son centre
        Gun? pick = null; double zMid = 0; int cnt = 0;
        foreach (var g in _ship.Battery.Guns) if (g.Side == side && !g.Out) { zMid += g.P.Z; cnt++; }
        if (cnt > 0) zMid /= cnt;
        foreach (var g in _ship.Battery.Guns)
            if (g.Side == side && !g.Out && (pick == null || Math.Abs(g.P.Z - zMid) < Math.Abs(pick.P.Z - zMid))) pick = g;
        foreach (var g in _ship.Battery.Guns)
        {
            if (g != pick) continue;
            var dir = new Vector3((float)g.Dir.X, 0, (float)g.Dir.Z).Normalized();
            double yaw = (v.Yaw + _bridgeYaw * 180 / Math.PI) * Math.PI / 180;
            if (yaw != 0)
            {
                double c = Math.Cos(yaw), s = Math.Sin(yaw);
                dir = new Vector3((float)(dir.X * c + dir.Z * s), 0, (float)(-dir.X * s + dir.Z * c)).Normalized();
            }
            double pitch = (v.Pitch) * Math.PI / 180 + _bridgePitch;
            var look = new Vector3((float)(dir.X * Math.Cos(pitch)), (float)Math.Sin(pitch), (float)(dir.Z * Math.Cos(pitch)));
            /* DERRIÈRE LA CULASSE, SUR L'AXE DU TUBE (signalé : décalé quand on la pointait).
               La pièce tourne autour de son MILIEU (son pivot) ; l'œil tournait autour de la
               bouche, si bien qu'un pointage le portait de côté, presque sur la culasse. Il
               tourne maintenant autour du même point qu'elle, et se tient à la longueur du
               tube derrière son milieu, plus un pas : là où se tient le chef de pièce. */
            var muzzle = g.P.ToGodot();
            // une pièce de batterie se vise PAR SON SABORD, l'œil au ras du tube ; une pièce de chasse, sur le pont, de plus haut
            bool port = Math.Abs(side) == 1;
            if (_ship.PiecePivot(g) is Vector3 pv && _ship.PieceEye(g, port ? 0.6f : 1.2f) is Vector3 local)
            {
                /* DERRIÈRE LA PIÈCE, SUR SON AXE (signalé : décalé). L'œil est lu sur la pièce
                   dessinée — un pas derrière son bout arrière, dans l'axe de ses sommets —
                   puis tourné autour de son pivot comme elle (ShipNode.AimPiece). */
                double c = Math.Cos(yaw), s = Math.Sin(yaw);
                var turned = new Vector3((float)(local.X * c + local.Z * s), local.Y, (float)(-local.X * s + local.Z * c));
                return (pv + turned + new Vector3(0, port ? 0.18f : (float)(v.Y ?? 0.55), 0), look.Normalized());
            }
            var at = muzzle - dir * (float)(v.Z ?? 2.2) + new Vector3(0, (float)(v.Y ?? 0.55), 0);
            return (at, look.Normalized());
        }
        return null;
    }

    void UpdateCamera(double delta)
    {
        if (_camMode != CamFlyBy) FlyByOff();
        var target = _follow
            ? _ship.Position + new Vector3(0, (float)(_ship.Spec.D * 0.3), 0)
            : Vector3.Zero;

        /* PAS DE ROTATION AUTOMATIQUE ICI, et c'est un retrait plutôt qu'un
           oubli. L'aperçu du plan de formes en a une, à juste titre : la coque y
           est immobile et tourner lentement autour d'elle est ce qui montre sa
           forme. Recopiée dans une scène où l'objet regardé peut tourner
           lui-même, elle devient ambiguë PAR CONSTRUCTION — elle attribue au
           navire un mouvement qui est celui de la caméra, et cela s'est signalé
           à l'usage sous la forme « le navire de 2 000 t tourne sur lui-même ».

           Vérifié au solveur après coup : deux minutes de calme plat, barre au
           milieu, cap immobile et vitesse angulaire résiduelle de 5e-9 rad/s.
           Elle ne tournait pas. Une caméra qui bouge toute seule autour d'une
           chose capable de bouger est un instrument qui ment. */
        if (_camMode == CamSwim && _fixEye == null) { SwimCamera(); return; }
        if (_camMode == CamSight && _fixEye == null) { SightCamera(); return; }
        if (_fixEye is Vector3 fe)
        {
            _cam.Position = fe;
            _cam.LookAt(_fixLook ?? Vector3.Zero, Vector3.Up);
            return;
        }

        // à la pièce de chasse, s'il y en a une en batterie : l'œil est sur son axe, comme elle est pointée
        if (_gunPost is int pt) _bridgeYaw = _train.GetValueOrDefault(pt);
        if (_gunPost is int post2 && GunEye(post2, PostView) is { } poste2)
        {
            var xfp = _ship.GlobalTransform;
            _cam.Position = xfp * poste2.At;
            /* LA LIGNE DE MIRE CONTRE L'HORIZON, pas contre le pont : le boulet part
               contre l'horizon (le chef de pièce choisit son moment dans le roulis), et
               une mire qui tangue avec le pont visait à côté de quelques degrés — dix
               mètres de voile à deux cents. Le cap suit la pièce ; le site est celui du
               tir, but en blanc plus la hausse : ce qu'on vise est la ligne du départ. */
            var aim = xfp.Basis * poste2.Dir;
            var flat = new Vector3(aim.X, 0, aim.Z);
            if (flat.LengthSquared() > 1e-6f)
            {
                flat = flat.Normalized();
                double site = SightOf(post2);
                var look = flat * (float)Math.Cos(site) + new Vector3(0, (float)Math.Sin(site), 0);
                _cam.LookAt(_cam.Position + look * 120f, Vector3.Up);
            }
            else _cam.LookAt(xfp * (poste2.At + poste2.Dir * 120f), Vector3.Up);
            return;
        }

        if (_camMode == 1)
        {
            DeckCamera();
            return;
        }

        if (_camMode == CamFlyBy)
        {
            FlyByCamera(delta);
            return;
        }

        if (_camMode == CamBell)
        {
            BellCamera();
            return;
        }

        /* MI-EAU : l'œil POSÉ SUR LA SURFACE, moitié dedans moitié dehors — la
           vue en coupe des photographes sous-marins. La hauteur n'est pas
           choisie, elle est LUE sur la houle à l'endroit de l'œil, si bien que la
           ligne de partage reste au milieu de l'image quand la mer respire ; et
           l'on regarde à l'horizontale, sans quoi la ligne file hors du cadre. */
        if (_camMode == 3)
        {
            float r3 = Math.Max(12f, _dist * 0.35f);
            var at = new Vector3(_ship.Position.X + Mathf.Sin(_orbit) * r3, 0, _ship.Position.Z + Mathf.Cos(_orbit) * r3);
            /* UN QUART DE MÈTRE AU-DESSUS de la houle locale, et non dessus
               exactement : à fleur d'eau, la surface vue de l'œil même s'étale en une
               bande sombre au milieu de l'image et mange les deux moitiés. Relevée
               d'un rien, elle redevient une LIGNE. */
            at.Y = (float)_sea.Core.Sample(at.X, at.Z, _t) + _splitLift;
            _cam.Position = at;
            var look = _ship.Position;
            _cam.LookAt(new Vector3(look.X, at.Y, look.Z), Vector3.Up);
            return;
        }

        /* DU PONTON : l'œil immobile sur le musoir, à hauteur d'homme, et LIBRE.
           C'est l'inverse exact de toutes les autres vues — celles-ci sont portées
           par la coque et regardent le monde ; celle-là est portée par le monde et
           regarde où on la tourne.

           ELLE NE SUIT PLUS LE NAVIRE, et c'était le défaut : verrouillée sur la
           coque, on ne pouvait ni regarder le port, ni voir arriver autre chose,
           ni laisser le navire sortir du cadre. Elle s'ouvre sur lui — sans quoi
           l'on cherche où l'on est en y entrant — puis elle s'en détache.

           L'ŒIL EST EN COORDONNÉES LOCALES. La tête du ponton est un point du
           monde, donc en mètres vrais, et l'origine glisse sous lui : on la
           retranche à chaque image plutôt que de retenir un point qui se
           périmerait au premier recentrage.

           ET ELLE SE REND QUAND ON S'EN VA : la coque sortie des huit cents
           mètres DE CE PONTON-LÀ, la vue redevient l'orbite. Laissée en place,
           elle montrerait un point à l'horizon et le joueur croirait le jeu
           bloqué. La portée se mesure sur le ponton RETENU et non sur le plus
           proche, sans quoi s'éloigner de l'un en s'approchant d'un autre ne
           rendrait jamais la vue. */
        if (_camMode == 4)
        {
            if (_jettyHeld is not { } jv || !InJettyView(jv.Head))
            {
                _camMode = 0;
                _jettyHeld = null;
                Say("Le ponton est hors de vue");
                UpdateInfo();
            }
            else
            {
                var jo = _sea.Core.Origin;
                var jat = new Vector3((float)(jv.Head.X - jo.X),
                                      (float)(NavalSim.Core.Berth.DeckY + JettyEye),
                                      (float)(jv.Head.Z - jo.Z));
                double cpj = Math.Cos(_jettyPitch);
                var jdir = new Vector3((float)(Math.Sin(_jettyYaw) * cpj),
                                       (float)Math.Sin(_jettyPitch),
                                       (float)(Math.Cos(_jettyYaw) * cpj));
                _cam.Position = jat;
                _cam.LookAt(jat + jdir * 400f, Vector3.Up);
                return;
            }
        }

        if (_fixed)
        {
            // la position ET le relèvement sont verrouillés : rien ne suit
            double cp = Math.Cos(_fixPitch), reach = 200 * _ship.Spec.L / 24;
            var a = _anchor;
            _cam.Position = a.ToGodot();
            _cam.LookAt(new Vector3(
                (float)(a.X + Math.Sin(_fixYaw) * cp * reach),
                (float)(a.Y + Math.Sin(_fixPitch) * reach),
                (float)(a.Z + Math.Cos(_fixYaw) * cp * reach)), Vector3.Up);
            return;
        }

        float h = _dist * Mathf.Sin(_pitch);
        float r = _dist * Mathf.Cos(_pitch);
        /* SOUS LA QUILLE, SI ON LE DEMANDE. L'œil était tenu à deux mètres au
           moins au-dessus d'elle : on ne pouvait donc jamais passer dessous ni
           regarder le ciel à travers la surface. Le plancher ne vaut plus que
           lorsqu'on la regarde d'en haut ; l'inclinaison négative descend sous la
           quille, et c'est là qu'est la fenêtre de Snell. */
        float eyeY = _pitch >= 0 ? Mathf.Max(2f, h) : h;
        var eye = target + new Vector3(Mathf.Sin(_orbit) * r, eyeY, Mathf.Cos(_orbit) * r);
        _cam.Position = eye;
        _cam.LookAt(target, Vector3.Up);
    }
}
