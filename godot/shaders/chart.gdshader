/* LA CARTE DE LA CHAMBRE — vierge, et qui ne se remplit que de ce qu'on est
 * allé voir.
 *
 * Deux images se rencontrent ici : le RELIEF, dessiné une fois pour toute la
 * région, et le VOILE, que la route du navire perce à mesure. Là où le voile
 * tient, on ne voit que le papier ; là où il est percé, la côte paraît.
 *
 * Le bord de la découverte n'est pas net : un cartographe ne trace pas une
 * frontière là où son regard s'arrête. Le voile s'amincit donc sur une marge,
 * et il reste un peu de brûlé au bord — c'est ce qui fait lire la carte comme
 * une carte en cours plutôt que comme une image tronquée.
 */
shader_type canvas_item;

uniform sampler2D u_mask : filter_linear, repeat_disable;   // 1 : vu · 0 : encore blanc
uniform vec3 u_paper : source_color = vec3(0.88, 0.82, 0.68);
uniform vec3 u_burn : source_color = vec3(0.62, 0.50, 0.32);
uniform float u_grain = 0.055;

// un grain de papier, pour que le vierge ne soit pas un aplat mort
float noise(vec2 p) {
	vec2 i = floor(p), f = fract(p);
	f = f * f * (3.0 - 2.0 * f);
	float a = fract(sin(dot(i, vec2(127.1, 311.7))) * 43758.5);
	float b = fract(sin(dot(i + vec2(1.0, 0.0), vec2(127.1, 311.7))) * 43758.5);
	float c = fract(sin(dot(i + vec2(0.0, 1.0), vec2(127.1, 311.7))) * 43758.5);
	float d = fract(sin(dot(i + vec2(1.0, 1.0), vec2(127.1, 311.7))) * 43758.5);
	return mix(mix(a, b, f.x), mix(c, d, f.x), f.y);
}

void fragment() {
	vec4 land = texture(TEXTURE, UV);
	float seen = texture(u_mask, UV).r;

	vec3 paper = u_paper * (1.0 - u_grain + u_grain * 2.0 * noise(UV * 420.0));
	// le papier vieillit vers ses bords, comme une feuille qu'on manipule
	vec2 d = abs(UV - 0.5) * 2.0;
	paper = mix(paper, u_burn, smoothstep(0.75, 1.05, max(d.x, d.y)) * 0.55);

	/* La côte d'abord posée sur le papier, puis le voile par-dessus : dans cet
	   ordre, une terre à peine entrevue transparaît au lieu de disparaître. */
	vec3 col = mix(paper, land.rgb, land.a);
	float veil = 1.0 - smoothstep(0.18, 0.62, seen);
	col = mix(col, paper, veil);
	// le liseré brûlé au bord du connu
	col = mix(col, u_burn, smoothstep(0.62, 0.30, seen) * smoothstep(0.10, 0.28, seen) * 0.45);

	COLOR = vec4(col, 1.0);
}
