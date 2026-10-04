/* LE FOND DE LUI-MÊME — ce que la mer dépose là où personne n'a peint (le pinceau
 * du mode création passe par-dessus : ground_paint, appelé après).
 *
 * Quatre fonds, décidés par ce que le lieu EST, comme un pilote les lit sur sa
 * carte :
 *  - le SABLE BLANC des hauts-fonds, de la laisse à une dizaine de mètres — celui
 *    qui fait l'eau turquoise des Caraïbes ;
 *  - la VASE là où l'eau ne bouge pas : une rade abritée (l'ABRI, shelter_at — le
 *    même que celui de la mer), et le grand fond, où se pose ce qui est fin ;
 *  - l'HERBIER (Thalassia, l'herbe à tortues) par plaques, sur le sable plat
 *    d'un à douze mètres : il lui faut la lumière, et un fond qui ne glisse pas ;
 *  - la ROCHE où la pente est trop raide pour que rien n'y tienne.
 *
 * Les motifs sont ancrés au MONDE (u_true : l'origine en mètres vrais), et les
 * plaques d'herbier ont la taille d'un pré : quelques dizaines de mètres.
 * Les teintes sont celles des pinceaux (ground_paint) : une seule définition.
 */

uniform vec2 u_true = vec2(0.0);
uniform vec3 u_rock = vec3(0.15, 0.13, 0.11);

vec3 seabed_rock(vec2 q, float detail) {
	float lump = paint_noise(q * 0.8) * 0.6 + paint_noise(q * 4.0) * 0.4;
	return u_rock * mix(1.0, 0.6 + 0.8 * lump, max(detail, 0.5));
}

/* `base` : la teinte des sommets ; `world` : la position dans le repère des
   shaders ; `pix` : la largeur d'un pixel au sol. */
vec3 seabed(vec3 base, vec3 world, float pix) {
	float d = -world.y;                         // la profondeur
	if (d < -0.6) return base;                  // au-dessus de la laisse : la grève
	vec2 q = world.xz + u_true;                 // en mètres vrais
	float detail = 1.0 - smoothstep(0.06, 0.35, pix);
	// la pente, tirée des dérivées : un carreau de cinq mètres suffit à la lire
	vec3 n = normalize(cross(dFdy(world), dFdx(world)));
	float slope = 1.0 - abs(n.y);
	float shelter = shelter_at(world.xz);

	// le sable de la grève devient blanc en entrant dans l'eau
	vec3 col = mix(base, paint_whitesand(q, detail), smoothstep(-0.6, 0.8, d));

	// la vase : l'eau calme d'une rade, et le grand fond — par nappes
	float calm = smoothstep(0.8, 0.35, shelter) * smoothstep(1.0, 4.0, d);
	float deep = smoothstep(16.0, 40.0, d) * 0.85;
	float blot = paint_noise(q * 0.03) * 0.7 + paint_noise(q * 0.2) * 0.3;
	float mud = clamp(max(calm, deep) * smoothstep(0.2, 0.55, blot + max(calm, deep) * 0.35), 0.0, 1.0);
	col = mix(col, paint_mud(q, detail), mud);

	// l'herbier : par plaques, sur le plat, là où la lumière arrive
	float band = smoothstep(0.8, 2.0, d) * (1.0 - smoothstep(8.0, 12.0, d));
	float meadow = paint_noise(q * 0.018) * 0.65 + paint_noise(q * 0.07) * 0.35;
	float grass = band * smoothstep(0.52, 0.62, meadow) * (1.0 - smoothstep(0.12, 0.25, slope)) * (1.0 - mud * 0.7);
	col = mix(col, paint_seagrass(q, detail), grass);

	// la roche, où rien ne tient
	float rock = smoothstep(0.32, 0.55, slope + (paint_noise(q * 0.15) - 0.5) * 0.15) * smoothstep(0.3, 1.5, d);
	col = mix(col, seabed_rock(q, detail), rock);
	return col;
}
