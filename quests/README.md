# Quêtes

Chaque fichier `quests/*.json` est une quête. Il suffit de le déposer dans ce
dossier : le serveur de dev le liste tout seul, et `node build.js` l'embarque
dans la page publiée (il écrit aussi `quests/index.json` pour un hébergeur
statique). Le joueur la choisit dans le menu **Mode libre / Quête · …** du
panneau des instruments. Le monde reste ouvert : la quête ne fait que tendre
un fil d'un lieu à l'autre.

Pendant une quête :
- l'objectif s'écrit en doré sous la date et le temps, avec la distance et le
  cap à suivre (« Accoster à Port Morant — 11,6 M au 096° ») ;
- son lieu est cerclé de doré sur la carte (touche `O` pour la grande carte) ;
- quand une étape est remplie, son `message` s'affiche au milieu de l'écran
  (un clic le ferme), puis la consigne de l'étape suivante ;
- la progression est gardée dans le navigateur, et reprend au rechargement.

## Format

```json
{
  "id": "la-lettre-du-gouverneur",
  "region": "caraibes",
  "title": "La lettre du gouverneur",
  "summary": "Une ligne de présentation.",
  "intro": "Affiché au lancement de la quête (sinon : summary).",
  "steps": [
    {
      "title": "Accoster à Port Morant",
      "brief": "Consigne affichée quand l'étape commence (facultatif).",
      "at": { "port": "port-morant" },
      "goal": "dock",
      "radius": 450,
      "message": "Affiché quand l'étape est remplie.\nUn saut de ligne avec \\n."
    }
  ],
  "outro": "Affiché à la fin de la quête (facultatif)."
}
```

`ship` (Godot) : le navire que la quête impose — le nom de sa fiche, sans
`.json` (`"ship": "sloop"`). Un chapitre se court sur le bord qu'il raconte : on
ne porte pas six tonnes de vivres à travers une rade dans un galion de trois
cents tonneaux parce qu'on l'avait sous la main. Le mot n'a d'effet qu'au **début**
de la quête ; reprendre une partie enregistrée garde le bord que la sauvegarde
connaît, sans quoi l'on effacerait un navire que le joueur a gagné. Fiche
introuvable : on garde le navire courant, avec un avertissement.

`rang` et `bourse` (Godot) : le rang que la quête donne en commençant
(`"rang": "Pêcheur"`, affiché devant la bourse) et la bourse de départ en
écus (`"bourse": 2`), à la place des quatre cents écus d'une partie neuve. Comme
`ship`, au **début** de la quête seulement.

`kind` et `chapter` (Godot) : `"kind": "story"` fait de la quête un chapitre de l'**Histoire** (écran de titre → Histoire lance le premier chapitre pas encore fini, dans l'ordre de `chapter`) ; sinon c'est une **mission**, qu'on choisit dans la liste (Missions). Absent : mission.

`region` (Godot) : la carte où la quête se joue — le nom de la fiche de
`world/`, `caraibes` pour la Jamaïque, `tortue` pour la Tortue. Ailleurs, la
quête attend : elle ne vise rien et ne s'accomplit pas, et la ligne d'objectif
dit « dans les eaux de la Jamaïque ». Absent : partout. La page l'ignore.

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
| `cargo` | le fret de l'étape (voir plus bas) |
| `kg` | pour `peche` et `vente` : les kilos à atteindre |
| `rang` | (Godot) le rang que l'étape donne quand elle est remplie (« Patron ») |

### Le fret (`cargo`)

Ce qui fait la différence entre un fil tendu d'un lieu à l'autre et un
**voyage** : sans cargaison, aller quelque part et en revenir ne se distingue
pas d'une promenade.

```json
"cargo": {
  "needs":  { "kind": "vivres", "tonnes": 6 },
  "unload": "vivres",
  "load":   { "kind": "viande", "tonnes": 5 },
  "pay":    720
}
```

| champ | rôle |
|---|---|
| `needs` | ce qu'il faut à bord pour que l'étape compte. C'est une **garde**, pas une consigne : sur le chemin normal c'est l'étape d'avant qui a chargé la cale. Elle ne mord que si le joueur a jeté sa cargaison — auquel cas il n'est pas payé |
| `unload` | ce qu'on débarque ici, tout ce qu'on en porte |
| `load` | ce qu'on embarque ici, et combien de tonnes |
| `pay` | ce qu'on touche, en **pièces d'argent** (60 pour un écu) |

