# Fiches navires

Un fichier JSON par bâtiment. **Le dossier fait foi** : déposez le fichier ici,
rechargez la page, il est dans le sélecteur. Rien à déclarer ailleurs.

## Ajouter un navire depuis un .glb

```bash
node tools/add-ship.js ships/models/mon-bateau.glb --nom "La Sirène"
```

L'outil lit la boîte englobante réelle du maillage, en déduit longueur, largeur
et tirant, **mesure le volume d'enveloppe avec les mêmes formules que le
solveur**, et fixe un tonnage qui la pose à ~36 % d'immersion. Il écrit
`ships/<id>.json`, qu'il ne reste qu'à affiner.

Options : `--tonnes N` pour imposer le déplacement, `--id slug`, `--force` pour
écraser une fiche existante.

Une fiche illisible est ignorée avec un avertissement en console — elle
n'empêche jamais les autres navires de se charger.

## Principe

Le JSON énonce ce qu'énoncerait un architecte naval : dimensions principales,
coefficients de formes, un **déplacement en tonnes**, un plan de voilure. Tout ce
dont le solveur a besoin en est *dérivé*. Un navire plus grand est donc plus grand
en tout — résistance, giration, inertie — sans un seul nombre magique par bateau.

Les coefficients hydro sont **par unité de surface**, jamais des forces absolues :

| champ | unité | s'applique à |
|---|---|---|
| `hydro.resistance` | N par (m/s)² par m² | maître-couple (`beam × keelDepth`) |
| `hydro.lateralGrip` | N par (m/s) par m² | plan de dérive (`length × keelDepth`) |
| `rudder.power` | idem | plan de dérive |

C'est ce qui permet aux mêmes valeurs de servir une goélette de 24 m et une
frégate de 60 m.

## Machine

`engine.topSpeed` (m/s) dimensionne la poussée : elle est celle qui équilibre
exactement la résistance à cette vitesse, donc une coque plus traînante reçoit
d'office une machine plus forte.

`engine.sternPower` est la fraction de cette poussée disponible **en marche
arrière**, en négatif. Aucun navire ne recule aussi fort qu'il n'avance — une
hélice inversée travaille contre son propre sillage — et la valeur borne
directement la course du chadburn vers l'arrière : à `-0.5`, le télégraphe ne
descend pas sous −50 %. Compter −0,5 pour un grand bâtiment, −0,7 pour un
chaland qui manœuvre au port.

## Avirons et chaloupe

Une embarcation menée à l'aviron déclare un bloc `oars` :

```json
"oars": { "pairs": 2, "period": 2.2, "length": 3.4 }
```

`pairs` est le nombre de paires d'avirons dessinées, `period` la durée d'un coup
de nage en secondes, `length` la longueur d'un aviron. La **poussée reste celle de
`engine.topSpeed`**, dépensée par à-coups : la pale est dans l'eau pendant 45 % du
coup, et scier rend `sternPower` de la poussée. Elle s'applique **aux pales**,
à 60 % d'un aviron au-delà du plat-bord, ce qui fait tourner l'embarcation quand
un seul bord nage. Pas de gouvernail : `rudder.power` à 0.

Un navire qui porte une chaloupe la **nomme** par l'`id` de sa fiche :
`"boat": "chaloupe"`. Touche `N` pour l'affaler ou la hisser. Voir
`chaloupe.json` : 7 m, 3 t, `hydro.resistance` à 200 et non 61 — une petite coque
traîne plus par unité de section, et à 61 la poussée qui en découle ne faisait
que 194 N, quand quatre nageurs en tirent plutôt 600.

## Lanternes

Une fiche déclare les feux qu'elle porte et où ils pendent :

```json
"lanterns": [
  { "x": 0, "zFrac": -0.41, "size": 1.2 },
  { "xFrac":  0.30, "zFrac": -0.40 },
  { "xFrac": -0.30, "zFrac": -0.40 }
]
```

