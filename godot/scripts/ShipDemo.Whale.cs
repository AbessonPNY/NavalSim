using Godot;
using System;
using NavalSim.Core;

namespace NavalSim;

/// <summary>
/// LA BALEINE DANS LA SCÈNE : le noyau décide (<see cref="Whale"/>), le nœud
/// dessine (<see cref="WhaleNode"/>), et ce fichier fait le reste — la lire
/// dans settings.json, la laisser au large, dire ce qu'on voit d'où on le voit,
/// et, quand elle frappe, la marque dans le bordé et le bruit du coup.
///
/// Elle ne vit qu'au LARGE : à un kilomètre de jeu des côtes et par soixante
/// mètres de fond, comme un cachalot, qui chasse le calmar dans les grands
/// fonds et ne vient pas en rade.
/// </summary>
public partial class ShipDemo : Node3D
{
    WhaleSettings _whaleRules = new();
    Whale _whale = null!;
    WhaleNode _whaleNode = null!;

    void BuildWhale()
    {
        _whaleNode = new WhaleNode();
        AddChild(_whaleNode);
        string? glb = _whaleRules.Glb == null ? null
            : System.IO.Path.GetFullPath(System.IO.Path.Combine(ProjectSettings.GlobalizePath("res://"), "..", _whaleRules.Glb));
        _whaleNode.Build(glb);
        _whale = new Whale(_whaleRules)
        {
            Spout = (at, dir) => _gunFx.Spout(V(at), V(dir)),
            Splash = (at, water, speed) => _spray.Pool.Burst(at, water, speed, 1.4),
            Event = WhaleSay,
            OnRam = Rammed
        };
    }

    static Vector3 V(Vec3d v) => new((float)v.X, (float)v.Y, (float)v.Z);

    /// <summary>Le fond sous un point LOCAL, pour qu'elle ne vienne pas s'échouer.</summary>
    double BedLocal(double x, double z)
    {
        var o = _sea.Core.Origin;
        return _world == null ? -1000 : _world.HeightAt(o.X + x, o.Z + z);
    }

    double ShoreLocal(double x, double z)
    {
        var o = _sea.Core.Origin;
        return _world == null ? 1e9 : _world.ShoreDistance(o.X + x, o.Z + z);
    }

    void WhaleTick(double dt)
    {
        if (_whale == null || _inTitle) return;
        _whale.Update(dt, _ship.Physics, _sea.Core, _t, BedLocal, ShoreLocal);
        _whaleNode.Sync(_whale, _sea.Core, _t, dt);
    }

    /* D'OÙ ON LA VOIT, dit comme à bord : relevée sur le navire, et non en
       degrés — « par tribord avant », « droit derrière ». Tribord est −x local. */
    string WhaleBearing()
    {
        var b = _ship.Physics.Body;
        var l = b.Quat.Inverted().Rotate(new Vec3d(_whale.Pos.X - b.Pos.X, 0, _whale.Pos.Z - b.Pos.Z));
        double a = Math.Atan2(-l.X, l.Z) * 180 / Math.PI;       // + : tribord
        double r = Math.Abs(a);
        string bord = a > 0 ? "tribord" : "bâbord";
        return r < 15 ? "droit devant"
             : r < 70 ? $"par {bord} avant"
             : r < 110 ? $"par le travers {bord}"
             : r < 165 ? $"par {bord} arrière"
             : "droit derrière";
    }

    void WhaleSay(string kind)
    {
        var b = _ship.Physics.Body;
        double d = Math.Sqrt(Math.Pow(_whale.Pos.X - b.Pos.X, 2) + Math.Pow(_whale.Pos.Z - b.Pos.Z, 2));
        string loin = d >= 1000 ? FormattableString.Invariant($"{d / 1000:F1} km").Replace('.', ',') : $"{d:F0} m";
        string qui = _whale.White ? "La baleine blanche" : "Une baleine";
        GD.Print(FormattableString.Invariant($"baleine : {kind}, {_whale.Mood}, à {d:F0} m, profondeur {_whale.Y:F1} m, elle {_whale.Speed:F1} m/s, navire {Math.Sqrt(b.Vel.X * b.Vel.X + b.Vel.Z * b.Vel.Z):F1} m/s"));
        switch (kind)
        {
            case "seen": Say($"Elle souffle ! {qui} {WhaleBearing()}, à {loin}"); break;
            case "sounds": Say("Elle sonde — la queue haute, elle plonge"); break;
            case "under": Say("Une ombre passe sous la quille…"); break;
            case "charge": Say($"{(_whale.White ? "La baleine blanche" : "La baleine")} charge ! {WhaleBearing()}"); break;
            case "outrun": Say("La baleine renonce : nous filons trop vite pour elle"); break;
            case "shoal": Say("La baleine n'ose pas les hauts-fonds — elle s'éloigne"); break;
        }
    }

    /// <summary>
    /// LE COUP : le noyau a déjà poussé la coque et ouvert la voie d'eau ; ici,
    /// ce qui se voit et s'entend — la marque dans le bordé, le bruit du chêne,
    /// l'eau jetée de part et d'autre de la tête.
    /// </summary>
    void Rammed(WhaleRam r)
    {
        GD.Print(FormattableString.Invariant($"baleine : COUP à {r.Speed:F1} m/s, navire +{r.DeltaV:F2} m/s, voie d'eau {r.Area:F2} m² (compartiment {r.Comp})"));
        _sound?.Crash(r.At, 2.5, r.Speed, "hull");
        _ship.Scar(V(r.At), 2.2);
        _spray.Pool.Burst(r.At, 10 + 2 * r.Speed, r.Speed + 2, 1.2);
        Say(FormattableString.Invariant(
            $"{(_whale.White ? "La baleine blanche" : "La baleine")} nous frappe ! Voie d'eau de {r.Area:F1} m² — aux pompes !").Replace("0.", "0,"));
    }

    /// <summary>⇧K, et --baleine : la faire venir, d'une humeur choisie ou tirée.</summary>
    void SummonWhale(WhaleMood? mood = null)
    {
        if (_whale == null) return;
        var m = mood ?? WhaleMood.Hostile;
        if (!_whale.Summon(_ship.Physics, m, 700, BedLocal))
            Say("Pas assez d'eau par ici pour une baleine");
    }
}
