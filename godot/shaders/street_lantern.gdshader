/* LES LANTERNES DES RUES (demandé : « des lanternes de nuit dans les rues, qui
 * dessineraient un peu les contours de la ville, jusqu'à deux heures du matin »).
 *
 * Aucune n'est une lumière : trois cents OmniLight3D coûteraient plus que la
 * ville, et la règle est de ne jamais en ajouter en jeu. Chaque lanterne est
 * trois panneaux d'un MultiMesh (TownNode.Lamps) : le halo de la flamme en
 * mètres, son repère à taille d'écran — ce qui fait, de la rade, le contour de
 * la ville —, et la flaque de lumière qu'elle jette sur le pavé dessous.
 *
 * Tout le temps est ici. Chaque instance porte, dans INSTANCE_CUSTOM, sa phase
 * de flamme (r), l'heure après minuit où on la souffle (g) et la nuit qu'il
 * faut pour qu'on l'allume (b) : l'allumeur fait sa tournée au crépuscule, et
 * les lanternes s'éteignent une à une vers deux heures, pas d'un seul coup.
 * La démo ne pousse que la nuit, l'heure et l'horloge.
 *
 * Une maison retirée dans l'éditeur emporte ses lanternes : l'échelle de
 * l'instance est celle de la maison, et une échelle nulle n'est rien ; une
 * autre échelle ne change que la place de la lanterne, pas sa taille.
 */
shader_type spatial;
render_mode unshaded, blend_add, depth_draw_never, cull_disabled, shadows_disabled, fog_disabled;

#include "res://shaders/sky.gdshaderinc"
#include "res://shaders/lantern.gdshaderinc"

uniform vec3 u_color = vec3(1.0, 0.812, 0.478);   // 0xffcf7a, en sRGB, comme les fanaux
uniform float u_size = 1.0;     // en mètres ; en fraction d'écran si u_fixed ; le rayon de la flaque si u_ground
uniform float u_fixed = 0.0;
uniform float u_ground = 0.0;   // 1 : la flaque, couchée au sol, u_drop sous la flamme
uniform float u_drop = 3.0;
uniform float u_gain = 1.0;
uniform float u_night = 0.0;    // SkyCore.Night, le même chiffre que les fanaux
uniform float u_hour = 12.0;    // l'heure du jour, de 0 à 24
uniform float u_time = 0.0;

varying float v_on;

void vertex() {
	float seed = INSTANCE_CUSTOM.r;
	/* ALLUMÉE du crépuscule (sa nuit à elle) jusqu'à son heure après minuit ;
	   l'après-midi compte comme le soir. Entre deux heures et l'aube, tout est
	   soufflé — c'est l'heure où l'huile manque et où l'allumeur dort. */
	float lit = smoothstep(INSTANCE_CUSTOM.b, INSTANCE_CUSTOM.b + 0.06, u_night);
	float late = u_hour < 12.0 ? 1.0 - smoothstep(INSTANCE_CUSTOM.g, INSTANCE_CUSTOM.g + 0.02, u_hour) : 1.0;
	/* UNE MÈCHE DANS LA CORNE, qui respire : deux battements lents qui se
	   croisent, jamais un clignotement. La flaque respire avec elle. */
	float breath = 0.90 + 0.10 * sin(u_time * (5.0 + 3.0 * seed) + seed * 40.0) * sin(u_time * 1.7 + seed * 17.0);
	v_on = lit * late * breath;

	float sc = length(MODEL_MATRIX[0].xyz);
	if (v_on <= 0.002 || sc < 1e-4) {
		// soufflée : un point hors du champ, rien n'est tramé
		POSITION = vec4(0.0, 0.0, 2.0, 1.0);
	} else if (u_ground > 0.5) {
		/* l échelle de la maison place la lanterne, elle ne la grossit pas : une
		   maison agrandie n a pas de plus grosse lanterne */
		POSITION = PROJECTION_MATRIX * MODELVIEW_MATRIX
			* vec4(vec3(VERTEX.x * u_size, -u_drop, VERTEX.z * u_size) / sc, 1.0);
	} else {
		/* face à l'œil, comme lantern_glow ; à taille fixe le décalage suit la
		   profondeur : même taille à l'écran à toute distance */
		vec4 c = MODELVIEW_MATRIX * vec4(0.0, 0.0, 0.0, 1.0);
		float s = u_size * (u_fixed > 0.5 ? -c.z : 1.0);
		POSITION = PROJECTION_MATRIX * vec4(c.xyz + vec3(VERTEX.xy * s, 0.0), 1.0);
	}
}

void fragment() {
	float r = length(UV - 0.5) * 2.0;
	vec3 shown;
	if (u_ground > 0.5) {
		// la lumière d'une flamme sur le pavé : forte dessous, qui s'efface en carré
		float f = max(1.0 - r * r, 0.0);
		shown = u_color * vec3(1.0, 0.86, 0.62) * f * f;
	} else {
		vec4 g = glow(r);
		shown = u_color * g.rgb * g.a;
	}
	ALBEDO = naval_display(shown * v_on * u_gain);
	ALPHA = 1.0;
}
