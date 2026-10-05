using System;

namespace NavalSim.Core;

/// <summary>Ce que le nageur veut, à cette image : les touches tenues, le regard.</summary>
public struct SwimInput
{
    /// <summary>−1 à 1 : avancer, à droite, monter.</summary>
    public double Fwd, Side, Up;
    /// <summary>Forcer : plus vite, et le souffle part plus vite.</summary>
    public bool Hard;
}

/// <summary>
/// LE NAGEUR — un homme à l'eau, en mètres VRAIS (rien à décaler quand l'origine
/// glisse). <see cref="Pos"/> est la place de son ŒIL : c'est ce que la caméra
/// montre et ce qui décide s'il respire.
///
/// LES CHIFFRES SONT CEUX D'UN HOMME, sans palmes ni masque (1690) :
/// — en surface, une brasse de marin à 0,9 m/s, un crawl forcé à 1,4 m/s ;
/// — dessous, 0,75 m/s, 1,1 en forçant : sans palmes on ne va pas plus vite ;
/// — poumons pleins, il FLOTTE ; la pression écrase l'air de ses poumons, et vers
///   douze mètres il devient neutre, puis coule — l'apnéiste le sait, il remonte
///   en se battant les dix premiers mètres et redescend sans effort au-delà ;
/// — l'APNÉE d'un pêcheur aguerri : soixante-dix secondes à nager tranquillement,
///   moins en forçant, moins en profondeur (on y travaille davantage). Les
///   contractions du diaphragme préviennent vers le tiers ; à zéro, la syncope.
/// Les écarts : l'eau des Caraïbes est à 27 °C (pas de froid à compter), et la vue
/// sous l'eau est nette — un œil nu y voit flou, ce que le jeu ne rend pas.
/// </summary>
public sealed class Swimmer
{
    public Vec3d Pos, Vel;
    /// <summary>Le regard : lacet (0 vers +z, comme les caméras) et site.</summary>
    public double Yaw, Pitch;
    /// <summary>La provision d'air, de 1 (poumons pleins) à 0 (la syncope).</summary>
    public double Breath = 1;
    /// <summary>La tête sous l'eau.</summary>
    public bool Under;
    /// <summary>Le souffle est à bout : la syncope. Celui qui le mène doit le repêcher.</summary>
    public bool Blackout;
    /// <summary>Secondes passées dessous, cette plongée.</summary>
    public double DiveTime;

    public const double SurfaceSpeed = 0.9, SurfaceHard = 1.4;
    public const double UnderSpeed = 0.75, UnderHard = 1.1;
    /// <summary>L'œil au-dessus de l'eau, la tête hors de l'eau en nageant.</summary>
    public const double EyeAbove = 0.14;
    /// <summary>L'œil ne descend pas plus près du fond : la poitrine y est déjà.</summary>
    public const double EyeOverBed = 0.35;
    /// <summary>Secondes d'apnée à nager tranquillement près de la surface.</summary>
    public const double Apnea = 70;
    /// <summary>Le souffle repris en surface : de vide à plein, en tant de secondes.</summary>
    public const double Recover = 20;
    /// <summary>La profondeur où l'homme devient neutre, poumons comprimés.</summary>
    public const double NeutralDepth = 12;
    /// <summary>Sous ce reste d'air, le diaphragme se contracte.</summary>
    public const double Contractions = 0.35;

    /// <summary>La direction du regard (lacet, site).</summary>
    public Vec3d Look => new(Math.Sin(Yaw) * Math.Cos(Pitch), Math.Sin(Pitch), Math.Cos(Yaw) * Math.Cos(Pitch));

