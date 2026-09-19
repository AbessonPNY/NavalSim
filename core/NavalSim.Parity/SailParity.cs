using System.Text.Json;
using NavalSim.Core;

namespace NavalSim.Parity;

/// <summary>
/// La parité de la TOILE : tissage, forme à chaque image, normales, brassage.
///
/// La toile vit en float des deux côtés — Float32Array là-bas, float[] ici —,
/// donc l'accord attendu est celui de deux flottants 32 bits arrondis aux mêmes
/// endroits : un écart relatif de l'ordre de 1e-7, pas zéro. Le seuil est posé
/// à 1e-6 relatif, dix fois au-dessus ; une formule fausse s'y verrait en
/// centimètres. Le profil de creux et le brassage sont en double : 1e-12.
/// </summary>
public static class SailParity
{
    public static int Run(string dumpPath)
    {
        if (!File.Exists(dumpPath))
        {
            Console.Error.WriteLine($"releve des voiles introuvable : {dumpPath}");
            Console.Error.WriteLine("lancer d'abord :  node tools/parity-sails.js");
            return 1;
        }
        using var doc = JsonDocument.Parse(File.ReadAllText(dumpPath));
        var rootEl = doc.RootElement;
        int failures = 0;
        Console.WriteLine("--- la toile : tissage, forme, normales, brassage ---");

        // --- le profil de creux ---
        double worstBelly = 0;
        foreach (var e in rootEl.GetProperty("bellyProfile").EnumerateArray())
        {
            double cs = SailCloth.BellyProfile(e.GetProperty("x").GetDouble(), e.GetProperty("d").GetDouble(),
                e.GetProperty("p0").GetBoolean(), e.GetProperty("p1").GetBoolean(),
                e.GetProperty("r").GetDouble(), e.GetProperty("c").GetDouble());
            worstBelly = Math.Max(worstBelly, Math.Abs(cs - e.GetProperty("f").GetDouble()));
        }
        bool bellyOk = worstBelly <= 1e-12;
        if (!bellyOk) failures++;
        Console.WriteLine($"  {(bellyOk ? "OK  " : "FAUX")}  profil de creux           pire ecart {worstBelly:E1}");

        // --- chaque voile ---
        foreach (var s in rootEl.GetProperty("sails").EnumerateArray())
        {
            var corners = s.GetProperty("corners").EnumerateArray()
                .Select(c => new Vec3d(c[0].GetDouble(), c[1].GetDouble(), c[2].GetDouble())).ToArray();
            var d = s.GetProperty("dir");
            var cloth = new SailCloth(corners, new Vec3d(d[0].GetDouble(), d[1].GetDouble(), d[2].GetDouble()),
                                      ReadCut(s.GetProperty("cut")));
            double belly = s.GetProperty("belly").GetDouble();

            double worstBuild = 0;
            string where = "";
            void Cmp(string name, JsonElement js, float[] cs, ref double worst)
            {
                if (js.GetArrayLength() != cs.Length)
                {
                    Console.Error.WriteLine($"  {name} : {js.GetArrayLength()} valeurs cote JS, {cs.Length} cote C#");
                    worst = double.PositiveInfinity; where = name; return;
                }
                for (int i = 0; i < cs.Length; i++)
                {
                    double j = js[i].GetDouble();
                    double e = Math.Abs(j - cs[i]) / Math.Max(1, Math.Abs(j));
                    if (e > worst) { worst = e; where = $"{name}[{i}]"; }
                }
            }
            Cmp("base", s.GetProperty("base"), cloth.Base, ref worstBuild);
            Cmp("w", s.GetProperty("w"), cloth.W, ref worstBuild);
            Cmp("sag", s.GetProperty("sag"), cloth.Sag, ref worstBuild);
            Cmp("u", s.GetProperty("u"), cloth.U, ref worstBuild);
            Cmp("swag", s.GetProperty("swag"), cloth.Swag, ref worstBuild);
            Cmp("uv", s.GetProperty("uv"), cloth.Uvs, ref worstBuild);
            var idx = s.GetProperty("index");
            bool idxOk = idx.GetArrayLength() == cloth.Indices.Length
                && cloth.Indices.Select((v, i) => idx[i].GetInt32() == v).All(b => b);
            bool metaOk = s.GetProperty("nSwag").GetInt32() == cloth.NSwag && s.GetProperty("nu1").GetInt32() == cloth.Nu1;

            double worstShape = 0, worstNor = 0;
            string whereBuild = where;
            foreach (var st in s.GetProperty("shapes").EnumerateArray())
            {
                cloth.Shape(belly, st.GetProperty("load").GetDouble(), st.GetProperty("luffing").GetBoolean(),
                            st.GetProperty("t").GetDouble(), st.GetProperty("set").GetDouble());
                Cmp("pos", st.GetProperty("pos"), cloth.Positions, ref worstShape);
                Cmp("nor", st.GetProperty("nor"), cloth.Normals, ref worstNor);
            }
            /* LES NORMALES ONT LEUR PROPRE SEUIL, et il est mesure. Les positions
               tiennent a un ulp de float (1e-7) ; mais a la pointe d'un foc, ou ses
               trois coins ramenent la derniere rangee en un point, les triangles sont
               presque nuls et leur normale amplifie cet ulp — celui d'un sinus que V8
               et .NET n'arrondissent pas pareil — jusqu'a 4e-6. C'est 0,0002 degre
               d'orientation : 1e-5 le laisse passer et arrete toute vraie faute. */
            bool ok = worstBuild <= 1e-6 && worstShape <= 1e-6 && worstNor <= 1e-5 && idxOk && metaOk;
            if (!ok) failures++;
            Console.WriteLine($"  {(ok ? "OK  " : "FAUX")}  {s.GetProperty("id").GetString(),-14} creux {belly,4:F2}"
                + $"   tissage {worstBuild:E1}   forme {worstShape:E1}   normales {worstNor:E1}"
                + (ok ? "" : $"   <- {(worstBuild > 1e-6 ? whereBuild : where)}{(idxOk ? "" : " indices")}{(metaOk ? "" : " festons")}"));
        }

        // --- le brassage ---
        var trim = new BraceTrim();
        // le foc a son propre pivot mais le MÊME angle, aux trois quarts
        double worstTrim = 0;
        int step = 0, worstStep = 0;
        foreach (var e in rootEl.GetProperty("trim").EnumerateArray())
        {
            double a = trim.Update(e.GetProperty("sheet").GetDouble(), e.GetProperty("tack").GetInt32(),
                                   e.GetProperty("luffing").GetBoolean(), e.GetProperty("t").GetDouble());
            double er = Math.Max(Math.Abs(a - e.GetProperty("rig").GetDouble()),
                                 Math.Abs(a * 0.75 - e.GetProperty("jib").GetDouble()));
            if (er > worstTrim) { worstTrim = er; worstStep = step; }
            step++;
        }
        bool trimOk = worstTrim <= 1e-12;
        if (!trimOk) failures++;
        Console.WriteLine($"  {(trimOk ? "OK  " : "FAUX")}  brassage, {step} pas       pire ecart {worstTrim:E1} rad"
            + (trimOk ? "" : $"  (pas {worstStep})"));

        // --- les pavillons : leur coupe, puis leur ondulation ---
        if (rootEl.TryGetProperty("flags", out var flags))
            foreach (var f in flags.EnumerateArray())
            {
                var l = JsonSerializer.Deserialize<FlagSpec>(f.GetProperty("l").GetRawText())!;
                var cloth = new FlagCloth(f.GetProperty("spec").GetProperty("L").GetDouble(), l, f.GetProperty("seed").GetDouble());
                double worst = 0, worstNor = 0, worstYaw = 0;
                string where = "";
                void Cmp(string name, JsonElement js, float[] cs, ref double w)
                {
                    if (js.GetArrayLength() != cs.Length) { w = double.PositiveInfinity; where = name + " (longueur)"; return; }
                    for (int i = 0; i < cs.Length; i++)
                    {
                        double j = js[i].GetDouble(), e = Math.Abs(j - cs[i]) / Math.Max(1, Math.Abs(j));
                        if (e > w) { w = e; where = $"{name}[{i}]"; }
                    }
                }
                Cmp("base", f.GetProperty("base"), cloth.Base, ref worst);
                Cmp("u", f.GetProperty("u"), cloth.U, ref worst);
                Cmp("v", f.GetProperty("v"), cloth.V, ref worst);
                Cmp("uv", f.GetProperty("uv"), cloth.Uvs, ref worst);
                var idx = f.GetProperty("idx");
                bool idxOk = idx.GetArrayLength() == cloth.Indices.Length
                    && cloth.Indices.Select((v, i) => idx[i].GetInt32() == v).All(b => b);
                bool metaOk = f.GetProperty("shape").GetString() == cloth.ShapeName
                    && Math.Abs(f.GetProperty("hoist").GetDouble() - cloth.Hoist) < 1e-12
                    && Math.Abs(f.GetProperty("fly").GetDouble() - cloth.Fly) < 1e-12
                    && Math.Abs(f.GetProperty("wave").GetDouble() - cloth.Wave) < 1e-12;
                foreach (var fr in f.GetProperty("frames").EnumerateArray())
                {
                    double yaw = cloth.Stream(fr.GetProperty("beta").GetDouble(), fr.GetProperty("tack").GetDouble(),
                                              fr.GetProperty("vApp").GetDouble(), fr.GetProperty("t").GetDouble());
                    worstYaw = Math.Max(worstYaw, Math.Abs(yaw - fr.GetProperty("yaw").GetDouble()));
                    Cmp("pos", fr.GetProperty("pos"), cloth.Positions, ref worst);
                    Cmp("nor", fr.GetProperty("nor"), cloth.Normals, ref worstNor);
                }
                bool ok = worst <= 1e-6 && worstNor <= 1e-5 && worstYaw <= 1e-12 && idxOk && metaOk;
                if (!ok) failures++;
                Console.WriteLine($"  {(ok ? "OK  " : "FAUX")}  pavillon {cloth.ShapeName,-11}  grille et onde {worst:E1}   normales {worstNor:E1}   lacet {worstYaw:E1}"
                    + (ok ? "" : $"   <- {where}{(idxOk ? "" : " indices")}{(metaOk ? "" : " cotes")}"));
            }
        return failures;
    }

