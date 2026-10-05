using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace NavalSim.Core;

/// <summary>Ce qu'il faut faire au lieu d'une étape.</summary>
public enum Goal
{
    /// <summary>Entrer dans le cercle.</summary>
    Reach,
    /// <summary>En sortir — s'éloigner, dans n'importe quelle direction.</summary>
    Leave,
    /// <summary>Y rester sous <see cref="QuestStep.MaxSpeed"/> nœuds pendant <see cref="QuestStep.Hold"/> secondes.</summary>
    Stop,
    /// <summary>Y être amarré ou mouillé.</summary>
    Dock,
    /* CE QUE FAIT LE NAVIRE ET NON OÙ IL EST (Godot) : le lieu de l'étape n'y
       compte pas : l'hôte crédite ce qui se fait (Quests.Credit), depuis le
       début de l'étape. */
    /// <summary>Pêcher <see cref="QuestStep.Kg"/> kilos.</summary>
    Fish,
    /// <summary>En vendre <see cref="QuestStep.Kg"/> kilos au comptoir.</summary>
    Sell,
    /// <summary>Acheter un navire au chantier.</summary>
    Buy,
    /* LES GESTES DU TUTORIEL (Godot) : l'hôte crédite ce que fait la main. */
    /// <summary>Border l'écoute de <see cref="QuestStep.Angle"/> degrés.</summary>
    SheetIn,
    /// <summary>La choquer d'autant.</summary>
    SheetOut,
    /// <summary>Virer de bord <see cref="QuestStep.Count"/> fois : le vent passe sur l'autre amure.</summary>
    Tack
}

/// <summary>
/// LE LIEU D'UNE ÉTAPE, tel que la fiche le donne — et jamais en mètres tant
/// qu'on ne le demande pas. Un port de la région, un relèvement et une distance
/// depuis ce port, une vraie latitude et longitude, ou des mètres du monde.
/// </summary>
public sealed class PlaceSpec
{
    public string Port = "";
    public double? Bearing, Miles, Distance, Lat, Lon, X, Z;

    /// <summary>Vrai si la fiche demande un écart depuis le port plutôt que son ponton.</summary>
    public bool Offset => Bearing != null || Miles != null || Distance != null;
    /// <summary>Aucun lieu : une étape de pêche n'en a pas, le poisson est à trouver.</summary>
    public bool None => Port.Length == 0 && Lat == null && X == null && Z == null;
}

/// <summary>
/// LE FRET D'UNE ÉTAPE — ce qu'il faut avoir à bord pour qu'elle compte, ce
/// qu'on y laisse, ce qu'on y prend, et ce qu'on en touche.
///
/// C'est ce qui fait la différence entre un fil tendu d'un lieu à l'autre et un
/// VOYAGE : sans cargaison, aller quelque part et en revenir ne se distingue pas
/// d'une promenade. Et tout passe par la cale réelle — du poids qui enfonce la
/// coque et déplace son centre de gravité —, jamais par un compteur à part : un
/// navire qui porte six tonnes doit s'asseoir de six tonnes.
///
/// <see cref="Needs"/> est une GARDE et non une consigne : sur le chemin normal
/// c'est l'étape d'avant qui a chargé la cale, et le joueur n'a rien à faire.
/// Elle ne mord que s'il a jeté sa cargaison par-dessus bord — auquel cas il
/// n'est pas payé, ce qui est la seule réponse juste.
/// </summary>
public sealed class Freight
{
    /// <summary>Ce qu'il faut à bord, et combien de tonnes, pour que l'étape compte.</summary>
    public string Needs = "";
    public double NeedsTonnes;
    /// <summary>Ce qu'on débarque ici — tout ce qu'on en porte.</summary>
    public string Unload = "";
    /// <summary>Ce qu'on embarque ici, et combien.</summary>
    public string Load = "";
    public double LoadTonnes;
    /// <summary>Ce qu'on touche, en pièces d'argent (60 pour un écu).</summary>
    public double Pay;

    public static Freight? FromJson(JsonElement e)
    {
        if (e.ValueKind != JsonValueKind.Object) return null;
        var f = new Freight
        {
            Unload = Js.Str(e, "unload"),
            Pay = Js.Opt(e, "pay") ?? 0
        };
        if (e.TryGetProperty("needs", out var n) && n.ValueKind == JsonValueKind.Object)
        {
            f.Needs = Js.Str(n, "kind");
            f.NeedsTonnes = Js.Opt(n, "tonnes") ?? 0;
        }
        if (e.TryGetProperty("load", out var l) && l.ValueKind == JsonValueKind.Object)
        {
            f.Load = Js.Str(l, "kind");
            f.LoadTonnes = Js.Opt(l, "tonnes") ?? 0;
        }
        return f;
    }
}

