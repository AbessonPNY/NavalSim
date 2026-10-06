using Godot;
using System;
using NavalSim.Core;

namespace NavalSim;

/// <summary>
/// LE CINÉMA EN BATAILLE (demandé) — quand le canon parle autour de nous, le drone
/// change de manière : il ne filme plus une croisière, il couvre une action.
///
/// LE CONTRECHAMP SUR CELUI QUI TIRE. Une pièce part — d'un navire à portée, ou de
/// la nôtre —, et l'image COUPE (pas de fondu : l'action ne se fond pas) sur un plan
/// pris derrière notre navire, en surplomb de son épaule, braqué sur l'autre : le
/// nôtre au premier plan, au bord du cadre et dans le flou, l'autre net au fond. Et
/// le plan s'ouvre sur un ZOOM ÉCLAIR : la focale part large et se serre sur lui en
/// un quart de seconde, comme l'opérateur qui cherche le coup de feu. Quand c'est
/// nous qui tirons, c'est sur celui qu'on vise.
///
/// LE DRONE DEVIENT AGRESSIF entre deux contrechamps : plus près, plus bas, plus
/// vite, des coupes franches au lieu des fondus, un regard qui rattrape vite, et des
/// coups de focale secs au milieu d'un plan.
///
/// TOUT EST PORTÉ À LA MAIN : un tremblé de cadreur (lacet, tangage, roulis à
/// fréquences étrangères les unes aux autres), qui va comme la focale — serré, on
/// tremble moins en angle pour ne pas trembler plus à l'image. Il court sur le temps
/// RÉEL : la compression du temps ne doit pas faire trembler plus vite.
///
/// La bataille dure tant qu'on tire à portée, plus vingt-cinq secondes ; après, le
/// drone retrouve sa douceur. settings.json → flyby → bataille (false l'ôte).
/// </summary>
public partial class ShipDemo
{
    /// <summary>Le cinéma passe-t-il au style de bataille ? settings.json → flyby → bataille.</summary>
    bool _flyBattleOn = true;
    double _battleUntil = -1;
    /// <summary>Le contrechamp en cours : l'autre navire, depuis quand, de quel bord on se tient.</summary>
    ShipNode? _duelFoe;
    double _duelT, _duelCool = -1;
    int _duelSide = 1;
    float _duelFrom;
    /// <summary>Les coups de focale du drone agressif : de quoi, vers quoi, depuis quand, et le prochain.</summary>
    float _snapFrom = FlyFov, _snapTo = FlyFov;
    double _snapT = 1, _snapNext;

    /// <summary>Distance en deçà de laquelle un coup de canon fait bataille, en mètres.</summary>
    const double BattleRange = 1500;
    /// <summary>Durée d'un contrechamp, en secondes ; et l'écart minimal entre deux.</summary>
    const double DuelHold = 2.8, DuelGap = 5.5;
    /// <summary>Le zoom éclair, en secondes.</summary>
    const double CrashZoom = 0.28;

    bool InBattle => _flyBattleOn && _cine && _camMode == CamFlyBy && _t < _battleUntil;

    /// <summary>Une pièce vient de partir (Gunnery.OnFire) : est-ce notre bataille, et qui filmer.</summary>
    void CineShot(ShipPhysics from, Vec3d at, Vec3d dir)
    {
        if (!_flyBattleOn || !_cine || _camMode != CamFlyBy || _ship == null) return;
        ShipNode? other = null;
        if (from == _ship.Physics) other = AimedAt(at, dir);
        else
        {
            foreach (var s in _others)
                if (s.Physics == from) { other = s; break; }
            if (other != null && (other.IsGhost || (other.Physics.Body.Pos - _ship.Physics.Body.Pos).LengthXZ > BattleRange)) other = null;
        }
        if (other == null) return;
        bool fresh = _t >= _battleUntil;
        _battleUntil = _t + 25;
        if (fresh) _snapNext = 0;
        if (_t < _duelCool || _duelFoe != null) return;
        _duelFoe = other;
        _duelT = 0;
        _duelCool = _t + DuelGap;
        _duelSide = _flyRng.Randf() < 0.5 ? -1 : 1;
        _duelFrom = 0;            // posé à la première image, quand on connaît la focale visée
        FlyByOff();               // un fondu en cours ne doit pas noircir la coupe
    }

    /// <summary>Le navire que notre coup vise : le plus proche dans un cône de vingt degrés autour du tir.</summary>
    ShipNode? AimedAt(Vec3d at, Vec3d dir)
    {
        double dl = Math.Sqrt(dir.X * dir.X + dir.Z * dir.Z);
        if (dl < 1e-6) return null;
        ShipNode? best = null; double bd = BattleRange;
        foreach (var s in _others)
        {
            if (s.IsGhost || s.Physics.Foundered) continue;
            var p = s.Physics.Body.Pos;
            double dx = p.X - at.X, dz = p.Z - at.Z, d = Math.Sqrt(dx * dx + dz * dz);
            if (d > bd || d < 1) continue;
            if ((dx * dir.X + dz * dir.Z) / (d * dl) < Math.Cos(20 * Math.PI / 180)) continue;
            bd = d; best = s;
        }
        return best;
    }

