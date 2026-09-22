/* LA COQUE, VUE PAR LA MER — HULL_GLSL d'ocean.js, écrit UNE fois.
 *
 * La mer et le champ d'écume l'incluent tous deux, faute de quoi ils
 * dessineraient deux navires différents : le collier d'un côté, le sillage
 * qu'il dépose de l'autre.
 *
 * Ce qui revient est une distance SIGNÉE en MÈTRES au contour réel de sa
 * flottaison — négative dedans, positive dehors —, par la formule de boîte
 * habituelle. Des mètres et non des unités normalisées : une bande de largeur
 * normalisée constante est mince par le travers et épaisse de plusieurs mètres
 * devant l'étrave, ce qui gonflait justement le collier à ses extrémités.
 */

#define NSHIP 16

/* Une RANGÉE par navire : ses demi-largeurs de l'arrière à l'étrave, en
   fractions de sa plus grande demi-largeur (HullProfile, dans le noyau). En
   flottants plutôt qu'en octets : l'original quantifiait sur huit bits parce
   que WebGL 1 n'offrait pas mieux, contrainte qui n'existe pas ici. */
uniform sampler2D u_hull_prof : filter_linear, repeat_disable;
uniform int   u_ship_count = 0;
uniform vec3  u_ship_pos[NSHIP];
uniform vec2  u_ship_fwd[NSHIP];
uniform vec2  u_ship_half[NSHIP];     // (demi-longueur hors tout, plus grande demi-largeur), mètres
uniform vec2  u_hull_ends[NSHIP];     // où son corps à la flottaison commence et finit, mètres
uniform float u_ship_speed[NSHIP];
uniform float u_ship_afloat[NSHIP];

/* La distance signée en mètres à la vraie flottaison d'UN navire.

   Le corps est borné sur SES PROPRES extrémités, pas sur sa longueur hors
   tout : sa flottaison est plus courte qu'elle — l'étrave s'élance au-dessus,
   la voûte la surplombe — et mesurer les bouts à mi-longueur laissait un fil
   d'écume courir au-delà de l'étrave, là où le profil s'était déjà effilé à
   rien. Pris autour du milieu du corps, pour qu'une étrave fine et un arrière
   plein soient traités sans la supposer symétrique. */
float hull_gap(vec2 rel, vec2 f2, vec2 r2, vec2 half_lb, vec2 ends, float row,
               out float t_along) {
	float along   = dot(rel, f2);
	float athwart = dot(rel, r2);
	float half_l  = max(half_lb.x, 0.01);
	t_along = along / half_l;                        // -1 à l'arrière .. +1 à l'étrave
	float half_b = texture(u_hull_prof, vec2(clamp(t_along * 0.5 + 0.5, 0.0, 1.0), row)).r
	             * half_lb.y;
	float mid  = (ends.x + ends.y) * 0.5;
	float half_body = max((ends.y - ends.x) * 0.5, 0.01);
	vec2 d = vec2(abs(athwart) - half_b, abs(along - mid) - half_body);
	return length(max(d, 0.0)) + min(max(d.x, d.y), 0.0);
}
