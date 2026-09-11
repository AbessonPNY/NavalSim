using Godot;
using NavalSim.Core;

namespace NavalSim;

/// <summary>
/// La mer, côté moteur : un plan, un matériau, et le pont qui porte le spectre du
/// noyau jusqu'aux uniformes du shader.
///
/// Elle ne DÉCIDE de rien. Le spectre, la phase et l'origine flottante vivent
/// dans <see cref="NavalSim.Core.Ocean"/>, qui ne connaît pas Godot et que le
/// banc de parité compare au JavaScript d'origine. Ce fichier ne fait que
/// recopier ce que le noyau a calculé, une fois par image.
/// </summary>
public partial class OceanNode : Node3D
{
    /// <summary>Le noyau. C'est lui qu'on interroge pour savoir où est l'eau.</summary>
    public Ocean Core { get; } = new();

    MeshInstance3D _plane = null!;
    ShaderMaterial _mat = null!;

    // les réserves d'uniformes, écrites sur place : reconstruire ces tableaux
    // soixante fois par seconde ferait mille objets éphémères par seconde, donc
    // des pauses de ramasse-miettes — et une pause de ramasse-miettes EST une
    // saccade. Même discipline que setSeaState côté JavaScript.
    readonly Godot.Collections.Array _waveA = new();
    readonly Godot.Collections.Array _waveB = new();
    readonly float[] _wavePhase = new float[Config.NWaves];

    public override void _Ready()
    {
        for (int i = 0; i < Config.NWaves; i++)
        {
            _waveA.Add(Variant.From(Vector4.Zero));
            _waveB.Add(Variant.From(Vector2.Zero));
        }

        var shader = GD.Load<Shader>("res://shaders/ocean.gdshader");
        if (shader == null)
        {
            GD.PushError("shaders/ocean.gdshader introuvable");
            return;
        }
        _mat = new ShaderMaterial { Shader = shader };

        /* Le plan, et son nombre de segments : c'est le même OCEAN_SEG que le
           JavaScript, parce que le remaillage vers la caméra du vertex shader est
           calibré dessus — la taille locale de la maille sort de sa dérivée, et
           elle sert de critère unique à l'anticrénelage des vagues. */
        var mesh = new PlaneMesh
        {
            Size = new Vector2((float)Config.OceanSize, (float)Config.OceanSize),
            SubdivideWidth = Config.OceanSeg - 1,
            SubdivideDepth = Config.OceanSeg - 1
        };

        _plane = new MeshInstance3D
        {
            Mesh = mesh,
            MaterialOverride = _mat,
            // le déplacement sort du vertex shader, donc la boîte que Godot a
            // calculée sur le plan au repos est fausse : sans cela il l'écarte du
            // rendu dès que la caméra regarde de côté
            CustomAabb = new Aabb(
                new Vector3(-(float)Config.OceanSize, -60, -(float)Config.OceanSize),
                new Vector3((float)Config.OceanSize * 2, 120, (float)Config.OceanSize * 2)),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off
        };
        AddChild(_plane);

        _mat.SetShaderParameter("u_half", (float)Config.OceanSize * 0.5f);
        _mat.SetShaderParameter("u_seg", (float)Config.OceanSeg);
    }

    /// <summary>
    /// Tient le plan sous l'œil (l'illusion de mer sans fin) et recopie le
    /// spectre. La phase vient du noyau, réduite modulo 2π en DOUBLE : c'est ce
    /// qui la laisse tenir exactement dans le flottant 32 bits du shader, si loin
    /// qu'elle navigue.
    /// </summary>
    public void UpdateFrom(Vector3 eye, double t)
    {
        if (_mat == null) return;
        Core.Time = t;

        var centre = new Vector3(eye.X, 0, eye.Z);
        _plane.Position = centre;
        _mat.SetShaderParameter("u_centre", centre);
        _mat.SetShaderParameter("u_time", (float)t);
        _mat.SetShaderParameter("u_sharp", (float)Core.Sharp);

        for (int i = 0; i < Config.NWaves; i++)
        {
            ref Wave w = ref Core.Waves[i];
            _waveA[i] = Variant.From(new Vector4((float)w.Dx, (float)w.Dz, (float)w.Amp, (float)w.K));
            _waveB[i] = Variant.From(new Vector2((float)w.Omega, (float)w.Q));
            _wavePhase[i] = (float)w.Phase;
        }
        _mat.SetShaderParameter("u_wave_a", _waveA);
        _mat.SetShaderParameter("u_wave_b", _waveB);
        _mat.SetShaderParameter("u_wave_phase", _wavePhase);
    }
}
