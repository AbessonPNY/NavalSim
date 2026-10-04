# Le monde : la Jamaïque

Le monde est la vraie Jamaïque, **formes réelles, distances et tailles × 0,4**,
**hauteurs × 0,25**. La carte marine affiche les vraies latitudes et longitudes.

Elle a d'abord été toute la mer des Caraïbes au dixième, et la rade de Kingston
y tenait en trois cents mètres — « un petit port breton ». À 0,4 et recadrée
sur l'île, la rade fait un vrai mille (Port-Royal – Passage Fort 1,3 M), l'île
entière 36 M d'une pointe à l'autre, pour la même finesse de côte (45 m par
pixel) et deux fois moins de mémoire. Cuba, Hispaniola et la Terre-Ferme en
sont sorties ; elles reviendraient comme d'autres régions.

Tout vient de deux fichiers, et rien d'autre dans le jeu ne connaît la forme
de la terre :

| fichier | ce qu'il dit |
|---|---|
| `world/caraibes.json` | la région : où l'image se place en latitude/longitude, comment le gris devient une hauteur, les **ports**, les **modèles** posés |
| `world/caraibes-relief.png` | le **relief**, en niveaux de gris, à peindre |

## Peindre le relief

Ouvrez `world/caraibes-relief.png` dans Photoshop, GIMP ou Krita, en
**niveaux de gris 8 bits**, sans changer sa taille ni son cadrage.

- **Gris 128 (moyen) = le rivage.** Plus clair = terre, plus foncé = mer.
- Le gris n'est **pas linéaire** : la hauteur croît comme le carré de l'écart
  au gris 128, pour que les premiers mètres — ceux qui échouent un navire —
  aient beaucoup de nuances.

| gris | hauteur (jeu) | | gris | profondeur |
|---|---|---|---|---|
| 255 | 1 500 m | | 128 | 0 m |
| 192 | 381 m | | 124 | −0,4 m |
| 160 | 95 m | | 110 | −8 m |
| 140 | 13 m | | 100 | −19 m |
| 132 | 1,5 m | | 64 | −100 m |
| 129 | 0,1 m | | 0 | −400 m |

(`hauteur = 1500 × ((gris−128)/127)²`, `profondeur = −400 × ((128−gris)/128)²`,
réglables dans `relief` : `maxHeight`, `maxDepth`, `curve`.)

- Un pixel vaut **45 m de jeu** (112 m réels). Une langue de terre plus fine
  qu'un pixel et demi disparaît : peignez-la d'au moins 2 pixels.
- Les **fonds** comptent pour la navigation : 12 m à 40 m du rivage, puis le
  plateau ; les profondeurs ne sont **pas** réduites (une quille est une
  quille). Pour une passe ou un chenal, gris ≤ 110 (8 m et plus).
- Autour de chaque port, le bassin est de toute façon creusé à
  `harbourDepth` (11 m) jusqu'au quai.
- Rechargez la page : le jeu relit l'image. `node build.js` l'embarque.

### Un relief LOCAL, plus fin (patch)

Un pixel de la grande image vaut 45 m : une pointe de sable de 300 m y tient en
sept pixels, et aucun port ne peut être fidèle à ce compte. On pose donc une
SECONDE image, fine, sur un bout de la région :

```bash
node tools/relief-patch.js port-royal 2048 2000    # 2048 px pour 2000 m : ~1 m/px
```

Elle est peinte d'après le relief actuel — un patch neuf ne change donc RIEN —
et son entrée est ajoutée à `world/caraibes.json` :

```json
"patches": [
  { "key": "port-royal", "image": "world/port-royal-relief.png",
    "west": -76.856648, "east": -76.809353, "south": 17.915345, "north": 17.960341,
    "feather": 80 }
]
```

Le gris s'y lit par la **même loi** que la grande image, et se **fond** dans
elle sur `feather` mètres au bord : aucune marche au bord du carré. Tout ce qui
lit la terre suit — flottaison, échouage, écume du rivage, carte marine.

Deux façons de la travailler :

- **À la peinture**, comme la grande image (mêmes gris, mêmes règles).
- **À la sculpture**, dans Blender : sortez le terrain, sculptez, rendez-le.

```bash
node tools/zone-glb.js port-royal 1800 1400          # le terrain en .glb (pas = la finesse du patch)
# … sculptez l'objet « terre » dans Blender, exportez au même endroit …
node tools/relief-bake.js world/models/port-royal-zone.glb port-royal
```

L'aller-retour a été mesuré : 3,6 cm d'écart moyen sur la rade. Ce que le
maillage ne couvre pas garde son gris, donc on peut sculpter un coin seulement.

**Ce que ça coûte.** Godot bâtit la terre aussi fine que sa source, borné par
`LandNode.FineMax` (288 segments par carreau de 1440 m, soit 5 m). Relevé à
Port-Royal : 4,26 ms par image et 1 169 k triangles avec le patch, contre
3,12 ms et 514 k sans. La page publiée embarque l'image locale comme la grande.

