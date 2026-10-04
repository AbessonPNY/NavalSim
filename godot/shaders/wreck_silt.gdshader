/* LA VASE QUI SE DÉPOSE SUR UNE ÉPAVE — sa passe, comme la neige a la sienne, et
 * par LA MÊME LOI (ship_snow.gdshaderinc) : ce qui regarde le haut se couvre
 * d'abord — pont, plats-bords, dessus des châteaux —, puis les pentes plus
 * douces à mesure que la couche s'épaissit. Le bord est cassé par le même bruit,
 * posé dans le repère du maillage.
 *
 * L'ÉPAISSEUR EST PROPRE À CHAQUE ÉPAVE et non au modèle : deux galions coulés à
 * un mois d'écart partagent leurs matières (Duplicate), pas leur âge. D'où un
 * uniforme D'INSTANCE, que WreckSiteNode pousse sur chaque maillage de l'épave
 * (WreckRegistry.SiltCover). Éclairée comme la neige, avant la brume.
 */
shader_type spatial;
render_mode blend_mix, depth_draw_never, cull_disabled, shadows_disabled;

#include "res://shaders/ship_snow.gdshaderinc"

// la vase d'une rade, grise et un peu verte (sRGB 70 6a 56) : de la famille du fond vaseux, plus claire — un dépôt frais
const vec3 SILT_COLOR = vec3(0.162, 0.144, 0.092);

instance uniform float u_silt = 0.0;

varying vec3 v_local;
varying float v_up;

void vertex() {
	v_local = VERTEX;
	v_up = normalize((MODEL_MATRIX * vec4(NORMAL, 0.0)).xyz).y;
	POSITION = PROJECTION_MATRIX * MODELVIEW_MATRIX * vec4(VERTEX, 1.0);
	POSITION.z *= 1.00005;
}

void fragment() {
	float s = naval_snow(v_up, v_local, u_silt);
	if (s <= 0.002) discard;
	// une vase fine se marbre : plus chargée dans les creux du bruit
	float n = naval_snow_noise(v_local * 0.9) * 0.6 + naval_snow_noise(v_local * 4.3) * 0.4;
	ALBEDO = SILT_COLOR * (0.6 + 0.8 * n);
	ROUGHNESS = 1.0;
	SPECULAR = 0.15;
	ALPHA = s;
}
