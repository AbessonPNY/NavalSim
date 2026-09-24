/* LA MER.
 *
 * Le déplacement vient de gerstner.gdshaderinc, le ciel de sky.gdshaderinc, la
 * brume de haze.gdshaderinc — trois définitions uniques, partagées. Ce fichier
 * n'ajoute que ce qui lui appartient en propre : le remaillage vers la caméra,
 * les rides, et la façon dont l'eau renvoie ce ciel-là.
 *
 * `unshaded` n'est pas un raccourci : cette mer est ENTIÈREMENT analytique, comme
 * l'originale. Sa couleur est du ciel réfléchi plus un corps d'eau, calculés ici ;
 * laisser Godot l'éclairer par-dessus compterait la même lumière deux fois.
 */
shader_type spatial;

/* world_vertex_coords : VERTEX arrive et repart en coordonnées MONDE.
 *
 * Ce n'est pas une commodité, c'est l'invariant. La phase de Gerstner doit être
 * prise en espace monde parce que le plan de mer est recentré sur la caméra à
 * chaque image ; en local, la houle resterait collée à la caméra et se
 * désynchroniserait du champ de hauteur que le solveur échantillonne. */
render_mode world_vertex_coords, cull_disabled, unshaded, depth_draw_always;

#include "res://shaders/gerstner.gdshaderinc"
#include "res://shaders/shelter.gdshaderinc"
#include "res://shaders/sky.gdshaderinc"
#include "res://shaders/ride.gdshaderinc"
#include "res://shaders/haze.gdshaderinc"
#include "res://shaders/hull_gap.gdshaderinc"

uniform float u_half = 3500.0;
uniform float u_seg = 384.0;
uniform vec3  u_centre = vec3(0.0);

/* LES COULEURS DE L EAU, EN LINEAIRE, ET C EST TOUT LE PIEGE.

   Cote JavaScript elles sont ecrites en hex — 0x0e3347, 0x2f8fa8, 0x2f9e86 —
   que three.js lit comme du sRGB et convertit en espace lineaire de travail.
   Ce sont donc CES valeurs-ci que le shader voit, et non les composantes du hex.

   Je les avais d abord inventees a l oeil, et l eau claire en sortait deux fois
   trop sombre et desaturee : la mer virait au gris, sur lequel la moindre ecume
   ressortait comme de la peinture. Converties proprement, elle retrouve son bleu.

   Declarees SANS `source_color` a dessein : ces nombres sont DEJA lineaires, et
   l indication ferait convertir Godot une seconde fois. */
uniform vec3 u_deep    = vec3(0.00439, 0.03310, 0.06301);   // 0x0e3347
uniform vec3 u_shallow = vec3(0.02843, 0.27468, 0.39152);   // 0x2f8fa8
uniform vec3 u_sss     = vec3(0.02843, 0.34190, 0.23843);   // 0x2f9e86

/* LES RÉGLAGES DE MISE AU POINT (menu, section Mer) : la rugosité de l'eau, qui
   monte avec le vent, le flou du ciel dans le miroir qui la suit, et la part des
   rides, des moutons et de l'écume en traits. */
uniform float u_rough_base = 0.055;
uniform float u_rough_wind = 0.15;
uniform float u_sky_blur = 0.6;
uniform float u_cap_gain = 1.0;
uniform float u_foam_gain = 1.0;
uniform float u_jac_foam = 0.85;
uniform float u_streak_gain = 1.0;
uniform float u_kelvin_gain = 1.0;
uniform float u_submerged = 0.0;    // l'œil est sous la surface  // le V de Kelvin : relief et ombrage
/* Les bras de Kelvin de chaque navire, tirés de la route de son étrave
   (OceanNode.Kelvin) : une rangée par bras, (x, z, âge en mètres, valide). */
uniform sampler2D u_kelvin : filter_nearest, repeat_disable;
const int KELVIN_N = 48;  // les stries qui suivent la pente
uniform vec3  u_sun_col = vec3(1.0, 0.95, 0.86);   // SkyNode.PushTo : couleur × intensité × 0,5     // où l'écume commence : un jacobien sous ce seuil
// hauteur de reference de la lueur de crete, a l echelle de l etat de mer :
// le noyau la calcule deja (Ocean.AmpMax), on ne la redecide pas ici
uniform float u_amp_max = 1.0;

/* LA NUIT SUR L'EAU. u_water_light : la lumière qui est dans l'eau pour en
   ressortir — 1 le jour, un peu moins soleil bas, très peu la nuit, un peu plus
   sous une lune (Sky.WaterLight). u_moon, u_moon_lit : la lune, et ce qu'elle
   pose de lumière sur la mer, sa route. */
uniform float u_water_light = 1.0;
uniform vec3  u_moon = vec3(0.0, 1.0, 0.0);
uniform float u_moon_lit = 0.0;

/* LES FEUX DU BORD, que la mer ne verrait pas autrement : elle est `unshaded`,
   calculée tout entière ici, et aucune lampe du moteur ne l'atteint. Position et
   énergie de chaque feu (xyz, w), sa portée à part. */
#define NLAMP 8
uniform vec4  u_lamp[NLAMP];
uniform float u_lamp_range[NLAMP];
uniform int   u_lamp_count = 0;
// leurs deux forces, réglables (reglages.ini, [lanternes])
uniform float u_lamp_reflection = 2.4;
uniform float u_lamp_water = 0.6;
// 0xffb765, la flamme de _lanternAt, en linéaire
const vec3 LAMP_COL = vec3(1.0, 0.473, 0.130);

/* CE QUI EST SOUS LA SURFACE, VU À TRAVERS ELLE. L'image et la profondeur déjà
   rendues — la coque comprise, dessinée avant la mer (voir la fin du shader).
   u_absorb : l'extinction par canal et par mètre ; u_refract : Snell, 1 pour de
   l'eau de mer — un cadran, pas une rustine. */
uniform sampler2D u_screen : hint_screen_texture, filter_linear;
uniform sampler2D u_depth : hint_depth_texture, filter_nearest;
uniform vec3  u_absorb = vec3(0.34, 0.13, 0.085);
uniform float u_refract = 1.0;
/* LE VOILE DE SURFACE : la part de ce qui est dessous qui passe À ÉPAISSEUR
   NULLE. L'extinction seule ne coupe rien au ras de la flottaison — sous dix
   centimètres d'eau, exp(−0,34 × 0,1) laisse passer 97 % — et le bordé immergé
   se lisait comme au travers d'une vitre (signalé). Une vraie surface diffuse
   dès le premier centimètre : les rides brisent l'image, et une eau de rade
   porte toujours sa part de particules. 1 : une vitre ; 0 : de l'encre. */
uniform float u_veil = 0.55;
/* L'OMBRE DES COQUES SUR L'EAU : la part de la lumière de l'eau qu'une coque
   retire là où elle cache le soleil. 0 : pas d'ombre ; 1 : de l'encre. La mer
   n'est pas éclairée par le moteur (unshaded) et ne reçoit donc aucune carte
   d'ombre : l'ombre est calculée ici, contre le contour de flottaison que la
   mer connaît déjà, élevé à la hauteur des hauts de chaque coque. */
uniform float u_shadow = 0.5;
// la part du soleil qui passe encore (SkyNode.Sunlit) : sous un ciel fermé, plus d'ombre franche
uniform float u_sunlit = 1.0;
uniform float u_ship_top[NSHIP];      // ses hauts au-dessus de la flottaison, mètres
// la décharge au-dessus éclaire l'eau comme le ciel
uniform float u_flash = 0.0;

// le champ d'écume persistant (FoamField) : ce que les crêtes ont laissé
uniform sampler2D u_foam_tex : filter_linear, repeat_disable;
uniform vec2  u_foam_origin = vec2(0.0);
uniform float u_foam_size = 620.0;
uniform float u_foam_on = 0.0;

