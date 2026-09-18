/* L'ÉCUME QUI SE SOUVIENT — le champ persistant de foam.js.
 *
 * L'écume de crête du shader de mer n'existe que là où une crête est raide À
 * CET INSTANT : elle clignote et ne reste pas. La vraie écume est LAISSÉE : une
 * crête déferle, le mousseux reste sur l'eau et se disperse en quelques dizaines
 * de secondes. C'est ce champ-là qui couvre une mer de tempête de traînées, et
 * c'est lui qui manquait au portage.
 *
 * Une passe par image : le champ de l'image d'avant est reporté (décalé de ce
 * dont l'ancre a bougé, pour que l'écume reste où elle est dans le MONDE), étalé
 * sur quatre voisins, atténué, puis on dépose l'écume fraîche là où une crête
 * déferle. Le sillage des coques et les bouillonnements d'épave viendront avec
 * le profil de coque, que la mer ne connaît pas encore.
 */
shader_type canvas_item;
render_mode blend_disabled, unshaded;

// les vagues : MÊMES uniformes que la mer, poussés par OceanNode.PushWaves
#include "res://shaders/gerstner.gdshaderinc"

// le nombre de vagues que lit l'écume, Config.NWavesFoam : les plus énergiques,
// puisque le tri du spectre les met en tête
#define NW_FOAM 7

uniform sampler2D u_prev : filter_linear, repeat_disable;
uniform vec2 u_offset_uv;
uniform vec2 u_origin;
uniform float u_decay = 1.0;
uniform float u_size = 620.0;
uniform float u_texel = 0.0009765625;

void fragment() {
	vec2 wpos = u_origin + UV * u_size;      // XZ monde de ce texel

	/* Reporter le champ. La lecture est décalée de ce dont l'ancre a bougé ;
	   l'étalement sur quatre voisins le laisse se disperser en vieillissant. */
	vec2 puv = UV + u_offset_uv;
	float prev = 0.0;
	if (puv.x > 0.0 && puv.x < 1.0 && puv.y > 0.0 && puv.y < 1.0) {
		prev  = texture(u_prev, puv).r * 0.60;
		prev += texture(u_prev, puv + vec2(u_texel, 0.0)).r * 0.10;
		prev += texture(u_prev, puv - vec2(u_texel, 0.0)).r * 0.10;
		prev += texture(u_prev, puv + vec2(0.0, u_texel)).r * 0.10;
		prev += texture(u_prev, puv - vec2(0.0, u_texel)).r * 0.10;
	}
	prev *= u_decay;

	/* L'écume fraîche là où une crête déferle. Le profil NON affûté et sans
	   borne de maille, comme l'original : c'est une question sur la mer, pas sur
	   la façon dont un maillage la dessine. Le havre n'est pas encore porté,
	   d'où un abri de 1. */
	float steep = 0.0;
	for (int i = 0; i < NW_FOAM; i++) {
		vec2 d = u_wave_a[i].xy;
		float amp = u_wave_a[i].z;
		float k = u_wave_a[i].w;
		float omega = u_wave_b[i].x;
		float Q = u_wave_b[i].y;
		float f = k * dot(d, wpos) - omega * u_time + u_wave_phase[i];
		steep += Q * k * amp * max(sin(f), 0.0);
	}
	float breaking = smoothstep(0.66, 1.05, steep) * 0.9;

	// déposer, sans jamais accumuler au-delà de la saturation
	COLOR = vec4(clamp(max(prev, breaking), 0.0, 1.0), 0.0, 0.0, 1.0);
}
