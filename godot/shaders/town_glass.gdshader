/* LES FENÊTRES DES BÂTIMENTS MODELÉS, la nuit — la règle de town.gdshader,
 * appliquée cette fois à une surface qui n'est QUE des fenêtres (la matière
 * « fenetre » d'un .glb) plutôt qu'à un mur où il faut les dessiner.
 *
 * Toutes les maisons ne s'allument pas, et le tirage vient de LA POSITION de
 * l'instance, hachée dans le vertex : rien à stocker, et la même maison a la
 * même fenêtre d'une partie à l'autre. Une fenêtre ÉMET : elle garde sa couleur
 * chaude quand les murs prennent celle de la lune.
 */
shader_type spatial;

uniform float u_night = 0.0;          // 0 le jour, 1 la nuit — la même que les fanaux
uniform vec3 u_warm = vec3(1.0, 0.66, 0.30);
uniform float u_gain = 2.2;
uniform float u_lit_share = 0.55;
uniform vec3 u_glass : source_color = vec3(0.06, 0.07, 0.09);

varying float v_lit;

float hash21(vec2 p) {
	return fract(sin(dot(p, vec2(12.9898, 78.233))) * 43758.5453);
}

void vertex() {
	vec2 seed = MODEL_MATRIX[3].xz;                    // où la maison se tient : son tirage
	v_lit = step(1.0 - u_lit_share, hash21(floor(seed * 7.13)));
}

void fragment() {
	ALBEDO = u_glass;
	ROUGHNESS = 0.25;
	SPECULAR = 0.6;
	EMISSION = u_warm * (u_gain * u_night * v_lit);
}
