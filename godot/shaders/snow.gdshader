/* LA NEIGE — snow.js : le réseau de la pluie, qui tombe lentement.
 *
 * Le même tour que rain.gdshader, pour les mêmes raisons : une boîte de graines
 * repliée autour de l'œil par un modulo, aucun travail processeur, une vraie
 * parallaxe. Ce qui change est ce que l'œil lit comme de la neige : un FLOCON
 * rond et non une traînée, une chute d'un mètre par seconde, et un balancement —
 * chaque flocon erre de côté sur son propre cercle lent, et le vent emporte le
 * tout.
 *
 * Elle RÉFLÉCHIT : sa couleur est celle de la lumière (la démo lui donne celle
 * de l'horizon), gris-bleu la nuit, jamais lumineuse. Sa graine est dans UV.x.
 */
shader_type spatial;
render_mode unshaded, blend_mix, depth_draw_never, cull_disabled, shadows_disabled, fog_disabled;

#include "res://shaders/sky.gdshaderinc"

uniform vec3 u_eye;
uniform vec3 u_drift = vec3(0.0, -1.1, 0.0);
uniform float u_time;
uniform float u_box = 60.0;
uniform float u_amount = 0.0;
uniform float u_scale = 600.0;                     // la hauteur de l'image, en pixels
uniform vec3 u_color = vec3(0.910, 0.933, 0.949);   // 0xe8eef2, en valeur d'écran

varying float v_fade;

void vertex() {
	float seed = UV.x;
	// chaque flocon se balance sur son propre cercle lent, en tombant
	vec3 sway = vec3(sin(u_time * 0.7 + seed), 0.0, cos(u_time * 0.55 + seed * 1.3)) * 0.6;
	vec3 p = VERTEX + u_drift * u_time + sway;
	vec3 rel = mod(p - u_eye + u_box * 0.5, u_box) - u_box * 0.5;
	float d = length(rel);
	v_fade = u_amount * (1.0 - smoothstep(u_box * 0.24, u_box * 0.48, d));
	// une chute légère a moins de flocons : le réseau s'éclaircit par la graine
	if (fract(seed * 0.618) > u_amount) v_fade = 0.0;
	VERTEX = u_eye + rel;
	float depth = -(MODELVIEW_MATRIX * vec4(VERTEX, 1.0)).z;
	POINT_SIZE = clamp(u_scale * (0.045 + 0.03 * fract(seed)) / max(0.5, depth), 1.0, 14.0);
}

void fragment() {
	if (v_fade <= 0.003) discard;
	vec2 q = POINT_COORD * 2.0 - 1.0;
	float r = dot(q, q);
	if (r > 1.0) discard;
	ALBEDO = naval_display(u_color);
	ALPHA = v_fade * (1.0 - r) * 0.9;
}
