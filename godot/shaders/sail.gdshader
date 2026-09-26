/* LA TOILE — le matériau `canvas` de ship-model.js et Naval.applySailLight.
 *
 * La toile à voile est mince, et une toile mince s'éclaire À TRAVERS. Le
 * soleil derrière une voile, l'essentiel de ce qu'on voit n'a jamais touché la
 * face avant : c'est passé par le tissage, et la voile paraît plus claire que
 * tout ce qui est éclairé de face, les espars dessinés en barres sombres
 * dessus. Un matériau éclairé ordinaire ne sait pas faire cela — sa face
 * arrière devient simplement noire.
 *
 * Le lobe est celui que la mer emploie déjà pour la lumière qui traverse une
 * crête : le plus fort quand l'œil, la toile et le soleil s'alignent, et
 * seulement là où le soleil est sur la face qu'on NE regarde PAS. La normale est
 * prise brute sur la géométrie, pas celle tournée vers l'œil, si bien qu'une
 * voile vue de l'un ou l'autre bord donne la même réponse.
 *
 * Ce qui reste en émission constante est la lumière que le ciel envoie à
 * travers le tissage de tous les côtés à la fois, qu'aucun lobe ne représente.
 *
 * Un écart d'arithmétique, dit ici plutôt que caché : l'original AJOUTE le lobe
 * après l'encodage sRGB, Godot l'ajoute en linéaire avant. Même lobe, même
 * couleur, même force ; la courbe diffère dans les tons moyens.
 */
shader_type spatial;
render_mode cull_disabled;

#include "res://shaders/ship_haze.gdshaderinc"
#include "res://shaders/ship_snow.gdshaderinc"

uniform vec3 u_canvas : source_color = vec3(0.95, 0.92, 0.86);
uniform vec3 u_emissive : source_color = vec3(0.553, 0.525, 0.435);   // 0x8d866f
uniform float u_emissive_k = 0.12;
uniform sampler2D u_map : source_color, filter_linear_mipmap_anisotropic, repeat_disable;
uniform float u_has_map = 0.0;
uniform vec3 u_sun_col = vec3(1.0, 0.95, 0.86);
uniform float u_translucency = 0.45;   // la part de la lumière reçue derrière qui ressort devant
uniform float u_snow = 0.0;          // la couche de neige sur CE navire : pendante, la toile n'en garde presque pas
/* CE QUI RESTE DE LA TOILE SOUS LES ÉTOILES, de 0 à 1 : la part de sa lumière de
   jour qu'elle garde quand le ciel est au plus noir. À 0 elle disparaît tout à
   fait et la mâture perd sa silhouette ; à 1 elle pend comme un drap éclairé.
   settings.json → night.canvas. */
uniform float u_canvas_floor = 0.05;

/* CE QUE LE FEU A DÉVORÉ DE CETTE VOILE-CI, de 0 (intacte) à 1 (plus rien).
 *
 * « instance » et non « uniform » tout court : les voiles d'une même sorte
 * PARTAGENT leur matière — une seule est créée par sorte, et la régler réglerait
 * toute la voilure d'un coup. Ce qui appartient à UNE voile doit donc passer par
 * son maillage, comme la rotation des bouffées et la taille des embruns. */
instance uniform float u_burn = 0.0;

/* UN GRAIN DOUX, tiré du hachage du ciel : de la valeur interpolée sur une
   grille, ce qui suffit à déchiqueter une lisière. Une lisière droite se lit
   comme un ciseau, et le feu ne coupe pas droit. */
float naval_grain(vec2 q) {
	vec2 i = floor(q), f = fract(q);
	f = f * f * (3.0 - 2.0 * f);
	float a0 = naval_hash13(vec3(i, 0.0));
	float b0 = naval_hash13(vec3(i + vec2(1.0, 0.0), 0.0));
	float c0 = naval_hash13(vec3(i + vec2(0.0, 1.0), 0.0));
	float d0 = naval_hash13(vec3(i + vec2(1.0, 1.0), 0.0));
	return mix(mix(a0, b0, f.x), mix(c0, d0, f.x), f.y);
}

varying vec3 v_world;
varying vec3 v_raw_n;
varying vec3 v_local;

void vertex() {
	v_world = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	v_raw_n = normalize((MODEL_MATRIX * vec4(NORMAL, 0.0)).xyz);
	v_local = VERTEX;
}

