using System.Globalization;
using NavalSim.Core;

/* LE BANC DE MESURE.
 *
 * Distinct du banc de parité, et pour une raison : la parité répond à « les deux
 * disent-ils la même chose », ce banc-ci répond à « qu'est-ce que cela vaut ».
 * Ce projet mesure beaucoup — le CLAUDE.md est pour moitié un cahier de relevés
 * — et bricoler un script jetable à chaque question est ce qui fait qu'on prend
 * une mesure juste après avoir changé un réglage, c'est-à-dire qu'on mesure le
 * changement et non le réglage.
 *
 *   dotnet run --project core/NavalSim.Lab -- gale 9.9 1.35 schooner
 */

string shipsDir = Path.GetFullPath(Path.Combine(
    AppContext.BaseDirectory, "..", "..", "..", "..", "..", "ships"));

/* La machine est en francais, donc la culture par defaut met des virgules
   decimales et `double.Parse` refuse « 9.9 ». Un banc de mesure doit lire et
   ecrire les MEMES nombres que le releve de node auquel on le compare, sinon on
   passe son temps a se demander si 13,54 et 13.54 sont le meme chiffre. */
CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;

string mode = args.Length > 0 ? args[0] : "gale";

switch (mode)
{
    case "gale": Gale(); break;
    case "waves": DumpWaves(); break;
    case "parallele": Parallele(); break;
    default:
        Console.Error.WriteLine($"mode inconnu : {mode}");
        return 1;
}
return 0;

/* CE QUE VAUT UN ÉTAT DE MER, ET CE QU'IL FAIT AU NAVIRE.
 *
 * Les deux questions sont posées ensemble parce qu'elles se répondent l'une
 * l'autre : une houle très creuse mais très longue soulève une coque en bloc
 * sans la travailler, et l'on peut donc avoir treize mètres de hauteur
 * significative pour cinq degrés de roulis. Mesurer la mer seule laisserait
 * croire à une tempête que le navire ne sent pas. */
/* LES SOLVEURS SUR PLUSIEURS CŒURS, ÉPROUVÉS. Deux flottes identiques, l'une
   menée en série, l'autre en parallèle, dans la même mer : les trajectoires
   doivent être égales AU BIT PRÈS — chaque solveur ne touche qu'à son propre
   état et ne fait que LIRE la mer, donc l'ordre des calculs ne peut rien
   changer. Et le temps de chacune, pour savoir ce que le parallélisme rapporte.
     dotnet run --project core/NavalSim.Lab -c Release -- parallele 16 pirate */
