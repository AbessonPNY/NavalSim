using Godot;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using NavalSim.Core;

namespace NavalSim;

/// <summary>
/// LES MATIÈRES DU SOL ET DE LA MER, lues dans <c>world/materiaux.json</c> (demandé : « définir
/// dans un json les matériaux herbe, sable, pavés, boue, ceux des fonds marins, et celui de
/// la mer »). Chaque matière a sa TEINTE — celle des sommets de la terre et des pinceaux, une
/// seule définition — et peut recevoir des cartes PBR (couleur, normale, rugosité, hauteur,
/// métal) : elle est alors LUE dans ses textures, et sa hauteur décide de la frontière avec
/// sa voisine. Sans carte, elle garde son dessin d'avant, à l'identique.
///
/// Les textures de toutes les matières sont rangées dans DEUX piles (Texture2DArray) — une
/// lecture par carte et par pixel quel que soit leur nombre : A, la couleur et la hauteur ;
/// B, la normale (xy), la rugosité et le métal. Lues une fois par lancement : changer de
/// région ne les relit pas.
/// </summary>
public static class GroundMaterials
{
    /// <summary>L'ordre des matières : celui des tableaux du shader (ground_paint.gdshaderinc, MATS).</summary>
    public static readonly string[] Keys = { "herbe", "paves", "sable", "sable_blanc", "vase", "herbier", "roche", "foret" };
    const int N = 8;

    public sealed class Mat
    {
        public Color Col;                 // linéaire
        public double Rough = 0.94, Metal, Size = 2, NormalStrength = 1;
        public bool DirectX;
        public Color Tint = Colors.White; // linéaire
        public int Layer = -1;            // sa couche dans les piles, -1 : dessinée
    }

    static readonly Mat[] _mats = new Mat[N];
    static Texture2DArray? _a, _b;
    static bool _loaded;

    /// <summary>Les teintes de la mer : l'eau profonde, l'eau claire des hauts-fonds (linéaires).</summary>
    public static Color Deep { get; private set; } = Hex("#0e3347");
    public static Color Shallow { get; private set; } = Hex("#2f8fa8");

    public static Mat Get(int i) { Load(); return _mats[i]; }
    public static Color Grass => Get(0).Col;
    public static Color Cobble => Get(1).Col;
    public static Color Sand => Get(2).Col;
    public static Color WhiteSand => Get(3).Col;
    public static Color Mud => Get(4).Col;
    public static Color Seagrass => Get(5).Col;
    public static Color Rock => Get(6).Col;
    public static Color Wood => Get(7).Col;

    /// <summary>Une teinte écrite en sRGB (« #c9b183 »), en linéaire — ce que lisent les sommets et les shaders.</summary>
    static Color Hex(string s) => Color.FromHtml(s).SrgbToLinear();

    // les teintes d'avant le fichier : un fichier absent ou incomplet rend la même image
    static readonly string[] Fallback = { "#4f6a3a", "#5e5c58", "#c9b183", "#e6ddc4", "#4a4738", "#304626", "#6c665c", "#34502c" };

