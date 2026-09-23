using Godot;
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using NavalSim.Core;

namespace NavalSim;

/// <summary>
/// LES PARTIES ENREGISTRÉES — une par fichier, dans <c>user://parties</c>.
///
/// Ce qu'une sauvegarde retient est ce qu'il faut pour REPRENDRE : où l'on est
/// et sur quel cap, quelle région, quel navire, l'heure et la date, le temps
/// qu'il fait, la bourse, la cale et la poudre, la toile portée, le pavillon,
/// l'état des quêtes, le carnet du capitaine (sa carte, ses traits, son voile)
/// et les coques qui étaient à flot autour de vous.
///
/// Rien n'est recalculé à la reprise : on REPOSE. C'est la même malle que la
/// traversée entre régions (<see cref="Voyage"/>), en plus complet — et comme
/// elle, elle passe par un rechargement de scène quand la région change.
/// </summary>
public partial class ShipDemo
{
    public sealed class SaveState
    {
        [JsonPropertyName("id")] public string Id { get; set; } = "";
        [JsonPropertyName("nom")] public string Nom { get; set; } = "";
        [JsonPropertyName("ecrit")] public string Ecrit { get; set; } = "";
        [JsonPropertyName("lieu")] public string Lieu { get; set; } = "";
        [JsonPropertyName("region")] public string Region { get; set; } = "caraibes";
        [JsonPropertyName("navire")] public string Navire { get; set; } = "";
        [JsonPropertyName("x")] public double X { get; set; }
        [JsonPropertyName("z")] public double Z { get; set; }
        [JsonPropertyName("cap")] public double Cap { get; set; }
        [JsonPropertyName("erre")] public double Erre { get; set; }
        [JsonPropertyName("t")] public double T { get; set; }
        [JsonPropertyName("heure")] public double Heure { get; set; }
        [JsonPropertyName("calDebut")] public string CalDebut { get; set; } = "";
        [JsonPropertyName("calJour")] public int CalJour { get; set; }
        [JsonPropertyName("force")] public double Force { get; set; } = 4;
        [JsonPropertyName("vent")] public double Vent { get; set; } = 105;
        [JsonPropertyName("meteoAuto")] public bool MeteoAuto { get; set; }
        [JsonPropertyName("nuages")] public double Nuages { get; set; } = 0.06;
        [JsonPropertyName("sous")] public long Sous { get; set; }
        [JsonPropertyName("poudre")] public int Poudre { get; set; }
        [JsonPropertyName("voiles")] public bool Voiles { get; set; }
        [JsonPropertyName("toile")] public double Toile { get; set; } = 1;
        [JsonPropertyName("ris")] public int Ris { get; set; }
        [JsonPropertyName("ecoute")] public double Ecoute { get; set; }
        [JsonPropertyName("pavillon")] public bool Pavillon { get; set; } = true;
        [JsonPropertyName("cargo")] public List<Colis> Cargo { get; set; } = new();
        [JsonPropertyName("flotte")] public List<Coque> Flotte { get; set; } = new();
        /// <summary>Les quêtes et le carnet, tels qu'ils s'écrivent déjà dans user:// — recopiés ici.</summary>
        [JsonPropertyName("quetes")] public string Quetes { get; set; } = "";
        [JsonPropertyName("carnet")] public string Carnet { get; set; } = "";
    }

    public sealed class Colis
    {
        [JsonPropertyName("cale")] public int Cale { get; set; }
        [JsonPropertyName("niveau")] public double Niveau { get; set; }
        [JsonPropertyName("bord")] public double Bord { get; set; }
        [JsonPropertyName("kg")] public double Kg { get; set; }
        [JsonPropertyName("nature")] public string Nature { get; set; } = "lest";
    }

    public sealed class Coque
    {
        [JsonPropertyName("fiche")] public string Fiche { get; set; } = "";
        [JsonPropertyName("x")] public double X { get; set; }
        [JsonPropertyName("z")] public double Z { get; set; }
        [JsonPropertyName("cap")] public double Cap { get; set; }
        [JsonPropertyName("erre")] public double Erre { get; set; }
        [JsonPropertyName("hostile")] public bool Hostile { get; set; }
    }

