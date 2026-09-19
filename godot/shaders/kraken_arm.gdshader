/* UN BRAS DE KRAKEN, COURBÉ — _bend de kraken.js, fait par la carte graphique.
 *
 * Le maillage est le bras DROIT (celui du modèle, ou le tube que la démo dessine
 * à défaut), debout sur +Y depuis sa racine. Chaque image, le noyau calcule la
 * ligne médiane du bras et ses repères transportés, trente anneaux ; ce shader y
 * plie le maillage. Un sommet à la hauteur y va sur la ligne à cette fraction de
 * sa longueur ; son x le long de N — l'intérieur, où vont les ventouses — et son z
 * le long de N×T, qui garde la main du modèle (T×N le mettrait en miroir et
 * retournerait chaque face). Les normales tournent avec le repère. Au-delà des
 * bouts, le dépassement continue le long de la tangente. Rien n'est réécrit côté
 * processeur : trois tableaux de trente points par bras et par image.
 *
 * Mouillé, sombre, et éclairé comme tout ce qui réfléchit : la brume de la mer
 * par-dessus, comme la coque (voir hull.gdshader).
 */
shader_type spatial;
render_mode cull_disabled;

#include "res://shaders/ship_haze.gdshaderinc"

const int RINGS = 30;
// par anneau, trois vecteurs à la suite : la ligne médiane (mètres locaux), la
// tangente, l'intérieur — un seul tableau, une seule remise par bras et par image
uniform vec3 u_ctn[90];
uniform float u_y0 = 0.0;     // la racine du bras droit
uniform float u_len = 16.0;   // sa longueur
uniform float u_k = 1.0;      // son épaisseur, rayon voulu sur rayon modelé

uniform vec3 u_albedo : source_color = vec3(0.290, 0.106, 0.141);   // 0x4a1b24
uniform float u_roughness = 0.34;
uniform float u_metallic = 0.04;

varying vec3 v_world;

void vertex() {
	float y = VERTEX.y - u_y0;
	float s = clamp(y / u_len, 0.0, 1.0);
	float ex = (y - s * u_len) * u_k;
	float fr = s * float(RINGS - 1);
	int i0 = min(RINGS - 2, int(floor(fr)));
	float u = fr - float(i0);
	int i1 = i0 + 1;
	vec3 T = mix(u_ctn[i0 * 3 + 1], u_ctn[i1 * 3 + 1], u);
	vec3 N = mix(u_ctn[i0 * 3 + 2], u_ctn[i1 * 3 + 2], u);
	vec3 M = cross(N, T);          // N × T, le troisième axe à main droite
	vec3 c = mix(u_ctn[i0 * 3], u_ctn[i1 * 3], u);
	vec3 p = c + N * (VERTEX.x * u_k) + M * (VERTEX.z * u_k) + T * ex;
	NORMAL = normalize(N * NORMAL.x + T * NORMAL.y + M * NORMAL.z);
	// posé à l'origine, transformée identité : les coordonnées sont celles du monde local
	VERTEX = p;
	v_world = p;
}

void fragment() {
	float hz = haze_along(CAMERA_POSITION_WORLD, v_world);
	ALBEDO = u_albedo * (1.0 - hz);
	SPECULAR = 0.5 * (1.0 - hz);
	EMISSION = ship_haze_colour(CAMERA_POSITION_WORLD, v_world) * hz;
	ROUGHNESS = u_roughness;
	METALLIC = u_metallic;
}