void Parallele()
{
    int n = args.Length > 1 ? int.Parse(args[1], CultureInfo.InvariantCulture) : 16;
    string name = args.Length > 2 ? args[2] : "frigate17e";
    var spec = ShipSpec.FromJson(File.ReadAllText(Path.Combine(shipsDir, name + ".json")));
    var ocean = new Ocean { Swell = 1.6, Time = 0 };
    ocean.SetSeaState(7, 210);

    ShipPhysics[] Fleet()
    {
        var f = new ShipPhysics[n];
        for (int i = 0; i < n; i++)
        {
            var p = new ShipPhysics(spec, new HullLines(spec));
            var ctrl = new Controls { Throttle = 0.5, Rudder = 0.2, Sheet = 0.6, SailsSet = true };
            p.Settle(ocean, ctrl);
            p.Body.Pos = new Vec3d((i % 6 - 2.5) * 140, p.Body.Pos.Y, 160 + (i / 6) * 180);
            f[i] = p;
        }
        return f;
    }
    var ctrls = new Controls { Throttle = 0.5, Rudder = 0.2, Sheet = 0.6, SailsSet = true };
    var serial = Fleet();
    var par = Fleet();
    const int frames = 600, sub = 4;
    double dt = 1.0 / 60 / sub;

    var sw = System.Diagnostics.Stopwatch.StartNew();
    double t = 0;
    for (int f = 0; f < frames; f++)
    {
        foreach (var p in serial) { double tt = t; for (int s = 0; s < sub; s++) { p.Step(dt, ocean, ctrls, tt); tt += dt; } }
        t += sub * dt;
    }
    double msSerial = sw.Elapsed.TotalMilliseconds;

    sw.Restart();
    t = 0;
    for (int f = 0; f < frames; f++)
    {
        double t0 = t;
        System.Threading.Tasks.Parallel.For(0, n, i =>
        {
            double tt = t0;
            for (int s = 0; s < sub; s++) { par[i].Step(dt, ocean, ctrls, tt); tt += dt; }
        });
        t += sub * dt;
    }
    double msPar = sw.Elapsed.TotalMilliseconds;

    double worst = 0;
    for (int i = 0; i < n; i++)
    {
        var a = serial[i].Body; var b = par[i].Body;
        worst = Math.Max(worst, Math.Abs(a.Pos.X - b.Pos.X) + Math.Abs(a.Pos.Y - b.Pos.Y) + Math.Abs(a.Pos.Z - b.Pos.Z)
                              + Math.Abs(a.Quat.X - b.Quat.X) + Math.Abs(a.Quat.Y - b.Quat.Y)
                              + Math.Abs(a.Quat.Z - b.Quat.Z) + Math.Abs(a.Quat.W - b.Quat.W));
    }
    Console.WriteLine($"{n} × {spec.Name}, {frames} images de {sub} sous-pas, {Environment.ProcessorCount} cœurs logiques");
    Console.WriteLine($"  série      {msSerial / frames:F3} ms par image");
    Console.WriteLine($"  parallèle  {msPar / frames:F3} ms par image   (×{msSerial / msPar:F1})");
    Console.WriteLine($"  écart des trajectoires : {worst:E1} {(worst == 0 ? "— identiques au bit près" : "— DIFFÉRENTES")}");
}

/* Les vagues que lit le solveur, une par ligne, pour les poser à côté de
   `Naval.app.ocean.cpuWaves` dans la page : amp, k, dx, dz, omega, Q. */
void DumpWaves()
{
    double force = args.Length > 1 ? double.Parse(args[1], CultureInfo.InvariantCulture) : 9.9;
    double swell = args.Length > 2 ? double.Parse(args[2], CultureInfo.InvariantCulture) : 1.35;
    var ocean = new Ocean { Swell = swell, Time = 0 };
    ocean.SetSeaState(force, 210);
    Console.WriteLine($"force {force}, creux {swell}, sharp {ocean.Sharp:F4}, cpu {ocean.CpuWaveCount}");
    for (int i = 0; i < ocean.CpuWaveCount; i++)
    {
        var w = ocean.Waves[i];
        Console.WriteLine(FormattableString.Invariant(
            $"  {w.Amp:F7} {w.K:F9} {w.Dx:F8} {w.Dz:F8} {w.Omega:F8} {w.Q:F8}"));
    }
}