varying vec3 v_world;
varying vec3 v_nrm;
varying float v_foam;
varying float v_rel;
varying float v_spacing;

void vertex() {
	/* DÉPENSER LES SOMMETS LÀ OÙ ILS SONT VUS. La grille est uniforme dans le
	   tampon, donc on la ramène vers le centre : les quadrilatères font environ
	   un mètre le long de la coque et des dizaines de mètres à l'horizon, pour
	   le même nombre de triangles. */
	vec2 local = VERTEX.xz - u_centre.xz;
	vec2 q = local / u_half;
	vec2 aq = abs(q);
	local = sign(q) * (aq * (0.06 + 0.94 * aq * aq)) * u_half;

	/* Combien de mètres ce quadrilatère couvre, tiré de la dérivée du remaillage.
	   Tout ce qui suit est borné en bande passante contre CELA, et jamais contre
	   la distance : un seul critère, donc aucune couture ne peut apparaître là où
	   deux seuils se seraient contredits. */
	vec2 dsp = (vec2(0.06) + 2.82 * aq * aq) * (u_half * 2.0 / u_seg);
	float spacing = max(dsp.x, dsp.y);
	v_spacing = spacing;

	vec3 w0 = vec3(local.x + u_centre.x, 0.0, local.y + u_centre.z);
	// ce qui reste de la mer derrière un môle, en mètres MONDE
	float shelter = shelter_at(w0.xz);

	vec3 disp;
	vec3 nrm_acc;
	float jacobian;
	float height;
	gerstner_sum(w0.xz, u_time, shelter, spacing, disp, nrm_acc, jacobian, height);

	VERTEX = w0 + disp;
	v_nrm = gerstner_normal(nrm_acc);
	NORMAL = v_nrm;

	v_world = VERTEX;
	/* L'eau battue : là où la surface se tasse (voir gerstner_jacobian). Rien au
	   seuil u_jac_foam, tout 0,25 plus bas ; 0 serait la crête qui se replie.
	   Mesuré sur la houle du noyau (400 × 400 m, pas d'un mètre) : nos vagues
	   sont douces, J ne passe sous 0,7 que sur 1 à 3,5 % de la mer de force 5
	   à 8, et presque jamais sous 0,5 — d'où un seuil à 0,75 et non à 0,5. */
	v_foam = smoothstep(u_jac_foam, u_jac_foam - 0.25, jacobian);
	v_rel = height;
}

// ---------------------------------------------------------------------------
//  LES RIDES
// ---------------------------------------------------------------------------

float onde_hash(vec2 p) { return naval_hash13(vec3(p, 3.0)); }

float onde_noise(vec2 p) {
	vec2 i = floor(p);
	vec2 f = fract(p);
	f = f * f * (3.0 - 2.0 * f);
	return mix(mix(onde_hash(i), onde_hash(i + vec2(1, 0)), f.x),
	           mix(onde_hash(i + vec2(0, 1)), onde_hash(i + vec2(1, 1)), f.x), f.y);
}

float onde_fbm(vec2 p) {
	return onde_noise(p) * 0.55 + onde_noise(p * 2.03 + 11.0) * 0.28
	     + onde_noise(p * 4.11 - 7.0) * 0.17;
}

/* L'ÉCUME EN TRAITS. Ce qu'on veut voir d'une grosse mer, dessiné comme un
   film d'animation le ferait : des traits blancs qui ondulent sur le HAUT des
   vagues, granuleux comme de la craie, sur des pentes sombres (d'après une
   retouche d'Arnaud).

   Deux essais écartés : un semis de bruit lisse (des taches) et un réseau
   cellulaire de Worley — il donnait une peau de crocodile, des mailles de
   même taille sur toute la mer. Ici, un BRUIT DE PERLIN : ses lignes de
   passage par zéro sont continues, sinueuses, sans maille ni grille — des
   traits. Leur épaisseur suit ce que l'eau est battue : des fils sur la
   frange, des bandes au sommet. Étirés en travers du vent comme les crêtes,
   gauchis pour ne pas être réguliers, un second jeu plus fin là où c'est le
   plus battu, et un pointillé de bulles qui ne vit que de près. Rend 1 sur
   l'écume. */

// un trait : là où le bruit passe par zéro, d'une largeur w (en part de l'amplitude)
float foam_stroke(vec2 q, float w) {
	float n = abs(perlin(q) + 0.5 * perlin(q * 2.03 + 9.1));
	return 1.0 - smoothstep(w * 0.45, w, n);
}

float lace(vec2 p, float fill, float fine) {
	vec2 w = u_wind;
	vec2 x = vec2(dot(p, w), dot(p, vec2(-w.y, w.x)));
	// en travers du vent, deux fois plus long ; dérive avec lui
	vec2 q = vec2(x.x - u_time * 0.35, x.y * 0.45) * 0.16;
	q += (vec2(perlin(q * 0.37 + 3.1), perlin(q * 0.37 - 5.3))) * 0.9;
	float foam = foam_stroke(q, mix(0.07, 0.55, fill));
	foam = max(foam, foam_stroke(q * 2.3 + 17.0, mix(0.03, 0.3, fill)) * smoothstep(0.25, 0.8, fill) * fine);
	/* Le grain des bulles, de près seulement : un bruit LISSÉ à trois par mètre,
	   seuillé en douceur. Un premier pointillé en cases (floor) sortait en
	   damier de pixels, et clignotait d'être retiré trois fois par seconde —
	   relevé sur une capture. */
	float g = onde_noise(p * 3.0 + u_wind * u_time * 0.15);
	return foam * mix(1.0, smoothstep(0.28, 0.62, g), 0.45 * fine);
}

// la profondeur de VUE, en mètres, de ce qui est déjà rendu à ce point de l'écran
// (la matrice est passée : les intégrées n'existent que dans fragment())
float view_depth(vec2 uv, mat4 inv_proj) {
	float d = textureLod(u_depth, uv, 0.0).r;
	vec4 v = inv_proj * vec4(uv * 2.0 - 1.0, d, 1.0);
	return -v.z / v.w;
}

/* LE REFLET DES COQUES — ce que la page obtient en rendant le monde une seconde
   fois depuis un œil en miroir sous la surface. Ici il est cherché DANS L'IMAGE
   déjà rendue, que la mer relit de toute façon pour la coque vue à travers
   l'eau : le rayon renvoyé par la surface est suivi pas à pas dans l'espace de
   vue, projeté à chaque pas sur l'écran, et comparé à la profondeur de ce qui
   s'y trouve ; là où il passe derrière une surface opaque, il l'a touchée, et
   c'est sa couleur qui se reflète.

   Pas de seconde passe, et pas de plan de coupe — que Godot n'offre pas pour les
   matières d'un modèle : un rayon renvoyé MONTE, il ne peut pas rencontrer le
   bas d'une coque sous l'eau. Le prix : ce qui est hors de l'image ne se reflète
   pas. Le reflet s'efface donc vers les bords de l'écran au lieu de s'y couper.
   Rend (couleur encodée, part touchée). */
