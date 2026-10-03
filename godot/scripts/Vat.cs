using Godot;
using System;

namespace NavalSim;

/// <summary>
/// UN FIGURANT CUIT DANS UNE TEXTURE — chargé une fois, posé où l'on veut.
///
/// Trois fichiers, écrits par VAT Toolkit dans <c>characters/&lt;nom&gt;/</c> :
/// le maillage (<c>vat_export.glb</c>), ses déplacements image par image
/// (<c>_positions.exr</c>) et ses normales (<c>_normals.png</c>). Le format est
/// décrit dans <c>characters/README.md</c> et lu par <c>shaders/vat.gdshader</c>.
///
/// OUVERT À L'EXÉCUTION, comme les modèles de navires : ces fichiers vivent hors
/// de <c>res://</c>, Godot ne les importe pas, et c'est voulu — on veut pouvoir
/// recuire un personnage dans Blender et relancer sans réimporter quoi que ce
/// soit. L'EXR est chargé en demi-flottants et ne doit JAMAIS être compressé :
/// des décalages en huit bits font trembler un visage.
///
/// GODOT SEULEMENT. Quinze mégaoctets de texture flottante ne tiennent pas dans
/// les seize de la page, et le shader qui les lit non plus. C'est le même cas
/// que La Boussole et le sloop, et il faut le savoir avant de publier.
/// </summary>
public static class Vat
{
    /// <summary>
    /// Ce qu'il faut pour poser un figurant : son maillage, sa matière animée, sa
    /// taille, et le POINT LE PLUS BAS qu'il atteint en bougeant.
    /// </summary>
    public readonly record struct Figure(Mesh Mesh, ShaderMaterial Material, float Height, float Floor);

