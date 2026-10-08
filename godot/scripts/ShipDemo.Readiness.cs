using Godot;
using System;
using System.Collections.Generic;
using NavalSim.Core;

namespace NavalSim;

/// <summary>
/// LE BRANLE-BAS DES NAVIRES DU JEU (demandé : « un navire attaqué par surprise ne doit pas
/// répliquer à la seconde, il n'a pas eu le temps de se préparer »).
///
/// Un navire en paix ne navigue pas ses pièces chargées et en batterie : elles sont à
/// l'intérieur, à la serre, la poudre en soute. Avant de tirer, il faut faire branle-bas —
/// dégager les ponts, monter la poudre, charger, ouvrir les mantelets, mettre en batterie.
/// Cinq à dix minutes pour un bâtiment de guerre exercé de 1690, davantage pour un marchand ;
/// le jeu les comprime comme il comprime le rechargement (settings.json → gunnery.branleBas).
///
/// CE QUI L'ALERTE : un coup reçu, toile comprise ; un boulet qui tombe près de lui ; une bordée
/// tirée à portée d'oreille. L'alerte ne fait pas un ennemi — seul un coup au but en fait un
/// (Struck) —, elle met l'équipage aux postes. CEUX QUI NE SONT JAMAIS SURPRIS : le pirate qui
/// chasse, l'escarmouche, les spectres, et les deux navires d'une rencontre armée, qui se sont
/// vus venir de loin. Le nôtre a son propre branle-bas, à l'ordre (ShipDemo.Ports.cs).
/// </summary>
public partial class ShipDemo
{
    /// <summary>L'heure de jeu où chaque navire alerté est paré ; absent, il est en paix.</summary>
    readonly Dictionary<ShipNode, double> _clearedAt = new();

    // ce qu'un boulet qui tombe près d'un navire, ou une bordée qu'il entend, a de rayon
    const double SplashAlarm = 90, HeardAlarm = 900;
    // les mantelets s'ouvrent dans les dernières secondes du branle-bas : les pièces sortent à la fin
    const double LidsLead = 6;

    /// <summary>Paré à combattre : il a fait branle-bas, ou il ne peut pas être surpris.</summary>
    bool Cleared(ShipNode s) =>
        s.IsGhost || _skirmish || _pirates.ContainsKey(s)
        || (_clearedAt.TryGetValue(s, out double at) && _t >= at);

    /// <summary>Le branle-bas touche-t-il à sa fin (les mantelets peuvent s'ouvrir) ?</summary>
    bool ClearingLids(ShipNode s) =>
        Cleared(s) || (_clearedAt.TryGetValue(s, out double at) && _t >= at - LidsLead);

    /// <summary>
    /// L'ALERTE : l'équipage aux postes. <paramref name="now"/> : il était déjà paré (une rencontre
    /// armée, une partie rechargée en plein combat). Une seconde alerte ne remet pas la pendule.
    /// </summary>
    void Alarm(ShipNode s, bool now = false, bool say = false)
    {
        if (s == _ship || s.Physics.Foundered) return;
        if (_clearedAt.TryGetValue(s, out double at))
        {
            if (now && at > _t) _clearedAt[s] = _t;
            return;
        }
        if (now || s.IsGhost || _skirmish || _pirates.ContainsKey(s)) { _clearedAt[s] = _t; return; }
        var r = _gunnery.Rules;
        double prep = r.ClearLo + _gunRng.NextDouble() * Math.Max(0, r.ClearHi - r.ClearLo);
        _clearedAt[s] = _t + prep;
        GD.Print(FormattableString.Invariant($"[branle-bas] {s.Spec.Name} : paré dans {prep:F0} s"));
        // la vigie le voit : on n'annonce que ce qui est à portée de vue, et seulement un coup reçu
        if (say && (s.Physics.Body.Pos - _ship.Physics.Body.Pos).LengthXZ < 2000)
            Say($"Branle-bas à bord du {s.Spec.Name}");
    }

    /// <summary>Un boulet tombé à l'eau : les navires tout près l'ont vu passer.</summary>
    void AlarmSplash(Vec3d at)
    {
        foreach (var s in _others)
            if ((s.Physics.Body.Pos - at).LengthXZ < SplashAlarm + s.Spec.L * 0.5) Alarm(s, say: true);
    }

    /// <summary>Une bordée, ou une pièce, tirée : qui l'entend fait branle-bas — le tireur aussi.</summary>
    void AlarmHeard(ShipPhysics from)
    {
        var p = from.Body.Pos;
        foreach (var s in _others)
            if (s.Physics == from) Alarm(s, now: true);
            else if ((s.Physics.Body.Pos - p).LengthXZ < HeardAlarm) Alarm(s);
    }
}

/// <summary>
/// L'ESSAI DE LA SURPRISE (-- --surprise fiche) : une coque en paix posée par notre travers
/// tribord, à 150 m, arrêtée ; on fait branle-bas et l'on ouvre le feu dès que nos pièces sont
/// en batterie, puis à chaque bordée prête. Chaque seconde : où en est son branle-bas, ses
/// mantelets, et l'heure de son premier coup. Rien n'est enregistré.
/// </summary>
public partial class ShipDemo
{
    string? _surpriseTest;
    ShipNode? _surpriseShip;
    double _surpriseT = -1, _surpriseNext, _surpriseFired = -1, _surpriseOpened = -1, _surpriseWait = 6;

