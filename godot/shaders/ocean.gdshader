/* LA MER.
 *
 * Le déplacement vient de gerstner.gdshaderinc, qui est la SEULE définition de
 * la houle côté GPU — la passe d'écume l'inclura aussi. Ce fichier n'ajoute que
 * ce qui lui appartient en propre : le remaillage vers la caméra, et l'ombrage.
 *
 * L'OMBRAGE EST PROVISOIRE, et il vaut mieux le dire que le laisser croire. Le
 * ciel analytique, la brume, le reflet à lobe micro-facettes, l'écume, la vue
 * sous-marine et la coque vue à travers l'eau sont six passes distinctes du
 * projet JavaScript, chacune avec ses propres raisons ; aucune n'est ici. Ce
 * qu'on veut vérifier aujourd'hui est que la GÉOMÉTRIE de la mer est juste et
 * qu'elle s'accorde au solveur.
 */
shader_type spatial;

/* world_vertex_coords : VERTEX arrive et repart en coordonnées MONDE.
 *
 * Ce n'est pas une commodité, c'est l'invariant. La phase de Gerstner doit être
 * prise en espace monde parce que le plan de mer est recentré sur la caméra à
 * chaque image ; en local, la houle resterait collée à la caméra et se
 * désynchroniserait du champ de hauteur que le solveur échantillonne. */
render_mode world_vertex_coords, cull_disabled, specular_schlick_ggx;

#include "res://shaders/gerstner.gdshaderinc"

// demi-largeur du plan de mer, et son nombre de segments : de quoi retrouver la
// taille locale de la maille par la dérivée du remaillage
uniform float u_half = 3500.0;
uniform float u_seg = 384.0;

// le centre du plan, en monde : le remaillage concentre les sommets autour
uniform vec3 u_centre = vec3(0.0);

uniform vec3 u_deep : source_color = vec3(0.012, 0.055, 0.09);
uniform vec3 u_shallow : source_color = vec3(0.06, 0.20, 0.24);
uniform vec3 u_sss : source_color = vec3(0.10, 0.34, 0.30);

varying vec3 v_world;
varying float v_foam;
varying float v_rel;
varying float v_spacing;

void vertex() {
	/* DÉPENSER LES SOMMETS LÀ OÙ ILS SONT VUS. La grille est uniforme dans le
	   tampon, donc on la ramène vers le centre : les quadrilatères font environ
	   un mètre le long de la coque et des dizaines de mètres à l'horizon, pour
	   le même nombre de triangles. Cubique dans la coordonnée normalisée, avec
	   un plancher linéaire pour que le centre même ne s'effondre pas. */
	vec2 local = VERTEX.xz - u_centre.xz;
	vec2 q = local / u_half;
	vec2 aq = abs(q);
	local = sign(q) * (aq * (0.06 + 0.94 * aq * aq)) * u_half;

	/* Combien de mètres ce quadrilatère couvre, tiré tout droit de la dérivée du
	   remaillage ci-dessus. Tout ce qui suit est borné en bande passante contre
	   CELA, et jamais contre la distance. */
	vec2 dsp = (vec2(0.06) + 2.82 * aq * aq) * (u_half * 2.0 / u_seg);
	float spacing = max(dsp.x, dsp.y);
	v_spacing = spacing;

	vec3 w0 = vec3(local.x + u_centre.x, 0.0, local.y + u_centre.z);

	// l'abri du havre n'est pas encore porté : la mer est partout la mer
	float shelter = 1.0;

	vec3 disp;
	vec3 nrm_acc;
	float steep;
	float height;
	gerstner_sum(w0.xz, u_time, shelter, spacing, disp, nrm_acc, steep, height);

	VERTEX = w0 + disp;
	NORMAL = gerstner_normal(nrm_acc);

	v_world = VERTEX;
	v_foam = smoothstep(0.55, 0.95, steep);
	v_rel = height;                 // hauteur signée, pour la lueur de crête
}

void fragment() {
	vec3 n = normalize(NORMAL);

	/* Le corps de l'eau : sombre de haut, plus clair sous un angle rasant, ce
	   qui est la part de Fresnel qu'on garde ici. Le vrai terme se prend sur
	   V·H et non sur N·V — le prendre sur la normale effondre le spéculaire à
	   ~2 % sous tous les angles depuis lesquels on regarde réellement la mer —
	   mais le chemin de scintillement viendra avec le ciel, pas maintenant. */
	float fres = pow(1.0 - clamp(dot(n, VIEW), 0.0, 1.0), 4.0);
	vec3 body = mix(u_deep, u_shallow, fres);

	// la lumière qui traverse une crête : elle s'allume par en dessous
	float lift = clamp(v_rel * 0.5 + 0.5, 0.0, 1.0);
	body += u_sss * lift * 0.35;

	// l'écume de crête, là où la pente sature
	body = mix(body, vec3(0.85, 0.89, 0.92), v_foam * 0.8);

	ALBEDO = body;
	ROUGHNESS = mix(0.04, 0.35, clamp(v_spacing / 40.0, 0.0, 1.0));
	METALLIC = 0.0;
	SPECULAR = 0.6;
	NORMAL = n;
}