/// <summary>Une étape : un lieu, ce qu'on y fait, et ce qui s'écrit à l'écran.</summary>
public sealed class QuestStep
{
    public string Title = "", Brief = "", Message = "";
    public Goal Goal = Goal.Reach;
    public PlaceSpec At = new();
    public double? Radius, MaxSpeed, Hold;
    /// <summary>Le fret de l'étape, ou nul : voir <see cref="Freight"/>.</summary>
    public Freight? Cargo;
    /// <summary>Les kilos d'un objectif de pêche ou de vente.</summary>
    public double? Kg;
    /// <summary>Le rang que l'étape donne quand elle est remplie, ou vide.</summary>
    public string Rank = "";
    /// <summary>L'espèce qui compte pour une pêche (« merou »), ou vide : toutes.</summary>
    public string Species = "";
    /// <summary>L'heure du ciel avant laquelle la faire (« avant » : 19), affichée ; ou nulle.</summary>
    public double? Before;
    /// <summary>Les degrés d'écoute à border ou choquer.</summary>
    public double? Angle;
    /// <summary>Combien de virements de bord.</summary>
    public double? Count;

    /// <summary>Ce qu'il faut atteindre pour un objectif qui se compte.</summary>
    public double Needed => Goal switch
    {
        Goal.Fish or Goal.Sell => Kg ?? 1,
        Goal.SheetIn or Goal.SheetOut => Angle ?? 15,
        Goal.Tack => Count ?? 1,
        _ => 1
    };

    /// <summary>Un objectif qui se compte et non un lieu : rien à viser.</summary>
    public bool Counts => Goal is Goal.Fish or Goal.SheetIn or Goal.SheetOut or Goal.Tack;

    /// <summary>Le rayon du lieu : celui de la fiche, sinon celui de l'objectif.</summary>
    public double R => Radius ?? Quests.Radius(Goal);

    /// <summary>Une étape lue seule — la quête la lit ainsi, et le banc de parité aussi.</summary>
    public static QuestStep FromJson(JsonElement s)
    {
        var step = new QuestStep
        {
            Title = Js.Str(s, "title"), Brief = Js.Str(s, "brief"), Message = Js.Str(s, "message"),
            Goal = Quests.GoalOf(Js.Str(s, "goal")),
            Radius = Js.Opt(s, "radius"), MaxSpeed = Js.Opt(s, "maxSpeed"), Hold = Js.Opt(s, "hold"),
            Kg = Js.Opt(s, "kg"), Rank = Js.Str(s, "rang"),
            Species = Js.Str(s, "espece"), Before = Js.Opt(s, "avant"),
            Angle = Js.Opt(s, "angle"), Count = Js.Opt(s, "nombre")
        };
        if (s.TryGetProperty("cargo", out var cg)) step.Cargo = Freight.FromJson(cg);
        if (s.TryGetProperty("at", out var at) && at.ValueKind == JsonValueKind.Object)
            step.At = new PlaceSpec
            {
                // « island » est l'ancien nom du champ, du temps où chaque port était une île
                Port = at.TryGetProperty("port", out var p) && p.ValueKind == JsonValueKind.String
                     ? p.GetString()! : Js.Str(at, "island"),
                Bearing = Js.Opt(at, "bearing"), Miles = Js.Opt(at, "miles"), Distance = Js.Opt(at, "distance"),
                Lat = Js.Opt(at, "lat"), Lon = Js.Opt(at, "lon"), X = Js.Opt(at, "x"), Z = Js.Opt(at, "z")
            };
        return step;
    }

}

