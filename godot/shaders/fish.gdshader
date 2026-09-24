/* UN BANC DE POISSONS — un seul modèle, instancié, et qui nage tout seul.
 *
 * RIEN N'EST CALCULÉ PAR LE PROCESSEUR. Le banc est un MultiMesh dont les
 * transformations d'instance ne bougent JAMAIS : chaque poisson porte quatre
 * nombres (INSTANCE_CUSTOM) — sa phase, son rayon de ronde, sa hauteur dans
 * l'eau, sa taille —, et c'est le shader qui en tire, à chaque image, où il est,
 * où il regarde et comment son corps est plié. Trente poissons coûtent un appel
 * de dessin et pas une ligne de C#. C'est la condition qu'on s'était donnée :
 * de la vie à bord et sous l'eau sans toucher aux images par seconde.
 *
 * ILS TOURNENT EN ROND, et ce n'est pas une facilité : un banc de récif ne va
 * nulle part, il tourne au-dessus de son patate de corail. Une ronde donne donc
 * le bon mouvement ET une courbure permanente, ce qui répond à la demande — que
 * la forme suive le déplacement. Un poisson qui vire n'est pas un poisson droit
 * qu'on a tourné : son corps épouse l'arc qu'il décrit. Ici l'arc est connu
 * exactement (c'est un cercle de rayon R), donc la flexion aussi : la corde d'un
 * cercle s'écarte de z²/2R, et c'est tout ce qu'il y a à écrire.
 *
 * ET ILS ONDULENT. Une onde remonte de la queue vers la tête, d'amplitude qui
 * DÉCROÎT vers l'avant — c'est la nage carangiforme, celle de presque tous les
 * poissons : la tête tient le cap, le corps ondule, la queue balaie. L'inverse
 * (une amplitude constante) donne un serpent, et se voit tout de suite.
 *
 * Le modèle est lu tel quel (creatures/fish.glb) : le shader ne suppose de lui
 * que son axe — nez vers +Z — et ses bornes, que le C# lui donne en uniformes.
 * Il n'a besoin ni de squelette, ni d'animation, ni de matière.
 */
shader_type spatial;
render_mode cull_disabled;

#include "res://shaders/gerstner.gdshaderinc"
#include "res://shaders/shelter.gdshaderinc"
#include "res://shaders/sky.gdshaderinc"
#include "res://shaders/ride.gdshaderinc"
// la MÊME lumière que le sable au-dessous : un banc qui passe sous une nervure
// la prend sur le dos, et c'est ce qui le pose DANS l'eau au lieu de dessus
#include "res://shaders/caustics.gdshaderinc"

/* LE SENS DU MODÈLE : +1 si son nez est vers +Z, −1 s'il regarde l'autre bord.
   Un demi-tour, et non une convention imposée au modeleur : on ne devine pas de
   quel côté regarde une maille, et se tromper fait nager tout un banc à reculons
   — ce qui se voit, mais seulement en jeu. Un chiffre dans settings.json vaut
   mieux qu'un aller-retour dans Blender. */
uniform float u_fish_face = 1.0;
// les bornes du modèle sur son axe, DANS CE SENS-LÀ
uniform float u_fish_tail = -0.11;   // le bout de la queue
uniform float u_fish_nose = 0.11;    // le bout du nez
uniform float u_fish_half = 0.06;    // sa demi-hauteur, pour la robe
// la vitesse de nage, en longueurs de corps par seconde (u_fish_beat : battements/s)
uniform float u_fish_speed = 1.1;
uniform float u_fish_beat = 2.6;
// l'amplitude du balancement de la queue, en part de la longueur
uniform float u_fish_sway = 0.11;
// la teinte du banc, et de combien elle varie d'un poisson à l'autre
uniform vec3 u_fish_tint = vec3(0.86, 0.62, 0.26);
uniform float u_fish_vary = 0.25;

varying float v_caustic;
varying float v_belly;    // 1 sur le dos, 0 sous le ventre
varying float v_along;    // 0 à la queue, 1 au nez
varying vec3 v_tint;

// un nombre au hasard, stable, tiré de la phase du poisson
float grain(float s, float k) { return fract(sin(s * 43758.5 + k * 127.1) * 12345.678); }

