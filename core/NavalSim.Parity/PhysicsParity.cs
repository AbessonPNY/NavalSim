using System.Text.Json;
using NavalSim.Core;

namespace NavalSim.Parity;

/// <summary>
/// La parité du SOLVEUR, et c'est la plus exigeante des trois.
///
/// Le plan de formes et la mer sont des fonctions PURES : on les interroge en un
/// point et on compare. Le solveur est un INTÉGRATEUR — l'état de chaque pas est
/// l'entrée du suivant — donc un écart d'un ulp au premier pas est amplifié par
/// tous ceux d'après, et deux implémentations honnêtes DOIVENT finir par se
/// séparer. La question n'est donc pas « sont-ils identiques » mais « à quelle
/// vitesse se séparent-ils, et cela reste-t-il sous ce qui compte ».
///
/// D'où un seuil exprimé en unités physiques et non en ulps : un CENTIMÈTRE de
/// position après cinq à sept secondes de simulation, ce qui est mille fois plus
/// fin que tout ce dont dépend le comportement du navire.
/// </summary>
public static class PhysicsParity
{
    public static int Run(string dumpPath, string shipsDir)
    {
        if (!File.Exists(dumpPath))
        {
            Console.Error.WriteLine($"releve solveur introuvable : {dumpPath}");
            Console.Error.WriteLine("lancer d'abord :  node tools/parity-physics.js");
            return 1;
        }

        using var doc = JsonDocument.Parse(File.ReadAllText(dumpPath));
        int failures = 0;

        const double TolPos = 1e-2;      // un centimètre
        const double TolQuat = 1e-4;     // en composante de quaternion
        const double TolBuild = 1e-9;    // ce qui est bâti, pas intégré

        Console.WriteLine("--- le solveur : la trajectoire, pas a pas ---");

        foreach (var sc in doc.RootElement.EnumerateArray())
        {
            string id = sc.GetProperty("id").GetString()!;
            string ship = sc.GetProperty("ship").GetString()!;

            var spec = ShipSpec.FromJson(File.ReadAllText(Path.Combine(shipsDir, ship + ".json")));
            var lines = new HullLines(spec);
            var phys = new ShipPhysics(spec, lines);

            var ocean = new Ocean { Swell = 1.35, Time = 0 };
            ocean.SetSeaState(sc.GetProperty("force").GetDouble(), sc.GetProperty("deg").GetDouble());

            var jc = sc.GetProperty("ctrl");
            var ctrl = new Controls
            {
                Throttle = jc.GetProperty("throttle").GetDouble(),
                Rudder = jc.GetProperty("rudder").GetDouble(),
                Sheet = jc.GetProperty("sheet").GetDouble(),
                SailsSet = jc.GetProperty("sailsSet").GetBoolean()
            };

            // --- ce qui est BÂTI, avant qu'on intègre quoi que ce soit ---
            double worstBuild = 0;
            string whereBuild = "";
            void B(string what, double js, double cs)
            {
                double d = Math.Abs(js - cs);
                double rel = Math.Max(Math.Abs(js), 1.0);
                if (d / rel > worstBuild) { worstBuild = d / rel; whereBuild = what; }
            }

            B("hullVolume", sc.GetProperty("hullVolume").GetDouble(), phys.HullVolume);
            B("cargoCapacity", sc.GetProperty("cargoCapacity").GetDouble(), phys.CargoCapacity);
            B("pumpRate", sc.GetProperty("pumpRate").GetDouble(), phys.PumpRate);
            if (sc.GetProperty("probes").GetInt32() != phys.Probes.Length)
            {
                Console.Error.WriteLine($"  {id}: {sc.GetProperty("probes").GetInt32()} sondes "
                                      + $"cote JS, {phys.Probes.Length} cote C#");
                failures++;
            }
            var jcomps = sc.GetProperty("comps");
            for (int i = 0; i < phys.Comps.Length && i < jcomps.GetArrayLength(); i++)
            {
                B($"comp[{i}].cap", jcomps[i].GetProperty("cap").GetDouble(), phys.Comps[i].Cap);
                B($"comp[{i}].halfB", jcomps[i].GetProperty("halfB").GetDouble(), phys.Comps[i].HalfB);
                B($"comp[{i}].deckY", jcomps[i].GetProperty("deckY").GetDouble(), phys.Comps[i].DeckY);
                B($"comp[{i}].keelY", jcomps[i].GetProperty("keelY").GetDouble(), phys.Comps[i].KeelY);
                B($"comp[{i}].midZ", jcomps[i].GetProperty("midZ").GetDouble(), phys.Comps[i].Mid.Z);
            }

            if (sc.GetProperty("breach").GetBoolean())
            {
                phys.MakeBreach(1, 0.30, 0.20);
                phys.MakeBreach(3, 0.18, 0.35);
            }

            // --- la trajectoire ---
            const double dt = 1.0 / 120;
            double t = 0;
            int steps = sc.GetProperty("steps").GetInt32();
            var track = sc.GetProperty("track");
            int k = 0;

            double worstPos = 0, worstQuat = 0, worstState = 0;
            string whereState = "";
            var nonFinite = new List<string>();
            double firstDivergePos = -1;

            for (int i = 0; i < steps; i++)
            {
                phys.Step(dt, ocean, ctrl, t);
                t += dt;
                if (!(i % 50 == 49 || i == steps - 1)) continue;
                if (k >= track.GetArrayLength()) break;

                var e = track[k++];
                var b = phys.Body;

                double dpos = Math.Sqrt(
                    Sq(e.GetProperty("px").GetDouble() - b.Pos.X) +
                    Sq(e.GetProperty("py").GetDouble() - b.Pos.Y) +
                    Sq(e.GetProperty("pz").GetDouble() - b.Pos.Z));
                if (dpos > worstPos) worstPos = dpos;
                if (firstDivergePos < 0 && dpos > TolPos) firstDivergePos = t;

                double dq = Math.Max(Math.Max(
                    Math.Abs(e.GetProperty("qx").GetDouble() - b.Quat.X),
                    Math.Abs(e.GetProperty("qy").GetDouble() - b.Quat.Y)), Math.Max(
                    Math.Abs(e.GetProperty("qz").GetDouble() - b.Quat.Z),
                    Math.Abs(e.GetProperty("qw").GetDouble() - b.Quat.W)));
                if (dq > worstQuat) worstQuat = dq;

                /* JSON N'A PAS DE NaN : `JSON.stringify` écrit `null` à sa place,
                   donc un relevé nul signale une valeur NON FINIE côté JS. On ne
                   l'écrase pas en silence — on le COMPTE et on le nomme, parce
                   que c'est un défaut de l'original que ce banc vient de
                   trouver, et le taire reviendrait à le perdre. */
                void St(string what, JsonElement el, double cs)
                {
                    if (el.ValueKind == JsonValueKind.Null)
                    {
                        if (!nonFinite.Contains(what)) nonFinite.Add(what);
                        return;
                    }
                    double js = el.GetDouble();
                    double d = Math.Abs(js - cs) / Math.Max(Math.Abs(js), 1.0);
                    if (d > worstState) { worstState = d; whereState = what; }
                }
                St("mass", e.GetProperty("mass"), b.Mass);
                St("comY", e.GetProperty("comY"), b.Com.Y);
                St("submergedFrac", e.GetProperty("sub"), phys.SubmergedFrac);
                St("draft", e.GetProperty("draft"), phys.Draft);
                St("floodVol", e.GetProperty("flood"), phys.FloodVol);
                St("freeSurfaceRise", e.GetProperty("fsr"), phys.FreeSurfaceRise);
                St("sailDrive", e.GetProperty("drive"), phys.SailDrive);
                St("sailLoad", e.GetProperty("load"), phys.SailLoad);
                St("appWindAngle", e.GetProperty("beta"), phys.AppWindAngle);
                St("optSheet", e.GetProperty("opt"), phys.OptSheet ?? -99);
                if (e.GetProperty("tack").GetInt32() != phys.Tack)
                {
                    Console.Error.WriteLine($"  {id}: amure {e.GetProperty("tack").GetInt32()} "
                                          + $"contre {phys.Tack} a t={t:F2}");
                    failures++;
                }
            }

            bool ok = worstBuild <= TolBuild && worstPos <= TolPos && worstQuat <= TolQuat;
            if (!ok) failures++;

            Console.WriteLine($"  {(ok ? "OK   " : "ECART")} {id,-14} {ship,-9}"
                            + $"  bati {worstBuild:E1}   position {worstPos:E2} m"
                            + $"   quat {worstQuat:E2}   etat {worstState:E1}"
                            + (nonFinite.Count > 0
                                ? $"   [JS non fini : {string.Join(", ", nonFinite)}]" : ""));
            if (!ok)
                Console.WriteLine($"        bati sur {whereBuild}, etat sur {whereState}"
                                + (firstDivergePos > 0 ? $", position depasse le cm a t={firstDivergePos:F2} s" : ""));
        }

        return failures;
    }

