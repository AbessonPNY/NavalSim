using Godot;
using System;
using NavalSim.Core;

namespace NavalSim;

/// <summary>
/// FLY-BY — LE DRONE DU CINÉMA. Une vue qu'on ne pilote pas : elle filme.
///
/// EN ROUTE, UN TRAVELLING PARALLÈLE. Le drone suit un couloir tiré à côté de la
/// route du navire — un bord, une hauteur —, part de l'arrière et le DÉPASSE,
/// puisqu'il va plus vite que lui. Au bout de son couloir, un fondu au noir le
/// pose sur un autre, et le passage recommence. La route qu'il suit est celle du
/// navire LISSÉE sur quelques secondes : une embardée ne doit pas faire zigzaguer
/// le drone, sans quoi l'image montrerait la barre au lieu du navire.
///
/// IL NE LE VISE PAS. Il regarde un point de la route un peu en avant de
/// l'étrave : le navire reste dans l'axe de l'image sans en être le centre, et
/// c'est le décalage qui fait le plan. Le regard suit avec une seconde de retard,
/// comme un opérateur qui panoramique, et non comme un viseur accroché.
///
/// À L'ARRÊT, DES TOURS — demandés tels quels, et contre une règle de la vue en
/// orbite : une caméra qui tourne seule autour d'un navire immobile fait croire
/// que c'est LUI qui tourne (signalé jadis, d'où l'orbite sans rotation). Ici la
/// rotation est lente, la mer défile derrière lui, et un fondu toutes les six
/// secondes casse la continuité qui fabrique l'illusion.
///
/// TOUT EST TENU RELATIVEMENT AU NAVIRE (le long de sa route, à côté, au-dessus) :
/// rien à décaler quand l'origine glisse.
/// </summary>
public partial class ShipDemo
{
    const int CamFlyBy = 5;
    /// <summary>L'objectif du drone : plus serré que l'œil, ce qui aplatit et fait cinéma.</summary>
    const float FlyFov = 40;
    /// <summary>Un plan à l'arrêt, en secondes, avant le fondu suivant (settings.json → flyby.plan).</summary>
    double _flyHold = 6;
    /// <summary>L'allure du drone, en part de celle réglée à l'œil (settings.json → flyby.vitesse).</summary>
    double _flySpeed = 1;
    /// <summary>Le fondu au noir : sortie, puis entrée, en secondes chacune.</summary>
    const double FlyFade = 0.45;

    Vector3 _flyCourse, _flyLook;
    bool _flyInit, _flyStill;
    double _flyAlong, _flySide, _flyH, _flyClock, _flyAngle, _flyR;
    int _flyTurn = 1;
    readonly RandomNumberGenerator _flyRng = new();

    ColorRect? _flyFadeRect;
    double _flyFadeT = -1;          // <0 : aucun fondu en cours
    Action? _flySwap;

    void EnterFlyBy()
    {
        _camMode = CamFlyBy;
        SetLens(FlyFov, OutsideNear);
        _flyInit = false;
    }

    /// <summary>Le voile du fondu, sous le HUD : le noir couvre l'image, pas les instruments.</summary>
    float _flyShipY;

    ColorRect FadeRect()
    {
        if (_flyFadeRect != null) return _flyFadeRect;
        var layer = new CanvasLayer { Layer = 0 };
        AddChild(layer);
        _flyFadeRect = new ColorRect { Color = new Color(0, 0, 0, 0), MouseFilter = Control.MouseFilterEnum.Ignore };
        _flyFadeRect.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        layer.AddChild(_flyFadeRect);
        return _flyFadeRect;
    }

    /// <summary>Hors du drone, plus de voile : une autre vue ne doit pas hériter d'un noir à moitié levé.</summary>
    void FlyByOff()
    {
        _flyFadeT = -1; _flySwap = null;
        if (_flyFadeRect != null) _flyFadeRect.Color = new Color(0, 0, 0, 0);
    }

    void FadeTo(Action swap)
    {
        if (_flyFadeT >= 0) return;
        _flyFadeT = 0; _flySwap = swap;
    }

    void FadeTick(double dt)
    {
        if (_flyFadeT < 0) return;
        _flyFadeT += dt;
        float a;
        if (_flyFadeT < FlyFade) a = (float)(_flyFadeT / FlyFade);
        else
        {
            // au plus noir, on change de plan
            if (_flySwap != null) { _flySwap(); _flySwap = null; }
            a = (float)Math.Max(0, 1 - (_flyFadeT - FlyFade) / FlyFade);
            if (_flyFadeT >= 2 * FlyFade) _flyFadeT = -1;
        }
        // un noir qui monte et descend en douceur : la courbe et non la droite
        a = a * a * (3 - 2 * a);
        FadeRect().Color = new Color(0, 0, 0, a);
    }

    /// <summary>Le plan suivant : un fondu en croisière, une COUPE franche en bataille.</summary>
    void NextShot()
    {
        if (InBattle) { FlyByOff(); NewShot(); }
        else FadeTo(NewShot);
    }

