/* WHAT THE TWO WINDOW SHADERS SHARE — the walls of a generated town
 * (town.gdshader) and the window panes of a modelled building
 * (town_glass.gdshader). The same night, the same flame colour, the same share
 * of lit houses and the same draw from a house's position: a modelled house and
 * a box beside it must light up by the same rule, or the street reads as two.
 */

uniform float u_night = 0.0;          // 0 le jour, 1 la nuit — la même que les fanaux
uniform vec3 u_warm = vec3(1.0, 0.66, 0.30);
uniform float u_gain = 2.2;
/// <summary>La part des maisons qui s'allument.</summary>
uniform float u_lit_share = 0.55;

float hash21(vec2 p) {
	return fract(sin(dot(p, vec2(12.9898, 78.233))) * 43758.5453);
}
