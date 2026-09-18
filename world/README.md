# Le monde : la mer des Caraïbes

Le monde est la vraie géographie autour de la Jamaïque, **formes réelles,
distances et tailles ÷ 10**, **hauteurs × 0,25** (à pleine échelle, une
traversée vers Carthagène dure quatre jours ; au dixième, une soirée). La
carte marine affiche les vraies latitudes et longitudes.

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

- Un pixel vaut **45 m de jeu** (450 m réels). Une langue de terre plus fine
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

## Déboguer

```js
Naval.app.world.heightAt(x, z)        // hauteur en un point (mètres du monde)
Naval.Geo.fix(x, z)                    // → { lat, lon }
Naval.Geo.toXZ(lat, lon)               // → { x, z }
Naval.app.world.isles                  // les ports, avec leur ponton calculé
```
