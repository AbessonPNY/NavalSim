# Simulation navale — notes de projet

Simulation 3D d'un navire à voile : flottaison d'Archimède réelle sur une houle
de Gerstner, gouvernail, gréement. Aucune dépendance npm — Node et un navigateur
suffisent. three.js vient d'un CDN.

## Démarrer

```bash
node build.js          # produit dist/naval-sim.html (fichier autonome publiable)
```

Le serveur de dev est déclaré dans `.claude/launch.json` (`node .claude/serve.js`,
port 8765) : demandez-moi de le lancer, ou `node .claude/serve.js`.

Artifact publié : https://claude.ai/code/artifact/e9837c06-9839-4888-a33a-bd03cdcd233c
Pour le mettre à jour depuis une nouvelle session, il faut passer cette URL
explicitement, sinon un second artifact est créé.

## Architecture

`naval-sim.html` ne contient que le balisage et un `main()` de câblage. Toute la
logique est dans `js/`, en classes attachées à un espace de noms global `Naval`
(pas de modules ES : le build inline les fichiers par simple concaténation).

| module | rôle |
|---|---|
| `config.js` | constantes du monde (ρ, g, grille de sondes, liste de repli des navires) |
| `weather.js` · `storms.js` | le vent qui se conduit seul · les dépressions, qui ont un lieu |
| `rain.js` · `splash.js` | le rideau de pluie · l’eau jetée par ce qui tombe dedans |
| `ship-spec.js` | lit une fiche JSON et en **dérive** tout ce que le solveur consomme |
| `hull-lines.js` | le plan de formes, en fonctions pures |
| `stage.js` | renderer, scène, lumière, ciel |
| `ocean.js` | houle de Gerstner : shader GPU **et** échantillonnage CPU |
| `foam.js` · `ssao.js` | champ d'écume persistant · occlusion ambiante du navire |
| `underwater.js` | la coque vue à travers l'eau, extinction par canal |
| `world.js` · `land.js` | les îles, en fonction pure de la position · leur maillage |
| `chart.js` | la carte marine, le point et le sillage tracé |
| `ship-model.js` | coque, gréement, voiles, sillage, chargement .glb |
| `ship-physics.js` | sondes, corps rigide 6 ddl, gouvernail, voiles |
| `controls.js` · `camera-rig.js` · `hud.js` | barre, caméras, instruments |
| `helm.js` | la barre des navires qui ne sont pas le vôtre |
| `guns.js` | la bordée, et surtout sa fumée |

## Cahier des charges

**Plusieurs bâtiments peuvent être à flot en même temps**, à l'écran ou sur une
carte. Pas de réseau pour l'instant : ils tournent tous dans la même page, dans
la même boucle. Cela ne change rien à ce qui est déjà écrit, mais cela **borne ce
qu'on a le droit d'écrire ensuite** — toute nouveauté qui décrit l'état d'un
navire doit vivre sur *son* instance, jamais dans un global ni dans un uniforme
de la mer.

Ce qui supporte déjà N navires, sans rien changer :

- `ShipSpec`, `HullLines`, `ShipPhysics`, `ShipModel` sont des classes d'instance,
  sans état statique — il suffit d'en construire plusieurs ;
- l'**occlusion ambiante**, qui isole les navires par une couche de rendu
  (`Naval.SHIP_LAYER`) et non par un objet : dix navires passent dans la même
  passe, pour le même prix ;
- les **ombres portées** et les patches de matériau (`applyHaze`,
  `applySailLight`, `applyShipAO`), tous par maillage ou par matériau.

- **la mer et l'écume portent la flotte.** Les uniformes de coque sont des
  tableaux de `Naval.Config.MAX_SHIPS`, `uShipCount` disant combien sont vivants,
  et les deux shaders bouclent dessus en combinant par `max()` — deux coques bord
  à bord font une tache blanche, pas une tache deux fois blanche. Coût mesuré
  nul : la boucle tourne `NSHIP` fois quoi qu'il arrive, 0,40 ms à trois coques
  contre 0,42 à une ;
- **la passe d'eau transparente et le SSAO** portaient déjà N navires sans le
  savoir, isolant par couche de rendu et non par objet.

Ce qui suppose encore un navire unique, et qu'il faudra lever :

- **la fenêtre d'écume et la boîte d'ombre suivent un seul navire**
  (`foam.update(..., body.pos)`, `stage.aimSun(body.pos)`) : deux bâtiments
  éloignés ne peuvent pas être servis par la même fenêtre de 620 m ;
- **la barre, les instruments et les caméras** désignent l'entrée 0 de la flotte.
  C'est voulu — ce sont ceux du navire qu'on commande — et le bouton ⚓ du
  panneau Flotte déplace la barre en **échangeant** les entrées, puisque l'indice
  0 *est* le navire commandé pour la boucle, les instruments, les caméras et la
  fenêtre d'écume. L'indice étant aussi la ligne de texture, les profils sont
  réécrits avec, exactement comme à un retrait.

  **Les ordres voyagent aussi.** Le navire qu'on quitte garde une **copie** de
  l'état de barre comme sien, donc il continue sous la machine et les écoutes
  qu'on lui a laissées au lieu de se mettre en panne ; celui qu'on prend cède
  les siens à la console, qui montre alors ce qu'il fait vraiment et non ce que
  faisait le précédent. Partager un seul objet ferait obéir toute la flotte à la
  même roue.

  **Et rien ne doit RETENIR cet objet, ce qui a coûté un vrai bug.** L'échange
  réaffecte le `ctrl` de chaque entrée, mais une barre automatique construite
  avec `controls.state` en gardait une référence capturée à l'armement — que
  l'échange ne pouvait pas suivre. Le navire qu'on venait de quitter continuait
  donc d'écrire dans **votre** console, soixante fois par seconde : un chaland
  laissé derrière mettait votre machine en avant toute et votre barre à fond,
  un voilier laissé derrière tenait votre machine à zéro — la machine ne
  répondait plus du tout — et vos écoutes comme votre voilure ne vous
  appartenaient pas davantage.

  Les commandes sont donc **passées à chaque image** (`helm.update(dt, ocean, e.ctrl)`)
  plutôt que retenues. Ce n'est pas la même chose que de corriger l'échange pour
  qu'il répare aussi les barres : l'appelant tient l'entrée, l'entrée tient ses
  commandes du moment, et il ne reste plus rien qui puisse se périmer. Même
  famille de faute que le vecteur temporaire aliasé sur une valeur vivante.

**Mettre une coque à l'eau se fait par le panneau « Flotte »**, et il y a une
raison de ne pas le faire à la main : quatre choses sont faciles à oublier et
chacune donne un symptôme discret. Sans `applyAtmosphere` et `enableLighting`
elle ne prend ni brume, ni ombre, ni occlusion, et n'apparaît pas dans l'eau
transparente. Sans `settle()` **avant** de la positionner elle arrive en plein
ciel et rebondit. Sans `setHullProfile(i, …)` à son propre indice elle écume
autour des formes d'un autre. Et une entrée réduite à `{body, spec, afloat}` est
valide — la mer l'écumera — mais rien ne la fera naviguer : il lui faut aussi son
`physics`, son `ship` et son `ctrl`. `launch()` fait les quatre.

**L'indice dans la flotte EST la ligne de texture**, donc tout retrait réécrit
tous les profils (`refitProfiles`). Retirer le deuxième de trois fait glisser le
troisième d'un cran : sans réécriture il hériterait des formes du navire retiré.
De même, changer de navire **détruit les conserves** au lieu de vider la liste,
sinon leurs coques resteraient dans la scène, à naviguer sans rien pour les
piloter.

**Un profil de coque par LIGNE de texture, pas une texture par navire.** GLSL
ES 1.0 refuse d'indexer un tableau de samplers, donc `Naval.HullProfiles` empile
les demi-largeurs de chaque bâtiment dans une texture de `MAX_SHIPS` lignes, lue
au centre exact de sa ligne — aucun mélange entre navires. Pour la même raison,
`hullGap()` reçoit les uniformes de la coque **en paramètres** plutôt qu'un
indice : seule une expression constante peut indexer un tableau d'uniformes, et
un compteur de boucle en est une, un paramètre de fonction non.

En revanche la barre, les instruments et les caméras n'ont *pas* à devenir
multiples : ce sont ceux du navire qu'on commande. Il leur faudra désigner
lequel, pas se dupliquer.

## Le monde ouvert

**Il n'y a pas de fichier de carte, et il n'y en aura jamais.** Les îles sont
une **fonction pure de la position** : un hachage sur une grille grossière de
5,2 km, une île dans à peu près une case sur deux. La mer est donc sans fin,
identique sur toutes les machines et d'une session à l'autre, et ne coûte rien à
stocker. Revenir à 46° N y retrouve la même île, avec les mêmes baies.

**`world.js` travaille en mètres monde VRAIS, jamais en coordonnées locales.**
L'origine flottante fait glisser le zéro local à mesure qu'elle navigue ; une
terre placée en local s'en irait sous elle à chaque recentrage. C'est aussi
pourquoi `land.js` bâtit la géométrie dans le repère **de l'île** et se contente
de la **positionner** à `île − origine` : une île ne change jamais, donc la
recentrer est une affectation de vecteur, pas une reconstruction.

**Le rivage n'est pas un cercle.** Le rayon est modulé par quelques harmoniques
du relèvement, ce qui lui donne caps et anses — un disque se lit comme une pièce
tombée dans l'eau, et aucun détail de relief ne l'en sauve. `Chart` trace son
contour avec **le même `_shore`** que le maillage du terrain, donc la carte ne
peut pas montrer une côte que l'œil ne trouve pas : c'est la règle du plan de
formes unique, appliquée à la terre.

**La brume a dû s'ouvrir.** Réglée à 2,4 km de portée quand il n'y avait rien à
voir au-delà du navire, elle ne laissait passer que 11 % d'une île à un mille et
la réduisait à une tache. Portée à environ 8 km — un jour clair plutôt qu'un
jour de brume — sans quoi « terres à vue » ne veut rien dire.

**Le point se prend en latitude et longitude.** Un mille marin **est** une
minute de latitude, par définition, donc le nord se convertit exactement, sans
projection ni bidouille. L'est se resserre en cosinus de la latitude, ce qui est
réel : un degré de longitude fait 111 km à l'équateur et rien au pôle. L'ignorer
fausserait toute distance lue sur la carte de ce facteur.

**La carte est un canvas 2-D**, pas du WebGL : elle est plate, nord en haut,
faite de traits fins et de texte — ce qu'un canvas fait bien et un shader mal —
et la page a de toute façon 98 % de son image inoccupée. Elle trace en
coordonnées **vraies** ; en local, le navire reviendrait au centre à chaque
recentrage, ce qu'une carte ne doit jamais faire.

## Invariants à ne pas casser

**Un seul plan de formes.** `hull-lines.js` sert à la fois au maillage visible et
à la grille de sondes. Si les deux divergent, ce qu'on voit ne correspond plus à
ce qui flotte — c'est l'invariant le plus important du projet.

**L'origine flotte, et la phase de la houle est ce qui le permet.** Tout est
calculé près de zéro ; `ocean.origin` retient où ce zéro se trouve réellement.
Au-delà de `REBASE_RADIUS` (1500 m) le monde entier glisse sous la flotte.

Sans cela la mer meurt bien avant ce qu'on imagine : la phase de Gerstner vaut
`k·x`, et avec `k` jusqu'à 3 rad/m une position de quelques kilomètres consomme
déjà la quasi-totalité des sept chiffres d'un flottant 32 bits. Les vagues ne
tremblent pas, elles **disparaissent** — vérifié à 400 km : avec l'origine
flottante la houle est normale, sans elle la mer est une nappe parfaitement
lisse.

Décaler l'origine ferait glisser toute la mer de côté, **sauf** si on rend la
phase que ce décalage représente, `k·(d·origine)`. Or cette phase croît sans
borne et ramènerait le problème — sauf qu'une phase ne compte que **modulo 2π**.
Réduite dans les doubles de JavaScript (`syncPhase`), elle reste un petit nombre
que le shader tient exactement. Mesuré : **saut nul** à chaque recentrage,
jusqu'à une origine de 2 300 km.

Trois calculateurs de houle doivent lire cette phase — le vertex shader de la
mer, l'échantillonneur CPU et la passe d'écume — et en oublier un les
désynchroniserait silencieusement.

**Tout ce qui tient une position doit se décaler dans la MÊME image** : les
coques, le champ d'écume (ses *deux* ancres, sinon l'image suivante lit le
décalage comme un glissement colossal et étale le champ), et les caméras — la
caméra fixe surtout, seule chose délibérément immobile, donc seule à se
retrouver à mille mètres de là.

**Le shader et le CPU doivent partager les mêmes vagues.** `ocean.js` calcule la
phase en **espace monde** (`modelMatrix * position`) car le plan est recentré sur
la caméra à chaque image. En espace local, la houle resterait collée à la caméra
et se désynchroniserait du champ de hauteur que le solveur échantillonne.

**Le gouvernail est une force à l'étambot, jamais un couple pur.** C'est ce qui
place le point de pivot en avant du maître-couple, comme sur un vrai navire
faisant route avant, au lieu de la faire tourner autour de son milieu.

**Le cap se lit sur le vecteur d'étrave**, pas sur l'angle d'Euler. Le prendre
sur Euler donnait un signe inverse, qui annulait exactement une erreur de sens
du gouvernail : les deux fautes se masquaient mutuellement.

**Ne jamais réutiliser un vecteur temporaire pour une valeur vivante.** Le centre
de gravité monde a son propre vecteur (`_cog`) : il avait été aliasé sur `_tmp`,
écrasé dès la première sonde, ce qui faussait tous les bras de levier et
provoquait roulis parasite puis explosion numérique.

**Le ciel n'existe qu'une fois.** `Naval.SKY_GLSL` (dans `stage.js`) est la seule
définition du ciel ; le dôme et le reflet de la mer l'appellent tous les deux. Si
l'eau miroitait un autre ciel que celui du dessus, l'horizon montrerait une
couture. Le dôme ajoute seulement le disque solaire ; la mer tire son reflet
d'un lobe micro-facettes.

**Le Fresnel spéculaire se prend sur `V·H`**, pas sur `N·V`. Le prendre sur la
normale effondre le terme à ~2 % sous tous les angles depuis lesquels on regarde
réellement la mer, et supprime le chemin de scintillement.

**Un seul profil de voile.** `Naval.SAIL_FOIL` (dans `ship-physics.js`) porte les
trois coefficients de l'aérofoil. Ils sont lus deux fois : pour fabriquer la
force, et pour en déduire le bordage optimal en forme fermée (`optimalAoA`).
Réécrits en dur aux deux endroits, ils finiraient par diverger et le repère de
la console désignerait un réglage que les voiles ne veulent pas — même famille
de faute que le plan de formes unique.

**Les coefficients hydro sont par unité de surface**, pas des forces absolues.
C'est ce qui permet aux mêmes valeurs de servir une goélette de 24 m et une
frégate de 60 m sans réglage par navire.

## Fiches navires

Un JSON par bâtiment dans `ships/`. **Le dossier fait foi** : y déposer un
fichier suffit (le serveur de dev liste le dossier en direct, le build le balaie).
Le **tonnage est l'entrée** ; la fraction immergée en découle. Viser 30–45 %
d'immersion coque.

```bash
node tools/add-ship.js ships/models/mon-bateau.glb --nom "La Sirène"
```

L'outil lit la boîte englobante réelle du .glb et mesure le volume d'enveloppe
**avec les mêmes formules que le solveur**, pour proposer un tonnage cohérent.

**Deux fiches peuvent partager une carène, et alors elles partagent ses cotes.**
La Roter Löwe et le navire pirate sortent du même dessin : relevé sur les
maillages, bau/longueur 0,26 et creux/longueur 0,42 pour l'un comme pour
l'autre, toutes les cotes du second valant exactement le double du premier. Ils
sont donc à 30 m et 240 t tous les deux.

Le piège est qu'une fiche est un **tout cohérent** : corriger la longueur et le
tonnage sans le reste laisse un navire impossible. Passée à 30 m sans y toucher,
la Roter Löwe gardait 14,5 m de bau, des mâts de 38 m plus hauts qu'elle n'est
longue et 1 850 m² de voilure — **8,4 % d'immersion**, elle flottait comme un
bouchon. Les longueurs vont en ×s, les surfaces en ×s², la vitesse machine en
×√s (Froude), et `rudder.power` ne bouge pas, étant par unité de surface —
mais il revient de 275 à 91,5, le triplement ayant visé les lourds carrés de
60 m dont l'inertie de lacet est trente-deux fois celle-ci. Après : **32,6 %**
d'immersion et 2,82 m de tirant.

Voir `ships/README.md` pour le format complet.

## Contraintes de publication (raison d'être de build.js)

Une page publiée tourne sous une politique de sécurité stricte. Le build existe
pour la satisfaire :

- un `<script src="js/...">` **local est bloqué** → tout est inliné ;
- `fetch()` d'un fichier local est bloqué → les fiches JSON sont embarquées dans
  `Naval.SHIP_DATA` ;
- un `.glb` n'est ni chargeable localement ni téléversable comme asset d'artifact
  (types acceptés : png, jpg, svg, mp4, pdf, woff, csv, md, json, txt) → ses
  octets sont embarqués en base64 et analysés par `GLTFLoader.parse()`, sans
  aucune requête réseau ;
- le build **refuse** de produire un fichier contenant encore une référence locale.

`GLTFLoader` reste chargé depuis jsdelivr via l'import map. S'il est bloqué, la
coque procédurale est conservée avec un avertissement : un modèle manquant
n'interrompt jamais la simulation.

## Pièges rencontrés

**Une page autonome doit déclarer son propre encodage, en PREMIER.** Il n'y
avait aucun `<meta charset>` dans `naval-sim.html`, au motif que l'hôte des
artifacts en fournit un et que le serveur de dev pose l'en-tête lui-même. Les
deux sont vrais, et cela ne suffit pas : déposée sur un hébergeur ordinaire —
Apache chez OVH — la page est servie en `text/html` sans charset, le navigateur
retombe sur du latin-1, et toute l'interface française part en mojibake. Le
symptôme est déroutant parce qu'il **n'apparaît pas en local** : les deux
mécanismes qui masquaient le manque sont justement ceux du développement.

La balise est à l'**octet 0**. Un navigateur ne lit que le premier kilo-octet du
document pour la trouver, donc tout ce qui la précède la met en danger — et rien
n'a besoin de la précéder, pas même le `<title>`.

**Encodage.** `Get-Content` en PowerShell 5.1 lit en ANSI, pas en UTF-8 : extraire
puis réécrire un fichier accentué produit du mojibake et un BOM. Utiliser
`[System.IO.File]::ReadAllText/WriteAllText` avec un encodage explicite.

**Git et les .glb.** Ils sont binaires ; une conversion de fins de ligne les
corromprait silencieusement. `.gitattributes` les marque `binary`.

**Le compteur d'images ne mesure pas la simulation, il mesure l'horloge du
volet.** Mesuré avec une boucle `requestAnimationFrame` **vide**, ne faisant
rigoureusement rien : intervalle médian de 31,2 ms, soit 32 images/s. Le volet
de prévisualisation cadence à 32 quoi qu'on lui demande, si bien que le compteur
affichait le même 32 par mer calme et en tempête. Il disait vrai et n'apprenait
rien.

D'où le second chiffre, le **travail** dans l'image : du haut de la boucle à la
fin du rendu. Il bouge, lui — 0,86 ms à un navire, 2,72 ms à quatre, dans un
budget de 31,3. Autrement dit la page est oisive 98 % du temps, et le rendu
coûte la même chose par mer plate qu'en tempête (0,22 contre 0,21 ms), le shader
bouclant de toute façon sur les dix-huit composantes. Attention : c'est du CPU
seul — sans requête de chronomètre GPU, un shader devenu coûteux ne s'y verrait
pas.

**Mesures dans le navigateur.** Quand le volet de prévisualisation n'est pas
composité, `requestAnimationFrame` est bridé — jusqu'à **zéro** image, pas
seulement une par seconde : la simulation est alors complètement arrêtée et les
relevés paraissent figés ou absurdes (vitesse qui ne monte pas, immersion
incohérente). Ce n'est pas un bug de physique. `document.hidden` reste à `false`
dans ce cas et ne suffit donc pas à le détecter ; le test fiable est de comparer
`Naval.app.ocean.uniforms.uTime.value` avant et après une attente. Pour mesurer
sérieusement, ne pas dépendre de la boucle : piloter le solveur à la main depuis
la console (`physics.step(dt, ocean, ctrl, t)` en boucle, `t` avancé soi-même),
ce qui donne en prime des mesures reproductibles à pas fixe. Une capture d'écran
force quelques images au passage, ce qui suffit à rafraîchir la télémétrie.

Corollaire pour le **rendu** : la boucle gelée ne met plus à jour les uniformes
qu'elle alimente. Déplacer `stage.camera` à la main puis appeler `stage.render()`
laisse `ocean.uniforms.uCam` sur la position de la dernière vraie image, et tout
ce qui dépend du regard — brume, scintillement, translucidité de la toile — est
calculé depuis un œil qui n'est plus là. J'ai cru une heure durant que le shader
de voile était mort alors que seul le banc de mesure l'était. Recopier `uCam`
soi-même après avoir bougé la caméra.

