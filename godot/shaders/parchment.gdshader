/* LA CARTE DES MISSIONS : un parchemin déroulé sur une table (MissionMap.cs).
 *
 * Tout est dessiné ici, sans image : la table de bois, la feuille aux bords
 * déchirés et roussis, ses taches, et les deux rouleaux qui la tiennent ouverte.
 * Le trait des missions, les points rouges et les titres sont posés par-dessus,
 * par le Control.
 *
 * u_frame : le cadre 16:9 dans l'écran, en UV (x, y, largeur, hauteur) — toutes
 * les coordonnées de la carte (quests/carte-missions.json) y sont rapportées, si
 * bien qu'un écran plus large ou plus haut montre plus de table, jamais une
 * carte déformée.
 */
shader_type canvas_item;

#include "res://shaders/paper.gdshaderinc"

uniform vec4 u_frame = vec4(0.0, 0.0, 1.0, 1.0);

const vec3 WOOD = vec3(0.24, 0.155, 0.095);
const vec3 SHEET = vec3(0.83, 0.73, 0.56);
const vec3 AGED = vec3(0.64, 0.51, 0.33);
const vec3 SCORCH = vec3(0.36, 0.24, 0.13);
// the sheet between its rolls, and the rolls themselves (centre, half-width)
const float ROLL_L = 0.072, ROLL_R = 0.925, ROLL_HW = 0.078;

vec3 wood(vec2 p) {
	// planks along x, a seam every quarter of the height; the grain is stretched with them
	float plank = floor(p.y * 4.0);
	vec2 q = vec2(p.x * 2.2 + plank * 3.7, p.y * 46.0);
	float g = paper_fbm(q) * 0.6 + 0.4 * paper_noise(vec2(p.x * 9.0, p.y * 160.0 + plank));
	vec3 c = WOOD * (0.72 + 0.55 * g);
	float seam = smoothstep(0.012, 0.0, abs(fract(p.y * 4.0) - 0.5) - 0.488);
	return c * (1.0 - 0.55 * seam);
}

/* the torn edge: where the sheet stops, top and bottom, at this x */
float edge_top(float x) { return 0.085 + 0.016 * paper_fbm(vec2(x * 7.0, 1.3)) + 0.006 * paper_noise(vec2(x * 80.0, 4.1)); }
float edge_bot(float x) { return 0.962 - 0.012 * paper_fbm(vec2(x * 6.0, 7.7)) - 0.005 * paper_noise(vec2(x * 70.0, 2.9)); }

vec3 sheet(vec2 p) {
	vec3 c = SHEET * (0.95 + 0.10 * paper_noise(p * vec2(700.0, 400.0)));
	// the blotches of age, large and soft, and the darker stains
	float m = paper_fbm(p * vec2(4.0, 2.6));
	c = mix(c, AGED, smoothstep(0.40, 0.90, m) * 0.55);
	float stain = paper_fbm(p * vec2(9.0, 6.0) + 31.0);
	c = mix(c, AGED * 0.9, smoothstep(0.60, 0.85, stain) * 0.35);
	// the edges brown first: it is where hands and damp take the sheet
	float t = edge_top(p.x), b = edge_bot(p.x);
	float near = min(p.y - t, b - p.y);
	c = mix(c, AGED, smoothstep(0.07, 0.0, near) * 0.6);
	c = mix(c, SCORCH, smoothstep(0.012, 0.0, near) * 0.7);
	return c;
}

/* a roll: a cylinder of the same sheet, lit from the upper left, its layers showing */
vec3 roll(vec2 p, float cx, out float inside) {
	float t = (p.x - cx) / ROLL_HW;
	inside = step(abs(t), 1.0);
	float n = sqrt(max(0.0, 1.0 - t * t));
	vec3 normal = vec3(t, 0.0, n);
	// diffuse from the upper left, the round edges falling into shade, and a soft sheen
	vec3 L = normalize(vec3(-0.55, 0.0, 0.83));
	float lit = 0.22 + 0.86 * max(0.0, dot(normal, L));
	float sheen = pow(max(0.0, dot(reflect(-L, normal), vec3(0.0, 0.0, 1.0))), 12.0) * 0.18;
	vec3 c = mix(SHEET, AGED, 0.25 + 0.35 * paper_fbm(vec2(cx * 7.0 + t * 0.3, p.y * 2.5))) * lit + sheen;
	c *= 1.0 - 0.5 * pow(1.0 - n, 3.0);
	return c;
}

void fragment() {
	vec2 p = (UV - u_frame.xy) / u_frame.zw;
	vec3 col = wood(p);

	// the sheet, from roll to roll, with its shadow on the table
	float t = edge_top(p.x), b = edge_bot(p.x);
	float inSheet = step(ROLL_L, p.x) * step(p.x, ROLL_R) * step(t, p.y) * step(p.y, b);
	float shadow = smoothstep(0.03, 0.0, p.y - b) * step(b, p.y) + smoothstep(0.02, 0.0, t - p.y) * step(p.y, t) * 0.5;
	col *= 1.0 - 0.45 * shadow * step(ROLL_L, p.x) * step(p.x, ROLL_R);
	if (inSheet > 0.5) {
		col = sheet(p);
		// the rolls darken the sheet where it leaves them
		float dl = p.x - (ROLL_L + ROLL_HW), dr = (ROLL_R - ROLL_HW) - p.x;
		col *= 1.0 - 0.35 * smoothstep(0.035, 0.0, min(dl, dr));
	}

	// the rolls, over everything: they run off the top and bottom of the table
	float in_l, in_r;
	vec3 rl = roll(p, ROLL_L, in_l);
	vec3 rr = roll(p, ROLL_R, in_r);
	// and cast their own shadow on the table, to the right of each (light from the left)
	float rs = smoothstep(0.03, 0.0, abs(p.x - (ROLL_L + ROLL_HW + 0.012)) - 0.012) * (1.0 - inSheet)
	         + smoothstep(0.03, 0.0, abs(p.x - (ROLL_R + ROLL_HW + 0.012)) - 0.012);
	col *= 1.0 - 0.35 * rs;
	col = mix(col, rl, in_l);
	col = mix(col, rr, in_r);

	COLOR = vec4(col, 1.0);
}
