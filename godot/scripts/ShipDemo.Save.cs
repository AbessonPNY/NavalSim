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
        /// <summary>
        /// D'OÙ ELLE VIENT — « histoire », « mission » ou « libre ». Une partie
        /// s'enregistre dans le menu qui l'a ouverte et nulle part ailleurs : une
        /// sortie en jeu libre n'a rien à faire dans la liste de l'Histoire, ni
        /// une mission dans celle du jeu libre (signalé).
        /// Vide dans les vieux fichiers : on le devine alors sur la quête.
        /// </summary>
        [JsonPropertyName("mode")] public string Mode { get; set; } = "";
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
        /// <summary>
        /// Le défilement du jour, en heures de ciel par minute réelle. Zéro veut
        /// dire « pas enregistré » — une partie d'avant garde alors le taux du
        /// démarrage plutôt que d'arrêter le soleil, ce qui serait le pire des
        /// deux sens possibles pour un champ manquant.
        /// </summary>
        [JsonPropertyName("defilement")] public double Defilement { get; set; }
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
        /* LE JOURNAL DE BORD ET LA CARTE VIVENT DANS LA PARTIE, et nulle part
           ailleurs. Le carnet de carte a longtemps été un fichier GLOBAL que la
           reprise reposait : une sortie neuve rouvrait celui de la précédente,
           avec ses traits et son voile déjà levé (signalé). Il est maintenant
           enregistré RÉGION PAR RÉGION — une partie peut avoir passé à la Tortue,
           et ce qu'elle y a relevé lui appartient aussi. */
        [JsonPropertyName("journal")] public string Journal { get; set; } = "";
        /// <summary>Le carnet de la seule région ouverte — LU pour les parties d'avant, plus écrit.</summary>
        [JsonPropertyName("carnet")] public string Carnet { get; set; } = "";
        /// <summary>Un carnet par région visitée, la clé de la région pour clé.</summary>
        [JsonPropertyName("carnets")] public Dictionary<string, string> Carnets { get; set; } = new();
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
            Defilement = _sky.DayRate,
            Sous = _purse.Sous, Poudre = p.Powder,
            Voiles = _ship.Ctrl.SailsSet, Toile = _ship.Ctrl.Canvas, Ris = _reef,
            Ecoute = _ship.Ctrl.Sheet, Pavillon = _colours,
            /* TROIS MENUS, TROIS LISTES : la quête en cours dit laquelle. Un
               chapitre, c'est l'Histoire ; une autre quête, c'est une mission ;
               pas de quête, c'est le jeu libre. */
            Mode = _quests?.Active == null ? "libre"
                 : _quests.Active.Kind == "story" ? "histoire" : "mission"
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
        s.Journal = _journal.Pages.Count > 0 ? _journal.ToJson() : "";
        s.Carnets = BooksNow();
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

    /// <summary>
    /// Enregistrer la partie en cours. Le même fichier tant qu'on joue la même
    /// partie — l'identifiant est gardé, et repris au chargement.
    /// </summary>
    /// <param name="asked">
    /// Vrai si le JOUEUR l'a demandé, faux si c'est le jeu qui enregistre en
    /// passant (retour au menu, sortie du jeu).
    /// </param>
    void SaveGame(bool asked = true)
    {
        /* UNE ESCARMOUCHE NE SE REPREND PAS. C'est une bataille montée d'un bloc,
           sans bourse, sans quête et sans lendemain : l'enregistrer sèmerait des
           parties que ni l'Histoire, ni les Missions, ni le Jeu libre ne peuvent
           montrer — et le menu d'Échap l'écrit à chaque retour au titre. */
        if (_skirmish) { Say("Une escarmouche ne s'enregistre pas"); return; }

        /* ET LE JEU LIBRE NE S'ENREGISTRE PAS TOUT SEUL (signalé). Sortir au menu
           ou quitter écrivait une partie à chaque fois, et comme chaque nouvelle
           partie reçoit un identifiant neuf, la liste du Jeu libre s'allongeait
           d'une ligne par session — des parties que personne n'avait demandé de
           garder, et qu'il fallait effacer une à une.

           La règle : en jeu libre, le jeu n'écrit de lui-même que ce qui a DÉJÀ
           un nom, c'est-à-dire ce que le joueur a choisi de garder une fois. Une
           partie neuve reste en l'air tant qu'il ne l'enregistre pas — le menu
           d'Échap le dit en toutes lettres (« Partie non enregistrée ») et sa
           première entrée est justement de l'enregistrer.

           Une quête en cours, elle, s'enregistre toujours : une Histoire ou une
           Mission perdue en fermant le jeu serait une tout autre affaire. */
        if (!asked && _gameId.Length == 0 && _quests?.Active == null) return;
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

    /// <summary>
    /// LE MODE D'UNE PARTIE, y compris des anciennes. Avant que le fichier ne le
    /// dise, une partie d'histoire est celle qui avait une quête en cours : c'est
    /// exactement ce que son carnet de quêtes porte (« active »), et cela suffit
    /// à ranger les parties déjà écrites sans rien leur demander.
    /// </summary>
    string ModeOf(SaveState s)
    {
        if (s.Mode.Length > 0) return s.Mode;
        var m = System.Text.RegularExpressions.Regex.Match(s.Quetes, "\"active\"\\s*:\\s*\"([^\"]+)\"");
        if (!m.Success) return "libre";
        return _quests?.ById(m.Groups[1].Value)?.Kind == "story" ? "histoire" : "mission";
    }

    /// <summary>Les parties d'un mode qu'on peut reprendre, la plus fraîche d'abord.</summary>
    List<SaveState> Saves(string mode)
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
                if (s == null || ModeOf(s) != mode) continue;
                s.Id = System.IO.Path.GetFileNameWithoutExtension(name);
                outp.Add(s);
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
        /* LES CARNETS ET LES QUÊTES SONT DÉJÀ DES FICHIERS : on les repose là où la
           scène neuve ira les lire, plutôt que d'ouvrir un second chemin qui
           finirait par dire autre chose. Et les régions que cette partie-là n'a
           pas visitées sont EFFACÉES : sans quoi on hériterait du carnet d'une
           autre partie en traversant. */
        var books = new Dictionary<string, string>(s.Carnets);
        // une partie d'avant n'en portait qu'un, sans dire de quelle région : c'est la sienne
        if (books.Count == 0 && s.Carnet.Length > 0) books[s.Region is "" ? "caraibes" : s.Region] = s.Carnet;
        PutBooks(books);
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

        if (s.Journal.Length > 0) _journal.FromJson(s.Journal);
        _t = s.T;
        _calendar.SetStart(s.CalDebut);
        for (int d = 0; d < s.CalJour; d++) _calendar.NextDay();
        _sky.Core.SetTimeOfDay(s.Heure, _sky.Latitude);

        _force = s.Force; _windDeg = s.Vent; _cloud = s.Nuages;
        // le pas du jour qu'elle avait : zéro veut dire « pas enregistré »
        if (s.Defilement > 0) SetDayRate(s.Defilement);
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
    void StoryItems() => GameItems("histoire", "Nouvelle partie", NewGame);

    /// <summary>Le jeu libre : commencer, ou reprendre.</summary>
    void FreeItems() => GameItems("libre", "Nouvelle partie", NewFree);

    /// <summary>
    /// UN MENU DE MODE, EN DEUX TEMPS : « Nouvelle partie » et « Charger une
    /// partie » (demandé). La liste des parties commencées était à plat sous les
    /// deux boutons, si bien que le premier regard tombait sur des noms de
    /// sauvegardes plutôt que sur la question qu'on se pose en arrivant —
    /// commencer, ou reprendre. Elle est maintenant derrière son propre mot, et
    /// « Charger » ne paraît que s'il y a quelque chose à charger.
    /// </summary>
    void GameItems(string mode, string label, Action neuve)
    {
        _eraseId = "";
        ClearItems();
        Item(label, neuve, 34);
        if (Saves(mode).Count > 0)
            Item("Charger une partie", () => LoadItems(mode, label, neuve), 34);
        Item("Retour", MainItems, 34);
        ShowPick();
    }

    /// <summary>
    /// LES PARTIES COMMENCÉES, la plus fraîche d'abord, chacune avec la date
    /// RÉELLE où on l'a laissée — celle de la montre et non celle du bord
    /// (demandé). Les deux se lisent sur la même ligne : le nom porte le lieu et
    /// la date de jeu, la fin dit quand on y a joué.
    /// </summary>
    void LoadItems(string mode, string label, Action neuve)
    {
        _eraseId = "";
        ClearItems();
        var list = Saves(mode);
        foreach (var s in list)
        {
            var save = s;
            Item($"{s.Nom}   ·   {Written(s)}", () => LoadGame(save), 26);
        }
        if (list.Count == 0) Item("Aucune partie enregistrée", () => { }, 28);
        else Item("Effacer une partie", () => EraseItems(mode, label, neuve), 28);
        Item("Retour", () => GameItems(mode, label, neuve), 34);
        ShowPick();
    }

    /// <summary>
    /// Quand on y a joué, en clair. <c>Ecrit</c> est écrit en « yyyy-MM-dd HH:mm »
    /// pour que le tri des parties soit celui des chaînes ; il se lit mal, et ce
    /// n'est pas ce qu'on montre.
    /// </summary>
    static string Written(SaveState s)
    {
        if (!DateTime.TryParseExact(s.Ecrit, "yyyy-MM-dd HH:mm",
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out var d)) return s.Ecrit;
        var today = DateTime.Now.Date;
        string jour = d.Date == today ? "aujourd'hui"
                    : d.Date == today.AddDays(-1) ? "hier"
                    : "le " + d.ToString("dd/MM/yyyy");
        return $"{jour} à {d:HH}h{d:mm}";
    }

    /// <summary>La partie qu'on s'apprête à effacer : le premier appui demande, le second fait.</summary>
    string _eraseId = "";

    /// <summary>
    /// EFFACER, EN DEUX TEMPS. Une partie effacée ne revient pas : le premier
    /// appui met la ligne en garde, le second seulement emporte le fichier.
    /// </summary>
    void EraseItems(string mode, string label, Action neuve)
    {
        ClearItems();
        foreach (var s in Saves(mode))
        {
            var save = s;
            bool armed = _eraseId == s.Id;
            Item(armed ? $"Effacer pour de bon : {s.Nom} ?" : $"{s.Nom}   ·   {Written(s)}",
                 () =>
                 {
                     if (_eraseId != save.Id) { _eraseId = save.Id; EraseItems(mode, label, neuve); return; }
                     DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath(PathOf(save.Id)));
                     GD.Print($"partie effacée : {save.Nom}");
                     if (_gameId == save.Id) _gameId = "";
                     _eraseId = "";
                     EraseItems(mode, label, neuve);
                 }, 28);
        }
        if (_titleItems.Count == 0) Item("Plus aucune partie", () => { }, 28);
        // on revient à la LISTE d'où l'on vient, pas au menu du mode
        Item("Retour", () => LoadItems(mode, label, neuve), 34);
        ShowPick();
    }

    /* UNE PARTIE NEUVE N'A PAS ENCORE DE NUMÉRO : Home() efface celui de la partie
       qu'on quittait, et le premier enregistrement en tire un. Lui en donner un
       ici ne servirait à rien qu'à le voir effacé juste après. */

    /// <summary>Une partie neuve : le port de départ, la première page du carnet.</summary>
    void NewGame() => Story();

    /// <summary>Une sortie neuve en jeu libre.</summary>
    void NewFree() => FreePlay();
}
