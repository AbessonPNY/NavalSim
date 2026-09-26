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

    /// <summary>
    /// CE QUI EST DANS ships/ SANS ETRE UN NAVIRE : des LISTES de navires.
    /// index.json dit lesquels la page doit demander, libre.json lesquels le jeu
    /// libre propose. Les lire comme des fiches donnerait deux bateaux fantomes
    /// dans le selecteur, et qui decalent tous les indices derriere eux.
    /// </summary>
    public static readonly string[] NotShips = { "index.json", "libre.json" };

    /// <summary>
    /// Les fiches presentes, triees. Les listes en sont ecartees : voir
    /// <see cref="NotShips"/>.
    /// </summary>
    public static List<string> Discover()
    {
        var found = new List<string>();
        if (!System.IO.Directory.Exists(Folder))
        {
            GD.PushWarning($"dossier des fiches introuvable : {Folder}");
            return found;
        }
        foreach (string f in System.IO.Directory.GetFiles(Folder, "*.json"))
            if (Array.IndexOf(NotShips, System.IO.Path.GetFileName(f)) < 0)
                found.Add(f);
        found.Sort();
        return found;
    }

    /// <summary>
    /// Charge une fiche. Une fiche illisible n'interrompt jamais rien -- meme
    /// contrat que le modele .glb manquant cote JavaScript, ou la coque
    /// procedurale est conservee avec un avertissement.
    /// </summary>
    public static ShipSpec? Load(string path)
    {
        try
        {
            return ShipSpec.FromJson(System.IO.File.ReadAllText(path));
        }
        catch (System.Exception e)
        {
            GD.PushWarning($"fiche illisible {System.IO.Path.GetFileName(path)} : {e.Message}");
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
    public static List<FreeShip> FreeRoster(List<string> paths)
    {
        var listing = new List<FreeShip>();
        string fichier = System.IO.Path.Combine(Folder, "libre.json");
        var demandes = new List<(string ship, string label, int year, string image, string locked)>();
        if (System.IO.File.Exists(fichier))
        {
            try
            {
                using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(fichier));
                if (doc.RootElement.TryGetProperty("ships", out var arr) && arr.ValueKind == JsonValueKind.Array)
                    foreach (var e in arr.EnumerateArray())
                        demandes.Add((Txt(e, "ship"), Txt(e, "label"),
                                      e.TryGetProperty("year", out var y) && y.TryGetInt32(out int yy) ? yy : 0,
                                      Txt(e, "image"), Txt(e, "locked")));
            }
            catch (Exception ex) { GD.PushWarning($"libre.json illisible ({ex.Message}) : tout le dossier est offert"); }
        }
        if (demandes.Count == 0)
            foreach (var q in paths)
                demandes.Add((System.IO.Path.GetFileNameWithoutExtension(q), "", 0, "", ""));

        foreach (var d in demandes)
        {
            int k = paths.FindIndex(q => System.IO.Path.GetFileNameWithoutExtension(q) == d.ship);
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
                Tonnes = spec.Tonnes, Image = img, Locked = d.locked
            });
        }
        return listing;
    }

    static string Txt(JsonElement e, string k)
        => e.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
}
