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
    case "embrun": Embrun(); break;
    case "canon": Canon(); break;
    case "ports": Ports(); break;
    case "quete": Quete(); break;
    case "ville": Ville(); break;
    case "traversees": Traversees(); break;
    case "elan": Elan(); break;
    case "baleine": Baleine(); break;
    case "estime": Estime(); break;
    case "serpent": Serpent(); break;
    case "latine": Latine(); break;
    case "bordee": Bordee(); break;
    case "allures": Allures(); break;
    case "ris": Ris(); break;
    case "calibres": Calibres(); break;
    case "anneaux": Anneaux(); break;
    case "saut": Saut(); break;
    case "filins": Filins(); break;
    case "mouillage": Mouillage(); break;
    case "vent": Vent(); break;
    default:
        Console.Error.WriteLine($"mode inconnu : {mode}");
        return 1;
}
return 0;

/* LE VOL DU BOULET, ÉPROUVÉ contre les chiffres que guns.js annonce pour un calibre
   de référence (k = 1) : 296 m/s restants et 1,46 s pour 500 m, plus de dix mètres
   de chute là-dessus, 1,2 m à 200. Un boulet lâché à plat à 6,5 m sur une mer
   plate, sans cible : on relève sa course. */
void Canon()
{
    var ocean = new Ocean { Swell = 1.35, Time = 0 };
    ocean.SetSeaState(0, 0);
    var g = new Gunnery();
    var shot = new Gunnery.Shot { P = new Vec3d(0, 6.5, 0), V = new Vec3d(440, 0, 0), C = 0.00097, K = 1 };
    g.Shots.Add(shot);
    double t = 0, dt = 1.0 / 240;
    bool r200 = false, r500 = false;
    while (g.Shots.Count > 0 && t < 12)
    {
        g.Update(dt, ocean, t);
        t += dt;
        if (!r200 && shot.P.X >= 200) { r200 = true; Console.WriteLine($"200 m : {t:F3} s, {shot.V.Length:F0} m/s, chute {6.5 - shot.P.Y:F2} m"); }
        if (!r500 && shot.P.X >= 500) { r500 = true; Console.WriteLine($"500 m : {t:F3} s, {shot.V.Length:F0} m/s, chute {6.5 - shot.P.Y:F2} m"); }
    }
    Console.WriteLine($"dans l'eau à {shot.P.X:F0} m, au bout de {t:F2} s");
}

/* L'EMBRUN CONTRE LA COQUE, ÉPROUVÉ. Des gerbes lancées au ras du bordé, des
   deux bords et sur toute la longueur ; à chaque pas on compte les paquets qui
   se trouvent DANS le volume de la coque — ce que l'on voyait depuis la chambre
   du capitaine. Sans obstacle, puis avec.
     dotnet run --project core/NavalSim.Lab -c Release -- embrun schooner */
