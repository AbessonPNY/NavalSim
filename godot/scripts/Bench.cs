using Godot;
using System.Diagnostics;

namespace NavalSim;

/// <summary>
/// Le banc du solveur, EXECUTE DANS LE MOTEUR.
///
/// Il existe pour lever une reserve et une seule. Le chiffre de 0,534 ms mesure
/// du C# sous `dotnet` seul ; rien ne prouvait qu'il tienne une fois le CLR
/// heberge par Godot, ni que le cout de traverser vers l'API du moteur soit
/// negligeable. On mesure donc les deux variantes cote a cote :
///
///   A. doubles bruts, aucun type Godot -- ce que sera le noyau
///   B. Vector3 et Quaternion de Godot -- le C# « idiomatique »
///
/// Si B coute cher, l'architecture qui garde le noyau hors du moteur est
/// justifiee par la mesure et non par le principe. Si B est gratuit, on le sait
/// aussi, et on ne s'impose pas une frontiere pour rien.
/// </summary>
public partial class Bench : Node
{
    const int NPROBE = 11 * 7 * 7, SUB = 4, NW = 10;
    const double SHARP = 1.35;

    static readonly double[] wdx = new double[NW], wdz = new double[NW],
        wamp = new double[NW], wk = new double[NW], wom = new double[NW],
        wq = new double[NW], wph = new double[NW];

    static readonly double[] px = new double[NPROBE], py = new double[NPROBE],
        pz = new double[NPROBE], pfrac = new double[NPROBE];

    static readonly Vector3[] gProbe = new Vector3[NPROBE];
    static readonly float[] gFrac = new float[NPROBE];

    const double qx = 0.04, qy = 0.12, qz = 0.02, qw = 0.991;
    const double bx = 3.0, by = 0.4, bz = -2.0;

    public override void _Ready()
    {
        Setup();

        // --- A : doubles bruts ---
        double sink = 0;
        for (int i = 0; i < 200; i++) sink += FrameRaw(i * 0.016);
        const int N = 2000;
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < N; i++) sink += FrameRaw(i * 0.016);
        sw.Stop();
        double msA = sw.Elapsed.TotalMilliseconds / N;

        // --- B : types Godot ---
        double sinkB = 0;
        for (int i = 0; i < 200; i++) sinkB += FrameGodot(i * 0.016f, true);
        sw.Restart();
        for (int i = 0; i < N; i++) sinkB += FrameGodot(i * 0.016f, true);
        sw.Stop();
        double msB = sw.Elapsed.TotalMilliseconds / N;

        // --- C : types Godot, rotation ecrite a la main ---
        double sinkC = 0;
        for (int i = 0; i < 200; i++) sinkC += FrameGodot(i * 0.016f, false);
        sw.Restart();
        for (int i = 0; i < N; i++) sinkC += FrameGodot(i * 0.016f, false);
        sw.Stop();
        double msC = sw.Elapsed.TotalMilliseconds / N;

        GD.Print($"DANS GODOT  .NET {System.Environment.Version}");
        GD.Print($"  A. doubles bruts (le noyau)        {msA:F3} ms/image/coque   [{sink:F0}]");
        GD.Print($"  B. Vector3 + operateur Quaternion  {msB:F3} ms/image/coque   [{sinkB:F0}]");
        GD.Print($"  C. Vector3, rotation a la main     {msC:F3} ms/image/coque   [{sinkC:F0}]");
        GD.Print($"  B/A {msB / msA:F2}    C/A {msC / msA:F2}    B/C {msB / msC:F2}");

