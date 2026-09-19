/* LA BULLE AUTOUR DE LA BOUTEILLE — le halo de flotsam.js, demandé parce
 * qu'une bouteille est introuvable dans la houle.
 *
 * Une bulle de savon plutôt qu'une lampe : presque rien de face, vive sur son
 * bord (Fresnel), avec l'arc-en-ciel d'un film mince qui marche avec l'angle et,
 * lentement, avec le temps, un éclat de soleil et quelques paillettes qui
 * rampent sur sa peau. Additive et sans écrire de profondeur, donc elle ne cache
 * jamais ce qui est dedans ni derrière ; la profondeur est LUE, si bien que la
 * mer la coupe à la flottaison. Elle DONNE sa lumière au lieu d'en renvoyer —
 * c'est la seule chose ici qui doive luire, et la nuit est justement le moment
 * où il faut la trouver.
 */
shader_type spatial;
render_mode unshaded, blend_add, depth_draw_never, cull_disabled, shadows_disabled, fog_disabled;

#include "res://shaders/sky.gdshaderinc"

uniform vec3 u_color : source_color = vec3(0.66, 0.925, 1.0);
uniform float u_intensity = 1.0;
uniform float u_time = 0.0;

varying vec3 v_world;
varying vec3 v_normal;

void vertex() {
	v_world = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	v_normal = normalize((MODEL_MATRIX * vec4(NORMAL, 0.0)).xyz);
}

void fragment() {
	vec3 V = normalize(CAMERA_POSITION_WORLD - v_world);
	vec3 N = normalize(v_normal);
	float front = FRONT_FACING ? 1.0 : 0.35;      // la paroi du fond, vue à travers celle de devant
	float edge = 1.0 - abs(dot(N, V));
	float rim = pow(edge, 2.6);
	// film mince : la teinte marche avec l'angle et, lentement, avec le temps
	vec3 film = 0.5 + 0.5 * cos(6.2832 * (vec3(0.0, 0.33, 0.67) + edge * 1.4 + u_time * 0.04));
	vec3 col = mix(u_color, film, 0.35);
	float pulse = 0.85 + 0.15 * sin(u_time * 1.7);
	// un éclat de soleil et une lumière de fenêtre, sur la paroi de devant seulement
	vec3 R = reflect(-V, N);
	float glint = FRONT_FACING
		? pow(max(dot(R, normalize(u_sun)), 0.0), 80.0) * 1.6
		  + pow(max(dot(N, normalize(vec3(-0.45, 0.8, 0.4))), 0.0), 40.0) * 0.25 * (1.0 - edge)
		: 0.0;
	// des paillettes qui rampent sur sa peau
	float sp = sin(v_world.x * 6.1 + u_time * 1.3) * sin(v_world.y * 7.3 - u_time * 1.1) * sin(v_world.z * 6.7 + u_time * 0.9);
	float spark = pow(max(sp, 0.0), 14.0) * 1.4;
	vec3 c = col * (rim * 1.25 * pulse + 0.035) * front + vec3(glint) + u_color * spark * front;
	ALBEDO = naval_display(c * u_intensity);
	ALPHA = 1.0;
}
