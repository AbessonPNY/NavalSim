namespace NavalSim.Core;

/// <summary>
/// LE CAP AU COMPAS ET LE LACET DU JEU, dans un sens et dans l'autre. Le nord est
/// +z et l'EST EST −x (CLAUDE.md, « Repères ») : une inversion de signe ici
/// retournerait la carte entière. La formule était réécrite partout où l'on lit
/// un cap ou en pose un ; elle n'est plus qu'ici.
/// </summary>
public static class Compass
{
    /// <summary>Le cap en degrés, de 0 à 360, lu sur le vecteur d'étrave (pas sur des angles d'Euler).</summary>
    public static double HeadingDeg(Vec3d fwd) => (Math.Atan2(-fwd.X, fwd.Z) * 180 / Math.PI + 360) % 360;

    /// <summary>Le lacet (radians, autour de +y) d'une coque qui porte ce cap.</summary>
    public static double YawOf(double headingDeg) => -headingDeg * Math.PI / 180;
}