| champ | ce qu'il dit |
|---|---|
| `x` · `z` | la place en **mètres** dans le repère du navire (+z l'étrave, +x bâbord) |
| `xFrac` · `zFrac` | la même place en **fractions** du bau et de la longueur — suit la taille du navire |
| `y` | la hauteur en mètres ; **absent**, le feu se pose sur le pont à cette station, lu sur le modèle |
| `above` | de combien le relever au-dessus de ce pont |
| `size` | 1 par défaut, la taille de la lueur |
| `color` | `"0xffcf7a"` par défaut, la couleur de la flamme |

Pas de champ `lanterns` : un seul feu au couronnement, placé automatiquement,
comme avant. Une **liste vide** (`"lanterns": []`) : aucun feu.

## Fenêtres allumées la nuit

glTF n'a pas de « texture de nuit », et il n'en a pas besoin : il a une **carte
émissive**. Dans Blender, branchez une image où seules les fenêtres sont claires
sur l'entrée **Émission** du matériau ; l'exportateur la porte en
`emissiveTexture` dans le `.glb`. Le jeu la trouve seul et ne règle qu'une
chose, l'intensité : **zéro le jour**, pleine la nuit, sur la même bascule que
les feux et le ciel. Rien à déclarer dans la fiche.

Sans image, il reste le repli par **nom de matière** : une matière nommée
`fenetre`, `window`, `vitre`, `glass`, `hublot`, `lamp`, `lanterne`… reçoit une
émissive chaude la nuit. C'est ce qui allume la Roter Löwe, dont le modèle porte
une matière `glass`.

`model.nightGlow` règle la force (1 par défaut, 0 pour ne rien allumer).

Trois réglages de jeu, dans `settings.json` :

```json
"night": { "glow": 2.6, "lightAt": 0.35, "snuffAt": 0.25 }
```

`glow` est la force de l'émissive (que `model.nightGlow` d'une fiche multiplie
encore), `lightAt` et `snuffAt` les deux seuils d'allumage et d'extinction, sur
une échelle où 0 est le coucher du soleil et 1 dix degrés plus bas. Les fenêtres
s'allument **d'un coup** et sont soufflées de même : deux seuils plutôt qu'un
pour qu'un soleil qui hésite à la limite ne fasse pas battre le bord.

**Attention à ce que le gréement pourrait prendre pour une vergue.** Une pièce
longue, mince et posée en travers de l'axe est lue comme un espar : une fenêtre
à plat dans le château arrière l'a été, et partait brasser derrière la poupe.
Les matières de vitrage et de fanal (la liste ci-dessus) sont donc refusées au
gréement, et une fiche peut en écarter d'autres par leur nom de maillage :

```json
"model": { "glb": "ships/models/…", "rigIgnore": ["Plane_5", "vitrail"] }
```

La console nomme ce qui a été écarté à chaque chargement.

## Le tonnage est l'entrée

`displacementTonnes` fixe la masse. La fraction de volume immergé en découle.
Si elle dépasse 1, le navire coule et la console vous le dit explicitement au
chargement — c'est une erreur de fiche, pas de physique.

## Champs de formes

| champ | effet |
|---|---|
| `sectionTuck` | 0 = section en V pincée ; 1 = section carrée. Bas = voilier, haut = navire de charge |
| `sectionPower` | courbure du bouchain |
| `waterlinePower` / `waterlineFull` | finesse des entrées d'eau |
| `transomWidth` | largeur conservée au tableau arrière |
| `dragAft` | quille plus profonde à l'arrière |
| `forefootLift` / `counterLift` | relevé de l'étrave et de la voûte |

## Gréements

`rig.type` vaut `"gaff"` (bôme pivotante), `"square"` (vergues carrées) ou
`"none"`. En carré, chaque mât liste ses vergues en fractions de sa hauteur.

Le solveur ne modélise **qu'une seule aile équivalente** (`sailArea`, `ceHeight`),
pas chaque voile séparément — le gréement est une représentation visuelle.

## Mettre en ligne (OVH, Apache, nginx, GitHub Pages…)

**Lancez `node build.js` avant de téléverser.** Il écrit `ships/index.json`, la
liste des navires que la page demande au démarrage.

Le serveur de développement répond à ce chemin par un listage du dossier en
direct, sans fichier. Un hébergeur statique n'a pas cet endpoint : sans le
fichier, la page retombe sur la courte liste codée dans `js/config.js`, et **tout
navire ajouté depuis n'est jamais demandé** — ses `.glb` semblent alors « ne pas
se charger » alors qu'ils n'ont jamais été réclamés.

Deux façons de déployer :

