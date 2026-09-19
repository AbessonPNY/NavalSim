/* DES GOUTTES SUR L'OBJECTIF, au sortir de l'eau.
 *
 * Une vitre mouillée ne fait pas une tache floue : elle porte des perles qui
 * DÉVIENT ce qui est derrière, chacune une petite lentille, et le reste de
 * l'image demeure net. On lit donc l'image déjà rendue (la texture d'écran, le
 * même tour que l'oculaire de la lunette) et on la relit décalée par la pente de
 * chaque perle.
 *
 * Trois choses la rendent vivante : un SEMIS de perles immobiles, réparties par
 * cases mais placées au hasard dans leur case, de tailles différentes ; quelques
 * grosses gouttes qui GLISSENT vers le bas en laissant une traînée plus fine,
 * chacune à son rythme ; et un voile mince sur tout l'objectif, qui sèche le
 * premier. Tout s'efface avec `u_wet`, que la caméra baisse en sortant de l'eau.
 *
 * Les gouttes sont posées dans un repère CORRIGÉ DE L'ASPECT : sans cela, elles
 * sortent ovales sur un écran large.
 */
shader_type canvas_item;
render_mode unshaded;

uniform sampler2D u_scene : hint_screen_texture, filter_linear, repeat_disable;
uniform float u_wet = 0.0;      // de 1 (sortie de l'eau) à 0 (sec)
uniform float u_time = 0.0;
uniform float u_aspect = 1.777;

float hash1(vec2 p) { return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453); }
vec2 hash2(vec2 p) { return vec2(hash1(p), hash1(p + 17.3)); }

/* Le semis : une perle par case, placée au hasard dedans, et les cases voisines
   regardées aussi pour qu'une perle déborde de la sienne. Rend la PENTE de la
   perle (sa normale projetée) et sa couverture. */
vec3 beads(vec2 p, float scale, float seed, float life) {
	vec2 q = p * scale;
	vec2 i = floor(q), f = fract(q);
	vec3 best = vec3(0.0);
	for (int y = -1; y <= 1; y++)
		for (int x = -1; x <= 1; x++) {
			vec2 o = vec2(float(x), float(y));
			vec2 h = hash2(i + o + seed);
			/* DEUX CASES SUR TROIS RESTENT SÈCHES. Moins de perles, mais chacune
			   plus grosse et qui plie l'image plus fort, se lit comme de l'eau ;
			   un semis fin et serré se lit comme du verre dépoli. */
			if (hash1(i + o + seed + 31.7) < 0.68) continue;
			// chaque perle a son heure : les plus petites sèchent les premières
			float r = (0.13 + 0.26 * h.x) * smoothstep(0.0, 0.45 + 0.4 * h.y, life);
			if (r <= 0.001) continue;
			vec2 c = o + vec2(h.y, hash1(i + o + seed + 5.1));
			vec2 d = f - c;
			float dist = length(d);
			if (dist > r) continue;
			// une calotte : plate au centre, très penchée au bord
			float k = sqrt(max(0.0, 1.0 - (dist * dist) / (r * r)));
			vec3 got = vec3(d / max(dist, 1e-4) * (1.0 - k), k);
			if (got.z > best.z) best = got;
		}
	return best;
}

void fragment() {
	if (u_wet <= 0.001) {
		COLOR = texture(u_scene, SCREEN_UV);
	} else {
		vec2 p = vec2(SCREEN_UV.x * u_aspect, SCREEN_UV.y);

		// le semis, deux tailles : de grosses perles et une poussière de fines
		vec3 b1 = beads(p, 10.0, 0.0, u_wet);
		vec3 b2 = beads(p, 24.0, 3.7, u_wet * 0.8);

		/* Les gouttes qui GLISSENT. Chacune descend à son allure, ralentit et
		   repart (une goutte accroche, puis file), et laisse derrière elle une
		   traînée plus mince qui sèche vite. */
		vec3 run = vec3(0.0);
		for (int i = 0; i < 4; i++) {
			float fi = float(i);
			vec2 h = hash2(vec2(fi, 7.0));
			float speed = 0.05 + 0.10 * h.x;
			// son avance, avec des reprises : le sinus la fait accrocher puis filer
			float y = fract(h.y + u_time * speed + 0.03 * sin(u_time * (0.7 + h.x) + fi));
			vec2 c = vec2(h.x * u_aspect, y);
			vec2 d = p - c;
			float r = (0.055 + 0.03 * h.y) * u_wet;
			float dist = length(d);
			if (dist < r) {
				float k = sqrt(max(0.0, 1.0 - (dist * dist) / (r * r)));
				vec3 got = vec3(d / max(dist, 1e-4) * (1.0 - k), k * 1.2);
				if (got.z > run.z) run = got;
			}
			// la traînée : au-dessus d'elle, mince, et qui s'efface vers le haut
			float trail = smoothstep(r * 0.55, 0.0, abs(d.x))
			            * smoothstep(0.0, 0.25, c.y - p.y) * smoothstep(0.30, 0.0, c.y - p.y);
			if (trail > run.z * 0.4) run.xy += vec2(sign(d.x) * trail * 0.5, 0.0);
		}

		// la déviation d'une vraie goutte est FORTE : une perle d'un demi-centimètre
		// porte l'image de plusieurs degrés de champ
		vec2 slope = b1.xy * 0.085 + b2.xy * 0.030 + run.xy * 0.12;
		float cover = max(max(b1.z, b2.z * 0.7), run.z);

		// la perle est une lentille : elle regarde l'image AILLEURS, et un peu à côté
		vec2 uv = SCREEN_UV + slope * u_wet;
		vec3 col = texture(u_scene, clamp(uv, vec2(0.001), vec2(0.999))).rgb;

		/* Le voile d'eau entre les perles : il ne dévie rien, il DÉLAVE — un fin
		   film qui diffuse. Il sèche le premier, donc il ne reste vite que les
		   perles. */
		float film = u_wet * u_wet * 0.16;
		vec3 blur = (texture(u_scene, SCREEN_UV + vec2(0.004, 0.0)).rgb
		           + texture(u_scene, SCREEN_UV - vec2(0.004, 0.0)).rgb
		           + texture(u_scene, SCREEN_UV + vec2(0.0, 0.004)).rgb
		           + texture(u_scene, SCREEN_UV - vec2(0.0, 0.004)).rgb) * 0.25;
		col = mix(col, blur, film);

		// le bord d'une perle prend la lumière, et son cœur assombrit un peu
		col += vec3(0.25, 0.32, 0.38) * pow(cover, 3.0) * 0.22 * u_wet;
		col *= 1.0 - 0.06 * cover * u_wet;
		COLOR = vec4(col, 1.0);
	}
}
