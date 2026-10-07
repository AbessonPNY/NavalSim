/* LE SOL PEINT — herbe, pavés, sable, posés à la main par-dessus la teinte que
 * le relief donne de lui-même (LandNode.Tint, aux sommets, cinq mètres de pas).
 *
 * L'image de peinture (GroundPaint.cs) dit, par demi-mètre, combien de chacun
 * (rgb) et combien elle recouvre (a). Ici, chacun devient une MATIÈRE, dessinée
 * et non lue dans une texture : une herbe en touffes, des pavés en cellules
 * jointoyées, un sable à grain fin. Rien à charger, et la finesse va jusqu'au
 * pixel qu'on regarde.
 *
 * LES COULEURS VIENNENT DE LandNode (u_grass, u_cobble, u_sand) : une seule
 * définition, la même que celle des sommets — une rue peinte à côté d'une rue
 * tracée par la grille garde le même gris.
 *
 * ANCRÉ AU MONDE : les motifs se calculent en mètres depuis le coin du carré,
 * qui ne bouge pas quand l'origine glisse. Et ils s'EFFACENT avec la distance,
 * vers leur couleur moyenne, quand un pixel couvre plus qu'un pavé : sans quoi
 * un pavage vu de loin scintille.
 */

uniform sampler2D u_paint : filter_linear, repeat_disable;
// la seconde couche, celle des FONDS : r sable blanc, g vase, b herbier
uniform sampler2D u_paint2 : filter_linear, repeat_disable;
uniform vec4 u_paint_rect = vec4(0.0, 0.0, 0.0, 0.0);  // coin (scène), 1 / côté
uniform float u_paint_on = 0.0;
/* l'origine en mètres vrais : les textures des matières y sont ancrées, la peinture
   comme le sol de lui-même — un sable peint sur du sable ne montre pas de couture */
uniform vec2 u_true = vec2(0.0);
uniform vec3 u_grass = vec3(0.078, 0.141, 0.042);
uniform vec3 u_cobble = vec3(0.111, 0.105, 0.097);
uniform vec3 u_sand = vec3(0.584, 0.439, 0.223);
uniform vec3 u_whitesand = vec3(0.79, 0.73, 0.57);
uniform vec3 u_mud = vec3(0.07, 0.065, 0.04);
uniform vec3 u_seagrass = vec3(0.03, 0.06, 0.02);
uniform vec3 u_wood = vec3(0.034, 0.081, 0.025);

vec2 paint_hash22(vec2 p) {
	vec3 a = fract(vec3(p.xyx) * vec3(0.1031, 0.1030, 0.0973));
	a += dot(a, a.yzx + 33.33);
	return fract((a.xx + a.yz) * a.zy);
}

float paint_noise(vec2 p) {
	vec2 i = floor(p), f = fract(p);
	vec2 u = f * f * (3.0 - 2.0 * f);
	float a = paint_hash22(i).x, b = paint_hash22(i + vec2(1.0, 0.0)).x;
	float c = paint_hash22(i + vec2(0.0, 1.0)).x, d = paint_hash22(i + vec2(1.0, 1.0)).x;
	return mix(mix(a, b, u.x), mix(c, d, u.x), u.y);
}

/* L'HERBE : des touffes à deux échelles, et des plaques plus sèches tous les
   quelques mètres — un pré au soleil n'est jamais d'un seul vert. */
vec3 paint_grass(vec2 q, float detail) {
	float tuft = paint_noise(q * 2.3) * 0.6 + paint_noise(q * 7.1) * 0.4;
	float dry = smoothstep(0.55, 0.85, paint_noise(q * 0.18));
	vec3 g = u_grass * mix(1.0, 0.72 + 0.56 * tuft, detail);
	return mix(g, g * vec3(1.55, 1.35, 0.9), dry * 0.55);
}

/* LES PAVÉS : un pavé par cellule de Voronoï, un peu plus d'un quart de mètre,
   chacun sa teinte de pierre, et le joint sombre là où deux cellules se
   touchent (F2 − F1 petit). */
