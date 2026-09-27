using Godot;
using System;
using NavalSim.Core;

namespace NavalSim;

/// <summary>
/// COUVRIR LES FEUX (⇧L).
///
/// Un navire qui ne veut pas être vu la nuit n'a qu'une chose à faire : éteindre.
/// Ce n'est pas un interrupteur — c'est un HOMME qui part de l'arrière et remonte
/// vers l'avant, et chaque flamme meurt en moins d'une seconde une fois qu'il y
/// est. Le décalage est sa marche, la progression la flamme qui tombe ; la
/// tournée entière prend quelques secondes, et l'on peut la regarder venir.
///
/// Le détail vit dans <see cref="ShipNode.Douse"/>, qui range ses fanaux de
/// l'arrière vers l'avant et finit par les fenêtres de la chambre — celles qu'un
/// guetteur voit le plus longtemps.
/// </summary>
public partial class ShipDemo : Node3D
{
    /// <summary>
    /// ⇧L — UN SEUL ORDRE, ET IL EST TOUJOURS L'INVERSE DE CE QU'ON VOIT.
    ///
    /// La touche ne couvrait que les feux, donc elle ne servait que la nuit : de
    /// jour, « rallumez » ne rallumait rien, puisque c'est l'heure qui décidait
    /// seule. Or on allume en plein jour, et pour une bonne raison — par gros
    /// temps, dans un grain ou dans la brume, un navire montre ses feux pour ne
    /// pas se faire aborder (demandé).
    ///
    /// On demande donc le contraire de ce qui brûle, et le bord se débrouille :
    /// découvrir s'il n'y avait qu'un voile, battre le briquet s'il faisait
    /// jour. Le joueur n'a qu'un ordre à connaître, et il vaut à toute heure.
    /// </summary>
    void Douse()
    {
        bool montre = _ship.LanternsShowing;
        _ship.OrderLanterns(!montre, _t, _sky.Core.Night);
        bool nuit = _sky.Core.Night > 0.15;
        /* CE QU'ON EN DIT : allumer de jour est une manœuvre de MAUVAIS TEMPS, et
           le dire est la seule façon d'apprendre au joueur à quoi elle sert. */
        Say(montre
            ? (nuit ? "Couvrez les feux !" : "On souffle les fanaux")
            : (nuit ? "Rallumez les feux !" : "Allumez les feux — qu'on nous voie !"));
        JournalLog(montre ? "Feux couverts." : "Feux allumés.");
    }

    /// <summary>
    /// LA CHAMBRE SEULE (⇧C) — le capitaine se couche. Sa bougie s'éteint, ses
    /// fenêtres de poupe s'éteignent avec, et le fanal reste allumé : un navire
    /// qui fait route porte ses feux, et ce n'est pas parce qu'on dort qu'on
    /// devient invisible.
    ///
    /// C'est aussi la SONDE d'une question qu'on ne pouvait pas trancher à l'œil :
    /// la chambre reste-t-elle claire quand ce qui l'éclaire est mort ? Un fanal
    /// est une lumière OMNIDIRECTIONNELLE, et il traverse le bordé dès que les
    /// ombres portées sont coupées (Réglages → Ombres des fanaux). Éteindre la
    /// chambre seule répond : si elle reste claire, la lumière vient du dehors.
    /// </summary>
    void DouseCabin()
    {
        bool dark = !_ship.CabinDark;
        _ship.DouseCabin(dark, _t);
        Say(dark ? "Le capitaine souffle sa bougie" : "On rallume dans la chambre");
    }
}
