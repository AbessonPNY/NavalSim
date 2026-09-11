namespace NavalSim.Core;

public sealed partial class ShipPhysics
{
    /* ------------------------------------------------------------------ */
    /*  LE PAS                                                             */
    /* ------------------------------------------------------------------ */

    /// <summary>
    /// Un sous-pas du solveur.
    ///
    /// <paramref name="neighbours"/> est DONNÉ à chaque appel et jamais retenu,
    /// pour la raison qui a déjà coûté un bug ici : une référence gardée se
    /// périme dès que la flotte change. Nul ou vide, elle ne heurte personne.
    /// </summary>
    public void Step(double dt, Ocean ocean, Controls ctrl, double t,
                     IReadOnlyList<ShipPhysics>? neighbours = null)
    {
        var b = Body;
        var S = Spec;

        /* TRIBORD EST LE −X LOCAL, ET CE N'EST PAS UN CHOIX.
           three.js est direct, donc avec l'étrave sur +z et le mât sur +y le
           vecteur vers la main droite de l'homme de barre est étrave × haut
           = ẑ × ŷ = −x̂. Écrit +x — comme il l'était — chaque usage de cet axe
           voulait dire l'inverse de son nom : barre à tribord elle abattait sur
           bâbord, et la batterie marquée « Tribord » crachait par l'autre
           muraille. Le gouvernail est revenu juste tout seul avec le signe,
           ayant toujours été correct RELATIVEMENT à ce vecteur.

           Les trois autres usagers projettent puis reconstruisent sur cet axe
           (la dérive, la résistance latérale, le relèvement de la gerbe), donc
           ils sont invariants au retournement ; les voiles y lisent `tack`, qui
           désigne enfin l'amure qu'il nomme. */
        Vec3d fwd = b.Quat.Rotate(new Vec3d(0, 0, 1));
        Vec3d right = b.Quat.Rotate(new Vec3d(-1, 0, 0));
        Vec3d up = b.Quat.Rotate(new Vec3d(0, 1, 0));

        // La toile rentre ou sort AVANT que quoi que ce soit demande quelle
        // poussée elle a.
        double wantSet = ctrl.SailsSet ? 1 : 0;
        double stepSet = SetRate * dt;
        SetFrac += Math.Max(-stepSet, Math.Min(stepSet, wantSet - SetFrac));

        // L'eau qui entre et qui sort D'ABORD : elle fixe la masse et le centre
        // de gravité contre lesquels tout ce qui suit — la pesanteur, les
        // moments, l'inertie — sera ensuite pris.
        Flooding(dt, ocean, t);

        Vec3d force = Vec3d.Zero, torque = Vec3d.Zero;
        force.Y -= b.Mass * Config.G;         // la pesanteur au CdG ne fait aucun moment

        // le CdG monde — son PROPRE vecteur, parce qu'un temporaire réutilisé
        // ici a déjà coûté un roulis parasite puis une explosion numérique
        Vec3d cog = b.Quat.Rotate(b.Com) + b.Pos;

        // --- poussée d'Archimède et amortissement vertical, sur les sondes immergées ---
        double submergedVol = 0, lowestY = double.PositiveInfinity;
        double slamW = 0, slamV = 0, slamP = 0;
        Vec3d slamAt = Vec3d.Zero;

        for (int i = 0; i < Probes.Length; i++)
        {
            ref Probe pr = ref Probes[i];
            Vec3d pw = b.Quat.Rotate(pr.Local) + b.Pos;
            if (pw.Y < lowestY) lowestY = pw.Y;
            double depth = ocean.Sample(pw.X, pw.Z, t) - pw.Y;

            /* À quel point cette cellule est pleine, et à quelle vitesse cela
               CHANGE. Le taux est tout le détecteur de gerbe, et c'est une
               question différente de la première posée.

               La première écriture mesurait la coque qui DESCEND, ce qui rate la
               moitié de ce que l'œil voit : une lame qui monte à la rencontre
               d'une étrave immobile jette tout autant d'eau — c'est même la
               définition d'une déferlante. Demander plutôt à quelle vitesse
               chaque cellule passe sous l'eau attrape les deux, et ne demande
               pas lequel des deux a bougé.

               Mieux : cela se localise tout seul. Une cellule déjà profonde ne
               compte pour rien, rien de neuf n'y étant déplacé ; une cellule en
               l'air non plus. Seules comptent celles qui TRAVERSENT la surface,
               c'est-à-dire exactement l'endroit d'où l'eau est jetée. */
            double f = depth > 0 ? Math.Min(1, depth / ProbeH) : 0;
            double df = f - pr.Frac;
            pr.Frac = f;

            if (df > 0 && _slamWarm <= 0)
            {
                double drive = pr.Vol * df / dt;              // m³/s neufs déplacés ici
                slamW += drive;
                /* À quelle vitesse coque et mer se referment, en m/s — MAIS LIRE
                   LE PLAFOND. Une cellule ne peut se remplir que d'une cellule
                   entière en une image, donc cette mesure sature à ProbeH/dt :
                   quelque trente-six mètres par seconde à soixante images, et
                   DAVANTAGE sur une image lente. Ce plafond est une propriété de
                   l'horloge d'affichage et non de la mer, et pris au mot il
                   envoyait l'embrun à seize mètres en l'air par forte houle.

                   Sept mètres par seconde est la borne honnête : c'est à peu
                   près la vitesse orbitale de la mer la plus creuse de ce
                   modèle, et une coque et une lame qui se rencontrent plus fort
                   que cela, c'est l'arithmétique qui manque d'images. */
                double closing = Math.Min(7.0, df * ProbeH / dt);
                if (closing > slamV) slamV = closing;
                /* OÙ cela se passe est pondéré par le CARRÉ de la vitesse de
                   passage, et c'est la différence entre une gerbe à l'étrave et
                   une gerbe nulle part en particulier.

                   Une moyenne simple sur les cellules qui traversent atterrit au
                   maître-bau presque à tous les coups, et non parce que la
                   physique le dit — parce que la coque y est la plus LARGE, donc
                   c'est là qu'il y a le plus de cellules. L'eau est jetée là où
                   le travail est le plus dur, ce qui est un MAXIMUM et non une
                   moyenne. */
                double wpos = drive * closing * closing;
                slamP += wpos;
                slamAt += pw * wpos;
            }

            if (depth <= 0) continue;
            double dispVol = pr.Vol * f;
            submergedVol += dispVol;

            Vec3d r = pw - cog;                                // le bras de levier
            double fb = Config.Rho * Config.G * dispVol;       // Archimède, droit vers le haut

            // la vitesse de CE point = v + ω × r ; on amortit sa composante verticale
            Vec3d vp = b.AngVel.Cross(r) + b.Vel;
            double dragF = -vp.Y * dispVol * S.HeaveDamp;

            double fy = fb + dragF;
            force.Y += fy;
            torque.X += -r.Z * fy;                             // (r × (0,fy,0)).x
            torque.Z += r.X * fy;                              // (r × (0,fy,0)).z
        }

        SubmergedFrac = submergedVol / HullVolume;
        Draft = Math.Max(0, ocean.Sample(cog.X, cog.Z, t) - lowestY);

        SlamRate = slamW; SlamSpeed = slamV;
        if (slamP > 0)
        {
            slamAt = slamAt / slamP;
            /* PORTÉ JUSQU'À LA RALINGUE de sa flottaison. Le centroïde vit dans
               le plan de flottaison — c'est une moyenne de points DANS la coque
               — donc l'embrun né là montait à travers son propre pont et se
               lisait comme de l'eau embarquée plutôt que de l'eau jetée.

               L'écarter d'une distance fixe ne suffit pas, et ce fut la deuxième
               tentative : une coque est LONGUE, donc un point à huit mètres en
               avant du maître-bau, poussé de deux mètres de plus, reste à quatre
               mètres de l'étrave — et l'embrun court le long du pont.

               Il faut le poser SUR son contour. Ramenée à sa demi-longueur et à
               sa demi-largeur, la coque devient un cercle unité : normaliser là
               puis revenir pose la gerbe sur la flottaison, au relèvement d'où le
               coup est venu, quelles que soient ses proportions. Retombant à
               plat il n'y a pas de relèvement — la moyenne est au centre — et
               c'est l'étrave qui est prise, là où une chute à plat jette l'eau
               qu'on remarque. */
            Vec3d outv = slamAt - b.Pos;
            double ex = outv.Dot(right) / (S.B * 0.5);
            double ez = outv.Dot(fwd) / (S.L * 0.5);
            double n = Math.Sqrt(ex * ex + ez * ez);
            if (n < 0.2) { ex = 0; ez = 1.0; }
            else { ex /= n; ez /= n; }
            slamAt = b.Pos + right * (ex * S.B * 0.5 * 1.06) + fwd * (ez * S.L * 0.5 * 1.06);
            slamAt.Y = ocean.Sample(slamAt.X, slamAt.Z, t);
        }
        if (_slamWarm > 0) _slamWarm--;
        _slamCool -= dt;

        /* Cinq secondes de silence ont un défaut qui vaut d'être corrigé : une
           claque tire, et la lame verte qui monte à bord deux secondes plus tard
           — celle qui valait le coup — est jetée parce que la pendule n'avait pas
           fini. Un impact au moins DEUX FOIS plus gros peut donc prendre la
           place, passé une seconde. Il reste rare par construction, doubler étant
           beaucoup. */
        bool big = slamW > _slamLast * 2 && (SlamPause - _slamCool) > 1.0;
        if (OnSlam != null && (_slamCool <= 0 || big)
            && slamW > (HullVolume / S.D) * SlamTrigger)
        {
            _slamCool = SlamPause;
            _slamLast = slamW;
            OnSlam(slamAt, slamW, slamV);
        }

        // de combien elle est sous la mer — la mesure par laquelle elle est perdue
        DepthBelow = ocean.Sample(b.Pos.X, b.Pos.Z, t) - b.Pos.Y;

        /* Ce qui d'elle travaille encore la surface. Le collier, la gerbe
           d'étrave et le sillage appartiennent tous à une coque qui FEND l'eau ;
           une fois dessous ils doivent partir, sans quoi un anneau d'écume reste
           à chevaucher l'épave avec rien dessous. */
        double u = Math.Min(1, Math.Max(0, (SubmergedFrac - 0.78) / (0.95 - 0.78)));
        Afloat = 1 - u * u * (3 - 2 * u);

        /* Sombrée quand elle RESTE dessous, et non à l'instant où une mer
           l'ensevelit : une lame qui monte à bord met le pont sous l'eau une
           seconde par n'importe quel gros temps. */
        _underFor = SubmergedFrac > 0.95 ? _underFor + dt : 0;
        if (_underFor > 3) Foundered = true;

        double inWater = submergedVol > 0 ? 1 : 0;
        double vFwd = b.Vel.Dot(fwd);
        double vRight = b.Vel.Dot(right);

        // --- la machine ---
        force += fwd * (ctrl.Throttle * S.MaxThrust * inWater);

        // --- la résistance de coque : quadratique en route ; la quille lestée
        //     mord fort en travers, ce qui est ce qui borne la dérive ---
        force += fwd * (-Math.Sign(vFwd) * vFwd * vFwd * S.Drag * inWater);
        force += right * ((-vRight * Math.Abs(vRight) * S.LateralQuad
                           - vRight * S.LateralLinear) * inWater);

        /* --- LE GOUVERNAIL ---
           Une force transversale à l'étambot, JAMAIS un couple pur. L'appliquer
           là où le gouvernail se trouve réellement — tout à l'arrière — est ce
           qui place le point de pivot en AVANT du maître-couple, environ au quart
           de sa longueur en arrière de l'étrave, comme un vrai navire faisant
           route avant. Il s'inverse correctement en marche arrière aussi. */
        double delta = ctrl.Rudder * S.RudderMax;
        double rudderF = -S.RudderK * vFwd * Math.Abs(vFwd) * Math.Sin(delta) * inWater;
        Vec3d fVec = right * rudderF;
        force += fVec;
        Vec3d arm = b.Quat.Rotate(new Vec3d(0, S.RudderY, S.RudderZ)) + b.Pos - cog;
        torque += arm.Cross(fVec);

        Sails(ctrl, ocean, cog, ref force, ref torque, fwd, right);
        Ground(dt, ref force, ref torque, cog, ocean);
        Collide(dt, ref force, ref torque, cog, neighbours);
        Moor(ref force, ref torque, cog, ocean);

        // --- intégration linéaire ---
        b.Vel += force * (dt / b.Mass);
        b.Vel *= 1 - 0.02 * dt;                       // un amortissement global très faible
        double vl = b.Vel.Length;
        if (vl > 40) b.Vel = b.Vel * (40 / vl);       // garde-fou
        b.Pos += b.Vel * dt;

        // --- intégration angulaire, dans le repère propre où l'inertie est diagonale ---
        Quatd qc = b.Quat.Inverted();
        Vec3d Tb = qc.Rotate(torque);
        Tb = new Vec3d(Tb.X / b.Ib.X, Tb.Y / b.Ib.Y, Tb.Z / b.Ib.Z);
        Tb = b.Quat.Rotate(Tb);
        b.AngVel += Tb * dt;

        /* Amortissement ANISOTROPE : on tient le roulis et le tangage
           fermement, pour la stabilité, mais on laisse le lacet libre pour que
           le gouvernail puisse réellement la faire tourner. Un amortisseur
           isotrope étrangle l'évolution. */
        double wy = b.AngVel.Dot(up);
        b.AngVel = (b.AngVel - up * wy) * (1 - 3.0 * dt) + up * (wy * (1 - 0.5 * dt));
        double al = b.AngVel.Length;
        if (al > 4) b.AngVel = b.AngVel * (4 / al);

        double wlen = b.AngVel.Length;
        if (wlen > 1e-8)
        {
            Vec3d axis = b.AngVel * (1 / wlen);
            Quatd dq = Quatd.FromAxisAngle(axis, wlen * dt);
            // premultiply : dq · quat, puis renormalisation — Godot VÉRIFIE la
            // norme d'un quaternion là où three.js laissait passer
            b.Quat = (dq * b.Quat).Normalized();
        }

        // Garde-fou NaN : on récupère dans un état droit plutôt que de figer la boucle
        double probe = b.Pos.X + b.Pos.Y + b.Pos.Z + b.Vel.X + b.Vel.Y + b.Vel.Z + b.Quat.X;
        if (double.IsNaN(probe) || double.IsInfinity(probe))
        {
            b.Pos = new Vec3d(double.IsFinite(b.Pos.X) ? b.Pos.X : 0, 0.4,
                              double.IsFinite(b.Pos.Z) ? b.Pos.Z : 0);
            b.Vel = Vec3d.Zero;
            b.AngVel = Vec3d.Zero;
            b.Quat = Quatd.Identity;
        }
    }

