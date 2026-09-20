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
    Dock
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
}

/// <summary>Une étape : un lieu, ce qu'on y fait, et ce qui s'écrit à l'écran.</summary>
public sealed class QuestStep
{
    public string Title = "", Brief = "", Message = "";
    public Goal Goal = Goal.Reach;
    public PlaceSpec At = new();
    public double? Radius, MaxSpeed, Hold;

    /// <summary>Le rayon du lieu : celui de la fiche, sinon celui de l'objectif.</summary>
    public double R => Radius ?? Quests.Radius(Goal);

    /// <summary>Une étape lue seule — la quête la lit ainsi, et le banc de parité aussi.</summary>
    public static QuestStep FromJson(JsonElement s)
    {
        var step = new QuestStep
        {
            Title = Str(s, "title"), Brief = Str(s, "brief"), Message = Str(s, "message"),
            Goal = Quests.GoalOf(Str(s, "goal")),
            Radius = Opt(s, "radius"), MaxSpeed = Opt(s, "maxSpeed"), Hold = Opt(s, "hold")
        };
        if (s.TryGetProperty("at", out var at) && at.ValueKind == JsonValueKind.Object)
            step.At = new PlaceSpec
            {
                // « island » est l'ancien nom du champ, du temps où chaque port était une île
                Port = at.TryGetProperty("port", out var p) && p.ValueKind == JsonValueKind.String
                     ? p.GetString()! : Str(at, "island"),
                Bearing = Opt(at, "bearing"), Miles = Opt(at, "miles"), Distance = Opt(at, "distance"),
                Lat = Opt(at, "lat"), Lon = Opt(at, "lon"), X = Opt(at, "x"), Z = Opt(at, "z")
            };
        return step;
    }

    internal static string Str(JsonElement e, string k) =>
        e.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()! : "";
    internal static double? Opt(JsonElement e, string k) =>
        e.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : null;
}

/// <summary>Une quête lue dans <c>quests/*.json</c>. Le format est dans <c>quests/README.md</c>.</summary>
public sealed class QuestSpec
{
    public string Id = "", Title = "", Summary = "", Intro = "", Outro = "";
    public readonly List<QuestStep> Steps = new();

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
            if (Steps[i].At == null) return $"étape {i + 1} sans lieu (at)";
        return null;
    }

    public static QuestSpec FromJson(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var r = doc.RootElement;
        var q = new QuestSpec
        {
            Id = Str(r, "id"), Title = Str(r, "title"), Summary = Str(r, "summary"),
            Intro = Str(r, "intro"), Outro = Str(r, "outro")
        };
        if (r.TryGetProperty("steps", out var steps) && steps.ValueKind == JsonValueKind.Array)
            foreach (var st in steps.EnumerateArray())
                q.Steps.Add(QuestStep.FromJson(st));
        return q;

        static string Str(JsonElement e, string k) =>
            e.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()! : "";
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
    /// <summary>L'objectif a changé.</summary>
    public Action? OnChange;

    public Quests(World world) { _world = world; }

    /// <summary>Le rayon par défaut de chaque sorte d'objectif, en mètres.</summary>
    public static double Radius(Goal g) => g switch
    {
        Goal.Leave => 1852,   // hors du cercle : un mille franc d'une rade
        Goal.Stop => 250,
        Goal.Dock => 450,
        _ => 300
    };

    public static Goal GoalOf(string s) => s switch
    {
        "leave" => Goal.Leave,
        "stop" => Goal.Stop,
        "dock" => Goal.Dock,
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
            double d = a.Miles != null ? a.Miles.Value * 1852 : (a.Distance ?? 0);
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
        Step = 0; Hold = 0;
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
        Active = null; Step = 0; Hold = 0;
        OnChange?.Invoke();
    }

    public QuestStep? Current => Active != null && Step < Active.Steps.Count ? Active.Steps[Step] : null;

    /// <summary>Ce que l'écran montre : l'étape, son lieu, à quelle distance et dans quel relèvement.</summary>
    public Objective? Aim(double fromX, double fromZ)
    {
        var s = Current;
        if (s == null || Active == null) return null;
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
        if (step == null || s.Lost) return;
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
            default: met = inside; break;
        }
        if (met) Advance();
    }

    void Advance()
    {
        var q = Active!;
        var s = Current!;
        if (s.Message.Length > 0) OnShow?.Invoke(s.Title, s.Message);
        Step++;
        Hold = 0;
        if (Step >= q.Steps.Count)
        {
            Done.Add(q.Id);
            if (q.Outro.Length > 0) OnShow?.Invoke(q.Title.Length > 0 ? q.Title : q.Id, q.Outro);
            Active = null; Step = 0;
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
