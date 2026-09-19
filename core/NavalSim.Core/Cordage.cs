namespace NavalSim.Core;

/// <summary>
/// LES BOUTS ROMPUS — la simulation de cordage.js, sans Godot : les bouts qui
/// pendent d'un mât touché, pour qu'un mât blessé ne ressemble plus à un mât
/// intact jusqu'au coup qui l'abat.
///
/// UNE CORDE EST UNE CHAÎNE DE VERLET, pas une animation : des points, une
/// contrainte de distance entre voisins, et rien d'autre — ni angle, ni rotation,
/// ni force à intégrer, rien qui puisse diverger. Mesuré dans la page : cent
/// soixante cordes, une flotte entière démâtée, coûtent un dixième de
/// milliseconde. La simulation n'est pas la facture.
///
/// Le point d'attache n'est pas simulé, il est LU : le moteur écrit chaque image
/// dans <see cref="Rope.Hx"/> où est le point du mât qui le porte, et la hauteur
/// de la mer dessous dans <see cref="Rope.Sea"/>. Tout en mètres locaux.
/// </summary>
public sealed class Cordage
{
    /* Dix points suffisent pour un bout de six mètres : ce que l'œil lit, c'est
       le BALANCEMENT — une corde en retard sur son roulis —, pas la chaînette. */
    public const int P = 10;
    public const int Max = 96;                 // cordes à la fois, pour toute la flotte
    const double H = 1.0 / 120;

    public readonly float[] Px = new float[Max * P], Py = new float[Max * P], Pz = new float[Max * P];
    readonly float[] _ox = new float[Max * P], _oy = new float[Max * P], _oz = new float[Max * P];

    public sealed class Rope
    {
        public bool On;
        public double Seg;
        public double Hx, Hy, Hz;              // le point d'attache, relu à chaque image
        public double Sea = -50;               // la mer sous elle, relue une fois par image
        public object? Tag;                    // ce que le moteur y attache (le navire, le nœud qui la porte)
    }

    public readonly Rope[] Ropes = new Rope[Max];
    /// <summary>Le nombre de places en service (vivantes ou non) au début de la réserve.</summary>
    public int N { get; private set; }
    int _cursor;
    double _acc;
    readonly Random _rng;

    public Cordage(Random? rng = null)
    {
        _rng = rng ?? new Random();
        for (int i = 0; i < Max; i++) Ropes[i] = new Rope();
    }

    int Slot()
    {
        for (int k = 0; k < Max; k++)
        {
            int i = (_cursor + k) % Max;
            if (Ropes[i].On) continue;
            _cursor = (i + 1) % Max;
            if (i >= N) N = i + 1;
            return i;
        }
        return -1;              // pleine : elle traîne moins de bouts, jamais une file
    }

    /// <summary>Une corde de <paramref name="len"/> mètres pendue en <paramref name="anchor"/>. Rend sa place, ou −1.</summary>
    public int Hang(Vec3d anchor, double len, object? tag)
    {
        int s = Slot();
        if (s < 0) return -1;
        int b = s * P;
        double seg = len / (P - 1);
        /* Un soupçon hors de l'axe, sans quoi la première passe de contraintes n'a
           rien contre quoi pousser et la corde se dresse comme un fil de fer. */
        double jx = (_rng.NextDouble() - 0.5) * 0.4, jz = (_rng.NextDouble() - 0.5) * 0.4;
        for (int j = 0; j < P; j++)
        {
            int i = b + j;
            Px[i] = _ox[i] = (float)(anchor.X + jx * j / P);
            Py[i] = _oy[i] = (float)(anchor.Y - seg * j);
            Pz[i] = _oz[i] = (float)(anchor.Z + jz * j / P);
        }
        var r = Ropes[s];
        r.On = true; r.Seg = seg; r.Sea = -50; r.Tag = tag;
        r.Hx = anchor.X; r.Hy = anchor.Y; r.Hz = anchor.Z;
        return s;
    }

    public void Drop(int s) => Ropes[s].On = false;

    public void Clear()
    {
        foreach (var r in Ropes) r.On = false;
        N = 0;
    }

    /// <summary>Ramener N à la dernière place vivante — plus rien à parcourir au-delà.</summary>
    public void Trim()
    {
        int top = 0;
        for (int s = 0; s < N; s++) if (Ropes[s].On) top = s + 1;
        N = top;
    }

