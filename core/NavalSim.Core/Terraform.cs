using System;
using System.IO;

namespace NavalSim.Core;

/// <summary>
/// LE TERRASSEMENT D'UN PORT (demandé : « monter le sol et le descendre, ajuster la
/// hauteur au bord de l'eau ou surélever le terrain qui le nécessite »). Une grille de
/// DÉCALAGES de hauteur, en mètres, sur un carré autour du port — le même carré que
/// son sol peint —, ajoutée au relief par <see cref="World.IslandHeight"/>. Tout ce qui
/// lit la terre la voit donc : l'échouage, les sondes, le fond, le rivage, le semis.
///
/// Une grille nulle ne change rien : le relief de la fiche reste la vérité, le
/// terrassement n'en est que la retouche, comme les retouches des objets posés.
///
/// Sur le disque (world/terrassement/&lt;port&gt;.bin) : « NVTR », le côté de la grille
/// en cases (int32), son pas en mètres (float32), puis les décalages en CENTIMÈTRES,
/// int16, ligne après ligne (z), petit-boutiste — ± 327 m, deux mégaoctets à un mètre.
/// </summary>
public sealed class Terraform
{
    public readonly double X0, Z0, Side, Step;
    public readonly int N;
    /// <summary>Le décalage aux nœuds de la grille, en mètres ; [j * N + i], i le long de x.</summary>
    public readonly float[] H;

    /// <param name="cx">Le centre du carré, en mètres vrais.</param>
    public Terraform(double cx, double cz, double side, double step)
    {
        Step = step;
        N = Math.Max(2, (int)Math.Round(side / step) + 1);
        Side = (N - 1) * step;
        X0 = cx - Side * 0.5; Z0 = cz - Side * 0.5;
        H = new float[N * N];
    }

    public bool Covers(double x, double z) => x >= X0 && z >= Z0 && x <= X0 + Side && z <= Z0 + Side;

    /// <summary>Le décalage en un point, bilinéaire ; nul hors du carré.</summary>
    public double At(double x, double z)
    {
        double fi = (x - X0) / Step, fj = (z - Z0) / Step;
        if (fi < 0 || fj < 0 || fi > N - 1 || fj > N - 1) return 0;
        int i = Math.Min(N - 2, (int)fi), j = Math.Min(N - 2, (int)fj);
        double a = fi - i, b = fj - j;
        int k = j * N + i;
        return (H[k] * (1 - a) + H[k + 1] * a) * (1 - b) + (H[k + N] * (1 - a) + H[k + N + 1] * a) * b;
    }

    public enum Tool { Raise, Lower, Flatten, Smooth }

    /// <summary>
    /// UN COUP DE PINCEAU, et le rectangle de nœuds qu'il a touchés (pour ne rebâtir
    /// que ce qui a changé). <paramref name="rate"/> : mètres par seconde au centre ;
    /// <paramref name="target"/> : la hauteur du SOL (relief compris) que l'aplanissoir
    /// vise, et <paramref name="ground"/> la hauteur du sol sans ce terrassement, pour
    /// la convertir en décalage. Plein jusqu'à mi-rayon, puis un fondu.
    /// </summary>
    public (int I0, int J0, int I1, int J1)? Dab(double x, double z, double radius, Tool tool, double rate, double dt,
                                                  double target, Func<double, double, double> ground)
    {
        double ci = (x - X0) / Step, cj = (z - Z0) / Step, rp = Math.Max(0.5, radius / Step);
        int i0 = Math.Max(0, (int)Math.Floor(ci - rp)), i1 = Math.Min(N - 1, (int)Math.Ceiling(ci + rp));
        int j0 = Math.Max(0, (int)Math.Floor(cj - rp)), j1 = Math.Min(N - 1, (int)Math.Ceiling(cj + rp));
        if (i0 > i1 || j0 > j1) return null;
        float[]? before = tool == Tool.Smooth ? (float[])H.Clone() : null;
        for (int j = j0; j <= j1; j++)
            for (int i = i0; i <= i1; i++)
            {
                double dx = (i - ci) / rp, dz = (j - cj) / rp;
                double d = Math.Sqrt(dx * dx + dz * dz);
                if (d >= 1) continue;
                double w = MathX.Smooth01(d < 0.5 ? 1 : 1 - (d - 0.5) / 0.5);
                int k = j * N + i;
                double step = rate * dt * w;
                switch (tool)
                {
                    case Tool.Raise: H[k] += (float)step; break;
                    case Tool.Lower: H[k] -= (float)step; break;
                    case Tool.Flatten:
                    {
                        // vers la hauteur visée, sans la dépasser
                        double want = target - ground(X0 + i * Step, Z0 + j * Step);
                        double gap = want - H[k];
                        H[k] += (float)(Math.Sign(gap) * Math.Min(Math.Abs(gap), step));
                        break;
                    }
                    case Tool.Smooth:
                    {
                        // vers la moyenne de ses voisins, sol compris : un talus devient une pente
                        double sum = 0; int n = 0;
                        for (int b = -1; b <= 1; b++)
                            for (int a = -1; a <= 1; a++)
                            {
                                int ii = i + a, jj = j + b;
                                if (ii < 0 || jj < 0 || ii >= N || jj >= N) continue;
                                sum += ground(X0 + ii * Step, Z0 + jj * Step) + before![jj * N + ii]; n++;
                            }
                        double here = ground(X0 + i * Step, Z0 + j * Step) + before![k];
                        double gap = sum / n - here;
                        H[k] += (float)(gap * Math.Min(1, rate * 0.5 * dt * w));
                        break;
                    }
                }
            }
        return (i0, j0, i1, j1);
    }

    public bool Any()
    {
        foreach (var v in H) if (v != 0) return true;
        return false;
    }

    public byte[] Save()
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write(new[] { (byte)'N', (byte)'V', (byte)'T', (byte)'R' });
        w.Write(N); w.Write((float)Step);
        foreach (var v in H) w.Write((short)Math.Clamp(Math.Round(v * 100), short.MinValue, short.MaxValue));
        w.Flush();
        return ms.ToArray();
    }

    /// <summary>Relire ; faux (et rien de changé) si le fichier n'est pas celui de cette grille.</summary>
    public bool Load(byte[] data)
    {
        if (data.Length < 12 || data[0] != 'N' || data[1] != 'V' || data[2] != 'T' || data[3] != 'R') return false;
        using var r = new BinaryReader(new MemoryStream(data));
        r.ReadBytes(4);
        int n = r.ReadInt32(); float step = r.ReadSingle();
        if (n != N || Math.Abs(step - Step) > 1e-4 || data.Length < 12 + n * n * 2) return false;
        for (int k = 0; k < H.Length; k++) H[k] = r.ReadInt16() / 100f;
        return true;
    }
}
