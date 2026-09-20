using Godot;

namespace NavalSim;

/// <summary>
/// SOUS L'EAU — une passe plein écran (<see cref="ScreenEffect"/>) qui n'est
/// armée que lorsque l'œil passe sous la surface. Elle fait deux choses : l'eau
/// qui mange la lumière avec la distance, canal par canal, et les rais de soleil
/// qui filtrent entre les vagues, intégrés le long du rayon de vue.
///
/// La surface vue de dessous — la fenêtre de Snell et le miroir au-delà — n'est
/// pas ici : elle appartient à la mer elle-même (ocean.gdshader), qui sait déjà
/// où elle est et comment le ciel s'y plie.
/// </summary>
public partial class UnderwaterEffect : ScreenEffect
{
    /// <summary>La mer au-dessus de l'œil, en mètres monde.</summary>
    public float SeaY;
    public Vector3 Sun = Vector3.Up;
    public float SunLight = 1;
    /// <summary>La couleur de l'eau profonde, en linéaire (celle de la mer).</summary>
    public Color Water = new(0.004f, 0.033f, 0.063f);
    /// <summary>La force des rais, la distance jusqu'où on les intègre, et la densité de l'eau.</summary>
    public float Shafts = 1f, Reach = 60f, Density = 1f;
    public float Time;
    /// <summary>De 0 (tout entière dedans ou dehors) à 1 (l'œil à cheval sur la surface).</summary>
    public float Straddle;

    public UnderwaterEffect() : base("res://shaders/underwater.glsl", "naval_sous_eau", 192) { }

    protected override bool Prepare(RenderSceneData sd, Vector2I size)
    {
        Put(0, sd.GetCamProjection().Inverse());
        Put(64, new Projection(sd.GetCamTransform()));
        Col(128, new Vector4(Sun.X, Sun.Y, Sun.Z, SunLight));
        Col(144, new Vector4(Water.R, Water.G, Water.B, Time));
        Col(160, new Vector4(SeaY, Shafts, Reach, Density));
        Col(176, new Vector4(Straddle, 0, 0, 0));
        return true;
    }
}