void Gale()
{
    double force = args.Length > 1 ? double.Parse(args[1], CultureInfo.InvariantCulture) : 9.9;
    double swell = args.Length > 2 ? double.Parse(args[2], CultureInfo.InvariantCulture) : 1.35;
    string name = args.Length > 3 ? args[3] : "schooner";

    var spec = ShipSpec.FromJson(File.ReadAllText(Path.Combine(shipsDir, name + ".json")));
    var lines = new HullLines(spec);
    var phys = new ShipPhysics(spec, lines);

    var ocean = new Ocean { Swell = swell, Time = 0 };
    ocean.SetSeaState(force, 210);
    var ctrl = new Controls { Throttle = 0, Rudder = 0, Sheet = 0, SailsSet = false };
    phys.Settle(ocean, ctrl);

    // --- la mer elle-même ---
    const int N = 60;
    const double SPAN = 900;
    double s = 0, s2 = 0, lo = 1e9, hi = -1e9;
    int n = 0;
    for (int i = 0; i < N; i++)
        for (int j = 0; j < N; j++)
        {
            double x = (i / (double)(N - 1) - 0.5) * SPAN;
            double z = (j / (double)(N - 1) - 0.5) * SPAN;
            double y = ocean.Sample(x, z, 0);
            s += y; s2 += y * y;
            if (y < lo) lo = y;
            if (y > hi) hi = y;
            n++;
        }
    double mean = s / n;
    double hs = 4 * Math.Sqrt(Math.Max(0, s2 / n - mean * mean));

    double longest = 0, biggest = 0;
    foreach (var w in ocean.Waves) { longest = Math.Max(longest, w.L); biggest = Math.Max(biggest, w.Amp); }

    Console.WriteLine($"force {force}, creux {swell} — sharp {ocean.Sharp:F3}");
    Console.WriteLine($"  Hs mesuree      {hs:F2} m");
    Console.WriteLine($"  crete a creux   {hi - lo:F2} m   (de {lo:F2} a {hi:F2})");
    Console.WriteLine($"  plus longue     {longest:F0} m");
    Console.WriteLine($"  plus grosse amp {biggest:F2} m");

    // --- et ce que la coque en fait ---
    var b = phys.Body;
    double dt = 1.0 / 120, t = 0;
    double hMax = 0, tMax = 0, yLo = 1e9, yHi = -1e9, subLo = 1, subHi = 0;
    double h2 = 0, p2 = 0, y1 = 0, y2 = 0;
    int m = 0;
    /* DIX MINUTES, ET DES ÉCARTS-TYPES — la fenêtre de quatre-vingts secondes
       mentait. Une mer à dix-huit composantes ne montre en 80 s que cinq ou six
       périodes de sa houle dominante, et l'extrême qu'on y lit dépend surtout de
       la réalisation : relevé sur le même chaland, force 9,9, creux 2,6, le MÊME
       code JavaScript donnait 17 à 39 m de pilonnement selon l'origine choisie.
       Et l'origine zéro, où ce labo démarre, phases nulles, en est une calme —
       19 m, là où la page en montrait 36. On a cru à un solveur deux fois trop
       mou ; sur 600 s les sept réalisations tombent toutes à 6,8° de roulis
       RMS et 7 m de pilonnement RMS, origine zéro comprise. */
    for (int i = 0; i < 120 * 600; i++)
    {
        phys.Step(dt, ocean, ctrl, t);
        t += dt;
        if (t < 10) continue;                  // on laisse la mer la prendre

        Vec3d up = b.Quat.Rotate(new Vec3d(0, 1, 0));
        Vec3d fwd = b.Quat.Rotate(new Vec3d(0, 0, 1));
        Vec3d right = b.Quat.Rotate(new Vec3d(-1, 0, 0));
        double heel = Math.Atan2(-right.Y, up.Y) * 180 / Math.PI;
        double trim = Math.Asin(Math.Clamp(fwd.Y, -1, 1)) * 180 / Math.PI;
        hMax = Math.Max(hMax, Math.Abs(heel));
        tMax = Math.Max(tMax, Math.Abs(trim));
        h2 += heel * heel; p2 += trim * trim; y1 += b.Pos.Y; y2 += b.Pos.Y * b.Pos.Y; m++;
        yLo = Math.Min(yLo, b.Pos.Y); yHi = Math.Max(yHi, b.Pos.Y);
        subLo = Math.Min(subLo, phys.SubmergedFrac);
        subHi = Math.Max(subHi, phys.SubmergedFrac);
    }
    Console.WriteLine();
    double yMean = y1 / m;
    Console.WriteLine($"  {spec.Name}, 600 s dans cette mer :");
    Console.WriteLine($"  roulis          RMS {Math.Sqrt(h2 / m):F2} deg   max {hMax:F1} deg");
    Console.WriteLine($"  tangage         RMS {Math.Sqrt(p2 / m):F2} deg   max {tMax:F1} deg");
    Console.WriteLine($"  pilonnement     RMS {Math.Sqrt(Math.Max(0, y2 / m - yMean * yMean)):F2} m     amplitude {yHi - yLo:F2} m");
    Console.WriteLine($"  immersion       {subLo * 100:F1} a {subHi * 100:F1} %");
}
