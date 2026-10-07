# Les navires du jeu — ce qu'ils ont le droit et le pouvoir de faire

Tout navire que le joueur ne commande pas, dans la version Godot. État au 4 octobre 2026,
lu dans le code. Les chiffres viennent du code ou de `settings.json` ; ce qui est
incertain est dit comme tel, en fin de note.

## Ce qu'ils ont tous en commun

**Ce sont de vrais navires.** Chacun a son solveur, la même physique que le vôtre :
- il flotte et dérive ;
- il remonte au vent comme son gréement le permet ;
- il s'échoue, et le fond dur l'ouvre (corail des récifs, écueils, dès un nœud et demi) ;
- il prend l'eau et ses pompes travaillent ; le charpentier bouche une brèche de 0,12 m²
  au plus toutes les 40 s ;
- il heurte les autres coques et les pontons ;
- sa toile se déchire, son mât casse ;
- son équipage combat le feu ;
- sa soute peut sauter.

Rien ne le pousse à la main. Seules exceptions : le vaisseau qui rôde, et un navire de rade
tenu à quai par ses amarres.

**Ils sont au plus quinze avec vous** (`Config.MaxShips` = 16).
- Le trafic de rade laisse toujours quatre places libres aux rencontres et aux pirates.
- La touche U, le vaisseau qui rôde et une partie rechargée ne regardent pas ce plafond :
  au-delà, la mer ne dessine plus la coque autour d'eux, mais elle flotte.
- Les navires au mouillage des ports ne comptent pas (voir plus bas).

**Pas de simplification au loin.** Un navire à huit kilomètres est simulé comme à cent
mètres. Seuls ses fanaux s'atténuent au-delà de 1 500 m.

**Leur pavillon** est tiré au sort parmi les nations, sauf les fiches à pavillon noir.
C'est le pavillon qui décide des camps : on ne tire jamais sur les siens, et un coup
fratricide ne fait pas d'ennemi.

## Comment ils barrent

Tous, sauf le vaisseau qui rôde, barrent par la **barre automatique** (`core/NavalSim.Core/AutoHelm.cs`).
Elle écrit dans les mêmes commandes que votre main : gouvernail, écoute, toile. Elle ne
triche pas.

- **Au près**, elle ne pointe pas dans le vent. Elle tire des bordées à 50° du vent pour un
  gréement carré, 47° pour un aurique, de cinq minutes au moins.
- **Elle vire lof pour lof** : elle abat et fait le tour par le vent arrière. Seul le pilote de
  rade vire vent devant (aurique), car une rade n'a pas la place d'abattre.
- **Autour d'une cible**, elle vise un cercle (sa « garde ») et non le centre. Une
  poursuite devient une ronde autour de la proie, à 1,6 longueur par défaut, 185 m pour un
  pirate, 120 m en escarmouche.
- **Ses écoutes** suivent l'optimum que le solveur calcule pour la marque verte de la
  console.
- **Sa barre se règle sur l'erre** (nouveau). Au-delà de cinq nœuds, elle donne moins de barre,
  comme l'inverse du carré de la vitesse. Le gain de vent du jeu fait courir un sloop à
  quatorze nœuds, et la barre réglée pour cinq surcorrigeait. C'était le sillage en
  serpent du sloop de Port-Royal : 15,6 °/s de lacet moyen avant, 2,8 après.
- **Elle sonde** (nouveau). Toutes les demi-secondes, six coups de sonde le long du cap voulu,
  jusqu'à quatre longueurs ou trente secondes de route. Il faut le tirant d'eau plus un mètre,
  et un écueil compte par son sommet.
  - Au près, un danger sur son bord la fait changer de bord.
  - Sinon, elle prend le cap libre le plus proche du sien, en commençant du côté de sa marque.
    Elle le tient huit secondes, puis revient à sa marque quand la route est libre.
  - Tout est barré devant : demi-tour.
  - Au banc, un sloop qui s'empalait sur Lime Cay le contourne désormais d'une seule dérobade.
- **Les machines** (barge, cotre, vedette) vont droit à leur marque à la machine, sans se
  soucier du vent ; elles sondent aussi.

**Ce que la barre ne sait pas faire :**
- prendre un ris ou ferler par gros temps : toute la toile reste dehors ;
- éviter une autre coque ;
- mouiller ;
- reprendre une route une fois son ennemi coulé ;
- au près, rallier vite une marque au vent : défaut ancien, une marque à 1 200 m dans le vent
  n'est pas atteinte en vingt-cinq minutes.

## Par sorte de navire

### Le marchand rencontré au large

- **Quand il paraît** :
  - une voile toutes les 55 à 720 secondes, deux à la fois au plus ;
  - jamais quand vous êtes au port, ni à moins de 2 500 m d'une côte ;
  - à 4 000–5 000 m de vous, à 1 500 m au moins de toute terre.
