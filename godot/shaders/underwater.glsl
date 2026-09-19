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

/* SOUS L'EAU — voir UnderwaterEffect.cs.
 *
 * Deux choses, et une seule passe.
 *
 * L'EAU QUI MANGE LA LUMIÈRE. Sous la surface, tout est vu à travers des mètres
 * d'eau : l'extinction est PAR CANAL — le rouge parti en quelques mètres, le
 * bleu portant trois fois plus loin —, ce qui fait tout virer au vert puis au
 * bleu puis à rien, au lieu de griser comme ferait une brume. Le fond de l'image
 * (le ciel, que l'œil ne devrait plus voir) est traité comme de l'eau très
 * lointaine : il disparaît de lui-même.
 *
 * LES RAIS QUI FILTRENT ENTRE LES VAGUES. Le soleil traverse une surface qui
 * n'est pas plane : elle le concentre en nappes qui descendent, s'écartent un
 * peu et s'éteignent avec la profondeur. On les intègre le long du rayon de
 * vue : à chaque pas, on remonte jusqu'à la surface DANS LA DIRECTION DU SOLEIL
 * et on lit là-haut une figure de caustiques qui dérive ; ce qui est lu est
 * ajouté, atténué par la profondeur du point et par l'eau déjà traversée. C'est
 * donc bien un volume éclairé, et non un dégradé posé sur l'image : les rais
 * passent devant et derrière ce qui est dans l'eau, et se coupent sur ce qui est
 * opaque.
 */

layout(location = 0) in vec2 uv;
layout(location = 0) out vec4 frag;

layout(set = 0, binding = 0) uniform sampler2D src_color;   // rgb, et la profondeur de vue en alpha
layout(set = 0, binding = 1) uniform sampler2D src_depth;
layout(set = 0, binding = 2, std140) uniform Params {
	mat4 inv_proj;    // écran -> vue
	mat4 inv_view;    // vue -> monde
	vec4 sun;         // xyz : le soleil, monde ; w : sa force
	vec4 water;       // rgb : la couleur de l'eau profonde ; a : le temps
	vec4 misc;        // x : la mer sous l'œil (hauteur) ; y : force des rais ; z : portée ; w : densité
} p;

float hash(vec2 q) { return fract(sin(dot(q, vec2(127.1, 311.7))) * 43758.5453); }

float noise(vec2 q) {
	vec2 i = floor(q), f = fract(q);
	f = f * f * (3.0 - 2.0 * f);
	return mix(mix(hash(i), hash(i + vec2(1, 0)), f.x),
	           mix(hash(i + vec2(0, 1)), hash(i + vec2(1, 1)), f.x), f.y);
}

/* Les caustiques d'une surface agitée, vues d'en dessous : des nappes claires
   séparées par du sombre. Deux couches qui dérivent l'une contre l'autre, prises
   en crêtes (1 − |2n − 1|) pour que ce soient des LIGNES et non des taches. */
float caustics(vec2 q, float t) {
	float a = noise(q * 0.10 + vec2(t * 0.05, -t * 0.03));
	float b = noise(q * 0.23 - vec2(t * 0.02, t * 0.06));
	float r1 = 1.0 - abs(2.0 * a - 1.0);
	float r2 = 1.0 - abs(2.0 * b - 1.0);
	return pow(r1, 3.0) * 0.7 + pow(r2, 4.0) * 0.5;
}

void main() {
	vec4 src = texture(src_color, uv);
	float view_z = src.a;                       // la profondeur de vue, en mètres, rangée par la copie

	// le rayon de vue, en monde
	vec4 h = p.inv_proj * vec4(uv * 2.0 - 1.0, 1.0, 1.0);
	vec3 view_dir = normalize(h.xyz / h.w);
	vec3 dir = normalize((p.inv_view * vec4(view_dir, 0.0)).xyz);
	vec3 eye = p.inv_view[3].xyz;

	// le fond de l'image est de l'eau très lointaine : l'œil ne doit plus voir le ciel
	float far = 400.0;
	float dist = view_z > far * 0.5 ? p.misc.z * 2.0 : view_z / max(-view_dir.z, 1e-3);

	// --- l'eau mange la lumière : extinction par canal ---
	vec3 k = vec3(0.34, 0.13, 0.085) * p.misc.w;
	vec3 through = exp(-k * dist);
	vec3 col = mix(p.water.rgb, src.rgb, through);

	// --- les rais qui descendent ---
	float reach = min(dist, p.misc.z);
	vec3 sun = normalize(p.sun.xyz);
	float up_sun = max(sun.y, 0.08);
	float shafts = 0.0;
	const int N = 24;
	float step_m = reach / float(N);
	// un décalage par pixel, pour que les pas ne se lisent pas en tranches
	float jitter = hash(uv * 1024.0) * step_m;
	for (int i = 0; i < N; i++) {
		vec3 P = eye + dir * (jitter + step_m * float(i));
		float below = p.misc.x - P.y;                     // de combien ce point est sous la mer
		if (below <= 0.0) continue;
		// remonter jusqu'à la surface DANS LA DIRECTION DU SOLEIL : là où ce rai est né
		vec2 q = P.xz + sun.xz * (below / up_sun);
		float lit = caustics(q, p.water.a);
		// ce qui reste de lumière à cette profondeur, et l'eau déjà traversée pour la voir
		shafts += lit * exp(-below * 0.045) * step_m;
	}
	shafts *= p.misc.y * p.sun.w * up_sun;
	// les rais sont de la lumière du soleil, bleuie par l'eau qu'elle a traversée
	col += vec3(0.55, 0.85, 1.0) * shafts * 0.0022;

	frag = vec4(col, 1.0);
}
