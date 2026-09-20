using System;
using System.IO;
using System.Text.Json;
using NavalSim.Core;

namespace NavalSim.Parity;

/// <summary>
/// LE MONDE : la même terre des deux côtés.
///
/// Ce banc-ci compare autre chose que les précédents. Ailleurs, deux formules
/// partent des mêmes nombres ; ici elles partent d'une IMAGE, et il a donc fallu
/// décoder le PNG deux fois, en Node et en C#. La première chose vérifiée est
/// donc que les deux décodeurs rendent les mêmes octets — sans quoi tout ce qui
/// suit comparerait deux reliefs plutôt que deux calculs, et le dirait mal.
/// </summary>
public static class WorldParity
{
    // tout est en double des deux côtés, sauf le champ de distance
    const double Tol = 1e-9;
    /* Le champ de distance est stocké en flottant 32 bits des deux côtés
       (Float32Array, float[]) : sur une centaine de kilomètres, le cran vaut déjà
       le centimètre. Exiger mieux, ce serait exiger que float32 ait la précision
       de double. */
    const double TolShore = 0.05;
    /* Et le point de rivage le plus proche descend ce champ en six pas : un cran
       de float32 sur le gradient déplace le point d'arrivée de bien plus que le
       cran lui-même. */
    const double TolPoint = 2.0;

