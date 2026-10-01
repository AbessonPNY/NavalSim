/* L'OCULAIRE — la passe de spyglass.js, posée sur l'image FINIE.
 *
 * La page recopie l'écran dans une texture et le redessine à travers le verre ;
 * ici la texture d'écran d'un canvas_item est exactement cela : l'image déjà
 * rendue, déjà encodée, telle qu'elle s'affiche. Les formules sont donc celles
 * de la page, sur les mêmes valeurs. Seul l'axe vertical change de sens (y vers
 * le bas ici) : le verre est lu retourné pour que la fêlure parte du même bord.
 */
shader_type canvas_item;
render_mode unshaded;

uniform sampler2D u_scene : hint_screen_texture, filter_linear, repeat_disable;
uniform sampler2D u_glass : filter_linear_mipmap;      // r rayures, g trace, b poussière, a fêlure

/* L'OCULAIRE, en hauteurs d'écran : un peu plus que la moitié, de sorte que le
   cercle DÉBORDE en haut et en bas — l'œil collé au tube ne voit pas son bord
   tout autour, seulement sur les côtés. Demandé : toute la hauteur. */
const float R = 0.53;

#include "res://shaders/beads.gdshaderinc"

uniform float u_rain = 0.0;    // la pluie qui tombe, 0 à 1 ; la neige n'y laisse rien
uniform float u_time = 0.0;

// un canal de la scène à travers la lentille, au barillet k
float lens_tap(vec2 p, float k, int c, float soft, vec2 res) {
	vec2 aspect = vec2(res.x / res.y, 1.0);
	float r2 = dot(p, p) / (R * R);
	/* Divisé par (1 + k) pour que le bord retombe sur le bord : le barillet est
	   dans la FORME de la courbe — le milieu tenu grand, le bord tassé —, pas
	   dans sa portée. */
	vec2 q = p * (1.0 + k * r2) / (1.0 + k);
	vec2 uv = q / aspect + 0.5;
	/* LE BORD DE L'IMAGE EST TENU, plus rendu noir : le cercle déborde l'écran
	   en haut et en bas, donc le verre y regarde hors de l'image — et une goutte
	   qui plie la vue vers le bord aussi. Du noir y faisait des taches. */
	uv = clamp(uv, vec2(0.0005), vec2(0.9995));
	vec2 px = soft / res;
	vec4 s = texture(u_scene, uv) * 0.4
	       + texture(u_scene, uv + vec2( px.x,  px.y)) * 0.15
	       + texture(u_scene, uv + vec2(-px.x,  px.y)) * 0.15
	       + texture(u_scene, uv + vec2( px.x, -px.y)) * 0.15
	       + texture(u_scene, uv + vec2(-px.x, -px.y)) * 0.15;
	return c == 0 ? s.r : (c == 1 ? s.g : s.b);
}

void fragment() {
	vec2 res = 1.0 / SCREEN_PIXEL_SIZE;
	vec2 p = (SCREEN_UV - 0.5) * vec2(res.x / res.y, 1.0);
	float r = length(p) / R;                     // 0 au centre, 1 au bord
	if (r > 1.02) { COLOR = vec4(0.0, 0.0, 0.0, 1.0); }
	else {
		vec4 g = texture(u_glass, vec2(p.x, -p.y) / R * 0.5 + 0.5);

		/* Une fêlure plie ce qui est derrière elle : l'image saute de côté à
		   travers la ligne, et c'est ce qui la lit comme du verre cassé plutôt
		   que comme un cheveu sur la lentille. */
		p += normalize(p + 1e-4) * g.a * 0.0035;

		/* LA PLUIE SUR L'OBJECTIF. Prise dans le repère de la LENTILLE (p / R) et
		   non de l'écran : les gouttes sont sur le verre, elles ont sa taille. Une
		   perle est une lentille : elle regarde l'image à côté, et c'est ce qui la
		   lit comme de l'eau plutôt que comme une tache. */
		vec3 drop = vec3(0.0);
		if (u_rain > 0.001) {
			vec2 lp = p / R;
			vec3 d1 = rain_beads(lp, 5.0, 0.0, u_time, u_rain);
			vec3 d2 = rain_beads(lp, 11.0, 9.1, u_time * 1.3, u_rain);
			drop = d1.z > d2.z * 0.8 ? d1 : d2 * vec3(0.6, 0.6, 0.8);
			p += drop.xy * R * 0.10;
		}

		/* BARILLET, et couleur latérale : une lentille unique plie le bleu plus
		   fort que le rouge, les franges s'ouvrent vers le bord. Et le bord est
		   mou : le champ est courbe, seul le milieu est net. */
		float soft = 0.6 + 2.8 * r * r;
		vec3 col = vec3(lens_tap(p, 0.30, 0, soft, res),
		                lens_tap(p, 0.33, 1, soft, res),
		                lens_tap(p, 0.37, 2, soft, res));

		float lum = dot(col, vec3(0.30, 0.59, 0.11));
		// un pouce gras : l'image laiteuse perd son contraste
		col = mix(col, vec3(lum) * 0.85 + 0.10, g.g * 0.30);
		// les rayures prennent la lumière qui passe : elles s'éteignent la nuit
		col += (g.r + g.a * 0.8) * (0.03 + 0.55 * lum);
		// la poussière est une ombre dessus
		col *= 1.0 - g.b * 0.75;

		// le bord d'une goutte prend la lumière, son cœur assombrit un peu
		col += vec3(0.25, 0.32, 0.38) * pow(drop.z, 3.0) * 0.18 * (0.3 + lum);
		col *= 1.0 - 0.05 * drop.z;

		// la lumière tombe vers le diaphragme, cos⁴ et un peu plus
		col *= mix(0.45, 1.0, pow(max(0.0, 1.0 - r * r * 0.85), 1.6));

		// le bord du tube : un noir doux, et l'éclat de son biseau
		float rim = smoothstep(1.0, 0.975, r);
		float bevel = exp(-pow((r - 0.965) / 0.012, 2.0)) * 0.10;
		COLOR = vec4(col * rim + vec3(bevel * (0.4 + lum)), 1.0);
	}
}
