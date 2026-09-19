/* Le bois et le métal d'un spectre : la règle, et ses deux passes, sont dans
 * ship_ghost.gdshaderinc. Il ne porte pas d'ombre — une ombre franche sous une
 * chose qu'on voit au travers la contredirait.
 */
shader_type spatial;
render_mode blend_mix, depth_draw_always, cull_back, shadows_disabled;

#include "res://shaders/ship_ghost.gdshaderinc"

varying vec3 v_world;

void vertex() {
	v_world = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	POSITION = PROJECTION_MATRIX * MODELVIEW_MATRIX * vec4(VERTEX, 1.0);
	POSITION.z *= ghost_nudge();
}

void fragment() {
	vec3 al, em; float a;
	ghost_fragment(UV, CAMERA_POSITION_WORLD, v_world, TIME, abs(dot(normalize(NORMAL), normalize(VIEW))), al, em, a);
	// ce qui est effacé n'écrit pas de profondeur : la mer se voit au travers
	if (a < 0.01) discard;
	ALBEDO = al * u_pass; EMISSION = em * u_pass; ALPHA = a * u_pass;
	ROUGHNESS = 0.8;
	SPECULAR = 0.3 * u_pass;
}
