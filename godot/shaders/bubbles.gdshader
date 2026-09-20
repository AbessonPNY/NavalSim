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
render_mode unshaded, blend_add, depth_draw_never, cull_disabled, shadows_disabled, fog_disabled;

#include "res://shaders/sky.gdshaderinc"

uniform vec3 u_color : source_color = vec3(0.88, 0.94, 0.98);
// vives : l'eau de la passe sous-marine en mange les trois quarts à dix mètres
uniform float u_gain = 2.0;

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
	float rim = pow(r, 2.2) * smoothstep(1.0, 0.72, r);
	float core = 0.16 * (1.0 - r * r);
	// un reflet en haut à gauche, comme toutes en portent un
	float glint = pow(max(0.0, 1.0 - length(d - vec2(-0.16, 0.16)) * 6.0), 2.0) * 0.5;
	float a = rim * 0.9 + core + glint;
	ALBEDO = naval_display(u_color * a * u_gain);
	ALPHA = 1.0;
}
