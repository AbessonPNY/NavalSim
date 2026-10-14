# Se repérer comme en 1690 : l'estime, le soleil, la Polaire et le vrai ciel

Ce document raconte comment le jeu permet de faire le point, et comment c'est fait.
Il s'adresse au joueur curieux autant qu'à qui reprendra le code.

## Ce qu'un pilote savait faire en 1690

| ce qu'il cherche | comment | précision | dans le jeu |
|---|---|---|---|
| sa **position au jour le jour** | **l'estime** : à chaque sablier (une demi-heure), il file le loch (la vitesse) et lit le compas (le cap), et porte la route sur la carte | elle dérive : quelques % de la distance courue | oui, depuis longtemps |
| sa **latitude, le jour** | la **hauteur du soleil à midi** au **quartier de Davis** (1594), corrigée de la déclinaison du jour lue dans les tables | quelques minutes d'arc | oui, instrument en main |
| sa **latitude, la nuit** | la **hauteur de la Polaire** à l'**arbalestrille**, corrigée de la position des « Gardes » | une dizaine de minutes d'arc | oui, instrument en main |
| sa **longitude** | **impossible en mer** : il faut une montre marine (Harrison, années 1760) ou les distances lunaires (même époque) | — | non, et c'est voulu : on la tient à l'estime |

Le **sextant** (1757) et l'**octant** de Hadley (1731) viennent après l'époque du jeu.
Le jeu a déjà son anachronisme assumé (le dériveur ILCA 4) ; on n'en ajoute pas un second à la navigation.

## Comment prendre une hauteur

Ouvrez la **carte (Ctrl I)**. Le bouton **« ☼ ☆ Prendre la hauteur »** s'allume :
- **autour de midi** (11 h – 13 h), par temps clair, une fois par jour : le **soleil** ;
- **la nuit**, ciel dégagé, une fois par nuit : la **Polaire**.

L'œil se place au bord du navire, du côté où l'on vise, là où rien ne cache l'horizon.

### Le quartier de Davis, à midi

On **tourne le dos au soleil**. Par la **fente** du viseur d'horizon, on voit la mer.
Sur la plaque tombe l'**ombre** du viseur de soleil : une bande de lumière dorée, floue,
large comme le disque du soleil (un demi-degré). On fait glisser le viseur — **l'arc** —
jusqu'à ce que l'ombre se pose **sur l'horizon**.

La mer roule : six dixièmes du mouvement du pont passent dans la visée, et la main
tremble d'autant plus que la mer est grosse. L'horizon et l'ombre bougent ensemble ;
c'est l'œil qui juge quand ils se rejoignent.

Il faut prendre le soleil **quand il culmine** — l'indicateur dit « il monte encore »,
« il culmine », « il redescend ». Près de midi il bouge à peine, mais pas assez peu : pris
à 11 h 40, il est encore bas d'un demi-degré, et la latitude est fausse de **30′**
(mesuré au banc « astres »).

### L'arbalestrille, la nuit

Un bâton (le marteau) tenu en travers de la visée : on met **son bout bas sur
l'horizon** et **son bout haut sur la Polaire**. La visée part à mi-chemin des deux,
comme on tenait l'instrument. Moins précis : on le tient à bout de bras, de nuit.

### Les commandes

| | |
|---|---|
| molette | l'arc, de 10′ (avec ⇧ : de 1′) |
| ↑ ↓ | l'arc, d'une minute |
| bouton de souris tenu, de haut en bas | viser |
| Entrée | lire |
| Échap | ranger l'instrument |

### Le calcul, posé sous vos yeux

**Au soleil** : la hauteur lue (h), la déclinaison du jour (δ, la table), la
distance au zénith (90° − h), et la latitude :

