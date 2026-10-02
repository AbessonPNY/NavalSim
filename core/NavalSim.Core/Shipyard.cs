using System;
using System.Collections.Generic;
using System.Text.Json;

namespace NavalSim.Core;

/// <summary>
/// LE CHANTIER — market/chantier.json. On n'y a jamais deux coques : on achète
/// en laissant la sienne, que le chantier reprend à une part de son prix. Les
/// prix sont à la tonne de DÉPLACEMENT, la seule grandeur que toutes les fiches
/// écrivent de la même façon.
/// </summary>
public sealed class Shipyard
{
    public sealed class Yard
    {
        public string Port = "", Name = "";
        public readonly List<string> Ships = new();
    }

    public double EcusPerTonne = 3, TradeIn = 0.5;
    public readonly List<Yard> Yards = new();

    public Yard? At(string port) => Yards.Find(y => y.Port == port);

    /// <summary>Le prix d'une coque neuve, en pièces (sous).</summary>
    public long Price(double tonnes) => (long)Math.Round(tonnes * EcusPerTonne * Market.SousParEcu);

    /// <summary>Ce que le chantier rend pour celle qu'on lui laisse, en pièces.</summary>
    public long Allowance(double tonnes) => (long)Math.Round(Price(tonnes) * TradeIn);

    /// <summary>Ce qu'il faut sortir de la bourse : la neuve, moins la reprise. Négatif quand on descend vers plus petit : le chantier rend la différence.</summary>
    public long Net(double newTonnes, double oldTonnes) => Price(newTonnes) - Allowance(oldTonnes);

    public static Shipyard FromJson(string json)
    {
        using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
        var r = doc.RootElement;
        var y = new Shipyard();
        if (r.TryGetProperty("ecusParTonne", out var e) && e.ValueKind == JsonValueKind.Number) y.EcusPerTonne = e.GetDouble();
        if (r.TryGetProperty("reprise", out var t) && t.ValueKind == JsonValueKind.Number) y.TradeIn = Math.Clamp(t.GetDouble(), 0, 1);
        if (r.TryGetProperty("chantiers", out var cs) && cs.ValueKind == JsonValueKind.Array)
            foreach (var c in cs.EnumerateArray())
            {
                var yd = new Yard
                {
                    Port = c.TryGetProperty("port", out var p) ? p.GetString() ?? "" : "",
                    Name = c.TryGetProperty("nom", out var n) ? n.GetString() ?? "" : ""
                };
                if (c.TryGetProperty("navires", out var ns) && ns.ValueKind == JsonValueKind.Array)
                    foreach (var s in ns.EnumerateArray()) if (s.GetString() is { Length: > 0 } id) yd.Ships.Add(id);
                if (yd.Port.Length > 0) y.Yards.Add(yd);
            }
        return y;
    }
}
