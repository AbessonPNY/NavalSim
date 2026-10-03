using System;
using System.Collections.Generic;

namespace NavalSim.Core;

/// <summary>
/// LES FILINS À CROCHET — ce qui tient deux coques ensemble pendant un abordage.
///
/// Avant eux, l'abordage se SUBISSAIT : le pirate se rangeait à couple, cinq
/// secondes passaient, et le navire était pillé. Rien à faire entre les deux. Les
/// filins en font une lutte — il lance ses crochets, les bouts halent les deux
/// coques l'une contre l'autre, et l'on peut les couper. Tous coupés, il doit
/// relancer, et la proie a le temps de faire servir.
///
/// UNE CONTRAINTE, PAS UN RESSORT. C'est la décision qui gouverne tout le
/// fichier. Un ressort entre deux coques de trois cents tonnes demande une raideur
/// énorme pour ne pas s'étirer, et une raideur énorme au pas de temps du solveur
/// diverge au premier choc — la coque part à l'infini, ce qui est exactement la
/// panne qu'un abordage ne doit jamais produire. Une contrainte, elle, ne peut
/// pas diverger : elle RETIRE la vitesse d'écartement au lieu d'ajouter une
/// force, et la même correction appliquée deux fois ne fait rien la seconde. Même
/// raison que la chaîne de Verlet des bouts rompus.
///
/// UN BOUT NE POUSSE PAS. Il ne fait rien tant qu'il a du mou, et tire seulement
/// quand il est raide. C'est ce qui le distingue d'une barre, et ce qui permet
/// aux deux coques de rouler l'une contre l'autre sans se repousser.
///
/// ET L'ÉQUIPAGE HALE. Un crochet qui mord ne fait pas que retenir : les hommes
/// tirent dessus, donc la longueur du bout DIMINUE, et les coques se rapprochent
/// d'elles-mêmes. C'est cela qui fait qu'un abordage aboutit au lieu de traîner —
/// et c'est ce qu'il faut couper pour s'en défaire.
/// </summary>
public sealed class Grapple
{
    /// <summary>Combien de crochets un pirate lance d'un coup.</summary>
    public const int Volee = 4;
    /// <summary>À quelle distance de bord à bord il les lance, en mètres.</summary>
    public const double Portee = 26;
    /// <summary>Le temps de vol d'un crochet, avant qu'il morde ou retombe.</summary>
    public const double Vol = 0.8;
    /// <summary>Ce que l'équipage hale, en mètres par seconde.</summary>
    public const double Hale = 0.55;
    /// <summary>
    /// LA MARGE QUE LE HALAGE LAISSE ENTRE LES DEUX BORDÉS, en mètres.
    ///
    /// La longueur minimale n'est PAS une constante : c'est la somme des demi-baux
    /// plus cette marge. Écrite en constante (2,5 m), elle halait deux frégates de
    /// 7,25 m de bau jusqu'à 2,9 m de centre à centre — c'est-à-dire l'une DANS
    /// l'autre. Il n'y a pas de collision coque contre coque dans ce jeu : rien ne
    /// les séparait, la géométrie des ancres finissait par se retourner, et la
    /// paire partait à 43 m/s. Mesuré au banc avant d'y toucher.
    ///
    /// Le bout est donc ce qui les arrête, et il doit s'arrêter où le bois commence.
    ///
    /// ET LE PLANCHER SE CALCULE PAR BOUT, pas par paire : un filin tendu de
    /// l'étrave de l'un à la poupe de l'autre est OBLIQUE. Le raccourcir jusqu'à la
    /// somme des demi-baux le tire en diagonale, et les deux coques se rentrent
    /// dedans par le travers pendant que le bout, lui, se croit satisfait — mesuré :
    /// 4,4 m entre deux centres de frégates larges de 7,25. Le plancher est donc
    /// l'hypoténuse : les baux en travers, l'écart des ancres en long.
    /// </summary>
    public const double Marge = 0.6;
    /// <summary>Au-delà, le bout casse — on ne retient pas un trois-mâts au chanvre.</summary>
    public const double Rupture = 34;

    /// <summary>Un filin lancé. Tant que <see cref="Fly"/> court, il vole.</summary>
    public sealed class Line
    {
        /// <summary>Qui l'a lancé, et sur qui.</summary>
        public ShipPhysics From = null!, To = null!;
        /// <summary>Ses deux bouts, en repère LOCAL de chaque coque — ils suivent donc le roulis.</summary>
        public Vec3d A, B;
        /// <summary>La longueur du bout, que l'équipage raccourcit.</summary>
        public double Len;
        /// <summary>Jusqu'où l'équipage peut haler : bord à bord, jamais au travers.</summary>
        public double Min;
        /// <summary>Ce qu'il reste de vol. Zéro : il a mordu.</summary>
        public double Fly;
        /// <summary>Faux : il retombera à l'eau sans rien prendre.</summary>
        public bool Bites;
        /// <summary>Ce qu'il tire en ce moment, en newtons — pour le son et l'épaisseur du trait.</summary>
        public double Strain;
    }

