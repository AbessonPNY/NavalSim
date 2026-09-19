/* L'ÉCLAIR QUI DESCEND — le dessin de lightning.js, peint ici par le shader.
 *
 * La page le peint sur un canvas, comme Arnaud l'avait esquissé : de larges
 * zigzags qui descendent avec crochets et fourches, un halo violet autour d'un
 * cœur blanc. Un tube le long d'une ligne brisée avait été essayé trois fois et
 * se lisait chaque fois comme un laser. Ici la même ligne brisée, tirée au hasard
 * à chaque coup par LightningNode, arrive en uniformes, et chaque pixel se peint
 * par sa distance à elle, en reprenant les trois traits de la page dans le même
 * ordre : halo, couleur, cœur — chacun avec son ombre portée floue.
 *
 * Sur une bande de 150 × 300 m, toujours tournée vers l'œil autour de son axe et
 * épinglée entre le nuage et la tête de mât : testée en profondeur, elle tombe
 * exactement sur la pomme du mât. Elle ÉMET : ni lumière qui la suive, ni brume.
 */
shader_type spatial;
render_mode unshaded, blend_add, depth_draw_never, cull_disabled, shadows_disabled, fog_disabled;

#include "res://shaders/sky.gdshaderinc"

const vec2 PIC = vec2(512.0, 1024.0);      // le dessin de la page, en pixels
const int MAXP = 48;

uniform vec3 u_top;
uniform vec3 u_bottom;
uniform float u_width = 150.0;
uniform float u_opacity = 0.0;
uniform vec2 u_pts[48];
uniform int u_n = 0;
uniform vec2 u_fork[8];                    // deux fourches de quatre points

void vertex() {
	vec3 axis = normalize(u_top - u_bottom);
	vec3 side = cross(axis, CAMERA_POSITION_WORLD - u_bottom);
	side = dot(side, side) < 1e-8 ? vec3(1.0, 0.0, 0.0) : normalize(side);
	// UV.y 0 en haut (le nuage), 1 en bas (la tête de mât), comme le canvas
	VERTEX = mix(u_top, u_bottom, UV.y) + side * ((UV.x * 2.0 - 1.0) * u_width * 0.5);
}

float seg(vec2 p, vec2 a, vec2 b) {
	vec2 ab = b - a;
	float h = clamp(dot(p - a, ab) / max(dot(ab, ab), 1e-6), 0.0, 1.0);
	return length(p - a - ab * h);
}

// un trait de la page, posé « par-dessus » : d'abord son ombre floue, puis lui
void stroke(inout vec4 acc, float d, float w, vec3 col, float a, float blur) {
	float sigma = max(blur * 0.5, 0.5);
	float sh = exp(-pow(max(0.0, d - w * 0.5), 2.0) / (2.0 * sigma * sigma)) * a;
	vec3 shc = vec3(150.0, 120.0, 255.0) / 255.0;
	acc.rgb = shc * sh + acc.rgb * (1.0 - sh);
	acc.a = sh + acc.a * (1.0 - sh);
	float cv = clamp(w * 0.5 - d + 0.5, 0.0, 1.0) * a;
	acc.rgb = col * cv + acc.rgb * (1.0 - cv);
	acc.a = cv + acc.a * (1.0 - cv);
}

void fragment() {
	vec2 p = UV * PIC;
	float dm = 1e9;
	for (int i = 0; i < MAXP - 1; i++) {
		if (i + 1 >= u_n) break;
		dm = min(dm, seg(p, u_pts[i], u_pts[i + 1]));
	}
	float f0 = 1e9, f1 = 1e9;
	for (int i = 0; i < 3; i++) {
		f0 = min(f0, seg(p, u_fork[i], u_fork[i + 1]));
		f1 = min(f1, seg(p, u_fork[4 + i], u_fork[4 + i + 1]));
	}
	vec4 acc = vec4(0.0);
	// les fourches d'abord, puis le tronc : halo, couleur, cœur blanc
	stroke(acc, f0, 10.0, vec3(120.0, 90.0, 255.0) / 255.0, 0.35, 18.0);
	stroke(acc, f0, 3.0, vec3(200.0, 190.0, 255.0) / 255.0, 0.9, 6.0);
	stroke(acc, f1, 10.0, vec3(120.0, 90.0, 255.0) / 255.0, 0.35, 18.0);
	stroke(acc, f1, 3.0, vec3(200.0, 190.0, 255.0) / 255.0, 0.9, 6.0);
	stroke(acc, dm, 26.0, vec3(110.0, 80.0, 255.0) / 255.0, 0.30, 30.0);
	stroke(acc, dm, 11.0, vec3(160.0, 130.0, 255.0) / 255.0, 0.85, 14.0);
	stroke(acc, dm, 4.5, vec3(1.0), 1.0, 4.0);
	if (acc.a < 0.002) discard;
	// une image sRGB que three décode avant de l'ajouter : la même chose ici
	ALBEDO = naval_display(acc.rgb / acc.a);
	ALPHA = acc.a * u_opacity;
}
