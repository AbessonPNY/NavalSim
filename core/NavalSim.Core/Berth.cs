using System;

namespace NavalSim.Core;

/// <summary>
/// LE POSTE D'AMARRAGE — où une coque se range le long du ponton de son port,
/// et dans quel sens.
///
/// Les cotes du ponton vivent ICI et non dans ce qui le dessine : le poste et
/// l'ouvrage doivent s'accorder, et deux constantes écrites à deux endroits
/// finissent par se contredire. Une définition, plusieurs usagers.
///
/// Les nombres viennent de la page, où chacun a été trouvé à l'essai :
/// quatre mètres et demi de dégagement parce qu'un mètre quatre ne suffit pas
/// quand la coque évite sur ses bouts dans une baie ouverte — elle venait
/// frotter le bordage ; et RENTRÉE sous le musoir plutôt qu'à cheval dessus,
/// sans quoi son amarre de proue n'a plus de bitte devant elle et se retrouve
/// élongée vers l'arrière, ce qui est du mou et non une amarre.
/// </summary>
public static class Berth
{
    /// <summary>Le tablier du ponton, en travers.</summary>
    public const double Width = 7.0;
    /// <summary>Sa hauteur au-dessus du niveau moyen de la mer.</summary>
    public const double DeckY = 1.7;

    /// <summary>
    /// Le poste, en mètres MONDE VRAIS, et le cap à y prendre : l'étrave au
    /// large, le long du ponton, pour qu'un navire qu'on prend en main puisse
    /// faire servir sans commencer par un demi-tour dans un cul-de-sac.
    /// </summary>
    public static (double X, double Z, double Heading) At(Isle home, double shipL, double shipB)
    {
        var p = home.Port;
        double ca = Math.Cos(p.Ang), sa = Math.Sin(p.Ang);
        // demi-ponton, demi-bau, et le dégagement qui fait le travail de l'abri
        double off = Width * 0.5 + shipB * 0.5 + 4.5;
        /* Bornée à la moitié de la longueur du ponton : sans quoi une grande
           coque irait chercher le fond en remontant vers la plage. */
        double rentre = Math.Min(shipL * 0.45, p.Reach * 0.5);
        double R = p.ShoreR + p.Reach - rentre;
        return (home.X + ca * R - sa * off,
                home.Z + sa * R + ca * off,
                Math.Atan2(ca, sa));
    }
}
