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
| `fenetre` | s'allume la nuit, comme les fanaux (`shaders/town_glass.gdshader`) |

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
