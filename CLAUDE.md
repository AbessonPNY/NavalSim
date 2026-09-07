# Simulation navale — notes de projet

Simulation 3D d'un navire à voile : flottaison d'Archimède réelle sur une houle
de Gerstner, gouvernail, gréement. Aucune dépendance npm — Node et un navigateur
suffisent. three.js vient d'un CDN.

## Démarrer

```bash
node build.js          # produit dist/naval-sim.html (fichier autonome publiable)
```

Le serveur de dev est déclaré dans `.claude/launch.json` (`node .claude/serve.js`,
port 8765) : demandez-moi de le lancer, ou `node .claude/serve.js`.

Artifact publié : https://claude.ai/code/artifact/e9837c06-9839-4888-a33a-bd03cdcd233c
Pour le mettre à jour depuis une nouvelle session, il faut passer cette URL
explicitement, sinon un second artifact est créé.

## Architecture

`naval-sim.html` ne contient que le balisage et un `main()` de câblage. Toute la
logique est dans `js/`, en classes attachées à un espace de noms global `Naval`
(pas de modules ES : le build inline les fichiers par simple concaténation).

| module | rôle |
|---|---|
| `config.js` | constantes du monde (ρ, g, grille de sondes, liste de repli des navires) |
| `ship-spec.js` | lit une fiche JSON et en **dérive** tout ce que le solveur consomme |
| `hull-lines.js` | le plan de formes, en fonctions pures |
| `stage.js` | renderer, scène, lumière, ciel |
| `ocean.js` | houle de Gerstner : shader GPU **et** échantillonnage CPU |
| `ship-model.js` | coque, gréement, voiles, sillage, chargement .glb |
| `ship-physics.js` | sondes, corps rigide 6 ddl, gouvernail, voiles |
| `controls.js` · `camera-rig.js` · `hud.js` | barre, caméras, instruments |

## Invariants à ne pas casser

**Un seul plan de formes.** `hull-lines.js` sert à la fois au maillage visible et
à la grille de sondes. Si les deux divergent, ce qu'on voit ne correspond plus à
ce qui flotte — c'est l'invariant le plus important du projet.

**Le shader et le CPU doivent partager les mêmes vagues.** `ocean.js` calcule la
phase en **espace monde** (`modelMatrix * position`) car le plan est recentré sur
la caméra à chaque image. En espace local, la houle resterait collée à la caméra
et se désynchroniserait du champ de hauteur que le solveur échantillonne.

**Le gouvernail est une force à l'étambot, jamais un couple pur.** C'est ce qui
place le point de pivot en avant du maître-couple, comme sur un vrai navire
faisant route avant, au lieu de la faire tourner autour de son milieu.

**Le cap se lit sur le vecteur d'étrave**, pas sur l'angle d'Euler. Le prendre
sur Euler donnait un signe inverse, qui annulait exactement une erreur de sens
du gouvernail : les deux fautes se masquaient mutuellement.

**Ne jamais réutiliser un vecteur temporaire pour une valeur vivante.** Le centre
de gravité monde a son propre vecteur (`_cog`) : il avait été aliasé sur `_tmp`,
écrasé dès la première sonde, ce qui faussait tous les bras de levier et
provoquait roulis parasite puis explosion numérique.

**Les coefficients hydro sont par unité de surface**, pas des forces absolues.
C'est ce qui permet aux mêmes valeurs de servir une goélette de 24 m et une
frégate de 60 m sans réglage par navire.

## Fiches navires

Un JSON par bâtiment dans `ships/`. **Le dossier fait foi** : y déposer un
fichier suffit (le serveur de dev liste le dossier en direct, le build le balaie).
Le **tonnage est l'entrée** ; la fraction immergée en découle. Viser 30–45 %
d'immersion coque.

```bash
node tools/add-ship.js ships/models/mon-bateau.glb --nom "La Sirène"
```

L'outil lit la boîte englobante réelle du .glb et mesure le volume d'enveloppe
**avec les mêmes formules que le solveur**, pour proposer un tonnage cohérent.

Voir `ships/README.md` pour le format complet.

## Contraintes de publication (raison d'être de build.js)

Une page publiée tourne sous une politique de sécurité stricte. Le build existe
pour la satisfaire :

- un `<script src="js/...">` **local est bloqué** → tout est inliné ;
- `fetch()` d'un fichier local est bloqué → les fiches JSON sont embarquées dans
  `Naval.SHIP_DATA` ;
- un `.glb` n'est ni chargeable localement ni téléversable comme asset d'artifact
  (types acceptés : png, jpg, svg, mp4, pdf, woff, csv, md, json, txt) → ses
  octets sont embarqués en base64 et analysés par `GLTFLoader.parse()`, sans
  aucune requête réseau ;
- le build **refuse** de produire un fichier contenant encore une référence locale.

`GLTFLoader` reste chargé depuis jsdelivr via l'import map. S'il est bloqué, la
coque procédurale est conservée avec un avertissement : un modèle manquant
n'interrompt jamais la simulation.

## Pièges rencontrés

**Encodage.** `Get-Content` en PowerShell 5.1 lit en ANSI, pas en UTF-8 : extraire
puis réécrire un fichier accentué produit du mojibake et un BOM. Utiliser
`[System.IO.File]::ReadAllText/WriteAllText` avec un encodage explicite.

**Git et les .glb.** Ils sont binaires ; une conversion de fins de ligne les
corromprait silencieusement. `.gitattributes` les marque `binary`.

**Mesures dans le navigateur.** Quand le volet de prévisualisation est masqué, le
navigateur bride `requestAnimationFrame` à ~1 image/s : la simulation tourne au
ralenti et les relevés semblent figés ou absurdes (vitesse qui ne monte pas,
immersion incohérente). Ce n'est pas un bug de physique. Vérifier
`document.hidden` avant de conclure.

**Shaders.** Un `ShaderMaterial` avec `fog: true` doit fusionner
`THREE.UniformsLib.fog`, sinon le rendu lève une erreur sur `fogColor.value`. Et
un `#define N` écrase l'identifiant `N` jusque dans le fragment shader.

**Rendu.** Une mise à l'échelle négative (`scale.x = -1`) fait disparaître un
maillage. Une voile opaque éclairée à contre-jour devient noire : le tissu porte
une composante `emissive` pour rester lisible.

## Conventions

Interface et commentaires en français pour l'utilisateur ; commentaires de code
en anglais. Explication du *pourquoi*, pas du *quoi*.
