namespace NavalSim.Core;

/// <summary>
/// Un vecteur a trois composantes, EN DOUBLE, et c'est toute sa raison d'etre.
///
/// J'avais d'abord pris <c>System.Numerics.Vector3</c>, pour le SIMD. C'etait une
/// faute : il est en flottant 32 BITS, comme le <c>Vector3</c> de Godot. Le banc
/// de parite l'a trouve tout seul -- le centre de gravite en sortait a 9,4e-8 du
/// JS, ce qui est exactement l'epsilon du float32 et n'a rien a voir avec une
/// difference de formule.
///
/// Or ce projet a deux endroits ou le float32 n'est pas une approximation mais
/// une panne :
///
///   - LA PHASE DE GERSTNER. Elle vaut k.x avec k jusqu'a 3 rad/m, donc une
///     position de quelques kilometres consomme deja la quasi-totalite des sept
///     chiffres d'un float32. C'est tout le sujet de l'origine flottante, et la
///     reduction modulo 2pi qui la sauve n'a de sens QUE si elle est faite en
///     double -- c'est litteralement ce que fait le JavaScript, dont les nombres
///     sont des doubles.
///
///   - LES BRAS DE LEVIER. Le centre de gravite monde est soustrait a chaque
///     sonde pour en tirer un couple. Une erreur la-dessus ne se voit pas comme
///     du bruit : elle se voit comme du roulis parasite, puis comme une
///     divergence numerique. Ce projet a deja paye ce bug une fois, par un
///     vecteur temporaire aliase.
///
/// Le SIMD est donc rendu -- ce sont des doubles, et le gain mesure du portage
/// (0,534 ms contre 0,882) ne venait pas de la de toute facon. La conversion vers
/// le Vector3 de Godot se fait aux seules FRONTIERES, une fois par image et par
/// objet, la ou elle ne coute rien et ou le float32 est ce que le moteur veut.
/// </summary>
public struct Vec3d : IEquatable<Vec3d>
{
    public double X, Y, Z;

    public Vec3d(double x, double y, double z) { X = x; Y = y; Z = z; }

    public static readonly Vec3d Zero = new(0, 0, 0);
    public static readonly Vec3d UnitY = new(0, 1, 0);

    public static Vec3d operator +(Vec3d a, Vec3d b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    public static Vec3d operator -(Vec3d a, Vec3d b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    public static Vec3d operator -(Vec3d a) => new(-a.X, -a.Y, -a.Z);
    public static Vec3d operator *(Vec3d a, double s) => new(a.X * s, a.Y * s, a.Z * s);
    public static Vec3d operator *(double s, Vec3d a) => a * s;
    public static Vec3d operator /(Vec3d a, double s) => new(a.X / s, a.Y / s, a.Z / s);

    public readonly double LengthSquared => X * X + Y * Y + Z * Z;
    public readonly double Length => Math.Sqrt(LengthSquared);

    public readonly double Dot(in Vec3d b) => X * b.X + Y * b.Y + Z * b.Z;

    public readonly Vec3d Cross(in Vec3d b)
        => new(Y * b.Z - Z * b.Y, Z * b.X - X * b.Z, X * b.Y - Y * b.X);

    public readonly Vec3d Normalized()
    {
        double n = Length;
        return n > 0 ? this / n : Zero;
    }

    public readonly bool Equals(Vec3d o) => X == o.X && Y == o.Y && Z == o.Z;
    public readonly override bool Equals(object? o) => o is Vec3d v && Equals(v);
    public readonly override int GetHashCode() => HashCode.Combine(X, Y, Z);
    public readonly override string ToString() => $"({X:F3}, {Y:F3}, {Z:F3})";
}
