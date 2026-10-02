using System;
using System.Collections.Generic;
using System.Text.Json;

namespace NavalSim.Core;

/// <summary>Une espèce qu'on pêche à la ligne — fishing/poissons.json.</summary>
public sealed class FishSpecies
{
    public string Key = "", Name = "", Note = "";
    public double WMin = 1, WMax = 5, DMin = 10, DMax = 50;
    /// <summary>Le fond qu'elle aime : « roche », « tombant » ou « sable ».</summary>
    public string Bottom = "roche";
    /// <summary>Les touches par heure et par ligne, au meilleur endroit et à la meilleure heure.</summary>
    public double BitesPerHour = 6;
    /// <summary>Son prix frais, en pièces d'argent le kilo.</summary>
    public double PricePerKg = 6;
    /// <summary>Son appétit selon l'heure : aube, jour, crépuscule, nuit.</summary>
    public double Dawn = 1.5, Day = 0.7, Dusk = 1.5, Night = 0.5;
    /// <summary>La chance qu'un poisson de son poids maximal casse la ligne en remontant (il s'enroche, il scie la ligne sur le corail).</summary>
    public double Break = 0.1;
}

/// <summary>Les règles de la ligne à main, et les espèces.</summary>
public sealed class FishingRules
{
    public double LinesPerMan = 0.5, MaxSpeed = 0.7, StrikeWindow = 3.0, HaulSpeed = 0.8;
    /// <summary>La longueur d'une ligne, en mètres : plus profond, elle ne touche pas le fond.</summary>
    public double LineLength = 120;
    /// <summary>Les secondes pour réappâter une ligne et la refiler au fond.</summary>
    public double Rebait = 8;
    /// <summary>Le plus de lignes qu'un bord met à l'eau, quel que soit son équipage.</summary>
    public int MaxLines = 12;
    /// <summary>Minutes de jeu : plein prix jusqu'à Fresh, perdu à Spoil.</summary>
    public double FreshMin = 20, SpoilMin = 60;
    public readonly List<FishSpecies> Species = new();

    public FishSpecies? Of(string key) => Species.Find(s => s.Key == key);

    public static FishingRules FromJson(string json)
    {
        using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
        var r = doc.RootElement;
        var outp = new FishingRules();
        double N(JsonElement e, string k, double d) => e.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : d;
        if (r.TryGetProperty("ligne", out var l))
        {
            outp.LinesPerMan = N(l, "lignesParHomme", outp.LinesPerMan);
            outp.MaxSpeed = N(l, "vitesseMax", outp.MaxSpeed);
            outp.StrikeWindow = N(l, "ferrer", outp.StrikeWindow);
            outp.HaulSpeed = N(l, "remontee", outp.HaulSpeed);
            outp.FreshMin = N(l, "frais", outp.FreshMin);
            outp.SpoilMin = N(l, "gate", outp.SpoilMin);
            outp.LineLength = N(l, "longueur", outp.LineLength);
            outp.Rebait = N(l, "appat", outp.Rebait);
            outp.MaxLines = (int)N(l, "lignesMax", outp.MaxLines);
        }
        if (r.TryGetProperty("especes", out var es) && es.ValueKind == JsonValueKind.Array)
            foreach (var e in es.EnumerateArray())
            {
                var s = new FishSpecies
                {
                    Key = e.TryGetProperty("key", out var k) ? k.GetString() ?? "" : "",
                    Name = e.TryGetProperty("nom", out var n) ? n.GetString() ?? "" : "",
                    Note = e.TryGetProperty("note", out var no) ? no.GetString() ?? "" : "",
                    Bottom = e.TryGetProperty("fond", out var f) ? f.GetString() ?? "roche" : "roche",
                    BitesPerHour = N(e, "touchesParHeure", 6),
                    PricePerKg = N(e, "prixKg", 6),
                    Break = N(e, "casse", 0.1)
                };
                if (e.TryGetProperty("poids", out var w) && w.GetArrayLength() == 2) { s.WMin = w[0].GetDouble(); s.WMax = w[1].GetDouble(); }
                if (e.TryGetProperty("profondeur", out var d) && d.GetArrayLength() == 2) { s.DMin = d[0].GetDouble(); s.DMax = d[1].GetDouble(); }
                if (e.TryGetProperty("activite", out var a))
                {
                    s.Dawn = N(a, "aube", s.Dawn); s.Day = N(a, "jour", s.Day);
                    s.Dusk = N(a, "crepuscule", s.Dusk); s.Night = N(a, "nuit", s.Night);
                }
                if (s.Key.Length > 0) outp.Species.Add(s);
            }
        return outp;
    }
}