        GetTree().Quit();
    }

    static void Setup()
    {
        for (int i = 0; i < NW; i++)
        {
            double a = i * 0.7;
            wdx[i] = Mathf.Cos(a); wdz[i] = Mathf.Sin(a);
            wamp[i] = 1.2 / (1 + i * 0.4);
            wk[i] = 0.05 + i * 0.03; wom[i] = 0.6 + i * 0.11;
            wq[i] = 0.4; wph[i] = i * 1.3;
        }
        for (int i = 0; i < NPROBE; i++)
        {
            px[i] = (i % 11) - 5; py[i] = ((i / 11) % 7) - 3; pz[i] = (i / 77) - 3;
            gProbe[i] = new Vector3((float)px[i], (float)py[i], (float)pz[i]);
        }
    }

    // ---------------- A : doubles bruts ----------------

    static double SampleRaw(double x, double z, double t)
    {
        double y = 0;
        for (int i = 0; i < NW; i++)
        {
            double f = wk[i] * (wdx[i] * x + wdz[i] * z) - wom[i] * t + wph[i];
            double c = System.Math.Cos(f), sn = System.Math.Sin(f);
            double a_s = System.Math.Abs(sn);
            double sp = System.Math.Pow(System.Math.Max(a_s, 1e-4), SHARP - 1.0);
            double amp = wamp[i];
            y += amp * System.Math.Sign(sn) * a_s * sp;
            double WA = wk[i] * amp * SHARP * sp;
            double nx = -wdx[i] * WA * c;
            double nz = -wdz[i] * WA * c;
            double ny = -wq[i] * WA * sn;
            _ = nx + nz + ny;
        }
        return y;
    }

    static double FrameRaw(double t)
    {
        double vol = 0;
        for (int s = 0; s < SUB; s++)
        {
            for (int i = 0; i < NPROBE; i++)
            {
                double lx = px[i], ly = py[i], lz = pz[i];
                double ix = qw * lx + qy * lz - qz * ly;
                double iy = qw * ly + qz * lx - qx * lz;
                double iz = qw * lz + qx * ly - qy * lx;
                double iw = -qx * lx - qy * ly - qz * lz;
                double wx = ix * qw + iw * -qx + iy * -qz - iz * -qy + bx;
                double wy = iy * qw + iw * -qy + iz * -qx - ix * -qz + by;
                double wz = iz * qw + iw * -qz + ix * -qy - iy * -qx + bz;
                double depth = SampleRaw(wx, wz, t) - wy;
                double f = depth > 0 ? System.Math.Min(1.0, depth / 0.6) : 0.0;
                pfrac[i] = f;
                vol += f;
            }
            t += 1.0 / 240.0;
        }
        return vol;
    }

    // ---------------- B : types Godot ----------------

    static float SampleGodot(float x, float z, float t)
    {
        float y = 0;
        for (int i = 0; i < NW; i++)
        {
            float f = (float)(wk[i] * (wdx[i] * x + wdz[i] * z) - wom[i] * t + wph[i]);
            float c = Mathf.Cos(f), sn = Mathf.Sin(f);
            float a_s = Mathf.Abs(sn);
            float sp = Mathf.Pow(Mathf.Max(a_s, 1e-4f), (float)SHARP - 1f);
            float amp = (float)wamp[i];
            y += amp * Mathf.Sign(sn) * a_s * sp;
            float WA = (float)wk[i] * amp * (float)SHARP * sp;
            float nx = (float)-wdx[i] * WA * c;
            float nz = (float)-wdz[i] * WA * c;
            float ny = (float)-wq[i] * WA * sn;
            _ = nx + nz + ny;
        }
        return y;
    }

    static double FrameGodot(float t, bool useOperator)
    {
        // Normalise : Godot VERIFIE la normalisation d'un quaternion et leve si
        // elle manque, la ou three.js laisse passer. Le quaternion d'essai vaut
        // 0,9985 de norme -- inoffensif cote JS, refuse ici. C'est une difference
        // de temperament du moteur qu'il faudra garder en tete au portage du
        // solveur, dont les quaternions s'accumulent sur des milliers de pas.
        var rot = new Quaternion((float)qx, (float)qy, (float)qz, (float)qw).Normalized();
        var org = new Vector3((float)bx, (float)by, (float)bz);
        double vol = 0;
        for (int s = 0; s < SUB; s++)
        {
            for (int i = 0; i < NPROBE; i++)
            {
                Vector3 w = Rotate(rot, gProbe[i], useOperator) + org;
                float depth = SampleGodot(w.X, w.Z, t) - w.Y;
                float f = depth > 0 ? Mathf.Min(1f, depth / 0.6f) : 0f;
                gFrac[i] = f;
                vol += f;
            }
            t += 1f / 240f;
        }
        return vol;
    }

    /// <summary>
    /// Tourne un vecteur par un quaternion, des deux facons, pour les departager.
    ///
    /// L'operateur de Godot appelle IsNormalized() a CHAQUE invocation, donc une
    /// racine carree par sonde -- 539 fois par sous-pas et par coque, pour verifier
    /// une propriete qui n'a pas change depuis le debut de l'image. C'est ce que
    /// la variante C mesure : meme type Vector3, meme arithmetique, sans le
    /// controle.
    /// </summary>
    static Vector3 Rotate(in Quaternion q, in Vector3 v, bool useOperator)
    {
        if (useOperator) return q * v;

        // la meme rotation, ecrite a la main : v + 2w(q x v) + 2(q x (q x v))
        float ix = q.W * v.X + q.Y * v.Z - q.Z * v.Y;
        float iy = q.W * v.Y + q.Z * v.X - q.X * v.Z;
        float iz = q.W * v.Z + q.X * v.Y - q.Y * v.X;
        float iw = -q.X * v.X - q.Y * v.Y - q.Z * v.Z;
        return new Vector3(
            ix * q.W + iw * -q.X + iy * -q.Z - iz * -q.Y,
            iy * q.W + iw * -q.Y + iz * -q.X - ix * -q.Z,
            iz * q.W + iw * -q.Z + ix * -q.Y - iy * -q.X);
    }
}
