using System.Text.Json;

namespace NavalSim.Core;

/// <summary>
/// Une pièce, dans le repère de SA coque : sa bouche, l'axe de son tube, son
/// calibre relatif, son groupe — +1 tribord, −1 bâbord, +2 poupe, −2 proue,
/// choisis pour que « l'autre » soit le négatif.
/// </summary>
public sealed class Gun
{
    public int Side;
    public Vec3d P;                      // la bouche
    public Vec3d Dir;                    // le long du tube, vers l'extérieur
    public double Cal = 1;               // relatif au travers : une pièce de chasse est plus petite
    public bool Chase;
    public double ReadyAt;               // l'heure de jeu où elle est de nouveau chargée
    public bool Out;                     // démontée
    public double Damage;
}

/// <summary>La batterie d'un navire, et le curseur qui la parcourt coup par coup.</summary>
public sealed class Battery
{
    public readonly List<Gun> Guns = new();
    internal readonly Dictionary<int, int> Cursor = new();

    /// <summary>Combien d'un groupe sont en état, et combien il en avait.</summary>
    public (int Ok, int All) Count(int side)
    {
        int ok = 0, all = 0;
        foreach (var g in Guns) if (g.Side == side) { all++; if (!g.Out) ok++; }
        return (ok, all);
    }

    public bool Has(int side) { foreach (var g in Guns) if (g.Side == side) return true; return false; }

    /* UN BOULET DANS SON FLANC EST UN BOULET DANS SA BATTERIE. Ce qui traverse le
       bordé au droit d'une pièce brise son affût ou tue ses servants : chaque
       pièce prend des dégâts par sa distance au coup, dans une travée de huit
       centièmes de sa longueur, et par le calibre qui l'a fait. Hors de combat à
       un ; ils S'ACCUMULENT, si bien qu'un bord battu encore et encore perd ses
       pièces une à une. `local` : où le coup est entré, dans son repère. */
    public List<Gun> Wound(Vec3d local, double k, double shipL)
    {
        var outList = new List<Gun>();
        double R = Math.Max(1.5, 0.08 * shipL);
        double blow = 1.6 * Math.Min(2, k);
        foreach (var g in Guns)
        {
            if (g.Out) continue;
            double d = (local - g.P).Length;
            if (d >= R) continue;
            g.Damage += (1 - d / R) * blow;
            if (g.Damage >= 1) { g.Out = true; outList.Add(g); }
        }
        return outList;
    }

    /// <summary>Un radoub remonte ses pièces, chargées.</summary>
    public void Restore()
    {
        foreach (var g in Guns) { g.Out = false; g.Damage = 0; g.ReadyAt = 0; }
    }
}

/// <summary>
/// Une coque que les boulets peuvent chercher : sa physique, sa batterie, et son
/// flanc tel que l'ŒIL le voit — la coquille du modèle par compartiment, là où
/// elle en a un (sinon les compartiments du solveur) — et ses mâts debout.
/// </summary>
public sealed class ShotTarget
{
    public required ShipPhysics Physics;
    public Battery? Battery;
    /// <summary>Par compartiment : demi-largeur, pont, quille, z0, z1 — le flanc du modèle. Nul : celui du solveur.</summary>
    public (double Half, double Deck, double Keel, double Z0, double Z1)[]? Shell;
    /// <summary>Les mâts debout : pied, hauteur, station, indice du mât.</summary>
    public Func<IReadOnlyList<(double Heel, double Height, double Z, int Fall)>>? Masts;
    public object? Tag;
}

/// <summary>Les règles de jeu de l'artillerie, settings.json → gunnery.</summary>
public sealed class GunnerySettings
{
    public double ReloadLo = 30, ReloadHi = 60;     // secondes qu'une pièce est hors d'usage après le coup

    public static GunnerySettings FromJson(JsonElement j)
    {
        var s = new GunnerySettings();
        if (j.TryGetProperty("reload", out var r) && r.ValueKind == JsonValueKind.Array && r.GetArrayLength() == 2)
        { s.ReloadLo = r[0].GetDouble(); s.ReloadHi = r[1].GetDouble(); }
        return s;
    }
}

