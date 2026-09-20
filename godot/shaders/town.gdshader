/* LES FENÊTRES D'UNE VILLE, LA NUIT.
 *
 * Une ville éteinte au clair de lune est un tas de cailloux ; ce sont ses
 * fenêtres qui en font un lieu habité, et c'est la seule chose qu'on en voit
 * d'un mille au large. Elles sont dessinées ICI, dans le mur lui-même : une
 * ville est un MultiMesh de quelques centaines de boîtes, et y accrocher une
 * lumière par maison recompilerait tout le rendu — la règle du projet le
 * défend, et Godot n'y survivrait pas plus que three.
 *
 * TOUTES LES MAISONS NE S'ALLUMENT PAS, et aucune ne s'allume comme sa voisine :
 * le tirage vient de la POSITION de la maison, hachée dans le vertex. Rien à
 * stocker, et la même maison a la même fenêtre d'une partie à l'autre.
 *
 * La lumière ne suit pas ce qui la reçoit : une fenêtre ÉMET, donc elle garde sa
 * couleur chaude quand la mer et les murs prennent celle de la lune.
 */
shader_type spatial;

uniform float u_night = 0.0;          // 0 le jour, 1 la nuit — la même que les fanaux
uniform vec3 u_warm = vec3(1.0, 0.66, 0.30);
uniform float u_gain = 2.2;
/// <summary>La part des maisons qui s'allument.</summary>
uniform float u_lit_share = 0.55;

varying vec3 v_local;
varying float v_lit;

float hash21(vec2 p) {
	return fract(sin(dot(p, vec2(12.9898, 78.233))) * 43758.5453);
}

void vertex() {
	v_local = VERTEX;                                  // la boîte unitaire, avant sa mise à l'échelle
	vec2 seed = MODEL_MATRIX[3].xz;                    // où la maison se tient : son tirage
	v_lit = step(1.0 - u_lit_share, hash21(floor(seed * 7.13)));
}

/* Deux fenêtres par face, dans la moitié haute du mur. Le pignon n'en a pas :
   on ne perce pas un mur qui porte le faîtage. */
float window_mask(vec3 p) {
	bool onX = abs(p.x) > abs(p.z);
	float u = onX ? p.z : p.x;
	float side = onX ? p.x : p.z;
	if (abs(side) < 0.44) return 0.0;                  // dessus, dessous : pas de fenêtre
	float cell = fract(u * 2.5 + 0.5);
	float wx = smoothstep(0.28, 0.34, cell) * (1.0 - smoothstep(0.66, 0.72, cell));
	float wy = smoothstep(-0.04, 0.0, p.y) * (1.0 - smoothstep(0.24, 0.28, p.y));
	return wx * wy;
}

void fragment() {
	ALBEDO = COLOR.rgb;                                // la couleur d'instance, comme avant
	ROUGHNESS = 0.92;
	METALLIC = 0.0;
	EMISSION = u_warm * (window_mask(v_local) * v_lit * u_night * u_gain);
}