### Repartir des côtes réelles

```bash
node tools/region-heightmap.js
```

réécrit l'image à partir de `world/sources/natural-earth-caraibes.json`
(côtes Natural Earth 1:10 M, domaine public) et de
`world/sources/reliefs.json` (les chaînes de montagnes : crête en
[lat, lon], hauteur réelle, demi-largeur réelle ; les bandes de terre trop
fines comme les Palisadoes ; les îlots). **Cela écrase les retouches.**

## Les ports

Dans `world/caraibes.json` → `ports` :

```json
{ "key": "port-royal", "name": "Port-Royal", "lat": 17.9425, "lon": -76.8330, "quay": 0, "start": true }
```

| champ | rôle |
|---|---|
| `key` | identifiant (quêtes, marché) |
| `name` | le nom affiché |
| `lat`, `lon` | la ville ; le jeu cherche le rivage depuis là… |
| `quay` | …dans ce relèvement (degrés vrais, 0 nord, 90 est) : c'est la direction où le ponton avance dans l'eau |
| `start` | le port où la partie commence |
| `mole` | `true` : un bassin fermé par un môle, qui abrite de la houle (par défaut non : un port naturel) |
| `wild` | `true` : un **débarcadère** — le ponton seul, sans ville, sans marché, et où aucun marchand ne fait route. Un enclos à bétail, une anse où l'on charge : un endroit où l'on accoste, pas où l'on commerce |

Le ponton va du rivage jusqu'à 9 m d'eau (150 m au plus). Un port dont le
rivage est introuvable à 3 km dans ce relèvement est ignoré, avec un message
dans la console.

**Les rades de la Jamaïque** — `passage-fort`, `old-harbour`, `withywood`,
`black-river`, `savanna-la-mar`, `negril`, `lucea`, `montego-bay`,
`dry-harbour`, `port-maria`, `port-antonio`, `port-morant`, `yallahs` — sont
celles que porte une carte anglaise de la fin du XVIIe siècle, à leurs vraies
latitudes. Elles font le tour de l'île dans le sens des aiguilles depuis
Port-Royal. De Port-Royal : Passage Fort 1,3 M, Old Harbour 5,9 M, Yallahs
6,2 M, Port Morant 11,6 M, Negril — la pointe de l'ouest — 35,5 M.

Avant d'écrire une fiche, on peut lire ce qu'elle donne :

```bash
dotnet run --project core/NavalSim.Lab -- ports          # rivage trouvé, longueur de la jetée, fond à la tête, abri
```

## Les villes

Chaque **port** bâtit la sienne tout seul : on ne mouille pas devant un rivage
désert. Pour un lieu habité SANS port — Kingston, sur la rive d'en face —,
`world/caraibes.json` → `towns` :

```json
{ "key": "kingston", "name": "Kingston", "lat": 17.9715, "lon": -76.7935,
  "radius": 820, "houses": 420 }
```

| champ | rôle |
|---|---|
| `lat`, `lon` | le cœur de la ville ; le semis part de là en spirale |
| `radius` | jusqu'où elle s'étend (m de jeu) ; elle s'y effiloche |
| `houses` | le nombre voulu — on en obtient moins si le terrain s'y refuse |

Les maisons sont **à l'échelle du navire** : 5 à 9 m de large, 3 à 6 m au mur,
un entrepôt jusqu'à 16 × 21. Elles ne se posent que sur du terrain à plus de
1,2 m au-dessus de l'eau, à moins d'un sur quatre de pente, entre 11 et 700 m du
rivage, et leur façade regarde l'eau. Rien à modéliser : deux maillages
multipliés.

### Bâtir un port à la main

Pour modeler une ville fidèle — Port-Royal — sortez le terrain du jeu et
travaillez dessus :

```bash
node tools/zone-glb.js                     # Port-Royal, 1800 × 1400 m au pas de 4 m
node tools/zone-glb.js port-royal 2400 1800 3
node tools/zone-glb.js kingston
```

Écrit `world/models/<lieu>-zone.glb` (hors dépôt : il se regénère) avec quatre
objets — `terre` (le relief exact, lu par la même loi que le jeu), `mer` (le
niveau zéro), `ponton` et `mole` (l ouvrage du port, à sa place et à son cap).
L ORIGINE DU FICHIER EST LE LIEU : ce que vous poserez dessus se replace dans le
jeu aux mêmes coordonnées, par `assets`. Relevé pour Port-Royal : relief de
−103 m à +18 m, le lieu à x −342,5, z 15,2 (17,9378° N, 76,8330° O).

### Les bâtiments (Godot)

Trois modèles dans `world/models`, écrits par `node tools/town-glb.js` et
modifiables dans Blender :

