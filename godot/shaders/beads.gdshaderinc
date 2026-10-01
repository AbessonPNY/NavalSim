/* LES PERLES D'EAU SUR UN VERRE — une définition, deux usagers : l'objectif qui
 * sort de l'eau (droplets.gdshader) et celui de la lunette sous la pluie
 * (spyglass.gdshader). Le hasard, la forme d'une perle, le semis qui sèche, et
 * la pluie qui renouvelle.
 */

float hash1(vec2 p) { return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453); }
vec2 hash2(vec2 p) { return vec2(hash1(p), hash1(p + 17.3)); }

/* UNE PERLE : rend sa PENTE (sa normale projetée, xy) et sa couverture (z),
   0 hors d'elle. d : le point vu depuis son centre ; r : son rayon ; cell : sa
   case, d'où elle tire sa forme. */
vec3 bead_shape(vec2 d, float r, vec2 cell) {
	/* NI RONDE NI TOUTES PAREILLES. Une goutte sur une vitre est tirée par
	   son poids : un peu ovale, un peu de travers, et pointue par le HAUT —
	   la larme. Chacune a donc son allongement, son inclinaison et sa
	   pointe, tirés de sa case. */
	float ang = hash1(cell + 11.9) * 3.1416;
	float squash = 0.72 + 0.55 * hash1(cell + 23.4);
	float cs = cos(ang), sn = sin(ang);
	vec2 e = vec2(cs * d.x - sn * d.y, sn * d.x + cs * d.y);
	e.x /= squash;
	// la pointe : le haut de la goutte est plus étroit que son ventre
	e.y *= e.y > 0.0 ? 1.0 + 0.45 * hash1(cell + 41.2) : 0.92;
	float dist = length(e);
	if (dist > r) return vec3(0.0);
	// une calotte : plate au centre, très penchée au bord
	float k = sqrt(max(0.0, 1.0 - (dist * dist) / (r * r)));
	return vec3(e / max(dist, 1e-4) * (1.0 - k), k);
}

/* Le semis : une perle par case, placée au hasard dedans, et les cases voisines
   regardées aussi pour qu'une perle déborde de la sienne. Rend la PENTE de la
   perle (sa normale projetée) et sa couverture. */
vec3 beads(vec2 p, float scale, float seed, float life) {
	vec2 q = p * scale;
	vec2 i = floor(q), f = fract(q);
	vec3 best = vec3(0.0);
	for (int y = -1; y <= 1; y++)
		for (int x = -1; x <= 1; x++) {
			vec2 o = vec2(float(x), float(y));
			vec2 h = hash2(i + o + seed);
			/* DEUX CASES SUR TROIS RESTENT SÈCHES. Moins de perles, mais chacune
			   plus grosse et qui plie l'image plus fort, se lit comme de l'eau ;
			   un semis fin et serré se lit comme du verre dépoli. */
			if (hash1(i + o + seed + 31.7) < 0.82) continue;
			// chaque perle a son heure : les plus petites sèchent les premières
			// les plus grosses ôtées : au-delà d'une certaine taille, une perle se lit
			// comme une bulle de savon collée à l'objectif, pas comme une goutte
			float r = (0.10 + 0.13 * h.x) * smoothstep(0.0, 0.45 + 0.4 * h.y, life);
			if (r <= 0.001) continue;
			vec2 c = o + vec2(h.y, hash1(i + o + seed + 5.1));
			vec3 got = bead_shape(f - c, r, i + o + seed);
			if (got.z <= 0.0) continue;
			if (got.z > best.z) best = got;
		}
	return best;
}

/* LA PLUIE SUR UNE LENTILLE. Pas le semis d'une sortie de l'eau, qui sèche
   d'un bloc : des gouttes qui ARRIVENT. Chaque case a son cycle, à son rythme :
   une goutte s'y pose (elle s'étale en un instant), tient, puis s'en va — elle
   s'évapore ou rejoint une voisine —, et la suivante tombe AILLEURS dans la case,
   puisque sa place est tirée du numéro de son cycle. Plus il pleut, plus de cases
   reçoivent. amount : de 0 (sec) à 1 (averse). */
vec3 rain_beads(vec2 p, float scale, float seed, float t, float amount) {
	vec2 q = p * scale;
	vec2 i = floor(q), f = fract(q);
	vec3 best = vec3(0.0);
	for (int y = -1; y <= 1; y++)
		for (int x = -1; x <= 1; x++) {
			vec2 o = vec2(float(x), float(y));
			vec2 cell = i + o + seed;
			if (hash1(cell + 63.1) > 0.15 + 0.55 * amount) continue;
			float period = 3.0 + 7.0 * hash1(cell + 2.9);
			float u = t / period + hash1(cell + 8.3);
			float ph = fract(u);
			vec2 at = cell + floor(u) * 7.31;          // sa place change à chaque goutte
			float r = (0.12 + 0.16 * hash1(at + 1.7))
			        * smoothstep(0.0, 0.025, ph) * (1.0 - smoothstep(0.70, 1.0, ph));
			if (r <= 0.001) continue;
			vec2 c = o + vec2(0.2, 0.2) + 0.6 * hash2(at + 4.4);
			vec3 got = bead_shape(f - c, r, at);
			if (got.z > best.z) best = got;
		}
	return best;
}
