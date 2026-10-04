/* CE QUE LA MER DÉPOSE SUR UNE ÉPAVE — sa passe, comme la neige a la sienne.
 *
 * DEUX COUCHES, d'après une épave réelle (référence de l'utilisateur) :
 *  - LA VASE, par LA MÊME LOI que la neige (ship_snow.gdshaderinc) : ce qui
 *    regarde le haut se couvre d'abord — pont, plats-bords, dessus des châteaux —,
 *    puis les pentes plus douces ; un dépôt clair, gris de sable ;
 *  - LA CROÛTE, qui prend PARTOUT, flancs et dessous compris, car les algues et
 *    les concrétions ne tombent pas, elles poussent : un marbré d'algues
 *    brun-vert, des plaques pâles de concrétion, quelques coulures de rouille
 *    autour des ferrures. Elle vient après le premier voile, par plaques qui
 *    grandissent et se rejoignent.
 * Les bruits sont posés dans le repère du maillage : les plaques restent où elles
 * ont poussé.
 *
 * L'ÉPAISSEUR EST PROPRE À CHAQUE ÉPAVE et non au modèle : deux galions coulés à
 * un mois d'écart partagent leurs matières (Duplicate), pas leur âge. D'où un
 * uniforme D'INSTANCE, que WreckSiteNode pousse sur chaque maillage de l'épave
 * (WreckRegistry.SiltCover). Éclairée comme la neige, avant la brume.
 */
shader_type spatial;
render_mode blend_mix, depth_draw_never, cull_disabled, shadows_disabled;

#include "res://shaders/ship_snow.gdshaderinc"

// le dépôt des plats, gris de sable (sRGB 94 8c 76)
const vec3 SILT_COLOR = vec3(0.296, 0.262, 0.181);
// la croûte : algue brun-vert, son ombre, la concrétion pâle, la rouille (sRGB 5d5a3a, 3a3a26, a39d8e, 8a5a2e)
const vec3 ALGAE = vec3(0.109, 0.102, 0.042);
const vec3 ALGAE_DARK = vec3(0.042, 0.042, 0.019);
const vec3 CRUST = vec3(0.366, 0.337, 0.270);
const vec3 RUST = vec3(0.254, 0.102, 0.027);

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
	// la vase, sur ce qui regarde le haut
	float silt = naval_snow(v_up, v_local, u_silt);

	// la croûte, partout, après le premier voile : des plaques qui grandissent
	float grow = smoothstep(0.3, 1.0, u_silt);
	float n1 = naval_snow_noise(v_local * 1.3), n2 = naval_snow_noise(v_local * 5.3), n3 = naval_snow_noise(v_local * 17.0);
	float lump = n1 * 0.6 + n2 * 0.4;
	float foul = grow * smoothstep(0.6 - 0.6 * grow, 0.85 - 0.45 * grow, lump) * 0.95;

	float a = 1.0 - (1.0 - silt) * (1.0 - foul);
	if (a <= 0.002) discard;

	/* LE FOND DE LA CROÛTE EST PÂLE — la concrétion grise d'une coque noyée —,
	   marbré de plaques d'algues aux bords doux, piqué de points sombres et de
	   coulures de rouille. Sombre partout, elle se lisait comme un camouflage. */
	vec3 crust = CRUST * (0.8 + 0.4 * n3);
	crust = mix(crust, mix(ALGAE_DARK, ALGAE, n1) * 1.8, smoothstep(0.4, 0.8, n2) * 0.65);
	crust *= mix(1.0, 0.55, smoothstep(0.72, 0.86, n3));
	crust = mix(crust, RUST, smoothstep(0.2, 0.08, n3) * smoothstep(0.45, 0.65, n2) * 0.7);
	vec3 dep = SILT_COLOR * (0.75 + 0.5 * (n1 * 0.6 + n3 * 0.4));
	// la vase par-dessus la croûte : elle se pose sur ce qui a poussé
	ALBEDO = (dep * silt + crust * foul * (1.0 - silt)) / max(a, 1e-3);
	ROUGHNESS = 1.0;
	SPECULAR = 0.15;
	ALPHA = a;
}
