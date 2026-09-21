using Godot;
using System;
using NavalSim.Core;

namespace NavalSim;

/// <summary>
/// CE QU'IL Y A DANS LA BOUTEILLE.
///
/// Le module des débris ne fait que flotter, dériver et dire quand le navire est
/// venu assez près pour en prendre une à bord ; ce qu'elle contient regarde le
/// jeu, et c'est ici. Trois tirages égaux, comme dans la page : une page de
/// journal de bord — le dernier geste de quelqu'un ; une page de carnet de
/// comptes — le cours d'un port, daté ; ou UNE CARTE — une cargaison échouée sur
/// les hauts-fonds d'une île, portée d'une croix sur la carte du capitaine.
///
/// LA CROIX VIT DANS LE CARNET, pas dans les débris : c'est un lieu qu'on a
/// APPRIS et non qu'on a vu, donc il survit à la fermeture du jeu, et la
/// cargaison est replantée à l'ouverture d'après la croix elle-même. Le carnet
/// est la seule mémoire, et il n'y en a pas deux à tenir d'accord.
/// </summary>
public partial class ShipDemo : Node3D
{
    readonly RandomNumberGenerator _bottleRng = new();

    /* Cinq pages de journal, celles de la page : ce qu'on écrit quand on sait
       que c'est la dernière chose qu'on écrira. */
    static readonly (string A, string B)[] Journal =
    {
        ("Vent tombé au matin, la mer reste grosse.", "L'équipage murmure, l'eau monte à la cale."),
        ("Aperçu une voile noire au vent à nous.", "Nous forçons de toile, mais elle gagne."),
        ("Deux voies d'eau à l'avant, les pompes ne suffisent plus.", "Que Dieu garde ceux qui liront ceci."),
        ("Le second est mort ce midi d'un éclat de chêne.", "Nous tenons encore le cap sur le port."),
        ("La poudre est mouillée, les pièces se taisent.", "Je jette ce journal à la mer avant la fin.")
    };

    static readonly string[] Aires =
    {
        "au nord", "au nord-est", "à l'est", "au sud-est",
        "au sud", "au sud-ouest", "à l'ouest", "au nord-ouest"
    };

    /* « de Le Carénage » et « à Le Carénage » ne se disent pas : l'article se
       contracte avec la préposition. La règle vit avec les noms qu'elle
       gouverne, comme dans la page. */
    static string ANom(string n) =>
        n.StartsWith("Le ") ? "au " + n.Substring(3)
        : n.StartsWith("Les ") ? "aux " + n.Substring(4)
        : n.StartsWith("La ") ? "à la " + n.Substring(3)
        : "à " + n;

    static string DeNom(string n) =>
        n.StartsWith("Le ") ? "du " + n.Substring(3)
        : n.StartsWith("Les ") ? "des " + n.Substring(4)
        : n.StartsWith("La ") ? "de la " + n.Substring(3)
        // et « de » s élide devant une voyelle : d Old Harbour, d un navire inconnu
        : n.Length > 0 && "AEIOUYÀÂÉÈÊÎÔÛaeiouyàâéèêîôû".IndexOf(n[0]) >= 0 ? "d’" + n
        : "de " + n;

