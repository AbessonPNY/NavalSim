/* LA NEIGE POSÉE SUR UN MODÈLE .glb — sa passe, comme la brume a la sienne.
 *
 * Les matériaux de l'artiste restent tels quels ; celle-ci redessine la même
 * géométrie par-dessus, ÉCLAIRÉE — de la neige qui ne prendrait pas la lumière
 * se lirait comme de la peinture blanche la nuit —, à l'opacité de la couche.
 * Avant la brume dans la chaîne : l'air est devant la neige aussi. Avancée d'un
 * vingt-millième de sa distance contre le scintillement, moitié de la brume, qui
 * passe donc devant elle.
 */
shader_type spatial;
render_mode blend_mix, depth_draw_never, cull_disabled, shadows_disabled;

#include "res://shaders/ship_snow.gdshaderinc"

uniform float u_snow = 0.0;

varying vec3 v_local;
varying float v_up;

void vertex() {
	v_local = VERTEX;
	v_up = normalize((MODEL_MATRIX * vec4(NORMAL, 0.0)).xyz).y;
	POSITION = PROJECTION_MATRIX * MODELVIEW_MATRIX * vec4(VERTEX, 1.0);
	POSITION.z *= 1.00005;
}

void fragment() {
	float s = naval_snow(v_up, v_local, u_snow);
	if (s <= 0.002) discard;
	ALBEDO = NAVAL_SNOW_COLOR;
	ROUGHNESS = 0.9;
	SPECULAR = 0.3;
	ALPHA = s;
}
