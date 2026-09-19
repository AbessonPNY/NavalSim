/* LA BRUME OÙ IL GLISSE : des bouffées douces du même vert pâle au ras de
 * l'eau, le long de lui et autour, chacune dérivant et respirant à sa façon.
 * La mer cache leur moitié basse — la profondeur est lue —, et c'est ce qui les
 * pose SUR l'eau.
 *
 * Un panneau face à l'œil, tourné sur lui-même, additif. Une matière pour tout
 * le navire ; chaque bouffée porte son opacité, sa taille et son angle en
 * uniformes d'instance. Additionnée après encodage dans la page : on vise la
 * même valeur à l'écran par naval_display, comme le fait lantern_glow.
 */
shader_type spatial;
render_mode unshaded, blend_add, depth_draw_never, cull_disabled, shadows_disabled, fog_disabled;

#include "res://shaders/sky.gdshaderinc"

uniform sampler2D u_tex : filter_linear_mipmap, repeat_disable;
uniform vec3 u_color = vec3(0.435, 0.910, 0.784);   // 0x6fe8c8, en sRGB
instance uniform float u_opacity = 0.0;
instance uniform float u_size = 1.0;
instance uniform float u_rot = 0.0;

void vertex() {
	vec4 c = MODELVIEW_MATRIX * vec4(0.0, 0.0, 0.0, 1.0);
	float cs = cos(u_rot), sn = sin(u_rot);
	vec2 v = vec2(cs * VERTEX.x - sn * VERTEX.y, sn * VERTEX.x + cs * VERTEX.y) * u_size;
	POSITION = PROJECTION_MATRIX * vec4(c.xyz + vec3(v, 0.0), 1.0);
}

void fragment() {
	float a = texture(u_tex, UV).a;
	ALBEDO = naval_display(u_color * a * u_opacity);
	ALPHA = 1.0;
}
