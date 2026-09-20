using System;

namespace NavalSim.Core;

/// <summary>
/// Comment une voile est taillée : où elle est la plus creuse sur chaque axe,
/// quels bords sont LACÉS à un espar ou un étai, sa guindant et sa rondeur.
/// Les défauts sont l'ancienne bulle, si bien qu'une voile qui ne dit rien est
/// formée exactement comme avant — l'objet `cut` de <c>_sailSurface</c>.
/// </summary>
public sealed class SailCut
{
    public string Kind = "";
    public double UPeak = 0.5, VPeak = 0.5;
    public bool UPin0 = true, UPin1 = true, VPin0 = true, VPin1 = true;
    public double Free = 0.78;
    public double? FreeU;
    public bool? HangU0, HangU1, HangV0, HangV1;
    public double Crown = 1;
    public double Bow, RoachFoot;

    /// <summary>La voile carrée, sous sa vergue : ce que _squareRig et _rigModel passent tous deux.</summary>
    public static SailCut Square() => new()
    {
        Kind = "square", VPeak = 0.30, VPin1 = false, Free = 0.25, Crown = 0.55,
        UPin0 = false, UPin1 = false, FreeU = 0.35, HangU0 = true, HangU1 = true,
        Bow = 0.05, RoachFoot = 0.11
    };
}

/// <summary>
/// Une voile comme SURFACE et non comme feuille — porté de <c>_sailSurface</c> et
/// de <c>setSailShape</c> (ship-model.js).
///
/// Un quadrilatère plat se lit comme de la tôle quel que soit l'éclairage : sa
/// normale est constante, une seule teinte sur toute la toile, nulle part où la
/// lumière puisse tourner. Une voile est donc une grille tendue entre ses quatre
/// coins et poussée le long de sa propre normale, le plus loin là où rien ne la
/// tient.
///
/// En <c>float</c> là où l'original tenait des <c>Float32Array</c> — la base, les
/// poids, le tampon de sommets, les normales —, calcul en double entre deux, pour
/// que le banc de parité la compare au bit près. Le moteur ne fait que recopier
/// <see cref="Positions"/> et <see cref="Normals"/> dans un maillage.
/// </summary>
public sealed class SailCloth
{
    public readonly float[] Base, W, Sag, U, Swag, Uvs;
    public readonly int[] Indices;
    public readonly int NSwag, Nu1;
    public readonly Vec3d Dir;
    public readonly string Kind;

    /// <summary>La toile telle qu'elle est à cette image, et ses normales.</summary>
    public readonly float[] Positions, Normals;

    /// <summary>
    /// La profondeur de la toile le long d'un axe. <paramref name="d"/> est où
    /// elle est la plus creuse, <paramref name="pin0"/>/<paramref name="pin1"/>
    /// disent si chaque bout est LACÉ. Toute la question est là : un bord lacé
    /// est tiré à plat contre son espar, mais un bord libre n'est tenu qu'à ses
    /// deux coins et garde l'essentiel de son creux jusqu'à la ralingue — il
    /// part à une fraction <paramref name="r"/> du maximum et non à rien.
    /// </summary>
    public static double BellyProfile(double x, double d, bool pin0, bool pin1, double r, double crown)
    {
        // sin(π·x^k) : une demi-sinusoïde dont la crête a glissé jusqu'à d
        double k = Math.Log(0.5) / Math.Log(d);
        double f = Math.Sin(Math.PI * Math.Pow(x, k));
        if (!pin1 && x > d) f = 1 - (1 - r) * Math.Pow((x - d) / (1 - d), 2);
        if (!pin0 && x < d) f = 1 - (1 - r) * Math.Pow((d - x) / d, 2);
        /* Puis aplatie en haut : une demi-sinusoïde est une BOSSE, une toile
           pleine est un COUSSIN — large et presque plate au milieu, ne tombant
           franchement que dans le dernier bout de sa largeur. */
        return crown == 1 ? f : Math.Pow(f, crown);
    }