    /// <summary>
    /// LE CONTRECHAMP : derrière notre navire, le nôtre au bord du cadre, l'autre au
    /// fond. Vrai tant qu'il dure ; faux quand il est fini ou que l'autre n'est plus.
    /// </summary>
    bool DuelCamera(double dt)
    {
        var foe = _duelFoe;
        if (foe == null) return false;
        _duelT += dt;
        if (!IsInstanceValid(foe) || foe.Physics.Foundered || _duelT > DuelHold)
        {
            _duelFoe = null;
            // et l'on COUPE sur un plan neuf du drone
            NewShot();
            return false;
        }
        var me = _ship.Position;
        var them = foe.Position;
        var d = them - me; d.Y = 0;
        float D = Math.Max(1, d.Length());
        d /= D;
        var side = new Vector3(-d.Z, 0, d.X) * _duelSide;
        float L = (float)_ship.Spec.L, Lf = (float)foe.Spec.L;
        // en arrière du nôtre, d'autant plus que l'autre est loin : il reste un premier plan, pas un mur
        float back = 2.0f * L + 0.25f * D;
        float dist = D + back;
        var vp = GetViewport().GetVisibleRect().Size;
        float aspect = vp.Y > 1 ? vp.X / vp.Y : 16f / 9f;
        // la focale qui met l'autre sur une petite moitié de la largeur
        float hHalf = Mathf.Atan(Lf * 0.5f / 0.45f / dist);
        float fov = Mathf.Clamp(2 * Mathf.RadToDeg(Mathf.Atan(Mathf.Tan(hHalf) / aspect)), 5, 45);
        hHalf = Mathf.Atan(Mathf.Tan(Mathf.DegToRad(fov) * 0.5f) * aspect);
        // le nôtre au bord du cadre : aux huit dixièmes de la demi-largeur
        float lateral = Mathf.Tan(0.8f * hHalf) * back;
        var eye = me - d * back + side * lateral + new Vector3(0, 0.30f * L, 0);
        eye = AboveSeaAndLand(eye);
        var target = them + new Vector3(0, 0.2f * Lf, 0);

        // LE ZOOM ÉCLAIR : de trois fois plus large à la focale visée, puis une lente poussée
        if (_duelFrom <= 0) _duelFrom = Math.Min(62, fov * 3f);
        float x = (float)Math.Min(1, _duelT / CrashZoom);
        float e = 1 - (1 - x) * (1 - x) * (1 - x);
        float now = Mathf.Lerp(_duelFrom, fov, e) * (1 - 0.04f * (float)Math.Max(0, (_duelT - CrashZoom) / DuelHold));
        _cam.Fov = now;
        _cam.Position = eye;
        _cam.LookAt(target, Vector3.Up);
        HandHeld(now, 1.0f);
        _flyLook = Vector3.Zero;
        return true;
    }

    /// <summary>
    /// LES COUPS DE FOCALE du drone agressif : toutes les une à deux secondes et
    /// demie, une autre focale, prise en un dixième de seconde. Hors bataille, la
    /// focale revient doucement à celle du drone.
    /// </summary>
    float SnapFov(double dt, bool battle)
    {
        if (!battle)
        {
            _snapTo = FlyFov;
            _snapFrom = Mathf.Lerp(_snapFrom, FlyFov, (float)(1 - Math.Exp(-dt / 1.2)));
            _snapT = 1;
            return _snapFrom;
        }
        if (_t >= _snapNext)
        {
            float[] fovs = { 22, 30, 40, 52 };
            float pick = fovs[_flyRng.RandiRange(0, fovs.Length - 1)];
            if (Mathf.Abs(pick - _snapTo) < 1) pick = fovs[(Array.IndexOf(fovs, pick) + 2) % fovs.Length];
            _snapFrom = Mathf.Lerp(_snapFrom, _snapTo, Math.Min(1, (float)_snapT));
            _snapTo = pick;
            _snapT = 0;
            _snapNext = _t + 1.0 + 1.5 * _flyRng.Randf();
        }
        _snapT += dt / 0.12;
        float s = (float)Math.Min(1, _snapT);
        s = s * s * (3 - 2 * s);
        return Mathf.Lerp(_snapFrom, _snapTo, s);
    }

    /// <summary>
    /// LA CAMÉRA À L'ÉPAULE : trois petites rotations bruitées sur le cadre déjà posé.
    /// L'amplitude va comme la focale (un quarantième de son angle, à <paramref name="amp"/> = 1).
    /// </summary>
    void HandHeld(float fov, float amp)
    {
        double t = Time.GetTicksMsec() / 1000.0;
        float k = amp * fov * 0.025f * Mathf.Pi / 180f;
        float yaw = k * (float)(0.6 * Math.Sin(t * 1.7) + 0.3 * Math.Sin(t * 3.9 + 1.3) + 0.15 * Math.Sin(t * 7.3 + 0.4));
        float pitch = k * (float)(0.6 * Math.Sin(t * 1.3 + 2.1) + 0.3 * Math.Sin(t * 4.7 + 0.7) + 0.15 * Math.Sin(t * 8.1 + 2.9));
        float roll = k * 0.8f * (float)(Math.Sin(t * 0.9 + 0.5) + 0.4 * Math.Sin(t * 2.3 + 1.9));
        _cam.RotateObjectLocal(Vector3.Up, yaw);
        _cam.RotateObjectLocal(Vector3.Right, pitch);
        _cam.RotateObjectLocal(Vector3.Back, roll);
    }

    /// <summary>Ni dans la mer ni dans la terre : trois mètres au-dessus de la crête, six au-dessus du relief.</summary>
    Vector3 AboveSeaAndLand(Vector3 eye)
    {
        double floor = _sea.Core.Sample(eye.X, eye.Z, _sea.Core.Time) + 3;
        if (_world != null)
        {
            var o = _sea.Core.Origin;
            floor = Math.Max(floor, _world.HeightAt(o.X + eye.X, o.Z + eye.Z) + 6);
        }
        if (eye.Y < floor) eye.Y = (float)floor;
        return eye;
    }
}
