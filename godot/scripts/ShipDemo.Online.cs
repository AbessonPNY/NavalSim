using Godot;
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using NavalSim.Core;

namespace NavalSim;

/// <summary>
/// LES PARTIES EN LIGNE (demandé) : une partie enregistrée s'envoie au serveur
/// (<c>serveur/api.php</c>), avec ses comptes, et se reprend de là sur n'importe
/// quel poste. Le menu est derrière « Charger une partie ».
///
/// LE JOUEUR EST UNE CLÉ, PAS UN COMPTE : tirée au hasard au premier envoi,
/// gardée dans <c>user://en-ligne.ini</c> avec le pseudo. Pas de mot de passe ;
/// pour changer de poste, on copie la clé ici et on la colle là-bas. Le serveur
/// n'en garde que l'empreinte.
///
/// L'adresse du serveur est dans settings.json → <c>enLigne.url</c> ; vide, le
/// menu dit qu'il n'y en a pas.
/// </summary>
public partial class ShipDemo
{
    /// <summary>
    /// LES COMPTES D'UNE PARTIE : ce qu'elle a fait, depuis son premier jour.
    /// Les compteurs vivent d'un bout à l'autre de la partie et traversent les
    /// régions (statique, comme la malle) ; le reste est un relevé de l'instant,
    /// réécrit à chaque enregistrement pour que le serveur n'ait rien à recalculer.
    /// </summary>
    public sealed class Tally
    {
        // --- ce qui s'additionne
        [JsonPropertyName("milles")] public double Milles { get; set; }
        [JsonPropertyName("ports")] public List<string> Ports { get; set; } = new();
        [JsonPropertyName("coups")] public int Coups { get; set; }
        [JsonPropertyName("bordees")] public int Bordees { get; set; }
        [JsonPropertyName("coules")] public int Coules { get; set; }
        [JsonPropertyName("naufrages")] public int Naufrages { get; set; }
        [JsonPropertyName("captures")] public int Captures { get; set; }
        [JsonPropertyName("poissons")] public int Poissons { get; set; }
        [JsonPropertyName("kgPeche")] public double KgPeche { get; set; }
        [JsonPropertyName("lambis")] public int Lambis { get; set; }
        [JsonPropertyName("perles")] public int Perles { get; set; }
        [JsonPropertyName("ecusRepeches")] public int EcusRepeches { get; set; }
        [JsonPropertyName("coffres")] public int Coffres { get; set; }
        /// <summary>Ce que les ventes et les contrats ont rapporté, en sous.</summary>
        [JsonPropertyName("gains")] public double Gains { get; set; }

        // --- le relevé de l'instant (écrit par Collect, jamais relu)
        [JsonPropertyName("sous")] public long Sous { get; set; }
        [JsonPropertyName("ecus")] public double Ecus { get; set; }
        [JsonPropertyName("navire")] public string Navire { get; set; } = "";
        [JsonPropertyName("rang")] public string Rang { get; set; } = "";
        [JsonPropertyName("jours")] public int Jours { get; set; }
        [JsonPropertyName("caleKg")] public double CaleKg { get; set; }
        [JsonPropertyName("tresors")] public int Tresors { get; set; }
        [JsonPropertyName("pecheABord")] public double PecheABord { get; set; }
    }

    /// <summary>Les comptes de la partie qu'on joue : remis à zéro par Home, reposés par Resume.</summary>
    static Tally _tally = new();
    /// <summary>Les navires que VOS boulets ont touchés : s'ils sombrent, c'est à vous qu'on le compte.</summary>
    readonly HashSet<ShipNode> _hitByMe = new();

    void TallyReset()
    {
        _tally = new Tally();
        _hitByMe.Clear();
    }

    /// <summary>Le relevé de l'instant, posé sur les comptes juste avant qu'ils s'écrivent.</summary>
    Tally TallyNow()
    {
        var t = _tally;
        t.Sous = _purse.Sous;
        t.Ecus = Math.Round((double)_purse.Sous / Market.SousParEcu, 1);
        t.Navire = _ship.Styled;
        t.Rang = _rank;
        t.Jours = _calendar.Day;
        double kg = 0;
        foreach (var c in _ship.Physics.Cargo) if (c.Kind != "lest") kg += c.Kg;
        t.CaleKg = Math.Round(kg);
        int n = 0;
        foreach (var (_, k) in _treasureHold) n += k;
        t.Tresors = n;
        double fish = 0;
        foreach (var l in _catch) fish += l.Kg;
        t.PecheABord = Math.Round(fish, 1);
        t.Milles = Math.Round(t.Milles, 2);
        return t;
    }

