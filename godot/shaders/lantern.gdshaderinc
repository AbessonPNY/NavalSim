/* LA LUEUR D'UNE FLAMME DERRIÈRE LA CORNE — une définition, trois usagers : les
 * fanaux du bord (lantern_glow.gdshader), les lanternes des rues
 * (street_lantern.gdshader) et le fanal vu d'en dessous, à travers la mer
 * (ocean.gdshader).
 *
 * Le dégradé radial de Naval.glowTexture (la page), ses quatre arrêts repris
 * tels quels, en sRGB : blanc au cœur, puis chaud, puis ambre, puis rien.
 * r : 0 au centre, 1 au bord du panneau. */
vec4 glow(float r) {
	vec4 a = vec4(1.0, 1.0, 1.0, 1.0);
	vec4 b = vec4(1.0, 228.0 / 255.0, 170.0 / 255.0, 0.92);
	vec4 c = vec4(1.0, 170.0 / 255.0, 70.0 / 255.0, 0.32);
	vec4 d = vec4(1.0, 140.0 / 255.0, 40.0 / 255.0, 0.0);
	if (r < 0.16) return mix(a, b, r / 0.16);
	if (r < 0.42) return mix(b, c, (r - 0.16) / 0.26);
	if (r < 1.0) return mix(c, d, (r - 0.42) / 0.58);
	return d;
}

/* La couleur d'un fanal, 0xffcf7a en sRGB : celle du panneau par défaut
 * (u_color de lantern_glow.gdshader) et celle du feu vu d'en dessous. */
const vec3 LANTERN_FLAME = vec3(1.0, 0.812, 0.478);