    /// <summary>La partie qu'on vient de choisir : posée avant le rechargement de scène, reprise après.</summary>
    static SaveState? _loading;
    /// <summary>Celle qu'on joue : « Sauvegarder » réécrit son fichier plutôt que d'en semer un second.</summary>
    string _gameId = "";

    const string SaveDir = "user://parties";

    static string PathOf(string id) => $"{SaveDir}/{id}.json";

    // ------------------------------------------------------------------
    //  ÉCRIRE
    // ------------------------------------------------------------------

    /// <summary>Ce que le bord est, à cet instant.</summary>
    SaveState Collect()
    {
        var p = _ship.Physics;
        var b = p.Body;
        var (tx, tz) = TruePos();
        var f = b.Quat.Rotate(new Vec3d(0, 0, 1));
        var s = new SaveState
        {
            Id = _gameId.Length > 0 ? _gameId : Guid.NewGuid().ToString("N")[..8],
            Ecrit = DateTime.Now.ToString("yyyy-MM-dd HH:mm"),
            Region = _world?.Region.Key ?? "caraibes",
            Navire = _paths.Count > 0 ? System.IO.Path.GetFileName(_paths[_index]) : "",
            X = tx, Z = tz,
            Cap = (Math.Atan2(-f.X, f.Z) * 180 / Math.PI + 360) % 360,       // l'est est −x
            Erre = Math.Sqrt(b.Vel.X * b.Vel.X + b.Vel.Z * b.Vel.Z),
            T = _t, Heure = _sky.Core.DayTime,
            CalDebut = _calendar.Start.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
            CalJour = _calendar.Day,
            Force = _force, Vent = _windDeg, MeteoAuto = _weather.On, Nuages = _cloud,
            Sous = _purse.Sous, Poudre = p.Powder,
            Voiles = _ship.Ctrl.SailsSet, Toile = _ship.Ctrl.Canvas, Ris = _reef,
            Ecoute = _ship.Ctrl.Sheet, Pavillon = _colours
        };
        foreach (var c in p.Cargo)
            s.Cargo.Add(new Colis { Cale = c.Hold, Niveau = c.Level, Bord = c.Side, Kg = c.Kg, Nature = c.Kind });
        // les coques à flot autour de vous ; les spectres ne se sauvent pas
        var o = _sea.Core.Origin;
        foreach (var other in _others)
        {
            if (other.IsGhost) continue;
            var ob = other.Physics.Body;
            var of = ob.Quat.Rotate(new Vec3d(0, 0, 1));
            s.Flotte.Add(new Coque
            {
                Fiche = other.Spec.Id + ".json",
                X = o.X + ob.Pos.X, Z = o.Z + ob.Pos.Z,
                Cap = (Math.Atan2(-of.X, of.Z) * 180 / Math.PI + 360) % 360,
                Erre = Math.Sqrt(ob.Vel.X * ob.Vel.X + ob.Vel.Z * ob.Vel.Z),
                Hostile = _hostile.ContainsKey(other)
            });
        }
        s.Quetes = _quests?.ToJson() ?? "";
        s.Carnet = _book?.ToJson() ?? "";
        s.Lieu = Where();
        s.Nom = $"{_ship.Spec.Name} — {s.Lieu}, {_calendar.Date:dd/MM/yyyy}";
        return s;
    }

    /// <summary>Où l'on est, en mots : le port si l'on y est, sinon la terre la plus proche.</summary>
    string Where()
    {
        if (_portHere != null) return _portHere.Name;
        var (tx, tz) = TruePos();
        Isle? near = null; double best = double.MaxValue;
        foreach (var i in _world?.Isles ?? new List<Isle>())
        {
            double d = Math.Sqrt((i.X - tx) * (i.X - tx) + (i.Z - tz) * (i.Z - tz));
            if (d < best) { best = d; near = i; }
        }
        if (near == null) return "en mer";
        return best < 900 ? near.Name : $"{best / 1852:F0} milles de {near.Name}";
    }

