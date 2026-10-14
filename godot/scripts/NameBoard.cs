using Godot;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using NavalSim.Core;

namespace NavalSim;

/// <summary>
/// LE NOM PEINT SUR LA POUPE — et la liste d'où il sort (ships/noms.json, <see cref="ShipNames"/>).
///
/// UN MODÈLE DIT OÙ, PAS QUOI. Un objet du .glb nommé <c>nom</c> (<c>nom_poupe</c>,
/// <c>Nom.001</c>, <c>name</c>…) est un GUIDE : un plan mince posé là où les lettres
/// iront, qui n'est jamais dessiné. Le nom vient de la coque, à la mise à l'eau ; le
/// même modèle porte ainsi « Speedwell » dans une rade et « La Fortune » dans l'autre.
///
/// UN DÉCALQUE ET NON UNE MATIÈRE. Les lettres sont projetées par un <see cref="Decal"/>
/// sur le bois qui est derrière le guide : elles font partie de sa surface, donc prennent
/// sa lumière, et les passes qui l'habillent (blessures, neige, brume) passent sur elles
/// comme sur le reste du tableau. Une matière refaite aurait dû reprendre toute la
/// chaîne des passes, et dépendait des UV de l'artiste.
/// </summary>
public static class NameBoard
{
    /* LE MOT ENTIER : « nom » au début ou après un séparateur, suivi d'un séparateur, d'un
       chiffre ou de rien. « nom_poupe », « Nom.001 », « name », « nameboard » sont pris ;
       « nommé », « canonPoupe » et « Plane » ne le sont pas. */
    static readonly Regex Guides = new("(^|[^a-z0-9])(nom|name|nameboard)([^a-z]|$)", RegexOptions.IgnoreCase);

    /// <summary>Un guide, dans le repère du modèle (avant l'échelle de la fiche) : le cadre du décalque et sa taille.</summary>
    public readonly record struct Guide(Transform3D Frame, float Width, float Height, float Thick);

    // ------------------------------------------------------------------
    //  LA LISTE, ET LES NOMS À FLOT
    // ------------------------------------------------------------------

    static ShipNames? _names;
    /// <summary>ships/noms.json, lu une fois. Absent ou illisible : aucune liste, les coques gardent le nom de leur fiche.</summary>
    public static ShipNames Names => _names ??= Load();

