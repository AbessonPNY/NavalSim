using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace NavalSim;

/// <summary>
/// CE QUE COÛTE LA CARTE À CHARGER (demandé : « le coût en ms du chargement des assets de
/// la map, hors bateau et physique », affiché sous les images par seconde). Chaque étape
/// s'y inscrit en se chronométrant : le monde lu, les villes, la terre (ses carreaux, y
/// compris ceux qui se bâtissent plus tard en chemin), les modèles posés, la végétation,
/// les pontons, les ajouts du mode création. Les navires et la physique n'y sont pas.
/// Un total qui ne fait que croître : une carreau bâti en route s'y ajoute aussi.
/// </summary>
public static class MapCost
{
    static readonly Dictionary<string, double> Ms = new();
    static readonly List<string> Order = new();
    public static double Total { get; private set; }

    public static void Add(string part, double ms)
    {
        if (!Ms.ContainsKey(part)) { Ms[part] = 0; Order.Add(part); }
        Ms[part] += ms;
        Total += ms;
    }

    /// <summary>Chronométrer un bloc : <c>using (MapCost.Time("villes")) { … }</c>.</summary>
    public static Span Time(string part) => new(part);

    public readonly struct Span : IDisposable
    {
        readonly string _part;
        readonly long _t0;
        public Span(string part) { _part = part; _t0 = Stopwatch.GetTimestamp(); }
        public void Dispose() => Add(_part, (Stopwatch.GetTimestamp() - _t0) * 1000.0 / Stopwatch.Frequency);
    }

    /// <summary>Le détail, une étape après l'autre : « monde 610 · villes 340 · … ».</summary>
    public static string Detail()
    {
        var sb = new StringBuilder();
        foreach (var p in Order)
        {
            if (sb.Length > 0) sb.Append(" · ");
            sb.Append(p).Append(' ').Append(Ms[p].ToString("F0"));
        }
        return sb.ToString();
    }
}