    /// <summary>Enregistrer la partie en cours. Le même fichier tant qu'on joue la même partie.</summary>
    void SaveGame()
    {
        DirAccess.MakeDirRecursiveAbsolute(ProjectSettings.GlobalizePath(SaveDir));
        var s = Collect();
        _gameId = s.Id;
        using var f = FileAccess.Open(PathOf(s.Id), FileAccess.ModeFlags.Write);
        if (f == null) { Say("La partie n'a pas pu être enregistrée"); return; }
        f.StoreString(JsonSerializer.Serialize(s, new JsonSerializerOptions { WriteIndented = true }));
        f.Flush();
        SaveQuests();
        SaveBook();
        Say("Partie enregistrée — " + s.Nom);
        GD.Print($"partie enregistrée : {PathOf(s.Id)}");
    }

    // ------------------------------------------------------------------
    //  LIRE
    // ------------------------------------------------------------------

    /// <summary>Les parties qu'on peut reprendre, la plus fraîche d'abord.</summary>
    static List<SaveState> Saves()
    {
        var outp = new List<SaveState>();
        var dir = DirAccess.Open(SaveDir);
        if (dir == null) return outp;
        foreach (string name in dir.GetFiles())
        {
            if (!name.EndsWith(".json")) continue;
            try
            {
                using var f = FileAccess.Open($"{SaveDir}/{name}", FileAccess.ModeFlags.Read);
                if (f == null) continue;
                var s = JsonSerializer.Deserialize<SaveState>(f.GetAsText());
                if (s != null) { s.Id = System.IO.Path.GetFileNameWithoutExtension(name); outp.Add(s); }
            }
            catch (JsonException e) { GD.PushWarning($"[partie] {name} illisible : {e.Message}"); }
        }
        outp.Sort((a, b) => string.CompareOrdinal(b.Ecrit, a.Ecrit));
        return outp;
    }

    /// <summary>
    /// Reprendre une partie : la malle est posée, la scène rechargée. Tout le
    /// reste se fait au réveil, dans <see cref="Resume"/> — une partie peut être
    /// d'une autre région que celle qui est chargée.
    /// </summary>
    void LoadGame(SaveState s)
    {
        /* LE CARNET ET LES QUÊTES SONT DÉJÀ DES FICHIERS : on les repose là où la
           scène neuve ira les lire, plutôt que d'ouvrir un second chemin qui
           finirait par dire autre chose. */
        string carnet = s.Region is "" or "caraibes" ? "user://carnet.json" : $"user://carnet-{s.Region}.json";
        if (s.Carnet.Length > 0) { using var f = FileAccess.Open(carnet, FileAccess.ModeFlags.Write); f?.StoreString(s.Carnet); }
        if (s.Quetes.Length > 0) { using var f = FileAccess.Open(QuestPath, FileAccess.ModeFlags.Write); f?.StoreString(s.Quetes); }
        _loading = s;
        GetTree().ReloadCurrentScene();
    }

