/* LA NEIGE QUI TIENT SUR ELLE — applySnowCover de ship-model.js, écrite UNE fois
 * pour trois usagers : la passe greffée sur les modèles .glb (ship_snow), la
 * coque et les espars procéduraux (hull), la toile (sail).
 *
 * Là où une surface regarde le ciel, sa couleur passe à la neige selon
 * l'épaisseur de la couche : d'abord les plats — pont, hunes, dessus des vergues
 * et des lisses —, puis les pentes plus douces. Le bord est cassé par un bruit
 * posé dans SON repère (la position du sommet dans son maillage), si bien que les
 * plaques restent où elles sont tombées quand elle roule. Une voile, pendante,
 * n'en prend presque pas, par la même règle.
 */

const vec3 NAVAL_SNOW_COLOR = vec3(0.90, 0.92, 0.95);

float naval_snow_hash(vec3 p) {
	return fract(sin(dot(p, vec3(12.9898, 78.233, 37.719))) * 43758.5453);
}

float naval_snow_noise(vec3 p) {
	vec3 i = floor(p), f = fract(p);
	f = f * f * (3.0 - 2.0 * f);
	float a = mix(mix(naval_snow_hash(i), naval_snow_hash(i + vec3(1, 0, 0)), f.x),
	              mix(naval_snow_hash(i + vec3(0, 1, 0)), naval_snow_hash(i + vec3(1, 1, 0)), f.x), f.y);
	float b = mix(mix(naval_snow_hash(i + vec3(0, 0, 1)), naval_snow_hash(i + vec3(1, 0, 1)), f.x),
	              mix(naval_snow_hash(i + vec3(0, 1, 1)), naval_snow_hash(i + vec3(1, 1, 1)), f.x), f.y);
	return mix(a, b, f.z);
}

/* Combien de neige ici, 0 à 1 (la page en prend 95 %) : `up` est la composante
   verticale de la normale MONDE, `p` la position dans le maillage, `cover`
   l'épaisseur de la couche sur ce navire. */
float naval_snow(float up, vec3 p, float cover) {
	if (cover <= 0.001) return 0.0;
	float n = naval_snow_noise(p * 1.7) * 0.6 + naval_snow_noise(p * 5.3) * 0.4;
	// plus la couche est épaisse, plus les pentes qui la retiennent sont raides
	float reach = mix(0.92, 0.45, cover);
	float s = smoothstep(reach, reach + 0.2, up + (n - 0.5) * 0.35) * smoothstep(0.0, 0.35, cover + n * 0.3 - 0.15);
	return clamp(s, 0.0, 1.0) * 0.95;
}