Tout passe par la cale réelle — du poids qui enfonce la coque et déplace son
centre de gravité —, jamais par un compteur à part : un navire qui prend six
tonnes s'assied de six tonnes, et le joueur le lit sur sa ligne d'eau. On
débarque avant d'embarquer, comme à tout quai. Rangé au fond de la cale et au
milieu, là où le comptoir met ses épices ; le joueur peut le rarrimer ensuite,
c'est son affaire.

La nature (`kind`) est un mot libre : `vivres`, `viande`, `epice`, ce qu'on
veut. Seules les épices ont un cours ; le reste se porte sans se vendre, ce qui
est exactement ce qu'il faut pour un fret payé au voyage.

### Objectifs

- **`reach`** : entrer dans le cercle.
- **`leave`** : sortir du cercle — s'éloigner d'un port ou d'un lieu, dans
  n'importe quelle direction (« Quitter la rade »). La ligne d'objectif dit
  où l'on en est : « 0,4 M sur 1,0 M ».
- **`stop`** : rester dans le cercle sous `maxSpeed` nœuds pendant `hold`
  secondes (en panne, voiles ferlées ou au mouillage). La ligne d'objectif
  décompte les secondes.
- **`dock`** : être amarré ou mouillé dans le cercle (touche `M` au ponton).
- **`peche`** (Godot) : pêcher `kg` kilos depuis le début de l'étape
  (fishing/README.md). Le lieu (`at`) est **facultatif** : le poisson est à
  trouver, la ligne d'objectif ne dit que le compte, « 12,4 kg pêchés sur 40 ».
- **`vente`** (Godot) : vendre `kg` kilos de poisson au comptoir depuis le
  début de l'étape.
- **`achat`** (Godot) : acheter un navire au chantier (market/chantier.json).
- **`border`** · **`choquer`** (Godot) : border ou choquer l'écoute de `angle`
  degrés (15 par défaut) — les gestes du tutoriel, crédités à mesure que la main
  les fait. Pas de lieu.
- **`virer`** (Godot) : virer de bord `nombre` fois — le vent passe sur l'autre
  amure, vent devant ou lof pour lof. Pas de lieu.

Une pêche peut ne compter qu'une espèce : `"espece": "merou"` (ou `vivaneau`).
`"avant": 19` écrit à côté de l'objectif l'heure du ciel avant laquelle le
faire (« avant 19 h »), et la nuit tombée quand elle est passée ; rien ne casse.

Le compte de ces objectifs se sauvegarde avec l'étape (`compte` dans
`quetes.json`) : quitter à mi-pêche ne fait pas recommencer.

### Lieux (`at`)

Un lieu se donne par rapport à un **port** de la région (`world/caraibes.json`)
ou par ses **vraies latitude et longitude** : la carte réduite en fait ses
propres mètres.

| forme | sens |
|---|---|
| `{ "port": "port-morant" }` | la tête du ponton de ce port |
| `{ "port": "port-royal", "bearing": 180, "miles": 2 }` | à 2 milles de la carte du port, dans le relèvement 180° (0 nord, 90 est). `distance` en mètres au lieu de `miles` |
| `{ "lat": 19.85, "lon": -73.62 }` | latitude et longitude réelles, comme la carte les affiche |
| `{ "x": -4000, "z": 9000 }` | mètres du monde (0 = Port-Royal) |

`"island"` est encore compris à la place de `"port"` (ancien nom).

Ports de la Jamaïque, dans le sens des aiguilles depuis Port-Royal :
`port-royal`, `passage-fort`, `old-harbour`, `withywood`, `black-river`,
`savanna-la-mar`, `negril`, `lucea`, `montego-bay`, `dry-harbour`,
`port-maria`, `port-antonio`, `port-morant`, `yallahs` — les rades que porte
une carte anglaise de la fin du XVIIe siècle.

À l'échelle 0,4, de Port-Royal : Passage Fort 1,3 M, Old Harbour 5,9 M,
Yallahs 6,2 M, Port Morant 11,6 M, Negril — la pointe de l'ouest — 35,5 M.
Compter une heure pour six milles, davantage contre l'alizé.

## L'histoire en chapitres (Godot)

Tous les chapitres de l'histoire sont dans **`quests/histoire.json`**, sous
`"chapitres"`, dans l'ordre : le texte se relit et se retouche d'un seul tenant.
Chacun est une quête comme les autres (mêmes champs, mêmes objectifs), plus :

