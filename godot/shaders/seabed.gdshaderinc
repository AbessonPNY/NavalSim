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

// u_true : déclaré dans ground_paint.gdshaderinc, inclus avant
uniform vec3 u_rock = vec3(0.15, 0.13, 0.11);

/* LES RÉCIFS DE LA FICHE (core/Reefs.cs, Reef.Locate) : la MÊME ellipse que le
   noyau relève dans le relief, en mètres vrais. a : centre et grand axe ; b :
   demi-axes et tombant. LandNode les pousse une fois. Changer la forme d'un côté
   sans l'autre peindrait le corail à côté du haut-fond. */
#define REEFS 16
uniform vec4 u_reef_a[REEFS];
uniform vec4 u_reef_b[REEFS];
uniform int u_reef_n = 0;

// sur un récif : 1 sur le plateau, 0 au pied du tombant ; et le rayon de l'ellipse
float reef_at(vec2 q, out float e, out vec2 rel) {
	float best = 0.0; e = 9.0; rel = vec2(0.0);
	for (int i = 0; i < REEFS; i++) {
		if (i >= u_reef_n) break;
		vec4 a = u_reef_a[i], b = u_reef_b[i];
		vec2 d = q - a.xy;
		float l = dot(d, a.zw), w = -d.x * a.w + d.y * a.z;
		float th = atan(w / b.y, l / b.x), ph = b.w;
		// l'ourlet de Reef.Wobble — la même formule, au centimètre
		float ee = length(vec2(l / b.x, w / b.y)) / (1.0 + 0.12 * sin(3.0 * th + ph) + 0.07 * sin(5.0 * th + 1.7 * ph) + 0.04 * sin(9.0 * th + 2.3 * ph));
		float past = max(0.0, ee - 1.0) * min(b.x, b.y);
		float t = clamp(1.0 - past / max(1.0, b.z), 0.0, 1.0);
		if (t > best) { best = t; e = ee; rel = vec2(l / b.x, w / b.y); }
	}
	return best;
}

/* LE CORAIL D'UN RÉCIF vu d'en haut. Sur le plateau, des TÊTES éparses — brunes,
   olive, quelques violets d'éponges — sur un sable de débris ; au bord, là où la
   houle le travaille, le corail se serre en ÉPERONS séparés de SILLONS de sable
   qui descendent vers le large, irréguliers. Teintes sourdes : c'est la mer, à
   travers elle et sous les caustiques, qui les éclaire.
   « e » : le rayon de l'ourlet (1 au bord du plateau). */
vec3 reef_paint(vec2 q, vec2 rel, float e, float detail) {
	float head = paint_noise(q * 0.45) * 0.55 + paint_noise(q * 1.7) * 0.3 + paint_noise(q * 5.0) * 0.15;
	vec3 brown = vec3(0.13, 0.085, 0.045), olive = vec3(0.075, 0.075, 0.035), violet = vec3(0.09, 0.05, 0.08);
	vec3 coral = mix(brown, olive, smoothstep(0.35, 0.7, paint_noise(q * 0.13)));
	coral = mix(coral, violet, smoothstep(0.8, 0.92, paint_noise(q * 0.4 + 7.0)) * 0.6);
	coral *= mix(1.0, 0.7 + 0.6 * paint_noise(q * 3.3), max(detail, 0.4));
	vec3 rubble = mix(paint_whitesand(q, detail), coral, 0.25);
	// le plateau : des têtes, plus serrées vers le bord
	float cover = smoothstep(0.62 - 0.25 * smoothstep(0.4, 1.0, e), 0.72 - 0.2 * smoothstep(0.4, 1.0, e), head);
	// le bord : éperons et sillons, perpendiculaires à l'ourlet, rompus de bruit
	float ang = atan(rel.y, rel.x);
	float spur = smoothstep(0.35, 0.65, sin(ang * 38.0 + paint_noise(q * 0.06) * 9.0 + paint_noise(q * 0.3) * 2.0) * 0.5 + 0.5);
	float rim = smoothstep(0.75, 0.95, e) * (1.0 - smoothstep(1.15, 1.5, e));
	cover = mix(cover, spur * smoothstep(0.3, 0.6, head + 0.25), rim);
	return mix(rubble, coral, cover);
}