    /// <summary>
    /// Tisser la voile entre ses coins, donnés dans l'ordre cyclique ; une voile
    /// à trois coins répète le dernier et la grille se ferme le long de ce bord.
    /// </summary>
    public SailCloth(Vec3d[] corners, Vec3d dir, SailCut c)
    {
        Kind = c.Kind;
        double chordFree = c.FreeU ?? c.Free;
        bool hPinU0 = c.HangU0 ?? c.UPin0, hPinU1 = c.HangU1 ?? c.UPin1;
        bool hPinV0 = c.HangV0 ?? c.VPin0, hPinV1 = c.HangV1 ?? c.VPin1;

        /* SEIZE colonnes pour une voile carrée, et ce sont les festons qui l'ont
           imposé : un feston veut quatre colonnes pour se dessiner en boucle et
           non en encoche, et un raban qui tombe ENTRE deux colonnes n'est jamais
           échantillonné à son pincement. Rien d'autre à bord n'en veut autant. */
        int nu = c.Kind == "square" ? 16 : 8, nv = 8;
        double HangU(double x) => (hPinU0 ? 0 : (1 - x) * (1 - x)) + (hPinU1 ? 0 : x * x);
        double HangV(double x) => (hPinV0 ? 0 : (1 - x) * (1 - x)) + (hPinV1 ? 0 : x * x);
        Vec3d c00 = corners[0], c10 = corners[1], c11 = corners[2];
        Vec3d c01 = corners.Length > 3 ? corners[3] : corners[2];

        /* COMMENT ELLE EST SERRÉE. Une voile carrée ferlée n'est pas un boudin :
           ramassée sur sa vergue et tenue par des rabans à intervalles, elle pend
           entre deux rabans en FESTON. Un raban à peu près tous les trois mètres
           de vergue — la portée d'un homme —, calé sur un diviseur du nombre de
           colonnes, sans quoi il tombe entre deux sommets et ne pince rien. */
        int nSwag = 0;
        if (c.Kind == "square")
        {
            double yard = Dist(c00, c10), want = yard / 3.0, bestErr = double.PositiveInfinity;
            for (int d = 2; d <= 4; d *= 2)
                if (Math.Abs(d - want) < bestErr) { bestErr = Math.Abs(d - want); nSwag = d; }
        }
        NSwag = nSwag;

        Vec3d across = c10 - c00;
        double head = across.Length;
        if (head > 1e-6) across = Div(across, head);
        Vec3d up = c00 - c01;
        double drop = up.Length;
        if (drop > 1e-6) up = Div(up, drop);
        double kRoach = Math.Log(0.5) / Math.Log(0.58);   // le plus creux à v = 0,58

        int n = (nu + 1) * (nv + 1);
        Base = new float[n * 3]; W = new float[n]; Sag = new float[n]; U = new float[n];
        Swag = new float[n]; Uvs = new float[n * 2];
        int k = 0;
        for (int j = 0; j <= nv; j++)
        {
            double v = (double)j / nv;
            Vec3d a = Lerp(c00, c01, v);                 // le long d'une chute
            Vec3d b = Lerp(c10, c11, v);                 // le long de l'autre
            for (int i = 0; i <= nu; i++, k++)
            {
                double u = (double)i / nu;
                Vec3d p = Lerp(a, b, u);
                /* SES BORDS SONT TAILLÉS, et c'est un fait de la toile, pas du
                   vent : les chutes rentrent à mi-hauteur, la bordure remonte au
                   milieu — le rond d'une basse voile. Cuit dans la géométrie de
                   base, donc elle garde sa coupe même choquée. */
                if (c.Bow != 0) p = AddScaled(p, across,
                    -c.Bow * head * (2 * u - 1) * Math.Sin(Math.PI * Math.Pow(v, kRoach)));
                if (c.RoachFoot != 0) p = AddScaled(p, up,
                    c.RoachFoot * drop * Math.Sin(Math.PI * u) * v * v);
                Base[k * 3] = (float)p.X; Base[k * 3 + 1] = (float)p.Y; Base[k * 3 + 2] = (float)p.Z;
                W[k] = (float)(BellyProfile(u, c.UPeak, c.UPin0, c.UPin1, chordFree, c.Crown)
                             * BellyProfile(v, c.VPeak, c.VPin0, c.VPin1, c.Free, 1));
                /* La toile PEND : une bordure libre est plus longue que la droite
                   entre ses points d'écoute, elle sourit entre eux. Seul l'axe
                   libre pend : une chute est tendue, pas suspendue. */
                Sag[k] = (float)Math.Max(HangU(u) * Math.Sin(Math.PI * v), HangV(v) * Math.Sin(Math.PI * u));
                U[k] = (float)u;
                // sur la grille paramétrique, jamais sur les positions finies : la
                // peinture voyage avec le tissage ; 1 − v, sans quoi elle est à l'envers
                Uvs[k * 2] = (float)u; Uvs[k * 2 + 1] = (float)(1 - v);
                // sa place dans son feston, une fois pour toutes : u ne change jamais
                Swag[k] = nSwag != 0 ? (float)Math.Pow(Math.Abs(Math.Sin(Math.PI * u * nSwag)), 0.7) : 0f;
            }
        }
        Indices = new int[nv * nu * 6];
        int t = 0;
        for (int j = 0; j < nv; j++)
            for (int i = 0; i < nu; i++)
            {
                int q = j * (nu + 1) + i;
                Indices[t++] = q; Indices[t++] = q + nu + 1; Indices[t++] = q + nu + 2;
                Indices[t++] = q; Indices[t++] = q + nu + 2; Indices[t++] = q + 1;
            }
        Nu1 = nu + 1;
        Dir = dir.Normalized();
        Positions = (float[])Base.Clone();
        Normals = new float[n * 3];
        ComputeNormals();
    }

