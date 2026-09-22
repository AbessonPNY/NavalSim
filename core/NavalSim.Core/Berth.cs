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
    /// <summary>Ce que la poupe garde entre elle et le rivage, en mètres.</summary>
    public const double SternClear = 8.0;

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
        /* ET SA POUPE RESTE DANS L'EAU. Rentrée de 45 % de sa longueur, une
           coque de trente mètres finissait la poupe sur le sable au Môle de
           Port-Royal, dont le ponton ne fait que vingt-huit mètres (signalé :
           « la Roter Löwe démarre dans le sable ») ; la grande frégate, elle,
           naissait neuf mètres au-dessus de l'eau, en pleine terre. Relevé le
           long de ce ponton : 0 m d'eau au rivage, 6,6 m à six mètres, 11 m
           au-delà de onze. La poupe se tient donc au moins à SternClear du
           rivage — ou à la moitié du ponton s'il est plus court que cela. */
        double rentre = Math.Min(shipL * 0.45, p.Reach * 0.5);
        double keep = Math.Min(SternClear, p.Reach * 0.5) + shipL * 0.5;
        double R = p.ShoreR + Math.Max(p.Reach - rentre, keep);
        return (home.X + ca * R - sa * off,
                home.Z + sa * R + ca * off,
                Math.Atan2(ca, sa));
    }
}
