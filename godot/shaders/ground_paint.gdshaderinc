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
uniform vec4 u_paint_rect = vec4(0.0, 0.0, 0.0, 0.0);  // coin (scène), 1 / côté
uniform float u_paint_on = 0.0;
uniform vec3 u_grass = vec3(0.078, 0.141, 0.042);
uniform vec3 u_cobble = vec3(0.111, 0.105, 0.097);
uniform vec3 u_sand = vec3(0.584, 0.439, 0.223);

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

/* La couleur du sol en ce point : celle des sommets, recouverte de ce qu'on a
   peint. `pix` est la largeur d'un pixel au sol, en mètres. */
vec3 ground_paint(vec3 base, vec3 world, float pix) {
	if (u_paint_on < 0.5) return base;
	vec2 q = world.xz - u_paint_rect.xy;           // en mètres, depuis le coin du carré
	vec2 uv = q * u_paint_rect.zw;
	if (uv.x < 0.0 || uv.y < 0.0 || uv.x > 1.0 || uv.y > 1.0) return base;
	vec4 p = texture(u_paint, uv);
	if (p.a < 0.004) return base;
	float detail = 1.0 - smoothstep(0.06, 0.35, pix);
	vec3 w = p.rgb / max(1e-3, p.r + p.g + p.b);
	vec3 painted = vec3(0.0);
	if (w.r > 0.003) painted += w.r * paint_grass(q, detail);
	if (w.g > 0.003) painted += w.g * paint_cobble(q, detail);
	if (w.b > 0.003) painted += w.b * paint_sand(q, detail);
	return mix(base, painted, p.a);
}
