# Navires ajoutés par des mods

Un dossier par navire, comme ceux du jeu (`ships/<id>/`) :

```
mods/navires/
  la-sirene/
    fiche.json      ← l'identifiant du navire est le nom du dossier : « la-sirene »
    modele.glb      ← « "model": { "glb": "modele.glb" } » dans la fiche
    textures/       ← ce qui lui est propre ; un chemin sans dossier se lit ici
```

Le jeu lit ses navires, PUIS ceux d'ici : un mod ne décale pas les navires du jeu, et ne peut pas
reprendre l'identifiant d'un des leurs (il est écarté, avec un mot dans la console). Retirer le
dossier retire le navire.

**Créer** : `node tools/add-ship.js chemin/la-sirene.glb --nom "La Sirène" --mod`.
**Caler** la fiche sur le modèle (flottaison, poids, quille, pont, mâts) :
`node tools/fit-ship.js la-sirene --ecrire`.

Le format complet d'une fiche est dans `ships/README.md`. Pour qu'un navire de mod soit
proposé en jeu libre ou vendu au chantier, il faut le nommer dans `ships/libre.json` ou
`market/chantier.json` ; sinon il existe (le mode débug l'ouvre) sans être offert.
