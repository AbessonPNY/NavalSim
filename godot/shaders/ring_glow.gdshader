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
 * LA LUMIÈRE ELLE-MÊME EST DANS teleport.gdshaderinc, partagée avec la sphère et
 * avec le reflet des deux dans l'eau : un reflet qui ne serait pas la même
 * lumière n'en serait pas un.
 *
 * PAS DE PROFONDEUR, et la mer ne les reflétera jamais d'elle-même : son miroir
 * est en espace écran, or elle est OPAQUE, donc dessinée avant la passe
 * transparente où ils vivent. C'est teleport_mirror.gdshader qui s'en charge, en
 * peignant la copie retournée.
 */
shader_type spatial;
render_mode blend_add, unshaded, shadows_disabled, depth_draw_never, cull_disabled;

#include "res://shaders/teleport.gdshaderinc"

uniform float u_time = 0.0;

varying float v_ang;

void vertex() {
	v_ang = naval_ring_angle(VERTEX);
}

void fragment() {
	ALBEDO = naval_ring_light(v_ang, clamp(u_level, 0.0, 1.0), u_time);
	ALPHA = 1.0;
}
