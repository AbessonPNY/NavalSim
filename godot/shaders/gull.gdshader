/* UNE MOUETTE, ET TOUT SON VOL DANS LE VERTEX SHADER.
 *
 * Le portage de js/gulls.js, avec la même arithmétique et un seul changement :
 * la page tient chaque oiseau dans trois maillages qu'elle déplace et tourne à
 * la main, ici les douze sont un MultiMesh dont les instances ne bougent JAMAIS.
 * Chacune porte quatre nombres — sa phase, son rayon, son altitude, sa vitesse
 * angulaire — et le shader en tire, à chaque image, où elle est, où elle
 * regarde, de combien elle s'incline et où en sont ses ailes. Douze oiseaux
 * coûtent un appel de dessin et pas une ligne de C#.
 *
 * CE QUI FAIT L'OISEAU, ce n'est ni sa forme ni sa couleur, c'est trois choses
 * qu'on ne peut pas ôter sans qu'il redevienne un bout de carton :
 *
 *   — le CERCLE, lent, et l'oiseau qui s'incline DEDANS. Une aile plus haute que
 *     l'autre du bon côté, et la chose vole ; sans inclinaison, elle glisse sur
 *     un anneau ;
 *   — les SALVES : une mouette qui bat sans arrêt paraît mécanique, ce sont les
 *     plans qui lui donnent du poids ;
 *   — le battement DESCENDANT plus vif que le remontant, la récupération étant
 *     la moitié lente.
 *
 * Le reste du bandeau — où le cercle est posé, et quand les suiveuses rentrent —
 * est la règle partagée avec la page (core/Gulls.cs).
 */
shader_type spatial;
render_mode cull_disabled;

uniform float u_time;
// l'envergure du modèle, en mètres : sert à savoir où commence l'aile
uniform float u_gull_span = 2.2;
// les battements par seconde, et la fréquence des salves
uniform float u_gull_beat = 2.9;
uniform float u_gull_gust = 0.077;

varying vec3 v_col;

// un nombre au hasard, stable, tiré de la phase de l'oiseau
float grain(float s, float k) { return fract(sin(s * 43758.5 + k * 127.1) * 12345.678); }

void vertex() {
	vec4 c = INSTANCE_CUSTOM;
	float phase = c.x * 6.2831853;
	float R = c.y;              // le rayon de son cercle, en mètres
	float alt = c.z;            // son altitude
	float omega = c.w;          // sa vitesse angulaire, signée : le sens du virage

	float a = phase + omega * u_time;
	// le bercement lent, qui empêche le bandeau d'être un disque
	float bob = 0.9 + 1.7 * grain(c.x, 3.0);
	vec3 centre = vec3(cos(a) * R, alt + sin(u_time * 0.31 + phase) * bob, sin(a) * R);

	/* LES AILES. La page tourne chaque aile autour de l'axe avant-arrière de
	   l'oiseau ; ici c'est le même angle, appliqué au sommet selon le bord
	   auquel il appartient — d'où le signe. La rampe le retire au corps et à la
	   queue, qui ne battent pas : sans elle, le corps tremblerait au rythme des
	   ailes. */
	float gate = max(0.0, sin(u_time * u_gull_gust * 6.2831853 + grain(c.x, 7.0) * 6.28));
	float drive = pow(gate, 0.6);
	float beat = sin(u_time * u_gull_beat * 6.2831853 + grain(c.x, 11.0) * 6.28);
	float f = (beat > 0.0 ? beat * 0.62 : beat) * drive * 0.85 + 0.06;
	float aile = f * sign(VERTEX.x) * smoothstep(0.02, 0.06, abs(VERTEX.x));

	vec3 p = VERTEX;
	float cs = cos(aile), sn = sin(aile);
	p = vec3(p.x * cs - p.y * sn, p.x * sn + p.y * cs, p.z);

	/* SON CAP est la tangente à son cercle, et elle s'incline DEDANS : le seul
	   indice qui sépare un oiseau en vol d'une découpe qui tourne en rond. */
	float roulis = omega > 0.0 ? 0.30 : -0.30;
	float rc = cos(roulis), rs = sin(roulis);
	p = vec3(p.x * rc - p.y * rs, p.x * rs + p.y * rc, p.z);

	float lacet = atan(-sin(a) * omega, cos(a) * omega);
	float lc = cos(lacet), ls = sin(lacet);
	p = vec3(p.x * lc + p.z * ls, p.y, -p.x * ls + p.z * lc);

	VERTEX = centre + p;

	// la normale suit les deux tours, sinon l'oiseau s'éclaire comme s'il était droit
	vec3 n = NORMAL;
	n = vec3(n.x * cs - n.y * sn, n.x * sn + n.y * cs, n.z);
	n = vec3(n.x * rc - n.y * rs, n.x * rs + n.y * rc, n.z);
	NORMAL = vec3(n.x * lc + n.z * ls, n.y, -n.x * ls + n.z * lc);

	v_col = COLOR.rgb;
}

void fragment() {
	/* Le manteau gris qui court vers des pointes noires est dans les SOMMETS :
	   c'est le seul marquage qui survive à trois pixels de haut, et il ne coûte
	   rien — une texture ne serait de toute façon pas chargeable par la page. */
	ALBEDO = v_col;
	ROUGHNESS = 0.82;
	METALLIC = 0.0;
}
