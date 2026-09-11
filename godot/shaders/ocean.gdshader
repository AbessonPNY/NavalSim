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
render_mode world_vertex_coords, cull_disabled, unshaded;

#include "res://shaders/gerstner.gdshaderinc"
#include "res://shaders/sky.gdshaderinc"
#include "res://shaders/haze.gdshaderinc"

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
// hauteur de reference de la lueur de crete, a l echelle de l etat de mer :
// le noyau la calcule deja (Ocean.AmpMax), on ne la redecide pas ici
uniform float u_amp_max = 1.0;
uniform vec2  u_wind = vec2(1.0, 0.0);

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
   plastique moulé. Trois octaves de bruit, dérivant avec le vent à des rythmes
   différents, dérivées pour donner une pente. */
vec3 ride_normale(vec2 p, float fade) {
	if (fade <= 0.001) return vec3(0.0, 1.0, 0.0);
	vec2 d1 = u_wind * u_time * 0.65;
	vec2 d2 = u_wind * u_time * 0.31;
	float e = 0.35;
	vec2 q = p * 0.22;
	float h  = onde_fbm(q + d1 * 0.22) + onde_fbm(q * 2.7 - d2 * 0.5) * 0.5;
	float hx = onde_fbm(q + vec2(e, 0.0) * 0.22 + d1 * 0.22)
	         + onde_fbm((q + vec2(e, 0.0)) * 2.7 - d2 * 0.5) * 0.5;
	float hz = onde_fbm(q + vec2(0.0, e) * 0.22 + d1 * 0.22)
	         + onde_fbm((q + vec2(0.0, e)) * 2.7 - d2 * 0.5) * 0.5;
	float s = 1.35 * u_ripple * fade;
	return normalize(vec3(-(hx - h) * s, 1.0, -(hz - h) * s));
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
	             * (1.0 - smoothstep(25.0, 260.0, dist));
	vec3 rn = ride_normale(v_world.xz, r_fade);
	N = normalize(vec3(N.x + rn.x, N.y, N.z + rn.z));

	// --- le miroir ---
	vec3 R = reflect(-V, N);
	R.y = abs(R.y) + 0.003;             // ne jamais échantillonner sous l'horizon
	/* PAS DE NUAGE DANS LE MIROIR. Le reflet est un lobe micro-facettes, donc
	   tout ce qui a la structure fine d'un cirrus redescend en bavure grise qui
	   se lit comme de la saleté sur l'eau plutôt que comme du ciel. */
	vec3 sky = naval_sky(R, u_sun, u_zenith, u_horizon, 0.0);

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

	/* La lueur sous la surface : une crête éclairée par-derrière laisse passer la
	   lumière et vire à ce vert caractéristique. La plus forte sur les sommets, et
	   seulement quand le soleil est de l'autre côté. */
	float back = pow(max(dot(-V, u_sun) * 0.5 + 0.5, 0.0), 3.0);
	float crest = smoothstep(0.15, 1.0, v_rel / max(u_amp_max, 0.05));
	body += u_sss * crest * back * 0.55 * (1.0 - far);

	vec3 c = mix(body, sky, F);

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
	float rough = clamp(0.055 + 0.30 * v_foam + 0.24 * far + 0.26 * coarse, 0.03, 0.6);
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

	// un peu de teinte diffuse, pour que les creux ne soient pas morts
	c += u_shallow * max(dot(N, u_sun), 0.0) * 0.05 * (1.0 - far);

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
	float foam = clamp(v_foam * (0.35 + 1.1 * fn), 0.0, 1.0) * (1.0 - far * 0.85);
	vec3 foam_col = body * 0.5 + u_horizon * 1.05 * (1.0 - 0.55 * u_storm);
	c = mix(c, foam_col, foam * 0.85);

	/* ET LA BRUME EN DERNIER, parce que c'est l'air devant tout le reste. La mer
	   et le navire la lisent dans la MÊME fonction, sinon le bâtiment reste net
	   contre une mer délavée. */
	float hz = haze_along(eye, v_world);
	c = mix(c, u_horizon * (1.0 - 0.55 * u_storm), hz);

	ALBEDO = c;
}