    /// <summary>Le chemin fait, à chaque image : la vitesse plutôt que la position, qu'un saut d'origine fausserait.</summary>
    void TallySail(double dt)
    {
        if (_inTitle || _world == null || dt <= 0 || dt > 0.5) return;
        _tally.Milles += _ship.Physics.Body.Vel.LengthXZ * dt / MetresPerMinute;
    }

    void TallyPort(Isle port)
    {
        if (!_tally.Ports.Contains(port.Key)) _tally.Ports.Add(port.Key);
    }

    // ------------------------------------------------------------------
    //  LE SERVEUR
    // ------------------------------------------------------------------

    /// <summary>settings.json → enLigne.url : l'adresse de api.php. Vide, pas de jeu en ligne.</summary>
    string _onlineUrl = "";
    /// <summary>L'identité du joueur ; l'essai (--en-ligne-essai) en prend une à part.</summary>
    string OnlineIni = "user://en-ligne.ini";

    string OnlineKey
    {
        get
        {
            var cf = new ConfigFile();
            return cf.Load(OnlineIni) == Error.Ok ? (string)cf.GetValue("joueur", "cle", "") : "";
        }
    }

    string OnlinePseudo
    {
        get
        {
            var cf = new ConfigFile();
            return cf.Load(OnlineIni) == Error.Ok ? (string)cf.GetValue("joueur", "pseudo", "") : "";
        }
    }

    void OnlineRemember(string key, string pseudo)
    {
        var cf = new ConfigFile();
        cf.Load(OnlineIni);
        cf.SetValue("joueur", "cle", key);
        cf.SetValue("joueur", "pseudo", pseudo);
        cf.Save(OnlineIni);
    }

    /// <summary>
    /// LA SAUVEGARDE AUTOMATIQUE EN LIGNE, choisie par le joueur au menu d'Échap :
    /// chaque enregistrement, demandé ou fait en partant, part aussi au serveur.
    /// </summary>
    bool OnlineAuto
    {
        get
        {
            var cf = new ConfigFile();
            return cf.Load(OnlineIni) == Error.Ok && (bool)cf.GetValue("joueur", "auto", false);
        }
        set
        {
            var cf = new ConfigFile();
            cf.Load(OnlineIni);
            cf.SetValue("joueur", "auto", value);
            cf.Save(OnlineIni);
        }
    }

    /// <summary>Un serveur, une clé, un pseudo : on peut lui parler.</summary>
    bool OnlineReady => _onlineUrl.Length > 0 && KeyShape(OnlineKey) && OnlinePseudo.Length > 0;

    /// <summary>
    /// SE FAIRE CONNAÎTRE : la clé est tirée si l'on n'en a pas, le pseudo
    /// déclaré. <paramref name="done"/> reçoit vide si c'est fait, sinon le mot du serveur.
    /// </summary>
    void Register(string pseudo, Action<string> done)
    {
        pseudo = pseudo.Trim();
        if (pseudo.Length < 3) { done("Trois lettres au moins"); return; }
        string key = KeyShape(OnlineKey) ? OnlineKey : NewOnlineKey();
        Online("joueur", new JsonObject { ["pseudo"] = pseudo }, (r, why) =>
        {
            if (r == null) { done(why); return; }
            OnlineRemember(key, pseudo);
            done("");
        }, key);
    }

    /// <summary>256 bits tirés par le système, en hexadécimal : c'est tout ce qui dit « c'est moi ».</summary>
    static string NewOnlineKey() =>
        Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();

    static bool KeyShape(string k) =>
        k.Length == 64 && System.Text.RegularExpressions.Regex.IsMatch(k, "^[0-9a-f]{64}$");