void Embrun()
{
    string name = args.Length > 1 ? args[1] : "schooner";
    var spec = ShipSpec.FromJson(File.ReadAllText(Path.Combine(shipsDir, name + ".json")));
    var lines = new HullLines(spec);
    var phys = new ShipPhysics(spec, lines);
    var ocean = new Ocean { Swell = 1.35, Time = 0 };
    ocean.SetSeaState(0, 0);
    phys.Settle(ocean, new Controls());
    var prof = HullProfile.Procedural(spec, lines);
    Func<double, double> top = z => lines.DeckY(Math.Clamp(z / spec.L + 0.5, 0, 1));
    Func<double, double> keel = z => lines.KeelY(Math.Clamp(z / spec.L + 0.5, 0, 1));
    var col = new HullCollider(phys, prof, top, keel);

    // le même test que l'obstacle, sans rien corriger : dedans ou pas
    bool Inside(Vec3d p)
    {
        var b = phys.Body;
        Vec3d l = b.Quat.Inverted().Rotate(p - b.Pos);
        if (l.Z < prof.EndAft || l.Z > prof.EndFwd) return false;
        double u = Math.Clamp(l.Z / prof.HalfLen * 0.5 + 0.5, 0, 1) * prof.Fractions.Length - 0.5;
        int n = prof.Fractions.Length;
        int i0 = Math.Clamp((int)Math.Floor(u), 0, n - 1), i1 = Math.Min(i0 + 1, n - 1);
        double ft = Math.Clamp(u - Math.Floor(u), 0, 1);
        double hb = (prof.Fractions[i0] * (1 - ft) + prof.Fractions[i1] * ft) * prof.MaxHalfB;
        return Math.Abs(l.X) < hb * 0.98 && l.Y < top(l.Z) - 0.05 && l.Y > keel(l.Z) + 0.05;
    }

    foreach (bool with in new[] { false, true })
    {
        var pool = new SprayPool(new Random(11));
        if (with) pool.Colliders.Add(col);
        long inside = 0, seen = 0;
        for (int burst = 0; burst < 40; burst++)
        {
            double z = (burst % 10 - 4.5) / 10.0 * spec.L * 0.8;
            double side = burst % 2 == 0 ? 1 : -1;
            double hb = prof.MaxHalfB * 1.06;
            pool.Burst(new Vec3d(side * hb, 0, z), 6, 4);
            for (int s = 0; s < 120; s++)
            {
                pool.Update(1.0 / 60);
                for (int i = 0; i < pool.Count; i++)
                {
                    seen++;
                    if (Inside(new Vec3d(pool.Pos[i * 3], pool.Pos[i * 3 + 1], pool.Pos[i * 3 + 2]))) inside++;
                }
            }
        }
        Console.WriteLine($"  {(with ? "avec l'obstacle " : "sans obstacle   ")}  {inside,7} paquets·image dans la coque sur {seen} ({100.0 * inside / Math.Max(1, seen):F2} %)");
    }
}

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

/* CE QUE VAUT UN ÉTAT DE MER, ET CE QU'IL FAIT AU NAVIRE.
 *
 * Les deux questions sont posées ensemble parce qu'elles se répondent l'une
 * l'autre : une houle très creuse mais très longue soulève une coque en bloc
 * sans la travailler, et l'on peut donc avoir treize mètres de hauteur
 * significative pour cinq degrés de roulis. Mesurer la mer seule laisserait
 * croire à une tempête que le navire ne sent pas. */
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

/* LES PORTS D UNE FICHE DE REGION : est-ce qu ils naissent, et ce qu on trouve
   au bout de leur ponton. Un port se donne par un point de ville et le
   RELEVEMENT que regarde le quai ; tout le reste -- le rivage, la longueur de
   la jetee, l eau sous sa tete -- est trouve dans l image. Autant le lire avant
   d ecrire une quete qui y envoie le joueur. */
void Ports()
{
    string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
    string sheet = args.Length > 1 ? args[1] : Path.Combine(root, "world", "caraibes.json");
    var region = RegionSpec.FromJson(File.ReadAllText(sheet));
    var (w, h, grey) = GreyPng.Decode(File.ReadAllBytes(Path.Combine(root, region.Relief.Image)));
    var world = new World(region, w, h, grey, m => Console.WriteLine("  ! " + m));

    Console.WriteLine($"{region.Name} : {world.Isles.Count} port(s) nes de la fiche");
    Console.WriteLine($"{"port",-18}{"lat/lon",-22}{"rivage",8}{"jetee",7}{"tete",8}{"abri",7}  plus proche");
    foreach (var i in world.Isles)
    {
        var g = world.Geo.ToXZ(i.Lat, i.Lon);
        double toShore = Math.Sqrt((i.X - g.X) * (i.X - g.X) + (i.Z - g.Z) * (i.Z - g.Z));
        double head = world.HeightAt(i.Port.Hx, i.Port.Hz);
        double shelter = world.Shelter(i.Port.Hx, i.Port.Hz);
        string near = "";
        double best = double.MaxValue;
        foreach (var j in world.Isles)
        {
            if (ReferenceEquals(i, j)) continue;
            double d = Math.Sqrt((i.X - j.X) * (i.X - j.X) + (i.Z - j.Z) * (i.Z - j.Z));
            if (d < best) { best = d; near = j.Key; }
        }
        Console.WriteLine($"{i.Key,-18}{i.Lat,8:F3} {i.Lon,9:F3}    {toShore,6:F0} m{i.Port.Reach,6:F0} m{head,7:F1} m{shelter,6:F2}   {near} a {best / 1852:F1} M");
    }
}

/* UNE QUETE, LUE SUR LE TERRAIN : ou tombe chaque etape, ce qu il y a d eau
   dessous, et le chemin d une etape a la suivante. Une consigne peut etre
   parfaitement ecrite et poser son cercle sur un haut-fond ou sur la terre --
   personne ne le verrait avant d y envoyer un navire. */
void Quete()
{
    string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
    var region = RegionSpec.FromJson(File.ReadAllText(Path.Combine(root, "world", "caraibes.json")));
    var (w, h, grey) = GreyPng.Decode(File.ReadAllBytes(Path.Combine(root, region.Relief.Image)));
    var world = new World(region, w, h, grey, m => Console.WriteLine("  ! " + m));
    var Q = new Quests(world);

    string dir = Path.Combine(root, "quests");
    foreach (string f in Directory.GetFiles(dir, "*.json"))
        if (Path.GetFileName(f) != "index.json")
            Q.Add(QuestSpec.FromJson(File.ReadAllText(f)), m => Console.WriteLine("  ! " + m));

    foreach (var q in Q.List)
    {
        if (args.Length > 1 && q.Id != args[1]) continue;
        Console.WriteLine();
        Console.WriteLine($"{q.Title} ({q.Id}) -- {q.Steps.Count} etape(s)");
        Console.WriteLine($"  {"etape",-32}{"objectif",-8}{"rayon",7}{"fond",9}{"rivage",9}   du precedent");
        double px = 0, pz = 0;
        bool first = true;
        foreach (var step in q.Steps)
        {
            var pl = Q.Place(step, m => Console.WriteLine("  ! " + m));
            double bed = world.HeightAt(pl.X, pl.Z);
            double shore = world.ShoreDistance(pl.X, pl.Z);
            string leg = first ? "" : $"{Math.Sqrt((pl.X - px) * (pl.X - px) + (pl.Z - pz) * (pl.Z - pz)) / 1852,7:F2} M";
            string title = step.Title.Length > 28 ? step.Title.Substring(0, 28) + "..." : step.Title;
            Console.WriteLine($"  {title,-32}{step.Goal.ToString().ToLowerInvariant(),-8}{step.R,6:F0} m{-bed,7:F1} m{shore,7:F0} m   {leg}");
            px = pl.X; pz = pl.Z; first = false;
        }
    }
}

/* OU SE POSENT LES MAISONS, et a quelle hauteur. Une ville qui a l air de
   flotter peut venir de trois endroits -- le semis, le relief, ou le rendu --
   et ce mode repond pour les deux premiers. */
void Ville()
{
    string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
    var region = RegionSpec.FromJson(File.ReadAllText(Path.Combine(root, "world", "caraibes.json")));
    var (w, h, grey) = GreyPng.Decode(File.ReadAllBytes(Path.Combine(root, region.Relief.Image)));
    var world = new World(region, w, h, grey);
    string key = args.Length > 1 ? args[1] : "port-royal";
    var isl = world.ByKey(key);
    if (isl == null) { Console.WriteLine($"port inconnu : {key}"); return; }

    var houses = Town.Plant(world, isl.X, isl.Z, 420, 150, 7920);
    Console.WriteLine($"{isl.Name} : {houses.Count} maison(s)");
    double lo = 1e9, hi = -1e9, sum = 0; int sous = 0;
    foreach (var m in houses)
    {
        double bed = world.HeightAt(m.X, m.Z);
        if (m.Y < lo) lo = m.Y;
        if (m.Y > hi) hi = m.Y;
        sum += m.Y;
        if (m.Y < 1) sous++;
    }
    Console.WriteLine($"  hauteur des seuils : {lo:F2} a {hi:F2} m, moyenne {sum / Math.Max(1, houses.Count):F2} m, {sous} sous 1 m");
    Console.WriteLine($"  {"maison",-8}{"x",10}{"z",10}{"seuil",8}{"relief",9}{"ecart",8}");
    for (int i = 0; i < Math.Min(8, houses.Count); i++)
    {
        var m = houses[i];
        double bed = world.HeightAt(m.X, m.Z);
        Console.WriteLine($"  {i,-8}{m.X,10:F0}{m.Z,10:F0}{m.Y,8:F2}{bed,9:F2}{m.Y - bed,8:F2}");
    }
}
/* LES TRAVERSEES : chaque region de world/, ses atterrages lus sur le terrain
   (de l eau libre, loin de la cote ?), puis ce que coute d aller de l une a
   l autre par les alizes (vent d est-nord-est, 75°) et par le vent contraire.
   Un atterrage pose sur un haut-fond ferait arriver le joueur echoue. */
void Traversees()
{
    string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
    var regions = new List<(RegionSpec R, World W)>();
    foreach (var f in Directory.GetFiles(Path.Combine(root, "world"), "*.json"))
    {
        var region = RegionSpec.FromJson(File.ReadAllText(f));
        region.Key = Path.GetFileNameWithoutExtension(f);
        var (w, h, grey) = GreyPng.Decode(File.ReadAllBytes(Path.Combine(root, region.Relief.Image)));
        regions.Add((region, new World(region, w, h, grey, m => Console.WriteLine("  ! " + m))));
    }
    foreach (var (r, w) in regions)
    {
        Console.WriteLine($"{r.Name} ({r.Key}) : {r.Approaches.Count} atterrage(s)");
        foreach (var a in r.Approaches)
        {
            var g = w.Geo.ToXZ(a.Lat, a.Lon);
            Console.WriteLine($"  {a.Name,-44} fond {-w.HeightAt(g.X, g.Z),5:F0} m, cote a {w.ShoreDistance(g.X, g.Z),6:F0} m{(w.ShoreDistance(g.X, g.Z) < Passage.Offing ? "  ! EN DECA DU LARGE" : "")}");
        }
    }
    foreach (var (a, wa) in regions)
        foreach (var (b, _) in regions)
        {
            if (ReferenceEquals(a, b) || wa.StartPort is not { } home) continue;
            foreach (double wind in new[] { 75.0, 255.0 })
            {
                var p = Passage.PlanTo(b, home.Lat, home.Lon, wind);
                if (p == null) continue;
                Console.WriteLine($"{home.Name} -> {b.Name}, vent du {wind:F0} : {p.Miles:F0} M au {p.Course:F0} ({Passage.ToPoint(p.Course)}), {p.Knots:F1} nd, {Passage.Say(p.Hours)} -- {p.At.Name}");
            }
        }
}

/* L ELAN DE B : la vitesse de regime a 45 et 180 fois la poussee, mer calme,
   sans toile. Pour savoir ce qu un double appui fait vraiment.
     dotnet run --project core/NavalSim.Lab -- elan frigate17e */
void Elan()
{
    string name = args.Length > 1 ? args[1] : "frigate17e";
    var spec = ShipSpec.FromJson(File.ReadAllText(Path.Combine(shipsDir, name + ".json")));
    foreach (double thr in new[] { 1.0, 45.0, 180.0 })
    {
        var ocean = new Ocean { Swell = 1.0, Time = 0 };
        ocean.SetSeaState(1, 210);
        var p = new ShipPhysics(spec, new HullLines(spec));
        var ctrl = new Controls { Throttle = thr, Rudder = 0, Sheet = 0.6, SailsSet = false };
        p.Settle(ocean, ctrl);
        double dt = 1.0 / 60, t = 0;
        for (int k = 0; k < 60 * 480; k++) { p.Step(dt, ocean, ctrl, t); t += dt; }
        var v = p.Body.Vel;
        var fwd = p.Body.Quat.Rotate(new Vec3d(0, 0, 1));
        double pitch = Math.Asin(Math.Clamp(fwd.Y, -1, 1)) * 180 / Math.PI;
        Console.WriteLine($"{name} poussee x{thr,4:F0} : {Math.Sqrt(v.X * v.X + v.Z * v.Z) / 0.5144,6:F1} noeuds apres 8 min, assiette {pitch:F1} deg, immersion {p.SubmergedFrac * 100:F0} %, y {p.Body.Pos.Y:F2} m");
    }
}

/* LA BALEINE, EPROUVEE : une fregate en mer calme, une baleine de chaque
   humeur appelee a 800 m, et ce qui arrive -- ses etats, son passage sous la
   quille, le coup de boutoir (impulsion, voie d'eau).
     dotnet run --project core/NavalSim.Lab -- baleine */
void Baleine()
{
    var spec = ShipSpec.FromJson(File.ReadAllText(Path.Combine(shipsDir, "frigate17e.json")));
    foreach (var mood in new[] { WhaleMood.Indifferent, WhaleMood.Curious, WhaleMood.Hostile })
    {
        var ocean = new Ocean { Swell = 1.0, Time = 0 };
        ocean.SetSeaState(2, 210);
        var p = new ShipPhysics(spec, new HullLines(spec));
        var ctrl = new Controls { Throttle = 0, Rudder = 0, Sheet = 0.6, SailsSet = false };
        p.Settle(ocean, ctrl);
        var w = new Whale(new WhaleSettings { PerHour = 0 }, 7);
        var st = w.State;
        double t = 0, minD = 1e9, minUnder = 1e9;
        w.Event = e => Console.WriteLine($"  {t,6:F0} s  evenement {e}");
        w.Spout = (at, d) => { };
        w.OnRam = r => Console.WriteLine($"  {t,6:F0} s  COUP : {r.Speed:F1} m/s, navire +{r.DeltaV:F2} m/s, voie d'eau {r.Area:F2} m2 au compartiment {r.Comp}");
        w.Summon(p, mood, 800, null);
        double dt = 1.0 / 60;
        for (int k = 0; k < 60 * 600 && w.State != WhaleState.Absent; k++)
        {
            p.Step(dt, ocean, ctrl, t);
            w.Update(dt, p, ocean, t, null, null);
            t += dt;
            if (w.State != st) { Console.WriteLine($"  {t,6:F0} s  {st} -> {w.State}, a {Dist():F0} m, profondeur {w.Y:F1} m"); st = w.State; }
            double d = Dist();
            if (d < minD) minD = d;
            if (d < 20) minUnder = Math.Min(minUnder, w.Y);
        }
        double Dist() => Math.Sqrt(Math.Pow(w.Pos.X - p.Body.Pos.X, 2) + Math.Pow(w.Pos.Z - p.Body.Pos.Z, 2));
        Console.WriteLine($"{mood} : au plus pres {minD:F0} m{(minUnder < 1e8 ? $", profondeur a l'aplomb {minUnder:F1} m" : "")}, coups {w.Rams}, voies d'eau {p.Breaches.Count}, fin a {t:F0} s\n");
    }
}

/* L ESTIME, EPROUVEE : cent navires tires au hasard (leurs instruments) courent
   20 km de jeu a 3 m/s au 060, avec 4 degres de derive sous le vent. L erreur
   vraie a l arrivee, comparee a l incertitude que l estime annonce : si elle
   est honnete, deux tiers des erreurs tombent dans un ecart-type.
     dotnet run --project core/NavalSim.Lab -- estime */
void Estime()
{
    var rules = new ReckoningSettings();
    double glass = 15, dt = 0.25, speed = 3, heading = 60, leeway = 4;
    var errs = new List<double>();
    double sig = 0;
    int inside = 0;
    for (int seed = 1; seed <= 100; seed++)
    {
        var r = new Reckoning(rules, seed);
        r.Fix(0, 0);
        double x = 0, z = 0, track = (heading + leeway) * Math.PI / 180;
        double vx = -Math.Sin(track) * speed, vz = Math.Cos(track) * speed;
        for (double t = 0; t < 20000 / speed; t += dt)
        {
            r.Step(dt, vx, vz, heading, glass);
            x += vx * dt; z += vz * dt;
        }
        double e = Math.Sqrt((r.X - x) * (r.X - x) + (r.Z - z) * (r.Z - z));
        sig = Math.Sqrt(r.SigN * r.SigN + r.SigE * r.SigE);
        errs.Add(e);
        if (e < sig) inside++;
    }
    errs.Sort();
    Console.WriteLine($"20 km courus : erreur mediane {errs[50]:F0} m, 90e centile {errs[90]:F0} m, pire {errs[^1]:F0} m");
    Console.WriteLine($"incertitude annoncee {sig:F0} m (les deux axes) ; {inside} erreurs sur 100 dedans");
    // sans derive, pour la part des instruments seuls
    var r2 = new Reckoning(rules, 3); r2.Fix(0, 0);
    Console.WriteLine($"derive de {leeway} degres sur 20 km : {20000 * Math.Sin(leeway * Math.PI / 180):F0} m a elle seule");
}

/* LE SERPENT, EPROUVE : une fregate immobile sous la pluie, le serpent appele,
   trois minutes -- ses etats, ses coups, les voies d eau, la longueur de son
   corps (qui doit rester sa longueur), et la pluie qui cesse.
     dotnet run --project core/NavalSim.Lab -- serpent */
void Serpent()
{
    var spec = ShipSpec.FromJson(File.ReadAllText(Path.Combine(shipsDir, "frigate17e.json")));
    var ocean = new Ocean { Swell = 1.0, Time = 0 };
    ocean.SetSeaState(3, 210);
    var p = new ShipPhysics(spec, new HullLines(spec));
    var ctrl = new Controls { Throttle = 0, Rudder = 0, Sheet = 0.6, SailsSet = false };
    p.Settle(ocean, ctrl);
    var s = new SeaSerpent(new SerpentSettings(), 11);
    double t = 0;
    var st = s.State;
    s.Event = e => Console.WriteLine($"  {t,6:F0} s  evenement {e}");
    s.OnStrike = k => Console.WriteLine($"  {t,6:F0} s  COUP {k.Kind} : navire +{k.DeltaV:F2} m/s, voie d'eau {k.Area:F2} m2");
    s.Summon(p, null);
    double dt = 1.0 / 60, minD = 1e9, maxRise = -1e9;
    for (int k = 0; k < 60 * 240 && s.State != SerpentState.Absent; k++)
    {
        double wet = t < 180 ? 0.8 : 0;          // la pluie cesse a trois minutes
        p.Step(dt, ocean, ctrl, t);
        s.Update(dt, p, ocean, t, wet, null, null);
        t += dt;
        if (s.State != st) { Console.WriteLine($"  {t,6:F0} s  {st} -> {s.State}"); st = s.State; }
        double d = Math.Sqrt(Math.Pow(s.Head.X - p.Body.Pos.X, 2) + Math.Pow(s.Head.Z - p.Body.Pos.Z, 2));
        minD = Math.Min(minD, d);
        maxRise = Math.Max(maxRise, s.Head.Y);
        if (double.IsNaN(s.Head.X) || double.IsNaN(s.Spine[^1].X)) { Console.WriteLine("  NaN !"); break; }
    }
    double len = 0;
    for (int i = 1; i < SeaSerpent.N; i++) len += (s.Spine[i] - s.Spine[i - 1]).Length;
    Console.WriteLine($"au plus pres {minD:F0} m, tete au plus haut {maxRise:F1} m, voies d'eau {p.Breaches.Count}, corps {len:F1} m, fin {s.State} a {t:F0} s");
}

/* LA LATINE, EPROUVEE : le Roter Lowe, avec et sans sa latine d artimon, a
   plusieurs angles du vent vrai, ecoutes des carres a leur reglage optimal ;
   la vitesse apres quatre minutes, et l angle ou l equipage a mis la latine.
     dotnet run --project core/NavalSim.Lab -- latine */
void Latine()
{
    string json = File.ReadAllText(Path.Combine(shipsDir, "frigate17e.json"));
    var with = ShipSpec.FromJson(json);
    var without = ShipSpec.FromJson(System.Text.RegularExpressions.Regex.Replace(json, "\"lateen\":[^}]*},", ""));
    var centred = ShipSpec.FromJson(json.Replace("\"ceHeight\": 7.5, \"ceZ\": -10", "\"ceHeight\": 9.75, \"ceZ\": -0.5"));
    foreach (double off in new[] { 50.0, 60.0, 75.0, 90.0, 120.0 })
    {
        string line = $"vent a {off,3:F0} deg :";
        foreach (var (name, spec) in new[] { ("avec", with), ("sans", without), ("centree", centred) })
        {
            var ocean = new Ocean { Swell = 1.0, Time = 0 };
            ocean.SetSeaState(5, 0);                  // le vent vient du nord
            var p = new ShipPhysics(spec, new HullLines(spec));
            var ctrl = new Controls { Throttle = 0, Rudder = 0, Sheet = 0.3, SailsSet = true };
            p.Settle(ocean, ctrl);
            // le cap : off degres du lit du vent, l est est -x
            double yaw = -off * Math.PI / 180;
            p.Body.Quat = Quatd.FromAxisAngle(new Vec3d(0, 1, 0), yaw);
            double dt = 1.0 / 60, t = 0;
            for (int k = 0; k < 60 * 240; k++)
            {
                if (p.OptSheet is double o) ctrl.Sheet = o;
                // la barre tient le cap : ce qui compte est la vitesse, pas la derive de route
                var f = p.Body.Quat.Rotate(new Vec3d(0, 0, 1));
                double hdg = Math.Atan2(f.X, f.Z);
                ctrl.Rudder = Math.Clamp(-(Math.IEEERemainder(yaw - hdg, 2 * Math.PI)) * 3, -1, 1);
                p.Step(dt, ocean, ctrl, t); t += dt;
            }
            var v = p.Body.Vel;
            line += $"  {name} {Math.Sqrt(v.X * v.X + v.Z * v.Z) / 0.5144,5:F2} nd (barre {ctrl.Rudder,5:F2}{(name == "avec" ? $", latine a {p.LateenAngle * 180 / Math.PI:F0} deg" : "")})";
        }
        Console.WriteLine(line);
    }
}

/* DEUX BORDÉES REÇUES : vingt-quatre coups au flanc d'une frégate du XVIIe, en
   mer calme, aux hauteurs où un boulet frappe un bordé (tirées au hasard, graine
   fixe). On relève l'eau embarquée et la gîte, minute par minute, pour
   plusieurs tailles de trou.
     dotnet run --project core/NavalSim.Lab -c Release -- bordee */
void Bordee()
{
    string json = File.ReadAllText(Path.Combine(shipsDir, "frigate17e.json"));
    var spec = ShipSpec.FromJson(json);
    foreach (double area in new[] { 0.10, 0.025 })
    foreach (bool plug in new[] { false, true })
    {
        var ocean = new Ocean { Swell = 1.0, Time = 0 };
        ocean.SetSeaState(3, 0);
        var p = new ShipPhysics(spec, new HullLines(spec));
        var ctrl = new Controls { Throttle = 0, Rudder = 0, Sheet = 0.6, SailsSet = false };
        p.Settle(ocean, ctrl);
        if (!plug) p.PlugEvery = 0;
        var rng = new Random(7);
        int below = 0;
        for (int i = 0; i < 24; i++)
        {
            int c = rng.Next(p.Comps.Length);
            // de trente centimètres sous l eau au pont : la muraille qu un boulet tiré à hauteur de batterie trouve
            var cc = p.Comps[c];
            double yHit = -0.3 - p.Body.Pos.Y + (cc.DeckY + 0.3 + p.Body.Pos.Y) * rng.NextDouble();
            double frac = (yHit - cc.KeelY) / (cc.DeckY - cc.KeelY);
            var br = p.MakeBreach(c, area, frac, 1);
            if (br != null && br.Y < 0) below++;
        }
        Console.WriteLine($"trou {area:F3} m2, charpentier {(plug ? "oui" : "non")} : {p.Breaches.Count} breches, {below} sous la flottaison, pompes {p.PumpRate * 60:F1} m3/min, {p.Comps.Length} compartiments");
        double dt = 1.0 / 60, t = 0;
        for (int m = 1; m <= 10; m++)
        {
            for (int k = 0; k < 60 * 60; k++) { p.Step(dt, ocean, ctrl, t); t += dt; }
            var up = p.Body.Quat.Rotate(new Vec3d(0, 1, 0));
            Console.WriteLine($"  {m,2} min : {p.Breaches.Count,2} breches, eau {p.FloodTonnes,6:F1} t, debit {p.FloodRate * 60,6:F1} m3/min, gite {Math.Asin(Math.Clamp(up.X, -1, 1)) * 180 / Math.PI,6:F1} deg, y {p.Body.Pos.Y,6:F2}{(p.Foundered ? " COULE" : "")}");
        }
    }
}

/* CE QU'ELLE DONNE, ALLURE PAR ALLURE ET PAR FORCE : toile établie, écoutes au
   mieux, barre tenue sur le cap, quatre minutes de décantation.
     dotnet run --project core/NavalSim.Lab -c Release -- allures frigate17e */
void Allures()
{
    string name = args.Length > 1 ? args[1] : "frigate17e";
    var spec = ShipSpec.FromJson(File.ReadAllText(Path.Combine(shipsDir, name + ".json")));
    Console.WriteLine($"{spec.Name} : L {spec.L:F0} m, voilure {spec.SailArea:F0} m2");
    foreach (double force in new[] { 2.0, 3.0, 4.0, 5.0 })
    {
        string line = $"force {force:F0} :";
        foreach (double off in new[] { 45.0, 60.0, 90.0, 135.0, 180.0 })
        {
            var ocean = new Ocean { Swell = 1.0, Time = 0 };
            ocean.SetSeaState(force, 0);
            var p2 = new ShipPhysics(spec, new HullLines(spec));
            var ctrl = new Controls { Throttle = 0, Rudder = 0, Sheet = 0.3, SailsSet = true };
            p2.Settle(ocean, ctrl);
            double yaw = -off * Math.PI / 180;
            p2.Body.Quat = Quatd.FromAxisAngle(new Vec3d(0, 1, 0), yaw);
            double dt = 1.0 / 60, t = 0;
            for (int k = 0; k < 60 * 240; k++)
            {
                if (p2.OptSheet is double o) ctrl.Sheet = o;
                var f = p2.Body.Quat.Rotate(new Vec3d(0, 0, 1));
                double hdg = Math.Atan2(f.X, f.Z);
                ctrl.Rudder = Math.Clamp(-(Math.IEEERemainder(yaw - hdg, 2 * Math.PI)) * 3, -1, 1);
                p2.Step(dt, ocean, ctrl, t); t += dt;
            }
            var v = p2.Body.Vel;
            line += $"  {off,3:F0} deg {Math.Sqrt(v.X * v.X + v.Z * v.Z) / 0.5144,5:F2} nd";
        }
        Console.WriteLine(line);
    }
}

/* CE QUE COÛTE ET CE QUE RAPPORTE UN RIS : la même coque au travers, toute la
   toile, aux huniers, aux huniers au bas ris, par petit temps et par gros.
     dotnet run --project core/NavalSim.Lab -c Release -- ris frigate17e */
void Ris()
{
    string name = args.Length > 1 ? args[1] : "frigate17e";
    var spec = ShipSpec.FromJson(File.ReadAllText(Path.Combine(shipsDir, name + ".json")));
    Console.WriteLine($"{spec.Name}, vent par le travers");
    foreach (double force in new[] { 3.0, 6.0, 8.0 })
    {
        string line = $"force {force:F0} :";
        foreach (double canvas in new[] { 1.0, 0.6, 0.35 })
        {
            var ocean = new Ocean { Swell = 1.0, Time = 0 };
            ocean.SetSeaState(force, 0);
            var p2 = new ShipPhysics(spec, new HullLines(spec));
            var ctrl = new Controls { Throttle = 0, Rudder = 0, Sheet = 0.3, SailsSet = true, Canvas = canvas };
            p2.Settle(ocean, ctrl);
            double yaw = -90 * Math.PI / 180;
            p2.Body.Quat = Quatd.FromAxisAngle(new Vec3d(0, 1, 0), yaw);
            double dt = 1.0 / 60, t = 0, heel = 0;
            for (int k = 0; k < 60 * 240; k++)
            {
                if (p2.OptSheet is double o) ctrl.Sheet = o;
                var f = p2.Body.Quat.Rotate(new Vec3d(0, 0, 1));
                double hdg = Math.Atan2(f.X, f.Z);
                ctrl.Rudder = Math.Clamp(-(Math.IEEERemainder(yaw - hdg, 2 * Math.PI)) * 3, -1, 1);
                p2.Step(dt, ocean, ctrl, t); t += dt;
                if (k > 60 * 200)
                {
                    var up = p2.Body.Quat.Rotate(new Vec3d(0, 1, 0));
                    heel = Math.Max(heel, Math.Abs(Math.Asin(Math.Clamp(up.X, -1, 1)) * 180 / Math.PI));
                }
            }
            var v = p2.Body.Vel;
            line += $"  {canvas * 100,3:F0} % : {Math.Sqrt(v.X * v.X + v.Z * v.Z) / 0.5144,5:F2} nd, gite {heel,4:F1} deg, toile {p2.SailLoad,5:F1} N/m2 ;";
        }
        Console.WriteLine(line);
    }
}

/* CE QU'UN CALIBRE CHANGE : le meme coup, tire a plat de 6,5 m, pour la bordee
   et pour les pieces de chasse d une fregate.
     dotnet run --project core/NavalSim.Lab -c Release -- calibres */
void Calibres()
{
    foreach (double k in new[] { 1.0, 0.92, 0.88, 0.78 })
    {
        var ocean = new Ocean { Swell = 1.0, Time = 0 };
        ocean.SetSeaState(0, 0);
        var g = new Gunnery();
        // la vitesse a la bouche suit le calibre, comme dans Gunnery.Fire
        var shot = new Gunnery.Shot { P = new Vec3d(0, 6.5, 0), V = new Vec3d(440 * (0.78 + 0.22 * k), 0, 0), C = 0.00097 / k, K = k };
        g.Shots.Add(shot);
        double t = 0, dt = 1.0 / 240, at200 = 0, at400 = 0;
        while (g.Shots.Count > 0 && t < 20)
        {
            double was = shot.P.X;
            g.Update(dt, ocean, t); t += dt;
            if (was < 200 && shot.P.X >= 200) at200 = shot.V.Length;
            if (was < 400 && shot.P.X >= 400) at400 = shot.V.Length;
        }
        Console.WriteLine($"calibre {k:F2} : portee {shot.P.X,4:F0} m, reste {at200,3:F0} m/s a 200 m, {at400,3:F0} a 400 ; trou {0.025 * k * k * 1e4,4:F0} cm2, recul {3000 * k * k,5:F0} kg.m/s");
    }
}

/* LA NORMALE D'UN ANNEAU, EPROUVEE CONTRE DES TORES D'INCLINAISON CONNUE.
   On ne peut pas juger a l'oeil qu'un anneau tourne autour du bon axe : un
   anneau qui tourne autour d'un axe FAUX tourne quand meme, il balaie
   seulement au lieu de pivoter, et par mer formee cela se confond avec le
   roulis. D'ou une epreuve sans rendu, sur des tores dont on sait la reponse.

   Rings.Axis est PURE et partagee avec js/ship-model.js (Naval.ringAxis) ;
   scratchpad n'existe plus d'une session a l'autre, ce banc reste. */
void Anneaux()
{
    (string Nom, Vec3d N, double R, double r)[] cas =
    {
        ("vertical dans l'axe (normale = travers)", new Vec3d(1, 0, 0), 20, 0.7),
        ("vertical en travers (normale = etrave)",   new Vec3d(0, 0, 1), 20, 0.7),
        ("couche a plat (normale = verticale)",      new Vec3d(0, 1, 0), 20, 0.7),
        ("incline 30 deg sur l'axe",                new Vec3d(Math.Cos(Math.PI / 6), Math.Sin(Math.PI / 6), 0), 20, 0.7),
        ("incline 45 deg quelconque",               new Vec3d(0.5, 0.7071, 0.5), 20, 0.7),
        ("incline 12 deg, presque plat",            new Vec3d(Math.Sin(0.209), Math.Cos(0.209), 0), 20, 0.7),
        ("tube epais R=20 r=6",                     new Vec3d(0.3, 0.8, -0.52), 20, 6),
    };
    double pire = 0;
    foreach (var c in cas)
    {
        var n = c.N.Normalized();
        // deux vecteurs de son plan
        var a = Math.Abs(n.X) < 0.9 ? new Vec3d(1, 0, 0) : new Vec3d(0, 1, 0);
        var u = a.Cross(n).Normalized();
        var w = n.Cross(u).Normalized();
        double xx = 0, xy = 0, xz = 0, yy = 0, yz = 0, zz = 0;
        int np = 0;
        for (int p = 0; p < 96; p++)
        {
            double th = 2 * Math.PI * p / 96, cu = Math.Cos(th), su = Math.Sin(th);
            for (int q = 0; q < 16; q++)
            {
                double ph = 2 * Math.PI * q / 16, rad = c.R + c.r * Math.Cos(ph), h = c.r * Math.Sin(ph);
                // le tore est centre sur zero : la covariance se prend telle quelle
                double dx = u.X * cu * rad + w.X * su * rad + n.X * h;
                double dy = u.Y * cu * rad + w.Y * su * rad + n.Y * h;
                double dz = u.Z * cu * rad + w.Z * su * rad + n.Z * h;
                xx += dx * dx; xy += dx * dy; xz += dx * dz;
                yy += dy * dy; yz += dy * dz; zz += dz * dz; np++;
            }
        }
        var got = Rings.Axis(xx / np, xy / np, xz / np, yy / np, yz / np, zz / np);
        double dot = Math.Abs(got.X * n.X + got.Y * n.Y + got.Z * n.Z);
        double deg = Math.Acos(Math.Min(1, dot)) * 180 / Math.PI;
        pire = Math.Max(pire, deg);
        Console.WriteLine($"{c.Nom,-42} ecart {deg,7:F4} deg");
    }
    Console.WriteLine($"\npire ecart : {pire:F4} deg {(pire < 0.5 ? "-> OK" : "-> NON")}");
}

/* LE SAUT PAR LES ANNEAUX, EPROUVE SUR LE VRAI RELIEF.

   Un refus qui ne refuse pas est la panne qu on ne verrait jamais en jouant :
   on saute, on arrive dans une colline, et l on met vingt minutes a comprendre
   que c est la REGLE qui avait dit oui. On la passe donc sur des points dont on
   sait la reponse : le milieu d un port (a terre autour), le large, et une
   couronne de points a distance croissante d une cote. */
void Saut()
{
    string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
    var region = RegionSpec.FromJson(File.ReadAllText(Path.Combine(root, "world", "caraibes.json")));
    var (w, h, grey) = GreyPng.Decode(File.ReadAllBytes(Path.Combine(root, region.Relief.Image)));
    var world = new World(region, w, h, grey, m => Console.WriteLine("  ! " + m));

    // la Roter Lowe : 30 m de coque, 3,85 m sous la flottaison
    const double L = 30, draft = 3.85;
    Console.WriteLine($"charge {Teleport.Charge:F0} s, rond d eau exige {Math.Max(10, L * Teleport.Clearance):F0} m");
    Console.WriteLine();

    var port = world.Isles[0];
    Console.WriteLine($"depuis {port.Name} :");
    (string Quoi, double X, double Z)[] cas =
    {
        ("le sommet de son ile",   port.X, port.Z),
        ("la tete de sa jetee",    port.Port.Hx, port.Port.Hz),
    };
    foreach (var c in cas)
        Console.WriteLine($"   {c.Quoi,-26} fond {world.HeightAt(c.X, c.Z),7:F1} m  ->  {Teleport.Check(world, c.X, c.Z, draft, L)}");

    /* UNE COURONNE : on s ecarte de la tete de jetee vers le large et l on
       regarde a quelle distance la regle passe du refus a l accord. */
    Console.WriteLine();
    Console.WriteLine("en s ecartant de la jetee vers le large :");
    double bx = port.Port.Hx, bz = port.Port.Hz;
    // la direction du large : celle ou le fond descend le plus vite
    double best = 0, ba = 0;
    for (int i = 0; i < 36; i++)
    {
        double a = i * Math.PI / 18;
        double d = -world.HeightAt(bx + Math.Cos(a) * 1500, bz + Math.Sin(a) * 1500);
        if (d > best) { best = d; ba = a; }
    }
    Teleport.Verdict? avant = null;
    for (int m = 0; m <= 3000; m += 100)
    {
        double x = bx + Math.Cos(ba) * m, z = bz + Math.Sin(ba) * m;
        var v = Teleport.Check(world, x, z, draft, L);
        if (v != avant)
        {
            Console.WriteLine($"   a {m,5} m : fond {world.HeightAt(x, z),7:F1} m  ->  {v}");
            avant = v;
        }
    }

    /* ET LE LARGE : a trois milles de toute terre, tout doit passer. On tire au
       hasard pour ne pas se rassurer sur un point choisi. */
    Console.WriteLine();
    var rnd = new Random(20260930);
    int bons = 0, essais = 0;
    while (essais < 200)
    {
        double x = world.Extent * rnd.NextDouble(), z = world.Extent * rnd.NextDouble();
        if (world.ShoreDistance(x, z) < 3000) continue;
        essais++;
        if (Teleport.Check(world, x, z, draft, L) == Teleport.Verdict.Bon) bons++;
    }
    Console.WriteLine($"au large (plus de 3 km de toute terre) : {bons}/{essais} points acceptes");
}

/* LES FILINS D ABORDAGE, EPROUVES.

   Ce qu on veut savoir n est pas « est-ce joli » mais « est-ce que cela
   diverge ». Une contrainte entre deux coques de trois cents tonnes, appliquee
   soixante fois par seconde, est exactement le genre de chose qui part a
   l infini sans prevenir — et un abordage qui envoie le navire dans l espace est
   la pire panne possible, parce qu elle arrive au moment le plus tendu.

   On lance donc une volee entre deux fregates, on tourne dix minutes, et l on
   regarde trois choses : l ecart entre les coques (il doit se refermer et non
   osciller), la vitesse de chacune (elle doit rester finie), et le fait que rien
   ne soit NaN. */
void Filins()
{
    var spec = ShipSpec.FromJson(File.ReadAllText(Path.Combine(shipsDir, "frigate17e.json")));
    var ocean = new Ocean { Swell = 1.0, Time = 0 };
    ocean.SetSeaState(3, 210);

    ShipPhysics Neuf(double x, double z)
    {
        var p = new ShipPhysics(spec, new HullLines(spec));
        var c = new Controls { Throttle = 0, Rudder = 0, Sheet = 0.6, SailsSet = false };
        p.Settle(ocean, c);
        p.Body.Pos = new Vec3d(x, p.Body.Pos.Y, z);
        return p;
    }

    // deux fregates bord a bord, a dix-huit metres — une portee de crochet
    var pirate = Neuf(0, 0);
    var proie = Neuf(18, 0);
    var ctrl = new Controls { Throttle = 0, Rudder = 0, Sheet = 0.6, SailsSet = false };

    var g = new Grapple();
    g.Throw(pirate, proie, new Random(20261001));
    Console.WriteLine($"volee de {Grapple.Volee} crochets : {g.Lines.Count} partis");

    double dt = 1.0 / 60, t = 0, pireV = 0;
    bool nan = false;
    Console.WriteLine("   t (s)   ecart   mordus   vitesse pirate   vitesse proie");
    for (int k = 0; k <= 60 * 600; k++)
    {
        pirate.Step(dt, ocean, ctrl, t);
        proie.Step(dt, ocean, ctrl, t);
        g.Step(dt);
        t += dt;

        double ecart = Hypo(proie.Body.Pos.X - pirate.Body.Pos.X, proie.Body.Pos.Z - pirate.Body.Pos.Z);
        double v1 = Hypo(pirate.Body.Vel.X, pirate.Body.Vel.Z), v2 = Hypo(proie.Body.Vel.X, proie.Body.Vel.Z);
        pireV = Math.Max(pireV, Math.Max(v1, v2));
        if (double.IsNaN(ecart) || double.IsNaN(v1) || double.IsNaN(v2)) { nan = true; break; }

        if (k % (60 * 60) == 0 || k == 60 * 30)
            Console.WriteLine($"   {t,5:F0}   {ecart,6:F2}   {g.Held,6}   {v1,14:F3}   {v2,12:F3}");
    }

    Console.WriteLine();
    Console.WriteLine($"pire vitesse atteinte : {pireV:F2} m/s   NaN : {(nan ? "OUI" : "non")}");
    Console.WriteLine(!nan && pireV < 12 ? "-> la contrainte tient" : "-> ELLE DIVERGE");

    // et la coupe : un bout a la fois, le plus raide
    Console.WriteLine();
    int reste = g.Held;
    while (g.Cut(proie)) { Console.WriteLine($"   coupe : il en reste {g.Held}"); if (--reste < 0) break; }
    Console.WriteLine($"apres la hache : {g.Held} filin(s), {g.Lines.Count} bout(s) en tout");
}

static double Hypo(double a, double b) => Math.Sqrt(a * a + b * b);

/* LES NAVIRES AU MOUILLAGE, EPROUVES SUR LES QUATORZE PORTS.

   Trois choses a verifier, et aucune ne se voit a l oeil depuis un seul port :
   que chaque poste porte assez d eau, qu aucun navire n en touche un autre, et
   que le semis rende la MEME chose deux fois — une rade doit retrouver ses
   navires a la meme place d une partie a l autre. */
void Mouillage()
{
    string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
    var region = RegionSpec.FromJson(File.ReadAllText(Path.Combine(root, "world", "caraibes.json")));
    var (iw, ih, grey) = GreyPng.Decode(File.ReadAllBytes(Path.Combine(root, region.Relief.Image)));
    var world = new World(region, iw, ih, grey, m => Console.WriteLine("  ! " + m));

    // les trois navires autorises : Roter Lowe, sloop, vaisseau de ligne
    (string Nom, double L, double B, double Draft)[] flotte =
    {
        ("sloop",      10.6,  3.50, 1.55),
        ("roter lowe", 30.0,  7.25, 3.85),
        ("belliqueuse",70.0, 14.50, 6.60),
    };
    var dims = new List<(double, double, double)>();
    foreach (var f in flotte) dims.Add((f.L, f.B, f.Draft));

    int total = 0, quai = 0, rade = 0, manques = 0;
    double pireEau = 99;
    Console.WriteLine($"{"port",-24}{"ponton",8}{"places",8}  detail");
    foreach (var isl in world.Isles)
    {
        if (isl.Port.Hx == 0 && isl.Port.Hz == 0) continue;
        uint g = 0; foreach (char c in isl.Key) g = g * 131 + c;
        var ps = Moored.Postes(world, isl, dims, 30, 7.25, g);
        var deux = Moored.Postes(world, isl, dims, 30, 7.25, g);
        bool memes = ps.Count == deux.Count;
        for (int i = 0; i < ps.Count && memes; i++)
            memes = Math.Abs(ps[i].X - deux[i].X) < 1e-9 && Math.Abs(ps[i].Z - deux[i].Z) < 1e-9;

        var mots = new List<string>();
        for (int i = 0; i < ps.Count; i++)
        {
            double fond = -world.HeightAt(ps[i].X, ps[i].Z);
            pireEau = Math.Min(pireEau, fond);
            mots.Add((ps[i].Mouille ? "rade" : "quai") + $" {fond:F1} m");
            if (ps[i].Mouille) rade++; else quai++;
        }
        manques += dims.Count - ps.Count;
        total += ps.Count;
        Console.WriteLine($"{isl.Name,-24}{isl.Port.Reach,7:F0}m{ps.Count,8}  {string.Join(", ", mots)}{(memes ? "" : "   !! SEMIS INSTABLE")}");
    }

    Console.WriteLine();
    Console.WriteLine($"{total} navire(s) poses : {quai} a quai, {rade} en rade ; {manques} sans place");
    Console.WriteLine($"le moins d eau sous un poste : {pireEau:F1} m");
    Console.WriteLine(pireEau > 6.6 + Moored.Sous - 0.2 ? "-> tous portent leur tirant" : "-> UN POSTE EST TROP MAIGRE");
}

/* CE QUE RAPPORTE LE FACTEUR DE VENT : la même coque au travers, par force 4 et
   force 6, pour chaque facteur. Vitesse, gîte et poussée.
     dotnet run --project core/NavalSim.Lab -c Release -- vent frigate17e */
void Vent()
{
    string name = args.Length > 1 ? args[1] : "frigate17e";
    var spec = ShipSpec.FromJson(File.ReadAllText(Path.Combine(shipsDir, name + ".json")));
    Console.WriteLine($"{spec.Name}, vent par le travers, 4 minutes");
    foreach (double force in new[] { 3.0, 4.0, 6.0 })
    {
        foreach (double gain in new[] { 1.0, 1.5, 2.0, 3.0, 4.0 })
        {
            Config.WindGain = gain;
            Config.WindHeel = args.Length > 2 && args[2] == "lie" ? gain : 1.0;
            var ocean = new Ocean { Swell = 1.0, Time = 0 };
            ocean.SetSeaState(force, 0);
            var p2 = new ShipPhysics(spec, new HullLines(spec));
            var ctrl = new Controls { Throttle = 0, Rudder = 0, Sheet = 0.3, SailsSet = true };
            p2.Settle(ocean, ctrl);
            double yaw = -90 * Math.PI / 180;
            p2.Body.Quat = Quatd.FromAxisAngle(new Vec3d(0, 1, 0), yaw);
            double dt = 1.0 / 60, t = 0, heel = 0, load = 0;
            for (int k = 0; k < 60 * 240; k++)
            {
                if (p2.OptSheet is double o) ctrl.Sheet = o;
                var f = p2.Body.Quat.Rotate(new Vec3d(0, 0, 1));
                double hdg = Math.Atan2(f.X, f.Z);
                ctrl.Rudder = Math.Clamp(-(Math.IEEERemainder(yaw - hdg, 2 * Math.PI)) * 3, -1, 1);
                p2.Step(dt, ocean, ctrl, t); t += dt;
                if (k > 60 * 200)
                {
                    var up = p2.Body.Quat.Rotate(new Vec3d(0, 1, 0));
                    heel = Math.Max(heel, Math.Abs(Math.Asin(Math.Clamp(up.X, -1, 1)) * 180 / Math.PI));
                    load = Math.Max(load, p2.SailLoad);
                }
            }
            var v = p2.Body.Vel;
            Console.WriteLine($"force {force:F0}  gain {gain,3:F1} : {Math.Sqrt(v.X * v.X + v.Z * v.Z) / 0.5144,5:F2} nd, gite {heel,5:F1} deg, toile {load,6:F1} N/m2, poussee {p2.SailDrive / 1000,6:F0} kN");
        }
        Console.WriteLine();
    }
    Config.WindGain = 1.0; Config.WindHeel = 1.0;
}