/// <summary>Un lot de poisson à bord : l'espèce, le poids, et quand il est sorti de l'eau (secondes de jeu).</summary>
public readonly record struct FishLot(string Key, double Kg, double CaughtAt);

/// <summary>
/// LA PÊCHE À LA LIGNE À MAIN — ce qui ne dépend que du lieu, de l'heure et du
/// hasard, donc ce qui se teste au banc.
///
/// RIEN N'EST MARQUÉ : le poisson tient au FOND, et c'est au pêcheur d'apprendre
/// où. Le relief ne dit pas la nature du fond, mais il en dit la PENTE, qui la
/// trahit : un fond qui monte et descend sur quelques mètres est de la roche ou
/// un récif, une pente forte et longue est un tombant, un fond plat est du sable.
/// Le mérou tient la roche de huit à quarante-cinq mètres, le vivaneau les
/// tombants de vingt-cinq à cent dix — et l'un comme l'autre mordent mieux à
/// l'aube et au crépuscule.
/// </summary>
public static class Fishing
{
    /// <summary>Le fond en un point (mètres vrais) : sa profondeur et sa pente, lue sur douze mètres.</summary>
    public static (double Depth, double Slope) BottomAt(World w, double x, double z)
    {
        double h = w.HeightAt(x, z);
        double gx = w.HeightAt(x + 6, z) - w.HeightAt(x - 6, z);
        double gz = w.HeightAt(x, z + 6) - w.HeightAt(x, z - 6);
        return (-h, Math.Sqrt(gx * gx + gz * gz) / 12);
    }

    static double Smooth(double a, double b, double v)
    {
        double u = Math.Clamp((v - a) / (b - a), 0, 1);
        return u * u * (3 - 2 * u);
    }

    /// <summary>
    /// CE QUE CE FOND VAUT POUR CETTE ESPÈCE, de 0 à 1 : sa fenêtre de profondeur,
    /// adoucie aux bords (un mérou ne s'arrête pas net à quarante-cinq mètres), et
    /// la nature du fond lue sur sa pente. Jamais tout à fait nul dans la fenêtre :
    /// un poisson de passage mord AUSSI sur le mauvais fond — moins, mais assez pour
    /// qu'on pêche partout et à toute heure (demandé). Le bon fond vaut trois fois le
    /// mauvais : c'est ce qui reste à apprendre.
    /// </summary>
    public static double Habitat(FishSpecies s, double depth, double slope)
    {
        if (depth <= 0.5) return 0;
        double span = s.DMax - s.DMin, edge = 0.15 * span;
        double d = Smooth(s.DMin - edge, s.DMin + edge, depth) * (1 - Smooth(s.DMax - edge, s.DMax + edge, depth));
        if (d <= 0) return 0;
        double b = s.Bottom switch
        {
            "roche" => 0.3 + 0.7 * Smooth(0.03, 0.12, slope),
            "tombant" => 0.3 + 0.7 * Smooth(0.08, 0.25, slope),
            _ => 1 - 0.7 * Smooth(0.05, 0.15, slope)
        };
        return d * b;
    }

