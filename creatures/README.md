# Créatures

## Le kraken — `creatures/kraken.glb`

Nommé par `settings.json` → `storm.kraken.glb`. Absent ou illisible : le jeu
dessine le kraken lui-même, comme avant. Le build embarque le fichier dans la
page publiée.

Le modèle de départ est le kraken dessiné, écrit en `.glb` :

```bash
node tools/kraken-glb.js
```

Ouvrez-le dans Blender, modifiez, puis **Fichier → Exporter → glTF 2.0**, format
**glTF Binary (.glb)**, textures **incluses** dans le fichier.

### Ce que le jeu lit, par le NOM des objets

| nom | rôle |
|---|---|
| `bras`, `bras.001`… | un bras, modelé **droit**. Dans Blender il est **debout sur l'axe +Z**, la base à l'origine, la pointe en haut : sa hauteur est sa longueur. Le jeu le courbe à chaque image le long du chemin du bras (arche quand il rôde, enroulement autour d'un mât quand il saisit). Plusieurs bras différents sont distribués à tour de rôle. |
| tout le reste | le corps : manteau centré sur l'origine, **yeux vers −Y** dans Blender (l'avant glTF est +Z). |

- **Les ventouses vont du côté +X du bras** : c'est ce côté que le jeu tourne
  vers ce que le bras enlace (le navire, le mât), et vers l'intérieur de l'arche.
- **Unités : le mètre.** Le bras de départ fait 16 m et 0,95 m de rayon à la
  base ; il est étiré à la longueur du chemin, et son épaisseur varie un peu
  d'un bras à l'autre.
- Les objets enfants d'un objet `bras` font partie de ce bras (ventouses
  modelées à part, par exemple).
- Matières : celles du fichier, textures comprises. L'émission est conservée
  (les yeux luisent la nuit).
- **Le relief : une normal map, pas un déplacement.** Le glTF n'a pas de canal
  de déplacement, l'export de Blender le jette sans rien dire. Cuire le relief
  (Cycles → Bake → Normal) et brancher l'image dans le « Normal Map » du
  Principled BSDF : le corps la prend tel quel, les bras la courbent avec eux
  (tangentes refaites au chargement ; la force du nœud Normal Map est reprise).
  Seules la couleur et la normal map passent sur les bras.
- Pas d'animation ni d'armature à prévoir : la courbure est calculée.
- Le rayon de touche du corps suit la taille du modèle.

## Les dauphins — `creatures/dolphin.glb`

Nommé par `settings.json` → `dolphins.glb`. Absent ou illisible : les dauphins
restent dessinés par le code. Le build l'embarque. Modèle de départ :

```bash
node tools/dolphin-glb.js
```

| nom | rôle |
|---|---|
| `queue` (ou `tail`, `caudale`) | la nageoire caudale. Son **origine est l'articulation** au pédoncule : le jeu la fait battre autour de son axe **X**. Ce qui lui est parenté bat avec elle. |
| tout le reste | le corps (ici `corps`, `aileron`, `nageoire_gauche`, `nageoire_droite`) |

- **Le nez vers −Y dans Blender** (l'avant glTF est +Z), le dos vers le haut ;
  unités : le mètre (2,35 m du bec au pédoncule, la caudale en plus).
- Le corps porte des couleurs de sommet (dos sombre, ventre clair) et des UV
  (u autour du corps, v du bec à la queue) pour peindre une texture.
- Pas d'armature ni d'animation à prévoir : la nage (arc, tangage, battement)
  est calculée. Les nageoires sont des plaques fines à matière double face.
- Chaque dauphin reçoit sa copie du modèle ; la taille varie un peu d'un animal
  à l'autre (×0,85 à ×1,2).

## Les marins — `creatures/sailor.glb`

**Pas chargé tant que les hommes sur le pont sont désactivés**
(`crew.enabled: false`, depuis le 2026-09-18) ; le build ne l'embarque pas non plus.

Nommé par `settings.json` → `crew.glb`. Absent ou illisible : les hommes
restent dessinés par le code. Le build l'embarque. Modèle de départ :

```bash
node tools/sailor-glb.js
```

| nom (de l'objet ou d'un parent) | rôle |
|---|---|
| `jambes` (ou `leg`, `culotte`, `chaussure`, `pied`…) | s'écartent et plient au genou quand la mer grossit |
| `corps` (ou `body`, `torse`, `chemise`, `shirt`) | la chemise : **teintée par homme** — peignez-la claire, la teinte multiplie |
| `bras` (ou `arm`, `main`, `hand`) | s'écartent de l'épaule sur un coup de roulis |
| `tete` (ou `head`, `bonnet`, `cap`, `chapeau`, `cou`) | tourne de temps en temps autour de l'axe vertical |
| tout le reste | suit le corps, sans mouvement propre |

- **Debout, les pieds sur l'origine**, **regard vers −Y dans Blender** (l'avant
  glTF est +Z) ; unités : le mètre, environ 1,75 m.
- Le mouvement est calculé pour ces hauteurs : **épaules vers x ±0,235 à
  1,41 m** (pivot des bras), respiration entre 1,0 et 1,45 m, jambes jusqu'à
  0,86 m. Gardez les proportions, ou les membres pivoteront au mauvais endroit.
