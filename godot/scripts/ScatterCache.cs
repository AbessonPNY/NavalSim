using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using NavalSim.Core;

namespace NavalSim;

/// <summary>
/// LE SEMIS GARDÉ SUR LE DISQUE (demandé : « il est bien mis en cache ? » — il ne
/// l'était pas). Les places d'une foule ne dépendent que de sa fiche, du relief, de la
/// région et des retouches : tirées une fois (Scatter.Place), elles sont relues ensuite
/// d'un fichier — la flore de toute l'île coûtait quatre secondes à chaque chargement.
///
/// L'EMPREINTE dit quand recommencer : chaque champ du semis, et la taille et la date
/// des fichiers de la région (sa fiche, son relief et ses carreaux, ses retouches). Qu'un
/// seul change, et le nom du fichier change avec lui : l'ancien n'est plus lu.
/// Un changement des RÈGLES de placement (le code) passe par <see cref="Recipe"/>.
/// </summary>
public static class ScatterCache
{
    const string Dir = "user://semis-cache";
    /// <summary>À monter quand Scatter.Place change de règle : toutes les places sont alors retirées.</summary>
    const int Recipe = 1;

    static readonly FieldInfo[] Fields = typeof(ScatterSpec).GetFields(BindingFlags.Public | BindingFlags.Instance);

    /// <summary>L'empreinte des fichiers d'une région, calculée une fois par chargement.</summary>
    public static string RegionPrint(World w)
    {
        var sb = new StringBuilder();
        void Add(string rel)
        {
            if (string.IsNullOrEmpty(rel)) return;
            var fi = new FileInfo(Assets.Path(rel));
            sb.Append(rel).Append(fi.Exists ? $":{fi.Length:x}:{fi.LastWriteTimeUtc.Ticks:x}" : ":-").Append('|');
        }
        string key = w.Region.Key;
        Add($"world/{key}.json");
        Add(w.Region.Relief.Image);
        foreach (var p in w.Region.Patches) Add(p.Image);
        Add($"world/retouches/{key}.json");
        return sb.ToString();
    }

    /// <summary>Les places de ce semis : relues du disque si l'empreinte n'a pas bougé, sinon tirées et écrites.</summary>
    public static List<ScatterPlace> Places(World w, ScatterSpec sp, string regionPrint, out bool cached)
    {
        var sb = new StringBuilder(regionPrint).Append('#').Append(Recipe).Append('#');
        foreach (var f in Fields) sb.Append(f.Name).Append('=').Append(Convert.ToString(f.GetValue(sp), System.Globalization.CultureInfo.InvariantCulture)).Append(';');
        ulong h = 1469598103934665603UL;
        foreach (char c in sb.ToString()) { h ^= c; h *= 1099511628211UL; }
        string safe = new string(Array.ConvertAll(sp.Name.ToCharArray(), c => char.IsLetterOrDigit(c) ? c : '_'));
        string path = Path.Combine(ProjectSettings.GlobalizePath(Dir), $"{w.Region.Key}-{safe}-{h:x16}.bin");

        if (File.Exists(path))
        {
            try
            {
                using var br = new BinaryReader(File.OpenRead(path));
                int n = br.ReadInt32();
                var list = new List<ScatterPlace>(n);
                for (int i = 0; i < n; i++)
                    list.Add(new ScatterPlace(br.ReadDouble(), br.ReadDouble(), br.ReadDouble(), br.ReadDouble(), br.ReadDouble(), br.ReadDouble()));
                cached = true;
                return list;
            }
            catch (Exception e) { GD.PushWarning($"semis « {sp.Name} » : cache illisible ({e.Message}), retiré"); }
        }

        var placed = Scatter.Place(w, sp);
        cached = false;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            // les anciennes empreintes de ce semis ne serviront plus : on ne les garde pas
            foreach (var old in Directory.GetFiles(Path.GetDirectoryName(path)!, $"{w.Region.Key}-{safe}-*.bin"))
                if (old != path) File.Delete(old);
            using var bw = new BinaryWriter(File.Create(path));
            bw.Write(placed.Count);
            foreach (var p in placed) { bw.Write(p.X); bw.Write(p.Z); bw.Write(p.Yaw); bw.Write(p.TiltX); bw.Write(p.TiltZ); bw.Write(p.Size); }
        }
        catch (Exception e) { GD.PushWarning($"semis « {sp.Name} » : cache non écrit ({e.Message})"); }
        return placed;
    }
}
