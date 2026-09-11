/* LA BRUME, AUSSI COMME UNE SEULE FONCTION PARTAGÉE.
 *
 * Tout ce que l'œil voit à travers l'air doit employer CECI, et non un second
 * modèle de brouillard : la mer et le navire doivent s'assombrir au MÊME rythme,
 * sinon le bâtiment reste net contre une mer délavée et l'illusion s'effondre.
 *
 * C'est du Beer-Lambert sur la profondeur traversée, et non une décroissance en
 * carré de la distance — les deux ne peuvent s'accorder qu'à une seule portée,
 * ce qui est exactement pourquoi le navire restait net au loin.
 *
 * Pendant Godot de Naval.HAZE_GLSL.
 */

uniform float u_haze = 0.00085;    // extinction par mètre au niveau de la mer
uniform float u_haze_h = 150.0;    // hauteur d'échelle de la couche (m)

float haze_along(vec3 from, vec3 to) {
	vec3 d = to - from;
	float dist = length(d);
	if (dist < 0.001) return 0.0;
	float y0 = max(from.y, 0.0);
	float y1 = max(to.y, 0.0);
	float dy = y1 - y0;
	float depth;
	if (abs(dy) < 0.01) {
		depth = exp(-y0 / u_haze_h) * dist;
	} else {
		// l'intégrale de exp(-y/H) le long du segment, en forme fermée
		depth = dist * (u_haze_h / dy) * (exp(-y0 / u_haze_h) - exp(-y1 / u_haze_h));
	}
	return 1.0 - exp(-u_haze * abs(depth));
}
