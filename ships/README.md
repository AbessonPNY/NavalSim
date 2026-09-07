# Fiches navires

Un fichier JSON par bâtiment. **Le dossier fait foi** : déposez le fichier ici,
rechargez la page, il est dans le sélecteur. Rien à déclarer ailleurs.

## Ajouter un navire depuis un .glb

```bash
node tools/add-ship.js ships/models/mon-bateau.glb --nom "La Sirène"
```

L'outil lit la boîte englobante réelle du maillage, en déduit longueur, largeur
et tirant, **mesure le volume d'enveloppe avec les mêmes formules que le
solveur**, et fixe un tonnage qui la pose à ~36 % d'immersion. Il écrit
`ships/<id>.json`, qu'il ne reste qu'à affiner.

Options : `--tonnes N` pour imposer le déplacement, `--id slug`, `--force` pour
écraser une fiche existante.

Une fiche illisible est ignorée avec un avertissement en console — elle
n'empêche jamais les autres navires de se charger.

## Principe

Le JSON énonce ce qu'énoncerait un architecte naval : dimensions principales,
coefficients de formes, un **déplacement en tonnes**, un plan de voilure. Tout ce
dont le solveur a besoin en est *dérivé*. Un navire plus grand est donc plus grand
en tout — résistance, giration, inertie — sans un seul nombre magique par bateau.

Les coefficients hydro sont **par unité de surface**, jamais des forces absolues :

| champ | unité | s'applique à |
|---|---|---|
| `hydro.resistance` | N par (m/s)² par m² | maître-couple (`beam × keelDepth`) |
| `hydro.lateralGrip` | N par (m/s) par m² | plan de dérive (`length × keelDepth`) |
| `rudder.power` | idem | plan de dérive |

C'est ce qui permet aux mêmes valeurs de servir une goélette de 24 m et une
frégate de 60 m.

## Le tonnage est l'entrée

`displacementTonnes` fixe la masse. La fraction de volume immergé en découle.
Si elle dépasse 1, le navire coule et la console vous le dit explicitement au
chargement — c'est une erreur de fiche, pas de physique.

## Champs de formes

| champ | effet |
|---|---|
| `sectionTuck` | 0 = section en V pincée ; 1 = section carrée. Bas = voilier, haut = navire de charge |
| `sectionPower` | courbure du bouchain |
| `waterlinePower` / `waterlineFull` | finesse des entrées d'eau |
| `transomWidth` | largeur conservée au tableau arrière |
| `dragAft` | quille plus profonde à l'arrière |
| `forefootLift` / `counterLift` | relevé de l'étrave et de la voûte |

## Gréements

`rig.type` vaut `"gaff"` (bôme pivotante), `"square"` (vergues carrées) ou
`"none"`. En carré, chaque mât liste ses vergues en fractions de sa hauteur.

Le solveur ne modélise **qu'une seule aile équivalente** (`sailArea`, `ceHeight`),
pas chaque voile séparément — le gréement est une représentation visuelle.

## Modèles .glb

```json
"model": { "glb": "ships/models/barge.glb", "lengthAxis": "z",
           "offset": [0,0,0], "rotationY": 0 }
```

Convention : **étrave vers +z**, origine sur la flottaison, au maître-couple. Le
modèle est mis à l'échelle pour que sa longueur corresponde à `hull.length`
(ou forcez-la avec `"scale"`).

**Le .glb ne porte que l'apparence.** La flottaison vient toujours des cotes du
JSON : on ne peut pas déduire un volume de carène fiable d'un maillage
quelconque sans une voxelisation coûteuse. Si le modèle ne charge pas, la coque
procédurale est conservée et un avertissement est écrit en console.

`node tools/make-glb.js ships/models/exemple.glb` produit un .glb minimal valide,
utile comme gabarit.

### Publication : le modèle est embarqué

En local, le .glb est chargé par le serveur de développement. Au build, ses
octets sont encodés en base64 et **embarqués dans la page** (`model.glbBase64`),
puis analysés en mémoire par `GLTFLoader.parse()` — aucune requête réseau, donc
aucune politique de sécurité à satisfaire.

C'était nécessaire : une page publiée ne peut pas lire un fichier local, et le
`.glb` n'est pas un type téléversable comme *asset* d'artifact (seuls png, jpg,
svg, mp4, pdf, woff, csv, md, json, txt le sont).

Coût : le base64 gonfle les octets d'environ un tiers, et la page entière est
plafonnée à 16 Mo. Un modèle de quelques Mo passe sans peine ; au-delà,
décimez le maillage ou restez sur la coque procédurale.

Le chargeur `GLTFLoader` lui-même vient de jsdelivr via l'import map. S'il est
bloqué, le repli sur la coque procédurale s'applique — la simulation ne s'arrête
jamais pour un modèle manquant.
