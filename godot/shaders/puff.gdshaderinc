/* UNE BOUFFÉE — ce que les THREE.Sprite de guns.js et explosion.js dessinent :
 * un carré toujours face à l'œil, sa texture, sa couleur, son opacité, et un
 * angle à elle dans le plan de l'écran. Une instance de MultiMesh par bouffée :
 * la position est l'origine de sa transformée, la TAILLE la longueur de son
 * premier axe, l'ANGLE sa direction ; INSTANCE_CUSTOM porte la couleur (déjà
 * linéaire) et l'opacité. Écrit une fois pour les deux mélanges (puff_add,
 * puff_mix), qui ne diffèrent que par leur render_mode.
 */

/* LE CIEL EN GLOBAL, comme les rubans de cordage : la bouffée ne recevait rien du
   ciel et sa couleur se calcule sur le processeur ; seule la brume en a besoin, et
   SkyNode l écrit une fois par image (PushGlobals). */
#define NAVAL_SKY_GLOBAL
#include "res://shaders/ship_haze.gdshaderinc"

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
	/* L AIR ENTRE L ŒIL ET ELLE (signalé : dans la brume, la fumée des cheminées était
	   « dessinée au-dessus », noire sur des maisons noyées). Ce qui voile prend la couleur
	   de la brume, comme un navire (hull_haze) ; ce qui brille s y éteint. */
	float hz = pow(haze_along(CAMERA_POSITION_WORLD, v_world), 1.5);
#ifdef PUFF_ADD
	ALBEDO = v_col.rgb * t.rgb * (1.0 - hz);
#else
	ALBEDO = mix(v_col.rgb * t.rgb, ship_haze_colour(CAMERA_POSITION_WORLD, v_world), hz);
#endif
	ALPHA = t.a * v_col.a;
	/* LA FUMÉE SE FOND DANS L'EAU au lieu d'y être coupée : une bouffée couchée
	   sur la mer a la moitié basse de son disque sous la surface, et la mer, qui
	   écrit sa profondeur, la tranchait net le long de chaque crête — vue d'en haut,
	   une rangée d'arcs, un par bouffée. Elle s'efface donc sur deux mètres au-dessus
	   de son plancher, avant que les vagues n'aient rien à couper. */
	if (v_floor.x > 0.5) ALPHA *= smoothstep(v_floor.y - 0.3, v_floor.y + 2.0, v_world.y);
}
