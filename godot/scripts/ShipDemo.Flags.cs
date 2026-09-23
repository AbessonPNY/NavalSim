using Godot;
using System;
using NavalSim.Core;

namespace NavalSim;

/// <summary>
/// LES PAVILLONS DU MONDE — ships/textures/flags/flags.json, lu par la page aussi.
/// Le vôtre se choisit dans les options ; les autres voiles en arborent un tiré
/// au poids, et c'est lui qui dit qui elles sont.
/// </summary>
public partial class ShipDemo
{
    Nations _nations = new();
    readonly Random _flagRng = new();

    void LoadNations()
    {
        string path = System.IO.Path.GetFullPath(System.IO.Path.Combine(
            ProjectSettings.GlobalizePath("res://"), "..", "ships", "textures", "flags", "flags.json"));
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(System.IO.File.ReadAllText(path));
            _nations = Nations.FromJson(doc.RootElement);
        }
        catch (Exception ex) { GD.PushWarning($"flags.json illisible ({ex.Message}) : les navires gardent les couleurs de leur fiche"); }
    }

    /* VOTRE PAVILLON, hissé sur le navire à la barre — à la mise en service et à
       chaque changement, puisque les couleurs sont celles de qui commande. Vide :
       celles de la fiche. */
    void HoistNation()
    {
        if (_ship == null || !_ship.HasFlag) return;
        var p = _nations.All.Find(x => x.Id == _settings?.Nation);
        if (p != null) _ship.SetEnsign(p.Image, p);
        else _ship.ResetEnsign();
        _ship.ShowColours(true);
    }

    /* LES COULEURS D'UNE AUTRE VOILE : un pavillon national tiré au poids. Le
       pirate garde le sien — il paraît ici sous le noir, sans ruse d'emprunt. */
    void Colours(ShipNode s)
    {
        /* LE PAVILLON D'ABORD, L'ÉTOFFE ENSUITE — la nation se pose même sur une
           coque qui n'a nulle part où la hisser. Sans cela, une Vedette sans tête
           de mât croisée en mer n'était d'aucun pays : la vigie l'annonçait comme
           « un marchand » faute de savoir le dire, et un camp ne l'aurait pas
           reconnue pour sienne. */
        if (IsJolly(s)) return;
        if (_nations.Draw(_flagRng.NextDouble) is { } n) s.SetEnsign(n.Image, n);
    }
}
