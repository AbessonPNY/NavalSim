# Simulation navale — notes de projet

Simulation 3D d'un navire à voile : flottaison d'Archimède réelle sur une houle
de Gerstner, gouvernail, gréement, combat, commerce. Aucune dépendance npm —
Node et un navigateur suffisent. three.js (r160) vient d'un CDN.

**Ce fichier est court exprès** : il est relu à chaque échange. Le récit
complet — chaque décision, chaque mesure, chaque piège — est dans
**`docs/journal.md`** (≈ 320 Ko). Ne le lire qu'à la section utile
(`grep -n "^## " docs/journal.md`, puis `sed -n` sur la plage), jamais en
entier. Toute leçon nouvelle s'y ajoute, dans la section qui la concerne ;
ici ne vont que les règles qu'il faut avoir en tête à chaque modification.

## Démarrer

```bash
node build.js          # produit dist/naval-sim.html (fichier autonome publiable)
```

Serveur de dev : `.claude/launch.json` (`node .claude/serve.js`, port 8765).

Artifact publié : https://claude.ai/code/artifact/e9837c06-9839-4888-a33a-bd03cdcd233c
— passer cette URL explicitement pour le mettre à jour, sinon un second
artifact est créé.

## Architecture

`naval-sim.html` ne contient que le balisage et un `main()` de câblage. Toute la
logique est dans `js/`, en classes attachées à l'espace de noms global `Naval`
(pas de modules ES : le build inline les fichiers par concaténation).
`Naval.app` expose les instances vivantes pour le débogage.

| module | rôle |
|---|---|
| `config.js` | constantes du monde (ρ, g, grille de sondes, liste de repli des navires) |
| `weather.js` · `storms.js` | le vent qui se conduit seul · les dépressions, qui ont un lieu |
| `rain.js` · `snow.js` · `splash.js` | le rideau de pluie · la neige · l'eau jetée par ce qui tombe dedans |
| `climate.js` | la température (en mots), les averses, pluie ou neige |
| `wreck-air.js` | l'air qui remonte d'une épave |
| `lightning.js` · `kraken.js` | la foudre qui tombe sur une tête de mât · le monstre des dépressions |
| `ghosts.js` | la flotte fantôme du Cimetière des Galions (nuit, `Naval.app.fantomes(true)` pour y aller) |
| `spyglass.js` | la lunette |
| `bloom.js` | la lueur des lumières trop vives, la nuit seulement |
| `anchor.js` | mouiller et lever l'ancre |
| `flotsam.js` | débris d'un naufrage, la bouteille, la cargaison échouée |
| `ship-spec.js` | lit une fiche JSON et en **dérive** ce que le solveur consomme |
| `hull-lines.js` | le plan de formes, en fonctions pures |
| `stage.js` · `calendar.js` | renderer, scène, lumière, ciel, lune · la date et la saison |
| `ocean.js` | houle de Gerstner : shader GPU **et** échantillonnage CPU |
| `foam.js` · `ssao.js` | champ d'écume persistant · occlusion ambiante du navire |
| `underwater.js` | la coque vue à travers l'eau |
| `world.js` · `land.js` | la Jamaïque (×0,4) lue dans `world/` (relief, ports, modèles posés, `Naval.Geo`) · le relief en carreaux |
| `jetty.js` | le ponton d'un port |
| `chart.js` | la carte marine |
| `quests.js` | les quêtes : étapes, lieux, objectifs (`quests/*.json`) |
| `ship-model.js` | coque, gréement, voiles, pavillons, lanternes, safran, fenêtres de nuit, avirons, .glb |
| `ship-physics.js` | sondes, corps rigide 6 ddl, gouvernail, voiles, avirons |
| `controls.js` · `camera-rig.js` · `hud.js` | barre, caméras (vues à bord dans la fiche), instruments |
| `helm.js` | la barre des navires qui ne sont pas le vôtre |
| `guns.js` · `explosion.js` | la bordée et sa fumée · la soute qui saute |
| `cordage.js` | les bouts rompus |
| `crew.js` | les hommes sur le pont — **désactivés** (`crew.enabled`), gardés pour un marin qui manœuvre |
| `gulls.js` · `dolphins.js` | les mouettes (à moins de 2 km des côtes) · les dauphins de l'étrave, par mer calme |
| `sound.js` | le bruit, et le temps qu'il met à venir |
| `capture.js` | la capture d'écran (touche `I`), écrite par le serveur de dev |
| `purse.js` | la bourse, le cours des épices, la poudre |