    static SailCut ReadCut(JsonElement c)
    {
        var cut = new SailCut();
        if (c.ValueKind != JsonValueKind.Object) return cut;
        double D(string n, double def) => c.TryGetProperty(n, out var v) ? v.GetDouble() : def;
        bool? B(string n) => c.TryGetProperty(n, out var v) ? v.GetBoolean() : null;
        cut.Kind = c.TryGetProperty("kind", out var k) ? k.GetString() ?? "" : "";
        cut.UPeak = D("uPeak", 0.5); cut.VPeak = D("vPeak", 0.5);
        // l'original : `c.uPin0 !== false`, donc seul un false explicite délace
        cut.UPin0 = B("uPin0") != false; cut.UPin1 = B("uPin1") != false;
        cut.VPin0 = B("vPin0") != false; cut.VPin1 = B("vPin1") != false;
        cut.Free = D("free", 0.78);
        cut.FreeU = c.TryGetProperty("freeU", out var fu) ? fu.GetDouble() : null;
        cut.HangU0 = B("hangU0"); cut.HangU1 = B("hangU1"); cut.HangV0 = B("hangV0"); cut.HangV1 = B("hangV1");
        cut.Crown = D("crown", 1); cut.Bow = D("bow", 0); cut.RoachFoot = D("roachFoot", 0);
        return cut;
    }
}
