#[vertex]
#version 450

// un triangle qui couvre l'écran, sans tampon de sommets
layout(location = 0) out vec2 uv;

void main() {
	vec2 p = vec2((gl_VertexIndex << 1) & 2, gl_VertexIndex & 2);
	uv = p;
	gl_Position = vec4(p * 2.0 - 1.0, 0.0, 1.0);
}

#[fragment]
#version 450

/* SOUS L'EAU — voir UnderwaterEffect.cs.
 *
 * Deux choses, et une seule passe.
 *
 * L'EAU QUI MANGE LA LUMIÈRE. Sous la surface, tout est vu à travers des mètres
 * d'eau : l'extinction est PAR CANAL — le rouge parti en quelques mètres, le
 * bleu portant trois fois plus loin —, ce qui fait tout virer au vert puis au
 * bleu puis à rien, au lieu de griser comme ferait une brume. Le fond de l'image
 * (le ciel, que l'œil ne devrait plus voir) est traité comme de l'eau très
 * lointaine : il disparaît de lui-même.
 *
 * LES RAIS QUI FILTRENT ENTRE LES VAGUES. Le soleil traverse une surface qui
 * n'est pas plane : elle le concentre en nappes qui descendent, s'écartent un
 * peu et s'éteignent avec la profondeur. On les intègre le long du rayon de
 * vue : à chaque pas, on remonte jusqu'à la surface DANS LA DIRECTION DU SOLEIL
 * et on lit là-haut une figure de caustiques qui dérive ; ce qui est lu est
 * ajouté, atténué par la profondeur du point et par l'eau déjà traversée. C'est
 * donc bien un volume éclairé, et non un dégradé posé sur l'image : les rais
 * passent devant et derrière ce qui est dans l'eau, et se coupent sur ce qui est
 * opaque.
 *
 * L'IMAGE ADOUCIE PAR L'EAU. L'eau diffuse vers l'avant : chaque mètre traversé
 * dévie un peu la lumière de son trajet, et ce qui est loin se lit plus mou que
 * ce qui est près — un flou qui grandit avec la distance, et non un voile posé
 * sur toute l'image. Il se calcule en RASSEMBLANT : un point de l'écran prend ses
 * voisins dans un disque à la mesure de sa propre distance, mais un voisin n'y
 * entre que si son propre flou le porte jusque-là — si bien qu'une chose proche
 * et nette ne bave pas sur le lointain, ni le lointain sur elle.
 *
 * CE QUI FLOTTE DANS L'EAU. Des grains de vase, des débris de plancton, la
 * « neige » de la mer : des points qui prennent la lumière, épais près du fond où
 * le ressac les soulève, rares en pleine eau, et bien plus nombreux dans une rade
 * à l'eau dormante (l'abri). Ils occupent une grille du monde — une case de
 * quarante centimètres, un grain ou aucun —, que le rayon de vue parcourt case à
 * case sur huit mètres : ils restent donc à leur place quand on tourne la tête,
 * dérivent ensemble avec le courant et vont et viennent avec le ressac. Le plus
 * proche est un disque flou et pâle (l'œil n'accommode pas à dix centimètres),
 * le lointain un point qui s'éteint dans l'eau comme le reste.
 */

layout(location = 0) in vec2 uv;
layout(location = 0) out vec4 frag;

layout(set = 0, binding = 0) uniform sampler2D src_color;   // rgb, et la profondeur de vue en alpha
layout(set = 0, binding = 1) uniform sampler2D src_depth;
layout(set = 0, binding = 2, std140) uniform Params {
	mat4 inv_proj;    // écran -> vue
	mat4 inv_view;    // vue -> monde
	vec4 sun;         // xyz : le soleil, monde ; w : sa force
	vec4 water;       // rgb : la couleur de l'eau profonde ; a : le temps
	vec4 misc;        // x : la mer sous l'œil (hauteur) ; y : force des rais ; z : portée ; w : densité
	vec4 split;       // x : l'œil à cheval sur la surface (0 à 1) ; y : force du flou ; z : force des grains ; w : l'eau trouble (0 à 1)
	vec4 bed;         // x : le fond sous l'œil (hauteur) ; y : le ressac, en mètres ; zw : l'origine du monde, ramenée sur la période de la grille (cases)
} p;

float hash(vec2 q) { return fract(sin(dot(q, vec2(127.1, 311.7))) * 43758.5453); }

float noise(vec2 q) {
	vec2 i = floor(q), f = fract(q);
	f = f * f * (3.0 - 2.0 * f);
	return mix(mix(hash(i), hash(i + vec2(1, 0)), f.x),
	           mix(hash(i + vec2(0, 1)), hash(i + vec2(1, 1)), f.x), f.y);
}

/* Les caustiques d'une surface agitée, vues d'en dessous : des nappes claires
   séparées par du sombre. Deux couches qui dérivent l'une contre l'autre, prises
   en crêtes (1 − |2n − 1|) pour que ce soient des LIGNES et non des taches. */
