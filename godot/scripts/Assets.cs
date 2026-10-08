using Godot;

namespace NavalSim;

/// <summary>
/// OÙ SONT LES FICHIERS — et la seule chose qui sépare les deux versions du jeu.
///
/// Tout ce que le jeu lit (fiches, modèles, réglages, monde, pavillons) vit à la
/// RACINE DU DÉPÔT, hors du projet Godot, parce que la page le lit aussi : une
/// fiche corrigée l'est pour les deux moteurs, et c'est la moitié de ce qui tient
/// le portage droit.
///
/// Sauf que les deux n'ont pas la même contrainte. La page publiée est UN fichier
/// autonome, plafonné à 16 Mo, où chaque modèle entre en base64 ; Godot lit sur
/// le disque et n'a pas de plafond. Une frégate exportée en pleine définition
/// pèse 14 Mo à elle seule — elle est splendide dans Godot et elle interdit la
/// page.
///
/// D'où <c>godot-models/</c>, qui DOUBLE l'arborescence : un fichier qu'on y
/// trouve remplace celui de la racine, pour Godot seulement. Rien d'autre à
/// déclarer, aucune liste à tenir — c'est la présence du fichier qui décide, et
/// un dossier vide rend au jeu son comportement d'avant.
///
/// Il est HORS du dossier godot/ à dessein : ce qui est dans le projet, l'éditeur
/// l'importe, et ces modèles-là sont ouverts à l'exécution par GltfDocument. Ils
/// n'ont rien à faire dans la base de ressources.
///
/// Ce qu'on y met, en pratique : des modèles. Ce que le mécanisme accepte : tout
/// fichier, réglages compris — utile pour essayer une valeur sans toucher à ce
/// que la page verra.
/// </summary>
public static class Assets
{
    /// <summary>La racine du dépôt, un cran au-dessus du projet Godot.</summary>
    public static string Root { get; } = System.IO.Path.GetFullPath(
        System.IO.Path.Combine(ProjectSettings.GlobalizePath("res://"), ".."));

    /// <summary>Le doublon propre à Godot, s'il existe.</summary>
    public static string Heavy { get; } = System.IO.Path.Combine(Root, "godot-models");

    /// <summary>
    /// Le chemin complet d'un fichier donné RELATIVEMENT à la racine du dépôt
    /// (« ships/frigate17e/modele.glb »). Celui de <c>godot-models/</c> gagne
    /// s'il existe.
    /// </summary>
    public static string Path(string relative)
    {
        string mine = System.IO.Path.GetFullPath(System.IO.Path.Combine(Heavy, relative));
        return System.IO.File.Exists(mine)
            ? mine
            : System.IO.Path.GetFullPath(System.IO.Path.Combine(Root, relative));
    }

    /// <summary>
    /// Ouvrir un .glb à l'exécution (GltfDocument), rien si le fichier est
    /// illisible : chaque appelant dit lui-même ce qu'il fait à la place. Le chemin
    /// est COMPLET — passé par <see cref="Path"/> d'ordinaire.
    /// </summary>
    public static Node3D? LoadGlb(string fullPath) => LoadGlb(fullPath, out _);

    public static Node3D? LoadGlb(string fullPath, out Error err)
    {
        var doc = new GltfDocument();
        var state = new GltfState();
        err = doc.AppendFromFile(fullPath, state);
        return err == Error.Ok ? doc.GenerateScene(state) as Node3D : null;
    }

    /// <summary>The name ending that marks the artist's far version of a model, in the same .glb.</summary>
    public const string LodLowSuffix = "_LOD_low";

