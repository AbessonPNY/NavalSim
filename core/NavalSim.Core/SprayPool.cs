using System;

namespace NavalSim.Core;

/// <summary>
/// L'EAU JETÉE — la réserve de gouttes de splash.js.
///
/// Quelque chose entre dans la mer et la mer doit aller quelque part. Une gerbe
/// n'est pas un effet collé sur un événement : c'est le volume d'eau que l'objet
/// vient de déplacer, qui arrive ailleurs. D'où ses deux nombres, qui font deux
/// métiers différents : COMBIEN d'eau — des mètres cubes, le nombre de gouttes,
/// la largeur de la nappe, le temps qu'elle pend —, et À QUELLE VITESSE elle est
/// entrée, qui fixe la HAUTEUR et rien d'autre.
///
/// Sans Godot : le moteur ne fait que dessiner <see cref="Pos"/>,
/// <see cref="Size"/> et <see cref="Alpha"/> sur <see cref="Count"/> gouttes.
/// </summary>
public sealed class SprayPool
{
    /* Quatre mille, pas deux. La réserve est un BUDGET et non un fait de l'eau, et
       un événement en demande bien plus que les autres : une soute qui saute jette
       plus de soixante planches qui retombent en quelques secondes, chacune avec
       sa colonne. À deux mille la réserve restait pleine toute l'averse. */
    public const int Max = 4000;

    struct Drop
    {
        public Vec3d P, V;
        public double S0, S1, T, Life, Y0, K;
        public bool On;
    }

    readonly Drop[] _live = new Drop[Max];
    int _n, _cursor;
    readonly Random _rng;

    /// <summary>Ce qui est à dessiner cette image : position, taille, opacité.</summary>
    public readonly float[] Pos = new float[Max * 3], Size = new float[Max], Alpha = new float[Max];
    public int Count { get; private set; }

    public SprayPool(Random? rng = null) { _rng = rng ?? new Random(); }

    /// <summary>Ce que l'embrun ne traverse pas : les coques à flot (HullCollider).</summary>
    public readonly System.Collections.Generic.List<ISprayCollider> Colliders = new();

    /* Un curseur tournant plutôt qu'une recherche depuis zéro : une grosse gerbe
       demande deux cent quarante places d'un coup, et repartir du haut à chaque
       fois en faisait un quart de million de comparaisons dans une image. */
    int Slot()
    {
        for (int k = 0; k < Max; k++)
        {
            int i = (_cursor + k) % Max;
            if (!_live[i].On)
            {
                _cursor = (i + 1) % Max;
                if (i >= _n) _n = i + 1;
                return i;
            }
        }
        return -1;          // pleine : la gerbe est simplement plus petite, jamais mise en attente
    }