float caustics(vec2 q, float t) {
	float a = noise(q * 0.10 + vec2(t * 0.05, -t * 0.03));
	float b = noise(q * 0.23 - vec2(t * 0.02, t * 0.06));
	float r1 = 1.0 - abs(2.0 * a - 1.0);
	float r2 = 1.0 - abs(2.0 * b - 1.0);
	return pow(r1, 3.0) * 0.7 + pow(r2, 4.0) * 0.5;
}

/* Un hachage entier (pcg3d) : la grille des grains a une période de 4096 cases,
   et deux cases distantes d'une période doivent tirer le même grain, d'où les
   indices pris modulo et non des flottants. */
uvec3 pcg3d(uvec3 v) {
	v = v * 1664525u + 1013904223u;
	v.x += v.y * v.z; v.y += v.z * v.x; v.z += v.x * v.y;
	v ^= v >> 16u;
	v.x += v.y * v.z; v.y += v.z * v.x; v.z += v.x * v.y;
	return v;
}
vec4 hash4(ivec3 c) {
	uvec3 a = pcg3d(uvec3(c) & 4095u);
	uvec3 b = pcg3d(a ^ uvec3(0x9E37u, 0x85EBu, 0xC2B2u));
	return vec4(a, b.x) * (1.0 / 4294967295.0);
}

const float MOTE_CELL = 0.40;    // la case de la grille des grains, en mètres
const float MOTE_REACH = 8.0;    // jusqu'où on les cherche : au-delà, l'eau les a éteints

/* Le flou d'un point de l'écran, en pixels d'une image de 1080 lignes, selon la
   distance de ce qu'il montre : un rien tout près, quelques pixels à vingt mètres. */
float blur_px(float d, float trouble) {
	return min(0.35 + 0.22 * d * (1.0 + 1.5 * trouble), 7.0) * p.split.y;
}