    /// <summary>
    /// UN APPEL AU SERVEUR : une enveloppe JSON postée, une réponse JSON rendue.
    /// <paramref name="done"/> reçoit la réponse si elle dit « ok », sinon null et
    /// le mot du serveur (ou celui du réseau).
    /// </summary>
    void Online(string action, JsonObject body, Action<JsonNode?, string> done, string? key = null)
    {
        body["a"] = action;
        body["cle"] = key ?? OnlineKey;
        var http = new HttpRequest { Timeout = 20, ProcessMode = ProcessModeEnum.Always };
        AddChild(http);
        http.RequestCompleted += (result, code, _, raw) =>
        {
            http.QueueFree();
            if (result != (long)HttpRequest.Result.Success) { done(null, "serveur injoignable"); return; }
            JsonNode? r = null;
            try { r = JsonNode.Parse(raw); } catch (JsonException) { }
            if (r?["ok"]?.GetValue<bool>() == true) { done(r, ""); return; }
            string why = r?["erreur"]?.GetValue<string>() ?? $"réponse {code} du serveur";
            GD.PushWarning($"[en ligne] {action} : {why}");
            done(null, why);
        };
        var err = http.Request(_onlineUrl, new[] { "Content-Type: application/json" },
                               HttpClient.Method.Post, body.ToJsonString());
        if (err != Error.Ok) { http.QueueFree(); done(null, "requête refusée : " + err); }
    }

    // ------------------------------------------------------------------
    //  AU MENU D'ÉCHAP : LE CHOIX, AU MOMENT D'ENREGISTRER
    // ------------------------------------------------------------------

    CheckButton? _autoOnline;
    /// <summary>La question du pseudo, à la place des entrées, la première fois qu'on coche.</summary>
    VBoxContainer? _pseudoAsk;
    LineEdit? _pseudoField;
    Label? _pseudoNote;

    void BuildAutoOnline(VBoxContainer box, VBoxContainer stack)
    {
        _autoOnline = new CheckButton { Text = "Sauvegarde automatique en ligne", FocusMode = Control.FocusModeEnum.None };
        _autoOnline.AddThemeFontSizeOverride("font_size", 15);
        _autoOnline.Toggled += on =>
        {
            if (!on) { OnlineAuto = false; return; }
            if (OnlineReady) { OnlineAuto = true; PushNow(); return; }
            // pas encore connu du serveur : le pseudo d'abord, la case se cochera après
            _autoOnline.SetPressedNoSignal(false);
            _pseudoField!.Text = OnlinePseudo;
            _pseudoNote!.Text = "Ce nom sera le vôtre sur le serveur.";
            _pauseMain!.Visible = false;
            _pseudoAsk!.Visible = true;
            _pseudoField.CallDeferred(Control.MethodName.GrabFocus);
        };
        box.AddChild(_autoOnline);

        var ask = _pseudoAsk = new VBoxContainer { Visible = false };
        ask.AddThemeConstantOverride("separation", 8);
        stack.AddChild(ask);
        var q = new Label { Text = "Votre pseudo de capitaine", HorizontalAlignment = HorizontalAlignment.Center };
        q.AddThemeFontSizeOverride("font_size", 18);
        q.AddThemeColorOverride("font_color", new Color(0.92f, 0.78f, 0.36f));
        ask.AddChild(q);
        _pseudoField = new LineEdit { PlaceholderText = "3 à 24 lettres", MaxLength = 24 };
        _pseudoField.AddThemeFontSizeOverride("font_size", 18);
        ask.AddChild(_pseudoField);
        _pseudoNote = new Label { HorizontalAlignment = HorizontalAlignment.Center, AutowrapMode = TextServer.AutowrapMode.WordSmart };
        _pseudoNote.AddThemeFontSizeOverride("font_size", 12);
        _pseudoNote.AddThemeColorOverride("font_color", new Color(0.70f, 0.74f, 0.78f));
        ask.AddChild(_pseudoNote);
        void Submit()
        {
            _pseudoNote!.Text = "Le serveur…";
            Register(_pseudoField!.Text, why =>
            {
                if (why.Length > 0) { _pseudoNote.Text = why; return; }
                OnlineAuto = true;
                CloseAsk();
                PauseNote();
                PushNow();
            });
        }
        _pseudoField.TextSubmitted += _ => Submit();
        foreach (var (text, go) in new (string, Action)[] { ("Valider", Submit), ("Annuler", CloseAsk) })
        {
            var b = new Button { Text = text, FocusMode = Control.FocusModeEnum.None, Flat = true };
            b.AddThemeFontSizeOverride("font_size", 18);
            b.Pressed += go;
            ask.AddChild(b);
        }
    }

    /// <summary>
    /// COCHER ENVOIE TOUT DE SUITE la dernière partie enregistrée — le fichier
    /// tel qu'il est, sans en écrire un nouveau dans le dos du joueur.
    /// </summary>
    void PushNow()
    {
        if (_gameId.Length > 0) AutoPush(_gameId);
    }