| fichier | ce que c'est | emprise |
|---|---|---|
| `eglise.glb` | nef, clocher, flèche et croix — **une seule par ville**, sur la plus grande parcelle près du centre | 9 × 18 m, croix à 18 m |
| `maison-a.glb` | case basse à véranda | 7 × 5 m |
| `maison-b.glb` | maison de ville à étage et balcon | 6 × 6 m |

Conventions : **origine au sol**, au milieu de l'emprise, **façade vers +z**,
mètres vrais. Le jeu met chaque bâtiment à l'échelle de sa parcelle **sans le
déformer** (le plus petit des deux rapports), et le tire au sort d'après sa
position : la même maison au même endroit d'une partie à l'autre.

Ce qui compte est le **nom des matières**, car chacune devient un MultiMesh :

| matière | rôle |
|---|---|
| `mur` | le crépi ; il prend la teinte de la maison (une par instance) |
| `toit` · `bois` · `pierre` | gardent la couleur du modèle |
| `fenetre` | s'allume la nuit, comme les fanaux (`shaders/town_glass.gdshader`). Plusieurs groupes — `fenetre`, `fenetre_002`, `fenetre_003`… (le numéro final fait le groupe) — s'allument chacun selon son tirage, maison par maison, et chacun à son heure du crépuscule |

Un modèle manquant ou illisible : la ville retombe sur ses boîtes et le dit en
console.  Mesuré à Port-Royal : 5,13 ms par image avec les modèles contre 4,80
en boîtes, 282 appels de dessin contre 203.

## Les modèles posés

Dans `world/caraibes.json` → `assets` :

```json
{ "name": "Fort Charles", "glb": "world/assets/fort-charles.glb",
  "lat": 17.9368, "lon": -76.8420, "yaw": 30, "scale": 1 }
```

| champ | rôle |
|---|---|
| `glb` | le modèle (textures **incluses** dans le .glb) |
| `lat`, `lon` | où il est posé |
| `yaw` | orientation, degrés vrais (0 : l'avant du modèle, +Z glTF, vers le nord) |
| `scale` | facteur d'échelle (1 par défaut) |
| `y` | hauteur de son origine ; absent, il est posé sur le relief |

Modélisez **à l'échelle du jeu** : les bâtiments et les navires sont à taille
réelle, seules les distances entre les lieux sont réduites. Un fort de 60 m
fait 60 m. `node build.js` embarque les modèles.

## Les semis (Godot)

Un modèle répandu au hasard sur une zone — les rochers d'une plage. Dans `world/caraibes.json` → `semis` :

```json
{ "name": "Rochers des plages de Port-Royal", "glb": "props/rocher.glb", "patch": "port-royal",
  "nombre": 48, "taille": [0.4, 2.2], "frange": [-0.4, 1.6], "penche": 18, "graine": 23 }
```

| champ | rôle |
|---|---|
| `patch` | la zone : la clé d'un carreau de relief (`patches`) ; ou bien `lat`, `lon`, `rayon` (m) |
| `nombre` | combien en poser (moins s'il n'y a pas la place) |
| `taille` | sa plus grande dimension en mètres, tirée entre les deux — le .glb y est ramené quelle que soit son échelle ; les petits sont les plus nombreux |
| `frange` | l'altitude où il se pose (m) : une plage, de l'eau à la laisse de haute mer |
| `penche` | son inclinaison au plus, en degrés ; la rotation autour de la verticale est libre |
| `graine` | le hasard est tenu : les mêmes rochers aux mêmes places d'une partie à l'autre |
| `ecart` | l'écart minimal entre deux, en mètres ; absent, il va comme leur taille (un rocher). Un arbre loge sa couronne, pas sa hauteur |
| `enfonce` | de combien il s'enfonce, en part de sa demi-hauteur (0,4 ; un arbre : presque rien) |
| `visible` | jusqu'où on le dessine, en mètres (900 ; un cocotier se voit du large) |
| `abri` | `[min, max]` : l'abri où il vit, de 0 (le fond d'une rade) à 1 (le large) — le même que celui de la mer. Un corail ne pousse pas dans la vase d'un port |
| `amas` | `[centres, rayon]` : en pâtés — tant de centres tirés dans la zone, chaque pièce à moins du rayon de l'un d'eux. Les centres sont tirés sur la seule `graine` : deux semis de même graine dans la même zone partagent leurs récifs |
| `herbier` | `true` : seulement sur les prés d'herbier que le fond peint de lui-même (`core/Seabed.cs`, jumelle de `seabed.gdshaderinc`) |
| `foule` | `true` : des milliers de petites pièces, dessinées par cases de 48 m (MultiMesh) au lieu d'un nœud chacune ; on ne les reprend pas une à une en mode création |
| `ecueil` | `true` : un ÉCUEIL — la coque le heurte et s'y ouvre (rochers, têtes de corail). Il est inscrit à sa place vue : déplacé, retiré ou copié en mode création, son danger le suit |
| `recifs` | `true` : seulement sur les récifs de la fiche (le corail des cayes) |
| `ondule` | une foule qui ondule avec le ressac de la houle (`seaweed.gdshader`) ; sa souplesse : 1 pour l'herbe, 0,35 pour une gorgone cornée. Fort dans les hauts-fonds, éteint par grand fond, calmé par l'abri |

