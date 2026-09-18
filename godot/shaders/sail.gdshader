/* LA TOILE — le matériau `canvas` de ship-model.js et Naval.applySailLight.
 *
 * La toile à voile est mince, et une toile mince s'éclaire À TRAVERS. Le
 * soleil derrière une voile, l'essentiel de ce qu'on voit n'a jamais touché la
 * face avant : c'est passé par le tissage, et la voile paraît plus claire que
 * tout ce qui est éclairé de face, les espars dessinés en barres sombres
 * dessus. Un matériau éclairé ordinaire ne sait pas faire cela — sa face
 * arrière devient simplement noire.
 *
 * Le lobe est celui que la mer emploie déjà pour la lumière qui traverse une
 * crête : le plus fort quand l'œil, la toile et le soleil s'alignent, et
 * seulement là où le soleil est sur la face qu'on NE regarde PAS. La normale est
 * prise brute sur la géométrie, pas celle tournée vers l'œil, si bien qu'une
 * voile vue de l'un ou l'autre bord donne la même réponse.
 *
 * Ce qui reste en émission constante est la lumière que le ciel envoie à
 * travers le tissage de tous les côtés à la fois, qu'aucun lobe ne représente.
 *
 * Un écart d'arithmétique, dit ici plutôt que caché : l'original AJOUTE le lobe
 * après l'encodage sRGB, Godot l'ajoute en linéaire avant. Même lobe, même
 * couleur, même force ; la courbe diffère dans les tons moyens.
 */
shader_type spatial;
render_mode cull_disabled;

#include "res://shaders/ship_haze.gdshaderinc"

uniform vec3 u_canvas : source_color = vec3(0.95, 0.92, 0.86);
uniform vec3 u_emissive : source_color = vec3(0.553, 0.525, 0.435);   // 0x8d866f
uniform float u_emissive_k = 0.12;
uniform sampler2D u_map : source_color, filter_linear_mipmap_anisotropic, repeat_disable;
uniform float u_has_map = 0.0;
uniform vec3 u_sun_col = vec3(1.0, 0.95, 0.86);

varying vec3 v_world;
varying vec3 v_raw_n;

void vertex() {
	v_world = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	v_raw_n = normalize((MODEL_MATRIX * vec4(NORMAL, 0.0)).xyz);
}

void fragment() {
	/* La couleur RESTE et multiplie l'image, au lieu de passer au blanc : c'est
	   le ton de sa toile, qui garde une voile peinte du même blanc cassé que ses
	   voiles unies — un motif sur un matériau blanc est un drap à côté du foc. */
	vec3 diffuse = u_canvas;
	if (u_has_map > 0.5) diffuse *= texture(u_map, UV).rgb;

	vec3 to_eye = normalize(CAMERA_POSITION_WORLD - v_world);   // pas E : réservé
	vec3 N = normalize(v_raw_n);
	// le soleil doit être sur la face opposée, quelle que soit celle qu'on regarde
	float thru = max(-sign(dot(N, to_eye)) * dot(N, u_sun), 0.0);
	// et il faut regarder dedans
	float lobe = pow(max(dot(-to_eye, u_sun), 0.0), 3.0);
	vec3 lit_through = u_sun_col * diffuse * thru * lobe * 1.4;

	// la brume en dernier, par le même tour que la coque : voir hull.gdshader
	float hz = haze_along(CAMERA_POSITION_WORLD, v_world);
	ALBEDO = diffuse * (1.0 - hz);
	SPECULAR = 0.5 * (1.0 - hz);
	ROUGHNESS = 0.95;
	METALLIC = 0.0;
	EMISSION = (u_emissive * u_emissive_k + lit_through) * (1.0 - hz)
	         + ship_haze_colour(CAMERA_POSITION_WORLD, v_world) * hz;
}
