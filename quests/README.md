# Quêtes

Chaque fichier `quests/*.json` est une quête. Il suffit de le déposer dans ce
dossier : le serveur de dev le liste tout seul, et `node build.js` l'embarque
dans la page publiée (il écrit aussi `quests/index.json` pour un hébergeur
statique). Le joueur la choisit dans le menu **Mode libre / Quête · …** du
panneau des instruments. Le monde reste ouvert : la quête ne fait que tendre
un fil d'un lieu à l'autre.

Pendant une quête :
- l'objectif s'écrit en doré sous la date et le temps, avec la distance et le
  cap à suivre (« Accoster au Carénage — 6,4 M au 312° ») ;
- son lieu est cerclé de doré sur la carte (touche `O` pour la grande carte) ;
- quand une étape est remplie, son `message` s'affiche au milieu de l'écran
  (un clic le ferme), puis la consigne de l'étape suivante ;
- la progression est gardée dans le navigateur, et reprend au rechargement.

## Format

```json
{
  "id": "la-lettre-du-gouverneur",
  "title": "La lettre du gouverneur",
  "summary": "Une ligne de présentation.",
  "intro": "Affiché au lancement de la quête (sinon : summary).",
  "steps": [
    {
      "title": "Accoster au Carénage",
      "brief": "Consigne affichée quand l'étape commence (facultatif).",
      "at": { "island": "carenage", "port": true },
      "goal": "dock",
      "radius": 450,
      "message": "Affiché quand l'étape est remplie.\nUn saut de ligne avec \\n."
    }
  ],
  "outro": "Affiché à la fin de la quête (facultatif)."
}
```

| champ d'étape | rôle |
|---|---|
| `title` | le nom de l'étape : ligne d'objectif, titre du message, nom sur la carte |
| `at` | le lieu (voir plus bas) — **obligatoire** |
| `goal` | ce qu'il faut y faire : `reach` (par défaut), `leave`, `stop` ou `dock` |
| `radius` | rayon du lieu en mètres (par défaut 300 ; 1852 pour `leave`, 250 pour `stop`, 450 pour `dock`) |
| `brief` | consigne affichée au début de l'étape |
| `message` | texte affiché quand l'étape est remplie |
| `maxSpeed` | pour `stop` : vitesse maximale en nœuds (1 par défaut) |
| `hold` | pour `stop` : secondes à tenir (8 par défaut) |

### Objectifs

- **`reach`** : entrer dans le cercle.
- **`leave`** : sortir du cercle — s'éloigner d'un port ou d'un lieu, dans
  n'importe quelle direction (« Quitter la rade »). La ligne d'objectif dit
  où l'on en est : « 0,4 M sur 1,0 M ».
- **`stop`** : rester dans le cercle sous `maxSpeed` nœuds pendant `hold`
  secondes (en panne, voiles ferlées ou au mouillage). La ligne d'objectif
  décompte les secondes.
- **`dock`** : être amarré ou mouillé dans le cercle (touche `M` au ponton).

### Lieux (`at`)

Mieux vaut situer un lieu **par rapport à une île** : l'échelle de la carte
(`Naval.MAP_SCALE`) déplace les îles, et un lieu écrit en mètres resterait en
pleine mer.

| forme | sens |
|---|---|
| `{ "island": "carenage", "port": true }` | la tête du ponton du port de l'île |
| `{ "island": "tortue", "bearing": 270, "miles": 2 }` | à 2 milles **au large de la côte**, dans le relèvement 270° vu du centre de l'île (0 nord, 90 est). `distance` en mètres au lieu de `miles` |
| `{ "island": "tortue" }` | sur la côte au nord de l'île (relèvement 0, distance 0) |
| `{ "lat": 13.52, "lon": -60.95 }` | latitude et longitude, comme la carte les affiche |
| `{ "x": -4000, "z": 9000 }` | mètres du monde (comme le Cimetière des Galions dans `settings.json`) |

Îles : `port-royal`, `carenage`, `saint-pierre`, `tortue`.

## Déboguer

Dans la console du navigateur :

```js
Naval.app.quests.start('la-lettre-du-gouverneur')   // lancer
Naval.app.allerQuete()                              // se téléporter au lieu de l'étape
Naval.app.quests.step                               // l'étape en cours (0 = la première)
Naval.app.quests.stop()                             // revenir au mode libre
```

Une quête mal formée (sans `id`, sans étapes, étape sans `at`, objectif
inconnu) est ignorée, avec la raison dans la console.
