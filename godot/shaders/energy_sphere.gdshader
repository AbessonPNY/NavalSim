/* LA SPHÈRE D'ÉNERGIE — la bulle qui se ferme autour du navire avant le saut.
 *
 * ELLE NE FAIT PAS QUE MONTER EN OPACITÉ : elle commence en BULLE et finit en
 * MUR. Une transparence qui monte uniformément de 0 à 1 se lit comme un voile
 * qu'on tire, pas comme quelque chose qui se charge. On sépare donc deux termes :
 *
 *   · le LISERÉ de bord (fresnel), fort au début et noyé à la fin — c'est lui
 *     qui donne la bulle de savon, une coquille dont on ne voit que la tranche ;
 *   · le REMPLISSAGE, en carré du niveau, donc tardif : le cœur reste clair
 *     pendant que le bord brille déjà, puis se ferme d'un coup sur la fin.
 *
 * Leur somme atteint 1 : à pleine charge la sphère est opaque et l'on ne voit
 * plus le navire dedans. C'est ce qui a été demandé, et c'est aussi ce qui
 * justifie la coupure — on ne verra pas le navire disparaître, on verra la
 * lumière se refermer sur lui.
 *
 * DEUX FACES, comme le .glb le déclare. La caméra de pont est DEDANS : en ne
 * gardant que les faces avant, la coquille disparaîtrait pour qui la regarde de
 * l'intérieur, ce qui est exactement le point de vue le plus intéressant.
 *
 * ELLE N'ÉCRIT PAS SA PROFONDEUR mais elle la TESTE. Sans écriture, la mâture qui
 * la traverse ne serait pas découpée par elle ; avec le test, l'hémisphère
 * lointain reste caché derrière la coque au lieu de doubler le voile.
 */
shader_type spatial;
render_mode blend_mix, unshaded, shadows_disabled, depth_draw_never, cull_disabled;

/* LE BLEU DU DÉBUT ET LE BLANC DE LA FIN, les mêmes que les anneaux : c'est la
   même machine, et deux bleus voisins mais différents se liraient comme une
   erreur plutôt que comme une nuance. */
uniform vec3 u_cold : source_color = vec3(0.20, 0.36, 0.88);
uniform vec3 u_hot : source_color = vec3(0.94, 0.97, 1.00);

/// 0 absente, 1 pleine — ShipNode la fait monter avec les anneaux et retomber vite.
uniform float u_level = 0.0;
/* CE QUE VAUT SA LUMIÈRE EN HDR. Plus bas que les anneaux (6) : eux sont un
   filament, elle est une surface de vingt mètres, et la même valeur ferait une
   tache blanche qui mangerait l'écran. */
uniform float u_energy = 3.2;
/// La dureté du liseré. Plus haut, plus fin.
uniform float u_rim = 2.6;

void fragment() {
	float lvl = clamp(u_level, 0.0, 1.0);

	/* LA COQUILLE. `dot(NORMAL, VIEW)` vaut 1 en face et 0 à la tranche, les deux
	   étant en espace vue : la valeur absolue la rend juste des DEUX côtés, sans
	   quoi l'hémisphère lointain aurait son liseré à l'envers. */
	float face = abs(dot(normalize(NORMAL), normalize(VIEW)));
	float shell = pow(1.0 - face, u_rim);          // 0 au centre, 1 à la tranche

	/* DE LA TRANCHE VERS LE CŒUR, PUIS TOUT ENTIÈRE — et le tout MULTIPLIÉ par le
	   niveau.
	   Ce dernier facteur manquait, et c'était la faute : sans lui, la tranche
	   valait 1 dès que la bulle paraissait, si bien qu'elle s'allumait d'un coup
	   au lieu de monter — et d'un coup dans sa couleur FROIDE, puisque rien
	   n'était encore chargé. Les deux défauts signalés n'en faisaient qu'un. */
	float a = mix(shell, 1.0, lvl * lvl);          // la tranche d'abord, le cœur ensuite
	ALPHA = clamp(a * lvl, 0.0, 1.0);

	/* Sa lumière s'éclaircit AVEC son alpha : une surface qui devient à la fois
	   plus opaque et plus claire se lit comme une charge, là où une opacité seule
	   se lirait comme une fumée.

	   LA COULEUR BLANCHIT AVANT DE SE FERMER (puissance 0,6) : à mi-charge elle
	   est déjà aux trois quarts du blanc, alors qu'elle n'est qu'au quart de son
	   opacité. L'ordre inverse — se fermer d'abord, blanchir ensuite — donne un
	   mur bleu qui s'éclaircit après coup, ce qui ne se lit pas comme une montée
	   en puissance. */
	ALBEDO = mix(u_cold, u_hot, pow(lvl, 0.6)) * u_energy * (0.25 + lvl);
}
