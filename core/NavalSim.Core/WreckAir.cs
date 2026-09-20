using System;
using System.Collections.Generic;

namespace NavalSim.Core;

/// <summary>Une coque telle que l'air la voit : son solveur, et sa coque VISIBLE si elle en a une.</summary>
public readonly record struct WreckHull(
    ShipPhysics Physics,
    (double Half, double Deck, double Keel, double Z0, double Z1)[]? Shell);

/// <summary>Un bouillon qui crève la surface : où, sa largeur, sa force.</summary>
public readonly record struct Boil(double X, double Z, double R, double W);

/// <summary>
/// L'AIR QUI REMONTE D'UN NAVIRE QUI COULE — wreck-air.js.
///
/// Rien ici ne décide qu'un naufrage doit faire des bulles. Le solveur sait déjà
/// combien d'eau entre dans chaque compartiment, et chaque mètre cube qui entre
/// en pousse un d'air dehors ; il le porte au compte du compartiment dès que la
/// sortie est noyée. Ce fichier ne fait que le porter jusqu'en haut.
///
/// ET IL PREND SON TEMPS, ce qui est l'essentiel de ce qui le fait lire comme
/// venant de DESSOUS et non comme une fontaine en surface. Une poche d'air monte
/// à une vitesse fixée par sa taille — la calotte sphérique de Davies et Taylor,
/// U ≈ 0,71·√(g·r) —, donc un mètre cube fait environ deux mètres par seconde, et
/// d'une épave par vingt mètres de fond il arrive dix secondes après être parti.
/// Le bouillonnement continue donc après qu'elle a disparu, et s'éteint au lieu
/// de s'arrêter. La même idée que le bruit d'un canon : le délai est une
/// division, et c'est lui l'effet.
///
/// Ce qui arrive fait deux choses, par des rouages qui existent déjà : une gerbe
/// basse (la réserve d'embrun) et un bouillon dans le champ d'écume, qui reste.
/// </summary>
public sealed class WreckAir
{
    sealed class Slug
    {
        public double X, Z, V, R, Rise, Spread, At;
        public bool Big;
    }

    sealed class Bubbling { public double X, Z, R, Born, Life, W; }
    sealed class CompState { public double Owed, Flux, Gulp = 0.5; }

    readonly List<Slug> _rising = new();
    readonly List<Bubbling> _boils = new();
    readonly List<Boil> _list = new();
    readonly Dictionary<Compartment, CompState> _state = new();
    readonly Dictionary<ShipPhysics, bool> _breath = new();
    readonly Func<double> _rng;
    public double Clock { get; private set; }

    /// <summary>Une gerbe : où, l'eau soulevée (m³), sa vitesse, et le jet quand c'est étroit et violent.</summary>
    public Action<Vec3d, double, double, double>? OnBurst;
    /// <summary>Son dernier souffle : où, et combien de mètres cubes d'un coup.</summary>
    public Action<Vec3d, double>? OnLastBreath;
    /// <summary>Une poche qui PART du fond : d'où (monde), son volume, sa vitesse de montée, et combien de temps elle a à monter.</summary>
    public Action<Vec3d, double, double, double>? OnSlug;

    /// <summary>Les bouillons de cette image, le plus fort d'abord — à poser dans le champ d'écume.</summary>
    public IReadOnlyList<Boil> Boils => _list;

    public WreckAir(Func<double>? random = null) { _rng = random ?? new Random().NextDouble; }

    /* L'air ne sort pas d'une coque en filet continu mais par GORGÉES — il
       s'amasse sous un barrot jusqu'à ce que la poche déborde. La taille suit le
       débit, si bien qu'un compartiment qui embarque la mer verte en lâche de
       grosses plusieurs fois par seconde, pendant que les dernières poches d'une
       épave remontent une à une, à quelques secondes d'intervalle. */
    double NextGulp(double flux)
    {
        double b = Math.Min(5, Math.Max(0.25, 0.35 * flux));
        return b * (0.45 + _rng() * 1.1);
    }

