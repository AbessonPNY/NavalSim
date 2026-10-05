using Godot;

namespace NavalSim;

/// <summary>
/// LE REFLET DE LENTILLE (lens_flare.glsl) — demandé. Une passe plein écran
/// (<see cref="ScreenEffect"/>), armée seulement quand le soleil est dans le
/// champ : les fantômes alignés sur l'axe soleil–centre, le voile et les branches
/// du diaphragme autour de lui. Le shader regarde lui-même si le soleil se voit
/// (ce qui passe devant l'éteint) ; on lui donne où il est, sa couleur, et combien
/// en laisser — le ciel couvert, la brume, l'orage et le réglage du menu.
///
/// C'est un artefact d'OBJECTIF, pas de l'œil : un marin ne voit pas de fantômes.
/// Il se règle et s'éteint au menu (Lumière : reflet de lentille).
/// </summary>
public partial class LensFlareEffect : ScreenEffect
{
    /// <summary>Le soleil sur l'image, de 0 à 1 (haut à gauche).</summary>
    public Vector2 SunUv;
    /// <summary>Ce qu'on en laisse passer, réglage compris ; zéro : rien à tracer.</summary>
    public float Strength;
    public Color SunColour = Colors.White;
    /// <summary>Le rayon du disque sondé, en part de la hauteur de l'image.</summary>
    public float ProbeRadius = 0.008f;

    public LensFlareEffect() : base("res://shaders/lens_flare.glsl", "naval_reflet", 96) { }

    protected override bool Prepare(RenderSceneData sd, Vector2I size)
    {
        if (Strength <= 0) return false;
        Put(0, sd.GetCamProjection().Inverse());
        Col(64, new Vector4(SunUv.X, SunUv.Y, Strength, size.X / (float)size.Y));
        Col(80, new Vector4(SunColour.R, SunColour.G, SunColour.B, ProbeRadius));
        return true;
    }
}