    /// <summary>
    /// LA REPRISE, à la place de la mise à quai : le bord est reposé tel qu'il
    /// était. Faux s'il n'y a rien à reprendre.
    /// </summary>
    bool Resume()
    {
        if (_loading is not { } s || _world == null) return false;
        _loading = null;
        _gameId = s.Id;
        var p = _ship.Physics;
        var b = p.Body;

        _purse = new Purse(s.Sous);
        p.Powder = Math.Min(s.Poudre, p.PowderMax);
        p.ClearCargo();
        foreach (var c in s.Cargo) p.LoadCargo(c.Cale, c.Niveau, c.Bord, c.Kg / 1000, c.Nature);

        _t = s.T;
        _calendar.SetStart(s.CalDebut);
        for (int d = 0; d < s.CalJour; d++) _calendar.NextDay();
        _sky.Core.SetTimeOfDay(s.Heure, _sky.Latitude);

        _force = s.Force; _windDeg = s.Vent; _cloud = s.Nuages;
        _weather.On = s.MeteoAuto;
        if (_weather.On) _weather.Sync(_force, _windDeg);
        Restate();

        // l'origine glisse à la position vraie : la coque reste près de zéro
        var o = _sea.Core.Origin;
        _sea.Core.Rebase(s.X - o.X, s.Z - o.Z);
        b.Pos = new Vec3d(0, _eqY, 0);
        b.Quat = Quatd.FromAxisAngle(new Vec3d(0, 1, 0), -s.Cap * Math.PI / 180);
        b.AngVel = Vec3d.Zero;
        b.Vel = b.Quat.Rotate(new Vec3d(0, 0, s.Erre));
        _ship.Ctrl.SailsSet = s.Voiles;
        _ship.Ctrl.Canvas = s.Toile;
        _ship.Ctrl.Sheet = s.Ecoute;
        _reef = Math.Clamp(s.Ris, 0, Reefs.Length - 1);
        _colours = s.Pavillon;
        _ship.ShowColours(_colours, true);
        _ship.SyncTransform();

        // le carnet et les quêtes ont été reposés dans leurs fichiers avant le rechargement

        // les coques qui étaient à flot : remises à leur place et sur leur erre
        foreach (var c in s.Flotte)
        {
            int idx = _paths.FindIndex(q => System.IO.Path.GetFileName(q) == c.Fiche);
            if (idx < 0) continue;
            int before = _others.Count;
            SpawnFleet(1, idx);
            if (_others.Count == before) continue;
            var other = _others[^1];
            var ob = other.Physics.Body;
            var no = _sea.Core.Origin;
            ob.Pos = new Vec3d(c.X - no.X, ob.Pos.Y, c.Z - no.Z);
            ob.Quat = Quatd.FromAxisAngle(new Vec3d(0, 1, 0), -c.Cap * Math.PI / 180);
            ob.Vel = ob.Quat.Rotate(new Vec3d(0, 0, c.Erre));
            ob.AngVel = Vec3d.Zero;
            other.SyncTransform();
            if (c.Hostile) _hostile[other] = (_ship, 0);
        }

        _askTitle = false;
        _reck?.Fix(s.X, s.Z);
        GD.Print(FormattableString.Invariant(
            $"partie reprise : « {s.Nom} », {s.Region}, {_calendar.Date:yyyy-MM-dd} {s.Heure:F1} h, bourse {s.Sous} sous, {s.Flotte.Count} coque(s) autour"));
        ShowNotice(s.Nom, $"Vous reprenez la partie là où vous l'aviez laissée : {s.Lieu}, le "
            + _calendar.Date.ToString("d MMMM yyyy", new System.Globalization.CultureInfo("fr-FR"))
            + $", {(int)s.Heure} h.");
        return true;
    }

    // ------------------------------------------------------------------
    //  LES ENTRÉES DE MENU
    // ------------------------------------------------------------------

    /// <summary>L'Histoire : reprendre une partie, en commencer une neuve, ou en effacer une.</summary>
    void StoryItems()
    {
        _eraseId = "";
        ClearItems();
        var list = Saves();
        foreach (var s in list)
        {
            var save = s;
            Item($"{s.Nom}   ({s.Ecrit})", () => LoadGame(save), 28);
        }
        Item("Nouvelle partie", NewGame, 34);
        if (list.Count > 0) Item("Effacer une partie", EraseItems, 28);
        Item("Retour", MainItems, 34);
        ShowPick();
    }

    /// <summary>La partie qu'on s'apprête à effacer : le premier appui demande, le second fait.</summary>
    string _eraseId = "";

    /// <summary>
    /// EFFACER, EN DEUX TEMPS. Une partie effacée ne revient pas : le premier
    /// appui met la ligne en garde, le second seulement emporte le fichier.
    /// </summary>
    void EraseItems()
    {
        ClearItems();
        foreach (var s in Saves())
        {
            var save = s;
            bool armed = _eraseId == s.Id;
            Item(armed ? $"Effacer pour de bon : {s.Nom} ?" : $"{s.Nom}   ({s.Ecrit})",
                 () =>
                 {
                     if (_eraseId != save.Id) { _eraseId = save.Id; EraseItems(); return; }
                     DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath(PathOf(save.Id)));
                     GD.Print($"partie effacée : {save.Nom}");
                     if (_gameId == save.Id) _gameId = "";
                     _eraseId = "";
                     EraseItems();
                 }, 28);
        }
        if (_titleItems.Count == 0) Item("Plus aucune partie", () => { }, 28);
        Item("Retour", StoryItems, 34);
        ShowPick();
    }

    /// <summary>Une partie neuve : le port de départ, la première page du carnet.</summary>
    void NewGame()
    {
        _gameId = Guid.NewGuid().ToString("N")[..8];
        Story();
    }
}
