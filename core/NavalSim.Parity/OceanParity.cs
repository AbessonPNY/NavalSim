using System.Text.Json;
using NavalSim.Core;

namespace NavalSim.Parity;

/// <summary>
/// La parite de la MER, et c'est celle qui compte le plus.
///
/// Un plan de formes qui derive se voit : la coque a l'air fausse. Une houle qui
/// derive ne se voit PAS -- elle fait flotter la coque sur une mer que l'oeil ne
/// voit pas, ce qui est la panne silencieuse contre laquelle tout le projet est
/// ecrit. D'ou trois verifications distinctes plutot qu'une :
///
///   1. LE SPECTRE. Les dix-huit composantes, une a une.
///   2. L'ECHANTILLONNAGE. La hauteur et la normale, sur une grille large et a
///      des instants qui ne tombent pas rond.
///   3. LE RECENTRAGE. Un point fixe DANS LE MONDE doit garder sa hauteur quand
///      l'origine glisse sous lui. C'est l'invariant de l'origine flottante, et
///      il est teste jusqu'a 1 800 km.
/// </summary>
public static class OceanParity
{
    sealed class Acc
    {
        public double WorstAbs, WorstRel;
        public string WhereAbs = "", WhereRel = "";

        public void Check(string what, double js, double cs)
        {
            double a = Math.Abs(js - cs);
            if (a > WorstAbs) { WorstAbs = a; WhereAbs = what; }
            double scale = Math.Max(Math.Abs(js), Math.Abs(cs));
            if (scale > 1e-12)
            {
                double r = a / scale;
                if (r > WorstRel) { WorstRel = r; WhereRel = what; }
            }
        }
    }

