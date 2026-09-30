/* LA LUEUR DES ANNEAUX — une lumière blanc-bleu qui court le long d'eux.
 *
 * ADDITIF ET SANS OMBRE, parce que c'est de la LUMIÈRE et non de la matière :
 * elle s'ajoute à ce qu'il y a derrière, ne cache rien, n'écrit pas sa
 * profondeur et ne porte aucune ombre. Un anneau éteint n'ajoute donc rien —
 * mais il coûterait quand même son appel de dessin, c'est pourquoi ShipNode le
 * CACHE au lieu de le laisser à zéro.
 *
 * ELLE NE SUIT PAS LE SOLEIL. C'est la règle du projet : ce qui réfléchit suit
 * la lumière, ce qui ÉMET ne la suit pas. Pas de brume non plus — la passe de
 * brume est refusée aux matériaux de shader, et un anneau qui s'éteindrait avec
 * la distance cesserait d'être un phare.
 *
 * LE BLEU EST CELUI DU REPOS, LE BLANC CELUI DE LA PLEINE CHARGE. Un arc
 * électrique va du bleu au blanc à mesure qu'il chauffe, et l'œil lit cette
 * montée comme une montée d'ÉNERGIE sans qu'on ait rien à expliquer. C'est ce
 * qui doit se lire quand l'anneau se prépare à porter le navire ailleurs.
 *
 * L'ANGLE EST CALCULÉ, PAS LU DANS L'UV. Les UV d'un anneau dépendent de la
 * façon dont il a été déplié, qui n'est pas une chose qu'on peut demander à
 * l'auteur de tenir. On prend donc l'angle autour de sa normale, à partir de son
 * centre — deux uniformes que ShipNode connaît, chacun dans le repère du
 * MAILLAGE qui porte ce matériau. Un anneau redéplié dans Blender ne change
 * rien ici.
 */
shader_type spatial;
render_mode blend_add, unshaded, shadows_disabled, depth_draw_never, cull_disabled;

/* LE BLEU DU REPOS. Assez saturé pour se lire comme froid contre un ciel de
   jour, qui est lui-même bleu — d'où le rouge presque nul. */
uniform vec3 u_cold : source_color = vec3(0.16, 0.42, 1.00);
/* ET LE BLANC DE LA CHARGE, qui garde du bleu : un blanc pur se lit comme du
   soleil sur du métal, pas comme une décharge. */
uniform vec3 u_hot : source_color = vec3(0.78, 0.90, 1.00);

/// 0 éteint, 1 pleine charge. ShipNode le monte et le descend en rampe.
uniform float u_level = 0.0;
/* CE QUE VAUT « BLANC » EN HDR. Au-dessus de 1, donc au-dessus de ce qu'un écran
   peut montrer : c'est ce débordement que la passe de lueur ramasse, et c'est
   lui qui fait le halo. Six a été pris pour passer largement le seuil de jour
   que ShipDemo relève pendant la charge — à juger à l'œil. */
uniform float u_energy = 6.0;

/// Le centre de l'anneau et la normale de son plan, dans le repère du MAILLAGE.
uniform vec3 u_centre = vec3(0.0);
uniform vec3 u_axis = vec3(0.0, 0.0, 1.0);

uniform float u_time = 0.0;
/// Combien de bandes courent le long de l'anneau, et leurs tours par seconde.
uniform float u_bands = 3.0;
uniform float u_turns = 0.35;

varying float v_ang;

void vertex() {
	vec3 n = normalize(u_axis);
	/* Deux axes de son plan. Le choix du premier évite le cas dégénéré où l'on
	   croiserait deux vecteurs colinéaires — c'est le même repli que Rings.Axis
	   côté C#, et il n'a pas besoin d'être le même d'un anneau à l'autre : seule
	   compte la continuité le long d'UN anneau. */
	vec3 a = abs(n.x) < 0.9 ? vec3(1.0, 0.0, 0.0) : vec3(0.0, 1.0, 0.0);
	vec3 u = normalize(cross(a, n));
	vec3 w = cross(n, u);
	vec3 d = VERTEX - u_centre;
	v_ang = atan(dot(d, w), dot(d, u)) * 0.15915494 + 0.5;   // 1/2pi : 0..1 le long de l'anneau
}

void fragment() {
	float lvl = clamp(u_level, 0.0, 1.0);
	/* LA BANDE QUI COURT. `fract` d'un angle multiplié : elle repasse d'elle-même
	   par le raccord sans qu'on ait à le traiter, l'angle étant continu modulo 1.
	   Elle court PLUS VITE à mesure que la charge monte. */
	float band = fract(v_ang * u_bands - u_time * u_turns * (0.4 + 1.6 * lvl));
	float crest = 1.0 - abs(band - 0.5) * 2.0;
	/* Plus étroite à froid, plus large et plus molle à chaud : c'est ce qui fait
	   passer d'un filet qui court à un anneau tout entier allumé. */
	float glow = smoothstep(mix(0.82, 0.30, lvl), 1.0, crest);

	// le fil de l'anneau, toujours un peu present des qu'il est allume
	float body = mix(0.06, 0.40, lvl);
	vec3 col = mix(u_cold, u_hot, lvl * (0.30 + 0.70 * glow));
	ALBEDO = col * u_energy * lvl * (body + glow * (0.5 + 1.1 * lvl));
	ALPHA = 1.0;
}
