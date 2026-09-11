/* LE CIEL, ÉCRIT UNE SEULE FOIS.
 *
 * Le dôme et le reflet de la mer l'appellent tous les deux, et ils DOIVENT
 * venir du même code : si l'eau miroitait un autre ciel que celui du dessus,
 * l'horizon montrerait une couture et l'image entière se lirait comme fausse.
 * Le dôme ajoute seulement le disque solaire ; la mer tire son éclat d'un lobe
 * micro-facettes, ce qui l'étale en route de scintillement.
 *
 * Pendant Godot de Naval.SKY_GLSL.
 */

// --- ce que le ciel porte aujourd'hui ---
uniform vec3  u_sun;              // direction VERS le soleil, unitaire
// DEJA EN LINEAIRE : Sky.cs les calcule numeriquement, comme le faisait
// `setRGB` de three.js, qui ecrit dans l espace de travail et non en sRGB.
// Pas de `source_color`, qui les convertirait une seconde fois.
uniform vec3  u_zenith  = vec3(0.12, 0.36, 0.52);
uniform vec3  u_horizon = vec3(0.82, 0.89, 0.93);
uniform float u_cloud = 0.12;     // la couverture, de 0 au couvert
uniform float u_sky_time = 0.0;   // le pont de nuages dérive avec le jour
uniform float u_storm = 0.0;      // le couvercle, de 0 à 1

// --- un grain qu'on n'a pas encore atteint ---
uniform vec2  u_storm_dir = vec2(1.0, 0.0);        // relèvement du grain le plus proche
uniform float u_storm_loom = 0.0;                  // à quel point ce quart de ciel est noir
uniform vec2  u_storm_flash_dir = vec2(1.0, 0.0);  // où dans le banc se trouve CE coup
uniform float u_storm_flash = 0.0;                 // et sa force, à l'instant


float naval_hash13(vec3 p) {
	p = fract(p * 0.1031);
	p += dot(p, p.yzx + 33.33);
	return fract((p.x + p.y) * p.z);
}

/* LES ÉTOILES, posées sur un réseau de cellules du VECTEUR DIRECTION et non sur
   une grille de latitude et longitude — celle-ci les entasserait aux pôles et
   laisserait une calvitie au zénith. Une cellule sur cent trente porte une
   étoile, décentrée dans sa case pour que le réseau ne se voie jamais.

   Elles ont une vraie LARGEUR plutôt que d'être des points. Une étoile plus
   étroite qu'un pixel est une tempête de scintillement garantie dès que la
   caméra tourne — le même piège d'aliasing que les rides de la mer, et le même
   remède : être plus large que le pas d'échantillonnage, pas plus brillante. */
float naval_stars(vec3 dir) {
	vec3 d = dir * 190.0;
	vec3 id = floor(d);
	vec3 f = fract(d) - 0.5;
	float h = naval_hash13(id);
	if (h < 0.986) return 0.0;
	vec3 off = vec3(naval_hash13(id + 11.0), naval_hash13(id + 23.0),
	                naval_hash13(id + 37.0)) - 0.5;
	float r = length(f - off * 0.55);
	// un éventail de magnitudes : un ciel d'étoiles égales se lit comme une texture
	float mag = pow(fract(h * 613.0), 1.5);
	return smoothstep(0.17, 0.02, r) * (0.28 + 0.95 * mag);
}

float naval_noise2(vec2 p) {
	vec2 i = floor(p);
	vec2 f = fract(p);
	f = f * f * (3.0 - 2.0 * f);
	float a = naval_hash13(vec3(i, 7.0));
	float b = naval_hash13(vec3(i + vec2(1.0, 0.0), 7.0));
	float c = naval_hash13(vec3(i + vec2(0.0, 1.0), 7.0));
	float d = naval_hash13(vec3(i + vec2(1.0, 1.0), 7.0));
	return mix(mix(a, b, f.x), mix(c, d, f.x), f.y);
}

/* LE NUAGE, SUR UN PLAN ET NON SUR LE DÔME.
 *
 * Projeter la direction de vue par dir.xz/dir.y pose le bruit sur un pont plat
 * au-dessus, qui est ce sur quoi les nuages se tiennent réellement : ils se
 * serrent vers l'horizon et s'ouvrent au zénith, tout seuls. Enrouler une
 * texture autour de la sphère donne une dispersion égale qui se lit comme du
 * papier peint, et aucun détail ne l'en sauve.
 *
 * L'ÉCHELLE EST TOUT. Le premier jet lisait le bruit trop fin, ce qui faisait
 * tenir tout le ciel visible dans quelques centièmes d'unité de bruit : le motif
 * y est quasi constant, et ce qui en sort est un voile pâle uniforme. Le bruit a
 * des motifs d'environ une unité, donc le ciel doit en couvrir une dizaine.
 *
 * Le caractère vient ensuite de l'ANISOTROPIE : lu quatre fois plus fin en
 * travers de la traînée que dans sa longueur. Isotrope on obtient un ciel
 * pommelé — vrai, mais couvert. Étiré, on obtient des cirrus.
 */
