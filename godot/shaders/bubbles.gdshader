/* UNE BULLE D'AIR DANS L'EAU. Ce n'est pas une boule blanche : c'est une
 * SURFACE qui sépare l'air de l'eau, presque invisible de face et vive sur son
 * bord, où le regard rase la paroi et où la réflexion totale renvoie tout. Un
 * panneau face à l'œil suffit donc, à condition que l'éclat vienne du BORD.
 *
 * Elles ne s'éclairent pas : elles renvoient ce qui passe. Additives et sans
 * écrire de profondeur, mais la profondeur est LUE, si bien que la coque qui
 * coule les cache quand elles passent derrière elle.
 */
shader_type spatial;
render_mode unshaded, blend_mix, depth_draw_never, cull_disabled, shadows_disabled, fog_disabled;

#include "res://shaders/sky.gdshaderinc"

uniform vec3 u_color : source_color = vec3(0.93, 0.96, 0.99);
// vives : l'eau de la passe sous-marine en mange les trois quarts à dix mètres
uniform float u_gain = 1.0;
uniform float u_opacity = 0.85;   // presque opaque : de l'air dans de l'eau ne se traverse pas

varying float v_seed;

void vertex() {
	v_seed = INSTANCE_CUSTOM.x;
	// un panneau toujours face à l'œil, à la taille que lui donne son instance
	vec4 c = MODELVIEW_MATRIX * vec4(0.0, 0.0, 0.0, 1.0);
	float s = length(MODEL_MATRIX[0].xyz);
	POSITION = PROJECTION_MATRIX * vec4(c.xyz + vec3(VERTEX.xy * s, 0.0), 1.0);
}

void fragment() {
	vec2 d = UV - 0.5;
	float r = length(d) * 2.0;
	if (r > 1.0) discard;
	/* Le bord brille et le centre reste pâle : c'est ainsi qu'on voit une bulle.
	   Un anneau trop mince et trop bleu la faisait lire comme du néon — relevé sur
	   une capture ; le bord est donc plus large et le cœur moins vide. */
	/* BLANCHE ET PRESQUE OPAQUE. Une bulle d'air dans l'eau n'est pas un verre
	   qu'on traverse : sa paroi renvoie tout, et elle se lit comme une perle
	   blanche — un peu plus sombre au bord, vive au milieu, avec le reflet que
	   toutes portent en haut à gauche. Un premier jet additif en faisait des
	   anneaux de néon. */
	float edge = smoothstep(1.0, 0.86, r);
	float body = 0.72 + 0.28 * (1.0 - r * r);
	float glint = pow(max(0.0, 1.0 - length(d - vec2(-0.18, 0.18)) * 5.0), 2.0) * 0.5;
	ALBEDO = naval_display(u_color * (body + glint) * u_gain);
	ALPHA = edge * u_opacity;
}
