# Peindre une voile

Une fiche déclare ses voiles peintes dans `appearance.canvasMap`, **une entrée
par type de voile** :

```json
"appearance": {
  "canvas": "0xece4d2",
  "canvasMap": {
    "square": "ships/textures/ma-voile.png"
  }
}
```

Les clés possibles sont `square` (les voiles carrées — fait), puis `jib` et
`gaff` (à venir). Un type qui n'est pas nommé garde la toile unie, donc on peut
peindre les carrés d'un navire sans toucher à son foc.

Le chemin est **relatif à la racine du jeu**, comme celui d'un `.glb`.

## Ce que la texture doit être

- **PNG, JPG ou WEBP.** Le build refuse le reste.
- **Carrée**, et une puissance de deux si possible (512, 1024). Ce n'est pas
  obligatoire mais le mipmapping y est meilleur, et une voile est presque
  toujours vue de loin et de biais.
- **Une image par voile entière**, pas un motif à répéter : elle est étirée de
  la têtière au point et d'une ralingue à l'autre. L'enroulement est en
  `ClampToEdge`.
- **La couleur de `appearance.canvas` multiplie l'image.** C'est voulu : c'est
  ce qui garde une voile peinte du même écru fatigué que les voiles unies du
  même navire. Peignez sur un fond clair, pas sur du blanc pur.

## L'orientation

`node tools/uv-chart.js` écrit `uv-carree-repere.png`, qui est le repère — et
qui porte ce nom-là exprès : il ne doit jamais pouvoir écraser une vraie toile.

| coin de l'image | coin de la voile |
|---|---|
| haut gauche, **rouge** | têtière, bâbord |
| haut droit, **vert** | têtière, tribord |
| bas gauche, **bleu** | point, bâbord |
| bas droit, **jaune** | point, tribord |

La flèche pointe vers la **têtière**. Le haut de l'image est le haut de la
voile — si votre motif arrive la tête en bas, c'est l'image qu'il faut
retourner, pas le code.

Le bandeau sombre en haut du repère est la **têtière**, envergée sur sa vergue :
cette bande-là est cachée par l'espar, ne mettez rien d'important dedans.

Le quadrillage du repère est en 8 × 8, mais une voile **carrée** est maillée en
**16 colonnes sur 8 rangs** — les festons du ferlage l'ont exigé. Un détail plus
fin qu'un seizième de largeur ou qu'un huitième de hauteur est dessiné pour un
maillage qui ne peut pas le porter.

**Vue de l'autre bord, l'image est en miroir**, et c'est juste : on regarde
l'envers de la toile. Un motif qui doit rester lisible des deux côtés doit être
symétrique — comme l'est une croix, et comme ne l'est pas un texte.

**Ferler ne déplace pas le motif.** Les UV sont posées sur la grille
paramétrique et non sur les positions finales, donc le creux, la coupe,
l'affaissement et l'enroulement bougent la toile sans traîner la peinture
dessus : la voile est peinte avant d'être enverguée, et la peinture suit le
tissu. Vérifié, un ferlage complet ne bouge pas une UV.

## Et le bord n'est pas droit

C'est le piège de cet UV et la raison d'être du repère. Une voile carrée est
**taillée creuse** : ses ralingues rentrent à mi-hauteur pour passer au clair
des haubans, et son point est relevé au milieu — le rond de fond, qui dégage
les étais et le mât au-dessous.

Le carré unitaire de l'image tombe **entièrement** sur la toile — rien n'est
coupé — mais il est **déformé** : le milieu du bas de l'image est tiré vers le
haut par le rond de fond, et les milieux des côtés sont tirés vers l'intérieur.
Un motif centré dans l'image arrive bien au centre de la voile ; un motif
appuyé sur le bord de l'image suit la courbe de la ralingue au lieu d'une
droite. Peignez le repère, regardez-le en place, ajustez.

## Publication

Une page publiée ne peut pas aller chercher un fichier local. `node build.js`
lit l'image et **réécrit le chemin en `data:` URI** dans la fiche embarquée,
exactement comme il embarque les octets d'un `.glb`. Rien à faire de plus — mais
comptez le poids : une texture de 1024 en PNG pèse vite plusieurs centaines de
kilo-octets dans le fichier publié, et un JPG de qualité 85 fait le même travail
pour un dixième.

Le build **avertit** si le fichier est absent. Cet avertissement compte plus
qu'ailleurs : une texture manquante laisse la toile unie, ce qui ressemble
exactement à une voile qu'on n'a pas encore peinte — une panne qu'on ne
distingue pas de l'état voulu est une panne qui part en production.

## Poids

Une toile est du **bruit** : un tissage, un grain, des fibres. C'est le pire cas
pour un PNG, qui est sans perte et compresse par lignes — la toile carrée de ce
dossier fait 305 Ko en 512 px, et une fois embarquée en base64 elle ajoute
**400 Ko** au fichier publié, qui passe de 2,68 à 3,02 Mo.

Un JPEG de qualité 85–90 rend le même tissage pour une fraction de ça, et les
artefacts d'un JPEG sur du bruit sont exactement ce qu'on ne voit pas. Gardez le
PNG si la voile porte un motif à bords nets — un blason, une croix, des bandes —
où le JPEG baverait ; prenez le JPEG pour de la toile.
