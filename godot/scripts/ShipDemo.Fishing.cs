using Godot;
using System;
using System.Linq;
using System.Collections.Generic;
using NavalSim.Core;

namespace NavalSim;

/// <summary>
/// LA PÊCHE À LA LIGNE À MAIN — lancer, attendre, ferrer.
///
/// ESPACE, navire en panne ou au mouillage : les lignes descendent, une pour deux
/// hommes. La sonde dit le fond — sa profondeur, et ce que le suif du plomb a
/// rapporté : du sable qui colle, de la roche qui le laisse propre, un plomb qui
/// glisse sur un tombant. Rien d'autre n'est marqué : le poisson tient au fond,
/// et c'est au pêcheur d'apprendre où (fishing/poissons.json).
///
/// « ÇA MORD ! » : Espace dans la seconde et demie, ou il emporte l'appât. Ferré,
/// on le hale à la main, le temps que la profondeur demande ; un gros mérou file
/// s'enrocher et casse parfois la ligne. Espace sans touche : on relève tout. Et
/// l'on relève de soi-même si le navire prend de l'erre — une ligne ne traîne
/// pas derrière un sloop qui fait route.
///
/// LE POISSON SE GÂTE, en temps de jeu : plein prix une vingtaine de minutes, puis
/// de moins en moins, et passé l'heure il passe par-dessus bord. Il pèse dans la
/// cale (une nature « poisson » dans <see cref="ShipPhysics.Cargo"/>), mais c'est
/// la liste des prises qui dit QUOI et DEPUIS QUAND — la cale ne sait que des
/// kilos.
/// </summary>
public partial class ShipDemo
{
    FishingRules? _angling;
    HandLines? _lines;
    /// <summary>Les prises à bord, chacune avec l'heure (temps de jeu) où elle est sortie de l'eau.</summary>
    readonly List<FishLot> _catch = new();
    readonly Random _fishRng = new();
    Label? _fishLine;
    /// <summary>Jusqu'à quand un Espace tardif est pris pour un ferrage manqué, et non pour « relever ».</summary>
    double _fishLate = -1;
    double _fishAcc;
    /// <summary>La prise la plus vieille dont on a déjà dit qu'elle ne serait bientôt plus fraîche.</summary>
    double _fishWarned = double.NegativeInfinity;
    /// <summary>Le rang du capitaine (« Pêcheur »), vide en jeu libre.</summary>
    string _rank = "";

    const string FishKind = "poisson";

    void LoadFishing()
    {
        string path = Assets.Path("fishing/poissons.json");
        if (!System.IO.File.Exists(path)) { GD.PushWarning("[pêche] fishing/poissons.json absent : pas de pêche"); return; }
        try { _angling = FishingRules.FromJson(System.IO.File.ReadAllText(path)); }
        catch (System.Text.Json.JsonException e) { GD.PushWarning($"[pêche] poissons.json illisible : {e.Message}"); return; }
        _lines = new HandLines(_angling)
        {
            OnBite = l => { Say("Ça mord ! — Espace pour ferrer"); Shout("ca-mord", 0.3, 2); },
            OnMissed = l =>
            {
                Say("Trop tard — il a mangé l'appât");
                // un Espace qui arrive juste après ne relève pas toutes les lignes
                _fishLate = _t + 1.2;
            },
            OnHooked = l => { Say("Ferré ! On hale à la main…"); Shout("ferre", 0.3, 2); },
            OnCatch = (f, kg) =>
            {
                _catch.Add(new FishLot(f.Key, kg, _t));
                FishHold();
                _quests?.Credit(Goal.Fish, kg);
                Say($"Un {f.Name.ToLowerInvariant()} de {Kg(kg)} kg à bord");
            },
            OnLost = (f, kg) => Say(f.Bottom == "roche"
                ? $"La ligne a cassé — le {f.Name.ToLowerInvariant()} s'est enroché"
                : $"La ligne a cassé — le {f.Name.ToLowerInvariant()} est reparti")
        };
        GD.Print($"pêche : {_angling.Species.Count} espèce(s)");
    }

