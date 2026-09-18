#[compute]
#version 450

// L'image du rendu, recopiée dans le tampon que lit l'effet, AVEC la profondeur
// de vue de chaque pixel dans le canal alpha : un effet qui lit les deux n'a
// plus qu'une lecture par échantillon. Voir ScreenEffect.cs — Godot ne donne à
// l'image ni le droit d'être copiée ni celui d'être écrite par un calcul, mais
// bien d'être lue et d'être la cible d'un tracé.
//
// Les paramètres de chaque effet COMMENCENT par l'inverse de la projection : ce
// calcul les lit tels quels.

layout(local_size_x = 8, local_size_y = 8, local_size_z = 1) in;

layout(set = 0, binding = 0) uniform sampler2D src_color;
layout(rgba16f, set = 0, binding = 1) uniform restrict writeonly image2D dst;
layout(set = 0, binding = 2) uniform sampler2D src_depth;
layout(set = 0, binding = 3, std140) uniform Params {
	mat4 inv_proj;
} p;

void main() {
	ivec2 px = ivec2(gl_GlobalInvocationID.xy);
	ivec2 size = imageSize(dst);
	if (px.x >= size.x || px.y >= size.y) return;
	vec2 uv = (vec2(px) + 0.5) / vec2(size);
	vec4 v = p.inv_proj * vec4(uv * 2.0 - 1.0, texelFetch(src_depth, px, 0).r, 1.0);
	// le ciel est à l'infini (profondeur 0, inversée) : borné sous le plafond du demi-flottant
	float z = v.w > 1e-6 ? min(-v.z / v.w, 60000.0) : 60000.0;
	imageStore(dst, px, vec4(texelFetch(src_color, px, 0).rgb, z));
}