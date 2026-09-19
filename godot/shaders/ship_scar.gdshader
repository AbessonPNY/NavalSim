/* LES BLESSURES POSÉES SUR UN MODÈLE .glb — leur passe, comme la neige et la
 * brume ont la leur ; la règle est dans ship_scar.gdshaderinc.
 *
 * Redessinée par-dessus ses matières, ÉCLAIRÉE comme elles, et réduite à un seul
 * mélange : la lèvre qui assombrit et le bois brut qui recouvre donnent ensemble
 * une couleur et une opacité, si bien que le résultat est exactement celui de la
 * page — base·(1 − lèvre), puis le chêne à la part de bois brut, puis l'impact
 * peint par-dessus. Première de la chaîne : la neige et l'air sont devant elle.
 */
shader_type spatial;
render_mode blend_mix, depth_draw_never, cull_disabled, shadows_disabled;

#include "res://shaders/ship_scar.gdshaderinc"

// le monde → son repère, relu à chaque image : les marques restent où elles ont frappé quand elle roule
uniform mat4 u_ship_inv;

varying vec3 v_ship;

void vertex() {
	v_ship = (u_ship_inv * MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	POSITION = PROJECTION_MATRIX * MODELVIEW_MATRIX * vec4(VERTEX, 1.0);
	POSITION.z *= 1.00003;          // un rien devant, contre le scintillement
}

void fragment() {
	if (u_scar_count == 0) discard;
	float raw, lip; vec4 paint;
	ship_scars(v_ship, raw, lip, paint);
	// base·(1 − lèvre)·(1 − brut) + chêne·brut, puis l'impact peint : un seul mélange
	float a0 = 1.0 - (1.0 - lip) * (1.0 - raw);
	float a = 1.0 - (1.0 - a0) * (1.0 - paint.a);
	if (a < 0.002) discard;
	ALBEDO = (SCAR_OAK * raw * (1.0 - paint.a) + paint.rgb * paint.a) / a;
	ROUGHNESS = 0.85;
	SPECULAR = 0.3;
	ALPHA = a;
}
