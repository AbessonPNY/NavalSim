/* LA HOULE, ÉCRITE UNE SEULE FOIS CÔTÉ GPU.
 *
 * Trois calculateurs doivent lire la même mer : ce fichier (que le shader de la
 * mer et la passe d'écume incluent tous les deux), l'échantillonneur CPU de
 * core/NavalSim.Core/Ocean.cs, et rien d'autre. En oublier un ne lève aucune
 * erreur — il fait flotter la coque sur une mer que l'œil ne voit pas, ce qui
 * est la panne silencieuse contre laquelle tout ce projet est écrit.
 *
 * C'est le pendant Godot de Naval.HULL_GLSL et Naval.SHELTER_GLSL, qui étaient
 * des chaînes JavaScript concaténées dans plusieurs shaders. `#include` fait
 * ici le même travail, en mieux : le fichier existe, donc on peut l'ouvrir.
 *
 * Reste l'irréductible : le C# et ce fichier sont deux transcriptions de la
 * même arithmétique, et rien dans le langage ne peut les lier. C'est le banc de
 * parité qui tient cette moitié-là — il compare le C# au JavaScript d'origine,
 * dont ce shader est la copie conforme. Toute modification ici en demande une
 * là, et l'inverse.
 */

#define NW 18

// --- le spectre, tel que Ocean.cs le pose à chaque image ---
// xy : direction de propagation · z : amplitude en mètres · w : nombre d'onde
uniform vec4  u_wave_a[NW];
// x : pulsation · y : raideur de Gerstner (le terme horizontal)
uniform vec2  u_wave_b[NW];
// ce que l'origine flottante doit à chaque composante, réduit modulo 2π en
// double côté C# — c'est ce qui permet de naviguer à des milliers de kilomètres
// sans que la mer ne meure dans les sept chiffres d'un flottant 32 bits
uniform float u_wave_phase[NW];

uniform float u_time;
// exposant d'aiguisage des crêtes. 1 = sinusoïde pure
uniform float u_sharp;


/* LE PROFIL AIGUISÉ, et la raison qu'il existe.
 *
 * Gerstner seul ne peut pas porter une crête pointue : son cuspage vient du
 * terme horizontal Q, et à dix-huit composantes le budget de raideur se divise
 * si loin que chacune reste quasi sinusoïdale. Le profil est donc piqué
 * directement — sign(s)·|s|^p.
 *
 * La symétrie IMPAIRE n'est pas décorative : elle fait que la moyenne reste
 * exactement nulle. Le moindre décalage continu ici déplacerait silencieusement
 * le niveau moyen de la mer, et la flottaison de toute la flotte avec lui.
 *
 * `dsl` rend la dérivée, sur cos(f) : les crêtes ne sont pas seulement plus
 * hautes, elles sont plus raides sur leurs flancs, et c'est cela qui se lit
 * comme une mer dure plutôt que comme une houle de bosses rondes.
 */
float gerstner_profile(float s, float sharp, out float dsl) {
	float as = abs(s);
	float sp = pow(max(as, 1e-4), sharp - 1.0);
	dsl = sharp * sp;
	return sign(s) * as * sp;
}


/* LA SOMME DU SPECTRE en un point du MONDE.
 *
 * En monde, jamais en local : le plan de mer est recentré sur la caméra à
 * chaque image, donc une phase prise en local collerait la houle à la caméra —
 * plus aucune sensation d'erre — et la désynchroniserait du champ de hauteur
 * que le solveur échantillonne.
 *
 * `spacing` est la taille locale de la maille, en mètres, et sert à borner la
 * bande passante. Négatif, il la désactive : la passe d'écume n'a pas de
 * maille, et le CPU non plus.
 *
 * `shelter` est ce qui reste de la mer dans le havre. Contrairement au terme
 * d'anticrénelage ci-dessous, qui n'appartient qu'au maillage, celui-ci est de
 * la PHYSIQUE : le solveur et l'écume le lisent aussi.
 */
void gerstner_sum(vec2 world_xz, float t, float shelter, float spacing,
                  out vec3 disp, out vec3 nrm_acc, out float steep, out float height) {
	disp = vec3(0.0);
	nrm_acc = vec3(0.0);
	steep = 0.0;
	height = 0.0;

	for (int i = 0; i < NW; i++) {
		vec2 d = u_wave_a[i].xy;
		float amp = u_wave_a[i].z;
		float k = u_wave_a[i].w;
		float omega = u_wave_b[i].x;
		float Q = u_wave_b[i].y;

		/* Écarter toute vague que cette maille est trop grossière pour porter.
		   Une longueur d'onde demande plusieurs sommets en travers ; en dessous,
		   le maillage l'échantillonne crénelée et le battement entre les deux
		   fréquences est précisément le moiré qu'on voit. Fondre l'amplitude
		   retire le battement au lieu de le laisser se battre avec la grille.

		   Borné contre la taille locale de la MAILLE et jamais contre la
		   distance : un seul critère, donc aucune couture ne peut apparaître là
		   où deux seuils de distance se seraient contredits.

		   Près de la coque la maille fait environ un mètre, donc rien n'y est
		   retiré et la surface visible correspond bien au champ de hauteur que
		   le solveur échantillonne. */
		if (spacing > 0.0) {
			float lambda = 6.28318530718 / k;
			amp *= smoothstep(2.5, 6.0, lambda / spacing);
		}
		amp *= shelter;

		float f = k * dot(d, world_xz) - omega * t + u_wave_phase[i];
		float c = cos(f);
		float s = sin(f);

		float dsl;
		float hs = gerstner_profile(s, u_sharp, dsl);

		disp.x += Q * amp * d.x * c;
		disp.z += Q * amp * d.y * c;
		disp.y += amp * hs;
		height += amp * hs;

		float WA = k * amp * dsl;
		nrm_acc.x -= d.x * WA * c;
		nrm_acc.z -= d.y * WA * c;
		nrm_acc.y -= Q * WA * s;

		steep += Q * WA * max(s, 0.0);
	}
}


/* La normale, assemblée depuis l'accumulateur. Écrite ici plutôt que chez
   l'appelant pour que la mer et l'écume ne puissent pas l'assembler
   différemment. */
vec3 gerstner_normal(vec3 nrm_acc) {
	return normalize(vec3(nrm_acc.x, 1.0 - nrm_acc.y, nrm_acc.z));
}
