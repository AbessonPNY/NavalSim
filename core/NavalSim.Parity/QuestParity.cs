using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using NavalSim.Core;

namespace NavalSim.Parity;

/// <summary>
/// LES QUÊTES : le même fil tendu sur le même monde.
///
/// Une quête calcule peu, et c'est ce qui la rend dangereuse à porter : rien à
/// l'écran ne dit qu'un lieu est tombé deux cents mètres à côté, ni qu'une étape
/// s'est accomplie un cran trop tôt. On compare donc deux choses — OÙ tombe
/// chaque forme de lieu que le format accepte, et le DÉROULÉ complet d'une
/// quête d'essai le long d'une route écrite dans le relevé.
///
/// La fiche d'essai voyage dans le relevé : les deux côtés lisent la même, si
/// bien qu'un écart ne peut venir que du code.
/// </summary>
public static class QuestParity
{
    // tout est en double des deux côtés : un lieu sort d'une formule, rien ne l'arrondit
    const double Tol = 1e-9;

    public static int Run(string dumpPath, string worldDir)
    {
        if (!File.Exists(dumpPath))
        {
            Console.Error.WriteLine($"releve des quetes introuvable : {dumpPath}");
            Console.Error.WriteLine("lancer d'abord :  node tools/parity-quests.js");
            return 1;
        }
        using var doc = JsonDocument.Parse(File.ReadAllText(dumpPath));
        var d = doc.RootElement;

        int failures = 0;
        double worst = 0; string worstWhere = "";
        void Check(string what, double js, double cs)
        {
            double diff = Math.Abs(js - cs);
            if (diff > worst) { worst = diff; worstWhere = what; }
            if (diff > Tol)
            {
                if (failures < 6) Console.Error.WriteLine($"  ECART {what} : JS {js:R}, C# {cs:R} (ecart {diff:E2})");
                failures++;
            }
        }
        void Same(string what, string js, string cs)
        {
            if (js == cs) return;
            if (failures < 6) Console.Error.WriteLine($"  ECART {what} : JS « {js} », C# « {cs} »");
            failures++;
        }

        // le même monde que la page : la fiche de région et son relief
        var region = RegionSpec.FromJson(File.ReadAllText(Path.Combine(worldDir, "caraibes.json")));
        var (w, h, grey) = GreyPng.Decode(File.ReadAllBytes(
            Path.GetFullPath(Path.Combine(worldDir, "..", region.Relief.Image))));
        var world = new World(region, w, h, grey);
        var Q = new Quests(world);

        // --- où tombe chaque forme de lieu ---
        var places = d.GetProperty("places");
        foreach (var p in places.EnumerateArray())
        {
            var step = QuestStep.FromJson(p.GetProperty("step"));
            var got = Q.Place(step);
            string what = $"lieu « {step.Title} »";
            Check($"{what}.x", p.GetProperty("x").GetDouble(), got.X);
            Check($"{what}.z", p.GetProperty("z").GetDouble(), got.Z);
            Check($"{what}.r", p.GetProperty("r").GetDouble(), got.R);
        }
        Console.WriteLine($"  {(failures == 0 ? "OK   " : "ECART")} {places.GetArrayLength()} forme(s) de lieu"
                        + $"       pire ecart {worst:E2}");

        // --- la ligne d'objectif : distance et relèvement depuis trois points ---
        var quest = QuestSpec.FromJson(d.GetProperty("quest").GetRawText());
        Q.Add(quest);
        int before = failures;
        var from = d.GetProperty("from");
        var aims = d.GetProperty("aims");
        for (int i = 0; i < aims.GetArrayLength(); i++)
        {
            Q.Start(quest.Id);
            Q.Step = 1;                                   // la même étape que le relevé
            var a = Q.Aim(from[i][0].GetDouble(), from[i][1].GetDouble())!.Value;
            var j = aims[i];
            Check($"objectif[{i}].dist", j.GetProperty("dist").GetDouble(), a.Dist);
            Check($"objectif[{i}].bearing", j.GetProperty("bearing").GetDouble(), a.Bearing);
            Check($"objectif[{i}].x", j.GetProperty("x").GetDouble(), a.X);
            Check($"objectif[{i}].z", j.GetProperty("z").GetDouble(), a.Z);
            Check($"objectif[{i}].r", j.GetProperty("r").GetDouble(), a.R);
            if (j.GetProperty("count").GetInt32() != a.Count)
            {
                Console.Error.WriteLine($"  ECART objectif[{i}].count");
                failures++;
            }
        }
        Console.WriteLine($"  {(failures == before ? "OK   " : "ECART")} {aims.GetArrayLength()} ligne(s) d objectif"
                        + $"       distance et relevement");

        /* --- LE DÉROULÉ, pas à pas. C'est le vrai banc : la route franchit
               chaque cercle de peu, et un seuil déplacé d'un cran change le pas
               auquel l'étape tombe — ce qu'on lit ici et nulle part ailleurs. */
        before = failures;
        var shown = new List<(int Tick, string Title, string Text)>();
        Q.OnShow = (t, x) => shown.Add((-1, t, x));
        Q.Start(quest.Id);
        for (int i = 0; i < shown.Count; i++) shown[i] = (0, shown[i].Title, shown[i].Text);

        var track = d.GetProperty("track");
        var ticks = d.GetProperty("ticks");
        int steps = 0;
        for (int k = 0; k < track.GetArrayLength(); k++)
        {
            var s = track[k];
            int had = shown.Count;
            Q.Update(0.1, new QuestState(
                s.GetProperty("x").GetDouble(), s.GetProperty("z").GetDouble(),
                s.GetProperty("speed").GetDouble(),
                s.GetProperty("moored").GetBoolean(), s.GetProperty("lost").GetBoolean()));
            for (int i = had; i < shown.Count; i++) shown[i] = (k + 1, shown[i].Title, shown[i].Text);

            var t = ticks[k];
            int step = Q.Active != null ? Q.Step : -1;
            if (t.GetProperty("step").GetInt32() != step)
            {
                if (failures < 6)
                    Console.Error.WriteLine($"  ECART pas {k} : etape JS {t.GetProperty("step").GetInt32()}, C# {step}");
                failures++;
            }
            Check($"pas {k}.hold", t.GetProperty("hold").GetDouble(), Q.Hold);
            var aim = Q.Aim(s.GetProperty("x").GetDouble(), s.GetProperty("z").GetDouble());
            Check($"pas {k}.dist", t.GetProperty("dist").GetDouble(), aim?.Dist ?? -1);
            Check($"pas {k}.bearing", t.GetProperty("bearing").GetDouble(), aim?.Bearing ?? -1);
            steps++;
        }

        // --- et ce qui s'est affiché, dans l'ordre et au même pas ---
        var js = d.GetProperty("shown");
        if (js.GetArrayLength() != shown.Count)
        {
            Console.Error.WriteLine($"  ECART {js.GetArrayLength()} message(s) cote JS, {shown.Count} cote C#");
            failures++;
        }
        else
            for (int i = 0; i < shown.Count; i++)
            {
                if (js[i].GetProperty("tick").GetInt32() != shown[i].Tick)
                {
                    Console.Error.WriteLine($"  ECART message {i} : pas {js[i].GetProperty("tick").GetInt32()} "
                                          + $"cote JS, {shown[i].Tick} cote C#");
                    failures++;
                }
                Same($"message {i}.titre", js[i].GetProperty("title").GetString() ?? "", shown[i].Title);
                Same($"message {i}.texte", js[i].GetProperty("text").GetString() ?? "", shown[i].Text);
            }

        bool finished = Q.Done.Contains(quest.Id);
        if (!finished) { Console.Error.WriteLine("  ECART la quete d essai ne s est pas achevee cote C#"); failures++; }

        Console.WriteLine($"  {(failures == before ? "OK   " : "ECART")} {steps} pas, "
                        + $"{shown.Count} message(s), quete {(finished ? "achevee" : "INACHEVEE")}"
                        + $"   pire ecart {worst:E2}");
        if (worst > 0 && failures == 0) Console.WriteLine($"        (le pire est sur {worstWhere})");
        return failures;
    }
}
