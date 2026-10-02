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
        double wantSet = ctrl.SailsSet ? Math.Clamp(ctrl.Canvas, 0, 1) : 0;
        double stepSet = SetRate * dt;
        SetFrac += Math.Max(-stepSet, Math.Min(stepSet, wantSet - SetFrac));

        // L'eau qui entre et qui sort D'ABORD : elle fixe la masse et le centre
        // de gravité contre lesquels tout ce qui suit — la pesanteur, les
        // moments, l'inertie — sera ensuite pris.
        Flooding(dt, ocean, t);
        TrappedBreath(dt, ocean, t);

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
            double depth = ocean.Sample(pw.X, pw.Z, t, out Vec3d nrm) - pw.Y;

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

            /* LE COUP DE FREIN D'UNE CRÊTE. L'étrave qui avance dans une lame
               chasse devant elle l'eau qu'elle y enfonce, et lui rend son élan :
               ce que l'eau gagne, elle le perd — ρ · débit · vitesse, par le
               coefficient de masse ajoutée (SlamBrake).

               MAIS SEULE L'EAU QU'ELLE ENFONCE EN AVANÇANT. Une première écriture
               comptait toute cellule qui franchit la surface, et la plupart la
               franchissent parce que la coque pilonne ou que la mer monte — cette
               eau-là n'est poussée nulle part : par force 5, la Roter Löwe tombait
               de 5,2 à 1,9 nœuds. La part due à l'avance est la PENTE de la lame
               dans le sens de la marche par la vitesse du bordé, ∇η·v, pour les
               cellules de la bande de flottaison seulement — une pleine ne gagne
               plus rien, une sèche n'a encore rien. Par mer plate la pente est
               nulle : rien ne freine.

               Horizontal seulement : le même choc pousse la coque VERS LE HAUT, et
               c'est déjà ce qui la catapulte. Appliqué au point qui entre — une
               étrave qui enfourne freine de l'avant. */
            if (SlamBrake > 0 && f > 0 && f < 1 && nrm.Y > 0.2)
            {
                Vec3d rs = pw - cog;
                Vec3d vs = b.AngVel.Cross(rs) + b.Vel;
                double rise = -(nrm.X * vs.X + nrm.Z * vs.Z) / nrm.Y;     // m/s : la lame qui monte contre le bordé
                if (rise > 0)
                {
                    double ram = pr.Vol * rise / ProbeH;                  // m³/s enfoncés en avançant
                    Vec3d brake = new Vec3d(vs.X, 0, vs.Z) * (-Config.Rho * SlamBrake * ram);
                    force += brake;
                    torque += rs.Cross(brake);
                }
            }

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
        // la carène qui la porte au repos déplace sa masse : au-delà, elle est à flot
        Wet = Math.Clamp(submergedVol * Config.Rho / b.Mass, 0, 1);
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
        if (OnSlam != null && Glide == null && (_slamCool <= 0 || big)
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
        force += fwd * (-Math.Sign(vFwd) * vFwd * vFwd * S.Drag * inWater * HydroScale);
        force += right * ((-vRight * Math.Abs(vRight) * S.LateralQuad
                           - vRight * S.LateralLinear) * inWater * HydroScale);

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

        /* --- LES AVIRONS ---
           NAGÉS UN BORD À LA FOIS. La poussée d'un banc s'applique À SES PELLES
           et non à ses tolets : le nageur sur son aviron et l'aviron sur son
           tolet sont des forces INTÉRIEURES au système « embarcation et
           avirons », et la seule poussée du dehors est celle de l'eau sur la
           pelle, aux deux tiers de l'aviron hors du plat-bord. Prise au tolet le
           levier valait le tiers de la vérité, et une chaloupe pivotait d'un
           degré par seconde.

           Nager d'un bord seulement la fait tourner sans qu'aucune règle ne le
           dise : une embarcation sans gouvernail se conduit exactement ainsi.

           LE COUP EST UNE IMPULSION, PAS UNE POUSSÉE : la pelle est dans l'eau
           les 45 premiers pour cent du cycle, en demi-sinus de force, et rien
           sur le retour. Dimensionné pour que la moyenne des deux bancs nageant
           ensemble vaille la poussée de la machine — sa « topSpeed » reste donc sa
           vitesse ; scier (−1) en donne « sternPower ». La PHASE est la sienne, et
           le modèle la lit pour balancer les avirons en mesure. */
        if (S.Oars is OarsSpec oars)
        {
            double amp = S.MaxThrust * Math.PI / (4 * 0.45);
            // bâbord est +x dans son repère, tribord −x (l'invariant de main)
            double outb = S.B * 0.5 + 0.6 * oars.Length;
            for (int side = 0; side < 2; side++)
            {
                double inp = side == 0 ? ctrl.OarL : ctrl.OarR;
                /* LA PELLE DOIT TROUVER L'EAU. Échouée, la chaloupe nageait encore
                   dans le sable, en mesure et sans avancer (signalé) : la force
                   tombait avec la carène, pas le geste. Une pelle qui touche le
                   fond — la grève sous elle plus haute que la mer à vingt
                   centimètres près — ne nage pas, et l'aviron reste au repos. Sans
                   monde (le banc de parité), la mer est partout. */
                Vec3d oArm = b.Quat.Rotate(new Vec3d(side == 0 ? outb : -outb, 0, 0)) + b.Pos - cog;
                if (inp != 0 && World != null)
                {
                    Vec3d blade = oArm + cog;
                    if (World.HeightAt(ocean.Origin.X + blade.X, ocean.Origin.Z + blade.Z)
                        > ocean.Sample(blade.X, blade.Z, t) - 0.2) inp = 0;
                }
                OarInput[side] = inp;
                if (inp == 0) continue;
                OarPhase[side] = (OarPhase[side] + dt / oars.Period) % 1;
                double nage = OarPhase[side];
                if (nage >= 0.45) continue;
                double f = amp * Math.Sin(Math.PI * nage / 0.45)
                         * (inp > 0 ? 1 : S.SternPower) * Math.Abs(inp) * inWater;
                Vec3d oVec = fwd * f;
                force += oVec;
                torque += oArm.Cross(oVec);
            }
        }

        Sails(ctrl, ocean, cog, ref force, ref torque, fwd, right);
        Ground(dt, ref force, ref torque, cog, ocean);
        Jetty(dt, ref force, ref torque, cog, ocean);
        Collide(dt, ref force, ref torque, cog, neighbours);
        Moor(ref force, ref torque, cog, ocean);

        // --- intégration linéaire ---
        b.Vel += force * (dt / b.Mass);
        b.Vel *= 1 - 0.02 * dt;                       // un amortissement global très faible
        double vl = b.Vel.Length;
        double cap = Spec.SpeedLimit;                 // garde-fou : 40, sauf fiche moderne
        if (vl > cap) b.Vel = b.Vel * (cap / vl);
        b.Pos += b.Vel * dt;

        // --- intégration angulaire, dans le repère propre où l'inertie est diagonale ---
        Quatd qc = b.Quat.Inverted();
        Vec3d Tb = qc.Rotate(torque);
        Tb = new Vec3d(Tb.X / b.Ib.X, Tb.Y / b.Ib.Y, Tb.Z / b.Ib.Z);
        Tb = b.Quat.Rotate(Tb);
        b.AngVel += Tb * dt;

        /* LE TANGAGE, ET LUI SEUL, EST AMORTI PAR L'EAU QUI RESTE. Cet amortisseur
           est l'image de ce que la mer oppose à une coque qui tangue — les vagues
           qu'elle fait en tanguant, le frottement de son bordé —, donc il ne vaut
           que ce qu'il reste de carène dans l'eau. Appliqué à plein l'étrave hors
           de l'eau, il TENAIT l'assiette : catapultée par une crête, elle restait
           suspendue au lieu de piquer du nez (signalé), et filait de crête en
           crête sans traînée de coque.

           LE ROULIS GARDE LE SIEN, et ce n'est pas une paresse : libéré avec le
           tangage, le banc la faisait chavirer (109° sous voiles, force 9). Le
           roulis d'un trois-mâts est tenu par bien autre chose que sa carène — la
           toile qui freine en balayant l'air, les fonds qui rasent l'eau —, et
           c'est ce qu'il reste ici dans un seul nombre.

           Le lacet reste libre pour que le gouvernail puisse réellement la faire
           tourner : un amortisseur isotrope étrangle l'évolution. */
        // écrit pour rendre 1 EXACTEMENT à flot : 0,05 + 0,95 n en est pas un, en virgule flottante
        double hold = DampInAir ? 1.0 : 1.0 - 0.95 * (1.0 - SmoothStep(WetHoldLo, WetHoldHi, Wet));
        double wy = b.AngVel.Dot(up);
        /* LA FORMULE D'ORIGINE TANT QUE L'EAU LA TIENT : à flot, rien ne change, et
           la parité avec la page reste au bit près. Décomposer en trois axes donne
           le même nombre aux arrondis près — et ces arrondis-là, qu'aucune mer ne
           justifie, suffisaient à faire diverger le banc de parité en dix
           minutes. On ne décompose donc que quand le tangage doit s'en distinguer. */
        if (hold >= 1)
            b.AngVel = (b.AngVel - up * wy) * (1 - 3.0 * dt) + up * (wy * (1 - 0.5 * dt));
        else
        {
            double wp = b.AngVel.Dot(right), wr = b.AngVel.Dot(fwd);
            b.AngVel = fwd * (wr * (1 - 3.0 * dt))
                     + right * (wp * (1 - 3.0 * hold * dt))
                     + up * (wy * (1 - 0.5 * dt));
        }
        double al = b.AngVel.Length;
        if (al > 4) b.AngVel = b.AngVel * (4 / al);

        double wlen = b.AngVel.Length;
        if (wlen > 1e-8)
        {
            Vec3d axis = b.AngVel * (1 / wlen);
            Quatd dq = Quatd.FromAxisAngle(axis, wlen * dt);
            Vec3d comBefore = RotateAboutCom ? b.Quat.Rotate(b.Com) : Vec3d.Zero;
            // premultiply : dq · quat, puis renormalisation — Godot VÉRIFIE la
            // norme d'un quaternion là où three.js laissait passer
            b.Quat = (dq * b.Quat).Normalized();
            /* AUTOUR DE SON CENTRE DE GRAVITÉ, ce que dit la mécanique : l'origine
               du repère est déplacée de ce que la rotation ferait faire au centre.
               Négligeable pour un navire entier (son centre est à un mètre de
               l'origine), faux pour une moitié dont il est à sept : elle pivotait
               autour de sa tranche. Réservé aux moitiés, la page n'ayant que des
               navires entiers. */
            if (RotateAboutCom) b.Pos += comBefore - b.Quat.Rotate(b.Com);
        }

        if (Glide is double gy)
        {
            if (Foundered || FloodVol > 0.25 * HullVolume) Glide = null;
            else
            {
                b.Pos = new Vec3d(b.Pos.X, gy, b.Pos.Z);
                b.Vel = new Vec3d(b.Vel.X, 0, b.Vel.Z);
                Vec3d hx = b.Quat.Rotate(new Vec3d(0, 0, 1));
                b.Quat = Quatd.FromAxisAngle(new Vec3d(0, 1, 0), Math.Atan2(hx.X, hx.Z));
                b.AngVel = new Vec3d(0, b.AngVel.Y, 0);
            }
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

    /* L'AIR SOUS SES BARROTS, qui travaille pendant qu'elle descend.
       Les compartiments ne décrivent que le volume de carène : ils ont fini de
       roter à l'instant où elle est dessous, ce qui est exactement l'inverse de
       la vérité — une épave rote pendant des minutes. Ce qu'ils ne disent pas,
       c'est l'air sous les barrots, dans les châteaux et les caissons, qui se
       libère à mesure. Une poche, donc, lâchée sur une exponentielle tant que
       son point le plus haut est noyé. Les DOUZE POUR CENT de son volume de
       coque et les vingt secondes sont CHOISIS, non mesurés — il n'y a rien
       dans ce modèle contre quoi les mesurer —, et c'est dit ici plutôt que
       laissé passer pour de la physique. */
    /// <summary>
    /// LE GAIN DE VENT SUR LA SEULE COMPOSANTE QUI FAIT AVANCER. Multiplié en
    /// entier, il triplait aussi la poussée en travers, que la quille ne retient
    /// pas trois fois mieux : à gain 3 elle dérivait de 30° au près par force 6,
    /// contre 20 au naturel, et glissait en crabe (signalé). La composante en
    /// travers reste donc celle du vent réel — elle dérive comme un vrai navire,
    /// en allant plus vite. À gain 1, c'est la force elle-même, au bit près.
    /// </summary>
    static Vec3d Boost(in Vec3d f, in Vec3d fwd)
    {
        var ax = new Vec3d(fwd.X, 0, fwd.Z).Normalized();
        return f + ax * ((Config.WindGain - 1) * f.Dot(ax));
    }

    static double SmoothStep(double a, double b, double x)
    {
        double u = Math.Clamp((x - a) / (b - a), 0, 1);
        return u * u * (3 - 2 * u);
    }

    void TrappedBreath(double dt, Ocean ocean, double t)
    {
        if (Foundered && !_trapFilled)
        {
            _trapFilled = true;
            TrappedAir = 0.12 * HullVolume;
        }
        if (TrappedAir <= 1e-4) return;
        Compartment? top = null;
        double topY = double.NegativeInfinity;
        foreach (var c in Comps)
        {
            Vec3d pw = Body.Quat.Rotate(new Vec3d(0, c.DeckY, c.Mid.Z)) + Body.Pos;
            if (pw.Y > topY) { topY = pw.Y; top = c; _trapAt = pw; }
        }
        if (top != null && ocean.Sample(_trapAt.X, _trapAt.Z, t) > topY)
        {
            /* Quarante-cinq secondes, et non vingt comme la page : demandé, le
               chapelet de bulles doit durer bien après qu'elle a disparu. */
            double outv = TrappedAir * (1 - Math.Exp(-dt / 45));
            TrappedAir -= outv;
            top.Air += outv;
            top.Vent = _trapAt;
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
        OptSheet = Math.Max(S.MinSheet, Math.Min(S.MaxSheet, beta - OptimalAoA(beta)));

        LateenAngle = 0;
        /* LA BUTÉE DES VERGUES : bordées plus près, elles restent à leur butée —
           c'est ce qui fait qu'un carré remonte mal au vent, et qu'on gréait une
           latine à l'artimon (sans elle, le solveur laissait brasser les carrés
           jusqu'à l'axe, comme des voiles en long). */
        double aoa = beta - Math.Max(ctrl.Sheet, S.MinSheet);
        // plus rien en l'air dont il vaille la peine de parler
        if (SetFrac * Standing * Whole < 0.01) { Lateen(beta, vApp, app, cog, ref force, ref torque, fwd); return; }
        if (aoa <= 0.02) { Luffing = true; Lateen(beta, vApp, app, cog, ref force, ref torque, fwd); return; }      // trop choqué, ou en panne

        double CL = SailFoil.KL * Math.Sin(2 * aoa);
        double CD = SailFoil.CD0 + SailFoil.KD * Math.Sin(aoa) * Math.Sin(aoa);
        // la surface réellement établie, qui est ce contre quoi le vent pousse
        double q = 0.5 * Config.RhoAir * vApp * vApp * S.SquareArea * SetFrac * Standing * Whole;

        Vec3d sailF = app * (CD * q / vApp);              // la traînée, le long du vent
        Vec3d lift = new Vec3d(app.Z, 0, -app.X).Normalized();
        if (lift.Dot(fwd) < 0) lift = -lift;              // la portance la pousse en avant
        sailF += lift * (CL * q);
        /* LE FACTEUR DE VENT s'applique à la POUSSÉE, et à elle seule : ni à ce que
           la TOILE endure, qui reste la pression réelle — sinon force 4 déchirerait
           ce que force 7 laisse entier —, ni à la dérive. Voir Boost. */
        var pushed = Boost(sailF, fwd);
        force += pushed;

        Vec3d arm = b.Quat.Rotate(_ce) + b.Pos - cog;
        torque += arm.Cross(sailF * Config.WindHeel);
        SailDrive = pushed.Dot(fwd);

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
        double canvas = S.SquareArea * Math.Max(0.05, Standing * Whole);
        SailLoad = canvas > 1e-9 ? sailF.Length / canvas : 0;
        Lateen(beta, vApp, app, cog, ref force, ref torque, fwd);
    }

    /// <summary>
    /// LA LATINE D'ARTIMON : une voile en long, que l'équipage borde seul. Le
    /// même profil que le carré, mais elle s'écarte de l'axe de la seule quantité
    /// qu'il faut — l'incidence optimale, de 5° à LateenMax — là où un carré ne
    /// se brasse pas si près : au près, elle porte encore quand les carrés
    /// faseyent. Son centre de voilure est à l'artimon, loin sur l'arrière : elle
    /// pousse la poupe sous le vent, donc fait lofer — c'est pour cela qu'on la
    /// portait. Elle ne tombe qu'avec son mât (LateenUp).
    /// </summary>
    void Lateen(double beta, double vApp, in Vec3d app, in Vec3d cog, ref Vec3d force, ref Vec3d torque, in Vec3d fwd)
    {
        var S = Spec; var b = Body;
        if (S.LateenArea <= 0 || !LateenUp || SetFrac * Whole < 0.01) return;
        double lat = Math.Max(0.08, Math.Min(S.LateenMax, beta - OptimalAoA(beta)));
        LateenAngle = lat;
        double aoa = beta - lat;
        if (aoa <= 0.02) return;                          // vent debout : elle fasèye
        double CL = SailFoil.KL * Math.Sin(2 * aoa);
        double CD = SailFoil.CD0 + SailFoil.KD * Math.Sin(aoa) * Math.Sin(aoa);
        double q = 0.5 * Config.RhoAir * vApp * vApp * S.LateenArea * SetFrac * Whole;
        Vec3d f = app * (CD * q / vApp);
        Vec3d lift = new Vec3d(app.Z, 0, -app.X).Normalized();
        if (lift.Dot(fwd) < 0) lift = -lift;
        f += lift * (CL * q);
        Vec3d pushedL = Boost(f, fwd);                    // voir Sails : la poussée seule
        force += pushedL;
        Vec3d arm = b.Quat.Rotate(_ceL) + b.Pos - cog;
        torque += arm.Cross(f * Config.WindHeel);
        SailDrive += pushedL.Dot(fwd);
    }

    /* ------------------------------------------------------------------ */
    /*  ELLE TOUCHE LE FOND                                                */
    /* ------------------------------------------------------------------ */

    /// <summary>
    /// L'échouage se sonde en TROIS STATIONS, jamais sur les sondes de carène.
    /// <c>HeightAt</c> balaie la grille des îles ; l'appeler trois cents fois par
    /// sous-pas coûterait plus cher que tout le solveur réuni. L'étrave, le milieu
    /// et l'étambot suffisent à tout ce qui compte : elle s'ensable par l'avant
    /// sur une plage en pente douce, pivote sur un haut-fond qui la prend par le
    /// travers, ou s'assoit sur une quille droite.
    ///
    /// MAIS CINQ POINTS DE LA MEMBRURE À CHAQUE STATION, et non le seul point de
    /// quille — quille, les deux bouchains, les deux plats-bords. La quille n'est
    /// le point le plus bas que tant qu'elle est DROITE ; couchée, c'est son
    /// bordé, et retournée, c'est son plat-bord. Sondée sur la seule quille, une
    /// épave qui se retournait sur le fond y passait au travers : galion sabordé
    /// par onze mètres d'eau, quille tenue à −11,26 pendant que le reste
    /// descendait à −15,13 — quatre mètres DANS le sable — pour finir enterrée la
    /// quille en l'air, ce qui se voyait comme une disparition.
    ///
    /// ET C'EST LE BOIS QU'ON VOIT QUI SONDE, pas la cote de la fiche : un .glb
    /// n'a aucune raison d'avoir son bas de coque là où le plan de formes met sa
    /// quille, et la Roter Löwe en montrait 88 cm d'écart — la sonde sous le bois,
    /// donc la coque flottant au-dessus du sable. Voir <c>GroundLift</c>.
    ///
    /// LE FOND N'EST LU QU'UNE FOIS PAR STATION, sur l'axe de la coque, et les
    /// cinq points sont éprouvés contre cette hauteur-là. C'est une approximation,
    /// et elle est bonne exactement là où elle sert : les points hauts de la
    /// membrure ne touchent que couchée ou retournée, et ils sont alors presque à
    /// l'aplomb de cet axe. Droite, ils sont deux mètres au-dessus de la quille et
    /// ne touchent jamais — un échouage ordinaire ne change pas d'un cheveu, et le
    /// prix reste de trois <c>HeightAt</c>, comme avant.
    ///
    /// Le fond répond comme un ressort raide très amorti, appliqué AU point de
    /// contact — donc elle se soulève, gîte et embarde exactement comme la
    /// géométrie l'impose. Rien ne décide qu'elle est échouée ; les forces le
    /// font, comme rien ne décide qu'elle flotte.
    /// </summary>
    /// <summary>
    /// LE PONTON EST UN MUR, et c'est tout ce qu'il a besoin d'être.
    ///
    /// Il n'y a pas de moteur physique ici, donc pas de boîte de collision : il y a
    /// des forces. Un ponton est un SEGMENT dans le plan — de sa racine à son
    /// musoir, deux points que PortWorks tient déjà (Sx,Sz → Hx,Hz) — et une
    /// demi-largeur de tablier. Un point de bordé qui entre dans cette bande est
    /// repoussé perpendiculairement, par le même ressort raide et amorti que le
    /// fond. Rien ne décide qu'elle touche ; les forces le font.
    ///
    /// UN MUR VERTICAL SUR TOUTE LA HAUTEUR, et non un tablier à 1,70 m : les pieux
    /// descendent jusqu'au fond, donc une quille ne passe pas plus dessous qu'un
    /// pavois ne passe au travers. Se donner la peine de distinguer les deux
    /// n'apporterait qu'un cas où le navire traverse.
    ///
    /// SUR LES DEUX BORDÉS DE TROIS MEMBRURES — les mêmes stations que l'échouage.
    /// Le point de quille ne sert à rien ici : un ponton se prend par le flanc, et
    /// c'est le flanc qui doit toucher. Trois stations suffisent à ce qui compte :
    /// aborder de biais, pivoter sur le musoir, ranger le long du tablier.
    ///
    /// LE POSTE D'AMARRAGE EST HORS DE PORTÉE, et c'est vérifié : Berth.At laisse
    /// 4,5 m entre le bordé et le bord du tablier. Un navire à son poste n'est donc
    /// jamais repoussé — sans quoi il partirait tout seul à la première image.
    /// </summary>
    void Jetty(double dt, ref Vec3d force, ref Vec3d torque, in Vec3d cog, Ocean ocean)
    {
        if (Jetties.Length == 0) return;
        var S = Spec; var b = Body;
        double ox = ocean.Origin.X, oz = ocean.Origin.Z;
        double wx = ox + b.Pos.X, wz = oz + b.Pos.Z;
        double hw = Berth.Width * 0.5;
        // le même ressort que le fond : tout son poids à un tiers de mètre
        double kSpring = b.Mass * Config.G / (0.33 * 3);

        /* LA PILE EST RÉSERVÉE UNE FOIS, HORS DE LA BOUCLE. À l intérieur, chaque
           tour en aurait repris — quinze pontons par sous-pas, et la pile déborde
           sans que rien ne le dise. Signalé par l analyseur (CA2014), qui a vu ce
           que la relecture n avait pas vu. */
        ReadOnlySpan<double> stations = stackalloc double[] { 0.42, 0.0, -0.45 };
        foreach (var p in Jetties)
        {

            /* REJET PAR UN CERCLE D'ABORD. Quinze ports, trois stations, deux
               bordés, à chaque sous-pas : le test de segment coûterait plus que le
               reste du solveur si on le faisait partout. */
            double mx = (p.Sx + p.Hx) * 0.5, mz = (p.Sz + p.Hz) * 0.5;
            double ex = p.Hx - p.Sx, ez = p.Hz - p.Sz;
            double half = 0.5 * Math.Sqrt(ex * ex + ez * ez);
            double gx = wx - mx, gz = wz - mz, reach = half + S.L + hw;
            if (gx * gx + gz * gz > reach * reach) continue;

            double ee = Math.Max(1e-9, ex * ex + ez * ez);
            for (int st = 0; st < stations.Length; st++)
            {
                double zl = stations[st] * S.L, hb = Lines.HalfB(stations[st] + 0.5);
                if (hb < 0.05) continue;               // une membrure sans largeur n'a pas de flanc
                for (int side = -1; side <= 1; side += 2)
                {
                    Vec3d pw = b.Quat.Rotate(new Vec3d(side * hb, 0, zl)) + b.Pos;
                    double px = ox + pw.X, pz = oz + pw.Z;

                    // le point le plus proche DU SEGMENT, bornes comprises
                    double t = Math.Clamp(((px - p.Sx) * ex + (pz - p.Sz) * ez) / ee, 0, 1);
                    double nx = px - (p.Sx + ex * t), nz = pz - (p.Sz + ez * t);
                    double d = Math.Sqrt(nx * nx + nz * nz);
                    if (d >= hw) continue;

                    /* AU CŒUR MÊME DU TABLIER la normale n'existe pas : on prend la
                       perpendiculaire au ponton, du côté où le navire se trouve.
                       Sans ce repli, une division par zéro enverrait la coque à
                       l'infini — ce qui n'arrive qu'une fois sur mille, donc au
                       pire moment. */
                    if (d < 1e-4)
                    {
                        double L = Math.Sqrt(ee);
                        nx = -ez / L; nz = ex / L;
                        if (nx * gx + nz * gz < 0) { nx = -nx; nz = -nz; }
                        d = 1e-4;
                    }
                    nx /= d; nz /= d;
                    double pen = hw - d;

                    Vec3d r = pw - cog;
                    Vec3d vp = b.AngVel.Cross(r) + b.Vel;
                    // ce qui rentre dans le bois, et ce qui glisse le long
                    double vn = vp.X * nx + vp.Z * nz;
                    double push = Math.Max(0, kSpring * Math.Min(pen, 2.0) - vn * b.Mass * 1.2);
                    var fv = new Vec3d(nx * push, 0, nz * push);

                    /* ET ELLE FROTTE. Une coque qui range le long d'un ponton ne
                       glisse pas sur du verre : le bois mord, et c'est ce
                       frottement qui la fait s'arrêter au lieu de riper. Pris sur
                       la vitesse TANGENTE, donc sans rien retirer à l'accostage
                       lui-même. */
                    double tanx = vp.X - vn * nx, tanz = vp.Z - vn * nz;
                    fv += new Vec3d(-tanx, 0, -tanz) * (b.Mass * 0.35 * Math.Min(1, pen / 0.5));

                    force += fv;
                    torque += r.Cross(fv);
                }
            }
        }
    }

    void Ground(double dt, ref Vec3d force, ref Vec3d torque, in Vec3d cog, Ocean ocean)
    {
        Aground = 0;
        if (World == null) return;
        var S = Spec; var b = Body;
        double ox = ocean.Origin.X, oz = ocean.Origin.Z;
        /* LE BOIS QU'ON VOIT, ET NON LA COTE DE LA FICHE : voir GroundLift. Les
           cinq points montent du même écart, la membrure restant une membrure. */
        double keel = -(S.Hull.KeelDepth + S.Hull.KeelExtra) + GroundLift;
        double rail = S.Hull.FreeboardMid + GroundLift;
        // elle porte tout son poids à un tiers de mètre de pénétration
        double kSpring = b.Mass * Config.G / (0.33 * 3);

        _hardAgo = Math.Max(0, _hardAgo - dt);
        double spd = Math.Sqrt(b.Vel.X * b.Vel.X + b.Vel.Z * b.Vel.Z);

        ReadOnlySpan<double> stations = stackalloc double[] { 0.42, 0.0, -0.45 };
        for (int s = 0; s < stations.Length; s++)
        {
            double f = stations[s];
            double zl = f * S.L, hw = Lines.HalfB(f + 0.5);
            if (zl < ZLo || zl > ZHi) continue;      // une moitié : cette membrure est partie avec l'autre

            /* Le fond sous l'AXE de la membrure : c'est le seul point d'elle qui
               reste dessous de quelque façon qu'elle soit tombée. */
            Vec3d axis = b.Quat.Rotate(new Vec3d(0, 0, zl)) + b.Pos;
            double bed = World.HeightAt(ox + axis.X, oz + axis.Z);

            for (int k = 0; k < 5; k++)
            {
                if (k > 0 && hw < 0.05) break;   // une membrure sans largeur n'a que sa quille
                // quille, puis les deux bouchains, puis les deux plats-bords
                double lx = k == 0 ? 0 : (k % 2 == 1 ? -hw : hw);
                double ly = k == 0 ? keel : (k <= 2 ? GroundLift : rail);

                Vec3d pw = b.Quat.Rotate(new Vec3d(lx, ly, zl)) + b.Pos;
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
                        (int)Math.Floor(((zl + S.L / 2) / S.L) * Comps.Length)));
                    MakeBreach(comp, Math.Min(0.45, 0.06 * (spd - 2.0)), 0.06);
                    _hardAgo = 5;          // elle ne peut pas être percée deux fois dans un souffle
                }
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
        if (Moorings.Count == 0 && Grips.Count == 0) return;
        foreach (var m in Moorings) MoorOne(m, ref force, ref torque, cog, ocean);
        foreach (var m in Grips) MoorOne(m, ref force, ref torque, cog, ocean);
    }

    void MoorOne(Mooring m, ref Vec3d force, ref Vec3d torque, in Vec3d cog, Ocean ocean)
    {
        var b = Body;
        double ox = ocean.Origin.X, oz = ocean.Origin.Z;
        {
            m.Dragging = false;
            /* UN FREIN, pour ce qui la retient en nageant contre elle et non d'un
               point fixe : une résistance à son erre, par seconde, à son centre de
               gravité — elle ralentit sans se coucher. Un kraken n'est pas une
               bitte ; c'est plusieurs centaines de tonnes d'animal. */
            if (m.Brake > 0)
                force += new Vec3d(-b.Vel.X * b.Mass * m.Brake, 0, -b.Vel.Z * b.Mass * m.Brake);
            Vec3d pw = b.Quat.Rotate(new Vec3d(m.Lx, m.Ly, m.Lz)) + b.Pos;   // l'écubier
            Vec3d nrm = new Vec3d(m.Wx - ox - pw.X, m.Wy - pw.Y, m.Wz - oz - pw.Z);
            double d = nrm.Length;
            if (d < 1e-4) return;

            // le bout veut d en deçà de len, la défense veut d au-delà : rien à
            // faire tant qu'on n'est pas du mauvais côté de sa longueur
            if (m.Push ? (d >= m.Len) : (d <= m.Len)) return;
            nrm = nrm / d;

            Vec3d r = pw - cog;
            Vec3d vp = b.AngVel.Cross(r) + b.Vel;
            double closing = vp.Dot(nrm);        // vers la bitte est positif

            /* Une seule expression pour les deux : le bout veut d vers len, la
               défense veut d vers len par l'autre côté, donc l'allongement change
               simplement de signe — et le sens dans lequel « closing » est le
               mouvement que l'amortissement doit combattre aussi. */
            double sgn = m.Push ? -1 : 1;
            /* Un long câble est un ressort plus mou qu'une aussière courte ; Stretch
               dit à combien de mètres il prend son poids — absent, les quatre-vingts
               centimètres du chanvre d'un quai. */
            double k = m.Stretch > 0 ? b.Mass * Config.G / m.Stretch : b.Mass * Config.G / 0.8;
            double pull = sgn * (k * (d - m.Len)) - sgn * closing * b.Mass * 0.9;
            if (pull <= 0) return;
            pull *= sgn;
            /* Borné à un poids et demi. Un bout casse, et même avant de casser il
               n'y a aucun sens à ce qu'une amarre hale plus fort que le navire ne
               pèse — un ressort non borné plus un grand pas, c'est ainsi qu'un
               solveur envoie une coque en l'air. */
            pull = sgn * Math.Min(Math.Abs(pull), b.Mass * Config.G * 1.5);

            /* CE QUI TIENT NE TIENT PAS TOUT : au-delà de sa tenue il GLISSE. Le même
               ressort, dont le bout d'en face a le droit de céder — l'allongement
               que la borne refuse, c'est lui qui le paie en glissant vers elle. */
            if (m.Hold > 0 && pull > m.Hold)
            {
                double give = (pull - m.Hold) / k;
                double hl = Math.Sqrt(nrm.X * nrm.X + nrm.Z * nrm.Z);
                if (hl == 0) hl = 1;
                m.Wx -= nrm.X / hl * give; m.Wz -= nrm.Z / hl * give;    // nrm va de l'écubier au bout
                pull = m.Hold;
                m.Dragging = true;
            }
            m.Tension = pull;

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
        /* L'AMORTISSEUR PLEIN pendant qu'on la pose, même partie de haut : c'est
           une relaxation et non une chute, il faut qu'elle converge et non qu'elle
           tangue. Un amortisseur affaibli au départ ne change pas l'assise, il la
           rend moins finie au bout du compte de pas. */
        bool damp = DampInAir;
        DampInAir = true;
        int steps = (int)Math.Round(1200 * Math.Sqrt(Spec.L / 24));
        double t = 0;
        for (int i = 0; i < steps; i++)
        {
            Step(1.0 / 120, ocean, ctrl, t);
            t += 1.0 / 120;
        }
        Body.Vel = Vec3d.Zero;
        Body.AngVel = Vec3d.Zero;
        DampInAir = damp;

        ocean.SetSeaState(sea, deg);
        return Body.Pos.Y;
    }
}