| | Quoi téléverser | Remarques |
|---|---|---|
| **Fichier unique** *(le plus simple)* | `dist/naval-sim.html` seul | Tout est embarqué, y compris les `.glb` en base64. Aucune configuration serveur, aucun index. |
| **Arborescence** | `naval-sim.html`, `css/`, `js/`, `ships/` (index.json compris) | Permet de modifier une fiche sans rebuild. Exige l'index à jour. |

En cas de doute, vérifiez dans l'onglet Réseau du navigateur que
`ships/index.json` renvoie bien 200 et non 404.

Deux pièges d'hébergement à connaître :

- **La casse compte sous Linux**, pas sous Windows. `Fregate.glb` référencé
  `fregate.glb` fonctionne chez vous et échoue en ligne.
- **FTP en mode ASCII corrompt les `.glb`**, qui sont binaires. Transférez en
  mode binaire.

## Caméras

Le bloc `camera` d'une fiche règle les points de vue propres au navire.

| champ | unité | effet |
|---|---|---|
| `helmHeight` | **mètres au-dessus de la flottaison** | hauteur de l'œil en vue passerelle |
| `helmZFrac` | fraction de la longueur | position du poste de barre ; négatif = vers l'arrière |
| `chaseDist` / `chaseHigh` | mètres | recul et hauteur de la caméra de poursuite |
| `orbitDist` | mètres | distance initiale en vue orbite |

`helmHeight` est une hauteur absolue au-dessus de l'eau, pas un décalage
au-dessus du pont : c'est ainsi qu'on énonce naturellement une hauteur d'œil, et
ça reste juste quel que soit le franc-bord. Comptez le pont, plus la taille d'un
homme : environ 3,5 m sur la goélette, 8,9 m sur la frégate. Montez-la à 12 m et
vous vous retrouvez en tête de mât.

Si le champ est absent, la valeur historique `freeboardMid + 2,1 × (L/24)` est
appliquée — les anciennes fiches continuent donc de fonctionner.

### Vues à bord

`camera.decks` liste les points de vue **depuis le navire lui-même**, autant qu'on
veut. Le bouton caméra (ou `C`) les parcourt dans l'ordre, entre Orbite et Fixe.

```json
"decks": [
  { "name": "Passerelle", "x": 0, "y": 9.43, "zFrac": -0.4, "yaw": 0, "pitch": 0, "fov": 55 },
  { "name": "Chambre du capitaine", "x": 0, "y": 5.0, "zFrac": -0.33,
    "yaw": 180, "pitch": -4, "fov": 72, "near": 0.08 }
]
```

| champ | unité | effet |
|---|---|---|
| `name` | texte | ce qu'affiche le bouton caméra |
| `x` · `z` | mètres, repère du navire | position de l'œil (+z l'étrave, +x bâbord) |
| `xFrac` · `zFrac` | fractions du bau et de la longueur | la même, qui suit la taille du navire |
| `y` | **mètres au-dessus de la flottaison** | hauteur de l'œil ; absente, la règle historique |
| `yaw` | degrés | où l'on regarde : 0 l'étrave, 180 la poupe, 90 bâbord, −90 tribord |
| `pitch` | degrés | positif vers le haut |
| `fov` | degrés | focale verticale (55 par défaut) |
| `near` | mètres | plan de coupe proche (0,7 par défaut) |

Le glisser regarde autour **à partir** de ce regard, la molette zoome, et la
vue gîte avec le pont. **Pour un intérieur, baissez `near`** : à 0,7 m, une
cloison à portée de main est coupée net. Elle est rendue en sortant de la vue.

Deux conseils pour la chambre du capitaine dans Blender : les murs doivent
avoir leurs **faces tournées vers l'intérieur** (ou le matériau en *Backface
Culling* désactivé, que glTF exporte en `doubleSided`), sinon on voit la mer au
travers ; et une vitre qui porte une matière `glass` ou `fenetre` s'allume la
nuit sans rien de plus.

Sans `decks`, la fiche garde l'ancienne passerelle, tirée de `helmHeight` et
`helmZFrac` ci-dessous.

## Modèles .glb

```json
"model": { "glb": "ships/models/barge.glb", "lengthAxis": "z",
           "offset": [0,0,0], "rotationY": 0 }
```

Convention : **étrave vers +z**, origine sur la flottaison, au maître-couple. Le
modèle est mis à l'échelle pour que sa longueur corresponde à `hull.length`
(ou forcez-la avec `"scale"`).