    /// <summary>Les kilos à la française : « 12,4 ».</summary>
    static string Kg(double kg) => kg.ToString("0.#", System.Globalization.CultureInfo.GetCultureInfo("fr-FR"));

    /// <summary>
    /// LE POIDS DES PRISES DANS LA CALE, recalé sur la liste : la cale efface un
    /// colis de moins d'un kilo, et un vivaneau de huit cents grammes y
    /// disparaîtrait. On la remet donc d'accord avec la liste à chaque changement.
    /// </summary>
    void FishHold()
    {
        var ph = _ship.Physics;
        double want = _catch.Sum(l => l.Kg) / 1000, have = ph.CargoOf(FishKind);
        if (Math.Abs(want - have) > 1e-4) ph.LoadCargo(Config.NComp / 2, HoldFloor, 0, want - have, FishKind);
    }

    /// <summary>Le fond sous le navire, en mètres vrais.</summary>
    (double Depth, double Slope) FishBottom()
    {
        var (x, z) = TruePos();
        return _world == null ? (0, 0) : Fishing.BottomAt(_world, x, z);
    }

    double FishSpeed()
    {
        var v = _ship.Physics.Body.Vel;
        return Math.Sqrt(v.X * v.X + v.Z * v.Z);
    }

    /// <summary>
    /// CE QUE LE SUIF A RAPPORTÉ. Le plomb de sonde avait un creux garni de suif à
    /// sa base, et c'est ainsi qu'un pilote LISAIT le fond : du sable s'y colle, la
    /// roche le laisse propre et marqué, et sur un tombant le plomb glisse.
    /// </summary>
    static string Suif(double slope) =>
        slope < 0.05 ? "du sable colle au suif"
        : slope < 0.15 ? "le suif remonte propre et marqué : de la roche"
        : "le plomb a glissé : ça tombe à pic";

    /// <summary>Espace en jeu : ferrer, relever ou lancer. Rend vrai s'il l'a prise.</summary>
    bool FishKey()
    {
        if (_lines == null || _angling == null || _inTitle || _editing || _mother != null) return false;
        var (depth, slope) = FishBottom();
        if (_lines.Biting) { _lines.Strike(depth); return true; }
        if (_t < _fishLate) return true;
        if (_lines.Wet) { _lines.Raise(); Say("Lignes relevées"); return true; }

        double v = FishSpeed();
        if (v > _angling.MaxSpeed)
        {
            Say(FormattableString.Invariant($"Trop d'erre pour pêcher — {v * 1.94384:F1} nœuds : mettez en panne ou mouillez"));
            return true;
        }
        if (depth < 2) { Say("Pas assez d'eau pour pêcher"); return true; }
        if (depth > _angling.LineLength)
        {
            Say(FormattableString.Invariant($"Pas de fond : la sonde file {_angling.LineLength:F0} m sans toucher"));
            return true;
        }
        int n = Fishing.Lines(_angling, _ship.Spec.Equipage, _ship.Spec.Tonnes);
        _lines.Cast(n);
        Say(FormattableString.Invariant($"{n} ligne{(n > 1 ? "s" : "")} à l'eau — fond de {depth:F0} m, {Suif(slope)}"));
        return true;
    }

    void FishTick(double dt)
    {
        if (_lines == null || _angling == null) return;
        if (_lines.Wet)
        {
            // une ligne ne traîne pas derrière un navire qui fait route, ni sous l'affiche du titre
            if (_inTitle || _mother != null) _lines.Raise();
            else if (FishSpeed() > _angling.MaxSpeed * 1.5)
            {
                _lines.Raise();
                Say("Le navire prend de l'erre — on relève les lignes");
            }
            else
            {
                var (depth, slope) = FishBottom();
                _lines.Step(dt * (_fishTest is Vector3 fz ? fz.Z : 1), depth, slope, _sky.Core.DayTime, _fishRng.NextDouble);
            }
        }

        // le poisson se gâte : une fois par seconde suffit
        _fishAcc += dt;
        if (_fishAcc >= 1 && _catch.Count > 0)
        {
            _fishAcc = 0;
            double spoilAt = _t - _angling.SpoilMin * 60;
            double gone = _catch.Where(l => l.CaughtAt <= spoilAt).Sum(l => l.Kg);
            if (gone > 0)
            {
                _catch.RemoveAll(l => l.CaughtAt <= spoilAt);
                FishHold();
                Say($"{Kg(gone)} kg de poisson gâté jetés par-dessus bord");
            }
            // un mot quand la plus vieille prise passe son plein prix — une fois par prise
            double oldest = _catch.Count > 0 ? _catch.Min(l => l.CaughtAt) : double.PositiveInfinity;
            if (oldest < _t - _angling.FreshMin * 60 && oldest > _fishWarned)
            {
                _fishWarned = oldest;
                Say("Le poisson perd sa fraîcheur — il faut le vendre");
            }
        }
        FishLineText();
    }

