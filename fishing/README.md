# La pêche à la ligne à main (Godot)

`fishing/poissons.json` : les espèces, où elles tiennent, ce qu'elles pèsent et
ce qu'elles valent ; et les règles de la ligne. Lu par
`core/NavalSim.Core/Fishing.cs` (règles, testables au banc) et
`godot/scripts/ShipDemo.Fishing.cs` (le geste, la cale, la plaque du HUD).

## Le geste

- **Espace**, navire en panne ou au mouillage (sous `vitesseMax` m/s) : les
  lignes descendent, une pour deux hommes (`equipage` de la fiche du navire,
  sinon estimé sur le tonnage ; au plus `lignesMax`). La sonde dit la
  profondeur, et ce que le suif du plomb a rapporté : *du sable colle au suif*
  (fond plat), *le suif remonte propre et marqué : de la roche*, *le plomb a
  glissé : ça tombe à pic* (tombant). Plus profond que `longueur` : pas de fond,
  on ne pêche pas.
- **« ÇA MORD ! »**, en grand au milieu de l'écran avec le temps qui reste :
  Espace dans les `ferrer` secondes (3), sinon le poisson mange l'appât. Ferré, on le hale à `remontee` m/s depuis le fond, plus un peu pour
  un gros poisson ; il peut encore casser la ligne (`casse` × son poids sur le
  poids maximal de l'espèce). Puis `appat` secondes pour réappâter.
- **Espace sans touche** : on relève les lignes. Elles se relèvent seules si le
  navire prend de l'erre.

## Où et quand

On pêche **partout et à toute heure** : un mauvais fond donne encore le tiers
des touches d'un bon, la nuit la moitié de l'appétit du jour. Rien n'est marqué sur la carte. Le relief ne dit pas la nature du fond, mais
sa **pente** la trahit : la roche est un fond qui monte et descend sur quelques
mètres, un tombant une pente forte, le sable un fond plat. Chaque espèce a une
fenêtre de profondeur (adoucie aux bords) et un fond préféré ; les touches par
heure et par ligne valent `touchesParHeure` au meilleur endroit, multipliées
par l'appétit de l'heure (`activite` : `aube`, `jour`, `crepuscule`, `nuit`,
1,5 / 0,7 / 1,5 / 0,5 par défaut).

Le banc dessine la carte des bons fonds autour du port et joue une sortie :

```bash
dotnet run --project core/NavalSim.Lab -c Release -- peche 3 6.5
```

(rayon en km, heure). Relevé : une demi-heure de jeu sur le meilleur fond du
mérou, à 2,4 km du port, rapporte environ 150 kg et 25 écus ; sur celui du
vivaneau, à 1,4 km, environ 70 kg et 16 écus.

## La fraîcheur

En **temps de jeu** (celui où le navire fait route), pas en heures du
calendrier, qui défile bien plus vite. Plein prix pendant `frais` minutes, puis
de moins en moins, et à `gate` minutes le poisson passe par-dessus bord. La
plaque au-dessus de la bourse dit ce qui reste.

Le poisson pèse dans la cale (nature `poisson`) ; c'est la liste des prises
qui dit l'espèce et l'heure de chacune. Elle se sauvegarde, passe les
traversées, et suit la cale quand on change de bord au chantier.

## Sur le pont

Chaque prise tombe à bord — le modèle que nomme sa fiche (`modele`, avec `dos` :
l'axe vers lequel il a le dos, `+y` s'il est modelé debout, `+z` couché sur le
flanc ; une espèce sans modèle prend celui du mérou teinté de rouge) : hissée par-dessus le pavois du bord où
pendait la ligne, elle rebondit, se débat dix à trente secondes, puis reste
couchée sur le flanc. Elle glisse quand le navire gîte ou tangue — la pesanteur
est tournée dans le repère du bord, son accélération retranchée — et les
poissons s'empilent. Sa taille suit son poids (un mérou de dix kilos fait
quatre-vingts centimètres).

Son **poids est là où elle gît** : un colis au niveau du pont, dans le
compartiment et du bord où elle est. C'est juste, donc c'est peu : cent
cinquante kilos sur les vingt-deux tonnes du sloop l'enfoncent de quelques
millimètres. Au-delà de quatre-vingts poissons, les suivants pèsent au fond
de la cale sans être dessinés.

## La vente

Au comptoir, à quai : la section **Poisson frais**, une ligne par espèce, au
`prixKg` de l'espèce multiplié par la fraîcheur de chaque prise.
