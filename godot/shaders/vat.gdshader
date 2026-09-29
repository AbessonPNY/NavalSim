shader_type spatial;

/*  L'ANIMATION CUITE DANS UNE TEXTURE — VAT, Vertex Animation Texture.

    UN PERSONNAGE SANS SQUELETTE. Chaque sommet a sa colonne dans une texture,
    chaque image de l'animation sa ligne ; le sommet lit son déplacement et
    s'écarte de sa pose de repos. Aucun os, aucune peau à calculer, et Godot
    instancie les figurants entre eux puisqu'ils partagent maillage et matière.
    C'est la manière de peupler un quai sans rien payer.

    CE QUE LA TEXTURE CONTIENT, et c'est ce qui casse quand on se trompe :

      · des DÉCALAGES et non des positions — la ligne 0 est nulle partout, c'est
        la pose de repos ; lues comme des positions absolues, les figurants se
        rassembleraient tous à l'origine du monde ;
      · en MÈTRES BRUTS (l'exportateur n'a pas normalisé), donc rien à remapper ;
      · en demi-flottants, donc la texture ne doit surtout pas être compressée ;
      · indexées par UV2.x, que le maillage porte : la colonne du sommet.

    L'ALLER-RETOUR PLUTÔT QUE LA BOUCLE. Cette cuisson n'est pas cyclique : sa
    première ligne est la pose de repos et sa dernière est en plein mouvement,
    si bien qu'un rebouclage claquerait à chaque tour. On la lit donc en avant
    puis en arrière — sans couture par construction, et invisible sur un homme
    qui attend. Une animation vraiment cyclique se lirait avec u_pingpong à 0.

    LE DÉPHASAGE EST PAR INSTANCE (instance uniform) : deux figurants côte à
    côte qui respirent au même rythme se lisent comme un seul objet dupliqué,
    ce qui est exactement l'effet qu'on veut éviter.
*/

uniform sampler2D u_pos : filter_nearest, repeat_disable, hint_default_black;
uniform sampler2D u_nrm : filter_nearest, repeat_disable;
uniform vec3 u_tint : source_color = vec3(0.62, 0.58, 0.52);

/* SA PEAU, SI ON LA LUI DONNE. VAT Toolkit n'exporte QUE de la géométrie : son
   glTF n'a ni materials, ni images, ni textures — le maillage sort nu. Mais il
   garde les UV, intactes et dans le carré unité, si bien qu'il suffit de
   déposer l'image à côté des trois autres fichiers pour qu'elle retombe juste.
   Sans elle, u_tint tient lieu de peau. */
uniform sampler2D u_albedo : source_color, hint_default_white;
uniform float u_hastex = 0.0;
uniform float u_rough = 0.85;

/// <summary>Combien de lignes porte la texture, c'est-à-dire d'images.</summary>
uniform float u_frames = 249.0;
/// <summary>
/// LA CADENCE DE LECTURE, et ce n'est pas forcément celle de la cuisson.
///
/// Vingt-quatre est le taux de la scène Blender, donc la vitesse VRAIE du
/// mouvement. Mais cette cuisson-ci n'est pas cyclique : lue en aller-retour,
/// elle met 248 images à l'aller et autant au retour, soit VINGT SECONDES pour
/// un cycle à sa vitesse d'origine — et un corps qui met vingt secondes à
/// osciller ne ressemble à rien de vivant.
///
/// On l'a poussée à 72 un moment, en croyant que la lenteur faisait la
/// gélatine. Elle ne la faisait pas : la cause était une DÉRIVE DU CORPS dans
/// la cuisson — les pieds se déplaçaient de cinq centimètres, mesurés. Accélérer
/// ne faisait que patiner plus vite. On revient donc à la vitesse vraie, et la
/// justesse viendra de l'animation, pas de sa cadence de lecture.
///
/// Le cycle, en secondes : 2 × (images / u_fps) en aller-retour, (images / u_fps)
/// en boucle franche.
/// </summary>
uniform float u_fps = 24.0;
/// <summary>1 : aller-retour (cuisson non cyclique). 0 : boucle franche.</summary>
uniform float u_pingpong = 1.0;

/* LES AXES, PARCE QU'ON NE PEUT PAS LES DEVINER. Blender travaille en Z vers le
   haut, le glTF en Y ; l'exportateur convertit le MAILLAGE, et selon l'outil il
   convertit ou non les décalages. Les deux conventions donnent des amplitudes
   également plausibles sur un homme debout, donc aucune lecture des chiffres ne
   tranche : on regarde, et on bascule ce nombre. 0 = tel quel, 1 = Z-up rendu
   en Y-up. */
uniform int u_axes = 1;

/// <summary>Zéro fige la pose de repos — pour juger le maillage seul.</summary>
uniform float u_amp = 1.0;

/* LA NORMALE VIENT-ELLE DE LA TEXTURE ? Zéro garde celle du maillage, figée
   dans la pose de repos : l'éclairage cesse de suivre le mouvement, mais on voit
   tout de suite si c'est la texture qui l'assombrissait. Un cadran de
   diagnostic, à laisser à un. */
uniform float u_texnrm = 1.0;

/// <summary>Les axes de la NORMALE, qui peuvent différer de ceux du déplacement.</summary>
uniform int u_nrmaxes = 1;

instance uniform float u_phase = 0.0;

vec3 naval_vat_axes(vec3 v) {
	// 1 : Blender (Z en haut) rendu en glTF (Y en haut). LE SIGNE EST POSITIF —
	// mesure sur la rigidite des aretes : (x, z, y) donne 2,08 % de variation,
	// (x, z, -y) en donne 4,53, et la pire orientation 10,56. Un corps ne
	// s etire pas, donc la plus rigide est la vraie.
	return u_axes == 1 ? vec3(v.x, v.z, v.y) : v;
}

void vertex() {
	float t = TIME * u_fps / u_frames + u_phase;
	// aller-retour : 0 → 1 → 0, continu et sans couture
	float f = u_pingpong > 0.5 ? abs(fract(t * 0.5) * 2.0 - 1.0) : fract(t);
	float row = f * (u_frames - 1.0);

	/* DEUX LIGNES ET UN FONDU. La cuisson est à 24 images par seconde et l'écran
	   en rend 60 : lue au plus proche, l'animation saccade à la fréquence de la
	   cuisson. Le fondu entre deux lignes coûte une seconde lecture de texture
	   et rend le mouvement continu. */
	float r0 = floor(row);
	float r1 = min(r0 + 1.0, u_frames - 1.0);
	float k = row - r0;
	vec2 c0 = vec2(UV2.x, (r0 + 0.5) / u_frames);
	vec2 c1 = vec2(UV2.x, (r1 + 0.5) / u_frames);

	vec3 d = mix(texture(u_pos, c0).rgb, texture(u_pos, c1).rgb, k);
	VERTEX += naval_vat_axes(d) * u_amp;

	/* LA NORMALE SUIT, sans quoi l'éclairage reste celui de la pose de repos et
	   le personnage se lit comme une statue qui glisse. Le PNG est en huit bits,
	   donc remappé de [-1, 1] vers [0, 1] : on le défait. */
	if (u_texnrm > 0.5) {
		vec3 n = mix(texture(u_nrm, c0).rgb, texture(u_nrm, c1).rgb, k) * 2.0 - 1.0;
		NORMAL = normalize(u_nrmaxes == 1 ? vec3(n.x, n.z, n.y) : n);
	}
}

void fragment() {
	ALBEDO = u_hastex > 0.5 ? texture(u_albedo, UV).rgb : u_tint;
	ROUGHNESS = u_rough;
	SPECULAR = 0.35;
}