    /// <summary>
    /// L'APPÉTIT À CETTE HEURE (0–24) : l'aube de cinq à huit, le crépuscule de
    /// dix-sept à vingt, en fondus d'une heure ; le jour et la nuit entre les deux.
    /// </summary>
    public static double Activity(FishSpecies s, double hour)
    {
        hour = ((hour % 24) + 24) % 24;
        double Bump(double from, double to) => Smooth(from - 1, from, hour) * (1 - Smooth(to, to + 1, hour));
        double dawn = Bump(5, 8), dusk = Bump(17, 20);
        double day = Smooth(7, 9, hour) * (1 - Smooth(16, 18, hour));
        double night = 1 - Math.Max(Math.Max(dawn, dusk), day);
        return Math.Max(Math.Max(dawn * s.Dawn, dusk * s.Dusk), Math.Max(day * s.Day, night * s.Night));
    }

    /// <summary>Les touches par heure et par ligne, ici et maintenant.</summary>
    public static double Rate(FishSpecies s, double depth, double slope, double hour) =>
        s.BitesPerHour * Habitat(s, depth, slope) * Activity(s, hour);

    /// <summary>Le poids d'une prise, tiré sur <paramref name="r"/> (0..1) : les petits sont les plus nombreux, le gros reste rare.</summary>
    public static double Weight(FishSpecies s, double r) => s.WMin + (s.WMax - s.WMin) * Math.Pow(r, 2.4);

    /// <summary>
    /// LES LIGNES D'UN BORD : une pour deux hommes, au moins une, au plus
    /// <see cref="FishingRules.MaxLines"/>. Sans équipage écrit dans la fiche, on
    /// l'estime sur le tonnage — dix hommes pour les vingt-deux tonnes du sloop.
    /// </summary>
    public static int Lines(FishingRules r, int? crew, double tonnes)
    {
        double men = crew ?? 10 * Math.Pow(Math.Max(1, tonnes) / 22, 0.6);
        return Math.Clamp((int)Math.Round(men * r.LinesPerMan), 1, r.MaxLines);
    }

    /// <summary>Ce qu'il reste de son prix à cet âge (minutes de jeu) : entier, puis en baisse, puis rien.</summary>
    public static double Freshness(FishingRules r, double ageMin) =>
        ageMin <= r.FreshMin ? 1 : Math.Max(0, 1 - (ageMin - r.FreshMin) / Math.Max(1, r.SpoilMin - r.FreshMin));
}

/// <summary>
/// LES LIGNES À L'EAU, et ce qui leur arrive — lancer, attendre, ferrer.
///
/// Chaque ligne a son sort : elle ATTEND au fond ; une touche la fait MORDRE, et
/// il faut ferrer dans la fenêtre (<see cref="FishingRules.StrikeWindow"/>), sinon
/// le poisson emporte l'appât ; ferrée, on la HALE, à la main, le temps que la
/// profondeur demande, et un gros poisson peut encore casser ; puis on RÉAPPÂTE.
/// Les lignes vivent ensemble mais pas en mesure : pendant qu'on hale un mérou,
/// les autres pêchent toujours.
///
/// Le hasard est passé par l'hôte, l'heure et le fond aussi : ce module ne lit
/// rien du monde, il se rejoue au banc à l'identique.
/// </summary>
public sealed class HandLines
{
    public enum LineState { Wait, Bite, Haul, Bait }

    public sealed class Line
    {
        public LineState State;
        /// <summary>Le temps qui reste à l'état (fenêtre de touche, halage, appât).</summary>
        public double T;
        public FishSpecies? Fish;
        public double Kg;
    }

    readonly FishingRules _r;
    public readonly List<Line> Lines = new();
    /// <summary>Des lignes sont à l'eau.</summary>
    public bool Wet => Lines.Count > 0;