    /// <summary>
    /// <paramref name="water"/> en mètres cubes jetés, <paramref name="speed"/> la
    /// vitesse de rencontre en m/s, <paramref name="at"/> dans le repère local de
    /// tout le reste. <paramref name="jet"/> relève le plafond de hauteur pour le
    /// seul cas que ce plafond ne décrit pas — un petit corps très rapide ; 1
    /// n'y change rien.
    /// </summary>
    public void Burst(Vec3d at, double water, double speed, double jet = 1)
    {
        water = Math.Max(0, water);
        speed = Math.Max(0, speed);
        if (water < 0.02 || speed < 0.4) return;

        // la taille de l'événement, en mètres : une racine cubique, c'est un volume
        double R = Math.Pow(water, 1.0 / 3);

        /* LA SIMILITUDE DE FROUDE. La gravité fixe la seule horloge qu'ait une
           gerbe : de l'eau jetée à quatre mètres par seconde monte et retombe en
           huit dixièmes de seconde, à côté de quoi que ce soit — et à côté d'une
           frégate de soixante mètres c'est un éclair, que l'œil lit comme
           « petit ». Pour un écoulement gouverné par la gravité, la vitesse va
           comme la racine de la longueur : la gerbe monte en proportion de R et
           pend en proportion de √R. Une grande gerbe est une gerbe LENTE. */
        const double RREF = 2.5;
        double froude = Math.Sqrt(Math.Max(0.35, R / RREF));
        /* Puis tenue à ce qu'une cavité de cette taille peut jeter : la couronne
           monte à peu près aussi haut que la cavité est large — c'est la même eau,
           repliée. Sauf le petit corps très rapide, qui tire un jet de Worthington
           bien plus haut que sa cavité : l'appelant relève alors le plafond. */
        double j = Math.Max(1, jet);
        double vMax = Math.Sqrt(2 * 9.81 * 1.05 * R) * j;
        double v0 = Math.Min(vMax, (0.6 + speed * 0.85) * froude);
        // et la FORME suit le même nombre : une cavité étroite tire droit, une colonne
        double tight = Math.Min(1, Math.Max(0, (j - 1) / 2.0));

        // beaucoup de petites plutôt que peu de grosses : l'embrun se lit à son grain
        int count = Math.Max(8, Math.Min(420, (int)Math.Round(water * 11, MidpointRounding.AwayFromZero)));
        for (int i = 0; i < count; i++)
        {
            int s = Slot();
            if (s < 0) break;
            ref Drop d = ref _live[s];

            /* Une COURONNE, pas une fontaine : l'eau quitte le bord de la cavité,
               elle monte en biais vers l'extérieur et le milieu reste plutôt vide.
               Tout droit, c'est un tuyau d'arrosage. */
            bool sheet = i < count * (0.16 - 0.07 * tight);
            double a = _rng.NextDouble() * Math.PI * 2;
            double e = sheet ? (0.15 + _rng.NextDouble() * 0.45)
                             : (0.55 + 0.62 * tight + _rng.NextDouble() * (0.85 - 0.48 * tight));
            double sp = v0 * (sheet ? (0.20 + _rng.NextDouble() * 0.35) * (1 - 0.45 * tight)
                                    : 0.35 + _rng.NextDouble() * 0.95);
            // et une bouche plus petite, qui est la cavité étroite
            double mouth = R * (0.5 - 0.30 * tight);
            d.P = new Vec3d(at.X + Math.Cos(a) * mouth * _rng.NextDouble(),
                            at.Y + _rng.NextDouble() * R * 0.3,
                            at.Z + Math.Sin(a) * mouth * _rng.NextDouble());
            d.V = new Vec3d(Math.Cos(a) * Math.Cos(e) * sp, Math.Sin(e) * sp, Math.Sin(a) * Math.Cos(e) * sp);

            /* Un sprite n'est pas une goutte, c'est un PAQUET d'embrun — de l'eau
               déchirée et l'air qu'elle tient. Borné en mètres : de l'eau déchirée
               ne tient pas ensemble au-delà d'un demi-mètre, quoi qui l'ait jetée.
               La nappe est le rideau blanc au pied, lent, large et vite parti. */
            d.S0 = sheet ? Math.Min(2.2, R * 0.34) : Math.Min(0.45, R * (0.06 + _rng.NextDouble() * 0.10));
            d.S1 = sheet ? Math.Min(6.0, R * 1.00) : d.S0 * 1.9;
            // et elle pend √R plus longtemps : la même similitude
            d.Life = (sheet ? 0.35 + _rng.NextDouble() * 0.3 : 0.55 + _rng.NextDouble() * 0.75) * froude;
            d.T = 0;
            // la surface échantillonnée UNE fois, ici : trois cents gouttes interrogeant la mer coûteraient plus que le solveur
            d.Y0 = at.Y;
            /* L'air retient fort une petite goutte et à peine une grosse : la traînée
               va avec la surface, la masse avec le volume. Sans cela une grande nappe
               s'éteint aussi vite qu'une gouttelette — l'air de maquette encore. */
            d.K = 1.1 * Math.Min(2.4, 0.35 / Math.Max(0.05, d.S0));
            d.On = true;
        }
    }

    /// <summary>
    /// Toute goutte vivante tient une position locale : elle glisse avec le monde.
    /// Oubliée, un recentrage laisserait l'embrun pendu un kilomètre et demi derrière.
    /// </summary>
    public void Rebase(double dx, double dz)
    {
        for (int i = 0; i < _n; i++)
            if (_live[i].On) _live[i].P = new Vec3d(_live[i].P.X - dx, _live[i].P.Y, _live[i].P.Z - dz);
    }

    public void Update(double dt)
    {
        if (dt <= 0) return;
        int w = 0, top = 0;
        for (int i = 0; i < _n; i++)
        {
            ref Drop d = ref _live[i];
            if (!d.On) continue;
            d.T += dt;
            double u = d.T / d.Life;
            // partie quand elle est retombée d'où elle venait, ou quand son temps est fini
            if (u >= 1 || (d.V.Y < 0 && d.P.Y < d.Y0 - 0.35)) { d.On = false; continue; }

            d.V = new Vec3d(d.V.X, d.V.Y - 9.81 * dt, d.V.Z) * Math.Max(0, 1 - d.K * dt);
            d.P = d.P + d.V * dt;
            // renvoyée par une coque, ou restée sur son pont
            bool alive = true;
            foreach (var c in Colliders)
                if (!c.Collide(ref d.P, ref d.V)) { alive = false; break; }
            if (!alive) { d.On = false; continue; }

            Pos[w * 3] = (float)d.P.X; Pos[w * 3 + 1] = (float)d.P.Y; Pos[w * 3 + 2] = (float)d.P.Z;
            Size[w] = (float)(d.S0 + (d.S1 - d.S0) * Math.Pow(u, 0.6));
            /* Monte vite, part lentement : l'embrun paraît d'un coup et s'amincit en
               retombant. Bien sous un, à dessein : une gerbe est des dizaines de ces
               paquets superposés, et à pleine opacité ils s'empileraient en un bloc
               blanc au lieu de bâtir quelque chose à travers quoi l'on voit. */
            Alpha[w] = (float)(Math.Min(1, u * 12) * Math.Pow(1 - u, 0.9) * 0.45);
            w++;
            top = i + 1;
        }
        _n = top;
        Count = w;
    }
}