Autre piège de mesure : `read_console_messages` conserve le tampon **d'un
chargement à l'autre**. Une erreur de shader déjà corrigée continue de s'afficher
après rechargement, aux mêmes numéros de ligne, et fait croire à une panne qui
n'existe plus. Ne pas conclure sur la console seule — vérifier que le correctif
est bien servi (le serveur de dev laisse le navigateur mettre les `.js` en
cache ; `fetch(url, {cache:'reload'})` avant de recharger règle la question).
À l'inverse, pour retrouver l'erreur *courante* sous une pile de vieilles, la
filtrer par motif sur un identifiant du code fraîchement écrit : c'est ce qui a
fait sortir le `half` réservé après plusieurs minutes passées à suspecter le
mauvais fichier.

**Shaders.** Un `ShaderMaterial` avec `fog: true` doit fusionner
`THREE.UniformsLib.fog`, sinon le rendu lève une erreur sur `fogColor.value`. Et
un `#define N` écrase l'identifiant `N` jusque dans le fragment shader.

**Rendu.** Une mise à l'échelle négative (`scale.x = -1`) fait disparaître un
maillage. Une voile opaque éclairée à contre-jour devient noire : le tissu porte
une composante `emissive` pour rester lisible.

**Mise en ligne.** `node build.js` écrit `ships/index.json` — et **avertit** quand
`Naval.Config.SHIPS` a dérivé du dossier, ce qui arrive à chaque navire ajouté.
L'avertissement a servi dès le navire pirate. Le serveur de dev
répond à ce chemin par un listage en direct, sans fichier ; un hébergeur statique
non. Sans cet index, la page retombe sur la liste courte de `config.js` et tout
navire ajouté depuis n'est jamais demandé — ses .glb paraissent alors ne pas se
charger alors qu'ils n'ont jamais été réclamés. Le plus sûr reste de déployer
`dist/naval-sim.html` seul, qui embarque tout.

**Console de mer.** Six réglages : force de la houle (Beaufort), direction du
vent, hauteur du soleil (négative = nuit), **défilement du jour**, **creux** —
qui multiplie la hauteur significative au-delà de la table Beaufort — et
couverture nuageuse. Plus un bouton « Météo automatique », qui laisse le vent se
conduire tout seul. Ce dernier existe parce qu'un spectre
étalé sur dix-huit composantes et un éventail de directions paraît plus plat que
six harmoniques alignées, à hauteur égale : les crêtes ne se superposent plus.
Au-delà de ~1,6 un navire peut réellement chavirer, ce qui est voulu.

**Gréer un modèle importé.** Un `.glb` arrive avec une coque et des espars nus,
mais quasiment jamais de voiles — la Roter Löwe n'en a aucune, et ses nœuds
portent les noms Blender par défaut (`Cylinder.004`…), donc rien à quoi les
reconnaître *par le nom*. Ce qu'elle a, ce sont des **vergues**, et une vergue se
reconnaît à sa forme seule : un espar bien plus large en travers qu'épais, posé
en croix sur l'axe. `_rigModel()` les lit sur la géométrie, les regroupe par mât
(l'écart entre mâts est d'un ordre de grandeur supérieur à la dispersion sur un
même mât) et y suspend la toile. Aucune donnée par navire : déposer un carré
dans `ships/models` suffit à le gréer.

Deux points à ne pas défaire. Les vergues sont **reparentées dans le pivot** qui
porte la voile (`Object3D.attach`, qui conserve la transformée monde) : faire
tourner la toile seule la ferait glisser hors de sa propre vergue. Et la chute
des voiles est bornée par le **pont réel du modèle**, échantillonné par
`_deckProfile()` — un seul chiffre pour tout le navire ne suffit pas, la Roter
Löwe portant son château arrière neuf mètres au-dessus de son maître-bau : les
basses voiles traversaient la coque.

**Ferler prend la toile, pas les espars.** `setTrim` n'a jamais touché au groupe
entier, seulement à `this.canvases`. Un navire à sec de toile garde ses vergues en
croix et sa bôme en place — et pour un modèle importé, masquer le groupe lui
arracherait son gréement, puisque ses propres vergues y vivent désormais.

**Et elle se roule maintenant, au lieu de disparaître.**

La fraction de toile établie vit dans le **solveur** et non dans le modèle, et
c'est tout le point : une voile qu'on rentre n'est pas une animation avec une
force posée à côté, c'est **moins de surface en l'air**. Portée comme une
fraction, la pression aérodynamique est simplement multipliée par elle — moitié
de toile, moitié de poussée — et l'image ne peut pas diverger de la physique
puisqu'il n'y a qu'un seul nombre. Le modèle le lit pour savoir jusqu'où
enrouler le tissu. Mesuré sur la goélette pendant un ferlage : 0,99 de toile
pour 55 kN, 0,66 pour 37, 0,33 pour 19, 0 pour 0.

**Une seule règle sert les trois gréements**, et c'est un heureux hasard de
l'ordre dans lequel leurs coins avaient été donnés : chaque sommet remonte vers
celui qui lui fait face **au rang zéro**. Ce rang est la têtière d'un carré, qui
se rassemble donc sur sa vergue comme le feraient ses cargues ; c'est le point
d'une voile aurique, qui descend sur sa bôme ; et c'est la ligne d'amure d'un
foc, qui court le long de son étai. Chacun fait ce que son gréement fait
réellement.

**Jamais tout à fait à rien, cependant.** Rabattue exactement sur le rang, la
voile n'a plus aucune surface et disparaît, là où une voile ferlée est un gros
rouleau de toile qu'on voit d'un mille. Il reste six pour cent de sa chute, et
un **bourrelet** qui est le plus épais quand elle est complètement rentrée —
l'inverse du creux, qui lui s'annule. Sans ce bourrelet le reste est un ruban
plat : géométriquement une voile ferlée, visuellement un bout de ruban adhésif.
Mesuré sur la Roter Löwe à sec de toile : 0,55 m de haut pour 1,47 m
d'épaisseur.

Elle est donc **toujours dessinée**. La masquer à zéro était l'ancien
comportement et jetait précisément ce pour quoi le reliquat existe — quatre-
vingt-un sommets par voile, il n'y a rien à économiser. Trois secondes pour
ferler ou établir, ce qui est vif pour un vrai équipage et juste pour une
interface qui ne doit pas paraître collée.

**L'échelle d'un modèle se prend sur la coque seule.** `_hullScale()` cherche le
maillage le plus volumineux — les espars sont longs mais n'enferment presque
rien — et ramène *sa* longueur à `spec.L`. Mesurer l'objet entier comptait le
beaupré et les vergues comme du navire : la coque de la Roter Löwe sortait à
47,5 m là où le solveur en flottait 60, et tout ce qui découle des dimensions
annoncées débordait d'autant. Le collier d'écume, qui suit l'ellipse
`spec.L × spec.B`, dépassait ainsi de 6,3 m à l'étrave et à l'étambot.

La largeur, elle, n'est pas ajustée : elle suit les proportions propres du
modèle. La Roter Löwe fait 15,6 m au maître-bau pour 14,5 m annoncés, donc le
collier passe un demi-mètre en dedans du bordé. Corriger cela demanderait une
mise à l'échelle non uniforme, qui déformerait la carène.

**Les voiles sont des surfaces, pas des feuilles.** Un quadrilatère plat a une
normale constante : une seule teinte sur toute la toile, et nulle part où la
lumière tourne — ça lit comme du carton, quel que soit l'éclairage.
`_sailSurface()` construit donc une grille sur les quatre coins et la pousse le
long de sa normale. Le creux n'est pas figé dans la géométrie :
`setSailShape()` le règle à chaque image sur `sailLoad`, la pression que le
solveur calcule **déjà** pour propulser le navire. La voile se gonfle donc en se
bordant et se vide dès qu'on choque, sans seconde règle à tenir en accord avec
la première. Le champ `belly` d'une fiche est le creux en mètres à pleine charge
(3,3 m sur la Roter Löwe, soit 16 % de la largeur des basses voiles).

**Mais le creux n'est pas une bulle.** La première écriture le posait en
`sin(πu)·sin(πv)` : creux au centre, nul sur les quatre bords. Deux fautes, et
elles valaient pour toutes les voiles du gréement.

Une voile pleine est creuse **bien en avant du milieu de sa corde**, aux quatre
dixièmes environ, parce que c'est là que l'air tourne — pas à la moitié. Et les
seuls bords plats sont ceux **réellement lacés** à un espar ou à un étai : le
point d'une voile carrée n'est tenu que par ses deux points d'écoute, donc elle
porte du creux jusqu'à la ralingue au lieu de s'y annuler. Épingler ce point à
zéro aplatissait exactement ce que l'œil lit d'un carré.

**Et le creux est HAUT.** Ce n'est pas là qu'un bord libre le mettrait, et ce
sont les écoutes qui en décident : les points d'un carré sont halés en bas et
**en dehors**, sur les bras de la vergue du dessous, si bien que le bas est
étiré le long d'un espar auquel il n'est pas lacé, tandis que la toile juste
sous sa propre vergue n'a rien qui la tire nulle part et sace. Vu de profil,
c'est tout son dessin — pleine en haut, plate en bas. Relevé rang par rang sur
la basse voile de la Roter Löwe, de la têtière au point : 0 · 2,68 · 3,26 ·
**3,27** · 3,10 · 2,77 · 2,28 · 1,63 · 0,82 m. J'avais d'abord mis le creux aux
deux tiers de la chute, soit exactement l'inverse.

**Et la chute suit ce dessin, elle n'en est pas exclue.** Épinglée à zéro, elle
restait une droite plate : de profil, le milieu de la voile décrivait bien sa
courbe et son bord ne décrivait rien. Or une chute de carré n'est tenue qu'à la
patte et au point d'écoute — elle chasse sous le vent avec le reste, en gardant
un peu plus de la moitié du creux du milieu. Relevé : 1,84 m à la ralingue
contre 3,27 m au milieu, à la même hauteur, et la même forme du haut en bas.

**Creuser n'est pas pendre**, et confondre les deux coûte cher. La première
écriture n'avait qu'un jeu d'indicateurs de bords libres, servant aux deux : en
libérant la chute pour qu'elle se creuse, elle se mettait aussi à **pendre**
sur sa propre longueur, ce qu'aucune chute ne fait — l'écoute la raidit. Un
point, lui, n'est tenu qu'à ses deux coins et pend pour de bon. Les deux
notions ont donc leurs propres indicateurs (`hangU0`…), retombant sur ceux du
creux quand on ne dit rien. Chaque voile déclare donc sa **coupe** (`cut`) — où
elle est creuse, lesquels de ses bords sont lacés — et les valeurs par défaut
redonnent l'ancienne bulle, si bien qu'une voile qui ne dit rien ne change pas.
Mesuré sur la frégate : 3,27 m de creux pour 3,3 annoncés, au point v = 0,75, et
encore 79 % du maximum à la ralingue.

**Le creux plein est un COUSSIN, pas une bosse.** Même corrigé de sa position,
un demi-sinus reste bombé à la couronne et s'affaisse doucement en tous sens.
De la toile pleine ne fait pas ça : elle est large et presque plate au milieu,
et ne tourne franchement que dans le dernier huitième de sa largeur, là où la
ralingue la retient. Le remède tient en un exposant **inférieur à un** appliqué
au profil : il soulève tout ce qui n'est pas sur les bords sans déplacer la
crête. À 0,55, un huitième en dedans de la ralingue passe de 0,38 à **0,59** du
creux maximal, et le quart de 0,71 à 0,83 ; le point de ralingue monte de 0,78 à
0,87. Le creux maximal, lui, ne bouge pas — 3,30 m mesurés pour 3,3 annoncés.

Les carrés sont à 0,55, la toile aurique et le foc à 0,80 : une voile à corne
travaille en aile et garde un profil d'aérofoil, ce n'est pas un oreiller.

L'exposant ne porte que sur la **corde**, jamais sur la hauteur. C'est une
forme de *section*, et appliqué aussi le long de la voile il aplatissait la
différence entre une têtière pleine et un point étiré — c'est-à-dire justement
le profil qu'on cherche à lire de profil.

**Et le point s'affaisse.** La toile entre deux points d'écoute est plus longue
que la droite qui les joint : elle sourit entre eux, nulle aux coins qui sont
raidis. C'est la ligne qu'on lit sur un carré avant toute autre, et un point
réglé à la règle trahit une voile dessinée plutôt qu'enverguée. L'affaissement
ne porte que sur les bords **libres** — une chute est raidie par son écoute,
elle ne pend pas — donc la brigantine de la goélette, lacée sur trois bords,
n'en a aucun, tandis que le foc et les carrés en ont un. Il grandit un peu avec
le remplissage — le creux prend du tissu en travers et le rend vers le bas —
mais il reste **bien inférieur à la coupe** : sur un carré il ne fait qu'adoucir
le rond de fond, il ne le retourne jamais en sourire. 0,33 m pleine charge
contre 0,61 m de rond, sur la Roter Löwe.

**Et surtout, les bords sont TAILLÉS CREUX.** C'est ce qui a manqué le plus
longtemps, et ce n'est pas un effet du vent : une voile carrée est envergée sur
une vergue droite, donc sa têtière est une droite, mais tous ses autres bords
sont coupés **en dedans** de la droite. Tant qu'ils restaient réglés à la
règle, on pouvait la creuser et l'ombrer autant qu'on voulait — elle se lisait
comme un rectangle avec un dégradé dessus. La silhouette est ce que l'œil lit
en premier.

**Creux, pas rond**, et c'est toute la différence. Les chutes rentrent à
mi-hauteur pour que la toile passe au clair des haubans et que la ralingue
travaille en ligne droite ; le point est taillé **vers le haut** au milieu —
le rond de fond d'une basse voile, qui existe pour dégager les étais et le mât
au-dessous. Les points les plus larges d'une voile carrée sont donc ses coins,
et sa taille est son endroit le plus étroit. Coupée dans l'autre sens, elle
gonfle entre ses espars comme une taie d'oreiller sur un fil — c'est
exactement ce que donnait la première version, et le tracé de l'utilisateur
l'a dit du premier coup.

Étant affaire de **coupe** et non de pression, tout cela est cuit dans la
géométrie de base : molle, elle garde sa forme. Nul aux pattes, où la vergue la
tient, nul aux points d'écoute halés dans leurs coins. Mesuré sur la Roter Löwe
(vergue de 20,3 m) : chute creusée de **1,0 m** à mi-hauteur, point relevé de
**0,61 m** au milieu par rapport à la ligne de ses écoutes.

Attention, le creux de chute se **compose avec l'effilement du point** (0,86 de
la vergue) : la demi-largeur ne descend pas de la patte au point, elle tombe de
10,17 à 8,27 m puis **remonte** à 8,75. Une valeur de coupe qui paraît
raisonnable dans l'absolu peut être annulée ou doublée par une autre règle de
forme, et seul le relevé du bord réel le dit.

La grille est passée de 8×6 à 8×8 : la forme intéressante est désormais celle
qui court le long de la voile, et six rangs rendaient le creux bas en facettes.

**Ombres portées, et surtout pas de SSAO.** Le SSAO réclame une passe de
profondeur, que three rend avec un matériau de substitution. Or la mer est
déplacée dans son propre vertex shader : la substitution la dessinerait plate,
et l'occlusion serait fausse exactement au contact coque/eau, là où l'œil va
d'abord. Une ombre portée n'exige aucune passe de ce genre et donne davantage :
la toile qui assombrit le pont, la coque qui ombre son propre côté sous le vent,
un mât qui raye la voile derrière lui. Seul le navire y participe — la mer ne
projette ni ne reçoit, pour la même raison. La boîte d'ombre suit le navire
(`aimSun`) : laissée à l'origine, il en sort en une minute et ses ombres
s'arrêtent net. Et `shadow.normalBias` compte plus que `bias` ici, la toile
étant d'épaisseur nulle.

**L'écume suit l'empreinte réelle, plus une ellipse.** `Naval.HULL_GLSL` est
partagé par la mer et la passe d'écume, pour qu'elles ne dessinent jamais deux
navires différents. Il rend une distance **signée, en mètres**, à la vraie
flottaison : demi-largeurs relevées station par station sur le navire lui-même
(son propre maillage si c'est un modèle, `hull-lines.js` s'il est procédural),
portées dans une texture d'une seule ligne, puis distance par la formule de la
boîte. L'ellipse `spec.L × spec.B` passait jusqu'à 2 m en dedans du bordé sur la
moitié de la longueur, et 3,8 m au tableau arrière, où elle s'est effilée en
pointe alors que la coque fait encore près de quatre mètres.

Trois détails qui ont chacun coûté un aller-retour :

- **La bande de mesure va de sous la flottaison au haut de la préceinte**, pas à
  la flottaison seule. Il faut tracer la ligne que l'**œil** lui voit faire dans
  l'eau : une coque évasée surplombe sa propre flottaison — 6,35 m contre 7,7
  sur la Roter Löwe — et un contour pris à la flottaison exacte se dessine sous
  ses propres œuvres mortes, où il devient invisible.
- **Le profil doit être fairé.** Une coque .glb ne porte que quelques centaines
  de sommets : répartis sur soixante-quatre stations, la plupart n'en reçoivent
  qu'un ou deux et le maximum par station ressort en facettes — un collier en
  dents de scie. Trois passes d'un noyau 1-2-1 suffisent.
- **La carène se borne sur son corps de flottaison** (`uHullEnds`), pas sur la
  demi-longueur hors-tout : l'étrave s'élance au-dessus, la voûte surplombe. Pris
  à `spec.L/2`, il restait un filet d'écume filant devant l'étrave, là où le
  profil s'était déjà annulé.

**L'écume ne fabrique pas de lumière, elle en renvoie.** Sa couleur était une
constante presque blanche, `vec3(0,92 0,96 0,98)`, si bien qu'elle était aussi
claire à minuit qu'à midi : un sillage de nuit sortait **lumineux**, seule chose
de l'image à s'éclairer toute seule. De l'écume est blanche parce qu'elle
DIFFUSE, donc elle ne peut jamais être plus claire que ce qui l'éclaire.

Deux termes, et il faut les deux. Le terme de **ciel** est la lumière qui fait
le travail, et il porte déjà l'heure, la saison et le gros temps, `setSun`
reconstruisant la couleur d'horizon chaque fois que le soleil bouge. Le terme
d'**eau** est là parce que la mer garde un pigment **constant** — `uDeep` et
`uShallow` ne suivent pas le soleil — de sorte qu'en n'éclairant que l'écume on
la rendait plus **sombre** que l'eau sur laquelle elle repose, ce qui est le
même défaut retourné. De l'écume est claire *par rapport à la mer d'en dessous*,
à toute heure.

À midi la somme tombe à un centième de l'ancienne constante, donc le plein jour
ne bouge pas ; à minuit c'est une traînée pâle sur une mer sombre. Piège de
mesure au passage : comparer la contribution de l'écume d'une session à l'autre
ne veut rien dire, le champ d'écume étant persistant — quarante secondes de
sillage accumulé contre plusieurs minutes ne donnent pas le même nombre de
pixels blancs, et l'on croit avoir cassé ce qu'on vient de régler.

