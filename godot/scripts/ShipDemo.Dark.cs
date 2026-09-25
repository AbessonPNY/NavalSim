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
    void Douse()
    {
        bool dark = !_ship.Dark;
        _ship.Douse(dark, _t);
        /* CE QU'ON EN DIT dépend de l'heure : couvrir les feux en plein jour est
           une manœuvre sans objet, et le dire vaut mieux que de laisser croire
           qu'il ne s'est rien passé. */
        bool nuit = _sky.Core.Night > 0.15;
        Say(dark
            ? (nuit ? "Couvrez les feux !" : "Feux couverts — il fait jour")
            : "Rallumez les feux !");
        if (nuit) JournalLog(dark ? "Feux couverts." : "Feux rallumés.");
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