    /// <summary>On la repêche : on l'ouvre.</summary>
    void OpenBottle(string? from, double bx, double bz, double age)
    {
        string nom = from != null && from.Length > 0 ? from : "un navire inconnu";
        double draw = _bottleRng.Randf();

        // une page de journal : une fois sur trois, et toujours si le monde manque
        if (_world == null || _book == null || _chart == null || draw < 1.0 / 3)
        {
            var j = Journal[(int)(_bottleRng.Randf() * Journal.Length) % Journal.Length];
            ShowEncart("Bouteille à la mer", $"Journal de bord {DeNom(nom)} — « {j.A} {j.B} »");
            return;
        }

        /* UNE PAGE DE CARNET DE COMPTES, une fois sur trois : ce qu'on payait
           l'épice au port le plus proche, le jour où la bouteille a été jetée.
           Un renseignement comme ceux du comptoir — exact, et DATÉ de l'âge de
           la bouteille, parce qu'un chiffre sans son âge est un mensonge. */
        if (draw < 2.0 / 3)
        {
            Isle? near = null;
            double best = double.MaxValue;
            foreach (var i in _world.Isles)
            {
                double d = (i.X - bx) * (i.X - bx) + (i.Z - bz) * (i.Z - bz);
                if (d < best) { best = d; near = i; }
            }
            double tw = _sea.Core.Time - age;
            ShowEncart("Bouteille à la mer", FormattableString.Invariant(
                $"Carnet {DeNom(nom)} : {ANom(near!.Name)}, l'épice se payait {_market.BuyPrice(near.Key, tw):F0} la tonne ")
                + FormattableString.Invariant($"et se revendait {_market.SellPrice(near.Key, tw):F0} — relevé {Market.Age(age)}."));
            return;
        }

        /* LA CARTE. On essaie les ports dans un ordre quelconque : le premier qui
           a une batture prend la cargaison. Une mer dont aucune côte n'offrirait
           un mètre d'eau est possible en théorie — alors la carte est délavée, ce
           qui est une fin honnête et non une panne. */
        var isles = new System.Collections.Generic.List<Isle>(_world.Isles);
        for (int i = isles.Count - 1; i > 0; i--)
        {
            int j2 = (int)(_bottleRng.Randf() * (i + 1)) % (i + 1);
            (isles[i], isles[j2]) = (isles[j2], isles[i]);
        }
        foreach (var isl in isles)
        {
            string key = $"{isl.Key}-{_book.Crosses.Count}-{(int)(_bottleRng.Randf() * 100000)}";
            if (_flotsam.Strand(isl, key) is not { } c) continue;

            // le relèvement depuis le port, dit en mots : l'est est le −x
            double deg = (Math.Atan2(-(c.X - isl.X), c.Z - isl.Z) * 180 / Math.PI + 360) % 360;
            string aire = Aires[(int)Math.Round(deg / 45) % 8];
            string text = $"Une carte griffonnée par l'équipage {DeNom(nom)} : une cargaison échouée "
                        + $"sur les hauts-fonds {aire} {DeNom(isl.Name)}. Portée d'une croix sur votre carte — "
                        + "trop peu d'eau pour un navire, il faudra y aller en chaloupe.";
            _book.Mark(c.X, c.Z, $"cargaison {aire} {DeNom(isl.Name)}", key);
            _chart.Refresh();
            SaveBook();
            ShowEncart("Bouteille à la mer", text, 1);
            return;
        }
        ShowEncart("Bouteille à la mer", "Une carte délavée, illisible.");
    }

    /// <summary>
    /// LA CARGAISON RELEVÉE. La croix est rayée du carnet — ce qu'elle désignait
    /// n'y est plus — et les épices vont AU FOND DE LA CALE, ce qui se voit à son
    /// tirant d'eau : c'est la seule récompense de ce jeu qui change la façon dont
    /// la coque se conduit.
    /// </summary>
    void ClaimCargo(string key)
    {
        _book?.Strike(key);
        _chart?.Refresh();
        SaveBook();
        int t = 3 + (int)(_bottleRng.Randf() * 6);
        // au milieu, au ras des varangues : le fret d'un bord la ferait gîter
        _ship.Physics.LoadCargo(Config.NComp / 2, HoldFloor, 0, t, "epice");
        Say($"Cargaison récupérée : {t} t d'épices portées dans la cale.");
    }

    /// <summary>
    /// Ce que le carnet sait, reposé sur le fond au chargement : une croix qui
    /// désigne une caisse doit trouver la caisse, sinon la carte ment.
    /// </summary>
    void PlantCrosses()
    {
        if (_book == null || _world == null) return;
        foreach (var c in _book.Crosses)
            _flotsam.Plant(c.X, c.Z, _world.HeightAt(c.X, c.Z) + 0.45, c.Key);
        if (_book.Crosses.Count > 0)
            GD.Print($"{_book.Crosses.Count} croix sur la carte, autant de cargaisons posées");
    }
}
