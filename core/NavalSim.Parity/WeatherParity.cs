using System.Text.Json;
using NavalSim.Core;

namespace NavalSim.Parity;

/// <summary>
/// La parité de la MÉTÉO : le vent qui se conduit seul, et les dépressions.
///
/// Le vent tire au hasard ; le relevé le fait tirer dans mulberry32 à graine
/// fixe, et on tire ici dans le même : les deux doivent suivre le MÊME chemin,
/// au bruit des bibliothèques mathématiques près (Math.log, Math.cos, Math.exp
/// de V8 et de .NET ne s'accordent pas toujours sur le dernier bit — 1e-9 sur une
/// heure de vent, dix mille fois au-dessus, et une formule fausse s'y verrait en
/// Beaufort).
///
/// Le hachage des dépressions doit être EXACT : un bit de travers et la
/// dépression est ailleurs. Positions, forces et relèvements : 1e-9.
/// </summary>
public static class WeatherParity
{
    // mulberry32, comme tools/parity-weather.js
    sealed class Mulberry32
    {
        uint _a;
        public Mulberry32(uint seed) => _a = seed;
        public double Next()
        {
            unchecked
            {
                _a += 0x6D2B79F5;
                uint t = (_a ^ (_a >> 15)) * (1 | _a);
                t = (t + (t ^ (t >> 7)) * (61 | t)) ^ t;
                return (t ^ (t >> 14)) / 4294967296.0;
            }
        }
    }