- le soleil passe **au sud** (le cas de la Jamaïque presque toute l'année) :
  latitude = (90° − h) + δ ;
- il passe **au nord** (en été, quand la déclinaison dépasse la latitude) :
  latitude = δ − (90° − h).

Exemple, le 8 octobre 1690 à Port-Royal : δ = −6° 28′ (sud), h = 65° 35′ →
latitude = 24° 25′ − 6° 28′ ≈ 17° 56′ N (les minutes sont arrondies).

**À la Polaire** : sa hauteur, plus la **correction des Gardes**. En 1690 la Polaire
n'est pas au pôle : elle en est à **2° 22′** (contre 0° 44′ aujourd'hui) et tourne
autour en un jour sidéral. Au-dessus du pôle elle est trop haute de 2° 22′, en dessous
trop basse d'autant, à l'est ou à l'ouest juste. Les pilotes lisaient cette correction
dans un tableau, d'après la position de deux étoiles de la Petite Ourse (les Gardes).
Le jeu fait comme eux : il prend la place de la Garde la plus proche parmi les huit de la
table (voir « Le livre du pilote ») et lit la correction de cette place. La correction
exacte, −2° 22′ × cos(angle horaire), ne sert qu'à dresser la table.

La latitude trouvée est **portée sur la carte** : votre position estimée glisse vers le
nord ou le sud, avec l'erreur de **votre** main (plus un rien d'instrument). Le pilote
se croit à 4′ près au quartier, à 12′ près à l'arbalestrille — et il a tort la nuit,
puisque la table à huit places le trompe parfois de bien plus. La longitude, elle, ne
bouge pas : l'estime la garde.

## Le vrai ciel

Les étoiles du jeu sont les **vraies**, à leur place **de 1690**, et le ciel **tourne**.

### D'où viennent les étoiles