    /// <summary>
    /// Charger un personnage de <c>characters/&lt;nom&gt;/</c>. Null si un des trois
    /// fichiers manque ou se lit mal — le quai reste alors désert, ce qui vaut
    /// mieux qu'un maillage figé dans sa pose de repos sans qu'on sache pourquoi.
    /// </summary>
    public static Figure? Load(string nom)
    {
        string dir = System.IO.Path.GetFullPath(System.IO.Path.Combine(Assets.Root, "characters", nom));
        if (!System.IO.Directory.Exists(dir))
        { GD.PushWarning($"characters/{nom} : dossier introuvable."); return null; }

        /* LA CUISSON LA PLUS RÉCENTE, et non un nom écrit en dur.

           On recuit un personnage dix fois avant qu'il tienne, et VAT Toolkit
           nomme ses sorties comme on le lui dit — vat_export, vat_export2… Exiger
           un nom exact obligerait à renommer trois fichiers à chaque essai, ou
           pire, à corriger le code. On repère donc les cuissons par leur SUFFIXE,
           qui lui ne change pas, et la plus fraîche l'emporte : on dépose, elle
           gagne. Les anciennes restent à côté sans gêner, ce qui est commode pour
           comparer.

           Et l'on DIT laquelle a été prise. Un choix automatique qu'on ne voit
           pas est un piège : on croit essayer la nouvelle et l'on regarde
           l'ancienne. */
        string? pre = null;
        DateTime frais = DateTime.MinValue;
        int combien = 0;
        foreach (var f in System.IO.Directory.GetFiles(dir, "*_positions.*"))
        {
            string p = System.IO.Path.GetFileName(f);
            if (p.EndsWith("_positions.exr")) p = p[..^"_positions.exr".Length];
            else if (p.EndsWith("_positions.png")) p = p[..^"_positions.png".Length];
            else continue;
            if (!System.IO.File.Exists(System.IO.Path.Combine(dir, p + "_normals.png"))
                || !System.IO.File.Exists(System.IO.Path.Combine(dir, p + ".glb"))) continue;
            combien++;
            var t = System.IO.File.GetLastWriteTimeUtc(f);
            if (t > frais) { frais = t; pre = p; }
        }
        if (pre == null)
        {
            GD.PushWarning($"characters/{nom} : aucune cuisson complète (il faut <nom>.glb, <nom>_positions.exr et <nom>_normals.png).");
            return null;
        }
        if (combien > 1) GD.Print($"figurant {nom} : cuisson « {pre} », la plus récente des {combien}");
        string glb = System.IO.Path.Combine(dir, pre + ".glb");
        string png = System.IO.Path.Combine(dir, pre + "_normals.png");
        string exr = System.IO.Path.Combine(dir, pre + "_positions.exr");
        string ppng = System.IO.Path.Combine(dir, pre + "_positions.png");
        bool enPng = System.IO.File.Exists(ppng);

        if (Assets.LoadGlb(glb) is not Node3D root)
        {
            GD.PushWarning($"characters/{nom} : maillage illisible.");
            return null;
        }
        var mi = NodeWalk.FirstMesh(root);
        if (mi?.Mesh is not Mesh mesh) { root.QueueFree(); return null; }

        /* SANS UV2, RIEN NE MARCHE. C'est la colonne du sommet dans la texture ;
           un maillage exporté sans elle donnerait un personnage dont tous les
           sommets lisent la même colonne — un accordéon. On refuse plutôt que de
           montrer ça. */
        var arr = mesh.SurfaceGetArrays(0);
        if (arr[(int)Mesh.ArrayType.TexUV2].VariantType == Variant.Type.Nil)
        {
            GD.PushWarning($"characters/{nom} : le maillage n'a pas d'UV2 — c'est l'index de sommet, sans lui la cuisson ne se lit pas.");
            root.QueueFree();
            return null;
        }

        var pos = enPng ? LisPositionsPng(ppng, dir, pre, nom) : LisExr(exr, nom);
        var nrm = Charger(png, nom, "normales");
        root.QueueFree();
        if (pos == null || nrm == null) return null;

        var mat = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/vat.gdshader") };
        mat.SetShaderParameter("u_pos", pos);
        mat.SetShaderParameter("u_nrm", nrm);

        /* SA PEAU, POSÉE À CÔTÉ. L'exportateur laisse le maillage nu — ni
           matière, ni image — mais garde ses UV. On cherche donc l'image dans le
           dossier du personnage plutôt que dans le fichier, et on accepte les
           noms qu'un modeleur écrit sans y penser : albedo, basecolor, diffuse.
           Rien trouvé : il reste uni, et on le dit une fois. */
        if (Peau(dir) is ImageTexture peau)
        {
            mat.SetShaderParameter("u_albedo", peau);
            mat.SetShaderParameter("u_hastex", 1.0f);
        }
        else GD.Print($"figurant {nom} : aucune peau dans le dossier — il reste uni.");
        // la hauteur de la texture EST le nombre d'images : une ligne, une image
        mat.SetShaderParameter("u_frames", (float)pos.GetHeight());

        float h = mesh.GetAabb().Size.Y;
        float sol = Plancher(mesh, pos);
        GD.Print(FormattableString.Invariant(
            $"figurant {nom} : {h:F2} m de haut, {pos.GetWidth()} sommets × {pos.GetHeight()} images, plancher {sol:F3} m"));
        return new Figure(mesh, mat, h, sol);
    }

    /* PAS DE COMPRESSION, PAS DE MIPMAP, PAS DE FILTRE. Une texture de VAT n'est
       pas une image : c'est un tableau de nombres dont chaque texel appartient à
       un sommet précis. Interpolée, elle mélange deux sommets voisins qui n'ont
       aucune raison de se ressembler ; réduite en mipmap, elle les mélange tous.
       Le filtre au plus proche est posé dans le shader ; ici on veille au format. */
    static ImageTexture? Charger(string path, string nom, string quoi)
    {
        var img = Image.LoadFromFile(path);
        if (img == null)
        {
            GD.PushWarning($"characters/{nom} : {quoi} illisibles.");
            return null;
        }
        img.ClearMipmaps();
        return ImageTexture.CreateFromImage(img);
    }