    /// <summary>
    /// Remplir la toile, ou la vider — <c>setSailShape</c> pour cette voile.
    ///
    /// La profondeur est la pression que subit la toile, que le solveur calcule
    /// déjà pour la pousser : elle se gonfle à mesure qu'on la borde et fasille
    /// dès qu'on choque, sans seconde règle à tenir d'accord avec la première.
    /// <paramref name="set"/> est la fraction de toile établie, du solveur.
    /// </summary>
    public void Shape(double belly, double load, bool luffing, double t, double set)
    {
        // `belly || 0.8` : l'original prend aussi un zéro pour « absent »
        double full = belly != 0 ? belly : 0.8;
        double press = Math.Min(1, Math.Max(0, load / 35));   // Pa — une bonne brise la remplit
        double depth = luffing ? full * 0.12 : full * press;

        /* FERLER : chaque sommet remonte vers celui qui est au-dessus de lui sur la
           RANGÉE ZÉRO — la têtière d'une carrée, qui monte à sa vergue ; la bordure
           d'une aurique, qui descend sur sa bôme ; l'étai d'un foc. Jamais tout à
           fait à rien : six pour cent de sa chute, c'est le rouleau qu'on voit à un
           mille. */
        double sf = Math.Max(0, Math.Min(1, set));
        double stow = 0.06 + 0.94 * sf;
        // et le rouleau est le plus GROS quand elle est serrée : c'est la bune
        double bunt = full * 0.45 * (1 - sf);
        double furl = 1 - sf;
        float[] b = Base, w = W, sg = Sag, u = U;
        float[]? sw = NSwag != 0 ? Swag : null;
        Vec3d d = Dir;
        int nu1 = Nu1;
        // elle pend un peu, par-dessus sa coupe, sans jamais retourner le rond
        double hang = full * (0.10 + 0.06 * press);
        for (int k = 0, n = w.Length; k < n; k++)
        {
            int i3 = k * 3, r0 = (k % nu1) * 3;         // sa place sur la rangée zéro
            // son feston : 0,20 sous un raban, 2,05 au creux, 1 tout juste établie
            double q = sw != null ? sw[k] : 1;
            double st = sw != null ? 0.06 * (1 + furl * (0.20 + 1.85 * q - 1)) + 0.94 * sf : stow;
            double f = w[k] * ((depth + (luffing ? full * 0.22 * Math.Sin(u[k] * 7 - t * 9) : 0)) * sf
                               + bunt * (sw != null ? 0.35 + 0.85 * q : 1));
            double g = sg[k] * hang * sf;
            Positions[i3] = (float)(b[r0] + (b[i3] - b[r0]) * st + d.X * f);
            Positions[i3 + 1] = (float)(b[r0 + 1] + (b[i3 + 1] - b[r0 + 1]) * st + d.Y * f - g);
            Positions[i3 + 2] = (float)(b[r0 + 2] + (b[i3 + 2] - b[r0 + 2]) * st + d.Z * f);
        }
        ComputeNormals();                                // l'ombrage est tout l'enjeu
    }

    /// <summary>
    /// Les normales, comme <c>BufferGeometry.computeVertexNormals</c> de three.js
    /// r160 : produits vectoriels des faces cumulés sur leurs sommets, rangés en
    /// float à chaque ajout, puis normalisés par multiplication par 1/longueur.
    /// </summary>
    void ComputeNormals() => ComputeNormals(Positions, Normals, Indices);

