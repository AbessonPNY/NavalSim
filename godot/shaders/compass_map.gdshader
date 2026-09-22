/* LA CARTE DANS LA BOUSSOLE : un disque de la carte du capitaine, centré sur
 * le point estimé, nord en haut, transparent pour laisser voir la mer.
 * Posé sur un ColorRect carré : UV va de 0 à 1 d'un bord à l'autre.
 */
shader_type canvas_item;

uniform sampler2D u_chart : source_color, filter_linear, repeat_disable;
uniform vec2 u_center = vec2(0.5);   // le centre, en UV de la carte
uniform vec2 u_span = vec2(0.05);    // le rayon du disque, en UV de la carte (x, y)
uniform float u_alpha = 0.55;

void fragment() {
	vec2 d = UV * 2.0 - 1.0;
	float r = length(d);
	if (r > 1.0) discard;
	vec2 uv = u_center + d * u_span;
	vec3 c = texture(u_chart, uv).rgb;
	// hors de la feuille : le blanc de la marge, pas un pixel de bord étiré
	if (uv.x < 0.0 || uv.y < 0.0 || uv.x > 1.0 || uv.y > 1.0) c = vec3(0.80, 0.76, 0.66);
	// le bord s'efface, pour que le disque se pose sur l'image sans découpe franche
	COLOR = vec4(c, u_alpha * smoothstep(1.0, 0.94, r));
}