    /* ------------------------------------------------------------------ */
    /*  LES VOILES                                                         */
    /* ------------------------------------------------------------------ */

    /// <summary>
    /// L'angle d'incidence qui la pousse le plus fort pour un angle de vent
    /// apparent donné.
    ///
    /// La poussée va comme CL·sin β − CD·cos β : la portance tire en travers du
    /// vent, donc elle aide le plus quand le vent est par le travers, tandis que
    /// la traînée pousse sous le vent et n'aide qu'une fois passé le travers. En
    /// y substituant l'aérofoil ci-dessus et en annulant la dérivée, tout
    /// s'effondre en
    ///
    ///     tan 2α = 2·KL·sin β / (KD·cos β)
    ///
    /// — forme fermée, sans recherche ni table. CD0 en sort, n'étant fonction
    /// d'aucun α. Et cela tombe là où un marin le mettrait : bordé plat au près
    /// (β = 45° → écoutes à 11°) et en croix devant elle (β = 180° → 90°), en
    /// passant par 45° au travers.
    /// </summary>
    public static double OptimalAoA(double beta)
        => 0.5 * Math.Atan2(2 * SailFoil.KL * Math.Sin(beta), SailFoil.KD * Math.Cos(beta));

    /// <summary>
    /// Vent apparent = vent vrai vu d'un pont en mouvement. Les voiles sont
    /// traitées comme des aérofoils : portance en travers du vent apparent,
    /// traînée le long, toutes deux croissant comme le carré de la vitesse
    /// apparente. La force agit au centre de voilure, haut au-dessus de la
    /// flottaison — ce qui est exactement pourquoi elle gîte.
    /// </summary>
    void Sails(Controls ctrl, Ocean ocean, in Vec3d cog,
               ref Vec3d force, ref Vec3d torque, in Vec3d fwd, in Vec3d right)
    {
        var S = Spec; var b = Body;
        SailDrive = 0; Luffing = false; OptSheet = null; SailLoad = 0;

        Vec3d app = ocean.WindVec - b.Vel;
        app.Y = 0;
        double vApp = app.Length;
        AppWindSpeed = vApp;
        if (vApp <= 0.25) return;

        double fromFwd = -(app.X * fwd.X + app.Z * fwd.Z) / vApp;
        double fromRight = -(app.X * right.X + app.Z * right.Z) / vApp;
        double beta = Math.Atan2(Math.Abs(fromRight), fromFwd);   // 0 = vent debout
        AppWindAngle = beta;
        Tack = fromRight >= 0 ? 1 : -1;                           // +1 = vent sur la joue tribord

        /* Où les écoutes devraient être à cette allure, pour le repère de la
           console. Borné à ce que son gréement sait réellement faire : un carré
           ne peut pas brasser aussi loin qu'une bôme d'aurique s'écarte, donc
           vent arrière le repère se pose à sa butée plutôt qu'à un angle qu'elle
           n'atteindra jamais. */
        OptSheet = Math.Max(0, Math.Min(S.MaxSheet, beta - OptimalAoA(beta)));

        double aoa = beta - ctrl.Sheet;
        // plus rien en l'air dont il vaille la peine de parler
        if (SetFrac * Standing * Whole < 0.01) return;
        if (aoa <= 0.02) { Luffing = true; return; }      // trop choqué, ou en panne

        double CL = SailFoil.KL * Math.Sin(2 * aoa);
        double CD = SailFoil.CD0 + SailFoil.KD * Math.Sin(aoa) * Math.Sin(aoa);
        // la surface réellement établie, qui est ce contre quoi le vent pousse
        double q = 0.5 * Config.RhoAir * vApp * vApp * S.SailArea * SetFrac * Standing * Whole;

        Vec3d sailF = app * (CD * q / vApp);              // la traînée, le long du vent
        Vec3d lift = new Vec3d(app.Z, 0, -app.X).Normalized();
        if (lift.Dot(fwd) < 0) lift = -lift;              // la portance la pousse en avant
        sailF += lift * (CL * q);
        force += sailF;

        Vec3d arm = b.Quat.Rotate(_ce) + b.Pos - cog;
        torque += arm.Cross(sailF);
        SailDrive = sailF.Dot(fwd);

        /* La pression sur la toile, qui est ce qui la fait se creuser — par
           unité de toile QU'ELLE A ENCORE.

           La toile partie sort des DEUX côtés du rapport : la force tombe avec
           elle, et la surface qui la porte aussi. Ce qui reste dehors est donc
           sous exactement la même pression qu'avant — ce qui est le fait
           physique, et ce qui fait qu'une voile qui éclate n'en sauve aucune
           autre. */
        /* LE DÉNOMINATEUR EST GARDÉ, ce qu'il n'était pas, et le banc de parité
           l'a trouvé. Un bâtiment SANS VOILURE — le chaland, `sailArea: 0` —
           passe bel et bien par ici : la sortie anticipée teste `setFrac`, qui
           décroît depuis 1 sur trois secondes, donc elle ne mord pas tout de
           suite. Le calcul fait alors 0 / (0 × 1) = NaN.

           Bénin dans les faits — rien ne lit la charge de toile d'un chaland, et
           le modèle n'a aucune voile à creuser — mais un NaN n'est jamais la
           valeur voulue, et il suffit qu'un jour la console l'affiche ou qu'un
           terme le multiplie pour qu'il se répande sans rien dire. Zéro est la
           réponse juste : sans toile, il n'y a aucune pression sur la toile. */
        double canvas = S.SailArea * Math.Max(0.05, Standing * Whole);
        SailLoad = canvas > 1e-9 ? sailF.Length / canvas : 0;
    }

