/* LA BRUME D'UN NAVIRE, ÉCRITE UNE FOIS — la queue d'applyHaze (stage.js).
 *
 * Un navire se fond dans le CIEL QUI EST DERRIÈRE LUI, pas dans un gris plat :
 * complètement noyé, il prend exactement la couleur du dôme dans cette
 * direction, et il n'y a plus de bord à voir. La coque procédurale
 * (hull.gdshader) et la passe posée sur les modèles (hull_haze.gdshader)
 * incluent ceci, sans quoi un .glb et une coque dessinée ne disparaîtraient
 * pas de la même façon.
 *
 * L'original mélange APRÈS l'encodage sRGB de three.js, vers la valeur BRUTE du
 * ciel — celle que le dôme écrit à l'écran. Godot mélange en linéaire puis
 * encode ; on vise donc naval_display(ciel), la valeur linéaire qui s'affiche
 * comme ce ciel brut. Les deux bouts sont exacts — navire net, navire noyé —
 * et seul l'entre-deux suit une courbe un peu différente.
 */
#include "res://shaders/sky.gdshaderinc"
#include "res://shaders/haze.gdshaderinc"

vec3 ship_haze_colour(vec3 eye, vec3 world) {
	return naval_display(naval_sky(normalize(world - eye), u_sun, u_zenith, u_horizon));
}
