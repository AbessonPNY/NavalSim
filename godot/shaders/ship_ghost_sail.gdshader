/* LA TOILE EN LOQUES d'un spectre : une carte de transparence dessinée — blanc
 * la toile, noir ce qui manque —, découpée NET pour que les trous soient des
 * trous et non un voile. Les deux faces, comme toute toile ; les deux passes de
 * ship_ghost.gdshaderinc.
 *
 * La coupe ne dépend que de la carte, pas du fondu : la page comparait un
 * seuil fixe à opacité × carte, et toute la voile disparaissait tant que
 * le fondu n'était pas complet.
 */
shader_type spatial;
render_mode blend_mix, depth_draw_always, cull_disabled, shadows_disabled;

#include "res://shaders/ship_ghost.gdshaderinc"

uniform sampler2D u_torn : filter_linear_mipmap, repeat_disable;

varying vec3 v_world;

void vertex() {
	v_world = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	POSITION = PROJECTION_MATRIX * MODELVIEW_MATRIX * vec4(VERTEX, 1.0);
	POSITION.z *= ghost_nudge();
}

void fragment() {
	if (texture(u_torn, UV).r < 0.5) discard;
	vec3 al, em; float a;
	ghost_fragment(UV, CAMERA_POSITION_WORLD, v_world, TIME, abs(dot(normalize(NORMAL), normalize(VIEW))), al, em, a);
	if (a < 0.01) discard;
	ALBEDO = al * u_pass; EMISSION = em * u_pass; ALPHA = a * u_pass;
	ROUGHNESS = 0.9;
	SPECULAR = 0.2 * u_pass;
}
