/* SINE-BASED HASHES, written once.
 *
 * The small fract(sin(...)) hashes that several shaders each carried a copy of.
 * The naval_hash13 family (sky.gdshaderinc, no sine) is not here: it draws
 * different numbers, and folding one into the other would change the image.
 * Guarded, because a hull material reaches this file through both the snow
 * and the scars.
 */

#ifndef NAVAL_HASH_INC
#define NAVAL_HASH_INC

/* A 3D sine hash, the hull's: the scars' layout (ship_scar.gdshaderinc) and the
   snow's lattice (ship_snow.gdshaderinc). Both meshes are lit by the same hull
   material, so one copy was always going to drift from the other. */
float naval_hash_sin3(vec3 p) {
	return fract(sin(dot(p, vec3(12.9898, 78.233, 37.719))) * 43758.5453);
}

/* A 2D sine hash: the water beads on a lens (beads.gdshaderinc) and the boil
   of a sinking hull in the foam field (foam_field.gdshader). The compositor's
   underwater pass (underwater.glsl) carries the same one, but it is compiled
   by RenderingDevice, not by this preprocessor, and cannot include it. */
float naval_hash_sin2(vec2 p) {
	return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453);
}

/* A stable random number drawn from an instance's phase `s`, with a salt `k`
   for each independent draw: the fish of a shoal and the gulls of a flock. */
float naval_rand(float s, float k) {
	return fract(sin(s * 43758.5 + k * 127.1) * 12345.678);
}

#endif
