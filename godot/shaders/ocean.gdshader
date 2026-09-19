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
#include "res://shaders/sky.gdshaderinc"
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

// force des rides, montée avec le vent : 0,40 par calme, 2,6 en tempête
uniform float u_ripple = 0.9;
/* LES RÉGLAGES DE MISE AU POINT (menu, section Mer) : la rugosité de l'eau, qui
   monte avec le vent, le flou du ciel dans le miroir qui la suit, et la part des
   rides, des moutons et de l'écume en traits. */
uniform float u_rough_base = 0.055;
uniform float u_rough_wind = 0.15;
uniform float u_sky_blur = 0.6;
uniform float u_ride_gain = 1.0;
uniform float u_cap_gain = 1.0;
uniform float u_foam_gain = 1.0;
// hauteur de reference de la lueur de crete, a l echelle de l etat de mer :
// le noyau la calcule deja (Ocean.AmpMax), on ne la redecide pas ici
uniform float u_amp_max = 1.0;
uniform vec2  u_wind = vec2(1.0, 0.0);

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
	float shelter = 1.0;         // le havre n'est pas encore porté

	vec3 disp;
	vec3 nrm_acc;
	float steep;
	float height;
	gerstner_sum(w0.xz, u_time, shelter, spacing, disp, nrm_acc, steep, height);

	VERTEX = w0 + disp;
	v_nrm = gerstner_normal(nrm_acc);
	NORMAL = v_nrm;

	v_world = VERTEX;
	v_foam = smoothstep(0.55, 0.95, steep);
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

/* LES RIDES. Le maillage ne porte que dix-huit longues vagues ; tout ce qui est
   plus fin que quelques mètres doit vivre dans la NORMALE, ou la mer a l'air de
   plastique moulé.

   DES ARÊTES, PAS DES BOSSES. Un clapot de vent n'est pas une tôle bosselée :
   ce sont de petites crêtes FINES et des creux larges, couchées EN TRAVERS du
   vent, longues d'un bord à l'autre et courtes dans le sens où elles courent.
   Le premier jet sommait un bruit lisse, qui donnait des bosses molles et une
   lumière de plastique. Ici chaque octave est un bruit PLISSÉ (1 − |2n − 1|,
   au carré : une ligne vive là où le bruit passe par le milieu), étiré trois
   fois en travers du vent, et tourné de quelques degrés de plus à chaque octave
   pour que les trains se croisent comme sur une vraie mer. Chacun avance avec
   le vent à sa vitesse — les petites plus lentement, comme l'eau profonde le
   veut (c ∝ √λ). Ce sont ces arêtes qui accrochent le soleil, la lune et les
   feux en paillettes. */
float ride_h(vec2 p) {
	vec2 w = u_wind;
	vec2 x = vec2(dot(p, w), dot(p, vec2(-w.y, w.x)));   // le long du vent, en travers
	float h = 0.0, amp = 0.5, f = 0.42, c = 1.25;
	for (int i = 0; i < 3; i++) {
		float an = (float(i) - 1.0) * 0.38;
		vec2 r = vec2(cos(an) * x.x - sin(an) * x.y, sin(an) * x.x + cos(an) * x.y);
		// en travers du vent, trois fois plus long ; poussé le long du vent à sa vitesse
		vec2 q = vec2((r.x - u_time * c) * f, r.y * f * 0.33) + vec2(float(i) * 17.3, float(i) * 5.1);
		float n = onde_noise(q);
		float k = 1.0 - abs(2.0 * n - 1.0);
		h += k * k * amp;
		amp *= 0.5; f *= 2.13; c *= 0.69;
	}
	return h;
}

