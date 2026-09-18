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

// LE FLOU ANAMORPHIQUE — voir AnamorphicDofEffect.cs.
//
// Une profondeur de champ dont le cercle de confusion est une ELLIPSE deux fois
// plus haute que large : l'objectif anamorphique comprime l'image en largeur à la
// prise de vue et la redéploie à la projection, et son flou, rond sur la
// pellicule, sort ovale à l'écran.
//
// Rassembler plutôt que projeter : chaque pixel va chercher ses voisins sur
// l'ellipse de son flou, et n'en retient que ceux dont le PROPRE flou l'atteint.
// Ainsi un objet net ne bave pas sur le fond flou, et un premier plan flou
// déborde sur ce qui est derrière lui, comme dans une vraie optique.

layout(location = 0) in vec2 uv;
layout(location = 0) out vec4 frag;

layout(set = 0, binding = 0) uniform sampler2D src_color;   // rgb, et la profondeur de vue en alpha
layout(set = 0, binding = 1) uniform sampler2D src_depth;
layout(set = 0, binding = 2, std140) uniform Params {
	mat4 inv_proj;   // écran -> vue (projection corrigée Vulkan)
	vec4 zone;       // x : net à partir de (0 : aucun flou de près) ; y : flou au-delà (≥ 1e5 : aucun) ;
	                 // z : fondu, en part de la distance ; w : rayon de flou plein, en pixels, VERTICAL
	vec4 misc;       // x : échantillons ; y : largeur / hauteur de l'ellipse ; zw : taille de l'image
} p;


// le rayon du flou, en pixels (vertical), à cette profondeur : la même rampe que
// la profondeur de champ de Godot, linéaire sur le fondu
float coc(float z) {
	float f = p.zone.y < 1e5 ? clamp((z - p.zone.y) / max(1e-3, p.zone.y * p.zone.z), 0.0, 1.0) : 0.0;
	float n = p.zone.x > 0.0 ? clamp((p.zone.x - z) / max(1e-3, p.zone.x * p.zone.z), 0.0, 1.0) : 0.0;
	return max(f, n) * p.zone.w;
}

void main() {
	vec2 px = 1.0 / p.misc.zw;
	vec2 ell = vec2(p.misc.y, 1.0);                 // l'ellipse, en unités du rayon vertical
	vec4 center = texture(src_color, uv);
	float zc = center.a;
	float cc = coc(zc);

	/* JUSQU'OÙ CHERCHER : son propre flou, ou celui d'un voisin assez flou pour
	   l'atteindre — un premier plan flou déborde sur un fond net. Huit sondes sur
	   l'ellipse du flou plein et huit à mi-chemin. */
	float r = cc;
	for (int k = 0; k < 16; k++) {
		float a = float(k) * 0.7853982 + (k >= 8 ? 0.3927 : 0.0);
		float s = k >= 8 ? 0.5 : 1.0;
		vec2 o = vec2(cos(a), sin(a)) * ell * s * p.zone.w;
		float cp = coc(texture(src_color, uv + o * px).a);
		if (cp >= s * p.zone.w) r = max(r, cp);
	}
	if (r < 0.5) { frag = vec4(center.rgb, 1.0); return; }

	// la spirale d'or, tournée à chaque pixel : un grain fin plutôt qu'un motif
	int n = int(p.misc.x);
	float rot = 6.2831853 * fract(52.9829189 * fract(dot(gl_FragCoord.xy, vec2(0.06711056, 0.00583715))));
	vec3 acc = center.rgb;
	float wsum = 1.0;
	for (int i = 0; i < n; i++) {
		float t = sqrt((float(i) + 0.5) / float(n));
		float a = float(i) * 2.3999632 + rot;
		vec2 at = uv + vec2(cos(a), sin(a)) * ell * (t * r) * px;
		vec4 s = texture(src_color, at);
		float zs = s.a;
		float cs = coc(zs);
		// ce qui est DERRIÈRE ne déborde pas plus que le flou de ce pixel : le fond
		// flou ne mange pas le bord d'un objet net placé devant lui
		if (zs > zc) cs = min(cs, cc);
		float w = clamp(cs - t * r + 1.0, 0.0, 1.0);
		acc += s.rgb * w;
		wsum += w;
	}
	frag = vec4(acc / wsum, 1.0);
}
