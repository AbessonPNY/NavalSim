/* LES FENÊTRES DES BÂTIMENTS MODELÉS, la nuit — la règle de town.gdshader,
 * appliquée cette fois à une surface qui n'est QUE des fenêtres (la matière
 * « fenetre » d'un .glb) plutôt qu'à un mur où il faut les dessiner.
 *
 * Toutes les maisons ne s'allument pas, et le tirage vient de LA POSITION de
 * l'instance, hachée dans le vertex : rien à stocker, et la même maison a la
 * même fenêtre d'une partie à l'autre. Une fenêtre ÉMET : elle garde sa couleur
 * chaude quand les murs prennent celle de la lune.
 */
shader_type spatial;
/* MÉLANGÉE, ET SANS OMBRE PORTÉE. Une vitre invisible qui jette une ombre serait
   la plus visible des fenêtres — et c'est bien ce qu'elle ferait de jour, quand
   il ne doit rien rester d'elle. Le reste suit le mur. */
render_mode blend_mix, depth_draw_opaque;

#include "res://shaders/town_windows.gdshaderinc"
uniform vec3 u_glass : source_color = vec3(0.06, 0.07, 0.09);
/* LE GROUPE DE FENÊTRES (le numéro de la matière : fenetre_002 → 2). Il entre
   dans le tirage : chaque groupe d'une même maison s'allume pour son compte. */
uniform float u_group = 0.0;

varying float v_lit;
varying float v_when;

void vertex() {
	vec2 seed = floor(MODEL_MATRIX[3].xz * 7.13) + u_group * vec2(3.17, 11.3);   // où la maison se tient, et quel groupe
	v_lit = step(1.0 - u_lit_share, hash21(seed));
	/* ET À SON HEURE : chacune s'allume à un moment du crépuscule qui lui est
	   propre, au lieu que toute la ville s'éclaire d'un coup. */
	v_when = 0.15 + 0.6 * hash21(seed + vec2(5.3, 1.7));
}

void fragment() {
	/* ELLE N'EXISTE QUE LA NUIT.
	 *
	 * Le jour, la fenêtre est PEINTE dans l'albédo du bâtiment : ce panneau-ci
	 * est posé devant elle à la main, et s'il porte la moindre matière il cache
	 * ce qu'il est censé montrer. Il s'efface donc entièrement — alpha zéro —
	 * et le bois, le linteau, les carreaux du modéliste restent seuls à l'œil.
	 *
	 * La nuit, il s'allume, et lui seul : les murs prennent la couleur de la
	 * lune pendant qu'il garde sa chaleur, ce qui est la règle de la maison —
	 * ce qui réfléchit suit la lumière, ce qui ÉMET ne la suit pas.
	 *
	 * Et une maison éteinte reste transparente : on y voit alors la fenêtre
	 * peinte, sombre sous la lune, ce qui est exactement ce qu'on veut voir
	 * d'une maison où l'on dort. Les 45 % de maisons non tirées n'ont donc plus
	 * une vitre grise posée dessus, elles ont leur vraie fenêtre. */
	float on = smoothstep(v_when, v_when + 0.12, u_night) * v_lit;
	ALBEDO = u_glass;
	ROUGHNESS = 0.25;
	SPECULAR = 0.6;
	EMISSION = u_warm * (u_gain * on);
	ALPHA = on;
}