- `"cinematique"` : le film qui ouvre le chapitre (voir ci-dessous) ;
- `"fin"` : le texte de la question posée à la fin de la mission — passer au
  chapitre suivant, ou continuer à jouer librement.

Son numéro est son rang dans la liste (ou `"chapter"`), son `id` celui qu'il
écrit (sinon `chapitre-N`). Le menu **Histoire** commence le premier qu'on n'a
pas fini. Les missions, elles, restent chacune dans leur fichier.

**Tab** montre à tout moment les objectifs du chapitre : ce qui est fait, ce
qui est en cours et son compte, ce qui reste, et la consigne de l'étape (le
bord en batterie est passé à ⇧Tab).

Dans les textes, `{border}`, `{choquer}`, `{babord}`, `{tribord}`, `{toile}`,
`{pecher}` et `{objectifs}` deviennent la touche du clavier du joueur — un
texte écrit pour l'AZERTY dirait faux en QWERTY.

### La cinématique (`"cinematique"`)

```json
"cinematique": {
  "naufrage": { "navire": 7, "lat": 17.900, "lon": -76.800, "heure": 18.5,
                "force": 10, "duree": 70, "musique": "naufrage.ogg", "volume": 2 },
  "etablissement": { "port": "port-royal", "heure": 7.5, "duree": 24,
                     "rayon": 420, "hauteur": 90, "balayage": 110 }
}
```

- **`naufrage`** : le navire (son numéro dans `ships/index.json` — 7 est la Roter
  Löwe — ou le nom de sa fiche) sombre au lieu dit, par la force de vent donnée,
  à l'heure donnée : la foudre sur sa mâture, puis la mer qui entre ; quand il
  descend, ses pièces s'en vont en voltigeant et une sphère de lumière de trente
  centimètres descend au milieu d'elles, la caméra sous l'eau. `duree` borne la
  scène ; `musique` est un fichier de `medias/sound`, joué même musique coupée
  (absent : la tempête seule). Son épave reste au fond, inscrite au registre.
- **`etablissement`** : au matin, la caméra en arc au-dessus du port et de sa
  rade (`rayon` et `hauteur` en mètres, `balayage` en degrés), puis en fondu le
  ponton de départ et la fenêtre du chapitre.

Échap, Entrée ou Espace passent le film. Essais : `--quete chapitre-1` (avec le
film), `--sans-film` avant `--quete` (sans), `--objectifs` (le panneau de Tab
ouvert), `--essai-tuto` (l'écoute bordée puis choquée comme la main le ferait),
`--fin-chapitre` (la question de fin, tout de suite).

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

## La carte des missions — `carte-missions.json` (Godot)

Ce n'est pas une quête : le chargeur l'écarte. C'est le parchemin qu'ouvre **Missions** à l'écran titre
(`MissionMap.cs`, papier `parchment.gdshader`). Coordonnées en part de l'image 16:9 (x de 0 à 1 vers la
droite, y de 0 à 1 vers le bas) ; le parchemin va de 0,13 à 0,87, ses rouleaux tiennent les bords.

| champ | sens |
|---|---|
| `fond` | l'image du parchemin vierge, en 16:9, depuis la racine du projet (png, jpg ou webp) ; absente, le parchemin est dessiné (`parchment.gdshader`) |
| `chemin` | les points par où passe le trait, dans l'ordre ; il est lissé entre eux |
| `missions` | dans l'ordre du chemin. `titre` : ce qui est écrit, en anglaise ; `quete` : l'`id` d'une quête de ce dossier — absent ou inconnu, la mission paraît en encre pâle, « En préparation » ; `x`, `y` : le point rouge ; `etiquette` : `[x, y]` du coin bas-gauche du titre, ou `"centre"` |

Le trait est plein jusqu'à la première mission qu'on n'a pas finie, hachuré au-delà ; une mission finie
porte sa coche. Les quêtes qui ne sont pas sur la carte sont proposées en bas à droite. Toutes les quêtes
écrites se jouent depuis la carte, dans n'importe quel ordre. Tant que l'Histoire est fermée
(`StoryOpen`, `ShipDemo.Title.cs`), ses chapitres s'y jouent comme des missions, et leurs parties
s'enregistrent avec elles. Essai : `-- --carte-missions 1`.
