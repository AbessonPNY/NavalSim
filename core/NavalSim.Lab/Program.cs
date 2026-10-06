using System.Linq;
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
    case "envol": Envol(); break;
    case "derive": Derive(); break;
    case "soute": Soute(); break;
    case "rupture": Rupture(); break;
    case "hausse": Hausse(); break;
    case "astres": Astres(); break;
    case "semis": Semis(); break;
    case "profil": Profil(); break;
    case "centre": Centre(); break;
    case "pontons": Pontons(); break;
    case "abri": Abri(); break;
    case "peche": Peche(); break;
    case "virement": Virement(); break;
    case "rade": Rade(); break;
    case "sonde": Sonde(); break;
    case "ecueil": Ecueil(); break;
    case "sondeur": Sondeur(); break;
    case "sombrer": Sombrer(); break;
    case "godille": Godille(); break;
    case "nage": Nage(); break;
    case "contre": Contre(); break;
    case "abordage": Abordage(); break;
    case "rumbs": foreach (double h in new[] { 0, 5.5, 11.25, 22.5, 56.25, 61, 67.5, 90, 135, 180, 200, 247.5, 270, 303.75, 348.75, 355 }) Console.WriteLine(FormattableString.Invariant($"{h,7:F2}° : {Compass.RumbShort(h),-8} {Compass.Rumb(h),-28} {Compass.Quadrantal(h)}")); Console.WriteLine(FormattableString.Invariant($"relèvement d un point au nord-est : {Compass.BearingDeg(0, 0, -1, 1):F1}°")); break;
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
    Config.WindGain = args.Length > 5 ? double.Parse(args[5], CultureInfo.InvariantCulture) : 8;
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
    /* « -- bordee 40 » : le nombre de coups au but (24 par défaut). Le trou d'avant
       (0,025 m², le boulet et le bois autour) contre celui du boulet seul (0,012), et
       le charpentier d'avant (un tampon toutes les 40 s) contre celui d'une équipe (20). */
    int hits = args.Length > 1 ? int.Parse(args[1]) : 24;
    foreach (double area in new[] { 0.025, 0.012 })
    foreach (double plugEvery in new[] { 40.0, 20.0 })
    {
        bool plug = plugEvery > 0;
        var ocean = new Ocean { Swell = 1.0, Time = 0 };
        ocean.SetSeaState(3, 0);
        var p = new ShipPhysics(spec, new HullLines(spec));
        var ctrl = new Controls { Throttle = 0, Rudder = 0, Sheet = 0.6, SailsSet = false };
        p.Settle(ocean, ctrl);
        p.PlugEvery = plugEvery;
        var rng = new Random(7);
        int below = 0;
        for (int i = 0; i < hits; i++)
        {
            int c = rng.Next(p.Comps.Length);
            // de trente centimètres sous l eau au pont : la muraille qu un boulet tiré à hauteur de batterie trouve
            var cc = p.Comps[c];
            double yHit = -0.3 - p.Body.Pos.Y + (cc.DeckY + 0.3 + p.Body.Pos.Y) * rng.NextDouble();
            double frac = (yHit - cc.KeelY) / (cc.DeckY - cc.KeelY);
            var br = p.MakeBreach(c, area, frac, 1);
            if (br != null && br.Y < 0) below++;
        }
        Console.WriteLine($"trou {area:F3} m2, charpentier {(plug ? plugEvery + " s" : "non")} : {p.Breaches.Count} breches, {below} sous la flottaison, pompes {p.PumpRate * 60:F1} m3/min, {p.Comps.Length} compartiments");
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
    Config.WindGain = args.Length > 5 ? double.Parse(args[5], CultureInfo.InvariantCulture) : 8;
    var spec = ShipSpec.FromJson(File.ReadAllText(Path.Combine(shipsDir, name + ".json")));
    // le facteur de vent en troisieme argument : « -- allures frigate17e 8 »
    Config.WindGain = args.Length > 2 ? double.Parse(args[2], System.Globalization.CultureInfo.InvariantCulture) : 1.0;
    Console.WriteLine($"{spec.Name} : L {spec.L:F0} m, voilure {spec.SailArea:F0} m2, gain {Config.WindGain:F1}");
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
    Config.WindGain = args.Length > 5 ? double.Parse(args[5], CultureInfo.InvariantCulture) : 8;
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

    // le pirate quitte l'abordage : il largue ses crochets
    g.Throw(pirate, proie, new Random(7)); for (int k = 0; k < 40; k++) g.Step(dt);
    int avant = g.Lines.Count;
    g.Release(pirate);
    Console.WriteLine($"le pirate largue : {avant} -> {g.Lines.Count} bout(s)");
    // la proie sombre : ce qui la tient est tranché
    g.Throw(pirate, proie, new Random(9)); for (int k = 0; k < 40; k++) g.Step(dt);
    avant = g.Lines.Count;
    proie.Foundered = true;
    g.Prune(_ => true);
    Console.WriteLine($"la proie sombre : {avant} -> {g.Lines.Count} bout(s)");
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
    Config.WindGain = args.Length > 5 ? double.Parse(args[5], CultureInfo.InvariantCulture) : 8;
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

/* LE VIREMENT DE BORD, ÉPROUVÉ : au près à 70° du vent, une minute et demie
   d'erre, puis la barre pour passer le vent et prendre 70° de l'autre bord.
   Passe-t-il le vent, en combien de temps, et jusqu'où tombe son erre — selon le
   gain de vent (settings.json → wind.gain).
     dotnet run --project core/NavalSim.Lab -c Release -- virement frigate17e 4 */
void Virement()
{
    string name = args.Length > 1 ? args[1] : "frigate17e";
    Config.WindGain = args.Length > 5 ? double.Parse(args[5], CultureInfo.InvariantCulture) : 8;
    double force = args.Length > 2 ? double.Parse(args[2], CultureInfo.InvariantCulture) : 4;
    var spec = ShipSpec.FromJson(File.ReadAllText(Path.Combine(shipsDir, name + ".json")));
    Console.WriteLine($"{spec.Name}, force {force:F0}, virement de 70° à -70° du vent");
    foreach (bool crew in new[] { false, true })
    foreach (double gain in new[] { 1.0, 2.0, 4.0, 8.0 })
    {
        Config.WindGain = gain;
        Config.CrewTacks = crew;
        var ocean = new Ocean { Swell = 1.0, Time = 0 };
        ocean.SetSeaState(force, 0);
        var p2 = new ShipPhysics(spec, new HullLines(spec));
        var ctrl = new Controls { Throttle = 0, Rudder = 0, Sheet = 0.3, SailsSet = true };
        p2.Settle(ocean, ctrl);
        double y0 = 70 * Math.PI / 180;
        p2.Body.Quat = Quatd.FromAxisAngle(new Vec3d(0, 1, 0), y0);
        double dt = 1.0 / 60, t = 0, vMin = 99, vStart = 0, crossed = -1, done = -1, astern = 0, closest = 180;
        for (int k = 0; k < 60 * 240; k++)
        {
            if (p2.OptSheet is double o) ctrl.Sheet = o;
            var fw = p2.Body.Quat.Rotate(new Vec3d(0, 0, 1));
            double hdg = Math.Atan2(fw.X, fw.Z);
            double target = t < 90 ? y0 : -y0;
            ctrl.Rudder = Math.Clamp(-(Math.IEEERemainder(target - hdg, 2 * Math.PI)) * 3, -1, 1);
            /* LA BARRE TENUE DU MÊME BORD tant que le virement n'est pas fait, comme le
               tiendrait un timonier — même si le navire cule et que l'écart change de signe. */
            if (t >= 90 && hdg > -y0 + 10 * Math.PI / 180) ctrl.Rudder = Math.Sign(-(Math.IEEERemainder(-y0 - y0, 2 * Math.PI))) * 1.0;
            p2.Step(dt, ocean, ctrl, t); t += dt;
            var v = p2.Body.Vel;
            double sog = v.X * fw.X + v.Z * fw.Z;          // l'erre, signée : négative, il cule
            if (t < 90) { vStart = sog; continue; }
            if (crew && args.Length > 3 && args[3] == "trace" && Math.Floor(t / 3) != Math.Floor((t - dt) / 3)) Console.WriteLine(FormattableString.Invariant($"   gain {gain:F0} t {t - 90,4:F0} cap {hdg * 180 / Math.PI,5:F0}  phase {p2.TackPhase}  erre {sog / 0.5144,5:F2} nd  barre {ctrl.Rudder,4:F1}  lacet {p2.Body.AngVel.Y * 180 / Math.PI,5:F1}°/s  amure {p2.Tack}"));
            vMin = Math.Min(vMin, sog);
            closest = Math.Min(closest, Math.Abs(hdg) * 180 / Math.PI);
            if (sog < 0) astern += dt;
            if (crossed < 0 && hdg < 0) crossed = t - 90;
            if (done < 0 && hdg < -y0 + 10 * Math.PI / 180) done = t - 90;
        }
        Console.WriteLine(FormattableString.Invariant($"{(crew ? "équipage" : "seul    ")} gain {gain,3:F0} : erre {vStart / 0.5144,5:F2} nd ; passe le vent {(crossed < 0 ? "JAMAIS" : crossed.ToString("F0") + " s")}, à 60° de l'autre bord {(done < 0 ? "jamais" : done.ToString("F0") + " s")} ; erre au plus bas {vMin / 0.5144,5:F2} nd, à culer {astern:F0} s ; au plus près du vent {closest:F0}°"));
    }
    Config.WindGain = 1.0; Config.CrewTacks = false;
}

/* LA RADE DE PORT-ROYAL, ÉPROUVÉE : les routes que trace HarbourRoute (vers
   Passage Fort, vers le large, et retour), puis un sloop et un cotre qui les
   courent sous le pilote de rade, vent du départ (105°, force 4).
     dotnet run --project core/NavalSim.Lab -c Release -- rade [gain] [force] [vent] */
/* LA SONDE : une carte en caractères du fond autour d un point (lat lon [demi-côté m] [pas m]).
   # terre · + moins de 2 m · : 2 à 5 · . 5 à 10 · , 10 à 20 · blanc au-delà. */
void Sonde()
{
    string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
    var region = RegionSpec.FromJson(File.ReadAllText(Path.Combine(root, "world", "caraibes.json")));
    var (iw, ih, grey) = GreyPng.Decode(File.ReadAllBytes(Path.Combine(root, region.Relief.Image)));
    var imgs = new List<World.PatchImage>();
    foreach (var pz in region.Patches)
    {
        string pi = Path.Combine(root, pz.Image);
        if (!File.Exists(pi)) continue;
        var (pw, ph, pg) = GreyPng.Decode(File.ReadAllBytes(pi));
        imgs.Add(new World.PatchImage(pz, pw, ph, pg));
    }
    Config.Reefs = true;
    var world = new World(region, iw, ih, grey, m => { }, imgs);
    double lat = double.Parse(args[1], CultureInfo.InvariantCulture), lon = double.Parse(args[2], CultureInfo.InvariantCulture);
    double half = args.Length > 3 ? double.Parse(args[3], CultureInfo.InvariantCulture) : 2000;
    double step = args.Length > 4 ? double.Parse(args[4], CultureInfo.InvariantCulture) : 50;
    var (cx, cz) = world.Geo.ToXZ(lat, lon);
    Console.WriteLine(FormattableString.Invariant($"sonde autour de ({cx:F0}, {cz:F0}), fond {-world.HeightAt(cx, cz):F1} m au centre, nord en haut, est à droite, {step} m par caractère"));
    for (double z = cz + half; z >= cz - half; z -= step)
    {
        var sb = new System.Text.StringBuilder();
        // est = -x : on lit de l ouest (x grand) à l est (x petit)
        for (double x = cx + half; x >= cx - half; x -= step)
        {
            double h = world.HeightAt(x, z);
            sb.Append(Math.Abs(x - cx) < step / 2 && Math.Abs(z - cz) < step / 2 ? (char)0x40 : h >= 0 ? (char)0x23 : h > -2 ? (char)0x2b : h > -5 ? (char)0x3a : h > -10 ? (char)0x2e : h > -20 ? (char)0x2c : (char)0x20);
        }
        Console.WriteLine(sb.ToString());
    }
}

/* L'ÉCUEIL : un sloop lancé à six nœuds sur un récif (Lime Cay), sur une roche isolée
   qui affleure, et sur une plage de sable — le corail et la roche doivent tuer, le
   sable seulement arrêter. [nœuds] */
void Ecueil()
{
    string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
    string shipsDir = Path.Combine(root, "ships");
    var region = RegionSpec.FromJson(File.ReadAllText(Path.Combine(root, "world", "caraibes.json")));
    var (iw, ih, grey) = GreyPng.Decode(File.ReadAllBytes(Path.Combine(root, region.Relief.Image)));
    var imgs = new List<World.PatchImage>();
    foreach (var pz in region.Patches)
    {
        string pi = Path.Combine(root, pz.Image);
        if (!File.Exists(pi)) continue;
        var (pw, ph, pg) = GreyPng.Decode(File.ReadAllBytes(pi));
        imgs.Add(new World.PatchImage(pz, pw, ph, pg));
    }
    Config.Reefs = true; Config.WindGain = 8; Config.CrewTacks = true;
    var world = new World(region, iw, ih, grey, m => { }, imgs);
    double knots = args.Length > 1 ? double.Parse(args[1], CultureInfo.InvariantCulture) : 6;
    var lime = world.ReefList.Find(x => x.Spec.Name == "Lime Cay")!;
    // une roche qui affleure, seule, par fond de quinze mètres et plus, au sud de Lime Cay
    double rx = lime.X, rz = lime.Z - 700;
    var rock = world.Rocks.Add(rx, rz, 2.5, world.HeightAt(rx, rz), -0.4);
    // la plage : la côte sud des Palisadoes, à l'est des cayes
    var pr = world.ByKey("port-royal")!;
    double sx = pr.X - 1500, sz = pr.Z - 2000;
    while (world.HeightAt(sx, sz) < 0 && sz < pr.Z + 2000) sz += 5;
    var cases = new (string Name, double X, double Z)[] { ("récif de Lime Cay", lime.X, lime.Z), ("roche isolée", rx, rz), ("plage des Palisadoes", sx, sz) };
    var spec = ShipSpec.FromJson(File.ReadAllText(Path.Combine(shipsDir, "sloop.json")));
    Console.WriteLine(FormattableString.Invariant($"{spec.Name}, lancé à {knots} nœuds ; {world.Rocks.Count} roche(s)"));
    foreach (var (name, tx, tz) in cases)
    {
        // de 400 m au sud-ouest, droit dessus
        double fx = tx + 280, fz = tz - 280;
        if (world.HeightAt(fx, fz) > -4) { fx = tx; fz = tz - 400; }
        var ocean = new Ocean { Swell = 1.0, Time = 0 };
        ocean.SetSeaState(3, 105);
        var ph = new ShipPhysics(spec, new HullLines(spec));
        var ctrl = new Controls { Throttle = 0, Rudder = 0, Sheet = 0.5, SailsSet = false };
        ph.Settle(ocean, ctrl);
        ph.World = world;
        double hd = Math.Atan2(tx - fx, tz - fz);
        ph.Body.Pos = new Vec3d(fx, ph.Body.Pos.Y, fz);
        ph.Body.Quat = Quatd.FromAxisAngle(new Vec3d(0, 1, 0), hd);
        double v0 = knots * 0.5144;
        ph.Body.Vel = new Vec3d(Math.Sin(hd) * v0, 0, Math.Cos(hd) * v0);
        double dt = 1.0 / 30, tt = 0, hitAt = -1, hitSpd = 0;
        while (tt < 600 && !ph.Foundered)
        {
            /* LANCÉ ET TENU À SON ERRE jusqu'au choc : on mesure ce que fait l'écueil
               à six nœuds, pas ce que le vent du jour donne au sloop */
            if (hitAt < 0)
            {
                ph.Body.Quat = Quatd.FromAxisAngle(new Vec3d(0, 1, 0), hd);
                ph.Body.AngVel = Vec3d.Zero;
                ph.Body.Vel = new Vec3d(Math.Sin(hd) * v0, ph.Body.Vel.Y, Math.Cos(hd) * v0);
            }
            ph.Step(dt, ocean, ctrl, tt); tt += dt;
            if (hitAt < 0 && ph.Aground > 0.01) { hitAt = tt; hitSpd = ph.Body.Vel.LengthXZ / 0.5144; }
        }
        double area = 0; foreach (var b in ph.Breaches) area += b.Area;
        Console.WriteLine(FormattableString.Invariant($"  {name} : {(hitAt < 0 ? "jamais touché" : FormattableString.Invariant($"touché à {hitAt:F0} s ({hitSpd:F1} nd)"))}, {ph.Breaches.Count} brèche(s), {area:F2} m² ; ")
            + (ph.Foundered ? FormattableString.Invariant($"SOMBRÉ à {tt:F0} s, {tt - hitAt:F0} s après le choc") : FormattableString.Invariant($"à flot au bout de {tt:F0} s, échoué {ph.Aground:F2} m")));
    }
}

/* LE SONDEUR : un sloop mené par la barre automatique (comme un pirate ou une
   rencontre) dont la marque est de l'autre côté de Lime Cay — au vent, puis sous le
   vent —, sans sonde puis avec. [gain] [force] [vent] */
void Sondeur()
{
    string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
    var region = RegionSpec.FromJson(File.ReadAllText(Path.Combine(root, "world", "caraibes.json")));
    var (iw, ih, grey) = GreyPng.Decode(File.ReadAllBytes(Path.Combine(root, region.Relief.Image)));
    var imgs = new List<World.PatchImage>();
    foreach (var pz in region.Patches)
    {
        string pi = Path.Combine(root, pz.Image);
        if (!File.Exists(pi)) continue;
        var (pw, ph0, pg) = GreyPng.Decode(File.ReadAllBytes(pi));
        imgs.Add(new World.PatchImage(pz, pw, ph0, pg));
    }
    double gain = args.Length > 1 ? double.Parse(args[1], CultureInfo.InvariantCulture) : 8;
    double force = args.Length > 2 ? double.Parse(args[2], CultureInfo.InvariantCulture) : 4;
    double windDeg = args.Length > 3 ? double.Parse(args[3], CultureInfo.InvariantCulture) : 105;
    Config.Reefs = true; Config.WindGain = gain; Config.CrewTacks = true; Config.HelmBySpeed = true;
    var world = new World(region, iw, ih, grey, m => { }, imgs);
    var lime = world.ReefList.Find(x => x.Spec.Name == "Lime Cay")!;
    var spec = ShipSpec.FromJson(File.ReadAllText(Path.Combine(root, "ships", "sloop.json")));
    // le vent vient du 105 (est-sud-est) : est = −x. Au vent, on part de l'ouest vers l'est.
    var legs = new (string Name, double Fx, double Fz, double Tx, double Tz)[]
    {
        ("au vent, de l'ouest à l'est", lime.X + 600, lime.Z + 30, lime.X - 600, lime.Z - 30),
        ("sous le vent, de l'est à l'ouest", lime.X - 600, lime.Z - 30, lime.X + 600, lime.Z + 30),
    };
    Console.WriteLine(FormattableString.Invariant($"{spec.Name} contre Lime Cay ; gain {gain}, force {force}, vent {windDeg}°"));
    foreach (bool sounds in new[] { false, true })
        foreach (var (name, fx, fz, tx, tz) in legs)
        {
            Config.HelmSounds = sounds;
            var ocean = new Ocean { Swell = 1.0, Time = 0 };
            ocean.SetSeaState(force, windDeg);
            var ph = new ShipPhysics(spec, new HullLines(spec));
            var ctrl = new Controls { Throttle = 0, Rudder = 0, Sheet = 0.5, SailsSet = true };
            ph.Settle(ocean, ctrl);
            ph.World = world;
            double hd = Math.Atan2(tx - fx, tz - fz);
            ph.Body.Pos = new Vec3d(fx, ph.Body.Pos.Y, fz);
            ph.Body.Quat = Quatd.FromAxisAngle(new Vec3d(0, 1, 0), hd);
            var helm = new AutoHelm(ph) { Target = new Vec3d(tx, 0, tz) };
            double dt = 1.0 / 30, tt = 0, aground = 0, minDepth = 99, closest = 1e9;
            int dodges = 0; bool was = false;
            while (tt < 1500 && !ph.Foundered)
            {
                helm.Update(dt, ocean, ctrl);
                ph.Step(dt, ocean, ctrl, tt); tt += dt;
                var bp = ph.Body.Pos;
                if (ph.Aground > 0.01) aground += dt;
                minDepth = Math.Min(minDepth, -world.HeightAt(bp.X, bp.Z));
                closest = Math.Min(closest, Math.Sqrt((bp.X - tx) * (bp.X - tx) + (bp.Z - tz) * (bp.Z - tz)));
                bool now = helm.Dodging != null; if (now && !was) dodges++; was = now;
                if (closest < spec.L * 3) break;
            }
            double area = 0; foreach (var br in ph.Breaches) area += br.Area;
            Console.WriteLine(FormattableString.Invariant(
                $"  {(sounds ? "AVEC sonde" : "sans sonde")}, {name} : {(closest < spec.L * 3 ? "rendu" : "pas rendu")} en {tt / 60:F1} min (au plus près à {closest:F0} m), échoué {aground:F0} s, fond mini {minDepth:F1} m, {ph.Breaches.Count} brèche(s) {area:F2} m², {dodges} dérobade(s){(ph.Foundered ? ", SOMBRÉ" : "")}"));
        }
}

/* SOMBRER : combien de temps une coque met à couler, toutes ses tranches percées
   d'une brèche de tant de mètres carrés (le naufrage du film du chapitre 1).
   [fiche] [aire] [hauteur 0-1] [force] */
void Sombrer()
{
    string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
    string name = args.Length > 1 ? args[1] : "frigate17e";
    Config.WindGain = args.Length > 5 ? double.Parse(args[5], CultureInfo.InvariantCulture) : 8;
    var spec = ShipSpec.FromJson(File.ReadAllText(Path.Combine(root, "ships", name + ".json")));
    double force = args.Length > 4 ? double.Parse(args[4], CultureInfo.InvariantCulture) : 10;
    foreach (double area in args.Length > 2 ? new[] { double.Parse(args[2], CultureInfo.InvariantCulture) } : new[] { 1.4, 3, 6 })
    {
        double hf = args.Length > 3 ? double.Parse(args[3], CultureInfo.InvariantCulture) : 0.08;
        var ocean = new Ocean { Swell = 1.0, Time = 0 };
        ocean.SetSeaState(force, 105);
        var ph = new ShipPhysics(spec, new HullLines(spec));
        var ctrl = new Controls { SailsSet = true, Sheet = 0.6 };
        ph.Settle(ocean, ctrl);
        for (int i = 0; i < ph.Comps.Length; i++) ph.MakeBreach(i, area, hf, i % 2 == 0 ? 1 : -1);
        ph.PumpOn = false;
        double dt = 1.0 / 30, tt = 0, half = -1;
        while (tt < 300 && !ph.Foundered)
        {
            ph.Step(dt, ocean, ctrl, tt); tt += dt;
            if (half < 0 && ph.SubmergedFrac > 0.5) half = tt;
        }
        Console.WriteLine(FormattableString.Invariant($"{spec.Name}, {ph.Comps.Length} tranches percées de {area} m² à {hf:F2} de hauteur, force {force} : à moitié sous l'eau à {half:F0} s, {(ph.Foundered ? $"sombrée à {tt:F0} s" : "à flot au bout de 300 s")}"));
    }
}

/* LA GODILLE : un navire arrêté face au vent, voiles établies, trente secondes —
   barre au milieu, barre tenue, et la godille du clavier (un coup de barre de
   0,6 s à 1,6 par seconde, puis on lâche et elle revient à 2,5 par seconde), sans
   puis avec Config.RudderScull. [fiche] [force] */
void Godille()
{
    string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
    string name = args.Length > 1 ? args[1] : "sloop";
    double force = args.Length > 2 ? double.Parse(args[2], CultureInfo.InvariantCulture) : 3;
    var spec = ShipSpec.FromJson(File.ReadAllText(Path.Combine(root, "ships", name + ".json")));
    Config.WindGain = 8; Config.CrewTacks = true;
    Console.WriteLine(FormattableString.Invariant($"{spec.Name} face au vent, force {force} : lame {spec.RudderArea:F2} m², corde {spec.RudderChord:F2} m"));
    foreach (var (label, mode, scull) in new[] { ("barre au milieu", 0, false), ("barre tenue à bâbord", 1, true), ("godille, sans le modèle", 2, false), ("godille", 2, true), ("godille des deux bords", 3, true), ("godille au coup sec", 4, true) })
    {
        Config.RudderScull = scull;
        var ocean = new Ocean { Swell = 1.0, Time = 0 };
        ocean.SetSeaState(force, 105);
        var ph = new ShipPhysics(spec, new HullLines(spec));
        var ctrl = new Controls { SailsSet = true, Sheet = 0.3 };
        ph.Settle(ocean, ctrl);
        // l'étrave dans le lit du vent (cap 105) : est = −x
        double hd = 105 * Math.PI / 180;
        ph.Body.Quat = Quatd.FromAxisAngle(new Vec3d(0, 1, 0), -hd);
        double dt = 1.0 / 60, tt = 0, maxFwd = 0;
        Vec3d f0 = ph.Body.Quat.Rotate(new Vec3d(0, 0, 1));
        double h0 = Math.Atan2(-f0.X, f0.Z);
        while (tt < 30)
        {
            double ph2 = tt % 1.2;
            if (mode == 1) ctrl.Rudder = Math.Min(1, ctrl.Rudder + dt * 1.6);
            else if (mode == 2) ctrl.Rudder = ph2 < 0.6 ? Math.Min(1, ctrl.Rudder + dt * 1.6) : ctrl.Rudder * (1 - Math.Min(1, dt * 2.5));
            else if (mode == 4) ctrl.Rudder = ph2 < 0.6 ? Math.Min(1, ctrl.Rudder + dt * 1.6) : Math.Max(0, ctrl.Rudder - dt * 6.5);
            else if (mode == 3) ctrl.Rudder = (tt % 2.4) < 1.2 ? Math.Clamp(ctrl.Rudder + (ph2 < 0.6 ? 1 : -1) * dt * 1.6, -1, 1) : Math.Clamp(ctrl.Rudder + (ph2 < 0.6 ? -1 : 1) * dt * 1.6, -1, 1);
            ph.Step(dt, ocean, ctrl, tt); tt += dt;
            var fw = ph.Body.Quat.Rotate(new Vec3d(0, 0, 1));
            maxFwd = Math.Max(maxFwd, ph.Body.Vel.Dot(fw));
        }
        var f1 = ph.Body.Quat.Rotate(new Vec3d(0, 0, 1));
        double turn = Math.Atan2(Math.Sin(Math.Atan2(-f1.X, f1.Z) - h0), Math.Cos(Math.Atan2(-f1.X, f1.Z) - h0)) * 180 / Math.PI;
        Console.WriteLine(FormattableString.Invariant($"  {label,-26} : tourné de {turn,6:F1}° en 30 s, erre en avant au plus {maxFwd / 0.5144:F2} nd"));
    }
}

/* L'ABORDAGE : deux frégates bord à bord, cap au nord, la première avec de l'erre,
   la seconde qui vient sur elle de travers à une vitesse donnée. Les brèches que
   chacune y gagne en une minute, et leur aire. Un abordage se fait à couple, en
   glissant le long du bord : c'est la vitesse de TRAVERS au contact qui défonce
   un bordé, pas celle du navire sur l'eau. */
void Abordage()
{
    var spec = ShipSpec.FromJson(File.ReadAllText(Path.Combine(shipsDir, "frigate17e.json")));
    Console.WriteLine($"{spec.Name}, B {spec.B:F1} m");
    foreach (double way in new[] { 0.0, 3.0 })
    foreach (double cross in new[] { 0.5, 1.0, 2.0, 3.0, 4.0 })
    {
        var ocean = new Ocean { Swell = 1.0, Time = 0 };
        ocean.SetSeaState(2, 0);
        var a = new ShipPhysics(spec, new HullLines(spec));
        var b = new ShipPhysics(spec, new HullLines(spec));
        var ctrl = new Controls { SailsSet = false };
        a.Settle(ocean, ctrl); b.Settle(ocean, ctrl);
        a.PlugEvery = 0; b.PlugEvery = 0;
        // b à tribord de a (tribord = −x), à un demi-mètre de son bordé, qui vient dessus
        b.Body.Pos = new Vec3d(a.Body.Pos.X - spec.B - 0.5, b.Body.Pos.Y, a.Body.Pos.Z);
        a.Body.Vel = new Vec3d(0, 0, way);
        b.Body.Vel = new Vec3d(cross, 0, way);
        var both = new List<ShipPhysics> { a, b };
        double dt = 1.0 / 60, t = 0;
        for (int k = 0; k < 60 * 60; k++) { a.Step(dt, ocean, ctrl, t, both); b.Step(dt, ocean, ctrl, t, both); t += dt; }
        double areaA = 0, areaB = 0;
        foreach (var br in a.Breaches) areaA += br.Area;
        foreach (var br in b.Breaches) areaB += br.Area;
        Console.WriteLine(FormattableString.Invariant($"  erre {way:F0} m/s, de travers {cross:F1} m/s : abordée {a.Breaches.Count} brèche(s) {areaA:F3} m², l'abordeur {b.Breaches.Count} {areaB:F3} m²"));
    }
}

/* LA VOILE À CONTRE : un navire arrêté face au vent, voiles établies, la bôme tenue
   à contre vingt secondes d'un bord ou de l'autre. [fiche] [force] */
void Contre()
{
    string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
    string name = args.Length > 1 ? args[1] : "sloop";
    double force = args.Length > 2 ? double.Parse(args[2], CultureInfo.InvariantCulture) : 3;
    var spec = ShipSpec.FromJson(File.ReadAllText(Path.Combine(root, "ships", name + ".json")));
    Config.WindGain = 8; Config.CrewTacks = true;
    Console.WriteLine(FormattableString.Invariant($"{spec.Name} face au vent, force {force}"));
    foreach (var (label, side) in new[] { ("rien", 0), ("bôme à contre, tribord", 1), ("bôme à contre, bâbord", -1) })
    {
        var ocean = new Ocean { Swell = 1.0, Time = 0 };
        ocean.SetSeaState(force, 105);
        var ph = new ShipPhysics(spec, new HullLines(spec));
        var ctrl = new Controls { SailsSet = true, Sheet = 0.3 };
        ph.Settle(ocean, ctrl);
        ph.Body.Quat = Quatd.FromAxisAngle(new Vec3d(0, 1, 0), -105 * Math.PI / 180);
        Vec3d f0 = ph.Body.Quat.Rotate(new Vec3d(0, 0, 1));
        double h0 = Math.Atan2(-f0.X, f0.Z), dt = 1.0 / 60, tt = 0, back = 0;
        while (tt < 20)
        {
            ctrl.Backed = side;
            ph.Step(dt, ocean, ctrl, tt); tt += dt;
            var fw = ph.Body.Quat.Rotate(new Vec3d(0, 0, 1));
            back = Math.Min(back, ph.Body.Vel.Dot(fw));
        }
        var f1 = ph.Body.Quat.Rotate(new Vec3d(0, 0, 1));
        double h1 = Math.Atan2(-f1.X, f1.Z);
        double turn = Math.Atan2(Math.Sin(h1 - h0), Math.Cos(h1 - h0)) * 180 / Math.PI;
        Console.WriteLine(FormattableString.Invariant($"  {label,-24} : l'étrave a tourné de {turn,6:F1}° en 20 s (vers {(turn > 0 ? "bâbord" : "tribord")}), culé jusqu'à {-back / 0.5144:F2} nd"));
    }
}

/* LE NAGEUR sur une mer plate, un fond plat à 30 m : ses allures, son apnée, sa
   flottaison selon la profondeur. */
void Nage()
{
    double floor = -30;
    Func<double, double, double> fl = (x, z) => floor;
    (double, Swimmer) Run(SwimInput inp, double secs, Action<Swimmer>? setup = null, Func<Swimmer, bool>? stop = null, double hold = double.NaN)
    {
        var s = new Swimmer { Pos = new Vec3d(0, Swimmer.EyeAbove, 0) };
        setup?.Invoke(s);
        double t = 0, dt = 1.0 / 60;
        while (t < secs && !(stop?.Invoke(s) ?? false))
        {
            // tenu à cette profondeur : la flottaison le remonterait respirer
            if (!double.IsNaN(hold)) s.Pos = new Vec3d(s.Pos.X, hold, s.Pos.Z);
            s.Step(dt, inp, 0, floor, fl); t += dt;
        }
        return (t, s);
    }
    var (_, a) = Run(new SwimInput { Fwd = 1 }, 20);
    Console.WriteLine(FormattableString.Invariant($"surface, brasse : {a.Pos.Z / 20:F2} m/s ; souffle {a.Breath:F2}"));
    var (_, b) = Run(new SwimInput { Fwd = 1, Hard = true }, 20);
    Console.WriteLine(FormattableString.Invariant($"surface, forcé : {b.Pos.Z / 20:F2} m/s"));
    // le canard, puis tête en bas, droit vers le fond
    var (t10, c) = Run(new SwimInput { Fwd = 1, Up = -1 }, 60, s => s.Pitch = -1.45, s => s.Pos.Y < -10);
    Console.WriteLine(FormattableString.Invariant($"descendre à 10 m : {t10:F1} s, souffle {c.Breath:F2}"));
    var (tr, d) = Run(new SwimInput { Fwd = 1, Up = -1, Hard = true }, 60, s => s.Pitch = -1.45, s => s.Pos.Y < -10);
    Console.WriteLine(FormattableString.Invariant($"descendre à 10 m en forçant : {tr:F1} s, souffle {d.Breath:F2}"));
    // l'apnée, immobile sous 2 m ; nageant à 2 m ; nageant à 10 m
    foreach (var (lab, y, fwd, hard) in new[] { ("immobile à 2 m", -2.0, 0.0, false), ("nageant à 2 m", -2.0, 1.0, false), ("nageant à 10 m", -10.0, 1.0, false), ("forçant à 10 m", -10.0, 1.0, true) })
    {
        var (ta, e) = Run(new SwimInput { Fwd = fwd, Hard = hard }, 300, s => { s.Under = true; s.DiveTime = 1; s.Pos = new Vec3d(0, y, 0); }, s => s.Blackout, y);
        Console.WriteLine(FormattableString.Invariant($"apnée {lab,-16} : {ta:F0} s (contractions à {ta * (1 - Swimmer.Contractions):F0} s)"));
    }
    // lâché sans rien faire à 4, 10, 16 m : flotte-t-il ?
    foreach (double y in new[] { -4.0, -10.0, -16.0 })
    {
        var (_, g) = Run(new SwimInput(), 10, s => { s.Under = true; s.DiveTime = 1; s.Pos = new Vec3d(0, y, 0); });
        Console.WriteLine(FormattableString.Invariant($"lâché à {-y:F0} m : {(-g.Pos.Y):F1} m après 10 s{(g.Under ? "" : " (en surface)")}"));
    }
    // L'ALLURE PAR COUPS : moyenne, creux et sommet, l'air tenu à cette réserve
    foreach (var (lab, under, air, hard) in new[] { ("dessous, frais", true, 1.0, false), ("dessous, frais, forcé", true, 1.0, true), ("dessous, air 50 %", true, 0.5, false), ("dessous, air 20 %", true, 0.2, false), ("dessous, air 5 %", true, 0.05, false), ("surface", false, 1.0, false), ("surface, forcé", false, 1.0, true) })
    {
        var s = new Swimmer { Pos = new Vec3d(0, under ? -3 : Swimmer.EyeAbove, 0), Under = under, DiveTime = 1 };
        double tt = 0, dt = 1.0 / 60, z0 = 0, lo = 9, hi = 0;
        var inp = new SwimInput { Fwd = 1, Hard = hard };
        while (tt < 16)
        {
            s.Breath = air;
            if (under) s.Pos = new Vec3d(s.Pos.X, -3, s.Pos.Z);
            s.Step(dt, inp, 0, -30, fl); tt += dt;
            if (tt >= 4 && z0 == 0) z0 = s.Pos.Z;
            if (tt >= 4) { double v = s.Vel.LengthXZ; lo = Math.Min(lo, v); hi = Math.Max(hi, v); }
        }
        Console.WriteLine(FormattableString.Invariant($"allure {lab,-24} : moyenne {(s.Pos.Z - z0) / 12:F2} m/s, creux {lo:F2}, sommet {hi:F2}, vigueur {s.Vigor:F2}"));
    }
    var (tb, h) = Run(new SwimInput(), 60, s => { s.Breath = 0; }, s => s.Breath >= 1);
    // le saut : sous l'eau à 0,4 m, lancé de côté et vers le bas ; remonte-t-il ?
    for (int k = 0; k <= 10; k += 2)
    {
        var (_, j) = Run(new SwimInput(), k, s => { s.Under = true; s.DiveTime = 0; s.Pos = new Vec3d(0, -0.4, 0); s.Vel = new Vec3d(0.8, -0.6, 0); });
        Console.WriteLine(FormattableString.Invariant($"saut, après {k} s : œil à {j.Pos.Y:F2} m, vy {j.Vel.Y:F2}, {(j.Under ? "dessous" : "en surface")}"));
    }
    Console.WriteLine(FormattableString.Invariant($"reprendre son souffle, de vide à plein : {tb:F0} s"));
}

void Rade()
{
    string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
    var region = RegionSpec.FromJson(File.ReadAllText(Path.Combine(root, "world", "caraibes.json")));
    var (iw, ih, grey) = GreyPng.Decode(File.ReadAllBytes(Path.Combine(root, region.Relief.Image)));
    var imgs = new List<World.PatchImage>();
    foreach (var pz in region.Patches)
    {
        string pi = Path.Combine(root, pz.Image);
        if (!File.Exists(pi)) continue;
        var (pw, ph, pg) = GreyPng.Decode(File.ReadAllBytes(pi));
        imgs.Add(new World.PatchImage(pz, pw, ph, pg));
    }
    var world = new World(region, iw, ih, grey, m => { }, imgs);
    double gain = args.Length > 1 ? double.Parse(args[1], CultureInfo.InvariantCulture) : 8;
    double force = args.Length > 2 ? double.Parse(args[2], CultureInfo.InvariantCulture) : 4;
    double windDeg = args.Length > 3 ? double.Parse(args[3], CultureInfo.InvariantCulture) : 105;
    Config.WindGain = gain; Config.CrewTacks = true; Config.Reefs = true;
    Config.HelmBySpeed = !(args.Length > 6 && args[6] == "page");
    (double, double) Quay(string key)
    {
        var p = world.ByKey(key)!.Port;
        return (p.Hx + Math.Cos(p.Ang) * 200, p.Hz + Math.Sin(p.Ang) * 200);
    }
    var pr = Quay("port-royal"); var pf = Quay("passage-fort");
    foreach (string name in new[] { "sloop", "schooner" })
    {
        var spec = ShipSpec.FromJson(File.ReadAllText(Path.Combine(shipsDir, name + ".json")));
        double need = spec.Hull.KeelDepth + spec.Hull.KeelExtra + 1.5;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var toSea = HarbourRoute.ToSea(world, pr.Item1, pr.Item2, need);
        var routes = new List<(string Name, List<(double X, double Z)>? R)>
        {
            ("Port-Royal -> Passage Fort", HarbourRoute.Plan(world, pr.Item1, pr.Item2, pf.Item1, pf.Item2, need)),
            ("Passage Fort -> Port-Royal", HarbourRoute.Plan(world, pf.Item1, pf.Item2, pr.Item1, pr.Item2, need)),
            ("Port-Royal -> le large", toSea),
            ("le large -> Port-Royal", toSea == null ? null : Enumerable.Reverse(toSea).ToList()),
        };
        Console.WriteLine(FormattableString.Invariant($"{spec.Name} (fond voulu {need:F1} m), routes en {sw.ElapsedMilliseconds} ms ; gain {gain}, force {force}, vent {windDeg}°"));
        foreach (var (rn, route) in routes)
        {
            if (route == null) { Console.WriteLine($"  {rn} : AUCUNE ROUTE"); continue; }
            double len = 0;
            for (int k = 0; k + 1 < route.Count; k++) len += Math.Sqrt(Math.Pow(route[k + 1].X - route[k].X, 2) + Math.Pow(route[k + 1].Z - route[k].Z, 2));
            var ocean = new Ocean { Swell = 1.0, Time = 0 };
            ocean.SetSeaState(force, windDeg);
            var p2 = new ShipPhysics(spec, new HullLines(spec));
            var ctrl = new Controls { Throttle = 0, Rudder = 0, Sheet = 0.5, SailsSet = true };
            p2.Settle(ocean, ctrl);
            p2.World = world;
            double hd = Math.Atan2(route[1].X - route[0].X, route[1].Z - route[0].Z);
            p2.Body.Pos = new Vec3d(route[0].X, p2.Body.Pos.Y, route[0].Z);
            p2.Body.Quat = Quatd.FromAxisAngle(new Vec3d(0, 1, 0), hd);
            var pilot = new HarbourPilot(p2, world, route, need);
            double dt = 1.0 / 30, t = 0, aground = 0, minDepth = 99, sailed = 0, yawAbs = 0, rudAbs = 0;
            var last = p2.Body.Pos;
            while (t < 2400 && !pilot.Arrived)
            {
                pilot.Update(dt, ocean, ctrl);
                p2.Step(dt, ocean, ctrl, t); t += dt;
                if (args.Length > 4 && args[4] == "trace" && rn.StartsWith(args.Length > 5 ? args[5] : "Port-Royal -> Passage") && name == "sloop" && Math.Floor(t / 10) != Math.Floor((t - dt) / 10) && t < 600)
                {
                    var fw = p2.Body.Quat.Rotate(new Vec3d(0, 0, 1)); var bp0 = p2.Body.Pos;
                    Console.WriteLine(FormattableString.Invariant($"     t {t,4:F0} pos ({bp0.X:F0},{bp0.Z:F0}) fond {-world.HeightAt(bp0.X, bp0.Z),5:F1} cap {(Math.Atan2(-fw.X, fw.Z) * 180 / Math.PI + 360) % 360,4:F0} erre {Math.Sqrt(p2.Body.Vel.X * p2.Body.Vel.X + p2.Body.Vel.Z * p2.Body.Vel.Z) / 0.5144,5:F1} nd barre {ctrl.Rudder,4:F1} près {pilot.Helm.Beating} lof {pilot.Helm.Wearing} vire {p2.TackPhase} échoue {p2.Aground:F2} marque {pilot.Leg}"));
                }
                if (p2.Aground > 0.01) aground += dt;
                yawAbs += Math.Abs(p2.Body.AngVel.Y) * dt; rudAbs += Math.Abs(ctrl.Rudder) * dt;
                var bp = p2.Body.Pos;
                minDepth = Math.Min(minDepth, -world.HeightAt(bp.X, bp.Z));
                sailed += Math.Sqrt((bp.X - last.X) * (bp.X - last.X) + (bp.Z - last.Z) * (bp.Z - last.Z));
                last = bp;
            }
            var e = route[^1]; var bpe = p2.Body.Pos;
            double left = Math.Sqrt((e.X - bpe.X) * (e.X - bpe.X) + (e.Z - bpe.Z) * (e.Z - bpe.Z));
            Console.WriteLine(FormattableString.Invariant($"  {rn} : {route.Count} marques, {len / 1852:F2} M ; {(pilot.Arrived ? "ARRIVÉ" : "pas arrivé, reste " + left.ToString("F0") + " m, marque " + pilot.Leg)} en {t / 60:F1} min, {sailed / 1852:F2} M parcourus, échoué {aground:F0} s, fond mini {minDepth:F1} m ; lacet moyen {yawAbs / t * 180 / Math.PI:F1}°/s, barre moyenne {rudAbs / t:F2}"));
        }
    }
    // LE CHALAND POUR CARTHAGÈNE : la route du large, puis le cap sur la ville
    {
        var spec = ShipSpec.FromJson(File.ReadAllText(Path.Combine(shipsDir, "barge.json")));
        double need = spec.Hull.KeelDepth + spec.Hull.KeelExtra + 1.5;
        var route = HarbourRoute.ToSea(world, pr.Item1, pr.Item2, need)!;
        var ocean = new Ocean { Swell = 1.0, Time = 0 };
        ocean.SetSeaState(force, windDeg);
        var p2 = new ShipPhysics(spec, new HullLines(spec));
        var ctrl = new Controls();
        p2.Settle(ocean, ctrl);
        p2.World = world;
        p2.Body.Pos = new Vec3d(route[0].X, p2.Body.Pos.Y, route[0].Z);
        p2.Body.Quat = Quatd.FromAxisAngle(new Vec3d(0, 1, 0), Math.Atan2(route[1].X - route[0].X, route[1].Z - route[0].Z));
        var pilot = new HarbourPilot(p2, world, route, need) { Onward = world.Geo.ToXZ(10.4236, -75.5253) };
        double dt = 1.0 / 30, t = 0, beyondAt = -1, aground = 0;
        while (t < 4200 && (beyondAt < 0 || t < beyondAt + 120))
        {
            pilot.Update(dt, ocean, ctrl);
            p2.Step(dt, ocean, ctrl, t); t += dt;
            if (p2.Aground > 0.01) aground += dt;
            if (beyondAt < 0 && pilot.Beyond) beyondAt = t;
        }
        var on = pilot.Onward!.Value; var bp = p2.Body.Pos;
        Console.WriteLine(FormattableString.Invariant($"{spec.Name} (fond voulu {need:F1} m) : {(beyondAt < 0 ? "n'a pas gagné le large" : "au large en " + (beyondAt / 60).ToString("F1") + " min")}, échoué {aground:F0} s ; deux minutes après, cap {Compass.HeadingDeg(p2.Body.Quat.Rotate(new Vec3d(0, 0, 1))):F0}° pour un relèvement de Carthagène au {Compass.HeadingDeg(new Vec3d(on.X - bp.X, 0, on.Z - bp.Z)):F0}°"));
    }
    Config.WindGain = 1.0; Config.CrewTacks = false;
}

/* LA COQUE CATAPULTÉE PAR UNE CRÊTE : ce que fait son étrave quand elle sort de
   l'eau. Sous voiles, vent de trois quarts avant, par gros temps, dix minutes.
   Trois amortisseurs de tangage : l'ancien (plein même hors de l'eau), et deux
   lois qui le règlent sur la carène mouillée.
     dotnet run --project core/NavalSim.Lab -c Release -- envol frigate17e 9 */
void Envol()
{
    string name = args.Length > 1 ? args[1] : "frigate17e";
    Config.WindGain = args.Length > 5 ? double.Parse(args[5], CultureInfo.InvariantCulture) : 8;
    double force = args.Length > 2 ? double.Parse(args[2], CultureInfo.InvariantCulture) : 9;
    double off = args.Length > 3 ? double.Parse(args[3], CultureInfo.InvariantCulture) : 60;
    var spec = ShipSpec.FromJson(File.ReadAllText(Path.Combine(shipsDir, name + ".json")));
    Console.WriteLine($"{spec.Name}, force {force}, vent a {off} deg de l etrave, sous voiles, 10 min");
    foreach (var (label, old, lo, hi, brk) in new[] { ("ancien, sans frein ", true, 0.1, 0.5, 0.0), ("etrave, sans frein ", false, 0.1, 0.5, 0.0), ("etrave, frein 0,5  ", false, 0.1, 0.5, 0.5), ("etrave, frein 1    ", false, 0.1, 0.5, 1.0) })
    {
        var ocean = new Ocean { Swell = 1.35, Time = 0 };
        ocean.SetSeaState(force, 0);
        var p2 = new ShipPhysics(spec, new HullLines(spec)) { DampInAir = old, WetHoldLo = lo, WetHoldHi = hi, SlamBrake = brk };
        var ctrl = new Controls { Throttle = 0, Rudder = 0, Sheet = 0.3, SailsSet = true };
        p2.Settle(ocean, ctrl);
        double yaw = -off * Math.PI / 180;
        p2.Body.Quat = Quatd.FromAxisAngle(new Vec3d(0, 1, 0), yaw);
        double dt = 1.0 / 60, t = 0;
        int n = 0, light = 0, air = 0;
        double t2 = 0, t1 = 0, downPeak = 0, downSum = 0, wetMin = 1, sp = 0, he2 = 0, heelMax = 0;
        int ep = 0; bool inLight = false; double epPeak = 0;
        for (int k = 0; k < 60 * 600; k++)
        {
            if (p2.OptSheet is double o) ctrl.Sheet = o;
            var f = p2.Body.Quat.Rotate(new Vec3d(0, 0, 1));
            double hdg = Math.Atan2(f.X, f.Z);
            ctrl.Rudder = Math.Clamp(-(Math.IEEERemainder(yaw - hdg, 2 * Math.PI)) * 3, -1, 1);
            p2.Step(dt, ocean, ctrl, t); t += dt;
            if (k < 60 * 30) continue;
            n++;
            var fw = p2.Body.Quat.Rotate(new Vec3d(0, 0, 1));
            double trim = Math.Asin(Math.Clamp(fw.Y, -1, 1)) * 180 / Math.PI;
            t1 += trim; t2 += trim * trim;
            var right = p2.Body.Quat.Rotate(new Vec3d(1, 0, 0));
            // + : l etrave qui tombe (rotation autour de +x local, tribord = -x)
            double down = p2.Body.AngVel.Dot(right) * 180 / Math.PI;
            sp += Math.Sqrt(p2.Body.Vel.X * p2.Body.Vel.X + p2.Body.Vel.Z * p2.Body.Vel.Z);
            wetMin = Math.Min(wetMin, p2.Wet);
            double herr = Math.IEEERemainder(yaw - Math.Atan2(fw.X, fw.Z), 2 * Math.PI) * 180 / Math.PI;
            he2 += herr * herr;
            var upv = p2.Body.Quat.Rotate(new Vec3d(0, 1, 0));
            heelMax = Math.Max(heelMax, Math.Acos(Math.Clamp(upv.Y, -1, 1)) * 180 / Math.PI);
            if (p2.Wet < 0.35) air++;
            bool now = p2.Wet < 0.7;
            if (now) { light++; epPeak = Math.Max(epPeak, down); downPeak = Math.Max(downPeak, down); }
            if (inLight && !now) { ep++; downSum += epPeak; epPeak = 0; }
            inLight = now;
        }
        double mean = t1 / n, rms = Math.Sqrt(Math.Max(0, t2 / n - mean * mean));
        Console.WriteLine($"  {label} : allegee (mouille < 0,7) {100.0 * light / n,5:F2} %, en l air {100.0 * air / n,4:F2} %, "
            + $"{ep} episode(s), etrave qui tombe : {(ep > 0 ? downSum / ep : 0),5:F1} deg/s en moyenne, {downPeak,5:F1} au plus ; "
            + $"tangage ecart-type {rms:F2} deg, {sp / n / 0.5144:F1} nd, mouille min {wetMin:F2}, cap a {Math.Sqrt(he2 / n):F0} deg pres, inclinaison max {heelMax:F0} deg");
    }
}

/* LA DÉRIVE : l'angle entre l'étrave et la route, par allure, selon le gain de
   vent et le frein de crête. Une coque carrée dérive de 5 à 15° ; au-delà, elle
   glisse en crabe.
     dotnet run --project core/NavalSim.Lab -c Release -- derive frigate17e 4 */
void Derive()
{
    string name = args.Length > 1 ? args[1] : "frigate17e";
    Config.WindGain = args.Length > 5 ? double.Parse(args[5], CultureInfo.InvariantCulture) : 8;
    double force = args.Length > 2 ? double.Parse(args[2], CultureInfo.InvariantCulture) : 4;
    var spec = ShipSpec.FromJson(File.ReadAllText(Path.Combine(shipsDir, name + ".json")));
    Console.WriteLine($"{spec.Name}, force {force} : vitesse et derive (angle etrave / route), 4 min");
    foreach (var (gain, brk) in new[] { (1.0, 0.0), (1.0, 0.5), (3.0, 0.0), (3.0, 0.5) })
    {
        Config.WindGain = gain;
        string line = $"  gain {gain:F0}, frein {brk:F1} :";
        foreach (double off in new[] { 45.0, 60.0, 90.0, 135.0 })
        {
            var ocean = new Ocean { Swell = 1.0, Time = 0 };
            ocean.SetSeaState(force, 0);
            var p2 = new ShipPhysics(spec, new HullLines(spec)) { SlamBrake = brk };
            var ctrl = new Controls { Throttle = 0, Rudder = 0, Sheet = 0.3, SailsSet = true };
            p2.Settle(ocean, ctrl);
            double yaw = -off * Math.PI / 180;
            p2.Body.Quat = Quatd.FromAxisAngle(new Vec3d(0, 1, 0), yaw);
            double dt = 1.0 / 60, t = 0, lee = 0, sp = 0; int n = 0;
            for (int k = 0; k < 60 * 240; k++)
            {
                if (p2.OptSheet is double o) ctrl.Sheet = o;
                var f = p2.Body.Quat.Rotate(new Vec3d(0, 0, 1));
                double hdg = Math.Atan2(f.X, f.Z);
                ctrl.Rudder = Math.Clamp(-(Math.IEEERemainder(yaw - hdg, 2 * Math.PI)) * 3, -1, 1);
                p2.Step(dt, ocean, ctrl, t); t += dt;
                if (k < 60 * 180) continue;
                var v = p2.Body.Vel;
                double vf = v.X * f.X + v.Z * f.Z;
                var r = p2.Body.Quat.Rotate(new Vec3d(-1, 0, 0));
                double vl = v.X * r.X + v.Z * r.Z;
                lee += Math.Atan2(Math.Abs(vl), Math.Max(0.05, vf)) * 180 / Math.PI;
                sp += Math.Sqrt(v.X * v.X + v.Z * v.Z); n++;
            }
            line += $"  {off,3:F0} deg {sp / n / 0.5144,5:F1} nd derive {lee / n,4:F1} deg";
        }
        Console.WriteLine(line);
    }
    Config.WindGain = 1.0;
}

/* LA SOUTE QUI LA COUPE EN DEUX, au banc : la masse et son moment se
   retrouvent-ils dans les deux moitiés, et que fait chacune dans les trois
   minutes qui suivent — l'assiette, l'enfoncement, le moment où elle sombre.
     dotnet run --project core/NavalSim.Lab -c Release -- soute frigate17e 2 3 */
/* LA RUPTURE EN ROUTE, comme en jeu (ShipDemo.Breakup) : le navire file à tant de
   noeuds, la soute saute, le souffle écarte les moitiés (2 m/s à elles deux, 0,6 vers le
   haut), et l'on suit où va chaque moitié — le long du cap d'origine, en profondeur et en
   assiette. [fiche] [noeuds] [cloison] */
void Rupture()
{
    string name = args.Length > 1 ? args[1] : "frigate17e";
    double kn = args.Length > 2 ? double.Parse(args[2], CultureInfo.InvariantCulture) : 6;
    int k = args.Length > 3 ? int.Parse(args[3]) : 2;
    Config.WindGain = 8;
    if (args.Length > 5) ShipPhysics.AftBallast = double.Parse(args[5], CultureInfo.InvariantCulture);
    if (args.Length > 6) ShipPhysics.BallastSlide = double.Parse(args[6], CultureInfo.InvariantCulture);
    if (args.Length > 7) ShipPhysics.AftLeak = double.Parse(args[7], CultureInfo.InvariantCulture);
    var spec = ShipSpec.FromJson(File.ReadAllText(Path.Combine(shipsDir, name + ".json")));
    var ocean = new Ocean { Swell = 1.0, Time = 0 };
    ocean.SetSeaState(3, 0);
    var aft = new ShipPhysics(spec, new HullLines(spec));
    var ctrl = new Controls { Throttle = 0, Rudder = 0, Sheet = 0, SailsSet = false };
    aft.Settle(ocean, ctrl);
    var f = aft.Body.Quat.Rotate(new Vec3d(0, 0, 1));
    aft.Body.Vel = f * (kn * 0.5144);
    aft.BlowUp();
    var bow = aft.SplitOff(k);
    double total = aft.Body.Mass + bow.Body.Mass;
    bow.Body.Vel += f * (2.0 * aft.Body.Mass / total) + new Vec3d(0, 0.6, 0);
    aft.Body.Vel += f * (-2.0 * bow.Body.Mass / total) + new Vec3d(0, 0.6, 0);
    Vec3d a0 = aft.Body.Pos, b0 = bow.Body.Pos;
    Console.WriteLine(FormattableString.Invariant($"{spec.Name} à {kn} noeuds, rompue à la cloison {k} ; tranche {bow.CutArea:F1} m² ; avant {bow.Body.Mass / 1000:F0} t, arrière {aft.Body.Mass / 1000:F0} t"));
    double dt = 1.0 / 60, t = 0, goneA = -1, goneB = -1;
    string Row(ShipPhysics s, Vec3d p0)
    {
        var c = s.Body.Quat.Rotate(s.Body.Com) + s.Body.Pos;
        var ff = s.Body.Quat.Rotate(new Vec3d(0, 0, 1));
        double trim = Math.Asin(Math.Clamp(ff.Y, -1, 1)) * 180 / Math.PI;
        string comps = "";
        foreach (var cc in s.Comps) if (cc.Cap > 0) comps += FormattableString.Invariant($" {cc.Vol / cc.Cap * 100:F0}%");
        return FormattableString.Invariant($"avancé {(s.Body.Pos - p0).Dot(f),6:F1} m, vz {s.Body.Vel.Y,5:F2}, centre {c.Y,6:F1} m, assiette {trim,6:F1}° [{comps} ]{(s.Foundered ? " SOMBRÉE" : "")}");
    }
    for (int i = 0; i <= 60 * 150; i++)
    {
        if (i % (60 * (t < 20 ? 2 : 5)) == 0)
            Console.WriteLine(args.Length > 4 && args[4] == "arriere" ? FormattableString.Invariant($"  t {t,3:F0} s  arrière : {Row(aft, a0)}") : FormattableString.Invariant($"  t {t,3:F0} s  avant : {Row(bow, b0)}   |  arrière : {Row(aft, a0)}"));
        aft.Step(dt, ocean, ctrl, t);
        bow.Step(dt, ocean, ctrl, t);
        t += dt;
        if (goneA < 0 && aft.SubmergedFrac > 0.999 && aft.DepthBelow > 6) goneA = t;
        if (goneB < 0 && bow.SubmergedFrac > 0.999 && bow.DepthBelow > 6) goneB = t;
    }
    Console.WriteLine(FormattableString.Invariant($"  disparue : avant à {goneB:F0} s, arrière à {goneA:F0} s"));
}

/* LA HAUSSE : où passe un boulet, selon le coin sous la culasse — la même balistique
   que Gunnery (440 m/s, traînée c·v², la charge du calibre), une bouche à 4 m sur la
   mer, tirée d'un pont immobile. Pour chaque hausse : sa hauteur à 100, 200, 300 m,
   et où elle tombe. [k] (0,35 : une pièce de chasse ; 1 : le travers d'une frégate) */
void Hausse()
{
    double k = args.Length > 1 ? double.Parse(args[1], CultureInfo.InvariantCulture) : 0.35;
    double over = 4, c = 0.00097 / k, v0m = Ball.Muzzle * Ball.Charge(k);
    Console.WriteLine(FormattableString.Invariant($"calibre k {k}, bouche à {over} m, {v0m:F0} m/s"));
    foreach (double deg in new[] { -2.0, 0, 1, 2, 3, 4, 6, 8, 10, 12 })
    {
        double el = Gunnery.PointBlank(over) + deg * Math.PI / 180;
        double x = 0, y = over, vx = v0m * Math.Cos(el), vy = v0m * Math.Sin(el), h = 0.002;
        double? y100 = null, y200 = null, y300 = null;
        while (y > 0 && x < 3000)
        {
            double sp = Math.Sqrt(vx * vx + vy * vy);
            vx += -c * sp * vx * h; vy += -c * sp * vy * h - Config.G * h;
            double x0 = x; x += vx * h; y += vy * h;
            if (x0 < 100 && x >= 100) y100 = y;
            if (x0 < 200 && x >= 200) y200 = y;
            if (x0 < 300 && x >= 300) y300 = y;
        }
        string Y(double? v) => v is double d ? FormattableString.Invariant($"{d,6:F1} m") : "   à l'eau";
        Console.WriteLine(FormattableString.Invariant($"  hausse {deg,5:F0}° : à 100 m {Y(y100)}, à 200 m {Y(y200)}, à 300 m {Y(y300)}, tombe à {x,5:F0} m"));
    }
}

/* LES ASTRES DU PILOTE : la Polaire portée à 1690 par la précession, et une nuit de
   Port-Royal — sa hauteur, sa correction des Gardes, la latitude qu'on en tire ; puis
   la hauteur de midi du soleil et la latitude qu'elle donne. */
void Astres()
{
    foreach (double y in new[] { 2000.0, 1700.0, 1690.0, 1600.0 })
    {
        var (ra, dec) = Sights.Polaris(y);
        Console.WriteLine(FormattableString.Invariant($"Polaire en {y} : α {ra / 15:F2} h, δ {dec:F3}°, à {90 - dec:F2}° du pôle"));
    }
    double lat = 17.94, lon = -76.84;
    var (ra90, dec90) = Sights.Polaris(1690);
    var day = new DateTime(1690, 10, 8);
    foreach (double h in new[] { 20.0, 22.0, 0.0, 2.0, 4.0 })
    {
        var d = h < 12 ? day.AddDays(1) : day;
        double lha = Sights.HourAngle(d, h, lon, ra90);
        double alt = Sights.Altitude(lat, dec90, lha);
        double corr = Sights.GuardsCorrection(dec90, lha);
        Console.WriteLine(FormattableString.Invariant($"  {h:00} h : angle horaire {lha:F0}°, hauteur {Sights.Dm(alt)}, correction {Sights.Dm(corr)}, latitude {Sights.Dm(alt + corr)} (vraie {Sights.Dm(lat)})"));
    }
    string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
    var cat = StarCatalog.FromJson(File.ReadAllText(Path.Combine(root, "world", "etoiles.json")), 1690.77);
    Console.WriteLine(FormattableString.Invariant($"Catalogue : {cat.Stars.Count} étoiles en 1690"));
    foreach (var nm in new[] { "la Polaire", "Dubhe", "Mérak", "Sirius", "Véga", "Arcturus", "Acrux" })
        if (cat.Find(nm) is Star s)
        {
            double lha = Sights.HourAngle(day, 22, lon, s.Ra);
            Console.WriteLine(FormattableString.Invariant($"  {nm,-12} α {s.Ra / 15:F2} h δ {s.Dec,7:F2}° mag {s.Mag:F2} — à 22 h à Port-Royal : hauteur {Sights.Altitude(lat, s.Dec, lha),6:F1}°"));
        }
    var cal = new NavalSim.Core.Calendar("1690-10-08");
    double decl = cal.Declination();
    double hs = Sights.SunAltitude(lat, decl, 12);
    Console.WriteLine(FormattableString.Invariant($"Soleil le 8 octobre 1690 à midi : déclinaison {Sights.Dm(decl)}, hauteur {Sights.Dm(hs)}, latitude {Sights.Dm(Sights.LatitudeFromSun(hs, decl, Sights.SunSouth(lat, decl)))}"));
    Console.WriteLine(FormattableString.Invariant($"  à 11 h 40 : hauteur {Sights.Dm(Sights.SunAltitude(lat, decl, 11.667))} — prise trop tôt, la latitude serait {Sights.Dm(Sights.LatitudeFromSun(Sights.SunAltitude(lat, decl, 11.667), decl, true))}"));
}

void Soute()
{
    string name = args.Length > 1 ? args[1] : "frigate17e";
    Config.WindGain = args.Length > 5 ? double.Parse(args[5], CultureInfo.InvariantCulture) : 8;
    int k = args.Length > 2 ? int.Parse(args[2]) : 2;
    double force = args.Length > 3 ? double.Parse(args[3], CultureInfo.InvariantCulture) : 3;
    if (args.Length > 4) ShipPhysics.HalfDeckLeak = double.Parse(args[4], CultureInfo.InvariantCulture);
    if (args.Length > 5) ShipPhysics.HalfFloodable = double.Parse(args[5], CultureInfo.InvariantCulture);
    var spec = ShipSpec.FromJson(File.ReadAllText(Path.Combine(shipsDir, name + ".json")));
    var ocean = new Ocean { Swell = 1.0, Time = 0 };
    ocean.SetSeaState(force, 0);
    var aft = new ShipPhysics(spec, new HullLines(spec));
    var ctrl = new Controls { Throttle = 0, Rudder = 0, Sheet = 0, SailsSet = false };
    aft.Settle(ocean, ctrl);
    double m0 = aft.Body.Mass, mz0 = aft.Body.Mass * aft.Body.Com.Z, v0 = aft.HullVolume;
    aft.BlowUp();
    var bow = aft.SplitOff(k);
    double zc = aft.Bulkhead(k);
    Console.WriteLine($"{spec.Name}, coupee a la cloison {k} (z = {zc:F1} m), force {force}, pont {ShipPhysics.HalfDeckLeak}, inondable {ShipPhysics.HalfFloodable}");
    Console.WriteLine($"  volume   {v0:F1} m3 = {aft.HullVolume:F1} + {bow.HullVolume:F1}");
    // la masse seche se lit en retirant l eau deja embarquee
    double dryA = aft.Body.Mass - aft.FloodVol * Config.Rho, dryB = bow.Body.Mass - bow.FloodVol * Config.Rho;
    Console.WriteLine($"  masse    {m0 / 1000:F1} t = {dryA / 1000:F1} + {dryB / 1000:F1} t a sec");
    Console.WriteLine($"  sondes   {aft.Probes.Length} + {bow.Probes.Length}, voies d eau {aft.Breaches.Count} + {bow.Breaches.Count}");
    double dt = 1.0 / 60, t = 0;
    double sinkA = -1, sinkB = -1;
    for (int i = 0; i <= 60 * 180; i++)
    {
        if (i % (60 * (t < 40 ? 3 : 30)) == 0)
        {
            string Row(ShipPhysics s)
            {
                var f = s.Body.Quat.Rotate(new Vec3d(0, 0, 1));
                double trim = Math.Asin(Math.Clamp(f.Y, -1, 1)) * 180 / Math.PI;
                var c = s.Body.Quat.Rotate(s.Body.Com) + s.Body.Pos;
                string comps = "";
                foreach (var cc in s.Comps) if (cc.Cap > 0) comps += $" {cc.Vol:F0}/{cc.Cap:F0}";
                return $"assiette {trim,6:F1} deg, centre a {c.Y,6:F1} m, eau {s.FloodVol,6:F0} m3 [{comps} ], immerge {s.SubmergedFrac * 100,5:F1} %, masse {s.Body.Mass / 1000:F0} t{(s.Foundered ? " SOMBREE" : "")}";
            }
            Console.WriteLine($"  t {t,4:F0} s  arriere : {Row(aft)}");
            Console.WriteLine($"          avant   : {Row(bow)}");
        }
        aft.Step(dt, ocean, ctrl, t);
        bow.Step(dt, ocean, ctrl, t);
        t += dt;
        if (sinkA < 0 && aft.Foundered) sinkA = t;
        if (sinkB < 0 && bow.Foundered) sinkB = t;
    }
    Console.WriteLine($"  sombree : arriere a {(sinkA < 0 ? "jamais" : sinkA.ToString("F0") + " s")}, avant a {(sinkB < 0 ? "jamais" : sinkB.ToString("F0") + " s")}");
}

/* LE RELIEF D'UNE ZONE ET CE QUE SES SEMIS Y POSENT : la répartition des
   altitudes (pour choisir les franges), et le nombre posé par semis.
     dotnet run --project core/NavalSim.Lab -c Release -- semis ilot-cocotiers */
void Semis()
{
    string key = args.Length > 1 ? args[1] : "ilot-cocotiers";
    string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
    var region = RegionSpec.FromJson(File.ReadAllText(Path.Combine(root, "world", "caraibes.json")));
    var (iw, ih, grey) = GreyPng.Decode(File.ReadAllBytes(Path.Combine(root, region.Relief.Image)));
    // les reliefs locaux, comme Godot les charge (WorldLoad) : sans eux, pas d'îlot
    var imgs = new List<World.PatchImage>();
    foreach (var pz in region.Patches)
    {
        string pi = Path.Combine(root, pz.Image);
        if (!File.Exists(pi)) continue;
        var (pw, ph, pg) = GreyPng.Decode(File.ReadAllBytes(pi));
        imgs.Add(new World.PatchImage(pz, pw, ph, pg));
    }
    var world = new World(region, iw, ih, grey, m => Console.WriteLine("  ! " + m), imgs);
    var patch = region.Patches.Find(q => q.Key == key);
    if (patch == null) { Console.WriteLine("zone inconnue"); return; }
    var (ax, az) = world.Geo.ToXZ(patch.South, patch.West);
    var (bx, bz) = world.Geo.ToXZ(patch.North, patch.East);
    double x0 = Math.Min(ax, bx), x1 = Math.Max(ax, bx), z0 = Math.Min(az, bz), z1 = Math.Max(az, bz);
    Console.WriteLine($"{key} : {x1 - x0:F0} x {z1 - z0:F0} m, de ({x0:F0}, {z0:F0}) a ({x1:F0}, {z1:F0}) vrais");
    int[] bins = new int[12]; double[] edges = { -999, -1, 0, 1, 2, 3, 4, 6, 8, 12, 20, 40, 999 };
    double hmax = -999; int land = 0;
    for (double x = x0; x < x1; x += 4) for (double z = z0; z < z1; z += 4)
    {
        double h = world.HeightAt(x, z); hmax = Math.Max(hmax, h); if (h > 0) land++;
        for (int i = 0; i < 12; i++) if (h >= edges[i] && h < edges[i + 1]) { bins[i]++; break; }
    }
    for (int i = 0; i < 12; i++) if (bins[i] > 0) Console.WriteLine($"  {edges[i],5:F0} a {edges[i + 1],5:F0} m : {bins[i] * 16,8} m2");
    Console.WriteLine($"  terre {land * 16} m2, sommet {hmax:F1} m");
    foreach (var s in region.Scatters)
        if (s.Patch == key || s.Patch.Length == 0) Console.WriteLine($"  semis « {s.Name} » : {Scatter.Place(world, s).Count} / {s.Count}");

    // la carte de la zone, une case pour 28 m : ~ eau, . grève (0-2 m), : 2-6, + 6-20, # au-dessus ;
    // chaque semis y marque ses places de l'initiale de son rang (a, b, c...)
    const double cell = 28;
    int nx = (int)((x1 - x0) / cell), nz = (int)((z1 - z0) / cell);
    var map = new char[nz, nx];
    for (int j = 0; j < nz; j++) for (int i = 0; i < nx; i++)
    {
        double h = world.HeightAt(x0 + (i + 0.5) * cell, z0 + (j + 0.5) * cell);
        map[j, i] = h <= 0 ? '~' : h < 2 ? '.' : h < 6 ? ':' : h < 20 ? '+' : '#';
    }
    int rank = 0;
    foreach (var s in region.Scatters)
    {
        char mark = (char)('a' + rank++);
        if (s.Patch != key && s.Patch.Length != 0) continue;
        foreach (var p in Scatter.Place(world, s))
        {
            int i = (int)((p.X - x0) / cell), j = (int)((p.Z - z0) / cell);
            if (i >= 0 && i < nx && j >= 0 && j < nz) map[j, i] = mark;
        }
        Console.WriteLine($"  {mark} = {s.Name}");
    }
    // nord en haut ; est = -x monde, donc on retourne x pour avoir l est a droite
    for (int j = nz - 1; j >= 0; j--)
    {
        var line = new System.Text.StringBuilder();
        for (int i = nx - 1; i >= 0; i--) line.Append(map[j, i]);
        Console.WriteLine("  " + line);
    }
}

/* LE RELIEF LE LONG D'UNE LIGNE, en metres vrais : d'ou part l'herbe, ou s'arrete la greve.
     dotnet run --project core/NavalSim.Lab -c Release -- profil x z cap longueur */
void Profil()
{
    double px = double.Parse(args[1], CultureInfo.InvariantCulture), pz = double.Parse(args[2], CultureInfo.InvariantCulture);
    double cap = double.Parse(args[3], CultureInfo.InvariantCulture) * Math.PI / 180, len = double.Parse(args[4], CultureInfo.InvariantCulture);
    string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
    var region = RegionSpec.FromJson(File.ReadAllText(Path.Combine(root, "world", "caraibes.json")));
    var (iw, ih, grey) = GreyPng.Decode(File.ReadAllBytes(Path.Combine(root, region.Relief.Image)));
    var imgs = new List<World.PatchImage>();
    foreach (var pz2 in region.Patches)
    {
        string pi = Path.Combine(root, pz2.Image);
        if (!File.Exists(pi)) continue;
        var (pw, ph, pg) = GreyPng.Decode(File.ReadAllBytes(pi));
        imgs.Add(new World.PatchImage(pz2, pw, ph, pg));
    }
    var world = new World(region, iw, ih, grey, m => { }, imgs);
    for (double r = 0; r <= len; r += 5)
        Console.WriteLine($"  {r,5:F0} m : {world.HeightAt(px + Math.Sin(cap) * r, pz + Math.Cos(cap) * r),7:F2}");
}

/* LE CENTRE-VILLE D'UN PORT : l'axe trouve, les pates poses, et la carte des
   rues (# pate, = pave, h maison semee, . herbe, : greve, ~ eau ; 6 m la case).
     dotnet run --project core/NavalSim.Lab -c Release -- centre port-royal */
void Centre()
{
    string key = args.Length > 1 ? args[1] : "port-royal";
    string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
    var region = RegionSpec.FromJson(File.ReadAllText(Path.Combine(root, "world", "caraibes.json")));
    var (iw, ih, grey) = GreyPng.Decode(File.ReadAllBytes(Path.Combine(root, region.Relief.Image)));
    var imgs = new List<World.PatchImage>();
    foreach (var pz in region.Patches)
    {
        string pi = Path.Combine(root, pz.Image);
        if (!File.Exists(pi)) continue;
        var (pw, ph, pg) = GreyPng.Decode(File.ReadAllBytes(pi));
        imgs.Add(new World.PatchImage(pz, pw, ph, pg));
    }
    var world = new World(region, iw, ih, grey, m => { }, imgs);
    var isl = world.ByKey(key);
    if (isl == null) { Console.WriteLine("port inconnu"); return; }
    var sw = System.Diagnostics.Stopwatch.StartNew();
    var g = world.Grids.FirstOrDefault(q => q.Covers(isl.X, isl.Z, 600));
    Console.WriteLine($"{isl.Name} : grilles tracees en {sw.ElapsedMilliseconds} ms");
    if (g == null) { Console.WriteLine("  pas de centre-ville"); return; }
    Console.WriteLine($"  axe {Math.Atan2(g.Ux, g.Uz) * 180 / Math.PI:F0} deg (cap de l axe, nord 0), centre ({g.Cx:F0}, {g.Cz:F0}), {g.Blocks.Count} pates");
    var houses = Town.Plant(world, isl.X, isl.Z, 420, 150, 7920);
    Console.WriteLine($"  {houses.Count} maisons semees autour");
    const double cell = 6; int half = 50;
    var map = new char[2 * half, 2 * half];
    for (int j = 0; j < 2 * half; j++) for (int i = 0; i < 2 * half; i++)
    {
        double x = g.Cx + (i - half + 0.5) * cell, z = g.Cz + (j - half + 0.5) * cell;
        double h = world.HeightAt(x, z);
        map[j, i] = h <= 0 ? '~' : g.Paved(x, z) && h >= 1.2 ? '=' : h < 2 ? ':' : '.';
    }
    void Mark(double x, double z, char ch)
    {
        int i = (int)Math.Floor((x - g.Cx) / cell) + half, j = (int)Math.Floor((z - g.Cz) / cell) + half;
        if (i >= 0 && i < 2 * half && j >= 0 && j < 2 * half) map[j, i] = ch;
    }
    foreach (var b in g.Blocks)
    {
        // l emprise du pate, echantillonnee
        double ca = Math.Cos(b.Yaw), sa = Math.Sin(b.Yaw);
        for (double a = -b.W / 1.8; a <= b.W / 1.8; a += 2) for (double d = -b.D / 1.8; d <= b.D / 1.8; d += 2)
            Mark(b.X + a * ca + d * sa, b.Z - a * sa + d * ca, '#');
    }
    foreach (var h in houses) Mark(h.X, h.Z, 'h');
    // nord en haut, est (-x) a droite
    for (int j = 2 * half - 1; j >= 0; j--)
    {
        var line = new System.Text.StringBuilder();
        for (int i = 2 * half - 1; i >= 0; i--) line.Append(map[j, i]);
        Console.WriteLine("  " + line);
    }
}

/* DES MARQUES AUX PLACES : pour chaque marque (x,z vrais), le ponton qui part de la
   rive la plus proche, perpendiculaire, jusqu a quatre metres d eau ; ou le navire
   (fiche:x,z) ecarte vers le large jusqu a trouver son fond, le cap le long de la
   rive. Rend les entrees JSON a coller dans la fiche du monde.
     dotnet run --project core/NavalSim.Lab -c Release -- pontons p:x,z ... sloop.json:x,z ... */
void Pontons()
{
    string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
    var region = RegionSpec.FromJson(File.ReadAllText(Path.Combine(root, "world", "caraibes.json")));
    var (iw, ih, grey) = GreyPng.Decode(File.ReadAllBytes(Path.Combine(root, region.Relief.Image)));
    var imgs = new List<World.PatchImage>();
    foreach (var pz in region.Patches)
    {
        string pi = Path.Combine(root, pz.Image);
        if (!File.Exists(pi)) continue;
        var (pw, ph, pg) = GreyPng.Decode(File.ReadAllBytes(pi));
        imgs.Add(new World.PatchImage(pz, pw, ph, pg));
    }
    var world = new World(region, iw, ih, grey, m => { }, imgs);
    var inv = CultureInfo.InvariantCulture;
    /* LE SENS DU LARGE : la pente du RELIEF, et non le champ de distance au
       rivage — celui-ci est tire de la grande carte et ignore les reliefs locaux
       (Port-Royal est retravaille a la main) : il envoyait les pontons a deux
       cents metres de la rive. Le terrain descend vers la mer. */
    (double X, double Z) Sea(double x, double z)
    {
        double gx = world.HeightAt(x + 8, z) - world.HeightAt(x - 8, z);
        double gz = world.HeightAt(x, z + 8) - world.HeightAt(x, z - 8);
        double n = Math.Max(1e-9, Math.Sqrt(gx * gx + gz * gz));
        return (-gx / n, -gz / n);
    }
    // la rive la plus proche : un point de terre dont un voisin est dans l eau
    (double X, double Z) Shore(double x, double z)
    {
        double best = double.MaxValue, bx = x, bz = z;
        for (double u = -150; u <= 150; u += 1.5)
            for (double v = -150; v <= 150; v += 1.5)
            {
                double d2 = u * u + v * v;
                if (d2 >= best) continue;
                double px = x + u, pz = z + v;
                if (world.HeightAt(px, pz) <= 0) continue;
                if (world.HeightAt(px + 1.5, pz) > 0 && world.HeightAt(px - 1.5, pz) > 0
                    && world.HeightAt(px, pz + 1.5) > 0 && world.HeightAt(px, pz - 1.5) > 0) continue;
                best = d2; bx = px; bz = pz;
            }
        return (bx, bz);
    }
    // cap boussole d une direction (x, z) : nord +z, est -x
    double Compass(double dx, double dz) => ((Math.Atan2(-dx, dz) * 180 / Math.PI) % 360 + 360) % 360;
    foreach (var a in args.Skip(1))
    {
        var parts = a.Split(':');
        var xz = parts[1].Split(',');
        double x = double.Parse(xz[0], inv), z = double.Parse(xz[1], inv);
        double mx = x, mz = z;           // la marque : un navire part d elle, un ponton de la rive
        (x, z) = Shore(x, z);
        var (dx, dz) = Sea(x, z);
        if (parts[0] == "p")
        {
            double rx = x - dx * 6, rz = z - dz * 6;         // la racine, six metres a terre
            double len = 20;
            for (; len < 90; len += 1)
                if (world.HeightAt(rx + dx * len, rz + dz * len) <= -4) break;
            len = Math.Clamp(len + 2, 20, 90);
            Console.WriteLine(string.Format(inv, "    {{ \"x\": {0:F1}, \"z\": {1:F1}, \"cap\": {2:F0}, \"longueur\": {3:F0}, \"largeur\": 14 }},",
                rx + dx * len * 0.5, rz + dz * len * 0.5, Compass(dx, dz), len));
        }
        else
        {
            var spec = ShipSpec.FromJson(File.ReadAllText(Path.Combine(root, "ships", parts[0])));
            double draft = spec.Hull.KeelDepth + spec.Hull.KeelExtra;
            double bx = mx, bz = mz; int n = 0;
            // le navire tient par le travers de sa demi-largeur : on l eloigne jusqu a ce que tout le fond porte
            // tout son plan d eau doit porter : l etrave, la poupe et les deux bords, avec du pied
            bool Holds(double px, double pz)
            {
                double L = spec.Hull.Length * 0.5 + 4, B = spec.Hull.Beam * 0.5 + 3;
                for (int q = -2; q <= 2; q++)
                    for (int r = -1; r <= 1; r++)
                    {
                        double ax = -dz * L * q / 2 + dx * B * r, az = dx * L * q / 2 + dz * B * r;
                        if (world.HeightAt(px + ax, pz + az) > -(draft + 2.5)) return false;
                    }
                return true;
            }
            while (n++ < 60 && !Holds(bx, bz)) { bx += dx * 5; bz += dz * 5; }
            Console.WriteLine(string.Format(inv, "    {{ \"fiche\": \"{0}\", \"x\": {1:F1}, \"z\": {2:F1}, \"cap\": {3:F0} }},  // fond {4:F1} m, {5} pas",
                parts[0], bx, bz, Compass(-dz, dx), -world.HeightAt(bx, bz), n - 1));
        }
    }
}

/* L ABRI DU RIVAGE autour d un port : le temps de le batir, et sa carte
   (# terre, puis de la houle pleine a la plus calme : . : - = + *), 24 m la case.
     dotnet run --project core/NavalSim.Lab -c Release -- abri */
void Abri()
{
    string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
    var region = RegionSpec.FromJson(File.ReadAllText(Path.Combine(root, "world", "caraibes.json")));
    var (iw, ih, grey) = GreyPng.Decode(File.ReadAllBytes(Path.Combine(root, region.Relief.Image)));
    var imgs = new List<World.PatchImage>();
    foreach (var pz in region.Patches)
    {
        string pi = Path.Combine(root, pz.Image);
        if (!File.Exists(pi)) continue;
        var (pw, ph, pg) = GreyPng.Decode(File.ReadAllBytes(pi));
        imgs.Add(new World.PatchImage(pz, pw, ph, pg));
    }
    var world = new World(region, iw, ih, grey, m => { }, imgs);
    var sw = System.Diagnostics.Stopwatch.StartNew();
    var maps = world.ShelterMaps;
    Console.WriteLine($"{maps.Count} abri(s) batis en {sw.ElapsedMilliseconds} ms");
    foreach (var m in maps)
    {
        Console.WriteLine($"  {m.N} x {m.N} mailles, coin ({m.X0:F0}, {m.Z0:F0})");
        const double cell = 24;
        int n = (int)(m.Size / cell);
        for (int j = n - 1; j >= 0; j--)
        {
            var line = new System.Text.StringBuilder("  ");
            for (int i = n - 1; i >= 0; i--)
            {
                double x = m.X0 + (i + 0.5) * cell, z = m.Z0 + (j + 0.5) * cell;
                if (world.HeightAt(x, z) > 0) { line.Append('#'); continue; }
                double v = world.Shelter(x, z);
                line.Append(v > 0.95 ? '.' : v > 0.8 ? ':' : v > 0.6 ? '-' : v > 0.4 ? '=' : v > 0.25 ? '+' : '*');
            }
            Console.WriteLine(line);
        }
    }
}

/* OU MORD-IL ? Autour de Port-Royal, a l aube : # terre, . rien, puis la force du coin
   pour le merou (m faible, M bon) et le vivaneau (v, V) ; X : bon pour les deux. 120 m la case.
     dotnet run --project core/NavalSim.Lab -c Release -- peche [rayon_km] [heure] */
void Peche()
{
    string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
    var region = RegionSpec.FromJson(File.ReadAllText(Path.Combine(root, "world", "caraibes.json")));
    var (iw, ih, grey) = GreyPng.Decode(File.ReadAllBytes(Path.Combine(root, region.Relief.Image)));
    var imgs = new List<World.PatchImage>();
    foreach (var pz in region.Patches)
    {
        string pi = Path.Combine(root, pz.Image);
        if (!File.Exists(pi)) continue;
        var (pw, ph, pg) = GreyPng.Decode(File.ReadAllBytes(pi));
        imgs.Add(new World.PatchImage(pz, pw, ph, pg));
    }
    var world = new World(region, iw, ih, grey, m => { }, imgs);
    var rules = FishingRules.FromJson(File.ReadAllText(Path.Combine(root, "fishing", "poissons.json")));
    double km = args.Length > 1 ? double.Parse(args[1], CultureInfo.InvariantCulture) : 5;
    double hour = args.Length > 2 ? double.Parse(args[2], CultureInfo.InvariantCulture) : 6.5;
    var home = world.StartPort!;
    var mer = rules.Of("merou")!; var viv = rules.Of("vivaneau")!;
    const double cell = 120;
    int n = (int)(km * 1000 / cell);
    double bestM = 0, bestV = 0; (double, double) atM = default, atV = default;
    int goodM = 0, goodV = 0;
    for (int j = n; j >= -n; j--)
    {
        var line = new System.Text.StringBuilder("  ");
        for (int i = n; i >= -n; i--)
        {
            double x = home.X + i * cell, z = home.Z + j * cell;
            if (i == 0 && j == 0) { line.Append('@'); continue; }
            if (world.HeightAt(x, z) > 0) { line.Append('#'); continue; }
            var (d, sl) = Fishing.BottomAt(world, x, z);
            double rm = Fishing.Rate(mer, d, sl, hour), rv = Fishing.Rate(viv, d, sl, hour);
            if (rm > bestM) { bestM = rm; atM = (x, z); }
            if (rv > bestV) { bestV = rv; atV = (x, z); }
            bool gm = rm > 0.5 * mer.BitesPerHour, gv = rv > 0.5 * viv.BitesPerHour;
            if (gm) goodM++; if (gv) goodV++;
            line.Append(gm && gv ? 'X' : gm ? 'M' : gv ? 'V' : rm > 0.15 * mer.BitesPerHour ? 'm' : rv > 0.15 * viv.BitesPerHour ? 'v' : '.');
        }
        Console.WriteLine(line);
    }
    Console.WriteLine(FormattableString.Invariant($"heure {hour:F1} : activite merou {Fishing.Activity(mer, hour):F2}, vivaneau {Fishing.Activity(viv, hour):F2}"));
    Console.WriteLine(FormattableString.Invariant($"merou : {goodM} bonnes cases, au mieux {bestM:F1} touches/h/ligne a ({atM.Item1:F0}, {atM.Item2:F0}), {Math.Sqrt((atM.Item1 - home.X) * (atM.Item1 - home.X) + (atM.Item2 - home.Z) * (atM.Item2 - home.Z)):F0} m du port"));
    Console.WriteLine(FormattableString.Invariant($"vivaneau : {goodV} bonnes cases, au mieux {bestV:F1} touches/h/ligne a ({atV.Item1:F0}, {atV.Item2:F0}), {Math.Sqrt((atV.Item1 - home.X) * (atV.Item1 - home.X) + (atV.Item2 - home.Z) * (atV.Item2 - home.Z)):F0} m du port"));

    /* UNE SORTIE DE PÊCHE, JOUÉE : trente minutes de jeu avec les cinq lignes du
       sloop sur le meilleur fond de chaque espèce, un pêcheur qui ferre toujours à
       temps, et l'heure du ciel qui tourne au pas du jeu (2 h par minute). Ce que
       la sortie rapporte, en kilos et en pièces — c'est ce qui règle les prix. */
    foreach (var (nom, at) in new[] { ("merou", atM), ("vivaneau", atV) })
    {
        var (d, sl) = Fishing.BottomAt(world, at.Item1, at.Item2);
        double kgSum = 0, pieces = 0; int fish = 0, lost = 0, missed = 0;
        for (int run = 0; run < 20; run++)
        {
            var rng = new Random(run);
            var hl = new HandLines(rules);
            hl.OnCatch = (sp, kg) => { kgSum += kg; pieces += kg * sp.PricePerKg; fish++; };
            hl.OnLost = (sp, kg) => lost++;
            hl.OnMissed = l => missed++;
            hl.Cast(Fishing.Lines(rules, 10, 22));
            double t = 0, h = hour, dt = 0.1;
            while (t < 1800)
            {
                hl.Step(dt, d, sl, h, rng.NextDouble);
                // le pêcheur ferre dans la seconde
                foreach (var l in hl.Lines) if (l.State == HandLines.LineState.Bite && l.T < rules.StrikeWindow - 0.8) { hl.Strike(d); break; }
                t += dt; h = (h + dt / 30) % 24;
            }
        }
        Console.WriteLine(FormattableString.Invariant($"sortie de 30 min sur le fond du {nom} ({d:F0} m) : {kgSum / 20:F0} kg, {fish / 20.0:F1} poissons, {lost / 20.0:F1} lignes cassees, {pieces / 20:F0} pieces ({pieces / 20 / 60:F1} ecus)"));
    }
}
