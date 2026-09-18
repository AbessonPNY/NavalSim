using System;

namespace NavalSim.Core;

/// <summary>Ce qui arrête une goutte d'embrun : une coque, pour l'instant.</summary>
public interface ISprayCollider
{
    /// <summary>
    /// Corrige la goutte si elle est entrée dans l'obstacle. Rend <c>false</c>
    /// si elle doit disparaître (tombée sur un pont, elle est de l'eau embarquée).
    /// </summary>
    bool Collide(ref Vec3d p, ref Vec3d v);
}

/// <summary>
/// L'EMBRUN RENVOYÉ PAR LA COQUE — un AJOUT : dans la page les gouttes la
/// traversent. Une gerbe naît au bord du bordé et part en couronne, donc la moitié
/// de ses paquets file vers la coque ; sans obstacle ils la traversaient, et vus de
/// la chambre du capitaine ils étaient DEDANS, entre l'œil et la cloison, là où
/// rien ne pouvait les cacher.
///
/// La coque est lue sur ce que la mer en sait déjà : sa largeur à chaque station
/// par le profil de flottaison mesuré (<see cref="HullProfile"/>), son dessus —
/// châteaux compris — et sa quille par des fonctions de la station. Un volume
/// simple, droit en travers : il ne cherche pas à suivre l'évasement des hauts,
/// il cherche à empêcher l'eau de passer à travers le bois.
///
/// Par le côté, la goutte REBONDIT, avec la vitesse du bordé au point de choc
/// (v + ω × r) : une coque qui roule dans une gerbe la renvoie. Les deux
/// coefficients — 0,35 de rebond, 0,7 le long du bordé — sont CHOISIS : de l'eau
/// jetée sur du bois s'y écrase bien plus qu'elle ne rebondit. Par-dessus, elle
/// tombe sur le pont et y reste : de l'eau embarquée ne vole plus.
/// </summary>
public sealed class HullCollider : ISprayCollider
{
    public const double Restitution = 0.35, Slide = 0.7;

    readonly ShipPhysics _phys;
    readonly HullProfile _prof;
    readonly Func<double, double> _topAt, _keelAt;
    readonly double _reach;

    /// <param name="topAt">le dessus de la coque à la station z (repère du navire)</param>
    /// <param name="keelAt">le dessous de la quille à la station z</param>
    public HullCollider(ShipPhysics phys, HullProfile prof, Func<double, double> topAt, Func<double, double> keelAt)
    {
        _phys = phys; _prof = prof; _topAt = topAt; _keelAt = keelAt;
        // rien au-delà de cette distance ne peut toucher : un test grossier d'abord
        _reach = prof.HalfLen + prof.MaxHalfB + 2;
    }

    /// <summary>La demi-largeur à la station z, entre les stations du profil.</summary>
    double HalfB(double z)
    {
        var f = _prof.Fractions;
        int n = f.Length;
        double u = Math.Clamp((z / _prof.HalfLen) * 0.5 + 0.5, 0, 1) * n - 0.5;
        int i0 = Math.Clamp((int)Math.Floor(u), 0, n - 1), i1 = Math.Min(i0 + 1, n - 1);
        double t = Math.Clamp(u - Math.Floor(u), 0, 1);
        return (f[i0] * (1 - t) + f[i1] * t) * _prof.MaxHalfB;
    }

    public bool Collide(ref Vec3d p, ref Vec3d v)
    {
        var b = _phys.Body;
        Vec3d r = p - b.Pos;
        if (Math.Abs(r.X) > _reach || Math.Abs(r.Z) > _reach) return true;
        var inv = b.Quat.Inverted();
        Vec3d l = inv.Rotate(r);
        if (l.Z < _prof.EndAft || l.Z > _prof.EndFwd) return true;
        double hb = HalfB(l.Z);
        double top = _topAt(l.Z), keel = _keelAt(l.Z);
        if (Math.Abs(l.X) >= hb || l.Y >= top || l.Y <= keel) return true;

        // la vitesse du bordé à cet endroit, et celle de la goutte relativement à lui
        var w = b.AngVel;
        Vec3d hullV = new Vec3d(w.Y * r.Z - w.Z * r.Y, w.Z * r.X - w.X * r.Z, w.X * r.Y - w.Y * r.X) + b.Vel;
        Vec3d rel = inv.Rotate(v - hullV);

        double side = hb - Math.Abs(l.X), down = top - l.Y;
        if (down < side && rel.Y <= 0) return false;     // sur le pont : embarquée

        double sx = l.X >= 0 ? 1 : -1;
        l = new Vec3d(sx * (hb + 0.02), l.Y, l.Z);        // ressortie contre le bordé
        if (rel.X * sx < 0) rel = new Vec3d(-rel.X * Restitution, rel.Y * Slide, rel.Z * Slide);
        p = b.Pos + b.Quat.Rotate(l);
        v = b.Quat.Rotate(rel) + hullV;
        return true;
    }
}
