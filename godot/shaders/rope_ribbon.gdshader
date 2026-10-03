/* LE GRÉEMENT EN RUBANS — les cordages d'un modèle, redessinés (ShipNode.Ropes.cs).
 *
 * Un cylindre de trois centimètres tombe sous le pixel vers vingt-cinq mètres,
 * et un objet plus fin qu'un pixel SCINTILLE : il est tramé une image et pas la
 * suivante, selon où il tombe entre deux échantillons. Un plan texturé fait pire
 * — ses lignes fondent en gris dans les mipmaps, et il disparaît vu par la
 * tranche. Chaque corde est donc un RUBAN : deux triangles par tronçon, élargis
 * ici vers l'œil, de leur vraie largeur, mais JAMAIS plus minces que u_min_px
 * pixels. Quand le plancher l'élargit, la corde PÂLIT d'autant (son opacité est
 * sa vraie largeur sur la largeur dessinée) : ce qu'elle couvre à l'écran reste
 * ce qu'elle couvrirait si l'écran avait la résolution qu'il faut, et elle ne
 * clignote plus.
 *
 * Le maillage porte, à chaque bout d'un tronçon, sa position (VERTEX), la
 * direction de la corde (NORMAL), le bord (UV.x, −1 ou +1) et le rayon (UV.y),
 * dans le repère de la pièce : elle suit son mât quand il tombe, comme avant.
 *
 * ÉCLAIRÉ, parce qu'une corde réfléchit : la normale est celle d'un cylindre,
 * reconstruite en travers du ruban — de près elle a le modelé d'une corde.
 */
/* ÉCRIT SA PROFONDEUR, bien que mélangé : sans elle, la profondeur de champ lisait
 * à l'endroit d'une corde celle du ciel derrière elle — l'infini — et la floutait
 * comme l'horizon (signalé : « les haubans n'aiment pas le flou de profondeur »).
 * Sous TAA, c'est la variante opaque (rope_ribbon_taa.gdshader) qui sert. */
shader_type spatial;
render_mode blend_mix, depth_draw_always, cull_disabled, skip_vertex_transform, shadows_disabled;

#include "res://shaders/rope_ribbon.gdshaderinc"
