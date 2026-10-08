/* LES RIDES DE LA MER, ÉCRITES UNE SEULE FOIS.
 *
 * Le maillage de la mer ne porte que dix-huit longues vagues ; tout ce qui est
 * plus fin que quelques mètres vit ici, dans la NORMALE. Deux lecteurs : le
 * shader de la mer, qui en tire ce que la surface renvoie, et celui des
 * caustiques, qui en tire la lumière que ces mêmes rides rassemblent sur le
 * fond. C'est une ride qui fait le nœud de lumière sur le sable d'une plage —
 * la houle, à cette profondeur-là, ne courbe presque rien —, si bien que les
 * deux ne peuvent pas diverger sans que le fond cesse d'être l'ombre de sa
 * propre surface.
 *
 * Il faut naval_hash13 : inclure sky.gdshaderinc AVANT celui-ci.
 */

// force des rides, montée avec le vent : 0,40 par calme, 2,6 en tempête
uniform float u_ripple = 0.9;
// leur part, à l'œil (menu, section Mer)
uniform float u_ride_gain = 1.0;
// le vent, qui les couche : elles sont longues EN TRAVERS de lui
uniform vec2  u_wind = vec2(1.0, 0.0);

/* LE BRUIT DE PERLIN, commun aux rides et à l'écume : un bruit de gradient,
   lisse, sans grille visible, ≈ [−1, 1]. */
float perlin(vec2 p) {
	vec2 i = floor(p), f = fract(p);
	vec2 u = f * f * f * (f * (f * 6.0 - 15.0) + 10.0);
	float a0 = naval_hash13(vec3(i, 5.0)) * 6.2831853;
	float a1 = naval_hash13(vec3(i + vec2(1.0, 0.0), 5.0)) * 6.2831853;
	float a2 = naval_hash13(vec3(i + vec2(0.0, 1.0), 5.0)) * 6.2831853;
	float a3 = naval_hash13(vec3(i + vec2(1.0, 1.0), 5.0)) * 6.2831853;
	float g0 = dot(vec2(cos(a0), sin(a0)), f);
	float g1 = dot(vec2(cos(a1), sin(a1)), f - vec2(1.0, 0.0));
	float g2 = dot(vec2(cos(a2), sin(a2)), f - vec2(0.0, 1.0));
	float g3 = dot(vec2(cos(a3), sin(a3)), f - vec2(1.0, 1.0));
	return mix(mix(g0, g1, u.x), mix(g2, g3, u.x), u.y) * 1.4;   // ≈ [−1, 1]
}

/* LES RIDES. Le maillage ne porte que dix-huit longues vagues ; tout ce qui est
   plus fin que quelques mètres doit vivre dans la NORMALE, ou la mer a l'air de
   plastique moulé.

   DE LONGUES RIDES COUCHÉES EN TRAVERS DU VENT. Deux essais écartés : un fbm de
   bruit à valeurs, qui donnait des bosses molles ; puis un bruit PLISSÉ
   (1 − |2n − 1|), qui donnait bien des arêtes, mais un bruit à valeurs plissé
   dessine un RÉSEAU de nervures — une trame de cellules, relevée sur les
   captures, et du papier d'aluminium froissé dès qu'on pousse les rides. Ici
   un bruit de Perlin SIGNÉ et lisse, étiré trois fois en travers du vent : des
   ondulations longues et continues, sans pli ni maille. Trois octaves, tournées
   de 0,38 rad l'une de l'autre pour que les trains se croisent, chacune poussée
   à sa vitesse (les petites plus lentes, c ∝ √λ), et d'amplitude décroissant
   plus vite que leur fréquence ne croît, pour que la pente soit portée par la
   plus longue et que les fines ne fassent que la troubler. */
float ride_h(vec2 p) {
	vec2 w = u_wind;
	vec2 x = vec2(dot(p, w), dot(p, vec2(-w.y, w.x)));   // le long du vent, en travers
	float h = 0.0, amp = 1.0, f = 0.35, c = 1.25;
	for (int i = 0; i < 3; i++) {
		float an = (float(i) - 1.0) * 0.38;
		vec2 r = vec2(cos(an) * x.x - sin(an) * x.y, sin(an) * x.x + cos(an) * x.y);
		vec2 q = vec2((r.x - u_time * c) * f, r.y * f * 0.33) + vec2(float(i) * 17.3, float(i) * 5.1);
		h += perlin(q) * amp;
		amp *= 0.4; f *= 2.1; c *= 0.69;
	}
	return h;
}

