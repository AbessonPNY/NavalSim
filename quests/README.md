# Quêtes

Chaque fichier `quests/*.json` est une quête. Il suffit de le déposer dans ce
dossier : le serveur de dev le liste tout seul, et `node build.js` l'embarque
dans la page publiée (il écrit aussi `quests/index.json` pour un hébergeur
statique). Le joueur la choisit dans le menu **Mode libre / Quête · …** du
panneau des instruments. Le monde reste ouvert : la quête ne fait que tendre
un fil d'un lieu à l'autre.

Pendant une quête :
- l'objectif s'écrit en doré sous la date et le temps, avec la distance et le
  cap à suivre (« Accoster à Petit-Goâve — 22,7 M au 073° ») ;
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
      "title": "Accoster à Petit-Goâve",
      "brief": "Consigne affichée quand l'étape commence (facultatif).",
      "at": { "port": "petit-goave" },
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

Un lieu se donne par rapport à un **port** de la région (`world/caraibes.json`)
ou par ses **vraies latitude et longitude** : la carte réduite en fait ses
propres mètres.

| forme | sens |
|---|---|
| `{ "port": "petit-goave" }` | la tête du ponton de ce port |
| `{ "port": "port-royal", "bearing": 180, "miles": 2 }` | à 2 milles de la carte du port, dans le relèvement 180° (0 nord, 90 est). `distance` en mètres au lieu de `miles` |
| `{ "lat": 19.85, "lon": -73.62 }` | latitude et longitude réelles, comme la carte les affiche |
| `{ "x": -4000, "z": 9000 }` | mètres du monde (0 = Port-Royal) |

`"island"` est encore compris à la place de `"port"` (ancien nom).

Ports de la Jamaïque, dans le sens des aiguilles depuis Port-Royal :
`port-royal`, `passage-fort`, `old-harbour`, `withywood`, `black-river`,
`savanna-la-mar`, `negril`, `lucea`, `montego-bay`, `dry-harbour`,
`port-maria`, `port-antonio`, `port-morant`, `yallahs` — les rades que porte
une carte anglaise de la fin du XVIIe siècle. Le reste de la mer :
`santiago`, `tortue`, `petit-goave`, `carthagene`, `santa-marta`,
`portobelo`, `curacao`.

L'île entière tient dans neuf milles : de Port-Royal, Passage Fort est à
0,3 M, Yallahs à 1,6 M, Port Morant à 2,9 M, Negril — la pointe de l'ouest —
à 9,0 M.

## Dans Godot

Les mêmes fichiers, lus dans le même dossier : une quête écrite pour la page
vaut pour le portage, et il n'y a rien à réimporter quand on en ajoute une.
Les règles sont dans `core/NavalSim.Core/Quest.cs`, vérifiées contre
`js/quests.js` par le banc de parité (`node tools/parity-quests.js` puis
`dotnet run --project core/NavalSim.Parity`).

- le scénario se choisit dans **Échap → Quête**, ou au lancement :
  `-- --quete le-tour-de-la-jamaique` ;
- `-- --etape 1` saute au lieu de l'étape en cours, comme `allerQuete()` ;
- la progression est gardée en clair dans `user://quetes.json`, à côté du
  carnet de la carte ;
- l'objectif s'écrit en doré sous les instruments, et son lieu est cerclé de
  doré sur la carte du capitaine (touche `I`).

```bash
dotnet run --project core/NavalSim.Lab -- quete          # où tombe chaque étape, et ce qu'il y a d'eau dessous
dotnet run --project core/NavalSim.Lab -- ports          # les ports d'une fiche de région
```

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