**Le collier s'éteint vers l'intérieur, la gerbe vers l'arrière.** Deux règles
distinctes, et il ne faut pas les confondre. Le collier se fond *sous* le bordé
(`smoothstep(-1.3, -0.1, gapM)` plutôt qu'un `step`) : l'écume s'accumule contre
la coque et s'épuise dessous, alors qu'un bord interne net se lit comme un
autocollant posé sur l'eau. La gerbe d'étrave, elle, n'est pas une bande sur la
moitié avant mais une moustache au brion et deux ailes balayant vers l'arrière,
dont la crête s'écarte comme la **racine carrée** de la distance en arrière de
l'étrave — c'est ce qui lui donne son aile parabolique au lieu d'un bord droit.
Elle exige de l'erre, et sa rampe `fast` sature à deux fois la vitesse de `way` :
la gerbe continue donc de grossir quand le collier a fini de croître, ce qui fait
la différence entre un navire poussé et un navire simplement à flot. Les ailes
sont déposées **aussi** dans le champ d'écume, sinon le V resterait soudé à
l'étrave au lieu de rester dans l'eau derrière elle.

**`half` est un mot réservé en GLSL.** L'utiliser comme nom de variable ne fait
pas échouer un seul terme : **tout** le fragment shader refuse de compiler, la
mer disparaît et on voit le dôme de ciel à sa place. `node --check` ne peut rien
y voir, le shader n'étant qu'une chaîne de caractères pour lui.

**Le ciel éclaire, il ne fait pas que décorer.** Le navire était éclairé par un
`HemisphereLight` à deux couleurs qui tenait lieu d'un ciel que la scène
dessinait déjà correctement quelques mètres plus loin. `_buildEnvSky()` rend ce
ciel dans une cubemap, la préfiltre et la pose en `scene.environment`. Il appelle
le même `navalSky` et **partage les objets uniformes du dôme** — pas des copies —
donc il n'y a toujours qu'un seul ciel et rien ne peut diverger de ce qu'on voit
au-dessus. Le disque solaire en est volontairement exclu : la `DirectionalLight`
le représente déjà, l'y cuire l'éclairerait deux fois.

Trois choses en découlent. L'ambiante devient **directionnelle** — mesurée à
+6 % du côté du soleil, là où un `HemisphereLight`, qui n'utilise que la
composante verticale de la normale, donne rigoureusement zéro. Le **spéculaire
image** apparaît, ce qu'une lumière hémisphérique ne sait pas produire du tout,
et c'est le gain le plus visible. Et l'ambiante suit désormais seule l'élévation,
la nuit et l'éclair, au lieu d'une approximation réglée à la main. En
contrepartie `_hemiBase` a été divisé par trois : à son ancienne force
l'hémisphère comptait cette lumière une seconde fois et aplatissait justement
l'ombrage directionnel que l'environnement apporte.

Le préfiltrage coûte quelques millisecondes, et le curseur du soleil tire un
événement par pixel de glissement : `refreshEnvironment()` est donc **bridée**,
et un rafraîchissement sauté est rattrapé dans `render()`.

**L'explosion tient dans l'ORDRE des choses, pas dans la boule de feu**
(`explosion.js`, touche `K`). Un éclair parti en un dixième de seconde, une
boule de feu qui grandit vite et meurt en une seconde, de la fumée qui monte et
s'étale pendant vingt, des débris sur de vraies paraboles. Tout ensemble, c'est
un feu d'artifice ; échelonné, les parties lentes survivant aux rapides, c'est
un navire qui saute. Ce sont les **débris** qui donnent l'échelle : l'œil lit la
hauteur qu'ils atteignent et le temps qu'ils mettent à retomber, et aucune boule
de feu ne remplace ça.

**Trois charges, pas une.** Elles partent à quelques mètres et quelques
dixièmes de seconde d'écart — au milieu d'abord, puis sur l'avant, puis bien sur
l'arrière — ce qui se lit comme un navire qui se disloque là où une seule grosse
boule se lit comme une bombe posée à côté de lui. Chacune porte **son propre
éclair**, si bien que la lumière bégaie aussi, et c'est l'essentiel de l'effet.
Les points sont placés dans **son** repère puis portés dans le monde, donc ils
suivent sa gîte et son assiette pendant qu'elle roule. Les écarts sont **bornés
en mètres autant que mis à l'échelle**, sans quoi une goélette exploserait hors
de sa propre coque.

**Les débris de bois sont de VRAIS maillages, pas des sprites.** Un point
lumineux se lit comme une étincelle quelle que soit sa couleur ; ce qui le fait
lire comme un navire qui se disloque, c'est que les morceaux sont **opaques**,
éclairés par le même soleil que la coque, et qu'ils **culbutent** bout sur bout.
Une seule géométrie de planche et un seul matériau pour tous — il n'y a aucune
raison de construire deux douzaines de boîtes par explosion pour les jeter trois
secondes plus tard — et ce matériau reçoit la brume comme le reste, donc les
éclats respirent le même air que le bordé dont ils viennent.

Une conséquence gratuite : le bois étant opaque et la mer aussi, une planche qui
retombe **disparaît d'elle-même derrière l'eau**. La gerbe ne coûte rien parce
qu'il n'y a pas de gerbe à écrire.

Tout le reste est en billboards. Du feu volumétrique serait des jours de travail pour un
événement de trois secondes, et une douzaine de sprites bien cadencés se lisent
mieux qu'un mauvais volume. L'éclair réutilise la **foudre** déjà câblée, qui
éclaire pont, toile et ciel ensemble — exactement ce que fait une soute qui
saute. Les textures sont dessinées sur un canvas, une page publiée ne pouvant
pas aller chercher d'image.

**Et le naufrage qui suit n'est pas un cas particulier** : `blowUp()` ouvre
simplement les cinq compartiments d'un coup, souffle les pompes et l'admet déjà
à un cinquième — une explosion ne la fait pas commencer à embarquer depuis zéro.
Elle coule en quelques dizaines de secondes parce que l'arithmétique le dit, pas
parce qu'on l'a décidé. Mesuré sur la frégate : 1 206 t à 0,8 s, 1 987 t à 9 s.

**L'échouage se sonde en TROIS points, jamais sur les sondes de carène.**
`heightAt` balaie la grille des îles ; l'appeler trois cents fois par sous-pas
coûterait plus cher que tout le solveur réuni. L'étrave, le milieu et l'étambot
suffisent à tout ce qui compte : elle s'ensable par l'avant sur une plage en
pente douce, pivote sur un haut-fond qui la prend par le travers, ou s'assoit
sur une quille droite.

Le fond répond comme un **ressort raide amorti, appliqué AU point de contact** —
elle se soulève, gîte et embarde exactement comme la géométrie l'impose. Rien ne
décide qu'elle est échouée ; les forces le font, comme rien ne décide qu'elle
flotte. Et talonner en vitesse l'**ouvre au point de choc**, ce qui referme
enfin la boucle : l'échouage devient une vraie cause de l'envahissement déjà
écrit, au lieu du raccord manquant qu'il était.

Mesuré sur le chaland lancé à 8,7 nds sur une plage : premier contact à 38,6 s à
4,6 nds, une voie d'eau ouverte au même instant, arrêt complet en quinze
secondes, puis 12 t embarquées en deux minutes. La pénétration **oscille entre 0
et 0,25 m** au rythme de la houle — elle tape sur le banc, ce que je n'avais pas
prévu et qui tombe juste.

**Le naufrage n'est pas scripté, c'est du poids mal placé.** Méthode du *poids
ajouté* : l'eau embarquée est une masse, à l'endroit où elle repose. Rien ne
décide qu'elle coule — elle sombre quand ce poids dépasse ce que sa carène peut
déplacer. La grille de sondes fait déjà tout le reste : assiette, gîte et
enfoncement en découlent sans une ligne de plus.

Les compartiments (`Naval.Config.NCOMP`, cinq) sont découpés **sur les sondes
elles-mêmes**, donc leur capacité est du vrai volume de coque, mesuré sur le
même plan de formes que tout le reste. Ils sont numérotés **depuis l'étambot** :
le compartiment 0 est à l'arrière, le 4 à l'étrave.

Trois choses portent tout le comportement, et il ne faut pas les défaire :

- **L'entrée d'eau suit Torricelli**, `v = √(2gh)`. Un trou profond emplit bien
  plus vite qu'un trou près de la flottaison, et surtout la charge `h` **grandit
  à mesure qu'elle s'enfonce** : c'est l'emballement qui noie réellement un
  navire, et il est gratuit.
- **L'envahissement par le pont** prend le relais dès qu'un livet passe sous
  l'eau. C'est presque toujours lui qui achève, pas la voie d'eau initiale.
  Mesuré sur la Roter Löwe, une seule brèche de 0,30 m², pompes arrêtées : 5
  minutes pour 566 t et 3° d'assiette, puis les deux dernières minutes la font
  passer de −9° à −58° et elle sombre à 12,1 min, par l'arrière.
- **L'eau se met au fond, et court à la bande basse.** Son centre monte avec le
  remplissage, donc un fond d'eau est du lest et la raidit, tandis qu'une masse
  haute la chavire. Et un compartiment **à moitié plein** a une carène liquide
  (`4f(1−f)`, nulle à vide comme à plein) qui glisse sous le vent et combat le
  redressement — c'est pourquoi remplir complètement un compartiment est un vrai
  remède.

**Les pompes se dosent à la mesure, pas au calcul**, l'entrée d'eau dépendant de
la profondeur à laquelle elle finit par s'asseoir sur son trou. Réglées à
`6·10⁻⁵ × volume de coque` pour que **une** voie d'eau soit rattrapable et deux
non : sur la frégate, 0,343 t/s de pompes contre 0 t embarquée à une brèche,
0,334 t/s de gain à deux, 0,705 à trois. C'est délibérément généreux au regard de
l'histoire — une pompe à chaîne faisait de l'ordre d'une tonne par minute, et les
navires coulaient précisément parce qu'on ne suivait pas ; à cinq fois ça, le
contrôle des avaries devient une décision plutôt qu'une formalité.

**La carène liquide est une remontée du centre de gravité, pas un déplacement de
centroïde.** Je l'avais d'abord écrite comme de l'eau qui court à la bande basse,
proportionnellement à la gîte. C'était **faux, pas seulement faible** : mesuré sur
la goélette, ça déplaçait le centre de gravité de 2 cm et produisait 5 t·m, deux
ordres de grandeur sous le moment redresseur. Pire, l'eau de fond abaissait G de
23 cm — elle s'envahissait, devenait plus **raide**, et sombrait bolt upright.

Le vrai effet est la correction classique : une **remontée virtuelle de G** de
`Σ(ρ·i)/Δ`, où `i = l·b³/12` est le moment quadratique de la surface libre. Elle
ne dépend **pas de l'angle de gîte**, ce qui est précisément ce qui la rend
mortelle — le navire est déjà instable avant d'avoir donné de la bande. Et elle
va comme le **cube de la largeur**, d'où le cloisonnement longitudinal des vrais
navires : un compartiment large est pire que trois étroits contenant la même eau.

Mesuré sur la goélette (GM à sec 1,04 m), `freeSurface` à 1 contre 0 :

| situation | sans | avec |
|---|---|---|
| à la cape, voiles ferlées, force 6,5 | 11,9° | 14,7° |
| pressée sous voiles, force 7,5 | 14,9° | **40,7°** |

La remontée de G atteint 0,92 m contre 1,04 m de GM. Sous voiles elle **triple
la gîte**, se couche à 40°, met son livet sous l'eau et sombre en 1,7 minute.

Ce qu'on n'observe toujours pas, et qu'il ne faut pas confondre avec un défaut :
elle ne se **retourne** pas au-delà de 90°. Passé 40° son pont est sous l'eau et
l'envahissement par le pont l'achève en quelques secondes — elle se noie avant
d'avoir le temps de chavirer, ce qui est le sort réel de la plupart des navires
envahis.

**On voit à travers l'eau, et l'extinction est par canal** (`underwater.js`). La
mer était opaque : un navire qui sombrait ne s'enfonçait pas, il était *coupé* à
la flottaison. La mer ne peut pas savoir seule à quelle distance derrière elle se
trouve la coque, donc le navire est redessiné une fois, seul, dans ses propres
matériaux, vers une cible couleur + texture de profondeur ; la mer lit les deux
et en déduit l'épaisseur d'eau traversée.

Il est déjà isolé sur sa couche de rendu pour l'occlusion, donc cela coûte **un
seul dessin de plus d'un seul modèle** — et surtout la mer reste un objet opaque
testé en profondeur, au lieu de devenir transparente avec tous les problèmes de
tri que cela entraînerait.

L'extinction est **par canal** (`uAbsorb`, 0,34 / 0,13 / 0,085 par mètre) : le
rouge disparaît en trois mètres, le bleu porte trois fois plus loin. C'est toute
la raison pour laquelle ça se lit comme de l'eau et non comme du brouillard — un
coefficient gris unique la ferait virer au gris, ce que fait la brume, pas la
mer.

**La perte d'un navire se mesure sur son point le plus haut.** Passé dix mètres
sous la surface elle disparaît, la caméra bascule en vue fixe et un message le
dit. Mais le seuil porte sur le **sommet** du navire, jamais sur l'origine de sa
coque : elle sombre presque à la verticale, l'étambot le premier, et descend
l'étrave et le gréement encore en l'air. Relevé sur la Roter Löwe, avec l'origine
à dix mètres sous l'eau il restait **trente-trois mètres de mâture dressés au-
dessus de la mer** — l'escamoter là aurait fait s'évanouir ses mâts en pleine
vue. Sur le point le plus haut, elle est réellement partie. Coût : une boîte
englobante par image, et seulement une fois `foundered`.

La caméra fixe reçoit alors la **hauteur de la mer** comme cible (`plant(aimY)`)
et non la position de l'épave : braquée sur cette dernière elle plongerait du nez
sur cent mètres d'eau vide au lieu de tenir le carré de mer qui l'a engloutie.

**Un attribut `hidden` ne suffit pas si la classe pose un `display`.** La ligne
d'avarie porte `.allure`, qui vaut `display:flex` et l'emporte sur la règle
`[hidden]` du navigateur : elle restait donc affichée après réparation, à
annoncer « SOMBRÉ » sur un navire intact. Il faut une règle
`.allure.damage[hidden]{display:none}` explicite. Le piège vaut pour toute ligne
qu'on masque par attribut.

**Sous l'eau, trois choses changent, et en oublier une trahit tout.**

D'abord **la mer avait un seul côté**. Le plan était en `FrontSide` : vu d'en
dessous il n'existait pas, et on voyait le ciel au travers. Il est en
`DoubleSide`, et `gl_FrontFacing` décide de ce qu'on dessine — un nageur et une
vigie sont servis par le même matériau. Vue de dessous, la surface n'est pas une
mer mais un **plafond** : rien du travail de reflet ne s'y applique, le Fresnel
joue à l'envers, et ce qu'on voit est un couvercle argenté avec le soleil qui
brûle au travers en une tache.

Ensuite **le dôme de ciel devient l'eau profonde**. Le laisser dessiner le ciel
mettrait un horizon à l'intérieur de la mer.

Enfin **l'extinction remplace la brume**, par canal, avec l'absorption que la
mer emploie déjà pour montrer une coque coulée à travers la surface — mais **à
quatre dixièmes**. Ce n'est pas un truquage : `uAbsorb` a été mesuré pour un
regard qui traverse la surface *deux fois*, en descendant vers la coque et en
remontant vers l'œil. À l'horizontale il ne la traverse qu'une, donc la même eau
porte deux fois plus loin. À pleine force, une coque à vingt mètres n'existait
tout simplement pas.

**La fenêtre de Snell fait tout le travail.** Vu d'en dessous, l'hémisphère
entier du ciel se comprime dans un cône de **97°** — tout, d'un horizon à
l'autre, dans ce seul disque clair. Au-delà, l'angle dépasse la réflexion totale
et la surface devient un miroir. C'est ce cercle et son bord argenté qui font
lire une image comme étant *sous* l'eau plutôt que simplement bleue. La fonction
`refract` fait le calcul et rend le vecteur nul en réflexion totale, ce qui est
exactement le test d'être hors de la fenêtre.

**Deux ordres à ne pas intervertir.** La branche « vue de dessous » doit passer
**avant** le mélange du navire : écrite après, elle l'écrasait, et aucun
bâtiment n'était visible depuis l'eau — c'était précisément le symptôme. Et ce
qui sépare la surface de la coque est de l'**eau** quand on la regarde d'en haut
mais de l'**air** quand on la regarde d'en bas : un bateau vu par la fenêtre
n'est pas derrière des mètres de mer, il est simplement au-dessus. L'atténuer
comme s'il était noyé le teintait en bleu dès dix mètres de franc-bord, d'où
l'absorption ramenée à 2 % dans ce cas.

Le seuil se prend contre **la vague à la caméra**, pas contre le niveau moyen :
une crête qui passe met une vigie de pont bas sous l'eau puis l'en sort, et
c'est justement le moment qui vaut d'être vu. Un demi-mètre de lissage évite que
le passage clignote quand la surface chasse autour de l'objectif.

**La réfraction doit reporter l'ondulation, pas effacer l'épave.** Sans elle la
coque immergée se lit comme un autocollant vu à travers une vitre plate. Mais
c'est un effet qu'on rate par excès bien plus facilement que par défaut, et la
première version l'a raté.

**L'échelle se dérive, elle ne se devine pas.** Snell dévie le rayon d'environ
`(1 − 1/n)` de la pente, soit un quart pour l'eau de mer, et le déplacement à la
coque vaut cet angle multiplié par la profondeur. Plutôt que de deviner comment
cela tombe à l'écran, le décalage est construit en **mètres monde puis
reprojeté** : rapport d'image, champ et orientation de la caméra se règlent alors
tout seuls. `uProj` est recopié à chaque image, la passerelle et la caméra fixe
zoomant l'objectif plutôt que de bouger.

La première écriture était un bricolage en espace écran avec un coefficient de
**26 — cent fois trop grand**. Le décalage dépassait la moitié de l'écran, presque
tous les échantillons manquaient la coque, et le repli ci-dessous la
reconstruisait à partir de pixels sans rapport. Ça ne se lisait pas comme de
l'eau mais comme du bruit détruisant précisément ce qu'on cherchait à regarder.
Corrigé, `uRefract` vaut **1 = la physique**, et donne 3,3 px de déplacement à
60 m par mer 3 : une ondulation, pas une bavure. Écart moyen tombé de 46 à 27
sur 255.

Deux points de méthode. Il faut un **premier échantillon droit** rien que pour
connaître l'épaisseur avant de savoir de combien dévier — d'où deux lectures de
profondeur. Et si le rayon dévié tombe sur quelque chose situé **devant** l'eau,
il a atteint les œuvres mortes : on retombe alors sur l'échantillon droit, sinon
le pavois se retrouve étalé sur la mer.

Enfin le terme de profondeur **sature à cinq mètres**. Un rayon plus long
continue en toute rigueur de s'écarter davantage, mais au-delà de quelques mètres
le déplacement suffit à effacer sa silhouette — et elle doit rester lisible
pendant qu'elle s'efface. C'est un écart délibéré à la physique, au profit de ce
qu'on est venu voir.

**Le collier d'écume doit s'éteindre avec elle.** `uShipAfloat` tombe de 1 à 0
entre 78 % et 95 % d'immersion et multiplie le collier, la gerbe et le dépôt dans
le champ d'écume. Sans lui, un anneau d'écume reste à la surface au-dessus d'une
épave, avec rien dessous — et le champ étant persistant, il y traînerait encore
une trentaine de secondes.

**Une voie d'eau s'aggrave, elle ne se multiplie pas.** `worsenBreach()` double
l'aire du même trou à chaque appel et le laisse gagner les compartiments voisins
une fois passée une largeur de bordé, plutôt que d'ouvrir des trous indépendants :
une coque ne perce pas en cinq endroits à la fois, une couture cède et travaille.
Six pressions mènent la frégate au fond en 5,2 minutes, contre plus d'une
demi-heure avec des brèches séparées de taille fixe.

**Les étoiles vivent dans `navalSky`, donc la mer les reflète.** Elles sont
posées sur un réseau de cellules du **vecteur direction**, pas sur une grille de
latitude et longitude — celle-ci les entasserait aux pôles et laisserait une
calvitie au zénith. Elles sont ajoutées **avant** le mélange vers l'horizon, si
bien qu'elles s'amincissent dans la brume comme les vraies au lieu de flotter
par-dessus.

Une étoile plus étroite qu'un pixel est une tempête de scintillement garantie
dès que la caméra tourne : c'est le **même piège d'aliasing** que les rides de
la mer, et le remède est le même — être plus large que le pas d'échantillonnage,
pas plus brillant. D'où une vraie largeur et un `smoothstep`, jamais un point.

**La lanterne, ce sont DEUX lueurs pour deux métiers.** La proche est un sprite
de taille réelle, qui grossit quand on accoste et se lit comme un fanal pendu au
couronnement. La lointaine **ne s'atténue pas avec la distance**
(`sizeAttenuation:false`) : un fanal de taille honnête fait moins d'un pixel à
deux milles et disparaît, ce qui est exactement le contraire de ce à quoi sert
un feu. Celle-là est le repère de position, et tient quelques pixels quelle que
soit la distance.

**Le profil de pont est en dents de scie, il faut le lire au maximum.** Sur la
Roter Löwe il donne 18,4 puis 12,4 puis 5,3 m d'une station à l'autre, les cases
chevauchant ses galeries et ses rambardes ouvertes. Échantillonner **une seule**
station a fait tomber la lanterne dans un creux, cinq mètres sous son
couronnement et un peu trop en avant : elle la portait à l'intérieur de son
propre château arrière. La hauteur se prend donc au maximum sur la tranche
arrière, jamais à une station. Le même piège guette tout ce qu'on voudra poser
sur le pont d'un modèle importé.

Elle bat — deux sinusoïdes lentes déphasées, une seule se lirait comme une
pulsation régulière. C'est ce qui l'empêche de ressembler à un marqueur
d'interface plutôt qu'à une mèche dans une lanterne à corne. Sa texture est
**dessinée sur un canvas**, jamais chargée : une page publiée ne peut pas aller
chercher une image locale.

**Le pavillon montre le vent, les voiles montrent le réglage.** Un pavillon blanc
uni est envergué à la tête du grand mât, trouvé par la même lecture de forme que
les vergues : sur un modèle, la pièce la plus haute, bien plus haute qu'épaisse
et sur l'axe ; sur un navire procédural, simplement son plus grand mât. Un
bâtiment sans mât dans son modèle n'en porte pas, ce qui est le comportement
voulu — il n'a pas de drisse.

**Et il peut être noir.** Une fiche déclare son pavillon dans son
`appearance.ensign` : `"jolly"` pour la tête de mort, ou une couleur pour un
pavillon uni. Rien d'autre à toucher — le mât est trouvé par la même lecture de
forme, quel que soit le navire.

Le motif est **dessiné sur un canvas, jamais chargé**, et ce n'est pas une
préférence : une page publiée ne peut pas aller chercher une image, donc les
seules images de ce projet sont celles qu'il dessine — comme la lanterne, la
fumée et l'embrun — ou celles qui vivent *dans* un `.glb`, dont le build emporte
les octets en base64.

Dessiné **gros exprès**. Un motif lisible sur un écran à bout de bras est une
tache grise sur un pavillon à un demi-mille : à la taille où cette chose est
réellement vue, seules les grandes formes survivent, donc le crâne est large,
les os sont épais, et il n'y a aucun détail qui ne se lirait pas comme une
bavure.

Deux détails qui ont coûté un aller-retour. Le pavillon **n'avait pas de
coordonnées de texture** : ses `u` et `v` étaient calculés pour l'ondulation puis
jetés. Et l'émissif qui empêche un pavillon blanc de virer au gris contre un
ciel clair rendrait un pavillon noir **anthracite** — un pavillon sale et non un
pavillon sinistre : le motif apporte donc le sien, bien plus faible.

Il porte sur le vent **apparent**, comme tout ce qui flotte depuis un pont en
mouvement, et son lacet se déduit directement de ce que le solveur connaît déjà :
`atan2(−tack·sin β, −cos β)`, soit le bout au vent tourné de 180°. C'est
l'unique instrument qui dise où est le vent plutôt que où l'on a brassé, et il
bascule donc avant les voiles quand elle lofe. Sous 8 m/s l'ondulation s'éteint
et le pavillon **retombe le long du mât** en perdant sa longueur : à 2,7 m/s,
0,38 m d'ondulation pour 1,58 m d'affaissement ; à 17,9 m/s, 1,13 m d'ondulation
et plus d'affaissement du tout.

**L'occlusion ambiante ne porte que sur le navire** (`ssao.js`). Le SSAO réclame
une passe de profondeur, que three rend avec un matériau de substitution : la mer
étant déplacée dans son propre vertex shader, elle y serait dessinée plate et
l'occlusion serait fausse au contact coque/eau. Restreindre la passe au navire
lève l'objection — il est de la géométrie ordinaire — et coûte bien moins cher.
Il est isolé par une **couche de rendu** (`Naval.SHIP_LAYER`), jamais en masquant
le reste : masquer puis restaurer un graphe deux fois par image est plus lent et
se laisse facilement abandonner dans un mauvais état.

Le résultat est relu dans les matériaux du navire au chunk **`<aomap_fragment>`**,
et c'est le point important : three n'y multiplie que la lumière **indirecte**.
L'occlusion appartient à l'ambiante, pas au soleil — une planche au fond d'une
écoutille qu'un rayon atteint encore reste pleinement éclairée, elle ne perd que
le ciel. Multiplier la couleur finale y peindrait du gris jusque dans le soleil,
et ça se lit comme de la saleté.

Demi-résolution partout, l'occlusion étant basse fréquence et le flou qui suit
jetant de toute façon le détail supplémentaire. Mesuré sur la Roter Löwe :
occlusion moyenne 0,90 et minimum 0,53 au fond des creux, sur 5,3 % de l'image —
soit exactement sa surface à l'écran. Surcoût de soumission relevé à 0,10 ms par
image.

**`settle()` aussi avait un effet de bord**, de la même famille que `setSun()`.
Il aplatit la mer — nécessaire, la coque devant trouver ses lignes sans qu'une
houle la secoue — mais il la laissait plate. `commission()` masquait la faute en
appelant `refreshSea()` juste après, si bien qu'elle n'est apparue qu'avec le
**deuxième** appelant : mettre une coque à l'eau depuis le panneau Flotte
aplatissait la mer définitivement, la console continuant d'afficher force 6 et
plein creux au-dessus d'un lac. `setSeaState` retient donc son état, et `settle`
le remet en partant — aucun appelant n'a plus à le savoir. Leçon générale : un
effet de bord qu'un seul appelant compense n'est pas corrigé, il est caché.

**Attention en mesurant : `setSun()` a des effets de bord.** Il recalcule
`sun.intensity`, `hemi.intensity` **et** rappelle `refreshEnvironment()`, qui
libère la texture d'environnement précédente. Un banc de mesure qui éteint une
lumière puis appelle `setSun` la rallume ; un banc qui garde une poignée sur
`scene.environment` à travers un rafraîchissement pointe sur une texture morte.
Les deux m'ont fait conclure trois fois de suite que l'éclairage par le ciel ne
fonctionnait pas alors que seul le banc était faux. Appliquer la configuration
**après** `setSun`, et relire `scene.environment` après chaque rafraîchissement.

**La toile est éclairée à travers.** Le soleil derrière une voile, l'essentiel
de ce qu'on voit n'a jamais touché la face avant : c'est passé par le tissage, et
la voile est plus claire que tout ce qui est éclairé de face, vergues en barres
sombres dessus. Un matériau ordinaire ne sait pas faire ça — sa face arrière
devient noire. `Naval.applySailLight` ajoute le lobe transmis, le même que la
mer utilise pour la lumière traversant une crête (`uSSS`) : maximal quand œil,
toile et soleil sont alignés, et seulement là où le soleil est sur la face qu'on
**ne** regarde pas. La normale est prise brute sur la géométrie, pas la normale
retournée vers l'observateur, pour que les deux faces répondent pareil. Mesuré à
contre-jour contre plein feu : 237 contre 168 de luminance, et 93 à 60° du plan,
la transmission retombant bien avec l'angle.

**Les patches de matériau se chaînent, ils ne s'écrasent pas.** `applyHaze` et
`applySailLight` s'appliquent au même matériau. Chacun conserve le
`onBeforeCompile` précédent et l'appelle en premier ; la brume doit passer en
**dernier**, étant l'air devant tout le reste. Deux conséquences pénibles :

- Chacun doit pouvoir déclarer `uCam` et `uSun` sans savoir si l'autre l'a fait.
  Deux déclarations sont une redéfinition et **tout** le fragment shader échoue.
  D'où `Naval.SUN_UNIFORMS_GLSL`, sous garde de préprocesseur.
- Three met les programmes en cache sous `customProgramCacheKey()`, qui vaut par
  défaut le **texte source** de `onBeforeCompile`. Le texte de la fermeture de
  brume est identique pour tous les matériaux — la variable capturée qui les
  distingue n'y apparaît pas. Sans clé propre, la voile reçoit le programme de
  la coque et son code n'est jamais compilé : le patch est correct, se compose
  correctement, et ne s'exécute jamais, sans le moindre message. `applySailLight`
  ajoute donc `|sail-translucent` à la clé.

**Textures PBR.** Elles fonctionnent, et la Roter Löwe s'en sert déjà : son
matériau `hull` porte un `baseColorTexture` et ses maillages un `TEXCOORD_0`.
La condition est que l'image vive **dans** le `.glb` : le build en emporte les
octets en base64 et `GLTFLoader.parse()` lit tout depuis la mémoire. Une texture
en fichier image séparé ne marchera jamais — la politique de sécurité bloque le
`fetch` d'un fichier local, et le build n'embarque que les `.glb`. `normalMap`,
`roughnessMap` et `metalnessMap` passent par le même chemin.

**Le compas : c'est la CARTE qui tourne.** La ligne de foi reste en haut et la
rose pivote de `−cap` dessous, comme dans un habitacle — on lit ce qui passe
sous la marque. Faire tourner le navire au-dessus d'une rose fixe donnerait une
carte de navigation, pas un compas. Pas d'aiguille non plus : sur un compas à
carte, la carte *est* l'aimant, et une aiguille dessinée par-dessus est à la
fois fausse et illisible en travers des chiffres. Le N rouge porte le nord seul.
Les trente-six graduations sont construites en JavaScript plutôt qu'écrites dans
le balisage — trente-six occasions de se tromper d'une.

**Réglage des voiles.** Le modèle ne donne aucun retour lisible : à 45° de vent
apparent, des écoutes à 40° ne laissent que 5° d'incidence, donc `CL` s'effondre
et la poussée tombe au cinquième — sans que rien ne l'annonce, la console
affichant « voiles établies » et un nombre de kN plausible. D'où le repère vert
sur la barre d'écoutes, à `optimalAoA`, et la touche `T` qui y borde
directement. L'écoute initiale n'est plus une constante : elle est prise sur le
repère à la **première image** après l'armement, car `settle()` fait flotter le
navire dans un calme plat et le vent apparent n'existe qu'une fois `refreshSea()`
passé.

**Le banc tient dans un bouton** (`J`, ou la ligne « Débogage » du panneau
Flotte). Toute mesure d'artillerie de ce projet a commencé de la même façon :
une seconde coque de la même classe, arrêtée, parallèle, voiles ferlées, à une
distance connue par le travers, et les deux réparées entre deux coups. Le monter
à la main prenait une douzaine de lignes à chaque fois et se trompait une fois
sur trois — la mauvaise batterie, un navire encore sur son erre, une barre
automatique qui emmenait tranquillement la cible hors du banc.

Trois choses en font un banc plutôt que deux navires qui se trouvent près l'un
de l'autre, et toutes les trois sont faciles à oublier :

- **les deux sont ARRÊTÉS**, toile ferlée. Une cible qui dérive transforme une
  mesure reproductible en anecdote ;
- **on retire sa barre à la conserve.** Elle chasserait le navire à la roue, ce
  qui est exactement ce pour quoi elle est faite et exactement ce qui ruine
  l'expérience ;
- **elle se place à l'opposé de l'objectif**, pour qu'une seule vue les tienne
  tous les deux — et de quel côté est **demandé** plutôt que supposé : la caméra
  est plantée d'abord, son ancre lue, et la conserve va sur l'autre bord.

Deux pièges dans ce dernier point, et ils se sont présentés l'un après l'autre.
Écrit à l'envers — « elle se met à bâbord, puisque la caméra fixe se plante à
tribord » — c'était juste sur le gréement et faux à l'écran, et elle sortait à
moitié derrière la console de mer. Puis, corrigé, `setMode(3)` appelé pour un
mode où l'on était déjà rendait la main **sans avoir planté** : l'ancre valait
encore l'origine, le produit scalaire sortait nul, et la conserve était postée du
côté de l'objectif — un seul navire à l'écran. On appelle donc `plant()` sans
détour, et un garde-fou traite « composante par le travers nulle » comme « la
question n'a pas eu de réponse ».

Elle est une **copie du navire qu'on mène** plutôt qu'un galion écrit en dur :
deux exemplaires de ce qu'on est en train d'essayer est presque toujours ce
qu'on voulait dire.

**Débogage.** `Naval.app` expose les instances vivantes (`stage`, `ocean`,
`foam`, `physics`, `ship`, `cam`, `hud`) depuis la console. `Naval.app.stage.strike()`
déclenche un éclair à la demande. Plusieurs bugs de ce projet ont été longs à
cerner faute de pouvoir inspecter quoi que ce soit à l'exécution.

**Nuages : l'échelle du bruit est tout.** Le premier jet lisait le bruit sur
`dir.xz/up*0.055`, ce qui fait tenir **tout le ciel visible dans quelques
centièmes d'unité de bruit** : le motif y est quasi constant, et ce qui en sort
est un voile pâle uniforme, pas des nuages. Le bruit a des motifs d'environ une
unité, donc le ciel doit en couvrir une dizaine. Le piège s'est doublé d'un
piège de mesure : un A/B au pixel donnait « 30 743 px changés, écart moyen
9/255 », que j'ai lu comme « effet présent mais faible » alors que c'est la
signature exacte d'un bruit presque constant. Un écart faible et **étalé
partout** ne dit pas « trop discret », il dit « pas de structure ».

Le caractère vient ensuite de l'**anisotropie** : le bruit est lu quatre fois
plus fin en travers de la traînée que dans sa longueur (`vec2(q.x*4.2,
q.y*17.0)`). Isotrope, on obtient un ciel pommelé — vrai, mais couvert. Étiré,
on obtient des cirrus, et c'est ce qu'on veut par beau temps. Le seuil est pris
haut dans l'histogramme pour ne laisser passer que les crêtes, et la couverture
par défaut est de 12 % : du bleu, quelques traînées. Le curseur « Nuages » de la
console de mer va jusqu'au couvert.

**Et ils s'éteignent la nuit.** Un nuage n'a pas de lumière à lui : on ne le
voit que parce que le soleil est dessus, donc quand celui-ci passe sous
l'horizon il n'y a plus rien à voir et le ciel est simplement noir. Éclairés
toute la nuit ils étaient la seule chose de l'image à briller d'elle-même —
même faute que l'écume, même remède. L'extinction réemploie le smoothstep qui
fait sortir les étoiles, un peu plus bas pour que le nuage soit parti quand
elles sont franchement là : c'est le même événement vu des deux côtés, et lui
écrire un second seuil laisserait les deux dériver. Vérifié : à −12° de soleil,
couvert plein contre ciel clair, **zéro pixel d'écart**.

**Baisser le curseur ne baissait pas le ciel**, et c'est le piège de ce réglage.
La bande du seuil est large, donc déplacer son bord fait glisser beaucoup de
bruit au travers, mais lentement : passer de 12 % à 6 % ne retirait que 14 % de
nuage — 16,3 % du ciel couvert contre 14,0 — là où on en voulait la moitié.
C'est le **plancher** qu'il faut relever, la pente montant avec lui pour que le
couvert plein retombe exactement où il était. Réglé à `smoothstep(0,93 - amt·0,65, 0,99 - amt·0,36, f)` :
8,75 % du ciel au défaut contre 16,26 avant, soit la moitié, et le curseur va
toujours jusqu'au couvert.

**Piège de mesure, et il a coûté trois essais.** Lire les pixels après
`stage.render()` donne n'importe quoi : le rendu laisse une autre cible liée —
il y a la passe d'eau transparente, l'occlusion et la réflexion — et l'on relit
celle-là. Un ciel de nuit ressortait en bleu de plein jour, ce qui a fait
soupçonner le shader pendant un moment alors que seul le banc était faux. Il
faut `gl.bindFramebuffer(gl.FRAMEBUFFER, null)` avant de lire. Une fois
rebasculé, deux lectures de suite du même réglage donnent **0 %** d'écart, ce
qui est la vérification qu'il fallait faire d'abord.

Comme le reste du ciel, ils n'existent **qu'une fois** : `ocean.js` prend les
objets uniformes `uCloud`/`uSkyTime` de `stage.skyUniforms`, pas des copies,
donc rien ne peut diverger de ce qu'on voit au-dessus. La mer, elle, ne les
reflète **pas** — c'était un choix, expliqué plus bas — et le gros temps les
efface au profit de son couvercle.

**Les mouettes sont du mouvement, pas des oiseaux** (`gulls.js`). Ce que l'œil
reconnaît à distance n'est pas l'animal mais le vol : un cercle lent, un
inclinaison **dans** le virage, et des battements par bouffées entrecoupés de
vol plané. Tout le fichier est cette arithmétique-là ; l'oiseau lui-même n'est
que deux ailes, un corps et une queue.

Un tiers du vol suit le **navire** plutôt que l'île. Ce n'est pas une licence :
les goélands suivent les bâtiments, et surtout une mouette au-dessus de l'île
fait trois pixels à quatre cents mètres, quand une qui tourne au-dessus de la
dunette est un oiseau. Les autres restent sur leur île, pour que l'endroit garde
sa vie propre quand rien ne passe.

Deux détails de forme comptent. L'aile doit être **longue et mince** : la
première coupe avait 0,36 m de corde pour 0,9 m de demi-envergure, un allongement
de cinq, soit un pigeon qui plane. Et la **queue ne bat pas** — c'est le seul
élément fixe de la silhouette, sans elle l'oiseau est un corps entre deux ailes
et se lit comme une fléchette. Placées comme la terre, dans le repère de l'île
et positionnées contre l'origine courante, donc l'origine flottante ne leur
coûte rien. Surcoût mesuré : **0,05 ms par image** pour douze oiseaux.

**Brume.** Liée à l'état de la mer — voir « Le jour, la nuit, et le gros temps ».
Elle ne l'était délibérément pas, et le renversement est raisonné là-bas.

**Spectre.** Les vagues viennent d'un spectre JONSWAP : il fixe la *forme*
(quelles fréquences portent l'énergie, comment elles s'étalent), et la table
Beaufort fixe l'*échelle*, de sorte que la hauteur significative annoncée par la
console est celle qu'on obtient réellement. Le pic est corrigé pour une mer
limitée par le fetch : la relation « mer complètement développée » donnait des
houles de 800 m en tempête, plus longues que toute mer réelle.

**Sélection CPU.** Le solveur n'intègre pas les 18 composantes mais les plus
**énergétiques**, pas les plus longues. Prendre les plus longues était une
erreur : en tempête elles atteignent des centaines de mètres, et une houle
beaucoup plus longue que le navire le soulève en bloc sans le travailler. C'est
la bande autour du pic qui compte.

**Distances autour de la coque.** `ed` est une distance normalisée
*elliptique* : une bande d'épaisseur constante en `ed` est fine par le travers
mais épaisse de plusieurs mètres devant l'étrave, car l'ellipse est bien plus
longue que large. Toute épaisseur d'écume doit être exprimée en **mètres**
(`length(rel) * (1 - 1/ed)`), sinon le collier gonfle aux extrémités.

**Accents graves dans les shaders.** Les shaders sont écrits dans des *template
literals* : un accent grave dans un commentaire GLSL ferme la chaîne et casse
tout le module, sans message clair. `node --check js/*.js` le détecte.

**Moiré.** Le maillage de mer concentre ses sommets près de la caméra : la
maille passe d'environ 1 m à plusieurs mètres en quelques dizaines de mètres.
Dès qu'elle dépasse le quart d'une longueur d'onde, le maillage ne peut plus
porter cette vague et le battement entre les deux fréquences produit du moiré.
La parade est de **borner la bande passante contre la taille locale de la
maille**, jamais contre la distance : `vSpacing` sort de la dérivée du remap et
sert de critère unique aux vagues, aux rides et à la rugosité spéculaire. Deux
seuils de distance séparés finiraient toujours par se contredire et faire une
bande visible.

**Mer.** Le détail fin ne vient pas du maillage mais d'une perturbation de
normales (`rippleNormal`) — six vagues de Gerstner seules donnent du plastique
moulé. Ce détail doit être fondu avec la distance, sinon il bouillonne à
l'horizon. Le soleil haut rend le scintillement invisible : son reflet tombe à
quelques mètres du bord. La commande « hauteur du soleil » existe pour ça.

**Ce qu'on ne fera pas.** Le turquoise à caustiques des images de lagon vient
d'un fond de sable vu à travers deux mètres d'eau, pas d'un meilleur shader. En
pleine mer, rien ne remonte. Ce serait un décor distinct, pas du réalisme.

## La météo qui se fait toute seule

**Un vent posé une fois et jamais retouché est ce qu'il reste de plus artificiel
sur l'eau** : chaque mer devient sa propre photographie, et rien de ce qui
arrivera dans l'heure n'était pas déjà vrai à la première minute.

Ce qui rend un vrai vent vivant n'est pas d'être aléatoire, c'est d'avoir de la
**mémoire** : la prochaine rafale ressemble à la précédente. Tirer un nombre
neuf toutes les quelques secondes donne exactement l'inverse — un vent sans
passé, sautant entre des états sans rapport. D'où un processus
d'Ornstein-Uhlenbeck sur **deux échelles de temps** : le *système* (force et cap
moyens, une dizaine de minutes), qui donne sa forme à une heure de navigation,
et le *vent lui-même* autour de cette moyenne (une vingtaine de secondes), qui
sont les rafales et les risées.

