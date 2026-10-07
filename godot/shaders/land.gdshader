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
// la lumière du fond, partagée avec les poissons qui nagent dedans
#include "res://shaders/caustics.gdshaderinc"
// le sol peint à la main : herbe, pavés, sable, par-dessus la teinte des sommets
#include "res://shaders/ground_paint.gdshaderinc"
// le fond de lui-même, sous ce qu'on a peint : sable blanc, vase, herbier, roche
#include "res://shaders/seabed.gdshaderinc"

varying vec3 v_world;

/* LA TERRE DE ELLE-MÊME, en matières : le même partage par la hauteur que la teinte des
   sommets (LandNode.Tint — ses seuils poussés ici, une seule définition), et les rues
   marquées dans l'alpha des sommets. Seulement si une matière naturelle est texturée :
   sinon la couleur des sommets, telle quelle. */
uniform float u_sand_top = 0.6;
uniform float u_grass_from = 2.0;

/* Une somme pondérée ramenée à un poids total de un. */
Surf surf_norm(Surf s, float sum) {
	float k = 1.0 / max(sum, 1e-4);
	s.col *= k; s.n *= k; s.rough *= k; s.metal *= k; s.h *= k; s.ao *= k; s.pierce *= k;
	return s;
}

/* Une matière naturelle, pesée par sa part ET sa hauteur — LUE seulement si elle a une
   part : le sable texturé n'a rien à faire au sommet des montagnes. */
Surf natural_acc(Surf acc, inout float sum, int id, vec3 flat_col, float share, Where wh) {
	if (share <= 0.001) return acc;
	Surf s = u_mat_layer[id] > 0 ? surf_tri(id, wh) : surf_flat(flat_col, id);
	float k = share * pow(0.5 + s.h, 4.0);
	sum += k;
	return surf_acc(acc, s, k);
}

Surf natural(vec4 vcol, vec3 world, float pix, Where wh) {
	Surf base = surf_flat(vcol.rgb, 2);
	base.rough = 0.94;
	float paved = 1.0 - smoothstep(0.6, 0.9, vcol.a);
	if (u_mat_any < 0.5) return base;
	// sous 0,8 m le sable blanc du fond recouvre tout (seabed) : rien à lire ici
	if (seabed_white(-world.y) >= 0.999) return base;
	vec2 q = world.xz + u_true;
	float detail = 1.0 - smoothstep(0.06, 0.35, pix);
	float h = world.y;
	float ts = clamp((h - u_sand_top) / (u_grass_from - u_sand_top), 0.0, 1.0);
	float sand = 1.0 - ts;
	float grass = ts * (1.0 - clamp((h - 18.0) / 102.0, 0.0, 1.0));
	float rock = h < 120.0 ? 0.0 : (h < 700.0 ? min(1.0, (h - 120.0) / 580.0) * 0.7 : 0.7 + 0.3 * min(1.0, (h - 700.0) / 600.0));
	float wood = max(0.0, 1.0 - sand - grass - rock);
	/* une matière naturelle sans texture garde sa teinte, plate, comme les sommets ; par
	   la hauteur de chacune : la plus haute perce, comme dans surf_mix */
	Surf n = surf_zero();
	float sum = 0.0;
	n = natural_acc(n, sum, 2, u_sand, sand, wh);
	n = natural_acc(n, sum, 0, u_grass, grass, wh);
	n = natural_acc(n, sum, 6, u_rock, rock, wh);
	n = natural_acc(n, sum, 7, u_wood, wood, wh);
	n = surf_norm(n, sum);
	// la rue pavée, là où la grille l'a tracée
	return paved > 0.001 ? surf_mix(n, m_cobble(q, wh, detail), paved) : n;
}

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
	// la finesse vient des dérivées d'écran : la largeur d'un pixel sur le sable
	float pix = length(fwidth(v_world.xz));
	// la normale du relief, au monde : elle choisit sous quels angles une texture est plaquée
	vec3 nw = normalize((INV_VIEW_MATRIX * vec4(NORMAL, 0.0)).xyz);
	// où l'on lit, et de combien un pixel s'écarte de son voisin : ici, hors de toute condition
	Where wh;
	wh.p = vec3(v_world.x + u_true.x, v_world.y, v_world.z + u_true.y);
	wh.n = nw;
	wh.dx = dFdx(v_world);
	wh.dy = dFdy(v_world);
	Surf s = ground_paint(seabed(natural(COLOR, v_world, pix, wh), v_world, pix, wh), v_world, pix, wh);
	ALBEDO = s.col * caustiques(v_world, length(v_world - CAMERA_POSITION_WORLD), pix);
	ROUGHNESS = s.rough;
	METALLIC = s.metal;
	// l'occlusion d'une matière texturée : le ciel seul, le soleil n'en sait rien (AO_LIGHT_AFFECT nul)
	AO = s.ao;
	/* LA NORMALE D'UNE MATIÈRE TEXTURÉE, posée sur celle du relief : une texture plaquée
	   au sol, en x et z du monde — son repère tangent est donc +x et +z, couchés sur la pente. */
	if (dot(s.n, s.n) > 1e-6) {
		vec3 t = ground_tangent(nw);
		vec3 b = cross(t, nw);
		vec3 pn = normalize(t * s.n.x + b * s.n.y + nw * sqrt(max(0.0, 1.0 - dot(s.n, s.n))));
		NORMAL = normalize((VIEW_MATRIX * vec4(pn, 0.0)).xyz);
	}
}