    /// <summary>La plaque de la pêche, au-dessus de la bourse : les lignes, puis ce qu'on porte et pour combien de temps.</summary>
    void FishLineText()
    {
        if (_fishLine == null || _lines == null || _angling == null) return;
        var parts = new List<string>();
        if (_lines.Wet)
        {
            int bite = _lines.Lines.Count(l => l.State == HandLines.LineState.Bite);
            int haul = _lines.Lines.Count(l => l.State == HandLines.LineState.Haul);
            string s = $"{_lines.Lines.Count} ligne{(_lines.Lines.Count > 1 ? "s" : "")} à l'eau";
            if (haul > 0) s += $" · {haul} qu'on hale";
            if (bite > 0) s += " · ÇA MORD !";
            parts.Add(s);
        }
        if (_catch.Count > 0)
        {
            double oldest = _catch.Min(l => l.CaughtAt);
            double age = (_t - oldest) / 60;
            string fresh = age < _angling.FreshMin
                ? FormattableString.Invariant($"frais encore {Math.Ceiling(_angling.FreshMin - age):F0} min")
                : FormattableString.Invariant($"se gâte — perdu dans {Math.Ceiling(_angling.SpoilMin - age):F0} min");
            parts.Add($"{Kg(_catch.Sum(l => l.Kg))} kg de poisson à bord, {fresh}");
        }
        _fishLine.Text = string.Join("\n", parts);
        _fishLine.Visible = parts.Count > 0 && _hudOn && !_inTitle && !_chartOpen;
    }

    /// <summary>Le prix de ces prises ici et maintenant, en pièces : le cours de l'espèce, à leur fraîcheur.</summary>
    double FishValue(IEnumerable<FishLot> lots)
    {
        if (_angling == null) return 0;
        double sum = 0;
        foreach (var l in lots)
            if (_angling.Of(l.Key) is { } f)
                sum += l.Kg * f.PricePerKg * Fishing.Freshness(_angling, (_t - l.CaughtAt) / 60);
        return sum;
    }

    /// <summary>Vendre toutes les prises d'une espèce au comptoir.</summary>
    void SellFish(string key)
    {
        if (_portHere == null || !_alongside) return;
        var lots = _catch.Where(l => l.Key == key).ToList();
        if (lots.Count == 0) { Say("Rien à vendre"); return; }
        double kg = lots.Sum(l => l.Kg);
        double gain = Js.Round(FishValue(lots));
        _catch.RemoveAll(l => l.Key == key);
        FishHold();
        _purse.Add(gain);
        _quests?.Credit(Goal.Sell, kg);
        string name = _angling?.Of(key)?.Name.ToLowerInvariant() ?? key;
        Say($"{Kg(kg)} kg de {name} — {gain:F0} pièces");
        MarketTick();
    }

    /* L'ESSAI DE BOUT EN BOUT (--peche x,z,accélération) : la coque posée au
       point vrai (x, z), les lignes filées, un pêcheur qui ferre une demi-seconde
       après la touche ; l'étape remplie, retour au ponton, vente au comptoir, puis
       l'achat du cotre avec une bourse garnie pour l'essai. Le temps des lignes
       va à « accélération » fois celui du jeu : quarante kilos en un essai court. */
    Vector3? _fishTest;
    int _fishTestState;
    double _fishTestT, _fishBiteAt = -1;