    public static int Run(string dumpPath, string worldDir)
    {
        if (!File.Exists(dumpPath))
        {
            Console.Error.WriteLine($"releve du monde introuvable : {dumpPath}");
            Console.Error.WriteLine("lancer d'abord :  node tools/parity-world.js");
            return 1;
        }
        using var doc = JsonDocument.Parse(File.ReadAllText(dumpPath));
        var d = doc.RootElement;

        int failures = 0;
        double worst = 0; string worstWhere = "";
        void Check(string what, double js, double cs, double tol = Tol)
        {
            double diff = Math.Abs(js - cs);
            if (diff > worst) { worst = diff; worstWhere = what; }
            if (diff > tol)
            {
                if (failures < 6)
                    Console.Error.WriteLine($"  ECART {what} : JS {js:R}, C# {cs:R} (ecart {diff:E2})");
                failures++;
            }
        }

        // --- l'image, d'abord : les deux decodeurs rendent-ils le meme relief ? ---
        var region = RegionSpec.FromJson(File.ReadAllText(Path.Combine(worldDir, "caraibes.json")));
        var png = File.ReadAllBytes(Path.GetFullPath(Path.Combine(worldDir, "..", region.Relief.Image)));
        var (w, h, grey) = GreyPng.Decode(png);

        var im = d.GetProperty("image");
        int jw = im.GetProperty("w").GetInt32(), jh = im.GetProperty("h").GetInt32();
        if (jw != w || jh != h)
        {
            Console.Error.WriteLine($"  ECART image : {jw}x{jh} cote JS, {w}x{h} cote C#");
            return failures + 1;
        }
        long sum = 0;
        foreach (byte b in grey) sum += b;
        long jsum = im.GetProperty("sum").GetInt64();
        if (sum != jsum)
        {
            Console.Error.WriteLine($"  ECART image : somme {jsum} cote JS, {sum} cote C# "
                                  + "-- les deux decodeurs PNG ne rendent pas la meme image");
            failures++;
        }
        var probes = im.GetProperty("probes");
        int badProbe = 0;
        for (int n = 0; n < probes.GetArrayLength(); n++)
        {
            int k = (int)((long)n * (grey.Length - 1) / (probes.GetArrayLength() - 1));
            if (probes[n].GetInt32() != grey[k]) badProbe++;
        }
        if (badProbe > 0) { Console.Error.WriteLine($"  ECART image : {badProbe} sonde(s) differente(s)"); failures++; }

        var world = new World(region, w, h, grey);

        // --- la geographie ---
        var g = d.GetProperty("geo");
        Check("geo.lat0", g.GetProperty("lat0").GetDouble(), world.Geo.Lat0);
        Check("geo.lon0", g.GetProperty("lon0").GetDouble(), world.Geo.Lon0);
        Check("geo.scale", g.GetProperty("scale").GetDouble(), world.Geo.Scale);

        var jf = d.GetProperty("format");
        string[] cs = {
            Geo.Format(17.9375, true), Geo.Format(-76.8411, false),
            Geo.Format(-0.25, true), Geo.Format(9.999, false)
        };
        for (int i = 0; i < cs.Length; i++)
            if (jf[i].GetString() != cs[i])
            {
                Console.Error.WriteLine($"  ECART format[{i}] : JS « {jf[i].GetString()} », C# « {cs[i]} »");
                failures++;
            }

        // --- les grandeurs du monde ---
        var jwld = d.GetProperty("world");
        Check("px", jwld.GetProperty("px").GetDouble(), world.Px);
        Check("extent", jwld.GetProperty("extent").GetDouble(), world.Extent);
        Check("longestLeg", jwld.GetProperty("longestLeg").GetDouble(), world.LongestLeg);
        Check("harbourDepth", jwld.GetProperty("harbourDepth").GetDouble(), world.HarbourDepth);

        // --- la courbe gris -> metres ---
        var dec = d.GetProperty("decode");
        for (int v = 0; v < 256; v++) Check($"decode({v})", dec[v].GetDouble(), world.Decode(v));

        // --- les ports que la fiche fait naitre ---
        var jp = d.GetProperty("ports");
        if (jp.GetArrayLength() != world.Isles.Count)
        {
            Console.Error.WriteLine($"  ECART : {jp.GetArrayLength()} port(s) cote JS, {world.Isles.Count} cote C#");
            failures++;
        }
        else
            for (int i = 0; i < world.Isles.Count; i++)
            {
                var a = jp[i]; var b = world.Isles[i];
                if (a.GetProperty("key").GetString() != b.Key)
                {
                    Console.Error.WriteLine($"  ECART port[{i}] : « {a.GetProperty("key").GetString()} » contre « {b.Key} »");
                    failures++;
                    continue;
                }
                Check($"{b.Key}.x", a.GetProperty("x").GetDouble(), b.X);
                Check($"{b.Key}.z", a.GetProperty("z").GetDouble(), b.Z);
                var ap = a.GetProperty("port");
                Check($"{b.Key}.ang", ap.GetProperty("ang").GetDouble(), b.Port.Ang);
                Check($"{b.Key}.reach", ap.GetProperty("reach").GetDouble(), b.Port.Reach);
                Check($"{b.Key}.sx", ap.GetProperty("sx").GetDouble(), b.Port.Sx);
                Check($"{b.Key}.sz", ap.GetProperty("sz").GetDouble(), b.Port.Sz);
                Check($"{b.Key}.hx", ap.GetProperty("hx").GetDouble(), b.Port.Hx);
                Check($"{b.Key}.hz", ap.GetProperty("hz").GetDouble(), b.Port.Hz);
                var ab = ap.GetProperty("basin");
                Check($"{b.Key}.basin.x", ab.GetProperty("x").GetDouble(), b.Port.Basin.X);
                Check($"{b.Key}.basin.z", ab.GetProperty("z").GetDouble(), b.Port.Basin.Z);
                Check($"{b.Key}.basin.r", ab.GetProperty("r").GetDouble(), b.Port.Basin.R);
                var ah = ap.GetProperty("harbour");
                bool jsMole = ah.ValueKind == JsonValueKind.Object;
                if (jsMole != b.Port.Harbour.HasValue)
                {
                    Console.Error.WriteLine($"  ECART {b.Key} : mole {(jsMole ? "cote JS seulement" : "cote C# seulement")}");
                    failures++;
                }
                else if (jsMole && b.Port.Harbour is Harbour H)
                {
                    Check($"{b.Key}.harbour.cx", ah.GetProperty("cx").GetDouble(), H.Cx);
                    Check($"{b.Key}.harbour.cz", ah.GetProperty("cz").GetDouble(), H.Cz);
                    Check($"{b.Key}.harbour.r", ah.GetProperty("r").GetDouble(), H.R);
                    Check($"{b.Key}.harbour.wall", ah.GetProperty("wall").GetDouble(), H.Wall);
                    Check($"{b.Key}.harbour.top", ah.GetProperty("top").GetDouble(), H.Top);
                    Check($"{b.Key}.harbour.gap", ah.GetProperty("gap").GetDouble(), H.Gap);
                    Check($"{b.Key}.harbour.px", ah.GetProperty("px").GetDouble(), H.Px);
                    Check($"{b.Key}.harbour.pz", ah.GetProperty("pz").GetDouble(), H.Pz);
                }
            }

        // --- et le relief lui-meme, point par point ---
        var samples = d.GetProperty("samples");
        int n2 = samples.GetArrayLength();
        double worstShore = 0, worstPoint = 0;
        for (int i = 0; i < n2; i++)
        {
            var s = samples[i];
            double x = s.GetProperty("x").GetDouble(), z = s.GetProperty("z").GetDouble();
            var fix = world.Geo.Fix(x, z);
            Check($"lat[{i}]", s.GetProperty("lat").GetDouble(), fix.Lat);
            Check($"lon[{i}]", s.GetProperty("lon").GetDouble(), fix.Lon);
            var (pi, pj) = world.PixelAt(x, z);
            Check($"pi[{i}]", s.GetProperty("pi").GetDouble(), pi);
            Check($"pj[{i}]", s.GetProperty("pj").GetDouble(), pj);
            Check($"grey[{i}]", s.GetProperty("grey").GetDouble(), world.Grey(x, z));
            Check($"island[{i}]", s.GetProperty("island").GetDouble(), world.IslandHeight(x, z));
            Check($"height[{i}]", s.GetProperty("height").GetDouble(), world.HeightAt(x, z));
            Check($"shelter[{i}]", s.GetProperty("shelter").GetDouble(), world.Shelter(x, z));

            double jsShore = s.GetProperty("shore").GetDouble(), csShore = world.ShoreDistance(x, z);
            worstShore = Math.Max(worstShore, Math.Abs(jsShore - csShore));
            Check($"shore[{i}]", jsShore, csShore, TolShore);

            var ns = world.NearestShore(x, z);
            double dp = Math.Max(Math.Abs(s.GetProperty("nsx").GetDouble() - ns.X),
                                 Math.Abs(s.GetProperty("nsz").GetDouble() - ns.Z));
            worstPoint = Math.Max(worstPoint, dp);
            Check($"nearestShore[{i}]", 0, dp, TolPoint);
        }

        Console.WriteLine($"  OK    image {w}x{h}, {grey.Length / 1000000.0:F1} M pixels    "
                        + $"les deux decodeurs rendent la meme");
        Console.WriteLine($"  OK    {world.Isles.Count} port(s) nes de la fiche, {n2} point(s) de relief"
                        + $"   pire ecart {worst:E2}");
        Console.WriteLine($"  OK    champ de distance (float32, tol {TolShore} m)   pire ecart {worstShore:E2} m"
                        + $"   rivage le plus proche {worstPoint:E2} m");
        return failures;
    }
}