    /// <summary>La case dit ce qui est vrai : pas de serveur, pas de case ; pas de pseudo, pas cochée.</summary>
    void SyncAutoOnline()
    {
        if (_autoOnline == null) return;
        _autoOnline.Visible = _onlineUrl.Length > 0 && !_skirmish;
        _autoOnline.SetPressedNoSignal(OnlineAuto && OnlineReady);
    }

    // ------------------------------------------------------------------
    //  LE MENU
    // ------------------------------------------------------------------

    /// <summary>Le champ du pseudo, seul champ du titre : ClearItems l'emporte avec les boutons.</summary>
    LineEdit? _titleField;
    /// <summary>La ligne qu'on s'apprête à écraser ou effacer : le premier appui demande, le second fait.</summary>
    string _onlineArmed = "";

    /// <summary>Un mot sous le titre, celui du mode débug : on est au titre, le HUD est caché.</summary>
    void OnlineSay(string text) => DebugNote(text);

    void OnlineItems(string mode, string label, Action neuve)
    {
        _onlineArmed = "";
        ClearItems();
        if (_onlineUrl.Length == 0)
        {
            Item("Pas de serveur — settings.json, enLigne.url", () => { }, 26, open: false);
            Item("Retour", () => LoadItems(mode, label, neuve), 34);
            ShowPick();
            return;
        }
        string pseudo = OnlinePseudo;
        if (pseudo.Length == 0 || !KeyShape(OnlineKey)) { PseudoItems(mode, label, neuve); return; }
        Item($"Capitaine {pseudo}", () => { }, 26, open: false);
        Item("Envoyer une partie", () => SendItems(mode, label, neuve), 34);
        Item("Effacer une partie en ligne", () => FetchItems(mode, label, neuve), 28);
        Item("Changer de pseudo", () => PseudoItems(mode, label, neuve), 26);
        Item("Copier ma clé (pour un autre poste)", () =>
        {
            DisplayServer.ClipboardSet(OnlineKey);
            OnlineSay("Clé copiée — gardez-la pour vous");
        }, 26);
        Item("Retour", () => LoadItems(mode, label, neuve), 34);
        ShowPick();
    }

    /// <summary>
    /// LE PSEUDO : choisi la première fois, changé ensuite. La clé est tirée ici
    /// si l'on n'en a pas — ou collée, pour retrouver ses parties d'un autre poste.
    /// </summary>
    void PseudoItems(string mode, string label, Action neuve)
    {
        ClearItems();
        _titleField = new LineEdit
        {
            Text = OnlinePseudo, PlaceholderText = "votre pseudo", MaxLength = 24,
            Alignment = HorizontalAlignment.Right, CustomMinimumSize = new Vector2(0, 44)
        };
        _titleField.AddThemeFontSizeOverride("font_size", 24);
        _titleBox!.AddChild(_titleField);
        void Submit()
        {
            string p = _titleField!.Text.Trim();
            OnlineSay("Le serveur…");
            Register(p, why =>
            {
                if (why.Length > 0) { OnlineSay(why); return; }
                OnlineSay($"Bienvenue, capitaine {p}");
                OnlineItems(mode, label, neuve);
            });
        }
        _titleField.TextSubmitted += _ => Submit();
        _titleField.CallDeferred(Control.MethodName.GrabFocus);
        Item("Valider", Submit, 34);
        Item("Coller une clé d'un autre poste", () =>
        {
            string k = DisplayServer.ClipboardGet().Trim().ToLowerInvariant();
            if (!KeyShape(k)) { OnlineSay("Le presse-papiers ne tient pas de clé"); return; }
            OnlineSay("Le serveur…");
            Online("moi", new JsonObject(), (r, why) =>
            {
                if (r == null) { OnlineSay(why); return; }
                string p = r["pseudo"]?.GetValue<string>() ?? "";
                OnlineRemember(k, p);
                OnlineSay($"Rebonjour, capitaine {p}");
                OnlineItems(mode, label, neuve);
            }, k);
        }, 26);
        Item("Retour", () => LoadItems(mode, label, neuve), 34);
        ShowPick();
    }

    /// <summary>Les parties de ce poste, à envoyer : une ligne chacune, le clic l'envoie.</summary>
    void SendItems(string mode, string label, Action neuve)
    {
        ClearItems();
        var list = Saves(mode);
        foreach (var s in list)
        {
            var save = s;
            Item($"{s.Nom}   ·   {Written(s)}", () => SendSave(save), 26);
        }
        if (list.Count == 0) Item("Aucune partie sur ce poste", () => { }, 28, open: false);
        Item("Retour", () => OnlineItems(mode, label, neuve), 34);
        ShowPick();
    }

