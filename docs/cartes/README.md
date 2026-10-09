# Cartes d'époque

## Port-Royal, 1772

- `port-royal-1772.html` — le plan « Town and Harbour of Port Royal, 1772 » et la terre du jeu
  superposés, au calage réglable (nord, décalage, mille anglais ou marin). Servi par le serveur de
  dev : `http://localhost:8765/docs/cartes/port-royal-1772.html`.
- `port-royal-relief-calque-1772.png` — **le calque pour repeindre la tête de Port-Royal** : le
  même cadre que `world/port-royal-relief.png` (2048 × 2048, nord en haut, 0,977 m de jeu par
  pixel). Ouvrez le relief, posez ce calque au-dessus à demi-opacité, et repeignez le rivage
  dessous : gris 128 = la côte, plus clair = la terre (loi dans `world/README.md`).

**Calage** : la barre du plan (« Scale of half a Mile », 853 px) lue en mille anglais, 0,943 m par
pixel ; son nord vrai, le trait plein marqué « True North », 18,4° à droite du haut de la feuille
(le pointillé voisin est le nord magnétique) ; son Fort Charles (pixel 477, 690) posé sur le fort
du jeu (retouche `ajout:52`, x 45,5, z −72,8). **À l'échelle 1 en mètres de jeu** : la ville y
garde sa vraie taille, celle que demandent des maisons à taille réelle — alors que le reste de
l'île est à 0,4. Le relief actuel l'était déjà à peu près (une tache 2,5 fois trop grande,
réduite à 0,4) ; c'est sa FORME qui ne l'était pas.

**Pour 1690**, avant le séisme du 7 juin 1692 : la ville s'étendait aussi sur la partie hachurée
au nord-ouest, « Part of Old Port Royal sunk in 1692 » — la repeindre en terre.

Le relief repeint, le TERRASSEMENT (`world/terrassement/port-royal.bin`) s'y ajoute toujours :
ce qui était aplani pour la ville l'est par-dessus le nouveau rivage, à revoir où la côte bouge.
