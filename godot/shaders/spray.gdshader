/* L'EMBRUN — les points de splash.js.
 *
 * Un panneau par paquet d'embrun, face à l'œil. Sa taille suit exactement la
 * règle des points de la page : gl_PointSize = size · 600 / profondeur, en
 * PIXELS du tampon — donc, en mètres, size · 1200 / (P11 · hauteur de l'image).
 * Même perspective vraie : un paquet près de l'objectif est vraiment gros, et
 * une taille constante mettrait toute la nappe à une seule distance apparente.
 *
 * Sa couleur est l'horizon ×1,12, écrite TELLE QUELLE à l'écran comme dans la
 * page (un ShaderMaterial y sort sans encodage) : l'embrun est blanc parce qu'il
 * est plein d'air, donc aussi clair que ce qui l'éclaire — la nuit, très peu.
 * Le mélange se fait ici en linéaire et non dans l'espace de l'écran ; les deux
 * bouts sont exacts.
 */
shader_type spatial;
render_mode unshaded, blend_mix, depth_draw_never, cull_disabled, shadows_disabled, fog_disabled;

#include "res://shaders/sky.gdshaderinc"

varying float v_alpha;

void vertex() {
	float size = INSTANCE_CUSTOM.x;
	v_alpha = INSTANCE_CUSTOM.y;
	vec4 c = MODELVIEW_MATRIX * vec4(0.0, 0.0, 0.0, 1.0);
	float metres = size * 1200.0 / (PROJECTION_MATRIX[1][1] * VIEWPORT_SIZE.y);
	POSITION = PROJECTION_MATRIX * vec4(c.xyz + vec3(VERTEX.xy * metres, 0.0), 1.0);
}

void fragment() {
	// Naval.dropTexture : un rond blanc, plein au cœur, 0,72 au tiers, rien au bord
	float r = length(UV - 0.5) * 2.0;
	float a = r < 0.35 ? mix(1.0, 0.72, r / 0.35) : mix(0.72, 0.0, clamp((r - 0.35) / 0.65, 0.0, 1.0));
	ALBEDO = naval_display(u_horizon * 1.12);
	ALPHA = a * v_alpha;
}