float naval_clouds(vec3 dir, float amt) {
	float up = dir.y;
	if (up < 0.02 || amt <= 0.0) return 0.0;

	vec2 q = vec2(dir.x, dir.z) / up;
	vec2 p = vec2(q.x * 4.2, q.y * 17.0) + vec2(u_sky_time * 0.012, u_sky_time * 0.030);
	float f  = naval_noise2(p) * 0.55;
	f += naval_noise2(p * 2.13 + 3.7) * 0.28;
	f += naval_noise2(p * 4.37 + 9.1) * 0.17;

	/* Étiré. Trois octaves moyennées se serrent autour d'un demi, donc un seuil
	   pris dans cette plage allume TOUT le ciel un peu et donne un lavis pâle et
	   plat. Écartées, les basses valeurs tombent au clair du seuil et il y a du
	   vrai bleu entre les nuages — ce qui est ce qui fait lire un ciel comme du
	   temps qu'il fait. */
	f = clamp((f - 0.5) * 2.2 + 0.5, 0.0, 1.0);

	/* Haut dans l'histogramme : seules les crêtes du bruit passent, et elles
	   passent en traînées. Le curseur va toujours jusqu'au couvert, mais le beau
	   temps est le défaut.

	   C'est le PLANCHER qui est relevé et non le sommet, parce que baisser le
	   curseur ne baissait pas le ciel : la bande du seuil est large, donc
	   déplacer son bord fait glisser beaucoup de bruit au travers, mais
	   lentement. Passer de 12 % à 6 % ne retirait que 14 % de nuage là où on en
	   voulait la moitié. La pente monte avec le plancher pour que le couvert
	   plein retombe exactement où il était. */
	float c = smoothstep(0.93 - amt * 0.65, 0.99 - amt * 0.36, f);
	return c * smoothstep(0.02, 0.11, up);
}

/* La couverture est un PARAMÈTRE et non simplement u_cloud, pour un appelant :
   le miroir de la mer n'en demande aucune. Un cirrus réfléchi est une bavure
   grise qui se lit comme de la saleté sur la surface plutôt que comme du ciel —
   le reflet est un lobe micro-facettes, donc tout ce qui a une structure fine
   revient en bouillie. La règle du ciel unique tient toujours : c'est la même
   fonction, le même code, la même météo, à qui l'on demande une couverture
   différente. */