void main() {
	vec4 src = texture(src_color, uv);
	float view_z = src.a;                       // la profondeur de vue, en mètres, rangée par la copie

	// le rayon de vue, en monde
	vec4 h = p.inv_proj * vec4(uv * 2.0 - 1.0, 1.0, 1.0);
	vec3 view_dir = normalize(h.xyz / h.w);
	vec3 dir = normalize((p.inv_view * vec4(view_dir, 0.0)).xyz);
	vec3 eye = p.inv_view[3].xyz;

	// le fond de l'image est de l'eau très lointaine : l'œil ne doit plus voir le ciel
	float far = 400.0;
	float dist = view_z > far * 0.5 ? p.misc.z * 2.0 : view_z / max(-view_dir.z, 1e-3);

	/* À CHEVAL SUR LA SURFACE : la moitié basse de l'image est sous l'eau, la
	   haute dehors, et c'est le SENS DU RAYON qui tranche — l'œil est sur la
	   ligne, donc tout ce qui descend est dans l'eau. La bascule garde un cheveu
	   de fondu, le temps qu'une vague passe devant l'objectif. */
	float wet = p.split.x > 0.01 ? smoothstep(0.01, -0.01, dir.y) : 1.0;
	if (wet <= 0.001) {
		frag = vec4(src.rgb, 1.0);
		return;
	}

	float trouble = p.split.w;
	vec2 res = vec2(textureSize(src_color, 0));
	float px_scale = res.y / 1080.0;

	// --- l'image adoucie : un disque qui rassemble, à la mesure de chacun ---
	vec3 seen = src.rgb;
	float r_c = blur_px(dist, trouble) * px_scale;
	if (r_c > 0.6) {
		const int TAPS = 16;
		vec3 acc = src.rgb;
		float wsum = 1.0;
		float spin = hash(uv * 977.0) * 6.2831853;    // une rosace tournée par pixel : pas de motif
		for (int i = 0; i < TAPS; i++) {
			float rr = r_c * sqrt((float(i) + 0.5) / float(TAPS));
			float a = float(i) * 2.3999632 + spin;           // l'angle d'or
			vec2 o = vec2(cos(a), sin(a)) * rr;
			vec4 s = texture(src_color, uv + o / res);
			float ds = s.a > far * 0.5 ? p.misc.z * 2.0 : s.a;
			// il n'entre que si son propre flou le porte jusqu'ici
			float r_s = blur_px(ds, trouble) * px_scale;
			float w = clamp(min(r_s, r_c) - rr + 1.0, 0.0, 1.0);
			acc += s.rgb * w;
			wsum += w;
		}
		seen = acc / wsum;
	}

	// --- l'eau mange la lumière : extinction par canal ---
	vec3 k = vec3(0.34, 0.13, 0.085) * p.misc.w;
	vec3 through = exp(-k * dist);
	vec3 col = mix(p.water.rgb, seen, through);

	// --- les rais qui descendent ---
	float reach = min(dist, p.misc.z);
	vec3 sun = normalize(p.sun.xyz);
	float up_sun = max(sun.y, 0.08);
	float shafts = 0.0;
	const int N = 24;
	float step_m = reach / float(N);
	// un décalage par pixel, pour que les pas ne se lisent pas en tranches
	float jitter = hash(uv * 1024.0) * step_m;
	for (int i = 0; i < N; i++) {
		vec3 P = eye + dir * (jitter + step_m * float(i));
		float below = p.misc.x - P.y;                     // de combien ce point est sous la mer
		if (below <= 0.0) continue;
		// remonter jusqu'à la surface DANS LA DIRECTION DU SOLEIL : là où ce rai est né
		vec2 q = P.xz + sun.xz * (below / up_sun);
		float lit = caustics(q, p.water.a);
		// ce qui reste de lumière à cette profondeur, et l'eau déjà traversée pour la voir
		shafts += lit * exp(-below * 0.045) * step_m;
	}
	shafts *= p.misc.y * p.sun.w * up_sun;
	// les rais sont de la lumière du soleil, bleuie par l'eau qu'elle a traversée
	col += vec3(0.55, 0.85, 1.0) * shafts * 0.0022;

	// --- les grains : la grille parcourue case à case, du plus près au plus loin ---
	if (p.split.z > 0.0) {
		float t_end = min(dist, MOTE_REACH);
		float t_now = p.water.a;
		// le courant les emporte tous ensemble, le ressac les berce
		vec3 drift = vec3(0.035, 0.004, 0.022) * t_now
		           + p.bed.y * vec3(sin(t_now * 0.83), 0.35 * sin(t_now * 0.83 + 1.3), 0.6 * cos(t_now * 0.71));
		vec3 g0 = (eye - drift) / MOTE_CELL + vec3(p.bed.z, 0.0, p.bed.w);
		ivec3 cell = ivec3(floor(g0));
		vec3 sgn = sign(dir);
		vec3 inv = 1.0 / max(abs(dir), vec3(1e-5));
		vec3 t_max = (sgn * (vec3(cell) - g0) + max(sgn, 0.0)) * inv * MOTE_CELL;
		vec3 t_delta = inv * MOTE_CELL;
		float pix = 2.0 * p.inv_proj[1][1] / res.y;   // l'angle d'un pixel
		/* la lumière qui les éclaire : celle qui descend dans l'eau, bleuie — la même qui
		   éclaire le sable, un grain est aussi clair que lui et de sa couleur, c'est du fond soulevé —, et bien plus vive en
		   regardant vers le soleil : une poussière diffuse vers l'avant (le rai dans une
		   pièce sombre), presque rien vers l'arrière. */
		float fwd = max(dot(dir, sun), 0.0);
		float phase = 0.8 + 2.2 * pow(fwd, 6.0);
		vec3 lit_base = p.water.rgb * 3.0 + vec3(0.50, 0.75, 0.80) * vec3(0.92, 0.84, 0.66) * 1.25 * p.sun.w * (0.35 + 0.65 * up_sun) * phase;
		float sea_y = p.misc.x, bed_y = p.bed.x;
		float t = 0.0;
		for (int i = 0; i < 40 && t < t_end; i++) {
			vec4 hh = hash4(cell);
			// la case au milieu de l'eau : sa hauteur sur le fond dit combien elle en porte
			float cy = (float(cell.y) + 0.5) * MOTE_CELL + drift.y;
			float above = max(cy - bed_y, 0.0);
			float dens = (0.06 + 0.55 * exp(-above / 1.3)) * (0.6 + 1.6 * trouble);
			if (hh.w < dens) {
				vec3 m = vec3(cell) + 0.2 + 0.6 * hh.xyz
				       + 0.08 * sin(t_now * (0.5 + hh.yzx) + hh.zxy * 6.28);
				vec3 M = (m - vec3(p.bed.z, 0.0, p.bed.w)) * MOTE_CELL + drift;
				vec3 to = M - eye;
				float tm = dot(to, dir);
				if (tm > 0.12 && tm < dist && M.y < sea_y) {
					float perp = length(to - dir * tm);
					// un flocon de deux à six millimètres, jamais moins d'un pixel ; tout près, un disque flou
					float r0 = 0.002 + 0.004 * hh.x * hh.x * hh.x;
					float coc = 0.005 * smoothstep(0.9, 0.12, tm);
					float R = max(max(r0, coc), tm * pix * 1.1);
					float cover = exp(-(perp * perp) / (R * R)) * min(1.0, (r0 * r0 * 2.2) / (R * R) + 0.04 * coc / R);
					float depth_lit = exp(-max(sea_y - M.y, 0.0) * 0.045);
					vec3 mote = mix(p.water.rgb, lit_base * depth_lit * (0.6 + 0.8 * hh.y), exp(-k * tm));
					col = mix(col, mote, clamp(cover * p.split.z, 0.0, 0.75));
				}
			}
			// la case suivante
			if (t_max.x < t_max.y && t_max.x < t_max.z) { t = t_max.x; t_max.x += t_delta.x; cell.x += int(sgn.x); }
			else if (t_max.y < t_max.z) { t = t_max.y; t_max.y += t_delta.y; cell.y += int(sgn.y); }
			else { t = t_max.z; t_max.z += t_delta.z; cell.z += int(sgn.z); }
		}
	}

	frag = vec4(mix(src.rgb, col, wet), 1.0);
}