    public static int Run(string dumpPath)
    {
        if (!File.Exists(dumpPath))
        {
            Console.Error.WriteLine($"releve de la meteo introuvable : {dumpPath}");
            Console.Error.WriteLine("lancer d'abord :  node tools/parity-weather.js");
            return 1;
        }
        using var doc = JsonDocument.Parse(File.ReadAllText(dumpPath));
        var root = doc.RootElement;
        int failures = 0;
        Console.WriteLine("--- la meteo : le vent, les depressions ---");

        // --- le vent ---
        var wj = root.GetProperty("weather");
        var rng = new Mulberry32((uint)wj.GetProperty("seed").GetInt32());
        var w = new Weather(rng.Next) { On = true };
        var sync = wj.GetProperty("sync");
        w.Sync(sync[0].GetDouble(), sync[1].GetDouble());
        double seaF = sync[0].GetDouble(), seaD = sync[1].GetDouble();
        var dts = wj.GetProperty("dts").EnumerateArray().Select(e => e.GetDouble()).ToArray();
        int n = wj.GetProperty("n").GetInt32();
        var steps = wj.GetProperty("steps").EnumerateArray().ToArray();
        double worstWind = 0; string whereWind = "";
        int k = 0, movedMismatch = 0;
        for (int i = 0; i < n && k < steps.Length; i++)
        {
            double dt = dts[i % dts.Length];
            w.Update(dt);
            bool moved = w.ChaseSea(ref seaF, ref seaD, dt, w.Force, w.Dir);
            if (steps[k].GetProperty("n").GetInt32() != i) continue;
            var s = steps[k++];
            void C(string what, double js, double cs)
            {
                double d = Math.Abs(js - cs);
                if (d > worstWind) { worstWind = d; whereWind = $"{what} au pas {i}"; }
            }
            C("force", s.GetProperty("force").GetDouble(), w.Force);
            C("dir", s.GetProperty("dir").GetDouble(), w.Dir);
            C("meanForce", s.GetProperty("meanForce").GetDouble(), w.MeanForce);
            C("meanDir", s.GetProperty("meanDir").GetDouble(), w.MeanDir);
            C("tgtForce", s.GetProperty("tgtForce").GetDouble(), w.TgtForce);
            C("tgtDir", s.GetProperty("tgtDir").GetDouble(), w.TgtDir);
            C("seaForce", s.GetProperty("seaForce").GetDouble(), seaF);
            C("seaDir", s.GetProperty("seaDir").GetDouble(), seaD);
            if (s.GetProperty("moved").GetBoolean() != moved) movedMismatch++;
        }
        bool windOk = worstWind <= 1e-9 && movedMismatch == 0 && k == steps.Length;
        if (!windOk) failures++;
        Console.WriteLine($"  {(windOk ? "OK  " : "FAIL")}  vent, {n} pas       pire ecart {worstWind:E1}"
                        + (worstWind > 0 ? $"  {whereWind}" : "") + (movedMismatch > 0 ? $"  ; {movedMismatch} mer bougee/pas" : ""));

        // --- le hachage ---
        var storms = new Storms();
        int hashBad = 0, hashN = 0;
        foreach (var h in root.GetProperty("hash").EnumerateArray())
        {
            hashN++;
            if (storms.Hash(h[0].GetInt32(), h[1].GetInt32(), h[2].GetInt32()) != h[3].GetDouble()) hashBad++;
        }
        bool hashOk = hashBad == 0;
        if (!hashOk) failures++;
        Console.WriteLine($"  {(hashOk ? "OK  " : "FAIL")}  hachage, {hashN} tirages      {hashBad} different(s) -- exige au bit pres");

        // --- les cellules ---
        double worstCell = 0; int cellBad = 0, cellN = 0, cellStorms = 0;
        foreach (var c in root.GetProperty("cells").EnumerateArray())
        {
            cellN++;
            bool has = storms.CellStorm(c.GetProperty("i").GetInt32(), c.GetProperty("j").GetInt32(),
                                        c.GetProperty("t").GetDouble(), out var s);
            bool jsHas = !c.TryGetProperty("none", out _);
            if (has != jsHas) { cellBad++; continue; }
            if (!has) continue;
            cellStorms++;
            if (c.GetProperty("spin").GetInt32() != s.Spin) cellBad++;
            worstCell = Math.Max(worstCell, Math.Max(
                Math.Max(Math.Abs(c.GetProperty("x").GetDouble() - s.X), Math.Abs(c.GetProperty("z").GetDouble() - s.Z)),
                Math.Max(Math.Abs(c.GetProperty("r").GetDouble() - s.R), Math.Abs(c.GetProperty("peak").GetDouble() - s.Peak))));
        }
        bool cellOk = cellBad == 0 && worstCell <= 1e-9;
        if (!cellOk) failures++;
        Console.WriteLine($"  {(cellOk ? "OK  " : "FAIL")}  cellules, {cellStorms}/{cellN} depressions   pire ecart {worstCell:E1}"
                        + (cellBad > 0 ? $"  ; {cellBad} en desaccord" : ""));

        // --- le temps en un point ---
        double worstAt = 0; int atBad = 0, atIn = 0, atN = 0;
        foreach (var a in root.GetProperty("at").EnumerateArray())
        {
            atN++;
            bool has = storms.At(a.GetProperty("x").GetDouble(), a.GetProperty("z").GetDouble(),
                                 a.GetProperty("t").GetDouble(), out var q);
            bool jsHas = !a.TryGetProperty("none", out _);
            if (has != jsHas) { atBad++; continue; }
            if (!has) continue;
            atIn++;
            if (a.GetProperty("i").GetString() != $"{q.Storm.I}:{q.Storm.J}") { atBad++; continue; }
            double e = 0;
            e = Math.Max(e, Math.Abs(a.GetProperty("dist").GetDouble() - q.Dist) / Math.Max(1, q.Dist));
            e = Math.Max(e, Math.Abs(a.GetProperty("inten").GetDouble() - q.Inten));
            e = Math.Max(e, Math.Abs(a.GetProperty("force").GetDouble() - q.Force));
            double dd = Math.Abs(a.GetProperty("windDeg").GetDouble() - q.WindDeg);
            e = Math.Max(e, Math.Min(dd, 360 - dd));
            e = Math.Max(e, Math.Abs(a.GetProperty("loom").GetDouble() - q.Loom));
            e = Math.Max(e, Math.Abs(a.GetProperty("toX").GetDouble() - q.ToX));
            e = Math.Max(e, Math.Abs(a.GetProperty("toZ").GetDouble() - q.ToZ));
            worstAt = Math.Max(worstAt, e);
        }
        bool atOk = atBad == 0 && worstAt <= 1e-9;
        if (!atOk) failures++;
        Console.WriteLine($"  {(atOk ? "OK  " : "FAIL")}  temps en un point, {atIn}/{atN} dans une depression   pire ecart {worstAt:E1}"
                        + (atBad > 0 ? $"  ; {atBad} en desaccord" : ""));

        // --- la plus proche ---
        int nearBad = 0; double worstNear = 0;
        foreach (var f in root.GetProperty("nearest").EnumerateArray())
        {
            bool has = storms.Nearest(f.GetProperty("x").GetDouble(), f.GetProperty("z").GetDouble(),
                                      f.GetProperty("t").GetDouble(), 12, null, out var s, out double d);
            bool jsHas = !f.TryGetProperty("none", out _);
            if (has != jsHas || (has && f.GetProperty("i").GetString() != $"{s.I}:{s.J}")) { nearBad++; continue; }
            if (has) worstNear = Math.Max(worstNear, Math.Abs(f.GetProperty("dist").GetDouble() - d));
        }
        bool nearOk = nearBad == 0 && worstNear <= 1e-6;
        if (!nearOk) failures++;
        Console.WriteLine($"  {(nearOk ? "OK  " : "FAIL")}  la plus proche             pire ecart {worstNear:E1} m"
                        + (nearBad > 0 ? $"  ; {nearBad} en desaccord" : ""));

        // --- le climat ---
        var cj = root.GetProperty("climate");
        var crng = new Mulberry32((uint)cj.GetProperty("seed").GetInt32());
        var cal = new Calendar(cj.GetProperty("start").GetString());
        var cl = new Climate(ClimateSettings.FromJson(cj.GetProperty("settings")), crng.Next);
        double hour = cj.GetProperty("hour0").GetDouble();
        var dh = cj.GetProperty("dh").EnumerateArray().Select(e => e.GetDouble()).ToArray();
        int cn = cj.GetProperty("n").GetInt32();
        var csteps = cj.GetProperty("steps").EnumerateArray().ToArray();
        double worstTemp = 0, worstAmt = 0; int flagBad = 0, ck = 0; string whereTemp = "";
        for (int i = 0; i < cn && ck < csteps.Length; i++)
        {
            double dtH = dh[i % dh.Length];
            double storm = Math.Max(0, Math.Sin(i * 0.013)) * 0.8;
            double before = hour;
            hour = (hour + dtH) % 24;
            if (hour < before) cal.NextDay();
            cl.Update(dtH, cal, hour, storm);
            var pr = cl.Precipitation(i % 3 == 0 ? 0.4 : 0);
            if (csteps[ck].GetProperty("n").GetInt32() != i) continue;
            var s = csteps[ck++];
            double dT = Math.Abs(s.GetProperty("temp").GetDouble() - cl.Temp);
            if (dT > worstTemp) { worstTemp = dT; whereTemp = $"jour {cal.Day}, {hour:F2} h"; }
            worstAmt = Math.Max(worstAmt, Math.Abs(s.GetProperty("amount").GetDouble() - cl.Amount));
            worstAmt = Math.Max(worstAmt, Math.Abs(s.GetProperty("pAmount").GetDouble() - pr.Amount));
            if (s.GetProperty("showering").GetBoolean() != cl.Showering || s.GetProperty("snow").GetBoolean() != pr.Snow
                || s.GetProperty("word").GetString() != cl.Word() || s.GetProperty("day").GetInt32() != cal.Day) flagBad++;
        }
        bool climOk = worstTemp <= 1e-9 && worstAmt <= 1e-9 && flagBad == 0 && ck == csteps.Length;
        if (!climOk) failures++;
        Console.WriteLine($"  {(climOk ? "OK  " : "FAIL")}  climat, {cn} pas sur {cal.Day} jours   temperature {worstTemp:E1}"
                        + (worstTemp > 0 ? $" ({whereTemp})" : "") + $", averses {worstAmt:E1}"
                        + (flagBad > 0 ? $"  ; {flagBad} en desaccord (averse, neige, mot ou jour)" : ""));
        return failures;
    }
}