/// <summary>Une quête lue dans <c>quests/*.json</c>. Le format est dans <c>quests/README.md</c>.</summary>
public sealed class QuestSpec
{
    public string Id = "", Title = "", Summary = "", Intro = "", Outro = "";
    /// <summary>
    /// La région où elle se joue (le nom de sa fiche, « caraibes »), ou vide :
    /// partout. Un port n'a de sens que sur sa carte — ailleurs la quête attend,
    /// sans viser ni s'accomplir.
    /// </summary>
    public string Region = "";
    /// <summary>
    /// « story » : un chapitre de l'HISTOIRE, joués dans l'ordre de leur
    /// `chapter` ; sinon une MISSION, qu'on choisit dans la liste (Godot).
    /// </summary>
    public string Kind = "mission";
    public int Chapter;
    /// <summary>
    /// LE NAVIRE QU'ELLE IMPOSE, s'il y en a un : le nom de sa fiche, sans
    /// l'extension. Un chapitre se court sur le bord qu'il raconte — on ne porte
    /// pas six tonnes de vivres à travers une rade dans un galion de trois cents
    /// tonneaux parce qu'on l'avait sous la main. Vide : celui qu'on a.
    ///
    /// Le mot n'a de sens qu'au DÉBUT d'une quête. Reprendre une partie
    /// enregistrée ne le relit pas : la sauvegarde sait déjà quel bord on menait,
    /// et l'imposer à nouveau effacerait un navire que le joueur a gagné.
    /// </summary>
    public string Ship = "";
    /// <summary>Le rang qu'elle donne en commençant (« Pêcheur »), ou vide : celui qu'on a.</summary>
    public string Rank = "";
    /// <summary>La bourse de départ, en écus, ou nul : celle que la partie donne.</summary>
    public double? Purse;
    public readonly List<QuestStep> Steps = new();
    /// <summary>La cinématique qui l'ouvre (un chapitre de l'histoire), ou nulle.</summary>
    public CinematicSpec? Cinematic;
    /// <summary>La question de fin (« fin ») : ce qu'on dit avant de proposer la suite.</summary>
    public string End = "";

    /// <summary>
    /// Pourquoi cette quête est inacceptable, ou <c>null</c> si elle tient. Une
    /// fiche mal formée est ÉCARTÉE, pas corrigée : un lieu deviné enverrait le
    /// joueur quelque part sans que rien ne le dise.
    /// </summary>
    public string? Check()
    {
        if (string.IsNullOrEmpty(Id)) return "pas d'id";
        if (Steps.Count == 0) return "aucune étape";
        for (int i = 0; i < Steps.Count; i++)
            if (Steps[i].At.None && !Steps[i].Counts) return $"étape {i + 1} sans lieu (at)";
        return null;
    }

    public static QuestSpec FromJson(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return FromElement(doc.RootElement);
    }

    /// <summary>
    /// LES CHAPITRES DE L'HISTOIRE, tous dans un fichier (<c>quests/histoire.json</c>
    /// → « chapitres ») : le texte se relit et se retouche d'un seul tenant. Chacun
    /// est une quête « story » ; son numéro est son rang dans la liste, sauf s'il
    /// écrit le sien.
    /// </summary>
    public static List<QuestSpec> ChaptersFromJson(string json)
    {
        var list = new List<QuestSpec>();
        using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        if (!doc.RootElement.TryGetProperty("chapitres", out var ch) || ch.ValueKind != JsonValueKind.Array) return list;
        int n = 0;
        foreach (var e in ch.EnumerateArray())
        {
            n++;
            var q = FromElement(e);
            q.Kind = "story";
            if (q.Chapter == 0) q.Chapter = n;
            if (q.Id.Length == 0) q.Id = $"chapitre-{q.Chapter}";
            list.Add(q);
        }
        return list;
    }

    public static QuestSpec FromElement(JsonElement r)
    {
        var q = new QuestSpec
        {
            Id = Js.Str(r, "id"), Title = Js.Str(r, "title"), Summary = Js.Str(r, "summary"),
            Intro = Js.Str(r, "intro"), Outro = Js.Str(r, "outro"), Region = Js.Str(r, "region")
        };
        if (Js.Str(r, "kind") is { Length: > 0 } kind) q.Kind = kind;
        if (r.TryGetProperty("chapter", out var ch) && ch.ValueKind == JsonValueKind.Number) q.Chapter = ch.GetInt32();
        q.Ship = Js.Str(r, "ship");
        q.Rank = Js.Str(r, "rang");
        if (r.TryGetProperty("bourse", out var bo) && bo.ValueKind == JsonValueKind.Number) q.Purse = bo.GetDouble();
        if (r.TryGetProperty("steps", out var steps) && steps.ValueKind == JsonValueKind.Array)
            foreach (var st in steps.EnumerateArray())
                q.Steps.Add(QuestStep.FromJson(st));
        if (r.TryGetProperty("cinematique", out var ci)) q.Cinematic = CinematicSpec.FromJson(ci);
        q.End = Js.Str(r, "fin");
        return q;

    }
}

