# Les modèles propres à Godot

Ce dossier **double l'arborescence du dépôt**. Un fichier qu'on y trouve remplace
celui de la racine — **pour Godot seulement**.

```
godot-models/ships/models/roter_lowe_1597.glb     ← Godot lit celui-ci
ships/models/roter_lowe_1597.glb                  ← la page lit celui-là
```

Rien à déclarer, aucune liste à tenir : c'est la présence du fichier qui décide
(`Assets.Path`, dans `godot/scripts/`). Vider ce dossier rend au jeu son
comportement d'avant, et Godot annonce au démarrage ce qu'il y a trouvé.

## Pourquoi

Les deux versions n'ont pas la même contrainte. La page publiée est **un seul
fichier autonome, plafonné à 16 Mo**, où chaque modèle entre en base64 ; Godot
lit sur le disque et n'a pas de plafond. La frégate exportée en pleine définition
pèse 13,6 Mo à elle seule : splendide dans Godot, et elle interdit la page.

Le dossier est **hors de `godot/`** à dessein : ce qui est dans le projet,
l'éditeur l'importe, et ces modèles-là sont ouverts à l'exécution par
`GltfDocument`. Ils n'ont rien à faire dans la base de ressources.

## La marche à suivre

**On exporte une seule fois**, en pleine définition, ici — puis on en tire la
copie de la page :

```bash
node tools/page-models.js     # écrit les copies allégées à leur place habituelle
node build.js                 # et vérifie que la page tient sous 16 Mo
```

Ce que l'allègement fait, toujours : les textures 16 bits redescendent à 8 (une
carte de normales n'a jamais demandé plus). Ce qu'il fait en plus, sur demande,
c'est borner le côté de certaines images — `allegement.json` :

```json
{ "ships/models/roter_lowe_1597.glb": { "max": { "fabrics": 512 } } }
```

La clé est le chemin habituel du modèle, celui que la fiche donne ; `max` borne
les images dont le nom porte le motif. Sans entrée, un modèle n'est que ramené à
huit bits, ce qui suffit presque toujours.

### Et ce qui n'entre pas du tout : `page: false`

Borner des textures ne sauve pas tout. Deux galions de quatre mégaoctets ne
tiennent pas dans les seize de la page, et ce qui pèse chez eux n'est pas leur
étoffe : c'est leur **géométrie**, que nul `max` ne réduit. Un modèle peut donc
être tenu hors de la page, exprès :

```json
{ "ships/models/hero_ship.glb": { "page": false } }
```

L'outil ne fait alors aucune copie de page, **et efface celle qui traînerait** —
c'est arrivé, un export de Blender lâché à la place de la copie allégée, et la
page est sortie à 21 Mo. La fiche garde son `glb` sans mentir : `build.js` dit en
clair que le modèle est hors page, et `ShipModel` dessine la coque d'après ses
lignes, ce qui est un repli honnête et non une panne. C'est le cas de
**La Boussole**, jumelle de la Roter Löwe : la page porte l'une, Godot les deux.

Le corollaire, qui vaut d'être su : **les deux moteurs peuvent ne pas montrer le
même navire**. C'est le seul endroit du projet où c'est permis, et c'est le
plafond de 16 Mo qui l'impose.

Pour regarder un modèle sans ouvrir Blender — contenu, mesures, allongement,
silhouette, de quel bout est le nez :

```bash
node tools/glb-look.js godot-models/ships/models/roter_lowe_1597.glb
```

## Ce qu'on n'y met pas

Les **fiches** (`ships/*.json`), le **monde**, les **quêtes**, les **réglages** :
ce sont des données partagées, et les tenir en double est exactement ce que ce
projet interdit — une correction qui ne vaudrait que pour un moteur. Le
mécanisme les accepterait (il ne regarde pas l'extension), et c'est commode pour
essayer une valeur sans toucher à ce que la page verra ; mais rien ne doit y
rester.