vec3 seabed_rock(vec2 q, float detail) {
	float lump = paint_noise(q * 0.8) * 0.6 + paint_noise(q * 4.0) * 0.4;
	return u_rock * mix(1.0, 0.6 + 0.8 * lump, max(detail, 0.5));
}
Surf m_rock(vec2 q, float detail) { return u_mat_layer[6] > 0 ? surf_tex(6, q) : surf_flat(seabed_rock(q, detail), 6); }

/* La part du sable blanc à cette profondeur : il recouvre TOUT dès 0,8 m — ce qui est
   dessous n'a alors pas à être calculé (land.gdshader, natural). */
float seabed_white(float d) { return smoothstep(-0.6, 0.8, d); }

/* `base` : la teinte des sommets ; `world` : la position dans le repère des
   shaders ; `pix` : la largeur d'un pixel au sol.
   Chaque matière n'est LUE que là où elle a une part : les arguments d'une fonction
   sont évalués avant l'appel, et surf_mix ne saurait l'éviter. */
Surf seabed(Surf base, vec3 world, float pix) {
	float d = -world.y;                         // la profondeur
	if (d < -0.6) return base;                  // au-dessus de la laisse : la grève
	vec2 q = world.xz + u_true;                 // en mètres vrais
	float detail = 1.0 - smoothstep(0.06, 0.35, pix);
	// la pente, tirée des dérivées : un carreau de cinq mètres suffit à la lire
	vec3 n = normalize(cross(dFdy(world), dFdx(world)));
	float slope = 1.0 - abs(n.y);
	float shelter = shelter_at(world.xz);

	// le sable de la grève devient blanc en entrant dans l'eau
	float white = seabed_white(d);
	Surf col = white > 0.001 ? surf_mix(base, m_whitesand(q, q, detail), white) : base;

	// la vase : l'eau calme d'une rade, et le grand fond — par nappes
	float calm = smoothstep(0.8, 0.35, shelter) * smoothstep(1.0, 4.0, d);
	float deep = smoothstep(16.0, 40.0, d) * 0.85;
	float blot = paint_noise(q * 0.03) * 0.7 + paint_noise(q * 0.2) * 0.3;
	float mud = clamp(max(calm, deep) * smoothstep(0.2, 0.55, blot + max(calm, deep) * 0.35), 0.0, 1.0);
	if (mud > 0.001) col = surf_mix(col, m_mud(q, q, detail), mud);

	// l'herbier : par plaques, sur le plat, là où la lumière arrive
	float band = smoothstep(0.8, 2.0, d) * (1.0 - smoothstep(8.0, 12.0, d));
	float meadow = paint_noise(q * 0.018) * 0.65 + paint_noise(q * 0.07) * 0.35;
	float grass = band * smoothstep(0.52, 0.62, meadow) * (1.0 - smoothstep(0.12, 0.25, slope)) * (1.0 - mud * 0.7);
	if (grass > 0.001) col = surf_mix(col, m_seagrass(q, q, detail), grass);

	// la roche, où rien ne tient
	float rock = smoothstep(0.32, 0.55, slope + (paint_noise(q * 0.15) - 0.5) * 0.15) * smoothstep(0.3, 1.5, d);
	if (rock > 0.001) col = surf_mix(col, m_rock(q, detail), rock);

	// le récif : du corail sur le plateau et le haut du tombant, pas sur la caye sèche
	if (u_reef_n > 0) {
		float e; vec2 rel;
		float onreef = reef_at(q, e, rel);
		if (onreef > 0.0)
			col = surf_mix(col, surf_flat(reef_paint(q, rel, e, detail), 3), smoothstep(0.15, 0.7, onreef) * smoothstep(-0.6, 0.15, d));
	}
	return col;
}