La **force** revient vers une moyenne climatique, parce que la plupart des jours
sont une brise modérée et que les coups de vent sont rares : le rappel produit
cette distribution gratuitement, sans table de probabilités. Le **cap** ne
revient nulle part — aucun point de la rose n'est plus naturel qu'un autre, donc
sa moyenne est une marche aléatoire pure et le vent peut finir la journée
n'importe où. Mesuré sur une heure : force médiane 4,5, du 2,2 au 6,3 en
déciles, 34° de rotation totale.

Deux couplages, vrais tous les deux sur l'eau : un vent fort est plus rafaleux
en valeur absolue, donc la taille des rafales suit la force moyenne ; mais c'est
le vent **faible** qui est capricieux en direction, un coup de vent tenant son
cap des heures durant. Les deux vont donc en sens inverse. Et le cap a sa
**propre** constante de temps, plus longue que celle des rafales : une direction
qui oscille aussi vite que la force ne se lit pas comme de la météo mais comme
un instrument cassé — la barre la poursuivrait sans arrêt. Avec la même
constante que les rafales on mesurait **48° de rotation par minute** ; séparée,
16°.

Le pas d'intégration est la forme **exacte** du processus, pas un pas d'Euler :
le temps d'image n'est pas fixe, et un pas naïf rendrait le vent plus rafaleux
sur une image lente que sur une rapide — la météo dépendrait de la fréquence
d'affichage.

**Le vent et la mer ne sont pas la même chose, et c'est là que tout se joue.**
Pour une main sur la console les deux vont ensemble et `setSeaState` règle les
deux. Sous une météo qui tourne seule, non : une risée se sent dans la toile à
l'instant où elle arrive, alors que la mer qu'elle lève met des minutes à se
former et des minutes à se coucher. D'où `setWind`, qui ne touche pas au
spectre, appelé à chaque image, tandis que le spectre est reconstruit en
**retard** sur le vent. Ce n'est pas une licence, c'est le récit honnête.

