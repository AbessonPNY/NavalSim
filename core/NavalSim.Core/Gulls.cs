using System;

namespace NavalSim.Core;

/// <summary>
/// LES MOUETTES AU-DESSUS DES ÎLES — la règle, portée de <c>js/gulls.js</c>.
///
/// Un oiseau est la chose la moins chère au monde qui dise « il y a de la terre
/// ici ». Ce qui se reconnaît n'est pas l'animal mais le MOUVEMENT : un cercle
/// lent au-dessus du rivage, des ailes qui battent par salves puis s'ouvrent
/// pour planer. Tout tient donc dans de l'arithmétique — où chacune en est de
/// son cercle, de combien elle s'incline dedans, et si elle bat ou plane.
///
/// Ce qui est ICI est la règle du VOL COLLECTIF : d'où le bandeau se pose, et
/// quand les suiveuses rentrent. Le reste — la position de chaque oiseau sur son
/// cercle — appartient au shader, qui le refait à chaque image sans que personne
/// ne le calcule (voir gull.gdshader).
///
/// LE PERCHOIR EST LE POINT DE RIVAGE LE PLUS PROCHE, trouvé une fois et gardé
/// tant qu'on reste à portée : une côte n'est pas une île avec un milieu, et un
/// perchoir qui suivrait le navire le long de la plage traînerait les oiseaux
/// derrière lui.
/// </summary>
public sealed class GullRules
{
    /// <summary>Les peupler.</summary>
    public bool Enabled = true;
    /// <summary>Combien d'oiseaux en tout.</summary>
    public int Count = 12;
    /// <summary>Celles qui viennent au navire ; les autres restent sur leur île.</summary>
    public int Followers = 4;
    /// <summary>L'envergure, en mètres : une grande mouette.</summary>
    public double Span = 2.2;
    /// <summary>Au-delà de cette distance au rivage, plus d'oiseaux du tout.</summary>
    public double ShoreRange = 2000;
    /// <summary>Le rayon sur lequel elles tournent autour de leur perchoir.</summary>
    public double Reach = 700;
}

/// <summary>Où le bandeau tourne, à cette image.</summary>
public sealed class Gulls
{
    public readonly GullRules K;
    public Gulls(GullRules? k = null) { K = k ?? new GullRules(); }

    /// <summary>0 : autour du navire · 1 : rentrées sur leur perchoir.</summary>
    public double Home { get; private set; } = 1;
    /// <summary>Le perchoir, en mètres VRAIS. Nul tant qu'aucune côte n'est à portée.</summary>
    public (double X, double Z)? Roost { get; private set; }
    /// <summary>Y a-t-il des oiseaux à dessiner ?</summary>
    public bool Flying { get; private set; }
    /// <summary>Le centre du cercle des suiveuses, en mètres VRAIS.</summary>
    public (double X, double Z) Centre { get; private set; }

    /// <summary>
    /// Une image. <paramref name="shore"/> est la distance du navire au rivage,
    /// <paramref name="nearest"/> ce que le monde rend comme point de côte le
    /// plus proche — demandé seulement quand il faut, car il coûte une descente
    /// de gradient.
    /// </summary>
    public void Step(double dt, double sx, double sz, double shore, Func<(double X, double Z)> nearest)
    {
        if (!K.Enabled) { Flying = false; return; }

        /* PLUS RIEN AU LARGE. Une mouette dessinée à trois kilomètres est un
           pixel de bruit dans la brume ; la marge de 600 m laisse aux suiveuses
           le temps de rentrer avant qu'on ne les efface. */
        if (shore > K.ShoreRange + 600) { Flying = false; Roost = null; Home = 1; return; }

        if (Roost is not { } r
            || Math.Sqrt((sx - r.X) * (sx - r.X) + (sz - r.Z) * (sz - r.Z)) > K.ShoreRange + 1500)
            Roost = nearest();
        r = Roost.Value;
        Flying = true;

        /* PASSÉ DEUX KILOMÈTRES, LES SUIVEUSES TOURNENT POUR RENTRER : le centre
           de leur cercle glisse du navire vers l'île, à l'allure d'un oiseau et
           non d'un coup. En deçà, elles ressortent. La bande morte entre 80 % et
           100 % de la portée évite qu'elles fassent demi-tour à chaque vague. */
        double want = shore > K.ShoreRange ? 1 : shore < K.ShoreRange * 0.8 ? 0 : Home;
        Home += Math.Clamp(want - Home, -dt / 25, dt / 25);
        double hk = Home * Home * (3 - 2 * Home);
        Centre = (sx + (r.X - sx) * hk, sz + (r.Z - sz) * hk);
    }
}
