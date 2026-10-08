using Godot;
using System;
using System.Collections.Generic;
using System.Text.Json;
using NavalSim.Core;

namespace NavalSim;

/// <summary>
/// Ou vivent les fiches navires.
///
/// LE DOSSIER FAIT FOI, et c'est la regle du projet qu'on ne casse pas en
/// portant : deposer un fichier dans ships/ doit suffire. Le dossier reste donc
/// a la RACINE du depot, partage avec la simulation JavaScript, plutot que
/// recopie sous godot/ -- deux copies d'une fiche, c'est deux fiches qui
/// divergeront, exactement la faute que l'invariant du plan de formes unique
/// existe pour empecher.
///
/// En developpement on y accede en remontant d'un cran depuis res://. Le jour de
/// l'export il faudra les embarquer, puisqu'un binaire exporte n'a plus de depot
/// autour de lui -- c'est le pendant de ce que build.js faisait en les inlinant
/// dans Naval.SHIP_DATA, et c'est la meme contrainte pour la meme raison.
/// </summary>
public static class ShipLibrary
{
    /// <summary>Le dossier ships/, a la racine du depot.</summary>
    public static string Folder
    {
        get
        {
            return System.IO.Path.Combine(Assets.Root, "ships");
        }
    }

    /// <summary>Le dossier des navires ajoutés par les mods : mods/navires/, à la racine du dépôt.</summary>
    public static string ModsFolder => System.IO.Path.Combine(Assets.Root, "mods", "navires");

    /// <summary>Le nom de la fiche dans le dossier d'un navire.</summary>
    public const string SheetName = "fiche.json";

    /// <summary>
    /// UN NAVIRE, UN DOSSIER (08/10/2026, pour les mods) : ships/&lt;id&gt;/fiche.json, son modèle et ce
    /// qui lui est propre à côté. Les fiches présentes : celles du jeu, triées, PUIS celles des mods,
    /// triées — un mod ajouté ne décale pas les indices des navires du jeu (l'histoire en nomme un
    /// par son rang). Un dossier sans fiche (textures/, communes) n'est pas un navire. Un mod qui porte
    /// l'identifiant d'un navire du jeu est écarté, avec un mot.
    /// </summary>
    public static List<string> Discover()
    {
        var found = new List<string>();
        if (!System.IO.Directory.Exists(Folder))
        {
            GD.PushWarning($"dossier des fiches introuvable : {Folder}");
            return found;
        }
        var ids = new HashSet<string>();
        foreach (var root in new[] { Folder, ModsFolder })
        {
            if (!System.IO.Directory.Exists(root)) continue;
            var dirs = new List<string>(System.IO.Directory.GetDirectories(root));
            dirs.Sort(StringComparer.Ordinal);
            foreach (var d in dirs)
            {
                string f = System.IO.Path.Combine(d, SheetName);
                if (!System.IO.File.Exists(f)) continue;
                string id = IdOf(f);
                if (!ids.Add(id)) { GD.PushWarning($"[navires] {f} : l'identifiant « {id} » est déjà pris, ce navire est écarté"); continue; }
                found.Add(f);
            }
        }
        return found;
    }

    /// <summary>
    /// L'IDENTIFIANT D'UN NAVIRE d'après le chemin de sa fiche : le nom de son dossier (« roebuck »).
    /// Une seule définition — le jeu nommait ses navires par leur fichier, et vingt endroits auraient lu
    /// « fiche ». Un ancien chemin plat (« x.json ») donne encore « x ».
    /// </summary>
    public static string IdOf(string path)
    {
        string name = System.IO.Path.GetFileName(path);
        return name == SheetName
            ? System.IO.Path.GetFileName(System.IO.Path.GetDirectoryName(path) ?? "")
            : System.IO.Path.GetFileNameWithoutExtension(path);
    }

    /// <summary>
    /// UNE CLÉ DE DONNÉES ramenée à un identifiant : « frigate.json » ou « frigate » (les fiches du
    /// monde et les réglages écrivent l'un ou l'autre), et le nom d'aujourd'hui d'un navire renommé.
    /// </summary>
    public static string Key(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        s = System.IO.Path.GetFileName(s.Replace('\\', '/'));
        if (s.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) s = s[..^5];
        return RenamedIds.TryGetValue(s, out var now) ? now : s;
    }