- **Peu de triangles** (le modèle de départ en a 444) : chaque homme à bord est
  dessiné, et le mouvement est calculé sur chacun de ses sommets.
- Couleurs de sommet (couleurs du modèle de départ) et UV pour une texture ;
  plusieurs matières possibles, textures incluses dans le `.glb`.
- Pas d'armature ni d'animation à prévoir : tout est dans le shader.
- Tous les hommes partagent le modèle ; la taille varie un peu (×0,93 à ×1,05).

## La baleine — `creatures/whale.glb` (Godot)

Un cachalot de 15 m, nommé par `settings.json` → `whale.glb`. Absent ou
illisible : pas de baleine à voir (elle vit quand même, et peut frapper).
Modèle de départ :

```bash
node tools/whale-glb.js
```

| nom | rôle |
|---|---|
| `queue` | la nageoire caudale. Son **origine est la charnière**, au pédoncule ; le jeu la fait battre autour de son axe X, plus vite et plus ample quand elle force. |
| tout le reste | le corps (`corps`, `bosse`, `crete`, `machoire`, nageoires), **nez vers −Y** dans Blender (l'avant glTF est +Z), dos en haut, origine au milieu du corps. |

- **Unités : le mètre**, nez à +7 m, pédoncule à −6,2 m.
- La peau porte des couleurs de sommet (ardoise, plus pâle en bas). La
  **baleine blanche** (`whiteChance`, 4 %) garde le modèle et remplace ces
  couleurs par un blanc cassé.
- L'évent est pris à gauche du bout de la tête : le souffle d'un cachalot part
  en avant et à gauche, c'est ainsi qu'on le reconnaissait.

Réglages (`settings.json` → `whale`) : `perHour` (rencontres par heure, au
large), `awayFromShore` et `minDepth` (où elle vit), `sight` (où on l'aperçoit),
`surface`, `spout`, `dive` (son rythme, en secondes), `curious` et `hostile`
(ses humeurs), `charge` (m/s), `ramAgain`, `massTonnes`. En jeu : **⇧K** la fait
venir, et charger ; `-- --baleine 0|1|2` (indifférente, curieuse, hostile).


## Les poissons — `creatures/fish.glb` (Godot)

Des bancs dans les hauts-fonds. **Un seul modèle**, instancié : un banc est un
`MultiMesh`, et chaque poisson n'y porte que quatre nombres — sa phase, son
rayon de ronde, sa hauteur, sa taille. Tout le reste — où il est, où il regarde,
comment son corps est plié — est calculé par le shader à chaque image. Aucun
calcul par poisson côté processeur : c'est ce qui permet d'en mettre beaucoup.

Ce que le jeu demande au modèle :

- **Une seule maille**, sans squelette ni animation : la première qu'il trouve
  est prise, les autres sont ignorées. Fusionnez les nageoires dans le corps.
- **Dos en +Y**, donc le poisson est mince sur X, et sa longueur sur Z — la
  convention des coques. Le **sens** sur cet axe, lui, est un réglage : si le
  banc nage à reculons, mettez `"sens": -1` dans `settings.json` → `fish`, et
  rien d'autre à toucher. Pour le savoir sans lancer le jeu :
  `node tools/glb-look.js creatures/fish.glb` le devine (une caudale est un
  éventail, un museau une pointe).
- **L'échelle est libre.** Le jeu ramène la longueur du modèle (son étendue sur
  Z) à `settings.json` → `fish.taille`, comme une coque est ramenée à la
  longueur de sa fiche. Modelez à la taille qui vous arrange.
- **L'origine est libre** elle aussi : le jeu lit les bornes de la boîte et
  parle en « part du corps », de la queue au nez.
- Matière et couleurs de sommet **ignorées** : la robe est faite par le shader —
  dos sombre, ventre clair, une bande en travers, et une teinte qui change
  d'un poisson à l'autre. Un banc dont tous les individus ont exactement la même
  livrée se voit comme un décalque.

**Vérifiez l'export avant de lancer le jeu** — une sculpture qui n'y est pas ne
se voit qu'en jeu, et alors on cherche le bogue au mauvais endroit :

```bash
node tools/glb-look.js creatures/fish.glb
```

Il dit le contenu, les mesures, l'allongement et trace la silhouette. Un poisson
doit y montrer un allongement franc (3 : 1 et plus) ; « 1,4 : 1, PATATOÏDE »
veut dire que ce n'est pas le modèle que vous croyez exporter. Le cas le plus
fréquent est un modificateur de **multirésolution** : l'exportateur glTF de
Blender applique les modificateurs, mais pas celui-là, et sort la cage de base.
Appliquez-le (ou *Objet → Convertir → Maillage*) avant d'exporter.

Un modèle de départ, à ouvrir dans Blender :

```bash
node tools/fish-glb.js
```

Réglages (`settings.json` → `fish`) : `bancs` et `poissons` (combien, et de
combien), `fond_min` / `fond_max` (l'eau où ils tiennent), `portee` (jusqu'où
on en pose), `taille`, `rayon` (la largeur du banc), `vitesse` (en **longueurs
de corps par seconde**, donc un gros va plus vite), `battement` (coups de queue
par seconde), `balancement` (le balayage de la queue), `teinte` et `variete`.
En jeu : `-- --poissons 0|1`, pour voir ce qu'ils coûtent.