Réglages de jeu : `settings.json` (son : musique d'ambiance, coupée par défaut ; rencontres, nuit, bloom, naufrage, tempête : foudre et kraken, baleine, serpent de mer, brume de surface et estime (Godot), fantômes, calendrier, climat, dauphins, rechargement des pièces, hommes sur le pont, caustiques du fond, bancs de poissons).
Réglages d'aspect à l'œil (Godot, en tête de shader) : **`u_shadow`** dans `godot/shaders/ocean.gdshader` — l'ombre des coques sur l'eau (0,5 ; 0 = aucune, 1 = encre) ; et `AmbientGain` dans `godot/scripts/SkyNode.cs` — la lumière du ciel dans l'ombre des navires (1,0 ; plus bas = ombres plus franches), et `SunGain` à côté — le soleil de la page ramené à l'unité de Godot (1/π) ; par-dessus, l'étude de la lumière au menu (Lumière : force, chaleur, éclairage ambiant ; `reglages.ini` → `[lumiere]`). **`u_veil`** dans `godot/shaders/ocean.gdshader` — la part du dessous qui passe à travers la surface à épaisseur nulle, donc la netteté de la ligne d'eau sur le bordé (0,55 ; 1 = vitre, 0 = encre ; atténue aussi les hauts-fonds).
Monde : `world/caraibes.json` + `world/caraibes-relief.png` (relief peint en gris), format dans `world/README.md` ; `tools/region-heightmap.js` repart des côtes réelles. Godot : une région par fiche de `world/` (la Tortue : `world/tortue.json`), reliées par des **traversées** comptées et non naviguées (`core/Passage.cs`, atterrages `approaches`) — changer de région recharge la scène.
Quêtes : `quests/*.json`, format dans `quests/README.md` (`Naval.app.allerQuete()` pour sauter à l'étape).
Objets flottants : `props/Props.json`. Pavillons : `ships/textures/flags/flags.json`.
Fiches navires : `ships/*.json`, format dans `ships/README.md`. Kraken, dauphins, baleine et marins en `.glb` : `creatures/README.md`.

## Règles à ne jamais casser

**Plusieurs navires à flot.** Tout état d'un navire vit sur *son* instance ou
*son* entrée de flotte, jamais dans un global ni dans un uniforme de la mer.
L'entrée 0 est le navire commandé ; l'indice dans la flotte **est** la ligne de
la texture de profils (tout retrait réécrit les profils). Les commandes (`ctrl`)
sont passées à chaque image, jamais retenues. Mettre une coque à l'eau passe
par `launch()` (atmosphère, éclairage, `settle()` avant de positionner, profil).
`MAX_SHIPS` = 8 dans la page, mesuré : c'est un plafond, six est le confort.
Godot : `Config.MaxShips` = 16, mesuré (16,8 ms/image à seize) ; le relever
touche aussi `NSHIP` (`hull_gap.gdshaderinc`) et les tableaux de `motion_blur.glsl`.

**Un seul plan de formes.** `hull-lines.js` sert au maillage visible et à la
grille de sondes. Côté Godot, même règle pour les shaders : la houle est dans
`gerstner.gdshaderinc` (mer, écume, caustiques du fond), les rides dans
`ride.gdshaderinc` (mer et fond), l'abri dans `shelter.gdshaderinc`. Plus généralement : **une définition, plusieurs usagers**
(`SKY_GLSL`, `SAIL_FOIL`, `HULL_GLSL`, `_shore`, `GLOW_NAMES`…) — jamais une
constante réécrite à deux endroits.

**Origine flottante.** Tout est calculé près de zéro, `ocean.origin` retient le
vrai zéro ; au-delà de 1500 m le monde glisse. La phase de houle est réduite
modulo 2π (`syncPhase`). Tout ce qui tient une position se décale dans la même
image. `world.js` et ce qui est « du monde » travaillent en mètres **vrais**.

**Trois calculateurs lisent la houle et l'abri** — vertex shader de la mer,
échantillonneur CPU, passe d'écume. En oublier un désynchronise en silence.
Phase en espace **monde** dans le shader.

**Repères.** Étrave +z, haut +y, donc **tribord = −x local** ; nord +z, donc
**est = −x monde**. `side = +1` veut dire tribord partout. Le cap se lit sur le
vecteur d'étrave, pas sur Euler. Le gouvernail est une force à l'étambot.

**Ne jamais réutiliser un vecteur temporaire pour une valeur vivante.**

**Lumières : ne jamais ajouter/retirer/masquer une lumière en jeu.** three
recompile tout contre le nombre de lumières visibles. Réserve créée au
démarrage, commutée par l'intensité ; passes compilées à l'armement.

**Ce qui réfléchit suit la lumière** (écume, fumée, cordages : couleur
d'horizon) ; **ce qui émet ne la suit pas** (flamme, éclair, fenêtres).

**Physique à pas fixe** : un sous-pas ≤ 1/15 s, `t` avancé à chaque sous-pas ;
les vitesses et plafonds s'écrivent **par seconde**, jamais par image.

**Coefficients hydro par unité de surface.** Une fiche est un tout cohérent :
longueurs ×s, surfaces ×s², vitesse machine ×√s.

## Contraintes de publication

**Deux définitions d'un même modèle, et une seule des deux est partagée.** La
page est un fichier autonome plafonné à 16 Mo ; Godot lit sur le disque et n'a
pas de plafond. Un modèle placé dans **`godot-models/<son chemin habituel>`**
remplace, pour Godot seulement, celui de la racine (`Assets.Path`) : on exporte
une fois en pleine définition là, puis `node tools/page-models.js` en tire la
copie allégée à sa place habituelle, et `node build.js` vérifie la limite.
Format et raisons : `godot-models/README.md`. Les données (fiches, monde,
quêtes, réglages) restent partagées — jamais de doublon.

La page publiée ne peut rien charger de local : `build.js` inline tous les
scripts, embarque fiches, réglages, props et pavillons (`Naval.SHIP_DATA`,
`SETTINGS`, `PROPS_DATA`, `FLAGS_DATA`, `QUESTS_DATA`, `REGION_DATA` avec le relief en data: URI), les `.glb` et sons en base64, les
images et `url()` CSS en `data:` URI, et **refuse** un fichier contenant encore
une référence locale. Textures d'un modèle : **dans** le `.glb`. Ce que le code
peut dessiner (lanterne, fumée, embrun, tête de mort) est dessiné sur un canvas.
La musique n'est pas embarquée (trop lourde). `node build.js` écrit
`ships/index.json` et avertit si `Naval.Config.SHIPS` a dérivé du dossier.
`<meta charset>` à l'octet 0. `.glb` marqués `binary` dans `.gitattributes`.

## Pièges fréquents

- **Shaders** : un accent grave dans un commentaire GLSL ferme le template
  literal (`node --check js/*.js`) ; `half` est réservé ; `#define N` écrase `N` ;
  `ShaderMaterial` avec `fog:true` fusionne `UniformsLib.fog` ; un
  `ShaderMaterial` au-dessus et au-dessous de l'eau déclare `clipping:true` et
  les morceaux `clipping_planes_*`.
- **Patches de matériau chaînés** : conserver le `onBeforeCompile` précédent,
  la brume en dernier, uniformes sous garde (`SUN_UNIFORMS_GLSL`), et une
  `customProgramCacheKey` propre à chaque greffe.
- **Un attribut `hidden` ne gagne pas contre un `display` de classe** : règle
  `[hidden]{display:none}` explicite.
- **Encodage PowerShell 5.1** : `Get-Content` lit en ANSI ; utiliser
  `[System.IO.File]::ReadAllText/WriteAllText` avec encodage explicite.
- **Clavier** : tester `e.code` (AZERTY), repli sur `e.key` si `code` est vide ;
  pas de touches de fonction (F6 est pris par le navigateur) ; ignorer la frappe
  dans un champ ; refuser `repeat` pour les commandes à un cran.
- **Effets de bord** : `setSun()` rallume les lumières et libère la texture
  d'environnement ; `settle()` aplatit la mer puis la rend.

## Mesurer dans le navigateur

- Le volet de prévisualisation bride `requestAnimationFrame`, jusqu'à zéro :
  vérifier que `ocean.uniforms.uTime` avance, sinon piloter le solveur à la
  main (`physics.step(dt, ocean, ctrl, t)`), ce qui donne des mesures à pas fixe.
- Boucle gelée : recopier `uCam` après avoir bougé la caméra.
- Lire des pixels : `gl.bindFramebuffer(gl.FRAMEBUFFER, null)` d'abord ;
  `gl.finish()` ne bloque pas sous ANGLE et le chrono GPU rend zéro sur les
  petites passes → temps mural de lots entrelacés terminés par `readPixels`.
- `read_console_messages` garde le tampon d'un chargement à l'autre ;
  `fetch(url, {cache:'reload'})` avant de recharger.
- Laisser décanter ~15 s après un changement d'état de mer avant de compter ;
  rendre les rappels de test d'origine ; masquer les coques qu'on ne mesure pas.
- Un effet réglé à l'œil au milieu d'autres mesure la somme : éteindre les
  voisins avant de juger.
- Captures d'écran et longues mesures : **demander avant** (coût en tokens).

## En suspens

- LOD des navires éloignés : simulation (`LOD_FAR`) et masquage par la brume
  (`HAZE_HIDE`) faits, journal « Les coques lointaines » ; le gain de rendu
  reste à mesurer sur une boucle qui tourne.
- Relief (normal map) : interrupteur et intensité dans `settings.json`.
- Fusionner les voies d'eau d'un même endroit (voir le journal).
- Chaloupe : console de barre encore celle d'un navire, pas de `.glb`.

## Conventions

Interface et commentaires destinés au joueur en français ; commentaires de code
en anglais. Expliquer le *pourquoi*, pas le *quoi*. Commit et push seulement
quand on le demande.
