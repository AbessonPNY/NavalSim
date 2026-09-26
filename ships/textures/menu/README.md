# Les visuels du choix de navire

Une image par navire, montrée sur sa carte quand on ouvre **Jeu libre**.

## Le nom du fichier

Celui de la fiche, sans `.json` :

```
ships/textures/menu/boussole.jpg      → ships/boussole.json
ships/textures/menu/frigate17e.jpg    → ships/frigate17e.json
```

Rien à déclarer : déposer le fichier suffit. `.jpg`, `.png`, `.jpeg` et `.webp`
sont cherchés dans cet ordre. Pour mettre une image ailleurs ou sous un autre
nom, `ships/libre.json` a un champ `image`, qui part de la racine du dépôt.

## Le format

**16:9**, comme une capture d'écran — parce que c'en est une. `512 × 288` suffit
largement ; plus grand ne gêne pas, l'image est ramenée à la carte en la
**couvrant**, donc rien ne se déforme et ce qui dépasse est rogné à gauche et à
droite. Un format plus carré perdrait du haut et du bas : cadrez la coque au
centre.

Tant qu'un visuel manque, la carte est peinte par le code — ciel sur mer, et le
nom dessous. Rien n'est cassé, et le jour où l'image arrive elle prend sa place
sans qu'on touche au code.

## Ce qui n'est pas ici

**Ces images ne partent pas dans la page publiée.** Elles ne sont citées par
aucune fiche, donc `build.js` ne les embarque pas, et elles ne coûtent rien aux
16 Mo. Le menu qui les emploie est celui de Godot.
