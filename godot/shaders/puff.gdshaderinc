/* UNE BOUFFÉE — ce que les THREE.Sprite de guns.js et explosion.js dessinent :
 * un carré toujours face à l'œil, sa texture, sa couleur, son opacité, et un
 * angle à elle dans le plan de l'écran. Une instance de MultiMesh par bouffée :
 * la position est l'origine de sa transformée, la TAILLE la longueur de son
 * premier axe, l'ANGLE sa direction ; INSTANCE_CUSTOM porte la couleur (déjà
 * linéaire) et l'opacité. Écrit une fois pour les deux mélanges (puff_add,
 * puff_mix), qui ne diffèrent que par leur render_mode.
 */

#include "res://shaders/sky.gdshaderinc"

uniform sampler2D u_tex : source_color, filter_linear_mipmap, repeat_disable;

varying vec4 v_col;
varying vec3 v_world;
varying vec2 v_floor;      // x : a-t-elle un plancher ; y : sa hauteur

void vertex() {
	vec3 c = MODEL_MATRIX[3].xyz;
	vec3 ax = MODEL_MATRIX[0].xyz;
	float s = length(ax);
	float a = atan(ax.y, ax.x);
	v_col = INSTANCE_CUSTOM;
	// un sprite : les coins en espace de VUE, tournés de son angle
	vec2 q = VERTEX.xy * s;
	vec2 r = vec2(q.x * cos(a) - q.y * sin(a), q.x * sin(a) + q.y * cos(a));
	vec4 mv = VIEW_MATRIX * vec4(c, 1.0);
	mv.xy += r;
	POSITION = PROJECTION_MATRIX * mv;
	v_world = (INV_VIEW_MATRIX * mv).xyz;
	// le plancher voyage dans le troisième axe, que le sprite n'emploie pas
	v_floor = MODEL_MATRIX[2].xy;
}

void fragment() {
	vec4 t = texture(u_tex, UV);
	if (t.a * v_col.a < 0.002) discard;
	ALBEDO = v_col.rgb * t.rgb;
	ALPHA = t.a * v_col.a;
	/* LA FUMÉE SE FOND DANS L'EAU au lieu d'y être coupée : une bouffée couchée
	   sur la mer a la moitié basse de son disque sous la surface, et la mer, qui
	   écrit sa profondeur, la tranchait net le long de chaque crête — vue d'en haut,
	   une rangée d'arcs, un par bouffée. Elle s'efface donc sur deux mètres au-dessus
	   de son plancher, avant que les vagues n'aient rien à couper. */
	if (v_floor.x > 0.5) ALPHA *= smoothstep(v_floor.y - 0.3, v_floor.y + 2.0, v_world.y);
}
