#[vertex]
#version 450

// un triangle qui couvre l'écran, sans tampon de sommets
layout(location = 0) out vec2 uv;

void main() {
	vec2 p = vec2((gl_VertexIndex << 1) & 2, gl_VertexIndex & 2);
	uv = p;
	gl_Position = vec4(p * 2.0 - 1.0, 0.0, 1.0);
}

#[fragment]
#version 450

// LE FLOU DE MOUVEMENT DE LA CAMÉRA — voir MotionBlurEffect.cs.
//
// Chaque pixel retrouve où il était dans le monde (par la profondeur), où ce
// point se trouvait à l'écran à l'image précédente (par la caméra d'alors), et
// l'image est moyennée le long de ce trajet, raccourci à la part que l'obturateur
// laisse ouverte. Une caméra qui suit un navire fait ainsi défiler la mer en flou
// et garde la coque nette, puisque la coque bouge avec elle.

layout(location = 0) in vec2 uv;
layout(location = 0) out vec4 frag;

layout(set = 0, binding = 0) uniform sampler2D src_color;
layout(set = 0, binding = 1) uniform sampler2D src_depth;
layout(set = 0, binding = 2, std140) uniform Params {
	mat4 inv_proj;   // écran -> vue, image courante (projection corrigée Vulkan)
	mat4 reproj;     // vue courante -> écran de l'image précédente
	vec4 misc;       // x : part d'obturateur ; y : échantillons ; z : longueur max (fraction d'écran)
} p;

void main() {
	float d = texture(src_depth, uv).r;
	vec4 v = p.inv_proj * vec4(uv * 2.0 - 1.0, d, 1.0);
	v /= v.w;

	vec4 prev = p.reproj * vec4(v.xyz, 1.0);
	vec2 puv = (prev.xy / prev.w) * 0.5 + 0.5;
	vec2 vel = (uv - puv) * p.misc.x;
	// borné : un changement de vue ou de navire ne doit pas faire une traînée d'un bord à l'autre
	float len = length(vel);
	if (len > p.misc.z) vel *= p.misc.z / len;

	int n = int(p.misc.y);
	vec4 acc = vec4(0.0);
	for (int i = 0; i < n; i++) {
		float t = (float(i) + 0.5) / float(n) - 0.5;
		acc += texture(src_color, uv + vel * t);
	}
	frag = acc / float(n);
}