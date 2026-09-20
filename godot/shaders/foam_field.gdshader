/* L'ÉCUME QUI SE SOUVIENT — le champ persistant de foam.js.
 *
 * L'écume de crête du shader de mer n'existe que là où une crête est raide À
 * CET INSTANT : elle clignote et ne reste pas. La vraie écume est LAISSÉE : une
 * crête déferle, le mousseux reste sur l'eau et se disperse en quelques dizaines
 * de secondes. C'est ce champ-là qui couvre une mer de tempête de traînées, et
 * c'est lui qui manquait au portage.
 *
 * Une passe par image : le champ de l'image d'avant est reporté (décalé de ce
 * dont l'ancre a bougé, pour que l'écume reste où elle est dans le MONDE), étalé
 * sur quatre voisins, atténué, puis on dépose l'écume fraîche là où une crête
 * déferle, et là où une coque déchire la surface. Les bouillonnements d'épave
 * viendront avec les naufrages.
 */
shader_type canvas_item;
render_mode blend_disabled, unshaded;

// les vagues : MÊMES uniformes que la mer, poussés par OceanNode.PushWaves
#include "res://shaders/gerstner.gdshaderinc"
#include "res://shaders/shelter.gdshaderinc"
// la coque : MÊME contour que celui que la mer lit pour son collier
#include "res://shaders/hull_gap.gdshaderinc"

// le nombre de vagues que lit l'écume, Config.NWavesFoam : les plus énergiques,
// puisque le tri du spectre les met en tête
#define NW_FOAM 7

uniform sampler2D u_prev : filter_linear, repeat_disable;
uniform vec2 u_offset_uv;
uniform vec2 u_origin;
uniform float u_decay = 1.0;
uniform float u_size = 620.0;
uniform float u_texel = 0.0009765625;
uniform float u_jac_foam = 0.85;     // le même seuil que la mer

/* LES BOUILLONS D'UNE ÉPAVE : (x, z, rayon, force). Seize par image, un budget
   et non une borne de la mer — le champ garde ce qu'on y a posé, donc un
   bouillon laissé de côté une image n'est pas perdu. */
#define NBOIL 16
uniform vec4 u_boil[NBOIL];
uniform int u_boil_count = 0;

float boil_hash(vec2 p) { return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453); }
float boil_noise(vec2 p) {
	vec2 i = floor(p), f = fract(p);
	f = f * f * (3.0 - 2.0 * f);
	return mix(mix(boil_hash(i), boil_hash(i + vec2(1.0, 0.0)), f.x),
	           mix(boil_hash(i + vec2(0.0, 1.0)), boil_hash(i + vec2(1.0, 1.0)), f.x), f.y);
}