void vertex() {
	// le demi-tour d'abord : tout ce qui suit travaille sur un poisson qui
	// regarde vers +Z, quel que soit le sens dans lequel il a été modelé
	vec3 V0 = VERTEX, N0 = NORMAL;
	if (u_fish_face < 0.0) { V0.x = -V0.x; V0.z = -V0.z; N0.x = -N0.x; N0.z = -N0.z; }

	vec4 c = INSTANCE_CUSTOM;
	float phase = c.x * 6.2831853;
	float R = max(c.y, 0.35);        // le rayon de sa ronde
	float lift = c.z;                // sa hauteur dans le banc
	float size = max(c.w, 0.2);

	float L = (u_fish_nose - u_fish_tail) * size;
	float s = clamp((V0.z - u_fish_tail) / max(u_fish_nose - u_fish_tail, 1e-4), 0.0, 1.0);
	v_along = s;
	v_belly = clamp(0.5 + V0.y / max(u_fish_half, 1e-4) * 0.5, 0.0, 1.0);
	v_tint = u_fish_tint * (1.0 - u_fish_vary * 0.5 + u_fish_vary * grain(c.x, 3.0))
	       + vec3(grain(c.x, 7.0) - 0.5, grain(c.x, 11.0) - 0.5, grain(c.x, 13.0) - 0.5) * u_fish_vary * 0.35;

	/* SA RONDE. La vitesse est donnée en longueurs de corps par seconde — c'est
	   ainsi qu'on mesure un poisson, et cela fait qu'un gros va plus vite qu'un
	   petit sans qu'on ait à s'en occuper. Le sens tourne avec le banc, pas avec
	   l'individu : ils tournent tous du même bord, sinon ce n'est plus un banc. */
	float v = u_fish_speed * L;
	float a = phase + u_time * v / R;
	vec2 tang = vec2(cos(a), -sin(a));                 // le cap, dans le plan xz
	vec3 centre = vec3(R * sin(a), lift, R * cos(a));

	vec3 p = V0 * size;

	/* L'ONDULATION, de la queue vers la tête, amplitude décroissante vers
	   l'avant. (1 − s)² et non (1 − s) : le museau doit être à peu près immobile,
	   sinon le poisson nage de la tête. */
	float taper = (1.0 - s) * (1.0 - s);
	float wave = sin(s * 5.0 - u_time * u_fish_beat * 6.2831853 + phase * 3.0);
	float sway = u_fish_sway * L * taper * wave;
	p.x += sway;

	/* LA FLEXION DU VIRAGE : la corde d'un cercle de rayon R s'écarte de z²/2R.
	   Prise sur la distance au CENTRE du corps pour que la queue et le nez se
	   plient tous les deux vers l'intérieur, comme un poisson qui tourne. */
	float mid = (u_fish_nose + u_fish_tail) * 0.5 * size;
	float dz = p.z - mid;
	p.x -= dz * dz / (2.0 * R);

	/* LA NORMALE SUIT LE CORPS, sans quoi un poisson plié s'éclaire comme un
	   poisson droit et le pli ne se voit pas. La pente de la déformation en z
	   suffit : c'est elle qui tourne les flancs vers la lumière. */
	float dsway = u_fish_sway * L * (-2.0 * (1.0 - s) * 5.0 * wave
	            + taper * 5.0 * cos(s * 5.0 - u_time * u_fish_beat * 6.2831853 + phase * 3.0))
	            / max(u_fish_nose - u_fish_tail, 1e-4);
	float slope = dsway - dz / R;
	vec3 n = normalize(vec3(N0.x, N0.y, N0.z - N0.x * slope));

	// et tout cela tourné vers son cap, puis posé sur sa ronde
	mat2 M = mat2(vec2(tang.y, -tang.x), vec2(tang.x, tang.y));
	VERTEX = centre + vec3((M * p.xz).x, p.y, (M * p.xz).y);
	NORMAL = vec3((M * n.xz).x, n.y, (M * n.xz).y);

	/* LA LUMIÈRE DU FOND, LUE UNE FOIS PAR SOMMET et non par pixel. C'est la
	   dépense qu'il fallait surveiller : un banc proche emplit l'écran, et payer
	   dix-huit vagues et six lectures de ride par PIXEL de poisson coûtait une
	   milliseconde et demie à lui seul (mesuré). Un poisson fait vingt-six
	   centimètres — la nervure de lumière qui passe dessus est plus large que
	   lui, et l'interpoler sur ses sommets ne se voit pas. */
	vec3 w = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	float d = length(w - CAMERA_POSITION_WORLD);
	v_caustic = caustiques(w, d, max(0.05, d * 0.0015)).g;
}

void fragment() {
	/* LA ROBE. Le modèle n'apporte ni matière ni couleur de sommet : elle est
	   faite ici, et c'est aussi bien — un banc dont tous les poissons ont
	   exactement la même livrée se voit comme un décalque.

	   Le dos sombre et le ventre clair ne sont pas une décoration : c'est le
	   contre-ombrage, que presque tout ce qui nage porte — sombre vu d'en haut
	   sur le fond, clair vu d'en bas sur le ciel. */
	vec3 dos = v_tint * 0.35;
	vec3 ventre = mix(v_tint, vec3(0.92, 0.94, 0.90), 0.72);
	vec3 robe = mix(ventre, dos, smoothstep(0.35, 0.95, v_belly));
	// une bande sombre en travers, comme en portent tant de poissons de récif
	robe *= 1.0 - 0.35 * exp(-pow((v_along - 0.72) / 0.07, 2.0));

	ALBEDO = robe * v_caustic;
	ROUGHNESS = 0.34;
	METALLIC = 0.12;
	SPECULAR = 0.6;
}
