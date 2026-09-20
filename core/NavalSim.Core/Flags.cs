using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NavalSim.Core;

/// <summary>
/// Un pavillon que la fiche déclare (<c>flags</c>) : où il flotte, sa coupe, son image.
/// Voir ships/README.md.
/// </summary>
public sealed class FlagSpec
{
    /// <summary>« stern » : sur une hampe au couronnement ; « bow » : au bout du beaupré.</summary>
    [JsonPropertyName("at")]     public string? At { get; set; }
    /// <summary>En tête de ce mât, compté de l'avant (0 la misaine) ; négatif, de l'arrière.</summary>
    [JsonPropertyName("mast")]   public int? Mast { get; set; }
    [JsonPropertyName("shape")]  public string? Shape { get; set; }
    [JsonPropertyName("size")]   public double? Size { get; set; }
    /// <summary>Ses propres armes ; « nation » ou « nation:clé » : l'image que flags.json donne à la nation hissée.</summary>
    [JsonPropertyName("image")]  public string? Image { get; set; }
    [JsonPropertyName("above")]  public double Above { get; set; }
    [JsonPropertyName("length")] public double? Length { get; set; }
    [JsonPropertyName("tilt")]   public double? Tilt { get; set; }
    [JsonPropertyName("staff")]  public double? Staff { get; set; }
    [JsonPropertyName("rake")]   public double? Rake { get; set; }
    [JsonPropertyName("x")]      public double? X { get; set; }
    [JsonPropertyName("xFrac")]  public double? XFrac { get; set; }
    [JsonPropertyName("y")]      public double? Y { get; set; }
    [JsonPropertyName("z")]      public double? Z { get; set; }
    [JsonPropertyName("zFrac")]  public double? ZFrac { get; set; }
}

/// <summary>
/// La coupe d'un pavillon, telle que la grille la voit — Naval.FLAG_SHAPES :
/// <c>Taper</c> jusqu'où le long du battant il garde toute sa hauteur, <c>Tip</c>
/// la hauteur qui reste au bout, <c>Notch</c> la profondeur de la fourche,
/// <c>Length</c> le battant sur le guindant.
/// </summary>
public readonly record struct FlagShape(double Taper, double Tip, double Notch, double Length)
{
    public static readonly IReadOnlyDictionary<string, FlagShape> All = new Dictionary<string, FlagShape>
    {
        ["rect"]        = new(0,    1,    0,    1.6),
        ["swallowtail"] = new(0,    1,    0.28, 1.7),
        ["pennant"]     = new(0,    0.06, 0,    3.0),
        ["streamer"]    = new(0.22, 0.14, 0.10, 5.0)
    };
}

/// <summary>
/// UN PAVILLON COMME TOILE — la grille de _flagAt et l'ondulation de setFlag
/// (ship-model.js). Le guindant est à u = 0 et le battant file vers +z, le
/// guindant descend en −y. La coupe est posée SUR la grille même que l'onde
/// travaille, si bien que rien de l'ondulation n'a à la connaître : un
/// amincissement resserre la hauteur vers le battant autour du milieu du
/// guindant, une fourche ramène le bord du battant en V. Les coordonnées de
/// texture suivent la coupe : un dessin peint sur un rectangle est ROGNÉ par la
/// fourche, pas écrasé dedans.
///
/// En flottants comme la page (Float32Array), le calcul en double.
/// </summary>
public sealed class FlagCloth
{
    public readonly string ShapeName;
    public readonly double Hoist, Fly, Wave, Seed;
    public readonly float[] Base, U, V, Uvs, Positions, Normals;
    public readonly int[] Indices;

    /// <param name="shipL">Sa longueur : un pavillon est à l'échelle de qui le porte.</param>
    /// <param name="seed">Sa phase à lui, sans quoi trois pavillons onduleraient comme un seul.</param>
    public FlagCloth(double shipL, FlagSpec l, double seed)
    {
        ShapeName = l.Shape != null && FlagShape.All.ContainsKey(l.Shape) ? l.Shape : "rect";
        var shape = FlagShape.All[ShapeName];
        double len = l.Length ?? shape.Length;
        Hoist = 0.045 * shipL * (l.Size ?? 1);
        Fly = Hoist * len;
        Wave = 7.0 * len / 1.6;
        Seed = seed;
        int nu = (int)Math.Min(48, Math.Round(14 * Math.Max(1, len / 1.6), MidpointRounding.AwayFromZero)), nv = 6;
        int n = (nu + 1) * (nv + 1);
        Base = new float[n * 3]; U = new float[n]; V = new float[n]; Uvs = new float[n * 2];
        int k = 0;
        for (int j = 0; j <= nv; j++)
            for (int i = 0; i <= nu; i++, k++)
            {
                double v = (double)j / nv;
                double u = Math.Min((double)i / nu, 1 - shape.Notch * (1 - Math.Abs(2 * v - 1)));
                double w = u <= shape.Taper ? 1 : 1 - (1 - shape.Tip) * (u - shape.Taper) / (1 - shape.Taper);
                Base[k * 3] = 0;
                Base[k * 3 + 1] = (float)(-Hoist * (0.5 + (v - 0.5) * w));
                Base[k * 3 + 2] = (float)(u * Fly);
                U[k] = (float)u; V[k] = (float)v;
                // coupé par l'amincissement, pas écrasé ; v retourné : bâti vers le bas
                Uvs[k * 2] = (float)u; Uvs[k * 2 + 1] = (float)(1 - (0.5 + (v - 0.5) * w));
            }
        Indices = new int[nu * nv * 6];
        int q = 0;
        for (int j = 0; j < nv; j++)
            for (int i = 0; i < nu; i++)
            {
                int a = j * (nu + 1) + i;
                Indices[q++] = a; Indices[q++] = a + nu + 1; Indices[q++] = a + nu + 2;
                Indices[q++] = a; Indices[q++] = a + nu + 2; Indices[q++] = a + 1;
            }
        Positions = (float[])Base.Clone();
        Normals = new float[n * 3];
        SailCloth.ComputeNormals(Positions, Normals, Indices);
    }