- **Une rencontre sur quatre est un pirate.** Une sur six environ (18 %) est une paire aux prises :
  un pirate posé par le travers d'un marchand, à 160 m.
- **Ses fiches** : Boussole, frégate, Roebuck, Roter Löwe, sloop. La liste `encounters.exclude`
  écarte la barge, le cotre, la goélette, la vedette, la chaloupe et les objets flottants.
- **Sa route** : un port tiré au sort dont la route est dégagée, sinon un point à 15 km devant lui.
- **Il se méfie** si vous avez amené vos couleurs (⇧P) et êtes à moins de 1 200 m : il fuit
  droit à l'opposé.
- **Il riposte** si vous touchez sa coque ou sa mâture. Un trou dans une voile ne le fâche pas.
  - Il vient alors sur vous et vous canonne.
  - Il n'abandonne jamais, même sans canons, tant que l'un de vous flotte.
- **Il disparaît** au-delà de 9 000 m, ou coulé à plus de 3 000 m.
- **La vigie** l'annonce à 600 m ; la lunette jusqu'à 7 000 m.

### Le pirate (pavillon noir)

Son cerveau est `core/NavalSim.Core/Pirate.cs`.

- **La chasse** :
  - Sa proie est la coque non pirate la plus proche dans 4 000 m, vous compris, sauf celle qu'il a
    pillée dans le dernier quart d'heure. Un autre pavillon noir n'est jamais une proie.
  - Il garde sa proie jusqu'à ce qu'elle coule.
  - Sans proie, il tourne autour de vous sans tirer.
- **Il passe à l'abordage** dès que sa proie est abîmée : plus de 6 % d'eau embarquée, mâture
  touchée, un quart de ses pièces démontées, ou quatre brèches.
  - Il cesse le feu, vient dans son sillage puis à couple, et lance quatre grappins à moins de
    26 m, une volée toutes les 6 secondes.
  - Les filins halent. Vous seul pouvez les trancher (⇧D) : un navire mené par le jeu ne coupe pas.
  - Il largue ses crochets quand il quitte l'abordage ; un filin tenu à une coque qui sombre est tranché.
  - Cinq secondes tenu à couple, il pille.
- **Le pillage** :
  - Sur vous : la moitié de votre bourse et toute votre cargaison.
  - Sur un autre navire : rien n'est transféré, un message seulement.
  - Si vous lui aviez tiré dessus, votre soute saute : la partie est finie.
- **La fuite** : trois minutes vers un point à 3 000 m à l'opposé ; il ignore sa proie un quart
  d'heure.
- **Canonné en chasse**, il prend le tireur pour proie.
- **Pavillon amené** : il vous ignore, sauf s'il vous chassait déjà.
- **Il ne disparaît seul** que s'il est né d'une rencontre. Né de la touche U, du panneau Flotte
  ou d'une partie rechargée, il reste.

### Les navires de la rade (horaires)

Les règles sont dans `world/horaires/caraibes.json` et `world/README.md`.

- **Leurs mouvements** :
  - Départs et arrivées à heure fixe, aux postes numérotés des pontons de Port-Royal.
  - Six à la fois au plus, ceux à quai compris.
  - Au-delà de force 5, ni départ ni arrivée.
- **Ils paraissent toujours hors de votre vue** : loin, ou dans votre dos. Sinon ils attendent,
  et un mouvement est manqué au bout d'une heure du ciel.
- **Leur pilote** (`HarbourPilot.cs`) suit une route tracée dans l'eau, qui contourne la terre et
  les récifs. Il tire des bordées d'une minute et vire vent devant s'il est aurique. Au près, il
  sonde devant l'étrave.
- **À quai**, ils sont tenus par leurs amarres, voiles ferlées. Rendus, ils s'amarrent devant leur
  poste, font deux heures d'escale, puis s'en vont quand vous ne les regardez plus.
- **La barge pour Carthagène** continue droit au-delà du large, en sondant, et s'efface passé
  l'horizon.

### L'escarmouche

- **Les forces** : douze coques en tout, cinq de votre côté et six en face, sous deux nations
  tirées au sort. Force 5, vent du 75°.
- **Les fiches** : Roter Löwe, frégate, pirate, goélette, cotre. Une fiche sans canons est
  écartée ; le pirate s'y bat pour son camp.
- **Le choix de l'ennemi** : l'adversaire que le moins de coques visent déjà, le plus proche à
  égalité. Il est gardé tant qu'il flotte à moins de 1 200 m.
- **Ce qui est coupé** pendant l'escarmouche : rencontres, trafic, fantômes, monstres, méfiance.
  La foudre et le feu restent.

### La flotte fantôme (Cimetière des Galions)

