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

/* LE REFLET DE LENTILLE — voir LensFlareEffect.cs.
 *
 * Ce que fait un objectif, et non un œil, quand le soleil entre dans le champ :
 * la lumière se réfléchit d'une lentille à l'autre et revient sur l'image en
 * FANTÔMES, des disques pâles alignés sur la droite qui va du soleil au centre
 * de l'image et au-delà (le centre optique est un axe de symétrie, d'où l'ali-
 * gnement, et les fantômes passent de l'autre côté). Leur forme est celle du
 * diaphragme, leur teinte celle du traitement des verres : ambre, vert d'eau,
 * violet. Autour du soleil même, un voile qui baisse le contraste et les rayons
 * du diaphragme (six lames : six branches).
 *
 * Il n'y a de reflet que si le soleil se VOIT : on sonde l'image sur son disque,
 * et chaque sonde qui tombe sur autre chose que le ciel (une voile, un mât, la
 * terre) l'éteint d'autant. Un mât qui passe devant le soleil fait battre le
 * reflet, comme dans un objectif.
 */

layout(location = 0) in vec2 uv;
layout(location = 0) out vec4 frag;

layout(set = 0, binding = 0) uniform sampler2D src_color;   // rgb, et la profondeur de vue en alpha
layout(set = 0, binding = 1) uniform sampler2D src_depth;
layout(set = 0, binding = 2, std140) uniform Params {
	mat4 inv_proj;
	vec4 sun;     // xy : le soleil sur l'image (0 à 1) ; z : la force du reflet ; w : largeur / hauteur
	vec4 tint;    // rgb : la couleur du soleil ; w : le rayon du disque sondé, en part de la hauteur
} p;


// un fantôme : un disque au bord un peu plus vif (le diaphragme), flou au dehors
float ghost(vec2 d, float r) {
	float x = length(d) / r;
	float disc = smoothstep(1.0, 0.82, x);
	float rim = smoothstep(0.70, 0.95, x) * smoothstep(1.05, 0.95, x);
	return disc * 0.65 + rim * 0.55;
}

void main() {
	vec4 src = texture(src_color, uv);
	vec2 asp = vec2(p.sun.w, 1.0);

	/* --- le soleil se voit-il ? Seize sondes sur son disque ---
	   Le CIEL est ce qui est au plan lointain de la caméra (profondeur 0, inversée) :
	   sa distance se tire de la projection elle-même, plutôt que d'un nombre supposé
	   (on avait cru le ciel « à l'infini », il est au plan lointain). */
	vec4 fz = p.inv_proj * vec4(0.0, 0.0, 0.0, 1.0);
	float sky_z = 0.98 * min(fz.w > 1e-6 ? -fz.z / fz.w : 60000.0, 60000.0);
	float seen = 0.0;
	for (int i = 0; i < 16; i++) {
		float a = float(i) * 2.3999632;
		float rr = p.tint.w * sqrt((float(i) + 0.5) / 16.0);
		vec2 q = p.sun.xy + vec2(cos(a), sin(a)) * rr / asp;
		if (q.x < 0.0 || q.x > 1.0 || q.y < 0.0 || q.y > 1.0) continue;
		seen += texture(src_color, q).a > sky_z ? 1.0 : 0.0;
	}
	float k = p.sun.z * seen / 16.0;
	if (k <= 1e-4) {
		frag = vec4(src.rgb, 1.0);
		return;
	}

	vec3 sun_col = p.tint.rgb;
	vec2 to_c = vec2(0.5) - p.sun.xy;
	vec3 add = vec3(0.0);

	// --- les fantômes, sur l'axe soleil – centre – au-delà ---
	const int G = 6;
	const float at[G] = float[](0.45, 0.78, 1.18, 1.42, 1.75, 2.15);
	const float rad[G] = float[](0.030, 0.012, 0.075, 0.020, 0.055, 0.130);
	const vec3 col[G] = vec3[](vec3(1.00, 0.72, 0.38), vec3(0.55, 1.00, 0.80), vec3(0.62, 0.78, 1.00),
	                           vec3(1.00, 0.90, 0.55), vec3(0.75, 0.55, 1.00), vec3(0.50, 0.85, 0.95));
	const float gain[G] = float[](0.050, 0.090, 0.022, 0.060, 0.026, 0.012);
	for (int i = 0; i < G; i++) {
		vec2 c = p.sun.xy + to_c * at[i];
		add += col[i] * gain[i] * ghost((uv - c) * asp, rad[i]);
	}

	// --- autour du soleil : le voile, et les six branches du diaphragme ---
	vec2 d = (uv - p.sun.xy) * asp;
	float r = length(d);
	float veil = exp(-r * 9.0) * 0.10 + exp(-r * 38.0) * 0.18;
	float th = atan(d.y, d.x);
	float spokes = pow(abs(cos(3.0 * th)), 60.0) * exp(-r * 16.0) * 0.22;
	add += vec3(veil + spokes);

	// --- l'anneau, plus loin : un arc de couleurs, le halo de la lentille frontale ---
	float ring = smoothstep(0.018, 0.0, abs(r - 0.36));
	add += vec3(0.55, 0.75, 1.0) * ring * 0.018 * smoothstep(0.0, 0.6, length(to_c));

	frag = vec4(src.rgb + add * sun_col * k, 1.0);
}
