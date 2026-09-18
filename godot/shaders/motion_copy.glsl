#[compute]
#version 450

// L'image du rendu, recopiée dans le tampon que le flou lira : Godot ne lui
// donne pas le droit d'être copiée ni écrite par un calcul, mais bien d'être lue
// et d'être la cible d'un tracé. Voir MotionBlurEffect.cs.

layout(local_size_x = 8, local_size_y = 8, local_size_z = 1) in;

layout(set = 0, binding = 0) uniform sampler2D src_color;
layout(rgba16f, set = 0, binding = 1) uniform restrict writeonly image2D dst;

void main() {
	ivec2 px = ivec2(gl_GlobalInvocationID.xy);
	ivec2 size = imageSize(dst);
	if (px.x >= size.x || px.y >= size.y) return;
	imageStore(dst, px, texelFetch(src_color, px, 0));
}