    /// <summary>
    /// OÙ S'ASSOIT-ELLE ?
    ///
    /// Tout le reste compare des trajectoires, ce qui est nécessaire et pas
    /// suffisant. La question qu'on pose vraiment à un solveur de flottaison est
    /// celle-ci : lâchée dans une eau plate, à quelle hauteur s'arrête-t-elle ?
    ///
    /// C'est aussi le plus long essai de dérive qu'on puisse écrire — settle()
    /// intègre 1 200 fois la racine de sa longueur sur vingt-quatre, soit près de
    /// deux mille pas pour la frégate — et le seul qui vérifie que la mer est bien
    /// RENDUE en partant, ce qui a coûté un bug à l'original.
    /// </summary>
    public static int RunSettle(string dumpPath, string shipsDir)
    {
        if (!File.Exists(dumpPath))
        {
            Console.Error.WriteLine($"releve settle introuvable : {dumpPath}");
            return 1;
        }

        using var doc = JsonDocument.Parse(File.ReadAllText(dumpPath));
        int failures = 0;

        Console.WriteLine("--- settle : ou la coque s'assoit, en eau plate ---");
        foreach (var e in doc.RootElement.EnumerateArray())
        {
            string ship = e.GetProperty("ship").GetString()!;
            var spec = ShipSpec.FromJson(File.ReadAllText(Path.Combine(shipsDir, ship + ".json")));
            var phys = new ShipPhysics(spec, new HullLines(spec));
            var ocean = new Ocean { Swell = 1.35, Time = 0 };
            ocean.SetSeaState(5, 140);
            var ctrl = new Controls { Throttle = 0, Rudder = 0, Sheet = 0, SailsSet = false };

            double y = phys.Settle(ocean, ctrl);

            double dy = Math.Abs(e.GetProperty("y").GetDouble() - y);
            double dDraft = Math.Abs(e.GetProperty("draft").GetDouble() - phys.Draft);
            double dSub = Math.Abs(e.GetProperty("sub").GetDouble() - phys.SubmergedFrac);

            // et la mer a-t-elle bien ete remise comme elle etait ?
            bool seaBack = Math.Abs(ocean.SeaState - e.GetProperty("seaAfter").GetDouble()) < 1e-12
                        && Math.Abs(ocean.WindDeg - e.GetProperty("degAfter").GetDouble()) < 1e-12;

            bool ok = dy < 1e-6 && dDraft < 1e-6 && dSub < 1e-9 && seaBack;
            if (!ok) failures++;

            Console.WriteLine($"  {(ok ? "OK   " : "ECART")} {ship,-14}"
                            + $" y {y,7:F3} m   tirant {phys.Draft,5:F2} m"
                            + $"   immersion {phys.SubmergedFrac * 100,5:F1} %"
                            + $"   ecart y {dy:E1} m"
                            + (seaBack ? "   mer rendue" : "   MER NON RENDUE"));
        }
        return failures;
    }

    static double Sq(double v) => v * v;
}