/* LES RIDES EN TEXTURE (tools/sea-ripples.js ; world/materiaux.json → mer.rides) : les pentes
   d'un vrai spectre de vagues de vent (Phillips, Tessendorf), carrelables et bouclées dans le
   temps, calées sur Cox et Munk. Chargées, elles remplacent le bruit de Perlin ci-dessus — pour
   la mer ET pour le fond (caustics.gdshaderinc), une seule définition. Deux nappes : la carte à sa
   taille, et la même trois fois plus grande, tournée d'une demi-radian et décalée dans le temps,
   qui comble l'écart jusqu'aux plus courtes des dix-huit longues vagues par gros temps.
   En GLOBAL : cinq shaders lisent les rides, et la carte se pose une fois (GroundMaterials). */
global uniform sampler2DArray naval_ride_map : filter_linear_mipmap, repeat_enable;
global uniform vec4 naval_ride_a;   // x : 1 si la carte est chargée ; y : images ; z : période (s) ; w : taille (m)
global uniform vec4 naval_ride_b;   // x : pente_max ; y : force ; z : échelle de la seconde nappe ; w : son poids

// la pente de la carte au point uvm (mètres, le long du vent puis en travers), à l'image du moment
vec2 ride_tex_at(vec2 uvm, float toff, float lod) {
	float fr = fract((u_time + toff) / naval_ride_a.z) * naval_ride_a.y;
	float f0 = floor(fr), k = fr - f0, f1 = mod(f0 + 1.0, naval_ride_a.y);
	vec2 uv = uvm / naval_ride_a.w;
	vec2 s0, s1;
	if (lod < 0.0) {
		s0 = texture(naval_ride_map, vec3(uv, f0)).rg;
		s1 = texture(naval_ride_map, vec3(uv, f1)).rg;
	} else {
		s0 = textureLod(naval_ride_map, vec3(uv, f0), lod).rg;
		s1 = textureLod(naval_ride_map, vec3(uv, f1), lod).rg;
	}
	return (mix(s0, s1, k) * (255.0 / 127.0) - 128.0 / 127.0) * naval_ride_b.x;
}

/* Les deux nappes, dans le repère du vent. La seconde est la carte agrandie : ses pentes sont
   celles de la carte (une mer de vent est à peu près semblable à elle-même), ramenées dans le
   repère de la première par la rotation inverse. Leur somme garde la pente moyenne de Cox et Munk. */
vec2 ride_tex_slope(vec2 x, float lod) {
	vec2 a = ride_tex_at(x, 0.0, lod);
	float sc = naval_ride_b.z, c = cos(0.5), s = sin(0.5);
	vec2 xr = vec2(c * x.x - s * x.y, s * x.x + c * x.y) / sc;
	vec2 b = ride_tex_at(xr, naval_ride_a.z * 0.37, lod >= 0.0 ? max(0.0, lod - log2(sc)) : -1.0);
	b = vec2(c * b.x + s * b.y, -s * b.x + c * b.y);
	return (a + naval_ride_b.w * b) * inversesqrt(1.0 + naval_ride_b.w * naval_ride_b.w);
}

// la pente des rides au point p du monde, en x et z du monde ; lod < 0 : celui du pixel
vec2 ride_tex_grad(vec2 p, float lod) {
	vec2 w = u_wind, pw = vec2(-w.y, w.x);
	vec2 g = ride_tex_slope(vec2(dot(p, w), dot(p, pw)), lod);
	return g.x * w + g.y * pw;
}

/* La force : la pente de la carte est celle d'un vent de référence ; u_ripple dit le vent du
   moment (0,9 par défaut, comme le bruit), la fiche peut la régler (« force »). */
float ride_tex_gain() { return u_ripple / 0.9 * u_ride_gain * naval_ride_b.y; }

vec3 ride_normale(vec2 p, float fade) {
	if (fade <= 0.001) return vec3(0.0, 1.0, 0.0);
	if (naval_ride_a.x > 0.5) {
		vec2 g = ride_tex_grad(p, -1.0) * ride_tex_gain() * fade;
		return normalize(vec3(-g.x, 1.0, -g.y));
	}
	float e = 0.2;
	float h  = ride_h(p);
	float hx = ride_h(p + vec2(e, 0.0));
	float hz = ride_h(p + vec2(0.0, e));
	float s = 0.5 * u_ripple * u_ride_gain * fade / e;
	return normalize(vec3(-(hx - h) * s, 1.0, -(hz - h) * s));
}
