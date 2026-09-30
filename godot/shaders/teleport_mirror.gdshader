/* LE REFLET DU TÉLÉPORTEUR DANS L'EAU — la copie retournée, dessinée.
 *
 * POURQUOI IL FAUT LA DESSINER. Le miroir de la mer est en espace écran : il
 * suit un rayon renvoyé par la surface et le compare à l'image déjà rendue. Or
 * la mer est un matériau OPAQUE, donc dessinée AVANT la passe transparente où
 * vivent les anneaux et la bulle : son image et sa profondeur sont celles d'un
 * monde où ils n'existent pas encore. Aucune écriture de profondeur ne rattrape
 * un ordre de passe — essayé, rendu.
 *
 * Reste ce que la page faisait déjà : rendre une seconde fois depuis un œil en
 * miroir. Ici c'est la GÉOMÉTRIE qu'on retourne plutôt que l'œil — ShipNode tient
 * une copie de l'appareil, symétrique du plan d'eau, et c'est elle qu'on peint.
 * Même lumière (teleport.gdshaderinc), donc pas de dérive possible entre l'objet
 * et son reflet.
 *
 * TROIS CHOSES LE DISTINGUENT D'UNE SIMPLE COPIE :
 *
 *   · il ne teste pas la profondeur — sans quoi la mer, opaque et devant lui, le
 *     cacherait entièrement ;
 *   · il se MASQUE donc lui-même à l'eau, en relisant ce que la passe opaque a
 *     laissé : partout où ce n'est pas la surface, il se refuse. C'est ce qui
 *     l'empêche de peindre par-dessus la coque, la terre ou le ciel ;
 *   · il est RIDÉ par la houle, lue dans le même gerstner.gdshaderinc que la mer.
 *     Un reflet net sur une mer formée est ce qui trahit le plus une image de
 *     synthèse.
 */
shader_type spatial;
/* world_vertex_coords : VERTEX arrive en coordonnées MONDE, comme dans le shader
   de la mer. C'est indispensable pour lire la houle au bon endroit — un point lu
   ailleurs donnerait des rides plausibles mais DÉPHASÉES, c'est-à-dire un reflet
   qui ondule sans suivre l'eau qu'on voit sous lui. */
render_mode blend_add, unshaded, shadows_disabled, depth_draw_never, depth_test_disabled, cull_disabled, world_vertex_coords;

#include "res://shaders/gerstner.gdshaderinc"
#include "res://shaders/shelter.gdshaderinc"
#include "res://shaders/teleport.gdshaderinc"

/// 0 : un anneau ; 1 : la sphère. Le reflet des deux passe par ce shader.
uniform float u_sphere = 0.0;
/// Ce qu'il reste de la lumière après le passage par la surface.
uniform float u_gain = 0.06;
/// Combien la pente de la houle hache le reflet. 0 : lisse comme un miroir.
uniform float u_ripple = 3.0;
/* DE COMBIEN LA HOULE SOULÈVE LA DÉCOUPE. 1 est le chiffre PHYSIQUE : la surface
   est là où la vague la met. Au-dessus, c est du théâtre — utile parce qu à une
   incidence rasante une lame d un mètre ne déplace la découpe que de quelques
   pixels, et que ce saut est justement ce qui dit à l œil que le reflet est POSÉ
   sur l eau et non dessiné dessus. */
uniform float u_swell = 1.0;
/// Le niveau de la mer sous le navire, dans le repère de la SCÈNE — pour le pli.
uniform float u_sea_y = 0.0;
/* LE PLAN D'EAU EN ESPACE VUE (xyz : sa normale, w : sa distance), posé par
   ShipDemo à chaque image.

   EN ESPACE VUE ET NON EN MONDE, et c'est tout le sujet : Godot rend les grands
   mondes en coordonnées RELATIVES À LA CAMÉRA. Un Y reconstruit par
   INV_VIEW_MATRIX n'est donc pas celui qu'on croit, et le masque, qui le
   comparait à une hauteur de mer absolue, rejetait TOUT — donc pas de reflet, ce
   qui est exactement le symptôme observé. En espace vue la question ne se pose
   plus : tout y est dans le même repère, celui que le shader reçoit. */