    /* ------------------------------------------------------------------ */
    /*  ELLE TOUCHE LE FOND                                                */
    /* ------------------------------------------------------------------ */

    /// <summary>
    /// L'échouage se sonde en TROIS POINTS, jamais sur les sondes de carène.
    /// <c>HeightAt</c> balaie la grille des îles ; l'appeler trois cents fois par
    /// sous-pas coûterait plus cher que tout le solveur réuni. L'étrave, le milieu
    /// et l'étambot suffisent à tout ce qui compte : elle s'ensable par l'avant
    /// sur une plage en pente douce, pivote sur un haut-fond qui la prend par le
    /// travers, ou s'assoit sur une quille droite.
    ///
    /// Le fond répond comme un ressort raide très amorti, appliqué AU point de
    /// contact — donc elle se soulève, gîte et embarde exactement comme la
    /// géométrie l'impose. Rien ne décide qu'elle est échouée ; les forces le
    /// font, comme rien ne décide qu'elle flotte.
    /// </summary>
    void Ground(double dt, ref Vec3d force, ref Vec3d torque, in Vec3d cog, Ocean ocean)
    {
        Aground = 0;
        if (World == null) return;
        var S = Spec; var b = Body;
        double ox = ocean.Origin.X, oz = ocean.Origin.Z;
        double keel = -(S.Hull.KeelDepth + S.Hull.KeelExtra);
        // elle porte tout son poids à un tiers de mètre de pénétration
        double kSpring = b.Mass * Config.G / (0.33 * 3);

        _hardAgo = Math.Max(0, _hardAgo - dt);
        double spd = Math.Sqrt(b.Vel.X * b.Vel.X + b.Vel.Z * b.Vel.Z);

        ReadOnlySpan<double> stations = stackalloc double[] { 0.42, 0.0, -0.45 };
        for (int s = 0; s < stations.Length; s++)
        {
            double f = stations[s];
            Vec3d pw = b.Quat.Rotate(new Vec3d(0, keel, f * S.L)) + b.Pos;
            double bed = World.HeightAt(ox + pw.X, oz + pw.Z);
            double pen = bed - pw.Y;
            if (pen <= 0) continue;
            Aground = Math.Max(Aground, pen);

            Vec3d r = pw - cog;
            // la vitesse de CE point, pour que l'amortissement combatte le vrai mouvement
            Vec3d vp = b.AngVel.Cross(r) + b.Vel;

            double upF = kSpring * Math.Min(pen, 2.5) - vp.Y * b.Mass * 1.2;
            double fy = Math.Max(0, upF);
            force.Y += fy;
            torque.X += -r.Z * fy;
            torque.Z += r.X * fy;

            // et elle laboure : le sable et la roche tiennent une coque bien
            // plus fort que l'eau ne le fait
            Vec3d fVec = new Vec3d(-vp.X, 0, -vp.Z) * (b.Mass * 0.9);
            force += fVec;
            torque += r.Cross(fVec);

            /* Talonnée en vitesse, elle S'OUVRE. Une coque ne rebondit pas sur la
               roche, et le trou est là où elle a frappé — donc l'échouage devient
               enfin une vraie cause de l'envahissement déjà écrit. */
            if (spd > 2.2 && _hardAgo <= 0)
            {
                int comp = Math.Min(Comps.Length - 1, Math.Max(0,
                    (int)Math.Floor(((f * S.L + S.L / 2) / S.L) * Comps.Length)));
                MakeBreach(comp, Math.Min(0.45, 0.06 * (spd - 2.0)), 0.06);
                _hardAgo = 5;          // elle ne peut pas être percée deux fois dans un souffle
            }
        }
    }