**Reconstruire le spectre rejoue la phase de chaque composante**, et c'est le
vrai piège. La phase vaut `k·(d·r) − ω·t + correction`. Changez ω d'un millième
et le terme `ω·t` saute de `Δω·t` — or `t` est l'horloge courante, des milliers
de secondes : un radian entier. Même chose pour la direction, `k·(d·origine)`
étant évalué contre une origine qui peut être à des centaines de kilomètres, si
bien qu'un centième de degré déplace la phase au navire d'une bonne fraction de
longueur d'onde.

Ce n'est pas une erreur d'arrondi avec laquelle on vit : ensemble elles
rebrassent la mer à chaque rafale, ce qui se lit comme un bouillonnement. Les
deux sont absorbées **exactement**, et pour la raison qui fait marcher l'origine
flottante : ce qui change est une phase, et une phase ne compte que modulo 2π.
La correction est posée pour que le total soit inchangé **à l'origine locale**,
là où est la flotte. Mesuré, pour un pas de veille, contre un creux de 0,60 m
de rms :

| distance | avec report | sans report |
|---|---|---|
| 0 – 25 m | **0,003 m** | 0,812 |
| 25 – 100 m | 0,013 | 0,752 |
| 100 – 250 m | 0,032 | 0,729 |
| 250 – 600 m | 0,076 | 0,770 |
| 600 – 1500 m | 0,195 | 0,762 |

Sans report la mer est intégralement rebrassée partout — l'écart dépasse la mer
elle-même. Avec, le navire flotte dans une mer continue au millimètre, et
l'écart croît avec la distance comme il le doit, deux spectres différents étant
réellement deux mers différentes. La correction est gardée **par bande
spectrale** et non par entrée de `waves`, qui est triée par énergie et se
réordonne dès que le vent change.

**La vitesse est plafonnée, et ce n'est pas un réglage de goût.** Une
reconstruction déplace le nombre d'onde de chaque composante, et `k` entre dans
la phase multiplié par la position : mesuré sur 400 m, un pas d'**un** Beaufort
déplace la surface de 9,8 m de rms. Autrement dit un seul cran de 0,1 — le plus
fin que la console sache même afficher — suffirait à rebrasser la mer. D'où
`seaRate`, 0,0125 Beaufort **par seconde**, et 0,30° de rotation par seconde :
encore trois quarts de Beaufort par minute, bien plus vite que n'importe quelle
météo.

Par **seconde**, et non par reconstruction, ce qui a d'abord été écrit de
travers. Un plafond par appel fait avancer la mer plus vite sur une machine
rapide, et pose son pas de façon inégale dès que le temps d'image se met à
flotter — la fréquence d'affichage devient un paramètre physique. Seule la
vitesse a un sens ; le pas est ce que `dt` en fait.

**Et le spectre est reconstruit à CHAQUE image**, pas cinq fois par seconde.
À 5 Hz, chaque reconstruction devait porter un cinquième de seconde de dérive,
et au-delà du demi-mille cela fait vingt centimètres de surface qui bougent
d'un coup, cinq fois par seconde — un scintillement, et c'est exactement ce
qu'on voyait. Étalée sur les images, la même dérive totale fait un onzième de
cela par pas :

| pas | 600 – 1500 m | part du creux |
|---|---|---|
| 0,2 s (5 Hz) | 0,188 m | 32 % |
| une image (60 Hz) | **0,016 m** | 2,8 % |

Le total déplacé est identique — c'est le **grain** qui change, et c'est lui
seul que l'œil attrape.

**Reconstruire à chaque image ne se paie qu'à une condition : ne rien
allouer.** `setSeaState` fabriquait dix-neuf objets neufs par appel, ce qui
n'était rien quand une main sur un curseur l'appelait deux fois par minute.
Soixante fois par seconde, cela fait plus de mille objets éphémères par
seconde, donc des pauses de ramasse-miettes — et une pause de ramasse-miettes
*est* une saccade. Les tableaux sont désormais des réserves écrites sur place :
mesuré à **7 octets par reconstruction**, soit rien, pour 10 µs de calcul.

Enfin, **un processus d'Ornstein-Uhlenbeck échantillonné par image est du bruit
blanc**. Ses trajectoires sont continues mais nulle part dérivables : le vent
reçoit un coup de dé neuf soixante fois par seconde, et les voiles, le pavillon
et tous les cadrans tremblent avec. Une vraie rafale n'a aucune énergie à
trente hertz. Le processus est donc la **cible**, et le vent qui souffle la
suit à travers un retard court (1,1 s) : mêmes statistiques sur vingt secondes,
courbe lisse sur une. Mesuré sur la dérivée seconde image par image :
**0,00028 contre 0,0268**, soit 96 fois moins de secousse, pour une amplitude
de rafale inchangée.

Corollaire : la météo pilote l'océan **directement**, en pleine précision, et
n'écrit dans les curseurs que pour l'affichage. D'où la séparation de
`refreshSea()` (régler *et* afficher) et de `showSea()` (afficher seulement) —
sans quoi la console, dont le pas est cent fois trop grossier, serait le goulot
qui empêche la mer d'être continue. Le panneau montre le cap du **vent** et non
celui de la houle, comme son étiquette le dit ; l'écart entre les deux est
justement ce qu'il y a d'intéressant à voir — après une saute, les lames
continuent de courir d'où le vent soufflait un quart d'heure plus tôt.

Prendre un curseur en main **arrête** la météo automatique plutôt que d'être
écrasé un dixième de seconde plus tard, et l'arrêt adopte ce que la console
affiche. Une mer qui a rattrapé un vent stable ne reconstruit **rien du tout** :
`chaseSea` dit qu'elle n'a pas bougé et l'appel est sauté.

## Le jour, la nuit, et le gros temps

**Le soleil suit de la vraie trigonométrie sphérique, pas une sinusoïde
déguisée.** Trois lignes, une latitude — que ce monde a déjà, la carte prenant
le point en latitude et longitude — et l'on obtient gratuitement tout ce qu'un
arc dessiné à la main doit se faire dire : le soleil se lève à l'est, se couche
à l'ouest, passe plein sud au méridien sous ces latitudes, monte plus haut en
été, et reste sous l'horizon pour la bonne part de la journée. Une sinusoïde en
hauteur avec un relèvement fixe fait lever et coucher le soleil au même endroit,
ce dont on s'aperçoit sans pouvoir dire pourquoi. Vérifié : coucher à 18 h 42 au
relèvement 286°, hauteur maximale 55,4° au méridien, pour 46,2° N et 12° de
déclinaison.

**La vitesse de défilement est un multiplicateur d'un taux ÉNONCÉ** : une minute
réelle pour une heure, donc vingt-quatre minutes pour un jour entier à ×1, et
douze à ×2, où le curseur se trouve au départ. Énoncer le taux est le point —
« ×2 » ne veut rien dire tant que ×1 n'en veut rien. La console affiche donc
« ×2 · 12 min/jour » et non un chiffre nu. Mesuré à 2,7 µs par appel, soit
0,17 ms par seconde : le prix est la cubemap d'environnement qu'il fait
reconstruire, et elle est déjà bridée.

Prendre le curseur de hauteur en main **arrête** le défilement, pour la même
raison que la mer : être écrasé un quart de seconde plus tard n'est pas une
interface. Et le curseur descend maintenant à −40°, parce que le soleil y
descend vraiment sous ces latitudes ; arrêté à −10, il aurait montré une nuit
bloquée au crépuscule.

**Le gros temps ne fait pas descendre le soleil.** C'était la première lecture
et elle était fausse. Un coup de vent à midi est sombre parce que le ciel s'est
fermé, pas parce que le soleil s'est couché : la lumière reste où elle est dans
le ciel et cesse simplement d'arriver. La hauteur n'est donc pas touchée, et ce
qui change est le couvercle au-dessus, la crasse entre, et ce qui passe du
soleil.

**Et la brume est désormais LIÉE à l'état de la mer, alors qu'elle ne l'était
délibérément pas.** La règle d'origine — un coup de vent ne doit pas fermer
l'horizon, puisqu'il le fermait exactement quand les grosses lames devenaient
intéressantes à regarder — était juste tant qu'un coup de vent était la seule
météo qui existait : fermer la vue enlevait toute la récompense d'avoir levé la
mer. Elle cesse de l'être dès que le coup de vent apporte un ciel à lui,
couvercle au-dessus et pluie sur l'eau, car alors la crasse ne cache plus le
spectacle, elle **est** le spectacle — et une tempête à travers laquelle on voit
à huit kilomètres n'est pas une tempête. Elle arrive donc tard et raide : rien
du tout sous force 5, où la mer est vive et la journée belle, puis en quelques
crans jusqu'à un mille de visibilité. Le beau temps n'est pas touché.

**Le couvercle se pose EN DERNIER, et c'est tout ce qui compte.** Posé avant, il
était défait : la quasi-totalité du ciel qu'on regarde vraiment tient à moins de
vingt degrés de l'horizon, soit exactement la bande que le terme de brume est en
train de relaver en blanc. La tempête sortait alors en gris pâle au lieu de
sombre. Il passe donc après le halo solaire et après le voile d'horizon. Pas
d'une couleur plate non plus : un ciel couvert est le plus sombre au zénith et
se relève un peu vers l'horizon, où la lumière passe sous le bord des nuages —
et ce dégradé est l'essentiel de ce qui l'empêche de se lire comme un mur.

Trois choses suivent, et en oublier une trahit tout. Les **nuages s'effacent**
au lieu d'être poussés au maximum : un ciel couvert n'a pas de structure, et une
tempête qui montrerait encore des cirrus se lirait comme une belle journée mal
éclairée. Le **corps de l'eau** s'assombrit aussi — l'essentiel de la couleur
d'une mer est de la lumière du ciel rediffusée vers le haut, donc un couvercle
là-haut doit atteindre l'eau, faute de quoi la mer restait turquoise sous un
ciel d'ardoise, ce qui se lit comme deux images collées l'une à l'autre. Et
l'**environnement** partage l'uniforme de tempête, si bien que le navire est
éclairé *par* le gros temps et pas seulement *devant* lui : sans cela il
resterait en pleine lumière sous un ciel noir, la seule erreur d'éclairage que
personne ne rate.

**Plus de nuages dans l'eau**, sur demande. Un cirrus réfléchi est une bavure
grise qui se lit comme de la saleté sur la surface plutôt que comme du ciel : le
reflet est un lobe micro-facettes, donc tout ce qui a une structure fine revient
en bouillie. La règle du ciel unique tient toujours — c'est la même fonction, le
même code, la même météo, à qui l'on demande une couverture différente, la
quantité de nuage étant devenue un paramètre.

Mais il faut les **deux moitiés** pour que cela veuille dire quelque chose, et
la première tentative n'en avait qu'une. Le ciel analytique ne mettait plus de
nuages dans le lobe, mais la passe de **réflexion planaire** rend la scène
réelle, dôme compris, et le dôme les remettait aussitôt dans l'eau par la
texture. Mesuré caméra plongeante à 66°, de ciel bleu à couvert complet : 1,78 %
des pixels changeaient encore, contre **zéro** une fois le dôme éteint pour la
durée de la passe.

**La pluie est un RÉSEAU, pas une simulation** (`rain.js`). Chaque goutte porte
un point de départ fixe, et le vertex shader replie `graine − œil` dans une
seule boîte par un modulo avant d'y rajouter l'œil. Il en sort une boîte de
pluie toujours centrée sur le spectateur si loin qu'il navigue, avec une vraie
parallaxe — une goutte à un mètre file, une goutte à trente bouge à peine — sans
un seul repli à tenir, sans une position à réécrire et sans un octet de travail
processeur par image. L'origine flottante ne lui coûte rien pour la même raison :
tout est relatif à un œil qu'on lui donne à chaque image.

Trois détails la font lire comme de la pluie. Ce sont des **segments** et non des
points : ce que l'œil reconnaît est la traînée, un point qui tombe se lit comme
de la neige ou comme une saleté sur l'objectif. La traînée est ajoutée **après**
le repli, sinon une goutte à cheval sur le bord de la boîte serait coupée en
deux. Et l'**inclinaison** vaut à elle seule tout le reste : la pluie tombe à peu
près à la même vitesse par tous les temps, donc c'est le vent qui la couche, et
cet angle-là se lit instantanément et sans y penser.

Elle est coupée sous l'eau — un rideau de pluie vu d'en dessous n'a aucun sens —
et retirée de la **passe de réflexion**, son réseau étant replié autour de l'œil
réel : vue depuis la caméra miroir sous la surface, elle serait tout simplement
ailleurs.

## La gerbe

**Une gerbe n'est pas un effet accroché à un événement, c'est le volume d'eau
que l'objet vient de prendre à sa place, et qui arrive ailleurs.** Tout découle
de là, à commencer par les deux nombres qu'on donne à une gerbe, qui font deux
métiers différents :

- **combien d'eau**, en mètres cubes. C'est par là que le poids entre : un corps
  lourd s'enfonce davantage avant que l'eau ne l'arrête, donc il déplace plus, et
  ce qu'on lit comme « une grosse gerbe » est presque entièrement de la quantité
  — le nombre de gouttes, la largeur de la nappe, le temps qu'elle reste en
  l'air ;
- **à quelle vitesse il est entré**, en mètres par seconde. C'est ce qui décide
  de la **hauteur**, et rien d'autre ne le décide. Lâchez doucement un boulet et
  beaucoup d'eau bougera de très peu.

Le poids entre aussi dans la vitesse, mais par la porte de service : un corps
léger est arrêté par la surface et sa gerbe meurt avec lui, un corps lourd
continue comme si l'eau n'était pas là.

**L'eau sort PLUS VITE que le corps n'est entré.** Elle est chassée d'un
interstice qui se referme — c'est pourquoi un plat fait mal, et pourquoi une
coque qui tape à quatre mètres par seconde envoie de l'eau à cinq mètres de
haut et non à un. La première écriture la faisait sortir plus lentement que le
choc : le panache dépassait à peine la surface et se lisait comme de la mousse.

**Le déclencheur était déjà calculé, mais pas celui qu'on croit.** La première
écriture mesurait la coque qui descend — et cela rate la moitié de ce que l'œil
voit. Une lame qui monte à la rencontre d'une étrave immobile jette tout autant
d'eau : c'est même la définition d'une déferlante. La bonne question n'est pas
« à quelle vitesse la coque descend-elle » mais **« à quelle vitesse cette
cellule passe-t-elle sous l'eau »**, et elle ne demande pas lequel des deux a
bougé.

Elle se localise en prime toute seule. Une cellule déjà profonde ne compte pour
rien, puisque rien de neuf n'y est déplacé ; une cellule en l'air non plus.
Seules comptent celles qui **traversent** la surface, c'est-à-dire exactement
l'endroit d'où l'eau est jetée — sans pondération de profondeur à inventer et
sans flottaison à aller chercher. Il suffit de garder d'une image à l'autre le
taux de remplissage de chaque sonde.

**Une moyenne tombe toujours au milieu, et le milieu n'est pas où l'on regarde.**
Deuxième faute, plus subtile que la première et signalée à l'usage : « on ne
constate pas l'effet devant la coque quand le bateau retombe ou fend la mer ».
La moyenne pondérée des cellules qui traversent atterrit au maître-bau presque à
tous les coups, et non parce que la physique le dit — parce que la coque y est
la plus **large**, donc c'est là qu'il y a le plus de cellules. Moyenner une
étrave qui plonge à quatre mètres par seconde avec un milieu tranquille plein de
cellules donne un point au milieu : l'effet était là, et il n'était jamais où
l'œil se portait.

L'eau est jetée là où le travail est le plus dur, ce qui est un **maximum** et
non une moyenne. Pondérer la position par le **carré** de la vitesse de passage
tire le point dessus — seize contre un pour une étrave qui descend quatre fois
plus vite — tandis qu'une coque qui retombe à plat garde son point au milieu,
toutes ses cellules passant alors à la même vitesse et rien ne se détachant.
Mesuré : sur les impacts francs, **100 %** tombent en avant du tiers avant, et
jusqu'à 0,71 de la demi-longueur, soit au ras de l'étrave. La *taille* de la
gerbe, elle, garde la moyenne honnête en mètres cubes par seconde : ce sont deux
questions différentes et elles ont chacune leur accumulateur.

**Et le point est porté jusqu'à la RALINGUE de sa flottaison.** Le centroïde vit
dans le plan de flottaison, donc l'embrun montait à travers son propre pont et
se lisait comme de l'eau embarquée plutôt que de l'eau jetée.

L'écarter d'une distance fixe ne suffit pas, et ce fut la deuxième tentative :
une coque est **longue**, donc un point à huit mètres en avant du maître-bau,
poussé de deux mètres de plus, reste à quatre mètres de l'étrave — et l'embrun
court le long du pont de l'avant vers la taille, ce qui est exactement ce qu'on
a vu.

Il faut le poser **sur** son contour, pas le pousser vers lui. Ramenée à sa
demi-longueur et à sa demi-largeur, la coque devient un cercle unité :
normaliser là puis revenir pose la gerbe sur la flottaison, au relèvement d'où
le coup est venu, quelles que soient ses proportions. Un coup à l'avant crève à
l'étrave, un coup par le travers par-dessus bord, et une seule ligne
d'arithmétique fait les deux. Retombant à plat il n'y a pas de relèvement — la
moyenne est au centre — et c'est l'étrave qui est prise, là où une chute à plat
jette l'eau qu'on remarque.

**Un impact est un ÉVÉNEMENT, pas un état.** Elle chasse toujours *un peu* d'eau
vers le bas ; ce qui compte est le franchissement d'un seuil, suivi d'un temps
mort avant le suivant. Sans ce temps mort une entrée franche engendrerait une
gerbe à chaque image pendant un tiers de seconde, ce qui se lit comme un jet.

**Le seuil est une VITESSE, en mètres par seconde, et il a fallu une frégate
pour montrer pourquoi.** Il avait d'abord été écrit comme une fraction du volume
de coque par seconde, ce qui avait l'air indépendant du navire et ne l'était pas.
Le débit de traversée va comme la **surface** de flottaison multipliée par la
vitesse de passage, donc en L·B ; le volume de coque va en L·B·D. Diviser l'un
par l'autre laisse un D au dénominateur, et pénalise donc un navire d'être
profond : la frégate, trois fois le creux du chaland, sortait au tiers du débit
pour la même mer et ne jetait **rien du tout** — deux minutes de force 7 sans
une seule gerbe, et personne ne l'aurait deviné en regardant le chaland.

Divisé par sa section moyenne — volume de coque sur creux — il reste des mètres
par seconde, c'est-à-dire la même question posée à toutes les coques : à quelle
vitesse, en moyenne sur sa longueur mouillée, passe-t-elle sous l'eau ? Relevé
à 1,9 m/s, deux minutes de mer établie par état, en route :

| force | 3 | 4 | 5 | 6 | 7 | 9 |
|---|---|---|---|---|---|---|
| chaland, gerbes/min | 0 | — | 2 | 4 | 7 | 12 |
| frégate, gerbes/min | 0 | 2 | 4 | 4 | 4 | 9,5 |

Silencieuse sous force 4 sur l'une comme sur l'autre, ce qui est le point : un
seul nombre, et il veut dire la même chose partout.

**Et une gerbe toutes les cinq secondes au plus, ce qui est une borne assumée et
non un anti-rebond.** La physique trouve volontiers une douzaine d'impacts dans
ces cinq secondes et tous sont réels — mais douze panaches en cinq secondes ne
se lisent pas comme un navire qui travaille dans la lame, ils se lisent comme un
chapelet de pétards le long du bord. Ce que l'œil demande à une mer, c'est **une**
masse d'eau jetée, assez grosse pour être regardée, puis le temps de la regarder.

Un défaut à corriger dans la borne : une claque tire, et la lame verte qui monte
à bord deux secondes plus tard — celle qui valait le coup — est jetée parce que
la pendule n'avait pas fini. Un impact au moins **deux fois** plus gros que celui
qui tient la place peut donc la prendre, passé une seconde. Il reste rare par
construction, doubler étant beaucoup : relevé, l'écart minimal tombe à 5 s par
tous les temps sauf en tempête, où l'exception a joué une fois à 2,2 s.

**La vitesse d'éjection s'est trompée des deux côtés avant de tomber juste.** En
dessous de la vitesse de choc, la couronne dépassait à peine la surface et se
lisait comme de la mousse ; au double, une belle brise envoyait l'eau à cinq
mètres. Ce qui tranche est que la première de ces mesures a été prise pendant
que l'embrun naissait encore **dans** la coque, où la moitié ne se voyait pas :
la faute était l'endroit, pas la vitesse, et monter la vitesse pour compenser
soignait le symptôme.

**Et elle est mise à l'échelle de l'événement, ce qui est ce qui empêche un
grand navire de ressembler à sa maquette.** La pesanteur fixe la seule pendule
qu'ait une gerbe : de l'eau lancée à quatre mètres par seconde est montée et
retombée en huit dixièmes de seconde quoi qu'elle côtoie — donc à côté d'une
frégate de soixante mètres c'est un clignotement, et l'œil lit le clignotement
et dit « petit ». Le cinéma le sait depuis un siècle : une maquette se trahit
par une eau qui bouge trop vite pour sa taille apparente.

Le remède est la similitude de **Froude**, qui se trouve être la physique
exacte : pour un mouvement gouverné par la pesanteur, des écoulements
géométriquement semblables ont des vitesses en **racine de la longueur**. Le jet
porte donc un facteur √(R/Rréf) et tout le reste suit tout seul — la couronne
monte proportionnellement à R et tient l'air proportionnellement à √R, sous une
pesanteur ordinaire et sans rien truquer.