void fragment() {
	/* La couleur RESTE et multiplie l'image, au lieu de passer au blanc : c'est
	   le ton de sa toile, qui garde une voile peinte du même blanc cassé que ses
	   voiles unies — un motif sur un matériau blanc est un drap à côté du foc. */
	/* CE QU'IL Y A DE JOUR, tiré de l'horizon qui porte l'heure. Il servait à
	   l'émission seule ; il sert maintenant aussi à la translucidité, et c'est
	   pourquoi il est calculé ici, avant l'un comme l'autre. Une définition,
	   deux usagers — jamais deux fois la même heure lue de deux façons. */
	float ciel = dot(u_horizon, vec3(0.2126, 0.7152, 0.0722));
	float jour = clamp(ciel / 0.85, u_canvas_floor, 1.0);

	vec3 diffuse = u_canvas;
	if (u_has_map > 0.5) diffuse *= texture(u_map, UV).rgb;
	// par sa normale, comme la page : c'est la face qui regarde le ciel qui la reçoit
	diffuse = mix(diffuse, NAVAL_SNOW_COLOR, naval_snow(v_raw_n.y, v_local, u_snow));

	/* ELLE SE CONSUME DU PIED VERS LA TÊTIÈRE, parce que la flamme MONTE : ce qui
	 * brûle d'abord est ce qui pend au-dessus du foyer, et le feu remonte la toile
	 * en laissant une lisière de braise derrière lui.
	 *
	 * Deux parts dans le front : la HAUTEUR, qui donne le sens, et le grain, qui
	 * donne la déchirure. Sans le grain la voile se mangerait par une ligne
	 * horizontale ; sans la hauteur elle se mangerait par plaques, ce qui est de
	 * la moisissure et non du feu. */
	float braise = 0.0;
	if (u_burn > 0.001) {
		float bas = 1.0 - UV.y;                       // 0 à la têtière, 1 au point d'écoute
		float front = bas * 0.62 + naval_grain(UV * vec2(5.0, 7.0)) * 0.38;
		float seuil = 1.0 - u_burn;
		if (front > seuil) discard;                   // ce qui est consumé n'est plus là
		/* LA LISIÈRE : la braise qui ronge, et le roussi devant elle. Le roussi est
		   plus large que la braise — une toile noircit bien avant de s'enflammer. */
		braise = smoothstep(seuil - 0.09, seuil, front);
		diffuse *= mix(1.0, 0.12, smoothstep(seuil - 0.30, seuil - 0.02, front));
	}

	vec3 to_eye = normalize(CAMERA_POSITION_WORLD - v_world);   // pas E : réservé
	vec3 N = normalize(v_raw_n);
	// le soleil doit être sur la face opposée, quelle que soit celle qu'on regarde
	float thru = max(-sign(dot(N, to_eye)) * dot(N, u_sun), 0.0);
	// et il faut regarder dedans
	float lobe = pow(max(dot(-to_eye, u_sun), 0.0), 3.0);
	vec3 lit_through = u_sun_col * diffuse * thru * lobe * 1.4;

	// la brume en dernier, par le même tour que la coque : voir hull.gdshader
	float hz = haze_along(CAMERA_POSITION_WORLD, v_world);
	ALBEDO = diffuse * (1.0 - hz);
	/* ET LA TOILE LAISSE PASSER LE JOUR DE PARTOUT, pas seulement dans l'axe : le
	   lobe ci-dessus est la lumière qui file droit à travers le tissage, mais une
	   toile de lin en diffuse aussi une bonne part de l'autre côté — une voile au
	   contre-jour est claire même quand on ne regarde pas vers le soleil. Godot le
	   fait pour TOUTE lumière (BACKLIGHT) : le soleil, la lune, et le fanal de
	   poupe qui éclaire la misaine par derrière. Un écart voulu avec la page, qui
	   n'a que le lobe. */
	/* ET LA TRANSLUCIDITÉ SUIT LE JOUR ELLE AUSSI.
	 *
	 * C'est le terme qui gardait la toile allumée quand tout le reste était
	 * éteint, et il a été trouvé en éteignant les cinq termes un par un jusqu'à
	 * n'en laisser qu'un : sur six variantes, celle qui garde la seule
	 * translucidité est la seule qui ressemble encore au témoin (Arnaud, 27/09).
	 *
	 * La raison est dans sa nature : le BACKLIGHT n'a PAS de chute en N·L. Une
	 * voile éclairée par-derrière rend autant qu'une voile éclairée de face, et
	 * une lueur qui vient de n'importe où la rallume. De jour c'est exactement ce
	 * qu'on veut — une voile à contre-jour est claire, c'est même sa signature.
	 * La nuit, elle devient la seule chose de l'image éclairée de tous les côtés
	 * à la fois, et un navire aux feux couverts montre ses voiles à un mille.
	 *
	 * Le même facteur que l'émission, donc, et pour la même raison : ce qui
	 * traverse une toile est ce que le ciel lui donne, et à minuit il ne donne
	 * presque rien. */
	BACKLIGHT = diffuse * u_translucency * jour * (1.0 - hz);
	SPECULAR = 0.5 * (1.0 - hz);
	ROUGHNESS = 0.95;
	METALLIC = 0.0;
	/* ET CE QUI TRAVERSE LE TISSAGE SUIT LE CIEL QUI LE DONNE.
	 *
	 * Cette émission constante est la lumière que le CIEL envoie à travers la
	 * toile de tous les côtés à la fois, qu'aucun lobe ne représente. Constante,
	 * elle l'était aussi la nuit : à minuit tout le reste de l'image tombe à
	 * quelques millièmes et la toile restait à 0,066, si bien qu'un navire feux
	 * couverts dans le noir montrait ses voiles comme des draps pendus (signalé).
	 * Ce n'est pas une lampe, c'est une fenêtre : sans jour derrière, elle ne
	 * donne rien.
	 *
	 * Tiré de l'horizon lui-même, qui porte l'heure — même remède que la brume
	 * rasante, même faute : rien de ce qui ne fait que RENVOYER la lumière ne
	 * doit être plus clair que ce qui tombe dessus. Le plancher laisse de quoi
	 * deviner la toile sous les étoiles, sans quoi elle disparaîtrait tout à
	 * fait et la mâture perdrait sa silhouette. */

	/* ET LA BRAISE ÉMET. Elle ne suit ni le ciel ni l'heure — c'est du feu, la
	   seule chose de cette voile qui ait une lumière à elle. La brume la mange
	   comme le reste : une voile qui brûle à un mille est une lueur, pas une
	   flamme. */
	EMISSION = (u_emissive * u_emissive_k * jour + lit_through
	            + vec3(1.0, 0.34, 0.06) * braise * 4.0) * (1.0 - hz)
	         + ship_haze_colour(CAMERA_POSITION_WORLD, v_world) * hz;
}