uniform vec4 u_sea_plane = vec4(0.0, 1.0, 0.0, 0.0);
/// La bande de hauteur qui compte pour de l'eau, autour de ce plan.
uniform float u_sea_band = 6.0;
/* LE MASQUE, QU'ON PEUT COUPER (--reflet 2). Un masque qui refuse tout et une
   géométrie absente donnent la même image : rien. Ce cadran sépare les deux en
   un lancement, ce qui vaut mieux qu'une troisième hypothèse. */
uniform float u_mask = 1.0;
uniform float u_fade_on = 1.0;
/// 1 : peindre la DISTANCE AU PLAN au lieu du reflet (--reflet 4). Noir = eau.
uniform float u_debug = 0.0;
/// Sur combien de mètres sous la surface il s'efface.
uniform float u_fade = 14.0;

varying float v_ang;
varying float v_under;      // ce qu'il est sous la surface, en mètres
varying vec3 v_world;       // et où il est, pour la houle lue par pixel

void vertex() {
	/* L'ANGLE SE PREND EN REPÈRE MODÈLE, où le centre et la normale de l'anneau
	   sont écrits. VERTEX étant maintenant en monde, on l'y ramène — une inversion
	   par sommet, sur soixante-quatre, ce qui ne se mesure pas. */
	v_ang = naval_ring_angle((inverse(MODEL_MATRIX) * vec4(VERTEX, 1.0)).xyz);
	/* LE MONDE, POUR QUE LE FRAGMENT SACHE OÙ IL EST. Il peut être relatif à la
	   caméra — Godot le fait sur les grands mondes — ce qui décale la PHASE de la
	   houle lue plus bas sans en changer la forme. Pour une brisure, cela suffit :
	   on veut des rides plausibles, pas les rides exactes de la facette. */
	v_world = VERTEX;

	/* CE QU'IL EST SOUS LA SURFACE, mesuré en ESPACE VUE contre le plan d'eau, et
	   non par une différence de hauteurs monde — mêmes coordonnées relatives à la
	   caméra, même piège. */
	vec3 pv = (VIEW_MATRIX * vec4(VERTEX, 1.0)).xyz;
	/* NON BORNÉ ICI : le fragment y ajoutera la hauteur de la vague, et il a
	   besoin du signe pour savoir de quel côté de la surface il est. */
	v_under = u_sea_plane.w - dot(u_sea_plane.xyz, pv);

	/* ET PLUS AUCUN DÉPLACEMENT ICI. Rider un reflet en bougeant ses SOMMETS
	   demande une maille plus fine que la ride : sur un anneau de soixante-quatre
	   sommets, deux voisins sont à 3,7 m l'un de l'autre et le décalage montait à
	   7 — le double. Le reflet ne se brisait donc pas, il se PLIAIT en facettes, et
	   cela se voyait comme un défaut de géométrie (signalé). La brisure est passée
	   au fragment, où elle est aussi fine que l'écran. */
}