Cela sonne à l'envers de rendre une grosse gerbe plus **rapide** quand le
reproche était qu'elle avait l'air trop rapide. Ce n'en est pas un : la vitesse
absolue croît en √R tandis que la taille croît en R, donc ce qu'on voit — la
vitesse rapportée à la taille — décroît en 1/√R. Une grosse gerbe est une gerbe
lente, et c'est l'arithmétique qui le dit. Mesuré, chaland contre frégate :

| | chaland, L = 28 m | frégate, L = 60 m |
|---|---|---|
| rayon de gerbe | 3,3 m | 4,9 m |
| hauteur | 0,8 – 1,0 m | 1,5 – 2,1 m |
| durée de vol | 0,8 s | 1,1 – 1,3 s |

**La traînée aussi va avec la taille**, et par la même porte : elle croît comme
une surface quand la masse croît comme un volume, donc ce qui freine va en
1/taille. Sans cela chaque paquet décélère pareil et une nappe entière se
dissipe aussi vite qu'une gouttelette — la maquette revient par un autre
chemin.

**Et par grosse houle, ça partait en feu d'artifice — à cause de la fréquence
d'affichage.** Une cellule ne peut se remplir que d'une cellule entière en une
image, donc la vitesse de passage **sature à `probeH/dt`** : quelque trente-six
mètres par seconde à soixante images, et *davantage* sur une image lente. Ce
plafond est une propriété de l'horloge d'affichage et non de la mer. Pris au
mot, il envoyait l'embrun à seize mètres en l'air par forte houle — et plus haut
encore sur une machine plus lente, ce qui est la signature même d'une grandeur
qu'on n'aurait jamais dû lire telle quelle.

Sept mètres par seconde est la borne honnête : c'est à peu près la vitesse
orbitale de la mer la plus creuse de ce modèle, et une coque et une lame qui se
rencontrent plus fort que cela, c'est l'arithmétique qui manque d'images, pas
l'océan qui fait quelque chose de remarquable.

Le jet est en outre **borné par ce qu'une cavité de cette taille peut jeter** :
la couronne monte à peu près autant que la cavité est large, étant la même eau
repliée, donc un panache qui part à trois ou quatre fois son propre rayon a
cessé d'être de l'eau déplacée pour devenir une fusée. Et la taille d'un paquet
est plafonnée en mètres autant que mise à l'échelle — de l'eau déchirée ne tient
pas ensemble au-delà d'un demi-mètre, quoi qu'on l'ait lancée, sans quoi un gros
événement partait en poignée de rochers. Mesuré, hauteur maximale du panache sur
deux minutes :

| | force 6 | force 8 | force 9 |
|---|---|---|---|
| chaland, 28 m | 0,9 m | 3,6 m | 4,4 m |
| frégate, 60 m | 4,3 m | 5,5 m | 7,0 m |

Contre seize mètres avant les bornes. La frégate monte plus haut en mètres et
moins haut rapportée à sa longueur — un neuvième contre un sixième — ce qui est
exactement ce que la similitude demande.

**Et rien ne compte tant que les sondes ne sont pas remplies.** Tout ce qui la
DÉPLACE sans la faire naviguer — asseoir une coque neuve, renflouer une épave —
fait traverser la surface à toutes ses cellules d'un coup, ce qui se lirait
comme le navire entier qui tape. Trois images de chauffe suffisent.

**Piège de mesure, et il a failli passer.** Le premier comptage changeait l'état
de mer puis comptait aussitôt, et trouvait des gerbes par calme plat. Changer le
spectre fait **sursauter** la coque, et le sursaut déclenche un impact bien
réel. Quinze secondes de décantation avant de compter, et le calme ressort
silencieux à tous les seuils — comme il se doit. Un relevé pris juste après
avoir changé un réglage mesure le changement, pas le réglage.

**Second piège, du même banc.** Les rappels de test posés sur `onSlam` se
chaînent d'une expérience à l'autre : chaque essai gardait le précédent et
l'appelait, si bien qu'un ancien rappel écrivait par-dessus le résultat du
nouveau et l'on lisait la mesure d'avant en croyant lire celle d'après. Rendre
le rappel d'origine à la fin de chaque essai, ou recharger la page entre deux.

**Fin et nombreux plutôt que gros et rares.** L'embrun n'est pas un ensemble
d'objets, c'est une texture, et l'œil la lit à son grain. Trop peu de sprites et
l'on compte les points ; trop gros, et c'est de l'ouate — les deux ont été
essayés dans cet ordre. Et l'opacité reste bien sous l'unité, un panache étant
fait de dizaines de ces sprites qui se recouvrent : à pleine opacité ils
s'empilent en un corps blanc plein au lieu de se construire en quelque chose au
travers de quoi on voit.

Un sprite n'est pas **une** goutte, c'est un paquet d'eau déchirée et de l'air
qu'elle contient : dimensionné comme une vraie goutte, il disparaît à toute
distance et le panache entier se lit comme de la poussière sur l'objectif.

Le tout tient dans **un** objet `Points` et un seul appel de dessin — des sprites
avec chacun leur matériau conviennent très bien à une douzaine de bouffées de
fumée et pas du tout à deux cents gouttes. La recherche d'un emplacement libre
se fait au **curseur tournant** : une grosse gerbe en demande quatre cents d'un
coup, et repartir du début à chaque fois en faisait un quart de million de
comparaisons dans une seule image, pour une réserve presque vide. Coût mesuré :
**13 µs par image** avec sept cents gouttes vivantes.

**Et les débris de l'explosion giclent aussi.** Une planche qui touchait l'eau
était laissée à couler derrière une surface opaque, ce qui ne coûtait rien et
avait l'air juste — mais celle qui manquait l'eau tombait alors pour l'éternité,
et l'on jetait le seul moment où une planche qui retombe vaut d'être regardée.
Elle disparaît maintenant à l'entrée, en jetant son eau. Mesuré : quarante-quatre
planches à la mer pour une soute qui saute.

**Et les débris gerbent pour de bon en retombant.** La planche demandait neuf
dixièmes de mètre cube, ce qui décrit le bois lui-même et non le trou qu'il
perce : dix gouttes, un demi-mètre, invisible. Elle emprunte désormais la
**colonne** du boulet plutôt que la couronne de la coque — une planche qui
arrive à vingt-cinq mètres par seconde est bien plus près d'un projectile que
d'un navire qui s'assoit dans un creux — mais avec un plafond plus bas, donc
plus courte et plus large, ce que fait réellement un morceau de bois.

La taille est fixée par la **réserve** autant que par l'eau, et c'est elle qui a
tranché. Une soute qui saute jette une soixantaine de planches qui retombent en
quelques secondes, chacune avec sa colonne. La réserve est passée de deux mille
à **quatre mille places** — un tampon deux fois plus gros et rien d'autre de
mesurable, 76 µs par image au plus fort — et le volume à 4,5 m³ : relevé, 57
gerbes, un pic de 2 850 gouttes, **aucune saturation**, et des colonnes de plus
de six mètres. À six mètres cubes la réserve restait saturée quatre secondes et
demie, et le curseur tournant recyclait alors des gouttes encore en vol : les
premiers panaches étaient coupés en plein vol pour payer les derniers.

Enfin, la gerbe et les débris **se recentrent** avec le reste. Chaque goutte
tient une position dans le repère local ; sans cela un recentrage laisse
l'embrun suspendu à quinze cents mètres derrière. L'explosion avait la même
faute en sommeil depuis le début — trois secondes de vol suffisent à croiser un
recentrage.

## Le fret, et où on le met

**Le fret est la même chose que l'eau embarquée, et c'est délibérément la même
machinerie** : un poids, à l'endroit où il repose. La méthode du poids ajouté
était déjà écrite pour l'envahissement ; charger un navire n'a demandé que de
lui donner un second client. Une seule différence dans l'arithmétique, et elle
est la bonne : le fret est du poids **mort**, saisi et arrimé, donc il n'apporte
aucune carène liquide. Il alourdit et déplace le centre de gravité ; il ne
ballotte pas.

Il se range dans les **compartiments de l'envahissement**, parce qu'ils sont
découpés sur la grille de sondes : une cale est donc du vrai volume de coque,
avec une largeur et une hauteur mesurées sur le même plan de formes que tout le
reste. Trois hauteurs et cinq compartiments — assez pour que l'arrimage soit une
décision, pas assez pour que ce soit un tableur.

**L'endroit compte plus que la quantité, et c'est tout l'intérêt de pouvoir le
choisir.** Relevé sur le chaland, lège 210 t, avec 120 t de fret :

| arrimage | déplacement | immersion | assiette | gîte | GM |
|---|---|---|---|---|---|
| à vide | 210 t | 39 % | 1,2° | 0° | 2,37 |
| fond, au milieu | 330 t | 61 % | 1,8° | 0° | **2,48** |
| fond, **à l'avant** | 330 t | 61 % | **16,2°** | 0° | 2,48 |
| **sur le pont** | 330 t | 61 % | 1,8° | 0° | **1,85** |
| entrepont, **à tribord** | 330 t | 60 % | 1,7° | **−16°** | 2,18 |

Trois leçons, et aucune n'a été écrite à la main :

- **d'avant en arrière, c'est l'assiette.** Cent vingt tonnes dans la cale
  avant enfoncent l'étrave de seize degrés et portent le tirant de 2,35 m à
  5,48 — c'est ainsi qu'un navire mal chargé embarque la mer par l'avant ;
- **en travers, c'est une gîte qu'aucune barre ne rattrape.** Seize degrés pour
  le même poids mis à tribord, et deux cents tonnes en font trente-sept, ce qui
  est la limite du raisonnable ;
- **en hauteur, c'est le GM, et c'est la dangereuse.** Du poids sur le pont
  n'achète rien et coûte un quart de la hauteur métacentrique. Du lest au fond
  de cale fait l'inverse et la **raidit** — 2,48 contre 2,37 à vide. C'est la
  manière classique de perdre un navire par ailleurs sain, et elle est ici
  gratuite : elle sort du calcul du centre de gravité, pas d'une règle ajoutée.

**Sa capacité n'est pas un chiffre d'équilibrage** : c'est le poids qui l'amène
à 85 % de coque immergée, ce qui est déjà bas sur l'eau. Rien n'empêche de la
charger au-delà — pouvoir ruiner un navire en le surchargeant est précisément
l'intérêt — mais la console vire à l'orange puis au rouge bien avant qu'elle ne
s'en aille.

**Le déplacement affiché est devenu vivant.** Il donnait la valeur de la fiche,
qui ne bougeait jamais et ne disait donc rien ; il donne maintenant ce qu'elle
pèse réellement — lège, plus le fret, plus l'eau prise. Tout l'intérêt de
charger un navire est de le voir s'alourdir et s'enfoncer.

**Un piège d'ordre d'initialisation.** La capacité a besoin du volume de coque,
qui n'est connu qu'une fois les sondes construites — et les sondes se
construisent AVANT le bloc où vivent les autres champs. Un `= 0` d'apparence
inoffensive écrivait donc par-dessus la vraie valeur, et la console annonçait
une capacité de zéro. Le champ n'est plus initialisé là, et la raison est
écrite sur place.

## Les dépressions ont un lieu

**Écrites comme les îles, et pour les mêmes raisons** : un hachage sur une
grille grossière, pas de fichier de carte, pas d'état, rien à stocker. Revenir
sur la même eau y retrouve la même dépression. La différence est qu'une tempête
a aussi un **temps** — un système se déplace — donc c'est une fonction pure de
la position *et* de l'horloge, ce qui ne coûte rien de plus et offre un ciel qui
vient à sa rencontre autant qu'elle y navigue.

**La dérive est une onde triangulaire, pas une droite**, et ce n'est pas de la
paresse. Un centre qui file dans une direction s'en va sans borne : aucune
recherche de cellules voisines ne pourrait être sûre de le trouver. Prendre la
dérive modulo quelque chose le fait au contraire **sauter** en arrière — et un
saut, c'est un état de mer qui change de quatre Beaufort entre deux images. Un
triangle est **borné ET continu**, seule combinaison qui serve ici : c'est la
même raison qui fait réduire la phase de la houle modulo 2π plutôt que de
l'écrêter.

**Et l'amplitude doit dépasser la cellule**, ce qui fut la première faute.
Enfermé dans la sienne, un centre parcourait trois kilomètres pour un rayon de
trois et demi — moins d'un rayon — si bien qu'un navire à la cape voyait le
grain se retirer et revenir sur lui indéfiniment : relevé, l'intensité faisait
1 · 0,74 · 0,01 · 0,82 · 0,99 sur une demi-heure. Des accalmies, jamais de
délivrance ; et une dépression qui ne part jamais n'est pas de la météo, c'est
un lieu. Portée à plus d'une cellule, elle parcourt **24,4 km, sept fois son
rayon**, et le même navire immobile est dégagé dès la sixième minute, revisité
vers la trente-cinquième, puis tranquille deux heures durant. Attendre devient
un vrai choix à côté de s'en aller.

Le prix est une recherche sur **deux anneaux** de cellules au lieu d'un, soit
vingt-cinq hachages par image, ce qui ne coûte rien. Couverture mesurée sur
1 600 km² et six instants : **5,9 %** de la mer sous un grain, **28,8 %** à
portée d'en voir un à l'horizon.

Dimensionnées pour un navire et non pour une carte météo : onze kilomètres entre
candidates, deux à quatre de rayon. Une vraie dépression fait des centaines de
kilomètres et demanderait une semaine à traverser à la voile ; celles-ci se
franchissent en dix ou vingt minutes, ce qui est assez long pour être une
épreuve et assez court pour qu'on en sorte.

**Le creusement n'est pas linéaire.** Une dépression a une large épaule et un
cœur dur, donc l'essentiel de la traversée n'est que du mauvais temps et c'est
le dernier tiers qu'on retient. Relevé en approchant du centre, dépression de
3,5 km de rayon, pic 8,5 :

| distance | 11 km | 8,4 | 6,3 | 3,5 (lisière) | 2,4 | 1,4 | 0,5 | 0 |
|---|---|---|---|---|---|---|---|---|
| force | 0 | 0 | 0 | 0 | 1,8 | 5,5 | 8,0 | 8,5 |
| noirceur du ciel | 0 | 0,13 | 0,50 | 1,00 | 0,82 | 0,45 | 0,20 | 0,15 |

**Le vent tourne AUTOUR du centre**, et c'est ce qui fait lire un système comme
un système plutôt que comme une mer qui grossit. Surtout tangentiel avec un peu
d'aspiration vers l'intérieur, ce que fait une vraie dépression — et cela veut
dire qu'on peut trouver le milieu à la seule sensation du vent, sans baromètre.

**Une seule cible, chassée par le spectre.** Trois choses peuvent décider de
l'état de mer — la console, la météo automatique, et la dépression, qui
l'emporte sur les deux autres parce qu'une dépression ne négocie pas. Elles sont
composées en **un** objectif que le spectre poursuit ensuite, plutôt qu'en trois
appelants qui se relaient sur `setSeaState` : ainsi le plafond de vitesse
s'applique à toutes, ce qui compte surtout pour la dépression — y entrer est
exactement le cas où un saut sans borne rebrasserait toute la mer.

Le plafond ne mord pas, et c'est voulu : monter de 0 à 7,5 Beaufort demande
600 s au plafond, quand traverser 3,5 km à quatre nœuds en prend 875. **C'est la
géométrie qui gouverne**, le plafond n'étant là que pour le cas pathologique.

Corollaire : laissée à elle-même, sans météo automatique ni dépression, la
console **est** la mer, et le retard est tenu égal à elle. Sans cela un curseur
déplacé à la main serait doucement ramené vers la valeur où le retard en était
resté.

**Le grain se voit venir**, et c'est la moitié de l'intérêt : un quart de ciel
noir posé sur l'horizon, dans sa direction et nulle part ailleurs, qui s'efface
au profit du couvercle général une fois qu'on est dedans — il n'y a plus rien à
désigner quand on y est. Cela coûte un produit scalaire.

Le régler a demandé trois essais, et le premier était **géométriquement juste et
visuellement nul** : un grain à six kilomètres sous-tend ±29° et ne monte qu'à
13°, si bien que le cône étroit d'origine confinait tout à une bande de ciel de
cent cinquante pixels — mesurable à 8,9 % de l'image, avec un écart moyen de
25/255, et parfaitement invisible. En l'élargissant grossièrement on obtient
l'inverse : 98,7 % de l'image et 197 d'écart, tout le ciel et toute la mer lavés
de gris. Ce qui manquait au premier n'était pas l'étendue mais la **noirceur**.
Le réglage retenu donne 87 d'écart moyen à sa noirceur réelle — un banc gris
qu'on distingue nettement, avec du ciel clair de part et d'autre.

La dépression est aussi **portée sur la carte**, en disque doux plutôt qu'en
contour : on sait à peu près où est un grain, jamais où il finit. Et sous la
terre, étant de la météo et non de la géographie.

**Et le banc s'allume par en dedans.** Un grain vu de six milles n'est pas une
forme grise morte : il s'éclaire brièvement, quelque part le long de son front,
et c'est l'essentiel de ce qui le distingue d'un banc de brume. Cousin de
`strike()` et délibérément pas la même chose : un coup au-dessus d'elle éclaire
le pont, la toile et tout le ciel ensemble, ce qui est juste quand l'orage est
sur elle et faux à six milles, où l'on voit une tache de nuage s'allumer et
rien d'autre — pas de lumière sur les voiles, pas d'ombre qui bouge. Celui-ci ne
sort donc jamais du shader de ciel.

Court, aussi : la foudre proche porte une enveloppe de quatre pointes sur une
seconde parce qu'on est dedans et que le détail se voit ; à cette distance un
coup est un clignement, et le dessiner plus long le fait lire comme une lampe.
Et sur son **propre** relèvement, à quelques dizaines de degrés du milieu du
banc — un front entier qui clignote d'un bloc se lit comme un interrupteur.
Mesuré : un éclair toutes les 5,7 s à 0,53 de noirceur, allumé 5 % du temps.

**Deux erreurs de luminosité, opposées, et la seconde est instructive.** La
première version ajoutait 1,9 fois la couleur d'horizon : additive dans un ciel
que la brume étale ensuite sur la mer, elle sortait comme un second soleil posé
sur l'eau. La correction évidente — la rendre proportionnelle à l'horizon, comme
l'écume — est **exactement fausse**, et pour une raison qui vaut d'être retenue :
l'écume **réfléchit**, donc elle doit suivre la lumière ; un éclair **émet**.
Proportionnel à l'ambiante il s'éteignait la nuit, c'est-à-dire précisément à
l'heure où un grain lointain s'allume comme une lampe derrière un drap.

Il a donc sa couleur propre, bleutée, et ce qu'il ajoute ne dépend pas de ce qui
est éclairé par ailleurs — c'est ce qui en fait un éclair. Vérifié : contribution
**identique à midi et à minuit**, +53 au maximum et +16 en moyenne dans les deux
cas.

## Une barre qui n'est pas la vôtre

**Elle écrit dans le même `ctrl` qu'une main sur la roue** : un gouvernail de −1
à 1, une écoute, des voiles établies ou ferlées. Rien n'atteint le solveur, et
c'est le point — une barre automatique capable de pousser la coque tricherait,
et cesserait du même coup d'être une épreuve de la navigabilité du navire. Si
elle ne sait pas remonter au vent, vous non plus.

**Elle ne peut pas aller où elle vise**, si c'est dans le vent. Une chasse droit
au vent n'est pas un cap mais une suite de **bords**, et choisir lequel est
l'essentiel du métier. Le choix a de la mémoire : pris à neuf à chaque image sur
le côté où la proie se trouve, un but plein vent debout passe d'une amure à
l'autre à chaque instant et le navire reste en panne, à virer sans fin.

**ELLE ABAT, ELLE NE VIRE PAS VENT DEVANT.** C'est la décision qui sépare une
barre qui marche d'un navire planté dans le lit du vent jusqu'à la marée
suivante, et elle a coûté trois tentatives.

Le chemin le plus court vers un cap n'est pas toujours un chemin praticable.
Virer vent devant traverse le lit du vent, où les écoutes reviennent dans l'axe
et où il n'y a **plus aucune poussée** ; le navire perd son erre, et l'autorité
du gouvernail allant comme le **carré** de la vitesse, elle meurt avant que
l'étrave soit passée. C'est manquer à virer, à tous les coups.

Et ce n'est pas affaire de degré. La règle avait d'abord été écrite comme une
particularité du carré, sur un critère de vitesse — c'était faux : **rien** dans
ce modèle ne vire vent devant. Mise sur l'autre amure, abattée interdite, depuis
le meilleur de ce que chacune tient au près :

| | départ | au plus près | issue |
|---|---|---|---|
| goélette, aurique | 4,07 nds | 22° du vent | retombe |
| pirate, carré | 2,18 nds | 29° du vent | s'arrête à 0,96 nd |

Aucune ne passe. Le pirate, à qui l'on demandait 119° sur bâbord à neuf dixièmes
de nœud, a tourné de **cinq degrés en dix minutes** en ralentissant tout du long.

