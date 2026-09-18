/* LA BRUME POSÉE SUR UN MODÈLE .glb — l'équivalent idiomatique de la greffe
 * `onBeforeCompile` d'applyHaze.
 *
 * Les matériaux d'un .glb restent ceux de l'artiste, PBR, textures et relief
 * compris : les réécrire en shader pour y glisser une ligne serait tout perdre
 * pour rien. Ce shader est leur `next_pass` — la même géométrie redessinée
 * par-dessus, qui ne peint que l'air entre l'œil et elle : la couleur du ciel
 * derrière, à l'opacité de la brume traversée.
 *
 * Le seul point délicat est la PROFONDEUR : redessiner exactement la même
 * surface à exactement la même profondeur est une invitation au scintillement
 * (z-fighting). La passe est donc avancée d'un dix-millième de sa distance —
 * dix centimètres à un kilomètre, rien qu'on puisse voir déborder, et bien
 * au-dessus de la précision d'un tampon de profondeur flottant.
 */
shader_type spatial;
render_mode unshaded, blend_mix, depth_draw_never, cull_disabled, shadows_disabled, fog_disabled;

#include "res://shaders/ship_haze.gdshaderinc"

varying vec3 v_world;

void vertex() {
	v_world = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	POSITION = PROJECTION_MATRIX * MODELVIEW_MATRIX * vec4(VERTEX, 1.0);
	// profondeur inversée (Vulkan) : plus grand z = plus près
	POSITION.z *= 1.0001;
}

void fragment() {
	ALBEDO = ship_haze_colour(CAMERA_POSITION_WORLD, v_world);
	ALPHA = haze_along(CAMERA_POSITION_WORLD, v_world);
}
