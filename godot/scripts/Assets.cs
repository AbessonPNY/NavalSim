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
    /// (« ships/models/roter_lowe_1597.glb »). Celui de <c>godot-models/</c> gagne
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
    /// Returns the triangle count that got levels.
    /// </summary>
    public static int AddLods(Node root)
    {
        int done = 0;
        foreach (var mi in NodeWalk.Meshes(root))
        {
            if (mi.Mesh is not ArrayMesh am || am.GetBlendShapeCount() > 0) continue;
            int tris = 0;
            for (int s = 0; s < am.GetSurfaceCount(); s++)
                if (am.SurfaceGetPrimitiveType(s) == Mesh.PrimitiveType.Triangles)
                {
                    int ix = am.SurfaceGetArrayIndexLen(s);
                    tris += (ix > 0 ? ix : am.SurfaceGetArrayLen(s)) / 3;
                }
            if (tris < LodFrom) continue;
            var im = new ImporterMesh();
            for (int s = 0; s < am.GetSurfaceCount(); s++)
                im.AddSurface(am.SurfaceGetPrimitiveType(s), am.SurfaceGetArrays(s), null, null,
                              am.SurfaceGetMaterial(s), am.SurfaceGetName(s));
            // the import defaults: merge normals under 25°, split above 60°
            im.GenerateLods(25, 60, new Godot.Collections.Array());
            var lodded = im.GetMesh();
            lodded.ResourceName = am.ResourceName;
            mi.Mesh = lodded;
            done += tris;
        }
        return done;
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