    public void Update(double dt, IReadOnlyList<WreckHull> hulls, Ocean ocean, double t)
    {
        if (dt <= 0) return;
        Clock += dt;

        // --- 1. ce que chaque coque a expiré, mis en route vers le haut ---
        foreach (var h in hulls)
        {
            var ph = h.Physics;
            if (ph.Comps.Length == 0) continue;
            LastBreath(h, ocean, t);
            foreach (var c in ph.Comps)
            {
                if (!_state.TryGetValue(c, out var s)) _state[c] = s = new CompState();
                double a = c.Air; c.Air = 0;
                // lissé sur une demi-seconde : le débit d'une image est trop nerveux pour tailler une gorgée
                s.Flux += (a / dt - s.Flux) * Math.Min(1, dt / 0.5);
                s.Owed += a;
                if (s.Owed < s.Gulp) continue;

                double depth = Math.Max(0.3, ocean.Sample(c.Vent.X, c.Vent.Z, t) - c.Vent.Y);
                int n = 0;
                while (s.Owed >= s.Gulp && n++ < 8 && _rising.Count < 400)
                {
                    double V = s.Gulp;
                    s.Owed -= V;
                    s.Gulp = NextGulp(s.Flux);
                    double r = Math.Cbrt(3 * V / (4 * Math.PI));
                    double rise = 0.71 * Math.Sqrt(9.81 * r);
                    /* Un panache s'élargit en montant : plus elle est profonde,
                       plus large est la tache qu'il crève — le bouillon au-dessus
                       d'une épave profonde est vaste et lent, celui d'une coque
                       encore en surface est serré. */
                    double spread = 0.4 + 0.12 * depth;
                    double ang = _rng() * Math.PI * 2, rad = spread * Math.Sqrt(_rng());
                    double x = c.Vent.X + Math.Cos(ang) * rad, z = c.Vent.Z + Math.Sin(ang) * rad;

                    /* TANT QU'ELLE EST EN SURFACE, L'AIR SORT LE LONG DE SES FLANCS.
                       Il s'échappe par toutes ses ouvertures — écoutilles, mais
                       sabords et pavois aussi — et surtout, ce qui crève DANS son
                       contour est caché par son propre bordé. Une part des gorgées
                       est donc portée à sa flottaison par le travers de la sortie,
                       et cette part s'efface à mesure qu'elle descend plus bas que
                       son propre creux, le panache se refermant alors sur elle. */
                    var S = ph.Spec;
                    double deep = Math.Min(1, Math.Max(0, (depth - S.D) / (S.D + 4)));
                    if (_rng() > deep)
                    {
                        var b = ph.Body;
                        Vec3d f = b.Quat.Rotate(new Vec3d(0, 0, 1));
                        double fl = Math.Sqrt(f.X * f.X + f.Z * f.Z);
                        double fx = fl > 1e-9 ? f.X / fl : 0, fz = fl > 1e-9 ? f.Z / fl : 1;
                        double sx = -fz, sz = fx;                       // par le travers
                        double along = (c.Vent.X - b.Pos.X) * fx + (c.Vent.Z - b.Pos.Z) * fz;
                        double ez = Math.Max(-0.95, Math.Min(0.95, along / (S.L * 0.5)));
                        double ex = Math.Sqrt(1 - ez * ez) * (_rng() < 0.5 ? -1 : 1);
                        double outr = 1.08 + 0.25 * _rng();
                        x = b.Pos.X + fx * ez * S.L * 0.5 * outr + sx * ex * S.B * 0.5 * outr;
                        z = b.Pos.Z + fz * ez * S.L * 0.5 * outr + sz * ex * S.B * 0.5 * outr;
                    }
                    _rising.Add(new Slug { X = x, Z = z, V = V, R = r, Rise = rise, Spread = spread, At = Clock + depth / rise });
                    // et on la MONTRE : sans cela l'air ne se voyait qu'en crevant la surface
                    OnSlug?.Invoke(new Vec3d(x, c.Vent.Y, z), V, rise, depth / rise);
                }
                if (s.Owed > 20) s.Owed = 20;      // jamais un arriéré qui éclate d'un coup
            }
        }

        // --- 2. ce qui arrive en haut ---
        int fired = 0;
        for (int i = _rising.Count - 1; i >= 0; i--)
        {
            var b = _rising[i];
            if (b.At > Clock) continue;
            // un budget par image, pour épargner la réserve de gouttes : une gorgée
            // retenue arrive une image ou deux plus tard, ce que personne ne voit
            if (fired >= 6) break;
            fired++;
            _rising[i] = _rising[^1];
            _rising.RemoveAt(_rising.Count - 1);

            var at = new Vec3d(b.X, ocean.Sample(b.X, b.Z, t), b.Z);
            /* Le dôme que la poche soulève fait environ deux fois son propre
               volume d'eau — la calotte traîne un sillage derrière elle —, jeté à
               sa vitesse de montée amplifiée par la détente du dernier mètre : une
               poussée et un crachat déchiré, pas une colonne. Son dernier souffle
               n'est pas une poche qui monte mais tout le haut qui vente d'un coup,
               donc jeté plus fort et plus haut. */
            if (b.Big) OnBurst?.Invoke(at, b.V * 2, 4 + 3 * b.Rise, 1.8);
            else OnBurst?.Invoke(at, b.V * 2, 2.5 + 3 * b.Rise, 1);
            /* La tache qu'il crève est large comme le PANACHE, pas comme la poche :
               l'air d'une épave profonde arrive étalé sur plusieurs mètres. Trois
               secondes, parce qu'un bouillon enfle et s'étale avant que l'eau se
               referme. */
            _boils.Add(new Bubbling
            {
                X = b.X, Z = b.Z, R = Math.Max(2.5, 1.3 * b.Spread) + 2.5 * b.R,
                Born = Clock, Life = b.Big ? 6.0 : 3.0
            });
        }

        // --- 3. les bouillons à poser dans l'écume cette image, le plus fort d'abord ---
        _list.Clear();
        for (int i = _boils.Count - 1; i >= 0; i--)
        {
            var bo = _boils[i];
            double u = (Clock - bo.Born) / bo.Life;
            if (u >= 1) { _boils[i] = _boils[^1]; _boils.RemoveAt(_boils.Count - 1); continue; }
            // en grand d'un coup quand le dôme crève, puis qui s'étale et s'amincit
            bo.W = Math.Min(1, u * 8) * (1 - u * u);
            _list.Add(new Boil(bo.X, bo.Z, bo.R * (0.7 + 0.6 * u), bo.W));
        }
        _list.Sort((p, q) => q.W.CompareTo(p.W));
    }