    void SurpriseTick()
    {
        if (_surpriseTest == null || !_booted || _inTitle) return;
        double dt = GetProcessDeltaTime();
        // après le départ au large (-- --large), qui retire les autres navires
        if ((_surpriseWait -= dt) > 0) return;
        if (_surpriseShip == null)
        {
            int idx = _paths.FindIndex(p => System.IO.Path.GetFileNameWithoutExtension(p) == _surpriseTest);
            if (idx < 0) { GD.Print($"[surprise] fiche {_surpriseTest} introuvable"); _surpriseTest = null; return; }
            var b = _ship.Physics.Body;
            var fwd = b.Quat.Rotate(new Vec3d(0, 0, 1)); fwd = new Vec3d(fwd.X, 0, fwd.Z).Normalized();
            var star = fwd.Cross(new Vec3d(0, 1, 0));
            var o = _sea.Core.Origin;
            var spot = b.Pos + star * 150;
            var to = spot + fwd * 5000;
            _surpriseShip = Put(idx, spot, new Vec3d(to.X + o.X, 0, to.Z + o.Z), false);
            if (_surpriseShip == null) { GD.Print("[surprise] coque non posée"); _surpriseTest = null; return; }
            _surpriseShip.Ctrl.SailsSet = false;
            _surpriseShip.Physics.Body.Vel = Vec3d.Zero;
            _portsOrder = true;           // le nôtre fait branle-bas : c'est lui qui attaque
            _gunSide = 1;
            _surpriseT = 0; _surpriseNext = 0;
            GD.Print($"[surprise] {_surpriseShip.Spec.Name} posé à 150 m par notre travers tribord, en paix");
            return;
        }
        _surpriseT += dt;
        var s = _surpriseShip;
        if (_firedAt.TryGetValue(s.Physics, out double f) && _surpriseFired < 0) _surpriseFired = _surpriseT;
        if (_surpriseOpened < 0 && s.HasPortLids && (s.PortsReady(1) || s.PortsReady(-1))) _surpriseOpened = _surpriseT;
        // notre feu : dès que la bordée est prête
        if (_ship.PortsReady(1) && _gunnery.Loaded(_ship.Battery, 1).Ready > 0 && _fireWhenOpen == null) Fire(false, true);
        if (_surpriseT < _surpriseNext) return;
        _surpriseNext += 5;
        _clearedAt.TryGetValue(s, out double at);
        string etat = Cleared(s) ? "paré" : _clearedAt.ContainsKey(s) ? FormattableString.Invariant($"branle-bas, paré dans {at - _t:F0} s") : "en paix";
        GD.Print(FormattableString.Invariant(
            $"[surprise] t {_surpriseT:F0} s : {etat} ; mantelets {(s.HasPortLids ? (_surpriseOpened >= 0 ? $"ouverts à {_surpriseOpened:F0} s" : "fermés") : "aucun")} ; ennemi {(EnemyOf(s) != null ? "oui" : "non")} ; premier coup {(_surpriseFired >= 0 ? $"à {_surpriseFired:F0} s" : "pas encore")}"));
        if (_surpriseT > 240) _surpriseTest = null;
    }
}

/// <summary>LE RELEVÉ DE L'ŒIL (-- --mesure-oeil N) : sa hauteur sur l'eau et ses sauts, sur N secondes.</summary>
public partial class ShipDemo
{
    double _eyeProbe = -1, _eyeT, _eyeSum, _eyeSum2, _eyeMin = 1e9, _eyeMax = -1e9, _eyeJump, _eyeLast = double.NaN;
    int _eyeN;

    void EyeProbeTick(double dt)
    {
        if (_eyeProbe <= 0 || !_booted || _inTitle) return;
        _eyeT += dt;
        if (_eyeT < 8) return;                       // que la mer et le drone aient pris leur allure
        double y = _cam.GlobalPosition.Y;
        _eyeSum += y; _eyeSum2 += y * y; _eyeN++;
        _eyeMin = Math.Min(_eyeMin, y); _eyeMax = Math.Max(_eyeMax, y);
        if (!double.IsNaN(_eyeLast)) _eyeJump = Math.Max(_eyeJump, Math.Abs(y - _eyeLast) / Math.Max(1e-3, dt));
        _eyeLast = y;
        if (_eyeT < 8 + _eyeProbe) return;
        double m = _eyeSum / _eyeN, sd = Math.Sqrt(Math.Max(0, _eyeSum2 / _eyeN - m * m));
        GD.Print(FormattableString.Invariant($"[oeil] {_eyeN} images : hauteur {m:F2} m (écart {sd:F2}, de {_eyeMin:F2} à {_eyeMax:F2}) ; plus vive montée ou descente {_eyeJump:F1} m/s ; plus haute crête réaliste {HighCrest():F2} m (toutes en phase : {_sea.Core.AmpMax:F2})"));
        _eyeProbe = -1;
    }
}
