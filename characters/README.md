# Les figurants

Un personnage animé **sans squelette**. Chaque sommet a sa colonne dans une
texture, chaque image de l'animation sa ligne ; le sommet y lit son déplacement
et s'écarte de sa pose de repos. Aucun os, aucune peau à calculer, et Godot
instancie les figurants entre eux puisqu'ils partagent maillage et matière —
quatorze ports coûtent un appel de dessin.

## Le dossier

```
characters/<nom>/
  vat_export.glb              le maillage, avec UV2 = la colonne du sommet
  vat_export_positions.exr    les déplacements, une ligne par image
  vat_export_normals.png      les normales, même grille
  <quelquechose>_albedo.png   sa peau — facultative, voir plus bas
```

**VAT Toolkit n'exporte QUE de la géométrie.** Son glTF n'a ni `materials`, ni
`images`, ni `textures` : le maillage sort nu, et `primitive.material` est
absent. Mais il garde les **UV**, intactes et dans le carré unité — il suffit
donc de déposer l'image de couleur à côté des trois autres fichiers. Le chargeur
prend le premier fichier dont le nom porte `albedo`, `basecolor`, `diffuse` ou
`couleur`. Sans elle le personnage est uni (`u_tint`), et la console le dit.

Lu par [`godot/scripts/Vat.cs`](../godot/scripts/Vat.cs), animé par
[`godot/shaders/vat.gdshader`](../godot/shaders/vat.gdshader).

**Godot seulement.** Quinze mégaoctets de texture flottante ne tiennent pas dans
les seize de la page, et le shader qui les lit non plus — c'est le même cas que
La Boussole et le sloop. La page bâtit simplement ses pontons sans figurant.

## La cuisson, dans Blender

Extension **VAT Toolkit**, onglet *Vertex Animation* :

| réglage | valeur | pourquoi |
|---|---|---|
| **Positions** | `Offsets` | des déplacements, pas des positions absolues. Lues comme absolues, tous les figurants se rassembleraient à l'origine du monde |
| **Backend** | `VAT Texture` | l'EXR |
| **Normalize (for PNG)** | décoché | l'EXR porte des mètres bruts, rien à remapper |
| **Wrap** | `None` | |
| **Range** | 1 → fin+1 exclue, pas de 1 | une ligne par image |

Et **sans compression** dans l'EXR : le lecteur la refuse, en le disant.

## Ce que l'outil convertit — et ce qu'il oublie

Relevé sur `pirate_0001`, et c'est le piège de ce format :

- les **positions** sortent déjà dans le repère du glTF (Y vers le haut) ;
- les **normales** sortent dans celui de Blender (Z vers le haut).

D'où deux cadrans séparés dans le shader, `u_axes` (0) et `u_nrmaxes` (1). Avec
la normale non convertie, le personnage est **noir** quel que soit le soleil —
un symptôme qui ne ressemble pas à sa cause, d'où cette ligne.

## Les cadrans du shader

| | défaut | rôle |
|---|---|---|
| `u_fps` | 24 | les images par seconde de la scène qui a cuit |
| `u_frames` | lu sur la hauteur de la texture | |
| `u_pingpong` | 1 | **aller-retour** plutôt que boucle : une cuisson dont la première ligne est la pose de repos et la dernière en plein mouvement claquerait à chaque tour. Zéro pour une animation vraiment cyclique |
| `u_axes` | 0 | les axes du déplacement |
| `u_nrmaxes` | 1 | ceux de la normale — voir plus haut |
| `u_texnrm` | 1 | 0 garde la normale du maillage, figée : cadran de diagnostic |
| `u_amp` | 1 | 0 fige la pose de repos, pour juger le maillage seul |
| `u_tint` | gris-brun | le `.glb` de `pirate_0001` n'a ni matière ni image : il est uni tant qu'on ne lui donne pas sa peau |

`u_phase` est un **uniforme d'instance** : deux figurants côte à côte qui
respirent au même rythme se lisent comme un seul objet dupliqué.

## Ajouter un figurant

Déposer le dossier, puis l'appeler par son nom — `Vat.Load("pirate_0001")`. Il
faut ensuite décider où il se tient ; `pirate_0001` est posé sur le tablier des
pontons par `JettyNode.Figurant`, aux deux tiers du musoir, face au large.

Le maillage sort **normalisé à un mètre de haut** : c'est le chargeur qui le
ramène à la taille d'un homme (1,75 m), et l'échelle emporte ses déplacements
avec lui.

## `vat_export` reste, et ce n'est pas un oubli

Le dossier de `pirate_0001` porte deux cuissons : `vat_export4`, qui sert, et
`vat_export`, qui ne sert plus. **Elle est gardée exprès.**

C'est la seule qui reste en **EXR non normalisé**, donc le seul fichier qui
éprouve encore ce chemin de lecture — celui que Godot ne sait pas ouvrir et que
`Vat.LisExr` ouvre à la main. L'effacer laisserait soixante lignes de décodeur
sans rien pour les vérifier, et la panne ne se découvrirait qu'au prochain
modeleur qui choisirait ce format.

Elle coûte 16,7 Mo. C'est le prix d'un témoin, et il est bas.