vec3 paint_cobble(vec2 q, float detail) {
	vec2 p = q * 3.6;
	vec2 i = floor(p), f = fract(p);
	float f1 = 8.0, f2 = 8.0;
	vec2 cell = vec2(0.0);
	for (int y = -1; y <= 1; y++)
		for (int x = -1; x <= 1; x++) {
			vec2 o = vec2(float(x), float(y));
			vec2 r = o + paint_hash22(i + o) * 0.8 + 0.1 - f;
			float d = dot(r, r);
			if (d < f1) { f2 = f1; f1 = d; cell = i + o; }
			else if (d < f2) f2 = d;
		}
	float edge = sqrt(f2) - sqrt(f1);
	float stone = 0.72 + 0.56 * paint_hash22(cell + 17.0).x;
	float joint = smoothstep(0.03, 0.11, edge);
	vec3 c = u_cobble * stone * mix(0.45, 1.0, joint);
	// au loin, la moyenne : la pierre et un joint sur cinq
	return mix(u_cobble * 0.9, c, detail);
}

/* LE SABLE : un grain fin, et des rides lâches que le vent y laisse. */
vec3 paint_sand(vec2 q, float detail) {
	float grain = paint_noise(q * 23.0) * 0.5 + paint_noise(q * 5.0) * 0.5;
	float ripple = sin(q.x * 2.1 + paint_noise(q * 0.7) * 6.0) * 0.5 + 0.5;
	return u_sand * mix(1.0, 0.86 + 0.18 * grain + 0.06 * ripple, detail);
}

/* LE SABLE BLANC des hauts-fonds coralliens : celui qui fait l'eau turquoise.
   Le même grain que le sable de la grève, plus fin et plus clair. */
vec3 paint_whitesand(vec2 q, float detail) {
	float grain = paint_noise(q * 31.0) * 0.5 + paint_noise(q * 7.0) * 0.5;
	float ripple = sin(q.y * 1.7 + paint_noise(q * 0.5) * 7.0) * 0.5 + 0.5;
	return u_whitesand * mix(1.0, 0.9 + 0.12 * grain + 0.06 * ripple, detail);
}

/* LA VASE d'un bassin : lisse, sombre, en nappes plus ou moins chargées — ce que
   dépose une rade où l'eau ne bouge pas. */
vec3 paint_mud(vec2 q, float detail) {
	float blot = paint_noise(q * 0.35) * 0.6 + paint_noise(q * 1.6) * 0.4;
	return u_mud * mix(1.0, 0.75 + 0.55 * blot, max(detail, 0.6));
}

/* L'HERBIER : des touffes de posidonies, denses par plaques, et le sable entre
   elles. Les feuilles couchées par le courant font des traits dans un sens. */
vec3 paint_seagrass(vec2 q, float detail) {
	float clump = smoothstep(0.35, 0.65, paint_noise(q * 0.45) * 0.7 + paint_noise(q * 2.2) * 0.3);
	float blade = paint_noise(vec2(q.x * 9.0, q.y * 1.5));
	vec3 grass = u_seagrass * mix(1.0, 0.7 + 0.6 * blade, detail);
	return mix(u_whitesand * 0.55, grass, clump);
}

/* ------------------------------------------------------------------ */
/*  LES MATIÈRES DU SOL (world/materiaux.json, GroundMaterials.cs)      */
/* ------------------------------------------------------------------ */

/* UNE SURFACE, et non plus une couleur : ce qu'une matière PBR apporte — sa couleur,
   sa normale (en repère tangent, xy), sa rugosité, son métal et sa HAUTEUR, qui décide
   d'où passe la frontière entre deux matières (le grain du sable perce l'herbe, il ne
   se fond pas dedans). « tex » : vient d'une texture ; deux surfaces dessinées se
   mélangent comme avant, au fondu. */
struct Surf { vec3 col; vec2 n; float rough; float metal; float h; float tex; };