    /// <summary>Une touche : à ferrer.</summary>
    public Action<Line>? OnBite;
    /// <summary>La fenêtre est passée : il a mangé l'appât.</summary>
    public Action<Line>? OnMissed;
    /// <summary>Ferré : on hale.</summary>
    public Action<Line>? OnHooked;
    /// <summary>À bord.</summary>
    public Action<FishSpecies, double>? OnCatch;
    /// <summary>La ligne a cassé en remontant.</summary>
    public Action<FishSpecies, double>? OnLost;

    public HandLines(FishingRules r) { _r = r; }

    public void Cast(int n)
    {
        Lines.Clear();
        for (int i = 0; i < n; i++)
            // filées l'une après l'autre : elles n'arrivent pas au fond en même temps
            Lines.Add(new Line { State = LineState.Bait, T = 2 + 1.5 * i });
    }

    public void Raise() => Lines.Clear();

    /// <summary>Une ligne mord-elle en ce moment ?</summary>
    public bool Biting => Lines.Exists(l => l.State == LineState.Bite);

    /// <summary>
    /// FERRER : la ligne qui mord depuis le plus longtemps (la moins de fenêtre
    /// restante). Rend faux s'il n'y avait rien à ferrer.
    /// </summary>
    public bool Strike(double depth)
    {
        Line? best = null;
        foreach (var l in Lines)
            if (l.State == LineState.Bite && (best == null || l.T < best.T)) best = l;
        if (best == null) return false;
        best.State = LineState.Haul;
        // le halage : la profondeur à la main, et un gros poisson se défend
        best.T = Math.Max(4, Math.Min(depth, _r.LineLength) / _r.HaulSpeed) + 0.6 * best.Kg;
        OnHooked?.Invoke(best);
        return true;
    }

    /// <summary>
    /// Un pas : <paramref name="rnd"/> rend un tirage uniforme dans [0, 1). Le fond
    /// est celui SOUS le navire, maintenant — il a pu dériver depuis le lancer.
    /// </summary>
    public void Step(double dt, double depth, double slope, double hour, Func<double> rnd)
    {
        foreach (var l in Lines)
        {
            switch (l.State)
            {
                case LineState.Bait:
                    l.T -= dt;
                    if (l.T <= 0) l.State = LineState.Wait;
                    break;
                case LineState.Wait:
                {
                    // une ligne qui pend dans le bleu sans toucher le fond ne prend ni mérou ni vivaneau
                    if (depth > _r.LineLength) break;
                    double total = 0;
                    foreach (var s in _r.Species) total += Fishing.Rate(s, depth, slope, hour);
                    if (total <= 0 || rnd() >= total / 3600 * dt) break;
                    // laquelle : au prorata de son appétit ici
                    double pick = rnd() * total;
                    FishSpecies f = _r.Species[^1];
                    foreach (var s in _r.Species)
                    {
                        pick -= Fishing.Rate(s, depth, slope, hour);
                        if (pick <= 0) { f = s; break; }
                    }
                    l.Fish = f;
                    l.Kg = Math.Round(Fishing.Weight(f, rnd()), 1);
                    l.State = LineState.Bite;
                    l.T = _r.StrikeWindow;
                    OnBite?.Invoke(l);
                    break;
                }
                case LineState.Bite:
                    l.T -= dt;
                    if (l.T <= 0)
                    {
                        OnMissed?.Invoke(l);
                        l.State = LineState.Bait; l.T = _r.Rebait; l.Fish = null;
                    }
                    break;
                case LineState.Haul:
                    l.T -= dt;
                    if (l.T <= 0 && l.Fish != null)
                    {
                        if (rnd() < l.Fish.Break * l.Kg / l.Fish.WMax) OnLost?.Invoke(l.Fish, l.Kg);
                        else OnCatch?.Invoke(l.Fish, l.Kg);
                        l.State = LineState.Bait; l.T = _r.Rebait; l.Fish = null;
                    }
                    break;
            }
        }
    }
}
