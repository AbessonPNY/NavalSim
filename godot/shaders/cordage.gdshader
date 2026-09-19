/* LES BOUTS ROMPUS, DESSINÉS — le ruban de cordage.js.
 *
 * Une ligne fait un pixel quoi qu'on lui demande : une corde en ligne est un
 * scintillement sous le pixel à toute distance qui compte. Chaque corde est donc
 * un RUBAN, une bande de triangles tournée vers l'œil, de sa vraie largeur en
 * mètres avec un plancher en pixels — plus large que le pas d'échantillonnage,
 * jamais plus claire. Toutes les cordes de la flotte en UN appel.
 *
 * Le maillage ne porte que des numéros : VERTEX.x le point le long de la corde,
 * VERTEX.y la corde, VERTEX.z le bord (−1, +1). Les points eux-mêmes arrivent
 * chaque image dans une texture de données (x, y, z, et en w l'opacité — 0 : pas
 * dessinée), écrite par CordageNode à partir du noyau.
 */
shader_type spatial;
render_mode unshaded, blend_mix, depth_draw_never, cull_disabled, shadows_disabled, fog_disabled;

#include "res://shaders/ship_haze.gdshaderinc"

uniform sampler2D u_pts : filter_nearest, repeat_disable;
uniform int u_p = 10;
uniform float u_width = 0.07;      // un cordage de quatre pouces, de diamètre
uniform float u_min_px = 1.3;      // et jamais plus mince que ceci à l'écran
uniform float u_px_scale = 0.0018; // mètres par pixel à un mètre de l'œil
uniform vec3 u_color = vec3(0.427, 0.361, 0.282);   // chanvre goudronné, en valeur d'écran

varying float v_fade;
varying vec3 v_world;

void vertex() {
	int j = int(VERTEX.x + 0.5), r = int(VERTEX.y + 0.5);
	float side = VERTEX.z;
	vec4 p = texelFetch(u_pts, ivec2(j, r), 0);
	v_fade = p.w;
	v_world = p.xyz;
	if (p.w <= 0.005) {
		// pas dessinée : repoussée hors du volume de vue, rien à tramer
		POSITION = vec4(2.0, 2.0, 2.0, 1.0);
	} else {
		vec3 po = texelFetch(u_pts, ivec2(max(j - 1, 0), r), 0).xyz;
		vec3 pn = texelFetch(u_pts, ivec2(min(j + 1, u_p - 1), r), 0).xyz;
		vec3 tg = pn - po;
		tg = dot(tg, tg) < 1e-10 ? vec3(0.0, 1.0, 0.0) : normalize(tg);
		// posé à l'origine, transformée identité : VIEW_MATRIX seul mène en vue
		vec4 mv = VIEW_MATRIX * vec4(p.xyz, 1.0);
		vec3 tv = normalize((VIEW_MATRIX * vec4(tg, 0.0)).xyz);
		vec3 eye = normalize(mv.xyz);    // l'œil EST l'origine en espace de vue
		vec3 dir = cross(tv, eye);
		float l = length(dir);
		dir = l < 1e-4 ? vec3(1.0, 0.0, 0.0) : dir / l;
		// mètres par pixel à cette profondeur : le plancher est un vrai nombre de pixels
		float mpp = max(0.001, -mv.z) * u_px_scale;
		float w2 = max(u_width * 0.5, u_min_px * 0.5 * mpp);
		mv.xyz += dir * w2 * side;
		POSITION = PROJECTION_MATRIX * mv;
	}
}

void fragment() {
	// ce qui réfléchit suit la lumière : la couleur arrive déjà réglée sur l'horizon
	float h = haze_along(CAMERA_POSITION_WORLD, v_world);
	ALBEDO = mix(naval_display(u_color), ship_haze_colour(CAMERA_POSITION_WORLD, v_world), h);
	ALPHA = v_fade;
}
