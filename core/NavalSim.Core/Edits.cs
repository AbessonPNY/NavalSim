using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;

namespace NavalSim.Core;

/// <summary>
/// UNE RETOUCHE : ce que la main a changé à un objet que le monde pose de
/// lui-même — une maison de Port-Royal déplacée, un rocher retiré, un cocotier
/// tourné. Elle dit l'état VOULU, pas l'écart : la place en mètres vrais, le cap
/// en degrés, l'échelle, et ce qu'on l'a levé ou enfoncé par rapport au sol.
/// </summary>
public sealed class Edit
{
    public string Id = "";
    public double X, Z, Yaw, Scale = 1, Dy;
    public bool Removed;
    /// <summary>
    /// UN AJOUT, et non une retouche : ce que la main a posé de plus. Il dit d'où il
    /// vient — la COPIE d'un objet du monde (<see cref="From"/>, son nom), qui en
    /// reprend le modèle, la taille et la pose, ou un modèle BRUT de la palette
    /// (<see cref="Glb"/>). Un ajout qu'on retire disparaît du fichier.
    /// </summary>
    public string From = "", Glb = "";
    public bool Added => From.Length > 0 || Glb.Length > 0;
}

/// <summary>
/// LES RETOUCHES D'UNE RÉGION — world/retouches/&lt;région&gt;.json, format dans
/// world/README.md.
///
/// LE MONDE RESTE PROCÉDURAL, ET LA MAIN PASSE PAR-DESSUS. Les maisons, les pâtés
/// et les semis sont toujours tirés sur leur graine ; une retouche ne remplace
/// que l'objet qu'elle nomme, par un nom STABLE (« maison:Port-Royal:37 »). Ce
/// qu'on n'a pas touché suit donc encore les règles — un relief retravaillé
/// déplace les maisons qu'on n'a pas posées soi-même —, et retirer une retouche
/// rend l'objet à sa place automatique.
///
/// Le revers, qu'il faut savoir : le nom est un RANG dans un tirage. Changer la
/// graine, le nombre de maisons ou le relief d'un port renumérote ses maisons, et
/// une retouche s'appliquerait alors à une autre. Les retouches sont faites pour
/// un monde dont les règles sont arrêtées.
/// </summary>
public sealed class Edits
{
    readonly Dictionary<string, Edit> _byId = new();

    public int Count => _byId.Count;
    public IEnumerable<Edit> All => _byId.Values;

    public Edit? Get(string id) => _byId.TryGetValue(id, out var e) ? e : null;
    public void Set(Edit e) => _byId[e.Id] = e;
    public bool Forget(string id) => _byId.Remove(id);

    /// <summary>Le prochain nom d'ajout libre : « ajout:N », N au-delà de tous ceux qu'on a vus.</summary>
    public string NextAddId(IEnumerable<string> alsoTaken)
    {
        int n = 0;
        void See(string id)
        {
            if (id.StartsWith("ajout:", StringComparison.Ordinal) && int.TryParse(id.AsSpan(6), out int k)) n = Math.Max(n, k);
        }
        foreach (var id in _byId.Keys) See(id);
        foreach (var id in alsoTaken) See(id);
        return "ajout:" + (n + 1);
    }

    public static Edits Parse(string json)
    {
        var outp = new Edits();
        using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
        if (!doc.RootElement.TryGetProperty("retouches", out var arr) || arr.ValueKind != JsonValueKind.Array) return outp;
        foreach (var r in arr.EnumerateArray())
        {
            string id = r.TryGetProperty("id", out var i) ? i.GetString() ?? "" : "";
            if (id.Length == 0) continue;
            outp.Set(new Edit
            {
                Id = id,
                X = r.TryGetProperty("x", out var x) ? x.GetDouble() : 0,
                Z = r.TryGetProperty("z", out var z) ? z.GetDouble() : 0,
                Yaw = r.TryGetProperty("cap", out var y) ? y.GetDouble() : 0,
                Scale = r.TryGetProperty("echelle", out var s) ? s.GetDouble() : 1,
                Dy = r.TryGetProperty("dy", out var d) ? d.GetDouble() : 0,
                Removed = r.TryGetProperty("retire", out var rm) && rm.ValueKind == JsonValueKind.True,
                From = r.TryGetProperty("de", out var de) ? de.GetString() ?? "" : "",
                Glb = r.TryGetProperty("glb", out var gl) ? gl.GetString() ?? "" : ""
            });
        }
        return outp;
    }

    public static Edits Load(string path) => File.Exists(path) ? Parse(File.ReadAllText(path)) : new Edits();

    /// <summary>
    /// Écrire, une retouche par ligne et dans l'ordre des noms : le fichier se
    /// lit et se compare à l'œil (un diff montre la maison qu'on a bougée), et
    /// deux enregistrements du même état donnent le même fichier.
    /// </summary>
    public string ToJson(string region)
    {
        var ids = new List<string>(_byId.Keys);
        ids.Sort(string.CompareOrdinal);
        var sb = new StringBuilder();
        sb.Append("{\n  \"region\": ").Append(JsonSerializer.Serialize(region)).Append(",\n");
        sb.Append("  \"note\": \"Retouches de l'éditeur (mode création, ²) : ce que la main a changé aux objets que le monde pose de lui-même. Format : world/README.md.\",\n");
        sb.Append("  \"retouches\": [");
        for (int k = 0; k < ids.Count; k++)
        {
            var e = _byId[ids[k]];
            sb.Append(k == 0 ? "\n" : ",\n").Append("    { \"id\": ").Append(JsonSerializer.Serialize(e.Id));
            if (e.From.Length > 0) sb.Append(", \"de\": ").Append(JsonSerializer.Serialize(e.From));
            if (e.Glb.Length > 0) sb.Append(", \"glb\": ").Append(JsonSerializer.Serialize(e.Glb));
            if (e.Removed) sb.Append(", \"retire\": true");
            else
            {
                sb.Append(FormattableString.Invariant($", \"x\": {e.X:F2}, \"z\": {e.Z:F2}, \"cap\": {e.Yaw:F1}"));
                if (Math.Abs(e.Scale - 1) > 1e-4) sb.Append(FormattableString.Invariant($", \"echelle\": {e.Scale:F3}"));
                if (Math.Abs(e.Dy) > 1e-4) sb.Append(FormattableString.Invariant($", \"dy\": {e.Dy:F2}"));
            }
            sb.Append(" }");
        }
        sb.Append(ids.Count > 0 ? "\n  ]\n}\n" : "]\n}\n");
        return sb.ToString();
    }

    public void Save(string path, string region)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, ToJson(region), new UTF8Encoding(false));
    }
}