Elle fait donc le tour par l'**autre côté** — en s'écartant du vent, trois fois
plus loin mais voiles pleines sur tout le parcours, en gagnant de la vitesse au
lieu d'en perdre. C'est abattre en grand, c'est ce que les carrés faisaient
vraiment, et voilà pourquoi. La manœuvre **se verrouille** : abattre la rend
rapide, ce qui défait la condition même qui l'a déclenchée, et elle se
remettrait au lof à mi-tour.

**Ce qu'il ne faut PAS faire, et que j'ai fait deux fois.** Un terme intégral
dans le *gouvernail* ne peut rien : la barre était déjà à fond, et à un nœud la
toile la bat sans discussion. Et **faire faseyer** pour ôter la poussée — ce
qu'un marin ferait d'un navire coiffé — est exactement le remède qui tue : ne
pas être poussée est ce qui la maintient coiffée, donc la condition qui
déclenche le faseyement se confirme elle-même. Écrite sur un critère de vitesse,
elle se déclenchait à **zéro nœud**, avant que le navire ait jamais bougé, et il
est resté en panne pendant les seize minutes de l'essai. Rien ne fait donc
faseyer ; abattre est la réponse à être bloqué.

**Elle change d'amure sur la LIGNE DE BORD**, quand le but relève de son angle
de près sur l'autre bord — c'est le premier instant où l'autre amure le fait
porter. Une marge de quinze degrés paraissait raisonnable et ne l'était pas :
elle changeait d'amure alors qu'elle refermait très bien, et chaque changement
coûtait plus que le bord n'avait rapporté — la distance a oscillé entre 1 190 et
1 500 m pendant trois quarts d'heure sans jamais converger. La ligne de bord
porte en outre sa propre hystérésis, le but relevant du même angle de l'autre
bord dès qu'elle est passée.

Prolonger **au-delà** de la ligne de bord a été essayé aussi, en se disant qu'une
manœuvre aussi chère doit être rare. Ça n'apporte rien : à quatre fois la bordée
minimale, 756 m contre 754.

**L'angle de bord est MESURÉ, et sur la ROUTE, pas sur le cap.** C'est là qu'est
toute la difficulté, et ma première table s'y est trompée. Ce qui compte n'est
pas jusqu'où elle peut pointer mais où son gain au vent culmine — question
différente et toujours plus ouverte. Or ces navires **dérivent beaucoup** : le
pirate, cap à 50° du vent, fait en réalité route à **68°**. Une polaire prise sur
le cap le flatte du double. La mienne annonçait 1,23 nœud de gain là où la vérité
est 0,73, et j'ai ensuite passé une heure à me demander pourquoi il mettait
trente-trois minutes à gagner huit mètres.

Force 4, écoutes sur `optSheet`, cap tenu 150 s. Vitesse, route réelle, gain au
vent **sur cette route** :

| cap au vent | 40° | 50° | 60° | 70° |
|---|---|---|---|---|
| pirate, carré, 30 m | 2,33 | 3,09 | 3,72 | 4,22 |
| *route* | 56° | 63° | 71° | 79° |
| *gain au vent* | 1,29 | **1,40** | 1,23 | 0,82 |