    /* ------------------------------------------------------------------ */
    /*  BORD À BORD                                                        */
    /* ------------------------------------------------------------------ */

    /// <summary>
    /// L'ABORDAGE, écrit comme l'échouage et pour la même raison : un ressort
    /// raide très amorti appliqué AU point de contact. Lui donner une règle à
    /// elle aurait garanti qu'elle contredise le fond la première fois qu'on la
    /// pousse sur un haut-fond à couple d'un autre.
    ///
    /// L'essai porte sur sa VRAIE flottaison, pas sur une ellipse : son contour
    /// est échantillonné station par station des deux bords, avec le même
    /// <c>HalfB</c> dont sont bâties la grille de sondes et la coque visible — donc
    /// ce qui la repousse est ce qu'on voit se toucher. L'histoire de l'écume est
    /// l'avertissement : l'ellipse passait jusqu'à deux mètres en dedans du bordé.
    ///
    /// C'est un essai EN PLAN, sans hauteur, et c'est réfléchi plutôt que
    /// paresseux : deux coques qui se rencontrent flottent chacune à sa
    /// flottaison, donc le contact intéressant est toujours bord contre bord.
    ///
    /// Les deux le font l'une contre l'autre, donc la paire s'écarte sans que
    /// personne n'arbitre : chacune paie ses propres contacts et Newton est
    /// satisfait par symétrie plutôt que par comptabilité.
    /// </summary>
    void Collide(double dt, ref Vec3d force, ref Vec3d torque, in Vec3d cog,
                 IReadOnlyList<ShipPhysics>? others)
    {
        Touching = 0;
        if (others == null || others.Count < 2) return;

        var S = Spec; var b = Body;
        // elle porte tout son poids à un tiers de mètre de chevauchement
        double kSpring = b.Mass * Config.G / 0.33;
        double spd = Math.Sqrt(b.Vel.X * b.Vel.X + b.Vel.Z * b.Vel.Z);
        _hardHit = Math.Max(0, _hardHit - dt);

        const int NS = 9;                                   // stations le long de chaque bord
        foreach (var o in others)
        {
            if (o == null || ReferenceEquals(o, this) || o.Foundered) continue;
            var ob = o.Body; var oS = o.Spec;

            /* Phase large, et c'est ce qui rend ceci bon marché : deux coques
               dont les centres sont plus éloignés que la somme de leurs
               demi-longueurs ne peuvent pas se toucher. */
            double dx = ob.Pos.X - b.Pos.X, dz = ob.Pos.Z - b.Pos.Z;
            double far = (S.L + oS.L) * 0.5;
            if (dx * dx + dz * dz > far * far) continue;

            Quatd oInv = ob.Quat.Inverted();

            for (int i = 0; i < NS; i++)
            {
                double t = (i + 0.5) / NS;                  // 0 à l'étambot, 1 à l'étrave
                double zl = (t - 0.5) * S.L, hw = Lines.HalfB(t);
                if (hw < 0.05) continue;

                for (int sgn = -1; sgn <= 1; sgn += 2)
                {
                    Vec3d pw = b.Quat.Rotate(new Vec3d(sgn * hw, 0, zl)) + b.Pos;

                    // dans SON repère, où son propre contour est une paire de nombres
                    Vec3d r2 = oInv.Rotate(pw - ob.Pos);
                    double ot = r2.Z / oS.L + 0.5;
                    if (ot <= 0 || ot >= 1) continue;
                    double ohw = o.Lines.HalfB(ot);
                    double pen = ohw - Math.Abs(r2.X);
                    if (pen <= 0) continue;

                    Touching = Math.Max(Touching, pen);

                    // hors de son bordé, en travers, ramené dans le monde
                    double sx = Math.Sign(r2.X);
                    if (sx == 0) sx = 1;
                    Vec3d nrm = ob.Quat.Rotate(new Vec3d(sx, 0, 0));
                    nrm.Y = 0;
                    if (nrm.LengthSquared < 1e-6) continue;
                    nrm = nrm.Normalized();

                    Vec3d r = pw - cog;
                    // la vitesse de CE point, pour que l'amortissement combatte
                    // le vrai mouvement
                    Vec3d vp = b.AngVel.Cross(r) + b.Vel;
                    double closing = vp.Dot(nrm);

                    double push = kSpring * Math.Min(pen, 1.2) / NS - closing * b.Mass * 1.6 / NS;
                    if (push <= 0) continue;

                    Vec3d fVec = nrm * push;
                    force += fVec;
                    torque += r.Cross(fVec);

                    /* Et abordée en vitesse, elle S'OUVRE, exactement comme sur
                       la roche. L'abordage devient donc une vraie cause de
                       l'envahissement déjà écrit, sans une ligne à lui. */
                    if (spd > 2.2 && _hardHit <= 0)
                    {
                        int comp = Math.Min(Comps.Length - 1, Math.Max(0,
                            (int)Math.Floor(t * Comps.Length)));
                        MakeBreach(comp, Math.Min(0.40, 0.05 * (spd - 2.0)), 0.42);
                        _hardHit = 5;      // pas deux fois dans le même souffle
                    }
                }
            }
        }
    }

