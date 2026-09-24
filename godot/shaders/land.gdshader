/* LA TERRE ET LE FOND DE LA MER — et, sur le fond, LES CAUSTIQUES : la lumière
 * du soleil rassemblée par la houle dans les hauts-fonds.
 *
 * POURQUOI LE RELIEF A SON PROPRE SHADER. Il s'est contenté longtemps d'une
 * StandardMaterial3D — couleur de sommet, mat, rien d'autre —, et les caustiques
 * ont d'abord été écrites en SECONDE PASSE par-dessus, en mélange multiplicatif :
 * une caustique n'ajoute pas de lumière, elle la DÉPLACE, ce qui brille sur une
 * nervure manque entre deux, et seul un produit dit cela. Mesuré : sous Forward+,
 * une passe `blend_mul` ne dessine AUCUN fragment — le rendu la laisse tomber en
 * silence. La sonde : la même passe rendue soixante fois plus chère ne coûtait
 * pas un centième de milliseconde de plus, là où en `blend_add` elle en coûtait
 * six. Plutôt que de contourner, on prend l'endroit juste : ici, dans la matière
 * du fond lui-même, où multiplier l'ALBEDO revient exactement à multiplier la
 * lumière qui en repart — et le moteur l'éclaire, l'ombre et la brume par-dessus,
 * comme avant.
 *
 * OÙ ELLES SONT, et pourquoi là. Sur le FOND, et non dans le shader de la mer où
 * l'on aurait pu les glisser pour rien : la mer ne couvre que les pixels qu'on
 * voit À TRAVERS elle. Sous l'eau, le fond est devant l'œil sans surface entre
 * les deux, et c'est précisément de là qu'on vient les regarder. La lumière tombe
 * sur le sable ; elle n'appartient pas à la fenêtre par laquelle on la regarde.
 * Une fois posée ici, le shader de la mer la retrouve tout seul dans l'image
 * qu'il relit pour le dessous de la surface — rien à écrire deux fois.
 *
 * COMMENT, en un mot : ce sont les MÊMES VAGUES. La courbure de la surface, et
 * rien d'autre, décide où les rayons se rassemblent ; le calcul est dans
 * gerstner.gdshaderinc, à côté de la houle dont il n'est qu'une lecture de plus,
 * et les rides viennent de ride.gdshaderinc, que la mer lit aussi pour sa
 * normale. Une mer qui change change le fond dans la même image, et un plat calme
 * n'écrit rien du tout : à courbure nulle le déterminant vaut un, et le facteur
 * exactement un.
 */
shader_type spatial;
render_mode world_vertex_coords;

#include "res://shaders/gerstner.gdshaderinc"
#include "res://shaders/shelter.gdshaderinc"
// naval_hash13, dont les rides ont besoin — et dans cet ordre
#include "res://shaders/sky.gdshaderinc"
#include "res://shaders/ride.gdshaderinc"

// la part du soleil qui passe encore les nuages (SkyNode.Sunlit) — sa direction,
// u_sun, vient du ciel avec le reste (sky.gdshaderinc)
uniform float u_sunlit = 1.0;

/* LES CINQ CADRANS (settings.json → caustics). */
// la force du contraste, 1 = ce que la réfraction donne ; 0 = pas de caustiques
uniform float u_caustic_gain = 1.0;
// la profondeur où elles s'éteignent, en mètres
uniform float u_caustic_depth = 14.0;
// la distance à l'œil où elles s'éteignent, en mètres
uniform float u_caustic_far = 700.0;
// le plancher du déterminant : au foyer exact, la nervure serait infinie
uniform float u_caustic_floor = 0.12;
// l'écart des couleurs au foyer, 1 = celui de l'eau de mer
uniform float u_caustic_spread = 1.0;

/* LES TROIS INDICES DE L'EAU DE MER, rouge, vert, bleu. Ils ne diffèrent que d'un
   demi pour cent, mais le foyer d'une nervure est un point de rencontre : un demi
   pour cent d'écart sur la déviation y devient un liseré coloré, et c'est à cela
   qu'on reconnaît une vraie caustique d'un motif peint. */
const vec3 IOR = vec3(1.331, 1.335, 1.340);

/* LA COURBURE DES RIDES, par différences finies sur le MÊME champ que la surface
   (ride.gdshaderinc). Six lectures : les deux courbures droites en différence
   centrée, et la croisée par le coin — sans elle, les nœuds de lumière se
   rangeraient en quadrillage au lieu de faire des mailles obliques.

   L'amplitude est celle que la mer donne à sa normale, un demi de u_ripple fois
   la part réglée : la ride qui éclaire le fond est exactement celle qu'on voit
   luire à la surface. */
vec3 ride_courbure(vec2 p, float e) {
	float h  = ride_h(p);
	float hx = ride_h(p + vec2(e, 0.0));
	float hX = ride_h(p - vec2(e, 0.0));
	float hz = ride_h(p + vec2(0.0, e));
	float hZ = ride_h(p - vec2(0.0, e));
	float hd = ride_h(p + vec2(e, e));
	float a = 0.5 * u_ripple * u_ride_gain / (e * e);
	return vec3((hx - 2.0 * h + hX) * a, (hz - 2.0 * h + hZ) * a, (hd - hx - hz + h) * a);
}

