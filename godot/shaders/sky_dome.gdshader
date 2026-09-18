/* LE DÔME DE CIEL.
 *
 * Il n'ajoute qu'une chose à sky.gdshaderinc : le DISQUE solaire, que lui seul
 * dessine. La mer, elle, tire son éclat d'un lobe micro-facettes — ce qui est ce
 * qui l'étale en route de scintillement au lieu d'y coller une pastille.
 */
shader_type sky;

#include "res://shaders/sky.gdshaderinc"

// l'éclair PROCHE, qui éclaire toute la voûte — à ne pas confondre avec
// u_storm_flash, qui n'allume qu'un quart d'horizon à six milles de là
uniform float u_flash = 0.0;

// la lune du dôme : sa direction (partagée avec la mer), son disque et sa phase
uniform vec3 u_moon = vec3(0.0, 1.0, 0.0);
uniform float u_moon_disc = 0.0;
uniform float u_moon_phase = -1.0;

void sky() {
	vec3 dir = EYEDIR;
	vec3 c = naval_sky(dir, u_sun, u_zenith, u_horizon);

	// le disque lui-même
	c += vec3(1.0, 0.95, 0.85) * pow(max(dot(normalize(dir), u_sun), 0.0), 2200.0) * 6.0;

	/* LA LUNE, au dôme seulement comme le disque du soleil : un disque pâle
	   éclairé du côté qui fait face au soleil, le terminateur en ellipse à
	   travers, la part sombre à peine là contre le ciel, et un halo froid.
	   Dessinée plus grande que nature, comme l'œil s'en souvient. u_moon_phase
	   va de 1 (nouvelle) à −1 (pleine). */
	float ml = u_moon_disc * (1.0 - u_storm);
	if (ml > 0.001) {
		vec3 d = normalize(dir);
		float cm = dot(d, u_moon);
		const float MR = 0.021;
		vec3 rt = normalize(cross(u_moon, vec3(0.0, 1.0, 0.0)) + vec3(1e-5, 0.0, 0.0));
		vec3 upm = cross(rt, u_moon);
		vec2 p = vec2(dot(d, rt), dot(d, upm)) / MR;
		float r2 = dot(p, p);
		if (cm > 0.0 && r2 < 1.0) {
			vec2 s = normalize(vec2(dot(u_sun, rt), dot(u_sun, upm)) + vec2(1e-5, 0.0));
			vec2 q = vec2(dot(p, s), dot(p, vec2(-s.y, s.x)));
			float edge = u_moon_phase * sqrt(max(0.0, 1.0 - q.y * q.y));
			float lit = smoothstep(edge - 0.08, edge + 0.08, q.x);
			float limb = 1.0 - 0.3 * r2;
			vec3 face = mix(c * 0.6 + vec3(0.02, 0.025, 0.035), vec3(0.95, 0.96, 1.0) * 1.3 * limb, lit);
			c = mix(c, face, smoothstep(1.0, 0.9, r2) * min(1.0, ml * 2.0));
		}
		c += vec3(0.55, 0.62, 0.80) * pow(max(cm, 0.0), 1500.0) * 0.35 * ml;
		c += vec3(0.35, 0.40, 0.55) * pow(max(cm, 0.0), 80.0) * 0.06 * ml;
	}

	/* La décharge éclaire toute la voûte, et le plus fort en bas. C'est ce qui
	   sépare l'éclair proche du grain lointain : celui-là est une lumière
	   QUELQUE PART, celui-ci est au-dessus d'elle. */
	c += vec3(0.62, 0.70, 0.92) * u_flash * (1.6 - 0.9 * clamp(dir.y, 0.0, 1.0));

	// le fond est VU, donc desencode ; la passe cubemap nourrit la lumiere
	// ambiante et garde les valeurs brutes
	COLOR = AT_CUBEMAP_PASS ? c : naval_display(c);
}
