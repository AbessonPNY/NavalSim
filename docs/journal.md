# Journal de conception — NavalSim

Simulation 3D d'un navire à voile : flottaison d'Archimède réelle sur une houle
de Gerstner, gouvernail, gréement. Aucune dépendance npm — Node et un navigateur
suffisent. three.js vient d'un CDN.

## Démarrer

```bash
node build.js          # produit dist/naval-sim.html (fichier autonome publiable)
```

Le serveur de dev est déclaré dans `.claude/launch.json` (`node .claude/serve.js`,
port 8765) : demandez-moi de le lancer, ou `node .claude/serve.js`.

Artifact publié : https://claude.ai/code/artifact/e9837c06-9839-4888-a33a-bd03cdcd233c
Pour le mettre à jour depuis une nouvelle session, il faut passer cette URL
explicitement, sinon un second artifact est créé.

## Architecture

`naval-sim.html` ne contient que le balisage et un `main()` de câblage. Toute la
logique est dans `js/`, en classes attachées à un espace de noms global `Naval`
(pas de modules ES : le build inline les fichiers par simple concaténation).

| module | rôle |
|---|---|
| `config.js` | constantes du monde (ρ, g, grille de sondes, liste de repli des navires) |
| `weather.js` · `storms.js` | le vent qui se conduit seul · les dépressions, qui ont un lieu |
| `rain.js` · `splash.js` | le rideau de pluie · l’eau jetée par ce qui tombe dedans |
| `wreck-air.js` | l’air qui remonte d’une épave, et le temps qu’il met à venir |
| `spyglass.js` | la lunette : une focale étroite, puis un verre qui a vu la mer |
| `bloom.js` | ce qui déborde d'une lumière trop vive pour l'image, la nuit seulement |
| `anchor.js` | mouiller et lever l’ancre : la chute, le câble, le cabestan |
| `flotsam.js` | ce qui remonte d’un naufrage, la bouteille, la cargaison échouée |
| `ship-spec.js` | lit une fiche JSON et en **dérive** tout ce que le solveur consomme |
| `hull-lines.js` | le plan de formes, en fonctions pures |
| `stage.js` | renderer, scène, lumière, ciel |
| `ocean.js` | houle de Gerstner : shader GPU **et** échantillonnage CPU |
| `foam.js` · `ssao.js` | champ d'écume persistant · occlusion ambiante du navire |
| `underwater.js` | la coque vue à travers l'eau, extinction par canal |
| `world.js` · `land.js` | l'archipel et ses ports · le maillage des îles |
| `jetty.js` | le ponton d'un port, construit ici ou fourni en .glb |
| `chart.js` | la carte marine, le point et le sillage tracé |
| `ship-model.js` | coque, gréement, voiles, sillage, chargement .glb |
| `ship-physics.js` | sondes, corps rigide 6 ddl, gouvernail, voiles |
| `controls.js` · `camera-rig.js` · `hud.js` | barre, caméras, instruments |
| `helm.js` | la barre des navires qui ne sont pas le vôtre |
| `guns.js` | la bordée, et surtout sa fumée |
| `cordage.js` | les bouts rompus qui pendent d'un mât touché |
| `sound.js` | le bruit des pièces, et le temps qu'il met à venir |
| `purse.js` | la bourse, le cours des épices, la poudre |

## Cahier des charges

**Plusieurs bâtiments peuvent être à flot en même temps**, à l'écran ou sur une
carte. Pas de réseau pour l'instant : ils tournent tous dans la même page, dans
la même boucle. Cela ne change rien à ce qui est déjà écrit, mais cela **borne ce
qu'on a le droit d'écrire ensuite** — toute nouveauté qui décrit l'état d'un
navire doit vivre sur *son* instance, jamais dans un global ni dans un uniforme
de la mer.

Ce qui supporte déjà N navires, sans rien changer :

- `ShipSpec`, `HullLines`, `ShipPhysics`, `ShipModel` sont des classes d'instance,
  sans état statique — il suffit d'en construire plusieurs ;
- l'**occlusion ambiante**, qui isole les navires par une couche de rendu
  (`Naval.SHIP_LAYER`) et non par un objet : dix navires passent dans la même
  passe, pour le même prix ;
- les **ombres portées** et les patches de matériau (`applyHaze`,
  `applySailLight`, `applyShipAO`), tous par maillage ou par matériau.

- **la mer et l'écume portent la flotte.** Les uniformes de coque sont des
  tableaux de `Naval.Config.MAX_SHIPS`, `uShipCount` disant combien sont vivants,
  et les deux shaders bouclent dessus en combinant par `max()` — deux coques bord
  à bord font une tache blanche, pas une tache deux fois blanche. Coût mesuré
  nul : la boucle tourne `NSHIP` fois quoi qu'il arrive, 0,40 ms à trois coques
  contre 0,42 à une ;
- **la passe d'eau transparente et le SSAO** portaient déjà N navires sans le
  savoir, isolant par couche de rendu et non par objet.

Ce qui suppose encore un navire unique, et qu'il faudra lever :

- **la fenêtre d'écume et la boîte d'ombre suivent un seul navire**
  (`foam.update(..., body.pos)`, `stage.aimSun(body.pos)`) : deux bâtiments
  éloignés ne peuvent pas être servis par la même fenêtre de 620 m ;
- **la barre, les instruments et les caméras** désignent l'entrée 0 de la flotte.
  C'est voulu — ce sont ceux du navire qu'on commande — et le bouton ⚓ du
  panneau Flotte déplace la barre en **échangeant** les entrées, puisque l'indice
  0 *est* le navire commandé pour la boucle, les instruments, les caméras et la
  fenêtre d'écume. L'indice étant aussi la ligne de texture, les profils sont
  réécrits avec, exactement comme à un retrait.

  **Les ordres voyagent aussi.** Le navire qu'on quitte garde une **copie** de
  l'état de barre comme sien, donc il continue sous la machine et les écoutes
  qu'on lui a laissées au lieu de se mettre en panne ; celui qu'on prend cède
  les siens à la console, qui montre alors ce qu'il fait vraiment et non ce que
  faisait le précédent. Partager un seul objet ferait obéir toute la flotte à la
  même roue.

  **Et rien ne doit RETENIR cet objet, ce qui a coûté un vrai bug.** L'échange
  réaffecte le `ctrl` de chaque entrée, mais une barre automatique construite
  avec `controls.state` en gardait une référence capturée à l'armement — que
  l'échange ne pouvait pas suivre. Le navire qu'on venait de quitter continuait
  donc d'écrire dans **votre** console, soixante fois par seconde : un chaland
  laissé derrière mettait votre machine en avant toute et votre barre à fond,
  un voilier laissé derrière tenait votre machine à zéro — la machine ne
  répondait plus du tout — et vos écoutes comme votre voilure ne vous
  appartenaient pas davantage.

  Les commandes sont donc **passées à chaque image** (`helm.update(dt, ocean, e.ctrl)`)
  plutôt que retenues. Ce n'est pas la même chose que de corriger l'échange pour
  qu'il répare aussi les barres : l'appelant tient l'entrée, l'entrée tient ses
  commandes du moment, et il ne reste plus rien qui puisse se périmer. Même
  famille de faute que le vecteur temporaire aliasé sur une valeur vivante.

**Mettre une coque à l'eau se fait par le panneau « Flotte »**, et il y a une
raison de ne pas le faire à la main : quatre choses sont faciles à oublier et
chacune donne un symptôme discret. Sans `applyAtmosphere` et `enableLighting`
elle ne prend ni brume, ni ombre, ni occlusion, et n'apparaît pas dans l'eau
transparente. Sans `settle()` **avant** de la positionner elle arrive en plein
ciel et rebondit. Sans `setHullProfile(i, …)` à son propre indice elle écume
autour des formes d'un autre. Et une entrée réduite à `{body, spec, afloat}` est
valide — la mer l'écumera — mais rien ne la fera naviguer : il lui faut aussi son
`physics`, son `ship` et son `ctrl`. `launch()` fait les quatre.

**L'indice dans la flotte EST la ligne de texture**, donc tout retrait réécrit
tous les profils (`refitProfiles`). Retirer le deuxième de trois fait glisser le
troisième d'un cran : sans réécriture il hériterait des formes du navire retiré.
De même, changer de navire **détruit les conserves** au lieu de vider la liste,
sinon leurs coques resteraient dans la scène, à naviguer sans rien pour les
piloter.

**Un profil de coque par LIGNE de texture, pas une texture par navire.** GLSL
ES 1.0 refuse d'indexer un tableau de samplers, donc `Naval.HullProfiles` empile
les demi-largeurs de chaque bâtiment dans une texture de `MAX_SHIPS` lignes, lue
au centre exact de sa ligne — aucun mélange entre navires. Pour la même raison,
`hullGap()` reçoit les uniformes de la coque **en paramètres** plutôt qu'un
indice : seule une expression constante peut indexer un tableau d'uniformes, et
un compteur de boucle en est une, un paramètre de fonction non.

En revanche la barre, les instruments et les caméras n'ont *pas* à devenir
multiples : ce sont ceux du navire qu'on commande. Il leur faudra désigner
lequel, pas se dupliquer.

## Le monde : quatre îles et leurs ports

**« Il n'y a pas de fichier de carte, et il n'y en aura jamais » — c'était faux,
et le revirement est raisonné.** Les îles étaient une fonction pure de la
position : un hachage sur une grille de 5,2 km, une île dans une case sur deux,
donc une mer sans fin qui ne coûtait rien à stocker. C'était la bonne forme pour
un monde où il n'y a rien — une infinité de terres anonymes vaut mieux que
quatre bits de terre, jusqu'à l'instant précis où il y a quelque chose entre
quoi naviguer. Un port qu'on quitte et où l'on revient vaut mieux que dix mille
baies sans nom, et on ne nomme pas ce qu'on n'a pas décidé.

Le hachage est donc remplacé par une **table de quatre**, dans
`Naval.ARCHIPELAGO`. Ce qui survit est la part de l'ancienne règle qui portait
vraiment quelque chose : c'est toujours une fonction pure de la position, sans
état et sans fichier à charger, et cela répond toujours en mètres monde vrais.
Il n'y a pas de fichier de carte ; il y a quatre constantes.

**Le prix est que la mer au-delà est vide**, et pour de bon : mettre le cap à
l'ouest de La Tortue, c'est ne plus jamais rien rencontrer.

**Port-Royal, Le Carénage, Saint-Pierre, La Tortue** — quatre ports réels de
l'époque de la flibuste, disposés comme les Îles du Vent dont ils sont tirés :
une chaîne nord-sud avec une île au large dans l'est, de **7,3 à 14,5 milles**
les unes des autres. Assez loin pour qu'une traversée en soit une, assez près
pour qu'on lève la suivante avant d'avoir perdu la précédente. Elles font de 2,6
à 4,2 km de rayon et jusqu'à 520 m de haut, soit dix fois les cailloux d'avant.

**Et le point est passé à 13° N.** Ce n'est pas de la décoration : le soleil suit
de la vraie trigonométrie sphérique depuis cette latitude, donc le jour est
presque égal toute l'année, midi passe près du zénith et le crépuscule est
court. C'est ce que fait la lumière aux Antilles, et c'est une seule constante
dans `Naval.Geo` si l'on veut revenir en Gascogne.

**Le plateau côtier se compte en MÈTRES, plus en fraction de rayon.** Il valait
0,35 du rayon de l'île, ce qui ne se voyait pas tant qu'une île faisait quelques
centaines de mètres et devenait absurde dès qu'elles ont fait des kilomètres :
une île de quatre kilomètres avait un mille et demi de hauts-fonds autour
d'elle, donc rien ne pouvait accoster nulle part et un ponton aurait dû être une
digue. Un plateau fait quelques centaines de mètres quoi que fasse l'île
derrière — 240 m, ce qui tombe dans la fourchette où les anciennes mesures
d'échouage avaient été prises.

**`world.js` travaille en mètres monde VRAIS, jamais en coordonnées locales.**
L'origine flottante fait glisser le zéro local à mesure qu'elle navigue ; une
terre placée en local s'en irait sous elle à chaque recentrage. C'est aussi
pourquoi `land.js` bâtit la géométrie dans le repère **de l'île** et se contente
de la **positionner** à `île − origine` : une île ne change jamais, donc la
recentrer est une affectation de vecteur, pas une reconstruction.

**Le rivage n'est pas un cercle.** Le rayon est modulé par quelques harmoniques
du relèvement, ce qui lui donne caps et anses — un disque se lit comme une pièce
tombée dans l'eau, et aucun détail de relief ne l'en sauve. `Chart` trace son
contour avec **le même `_shore`** que le maillage du terrain, donc la carte ne
peut pas montrer une côte que l'œil ne trouve pas : c'est la règle du plan de
formes unique, appliquée à la terre.

**La brume a dû s'ouvrir.** Réglée à 2,4 km de portée quand il n'y avait rien à
voir au-delà du navire, elle ne laissait passer que 11 % d'une île à un mille et
la réduisait à une tache. Portée à environ 8 km — un jour clair plutôt qu'un
jour de brume — sans quoi « terres à vue » ne veut rien dire.

**Le point se prend en latitude et longitude.** Un mille marin **est** une
minute de latitude, par définition, donc le nord se convertit exactement, sans
projection ni bidouille. L'est se resserre en cosinus de la latitude, ce qui est
réel : un degré de longitude fait 111 km à l'équateur et rien au pôle. L'ignorer
fausserait toute distance lue sur la carte de ce facteur.

**La carte est un canvas 2-D**, pas du WebGL : elle est plate, nord en haut,
faite de traits fins et de texte — ce qu'un canvas fait bien et un shader mal —
et la page a de toute façon 98 % de son image inoccupée. Elle trace en
coordonnées **vraies** ; en local, le navire reviendrait au centre à chaque
recentrage, ce qu'une carte ne doit jamais faire.

## Elle commence à quai

## Le môle, le bassin, et l'abri qui en sort

**UNE ÉCHANCRURE N'ABRITE RIEN, et c'est un rapport de deux nombres qui le
dit.** Relevé sur la baie de Port-Royal avant d'y toucher : **2 043 m
d'ouverture** pour 675 m de creux dans la côte, quand la houle qui porte
l'énergie à force 4 fait **23 à 40 m de longueur d'onde**. Le rapport ouverture
sur longueur d'onde vaut **soixante-deux**. Or la diffraction n'abrite que si la
passe est de l'ordre de quelques longueurs d'onde : à soixante-deux, la mer
entre tout droit sans rien perdre, et un coefficient d'atténuation posé là
n'aurait été qu'un abri décrété.

Le havre est donc un **bassin fermé par un môle**, et l'abri sort de la
géométrie : **130 m de passe pour 33 m de lame**, soit un rapport de quatre.

**Un disque et un anneau**, ce qui n'est pas de la paresse mais la condition
pour que la même forme soit calculable **trois fois** sans pouvoir diverger — au
CPU pour le solveur, dans le shader de la mer et dans celui de l'écume. Une côte
dessinée à la main ne s'écrit pas en quatre lignes de GLSL.

**Le môle est de la TERRE**, et c'est tout l'intérêt de l'écrire dans
`heightAt` plutôt que comme un objet avec une boîte de collision : un mur qui
est de la terre est un mur dont l'échouage sait déjà s'occuper. Elle le heurte,
se soulève, embarde et s'ouvre le flanc dessus exactement comme sur un
haut-fond, et pas une ligne de cela n'a été écrite deux fois.

Le maillage sort des **mêmes quatre nombres** — centre, rayon, épaisseur,
demi-angle de la passe — donc ce qui arrête la coque est ce que l'œil voit
l'arrêter. Vérifié sur les sommets : rayon 163 à 195 m, hauteurs −9,4 à +3,4,
soit 18 m d'arase et deux talus de 7, ce que `heightAt` donne au centimètre
près. Chaque tronçon demande à l'île ce qu'il y a dessous et s'efface quand elle
est plus haute — sans quoi une bande de pierre monterait à flanc de colline — ce
qui lui donne ses racines sur la plage pour rien.

**L'ABRI EST UNE FRACTION D'AMPLITUDE, ET TROIS CALCULATEURS DOIVENT LA LIRE.**
`Naval.SHELTER_GLSL` et `World.shelter` sont une **paire appariée**, et c'est
tout le risque de cette fonctionnalité : le shader de la mer, l'échantillonneur
CPU que la grille de sondes lit, et la passe d'écume. En oublier un ne casse
rien visiblement — il fait flotter la coque sur une mer que l'œil ne voit pas,
ce qui est exactement la panne silencieuse contre laquelle la phase de Gerstner
s'était déjà retournée.

Le modèle est celui d'un bassin derrière un mur : tout ce qui entre passe par la
passe et s'étale depuis elle, donc ce qui reste décroît avec la distance à la
bouche ; hors du mur, rien ne change. Un seul havre est passé au shader, en
mètres **locaux**, réécrit à chaque image : ils sont à sept milles les uns des
autres et l'abri porte à deux cents mètres, donc deux ne peuvent jamais être en
jeu ensemble.

Mesuré, hauteur significative à force 4 :

| | au poste | dans la passe | au large |
|---|---|---|---|
| Hs | **0,35 m** | 2,32 | 2,29 |

La mer divisée par **six et demi**, et le mou a pu être rendu aux amarres.
Relevé sur quatre minutes, mou de cinquante centimètres retrouvé : chaland
**0 talonnage** pour 0,54 m d'évitage, galion **0** pour 0,87 m. Contre 0,29 m
de talonnage avant le môle, quand il fallait les tenir à quinze centimètres.

Reste la frégate de deux mille tonneaux, à 0,18 m : elle cale 6,78 m et ce port
n'est pas pour elle.

**Le port est LU sur l'île, pas posé dessus.** Un havre est une baie, donc le
port se tient sur le relèvement où la côte rentre le plus près du milieu de
l'île — la plus profonde morsure du rivage, celle qui a de la terre de part et
d'autre. C'est le même `_shore` qui dessine le maillage du terrain et le
contour de la carte qui en décide, si bien que le ponton ne peut pas se
retrouver sur un cap que l'œil voit être un cap.

**La longueur du ponton est RÉSOLUE, pas choisie** : on inverse le profil du
plateau pour la profondeur qu'un poste demande, et c'est là que va le musoir.
Les deux bornes ont été trouvées en les essayant. Douze mètres donnaient un
appontement de 85 m sur des pieux de quatorze, c'est-à-dire un viaduc. Six et
demi paraissaient bons au musoir et ne l'étaient pas : une coque se range **le
long** du ponton, donc en dedans du musoir, là où le fond remonte encore — le
poste faisait quatre mètres, et par force 4 elle talonnait de 45 cm en évitant
sur ses amarres. Un navire qui touche à son propre quai n'est pas un port. À
sept mètres et demi : poste à **5,33 m**, talonnage maximal **0,23 m** sur cinq
minutes de force 4.

**Le ponton est CONSTRUIT tant que personne n'en fournit un**, et dans cet
ordre. Un repli écrit après l'asset est un repli que personne ne regarde. Ce qui
le fait lire comme un ponton plutôt que comme une planche sur l'eau, ce sont ses
**jambes** : le tablier est de niveau parce qu'un tablier l'est, les pieux ne le
sont pas parce que le fond ne l'est pas — chaque paire est coupée sur le fond
qu'elle touche, lu dans le même `heightAt` où la quille talonne. La structure
s'allonge donc des jambes à mesure qu'elle marche vers le large, et c'est toute
sa silhouette ; une rangée de poteaux égaux se lit comme une clôture. Le bordage
court **en travers**, comme on borde un appontement sur ses bauquières.

**Elle démarre à Port-Royal, et c'est l'origine flottante qui rend cela gratuit.**
`settle()` pose toujours la coque au zéro **local** ; il suffit donc de dire où
ce zéro se trouve réellement dans le monde et elle s'y assoit — sondes, échouage
et tirant d'eau compris, puisque le vrai solveur tourne pendant qu'elle
s'installe. Son cap est mis **avant** le settle et non après : posée en travers
du poste elle chercherait le fond avec un bord au lieu de sa quille.

**Et elle est AMARRÉE, sans quoi le départ au port ne tient pas une minute.**
Rien ne retient une coque libre : relevé avant les bouts, trente degrés
d'abattée en quelques secondes et vingt mètres de dérive sur le ponton. Les
amarres sont l'échouage et l'abordage écrits une troisième fois — un ressort
raide, très amorti, appliqué **au** point où il agit — **avec une différence, et
c'est tout le caractère d'un cordage : il tire et ne pousse jamais.** En deçà de
sa longueur un bout est mou et ne fait rien, donc elle évite sur son mou et
raidit quand elle l'a filé, ce qui est ce que fait une coque à quai ; deux
ressorts vers un point fixe la feraient tenir comme boulonnée.

Trois choses ont coûté un essai chacune :

- **les bittes sont du MÊME bord que le poste.** Posées en face, les bouts la
  halaient au travers du ponton — et elle s'en allait de vingt mètres en tenant
  parfaitement son cap, ce qui est exactement ce à quoi ressemble une amarre qui
  tire du bon côté à travers la structure ;
- **elles se prennent en face de ses écubiers, pas le long du quai.** Réparties
  d'avance sur la longueur du ponton elles tombent n'importe où par rapport au
  navire : relevé, des bouts de **30 m** sur un chaland de 28. On prend
  l'écubier, on cherche la bitte en face, et l'amarre élonge de quelques mètres
  dans le sens où elle doit retenir. Après : **6,3 · 9,2 · 7,8 · 7,7 m** ;
- **et elle se range en dedans du musoir.** Le centre posé en face de la tête du
  ponton la fait dépasser de la moitié de sa longueur, et son amarre de proue
  n'a plus de bitte devant elle.

Chaque bout est enfin **frappé à la longueur mesurée sur place**, plus un demi-
mètre de mou. Une longueur choisie d'avance donne des gardes tendues au repos —
12,1 m sur un bout de 5,1 — qui halent en permanence et déplacent le poste
jusqu'à trouver leur équilibre. Un matelot tourne le bout à la longueur où il
tombe. Vérifié : elle tient son poste à 5–7 m pendant huit minutes de force 4,
puis **`M`** largue tout et elle sort à 3,8 nœuds sans toucher.

Les bittes sont prises sur le **port** et non sur le maillage du ponton, pour
que l'amarrage tienne aussi bien avec le ponton construit ici qu'avec celui
qu'on fournira.

**ÊTRE AU POSTE EST UNE QUESTION DE LIEU, PAS DE RANG**, et l'avoir écrit
autrement a coûté un bug signalé à l'usage : « la Roter Löwe apparaît face au
port, collée au ponton ». La condition était « au premier armement ».
Or `settle()` repose **toujours** la coque au zéro local, donc changer de navire
la ramène au poste de toute façon — mais avec un quaternion neuf, c'est-à-dire
cap zéro, en travers du ponton, et sans un bout. Un drapeau « déjà fait » ne
pouvait pas voir ça ; comparer l'origine courante au poste, oui.

Deux corollaires, une fois la question posée en termes de lieu :

- **l'écartement suit le bau de CE navire.** Le poste est calé une fois pour
  toutes sur le premier ; une coque plus large chevaucherait le ponton d'autant.
  On n'y déplace pas le poste — ce serait un recentrage, qui a sa propre liste de
  choses à décaler — on écarte la coque en local de la différence ;
- **et les gros vont au bout du quai**, ce qu'un vrai quai impose : le fond
  remonte vers la plage. Elle marche vers le musoir jusqu'à trouver l'eau qu'il
  lui faut, et pas au-delà — au-delà il n'y a plus de quai. Une coque trop
  profonde pour ce port le reste, et le dire vaut mieux que creuser la baie
  sous elle.

Il faut **2,6 m sous quille et non 1,5** : `navigable` parle d'un navire qui
passe, un navire qui reste s'y fait poser par la houle plusieurs fois par
minute. Et la sonde se prend **sous ses extrémités**, pas sous son milieu —
même leçon que l'échouage, qui se sonde en trois points pour cette raison
exacte. Le fond remonte vers la plage, donc sa poupe est toujours dans moins
d'eau que son nombril : sondée au centre seul, elle se rangeait dans sept mètres
et talonnait quand même de vingt-neuf centimètres, de l'arrière.

**UN CORDAGE TIRE ET NE POUSSE JAMAIS, DONC IL NE PEUT PAS LA TENIR ÉCARTÉE.**
C'est évident après coup et cela ne l'était pas : avec quatre bouts et rien
d'autre, un vent qui la met au quai la fait traverser le ponton pendant que
toutes ses amarres pendent molles. **C'est le quai qui tient un navire à
distance du quai**, pas ses cordages.

Une défense est donc **le même ressort avec le signe retourné** : elle pousse
quand on entame son épaisseur et ne fait rien tant qu'on ne l'entame pas. Un
seul drapeau (`push`) dans `_moor`, deux défenses frappées par le travers à
l'avant et à l'arrière, et la question est réglée. Relevé, trois minutes de
force 4, six navires :

| | chaland | Roter Löwe | pirate | goélette | cotre | frégate 2000 t |
|---|---|---|---|---|---|---|
| tirant | 2,86 m | 3,21 | 3,15 | 2,87 | 2,46 | **6,78** |
| évitage | 0,50 | 0,38 | 0,38 | 0,48 | 0,58 | 0,62 |
| jeu au ponton | 4,04 | 4,25 | 4,26 | 4,10 | 3,97 | 4,32 |
| talonnage | **0** | **0** | **0** | **0** | **0** | 0,29 |

Contre six à dix mètres d'évitage et un bordage frotté avant les défenses. Reste
la frégate de deux mille tonneaux, qui cale **6,78 m** et en demanderait 9,4
sous ses extrémités quand le musoir n'en offre que neuf : elle est trop grosse
pour ce ponton, elle le restera, et le dire vaut mieux que creuser la baie sous
elle. Un vrai navire de ce port mouillerait en rade.

**Tenue courte, et c'est un renoncement assumé.** Un demi-mètre de mou lisait
bien — elle évitait, rangeait dans la houle et raidissait en filant son mou, ce
que fait une coque à quai — mais dans une baie ouverte cela lui faisait promener
ses extrémités jusqu'à toucher. Quinze centimètres, donc : on prend le boulon en
attendant que le port soit abrité, auquel cas c'est ce chiffre-là qu'il faudra
rendre.

## Mouiller

**UNE ANCRE AU FOND EST UNE AMARRE DONT LA BITTE PEUT BOUGER** (`anchor.js`).
Le quai tient déjà un navire par un ressort qui tire et ne pousse jamais, frappé
sur un point en mètres monde vrais ; être à l'ancre, c'est la même ligne frappée
sur un point du fond, avec deux différences que `_moor` porte désormais par ligne :
un long câble est un ressort plus **souple** qu'une aussière (`stretch`, il doit
soulever sa propre chute avant de raidir), et son bout **cède** quand on le tire
plus fort que l'ancre ne résiste (`hold`) — le point d'ancrage glisse alors sur le
fond vers le navire, de ce que le plafond a refusé d'allongement. Aucune règle
« l'ancre chasse » : le même ressort, avec son bout libre de céder. Tout le reste
— la chute depuis le bossoir, la gerbe, la descente, le câble qui file, le
cabestan — vit dans le module et le solveur n'en sait rien.

**La tenue est énoncée, pas réglée.** Une ancre de bossoir pesait environ quatre
millièmes du déplacement — une tonne pour un galion de 240 t, quatre pour un
vaisseau de ligne, ce qu'ils portaient — et tient à peu près huit fois son poids
**si elle travaille à plat**. Elle ne le fait qu'avec de la **touée** : le câble
doit tirer le long du fond et non vers le haut, donc la tenue tombe quand la
longueur filée se rapproche de la profondeur. L'équipage file trois fois et demie
le fond s'il l'a. Relevé :

| | fond | câble | touée | tenue |
|---|---|---|---|---|
| chaland au port | 9 m | 31 m | 3,4 | 41 kN |
| pirate au large | 70 m | **180 m, tout** | 2,6 | 30 kN |

Au large la mer fait soixante-dix mètres partout et le câble s'arrête à six
longueurs de coque : on mouille, mais on tient mal. Force 4 à sec de toile, elle
évite de 11 m sur son mou et le câble ne raidit jamais — le fardage de ce solveur
est faible ; voiles établies, il raidit à la tenue, l'ancre chasse et le navire la
traîne à 1,6 m/s. C'est la vraie raison pour laquelle on mouillait en rade et non
en pleine mer, et elle sort de l'arithmétique.

**Relevé de la chute**, pirate par 70 m : gerbe à 0,87 s, trois mètres par seconde
dans l'eau, **au fond à 23,5 s**. Levée : le cabestan reprend 1,2 m/s et la hale
vers son ancre par le même ressort raccourci, l'ancre dérape à 87 s, bossée à
150 s. « L'ancre chasse » est tu pendant qu'on vire — le cabestan tire au-delà de
la tenue exprès.

**LE BOSSOIR EST EN DEHORS DU BORDÉ, et l'avoir écrit en dedans a tout caché.**
Posé à la moitié du bau, l'ancre était mouillée DANS la coque et tombait sans
qu'on la voie à travers son propre fond : les chiffres étaient justes et il n'y
avait rien à l'écran. Il est pris sur le bordé du modèle (`ship.shell`), sur
l'avant, à tribord, à l'extérieur de la plus grande demi-largeur.

**Le câble est une chaîne de Verlet épinglée aux deux bouts** — bossoir et
organeau — lourde dans l'air, presque sans poids et très freinée dans l'eau, et
jamais sous le fond, pris en droite entre le fond sous elle et le fond sous
l'ancre, échantillonnés deux fois par seconde. Il est dessiné avec **le même
shader de ruban que les bouts rompus** et ses propres uniformes de largeur — une
définition de ce qu'est un cordage, et un câble de 18 cm n'est pas un bras. Ancre
et câble sont aussi sur `SHIP_LAYER`, pour que la passe qui montre une coque à
travers l'eau montre l'ancre qui descend.

**UN SHADERMATERIAL N'OBÉIT AU PLAN DE COUPE QUE S'IL LE DÉCLARE**, et le câble
l'a révélé : signalé à l'usage, une longue ligne sombre ondulait sur la mer au
large de l'étrave. Le miroir de la mer ne garde que ce qui est au-dessus de
l'eau par un plan de coupe du renderer ; les matériaux standard le respectent
d'eux-mêmes, un `ShaderMaterial` seulement avec `clipping:true` et les morceaux
`clipping_planes_*` de three. Le ruban des cordages n'avait ni l'un ni l'autre,
donc ce qui en était sous l'eau passait tout entier dans le reflet, déformé par
la houle — invisible sur un bout rompu qui pend à la surface, flagrant sur un
câble qui descend de soixante-dix mètres. Corrigé dans le shader commun, ce qui
règle les deux. **Vérifié à l'usage et non au banc** : trois essais synthétiques
(câble réel, câble enfoncé, bout posé à deux mètres sous la surface) n'ont pas
reproduit le fantôme, avec ou sans la coupe. Tout autre `ShaderMaterial` qu'on
posera au-dessus et au-dessous de l'eau a le même piège.

**`M` tient tout le mouillage** : à quai il largue les amarres — et elles seules,
`castOff` gardant la ligne d'ancre — libre il mouille, ancre dehors il la fait
lever. Ce qui manque : la compression du temps reste refusée tant qu'une ligne est
dehors, le test « à quai » ne distinguant pas encore une ancre d'un quai, et les
conserves ne mouillent pas.

## La chaloupe

**UNE CHALOUPE EST UN NAVIRE**, et c'est tout ce qui l'a rendue facile. Sa fiche
(`ships/chaloupe.json`) est une fiche comme les autres, le navire qui la porte la
nomme (`"boat": "chaloupe"` : Roter Löwe, galion pirate, frégate), elle est mise à
l'eau par le même `launch()` que les conserves, et on en prend la barre par le
même `takeHelm` que le bouton ⚓. Flotter, talonner, s'échouer moins — 0,45 m de
tirant —, repêcher une bouteille : rien de cela n'a été écrit pour elle. `N`
l'affale le long du bord (bâbord, l'ancre pendant à tribord ; à quai, le bord le
plus loin des bittes, sinon on l'affalait sur le ponton) ou la hisse, à moins de
`0,55·L + B + 6` m du navire et sous 1,2 m/s. Exclue des rencontres par
`settings.json`.

**Le navire mère mouille** : toile ferlée, machine stoppée, ancre mouillée s'il
n'est ni à quai ni déjà à l'ancre. **Et sa barre de réserve est mise au repos**
(`helmRepos`), ce qui a coûté un essai : l'entrée 0 porte une `AutoHelm` qu'elle
n'emploie pas, prête à reprendre la route quand on change de navire — et qui,
dès qu'on passait dans la chaloupe, chassait le navire à la barre, c'est-à-dire
la chaloupe. Relevé : barre à −1 et voiles établies une seconde après l'affalage.
Hisser la rend.

**LES AVIRONS SONT DANS LE SOLVEUR, PAS DANS L'IMAGE.** Deux bords indépendants
(`ctrl.oarL`, `ctrl.oarR`, de −1 à 1 : `Q`/`A` bâbord nage/scie, `E`/`D`
tribord, `W`/`S` les deux), chacun avec sa phase de coup (`physics.oar`). La
poussée est celle de `engine.topSpeed`, dépensée en demi-sinus pendant les 45 %
du coup où la pale est dans l'eau — pas une machine constante — et le modèle lit
la phase pour faire battre les avirons : pale carrée et tirée vers l'arrière,
puis dégagée et plate pour revenir. Sans gouvernail : on tourne en nageant d'un
seul bord.

**LE LEVIER EST LA PALE, PAS LE TOLET.** Première écriture : la poussée au tolet,
à une demi-largeur de l'axe, et la chaloupe tournait d'un degré par seconde en
pivotant. La main sur le manche et le manche sur le tolet sont des forces
**intérieures** au système chaloupe-avirons ; la seule poussée du dehors est
l'eau sur la pale, à 60 % d'un aviron au-delà du plat-bord. Trois fois le bras
de levier, et c'est la physique et non un réglage. Second défaut, de fiche : à
`resistance` 61, celle des grandes coques, la poussée dérivée faisait 194 N et il
fallait plus de quarante secondes pour prendre l'erre. À 200, 635 N — quatre
nageurs. Relevé à pas fixe, coque isolée en eau libre :

| `resistance` | poussée | erre à 15 s | un bord, 10 s | pivot, 10 s | scier, 12 s |
|---|---|---|---|---|---|
| 120 | 381 N | 1,30 m/s | 23° | 42° | −0,37 m/s |
| **200** | **635 N** | **1,60** | **56°** | **93°** | **−0,74** |
| 280 | 889 N | 1,73 | 78° | 139° | −1,09 |

**Ce qu'elle rapporte.** La cargaison échouée ne se prend plus « en envoyant le
canot » à 180 m : il faut y aller en chaloupe et s'arrêter à 15 m
(`cargo.needsBoat`, `claimRadius`). Les épices vont dans la cale du **navire
mère** et non dans la chaloupe — huit tonnes dans une embarcation qui en déplace
trois, c'est elle qui coulait. Relevé : cargaison refusée au galion, prise en
chaloupe à 5 m.

Ce qui manque : la console de barre montre encore machine, barre et écoutes en
chaloupe ; aucun modèle `.glb` ; la chaloupe ne se range pas visiblement sur le
pont du navire mère.

## Invariants à ne pas casser

**Un seul plan de formes.** `hull-lines.js` sert à la fois au maillage visible et
à la grille de sondes. Si les deux divergent, ce qu'on voit ne correspond plus à
ce qui flotte — c'est l'invariant le plus important du projet.

**L'origine flotte, et la phase de la houle est ce qui le permet.** Tout est
calculé près de zéro ; `ocean.origin` retient où ce zéro se trouve réellement.
Au-delà de `REBASE_RADIUS` (1500 m) le monde entier glisse sous la flotte.

Sans cela la mer meurt bien avant ce qu'on imagine : la phase de Gerstner vaut
`k·x`, et avec `k` jusqu'à 3 rad/m une position de quelques kilomètres consomme
déjà la quasi-totalité des sept chiffres d'un flottant 32 bits. Les vagues ne
tremblent pas, elles **disparaissent** — vérifié à 400 km : avec l'origine
flottante la houle est normale, sans elle la mer est une nappe parfaitement
lisse.

Décaler l'origine ferait glisser toute la mer de côté, **sauf** si on rend la
phase que ce décalage représente, `k·(d·origine)`. Or cette phase croît sans
borne et ramènerait le problème — sauf qu'une phase ne compte que **modulo 2π**.
Réduite dans les doubles de JavaScript (`syncPhase`), elle reste un petit nombre
que le shader tient exactement. Mesuré : **saut nul** à chaque recentrage,
jusqu'à une origine de 2 300 km.

Trois calculateurs de houle doivent lire cette phase — le vertex shader de la
mer, l'échantillonneur CPU et la passe d'écume — et en oublier un les
désynchroniserait silencieusement.

**Tout ce qui tient une position doit se décaler dans la MÊME image** : les
coques, le champ d'écume (ses *deux* ancres, sinon l'image suivante lit le
décalage comme un glissement colossal et étale le champ), et les caméras — la
caméra fixe surtout, seule chose délibérément immobile, donc seule à se
retrouver à mille mètres de là.

**Le shader et le CPU doivent partager les mêmes vagues.** `ocean.js` calcule la
phase en **espace monde** (`modelMatrix * position`) car le plan est recentré sur
la caméra à chaque image. En espace local, la houle resterait collée à la caméra
et se désynchroniserait du champ de hauteur que le solveur échantillonne.

**Le gouvernail est une force à l'étambot, jamais un couple pur.** C'est ce qui
place le point de pivot en avant du maître-couple, comme sur un vrai navire
faisant route avant, au lieu de la faire tourner autour de son milieu.

**Le cap se lit sur le vecteur d'étrave**, pas sur l'angle d'Euler. Le prendre
sur Euler donnait un signe inverse, qui annulait exactement une erreur de sens
du gouvernail : les deux fautes se masquaient mutuellement.

**TRIBORD EST LE −X LOCAL, ET L'EST EST LE −X DU MONDE. Ce n'est pas une
convention, c'est une conséquence, et l'avoir écrite à l'envers a fait vivre
deux fautes qui se masquaient — exactement la famille ci-dessus, retrouvée sur
un autre axe.**

three.js est **direct**. Avec l'étrave sur +z et le mât sur +y, la main droite
d'un homme de barre pointe vers `étrave × haut` = ẑ × ŷ = **−x̂**. Il n'y a rien
à choisir là-dedans, c'est du produit vectoriel. Or tout le projet appelait
« tribord » le +x.

Et l'est suit, par un second raisonnement qui ne laisse pas plus de liberté :
un relèvement doit **croître** quand elle abat sur tribord. Elle tourne alors
vers −x, donc le cap ne monte que si l'est est en −x. Le nord en +z force
l'est en −x aussi sûrement que la main droite force le tribord. Autrement dit
le repère géographique du jeu était **le miroir** de celui de ses coques.

**Les deux fautes s'annulaient dans les instruments, et seulement là.** Barre à
tribord, l'étrave partait vers +x — son bâbord — mais le compas, qui croyait
+x à l'est, lisait un cap qui monte, et la carte traçait un sillage tournant à
droite. Aiguille et carte étaient donc d'accord entre elles et **fausses
ensemble**. Ne dépassaient de ce mensonge que les deux choses qui parlent au
monde plutôt qu'à l'instrument : la vue en 3-D, où elle abattait visiblement à
gauche, et les canons, où la batterie marquée « Tribord » crachait par l'autre
muraille. C'est exactement ce qui a été signalé à l'usage — « bâbord est à
gauche et tribord à droite, les canons et la navigation font l'inverse » — et
la carte paraissait « en miroir » parce qu'une île relevée à tribord s'y
dessinait à bâbord.

Corriger les canons seuls aurait été le piège : une bordée juste sur une coque
qui tourne encore du mauvais côté, sous une carte encore retournée. **Les deux
signes se corrigent ensemble ou pas du tout.**

Le remède tient en un vecteur et une poignée de conséquences :

- `right.set(-1,0,0)` dans le solveur, et **le gouvernail est revenu juste tout
  seul**, ayant toujours été correct *relativement* à cet axe. Trois des quatre
  autres usagers — dérive, résistance latérale, relèvement de la gerbe —
  projettent sur cet axe puis reconstruisent dessus : ils sont **invariants** au
  retournement, et c'est ce qui a rendu l'opération sûre. Le quatrième, `tack`,
  désigne enfin l'amure qu'il nomme ;
- l'est à −x dans le compas, la carte, la barre automatique, le point en
  latitude et longitude, le vent et le **soleil** — qui se lève désormais où la
  carte dit qu'est l'est ;
- `side = +1` veut dire tribord partout : la lecture des batteries prend les
  pièces en x négatif, le boulet part vers `−side`, et l'arrimage met à tribord
  ce qu'on lui dit d'y mettre.

Relevé après coup, à pas fixe : barre à tribord, cap **000 → 023** avec
l'étrave partie vers −x ; bordée « Tribord » lancée sur −x ; les six pièces
marquées tribord du galion pirate toutes en x négatif ; un point relevé à
tribord tracé **à droite** sur la carte ; soleil au relèvement 090 en −x ;
pavillon qui fait suivre le vent. Et le poste tenu, 0,67 m d'évitage pour
**zéro** talonnage sur quatre minutes — l'amarrage ne lisait pas cet axe.

**Ne jamais réutiliser un vecteur temporaire pour une valeur vivante.** Le centre
de gravité monde a son propre vecteur (`_cog`) : il avait été aliasé sur `_tmp`,
écrasé dès la première sonde, ce qui faussait tous les bras de levier et
provoquait roulis parasite puis explosion numérique.

**Le ciel n'existe qu'une fois.** `Naval.SKY_GLSL` (dans `stage.js`) est la seule
définition du ciel ; le dôme et le reflet de la mer l'appellent tous les deux. Si
l'eau miroitait un autre ciel que celui du dessus, l'horizon montrerait une
couture. Le dôme ajoute seulement le disque solaire ; la mer tire son reflet
d'un lobe micro-facettes.

**Le Fresnel spéculaire se prend sur `V·H`**, pas sur `N·V`. Le prendre sur la
normale effondre le terme à ~2 % sous tous les angles depuis lesquels on regarde
réellement la mer, et supprime le chemin de scintillement.

**Un seul profil de voile.** `Naval.SAIL_FOIL` (dans `ship-physics.js`) porte les
trois coefficients de l'aérofoil. Ils sont lus deux fois : pour fabriquer la
force, et pour en déduire le bordage optimal en forme fermée (`optimalAoA`).
Réécrits en dur aux deux endroits, ils finiraient par diverger et le repère de
la console désignerait un réglage que les voiles ne veulent pas — même famille
de faute que le plan de formes unique.

**Les coefficients hydro sont par unité de surface**, pas des forces absolues.
C'est ce qui permet aux mêmes valeurs de servir une goélette de 24 m et une
frégate de 60 m sans réglage par navire.

## Fiches navires

Un JSON par bâtiment dans `ships/`. **Le dossier fait foi** : y déposer un
fichier suffit (le serveur de dev liste le dossier en direct, le build le balaie).
Le **tonnage est l'entrée** ; la fraction immergée en découle. Viser 30–45 %
d'immersion coque.

```bash
node tools/add-ship.js ships/models/mon-bateau.glb --nom "La Sirène"
```

L'outil lit la boîte englobante réelle du .glb et mesure le volume d'enveloppe
**avec les mêmes formules que le solveur**, pour proposer un tonnage cohérent.

**Deux fiches peuvent partager une carène, et alors elles partagent ses cotes.**
La Roter Löwe et le navire pirate sortent du même dessin : relevé sur les
maillages, bau/longueur 0,26 et creux/longueur 0,42 pour l'un comme pour
l'autre, toutes les cotes du second valant exactement le double du premier. Ils
sont donc à 30 m et 240 t tous les deux.

Le piège est qu'une fiche est un **tout cohérent** : corriger la longueur et le
tonnage sans le reste laisse un navire impossible. Passée à 30 m sans y toucher,
la Roter Löwe gardait 14,5 m de bau, des mâts de 38 m plus hauts qu'elle n'est
longue et 1 850 m² de voilure — **8,4 % d'immersion**, elle flottait comme un
bouchon. Les longueurs vont en ×s, les surfaces en ×s², la vitesse machine en
×√s (Froude), et `rudder.power` ne bouge pas, étant par unité de surface —
mais il revient de 275 à 91,5, le triplement ayant visé les lourds carrés de
60 m dont l'inertie de lacet est trente-deux fois celle-ci. Après : **32,6 %**
d'immersion et 2,82 m de tirant.

Voir `ships/README.md` pour le format complet.

## Contraintes de publication (raison d'être de build.js)

Une page publiée tourne sous une politique de sécurité stricte. Le build existe
pour la satisfaire :

- un `<script src="js/...">` **local est bloqué** → tout est inliné ;
- `fetch()` d'un fichier local est bloqué → les fiches JSON sont embarquées dans
  `Naval.SHIP_DATA` ;
- un `.glb` n'est ni chargeable localement ni téléversable comme asset d'artifact
  (types acceptés : png, jpg, svg, mp4, pdf, woff, csv, md, json, txt) → ses
  octets sont embarqués en base64 et analysés par `GLTFLoader.parse()`, sans
  aucune requête réseau ;
- le build **refuse** de produire un fichier contenant encore une référence locale.

`GLTFLoader` reste chargé depuis jsdelivr via l'import map. S'il est bloqué, la
coque procédurale est conservée avec un avertissement : un modèle manquant
n'interrompt jamais la simulation.

## Pièges rencontrés

**Une page autonome doit déclarer son propre encodage, en PREMIER.** Il n'y
avait aucun `<meta charset>` dans `naval-sim.html`, au motif que l'hôte des
artifacts en fournit un et que le serveur de dev pose l'en-tête lui-même. Les
deux sont vrais, et cela ne suffit pas : déposée sur un hébergeur ordinaire —
Apache chez OVH — la page est servie en `text/html` sans charset, le navigateur
retombe sur du latin-1, et toute l'interface française part en mojibake. Le
symptôme est déroutant parce qu'il **n'apparaît pas en local** : les deux
mécanismes qui masquaient le manque sont justement ceux du développement.

La balise est à l'**octet 0**. Un navigateur ne lit que le premier kilo-octet du
document pour la trouver, donc tout ce qui la précède la met en danger — et rien
n'a besoin de la précéder, pas même le `<title>`.

**Encodage.** `Get-Content` en PowerShell 5.1 lit en ANSI, pas en UTF-8 : extraire
puis réécrire un fichier accentué produit du mojibake et un BOM. Utiliser
`[System.IO.File]::ReadAllText/WriteAllText` avec un encodage explicite.

**Git et les .glb.** Ils sont binaires ; une conversion de fins de ligne les
corromprait silencieusement. `.gitattributes` les marque `binary`.

**Le compteur d'images ne mesure pas la simulation, il mesure l'horloge du
volet.** Mesuré avec une boucle `requestAnimationFrame` **vide**, ne faisant
rigoureusement rien : intervalle médian de 31,2 ms, soit 32 images/s. Le volet
de prévisualisation cadence à 32 quoi qu'on lui demande, si bien que le compteur
affichait le même 32 par mer calme et en tempête. Il disait vrai et n'apprenait
rien.

D'où le second chiffre, le **travail** dans l'image : du haut de la boucle à la
fin du rendu. Il bouge, lui — 0,86 ms à un navire, 2,72 ms à quatre, dans un
budget de 31,3. Autrement dit la page est oisive 98 % du temps, et le rendu
coûte la même chose par mer plate qu'en tempête (0,22 contre 0,21 ms), le shader
bouclant de toute façon sur les dix-huit composantes. Attention : c'est du CPU
seul — sans requête de chronomètre GPU, un shader devenu coûteux ne s'y verrait
pas.

**Mesures dans le navigateur.** Quand le volet de prévisualisation n'est pas
composité, `requestAnimationFrame` est bridé — jusqu'à **zéro** image, pas
seulement une par seconde : la simulation est alors complètement arrêtée et les
relevés paraissent figés ou absurdes (vitesse qui ne monte pas, immersion
incohérente). Ce n'est pas un bug de physique. `document.hidden` reste à `false`
dans ce cas et ne suffit donc pas à le détecter ; le test fiable est de comparer
`Naval.app.ocean.uniforms.uTime.value` avant et après une attente. Pour mesurer
sérieusement, ne pas dépendre de la boucle : piloter le solveur à la main depuis
la console (`physics.step(dt, ocean, ctrl, t)` en boucle, `t` avancé soi-même),
ce qui donne en prime des mesures reproductibles à pas fixe. Une capture d'écran
force quelques images au passage, ce qui suffit à rafraîchir la télémétrie.

Corollaire pour le **rendu** : la boucle gelée ne met plus à jour les uniformes
qu'elle alimente. Déplacer `stage.camera` à la main puis appeler `stage.render()`
laisse `ocean.uniforms.uCam` sur la position de la dernière vraie image, et tout
ce qui dépend du regard — brume, scintillement, translucidité de la toile — est
calculé depuis un œil qui n'est plus là. J'ai cru une heure durant que le shader
de voile était mort alors que seul le banc de mesure l'était. Recopier `uCam`
soi-même après avoir bougé la caméra.

Autre piège de mesure : `read_console_messages` conserve le tampon **d'un
chargement à l'autre**. Une erreur de shader déjà corrigée continue de s'afficher
après rechargement, aux mêmes numéros de ligne, et fait croire à une panne qui
n'existe plus. Ne pas conclure sur la console seule — vérifier que le correctif
est bien servi (le serveur de dev laisse le navigateur mettre les `.js` en
cache ; `fetch(url, {cache:'reload'})` avant de recharger règle la question).
À l'inverse, pour retrouver l'erreur *courante* sous une pile de vieilles, la
filtrer par motif sur un identifiant du code fraîchement écrit : c'est ce qui a
fait sortir le `half` réservé après plusieurs minutes passées à suspecter le
mauvais fichier.

**Shaders.** Un `ShaderMaterial` avec `fog: true` doit fusionner
`THREE.UniformsLib.fog`, sinon le rendu lève une erreur sur `fogColor.value`. Et
un `#define N` écrase l'identifiant `N` jusque dans le fragment shader.

**Rendu.** Une mise à l'échelle négative (`scale.x = -1`) fait disparaître un
maillage. Une voile opaque éclairée à contre-jour devient noire : le tissu porte
une composante `emissive` pour rester lisible.

**Mise en ligne.** `node build.js` écrit `ships/index.json` — et **avertit** quand
`Naval.Config.SHIPS` a dérivé du dossier, ce qui arrive à chaque navire ajouté.
L'avertissement a servi dès le navire pirate. Le serveur de dev
répond à ce chemin par un listage en direct, sans fichier ; un hébergeur statique
non. Sans cet index, la page retombe sur la liste courte de `config.js` et tout
navire ajouté depuis n'est jamais demandé — ses .glb paraissent alors ne pas se
charger alors qu'ils n'ont jamais été réclamés. Le plus sûr reste de déployer
`dist/naval-sim.html` seul, qui embarque tout.

**Console de mer.** Six réglages : force de la houle (Beaufort), direction du
vent, hauteur du soleil (négative = nuit), **défilement du jour**, **creux** —
qui multiplie la hauteur significative au-delà de la table Beaufort — et
couverture nuageuse. Plus un bouton « Météo automatique », qui laisse le vent se
conduire tout seul. Ce dernier existe parce qu'un spectre
étalé sur dix-huit composantes et un éventail de directions paraît plus plat que
six harmoniques alignées, à hauteur égale : les crêtes ne se superposent plus.
Au-delà de ~1,6 un navire peut réellement chavirer, ce qui est voulu.

**ET LA LIGNE DIT QUI COMMANDE LA MER**, ce qui manquait et s'est signalé à
l'usage sous la forme la plus révélatrice qui soit : « avant je ne pouvais pas
forcer la force de la houle, maintenant je peux — c'est normal ? ».

La règle était juste et invisible. Trois prétendants, et la précédence est
**dépression > météo automatique > console** : prendre un curseur en main met la
météo au repos sur-le-champ, mais une dépression ne négocie pas et l'emporte sur
les deux — seulement **vers le haut**, `squall.force > tgtF`, elle n'empêche
jamais de monter la houle, elle empêche de la baisser sous ce qu'elle impose.

Le symptôme n'était donc pas un refus mais une **réécriture** : tant qu'un autre
décide, la boucle repose la valeur dans le curseur cinq fois par seconde. On le
pousse, il revient tout seul, et cela se lit comme une panne alors que c'est la
règle. La météo automatique annonçait déjà son état sur son bouton ; la
dépression n'avait rien pour elle — et c'est justement celle qui surprend,
puisqu'elle arrive sans qu'on l'ait demandée et repart de même.

La ligne porte donc son maître, à l'accent : « Force de la houle · **dépression** »
ou « · **météo auto** ». Sur CETTE ligne et pas ailleurs, parce que c'est ce
réglage-là qui a perdu la main et qu'une alerte posée en haut du panneau
n'aurait pas dit lequel. Vérifié sur les quatre états — console seule, météo en
route, météo rendue, et au cœur d'un grain de force 8,9 avec le curseur à 4.

**Gréer un modèle importé.** Un `.glb` arrive avec une coque et des espars nus,
mais quasiment jamais de voiles — la Roter Löwe n'en a aucune, et ses nœuds
portent les noms Blender par défaut (`Cylinder.004`…), donc rien à quoi les
reconnaître *par le nom*. Ce qu'elle a, ce sont des **vergues**, et une vergue se
reconnaît à sa forme seule : un espar bien plus large en travers qu'épais, posé
en croix sur l'axe. `_rigModel()` les lit sur la géométrie, les regroupe par mât
(l'écart entre mâts est d'un ordre de grandeur supérieur à la dispersion sur un
même mât) et y suspend la toile. Aucune donnée par navire : déposer un carré
dans `ships/models` suffit à le gréer.

Deux points à ne pas défaire. Les vergues sont **reparentées dans le pivot** qui
porte la voile (`Object3D.attach`, qui conserve la transformée monde) : faire
tourner la toile seule la ferait glisser hors de sa propre vergue. Et la chute
des voiles est bornée par le **pont réel du modèle**, échantillonné par
`_deckProfile()` — un seul chiffre pour tout le navire ne suffit pas, la Roter
Löwe portant son château arrière neuf mètres au-dessus de son maître-bau : les
basses voiles traversaient la coque.

**Ferler prend la toile, pas les espars.** `setTrim` n'a jamais touché au groupe
entier, seulement à `this.canvases`. Un navire à sec de toile garde ses vergues en
croix et sa bôme en place — et pour un modèle importé, masquer le groupe lui
arracherait son gréement, puisque ses propres vergues y vivent désormais.

**Et elle se roule maintenant, au lieu de disparaître.**

La fraction de toile établie vit dans le **solveur** et non dans le modèle, et
c'est tout le point : une voile qu'on rentre n'est pas une animation avec une
force posée à côté, c'est **moins de surface en l'air**. Portée comme une
fraction, la pression aérodynamique est simplement multipliée par elle — moitié
de toile, moitié de poussée — et l'image ne peut pas diverger de la physique
puisqu'il n'y a qu'un seul nombre. Le modèle le lit pour savoir jusqu'où
enrouler le tissu. Mesuré sur la goélette pendant un ferlage : 0,99 de toile
pour 55 kN, 0,66 pour 37, 0,33 pour 19, 0 pour 0.

**Une seule règle sert les trois gréements**, et c'est un heureux hasard de
l'ordre dans lequel leurs coins avaient été donnés : chaque sommet remonte vers
celui qui lui fait face **au rang zéro**. Ce rang est la têtière d'un carré, qui
se rassemble donc sur sa vergue comme le feraient ses cargues ; c'est le point
d'une voile aurique, qui descend sur sa bôme ; et c'est la ligne d'amure d'un
foc, qui court le long de son étai. Chacun fait ce que son gréement fait
réellement.

**ET ELLE PEND EN FESTONS, ce qui est ce à quoi on reconnaît un carré
ferlé.** Une voile carrée handée n'est pas un boudin : elle est cargue-fond sur
sa vergue puis saisie par des **garcettes** à intervalles, si bien qu'entre deux
garcettes la toile pend en baie. Cette rangée de baies se lit d'une encablure,
là où un rouleau d'épaisseur constante se lit comme un store enroulé. La
demande venait d'une photo, et elle avait raison.

Le compte est **dérivé** et non choisi : une garcette tous les trois mètres de
vergue, soit à peu près la portée d'un bras — la basse vergue de 10,2 m en
reçoit quatre, les huniers de 6,6 m en reçoivent deux, et rien n'est réglé par
navire. Les deux bouts du réglage comptent : le gonflement seul donne un ruban
ondulé, c'est le **pincement** qui dit « saisi ici ». La chute résiduelle passe
donc de 0,20 à 2,05 fois sa moyenne, et le bourrelet suit — la toile est la plus
épaisse là où il y en a le plus à ramasser.

Tout cela arrive avec le ferlage et s'en va avec lui : à voile pleine chaque
facteur vaut exactement un et l'arithmétique est celle d'avant. Relevé sur la
basse voile, chute colonne par colonne :

| | ondulation |
|---|---|
| toute dehors | 5,17 → 4,83 → 5,17 m, c'est sa **coupe** et rien d'autre |
| à mi-voilure | 2,4 à 2,8 m, elle commence à se ramasser |
| ferlée | **0,06** sous chaque garcette, **0,61** au creux des baies |

**Et il a fallu SEIZE colonnes**, ce qui est la seule chose que cela ait coûté.
Une baie demande quatre colonnes pour se dessiner en boucle plutôt qu'en
encoche, et surtout une garcette qui tombe **entre** deux colonnes n'est jamais
échantillonnée à son pincement : mesuré sur la grille de huit, trois festons sur
une vergue de dix mètres donnaient 0,58 m de creux contre 0,32 sous les
saisines, un rapport de deux là où le calcul en demande dix. Même aliasing que
les rides de la mer et que les étoiles — la forme était là et l'échantillonnage
ne pouvait pas la tenir. Le compte est en outre **rabattu sur un diviseur** du
nombre de colonnes, faute de quoi les garcettes retombent dans les trous.

Seules les voiles carrées prennent ces colonnes, puisqu'elles seules sont handées
ainsi. Mesuré : **0,051 ms par image** pour les cinq voiles du galion à huit
colonnes, **0,097 à seize** — un vingtième de milliseconde par navire à
gréement carré. L'aurique descend sur sa bôme et le foc court le long de son
étai ; ni l'un ni l'autre ne se ferle comme ça, et ils gardent le rouleau uni
jusqu'à ce qu'ils aient leur propre règle.

**Jamais tout à fait à rien, cependant.** Rabattue exactement sur le rang, la
voile n'a plus aucune surface et disparaît, là où une voile ferlée est un gros
rouleau de toile qu'on voit d'un mille. Il reste six pour cent de sa chute, et
un **bourrelet** qui est le plus épais quand elle est complètement rentrée —
l'inverse du creux, qui lui s'annule. Sans ce bourrelet le reste est un ruban
plat : géométriquement une voile ferlée, visuellement un bout de ruban adhésif.
Mesuré sur la Roter Löwe à sec de toile : 0,55 m de haut pour 1,47 m
d'épaisseur.

Elle est donc **toujours dessinée**. La masquer à zéro était l'ancien
comportement et jetait précisément ce pour quoi le reliquat existe — quatre-
vingt-un sommets par voile, il n'y a rien à économiser. Trois secondes pour
ferler ou établir, ce qui est vif pour un vrai équipage et juste pour une
interface qui ne doit pas paraître collée.

**L'échelle d'un modèle se prend sur la coque seule.** `_hullScale()` cherche le
maillage le plus volumineux — les espars sont longs mais n'enferment presque
rien — et ramène *sa* longueur à `spec.L`. Mesurer l'objet entier comptait le
beaupré et les vergues comme du navire : la coque de la Roter Löwe sortait à
47,5 m là où le solveur en flottait 60, et tout ce qui découle des dimensions
annoncées débordait d'autant. Le collier d'écume, qui suit l'ellipse
`spec.L × spec.B`, dépassait ainsi de 6,3 m à l'étrave et à l'étambot.

La largeur, elle, n'est pas ajustée : elle suit les proportions propres du
modèle. La Roter Löwe fait 15,6 m au maître-bau pour 14,5 m annoncés, donc le
collier passe un demi-mètre en dedans du bordé. Corriger cela demanderait une
mise à l'échelle non uniforme, qui déformerait la carène.

**Les voiles sont des surfaces, pas des feuilles.** Un quadrilatère plat a une
normale constante : une seule teinte sur toute la toile, et nulle part où la
lumière tourne — ça lit comme du carton, quel que soit l'éclairage.
`_sailSurface()` construit donc une grille sur les quatre coins et la pousse le
long de sa normale. Le creux n'est pas figé dans la géométrie :
`setSailShape()` le règle à chaque image sur `sailLoad`, la pression que le
solveur calcule **déjà** pour propulser le navire. La voile se gonfle donc en se
bordant et se vide dès qu'on choque, sans seconde règle à tenir en accord avec
la première. Le champ `belly` d'une fiche est le creux en mètres à pleine charge
(3,3 m sur la Roter Löwe, soit 16 % de la largeur des basses voiles).

**Mais le creux n'est pas une bulle.** La première écriture le posait en
`sin(πu)·sin(πv)` : creux au centre, nul sur les quatre bords. Deux fautes, et
elles valaient pour toutes les voiles du gréement.

Une voile pleine est creuse **bien en avant du milieu de sa corde**, aux quatre
dixièmes environ, parce que c'est là que l'air tourne — pas à la moitié. Et les
seuls bords plats sont ceux **réellement lacés** à un espar ou à un étai : le
point d'une voile carrée n'est tenu que par ses deux points d'écoute, donc elle
porte du creux jusqu'à la ralingue au lieu de s'y annuler. Épingler ce point à
zéro aplatissait exactement ce que l'œil lit d'un carré.

**Et le creux est HAUT.** Ce n'est pas là qu'un bord libre le mettrait, et ce
sont les écoutes qui en décident : les points d'un carré sont halés en bas et
**en dehors**, sur les bras de la vergue du dessous, si bien que le bas est
étiré le long d'un espar auquel il n'est pas lacé, tandis que la toile juste
sous sa propre vergue n'a rien qui la tire nulle part et sace. Vu de profil,
c'est tout son dessin — pleine en haut, plate en bas. Relevé rang par rang sur
la basse voile de la Roter Löwe, de la têtière au point : 0 · 2,68 · 3,26 ·
**3,27** · 3,10 · 2,77 · 2,28 · 1,63 · 0,82 m. J'avais d'abord mis le creux aux
deux tiers de la chute, soit exactement l'inverse.

**Et la chute suit ce dessin, elle n'en est pas exclue.** Épinglée à zéro, elle
restait une droite plate : de profil, le milieu de la voile décrivait bien sa
courbe et son bord ne décrivait rien. Or une chute de carré n'est tenue qu'à la
patte et au point d'écoute — elle chasse sous le vent avec le reste, en gardant
un peu plus de la moitié du creux du milieu. Relevé : 1,84 m à la ralingue
contre 3,27 m au milieu, à la même hauteur, et la même forme du haut en bas.

**Creuser n'est pas pendre**, et confondre les deux coûte cher. La première
écriture n'avait qu'un jeu d'indicateurs de bords libres, servant aux deux : en
libérant la chute pour qu'elle se creuse, elle se mettait aussi à **pendre**
sur sa propre longueur, ce qu'aucune chute ne fait — l'écoute la raidit. Un
point, lui, n'est tenu qu'à ses deux coins et pend pour de bon. Les deux
notions ont donc leurs propres indicateurs (`hangU0`…), retombant sur ceux du
creux quand on ne dit rien. Chaque voile déclare donc sa **coupe** (`cut`) — où
elle est creuse, lesquels de ses bords sont lacés — et les valeurs par défaut
redonnent l'ancienne bulle, si bien qu'une voile qui ne dit rien ne change pas.
Mesuré sur la frégate : 3,27 m de creux pour 3,3 annoncés, au point v = 0,75, et
encore 79 % du maximum à la ralingue.

**Le creux plein est un COUSSIN, pas une bosse.** Même corrigé de sa position,
un demi-sinus reste bombé à la couronne et s'affaisse doucement en tous sens.
De la toile pleine ne fait pas ça : elle est large et presque plate au milieu,
et ne tourne franchement que dans le dernier huitième de sa largeur, là où la
ralingue la retient. Le remède tient en un exposant **inférieur à un** appliqué
au profil : il soulève tout ce qui n'est pas sur les bords sans déplacer la
crête. À 0,55, un huitième en dedans de la ralingue passe de 0,38 à **0,59** du
creux maximal, et le quart de 0,71 à 0,83 ; le point de ralingue monte de 0,78 à
0,87. Le creux maximal, lui, ne bouge pas — 3,30 m mesurés pour 3,3 annoncés.

Les carrés sont à 0,55, la toile aurique et le foc à 0,80 : une voile à corne
travaille en aile et garde un profil d'aérofoil, ce n'est pas un oreiller.

L'exposant ne porte que sur la **corde**, jamais sur la hauteur. C'est une
forme de *section*, et appliqué aussi le long de la voile il aplatissait la
différence entre une têtière pleine et un point étiré — c'est-à-dire justement
le profil qu'on cherche à lire de profil.

**Et le point s'affaisse.** La toile entre deux points d'écoute est plus longue
que la droite qui les joint : elle sourit entre eux, nulle aux coins qui sont
raidis. C'est la ligne qu'on lit sur un carré avant toute autre, et un point
réglé à la règle trahit une voile dessinée plutôt qu'enverguée. L'affaissement
ne porte que sur les bords **libres** — une chute est raidie par son écoute,
elle ne pend pas — donc la brigantine de la goélette, lacée sur trois bords,
n'en a aucun, tandis que le foc et les carrés en ont un. Il grandit un peu avec
le remplissage — le creux prend du tissu en travers et le rend vers le bas —
mais il reste **bien inférieur à la coupe** : sur un carré il ne fait qu'adoucir
le rond de fond, il ne le retourne jamais en sourire. 0,33 m pleine charge
contre 0,61 m de rond, sur la Roter Löwe.

**Et surtout, les bords sont TAILLÉS CREUX.** C'est ce qui a manqué le plus
longtemps, et ce n'est pas un effet du vent : une voile carrée est envergée sur
une vergue droite, donc sa têtière est une droite, mais tous ses autres bords
sont coupés **en dedans** de la droite. Tant qu'ils restaient réglés à la
règle, on pouvait la creuser et l'ombrer autant qu'on voulait — elle se lisait
comme un rectangle avec un dégradé dessus. La silhouette est ce que l'œil lit
en premier.

**Creux, pas rond**, et c'est toute la différence. Les chutes rentrent à
mi-hauteur pour que la toile passe au clair des haubans et que la ralingue
travaille en ligne droite ; le point est taillé **vers le haut** au milieu —
le rond de fond d'une basse voile, qui existe pour dégager les étais et le mât
au-dessous. Les points les plus larges d'une voile carrée sont donc ses coins,
et sa taille est son endroit le plus étroit. Coupée dans l'autre sens, elle
gonfle entre ses espars comme une taie d'oreiller sur un fil — c'est
exactement ce que donnait la première version, et le tracé de l'utilisateur
l'a dit du premier coup.

Étant affaire de **coupe** et non de pression, tout cela est cuit dans la
géométrie de base : molle, elle garde sa forme. Nul aux pattes, où la vergue la
tient, nul aux points d'écoute halés dans leurs coins. Mesuré sur la Roter Löwe
(vergue de 20,3 m) : chute creusée de **1,0 m** à mi-hauteur, point relevé de
**0,61 m** au milieu par rapport à la ligne de ses écoutes.

Attention, le creux de chute se **compose avec l'effilement du point** (0,86 de
la vergue) : la demi-largeur ne descend pas de la patte au point, elle tombe de
10,17 à 8,27 m puis **remonte** à 8,75. Une valeur de coupe qui paraît
raisonnable dans l'absolu peut être annulée ou doublée par une autre règle de
forme, et seul le relevé du bord réel le dit.

La grille est passée de 8×6 à 8×8 : la forme intéressante est désormais celle
qui court le long de la voile, et six rangs rendaient le creux bas en facettes.

Et à **16×8 pour les carrés seuls**, les festons du ferlage l'ayant exigé —
voir plus haut.

**Ombres portées, et surtout pas de SSAO.** Le SSAO réclame une passe de
profondeur, que three rend avec un matériau de substitution. Or la mer est
déplacée dans son propre vertex shader : la substitution la dessinerait plate,
et l'occlusion serait fausse exactement au contact coque/eau, là où l'œil va
d'abord. Une ombre portée n'exige aucune passe de ce genre et donne davantage :
la toile qui assombrit le pont, la coque qui ombre son propre côté sous le vent,
un mât qui raye la voile derrière lui. Seul le navire y participe — la mer ne
projette ni ne reçoit, pour la même raison. La boîte d'ombre suit le navire
(`aimSun`) : laissée à l'origine, il en sort en une minute et ses ombres
s'arrêtent net. Et `shadow.normalBias` compte plus que `bias` ici, la toile
étant d'épaisseur nulle.

**L'écume suit l'empreinte réelle, plus une ellipse.** `Naval.HULL_GLSL` est
partagé par la mer et la passe d'écume, pour qu'elles ne dessinent jamais deux
navires différents. Il rend une distance **signée, en mètres**, à la vraie
flottaison : demi-largeurs relevées station par station sur le navire lui-même
(son propre maillage si c'est un modèle, `hull-lines.js` s'il est procédural),
portées dans une texture d'une seule ligne, puis distance par la formule de la
boîte. L'ellipse `spec.L × spec.B` passait jusqu'à 2 m en dedans du bordé sur la
moitié de la longueur, et 3,8 m au tableau arrière, où elle s'est effilée en
pointe alors que la coque fait encore près de quatre mètres.

Trois détails qui ont chacun coûté un aller-retour :

- **La bande de mesure va de sous la flottaison au haut de la préceinte**, pas à
  la flottaison seule. Il faut tracer la ligne que l'**œil** lui voit faire dans
  l'eau : une coque évasée surplombe sa propre flottaison — 6,35 m contre 7,7
  sur la Roter Löwe — et un contour pris à la flottaison exacte se dessine sous
  ses propres œuvres mortes, où il devient invisible.
- **Le profil doit être fairé.** Une coque .glb ne porte que quelques centaines
  de sommets : répartis sur soixante-quatre stations, la plupart n'en reçoivent
  qu'un ou deux et le maximum par station ressort en facettes — un collier en
  dents de scie. Trois passes d'un noyau 1-2-1 suffisent.
- **La carène se borne sur son corps de flottaison** (`uHullEnds`), pas sur la
  demi-longueur hors-tout : l'étrave s'élance au-dessus, la voûte surplombe. Pris
  à `spec.L/2`, il restait un filet d'écume filant devant l'étrave, là où le
  profil s'était déjà annulé.

**L'écume ne fabrique pas de lumière, elle en renvoie.** Sa couleur était une
constante presque blanche, `vec3(0,92 0,96 0,98)`, si bien qu'elle était aussi
claire à minuit qu'à midi : un sillage de nuit sortait **lumineux**, seule chose
de l'image à s'éclairer toute seule. De l'écume est blanche parce qu'elle
DIFFUSE, donc elle ne peut jamais être plus claire que ce qui l'éclaire.

Deux termes, et il faut les deux. Le terme de **ciel** est la lumière qui fait
le travail, et il porte déjà l'heure, la saison et le gros temps, `setSun`
reconstruisant la couleur d'horizon chaque fois que le soleil bouge. Le terme
d'**eau** est là parce que la mer garde un pigment **constant** — `uDeep` et
`uShallow` ne suivent pas le soleil — de sorte qu'en n'éclairant que l'écume on
la rendait plus **sombre** que l'eau sur laquelle elle repose, ce qui est le
même défaut retourné. De l'écume est claire *par rapport à la mer d'en dessous*,
à toute heure.

À midi la somme tombe à un centième de l'ancienne constante, donc le plein jour
ne bouge pas ; à minuit c'est une traînée pâle sur une mer sombre. Piège de
mesure au passage : comparer la contribution de l'écume d'une session à l'autre
ne veut rien dire, le champ d'écume étant persistant — quarante secondes de
sillage accumulé contre plusieurs minutes ne donnent pas le même nombre de
pixels blancs, et l'on croit avoir cassé ce qu'on vient de régler.

**Le collier s'éteint vers l'intérieur, la gerbe vers l'arrière.** Deux règles
distinctes, et il ne faut pas les confondre. Le collier se fond *sous* le bordé
(`smoothstep(-1.3, -0.1, gapM)` plutôt qu'un `step`) : l'écume s'accumule contre
la coque et s'épuise dessous, alors qu'un bord interne net se lit comme un
autocollant posé sur l'eau. La gerbe d'étrave, elle, n'est pas une bande sur la
moitié avant mais une moustache au brion et deux ailes balayant vers l'arrière,
dont la crête s'écarte comme la **racine carrée** de la distance en arrière de
l'étrave — c'est ce qui lui donne son aile parabolique au lieu d'un bord droit.
Elle exige de l'erre, et sa rampe `fast` sature à deux fois la vitesse de `way` :
la gerbe continue donc de grossir quand le collier a fini de croître, ce qui fait
la différence entre un navire poussé et un navire simplement à flot. Les ailes
sont déposées **aussi** dans le champ d'écume, sinon le V resterait soudé à
l'étrave au lieu de rester dans l'eau derrière elle.

**`half` est un mot réservé en GLSL.** L'utiliser comme nom de variable ne fait
pas échouer un seul terme : **tout** le fragment shader refuse de compiler, la
mer disparaît et on voit le dôme de ciel à sa place. `node --check` ne peut rien
y voir, le shader n'étant qu'une chaîne de caractères pour lui.

**Le ciel éclaire, il ne fait pas que décorer.** Le navire était éclairé par un
`HemisphereLight` à deux couleurs qui tenait lieu d'un ciel que la scène
dessinait déjà correctement quelques mètres plus loin. `_buildEnvSky()` rend ce
ciel dans une cubemap, la préfiltre et la pose en `scene.environment`. Il appelle
le même `navalSky` et **partage les objets uniformes du dôme** — pas des copies —
donc il n'y a toujours qu'un seul ciel et rien ne peut diverger de ce qu'on voit
au-dessus. Le disque solaire en est volontairement exclu : la `DirectionalLight`
le représente déjà, l'y cuire l'éclairerait deux fois.

Trois choses en découlent. L'ambiante devient **directionnelle** — mesurée à
+6 % du côté du soleil, là où un `HemisphereLight`, qui n'utilise que la
composante verticale de la normale, donne rigoureusement zéro. Le **spéculaire
image** apparaît, ce qu'une lumière hémisphérique ne sait pas produire du tout,
et c'est le gain le plus visible. Et l'ambiante suit désormais seule l'élévation,
la nuit et l'éclair, au lieu d'une approximation réglée à la main. En
contrepartie `_hemiBase` a été divisé par trois : à son ancienne force
l'hémisphère comptait cette lumière une seconde fois et aplatissait justement
l'ombrage directionnel que l'environnement apporte.

Le préfiltrage coûte quelques millisecondes, et le curseur du soleil tire un
événement par pixel de glissement : `refreshEnvironment()` est donc **bridée**,
et un rafraîchissement sauté est rattrapé dans `render()`.

**L'explosion tient dans l'ORDRE des choses, pas dans la boule de feu**
(`explosion.js`, touche `K`). Un éclair parti en un dixième de seconde, une
boule de feu qui grandit vite et meurt en une seconde, de la fumée qui monte et
s'étale pendant vingt, des débris sur de vraies paraboles. Tout ensemble, c'est
un feu d'artifice ; échelonné, les parties lentes survivant aux rapides, c'est
un navire qui saute. Ce sont les **débris** qui donnent l'échelle : l'œil lit la
hauteur qu'ils atteignent et le temps qu'ils mettent à retomber, et aucune boule
de feu ne remplace ça.

**Trois charges, pas une.** Elles partent à quelques mètres et quelques
dixièmes de seconde d'écart — au milieu d'abord, puis sur l'avant, puis bien sur
l'arrière — ce qui se lit comme un navire qui se disloque là où une seule grosse
boule se lit comme une bombe posée à côté de lui. Chacune porte **son propre
éclair**, si bien que la lumière bégaie aussi, et c'est l'essentiel de l'effet.
Les points sont placés dans **son** repère puis portés dans le monde, donc ils
suivent sa gîte et son assiette pendant qu'elle roule. Les écarts sont **bornés
en mètres autant que mis à l'échelle**, sans quoi une goélette exploserait hors
de sa propre coque.

**Les débris de bois sont de VRAIS maillages, pas des sprites.** Un point
lumineux se lit comme une étincelle quelle que soit sa couleur ; ce qui le fait
lire comme un navire qui se disloque, c'est que les morceaux sont **opaques**,
éclairés par le même soleil que la coque, et qu'ils **culbutent** bout sur bout.
Une seule géométrie de planche et un seul matériau pour tous — il n'y a aucune
raison de construire deux douzaines de boîtes par explosion pour les jeter trois
secondes plus tard — et ce matériau reçoit la brume comme le reste, donc les
éclats respirent le même air que le bordé dont ils viennent.

Une conséquence gratuite : le bois étant opaque et la mer aussi, une planche qui
retombe **disparaît d'elle-même derrière l'eau**. La gerbe ne coûte rien parce
qu'il n'y a pas de gerbe à écrire.

**Une LUEUR LOCALE, et non plus la foudre.** Le premier jet réemployait
`strike()`, au motif qu'une soute qui saute éclaire pont, toile et ciel
ensemble. C'était faux, et signalé à l'usage : `strike()` blanchit **tout le
ciel**, ce qui est juste quand l'orage est au-dessus d'elle et absurde pour une
explosion, laquelle est une lumière *quelque part*. Le ciel qui vire d'un bloc
se lisait comme un éclair mal placé, et il fallait un instant pour comprendre
que le navire venait de sauter.

Chaque charge porte donc sa propre lampe jaune, bornée à cinquante-cinq mètres :
ce qui doit s'éclairer est elle et l'eau autour d'elle, pas l'horizon. Réserve
fixe commutée par l'intensité, pour la raison mesurée du côté des canons — une
lumière ajoutée puis retirée fait recompiler tous les matériaux, et trois
charges en feraient six recompilations. Vérifié : le flash de ciel reste à
**zéro** avant comme après, et la coque, le pont et la toile prennent le jaune.

Tout le reste est en billboards. Du feu volumétrique serait des jours de travail pour un
événement de trois secondes, et une douzaine de sprites bien cadencés se lisent
mieux qu'un mauvais volume. Les textures sont dessinées sur un canvas, une page publiée ne pouvant
pas aller chercher d'image.

**Et le naufrage qui suit n'est pas un cas particulier** : `blowUp()` ouvre
simplement les cinq compartiments d'un coup, souffle les pompes et l'admet déjà
à un cinquième — une explosion ne la fait pas commencer à embarquer depuis zéro.
Elle coule en quelques dizaines de secondes parce que l'arithmétique le dit, pas
parce qu'on l'a décidé. Mesuré sur la frégate : 1 206 t à 0,8 s, 1 987 t à 9 s.

**L'échouage se sonde en TROIS points, jamais sur les sondes de carène.**
`heightAt` balaie la grille des îles ; l'appeler trois cents fois par sous-pas
coûterait plus cher que tout le solveur réuni. L'étrave, le milieu et l'étambot
suffisent à tout ce qui compte : elle s'ensable par l'avant sur une plage en
pente douce, pivote sur un haut-fond qui la prend par le travers, ou s'assoit
sur une quille droite.

Le fond répond comme un **ressort raide amorti, appliqué AU point de contact** —
elle se soulève, gîte et embarde exactement comme la géométrie l'impose. Rien ne
décide qu'elle est échouée ; les forces le font, comme rien ne décide qu'elle
flotte. Et talonner en vitesse l'**ouvre au point de choc**, ce qui referme
enfin la boucle : l'échouage devient une vraie cause de l'envahissement déjà
écrit, au lieu du raccord manquant qu'il était.

Mesuré sur le chaland lancé à 8,7 nds sur une plage : premier contact à 38,6 s à
4,6 nds, une voie d'eau ouverte au même instant, arrêt complet en quinze
secondes, puis 12 t embarquées en deux minutes. La pénétration **oscille entre 0
et 0,25 m** au rythme de la houle — elle tape sur le banc, ce que je n'avais pas
prévu et qui tombe juste.

**BORD À BORD, et c'est l'échouage écrit une seconde fois.** Un ressort raide
très amorti appliqué **au point de contact**, donc elle est repoussée, embardée
et couchée exactement comme la géométrie l'impose. Rien ne décide que deux
navires se sont touchés : les forces le font, comme rien ne décide qu'elle
flotte. Lui donner une règle à elle aurait garanti qu'elle contredise le fond
la première fois qu'on la pousse sur un haut-fond à couple d'un autre.

**L'essai porte sur la vraie flottaison, pas sur une ellipse.** Son contour est
échantillonné station par station des deux bords et chaque point demande s'il
est dans le contour de l'autre — le même `halfB()` dont sont bâties la grille de
sondes et la coque visible, donc ce qui la repousse est ce qu'on voit se
toucher. L'histoire de l'écume est l'avertissement : l'ellipse passait jusqu'à
deux mètres en dedans du bordé, ce qui est invisible sur un collier d'écume et
ferait ici deux mètres d'interpénétration.

**C'est un essai en PLAN, sans hauteur**, et c'est réfléchi plutôt que
paresseux : deux coques qui se rencontrent flottent chacune à sa flottaison,
donc le contact intéressant est toujours bord contre bord. Un essai en trois
dimensions coûterait plusieurs fois plus cher pour attraper un cas — un navire
chevauchant l'autre — que ce modèle ne sait pas produire.

**Les deux le font l'une contre l'autre**, donc la paire s'écarte sans que
personne n'arbitre : chacune paie ses propres contacts et Newton est satisfait
par symétrie plutôt que par comptabilité. La liste des voisins est **donnée à
chaque image** et jamais retenue, pour la raison qui a déjà coûté un bug ici —
une référence gardée se périme dès que la flotte change.

**Et abordée en vitesse, elle S'OUVRE**, exactement comme sur la roche : une
coque lancée dans une autre ne rebondit pas, et le trou est là où elle a frappé.
L'abordage devient donc une vraie cause de l'envahissement déjà écrit, sans une
ligne à lui. Mesuré, deux galions de 240 t par le travers :

| vitesse au choc | 1,5 m/s | 3,45 | 5,77 |
|---|---|---|---|
| pénétration | 0,49 m | 0,77 | 1,13 |
| gîte maximale | 6° / 7° | 8° / 9° | 11° / 12° |
| voies ouvertes | **aucune** | 1 de chaque bord | 1 de chaque bord |

Un accostage ne fait donc rien, et c'est voulu — on peut se ranger à couple. Le
transfert de quantité de mouvement sort tout seul : lancée à 2,18 nds elle tombe
à 0,67 et l'autre part de zéro à 1,15, puis elles se repoussent jusqu'à onze
mètres.

Le coût est en N² mais dérisoire, la phase large rejetant tout couple plus
éloigné que la somme des demi-longueurs : **0,009 ms par paire**, soit 0,25 ms
à huit coques dans le pire des cas, contre 10,7 ms de solveur.

**Le naufrage n'est pas scripté, c'est du poids mal placé.** Méthode du *poids
ajouté* : l'eau embarquée est une masse, à l'endroit où elle repose. Rien ne
décide qu'elle coule — elle sombre quand ce poids dépasse ce que sa carène peut
déplacer. La grille de sondes fait déjà tout le reste : assiette, gîte et
enfoncement en découlent sans une ligne de plus.

Les compartiments (`Naval.Config.NCOMP`, cinq) sont découpés **sur les sondes
elles-mêmes**, donc leur capacité est du vrai volume de coque, mesuré sur le
même plan de formes que tout le reste. Ils sont numérotés **depuis l'étambot** :
le compartiment 0 est à l'arrière, le 4 à l'étrave.

Trois choses portent tout le comportement, et il ne faut pas les défaire :

- **L'entrée d'eau suit Torricelli**, `v = √(2gh)`. Un trou profond emplit bien
  plus vite qu'un trou près de la flottaison, et surtout la charge `h` **grandit
  à mesure qu'elle s'enfonce** : c'est l'emballement qui noie réellement un
  navire, et il est gratuit.
- **L'envahissement par le pont** prend le relais dès qu'un livet passe sous
  l'eau. C'est presque toujours lui qui achève, pas la voie d'eau initiale.
  Mesuré sur la Roter Löwe, une seule brèche de 0,30 m², pompes arrêtées : 5
  minutes pour 566 t et 3° d'assiette, puis les deux dernières minutes la font
  passer de −9° à −58° et elle sombre à 12,1 min, par l'arrière.
- **L'eau se met au fond, et court à la bande basse.** Son centre monte avec le
  remplissage, donc un fond d'eau est du lest et la raidit, tandis qu'une masse
  haute la chavire. Et un compartiment **à moitié plein** a une carène liquide
  (`4f(1−f)`, nulle à vide comme à plein) qui glisse sous le vent et combat le
  redressement — c'est pourquoi remplir complètement un compartiment est un vrai
  remède.

**Les pompes se dosent à la mesure, pas au calcul**, l'entrée d'eau dépendant de
la profondeur à laquelle elle finit par s'asseoir sur son trou. Réglées à
`6·10⁻⁵ × volume de coque` pour que **une** voie d'eau soit rattrapable et deux
non : sur la frégate, 0,343 t/s de pompes contre 0 t embarquée à une brèche,
0,334 t/s de gain à deux, 0,705 à trois. C'est délibérément généreux au regard de
l'histoire — une pompe à chaîne faisait de l'ordre d'une tonne par minute, et les
navires coulaient précisément parce qu'on ne suivait pas ; à cinq fois ça, le
contrôle des avaries devient une décision plutôt qu'une formalité.

**La carène liquide est une remontée du centre de gravité, pas un déplacement de
centroïde.** Je l'avais d'abord écrite comme de l'eau qui court à la bande basse,
proportionnellement à la gîte. C'était **faux, pas seulement faible** : mesuré sur
la goélette, ça déplaçait le centre de gravité de 2 cm et produisait 5 t·m, deux
ordres de grandeur sous le moment redresseur. Pire, l'eau de fond abaissait G de
23 cm — elle s'envahissait, devenait plus **raide**, et sombrait bolt upright.

Le vrai effet est la correction classique : une **remontée virtuelle de G** de
`Σ(ρ·i)/Δ`, où `i = l·b³/12` est le moment quadratique de la surface libre. Elle
ne dépend **pas de l'angle de gîte**, ce qui est précisément ce qui la rend
mortelle — le navire est déjà instable avant d'avoir donné de la bande. Et elle
va comme le **cube de la largeur**, d'où le cloisonnement longitudinal des vrais
navires : un compartiment large est pire que trois étroits contenant la même eau.

Mesuré sur la goélette (GM à sec 1,04 m), `freeSurface` à 1 contre 0 :

| situation | sans | avec |
|---|---|---|
| à la cape, voiles ferlées, force 6,5 | 11,9° | 14,7° |
| pressée sous voiles, force 7,5 | 14,9° | **40,7°** |

La remontée de G atteint 0,92 m contre 1,04 m de GM. Sous voiles elle **triple
la gîte**, se couche à 40°, met son livet sous l'eau et sombre en 1,7 minute.

Ce qu'on n'observe toujours pas, et qu'il ne faut pas confondre avec un défaut :
elle ne se **retourne** pas au-delà de 90°. Passé 40° son pont est sous l'eau et
l'envahissement par le pont l'achève en quelques secondes — elle se noie avant
d'avoir le temps de chavirer, ce qui est le sort réel de la plupart des navires
envahis.

**On voit à travers l'eau, et l'extinction est par canal** (`underwater.js`). La
mer était opaque : un navire qui sombrait ne s'enfonçait pas, il était *coupé* à
la flottaison. La mer ne peut pas savoir seule à quelle distance derrière elle se
trouve la coque, donc le navire est redessiné une fois, seul, dans ses propres
matériaux, vers une cible couleur + texture de profondeur ; la mer lit les deux
et en déduit l'épaisseur d'eau traversée.

Il est déjà isolé sur sa couche de rendu pour l'occlusion, donc cela coûte **un
seul dessin de plus d'un seul modèle** — et surtout la mer reste un objet opaque
testé en profondeur, au lieu de devenir transparente avec tous les problèmes de
tri que cela entraînerait.

L'extinction est **par canal** (`uAbsorb`, 0,34 / 0,13 / 0,085 par mètre) : le
rouge disparaît en trois mètres, le bleu porte trois fois plus loin. C'est toute
la raison pour laquelle ça se lit comme de l'eau et non comme du brouillard — un
coefficient gris unique la ferait virer au gris, ce que fait la brume, pas la
mer.

**La perte d'un navire se mesure sur son point le plus haut.** Passé dix mètres
sous la surface elle disparaît, la caméra bascule en vue fixe et un message le
dit. Mais le seuil porte sur le **sommet** du navire, jamais sur l'origine de sa
coque : elle sombre presque à la verticale, l'étambot le premier, et descend
l'étrave et le gréement encore en l'air. Relevé sur la Roter Löwe, avec l'origine
à dix mètres sous l'eau il restait **trente-trois mètres de mâture dressés au-
dessus de la mer** — l'escamoter là aurait fait s'évanouir ses mâts en pleine
vue. Sur le point le plus haut, elle est réellement partie. Coût : une boîte
englobante par image, et seulement une fois `foundered`.

La caméra fixe reçoit alors la **hauteur de la mer** comme cible (`plant(aimY)`)
et non la position de l'épave : braquée sur cette dernière elle plongerait du nez
sur cent mètres d'eau vide au lieu de tenir le carré de mer qui l'a engloutie.

**Un attribut `hidden` ne suffit pas si la classe pose un `display`.** La ligne
d'avarie porte `.allure`, qui vaut `display:flex` et l'emporte sur la règle
`[hidden]` du navigateur : elle restait donc affichée après réparation, à
annoncer « SOMBRÉ » sur un navire intact. Il faut une règle
`.allure.damage[hidden]{display:none}` explicite. Le piège vaut pour toute ligne
qu'on masque par attribut.

**Sous l'eau, trois choses changent, et en oublier une trahit tout.**

D'abord **la mer avait un seul côté**. Le plan était en `FrontSide` : vu d'en
dessous il n'existait pas, et on voyait le ciel au travers. Il est en
`DoubleSide`, et `gl_FrontFacing` décide de ce qu'on dessine — un nageur et une
vigie sont servis par le même matériau. Vue de dessous, la surface n'est pas une
mer mais un **plafond** : rien du travail de reflet ne s'y applique, le Fresnel
joue à l'envers, et ce qu'on voit est un couvercle argenté avec le soleil qui
brûle au travers en une tache.

Ensuite **le dôme de ciel devient l'eau profonde**. Le laisser dessiner le ciel
mettrait un horizon à l'intérieur de la mer.

Enfin **l'extinction remplace la brume**, par canal, avec l'absorption que la
mer emploie déjà pour montrer une coque coulée à travers la surface — mais **à
quatre dixièmes**. Ce n'est pas un truquage : `uAbsorb` a été mesuré pour un
regard qui traverse la surface *deux fois*, en descendant vers la coque et en
remontant vers l'œil. À l'horizontale il ne la traverse qu'une, donc la même eau
porte deux fois plus loin. À pleine force, une coque à vingt mètres n'existait
tout simplement pas.

**La fenêtre de Snell fait tout le travail.** Vu d'en dessous, l'hémisphère
entier du ciel se comprime dans un cône de **97°** — tout, d'un horizon à
l'autre, dans ce seul disque clair. Au-delà, l'angle dépasse la réflexion totale
et la surface devient un miroir. C'est ce cercle et son bord argenté qui font
lire une image comme étant *sous* l'eau plutôt que simplement bleue. La fonction
`refract` fait le calcul et rend le vecteur nul en réflexion totale, ce qui est
exactement le test d'être hors de la fenêtre.

**Deux ordres à ne pas intervertir.** La branche « vue de dessous » doit passer
**avant** le mélange du navire : écrite après, elle l'écrasait, et aucun
bâtiment n'était visible depuis l'eau — c'était précisément le symptôme. Et ce
qui sépare la surface de la coque est de l'**eau** quand on la regarde d'en haut
mais de l'**air** quand on la regarde d'en bas : un bateau vu par la fenêtre
n'est pas derrière des mètres de mer, il est simplement au-dessus. L'atténuer
comme s'il était noyé le teintait en bleu dès dix mètres de franc-bord, d'où
l'absorption ramenée à 2 % dans ce cas.

Le seuil se prend contre **la vague à la caméra**, pas contre le niveau moyen :
une crête qui passe met une vigie de pont bas sous l'eau puis l'en sort, et
c'est justement le moment qui vaut d'être vu. Un demi-mètre de lissage évite que
le passage clignote quand la surface chasse autour de l'objectif.

**La réfraction doit reporter l'ondulation, pas effacer l'épave.** Sans elle la
coque immergée se lit comme un autocollant vu à travers une vitre plate. Mais
c'est un effet qu'on rate par excès bien plus facilement que par défaut, et la
première version l'a raté.

**L'échelle se dérive, elle ne se devine pas.** Snell dévie le rayon d'environ
`(1 − 1/n)` de la pente, soit un quart pour l'eau de mer, et le déplacement à la
coque vaut cet angle multiplié par la profondeur. Plutôt que de deviner comment
cela tombe à l'écran, le décalage est construit en **mètres monde puis
reprojeté** : rapport d'image, champ et orientation de la caméra se règlent alors
tout seuls. `uProj` est recopié à chaque image, la passerelle et la caméra fixe
zoomant l'objectif plutôt que de bouger.

La première écriture était un bricolage en espace écran avec un coefficient de
**26 — cent fois trop grand**. Le décalage dépassait la moitié de l'écran, presque
tous les échantillons manquaient la coque, et le repli ci-dessous la
reconstruisait à partir de pixels sans rapport. Ça ne se lisait pas comme de
l'eau mais comme du bruit détruisant précisément ce qu'on cherchait à regarder.
Corrigé, `uRefract` vaut **1 = la physique**, et donne 3,3 px de déplacement à
60 m par mer 3 : une ondulation, pas une bavure. Écart moyen tombé de 46 à 27
sur 255.

Deux points de méthode. Il faut un **premier échantillon droit** rien que pour
connaître l'épaisseur avant de savoir de combien dévier — d'où deux lectures de
profondeur. Et si le rayon dévié tombe sur quelque chose situé **devant** l'eau,
il a atteint les œuvres mortes : on retombe alors sur l'échantillon droit, sinon
le pavois se retrouve étalé sur la mer.

Enfin le terme de profondeur **sature à cinq mètres**. Un rayon plus long
continue en toute rigueur de s'écarter davantage, mais au-delà de quelques mètres
le déplacement suffit à effacer sa silhouette — et elle doit rester lisible
pendant qu'elle s'efface. C'est un écart délibéré à la physique, au profit de ce
qu'on est venu voir.

**Le collier d'écume doit s'éteindre avec elle.** `uShipAfloat` tombe de 1 à 0
entre 78 % et 95 % d'immersion et multiplie le collier, la gerbe et le dépôt dans
le champ d'écume. Sans lui, un anneau d'écume reste à la surface au-dessus d'une
épave, avec rien dessous — et le champ étant persistant, il y traînerait encore
une trentaine de secondes.

**L'AIR QUI REMONTE D'UNE ÉPAVE N'EST PAS UN EFFET POSÉ SUR LE NAUFRAGE, c'est
l'envahissement lu de l'autre côté** (`wreck-air.js`). Chaque mètre cube qui
entre en chasse un d'air, et le solveur savait déjà combien entre et où. Il le
crédite désormais au compartiment (`c.air`) — **à condition que la sortie soit
sous l'eau** : une coque percée bas dont le pont est encore sec expire par ses
écoutilles, dans l'air, et rien ne se voit sur la mer. Pure comptabilité, rien
n'est relu par la physique, et l'échantillon du sommet de compartiment est
celui que l'envahissement par le pont prenait déjà, simplement pris plus tôt.

**Et il met le temps de remonter**, ce qui est l'essentiel — la même idée que le
son d'un canon. Une poche monte à la vitesse de sa propre taille, la calotte
sphérique de Davies-Taylor, `U ≈ 0,71·√(g·r)` : un mètre cube fait deux mètres
par seconde, donc depuis une épave posée par soixante-six mètres il arrive
**quarante secondes** après être parti. Relevé sur le pirate sabordé en eau
profonde, poche piégée alors à 5 % : premières gerbes à 3 s, coulé à 9,6 s,
**dernières à 140 s** — le
bouillonnement dure deux minutes après qu'elle a disparu, et s'éteint au lieu de
s'arrêter. Le panache s'élargit en montant (`0,4 + 0,12·profondeur`), si bien
qu'un bouillon au-dessus d'une épave profonde est large et lent.

L'air sort **par bouffées** et non en filet, la taille suivant le débit : une
cale qui embarque en grand crache de grosses poches plusieurs fois par seconde,
les dernières poches d'une épave remontent une à une, à des secondes d'écart.
Ce qui arrive passe par deux machineries existantes — une gerbe basse dans
`splash.js`, et un **bouillon** dans le champ d'écume persistant, qui est ce qui
reste : cœur plein, bord déchiré en cellules par deux octaves de bruit qui
dérivent l'une contre l'autre, et le champ le porte et l'efface comme toute
écume. Il n'a rien à voir avec la houle, donc aucune parité à tenir entre les
trois calculateurs.

**Une poche piégée, et ses chiffres sont CHOISIS.** Les compartiments se
remplissent jusqu'à leur plus haute sonde ; une fois pleins, l'envahissement n'a
plus rien à admettre et le bouillonnement s'arrêterait net à l'instant où elle
passe sous l'eau — exactement à l'envers. Ce que les compartiments ne décrivent
pas est l'air sous les barrots, dans les châteaux et les coffres. Huit pour cent
du volume de coque, rendus sur vingt secondes tant que son point le plus haut
est noyé : rien dans ce modèle ne permet de les mesurer, et c'est écrit sur place.

**Le bouillon caché par son propre bordé, et c'est la mesure qui l'a trouvé.**
Posé à l'aplomb de la sortie, sur l'axe du pont, il était invisible pendant toute
la descente : le pirate était immergé à 98 % pour le solveur, seize bouillons en
place au-dessus de son pont, et pas un ne se voyait — son modèle se tient trois
mètres plus haut que ses lignes de solveur, l'écart déjà consigné pour le
boulet. Une part des bouffées est donc portée **à sa flottaison, par le travers
de la sortie** — l'air sort aussi par les sabords et la lisse — et cette part
s'éteint quand la profondeur dépasse son creux et que le panache se referme sur
elle.

Coût : **0,002 ms** de processeur par image ; la passe d'écume, synchronisée par
une lecture de pixel, 0,87 ms sans bouillon contre 1,28 avec seize grands
bouillons, dans un bruit de 0,5 à 1,8 — et seulement pendant un naufrage. Le
champ ne couvre que 620 m autour du navire commandé : une conserve qui sombre
plus loin gerbe, mais ne laisse pas de remous.

**LE DERNIER SOUPIR, demandé comme du théâtre, et il a une vraie cause** — seule
raison pour laquelle il a le droit d'être là. L'air des châteaux et de sous le
pont supérieur n'a nulle part où aller tant qu'un bout en reste hors de l'eau ;
à l'instant où le plus haut est noyé, tout part d'un coup. Ce n'est donc pas une
gerbe posée par-dessus : c'est **la poche piégée**, dont six dixièmes partent en
un souffle au lieu de s'égoutter, à l'endroit qui a sombré en dernier. Une
grande gerbe puis deux plus petites le long de sa longueur, à 0,2 et 0,45 s :
une seule se lit comme une chose qui crève la surface, l'échelonnement comme un
navire qui lâche tout. Tirées avec le facteur `jet` de `splash.js`, celui du cas
étroit et violent.

**Lu sur la coque VISIBLE, pas sur celle du solveur**, pour la raison du
paragraphe précédent : `ship.shell` est le propre bordé du modèle découpé dans les
compartiments, et il part quand tous ses sommets sont à trente centimètres sous
la mer. Une coque procédurale n'en a pas, et là les compartiments du solveur
SONT ce qui est dessiné. Relevé sur le pirate sabordé : coulé à 9,6 s, **dernier
soupir à 10,5 s**, 32 m³ d'un coup, 831 gouttes en l'air la demi-seconde
suivante.

**Le panneau Flotte dit « coulé »** à la place de « 0 nds » : une épave n'a pas
de vitesse, elle a un sort.

**Une voie d'eau s'aggrave, elle ne se multiplie pas.** `worsenBreach()` double
l'aire du même trou à chaque appel et le laisse gagner les compartiments voisins
une fois passée une largeur de bordé, plutôt que d'ouvrir des trous indépendants :
une coque ne perce pas en cinq endroits à la fois, une couture cède et travaille.
Six pressions mènent la frégate au fond en 5,2 minutes, contre plus d'une
demi-heure avec des brèches séparées de taille fixe.

**Les étoiles vivent dans `navalSky`, donc la mer les reflète.** Elles sont
posées sur un réseau de cellules du **vecteur direction**, pas sur une grille de
latitude et longitude — celle-ci les entasserait aux pôles et laisserait une
calvitie au zénith. Elles sont ajoutées **avant** le mélange vers l'horizon, si
bien qu'elles s'amincissent dans la brume comme les vraies au lieu de flotter
par-dessus.

Une étoile plus étroite qu'un pixel est une tempête de scintillement garantie
dès que la caméra tourne : c'est le **même piège d'aliasing** que les rides de
la mer, et le remède est le même — être plus large que le pas d'échantillonnage,
pas plus brillant. D'où une vraie largeur et un `smoothstep`, jamais un point.

**La lanterne, ce sont DEUX lueurs pour deux métiers.** La proche est un sprite
de taille réelle, qui grossit quand on accoste et se lit comme un fanal pendu au
couronnement. La lointaine **ne s'atténue pas avec la distance**
(`sizeAttenuation:false`) : un fanal de taille honnête fait moins d'un pixel à
deux milles et disparaît, ce qui est exactement le contraire de ce à quoi sert
un feu. Celle-là est le repère de position, et tient quelques pixels quelle que
soit la distance.

**Le profil de pont est en dents de scie, il faut le lire au maximum.** Sur la
Roter Löwe il donne 18,4 puis 12,4 puis 5,3 m d'une station à l'autre, les cases
chevauchant ses galeries et ses rambardes ouvertes. Échantillonner **une seule**
station a fait tomber la lanterne dans un creux, cinq mètres sous son
couronnement et un peu trop en avant : elle la portait à l'intérieur de son
propre château arrière. La hauteur se prend donc au maximum sur la tranche
arrière, jamais à une station. Le même piège guette tout ce qu'on voudra poser
sur le pont d'un modèle importé.

Elle bat — deux sinusoïdes lentes déphasées, une seule se lirait comme une
pulsation régulière. C'est ce qui l'empêche de ressembler à un marqueur
d'interface plutôt qu'à une mèche dans une lanterne à corne. Sa texture est
**dessinée sur un canvas**, jamais chargée : une page publiée ne peut pas aller
chercher une image locale.

**ET LEUR NOMBRE ET LEUR PLACE SONT DANS LA FICHE** (`lanterns`), parce qu'un
galion en portait souvent trois au couronnement et qu'aucune règle de forme ne
peut deviner combien. Chaque entrée donne sa place dans SON repère — `x`/`z` en
mètres ou `xFrac`/`zFrac` en fractions du bau et de la longueur, ce qui suit le
navire quelle que soit sa taille — plus, si l'on veut, `y` en dur, `above` pour
la relever au-dessus du pont, `size` et `color`. **`y` absent veut dire « sur le
pont à cette station »**, lu sur le modèle par le même maximum sur une tranche
que ci-dessus, désormais généralisé à n'importe quelle station plutôt qu'au seul
arrière. Une fiche muette garde le feu de couronnement d'avant ; une **liste
vide** n'en porte aucun — qui déclare ses feux déclare tout ce qu'il porte, y
compris rien. Chacun a sa propre graine de scintillement, sans quoi trois feux
battraient ensemble et se liraient comme une guirlande. Relevé sur la Roter
Löwe : un feu à 7,79 m sur 12,24 m d'arrière, exactement où le placement
automatique le mettait, et trois feux quand la fiche en nomme trois.

**LES FENÊTRES S'ALLUMENT LA NUIT, ET RIEN N'EST PEINT DEUX FOIS POUR ÇA.** glTF
n'a pas de « texture de nuit » : il a une **carte émissive**, qui est exactement
la bonne chose — la part de la matière qui émet au lieu de renvoyer. Le modéliste
peint donc une image où seules les fenêtres sont claires, la branche dans le nœud
Émission de Blender, et l'exportateur la porte en `emissiveTexture`. Le jeu la
trouve seul (`_findNightGlow`) et ne règle qu'une chose : `emissiveIntensity`,
zéro le jour — une vitre au soleil ne luit pas, elle reflète — et pleine à la
nuit, sur le même `night` que les feux et le ciel.

Le **repli est le contrat des canons**, un mot dans un nom de matière
(`fenetre`, `window`, `vitre`, `glass`, `lamp`…) : la matière reçoit une émissive
chaude sans qu'aucune image n'ait à être peinte. C'est ce qui allume la Roter
Löwe aujourd'hui, dont le `.glb` porte une matière `glass` et aucune carte
émissive. Une carte émissive est **multipliée par la couleur émissive**, que
l'exportateur laisse noire quand le facteur est nul : sans le blanc posé ici, la
carte serait là et ne donnerait rien. Force réglable par `model.nightGlow` (1 par
défaut, 0 pour ne rien allumer). Relevé : `glass` à 0 le jour, 1 la nuit, et une
seule matière touchée quel que soit le nombre de maillages qui la partagent.

**ET ELLES S'ALLUMENT D'UN COUP, PUIS SONT SOUFFLÉES À L'AUBE** — demandé à
l'usage, et c'est ce que fait un équipage : on allume les fanaux quand il fait
nuit, on ne les baisse pas pendant une heure de crépuscule. `Naval.NIGHT` porte
les trois nombres, réglables dans `settings.json` : `glow` la force de
l'émissive, `lightAt` et `snuffAt` les deux seuils sur le `night` du stage
(0 au coucher, 1 dix degrés plus bas). **Deux seuils et non un**, la même
hystérésis que la ligne de bord de la barre automatique : avec un seul, un
soleil qui hésite à la limite ferait battre tout le bord. Relevé en balayant le
soleil de +2° à −8° et retour : rien jusqu'à −3,4°, plein feu dès **−3,5°**
(`night` 0,35), toujours allumé en remontant jusqu'à −2,6°, **éteint net à
−2,4°** (`night` 0,24).

**ET LA NUIT NE DOIT PLUS FAIRE UN À-COUP**, ce qui fut signalé à l'usage — « un
léger freeze de l'image au passage direct à la nuit » — et attribué à la lueur.
Ce n'était pas elle : c'était **la lampe du fanal**. Le groupe du feu était
masqué le jour et rendu à la nuit, or three compile ses programmes contre le
nombre de lumières qu'il **voit** : masquer puis rendre une `PointLight` change
ce compte et fait recompiler toute la scène, sur place, dans l'image où le
soleil passe sous l'horizon. C'est exactement la leçon déjà payée par les
bouches à feu, réapprise par une autre porte.

Trois choses compilaient donc au crépuscule, et chacune a été ramenée au
démarrage : la lampe est **laissée dans la scène** et commutée par son intensité
(zéro candela le jour) ; ses deux sprites restent visibles à **opacité nulle**,
deux quadrilatères transparents par feu ne coûtant rien quand une compilation
coûte une image entière ; et la lueur compile ses trois programmes à l'armement
(`bloom.prime()`), l'addition finale détournée dans une cible pour ne pas
éclairer l'image du jour. Relevé, galion armé, image terminée par un `readPixels`
qui force la synchronisation :

| | programmes | première image de nuit |
|---|---|---|
| avant | 49 → **63** | **106,4 ms** |
| lampe commutée par l'intensité | 52 → 55 | 12,9 |
| **avec les sprites et la lueur amorcés** | 60 → **61** | **5,8 ms** |

Soit le prix d'une image ordinaire (4,9 ms le jour, 5,2 à 5,8 de nuit), et un
balayage continu du soleil de +3° à −9° ne dépasse jamais 6,0 ms.

**Monter `glow` ne fait pas ce qu'on croit**, et la mesure vaut d'être gardée.
Rendu de nuit, œil par la hanche à 22 m, fenêtre comparée à elle-même éteinte :
les mêmes 1 372 pixels s'éclairent quelle que soit la force, et les deux tiers
d'entre eux **saturent déjà à `glow` 1**. Passer à 2,6 relève la moyenne de 76 à
94 sur 255 — ce sont les bords sombres qui montent, pas le cœur, déjà blanc — et
5 n'ajoute que 107. Sans passe de *bloom*, une émissive au-delà de 1 n'a nulle
part où aller : elle écrête. Pour une vitre franchement lumineuse il faudra une
vraie lueur autour, pas un nombre plus grand.

**ET CE QUI ÉCLAIRE N'EST PAS UN ESPAR**, ce qui s'est signalé à l'usage dès la
première fenêtre posée : « mes deux polygones sont derrière la poupe, ils ont
été déplacés ». Ils l'étaient — par le **gréement**, et non par la nuit. Une
vitre ajoutée au château arrière mesurait 4,20 m de large pour 0,44 d'épaisseur,
posée en travers et sur l'axe : elle passait les trois épreuves de forme d'une
vergue (longue, mince des deux côtés, centrée), se faisait donc reparenter dans
un pivot de mât — d'où le déplacement — et brassait avec lui à chaque
changement d'écoute. Elle s'était même trouvé un mât, une plaque de tableau
arrière haute et mince, et une voile pendait de la fenêtre.

La forme ne peut pas trancher ce cas : une vitre et une vergue ont la même. La
**matière**, oui — une vergue est en bois — donc `Naval.GLOW_NAMES` sert
maintenant deux fois, à allumer la nuit et à refuser ces pièces au gréement.
Une définition, deux usagers, la règle du projet. Et une fiche peut nommer en
clair ce qu'elle veut tenir hors du gréement (`model.rigIgnore`, une liste de
morceaux de noms de maillages), avec un avertissement en console nommant ce qui
a été écarté — une pièce retirée en silence est une pièce qu'on cherchera. Relevé
après correction sur la Roter Löwe : la fenêtre revenue sous son propre nœud,
**3 millimètres** de déplacement en brassant à fond contre plusieurs mètres
avant, quatre mâts tombés à trois et cinq voiles à quatre.

**Le pavillon montre le vent, les voiles montrent le réglage.** Un pavillon blanc
uni est envergué à la tête du grand mât, trouvé par la même lecture de forme que
les vergues : sur un modèle, la pièce la plus haute, bien plus haute qu'épaisse
et sur l'axe ; sur un navire procédural, simplement son plus grand mât. Un
bâtiment sans mât dans son modèle n'en porte pas, ce qui est le comportement
voulu — il n'a pas de drisse.

**Et il peut être noir.** Une fiche déclare son pavillon dans son
`appearance.ensign` : `"jolly"` pour la tête de mort, ou une couleur pour un
pavillon uni. Rien d'autre à toucher — le mât est trouvé par la même lecture de
forme, quel que soit le navire.

Le motif est **dessiné sur un canvas, jamais chargé**, et ce n'est pas une
préférence : une page publiée ne peut pas aller chercher une image, donc les
seules images de ce projet sont celles qu'il dessine — comme la lanterne, la
fumée et l'embrun — ou celles qui vivent *dans* un `.glb`, dont le build emporte
les octets en base64.

Dessiné **gros exprès**. Un motif lisible sur un écran à bout de bras est une
tache grise sur un pavillon à un demi-mille : à la taille où cette chose est
réellement vue, seules les grandes formes survivent, donc le crâne est large,
les os sont épais, et il n'y a aucun détail qui ne se lirait pas comme une
bavure.

Deux détails qui ont coûté un aller-retour. Le pavillon **n'avait pas de
coordonnées de texture** : ses `u` et `v` étaient calculés pour l'ondulation puis
jetés. Et l'émissif qui empêche un pavillon blanc de virer au gris contre un
ciel clair rendrait un pavillon noir **anthracite** — un pavillon sale et non un
pavillon sinistre : le motif apporte donc le sien, bien plus faible.

**ET LE PAVILLON DEMANDE SON MÂT AU GRÉEMENT, au lieu de le chercher une
seconde fois.** Signalé à l'usage — « le galion pirate n'a plus son drapeau » —
et il ne l'avait en réalité jamais eu depuis que le gréement lui prend son mât.

C'est une dépendance d'**ordre**. `_rigModel` tourne avant `_buildFlag` et
**reparente** le fût dans son groupe de chute, pour que tout ce qui appartient à
ce mât passe par-dessus bord ensemble. `_modelParts` ne le voit donc plus, et la
règle de forme du pavillon ne trouve plus rien. Relevé sur le galion pirate :
**trois mâts trouvés** dont un avec son fût de 19,3 m, et **zéro candidat** pour
le pavillon. Ses autres pièces sont des mâts fondus avec leurs vergues — 0,5 ×
17,2 × 21,6 m — que la règle refuse à juste titre, et c'est bien elle qui a
raison.

Le chercheur de mâts a déjà répondu à la question, et il est le **seul** à
pouvoir y répondre puisque c'est lui qui a déplacé la pièce. La poser deux fois,
c'était se donner deux réponses à tenir en accord — exactement la faute que
l'invariant du plan de formes unique existe pour empêcher. La règle de forme
reste, mais en **repli** : un navire sans mât gréé n'a pas de drisse, ce qui est
le comportement voulu. Vérifié : tête de mort à la pomme, 21,4 m.

**ET IL PEND DANS SON MÂT, PAS SUR LE NAVIRE** — ce qui fut la seconde moitié
du même bug, signalée dès que la première fut corrigée : « le drapeau ne
disparaît pas quand le mât est tombé, il ne le suit pas ». Il restait en l'air,
seul, à l'endroit exact où la pomme se trouvait, ce qui est pire que pas de
pavillon du tout.

C'est la raison même qui met les vergues et la toile dans la chute plutôt que
dans le navire : **tout ce qui appartient à ce mât doit passer par-dessus bord
ENSEMBLE**. Un pavillon est frappé à une drisse, et une drisse est tournée au
mât. Accroché au groupe de la coque il fallait le faire tomber, le recentrer et
le détruire à la main, c'est-à-dire réécrire trois fois ce que le graphe fait
gratuitement ; accroché à la chute il ne demande rien. Une réparation le rend
avec l'espar, puisqu'il en est un enfant.

Relevé, mât abattu à la main : 21,3 m et l'axe à zéro ; 14,2 m et douze mètres
de côté à deux secondes ; sous l'eau à quatre ; **parti avec sa chute à six**.


Il porte sur le vent **apparent**, comme tout ce qui flotte depuis un pont en
mouvement, et son lacet se déduit directement de ce que le solveur connaît déjà :
`atan2(−tack·sin β, −cos β)`, soit le bout au vent tourné de 180°. C'est
l'unique instrument qui dise où est le vent plutôt que où l'on a brassé, et il
bascule donc avant les voiles quand elle lofe. Sous 8 m/s l'ondulation s'éteint
et le pavillon **retombe le long du mât** en perdant sa longueur : à 2,7 m/s,
0,38 m d'ondulation pour 1,58 m d'affaissement ; à 17,9 m/s, 1,13 m d'ondulation
et plus d'affaissement du tout.

**L'occlusion ambiante ne porte que sur le navire** (`ssao.js`). Le SSAO réclame
une passe de profondeur, que three rend avec un matériau de substitution : la mer
étant déplacée dans son propre vertex shader, elle y serait dessinée plate et
l'occlusion serait fausse au contact coque/eau. Restreindre la passe au navire
lève l'objection — il est de la géométrie ordinaire — et coûte bien moins cher.
Il est isolé par une **couche de rendu** (`Naval.SHIP_LAYER`), jamais en masquant
le reste : masquer puis restaurer un graphe deux fois par image est plus lent et
se laisse facilement abandonner dans un mauvais état.

Le résultat est relu dans les matériaux du navire au chunk **`<aomap_fragment>`**,
et c'est le point important : three n'y multiplie que la lumière **indirecte**.
L'occlusion appartient à l'ambiante, pas au soleil — une planche au fond d'une
écoutille qu'un rayon atteint encore reste pleinement éclairée, elle ne perd que
le ciel. Multiplier la couleur finale y peindrait du gris jusque dans le soleil,
et ça se lit comme de la saleté.

Demi-résolution partout, l'occlusion étant basse fréquence et le flou qui suit
jetant de toute façon le détail supplémentaire. Mesuré sur la Roter Löwe :
occlusion moyenne 0,90 et minimum 0,53 au fond des creux, sur 5,3 % de l'image —
soit exactement sa surface à l'écran. Surcoût de soumission relevé à 0,10 ms par
image.

**`settle()` aussi avait un effet de bord**, de la même famille que `setSun()`.
Il aplatit la mer — nécessaire, la coque devant trouver ses lignes sans qu'une
houle la secoue — mais il la laissait plate. `commission()` masquait la faute en
appelant `refreshSea()` juste après, si bien qu'elle n'est apparue qu'avec le
**deuxième** appelant : mettre une coque à l'eau depuis le panneau Flotte
aplatissait la mer définitivement, la console continuant d'afficher force 6 et
plein creux au-dessus d'un lac. `setSeaState` retient donc son état, et `settle`
le remet en partant — aucun appelant n'a plus à le savoir. Leçon générale : un
effet de bord qu'un seul appelant compense n'est pas corrigé, il est caché.

**Attention en mesurant : `setSun()` a des effets de bord.** Il recalcule
`sun.intensity`, `hemi.intensity` **et** rappelle `refreshEnvironment()`, qui
libère la texture d'environnement précédente. Un banc de mesure qui éteint une
lumière puis appelle `setSun` la rallume ; un banc qui garde une poignée sur
`scene.environment` à travers un rafraîchissement pointe sur une texture morte.
Les deux m'ont fait conclure trois fois de suite que l'éclairage par le ciel ne
fonctionnait pas alors que seul le banc était faux. Appliquer la configuration
**après** `setSun`, et relire `scene.environment` après chaque rafraîchissement.

**La toile est éclairée à travers.** Le soleil derrière une voile, l'essentiel
de ce qu'on voit n'a jamais touché la face avant : c'est passé par le tissage, et
la voile est plus claire que tout ce qui est éclairé de face, vergues en barres
sombres dessus. Un matériau ordinaire ne sait pas faire ça — sa face arrière
devient noire. `Naval.applySailLight` ajoute le lobe transmis, le même que la
mer utilise pour la lumière traversant une crête (`uSSS`) : maximal quand œil,
toile et soleil sont alignés, et seulement là où le soleil est sur la face qu'on
**ne** regarde pas. La normale est prise brute sur la géométrie, pas la normale
retournée vers l'observateur, pour que les deux faces répondent pareil. Mesuré à
contre-jour contre plein feu : 237 contre 168 de luminance, et 93 à 60° du plan,
la transmission retombant bien avec l'angle.

**Les patches de matériau se chaînent, ils ne s'écrasent pas.** `applyHaze` et
`applySailLight` s'appliquent au même matériau. Chacun conserve le
`onBeforeCompile` précédent et l'appelle en premier ; la brume doit passer en
**dernier**, étant l'air devant tout le reste. Deux conséquences pénibles :

- Chacun doit pouvoir déclarer `uCam` et `uSun` sans savoir si l'autre l'a fait.
  Deux déclarations sont une redéfinition et **tout** le fragment shader échoue.
  D'où `Naval.SUN_UNIFORMS_GLSL`, sous garde de préprocesseur.
- Three met les programmes en cache sous `customProgramCacheKey()`, qui vaut par
  défaut le **texte source** de `onBeforeCompile`. Le texte de la fermeture de
  brume est identique pour tous les matériaux — la variable capturée qui les
  distingue n'y apparaît pas. Sans clé propre, la voile reçoit le programme de
  la coque et son code n'est jamais compilé : le patch est correct, se compose
  correctement, et ne s'exécute jamais, sans le moindre message. `applySailLight`
  ajoute donc `|sail-translucent` à la clé.

**Une voile peut être PEINTE, et c'est la seule image de ce projet qui soit
fournie plutôt que dessinée.** Toutes les autres — la lanterne, la fumée,
l'embrun, la tête de mort — sont faites sur un canvas à l'exécution, précisément
parce qu'une page publiée ne peut pas aller chercher un fichier local. Un motif
sur une basse voile ne se dessine pas en vingt lignes, donc la règle est
honorée autrement : `build.js` lit l'image et **réécrit le chemin en `data:`
URI** dans la fiche embarquée, exactement comme il embarque les octets d'un
`.glb`. La chaîne qui arrive au `TextureLoader` est un chemin sur le serveur de
dev et les octets eux-mêmes dans la page publiée ; rien du côté du jeu ne
connaît la différence. Vérifié sur le fichier construit : les octets sont
identiques à ceux du disque et **aucun chemin local ne survit**.

**Les coordonnées de texture étaient calculées puis jetées** — exactement la
faute du pavillon, retrouvée au même endroit et pour la même raison : `u` était
gardé parce que le ferlage en a besoin, et `v` n'existait que comme variable de
boucle. `1 - v` parce que les rangs courent de la têtière vers le bas quand le
`v` d'une image monte ; sans le retournement, un motif arrive sur la tête.

**Et elles sont posées sur la grille PARAMÉTRIQUE, pas sur les positions
finales.** C'est ce qui compte : la coupe, le creux, l'affaissement et
l'enroulement déplacent la toile — trois d'entre eux à chaque image — et aucun
ne doit traîner la peinture dessus. Une voile est peinte avant d'être
enverguée, donc la peinture suit le tissu. Vérifié : un ferlage complet ne
bouge pas une seule UV. Le carré unitaire tombe **entièrement** sur la voile ;
la coupe ne rogne rien, elle déforme — le milieu du bas de l'image est tiré
vers le haut par le rond de fond.

**Une matière par TYPE de voile, pas une par navire ni une par voile.** Un motif
appartient aux carrés et n'a rien à faire sur un foc, qui est une autre voile
d'une autre coupe : une fiche les peint donc un type à la fois
(`appearance.canvasMap.square`), et ce qu'elle ne nomme pas garde la toile unie.
Pas une par voile non plus — un galion en porte neuf, ce qui ferait neuf
programmes là où un suffit. Relevé : cinq voiles carrées, **une** matière.

Deux pièges. La **couleur reste** et multiplie l'image plutôt que de passer au
blanc : `appearance.canvas` est le ton de sa toile, et c'est lui qui garde une
voile peinte du même écru fatigué que ses voiles unies. Et la matière neuve doit
être **prévenue du ciel à la main** : `applyHaze` la trouve toute seule en
parcourant le groupe, mais `applySailLight` est appelée sur des matières
nommées, si bien qu'une voile peinte aurait été la seule chose à bord à ne pas
s'éclairer à travers. Le modèle arrivant du réseau bien après la coque, les
uniformes sont retenus et le patch posé à la construction de la matière.

`node tools/uv-chart.js` écrit le repère UV — quadrillage 8 × 8, bandeau de
têtière, flèche vers le haut et quatre coins de couleurs différentes, la seule
chose qu'un repère UV doit rendre impossible à confondre étant une image
retournée ou en miroir. Voir `ships/textures/README.md`. Fait pour les carrés ;
le foc et l'antenne latine seront la même ligne avec un autre mot dedans.

**Textures PBR.** Elles fonctionnent, et la Roter Löwe s'en sert déjà : son
matériau `hull` porte un `baseColorTexture` et ses maillages un `TEXCOORD_0`.
La condition est que l'image vive **dans** le `.glb` : le build en emporte les
octets en base64 et `GLTFLoader.parse()` lit tout depuis la mémoire. Une texture
en fichier image séparé ne marchera jamais — la politique de sécurité bloque le
`fetch` d'un fichier local, et le build n'embarque que les `.glb`. `normalMap`,
`roughnessMap` et `metalnessMap` passent par le même chemin.

**Le relief se tire de la rugosité, parce que glTF n'a pas de bump.** Demandé
comme « refaire le shader pour qu'il prenne la normal map » — or le shader
n'y était pour rien : les matériaux du `.glb` sont des `MeshStandardMaterial`
qui lisent déjà carte normale et rugosité, et les greffes du projet (brume,
occlusion) n'y touchent pas. Ce qui manquait était **dans le fichier** : relevé
sur les cinq modèles, aucune `normalTexture`, et pour la Roter Löwe une seule
image de couleur. Une image en niveaux de gris branchée en relief dans Blender
est jetée par l'exportateur sans un mot ; seule sa moitié rugosité survit, dans
le canal vert.

`model.relief` la relit donc comme une hauteur : Sobel sur le processeur, une
fois par texture, rendu en carte normale **tangentielle** plutôt qu'en
`bumpMap`. Un bump différencie la hauteur par bloc de pixels dans le shader et
fourmille sur une coque qui bouge ; une carte normale se mipmappe comme toute
image. Pas de tangentes dans ces modèles, donc three bâtit le repère sur les
dérivées d'UV, et `normalScale.y` est **négatif** exactement comme GLTFLoader le
pose dans ce cas.

**Le signe a été mesuré, pas raisonné.** Rendu contre le `bumpMap` de three —
dont la convention, blanc en relief, est connue — sur trois vues, corrélation
de l'écart d'éclairage : `(1,−1)` gagne partout (0,27 · 0,49 · 0,50), et
retourner l'axe v fait passer au négatif (−0,16 · −0,37). L'axe u discrimine
mal de flanc (0,27 contre 0,26) parce que le bordé court à l'horizontale et n'a
presque pas de pente dans ce sens ; la vue de dessus le tranche (0,49 contre
0,29). Réglé à 16 : à 6 rien ne se voyait, même rasant.

Coût : **63 ms** au chargement pour 2048 × 1536, et une texture de plus en
mémoire vidéo (16 Mo avec ses mipmaps). **Rien par image** : coque plein écran,
1,94 ms sans contre 1,97 avec au meilleur essai, dans un bruit de 1,9 à 3,6.
Le canvas de travail, lui, gardait 12 Mo côté navigateur pour rien : il est
vidé dans `onUpdate`, c'est-à-dire **après** l'envoi — three ne relit jamais
l'image tant que personne ne relève la version de la texture. Vérifié : image à
`null`, texture toujours liée, relief toujours dessiné, et une carte neuve
refaite puis libérée à chaque changement de navire. La vraie facture est ailleurs : l'exportateur réécrit la
rugosité en **PNG de 1,15 Mo**, et la page construite passe de 3,4 à 5,2 Mo.

**Le compas : c'est la CARTE qui tourne.** La ligne de foi reste en haut et la
rose pivote de `−cap` dessous, comme dans un habitacle — on lit ce qui passe
sous la marque. Faire tourner le navire au-dessus d'une rose fixe donnerait une
carte de navigation, pas un compas. Pas d'aiguille non plus : sur un compas à
carte, la carte *est* l'aimant, et une aiguille dessinée par-dessus est à la
fois fausse et illisible en travers des chiffres. Le N rouge porte le nord seul.
Les trente-six graduations sont construites en JavaScript plutôt qu'écrites dans
le balisage — trente-six occasions de se tromper d'une.

**Réglage des voiles.** Le modèle ne donne aucun retour lisible : à 45° de vent
apparent, des écoutes à 40° ne laissent que 5° d'incidence, donc `CL` s'effondre
et la poussée tombe au cinquième — sans que rien ne l'annonce, la console
affichant « voiles établies » et un nombre de kN plausible. D'où le repère vert
sur la barre d'écoutes, à `optimalAoA`, et la touche `T` qui y borde
directement. L'écoute initiale n'est plus une constante : elle est prise sur le
repère à la **première image** après l'armement, car `settle()` fait flotter le
navire dans un calme plat et le vent apparent n'existe qu'une fois `refreshSea()`
passé.

**Le banc tient dans un bouton** (`J`, ou la ligne « Débogage » du panneau
Flotte). Toute mesure d'artillerie de ce projet a commencé de la même façon :
une seconde coque de la même classe, arrêtée, parallèle, voiles ferlées, à une
distance connue par le travers, et les deux réparées entre deux coups. Le monter
à la main prenait une douzaine de lignes à chaque fois et se trompait une fois
sur trois — la mauvaise batterie, un navire encore sur son erre, une barre
automatique qui emmenait tranquillement la cible hors du banc.

Trois choses en font un banc plutôt que deux navires qui se trouvent près l'un
de l'autre, et toutes les trois sont faciles à oublier :

- **les deux sont ARRÊTÉS**, toile ferlée. Une cible qui dérive transforme une
  mesure reproductible en anecdote ;
- **on retire sa barre à la conserve.** Elle chasserait le navire à la roue, ce
  qui est exactement ce pour quoi elle est faite et exactement ce qui ruine
  l'expérience ;
- **elle se place à l'opposé de l'objectif**, pour qu'une seule vue les tienne
  tous les deux — et de quel côté est **demandé** plutôt que supposé : la caméra
  est plantée d'abord, son ancre lue, et la conserve va sur l'autre bord.

Deux pièges dans ce dernier point, et ils se sont présentés l'un après l'autre.
Écrit à l'envers — « elle se met à bâbord, puisque la caméra fixe se plante à
tribord » — c'était juste sur le gréement et faux à l'écran, et elle sortait à
moitié derrière la console de mer. Puis, corrigé, `setMode(3)` appelé pour un
mode où l'on était déjà rendait la main **sans avoir planté** : l'ancre valait
encore l'origine, le produit scalaire sortait nul, et la conserve était postée du
côté de l'objectif — un seul navire à l'écran. On appelle donc `plant()` sans
détour, et un garde-fou traite « composante par le travers nulle » comme « la
question n'a pas eu de réponse ».

Elle est une **copie du navire qu'on mène** plutôt qu'un galion écrit en dur :
deux exemplaires de ce qu'on est en train d'essayer est presque toujours ce
qu'on voulait dire.

**La capture d'écran s'écrit dans le dossier du jeu** (touche `I`), et il a
fallu passer par le serveur pour ça : une page ne peut pas écrire sur le disque,
alors que le serveur de dev sert déjà ce dossier-là. Elle lui est donc postée et
c'est lui qui écrit, dans `captures/`, sous un nom horodaté. Une page publiée
n'a pas de serveur derrière elle et retombe sur le navigateur — c'est une autre
chose à un autre endroit, et le message le dit plutôt que de faire comme si
c'était pareil.

Ce qui en sort **ne porte aucun instrument** : les cadrans sont des éléments DOM
posés par-dessus le canvas, donc ils ne sont tout simplement pas dans le tampon
de dessin. La capture est la mer et le navire, sans avoir eu à masquer quoi que
ce soit.

**Le piège est de dessiner d'abord.** Le renderer est construit avec
`preserveDrawingBuffer` à faux, donc le contenu du tampon est **indéfini** une
fois l'image composée. Appelé depuis une touche — ce qui arrive entre deux
images et non pendant l'une d'elles — `toDataURL` rend une image vide ou
déchirée, et sans le moindre message. Redessiner juste avant de lire est ce qui
fait exister les pixels au moment où on les demande.

**Et il faut refuser une image vide plutôt que l'écrire.** Un canvas sans
surface — fenêtre réduite, volet non composité — rend un PNG de **trois
octets**, et c'est exactement ce qui s'est retrouvé sur le disque au premier
essai, avec un message annonçant que tout allait bien. Le garde-fou est des deux
côtés : la page refuse sous huit pixels de côté, le serveur refuse sous un
kilo-octet encodé. Un outil qui ment sur ce qu'il vient d'écrire est pire que
pas d'outil.

**Débogage.** `Naval.app` expose les instances vivantes (`stage`, `ocean`,
`foam`, `physics`, `ship`, `cam`, `hud`) depuis la console. `Naval.app.stage.strike()`
déclenche un éclair à la demande. Plusieurs bugs de ce projet ont été longs à
cerner faute de pouvoir inspecter quoi que ce soit à l'exécution.

**Nuages : l'échelle du bruit est tout.** Le premier jet lisait le bruit sur
`dir.xz/up*0.055`, ce qui fait tenir **tout le ciel visible dans quelques
centièmes d'unité de bruit** : le motif y est quasi constant, et ce qui en sort
est un voile pâle uniforme, pas des nuages. Le bruit a des motifs d'environ une
unité, donc le ciel doit en couvrir une dizaine. Le piège s'est doublé d'un
piège de mesure : un A/B au pixel donnait « 30 743 px changés, écart moyen
9/255 », que j'ai lu comme « effet présent mais faible » alors que c'est la
signature exacte d'un bruit presque constant. Un écart faible et **étalé
partout** ne dit pas « trop discret », il dit « pas de structure ».

Le caractère vient ensuite de l'**anisotropie** : le bruit est lu quatre fois
plus fin en travers de la traînée que dans sa longueur (`vec2(q.x*4.2,
q.y*17.0)`). Isotrope, on obtient un ciel pommelé — vrai, mais couvert. Étiré,
on obtient des cirrus, et c'est ce qu'on veut par beau temps. Le seuil est pris
haut dans l'histogramme pour ne laisser passer que les crêtes, et la couverture
par défaut est de 12 % : du bleu, quelques traînées. Le curseur « Nuages » de la
console de mer va jusqu'au couvert.

**Et ils s'éteignent la nuit.** Un nuage n'a pas de lumière à lui : on ne le
voit que parce que le soleil est dessus, donc quand celui-ci passe sous
l'horizon il n'y a plus rien à voir et le ciel est simplement noir. Éclairés
toute la nuit ils étaient la seule chose de l'image à briller d'elle-même —
même faute que l'écume, même remède. L'extinction réemploie le smoothstep qui
fait sortir les étoiles, un peu plus bas pour que le nuage soit parti quand
elles sont franchement là : c'est le même événement vu des deux côtés, et lui
écrire un second seuil laisserait les deux dériver. Vérifié : à −12° de soleil,
couvert plein contre ciel clair, **zéro pixel d'écart**.

**Baisser le curseur ne baissait pas le ciel**, et c'est le piège de ce réglage.
La bande du seuil est large, donc déplacer son bord fait glisser beaucoup de
bruit au travers, mais lentement : passer de 12 % à 6 % ne retirait que 14 % de
nuage — 16,3 % du ciel couvert contre 14,0 — là où on en voulait la moitié.
C'est le **plancher** qu'il faut relever, la pente montant avec lui pour que le
couvert plein retombe exactement où il était. Réglé à `smoothstep(0,93 - amt·0,65, 0,99 - amt·0,36, f)` :
8,75 % du ciel au défaut contre 16,26 avant, soit la moitié, et le curseur va
toujours jusqu'au couvert.

**Piège de mesure, et il a coûté trois essais.** Lire les pixels après
`stage.render()` donne n'importe quoi : le rendu laisse une autre cible liée —
il y a la passe d'eau transparente, l'occlusion et la réflexion — et l'on relit
celle-là. Un ciel de nuit ressortait en bleu de plein jour, ce qui a fait
soupçonner le shader pendant un moment alors que seul le banc était faux. Il
faut `gl.bindFramebuffer(gl.FRAMEBUFFER, null)` avant de lire. Une fois
rebasculé, deux lectures de suite du même réglage donnent **0 %** d'écart, ce
qui est la vérification qu'il fallait faire d'abord.

Comme le reste du ciel, ils n'existent **qu'une fois** : `ocean.js` prend les
objets uniformes `uCloud`/`uSkyTime` de `stage.skyUniforms`, pas des copies,
donc rien ne peut diverger de ce qu'on voit au-dessus. La mer, elle, ne les
reflète **pas** — c'était un choix, expliqué plus bas — et le gros temps les
efface au profit de son couvercle.

**Les mouettes sont du mouvement, pas des oiseaux** (`gulls.js`). Ce que l'œil
reconnaît à distance n'est pas l'animal mais le vol : un cercle lent, un
inclinaison **dans** le virage, et des battements par bouffées entrecoupés de
vol plané. Tout le fichier est cette arithmétique-là ; l'oiseau lui-même n'est
que deux ailes, un corps et une queue.

Un tiers du vol suit le **navire** plutôt que l'île. Ce n'est pas une licence :
les goélands suivent les bâtiments, et surtout une mouette au-dessus de l'île
fait trois pixels à quatre cents mètres, quand une qui tourne au-dessus de la
dunette est un oiseau. Les autres restent sur leur île, pour que l'endroit garde
sa vie propre quand rien ne passe.

Deux détails de forme comptent. L'aile doit être **longue et mince** : la
première coupe avait 0,36 m de corde pour 0,9 m de demi-envergure, un allongement
de cinq, soit un pigeon qui plane. Et la **queue ne bat pas** — c'est le seul
élément fixe de la silhouette, sans elle l'oiseau est un corps entre deux ailes
et se lit comme une fléchette. Placées comme la terre, dans le repère de l'île
et positionnées contre l'origine courante, donc l'origine flottante ne leur
coûte rien. Surcoût mesuré : **0,05 ms par image** pour douze oiseaux.

**Brume.** Liée à l'état de la mer — voir « Le jour, la nuit, et le gros temps ».
Elle ne l'était délibérément pas, et le renversement est raisonné là-bas.

**Spectre.** Les vagues viennent d'un spectre JONSWAP : il fixe la *forme*
(quelles fréquences portent l'énergie, comment elles s'étalent), et la table
Beaufort fixe l'*échelle*, de sorte que la hauteur significative annoncée par la
console est celle qu'on obtient réellement. Le pic est corrigé pour une mer
limitée par le fetch : la relation « mer complètement développée » donnait des
houles de 800 m en tempête, plus longues que toute mer réelle.

**Sélection CPU.** Le solveur n'intègre pas les 18 composantes mais les plus
**énergétiques**, pas les plus longues. Prendre les plus longues était une
erreur : en tempête elles atteignent des centaines de mètres, et une houle
beaucoup plus longue que le navire le soulève en bloc sans le travailler. C'est
la bande autour du pic qui compte.

**Distances autour de la coque.** `ed` est une distance normalisée
*elliptique* : une bande d'épaisseur constante en `ed` est fine par le travers
mais épaisse de plusieurs mètres devant l'étrave, car l'ellipse est bien plus
longue que large. Toute épaisseur d'écume doit être exprimée en **mètres**
(`length(rel) * (1 - 1/ed)`), sinon le collier gonfle aux extrémités.

**Accents graves dans les shaders.** Les shaders sont écrits dans des *template
literals* : un accent grave dans un commentaire GLSL ferme la chaîne et casse
tout le module, sans message clair. `node --check js/*.js` le détecte.

**Moiré.** Le maillage de mer concentre ses sommets près de la caméra : la
maille passe d'environ 1 m à plusieurs mètres en quelques dizaines de mètres.
Dès qu'elle dépasse le quart d'une longueur d'onde, le maillage ne peut plus
porter cette vague et le battement entre les deux fréquences produit du moiré.
La parade est de **borner la bande passante contre la taille locale de la
maille**, jamais contre la distance : `vSpacing` sort de la dérivée du remap et
sert de critère unique aux vagues, aux rides et à la rugosité spéculaire. Deux
seuils de distance séparés finiraient toujours par se contredire et faire une
bande visible.

**Mer.** Le détail fin ne vient pas du maillage mais d'une perturbation de
normales (`rippleNormal`) — six vagues de Gerstner seules donnent du plastique
moulé. Ce détail doit être fondu avec la distance, sinon il bouillonne à
l'horizon. Le soleil haut rend le scintillement invisible : son reflet tombe à
quelques mètres du bord. La commande « hauteur du soleil » existe pour ça.

**Ce qu'on ne fera pas.** Le turquoise à caustiques des images de lagon vient
d'un fond de sable vu à travers deux mètres d'eau, pas d'un meilleur shader. En
pleine mer, rien ne remonte. Ce serait un décor distinct, pas du réalisme.

## La météo qui se fait toute seule

**Un vent posé une fois et jamais retouché est ce qu'il reste de plus artificiel
sur l'eau** : chaque mer devient sa propre photographie, et rien de ce qui
arrivera dans l'heure n'était pas déjà vrai à la première minute.

Ce qui rend un vrai vent vivant n'est pas d'être aléatoire, c'est d'avoir de la
**mémoire** : la prochaine rafale ressemble à la précédente. Tirer un nombre
neuf toutes les quelques secondes donne exactement l'inverse — un vent sans
passé, sautant entre des états sans rapport. D'où un processus
d'Ornstein-Uhlenbeck sur **deux échelles de temps** : le *système* (force et cap
moyens, une dizaine de minutes), qui donne sa forme à une heure de navigation,
et le *vent lui-même* autour de cette moyenne (une vingtaine de secondes), qui
sont les rafales et les risées.

La **force** revient vers une moyenne climatique, parce que la plupart des jours
sont une brise modérée et que les coups de vent sont rares : le rappel produit
cette distribution gratuitement, sans table de probabilités. Le **cap** ne
revient nulle part — aucun point de la rose n'est plus naturel qu'un autre, donc
sa moyenne est une marche aléatoire pure et le vent peut finir la journée
n'importe où. Mesuré sur une heure : force médiane 4,5, du 2,2 au 6,3 en
déciles, 34° de rotation totale.

Deux couplages, vrais tous les deux sur l'eau : un vent fort est plus rafaleux
en valeur absolue, donc la taille des rafales suit la force moyenne ; mais c'est
le vent **faible** qui est capricieux en direction, un coup de vent tenant son
cap des heures durant. Les deux vont donc en sens inverse. Et le cap a sa
**propre** constante de temps, plus longue que celle des rafales : une direction
qui oscille aussi vite que la force ne se lit pas comme de la météo mais comme
un instrument cassé — la barre la poursuivrait sans arrêt. Avec la même
constante que les rafales on mesurait **48° de rotation par minute** ; séparée,
16°.

Le pas d'intégration est la forme **exacte** du processus, pas un pas d'Euler :
le temps d'image n'est pas fixe, et un pas naïf rendrait le vent plus rafaleux
sur une image lente que sur une rapide — la météo dépendrait de la fréquence
d'affichage.

**Le vent et la mer ne sont pas la même chose, et c'est là que tout se joue.**
Pour une main sur la console les deux vont ensemble et `setSeaState` règle les
deux. Sous une météo qui tourne seule, non : une risée se sent dans la toile à
l'instant où elle arrive, alors que la mer qu'elle lève met des minutes à se
former et des minutes à se coucher. D'où `setWind`, qui ne touche pas au
spectre, appelé à chaque image, tandis que le spectre est reconstruit en
**retard** sur le vent. Ce n'est pas une licence, c'est le récit honnête.

**Reconstruire le spectre rejoue la phase de chaque composante**, et c'est le
vrai piège. La phase vaut `k·(d·r) − ω·t + correction`. Changez ω d'un millième
et le terme `ω·t` saute de `Δω·t` — or `t` est l'horloge courante, des milliers
de secondes : un radian entier. Même chose pour la direction, `k·(d·origine)`
étant évalué contre une origine qui peut être à des centaines de kilomètres, si
bien qu'un centième de degré déplace la phase au navire d'une bonne fraction de
longueur d'onde.

Ce n'est pas une erreur d'arrondi avec laquelle on vit : ensemble elles
rebrassent la mer à chaque rafale, ce qui se lit comme un bouillonnement. Les
deux sont absorbées **exactement**, et pour la raison qui fait marcher l'origine
flottante : ce qui change est une phase, et une phase ne compte que modulo 2π.
La correction est posée pour que le total soit inchangé **à l'origine locale**,
là où est la flotte. Mesuré, pour un pas de veille, contre un creux de 0,60 m
de rms :

| distance | avec report | sans report |
|---|---|---|
| 0 – 25 m | **0,003 m** | 0,812 |
| 25 – 100 m | 0,013 | 0,752 |
| 100 – 250 m | 0,032 | 0,729 |
| 250 – 600 m | 0,076 | 0,770 |
| 600 – 1500 m | 0,195 | 0,762 |

Sans report la mer est intégralement rebrassée partout — l'écart dépasse la mer
elle-même. Avec, le navire flotte dans une mer continue au millimètre, et
l'écart croît avec la distance comme il le doit, deux spectres différents étant
réellement deux mers différentes. La correction est gardée **par bande
spectrale** et non par entrée de `waves`, qui est triée par énergie et se
réordonne dès que le vent change.

**La vitesse est plafonnée, et ce n'est pas un réglage de goût.** Une
reconstruction déplace le nombre d'onde de chaque composante, et `k` entre dans
la phase multiplié par la position : mesuré sur 400 m, un pas d'**un** Beaufort
déplace la surface de 9,8 m de rms. Autrement dit un seul cran de 0,1 — le plus
fin que la console sache même afficher — suffirait à rebrasser la mer. D'où
`seaRate`, 0,0125 Beaufort **par seconde**, et 0,30° de rotation par seconde :
encore trois quarts de Beaufort par minute, bien plus vite que n'importe quelle
météo.

Par **seconde**, et non par reconstruction, ce qui a d'abord été écrit de
travers. Un plafond par appel fait avancer la mer plus vite sur une machine
rapide, et pose son pas de façon inégale dès que le temps d'image se met à
flotter — la fréquence d'affichage devient un paramètre physique. Seule la
vitesse a un sens ; le pas est ce que `dt` en fait.

**Et le spectre est reconstruit à CHAQUE image**, pas cinq fois par seconde.
À 5 Hz, chaque reconstruction devait porter un cinquième de seconde de dérive,
et au-delà du demi-mille cela fait vingt centimètres de surface qui bougent
d'un coup, cinq fois par seconde — un scintillement, et c'est exactement ce
qu'on voyait. Étalée sur les images, la même dérive totale fait un onzième de
cela par pas :

| pas | 600 – 1500 m | part du creux |
|---|---|---|
| 0,2 s (5 Hz) | 0,188 m | 32 % |
| une image (60 Hz) | **0,016 m** | 2,8 % |

Le total déplacé est identique — c'est le **grain** qui change, et c'est lui
seul que l'œil attrape.

**Reconstruire à chaque image ne se paie qu'à une condition : ne rien
allouer.** `setSeaState` fabriquait dix-neuf objets neufs par appel, ce qui
n'était rien quand une main sur un curseur l'appelait deux fois par minute.
Soixante fois par seconde, cela fait plus de mille objets éphémères par
seconde, donc des pauses de ramasse-miettes — et une pause de ramasse-miettes
*est* une saccade. Les tableaux sont désormais des réserves écrites sur place :
mesuré à **7 octets par reconstruction**, soit rien, pour 10 µs de calcul.

Enfin, **un processus d'Ornstein-Uhlenbeck échantillonné par image est du bruit
blanc**. Ses trajectoires sont continues mais nulle part dérivables : le vent
reçoit un coup de dé neuf soixante fois par seconde, et les voiles, le pavillon
et tous les cadrans tremblent avec. Une vraie rafale n'a aucune énergie à
trente hertz. Le processus est donc la **cible**, et le vent qui souffle la
suit à travers un retard court (1,1 s) : mêmes statistiques sur vingt secondes,
courbe lisse sur une. Mesuré sur la dérivée seconde image par image :
**0,00028 contre 0,0268**, soit 96 fois moins de secousse, pour une amplitude
de rafale inchangée.

Corollaire : la météo pilote l'océan **directement**, en pleine précision, et
n'écrit dans les curseurs que pour l'affichage. D'où la séparation de
`refreshSea()` (régler *et* afficher) et de `showSea()` (afficher seulement) —
sans quoi la console, dont le pas est cent fois trop grossier, serait le goulot
qui empêche la mer d'être continue. Le panneau montre le cap du **vent** et non
celui de la houle, comme son étiquette le dit ; l'écart entre les deux est
justement ce qu'il y a d'intéressant à voir — après une saute, les lames
continuent de courir d'où le vent soufflait un quart d'heure plus tôt.

Prendre un curseur en main **arrête** la météo automatique plutôt que d'être
écrasé un dixième de seconde plus tard, et l'arrêt adopte ce que la console
affiche. Une mer qui a rattrapé un vent stable ne reconstruit **rien du tout** :
`chaseSea` dit qu'elle n'a pas bougé et l'appel est sauté.

## Le jour, la nuit, et le gros temps

**Le soleil suit de la vraie trigonométrie sphérique, pas une sinusoïde
déguisée.** Trois lignes, une latitude — que ce monde a déjà, la carte prenant
le point en latitude et longitude — et l'on obtient gratuitement tout ce qu'un
arc dessiné à la main doit se faire dire : le soleil se lève à l'est, se couche
à l'ouest, passe plein sud au méridien sous ces latitudes, monte plus haut en
été, et reste sous l'horizon pour la bonne part de la journée. Une sinusoïde en
hauteur avec un relèvement fixe fait lever et coucher le soleil au même endroit,
ce dont on s'aperçoit sans pouvoir dire pourquoi. Vérifié : coucher à 18 h 42 au
relèvement 286°, hauteur maximale 55,4° au méridien, pour 46,2° N et 12° de
déclinaison.

*(Relevé en Gascogne, avant que l'archipel ne parte aux Antilles. À 13,6° N la
même arithmétique donne un tout autre ciel — jour presque égal toute l'année,
midi près du zénith, crépuscule court — et c'est précisément l'intérêt d'avoir
écrit la trigonométrie plutôt qu'une courbe : la latitude a changé d'une
constante et la lumière a suivi toute seule.)*

**La vitesse de défilement est un multiplicateur d'un taux ÉNONCÉ** : une minute
réelle pour une heure, donc vingt-quatre minutes pour un jour entier à ×1, et
douze à ×2, où le curseur se trouve au départ. Énoncer le taux est le point —
« ×2 » ne veut rien dire tant que ×1 n'en veut rien. La console affiche donc
« ×2 · 12 min/jour » et non un chiffre nu. Mesuré à 2,7 µs par appel, soit
0,17 ms par seconde : le prix est la cubemap d'environnement qu'il fait
reconstruire, et elle est déjà bridée.

Prendre le curseur de hauteur en main **arrête** le défilement, pour la même
raison que la mer : être écrasé un quart de seconde plus tard n'est pas une
interface. Et le curseur descend maintenant à −40°, parce que le soleil y
descend vraiment sous ces latitudes ; arrêté à −10, il aurait montré une nuit
bloquée au crépuscule.

**Le gros temps ne fait pas descendre le soleil.** C'était la première lecture
et elle était fausse. Un coup de vent à midi est sombre parce que le ciel s'est
fermé, pas parce que le soleil s'est couché : la lumière reste où elle est dans
le ciel et cesse simplement d'arriver. La hauteur n'est donc pas touchée, et ce
qui change est le couvercle au-dessus, la crasse entre, et ce qui passe du
soleil.

**Et la brume est désormais LIÉE à l'état de la mer, alors qu'elle ne l'était
délibérément pas.** La règle d'origine — un coup de vent ne doit pas fermer
l'horizon, puisqu'il le fermait exactement quand les grosses lames devenaient
intéressantes à regarder — était juste tant qu'un coup de vent était la seule
météo qui existait : fermer la vue enlevait toute la récompense d'avoir levé la
mer. Elle cesse de l'être dès que le coup de vent apporte un ciel à lui,
couvercle au-dessus et pluie sur l'eau, car alors la crasse ne cache plus le
spectacle, elle **est** le spectacle — et une tempête à travers laquelle on voit
à huit kilomètres n'est pas une tempête. Elle arrive donc tard et raide : rien
du tout sous force 5, où la mer est vive et la journée belle, puis en quelques
crans jusqu'à un mille de visibilité. Le beau temps n'est pas touché.

**Le couvercle se pose EN DERNIER, et c'est tout ce qui compte.** Posé avant, il
était défait : la quasi-totalité du ciel qu'on regarde vraiment tient à moins de
vingt degrés de l'horizon, soit exactement la bande que le terme de brume est en
train de relaver en blanc. La tempête sortait alors en gris pâle au lieu de
sombre. Il passe donc après le halo solaire et après le voile d'horizon. Pas
d'une couleur plate non plus : un ciel couvert est le plus sombre au zénith et
se relève un peu vers l'horizon, où la lumière passe sous le bord des nuages —
et ce dégradé est l'essentiel de ce qui l'empêche de se lire comme un mur.

Trois choses suivent, et en oublier une trahit tout. Les **nuages s'effacent**
au lieu d'être poussés au maximum : un ciel couvert n'a pas de structure, et une
tempête qui montrerait encore des cirrus se lirait comme une belle journée mal
éclairée. Le **corps de l'eau** s'assombrit aussi — l'essentiel de la couleur
d'une mer est de la lumière du ciel rediffusée vers le haut, donc un couvercle
là-haut doit atteindre l'eau, faute de quoi la mer restait turquoise sous un
ciel d'ardoise, ce qui se lit comme deux images collées l'une à l'autre. Et
l'**environnement** partage l'uniforme de tempête, si bien que le navire est
éclairé *par* le gros temps et pas seulement *devant* lui : sans cela il
resterait en pleine lumière sous un ciel noir, la seule erreur d'éclairage que
personne ne rate.

**Plus de nuages dans l'eau**, sur demande. Un cirrus réfléchi est une bavure
grise qui se lit comme de la saleté sur la surface plutôt que comme du ciel : le
reflet est un lobe micro-facettes, donc tout ce qui a une structure fine revient
en bouillie. La règle du ciel unique tient toujours — c'est la même fonction, le
même code, la même météo, à qui l'on demande une couverture différente, la
quantité de nuage étant devenue un paramètre.

Mais il faut les **deux moitiés** pour que cela veuille dire quelque chose, et
la première tentative n'en avait qu'une. Le ciel analytique ne mettait plus de
nuages dans le lobe, mais la passe de **réflexion planaire** rend la scène
réelle, dôme compris, et le dôme les remettait aussitôt dans l'eau par la
texture. Mesuré caméra plongeante à 66°, de ciel bleu à couvert complet : 1,78 %
des pixels changeaient encore, contre **zéro** une fois le dôme éteint pour la
durée de la passe.

**La pluie est un RÉSEAU, pas une simulation** (`rain.js`). Chaque goutte porte
un point de départ fixe, et le vertex shader replie `graine − œil` dans une
seule boîte par un modulo avant d'y rajouter l'œil. Il en sort une boîte de
pluie toujours centrée sur le spectateur si loin qu'il navigue, avec une vraie
parallaxe — une goutte à un mètre file, une goutte à trente bouge à peine — sans
un seul repli à tenir, sans une position à réécrire et sans un octet de travail
processeur par image. L'origine flottante ne lui coûte rien pour la même raison :
tout est relatif à un œil qu'on lui donne à chaque image.

Trois détails la font lire comme de la pluie. Ce sont des **segments** et non des
points : ce que l'œil reconnaît est la traînée, un point qui tombe se lit comme
de la neige ou comme une saleté sur l'objectif. La traînée est ajoutée **après**
le repli, sinon une goutte à cheval sur le bord de la boîte serait coupée en
deux. Et l'**inclinaison** vaut à elle seule tout le reste : la pluie tombe à peu
près à la même vitesse par tous les temps, donc c'est le vent qui la couche, et
cet angle-là se lit instantanément et sans y penser.

Elle est coupée sous l'eau — un rideau de pluie vu d'en dessous n'a aucun sens —
et retirée de la **passe de réflexion**, son réseau étant replié autour de l'œil
réel : vue depuis la caméra miroir sous la surface, elle serait tout simplement
ailleurs.

## La gerbe

**Une gerbe n'est pas un effet accroché à un événement, c'est le volume d'eau
que l'objet vient de prendre à sa place, et qui arrive ailleurs.** Tout découle
de là, à commencer par les deux nombres qu'on donne à une gerbe, qui font deux
métiers différents :

- **combien d'eau**, en mètres cubes. C'est par là que le poids entre : un corps
  lourd s'enfonce davantage avant que l'eau ne l'arrête, donc il déplace plus, et
  ce qu'on lit comme « une grosse gerbe » est presque entièrement de la quantité
  — le nombre de gouttes, la largeur de la nappe, le temps qu'elle reste en
  l'air ;
- **à quelle vitesse il est entré**, en mètres par seconde. C'est ce qui décide
  de la **hauteur**, et rien d'autre ne le décide. Lâchez doucement un boulet et
  beaucoup d'eau bougera de très peu.

Le poids entre aussi dans la vitesse, mais par la porte de service : un corps
léger est arrêté par la surface et sa gerbe meurt avec lui, un corps lourd
continue comme si l'eau n'était pas là.

**L'eau sort PLUS VITE que le corps n'est entré.** Elle est chassée d'un
interstice qui se referme — c'est pourquoi un plat fait mal, et pourquoi une
coque qui tape à quatre mètres par seconde envoie de l'eau à cinq mètres de
haut et non à un. La première écriture la faisait sortir plus lentement que le
choc : le panache dépassait à peine la surface et se lisait comme de la mousse.

**Le déclencheur était déjà calculé, mais pas celui qu'on croit.** La première
écriture mesurait la coque qui descend — et cela rate la moitié de ce que l'œil
voit. Une lame qui monte à la rencontre d'une étrave immobile jette tout autant
d'eau : c'est même la définition d'une déferlante. La bonne question n'est pas
« à quelle vitesse la coque descend-elle » mais **« à quelle vitesse cette
cellule passe-t-elle sous l'eau »**, et elle ne demande pas lequel des deux a
bougé.

Elle se localise en prime toute seule. Une cellule déjà profonde ne compte pour
rien, puisque rien de neuf n'y est déplacé ; une cellule en l'air non plus.
Seules comptent celles qui **traversent** la surface, c'est-à-dire exactement
l'endroit d'où l'eau est jetée — sans pondération de profondeur à inventer et
sans flottaison à aller chercher. Il suffit de garder d'une image à l'autre le
taux de remplissage de chaque sonde.

**Une moyenne tombe toujours au milieu, et le milieu n'est pas où l'on regarde.**
Deuxième faute, plus subtile que la première et signalée à l'usage : « on ne
constate pas l'effet devant la coque quand le bateau retombe ou fend la mer ».
La moyenne pondérée des cellules qui traversent atterrit au maître-bau presque à
tous les coups, et non parce que la physique le dit — parce que la coque y est
la plus **large**, donc c'est là qu'il y a le plus de cellules. Moyenner une
étrave qui plonge à quatre mètres par seconde avec un milieu tranquille plein de
cellules donne un point au milieu : l'effet était là, et il n'était jamais où
l'œil se portait.

L'eau est jetée là où le travail est le plus dur, ce qui est un **maximum** et
non une moyenne. Pondérer la position par le **carré** de la vitesse de passage
tire le point dessus — seize contre un pour une étrave qui descend quatre fois
plus vite — tandis qu'une coque qui retombe à plat garde son point au milieu,
toutes ses cellules passant alors à la même vitesse et rien ne se détachant.
Mesuré : sur les impacts francs, **100 %** tombent en avant du tiers avant, et
jusqu'à 0,71 de la demi-longueur, soit au ras de l'étrave. La *taille* de la
gerbe, elle, garde la moyenne honnête en mètres cubes par seconde : ce sont deux
questions différentes et elles ont chacune leur accumulateur.

**Et le point est porté jusqu'à la RALINGUE de sa flottaison.** Le centroïde vit
dans le plan de flottaison, donc l'embrun montait à travers son propre pont et
se lisait comme de l'eau embarquée plutôt que de l'eau jetée.

L'écarter d'une distance fixe ne suffit pas, et ce fut la deuxième tentative :
une coque est **longue**, donc un point à huit mètres en avant du maître-bau,
poussé de deux mètres de plus, reste à quatre mètres de l'étrave — et l'embrun
court le long du pont de l'avant vers la taille, ce qui est exactement ce qu'on
a vu.

Il faut le poser **sur** son contour, pas le pousser vers lui. Ramenée à sa
demi-longueur et à sa demi-largeur, la coque devient un cercle unité :
normaliser là puis revenir pose la gerbe sur la flottaison, au relèvement d'où
le coup est venu, quelles que soient ses proportions. Un coup à l'avant crève à
l'étrave, un coup par le travers par-dessus bord, et une seule ligne
d'arithmétique fait les deux. Retombant à plat il n'y a pas de relèvement — la
moyenne est au centre — et c'est l'étrave qui est prise, là où une chute à plat
jette l'eau qu'on remarque.

**Un impact est un ÉVÉNEMENT, pas un état.** Elle chasse toujours *un peu* d'eau
vers le bas ; ce qui compte est le franchissement d'un seuil, suivi d'un temps
mort avant le suivant. Sans ce temps mort une entrée franche engendrerait une
gerbe à chaque image pendant un tiers de seconde, ce qui se lit comme un jet.

**Le seuil est une VITESSE, en mètres par seconde, et il a fallu une frégate
pour montrer pourquoi.** Il avait d'abord été écrit comme une fraction du volume
de coque par seconde, ce qui avait l'air indépendant du navire et ne l'était pas.
Le débit de traversée va comme la **surface** de flottaison multipliée par la
vitesse de passage, donc en L·B ; le volume de coque va en L·B·D. Diviser l'un
par l'autre laisse un D au dénominateur, et pénalise donc un navire d'être
profond : la frégate, trois fois le creux du chaland, sortait au tiers du débit
pour la même mer et ne jetait **rien du tout** — deux minutes de force 7 sans
une seule gerbe, et personne ne l'aurait deviné en regardant le chaland.

Divisé par sa section moyenne — volume de coque sur creux — il reste des mètres
par seconde, c'est-à-dire la même question posée à toutes les coques : à quelle
vitesse, en moyenne sur sa longueur mouillée, passe-t-elle sous l'eau ? Relevé
à 1,9 m/s, deux minutes de mer établie par état, en route :

| force | 3 | 4 | 5 | 6 | 7 | 9 |
|---|---|---|---|---|---|---|
| chaland, gerbes/min | 0 | — | 2 | 4 | 7 | 12 |
| frégate, gerbes/min | 0 | 2 | 4 | 4 | 4 | 9,5 |

Silencieuse sous force 4 sur l'une comme sur l'autre, ce qui est le point : un
seul nombre, et il veut dire la même chose partout.

**Et une gerbe toutes les cinq secondes au plus, ce qui est une borne assumée et
non un anti-rebond.** La physique trouve volontiers une douzaine d'impacts dans
ces cinq secondes et tous sont réels — mais douze panaches en cinq secondes ne
se lisent pas comme un navire qui travaille dans la lame, ils se lisent comme un
chapelet de pétards le long du bord. Ce que l'œil demande à une mer, c'est **une**
masse d'eau jetée, assez grosse pour être regardée, puis le temps de la regarder.

Un défaut à corriger dans la borne : une claque tire, et la lame verte qui monte
à bord deux secondes plus tard — celle qui valait le coup — est jetée parce que
la pendule n'avait pas fini. Un impact au moins **deux fois** plus gros que celui
qui tient la place peut donc la prendre, passé une seconde. Il reste rare par
construction, doubler étant beaucoup : relevé, l'écart minimal tombe à 5 s par
tous les temps sauf en tempête, où l'exception a joué une fois à 2,2 s.

**La vitesse d'éjection s'est trompée des deux côtés avant de tomber juste.** En
dessous de la vitesse de choc, la couronne dépassait à peine la surface et se
lisait comme de la mousse ; au double, une belle brise envoyait l'eau à cinq
mètres. Ce qui tranche est que la première de ces mesures a été prise pendant
que l'embrun naissait encore **dans** la coque, où la moitié ne se voyait pas :
la faute était l'endroit, pas la vitesse, et monter la vitesse pour compenser
soignait le symptôme.

**Et elle est mise à l'échelle de l'événement, ce qui est ce qui empêche un
grand navire de ressembler à sa maquette.** La pesanteur fixe la seule pendule
qu'ait une gerbe : de l'eau lancée à quatre mètres par seconde est montée et
retombée en huit dixièmes de seconde quoi qu'elle côtoie — donc à côté d'une
frégate de soixante mètres c'est un clignotement, et l'œil lit le clignotement
et dit « petit ». Le cinéma le sait depuis un siècle : une maquette se trahit
par une eau qui bouge trop vite pour sa taille apparente.

Le remède est la similitude de **Froude**, qui se trouve être la physique
exacte : pour un mouvement gouverné par la pesanteur, des écoulements
géométriquement semblables ont des vitesses en **racine de la longueur**. Le jet
porte donc un facteur √(R/Rréf) et tout le reste suit tout seul — la couronne
monte proportionnellement à R et tient l'air proportionnellement à √R, sous une
pesanteur ordinaire et sans rien truquer.

Cela sonne à l'envers de rendre une grosse gerbe plus **rapide** quand le
reproche était qu'elle avait l'air trop rapide. Ce n'en est pas un : la vitesse
absolue croît en √R tandis que la taille croît en R, donc ce qu'on voit — la
vitesse rapportée à la taille — décroît en 1/√R. Une grosse gerbe est une gerbe
lente, et c'est l'arithmétique qui le dit. Mesuré, chaland contre frégate :

| | chaland, L = 28 m | frégate, L = 60 m |
|---|---|---|
| rayon de gerbe | 3,3 m | 4,9 m |
| hauteur | 0,8 – 1,0 m | 1,5 – 2,1 m |
| durée de vol | 0,8 s | 1,1 – 1,3 s |

**La traînée aussi va avec la taille**, et par la même porte : elle croît comme
une surface quand la masse croît comme un volume, donc ce qui freine va en
1/taille. Sans cela chaque paquet décélère pareil et une nappe entière se
dissipe aussi vite qu'une gouttelette — la maquette revient par un autre
chemin.

**Et par grosse houle, ça partait en feu d'artifice — à cause de la fréquence
d'affichage.** Une cellule ne peut se remplir que d'une cellule entière en une
image, donc la vitesse de passage **sature à `probeH/dt`** : quelque trente-six
mètres par seconde à soixante images, et *davantage* sur une image lente. Ce
plafond est une propriété de l'horloge d'affichage et non de la mer. Pris au
mot, il envoyait l'embrun à seize mètres en l'air par forte houle — et plus haut
encore sur une machine plus lente, ce qui est la signature même d'une grandeur
qu'on n'aurait jamais dû lire telle quelle.

Sept mètres par seconde est la borne honnête : c'est à peu près la vitesse
orbitale de la mer la plus creuse de ce modèle, et une coque et une lame qui se
rencontrent plus fort que cela, c'est l'arithmétique qui manque d'images, pas
l'océan qui fait quelque chose de remarquable.

Le jet est en outre **borné par ce qu'une cavité de cette taille peut jeter** :
la couronne monte à peu près autant que la cavité est large, étant la même eau
repliée, donc un panache qui part à trois ou quatre fois son propre rayon a
cessé d'être de l'eau déplacée pour devenir une fusée. Et la taille d'un paquet
est plafonnée en mètres autant que mise à l'échelle — de l'eau déchirée ne tient
pas ensemble au-delà d'un demi-mètre, quoi qu'on l'ait lancée, sans quoi un gros
événement partait en poignée de rochers. Mesuré, hauteur maximale du panache sur
deux minutes :

| | force 6 | force 8 | force 9 |
|---|---|---|---|
| chaland, 28 m | 0,9 m | 3,6 m | 4,4 m |
| frégate, 60 m | 4,3 m | 5,5 m | 7,0 m |

Contre seize mètres avant les bornes. La frégate monte plus haut en mètres et
moins haut rapportée à sa longueur — un neuvième contre un sixième — ce qui est
exactement ce que la similitude demande.

**Et rien ne compte tant que les sondes ne sont pas remplies.** Tout ce qui la
DÉPLACE sans la faire naviguer — asseoir une coque neuve, renflouer une épave —
fait traverser la surface à toutes ses cellules d'un coup, ce qui se lirait
comme le navire entier qui tape. Trois images de chauffe suffisent.

**Piège de mesure, et il a failli passer.** Le premier comptage changeait l'état
de mer puis comptait aussitôt, et trouvait des gerbes par calme plat. Changer le
spectre fait **sursauter** la coque, et le sursaut déclenche un impact bien
réel. Quinze secondes de décantation avant de compter, et le calme ressort
silencieux à tous les seuils — comme il se doit. Un relevé pris juste après
avoir changé un réglage mesure le changement, pas le réglage.

**Second piège, du même banc.** Les rappels de test posés sur `onSlam` se
chaînent d'une expérience à l'autre : chaque essai gardait le précédent et
l'appelait, si bien qu'un ancien rappel écrivait par-dessus le résultat du
nouveau et l'on lisait la mesure d'avant en croyant lire celle d'après. Rendre
le rappel d'origine à la fin de chaque essai, ou recharger la page entre deux.

**Fin et nombreux plutôt que gros et rares.** L'embrun n'est pas un ensemble
d'objets, c'est une texture, et l'œil la lit à son grain. Trop peu de sprites et
l'on compte les points ; trop gros, et c'est de l'ouate — les deux ont été
essayés dans cet ordre. Et l'opacité reste bien sous l'unité, un panache étant
fait de dizaines de ces sprites qui se recouvrent : à pleine opacité ils
s'empilent en un corps blanc plein au lieu de se construire en quelque chose au
travers de quoi on voit.

Un sprite n'est pas **une** goutte, c'est un paquet d'eau déchirée et de l'air
qu'elle contient : dimensionné comme une vraie goutte, il disparaît à toute
distance et le panache entier se lit comme de la poussière sur l'objectif.

Le tout tient dans **un** objet `Points` et un seul appel de dessin — des sprites
avec chacun leur matériau conviennent très bien à une douzaine de bouffées de
fumée et pas du tout à deux cents gouttes. La recherche d'un emplacement libre
se fait au **curseur tournant** : une grosse gerbe en demande quatre cents d'un
coup, et repartir du début à chaque fois en faisait un quart de million de
comparaisons dans une seule image, pour une réserve presque vide. Coût mesuré :
**13 µs par image** avec sept cents gouttes vivantes.

**Et les débris de l'explosion giclent aussi.** Une planche qui touchait l'eau
était laissée à couler derrière une surface opaque, ce qui ne coûtait rien et
avait l'air juste — mais celle qui manquait l'eau tombait alors pour l'éternité,
et l'on jetait le seul moment où une planche qui retombe vaut d'être regardée.
Elle disparaît maintenant à l'entrée, en jetant son eau. Mesuré : quarante-quatre
planches à la mer pour une soute qui saute.

**Et les débris gerbent pour de bon en retombant.** La planche demandait neuf
dixièmes de mètre cube, ce qui décrit le bois lui-même et non le trou qu'il
perce : dix gouttes, un demi-mètre, invisible. Elle emprunte désormais la
**colonne** du boulet plutôt que la couronne de la coque — une planche qui
arrive à vingt-cinq mètres par seconde est bien plus près d'un projectile que
d'un navire qui s'assoit dans un creux — mais avec un plafond plus bas, donc
plus courte et plus large, ce que fait réellement un morceau de bois.

La taille est fixée par la **réserve** autant que par l'eau, et c'est elle qui a
tranché. Une soute qui saute jette une soixantaine de planches qui retombent en
quelques secondes, chacune avec sa colonne. La réserve est passée de deux mille
à **quatre mille places** — un tampon deux fois plus gros et rien d'autre de
mesurable, 76 µs par image au plus fort — et le volume à 4,5 m³ : relevé, 57
gerbes, un pic de 2 850 gouttes, **aucune saturation**, et des colonnes de plus
de six mètres. À six mètres cubes la réserve restait saturée quatre secondes et
demie, et le curseur tournant recyclait alors des gouttes encore en vol : les
premiers panaches étaient coupés en plein vol pour payer les derniers.

Enfin, la gerbe et les débris **se recentrent** avec le reste. Chaque goutte
tient une position dans le repère local ; sans cela un recentrage laisse
l'embrun suspendu à quinze cents mètres derrière. L'explosion avait la même
faute en sommeil depuis le début — trois secondes de vol suffisent à croiser un
recentrage.

## Le fret, et où on le met

**Le fret est la même chose que l'eau embarquée, et c'est délibérément la même
machinerie** : un poids, à l'endroit où il repose. La méthode du poids ajouté
était déjà écrite pour l'envahissement ; charger un navire n'a demandé que de
lui donner un second client. Une seule différence dans l'arithmétique, et elle
est la bonne : le fret est du poids **mort**, saisi et arrimé, donc il n'apporte
aucune carène liquide. Il alourdit et déplace le centre de gravité ; il ne
ballotte pas.

Il se range dans les **compartiments de l'envahissement**, parce qu'ils sont
découpés sur la grille de sondes : une cale est donc du vrai volume de coque,
avec une largeur et une hauteur mesurées sur le même plan de formes que tout le
reste. Trois hauteurs et cinq compartiments — assez pour que l'arrimage soit une
décision, pas assez pour que ce soit un tableur.

**L'endroit compte plus que la quantité, et c'est tout l'intérêt de pouvoir le
choisir.** Relevé sur le chaland, lège 210 t, avec 120 t de fret :

| arrimage | déplacement | immersion | assiette | gîte | GM |
|---|---|---|---|---|---|
| à vide | 210 t | 39 % | 1,2° | 0° | 2,37 |
| fond, au milieu | 330 t | 61 % | 1,8° | 0° | **2,48** |
| fond, **à l'avant** | 330 t | 61 % | **16,2°** | 0° | 2,48 |
| **sur le pont** | 330 t | 61 % | 1,8° | 0° | **1,85** |
| entrepont, **à tribord** | 330 t | 60 % | 1,7° | **−16°** | 2,18 |

Trois leçons, et aucune n'a été écrite à la main :

- **d'avant en arrière, c'est l'assiette.** Cent vingt tonnes dans la cale
  avant enfoncent l'étrave de seize degrés et portent le tirant de 2,35 m à
  5,48 — c'est ainsi qu'un navire mal chargé embarque la mer par l'avant ;
- **en travers, c'est une gîte qu'aucune barre ne rattrape.** Seize degrés pour
  le même poids mis à tribord, et deux cents tonnes en font trente-sept, ce qui
  est la limite du raisonnable ;
- **en hauteur, c'est le GM, et c'est la dangereuse.** Du poids sur le pont
  n'achète rien et coûte un quart de la hauteur métacentrique. Du lest au fond
  de cale fait l'inverse et la **raidit** — 2,48 contre 2,37 à vide. C'est la
  manière classique de perdre un navire par ailleurs sain, et elle est ici
  gratuite : elle sort du calcul du centre de gravité, pas d'une règle ajoutée.

**Sa capacité n'est pas un chiffre d'équilibrage** : c'est le poids qui l'amène
à 85 % de coque immergée, ce qui est déjà bas sur l'eau. Rien n'empêche de la
charger au-delà — pouvoir ruiner un navire en le surchargeant est précisément
l'intérêt — mais la console vire à l'orange puis au rouge bien avant qu'elle ne
s'en aille.

**Le déplacement affiché est devenu vivant.** Il donnait la valeur de la fiche,
qui ne bougeait jamais et ne disait donc rien ; il donne maintenant ce qu'elle
pèse réellement — lège, plus le fret, plus l'eau prise. Tout l'intérêt de
charger un navire est de le voir s'alourdir et s'enfoncer.

**Un piège d'ordre d'initialisation.** La capacité a besoin du volume de coque,
qui n'est connu qu'une fois les sondes construites — et les sondes se
construisent AVANT le bloc où vivent les autres champs. Un `= 0` d'apparence
inoffensive écrivait donc par-dessus la vraie valeur, et la console annonçait
une capacité de zéro. Le champ n'est plus initialisé là, et la raison est
écrite sur place.

## Les dépressions ont un lieu

**Écrites comme les îles, et pour les mêmes raisons** : un hachage sur une
grille grossière, pas de fichier de carte, pas d'état, rien à stocker. Revenir
sur la même eau y retrouve la même dépression. La différence est qu'une tempête
a aussi un **temps** — un système se déplace — donc c'est une fonction pure de
la position *et* de l'horloge, ce qui ne coûte rien de plus et offre un ciel qui
vient à sa rencontre autant qu'elle y navigue.

**La dérive est une onde triangulaire, pas une droite**, et ce n'est pas de la
paresse. Un centre qui file dans une direction s'en va sans borne : aucune
recherche de cellules voisines ne pourrait être sûre de le trouver. Prendre la
dérive modulo quelque chose le fait au contraire **sauter** en arrière — et un
saut, c'est un état de mer qui change de quatre Beaufort entre deux images. Un
triangle est **borné ET continu**, seule combinaison qui serve ici : c'est la
même raison qui fait réduire la phase de la houle modulo 2π plutôt que de
l'écrêter.

**Et l'amplitude doit dépasser la cellule**, ce qui fut la première faute.
Enfermé dans la sienne, un centre parcourait trois kilomètres pour un rayon de
trois et demi — moins d'un rayon — si bien qu'un navire à la cape voyait le
grain se retirer et revenir sur lui indéfiniment : relevé, l'intensité faisait
1 · 0,74 · 0,01 · 0,82 · 0,99 sur une demi-heure. Des accalmies, jamais de
délivrance ; et une dépression qui ne part jamais n'est pas de la météo, c'est
un lieu. Portée à plus d'une cellule, elle parcourt **24,4 km, sept fois son
rayon**, et le même navire immobile est dégagé dès la sixième minute, revisité
vers la trente-cinquième, puis tranquille deux heures durant. Attendre devient
un vrai choix à côté de s'en aller.

Le prix est une recherche sur **deux anneaux** de cellules au lieu d'un, soit
vingt-cinq hachages par image, ce qui ne coûte rien. Couverture mesurée sur
1 600 km² et six instants : **5,9 %** de la mer sous un grain, **28,8 %** à
portée d'en voir un à l'horizon.

Dimensionnées pour un navire et non pour une carte météo : onze kilomètres entre
candidates, deux à quatre de rayon. Une vraie dépression fait des centaines de
kilomètres et demanderait une semaine à traverser à la voile ; celles-ci se
franchissent en dix ou vingt minutes, ce qui est assez long pour être une
épreuve et assez court pour qu'on en sorte.

**Le creusement n'est pas linéaire.** Une dépression a une large épaule et un
cœur dur, donc l'essentiel de la traversée n'est que du mauvais temps et c'est
le dernier tiers qu'on retient. Relevé en approchant du centre, dépression de
3,5 km de rayon, pic 8,5 :

| distance | 11 km | 8,4 | 6,3 | 3,5 (lisière) | 2,4 | 1,4 | 0,5 | 0 |
|---|---|---|---|---|---|---|---|---|
| force | 0 | 0 | 0 | 0 | 1,8 | 5,5 | 8,0 | 8,5 |
| noirceur du ciel | 0 | 0,13 | 0,50 | 1,00 | 0,82 | 0,45 | 0,20 | 0,15 |

**Le vent tourne AUTOUR du centre**, et c'est ce qui fait lire un système comme
un système plutôt que comme une mer qui grossit. Surtout tangentiel avec un peu
d'aspiration vers l'intérieur, ce que fait une vraie dépression — et cela veut
dire qu'on peut trouver le milieu à la seule sensation du vent, sans baromètre.

**Une seule cible, chassée par le spectre.** Trois choses peuvent décider de
l'état de mer — la console, la météo automatique, et la dépression, qui
l'emporte sur les deux autres parce qu'une dépression ne négocie pas. Elles sont
composées en **un** objectif que le spectre poursuit ensuite, plutôt qu'en trois
appelants qui se relaient sur `setSeaState` : ainsi le plafond de vitesse
s'applique à toutes, ce qui compte surtout pour la dépression — y entrer est
exactement le cas où un saut sans borne rebrasserait toute la mer.

Le plafond ne mord pas, et c'est voulu : monter de 0 à 7,5 Beaufort demande
600 s au plafond, quand traverser 3,5 km à quatre nœuds en prend 875. **C'est la
géométrie qui gouverne**, le plafond n'étant là que pour le cas pathologique.

Corollaire : laissée à elle-même, sans météo automatique ni dépression, la
console **est** la mer, et le retard est tenu égal à elle. Sans cela un curseur
déplacé à la main serait doucement ramené vers la valeur où le retard en était
resté.

**Le grain se voit venir**, et c'est la moitié de l'intérêt : un quart de ciel
noir posé sur l'horizon, dans sa direction et nulle part ailleurs, qui s'efface
au profit du couvercle général une fois qu'on est dedans — il n'y a plus rien à
désigner quand on y est. Cela coûte un produit scalaire.

Le régler a demandé trois essais, et le premier était **géométriquement juste et
visuellement nul** : un grain à six kilomètres sous-tend ±29° et ne monte qu'à
13°, si bien que le cône étroit d'origine confinait tout à une bande de ciel de
cent cinquante pixels — mesurable à 8,9 % de l'image, avec un écart moyen de
25/255, et parfaitement invisible. En l'élargissant grossièrement on obtient
l'inverse : 98,7 % de l'image et 197 d'écart, tout le ciel et toute la mer lavés
de gris. Ce qui manquait au premier n'était pas l'étendue mais la **noirceur**.
Le réglage retenu donne 87 d'écart moyen à sa noirceur réelle — un banc gris
qu'on distingue nettement, avec du ciel clair de part et d'autre.

La dépression est aussi **portée sur la carte**, en disque doux plutôt qu'en
contour : on sait à peu près où est un grain, jamais où il finit. Et sous la
terre, étant de la météo et non de la géographie.

**Et le banc s'allume par en dedans.** Un grain vu de six milles n'est pas une
forme grise morte : il s'éclaire brièvement, quelque part le long de son front,
et c'est l'essentiel de ce qui le distingue d'un banc de brume. Cousin de
`strike()` et délibérément pas la même chose : un coup au-dessus d'elle éclaire
le pont, la toile et tout le ciel ensemble, ce qui est juste quand l'orage est
sur elle et faux à six milles, où l'on voit une tache de nuage s'allumer et
rien d'autre — pas de lumière sur les voiles, pas d'ombre qui bouge. Celui-ci ne
sort donc jamais du shader de ciel.

Court, aussi : la foudre proche porte une enveloppe de quatre pointes sur une
seconde parce qu'on est dedans et que le détail se voit ; à cette distance un
coup est un clignement, et le dessiner plus long le fait lire comme une lampe.
Et sur son **propre** relèvement, à quelques dizaines de degrés du milieu du
banc — un front entier qui clignote d'un bloc se lit comme un interrupteur.
Mesuré : un éclair toutes les 5,7 s à 0,53 de noirceur, allumé 5 % du temps.

**Deux erreurs de luminosité, opposées, et la seconde est instructive.** La
première version ajoutait 1,9 fois la couleur d'horizon : additive dans un ciel
que la brume étale ensuite sur la mer, elle sortait comme un second soleil posé
sur l'eau. La correction évidente — la rendre proportionnelle à l'horizon, comme
l'écume — est **exactement fausse**, et pour une raison qui vaut d'être retenue :
l'écume **réfléchit**, donc elle doit suivre la lumière ; un éclair **émet**.
Proportionnel à l'ambiante il s'éteignait la nuit, c'est-à-dire précisément à
l'heure où un grain lointain s'allume comme une lampe derrière un drap.

Il a donc sa couleur propre, bleutée, et ce qu'il ajoute ne dépend pas de ce qui
est éclairé par ailleurs — c'est ce qui en fait un éclair. Vérifié : contribution
**identique à midi et à minuit**, +53 au maximum et +16 en moyenne dans les deux
cas.

## Une barre qui n'est pas la vôtre

**Elle écrit dans le même `ctrl` qu'une main sur la roue** : un gouvernail de −1
à 1, une écoute, des voiles établies ou ferlées. Rien n'atteint le solveur, et
c'est le point — une barre automatique capable de pousser la coque tricherait,
et cesserait du même coup d'être une épreuve de la navigabilité du navire. Si
elle ne sait pas remonter au vent, vous non plus.

**Elle ne peut pas aller où elle vise**, si c'est dans le vent. Une chasse droit
au vent n'est pas un cap mais une suite de **bords**, et choisir lequel est
l'essentiel du métier. Le choix a de la mémoire : pris à neuf à chaque image sur
le côté où la proie se trouve, un but plein vent debout passe d'une amure à
l'autre à chaque instant et le navire reste en panne, à virer sans fin.

**ELLE ABAT, ELLE NE VIRE PAS VENT DEVANT.** C'est la décision qui sépare une
barre qui marche d'un navire planté dans le lit du vent jusqu'à la marée
suivante, et elle a coûté trois tentatives.

Le chemin le plus court vers un cap n'est pas toujours un chemin praticable.
Virer vent devant traverse le lit du vent, où les écoutes reviennent dans l'axe
et où il n'y a **plus aucune poussée** ; le navire perd son erre, et l'autorité
du gouvernail allant comme le **carré** de la vitesse, elle meurt avant que
l'étrave soit passée. C'est manquer à virer, à tous les coups.

Et ce n'est pas affaire de degré. La règle avait d'abord été écrite comme une
particularité du carré, sur un critère de vitesse — c'était faux : **rien** dans
ce modèle ne vire vent devant. Mise sur l'autre amure, abattée interdite, depuis
le meilleur de ce que chacune tient au près :

| | départ | au plus près | issue |
|---|---|---|---|
| goélette, aurique | 4,07 nds | 22° du vent | retombe |
| pirate, carré | 2,18 nds | 29° du vent | s'arrête à 0,96 nd |

Aucune ne passe. Le pirate, à qui l'on demandait 119° sur bâbord à neuf dixièmes
de nœud, a tourné de **cinq degrés en dix minutes** en ralentissant tout du long.

Elle fait donc le tour par l'**autre côté** — en s'écartant du vent, trois fois
plus loin mais voiles pleines sur tout le parcours, en gagnant de la vitesse au
lieu d'en perdre. C'est abattre en grand, c'est ce que les carrés faisaient
vraiment, et voilà pourquoi. La manœuvre **se verrouille** : abattre la rend
rapide, ce qui défait la condition même qui l'a déclenchée, et elle se
remettrait au lof à mi-tour.

**Ce qu'il ne faut PAS faire, et que j'ai fait deux fois.** Un terme intégral
dans le *gouvernail* ne peut rien : la barre était déjà à fond, et à un nœud la
toile la bat sans discussion. Et **faire faseyer** pour ôter la poussée — ce
qu'un marin ferait d'un navire coiffé — est exactement le remède qui tue : ne
pas être poussée est ce qui la maintient coiffée, donc la condition qui
déclenche le faseyement se confirme elle-même. Écrite sur un critère de vitesse,
elle se déclenchait à **zéro nœud**, avant que le navire ait jamais bougé, et il
est resté en panne pendant les seize minutes de l'essai. Rien ne fait donc
faseyer ; abattre est la réponse à être bloqué.

**Elle change d'amure sur la LIGNE DE BORD**, quand le but relève de son angle
de près sur l'autre bord — c'est le premier instant où l'autre amure le fait
porter. Une marge de quinze degrés paraissait raisonnable et ne l'était pas :
elle changeait d'amure alors qu'elle refermait très bien, et chaque changement
coûtait plus que le bord n'avait rapporté — la distance a oscillé entre 1 190 et
1 500 m pendant trois quarts d'heure sans jamais converger. La ligne de bord
porte en outre sa propre hystérésis, le but relevant du même angle de l'autre
bord dès qu'elle est passée.

Prolonger **au-delà** de la ligne de bord a été essayé aussi, en se disant qu'une
manœuvre aussi chère doit être rare. Ça n'apporte rien : à quatre fois la bordée
minimale, 756 m contre 754.

**L'angle de bord est MESURÉ, et sur la ROUTE, pas sur le cap.** C'est là qu'est
toute la difficulté, et ma première table s'y est trompée. Ce qui compte n'est
pas jusqu'où elle peut pointer mais où son gain au vent culmine — question
différente et toujours plus ouverte. Or ces navires **dérivent beaucoup** : le
pirate, cap à 50° du vent, fait en réalité route à **68°**. Une polaire prise sur
le cap le flatte du double. La mienne annonçait 1,23 nœud de gain là où la vérité
est 0,73, et j'ai ensuite passé une heure à me demander pourquoi il mettait
trente-trois minutes à gagner huit mètres.

Force 4, écoutes sur `optSheet`, cap tenu 150 s. Vitesse, route réelle, gain au
vent **sur cette route** :

| cap au vent | 40° | 50° | 60° | 70° |
|---|---|---|---|---|
| pirate, carré, 30 m | 2,33 | 3,09 | 3,72 | 4,22 |
| *route* | 56° | 63° | 71° | 79° |
| *gain au vent* | 1,29 | **1,40** | 1,23 | 0,82 |

*(Relevé refait après que le pirate est passé de 60 m à 30. À 60 m il donnait
1,94 nœud au cap de 50° pour 0,73 de gain, sur une route de 68° : la petite
coque dérive cinq degrés de moins et gagne presque le double. L'angle de
meilleur gain, lui, ne bouge pas — cinquante degrés dans les deux cas — donc
`closeHauled` n'a rien à changer.)*

| cap au vent | 35° | 40° | 45° | 50° | 60° |
|---|---|---|---|---|---|
| goélette, aurique | 2,25 | 2,71 | 3,14 | 3,54 | 4,22 |
| *route* | 52° | 55° | 58° | 62° | 70° |
| *gain au vent* | 1,39 | 1,56 | 1,65 | **1,66** | 1,44 |

Cinquante degrés pour le carré, quarante-sept pour l'aurique. Ni l'un ni l'autre
n'est l'angle réel d'un navire de ce gréement — un carré tient soixante-dix —
et les deux viennent de la mesure et non du souvenir : soixante-dix coûterait au
pirate **les deux cinquièmes** de sa remontée dans CE modèle. La barre mène le
navire qu'elle a.

Voir la dernière colonne : au travers, la route du pirate est à **97°** du vent.
Il **recule** au vent en traversant.

**Au près elle gouverne au VENT, pas au compas**, ce qui est la manière dont on
le fait vraiment : une saute est ainsi rattrapée avant d'avoir rien coûté. Et
les écoutes suivent `optSheet`, que le solveur calcule déjà pour tracer le repère
vert de la console — une définition, deux usagers.

**Elle vise la RIVE de sa distance de garde, pas le navire.** Un chasseur qui
gouverne sur le centre de sa proie l'aborde, ce qui n'est pas une manœuvre : elle
est dirigée sur un cercle autour d'elle et referme par la tangente. Et un
bâtiment sans gréement — le chaland — passe **sous machine** : une barre qui
tire des bords sans toile ne fait que dériver.

Vérifiée en chasse, but fixe à 900 m, départ cap opposé et vitesse nulle :

| | but au vent | par le travers | sous le vent |
|---|---|---|---|
| pirate | ne rallie pas | rallie en 9,6 min, à 85 m | 11,7 min, à 54 m |
| goélette | 700 → 327 m en 20 min | 5,9 min, à 42 m | — |
| chaland (machine) | 10,3 min, à 12 m | — | — |

**Le pirate remontait mal au vent, et ce n'était pas la barre : c'était sa
taille.** À 60 m il ne gagnait que 0,73 nœud et payait dix minutes et trois
cents mètres à chaque abattée — il convergeait, mais il aurait fallu des heures,
et un pirate placé au vent du joueur n'était pas une menace. Descendu à 30 m il
gagne **1,40**, ce qui règle le grief sans toucher à la barre : c'est
exactement ce que la mise à l'échelle du gréement carré annonçait, et
accessoirement ce qu'un vrai pirate montait — un navire rapide et ardent, pas
une frégate.

**Ce que la mesure a trouvé au passage.** L'autorité du gouvernail va comme le
CARRÉ de la vitesse, donc aux allures de voile les grosses coques n'obéissent
quasiment plus. Taux de giration du pirate, barre à fond, avant correction :

| lancée | 1 nd | 2 | 3 | 5 | 8 |
|---|---|---|---|---|---|
| giration | 0,03 °/s | 0,08 | 0,17 | 0,46 | 1,13 |

Ce n'est pas une constante recopiée : `rudderK` est bien mis à l'échelle de la
surface latérale, comme tous les coefficients hydro. C'est que l'inertie de lacet
du pirate vaut cent huit fois celle de la goélette pour seize fois le moment de
barre. La goélette, elle, manœuvre très bien.

Cela valait pour les frégates du joueur autant que pour le pirate — on ne s'en
apercevait pas parce qu'on les mène à la machine, où elles filent onze nœuds et
retrouvent leur gouvernail. `rudder.power` a donc été **multiplié par trois**,
sur demande, les trois carrés passant de 92 à 275 :

| `rudder.power` | 92 | 183 | 275 | 366 |
|---|---|---|---|---|
| à 3 nds | 0,18 °/s | 0,34 | **0,51** | 0,67 |
| à 5 nds | 0,46 | 0,91 | 1,35 | 1,78 |

Un quart de tour en trois minutes à trois nœuds, ce qui est juste pour un lourd
carré sous voiles, et sans nervosité à la machine (62° en une minute).

## Les grosses pièces

**Un coup de canon n'est pas une petite explosion**, et le traiter comme telle
est la manière de le rater. Une explosion est une boule qui grandit dans toutes
les directions et qui **monte**. Un canon est un **jet** : les gaz sortent par
le travers à une vitesse énorme, l'air les arrête en quelques mètres, et le tout
s'enroule en un gros nuage qui ne va plus nulle part — sauf sous le vent.

**Et c'est ce dernier point qui est tout l'effet.** La fumée de poudre est
lente, épaisse et durable, donc **le navire sort de dessous la sienne** et
laisse une file de nuages suspendus au-dessus de l'eau là où chaque pièce a
parlé. Tous les récits de combat parlent de la fumée : elle aveuglait la
batterie, elle cachait l'ennemi, elle disait où était le vent. Une bouffée qui
meurt là où elle est née se lit comme un effet ; un banc qui descend sous le
vent se lit comme de l'artillerie.

La fumée ne décroît donc pas vers l'**arrêt** comme celle de l'explosion : elle
relaxe vers la vitesse de l'air. C'est une ligne, et elle donne d'un coup la
dérive, le banc sous le vent et le navire qui se dégage.

**Mais vers une FRACTION de la vitesse du vent seulement** — trois dixièmes — et
il a fallu la regarder pour le voir. Prise à la pleine vitesse de l'air, une
bordée est balayée du bord avant que l'œil ait fini de la lire : 7,5 m/s font
une encablure en vingt secondes. Un nuage de poudre est froid, dense et chargé
de grains imbrûlés ; il **tient où les pièces ont parlé** et s'affaisse bien plus
lentement que l'air autour de lui. La stagnation d'abord, la dérive ensuite.
Mesuré à `LAG = 0,30` : le banc s'écarte de 16 à 33 m en huit secondes, soit
2,1 m/s dans un vent de 7,5.

**ET LE FEU SE DIFFUSE DEDANS**, ce qui est l'essentiel de ce qui fait sentir la
poudre. La flamme est **dans** sa propre fumée et non devant : pendant une
fraction de seconde le nuage qui vient de naître brûle de l'intérieur, orange à
la volée, refroidissant vers l'extérieur. Éclairée seulement du dehors — ce
qu'elle était — la flamme était cachée par la chose même qu'elle venait de
faire, et la bordée se lisait comme une machine à fumée.

Chaque bouffée porte donc un facteur d'embrasement qui décroît sur **son propre
âge** et non sur une horloge à part : elles naissent en roulement le long du
bord, donc chacune s'allume et refroidit à son propre rythme et le banc entier
ne s'embrase pas d'un bloc. Trois dixièmes de seconde, soit à peu près le temps
que la charge continue de brûler hors de la pièce.

Et la flamme elle-même est une **langue**, pas une étincelle : six sprites
plutôt que trois, lancés strictement vers le dehors le long de l'âme et
grandissant en chemin — donc un cône et non une boule — avec un ordre de rendu
qui empêche la fumée de l'avaler. De la lumière additive devant son propre nuage
est exactement ce qu'est une lueur de bouche.

**ET LA LUEUR ÉCLAIRE SON BORDÉ**, ce qui est une lumière et non un dessin. Le
billboard à la volée est le feu qu'on voit ; ceci est ce que ce feu **fait** —
un coup de jaune sur sa muraille, sur ses porte-haubans et sous la voile
au-dessus, parti avant qu'on l'ait tout à fait vu. Un sprite ne sait pas le
faire : il est devant le bois, pas dessus.

**Une RÉSERVE, bâtie une fois et jamais remise dans la scène**, et c'est toute
l'ingénierie de la chose. Three compile ses shaders contre le nombre de lumières
qu'il voit, donc ajouter une lumière et la retirer un dixième de seconde plus
tard fait recompiler **tous** les matériaux de la scène, deux fois, par pièce —
une bordée serait une douzaine de recompilations complètes et une saccade
visible. Elles sont donc créées au démarrage, laissées dans la scène, laissées
**visibles**, et commutées par leur seule intensité : zéro est éteint, le compte
de lumières ne change jamais, et rien n'est jamais reconstruit. Mesuré :

| | programmes |
|---|---|
| réserve, allumée puis éteinte | 16 → **16** |
| une lumière ajoutée à la scène | 16 → **18** |

Et les retirer ne les rend pas : le coût est payé de nouveau au prochain ajout.

Quatre lampes, parce qu'une bordée part en roulement : à un dixième de seconde
chacune et autant d'intervalle, trois peuvent se chevaucher et quatre est
confortable. La cinquième pièce à parler dans le dixième de seconde vole la plus
ancienne, qui est invisible — une lueur déjà en train de mourir.

Elle est posée un peu **en dehors** de la volée, la pièce tirant à travers sa
muraille : posée sur la volée même, elle se trouve à l'intérieur d'elle et lui
éclaire la batterie à travers la coque au lieu de ses œuvres mortes. Et elle est
**bornée en portée** — neuf mètres sur un navire de trente, moins du tiers de sa
longueur — parce qu'une lampe sans portée inonderait tout son bord et se lirait
comme un éclair.

**Son intensité, elle, a été réglée à l'œil sur une image où la FLAMME faisait
tout le travail**, et c'était faux d'un ordre de grandeur. Mesuré ensuite au
pixel, lampe seule et flamme éteinte, à 126 m : le premier réglage n'éclairait
que **six pixels**. Il ne faisait rien du tout, et ce qu'on prenait pour la
lueur était le sprite. Une lumière ponctuelle est en **candelas** et
l'éclairement va en I/d², donc les quelques dizaines qui paraissaient
raisonnables à côté d'un soleil réglé à 0,77 sont une bougie :

| intensité (cd) | 22 | 100 | **400** | 1 500 | 6 000 |
|---|---|---|---|---|---|
| pixels éclairés | 6 | 70 | **402** | 645 | 778 |
| gain moyen | 17 | 20 | **26** | 54 | 117 |
| gain du plus touché | 23 | 66 | **149** | 240 | 402 |

Quatre cents est le point où elle se lit franchement sans brûler : le pixel le
plus touché gagne 149 sur 765, moitié moins qu'à 1 500 et quatre fois moins qu'à
6 000, qui sortent tous deux en tache blanche. Leçon générale, et c'est la même
que celle des nuages : **un effet réglé à l'œil au milieu d'autres effets mesure
la somme, pas la part**. Il faut éteindre les voisins avant de juger.

**ELLE NE FABRIQUE PAS DE LUMIÈRE, ELLE EN RENVOIE** — et c'est mot pour mot la
faute que l'écume avait eue, refaite ailleurs. Écrite en gris fixe, la fumée
était aussi pâle à minuit qu'à midi : une bordée de nuit sortait en **tache
blanche éclatante**, seule chose de l'image à s'éclairer toute seule. Signalé à
l'usage, capture à l'appui.

Le terme à employer est la couleur d'horizon, parce que `setSun` la reconstruit
déjà pour l'heure, la saison et le gros temps : la fumée hérite des trois sans
rien savoir d'aucun. Relevé de sa luminance :

| soleil | +50° | +20° | +5° | 0° | −6° | −12° et moins |
|---|---|---|---|---|---|---|
| horizon | 0,878 | 0,814 | 0,719 | 0,687 | 0,319 | **0,074** |

Un facteur **douze** que la fumée ignorait entièrement. Normalisée sur un beau
jour pour que le plein jour ne bouge pas, et plancher à 0,18, un nuage éclairé
par rien du tout étant un trou noir dans la mer — il y a toujours du ciel. La
teinte vient avec : un banc au couchant vire au chaud tout seul, pour rien.
Mesuré sur le gris de la bouffée : **0,64 à midi** (contre 0,62 avant, donc
inchangé), 0,32 au couchant, **0,112 la nuit** et bleuté.

**Mais la FLAMME n'est pas touchée**, et c'est toute la distinction : la fumée
réfléchit, le feu émet. Le même partage que l'écume et l'éclair lointain, qui
avait déjà coûté un aller-retour de ce côté-là. Seul le gris est atténué ; le
terme d'embrasement garde sa couleur pleine quelle que soit l'heure.

**Grise, et disparue en dix secondes plutôt qu'en vingt-deux.** Le blanc se lit
comme de la vapeur ; c'est la grisaille autant que l'opacité qui fait lire
quelque chose qui a **brûlé** et non quelque chose qui a bouilli. Et elle s'en
va tôt mais toujours **progressivement**, la décroissance étant une courbe lisse
sur toute la vie plutôt qu'un palier suivi d'une extinction : un banc s'amincit
sur place au lieu de s'éteindre. Relevé, opacité moyenne : 0,73 · 0,40 · 0,27 ·
0,12 · 0,04, plus rien à 10 s.

**Les bouches sont lues dans le modèle, mais par le NOM DE MATIÈRE**, ce qui
est un écart aux vergues et aux mâts et demande d'être défendu. Ces deux-là ont
une forme qu'une règle peut énoncer — un espar est long et mince, un mât c'est
la même chose debout. Un tube de canon n'a pas cette chance : c'est un cylindre
court et épais, ce qui décrit la moitié des accessoires de pont. Pire, une
batterie entière est presque toujours **un seul maillage**, donc il n'y a même
pas un objet par pièce à tester. Ce qu'il y a, en revanche, c'est un modéliste
qui l'a déjà dit : les tubes du pirate portent une matière nommée
`black_canon`. Le contrat est donc **un mot dans un nom de matière**
(`/canon|cannon|gun/i`), ce qui est bien moins à demander qu'un mesh par
pièce, et un navire qui ne dit rien n'a pas de batterie et ne tire pas.

**Grouper D'ABORD, chercher le large ensuite** — l'ordre des deux est tout. Un
tube est posé en travers, donc il n'occupe que son propre diamètre en `z`
alors que les pièces sont à des mètres l'une de l'autre : un seul test d'écart
les sépare, le même qui range les vergues sur les mâts, et la volée est
simplement le sommet le plus au large de son propre groupe.

Fait dans l'autre sens, on en perd la moitié — ce que faisait la première
écriture. Prendre les sommets proches du point le plus large de **toute** la
batterie suppose que son flanc est un plan ; il ne l'est pas, il rentre vers
l'avant et vers l'arrière, si bien que les pièces de l'arrière sont en dedans
des pièces du milieu et tombaient hors de la fenêtre. **Six pièces trouvées là
où il y en a douze**, toutes sur l'avant — et c'est la forme même du navire qui
en était la cause. Corrigé, la lecture donne les douze, et l'on voit la muraille
rentrer : x passe de 7,54 à 6,72 m et y monte de 5,95 à 6,68 avec la tonture.

**LA BATTERIE SE LIT AU SENS DES TUBES, et elle a quatre groupes et non deux
bords** : tribord `+1`, bâbord `−1`, poupe `+2`, proue `−2`, choisis pour que
« l'autre » reste le négatif — `⇧G` sert bâbord depuis tribord et la proue depuis
la poupe par la même règle. L'ancienne lecture rangeait les sommets d'un côté de
l'axe et les groupait par écart le long de la coque : elle ignorait la direction,
et les deux pièces de retraite ajoutées au tableau de la Roter Löwe, à un mètre de
part et d'autre de l'étambot, seraient parties chacune dans une bordée et auraient
tiré par la hanche. Les sommets sont désormais regroupés en pièces dans les trois
dimensions (maille de 0,45 m, assez large pour qu'un tube fait de deux anneaux nus
ne soit pas coupé en deux), puis chaque pièce dit dans quel sens elle est longue :
en travers, un bord ; dans l'axe, poupe ou proue selon sa position. Relevé : la
Roter Löwe 6 · 6 · **2 en poupe**, le pirate 6 · 6 inchangé.

Chaque pièce porte son **calibre**, rapporté à celui de la bordée — 0,73 pour les
pièces de retraite — et il entre dans le seul `k` par lequel `guns.js` dimensionne
déjà boulet, flamme, fumée et trou. Une bordée ne sert que son groupe : la retraite
ne part jamais avec le travers. Le tir à la roulée n'a rien eu à changer : `(ω × d).y`
lit le tangage pour un tube dans l'axe exactement comme la gîte pour un tube en
travers. Toutes les matières « canon » sont lues, plus seulement le premier
maillage trouvé.

**UN BOULET DANS SON BORDÉ EST UN BOULET DANS SA BATTERIE** (`woundGuns`). Ce qui
traverse la muraille à hauteur d'une pièce brise son affût, rompt sa brague ou
tue ses servants, et c'est ainsi qu'un navire se retrouvait avec tout un bord
réduit au silence pendant que l'autre tirait encore. Chaque pièce prend des
dégâts selon sa distance au point d'impact, dans une travée de huit centièmes de
la longueur, et selon le calibre du boulet ; elle est démontée à un. Un boulet de
sa taille en plein sabord fait 0,8 — il en faut deux au même endroit, ou une pièce
plus lourde ; un coup à une travée ne fait rien. Les dégâts **s'accumulent**, donc
un bord percé encore et encore perd ses pièces une à une. Une pièce démontée sort
simplement de `_battery` : les bordées raccourcissent sans qu'aucun appelant ne le
sache, la barre automatique des conserves comprise. La console affiche
« Tribord 5/6 », barre en rouge un groupe réduit au silence, et la réparation
(`restoreMasts`, déjà appelée par les trois chemins de réparation) remonte tout.
Relevé : 0,8 puis 1,6 sur la pièce touchée, 0 sur ses voisines, bordée suivante à
cinq pièces. Pas de rendu : la batterie est un seul maillage, une pièce ne peut
pas y être cachée seule.

**UN COUP LAISSE UNE MARQUE, ET UN SECOND AU MÊME ENDROIT L'AGGRAVE**
(`ship.scar`, `Naval.applyScars`). Chaque navire tient sa liste d'impacts dans
**son** repère — vingt-quatre au plus — et ses uniformes ne sont donnés qu'à ses
propres matériaux, si bien que deux navires du même combat portent chacun leurs
blessures. Un coup à moins d'une travée d'une marque la creuse au lieu d'en
ouvrir une autre, la même règle qu'une voie d'eau qui travaille plutôt que de se
multiplier ; la force suit le calibre et plafonne à dix ; liste pleine, la
marque la plus légère cède la place.

**Pas une texture peinte, une liste de points.** Peindre dans les UV demanderait
une coordonnée de texture au point d'impact, donc un lancer de rayon sur le
maillage à chaque touche, et une coque procédurale n'a pas d'UV du tout. Chaque
fragment demande plutôt à quelle distance il est d'une marque : la texture du
modèle reste intacte, et toute coque — modélisée ou construite — les prend de la
même façon.

**DES GRIFFURES, PAS DES TACHES**, demandé sur un photomontage après un deuxième
jet encore en taches brûlées cerclées d'un semis d'éclats, qui se lisait comme de
la peinture jetée sur la coque. Un boulet qui ripe sur du chêne ne le brûle pas :
il arrache la face patinée en longues déchirures claires qui courent **avec le
fil**. Chaque marque dessine donc quelques traits fins de bois cru, surtout le
long du bordé, chacun dévié de quelques degrés, dentelé sur sa longueur, effilé
aux deux bouts, avec une lèvre sombre là où le sillon fait ombre — plus nombreux
et plus longs à mesure que la travée est retouchée, jusqu'à cinq. Tracés dans le
plan du bord qui les porte, lu sur la position de la marque : le long et en
hauteur sur un flanc, en travers et en hauteur sur le tableau. Larges de huit
centimètres, plus qu'une vraie entaille, pour tenir un pixel à la distance d'où
on la regarde.

**RIEN NE SE VOIT AUX DEUX PREMIERS COUPS**, et c'est la correction d'un premier
jet signalé à l'usage, capture à l'appui : brûlure quasi noire dès le premier
boulet, trou au troisième, et la coque avait l'air barbouillée plutôt que
canonnée. Un boulet dans du chêne fait un trou gros comme le poing, invisible à
la distance d'où on la regarde. La visibilité suit donc `(force − 2)/6` : nulle
jusqu'au deuxième coup, pleine au huitième. Relevé à 14 m, coups au même sabord,
griffures : **0** pixel au 2e coup, **399** au 3e, 1 095 au 5e, 2 848 au 8e, dont
neuf sur dix plus clairs — contre 22 238 dès le troisième au premier jet.

**ET ELLES SONT PEINTES QUAND LA FICHE LE DIT** (`appearance.impactMaps`), les
griffures calculées restant le repli : « trop uniformes », et c'était juste — un
motif tiré de quelques hachages se reconnaît à la troisième marque. La fiche
donne une liste de **variantes**, chacune une liste de **stades** de gravité ;
PNG à fond transparent, fusion normale, la griffure horizontale avec le fil.

**Une seule texture**, une ligne par variante et une colonne par stade, assemblée
sur un canvas au chargement : GLSL ES 1.0 ne sait pas indexer un tableau de
samplers, et la cellule se choisit donc par arithmétique, comme la ligne de
profil de chaque coque. Chaque marque tire sa variante, un angle de quelques
degrés, sa taille et son sens de hachages de sa propre position — donc la même
marque à chaque image — et se colle dans le plan de son bord, sans UV. Stade 1
au troisième boulet, fondu vers le 2 au cinquième, 3 au septième. Tant que les
images ne sont pas chargées, `uScarMaps` vaut zéro et les griffures calculées
tiennent la place. Le build embarque chaque chemin, deux fiches nommant la même
image la portant deux fois : quelques centaines de kilo-octets contre une table
de dédoublonnage. Relevé avec `impact_001` en trois stades : **0 · 379 · 610 ·
1 326** pixels aux 2e, 3e, 5e et 7e coups, planche de 1 536 × 512. Les
couleurs vont dans le diffus et sont donc **éclairées** — une brûlure au soleil
n'est pas le noir d'une brûlure de nuit, la leçon de l'écume et de la fumée — et
le bois brûlé devient mat. Greffe chaînée avec sa propre clé de cache, **avant**
la brume, et jamais sur la toile. Le repère du navire est recomposé depuis le
corps dans `syncTo`, la matrice du groupe étant celle de l'image précédente ; la
réparation efface tout. Limite : la touche est
posée sur la boîte du bordé et non sur le maillage, donc près des extrémités, où
la coque rentre, la marque peut tomber un peu à côté.

**Une pression, un coup ; la touche maintenue, la bordée.** Un appui fait parler
une seule pièce, et la batterie se descend d'avant en arrière appui par appui —
ce qui donne quelque chose à faire entre deux salves et correspond à la manière
dont on sert un pont quand on ne tire pas ensemble. Les deux gestes se
distinguent par le **drapeau de répétition du navigateur** plutôt que par une
horloge à nous : le premier événement d'une pression porte `repeat` faux et
tous les suivants vrai, et un verrou empêche un maintien long de lâcher salve
sur salve.

**La bordée part pièce par pièce, et à intervalles IRRÉGULIERS.** Elles étaient
tirées en roulement le long du bord, et pas pour la parade : une batterie lâchée
d'un seul coup est une seule poussée, et cela se lit comme un seul objet qui
casse. Même argument que les trois charges de la soute et que les mâts qui
tombent l'un après l'autre.

Mais un neuvième de seconde régulier entre les pièces est un **roulement de
tambour** — une machine, pas un équipage. Chaque canon a son chef qui attend son
moment, sa lumière, sa mèche ; les intervalles s'éparpillent, et de temps en
temps une pièce a un long feu et parle bien après sa voisine. Relevé sur quarante
bordées : durée totale de **0,38 à 1,86 s** (moyenne 0,81), écart médian entre
coups de 0,133 s, neuvième décile 0,347, et **15 %** de longs silences. Aucune
salve ne sonne comme la précédente.

**Et aucune charge n'est pareille.** La poudre était faite, dosée et bourrée à la
main : quelques pour cent d'écart sur la charge est généreux plutôt que
pessimiste, et cela éparpille la chute des coups tout seul, sans seconde règle
sur la précision.

**MINCE PAR BOUFFÉE, épaisse par accumulation.** Une bordée seule ne doit pas
être un mur : ce qui remplissait une batterie n'était pas un coup mais une heure
de coups. L'opacité s'empile en 1−(1−a)ⁿ, donc il faut descendre franchement
pour que le cumul se voie — à 0,95, **deux** sprites suffisaient à boucher
(0,9975) et la première pièce posait déjà un mur que rien ne pouvait épaissir.
Ramenée au cinquième, une bouffée est un voile et l'on voit la mer au travers
d'une bordée.

**Le cumul butait d'abord sur autre chose que l'opacité.** Cinq bordées coup sur
coup donnaient un voile plus mince qu'une seule bordée regardée à son sommet —
et ce n'était pas la densité (à 0,38 simulé, même image), c'était que le banc
s'étalait et dérivait pendant ce temps. Deux demandes se tiraient dessus :
« qu'elle disparaisse plus vite » et « que le cumul fasse un nuage opaque » ne
peuvent pas être vraies ensemble, la fumée qui s'accumule étant par définition
celle qui reste. La première a donc été **révoquée** au profit de la seconde.

**ELLE EST LOURDE, ET ELLE SE COUCHE SUR L'EAU.** Le banc vit maintenant dix-
sept à vingt-huit secondes au lieu de sept à onze, et surtout sa portance est
**négative** : une fumée de poudre est froide et chargée de grains imbrûlés,
donc par petit temps elle ne monte pas, elle s'affaisse et reste sur la mer en
nappe. C'est ce que dit chaque récit de calme après un combat, et c'est ce qui
la rend gênante — un nuage qui monte libère la vue, un nuage qui se couche la
bouche.

Il lui faut alors un **plancher**, faute de quoi elle passe sous la surface et
disparaît par en dessous. La mer est échantillonnée **une fois par coup** et non
par bouffée et par image : deux cents bouffées interrogeant l'océan coûteraient
plus cher que le solveur, et en vingt secondes la mer sous un banc n'a pas bougé
de ce qui vaudrait la peine. Même arbitrage que pour les gouttes de l'embrun.

**Et la montée se compte en SECONDES, la descente en fraction de vie.** Écrite
en `u*11`, la montée s'allongeait avec la durée de vie : en portant le banc à
vingt secondes, une bouffée mettait presque deux secondes à devenir visible, ce
qui n'a aucun sens — une bouffée se forme en un instant quoi qu'il lui reste à
vivre. La disparition, elle, est bien une affaire de proportion.

Mesuré, cinq bordées coup sur coup puis quinze secondes d'attente :

| | avant | après |
|---|---|---|
| bouffées en l'air | 204 | **408** |
| encore là 15 s plus tard | — | **299** |
| hauteur sur l'eau | montait | 4,9 m de moyenne, 6,9 au plus haut |
| dérive du banc | — | 25 m de moyenne, 37 au plus loin |

Le banc finit couché **entre les deux navires**, ce qui est exactement le rôle
qu'il jouait.

**La fumée de poudre est PÂLE**, et cela avait été raté dans le même sens à la
première écriture : à sept dixièmes de gris, cela se lisait comme un banc de
**brume** couché le long du bord. Un canon et une
soute se séparent par la couleur autant que par la forme — la suie est un
incendie à bord, le blanc est de l'artillerie — et la densité n'est pas un goût,
c'est la raison même pour laquelle la fumée comptait : elle **aveuglait**.

**Et elle a sa propre texture, ce qui vaut vingt lignes.** La bouffée de
l'explosion est un dégradé radial dont on a mordu le bord, ce qui lui convient :
la fumée d'une boule de feu est mince et on en voit quelques-unes. Dessinez-en
quarante l'une sur l'autre à pleine opacité — ce que fait une bordée — et chaque
bord mordu tombe au même endroit : les dégradés s'additionnent en un galet blanc
parfaitement lisse. Le remède est des **grumeaux dans la texture** et non
davantage de sprites : une demi-douzaine de taches décentrées de tailles
différentes donnent à chaque bouffée une silhouette déchirée, et quarante de
celles-là, chacune tournée à son propre angle, ne s'accordent jamais et se
lisent comme des volutes. Même leçon que les voiles : ce que l'œil lit d'abord
est le **contour**, pas l'ombrage dedans.

**Et elle le SENT.** Pas une secousse décidée — l'arithmétique dit autre chose
et il vaut la peine de la laisser parler. Un boulet de douze part à quelque
440 m/s, donc avec les gaz derrière lui une pièce rend environ trois mille
kilogrammes-mètres par seconde à travers ses tourillons, six mètres au-dessus du
centre de gravité. Mesuré sur la frégate de 2 000 t, bordée de six pièces :
**1,07° de gîte** et 0,68 °/s de roulis, pour 0,009 m/s de translation. La
bordée qui couche un navire est l'une des choses les plus répétées sur la marine
à voile, et c'est à peu près un mythe : on la sent, elle ne renverse rien.

### Le boulet

**Il vole pour de vrai, et c'est la TRAÎNÉE QUADRATIQUE qui fait tout le
caractère du boulet rond.** Une sphère est un projectile déplorable : avec un
Cd voisin de 0,9, 5,4 kg et onze centimètres, la décélération vaut `c·v²` avec
`c = ρ·Cd·A/2m ≈ 0,001` par mètre. Ce seul nombre produit, sans un cas
particulier :

| portée | 100 m | 200 | 300 | 400 | 500 |
|---|---|---|---|---|---|
| vitesse | 358 m/s | 296 | 244 | 201 | 165 |
| temps | 0,27 s | 0,57 | 0,93 | 1,38 | 1,95 |
| **chute** | 0,3 m | **1,4** | 3,6 | 7,5 | **14** |

*(Calibre du navire de 30 m. Le même tableau sur la coque de 60 m donnait 1,2 m
de chute à 200 et 9,1 à 500 : un boulet deux fois plus gros porte plus loin,
`c` allant comme l'inverse du calibre.)*

Et donc la **portée de plein fouet**, qui n'a pas été décidée : on se battait à
deux cents mètres non par sauvagerie mais parce que c'est la distance à laquelle
une pièce pointée à plat touche ce qu'elle vise. À 500 m on est neuf mètres
bas ; à 800, c'est sans espoir. Le boulet est plus petit et plus léger sur un
petit navire, et `c` va comme l'inverse du calibre — la masse en cube quand
l'aire est en carré — donc son boulet perd son erre plus vite, sans second
réglage.

**Le tir est POINTÉ EN DESSOUS**, et c'est le troisième pointage : les deux
premiers se sont trompés dans le même sens. Trois degrés en l'air envoyaient le
boulet huit mètres au-dessus de sa lisse ; **à plat aussi**, et la mesure a dit
pourquoi : cette batterie est à six mètres et demi au-dessus de la mer, donc une
pièce à plat passe par-dessus une coque dont tout le franc-bord est moindre.
Six touches sur six à 160 m, six trous ouverts — et **pas une tonne d'eau en
cinq minutes**, tous étant au-dessus de sa flottaison. Ce qui est juste, et
inutile.

La pièce est donc pointée pour poser son coup **sur la mer** à une portée de
référence, ce que voulait dire *plein fouet* et à quoi servait le coin de mire :
d'un navire haut sur l'eau et à bout portant, il faut abaisser ou l'on tire dans
le gréement tout l'après-midi. L'angle sort de la hauteur de la volée au-dessus
de l'eau, donc une batterie basse est pointée à plat et une batterie haute bien
en dessous, sans rien à régler par navire. Relevé après correction : quatre
trous sur six **sous** la flottaison, deux au ras.

**L'essai se fait sur un SEGMENT, jamais sur un point.** Un boulet franchit
quatorze mètres en une image à soixante images par seconde, ce qui est plus
large que la coque qu'il doit toucher : essayé point par point il la traverse à
tous les coups, ce qui est la manière classique d'écrire un projectile qui ne
touche jamais rien. Et le vol est sous-divisé sur la **distance** et non sur le
temps — quatre mètres par pas — puisqu'il va quatorze mètres par image au départ
et deux à l'arrivée.

**La coque qu'il frappe est celle qu'on VOIT, et c'est ici que le modèle et le
solveur se sont contredits tout haut.** La grille de sondes est bâtie sur
`hull-lines.js` — longueur, largeur, franc-bord, tirant, tout de sa fiche —
tandis que le `.glb` est un autre objet, mis à l'échelle sur sa seule
**longueur**. Sur le pirate le solveur met son pont à y = 2,8 et le modèle met
ses sabords à y = 6,0 : trois mètres d'écart. Le premier coup est passé **six
mètres au-dessus d'elle**.

On ne peut pas simplement préférer le solveur : le joueur vise le bordé qu'il
regarde, et un boulet qui traverse l'image de sa muraille doit compter. La
**forme** est donc prise sur le modèle (`_hullShell()`, découpée dans les mêmes
compartiments que l'envahissement), et ce qu'on remet à la voie d'eau est une
**fraction** de son creux plutôt qu'une hauteur en mètres. Une fraction veut
dire la même chose dans les deux repères — zéro à la quille, un au livet — et
c'est exactement ce que `breach()` demande, si bien que le trou finit là où
l'œil l'a vu entrer sans que ni l'un ni l'autre ait eu à bouger.

**Le trou va comme le CARRÉ du calibre**, parce qu'un trou est une aire. Il
avait été écrit comme un dixième de mètre carré tout sec, ce qui n'est juste que
pour une seule taille de pièce : divisez le navire par deux et ses canons
suivent, mais un trou fixe dans une coque de huit fois moins de déplacement est
quatre fois la blessure. Mesuré avant correction, cinq trous coulaient un navire
de 240 t en **quatre minutes** là où six en condamnaient un de 2 000 en un quart
d'heure. Corrigé : 0,025 m² au lieu de 0,10, et elle sombre en douze minutes.

**Et ce qu'il coûte passe par ce qui existait déjà.** Un trou dans son bordé est
la même voie d'eau que la touche `B` ouvre, donc Torricelli, la carène liquide
et l'envahissement par le pont prennent le relais sans une ligne écrite pour
l'artillerie ; un mât est la même chute que la soute provoque. Rien de ce qui
arrive à un navire canonné n'est un cas particulier — ce qui est toute la raison
d'avoir bâti les deux d'abord. Mesuré, une bordée de six pièces à 160 m, pompes
en route :

| minutes | 0 | 2 | 4 | 6 | 8 | 10 | 12 | 14 |
|---|---|---|---|---|---|---|---|---|
| eau embarquée | 0 t | 75 | 160 | 256 | 371 | 504 | 657 | **834** |

Le débit **s'emballe**, les pompes perdent, et une seule bordée bien placée la
condamne en un quart d'heure. Une bordée ne coule donc pas un navire d'un coup,
et c'est juste.

**LES ÉCHARDES**, et c'est la chose que tous ceux qui y étaient ont écrite et
qu'aucune image n'a jamais montrée : le boulet lui-même tuait très peu de monde.
Ce qui vidait une batterie, c'était le **bois**. Un boulet à trois cents mètres
par seconde ne perce pas un trou net dans soixante centimètres de chêne, il fait
éclater le bordé vers l'intérieur et lance un nuage de poignards de chêne en
travers du pont. Les journaux de chirurgiens sont pleins de plaies d'échardes et
presque vides de boulets. Un impact qui ne donnerait qu'une bouffée et un trou
manquerait tout ce qu'une touche voulait dire.

**C'est le MÊME bois que l'épave d'une explosion** — même géométrie, même
culbute, même disparition dans la mer, même gerbe à l'entrée dans l'eau —
**taillé autrement**. Une explosion jette des morceaux de navire : courts,
épais, lents, cul par-dessus tête. Un boulet jette des éclats : longs, minces et
très rapides. Toute la différence tient dans les nombres remis à la même
machinerie, ce qui est l'intérêt d'avoir la machinerie.

**La plupart partent vers l'INTÉRIEUR**, le long du coup, parce que c'est là
qu'elles vont vraiment — et étant à l'intérieur elles sont cachées par son
propre bordé, ce qui n'est pas une perte mais la lecture juste : elles sont
entrées dans elle. Un peu moins de la moitié ressortent par le trou qu'il vient
de faire, et ce sont celles-là qu'on voit. Elles naissent un peu **en dehors**
du bordé, faute de quoi celles qui sortent seraient créées dans le maillage
qu'elles sont censées quitter.

**Et elles sont PÂLES**, ce qui est vrai et règle la seule vraie difficulté à
les dessiner. Le dehors d'un navire est vieilli et goudronné ; le dedans d'une
planche ne l'est pas, donc ce qu'un boulet arrache de sa muraille est du chêne
**cru**, bien plus clair que tout ce qui l'entoure. Des éclats sombres sur une
coque sombre à une encablure ne sont rien du tout ; du bois frais s'y détache.
Elles ont donc leur propre matière.

Leur **section** est grossie comme celle du boulet et pour la même raison — à
cette distance une écharde vraie fait trois pixels — mais leur **vol** est
honnête. Relevé, bordée de six pièces à 80 m : 16 à 21 échardes par touche, de
**0,40 à 1,55 m**, lancées de 5 à 19 m/s, **123 en l'air** après la salve.

**Et il a fallu transmettre le point d'impact.** La touche ne rendait qu'une
*fraction de hauteur*, ce qui suffit à l'envahissement — elle dit où l'**eau**
entre — mais ne peut pas dire d'où le bois s'envole. Deux questions différentes
sur le même événement, et chacune veut son propre nombre : `onStrike` rend
désormais aussi le point monde et la direction du coup.

**Un défaut trouvé par le banc au passage.** Le tir à la roulée était écrit
« attendre que la volée descende », et un navire immobile par calme plat ne
satisfait jamais cela : toutes les pièces partaient sur l'expiration du délai de
deux secondes et demie. On attend tant que la volée **monte** ; parfaitement
immobile compte comme tirable. Un chef de pièce attend la roulée quand il y a
une roulée à attendre ; sans elle, il tire.

**Un mât demande TROIS boulets.** Ils étaient épais comme une cuisse aux
jottereaux et faits pour être canonnés ; le perdre est le prix d'un feu soutenu
et non d'un coup heureux. Vérifié : trois touches et il part.

**Et les deux premières SE VOIENT désormais**, ce qui manquait : un boulet
tranche une partie de ce qui est saisi au mât, donc chaque touche laisse deux
bouts rompus qui pendent — voir « Les bouts rompus ». Sans eux, un mât blessé
deux fois était rigoureusement identique à un mât intact, et l'on ne pouvait pas
savoir qu'on était en train de le user.

**Le boulet qui manque tombe à la mer**, et la gerbe est celle qui existait
déjà — six coups, six colonnes, à 248 · 248 · 256 · 256 · 293 · 304 m, ce qui
est bien le pointage à plein fouet.

**Mais elle était invisible, et pour une raison de fond.** Dimensionnée
honnêtement en volume — 1,6 m³ pour le trou qu'un boulet fait — elle sortait à
**0,7 m de haut avec dix-huit gouttes**, ce qui à la distance où l'on tire n'est
rien du tout. Le plafond de `splash.js` en est la cause, et il est juste
partout ailleurs : *la couronne d'une gerbe monte à peu près autant que la
cavité est large*. Une coque qui s'assoit dans un creux est émoussée et lente,
elle ouvre une cavité large et peu profonde. **Un boulet à trois cents mètres
par seconde en perce une étroite et profonde**, et l'eau qui se referme dessus
tire un jet de Worthington bien plus haut que la cavité n'est large — grand,
mince, et sans rapport avec la gerbe d'un navire. C'est exactement le cas que la
règle ne décrit pas, donc l'appelant peut relever le plafond (`jet`, absent =
1, rien ne change ailleurs) et dit pourquoi.

Et le **volume** est fixé par une contrainte qui n'a rien à voir avec l'eau : la
réserve de gouttes en contient deux mille et une gerbe en prend onze par mètre
cube, donc une bordée de six doit y tenir ou les dernières pièces volent les
premières par le curseur tournant. Vingt-six mètres cubes font 286 gouttes
chacune, 1 716 pour la salve. Relevé : **9,1 m de haut, 286 gouttes**, contre
0,7 m et dix-huit — et c'est la densité bien plus que la hauteur qui fait lire
une colonne à distance.

**Et la FORME sort du même nombre**, plutôt que d'un second bouton, parce que
c'est la même cause. Une cavité large et plate jette son eau en dehors et fait
une **couronne** ; une cavité étroite et profonde la tire presque à la verticale
et fait une **colonne**. Ce qui relève le plafond est exactement ce qui redresse
le panache, donc un seul facteur dit les deux : moins de gouttes dans la collerette
basse, plus raides au-dessus, et une bouche plus petite. Élancement mesuré,
hauteur sur demi-largeur :

| | avant | après |
|---|---|---|
| boulet | 0,89 | **1,74** |
| coque, frégate | — | 0,54 |
| coque, chaland | — | 0,49 |

Un appelant qui ne demande rien reçoit la couronne **inchangée dans le moindre
détail** : `jet` absent donne `tight = 0`, et chacun des quatre termes retombe
alors exactement sur son ancienne valeur. La gerbe de coque ne bouge pas d'un
pouce — 420 gouttes et 2,4 m avant comme après.

**Le boulet est dessiné bien au-dessus de sa taille** (0,37 m de rayon pour onze
centimètres réels), et c'est délibéré : à un demi-mille il ferait un tiers de
pixel et n'existerait tout simplement pas. Même argument que la lueur lointaine
de la lanterne, et même réponse. Son **vol** est exact ; seul son diamètre est
un mensonge.

Rentré d'une fois et demie, de 0,55 m à 0,37, sur demande. Le premier chiffre
avait été réglé sur ce qui se voit à la distance où l'on tire et jamais vérifié
de près, où le boulet quittait une bouche plus petite que lui. Six fois nature
plutôt que neuf — ce qui reste le principe, simplement sans le baril.

**ELLE TIRE À LA ROULÉE**, et ce n'est pas un raffinement : sans cela les pièces
ne servent à rien dès qu'il y a de la mer.

Le pointage vaut huit centièmes de degré. Son mouvement en vaut plusieurs, et il
va droit dans le canon. Chaque pièce attend donc son moment, comme son chef le
faisait : une fois son tour venu dans le roulement, elle tient jusqu'à ce que la
volée **descende**, et parle alors. C'est le plus vieux tour d'un pont de
batterie et la raison pour laquelle les bordées étaient déchirées — chaque chef
jugeant sa propre roulée, ce qui est la même irrégularité que le roulement, mais
née plutôt qu'imposée. Et il ne peut pas attendre indéfiniment : deux secondes et
demie, puis il tire quoi qu'elle fasse.

**Qui descend, et non qui est horizontale**, ce qui a demandé un détour. Une
pièce pointe **en travers**, donc son **tangage** ne la touche presque pas :
tourner autour de l'axe transversal laisse un tube transversal où il était. Ce
qui lève un canon, c'est sa **gîte** — et sous voiles elle en porte une
permanente, la batterie au vent regardant le ciel et celle sous le vent
regardant l'eau, ce qui fut vrai de tout navire ayant jamais combattu à la
voile. Attendre l'horizontale n'arrivait donc jamais au vent, et toutes les
pièces partaient sur l'expiration du délai : les mêmes 800 m par calme plat que
par gros temps, ce qui est la façon dont la faute s'est signalée.

**Et la pièce est pointée sur l'HORIZON, pas sur son pont.** Un chef de pièce
vise le long de son tube la flottaison de l'ennemi et joue du coin de mire
jusqu'à ce que ça porte ; la gîte est son affaire, pas celle du boulet. Pointée
sur le pont — ce que revient à faire prendre l'assiette de la coque telle
quelle — la gîte permanente décide de tout, et la mesure fut brutale :

| batterie, force 4 | avant | après |
|---|---|---|
| au vent | 830 m | **274 m** |
| sous le vent | 45 m | **241 m** |

Un bord visant la lune et l'autre tirant dans sa propre muraille. Corrigé, les
deux tombent dans la même fourchette de 200 à 320 m, par calme comme par force
4, et la dispersion qui reste est celle des charges et du roulis.

**Ce qui reste à faire** : le gisement. Les pièces tirent perpendiculairement au
bord — on choisit le moment et le bord, pas encore la direction.

## Porter de la toile coûte de la toile

**LA VOILE EST LE FUSIBLE DU MÂT**, et c'est la phrase qui porte toute la
fonctionnalité. Une couture lâche, la voile éclate hors de ses ralingues et s'en
va — et c'est une BONNE nouvelle pour le navire, parce qu'elle emporte avec elle
la charge qu'elle mettait dans l'espar. Un gréement se sauve en perdant son
tissu. Le mât ne se perd que si l'on insiste au-delà de ce que la toile
elle-même pouvait encaisser.

**LE SEUIL EST UNE MESURE, PAS UN RÉGLAGE.** Relevé sur le galion pirate à
pleine voilure, bordée au mieux, quarante secondes par état de mer :

| force | 5 | 6 | 7 | 8 | 9 | 11 |
|---|---|---|---|---|---|---|
| N/m² | 89 | 154 | **215** | 175 | 437 | 561 |

On ferlait à force 7, et c'est très exactement là que la pression franchit deux
cents. `CANVAS_STRENGTH` vaut donc **220 N/m²** : le chiffre n'a pas été choisi,
il a été trouvé, et il tombe où l'histoire le met.

Une **PRESSION** et non une force, ce qui est tout le point : de la toile cède
au newton par mètre carré. C'est pour cela que **prendre un ris ne protège pas
ce qui reste dehors** — il réduit la surface exposée, donc le NOMBRE d'occasions
de déchirer par minute, jamais la tension sur ce qui porte encore. Vérifié à
force 11 : à pleine voilure la première voile part à 5 s et tout est parti en
trois minutes ; à 35 % de voilure la première part à 18 s et tout est parti
quand même. **Seul le ferlage met à l'abri** — à voilure nulle, rien, jamais.
C'est exactement la décision qu'on voulait rendre.

**Le risque est un TAUX, pas un seuil qui claque.** Même forme que le
déclencheur de gerbe : un franchissement, puis une probabilité par seconde, et
le carré de l'excès pour que force 7 pardonne et que force 9 ne pardonne pas.
Profil de la charge **soutenue**, deux minutes par état :

| force | moyenne | pic | temps au-dessus de 220 | au-dessus de 440 |
|---|---|---|---|---|
| 7 | 180 | 249 | **15 %** — les rafales seules | 0 % |
| 9 | 272 | 487 | **75 %** | 2 % |
| 11 | 455 | 670 | **100 %** | 54 % |

**Un seul nombre, deux usagers**, comme partout ailleurs ici. `ship.whole()` rend
la part de toile encore entière, pondérée par la surface exactement comme
`standing()` l'est par les mâts, et le solveur la multiplie dans la pression
pendant que le modèle cache le tissu correspondant. Elle sort en outre des DEUX
côtés du rapport qui donne `sailLoad` — la force tombe avec la voile, et la
surface qui la porte aussi — de sorte que ce qui reste dehors est sous
exactement la même pression qu'avant. C'est le fait physique, et c'est ce qui
fait qu'une voile qui éclate n'en sauve aucune autre.

Piège attrapé au passage : `setTrim` repasse soixante fois par seconde et
remettait **toute** la toile visible. Une voile déchirée serait revenue à
l'image suivante.

**L'ESPAR DEMANDE PLUS QUE LA TOILE, et l'avoir écrit autrement a coûté deux
réglages.** Premier essai, le mât passait par le compteur de blessures des
boulets — trois et il s'en va. Mathématiquement impossible : un mât ne porte que
deux ou trois voiles, donc il ne pouvait pas atteindre trois. Relevé, force 11,
cinq voiles perdues et **zéro mât**.

Deuxième essai, une cause propre au même seuil : le mât partait alors **avant**
la première déchirure — à trois et six secondes, zéro voile perdue d'abord — et
un fusible qui saute après le circuit ne sert à rien. Il lui faut donc sa propre
barre, **plus haute** : 2,6 fois ce que la toile tient, soit 572 N/m², quand une
force 11 donne 455 de moyenne et 670 de pic. **C'est la rafale qui casse le mât,
pas le coup de vent**, et le danger suit la toile que CE mât porte encore —
celui dont le fusible a sauté est hors de cause.

**Piège de banc, et il a failli faire régler de travers.** Lâcher la coque
immobile dans une force 11 déjà levée donne un coup de charge à 670 N/m² que le
jeu ne produit jamais : la mer y monte progressivement, le spectre étant tenu en
retard sur le vent. Le banc honnête fait monter la mer de 5 à 11 en deux
minutes, et alors l'enchaînement est celui qu'on cherchait :

| essai | 1re voile | mât | voiles perdues d'abord |
|---|---|---|---|
| 1 | 100 s | **104 s** | 2 |
| 2 | 95 s | **99 s** | 1 |
| 3 | 86 s | — | toute la toile, aucun mât |

**Et `Naval.app.tempete(où)` va chercher le gros temps**, parce qu'on ne règle
pas cela en attendant qu'une dépression veuille bien passer. Une dépression est
une fonction pure de la position et de l'heure, donc on n'en fabrique pas : on
va à elle. `Storms.nearest` balaie des anneaux de cellules de plus en plus
larges jusqu'à trouver, et le déplacement se fait en coordonnées LOCALES — le
recentrage de la boucle s'occupe du reste à l'image suivante, ce que l'origine
flottante rend gratuit. L'argument dit où l'on se pose : **0 le centre, 1 la
lisière**, où l'intensité est nulle par définition.

**UN CENTRE DE DÉPRESSION N'EST PAS FORCÉMENT DE L'EAU**, et l'avoir supposé a
coûté un bug signalé à l'usage avec capture à l'appui : la coque arrivait
**enterrée dans une île**, trente-huit mètres dans le fond, et ce qu'on voyait à
l'écran était son propre gréement depuis l'intérieur de la colline. C'était
évident après coup — un grain se hache sur la position exactement comme les
îles, donc rien ne les empêche de tomber au même endroit, et rien ne les en
empêchera jamais puisque ni l'un ni l'autre ne consulte l'autre. **C'est au
transport de chercher de l'eau, pas à la météo de l'éviter.**

Et le remède évident était insuffisant, ce qui est la vraie leçon. Chercher de
l'eau **dans** le grain choisi ne suffit pas : un grain posé sur une île n'en a
pas du tout, et la spirale finissait à huit kilomètres de son centre, au calme —
de la mer sans vent, c'est-à-dire l'inverse de ce qu'on venait chercher. Puis,
resserrée, elle trouvait le bord : relevé, posée à 2 529 m d'un rayon de 2 873,
soit **force 0,3**. Il faut donc **choisir la dépression sur ce critère** plutôt
que de la subir, d'où le filtre que `nearest` accepte désormais — et il exige de
l'eau dans la **moitié intérieure**, là où le grain vaut encore quelque chose.
Deux limites, une pour choisir et une pour poser. Vérifié : force **8,9** à un
mètre du centre, quarante-cinq nœuds, échouage nul.


## Une conserve qui tire

**IL NE MANQUAIT QUE LA DÉCISION.** `guns.targets` valait déjà la flotte
entière, donc un boulet cherchait n'importe quelle coque ; `broadside` prend son
navire en paramètre, donc rien n'y supposait le joueur ; et la barre automatique
vise déjà la **rive** de sa distance de garde et referme par la tangente, ce qui
*est* une position de combat. Personne ne disait jamais à une conserve de faire
feu, et c'est tout ce qui a dû s'écrire.

**LE PAVILLON NOIR EST UNE DÉCLARATION, PAS UNE DÉCORATION.** Un navire est
hostile parce qu'il arbore la tête de mort — `appearance.ensign === 'jolly'` —
et non parce qu'une fiche porterait un drapeau booléen à côté. C'était
précisément ce qu'un jolly roger voulait dire, et le **lire** plutôt que le
redire évite d'avoir deux vérités à tenir en accord. Hisser la tête de mort sur
n'importe quelle fiche suffit donc à en faire un ennemi, ce qui est le bon
contrat : rien par navire, comme le reste du gréement.

**TOUS LES AUTRES SONT PACIFIQUES JUSQU'À CE QU'ON LES TOUCHE**, et la riposte
n'a rien à deviner : le boulet portait déjà le corps qui l'a lâché (`b.from`), et
personne ne le lui demandait. C'est le seul renseignement qui ait dû voyager
jusqu'à `onStrike`. La victime se retourne contre celui-là, quel qu'il soit — le
joueur, un pirate, ou une autre conserve. **Personne n'arbitre les camps, ils se
forment.**

**Le bord est CHOISI sur le relèvement, pas deviné** : tribord vrai est
`étrave × haut`, et le produit scalaire en donne le signe. Elle ne parle que
lorsque la cible relève franchement par le travers — à moins de 0,62 du travers
elle se tait — parce qu'une pièce pointe en travers et qu'il n'y a pas de
gisement dans ce modèle. C'est exactement la limite du joueur, et elle oblige à
manœuvrer au lieu de mitrailler. Trois cent quarante mètres de portée, parce que
c'est là que s'arrête le plein fouet.

**Deux choses vivaient ailleurs et ont dû suivre**, et chacune rendait la
fonctionnalité muette :

- **la distance de garde**. Réglée à une longueur et demie, elle amène à
  quarante mètres — un abordage, pas un duel. Un hostile se tient à 185 m, dans
  la fourchette du plein fouet ;
- **la soute**. Le remplissage vivait dans `commission()`, le chemin du joueur,
  si bien qu'une conserve naissait **gréée de douze pièces et à sec de poudre**
  — et un pirate hostile sans une charge n'est qu'un navire qui vous suit.

**Et rien de ce qui ARRIVE n'a été écrit pour l'occasion**, ce qui est le point :
le boulet d'une conserve ouvre la même voie d'eau que la touche `B`, blesse un
mât par le même compteur que celui du joueur, et Torricelli prend le relais.
Relevé, deux galions par le travers à 150 m :

| | |
|---|---|
| pirate hostile, trois bordées | 18 coups, **3 voies d'eau** et **1 mât blessé** chez le joueur |
| conserve pacifique canonnée | 42 voies, puis **elle riposte** — 480 charges tombées à 474 |
| sa barre | vise désormais son agresseur et non plus le vaisseau amiral |


**LE PIRATE NE COULE PAS CE QU'IL VEUT PILLER** (`piloterPirate`). Un pirate ne
gagne rien à envoyer une prise par le fond : il la canonne jusqu'à ce qu'elle ne
puisse plus fuir ni se défendre, puis il vient à couple. Quatre temps, portés par
**son** entrée de flotte (`e.pirate`) et nulle part ailleurs :

- **chasse** — la proie non pirate la plus proche, à moins de quatre kilomètres,
  qu'il n'a pas pillée dans le quart d'heure : le joueur **ou n'importe quelle
  conserve**. Il la canonne à 185 m ;
- **abordage** — dès qu'elle est **abîmée** il cesse le feu (ses propres boulets
  frapperaient la coque qu'il vient vider) et vient par l'arrière : dans son
  sillage d'abord, puis à couple par la hanche. C'est le seul secteur où une
  bordée ne porte pas, une pièce pointant en travers — seules des pièces de
  retraite peuvent l'y chercher, et c'est ce qui leur donne leur raison d'être ;
- **pillage** — tenu cinq secondes à moins de 0,45 de la somme des longueurs et
  à moins de 2,5 m/s d'écart de vitesse. Sur le joueur : la moitié de la bourse
  et toutes les épices, par `unloadKind` — la cale fait foi. Sur une conserve à
  portée de vue, un message ;
- **fuite** — trois minutes à l'opposé de sa victime, qu'il ignore quinze.

**« Abîmée » se lit sur ce qui existe déjà**, sans compteur de points de vie à
côté : plus de 6 % du volume de coque embarqué, un mât perdu, un quart des pièces
démontées, ou quatre voies d'eau. Ce sont les dégâts réels qui décident, et une
proie qui lui échappe au-delà de 1 500 m le renvoie à la chasse.

Le point de fuite est **local** et glisse donc au recentrage, avec le reste. La
barre n'a pas eu à changer : elle vise un point et contourne une distance de
garde, et le pirate lui donne l'un et l'autre selon son humeur — 185 m en chasse,
zéro pour aborder ou fuir.

## Ce qui remonte d'un naufrage

**UNE OU DEUX PLANCHES, UN TONNEAU, ET PARFOIS UNE BOUTEILLE** (`flotsam.js`). Le
naufrage est lu sur `physics.foundered`, pour chaque coque de la flotte et une
seule fois (`WeakMap`) — rien n'a eu à prévenir le module. Planches et tonneaux
sont du **décor**, et c'est voulu : ils marquent l'endroit, et une mer avec un
tonneau dessus se lit comme un lieu où il s'est passé quelque chose. La bouteille,
un naufrage sur six, est la seule chose qu'on repêche — et le navire qu'on
commande n'en jette pas, une bouteille étant le dernier geste de quelqu'un.

Ils **remontent** de l'épave, flottent sur la hauteur de la houle et s'inclinent
sur sa pente (trois échantillons par objet, sans le solveur), **dérivent** à
quelques centièmes du vent, et **s'enfoncent** en fin de vie — quinze minutes,
trente pour la bouteille. Tenus en **mètres monde vrais** et posés contre
l'origine à chaque image, comme la terre : rien à décaler au recentrage. La
bouteille est dessinée trois fois nature, un vrai flacon étant un point à une
longueur de coque, et **portée sur la carte** (`chart.marks`) d'un point cerclé :
sans quoi on ne la retrouverait jamais dans la houle.

**ET ELLE EST DANS UNE BULLE**, demandé sur une image parce qu'on la perdait dans la
houle : une sphère de savon plutôt qu'une lumière — presque rien de face, claire au
bord (Fresnel), la dérive arc-en-ciel d'un film mince, un reflet du soleil et
quelques étincelles qui courent dessus. Ajoutée à l'image sans écrire de
profondeur, donc elle ne cache ni la bouteille ni ce qui est derrière, mais testée
en profondeur, donc la mer la coupe à la flottaison ; `clipping:true`, la leçon du
câble d'ancre. Elle **émet** — seule chose ici qui doive luire, et la nuit est
justement quand il faut encore la trouver. Au-delà de `markFrom` (60 m) une lueur
tenue à quelques pixels quelle que soit la distance prend le relais, la lueur
lointaine de la lanterne. Réglages dans `bottle.halo` de `Props.json`, taille en
mètres quelle que soit l'échelle de la bouteille. Relevé : à 8 m, 3,7 % de l'image
éclairée, +32 en moyenne et +188 au bord ; à 300 m, la marque seule.

**On la repêche en passant** : à moins de dix mètres et de deux nœuds. Ce qu'elle
contient se tire **à l'ouverture** et non au naufrage — ce qu'on lit dépend de qui
la trouve, pas de qui l'a jetée — et le module ne le sait pas, la page en décide :

- un **journal de bord**, le nom du navire et deux lignes du capitaine, sans effet ;
- le **cours de son port d'origine**, exact et **daté du naufrage** — le port le
  plus proche de sa mise à l'eau, retenu sur son entrée (`origine`) ;
- une **carte** menant à une **cargaison échouée** sur les hauts-fonds d'une île,
  hors du relèvement de son port, là où le fond remonte à environ un mètre : une
  caisse et un tonneau, le dessus hors de l'eau, et une croix sur la carte. Le
  navire ne peut pas y aller sans talonner, donc on la prend en **s'arrêtant à
  moins de 180 m** — on envoie le canot. Elle rapporte trois à huit tonnes
  d'épices ou un coffre de 150 à 500 écus.

L'encart est celui de la rumeur, titre changé : il ne met pas le navire en panne
et suspend la compression du temps tant qu'il est ouvert. Deux fautes de français
au passage, la règle vivant avec les noms : « il y a il y a », `age()` le disant
déjà, et « de Le Carénage » — `deNom` et `aNom` contractent l'article.

**Leurs réglages vivent dans `props/Props.json`**, un bloc par objet : `scale`,
`glb` (null = dessiné par le code) et `rotation` pour orienter un modèle,
`draft` (enfoncement en mètres, non mis à l'échelle), `life`, et pour la bouteille
et la cargaison leurs rayon et vitesse de repêchage ; `wreck` borne le nombre de
débris. **La fréquence de la bouteille n'y est plus** : c'est une règle de jeu et
non une propriété de l'objet, donc elle vit dans `settings.json`
(`wreck.bottleOneIn`, 6 — une fois sur six, 0 pour jamais), que la page passe au
module (`flotsam.bottleOneIn`). Relevé sur 1 200 naufrages : 16,5 %. Le fichier l'emporte champ par champ sur
`Naval.PROPS_DEFAULTS`, qui porte les mêmes valeurs — un fichier absent ou à moitié
écrit ne change rien. Servi par le serveur de développement, embarqué par le build
en `Naval.PROPS_DATA` avec les octets de tout `.glb` nommé, exactement comme une
fiche de navire. Un modèle est chargé une fois par sorte et **cloné** pour chaque
objet à flot, ses matières prenant la brume ; tant qu'il n'est pas là, ou s'il est
illisible, l'objet est dessiné par le code. La bouteille est désormais dessinée à
sa vraie taille et agrandie par le fichier (`scale: 3`), ce qui la rend réglable
comme le reste.

Relevé, goélette sabordée d'office avec bouteille forcée : un tonneau et la
bouteille à flot en trois secondes, une marque sur la carte, les trois contenus
lus, une cargaison posée par 0,7 m de fond au sud-ouest du Carénage, et 3 t
d'épices hissées à bord.

## Voile en vue

**UNE MER OÙ L'ON NE CROISE PERSONNE N'EST PAS UNE ROUTE**, et la flotte savait
déjà porter huit coques : il ne manquait que quelqu'un pour les mettre à l'eau au
large. Toutes les six à douze minutes de jeu, un navire tiré au sort parmi les
fiches de `ships/` est lancé par le même `launch()` que le panneau Flotte, **en
eau profonde** (plus de 25 m de fond, aucune île à moins de 800 m) et à quatre ou
cinq kilomètres — au-delà de ce que la brume laisse voir, pour qu'il **vienne**
de l'horizon au lieu d'apparaître. Pas plus de deux à la fois, jamais à quai, et
retiré au-delà de neuf kilomètres ou une fois coulé et laissé à trois : une
rencontre qu'on a laissée derrière n'occupe pas une place de la flotte pour
toujours. La compression du temps est refusée tant qu'une rencontre vivante est à
moins de 2 500 m, pour la raison de toutes les autres : ce qui approche mérite
qu'on le regarde.

**AU LARGE, ET AVEC DE L'EAU JUSQU'À OÙ ELLE VA** — signalé à l'usage : rester au
port faisait paraître des voiles, dont certaines sur l'île. Deux fautes. La garde
était `portIci`, qui dit « comptoir ouvert » et non « près de la terre ». Et l'eau
franche n'était exigée qu'au point de naissance : un pirate né derrière l'île du
port montait droit sur le joueur à quai, c'est-à-dire droit sur la plage, la
barre visant un point sans contourner la terre. Désormais rien ne paraît à moins
de `landDistance` (2 500 m) d'une côte, la naissance est à `spawnFromLand`
(1 500 m) de toute côte, et la **droite jusqu'au but** est sondée tous les 150 m à
plus de `clearWater` (400 m) du rivage (`World.shoreDistance`, le même `_shore` que
le terrain) : le joueur pour un pirate, pour un marchand un port dont la route est
claire hors les 1 500 derniers mètres, sinon un point du large à 15 km. Relevé à
quai : aucun pirate possible, marchands nés à 3,0–3,8 km de la terre, route à plus
de 900 m des côtes. Reste qu'un pirate en chasse vise le joueur où qu'il aille : si
l'on passe derrière une île, il talonne.

**Un sur quatre est un pirate, et il NE HISSE PAS SES COULEURS** tant qu'on ne
l'a pas vu. Il est lancé déguisé (`e.deguise`) : pavillon masqué, pacifique, cap
sur le joueur. Le pavillon noir est une déclaration — c'est ainsi qu'est lue
l'hostilité — donc le masquer, c'est littéralement ne rien déclarer, et le
système de combat n'a rien eu à apprendre. **Remarqué, il hisse**, devient
hostile, prend sa distance de garde et passe en chasse, et la phrase change :
« Voile en vue ! Elle hisse le pavillon noir ! » au lieu du nom d'un marchand.
Les autres font route vers un port, la tête du ponton en coordonnées vraies
posée sur leur entrée (`e.route`) et relue en local par la barre.

**Remarquer est une question d'ŒIL, et il y en a deux** : à moins de 600 m quoi
qu'on fasse, ou **à la lunette**, jusqu'à sept kilomètres, s'il est dans le
disque — son centre, pris à un tiers de sa longueur au-dessus de l'eau, projeté
dans les huit dixièmes intérieurs du rayon de l'oculaire. Aller le chercher du
regard est donc un vrai geste de jeu : on voit le pirate avant qu'il se sache vu,
et qu'il hisse quand on le lorgne est exactement le moment qu'on voulait.

**Le piège : la lunette tourne la caméra APRÈS la dernière image.** `aim()` fait
un `lookAt`, mais `camera.project` lit `matrixWorldInverse`, que seul le rendu
reconstruit. Projeté avant lui, le navire était cherché là où l'objectif pointait
à l'image précédente : vrai au banc, qui appelait `updateMatrixWorld()` de
lui-même, et jamais dans la boucle — un pirate au centre du disque à 4 147 m
n'a pas été remarqué en quinze secondes. Le test reconstruit la matrice avant de
projeter. Relevé ensuite : pirate déguisé à 4 011 m, pavillon masqué ; remarqué à
500 m dans la même image ; à la lunette, **remarqué à 4 745 m au premier contrôle**,
pavillon hissé.

**Chaque voile croisée arbore un pavillon**, tiré au `poids` dans
`ships/textures/flags/flags.json` — image, pays, `nationalite` insérée telle quelle
dans « Voile en vue ! C'est un navire hollandais ! ». Il est porté sur **son**
entrée (`e.pavillon`) et posé sur son matériau par `setEnsignMap`, qui reprend
aussi l'émissif faible d'un pavillon peint : celui d'un pavillon blanc délave les
couleurs. **Le pirate aussi en porte un, d'emprunt** — c'est ce que faisaient les
vrais — et le masquer ne se fait plus que faute de pavillon à emprunter.
Remarqué, il amène ces couleurs pour les siennes : `ensignMap` de sa fiche, sinon
l'entrée `pirate: true` du fichier. Hostilité toujours lue sur `ensign` de la
fiche, jamais sur l'image. Le build embarque la liste en `Naval.FLAGS_DATA`,
chaque image devenue ses octets, et ignore en le disant une image absente.
Relevé : marchand et pirate sous pavillon hollandais, le pirate remarqué à 400 m
passe au jolly roger et hostile.

**Les réglages vivent dans `settings.json`**, à la racine — le premier fichier de
réglages de **jeu** et non de navire ou d'objet : intervalle, nombre simultané,
distances d'apparition et de retrait, part de pirates, distance et champ de la
lunette, et les fiches à exclure (`bouee-canard`). Les distances sont en mètres
et les durées en secondes de jeu, que la compression accélère. Servi en
développement, embarqué par le build en `Naval.SETTINGS`, et fusionné champ par
champ sur des valeurs par défaut écrites dans la page : un fichier absent ne
change rien. `Naval.app.voileAuHasard()` en force une.

Ce qui n'est pas fait : un marchand suit une droite vers la tête du ponton et
peut talonner sur le plateau s'il y arrive — il n'accoste ni ne mouille ; et
aucune rencontre n'est **parlée** automatiquement, la rumeur restant ce qu'elle
était.

## Le bruit, et surtout le temps qu'il met à venir

**LE SON MET UNE SECONDE ET DEMIE À FAIRE CINQ CENTS MÈTRES**, et c'est cela —
bien plus que le timbre — qui fait lire un canon comme lointain. On voit la
flamme, on compte, puis on entend ; chacun l'a fait sous un orage, et c'est
pourquoi l'oreille sait d'emblée à quelle distance le coup est parti. Un
échantillon sourd joué à l'instant du flash sonne comme un canon **en sourdine**
; le même joué une seconde plus tard sonne comme un canon **loin**. Le retard
est une division, et il est l'essentiel de l'effet.

Trois cent quarante-trois mètres par seconde, ce qui n'est pas un réglage :
c'est la vitesse du son dans l'air à quinze degrés. Relevé sur le programme
réel, contre un échantillon proche de 5,64 s et un lointain de 3,29 s :

| distance | retard | coupure | échantillon | gain |
|---|---|---|---|---|
| 20 m | 0,06 s | 18,5 kHz | proche | 1,00 |
| 100 m | 0,29 s | 13,6 kHz | proche | 0,55 |
| 200 m | 0,58 s | 9,3 kHz | proche | 0,28 |
| **400 m** | 1,17 s | 4,3 kHz | **lointain** | 0,14 |
| 800 m | 2,33 s | 922 Hz | lointain | 0,07 |
| 1 500 m | **4,37 s** | 700 Hz (plancher) | lointain | 0,04 |
| 3 000 m | — | — | rejeté, hors de portée | — |

**L'AIR MANGE LES AIGUS, et c'est cela qui « étouffe » un coup bien avant qu'il
devienne un grondement.** Signalé à l'usage : « à cent mètres le son ne devrait-il
pas être presque étouffé ? ». La mesure disait non — à cent mètres c'était
l'échantillon proche à 60 % du volume, et physiquement c'est juste, cent mètres
étant à bout portant. Mais l'intuition attrapait quelque chose de réel que le
code ignorait : l'absorption atmosphérique croît avec la fréquence **et** avec la
distance, si bien qu'un rapport perd son claquement en premier et garde son
ventre.

Un passe-bas dont la coupure décroît en exponentielle — `20000·e^(−d/260)`,
plancher à 700 Hz — le rend sans rien basculer. Et il **a permis de repousser le
seuil de l'échantillon lointain de 260 à 400 m** : à la bascule la coupure est
déjà tombée à 4,3 kHz, donc les deux échantillons se ressemblent assez pour que
le passage ne s'entende pas. Sans le filtre il fallait basculer tôt pour que le
lointain ne surprenne pas ; avec lui, on garde le claquement aussi longtemps
qu'il est vrai.

**LE RELIEF GAUCHE-DROITE EST LA MÊME ARITHMÉTIQUE QUE LE CHOIX DU BORD EN
BATTERIE** : le produit scalaire du relèvement par le **travers de la caméra**
en donne le signe et l'ampleur. Il faut la caméra entière et pas seulement sa
position — sans son orientation, un duel à bâbord et à tribord sonnait
rigoureusement au centre, ce qui est le défaut le plus visible quand on regarde
un combat de côté. Un coup droit devant ou droit derrière tombe à zéro, ce qui
est juste : deux oreilles ne distinguent pas l'avant de l'arrière sans tourner
la tête. Relevé : **−0,90 · 0,00 · +0,90** à gauche, devant, à droite.
L'amplitude s'arrête à 0,9 — un panoramique à fond colle le son à une enceinte,
et rien dans la nature n'est aussi latéral.

**Et l'oreille est la CAMÉRA, ce qui se mesure.** Duel de deux galions à cent
mètres l'un de l'autre, joueur posé à dix mètres du milieu : l'écoute se trouve
en fait à **51 m**, la vue fixe se plantant sur sa hanche. Les coups arrivent
alors de 20 à 100 m — 0,06 s contre 0,29, et deux coupures différentes — si bien
que les deux bordées ne se confondent pas. À deux cents mètres, l'oreille est à
176 et tout se resserre : 0,54 s de retard moyen, 9,8 kHz, gain 0,30.

**L'OREILLE EST À LA CAMÉRA**, pas sur la coque : on entend d'où l'on regarde.
La vue fixe plantée à deux cents mètres retarde donc **votre propre** bordée,
ce qui est exactement ce qui se passerait.

**L'atténuation est une division, pas une courbe inventée** — une pression
acoustique décroît en 1/r — et le calibre que `guns.js` calcule déjà sert la
hauteur : une grosse pièce sonne plus grave, rendu par la vitesse de lecture
plutôt que par un troisième échantillon, ce qui allonge du même coup la détente.
Plus quelques pour cent de variation par coup, la même irrégularité que la
charge dosée à la main.

**Rien n'est chargé depuis un fichier dans la page publiée.** Les octets
voyagent en base64 dans `Naval.SOUND_DATA` et `sound.js` les décode **à la
main** — un `fetch()` sur une URI `data:` serait une requête, et le but est de
n'en faire aucune. Même chemin que les `.glb` et la voile peinte.

**Le prix a d'abord été en mégaoctets** : deux WAV de 44,1 kHz stéréo font
1,5 Mo, soit 2 Mo de base64. Passés en `.ogg`, les quatre échantillons pèsent
**228 Ko** — voir plus bas.

**LE BOIS QUI CASSE PART DE LA CIBLE, PAS DU CANON**, et c'est tout ce qui rend
une touche lisible à l'oreille : on entend la pièce, puis — s'il y a de la
distance — le coup dans la muraille. Les deux voyagent à la même vitesse depuis
deux endroits différents, donc **personne n'a eu à orchestrer** l'écart. Relevé,
cible par le travers à 160 m, oreille à 47 m de ses propres pièces :

| | le départ | l'impact |
|---|---|---|
| distance à l'oreille | 47 m | **159 m** |
| retard | 0,14 s | **0,47 s** |
| coupure | 16,7 kHz | **10,8 kHz** |

**TOUT CE QUI SONNE PASSE PAR LE MÊME `_jouer`**, et c'est la seule raison pour
laquelle cela marche sans une ligne de plus : le retard, l'absorption de l'air,
le relief gauche-droite et l'atténuation sont des propriétés de la **distance**,
pas du coup de canon. Les réécrire pour le bois, c'était se donner deux
acoustiques à tenir en accord. L'appelant ne choisit que ce qui lui appartient —
quel échantillon, à quelle hauteur, avec quelle force il a frappé.

**Deux échantillons tirés au sort**, parce qu'un seul se reconnaît à la troisième
touche et cesse d'être un choc pour devenir un bruitage — même raison que les
intervalles irréguliers de la bordée. La force du choc porte le volume, et c'est
la **vitesse restante** qui la donne : un boulet arrivé à bout de course cogne
moins fort. Et un mât n'est pas une muraille — même bois, plus léger et plus
sec, donc le même échantillon monté d'un ton plutôt qu'un troisième fichier.

**LA CLÉ EST UN NOM, PAS UN FICHIER**, et le build choisit l'extension. Les
échantillons sont arrivés en `.wav` puis ont été remplacés par des `.ogg` **dix
fois plus légers** — 228 Ko contre 2,27 Mo, la page construite passant de 6,08
à **3,43 Mo**. Sans cette indirection, l'échange demandait de retoucher le build
**et** la page : le format d'un asset n'est pas une information que le jeu ait à
porter. L'ordre de préférence est celui du poids à qualité égale (`.ogg`,
`.mp3`, `.m4a`, `.wav`), et un fichier qui en ombre un autre est **annoncé**
plutôt que laissé grossir le dépôt en silence.

Une réserve à connaître avant de publier : l'Ogg Vorbis n'a été accepté par
Safari que tardivement. Un chargement qui échoue n'interrompt jamais rien — le
`catch` est là pour ça — mais la page serait alors muette sur ces machines. Du
`.mp3` à côté suffirait, le build prenant le premier qu'il trouve.

**Un navigateur ne fait aucun bruit avant le premier geste de l'utilisateur**, et
c'est une règle qu'on ne contourne pas : on l'attend. Le premier clic ou la
première touche réveille le contexte et charge les deux échantillons, après quoi
personne n'y pense plus.

**DEUX BUGS TROUVÉS EN ÉCRIVANT CELUI-CI, et le premier était grave.**

`onRecoil` ne disait pas **de qui** était la pièce, et la page appliquait le
recul à `physics.body` — le vôtre. C'était juste tant que seul le joueur avait
des canons, et c'est devenu faux le jour même où les conserves ont eu le droit
de tirer : **une bordée pirate secouait votre coque.** Le corps qui tire voyage
donc désormais avec le coup. Le son a en outre gagné son propre rappel
(`onBang`) plutôt que de se greffer sur celui-là : ce qu'une pièce fait à la
coque et ce qu'elle fait à l'air sont deux événements, et les confondre ne
marchait que par accident.

Et le compteur de sources **fuyait** : incrémenté au départ, décrémenté sur
`onended`, il ne redescendait jamais d'une source qui n'avait pas démarré — et à
vingt-quatre fuites le son se taisait pour de bon, sans rien dire. Remplacé par
une liste d'**instants de fin**, purgée contre l'horloge, qui ne peut pas
dériver. Vérifié : quarante coups d'affilée plafonnent à 24, et sept secondes
plus tard il n'en reste qu'un.



## La musique, qui est un flux et non un tampon

**ET CE N'EST PAS UN DÉTAIL DE PLOMBERIE : c'est ce qui sépare une ambiance d'un
bruitage.** Un échantillon de canon dure cinq secondes, se décode une fois et se
rejoue cent fois depuis la mémoire. Une ambiance dure huit minutes. Passée par
`decodeAudioData` elle deviendrait du PCM flottant non compressé — 44 100 × 2
canaux × 4 octets par seconde — soit **quatre-vingt-dix mégaoctets de mémoire
vive pour dix sur le disque**, et près de sept cents pour l'heure que faisait la
première version du fichier. Un élément `<audio>` la lit au fil de l'eau et n'en
garde rien.

Elle ne passe donc **pas** non plus par `_jouer` : le retard, l'absorption de
l'air et le relief gauche-droite sont des propriétés d'un son qui vient d'un
**endroit**. Une musique ne vient de nulle part — elle est dans la tête du
commandant, pas sur l'eau.

**ET ELLE NE PEUT PAS ÊTRE EMBARQUÉE**, ce qui est une première dans ce projet.
Les deux pistes font 12,1 Mo, soit **16,2 Mo en base64** — au-delà du plafond
d'un artifact *avant même* de compter les 3,4 Mo de la page. C'est le premier
asset qui ne tienne pas dans la règle du fichier unique : la page autonome sera
donc muette de musique à moins qu'on ne pose les fichiers à côté d'elle. Un
chargement qui échoue ne dit rien et n'interrompt rien.

**DEUX SEUILS ET NON UN**, et c'est toute la différence entre une bascule et un
clignotement : on passe à l'action quand la voile noire est à **douze cents
mètres**, on n'en ressort qu'au-delà de **dix-huit cents**, et il faut quinze
secondes de calme pour se rasseoir. Un seuil unique ferait battre la musique à
chaque lame dès qu'un pirate croise à la distance juste — même hystérésis que la
ligne de bord de la barre automatique, et pour la même raison.

**LE SILENCE SE LIT SUR LA MONTRE, PAS SUR LES IMAGES**, et l'avoir écrit
autrement a coûté un essai. Cumuler le pas d'image paraît naturel et ne l'est
pas : ce pas est **plafonné à cinquante millisecondes** pour protéger le solveur
d'une saccade, si bien que sur un volet à deux images par seconde le compteur
n'avançait que d'un dixième de seconde par seconde réelle — vingt-trois secondes
de calme pour deux comptées, et la musique n'est jamais revenue. Le même défaut
frapperait n'importe quelle machine dès qu'elle rame, c'est-à-dire exactement
quand une bataille a lieu. La montre plutôt que l'horloge du **jeu**, aussi :
sous compression ×16 une accalmie de quinze secondes passerait en moins d'une.

Relevé : Vivaldi au départ ; voile noire à 900 m, l'action prend la main ;
pirate repoussé à 2 607 m, vingt secondes plus tard Vivaldi revient à plein
volume.

**La clé est un nom, pas un fichier**, et le build choisit l'extension par ordre
de poids (`.ogg`, `.mp3`, `.m4a`, `.wav`). Les bruitages sont arrivés en `.wav`
puis ont été remplacés par des `.ogg` **dix fois plus légers** — 228 Ko contre
2,27 Mo, la page passant de 6,08 à **3,43 Mo**. Sans cette indirection l'échange
demandait de retoucher le build *et* la page. Un fichier qui en ombre un autre
est **annoncé** plutôt que laissé grossir le dépôt en silence.

Une réserve avant de publier : l'Ogg Vorbis n'a été accepté par Safari que
tardivement. Poser un `.mp3` à côté suffirait, le build prenant le premier qu'il
trouve.

## Un mât qui tombe

**Deux groupes emboîtés, et l'emboîtement est toute l'astuce.** Un mât s'abat
sur son **pied**, donc ce qui tourne doit avoir son origine au niveau de
l'emplanture. Brasser, au contraire, est une rotation autour de l'axe
**vertical** — et une rotation autour d'un axe est la même où qu'on place
l'origine le long de cet axe. Le groupe extérieur peut donc descendre au pied
gratuitement, et `setTrim` continue de faire tourner l'intérieur exactement comme
avant, sans rien savoir de tout cela. Le fût est porté par l'extérieur, les
vergues et la toile pendent dans l'intérieur, où elles étaient déjà : tout ce
qui appartient à ce mât passe par-dessus bord ensemble.

**Le mât se trouve par la forme, comme la vergue, le test mis debout** : haut,
mince **des deux côtés**, et sur l'axe. Mince des deux côtés est ce qui fait le
travail — cela écarte tout ce qui est soudé à ses voisins, ce qui est l'état
ordinaire d'un modèle importé et décide de ce qui peut tomber ou non. Relevé
sur `pirateship.glb` :

| mesh | dx | dy | dz | verdict |
|---|---|---|---|---|
| `Cylinder` | 1,1 | 38,7 | 1,1 | un mât propre, il tombe |
| `Cylinder001` | 0,9 | 34,4 | **43,1** | deux ou trois mâts fondus en un seul mesh — refusé |
| `Cylinder004…008` | 13–21 | 0,3–0,5 | 0,3–0,6 | les vergues, chacune la sienne |

Le refus est voulu : sans mesh à lui, la toile tomberait d'un espar resté en
l'air, ce qui est pire que rien. **Modéliser un mesh par mât est donc la seule
exigence** — les origines, elles, n'ont pas à être placées, la boîte englobante
donnant le pied.

**C'est un PENDULE, pas une animation.** Un espar articulé à son emplanture est
une tige homogène sur un pivot, et cela a une équation — `a" = (3g/2L)·sin a` —
qu'il vaut mieux employer qu'une courbe dessinée à la main, pour une raison :
elle porte la **taille** du navire. Le taux va comme l'inverse de la racine de
la longueur, donc un petit mât fouette pendant qu'un lourd s'incline longtemps
d'abord, sans qu'un nombre ait été réglé pour l'un ni pour l'autre. C'est
l'argument de Froude déjà employé pour la gerbe, et la raison pour laquelle une
maquette ne paraît jamais grande.

Elle a aussi la bonne forme dans le temps toute seule : à peine mobile, puis
d'un coup. Un mât ne bascule pas, il pend, il s'incline, et il part — et aucune
courbe adoucie ne le reproduit, parce que ce qui le fait est que le moment de la
pesanteur croît avec l'angle même qu'il produit. Mesuré sur le grand mât du
pirate, 38,7 m :

| temps | 0 | 0,5 s | 1,0 | 1,5 | 2,0 | 2,5 | 3,0 | 3,6 |
|---|---|---|---|---|---|---|---|---|
| inclinaison | 2° | 8° | 14° | 21° | 30° | 42° | 59° | **80°** |

Un mât de quinze mètres fait le même parcours en **2,2 s** contre 3,6.

**Elle s'arrête à quatre-vingts degrés**, pas à plat : un vrai mât passe
par-dessus bord et **s'arrête net dans ses propres haubans**. À quatre-vingt-dix,
on lirait un arbre abattu.

**Puis elle s'en débarrasse**, ce qui manquait et se voyait : arrêté là, le mât
restait pour toujours couché en travers de son bord et la suivait partout — il
avait l'air **accroché** à elle. Ce qui se passe réellement est en deux temps et
le premier était là sans le second. Un mât abattu tient d'abord dans ses propres
haubans et **traîne**, c'est ce qui rend un démâtage si dangereux — le navire
est tiré par son épave au lieu d'en être quitte. Puis l'équipage prend les
haches, coupe les rides, et le tout part par le travers et coule : du bois
gorgé d'eau avec sa mâture et sa toile ne flotte pas longtemps.

Il s'enfonce dans **son repère à elle** plutôt que dans le monde, ce qui épargne
tout à ce morceau d'épave — rien à recentrer quand l'origine glisse, rien à
sortir du graphe, rien à détruire. Et la mer étant opaque, il disparaît de
lui-même en passant dessous, exactement comme les planches de l'explosion.

**Mais le budget se mesure sur ELLE, pas sur le mât**, et le premier réglage
s'était trompé de montre : six secondes de traîne puis huit d'enfoncement font
dix-huit, quand un navire qu'on fait sauter passe sous l'eau au bout de **six**.
Le mât descendait donc avec lui au lieu de s'en aller — précisément ce qu'on
voulait éviter, et signalé à l'usage. À 240 tonneaux la chose est brutale :
`blowUp()` verse d'emblée 154 t dans une coque qui en déplace 240, donc elle est
condamnée à la première image et sous l'eau à six secondes.

**ET RIEN DE TOUT CELA NE DOIT ÊTRE LINÉAIRE**, ce qui fut signalé et juste. Le
pendule donnait un très beau départ — immobile, puis d'un coup — mais il
arrivait à sa butée **à pleine vitesse** et s'y arrêtait net : le seul endroit
du mouvement qui trahissait une valeur écrêtée plutôt qu'une chose qui s'arrête.
Un mât ne rencontre pas un mur, ses rides prennent la charge sur le dernier
quart et le freinent. L'amortissement va donc comme le **carré** de ce qu'il a
parcouru dans ce dernier quart — nul quand il y entre, entier quand il y arrive
— et il est écrit par seconde et non par image, sans quoi le freinage
dépendrait de la fréquence d'affichage comme tant d'autres choses ici.

Le **roulé** était une rampe droite : il prend un `smoothstep`, doux aux deux
bouts, parce qu'il part d'un objet en équilibre sur sa lisse et finit couché —
les deux extrémités sont des états de repos.

La **descente**, elle, ne prend QUE l'entrée en douceur, et c'est délibéré :
elle n'a pas de fin. Le mât ne se pose pas au fond, il s'en va. Lui donner une
sortie douce serait le faire ralentir en s'enfonçant, ce qui est joli et faux ;
elle va donc comme le carré du temps, ce qui est aussi ce que fait un corps qui
coule.

Relevé sur les **vitesses**, qui sont ce qui dit s'il y a de la douceur — la
position ne le dit jamais :

| chute | 0,3 s | 1,3 | 2,0 | 2,3 | 2,5 | 2,8 | 3,0 |
|---|---|---|---|---|---|---|---|
| °/s | 15 | 25 | 43 | **51** | 47 | 13 | **0** |

| roulé | 3,3 s | 3,8 | 4,3 | 4,5 | 5,0 | 5,5 | 6,0 |
|---|---|---|---|---|---|---|---|
| °/s | 2 | 26 | 37 | **39** | 31 | 10 | **0** |

**Et raccourcir ne suffit pas : il faut qu'il ROULE.** Descendre tout droit à
côté d'une coque qui descend aussi ne se lit pas comme un départ. Il passe donc
la lisse, tourne au-delà de l'angle où ses haubans le tenaient, et part par le
travers — trois mètres et demi de côté, en quadratique pour qu'il s'écarte
d'abord doucement puis franchement, comme une chose qui bascule. Relevé :

| | mât | coque |
|---|---|---|
| 3 s | par terre à 80° | −2,4 m |
| 4 s | roule, −3,7 m, 0,7 m de côté | −1,5 m |
| 5 s | −9,2 m, 2,8 m de côté | −1,4 m |
| **7 s** | **parti** | −4,3 m, encore à flot |

Il est dégagé pendant qu'elle est encore là, ce qui est tout l'objet.

**Et la toile perdue est perdue pour de bon**, ce qui est la moitié de
l'intérêt. Le modèle rend la fraction de gréement encore debout, pondérée par
la **surface que chaque mât porte** et non par le nombre de mâts — un artimon
n'est pas un grand mât — et le solveur la multiplie dans la pression, exactement
comme la fraction de toile établie. Un seul nombre, donc l'image et la physique
ne peuvent pas diverger. Le grand mât du pirate porte la moitié de sa voilure :
tombé, `standing` vaut 0,501 des deux côtés.

La décroissance suit le **cosinus** de l'inclinaison, mais **remappé** pour
s'annuler là où elle s'arrête et non à quatre-vingt-dix : un cosinus brut lui
laissait huit pour cent de sa poussée avec les voiles déjà dans l'eau, en train
d'y être traînées.

**La soute qui saute les prend tous** — mais pas ensemble. Ils partent à
quelques dixièmes de seconde d'écart et alternativement de chaque bord, pour
exactement la raison des trois charges : au même instant cela se lit comme un
seul objet qui casse, échelonné cela se lit comme un navire qui se disloque.

**Ce qui manque encore**, et qu'il faudra pour un galion : une **antenne
latine** n'est pas reconnue. Le détecteur de vergues exige un espar posé en
travers de l'axe, et une antenne est inclinée dans le plan longitudinal — le mât
sera bien trouvé et tombera, mais nu. La civadière sous le beaupré passe, elle :
le code lit déjà un « mât » à 40,7 m sur l'avant du pirate.

## Les bouts rompus

**Le projet AFFIRMAIT deux choses qu'il ne montrait pas.** Qu'un mât qui tombe
s'arrête net dans ses propres haubans, et qu'un navire démâté est traîné par son
épave au lieu d'en être quitte. Les deux sont écrites plus haut et aucune n'était
à l'écran — si bien qu'un mât qui avait encaissé deux boulets ressemblait
exactement à un mât qu'on n'avait jamais touché : rien ne disait qu'on était en
train de le blesser, sinon le troisième coup qui l'abattait. Quelques bouts qui
pendent règlent ça, et c'est la manière la moins chère de le faire honnêtement.

**UN CORDAGE EST UNE CHAÎNE DE VERLET, pas une animation.** Des points, une
contrainte de distance entre voisins, et rien d'autre : pas d'angle, pas de
rotation, aucune force à intégrer et rien qui puisse diverger. C'est l'argument
du pendule du mât — employer l'arithmétique que la chose suit réellement plutôt
qu'une courbe dessinée à la main — et ici c'est en prime ce qu'il y a de moins
cher dans le fichier. Mesuré, douze points et trois itérations de contrainte :

| cordages | 8 | 24 | 64 | 160 |
|---|---|---|---|---|
| ms/image | 0,005 | 0,014 | 0,037 | **0,100** |

Relevé ensuite dans le navigateur et non plus dans node : **0,031 ms** pour les
douze bouts d'un galion, **0,118 ms** au plafond de la réserve (96 bouts,
1 728 triangles, **un** appel de dessin). La simulation n'est pas la facture.

**CE QUI COÛTE, C'EST DE LES DESSINER.** Un `THREE.Line` fait un pixel de large
quel que soit le `linewidth` demandé, WebGL l'ignorant sur presque toutes les
plateformes : un cordage tracé en ligne est un scintillement sous-pixel à toute
distance qui vaille. C'est le même piège d'aliasing que l'étoile plus étroite
qu'un pixel, la lueur lointaine de la lanterne et le boulet dessiné à cinq fois
sa taille, et la réponse est la même — être **plus large** que le pas
d'échantillonnage, jamais plus brillant. Chaque bout est donc un **ruban**, une
bande de triangles tournée vers la caméra, sa largeur en mètres réels avec un
plancher en pixels. Tous les cordages de la flotte vivent dans une seule
géométrie, exactement comme `splash.js` tient sept cents gouttes dans un seul
`Points`.

**Et au-delà d'une certaine distance ils ne sont plus dessinés du tout** — mais
la coupure est affaire de **lisibilité et non de coût**. À trois cents mètres un
cordage est un cheveu qui se lit comme du bruit sur le gréement, et le plancher
en pixels qui le sauve de près est précisément ce qui le fait scintiller de
loin. La simulation, elle, continue à travers la coupure : elle est gratuite, et
l'arrêter voudrait dire que le bout se remet d'un coup en position de repos dès
qu'on se rapproche. Fondu de 190 à 300 m ; vérifié en déplaçant la fenêtre
plutôt que la caméra, sur des bouts à 59 m : 12 tirés à pleine opacité, écart
moyen 44/255 en plein fondu, **zéro pixel** passé la borne.

**Un SOUS-PAS FIXE, et il compte plus ici que presque partout ailleurs.** Verlet
à `dt` variable n'est pas seulement imprécis, il change l'amortissement effectif
— et le volet cadence à 32 quoi qu'on lui demande. Plafonné à cinq sous-pas, sans
quoi un volet resté figé une seconde essaierait de rattraper d'un coup et
enverrait tout le gréement de la flotte en l'air.

**Les points d'attache sont relevés AU GRÉAGE**, là où les vergues viennent
d'être lues sur la géométrie et rangées sur leur mât. Les redemander plus tard
voudrait dire une seconde règle à tenir en accord avec la première, ce que
l'invariant du plan de formes unique existe pour empêcher. Rien par navire, comme
tout le reste du gréement : déposer un carré dans `ships/models` suffit à lui
donner ses bras et ses haubans. Les bouts de vergue vivent dans le **pivot**,
donc un bras coupé tourne avec sa vergue quand on brasse ; les haubans vivent
dans la **chute**, un hauban ne brassant pas.

**Et chaque bout tient un DÉCALAGE LOCAL, pas un point monde**, ce qui rend
l'origine flottante gratuite ici : l'attache est relue dans une matrice vivante à
chaque image, donc elle suit son roulis, sa gîte et un mât qui passe par-dessus
bord sans qu'aucun des trois ait à savoir que ceci existe.

**Ils réfléchissent, ils n'émettent pas**, sur la luminance de la couleur
d'horizon — la faute qu'avaient eue l'écume puis la fumée, et le même remède.

**Trois façons de mourir, et aucune n'est un appel à retenir** : la coque quitte
la scène, une réparation incrémente son *époque*, ou le mât finit de s'enfoncer
et devient invisible. Vérifié bout à bout : un mât abattu emporte ses quatre
bouts pendant les cinq secondes de sa chute, de sa roulée et de son enfoncement,
puis ils disparaissent avec lui au même instant ; une réparation en efface douze
d'un coup ; un changement de navire en efface quatre-vingt-seize.

**Deux réglages ratés, tous les deux dans le même sens, et il a fallu les
peindre en rouge pour les voir.** Un hauban court des jottereaux au cadènage,
donc un hauban rompu pend réellement seize mètres sur un mât de cette taille — et
seize mètres de cordage sont **parfaitement droits**, un bout libre pendant droit
quoi qu'il soit fait. Une droite de cette longueur ne se lit pas comme du cordage
mais comme du **fil de fer** : on aurait dit qu'on l'avait haubanée au grillage.
Et le terme de traînée de l'air, écrit à 0,55, paraît modeste et ne l'est pas :
contre 9,81 de pesanteur il couche un bout de plus de vingt degrés par brise
maniable, et tous les bouts du navire filent alors dans le même sens au même
angle, ce qui se lit comme un gréement sous tension et non comme un gréement
coupé. Ramenés à ce que l'œil accepte — ce qui reste d'un hauban après qu'il a
filé dans tout ce dans quoi il était passé — et la traînée à 0,28 :

| | avant | après |
|---|---|---|
| longueur moyenne | 12,4 m | **3,6 m** |
| inclinaison sur la verticale, vent 7,5 m/s | 21–40° | **12°** (5 à 21) |

Relevé au pixel, douze bouts à 46 m, à la largeur d'alors : **1 168 pixels**
changés, soit 0,117 % de l'image, pour un écart moyen de **78/255** et un maximum
de 184. Petite surface et fort contraste, ce qui est exactement le bon registre
pour du cordage — l'inverse du banc de grain, qui couvrait 8,9 % de l'image à
25/255 et ne se voyait pas.

**Et le diamètre est devenu HONNÊTE, ce qu'il n'était pas.** Un cordage se
mesurait à sa circonférence : un bras sur un navire de cette taille est un
quatre-pouces, soit trois centimètres au travers, et un hauban six ou sept.
Seize centimètres était une aussière et se lisait comme telle. Ce qui les rendait
visibles à toute distance qui compte n'a jamais été les mètres mais le **plancher
en pixels**, donc les mètres pouvaient revenir à la vérité sans que cela coûte
rien — à condition de bouger les deux ensemble, le plancher prenant la main
au-delà d'une quarantaine de mètres : n'affiner que la largeur n'aurait rien
changé à la distance d'où on la regarde réellement.

| largeur à l'écran | 15 m | 25 | 46 | 80 et au-delà |
|---|---|---|---|---|
| 16 cm, plancher 1,6 px | 6,7 px | 4,0 | 2,9 | 1,6 |
| **7 cm, plancher 1,3 px** | **2,9** | **1,8** | **1,3** | **1,3** |

Relevé au pixel sur huit bouts à 25 m : 2 167 pixels marqués contre **1 250**,
pour un écart moyen qui passe de 47,7 à **41,3** — deux fois moins d'encre et
toujours une marque franche. En dessous d'environ 1,2 px de plancher on
retomberait dans le scintillement contre lequel tout ce ruban a été écrit.

**Ce qu'on ne fait pas** : aucune collision cordage/coque ni cordage/vergue. Un
bout qui traverse un espar ne se remarque pas ; un bout qui ne bouge pas se
remarque tout de suite.

## La bourse, les épices et la poudre

**Premier morceau de JEU dans un moteur qui n'en avait aucun.** Tout ce qui
précède décrit un navire ; ceci décrit une raison de le mener quelque part.

**UN SEUL NOMBRE, DEUX LECTURES.** La bourse est un entier de **pièces
d'argent**, et rien d'autre. Les **écus d'or** sont une manière de le lire, pas
une seconde réserve : tenir deux compteurs, c'est garantir qu'ils divergeront le
jour où une transaction franchit la retenue. Même règle que la fraction de toile
établie et que la part de gréement debout — un nombre, deux usagers. Soixante
pièces pour un écu, ce qui n'est pas arbitraire : un écu valait trois livres et
une livre vingt sous.

**LES ÉPICES SONT DU VRAI FRET**, et c'est tout l'intérêt de les avoir posées
là. Acheter charge des tonnes dans la cale par `loadCargo`, donc elles enfoncent
la coque, changent son assiette et son GM comme n'importe quel poids — relevé,
240 t à vide deviennent 250 avec dix tonnes à bord — et une cargaison mal
arrimée se paie sur l'eau avant de se payer au comptoir. Rien n'a eu à être
écrit pour ça : la méthode du poids ajouté attendait un troisième client après
l'envahissement et le lest.

Mais il a fallu donner une **nature** au fret (`kind`), et la distinction porte
de l'argent : le plan d'arrimage charge et décharge à volonté, donc si les
épices avaient été du fret ordinaire on en aurait fabriqué gratuitement et le
commerce n'aurait plus rien voulu dire. Deux natures dans les mêmes
compartiments, la même masse, la même physique — seule la provenance diffère, et
c'est la seule chose que le solveur n'a pas à savoir.

**On ne vend que ce qu'on porte réellement** : la vente passe par `unloadKind`,
donc c'est la cale qui fait foi et non un compteur tenu à côté d'elle. Jeter sa
cargaison par-dessus bord la fait perdre, ce qui est le comportement juste.

**LE COURS EST UNE FONCTION PURE DU PORT ET DE L'HEURE**, écrit comme les îles
et comme les dépressions : aucun état, aucun fichier, la même chose sur toutes
les machines. Et **continu** — un tirage par palier ferait sauter le prix entre
deux images et l'on apprendrait à attendre devant le comptoir que le chiffre
change ; on interpole donc entre deux paliers par un smoothstep, même remède que
la dérive triangulaire des dépressions. Relevé à un instant donné :

| | Port-Royal | Le Carénage | Saint-Pierre | La Tortue |
|---|---|---|---|---|
| achat / vente, la tonne | 581 / 457 | 855 / 671 | 477 / 375 | 725 / 569 |

Un rapport de deux entre le port le moins cher et le plus cher : la destination
vaut d'être choisie, et le mauvais choix n'est pas ruineux.

**UN COURS DOIT TENIR LE TEMPS D'UNE TRAVERSÉE**, et le premier palier se
trompait d'un ordre de grandeur. À un quart d'heure, il tournait **six fois**
pendant un passage : relevé sur Port-Royal → Le Carénage, 7,8 milles et 93
minutes à cinq nœuds, le cours de vente faisait 671 · 607 · 379 · 577 · 730 ·
488 · **368**. On appareillait pour 671 et l'on trouvait 368.

Le défaut n'était donc pas de ne pas savoir où vendre : aucune information, si
parfaite fût-elle, n'aurait rattrapé ça. **Rien ne valait d'être su.** À trois
heures de palier, un passage en couvre 0,52 et le même trajet donne 671 · 670 ·
663 · 653 · **642** — quatre pour cent de dérive au lieu de quarante-cinq. Le
cours qu'on a vu garde son sens à l'arrivée, et il faut plusieurs traversées
pour qu'une route cesse d'être bonne.

C'est la même leçon que la météo automatique, dont le *système* tourne en une
dizaine de minutes quand ses rafales tournent en vingt secondes : ce qui compte
n'est jamais la valeur du pas, c'est son rapport à la durée de ce qu'on
entreprend.

**Le négociant prend sa marge, et c'est ce qui oblige à naviguer.** On achète
12 % au-dessus du cours et l'on revend 12 % en dessous, donc acheter et revendre
sur place **perd** de l'argent — vérifié, 24 000 pièces deviennent 21 520 sur un
aller-retour immobile. Sans cette marge il suffirait de cliquer deux fois pour
tondre la différence.

**On ne négocie qu'à quai, et « à quai » se mesure** : moins de 220 m du musoir
et moins de 0,8 m/s. Un navire qui passe au large ne commerce pas, et un navire
lancé à six nœuds ne débarque rien.

**LA POUDRE SE CONSOMME, une charge par coup**, et c'est ce qui donne un prix à
une bordée. La soute se dimensionne sur la batterie — quarante coups par pièce,
soit 480 sur le galion à douze canons, ce qui est une action longue et non une
escarmouche. Elle est débitée sur ce que les pièces ont **réellement** tiré et
non sur ce qu'on leur a demandé : `broadside` accepte désormais un plafond et
rend son compte, si bien qu'une bordée à court de gargousse est une bordée plus
**courte** — les pièces du bout restant muettes — et non une bordée qui ne part
pas. Vérifié : 480 → 479 au coup, → 473 à la bordée de six, et depuis quatre
charges la salve en tire quatre puis se tait.

**Un seul canal de messages**, celui que la capture d'écran avait ouvert pour
elle seule : le comptoir a exactement le même besoin — dire une phrase et
s'effacer — et lui en écrire un second aurait donné deux bandeaux capables de se
recouvrir.

**Une faute d'échelle rattrapée tôt.** La bourse de départ valait 1 500 pièces,
vingt-cinq écus, quand dix tonnes d'épices en coûtent quatre-vingt-dix-sept : le
bouton « acheter » échouait systématiquement au premier armement, et un jeu qui
commence par un refus n'explique rien à personne. Portée à 400 écus, de quoi
charger une quarantaine de tonnes au cours moyen.

## Savoir où vendre

**Deux renseignements, et ils ne se ressemblent pas** — c'est toute la question
posée par « comment savoir où la marchandise s'échangera au plus haut ».

**AU COMPTOIR : exact, et VIEUX.** Le négociant sait ce qu'on payait ailleurs
*quand la dernière nouvelle en est partie*. Le retard n'est pas inventé : c'est
la **distance divisée par la vitesse d'un navire porteur de nouvelles**, parce
que l'information voyageait par la mer, à la vitesse de la mer. Et c'est encore
une fonction pure — `spice(key, t − distance/vitesse)` — donc rien à stocker,
comme les îles, les dépressions et le cours lui-même.

Le port le plus lointain donne la nouvelle la plus alléchante ET la plus
périmée, et c'est là toute la tension. Relevé depuis Port-Royal :

| | nouvelle | âge | cours réel |
|---|---|---|---|
| Le Carénage | 682 | 1 h 33 | 671 |
| La Tortue | 503 | 2 h 15 | 569 |
| Saint-Pierre | **546** | 2 h 53 | **375** |

Saint-Pierre annonce 546 et paie 375 : un capitaine qui traverse sur cette
nouvelle-là se ruine. Les nouvelles sont listées **de la plus fraîche à la plus
vieille**, qui est l'ordre dans lequel on leur fait confiance.

**Et l'ÂGE est affiché à côté du chiffre.** Un prix périmé sans son âge est un
mensonge ; avec son âge, c'est un renseignement dont on juge soi-même. Toute la
différence tient dans trois mots à droite du nombre.

**EN MER : frais, et VAGUE.** On parle un navire, il dit ce qu'il a vu il y a
peu — mais un capitaine croisé au large ne récite pas une mercuriale, il dit que
ça se paie bien ou que ça ne se paie plus. Pas de chiffre, donc, et c'est
délibéré : **un chiffre se compare et se calcule, une appréciation se pèse.**
L'un se met en tableau, l'autre demande de décider.

Elle n'arrive qu'**en mer et en route** — à quai on a le comptoir, qui dit mieux
et pour rien — et elle **ne met pas le navire en panne** : on continue sa route
pendant qu'on lit. C'est la moitié de l'idée. Un renseignement reçu au comptoir
se range dans un tableau ; le même reçu en chemin oblige à décider avec de
l'erre sous la quille.

**ET LE RISQUE ÉTAIT DÉJÀ ÉCRIT.** Dérouter sur une rumeur, c'est réellement
tirer au sort : les dépressions sont une fonction pure de la position et de
l'heure et couvrent 5,9 % de la mer à tout instant, 28,8 % à portée d'en voir
une. Pas une ligne n'a eu à le décider — le monde le faisait déjà.

**Vraie, cependant.** Une rumeur FAUSSE est un autre jeu, celui où l'on doute de
ses sources, et il demande qu'on ait d'abord de quoi les recouper.

**Une faute de français au passage**, et elle vaut d'être notée parce qu'elle
dit où va une règle : « Il dit qu'les cours y sont mous ». On n'élide que devant
une voyelle, et la règle vit désormais **avec les mots qu'elle gouverne**, dans
`rumour()`, plutôt que dans la page qui les assemble — sans quoi ajouter une
bande demanderait de se souvenir d'aller corriger une phrase ailleurs.

**DEUX DÉFAUTS SIGNALÉS À L'USAGE, et le second était une vraie panne.**

« Je ne vois pas ma bourse. » Elle était au **bas** d'un panneau de six cents
pixels : sur une fenêtre courte elle passait sous le bord de l'écran, et un
compteur qu'il faut aller chercher n'en est pas un. Remontée en tête, juste sous
le sélecteur, sur une ligne à elle — c'est en outre le chiffre qu'on surveille le
plus souvent. Et `#instruments` est désormais **borné à la fenêtre** avec un
défilement, ce qui règle la classe entière de problème pour tout ce qu'on y
ajoutera plus tard.

« Les épices ne s'ajoutent pas au bateau. » Elles s'ajoutaient parfaitement — le
déplacement montait de dix tonnes — mais elles étaient chargées au niveau
**0,12** quand la grille d'arrimage ne dessine que 0,85, 0,50 et **0,18** : aucune
case ne pouvait donc les montrer. Du fret invisible dans un plan d'arrimage
n'est pas du fret, c'est une panne. Le niveau est maintenant **lu sur `LEVELS`**
plutôt que réécrit à côté — une définition, deux usagers, la règle que ce projet
applique partout ailleurs et que j'avais enfreinte pour un nombre.

**LE QUAI S'EST ÉLARGI ET A PRIS SA PASSERELLE.** Quatre mètres portent un homme
et sa charge, ce qui suffit à un appontement de service ; un quai où l'on
embarque du **fret** doit pouvoir porter des fûts posés de front et deux hommes
qui se croisent en portant la même caisse. Sept mètres, donc — et tout le reste
a suivi sans qu'on y touche, le poste s'écartant d'autant et les bittes avec,
la page lisant `jetty.width` au lieu de le réécrire. Vérifié après
élargissement : six amarres, 0,64 m d'évitage et **zéro talonnage** sur trois
minutes.

La **passerelle d'embarquement** est ce qui fait d'un appontement un quai de
commerce : sans elle on voit un navire à côté d'un ponton et rien qui dise par
où les caisses passent. Une planche en travers avec ses deux lisses, plus
quelques fûts et caisses sur le quai à côté.

Elle est **posée et non attachée à la coque**, et c'est un renoncement assumé :
la rattacher demanderait de la redessiner à chaque image pendant que le navire
évite sur ses amarres, pour une planche qu'on regarde deux secondes en
chargeant. Elle est donc au poste — du côté où la coque se range, aux sept
dixièmes du quai, là où un bâtiment de taille ordinaire présente son milieu.

### Ce qui n'est PAS fait

- **Les améliorations.** Nommées dans la demande, pas spécifiées : quelles
  améliorations, et sur quoi portent-elles ? La bourse sait déjà débiter, il ne
  manque que la liste.
- **Changer de navire remplit la soute.** `commission` arme la coque
  approvisionnée, ce qui est juste au premier armement et devient un
  approvisionnement gratuit quand on change de bâtiment au sélecteur. C'est la
  même liberté que le plan d'arrimage, qui charge des tonnes venues de nulle
  part : des affordances de bac à sable qu'il faudra trancher le jour où l'on
  décidera ce que « changer de navire » veut dire dans une partie.
- **Rien n'est sauvegardé.** La bourse repart à 400 écus à chaque chargement de
  la page.

## L'échelle de la carte

**LA DISTANCE EST RÉGLABLE, LA TAILLE NE L'EST PAS**, et ce n'est pas une demi-
mesure : ce sont deux questions différentes et une seule est affaire de goût. Le
plateau fait 240 m, un poste demande 9 m d'eau, le môle est épais de 18 et le
ponton long de 86 — ce sont des cotes de **navire**. Un monde qui les réduirait
mettrait un cinquième d'île sous son propre port et ramènerait la faute des
hauts-fonds que ce fichier a été écrit pour guérir. La distance, elle, n'est rien
d'autre que la durée d'une traversée : c'est le seul nombre qu'on puisse
honnêtement donner à régler.

`Naval.MAP_SCALE` multiplie donc les `x` et `z` des quatre entrées, et rien
d'autre. Huit nombres.

**LE FACTEUR A UN PLANCHER, ET C'EST LA GÉOMÉTRIE QUI LE POSE.** Laissez aux
îles leur taille et elles finissent par se toucher. Demandé à un tiers, la
réponse honnête était 0,70 :

| paire | eau entre les rivages | facteur minimal |
|---|---|---|
| **Port-Royal – Le Carénage** | 5 957 m | **0,69** |
| Le Carénage – Saint-Pierre | 5 674 m | 0,69 |
| Le Carénage – La Tortue | 9 084 m | 0,52 |
| Port-Royal – Saint-Pierre | 17 974 m | 0,39 |

À 0,333, Port-Royal et Le Carénage se chevaucheraient de **plus de trois
kilomètres** : une seule île en forme de huit.

**Et le rivage n'est PAS le rayon nominal.** `_shore` le module par quelques
harmoniques du relèvement, si bien que les côtes réelles débordent `r` de **14 à
22 %** — Port-Royal fait 4 853 m au plus large pour 4 200 annoncés. Mesurer le
plancher sur `r` aurait promis une passe qui n'existe pas. Le rivage réel est
donc échantillonné au démarrage (`isl.rShore`, 360 relèvements par île, une fois
pour toutes), et `_measure()` **avertit en clair** si deux côtes tombent à moins
d'une encablure : deux rivages qui se rejoignent ne se lisent pas comme une
erreur, ils se lisent comme une île mal fichue.

Relevé à 0,70, les six bordées :

| | milles | à 5 nœuds | passe entre les côtes |
|---|---|---|---|
| Le Carénage – Saint-Pierre | 5,1 | **1 h 01** | 1 636 m |
| Port-Royal – Le Carénage | 5,5 | 1 h 06 | **1 607 m** |
| Le Carénage – La Tortue | 6,0 | 1 h 12 | 4 302 m |
| Port-Royal – La Tortue | 8,0 | 1 h 36 | 6 744 m |
| Saint-Pierre – La Tortue | 9,8 | 1 h 58 | 10 905 m |
| Port-Royal – Saint-Pierre | 10,2 | **2 h 02** | 9 897 m |

Contre 1 h 28 à 2 h 54 auparavant. La passe la plus étroite garde 1 607 m, dont
1 127 d'eau franche une fois ôtés les deux plateaux : un vrai détroit, pas un
canal.

**CE QUI EST RÉGLÉ SUR LA TAILLE DU MONDE DOIT SUIVRE LE FACTEUR**, sinon un bug
déjà corrigé revient par la porte de derrière. Deux choses en dépendent, et une
troisième — c'est la plus jolie — n'en dépend pas et il ne faut surtout pas y
toucher :

- **le palier du cours est DÉRIVÉ** (`Market.tune`), la plus longue bordée
  divisée par cinq nœuds. Trois heures était juste pour cet archipel-là et ne
  veut rien dire en soi : toute la leçon du commerce est que ce qui compte est
  le **rapport** du palier à la durée d'un passage. Écrit en dur, il aurait
  ramené le 671-devenu-368 dès qu'on éloigne les îles. Vérifié : 2 h 04 au lieu
  de 3 h, et la plus courte traversée couvre **0,50** de palier — exactement le
  0,52 sur lequel le réglage avait été mesuré bon ;
- **la butée de zoom de la carte** sort de `world.extent`. À 24 milles sur un
  monde rapproché on ne cadre que de l'eau, et sur un monde élargi on ne tient
  plus l'archipel. Elle vaut 11 milles à 0,70, et les quatre îles tiennent dans
  le cercle ;
- **la vitesse de l'aviso ne bouge PAS.** `NOUVELLE` vaut 2,6 m/s parce qu'un
  navire va à cinq nœuds, et le retard d'une nouvelle est distance/vitesse :
  il suit donc le facteur tout seul. Le palier le suivant aussi, le rapport
  retard/palier — qui **est** la qualité du renseignement — reste invariant sans
  qu'une ligne s'en occupe. Relevé : Le Carénage à 0,53 de palier, La Tortue à
  0,78, Saint-Pierre à 0,99. Une vraie vitesse reste une vraie vitesse, et c'est
  ce qui fait qu'elle traverse les échelles.

Le poste, lui, n'a rien vu passer : six amarres, 1,48 m de tirant, **zéro
talonnage**. `settle()` repose toujours la coque au zéro local et le port est lu
sur l'île, donc déplacer l'île déplace le poste avec elle.

**Ce que cela ne donne PAS, et il faut le dire.** Ce n'est pas un mode arcade :
les traversées passent de 1 h 28 – 2 h 54 à 1 h 01 – 2 h 02, soit trente pour
cent, pas un tiers. Descendre plus bas demande de rétrécir les îles, ce que la
première section refuse.

**Il existe une autre voie, et elle coûte la chaîne.** Le plancher n'est imposé
que par les deux paires les plus serrées ; les lointaines ont du mou à revendre
(Port-Royal – Saint-Pierre tiendrait à 0,39). Re-répartir les quatre îles en
**grappe compacte** plutôt qu'en chaîne mettrait les six bordées entre 4,5 et
5,6 milles — **une heure partout**, sans qu'aucune île perde un mètre. Le prix
est la « chaîne nord-sud avec une île au large dans l'est » tirée des Îles du
Vent : une chaîne de quatre a forcément une grande diagonale, et c'est elle
qu'on paie. Décision de forme, pas de technique ; pas prise.

## Combien de coques

**Huit, et c'est mesuré et non ressenti.** `MAX_SHIPS` n'est pas un nombre libre :
il dimensionne les tableaux d'uniformes que les deux shaders indexent, le
`NSHIP` contre lequel ils sont compilés, et le nombre de lignes de la texture
de profils de coque. L'augmenter coûte un peu de travail dans **chaque fragment
de mer**, que les coques soient là ou non.

Le solveur domine et il est rigoureusement **linéaire** — ce sont les 539 sondes
de la grille, quatre sous-pas par image, et rien ne les partage entre navires :

| coques | 1 | 2 | 4 | 6 | 8 |
|---|---|---|---|---|---|
| solveur | 1,37 ms | 2,64 | 5,31 | 8,16 | **10,73** |
| par coque | 1,37 | 1,32 | 1,33 | 1,36 | **1,34** |

Le rendu de huit coques ajoute 3,56 ms, et les effets d'une vraie canonnade —
fumée, boulets, échardes, gerbes — moins d'un demi. Total autour de **15 ms pour
huit**, contre un budget de 16,7 à soixante images par seconde : quatre-vingt-
dix pour cent. En bataille réelle, un quart des images dépasse. Huit est donc le
plafond et non le confort ; **six** tient à 11 ms sans jamais déborder.

**Trois pièges de mesure, et le premier a failli me faire doubler le chiffre.**

- `stage.render()` dessine **toutes** les coques de la scène, pas seulement
  celles qu'on fait tourner. Un banc qui n'en anime que deux paie quand même le
  rendu des huit, et le coût par navire ressort à moitié de sa valeur. Il faut
  masquer les autres (`ship.group.visible`) pour isoler un palier.
- **Les relevés dérivent d'une session à l'autre.** Le même palier de huit a
  donné 8,3 ms sur une page fraîche et 16,2 après une longue série d'essais :
  modèles accumulés en mémoire, ramasse-miettes, état des coques. Prendre le
  chiffre conservateur, et refaire la mesure sur une page neuve avant de
  conclure.
- C'est du **processeur seul**. Sans requête de chronomètre GPU, un shader
  devenu coûteux ne s'y verrait pas.

**Une hypothèse testée et FAUSSE, qui vaut d'être gardée.** Après une longue
canonnade les navires portent dix-neuf à quarante-quatre voies d'eau chacun —
l'artillerie appelle `breach()` à chaque touche et la liste grandit sans borne,
contrairement à `worsenBreach()` qui aggrave le même trou. J'ai cru tenir là
l'explosion du coût. Mesuré : **cent soixante** voies d'eau réparties sur huit
coques ne coûtent que **9 %** de plus (10,77 contre 11,71 ms). Ce n'est pas le
sujet. La liste reste non bornée, ce qui n'est pas joli, mais cela ne se paie
pas.

## Les coques lointaines

**Un LOD de SIMULATION, pas de géométrie**, parce que c'est là qu'est le coût :
le solveur fait 1,5 à 1,8 ms par coque, le rendu de huit coques 3,56 ms en
tout. Et dans le solveur, c'est la lecture de la houle — dix vagues, une
puissance chacune, pour chaque sonde à chaque sous-pas — qui pèse.

Au-delà de `LOD_FAR` (1500 m) du navire commandé, et jusqu'à ce qu'elle
revienne sous `LOD_NEAR` (1200 m), une coque :

1. **lit la mer une fois par COLONNE de sondes** (63 pour la Roter Löwe au lieu
   de 315 sondes, 67 au lieu de 374 pour le chaland), à l'endroit où la colonne
   traverse son plan de flottaison, **avec la pente** ; chaque sonde lit sa
   hauteur d'eau sur ce plan tangent, à sa propre position ;
2. **fait un seul sous-pas par image** au lieu de quatre (la règle du 1/15 s
   fixe toujours le compte en temps pressé).

La grille n'est PAS allégée : mêmes volumes, même loi de remplissage, donc
même flottaison et même assiette — rien ne saute à la bascule. `lod` est posé
par la page à chaque image, comme les commandes.

Mesuré, solveur seul, pas fixe, médiane de six lots entrelacés :

| coque | exact (4 sous-pas) | colonnes, 4 sous-pas | LOD complet |
|---|---|---|---|
| chaland | 1,80 ms | 0,41 | **0,099** |
| Roter Löwe | 1,53 ms | 0,36 | **0,092** |

Dix-sept à dix-huit fois moins. Deux voiles au large coûtaient environ 3,3 ms,
elles en coûtent 0,2.

**La pente n'est pas un raffinement, et la première version l'a montré.** Sans
elle chaque sonde lisait la mer à l'aplomb du pied de sa colonne : juste à
l'endroit, faux gîté — à 28° une sonde de quille est deux ou trois mètres sous
le vent. Roter Löwe, force 8, largue, 90 s :

| | roulis RMS | pilonnement RMS | tangage RMS |
|---|---|---|---|
| exact | 4,42° | 1,85 m | 4,35° |
| colonnes à plat | 3,53 (−20 %) | 1,80 | 4,19 |
| exact, 1 sous-pas | 4,30 (−3 %) | 1,80 | 4,17 |

Avec la pente (autre tirage de mer) : exact 3,572° / 1,272 m / 3,592°,
colonnes 3,573 / 1,273 / 3,594, et 10 cm d'écart de route après 90 s à
12 nœuds. Le sous-pas unique reste dans 1,5 % et 3 m sur 550. En force 5 et au
chaland au moteur en force 7, les deux versions se confondent.

`ocean.sample()` rend désormais la pente sur demande (5ᵉ argument) : la
normale ne la rend pas, son y ayant pris le terme de raideur de Gerstner avant
d'être normalisé. Au passage, le solveur calculait une normale à chaque sonde
que personne ne lisait ; elle n'est plus demandée.

**Pièges de ce banc.** Le navire de départ est le chaland (`sailArea` 0) et il
est au port, où `shelter()` ramène la houle à 15 % : les premiers essais « en
force 7 et 9 » ne voyaient qu'une mer de port, et une coque « sous voiles » qui
ne bougeait pas. Bancs au large (6000, 0), fond à −70 m, fiche relue par
`new Naval.ShipSpec(json)`. La boucle du volet était gelée : la bascule en jeu
n'a pas été observée, seulement le solveur piloté à la main.

### Ce que la brume a déjà caché

Remarque d'Arnaud : avec la brume, on ne les voit pas de si loin. Juste — la
loi du shader, lue de 10 m vers un sommet à 25 m, laisse ce contraste :

| | 1 km | 2 km | 4 km | 5,5 km | 7 km |
|---|---|---|---|---|---|
| force 3 | 47 % | 22 % | 4,9 % | 1,6 % | 0,5 % |
| force 6 | 27 % | 7,5 % | 0,6 % | 0,1 % | — |
| force 8 | 0,4 % | — | — | — | — |

Les voiles naissent à 4–9 km : par beau temps la moitié y est invisible, par
gros temps toutes. Donc `Naval.hazeTransmit()` — la jumelle CPU de
`hazeAlong`, qui lit les mêmes uniformes — est évaluée au sommet de la mâture
(`ship.tallY()`) : sous `HAZE_HIDE` (1 %) la coque n'est plus dessinée, au-dessus
de `HAZE_SHOW` (2 %) elle revient. Une coque cachée n'anime plus ses voiles ni
son pavillon, et passe en LOD de simulation même en deçà de `LOD_FAR`.

**Par les calques, pas par `visible`** : `visible` porte déjà la voile
éclatée, le mât tombé, les couleurs amenées. `setHazed()` met le masque de
calques des maillages à zéro (ni rendu, ni ombre, ni SSAO) et le rend ensuite.
**Les lanternes sont sautées entières** : une lumière que le rendu ne voit plus
recompile la scène, et un feu est justement ce qui perce la brume la nuit.

Le gain de rendu n'est pas mesuré (boucle du volet gelée) ; le journal
« Combien de coques » donne l'ordre de grandeur, 3,56 ms pour huit coques.

### Le fanal au loin

Capture d'Arnaud, de nuit : deux voiles au loin lues comme deux réverbères.
Le repère de position du fanal est à taille d'**écran** fixe — voulu, sans lui
un feu disparaît — mais son éclat l'était aussi, et le bloom l'élargissait.
`setLantern(night, t, camPos, hazeU)` : au-delà de `night.farFrom` (1500 m)
l'éclat tombe en (farFrom/d)^`farFade` (1,5), la taille en racine, jamais sous
`farMinSize` (0,35) ; la brume le multiplie par la racine de sa transmission.
Réglages dans `settings.json`. Force 3, repère / taille :

| 100 m | 1,5 km | 2 km | 3 km | 5 km |
|---|---|---|---|---|
| 0,68 / 1 | 0,38 / 1 | 0,20 / 0,81 | 0,07 / 0,59 | 0,015 / 0,41 |

La brume agit dès le départ (moitié de l'éclat à 1,5 km par beau temps) :
c'est elle, et non le seuil, qui dit combien une lumière perce.

## Les vergues ne sautent plus d'un bord à l'autre

Signalé à l'usage : les mâts « sautent de droite à gauche selon le vent ».
`setTrim` écrivait `−tack·sheet` droit dans les pivots, et `tack` est le signe
nu du vent apparent en travers : il bascule dès que ce vent passe par l'arrière
(ou l'avant). Au vent arrière, une embardée d'un degré suffit, et tout le
gréement passait d'un bord à l'autre en une image — deux fois l'angle d'écoute,
jusqu'à 2,6 rad.

**Rien dans la physique ne lit ce signe** (la portance s'oriente sur l'étrave ;
le pavillon est continu à β = π ; le HUD n'en fait qu'un libellé), donc le
remède est dans le modèle, sur son instance :

- l'amure **montrée** ne change qu'après `BRACE_HOLD` (1,5 s) de vent tenu sur
  l'autre bord ;
- les vergues tournent à `BRACE_RATE` au plus (0,8 rad/s de temps de jeu, un
  peu plus que Q/E à 0,7), **en douceur aux deux bouts** : elles prennent de
  l'erre à `BRACE_ACCEL` (1 rad/s²) et sont freinées juste à temps, la vitesse
  permise étant √(2·a·écart). Écrit contre l'écart et non comme une courbe
  minutée, si bien qu'une écoute réglée pendant le mouvement est suivie ;
- le pas est pris sur l'horloge passée à `setTrim`, borné à 0,25 s : un saut
  d'horloge (mise à l'eau, compression) ne fait pas pivoter tout d'un coup.

Vérifié sur un gréement factice : une amure qui bat toutes les 0,7 s pendant
4 s ne bouge rien ; une amure qui passe et tient : 1,5 s d'attente, puis
+1,3 → −1,3 rad en 4,0 s, adouci aux deux bouts, sans dépasser, 0,013 rad au
plus par image ; le foc aux trois quarts. Q/E tenu une seconde : 0,25 rad de
retard au plus, rattrapé 0,7 s après le relâchement. Le frisson du faseyement
(±0,1 rad) s'ajoute après le lissage, il n'est pas amorti.

## Plusieurs pavillons, et leur coupe

Demandé sur photo d'un galion espagnol : grand pavillon sur une hampe à la
poupe, flamme en tête d'artimon, pavillon au bout du beaupré ; puis une flamme
à queue fendue (armoiries au guindant, longue pointe effilée).

**`flags` dans la fiche, rien de déclaré = l'ancien pavillon unique** en tête
du plus grand mât. `this.flag` reste l'alias du premier ; la page ne touche
plus `flag.pivot.visible` mais `showColours()`, qui amène ou hisse tout.

**La coupe se pose sur la grille que l'ondulation travaille déjà** (`u` le
long du battant, `v` le long du guindant) : une *réduction* de hauteur vers le
bout, autour du milieu du guindant, et une *entaille* qui ramène le bord libre
en V. `Naval.FLAG_SHAPES` en donne quatre : `rect`, `swallowtail`, `pennant`,
`streamer`. Les coordonnées de texture suivent la coupe : une image peinte sur
un rectangle est rognée par l'entaille, pas écrasée dedans. Une longue flamme
porte plus d'ondes (fréquence ∝ longueur) et plus de colonnes (jusqu'à 48).

**Les couleurs du navire sont partagées, les armes propres ne le sont pas** :
un pavillon sans `image` prend `mats.flag` (changé d'un coup par le pavillon
d'emprunt ou le pavillon noir) ; une `image` a sa matière, bâtie comme une
voile peinte et raccordée au ciel à la main pour la même raison. Le build
embarque chaque `flags[].image` et retire celles qui manquent.

**Le chercheur de mâts ne suffisait pas**, et c'est la vraie leçon. Il répond à
« quels mâts portent des vergues » : sur la Roter Löwe il trouvait le fût du
grand mât, une misaine sans fût propre, et la vergue de civadière au bout du
beaupré (1,1 m de haut) — **pas d'artimon**, qui ne porte pas de vergue carrée,
et dont le fût est fondu avec celui de misaine dans `Cylinder001`. Et le
beaupré « de la fiche » (7,75 m depuis l'étrave) mettait le pavillon trois
mètres devant l'espar dessiné, la coque du modèle portant déjà la guibre.

`_sparScan()` lit donc les espars **tels que dessinés** : les pièces fines près
de l'axe et hautes de plus de 0,3 L, rangées par stations de 0,06 L ; une suite
de stations qui dépasse 0,35 L est un mât, coiffé à son plus haut sommet (un mât
quêté s'étale sur plusieurs stations et reste un) ; les fûts déjà pris par le
gréement sont rajoutés depuis leurs chutes. Le beaupré est la pièce fine qui
va le plus loin devant. Relevé sur la Roter Löwe : artimon (−6,9 ; 13,9),
grand mât (0,4 ; 18,3), misaine (11,4 ; 15,2), bout de beaupré (20,2 ; 6,1).
Un pavillon de mât pend dans la chute la plus proche (à 0,06 L près) et tombe
avec elle ; sinon dans la coque. Les hampes n'appartiennent à aucun mât.

`_deckStations()` est devenu la définition unique du pont aux extrémités,
partagée par le fanal et les hampes.

**Le guindant suit la hampe.** Première version : pavillon accroché droit au
bout d'une hampe quêtée de 0,3 rad — il flottait à côté de son propre bois
(capture). Chaque pavillon pend maintenant dans une *monture* inclinée comme ce
qui le porte, et tourne au vent autour de cet axe. `tilt` (tête vers l'arrière
si positif) remplace l'inclinaison par défaut, et en donne une à un pavillon de
tête de mât.

**L'image est coupée, pas écrasée.** Les coordonnées de texture suivaient `v`
sur toute la hauteur alors que la forme se rétrécissait : les armes se
tassaient vers la pointe, et aucun gabarit n'aurait dit vrai. Elles suivent
maintenant la hauteur réelle (`0,5 + (v − 0,5)·w`), si bien que ce qui est peint
à une hauteur donnée flotte à cette hauteur. `tools/flag-template.js` écrit un
gabarit PNG + SVG par forme, lu dans `Naval.FLAG_SHAPES`.

**Le pavillon du joueur.** Un menu sous le choix du navire, rempli depuis
flags.json, hisse la nation choisie sur l'entrée 0 — à la mise en service et à
chaque prise de barre, les couleurs étant celles de qui commande. « Pavillon de
la fiche » rend l'original (`resetEnsign()`, gardé au premier changement). Le
choix vit dans `localStorage` (commodité, sous try/catch). L'état est déclaré
AVANT `commission()` : le premier appel précède de loin le chargement de la liste,
et un `let` plus bas aurait levé une erreur de zone morte. Purement visuel : les
pirates n'en tiennent pas compte.

**La flamme suit la nation, pas le navire.** Une `image` dans la fiche attachait
les armes à la coque : la Roter Löwe sous pavillon français gardait une flamme
castillane. Décidé avec Arnaud : c'est flags.json qui dit, par nation et par
coupe (`"streamer": "…"`), et la fiche ne dit que `"image": "nation"`.
`setEnsignMap(src, nation)` retient l'entrée et réaffecte la matière de ces
pavillons ; `resetEnsign()` l'oublie. Le build embarque toute clé d'une entrée
qui nomme une image, et retire celles qui manquent.
Puis `"nation:<clé>"` : le pavillon de poupe est un `rect` comme ceux des têtes
de mât, et la clé de coupe les aurait confondus — les armes de Colomb au
couronnement, les couleurs en tête de mât.

**Portage Godot (2026-09-19).** La grille et l'onde dans le noyau
(`FlagCloth`, `FlagShape`), la table des nations (`Nations`, tirage au poids) ;
le banc de la toile relève `_flagAt` et `setFlag` par leur texte et les compare
sur cinq coupes et trois brises : **écart nul**, au bit près. Le placement
(`ShipNode.Flags.cs`) suit la page : têtes de mât relues sur les espars, pendus
dans la chute de leur mât, hampes de poupe et de beaupré, bout du beaupré lu
sur le modèle. Relevé sur le Roter Löwe (30 m) : poupe à −13,3 m, beaupré à
+20,4, flamme à l'artimon, grand pavillon à 15,2 m. Le vôtre se choisit dans
les options (`reglages.ini` → `pavillon`), les autres voiles en tirent un ; le
pirate paraît sous le noir, sans ruse d'emprunt (le démasquage n'est pas
porté) ; chaque camp fantôme a sa nation, pâlie avec lui. Écart : le beaupré
d'une fiche procédurale (`rig.bowsprit`) n'est pas lu — aucune fiche à
coque dessinée ne déclare de pavillon d'étrave.

## La foudre et le kraken

Décidé avec Arnaud : un **kraken** d'abord, **dessiné par le code**, qu'on peut
**fuir** et **combattre**, qui d'abord garde ses distances et se rapproche si
l'on s'attarde — et qui **ne coule pas** le navire. Et la **foudre sur un mât**.

**Les deux vivent dans les dépressions existantes** (`storms.at` → `inten`) et
réutilisent ce qui existait : blessures de mât (`blesserMat`, désormais la règle
unique des boulets, de la foudre et des bras), voiles qui éclatent, gerbes,
sons retardés par la distance, cibles du canon (`guns.creatures`), refus de
presser le temps. Aucun n'est un navire : pas de place dans la flotte ni dans
la texture de profils. Réglages dans `settings.json` → `storm`.

**La foudre** (`lightning.js`) : par navire, `perMinuteAtCore`·k par minute
avec k croissant de `minInten` au centre, sur la plus haute tête de mât encore
debout (`ship.mastTops()`). Le trait est un tube additif, **sans lampe** ;
l'éclat vient de `stage.strike()`, allumé seulement à moins de 3 km. Tonnerre
**synthétisé** (`sound.tonnerre`, bruit brun) — aucun échantillon à embarquer.
Premier tracé : du bruit autour d'une droite, qui à un câble lit comme un
faisceau ; puis une marche aléatoire, encore droite de loin ; enfin un **zigzag**
à pas alternés de 8 à 30 m, rappelé sur la tête de mât sur le dernier tiers.

**Le kraken** (`kraken.js`) : absent → rôde (450 m, `lingerBefore` 45 s à
distance, puis approche à 1,2 m/s + 0,03 m/s par seconde passée) → étreinte à
45 m → plonge. Il **décroche** si l'on file plus de 7 nœuds (4 m/s) et renonce
à 800 m ; il plonge si l'intensité tombe sous la moitié du seuil, ou si ses
14 points de vie tombent (bras 1, manteau 2 par boulet plein calibre). Un bras
touché lâche prise 5 à 8 s. Toutes les 9 s un bras tord un mât (blessure) ou
déchire une voile. Grondement synthétisé (`sound.grondement`).

**Tenir un navire, c'est une amarre** — `ShipPhysics.grips`, traitée par
`_moor` comme `moorings` mais dans sa propre liste, parce que la page lit « des
amarres » comme « à quai ». Trois réglages, trouvés en mesurant :

- **Traction à la lisse, pas au mât.** Accrochée au tiers d'un mât, la traction
  horizontale avait un bras de levier de plusieurs mètres : galion couché à
  36–48° tant qu'il tenait. Le bras reste *dessiné* enroulé au mât ; la force
  s'applique là où il passe la lisse, et les bras alternent de bord.
- **`gripHold` 0,07 → 0,03** du poids par bras : même à la lisse, trois bras
  d'un bord à 7 % donnaient 44° de pointe. À 3 % : gîte RMS 4,2°, pointe 12,9°
  (mer libre : 15°).
- **Un frein, `gripBrake` 0,25/s par bras**, au centre de gravité : sous voiles
  en force 7 la Roter Löwe filait encore 12,5 nœuds, les points d'appui cédant
  (règle de l'ancre). Avec le frein : 10,7 → **0,3–0,6 nœud**, gîte 16° de
  moyenne (13,8° libre, c'est le vent).

Relevés au banc (solveur piloté à la main, Roter Löwe) : apparition → étreinte
en ~3 min à la cape ; fuite à 11 nœuds → renonce en ~80 s ; sept boulets au
manteau après un au bras → plonge et relâche tout. Chaland : 2,8 → 0,6 nœud.

**Dessin** : chaque bras est un tube de 30 anneaux × 7, transporté
parallèlement, reconstruit à chaque image (six bras : négligeable). Il rôde en
arche qui sort et rentre ; il saisit par une courbe de Bézier depuis sa base
sous le flanc, **bombée loin dehors** (une première version presque droite
lisait comme des perches), puis trois tours autour de l'ancre ; `grow` fait
avancer le bras le long de ce chemin. Le manteau n'affleure que d'un mètre (à
−2,4 m il flottait comme une assiette). Yeux en `MeshBasicMaterial` : ils
émettent. Phase du dos tirée à chaque apparition.

**Pièges de banc** : `tempete()` transporte le navire mais la mer ne monte
qu'à `weather.seaRate` (0,0125/s) — pour une capture, le relever quelques
secondes ; un test qui déchire toutes les voiles laisse `whole = 0` et le
navire immobile (`restoreMasts()`) ; l'éclair dure 0,25 s (0,38 d'abord, jugé trop long à l'usage), il faut retenir son
`age` pour le photographier ; le navire actif au démarrage est le chaland, au
port.

## Le kraken en .glb, la foudre dessinée, le safran

**Un modèle pour le kraken, et le bras reste calculé.** Arnaud veut texturer
le monstre et lui ajouter des ventouses. Le chemin du bras (arche, étreinte,
tours autour du mât) est calculé à chaque image ; plutôt que de demander une
armature et des animations, le `.glb` fournit un bras **modelé droit** (`bras…`,
debout sur +Z dans Blender) et le jeu le **courbe** : un sommet à la hauteur y
va au point du chemin à la fraction y/longueur, son x le long de N, son z le
long de N×T — T×N aurait inversé la chiralité et retourné les faces — et ses
normales tournent avec le repère. Le repère part de `n0`, le côté tourné vers
ce que le bras tient (vers le navire en étreinte, vers l'avant de l'arche en
rôdant) ; le transport parallèle le garde de ce côté à chaque courbe, d'où la
règle **ventouses sur +X**. Tout le reste du fichier est le corps. Modèle de
départ : `tools/kraken-glb.js` (le kraken dessiné, avec normales et UV, trois
matières). Le build l'embarque depuis `settings.json` → `storm.kraken.glb`.
Rayon de touche du corps : 0,32 × sa plus grande dimension horizontale (0,45
donnait 7,6 m pour les 5,5 du dessin). Doc : `creatures/README.md`.

**La foudre est un dessin.** Trois tubes (bruit, marche aléatoire, zigzag)
lisaient tous comme un faisceau. Sur le croquis d'Arnaud : grands zigzags
horizontaux qui descendent par crochets, avec des fourches. L'éclair est donc
peint sur un canevas (halo violet, cœur blanc), tendu sur une bande qui tourne
sa face vers la caméra autour de l'axe nuage → tête de mât : dans la scène,
testé en profondeur, dans les captures. 0,25 s de vie, à la demande d'Arnaud
(« il doit disparaître juste après avoir frappé »). Une fois vu un grand
rectangle blanc à la place du trait, jamais reproduit ensuite, même éclat du
ciel au maximum.

**Le safran tourne avec la barre.** Aucun navire n'en avait. Un objet
`gouvernail` d'un `.glb` est ferré sur son bord avant ; sinon une planche est
dessinée à l'étambot **à la flottaison** : sur la Roter Löwe à −10,3 m, quand le
tableau est à −12,3 — la voûte surplombe. Angle = barre × `maxAngle`, du signe
du solveur (barre positive : arrière du safran vers tribord). Pas de safran pour
`rudder.power` 0 (chaloupe). Mis à jour avec la toile, donc pas pour une coque
perdue dans la brume.

**La roue suit la barre.** Arnaud a ajouté au `.glb` de la Roter Löwe une
`barre` — un cylindre aplati, donc une roue d'axe longitudinal — et un
`rudder`. La roue tourne autour de l'axe où la pièce est la plus mince dans son
propre repère, 3 tours de chaque côté (1,25 d'abord : « la barre doit faire
plusieurs tours sur elle-même pour un gouvernail complet ») ; vérifié : barre à tribord, le haut de
la roue part à tribord, la roue reste dans son plan. **Le premier `rudder` exporté était
un nœud vide** (corrigé ensuite par Arnaud : maillage de 5,9 m de haut, ferré
vers z = −9) (ni maillage, ni enfant, ni transformation) : pris tel quel il
aurait été ferré et tourné sans rien montrer, et le safran dessiné retiré pour
lui. Un nom n'est donc retenu que s'il porte un maillage ; sinon la console le
dit et le dessin reste.

**Le safran a son rythme.** Demandé : « le gouvernail doit mettre plus longtemps
à se déplacer, surtout sur les gros navires ». Le solveur tient `rudderPos`, qui
rejoint `ctrl.rudder` en `rudder.hardOver` secondes de la barre droite à la
butée (défaut 3·√(L/24)), sous-pas par sous-pas ; la force de gouvernail vient
de cette position, et le safran comme la roue la dessinent. L'ordre (HUD,
clavier, barre des autres navires) reste instantané ; c'est l'exécution qui
traîne. Le pilote automatique voit donc un retard de plus dans sa boucle. La console « Barre »
montre la position réelle (barre et degrés, depuis `maxAngle` et non plus 35°
en dur) et l'ordre en repère tant que le safran ne l'a pas rejoint — « on vient à
tribord… 35° ».

**Le kraken choisit sa proie** (« il peut s'en prendre à d'autres navires dans la
même zone »). Chaque coque de la flotte tient son propre temps passé au cœur
d'une dépression (`lingered`, par entrée) ; le kraken monte à côté de celle
qui s'y est attardée le plus longtemps, et c'est désormais la **victime** —
son solveur, son modèle — qu'il suit, tient et déchire, qui que ce soit qu'on
commande. Prendre la barre d'un autre navire ne le fait plus plonger. Il plonge
si la victime quitte la flotte ou sombre (`alive`). Les messages passent par
des événements ; la page nomme le navire quand ce n'est pas le vôtre, et se tait
au-delà de 3 km. Presser le temps n'est refusé que si la victime est à moins de
3 km. Relevé : pirate seul au cœur → proie pirate ; les deux, le joueur depuis
plus longtemps → proie le joueur ; pirate tenu (6 bras, 0 sur le chaland),
retiré de la flotte → plongée, prises lâchées.

**Arnaud valide lui-même le rendu** : plus de captures de ma part.

## La flotte fantôme

Voulu par Arnaud : de nuit, en un lieu précis, une flotte de navires
fantomatiques rejoue une bataille passée ; on y assiste si l'on s'attarde.
Ses choix : **pris à partie si trop près**, **pâles et translucides**, **deux
contre deux**, et la scène finit au **premier** de trois événements — l'aube,
un camp coulé (le vaincu sombre, le vainqueur s'efface), ou le départ du
témoin. Puis : **voiles déchirées**, d'après une image.

**Le lieu** : *Le Cimetière des Galions*, (−9 000 ; 6 000) en mètres vrais,
70 m de fond, 5,7 km de toute côte, rayon 2 500 m, cercle pointillé et nom en
italique sur la carte (`chart.places`). `settings.json` → `ghosts`.

**Ce sont de vraies coques de la flotte**, mises à l'eau par `launch()`,
menées et combattues par la même barre et les mêmes pièces : chacune reçoit
pour `provoquePar` l'ennemi le plus proche de l'autre camp, et
`servirLesPieces` fait le reste. Seuls les modèles qui ont des canons se
battent (Roter Löwe, galion pirate) : deux Roter Löwe contre deux galions,
chaque camp sous un pavillon national tiré de flags.json. Le pirate y perd son
avidité (`hostile`, `pirate` à faux).

**Ce qui en fait des spectres, décidé autour d'eux** (`ghosts.canTouch`) :
fantôme contre fantôme, tout porte ; le vivant ne touche jamais un fantôme
(ni boulet — `guns.canHit` laisse passer —, ni abordage : les voisins de
collision sont filtrés) ; un fantôme ne touche un vivant que s'il s'est
retourné contre lui, à moins de `aggroRange` (350 m). Ils ne provoquent ni ne
sont provoqués par les vivants, un vrai pirate ne les chasse pas, le kraken ne
les prend pas, un fantôme coulé ne laisse ni débris ni bouteille, et ils
n'apparaissent pas dans le panneau Flotte. Pas de nouvelle voile ni de temps
pressé pendant la scène.

**L'aspect** : chaque matière du modèle teintée vers un vert froid, émissive
faible (ils luisent, ils ne réfléchissent pas la lune), transparente à 45 % ×
un fondu de 6 s ; fanaux verts ; voiles en loques par une carte de
transparence dessinée (pied en langues, entailles aux ralingues, trous
étoilés, trois variantes) découpée par `alphaTest`.

**Relevé** (banc piloté à la main, la boucle du volet tournant à 3–6 % du temps
réel — même cadencée par minuterie) : lignes à 700 m ; premières bordées vers
2 min ; 252 boulets et une Roter Löwe à 17 voies d'eau à 8 min ; première
coulée à 9 min 34, effacée et retirée 25 s plus tard ; une par camp à 11 min.
Témoin posé à 250 m d'un fantôme : les deux survivants se retournent contre
lui, six coups au but en une minute, l'un vient à 62 m. Départ à 4 km : fondu,
retrait, pas de nouvelle bataille cette nuit (`spent`). Aube : fondu en 6 s.
Victoire : vainqueurs effacés à 10 s, vaincus à 25 s — la première version les
effaçait ensemble, et décomptait deux fois le délai des coulés.

**Ils luisent et ils glissent** (demandé ensuite). La lueur : émissive portée
à 0,95 dans un vert d'eau clair — assez pour que le bloom de nuit s'en saisisse
— et une **aura** de trois sprites additifs le long de la coque (la texture du
fanal, vert, 20 % × fondu, qui palpite un peu), sans lampe. La glisse :
`ShipPhysics.glide`, une hauteur ; après intégration la coque y est remise,
droite, cap libre, vitesse verticale et rotations de roulis/tangage annulées, et
elle ne déclenche pas de gerbes. Relevé en mer 6 : houle de 4 à 7,5 m sous eux,
**0° de roulis, 0° de tangage, 0 m de pilonnement**, 9 nœuds. **La glisse lâche
à un quart du volume envahi** — sans cela un fantôme tenu à sa flottaison ne se
serait jamais « trouvé sous l'eau trois secondes », et n'aurait jamais coulé :
relevé, lâchée à 3,8 s d'une voie d'eau géante, coulé à 12 s.

**Le bas s'efface, la brume les porte** (d'après une capture retouchée par
Arnaud). `Naval.applyGhostFade` : une greffe de plus sur chaque matière du
fantôme, chaînée après toutes les autres — la brume comprise, qu'elle suit en
fin de fragment — avec sa clé `|ghost-fade`, qui multiplie l'alpha par une
rampe sur la hauteur **monde** : rien à 0,5 m au-dessus de la mer, entier à
0,5 + 0,15·L (le plat-bord d'un galion). La coque glissant à hauteur fixe, c'est
son bas qui disparaît ; un fantôme qui sombre s'efface par le bas en
s'enfonçant. Autour, onze bouffées de fumée (la texture des canons, teintée du
même vert, additive) au ras de l'eau, le long et de part et d'autre, qui
dérivent et respirent ; la mer en cache la moitié basse, ce qui les pose dessus.
Vérifié : 16 matières greffées, aucune erreur de compilation.

Piège d'outillage : des `
` écrits dans un gabarit de script par un heredoc
sont arrivés en vrais sauts de ligne dans une chaîne à guillemets simples —
`node --check` l'a vu, pas le build, qui a écrit une page cassée sans rien dire.

**Pièges** : `fantomes()` réglait le soleil par le curseur, que le cycle du
jour réécrit aussitôt depuis `stage.dayTime` : la scène se terminait « à
l'aube » à la première image. Il règle l'heure (21 h). Et une boucle gelée ne
rafraîchit pas `worldPos` : le module croyait le témoin encore à l'ancien
endroit.

**Portage Godot (2026-09-19).** La scène est dans le noyau (`Ghosts.cs`,
`GhostScene` + `IGhostHost`), la glisse dans le solveur (`ShipPhysics.Glide`,
scénario de parité « glisse » tenu à 2,5 µm), le filtre des boulets dans
`Gunnery.CanHit`. Touche P ou `--fantomes 1` pour y aller. Trois écarts voulus
avec la page : **une seule peau** — chaque matière est dessinée deux fois, une
pré-passe de profondeur (`render_priority` −10) puis la couleur un rien
avancée : sans elle bordé, vaigrage et pont s'empilaient à 45 % chacun et le
spectre paraissait plein (demandé par Arnaud) ; **la ligne de fondu ondule** et
le bas s'éteint vers le noir, avec des bandes pâles qui montent ; **de la
vapeur** monte des ponts (14 volutes qui naissent vite et s'éteignent
lentement). Puis, demandé encore : **pas de contour** — l'opacité suit |N·V|,
une surface vue de biais s'efface et la silhouette se fond ; lueur portée à
1,5 (le bord n'en porte plus), aura plus large et plus forte ; `opacity` de
settings.json passée de 0,45 à 0,3 (la page en profite aussi). Pas encore de
pavillons nationaux (Godot n'a pas de pavillons). Relevé : camp pirate percé
à t = 20 s, glisse lâchée à 30 % d'eau, coulés à 75 s, vainqueurs dissous et
retirés 10 s plus tard (flotte 5 → 3), sans exception.

Au passage, la toile de TOUS les navires côté Godot reçoit `BACKLIGHT`
(0,45) : la transmission diffuse d'un lin, pour toute lumière derrière elle
— soleil, lune, fanal —, en plus du lobe de la page qui ne vaut que dans
l'axe du soleil.

Bogue de la page trouvé au portage : `alphaTest` 0,45 comparé à
opacité × carte, avec une opacité ≤ 0,45 : toute la toile disparaissait tant
que le fondu n'était pas complet. Seuil désormais à la moitié de l'opacité
courante. Piège Godot : un `varying` ne s'écrit pas dans une fonction d'un
include, seulement dans `vertex()` — le passer en paramètre.

## La grande carte

Demandé : « pouvoir dézoomer plus la carte ou l'afficher à part avec une
touche ». Les deux. La butée de zoom passe de 1,35 à **2,5 fois l'étendue** de
l'archipel (21 M au lieu d'environ 11) — le Cimetière des Galions est au large.
Et **O** ouvre une grande carte au centre de l'écran : le **même** `draw()`,
rendu dans un second canevas par `chart.drawInto(canvas, scale, els, …)` qui
échange canevas, échelle et relevés puis les rend, avec les arguments du
dernier tracé de la petite carte. Zoom propre (molette, boutons), fermeture
par O ou Échap, 78 % de la hauteur de l'écran. Vérifié : 11 M à l'ouverture,
21 M en butée, la petite carte inchangée (1,60 M).

Piège de banc : un `keydown` envoyé à la fois à `window` et à `document` arrive
DEUX fois à l'écouteur de la fenêtre — la carte s'ouvrait et se refermait.

## La lune, une nuit sur deux ou presque

Demandé : « la nuit il peut y avoir la lune aléatoirement ». Jusque-là le
soleil passé sous l'horizon tenait lieu de lune — une lumière bleue fixe à
0,28, donc une lune chaque nuit, invisible.

**Tirée à la tombée de chaque nuit** (`stage.newMoon`) : présente avec la
probabilité `night.moonChance` (0,6), une phase de 0,2 à 1, et un décalage
horaire d'autant plus grand qu'elle est mince (± 9 h × (1 − phase)) — un
croissant suit le soleil, une pleine lune se lève quand il se couche. Sa
direction est l'opposé du soleil **tourné autour du pôle céleste** (latitude du
monde, vers le nord +z) de ce décalage : elle se lève, passe et se couche avec
le ciel, sans horloge à part.

**Ce qu'elle change** : `lightDir`, d'où vient la lumière directe (et
l'ombre), est la lune de nuit quand elle est levée ; l'intensité de nuit va de
0,12 (ciel vide) à 0,40 (pleine) ; la voûte dessine son disque — face éclairée
du côté du soleil, terminateur en ellipse, part sombre à peine visible, halo
froid, plus gros que nature — et la mer un second lobe de reflets, froid, qui
est son chemin sur l'eau (`uMoon`, `uMoonLit`, calculés dans `onSunChange`).
Rien de tout cela ne touche la houle : les trois calculateurs sont intacts.

Relevé sur six nuits : quatre lunes ; croissant de 0,32 haut à 19 h et couché
avant 1 h ; lune de 0,82 levée à 22 h, 63° à 1 h ; lumière 0,12 sans lune,
0,23–0,36 avec.

## La mer qui luisait la nuit

Signalé sur capture : de nuit, caméra au-dessus de l'eau, la mer paraissait
éclairée par-dessous. Vue d'aplomb, le reflet du ciel ne pèse que 2 % (Fresnel)
et l'on ne voit que le **corps** de l'eau — or `uDeep`/`uShallow` étaient un
pigment constant, turquoise à minuit comme à midi. Et la lueur des crêtes
(`uSSS`) se calculait sur un soleil passé sous l'horizon, maximale vue d'en
haut.

`uWaterLight` (posé avec le soleil) : `(0,75 + 0,25·t)` le jour (t = hauteur du
soleil / 30°), `0,07 + 0,16·clair de lune` la nuit ; il multiplie le corps, et
la lueur des crêtes s'éteint avec le soleil sous l'horizon. L'écume, qui se
règle sur le corps et l'horizon, suit d'elle-même. Relevé, vue plongeante,
moyenne de six points (RVB) : midi 56·107·131 inchangé ; nuit sans lune
**9·54·77 → 3·6·8** ; pleine lune 4·14·20.

## Le calendrier

Demandé : une date, en Estonia, en haut à droite sous la console, au format
« October 8th, 1598 ». `js/calendar.js` : un jour de plus à chaque minuit du
cycle (détecté dans la boucle quand `dayTime` revient en arrière), départ dans
`settings.json` → `calendar.start`. Grégorien — l'Espagne, la France et les
États italiens l'avaient adopté en 1582 — et `Date` le remonte sans broncher
(une année < 100 est posée à part, `Date.UTC` la prenant pour 19xx). Suffixes
anglais, 11th–13th compris.

**La saison vient de la date.** Le soleil était tenu à un printemps fixe
(déclinaison 12°) ; il prend maintenant celle du jour de l'année (approximation
en cosinus) : −6,5° le 8 octobre, −15° le 1er novembre, 11 h 45 de jour à cette
latitude au lieu d'environ 12 h 25.

**La colonne de droite est empilée.** Les hauteurs étaient écrites en dur et se
chevauchaient déjà (console de mer jusqu'à 448 px, boutons de caméra dès 366,
Flotte dès 398). `rangerColonne()` pose mer, date, boutons, Flotte l'un sous
l'autre toutes les demi-secondes, en sautant ce qui est masqué. Relevé sur
910 px de haut : 16–448, 452–484, 492–553, 561–707. **Reste** : la carte, ancrée
en bas, commence à 583 px et recouvre le bas de la Flotte sur un écran de cette
hauteur.

## Le temps qu'il fait : averses, neige, et la température en mots

Demandé : de la pluie et de la neige aléatoires, la neige en hiver, la
température affichée en Estonia. Deux décisions d'Arnaud : **le climat est
réglable et froid permis** (l'archipel est aux Antilles pour le soleil, où il ne
neige jamais — la température suit donc son propre climat, celui de la Manche
par défaut, sans toucher la course du soleil), et **la température se dit en
mots** : pas de thermomètre en mer en 1598.

`js/climate.js` : moyenne 9,5 °C, ± 9 sur l'année (le plus froid le 30 janvier),
± 3 sur la journée (le plus chaud à 15 h), ± 3 d'un jour à l'autre (stable pour
une même date), −3 par gros temps, −2 sous l'averse. Mots : glacial, gel,
froid mordant, frais, doux, tiède, chaud, chaleur lourde. **Averses** tirées en
heures DE JEU (3 par jour en moyenne, 20 à 90 minutes, intensité en cloche),
donc aussi fréquentes par journée quel que soit le temps pressé ; ce qui tombe
est la plus forte de l'averse et de la tempête, **neige sous 1,5 °C**. Une
averse couvre aussi le ciel (`updateWeather` prend un voile en plus). Relevé :
21 averses et 16 h de pluie en dix jours d'octobre ; assez froid pour neiger
une partie de chaque jour de décembre à mars, douze jours en novembre (avec
11 °C / ± 8, la neige n'était possible qu'aux matins les plus froids).

`js/snow.js` : le treillis de la pluie, en points ronds dimensionnés à l'écran,
chute d'un mètre par seconde, chaque flocon sur son petit cercle, le vent en
portant l'essentiel ; clairsemé par graine quand il neige peu ; retiré du
miroir comme la pluie. **Ce qui tombe réfléchit** : pluie et neige prennent
désormais la clarté de l'horizon — la pluie avait une couleur pâle fixe et se
voyait à minuit. Ligne `#tempLine` sous la date, dans la colonne empilée
(« Frais, il pleut »).

**Corrigé le 2026-09-19, trouvé en portant le climat sous Godot** : `_h` rendait
un hachage SIGNÉ — `x ^= x >>> 15` laisse un int32 en JavaScript —, donc dans
[−0,5 ; 0,5) et non [0 ; 1), et l'errance d'un jour à l'autre allait de **−6 à
0 °C** au lieu de ± 3. Le climat était en moyenne 3 °C plus froid que ses
réglages. Corrigé des deux côtés ensemble (`(x >>> 0)`, et `Climate.DayHash`
du noyau), la parité le vérifie. **Les relevés ci-dessus ont été faits avec ce
climat trop froid** : la neige de novembre et de mars y est surestimée ; à
remesurer si on veut les chiffres.

## Le manteau de neige

Demandé : que le navire se couvre d'un petit manteau blanc quand il neige.
`Naval.applySnowCover`, une greffe de plus sur les matières du navire, **avant
la brume**, clé `|snow` : la couleur diffuse va au blanc de neige là où la
normale **monde** regarde le ciel, d'abord les plats (pont, hunes, dessus des
vergues et des lisses), puis des pentes de plus en plus raides à mesure que la
couche s'épaissit ; bord cassé par un bruit posé dans le repère **du navire**,
pour que les plaques restent où elles sont tombées quand il roule. Une voile,
pendante, n'en prend presque pas par la même règle.

L'épaisseur est **au navire** (`ship.snowCover`, `ship._snowU`) — un navire
sorti de la neige la garde jusqu'à ce qu'elle fonde : +chute/600 par seconde
de jeu (dix minutes de forte chute), plafond 0,85, fonte de (0,5 + degrés au-dessus
du seuil)/1 800 par seconde. Les spectres n'en prennent pas. Relevé, pont de la
Roter Löwe vu d'aplomb à midi : 98 nu, 160 à 0,3, 175 à 0,8 ; 48 matières
greffées, sans erreur ; couche pleine en 8 min 30 de forte chute, fondue en
4 min 40 à 6 °C.

## Les dauphins, et des mouettes qui restent côtières

**Dauphins** (`js/dolphins.js`, `settings.json` → `dolphins`). Par mer calme
(sous force 3,5), loin d'un port (250 m), quatre bancs par jour DE JEU en
moyenne, de 3 à 7 animaux, pour 3 à 8 minutes ; ils partent aussi si la mer se
lève. Ils font ce que font les vrais : **surfent la vague d'étrave** si le
navire a de l'erre (1,5 m/s) — une place chacun, devant l'étrave et de part et
d'autre, qui ondule —, **tournent autour** s'il est arrêté, puis décrochent
vers l'arrière et plongent. Chacun poursuit sa place comme un nageur (ressort
amorti, plafond max(6 m/s, erre + 4)) et suit son propre cycle de surface : un
arc hors de l'eau sur 28 % du cycle, un vrai saut une fois sur cinq, une petite
gerbe en rentrant. Dessinés : corps tourné (profil en 12 points), aileron,
nageoires, caudale sur pivot qui bat ; contre-ombrage en couleurs de sommet ; la
brume sur leur matière. Premier réglage : hors de l'eau 5 à 8 % du temps, trop
timide ; arc relevé et lissage raccourci : **16 %**. Relevé : à 6 nœuds, 12–16 m
devant le centre du chaland (L/2 = 14, donc à l'étrave), 3–7 m par le travers ;
arrêté, 30–40 m autour ; partis en moins d'une minute. `Naval.app.dolphins.summon()`.

**Une gerbe à la sortie ET à l'entrée** (demandé ensuite). La seule gerbe, au
plongeon, demandait 0,5 m³ à la réserve d'embruns — qui compte onze gouttes au
mètre cube : six gouttes, invisibles. Désormais 1,8 m³ en sortant (jet plus
haut) et 3,2 m³ en rentrant, fois 2,2 sur un vrai saut. Relevé, trois dauphins,
une minute à 6 nœuds : 59 gerbes de sortie, 58 d'entrée, environ 63 gouttes par
seconde — loin des 2 000 de la réserve.

**Le dauphin en .glb** (`creatures/dolphin.glb`, `tools/dolphin-glb.js`,
`settings.json` → `dolphins.glb`, embarqué par le build comme le kraken). Le
modèle de départ est le dauphin dessiné : `corps` (profil tourné, UV, couleurs
de sommet), `aileron`, deux nageoires, et `queue` dont l'**origine est
l'articulation** — le jeu la fait battre autour de son X ; tout le reste est le
corps. Chaque animal reçoit sa copie ; les matières du fichier prennent la brume
(`setAtmosphere` retient l'air pour un modèle qui arrive après). **En
l'écrivant, un défaut du dessin** : la caudale était tournée de −π/2 et
s'étendait VERS L'AVANT dans le pédoncule ; elle traîne maintenant vers
l'arrière (−1,11 à −1,37 m). Vérifié : modèle chargé, corps 4 pièces, queue 1.

**Mouettes à moins de 2 km des côtes.** Elles s'affichaient d'après la distance
au **centre** de l'île (2,6 km), et le tiers qui suit le navire le suivait
n'importe où. Désormais la distance est prise à la **côte** (le même `_shore`),
et les suiveuses rentrent au-dessus de leur île au-delà de 2 km (retour à
0,8 × ce rayon, en 25 s dans les deux sens) ; au-delà de 2,6 km, plus aucune.
Relevé, Port-Royal : 316, 1 516 et 1 916 m du rivage → le vol à 25 m du navire ;
2 316 m → rentré ; 3 016 m → rien ; retour à 816 m → revenu. L'ancienne mesure
vidait le ciel à quelques centaines de mètres du rivage d'une grande île.

## À FAIRE — fusionner les voies d'eau d'un même endroit

**Décidé, pas fait, et délibérément remis.** L'artillerie appelle `breach()` à
chaque touche, donc la liste des voies d'eau grandit sans borne : dix-neuf à
quarante-quatre par navire après une longue canonnade. Cela contredit la règle
que le projet s'était donnée pour les avaries — *une voie d'eau s'aggrave, elle
ne se multiplie pas* — et `worsenBreach()` existe justement pour doubler l'aire
du même trou plutôt que d'en ouvrir un autre.

Ce n'est PAS un problème de coût : mesuré, cent soixante voies réparties sur
huit coques ne prennent que 9 % de plus. C'est un problème de **cohérence**, et
accessoirement de vraisemblance — une coque ne se perce pas en quarante endroits
distincts, une couture cède et travaille.

Le remède tiendrait en quelques lignes : à la touche, chercher une voie déjà
ouverte dans le même compartiment et à une hauteur voisine, et l'aggraver au
lieu d'en pousser une neuve. Reste à décider ce que « voisine » veut dire — sans
doute une fraction du creux du compartiment.

## Presser le temps

**Une traversée fait une à deux heures à cinq nœuds, et le solveur tournait en
temps RÉEL.** Une bordée entre deux ports était donc deux heures de quart à
regarder la même mer. Le soleil, lui, courait déjà **soixante fois** plus vite
que la coque — une minute réelle pour une heure de ciel — et personne ne l'avait
relevé parce que c'est joli. Il y avait deux horloges ; il en faut une, et qu'on
puisse la presser.

**CE QUI LIMITE EST LE SOUS-PAS, ET RIEN D'AUTRE** — mais il a fallu une mesure
pour le découvrir, parce que ce n'était pas vrai au départ. La boucle n'avançait
`t` **qu'une fois par image**, si bien que les quatre sous-pas échantillonnaient
tous la **même** mer : à ×64 la houle restait gelée une seconde entière, et
l'erreur venait de là et non du pas d'intégration. La signature était nette —
deux réglages de compression du simple au double, au même sous-pas, donnaient
**exactement le même écart** :

| | sous-pas | écart de cap |
|---|---|---|
| ×32, SUB 4, `t` par image | 1/7,5 s | **3,5°** |
| ×64, SUB 8, `t` par image | 1/7,5 s | **3,5°** |

Une fois `t` avancé à chaque sous-pas, il ne reste qu'un paramètre. D'où la
règle, **énoncée plutôt que réglée** : *un sous-pas ne dépasse jamais 1/15 s*,
et le compte en découle (`SUB = max(4, ⌈dt·15⌉)`), ce qui redonne exactement
les quatre d'avant à ×1. Relevé sur vingt secondes contre une référence au
1/960 :

| | SUB | ms/image | écart | cap |
|---|---|---|---|---|
| ×1 | 4 | 0,86 | — | — |
| **×16** | **4** | **0,86** | 0,16 m | **0,9°** |
| ×32 | 8 | 1,79 | 0,31 m | 1,9° |
| ×64 | 16 | 3,56 | 0,31 m | 1,0° |

**×16 est donc GRATUIT** : c'est le sous-pas d'aujourd'hui, et le coût par image
ne bouge pas d'un dixième de milliseconde. Au-delà on paie linéairement, ce qui
borne la compression par le **nombre de coques** plutôt que par la physique.

**RIEN N'EXPLOSE, et cela a été cherché plutôt que supposé.** Les trois ressorts
raides du projet — échouage, abordage, amarres — étaient les suspects :

| à ×16 contre ×1 | |
|---|---|
| force 9, eau libre | 7,8° de gîte contre 8,9 |
| haut-fond à 8,7 nds | elle s'arrête toujours |
| au poste, 90 s | **0,22 m d'évitage, identique au centimètre** |

Les amarres ne bougent pas d'un centimètre à toutes les compressions, ce qui
s'explique après coup : le bassin est abrité à 0,35 m de creux, donc le ressort
n'a rien à amortir. Le seul vrai écart est que l'échouage **s'adoucit** — 1,9
nœud résiduel contre 0,9 : sous-intégré, le fond la freine moins. Raison de plus
pour rendre la main avant d'y arriver.

**ELLE SE REND TOUTE SEULE, et c'est la moitié de la fonctionnalité.** Presser le
temps n'a de sens qu'en traversée : dès qu'il se passe quelque chose, le facteur
retombe à un sans qu'on ait à y penser. Même idée que le voile de `H` — on ne
met pas le navire en panne, on lui rend l'attention au moment où elle sert.
Vérifié un par un :

| état | facteur |
|---|---|
| à quai, ×16 demandé | **réel**, « la main rendue » |
| pièce attendant sa roulée · boulet en vol | **réel** |
| le coup passé | ×16 |
| terre à 900 m · à 3 000 m | **réel** · ×16 |
| voie d'eau ouverte · réparée | **réel** · ×16 |

La rumeur s'est signalée d'elle-même pendant les essais : le facteur est retombé
sans qu'on ait rien demandé, parce qu'une nouvelle venait d'arriver. C'est
exactement le cas pour lequel la fenêtre existe — *elle laisse au commandant la
possibilité de changer de cap* — et une décision prise à seize fois la vitesse
n'en serait pas une.

**Deux choses ailleurs ont dû suivre**, et les oublier aurait fait une panne
silencieuse de chacune :

- **le plafond d'image** (`Math.min(0.05, …)`) porte sur l'image RÉELLE, et la
  compression multiplie **après** lui. Écrit dans l'autre ordre, elle aurait été
  écrêtée dès ×2 sans un mot ;
- **le rattrapage des cordages** était plafonné à cinq sous-pas de 1/120 s, soit
  1/24 s de mer par image. À ×16 une image en porte un quart de seconde : les
  bouts rompus auraient pendu au **sixième** du roulis qui les jette — du
  ralenti accroché à un navire qui n'y est pas. Le plafond suit donc l'image, et
  s'arrête à un tiers de seconde : au-delà le gréement devient approximatif
  plutôt que cher, ce qui est le bon arbitrage pour du cordage que personne
  n'étudie à soixante fois la vitesse.

**Relevé à l'horloge murale**, ce qui est la seule vérification qui compte :
2,9 secondes réelles pour **47 secondes** de simulation, soit un rapport de
**16,0**. La barre de commande porte une cinquième colonne, de la même grammaire
que les quatre autres — un intitulé, une valeur, un organe — et **`+` presse le
temps, `−` le rend**.

**Et surtout PAS une lettre**, ce qui fut la première écriture et un vrai défaut
signalé à l'usage. Le clavier est plein : `w s a d` tiennent la machine et la
barre, `q e` les écoutes, et le reste de l'alphabet est pris. Posée sur `A`,
la commande volait la **barre à bâbord** — et de la pire manière, `A` étant une
touche TENUE dont le `keydown` se répète : tenir son gouvernail faisait monter la
compression jusqu’à ×64 toute seule. Un signe plutôt qu’une initiale règle en
prime la question des dispositions — « accélérer » ne commence pas par la même
lettre dans deux langues, quand `+` et `−` se lisent sans notice. Le drapeau de
répétition est refusé, un cran par appui, comme pour la bordée.

**Ce qui n'est PAS fait.** Le soleil lit le même `dt`, donc il se comprime avec
le reste et le rapport de soixante entre les deux horloges est **inchangé** :
la compression le réduit, elle ne le corrige pas. Les deux ne coïncideraient
qu'à ×60 avec le défilement du jour à ×1 — ce qui est désormais **atteignable**
(SUB 15, 3,4 ms la coque) et ferait une journée de vingt-quatre minutes de jeu
pour un ciel enfin d'accord avec la coque. Décision non prise.

## La lueur, et ce qu'elle coûte

**CE N'EST PAS UN EFFET, C'EST UN ÉCRÊTAGE RENDU LISIBLE** (`bloom.js`). La
mesure des fenêtres allumées l'a dit crûment : les deux tiers de leurs pixels
saturaient déjà à `glow` 1, si bien que multiplier l'émissive par cinq
n'ajoutait plus rien — 76, 94, puis 107 sur 255 de gain moyen pour 1, 2,6 et 5.
Ce qui manque à une lumière écrêtée n'est pas de l'intensité, c'est la façon dont
elle **déborde** : l'œil et l'objectif étalent le trop-plein autour de la source,
et c'est ce halo qui se lit comme « ça brille » plutôt que « c'est blanc ».

Le chemin est celui du verre de la lunette, et pour la même raison — ce qui
s'applique à l'image finie ne peut pas vivre dans la scène : l'écran est copié
dans une texture, ce qui dépasse le seuil est gardé, flouté au **quart de
résolution** en deux passes séparables dont l'écart double à la seconde, puis
**ajouté** par-dessus. Elle passe avant la lunette, donc on la voit aussi dans le
verre, et la capture d'écran repasse par les deux.

**LE JOUR, ELLE N'EXISTE PAS**, et c'était la crainte posée en la demandant :
« j'ai peur pour les perfs de nuit ». La passe est sautée dès que le soleil est
au-dessus de l'horizon — pas de copie, pas de cible, et les cibles sont même
rendues à la mémoire. Le prix ne se paie qu'aux heures où il y a quelque chose à
faire briller. Relevé à 1102 × 910, lots de vingt images **entrelacés** sans et
avec pour annuler la dérive, une lecture de pixel forçant la synchronisation :

| | par image | surcoût |
|---|---|---|
| sans lueur | 4,57 ms | — |
| **quart de résolution** | 4,83 | **+0,26 ms** |
| demi-résolution | 5,79 | +0,64 |
| pleine résolution | 5,65 | +0,50 |
| de jour, passe demandée | 4,95 | **0** |

Un quart de milliseconde par image de nuit, sur un budget de 16,7 : un pour cent
et demi. C'est la **copie plein écran** qui domine, pas le flou — d'où le peu de
différence entre demi et pleine résolution, et d'où le fait que descendre encore
la résolution du flou ne rapporterait presque rien. La supprimer demanderait de
rendre toute la scène dans une cible plutôt que dans le canevas, ce qui touche la
réflexion, l'eau transparente et l'occlusion : pas pour un quart de
milliseconde.

**Piège de mesure, et il a coûté deux essais.** `gl.finish()` ne bloque pas
réellement sous ANGLE : il rendait **zéro** pour la passe, ce qui est
impossible pour une copie d'un mégapixel. Et le chronomètre GPU
(`EXT_disjoint_timer_query_webgl2`), qui mesure très bien les 1,8 ms de la scène,
rend zéro lui aussi pour ces petites passes — y compris cinquante d'affilée dans
un seul chrono. Le seul relevé honnête est le temps **mural** d'un lot d'images
terminé par un `readPixels`, qui force la synchronisation pour de bon.

Réglages dans `settings.json` : `enabled`, `strength`, `threshold` (avec un
`knee`, sans quoi le contour du seuil se dessine dans le halo), `radius`, `scale`
et `nightOnly`. Relevé au pixel, fenêtre de la Roter Löwe de nuit : le halo
touche 0,77 % de l'image, +14 en moyenne et +41 au plus fort.

## Les quatre caméras

**La vue de poursuite a été retirée**, remplacée par une vue de **proue**
plantée en avant de sa route. L'arrière est le seul relèvement d'où un carré ne
montre presque rien de lui-même : les voiles sont vues par la tranche ou se
masquent l'une l'autre, et le sillage — ce que la vue existait pour montrer —
est la partie de lui qui bouge le moins. De l'avant il présente tout son plan de
voilure, sa lame d'étrave et sa gîte, et chacun des trois répond à la barre. La
distance qui grandissait avec la vitesse est partie avec : elle cadrait le
navire différemment à chaque nœud, ce qui est une drôle d'exigence pour une
caméra.

**Proue et Fixe sont la MÊME caméra** et partagent chacune de ses lignes :
plantée dans le monde, position et cap verrouillés, braquée sur le navire une
fois puis laissée tranquille. Seule la **station** diffère — en avant sur sa
route, ou par sa hanche tribord — et cette unique différence vaut deux entrées
au menu, un navire qui vient sur vous et un navire qui s'en va n'étant pas le
même plan. Les écrire comme deux caméras aurait voulu dire tenir en accord deux
gestions de glissement, deux zooms et deux recentrages, sans le moindre gain.

Une seule finesse de station : la vue de proue se plante un peu **en dehors** de
sa ligne de route et non dessus. Dessus, le navire viendrait droit sur
l'objectif ; à côté, il s'ouvre de bout en bout du plan à mesure qu'il passe, ce
qui est tout l'intérêt de la vue.

Une caméra plantée n'avance ni ne recule : la molette **ouvre la focale**, elle
ne déplace rien. Vérifié, ancre inchangée au millimètre pendant que le navire
passait de 92 à 87 m, focale de 55° à 75°.

Et la vue **Fixe** garde son indice, ce qui est voulu : le naufrage y bascule
(`setMode(3)`), la touche `X` la replante, et rien de tout cela n'a eu à
bouger. Une caméra nommée dans le code par son numéro se remplace en gardant son
numéro. `X` replante désormais l'une comme l'autre.

**LA PASSERELLE EST DEVENUE « À BORD », ET SES POINTS DE VUE SONT DANS LA FICHE**
(`camera.decks`). Le mode 2 lisait deux nombres, `helmZFrac` et `helmHeight` —
une seule vue, écrite comme une exception. Il lit désormais une **liste** : la
passerelle, la chambre du capitaine, tout ce que le modéliste a prévu de montrer.
Chaque entrée donne l'œil dans le repère du navire (comme les lanternes : `x`/`z`
en mètres ou `xFrac`/`zFrac`, `y` au-dessus de la flottaison), où il regarde
(`yaw` 0 l'étrave, 180 la poupe ; `pitch`), sa focale et son **plan proche**.
Le bouton caméra les parcourt avant de passer à Fixe, **sans toucher à la
numérotation** : le naufrage bascule toujours sur `setMode(3)` et `X` replante
toujours les modes 0 et 3. Une fiche muette garde son ancienne passerelle,
synthétisée à partir des deux anciens champs.

**Un intérieur demande un plan proche court, et la mer doit le savoir.** À
0,7 m — le réglage du dehors — une cloison à portée de main est coupée net ; la
chambre du capitaine le met à 0,08, et il est rendu en sortant de la vue. Mais
la passe de la coque vue à travers l'eau reconstruit ses profondeurs avec
`uNear` et `uFar`, que la mer n'avait lus **qu'une fois**, au branchement : le
rig les recopie désormais à chaque changement, faute de quoi l'épaisseur d'eau
serait fausse d'un facteur huit dès qu'on entre dans la chambre. Relevé en
parcourant les vues de la Roter Löwe : passerelle à (0 ; 9,43 ; −12) regard vers
l'étrave, 55°, 0,7 m ; chambre à (0 ; 5,0 ; −9,9) regard vers la poupe, 72°,
**0,08 m, et la mer à 0,08** ; Fixe ensuite, 55° et 0,7 m rendus. Le galion
pirate, sans liste, garde sa passerelle d'avant.

La chambre est **provisoire**, placée sur le profil de pont relevé : château à
6,6–7,3 m sur les douze derniers mètres, pont principal à 3,4 m, donc l'œil à
5 m sous le château, trois mètres en avant du tableau. Le modèle n'a pas encore
d'intérieur ; le modéliste en fournira un, faces tournées vers l'intérieur, et
réglera les chiffres dans la fiche.

**Un piège de mise en route.** Proue est la vue de départ, et une caméra plantée
qui n'a jamais été plantée regarde l'origine du monde. Rien ne la plante au
démarrage : `setMode` le fait, mais le mode initial est une affectation, pas un
appel — le constructeur appelle donc `setMode(0)` pour de bon, et `update`
plante à la première image si personne ne l'a fait. Même raison pour
`setSpec` : un nouveau navire n'est pas là où était l'ancien, la station est
donc reprise.

**LA LUNETTE, sur `L`, est deux choses faites à deux endroits** (`spyglass.js`).
Le **grossissement** est celui de la caméra : le monde est rendu par le même
renderer à travers une focale cent fois plus étroite, avec toutes ses passes —
mer, reflet, ombres, coque vue à travers l'eau. Rien n'est agrandi après coup,
donc une coque à deux milles est dessinée avec le détail qu'elle a. Le **verre**
est une passe posée sur l'image finie : l'écran est recopié dans une texture
(`copyFramebufferToTexture`) et redessiné à travers l'oculaire. Le barillet ne
peut pas vivre dans la scène — il plie l'image, et seul ce qui tient l'image
peut la plier.

**La règle du grossissement est énoncée, pas réglée.** Le disque occupe 84 % de
la hauteur ; l'écran fait 55° à l'œil nu, donc le disque en couvre 46°. À ×M il
doit tenir 46°/M du monde réel : la focale verticale vaut **55°/M**, soit 0,55° à
×100. La molette est le tube, de ×10 à ×100, et le glisser vise avec une
sensibilité divisée d'autant ; la caméra en cours garde sa position mais lui cède
la visée, la focale, le glisser et la molette (`rig.held`). Une main ne tient pas
une lunette immobile : trois sinus de quelques centièmes de degré, rien à l'œil
nu, une respiration visible à ×100.

**Le barillet est dans la FORME de la courbe, pas dans sa portée.** Écrit
`p·(1 + k·r²)`, il allait chercher l'image au-delà du cadre en haut et en bas du
disque, qui revenaient noirs ; divisé par `1 + k`, le bord retombe sur le bord et
il ne reste que ce qui fait un barillet — le milieu tenu grand, le bord tassé.
Trois `k` légèrement différents par canal donnent les franges d'un objectif à
lentille unique, et le bord se ramollit, le champ étant courbe.

**Le verre abîmé est dessiné une fois, et ne passe PAS par un canvas pour finir.**
Quatre calques dans quatre canaux, chacun lu autrement : les rayures (tournantes
de polissage et franches) accrochent la lumière qui traverse et disparaissent donc
la nuit, la trace de pouce blanchit et éteint le contraste, la poussière ombre, et
la fêlure partie d'un éclat du bord brille **et décale l'image** de part et d'autre
de sa ligne — c'est ce qui la lit comme du verre cassé plutôt que comme un cheveu
sur l'objectif. Assemblés en `DataTexture` et non sur un canvas : un canvas stocke
ses pixels prémultipliés, et la fêlure vit dans l'alpha comme une donnée — partout
où le verre est intact, c'est-à-dire presque partout, les trois autres canaux
seraient revenus à zéro.

**Deux pièges de banc, qui ont fait croire à une panne.** Visée sur une île à
13 km, la lunette montrait un disque bleu uni : ce n'était pas la copie, c'était
**la brume**, réglée pour huit kilomètres — une lunette ne perce pas l'air, et
l'image sans oculaire était tout aussi unie. Puis un disque beige uni : l'œil posé
pour l'essai était **dans la colline**. Vérifier la scène sans la passe avant de
soupçonner la passe. Et la capture d'écran redessine la lunette avant de lire
(`capture.post`), sans quoi l'image sortirait sans oculaire.

**Portage Godot (2026-09-19).** Le verre est un `ColorRect` sur un calque sous
le HUD, dont le shader lit `hint_screen_texture` : l'image finie, déjà
encodée — exactement ce que la page recopie du framebuffer, si bien que les
formules passent telles quelles (seul y change de sens). Le verre abîmé est
retracé par un petit traceur C# (`SpyglassGlass`), mêmes gestes, même
générateur de Lehmer, même graine. Deux pièges : **Godot refuse un champ sous
1°** (`p_fov < 1`), et ×100 en demande 0,55° — la caméra passe en projection
FRUSTUM, hauteur 2·près·tan(champ/2) au plan proche, sans borne ; et **les flous
se coupent à l'œil** (profondeur de champ réglée pour l'œil nu, flou de
mouvement qui ferait d'un coup de visée un trait). Relevé : noir hors du disque,
0,90 au centre, 0,49 à 0,9 rayon, 0,16 au biseau.

**Les touches sont passées dans un MÉMENTO, sur `F1`.** Elles vivaient sous les
curseurs, en sept rangées de `kbd` empilées dans les colonnes de la console de
barre : seize touches ne tiennent pas là sans faire du poste de barre un
pense-bête, et la console avait cessé de montrer ce que le navire fait pour
montrer ce que le clavier peut. Elles tiennent très bien sur une page qu'on
appelle et qu'on referme.

Ce qui reste sur la console est ce qu'on **tient à la main** : machine, barre,
écoutes, et le bord en batterie. Quatre colonnes de même grammaire — un
intitulé, une valeur, un organe — les canons ayant gagné la leur et annonçant
« Bâbord » ou « Tribord » comme les trois autres annoncent leurs degrés.

Trois décisions dans ce mémento :

- **il ne met pas le navire en panne.** `pointer-events:none` et rien qui
  capture le clavier : elle continue sa route pendant qu'on lit ses commandes,
  ce qui est le comportement juste — vérifié, la machine répond pendant que la
  page est ouverte ;
- **`Échap` ne fait que le fermer.** Une touche qui referme ce qui est ouvert et
  ne fait rien d'autre est la seule qu'on n'ait jamais à apprendre ;
- **et `F1` est retenu au vol.** Laissé passer, il ouvre l'aide du *navigateur*,
  ce qui sort du jeu et ne se voit pas venir. `e.key` rend `'F1'`, que le
  passage en minuscules donne `'f1'` — donc aucune collision avec `'f'`.

**LA RANGÉE DE CHIFFRES RANGE UN PANNEAU CHACUNE** — `1` le navire, `2`
l'assiette, `3` le compas, `4` l'état de la mer, `5` la carte. `H` fait le vide
d'un coup, ce qui sert à prendre une image propre ; ceci sert à autre chose, se
débarrasser de ce dont on n'a pas besoin en gardant le reste, qui est le geste
ordinaire et non l'exception.

Une **bascule** et non une fermeture : une touche qui n'ouvre pas ce qu'elle
ferme oblige à apprendre un second geste pour défaire le premier, et il n'y en a
pas. Les deux états sont **indépendants** — `H` pose un voile par-dessus tout,
`.off` est ce que le navigateur a rangé — si bien que masquer puis rendre
l'affichage ne ressuscite pas un panneau qu'on avait fermé.

**Elles ont remplacé les touches de fonction, et c'est `F6` qui a tranché.**
Celle-ci porte le focus sur la barre d'outils du navigateur, décision prise dans
son châssis **avant** que l'événement ne descende dans le document :
`preventDefault` ne retient que ce qui ATTEINT la page, donc il n'y avait rien à
faire — la carte ne basculait pas et un bandeau s'affichait en haut. `F3` est la
recherche et `F5` le rechargement : bloquables, mais il fallait y penser à
chaque fois, et `F5` laissée passer aurait relancé la simulation et jeté la
partie. Un chiffre n'est réservé par aucun navigateur et la question entière
disparaît.

**SUR `e.code` ET NON SUR `e.key`, ce qui est tout l'intérêt ici.** Sur un
clavier **AZERTY** la rangée du haut ne donne pas de chiffres sans `Maj` : elle
donne **& é " ' (**. Un test sur `e.key === '1'` obligerait donc un utilisateur
français à presser `Maj` pour ranger un panneau, dans un jeu dont toute
l'interface est en français. `e.code` désigne la touche **physique** et vaut
`Digit1` quelle que soit la disposition. Vérifié en simulant les deux : les
valeurs AZERTY `& é " ' (` basculent les cinq panneaux, la valeur QWERTY `3`
aussi, et le pavé numérique également.

Avec un **repli sur `e.key` quand `e.code` est vide**, et ce n'est pas de la
superstition : le volet d'automatisation de ce projet envoie ses touches **sans
`code` du tout** — mesuré, `code: ""` — si bien que le premier essai marchait
en événements fabriqués et échouait sous une vraie frappe. Un clavier physique
le remplit toujours ; le repli ne sert que le cas dégradé, où il faudra `Maj`
sur AZERTY, ce qui vaut mieux que rien. Deuxième fois que ce volet dit oui là où
le vrai navigateur dit non — à ranger avec `F6` et avec le compteur d'images.

Et la frappe est **ignorée dans un champ** : la liste des navires se cherche au
clavier, et lui voler ses chiffres aurait été un défaut ajouté par le remède.
Pas de `preventDefault` en revanche — un chiffre n'a aucun comportement par
défaut sur une page.

**`M` largue les amarres**, et c'est annoncé sur la console de barre — une
manœuvre sans touche affichée n'existe pas. `G` tire du bord **en batterie**,
`⇧G` de l'autre. Le bord
est choisi dans le panneau de barre et non deviné : la touche seule savait le
dire, ce qui est un moyen mnémotechnique et non une commande — rien à l'écran ne
disait de quel côté on allait tirer, et il fallait s'en souvenir au moment où
l'on a le moins de temps pour ça. Le bord est donc un **état qu'on voit**, comme
la barre et les écoutes. Et `⇧G` sert « l'autre bord » plutôt que « bâbord » :
le geste garde son sens quel que soit le bord armé, et l'on peut lâcher une
bordée du côté opposé sans changer d'ordre.

**`H` fait le vide.** Tout ce qui n'est pas la mer disparaît, message de naufrage
et mémento compris : le but est une image propre, et une demi-interface est pire que
l'interface entière. Les commandes continuent de répondre — on masque les
cadrans, on ne met pas le navire en panne. `F` cache le seul plan d'arrimage,
qui ne sert qu'à quai.

## Une fonte portée dans la page

**Le menu de `F1` prend une anglaise** — Estonia, pour son titre et ses sept
intitulés de section. Le reste n'y touche pas : ce qu'il y a sous les intitulés
reste en chasse fixe, parce qu'on cherche une touche **du regard** et qu'une
cursive ne se balaie pas. La fonte sert de titre, jamais d'étiquette.

**UNE ANGLAISE NE SE CHASSE PAS ET NE SE CAPITALISE PAS**, et c'est le seul
piège de mise en page. Ses lettres se **lient** : l'interlettrage qui fait
respirer une étiquette en petites capitales coupe ici chaque liaison, et les
capitales d'une cursive ne se lient à rien. Les deux réglages qui allaient très
bien à Rajdhani (`letter-spacing:.18em`, `text-transform:uppercase`) sont donc
exactement ceux qu'il faut retirer. Elle demande en prime du corps — à 20 px une
signature n'est qu'un gribouillis — d'où 42 px pour le titre et 22 pour les
sections.

**EMBARQUER N'ÉTAIT PAS OBLIGATOIRE, et l'avoir cru était une erreur qu'il vaut
mieux consigner.** J'ai d'abord écrit qu'un `<link>` vers `fonts.googleapis.com`
aurait cassé chez un hébergeur ordinaire. C'est faux, et la page le prouve
elle-même : `naval-sim.html` en porte un depuis toujours pour **Rajdhani et IBM
Plex Mono**. Le garde-fou du build ne refuse que les références **locales** ;
les distantes sont délibérément laissées tranquilles, comme jsdelivr pour
`GLTFLoader`. Le contraire aurait tenu en une ligne.

Ce qui défend le choix est autre chose : c'est une fonte de **titre**. Une fonte
de titre qui arrive en retard se voit sauter sous les yeux, et une qui n'arrive
pas laisse une cursive système que personne n'a choisie, sur le seul élément
dessiné pour elle — là où du texte courant en chasse fixe survit très bien à son
remplaçant. Quatre-vingts kilo-octets une fois, et la question ne se repose
jamais. **Le prix est une incohérence assumée** : deux fontes distantes, une
portée, jusqu'au jour où l'on décidera si ce projet dépend du réseau ou non.

**Le build sait désormais porter ce qu'une FEUILLE DE STYLE demande.** C'est le
chemin de la voile peinte, appliqué au CSS : `inlineCssUrls` réécrit chaque
`url()` local en `data:` URI, résolu contre le dossier **de la feuille** — ce
que `url()` veut dire — et le garde-fou de fin refuse maintenant aussi un
`url()` local survivant. Un `<script src>` manquant se voit tout de suite ; une
fonte manquante, non : la page se charge sans rien dire et le titre retombe sur
autre chose.

**Le sous-ensemble LATIN seul**, 80 Ko. Il porte U+0000–00FF, donc tous les
accents français, plus œ, les guillemets et l'apostrophe typographique. Les
sous-ensembles *latin-ext* et *vietnamien* que Google sert à côté ne
serviraient rien ici et coûteraient le double. Vérifié sur le fichier construit :
les octets embarqués sont **identiques** à ceux du disque, et aucun chemin local
ne survit.

Estonia est sous **SIL Open Font License 1.1** (Copyright 2010-2021 The Estonia
Project Authors). Le texte complet est dans `css/fonts/OFL.txt` et **doit
voyager avec les octets** — c'est la condition de la licence, et embarquer une
fonte sans sa licence est la manière discrète de ne pas la respecter.

## Le rechargement, pièce par pièce

Une pièce qui a tiré est hors de service le temps qu'on l'écouvillonne, la
charge, la bourre et la remette en batterie. Chaque canon porte son propre
`g.readyAt` (horloge `guns.clock`, avancée par `guns.update`), tiré au hasard
dans `Naval.GUNNERY.reload` — `settings.json → gunnery.reload`, **[30, 60] s**.

- **Réalisme** : une pièce lourde demandait plutôt 1 min 30 à 2 min à un
  équipage de marine entraîné, 3 à 5 min à un marchand. 30–60 s est un
  compromis de jeu, choisi par Arnaud.
- **La bordée** ne fait parler que les pièces chargées, étalées de quelques
  dixièmes (0,08–0,30 s, et une sur cinq environ qui traîne de 0,15–0,5 s de
  plus) : six pièces ≈ 1,2 s. Chacune repart en rechargement à partir de
  *son* coup.
- **Coup par coup** : le curseur saute les pièces qui rechargent. Il vit
  désormais sur le tableau de bouches du navire (`muzzles._next`) et plus dans
  `Guns` : un seul curseur par bord faisait avancer les pièces de tous les
  navires ensemble.
- **Rien de chargé** : pas de poudre dépensée, message « Pièces en
  rechargement · tribord — la première dans 33 s ».
- **Console** : « Tribord · 3 prêtes sur 6 » tant que tout n'est pas chargé,
  relue toutes les 0,5 s.
- **Les autres navires** attendent que 60 % de leur bord soit prêt avant de
  lâcher leur bordée ; le vieux délai 20–36 s n'est plus qu'une pause de 2–5 s.
- **Radoub** (`restoreMasts`) : les pièces repartent chargées.

Mesuré (galion pirate, horloge avancée à la main) : bordée de 6 à 0 / 0,20 /
0,43 / 0,65 / 0,77 / 1,16 s ; rechargements 33–52 s ; seconde bordée
immédiate refusée sans dépense de poudre ; +66 s : 5 pièces sur 6 prêtes.

## L'équipage : ce que coûterait de le montrer

Mesure du 2026-09-17, avant toute implémentation. Galion pirate, caméra à
47 m, 838×698 px ×1,25, ombres actives, boucle gelée, lots de 10 images
entrelacés (réflexion + `stage.render()` + bloom, terminés par `readPixels`),
médianes sur 8 tours. Marin factice : cylindre de 1 500 triangles.

| configuration | ms / image |
|---|---|
| aucun marin | 9,0 – 9,3 |
| 30 marins à squelette (20 os, `AnimationMixer`) | 12,8 – 14,1 |
| les mêmes, aussi sur la couche du navire (SSAO, coque sous l'eau) | 15,4 |
| les mêmes, sans projeter d'ombre | 13,1 |
| 180 marins à squelette | 26,7 (31,3 sur la couche du navire) |
| 30 marins instanciés, balancés dans le vertex shader (une texture lue) | 10,8 |
| 180 marins instanciés | 10,0 |

- Le **calcul** des squelettes n'y est pour rien : 0,085 ms pour 30 marins
  (mixer + matrices + os).
- Le coût est **par objet et par passe** : un `SkinnedMesh` est un appel de
  dessin plus l'envoi de sa texture d'os, à chaque passe qui le voit (miroir,
  principale…). ≈ 0,13 ms par marin et par image ; l'ombre ne change presque
  rien.
- **Instancié**, le coût ne dépend plus du nombre : environ 1 ms, à 30 comme à
  180 (dans le bruit de mesure).
- Donc, si l'équipage se voit un jour : animations précalculées dans une
  texture (VAT), un `InstancedMesh` par silhouette, hors de `SHIP_LAYER`. Les
  squelettes sont réservés à quelques personnages proches, s'il en faut.
- La gestion de l'équipage sans rien afficher (effectif, postes, blessés) est
  de la donnée par navire : coût négligeable.

## Quatre hommes sur le pont

Première présence humaine à bord (`js/crew.js`), en suivant la mesure
précédente : pas de squelette, un `InstancedMesh` par navire.

- **Silhouette dessinée** (444 triangles) : jambes et culotte large de
  toile, chemise, bras le long du corps, tête, bonnet de Monmouth. Couleurs
  passées de la fin du XVIIe ; la chemise varie par homme (attribut
  d'instance `aShirt`), la taille de ±6 %.
- **Animation au vertex shader**, partie du corps lue sur l'attribut
  `aPart` : respiration (≈ 4 s, torse et épaules), report du poids d'un pied
  sur l'autre (≈ 10 s, hanche dehors et épaules en contre), tête qui se tourne
  de temps en temps (±31°). Phase propre à chaque homme ; horloge commune
  `Naval.CREW_U.uCrewT` (du temps, pas l'état d'un navire), repliée à 3600 s
  pour la précision. L'ombre est celle de la silhouette immobile : quelques
  centimètres, invisibles.
- **Placement** (`_crewSpots`) : tirage de places sur le pont, par rayons vers
  le bas sur les maillages du modèle (voiles exclues). Une place vaut si la
  surface regarde en haut, n'est pas au-dessus de la lisse de la tranche
  (sinon c'est une vergue), est plane à ±12 cm sur 30 cm autour, sans rien à
  45 cm à hauteur de genou ni de poitrine, et hors de l'axe de recul des
  pièces. Tirées dans l'ordre, les quatre de la goélette étaient toutes sur
  l'avant : on garde maintenant douze candidates et on les prend **les plus
  éloignées** les unes des autres. Une fiche peut aussi les placer (`crew`,
  voir `ships/README.md`).
- **Coût tenu** : hors de `SHIP_LAYER` (ni SSAO ni coque sous l'eau), pas
  d'occlusion ni de cicatrices sur eux (`userData.crew`), un matériau par
  navire pour que la neige reste la sienne ; masqués au-delà de 350 m, dans
  la brume et sur un navire coulé.
- Vérifié sur les huit fiches : 4 hommes sur chacune des six de plus de 12 m,
  aucun sur la chaloupe ni la bouée ; programme compilé sans diagnostic. Le
  rendu est à juger à l'œil.
- **Ils tiennent debout** (`setCrew(camPos, aboard, body, dt)`, trois
  uniformes par navire) :
  - *d'aplomb* : la verticale vraie ramenée dans le repère du navire, prise à
    `upright` = 68 % (un marin encaisse le reste dans les chevilles et les
    genoux), ±15 % selon l'homme, suivie avec `lag` = 0,35 s de retard ; le
    corps tourne autour des pieds, dans le repère propre de chaque homme
    (`transpose(mat3(instanceMatrix))`) ;
  - *pieds écartés, genoux pliés* selon le pic de gîte retenu (décroissance
    8 s), de `stanceFrom` 3° à `stanceFull` 14° : ils restent campés entre
    deux coups de roulis ;
  - *bras écartés* (30 à 46°) selon la vitesse de roulis et de tangage, de
    `braceFrom` 6°/s à `braceFull` 18°/s, sortis en 0,25 s, rentrés en 1,2 s.
- Mesuré à pas fixe (gîte imposée 20°, roulis 15°/s) : redressement 10,2° à
  0,5 s, 13,6° à 2 s (= 0,68 × 20) ; bras 0,72 à 0,5 s puis 0,83 ; appui 0,49 à
  1 s, 0,86 à 3 s ; 3 s de calme : bras 0,07, appui encore 0,98.
- **En .glb** : `tools/sailor-glb.js` écrit la silhouette dans
  `creatures/sailor.glb` (quatre maillages nommés `jambes`, `corps`, `bras`,
  `tete`), que `settings.json → crew.glb` fait charger et que le build embarque
  (32 Ko). `Naval.loadCrewModel` fusionne les maillages en une géométrie, un
  groupe par matière, la partie du corps lue sur le nom de l'objet ou d'un
  parent ; les navires déjà à flot sont rhabillés (`Naval.crewShips`), leurs
  matériaux repassés dans la neige et la brume sur place. Vérifié : 1 332
  sommets chargés, parties 0 à 3, programme compilé. Non essayé : un modèle à
  plusieurs matières.
- Limites : la normale d'éclairage et l'ombre restent celles de l'homme non
  redressé (la normale est calculée avant `begin_vertex` dans three) ; ils ne
  réagissent ni aux tirs ni au kraken.

### Retirés (2026-09-18)

Arnaud les retire tels qu'ils sont : il préfère plus tard **un** marin, tiré
au hasard, qui effectue une manœuvre, plutôt que plusieurs hommes immobiles.
`crew.enabled` passe à `false` (réglages et défaut de `crew.js`, puisque les
navires se construisent parfois avant la lecture des réglages) ; le `.glb`
n'est plus chargé ni embarqué. Le code reste : placement sur le pont par
rayons, instanciation, lecture du `.glb` par noms, tenue à la gîte.

## Les quêtes

Premier pas vers les scénarios : `js/quests.js`, un fichier JSON par quête
dans `quests/` (format : `quests/README.md`), une quête d'exemple
(« La lettre du gouverneur », quatre étapes).

- **Le monde reste ouvert** : une quête se choisit dans le panneau des
  instruments (« Mode libre » par défaut) et ne fait que désigner des lieux.
- **Les lieux se rapportent aux îles** : `Naval.MAP_SCALE` (0,70) déplace les
  îles, donc un lieu écrit en mètres serait laissé en pleine mer. Formes :
  port d'une île (tête du ponton, `port.hx/hz`), relèvement et distance
  **depuis la côte** (`_shore` dans ce relèvement, pour que « deux milles au
  large » le reste quelle que soit la taille de l'île), latitude et longitude
  (inverse de `Naval.Geo.fix`), mètres du monde. Relèvement vrai, **est = −x**.
- **Objectifs** : `reach` (entrer dans le cercle), `leave` (en sortir : ajouté après coup, voir plus bas), `stop` (y rester
  sous `maxSpeed` nœuds pendant `hold` s, remis à zéro dès qu'on bouge),
  `dock` (amarré ou mouillé dans le cercle : `physics.moorings`, où l'ancre
  est une amarre de plus).
- **Affichage** : ligne dorée sous le temps (`#questLine`, dans la colonne de
  droite, qui reste quand H masque le reste) avec distance et cap ; anneau doré
  sur la carte (`chart.places` accepte maintenant une `color`) ; message au
  centre (`#questNote`, page de journal, titre en Estonia), en file, affiché
  4 s plus 55 ms par lettre, un clic pour passer.
- **Progression** gardée en `localStorage` (`navalsim.quests` : quête, étape,
  quêtes finies marquées ✓ au menu), reprise au chargement.
- **Build** : `Naval.QUESTS_DATA` embarqué, `quests/index.json` écrit pour
  l'hébergement statique ; le serveur de dev liste `quests/` en direct comme
  `ships/` (effectif à son prochain redémarrage).
- **Piège** : la reprise lancée au chargement écrivait la ligne d'objectif
  avant la déclaration de `physics` (« Cannot access 'physics' before
  initialization », dans une promesse, donc sans bruit) : les quêtes se
  chargent maintenant juste avant `Naval.app`.
- Vérifié : lieux calculés (Tortue + 2 M à l'ouest → x = −131, du côté +x) ;
  déroulé complet par `allerQuete()` ; amarrage refusé tant qu'on n'est pas
  amarré, panne remise à zéro quand on avance, 9,5 s tenues ne suffisent pas
  pour 10 ; dix messages dans l'ordre ; ✓ au menu ; reprise à l'étape 2 après
  rechargement (« Accoster au Carénage — 7,9 M au 048° »).
- **« Quitter la rade » ne se validait pas** (signalé à l'usage) : l'étape visait
  un point à un mille au large dans le relèvement 290° **depuis le centre** de
  Port-Royal, soit de l'autre côté de l'île, à 3,8 M du ponton. Quitter une
  rade n'est pas rallier un point : nouvel objectif `leave` (sortir d'un
  cercle, 1 M par défaut), ligne « 0,4 M sur 1,0 M ». Les jours, eux,
  défilaient bien (23 h 58 → 9 octobre, vérifié).

## Le bassin creusé

Signalé : la frégate de 2000 tonneaux, passée à 70 m avec le modèle
`laCouronne17e.glb`, finissait échouée à quai. Mesuré à son poste :
5,6 m de tirant d'eau, et sous elle 17,9 / 13,1 / 9,1 / 5,8 / **3,2 m** de
l'étrave à la poupe — le fond du bassin suivait la pente du plateau
(70 m × (distance/240)²), neuf mètres à la tête du ponton, trois à cinquante
mètres de la plage. La marche vers le musoir ne pouvait rien : la poupe d'un
navire de 70 m le long d'un ponton de 86 m est forcément près du rivage.

- `world._dredge`, lu par `heightAt` (donc par les sondes, les pieux du
  ponton et le maillage du fond) : **dans l'anneau du môle**, le fond est tenu
  à `harbourDepth` = 11 m, rejoint depuis le rivage par un quai de
  `quayRamp` = 15 m (smoothstep). Seul ce qui est déjà sous l'eau est creusé.
- Réalisme : Port-Royal de la Jamaïque avait justement de l'eau profonde
  près du bord ; onze mètres reçoivent un deux-mille-tonneaux chargé (6 à 8 m).
- Après : 14,4 / 11,0 / 11,0 / 11,0 / 11,0 m ; 30 s de solveur à pas fixe,
  mer modérée, amarrée : aucun échouage.

## La lanterne de la chambre

La bougie de la chambre du capitaine (Roter Löwe) passe dans une lanterne
pendue au plafond, `cabineLantern` dans le `.glb`, qui danse avec le navire.

- Champ `hang` d'un feu : l'objet nommé est détaché de sa place et accroché
  à un pivot au **haut de sa boîte** (le crochet) ; la flamme et sa lampe vont
  au milieu de la boîte. Ici 37 × 61 × 36 cm, crochet à 5,04 m, flamme à
  4,73 m, soit un pendule de 0,31 m (période ≈ 1,1 s).
- `swingLanterns(body, dt)` : pendule amorti (ζ = 0,12) dans le repère du
  navire, deux axes (roulis → travers, tangage → long), dont le repos est la
  **verticale vraie** : quand elle gîte, la lanterne reste d'aplomb avec un
  temps de retard et dépasse. Pas de 8 ms ; au-delà de 0,25 s d'image
  (temps pressé), elle pend simplement d'aplomb. Mesuré sur une gîte
  brusque de 15° : −14,8, −25,0, −13,6, −8,5, −16,9… autour de −15.
- Le nom est celui que three donne à l'objet : un nœud sans nom prend celui
  de son **maillage** — `cabineLantern` n'était pas un nom de nœud dans le
  fichier, et une recherche sur les seuls nœuds ne le trouvait pas.
- **Piège** : reconstruire les feux (`_buildLantern`) retirait le pivot, donc
  la lanterne du modèle avec lui ; elle est maintenant rendue à son parent
  d'origine (`attach`, rotation remise à zéro) avant que le pivot parte.
- Une bougie enfermée brûle comme une lanterne (scintillement plus calme que
  la mèche nue).

### Un vrai pendule, et son ombre

- **Pendule vrai** : un poids au bout d'une ligne de longueur fixe, intégré
  dans le monde (pas de 8 ms, contrainte de longueur par projection),
  entraîné par la gravité **moins l'accélération du crochet**. La vitesse du
  crochet vient du corps (v + ω × r), pas d'une différence de positions, sinon
  chaque glissement de l'origine flottante serait une secousse ; son
  accélération est lissée (40 ms) contre le grain des sous-pas. Amortissement
  ζ = 0,05. Vérifié sur un roulis régulier de ±10° en 8 s : la lanterne penche
  jusqu'à 12,9° par rapport au pont mais 2,9° seulement par rapport à la
  verticale — le crochet, 4,5 m au-dessus du centre de roulis, est jeté de
  côté de 0,49 m/s² au bout du roulis, soit atan(0,49/9,81) = 2,8°. Sur une mer
  irrégulière les à-coups la lancent davantage.
- **La corde** : un rayon vers le haut depuis le sommet de la lanterne trouve
  le plafond (2 m au plus) ; s'il reste plus de 3 cm, une ligne de chanvre
  goudronné est dessinée du crochet à l'anneau, et le pendule s'allonge
  d'autant. Arnaud peut donc retirer la corde modélisée.
- **L'ombre** : la lampe de la lanterne pendue projette des ombres (carte
  cubique 256², portée 4 m, `Naval.LANTERN_SHADOW`). `castShadow` est posé une
  fois, à l'arrivée du modèle ; ensuite seul `shadow.autoUpdate` bascule
  (`lanternShadows`) : vrai si la caméra est à moins de 4 m et la lampe
  allumée, faux sinon — la carte reste figée, rien ne se recompile.
- **Mesuré** (Roter Löwe, 22 h, 838×698 ×1,25, lots de 10 images entrelacés,
  médianes) : dans la chambre, ombre recalculée 10,07 ms contre 8,28 figée,
  soit **+1,8 ms**, 326 appels de dessin de plus (six faces) ; au large, ombre
  figée 10,98 contre 10,88 sans ombre : le coût permanent est dans le bruit.
  Premier passage plus bruité : chambre 10,74 recalculée, 7,59 figée, 8,22
  sans ombre.
- **Vu à l'usage (capture d'Arnaud) : une chambre noire et quatre taches au
  plafond.** La carte d'ombre ne connaît pas le verre : les vitres et le métal
  de la lanterne enfermaient la flamme, qui ne sortait que par quatre fentes du
  chapeau. Les pièces de la lanterne (et la corde) ne projettent plus d'ombre
  (`userData.noCast`, respecté par `enableLighting` qui remettait `castShadow`
  partout) ; tout le reste de la chambre en projette. Et la flamme était au
  milieu de la boîte, qui comprend la tige du haut, donc sous le chapeau : un
  rayon vers le bas trouve le fond de la lanterne, la mèche est 16 cm
  au-dessus (bougie de 14 cm). Relevé : fond 4,07 m, flamme 4,23, crochet 4,51.
- Les 326 appels viennent des objets dont la sphère englobante touche les
  4 m (coque, mâts, voiles) ; des couches les réduiraient, si la chambre
  devenait un lieu où l'on reste.

## La vraie mer des Caraïbes

Le 2026-09-18, l'archipel inventé (quatre îles en disques déformés,
`ARCHIPELAGO` + `_shore`) cède la place à la géographie réelle autour de la
Jamaïque. Décisions d'Arnaud : **échelle réduite** (formes réelles, distances
÷ 10), **Cuba au nord et la vraie côte de Colombie–Venezuela au sud** (il
avait dit « le continent au nord » ; au nord de la Jamaïque il y a Cuba, ce
qu'il a accepté), édition par **carte de hauteurs peinte + modèles .glb**.
Et le jeu démarre sans HUD : date, temps et carte seulement (`hud.startHidden`).

- **Une seule définition** : `world/caraibes.json` (cadre en lat/lon, échelle
  0,1, hauteurs × 0,25, codage du gris, ports, modèles) et
  `world/caraibes-relief.png`, 3104 × 2912 px, **45 m de jeu par pixel**,
  674 Ko. Tout le reste lit `heightAt`, `shoreDistance`, `nearestShore`.
- **Le gris** : 128 = rivage ; hauteur = 1500 × (écart/127)², profondeur =
  −400 × (écart/128)². Le carré donne des nuances fines près du rivage
  (129 = 0,1 m, 132 = 1,5 m) : c'est là que se joue l'échouage, et c'est
  peignable à la main.
- **`tools/region-heightmap.js`** : côtes Natural Earth 1:10 M (domaine
  public, téléchargées avec l'accord d'Arnaud, découpées à la zone :
  94 contours, 80 Ko dans `world/sources/`), remplissage pair-impair par
  lignes, distance exacte au rivage (Felzenszwalb) des deux côtés, puis :
  plaine côtière, collines au bruit, **chaînes** de `reliefs.json` (Blue
  Mountains, Sierra Maestra, massifs d'Hispaniola, Sierra Nevada de Santa
  Marta…) en gaussiennes le long de leur crête. Fonds : **12 m à 40 m du
  rivage**, puis plateau jusqu'à 70 m à 400 m, puis le large vers 400 m.
  Les fonds ne sont pas réduits : réduits, le port de Kingston (150 à 300 m
  de large en jeu) n'aurait eu que 7 m au milieu. 2 s de calcul.
- **Hauteurs × 0,25 et non × 0,1** : réduire seulement les distances aurait
  posé les 2 256 m des Blue Mountains sur 2 km de jeu, soit des pentes de 45°.
  Mesuré : Blue Mountain Peak 595 m, Sierra Nevada 1 473 m.
- **Les Palisadoes** (la langue de sable qui ferme le port de Kingston, un à
  deux pixels) sortaient à −0,1 m : l'interpolation noie ce qui est plus fin
  qu'un pixel. Tracées en « bande » d'au moins 1,5 pixel (`strips`), et la
  terre ne descend jamais sous 2,5 m : 2,4 à 2,8 m. Kingston montait à 246 m
  (crête des Blue Mountains trop à l'ouest) : recalée, 61 m.
- **Les ports** : une ville en lat/lon et le relèvement du quai ; le rivage
  est cherché dans ce relèvement, le ponton avance jusqu'à 9 m d'eau
  (150 m au plus), et un **bassin** autour est creusé à 11 m (`_dredge` :
  11 m dès qu'il y a un mètre d'eau, un mur de quai). Pas de môle par défaut :
  Kingston est un port naturel. Les huit ports se sont placés (ponton de
  34 à 100 m). La forme `isles` est gardée (clé, nom, x, z, `port`), donc
  marché, rumeurs, rencontres et départ n'ont pas changé.
- **`Naval.Geo`** prend l'échelle : `fix` et son inverse `toXZ` parlent en
  vraies latitudes et longitudes ; l'origine du monde est Port-Royal, et le
  soleil prend 17,9° N.
- **Relief affiché en carreaux** de 1 440 m (`land.js`) : à la résolution
  de l'image jusqu'à 4,5 km, au quart jusqu'à 14 km, une jupe de 30 m sous
  chaque bord contre les fentes entre niveaux, seuls les carreaux où il y a
  terre ou haut-fond. Mesuré au port : 75 carreaux affichés, **1,25 ms** par
  image (4,27 contre 3,02 sans relief), 0,7 ms pour bâtir un carreau,
  0,15 µs par `heightAt`. Premier passage « impatient » pour ne pas partir
  d'un port dont la rive d'en face manque encore.
- **Carte marine** : une image de la région tirée du relief (terre ombrée,
  hauts-fonds pâles), découpée à la vue ; les ports par leur nom.
- **Distance au rivage** : champ signé calculé au chargement au quart de la
  résolution (180 m) ; les mouettes perchent au point de rivage le plus proche
  (`nearestShore`, descente du champ), la cargaison échouée cherche la bande
  d'un mètre d'eau le long d'un relèvement depuis un port (pas de 2 m : la
  bande fait quelques mètres).
- **Modèles posés** (`assets`) : lat/lon, cap, échelle, posés sur le relief ;
  essai avec un modèle à Port-Royal : position exacte, hauteur 2,3 m, brume.
- Vérifié : départ amarré à Port-Royal (17°56,7' N 076°49,9' O) sur 11 m,
  sans échouage ; quête réécrite (Petit-Goâve à 22,7 M, naufrage dans le
  Passage du Vent par 212 m) ; Cimetière des Galions toujours en eau
  profonde ; aucune erreur.
- **Pas encore** : l'abri naturel des baies (la houle entre dans Kingston
  comme au large ; seuls les môles abritent) ; une résolution plus fine
  autour des ports (45 m par pixel est grossier pour une passe) ; les villes
  et forts en .glb ; les petites îles hors des données (seules Lime Cay et
  Rackham Cay, à positions approchées).

## La mer de Godot : des arêtes et de la dentelle

Demandé (2026-09-19) : une mer plus vraie, **dans Godot seulement**, sans
quitter le rendu de film d'animation — d'après une image de tempête, puis deux
photos d'écume. Rien ne touche la géométrie : la règle des trois calculateurs
n'est pas en jeu, la physique ne voit rien.

- **Les rides sont des arêtes.** L'ancien fbm lisse donnait des bosses molles.
  Chaque octave est désormais un bruit plissé (1 − |2n − 1|, au carré), étiré
  trois fois en travers du vent, tourné de 0,38 rad d'une octave à l'autre pour
  que les trains se croisent, poussé à sa vitesse (les petites plus lentes, c ∝
  √λ). Portée étendue de 260 à 350 m.
- **Le creux sombre, la crête claire.** Parti pris de dessin : le creux perd
  jusqu'à 38 % (moins de ciel, dans l'eau comme dans le miroir), la crête mince
  laisse passer le ciel — un vert sombre même sans soleil, sous l'orage ou la
  lune. Fondu vers le neutre avec la distance.
- **L'écume en traits, sur le haut des vagues** (d'après une retouche
  d'Arnaud : des traits blancs granuleux qui ondulent sur les crêtes). Deux
  essais écartés : un réseau de Worley — « peau de crocodile », des mailles de
  même taille partout — puis un pointillé en cases (`floor`) qui sortait en
  damier de pixels et clignotait, retiré trois fois par seconde. Retenu : les
  lignes de passage par zéro d'un **bruit de Perlin** (deux octaves, espace
  gauchi, étiré en travers du vent), continues et sinueuses ; leur épaisseur
  suit « battue » — la raideur, et surtout la **hauteur** : passé un peu de
  vent, toute crête qui monte assez blanchit (moutons), pleine au sommet, en
  fils sur la frange, rien sur la face. Un grain de bulles en bruit lissé, de
  près seulement ; un voile de fils fins sur la moitié haute, par plaques.
- **L'écume de Tessendorf : le jacobien.** La raideur « steep » était une somme
  tronquée (chaque vague à part, sa moitié positive) ; les moutons à la hauteur
  une règle à la main. Remplacés par le jacobien du déplacement horizontal,
  J = (1 + ∂Dx/∂x)(1 + ∂Dz/∂z) − (∂Dx/∂z)², écrit une fois dans
  `gerstner.gdshaderinc` pour la mer (écume instantanée) et le champ
  d'écume (qui garde ce qui a cassé, un cran plus bas, en traînées). **Mesuré
  sur la houle du noyau** (400 × 400 m, pas d'un mètre, quatre instants) : nos
  vagues sont douces — J min 0,37 à 0,52 de force 3 à 8, sous 0,7 sur 0,3 à
  3,5 % de la mer, jamais sous 0,5 ou presque ; l'ancienne règle n'écumait
  quasiment rien (0,4 % à force 8). Seuil 0,75, plein 0,25 plus bas, réglable.
- **Corrigé sur captures** (triptyques avant / 1re version / corrigé, aux
  valeurs par défaut — `--mer-defaut 1`) : le bruit plissé dessinait une trame
  de nervures, remplacé par un Perlin signé et lisse étiré en travers du vent,
  octaves décroissant plus vite qu'elles ne s'affinent ; la route de soleil a sa
  propre rugosité (0,055 + 0,08 du vent, Cox et Munk l'élargissent sans
  l'éteindre), les curseurs ne matifiant plus que le ciel renvoyé ; seuil du
  jacobien 0,85, têtes pleines dès la moitié de la rampe. **Piège** : les
  premières captures lisaient le reglages.ini du joueur (rides 2,3, rugosité
  0,22, seuil 0,49) — trois « défauts » sur quatre étaient ses réglages. Comparer
  toujours aux valeurs par défaut.
- **Panneau de mise au point** sur ⇧M, sur le côté, sans rien arrêter — lu par sa LETTRE (`Keycode`) et non par son emplacement : sur un AZERTY le M est à la place du point-virgule QWERTY, et l'emplacement « M » est la virgule ; le panneau ne s'ouvrait pas. Des touches à un coup de la démo, seule M change de place
  (`--panneau-mer 1`, `--masquer 1` pour une capture sans instruments).
- **Le sillage en traits** (d'après un dessin d'Arnaud sur une capture). Deux
  fils derrière le safran, à ±0,22 de la demi-largeur : déposés à la poupe dans
  le canal VERT du champ d'écume, qui n'est ni étalé ni mêlé à la nappe (étalé
  d'un texel par image, un fil devient une bande en une seconde) et s'efface
  plus lentement — tirés en lignes par le navire qui avance, ils suivent sa
  vraie route. Et le V de Kelvin, deux bras à 19,5° du cap (tan = 0,354),
  dessinés sur la mer dans le repère du navire : déposé image après image, un V
  qui avance se remplirait en triangle. Piège : un seuil franc sur un Perlin
  (smoothstep(−0,2 ; 0,5)) effaçait le trait sur des dizaines de mètres — la
  sonde en rouge montrait le V entier, la mer rien ; modulé entre 55 et 100 %.
  Il faut de l'erre pour les voir : `--throttle 1` sur la frégate du XVIIe.
- **Le reflet des coques** (« il y est sur le js »). La page rend le monde une
  seconde fois depuis un œil en miroir, avec un plan de coupe pour tenir le bas
  de la coque hors du reflet. Godot n'offre pas de plan de coupe pour les
  matières d'un modèle ; le reflet est donc cherché DANS L'IMAGE que la mer relit
  déjà pour la coque vue à travers l'eau : le rayon renvoyé (sur une normale
  adoucie de moitié) est suivi en espace de vue, 40 pas croissants et 5
  d'affinage, jusqu'à passer derrière une surface opaque. Un rayon renvoyé
  monte : il ne rencontre jamais le bas d'une coque. Limite : rien hors de
  l'image ; s'efface aux bords. Coût mesuré à 720p : +0,7 ms partout, **+0,3 ms**
  une fois borné au pied des navires (cinq demi-longueurs plus 10 m).
- **Le V qui suit la route.** Dessiné dans le repère du navire, il pivotait d'un
  bloc en virage pendant que les fils de poupe suivaient la route (relevé à
  l'usage). Désormais (`OceanNode.Kelvin.cs`) chaque étrave retient sa route, un
  point tous les 2 m sur 48 points ; chaque point pousse de part et d'autre une
  crête perpendiculaire à son cap d'alors, à 0,354 fois la distance parcourue
  depuis ; la mer lit les deux lignes brisées dans une petite texture
  (x, z, âge, valide) et en mesure la distance, seulement à moins de 150 m d'un
  navire. Ligne droite : le même V ; virage : des bras courbes. `--barre` tient
  la barre pour l'essai.
- **Le V n'est pas de l'écume** (Arnaud, d'après une photo aérienne). Un sillage
  de Kelvin, ce sont de petites vagues en échelon, pas des traits blancs : les
  bras ne déposent plus d'écume, ils portent des ondes DIVERGENTES dans la pente
  de la surface (crêtes à ~35° du bras, longueur d'onde ½·2πU²/g, **jamais sous
  3 m** — à la vitesse d'un galion la loi en donne 1,7, qui moiraient en
  hachures —, effacées quand un pixel dépasse 0,12 à 0,3 longueur d'onde), dans
  une bande qui s'évase (2 m + 0,22 × l'âge). Pencher la normale ne se voyait
  pas sous un ciel uniforme : elles se lisent par un OMBRAGE (face au ciel
  claire, dos sombre), comme les grandes vagues. Piège de banc : une capture à
  15 m/s venait de la touche B pressée par Arnaud pendant l'essai.
- **Le soleil se reflète dès son lever.** Le lobe était multiplié par N·L, qui ne
  vaut presque rien au ras de l'horizon : la route n'apparaissait qu'une fois le
  soleil monté. Un reflet de micro-facettes n'a pas ce facteur ; reste un test de
  face (smoothstep sur N·L) et le soleil au-dessus de l'horizon, gain 2,4 → 2,0,
  teinte du soleil (orangée à l'aube). `--vers-soleil` tourne l'œil vers lui.
- **Les stries de pente.** Passages par zéro d'un Perlin étiré le long de la plus
  grande pente des grandes vagues (normale sans les rides), sur les faces raides
  et par vent ; espacées de 4 m, larges d'une paume — une première version plus
  fine tombait sous le pixel dès vingt mètres. Curseur « Stries de pente ».
- **Réglages de mise au point** (menu, section Mer, `reglages.ini` → `[mer]`) :
  rugosité de base, rugosité ajoutée par le vent (nouveau : la mer se ternit en
  montant), flou du ciel dans l'eau (une eau rugueuse reflète un ciel flou —
  le miroir restait net, moitié de l'aspect plastique), rides, moutons, écume
  en traits.
- **Pas de texture à agrandir.** Rides et dentelle sont calculées à chaque
  pixel ; ce qui borne leur finesse est la bande passante voulue contre la
  maille, pas une résolution. Le champ d'écume persistant (1024² sur 620 m,
  0,6 m le texel) ne dit que COMBIEN d'écume ; la dentelle dessine sa forme.

Relevé, 1280×720, force 6, moitié basse de l'écran : de jour, luminance moyenne
0,298 → 0,319, écart-type 0,126 → 0,140, pixels quasi blancs 0,97 → 0,76 % ; de
nuit (soleil à −30°), moyenne 0,041 → 0,031, écart inchangé. Coût : carte
graphique 2,09 → 2,36 ms.

## Le naufrage, et ce qu'on voit sous l'eau (Godot)

Demandé (2026-09-20) : « les naufrages d'abord ».

- **L'air chassé, dans le solveur.** `Compartment` porte `Air`, `Vent`, `Over`,
  et `ShipPhysics` la poche `TrappedAir` (8 % du volume de coque, lâchée sur une
  exponentielle de 20 s tant que son point haut est noyé) : chaque m³ d'eau qui
  entre pendant que la sortie est noyée pousse un m³ d'air dehors. Le relevé du
  haut de chaque compartiment est pris AVANT les voies d'eau, comme la page, si
  bien qu'aucun chiffre du solveur ne bouge — banc de parité tenu, scénario
  « envahissement » à 1e-15.
- **L'air qui remonte** (`WreckAir`, noyau) : des gorgées taillées sur le débit,
  qui montent à la vitesse de Davies et Taylor (0,71·√(g·r)) — d'où le délai —,
  puis une gerbe basse et un bouillon dans le champ d'écume (canal r, seize par
  image). Son dernier souffle est lu sur la coque VISIBLE, pas sur celle du
  solveur.
- **Les débris** (`FlotsamNode`) : planches et tonneaux, une bouteille sur six
  naufrages (settings.json → wreck), avec sa bulle de savon (Fresnel, film mince,
  paillettes) et son repère de loin ; dérive au vent (2,5 %), s'enfonce en fin de
  vie ; repêchée à moins de 10 m et sous 1,03 m/s. La cargaison échouée attend le
  monde.
- **Le collier d'écume restait après le naufrage** (signalé à l'usage) :
  `OceanNode` envoyait « à flot = 1 » en dur ; le solveur avait déjà `Afloat`.
- **Sous l'eau** : la fenêtre de Snell de la page portée dans la mer
  (`u_submerged`, face arrière), et une passe plein écran (`UnderwaterEffect`)
  pour l'eau qui mange la lumière canal par canal et les rais de soleil intégrés
  le long du rayon de vue — remonter jusqu'à la surface DANS la direction du
  soleil et y lire des caustiques. Piège : la couleur d'eau prise au zénith
  sortait délavée ; c'est l'eau profonde de la mer qu'il faut, au tiers. Deux
  curseurs au panneau ⇧M.

- **Regarder le ciel depuis sous l'eau** (demandé). La vue d'orbite tenait l'œil
  à deux mètres au moins AU-DESSUS d'elle et son inclinaison à zéro : on ne
  pouvait jamais passer sous la quille. Le plancher ne vaut plus que par le
  dessus, l'inclinaison descend à −1,2 rad, et la fenêtre de Snell est là.
- **Les débris remontent avec de l'élan** (demandé). Ils montaient à vitesse fixe
  et se collaient à la surface ; c'est désormais un ressort amorti — la poussée
  les rappelle à leur flottaison, l'amortissement mange l'élan —, et ils jettent
  leur peu d'eau en crevant. Relevé (simulation du pas de temps) : le tonneau
  crève à 2,40 m/s, saute 55 cm, s'apaise en 5 s ; la planche 1,55 m/s, 27 cm,
  2,3 s ; la bouteille 1,74 m/s, 31 cm, 2,8 s. Le même ressort porte
  l'enfoncement de fin de vie, sa flottaison descendant.

- **Des gouttes sur l'objectif au sortir de l'eau** (demandé : « comme une
  vitre »). Une passe canvas sur l'image finie, sur le calque de l'oculaire :
  un semis de perles (une case sur deux vide, deux tailles, chacune séchant à
  son heure), quatre gouttes qui glissent en traînant, et un film qui délave et
  sèche le premier. Chaque perle est une LENTILLE — l'image est relue décalée
  par sa pente — et non une tache floue. La mouillure vaut 1 sous l'eau et sèche
  en sept secondes ; `--gouttes x` la pose pour l'essai. Premier jet trop
  gros et trop dense : un aquarium (relevé sur capture), perles réduites de
  moitié et film ramené de 0,35 à 0,16. Puis, demandé : MOINS de perles et plus
  accentuées — deux cases sur trois sèches, rayons doublés, déviation portée de
  0,030 à 0,085 (les glissantes à 0,12). Un semis fin et serré se lit comme du
  verre dépoli ; de grosses perles qui plient fort se lisent comme de l'eau.

- **La vue MI-EAU** (demandée, d'après une photo en coupe) : une quatrième vue
  (C, ou `--mi-eau 1`), l'œil posé sur la houle LUE à sa place — la ligne de
  partage reste donc au milieu quand la mer respire — et le regard à
  l'horizontale. Relevé d'un rien au-dessus (`--mi-eau-haut`, 5 cm par défaut) :
  à fleur d'eau exacte, la surface vue de l'œil même s'étale en bande sombre et
  mange les deux moitiés. Et la passe sous-marine tranche alors PAR PIXEL, au
  sens du rayon, au lieu d'être tout ou rien pour la caméra entière.

- **Les gouttes reprises** (demandé, d'après une photo de pare-brise) : formes
  et tailles AU HASARD — chaque perle a son allongement, son inclinaison et sa
  pointe par le haut, la larme d'une goutte tirée par son poids —, et plus
  grosses. Et, avant elles, une NAPPE : au sortir de l'eau l'objectif porte une
  lame d'eau qui tire l'image vers le bas, la brouille, puis s'égoutte en une
  demi-seconde en découvrant les perles. Piège : son bord festonné par un bruit
  pris par bandes (`floor`) sortait en marches d'escalier — relevé sur capture,
  remplacé par un bruit lissé. `--nappe x` la pose pour l'essai.

- **Les bulles qu'on VOIT monter** (signalé : « il n'y a pas d'air qui
  s'échappe »). Le solveur comptait l'air, WreckAir le faisait arriver en haut,
  mais entre les deux il montait en silence. Le noyau annonce donc chaque poche
  qui part (`OnSlug`), et un MultiMesh la montre : une GRAPPE qui tremble, à la
  vitesse de Davies et Taylor, si bien qu'elle crève À L'INSTANT où paraissent
  la gerbe et le bouillon — les deux bouts du même trajet. Petites au départ et
  qui GROSSISSENT (demandé, et c'est Boyle) : le rayon suit la racine cubique du
  rapport des pressions — de 12 m de fond, ×1,28 à 8 m, ×1,63 à 4 m, ×1,95 en
  surface. Deux essais écartés : des anneaux bleu néon (bord trop mince, couleur
  trop saturée), puis des bulles devenues invisibles à force d'être petites.
- **Un pavillon noyé ne danse plus** (signalé). Sous l'eau il n'y a pas de vent :
  le même calcul le fait retomber, avec un vent nul, et il perd sa longueur en
  pendant. Lu sur SA hauteur à lui — le grand pavillon de poupe touche l'eau
  bien avant les têtes de mât.
- **Gouttes encore réduites** (demandé) : les plus grosses ôtées (rayon max de
  0,39 à 0,23) et une case sur cinq seulement en porte une.

- **Des bulles blanches, sous la coque, et plus longtemps** (demandé : « des
  bulles blanches quasi opaques qui s'élèvent de dessous la coque … l'effet doit
  durer plus longtemps et surtout rester subtil »). Trois changements, dont deux
  dans le noyau : l'air emprisonné passe à 12 % du volume de carène (il était
  bien plus maigre) et se dissipe en 45 s au lieu de quelques-unes, et le dernier
  souffle ne prend plus qu'un tiers de ce qui reste — l'épave continue donc de
  souffler longtemps après avoir disparu. Côté image : `blend_mix` et non plus
  additif (une bulle CACHE ce qu'il y a derrière), blanc à 0,85 d'opacité, deux
  à sept bulles par poche seulement, et le départ descendu de 0,6 à 1,8 m SOUS le
  point de fuite — ce qui crèverait dans le contour de la coque serait de toute
  façon caché par son bordé.

- **Un pavillon noyé est PORTÉ** (demandé : « les drapeaux doivent flotter à
  faible vitesse comme si du vent les poussait par dessous »). Le premier jet le
  faisait pendre, ce qui était juste mais mort. `Stream` reçoit donc un
  `lift` de 0 (dans l'air) à 1 (dans l'eau) : l'onde ralentit — 6,5 rad/s dans
  l'air, 1,2 dans l'eau —, l'étamine cesse de retomber et se SOULÈVE d'un lent
  balancement qui ne s'arrête jamais. C'est le seul écart assumé avec la page,
  qui n'a pas d'eau ; la parité tient au bit près (`lift = 0` par défaut,
  cinq coupes à 0,0E+000).

- **Le trésor englouti** (demandé). Une pièce ne tombe pas comme une pierre :
  elle est plate, l'eau la porte par le travers, et elle descend en VOLTIGEANT à
  20–45 cm/s en basculant autour de son diamètre. Quatre corrections mesurées
  avant que ça se lise :
  1. *invisibles* — la nuée était semée sur toute la longueur ET la largeur : à
     300 pièces cela fait un point tous les deux mètres, qu'on ne voit pas. Semée
     serré vers le milieu (tirage au carré : l'or est dans la cale), et 120 à 500
     selon la coque ;
  2. *confettis* (signalé) — un panneau plat disparaît d'un coup de profil. Le
     maillage est maintenant un cylindre bas, épaisseur 14 % du diamètre, et la
     tranche est plus sombre que le champ ;
  3. *vert citron* — sous l'eau, la passe d'absorption mange le rouge la
     première : une émission couleur or ressort verte à quatre mètres. L'éclat
     part donc trop chaud (1,0 / 0,44 / 0,06) pour arriver doré à l'œil, la
     correction d'un plongeur faite à la source ;
  4. *ternes* — à cinq mètres une pièce ne fait plus trois pixels. Un socle
     d'émission plus un éclair en `pow(face, 4)` quand sa face passe à plat :
     d'une photo de trésor qui coule, on ne voit pas les pièces, on voit leurs
     éclats.
  `--tresor n` en sème sans couler, pour juger. Le maillage de repli cède la
  place à `props/ecu.glb` s'il existe (posé à plat, face vers +y, ramené au
  diamètre 1, sa texture reprise).

  Piège de mesure : quatre cadrages successifs n'ont rien montré parce que la
  nappe DESCEND — à 10 s elle s'étage de −0,4 à −4,6 m, et l'œil était dessous.
  Un compteur temporaire (nombre vivant, y extrêmes, y de la caméra) a réglé la
  question en une exécution, là où les captures ne répondaient pas.

- **Les pièces disparaissaient en passant la quille** (signalé, capture à
  l'appui : une frontière nette, or au-dessus, rien en dessous). Un matériau en
  MÉLANGE n'écrit pas sa profondeur ; la passe sous-marine, qui lit le tampon de
  profondeur pour savoir combien d'eau la lumière a traversée, prenait donc la
  distance de ce qu'il y avait DERRIÈRE la pièce — la coque, proche, ou l'eau
  lointaine. `depth_draw_always` règle tout. Le même piège guette toute chose
  transparente vue sous l'eau ; les bulles y échappent parce qu'elles ne quittent
  jamais le voisinage de la coque.

- **Le vrai reflet** (demandé : « les pièces pourraient refléter la lumière et
  créer un éclat ? »). L'éclat de départ s'allumait quand la face regardait
  l'ŒIL ; une pièce brille quand elle renvoie le SOLEIL vers l'œil. C'est le
  demi-vecteur, exposant 60 : l'éclair est bref et revient deux fois par tour,
  comme une faux dans un champ. La couleur est celle de l'heure (`u_sun_col`,
  poussée par SkyNode), et non un blanc de convention. Sous l'eau la réfraction
  couche un peu la course du soleil ; sous quarante-huit degrés l'écart ne se
  voit pas, et on garde la direction franche plutôt qu'un calcul que rien ne
  viendrait vérifier. Pas de halo : le projet ne fait luire que la nuit, et cette
  règle-là tient.

- **L'écume vue PAR EN DESSOUS** (demandé : « on ne voit que la transparence de
  l'eau »). La branche sous-marine du shader REMPLACE la couleur par la fenêtre
  de Snell, et effaçait donc l'écume calculée dix lignes plus haut : d'en bas, la
  mer n'était qu'une vitre. Trois choses à comprendre, toutes mesurées au repère
  coloré :
  1. *quelle écume* — pas la même qu'en haut. Un mouton de crête est une
     pellicule ; peint tel quel d'en dessous, tout l'horizon devenait laiteux
     (repère ROUGE : il couvrait tout). Seul se voit d'en bas ce qui a de
     l'ÉPAISSEUR — le sillage, le collier de la coque, la vieille écume —, et le
     collier, vu de trois mètres, a fallu le ramener de 1,7 à 0,95 (repère VERT :
     il emplissait le cadre) ;
  2. *quelle couleur* — pas celle d'en haut non plus. La passe sous-marine mange
     le rouge en premier : une écume peinte de sa couleur d'air ressort brune (le
     repère rouge arrivait bordeaux). On garde donc sa CLARTÉ — la luminance de
     `foam_col`, une seule définition — et on la porte sur un blanc bleuté. Du
     blanc à quelques mètres sous la surface EST cyan, c'est ce qu'on voit en
     plongée ;
  3. *jamais plus sombre que ce qu'elle couvre* : `max(c, milk)`. Des bulles
     ajoutent de la diffusion, elles n'ôtent pas de lumière.
  Et la nappe profonde est prise AVANT la dentelle (`persist_deep`) : les bulles
  qui pendent sous une écume se dispersent en descendant, le nuage y est plus
  continu que le lacis de la surface.

- **La langue de Port-Royal relevée** (signalé : « on est dans l'eau »). La
  plaine côtière de l'outil fait pourtant déjà 2,5 m partout — mais
  l'échantillonneur lit l'image **bilinéairement**, et une bande d'un pixel
  entre deux pixels de haute mer se fait moyenner jusqu'à presque rien : le sol
  mesurait **1,0 m** sous la ville, d'où des maisons qui paraissaient flotter.

  Deux nombres, tous deux le prix d'un pixel de 450 m : la bande passe à 2,3 px
  de large (au lieu de 1,5), ce qui lui garde un cœur que le flou n'atteint pas,
  et `reliefs.json` peut désormais donner une **hauteur** à une bande — 6 m
  pour les Palisadoes. Mesuré après coup : 3 m à 25 m du rivage, **6 m de 50 à
  75 m**, l'eau à 100 m. Une vraie langue de sable fait deux ou trois mètres ;
  ici il faut la dessiner plus haute pour qu'elle SE LISE comme telle, et c'est
  l'honnêteté de le dire dans ce sens-là.

  Le relief régénéré, la parité a été refaite sur la nouvelle image : elle tient.
  Port-Royal bâtit 49 maisons au lieu de 59 — la bande a changé de forme, et
  c'est le semis qui en décide.

## Le ponton et l'ancre (Godot)

**LE PONTON** — demandé pour le démarrage, et c'est la première chose qu'on voit
en prenant la barre. Porté tel quel : un tablier de niveau, des planches EN
TRAVERS (un ponton est ponté d'un bau à l'autre, les planches sont courtes et on
remplace celle qui pourrit), une passerelle d'embarquement, du fret sur le quai,
et trois bittes — deux au musoir, une à la racine, pour qu'une garde puisse
revenir vers la plage.

Ce qui le fait lire comme un ponton et non comme une planche sur l'eau, c'est
qu'il se tient sur des JAMBES dans de l'eau d'une vraie profondeur : chaque pieu
est coupé au fond qu'il touche, lu par la MÊME `HeightAt` que la quille
talonne. L'ouvrage prend donc de la jambe à mesure qu'il marche vers le large —
une rangée de poteaux égaux se lit comme une clôture. Ses cotes vivent dans
`Berth`, avec le poste d'amarrage, pour qu'ils ne puissent pas se contredire.

**L'ANCRE** — une ancre sur le fond est une AMARRE DONT LA BITTE PEUT BOUGER, et
c'est pourquoi elle n'a presque rien coûté : `Mooring` portait déjà tout, y
compris `Hold` (ce que le bout d'en face tient avant de céder) et
`Dragging`. Ce nœud-ci ne possède que ce que le solveur n'a pas à savoir : la
chute depuis le bossoir, la gerbe, la descente lente, le câble, le cabestan.

La tenue sort de l'arithmétique : huit fois le poids du fer, fondu par la TOUÉE
(`(scope − 1)/4`, borné). Mesuré au mouillage de Port-Royal : « Ancre au fond
par 11 m · 38 m de câble » — une touée de 3,5, elle tient. En soixante-dix
mètres d'eau avec deux cents de câble, elle chasserait, et c'est la vraie raison
pour laquelle on mouillait en rade.

**Le câble a demandé trois essais**, tous vus à la même capture sous-marine :
1. intégré au pas de l'image, il s'entortillait en ressort. La page l'intègre à
   PAS FIXE (1/60) avec quatorze passes de contraintes et le fond appliqué DANS
   la boucle ; repris tel quel ;
2. toujours en accordéon, parce qu'il partait TENDU et devait gagner d'un coup
   vingt-quatre mètres de mou. Il est maintenant posé dans la forme qu'il a
   vraiment — chaînette du bossoir jusqu'au toucher, puis TRAÎNE sur le fond
   jusqu'à l'organeau, qui est d'ailleurs ce qui fait la tenue ;
3. et cette pose n'avait aucun effet tant qu'on ne la refaisait pas au moment du
   toucher : la chaîne avait été tendue pendant la chute et le restait.

`M` mouille et vire au cabestan (par sa LETTRE, comme ⇧M : sur un AZERTY elle
n'est pas à la place du QWERTY). `--ancre 1` mouille d'emblée, pour juger.

**LE MÔLE**, demandé pour Port-Royal à l'essai — un drapeau dans la fiche, donc
réversible d'un caractère. Bâti des MÊMES quatre nombres que `HeightAt` lit :
centre, rayon, épaisseur et demi-angle de la passe. C'est la règle du plan de
formes appliquée à la maçonnerie — ce qui arrête la coque est ce que l'œil voit
l'arrêter. Et seulement là où il DÉPASSE du sol qui le porte : l'anneau continue
dans la colline derrière le port, où un mur de trois mètres est simplement
enterré, et l'y dessiner poserait un bandeau de pierre à flanc de coteau. Chaque
tronçon demande donc au relief ce qu'il a dessous, ce qui lui donne du même coup
ses racines sur la plage.

**ET L'ABRI, dans les TROIS calculateurs** — sans quoi le môle n'aurait été
qu'un mur dans la houle. La moitié du travail était déjà faite sans qu'on le
sache : `gerstner_sum` prenait déjà un paramètre `shelter` (mis à 1 avec le
commentaire « le havre n'est pas encore porté »), et `Ocean.Shelter` attendait
dans le noyau. Il manquait la fonction elle-même : `shelter.gdshaderinc`, le
jumeau exact de Naval.SHELTER_GLSL, inclus par le shader de la mer ET par la
passe d'écume, avec le noyau pour troisième lecteur. Un seul havre à la fois,
le plus proche, poussé à chaque image : on n'est jamais dans deux ports.

Vérifié par une capture à **force 6** : la mer moutonne dehors, le bassin est
lisse dedans, et l'écume s'arrête au mur — c'est la passe d'écume qui lit le
même abri. La parité a été refaite sur la fiche modifiée : elle tient.

Reste de ce lot : la chaloupe.

## L'ombre des coques sur l'eau, et le soleil derrière le reflet (Godot)

Demandé, retouche Photoshop à l'appui : une ombre du soleil plus visible sur
l'eau. Signalé en même temps : la route de soleil passait au travers du navire.

**LA MER NE RECEVAIT AUCUNE OMBRE** : elle est `unshaded`, donc hors des cartes
d'ombre du moteur. L'ombre est calculée dans son shader (`hull_shadow`) : on
remonte vers le soleil, et à huit hauteurs entre l'eau et les hauts de la coque
on demande à `hull_gap` — le contour de flottaison que la mer connaît déjà —
si le rayon est dedans. La coque est un prisme droit sur sa flottaison, la
pénombre s'élargit avec la hauteur, l'ombre est bornée à six fois la hauteur au
soleil rasant, s'efface la nuit et sous l'orage. Elle retire la lumière de
l'eau (`u_shadow` = 0,5, en tête du shader) et le reflet du soleil ; le miroir
du ciel reste, d'où une ombre plus franche vue d'en haut qu'à l'horizon.

Les hauts sont mesurés sur le modèle (`HullProfile.Top` : moyenne des ponts par
tranche tirée vers le plus haut) : barge 1,6 m, frégate 6,7 m, canot 0,6 m.

**LE SOLEIL DERRIÈRE LE REFLET** : le reflet de la coque remplaçait bien le ciel
dans le miroir, mais le reflet du soleil s'ajoutait après, sans masque — il
brillait au travers de l'image renversée du navire. Il est maintenant éteint à
proportion de ce que le reflet de la coque occupe.

**Puis : l'ombre sur l'écume et sur le navire.** L'écume se composait après l'ombre et restait blanche dedans : sa couleur perd maintenant 0,65 × l'ombre (plus que l'eau, qui garde le miroir du ciel). Sur les modèles, le moteur portait déjà l'ombre du soleil, mais l'ambiante du ciel (gain 3,2) éclairait l'ombre presque autant que le soleil la lumière : le gain passe à 2,0 (`SkyNode.AmbientGain`), à juger à l'œil.

**Puis : la forme de l'ombre.** Signalée étrange : une dalle trop longue aux bords en marches. La coque était un bloc de la hauteur de son château sur toute sa longueur, et huit hauteurs seulement se lisaient au soleil bas comme huit contours empilés. Les hauts sont maintenant relevés STATION PAR STATION sur le modèle (canal vert de la texture des profils) — frégate : 7,9 m au château, 4,3 m au milieu —, seize hauteurs, et une pénombre jamais plus fine que l'écart entre deux.

**Puis : l'ombre en peigne.** Des dents dans le sens du soleil : une station relevée trop basse entre deux hautes (pas de sommet au plus haut de la tranche) laissait passer un rai de soleil sur toute la longueur de l'ombre. Les hauts sont comblés entre voisines, pris au plus haut à deux stations près, puis moyennés (frégate : milieu 4,8 m au lieu de 4,3), et leur bord dans le shader est aussi large que l'écart entre deux hauteurs.

**Puis : les festons.** Au soleil bas, seize hauteurs tombaient à 3 m l'une de l'autre sur l'eau et chacune dessinait son contour de coque. Vingt-quatre hauteurs, une pénombre d'une fois et demie leur écart, et un décalage des hauteurs propre à chaque pixel (bruit à gradient entrelacé) : ce qui reste du motif devient grain.

## Le soleil par les fenêtres de la chambre (Godot)

Signalé : la lumière du soleil ne filtrait plus par les fenêtres de la cabine du capitaine. Relevé sur le Roter Löwe : quatre objets en verre, deux faisant encore ombre (`Plane_008_1`, `Plane_4`). `FindNightGlow` coupait bien l'ombre du verre, mais APRÈS le test « matière déjà vue » : seul le premier objet de chaque matière était traité, et les vitres qui partagent la matière « glass » (le modèle mis à jour en a plusieurs) bouchaient les fenêtres. L'ombre est maintenant coupée pour chaque objet qui porte du verre.

## L'ombre au couchant (Godot)

Signalé : l'ombre des coques sur l'eau s'estompait vers 18 h, soleil à 3°, quand elle devrait être encore là. Deux bornes l'effaçaient : elle s'éteignait sous 7° (smoothstep de 0,02 à 0,12 sur la hauteur du soleil — à 3°, un quart), et sa longueur était bornée à six fois la hauteur de la coque quand un soleil à 3° en donne dix-neuf. Elle ne s'éteint plus que dans les deux derniers degrés, et s'allonge jusqu'à vingt fois la hauteur ; la pénombre, qui suit l'écart entre les hauteurs échantillonnées, s'élargit d'elle-même avec la longueur.

## La lumière du soleil, à l'étude (Godot)

Demandé : étudier une lumière du soleil plus franche sur la coque, jaune et chaude, pour plus de contraste. Trois curseurs au menu (Lumière), gardés dans `reglages.ini` → `[lumiere]`, appliqués sur les valeurs de la page : **force du soleil** (×1,25 par défaut), **chaleur du soleil** (0,35 : la couleur tirée vers un jaune d'après-midi, 1,0/0,80/0,52, À LUMINANCE ÉGALE — la couleur change, pas la force ; le jour seulement, la lune garde sa lumière froide), **lumière du ciel dans l'ombre** (×0,85 : plus bas, plus de contraste). Relevé au démarrage : soleil (1,07 ; 0,94 ; 0,76) à 0,84 au lieu de 0,67, ciel 0,24 au lieu de 0,28.

Puis : la force du soleil jusqu'à ×5 (« juste mais suffisant » au maximum de 2,5), et le troisième curseur, qui « ne change pas grand-chose », devient l'**éclairage ambiant** tout entier. L'énergie ambiante seule ne touchait presque rien : l'ombre était éclairée surtout par les REFLETS du ciel et la lumière renvoyée (SSIL). Il règle maintenant aussi la passe cubemap du dôme (`u_env_gain`, qui nourrit ambiante et reflets — le ciel vu n'en change pas) et l'intensité du SSIL, de 0 à 1,5. Mesuré sur les deux tiers bas de l'image (surtout de la mer, qui n'en dépend pas) : 0,386 à 1,0, 0,357 à 0,5, 0,317 à 0.

**Le ciel fermé (signalé : par gros temps, la coque restait éclairée comme par beau temps).** Le noyau rabattait bien le soleil de 62 % en pleine tempête, mais la Force du soleil de l'étude passait par-dessus, et l'ambiante baissait AVEC l'orage (−55 %). `SkyNode.Gloom` = max(orage, ce que l'hôte pose dans `Overcast` : averse, grain, brume, couverture au-delà de 55 %). Le soleil perd `OvercastSun` (0,75) de sa force et la Force du soleil revient vers 1 ; l'ambiante gagne `OvercastSky` (1,8 : +180 %, la moitié sur les reflets du dôme) ; l'ombre des coques sur l'eau s'efface avec (`u_sunlit`). Mesuré à 45° : beau temps soleil 3,31 / ambiante 0,070 ; force 6,5 : 0,95 / 0,081 ; force 8 : 0,10 / 0,091 ; averse d'une heure : 0,56 / 0,166 (l'averse ne ferme pas le couvercle du noyau, son ambiante n'est pas rabattue d'abord). Piège de mesure : `--sun` arrête l'horloge, donc aussi les averses.

**Seul un ciel vraiment fermé agit.** Linéaire, une petite pluie qui fermait le ciel à 30 % ramenait une Force du soleil de 5 vers 3,8 et ôtait 40 % du soleil. Le ciel fermé passe donc par une marche douce (`Closed`) : rien sous 0,4 (`OvercastFrom`), tout à 0,9 (`OvercastFull`). Part du soleil gardée, Force à 5 : 1,00 à 0,3 ; 0,85 à 0,5 ; 0,29 à 0,68 ; 0,05 à 0,94.

## L'écume du rivage (Godot)

Demandé : détecter la bande de sable au contact de l'eau pour y faire, là aussi, un collier d'écume.

La mer ne connaît pas le relief, mais elle lit déjà l'image de profondeur de la scène (pour la coque vue à travers l'eau). Le point de la scène derrière chaque pixel de mer — le fond, la plage, un pilier de ponton — y est reconstitué en monde, et sa hauteur comparée à celle de la surface : sous un mètre environ d'eau, une bande d'écume, dans la dentelle de l'écume du large. C'est la terre DESSINÉE qui décide, donc l'écume suit exactement le rivage qu'on voit. La surface montant et descendant avec la houle quand le fond reste, la bande va et vient d'elle-même sur la plage ; une onde lente la pousse un peu plus. Les coques gardent leur collier (rien à moins de 2,5 m d'une flottaison) ; jusqu'à 600 m de l'œil, fondue dès 350. Vérifié à la compilation seulement : l'aspect se juge à l'œil.

## La brume de surface, la nuit (Godot)

Demandé : une météo qui n'existait pas, une brume de surface nocturne — et le serpent avec.

**UNE NAPPE, PAS UN BROUILLARD** : par nuit calme sous les tropiques, l'air qui refroidit au-dessus d'une mer restée tiède condense au ras de l'eau une couche de dix ou quinze mètres ; au-dessus, les étoiles, et la pomme des mâts qui en sort. C'est la loi de brume qui existait déjà (densité au ras de l'eau, décroissance exponentielle en hauteur) avec une couche basse et dense : à pleine brume, 0,02 par mètre et 12 m d'échelle au lieu de 0,00085 et 150. Le ciel la mêle (`Sky.Fog`), et tous ses lecteurs la voient sans une ligne de plus : la mer, les coques, la terre, les feux qui s'éteignent au loin, le masquage des coques lointaines.

Mesuré à pleine brume, œil à 2 m : **3 %** de la lumière passe à 200 m au ras de l'eau (on y voit à 150 m), **65 %** vers une tête de mât à 30 m de haut et 60 m de distance — la mâture sort de la nappe.

**LE CYCLE** (`core/SeaFog.cs`) : tirée au soir, une nuit sur trois ; elle monte à partir de 21 h, s'épaissit en une heure de jeu, tient jusqu'à une heure et demie après l'aube ; au-delà de force 4,5 le vent la déchire, deux fois plus vite. Premier seuil à 3,5 : la mer par défaut est à force 4, et la brume n'aurait jamais paru. « La brume monte sur l'eau », « La brume se lève » ; « · brume » à l'instrument de l'air.

**LE SERPENT** y vient comme sous la pluie (la brume compte pour 0,8 de pluie), et ses messages disent « dans la brume ».

⇧T : la brume tout de suite, trois heures de jeu ; `--brume 6`. Piège : la brume n'était créée qu'à sa première mise à jour, et `--brume`, lu avant, tombait dans le vide. Réglages : `settings.json` → `fog`.

## Le serpent de mer (Godot)

Choix d'Arnaud : il FRAPPE ET PLONGE (plutôt qu'enserrer ou chavirer), dans la
BRUME ET LA PLUIE, et il PEUT COULER le navire s'il n'est pas repoussé.

**LE NOYAU** (`core/SeaSerpent.cs`) : il ne vient qu'au large (1 km de côte,
40 m de fond) et sous la pluie — l'averse ou le grain, pas la neige —, trois
rencontres par heure de pluie. On l'ENTEND d'abord (un sifflement, le
grondement du kraken qui porte sur l'eau), puis ses anneaux crèvent la surface
à 180 m et tournent autour du navire, 15 à 30 s. Il fonce sous l'eau (11 m/s)
vers un point par le travers, DRESSE la tête à 7 m et frappe au sommet de sa
course : la TÊTE (voie d'eau à la flottaison, 0,10 à 0,22 m²), la QUEUE (plus
bas, 0,18 à 0,32 m², un choc plus fort), ou la MORSURE d'un mât (la blessure
des mâts : trois l'abattent). Le choc est une impulsion de 12 t partagée au
point touché, comme la baleine. Il replonge, resurgit 10 à 25 s plus tard.
Les boulets le blessent sur toute la longueur de son corps (10 points de vie) ;
touché, il plonge ; à bout, il fuit ; trente secondes sans pluie et il s'en va.

**LE CORPS SUIT LE CHEMIN DE LA TÊTE** : la tête laisse une trace, et les
quarante-huit points du corps sont pris le long d'elle à pas égal — chaque
anneau passe où la tête est passée, ce qui fait un serpent et non un tuyau. En
rôdant, une onde verticale fait sortir ses bosses ; dressé, le cou descend en
parabole sur ses douze premiers mètres. Contrôlé au labo (`-- serpent`) : 40,0 m
de corps à toute image. Premier jet du rééchantillonnage trop tortueux pour
être sûr : réécrit en simple marche le long de la trace.

**PIÈGES** : à 2,5 m de profondeur, sa charge traversait la quille d'une
frégate (tirant 2,9) — il passe sous le tirant + 2,5 m à moins de 30 m. Appelé
par la ligne de commande, il cherchait le navire AVANT que la traversée ne l'ait
posé à l'atterrage (« pas assez d'eau ») : l'appel est différé d'une seconde.
Et l'averse qu'il déclenche doit durer : `StartShower` compte en minutes de JEU,
vingt minutes n'en faisaient que dix secondes, la pluie cessait et il fuyait
sans frapper — quatre heures de jeu. Une « erreur fatale du CLR » en fin
d'essai était le processus tué par la limite de temps : sorti proprement
(`--quit-after`), code 0.

**LE RENDU** (`SerpentNode`) : un tube de 48 anneaux × 14 côtés refait à chaque
image, crête sur le dos, dos vert sombre et ventre pâle, peau mouillée
(rugosité 0,32) ; la tête en fuseau aplati, comme un congre, deux yeux vert
jaune qui luisent à peine ; la brume des navires par-dessus. Vérifié dans
Godot à l'atterrage de la Tortue : entendu, vu, coup de tête (+0,30 m/s,
0,13 m²), coup de queue (+0,40 m/s, 0,21 m²), fuite. ⇧J : une averse et le
serpent ; `--serpent 1`. Réglages : `settings.json` → `serpent`.

## L'écran de titre : le navire au large, et quatre entrées (Godot)

Demandé : le navire par beau temps au milieu de l'océan, la profondeur de champ mise au point à 2 m de la caméra — le navire flou dans le lointain —, et à la place de « Jouer » : Jeu libre, Histoire, Missions, Options.

Le fond était une vue sous la quille avec l'or qui tombait de la cale. Maintenant : le navire posé à l'ATTERRAGE de la région le plus proche du port de départ (7 km de côte, 363 m de fond pour Port-Royal), force 3, ciel dégagé, dix heures, faisant route — sous voiles bordées, ou à la machine pour une coque sans toile (le chaland, navire par défaut, restait planté à 0,0 m/s) ; l'œil au ras de l'eau à 95 m, un tour en cinq minutes ; flou au-delà de 2 m sur 6 de transition. La mise au point du joueur est reprise à l'entrée dans le jeu. Pendant le titre, ni route au carnet ni port touché : l'affiche ne doit pas percer le voile.

Le navire de l'affiche est toujours le Roter Löwe (`frigate17e`, demandé), mis à l'eau à l'ouverture du titre ; celui du joueur lui est rendu à l'entrée dans le jeu.

**Jeu libre** : le port de départ, sans quête (le navire y est remis droit et sans erre). **Histoire** : le premier chapitre non fini (`kind: story`, ordre de `chapter` ; le tour de la Jamaïque est le chapitre 1). **Missions** : la liste des autres, ✓ pour les finies, et Retour (ou Échap). Éprouvé : Jeu libre → à quai, aucune quête, mise au point rendue ; Histoire → à quai, « Le tour de la Jamaïque ».

## Chavirée : le bandeau, et R qui redresse (Godot)

Signalé : le navire se retrouve parfois la tête en bas, le jeu ne le voit pas, et R ne le redresse pas. Le bandeau n'attendait qu'un NAUFRAGE, et R ne remettait droite qu'une épave : une coque retournée flotte sur l'air de ses fonds, ni coulée ni droite. Chavirée maintenant quand le haut du navire passe sous 0,17 (couchée au-delà de 80°) pendant trois secondes, pour ne pas crier au chavirage sur un coup de roulis : bandeau « Votre navire a chaviré ! — R pour le redresser ». R redresse À SON CAP (gîte et assiette effacées, lacet gardé ; elle était reposée cap au nord, d'un bloc), pompe, replante la mâture. Éprouvé : `--chavirer 1` (la coque retournée une seconde après la mise à quai, qui la redresserait) → bandeau ; R → haut à 1,00, bandeau parti.

Et le N de la boussole passe en blanc, comme les autres (demandé).

## La boussole, et les notes à taille fixe (Godot)

Demandé : la boussole en bas à droite, avec la carte en surimpression transparente. Un disque de 230 px (`CompassNode`) : la carte du capitaine vue par un shader (`compass_map.gdshader`) centrée sur le point ESTIMÉ, à 55 % d'opacité, bord fondu ; par-dessus, la rose — nord en haut comme la carte (une rose qui tournerait avec le navire serait un GPS de voiture), N en rouge, trente-deux aires —, le navire au centre à son cap, le vent en flèche sur le bord, d'où il souffle, et le rayon en milles. Molette : de 900 m à 18 km de jeu. Posée dans l'image, au-dessus de la bande du masque de cinéma ; cachée avec les instruments, sous la carte ouverte et au titre.

Et les notes de la carte : corps FIXE (28 en Estonia), qui ne suit plus la loupe — elles rétrécissaient au dézoom jusqu'à l'illisible.

## Le curseur de l'heure (Godot)

Signalé : « le soleil se couche mais ne se lève pas ». Le cycle était juste (mesuré sur un jour accéléré : coucher à 18 h 20, lever à 5 h 50, image revenue à la même luminosité le lendemain) ; c'était le curseur « Hauteur du soleil » : il réglait la hauteur à relèvement fixe, ne pouvait pas faire passer le soleil du lever au coucher, et le toucher arrêtait le jour — posé le soir, le soleil ne se relevait plus. Remplacé par un curseur « Heure », de 0 à 24 h : la vraie course (est, sud, ouest, lune et feux la nuit), l'étiquette dit l'heure et la hauteur (« 06:18 · soleil 7° », « crépuscule », « nuit »). Le jour REPART de l'heure choisie ; seul « Défilement du jour » à zéro l'arrête.

## L'estime : où l'on croit être (Godot)

Demandé : savoir où l'on est sur la carte par une méthode de vraie navigation.
Proposé, et retenu : l'estime et la hauteur de midi d'abord, les relèvements
ensuite.

**LE POINT ESTIMÉ** (`core/Reckoning.cs`) : à chaque sablier — une demi-heure
de jeu, quinze secondes à l'allure du ciel —, on file le loch (la vitesse) et
on lit le compas (le cap), et l'on porte la distance courue dans cette
direction. Le loch a son erreur propre (6 %, tirée par navire) et une erreur
de lecture (3 %), le compas la sienne (2°) et une d'embardée (1°). La DÉRIVE
n'y est pas écrite : le compas lit le cap, pas la route, et au près le navire
glisse — elle sort de la physique. Un changement d'allure entre deux lectures
est une erreur de plus : la vedette échouée net a vu son estime courir 230 m
trop loin, le loch comptant encore seize mètres par seconde jusqu'au sablier.

**L'INCERTITUDE** croît AVEC la distance (7 %), pas comme sa racine : une
erreur de loch est la même d'un sablier à l'autre. En somme quadratique pas à
pas, elle annonçait 9 m après 20 km quand l'erreur vraie en faisait 1 800
(labo `-- estime`, cent navires, 4° de dérive) ; linéaire, 2 150 m, et 56
erreurs sur 100 dedans — l'écart-type qu'on attend.

**CE QUI RECALE** : un port à 300 m (on le reconnaît : exact) ; la HAUTEUR DE
MIDI, de 11 h 30 à 12 h 30, soleil visible, une fois par jour — la latitude au
quadrant de Davis, à 3 minutes d'arc près (2,2 km de jeu), et elle seule : la
longitude attend le chronomètre (1760) ; l'ATTERRAGE d'une traversée, à 4 % de
sa longueur (236 M → 7 km ; mesuré à l'arrivée à la Tortue : 8,2 km d'erreur).
Le point estimé est tiré autour du vrai, pas l'inverse.

**CE QUI A CHANGÉ À LA CARTE** : le carnet garde DEUX routes. `Track`, la vraie,
perce le voile — la vigie voit la vraie côte — et ne se dessine plus ; `Estim`,
l'estimée, est la route à la plume. Le navire est une croix de plume dans son
ellipse (un écart-type, nord-sud et est-ouest) ; la vérité, un point rouge,
seulement quand tout est dévoilé (débogage). La carte s'ouvre sur le point
estimé, la ligne d'objectif compte depuis lui, et le carnet le garde d'une
session à l'autre. Réglages : `settings.json` → `reckoning`.

## La carte nette à la loupe, et bornée à sa feuille (Godot)

Signalé : les dessins très pixelisés ; « un souci de dépliage UV » sur les
bords.

**LA PIXELISATION** : la feuille est une image de 2 048 pixels pour 122 km, un
pixel pour soixante mètres ; à la loupe la plus forte (3 km de large) il en
restait cinquante sur tout l'écran. L'encre (route, ports, croix, traits,
notes, objectif) est maintenant RETRACÉE à l'écran sur la carte ouverte, par la
même plume (`Pen`) : `Map` porte les pixels de feuille vers l'écran, `K` est
leur rapport. Les traits grossissent avec la loupe jusqu'à 1,5 fois, les
lettres entre 0,7 et 1,3 fois. Pendant ce temps la feuille se dessine sans
encre, sinon elle doublerait chaque trait, pixelisée dessous ; le bureau la
retrouve à la fermeture. Et un trait prend un point tous les trois pixels
d'écran au lieu de vingt mètres fixes : à la loupe, une courbe restait une
suite de facettes.

**LES BORDS** : la vue débordait de la feuille et la texture étirait ses
pixels de bord — les rayures, traits compris. Le centre est maintenant tenu
pour que la vue reste dans la feuille ; plus large qu'elle, elle la centre, et
seul ce qui est sur la feuille est montré.

## Trois mâts qui tombent (Godot)

Demandé : un navire doit pouvoir perdre ses trois mâts.

**MESURÉ** (sonde : mâts recensés, cassables, puis la soute qui les prend
tous) : frégate, galion et pirate n'en avaient qu'**un** de cassable sur trois ;
le cotre et la goélette, **aucun**. Trois causes :

1. **Pièces soudées** : sur les modèles, le mât de misaine et le beaupré ne
   font qu'un objet (19 m sur 19), que ni l'épreuve du mât ni celle de la
   vergue ne reconnaissent. On coupe désormais les pièces composites en îlots
   de triangles (reliés par leurs indices ou par des sommets à la même place,
   sans quoi un cylindre partirait en lanières), et l'on ne garde la coupe que
   si un îlot a la forme d'un espar.
2. **L'artimon latin** : mât et antenne en biais forment un seul îlot, 2,4 m
   sur 4,8 de large. Reconnu à son ÂME — des sommets alignés sur une verticale
   sur 60 % de sa hauteur —, il tombe entier, antenne comprise. Et un mât sans
   vergue carrée n'était pas un mât du tout (les mâts se trouvaient par leurs
   vergues) : les espars debout restés sans vergue deviennent des mâts à part.
   Le troisième « mât » reconnu jusque-là était la vergue de civadière, sous le
   beaupré. Piège : le safran est lui aussi debout sur l'axe ; écarté parce que
   son pied plonge sous la flottaison (le pont n'était pas le bon repère :
   l'artimon de la frégate traverse une dunette haute).
3. **Les coques sans modèle** dessinaient leurs mâts à même le gréement, sans
   groupe qui tombe : chaque mât a maintenant le sien, articulé au pied, avec
   ses vergues ou sa bôme et sa toile.

Résultat : frégate, galion et pirate 3 sur 3, goélette 2 sur 2 — et la soute
les prend tous.

## La carte à la plume : trait biseauté, flèche retour, notes en Estonia (Godot)

Demandé : une flèche retour qui efface les derniers traits ; un trait plus fin,
en biseau, façon calligraphie ; les notes dans la police Estonia.

**LA PLUME BISEAUTÉE** : un bec de 2,2 unités de carte tenu à 45°, qui ne
tourne pas avec la main. Chaque segment est le parallélogramme que ce bec
balaie : plein en travers du biseau, un cheveu dans son fil, sans rien calculer
d'autre. Un filet de 0,45 dessous pour que le trait ne se rompe pas. Piège :
`DrawColoredPolygon` triangule, et un parallélogramme presque plat la faisait
échouer (« Invalid polygon data ») ; `DrawPrimitive` à quatre sommets ne
triangule pas.

Puis le bec élargi de 1,1 à 1,5 à la demande, et rendu réglable au menu (Carte → Épaisseur de la plume, 0,5 à 4 ; `reglages.ini` → `[carte] epaisseur_plume`) : la carte se redessine sous le curseur.

**LA FLÈCHE RETOUR** : un bouton « ↶ Effacer le dernier trait » en haut à
gauche, hors de la feuille, qui fait ce que faisait déjà Retour arrière
(`Logbook.Undo`) ; ses clics ne commencent pas de trait.

**ESTONIA** (Robert Leuschke, licence OFL) : lue à l'exécution dans
`godot/fonts/Estonia-Regular.ttf` par `FontFile.LoadDynamicFont`, sans import ;
absente, les notes gardent la police du moteur. Corps 22 : une anglaise se lit
plus petite qu'une linéale au même corps.

## La baleine, dans l'esprit de Moby Dick (Godot)

Demandé : une baleine qu'on voit au loin à sa gerbe, qui passe sous le bateau
(une ombre) et peut le percuter.

**UN CACHALOT**, parce que c'est celui de Moby Dick et de l'Essex (1820,
frappé deux fois, défoncé à l'avant, coulé). 15 m, 40 t. Modèle écrit par
`tools/whale-glb.js` (le patron du dauphin), à retoucher dans Blender : tête en
boîte sur un tiers de la longueur (sections en superellipse, exposant 3, qui
s'arrondit à 2 derrière les nageoires), mâchoire étroite et pâle, bosse et
crête, queue de 4,6 m sur sa charnière. Premier jet aux triangles retournés
(normales vers l'intérieur) : contrôlé ensuite, 398 normales sur 398 vers
l'extérieur.

**LE NOYAU DÉCIDE** (`core/Whale.cs`) : au large seulement (1 km de jeu des
côtes, 60 m de fond), deux rencontres par heure. Elle respire 35 à 60 s en
surface, un souffle toutes les 7 à 12 s, puis sonde 45 à 90 s, queue haute (la
queue sort parce que le nez pique : l'assiette suit la pente de sa route).
L'humeur est tirée à la rencontre : indifférente (35 %), curieuse (45 %),
hostile (20 %).
- **La curieuse** vise le navire et passe son dos à deux mètres sous la quille
  (axe au tirant + 3,6 m) : c'est la mer qui la montre, sombre, par
  transparence. Premier jet : elle corrigeait sa route jusqu'au bout et tournait
  autour de la coque (20 m au plus près, quatre minutes) ; elle tient
  maintenant son cap dans les soixante derniers mètres — 0 m au plus près,
  axe à 6,5 m sous une frégate.
- **L'hostile** charge en surface à 6 m/s, 7,5 sur les 150 derniers mètres, et
  frappe DE LA TÊTE. Le coup est une impulsion partagée entre les deux masses
  (restitution 0,1), appliquée au point touché — il pousse, fait virer et
  gîter — et une voie d'eau basse dans le compartiment touché, 0,05 m² par m/s.
  Mesuré : frégate +0,94 à +1,23 m/s, 0,30 à 0,38 m². Elle revient une fois sur
  deux (`ramAgain`), comme celui de l'Essex.

**TROIS PIÈGES relevés dans Godot, pas au labo** — le labo n'a ni terre ni
longue durée :
1. Le `%` de C# garde le signe du dividende : passé un tour de cap, le calcul du
   virage s'inversait et elle chargeait en s'éloignant. `Math.IEEERemainder`.
2. L'évitement des hauts-fonds (36 m) la détournait à chaque approche d'un
   navire au bord du plateau. Pendant une poursuite elle ne craint plus que
   6 m ; en deçà, elle renonce, et le DIT (« elle n'ose pas les hauts-fonds »)
   au lieu de laisser croire qu'on l'a distancée.
3. Renoncer après 150 s coupait des charges qui gagnaient encore : elle renonce
   quand la distance a crû de 100 m depuis le plus près, ou après 5 min.

Vérifié dans Godot à l'atterrage de la Tortue (340 m de fond) : vue à 700 m,
charge annoncée à 450, coup à 7,5 m/s, navire +1,23 m/s, voie d'eau 0,37 m²,
puis seconde charge.

**LE SOUFFLE** : des bouffées blanches du système des fumées de poudre
(`GunFxNode.Spout`), jetées à 7–12 m/s en avant et à gauche, puis dans TOUT le
vent (la poudre n'en prend qu'un tiers) : de la vapeur, pas de la fumée froide.
Les messages disent d'où on la voit, relevée sur le navire (« par tribord
avant »). ⇧K la fait venir et charger ; `--baleine 0|1|2`.

## B B, et la vedette de débogage (Godot et page)

Demandé : un double appui sur B qui double la vitesse de l'élan ; puis une
vedette moderne et puissante pour parcourir vite la carte en débogage.

**DOUBLER LA VITESSE, C'EST QUADRUPLER LA POUSSÉE** — la résistance croît au
moins comme le carré de la vitesse. Deux appuis à moins de 400 ms : 180 fois
la poussée au lieu de 45 (le premier appui a pu couper l'élan, le second le
reprend). Mesuré au labo (`-- elan`, frégate, mer calme, 8 min) : 48,4 nœuds à
×45, mais **77,8 à ×180 comme à ×720** — le garde-fou du solveur, 40 m/s,
hérité de la page. Le double appui ne donne donc que ×1,6 sur un navire
d'époque, et c'est voulu : le garde-fou est ce qui empêche une coque de partir
à l'infini.

**LE GARDE-FOU DEVIENT UNE LIGNE DE FICHE** : `engine.speedLimit` (m/s), 40
par défaut, dans le noyau ET la page (`ship-spec.js`, `ship-physics.js`). Aucun
navire existant ne change ; le banc de parité tient.

**LA VEDETTE ALBATROS** (`ships/vedette.json`) : coque procédurale de 15 m sur
4,2, 18 t, avant relevé, tableau large, une timonerie ; machine pour 20 m/s,
garde-fou à 60. Mesuré : **36,6 nœuds** à pleine machine, assiette 0,9°,
immersion 19 % ; **116,6 nœuds** sous l'élan (le garde-fou). Pas de modèle de
glissement : c'est une coque à déplacement menée très vite, ce qui suffit à
couvrir la Jamaïque (36 M d'une pointe à l'autre) en une minute sous B.
Anachronique exprès — elle est dans la liste des navires, touche N.

## Le navire pâle : le soleil π fois trop fort, la brume dans le mauvais espace (Godot)

Signalé : « le bateau semble toujours très pâle, comme un manque de contraste ».

**DEUX CAUSES, mesurées au pixel** (sonde lisant l'image rendue en des points
du bordé, sans capture) :

1. **Le soleil.** three.js r160 (éclairage physique) divise l'éclairement
   direct par π — la réflectance de Lambert —, Godot non : le 2,1 de la page
   éclairait π fois plus fort. Le bois au soleil saturait (rouge 1,00, vert
   0,74) ; ramené par `SkyNode.SunGain` = 1/π : (0,71 ; 0,54 ; 0,40). La
   lumière du ciel était déjà dans la bonne mesure une fois `AmbientGain` à 1,0
   (réglé par Arnaud) : 0,28, contre 0,9/π dans la page. Piège de la mesure :
   une seule caméra ne voit qu'un flanc — les points de l'autre flanc
   retombaient sur le premier et semblaient insensibles à l'ambiante.
2. **La brume.** La page la mélange APRÈS l'encodage sRGB ; le `blend_mix` de
   la passe posée sur les modèles la mélangeait en linéaire, ce qui monte un
   bois sombre sous 20 % de brume à 0,44 au lieu de 0,36 — au gros temps
   (brume ×7,4), laiteux à 40 m quand l'eau à côté restait sombre. Premier
   remède, RETIRÉ : relire l'image rendue (`hint_screen_texture`) et mélanger
   soi-même, exactement. Cette image ne contient que l'opaque, et la passe
   écrivait par-dessus tout ce qui est transparent : le verre des lanternes et
   les vitres clignotaient selon l'ordre de tri (signalé), la neige et les
   blessures du bordé — des passes posées avant la brume — disparaissaient.
   Reste la brume à la puissance 1,5, sur les modèles comme sur la coque
   procédurale : à 0,01 près du mélange de la page pour un bordé sombre.

## La Tortue, et les traversées d'une région à l'autre (Godot)

Demandé : aller plus loin que la Jamaïque sans la rapetisser — option « B »,
des régions séparées reliées par la carte, en commençant par la Tortue.

**LA MER ENTRE DEUX CARTES N'EST PAS DESSINÉE, ELLE EST COMPTÉE.** Toute la mer
des Caraïbes à 0,4 ferait 23 500 × 13 800 pixels — au-delà de la plus grande
texture de Godot — et, surtout, Port-Royal – la Tortue à 0,4 fait 180 km de jeu :
seize heures réelles à six nœuds. Chaque région garde donc sa carte à 0,4, et la
traversée se calcule (`core/Passage.cs`) : les milles réels entre là où l'on
quitte la carte et l'**atterrage** d'arrivée (le plus proche, déclaré dans la
fiche, `approaches`), à la vitesse que le vent du départ permet sur cette route.
Le calendrier avance d'autant.

**LE VENT DÉCIDE.** La vitesse sur la route est une moyenne de journées de mer,
pas une polaire : 2 nœuds au près (on louvoie), 3,6 à 60°, 5 de travers, 5,5
grand largue, 4,6 plein vent arrière. Mesuré au labo (`-- traversees`) :
Port-Royal → la Tortue, 235 M au 58°, **4 jours et 17 heures** par l'alizé
(vent du 75°), 2 jours par vent contraire ; le retour, 1 jour et 21 heures.
C'est la vraie dissymétrie de l'époque : Port-Royal se quitte plus vite qu'elle
ne se rejoint.

**LA TORTUE** (`world/tortue.json`, relief 1 456 × 740, 45 m par pixel comme la
Jamaïque) : l'île et la côte nord de Saint-Domingue, du Môle Saint-Nicolas au
Cap-Français. Cinq ports, lus au labo : Basse-Terre (la rade de Cayonne, départ),
Port-de-Paix en face à 2,1 M de canal, Port-Margot, le Cap-Français à 5,1 M, le
Môle à 12,8 M. Trois atterrages, tous en eau libre (340 à 357 m de fond, 5,4 à
6,3 km de côte) ; ceux de la Jamaïque aussi (346 à 381 m, 6 à 9,4 km). Les
Montagnes du Nord-Ouest ont été ajoutées à `reliefs.json` ; les autres chaînes y
étaient depuis la carte au dixième.

**CHANGER DE RÉGION RECHARGE LA SCÈNE.** Le monde est lu une fois et rien après
ne change — c'est ce qui le rend sûr, et une trentaine de choses le tiennent (la
terre, les villes, les pontons, la carte, l'abri de la mer, le solveur…). Plutôt
que de toutes les rebrancher, la scène est rechargée, et le bord passe la
frontière dans un objet statique : la coque, la bourse, la cale case par case,
la poudre, l'horloge, le calendrier, le vent, la toile. Ne passent PAS : les
avaries, l'ancre, les débris, les autres navires. Mesuré en ligne de commande
(`--traversee tortue`, 12 t d'épices et 5 t de lest chargées) : 236 M en 45,7 h
par le vent du départ, arrivée le surlendemain à 7,2 h, bourse, 17 t de cale
dont 12 d'épices, tout retrouvé.

**Piège : la malle relue par la scène qui la remplit.** Au premier essai
l'arrivée se jouait dans la Jamaïque même — `SetSail` remplissait la malle et
`Arrive`, appelée juste après au chargement, la vidait aussitôt. La malle est
maintenant prise au tout début du chargement (`_arriving`), et seule la scène
suivante la trouve.

**Ce qui dépend de la région** : le carnet (un fichier par région — un trait de
la Jamaïque passerait au travers des terres de la Tortue ; la Jamaïque garde
`carnet.json`), les quêtes (champ `region` : ailleurs elles attendent, sans
viser ni s'accomplir, et la ligne d'objectif dit « dans les eaux de la
Jamaïque »), le Cimetière des Galions (en mètres de la Jamaïque : coupé
ailleurs).

**Au large** : à 3 km de jeu de toute côte (7,5 km réels), on le dit une fois,
et la carte (I) propose « Faire route… ». Plus près de terre, le même panneau
sert de raccourci de débogage, et il le dit.

## J : de l'eau sous la dépression (Godot)

Signalé : J menait au milieu de la Jamaïque. Le transport de la démo datait
d'avant la terre et posait la coque au centre du grain. Porté de `tempete()` :
on CHOISIT la dépression qui a de la mer dans sa moitié intérieure (25 m sous la
quille, pas de côte à 600 m), puis on l'y pose ; l'ancre est rentrée d'un coup.
Mesuré trois fois : force 7,2, 90 à 97 m de fond.

## La chaîne de l'ancre (Godot)

Signalé : la chaîne ne touchait pas le navire ; voulue plus fine, noir brillant.

**DEUX JOURS, un à chaque bout.** Côté navire, elle partait du bossoir — posé
EXPRÈS en dehors du bordé pour que l'ancre pende claire — donc à près d'un mètre
de la coque. Côté fond, l'organeau était compté verge debout même quand l'ancre
était couchée : la chaîne s'arrêtait 2,5 m au-dessus d'elle. Elle sort
maintenant de l'**écubier**, sur le bordé, et l'ancre couchée pointe sa verge
vers le navire, l'organeau au bout.

**Le bordé se lit au RAYON, pas aux sommets** : un modèle léger n'a aucun sommet
dans un mètre carré de son avant, et l'on retombait sur le plan de formes, plus
large d'un mètre. Un rayon tiré en travers contre les triangles de la coque
(`ShipNode.HalfAt`) : écubier à 1,53 m sur la barge (tranche 1,59), 2,04 m sur la
frégate (tranche 3,00 : l'avant s'affine). Écart mesuré aux deux bouts : 0 à
2 mm.

**Des maillons**, un tore étiré, chacun tourné d'un quart sur le précédent : fer
de 0,22 % de la longueur (6 cm sur 27 m, 3 à 9 cm), maillon de six fers sur
trois et demi, 148 à 325 maillons pour une touée de 39 m. Noir de fumée graissé :
albédo presque noir, rugosité 0,22, métal 0,9.

## Le plan d'arrimage (Godot)

Demandé : la fenêtre de la page pour simuler le chargement de poids.

Trois hauteurs (pont 0,85, entre 0,50, fond 0,18) et cinq cales — les
compartiments de l'envahissement, donc de vrais morceaux de coque. Clic : du lest
chargé, clic droit : déchargé, au pas choisi (1 à 100 t) et du bord choisi. La
grille montre tout ce que porte chaque case, lest et épices : c'est le poids qui
compte pour la coque. « Vider » jette TOUT, épices comprises, comme dans la
page — la cale fait foi — mais le dit (« 6,0 t d'épices par-dessus bord »).

**Une seule définition pour les niveaux** : le fond du plan EST `HoldFloor`,
celui où le comptoir range ses épices ; une cargaison chargée à un niveau que la
grille ne connaît pas serait invisible — le bug que la page a connu.

**LA TOUCHE** : aucune lettre partagée par AZERTY et QWERTY n'était libre. Reste
l'emplacement Z d'un QWERTY, qui est le W d'un AZERTY : lu par son EMPLACEMENT,
pour ne jamais tomber sur la machine, et nommé par
`DisplayServer.KeyboardGetKeycodeFromPhysical` — le mémento et le panneau disent
la lettre du clavier qu'on a sous les doigts (« W » ici), au lieu d'un nom de
QWERTY.

Le clic droit est lu sur le bouton lui-même (`GuiInput`) : un `Button` ne dit
pas quel bouton l'a pressé.

Mesuré par sonde, sur le vrai chemin des événements (chaland de 210 t) : touche
→ panneau ouvert ; 10 t au fond → 220 t, tirant 1,74 → 1,82 m ; clic droit →
210 t, case vide ; 20 t sur le pont à tribord → 230 t, gîte 3,1° du bon bord ;
« Vider » → 210 t, tirant 1,73 m, gîte 0,0°.

## La Jamaïque seule, à 0,4

Signalé : « la distance entre Port-Royal et la côte de la Jamaïque est bien trop
courte, tout cela ressemble à un petit port breton. »

**CE N'ÉTAIT PAS LA PEINTURE, C'ÉTAIT L'ÉCHELLE.** Au dixième, la rade de
Kingston tenait en trois cents mètres (Port-Royal – Kingston 0,28 M) ; repeindre
la côte n'y pouvait rien, puisque chaque distance est une latitude et une
longitude multipliées par `scale`. Deux voies chiffrées avant de toucher à rien :
toute la mer à 0,2 (la côte deux fois plus grossière, 90 m par pixel, ou 830 Mo
en pointe pour garder la finesse), ou la Jamaïque seule, recadrée. Choisi : la
Jamaïque à **0,4**, image 2 720 × 1 580, **toujours 45 m par pixel**, 21 Mo de
relief tenu au lieu de 45. Le monde se charge en 46 ms au lieu de 95.

Mesuré sur la nouvelle carte : Port-Royal – Passage Fort 1,3 M, Old Harbour
5,9 M, Yallahs 6,2 M, Port Morant 11,6 M au 096°, Negril 35,5 M ; la plus longue
traversée 47,5 M, d'où un palier de cours de 34 176 s (9,5 h). Port-Royal bâtit
maintenant ses 150 maisons — elle n'en trouvait que 64 sur une langue de sable
de trois pixels ramenée au dixième.

**CE QUI A DÛ SUIVRE, et qui ne suivait pas de lui-même** — tout ce qui était
écrit en mètres du monde plutôt qu'en latitude :
- le Cimetière des Galions (`settings.json`, `ghosts.js`, `Ghosts.cs`) serait
  tombé sur une colline de 534 m : porté à (−6 000, −14 000), 394 m de fond,
  11,6 km de toute côte ;
- les sept ports hors de l'île ont quitté la fiche, et la quête qui allait à
  Petit-Goâve a été ramenée en Jamaïque — le pli va au fort de Port Morant, la
  Sainte-Anne a disparu après avoir doublé la pointe Morant (293 m de fond,
  3,9 km de côte) ;
- le tour d'apprentissage aurait duré quatre heures, dont six milles contre
  l'alizé : il s'arrête à Old Harbour, vent portant, 8,7 M en tout ;
- le banc de parité testait l'ancien nom `island` sur Petit-Goâve : il le teste
  sur Yallahs.

Ce qui a suivi SEUL : les ports, leurs pontons et leurs villes, les lieux des
quêtes (écrits en ports et en latitudes), la carte du capitaine, le palier des
cours. C'est la raison d'écrire un lieu en latitude plutôt qu'en mètres.

## La ligne d'eau sur le bordé (Godot)

Signalé : « la transparence de l'eau au niveau du bateau est trop importante ;
l'eau doit se découper de manière plus visible sur la coque. »

**LE REFLET ÉTAIT EFFACÉ AVEC LE RESTE.** Le shader mêlait la surface déjà
composée — reflet du ciel, coque en miroir, soleil, écume — à l'image de ce qui
est dessous, dans la proportion de l'extinction. Sous une faible épaisseur
d'eau, T ≈ 1 : la surface disparaissait tout entière, reflet compris, et le
bordé immergé se lisait comme dans un aquarium. Or une surface est une SOMME : F
de reflet, et (1 − F) de ce qui sort de l'eau — le corps de la mer ou ce qui est
derrière. La transmission ne remplace désormais que le corps. La page porte la
même formule et le même défaut.

**LE VOILE DE SURFACE (`u_veil = 0,55`).** Le reflet seul ne suffit pas vu d'en
haut : à trente degrés, F vaut 4 %. L'extinction ne coupe rien au ras de la
flottaison — sous dix centimètres, exp(−0,34 × 0,1) laisse passer 97 %. Une
vraie surface diffuse dès le premier centimètre (les rides brisent l'image, une
eau de rade porte ses particules) : le voile est la part qui passe à épaisseur
nulle, avant que la profondeur n'éteigne le reste. Un seul nombre, 1 rend
l'ancienne vitre. Pas appliqué par en dessous, où la fenêtre de Snell montre de
l'air.

Réglé par l'utilisateur à l'œil ; rien n'a été mesuré ici, et c'est voulu.

## Le commerce porté (Godot)

Demandé pour que les premières missions servent à apprendre la navigation ET à
gagner de l'or.

**LE NOYAU D'ABORD, ET AU BIT.** `core/NavalSim.Core/Market.cs` porte la bourse
et le cours des épices de `purse.js`. Le cours est une fonction pure du port et
de l'heure : il n'y a aucune raison qu'il diffère d'une pièce entre la page et
Godot, et s'il diffère, l'une des deux ment au joueur. Le hachage multiplie en
double au-delà de 2^53, comme celui des dépressions — d'où `Js.cs`, qui tient
désormais pour les deux le ToInt32 et le `Math.round` de JavaScript (la demie
qui monte toujours, là où `Math.Round` de C# arrondit au pair). HUITIÈME PARTIE
du banc : 1 992 hachages au bit, 1 407 cours à la pièce, 240 nouvelles,
12 âges et 189 rumeurs à la lettre, la bourse opération par opération.

Le palier se recale sur le monde (`Tune(LongestLeg)`), comme la page : avec la
Terre-Ferme dans la carte, il vaut 53 429 s — près de quinze heures. Un cours
tient donc une session entière, ce qui est la leçon de la page (« un cours doit
tenir le temps d'une traversée ») poussée par la taille de la mer.

**LE COMPTOIR N'EXISTE QU'À QUAI**, et « à quai » se mesure : à moins de 220 m
de la tête du ponton et sans erre (0,8 m/s). Il dit le cours, prend l'ordre par
dizaines de tonnes, vend la poudre si le bord a des pièces, et liste ce qu'on
sait des AUTRES ports — le chiffre ferme et son âge, les plus fraîches d'abord,
dans un tableau qui défile : vingt ports ne tiennent pas sous le comptoir, et
couper la liste cacherait justement les plus lointains.

Mesuré à Port-Royal : achat de 10 t à 581, bourse 24 000 → 18 190, déplacement
210 → 220 t, tirant 1,56 → 1,61 m ; revente à 457, bourse 22 760 — l'aller-retour
sur place perd la marge, comme il doit. Les épices vont au fond, au milieu, au
niveau 0,18 que le plan d'arrimage de la page dessine — une seule constante,
que la cargaison des bouteilles emploie aussi.

**CE QU'ON LIT EN MER, DANS UN ENCART** — le navire parlé et la bouteille, comme
dans la page, et non dans le cadre du milieu des quêtes : il ne prend pas la
barre, il se pose en bas à gauche et s'efface seul. Le navire parlé vient toutes
les quatre à huit minutes, sous voiles et loin d'un port ; il dit vrai et sans
chiffre, et le liseré se dore pour une bonne nouvelle, se rouille pour une
mauvaise. La bouteille retrouve ses TROIS tirages : journal, carnet de comptes
daté de l'âge de la bouteille, ou carte.

**« De Old Harbour », « de un navire inconnu »** : la contraction de la page ne
connaissait que « Le » et « Les » ; les rades anglaises de la Jamaïque ont fait
paraître l'élision qui manquait. `DeNom` élide devant une voyelle.

**Le pirate qui aborde** prend la moitié de la bourse et toutes les épices, ce
que la page lui fait prendre. En Godot il pillait sans rien emporter : il n'y
avait pas de bourse.

**Ce qui n'est PAS gardé** : la bourse et la cale vivent le temps d'une séance,
comme dans la page. Garder l'une sans l'autre serait pire que rien — de l'or
qui survit à une cargaison qui s'évapore — et c'est un seul chantier.

## Les cartes en bouteille, et les villes qui s'allument (Godot)

Demandé : que les débris d'un naufrage rendent des cartes en bouteille.

**LA BOUTEILLE FLOTTAIT DÉJÀ** — `flotsam.js` était porté avec son halo, sa
prise en venant près et lentement, et un commentaire qui disait « ce qu'elle
contient regarde la page ; ici elle se nomme, EN ATTENDANT LA CARTE ». La carte
existait depuis la séance précédente ; il ne restait qu'à les joindre.

Une fois sur trois une page de journal de bord — le dernier geste de quelqu'un.
Deux fois sur trois une CARTE : une cargaison échouée sur la batture d'une île,
cherchée depuis le port dans une direction au hasard qui ne soit pas celle de sa
rade, à la première eau d'un mètre de fond. Mesuré : sur six bouteilles, deux
cartes, chaque croix tombée dans 0,73 et 0,80 m d'eau.

**LA CROIX VIT DANS LE CARNET, PAS DANS LES DÉBRIS**, et c'est le choix qui
tient tout le reste. Un lieu qu'on a APPRIS n'est pas un lieu qu'on a VU : il
doit survivre à la fermeture du jeu, alors qu'une caisse flottante est un objet
de la séance. Le carnet garde donc la croix, sa clé et son libellé ; à
l'ouverture, chaque croix repose sa caisse sur le fond. Une seule mémoire, rien
à tenir d'accord — et la carte ne peut pas mentir sur ce qu'elle montre.

**C'EST LE TIRANT D'EAU QUI DÉCIDE** qui peut aller la chercher, et non un
drapeau dans la fiche. La page demandait des avirons (`needsBoat`) ; la caisse
est posée dans un mètre d'eau, donc y vient qui peut y flotter. Mesuré :
chaloupe 0,66 m, chaland 1,55 m, cotre 1,96 m — le seuil à 1,20 m sépare
proprement. La règle est physique, elle se vérifiera toute seule le jour où la
chaloupe se mettra à l'eau depuis le bord.

**LA RÉCOMPENSE SE SENT À LA BARRE** : les épices vont au fond de la cale, au
milieu — un fret d'un bord la ferait gîter. Mesuré 210 t puis 216 t pour six
tonnes, et le tirant d'eau suit. C'est la seule récompense de ce jeu qui change
la façon dont la coque se conduit.

### Les villes la nuit

**LES FENÊTRES SONT DESSINÉES DANS LE MUR LUI-MÊME.** Une ville est un
MultiMesh de plusieurs centaines de boîtes ; y accrocher une lumière par maison
recompilerait tout le rendu, ce que la règle « ne jamais ajouter une lumière en
jeu » défend depuis three.js et que Godot ne supporterait pas mieux. Le shader
perce deux fenêtres par face dans la moitié haute du mur, et le pignon n'en a
pas — on ne perce pas un mur qui porte le faîtage.

**TOUTES NE S'ALLUMENT PAS, ET AUCUNE COMME SA VOISINE** : le tirage vient de la
POSITION de la maison, hachée dans le vertex. Rien à stocker, rien à passer par
instance, et la même maison a la même fenêtre d'une partie à l'autre. Elles
s'allument sur le MÊME chiffre que les fanaux du bord (`Sky.Night`) et non sur
un second seuil : la ville et le navire passent la nuit ensemble.

Le mur a donc sa matière et le toit garde l'ordinaire, qui coûte moins puisqu'il
n'a rien à éclairer.

## Deux pannes muettes : la terre à l'envers, la coque assise sur le sable

Signalées à l'essai, à un quart d'heure d'intervalle, et toutes deux invisibles
en tant que telles — on ne voyait que leurs conséquences.

**« LA TERRE EST ENCORE TROP SOUS L'EAU. »** Elle ne l'était pas : la sonde a
rendu 9,3 m de haut à quarante mètres du navire, 928 sommets hors d'eau sur
1 089 dans le carreau, celui-ci bâti, visible, bien placé (écart de 43 cm sur
1 343 m, la précision du flottant), matériau opaque, couleur de sable juste.
Tout était correct et on ne voyait rien. En cachant la mer : le ciel. En ôtant
la passe de brume : le ciel encore. En passant le matériau en DOUBLE FACE : la
côte, d'un coup, avec ses maisons et son môle.

**three.js tient pour face avant les triangles en sens direct, Godot en sens
HORAIRE.** Le relief était porté de `land.js` avec son ordre d'indices, donc
Godot le voyait par l'envers et le supprimait dès qu'on le regardait d'en haut.
Il ne restait qu'une mer plate avec des maisons posées dessus — ce qui se lit
exactement comme une terre noyée. La toile et les pavillons retournaient déjà
leurs triangles ; la côte était la seule à l'avoir manqué, et personne ne
pouvait le deviner puisque le symptôme parlait de hauteur.

**LEÇON : quand tout ce qu'on mesure est juste et que rien ne s'affiche, ce
n'est plus une valeur qu'il faut interroger, c'est une CONVENTION.** Le culling
désactivé est le test qui tranche en une capture.

**« CERTAINS BATEAUX ONT UN COLLIER D'ÉCUME RECTANGULAIRE. »** Tous l'avaient :
la sonde a rendu, pour chaque rangée de la texture de profils, des fractions de
1,00 à 1,00 — c'est-à-dire le rectangle de secours de `HullProfile.Finish`,
celui qui tient quand rien n'est mesurable. La bande de mesure tombait dix
mètres SOUS le bordé : `bande −11,83 à −9,59` pour une coque dont le maillage
va de −1,87 à +1,40.

La cause est trois étages plus haut. `Settle()` repose la coque au zéro LOCAL
et y fait tourner le vrai solveur, échouage compris — or au démarrage le zéro
local est Port-Royal, **c'est-à-dire la langue de sable**. La coque s'asseyait
donc sur le SOL : tirant d'eau 0,00 m, immersion 0,0 %, et pour « flottaison »
la hauteur du terrain. Le chiffre qui l'a dénoncée : la même coque s'asseyait à
7,92 m avant que la berge soit relevée et à 11,27 m après, sans qu'une ligne de
la coque ait changé — une flottaison ne suit pas le relief.

Le fond est maintenant décroché le temps qu'elle trouve ses lignes, et rendu
aussitôt. Mesuré : chaland à **y = 0,19 m, tirant 1,55 m, immersion 38,5 %**,
contour de 0,10 à 1,00 au lieu d'un rectangle plat ; frégate 5,64 m de tirant,
corps de −34,5 à +24,6 m, l'étrave fine comme elle doit l'être.

**LEÇON : un banc d'essai qui « réussit » avec un tirant d'eau de zéro n'a pas
réussi.** `Settle` imprimait depuis toujours « tirant 0,00 m, immersion 0,0 % »
et personne — moi compris — ne l'a lu comme l'aveu que c'était.

**Ce que la page fait et qu'il fallait lire** : elle amarre AVANT de s'asseoir,
et son journal le dit en toutes lettres. L'ordre inverse côté Godot était le
vrai bug ; décrocher le fond est le remède qui n'impose pas un ordre.

### Trois demandes de la même séance

**`R` répare ET renfloue**, comme la page, et un bandeau « Votre navire a
sombré ! » paraît quand il n'en dépasse plus rien. La page exige dix mètres
d'eau au-dessus du point le plus haut ; deux suffisent ici, et il le FAUT : dans
une rade de onze mètres une coque posée sur le sable n'a jamais dix mètres sur
la tête, et le bandeau ne venait pas là où l'on s'échoue le plus souvent.

**Le curseur d'écoute**, avec le repère doré du meilleur réglage : sans lui, un
débutant lit « voiles établies », voit un nombre plausible de kilonewtons et
n'apprend jamais que six fois cette force était à une touche de distance.

**L'HORIZON DE LA MER.** La nappe fait sept kilomètres et on en voyait le bord
en prenant de la hauteur. L'agrandir coûterait la finesse des vagues — le
remaillage vers la caméra est calibré sur sa taille, un mètre le long de la
coque — donc on lui coud un ANNEAU lisse de 3 000 à 40 000 m, un mètre et demi
plus bas pour que la houle le recouvre sans couture ni scintillement. Il ne
calcule rien : à trois kilomètres la brume éteint déjà 94 % du contraste, et
une crête n'y vaut plus un pixel. Même fonction de brume que tout le reste,
donc on ne voit pas où l'une finit et l'autre commence.

**La berge relevée au passage** (2,5 → 6 m au trait de côte, 20 m à sept cents ;
les Palisadoes de 90 à 130 m de large et de 6 à 9 m de haut). Elle avait été
changée en croyant corriger la terre noyée ; une fois la vraie cause trouvée,
les deux reliefs ont été comparés à la même capture, et le relevé tient mieux :
une berge franche au lieu d'un dégradé, et quinze maisons de plus à Port-Royal
(49 → 64). Gardé pour cela, et non pour la raison qui l'avait fait écrire.

## Les quêtes portées, et les rades de la Jamaïque (Godot)

Demandé : le système de quêtes, et la première — celle qui apprend à jouer —
menant d'une rade de la Jamaïque à l'autre, telles qu'une vieille carte
anglaise les porte.

**IL FALLAIT D'ABORD DES PORTS OÙ ALLER.** Treize rades ajoutées à
`world/caraibes.json`, à leurs vraies latitudes : Passage Fort, Old Harbour,
Withywood, Black River, Savanna-la-Mar, Negril, Lucea, Montego Bay, Dry
Harbour, Port Maria, Port Antonio, Port Morant, Yallahs. Une rade coûte quatre
nombres — le reste (le rivage, la longueur de la jetée, le bassin dragué) est
trouvé dans l'image. Mesuré : 21 ports nés en 46 ms, jetées de 32 à 62 m, et
les villes en 40 ms pour 3 170 maisons. Un mode de banc pour le dire avant
d'écrire quoi que ce soit : `Lab -- ports`. C'est lui qui a fait descendre le
ponton de Passage Fort de 150 m — la borne — à 36, en changeant son relèvement
de quai de 100° à 120° : au fond de la rade, l'eau est ailleurs.

Une ville sans port ne se bâtit que près de l'eau (les maisons se posent entre
11 et 700 m du rivage) : **St Jago de la Vega**, qui est à dix kilomètres dans
les terres, a donc été refusée par la règle et retirée de la fiche. Elle reste
dans la quête, comme la ville dont Passage Fort est le débarcadère — ce qu'elle
est.

**LE PORTAGE A TROUVÉ DEUX DÉFAUTS DANS LA PAGE**, tous deux invisibles à
l'œil, et c'est à peu près la raison d'être de ce banc :

- `place()` écrivait `if(isl && (a.port || !(bearing…)) && isl.port)`. Le
  premier terme avale les deux autres : dès que le champ s'appelle `port`,
  c'est la tête du ponton qu'on obtient, et la forme documentée
  `{ port, bearing, miles }` ne donnait jamais le point demandé. Elle ne
  marchait qu'avec `island`, l'ancien nom.
- `allerQuete()` lisait `world.byKey(s.at.island)` après avoir testé
  `s.at.port` : sur une étape écrite avec le nom moderne, la console plantait.

Les deux sont corrigés des deux côtés. Le premier n'aurait rien allumé : il
aurait posé le cercle deux milles ailleurs, et le joueur aurait cherché.

**LE BANC DE PARITÉ, SEPTIÈME PARTIE.** Une quête ne calcule presque rien, ce
qui la rend mal vérifiable à l'œil : un lieu mal résolu n'éteint aucun voyant,
une étape remplie un cran trop tôt fait sauter un message que personne ne
reverra. Le relevé prend donc les deux : **dix-sept formes de lieu** — y compris
celles qu'on n'écrirait pas soi-même, le port inconnu, le champ vide, l'ancien
nom — et le **déroulé complet** d'une quête d'essai le long d'une route bâtie
à partir des lieux eux-mêmes, deux cent quatre-vingt-dix pas à un dixième de
seconde. On compare l'étape en cours, le compteur de `stop`, la distance, le
relèvement, et **à quel pas exactement** chacun des sept messages s'affiche.
La fiche d'essai voyage DANS le relevé : les deux côtés lisent la même, et un
écart ne peut venir que du code. Pire écart : 4,6e-13 sur une distance.

**CE QUE L'ÉCRAN EN MONTRE.** Une ligne dorée sous les instruments, qui se
place d'après la hauteur du bandeau — laquelle change avec l'état du navire,
si bien qu'un nombre écrit en dur s'en décrocherait au premier échouage. Les
mots sont ceux de la page, jusqu'à la bascule des unités : en mètres sous le
demi-mille (« 20 m sur 900 m »), en milles au-delà, « vous y êtes » dans le
cercle. Les messages passent dans un cadre au milieu, en **file** et non en
remplacement : une étape remplie affiche son message PUIS la consigne de la
suivante, et écraser le premier par le second, c'est perdre la moitié de ce
qu'on est venu chercher. Le cadre prend la hauteur de son texte.

Le lieu de l'étape est **cerclé de doré sur la carte du capitaine**, et la
carte ne connaît pas les quêtes : elle DEMANDE — un `Func` qu'on lui donne —
et n'en garde rien. On peut jouer sans une seule quête sans qu'une ligne de la
carte s'en doute.

**LA PREMIÈRE QUÊTE, `le-tour-de-la-jamaique`** : appareiller (sortir du cercle
de Port-Royal, ce qui apprend M, V, A/D, Q/E), accoster à Passage Fort en
face, mettre en panne au sud des Palisadoes — 133 m d'eau, à un mille du
rivage —, relâcher à Yallahs puis à Port Morant en louvoyant contre l'alizé,
et rentrer vent arrière. Quatre milles en tout, les quatre sortes d'objectif
dans l'ordre où elles se compliquent, et trois rades portées sur la carte au
passage. L'outro nomme les dix autres, pour la suite.

**Et une leçon de ligne de commande** : les options du jeu passent APRÈS
`--`, sans quoi `OS.GetCmdlineUserArgs()` rend une liste vide et Godot avale
tout en silence — trois lancements à chercher pourquoi la capture ne venait
pas, alors que `--titre 0` n'était pas lu non plus.

## La carte du capitaine (Godot)

Demandé comme un défi : dans la chambre, une carte VIERGE sur laquelle le joueur
ajoute lui-même notes et dessins aux endroits visités.

**LA FEUILLE EXISTAIT DÉJÀ**, et c'est le plus joli de l'affaire. Le modèle du
navire porte un bureau dont le matériau s'appelle **`map_free`** — le
modéliste avait prévu la place. On ne dessine donc aucun objet nouveau : on
remplace ce que cette surface montre, en la retrouvant par le NOM de son
matériau. Un autre navire qui nomme ainsi sa feuille l'aura aussi, sans une
ligne de plus.

**CE QU'ON GARDE, CE SONT LES TRAITS, PAS UNE IMAGE**, et en mètres du MONDE.
Une carte annotée enregistrée en image serait lourde, liée à sa résolution et
illisible par qui que ce soit ; une liste de polylignes se redessine nette à
toute échelle, se corrige, et tient dans un `carnet.json` qu'on peut ouvrir.
C'est aussi ce qui fait que le même trait est au même endroit sur la feuille du
bureau et sur la carte ouverte : une définition, deux usagers.

**LA CARTE NE MONTRE QUE CE QU'ON A VU.** Le carnet retient la route par points
tous les 220 m ; chacun perce un voile de parchemin sur 2,6 km — l'horizon d'une
vigie à cette échelle. Le bord de la découverte n'est pas net : un cartographe ne
trace pas de frontière là où son regard s'arrête, donc le voile s'amincit et
laisse un liseré brûlé. Un port touché à moins de 300 m s'inscrit avec son nom.

Tout est dessiné dans un `SubViewport` : le fond de relief (le portage de
`chartImage`, terre en chamois ombré du nord-ouest, hauts-fonds en bande pâle,
grand fond laissé au papier), le voile, la route, les traits et les mots.

**Deux corrections d'échelle, toutes deux vues à la capture :**
- la feuille couvre les Caraïbes entières, où 2,6 km font vingt pixels : sans
  loupe elle ne montrait RIEN de ce qu'on venait de relever. D'où le zoom à la
  molette (3 à 260 km de large), qui s'ouvre sur sa position et zoome sous le
  curseur ;
- les lettres ne doivent PAS suivre la finesse de la feuille. Écrites avec le
  même facteur que les traits, deux notes couvraient la Jamaïque : un nom de
  port a sa taille sur le PAPIER, et doubler la résolution doit le rendre plus
  fin, pas plus gros.

**LA PLUME NE RECEVAIT PAS UN SEUL CLIC**, et la cause est un ordre, pas un
calcul. Godot sert l'INTERFACE avant `_UnhandledInput` : le fond sombre et la
feuille sont des `Control` qui arrêtent la souris, si bien que le clic était
déjà marqué traité quand la carte le cherchait. La sonde l'a pris sur le fait,
et de façon instructive : des événements fabriqués à la main touchaient parfois
la plume — tant qu'aucun mouvement de souris n'avait dit à l'interface quel
`Control` se tenait sous le curseur, le clic tombait jusqu'en bas. Dès le
premier déplacement, plus rien ne passait ; en jeu, où la souris bouge toujours,
plus rien ne passait jamais. La carte est donc servie dans `_Input`, qui vient
avant tout le monde. **Une surcouche qui prend la main prend l'entrée EN AMONT
de l'interface, pas en aval.** Mesuré ensuite par le chemin exact du joueur
(titre, Entrée, `I`, clic glissé, `E`, clic droit) : un trait de 9 points,
encre 0 → 1, champ de note ouvert et au foyer.

Au passage, `E` marchait déjà — le clavier, lui, ne traverse pas l'interface —
mais sans un trait à l'écran rien ne le disait. Et Échap pendant qu'on écrit
annule désormais la note, au lieu d'ouvrir le menu du jeu derrière la carte.

**LA CARTE ÉTAIT RETOURNÉE D'UN DEMI-TOUR, et le bouton de débogage l'a montré dès le premier essai** (signalé : « Port-Royal et Passage Fort au mauvais endroit »). La feuille avait sa propre conversion monde → pixels, un rectangle de mètres pris entre deux coins : le plus petit x pour le bord gauche — or l'est est −x, donc l'EST à gauche — et le plus petit z en haut, donc le SUD en haut. Le relief, lui, est peint pixel à pixel dans le repère de l'image. Toutes les marques — route, ports, voile percé, plume, croix, cercle de quête — tournaient donc d'un demi-tour par rapport à l'île, en restant parfaitement d'accord entre elles : sous le voile, rien ne pouvait le trahir. Prédit avant correction : Port-Royal attendu à (0,661 ; 0,598) de la feuille, l'ancienne conversion le posait au symétrique (0,339 ; 0,402) — là où la capture le montrait. Les marques passent désormais par `World.PixelAt`, la conversion même du relief (et son inverse exact pour la plume) : mesuré, Port-Royal à (0,661 ; 0,601), aller-retour feuille ↔ monde à 1 à 4 mm. Le carnet n'a rien eu à migrer : il garde des mètres du monde, seul le dessin était faux. **LEÇON : deux conversions pour le même passage, c'est deux vérités — et celle qu'on ne regarde pas peut être retournée sans que rien ne le dise.**

**Le bouton « Tout dévoiler (débogage) »**, en haut à droite de la carte ouverte, lève le voile sur toute l'île sans rien écrire au carnet — un regard de l'auteur, pas une découverte du capitaine ; le rebasculer rend le voile que la route a percé. Il a fallu que la plume RENDE les clics tombés hors de la feuille : servie avant l'interface, elle les gardait tous, et aucun bouton de cet écran n'en aurait jamais reçu un. Vérifié par sonde sur le vrai chemin des événements : le bouton bascule, la feuille dessine toujours, et le clic sur le bouton ne laisse aucun trait.

`I` ouvre la carte, clic pour tracer (trois encres), clic droit pour une note,
retour arrière pour effacer le dernier trait. Ce qui reste à faire : la vérifier
posée sur le bureau en 3D — la feuille est habillée, mais aucun cadrage n'a
encore permis de la voir en place.

**Un trait garde sa plume.** La largeur du bec suivait la loupe du MOMENT (plafonnée à ×1,5), les lettres la loupe tout court : une écriture fine faite de près tournait au pâté de loin (signalé). Chaque trait retient donc sa demi-largeur en mètres de carte au moment du tracé (`Stroke.W`, `"w"` dans le carnet, `ChartNode.NibMetres`), et se dessine à `W / MetresPerPixel × K` ; le filet dessous en suit 30 %, jamais sous 0,35 px. La glissière Épaisseur de la plume ne vaut plus que pour les traits à venir. Les traits des anciens carnets (sans `w`) gardent l'ancienne règle.

## Le son porté (Godot)

Demandé : le son des canons et l'ambiance. Tout l'intérêt de `sound.js` tient
en une ligne — **le temps que le bruit met à venir** —, et c'est ce qu'aucun
moteur ne fait pour vous.

**CE QUE GODOT FAIT DÉJÀ, on le lui laisse.** `AudioStreamPlayer3D` porte
l'atténuation en 1/r (`InverseDistance`, distance de référence 55 m comme la
page), le panoramique, et il écoute par la caméra active — ce qui EST le choix
de la page : on entend d'où l'on regarde, donc une vue plantée à deux cents
mètres retarde votre propre bordée. Réécrire tout cela aurait été refaire moins
bien ce qui existe.

**CE QU'IL NE FAIT PAS est écrit ici**, et c'est exactement ce que la page
avait de propre :
- le RETARD : la distance divisée par 343 m/s, une liste d'attentes purgée à
  chaque image. Mesuré : à 55 m, 0,16 s ; à 675 m, **1,97 s** ;
- l'ABSORPTION DES AIGUS, dont la coupure est posée coup par coup sur la
  distance (20 kHz × exp(−d/260), plancher 700 Hz). Mesuré : 16 kHz à 55 m,
  **1,5 kHz à 675 m** — le claquement est parti, il ne reste que le ventre ;
- la bascule d'échantillon à 400 m, que ce dégradé rend inaudible.

**Une seule voie pour tout ce qui sonne**, comme dans la page : le retard et
l'absorption sont des propriétés de la DISTANCE, pas du coup de canon. Le bois
qui casse s'entend donc à la distance de la CIBLE, et arrive après la pièce sans
qu'une ligne le dise.

**Les deux bruits fabriqués** — le tonnerre d'un coup de foudre, le grondement
du kraken — sont écrits dans un `AudioStreamWav` au premier usage puis joués
par la même acoustique. Rien à charger.

**La musique ne passe PAS par cette acoustique**, et ce n'est pas de la
plomberie : une musique ne vient de nulle part — elle est dans la tête du
commandant, pas sur l'eau. Elle suit la situation comme dans la page : une voile
hostile à moins de 1 200 m ou du fer en l'air fait passer à l'action, et le
calme revient quinze secondes après que tout s'est tu. Le seuil de retour est
plus large (1 800 m) : sans cela une voile qui louvoie à la limite ferait
clignoter la musique. Coupée par défaut, comme dans la page ; `--musique 1`
pour l'entendre sans chercher la bagarre, `--feu n` pour lâcher une bordée.

Trois réglages nouveaux dans `reglages.ini` : bruitages, musique, volume — ce
dernier sur le bus maître, l'endroit qui vaut pour tout ce qui sonne.

## L'écran de titre (Godot)

La page n'en a jamais eu : on n'y tombait pas dans un jeu, on y tombait dans une
simulation déjà lancée. Demandé, avec une maquette — titre à gauche, entrées à
droite, posées sur une capture sous-marine.

**Son fond n'est pas une image, c'est la simulation.** L'œil est planté à six
mètres et demi de la coque, deux mètres sous la flottaison, et fait le tour en
moins de deux minutes ; la cale lâche vingt-six pièces toutes les 1,6 s, réglé
pour tenir sous le plafond de `CoinNode` (huit par seconde contre une vie
moyenne de trente-sept, soit trois cents en vol). Rien n'est arrêté derrière :
la mer travaille, la coque roule. « Jouer » n'a donc qu'à rendre la caméra — le
monde est déjà chaud, il n'y a pas de chargement.

**Il ne redouble rien.** « Options » ouvre le menu d'Échap, qui existe et qui est
complet ; un second jeu de réglages aurait dérivé du premier en trois semaines.

**Il prend TOUTE l'entrée** : sans cela la barre et les canons répondraient
derrière lui. Les boutons, eux, sont des `Control` et ont la souris avant
`_UnhandledInput`. La souris ne fait pas son survol à elle : elle DÉPLACE le
choix des flèches, pour qu'il n'y ait jamais deux états sélectionnés.

**L'anglaise de la page sert de titre ici aussi** — Estonia, lue dans
`css/fonts` hors du projet Godot : une fonte, deux versions. Sa licence (SIL
OFL 1.1) voyage avec elle, dans les crédits, comme elle voyage dans la page.

**Trois pièges, tous relevés à la capture :**
- il se construisait AVANT la lecture de la ligne de commande, et son ouverture
  rallumait un panneau que `--masquer` venait d'éteindre. Il se construit
  maintenant en dernier, et ne s'ouvre que s'il doit s'ouvrir ;
- sa couche naissait visible : une capture ordinaire portait le titre par-dessus
  le jeu ;
- il est posé sous le masque de cinéma (couche −1) et non au-dessus : le texte
  tient dans le cadre du scope au lieu de déborder sur les bandes.

`--titre 0` entre droit dans le jeu, `--titre 1` le force même en capture ;
une capture ou une caméra imposée (`--eye`) le sautent d'elles-mêmes.

## Le monde porté (Godot)

La terre était le dernier gros morceau qui n'existait que dans la page. Elle
tient en deux fichiers — `world/caraibes.json` et une image de relief de neuf
millions de pixels — et en une règle : **fonction pure de la position, en mètres
VRAIS**, jamais dans les coordonnées locales du rendu.

**Tout world.js est parti au NOYAU**, parce que tout y est arithmétique : le
gris décodé en mètres, le dragage d'un bassin, le môle qui EST de la terre, le
champ de distance au rivage (Felzenszwalb et Huttenlocher), les ports que la
fiche fait naître en cherchant le rivage le long d'un relèvement. Seul le
dessin est resté dehors. Une sixième part de parité le vérifie : **8 ports, 1360
points de relief, pire écart 5,9e-9**.

**IL A FALLU DEUX DÉCODEURS PNG**, un en Node pour le relevé, un en C# pour le
banc et pour le jeu — sinon les deux côtés compareraient deux reliefs au lieu de
deux formules. Le relevé porte donc une EMPREINTE du gris (la somme des neuf
millions d'octets, plus 512 sondes) : si les décodeurs divergent, le banc le dit
là, au lieu de laisser croire à une divergence de calcul. Celui du C# sert aussi
au jeu — Godot sait lire un PNG, mais pas celui que le banc a vérifié.

**Un seul écart de formule, et il valait le détour** : `toFixed` de JS arrondit
au plus loin de zéro, `F1` de .NET arrondit au pair. 56,25 minutes de latitude
sortaient à 56,2 là où la page écrit 56,3 — la position de Port-Royal, sur
l'écran du navigateur.

**Et trois pièges côté Godot, tous relevés à la capture :**
- `new Color(0xc9, 0xb1, 0x83)` : le constructeur de Godot prend des
  FLOTTANTS, donc 0xc9 y vaut 201. La côte sortait d'un blanc parfait, sable et
  forêt confondus. `Color.Color8` ;
- les couleurs de sommet sont prises pour LINÉAIRES : les teintes de la page
  sont en sRGB, et sans la conversion la côte est délavée ;
- `SurfaceTool` pour les normales était un aller-retour de trop ; le maillage
  est livré complet d'un coup, normales comprises, par la fonction du noyau qui
  sert déjà à la voile et au pavillon — computeVertexNormals de three.js, et il
  n'y en a qu'un.

Le chargement entier — lire l'image, la décoder, en tirer le champ de distance,
faire naître les huit ports — prend **une centaine de millisecondes** au
démarrage, une fois.

**L'échouage n'a demandé qu'une ligne**, et c'est la preuve que le découpage
était juste : `IGround` attendait dans le noyau depuis le portage du solveur,
avec la bonne signature, et personne ne l'implémentait. `World : IGround` a
suffi. La MÊME `HeightAt` sert au dessin de la côte, aux trois sondes de la
quille et au bassin dragué : elle ne peut pas talonner sur un haut-fond qu'on ne
voit pas.

Mesuré : cap sur la plage, machine à fond, elle touche à dix secondes, **échouée
de 0,15 m**, et sa vitesse tombe de 1,7 à 0,1 m/s ; à vingt secondes elle est
enfoncée de 0,20 m et n'avance plus, machine toujours à un. Le fond la retient,
et rien nulle part ne DÉCIDE qu'elle est échouée — ce sont les forces, comme
rien ne décide qu'elle flotte.

**Elle démarre à son poste** (`Berth`, dans le noyau avec les cotes du ponton,
que le ponton lira) : l'origine flottante se place sur le poste, la coque reste
à zéro. Port-Royal, 11 m d'eau — le bassin dragué. Le tableau de bord porte
maintenant le fond sous la quille, et « ÉCHOUÉE · x m dans le fond » quand elle
talonne, comme le bandeau d'avarie de la page.

**Un défaut de la page trouvé en la portant** (signalé à l'écran : un
quadrillage sombre en travers du paysage). Les triangles de la JUPE d'un carreau
sont verticaux ; moyennés avec ceux du dessus par `computeVertexNormals`, ils
couchent la normale de chaque sommet de bord et posent une bande d'ombrage le
long de toutes les coutures. Les normales se calculent donc sur la surface
SEULE, et la jupe reçoit ensuite celle de son original — elle est là pour
boucher une fente, pas pour être éclairée pour elle-même. Corrigé des deux
côtés, `js/land.js` compris.

**LA JUPE DES CARREAUX EST PARTIE**, des deux côtés, et c'est le plus instructif
de la journée. Signalée à l'écran comme « un souci d'affichage » : un réseau de
rubans en travers du paysage, et des langues de terre qui traversaient la mer.

Trois essais pour la coincer, chacun écartant une hypothèse :
- les NORMALES d'abord — les triangles verticaux de la jupe couchent celles des
  sommets de bord. Corrigé (surface seule, la jupe reprenant la normale de son
  original), et les bandes sont passées de sombres à CLAIRES : symptôme changé,
  cause intacte ;
- le LOD ensuite : tous les carreaux forcés à la même finesse donnent la même
  image. Ce n'était donc pas le raccord fin/grossier ;
- la jupe peinte en ROUGE : toutes les bandes sont devenues rouges. Fin du
  doute.

Ce qu'elle a de vicieux : un rideau qui pend sous l'arête d'un carreau ne PEUT
PAS ne pas se voir. Vu depuis le bas d'une pente, il masque le pied du versant
d'en face sur toute sa hauteur — trente mètres à trois kilomètres font six
pixels. Et sur la mer, il se voyait par transparence, l'eau montrant ce qui est
dessous.

Elle ne servait qu'aux fentes entre un carreau fin et un grossier, or cette
frontière est à 4,5 km, où la brume éteint déjà **98 %** du contraste
(1 − exp(−0,00085 × 4500)) : rien ne s'y voit, pas même une fente. Deux carreaux
de même finesse partagent leur arête au bit près et n'ont jamais eu de fente
entre eux. Retirée ici et dans `js/land.js`.

**Le mémento des commandes (F1)**, demandé avec le retrait de ce que le tableau
de bord portait. Les deux lignes de touches qui couraient d'un bord à l'autre de
l'image sont devenues un panneau en trois colonnes, avec les sections du
`#keysMenu` de la page — Manœuvre, Artillerie, Avaries, Le temps qu'il fait,
Rencontres, Vues, Affichage. Un instrument qu'on lit d'un coup d'œil ne peut pas
être aussi le mode d'emploi. Il ne reste au bas des instruments qu'une ligne :
F1, la vue où l'on est, et si la météo se conduit seule. Les intitulés prennent
l'anglaise du projet, les touches la chasse fixe — une cursive ne se balaie pas.

Piège : le tableau de bord est dessiné PAR-DESSUS ce panneau (l'ordre des
enfants du calque en décide, et il y est depuis le début). Plutôt que de jouer
avec cet ordre, les instruments se rangent pendant qu'on lit la notice, et leur
état d'avant leur est rendu à la fermeture — sinon H perdrait son effet.

**DES VILLES, ce que la page n'a jamais eu** (demandé : des maisons à l'échelle
du navire sur la bande de Port-Royal et sur la côte de Kingston). Elle n'avait
que des modèles posés un à un ; ici le semis est une fonction pure du relief et
d'une graine, dans le noyau — les maisons sont donc les mêmes d'une partie à
l'autre, et la carte marine pourra les connaître.

Les règles sont celles d'un lieu habité de cette côte : sur le PLAT (moins d'un
sur quatre, mesuré sur douze mètres, la largeur d'une maison), à plus de 1,2 m
au-dessus de l'eau, entre 11 et 700 m du rivage, façade vers la rade — le
gradient du champ de distance donne la direction de la rue, et une maison sur
six se met en travers, parce qu'une ville n'est pas un régiment. Le semis part
du port EN SPIRALE et se vide vers les bords : un tirage uniforme dans un disque
donne une banlieue, pas un port.

À l'échelle, et c'est tout l'intérêt : 5 à 9 m de large, 3 à 6 m au mur, un
entrepôt jusqu'à 16 × 21, quand la coque fait 28 m. Deux maillages multipliés —
une boîte, un prisme —, teintés par leur couleur d'instance : 1 500 maisons
coûtent deux appels de dessin par ville et **119 ms** à semer, une fois.

Deux corrections vues à la capture : le toit se mesure sur la LARGEUR et non en
mètres absolus (le même nombre de mètres donne une casquette à une halle et un
clocher à une masure), et une maison se pose sur son point le PLUS BAS — lue au
centre, elle était en porte-à-faux dès que le terrain penchait.

Port-Royal n'obtient que 59 maisons sur les 150 demandées : sa langue de sable
est trop étroite pour davantage. C'est le semis qui le dit, et c'est juste.

Reste à porter : le ponton et le môle, l'abri dans les trois calculateurs de la
mer, les modèles posés, la carte et les quêtes.

## La latine d'artimon (Godot et page)

Demande : rendre jouable la voile latine que portent à l'artimon la
frégate 17e, le pirate et la frégate. Choix du joueur : **l'équipage la règle
seul**, aucune touche de plus — elle monte et descend avec le reste (V).

**Physique (partagée, parité tenue).** Le solveur n'avait qu'une aile
équivalente. La latine devient une seconde aile, EN LONG : `rig.lateen`
(surface prise sur `sailArea`, son propre centre de voilure loin sur
l'arrière), même profil `SailFoil`, écartée de l'axe de l'incidence optimale
(`OptimalAoA`, de 0,08 rad à `maxSheet`). `LateenAngle` dit où l'équipage l'a
mise ; `LateenUp` (dit par l'hôte : le solveur ne sait pas quel mât porte
quoi) l'éteint quand son mât tombe. Fiches : 45 m² à 7,5 m / −10 m pour la 17e
et le pirate, 245 m² à 16 m / −23 m pour la frégate.

**Ce que le labo a montré (`latine`).** Au près, AVEC 4,83 nd, SANS 5,08 : elle
ne fait pas aller plus vite — le solveur laissait déjà brasser les carrés
jusqu'à l'axe, comme des voiles en long. Ce qu'elle apporte est l'**équilibre** :
barre −0,09 contre −0,26 sans elle ; centrée (`ceZ` au maître-couple), on
retombe sur SANS. C'est d'ailleurs pour cela qu'on la portait. Une butée de
brasseyage (`minSheet` 0,45) rendait au carré sa mauvaise remontée au vent,
mais coûtait 13 % au près sur toutes les fiches : retirée des fiches, gardée
dans le code (0 par défaut).

**Le dessin (`ShipNode.Rig.cs`, `LateenOn`).** Sur les trois modèles,
l'antenne est une pièce à part (`Cylinder_003`), **déjà écartée** de 23 à 27°
autour du mât — large en travers : un filtre « mince en x » la rejetait, et
un premier filtre sans le pont prenait le safran. On la retient si elle est au
pont ou au-dessus, longue (≥ 0,15 L), près de l'artimon, en biais. Pic = son
bout le plus haut, amure = l'autre, écoute sous le pic à 1,6 m au-dessus du
pont ; la toile est un triangle taillé comme un foc, dans le plan de l'antenne
modelée. Antenne et toile tournent d'un bloc autour de l'axe du mât, de
`angle voulu − angle modelé` : vent portant, 83° (la butée). La page ne la
dessine pas encore.


## Le panneau Flotte (Godot)

⇧N (toutes les lettres étaient prises ; N change de navire) : le panneau de la
page — les coques à flot et leur vitesse (« coulé » pour une épave), une croix
pour en retirer une, une liste des fiches et « Mettre à l'eau ». Rien de neuf
dessous : `SpawnFleet` met à l'eau entière (profil, gerbes, humeur de pirate si
tête de mort), `RemoveShip` retire et réécrit les profils ; `LaunchBeside` ne
fait que POSER, en éventail à 2,4 fois les deux longueurs, au cap du navire
commandé. La liste n'est refaite que quand la flotte change (sinon les boutons
sous le pointeur disparaissent) ; les fiches sont lues après la construction
des panneaux, la liste de choix se remplit donc à la première ouverture.

**Posée à terre, une caisse s'envole.** Premier essai au port : la caisse
(0,2 t) montait à dix-sept mètres. Seule, elle flottait sagement (force 0 et 4) ;
la cause était le fond — posée quarante-six mètres dans les terres, sur un sol
à +9,3 m. Une frégate se serait échouée ; une caisse qui déplace dix fois son
poids en est éjectée. L'éventail tourne donc par huitièmes, puis s'éloigne
(×2, ×3), jusqu'à trouver 5 m d'eau ; faute de quoi la coque est retirée et on
le dit. Aussi : la pose prend la hauteur de la mer À L'ENDROIT (`Sample`),
l'équilibre de `Settle` étant pris sur une mer aplatie. **La page a le même
défaut** (`launch()` ne regarde pas le fond).

## Deux bordées couchaient une frégate : le trou, le charpentier, le bord

Signalé : deux bordées reçues, et la Roter Löwe d'en face se couchait sur le
flanc. Le labo (`bordee`, 24 coups au flanc de la frégate 17e, mer calme) :
chaque boulet de plein calibre ouvrait **0,1 m²**, un trou de 36 cm ; à ce
compte elle coulait en moins de deux minutes, ses pompes (2,6 m³/min)
valant un centième de ce qui entrait. Couchée plutôt que coulée droite : les
compartiments à moitié pleins (carène liquide) laissent l'eau courir au bord
bas et y rester — la houle choisit le bord.

Or les vaisseaux de l'époque encaissaient des centaines de coups sans sombrer :
des trous petits, presque tous au-dessus de l'eau, et **le charpentier** qui y
enfonçait des tampons coniques. Trois changements, noyau et page :

- **Le trou à la taille du boulet** : 0,025 m² × k² (un douze livres fait
  12 cm, 0,011 m², plus le bois arraché autour).
- **Le charpentier** (`Plug`, `plugEvery` 40 s, `plugMax` 0,12 m²) : une
  brèche bouchée à la fois, celle qui fait entrer le plus d'eau au moment où le
  tampon est prêt (aire × √charge). Rien au-delà de 0,12 m² (échouage, soute),
  rien sans équipage (pompes soufflées) ni sur une épave.
- **Le bord** : une brèche est posée au bordé du côté touché (`X`), sa charge
  se lit là. Ce que cela change : un trou du bord qui s'enfonce embarque plus.
  Ce que cela NE change pas, et il faut le savoir : l'eau d'un compartiment
  reste répartie sur toute sa largeur (une cale ouverte se remplit d'un bord à
  l'autre en quelques secondes), donc le bord touché ne fait pas gîter à lui
  seul ; c'est toujours la carène liquide qui choisit.

Mesuré, coups tirés de 30 cm sous l'eau au pont (11 sur 24 sous la flottaison) :

| trou | charpentier | eau à 3 min | à 10 min |
|---|---|---|---|
| 0,10 m² | non | 640 t, coulée | — |
| 0,10 m² | oui | 383 t | coulée à 5 min |
| 0,025 m² | non | 44 t | coulée vers 10 min |
| 0,025 m² | oui | 26 t | 24 t, 10 brèches, les pompes gagnent |

Parité : le scénario `envahissement` porte deux trous de boulet, un par bord,
et un tampon toutes les 4 s pour que le charpentier travaille dans l'essai.

**Et les marques ne se voyaient pas** (signalé : « les impacts doivent marquer la coque »). La passe s'affichait bien (peinte en rouge, elle couvrait la silhouette). Mais une marque ne paraissait qu'au-delà d'un poids de 2, et un coup en ajoute 2 × k : les demi-calibres de la Roter Löwe (k = 0,5) en ajoutent 1, et il fallait trois boulets à moins de 85 cm. Relevé en combat réel : 6 coups au but, 6 marques de poids 1, toutes invisibles. Le premier boulet laisse maintenant une éraflure (seuil 0,5 ; stades à 1,5, ~4 et ~6,5 ; opacité pleine dès le poids 1), dans `ship_scar.gdshaderinc` et dans le shader de la page — la même règle aux deux endroits. Piège de mesure : au début d'une partie les pièces sont en rechargement (26 s) ; une bordée tirée avant ne part pas.

## Seize coques (Godot)

Demandé : monter la flotte de huit à seize. Le plafond vivait à quatre endroits
— `Config.MaxShips` (rangées de profils, sillages, panneau Flotte, fantômes),
`NSHIP` dans `hull_gap.gdshaderinc` (mer et écume), les tableaux de
`motion_blur.glsl` et `MotionBlurEffect.MaxShips` (qui lit maintenant
`Config`). Les boucles des shaders s'arrêtent à `u_ship_count` : le plafond ne
coûte rien, seules les coques présentes coûtent. La page garde huit.

Mesuré (frégates 17e mises à l'eau par `LaunchBeside`, `--vsync 0`, 20 s après
décantation, temps mural par image et chronos du moteur) :

| coques | ms/image | GPU | rendu CPU | solveurs | appels de dessin |
|---|---|---|---|---|---|
| 1 | 2,75 | 2,17 | 0,32 | — | — |
| 8 | 8,6 | 5,2 | 0,62 | 1,86 | 243 |
| 16 | 16,8 | 5,8 | 1,66 | 2,56 | 928 |

Seize tiennent à soixante images par seconde. La carte graphique n'est pas la
limite (5,8 ms) ; les solveurs non plus (2,6 ms, sur plusieurs cœurs). Il
reste environ une milliseconde par coque sur le fil principal, non encore
attribuée — `Performance.TimeProcess` échantillonné à la cadence du tableau de
bord tombe sur les images qui le mettent à jour, et ment (18 à 26 ms) ; à
chronométrer bloc par bloc dans `_Process` le jour où il faudra gagner.

En passant : `--flotte` désignait la frégate 17e par son NUMÉRO (5), que
l'ajout de la caisse avait fait glisser sur la grande frégate — cherchée
désormais par son fichier. Et l'éventail du panneau prend un cercle de plus
toutes les six coques : seize sur un seul cercle de 110 m se touchaient.

## Une église et deux maisons (Godot)

Demandé : trois .glb d'habitations, l'église une seule fois par ville. Écrits
par `tools/town-glb.js` — des boîtes, des toits à deux et à quatre pentes, une
flèche, des baies posées à deux centimètres du mur — dans `world/models`. Deux
cent cinquante sommets chacun, quatorze kilo-octets.

**Une ville reste un MultiMesh par matière**, et c'est tout le sujet : trois
cents maisons en nœuds séparés coûteraient trois cents dessins. Chaque .glb
apporte donc ses surfaces (une par matière), chacune devient un MultiMesh que
toutes les maisons de ce modèle partagent, et le nom de la matière décide du
traitement — `mur` prend la teinte d'instance, `fenetre` s'allume la nuit par
la règle de `town.gdshader` portée dans `town_glass.gdshader` (le tirage vient
de la position de l'instance, rien n'est stocké).

**À l'échelle de la parcelle, sans déformer** : le plus petit des deux rapports
(largeur, profondeur), sinon un bâtiment étiré dans un seul axe se lit comme un
décor. L'église va à la plus grande parcelle proche du centre (relevé :
15 × 20 m contre 7,5 m pour une maison ordinaire), ce qui lui donne dix mètres
de nef et son clocher à vingt.

Mesuré à Port-Royal, 150 bâtiments : **5,13 ms par image contre 4,80 en
boîtes**, 282 appels de dessin contre 203, 514 k triangles contre 385 k. Le
surcoût est d'un tiers de milliseconde ; les boîtes restent en repli si un
modèle manque.

## Un relief local, pour un port qui soit vrai

Signalé, en regardant le terrain exporté : « beaucoup de polygones
sous-exploités, cette carte reflétant la réalité est très pixelisée à cette
échelle ». Relevé : la grande image fait 2720 × 1580 px pour 122 km, soit
**45 m par pixel** ; le sol dessiné est bâti à cette résolution exactement
(carreau de 1440 m en 32 segments), et j'avais exporté le .glb au pas de 4 m —
cent vingt fois trop de triangles, qui n'interpolaient rien.

**Un patch de relief** : une seconde image, fine, cadrée en degrés sur un bout
de la région (`patches` dans la fiche). Le gris s'y lit par la MÊME loi et se
fond dans la grande sur `feather` mètres. Ce sont les GRIS qu'on mêle, pas les
hauteurs : une seule loi, et deux gris mêlés restent un gris. Port-Royal :
2048² px pour 2000 m, **0,98 m par pixel**, quarante-cinq fois plus fin.

Ce qu'il a fallu toucher : `World.Grey` des deux côtés (C# et page, parité
tenue avec 289 points relevés dans le patch et sur son bord), le chargement
(Godot, page, `build.js` qui l'embarque), et `LandNode`, qui bâtit désormais
un carreau aussi fin que sa source — `World.ReliefPx` le lui dit —, borné par
`FineMax` (288 segments, 5 m).

Trois outils : `relief-patch.js` (créer le patch, peint d'après l'existant, donc
sans rien changer), `zone-glb.js` (sortir le terrain pour Blender, au pas de la
source désormais) et `relief-bake.js` (rendre la sculpture dans l'image, par
rastérisation des triangles ; ce qui n'est pas couvert garde son gris).

Mesures : aller-retour export → cuisson, **3,6 cm d'écart moyen** (pire 9,9 m,
par cent mètres de fond, où un cran de gris vaut plusieurs mètres — la loi
donne ses nuances aux premiers mètres, et c'est voulu). Coût du sol fin à
Port-Royal : **4,26 ms/image et 1 169 k triangles, contre 3,12 ms et 514 k** —
une milliseconde pour quarante-cinq fois la finesse, et seulement là où un
patch existe.

## Une normal map de 4,2 Mo, et la coque restée plate

Blender exporte volontiers une carte de normales telle qu'elle est entrée :
celle du bois de la frégate sortait en **16 bits par canal, RGBA** — 1024×1024
pour 4,2 Mo. Une carte de normales n'a que trois canaux utiles et n'a jamais
demandé plus de huit bits : ramenée en 8 bits RVB elle tombe à 0,51 Mo, le
modèle de 6,7 à 3,0 Mo, et la page publiée de 18,7 à 13,6 Mo. Elle venait de
passer la limite des 16 Mo sans que rien ne le dise avant `node build.js` : le
poids des textures d'un `.glb` est le poste qui la fait franchir, et un seul
export distrait suffit.

Deuxième leçon, plus sournoise : la normal map rebranchée dans Blender l'avait
été sur le mauvais matériau — `wood001` et non `hull`. Le `.glb` le dit sans
ambiguïté (`materials[].normalTexture`), l'œil dans le jeu beaucoup moins : une
coque plate ressemble à une coque dont la carte est trop faible. Lire le JSON du
modèle avant de chercher un bogue dans le moteur.

## Les coques se traversaient (Godot)

À l'abordage, une étrave passait au travers du navire visé (signalé). Le
contact entre bordés n'était pourtant pas à écrire : `ShipPhysics.Collide` le
fait depuis la page — neuf stations par bord, un ressort qui porte tout le
poids de la coque à un tiers de mètre de chevauchement, et l'ouverture du
bordé au-delà de 2,2 m/s. Ce qui manquait tenait en un argument.

`Step()` prend les voisines EN PARAMÈTRE et ne les retient jamais — une liste
gardée se périme dès que la flotte change, ce qui a déjà coûté un bogue à la
page. Le portage appelait `Step(dt, mer, commandes, t)` sans le cinquième
argument : `others` était nul, `Collide` sortait à sa première ligne, et rien
ne le disait. La liste est donc refaite à chaque image, à côté de celle des
coques à faire avancer, et les spectres ont la leur — ils ne se cognent
qu'entre eux, et une étrave leur passe au travers, ce qui est le propre d'un
spectre.

**Et un banc qui ne regardait pas là.** Aucun des dix scénarios de parité
n'avait deux coques : le seul morceau du solveur que le portage avait laissé
muet était aussi le seul que personne ne mesurait. Un onzième les met **à
couple** — deux frégates bord à bord, 13,5 m d'écart pour 14,5 m de bau, donc
un mètre de chevauchement au maître-bau dès le premier pas. Elles s'écartent
de 13,5 à 23,0 m en cinq secondes, le chevauchement relevé culmine à 12,9 cm,
et les deux moteurs tiennent le même fil à **9·10⁻¹⁶ m**. Le cœur était juste ;
c'est bien le câblage qui manquait.

En parallèle (`ParallelSolvers`), une coque lit la pose d'une autre pendant
qu'elle s'écrit. Ce sont des doubles alignés, donc jamais un nombre à moitié
écrit : au pire une pose d'un sous-pas de retard, quelques millimètres, et le
ressort y perd un cheveu de sa symétrie. Séquentiellement, la page fait déjà
pareil — elle avance ses coques l'une après l'autre, et la seconde voit la
première déjà partie.

## Un outil pour les textures qui sortent de Blender

La leçon d'au-dessus s'est répétée à l'export suivant — la carte de normales
du bois était revenue en 16 bits, et une seconde l'avait rejointe pour les
cordages : 13,5 Mo de modèle, et une page très au-delà de ses 16 Mo. Ce qu'on
refait deux fois à la main se range dans `tools/` : **`glb-8bit.js`** relit
chaque PNG d'un `.glb`, ramène à huit bits ce qui est en seize, laisse tomber
un alpha qui ne sert à rien, et borne le côté sur demande
(`--max fabrics=512`). Il ne touche à rien d'autre : mêmes maillages, mêmes
matières, mêmes noms — à passer après chaque export.

Sur la frégate : bois 4,21 → 0,51 Mo, étoffes 6,14 → 2,45 Mo, le modèle de
13,5 à 6,2 Mo. Les étoffes restent lourdes parce qu'une carte bruitée ne se
compresse pas ; c'est à cela que sert `--max`, un cordage n'ayant pas besoin
de mille pixels de côté.

**Et une leçon payée cher** : un `git checkout` sur un fichier que l'on vient
de recevoir et qui n'a jamais été indexé l'efface sans retour. Un export
Blender de treize mégaoctets a disparu comme cela. Devant un binaire modifié
qu'on n'a pas soi-même écrit, on en fait une copie AVANT d'y toucher — ou on
l'indexe (`git add`), ce qui suffit à le mettre à l'abri.

## Chaque partie dans son menu (Godot)

Une sortie en jeu libre enregistrée depuis Échap se retrouvait dans la liste de
l'**Histoire** : le fichier ne disait pas d'où la partie venait, et la seule
liste qui existait était celle du chapitre (signalé). Une partie porte donc
désormais son `mode` — `histoire`, `mission` ou `libre` —, et chacun des trois
menus a sa liste, avec sa nouvelle partie et l'effacement en deux temps.

Deux choix qui méritent d'être écrits. **Trois menus, trois listes** : une
mission n'est ni l'Histoire ni le jeu libre, elle garde donc les siennes, sous
« Missions » — et sa quête repart où elle en était puisque le carnet de quêtes
voyage avec le fichier. **Les anciennes parties se rangent seules** : avant le
champ `mode`,
celles qui n'avaient pas de quête active étaient des parties libres, et leur
propre carnet (`"active": null`) le dit — aucune migration à écrire, huit
fichiers déjà sur le disque classés sans y toucher.

Et un menu ne montre sa liste que s'il a quelque chose à proposer : sans partie
enregistrée, le Jeu libre prend la mer tout de suite et « Missions » ouvre
directement le choix des missions, comme avant.

## Conventions

Interface et commentaires en français pour l'utilisateur ; commentaires de code
en anglais. Explication du *pourquoi*, pas du *quoi*.