vec4 hull_mirror(vec3 P, vec3 Rv, mat4 proj, mat4 inv_proj) {
	float t = 0.6, prev_t = 0.0;
	for (int i = 0; i < 40; i++) {
		vec3 Q = P + Rv * t;
		if (Q.z > -0.05) break;                         // passé derrière l'œil
		vec4 cl = proj * vec4(Q, 1.0);
		vec2 uv = cl.xy / cl.w * 0.5 + 0.5;
		if (uv.x < 0.0 || uv.x > 1.0 || uv.y < 0.0 || uv.y > 1.0) break;
		float scene = view_depth(uv, inv_proj);
		float ray = -Q.z;
		// derrière une surface, et pas trop loin derrière : c'est elle qu'il a touchée
		if (ray > scene && ray - scene < 0.6 + 0.08 * t) {
			// affiner entre le pas d'avant et celui-ci
			float a = prev_t, b = t;
			for (int k = 0; k < 5; k++) {
				float m = 0.5 * (a + b);
				vec3 Qm = P + Rv * m;
				vec4 cm = proj * vec4(Qm, 1.0);
				vec2 um = cm.xy / cm.w * 0.5 + 0.5;
				if (-Qm.z > view_depth(um, inv_proj)) b = m; else a = m;
			}
			vec3 Qb = P + Rv * b;
			vec4 cb = proj * vec4(Qb, 1.0);
			vec2 ub = cb.xy / cb.w * 0.5 + 0.5;
			// s'efface vers les bords de l'image et avec la longueur du rayon
			vec2 edge = smoothstep(0.0, 0.08, ub) * (1.0 - smoothstep(0.92, 1.0, ub));
			float w = edge.x * edge.y * (1.0 - smoothstep(60.0, 140.0, b));
			return vec4(naval_encode(textureLod(u_screen, ub, 0.0).rgb), w);
		}
		prev_t = t;
		t = t * 1.13 + 0.25;
		if (t > 140.0) break;
	}
	return vec4(0.0);
}

/* LE V DE KELVIN, EN RELIEF. Un sillage de Kelvin n'est pas de l'écume — une
   première version le dessinait en traits blancs, et c'était faux : ce sont de
   petites VAGUES, de courtes crêtes posées en échelon le long de deux bras à
   19,5° de la route, qu'on voit parce qu'elles prennent la lumière. Seule
   l'étrave écume, et elle a déjà la sienne.

   Les bras sont lus dans u_kelvin, tirés de la route PASSÉE de l'étrave
   (OceanNode.Kelvin) : en virage ils se courbent avec elle. Le long de chacun,
   une bande d'ondes DIVERGENTES : crêtes tournées d'environ 35° sur le bras,
   de longueur d'onde réglée par la vitesse (l'onde transverse d'un navire à U
   mesure 2πU²/g — dix mètres à 4 m/s —, les divergentes à peu près la moitié).
   La bande s'élargit en vieillissant et s'éteint au loin.

   ET ELLES SE LISENT PAR LEUR OMBRE. Sous un ciel uniforme, pencher la normale
   ne change presque rien au ciel renvoyé : le premier relief ne se voyait pas.
   Ce qui dessine un V sur une photo aérienne, ce sont des bandes claires et
   sombres — la face d'une crête tournée vers le ciel, le dos à l'ombre de la
   suivante. D'où un troisième terme : l'ombrage, comme le creux sombre et la
   crête claire des grandes vagues.

   Rend (∂h/∂x, ∂h/∂z, ombrage) : la pente à retirer de la normale, et de
   combien éclaircir (positif) ou assombrir l'eau. */
vec3 kelvin_slope(vec2 p, float pix) {
	vec2 g = vec2(0.0);
	float shade = 0.0;
	for (int i = 0; i < NSHIP; i++) {
		if (i >= u_ship_count) break;
		vec2 rel = p - u_ship_pos[i].xz;
		if (dot(rel, rel) > 150.0 * 150.0) continue;
		float spd = u_ship_speed[i];
		float way = clamp(spd / 3.0, 0.0, 1.0);
		if (way < 0.02) continue;
		float fast = clamp(spd / 6.0, 0.0, 1.0);
		/* Pas sous trois mètres — un parti pris de dessin : à la vitesse d'un
		   galion la loi en donne moins de deux, qui tombent sous le pixel et
		   moirent en hachures au lieu de se lire en vagues —, et effacées là où
		   l'écran ne les résout plus (pix : la taille d'un pixel sur l'eau). */
		float lam = max(3.0, 0.5 * 6.2831853 * spd * spd / 9.81);
		float kw = 6.2831853 / lam;
		float resolved = 1.0 - smoothstep(lam * 0.12, lam * 0.3, pix);
		if (resolved <= 0.0) continue;
		for (int side = 0; side < 2; side++) {
			int row = i * 2 + side;
			float best = 0.0;
			vec2 grad = vec2(0.0);
			float wave = 0.0;
			vec4 a0 = texelFetch(u_kelvin, ivec2(0, row), 0);
			float run = 0.0;                             // mètres le long du bras
			for (int j = 1; j < KELVIN_N; j++) {
				vec4 a1 = texelFetch(u_kelvin, ivec2(j, row), 0);
				if (a1.w < 0.5) break;
				vec2 ab = a1.xy - a0.xy;
				float len = max(length(ab), 1e-4);
				vec2 t = ab / len, n = vec2(-t.y, t.x);
				float tt = clamp(dot(p - a0.xy, t) / len, 0.0, 1.0);
				float dk = length(p - (a0.xy + ab * tt));
				float age = mix(a0.z, a1.z, tt);
				// une bande qui s'évase : quelques mètres à l'étrave, des dizaines au loin
				float w = 2.0 + 0.22 * age;
				float env = exp(-(dk * dk) / (w * w)) * exp(-age / (45.0 + 90.0 * fast))
				          * smoothstep(0.5, 5.0, age);
				if (env > best) {
					best = env;
					float across = dot(p - a0.xy, n);
					float along = run + tt * len;
					vec2 dir = n * 0.82 + t * 0.57;      // la crête tournée de 35° sur le bras
					float ph = kw * (across * 0.82 + along * 0.57);
					grad = dir * kw * cos(ph);
					wave = sin(ph);
				}
				run += len;
				a0 = a1;
			}
			g += grad * best * 0.16 * way * resolved * u_kelvin_gain;
			shade += wave * best * 0.22 * way * resolved * u_kelvin_gain;
		}
	}
	return vec3(g, shade);
}

/* EST-ON DANS L'OMBRE D'UNE COQUE ? On remonte vers le soleil : à la hauteur h
   au-dessus de l'eau, le rayon est passé de h / sin(élévation) en travers. Si,
   à l'une des hauteurs de la coque, il tombe DANS son contour, le soleil est
   caché. La coque est traitée en prisme droit sur sa flottaison — l'évasement
   des hauts et les châteaux ne se voient pas dans une ombre douce —, et la
   pénombre s'élargit avec la hauteur, comme celle d'un vrai soleil. */
float hull_shadow(vec3 P, vec2 pix) {
	/* Un décalage des hauteurs propre à chaque pixel (bruit à gradient
	   entrelacé) : les contours que chaque hauteur dessine ne tombent plus au
	   même endroit d'un pixel à l'autre, et ce qui reste du motif se fond en
	   grain au lieu de se répéter (signalé). */
	float jit = fract(52.9829189 * fract(dot(pix, vec2(0.06711056, 0.00583715))));
	if (u_sun.y < 0.0) return 0.0;
	/* au ras de l'horizon l'ombre s'allongerait sans fin : bornée à VINGT fois la
	   hauteur — un soleil à 3° en donne dix-neuf ; bornée à six, elle était coupée
	   court au couchant, justement quand elle est la plus belle (signalé) */
	vec2 step_h = u_sun.xz / max(u_sun.y, 1e-3);
	float slen = length(step_h);
	if (slen > 20.0) step_h *= 20.0 / slen;
	float s = 0.0;
	for (int i = 0; i < NSHIP; i++) {
		if (i >= u_ship_count) break;
		float top = u_ship_top[i];
		if (top <= 0.0 || u_ship_afloat[i] <= 0.0) continue;
		vec2 rel = P.xz - u_ship_pos[i].xz;
		// loin de la coque ET de son ombre : rien à chercher
		vec2 mid = rel + step_h * top * 0.5;
		float reach = u_ship_half[i].x + length(step_h) * top * 0.5 + 3.0;
		if (dot(mid, mid) > reach * reach) continue;
		vec2 f2 = normalize(u_ship_fwd[i]);
		vec2 r2 = vec2(f2.y, -f2.x);
		float row = (float(i) + 0.5) / float(NSHIP);
		float here = 0.0;
		/* Vingt-quatre hauteurs, et une pénombre d'une fois et demie l'écart
		   entre deux : moins, et au soleil bas l'ombre se dessinait en
		   festons — autant de contours de coque empilés. */
		float sl = length(step_h);
		float stride = top * sl / 24.0;
		for (int k = 0; k < 24; k++) {
			float h = top * (float(k) + jit) / 24.0;
			float t_along;
			float gap = hull_gap(rel + step_h * h, f2, r2, u_ship_half[i], u_hull_ends[i], row, t_along);
			// la coque n'est haute là que si SES hauts, à cette station, dépassent h
			float deck = top * texture(u_hull_prof, vec2(clamp(t_along * 0.5 + 0.5, 0.0, 1.0), row)).g;
			float soft = 0.5 + 0.06 * h * sl + 1.5 * stride;
			float inside = 1.0 - smoothstep(-0.4, soft, gap);
			float band = max(0.25, top / 12.0);
			here = max(here, inside * (1.0 - smoothstep(deck - band, deck + band, h)));
		}
		s = max(s, here * u_ship_afloat[i]);
	}
	// il faut un soleil pour une ombre : ni la nuit, ni sous un ciel d'orage
	// il faut un soleil : elle ne s'éteint que dans ses deux derniers degrés sur l'horizon
	return s * smoothstep(0.0, 0.035, u_sun.y) * (1.0 - 0.7 * u_storm);
}