/* LES MATIÈRES, dans l'ordre de GroundMaterials.Keys : 0 herbe, 1 pavés, 2 sable,
   3 sable blanc, 4 vase, 5 herbier, 6 roche, 7 forêt. Une matière texturée a sa
   couche dans les deux piles (A : couleur + hauteur ; B : normale xy, rugosité, métal),
   une matière dessinée a 0 et garde son dessin (couche + 1 : un tableau d'uniformes
   vaut zéro tant qu'on ne l'a pas poussé). */
#define MATS 8
uniform sampler2DArray u_mat_a : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2DArray u_mat_b : filter_linear_mipmap_anisotropic, repeat_enable;
uniform int u_mat_layer[MATS];       // la couche + 1 ; 0 : pas de texture (un tableau d'uniformes n'a pas de défaut)
uniform float u_mat_size[MATS];      // mètres couverts par une répétition de la texture
uniform float u_mat_rough[MATS];
uniform float u_mat_metal[MATS];
uniform float u_mat_nstr[MATS];      // la force de la normale
uniform float u_mat_nflip[MATS];     // 1 : normale DirectX (vert vers le bas)
uniform vec3 u_mat_tint[MATS];
uniform float u_mat_any = 0.0;       // au moins une matière texturée : la terre se recalcule

Surf surf_flat(vec3 col, int id) {
	Surf s; s.col = col; s.n = vec2(0.0); s.rough = u_mat_rough[id]; s.metal = u_mat_metal[id]; s.h = 0.5; s.tex = 0.0;
	return s;
}

/* LIRE UNE MATIÈRE TEXTURÉE, sans que sa répétition se voie : une seconde lecture,
   tournée et plus grande, mêlée à la première par plaques de quelques répétitions —
   une plage de cent mètres ne montre plus son carreau de deux. */
Surf surf_tex(int id, vec2 q) {
	float layer = float(u_mat_layer[id] - 1), sz = max(0.05, u_mat_size[id]);
	vec2 uv1 = q / sz;
	vec2 uv2 = mat2(vec2(0.8, 0.6), vec2(-0.6, 0.8)) * q / (sz * 2.3) + vec2(0.37, 0.71);
	float k = smoothstep(0.3, 0.7, paint_noise(q / (sz * 3.1)));
	vec4 a = mix(texture(u_mat_a, vec3(uv1, layer)), texture(u_mat_a, vec3(uv2, layer)), k);
	vec4 b = mix(texture(u_mat_b, vec3(uv1, layer)), texture(u_mat_b, vec3(uv2, layer)), k);
	Surf s;
	s.col = a.rgb * u_mat_tint[id];
	s.h = a.a;
	s.n = (b.rg * 2.0 - 1.0) * u_mat_nstr[id];
	if (u_mat_nflip[id] > 0.5) s.n.y = -s.n.y;
	s.rough = b.b; s.metal = b.a; s.tex = 1.0;
	return s;
}

/* MÊLER DEUX SURFACES. Dessinées toutes deux : le fondu d'avant. Si l'une est une
   texture : par la hauteur — celle qui monte le plus haut au point l'emporte, sur une
   bande étroite autour du mélange voulu. */
Surf surf_mix(Surf a, Surf b, float t) {
	if (t <= 0.001) return a;
	if (t >= 0.999) return b;
	float k = t;
	if (a.tex + b.tex > 0.0) {
		float ma = a.h + (1.0 - t), mb = b.h + t;
		float m = max(ma, mb) - 0.2;
		float wa = max(ma - m, 0.0), wb = max(mb - m, 0.0);
		k = wb / max(wa + wb, 1e-4);
	}
	Surf r;
	r.col = mix(a.col, b.col, k); r.n = mix(a.n, b.n, k);
	r.rough = mix(a.rough, b.rough, k); r.metal = mix(a.metal, b.metal, k);
	r.h = mix(a.h, b.h, k); r.tex = max(a.tex, b.tex);
	return r;
}