- **Quand elle se lève** : de nuit, après 90 secondes dans le cercle. Deux Roter Löwe contre deux
  pirates, une bataille par nuit, dans les Caraïbes seulement.
- **Les coques** glissent un peu au-dessus de l'eau et ne heurtent qu'entre elles.
- **Le combat** : elles se battent entre elles. Elles ne vous touchent que si vous les provoquez,
  ou si vous venez à moins de 350 m. Vos boulets ne les touchent pas.
- **Elles s'en vont** à l'aube, quand vous vous éloignez, ou quand un camp a coulé.

### Le vaisseau qui rôde

- **Quand il paraît** : la nuit, par la brume, au large, 0,8 fois par heure de jeu. Il surgit à
  900 m sur l'avant.
- **Ce n'est pas un navire mené par le jeu** : sa position est écrite à chaque image, à cinq
  nœuds environ.
- **Il ne vient vers vous que si vos feux sont allumés.** Il rôde trois minutes et demie, puis
  s'efface.
- **Il ne tire pas** et ne heurte rien.

### Ce qui n'est pas un navire du jeu

- **Les navires au mouillage des ports**, et ceux posés à la main en mode création, sont du décor.
  Ils n'ont ni solveur, ni barre, ni canons. On ne peut pas les toucher : ni au canon, ni en les
  abordant. Ils suivent la houle, et c'est tout.
- **La chaloupe** est menée par vous. Pendant ce temps, votre navire reste au mouillage, voiles
  ferlées.

## Le combat

- **Qui tire** : un navire qui a un ennemi.
  - le pirate en chasse ;
  - le marchand provoqué ;
  - l'adversaire désigné en escarmouche ;
  - un spectre contre le sien.
- **Quand il tire** :
  - entre 12 et 340 m ;
  - la cible franchement par le travers ;
  - les pièces de chasse ne sont jamais servies ;
  - il faut au moins 60 % du bord chargé ;
  - une bordée toutes les 2 à 5 s, chaque pièce attendant son roulis.
- **Son pointage** : aucune visée ni anticipation. L'angle vient de la hauteur de la bouche ; la
  houle fait le reste.
- **Rechargement** : 30 à 60 s par pièce ; 40 charges de poudre par pièce.
- **Les dégâts reçus** sont les vôtres : brèches, pièces démontées, mâts blessés, voiles trouées
  (6 % de poussée par trou, la voile cède au septième), incendie.
- **Ce qu'aucun d'eux ne sait faire** : se rendre, amener ses couleurs, fuir un combat perdu.
- **Coulé**, un navire devient une épave inscrite au registre, avec son coffre. Celui d'un
  pirate est plus riche.

## Les monstres

- **Le kraken** peut prendre pour victime n'importe quelle coque dans une dépression, pas
  seulement la vôtre.
- **La foudre** frappe toute coque prise dans une dépression.
- **La baleine et le serpent de mer** n'en veulent qu'à vous.

## Réglages

Dans `settings.json` :
- `encounters` : fréquence, distances, part de pirates, fiches exclues ;
- `trafic` : six navires, sloop/sloop/goélette, force 5, 5 000 m ;
- `ghosts` et `wraith` ;
- `gunnery` : rechargement, voiles trouées ;
- `fire` : incendie et soute ;
- `wind.gain` : 8, le gain de vent qui fait courir tout le monde.

Les chiffres des pirates et de l'escarmouche sont dans le code (`Pirate.cs`,
`ShipDemo.Pirates.cs`, `ShipDemo.Skirmish.cs`).

## Corrigé le 4 octobre

- La barre se règle sur l'erre et sonde (`Config.HelmBySpeed`, `Config.HelmSounds`, allumés par
  Godot).
- Les navires nés après le chargement ne connaissaient pas les pontons et passaient au travers,
  ceux de la rade compris : ils les reçoivent désormais à leur mise à l'eau (`SpawnFleet`).
- Les filins de grappin se lâchent : le pirate largue ses crochets dès qu'il quitte
  l'abordage (pillage fait, fuite) ; un filin dont un bout est sur une coque coulée ou retirée
  est tranché.

## À vérifier (relevé dans le code, pas encore éprouvé en jeu)

1. Le vaisseau qui rôde reste peut-être une cible pour les boulets, malgré son commentaire.
2. La méfiance (pavillon amené à moins de 1 200 m) passe avant tout le reste. Elle pourrait faire
   appareiller un navire de rade amarré, ou votre navire au mouillage pendant la chaloupe.
3. Après une victoire, ou après une partie rechargée, un navire sans but garde ses dernières
   commandes et ne disparaît plus de lui-même.
4. Quelles fiches portent des canons dépend des modèles 3D : cela n'a pas été relevé fiche par
   fiche.