    /// <summary>Un plan neuf : un autre couloir en route, une autre place à l'arrêt.</summary>
    void NewShot()
    {
        double L = _ship.Spec.L;
        bool battle = InBattle;
        if (_flyStill)
        {
            _flyAngle += (_flyRng.Randf() < 0.5 ? -1 : 1) * (1.1 + 1.5 * _flyRng.Randf());
            // en bataille, plus près et plus bas : au ras des crêtes, dans la fumée
            _flyR = L * (battle ? 0.8 + 0.5 * _flyRng.Randf() : 1.2 + 0.9 * _flyRng.Randf());
            _flyH = L * (battle ? 0.03 + 0.18 * _flyRng.Randf() : 0.12 + 0.45 * _flyRng.Randf());
            _flyTurn = _flyRng.Randf() < 0.5 ? -1 : 1;
            _flyClock = 0;
        }
        else
        {
            /* ASSEZ LOIN POUR QU'IL TIENNE ENTIER dans l'objectif serré : à moins
               d'une longueur et demie, l'étrave sortait du cadre au passage. En
               bataille il n'a plus à tenir entier : on le frôle, et le cadre le coupe. */
            _flySide = (_flyRng.Randf() < 0.5 ? -1 : 1) * L * (battle ? 0.65 + 0.45 * _flyRng.Randf() : 1.4 + 1.0 * _flyRng.Randf());
            _flyH = L * (battle ? 0.04 + 0.18 * _flyRng.Randf() : 0.10 + 0.50 * _flyRng.Randf());
            _flyAlong = -FlySpan() * (battle ? 0.6 : 1);
        }
        _flyLook = Vector3.Zero;      // le regard du plan neuf part d'où il doit être, pas du précédent
    }

    /// <summary>La demi-longueur d'un passage : d'un peu derrière la poupe à un peu devant l'étrave.</summary>
    double FlySpan() => 1.4 * _ship.Spec.L + 25;

    void FlyByCamera(double dt)
    {
        // le contrechamp de bataille, s'il y en a un, prend l'image (ShipDemo.CineBattle.cs)
        if (DuelCamera(dt)) { FadeTick(dt); return; }
        bool battle = InBattle;
        var b = _ship.Physics.Body;
        var ship = _ship.Position;
        double L = _ship.Spec.L;
        /* L'ŒIL NE PLONGE PAS AVEC LA COQUE : la hauteur du navire, lissée sur trois secondes,
           porte le drone — son tangage et son pilonnement restent DANS l'image, ils ne secouent plus
           le cadre (signalé : « elle sursaute à chaque vague »). Le regard, lui, suit le vrai navire. */
        _flyShipY = _flyInit ? Mathf.Lerp(_flyShipY, ship.Y, (float)(1 - Math.Exp(-dt / 3.0))) : ship.Y;
        var carry = new Vector3(ship.X, _flyShipY, ship.Z);
        var v = new Vector3((float)b.Vel.X, 0, (float)b.Vel.Z);
        float speed = v.Length();
        var fw = b.Quat.Rotate(new Vec3d(0, 0, 1));
        var heading = new Vector3((float)fw.X, 0, (float)fw.Z).Normalized();
        // la route : celle où elle VA, et son cap quand elle ne va nulle part
        var want = speed > 0.5f ? v / speed : heading;

        // à l'arrêt ou en route, avec une marge pour qu'une erre qui hésite ne fasse pas sauter le plan
        bool still = _flyInit ? (_flyStill ? speed < 1.2f : speed < 0.6f) : speed < 0.9f;
        if (!_flyInit)
        {
            _flyCourse = want;
            _flyStill = still;
            _flyAngle = Math.Atan2(heading.X, heading.Z) + Math.PI * 0.5;
            NewShot();
            _flyInit = true;
        }
        else if (still != _flyStill) FadeTo(() => { _flyStill = still; NewShot(); });

        // la route lissée sur quatre secondes
        _flyCourse = _flyCourse.Lerp(want, (float)(1 - Math.Exp(-dt / 4.0))).Normalized();
        var course = _flyCourse;
        var side = new Vector3(-course.Z, 0, course.X);

        Vector3 eye, target;
        if (_flyStill)
        {
            /* LES TOURS : un tour en deux minutes, lent exprès — c'est la mer qui
               doit sembler bouger derrière lui, pas lui sur la mer. */
            _flyClock += dt;
            _flyAngle += _flyTurn * dt * 2 * Math.PI / 120 * _flySpeed * (battle ? 4 : 1);
            eye = carry + new Vector3((float)(Math.Sin(_flyAngle) * _flyR), (float)_flyH, (float)(Math.Cos(_flyAngle) * _flyR));
            // à côté de lui, pas sur lui : le centre de l'image glisse d'un dixième de longueur
            var off = (eye - carry).Cross(Vector3.Up).Normalized() * (float)(0.12 * L * _flyTurn);
            target = ship + new Vector3(0, (float)(0.15 * L), 0) + off;
            if (_flyClock > (battle ? Math.Min(3, _flyHold) : _flyHold)) NextShot();
        }
        else
        {
            /* LE TRAVELLING : le drone gagne sur le navire d'un tiers de son erre et
               d'un pas d'homme — lent, mais toujours devant à la fin. */
            _flyAlong += (0.35 * speed + 1.5) * _flySpeed * (battle ? 2.6 : 1) * dt;
            double bob = 0.04 * L * Math.Sin(_t * 0.31);          // une respiration, pas un roulis
            eye = carry + course * (float)_flyAlong + side * (float)_flySide + new Vector3(0, (float)(_flyH + bob), 0);
            target = ship + course * (float)(0.15 * L) + new Vector3(0, (float)(0.15 * L), 0);
            if (_flyAlong > FlySpan()) NextShot();
        }

        eye = AboveSeaAndLand(eye);

        // le regard d'un opérateur : une seconde de retard sur ce qu'il cadre ; en bataille il rattrape vite
        var dir = (target - eye).Normalized();
        _flyLook = _flyLook == Vector3.Zero ? dir : _flyLook.Lerp(dir, (float)(1 - Math.Exp(-dt / (battle ? 0.35 : 0.9)))).Normalized();
        _cam.Position = eye;
        _cam.LookAt(eye + _flyLook, Vector3.Up);
        _cam.Fov = SnapFov(dt, battle);
        if (battle) HandHeld(_cam.Fov, 1.2f);
        FadeTick(dt);
    }
}
