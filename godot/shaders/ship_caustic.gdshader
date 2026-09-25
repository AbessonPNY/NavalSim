/* LES CAUSTIQUES SUR UNE CARÈNE — la passe des modèles .glb.
 *
 * Le même réseau de lumière que sur le sable, sur ce qui est mouillé : une coque
 * sous la flottaison est dans l'eau comme le fond l'est, et rien ne justifie
 * qu'elle en soit exempte. C'est même là qu'on le voit le mieux, parce qu'un
 * bordé sombre et lisse porte les nervures là où le sable les noie.
 *
 * ELLE AJOUTE, ELLE NE MULTIPLIE PAS, et c'est un écart assumé. Sur le fond, le
 * facteur multiplie l'albédo, ce qui est l'opération juste — la caustique déplace
 * la lumière, elle n'en donne pas. Ici la matière est celle de l'artiste, on ne
 * la réécrit pas, et une passe multiplicative n'est pas dessinée sous Forward+
 * (mesuré ; voir land.gdshader). Reste l'addition, qui rend les nervures claires
 * et perd ce qu'elles ôtent entre elles. Sur une carène le marché est bon : ce
 * qu'on regarde, ce sont les traits de lumière qui courent sur le bordé.
 *
 * LA PROFONDEUR EST CELLE DU POINT LUI-MÊME : un point de coque à un mètre sous
 * la surface reçoit la caustique d'un mètre d'eau, comme un sable à un mètre. La
 * fonction partagée s'en charge — elle rend exactement un au-dessus de l'eau, et
 * la ligne de flottaison se dessine donc toute seule, à la vague près.
 */
shader_type spatial;
render_mode blend_add, depth_draw_never, cull_disabled, shadows_disabled, fog_disabled;

#include "res://shaders/gerstner.gdshaderinc"
#include "res://shaders/shelter.gdshaderinc"
#include "res://shaders/sky.gdshaderinc"
#include "res://shaders/ride.gdshaderinc"
#include "res://shaders/caustics.gdshaderinc"

/* Ce que la nervure ALLUME sur le bordé. Ni la couleur de la coque ni la
   lumière du soleil ne sont connues d'une passe qui ajoute : on pose un gris
   clair, que le cadran règle. Plus haut : un bordé qui luit comme du métal. */
uniform float u_caustic_hull = 0.42;

varying vec3 v_world;

void vertex() {
	v_world = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	POSITION = PROJECTION_MATRIX * MODELVIEW_MATRIX * vec4(VERTEX, 1.0);
	// la même avance que la neige, contre le scintillement
	POSITION.z *= 1.00005;
}

void fragment() {
	/* LE TRI D'ABORD, et il est large : rien de ce qui est à plus de trois mètres
	   et demi au-dessus du niveau moyen n'est jamais mouillé, ce qui écarte d'un
	   coup toute la mâture, les hauts et les ponts — c'est-à-dire l'essentiel des
	   pixels d'un navire. Sans lui, un gréement paierait dix-huit vagues et six
	   lectures de ride par pixel pour rendre exactement un. */
	if (v_world.y > 3.5) discard;

	float dist = length(v_world - CAMERA_POSITION_WORLD);
	vec3 k = caustiques(v_world, dist, max(0.05, dist * 0.0015));
	vec3 add = max(k - 1.0, vec3(0.0)) * u_caustic_hull;
	if (add.r + add.g + add.b < 0.002) discard;
	ALBEDO = add;
	ALPHA = 1.0;
}
