using Godot;
using System;

namespace NavalSim;

/// <summary>
/// LA TOILE PAR PALIERS (⇧V) — un navire ne réduisait pas en ferlant tout : il
/// prenait des ris. Quatre crans, dans l'ordre où on les prenait, et la touche
/// les descend l'un après l'autre avant de repartir à toute la toile.
///
/// Rien ici ne calcule : le palier n'est qu'une valeur écrite dans la commande
/// (Controls.Canvas), et le solveur monte ou descend sa toile à sa vitesse.
/// Moins de toile, c'est aussi moins de gîte et moins de casse — le déchirement
/// et la fatigue des mâts se comptent déjà sur ce qui est établi.
/// </summary>
public partial class ShipDemo
{
    static readonly (double Canvas, string Name)[] Reefs =
    {
        (1.00, "toute la toile"),
        (0.60, "huniers"),
        (0.35, "huniers au bas ris"),
        (0.00, "tout est serré")
    };

    int _reef;

    void ReefStep()
    {
        _reef = (_reef + 1) % Reefs.Length;
        var r = Reefs[_reef];
        _ship.Ctrl.Canvas = Math.Max(r.Canvas, 0.01);   // la fraction ordonnée ; serré passe par SailsSet
        _ship.Ctrl.SailsSet = r.Canvas > 0;
        Say(r.Canvas > 0 ? "On réduit : " + r.Name : "Serrez tout !");
    }

    /// <summary>Le palier où l'on est, pour l'écran ; « serré » quand la toile est rentrée.</summary>
    string ReefName() => !_ship.Ctrl.SailsSet ? "serré" : Reefs[_reef].Name;
}