    public void Rebase(double dx, double dz)
    {
        for (int s = 0; s < N; s++)
        {
            if (!Ropes[s].On) continue;
            int b = s * P;
            for (int j = 0; j < P; j++)
            {
                Px[b + j] -= (float)dx; _ox[b + j] -= (float)dx;
                Pz[b + j] -= (float)dz; _oz[b + j] -= (float)dz;
            }
            var r = Ropes[s];
            r.Hx -= dx; r.Hz -= dz;
        }
    }

    /// <summary>
    /// Faire avancer les cordes de <paramref name="dt"/>, les attaches et la mer
    /// déjà relues. <paramref name="windX"/>, <paramref name="windZ"/> : le vent VRAI.
    /// </summary>
    public void Step(double dt, double windX, double windZ)
    {
        /* UN PAS FIXE, et il compte ici plus que presque partout : Verlet à pas
           variable ne perd pas seulement en justesse, il change l'amortissement.
           Le plafond suit l'image — à seize fois la vitesse, une image porte
           légitimement un quart de seconde de mer —, et s'arrête à un tiers de
           seconde : au-delà le gréement devient approximatif plutôt que cher, et
           c'est toujours le garde-fou contre une image gelée. */
        double ceiling = Math.Min(Math.Max(5 * H, dt), 1.0 / 3);
        _acc = Math.Min(_acc + Math.Max(0, dt), ceiling);
        double hh = H * H;

        while (_acc >= H)
        {
            _acc -= H;
            for (int s = 0; s < N; s++)
            {
                var r = Ropes[s];
                if (!r.On) continue;
                int b = s * P;

                for (int j = 1; j < P; j++)
                {
                    int i = b + j;
                    double vx = (Px[i] - _ox[i]) / H, vy = (Py[i] - _oy[i]) / H, vz = (Pz[i] - _oz[i]) / H;
                    /* La traînée de l'air vers le vent VRAI, qui porte un bout coupé
                       sous le vent — et celle de l'eau vers rien du tout, qui fait
                       traîner une corde dans la mer. Le chanvre est lourd : ni l'une
                       ni l'autre n'est forte, l'essentiel du mouvement d'une corde est
                       celui de son attache. */
                    bool wet = Py[i] < r.Sea;
                    double cA = wet ? 7.0 : 0.28;
                    double ax = cA * ((wet ? 0 : windX) - vx);
                    double az = cA * ((wet ? 0 : windZ) - vz);
                    double ay = cA * (-vy) + (wet ? 4.2 : 0) - 9.81;

                    double nx = Px[i] + (Px[i] - _ox[i]) + ax * hh;
                    double ny = Py[i] + (Py[i] - _oy[i]) + ay * hh;
                    double nz = Pz[i] + (Pz[i] - _oz[i]) + az * hh;
                    _ox[i] = Px[i]; _oy[i] = Py[i]; _oz[i] = Pz[i];
                    Px[i] = (float)nx;
                    Py[i] = (float)Math.Max(r.Sea - 1.1, ny);   // à fleur d'eau, pas coulée
                    Pz[i] = (float)nz;
                }

                // l'attache n'est pas simulée, elle est LUE : c'est un point de son mât
                Px[b] = _ox[b] = (float)r.Hx; Py[b] = _oy[b] = (float)r.Hy; Pz[b] = _oz[b] = (float)r.Hz;

                for (int k = 0; k < 3; k++)
                    for (int j = 0; j < P - 1; j++)
                    {
                        int a = b + j, c = a + 1;
                        double dx = Px[c] - Px[a], dy = Py[c] - Py[a], dz = Pz[c] - Pz[a];
                        double d = Math.Sqrt(dx * dx + dy * dy + dz * dz);
                        if (d == 0) d = 1e-6;
                        double f = (d - r.Seg) / d * 0.5;
                        dx *= f; dy *= f; dz *= f;
                        if (j > 0) { Px[a] += (float)dx; Py[a] += (float)dy; Pz[a] += (float)dz; }
                        else { dx *= 2; dy *= 2; dz *= 2; }      // l'attache n'en prend rien
                        Px[c] -= (float)dx; Py[c] -= (float)dy; Pz[c] -= (float)dz;
                    }
            }
        }
    }
}