    /* ------------------------------------------------------------------ */
    /*  AMARRÉE                                                            */
    /* ------------------------------------------------------------------ */

    /// <summary>
    /// L'AMARRAGE, et c'est l'échouage et l'abordage écrits une troisième fois —
    /// un ressort raide, très amorti, appliqué AU point où il agit.
    ///
    /// AVEC UNE DIFFÉRENCE, ET C'EST TOUT LE CARACTÈRE D'UN CORDAGE : IL TIRE ET
    /// NE POUSSE JAMAIS. En deçà de sa longueur un bout est mou et ne fait rien,
    /// donc elle évite sur son mou et raidit quand elle l'a filé — ce que fait
    /// une coque à quai, et ce que deux ressorts vers un point fixe ne feraient
    /// pas : elle tiendrait comme boulonnée.
    ///
    /// D'où les DÉFENSES, qui sont le même ressort avec le signe retourné. Avec
    /// quatre bouts et rien d'autre, un vent qui la met au quai la fait traverser
    /// le ponton pendant que toutes ses amarres pendent molles — c'est le quai
    /// qui tient un navire à distance du quai, pas ses cordages.
    ///
    /// Les bittes sont tenues en mètres MONDE VRAIS et converties ici. En local
    /// elles rejoindraient la liste des choses à décaler au recentrage, liste sur
    /// laquelle ce projet a déjà oublié quelque chose deux fois — et contrairement
    /// à l'embrun, une amarre qui raterait un recentrage la traînerait de quinze
    /// cents mètres en une image.
    /// </summary>
    void Moor(ref Vec3d force, ref Vec3d torque, in Vec3d cog, Ocean ocean)
    {
        if (Moorings.Count == 0) return;
        var b = Body;
        double ox = ocean.Origin.X, oz = ocean.Origin.Z;
        // elle prend son propre poids à quatre-vingts centimètres d'allongement :
        // du chanvre, pas de l'acier
        double kLine = b.Mass * Config.G / 0.8;

        foreach (var m in Moorings)
        {
            Vec3d pw = b.Quat.Rotate(new Vec3d(m.Lx, m.Ly, m.Lz)) + b.Pos;   // l'écubier
            Vec3d nrm = new Vec3d(m.Wx - ox - pw.X, m.Wy - pw.Y, m.Wz - oz - pw.Z);
            double d = nrm.Length;
            if (d < 1e-4) continue;

            // le bout veut d en deçà de len, la défense veut d au-delà : rien à
            // faire tant qu'on n'est pas du mauvais côté de sa longueur
            if (m.Push ? (d >= m.Len) : (d <= m.Len)) continue;
            nrm = nrm / d;

            Vec3d r = pw - cog;
            Vec3d vp = b.AngVel.Cross(r) + b.Vel;
            double closing = vp.Dot(nrm);        // vers la bitte est positif

            /* Une seule expression pour les deux : le bout veut d vers len, la
               défense veut d vers len par l'autre côté, donc l'allongement change
               simplement de signe — et le sens dans lequel « closing » est le
               mouvement que l'amortissement doit combattre aussi. */
            double sgn = m.Push ? -1 : 1;
            double pull = sgn * (kLine * (d - m.Len)) - sgn * closing * b.Mass * 0.9;
            if (pull <= 0) continue;
            pull *= sgn;
            /* Borné à un poids et demi. Un bout casse, et même avant de casser il
               n'y a aucun sens à ce qu'une amarre hale plus fort que le navire ne
               pèse — un ressort non borné plus un grand pas, c'est ainsi qu'un
               solveur envoie une coque en l'air. */
            pull = sgn * Math.Min(Math.Abs(pull), b.Mass * Config.G * 1.5);

            Vec3d fVec = nrm * pull;
            force += fVec;
            torque += r.Cross(fVec);
        }
    }