/// <summary>Ce que l'écran a besoin de savoir de l'étape en cours.</summary>
public readonly record struct Objective(QuestSpec Quest, QuestStep Step, int Index, int Count,
                                        double X, double Z, double R,
                                        double Dist, double Bearing, double Hold);

/// <summary>L'état du navire que les quêtes regardent, et rien de plus.</summary>
public readonly record struct QuestState(double X, double Z, double Speed, bool Moored, bool Lost);

/// <summary>
/// LES QUÊTES : un scénario en étapes, lu dans <c>quests/*.json</c>.
///
/// Le monde reste ouvert ; une quête ne fait que TENDRE UN FIL d'un lieu à
/// l'autre. Chaque étape nomme un endroit et ce qu'il faut y faire ; quand c'est
/// fait, son message passe à l'écran et l'étape suivante commence.
///
/// Les lieux se disent contre le monde — un port de la région, ou une vraie
/// latitude et longitude que la carte réduite ramène à ses propres mètres. Un
/// scénario n'a donc pas à connaître l'échelle, et repeindre la côte ne le
/// périme pas.
///
/// Ce module tient l'état et les règles ; l'écran montre ce qu'il dit
/// (<see cref="OnShow"/> pour un message, <see cref="Objective"/> pour la ligne
/// dorée et le cercle sur la carte) et lui passe l'état du navire à chaque image.
/// </summary>
public sealed class Quests
{
    readonly World _world;

    public readonly List<QuestSpec> List = new();
    public QuestSpec? Active;
    public int Step;
    /// <summary>Secondes déjà tenues sur un objectif <see cref="Goal.Stop"/>.</summary>
    public double Hold;
    public readonly HashSet<string> Done = new();

    /// <summary>(titre, texte) — un message pour l'écran.</summary>
    public Action<string, string>? OnShow;
    /// <summary>
    /// COMBIEN DE TONNES DE CE GENRE LA COQUE PORTE — branché par l'hôte, parce
    /// que la cale est à elle et non au scénario. Nul : aucune garde de fret ne
    /// mord, ce qui est le bon défaut — une quête sans cargaison ne doit pas
    /// dépendre d'un branchement que personne n'a fait.
    /// </summary>
    public Func<string, double>? Aboard;
    /// <summary>
    /// LE FRET D'UNE ÉTAPE QUI VIENT D'ÊTRE REMPLIE : à l'hôte de débarquer,
    /// d'embarquer et de payer. Le scénario dit QUOI, la cale et la bourse
    /// savent COMMENT — et c'est la même séparation que partout ailleurs ici.
    /// </summary>
    public Action<Freight>? OnFreight;
    /// <summary>L'objectif a changé.</summary>
    public Action? OnChange;
    /// <summary>
    /// LE COMPTE DES OBJECTIFS QUI NE SONT PAS DES LIEUX : les kilos pêchés, les
    /// kilos vendus, les navires achetés DEPUIS LE DÉBUT DE L'ÉTAPE. Il vit ici et
    /// non chez l'hôte parce qu'il se sauvegarde avec l'étape : quitter à mi-pêche
    /// ne doit pas faire recommencer la pêche.
    /// </summary>
    public double Counted;
    /// <summary>Un rang vient d'être donné, par une étape remplie.</summary>
    public Action<string>? OnRank;
    /// <summary>Une quête vient d'être menée à son terme (après son dernier message).</summary>
    public Action<QuestSpec>? OnFinish;

    public Quests(World world) { _world = world; }

    /// <summary>Le rayon par défaut de chaque sorte d'objectif, en mètres.</summary>
    public static double Radius(Goal g) => g switch
    {
        Goal.Leave => Config.Mile,   // hors du cercle : un mille franc d'une rade
        Goal.Stop => 250,
        Goal.Dock => 450,
        _ => 300
    };

    public static Goal GoalOf(string s) => s switch
    {
        "leave" => Goal.Leave,
        "stop" => Goal.Stop,
        "dock" => Goal.Dock,
        "peche" => Goal.Fish,
        "vente" => Goal.Sell,
        "achat" => Goal.Buy,
        "border" => Goal.SheetIn,
        "choquer" => Goal.SheetOut,
        "virer" => Goal.Tack,
        _ => Goal.Reach
    };

    /// <summary>Verse une fiche dans la liste, ou dit pourquoi elle est écartée.</summary>
    public bool Add(QuestSpec q, Action<string>? warn = null)
    {
        string? why = q.Check();
        if (why != null) { warn?.Invoke($"[quêtes] {(q.Id.Length > 0 ? q.Id : "?")} ignorée : {why}"); return false; }
        List.Add(q);
        return true;
    }

