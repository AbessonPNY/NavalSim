# Les modèles propres à Godot

Ce dossier **double l'arborescence du dépôt**. Un fichier qu'on y trouve remplace
celui de la racine — **pour Godot seulement**.

```
godot-models/ships/models/fregate17e.glb     ← Godot lit celui-ci
ships/models/fregate17e.glb                  ← la page lit celui-là
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
{ "ships/models/fregate17e.glb": { "max": { "fabrics": 512 } } }
```

La clé est le chemin habituel du modèle, celui que la fiche donne ; `max` borne
les images dont le nom porte le motif. Sans entrée, un modèle n'est que ramené à
huit bits, ce qui suffit presque toujours.

Pour regarder un modèle sans ouvrir Blender — contenu, mesures, allongement,
silhouette, de quel bout est le nez :

```bash
node tools/glb-look.js godot-models/ships/models/fregate17e.glb
```

## Ce qu'on n'y met pas

Les **fiches** (`ships/*.json`), le **monde**, les **quêtes**, les **réglages** :
ce sont des données partagées, et les tenir en double est exactement ce que ce
projet interdit — une correction qui ne vaudrait que pour un moteur. Le
mécanisme les accepterait (il ne regarde pas l'extension), et c'est commode pour
essayer une valeur sans toucher à ce que la page verra ; mais rien ne doit y
rester.