    static void Load()
    {
        if (_loaded) return;
        _loaded = true;
        for (int i = 0; i < N; i++) _mats[i] = new Mat { Col = Hex(Fallback[i]) };
        string path = Assets.Path("world/materiaux.json");
        if (!File.Exists(path)) { GD.PushWarning("[matières] world/materiaux.json absent : les teintes d'origine"); return; }
        var sw = Stopwatch.StartNew();
        try
        {
            // les commentaires et la virgule finale tolérés : la fiche se retouche à la main
            using var doc = JsonDocument.Parse(File.ReadAllText(path),
                new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            var root = doc.RootElement;
            int cap = (int)root.Num("taille_texture", 2048);
            // une matière écrite hors de « matieres » : signalée plutôt qu'ignorée en silence
            foreach (var k in Keys)
                if (root.TryGetProperty(k, out _))
                    GD.PushWarning($"[matières] « {k} » est à la racine du fichier : elle doit être DANS « matieres », elle est ignorée");
            if (root.TryGetProperty("mer", out var sea))
            {
                if (sea.Str("profond") != "") Deep = Hex(sea.Str("profond"));
                if (sea.Str("clair") != "") Shallow = Hex(sea.Str("clair"));
            }
            var a = new Godot.Collections.Array<Image>();
            var b = new Godot.Collections.Array<Image>();
            var pending = new List<(int i, JsonElement maps)>();
            if (root.TryGetProperty("matieres", out var mats))
                for (int i = 0; i < N; i++)
                {
                    if (!mats.TryGetProperty(Keys[i], out var e)) continue;
                    var m = _mats[i];
                    if (e.Str("couleur") != "") m.Col = Hex(e.Str("couleur"));
                    m.Rough = e.Num("rugosite", m.Rough);
                    m.Metal = e.Num("metal", m.Metal);
                    m.Size = Math.Max(0.05, e.Num("taille", m.Size));
                    m.NormalStrength = e.Num("force_normale", m.NormalStrength);
                    m.DirectX = e.Str("normale") == "directx";
                    if (e.Str("teinte") != "") m.Tint = Hex(e.Str("teinte"));
                    if (e.TryGetProperty("cartes", out var maps) && maps.ValueKind == JsonValueKind.Object
                        && maps.Str("couleur") != "")
                        pending.Add((i, maps));
                }
            /* LA TAILLE COMMUNE : une pile n'a qu'une taille. La plus petite des couleurs
               lues, plafonnée — une matière plus fine est réduite, aucune n'est agrandie. */
            var colors = new Dictionary<int, Image>();
            int size = cap;
            foreach (var (i, maps) in pending)
            {
                var img = Read(maps.Str("couleur"), Keys[i]);
                if (img == null) continue;
                colors[i] = img;
                size = Math.Min(size, Math.Min(img.GetWidth(), img.GetHeight()));
            }
            size = Math.Max(16, size);
            foreach (var (i, maps) in pending)
            {
                if (!colors.TryGetValue(i, out var col)) continue;
                var m = _mats[i];
                m.Layer = a.Count;
                a.Add(PackA(col, Read(maps.Str("hauteur"), Keys[i]), size));
                b.Add(PackB(Read(maps.Str("normale"), Keys[i]), Read(maps.Str("rugosite"), Keys[i]),
                            Read(maps.Str("metal"), Keys[i]), m, size));
            }
            if (a.Count > 0)
            {
                _a = new Texture2DArray(); _a.CreateFromImages(a);
                _b = new Texture2DArray(); _b.CreateFromImages(b);
            }
            MapCost.Add("matières", sw.Elapsed.TotalMilliseconds);
            if (a.Count > 0) GD.Print($"[matières] {a.Count} texturée(s) en {size}² ({sw.ElapsedMilliseconds} ms)");
        }
        catch (Exception ex) { GD.PushWarning($"[matières] world/materiaux.json illisible : {ex.Message}"); }
    }

    /// <summary>Une carte, à partir de la racine du dépôt ; rien si elle n'est pas donnée ou pas lisible.</summary>
    static Image? Read(string rel, string who)
    {
        if (rel == "") return null;
        string full = Assets.Path(rel);
        var img = File.Exists(full) ? Image.LoadFromFile(full) : null;
        if (img == null || img.IsEmpty()) { GD.PushWarning($"[matières] {who} : « {rel} » introuvable ou illisible"); return null; }
        if (img.IsCompressed()) img.Decompress();
        img.ClearMipmaps();
        img.Convert(Image.Format.Rgba8);
        return img;
    }

    static byte[] Fit(Image? img, int size)
    {
        if (img == null) return Array.Empty<byte>();
        if (img.GetWidth() != size || img.GetHeight() != size) img.Resize(size, size, Image.Interpolation.Lanczos);
        return img.GetData();
    }

    /// <summary>La pile A : la couleur en rgb, la hauteur en alpha (moitié si absente).</summary>
    static Image PackA(Image col, Image? height, int size)
    {
        var c = Fit(col, size);
        var h = Fit(height, size);
        for (int p = 0; p < size * size; p++) c[p * 4 + 3] = h.Length > 0 ? h[p * 4] : (byte)128;
        var img = Image.CreateFromData(size, size, false, Image.Format.Rgba8, c);
        img.GenerateMipmaps();
        return img;
    }

    /// <summary>La pile B : la normale en rg, la rugosité en b, le métal en a — les valeurs de la fiche là où une carte manque.</summary>
    static Image PackB(Image? normal, Image? rough, Image? metal, Mat m, int size)
    {
        var n = Fit(normal, size);
        var r = Fit(rough, size);
        var t = Fit(metal, size);
        byte rr = (byte)Math.Clamp(m.Rough * 255, 0, 255), mm = (byte)Math.Clamp(m.Metal * 255, 0, 255);
        var o = new byte[size * size * 4];
        for (int p = 0; p < size * size; p++)
        {
            o[p * 4] = n.Length > 0 ? n[p * 4] : (byte)128;
            o[p * 4 + 1] = n.Length > 0 ? n[p * 4 + 1] : (byte)128;
            o[p * 4 + 2] = r.Length > 0 ? r[p * 4] : rr;
            o[p * 4 + 3] = t.Length > 0 ? t[p * 4] : mm;
        }
        var img = Image.CreateFromData(size, size, false, Image.Format.Rgba8, o);
        img.GenerateMipmaps();
        return img;
    }

    /// <summary>
    /// Tout pousser dans la matière de la terre — TOUJOURS, chaque tableau : un tableau
    /// d'uniformes n'a pas de valeur par défaut, et une rugosité jamais poussée vaut zéro.
    /// </summary>
    public static void Apply(ShaderMaterial mat)
    {
        Load();
        var layer = new int[N];
        var size = new float[N]; var rough = new float[N]; var metal = new float[N];
        var nstr = new float[N]; var nflip = new float[N]; var tint = new Vector3[N];
        bool any = false;
        for (int i = 0; i < N; i++)
        {
            var m = _mats[i];
            layer[i] = m.Layer + 1;
            any |= m.Layer >= 0;
            size[i] = (float)m.Size; rough[i] = (float)m.Rough; metal[i] = (float)m.Metal;
            nstr[i] = (float)m.NormalStrength; nflip[i] = m.DirectX ? 1 : 0;
            tint[i] = new Vector3(m.Tint.R, m.Tint.G, m.Tint.B);
        }
        mat.SetShaderParameter("u_mat_layer", layer);
        mat.SetShaderParameter("u_mat_size", size);
        mat.SetShaderParameter("u_mat_rough", rough);
        mat.SetShaderParameter("u_mat_metal", metal);
        mat.SetShaderParameter("u_mat_nstr", nstr);
        mat.SetShaderParameter("u_mat_nflip", nflip);
        mat.SetShaderParameter("u_mat_tint", tint);
        mat.SetShaderParameter("u_mat_any", any ? 1f : 0f);
        if (_a != null) { mat.SetShaderParameter("u_mat_a", _a); mat.SetShaderParameter("u_mat_b", _b); }
        // les teintes, celles des sommets : une seule définition, ici
        string[] names = { "u_grass", "u_cobble", "u_sand", "u_whitesand", "u_mud", "u_seagrass", "u_rock", "u_wood" };
        for (int i = 0; i < N; i++) mat.SetShaderParameter(names[i], new Vector3(_mats[i].Col.R, _mats[i].Col.G, _mats[i].Col.B));
    }

    /// <summary>Les teintes de l'eau, à la mer proche et au lointain.</summary>
    public static void ApplySea(ShaderMaterial? near, ShaderMaterial? far)
    {
        Load();
        var d = new Vector3(Deep.R, Deep.G, Deep.B);
        near?.SetShaderParameter("u_deep", d);
        near?.SetShaderParameter("u_shallow", new Vector3(Shallow.R, Shallow.G, Shallow.B));
        far?.SetShaderParameter("u_deep", d);
    }
}
