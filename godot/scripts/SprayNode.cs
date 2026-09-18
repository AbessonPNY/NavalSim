using Godot;
using NavalSim.Core;

namespace NavalSim;

/// <summary>
/// L'embrun à l'écran : la réserve du noyau (<see cref="SprayPool"/>) dessinée
/// d'un seul appel. Un MultiMesh plutôt que quatre mille nœuds, pour la raison
/// même de la page : « des sprites avec chacun sa matière vont très bien pour
/// une douzaine de bouffées de fumée, et très mal pour deux cents gouttes ».
/// </summary>
public partial class SprayNode : MultiMeshInstance3D
{
    public SprayPool Pool { get; } = new();

    // 12 flottants de transformée + 4 de données propres, par instance
    readonly float[] _buf = new float[SprayPool.Max * 16];
    ShaderMaterial _mat = null!;

    public override void _Ready()
    {
        _mat = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/spray.gdshader") };
        Multimesh = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            UseCustomData = true,
            Mesh = new QuadMesh { Size = Vector2.One, Material = _mat },
            InstanceCount = SprayPool.Max,
            VisibleInstanceCount = 0
        };
        CastShadow = ShadowCastingSetting.Off;
        // ses sommets sont réécrits à chaque image : aucune boîte calculée ne reste vraie
        CustomAabb = new Aabb(new Vector3(-1e5f, -1e4f, -1e5f), new Vector3(2e5f, 2e4f, 2e5f));
    }

    /// <summary>La couleur de l'embrun suit le ciel : poussée comme aux autres matières.</summary>
    public ShaderMaterial Material => _mat;

    public void Step(double dt)
    {
        Pool.Update(dt);
        int n = Pool.Count;
        var p = Pool.Pos;
        for (int i = 0; i < n; i++)
        {
            int o = i * 16;
            // base identité, origine à la goutte (lignes de la transformée 3×4)
            _buf[o] = 1; _buf[o + 1] = 0; _buf[o + 2] = 0; _buf[o + 3] = p[i * 3];
            _buf[o + 4] = 0; _buf[o + 5] = 1; _buf[o + 6] = 0; _buf[o + 7] = p[i * 3 + 1];
            _buf[o + 8] = 0; _buf[o + 9] = 0; _buf[o + 10] = 1; _buf[o + 11] = p[i * 3 + 2];
            _buf[o + 12] = Pool.Size[i]; _buf[o + 13] = Pool.Alpha[i]; _buf[o + 14] = 0; _buf[o + 15] = 0;
        }
        if (n > 0) Multimesh.Buffer = _buf;
        Multimesh.VisibleInstanceCount = n;
    }
}
