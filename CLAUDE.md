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
| `ship-spec.js` | lit une fiche JSON et en **dérive** tout ce que le solveur consomme |
| `hull-lines.js` | le plan de formes, en fonctions pures |
| `stage.js` | renderer, scène, lumière, ciel |
| `ocean.js` | houle de Gerstner : shader GPU **et** échantillonnage CPU |
| `foam.js` · `ssao.js` | champ d'écume persistant · occlusion ambiante du navire |
| `underwater.js` | la coque vue à travers l'eau, extinction par canal |
| `ship-model.js` | coque, gréement, voiles, sillage, chargement .glb |
| `ship-physics.js` | sondes, corps rigide 6 ddl, gouvernail, voiles |
| `controls.js` · `camera-rig.js` · `hud.js` | barre, caméras, instruments |

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

Ce qui suppose encore un navire unique, et qu'il faudra lever :

- **la mer et l'écume ne connaissent qu'une coque.** `uShipPos`, `uShipFwd`,
  `uShipHalf`, `uShipSpeed`, `uHullProf` et `uHullEnds` décrivent *un* bâtiment,
  et `ocean.js` comme `foam.js` les lisent tels quels. Le champ d'écume est déjà
  en espace monde, donc y **déposer** plusieurs navires est naturel ; c'est le
  collier instantané de `ocean.js` qui demandera un tableau ou une passe par
  navire ;
- **la fenêtre d'écume et la boîte d'ombre suivent un seul navire**
  (`foam.update(..., body.pos)`, `stage.aimSun(body.pos)`) : deux bâtiments
  éloignés ne peuvent pas être servis par la même fenêtre de 620 m ;
- **`main()` ne câble qu'un exemplaire** de chaque objet.

En revanche la barre, les instruments et les caméras n'ont *pas* à devenir
multiples : ce sont ceux du navire qu'on commande. Il leur faudra désigner
lequel, pas se dupliquer.

## Invariants à ne pas casser

**Un seul plan de formes.** `hull-lines.js` sert à la fois au maillage visible et
à la grille de sondes. Si les deux divergent, ce qu'on voit ne correspond plus à
ce qui flotte — c'est l'invariant le plus important du projet.

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

**Encodage.** `Get-Content` en PowerShell 5.1 lit en ANSI, pas en UTF-8 : extraire
puis réécrire un fichier accentué produit du mojibake et un BOM. Utiliser
`[System.IO.File]::ReadAllText/WriteAllText` avec un encodage explicite.

**Git et les .glb.** Ils sont binaires ; une conversion de fins de ligne les
corromprait silencieusement. `.gitattributes` les marque `binary`.

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

**Mise en ligne.** `node build.js` écrit `ships/index.json`. Le serveur de dev
répond à ce chemin par un listage en direct, sans fichier ; un hébergeur statique
non. Sans cet index, la page retombe sur la liste courte de `config.js` et tout
navire ajouté depuis n'est jamais demandé — ses .glb paraissent alors ne pas se
charger alors qu'ils n'ont jamais été réclamés. Le plus sûr reste de déployer
`dist/naval-sim.html` seul, qui embarque tout.

**Console de mer.** Quatre réglages : force de la houle (Beaufort), direction du
vent, hauteur du soleil (négative = nuit) et **creux**, qui multiplie la hauteur
significative au-delà de la table Beaufort. Ce dernier existe parce qu'un spectre
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

**Ferler prend la toile, pas les espars.** `setTrim` masque `this.canvases`, plus
jamais le groupe entier. Un navire à sec de toile garde ses vergues en croix et
sa bôme en place — et pour un modèle importé, masquer le groupe lui arracherait
son gréement, puisque ses propres vergues y vivent désormais.

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
`_sailSurface()` construit une grille sur les quatre coins et la pousse le long
de sa normale selon `sin(πu)·sin(πv)` — nul sur tous les bords, la toile étant
enverguée et bordée à ses points, maximal au milieu. Le creux n'est pas figé
dans la géométrie : `setSailShape()` le règle à chaque image sur `sailLoad`, la
pression que le solveur calcule **déjà** pour propulser le navire. La voile se
gonfle donc en se bordant et se vide dès qu'on choque, sans seconde règle à
tenir en accord avec la première. Le champ `belly` d'une fiche est le creux en
mètres à pleine charge (3,3 m sur la Roter Löwe, soit 16 % de la largeur des
basses voiles).

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

**Le pavillon montre le vent, les voiles montrent le réglage.** Un pavillon blanc
uni est envergué à la tête du grand mât, trouvé par la même lecture de forme que
les vergues : sur un modèle, la pièce la plus haute, bien plus haute qu'épaisse
et sur l'axe ; sur un navire procédural, simplement son plus grand mât. Un
bâtiment sans mât dans son modèle n'en porte pas, ce qui est le comportement
voulu — il n'a pas de drisse.

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

**Réglage des voiles.** Le modèle ne donne aucun retour lisible : à 45° de vent
apparent, des écoutes à 40° ne laissent que 5° d'incidence, donc `CL` s'effondre
et la poussée tombe au cinquième — sans que rien ne l'annonce, la console
affichant « voiles établies » et un nombre de kN plausible. D'où le repère vert
sur la barre d'écoutes, à `optimalAoA`, et la touche `T` qui y borde
directement. L'écoute initiale n'est plus une constante : elle est prise sur le
repère à la **première image** après l'armement, car `settle()` fait flotter le
navire dans un calme plat et le vent apparent n'existe qu'une fois `refreshSea()`
passé.

**Débogage.** `Naval.app` expose les instances vivantes (`stage`, `ocean`,
`foam`, `physics`, `ship`, `cam`, `hud`) depuis la console. `Naval.app.stage.strike()`
déclenche un éclair à la demande. Plusieurs bugs de ce projet ont été longs à
cerner faute de pouvoir inspecter quoi que ce soit à l'exécution.

**Brume.** Volontairement **découplée** de l'état de la mer. Physiquement un coup
de vent charge l'air, mais cela fermait l'horizon précisément quand les grosses
lames devenaient intéressantes à regarder.

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

## Conventions

Interface et commentaires en français pour l'utilisateur ; commentaires de code
en anglais. Explication du *pourquoi*, pas du *quoi*.
