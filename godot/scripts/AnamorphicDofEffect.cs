using Godot;

namespace NavalSim;

/// <summary>
/// LA PROFONDEUR DE CHAMP ANAMORPHIQUE — Godot ne dessine son flou qu'en carré,
/// hexagone ou cercle, sans rapport d'aspect : l'ovale d'un objectif de scope
/// s'écrit donc en passe plein écran (<see cref="ScreenEffect"/>). Même zone
/// nette, même fondu et même intensité que la profondeur de champ de Godot, qu'elle
/// remplace quand on la choisit ; l'ellipse est deux fois plus haute que large.
///
/// L'intensité : le rayon du flou plein, vertical, vaut Amount × hauteur × 0,25 —
/// 22 px en 1080p à 0,08. La qualité fixe le nombre d'échantillons.
/// </summary>
public partial class AnamorphicDofEffect : ScreenEffect
{
    public float Near, Far = DofRange.Infinity, Fade = 0.5f, Amount = 0.08f;
    public int Quality = 1;
    /// <summary>La largeur de l'ellipse sur sa hauteur : 0,5, le 2:1 vertical du scope.</summary>
    public float Squeeze = 0.5f;

    static readonly int[] SamplesFor = { 16, 32, 48, 80 };

    public AnamorphicDofEffect() : base("res://shaders/anamorphic_dof.glsl", "naval_anamorphose", 96) { }

    protected override bool Prepare(RenderSceneData sd, Vector2I size)
    {
        Put(0, sd.GetCamProjection().Inverse());
        Col(64, new Vector4(Near, Far, Fade, Amount * size.Y * 0.25f));
        Col(80, new Vector4(SamplesFor[System.Math.Clamp(Quality, 0, 3)], Squeeze, size.X, size.Y));
        return true;
    }
}
