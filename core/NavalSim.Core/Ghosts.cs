using System;
using System.Collections.Generic;
using System.Text.Json;

namespace NavalSim.Core;

/// <summary>Les règles de la flotte fantôme — settings.json → ghosts, Naval.GHOSTS.</summary>
public sealed class GhostRules
{
    public bool Enabled = true;
    public string Name = "Le Cimetière des Galions";
    public double X = -9000, Z = 6000;          // mètres VRAIS : le large, 70 m de fond, 5,7 km de toute côte
    public double Radius = 2500;                // le lieu, tel que la carte le trace
    public double LingerBefore = 90;            // secondes de nuit passées dedans avant qu'ils viennent
    public double AggroRange = 350;             // plus près que cela de l'un d'eux, et il se retourne contre vous
    public string[] SideA = { "frigate17e", "frigate17e" };
    public string[] SideB = { "pirate", "pirate" };
    public double Spread = 350;                 // à quelle distance du milieu chaque ligne commence
    public double Fade = 6;                     // secondes pour venir et pour partir
    public double Opacity = 0.45;
    public double VictorLinger = 10;            // ce que les vainqueurs restent avant de se dissoudre
    public double SunkLinger = 25;              // et les vaincus, en sombrant, avant de n'être plus

    public static GhostRules FromJson(JsonElement j)
    {
        var r = new GhostRules();
        double D(string k, double v) => j.TryGetProperty(k, out var e) && e.ValueKind == JsonValueKind.Number ? e.GetDouble() : v;
        string[] L(string k, string[] v)
        {
            if (!j.TryGetProperty(k, out var e) || e.ValueKind != JsonValueKind.Array) return v;
            var a = new List<string>();
            foreach (var x in e.EnumerateArray()) if (x.ValueKind == JsonValueKind.String) a.Add(x.GetString()!);
            return a.ToArray();
        }
        if (j.TryGetProperty("enabled", out var en) && (en.ValueKind == JsonValueKind.True || en.ValueKind == JsonValueKind.False))
            r.Enabled = en.GetBoolean();
        if (j.TryGetProperty("name", out var nm) && nm.ValueKind == JsonValueKind.String) r.Name = nm.GetString()!;
        r.X = D("x", r.X); r.Z = D("z", r.Z); r.Radius = D("radius", r.Radius);
        r.LingerBefore = D("lingerBefore", r.LingerBefore); r.AggroRange = D("aggroRange", r.AggroRange);
        r.SideA = L("sideA", r.SideA); r.SideB = L("sideB", r.SideB);
        r.Spread = D("spread", r.Spread); r.Fade = D("fade", r.Fade); r.Opacity = D("opacity", r.Opacity);
        r.VictorLinger = D("victorLinger", r.VictorLinger); r.SunkLinger = D("sunkLinger", r.SunkLinger);
        return r;
    }
}

/// <summary>Un spectre de la scène : sa coque, son camp, où il en est de son fondu, et contre qui il se bat.</summary>
public sealed class Ghost
{
    public required ShipPhysics Physics;
    /// <summary>Ce que le moteur y attache — son nœud.</summary>
    public object? Tag;
    public int Side;
    /// <summary>De 0 (absent) à 1 (entier) ; il va vers <see cref="Want"/> en <see cref="GhostRules.Fade"/> secondes.</summary>
    public double Fade, Want = 1, Wait;
    public bool FadeOnSink;
    public double? SunkAt;
    /// <summary>La coque qu'il combat (provoquePar) : un fantôme de l'autre camp, ou le témoin trop proche.</summary>
    public ShipPhysics? Foe;
}

/// <summary>Une coque mise à l'eau pour la scène : son solveur, son nœud, et sa flottaison d'équilibre.</summary>
public readonly record struct GhostHull(ShipPhysics Physics, object? Tag, double EqY);

/// <summary>Ce que la scène emprunte à l'hôte : la flotte, les mots.</summary>
public interface IGhostHost
{
    /// <summary>Combien de coques la flotte peut encore prendre.</summary>
    int Room { get; }
    /// <summary>Mettre à l'eau une coque de cette fiche, sans humeur de pirate ; null si impossible.</summary>
    GhostHull? Launch(string id);
    /// <summary>La retirer de la flotte.</summary>
    void Scuttle(Ghost g);
    void Say(string msg);
}

