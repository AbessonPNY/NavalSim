using Godot;
using System;
using NavalSim.Core;

namespace NavalSim;

/// <summary>
/// LE SERPENT DANS LA SCÈNE : le noyau décide (<see cref="SeaSerpent"/>), le
/// nœud dessine (<see cref="SerpentNode"/>), et ce fichier fait le reste — la
/// pluie qui le fait venir, ce qu'on entend et voit, le coup porté (la marque,
/// le bruit, le mât mordu), et les boulets qui le chassent.
/// </summary>
public partial class ShipDemo : Node3D
{
    SerpentSettings _serpentRules = new();
    SeaSerpent _serpent = null!;
    SerpentNode _serpentNode = null!;

    void BuildSerpent()
    {
        _serpentNode = new SerpentNode();
        AddChild(_serpentNode);
        _serpent = new SeaSerpent(_serpentRules)
        {
            Splash = (at, water, speed) => _spray.Pool.Burst(at, water, speed, 1.3),
            Event = SerpentSay,
            OnStrike = SerpentStruck
        };
    }

    /// <summary>
    /// Ce qui le fait venir : l'averse ou le grain, pas la neige — et la BRUME de
    /// surface, où il est chez lui : on ne le voit qu'à cent mètres, et on l'entend.
    /// </summary>
    double Wet() => Math.Max(Math.Max(_fall.Snow ? 0 : _fall.Amount, _inSquall ? _squall.Inten : 0),
                             (_seaFog?.Amount ?? 0) * 0.8);

    /// <summary>« dans la brume » ou « dans la pluie », selon ce qui le cache.</summary>
    string Veil() => (_seaFog?.Amount ?? 0) > 0.3 ? "dans la brume" : "dans la pluie";

    void SerpentTick(double dt)
    {
        if (_serpent == null || _inTitle) return;
        _serpent.Update(dt, _ship.Physics, _sea.Core, _t, Wet(), BedLocal, ShoreLocal);
        _serpentNode.Sync(_serpent);
    }

    /* D'OÙ ON LE VOIT, relevé sur le navire, comme la baleine. */
    string SerpentBearing()
    {
        var b = _ship.Physics.Body;
        var l = b.Quat.Inverted().Rotate(new Vec3d(_serpent.Head.X - b.Pos.X, 0, _serpent.Head.Z - b.Pos.Z));
        double a = Math.Atan2(-l.X, l.Z) * 180 / Math.PI;       // + : tribord
        double r = Math.Abs(a);
        string bord = a > 0 ? "tribord" : "bâbord";
        return r < 15 ? "droit devant" : r < 70 ? $"par {bord} avant" : r < 110 ? $"par le travers {bord}"
             : r < 165 ? $"par {bord} arrière" : "droit derrière";
    }

    void SerpentSay(string kind)
    {
        GD.Print($"serpent : {kind}, {_serpent.State}, pv {_serpent.Hp:F0}");
        switch (kind)
        {
            // on l'entend avant de le voir : le sifflement, et le grondement qui porte sur l'eau
            case "heard":
                _sound?.Growl(_serpent.Head);
                Say($"Un sifflement {Veil()}… quelque chose tourne autour de nous");
                break;
            case "seen": Say($"Serpent de mer ! Ses anneaux crèvent l'eau {SerpentBearing()}"); break;
            case "charge": Say($"Il plonge — il vient sur nous, {SerpentBearing()} !"); break;
            case "wounded": Say("Touché ! Le serpent plonge"); break;
            case "flees": Say($"Le serpent de mer s'enfuit {Veil()}"); break;
        }
    }

    /// <summary>Le coup : le noyau a poussé la coque et ouvert l'eau ; ici, ce qui se voit et s'entend.</summary>
    void SerpentStruck(SerpentStrike k)
    {
        GD.Print(FormattableString.Invariant($"serpent : coup {k.Kind}, navire +{k.DeltaV:F2} m/s, voie d'eau {k.Area:F2} m²"));
        if (k.Kind == "bite")
        {
            // la morsure : un mât debout, au hasard — la blessure des mâts, trois l'abattent
            var masts = _ship.MastBoxes();
            if (masts.Count == 0) { Say("Le serpent claque des mâchoires sur le pavois !"); return; }
            var m = masts[(int)(GD.Randf() * masts.Count) % masts.Count];
            bool down = _ship.WoundMast(m.Fall);
            _sound?.Crash(_serpent.Head, 1.5, 4, "mast");
            Say(down ? "Le serpent arrache un mât !" : "Le serpent mord un mât, qui craque !");
            return;
        }
        _sound?.Crash(k.At, 2.2, 6, "hull");
        _ship.Scar(new Vector3((float)k.At.X, (float)k.At.Y, (float)k.At.Z), 1.8);
        _spray.Pool.Burst(k.At, 12, 7, 1.3);
        Say(FormattableString.Invariant($"{(k.Kind == "tail" ? "Un coup de queue" : "Le serpent frappe de la tête")} ! Voie d'eau de {k.Area:F1} m² — aux pompes !").Replace("0.", "0,"));
    }

    /// <summary>⇧J et --serpent : une averse, et le serpent tout de suite.</summary>
    void SummonSerpent()
    {
        if (_serpent == null) return;
        // quatre heures de jeu, deux minutes réelles : vingt minutes n'en faisaient que dix secondes
        if (Wet() < _serpentRules.MinRain) _climate.StartShower(240, 0.8);
        if (!_serpent.Summon(_ship.Physics, BedLocal)) Say("Pas assez d'eau par ici pour le serpent de mer");
    }
}