    /// <summary>Le rang d'un navire dans une liste de fiches, d'après une clé de données ; −1 s'il n'y est pas.</summary>
    public static int IndexOf(List<string> paths, string key)
    {
        string k = Key(key);
        return paths.FindIndex(p => IdOf(p) == k);
    }

    /// <summary>Le chemin de la fiche d'un navire d'après sa clé, ou null : le jeu, puis les mods.</summary>
    public static string? SheetPath(string key)
    {
        string id = Key(key);
        foreach (var root in new[] { Folder, ModsFolder })
        {
            string f = System.IO.Path.Combine(root, id, SheetName);
            if (System.IO.File.Exists(f)) return f;
        }
        return null;
    }

    /// <summary>
    /// LES FICHES RENOMMÉES : une partie enregistrée avant garde l'ancien nom de fichier.
    /// Le Speedwell est devenu le Roebuck (07/10/2026) : trop peu de plans et de peintures
    /// d'époque pour le premier.
    /// </summary>
    static readonly Dictionary<string, string> RenamedIds = new() { ["speedwell"] = "roebuck" };

    /// <summary>
    /// Charge une fiche. Une fiche illisible n'interrompt jamais rien -- meme
    /// contrat que le modele .glb manquant cote JavaScript, ou la coque
    /// procedurale est conservee avec un avertissement.
    /// </summary>
    public static ShipSpec? Load(string path)
    {
        try
        {
            var spec = ShipSpec.FromJson(System.IO.File.ReadAllText(path));
            // son dossier, depuis la racine du dépôt : ce qu'elle nomme sans dossier s'y lit
            string dir = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path)) ?? "";
            spec.Folder = System.IO.Path.GetRelativePath(Assets.Root, dir).Replace('\\', '/');
            return spec;
        }
        catch (System.Exception e)
        {
            GD.PushWarning($"fiche illisible {IdOf(path)} : {e.Message}");
            return null;
        }
    }

    /// <summary>
    /// UNE COQUE QU'ON PEUT PRENDRE EN JEU LIBRE, telle que la carte la montre.
    /// </summary>
    public sealed class FreeShip
    {
        /// <summary>Le chemin de sa fiche, pour la lancer.</summary>
        public string Path = "";
        /// <summary>Le nom sur la carte, sans son millesime.</summary>
        public string Label = "";
        /// <summary>Son annee, 0 si on ne la connait pas.</summary>
        public int Year;
        /// <summary>Son deplacement, lu dans la fiche et nulle part ailleurs.</summary>
        public double Tonnes;
        /// <summary>Le visuel, chemin absolu, vide s'il n'y en a pas.</summary>
        public string Image = "";
        /// <summary>La raison qui l'interdit, vide si on peut la prendre.</summary>
        public string Locked = "";

        /// <summary>Ce qui s'ecrit sous la carte : « Roter Löwe - 1597 - 300 t ».</summary>
        public string Caption
        {
            get
            {
                /* LE MILLIER PREND SON ESPACE, comme partout ailleurs en francais :
                   la raison d un verrou parlait de « 2 000 t » quand la carte sous
                   elle annoncait « 2000 t ». */
                var fr = System.Globalization.CultureInfo.GetCultureInfo("fr-FR");
                string t = (Tonnes < 10 ? (Math.Round(Tonnes * 10) / 10).ToString("0.#", fr)
                                        : Math.Round(Tonnes).ToString("N0", fr)) + " t";
                return Year > 0 ? $"{Label} - {Year} - {t}" : $"{Label} - {t}";
            }
        }
    }

    /// <summary>
    /// LES NAVIRES OFFERTS EN JEU LIBRE, dans l'ordre de libre.json.
    ///
    /// Le dossier fait foi pour ce qui EXISTE, ce fichier pour ce qui est
    /// OFFERT : un chaland et une bouee sont des fiches valides qu'on ne propose
    /// pas. Sans le fichier, tout le dossier est offert — on ne prive jamais le
    /// joueur de ses navires sur une faute de virgule, on le dit et on continue.
    /// </summary>
    /// <param name="debug">Le mode débug (trois clics sur Crédits) : tout est ouvert.</param>
    public static List<FreeShip> FreeRoster(List<string> paths, bool debug = false)
    {
        HashSet<string>? open = null;
        string shut = "Pas encore à votre portée.";
        var listing = new List<FreeShip>();
        string fichier = System.IO.Path.Combine(Folder, "libre.json");
        var demandes = new List<(string ship, string label, int year, string image, string locked)>();
        if (System.IO.File.Exists(fichier))
        {
            try
            {
                using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(fichier));
                /* CE QUI EST OUVERT D'EMBLÉE : la liste « ouverts », les autres fermés avec la
                   raison de « fermes » — s'ils n'en ont pas une à eux. */
                if (doc.RootElement.TryGetProperty("ouverts", out var ov) && ov.ValueKind == JsonValueKind.Array)
                {
                    open = new HashSet<string>();
                    foreach (var o in ov.EnumerateArray()) if (o.GetString() is string s) open.Add(s);
                }
                if (Js.Str(doc.RootElement, "fermes") is { Length: > 0 } why) shut = why;
                if (doc.RootElement.TryGetProperty("ships", out var arr) && arr.ValueKind == JsonValueKind.Array)
                    foreach (var e in arr.EnumerateArray())
                        demandes.Add((Js.Str(e, "ship"), Js.Str(e, "label"),
                                      e.TryGetProperty("year", out var y) && y.TryGetInt32(out int yy) ? yy : 0,
                                      Js.Str(e, "image"), Js.Str(e, "locked")));
            }
            catch (Exception ex) { GD.PushWarning($"libre.json illisible ({ex.Message}) : tout le dossier est offert"); }
        }
        if (demandes.Count == 0)
            foreach (var q in paths)
                demandes.Add((IdOf(q), "", 0, "", ""));

        foreach (var d in demandes)
        {
            int k = paths.FindIndex(q => IdOf(q) == Key(d.ship));
            if (k < 0) { GD.PushWarning($"libre.json nomme « {d.ship} », qui n'a pas de fiche"); continue; }
            var spec = Load(paths[k]);
            if (spec == null) continue;
            string label = d.label, nom = spec.Name;
            int year = d.year;
            /* LE MILLESIME SE LIT DANS LE NOM, et c'est ce qui evite de l'ecrire
               deux fois : « La Boussole 1597 » donne « La Boussole » et 1597. La
               fiche peut toujours trancher, mais elle n'a rien a redire quand le
               nom suffit. */
            if (nom.Length > 5 && int.TryParse(nom.Substring(nom.Length - 4), out int fin)
                && fin > 1000 && fin < 2100 && nom[nom.Length - 5] == ' ')
            {
                if (year == 0) year = fin;
                if (label.Length == 0) label = nom.Substring(0, nom.Length - 5);
            }
            if (label.Length == 0) label = nom;

            string img = d.image.Length > 0 ? System.IO.Path.Combine(Assets.Root, d.image) : "";
            if (img.Length == 0)
                foreach (var ext in new[] { ".jpg", ".png", ".jpeg", ".webp" })
                {
                    string essai = System.IO.Path.Combine(Assets.Root, "ships", "textures", "menu", d.ship + ext);
                    if (System.IO.File.Exists(essai)) { img = essai; break; }
                }
            if (img.Length > 0 && !System.IO.File.Exists(img))
            {
                GD.PushWarning($"visuel introuvable pour « {d.ship} » : {img}");
                img = "";
            }
            listing.Add(new FreeShip
            {
                Path = paths[k], Label = label, Year = year,
                Tonnes = spec.Tonnes, Image = img,
                Locked = debug ? "" : d.locked.Length > 0 ? d.locked : open != null && !open.Contains(d.ship) ? shut : ""
            });
        }
        return listing;
    }

}
