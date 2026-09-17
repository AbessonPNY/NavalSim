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
