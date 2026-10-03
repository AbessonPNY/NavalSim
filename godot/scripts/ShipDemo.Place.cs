using NavalSim.Core;

namespace NavalSim;

/// <summary>
/// POSER LE NAVIRE AILLEURS, sans que le monde le remarque.
/// </summary>
public partial class ShipDemo
{
    /// <summary>
    /// Le point vrai (<paramref name="x"/>, <paramref name="z"/>) devient le zéro du
    /// calcul : la mer glisse dessous, la coque se pose ensuite à l'origine.
    /// La mer SEULE : ce qui tient une position (écume, autres coques, bêtes)
    /// garde la sienne, comme l'ont toujours fait la quête, la sauvegarde, la
    /// traversée, le titre et le ponton qui l'appellent.
    /// </summary>
    void RecentreOn(double x, double z)
    {
        var o = _sea.Core.Origin;
        _sea.Core.Rebase(x - o.X, z - o.Z);
    }

    /// <summary>
    /// UN SAUT AU LOIN, de (<paramref name="dx"/>, <paramref name="dz"/>) mètres :
    /// la mer, l'écume et les embruns glissent ensemble, la bête qui guettait
    /// replonge, la caméra fixe se replante, et la coque arrive sans erre. Le
    /// cimetière des galions et le cœur d'une dépression.
    /// </summary>
    void JumpBy(double dx, double dz)
    {
        _sea.Core.Time = _t;
        _sea.Core.Rebase(dx, dz);
        _foam.Rebase((float)dx, (float)dz);
        _spray.Pool.Rebase(dx, dz);
        if (_kraken.State == KrakenState.Lurk || _kraken.State == KrakenState.Grip) _kraken.Dive("storm");
        if (_fixed) Plant();
        var b = _ship.Physics.Body;
        b.Vel = Vec3d.Zero;
        b.AngVel = Vec3d.Zero;
    }
}
