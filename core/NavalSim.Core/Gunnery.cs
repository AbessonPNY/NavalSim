using System;
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
    public List<Gun> Wound(Vec3d local, double k, double shipL, double bite = 1)
    {
        var outList = new List<Gun>();
        double R = Math.Max(1.5, 0.08 * shipL);
        // et ce que le boulet avait encore dans le ventre : un coup mourant ne
        // démonte pas un affût qu'un coup à bout portant met en pièces
        double blow = 1.6 * Math.Min(2, k) * bite;
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

/// <summary>
/// CE QU'IL RESTE DANS LE BOULET QUAND IL ARRIVE — la règle de la distance, et
/// elle n'est écrite qu'ici.
///
/// Un boulet quitte la bouche à 440 m/s et perd sa vitesse en 1/(1 + c·x) : à
/// bout portant il l'a toute, à trois cents mètres les trois quarts, à six cents
/// les deux tiers. Ce qu'il fait au bois va comme son ÉNERGIE, donc comme le
/// CARRÉ de ce qui lui reste — c'est la seule loi, et elle donne d'elle-même les
/// deux faits qu'on attend : à bout portant on enfonce le bordé, de loin on le
/// cabosse. Rien n'est ajouté pour cela.
///
/// La vitesse de bouche dépend du calibre (une pièce de chasse pousse moins
/// fort), et la part qui reste est prise CONTRE LA SIENNE : une petite pièce
/// n'est pas punie deux fois, sa faiblesse est déjà dans son calibre.
/// </summary>
public static class Ball
{
    /// <summary>La vitesse à la bouche, avant la correction de calibre.</summary>
    public const double Muzzle = 440;

    /// <summary>Ce qu'une pièce de ce calibre pousse, en part d'une pièce de bordée.</summary>
    public static double Charge(double k) => 0.78 + 0.22 * k;

    /// <summary>
    /// La part de l'énergie de bouche qu'il a encore : (v/v₀)², entre 0 et 1.
    /// </summary>
    public static double Bite(double speed, double k)
    {
        double v0 = Muzzle * Charge(k);
        if (v0 < 1) return 1;
        double r = speed / v0;
        return Math.Clamp(r * r, 0, 1);
    }

    /* PAS DE SEUIL DU « BOULET MORT », ET C'EST MESURÉ. On en avait écrit un — en
       deçà de tant d'énergie, la balle marque et ne perce plus. Il ne se serait
       jamais déclenché : une pièce pointe LÉGÈREMENT VERS LE BAS (elle est à trois
       mètres sur l'eau), si bien qu'un boulet tombe à la mer avant d'avoir perdu
       assez de vitesse — 3,8 m de chute à 340 m, 9,1 m à 500 m, où il lui reste
       encore 38 % de son énergie. Une règle qui ne peut pas s'appliquer est une
       règle à ne pas écrire : la loi du carré suffit, seule. */
}

/// <summary>Les règles de jeu de l'artillerie, settings.json → gunnery.</summary>
public sealed class GunnerySettings
{
    public double ReloadLo = 30, ReloadHi = 60;     // secondes qu'une pièce est hors d'usage après le coup

    /// <summary>
    /// LA VIVACITÉ DU RECUL — un facteur, pas des secondes : 1 était la première
    /// mise au point, 2 (par défaut) va deux fois plus vite, le temps de recul et
    /// celui du retour à la batterie divisés d'autant. C'est un réglage à l'œil,
    /// et il n'appartient qu'au moteur qui fait bouger les pièces : la page ne
    /// les fait pas encore reculer et l'ignore donc sans dommage.
    /// </summary>
    public double RecoilSpeed = 2;

    /// <summary>
    /// LE FEU DE BOUCHE COMME LUMIÈRE — ce que le coup éclaire de son propre bord.
    ///
    /// Trois nombres, et ils ne servent qu'à l'œil : l'ÉNERGIE de la lampe, sa
    /// PORTÉE en mètres, et ce qu'elle DURE en secondes. Ce sont les seuls
    /// réglages d'artillerie qui ne changent rien au combat — une pièce ne tire
    /// ni plus loin ni plus fort parce que sa flamme éclaire mieux.
    ///
    /// L'énergie et la portée montent en k², k étant le calibre relatif : une
    /// pièce de chasse éclaire moins qu'un trente-six, ce qui est juste.
    /// </summary>
    public double FlashEnergy = 34, FlashRange = 26, FlashLife = 0.22;

    public static GunnerySettings FromJson(JsonElement j)
    {
        var s = new GunnerySettings();
        if (j.TryGetProperty("reload", out var r) && r.ValueKind == JsonValueKind.Array && r.GetArrayLength() == 2)
        { s.ReloadLo = r[0].GetDouble(); s.ReloadHi = r[1].GetDouble(); }
        if (j.TryGetProperty("recoilSpeed", out var v) && v.ValueKind == JsonValueKind.Number)
            s.RecoilSpeed = Math.Max(0.1, v.GetDouble());
        double D(string k, double d) => j.TryGetProperty(k, out var e) && e.ValueKind == JsonValueKind.Number ? e.GetDouble() : d;
        s.FlashEnergy = Math.Max(0, D("flashEnergy", s.FlashEnergy));
        s.FlashRange = Math.Max(1, D("flashRange", s.FlashRange));
        s.FlashLife = Math.Max(0.01, D("flashLife", s.FlashLife));
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

    struct Queued { public double T; public Gun G; public ShipPhysics Ship; public bool Together; }
    readonly List<Queued> _queue = new();

    /// <summary>Une pièce parle : sa bouche (monde local), l'axe du coup, son calibre, la mer sous elle, et qui a tiré.</summary>
    /// <summary>Une pièce parle : sa bouche, l axe du coup, son calibre, la mer sous elle, qui a tiré, et LAQUELLE — c est elle qui recule.</summary>
    public Action<Vec3d, Vec3d, double, double, ShipPhysics, Gun>? OnFire;
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

    /// <summary>
    /// DANS COMBIEN LE BORD ENTIER SERA PARÉ — la plus LENTE, et non la
    /// prochaine. <see cref="Loaded"/> ne donne l'attente que si rien n'est prêt,
    /// ce qui est la bonne réponse quand on tire à volonté et la mauvaise quand on
    /// attend le bord entier : à douze pièces sur quatorze, il annonçait zéro
    /// seconde (signalé par la mesure). Zéro quand tout est chargé.
    /// </summary>
    public double AllReadyIn(Battery b, int side)
    {
        double last = 0;
        foreach (var g in b.Guns)
        {
            if (g.Side != side || g.Out) continue;
            last = Math.Max(last, g.ReadyAt - Clock);
        }
        return Math.Max(0, last);
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
    public int Broadside(Battery b, int side, ShipPhysics ship, int max = int.MaxValue, bool together = false)
    {
        var g = new List<Gun>();
        foreach (var x in b.Guns) if (x.Side == side && !x.Out && x.ReadyAt <= Clock) g.Add(x);
        int fire = Math.Min(g.Count, Math.Max(0, max));
        double delay = 0;
        for (int n = 0; n < fire; n++)
        {
            _queue.Add(new Queued { T = -delay, G = g[n], Ship = ship, Together = together });
            Spent(g[n], delay);
            /* PARÉES, ELLES PARTENT ENSEMBLE. Une bordée tirée à volonté s'égrène
               le long du bord — chaque chef de pièce attend son roulis et sa
               mèche, et de temps en temps une fait long feu. Mais un bord PARÉ a
               été pointé et amorcé d'avance, et n'attend qu'un mot : quelques
               millièmes de seconde séparent alors les coups, le temps que l'ordre
               coure d'une pièce à l'autre. Pas de long feu non plus — on ne garde
               pas en batterie une pièce dont on doute.

               L'étalement est borné au BORD ENTIER et non pris par pièce : un pas
               fixe de huit millièmes ferait cent dix millisecondes sur quatorze
               pièces, et ce ne serait plus une salve. Cinquante millièmes d'un
               bout du bord à l'autre — quatre par pièce sur un bord de quatorze,
               dix sur un bord de six —, plus un rien d'irrégularité qu'aucune
               mèche n'évite. */
            if (together) delay += (fire > 1 ? 0.05 / (fire - 1) : 0) + _random() * 0.003;
            else
            {
                delay += 0.08 + _random() * 0.22;
                if (_random() < 0.18) delay += 0.15 + _random() * 0.35;   // une fait long feu
            }
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
        /* ET LA PETITE PIÈCE POUSSE MOINS FORT : tube plus court, charge plus
           légère. La traînée seule ne le disait pas — un boulet tiré à plat tombe
           à l eau en une seconde et demie quel que soit son calibre, si bien que
           la portée ne bougeait que de 3 % entre une pièce de bordée et un canon
           de chasse (signalé, mesuré). */
        double charge = (0.94 + _random() * 0.12) * Ball.Charge(k);
        const double spread = 0.010;
        Vec3d v0 = RotateAbout(RotateAbout(outDir, sideV, (_random() - 0.5) * spread), up, (_random() - 0.5) * spread)
                   * (Ball.Muzzle * charge) + body.Vel;
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

        OnFire?.Invoke(at, outDir, k, sea, ship, g);
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
            /* CHACUN ATTEND SON ROULIS — sauf sur ordre. Une pièce servie à
               volonté part quand sa bouche descend, et c'est ce qui fait qu'une
               bordée s'égrène. Mais un bord PARÉ part sur un mot : le capitaine a
               choisi le roulis pour tous, et chaque chef attendant le sien
               rendait la salve à sa traîne — mesuré, sept millièmes voulus entre
               pièces et quarante observés, six images d'attente par coup. */
            if (q.T < 2.5 && !q.Together)
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