    static ShipNames Load()
    {
        string path = System.IO.Path.Combine(Assets.Root, "ships", "noms.json");
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(System.IO.File.ReadAllText(path));
            var n = ShipNames.FromJson(doc.RootElement);
            GD.Print($"[noms] {string.Join(", ", Fmt(n))}");
            return n;
        }
        catch (Exception e)
        {
            GD.PushWarning($"[noms] ships/noms.json illisible ({e.Message}) : les navires gardent le nom de leur fiche");
            return new ShipNames();
        }
    }

    static IEnumerable<string> Fmt(ShipNames n) { foreach (var k in n.Nations) yield return $"{k} {n.Count(k)}"; }

    /* LES NOMS PORTÉS EN CE MOMENT, comptés et non rangés dans un ensemble : un doublon
       accepté faute de mieux ne doit pas libérer le nom de l'autre en partant. Ce n'est
       pas un état de navire — chaque coque tient le sien — mais le registre de la rade. */
    static readonly Dictionary<string, int> _afloat = new();
    static readonly Random _rng = new();

    public static void Hold(string name) => _afloat[name] = _afloat.TryGetValue(name, out int k) ? k + 1 : 1;

    public static void Release(string name)
    {
        if (!_afloat.TryGetValue(name, out int k)) return;
        if (k <= 1) _afloat.Remove(name); else _afloat[name] = k - 1;
    }

    /// <summary>Un nom libre pour <paramref name="nation"/> ; <paramref name="random"/> pour un tirage à graine (une rade revient la même).</summary>
    public static string? Draw(string? nation, Func<double>? random = null) =>
        Names.Draw(nation, random ?? _rng.NextDouble, n => _afloat.ContainsKey(n));

    // ------------------------------------------------------------------
    //  LES GUIDES DU MODÈLE
    // ------------------------------------------------------------------

    /// <summary>
    /// Sortir du modèle ses guides de nom, et les rendre en cadres. À appeler avant toute
    /// mesure de coque (comme les anneaux) : un guide n'est pas du bois.
    /// </summary>
    public static List<Guide> Take(Node3D model)
    {
        var found = ShipNode.Detach(model, Guides);
        var list = new List<Guide>();
        if (found.Count == 0) return list;
        var hull = HullCentre(model);
        foreach (var (node, parent) in found)
        {
            if (Frame(node, parent, hull) is { } g) list.Add(g);
            node.QueueFree();
        }
        return list;
    }

    /// <summary>Le centre de la coque — le maillage le plus volumineux, comme partout —, pour savoir où est le dehors.</summary>
    static Vector3 HullCentre(Node3D model) => HullBox(model).GetCenter();

    /// <summary>La boîte de la coque, dans le repère du modèle.</summary>
    internal static Aabb HullBox(Node3D model)
    {
        double best = -1;
        var c = new Aabb();
        var stack = new Stack<(Node, Transform3D)>();
        stack.Push((model, Transform3D.Identity));
        while (stack.Count > 0)
        {
            var (n, acc) = stack.Pop();
            if (n is MeshInstance3D mi && mi.Mesh != null)
            {
                var box = acc * mi.Mesh.GetAabb();
                double vol = (double)box.Size.X * box.Size.Y * box.Size.Z;
                if (vol > best) { best = vol; c = box; }
            }
            foreach (var kid in n.GetChildren())
                stack.Push((kid, kid is Node3D k3 ? acc * k3.Transform : acc));
        }
        return c;
    }

    /* LE CADRE DU DÉCALQUE. Un Decal projette selon son −Y, et son image court en X, le
       haut de l'image vers −Z. Donc : Y = la normale du guide tournée VERS LE DEHORS (du
       centre de la coque vers le guide), X = la droite de qui regarde le tableau depuis
       l'eau, Z = le bas. Le haut est le haut du navire projeté dans le plan du guide :
       un tableau incliné garde ses lettres droites. */
    static Guide? Frame(Node3D node, Transform3D parent, Vector3 hull)
    {
        var xf = parent * node.Transform;
        // un nœud vide (un « Empty » de Blender) vaut son cube unité
        var box = node is MeshInstance3D mi && mi.Mesh != null ? mi.Mesh.GetAabb() : new Aabb(-Vector3.One, Vector3.One * 2);
        var c = xf * box.GetCenter();
        var ax = new[]
        {
            xf.Basis * new Vector3(box.Size.X, 0, 0),
            xf.Basis * new Vector3(0, box.Size.Y, 0),
            xf.Basis * new Vector3(0, 0, box.Size.Z),
        };
        int thin = 0;
        for (int i = 1; i < 3; i++) if (ax[i].Length() < ax[thin].Length()) thin = i;
        var a = ax[(thin + 1) % 3];
        var b = ax[(thin + 2) % 3];
        var n = a.Cross(b);
        if (n.LengthSquared() < 1e-12f) { GD.PushWarning($"[nom] {node.Name} : guide sans surface, ignoré"); return null; }
        n = n.Normalized();
        if (n.Dot(c - hull) < 0) n = -n;
        var up = Vector3.Up - n * n.Dot(Vector3.Up);
        // un guide couché (un nom sur le pont) : l'étrave tient lieu de haut
        if (up.LengthSquared() < 1e-4f) up = Vector3.Back - n * n.Dot(Vector3.Back);
        up = up.Normalized();
        var right = (-n).Cross(up).Normalized();
        float w = Math.Abs(a.Dot(right)) + Math.Abs(b.Dot(right));
        float h = Math.Abs(a.Dot(up)) + Math.Abs(b.Dot(up));
        if (w < 1e-4f || h < 1e-4f) return null;
        var basis = new Basis(right, n, right.Cross(n));
        return new Guide(new Transform3D(basis, c), w, h, ax[thin].Length());
    }

    /// <summary>
    /// --nom-guide : un guide d essai collé au tableau d un modèle qui n en porte pas, aux
    /// deux tiers de sa hauteur, sur la moitié de sa largeur — de quoi juger le décalque
    /// sans passer par Blender. L étrave est +Z, le tableau est donc au bas de la boîte.
    /// </summary>
    public static bool TestGuide;
    /// <summary>De combien le guide d essai rentre dans la boîte de la coque (m) : elle déborde souvent le tableau.</summary>
    public static float TestInset;

    public static void AddTestGuide(Node3D model)
    {
        var b = HullBox(model);
        model.AddChild(new MeshInstance3D
        {
            Name = "nom_essai",
            Mesh = new QuadMesh { Size = new Vector2(b.Size.X * 0.5f, b.Size.Y * 0.12f) },
            Position = new Vector3(b.GetCenter().X, b.Position.Y + b.Size.Y * 0.62f, b.Position.Z + TestInset),
        });
    }

    /// <summary>
    /// Poser les décalques sous <paramref name="model"/>, cachés jusqu'au premier nom.
    /// <paramref name="k"/> : mètres par unité du modèle (l'échelle de la fiche), pour la profondeur.
    /// </summary>
    public static List<Decal> Mount(Node3D model, List<Guide> guides, double k)
    {
        var list = new List<Decal>();
        float unit = (float)(1 / Math.Max(k, 1e-6));
        foreach (var g in guides)
        {
            var d = new Decal
            {
                Name = "nom_peint",
                Transform = g.Frame,
                /* TRENTE CENTIMÈTRES DE PROFONDEUR, de part et d'autre du guide : de quoi
                   atteindre un tableau galbé ou des moulures en retrait, sans traverser la
                   coque jusqu'au bordé d'en face. */
                Size = new Vector3(g.Width, Math.Max(g.Thick * 2, 0.6f * unit), g.Height),
                // pas sur les flancs qui fuient : seulement ce qui fait face au guide
                NormalFade = 0.35f,
                UpperFade = 0.05f,
                LowerFade = 0.05f,
                /* LISIBLE À LA LUNETTE : quatre cents mètres. Au-delà, des lettres d'un pied
                   ne se lisent plus, et un décalque de moins dans la grappe. */
                DistanceFadeEnabled = true,
                DistanceFadeBegin = 400,
                DistanceFadeLength = 100,
                Visible = false,
            };
            model.AddChild(d);
            list.Add(d);
            if (TestGuide) GD.Print($"[nom] décalque {g.Frame} taille {d.Size} ; coque {HullBox(model)}");
        }
        return list;
    }

    /// <summary>Peindre <paramref name="text"/> sur chaque décalque, de l'encre <paramref name="ink"/> (0xrrggbb, ou celle de noms.json).</summary>
    public static void Paint(List<Decal> decals, string text, string? ink)
    {
        Color col;
        try { col = ColorX.Hex(ink is { Length: > 0 } ? ink : Names.Ink); }
        catch (FormatException) { col = ColorX.Hex("0xd8b25a"); }
        foreach (var d in decals)
        {
            if (!GodotObject.IsInstanceValid(d)) continue;
            var tex = text.Length > 0 ? Letters(text, d.Size.X / d.Size.Z) : null;
            d.TextureAlbedo = tex;
            d.Modulate = col;
            d.Visible = tex != null;
            if (TestGuide) GD.Print($"[nom] « {text} » peint ({tex?.GetSize()})");
        }
    }

    // ------------------------------------------------------------------
    //  LES LETTRES
    // ------------------------------------------------------------------

    static Font? _font;
    static Font LetterFont => _font ??= new SystemFont
    {
        // des capitales romaines, comme on les peignait ; la police du jeu en dernier recours
        FontNames = Names.Fonts.Count > 0 ? Names.Fonts.ToArray() : new[] { "Garamond", "Georgia", "Times New Roman", "serif" },
        FontWeight = 500,
    };

    const int Px = 96;
    static readonly Dictionary<(string, int), ImageTexture?> _cache = new();

    /// <summary>
    /// Le nom en lettres blanches sur fond transparent, à la proportion du guide (la couleur
    /// vient du décalque). Un nom trop long pour la place passe sur deux lignes, coupé au
    /// blanc le plus proche du milieu, plutôt que de rapetisser jusqu'à l'illisible.
    /// </summary>
    static ImageTexture? Letters(string text, float aspect)
    {
        if (Names.Capitals) text = text.ToUpper(CultureInfo.GetCultureInfo("fr-FR"));
        var key = (text, (int)MathF.Round(aspect * 10));
        if (_cache.TryGetValue(key, out var have)) return have;

        var lines = new List<Image>();
        var one = Line(text);
        if (one == null) return _cache[key] = null;
        int cut = -1;
        if ((float)one.GetWidth() / one.GetHeight() > aspect * 2.2f)
            for (int i = 0, mid = text.Length / 2; i < mid && cut < 0; i++)
            {
                if (mid + i < text.Length && text[mid + i] == ' ') cut = mid + i;
                else if (text[mid - i] == ' ') cut = mid - i;
            }
        if (cut > 0 && Line(text[..cut]) is { } l1 && Line(text[(cut + 1)..]) is { } l2) { lines.Add(l1); lines.Add(l2); }
        else lines.Add(one);

        int tw = 0, th = 0;
        foreach (var l in lines) { tw = Math.Max(tw, l.GetWidth()); th += l.GetHeight(); }
        // une marge, puis la proportion du guide : le nom au milieu, jamais déformé
        int pad = Px / 6;
        tw += 2 * pad; th += 2 * pad;
        int W = Math.Max(tw, (int)MathF.Ceiling(th * aspect));
        int H = Math.Max(th, (int)MathF.Ceiling(tw / aspect));
        var img = Image.CreateEmpty(W, H, false, Image.Format.Rgba8);
        // du blanc transparent et non du noir : les niveaux réduits ne cernent pas les lettres de sombre
        img.Fill(new Color(1, 1, 1, 0));
        int y = (H - th) / 2 + pad;
        foreach (var l in lines)
        {
            img.BlendRect(l, new Rect2I(0, 0, l.GetWidth(), l.GetHeight()), new Vector2I((W - l.GetWidth()) / 2, y));
            y += l.GetHeight();
        }
        img.GenerateMipmaps();
        return _cache[key] = ImageTexture.CreateFromImage(img);
    }

    /* UNE LIGNE, GLYPHE PAR GLYPHE, sans écran : le serveur de texte façonne la ligne
       (crénage, accents, repli sur une autre police pour un caractère qui manque) et
       garde chaque glyphe dans un atlas en mémoire ; on les recopie un à un, à la place
       que le façonnage leur donne. Pas de SubViewport à attendre une image : le nom est
       peint dans la même image que la coque est mise à l'eau. */
    static Image? Line(string text)
    {
        var ts = TextServerManager.GetPrimaryInterface();
        var line = new TextLine();
        if (!line.AddString(text, LetterFont, Px)) return null;
        float asc = line.GetLineAscent(), desc = line.GetLineDescent(), width = line.GetLineWidth();
        /* LA BOÎTE DE LA LIGNE, ÉLARGIE À CE QUI EN DÉPASSE. L'ascendante qu'annonce une
           police ne couvre pas toujours l'accent d'une capitale : celle de Garamond s'arrête
           sous lui, et « É » sortait en « E », rogné par le haut. On place d'abord chaque
           glyphe, on prend la boîte de tout, puis on dessine. */
        var placed = new List<(Image Glyph, int X, int Y)>();
        var atlases = new Dictionary<(Rid, int, long), Image>();
        float pen = 0;
        int x0 = 0, y0 = -(int)MathF.Ceiling(asc), x1 = (int)MathF.Ceiling(width), y1 = (int)MathF.Ceiling(desc);
        foreach (var g in ts.ShapedTextGetGlyphs(line.GetRid()))
        {
            int repeat = Math.Max(1, g["repeat"].AsInt32());
            float adv = g["advance"].AsSingle();
            var f = g["font_rid"].AsRid();
            long idx = g["index"].AsInt64();
            var off = g["offset"].AsVector2();
            var sz = new Vector2I(g["font_size"].AsInt32(), 0);
            for (int r = 0; r < repeat; r++, pen += adv)
            {
                if (!f.IsValid) continue;
                long ti = ts.FontGetGlyphTextureIdx(f, sz, idx);
                if (ti < 0) continue;                       // un blanc
                if (!atlases.TryGetValue((f, sz.X, ti), out var atlas))
                    atlases[(f, sz.X, ti)] = atlas = ts.FontGetTextureImage(f, sz, ti);
                if (atlas == null) continue;
                var uv = ts.FontGetGlyphUVRect(f, sz, idx);
                var go = ts.FontGetGlyphOffset(f, sz, idx);
                var gs = ts.FontGetGlyphSize(f, sz, idx);
                var src = new Rect2I((int)uv.Position.X, (int)uv.Position.Y, (int)uv.Size.X, (int)uv.Size.Y);
                if (src.Size.X <= 0 || src.Size.Y <= 0) continue;
                var glyph = atlas.GetRegion(src);
                if (glyph.GetFormat() != Image.Format.Rgba8) glyph.Convert(Image.Format.Rgba8);
                // un atlas sur-échantillonné : le glyphe ramené à sa taille de dessin
                int gw = Math.Max(1, (int)MathF.Round(gs.X)), gh = Math.Max(1, (int)MathF.Round(gs.Y));
                if (gw != src.Size.X || gh != src.Size.Y) glyph.Resize(gw, gh, Image.Interpolation.Bilinear);
                // à partir de la ligne de base, à l'origine
                int gx = (int)MathF.Floor(pen + off.X + go.X), gy = (int)MathF.Floor(off.Y + go.Y);
                placed.Add((glyph, gx, gy));
                x0 = Math.Min(x0, gx); y0 = Math.Min(y0, gy);
                x1 = Math.Max(x1, gx + gw); y1 = Math.Max(y1, gy + gh);
            }
        }
        if (placed.Count == 0) return null;
        var img = Image.CreateEmpty(x1 - x0 + 2, y1 - y0 + 2, false, Image.Format.Rgba8);
        img.Fill(new Color(1, 1, 1, 0));
        foreach (var (glyph, gx, gy) in placed)
            img.BlendRect(glyph, new Rect2I(0, 0, glyph.GetWidth(), glyph.GetHeight()), new Vector2I(gx - x0 + 1, gy - y0 + 1));
        return img;
    }

    /// <summary>Essai : le nom en image, écrit dans <paramref name="path"/> (--nom-essai).</summary>
    public static bool Proof(string text, float aspect, string path)
    {
        var tex = Letters(text, aspect);
        if (tex == null) return false;
        // sur un bordé sombre, à l'encre de noms.json : blanc sur transparent ne se lit pas
        var src = tex.GetImage();
        src.ClearMipmaps();
        var ink = ColorX.Hex(Names.Ink);
        var img = Image.CreateEmpty(src.GetWidth(), src.GetHeight(), false, Image.Format.Rgba8);
        img.Fill(new Color(0.16f, 0.10f, 0.06f));
        for (int y = 0; y < src.GetHeight(); y++)
            for (int x = 0; x < src.GetWidth(); x++)
            {
                float a = src.GetPixel(x, y).A;
                if (a > 0) img.SetPixel(x, y, img.GetPixel(x, y).Lerp(ink, a));
            }
        return img.SavePng(path) == Error.Ok;
    }
}
