/* L'HORIZON DE LA MER.
 *
 * La nappe de houle fait sept kilomètres de côté — c'est la taille que le
 * remaillage vers la caméra suppose, et l'agrandir coûterait la finesse des
 * vagues le long de la coque. En prenant de la hauteur on en voyait donc le
 * BORD, et le monde se lisait comme un décor posé sur rien.
 *
 * Cet anneau plat va du dedans de la nappe jusqu'à quarante kilomètres, où la
 * brume a tout éteint (1 − exp(−0,00085 × 40000) ≈ 1). Il n'a pas de vagues et
 * n'en a pas besoin : à trois kilomètres, la brume mange déjà 94 % du contraste
 * et personne n'y distingue une crête. Il porte la teinte PROFONDE de la mer,
 * noyée dans le ciel par la MÊME fonction de brume que tout le reste — c'est ce
 * qui fait qu'on ne voit pas où l'une finit et l'autre commence.
 */
shader_type spatial;
render_mode unshaded, cull_disabled, shadows_disabled, fog_disabled;

#include "res://shaders/ship_haze.gdshaderinc"

// la même couleur profonde que la mer (ocean.gdshader : u_deep)
uniform vec3 u_deep = vec3(0.00439, 0.03310, 0.06301);

varying vec3 v_world;

void vertex() {
	v_world = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
}

void fragment() {
	ALBEDO = mix(u_deep, ship_haze_colour(CAMERA_POSITION_WORLD, v_world),
	             haze_along(CAMERA_POSITION_WORLD, v_world));
}
