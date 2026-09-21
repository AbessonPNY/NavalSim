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

/* LE MÉLANGE DE LA PAGE SE FAISAIT DANS L'ESPACE DE L'ÉCRAN — three.js
   mélangeait APRÈS l'encodage sRGB. Mélangé en linéaire (ce que fait un
   blend_mix du moteur), un bois sombre sous 20 % de brume montait de 0,25 à
   0,44 à l'écran au lieu de 0,36 : au gros temps, le navire devenait laiteux
   à quarante mètres (signalé : « très pâle »).

   Relire l'image déjà rendue pour mélanger exactement a été essayé, et
   retiré : cette image ne contient que l'OPAQUE, et la passe écrivait par-
   dessus tout ce qui est transparent — le verre des lanternes et les vitres
   clignotaient selon l'ordre de tri, la neige et les blessures du bordé (des
   passes posées avant celle-ci) disparaissaient. La brume à la puissance 1,5
   approche le mélange de la page pour un bordé sombre (0,09, 0,34, 0,85 pour
   20, 50 et 90 % contre 0,09, 0,35, 0,85), comme sur la coque procédurale. */

varying vec3 v_world;

void vertex() {
	v_world = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	POSITION = PROJECTION_MATRIX * MODELVIEW_MATRIX * vec4(VERTEX, 1.0);
	// profondeur inversée (Vulkan) : plus grand z = plus près
	POSITION.z *= 1.0001;
}

void fragment() {
	ALBEDO = ship_haze_colour(CAMERA_POSITION_WORLD, v_world);
	ALPHA = pow(haze_along(CAMERA_POSITION_WORLD, v_world), 1.5);
}