**Le .glb ne porte que l'apparence.** La flottaison vient toujours des cotes du
JSON : on ne peut pas déduire un volume de carène fiable d'un maillage
quelconque sans une voxelisation coûteuse. Si le modèle ne charge pas, la coque
procédurale est conservée et un avertissement est écrit en console.

### Rugosité et relief

Les matériaux du .glb sont gardés tels quels : couleur, **rugosité**
(`metallicRoughnessTexture`, canal vert) et **carte normale** s'affichent sans
rien déclarer — à condition d'être sortis à l'export. Blender ne garde que ce
qui est branché directement sur le Principled BSDF ; un nœud *Bump*, un *Invert*
ou un *ColorRamp* intercalé fait disparaître l'image sans message.

glTF n'a pas de carte de relief en niveaux de gris. Si la même image sert de
rugosité **et** de relief, déclarez-le :

```json
"model": { "glb": "...", "relief": 16 }
```

La carte normale est alors **tirée de la rugosité** au chargement (dérivée de
Sobel, ~60 ms pour 2048 px), pour chaque matériau qui a une rugosité et pas de
carte normale. Le nombre est un gain de pente : 6 ne se voit pas, 16 marque les
préceintes et le fil du bois, au-delà le bordé paraît sculpté. Blanc = en
relief. À ne pas mettre sur une rugosité peinte en aplats, dont chaque bord
deviendrait une arête.

### Pavillon

```json
"appearance": { "ensign": "jolly", "ensignMap": "ships/textures/flags/jolly_roger_1690.jpg" }
```

- `ensign` décide du **camp** : `"jolly"` rend le navire hostile (pirate) ; une
  couleur (`"0xb22222"`) donne un pavillon uni et pacifique.
- `ensignMap` décide de l'**image** qui flotte (png, jpg ou webp). Absente, un
  pavillon `"jolly"` porte la tête de mort dessinée par le code. Les deux sont
  indépendants : on peut changer l'image sans changer de camp.

Le build embarque l'image dans la page publiée, comme les voiles peintes. En
cours de partie, `Naval.app.ship.setEnsignMap(chemin)` (ou celui d'une conserve)
hisse une autre image — dans la page publiée, seulement une image déjà nommée par
une fiche, puisque le build n'embarque que celles-là.

### Marques d'impact

```json
"appearance": {
  "impactMaps": [
    [ "ships/textures/impacts/impact_001_stage_1.png",
      "ships/textures/impacts/impact_001_stage_2.png",
      "ships/textures/impacts/impact_001_stage_3.png" ]
  ]
}
```

Une liste de **variantes**, chacune une liste de **stades** du plus léger au plus
profond. PNG carré (512 × 512), fond **transparent**, fusion normale, griffure
centrée et **horizontale** (le fil du bois), avec une marge vide jusqu'au bord.
Chaque impact tire une variante au hasard, un angle, une taille et un sens ;
rien n'apparaît avant le troisième coup au même endroit, puis stade 1, 2, 3 au
fil des coups. Une variante plus courte que les autres répète son dernier stade.
Sans cette clé, la coque garde des griffures calculées.

`node tools/make-glb.js ships/models/exemple.glb` produit un .glb minimal valide,
utile comme gabarit.

### Publication : le modèle est embarqué

En local, le .glb est chargé par le serveur de développement. Au build, ses
octets sont encodés en base64 et **embarqués dans la page** (`model.glbBase64`),
puis analysés en mémoire par `GLTFLoader.parse()` — aucune requête réseau, donc
aucune politique de sécurité à satisfaire.

C'était nécessaire : une page publiée ne peut pas lire un fichier local, et le
`.glb` n'est pas un type téléversable comme *asset* d'artifact (seuls png, jpg,
svg, mp4, pdf, woff, csv, md, json, txt le sont).

Coût : le base64 gonfle les octets d'environ un tiers, et la page entière est
plafonnée à 16 Mo. Un modèle de quelques Mo passe sans peine ; au-delà,
décimez le maillage ou restez sur la coque procédurale.

Le chargeur `GLTFLoader` lui-même vient de jsdelivr via l'import map. S'il est
bloqué, le repli sur la coque procédurale s'applique — la simulation ne s'arrête
jamais pour un modèle manquant.
