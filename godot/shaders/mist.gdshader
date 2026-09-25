/* LA BRUME RASANTE — celle qui traîne sur l'eau au petit matin.
 *
 * Ce n'est PAS la brume de surface, et les deux ne font pas le même métier. Celle
 * de `SeaFog` est une perte de VISIBILITÉ : une extinction par mètre, qui efface
 * l'horizon et pâlit ce qui est loin, mais qu'on ne voit jamais elle-même — on
 * voit ce qu'elle fait. Celle-ci est un OBJET : des traînées basses, stagnantes
 * et clairsemées, qu'on regarde. Un matin de calme en a les deux, et c'est
 * pourquoi celle-ci monte avec celle-là.
 *
 * DES NAPPES HORIZONTALES, ET PLATES. La fumée d'un canon est un nuage
 * qui roule et qui monte ; une brume de rayonnement ne monte pas — elle s'étale,
 * parce que c'est l'air au contact de l'eau froide qui condense, et rien ne le
 * soulève. Quelques nappes superposées, la plus haute à deux mètres, rendent donc
 * mieux la chose que n'importe quel volume : on voit à travers, elles se
 * déchirent, et le regard rasant les traverse en longueur, ce qui les épaissit
 * exactement comme de la vraie brume.
 *
 * REPLIÉE AUTOUR DE L'ŒIL, comme la pluie et la neige : le maillage est un disque
 * posé à l'origine, que le vertex shader recentre sur la caméra et pose sur la
 * houle. Rien n'est calculé par le processeur, jamais.
 */
shader_type spatial;
render_mode blend_mix, depth_draw_never, cull_disabled, unshaded, shadows_disabled;

#include "res://shaders/gerstner.gdshaderinc"
#include "res://shaders/shelter.gdshaderinc"
#include "res://shaders/sky.gdshaderinc"
#include "res://shaders/ride.gdshaderinc"

// l'œil, et ce qui décide de la nappe
uniform vec3 u_eye = vec3(0.0);
// de 0 à 1 : ce qu'il y en a, ce matin
uniform float u_mist = 0.0;
// l'épaisseur de ce qu'on voit, en mètres — la nappe la plus haute
uniform float u_mist_top = 2.0;
// la taille des traînées, en mètres, et leur dérive
uniform float u_mist_patch = 90.0;
uniform float u_mist_drift = 0.35;
// jusqu'où on la dessine
uniform float u_mist_far = 240.0;

// la hauteur de la nappe la plus basse au-dessus du niveau moyen, en mètres
uniform float u_mist_base = 0.05;

varying float v_alpha;
varying float v_clear;
varying vec3 v_world;

void vertex() {
	/* LE DISQUE EST POSÉ À L'ORIGINE et recentré ici : sa géométrie ne veut rien
	   dire sans cela. VERTEX.y porte le NUMÉRO de la nappe, de 0 à 1, et non une
	   hauteur — c'est ici qu'il en devient une. */
	float couche = VERTEX.y;
	vec2 p = VERTEX.xz + u_eye.xz;

	/* LES NAPPES SONT PLATES, et c'est la houle qui passe au travers.
	 *
	 * Elles suivaient la lame, et c'était deux fautes pour une. La première se
	 * voit : une brume de rayonnement ne monte ni ne descend avec la mer — c'est
	 * de l'air froid posé là, et un air posé est horizontal. La seconde se voyait
	 * plus encore : la nappe la plus basse, portée par la houle, plongeait SOUS
	 * la surface dans le creux d'à côté, et l'eau étant translucide on la voyait
	 * dessous, en plaques grises aux bords droits.
	 *
	 * On garde tout de même la hauteur d'eau, mais pour l'EFFACER : là où une
	 * crête traverse la nappe, la brume disparaît, et elle ne reste que dans les
	 * creux. C'est ce que fait la vraie — elle s'accumule dans les hollows et la
	 * lame la perce. */
	vec3 disp; vec3 nrm; float jac; float h;
	gerstner_sum(p, u_time, shelter_at(p), -1.0, disp, nrm, jac, h);

	float y = u_mist_base + couche * u_mist_top;
	VERTEX = vec3(p.x - u_eye.x, y, p.y - u_eye.z);
	v_world = vec3(p.x, y, p.y);
	v_clear = y - h;

	/* PLUS DENSE EN BAS, et qui s'éteint au sommet : une couche de contact. */
	float profil = 1.0 - couche * couche;

	/* ET RIEN SOUS LE NEZ. Une nappe qui passe à travers l'œil se lit comme un
	   voile collé à l'écran ; on l'efface sur les douze premiers mètres, et on la
	   rend au loin. Elle s'efface aussi au bord du disque, faute de quoi il aurait
	   un bord. */
	float d = distance(p, u_eye.xz);
	float pres = smoothstep(4.0, 16.0, d) * (1.0 - smoothstep(u_mist_far * 0.7, u_mist_far, d));

	v_alpha = u_mist * profil * pres;
}

void fragment() {
	if (v_alpha < 0.004) discard;

	/* CE QUI EN RESTE À CET ENDROIT, calculé au FRAGMENT et non au sommet : deux
	   octaves qui dérivent avec le vent, la grande qui fait les bancs et la petite
	   qui les déchire. Au sommet, le bruit n'était connu que quatorze fois sur le
	   rayon et la nappe se lisait en grands polygones aux bords rectilignes — une
	   brume n'a pas d'arêtes. Une brume stagnante dérive à peine : un dixième du
	   vent, pas davantage, sinon elle file comme de la fumée et le calme du matin
	   s'en va. */
	vec2 q = v_world.xz / max(u_mist_patch, 1.0) + u_wind * u_time * u_mist_drift * 0.01;
	float n = perlin(q) * 0.62 + perlin(q * 2.7 + 11.0) * 0.38;
	// clairsemée : la moitié de l'eau est nue, et c'est ce qui la rend crédible
	float banc = smoothstep(-0.05, 0.45, n);

	// la crête qui perce : sous l'eau il n'y a pas de brume, et au ras il y en a peu
	float perce = smoothstep(0.0, 0.30, v_clear);

	float a = v_alpha * banc * perce;
	if (a < 0.004) discard;

	/* ELLE N'A PAS DE COULEUR PROPRE : elle diffuse le ciel, comme l'écume et la
	   fumée. Celle de l'HORIZON, qui porte l'heure — bleutée avant le jour, rose
	   au lever, blanche à midi —, et c'est ce qui fait qu'elle appartient au
	   matin sans qu'on ait à le lui dire. */
	ALBEDO = u_horizon * 1.04;
	ALPHA = clamp(a, 0.0, 1.0);
}