    /* ------------------------------------------------------------------ */
    /*  ELLE S'ASSOIT                                                      */
    /* ------------------------------------------------------------------ */

    /// <summary>
    /// Lui laisser trouver sa propre flottaison en eau PLATE, pour que la hauteur
    /// d'équilibre relevée soit exacte. Un navire lourd a une période de
    /// pilonnement plus longue, donc le temps d'installation suit la racine de sa
    /// longueur.
    ///
    /// PUIS REMETTRE LA MER COMME ELLE ÉTAIT. L'aplatir est nécessaire — elle
    /// doit trouver ses lignes sans qu'une houle la secoue — mais la laisser plate
    /// est un piège, et cela a coûté un bug : mettre une coque à l'eau depuis le
    /// panneau Flotte aplatissait la mer définitivement, la console continuant
    /// d'afficher force 6 au-dessus d'un lac. Un seul appelant le compensait par
    /// hasard, si bien que la faute n'est apparue qu'avec le SECOND. Le rétablir
    /// ici veut dire qu'aucun appelant n'a plus à le savoir.
    ///
    /// LEÇON GÉNÉRALE : un effet de bord qu'un seul appelant compense n'est pas
    /// corrigé, il est caché.
    /// </summary>
    public double Settle(Ocean ocean, Controls ctrl)
    {
        // elle est sur le point d'être posée quelque part : aucune cellule qui
        // traverse ne compte comme une gerbe
        _slamWarm = 3;
        double sea = ocean.SeaState, deg = ocean.WindDeg;
        ocean.SetSeaState(0, 0);

        Body.Pos = new Vec3d(0, 0.4, 0);
        int steps = (int)Math.Round(1200 * Math.Sqrt(Spec.L / 24));
        double t = 0;
        for (int i = 0; i < steps; i++)
        {
            Step(1.0 / 120, ocean, ctrl, t);
            t += 1.0 / 120;
        }
        Body.Vel = Vec3d.Zero;
        Body.AngVel = Vec3d.Zero;

        ocean.SetSeaState(sea, deg);
        return Body.Pos.Y;
    }
}
