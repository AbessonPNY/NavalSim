using System;
using System.IO;
using System.Text.Json;
using NavalSim.Core;

namespace NavalSim.Parity;

/// <summary>
/// LE COMMERCE : le même cours des deux côtés, à la pièce.
///
/// Le cours est une fonction pure du port et de l'heure : il n'y a aucune raison
/// qu'il diffère d'une pièce entre la page et Godot, et s'il diffère, l'une des
/// deux ment au joueur. Le hachage est exigé AU BIT — il multiplie en double
/// au-delà de 2^53 —, les prix à la pièce, les textes à la lettre.
/// </summary>
public static class MarketParity
{
    public static int Run(string dumpPath, string worldDir)
    {
        if (!File.Exists(dumpPath))
        {
            Console.Error.WriteLine($"releve du commerce introuvable : {dumpPath}");
            Console.Error.WriteLine("lancer d'abord :  node tools/parity-market.js");
            return 1;
        }
        using var doc = JsonDocument.Parse(File.ReadAllText(dumpPath));
        var d = doc.RootElement;
        int failures = 0;
        void Fail(string what)
        {
            if (failures < 8) Console.Error.WriteLine("  ECART " + what);
            failures++;
        }

        var region = RegionSpec.FromJson(File.ReadAllText(Path.Combine(worldDir, "caraibes.json")));
        var (w, h, grey) = GreyPng.Decode(File.ReadAllBytes(Path.GetFullPath(Path.Combine(worldDir, "..", region.Relief.Image))));
        var world = new World(region, w, h, grey);
        var mk = new Market();
        double palier = mk.Tune(world.LongestLeg);
        if (palier != d.GetProperty("palier").GetDouble())
            Fail($"palier : JS {d.GetProperty("palier").GetDouble()}, C# {palier}");

        // --- le hachage, au bit ---
        int nh = 0, badH = 0;
        foreach (var e in d.GetProperty("hashes").EnumerateArray())
        {
            nh++;
            double js = e.GetProperty("h").GetDouble();
            double cs = Market.Hash(e.GetProperty("key").GetString()!, e.GetProperty("n").GetDouble());
            if (js != cs) { badH++; Fail($"hachage {e.GetProperty("key").GetString()}/{e.GetProperty("n").GetDouble()} : JS {js:R}, C# {cs:R}"); }
        }
        Console.WriteLine($"  {(badH == 0 ? "OK   " : "ECART")} hachage, {nh} tirages      {badH} different(s) -- exige au bit pres");

        // --- les cours, à la pièce ---
        int np = 0, badP = 0;
        foreach (var e in d.GetProperty("prices").EnumerateArray())
        {
            np++;
            string k = e.GetProperty("key").GetString()!;
            double t = e.GetProperty("t").GetDouble();
            if (mk.Spice(k, t) != e.GetProperty("spice").GetDouble()
                || mk.BuyPrice(k, t) != e.GetProperty("buy").GetDouble()
                || mk.SellPrice(k, t) != e.GetProperty("sell").GetDouble())
            { badP++; Fail($"cours {k} a t={t} : JS {e.GetProperty("spice").GetDouble()}, C# {mk.Spice(k, t)}"); }
        }
        Console.WriteLine($"  {(badP == 0 ? "OK   " : "ECART")} cours, {np} releves          {badP} different(s) -- a la piece");

        // --- les nouvelles : leur âge et leur chiffre ---
        int nn = 0, badN = 0; double worstLag = 0;
        foreach (var e in d.GetProperty("news").EnumerateArray())
        {
            nn++;
            var from = world.ByKey(e.GetProperty("from").GetString()!)!;
            var to = world.ByKey(e.GetProperty("to").GetString()!)!;
            var n = mk.NewsOf(from, to, e.GetProperty("t").GetDouble());
            double dl = Math.Abs(n.Lag - e.GetProperty("lag").GetDouble());
            worstLag = Math.Max(worstLag, dl);
            if (dl > 1e-6 || n.Sell != e.GetProperty("sell").GetDouble()) { badN++; Fail($"nouvelle {from.Key} -> {to.Key}"); }
        }
        Console.WriteLine($"  {(badN == 0 ? "OK   " : "ECART")} nouvelles, {nn}               retard pire ecart {worstLag:E1} s");

        // --- les mots : l'âge dit, les rumeurs ---
        int badT = 0;
        foreach (var e in d.GetProperty("ages").EnumerateArray())
        {
            string js = e.GetProperty("s").GetString()!, cs = Market.Age(e.GetProperty("lag").GetDouble());
            if (js != cs) { badT++; Fail($"age {e.GetProperty("lag").GetDouble()} : JS « {js} », C# « {cs} »"); }
        }
        int nr = 0;
        foreach (var e in d.GetProperty("rumours").EnumerateArray())
        {
            nr++;
            var isl = world.ByKey(e.GetProperty("key").GetString()!)!;
            var r = mk.RumourOf(isl, e.GetProperty("t").GetDouble(), e.GetProperty("rnd").GetDouble());
            if (r.Voile != e.GetProperty("voile").GetString() || r.Mot != e.GetProperty("mot").GetString()
                || r.Lie != e.GetProperty("lie").GetString() || r.Port != e.GetProperty("port").GetString()
                || r.Fort != e.GetProperty("fort").GetBoolean() || r.Faible != e.GetProperty("faible").GetBoolean())
            { badT++; Fail($"rumeur {isl.Key} : JS « {e.GetProperty("mot").GetString()} », C# « {r.Mot} »"); }
        }
        Console.WriteLine($"  {(badT == 0 ? "OK   " : "ECART")} textes, {d.GetProperty("ages").GetArrayLength()} ages et {nr} rumeurs   a la lettre");

        // --- la bourse ---
        var p = new Purse(Market.Depart);
        int badB = 0;
        foreach (var e in d.GetProperty("purse").EnumerateArray())
        {
            string op = e.GetProperty("op").GetString()!;
            double n = e.GetProperty("n").GetDouble();
            bool ok = op == "take" ? p.Take(n) : Add(p, n);
            if (ok != e.GetProperty("ok").GetBoolean() || p.Sous != e.GetProperty("sous").GetInt64()
                || p.Ecus != e.GetProperty("ecus").GetInt64() || p.Pieces != e.GetProperty("pieces").GetInt64())
            { badB++; Fail($"bourse {op} {n} : JS {e.GetProperty("sous").GetInt64()}, C# {p.Sous}"); }
        }
        Console.WriteLine($"  {(badB == 0 ? "OK   " : "ECART")} bourse, {d.GetProperty("purse").GetArrayLength()} operations       palier {palier} s");
        return failures;

        static bool Add(Purse p, double n) { p.Add(n); return true; }
    }
}