void fragment() {
	vec3 eye = CAMERA_POSITION_WORLD;
	vec3 V = normalize(eye - v_world);
	float dist = length(eye - v_world);

	/* Aplatir la normale avec la distance. Sans cela, un détail de vague plus
	   petit qu'un pixel se met à bouillonner à l'horizon — le plus laid des
	   artefacts d'un shader d'océan naïf. */
	float far = smoothstep(90.0, 1400.0, dist);
	vec3 N = normalize(mix(normalize(v_nrm), vec3(0.0, 1.0, 0.0), far * 0.92));

	/* Greffer les rides fines sur la normale de vague. Elles sont bornées en
	   bande passante contre LA MÊME taille de maille que les vagues : les rides
	   sont à l'échelle du mètre, donc elles doivent partir dès qu'un
	   quadrilatère couvre plusieurs mètres, ou elles crénellent comme les vagues
	   le faisaient. */
	float r_fade = (1.0 - smoothstep(0.8, 4.0, v_spacing))
	             * (1.0 - smoothstep(25.0, 350.0, dist));
	vec3 rn = ride_normale(v_world.xz, r_fade);
	N = normalize(vec3(N.x + rn.x, N.y, N.z + rn.z));

	// le V de Kelvin, dans la pente de la surface : il prend la lumière comme elle
	vec3 kg = kelvin_slope(v_world.xz, length(fwidth(v_world.xz)));
	N = normalize(vec3(N.x - kg.x, N.y, N.z - kg.y));

	// --- le miroir ---
	vec3 R = reflect(-V, N);
	R.y = abs(R.y) + 0.003;             // ne jamais échantillonner sous l'horizon
	/* PAS DE NUAGE DANS LE MIROIR. Le reflet est un lobe micro-facettes, donc
	   tout ce qui a la structure fine d'un cirrus redescend en bavure grise qui
	   se lit comme de la saleté sur l'eau plutôt que comme du ciel. */
	vec3 sky = naval_sky(R, u_sun, u_zenith, u_horizon, 0.0);
	/* UNE EAU RUGUEUSE REFLÈTE UN CIEL FLOU. Le miroir restait net quel que soit
	   le vent, et c'est la moitié de l'aspect plastique : une mer battue ne
	   renvoie plus l'image du ciel, elle en renvoie la moyenne, tirée vers le
	   haut. La rugosité monte avec le vent (u_ripple le porte). */
	float wind_n = smoothstep(0.4, 2.6, u_ripple);
	float rough_w = u_rough_base + u_rough_wind * wind_n;
	vec3 sky_soft = naval_sky(normalize(R + vec3(0.0, 0.8, 0.0)), u_sun, u_zenith, u_horizon, 0.0);
	sky = mix(sky, sky_soft, clamp(rough_w * 2.5 * u_sky_blur, 0.0, 0.9));

	/* DEUX FRESNEL, ET LES CONFONDRE M'A COÛTÉ UNE VERSION.
	   Celui du MIROIR se prend sur N·V : c'est la part de la lumière que la
	   surface renvoie au lieu de la laisser entrer, et à 2 % d'incidence normale
	   presque tout le reflet vit aux angles rasants. Celui du SCINTILLEMENT se
	   prend sur V·H, plus bas — ce sont deux questions différentes sur la même
	   surface. */
	float cnv = max(dot(N, V), 0.0);
	float F = 0.02 + 0.98 * pow(1.0 - cnv, 5.0);

	/* --- LE CORPS DE L'EAU ---
	   Droit dessus on voit DEDANS ; de biais on effleure le dessus. Et
	   l'essentiel de la couleur d'une mer est de la lumière du ciel rediffusée
	   vers le haut, pas le pigment de l'eau seul — sans ce terme elle se lit
	   comme de l'encre. */
	vec3 body = mix(u_deep, u_shallow, pow(cnv, 1.6));
	body += u_horizon * 0.13;

	/* Un coup de vent ôte sa couleur à l'eau aussi sûrement qu'au ciel. La
	   couleur d'une mer étant surtout du ciel rediffusé, un couvercle là-haut
	   doit atteindre le corps de l'eau — sans quoi elle reste turquoise sous un
	   ciel d'ardoise, ce qui se lit comme deux images collées l'une à l'autre. */
	body *= 1.0 - 0.50 * u_storm;
	/* ET LA NUIT LE PREND AUSSI. La couleur de l'eau est de la lumière rediffusée
	   vers le haut, et elle avait été écrite comme un pigment constant : à minuit,
	   droit dessous — là où le reflet du ciel est au plus faible et où l'on ne voit
	   que le corps de l'eau —, la mer luisait turquoise comme éclairée par-dessous. */
	body *= u_water_light;

	/* La lueur sous la surface : une crête éclairée par-derrière laisse passer la
	   lumière et vire à ce vert caractéristique. La plus forte sur les sommets, et
	   seulement quand le soleil est de l'autre côté. */
	float back = pow(max(dot(-V, u_sun) * 0.5 + 0.5, 0.0), 3.0);
	float crest = smoothstep(0.15, 1.0, v_rel / max(u_amp_max, 0.05));
	// à travers une crête seulement s'il y a de la lumière derrière : aucune, soleil couché
	body += u_sss * crest * back * 0.55 * (1.0 - far) * u_water_light * smoothstep(-0.05, 0.08, u_sun.y);

	/* LE CREUX SOMBRE ET LA CRÊTE CLAIRE — ce qui fait le relief d'une grosse
	   mer, et que l'éclairage seul ne donnait pas : un creux est entouré d'eau
	   plus haute qui lui cache une part du ciel (moins de ciel rediffusé, moins
	   de ciel dans le miroir), une crête est MINCE et la lumière du ciel la
	   traverse. Non pas du soleil seulement — sous un ciel d'orage ou de lune,
	   c'est tout le ciel qui passe au travers, et la face de la vague s'éclaire
	   d'un vert sombre. Un parti pris de dessin, comme un film d'animation
	   l'aurait : la mer se lit par ses masses avant de se lire par ses reflets. */
	float hrel = v_rel / max(u_amp_max, 0.05);
	float open = smoothstep(-0.75, 0.55, hrel);
	body += u_sss * crest * 0.35 * u_water_light * (1.0 - far);
	/* Le monde au-dessus de l'eau, sur le ciel : cherché de près seulement, et
	   sur une normale adoucie — les rides pleines le hacheraient en éclats. */
	/* Et seulement au pied d'un navire : son reflet ne peut tomber qu'à moins de
	   la hauteur de sa mâture de lui — deux longueurs et demie, avec de la marge.
	   Ailleurs la recherche ne trouverait que du ciel, à 0,7 ms l'image. */
	float mirror_a = 0.0;               // la part du miroir que tient une coque
	bool near_ship = false;
	for (int i = 0; i < NSHIP; i++) {
		if (i >= u_ship_count) break;
		float reach = 5.0 * u_ship_half[i].x + 10.0;
		vec2 dd = v_world.xz - u_ship_pos[i].xz;
		if (dot(dd, dd) < reach * reach) { near_ship = true; break; }
	}
	if (near_ship && dist < 400.0) {
		vec3 Nr = normalize(vec3(N.x * 0.5, N.y, N.z * 0.5));
		vec3 Rw = reflect(-V, Nr);
		if (Rw.y > 0.0) {
			vec3 Rv = normalize((VIEW_MATRIX * vec4(Rw, 0.0)).xyz);
			vec4 hm = hull_mirror(VERTEX, Rv, PROJECTION_MATRIX, INV_PROJECTION_MATRIX);
			mirror_a = hm.a * (1.0 - smoothstep(250.0, 400.0, dist));
			sky = mix(sky, hm.rgb, mirror_a);
		}
	}
	vec3 c = mix(body, sky, F);
	/* L'OMBRE DES COQUES : l'eau qu'elle couvre ne reçoit plus le soleil, donc
	   n'en renvoie plus par-dessous — ce qui reste est le ciel. Le miroir, lui,
	   garde son reflet : une ombre sur l'eau se voit dans l'eau, pas sur elle,
	   et c'est pourquoi elle est plus franche vue d'en haut qu'à l'horizon. */
	float shade = hull_shadow(v_world, FRAGCOORD.xy) * (1.0 - far) * u_sunlit;
	float sun_lit = 1.0;
	c = mix(c, c * (1.0 - u_shadow), shade * (1.0 - 0.6 * F));
	c *= 1.0 + kg.z;                    // les crêtes du V : claires au ciel, sombres au dos
	c *= mix(0.62, 1.0, mix(open, 1.0, far));

	/* --- LA ROUTE DE SCINTILLEMENT ---
	   Un lobe GGX, et non un point de miroir unique. La rugosité monte avec le
	   clapot local ET avec la distance, ce qui est exactement ce qui étire le
	   soleil en la longue route miroitante qu'on voit sur toute vraie mer.

	   Élargir le lobe à mesure que la maille grossit : un reflet serré chevauchant
	   une normale que la géométrie ne résout plus est l'autre moitié du moiré — il
	   scintille par intermittence entre deux quadrilatères voisins. La rugosité
	   tient lieu du détail de vague qu'on vient d'ôter. */
	vec3 H = normalize(u_sun + V);
	// dans l'ombre d'une coque, le soleil ne s'y reflète pas
	sun_lit = 1.0 - shade;
	/* ET LÀ OÙ L'EAU REFLÈTE UNE COQUE, elle ne reflète pas le soleil qui est
	   derrière : la route de soleil passait AU TRAVERS du reflet du navire
	   (signalé, soleil dans le dos de la coque). Le reflet de la coque
	   remplaçait bien le ciel, mais le soleil s'ajoutait après, sans masque. */
	sun_lit *= 1.0 - mirror_a;
	float coarse = smoothstep(0.8, 6.0, v_spacing);
	/* LA ROUTE DE SOLEIL GARDE SA PROPRE RUGOSITÉ. Celle des curseurs matifie
	   l'eau et floute le ciel qu'elle renvoie ; appliquée au soleil, elle
	   étalait sa route jusqu'à l'effacer — relevé sur une capture de grosse mer
	   au soleil bas. Le vent élargit la route (Cox et Munk), il ne l'éteint pas :
	   un peu du vent seulement. */
	float rough = clamp(0.055 + 0.08 * wind_n + 0.30 * v_foam + 0.24 * far + 0.26 * coarse, 0.03, 0.6);
	float a = rough * rough;
	float nh = max(dot(N, H), 0.0);
	float dd = nh * nh * (a * a - 1.0) + 1.0;
	float D = (a * a) / (3.14159265 * dd * dd);
	/* LE FRESNEL D'UNE MICRO-FACETTE SE PREND EN TRAVERS DU DEMI-VECTEUR, pas de
	   la normale. Le prendre sur N·V — l'erreur d'avant — effondrait le terme à
	   ~2 % sous tous les angles depuis lesquels on regarde réellement la mer, et
	   tuait le scintillement entièrement. */
	float voh = max(dot(V, H), 0.0);
	float Fs = 0.02 + 0.98 * pow(1.0 - voh, 5.0);
	/* LE SOLEIL SE REFLÈTE DÈS QU'IL SE LÈVE. Le reflet était multiplié par N·L,
	   l'éclairement d'une face : au ras de l'horizon, N·L ne vaut presque rien
	   sur toute la mer, et la route de soleil n'apparaissait qu'une fois l'astre
	   monté — relevé sur une capture de lever. Un reflet de micro-facettes n'a
	   pas ce facteur (il se simplifie avec le dénominateur) ; il reste seulement
	   qu'une face au dos du soleil ne le renvoie pas, et qu'un soleil couché ne
	   se reflète plus. Le gain passe de 2,4 à 2,0 : sans ce facteur, qui valait
	   0,7 à 0,9 sous un soleil haut, le plein jour aurait éclairci.

	   Et le reflet prend la COULEUR du soleil — orangée à l'aube —, au lieu d'un
	   blanc fixe : c'est la même lumière. */
	float facing = smoothstep(-0.02, 0.06, dot(N, u_sun)) * smoothstep(-0.03, 0.02, u_sun.y);
	vec3 sun_tint = u_sun_col / max(max(u_sun_col.r, max(u_sun_col.g, u_sun_col.b)), 1e-3);
	c += sun_tint * D * Fs * facing * 2.0 * sun_lit;

	/* Le même lobe pour la lune, froid et faible : la nuit, c'est la seule route
	   qu'il y ait. */
	if (u_moon_lit > 0.001) {
		vec3 Hm = normalize(u_moon + V);
		float nhm = max(dot(N, Hm), 0.0);
		float ddm = nhm * nhm * (a * a - 1.0) + 1.0;
		float Dm = (a * a) / (3.14159265 * ddm * ddm);
		float Fm = 0.02 + 0.98 * pow(1.0 - max(dot(V, Hm), 0.0), 5.0);
		c += vec3(0.72, 0.80, 1.0) * Dm * Fm * max(dot(N, u_moon), 0.0) * 1.1 * u_moon_lit;
	}

	// un peu de teinte diffuse, pour que les creux ne soient pas morts
	c += u_shallow * max(dot(N, u_sun), 0.0) * 0.05 * (1.0 - far);

	/* LES FEUX SUR L'EAU — un AJOUT, la page n'en a pas. Chaque lanterne pose
	   sur la mer ce qu'une flamme y pose la nuit :
	   · son REFLET, par le même lobe que le soleil et la lune — les rides l'étirent
	     en une traînée qui tremble, et c'est ce qui la fait lire de loin ;
	   · un peu de lumière ENTRÉE dans l'eau et qui en ressort teintée par elle ;
	   · et l'écume, qui la prend comme un drap blanc (plus bas).
	   La décroissance est celle de la lampe du moteur — en carré de la distance,
	   coupée à sa portée —, pour que l'eau et le bordé s'éclairent ensemble. Les
	   deux gains (2,4 pour le reflet, celui du soleil ; 0,6 d'eau claire) sont
	   CHOISIS, pas mesurés, et se jugent à l'œil : ils sont dans reglages.ini. */
	vec3 lamp_foam = vec3(0.0);
	for (int i = 0; i < NLAMP; i++) {
		if (i >= u_lamp_count) break;
		vec3 Lv = u_lamp[i].xyz - v_world;
		float dl = length(Lv);
		vec3 L = Lv / max(dl, 1e-3);
		float nd = dl / max(u_lamp_range[i], 0.01);
		nd *= nd; nd *= nd;
		float win = max(1.0 - nd, 0.0);
		win *= win;
		float irr = u_lamp[i].w * win / max(dl * dl, 0.01);
		if (irr <= 1e-5) continue;
		vec3 lc = LAMP_COL * irr * max(dot(N, L), 0.0);
		vec3 Hl = normalize(L + V);
		float nhl = max(dot(N, Hl), 0.0);
		float ddl = nhl * nhl * (a * a - 1.0) + 1.0;
		float Dl = (a * a) / (3.14159265 * ddl * ddl);
		float Fl = 0.02 + 0.98 * pow(1.0 - max(dot(V, Hl), 0.0), 5.0);
		c += lc * Dl * Fl * u_lamp_reflection;
		c += lc * (u_shallow * u_lamp_water + vec3(0.02));
		lamp_foam += lc;
	}

	/* L'ÉCUME DE CRÊTE, et il lui faut TROIS choses que le premier jet n'avait
	   pas — sans quoi la mer sort blanche comme un champ de neige, ce qu'elle a
	   fait.

	   Elle est BRISÉE PAR DU BRUIT : de l'écume n'est pas une nappe, c'est un
	   semis qui prend sur la crête et pas ailleurs. Sans cela chaque crête reçoit
	   une bande blanche continue, ce qui se lit comme de la peinture.

	   Elle FOND AVEC LA DISTANCE, et c'est ce qui manquait le plus : à l'horizon
	   la maille couvre des dizaines de mètres, donc toute crête y est « raide » et
	   tout l'horizon blanchissait d'un bloc.

	   Et elle ne FABRIQUE pas de lumière, elle en RENVOIE — d'où deux termes.
	   Celui de CIEL est la lumière qui fait le travail, et il porte déjà l'heure,
	   la saison et le gros temps. Celui d'EAU est là parce que la mer garde un
	   pigment CONSTANT — u_deep et u_shallow ne suivent pas le soleil — de sorte
	   qu'en n'éclairant que l'écume on la rendait plus SOMBRE que l'eau sur
	   laquelle elle repose, ce qui est le même défaut retourné. */
	float fn = onde_fbm(v_world.xz * 0.55 + u_time * 0.05);
	/* La dentelle, bornée en bande passante comme les rides : quand une maille
	   de la grille couvre plusieurs mètres, les fils ne se résolvent plus et
	   crénelleraient — elle cède alors à sa moyenne. */
	float lace_far = smoothstep(1.5, 5.0, v_spacing) + far;
	/* L'ÉCUME TIENT À LA CRÊTE : la part battue est la raideur (v_foam), et plus
	   encore près du sommet — c'est là que l'eau déferle, l'écume en redescend
	   par le dos de la vague en s'effilochant. */
	/* LES MOUTONS, là où la crête se pince et casse : le jacobien (v_foam), et
	   non plus la hauteur — une règle à la main qui blanchissait toute crête
	   haute, ronde ou non. Pleins au cœur, en traits sur la frange. */
	// des têtes franches : pleines dès la moitié de la rampe
	float battue = clamp(v_foam * 1.6 * u_cap_gain, 0.0, 1.0);
	// la finesse que la maille et la distance permettent encore
	float fine = (1.0 - smoothstep(0.8, 2.5, v_spacing)) * (1.0 - smoothstep(35.0, 140.0, dist));
	float lc = mix(lace(v_world.xz, battue, fine), 0.4 * battue + 0.2, clamp(lace_far, 0.0, 1.0));
	float crest_foam = clamp(lc * smoothstep(0.03, 0.3, battue) * (0.75 + 0.35 * fn) * u_foam_gain, 0.0, 1.0);
	/* LES STRIES QUI SUIVENT LA PENTE. Sur la face d'une grosse vague, l'eau
	   ruisselle et l'écume se déchire en longues traînées qui descendent, dans le
	   sens de la plus grande pente — ce que montrent les images de tempête, et
	   qui donne à une face de vague son grain. La pente est lue sur la normale des
	   GRANDES vagues seules (sans les rides) : sa part horizontale pointe vers le
	   bas. Les traînées sont les passages par zéro d'un Perlin étiré quinze fois
	   le long de cette pente, comme les traits d'écume ; elles ne viennent que
	   sur les faces raides, avec du vent, et cèdent de loin. */
	vec3 Ng = normalize(v_nrm);
	float slope = length(Ng.xz) / max(Ng.y, 0.05);
	vec2 down = Ng.xz / max(length(Ng.xz), 1e-4);
	vec2 across = vec2(-down.y, down.x);
	// des traînées de quatre mètres en quatre mètres, épaisses d'une paume : plus
	// fines, elles tombaient sous le pixel dès vingt mètres
	vec2 sq = vec2(dot(v_world.xz, down) * 0.035, dot(v_world.xz, across) * 0.28)
	        + vec2(0.0, u_time * 0.05);
	float streak = 1.0 - smoothstep(0.06, 0.2, abs(perlin(sq) + 0.4 * perlin(sq * 2.1 + 5.0)));
	float steep_face = smoothstep(0.12, 0.45, slope) * smoothstep(-0.6, 0.2, hrel);
	float streaks = streak * steep_face * smoothstep(0.6, 1.8, u_ripple)
	              * (1.0 - clamp(lace_far, 0.0, 1.0)) * 0.6 * u_streak_gain;
	crest_foam = max(crest_foam, streaks);

	/* ET CE QUI RESTE ENTRE LES CRÊTES : un voile de fils très fins, répandu par
	   places sur l'eau libre, que laissent les déferlantes passées. Rien par
	   petit temps ; il vient avec le vent (u_ripple le porte), par grandes
	   plaques qui dérivent — pas partout à la fois. Fils seulement : la dentelle
	   à son plus maigre. */
	float breeze = smoothstep(0.9, 2.1, u_ripple);
	float plaque = smoothstep(0.45, 0.8, onde_noise(v_world.xz * 0.012 - u_wind * u_time * 0.004 + 21.0));
	// et seulement sur la moitié haute : ce que les têtes blanches laissent en redescendant
	float voile = breeze * plaque * smoothstep(0.05, 0.4, hrel) * (1.0 - clamp(lace_far, 0.0, 1.0));
	if (voile > 0.001) crest_foam = max(crest_foam, lace(v_world.xz * 1.3 + 40.0, 0.0, fine) * voile * 0.45);

	/* L'ÉCUME OÙ LA COQUE COUPE L'EAU : un collier vif au ras du bordé, puis la
	   moustache d'étrave qui ne se montre qu'une fois qu'elle a de l'erre.
	   hull_gap donne la distance à sa VRAIE flottaison, en mètres — négative
	   dedans, positive dehors. */
	float hull_foam = 0.0;
	float near_hull = 1e9;              // la plus proche flottaison, pour laisser les coques à leur collier
	for (int i = 0; i < NSHIP; i++) {
		if (i >= u_ship_count) break;
		vec2 rel = v_world.xz - u_ship_pos[i].xz;
		vec2 f2 = normalize(u_ship_fwd[i]);
		vec2 r2 = vec2(f2.y, -f2.x);
		float t_along;
		float gap_m = hull_gap(rel, f2, r2, u_ship_half[i], u_hull_ends[i],
		                       (float(i) + 0.5) / float(NSHIP), t_along);
		near_hull = min(near_hull, gap_m);
		float way = clamp(u_ship_speed[i] / 3.0, 0.0, 1.0);

		/* Seuls le collier net et la vague d'étrave restent instantanés — ils
		   appartiennent à la coque et doivent bouger avec elle. La traînée qui
		   s'attarde derrière vient du champ d'écume, qui la laisse dans l'eau.

		   Le collier s'AMINCIT vers l'intérieur, sous le bordé, au lieu de
		   s'arrêter sur une ligne. L'écume s'accumule contre une coque et fuit
		   dessous ; un bord intérieur franc se lit comme une décalcomanie posée
		   sur l'eau. */
		float collar = (1.0 - smoothstep(0.0, 0.85, gap_m))
		             * smoothstep(-1.3, -0.1, gap_m);

		/* La vague d'étrave : pas une bande sur sa moitié avant, mais une
		   moustache à l'étrave et deux ailes qui filent vers l'arrière. La crête
		   s'écarte comme la RACINE CARRÉE de la distance derrière l'étrave, ce
		   qui donne à une vague d'étrave son aile parabolique plutôt qu'un bord
		   droit, et il lui faut de l'erre — un navire stoppé n'en jette aucune.

		   La rampe « fast » sature au double de « way », si bien que la gerbe
		   continue de monter quand le collier a cessé de grandir : c'est ce qui
		   la fait paraître menée plutôt que simplement à flot. */
		float fast   = clamp(u_ship_speed[i] / 6.0, 0.0, 1.0);
		float along  = t_along * u_ship_half[i].x;
		float s_aft  = max(u_hull_ends[i].y - along, 0.0);   // mètres derrière l'étrave
		float spread = 1.7 * sqrt(s_aft) * fast;
		float wing   = exp(-pow((gap_m - spread) / (0.75 + 0.9 * fast), 2.0));
		float fade   = exp(-s_aft / (5.0 + 30.0 * fast));    // s'éteint vers l'arrière
		float bow    = wing * fade * way * step(-0.1, gap_m);

		/* Les navires n'ajoutent pas d'écume au-delà de la saturation, ils se
		   la partagent : deux coques bord à bord font une tache blanche, pas une
		   tache deux fois blanche. */

		hull_foam = max(hull_foam,
			clamp(collar * 0.70 + bow * (0.55 + 0.75 * fast), 0.0, 1.0) * u_ship_afloat[i]);
	}
	hull_foam *= (0.45 + 0.80 * fn);

	/* L'écume déposée plus tôt et qui se disperse encore. Lue en espace MONDE,
	   donc elle reste où elle est née pendant que le navire s'éloigne. */
	float persist = 0.0;
	float wake_lines = 0.0;             // les fils de poupe, nets : le canal vert du champ
	if (u_foam_on > 0.5) {
		vec2 fuv = (v_world.xz - u_foam_origin) / u_foam_size;
		if (fuv.x > 0.0 && fuv.x < 1.0 && fuv.y > 0.0 && fuv.y < 1.0) {
			float edge = min(min(fuv.x, 1.0 - fuv.x), min(fuv.y, 1.0 - fuv.y));
			vec4 ft = texture(u_foam_tex, fuv);
			persist = ft.r * smoothstep(0.0, 0.03, edge);   // pas de bord franc
			wake_lines = ft.g * smoothstep(0.0, 0.03, edge);
		}
	}
	/* Gardée AVANT la dentelle pour la face du dessous : les bulles qui pendent
	   sous une nappe se dispersent en descendant, et le nuage y est plus continu
	   que le lacis qu'on voit d'en haut. */
	float persist_deep = persist;
	// la vieille écume est déchirée, en traînées de dentelle, pas un lavis uniforme
	persist *= 0.25 + 0.95 * lace(v_world.xz, persist, fine) * (1.0 - clamp(lace_far, 0.0, 1.0)) + 0.4 * clamp(lace_far, 0.0, 1.0);

	// les fils de poupe restent des fils : pas de dentelle, à peine rompus
	wake_lines = smoothstep(0.15, 0.6, wake_lines) * (0.75 + 0.35 * fn);
	/* L'ÉCUME DU RIVAGE : là où l'eau n'a plus qu'un mètre au-dessus du sable.
	   La hauteur d'eau se lit dans l'image déjà rendue : le point de la scène
	   derrière ce pixel de mer — le fond, la plage, un pilier de ponton — est
	   reconstitué en monde, et l'on compare sa hauteur à celle de la surface.
	   C'est la terre qu'on VOIT qui décide, donc l'écume suit exactement le
	   rivage dessiné. Comme la surface monte et descend avec la houle et le
	   fond non, la bande va et vient d'elle-même sur la plage ; une onde lente
	   la pousse un peu plus. Les coques gardent leur collier : rien à moins de
	   deux mètres et demi d'une flottaison. */
	float shore_foam = 0.0;
	if (dist < 600.0 && near_hull > 2.5) {
		float scene_z = view_depth(SCREEN_UV, INV_PROJECTION_MATRIX);
		float frag_z = -VERTEX.z;
		// du fond ou de la terre juste derrière — pas le ciel, pas le large
		if (scene_z < frag_z + 40.0) {
			vec3 sw = (INV_VIEW_MATRIX * vec4(VERTEX * (scene_z / max(frag_z, 1e-3)), 1.0)).xyz;
			float over = v_world.y - sw.y;                 // l'eau au-dessus du sable, en mètres
			float swash = 0.35 + 0.35 * sin(u_time * 1.1 + dot(v_world.xz, vec2(0.05, 0.03)));
			float band = (1.0 - smoothstep(0.0, 0.9 + swash, over)) * smoothstep(-0.2, 0.05, over);
			shore_foam = band * (0.55 + 0.45 * lace(v_world.xz * 0.9 + 17.0, 0.0, fine)) * (1.0 - smoothstep(350.0, 600.0, dist));
		}
	}
	float foam = clamp(max(max(max(crest_foam, persist), wake_lines), shore_foam) + hull_foam, 0.0, 1.0) * (1.0 - far * 0.85);
	vec3 foam_col = body * 0.5 + u_horizon * 1.05 * (1.0 - 0.55 * u_storm)
	              + lamp_foam * 0.8;          // l'écume prend la flamme comme un drap blanc
	/* L'ÉCUME À L'OMBRE : un drap blanc qui ne prend plus le soleil. Elle perd
	   plus que l'eau — l'eau garde le miroir du ciel, l'écume n'a que sa lumière
	   diffuse, et le soleil en était la plus grande part. */
	foam_col *= 1.0 - clamp(u_shadow * 1.3, 0.0, 0.9) * shade;
	c = mix(c, foam_col, foam * 0.85);

	/* ET LA BRUME EN DERNIER, parce que c'est l'air devant tout le reste. La mer
	   et le navire la lisent dans la MÊME fonction, sinon le bâtiment reste net
	   contre une mer délavée.

	   Et elle se fond dans le CIEL QUI EST JUSTE DERRIÈRE ce morceau d'eau, pas
	   dans une couleur de brouillard plate : à la limite la mer et le ciel dans
	   cette direction ont la même couleur, il ne reste plus de bord à voir, et
	   l'horizon se dissout au lieu de finir sur une ligne. Le portage gardait un
	   gris d'horizon uniforme ; c'est la loi de la page, et celle de la coque. */
	float hz = haze_along(eye, v_world);
	c = mix(c, naval_sky(normalize(v_world - eye), u_sun, u_zenith, u_horizon), hz);

	// une décharge au-dessus éclaire l'eau aussi, et l'écume davantage
	c += vec3(0.42, 0.50, 0.68) * u_flash * (0.55 + 0.9 * foam);

	/* CE QUI EST SOUS LA SURFACE, VU À TRAVERS ELLE — la fin du shader de la page.

	   La page dessinait la coque une seconde fois, seule, dans un tampon avec sa
	   profondeur. Ici elle est déjà dans l'image : la mer est rendue APRÈS tout
	   l'opaque (matière transparente, priorité la plus basse, voir OceanNode) et
	   relit l'image et la profondeur de ce qui est derrière elle. La différence
	   entre cette profondeur et celle de la surface est l'épaisseur d'eau en
	   travers. Là où rien n'est derrière — le ciel — l'épaisseur est immense et
	   l'eau reste opaque ; là où la coque est DEVANT, l'épaisseur est négative et
	   rien n'est doublé.

	   L'extinction est PAR CANAL — le rouge parti en trois mètres, le bleu portant
	   trois fois plus loin —, ce qui la fait passer au vert, puis au bleu, puis à
	   rien, au lieu de griser comme ferait la brume. C'est ce qui adoucit la
	   ligne de flottaison : sous la surface le bordé ne s'arrête pas, il s'enfonce. */
	/* VUE DE DESSOUS : LA FENÊTRE DE SNELL.

	   D'en dessous, tout l'hémisphère du dessus est resserré dans un cône
	   d'environ 97° — tout, d'un horizon à l'autre, dans ce seul disque clair.
	   Au-delà, plus rien n'entre : l'angle a passé la réflexion totale et la
	   surface devient un miroir. Ce cercle, et son bord argenté bien net, sont
	   ce qui fait lire une image comme étant SOUS l'eau plutôt que seulement
	   bleue.

	   refract() fait le travail et rend le vecteur nul en réflexion totale, ce
	   qui est exactement le test pour être hors de la fenêtre ; la normale est
	   retournée, la surface faisant face de l'autre côté. AVANT que la coque vue
	   à travers l'eau ne soit mêlée : écrit après, il l'effacerait. */
	if (u_submerged > 0.5 && !FRONT_FACING) {
		vec3 Vv = normalize(v_world - eye);
		vec3 refr = refract(Vv, -N, 1.333);
		if (dot(refr, refr) < 0.0001) {
			c = mix(u_shallow * 0.42, u_deep * 1.15, 0.6);            // le miroir
		} else {
			c = naval_sky(refr, u_sun, u_zenith, u_horizon, 0.0);
			// le disque du soleil, que seul le dôme dessine et le dôme n'est pas là
			c += vec3(1.0, 0.95, 0.85) * pow(max(dot(refr, u_sun), 0.0), 1400.0) * 4.0;
		}
		// le bord de la fenêtre, où la lumière s'entasse avant d'être coupée
		c += vec3(0.80, 0.92, 1.00) * pow(1.0 - abs(dot(Vv, N)), 6.0) * 0.22;

		/* L'ÉCUME PAR EN DESSOUS, et c'est elle qui manquait : vue d'en bas, la
		   mer n'était qu'une vitre. Un sillage est un nuage de BULLES, et une
		   bulle renvoie la lumière des deux côtés — de dessous il est laiteux et
		   il BOUCHE la fenêtre de Snell. C'est la première chose qu'on remarque
		   d'un bateau qui passe au-dessus.

		   La MÊME écume que la face supérieure (foam, foam_col) : une définition,
		   deux usagers, donc le collier, les fils de poupe et la vieille dentelle
		   y sont tous sans qu'on ait à les recompter. Un peu plus marquée qu'en
		   haut, non par caprice : la rétrodiffusion des bulles ne dépend pas du
		   côté d'où on regarde, mais le fond contre lequel on la voit est ici
		   bien plus sombre. */
		/* PAS LA MÊME ÉCUME QU'EN HAUT, et c'est une question d'ÉPAISSEUR. Un
		   mouton de crête est une pellicule : vu de dessus il blanchit la mer, vu
		   de dessous il ne vaut presque rien. Le sillage, le collier de la coque
		   et la vieille écume sont des nuages de bulles qui PENDENT sous la
		   surface — eux seuls se voient d'en bas. Peint avec le foam d'en haut,
		   tout l'horizon devenait laiteux (vu au repère rouge). */
		float under = clamp(max(max(max(persist, persist_deep * 0.7), wake_lines) * 1.5, hull_foam * 0.95)
		                  + crest_foam * 0.18, 0.0, 1.0);
		/* Et PLUS CLAIRE qu'en haut, pas moins : de dessous on la voit à
		   contre-jour, éclairée par la lumière qu'elle diffuse en la traversant. */
		/* SA COULEUR N'EST PAS CELLE D'EN HAUT. La passe sous-marine mange le
		   rouge en premier : une écume peinte de sa couleur d'air ressort brune
		   et sombre — vu au repère rouge, qui arrivait bordeaux. Physiquement
		   c'est juste, et c'est ce qu'on voit en plongée : du blanc à quelques
		   mètres sous la surface est CYAN. On garde donc sa clarté (la luminance
		   de foam_col, une seule définition) et on la porte sur un blanc bleuté,
		   qui traverse l'eau au lieu d'y disparaître.

		   Et jamais plus sombre que ce qu'elle couvre : des bulles AJOUTENT de la
		   diffusion, elles n'ôtent pas de la lumière. */
		float lum = dot(foam_col, vec3(0.30, 0.59, 0.11));
		vec3 milk = lum * vec3(0.72, 0.95, 1.10) * 1.8;
		c = mix(c, max(c, milk), under * 0.95);
	}

	vec2 base = SCREEN_UV;
	float thick0 = max(view_depth(base, INV_PROJECTION_MATRIX) - (-VERTEX.z), 0.0);

	/* LA RÉFRACTION : la ride de surface portée sur ce qui est dessous. Snell
	   dévie le rayon d'environ (1 − 1/n) de l'inclinaison, un quart pour l'eau de
	   mer, et le décalage au bordé est cet angle fois la profondeur — construit en
	   MÈTRES MONDE puis reprojeté, pour que champ, format et assiette de la
	   caméra s'arrangent d'eux-mêmes. Saturé à cinq mètres : au-delà il
	   effacerait son contour, et elle doit rester lisible en s'effaçant. */
	float bend = min(thick0, 5.0) * 0.25 * u_refract;
	vec4 c0 = PROJECTION_MATRIX * VIEW_MATRIX * vec4(v_world, 1.0);
	vec4 c1 = PROJECTION_MATRIX * VIEW_MATRIX * vec4(v_world + vec3(N.x, 0.0, N.z) * bend, 1.0);
	// de coordonnées projetées à SCREEN_UV : ×0,5 (la projection de Godot porte déjà le sens de l'écran)
	vec2 shift = (c1.xy / c1.w - c0.xy / c0.w) * 0.5;
	vec2 suv = clamp(base + shift, vec2(0.0), vec2(1.0));
	float thick = view_depth(suv, INV_PROJECTION_MATRIX) - (-VERTEX.z);
	/* Si le rayon dévié tombe sur quelque chose DEVANT l'eau, il a atteint ses
	   hauts, et les tirer dans l'eau étalerait sa lisse sur la mer : on revient
	   au regard droit. */
	if (thick <= 0.0) { suv = base; thick = thick0; }
	if (thick > 0.0) {
		vec3 T = clamp(exp(-u_absorb * thick), 0.0, 1.0);
		vec3 behind = naval_encode(textureLod(u_screen, suv, 0.0).rgb);
		if (u_submerged > 0.5 && !FRONT_FACING) {
			// par en dessous, ce qu'on voit à travers la fenêtre est EN L'AIR : rien à partager
			c = mix(c, behind, T);
		} else {
			/* CE QUE L'EAU LAISSE PASSER NE REMPLACE QUE CE QU'ELLE DIFFUSE, PAS
			   CE QU'ELLE RENVOIE. La surface est une somme : F de reflet — le ciel,
			   la coque en miroir, le soleil, l'écume — et (1 − F) de ce qui sort de
			   l'eau, soit le corps de la mer, soit ce qui est derrière, dans la
			   proportion de l'extinction. Le mélange faisait passer le bordé
			   immergé PAR-DESSUS le reflet aussi : sous une faible épaisseur
			   d'eau, T ≈ 1 et la surface disparaissait tout à fait — on voyait la
			   coque comme dans un aquarium, sans ligne d'eau (signalé). Ici le
			   reflet reste en place, et la flottaison se découpe sur le bordé
			   comme sur une vraie coque : de biais, presque tout est reflet. */
			c += (1.0 - F) * T * u_veil * (behind - body);
		}
	}

	ALBEDO = naval_display(c);
	// transparente pour lire ce qui est derrière, mais pleinement opaque à l'œil
	ALPHA = 1.0;
}
