/* CE QUI FAIT UN SPECTRE — _ghostify et applyGhostFade de ghosts.js, écrits
 * UNE fois pour deux usagers : ce qui est bois ou métal (ship_ghost) et la toile
 * en loques (ship_ghost_sail).
 *
 * Chaque matière du navire est REMPLACÉE par celle-ci, et non doublée d'une
 * passe : un fantôme ne garde rien de l'éclairage de l'artiste — il est teinté
 * vers un vert froid, luit de l'intérieur (il donne sa lumière, il ne prend pas
 * celle de la lune), et laisse voir au travers. Sa brume est dans ce shader
 * même : posée en passe par-dessus, elle redessinerait la coque tout entière,
 * y compris le bas qu'on a fait disparaître, et l'air trahirait la silhouette.
 *
 * UNE SEULE PEAU. Un modèle, c'est un bordé, un vaigrage, un pont, des
 * gaillards — des surfaces l'une derrière l'autre. À 45 % chacune, deux ou trois
 * épaisseurs donnaient un navire presque plein. Chaque matière est donc dessinée
 * deux fois : une pré-passe (u_pass = 0) qui n'écrit que la profondeur, toutes
 * avant toute couleur (render_priority), puis la couleur (u_pass = 1), un rien
 * avancée, qui ne passe plus que sur la surface la plus proche.
 *
 * LE BAS S'EFFACE, VERS LE NOIR. L'opacité et la lumière sont multipliées par une
 * rampe sur la hauteur MONDE : rien à u_lo au-dessus de la mer, entier à u_hi —
 * le plat-bord d'un galion. La lumière s'éteint avec l'opacité, si bien que le
 * bas s'assombrit avant de disparaître ; un spectre qui sombre s'efface par le
 * bas en s'enfonçant. Et la ligne ne se tient pas : elle ONDULE — deux houles
 * lentes qui se croisent le long d'elle et un frisson plus fin —, et des bandes
 * pâles montent dans ce qui reste.
 *
 * PAS DE CONTOUR. Une surface vue de biais s'efface — l'opacité suit |N·V| —,
 * si bien que la silhouette et chaque arête se fondent au lieu de se dessiner
 * d'un trait : le bord d'un spectre est flou. Sa lueur, elle, reste entière
 * dans le cœur, assez forte pour que la lueur de nuit en fasse un halo.
 */
#include "res://shaders/ship_haze.gdshaderinc"

uniform vec3 u_tint = vec3(0.5);              // linéaire : sa couleur, tirée vers le vert pâle
uniform sampler2D u_tex : source_color, filter_linear_mipmap_anisotropic;
uniform float u_has_tex = 0.0;
uniform float u_alpha = 0.0;                  // opacité × fondu
uniform float u_lo = 0.5;
uniform float u_hi = 4.0;
uniform float u_pass = 1.0;                   // 0 : la profondeur seule ; 1 : la couleur

// 0x5fe6c4 ; plus fort que la page (0,95), le bord qui s'efface ne portant plus de lumière
const vec3 GHOST_GLOW = vec3(0.1144, 0.7913, 0.5520) * 1.5;

// la couleur un rien devant la profondeur de la pré-passe (profondeur inversée : plus grand = plus près)
float ghost_nudge() { return 1.0 + 0.00003 * u_pass; }

/* La couleur éclairée, sa lueur (la brume comprise), et son opacité ; « world » :
   le point, en coordonnées du monde (un varying ne s'écrit que dans vertex()). */
void ghost_fragment(vec2 uv, vec3 eye, vec3 world, float time, float facing,
                    out vec3 albedo, out vec3 emission, out float alpha) {
	vec4 tx = u_has_tex > 0.5 ? texture(u_tex, uv) : vec4(1.0);
	float span = u_hi - u_lo;
	float wave = span * (0.16 * sin(world.x * 0.21 + world.z * 0.13 + time * 0.9)
	                   + 0.12 * sin(world.z * 0.37 - time * 1.4)
	                   + 0.05 * sin(world.x * 1.3 + world.z * 0.9 + time * 3.1));
	float rise = smoothstep(u_lo + wave, u_hi + wave, world.y);
	// des bandes pâles qui montent, tordues d'un bord à l'autre
	float band = 0.82 + 0.18 * sin(world.y * 1.7 - time * 1.2 + 1.5 * sin(world.x * 0.5 + world.z * 0.3 + time * 0.3));
	float hz = haze_along(eye, world);
	albedo = u_tint * tx.rgb * (1.0 - hz) * rise;
	emission = GHOST_GLOW * band * rise * (1.0 - hz) + ship_haze_colour(eye, world) * hz;
	// de biais, rien : le bord se fond
	float edge = smoothstep(0.05, 0.6, facing);
	alpha = u_alpha * tx.a * rise * band * edge;
}
