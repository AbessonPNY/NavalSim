/* LES BLESSURES, posées par-dessus ses matières plutôt que peintes dans une
 * texture — applyScars de ship-model.js, écrite UNE fois pour deux usagers : la
 * passe posée sur un modèle .glb (ship_scar) et la coque procédurale (hull).
 *
 * Peindre dans ses UV demanderait un UV au point d'impact — un rayon contre le
 * maillage à chaque coup — et une coque procédurale n'a pas d'UV du tout. Les
 * marques sont donc une courte liste de points dans SON repère, et chaque
 * fragment demande à quelle distance il est de l'un d'eux.
 *
 * DES ARRACHEMENTS, PAS DES TACHES. Un boulet qui glisse sur du chêne ne le
 * roussit pas, il arrache la face patinée en longues déchirures claires qui vont
 * AVEC le fil : quelques traits de bois brut, surtout le long de la coque,
 * dentelés, effilés aux deux bouts, avec une lèvre sombre où la rainure fait
 * ombre ; plus nombreux et plus longs à mesure que la travée est frappée. Et
 * quand la fiche en donne (appearance.impactMaps), les impacts PEINTS : une
 * variante par marque, trois stades de l'éraflure à la plaie ouverte.
 *
 * `w` est le poids de la marque : rien à voir jusqu'au deuxième boulet dans une
 * travée, tout au huitième.
 */

const int NSCAR = 24;
uniform vec4 u_scar[24];          // xyz : le point, dans son repère ; w : le poids
uniform int u_scar_count = 0;
uniform float u_scar_k = 1.0;     // une blessure à l'échelle du navire qui la prend
uniform sampler2D u_scar_tex : source_color, filter_linear_mipmap_anisotropic, repeat_disable;
uniform vec2 u_scar_grid = vec2(1.0, 1.0);   // stades en travers, variantes en hauteur
uniform float u_scar_maps = 0.0;

float scar_hash(vec3 p) { return fract(sin(dot(p, vec3(12.9898, 78.233, 37.719))) * 43758.5453); }

/* Ce que les marques font à ce point de son repère : `raw` la part de bois brut,
   `lip` l'ombre de la rainure, et `paint` l'impact peint (rgb, opacité). */
void ship_scars(vec3 P, out float raw, out float lip, out vec4 paint) {
	raw = 0.0; lip = 0.0; paint = vec4(0.0);
	for (int i = 0; i < NSCAR; i++) {
		if (i >= u_scar_count) break;
		float e = clamp((u_scar[i].w - 2.0) / 6.0, 0.0, 1.0);
		vec3 c = u_scar[i].xyz;
		float R = (0.45 + 0.75 * e) * u_scar_k;
		// le bord où elle est : le flanc court le long de z, le tableau en travers de x
		bool flank = abs(c.x) > 0.18 * (abs(c.z) + 1.0);
		vec2 q = flank ? vec2(P.z - c.z, P.y - c.y) : vec2(P.x - c.x, P.y - c.y);
		float sw = u_scar[i].w;
		if (u_scar_maps > 0.5 && sw > 2.0) {
			// PEINTE : une variante par marque, quelques degrés hors du fil, sa taille et sa main
			float h1 = scar_hash(c * 1.7 + vec3(1.0)), h2 = scar_hash(c * 2.9 + vec3(2.0)), h3 = scar_hash(c * 3.7 + vec3(3.0));
			float S = (1.1 + 0.5 * h1) * u_scar_k;
			float ang = (h2 - 0.5) * 0.45;
			vec2 dir = vec2(cos(ang), sin(ang)), nrm = vec2(-dir.y, dir.x);
			vec2 uv = vec2(dot(q, dir), dot(q, nrm)) / S + 0.5;
			if (h3 > 0.5) uv.x = 1.0 - uv.x;
			if (uv.x > 0.0 && uv.x < 1.0 && uv.y > 0.0 && uv.y < 1.0) {
				float variant = floor(h1 * u_scar_grid.y * 0.999);
				// le premier stade au troisième boulet, le deuxième au cinquième, le dernier au septième
				float st = clamp((sw - 3.0) / 2.0, 0.0, u_scar_grid.x - 1.0);
				float s0 = floor(st), s1 = min(s0 + 1.0, u_scar_grid.x - 1.0), tt = st - s0;
				vec2 cell = 1.0 / u_scar_grid;
				// droite dans sa case, et gardée à deux texels de son bord
				vec2 inset = vec2(uv.x, 1.0 - uv.y) * (1.0 - 4.0 / 512.0) + 2.0 / 512.0;
				vec4 T = mix(texture(u_scar_tex, (vec2(s0, variant) + inset) * cell),
				             texture(u_scar_tex, (vec2(s1, variant) + inset) * cell), tt);
				float a = T.a * clamp(sw - 2.0, 0.0, 1.0);
				paint = vec4(mix(paint.rgb, T.rgb, a), max(paint.a, a));
			}
		}
		if (u_scar_maps < 0.5 && e > 0.0 && distance(P, c) < R) {
			float strokes = 1.0 + floor(e * 4.0);
			for (int j = 0; j < 5; j++) {
				if (float(j) >= strokes) break;
				float fj = float(j);
				float h1 = scar_hash(c * 1.7 + vec3(fj * 13.1, fj * 7.3, 1.0));
				float h2 = scar_hash(c * 2.3 + vec3(fj * 5.9, 3.0, fj * 11.7));
				float h3 = scar_hash(c * 3.1 + vec3(2.0, fj * 17.3, fj * 3.7));
				float ang = (h1 - 0.5) * 0.7;                  // surtout dans le fil
				vec2 dir = vec2(cos(ang), sin(ang));
				vec2 nrm = vec2(-dir.y, dir.x);
				vec2 o = nrm * (h2 - 0.5) * 0.55 * R + dir * (h3 - 0.5) * 0.4 * R;
				float len = (0.45 + 0.45 * h3) * R;
				float u = dot(q - o, dir);
				// une ligne déchirée, dentelée
				float v = dot(q - o, nrm) + 0.018 * u_scar_k * sin(u * 23.0 / u_scar_k + h2 * 6.28);
				float t = abs(u) / len;
				if (t < 1.0) {
					// effilée aux deux bouts ; plus large que vrai pour tenir un pixel
					float w = 0.04 * u_scar_k * (1.0 - t * t) * (0.7 + 0.6 * h1);
					float a = min(1.0, 0.45 + e);
					raw = max(raw, (1.0 - smoothstep(0.35 * w, w, abs(v))) * a);
					lip = max(lip, (1.0 - smoothstep(w, 2.6 * w, abs(v))) * a * 0.35);
				}
			}
		}
	}
}

// le chêne frais, sous la face patinée
const vec3 SCAR_OAK = vec3(0.46, 0.34, 0.21);

/* Appliquée à une couleur : la lèvre assombrit, le bois brut recouvre, l'impact peint par-dessus. */
vec3 scar_apply(vec3 base, vec3 P) {
	float raw, lip; vec4 paint;
	ship_scars(P, raw, lip, paint);
	vec3 c = mix(base * (1.0 - lip), SCAR_OAK, raw);
	return mix(c, paint.rgb, paint.a);
}