    /// <summary>
    /// Le faire flotter. Le battant pointe droit sous le vent APPARENT : son
    /// relèvement sur l'étrave et le bord où elle est. Quand la brise tombe, il
    /// cesse d'onduler et pend — il perd de sa longueur en retombant, et c'est ce
    /// qui dit d'un coup d'œil que le vent est parti. Rend le lacet du pivot.
    /// </summary>
    /// <param name="lift">
    /// De 0 (dans l'air) à 1 (dans l'eau). Une étamine noyée ne pend pas : l'eau
    /// la PORTE — elle ondule lentement, soulevée par en dessous, et son onde
    /// s'étire d'autant. C'est le seul écart avec la page, qui n'a pas d'eau.
    /// </param>
    public double Stream(double beta, double tack, double vApp, double t, double lift = 0)
    {
        double yaw = Math.Atan2(tack * Math.Sin(beta), -Math.Cos(beta));
        double drive = Math.Min(1, vApp / 8);
        var p = Positions;
        for (int k = 0; k < U.Length; k++)
        {
            int i3 = k * 3;
            double u = U[k];
            // l'onde naît de rien sur la drisse et grandit vers le battant
            double sway = Math.Sin(u * Wave - t * (6.5 - 5.3 * lift) + V[k] * 1.2 + Seed)
                        * Hoist * 0.42 * Math.Pow(u, 1.3);
            // dans l'air, le vent la fait claquer ; dans l'eau, un lent balancement qui ne s'arrête pas
            p[i3] = (float)(sway * Math.Max(drive, 0.45 * lift));
            // elle pend d'autant moins qu'elle est portée : dans l'eau, elle se SOULÈVE
            double droop = (1 - drive) * (1 - lift) * u * u * Fly * 0.55;
            double rise = lift * u * u * Fly * 0.22 * (0.6 + 0.4 * Math.Sin(t * 0.7 + Seed + u * 2.0));
            p[i3 + 1] = (float)(Base[i3 + 1] - droop + rise);
            p[i3 + 2] = (float)(Base[i3 + 2] * (0.80 + 0.20 * Math.Max(drive, 0.8 * lift)));
        }
        SailCloth.ComputeNormals(Positions, Normals, Indices);
        return yaw;
    }
}

/// <summary>Une nation de flags.json : son pavillon, et ceux de ses autres coupes.</summary>
public sealed class Nation
{
    public string Id = "", Image = "";
    public string? Pays, Nationalite;
    public double Poids = 1;
    public bool Pirate;
    /// <summary>Toute autre clé qui nomme une image : une coupe (« streamer ») ou un emploi (« poupe »).</summary>
    public readonly Dictionary<string, string> Images = new();
}

/// <summary>
/// LES PAVILLONS DU MONDE — ships/textures/flags/flags.json. Chaque voile croisée
/// en arbore un, tiré au poids, et c'est lui qui dit qui elle est ; un pirate
/// aussi, et c'est un pavillon d'emprunt.
/// </summary>
public sealed class Nations
{
    public readonly List<Nation> All = new();

    public static Nations FromJson(JsonElement root)
    {
        var r = new Nations();
        if (!root.TryGetProperty("flags", out var arr) || arr.ValueKind != JsonValueKind.Array) return r;
        foreach (var f in arr.EnumerateArray())
        {
            if (f.ValueKind != JsonValueKind.Object) continue;
            var n = new Nation();
            foreach (var p in f.EnumerateObject())
                switch (p.Name)
                {
                    case "id": n.Id = p.Value.GetString() ?? ""; break;
                    case "image": n.Image = p.Value.GetString() ?? ""; break;
                    case "pays": n.Pays = p.Value.ValueKind == JsonValueKind.String ? p.Value.GetString() : null; break;
                    case "nationalite": n.Nationalite = p.Value.ValueKind == JsonValueKind.String ? p.Value.GetString() : null; break;
                    case "poids": if (p.Value.ValueKind == JsonValueKind.Number) n.Poids = p.Value.GetDouble(); break;
                    case "pirate": n.Pirate = p.Value.ValueKind == JsonValueKind.True; break;
                    default: if (p.Value.ValueKind == JsonValueKind.String) n.Images[p.Name] = p.Value.GetString()!; break;
                }
            if (n.Image != "") r.All.Add(n);          // un pavillon sans image n'en est pas un
        }
        return r;
    }

    public Nation? Pirate => All.Find(x => x.Pirate);

    /// <summary>Un pavillon national, tiré au poids — jamais le noir.</summary>
    public Nation? Draw(Func<double> random)
    {
        double tot = 0;
        foreach (var x in All) if (!x.Pirate) tot += x.Poids;
        double r = random() * tot;
        Nation? last = null;
        foreach (var x in All)
        {
            if (x.Pirate) continue;
            last = x;
            r -= x.Poids;
            if (r <= 0) return x;
        }
        return last;
    }
}
