namespace NavalSim.Core;

/// <summary>
/// LES CHIFFRES DU SPECTRE DE HOULE, réglables (l'atelier de la mer, Godot). Chacun
/// vaut par défaut ce que <see cref="Ocean.SetSeaState"/> portait en dur : à défauts
/// égaux, la mer est la même au bit près, et le banc de parité avec la page tient.
///
/// La houle est lue par TROIS calculateurs (le shader de la mer, l'échantillonneur de
/// la flottaison, la passe d'écume). Ils lisent tous le spectre que ce réglage façonne :
/// le changer change la mer que les coques sentent autant que celle qu'on voit — c'est
/// voulu, et c'est pourquoi ce n'est pas un réglage d'aspect.
/// </summary>
public sealed class SeaTune
{
    /// <summary>Le nombre de composantes du spectre, celles que le GPU dessine (8 à <see cref="Config.NWavesMax"/>).</summary>
    public int Count = Config.NWaves;
    /// <summary>Les plus énergiques d'entre elles, que la flottaison intègre (le reste ne soulève pas une coque).</summary>
    public int CpuCount = Config.NWavesCpu;
    /// <summary>JONSWAP : l'aiguisage du pic (3,3 en mer du Nord ; 1 rend Pierson-Moskowitz).</summary>
    public double Gamma = 3.3;
    /// <summary>La pulsation de pic, multipliée : plus haut, des vagues plus courtes et plus rapides.</summary>
    public double PeakScale = 1.0;
    /// <summary>La bande où l'on pose les composantes, en multiples de la pulsation de pic.</summary>
    public double BandLo = 0.62, BandHi = 3.4;
    /// <summary>L'étalement directionnel : base, part des courtes, et sa montée avec l'état de mer.</summary>
    public double SpreadBase = 0.16, SpreadShort = 0.55, SpreadState0 = 0.34, SpreadState1 = 0.045;
    /// <summary>La raideur de Gerstner (le terme horizontal) : base, montée par Beaufort, plafond par composante.</summary>
    public double ChopBase = 0.35, ChopState = 0.075, QMax = 0.85;
    /// <summary>L'aiguisage des crêtes : base, montée par Beaufort, plafond (au-dessus de 1).</summary>
    public double SharpBase = 0.06, SharpState = 0.055, SharpMax = 0.95;

    public SeaTune Clone() => (SeaTune)MemberwiseClone();
}
