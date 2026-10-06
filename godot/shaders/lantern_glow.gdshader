/* LA LUEUR D'UN FEU — les deux sprites de _lanternAt (ship-model.js) :
 * le halo de la lampe, en mètres, et le repère de position, à taille d'écran
 * fixe, sans lequel un fanal disparaît à deux milles.
 *
 * La texture de la page est un dégradé radial dessiné sur un canvas
 * (Naval.glowTexture) : ses quatre arrêts sont repris ici tels quels, en sRGB,
 * plutôt que dessinés dans une image.
 *
 * L'ÉCLAT VISE CE QUE LA PAGE AFFICHE. Là-bas le sprite est additif et s'ajoute
 * APRÈS l'encodage sRGB, dans l'espace de l'écran ; Godot additionne en
 * linéaire puis encode. Sur une nuit, où le fond est presque noir, ajouter la
 * valeur d'écran de la page passée par naval_display rend le même éclat ; en
 * plein jour le feu est de toute façon éteint.
 */
shader_type spatial;
render_mode unshaded, blend_add, depth_draw_never, cull_disabled, shadows_disabled, fog_disabled;

#include "res://shaders/sky.gdshaderinc"
#include "res://shaders/lantern.gdshaderinc"

uniform vec3 u_color = vec3(1.0, 0.812, 0.478);   // 0xffcf7a, en sRGB : voir plus haut
uniform float u_opacity = 0.0;
uniform float u_size = 1.0;     // en mètres ; en fraction d'écran si u_fixed
uniform float u_fixed = 0.0;

void vertex() {
	/* Un panneau toujours face à l'œil, comme un THREE.Sprite. À taille fixe, le
	   décalage est multiplié par la profondeur, exactement comme le fait le
	   sprite de three sans sizeAttenuation : même taille à l'écran à toute
	   distance. */
	vec4 c = MODELVIEW_MATRIX * vec4(0.0, 0.0, 0.0, 1.0);
	float s = u_size * (u_fixed > 0.5 ? -c.z : 1.0);
	POSITION = PROJECTION_MATRIX * vec4(c.xyz + vec3(VERTEX.xy * s, 0.0), 1.0);
}

void fragment() {
	vec4 g = glow(length(UV - 0.5) * 2.0);
	vec3 shown = u_color * g.rgb * g.a * u_opacity;
	ALBEDO = naval_display(shown);
	ALPHA = 1.0;
}