    /// <summary>
    /// LES POSITIONS EN PNG 16 BITS, normalisées.
    ///
    /// C'est le meilleur des deux formats et c'est mesuré : sur une étendue de
    /// déplacements de 0,37 m, seize bits donnent 5,7 MICROMÈTRES par pas, là où
    /// le demi-flottant de l'EXR en donne environ mille fois moins de précision
    /// aux mêmes valeurs. Et le fichier pèse quatre fois moins.
    ///
    /// MAIS LE PNG NE PORTE PAS SES BORNES. VAT Toolkit remappe les déplacements
    /// de [min, max] vers [0, 1] et n'écrit ces deux nombres NULLE PART — ils ne
    /// sont qu'affichés dans son panneau. Sans eux on lit des nombres sans unité,
    /// et le personnage se déforme d'un facteur inconnu. On les prend donc dans
    /// une fiche à côté, <c>&lt;cuisson&gt;.json</c>, et l'on REFUSE de deviner :
    /// un facteur inventé donnerait un résultat plausible et faux, ce qui est
    /// pire qu'un refus.
    /// </summary>
    static ImageTexture? LisPositionsPng(string path, string dir, string pre, string nom)
    {
        string fiche = System.IO.Path.Combine(dir, pre + ".json");
        if (!System.IO.File.Exists(fiche))
        {
            GD.PushWarning($"characters/{nom} : {pre}.json manque. Les positions sont en PNG normalisé, "
                + "donc il faut les bornes que VAT Toolkit affiche sous « Wrap » : "
                + "{ \"min\": ..., \"max\": ... }");
            return null;
        }
        double min, max;
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(System.IO.File.ReadAllText(fiche));
            min = doc.RootElement.GetProperty("min").GetDouble();
            max = doc.RootElement.GetProperty("max").GetDouble();
        }
        catch (Exception e)
        {
            GD.PushWarning($"characters/{nom} : {pre}.json illisible ({e.Message}).");
            return null;
        }

        /* ET ON LE DÉCODE NOUS-MÊMES, parce que GODOT RAMÈNE UN PNG 16 BITS À
           HUIT. Relevé : `Image.LoadFromFile` rend un Rgb8 sur ce fichier, ce qui
           réduit la précision de 5,7 micromètres à 1,5 MILLIMÈTRE par pas. Sur un
           corps d'un mètre c'est un tremblement visible — et c'est exactement le
           genre de perte silencieuse qu'on ne soupçonne pas, puisque l'image se
           charge sans erreur.

           Le PNG non entrelacé est simple : un en-tête, des blocs IDAT à
           décompresser, et des lignes préfixées d'un octet de filtre. Soixante
           lignes contre une dégradation invisible, le marché est bon. */
        var brut = LisPng16(path, nom);
        if (brut is not (int W, int H, ushort[] px)) return null;
        GD.Print(FormattableString.Invariant(
            $"figurant {nom} : positions PNG {W}×{H} en 16 bits, bornes {min:F4} à {max:F4}, {(max - min) / 65535 * 1e6:F1} µm par pas"));

