/* LA LUMIÈRE DU TÉLÉPORTEUR, ÉCRITE UNE SEULE FOIS.
 *
 * Trois shaders la dessinent : l'anneau, la sphère, et le REFLET des deux dans
 * l'eau. Le reflet doit être la même lumière, sinon il n'en est pas un — et une
 * formule recopiée dans trois fichiers dérive au premier réglage. C'est la même
 * règle que gerstner.gdshaderinc, et pour la même raison.
 *
 * Les uniformes sont ici aussi, donc communs aux trois : ShipNode pousse les
 * mêmes valeurs à la pièce et à son reflet sans avoir à savoir laquelle est
 * laquelle.
 */

/* LE BLEU DU REPOS ET LE BLANC DE LA PLEINE CHARGE. Un arc électrique va du bleu
   au blanc à mesure qu'il chauffe, et l'œil lit cette montée comme une montée
   d'ÉNERGIE sans qu'on ait rien à expliquer. */
uniform vec3 u_cold : source_color = vec3(0.16, 0.42, 1.00);
uniform vec3 u_hot : source_color = vec3(0.78, 0.90, 1.00);

/// 0 éteint, 1 pleine charge. ShipNode le monte et le descend en rampe.
uniform float u_level = 0.0;
/// Ce que vaut « blanc » en HDR : au-dessus de 1, donc ce que la lueur ramasse.
uniform float u_energy = 6.0;
/* PAS DE u_time ICI : gerstner.gdshaderinc le déclare déjà, et le reflet inclut
   les deux. Deux déclarations du même uniforme ne compilent pas. Le temps est
   donc PASSÉ à la fonction, ce qui est de toute façon plus honnête — une
   fonction pure ne devrait pas lire une horloge globale. */

// --- l'anneau ---
/// Le centre de l'anneau et la normale de son plan, dans le repère du MAILLAGE.
uniform vec3 u_centre = vec3(0.0);
uniform vec3 u_axis = vec3(0.0, 0.0, 1.0);
/// Combien de bandes courent le long de l'anneau, et leurs tours par seconde.
uniform float u_bands = 3.0;
uniform float u_turns = 0.35;

// --- la sphère ---
/// La dureté de son liseré de bord. Plus haut, plus fin.
uniform float u_rim = 2.6;

/* L'ANGLE LE LONG DE L'ANNEAU, de 0 à 1 — calculé et non lu dans l'UV, les UV
   d'un anneau dépendant de la façon dont il a été déplié, qui n'est pas une
   chose qu'on peut demander à l'auteur de tenir d'une version à l'autre. */
float naval_ring_angle(vec3 vertex) {
	vec3 n = normalize(u_axis);
	/* Le choix du premier axe évite le cas dégénéré où l'on croiserait deux
	   vecteurs colinéaires. Il n'a pas besoin d'être le même d'un anneau à
	   l'autre : seule compte la continuité le long d'UN anneau. */
	vec3 a = abs(n.x) < 0.9 ? vec3(1.0, 0.0, 0.0) : vec3(0.0, 1.0, 0.0);
	vec3 u = normalize(cross(a, n));
	vec3 w = cross(n, u);
	vec3 d = vertex - u_centre;
	return atan(dot(d, w), dot(d, u)) * 0.15915494 + 0.5;   // 1/2pi
}

/* CE QU'UN ANNEAU ÉMET. Additif : c'est de la lumière, pas de la matière. */
vec3 naval_ring_light(float ang, float lvl, float t) {
	/* LA BANDE QUI COURT. `fract` d'un angle multiplié : elle repasse d'elle-même
	   par le raccord sans qu'on ait à le traiter, l'angle étant continu modulo 1.
	   Elle court PLUS VITE à mesure que la charge monte. */
	float band = fract(ang * u_bands - t * u_turns * (0.4 + 1.6 * lvl));
	float crest = 1.0 - abs(band - 0.5) * 2.0;
	// plus étroite à froid, plus large et plus molle à chaud
	float glow = smoothstep(mix(0.82, 0.30, lvl), 1.0, crest);
	// le fil de l'anneau, toujours un peu présent dès qu'il est allumé
	float body = mix(0.06, 0.40, lvl);
	vec3 col = mix(u_cold, u_hot, lvl * (0.30 + 0.70 * glow));
	return col * u_energy * lvl * (body + glow * (0.5 + 1.1 * lvl));
}

/* CE QUE LA SPHÈRE ÉMET, et ce qu'elle cache. `face` vaut 1 vue de face et 0 à
   la tranche.
 *
 * OPAQUE NE DOIT PAS VOULOIR DIRE UNIFORME : une émission plate au-dessus du
 * blanc donne un DISQUE DÉCOUPÉ — plus de sphère, plus de navire. On garde donc
 * un relief dans la LUMIÈRE : un cœur sous le blanc, qui conserve sa couleur au
 * lieu de saturer, et un bord qui brûle à six fois. Une sphère éclairée par le
 * dedans fait cela d'elle-même, sa tranche étant la plus épaisse à traverser.
 *
 * Et l'alpha monte de la tranche vers le cœur, le tout MULTIPLIÉ par le niveau —
 * sans ce dernier facteur la tranche vaut 1 dès que la bulle paraît, et elle
 * s'allume d'un coup au lieu de monter. */
void naval_sphere_light(float face, float lvl, out vec3 col, out float alpha) {
	float shell = pow(1.0 - face, u_rim);          // 0 au centre, 1 à la tranche
	alpha = clamp(mix(shell, 1.0, lvl * lvl) * lvl, 0.0, 1.0);
	/* LA COULEUR BLANCHIT AVANT DE SE FERMER (puissance 0,6) : à mi-charge elle
	   est déjà aux trois quarts du blanc pour un quart de son opacité. L'ordre
	   inverse donne un mur bleu qui s'éclaircit après coup, ce qui ne se lit pas
	   comme une montée en puissance. */
	col = mix(u_cold, u_hot, pow(lvl, 0.6)) * u_energy * lvl * (0.55 + 5.5 * shell);
}
