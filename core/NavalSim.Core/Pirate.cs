namespace NavalSim.Core;

/// <summary>
/// LE PIRATE CHASSE, PUIS ABORDE — piloterPirate de la page, sans Godot.
///
/// IL NE COULE PAS CE QU'IL VEUT PILLER. Un pirate ne gagne rien à envoyer une
/// prise par le fond : il la canonne jusqu'à ce qu'elle ne puisse plus ni fuir ni
/// se défendre, puis vient à couple. Quatre temps, portés par SON humeur :
///   · CHASSE — la proie non pirate la plus proche, à sa garde, au plein fouet ;
///   · ABORDAGE — dès qu'elle est ABÎMÉE il cesse le feu et vient par l'arrière,
///     dans son sillage puis à couple par la hanche : le seul secteur où une
///     bordée ne porte pas ;
///   · PILLAGE — tenu cinq secondes bord à bord, presque à la même vitesse ;
///   · FUITE — il s'éloigne trois minutes, et laisse sa victime un quart d'heure.
/// « Abîmée » se lit sur ce qui existe déjà : personne n'arbitre, ce sont les
/// dégâts réels qui décident.
/// </summary>
public sealed class Pirate
{
    public enum Phase { Chasse, Abordage, Fuite }

    public Phase State = Phase.Chasse;
    /// <summary>Sa proie, ou nulle.</summary>
    public ShipPhysics? Cible;
    /// <summary>Les proies déjà pillées, et jusqu'à quelle heure il les laisse.</summary>
    public readonly Dictionary<ShipPhysics, double> Ignore = new();
    public double Tenu, FuiteJusque;
    public Vec3d Fuite;                      // local : à décaler au recentrage
    /// <summary>La garde de la chasse : le plein fouet de ses pièces.</summary>
    public double Garde;

    public Pirate(double garde) => Garde = garde;

    /// <summary>Une coque que le pirate peut voir : sa physique, sa batterie, si elle est pirate aussi.</summary>
    public readonly record struct Sail(ShipPhysics Physics, Battery? Battery, bool Hostile);

    /* ABÎMÉE, sur ce qui existe déjà et sans compteur à part : de l'eau embarquée,
       un mât perdu, un quart de ses pièces démontées, ou quatre voies d'eau. */
    public static bool Damaged(ShipPhysics p, Battery? b)
    {
        int outN = 0, all = 0;
        if (b != null) foreach (var g in b.Guns) { all++; if (g.Out) outN++; }
        return p.FloodVol > 0.06 * p.HullVolume
            || p.Standing < 0.9
            || (all > 0 && outN / (double)all >= 0.25)
            || p.Breaches.Count >= 4;
    }

    /// <summary>
    /// Une image : où il va (écrit dans <paramref name="helm"/>), et ce qui se passe.
    /// Rend « abordage » quand il cesse le feu pour venir à couple, « pillage » quand
    /// il a pillé, sinon rien. <paramref name="player"/> : la coque qu'on barre.
    /// </summary>
    public string? Pilot(double dt, double t, ShipPhysics self, AutoHelm helm, IReadOnlyList<Sail> fleet, ShipPhysics player)
    {
        var b = self.Body;
        // la proie : garder celle qu'on a, sinon la plus proche qu'on n'a pas déjà pillée
        ShipPhysics? prey = Cible;
        Battery? preyBat = null;
        if (prey != null)
        {
            bool found = false;
            for (int i = 0; i < fleet.Count; i++) if (fleet[i].Physics == prey) { found = true; preyBat = fleet[i].Battery; break; }
            if (!found || prey.Foundered) prey = null;
        }
        if (State != Phase.Fuite && prey == null)
        {
            Cible = null; State = Phase.Chasse;
            double bd = 4000;
            for (int i = 0; i < fleet.Count; i++)
            {
                var o = fleet[i];
                if (o.Physics == self || o.Hostile || o.Physics.Foundered) continue;
                if (Ignore.TryGetValue(o.Physics, out double until) && until > t) continue;
                double d = Hyp(o.Physics.Body.Pos.X - b.Pos.X, o.Physics.Body.Pos.Z - b.Pos.Z);
                if (d < bd) { bd = d; prey = o.Physics; preyBat = o.Battery; }
            }
            Cible = prey;
        }

        if (State == Phase.Fuite)
        {
            helm.Standoff = 0;
            helm.Target = Fuite;
            if (t > FuiteJusque) { State = Phase.Chasse; Cible = null; }
            return null;
        }
        if (prey == null) { helm.Standoff = Garde; helm.Target = player.Body.Pos; return null; }

        var pb = prey.Body;
        double dist = Hyp(pb.Pos.X - b.Pos.X, pb.Pos.Z - b.Pos.Z);

        if (State == Phase.Chasse)
        {
            helm.Standoff = Garde;
            helm.Target = pb.Pos;
            if (Damaged(prey, preyBat)) { State = Phase.Abordage; Tenu = 0; return "abordage"; }
            return null;
        }

        // ABORDAGE : dans son sillage, puis à couple par la hanche
        helm.Standoff = 0;
        Vec3d f = pb.Quat.Rotate(new Vec3d(0, 0, 1));
        f = new Vec3d(f.X, 0, f.Z).Normalized();
        var s = new Vec3d(-f.Z, 0, f.X);
        double Lp = prey.Spec.L, Le = self.Spec.L, Bp = prey.Spec.B, Be = self.Spec.B;
        int side = (b.Pos.X - pb.Pos.X) * s.X + (b.Pos.Z - pb.Pos.Z) * s.Z >= 0 ? 1 : -1;
        helm.Target = dist > (Lp + Le) * 0.5 + 20
            ? pb.Pos - f * (Lp * 0.5 + Le * 0.5 + 12)
            : pb.Pos - f * (Lp * 0.2) + s * (side * (Bp * 0.5 + Be * 0.5 + 1.5));

        double vrel = Hyp(b.Vel.X - pb.Vel.X, b.Vel.Z - pb.Vel.Z);
        if (dist < (Lp + Le) * 0.45 && vrel < 2.5) Tenu += dt; else Tenu = Math.Max(0, Tenu - dt * 0.5);
        if (dist > 1500) { State = Phase.Chasse; return null; }        // elle lui a échappé
        if (Tenu < 5) return null;

        // PILLAGE, puis la fuite, droit à l'opposé d'elle
        Ignore[prey] = t + 900;
        State = Phase.Fuite;
        FuiteJusque = t + 180;
        double ax = b.Pos.X - pb.Pos.X, az = b.Pos.Z - pb.Pos.Z, al = Hyp(ax, az);
        if (al == 0) al = 1;
        Fuite = new Vec3d(b.Pos.X + ax / al * 3000, 0, b.Pos.Z + az / al * 3000);
        Cible = null;
        return "pillage";
    }

    /// <summary>Ne tire que pendant la CHASSE : à l'abordage, ses boulets frapperaient la coque qu'il vient piller.</summary>
    public ShipPhysics? Enemy => State == Phase.Chasse ? Cible : null;

    public void Rebase(double dx, double dz) => Fuite = new Vec3d(Fuite.X - dx, Fuite.Y, Fuite.Z - dz);

    static double Hyp(double a, double b) => Math.Sqrt(a * a + b * b);
}