/// <summary>
/// LA FLOTTE FANTÔME — ghosts.js, la partie qui ne dessine rien.
///
/// En un lieu de la carte — en mètres vrais, comme les îles — et seulement la
/// nuit, un navire qui s'attarde assiste à une bataille d'un autre siècle :
/// quatre coques pâles, deux contre deux. Ce sont de VRAIES coques de la flotte,
/// menées et combattues par la même barre et les mêmes pièces ; ce qui en fait
/// des spectres est décidé autour d'elles :
///
///   · INTANGIBLES pour tout ce qui n'est pas un spectre — l'artillerie demande
///     <see cref="CanTouch"/> avant qu'un boulet compte —, sauf pour le navire
///     qui vient trop près : l'un d'eux se retourne alors contre lui, et son
///     boulet est bien réel ;
///   · elles FINISSENT au premier de trois événements : l'aube, un camp coulé
///     (les vaincus sombrent, les vainqueurs se dissolvent), ou le départ du
///     témoin.
///
/// La pâleur, la brume et la toile en loques sont l'affaire du moteur, qui lit
/// <see cref="Ghost.Fade"/>.
/// </summary>
public sealed class GhostScene
{
    public GhostRules Rules;
    public string State { get; private set; } = "idle";     // idle · battle · ending
    public double Linger;
    /// <summary>Une bataille par nuit.</summary>
    public bool Spent;
    bool _warned, _aggroSaid;
    public readonly List<Ghost> Entries = new();
    readonly List<Ghost> _alive = new();

    public GhostScene(GhostRules? rules = null) { Rules = rules ?? new GhostRules(); }

    public bool Active => State != "idle";

    public Ghost? Of(ShipPhysics? p)
    {
        if (p == null) return null;
        foreach (var g in Entries) if (g.Physics == p) return g;
        return null;
    }

    /// <summary>
    /// Un boulet de <paramref name="from"/> peut-il toucher <paramref name="to"/> ?
    /// Les spectres se touchent entre eux. Un spectre ne touche un vivant que
    /// s'il s'est retourné contre lui. Les vivants ne touchent jamais un spectre.
    /// </summary>
    public bool CanTouch(ShipPhysics? from, ShipPhysics to)
    {
        var fg = Of(from);
        var tg = Of(to);
        if (fg != null && tg != null) return true;
        if (tg != null) return false;
        if (fg != null) return fg.Foe == to;
        return true;
    }

    /// <summary>
    /// Une image. <paramref name="trueX"/>, <paramref name="trueZ"/> : le témoin en
    /// mètres vrais ; <paramref name="lit"/> : il fait nuit, par la règle des
    /// fanaux ; <paramref name="origin"/> : le vrai zéro de la mer.
    /// </summary>
    public void Update(double dt, ShipPhysics player, double trueX, double trueZ, bool lit,
                       Vec3d origin, double t, IGhostHost host)
    {
        var G = Rules;
        if (!G.Enabled) return;
        if (!lit) Spent = false;
        double d = Math.Sqrt((trueX - G.X) * (trueX - G.X) + (trueZ - G.Z) * (trueZ - G.Z));

        if (State == "idle")
        {
            bool inside = d < G.Radius && lit && !Spent && !player.Foundered;
            Linger = inside ? Linger + dt : 0;
            if (inside && !_warned && Linger > G.LingerBefore * 0.5)
            {
                _warned = true;
                host.Say("Une brume froide monte sur " + G.Name + "…");
            }
            if (Linger > G.LingerBefore) Rise(player, trueX, trueZ, origin, host);
            return;
        }

        // --- la bataille, et comment elle finit ---
        _alive.Clear();
        foreach (var e in Entries) if (!e.Physics.Foundered) _alive.Add(e);
        bool a0 = false, a1 = false;
        foreach (var e in _alive) { if (e.Side == 0) a0 = true; else a1 = true; }
        if (State == "battle")
        {
            if (!lit) End("dawn", host);
            else if (d > G.Radius * 1.3) End("left", host);
            else if (!a0 || !a1) End("victory", host);
        }

        // les cibles : l'ennemi fantôme le plus proche — ou le témoin, s'il vient trop près
        var pb = player.Body;
        foreach (var e in _alive)
        {
            ShipPhysics? foe = null;
            double best = double.PositiveInfinity;
            if (State == "battle" && !player.Foundered && (e.Physics.Body.Pos - pb.Pos).Length < G.AggroRange)
            {
                foe = player;
                if (!_aggroSaid) { _aggroSaid = true; host.Say("Les spectres vous ont vu !"); }
            }
            else
                foreach (var o in _alive)
                {
                    if (o.Side == e.Side) continue;
                    double dd = (e.Physics.Body.Pos - o.Physics.Body.Pos).Length;
                    if (dd < best) { best = dd; foe = o.Physics; }
                }
            e.Foe = State == "battle" ? foe : null;
        }

        // --- les fondus ---
        for (int i = Entries.Count - 1; i >= 0; i--)
        {
            var g = Entries[i];
            if (State == "ending" && !g.FadeOnSink)
            {
                g.Wait -= dt;
                if (g.Wait <= 0) g.Want = 0;
            }
            if (g.Physics.Foundered && g.SunkAt == null && !g.FadeOnSink)
            {
                g.SunkAt = t;
                g.Wait = G.SunkLinger; g.FadeOnSink = true;
            }
            if (g.FadeOnSink) { g.Wait -= dt; if (g.Wait <= 0) g.Want = 0; }
            double step = dt / Math.Max(0.1, G.Fade);
            g.Fade += Math.Max(-step, Math.Min(step, g.Want - g.Fade));
            if (g.Want == 0 && g.Fade <= 0)
            {
                Entries.RemoveAt(i);
                host.Scuttle(g);
            }
        }
        if (State == "ending" && Entries.Count == 0)
        {
            State = "idle";
            Linger = 0;
            _warned = false;
        }
    }