    public readonly List<Line> Lines = new();

    /// <summary>Combien de filins MORDUS tiennent en ce moment.</summary>
    public int Held
    {
        get { int n = 0; foreach (var l in Lines) if (l.Fly <= 0 && l.Bites) n++; return n; }
    }

    /// <summary>Tient-il celle-ci ?</summary>
    public bool Holds(ShipPhysics prey)
    {
        foreach (var l in Lines) if (l.To == prey && l.Fly <= 0 && l.Bites) return true;
        return false;
    }

    /// <summary>
    /// LANCER UNE VOLÉE. Chaque crochet part d'un point du pavois du lanceur vers
    /// un point du pavois de la proie, tirés au hasard le long des deux coques —
    /// des bouts parallèles se liraient comme une machine, pas comme quatre
    /// hommes qui lancent.
    ///
    /// TOUS NE MORDENT PAS, et c'est ce qui rend la volée vivante : plus les
    /// coques sont loin ou l'allure relative vive, moins il en prend. Un crochet
    /// manqué vole quand même et retombe — on le voit partir pour rien, ce qui
    /// vaut mieux qu'un crochet qui n'existe pas.
    /// </summary>
    public void Throw(ShipPhysics from, ShipPhysics to, Random rng)
    {
        var a = from.Body; var b = to.Body;
        double gap = MathX.Hyp(b.Pos.X - a.Pos.X, b.Pos.Z - a.Pos.Z) - (from.Spec.B + to.Spec.B) * 0.5;
        double vrel = MathX.Hyp(a.Vel.X - b.Vel.X, a.Vel.Z - b.Vel.Z);
        /* CE QUI DÉCIDE : la distance d'abord, l'allure ensuite. Un crochet se
           lance à quinze mètres sans peine et à vingt-cinq à la limite ; et sur une
           coque qui file, il rate même à cinq. */
        double chance = Math.Clamp(1.15 - gap / Portee, 0.1, 0.95)
                      * Math.Clamp(1.25 - vrel / 3.5, 0.1, 1.0);

        // le bord de la proie qui regarde le lanceur : on ne lance pas par-dessus elle
        int side = Facing(from, to);
        for (int i = 0; i < Volee; i++)
        {
            double fa = rng.NextDouble() * 0.7 - 0.35;         // le long du lanceur
            double fb = rng.NextDouble() * 0.7 - 0.35;         // et de la proie
            Lines.Add(new Line
            {
                From = from, To = to,
                A = new Vec3d(-side * from.Spec.B * 0.5, from.Spec.Hull.FreeboardMid, fa * from.Spec.L),
                B = new Vec3d(side * to.Spec.B * 0.5, to.Spec.Hull.FreeboardMid, fb * to.Spec.L),
                Min = Plancher(from, to, fa * from.Spec.L, fb * to.Spec.L),
                Len = Math.Max(Plancher(from, to, fa * from.Spec.L, fb * to.Spec.L),
                               gap + (from.Spec.B + to.Spec.B) * 0.5),
                Fly = Vol,
                Bites = rng.NextDouble() < chance
            });
        }
    }

    /// <summary>
    /// LA PLUS COURTE LONGUEUR QU'UN BOUT PUISSE PRENDRE : celle qu'il aurait si
    /// les deux coques étaient bord à bord, parallèles et jointives. En travers,
    /// c'est la somme des demi-baux ; en long, l'écart des deux points d'accroche.
    /// Le bout étant la diagonale des deux, c'est l'hypoténuse.
    /// </summary>
    static double Plancher(ShipPhysics from, ShipPhysics to, double za, double zb)
    {
        double travers = (from.Spec.B + to.Spec.B) * 0.5 + Marge;
        double lon = za - zb;
        return Math.Sqrt(travers * travers + lon * lon);
    }

    /// <summary>
    /// De quel côté du LANCEUR la proie se trouve : +1 tribord, −1 bâbord. Tribord
    /// est le −x local, comme partout dans ce projet.
    /// </summary>
    static int Facing(ShipPhysics from, ShipPhysics to)
    {
        Vec3d d = to.Body.Pos - from.Body.Pos;
        Vec3d stb = from.Body.Quat.Rotate(new Vec3d(-1, 0, 0));
        return d.X * stb.X + d.Z * stb.Z >= 0 ? 1 : -1;
    }