    /// <summary>
    /// La même règle pour tout maillage : la voile, le pavillon, et les carreaux
    /// de terre — c'est computeVertexNormals de three.js, et il n'y en a qu'un.
    /// </summary>
    public static void ComputeNormals(float[] p, float[] nr, int[] idx)
    {
        Array.Clear(nr);
        for (int i = 0; i < idx.Length; i += 3)
        {
            int a = idx[i] * 3, bb = idx[i + 1] * 3, c = idx[i + 2] * 3;
            double cbx = (double)p[c] - p[bb], cby = (double)p[c + 1] - p[bb + 1], cbz = (double)p[c + 2] - p[bb + 2];
            double abx = (double)p[a] - p[bb], aby = (double)p[a + 1] - p[bb + 1], abz = (double)p[a + 2] - p[bb + 2];
            double x = cby * abz - cbz * aby, y = cbz * abx - cbx * abz, z = cbx * aby - cby * abx;
            Add(nr, a, x, y, z); Add(nr, bb, x, y, z); Add(nr, c, x, y, z);
        }
        for (int v = 0; v < nr.Length; v += 3)
        {
            double x = nr[v], y = nr[v + 1], z = nr[v + 2];
            double len = Math.Sqrt(x * x + y * y + z * z);
            double s = 1 / (len != 0 ? len : 1);
            nr[v] = (float)(x * s); nr[v + 1] = (float)(y * s); nr[v + 2] = (float)(z * s);
        }
    }

    static void Add(float[] nr, int v, double x, double y, double z)
    {
        nr[v] = (float)(nr[v] + x); nr[v + 1] = (float)(nr[v + 1] + y); nr[v + 2] = (float)(nr[v + 2] + z);
    }

    // three.js, formule pour formule : lerpVectors, addScaledVector, divideScalar
    static Vec3d Lerp(Vec3d a, Vec3d b, double t) =>
        new(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t, a.Z + (b.Z - a.Z) * t);
    static Vec3d AddScaled(Vec3d p, Vec3d v, double s) => new(p.X + v.X * s, p.Y + v.Y * s, p.Z + v.Z * s);
    static Vec3d Div(Vec3d v, double s) { double m = 1 / s; return new(v.X * m, v.Y * m, v.Z * m); }
    static double Dist(Vec3d a, Vec3d b)
    {
        double dx = a.X - b.X, dy = a.Y - b.Y, dz = a.Z - b.Z;
        return Math.Sqrt(dx * dx + dy * dy + dz * dz);
    }
}

/// <summary>
/// LES VERGUES SONT HALÉES, ELLES NE SAUTENT PAS — le brassage de <c>setTrim</c>.
///
/// Le bord du solveur est le simple signe du vent en travers, et il bascule à
/// l'instant où le vent apparent passe l'arrière — ce qu'une embardée d'un degré
/// fait toutes les quelques secondes au vent arrière. Écrit tel quel dans les
/// pivots, il faisait passer tout le gréement d'un bord à l'autre en une image.
/// Rien dans la physique ne lit ce signe, donc le remède vit ici : le bord
/// AFFICHÉ ne change qu'une fois le vent resté de l'autre côté
/// <see cref="Hold"/> secondes, et les bras viennent alors à
/// <see cref="Rate"/> radians par seconde, en prenant de l'erre et en la cassant.
/// </summary>
public sealed class BraceTrim
{
    public const double Rate = 0.8;     // rad/s
    public const double Hold = 1.5;     // s
    public const double Accel = 1.0;    // rad/s², pour prendre de l'erre comme pour la casser

    double? _trimT;
    int? _tackShown;
    double _tackHeld, _brace, _braceV;

    /// <summary>L'angle des vergues à cet instant, frisson du faseyement compris.</summary>
    public double Update(double sheet, int tack, bool luffing, double t)
    {
        double shake = luffing ? Math.Sin(t * 11) * 0.10 : 0;
        double dt = Math.Max(0, Math.Min(0.25, t - (_trimT ?? t)));
        _trimT = t;
        if (_tackShown == null) { _tackShown = tack; _brace = -tack * sheet; }
        if (tack != _tackShown)
        {
            _tackHeld += dt;
            if (_tackHeld >= Hold) { _tackShown = tack; _tackHeld = 0; }
        }
        else _tackHeld = 0;
        /* Amortis à l'entrée et à la sortie : la vitesse permise est celle dont
           cette décélération arrête encore dans l'angle restant, √(2·a·err).
           Écrit contre l'erreur plutôt qu'en courbe minutée, si bien qu'une
           écoute bordée pendant que les vergues tournent est simplement suivie. */
        double want = -_tackShown.Value * sheet;
        double err = want - _brace;
        double vWant = Math.Sign(err) * Math.Min(Rate, Math.Sqrt(2 * Accel * Math.Abs(err)));
        double dv = Accel * dt;
        _braceV += Math.Max(-dv, Math.Min(dv, vWant - _braceV));
        _brace += _braceV * dt;
        // jamais au-delà de la marque : un pas qui la franchit s'y pose, à l'arrêt
        if ((want - _brace) * err <= 0) { _brace = want; _braceV = 0; }
        return _brace + shake;
    }
}
