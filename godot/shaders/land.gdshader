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
	// la finesse vient des dérivées d'écran : la largeur d'un pixel sur le sable
	ALBEDO = COLOR.rgb * caustiques(v_world, length(v_world - CAMERA_POSITION_WORLD),
	                                length(fwidth(v_world.xz)));
	ROUGHNESS = 0.94;
	METALLIC = 0.0;
}
