/* CE QUI ONDULE SUR LE FOND — l'herbier et les gorgones, semés en foule
 * (LandNode.BuildCrowd, semis « ondule »).
 *
 * Ils se couchent avec le RESSAC (gerstner_surge) : le va-et-vient que la houle
 * qu'on voit passer pousse jusqu'au fond, fort dans les hauts-fonds, éteint par
 * grand fond, calmé par l'abri d'une rade. Le pied tient, la pointe suit : la
 * flexion croît comme le carré de la hauteur, et la course est bornée à ce que
 * la lame peut donner sans s'arracher. Un frémissement à part, lent et propre à
 * chaque pièce, les garde vivants par mer d'huile.
 *
 * LA BRUME EST ICI, et non en passe : une passe redessinerait la géométrie
 * IMMOBILE par-dessus celle qui bouge, et se décollerait d'elle. Même tour que la
 * voile — l'albédo s'éteint, l'émission prend la couleur de l'air.
 *
 * La couleur et le grain viennent de la matière du modèle (u_albedo,
 * u_roughness) : remplacer le .glb par celui de l'artiste en reprend la teinte.
 */
shader_type spatial;
render_mode cull_disabled, world_vertex_coords;

#define NAVAL_SKY_GLOBAL
#include "res://shaders/ship_haze.gdshaderinc"
#include "res://shaders/gerstner.gdshaderinc"
#include "res://shaders/shelter.gdshaderinc"

uniform vec3 u_albedo = vec3(0.08, 0.12, 0.03);
uniform float u_roughness = 0.8;
// le pied et la hauteur du modèle, dans SON repère : de là, la part de lame qui fléchit
uniform float u_base_y = 0.0;
uniform float u_height = 1.0;
// la souplesse : 1 pour l'herbe, moins pour une gorgone, qui est cornée
uniform float u_bend = 1.0;

varying vec3 v_world;

void vertex() {
	// le pied de CETTE pièce, et sa hauteur en mètres (l'échelle est dans sa matrice)
	vec3 foot = (MODEL_MATRIX * vec4(0.0, u_base_y, 0.0, 1.0)).xyz;
	float scale = length(MODEL_MATRIX[1].xyz);
	float h = u_height * scale;
	// VERTEX est déjà en monde (world_vertex_coords) : sa part de lame, du pied à la pointe
	float up = clamp((VERTEX.y - foot.y) / max(h, 1e-3), 0.0, 1.0);
	float w = up * up;

	vec2 s = gerstner_surge(foot.xz, u_time, shelter_at(foot.xz), max(-foot.y, 0.1)) * u_bend;
	// le frémissement, lent, déphasé d'une pièce à l'autre
	float seed = dot(foot.xz, vec2(0.173, 0.291));
	s += vec2(sin(u_time * 0.9 + seed * 7.0), cos(u_time * 0.7 + seed * 5.0)) * 0.06 * h * u_bend;
	// pas plus que ce qu'une lame peut donner : les deux tiers de sa hauteur
	float L = length(s), cap = 0.65 * h;
	if (L > cap) s *= cap / L;

	VERTEX.xz += s * w;
	// la pointe qui se couche descend un peu : la lame garde à peu près sa longueur
	VERTEX.y -= 0.5 * dot(s, s) / max(h, 1e-3) * w;
	v_world = VERTEX;
}

void fragment() {
	if (!FRONT_FACING) NORMAL = -NORMAL;
	float hz = haze_along(CAMERA_POSITION_WORLD, v_world);
	ALBEDO = u_albedo * (1.0 - hz);
	ROUGHNESS = u_roughness;
	SPECULAR = 0.3 * (1.0 - hz);
	EMISSION = ship_haze_colour(CAMERA_POSITION_WORLD, v_world) * hz;
}
