/* CE QUI RESPIRE LE MÊME AIR QUE LA MER — la coque et les espars procéduraux.
 *
 * Tout ce que l'œil voit à travers l'air doit employer la MÊME brume, et non un
 * second modèle de brouillard : la mer et le navire doivent s'effacer au même
 * rythme, sinon le bâtiment reste net contre une mer délavée et l'illusion
 * s'effondre. La brume et sa couleur viennent de ship_haze.gdshaderinc, que
 * les modèles .glb lisent aussi.
 *
 * Le gros temps n'y touche PAS l'albédo : il assombrit le navire par sa
 * LUMIÈRE — soleil rabattu, ambiante tirée d'un ciel fermé —, comme l'original.
 * Ce qui réfléchit suit la lumière ; un facteur de plus ici le compterait deux
 * fois, et c'est ce que faisait la première version.
 */
shader_type spatial;
render_mode cull_disabled;

#include "res://shaders/ship_haze.gdshaderinc"

uniform vec3 u_albedo : source_color = vec3(0.22, 0.26, 0.31);
uniform float u_roughness : hint_range(0.0, 1.0) = 0.72;

varying vec3 v_world;

void vertex() {
	v_world = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
}

/* LA BRUME SE POSE DANS fragment(), ET C'EST EXACT PLUTÔT QU'APPROCHÉ.
 *
 * `CAMERA_POSITION_WORLD` n'existe pas dans `light()`, là où l'on aurait la
 * couleur déjà éclairée. Mais ce qu'on veut est
 *
 *     éclairé·(1−h) + ciel·h
 *
 * et l'éclairement étant LINÉAIRE en albédo, multiplier l'albédo par (1−h) et
 * poser ciel·h en émission donne exactement cela — le reflet spéculaire,
 * lui, ne passe pas par l'albédo, d'où le même facteur sur SPECULAR. */
void fragment() {
	float hz = haze_along(CAMERA_POSITION_WORLD, v_world);
	ALBEDO = u_albedo * (1.0 - hz);
	SPECULAR = 0.5 * (1.0 - hz);
	EMISSION = ship_haze_colour(CAMERA_POSITION_WORLD, v_world) * hz;
	ROUGHNESS = u_roughness;
	METALLIC = 0.0;
}