        /* LE REMAPPAGE EST FAIT ICI, UNE FOIS, plutôt que dans le shader à chaque
           sommet et à chaque image : c'est le même calcul, et il n'a aucune raison
           d'être refait soixante fois par seconde. On sort en demi-flottants, le
           format que le shader attend déjà de l'EXR — un seul chemin ensuite. */
        double s = max - min;
        var outBuf = new byte[W * H * 8];
        var un = BitConverter.GetBytes((ushort)0x3C00);       // 1.0 en demi-flottant
        for (int i = 0; i < W * H; i++)
        {
            for (int c = 0; c < 3; c++)
            {
                var h = (Half)(px[i * 3 + c] / 65535.0 * s + min);
                var bb = BitConverter.GetBytes(BitConverter.HalfToUInt16Bits(h));
                outBuf[i * 8 + c * 2] = bb[0];
                outBuf[i * 8 + c * 2 + 1] = bb[1];
            }
            outBuf[i * 8 + 6] = un[0];
            outBuf[i * 8 + 7] = un[1];
        }
        var img = Image.CreateFromData(W, H, false, Image.Format.Rgbah, outBuf);
        return ImageTexture.CreateFromImage(img);
    }

    /// <summary>
    /// UN PNG 16 BITS, LU ENTIER. Rend (largeur, hauteur, valeurs RVB non
    /// signées), ou nul en disant pourquoi. On refuse tout ce qui n'est pas du
    /// seize bits RVB ou RVBA non entrelacé : une cuisson en huit bits doit être
    /// refaite, pas rattrapée en silence.
    /// </summary>
    static (int, int, ushort[])? LisPng16(string path, string nom)
    {
        var b = System.IO.File.ReadAllBytes(path);
        if (b.Length < 8 || b[0] != 0x89 || b[1] != 'P' || b[2] != 'N' || b[3] != 'G')
        { GD.PushWarning($"characters/{nom} : ce n'est pas un PNG."); return null; }

        int W = 0, H = 0, bits = 0, type = 0, entrelace = 0;
        var idat = new System.IO.MemoryStream();
        int o = 8;
        while (o + 8 <= b.Length)
        {
            int len = (b[o] << 24) | (b[o + 1] << 16) | (b[o + 2] << 8) | b[o + 3];
            string tag = System.Text.Encoding.ASCII.GetString(b, o + 4, 4);
            int at = o + 8;
            if (tag == "IHDR")
            {
                W = (b[at] << 24) | (b[at + 1] << 16) | (b[at + 2] << 8) | b[at + 3];
                H = (b[at + 4] << 24) | (b[at + 5] << 16) | (b[at + 6] << 8) | b[at + 7];
                bits = b[at + 8]; type = b[at + 9]; entrelace = b[at + 12];
            }
            else if (tag == "IDAT") idat.Write(b, at, len);
            else if (tag == "IEND") break;
            o = at + len + 4;
        }
        if (bits != 16 || (type != 2 && type != 6) || entrelace != 0)
        {
            GD.PushWarning($"characters/{nom} : PNG en {bits} bits, type {type}, entrelacé {entrelace} — "
                + "il faut du 16 bits RVB(A) non entrelacé. Recuire avec « Normalize » et un backend 16 bits.");
            return null;
        }

        idat.Position = 0;
        using var zs = new System.IO.Compression.ZLibStream(idat, System.IO.Compression.CompressionMode.Decompress);
        int canaux = type == 6 ? 4 : 3, bpp = canaux * 2, stride = W * bpp;
        var raw = new byte[(stride + 1) * H];
        int lu = 0;
        while (lu < raw.Length)
        {
            int n = zs.Read(raw, lu, raw.Length - lu);
            if (n <= 0) break;
            lu += n;
        }
        if (lu < raw.Length)
        { GD.PushWarning($"characters/{nom} : PNG tronqué ({lu} octets sur {raw.Length})."); return null; }

        /* LE DÉFILTRAGE, ligne par ligne et EN PLACE dans un tampon à part : chaque
           filtre se réfère à l'octet de gauche et à la ligne du dessus DÉJÀ
           défiltrée, jamais aux données brutes. C'est la faute classique. */
        var img = new byte[stride * H];
        for (int y = 0; y < H; y++)
        {
            int f = raw[y * (stride + 1)];
            int src = y * (stride + 1) + 1, dst = y * stride;
            for (int i = 0; i < stride; i++)
            {
                int A = i >= bpp ? img[dst + i - bpp] : 0;
                int B = y > 0 ? img[dst - stride + i] : 0;
                int C = (i >= bpp && y > 0) ? img[dst - stride + i - bpp] : 0;
                int v = raw[src + i];
                v += f switch
                {
                    1 => A,
                    2 => B,
                    3 => (A + B) / 2,
                    4 => Paeth(A, B, C),
                    _ => 0
                };
                img[dst + i] = (byte)v;
            }
        }

        var px = new ushort[W * H * 3];
        for (int i = 0; i < W * H; i++)
            for (int c = 0; c < 3; c++)
                px[i * 3 + c] = (ushort)((img[i * bpp + c * 2] << 8) | img[i * bpp + c * 2 + 1]);
        return (W, H, px);
    }

    static int Paeth(int a, int b, int c)
    {
        int p = a + b - c, pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
        return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
    }

    /// <summary>
    /// LIRE L'EXR NOUS-MÊMES, parce que Godot ne le fait pas à l'exécution : son
    /// chargeur OpenEXR est un greffon d'ÉDITEUR, et `Image.LoadFromFile` rend
    /// une erreur sur un .exr dans un jeu qui tourne.
    ///
    /// Le nôtre est le cas facile, et c'est pour cela qu'on l'ouvre à la main
    /// plutôt que d'ajouter une étape de conversion à la chaîne du modéliste :
    /// NON COMPRESSÉ, en demi-flottants, une ligne par image. Le format se réduit
    /// alors à un en-tête d'attributs, une table d'adresses de lignes, puis les
    /// lignes elles-mêmes — chaque canal d'un bout à l'autre, dans l'ordre
    /// ALPHABÉTIQUE (A, B, G, R), ce qui n'est pas celui qu'on veut.
    ///
    /// On REFUSE tout ce qui sort de ce cas, en le disant : une cuisson
    /// compressée ou en flottants simples se lirait de travers, et un personnage
    /// qui explose silencieusement coûte une heure à comprendre.
    /// </summary>
    static ImageTexture? LisExr(string path, string nom)
    {
        var b = System.IO.File.ReadAllBytes(path);
        if (b.Length < 8 || BitConverter.ToUInt32(b, 0) != 20000630)
        { GD.PushWarning($"characters/{nom} : ce n'est pas un EXR."); return null; }

        int o = 8;
        var attrs = new System.Collections.Generic.Dictionary<string, (string Type, int At, int Len)>();
        while (o < b.Length)
        {
            int e = Array.IndexOf(b, (byte)0, o);
            string name = System.Text.Encoding.ASCII.GetString(b, o, e - o); o = e + 1;
            if (name.Length == 0) break;
            e = Array.IndexOf(b, (byte)0, o);
            string type = System.Text.Encoding.ASCII.GetString(b, o, e - o); o = e + 1;
            int n = BitConverter.ToInt32(b, o); o += 4;
            attrs[name] = (type, o, n); o += n;
        }
        if (!attrs.TryGetValue("dataWindow", out var dw) || !attrs.TryGetValue("compression", out var cp)
            || !attrs.TryGetValue("channels", out var ch))
        { GD.PushWarning($"characters/{nom} : EXR sans dataWindow, channels ou compression."); return null; }

        if (b[cp.At] != 0)
        { GD.PushWarning($"characters/{nom} : EXR COMPRESSÉ (code {b[cp.At]}). Recuire sans compression."); return null; }

        int x0 = BitConverter.ToInt32(b, dw.At), y0 = BitConverter.ToInt32(b, dw.At + 4);
        int x1 = BitConverter.ToInt32(b, dw.At + 8), y1 = BitConverter.ToInt32(b, dw.At + 12);
        int W = x1 - x0 + 1, H = y1 - y0 + 1;

        /* LES CANAUX, DANS L'ORDRE OÙ L'EXR LES RANGE — alphabétique, donc
           A, B, G, R. On relève la place de chacun plutôt que de la supposer :
           une cuisson sans alpha en aurait trois, et le décalage serait muet. */
        int co = ch.At, plan = 0;
        var rang = new System.Collections.Generic.Dictionary<string, int>();
        while (co < ch.At + ch.Len - 1)
        {
            int e = Array.IndexOf(b, (byte)0, co);
            string cn = System.Text.Encoding.ASCII.GetString(b, co, e - co);
            if (cn.Length == 0) break;
            co = e + 1;
            int type = BitConverter.ToInt32(b, co); co += 16;
            if (type != 1)
            { GD.PushWarning($"characters/{nom} : le canal {cn} n'est pas en demi-flottants."); return null; }
            rang[cn] = plan++;
        }
        foreach (var need in new[] { "R", "G", "B" })
            if (!rang.ContainsKey(need))
            { GD.PushWarning($"characters/{nom} : EXR sans canal {need}."); return null; }

        // la table des lignes, puis chaque ligne : y, taille, puis les canaux en entier
        int table = o;
        var outBuf = new byte[W * H * 8];              // RGBAH : quatre demi-flottants
        var un = BitConverter.GetBytes((ushort)0x3C00); // 1.0 en demi-flottant, pour l'alpha
        for (int y = 0; y < H; y++)
        {
            long at = BitConverter.ToInt64(b, table + y * 8);
            int px = (int)at + 8;
            int dst = y * W * 8;
            for (int x = 0; x < W; x++)
            {
                for (int c = 0; c < 3; c++)
                {
                    int src = px + rang["RGB"[c].ToString()] * W * 2 + x * 2;
                    outBuf[dst + x * 8 + c * 2] = b[src];
                    outBuf[dst + x * 8 + c * 2 + 1] = b[src + 1];
                }
                outBuf[dst + x * 8 + 6] = un[0];
                outBuf[dst + x * 8 + 7] = un[1];
            }
        }
        var img = Image.CreateFromData(W, H, false, Image.Format.Rgbah, outBuf);
        return ImageTexture.CreateFromImage(img);
    }

    /// <summary>
    /// LE POINT LE PLUS BAS DU FIGURANT EN MOUVEMENT, dans ses propres unités.
    ///
    /// Une cuisson ne commence pas forcément à la pose de repos : celle du pirate
    /// le tient neuf centimètres plus haut d'un bout à l'autre, soit SEIZE une
    /// fois à la taille d'un homme — il flottait au-dessus du tablier, signalé.
    /// Le décalage n'est écrit nulle part ; il se mesure.
    ///
    /// On ne regarde que le BAS DU CORPS (sous le quart de sa hauteur) : le point
    /// le plus bas est toujours un pied, et parcourir les sept mille sommets à
    /// chaque image coûterait deux cent mille lectures pour rien.
    ///
    /// Et c'est un MINIMUM sur toute l'animation, pas une moyenne : posé sur la
    /// moyenne, il s'enfoncerait dans le pont la moitié du temps.
    /// </summary>
    static float Plancher(Mesh mesh, ImageTexture tex)
    {
        var arr = mesh.SurfaceGetArrays(0);
        var v = arr[(int)Mesh.ArrayType.Vertex].AsVector3Array();
        var uv2 = arr[(int)Mesh.ArrayType.TexUV2].AsVector2Array();
        if (v.Length == 0 || uv2.Length != v.Length) return 0;

        var img = tex.GetImage();
        int W = img.GetWidth(), H = img.GetHeight();
        float haut = mesh.GetAabb().Size.Y, seuil = mesh.GetAabb().Position.Y + haut * 0.25f;
        float bas = float.MaxValue;
        for (int i = 0; i < v.Length; i++)
        {
            if (v[i].Y > seuil) continue;
            int c = Math.Clamp((int)(uv2[i].X * W), 0, W - 1);
            for (int y = 0; y < H; y++)
            {
                /* LA HAUTEUR VIENT DU CANAL BLEU : le shader lit (x, y, z) dans
                   (R, B, G) — la conversion Z-up de Blender vers le Y-up du glTF.
                   Une seule définition, deux usagers ; si l'une change, l'autre
                   doit suivre. */
                float y2 = v[i].Y + img.GetPixel(c, y).B;
                if (y2 < bas) bas = y2;
            }
        }
        return bas == float.MaxValue ? 0 : bas;
    }

    /// <summary>
    /// L'IMAGE DE PEAU, cherchée par son NOM et non par un chemin écrit en dur :
    /// le modeleur l'exporte de Blender comme il veut, et lui imposer un nom
    /// exact est le genre de règle qu'on oublie six mois plus tard. Le premier
    /// fichier dont le nom porte albedo, basecolor, diffuse ou couleur l'emporte,
    /// et on écarte les deux fichiers de la cuisson, qui ne sont pas des images.
    /// </summary>
    static ImageTexture? Peau(string dir)
    {
        foreach (var f in System.IO.Directory.GetFiles(dir))
        {
            string n = System.IO.Path.GetFileName(f).ToLowerInvariant();
            if (n.Contains("_positions.") || n.Contains("_normals.")) continue;   // les fichiers de cuisson, de n'importe laquelle
            if (!n.EndsWith(".png") && !n.EndsWith(".jpg") && !n.EndsWith(".jpeg") && !n.EndsWith(".webp")) continue;
            if (!n.Contains("albedo") && !n.Contains("basecolor") && !n.Contains("base_color")
                && !n.Contains("diffuse") && !n.Contains("couleur")) continue;
            var img = Image.LoadFromFile(f);
            if (img == null) continue;
            img.GenerateMipmaps();
            return ImageTexture.CreateFromImage(img);
        }
        return null;
    }

}