Jamais à moins de 35 m d'un ponton, jamais l'un dans l'autre (d'un même semis). Chacun est recentré sur sa boîte.
Deux semis sur deux franges qui se touchent font un premier rang et un fond : les cocotiers de l'îlot, détaillés
sur la grève (1,2 à 3,2 m), allégés derrière (2,8 à 12 m). Sous l'eau (`frange` négative), une pièce ne dépasse
jamais l'eau qui la couvre. Godot seulement.

### Le fond

Le fond se peint de lui-même (`godot/shaders/seabed.gdshaderinc`) — sable blanc des hauts-fonds, vase des eaux
calmes et du grand fond, herbier par plaques d'un à douze mètres, roche sur les tombants — et le pinceau du mode
création passe par-dessus. Les récifs de Port-Royal sont des semis en foule : corail cerveau, corne d'élan,
corne de cerf, gorgones, éponges, oursins, touffes d'herbier, rochers. Leurs modèles (`props/fond/`) sortent de
`node tools/reef-glb.js` : remplacer l'un d'eux par un modèle de Blender suffit, le semis le ramène à sa taille.
Essai : `--fond corail` (ou `herbier`, `rochers`…) pose l'œil sous l'eau sur le pâté le plus peuplé ;
`--fond 2616,-1992` sur un point (mètres vrais).

## Les récifs (Godot)

`world/caraibes.json` → `recifs` : des plateaux de corail qui affleurent, à leur place réelle
(les cayes de Port-Royal : Gun Cay, Rackham's Cay, Lime Cay, Maiden Cay, Drunkenman's Cay).

```json
{ "nom": "Lime Cay", "lat": 17.9184, "lon": -76.8200, "longueur": 200, "largeur": 100,
  "cap": 75, "sommet": 0.3, "caye": true }
```