    /* SON DERNIER SOUFFLE, quand le dernier de ses hauts passe sous l'eau.
       Du théâtre, demandé comme tel — et qui se trouve avoir une vraie cause,
       seule raison pour laquelle il est permis ici : l'air de ses châteaux et
       sous son pont n'a nulle part où aller tant qu'il en reste dehors ; à
       l'instant où le plus haut est noyé, tout part d'un coup. Ce n'est donc pas
       une gerbe posée par-dessus : c'est la poche que le solveur porte déjà,
       lâchée en grande partie d'un coup au lieu de son exponentielle lente, à
       l'endroit qui a disparu en dernier.

       Lu sur sa coque VISIBLE, pas sur celle du solveur : le galion pirate a un
       modèle trois mètres plus haut que ses lignes, et le pont du solveur était
       sous l'eau bien avant que l'œil voie quoi que ce soit disparaître. */
    void LastBreath(WreckHull h, Ocean ocean, double t)
    {
        var ph = h.Physics;
        if (!ph.Foundered) { _breath.Remove(ph); return; }
        if (_breath.TryGetValue(ph, out bool done) && done) return;
        _breath[ph] = false;

        var q = ph.Body.Quat;
        var P = ph.Body.Pos;
        int n = h.Shell != null ? h.Shell.Length : ph.Comps.Length;
        double high = double.NegativeInfinity;
        bool above = false;
        Vec3d last = P;
        for (int i = 0; i < n; i++)
        {
            Vec3d top = h.Shell != null
                ? new Vec3d(0, h.Shell[i].Deck, 0.5 * (h.Shell[i].Z0 + h.Shell[i].Z1))
                : new Vec3d(0, ph.Comps[i].DeckY, ph.Comps[i].Mid.Z);
            top = q.Rotate(top) + P;
            double over = top.Y - ocean.Sample(top.X, top.Z, t);
            if (over > -0.3) above = true;                  // une largeur de main encore dehors
            if (top.Y > high) { high = top.Y; last = top; }
        }
        if (above) return;
        _breath[ph] = true;

        // le plus gros de ce qui est enfermé, et jamais moins qu'une gorgée qui vaille
        double V = Math.Max(6, 0.6 * ph.TrappedAir);
        ph.TrappedAir = Math.Max(0, ph.TrappedAir - V);

        /* Une grande poussée là où elle a disparu, puis deux plus petites le long
           d'elle un instant après : une seule gerbe se lit comme une chose qui
           crève la surface, un décalage se lit comme un navire qui lâche tout. */
        Vec3d f = q.Rotate(new Vec3d(0, 0, 1));
        double fl = Math.Sqrt(f.X * f.X + f.Z * f.Z);
        double fx = fl > 1e-9 ? f.X / fl : 0, fz = fl > 1e-9 ? f.Z / fl : 1;
        double L = ph.Spec.L;
        (double Off, double Share, double Delay)[] shots = { (0, 0.62, 0), (-0.25, 0.22, 0.20), (0.30, 0.16, 0.45) };
        foreach (var (off, share, delay) in shots)
        {
            double v = V * share, r = Math.Cbrt(3 * v / (4 * Math.PI));
            double rise0 = 0.71 * Math.Sqrt(9.81 * r);
            _rising.Add(new Slug
            {
                X = last.X + fx * off * L, Z = last.Z + fz * off * L,
                V = v, R = r, Rise = rise0, Spread = 0.25 * L,
                At = Clock + delay, Big = true
            });
            OnSlug?.Invoke(new Vec3d(last.X + fx * off * L, last.Y - 1.0, last.Z + fz * off * L), v, rise0, delay);
        }
        OnLastBreath?.Invoke(last, V);
    }

    /// <summary>L'origine flottante : tout ce qui est tenu ici est en repère local.</summary>
    public void Rebase(double dx, double dz)
    {
        foreach (var b in _rising) { b.X -= dx; b.Z -= dz; }
        foreach (var b in _boils) { b.X -= dx; b.Z -= dz; }
    }
}
