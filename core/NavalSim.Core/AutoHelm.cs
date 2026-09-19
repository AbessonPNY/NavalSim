namespace NavalSim.Core;

/// <summary>
/// UN TIMONIER QUI N'EST PAS VOUS — helm.js, porté ligne à ligne.
///
/// Il écrit dans les MÊMES commandes qu'une main à la barre : un gouvernail de −1
/// à 1, une écoute, la toile établie ou ferlée. Rien ici ne touche au solveur, et
/// c'est tout l'intérêt — une barre automatique qui pousserait la coque tricherait,
/// et cesserait de prouver que le navire se manœuvre. S'il ne remonte pas au vent,
/// vous non plus.
///
/// Trois métiers, par ordre d'importance : il ne va pas où il pointe si c'est dans
/// le vent — une chasse plein vent debout n'est pas une route mais des BORDÉES ; il
/// ne vire pas non plus par le court, rien dans ce modèle ne passant vent devant :
/// il VIRE LOF POUR LOF ; et les écoutes suivent l'optimum que le solveur calcule
/// déjà pour la marque verte de la console. Une définition, deux usagers.
/// </summary>
public sealed class AutoHelm
{
    readonly ShipPhysics _ph;

    /* LE PRÈS, et c'est un angle MESURÉ plutôt qu'un souvenir : là où son
       gain au vent plafonne, pris sur sa ROUTE et non sur son cap, parce qu'elle
       dérive beaucoup. Cinquante degrés pour la voilure carrée, quarante-sept pour
       l'aurique : ni l'un ni l'autre n'est où un tel gréement tenait vraiment, mais
       la barre mène le navire qu'elle a. */
    public double CloseHauled;
    /// <summary>À quelle distance elle compte venir : visée sur un cercle autour de la cible, pas au centre.</summary>
    public double Standoff;
    /// <summary>Sans toile (la barge), elle va à la machine, droit sur la marque.</summary>
    public readonly bool UnderPower;
    /// <summary>La plus courte bordée qu'elle tiendra : changer de bord coûte près de dix minutes.</summary>
    public double MinLeg = 300;

    public int BeatSide = 1;       // le bord où elle est quand elle remonte au vent
    double _iErr, _legT;
    int _wearDir;
    /// <summary>La marque, dans le même repère local que le corps. Nulle : barre à zéro.</summary>
    public Vec3d? Target;
    public bool Beating { get; private set; }
    public bool Wearing { get; private set; }

    public AutoHelm(ShipPhysics physics, double? closeHauled = null, double? standoff = null)
    {
        _ph = physics;
        CloseHauled = closeHauled ?? (physics.Spec.Rig.Type == "square" ? 0.873 : 0.82);
        Standoff = standoff ?? physics.Spec.L * 1.6;
        var type = physics.Spec.Rig.Type;
        UnderPower = string.IsNullOrEmpty(type) || type == "none" || !(physics.Spec.SailArea > 0);
    }

    static double Wrap(double a) => Math.Atan2(Math.Sin(a), Math.Cos(a));