| champ | rôle |
|---|---|
| `lat` · `lon` | son centre, en coordonnées réelles |
| `longueur` · `largeur` · `cap` | son plateau, en mètres du monde réduit, le grand axe au cap boussole ; le bord ondule de lui-même (un récif n'est pas une ellipse) |
| `sommet` | la profondeur de sa crête sous la surface (0,5) ; jamais moins de 15 cm |
| `tombant` | la largeur de la pente qui le rejoint au fond d'alentour, en mètres (40) |
| `caye` | une caye de sable émerge en son milieu |

Le récif RELÈVE le fond (`World.HeightAt`, `core/NavalSim.Core/Reefs.cs`) : la coque s'y échoue,
le pilote de rade le contourne, l'eau y vire au turquoise. Le fond le peint de corail
(`seabed.gdshaderinc`, la même forme que le noyau) et la carte marine le marque d'un pointillé et
de croix, son nom à la loupe. Seize récifs au plus sont peints par région.

**Le fond dur tue.** Sur le sable et la vase, une coque ne s'ouvre qu'à plus de 2,2 m/s
(quatre nœuds), d'un petit trou que le charpentier bouche. Sur le CORAIL d'un récif et sur un
ÉCUEIL (`"ecueil"` d'un semis), elle s'ouvre dès 0,8 m/s (un nœud et demi), d'un trou qui grandit
avec la vitesse et qu'on ne bouche pas, et elle RACLE : une couture de plus toutes les secondes
et demie tant qu'elle avance dessus. Clouée sur du dur et à moitié pleine pendant vingt secondes,
elle est perdue — une coque crevée sur un récif ne coule pas, elle s'y assied, et la houle
l'achève. Banc : `-- ecueil [nœuds]` (un sloop sur Lime Cay, une roche, une plage) ;
`-- sonde lat lon [demi-côté] [pas]` dessine le fond en caractères.
Essai : `--fond x,z,hauteur` pose l'œil au-dessus d'un point.

## Le centre-ville d'un port (Godot)

Un port peut avoir des rues : des pâtés d'un même modèle alignés en rangées dans le sens de la longueur de la
terre, des rues pavées entre. Dans l'entrée du port → `centre` :

```json
"centre": { "glb": "world/models/cottage-ville.glb", "bloc": [31, 12.7], "longueur": 320, "rue": 8, "cour": 4,
            "ruelle": 2.5, "traverse": 3, "paires": 2, "altitude": 2 }
```

| champ | rôle |
|---|---|
| `bloc` | un pâté en mètres : longueur sur la rue, profondeur — le .glb y est ramené sans être déformé |
| `longueur` | jusqu'où les rangées s'étendent le long de l'axe (m) |
| `paires` | combien de paires de rangées DOS À DOS ; deux paires font trois rues en long |
| `rue` · `cour` · `ruelle` | la rue entre deux paires, la cour entre deux rangées dos à dos, le passage entre deux pâtés (m) |
| `traverse` | une rue de traverse tous les tant de pâtés |
| `altitude` | on ne bâtit qu'au-dessus : l'herbe, pas la grève (m) |
| `facade` | de combien tourner le modèle (degrés) si sa façade ne regarde pas la rue |

L'axe n'est pas écrit : c'est la direction où la terre autour du port s'étale le plus. Un pâté qui ne tient pas
tout entier sur l'herbe, sur le plat, loin du ponton, n'est pas bâti. La terre se pave à une rue de chaque pâté
(`StreetGrid`), et le semis des maisons ordinaires laisse le centre libre. Banc :
`dotnet run --project core/NavalSim.Lab -c Release -- centre port-royal` (la carte des rues).

## Les retouches : le mode création (Godot)

**²** (la touche sous Échap) bascule le mode création : une caméra libre, et tout ce que le monde pose de
lui-même se prend à la souris — maisons et église des villes, pâtés des centres, modèles posés, rochers et
arbres des semis, figurants de la plage. On le déplace (glisser), le tourne (molette, ⇧ par 45°), le grandit (PgUp PgDn), le lève
(↑ ↓), le retire (Suppr) ou le rend à l'automatique (⌫) ; Ctrl+Z annule, Ctrl+S enregistre, ² ressort et
enregistre. **Ctrl+C** copie l'objet pris, **Ctrl+V** le colle sous la souris (même modèle, même taille, même
cap) ; **Tab** ouvre la palette — un objet de chaque sorte que le monde contient, les modèles bruts de
`world/models` et `props`, et **les navires** (une entrée par fiche de `ships/` qui a un modèle) — dont le
choix se colle de même. Un navire se pose sur l'eau, au mouillage : un décor de rade, comme les navires des
ports, qui suit la houle et gîte sur sa pente, sans solveur ni barre ; il se déplace et se tourne, sans
échelle ni hauteur — c'est la mer qui le porte.

Rien n'est écrit dans la fiche : les retouches vont dans `world/retouches/<région>.json` et s'appliquent
par-dessus le placement automatique, au moment où chaque objet se pose.

```json
{ "region": "caraibes", "retouches": [
    { "id": "maison:Port-Royal:37", "x": -347.21, "z": -92.09, "cap": 39.6, "echelle": 1.2, "dy": 0.3 },
    { "id": "semis:Rochers des plages de Port-Royal:0", "retire": true },
    { "id": "ajout:1", "de": "pate:port-royal:13", "x": -326.21, "z": -86.09, "cap": 9.6 },
    { "id": "ajout:2", "glb": "props/coffre_2k.glb", "x": -374.21, "z": -84.09, "cap": 0.0 },
    { "id": "ajout:3", "fiche": "frigate17e", "x": -343.10, "z": 242.40, "cap": 34.4 }
] }
```

| champ | rôle |
|---|---|
| `id` | l'objet : `maison:<ville>:<rang>`, `pate:<port>:<rang>`, `modele:<rang>:<nom>`, `semis:<nom du semis>:<rang>` |
| `x` · `z` | sa place, en mètres vrais du monde |
| `cap` | sa rotation autour de la verticale, en degrés |
| `echelle` · `dy` | sa taille (1) et ce qu'on l'a levé au-dessus du sol (m) |
| `retire` | il n'est plus là |
| `de` | un AJOUT, copie de cet objet : son modèle, sa taille, sa couleur suivent la source |
| `fiche` | un AJOUT, navire au mouillage de cette fiche de `ships/` (sans `.json`) : décor qui suit la houle, visible à moins de `mouillage.portee` de l'œil |
| `glb` | un AJOUT, modèle brut : recentré, posé sur son point le plus bas, à la taille que donne `palette.json` (sinon son unité est le mètre, sauf au-delà de 40 unités : ramené à 4 m) |

**La palette d'un dossier** — `world/models/palette.json`, `props/palette.json` : le nom qu'on lit dans la
palette et la TAILLE d'un modèle brut en mètres (sa plus grande dimension). L'unité d'un fichier ne se devine
pas : un fort de cent unités et un tonneau de quatre-vingt-dix-huit se ressemblent. Un modèle absent paraît
quand même, à l'échelle de son fichier. Changer une taille ici change la taille de base des ajouts déjà posés :
leur `echelle` est relative à elle.

```json
{ "modeles": { "fort_001.glb": { "nom": "Fort", "taille": 100 } } }
```

**Les fumées de cheminée** sont des objets comme les autres (`fumee:<maison>:<n>`) : une maison sur trois et
deux par pâté en ont, au faîte. Tant qu'on ne la déplace pas, une fumée SUIT sa maison — place, cap, échelle
— et s'éteint si la maison est retirée ; une copie de maison emporte les siennes ; déplacée à la main, elle
devient indépendante. Son échelle est sa FORCE (PgUp, PgDn). Une boule bleue les montre en mode création.
`settings.json` → `chimneys.enabled`, et `chimneys.density` : la part des cheminées automatiques qui fument (0,35 ;
tirée sur leur nom, la même d'une partie à l'autre). Celles qu'on a posées ou retouchées fument toujours ; les
muettes restent visibles en mode création, pour qu'on puisse les prendre.

**Le nom est un rang dans un tirage** : changer la graine, le nombre de maisons, un semis ou le relief d'un port
renumérote ses objets, et une retouche s'appliquerait à un autre. Les retouches sont faites pour un monde dont
les règles sont arrêtées. Ce qu'on n'a pas touché suit toujours les règles.

## Pontons, navires au mouillage et départ écrits dans la fiche (Godot)

```json
"pontons": [ { "nom": "Ponton du chenal", "x": -367.8, "z": 406.4, "cap": 284, "longueur": 32, "largeur": 14 } ],
"mouilles": [ { "fiche": "frigate.json", "x": -330.5, "z": 432.6, "cap": 13 } ],
… dans l'entrée du port : "depart": { "x": -829.1, "z": 507.8, "cap": 102 }
```

En mètres vrais du monde ; les caps sont à la BOUSSOLE (0 nord, 90 est). Un ponton est donné par son MILIEU et
le cap de la racine vers le musoir ; il est bâti de la même charpente que le quai d'un port (une rangée de
jambes tous les cinq mètres en travers, chacune coupée au fond) et la coque le heurte : le solveur le voit
comme des segments de la largeur d'un quai, côte à côte. Un navire de `mouilles` est dessiné comme ceux des
rades et suit la houle. `depart` : le navire du joueur commence là, l'ancre au fond, au lieu du ponton du port.
Pontons et navires sont des objets du mode création (`ponton:N`, `mouille:N`) : on les déplace, les tourne,
les retire ; PgUp PgDn allongent un ponton. Le banc calcule des places depuis des marques :
`dotnet run --project core/NavalSim.Lab -c Release -- pontons p:x,z sloop.json:x,z …` — la rive la plus proche par
le relief, le ponton perpendiculaire jusqu'à quatre mètres d'eau, le navire écarté jusqu'à ce que tout son
plan d'eau porte.

