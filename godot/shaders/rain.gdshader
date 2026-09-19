/* LE RIDEAU DE PLUIE — rain.js.
 *
 * Pas une goutte simulée : un RÉSEAU de gouttes, infini dans tous les sens, que
 * l'œil traverse. Chaque goutte tient un point fixe, et le vertex shader replie
 * `graine − œil` dans une seule boîte par un modulo avant de rajouter l'œil :
 * une boîte de pluie toujours centrée sur qui regarde, avec une vraie parallaxe
 * — une goutte à un mètre file, une à trente bouge à peine —, et pas un octet de
 * travail processeur par image. Le maillage est posé à l'origine, transformée
 * identité : sa géométrie ne veut rien dire sans ce shader.
 *
 * En SEGMENTS et non en points : ce que l'œil reconnaît comme de la pluie, c'est
 * la TRAÎNÉE, sa longueur et son inclinaison, et les deux sortent du vecteur de
 * chute. Deux sommets par goutte sur la MÊME graine (UV.x : 0 la tête, 1 la
 * queue) : la traînée pousse après le repli, et une goutte à cheval sur le bord
 * de la boîte n'est jamais coupée en deux.
 */
shader_type spatial;
render_mode unshaded, blend_mix, depth_draw_never, cull_disabled, shadows_disabled, fog_disabled;

#include "res://shaders/sky.gdshaderinc"

uniform vec3 u_eye;
uniform vec3 u_slant = vec3(0.0, -7.4, 0.0);   // mètres par seconde
uniform float u_time;
uniform float u_box = 74.0;
uniform float u_len = 1.2;
uniform float u_amount = 0.0;
uniform vec3 u_color = vec3(0.725, 0.784, 0.831);   // 0xb9c8d4, en valeur d'écran : voir fragment

varying float v_fade;

void vertex() {
	// tomber d'abord, replier ensuite : dans l'autre ordre une goutte sortirait de la boîte sans y revenir
	vec3 p = VERTEX + u_slant * u_time;
	vec3 rel = mod(p - u_eye + u_box * 0.5, u_box) - u_box * 0.5;
	VERTEX = u_eye + rel - normalize(u_slant) * (UV.x * u_len);
	// éclaircie vers les coins de la boîte, pas coupée net : un bord franc serait un mur de gouttes
	float d = length(rel);
	v_fade = u_amount * (1.0 - smoothstep(u_box * 0.26, u_box * 0.48, d));
}

void fragment() {
	if (v_fade <= 0.003) discard;
	// la page écrit sa couleur sans encodage : décodée ici, comme tout ce qu'elle dessine en shader
	ALBEDO = naval_display(u_color);
	ALPHA = v_fade * 0.5;
}