/// <summary>
/// LES GROSSES PIÈCES — la partie de guns.js qui ne dessine rien : la bordée, le
/// feu qui attend le roulis, le boulet en vol et ce qu'il trouve. Le dessin (la
/// flamme, la fumée qui dérive sous le vent, la lueur sur le bordé) est l'affaire
/// du moteur, prévenu par <see cref="OnFire"/>.
/// </summary>
public sealed class Gunnery
{
    public GunnerySettings Rules;
    /// <summary>L'horloge des pièces, en secondes de jeu.</summary>
    public double Clock;
    public readonly List<Shot> Shots = new();
    public readonly List<ShotTarget> Targets = new();

    public sealed class Shot
    {
        public Vec3d P, V;
        public double T, C, K;
        public ShipPhysics From = null!;
    }

    struct Queued { public double T; public Gun G; public ShipPhysics Ship; }
    readonly List<Queued> _queue = new();

    /// <summary>Une pièce parle : sa bouche (monde local), l'axe du coup, son calibre, la mer sous elle, et qui a tiré.</summary>
    public Action<Vec3d, Vec3d, double, double, ShipPhysics>? OnFire;
    /// <summary>Un coup au but : la cible, « mast » ou « hull », l'indice, la hauteur en part de son creux, la vitesse, le calibre, qui a tiré, où, dans quel sens.</summary>
    public Action<ShotTarget, string, int, double, double, double, ShipPhysics, Vec3d, Vec3d>? OnStrike;
    /// <summary>Un boulet dans la mer : où, et la gerbe qu'il lève.</summary>
    public Action<Vec3d, double, double, double>? OnSplash;
    /// <summary>Ce qui n'est pas un navire et peut être touché (le kraken) : le segment balayé, et rend vrai s'il a touché.</summary>
    public Func<Vec3d, Vec3d, Shot, bool>? OnCreature;
    /// <summary>Qui a tiré, et la coque sur le chemin : faux, et le boulet la traverse comme si elle n'était pas là — ce qui, pour un fantôme, est le cas.</summary>
    public Func<ShipPhysics, ShotTarget, bool>? CanHit;

    readonly Func<double> _random;

    public Gunnery(GunnerySettings? rules = null, Func<double>? random = null)
    {
        Rules = rules ?? new GunnerySettings();
        var r = new Random();
        _random = random ?? r.NextDouble;
    }

    /* Une pièce qui a tiré est hors d'usage le temps que ses servants l'écouvillonnent,
       la chargent, la refoulent et la remettent en batterie — tiré par pièce, chaque
       équipe a son allure. Une minute et demie à deux pour une pièce lourde servie par
       une marine exercée ; le jeu en prend trente à soixante, pour qu'on puisse jouer. */
    void Spent(Gun g, double after) =>
        g.ReadyAt = Clock + after + Rules.ReloadLo + _random() * (Rules.ReloadHi - Rules.ReloadLo);

    /// <summary>Ce qui est chargé dans un groupe, ce qui est monté, et dans combien la prochaine.</summary>
    public (int Ready, int All, double Next) Loaded(Battery b, int side)
    {
        int ready = 0, all = 0; double next = double.PositiveInfinity;
        foreach (var g in b.Guns)
        {
            if (g.Side != side || g.Out) continue;
            all++;
            double w = g.ReadyAt - Clock;
            if (w <= 0) ready++; else next = Math.Min(next, w);
        }
        return (ready, all, ready > 0 ? 0 : next);
    }

    /* UNE PIÈCE, et la suivante la fois d'après : la batterie parcourue de l'avant à
       l'arrière coup par coup — ce qui est la façon dont on sert une batterie quand
       elle ne tire pas ensemble, et donne au joueur de quoi faire entre deux bordées. */
    public int FireOne(Battery b, int side, ShipPhysics ship)
    {
        var all = new List<Gun>();
        foreach (var g in b.Guns) if (g.Side == side && !g.Out) all.Add(g);
        if (all.Count == 0) return 0;
        b.Cursor.TryGetValue(side, out int cur);
        int start = cur % all.Count;
        for (int n = 0; n < all.Count; n++)
        {
            var g = all[(start + n) % all.Count];
            if (g.ReadyAt > Clock) continue;              // encore en charge : la suivante
            b.Cursor[side] = (start + n + 1) % all.Count;
            Spent(g, 0);
            _queue.Add(new Queued { T = 0, G = g, Ship = ship });
            return 1;
        }
        return 0;
    }