    public QuestSpec? ById(string id) => List.Find(q => q.Id == id);

    /// <summary>
    /// L'HÔTE DIT CE QUI VIENT DE SE FAIRE (des kilos pêchés, vendus, un navire
    /// acheté) ; cela ne compte que si c'est ce que l'étape en cours demande — le
    /// poisson pris avant qu'on vous le demande ne se vend pas deux fois.
    /// </summary>
    public void Credit(Goal g, double amount, string species = "")
    {
        if (Current is not QuestStep s || s.Goal != g || !HereNow) return;
        // une pêche d'une espèce : le vivaneau n'y compte pas pour le mérou
        if (s.Species.Length > 0 && species.Length > 0 && s.Species != species) return;
        Counted += amount;
        OnChange?.Invoke();
    }

    /// <summary>
    /// LE LIEU D'UNE ÉTAPE, en mètres VRAIS du monde.
    ///
    /// Le port seul donne la TÊTE DE SON PONTON, au large : c'est là qu'un navire
    /// s'amarre, et non sur la place du marché. Avec un relèvement ou une
    /// distance, c'est un écart depuis le port — un mouillage au vent, un point
    /// de rendez-vous à deux milles dans le sud.
    /// </summary>
    public (double X, double Z, double R, string Name) Place(QuestStep step, Action<string>? warn = null)
    {
        var a = step.At;
        double x = 0, z = 0;
        Isle? isl = a.Port.Length > 0 ? _world.ByKey(a.Port) : null;
        if (a.Port.Length > 0 && isl == null) warn?.Invoke($"[quêtes] port inconnu : {a.Port}");

        if (isl != null && !a.Offset)
        {
            x = isl.Port.Hx; z = isl.Port.Hz;
        }
        else if (isl != null)
        {
            // relèvement VRAI depuis le port (0 nord, 90 est — et l'est est −x ici)
            double b = (a.Bearing ?? 0) * Math.PI / 180;
            double d = a.Miles != null ? a.Miles.Value * Config.Mile : (a.Distance ?? 0);
            x = isl.X - Math.Sin(b) * d;
            z = isl.Z + Math.Cos(b) * d;
        }
        else if (a.Lat != null && a.Lon != null)
        {
            var g = _world.Geo.ToXZ(a.Lat.Value, a.Lon.Value);
            x = g.X; z = g.Z;
        }
        else
        {
            x = a.X ?? 0; z = a.Z ?? 0;
        }
        return (x, z, step.R, step.Title);
    }

    public void Start(string id)
    {
        Active = ById(id);
        Step = 0; Hold = 0; Counted = 0;
        if (Active != null)
        {
            OnShow?.Invoke(Active.Title.Length > 0 ? Active.Title : Active.Id,
                           Active.Intro.Length > 0 ? Active.Intro : Active.Summary);
            Brief();
        }
        OnChange?.Invoke();
    }

    public void Stop()
    {
        Active = null; Step = 0; Hold = 0; Counted = 0;
        OnChange?.Invoke();
    }

    public QuestStep? Current => Active != null && Step < Active.Steps.Count ? Active.Steps[Step] : null;

    /// <summary>Ce que l'écran montre : l'étape, son lieu, à quelle distance et dans quel relèvement.</summary>
    /// <summary>La quête en cours se joue-t-elle sur la carte chargée ?</summary>
    public bool HereNow => Active == null || Active.Region.Length == 0 || Active.Region == _world.Region.Key;

    public Objective? Aim(double fromX, double fromZ)
    {
        var s = Current;
        if (s == null || Active == null || !HereNow || s.At.None) return null;
        var p = Place(s);
        double dx = p.X - fromX, dz = p.Z - fromZ;
        double brg = Math.Atan2(-dx, dz) * 180 / Math.PI;          // l'est est −x
        if (brg < 0) brg += 360;
        return new Objective(Active, s, Step, Active.Steps.Count, p.X, p.Z, p.R,
                             Math.Sqrt(dx * dx + dz * dz), brg, Hold);
    }

