using Godot;
using System;
using System.Linq;
using System.Collections.Generic;
using NavalSim.Core;

namespace NavalSim;

/// <summary>
/// DEUX RAYONS DU COMPTOIR QUI NE SONT PAS DES DENRÉES : le poisson frais, qui se
/// vend au kilo et à sa fraîcheur, et le chantier, où l'on change de bord.
///
/// Comme les trésors, leurs lignes ne se rebâtissent que quand ce qu'elles
/// montrent change — des boutons refaits à chaque passage se déroberaient sous
/// le pointeur —, et leurs prix se réécrivent à chaque passage.
/// </summary>
public partial class ShipDemo
{
    VBoxContainer? _mkFish, _mkYard;
    string _mkFishSig = "", _mkYardSig = "";
    readonly List<(string Key, Label Kg, Label Price)> _mkFishRows = new();
    readonly List<(int Index, ShipSpec Spec, Label Price)> _mkYardRows = new();
    Shipyard _yard = new();
    readonly Dictionary<string, ShipSpec?> _yardSpecs = new();

    void BuildFishAndYard(VBoxContainer box)
    {
        string path = Assets.Path("market/chantier.json");
        if (System.IO.File.Exists(path))
        {
            try { _yard = Shipyard.FromJson(System.IO.File.ReadAllText(path)); }
            catch (System.Text.Json.JsonException e) { GD.PushWarning($"[chantier] chantier.json illisible : {e.Message}"); }
        }
        _mkFish = new VBoxContainer { Visible = false };
        _mkFish.AddThemeConstantOverride("separation", 2);
        box.AddChild(_mkFish);
        _mkYard = new VBoxContainer { Visible = false };
        _mkYard.AddThemeConstantOverride("separation", 2);
        box.AddChild(_mkYard);
    }


    /// <summary>Le poisson frais : une ligne par espèce à bord, ses kilos, ce qu'il vaut ici à sa fraîcheur.</summary>
    void FishRows()
    {
        if (_mkFish == null || _portHere == null || _angling == null) return;
        var keys = _catch.Select(l => l.Key).Distinct().OrderBy(k => k).ToList();
        string sig = string.Join(";", keys);
        _mkFish.Visible = _alongside && keys.Count > 0;
        var (ink, dim, gold) = _mkTreasureInk;
        if (sig != _mkFishSig)
        {
            _mkFishSig = sig;
            foreach (var c in _mkFish.GetChildren()) c.QueueFree();
            _mkFishRows.Clear();
            if (keys.Count > 0) _mkFish.AddChild(MkLabel("Poisson frais", 13, gold));
            foreach (var key in keys)
            {
                var f = _angling.Of(key);
                var row = new HBoxContainer();
                row.AddThemeConstantOverride("separation", 8);
                var name = MkLabel(f?.Name ?? key, 14, ink, expand: true);
                name.TooltipText = f?.Note ?? "";
                row.AddChild(Ellipse(name));
                var kg = MkLabel("", 14, dim, minW: 64, right: true);
                row.AddChild(kg);
                var price = MkLabel("", 14, new Color(0.95f, 0.88f, 0.66f), minW: 96, right: true);
                row.AddChild(price);
                var sell = new Button { Text = "Vendre", FocusMode = Control.FocusModeEnum.None };
                sell.Pressed += () => SellFish(key);
                row.AddChild(sell);
                _mkFish.AddChild(row);
                _mkFishRows.Add((key, kg, price));
            }
        }
        foreach (var (key, kg, price) in _mkFishRows)
        {
            var lots = _catch.Where(l => l.Key == key).ToList();
            kg.Text = $"{Kg(lots.Sum(l => l.Kg))} kg";
            price.Text = FormattableString.Invariant($"{Js.Round(FishValue(lots)):F0} p.");
        }
    }

    ShipSpec? YardSpec(string id)
    {
        if (_yardSpecs.TryGetValue(id, out var s)) return s;
        int idx = _paths.FindIndex(p => System.IO.Path.GetFileNameWithoutExtension(p) == id);
        s = idx >= 0 ? ShipLibrary.Load(_paths[idx]) : null;
        if (s == null) GD.PushWarning($"[chantier] pas de fiche « {id} »");
        _yardSpecs[id] = s;
        return s;
    }