vec3 ride_normale(vec2 p, float fade) {
	if (fade <= 0.001) return vec3(0.0, 1.0, 0.0);
	float e = 0.2;
	float h  = ride_h(p);
	float hx = ride_h(p + vec2(e, 0.0));
	float hz = ride_h(p + vec2(0.0, e));
	float s = 0.9 * u_ripple * u_ride_gain * fade / e;
	return normalize(vec3(-(hx - h) * s, 1.0, -(hz - h) * s));
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
	vec3 c = mix(body, sky, F);
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
	float coarse = smoothstep(0.8, 6.0, v_spacing);
	float rough = clamp(rough_w + 0.30 * v_foam + 0.24 * far + 0.26 * coarse, 0.03, 0.7);
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
	float shadow = max(dot(N, u_sun), 0.0);        // pas d'éclat sur une face au dos
	c += vec3(1.0, 0.96, 0.86) * D * Fs * shadow * 2.4;

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
	/* LES MOUTONS. Le HAUT de la vague blanchit, d'abord par la hauteur : passé
	   un certain vent, toute crête qui monte assez casse son sommet, raide ou
	   non — c'est ce qu'on voit d'une grosse mer, des têtes blanches sur des
	   pentes sombres. Pleine au sommet, en dentelle sur la frange, rien sur la
	   face. Une première version ne tenait l'écume qu'à la raideur, et la
	   dentelle courait sur toute la face de la vague en fils sans cime. */
	float sea = smoothstep(0.5, 1.6, u_ripple);
	float cap = clamp(smoothstep(0.22, 0.62, hrel) * sea * u_cap_gain, 0.0, 1.0);
	float battue = clamp(max(v_foam * mix(0.4, 1.2, smoothstep(0.0, 0.55, hrel)), cap), 0.0, 1.0);
	// la finesse que la maille et la distance permettent encore
	float fine = (1.0 - smoothstep(0.8, 2.5, v_spacing)) * (1.0 - smoothstep(35.0, 140.0, dist));
	float lc = mix(lace(v_world.xz, battue, fine), 0.4 * battue + 0.2, clamp(lace_far, 0.0, 1.0));
	float crest_foam = clamp(lc * smoothstep(0.03, 0.3, battue) * (0.75 + 0.35 * fn) * u_foam_gain, 0.0, 1.0);
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
	for (int i = 0; i < NSHIP; i++) {
		if (i >= u_ship_count) break;
		vec2 rel = v_world.xz - u_ship_pos[i].xz;
		vec2 f2 = normalize(u_ship_fwd[i]);
		vec2 r2 = vec2(f2.y, -f2.x);
		float t_along;
		float gap_m = hull_gap(rel, f2, r2, u_ship_half[i], u_hull_ends[i],
		                       (float(i) + 0.5) / float(NSHIP), t_along);
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
	if (u_foam_on > 0.5) {
		vec2 fuv = (v_world.xz - u_foam_origin) / u_foam_size;
		if (fuv.x > 0.0 && fuv.x < 1.0 && fuv.y > 0.0 && fuv.y < 1.0) {
			float edge = min(min(fuv.x, 1.0 - fuv.x), min(fuv.y, 1.0 - fuv.y));
			persist = texture(u_foam_tex, fuv).r
			        * smoothstep(0.0, 0.03, edge);   // pas de bord franc
		}
	}
	// la vieille écume est déchirée, en traînées de dentelle, pas un lavis uniforme
	persist *= 0.25 + 0.95 * lace(v_world.xz, persist, fine) * (1.0 - clamp(lace_far, 0.0, 1.0)) + 0.4 * clamp(lace_far, 0.0, 1.0);

	float foam = clamp(max(crest_foam, persist) + hull_foam, 0.0, 1.0) * (1.0 - far * 0.85);
	vec3 foam_col = body * 0.5 + u_horizon * 1.05 * (1.0 - 0.55 * u_storm)
	              + lamp_foam * 0.8;          // l'écume prend la flamme comme un drap blanc
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
		vec3 T = exp(-u_absorb * thick);
		c = mix(c, naval_encode(textureLod(u_screen, suv, 0.0).rgb), clamp(T, 0.0, 1.0));
	}

	ALBEDO = naval_display(c);
	// transparente pour lire ce qui est derrière, mais pleinement opaque à l'œil
	ALPHA = 1.0;
}