    void FishTestTick(double dt)
    {
        if (_fishTest is not Vector3 ft || _inTitle || _quests == null || (_quests.Active == null && _fishTestState < 5) || _lines == null) return;
        switch (_fishTestState)
        {
            case 0:
            {
                var o = _sea.Core.Origin;
                _sea.Core.Rebase(ft.X - o.X, ft.Y - o.Z);
                var b = _ship.Physics.Body;
                _anchor2?.Weigh(_ship);
                b.Pos = new Vec3d(0, b.Pos.Y, 0);
                b.Vel = Vec3d.Zero; b.AngVel = Vec3d.Zero;
                _ship.Ctrl.SailsSet = false; _ship.Ctrl.Throttle = 0;
                _ship.SyncTransform();
                _fishTestT = _t; _fishTestState = 1;
                GD.Print(FormattableString.Invariant($"[pêche] posée en ({ft.X:F0}, {ft.Y:F0}), rang « {_rank} », bourse {_purse.Sous} sous"));
                break;
            }
            case 1:
                if (_t - _fishTestT < 3) break;
                FishKey();
                GD.Print($"[pêche] {_lines.Lines.Count} lignes ; fond {FishBottom().Depth:F0} m, pente {FishBottom().Slope:F2}");
                _fishTestState = 2;
                break;
            case 2:
                if (_lines.Biting) { if (_fishBiteAt < 0) _fishBiteAt = _t; else if (_t - _fishBiteAt > 0.5) { FishKey(); _fishBiteAt = -1; } }
                if (Math.Floor(_t / 5) != Math.Floor((_t - dt) / 5))
                    GD.Print(FormattableString.Invariant($"[pêche] t {_t:F0}  {string.Join(" ", _lines.Lines.Select(l => l.State.ToString()[0]))}  à bord {_catch.Sum(l => l.Kg):F1} kg (cale {_ship.Physics.CargoOf(FishKind) * 1000:F1})  étape {_quests.Step + 1} compte {_quests.Counted:F1}  {_sky.Core.DayTime:F1} h"));
                if (_quests.Step >= 1) { _lines.Raise(); BackToBerth(); _fishTestT = _t; _fishTestState = 3; GD.Print("[pêche] étape remplie, retour au ponton"); }
                break;
            case 3:
                if (!_alongside) { if (_t - _fishTestT > 120) { GD.Print("[pêche] jamais à quai"); _fishTestState = 9; } break; }
                foreach (var key in _catch.Select(l => l.Key).Distinct().ToList()) SellFish(key);
                GD.Print(FormattableString.Invariant($"[pêche] vendu ; bourse {_purse.Sous} sous, étape {_quests.Step + 1}, cale poisson {_ship.Physics.CargoOf(FishKind):F3} t"));
                _fishTestT = _t; _fishTestState = 4;
                break;
            case 4:
                // une image au moins : la quête passe à l'achat à sa propre mise à jour
                if (_t - _fishTestT < 1) break;
                _purse.Add(150 * Market.SousParEcu);
                int idx = _paths.FindIndex(p => System.IO.Path.GetFileNameWithoutExtension(p) == "cotre");
                if (YardSpec("cotre") is { } spec) BuyShip(idx, spec);
                GD.Print(FormattableString.Invariant($"[pêche] après l'achat : {_ship.Spec.Name}, bourse {_purse.Sous} sous, rang « {_rank} », quête {(_quests.Active?.Id ?? "achevée")}"));
                _fishTestT = _t; _fishTestState = 5;
                break;
            case 5:
                if (_t - _fishTestT < 1) break;
                GD.Print($"[pêche] ensuite : rang « {_rank} », quête {(_quests.Active?.Id ?? "achevée")}, faites : {string.Join(",", _quests.Done)}");
                _fishTestState = 9;
                break;
        }
    }

    /// <summary>Une partie neuve : ni ligne à l'eau, ni poisson à bord, ni rang.</summary>
    void FishReset()
    {
        _lines?.Raise();
        _catch.Clear();
        _fishWarned = double.NegativeInfinity;
        _rank = "";
    }
}