    /// <summary>
    /// Le FICHIER part tel qu'il est écrit, pas une partie relue et réécrite : ce
    /// qu'une version plus récente du jeu y aurait mis voyage avec lui.
    /// </summary>
    void SendSave(SaveState s)
    {
        OnlineSay("Envoi…");
        PushSave(s.Id, why => OnlineSay(why.Length > 0 ? "Non envoyée : " + why : "Partie envoyée — " + s.Nom));
    }

    /// <summary>Les envois en cours, et ce qui attend qu'ils soient finis (quitter le jeu).</summary>
    int _uploads;
    Action? _afterUploads;

    void PushSave(string id, Action<string> done)
    {
        JsonObject? game = null;
        using (var f = FileAccess.Open(PathOf(id), FileAccess.ModeFlags.Read))
            if (f != null)
                try { game = JsonNode.Parse(f.GetAsText()) as JsonObject; } catch (JsonException) { }
        if (game == null) { done("la partie n'a pas pu être lue"); return; }
        game["id"] = id;
        _uploads++;
        Online("envoyer", new JsonObject { ["partie"] = game }, (r, why) =>
        {
            if (r != null) GD.Print($"[en ligne] partie envoyée : {id}");
            done(r == null ? why : "");
            if (--_uploads == 0 && _afterUploads is { } then) { _afterUploads = null; then(); }
        });
    }

    /// <summary>
    /// QUITTER APRÈS L'ENVOI : le jeu qui se ferme coupe la requête en vol, et la
    /// partie qu'on vient d'enregistrer n'arriverait pas. On attend la réponse,
    /// dix secondes au plus — un serveur muet ne retient pas le joueur.
    /// </summary>
    void AfterUploads(Action then)
    {
        if (_uploads == 0) { then(); return; }
        _afterUploads = then;
        Say("Envoi de la partie au serveur…");
        GetTree().CreateTimer(10, processAlways: true).Timeout += () =>
        {
            if (_afterUploads is { } t) { _afterUploads = null; t(); }
        };
    }

    /// <summary>
    /// L'ENVOI AUTOMATIQUE, après chaque enregistrement si le joueur l'a choisi.
    /// Ce qu'il en advient se dit sur le HUD, et sous le menu d'Échap s'il est ouvert.
    /// </summary>
    void AutoPush(string id)
    {
        if (!OnlineAuto || !OnlineReady) return;
        PushSave(id, why =>
        {
            string said = why.Length > 0 ? "Pas envoyée en ligne : " + why : "Envoyée en ligne";
            Say(said);
            if (_pauseNote != null && _pause is { Visible: true }) _pauseNote.Text += " · " + said;
        });
    }

    /// <summary>
    /// REPRENDRE UNE PARTIE DU SERVEUR : elle descend dans user://parties, à la
    /// place de celle du poste, puis se charge comme une autre.
    /// </summary>
    void DownloadAndLoad(string id, Action<string> fail)
    {
        Online("partie", new JsonObject { ["id"] = id }, (r, why) =>
        {
            if (r?["partie"] is not JsonObject game) { fail(why); return; }
            DirAccess.MakeDirRecursiveAbsolute(ProjectSettings.GlobalizePath(SaveDir));
            string text = game.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
            using (var f = FileAccess.Open(PathOf(id), FileAccess.ModeFlags.Write))
            {
                if (f == null) { fail("la partie n'a pas pu être écrite"); return; }
                f.StoreString(text);
            }
            SaveState? save = null;
            try { save = JsonSerializer.Deserialize<SaveState>(text); } catch (JsonException) { }
            if (save == null) { fail("partie illisible"); return; }
            save.Id = id;
            GD.Print($"[en ligne] partie reprise du serveur : {id}");
            LoadGame(save);
        });
    }

