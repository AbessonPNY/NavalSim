using Godot;
using System;
using NavalSim.Core;

namespace NavalSim;

/// <summary>
/// LE REPÈRE DE LA PROFONDEUR DE CHAMP : des bandes posées sur la mer, là où le
/// net commence (verte), là où le flou du loin commence (orangée) et là où il est
/// plein (pâle) — celles que le plan de flou fait toucher à l'eau. Montré pendant qu'on
/// règle, pour voir ce que « flou au-delà de 600 m » veut dire sur l'eau.
///
/// PAS UN CERCLE. Godot mesure le flou à la PROFONDEUR — la distance le long de
/// l'axe de la caméra —, pas à la distance de l'œil : les points de même flou
/// forment un plan face à la caméra, qui coupe la mer selon une ligne en travers
/// de l'image. C'est cette ligne qu'on trace, en suivant la houle.
///
/// Une bande DEBOUT sur l'eau plutôt que couchée : vue en rasant, une bande
/// couchée s'écrase à rien, alors qu'une bande debout d'une hauteur
/// proportionnelle à sa distance garde la même épaisseur à l'écran.
/// </summary>
public partial class DofMarker : MeshInstance3D
{
    const int Segments = 64;
	const float Lift = 0.2f;          // au-dessus de l'eau : sous elle, la mer transparente la cacherait
	const float Thickness = 0.004f;   // hauteur de la bande, en part de sa distance (≈ 4 px en 1080p)

	readonly ImmediateMesh _mesh = new();
	static readonly Color Start = new(1.0f, 0.55f, 0.15f), Full = new(0.85f, 0.9f, 1.0f), SharpFrom = new(0.55f, 0.95f, 0.6f);

	public DofMarker()
	{
		Mesh = _mesh;
		CastShadow = ShadowCastingSetting.Off;
		MaterialOverride = new StandardMaterial3D
		{
			ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
			VertexColorUseAsAlbedo = true,
			CullMode = BaseMaterial3D.CullModeEnum.Disabled,
			DisableFog = true
		};
		Visible = false;
	}

	/// <summary>Retracer les deux bandes pour cette caméra et cette mer.</summary>
	public void Draw(Camera3D cam, Ocean sea, double t, float near, float far, float fade)
	{
		_mesh.ClearSurfaces();
		Transform3D xf = cam.GlobalTransform;
		Vector3 f = -xf.Basis.Z, eye = xf.Origin;
		var h = new Vector3(f.X, 0, f.Z);
		if (h.Length() < 1e-3f) return;
		h = h.Normalized();
		float hf = h.Dot(f);
		// regardant presque à plomb, le plan de flou ne coupe plus la mer devant soi
		if (hf < 0.1f) return;
		var r = new Vector3(h.Z, 0, -h.X);
		Vector2 vp = cam.GetViewport().GetVisibleRect().Size;
		float tanH = Mathf.Tan(Mathf.DegToRad(cam.Fov) * 0.5f) * (vp.X / Math.Max(1f, vp.Y));
		// le net commence (verte) ; le flou du loin commence (orangée), puis est plein (pâle)
		if (near > 0) Band(sea, t, eye, f, h, r, hf, tanH, near, SharpFrom);
		if (far < DofRange.Infinity)
		{
			Band(sea, t, eye, f, h, r, hf, tanH, far, Start);
			Band(sea, t, eye, f, h, r, hf, tanH, far * (1 + fade), Full);
		}
	}

	void Band(Ocean sea, double t, Vector3 eye, Vector3 f, Vector3 h, Vector3 r, float hf, float tanH,
			  float depth, Color c)
	{
		float half = depth * tanH * 1.3f, thick = depth * Thickness, rf = r.Dot(f);
		_mesh.SurfaceBegin(Mesh.PrimitiveType.TriangleStrip);
		_mesh.SurfaceSetColor(c);
		for (int i = 0; i <= Segments; i++)
		{
			float s = -half + 2 * half * i / Segments;
			/* La profondeur d'un point p est (p − œil)·f : on cherche, le long de
			   l'horizontale h, celui qui est à la profondeur voulue ET sur l'eau. La
			   hauteur de l'eau dépend de l'endroit, l'endroit de la hauteur : trois
			   tours suffisent, la houle ne penche pas assez pour s'en écarter. */
            float y = 0, along = 0;
            Vector3 p = default;
            for (int k = 0; k < 3; k++)
            {
                along = (depth - s * rf - (y - eye.Y) * f.Y) / hf;
                p = eye + r * s + h * along;
                y = (float)sea.Sample(p.X, p.Z, t) + Lift;
            }
            p.Y = y;
            _mesh.SurfaceAddVertex(p);
            _mesh.SurfaceAddVertex(p + Vector3.Up * thick);
        }
        _mesh.SurfaceEnd();
    }
}