    /* LA BORDÉE ENTIÈRE, pièce par pièce et non d'un bloc : en ripple le long du
       bord, et IRRÉGULIÈRE — chaque chef de pièce attend son roulis et sa mèche, et
       de temps en temps une pièce fait long feu. Seules parlent les pièces chargées ;
       `max` la borne à ce que la soute peut payer. */
    public int Broadside(Battery b, int side, ShipPhysics ship, int max = int.MaxValue)
    {
        var g = new List<Gun>();
        foreach (var x in b.Guns) if (x.Side == side && !x.Out && x.ReadyAt <= Clock) g.Add(x);
        int fire = Math.Min(g.Count, Math.Max(0, max));
        double delay = 0;
        for (int n = 0; n < fire; n++)
        {
            _queue.Add(new Queued { T = -delay, G = g[n], Ship = ship });
            Spent(g[n], delay);
            delay += 0.08 + _random() * 0.22;
            if (_random() < 0.18) delay += 0.15 + _random() * 0.35;   // une fait long feu
        }
        return fire;
    }

    /* UNE PIÈCE PART. Pointée AU-DESSOUS de l'horizon, pas au-dessus : de but en
       blanc veut dire mettre le boulet sur la MER à une portée de référence, ce à
       quoi servait le coin sous la culasse — l'angle sort de la hauteur de la bouche
       au-dessus de l'eau, si bien qu'une batterie basse est pointée presque à plat et
       une haute bien plus bas, sans rien à régler par navire. Et pointée contre
       l'HORIZON, pas contre son pont : sa gîte est l'affaire du chef de pièce, pas du
       boulet — pointée sur le pont, la batterie au vent tirait à 830 m et celle sous
       le vent se mettait dans l'eau à 45. */
    void Fire(Gun g, ShipPhysics ship, Ocean ocean, double t)
    {
        var body = ship.Body;
        double k = Math.Max(0.35, ship.Spec.L / 60) * g.Cal;
        Vec3d at = body.Quat.Rotate(g.P) + body.Pos;
        double overSea = Math.Max(0, g.P.Y + body.Pos.Y);
        double elev = -Math.Max(0, overSea - 2.9) / 300;      // 2,9 m : la chute à 300 m
        Vec3d d = body.Quat.Rotate(g.Dir);
        d = new Vec3d(d.X, 0, d.Z);                          // son relèvement, pris à plat
        if (d.X * d.X + d.Z * d.Z < 1e-6) d = g.Dir;
        d = d.Normalized();
        Vec3d outDir = new Vec3d(d.X, elev, d.Z).Normalized();
        Vec3d up = new(0, 1, 0);
        Vec3d sideV = outDir.Cross(up).Normalized();
        double sea = ocean.Sample(at.X, at.Z, t) + 0.25 * k;

        /* LE BOULET, qui vole pour de vrai : 440 m/s à la bouche, puis une traînée
           QUADRATIQUE — une sphère est un piètre projectile ; c·v² avec
           c = ρ·Cd·A/2m ≈ 0,001 par mètre donne, sans aucun cas écrit, une vitesse en
           1/(1 + c·x), 1,46 s pour 500 m, plus de dix mètres de chute là-dessus — donc
           la portée de but en blanc. c va comme l'inverse du calibre. Pas deux charges
           pareilles, quelques pour cent, et un âme lisse n'est pas un instrument de
           précision. Tiré d'un pont qui marche : son erre entre dans le coup. */
        double charge = 0.94 + _random() * 0.12;
        const double spread = 0.010;
        Vec3d v0 = RotateAbout(RotateAbout(outDir, sideV, (_random() - 0.5) * spread), up, (_random() - 0.5) * spread)
                   * (440 * charge) + body.Vel;
        Shots.Add(new Shot { P = at, V = v0, C = 0.00097 / k, K = k, From = ship });

        /* ET ELLE LE SENT : une vraie impulsion à une vraie hauteur, donc un vrai
           moment de gîte — trois mille kg·m/s à travers les tourillons, à six mètres
           au-dessus de son centre de gravité. Pas grossie pour qu'on la sente : qu'une
           bordée couche un navire est presque un mythe, et l'arithmétique le dit. */
        double impulse = 3000 * k * k;
        Vec3d push = outDir * -impulse;
        body.Vel = body.Vel + push / body.Mass;
        Vec3d arm = body.Quat.Rotate(g.P - body.Com);
        Vec3d tq = arm.Cross(push);
        body.AngVel = body.AngVel + new Vec3d(tq.X / body.Ib.X, tq.Y / body.Ib.Y, tq.Z / body.Ib.Z);

        OnFire?.Invoke(at, outDir, k, sea, ship);
    }