void fragment() {
	/* PAS UN SEUL `return` ICI, ET CE N'EST PAS UN CHOIX DE STYLE : Godot refuse
	   `return` dans fragment(), et le shader entier cesse alors de compiler. Il ne le
	   dit qu'une fois, dans la console, au milieu du reste — et la pièce disparaît
	   sans autre signe. Une sonde de débogage écrite avec un `return` casse donc
	   exactement ce qu'elle devait mesurer. Trois essais ont été perdus à cela. */
	float lvl = clamp(u_level, 0.0, 1.0);

	/* LE MASQUE EST GÉOMÉTRIQUE, ET NON PLUS UNE LECTURE DE PROFONDEUR.

	   Premier essai : relire le tampon de profondeur pour ne peindre que là où la
	   passe opaque a laissé de l'eau. Mesuré : il rend ZÉRO partout depuis ce
	   matériau — `depth_test_disabled`, dont on a besoin pour passer par-dessus la
	   mer, prive aussi le shader de `DEPTH_TEXTURE`. Le masque refusait donc tout,
	   et le reflet ne paraissait qu'en une frange au ras de l'eau.

	   Or le critère juste n'a jamais eu besoin de cette lecture : ce qu'il faut
	   peindre, c'est ce qui est SOUS LE PLAN D'EAU. Le fragment le sait de
	   lui-même — v_under, mesuré en espace vue contre le plan que ShipNode lui
	   pousse. Rien à lire, rien à reconstruire, et cela vaut à toute distance.

	   Ce qu'on perd : le reflet peut déborder sur une plage qui serait dans le
	   même coin d'écran. Le voir arriver demanderait la profondeur qu'on n'a pas ;
	   au large, personne ne le verra. */
	/* LE MIROIR D'UNE MER N'EST PAS UN PLAN, C'EST LA SURFACE — et c'est ce qui
	   manquait. On coupait le reflet contre le niveau MOYEN, donc son bord était
	   une courbe géométrique parfaite qui n'ondulait pas d'un millimètre, quelle
	   que soit la houle. Signalé.

	   La correction tient en une addition : on lit la hauteur de la vague sous le
	   point et l'on s'en sert comme plan LOCAL. Le bord du reflet suit alors les
	   crêtes et les creux, il monte sur une lame et redescend derrière — et cela
	   par PIXEL, donc sans rien devoir à la finesse du maillage.

	   La brisure par la pente reste, mais elle n'était que la moitié du sujet :
	   elle hachait la lumière là où il fallait aussi DÉPLACER la découpe. */
	vec3 disp; vec3 nrm; float jac; float h;
	gerstner_sum(v_world.xz, u_time, shelter_at(v_world.xz), -1.0, disp, nrm, jac, h);
	float under = v_under + h * u_swell;
	bool sous_l_eau = under > 0.0;

	// il s'efface avec la profondeur, comme tout reflet
	float fade = 1.0;
	if (u_fade_on > 0.5) { fade = 1.0 - clamp(under / u_fade, 0.0, 1.0); fade *= fade; }

	vec3 col;
	if (u_sphere > 0.5) {
		float face = abs(dot(normalize(NORMAL), normalize(VIEW)));
		float a;
		naval_sphere_light(face, lvl, col, a);
		/* LA SPHÈRE SE REFLÈTE EN ADDITIF alors qu'elle est peinte en mélange : un
		   reflet n'OCCULTE rien, il s'ajoute à ce que l'eau montre déjà. Son opacité
		   devient donc une force. */
		col *= a;
	} else {
		col = naval_ring_light(v_ang, lvl, u_time);
	}
	/* LA BRISURE, PAR PIXEL. On lit la houle sous le point — le même
	   gerstner.gdshaderinc que la mer — et l'on éteint le reflet là où la facette
	   penche : c'est ce que fait l'eau, qui renvoie ailleurs ce qu'elle ne renvoie
	   plus vers l'œil. Le résultat est aussi fin que l'écran, là où le même effet
	   obtenu en bougeant les sommets pliait la géométrie en morceaux de 3,7 m.

	   Dix-huit composantes par pixel : c'est ce que la mer paie déjà sur tout
	   l'écran, et le reflet n'en couvre qu'un coin, quelques secondes par saut. */
	float pente = clamp(length(nrm.xz) * u_ripple, 0.0, 1.0);
	// il reste toujours un fond : une eau ridée ne DÉTRUIT pas un reflet, elle le hache
	col *= u_gain * fade * (1.0 - 0.85 * pente);

	// le refus, sans `return` : on éteint plutôt qu'on ne sort
	if (lvl <= 0.002 || (u_mask > 0.5 && !sous_l_eau)) col = vec3(0.0);

	/* LA SONDE EN DERNIER, APRÈS l'extinction : posée avant, ce qu'elle met là où
	   le masque refuse est éteint par le masque lui-même, et l'on ne voit pas
	   l'étendue du refus — qui est justement ce qu'on cherche. */
	if (u_debug > 0.5) col = sous_l_eau ? vec3(0.0, 1.0, 0.0) : vec3(1.0, 0.0, 0.0);

	ALBEDO = col;
	ALPHA = 1.0;
}
