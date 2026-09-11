/* CE QUI RESPIRE LE MÊME AIR QUE LA MER.
 *
 * Tout ce que l'œil voit à travers l'air doit employer la MÊME brume, et non un
 * second modèle de brouillard : la mer et le navire doivent s'assombrir au même
 * rythme, sinon le bâtiment reste net contre une mer délavée et l'illusion
 * s'effondre. C'est ce que faisait `applyHaze` côté JavaScript en greffant le
 * terme sur un matériau standard ; Godot n'ayant pas de greffe équivalente, la
 * coque et les espars portent ce shader-ci, qui appelle la même fonction.
 *
 * Le couvercle de tempête l'atteint aussi : il ne servirait à rien d'assombrir la
 * mer et le ciel en laissant la coque en pleine lumière — c'est la seule erreur
 * d'éclairage que personne ne rate.
 */
shader_type spatial;
render_mode cull_disabled;

#include "res://shaders/haze.gdshaderinc"

uniform vec3 u_albedo : source_color = vec3(0.22, 0.26, 0.31);
uniform float u_roughness : hint_range(0.0, 1.0) = 0.72;
uniform vec3 u_horizon = vec3(0.82, 0.89, 0.93);   // deja lineaire
uniform float u_storm = 0.0;

varying vec3 v_world;

void vertex() {
	v_world = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
}

/* LA BRUME SE POSE DANS fragment(), ET C'EST EXACT PLUTÔT QU'APPROCHÉ.
 *
 * Le premier jet la mettait dans `light()`, là où l'on a la couleur déjà
 * éclairée — mais `CAMERA_POSITION_WORLD` n'y existe pas, et Godot refuse de
 * compiler. Le remède n'est pas un pis-aller : ce qu'on veut est
 *
 *     éclairé·(1−h) + horizon·h
 *
 * et comme l'éclairement est LINÉAIRE en albédo, multiplier l'albédo par (1−h)
 * et poser horizon·h en émission donne exactement cela. On obtient donc le
 * mélange juste sans jamais avoir à voir la couleur finale.
 */
void fragment() {
	float hz = haze_along(CAMERA_POSITION_WORLD, v_world);
	ALBEDO = u_albedo * (1.0 - 0.45 * u_storm) * (1.0 - hz);
	EMISSION = u_horizon * (1.0 - 0.55 * u_storm) * hz;
	ROUGHNESS = u_roughness;
	METALLIC = 0.0;
}