    static Vec3d RotateAbout(Vec3d v, Vec3d axis, double a)
    {
        // Rodrigues : v·cos a + (k×v)·sin a + k(k·v)(1 − cos a)
        double c = Math.Cos(a), s = Math.Sin(a);
        return v * c + axis.Cross(v) * s + axis * (axis.Dot(v) * (1 - c));
    }

    /// <summary>
    /// Une image : les boulets en vol, puis les pièces qui attendent leur roulis.
    /// </summary>
    public void Update(double dt, Ocean ocean, double t)
    {
        Clock += dt;
        Flight(dt, ocean, t);
        /* ELLE TIRE SUR LE ROULIS, et sans cela les pièces ne servent à rien par une
           mer qui vaille : son tangage en force 4 fait cinquante fois le pointage.
           Chaque pièce, son tour venu, attend que sa bouche REDESCENDE — (ω × d).y,
           qui lit la gîte pour une pièce de travers et le tangage pour une pièce de
           chasse —, et au bout de deux secondes et demie tire quoi qu'elle fasse.
           Immobile compte comme bon : par calme plat il n'y a pas de roulis à attendre. */
        for (int i = _queue.Count - 1; i >= 0; i--)
        {
            var q = _queue[i];
            q.T += dt;
            _queue[i] = q;
            if (q.T < 0) continue;
            if (q.T < 2.5)
            {
                var b = q.Ship.Body;
                Vec3d d = b.Quat.Rotate(q.G.Dir);
                if (b.AngVel.Cross(d).Y > 0.004) continue;
            }
            Fire(q.G, q.Ship, ocean, t);
            _queue.RemoveAt(i);
        }
    }

    /* OÙ UN SEGMENT ENTRE D'ABORD DANS UNE BOÎTE, en part de lui-même, ou −1. Un
       segment et non un point : un boulet traverse quatorze mètres en une image, plus
       large que la coque qu'il doit toucher — testé point par point il passerait au
       travers à chaque fois. */
    static double Slab(Vec3d p0, Vec3d p1, Vec3d min, Vec3d max)
    {
        double t0 = 0, t1 = 1;
        for (int a = 0; a < 3; a++)
        {
            double s0 = a == 0 ? p0.X : a == 1 ? p0.Y : p0.Z, s1 = a == 0 ? p1.X : a == 1 ? p1.Y : p1.Z;
            double lo = a == 0 ? min.X : a == 1 ? min.Y : min.Z, hi = a == 0 ? max.X : a == 1 ? max.Y : max.Z;
            double d = s1 - s0;
            if (Math.Abs(d) < 1e-9) { if (s0 < lo || s0 > hi) return -1; continue; }
            double ta = (lo - s0) / d, tb = (hi - s0) / d;
            if (ta > tb) (ta, tb) = (tb, ta);
            if (ta > t0) t0 = ta;
            if (tb < t1) t1 = tb;
            if (t0 > t1) return -1;
        }
        return t0;
    }