    /// <summary>À chaque image.</summary>
    public void Update(double dt, QuestState s)
    {
        var step = Current;
        if (step == null || s.Lost || !HereNow) return;
        var p = Place(step);
        double dx = p.X - s.X, dz = p.Z - s.Z;
        bool inside = Math.Sqrt(dx * dx + dz * dz) <= p.R;
        bool met;
        switch (step.Goal)
        {
            case Goal.Leave: met = !inside; break;
            case Goal.Dock: met = inside && s.Moored; break;
            case Goal.Stop:
                bool still = inside && s.Speed <= (step.MaxSpeed ?? 1) * 0.5144;
                Hold = still ? Hold + dt : 0;
                met = Hold >= (step.Hold ?? 8);
                break;
            case Goal.Fish:
            case Goal.Sell:
            case Goal.SheetIn:
            case Goal.SheetOut:
            case Goal.Tack: met = Counted + 1e-6 >= step.Needed; break;
            case Goal.Buy: met = Counted >= 1; break;
            default: met = inside; break;
        }
        /* LA GARDE DU FRET, APRÈS l'objectif et jamais avant : arriver sans la
           cargaison, c'est arriver quand même — l'étape n'est simplement pas
           remplie, et le joueur reste devant son objectif au lieu de le voir
           s'accomplir à vide. */
        if (met && step.Cargo is { } c && c.Needs.Length > 0
            && (Aboard?.Invoke(c.Needs) ?? 0) + 1e-6 < c.NeedsTonnes) met = false;
        if (met) Advance();
    }

    void Advance()
    {
        var q = Active!;
        var s = Current!;
        /* LE FRET AVANT LE MESSAGE : celui-ci dit ce qui vient de se passer
           (« on roule les barriques à bord »), et il mentirait d'une image s'il
           paraissait avant que ce soit fait. */
        if (s.Cargo is { } c) OnFreight?.Invoke(c);
        if (s.Rank.Length > 0) OnRank?.Invoke(s.Rank);
        if (s.Message.Length > 0) OnShow?.Invoke(s.Title, s.Message);
        Step++;
        Hold = 0; Counted = 0;
        if (Step >= q.Steps.Count)
        {
            Done.Add(q.Id);
            if (q.Outro.Length > 0) OnShow?.Invoke(q.Title.Length > 0 ? q.Title : q.Id, q.Outro);
            Active = null; Step = 0;
            OnFinish?.Invoke(q);
        }
        else Brief();
        OnChange?.Invoke();
    }

    void Brief()
    {
        var s = Current;
        if (s != null && s.Brief.Length > 0) OnShow?.Invoke(s.Title, s.Brief);
    }

    // ------------------------------------------------------------------
    /* OÙ ON EN EST, en clair et à côté du carnet de la carte. Le même choix que
       pour lui : ce qu'un joueur a fait se relit dans un éditeur, et une quête
       bloquée se débloque sans nous. */

    public string ToJson()
    {
        var sb = new StringBuilder();
        sb.Append("{\n  \"active\": ").Append(Active == null ? "null" : JsonSerializer.Serialize(Active.Id))
          .Append(",\n  \"step\": ").Append(Step.ToString(CultureInfo.InvariantCulture))
          .Append(",\n  \"compte\": ").Append(Counted.ToString("R", CultureInfo.InvariantCulture))
          .Append(",\n  \"done\": [");
        bool first = true;
        foreach (string id in Done) { sb.Append(first ? "" : ", ").Append(JsonSerializer.Serialize(id)); first = false; }
        sb.Append("]\n}\n");
        return sb.ToString();
    }

    /// <summary>Reprend où l'on en était. Rend vrai si une quête est repartie.</summary>
    public bool FromJson(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var r = doc.RootElement;
            if (r.TryGetProperty("done", out var d) && d.ValueKind == JsonValueKind.Array)
                foreach (var e in d.EnumerateArray())
                    if (e.GetString() is string id) Done.Add(id);
            string? a = r.TryGetProperty("active", out var av) && av.ValueKind == JsonValueKind.String ? av.GetString() : null;
            int step = r.TryGetProperty("step", out var sv) && sv.ValueKind == JsonValueKind.Number ? sv.GetInt32() : 0;
            var q = a != null ? ById(a) : null;
            if (q == null || step >= q.Steps.Count) return false;
            Active = q; Step = Math.Max(0, step); Hold = 0;
            Counted = r.TryGetProperty("compte", out var cv) && cv.ValueKind == JsonValueKind.Number ? cv.GetDouble() : 0;
            OnChange?.Invoke();
            return true;
        }
        catch (JsonException)
        {
            /* UN JOURNAL DE BORD ILLISIBLE N'EST PAS UNE PANNE : on repart en
               mode libre, ce qui est l'état de départ du jeu. */
            return false;
        }
    }
}
