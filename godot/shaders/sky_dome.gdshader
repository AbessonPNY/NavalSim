/* LE DÔME DE CIEL.
 *
 * Il n'ajoute qu'une chose à sky.gdshaderinc : le DISQUE solaire, que lui seul
 * dessine. La mer, elle, tire son éclat d'un lobe micro-facettes — ce qui est ce
 * qui l'étale en route de scintillement au lieu d'y coller une pastille.
 */
shader_type sky;

#include "res://shaders/sky.gdshaderinc"

// l'éclair PROCHE, qui éclaire toute la voûte — à ne pas confondre avec
// u_storm_flash, qui n'allume qu'un quart d'horizon à six milles de là
uniform float u_flash = 0.0;

void sky() {
	vec3 dir = EYEDIR;
	vec3 c = naval_sky(dir, u_sun, u_zenith, u_horizon);

	// le disque lui-même
	c += vec3(1.0, 0.95, 0.85) * pow(max(dot(normalize(dir), u_sun), 0.0), 2200.0) * 6.0;

	/* La décharge éclaire toute la voûte, et le plus fort en bas. C'est ce qui
	   sépare l'éclair proche du grain lointain : celui-là est une lumière
	   QUELQUE PART, celui-ci est au-dessus d'elle. */
	c += vec3(0.62, 0.70, 0.92) * u_flash * (1.6 - 0.9 * clamp(dir.y, 0.0, 1.0));

	COLOR = c;
}