**Le ponton en .glb** — `world/models/ponton-petit.glb` (7 m, le quai d'un port) et `ponton-grand.glb` (14 m) :
non pas un ponton, mais ses PIÈCES, retrouvées par le NOM du nœud — `travee` (quatre mètres de tablier le long
de +x, de x = 0 à 4, centrée en travers, l'origine au niveau de la mer), `pieu` (une jambe d'UN mètre, de y = 0
à 1, étirée du fond jusque sous les longerons à chaque nœud) et `bitte`. Le jeu les assemble à la longueur et
à la largeur de chaque ponton ; les matières sont libres. Écrits par `node tools/jetty-glb.js` (ne le relancez
pas sur des fichiers retouchés : il les écrase). Absent : le ponton est dessiné par le code.

**Un tronçon d'une pièce** (`props/ponton.glb`, `props/ponton_large.glb`, prioritaires sur les précédents) : sans
pièces nommées, tout le modèle est la travée. Le jeu lit sur lui son sens (sa plus grande longueur en plan va
vers le large), son TABLIER (la surface tournée vers le haut la plus étendue, posée à hauteur de bordage) et
son échelle — en travers la largeur du ponton, en long et en hauteur celle où le garde-corps fait 1,10 m. Ses
PIEDS sont allongés par le code : tout ce qui est sous le tablier, passé la charpente, est étiré jusqu'à neuf
mètres sous l'eau (le fond les cache là où il est plus haut).

**L'abri du rivage** — dans l'entrée d'un port : `"abri": { "rayon": 1100 }`. Autour du port, sur ce rayon, la
houle est retenue par la FORME de la côte, comme derrière un môle : pour chaque point d'eau, la part des
directions qui atteignent le large (800 m sans terre) ; une rive droite reste battue, le fond d'un bassin ne
garde que 12 % de la houle. Calculé au chargement (une grille de 8 m), lu par la mer, l'écume et la coque.
Banc : `dotnet run --project core/NavalSim.Lab -c Release -- abri` (la carte).

## Le sol peint (Godot)

Le relief se teinte de lui-même par l'altitude, aux sommets de son maillage (cinq mètres de pas). Autour d'un
port, on peut PEINDRE par-dessus, au demi-mètre : de l'herbe, des pavés, du sable. Dans la fiche :

```json
"peinture": [ { "port": "port-royal", "image": "world/peinture/port-royal.png", "cote": 1024, "pas": 0.5 } ]
```

Un carré de `cote` mètres centré sur le port, un pixel par `pas`. Deux images PNG RGBA : la TERRE (le fichier
nommé) r = herbe, g = pavés, b = sable ; les FONDS (le même nom suffixé `-fonds`) r = sable blanc, g = vase,
b = herbier. a = combien chacune recouvre la teinte du relief (0 : on n'y a pas touché) ; peindre l'une efface
l'autre au même endroit. Elles s'écrivent au pinceau du mode création (², puis P) : 1 herbe, 2 pavés, 3 sable,
4 la gomme qui rend le relief, 5 6 7 sable blanc, vase, herbier — le pinceau des fonds vise à travers l'eau ; molette pour le rayon, ⇧ molette pour la force ; Ctrl+Z annule le dernier coup ; Ctrl+S (ou ² en
sortant) enregistre. Les matières sont dessinées par le shader (`ground_paint.gdshaderinc`) dans les teintes
de la terre (`LandNode`) : touffes d'herbe, pavés jointoyés, grain de sable, qui s'effacent vers leur couleur
moyenne au loin. Une seule peinture active par région pour l'instant.

## Les autres régions, et les traversées (Godot)

Chaque fichier `world/*.json` est une **région** : sa carte, son relief, ses
ports, à la même échelle (×0,4). Aujourd'hui : `caraibes` (la Jamaïque) et
`tortue` (la Tortue et la côte nord de Saint-Domingue, du Môle au Cap-Français).

La mer entre deux régions n'est pas dessinée : elle est **comptée**. Au large
(à 3 km de jeu de toute côte), la carte (I) propose « Faire route… » : pour
chaque autre région, les milles jusqu'à son atterrage le plus proche, la route,
et la durée que donne le vent du moment — au près on louvoie (2 nœuds sur la
route), grand largue on file (5,5). Le calendrier avance d'autant ; la coque, la
bourse, la cale, la poudre et le vent passent, pas les avaries ni l'ancre.

Les **atterrages** sont déclarés dans la fiche :

```json
"approaches": [
  { "name": "l'entrée ouest du canal de la Tortue", "lat": 19.99, "lon": -73.30, "heading": 90 }
]
```

| champ | rôle |
|---|---|
| `name` | dit à l'arrivée (« vous voici à… ») |
| `lat`, `lon` | le point d'arrivée : de l'eau libre, à plus de 3 km de jeu des côtes |
| `heading` | le cap à l'arrivée, degrés vrais (vers la terre) |

**Ajouter une région** : copier `world/tortue.json`, changer le cadre
(`relief` : `west`, `east`, `south`, `north` ; `height` ≈ 740 × l'écart de
latitude / 0,75 pour garder 45 m par pixel), les ports et les atterrages, puis

```bash
node tools/region-heightmap.js world/ma-region.json      # le relief, depuis les côtes réelles
dotnet run --project core/NavalSim.Lab -- ports world/ma-region.json
dotnet run --project core/NavalSim.Lab -- traversees      # atterrages sur le terrain, durées d'une région à l'autre
```

Les côtes de `natural-earth-caraibes.json` couvrent de −80,3° à −66,7° de
longitude et de 9° à 21,2° de latitude ; les chaînes de montagnes sont dans
`world/sources/reliefs.json`. Pour démarrer dans une région :
`-- --region tortue` ; pour essayer une traversée : `-- --traversee tortue`.

## Déboguer

```js
Naval.app.world.heightAt(x, z)        // hauteur en un point (mètres du monde)
Naval.Geo.fix(x, z)                    // → { lat, lon }
Naval.Geo.toXZ(lat, lon)               // → { x, z }
Naval.app.world.isles                  // les ports, avec leur ponton calculé
```

## Un îlot au large

```bash
node tools/islet.js ilot-cocotiers 17.84 -76.90 --sonde   # dire ce qu'il y a là
node tools/islet.js ilot-cocotiers 17.84 -76.90           # le poser
```

Il peint un **patch** (1024 px pour 1400 m) et ajoute son entrée à `patches` —
sans toucher à la grande image, donc une révision de la carte ne l'effacera pas.
Il **refuse** un point où le fond est à moins de dix-huit mètres : un îlot naît au
large, il ne se colle pas à une côte.

Le profil est ce qui fait tout le jeu : sommet à +5,5 m, plage jusqu'à 45 m,
**platier à −1 m** jusqu'à 175 m, puis un tombant à −22 m au pied (255 m). Ce
platier est l'interdit, et ses chiffres sont ceux du jeu et non des fiches — une
fiche donne la quille, l'eau donne l'enfoncement. Tirants RELEVÉS à flot :
chaloupe 0,70 m (elle passe, cinquante centimètres sous la quille), vedette 1,17
(trois centimètres : elle touche à la première houle), chaland 1,75, cotre 1,95,
goélette 2,53, Roter Löwe 3,04 — tous échoués. On mouille dehors et l'on finit à
l'aviron. Les trois chiffres à retoucher sont en tête de l'outil.

Les cocotiers viennent de `node tools/palm-glb.js` (`props/cocotier.glb`) et
sont posés par `assets`, comme tout modèle du monde.

**Pour en modeler un dans Blender**, trois choses et rien d'autre :

- **l'origine au PIED du tronc**, à la hauteur du sol : le jeu pose l'objet sur
  le relief par son origine, et une origine au milieu du stipe l'enterrerait à
  mi-hauteur ;
- **le haut vers +Z dans Blender** (l'export glTF en fait +Y), **unités : le
  mètre, à l'échelle du jeu** — celui du code fait 10,6 m de haut ;
- **les couleurs dans les sommets**, ou une texture INCLUSE dans le `.glb` : la
  page ne peut charger aucun fichier à côté.

L'orientation autour de la verticale n'a pas à être choisie dans le modèle :
chaque entrée `assets` porte son `yaw` et son `scale`.

Pour le regarder sans ouvrir Blender : `node tools/glb-look.js props/cocotier.glb`.

## Les horaires d'une rade (Godot)

`world/horaires/<région>.json` (`core/NavalSim.Core/Timetable.cs`) : des navires partent
d'un port et y arrivent À HEURE FIXE, l'heure du ciel. Ce sont des navires du jeu,
menés par un pilote de rade (`HarbourRoute.cs`, `HarbourPilot.cs`,
`godot/scripts/ShipDemo.Traffic.cs`) : la route contourne la terre sur une grille de
trente mètres, avec l'eau qu'il faut sous la quille et une distance à la côte ; le
pilote la suit, tire des bords courts, vire vent devant (aurique) et sonde devant
l'étrave au près. Combien à la fois, les candidats par défaut, jusqu'à quel temps :
`settings.json` → `trafic`. Banc : `-- rade [gain] [force] [vent]`.

```json
{
  "port-royal": {
    "postes": [
      { "n": 1, "ponton": 3, "nom": "Ponton du chenal, au nord" }
    ],
    "departs": [
      { "heure": "06:00", "poste": 1, "vers": "passage-fort", "fiches": ["sloop", "schooner"] },
      { "heure": "10:00", "poste": 1, "vers": "large", "puis": "Carthagène",
        "lat": 10.4236, "lon": -75.5253, "fiches": ["barge"] }
    ],
    "arrivees": [
      { "heure": "11:00", "de": "large", "poste": 1, "fiches": ["schooner"] }
    ]
  }
}
```

Par port (sa clé dans `ports`) :

- **`postes`** — les places où l'on part et où l'on arrive, numérotées par `n`.
  `ponton` : le numéro du ponton dans la liste `pontons` de la fiche (le mode
  création l'affiche au-dessus de chacun, « ponton 3 »). La place est à `ecart`
  mètres (15) au-delà de son musoir, là où l'éditeur a laissé le ponton ; s'il n'y a
  pas l'eau que la quille demande, elle recule le long du ponton jusqu'à l'eau qui la
  porte (le journal le dit). `x`, `z` (mètres vrais) posent un poste sans ponton.
- **`departs`** — `heure` (« 06:30 », ou 6.5), `poste`, `vers` (une clé de port, ou
  `large` : la sortie de la rade, trouvée seule), `fiches` (les candidats ; on en
  tire un), `avance` (les heures qu'il passe à quai avant, amarré, voiles ferlées :
  1). `puis` avec `lat`/`lon` : passé le large, il continue de faire route vers ce
  lieu réel et s'en va quand il a passé l'horizon (2,5 km de l'œil).
- **`arrivees`** — `heure` (celle où il se met en route de l'autre bout), `de` (une
  clé de port, ou `large`), `poste`, `fiches` ; `origine` : d'où il vient au-delà du
  large, pour le journal. Rendu, il ferle, court sur son erre et s'amarre où elle
  tombe ; il s'en va après deux heures d'escale, quand on ne le regarde plus.

Un poste sans `poste` écrit : devant le ponton du port. Un mouvement ne paraît que là
où l'œil n'est pas (loin, ou dans son dos) ; sinon il attend, et il est manqué une
heure après. Par gros temps (`forceMax`), ni départ ni arrivée. À l'ouverture, ce qui
est parti depuis peu est déjà en chemin, à la distance qu'il a pu faire.

**Le ciel tourne vite** (deux heures par minute réelle) : une traversée de la rade
dure plusieurs heures du ciel, et les horaires se lisent comme une ligne régulière —
« chaque jour à six heures, un sloop part pour Passage Fort ».

**Ajouter un ponton** : à la FIN de la liste `pontons` de la fiche, puis le placer en
mode création. Insérer au milieu renumérote ceux d'après, et les postes comme les
retouches (`ponton:N`) viseraient alors le voisin.

Essais : `--horaire depart:3` (ou `arrivee:1`) rend ce mouvement échu dès
l'ouverture ; `--horaire-vue 2` pose l'œil sur le poste 2 ; `--rade-vue 1` suit le
premier navire en route ; `--heure 9.5` règle le ciel.
