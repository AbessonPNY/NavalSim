# Les marchandises

`market/marchandises.json` dit **ce qu'on charge et ce qu'on revend**. Les deux
moteurs le lisent : la page le reçoit embarqué par `node build.js`
(`Naval.GOODS_DATA`) ou le cherche sur le serveur de dev ; Godot le lit sur le
disque au démarrage du comptoir.

```json
{
  "marge": 0.12,
  "marchandises": [
    { "key": "sucre", "nom": "Sucre", "prix": 150, "note": "la grande denrée" }
  ]
}
```

| champ | rôle |
|---|---|
| `key` | le mot que porte la cale. Jamais montré au joueur ; c'est lui que nomment les quêtes (`cargo`) et les sauvegardes |
| `nom` | ce que le joueur lit |
| `prix` | le cours **moyen** de la tonne, en pièces d'argent (60 pour un écu) |
| `note` | pourquoi elle est là — s'affiche en infobulle au comptoir |
| `marge` | la marge du négociant, prise des deux côtés (racine du fichier) |

## Comment un cours se forme

Le cours vrai d'un port oscille entre **0,55 et 1,45** fois le prix moyen, en
une courbe continue propre au **couple marchandise/port**. C'est là tout le
commerce : le sucre peut être haut à Montego Bay quand l'indigo y est bas, et
c'est cet écart-là — et non le niveau général d'un port — qui fait choisir une
route.

C'est une **fonction pure** du couple et de l'heure : rien à tenir, rien à
charger, la même chose sur toutes les machines et d'une partie à l'autre. Le
palier se recale sur la plus longue traversée du monde, si bien qu'un cours tient
le temps d'y aller.

Le négociant prend sa marge **des deux côtés**, donc un aller-retour à vide entre
deux ports au même cours **perd** de l'argent.

## Ce qui n'est pas là

Une marchandise **absente de la liste se porte mais ne se vend nulle part**.
C'est le cas des `vivres` et de la `viande` d'un fret sous contrat : on les
livre, on ne les brade pas. La cale, elle, ne connaît que des mots — elle porte
ce qu'on lui donne.

C'est aussi ce qui règle le pillage : un pirate emporte tout ce qui a un cours,
et laisse le reste.

## Ajouter une denrée

Une ligne, et rien à écrire dans le code. Le comptoir bâtit ses lignes d'après la
fiche, au démarrage. Après un ajout :

```bash
node build.js                                  # la page réembarque la fiche
node tools/parity-market.js                    # le relevé du commerce
dotnet run --project core/NavalSim.Parity      # et les deux moteurs se comparent
```

## Le choix des denrées

Celles de la **Jamaïque de 1690** : le sucre est la grande denrée, l'indigo le
plus cher au poids, le gingembre et le poivre de Jamaïque sont des exports
propres à l'île, le bois de campêche se coupe souvent illicitement dans la baie
de Campeche, et les cuirs sont ce que les boucaniers vendaient avant de se faire
pirates.

**Le café n'y est pas** : il n'arrive à la Jamaïque qu'en **1728**, trente-huit
ans après le début de la partie. Il se négociait bien en 1690 — le moka par le
Levant et les Hollandais — mais pas depuis Port-Royal.

## Le chantier (Godot)

`market/chantier.json` : les ports qui ont un chantier, et les fiches de
`ships/` qu'on y vend. Un navire se paie à la tonne de **déplacement**
(`ecusParTonne`), et le chantier reprend le vôtre à `reprise` de son prix :
on échange, on n'a jamais deux coques. Descendre vers plus petit rapporte la
différence. La section paraît au comptoir, à quai, dans un port qui a un
chantier ; la cale et les prises passent dans la nouvelle coque (ce qui n'y
tient pas reste au quai), la bourse et le rang restent au capitaine.

À 1,5 écu la tonne, le cotre Vigie (100 t) coûte 150 écus, moins 16,5 pour le
sloop : cinq à six bonnes sorties de pêche.