    /* Chaque boulet en l'air, et ce qu'il trouve. Sous-pas en DISTANCE plutôt qu'en
       temps : quatorze mètres par image à la bouche, deux en fin de course — quatre
       mètres par pas gardent la traînée honnête là où elle travaille. */
    void Flight(double dt, Ocean ocean, double t)
    {
        for (int i = Shots.Count - 1; i >= 0; i--)
        {
            var b = Shots[i];
            b.T += dt;
            bool dead = b.T > 12;                       // rien ne porte aussi longtemps
            double speed = b.V.Length;
            int n = Math.Max(1, Math.Min(16, (int)Math.Ceiling(speed * dt / 4)));
            double h = dt / n;
            for (int s = 0; s < n && !dead; s++)
            {
                Vec3d a0 = b.P;
                double sp = b.V.Length;
                b.V = b.V + b.V * (-b.C * sp * h);         // c·v², le long de sa course
                b.V = new Vec3d(b.V.X, b.V.Y - 9.81 * h, b.V.Z);
                b.P = b.P + b.V * h;
                Vec3d a1 = b.P;
                dead = HitShips(b, a0, a1) || (OnCreature?.Invoke(a0, a1, b) ?? false);
                if (dead) break;
                // et à défaut, la mer — une colonne HAUTE et MINCE, pas un dôme
                double seaY = ocean.Sample(b.P.X, b.P.Z, t);
                if (b.P.Y <= seaY)
                {
                    OnSplash?.Invoke(new Vec3d(b.P.X, seaY, b.P.Z), 26, 16, 3.0);
                    dead = true;
                }
            }
            if (dead) Shots.RemoveAt(i);
        }
    }

    bool HitShips(Shot b, Vec3d a0, Vec3d a1)
    {
        foreach (var e in Targets)
        {
            var ph = e.Physics;
            if (ph == b.From || ph.Foundered) continue;
            if (CanHit != null && !CanHit(b.From, e)) continue;
            var body = ph.Body;
            var inv = body.Quat.Inverted();
            Vec3d l0 = inv.Rotate(a0 - body.Pos), l1 = inv.Rotate(a1 - body.Pos);
            Vec3d dir = (a1 - a0).Normalized();
            // ses ESPARS se dressent au-dessus de sa coque : demandés d'abord
            if (e.Masts != null)
            {
                var masts = e.Masts();
                for (int mi = 0; mi < masts.Count; mi++)
                {
                    var m = masts[mi];
                    double u = Slab(l0, l1, new Vec3d(-1.1, m.Heel, m.Z - 1.1), new Vec3d(1.1, m.Heel + m.Height, m.Z + 1.1));
                    if (u < 0) continue;
                    OnStrike?.Invoke(e, "mast", m.Fall, 0.5, b.V.Length, b.K, b.From,
                        body.Quat.Rotate(l0 + (l1 - l0) * u) + body.Pos, dir);
                    return true;
                }
            }
            /* Son flanc tel que l'ŒIL l'a — la coquille du modèle où elle en a une —,
               la grille de sondes seulement pour une coque procédurale : les deux
               diffèrent de mètres en hauteur, et le boulet doit s'accorder à l'image. */
            var comps = ph.Comps;
            double L = ph.Spec.L;
            for (int ci = 0; ci < comps.Length; ci++)
            {
                Vec3d mn, mx;
                if (e.Shell != null && ci < e.Shell.Length)
                {
                    var sh = e.Shell[ci];
                    mn = new Vec3d(-sh.Half, sh.Keel, sh.Z0); mx = new Vec3d(sh.Half, sh.Deck, sh.Z1);
                }
                else
                {
                    var c = comps[ci];
                    if (c.Cap <= 0) continue;
                    mn = new Vec3d(-c.HalfB, c.KeelY, -L / 2 + ci * L / comps.Length);
                    mx = new Vec3d(c.HalfB, c.DeckY, -L / 2 + (ci + 1) * L / comps.Length);
                }
                double u = Slab(l0, l1, mn, mx);
                if (u < 0) continue;
                Vec3d hit = l0 + (l1 - l0) * u;
                // une part de son creux : la même chose dans les deux repères
                double frac = (hit.Y - mn.Y) / Math.Max(0.5, mx.Y - mn.Y);
                OnStrike?.Invoke(e, "hull", ci, frac, b.V.Length, b.K, b.From, body.Quat.Rotate(hit) + body.Pos, dir);
                return true;
            }
        }
        return false;
    }

    public void Rebase(double dx, double dz)
    {
        var d = new Vec3d(dx, 0, dz);
        foreach (var s in Shots) s.P = s.P - d;
    }
}