    /// <summary>
    /// LE CHANTIER : les coques qu'on y vend, au prix net de la reprise de la
    /// vôtre. Seulement dans un port qui en a un, et seulement à quai — on ne
    /// signe pas l'achat d'un navire depuis le large.
    /// </summary>
    void YardRows()
    {
        if (_mkYard == null || _portHere == null) return;
        var yard = _yard.At(_portHere.Key);
        _mkYard.Visible = _alongside && yard != null;
        if (yard == null) return;
        string sig = _portHere.Key + "|" + _index;
        var (ink, dim, gold) = _mkTreasureInk;
        if (sig != _mkYardSig)
        {
            _mkYardSig = sig;
            foreach (var c in _mkYard.GetChildren()) c.QueueFree();
            _mkYardRows.Clear();
            _mkYard.AddChild(MkLabel(yard.Name.Length > 0 ? yard.Name : "Chantier", 13, gold));
            _mkYard.AddChild(MkLabel(FormattableString.Invariant(
                $"le vôtre repris {_yard.Allowance(_ship.Spec.Tonnes) / Market.SousParEcu:F0} écus"), 12, dim));
            string mine = System.IO.Path.GetFileNameWithoutExtension(_paths[_index]);
            foreach (var id in yard.Ships)
            {
                if (id == mine || YardSpec(id) is not { } spec) continue;
                int idx = _paths.FindIndex(p => System.IO.Path.GetFileNameWithoutExtension(p) == id);
                var row = new HBoxContainer();
                row.AddThemeConstantOverride("separation", 8);
                var name = MkLabel(spec.Name, 14, ink, expand: true);
                name.TooltipText = FormattableString.Invariant($"{spec.Tonnes:F0} t, {spec.L:F1} m");
                row.AddChild(Ellipse(name));
                var price = MkLabel("", 14, new Color(0.95f, 0.88f, 0.66f), minW: 96, right: true);
                row.AddChild(price);
                var buy = new Button { Text = "Acheter", FocusMode = Control.FocusModeEnum.None };
                buy.Pressed += () => BuyShip(idx, spec);
                row.AddChild(buy);
                _mkYard.AddChild(row);
                _mkYardRows.Add((idx, spec, price));
            }
        }
        foreach (var (_, spec, price) in _mkYardRows)
        {
            long net = _yard.Net(spec.Tonnes, _ship.Spec.Tonnes);
            // négatif : on descend vers plus petit, et c'est le chantier qui paie
            price.Text = net >= 0 ? FormattableString.Invariant($"{net / (double)Market.SousParEcu:F0} écus")
                                  : FormattableString.Invariant($"+{-net / (double)Market.SousParEcu:F0} écus");
        }
    }

    /// <summary>
    /// CHANGER DE BORD. Le chantier prend l'ancien et livre le nouveau AU MÊME
    /// PONTON : la cale et les prises passent de l'un à l'autre (ce que la
    /// nouvelle cale ne peut prendre reste au quai), la bourse et le rang restent
    /// au capitaine.
    /// </summary>
    void BuyShip(int idx, ShipSpec spec)
    {
        if (_portHere == null || !_alongside || idx < 0 || idx == _index) return;
        long net = _yard.Net(spec.Tonnes, _ship.Spec.Tonnes);
        // vers plus petit, le chantier rend la différence
        if (net < 0) _purse.Add(-net);
        else if (!_purse.Take(net))
        {
            long manque = net - _purse.Sous;
            Say(FormattableString.Invariant($"Bourse trop courte — il manque {Math.Ceiling(manque / (double)Market.SousParEcu):F0} écus"));
            return;
        }
        var cargo = _ship.Physics.Cargo.Select(c => (c.Kind, Tonnes: c.Kg / 1000)).ToList();
        int powder = _ship.Physics.Powder;
        var port = _portHere;
        _lines?.Raise();
        _anchor2?.Weigh(_ship);
        Launch(idx);

        // au ponton du port, comme la mise à quai du départ, mais sans déplacer l'origine
        var (x, z, heading) = Berth.At(port, _ship.Spec.L, _ship.Spec.B);
        var o = _sea.Core.Origin;
        var b = _ship.Physics.Body;
        b.Pos = new Vec3d(x - o.X, _eqY, z - o.Z);
        b.Quat = Quatd.FromAxisAngle(new Vec3d(0, 1, 0), heading);
        b.Vel = Vec3d.Zero;
        b.AngVel = Vec3d.Zero;
        _ship.SyncTransform();

        // la cale passe, autant que la neuve en prend ; le poisson se recale sur la liste
        var ph = _ship.Physics;
        double left = 0;
        foreach (var (kind, t) in cargo)
        {
            if (kind == FishKind) continue;
            double room = Math.Max(0, ph.CargoCapacity - ph.CargoTonnes);
            double put = Math.Min(t, room);
            if (put > 0.001) ph.LoadCargo(Config.NComp / 2, HoldFloor, 0, put, kind);
            left += t - put;
        }
        FishHold();
        ph.Powder = Math.Min(powder, ph.PowderMax);
        _quests?.Credit(Goal.Buy, 1);
        Say($"{spec.Name} est à vous — " + (net >= 0 ? $"{net / Market.SousParEcu} écus" : $"le chantier vous rend {-net / Market.SousParEcu} écus")
            + (left > 0.05 ? FormattableString.Invariant($" ; {left:F1} t restent au quai, faute de place") : ""));
        _mkYardSig = "";
        MarketTick();
    }
}