/* chaque matière : sa texture si elle en a une (lue en mètres vrais, tq), sinon son
   dessin (lu en q, son repère à lui) */
Surf m_grass(vec2 q, vec2 tq, float detail)     { return u_mat_layer[0] > 0 ? surf_tex(0, tq) : surf_flat(paint_grass(q, detail), 0); }
Surf m_cobble(vec2 q, vec2 tq, float detail)    { return u_mat_layer[1] > 0 ? surf_tex(1, tq) : surf_flat(paint_cobble(q, detail), 1); }
Surf m_sand(vec2 q, vec2 tq, float detail)      { return u_mat_layer[2] > 0 ? surf_tex(2, tq) : surf_flat(paint_sand(q, detail), 2); }
Surf m_whitesand(vec2 q, vec2 tq, float detail) { return u_mat_layer[3] > 0 ? surf_tex(3, tq) : surf_flat(paint_whitesand(q, detail), 3); }
Surf m_mud(vec2 q, vec2 tq, float detail)       { return u_mat_layer[4] > 0 ? surf_tex(4, tq) : surf_flat(paint_mud(q, detail), 4); }
Surf m_seagrass(vec2 q, vec2 tq, float detail)  { return u_mat_layer[5] > 0 ? surf_tex(5, tq) : surf_flat(paint_seagrass(q, detail), 5); }

/* Une somme pondérée de surfaces (les matières d'un pixel peint) — le poids total vaut un. */
Surf surf_acc(Surf acc, Surf s, float w) {
	acc.col += s.col * w; acc.n += s.n * w; acc.rough += s.rough * w; acc.metal += s.metal * w;
	acc.h += s.h * w; acc.tex = max(acc.tex, s.tex * step(0.001, w));
	return acc;
}
Surf surf_zero() { Surf s; s.col = vec3(0.0); s.n = vec2(0.0); s.rough = 0.0; s.metal = 0.0; s.h = 0.0; s.tex = 0.0; return s; }

/* Le sol en ce point : celui de dessous, recouvert de ce qu'on a peint. `pix` est la
   largeur d'un pixel au sol, en mètres. */
Surf ground_paint(Surf base, vec3 world, float pix) {
	if (u_paint_on < 0.5) return base;
	vec2 q = world.xz - u_paint_rect.xy;           // en mètres, depuis le coin du carré
	vec2 uv = q * u_paint_rect.zw;
	if (uv.x < 0.0 || uv.y < 0.0 || uv.x > 1.0 || uv.y > 1.0) return base;
	vec4 p = texture(u_paint, uv);
	vec4 s = texture(u_paint2, uv);
	if (p.a < 0.004 && s.a < 0.004) return base;
	float detail = 1.0 - smoothstep(0.06, 0.35, pix);
	vec2 tq = world.xz + u_true;
	Surf out_s = base;
	if (p.a >= 0.004) {
		vec3 w = p.rgb / max(1e-3, p.r + p.g + p.b);
		Surf painted = surf_zero();
		if (w.r > 0.003) painted = surf_acc(painted, m_grass(q, tq, detail), w.r);
		if (w.g > 0.003) painted = surf_acc(painted, m_cobble(q, tq, detail), w.g);
		if (w.b > 0.003) painted = surf_acc(painted, m_sand(q, tq, detail), w.b);
		out_s = surf_mix(out_s, painted, p.a);
	}
	// les fonds par-dessus : le pinceau de l'une efface l'autre, l'ordre ne compte guère
	if (s.a >= 0.004) {
		vec3 w = s.rgb / max(1e-3, s.r + s.g + s.b);
		Surf painted = surf_zero();
		if (w.r > 0.003) painted = surf_acc(painted, m_whitesand(q, tq, detail), w.r);
		if (w.g > 0.003) painted = surf_acc(painted, m_mud(q, tq, detail), w.g);
		if (w.b > 0.003) painted = surf_acc(painted, m_seagrass(q, tq, detail), w.b);
		out_s = surf_mix(out_s, painted, s.a);
	}
	return out_s;
}