/* LE FACTEUR À POSER SUR LA COULEUR DU FOND. Un partout ailleurs. */
vec3 caustiques(vec3 world, float dist) {
	/* LE TRI GROSSIER D'ABORD, sur le niveau MOYEN de la mer, avant toute somme de
	   vagues : la montagne, la plage et le grand fond sortent en quatre
	   comparaisons. La houle ne dépasse pas quelques mètres, d'où la marge. */
	float flat_depth = -world.y;
	if (u_caustic_gain <= 0.001 || flat_depth < -5.0 || flat_depth > u_caustic_depth + 5.0
	    || dist > u_caustic_far || u_sun.y <= 0.0 || u_sunlit < 0.01) return vec3(1.0);

	// la largeur d'un pixel SUR LE FOND, en mètres : au-delà, un moiré
	float metre = length(fwidth(world.xz));

	/* LE CHEMIN SOUS L'EAU, et non la profondeur. Snell couche le rayon d'autant
	   que le soleil est bas : sin θ = n·sin θ', donc sa descente est plus longue
	   que la verticale, et les nervures s'étirent. Pris sur le niveau MOYEN pour
	   pouvoir servir de garde-fou à la somme elle-même — la houle ne change ce
	   chemin que de quelques pour cent. */
	float sin_i = sqrt(max(1.0 - u_sun.y * u_sun.y, 0.0));
	float cos_t = sqrt(max(1.0 - sin_i * sin_i / (1.335 * 1.335), 0.04));
	float hk0 = max(flat_depth, 0.0) / cos_t * (1.0 - 1.0 / 1.335);

	float height;
	vec3 curv;
	gerstner_bottom(world.xz, u_time, shelter_at(world.xz), metre, hk0, height, curv);

	/* ET LES RIDES PAR-DESSUS — ce sont ELLES qui font les caustiques d'une plage.
	   Mesuré sur notre spectre par force 5 : la houle seule ne module le fond que
	   de 4 % à un mètre (0,87 à 1,14), parce qu'il lui faudrait vingt-cinq mètres
	   d'eau pour mettre au point une vague de cinq mètres de long. Ce qui dessine
	   le réseau serré du sable, c'est le clapot d'un mètre, dont le foyer tombe
	   justement vers deux mètres de fond. Sans lui, il n'y aurait de lumière que
	   par le travers des tombants. */
	float rfade = (1.0 - smoothstep(0.35, 1.1, metre))               // sous le pixel
	            * (1.0 - smoothstep(0.8, 2.2, hk0 * 2.42 * u_ripple * u_ride_gain));
	if (rfade > 0.01) curv += ride_courbure(world.xz, max(0.2, metre)) * rfade;

	float depth = height - world.y;
	if (depth <= 0.0) return vec3(1.0);
	float path = depth / cos_t;

	/* LE DÉTERMINANT DE L'APPLICATION S ↦ S + h·κ·∇η, par couleur. κ = 1 − 1/n est
	   la fraction de la pente dont le rayon est dévié — le MÊME quart que la
	   réfraction du regard à la fin du shader de la mer. */
	vec3 kappa = mix(vec3(1.0 - 1.0 / 1.335), 1.0 - 1.0 / IOR, u_caustic_spread);
	vec3 hk = path * kappa;
	vec3 a = 1.0 + hk * curv.x;
	vec3 b = 1.0 + hk * curv.y;
	vec3 x = hk * curv.z;
	vec3 det = a * b - x * x;

	// la clarté est l'inverse de ce que l'application dilate les aires
	vec3 bright = 1.0 / max(abs(det), u_caustic_floor);

	/* CE QUI LES ÉTEINT EN PROFONDEUR n'est pas l'absorption — elle éteint autant
	   la nervure que le sable entre deux, et un RAPPORT ne s'absorbe pas. C'est
	   que le soleil a un demi-degré de large : à dix mètres, son foyer est déjà
	   étalé sur neuf centimètres, et les fines se brouillent les unes dans les
	   autres. Plus la diffusion de l'eau, qui pose son voile par-dessus. */
	float fade = (1.0 - smoothstep(u_caustic_depth * 0.55, u_caustic_depth, depth))
	           * (1.0 - smoothstep(u_caustic_far * 0.6, u_caustic_far, dist))
	           * smoothstep(0.02, 0.22, u_sun.y)   // rien au ras de l'horizon
	           * u_sunlit;                          // ni sous un ciel fermé

	return max(1.0 + clamp(bright - 1.0, vec3(-0.75), vec3(3.0)) * (u_caustic_gain * fade), vec3(0.0));
}

varying vec3 v_world;

void vertex() {
	v_world = VERTEX;
}

void fragment() {
	/* La couleur de la côte est dans les SOMMETS — du sable au bord de l'eau qui
	   monte en herbe, en forêt et en roche (LandNode.Tint), déjà linéaire.

	   Le facteur porte sur l'albédo, donc sur tout ce qui repart du fond, la part
	   du ciel comprise ; en toute rigueur une caustique ne module que le soleil
	   direct. L'écart ne se voit que dans l'ombre d'une coque, où il n'y a de
	   toute façon plus de caustique à voir. */
	ALBEDO = COLOR.rgb * caustiques(v_world, length(v_world - CAMERA_POSITION_WORLD));
	ROUGHNESS = 0.94;
	METALLIC = 0.0;
}