Du **Yale Bright Star Catalogue** (BSC5, Hoffleit & Warren, 1991), du domaine public,
publié par le CDS de Strasbourg. L'outil `tools/stars.js` en tire `world/etoiles.json` :
les **2 887 étoiles jusqu'à la magnitude 5,5**, chacune avec sa position de l'an 2000,
son mouvement propre, son éclat et sa couleur, et son nom (la lettre de Bayer et la
constellation, plus le nom usuel en français pour une cinquantaine : la Polaire,
Sirius, Véga, l'Épi, Antarès, Dubhe et Mérak…).

### Les ramener en 1690

Le ciel n'est pas immobile sur trois siècles :
- **le mouvement propre** : chaque étoile dérive un peu ; Arcturus d'une douzaine de
  minutes d'arc depuis 1690 ;
- **la précession des équinoxes** : l'axe de la Terre tourne comme une toupie, en
  26 000 ans. C'est elle qui éloigne la Polaire du pôle quand on remonte le temps.
  Le jeu l'applique avec les angles de l'Union astronomique internationale (1976).

Vérification au banc d'essai (`dotnet run --project core/NavalSim.Lab -c Release -- astres`) :
la Polaire est à 0,74° du pôle en 2000, 2,31° en 1700, 2,36° en 1690, 2,86° en 1600.
Le banc la vise aussi d'heure en heure une nuit à Port-Royal : sa hauteur va de 18° 44′
à 20° 16′, la correction des Gardes de −0° 49′ à −2° 20′, et la latitude trouvée est
chaque fois 17° 56′, la vraie.

### Le faire tourner

À chaque image, le jeu calcule le **temps sidéral** du lieu : l'heure des étoiles, qui
avance de 3 minutes 56 secondes par jour sur celle du soleil. Avec la latitude du navire,
il oriente la sphère céleste : le pôle céleste est au nord, à une hauteur égale à la
latitude ; tout le ciel tourne autour de lui de 15° par heure.

On voit donc ce qu'un marin voyait :
- la **Polaire** vers 19° ou 20° au-dessus de l'horizon nord, presque immobile ;
- les autres étoiles qui se lèvent à l'est et se couchent à l'ouest ;
- un ciel d'**octobre** qui n'est pas celui d'**avril** : un soir d'octobre à la
  Jamaïque, la Grande Ourse est passée sous l'horizon nord, Cassiopée est haute au
  nord, Véga à 25° à l'ouest-nord-ouest ; au printemps, la Croix du Sud monte à une
  dizaine de degrés au-dessus de l'horizon sud, dans la brume de l'horizon.

Pour trouver la Polaire : prolongez de cinq fois leur écart les deux étoiles du bout de
la Grande Ourse (Mérak puis Dubhe) quand elle est levée ; sinon, partez du « W » de
Cassiopée, de l'autre côté du pôle.

### Comment c'est dessiné

Les étoiles sont **peintes une fois** sur une grande image (4096 × 2048) en coordonnées
célestes — l'ascension droite en largeur, la déclinaison en hauteur —, chacune en petite
tache à son éclat (une magnitude, c'est 2,5 fois moins de lumière) et à sa couleur
(bleutée pour Rigel, orangée pour Bételgeuse). Le dôme du ciel, pour chaque pixel,
convertit la direction regardée en ascension droite et déclinaison, d'après le pôle, le
méridien et le temps sidéral, et lit l'image. Le ciel tourne sans rien repeindre : une
lecture de texture par pixel. Les étoiles s'éteignent dans la tempête.

**Le soleil suit le même ciel.** En écrivant ce document, on s'est aperçu que le soleil
*dessiné* gardait une latitude et une saison fixes (13,6° et une fin de printemps) :
en octobre, il passait 18° trop haut sur Port-Royal, à l'écart des étoiles et de ce que
lisait le quartier. Il suit désormais le lieu du navire et la date du calendrier : à
l'automne, midi est plus bas et les ombres plus longues.

## Les instruments, montrés en main

Pendant qu'on vise, l'instrument lui-même se montre dans le coin bas droit : ce que
la main règle, à côté de ce que l'œil voit.

**Le quartier de Davis** (John Davis, *The Seaman's Secrets*, 1594-1595) : un bâton
et deux arcs sur un même centre. Au centre, la **pinnule d'horizon**, percée d'une
fente. Sur le **petit arc** (60°, court rayon), la **pinnule d'ombre**, posée sur un
degré rond ; sur le **grand arc** (30°, long rayon), la **pinnule de visée**, où l'on
colle l'œil et qui glisse. **La hauteur est la somme des deux lectures.** Pourquoi
deux arcs ? Un petit arc ne se lit pas finement, mais on y pose sans peine une pinnule
sur un trait rond ; le grand, lui, se lit à la minute. Dans le jeu, la pinnule d'ombre
est prérèglée de dix à vingt degrés sous la hauteur attendue ; quand la pinnule de
visée bute au bout des 30°, le pilote la recale de dix degrés. Par le trou de la
pinnule de visée, on ne voit qu'un disque : la fente au milieu, le bois autour.

Deux pointillés montrent ce que l'instrument fait : le rayon du soleil qui passe par la
pinnule d'ombre et vient au centre, et celui de l'œil qui passe par la fente vers
l'horizon. L'angle entre les deux est la hauteur.

**L'arbalestrille** (la *cross-staff*) : un bâton carré qu'on porte à la joue, et le
**marteau** qui court dessus. On le pousse jusqu'à ce que ses deux bouts couvrent
l'horizon et l'étoile. Le bâton porte ses degrés là où tan(angle ÷ 2) = demi-marteau ÷
distance à l'œil : les graduations sont larges au bout et **se serrent vers l'œil**,
où sont les grands angles. Les vraies avaient plusieurs marteaux pour plusieurs
portées ; le jeu n'en garde qu'un (10° à 60°), qui suffit à la Polaire des Antilles.

**Des images à la place du dessin.** `instruments/instruments.json` dit où est tout,
en pixels d'une image : le centre et les rayons des arcs du quartier, les deux bouts
de l'arbalestrille. Tant que l'« image » est vide, le jeu dessine l'instrument ; une
image PNG le remplace, et le jeu pose par-dessus les pinnules et le marteau aux places
que la fiche dit. Les gabarits à peindre se tirent de la même fiche
(`node tools/instrument-templates.js` → `instruments/gabarits/`) : on peint sur les
traits, on change un rayon, on retire le gabarit. `quartier.vue` est la pinnule
d'horizon vue par le trou, sa fente au milieu de l'image.

## Le livre du pilote

Après une hauteur, le calcul ne s'affiche plus en lettres flottantes : il s'écrit à la page
de droite d'un livre ouvert, et la page de gauche montre la table qui a servi. On l'ouvre
aussi depuis la carte (« Livre du pilote »), et ← → passe d'une table à l'autre.

- **Le régiment du soleil** : la déclinaison du mois en cours, jour par jour, le jour de la
  partie marqué. Ce sont les nombres mêmes que le jeu emploie (`Calendar.DeclinationOn`) :
  lire la table et faire le calcul soi-même donne la latitude que le jeu donne.
- **La règle de l'étoile du Nord** : une roue avec la Polaire décalée du pôle, et la Garde
  (Kochab, β de la Petite Ourse) aux huit places que nommaient les régiments ibériques —
  *à la tête, au bras d'ouest, à la ligne d'ouest, sous le bras d'ouest, aux pieds…* Pour
  chacune, ce qu'il faut ajouter ou ôter à la hauteur de la Polaire ; et, en rouge, où est
  la Garde à cette heure. Les régiments dessinaient un homme au milieu de la roue ; le jeu
  ne garde que les mots (aucun humain à l'écran).

En 1690 la correction va jusqu'à 2° 19′ (« au bras d'est », « sous le bras d'ouest »), et non
à la tête ni aux pieds : la Garde et la Polaire ne sont pas exactement à l'opposé l'une de
l'autre autour du pôle (185° d'ascension droite d'écart, pas 180°).

**La table à huit places est simulée.** On lit la correction de la place la plus proche,
pas celle de l'heure exacte. Quand la Garde est tout juste sur une place, la latitude est
juste ; entre deux places, elle est fausse, jusqu'à près d'un degré là où la correction
varie le plus vite. Au banc, à Port-Royal le 8 octobre 1690, l'instrument réglé juste :
20 h, 11,8′ d'erreur ; 21 h, 37,4′ ; 23 h, 3′ (la Garde presque sur une place). Le soleil reste le meilleur moyen de faire le point.

## Le nocturlabe : l'heure la nuit

La nuit, un navire de 1690 n'a pour montre que le sablier qu'on retourne. Le **nocturlabe**
lit l'heure dans le ciel. Il a trois pièces sur un pivot creux :
1. la **plaque**, tenue par son **manche en bas**, avec l'anneau des dates ;
2. le **disque des heures**, qu'on tourne pour mettre sa **dent de minuit** sur la date du jour
   (24 dents : on compte les heures au toucher quand il fait trop noir pour lire) ;
3. le **bras**, l'alidade.

On vise la **Polaire par le trou du pivot**, on amène le bras sur la **Garde**, et l'heure
se lit où le bras croise le disque. La Petite Ourse tourne autour du pôle en un jour sidéral ;
à la même heure, elle a avancé d'un degré par jour, et l'anneau des dates rattrape ce degré.

Dans le jeu : carte (Ctrl I), « Nocturlabe », la nuit par ciel dégagé. Bouton tenu, le bras
suit la main ; molette : un degré ; Entrée : lire. On compare l'heure lue à celle du sablier.
L'instrument a l'erreur du vrai : il pivote sur la Polaire et non sur le pôle, et ses
heures sont de 15° quand le ciel en fait 15,04. Au banc, le bras posé sur la Garde à 21 h
le 8 octobre donne **4 minutes** d'écart.

En octobre à la Jamaïque, la Garde descend vers l'horizon au fil de la nuit (elle frôle les
4° vers minuit) : on la prend mieux le soir.

**L'anneau des dates vient du ciel.** Son 1er janvier est là où est la Garde au minuit du
1er janvier 1690 (`janvier` dans la fiche, 32°). Le jeu le recalcule au chargement
(`Sights.MidnightHourAngle`) et prévient si la fiche s'en écarte de plus d'un degré.

Plaque, disque et bras ont chacun leur image facultative et leur gabarit, comme le quartier
(`instruments/instruments.json`, `instruments/gabarits/`). Le livre aussi : une image du
livre ouvert, et les deux rectangles où le jeu écrit.

## Ce qui est simplifié, et pourquoi

- **Le monde est plat** : il n'y a pas de dépression de l'horizon (les 4′ qu'un pilote
  retranche quand son œil est à cinq mètres au-dessus de l'eau). Rien à corriger, donc
  rien n'est corrigé.
- **La déclinaison du soleil** vient d'une formule simple du calendrier (à un degré près),
  la même pour le soleil dessiné et pour la table du pilote : ce qu'on mesure et ce
  qu'on calcule restent d'accord.
- **Le calendrier est grégorien.** Un Anglais de 1690 datait au julien, dix jours de
  retard : son « 28 septembre » est notre 8 octobre. Le jeu ne fait pas la différence.
- **Les planètes** (Vénus, Jupiter, si brillantes) ne sont pas dans le ciel.
- **L'équation du temps** est ignorée : midi est le midi solaire, et la montre du jeu le
  donne (le seul temps qu'un navire de 1690 connaissait, le sablier recalé à midi).

## Où est le code

| fichier | rôle |
|---|---|
| `core/NavalSim.Core/Sights.cs` | les formules : hauteur du soleil, latitude, précession, angle horaire, correction des Gardes |
| `core/NavalSim.Core/Stars.cs` | le catalogue, porté à l'année de la partie |
| `core/NavalSim.Core/Reckoning.cs` | l'estime, et la latitude prise à l'instrument (`LatitudeSight`) |
| `godot/scripts/ShipDemo.Sight.cs` | l'instrument : la vue, les commandes, la lecture, le calcul affiché |
| `godot/scripts/SightPlate.cs` | ce qu'on voit à l'instrument : la fente, l'ombre, le marteau |
| `godot/scripts/SightInstrument.cs` · `instruments/instruments.json` · `tools/instrument-templates.js` | l'instrument en main (dessiné ou en image), la fiche de sa géométrie, ses gabarits |
| `godot/scripts/ShipDemo.Nocturnal.cs` · `NocturnalPlate.cs` | le nocturlabe : la vue, le bras, la lecture |
| `godot/scripts/ShipDemo.PilotBook.cs` · `PilotBook.cs` | le livre du pilote : régiment du soleil, règle de l'étoile du Nord, dernier calcul |
| `godot/scripts/StarMap.cs` · `ShipDemo.Stars.cs` | la carte du ciel peinte, et la sphère céleste tournée chaque image (le soleil aussi, au lieu et à la date) |
| `godot/shaders/sky.gdshaderinc` (`NAVAL_STAR_MAP`) · `sky_dome.gdshader` | le dôme qui lit la carte du ciel |
| `tools/stars.js` · `world/etoiles.json` | du catalogue BSC5 au fichier du jeu |

Essais sans clavier : `--heure 12 --hauteur auto` (le soleil), `--heure 22 --hauteur auto`
(la Polaire) — l'instrument se règle seul et lit, la console dit l'erreur.
Le nocturlabe : `--heure 21 --nocturlabe auto` (le bras posé sur la Garde, la console dit
l'écart au sablier) ; le livre : `--livre 0` (le régiment), `--livre 1` (la règle des Gardes).
