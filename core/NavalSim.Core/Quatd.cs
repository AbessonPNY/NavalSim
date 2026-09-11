namespace NavalSim.Core;

/// <summary>
/// Un quaternion EN DOUBLE, pour la même raison que <see cref="Vec3d"/> existe.
///
/// Celui de Godot est en flottant 32 bits, et l'orientation du corps est
/// INTÉGRÉE : elle s'accumule sur des milliers de pas, chacun ajoutant son
/// erreur d'arrondi à la précédente. Une coque qui navigue une heure fait
/// deux cent mille pas, et en float32 la dérive se lit comme une gîte qui
/// s'installe sans cause.
///
/// Les formules sont celles de three.js AU BIT PRÈS, et délibérément : c'est ce
/// qui permet au banc de parité de comparer le solveur porté au solveur
/// d'origine. Une écriture équivalente mais différemment associée donnerait des
/// derniers bits différents, et le banc ne saurait plus distinguer une faute de
/// portage d'une simple réassociation.
///
/// ET GODOT VÉRIFIE LA NORMALISATION, là où three.js laisse passer : son
/// opérateur lève sur un quaternion à 0,9985 de norme. Raison de plus pour
/// renormaliser à chaque pas, ce que fait <c>Integrate</c>.
/// </summary>
public struct Quatd
{
    public double X, Y, Z, W;

    public Quatd(double x, double y, double z, double w) { X = x; Y = y; Z = z; W = w; }

    public static readonly Quatd Identity = new(0, 0, 0, 1);

    /// <summary>Le produit a·b, dans l'ordre et l'associativité de three.js.</summary>
    public static Quatd operator *(Quatd a, Quatd b) => new(
        a.X * b.W + a.W * b.X + a.Y * b.Z - a.Z * b.Y,
        a.Y * b.W + a.W * b.Y + a.Z * b.X - a.X * b.Z,
        a.Z * b.W + a.W * b.Z + a.X * b.Y - a.Y * b.X,
        a.W * b.W - a.X * b.X - a.Y * b.Y - a.Z * b.Z);

    /// <summary>Le conjugué, qui est l'inverse d'un quaternion unitaire.</summary>
    public readonly Quatd Inverted() => new(-X, -Y, -Z, W);

    public readonly double Length => Math.Sqrt(X * X + Y * Y + Z * Z + W * W);

    public readonly Quatd Normalized()
    {
        double l = Length;
        if (l == 0) return Identity;
        return new Quatd(X / l, Y / l, Z / l, W / l);
    }

    public static Quatd FromAxisAngle(in Vec3d axis, double angle)
    {
        double half = angle / 2.0, s = Math.Sin(half);
        return new Quatd(axis.X * s, axis.Y * s, axis.Z * s, Math.Cos(half));
    }

    /// <summary>
    /// Tourne un vecteur. Transcription exacte de <c>applyQuaternion</c> de
    /// three.js, y compris l'ordre des opérations — voir la remarque en tête.
    /// </summary>
    public readonly Vec3d Rotate(in Vec3d v)
    {
        double ix = W * v.X + Y * v.Z - Z * v.Y;
        double iy = W * v.Y + Z * v.X - X * v.Z;
        double iz = W * v.Z + X * v.Y - Y * v.X;
        double iw = -X * v.X - Y * v.Y - Z * v.Z;
        return new Vec3d(
            ix * W + iw * -X + iy * -Z - iz * -Y,
            iy * W + iw * -Y + iz * -X - ix * -Z,
            iz * W + iw * -Z + ix * -Y - iy * -X);
    }

    public readonly override string ToString() => $"({X:F4}, {Y:F4}, {Z:F4}, {W:F4})";
}