    /// <summary>This node, or one of its parents below the model's root, is the far version.</summary>
    static bool InLodLow(Node n, Node root)
    {
        for (var p = n; p != null && p != root.GetParent(); p = p.GetParent())
            if (p.Name.ToString().EndsWith(LodLowSuffix, System.StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    /// <summary>
    /// THE ARTIST'S FAR VERSION (asked for): an object named « …_LOD_low » in the
    /// .glb, laid over the full one. Up to <paramref name="far"/> metres the full
    /// model is drawn and the low one is not; beyond, the other way — with a short
    /// dithered cross-fade (8 % of the distance) so that the swap does not pop.
    /// Never both at once outside that band. Returns how many meshes are the far
    /// version (0: the model has none, and nothing was changed).
    /// </summary>
    public static int SplitLodLow(Node root, double far)
    {
        int lows = 0;
        foreach (var mi in NodeWalk.Meshes(root)) if (InLodLow(mi, root)) lows++;
        if (lows == 0) return 0;
        float d = (float)far, m = (float)(far * 0.08);
        foreach (var mi in NodeWalk.Meshes(root))
        {
            mi.VisibilityRangeFadeMode = GeometryInstance3D.VisibilityRangeFadeModeEnum.Self;
            if (InLodLow(mi, root)) { mi.VisibilityRangeBegin = d; mi.VisibilityRangeBeginMargin = m; }
            else { mi.VisibilityRangeEnd = d; mi.VisibilityRangeEndMargin = m; }
        }
        return lows;
    }

    /// <summary>Below this many triangles a mesh is drawn as it is: simplifying it gains nothing.</summary>
    public const int LodFrom = 20000;

    /// <summary>
    /// LEVELS OF DETAIL for what is posed on land. The editor import pipeline builds
    /// them; runtime GltfDocument does not, so a 470 000-triangle town block drew all
    /// of them at 800 m — ten of them took 3.7 ms a frame seen from above (journal,
    /// « Ce que coûtent les pâtés de maisons »). Every heavy mesh is handed to
    /// ImporterMesh, which simplifies it (meshoptimizer) into a chain of index
    /// buffers over the same vertices; the renderer then picks one by screen size,
    /// shadows included. Ships are left alone: their meshes are rewritten live.
    /// Returns the triangle count that got levels, and how much of it came from the
    /// disk cache (<see cref="LodCache"/>) when the model's file is given.
    /// </summary>
    public static (int Tris, int Cached) AddLods(Node root, string? fullPath = null)
    {
        int done = 0, cached = 0, index = -1;
        string? stem = fullPath != null ? CacheStem(fullPath) : null;
        // the artist drew the far version: the full model is not simplified, the far one still may be
        bool hasLow = false;
        foreach (var mi in NodeWalk.Meshes(root)) hasLow |= InLodLow(mi, root);
        foreach (var mi in NodeWalk.Meshes(root))
        {
            index++;
            if (hasLow && !InLodLow(mi, root)) continue;
            if (mi.Mesh is not ArrayMesh am || am.GetBlendShapeCount() > 0) continue;
            int tris = 0;
            for (int s = 0; s < am.GetSurfaceCount(); s++)
                if (am.SurfaceGetPrimitiveType(s) == Mesh.PrimitiveType.Triangles)
                {
                    int ix = am.SurfaceGetArrayIndexLen(s);
                    tris += (ix > 0 ? ix : am.SurfaceGetArrayLen(s)) / 3;
                }
            if (tris < LodFrom) continue;
            done += tris;
            string? file = stem != null ? $"{stem}_{index}.res" : null;
            if (file != null && FromCache(file, am) is { } hit) { mi.Mesh = hit; cached += tris; continue; }
            var im = new ImporterMesh();
            for (int s = 0; s < am.GetSurfaceCount(); s++)
                im.AddSurface(am.SurfaceGetPrimitiveType(s), am.SurfaceGetArrays(s), null, null,
                              am.SurfaceGetMaterial(s), am.SurfaceGetName(s));
            // the import defaults: merge normals under 25°, split above 60°
            im.GenerateLods(25, 60, new Godot.Collections.Array());
            var lodded = im.GetMesh();
            lodded.ResourceName = am.ResourceName;
            mi.Mesh = lodded;
            if (file != null) ToCache(file, stem!, lodded);
        }
        return (done, cached);
    }

    /// <summary>
    /// WHERE SIMPLIFIED MESHES ARE KEPT. Simplifying the 470 000-triangle town block
    /// takes 0.55 s at every launch; read back from disk it is a few milliseconds.
    /// In the user folder, not the repository: it is derived, and rebuilt when lost.
    /// </summary>
    public const string LodCache = "user://lod-cache";

    /// <summary>Bump when the simplification changes, so that old entries are not read.</summary>
    const int LodRecipe = 1;

    /* The KEY is the model's file — its name, size and write time — plus the recipe.
       A model re-exported from Blender gets a new key, and its stale entries go
       (ToCache). The mesh's rank in the scene completes it: a .glb holds several. */
    static string CacheStem(string fullPath)
    {
        var fi = new System.IO.FileInfo(fullPath);
        string name = System.IO.Path.GetFileNameWithoutExtension(fullPath);
        ulong h = 1469598103934665603UL;                          // FNV-1a over the full path
        foreach (char c in fullPath.ToLowerInvariant()) { h ^= c; h *= 1099511628211UL; }
        return $"{LodCache}/{name}-{h:x16}-{fi.Length:x}-{fi.LastWriteTimeUtc.Ticks:x}-r{LodRecipe}";
    }

    /// <summary>What every key of this model shares, whatever its version: name and path.</summary>
    static string CacheModel(string stem)
    {
        // the name may hold dashes itself: count them from the end (size, time, recipe)
        string s = System.IO.Path.GetFileName(stem);
        for (int k = 0; k < 3; k++) s = s[..s.LastIndexOf('-')];
        return s + "-";
    }

    /* The cached mesh is GEOMETRY ONLY. Its materials come from the .glb just loaded:
       saving them would copy every texture into the cache, and the haze pass is worn
       by the loaded materials. Read with CacheMode.Ignore — a model posed three times
       is loaded three times, and a shared mesh would take the last one's materials. */
    static ArrayMesh? FromCache(string file, ArrayMesh source)
    {
        if (!ResourceLoader.Exists(file)) return null;
        if (ResourceLoader.Load(file, "ArrayMesh", ResourceLoader.CacheMode.Ignore) is not ArrayMesh m
            || m.GetSurfaceCount() != source.GetSurfaceCount()) return null;
        for (int s = 0; s < m.GetSurfaceCount(); s++) m.SurfaceSetMaterial(s, source.SurfaceGetMaterial(s));
        m.ResourceName = source.ResourceName;
        return m;
    }

    static void ToCache(string file, string stem, ArrayMesh lodded)
    {
        string dir = ProjectSettings.GlobalizePath(LodCache);
        System.IO.Directory.CreateDirectory(dir);
        // the same model under an older key: its re-export made these useless
        string prefix = CacheModel(stem);
        foreach (var old in System.IO.Directory.GetFiles(dir, prefix + "*.res"))
            if (!System.IO.Path.GetFileName(old).StartsWith(System.IO.Path.GetFileName(stem)))
                System.IO.File.Delete(old);
        var bare = (ArrayMesh)lodded.Duplicate();
        for (int s = 0; s < bare.GetSurfaceCount(); s++) bare.SurfaceSetMaterial(s, null);
        var err = ResourceSaver.Save(bare, file);
        if (err != Error.Ok) GD.PushWarning($"[détail] cache non écrit ({err}) : {file}");
    }

    /// <summary>
    /// Dire au démarrage ce que ce dossier contient. Un doublon oublié est une
    /// panne muette du genre le plus vicieux : on corrige un modèle, on relance,
    /// et Godot montre l'autre.
    /// </summary>
    public static void Say()
    {
        if (!System.IO.Directory.Exists(Heavy)) return;
        var files = System.IO.Directory.GetFiles(Heavy, "*", System.IO.SearchOption.AllDirectories);
        if (files.Length == 0) return;
        var noms = new System.Collections.Generic.List<string>();
        foreach (var f in files)
        {
            string rel = System.IO.Path.GetRelativePath(Heavy, f).Replace('\\', '/');
            /* N'EST ANNONCÉ QUE CE QUI REMPLACE VRAIMENT quelque chose : le
               README de ce dossier et sa fiche d'allègement n'ont pas de jumeau
               à la racine, ils ne remplacent rien, et les lister ferait croire
               le contraire. */
            if (System.IO.File.Exists(System.IO.Path.Combine(Root, rel))) noms.Add(rel);
        }
        if (noms.Count > 0)
            GD.Print($"godot-models : {noms.Count} fichier(s) propres à Godot — {string.Join(", ", noms)}");
    }
}