void fragment() {
	vec2 wpos = u_origin + UV * u_size;      // XZ monde de ce texel

	/* Reporter le champ. La lecture est décalée de ce dont l'ancre a bougé ;
	   l'étalement sur quatre voisins le laisse se disperser en vieillissant. */
	vec2 puv = UV + u_offset_uv;
	float prev = 0.0;
	if (puv.x > 0.0 && puv.x < 1.0 && puv.y > 0.0 && puv.y < 1.0) {
		prev  = texture(u_prev, puv).r * 0.60;
		prev += texture(u_prev, puv + vec2(u_texel, 0.0)).r * 0.10;
		prev += texture(u_prev, puv - vec2(u_texel, 0.0)).r * 0.10;
		prev += texture(u_prev, puv + vec2(0.0, u_texel)).r * 0.10;
		prev += texture(u_prev, puv - vec2(0.0, u_texel)).r * 0.10;
	}
	prev *= u_decay;

	/* LES LIGNES DE POUPE, dans le canal vert : NI étalées NI mélangées à la nappe
	   — sans quoi, étalées d'un texel par image, elles devenaient une bande en
	   une seconde —, seulement effacées, et plus lentement que la nappe. */
	float lines = 0.0;
	if (puv.x > 0.0 && puv.x < 1.0 && puv.y > 0.0 && puv.y < 1.0)
		lines = texture(u_prev, puv).g * pow(u_decay, 0.6);

	/* L'écume fraîche là où une crête déferle. Le profil NON affûté et sans
	   borne de maille, comme l'original : c'est une question sur la mer, pas sur
	   la façon dont un maillage la dessine. */
	vec3 jac = vec3(0.0);
	// LE MÊME ABRI QUE LA MER : sans lui, un bassin calme moutonnerait quand même
	float shelter = shelter_at(wpos);
	for (int i = 0; i < NW_FOAM; i++) {
		vec2 d = u_wave_a[i].xy;
		float amp = u_wave_a[i].z * shelter;
		float k = u_wave_a[i].w;
		float omega = u_wave_b[i].x;
		float Q = u_wave_b[i].y;
		float f = k * dot(d, wpos) - omega * u_time + u_wave_phase[i];
		gerstner_jacobian_term(d, amp, k, Q, sin(f), jac);
	}
	/* Laissée là où la crête casse — le même jacobien que la mer, un peu plus
	   bas : ce qui reste est ce qui a VRAIMENT déferlé. Elle y demeure, se
	   disperse, et les traînées descendent le dos des vagues qui passent. */
	float breaking = smoothstep(u_jac_foam - 0.06, u_jac_foam - 0.3, gerstner_jacobian(jac)) * 0.9;

	// --- et là où chaque coque déchire la surface ---
	float hull = 0.0;
	for (int i = 0; i < NSHIP; i++) {
		if (i >= u_ship_count) break;
		vec2 rel = wpos - u_ship_pos[i].xz;
		vec2 f2 = normalize(u_ship_fwd[i]);
		vec2 r2 = vec2(f2.y, -f2.x);
		float t_along;
		float gap_m = hull_gap(rel, f2, r2, u_ship_half[i], u_hull_ends[i],
		                       (float(i) + 0.5) / float(NSHIP), t_along);   // mètres à sa flottaison
		float way = clamp(u_ship_speed[i] / 3.0, 0.0, 1.0);
		/* Un ANNEAU sur sa flottaison, pas un disque remplissant toute son
		   empreinte — et il lui faut de l'erre pour paraître. Un navire stoppé ne
		   dérange presque rien ; remplir le contour de la coque quoi qu'il arrive
		   la laissait assise dans une flaque blanche permanente.

		   Plus large en dedans qu'en dehors, pour que l'anneau meure doucement
		   sous son bordé au lieu de finir sur une ligne. */
		float width = mix(2.2, 1.1 + 3.2 * way, step(0.0, gap_m));
		float band = 1.0 - smoothstep(0.0, width, abs(gap_m));

		/* Les ailes d'étrave encore, déposées cette fois dans le champ plutôt
		   que dessinées sur la mer : c'est ce qui laisse le V derrière elle une
		   fois qu'elle est passée, au lieu d'une vague soudée à l'étrave. */
		float fast   = clamp(u_ship_speed[i] / 6.0, 0.0, 1.0);
		float s_aft  = max(u_hull_ends[i].y - t_along * u_ship_half[i].x, 0.0);
		float spread = 1.7 * sqrt(s_aft) * fast;
		float wing   = exp(-pow((gap_m - spread) / (1.0 + 1.2 * fast), 2.0)) * fast;

		hull = max(hull, max(band * (0.06 + 1.0 * way), wing * 0.85) * u_ship_afloat[i]);

		/* Deux fils derrière le safran : l'eau que le gouvernail et l'étambot
		   fendent et retournent, de part et d'autre de l'axe. Déposés en deux
		   petites taches juste derrière la poupe, à chaque image : c'est le
		   navire qui avance qui les tire en lignes, et elles suivent donc sa
		   VRAIE route, virages compris. */
		float lat = dot(rel, r2);
		float behind = u_hull_ends[i].x - t_along * u_ship_half[i].x;   // mètres derrière la poupe
		float at_stern = exp(-pow((behind - 0.8) / 1.1, 2.0));
		float off = 0.22 * u_ship_half[i].y;
		float two = max(exp(-pow((lat - off) / 0.38, 2.0)), exp(-pow((lat + off) / 0.38, 2.0)));
		lines = max(lines, two * at_stern * way * u_ship_afloat[i]);
	}

	/* --- ET LÀ OÙ L'AIR D'UNE ÉPAVE CRÈVE LA SURFACE ---
	   Pas un disque : un bouillon est une plaque de cellules qui enflent, l'eau
	   écartée par chaque poche d'air qui arrive. Deux octaves de bruit qui
	   dérivent l'une contre l'autre lui donnent des cellules qui bouillonnent sur
	   place, et le bord est déchiré parce que le bruit le mange. Ce qui reste est
	   porté et fondu par le champ comme toute écume : le remous s'attarde là où
	   elle a coulé, une fois l'air fini. */
	float boil = 0.0;
	for (int i = 0; i < NBOIL; i++) {
		if (i >= u_boil_count) break;
		float d = length(wpos - u_boil[i].xy) / max(u_boil[i].z, 0.01);
		if (d < 1.0) {
			float n = 0.65 * boil_noise(wpos * 0.55 + vec2(u_time * 0.35, -u_time * 0.21))
			        + 0.35 * boil_noise(wpos * 1.40 - vec2(u_time * 0.60, u_time * 0.47));
			float body = 1.0 - smoothstep(0.25, 1.0, d + (n - 0.5) * 0.55);
			float cells = mix(1.0, smoothstep(0.28, 0.58, n), smoothstep(0.15, 0.75, d));
			boil = max(boil, u_boil[i].w * body * cells);
		}
	}

	// déposer, sans jamais accumuler au-delà de la saturation
	COLOR = vec4(clamp(max(prev, max(max(breaking, hull), boil)), 0.0, 1.0), clamp(lines, 0.0, 1.0), 0.0, 1.0);
}
