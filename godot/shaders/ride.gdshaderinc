/* LES RIDES DE LA MER, ÉCRITES UNE SEULE FOIS.
 *
 * Le maillage de la mer ne porte que dix-huit longues vagues ; tout ce qui est
 * plus fin que quelques mètres vit ici, dans la NORMALE. Deux lecteurs : le
 * shader de la mer, qui en tire ce que la surface renvoie, et celui des
 * caustiques, qui en tire la lumière que ces mêmes rides rassemblent sur le
 * fond. C'est une ride qui fait le nœud de lumière sur le sable d'une plage —
 * la houle, à cette profondeur-là, ne courbe presque rien —, si bien que les
 * deux ne peuvent pas diverger sans que le fond cesse d'être l'ombre de sa
 * propre surface.
 *
 * Il faut naval_hash13 : inclure sky.gdshaderinc AVANT celui-ci.
 */

// force des rides, montée avec le vent : 0,40 par calme, 2,6 en tempête
uniform float u_ripple = 0.9;
// leur part, à l'œil (menu, section Mer)
uniform float u_ride_gain = 1.0;
// le vent, qui les couche : elles sont longues EN TRAVERS de lui
uniform vec2  u_wind = vec2(1.0, 0.0);

/* LE BRUIT DE PERLIN, commun aux rides et à l'écume : un bruit de gradient,
   lisse, sans grille visible, ≈ [−1, 1]. */
float perlin(vec2 p) {
	vec2 i = floor(p), f = fract(p);
	vec2 u = f * f * f * (f * (f * 6.0 - 15.0) + 10.0);
	float a0 = naval_hash13(vec3(i, 5.0)) * 6.2831853;
	float a1 = naval_hash13(vec3(i + vec2(1.0, 0.0), 5.0)) * 6.2831853;
	float a2 = naval_hash13(vec3(i + vec2(0.0, 1.0), 5.0)) * 6.2831853;
	float a3 = naval_hash13(vec3(i + vec2(1.0, 1.0), 5.0)) * 6.2831853;
	float g0 = dot(vec2(cos(a0), sin(a0)), f);
	float g1 = dot(vec2(cos(a1), sin(a1)), f - vec2(1.0, 0.0));
	float g2 = dot(vec2(cos(a2), sin(a2)), f - vec2(0.0, 1.0));
	float g3 = dot(vec2(cos(a3), sin(a3)), f - vec2(1.0, 1.0));
	return mix(mix(g0, g1, u.x), mix(g2, g3, u.x), u.y) * 1.4;   // ≈ [−1, 1]
}

/* LES RIDES. Le maillage ne porte que dix-huit longues vagues ; tout ce qui est
   plus fin que quelques mètres doit vivre dans la NORMALE, ou la mer a l'air de
   plastique moulé.

   DE LONGUES RIDES COUCHÉES EN TRAVERS DU VENT. Deux essais écartés : un fbm de
   bruit à valeurs, qui donnait des bosses molles ; puis un bruit PLISSÉ
   (1 − |2n − 1|), qui donnait bien des arêtes, mais un bruit à valeurs plissé
   dessine un RÉSEAU de nervures — une trame de cellules, relevée sur les
   captures, et du papier d'aluminium froissé dès qu'on pousse les rides. Ici
   un bruit de Perlin SIGNÉ et lisse, étiré trois fois en travers du vent : des
   ondulations longues et continues, sans pli ni maille. Trois octaves, tournées
   de 0,38 rad l'une de l'autre pour que les trains se croisent, chacune poussée
   à sa vitesse (les petites plus lentes, c ∝ √λ), et d'amplitude décroissant
   plus vite que leur fréquence ne croît, pour que la pente soit portée par la
   plus longue et que les fines ne fassent que la troubler. */
float ride_h(vec2 p) {
	vec2 w = u_wind;
	vec2 x = vec2(dot(p, w), dot(p, vec2(-w.y, w.x)));   // le long du vent, en travers
	float h = 0.0, amp = 1.0, f = 0.35, c = 1.25;
	for (int i = 0; i < 3; i++) {
		float an = (float(i) - 1.0) * 0.38;
		vec2 r = vec2(cos(an) * x.x - sin(an) * x.y, sin(an) * x.x + cos(an) * x.y);
		vec2 q = vec2((r.x - u_time * c) * f, r.y * f * 0.33) + vec2(float(i) * 17.3, float(i) * 5.1);
		h += perlin(q) * amp;
		amp *= 0.4; f *= 2.1; c *= 0.69;
	}
	return h;
}

vec3 ride_normale(vec2 p, float fade) {
	if (fade <= 0.001) return vec3(0.0, 1.0, 0.0);
	float e = 0.2;
	float h  = ride_h(p);
	float hx = ride_h(p + vec2(e, 0.0));
	float hz = ride_h(p + vec2(0.0, e));
	float s = 0.5 * u_ripple * u_ride_gain * fade / e;
	return normalize(vec3(-(hx - h) * s, 1.0, -(hz - h) * s));
}