    /// <summary>
    /// Un pas. <paramref name="surfaceY"/> est la mer au-dessus de lui, <paramref name="floorY"/>
    /// le fond (ou le dessus d'un rocher) ; <paramref name="floorAt"/> lit le fond ailleurs, pour
    /// refuser un pas qui le mettrait à sec — un nageur ne monte pas sur la plage à la brasse.
    /// </summary>
    public void Step(double dt, in SwimInput inp, double surfaceY, double floorY, Func<double, double, double> floorAt)
    {
        if (Blackout) { Vel = Vel * Math.Max(0, 1 - dt * 2); return; }
        double depth = surfaceY - Pos.Y;

        // ce qu'il veut faire, en vitesse
        Vec3d want;
        var flat = new Vec3d(Math.Sin(Yaw), 0, Math.Cos(Yaw));
        var right = new Vec3d(-flat.Z, 0, flat.X);     // sa droite : regardant vers +z, elle est −x
        if (!Under)
        {
            double sp = inp.Hard ? SurfaceHard : SurfaceSpeed;
            want = flat * (inp.Fwd * sp) + right * (inp.Side * sp * 0.55);
            // le CANARD : tête en bas, il passe dessous
            if (inp.Up < 0) { Under = true; DiveTime = 0; Vel = new Vec3d(Vel.X, -0.9, Vel.Z); }
        }
        else
        {
            double sp = inp.Hard ? UnderHard : UnderSpeed;
            want = Look * (inp.Fwd * sp) + right * (inp.Side * sp * 0.5) + new Vec3d(0, inp.Up * sp * 0.8, 0);
        }

        // l'eau freine : il atteint ce qu'il veut en une demi-seconde, et s'y tient
        double k = Math.Min(1, dt / 0.5);
        Vel = new Vec3d(Vel.X + (want.X - Vel.X) * k, Vel.Y, Vel.Z + (want.Z - Vel.Z) * k);
        if (Under)
        {
            double vy = Vel.Y + (want.Y - Vel.Y) * (inp.Up != 0 || inp.Fwd != 0 ? k : 0);
            // l'air des poumons, comprimé avec la profondeur : il porte, puis il lâche
            // 0,4 m/s² près de la surface : deux à quatre kilos de flottaison nette sur un homme de
            // soixante-dix, poumons pleins ; il remonte à 0,4 m/s, et revient au jour en deux secondes
            double lift = 0.4 * (1 - depth / NeutralDepth);
            vy += lift * dt;
            vy *= Math.Max(0, 1 - dt * 1.0);           // la traînée verticale
            Vel = new Vec3d(Vel.X, vy, Vel.Z);
        }

        // un pas qui le mettrait au sec n'est pas fait (la plage, un haut-fond découvert)
        var next = Pos + Vel * dt;
        double nextFloor = floorAt(next.X, next.Z);
        if (nextFloor > surfaceY - 0.25) { next = new Vec3d(Pos.X, next.Y, Pos.Z); Vel = new Vec3d(0, Vel.Y, 0); nextFloor = floorY; }
        Pos = next;

        if (!Under)
        {
            // il flotte : l'œil suit la vague, avec le temps qu'il faut à un corps pour la suivre
            double target = surfaceY + EyeAbove;
            Pos = new Vec3d(Pos.X, Pos.Y + (target - Pos.Y) * Math.Min(1, dt / 0.25), Pos.Z);
            Vel = new Vec3d(Vel.X, 0, Vel.Z);
        }
        else
        {
            // le fond l'arrête ; la surface le reprend s'il y remonte en montant
            double low = nextFloor + EyeOverBed;
            if (Pos.Y < low) { Pos = new Vec3d(Pos.X, low, Pos.Z); if (Vel.Y < 0) Vel = new Vec3d(Vel.X, 0, Vel.Z); }
            if (Pos.Y > surfaceY - 0.1 && Vel.Y >= 0 && DiveTime > 0.5) Under = false;
        }

        // le souffle
        if (Under)
        {
            DiveTime += dt;
            double moving = Math.Min(1, Math.Abs(inp.Fwd) + Math.Abs(inp.Side) + Math.Abs(inp.Up));
            double burn = (0.75 + 0.25 * moving) * (inp.Hard ? 1.6 : 1) * (1 + Math.Max(0, depth) / 25);
            Breath -= dt * burn / Apnea;
            if (Breath <= 0) { Breath = 0; Blackout = true; }
        }
        else Breath = Math.Min(1, Breath + dt / Recover);
    }
}
