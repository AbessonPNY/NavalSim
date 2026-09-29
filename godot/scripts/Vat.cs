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
    /// <summary>Ce qu'il faut pour poser un figurant : son maillage et sa matière animée.</summary>
    public readonly record struct Figure(Mesh Mesh, ShaderMaterial Material, float Height);

    /// <summary>
    /// Charger un personnage de <c>characters/&lt;nom&gt;/</c>. Null si un des trois
    /// fichiers manque ou se lit mal — le quai reste alors désert, ce qui vaut
    /// mieux qu'un maillage figé dans sa pose de repos sans qu'on sache pourquoi.
    /// </summary>
    public static Figure? Load(string nom)
    {
        string dir = System.IO.Path.GetFullPath(System.IO.Path.Combine(Assets.Root, "characters", nom));
        string glb = System.IO.Path.Combine(dir, "vat_export.glb");
        string exr = System.IO.Path.Combine(dir, "vat_export_positions.exr");
        string png = System.IO.Path.Combine(dir, "vat_export_normals.png");
        foreach (var f in new[] { glb, exr, png })
            if (!System.IO.File.Exists(f))
            {
                GD.PushWarning($"characters/{nom} : {System.IO.Path.GetFileName(f)} manque — pas de figurant.");
                return null;
            }

        var doc = new GltfDocument();
        var state = new GltfState();
        if (doc.AppendFromFile(glb, state) != Error.Ok || doc.GenerateScene(state) is not Node3D root)
        {
            GD.PushWarning($"characters/{nom} : maillage illisible.");
            return null;
        }
        var mi = FirstMesh(root);
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

        var pos = LisExr(exr, nom);
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
        GD.Print(FormattableString.Invariant(
            $"figurant {nom} : {mesh.GetAabb().Size.Y:F2} m de haut, {pos.GetWidth()} sommets × {pos.GetHeight()} images"));
        return new Figure(mesh, mat, h);
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
            if (n.Contains("_positions.") || n.Contains("_normals.")) continue;
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

    static MeshInstance3D? FirstMesh(Node n)
    {
        if (n is MeshInstance3D m && m.Mesh != null) return m;
        foreach (var c in n.GetChildren())
            if (FirstMesh(c) is MeshInstance3D f) return f;
        return null;
    }
}
