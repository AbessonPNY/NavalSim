/* LA SPHÈRE D'ÉNERGIE — la bulle qui se ferme autour du navire avant le saut.
 *
 * DEUX FACES, comme le .glb le déclare. La caméra de pont est DEDANS : en ne
 * gardant que les faces avant, la coquille disparaîtrait pour qui la regarde de
 * l'intérieur, ce qui est le point de vue le plus intéressant des sept dernières
 * secondes.
 *
 * ELLE N'ÉCRIT PAS SA PROFONDEUR mais elle la TESTE : l'hémisphère lointain reste
 * ainsi caché derrière la coque au lieu de doubler le voile.
 *
 * LA LUMIÈRE EST DANS teleport.gdshaderinc, partagée avec les anneaux et avec le
 * reflet des deux dans l'eau.
 */
shader_type spatial;
render_mode blend_mix, unshaded, shadows_disabled, depth_draw_never, cull_disabled;

#include "res://shaders/teleport.gdshaderinc"

void fragment() {
	float face = abs(dot(normalize(NORMAL), normalize(VIEW)));
	vec3 col; float a;
	naval_sphere_light(face, clamp(u_level, 0.0, 1.0), col, a);
	ALBEDO = col;
	ALPHA = a;
}