    /* Quatre coques, deux lignes, face à face de part et d'autre du milieu du
       lieu, posées par le travers du témoin pour qu'il voie tout. */
    void Rise(ShipPhysics player, double trueX, double trueZ, Vec3d origin, IGhostHost host)
    {
        var G = Rules;
        Spent = true;
        _aggroSaid = false;
        var want = new List<(string Id, int Side)>();
        foreach (var id in G.SideA) want.Add((id, 0));
        foreach (var id in G.SideB) want.Add((id, 1));
        int n = Math.Min(want.Count, host.Room);
        if (n < 2) return;

        // l'axe des deux lignes : en travers de la ligne de visée du témoin
        double cx = G.X - origin.X, cz = G.Z - origin.Z;
        double ax = trueX - G.X, az = trueZ - G.Z;
        double al = Math.Sqrt(ax * ax + az * az);
        if (al == 0) al = 1;
        (ax, az) = (-az / al, ax / al);

        // pris dans la liste choisie de sorte que chaque camp en garde au moins un
        var order = new List<(string Id, int Side)>();
        if (n >= want.Count) order.AddRange(want);
        else
        {
            for (int i = 0; i < want.Count; i += 2) order.Add(want[i]);
            for (int i = 1; i < want.Count; i += 2) order.Add(want[i]);
            order.RemoveRange(n, order.Count - n);
        }
        int[] perSide = { 0, 0 };
        foreach (var (id, side) in order)
        {
            var h = host.Launch(id);
            if (h is not GhostHull hull) continue;
            int k = perSide[side]++;
            int sgn = side == 0 ? -1 : 1;
            double lat = (k - 0.5) * 160;                    // de front, à une encablure
            var b = hull.Physics.Body;
            b.Pos = new Vec3d(cx + ax * sgn * G.Spread + az * lat, b.Pos.Y, cz + az * sgn * G.Spread - ax * lat);
            // cap sur l'autre ligne
            b.Quat = Quatd.FromAxisAngle(new Vec3d(0, 1, 0), Math.Atan2(-ax * sgn, -az * sgn));
            b.Vel = Vec3d.Zero; b.AngVel = Vec3d.Zero;
            // elle glisse : tenue à sa flottaison, un peu au-dessus, et la houle passe au travers
            hull.Physics.Glide = hull.EqY + 0.25;
            b.Pos = new Vec3d(b.Pos.X, hull.Physics.Glide.Value, b.Pos.Z);
            Entries.Add(new Ghost { Physics = hull.Physics, Tag = hull.Tag, Side = side });
        }
        if (Entries.Count < 2)
        {
            foreach (var e in Entries) host.Scuttle(e);
            Entries.Clear();
            return;
        }
        State = "battle";
        host.Say("Des voiles pâles sortent de la brume : une bataille d’un autre siècle !");
    }

    void End(string why, IGhostHost host)
    {
        var G = Rules;
        State = "ending";
        foreach (var g in Entries)
        {
            if (g.FadeOnSink) continue;                       // elle sombre déjà à sa manière
            // les vaincus sombrent encore leur temps ; les autres partent après le leur
            if (g.Physics.Foundered) { g.FadeOnSink = true; g.Wait = G.SunkLinger; continue; }
            g.Wait = why == "victory" ? G.VictorLinger : 0;
        }
        host.Say(why == "dawn" ? "Au premier jour, les navires fantômes s’effacent."
               : why == "left" ? "Derrière vous, la bataille s’efface dans la nuit."
               : "Un camp sombre… et le vainqueur se dissout dans la brume.");
    }
}