    public static int Run(string dumpPath)
    {
        if (!File.Exists(dumpPath))
        {
            Console.Error.WriteLine($"releve mer introuvable : {dumpPath}");
            Console.Error.WriteLine("lancer d'abord :  node tools/parity-ocean.js");
            return 1;
        }

        using var doc = JsonDocument.Parse(File.ReadAllText(dumpPath));
        int failures = 0;

        /* Deux tolerances, comme pour le plan de formes, mais posees pour une
           raison differente : tout est en double des DEUX cotes ici, donc le seul
           ecart legitime vient de ce que Math.Pow, Math.Exp et Math.Sin de .NET
           ne sont pas tenus de rendre le meme dernier bit que ceux de V8. Cet
           ecart est RELATIF, et il se propage a travers le spectre -- d'ou un
           seuil relatif sur les grandeurs qui s'etalent sur des ordres de
           grandeur, et un seuil absolu, en METRES, sur ce qui est une hauteur. */
        const double TolRel = 1e-11;
        const double TolAbsM = 1e-9;    // un nanometre de mer

        Console.WriteLine("--- la mer : spectre et echantillonnage ---");
        foreach (var c in doc.RootElement.GetProperty("cases").EnumerateArray())
        {
            double force = c.GetProperty("force").GetDouble();
            double deg = c.GetProperty("deg").GetDouble();
            double swell = c.GetProperty("swell").GetDouble();
            double t0 = c.GetProperty("t0").GetDouble();

            var sea = new Ocean { Swell = swell, Time = t0 };
            sea.SetSeaState(force, deg);

            var spec = new Acc();
            var samp = new Acc();

            spec.Check("windSpeed", c.GetProperty("windSpeed").GetDouble(), sea.WindSpeed);
            var wv = c.GetProperty("windVec");
            spec.Check("windVec.x", wv[0].GetDouble(), sea.WindVec.X);
            spec.Check("windVec.z", wv[2].GetDouble(), sea.WindVec.Z);
            spec.Check("sharp", c.GetProperty("sharp").GetDouble(), sea.Sharp);
            spec.Check("ampMax", c.GetProperty("ampMax").GetDouble(), sea.AmpMax);

            int jsCpu = c.GetProperty("cpuWaves").GetInt32();
            if (jsCpu != sea.CpuWaveCount)
            {
                Console.Error.WriteLine($"  force {force}: {jsCpu} composantes CPU cote JS, "
                                      + $"{sea.CpuWaveCount} cote C#");
                failures++;
            }

            var jw = c.GetProperty("waves");
            if (jw.GetArrayLength() != sea.Waves.Length)
            {
                Console.Error.WriteLine($"  force {force}: {jw.GetArrayLength()} composantes cote JS, "
                                      + $"{sea.Waves.Length} cote C#");
                failures++;
            }
            else
                for (int i = 0; i < sea.Waves.Length; i++)
                {
                    var a = jw[i]; ref Wave b = ref sea.Waves[i];
                    spec.Check($"w[{i}].dx", a.GetProperty("dx").GetDouble(), b.Dx);
                    spec.Check($"w[{i}].dz", a.GetProperty("dz").GetDouble(), b.Dz);
                    spec.Check($"w[{i}].amp", a.GetProperty("amp").GetDouble(), b.Amp);
                    spec.Check($"w[{i}].k", a.GetProperty("k").GetDouble(), b.K);
                    spec.Check($"w[{i}].omega", a.GetProperty("omega").GetDouble(), b.Omega);
                    spec.Check($"w[{i}].Q", a.GetProperty("Q").GetDouble(), b.Q);
                    spec.Check($"w[{i}].L", a.GetProperty("L").GetDouble(), b.L);
                    spec.Check($"w[{i}].phase", a.GetProperty("phase").GetDouble(), b.Phase);
                    if (a.GetProperty("band").GetInt32() != b.Band)
                    {
                        Console.Error.WriteLine($"  force {force}: w[{i}] bande "
                            + $"{a.GetProperty("band").GetInt32()} contre {b.Band}");
                        failures++;
                    }
                }

            foreach (var s in c.GetProperty("samples").EnumerateArray())
            {
                double t = s.GetProperty("t").GetDouble();
                double x = s.GetProperty("x").GetDouble();
                double z = s.GetProperty("z").GetDouble();
                double y = sea.Sample(x, z, t, out Vec3d n);
                samp.Check($"y({x},{z},{t})", s.GetProperty("y").GetDouble(), y);
                samp.Check("n.x", s.GetProperty("nx").GetDouble(), n.X);
                samp.Check("n.y", s.GetProperty("ny").GetDouble(), n.Y);
                samp.Check("n.z", s.GetProperty("nz").GetDouble(), n.Z);
            }

            bool ok = spec.WorstRel <= TolRel && samp.WorstAbs <= TolAbsM;
            if (!ok) failures++;
            Console.WriteLine($"  {(ok ? "OK   " : "ECART")} force {force,-4} {deg,3}deg creux {swell:F2}"
                            + $"   spectre {spec.WorstRel:E1} rel   houle {samp.WorstAbs:E1} m");
            if (!ok)
                Console.WriteLine($"        pire : spectre sur {spec.WhereRel}, houle sur {samp.WhereAbs}");
        }

        Console.WriteLine();
        Console.WriteLine("--- l'origine flottante : la mer doit etre continue au recentrage ---");
        {
            var sea = new Ocean { Time = 1234.5 };
            sea.SetSeaState(5, 120);
            const double t = 1234.5;
            var acc = new Acc();
            double refY0 = double.NaN;

            foreach (var r in doc.RootElement.GetProperty("rebases").EnumerateArray())
            {
                sea.Rebase(r.GetProperty("dx").GetDouble(), r.GetProperty("dz").GetDouble());

                acc.Check("origin.x", r.GetProperty("originX").GetDouble(), sea.Origin.X);
                acc.Check("origin.z", r.GetProperty("originZ").GetDouble(), sea.Origin.Z);

                double worstJump = 0;
                foreach (var p in r.GetProperty("pts").EnumerateArray())
                {
                    double wx = p.GetProperty("wx").GetDouble();
                    double wz = p.GetProperty("wz").GetDouble();
                    double lx = wx - sea.Origin.X, lz = wz - sea.Origin.Z;
                    double y = sea.Sample(lx, lz, t);
                    acc.Check($"y@({wx},{wz})", p.GetProperty("y").GetDouble(), y);

                    // et le SAUT : le meme point monde, avant et apres le glissement
                    if (wx == 0 && wz == 0)
                    {
                        if (!double.IsNaN(refY0)) worstJump = Math.Abs(y - refY0);
                        refY0 = y;
                    }
                }
                Console.WriteLine($"  origine ({sea.Origin.X,9:F0}, {sea.Origin.Z,9:F0}) m"
                                + $"   saut au point fixe {worstJump:E1} m");
            }

            bool ok = acc.WorstAbs <= 1e-6;
            if (!ok) failures++;
            Console.WriteLine($"  {(ok ? "OK   " : "ECART")} accord avec le JS : {acc.WorstAbs:E1}"
                            + (ok ? "" : $"  sur {acc.WhereAbs}"));
        }

        return failures;
    }
}