    /// <summary>Les parties du serveur pour ce mode : à effacer.</summary>
    void FetchItems(string mode, string label, Action neuve)
    {
        ClearItems();
        Item("Le serveur…", () => { }, 28, open: false);
        Item("Retour", () => OnlineItems(mode, label, neuve), 34);
        ShowPick();
        Online("liste", new JsonObject(), (r, why) =>
        {
            ClearItems();
            int shown = 0;
            foreach (var node in r?["parties"]?.AsArray() ?? new JsonArray())
            {
                if (node == null || node["mode"]?.GetValue<string>() != mode) continue;
                var s = new SaveState
                {
                    Id = node["id"]!.GetValue<string>(),
                    Nom = node["nom"]?.GetValue<string>() ?? "",
                    Ecrit = node["ecrit"]?.GetValue<string>() ?? ""
                };
                var st = node["stats"];
                string line = $"{s.Nom}   ·   {Written(s)}" + StatsLine(st);
                shown++;
                EraseOnlineItem(s, line, mode, label, neuve);
            }
            if (r == null) Item(why, () => { }, 26, open: false);
            else if (shown == 0) Item("Aucune partie en ligne", () => { }, 28, open: false);
            Item("Retour", () => OnlineItems(mode, label, neuve), 34);
            ShowPick();
        });
    }

    /// <summary>Trois chiffres qui disent la partie : la bourse, le chemin, les jours.</summary>
    static string StatsLine(JsonNode? st)
    {
        if (st is not JsonObject o || !o.ContainsKey("ecus")) return "";
        double ecus = o["ecus"]!.GetValue<double>();
        double miles = o["milles"]?.GetValue<double>() ?? 0;
        int days = o["jours"]?.GetValue<int>() ?? 0;
        return FormattableString.Invariant($"   ·   {ecus:F0} écus, {miles:F0} milles, {days} j");
    }

    void EraseOnlineItem(SaveState s, string line, string mode, string label, Action neuve)
    {
        bool armed = _onlineArmed == s.Id;
        Item(armed ? $"Effacer du serveur pour de bon : {s.Nom} ?" : line, () =>
        {
            if (_onlineArmed != s.Id) { _onlineArmed = s.Id; FetchItems(mode, label, neuve); return; }
            _onlineArmed = "";
            Online("effacer", new JsonObject { ["id"] = s.Id }, (r, why) =>
            {
                OnlineSay(r == null ? why : "Effacée du serveur");
                FetchItems(mode, label, neuve);
            });
        }, 24);
    }
}

/// <summary>
/// L'ESSAI DE L'ALLER-RETOUR (-- --en-ligne-essai 1) : un joueur jetable, une
/// partie enregistrée en jeu libre avec la sauvegarde automatique cochée, puis le
/// serveur relu et la partie effacée des deux côtés. Rend 0 si tout est passé.
/// </summary>
public partial class ShipDemo
{
    bool _onlineTest, _onlineTestRan;
    double _onlineTestWait = 4;

    void OnlineTestTick()
    {
        if (!_onlineTest || _onlineTestRan || !_booted || _inTitle) return;
        if ((_onlineTestWait -= GetProcessDeltaTime()) > 0) return;
        _onlineTestRan = true;
        void Done(bool ok, string why)
        {
            GD.Print($"[en ligne] essai : {(ok ? "réussi" : "ÉCHOUÉ")} — {why}");
            DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath(OnlineIni));
            GetTree().Quit(ok ? 0 : 1);
        }
        if (_onlineUrl.Length == 0) { Done(false, "pas d'adresse dans settings.json"); return; }
        string pseudo = "essai-godot-" + Guid.NewGuid().ToString("N")[..6];
        Register(pseudo, why =>
        {
            if (why.Length > 0) { Done(false, "pseudo : " + why); return; }
            OnlineAuto = true;
            _tally.Coups = 7;                       // un compte reconnaissable au retour
            SaveGame();
            string id = _gameId;
            long sous = _purse.Sous;
            AfterUploads(() => Online("liste", new JsonObject(), (r, why2) =>
            {
                JsonNode? mine = null;
                foreach (var n in r?["parties"]?.AsArray() ?? new JsonArray())
                    if (n?["id"]?.GetValue<string>() == id) mine = n;
                if (mine == null) { Done(false, "la partie n'est pas dans la liste " + why2); return; }
                if (mine["stats"]?["coups"]?.GetValue<int>() != 7) { Done(false, "les comptes ne sont pas revenus"); return; }
                Online("partie", new JsonObject { ["id"] = id }, (p, why3) =>
                {
                    if (p?["partie"]?["sous"]?.GetValue<long>() != sous) { Done(false, "la partie ne revient pas entière " + why3); return; }
                    Online("effacer", new JsonObject { ["id"] = id }, (e, why4) =>
                    {
                        DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath(PathOf(id)));
                        Done(e != null, e != null ? $"{pseudo}, partie {id} envoyée, listée, relue, effacée" : "effacer : " + why4);
                    });
                });
            }));
        });
    }
}