vec3 naval_sky(vec3 dir, vec3 sun_dir, vec3 zenith, vec3 horizon, float cloud_amt) {
	float h = clamp(dir.y, 0.0, 1.0);
	vec3 c = mix(horizon, zenith, pow(h, 0.55));

	/* Le nuage passe AVANT le halo solaire et avant la brume d'horizon : un
	   nuage est éclairé par le soleil puis vu à travers le même air que tout le
	   reste, donc le poser après le laisserait flotter devant la crasse.

	   ET ILS S'ÉTEIGNENT AVEC LE SOLEIL. Un nuage n'a pas de lumière à lui : on
	   ne le voit que parce que le soleil est dessus, donc quand celui-ci passe
	   sous l'horizon il n'y a plus rien à voir. Éclairés toute la nuit ils
	   étaient la seule chose de l'image à briller d'elle-même — même faute que
	   l'écume, même remède.

	   Cela réemploie le SMOOTHSTEP MÊME qui fait sortir les étoiles, un peu plus
	   bas pour que le nuage soit parti quand elles sont franchement là : c'est le
	   même événement vu des deux côtés, et lui écrire un second seuil laisserait
	   les deux dériver. */
	float dusk = 1.0 - smoothstep(0.02, -0.10, sun_dir.y);
	float cl = naval_clouds(normalize(dir), cloud_amt) * (1.0 - u_storm) * dusk;
	if (cl > 0.001) {
		float sd0 = max(dot(normalize(dir), sun_dir), 0.0);
		vec3 lit = mix(horizon * 1.22, vec3(1.0, 0.98, 0.94), 0.30);
		// dessous gris, et un liseré vif là où le soleil est derrière eux
		lit = mix(lit * 0.66, lit * 1.18, pow(sd0, 3.0));
		c = mix(c, lit, cl * 0.62);   // les cirrus sont minces : le bleu passe au travers
	}

	/* Les étoiles sortent à mesure que le soleil descend, et elles vivent SOUS
	   la brume — ajoutées avant le mélange vers l'horizon ci-dessous, donc elles
	   s'amincissent vers l'horizon comme les vraies au lieu de flotter par-dessus
	   la crasse. */
	float night = smoothstep(0.05, -0.12, sun_dir.y);
	if (night > 0.001) {
		c += vec3(0.88, 0.92, 1.00) * 1.7 * (1.0 - u_storm) * naval_stars(normalize(dir))
		     * night * smoothstep(-0.02, 0.16, dir.y);
	}

	float sd = max(dot(normalize(dir), sun_dir), 0.0);
	// halo large : l'air disperse la lumière du soleil sur un grand arc
	c += vec3(1.00, 0.82, 0.55) * pow(sd, 8.0) * 0.30;
	c += vec3(1.00, 0.90, 0.72) * pow(sd, 64.0) * 0.35;
	// la brume s'épaissit vers l'horizon, où l'on regarde à travers plus d'air
	c = mix(c, horizon * 1.04, smoothstep(0.22, -0.02, dir.y));

	/* ET PUIS LE COUP DE VENT POSE SON COUVERCLE, PAR-DESSUS TOUT — le halo
	   solaire et le lavis d'horizon compris. IL DOIT VENIR EN DERNIER. Posé
	   avant, il était simplement défait : la quasi-totalité du ciel qu'on regarde
	   vraiment tient à moins de vingt degrés de l'horizon, soit exactement la
	   bande que le terme de brume est en train de relaver en blanc, si bien que
	   la tempête sortait en gris pâle au lieu de sombre.

	   Pas d'une couleur plate non plus. Un ciel couvert est le plus sombre au
	   zénith et se relève un peu vers l'horizon, où la lumière passe sous le bord
	   des nuages — et ce dégradé est l'essentiel de ce qui l'empêche de se lire
	   comme un mur. */
	if (u_storm > 0.001) {
		vec3 lid = horizon * mix(0.40, 0.16, smoothstep(0.0, 0.45, dir.y));
		c = mix(c, lid, u_storm * 0.96);
	}

	/* UN GRAIN QU'ON N'A PAS ENCORE ATTEINT : un quart de ciel noir debout sur
	   l'horizon, dans sa direction et nulle part ailleurs. C'est ce qui fait de
	   la météo quelque chose qu'on VOIT venir et qu'on peut contourner, plutôt
	   que quelque chose qui arrive simplement — et cela coûte un produit
	   scalaire.

	   Seulement près de l'horizon et seulement dans ce relèvement : une
	   dépression vue de l'extérieur est un mur avec du ciel au-dessus et de
	   l'eau claire de part et d'autre. Elle passe la main au couvercle ci-dessus
	   quand on entre, n'ayant plus rien à désigner une fois dedans. */
	if (u_storm_loom > 0.001) {
		vec2 hz = dir.xz;
		float hl = length(hz);
		if (hl > 1e-4) {
			float al = max(0.0, dot(hz / hl, u_storm_dir));
			/* Large, et HAUT. Le premier jet employait un cône étroit mourant à
			   quinze degrés d'élévation, ce qui confinait le tout à une bande de
			   ciel de quelques dizaines de pixels — mesurable à neuf pour cent de
			   l'image et parfaitement invisible. Un grain vu de six milles est un
			   MUR : il monte bien haut dans le ciel et s'étend sur une bonne part
			   de l'horizon. */
			float sect = pow(al, 4.5) * (1.0 - smoothstep(0.03, 0.34, dir.y));
			c = mix(c, horizon * 0.09, sect * u_storm_loom);

			/* ET IL S'ALLUME PAR EN DEDANS. Un grain vu de six milles n'est pas
			   une forme grise morte : il s'éclaire brièvement, quelque part le
			   long de son front, et c'est l'essentiel de ce qui le distingue d'un
			   banc de brume. AJOUTÉ plutôt que mélangé, parce qu'une décharge est
			   de la lumière qui arrive et non une couleur qu'on choisit.

			   Sur un lobe serré autour de SON PROPRE relèvement, pas celui du
			   banc : un coup se produit quelque part dans le nuage, donc il en
			   éclaire une partie, et un front entier qui clignote d'un bloc se lit
			   comme un interrupteur. */
			if (u_storm_flash > 0.001) {
				float fa = max(0.0, dot(hz / hl, u_storm_flash_dir));
				/* Faible, et SERRÉ. De la lumière additive dans un ciel que la
				   brume étale ensuite sur la mer ne reste pas où on la met : à
				   près du double de la couleur d'horizon elle sortait comme un
				   second soleil posé sur l'eau, ce qui est un essai nucléaire et
				   non un grain. */
				float lobe = pow(fa, 34.0) * (1.0 - smoothstep(0.01, 0.20, dir.y));
				/* SA PROPRE COULEUR, et non l'horizon réduit. Cette distinction a
				   coûté une version : l'écume prend sa clarté au ciel parce que
				   l'écume RÉFLÉCHIT, et le même réflexe appliqué ici rendait le
				   coup proportionnel à l'ambiante — donc il s'éteignait la nuit,
				   c'est-à-dire précisément à l'heure où un grain lointain s'allume
				   comme une lampe derrière un drap. UN ÉCLAIR ÉMET. Ce qu'il
				   ajoute ne dépend pas de ce qui est éclairé par ailleurs ; c'est
				   ce qui en fait un éclair. */
				c += vec3(0.58, 0.66, 0.85) * (u_storm_flash * lobe * 0.55 * u_storm_loom);
			}
		}
	}
	return c;
}

/// Ce que le ciel porte aujourd'hui, pour tout appelant sauf le miroir.
vec3 naval_sky(vec3 dir, vec3 sun_dir, vec3 zenith, vec3 horizon) {
	return naval_sky(dir, sun_dir, zenith, horizon, u_cloud);
}