    /// <summary>
    /// UNE IMAGE DE FILINS : le vol, le halage, et la contrainte.
    ///
    /// Appelée APRÈS que les deux coques ont été intégrées. Une contrainte corrige
    /// ce que l'intégration vient de faire ; la poser avant reviendrait à corriger
    /// l'image d'avant, et le bout paraîtrait élastique d'un pas de temps.
    /// </summary>
    public void Step(double dt)
    {
        for (int i = Lines.Count - 1; i >= 0; i--)
        {
            var l = Lines[i];
            if (l.Fly > 0)
            {
                l.Fly -= dt;
                if (l.Fly <= 0 && !l.Bites) { Lines.RemoveAt(i); continue; }   // il retombe à l'eau
                continue;
            }

            Vec3d pa = l.From.Body.Pos + l.From.Body.Quat.Rotate(l.A);
            Vec3d pb = l.To.Body.Pos + l.To.Body.Quat.Rotate(l.B);
            Vec3d d = pb - pa;
            double dist = Math.Sqrt(d.X * d.X + d.Y * d.Y + d.Z * d.Z);

            // ET IL CASSE. Un bout de chanvre ne retient pas une coque qui s'arrache.
            if (dist > Rupture) { Lines.RemoveAt(i); continue; }

            // l'équipage hale : c'est ce qui rapproche, et ce qu'il faut couper
            l.Len = Math.Max(l.Min, l.Len - Hale * dt);

            if (dist <= l.Len || dist < 1e-6) { l.Strain = 0; continue; }   // du mou : un bout ne pousse pas

            Vec3d n = d * (1.0 / dist);
            Vec3d ra = pa - Cog(l.From), rb = pb - Cog(l.To);
            Vec3d va = l.From.Body.Vel + l.From.Body.AngVel.Cross(ra);
            Vec3d vb = l.To.Body.Vel + l.To.Body.AngVel.Cross(rb);
            double vn = (vb - va).Dot(n);

            /* LA CORRECTION DE POSITION PASSE PAR LA VITESSE (Baumgarte) et non par
               un déplacement : déplacer une coque de trois cents tonnes à la main,
               c'est lui donner une énergie que rien n'a produite, et elle ressort
               en tremblement. On ajoute donc juste ce qu'il faut de vitesse pour
               que l'écart se résorbe en quelques images. */
            double biais = (dist - l.Len) * 0.30 / Math.Max(1e-4, dt);
            double k = Eff(l.From, ra, n) + Eff(l.To, rb, n);
            double j = -(vn + biais) / Math.Max(1e-9, k);
            if (j > 0) { l.Strain = 0; continue; }        // il ne pousse jamais

            l.Strain = -j / Math.Max(1e-4, dt);
            Impulse(l.From, ra, n * -j);
            Impulse(l.To, rb, n * j);
        }
    }

    /// <summary>
    /// COUPER UN FILIN — le plus RAIDE, parce que c'est celui qu'on voit et celui
    /// qui tire. Couper au hasard donnerait l'impression que la hache manque.
    /// Rend faux s'il n'y avait rien à couper.
    /// </summary>
    public bool Cut(ShipPhysics prey)
    {
        int best = -1; double worst = -1;
        for (int i = 0; i < Lines.Count; i++)
        {
            var l = Lines[i];
            if (l.To != prey || l.Fly > 0 || !l.Bites) continue;
            if (l.Strain >= worst) { worst = l.Strain; best = i; }
        }
        if (best < 0) return false;
        Lines.RemoveAt(best);
        return true;
    }

    /// <summary>Tout lâcher — le pirate s'en va, la proie a coulé, la partie reprend.</summary>
    public void Drop(ShipPhysics who)
    {
        Lines.RemoveAll(l => l.From == who || l.To == who);
    }

    public void Clear() => Lines.Clear();

    // ---- l'arithmétique du corps rigide, dans la convention du solveur ----

    static Vec3d Cog(ShipPhysics s) => s.Body.Quat.Rotate(s.Body.Com) + s.Body.Pos;

    /// <summary>
    /// La masse EFFECTIVE au point <paramref name="r"/> dans la direction
    /// <paramref name="n"/> : ce qu'il faut d'impulsion pour un mètre par seconde.
    /// Elle tient compte de la rotation — une traction à l'étrave fait pivoter la
    /// coque bien plus qu'elle ne la déplace, et l'ignorer donnerait des filins qui
    /// tirent trop fort par les bouts.
    /// </summary>
    static double Eff(ShipPhysics s, Vec3d r, Vec3d n)
    {
        var b = s.Body;
        Vec3d rn = r.Cross(n);
        Vec3d bb = b.Quat.Inverted().Rotate(rn);
        bb = new Vec3d(bb.X / b.Ib.X, bb.Y / b.Ib.Y, bb.Z / b.Ib.Z);
        return 1.0 / b.Mass + b.Quat.Rotate(bb).Cross(r).Dot(n);
    }

    /// <summary>
    /// Une impulsion au point <paramref name="r"/>. L'angulaire passe par le repère
    /// PROPRE, où l'inertie est diagonale — c'est la convention du solveur, et en
    /// prendre une autre ferait tourner la coque autour du mauvais axe.
    /// </summary>
    static void Impulse(ShipPhysics s, Vec3d r, Vec3d J)
    {
        var b = s.Body;
        b.Vel += J * (1.0 / b.Mass);
        Vec3d t = r.Cross(J);
        Vec3d tb = b.Quat.Inverted().Rotate(t);
        tb = new Vec3d(tb.X / b.Ib.X, tb.Y / b.Ib.Y, tb.Z / b.Ib.Z);
        b.AngVel += b.Quat.Rotate(tb);
    }

}
