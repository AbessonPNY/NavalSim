using System;

namespace NavalSim.Core;

/// <summary>
/// LA NORMALE D'UN ANNEAU, LUE SUR SES SOMMETS.
///
/// Ici plutot que dans le noeud de rendu parce que c'est une fonction PURE, et
/// que les deux moteurs doivent en donner le meme chiffre : la jumelle est
/// <c>Naval.ringAxis</c> dans js/ship-model.js. Une definition, plusieurs
/// usagers — et, ici, une definition qu'un banc peut eprouver sans moteur
/// (<c>dotnet run --project core/NavalSim.Lab -- anneaux</c>).
///
/// Ce qui la consomme est dans godot/scripts/ShipNode.Rings.cs, ou l'on explique
/// aussi pourquoi un anneau tourne par transformee et jamais par cuisson.
/// </summary>
public static class Rings
{
    /// <summary>
    /// La direction dans laquelle un nuage de points est MINCE, prise sur sa
    /// covariance (deja centree et divisee par le nombre de points).
    ///
    /// La boite englobante ne sert a rien ici : elle est alignee sur les axes, et
    /// un anneau incline n'est mince selon aucun d'eux. Ce qui marche a toute
    /// inclinaison est la direction de moindre VARIANCE — sur un tore de rayon R
    /// et de tube r, les sommets s'ecartent de R dans le plan et de r seulement
    /// en travers, et r est petit devant R par definition d'un anneau.
    ///
    /// Le plus petit vecteur propre de C est le plus GRAND de <c>tr(C).I - C</c>,
    /// qui a les memes vecteurs propres dans l'ordre inverse — et un plus grand
    /// vecteur propre se prend par iteration de puissance, en vingt lignes et
    /// sans solveur. C'est meme le cas facile : les deux valeurs propres du plan
    /// sont EGALES sur un anneau rond, donc degenerees, mais celle qu'on cherche
    /// est justement l'isolee, la seule que l'iteration puisse atteindre.
    /// Plusieurs departs, parce qu'un seul pourrait se trouver orthogonal a la
    /// reponse.
    ///
    /// Un nuage SANS plan — une sphere, un cube — rend un vecteur unitaire
    /// quelconque plutot qu'une erreur : l'anneau tourne alors autour de quelque
    /// chose, ce qui est moins grave qu'un navire qui ne se charge pas.
    /// </summary>
    public static Vec3d Axis(double xx, double xy, double xz, double yy, double yz, double zz)
    {
        double tr = xx + yy + zz;
        double mxx = tr - xx, myy = tr - yy, mzz = tr - zz, mxy = -xy, mxz = -xz, myz = -yz;

        Vec3d best = new(0, 1, 0);
        double bestQ = double.NegativeInfinity;
        Span<(double X, double Y, double Z)> seeds = stackalloc (double, double, double)[]
            { (1, 0, 0), (0, 1, 0), (0, 0, 1), (0.577, 0.577, 0.577) };
        foreach (var seed in seeds)
        {
            double ux = seed.X, uy = seed.Y, uz = seed.Z;
            for (int it = 0; it < 96; it++)
            {
                double wx = mxx * ux + mxy * uy + mxz * uz;
                double wy = mxy * ux + myy * uy + myz * uz;
                double wz = mxz * ux + myz * uy + mzz * uz;
                double len = Math.Sqrt(wx * wx + wy * wy + wz * wz);
                if (len < 1e-12) break;
                ux = wx / len; uy = wy / len; uz = wz / len;
            }
            double q = ux * (mxx * ux + mxy * uy + mxz * uz)
                     + uy * (mxy * ux + myy * uy + myz * uz)
                     + uz * (mxz * ux + myz * uy + mzz * uz);
            if (q > bestQ) { bestQ = q; best = new Vec3d(ux, uy, uz); }
        }

        /* LE SIGNE EST ARBITRAIRE — un vecteur propre vaut au signe pres — mais il
           decide du SENS de rotation, qui doit etre le meme d'une partie a
           l'autre. On le fixe sur la plus grande composante. */
        double ax = Math.Abs(best.X), ay = Math.Abs(best.Y), az = Math.Abs(best.Z);
        double dom = ax >= ay && ax >= az ? best.X : ay >= az ? best.Y : best.Z;
        if (dom < 0) best = new Vec3d(-best.X, -best.Y, -best.Z);
        double n = Math.Sqrt(best.X * best.X + best.Y * best.Y + best.Z * best.Z);
        return n > 1e-12 ? new Vec3d(best.X / n, best.Y / n, best.Z / n) : new Vec3d(0, 1, 0);
    }
}
