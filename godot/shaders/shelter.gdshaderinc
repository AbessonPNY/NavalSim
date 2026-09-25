/* CE QUI RESTE DE LA MER DERRIÈRE UN MÔLE, de zéro à un.
 *
 * Le jumeau GPU de World.Shelter — pendant Godot de Naval.SHELTER_GLSL. Trois
 * calculateurs doivent le lire : le shader de la mer (qui en tire l'amplitude
 * de chaque composante), la passe d'écume (qui décide où la crête casse) et
 * l'échantillonneur CPU du noyau (sur lequel la coque flotte). En oublier un ne
 * lève aucune erreur : il fait flotter la coque sur une mer que l'œil ne voit
 * pas.
 *
 * UN SEUL HAVRE À LA FOIS, et c'est assez : on n'est jamais dans deux ports.
 * Le plus proche est poussé par la démo à chaque image.
 *
 * LES COORDONNÉES SONT CELLES OÙ LA MER TRAVAILLE — près de zéro, l'origine
 * flottante déduite —, et NON les mètres vrais du monde. C'est la démo qui fait
 * la conversion en poussant u_harbour (voir PushHarbour) : elle seule connaît
 * l'origine du moment, et un uniforme en mètres vrais serait faux dès le premier
 * recentrage. L'échantillonneur du noyau fait le chemin inverse — il rend ses
 * coordonnées vraies avant d'appeler World.Shelter — et les trois lisent alors
 * le même abri.
 *
 * Ce n'est pas une précaution de style : poussé en mètres vrais, cet uniforme
 * laissait la houle du large entrer dans la rade sous les yeux d'une coque qui,
 * elle, flottait sur une eau calme.
 */

// xy : le centre · z : le rayon extérieur (mur compris) · w : 1 s'il y a un havre
uniform vec4 u_harbour = vec4(0.0);
// le point ABRITÉ, au fond du bassin : l'abri se mesure à sa distance
uniform vec2 u_harbour_pass = vec2(0.0);

float shelter_at(vec2 p) {
	if (u_harbour.w < 0.5) return 1.0;
	if (distance(p, u_harbour.xy) > u_harbour.z) return 1.0;
	float u = clamp(distance(p, u_harbour_pass) / (1.6 * (u_harbour.z - 18.0)), 0.0, 1.0);
	return 1.0 - u * u * (3.0 - 2.0 * u) * 0.88;
}
