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