    /// <summary>
    /// Une image. Les commandes sont DONNÉES à chaque appel, jamais retenues : une
    /// référence prise à la construction se périme dès qu'on change de navire, et la
    /// barre du navire quitté écrivait alors dans VOTRE gouvernail.
    /// </summary>
    public void Update(double dt, Ocean ocean, Controls c)
    {
        var b = _ph.Body;
        if (Target is not Vec3d target || _ph.Foundered) { c.Rudder = 0; return; }

        Vec3d fwd = b.Quat.Rotate(new Vec3d(0, 0, 1));
        double heading = Math.Atan2(-fwd.X, fwd.Z);
        double tx = target.X - b.Pos.X, tz = target.Z - b.Pos.Z;
        double range = Math.Sqrt(tx * tx + tz * tz);
        double bearing = Math.Atan2(-tx, tz);

        /* Viser le BORD de sa garde plutôt que le navire : une fois dedans, elle
           suit la tangente, ce qui change une route de collision en un cercle — et un
           navire qui tourne autour de sa proie à deux encablures menace bien plus
           qu'un navire qui lui rentre dans l'arrière. */
        if (range < Standoff * 2.2)
        {
            double t = Math.Min(1, Standoff / Math.Max(range, 1));
            bearing += Math.Asin(Math.Max(-1, Math.Min(1, t))) * 0.9;
        }

        double windFrom = ocean.WindDeg * Math.PI / 180;
        double off = Wrap(bearing - windFrom);             // 0 = plein vent debout

        double want;
        if (UnderPower)
        {
            want = bearing;                                // une machine se moque du vent
            Beating = false;
        }
        else if (Math.Abs(off) < CloseHauled)
        {
            /* Au près, ou presque : elle ne peut y aller, donc elle va aussi près
               qu'elle peut sur un bord ou l'autre. Un choix AVEC MÉMOIRE — repris à
               chaque image du côté où la marque se trouve, une marque au vent passe
               d'une joue à l'autre et elle reste dans le lit du vent. Elle change de
               bord SUR LA LAYLINE : quand la marque relève à son angle de près de
               l'AUTRE côté, premier instant où l'autre bord la rallie. */
            if (off * BeatSide < -(CloseHauled - 0.10) && _legT > MinLeg)
            {
                BeatSide = -BeatSide;
                _legT = 0;
                _iErr = 0;          // la barre tenue la retenait sur le bord qu'elle quitte
            }
            want = windFrom + BeatSide * CloseHauled;
            Beating = true;
            // une bordée est du temps passé à la TENIR ; tourner ne compte pas
            if (!Wearing) _legT += dt;
        }
        else
        {
            want = bearing;
            Beating = false;
        }

        double err = Wrap(want - heading);
        double rate = b.AngVel.Y;

        /* ELLE VIRE LOF POUR LOF, ELLE NE VIRE PAS VENT DEVANT. Passer par le lit du
           vent, c'est n'avoir plus aucune poussée ; elle perd son erre, et
           l'autorité du gouvernail va comme le CARRÉ de sa vitesse : elle manque à
           virer, chaque fois — goélette comme pirate, mesuré dans la page. Elle fait
           donc le tour par l'AUTRE côté, en s'éloignant du vent, trois fois plus
           loin mais toile pleine tout du long. Et cela SE VERROUILLE : abattre la rend
           rapide, ce qui défait la condition qui l'a déclenché. */
        double eUse = err;
        double toWind = Wrap(windFrom - heading);
        bool crosses = toWind * err > 0 && Math.Abs(toWind) < Math.Abs(err);
        if (_wearDir != 0)
        {
            if (Math.Abs(err) < 0.25) _wearDir = 0;
        }
        else if (!UnderPower && crosses && Math.Abs(err) > 0.35) _wearDir = err > 0 ? -1 : 1;
        // le long du tour : même destination, l'autre main
        if (_wearDir != 0 && _wearDir * err < 0) eUse = err - Math.Sign(err) * 6.2832;
        Wearing = _wearDir != 0;

        /* Le gouvernail : proportionnel à l'erreur, amorti par la vitesse à laquelle
           elle tourne déjà — sans quoi elle chasse. Hors du près, un terme tenu pour
           les quelques rayons qu'un cap réclame d'une coque jamais tout à fait
           équilibrée ; borné, et nul au près. */
        if (Beating) _iErr = 0;
        else _iErr = Math.Max(-0.55, Math.Min(0.55, _iErr + err * dt * 0.40));
        c.Rudder = Math.Max(-1, Math.Min(1, eUse * 1.9 + _iErr - rate * 2.6));

        /* Les écoutes sur la marque que le solveur trace déjà pour la console. Rien
           ne FASEYE : ôter la poussée pour laisser le gouvernail seul est ce que
           ferait un marin coincé face au vent, et c'est exactement faux ici — ne
           pas pousser est ce qui l'y garde. Virer lof pour lof est la réponse. */
        if (_ph.OptSheet is double opt && !UnderPower)
            c.Sheet += (opt - c.Sheet) * Math.Min(1, dt * 0.9);
        c.SailsSet = true;
        c.Throttle = UnderPower ? 1 : 0;       // la toile si elle en a, la machine sinon
    }
}
