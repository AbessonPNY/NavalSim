using Godot;
using NavalSim.Core;

namespace NavalSim;

/// <summary>
/// LE PASSAGE ENTRE LES DEUX MONDES : le noyau compte en doubles (Vec3d), Godot
/// dessine en simples (Vector3). Ici et pas ailleurs — le noyau ne connaît pas
/// Godot, et c'est voulu.
/// </summary>
public static class VecX
{
    public static Vector3 ToGodot(this Vec3d v) => new((float)v.X, (float)v.Y, (float)v.Z);
    public static Vec3d ToCore(this Vector3 v) => new(v.X, v.Y, v.Z);
}
