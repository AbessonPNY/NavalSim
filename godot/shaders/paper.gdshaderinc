/* LE GRAIN DU PAPIER — une définition, deux usagers : la carte de la chambre
 * (chart.gdshader) et la carte des missions (parchment.gdshader). */

/* a hash without sin(): fract(sin(x) * big) loses its randomness on the GPU and
   lines its values up, which thresholded stains showed as straight-edged patches */
float paper_hash(vec2 p) {
	vec3 p3 = fract(vec3(p.xyx) * 0.1031);
	p3 += dot(p3, p3.yzx + 33.33);
	return fract((p3.x + p3.y) * p3.z);
}

// a value noise, smooth (quintic) between its lattice points
float paper_noise(vec2 p) {
	vec2 i = floor(p), f = fract(p);
	f = f * f * f * (f * (f * 6.0 - 15.0) + 10.0);
	float a = paper_hash(i);
	float b = paper_hash(i + vec2(1.0, 0.0));
	float c = paper_hash(i + vec2(0.0, 1.0));
	float d = paper_hash(i + vec2(1.0, 1.0));
	return mix(mix(a, b, f.x), mix(c, d, f.x), f.y);
}

// four octaves of it: the blotches of an old sheet, not its grain
float paper_fbm(vec2 p) {
	float s = 0.0, a = 0.5;
	// each octave turned: their lattices no longer line up into a grid
	mat2 turn = mat2(vec2(0.80, 0.60), vec2(-0.60, 0.80));
	for (int i = 0; i < 4; i++) { s += a * paper_noise(p); p = turn * p * 2.03 + vec2(17.3, 9.1); a *= 0.5; }
	return s / 0.9375;
}
