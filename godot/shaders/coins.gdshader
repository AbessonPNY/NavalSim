/* DES PIÈCES D'OR QUI COULENT. Un disque épais qui bascule — CoinNode lui donne
 * sa rotation, autour de son diamètre : de face, le champ de la pièce ; de
 * travers, sa tranche. C'est ce passage qui fait l'éclat, comme une pièce qui
 * tourne sur elle-même en tombant.
 *
 * Métallique et éclairée comme le reste : l'or n'a pas de couleur à lui, il
 * renvoie celle du ciel et du soleil. Sous l'eau, la passe sous-marine lui mange
 * son rouge à mesure qu'elle descend — une pièce lointaine s'éteint d'elle-même,
 * sans qu'il y ait rien à écrire ici pour cela.
 *
 * L'axe de la pièce est +y en repère local : c'est celui du cylindre de repli, et
 * un .glb d'écu doit être posé à plat de la même façon.
 */
shader_type spatial;
render_mode blend_mix, shadows_disabled, specular_schlick_ggx;

uniform vec3 u_gold : source_color = vec3(0.83, 0.62, 0.22);
uniform vec3 u_edge : source_color = vec3(0.42, 0.30, 0.10);
/* L'ÉCLAT EST PLUS ROUGE QUE L'OR. L'eau éteint le rouge la première : une
   émission couleur or ressort vert citron à quatre mètres. On la part donc trop
   chaude, pour qu'elle arrive dorée à l'œil — c'est la correction d'un
   plongeur qui remonte ses photos, faite à la source. */
uniform vec3 u_glow : source_color = vec3(1.0, 0.44, 0.06);
uniform sampler2D u_tex : source_color, filter_linear_mipmap;
uniform bool u_textured = false;   // vrai si le .glb de l'écu porte sa propre gravure

varying float v_fade;
varying float v_face;
varying float v_flat;

void vertex() {
	v_fade = INSTANCE_CUSTOM.y;
	// la tranche ou le champ : la normale LOCALE le dit, avant toute rotation
	v_flat = abs(NORMAL.y);
	// de combien sa face regarde l'œil : c'est l'axe y de l'instance
	vec3 n = normalize((MODEL_MATRIX * vec4(0.0, 1.0, 0.0, 0.0)).xyz);
	vec3 at = (MODEL_MATRIX * vec4(0.0, 0.0, 0.0, 1.0)).xyz;
	v_face = abs(dot(n, normalize(CAMERA_POSITION_WORLD - at)));
}

void fragment() {
	// le listel de la tranche, un rien plus sombre que le champ de la pièce
	vec3 gold = u_textured ? texture(u_tex, UV).rgb * u_gold * 1.6 : u_gold;
	ALBEDO = mix(u_edge, gold, smoothstep(0.35, 0.75, v_flat));
	METALLIC = 1.0;
	ROUGHNESS = 0.28;
	/* L'ÉCLAT DE LA BASCULE. Sous l'eau la lumière est rare et l'absorption mange
	   le rouge : un or seulement métallique y devient un jeton verdâtre. Quand sa
	   face passe à plat vers l'œil, elle renvoie d'un coup tout ce qu'elle a de
	   ciel — c'est cet éclair-là qui dit « or » plutôt que « caillou », et il
	   s'éteint de lui-même dès qu'elle se remet de profil. La tranche, elle, ne
	   le jette pas : c'est le champ qui brille. */
	/* Un SOCLE, et l'éclair par-dessus : à cinq mètres une pièce ne fait plus que
	   trois pixels, et l'absorption lui a déjà pris son rouge — sans ce socle il
	   ne reste qu'un grain verdâtre. C'est ce que montre une photo de trésor qui
	   coule : on ne voit pas les pièces, on voit leurs éclats. */
	EMISSION = u_glow * (0.30 + 2.6 * pow(v_face, 4.0)) * v_flat;
	ALPHA = v_fade;
}