*(Relevé refait après que le pirate est passé de 60 m à 30. À 60 m il donnait
1,94 nœud au cap de 50° pour 0,73 de gain, sur une route de 68° : la petite
coque dérive cinq degrés de moins et gagne presque le double. L'angle de
meilleur gain, lui, ne bouge pas — cinquante degrés dans les deux cas — donc
`closeHauled` n'a rien à changer.)*

| cap au vent | 35° | 40° | 45° | 50° | 60° |
|---|---|---|---|---|---|
| goélette, aurique | 2,25 | 2,71 | 3,14 | 3,54 | 4,22 |
| *route* | 52° | 55° | 58° | 62° | 70° |
| *gain au vent* | 1,39 | 1,56 | 1,65 | **1,66** | 1,44 |

Cinquante degrés pour le carré, quarante-sept pour l'aurique. Ni l'un ni l'autre
n'est l'angle réel d'un navire de ce gréement — un carré tient soixante-dix —
et les deux viennent de la mesure et non du souvenir : soixante-dix coûterait au
pirate **les deux cinquièmes** de sa remontée dans CE modèle. La barre mène le
navire qu'elle a.

Voir la dernière colonne : au travers, la route du pirate est à **97°** du vent.
Il **recule** au vent en traversant.

**Au près elle gouverne au VENT, pas au compas**, ce qui est la manière dont on
le fait vraiment : une saute est ainsi rattrapée avant d'avoir rien coûté. Et
les écoutes suivent `optSheet`, que le solveur calcule déjà pour tracer le repère
vert de la console — une définition, deux usagers.

**Elle vise la RIVE de sa distance de garde, pas le navire.** Un chasseur qui
gouverne sur le centre de sa proie l'aborde, ce qui n'est pas une manœuvre : elle
est dirigée sur un cercle autour d'elle et referme par la tangente. Et un
bâtiment sans gréement — le chaland — passe **sous machine** : une barre qui
tire des bords sans toile ne fait que dériver.

Vérifiée en chasse, but fixe à 900 m, départ cap opposé et vitesse nulle :

| | but au vent | par le travers | sous le vent |
|---|---|---|---|
| pirate | ne rallie pas | rallie en 9,6 min, à 85 m | 11,7 min, à 54 m |
| goélette | 700 → 327 m en 20 min | 5,9 min, à 42 m | — |
| chaland (machine) | 10,3 min, à 12 m | — | — |

**Le pirate remontait mal au vent, et ce n'était pas la barre : c'était sa
taille.** À 60 m il ne gagnait que 0,73 nœud et payait dix minutes et trois
cents mètres à chaque abattée — il convergeait, mais il aurait fallu des heures,
et un pirate placé au vent du joueur n'était pas une menace. Descendu à 30 m il
gagne **1,40**, ce qui règle le grief sans toucher à la barre : c'est
exactement ce que la mise à l'échelle du gréement carré annonçait, et
accessoirement ce qu'un vrai pirate montait — un navire rapide et ardent, pas
une frégate.

**Ce que la mesure a trouvé au passage.** L'autorité du gouvernail va comme le
CARRÉ de la vitesse, donc aux allures de voile les grosses coques n'obéissent
quasiment plus. Taux de giration du pirate, barre à fond, avant correction :

| lancée | 1 nd | 2 | 3 | 5 | 8 |
|---|---|---|---|---|---|
| giration | 0,03 °/s | 0,08 | 0,17 | 0,46 | 1,13 |

Ce n'est pas une constante recopiée : `rudderK` est bien mis à l'échelle de la
surface latérale, comme tous les coefficients hydro. C'est que l'inertie de lacet
du pirate vaut cent huit fois celle de la goélette pour seize fois le moment de
barre. La goélette, elle, manœuvre très bien.

Cela valait pour les frégates du joueur autant que pour le pirate — on ne s'en
apercevait pas parce qu'on les mène à la machine, où elles filent onze nœuds et
retrouvent leur gouvernail. `rudder.power` a donc été **multiplié par trois**,
sur demande, les trois carrés passant de 92 à 275 :

| `rudder.power` | 92 | 183 | 275 | 366 |
|---|---|---|---|---|
| à 3 nds | 0,18 °/s | 0,34 | **0,51** | 0,67 |
| à 5 nds | 0,46 | 0,91 | 1,35 | 1,78 |

Un quart de tour en trois minutes à trois nœuds, ce qui est juste pour un lourd
carré sous voiles, et sans nervosité à la machine (62° en une minute).

## Les grosses pièces

**Un coup de canon n'est pas une petite explosion**, et le traiter comme telle
est la manière de le rater. Une explosion est une boule qui grandit dans toutes
les directions et qui **monte**. Un canon est un **jet** : les gaz sortent par
le travers à une vitesse énorme, l'air les arrête en quelques mètres, et le tout
s'enroule en un gros nuage qui ne va plus nulle part — sauf sous le vent.

**Et c'est ce dernier point qui est tout l'effet.** La fumée de poudre est
lente, épaisse et durable, donc **le navire sort de dessous la sienne** et
laisse une file de nuages suspendus au-dessus de l'eau là où chaque pièce a
parlé. Tous les récits de combat parlent de la fumée : elle aveuglait la
batterie, elle cachait l'ennemi, elle disait où était le vent. Une bouffée qui
meurt là où elle est née se lit comme un effet ; un banc qui descend sous le
vent se lit comme de l'artillerie.

La fumée ne décroît donc pas vers l'**arrêt** comme celle de l'explosion : elle
relaxe vers la vitesse de l'air. C'est une ligne, et elle donne d'un coup la
dérive, le banc sous le vent et le navire qui se dégage.

**Mais vers une FRACTION de la vitesse du vent seulement** — trois dixièmes — et
il a fallu la regarder pour le voir. Prise à la pleine vitesse de l'air, une
bordée est balayée du bord avant que l'œil ait fini de la lire : 7,5 m/s font
une encablure en vingt secondes. Un nuage de poudre est froid, dense et chargé
de grains imbrûlés ; il **tient où les pièces ont parlé** et s'affaisse bien plus
lentement que l'air autour de lui. La stagnation d'abord, la dérive ensuite.
Mesuré à `LAG = 0,30` : le banc s'écarte de 16 à 33 m en huit secondes, soit
2,1 m/s dans un vent de 7,5.

**ET LE FEU SE DIFFUSE DEDANS**, ce qui est l'essentiel de ce qui fait sentir la
poudre. La flamme est **dans** sa propre fumée et non devant : pendant une
fraction de seconde le nuage qui vient de naître brûle de l'intérieur, orange à
la volée, refroidissant vers l'extérieur. Éclairée seulement du dehors — ce
qu'elle était — la flamme était cachée par la chose même qu'elle venait de
faire, et la bordée se lisait comme une machine à fumée.

Chaque bouffée porte donc un facteur d'embrasement qui décroît sur **son propre
âge** et non sur une horloge à part : elles naissent en roulement le long du
bord, donc chacune s'allume et refroidit à son propre rythme et le banc entier
ne s'embrase pas d'un bloc. Trois dixièmes de seconde, soit à peu près le temps
que la charge continue de brûler hors de la pièce.

Et la flamme elle-même est une **langue**, pas une étincelle : six sprites
plutôt que trois, lancés strictement vers le dehors le long de l'âme et
grandissant en chemin — donc un cône et non une boule — avec un ordre de rendu
qui empêche la fumée de l'avaler. De la lumière additive devant son propre nuage
est exactement ce qu'est une lueur de bouche.

**ET LA LUEUR ÉCLAIRE SON BORDÉ**, ce qui est une lumière et non un dessin. Le
billboard à la volée est le feu qu'on voit ; ceci est ce que ce feu **fait** —
un coup de jaune sur sa muraille, sur ses porte-haubans et sous la voile
au-dessus, parti avant qu'on l'ait tout à fait vu. Un sprite ne sait pas le
faire : il est devant le bois, pas dessus.

**Une RÉSERVE, bâtie une fois et jamais remise dans la scène**, et c'est toute
l'ingénierie de la chose. Three compile ses shaders contre le nombre de lumières
qu'il voit, donc ajouter une lumière et la retirer un dixième de seconde plus
tard fait recompiler **tous** les matériaux de la scène, deux fois, par pièce —
une bordée serait une douzaine de recompilations complètes et une saccade
visible. Elles sont donc créées au démarrage, laissées dans la scène, laissées
**visibles**, et commutées par leur seule intensité : zéro est éteint, le compte
de lumières ne change jamais, et rien n'est jamais reconstruit. Mesuré :

| | programmes |
|---|---|
| réserve, allumée puis éteinte | 16 → **16** |
| une lumière ajoutée à la scène | 16 → **18** |

Et les retirer ne les rend pas : le coût est payé de nouveau au prochain ajout.

Quatre lampes, parce qu'une bordée part en roulement : à un dixième de seconde
chacune et autant d'intervalle, trois peuvent se chevaucher et quatre est
confortable. La cinquième pièce à parler dans le dixième de seconde vole la plus
ancienne, qui est invisible — une lueur déjà en train de mourir.

Elle est posée un peu **en dehors** de la volée, la pièce tirant à travers sa
muraille : posée sur la volée même, elle se trouve à l'intérieur d'elle et lui
éclaire la batterie à travers la coque au lieu de ses œuvres mortes. Et elle est
**bornée en portée** — neuf mètres sur un navire de trente, moins du tiers de sa
longueur — parce qu'une lampe sans portée inonderait tout son bord et se lirait
comme un éclair.

**Son intensité, elle, a été réglée à l'œil sur une image où la FLAMME faisait
tout le travail**, et c'était faux d'un ordre de grandeur. Mesuré ensuite au
pixel, lampe seule et flamme éteinte, à 126 m : le premier réglage n'éclairait
que **six pixels**. Il ne faisait rien du tout, et ce qu'on prenait pour la
lueur était le sprite. Une lumière ponctuelle est en **candelas** et
l'éclairement va en I/d², donc les quelques dizaines qui paraissaient
raisonnables à côté d'un soleil réglé à 0,77 sont une bougie :

| intensité (cd) | 22 | 100 | **400** | 1 500 | 6 000 |
|---|---|---|---|---|---|
| pixels éclairés | 6 | 70 | **402** | 645 | 778 |
| gain moyen | 17 | 20 | **26** | 54 | 117 |
| gain du plus touché | 23 | 66 | **149** | 240 | 402 |

Quatre cents est le point où elle se lit franchement sans brûler : le pixel le
plus touché gagne 149 sur 765, moitié moins qu'à 1 500 et quatre fois moins qu'à
6 000, qui sortent tous deux en tache blanche. Leçon générale, et c'est la même
que celle des nuages : **un effet réglé à l'œil au milieu d'autres effets mesure
la somme, pas la part**. Il faut éteindre les voisins avant de juger.

**Grise, et disparue en dix secondes plutôt qu'en vingt-deux.** Le blanc se lit
comme de la vapeur ; c'est la grisaille autant que l'opacité qui fait lire
quelque chose qui a **brûlé** et non quelque chose qui a bouilli. Et elle s'en
va tôt mais toujours **progressivement**, la décroissance étant une courbe lisse
sur toute la vie plutôt qu'un palier suivi d'une extinction : un banc s'amincit
sur place au lieu de s'éteindre. Relevé, opacité moyenne : 0,73 · 0,40 · 0,27 ·
0,12 · 0,04, plus rien à 10 s.

**Les bouches sont lues dans le modèle, mais par le NOM DE MATIÈRE**, ce qui
est un écart aux vergues et aux mâts et demande d'être défendu. Ces deux-là ont
une forme qu'une règle peut énoncer — un espar est long et mince, un mât c'est
la même chose debout. Un tube de canon n'a pas cette chance : c'est un cylindre
court et épais, ce qui décrit la moitié des accessoires de pont. Pire, une
batterie entière est presque toujours **un seul maillage**, donc il n'y a même
pas un objet par pièce à tester. Ce qu'il y a, en revanche, c'est un modéliste
qui l'a déjà dit : les tubes du pirate portent une matière nommée
`black_canon`. Le contrat est donc **un mot dans un nom de matière**
(`/canon|cannon|gun/i`), ce qui est bien moins à demander qu'un mesh par
pièce, et un navire qui ne dit rien n'a pas de batterie et ne tire pas.

**Grouper D'ABORD, chercher le large ensuite** — l'ordre des deux est tout. Un
tube est posé en travers, donc il n'occupe que son propre diamètre en `z`
alors que les pièces sont à des mètres l'une de l'autre : un seul test d'écart
les sépare, le même qui range les vergues sur les mâts, et la volée est
simplement le sommet le plus au large de son propre groupe.

Fait dans l'autre sens, on en perd la moitié — ce que faisait la première
écriture. Prendre les sommets proches du point le plus large de **toute** la
batterie suppose que son flanc est un plan ; il ne l'est pas, il rentre vers
l'avant et vers l'arrière, si bien que les pièces de l'arrière sont en dedans
des pièces du milieu et tombaient hors de la fenêtre. **Six pièces trouvées là
où il y en a douze**, toutes sur l'avant — et c'est la forme même du navire qui
en était la cause. Corrigé, la lecture donne les douze, et l'on voit la muraille
rentrer : x passe de 7,54 à 6,72 m et y monte de 5,95 à 6,68 avec la tonture.

**Une pression, un coup ; la touche maintenue, la bordée.** Un appui fait parler
une seule pièce, et la batterie se descend d'avant en arrière appui par appui —
ce qui donne quelque chose à faire entre deux salves et correspond à la manière
dont on sert un pont quand on ne tire pas ensemble. Les deux gestes se
distinguent par le **drapeau de répétition du navigateur** plutôt que par une
horloge à nous : le premier événement d'une pression porte `repeat` faux et
tous les suivants vrai, et un verrou empêche un maintien long de lâcher salve
sur salve.

**La bordée part pièce par pièce, et à intervalles IRRÉGULIERS.** Elles étaient
tirées en roulement le long du bord, et pas pour la parade : une batterie lâchée
d'un seul coup est une seule poussée, et cela se lit comme un seul objet qui
casse. Même argument que les trois charges de la soute et que les mâts qui
tombent l'un après l'autre.

Mais un neuvième de seconde régulier entre les pièces est un **roulement de
tambour** — une machine, pas un équipage. Chaque canon a son chef qui attend son
moment, sa lumière, sa mèche ; les intervalles s'éparpillent, et de temps en
temps une pièce a un long feu et parle bien après sa voisine. Relevé sur quarante
bordées : durée totale de **0,38 à 1,86 s** (moyenne 0,81), écart médian entre
coups de 0,133 s, neuvième décile 0,347, et **15 %** de longs silences. Aucune
salve ne sonne comme la précédente.

**Et aucune charge n'est pareille.** La poudre était faite, dosée et bourrée à la
main : quelques pour cent d'écart sur la charge est généreux plutôt que
pessimiste, et cela éparpille la chute des coups tout seul, sans seconde règle
sur la précision.

**La fumée de poudre est PÂLE et ÉPAISSE**, et les deux ont été ratés dans le
même sens à la première écriture : à demi-opacité et sept dixièmes de gris, cela
se lisait comme un banc de **brume** couché le long du bord. Un canon et une
soute se séparent par la couleur autant que par la forme — la suie est un
incendie à bord, le blanc est de l'artillerie — et la densité n'est pas un goût,
c'est la raison même pour laquelle la fumée comptait : elle **aveuglait**.

**Et elle a sa propre texture, ce qui vaut vingt lignes.** La bouffée de
l'explosion est un dégradé radial dont on a mordu le bord, ce qui lui convient :
la fumée d'une boule de feu est mince et on en voit quelques-unes. Dessinez-en
quarante l'une sur l'autre à pleine opacité — ce que fait une bordée — et chaque
bord mordu tombe au même endroit : les dégradés s'additionnent en un galet blanc
parfaitement lisse. Le remède est des **grumeaux dans la texture** et non
davantage de sprites : une demi-douzaine de taches décentrées de tailles
différentes donnent à chaque bouffée une silhouette déchirée, et quarante de
celles-là, chacune tournée à son propre angle, ne s'accordent jamais et se
lisent comme des volutes. Même leçon que les voiles : ce que l'œil lit d'abord
est le **contour**, pas l'ombrage dedans.

**Et elle le SENT.** Pas une secousse décidée — l'arithmétique dit autre chose
et il vaut la peine de la laisser parler. Un boulet de douze part à quelque
440 m/s, donc avec les gaz derrière lui une pièce rend environ trois mille
kilogrammes-mètres par seconde à travers ses tourillons, six mètres au-dessus du
centre de gravité. Mesuré sur la frégate de 2 000 t, bordée de six pièces :
**1,07° de gîte** et 0,68 °/s de roulis, pour 0,009 m/s de translation. La
bordée qui couche un navire est l'une des choses les plus répétées sur la marine
à voile, et c'est à peu près un mythe : on la sent, elle ne renverse rien.

### Le boulet

**Il vole pour de vrai, et c'est la TRAÎNÉE QUADRATIQUE qui fait tout le
caractère du boulet rond.** Une sphère est un projectile déplorable : avec un
Cd voisin de 0,9, 5,4 kg et onze centimètres, la décélération vaut `c·v²` avec
`c = ρ·Cd·A/2m ≈ 0,001` par mètre. Ce seul nombre produit, sans un cas
particulier :

| portée | 100 m | 200 | 300 | 400 | 500 |
|---|---|---|---|---|---|
| vitesse | 358 m/s | 296 | 244 | 201 | 165 |
| temps | 0,27 s | 0,57 | 0,93 | 1,38 | 1,95 |
| **chute** | 0,3 m | **1,4** | 3,6 | 7,5 | **14** |

*(Calibre du navire de 30 m. Le même tableau sur la coque de 60 m donnait 1,2 m
de chute à 200 et 9,1 à 500 : un boulet deux fois plus gros porte plus loin,
`c` allant comme l'inverse du calibre.)*

Et donc la **portée de plein fouet**, qui n'a pas été décidée : on se battait à
deux cents mètres non par sauvagerie mais parce que c'est la distance à laquelle
une pièce pointée à plat touche ce qu'elle vise. À 500 m on est neuf mètres
bas ; à 800, c'est sans espoir. Le boulet est plus petit et plus léger sur un
petit navire, et `c` va comme l'inverse du calibre — la masse en cube quand
l'aire est en carré — donc son boulet perd son erre plus vite, sans second
réglage.

**Le tir est POINTÉ EN DESSOUS**, et c'est le troisième pointage : les deux
premiers se sont trompés dans le même sens. Trois degrés en l'air envoyaient le
boulet huit mètres au-dessus de sa lisse ; **à plat aussi**, et la mesure a dit
pourquoi : cette batterie est à six mètres et demi au-dessus de la mer, donc une
pièce à plat passe par-dessus une coque dont tout le franc-bord est moindre.
Six touches sur six à 160 m, six trous ouverts — et **pas une tonne d'eau en
cinq minutes**, tous étant au-dessus de sa flottaison. Ce qui est juste, et
inutile.

La pièce est donc pointée pour poser son coup **sur la mer** à une portée de
référence, ce que voulait dire *plein fouet* et à quoi servait le coin de mire :
d'un navire haut sur l'eau et à bout portant, il faut abaisser ou l'on tire dans
le gréement tout l'après-midi. L'angle sort de la hauteur de la volée au-dessus
de l'eau, donc une batterie basse est pointée à plat et une batterie haute bien
en dessous, sans rien à régler par navire. Relevé après correction : quatre
trous sur six **sous** la flottaison, deux au ras.

**L'essai se fait sur un SEGMENT, jamais sur un point.** Un boulet franchit
quatorze mètres en une image à soixante images par seconde, ce qui est plus
large que la coque qu'il doit toucher : essayé point par point il la traverse à
tous les coups, ce qui est la manière classique d'écrire un projectile qui ne
touche jamais rien. Et le vol est sous-divisé sur la **distance** et non sur le
temps — quatre mètres par pas — puisqu'il va quatorze mètres par image au départ
et deux à l'arrivée.

**La coque qu'il frappe est celle qu'on VOIT, et c'est ici que le modèle et le
solveur se sont contredits tout haut.** La grille de sondes est bâtie sur
`hull-lines.js` — longueur, largeur, franc-bord, tirant, tout de sa fiche —
tandis que le `.glb` est un autre objet, mis à l'échelle sur sa seule
**longueur**. Sur le pirate le solveur met son pont à y = 2,8 et le modèle met
ses sabords à y = 6,0 : trois mètres d'écart. Le premier coup est passé **six
mètres au-dessus d'elle**.

On ne peut pas simplement préférer le solveur : le joueur vise le bordé qu'il
regarde, et un boulet qui traverse l'image de sa muraille doit compter. La
**forme** est donc prise sur le modèle (`_hullShell()`, découpée dans les mêmes
compartiments que l'envahissement), et ce qu'on remet à la voie d'eau est une
**fraction** de son creux plutôt qu'une hauteur en mètres. Une fraction veut
dire la même chose dans les deux repères — zéro à la quille, un au livet — et
c'est exactement ce que `breach()` demande, si bien que le trou finit là où
l'œil l'a vu entrer sans que ni l'un ni l'autre ait eu à bouger.

**Le trou va comme le CARRÉ du calibre**, parce qu'un trou est une aire. Il
avait été écrit comme un dixième de mètre carré tout sec, ce qui n'est juste que
pour une seule taille de pièce : divisez le navire par deux et ses canons
suivent, mais un trou fixe dans une coque de huit fois moins de déplacement est
quatre fois la blessure. Mesuré avant correction, cinq trous coulaient un navire
de 240 t en **quatre minutes** là où six en condamnaient un de 2 000 en un quart
d'heure. Corrigé : 0,025 m² au lieu de 0,10, et elle sombre en douze minutes.

**Et ce qu'il coûte passe par ce qui existait déjà.** Un trou dans son bordé est
la même voie d'eau que la touche `B` ouvre, donc Torricelli, la carène liquide
et l'envahissement par le pont prennent le relais sans une ligne écrite pour
l'artillerie ; un mât est la même chute que la soute provoque. Rien de ce qui
arrive à un navire canonné n'est un cas particulier — ce qui est toute la raison
d'avoir bâti les deux d'abord. Mesuré, une bordée de six pièces à 160 m, pompes
en route :

| minutes | 0 | 2 | 4 | 6 | 8 | 10 | 12 | 14 |
|---|---|---|---|---|---|---|---|---|
| eau embarquée | 0 t | 75 | 160 | 256 | 371 | 504 | 657 | **834** |

Le débit **s'emballe**, les pompes perdent, et une seule bordée bien placée la
condamne en un quart d'heure. Une bordée ne coule donc pas un navire d'un coup,
et c'est juste.

**LES ÉCHARDES**, et c'est la chose que tous ceux qui y étaient ont écrite et
qu'aucune image n'a jamais montrée : le boulet lui-même tuait très peu de monde.
Ce qui vidait une batterie, c'était le **bois**. Un boulet à trois cents mètres
par seconde ne perce pas un trou net dans soixante centimètres de chêne, il fait
éclater le bordé vers l'intérieur et lance un nuage de poignards de chêne en
travers du pont. Les journaux de chirurgiens sont pleins de plaies d'échardes et
presque vides de boulets. Un impact qui ne donnerait qu'une bouffée et un trou
manquerait tout ce qu'une touche voulait dire.

**C'est le MÊME bois que l'épave d'une explosion** — même géométrie, même
culbute, même disparition dans la mer, même gerbe à l'entrée dans l'eau —
**taillé autrement**. Une explosion jette des morceaux de navire : courts,
épais, lents, cul par-dessus tête. Un boulet jette des éclats : longs, minces et
très rapides. Toute la différence tient dans les nombres remis à la même
machinerie, ce qui est l'intérêt d'avoir la machinerie.

**La plupart partent vers l'INTÉRIEUR**, le long du coup, parce que c'est là
qu'elles vont vraiment — et étant à l'intérieur elles sont cachées par son
propre bordé, ce qui n'est pas une perte mais la lecture juste : elles sont
entrées dans elle. Un peu moins de la moitié ressortent par le trou qu'il vient
de faire, et ce sont celles-là qu'on voit. Elles naissent un peu **en dehors**
du bordé, faute de quoi celles qui sortent seraient créées dans le maillage
qu'elles sont censées quitter.

**Et elles sont PÂLES**, ce qui est vrai et règle la seule vraie difficulté à
les dessiner. Le dehors d'un navire est vieilli et goudronné ; le dedans d'une
planche ne l'est pas, donc ce qu'un boulet arrache de sa muraille est du chêne
**cru**, bien plus clair que tout ce qui l'entoure. Des éclats sombres sur une
coque sombre à une encablure ne sont rien du tout ; du bois frais s'y détache.
Elles ont donc leur propre matière.

Leur **section** est grossie comme celle du boulet et pour la même raison — à
cette distance une écharde vraie fait trois pixels — mais leur **vol** est
honnête. Relevé, bordée de six pièces à 80 m : 16 à 21 échardes par touche, de
**0,40 à 1,55 m**, lancées de 5 à 19 m/s, **123 en l'air** après la salve.

**Et il a fallu transmettre le point d'impact.** La touche ne rendait qu'une
*fraction de hauteur*, ce qui suffit à l'envahissement — elle dit où l'**eau**
entre — mais ne peut pas dire d'où le bois s'envole. Deux questions différentes
sur le même événement, et chacune veut son propre nombre : `onStrike` rend
désormais aussi le point monde et la direction du coup.

**Un défaut trouvé par le banc au passage.** Le tir à la roulée était écrit
« attendre que la volée descende », et un navire immobile par calme plat ne
satisfait jamais cela : toutes les pièces partaient sur l'expiration du délai de
deux secondes et demie. On attend tant que la volée **monte** ; parfaitement
immobile compte comme tirable. Un chef de pièce attend la roulée quand il y a
une roulée à attendre ; sans elle, il tire.

**Un mât demande TROIS boulets.** Ils étaient épais comme une cuisse aux
jottereaux et faits pour être canonnés ; le perdre est le prix d'un feu soutenu
et non d'un coup heureux. Vérifié : trois touches et il part.

**Le boulet qui manque tombe à la mer**, et la gerbe est celle qui existait
déjà — six coups, six colonnes, à 248 · 248 · 256 · 256 · 293 · 304 m, ce qui
est bien le pointage à plein fouet.

**Mais elle était invisible, et pour une raison de fond.** Dimensionnée
honnêtement en volume — 1,6 m³ pour le trou qu'un boulet fait — elle sortait à
**0,7 m de haut avec dix-huit gouttes**, ce qui à la distance où l'on tire n'est
rien du tout. Le plafond de `splash.js` en est la cause, et il est juste
partout ailleurs : *la couronne d'une gerbe monte à peu près autant que la
cavité est large*. Une coque qui s'assoit dans un creux est émoussée et lente,
elle ouvre une cavité large et peu profonde. **Un boulet à trois cents mètres
par seconde en perce une étroite et profonde**, et l'eau qui se referme dessus
tire un jet de Worthington bien plus haut que la cavité n'est large — grand,
mince, et sans rapport avec la gerbe d'un navire. C'est exactement le cas que la
règle ne décrit pas, donc l'appelant peut relever le plafond (`jet`, absent =
1, rien ne change ailleurs) et dit pourquoi.

Et le **volume** est fixé par une contrainte qui n'a rien à voir avec l'eau : la
réserve de gouttes en contient deux mille et une gerbe en prend onze par mètre
cube, donc une bordée de six doit y tenir ou les dernières pièces volent les
premières par le curseur tournant. Vingt-six mètres cubes font 286 gouttes
chacune, 1 716 pour la salve. Relevé : **9,1 m de haut, 286 gouttes**, contre
0,7 m et dix-huit — et c'est la densité bien plus que la hauteur qui fait lire
une colonne à distance.

**Et la FORME sort du même nombre**, plutôt que d'un second bouton, parce que
c'est la même cause. Une cavité large et plate jette son eau en dehors et fait
une **couronne** ; une cavité étroite et profonde la tire presque à la verticale
et fait une **colonne**. Ce qui relève le plafond est exactement ce qui redresse
le panache, donc un seul facteur dit les deux : moins de gouttes dans la collerette
basse, plus raides au-dessus, et une bouche plus petite. Élancement mesuré,
hauteur sur demi-largeur :

| | avant | après |
|---|---|---|
| boulet | 0,89 | **1,74** |
| coque, frégate | — | 0,54 |
| coque, chaland | — | 0,49 |

Un appelant qui ne demande rien reçoit la couronne **inchangée dans le moindre
détail** : `jet` absent donne `tight = 0`, et chacun des quatre termes retombe
alors exactement sur son ancienne valeur. La gerbe de coque ne bouge pas d'un
pouce — 420 gouttes et 2,4 m avant comme après.

**Le boulet est dessiné bien au-dessus de sa taille** (0,55 m de rayon pour onze
centimètres réels), et c'est délibéré : à un demi-mille il ferait un tiers de
pixel et n'existerait tout simplement pas. Même argument que la lueur lointaine
de la lanterne, et même réponse. Son **vol** est exact ; seul son diamètre est
un mensonge.

**ELLE TIRE À LA ROULÉE**, et ce n'est pas un raffinement : sans cela les pièces
ne servent à rien dès qu'il y a de la mer.

Le pointage vaut huit centièmes de degré. Son mouvement en vaut plusieurs, et il
va droit dans le canon. Chaque pièce attend donc son moment, comme son chef le
faisait : une fois son tour venu dans le roulement, elle tient jusqu'à ce que la
volée **descende**, et parle alors. C'est le plus vieux tour d'un pont de
batterie et la raison pour laquelle les bordées étaient déchirées — chaque chef
jugeant sa propre roulée, ce qui est la même irrégularité que le roulement, mais
née plutôt qu'imposée. Et il ne peut pas attendre indéfiniment : deux secondes et
demie, puis il tire quoi qu'elle fasse.

**Qui descend, et non qui est horizontale**, ce qui a demandé un détour. Une
pièce pointe **en travers**, donc son **tangage** ne la touche presque pas :
tourner autour de l'axe transversal laisse un tube transversal où il était. Ce
qui lève un canon, c'est sa **gîte** — et sous voiles elle en porte une
permanente, la batterie au vent regardant le ciel et celle sous le vent
regardant l'eau, ce qui fut vrai de tout navire ayant jamais combattu à la
voile. Attendre l'horizontale n'arrivait donc jamais au vent, et toutes les
pièces partaient sur l'expiration du délai : les mêmes 800 m par calme plat que
par gros temps, ce qui est la façon dont la faute s'est signalée.

**Et la pièce est pointée sur l'HORIZON, pas sur son pont.** Un chef de pièce
vise le long de son tube la flottaison de l'ennemi et joue du coin de mire
jusqu'à ce que ça porte ; la gîte est son affaire, pas celle du boulet. Pointée
sur le pont — ce que revient à faire prendre l'assiette de la coque telle
quelle — la gîte permanente décide de tout, et la mesure fut brutale :

| batterie, force 4 | avant | après |
|---|---|---|
| au vent | 830 m | **274 m** |
| sous le vent | 45 m | **241 m** |

Un bord visant la lune et l'autre tirant dans sa propre muraille. Corrigé, les
deux tombent dans la même fourchette de 200 à 320 m, par calme comme par force
4, et la dispersion qui reste est celle des charges et du roulis.

**Ce qui reste à faire** : le gisement. Les pièces tirent perpendiculairement au
bord — on choisit le moment et le bord, pas encore la direction.

## Un mât qui tombe

**Deux groupes emboîtés, et l'emboîtement est toute l'astuce.** Un mât s'abat
sur son **pied**, donc ce qui tourne doit avoir son origine au niveau de
l'emplanture. Brasser, au contraire, est une rotation autour de l'axe
**vertical** — et une rotation autour d'un axe est la même où qu'on place
l'origine le long de cet axe. Le groupe extérieur peut donc descendre au pied
gratuitement, et `setTrim` continue de faire tourner l'intérieur exactement comme
avant, sans rien savoir de tout cela. Le fût est porté par l'extérieur, les
vergues et la toile pendent dans l'intérieur, où elles étaient déjà : tout ce
qui appartient à ce mât passe par-dessus bord ensemble.

**Le mât se trouve par la forme, comme la vergue, le test mis debout** : haut,
mince **des deux côtés**, et sur l'axe. Mince des deux côtés est ce qui fait le
travail — cela écarte tout ce qui est soudé à ses voisins, ce qui est l'état
ordinaire d'un modèle importé et décide de ce qui peut tomber ou non. Relevé
sur `pirateship.glb` :

| mesh | dx | dy | dz | verdict |
|---|---|---|---|---|
| `Cylinder` | 1,1 | 38,7 | 1,1 | un mât propre, il tombe |
| `Cylinder001` | 0,9 | 34,4 | **43,1** | deux ou trois mâts fondus en un seul mesh — refusé |
| `Cylinder004…008` | 13–21 | 0,3–0,5 | 0,3–0,6 | les vergues, chacune la sienne |

Le refus est voulu : sans mesh à lui, la toile tomberait d'un espar resté en
l'air, ce qui est pire que rien. **Modéliser un mesh par mât est donc la seule
exigence** — les origines, elles, n'ont pas à être placées, la boîte englobante
donnant le pied.

**C'est un PENDULE, pas une animation.** Un espar articulé à son emplanture est
une tige homogène sur un pivot, et cela a une équation — `a" = (3g/2L)·sin a` —
qu'il vaut mieux employer qu'une courbe dessinée à la main, pour une raison :
elle porte la **taille** du navire. Le taux va comme l'inverse de la racine de
la longueur, donc un petit mât fouette pendant qu'un lourd s'incline longtemps
d'abord, sans qu'un nombre ait été réglé pour l'un ni pour l'autre. C'est
l'argument de Froude déjà employé pour la gerbe, et la raison pour laquelle une
maquette ne paraît jamais grande.

Elle a aussi la bonne forme dans le temps toute seule : à peine mobile, puis
d'un coup. Un mât ne bascule pas, il pend, il s'incline, et il part — et aucune
courbe adoucie ne le reproduit, parce que ce qui le fait est que le moment de la
pesanteur croît avec l'angle même qu'il produit. Mesuré sur le grand mât du
pirate, 38,7 m :

| temps | 0 | 0,5 s | 1,0 | 1,5 | 2,0 | 2,5 | 3,0 | 3,6 |
|---|---|---|---|---|---|---|---|---|
| inclinaison | 2° | 8° | 14° | 21° | 30° | 42° | 59° | **80°** |

Un mât de quinze mètres fait le même parcours en **2,2 s** contre 3,6.

**Elle s'arrête à quatre-vingts degrés**, pas à plat : un vrai mât passe
par-dessus bord et **s'arrête net dans ses propres haubans**, ce qui est
justement pourquoi un navire démâté est traîné par son gréement au lieu d'en
être débarrassé. À quatre-vingt-dix, on lirait un arbre abattu.

**Et la toile perdue est perdue pour de bon**, ce qui est la moitié de
l'intérêt. Le modèle rend la fraction de gréement encore debout, pondérée par
la **surface que chaque mât porte** et non par le nombre de mâts — un artimon
n'est pas un grand mât — et le solveur la multiplie dans la pression, exactement
comme la fraction de toile établie. Un seul nombre, donc l'image et la physique
ne peuvent pas diverger. Le grand mât du pirate porte la moitié de sa voilure :
tombé, `standing` vaut 0,501 des deux côtés.

La décroissance suit le **cosinus** de l'inclinaison, mais **remappé** pour
s'annuler là où elle s'arrête et non à quatre-vingt-dix : un cosinus brut lui
laissait huit pour cent de sa poussée avec les voiles déjà dans l'eau, en train
d'y être traînées.

**La soute qui saute les prend tous** — mais pas ensemble. Ils partent à
quelques dixièmes de seconde d'écart et alternativement de chaque bord, pour
exactement la raison des trois charges : au même instant cela se lit comme un
seul objet qui casse, échelonné cela se lit comme un navire qui se disloque.

**Ce qui manque encore**, et qu'il faudra pour un galion : une **antenne
latine** n'est pas reconnue. Le détecteur de vergues exige un espar posé en
travers de l'axe, et une antenne est inclinée dans le plan longitudinal — le mât
sera bien trouvé et tombera, mais nu. La civadière sous le beaupré passe, elle :
le code lit déjà un « mât » à 40,7 m sur l'avant du pirate.

## Les quatre caméras

**La vue de poursuite a été retirée**, remplacée par une vue de **proue**
plantée en avant de sa route. L'arrière est le seul relèvement d'où un carré ne
montre presque rien de lui-même : les voiles sont vues par la tranche ou se
masquent l'une l'autre, et le sillage — ce que la vue existait pour montrer —
est la partie de lui qui bouge le moins. De l'avant il présente tout son plan de
voilure, sa lame d'étrave et sa gîte, et chacun des trois répond à la barre. La
distance qui grandissait avec la vitesse est partie avec : elle cadrait le
navire différemment à chaque nœud, ce qui est une drôle d'exigence pour une
caméra.

**Proue et Fixe sont la MÊME caméra** et partagent chacune de ses lignes :
plantée dans le monde, position et cap verrouillés, braquée sur le navire une
fois puis laissée tranquille. Seule la **station** diffère — en avant sur sa
route, ou par sa hanche tribord — et cette unique différence vaut deux entrées
au menu, un navire qui vient sur vous et un navire qui s'en va n'étant pas le
même plan. Les écrire comme deux caméras aurait voulu dire tenir en accord deux
gestions de glissement, deux zooms et deux recentrages, sans le moindre gain.

Une seule finesse de station : la vue de proue se plante un peu **en dehors** de
sa ligne de route et non dessus. Dessus, le navire viendrait droit sur
l'objectif ; à côté, il s'ouvre de bout en bout du plan à mesure qu'il passe, ce
qui est tout l'intérêt de la vue.

Une caméra plantée n'avance ni ne recule : la molette **ouvre la focale**, elle
ne déplace rien. Vérifié, ancre inchangée au millimètre pendant que le navire
passait de 92 à 87 m, focale de 55° à 75°.

Et la vue **Fixe** garde son indice, ce qui est voulu : le naufrage y bascule
(`setMode(3)`), la touche `X` la replante, et rien de tout cela n'a eu à
bouger. Une caméra nommée dans le code par son numéro se remplace en gardant son
numéro. `X` replante désormais l'une comme l'autre.

**Un piège de mise en route.** Proue est la vue de départ, et une caméra plantée
qui n'a jamais été plantée regarde l'origine du monde. Rien ne la plante au
démarrage : `setMode` le fait, mais le mode initial est une affectation, pas un
appel — le constructeur appelle donc `setMode(0)` pour de bon, et `update`
plante à la première image si personne ne l'a fait. Même raison pour
`setSpec` : un nouveau navire n'est pas là où était l'ancien, la station est
donc reprise.

**Les touches.** `G` donne la bordée tribord, `⇧G` celle de bâbord —
un seul moyen mnémotechnique et deux batteries.

**`H` fait le vide.** Tout ce qui n'est pas la mer disparaît, message de naufrage
compris : le but est une image propre, et une demi-interface est pire que
l'interface entière. Les commandes continuent de répondre — on masque les
cadrans, on ne met pas le navire en panne. `F` cache le seul plan d'arrimage,
qui ne sert qu'à quai.

## Conventions

Interface et commentaires en français pour l'utilisateur ; commentaires de code
en anglais. Explication du *pourquoi*, pas du *quoi*.
