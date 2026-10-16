# Le serveur des parties en ligne

Un seul fichier PHP (`api.php`), pour un hébergement mutualisé : PHP 7.4 ou plus,
PDO, rien à installer. Les parties et leurs comptes y sont gardés par joueur.

## Installer

0. Recopier `config.exemple.php` en `config.php` et le remplir. `config.php` n'est pas suivi
   par git : il porte le mot de passe de la base.
1. Déposer le dossier `serveur/` chez l'hébergeur, par exemple sous `www/naval/`
   (FTP ou gestionnaire de fichiers) : `api.php`, `config.php`, `.htaccess`,
   `donnees/.htaccess`.
2. Par défaut la base est un fichier SQLite dans `donnees/` : rien à créer. Le
   dossier doit être inscriptible par PHP (c'est le cas presque partout).
   Pour MySQL, changer `dsn`, `utilisateur` et `mot_de_passe` dans `config.php` ;
   les tables se créent au premier appel.
3. Vérifier, depuis ce poste :

   ```bash
   node serveur/essai.js https://votre-site.fr/naval/api.php
   ```

4. Mettre l'adresse dans `settings.json` du jeu :

   ```json
   "enLigne": { "url": "https://votre-site.fr/naval/api.php" }
   ```

   **HTTPS** de préférence : la clé du joueur passe dans la requête.

Vérifier aussi que `https://votre-site.fr/naval/donnees/parties.sqlite` et
`.../config.php` répondent « interdit » : sans `.htaccess` (serveur nginx), la base
serait téléchargeable ; il faut alors placer `donnees/` hors du dossier public et
changer le chemin dans `config.php`.

## Le joueur

Pas de compte ni de mot de passe : au premier passage par « Parties en ligne », le
jeu tire une clé de 256 bits, la garde dans `user://en-ligne.ini` avec le pseudo
choisi, et le serveur n'en garde que l'empreinte SHA-256. Pour jouer d'un autre
poste : « Copier ma clé » ici, « Coller une clé d'un autre poste » là-bas.
Clé perdue, parties perdues : il n'y a personne à qui la redemander.

## Ce qui est gardé

Table `parties` : la partie entière (`donnees`, le fichier tel que le jeu l'écrit),
et à part, pour s'en servir sans l'ouvrir, `nom`, `mode`, `region`, `navire`,
`ecrit` (l'heure du poste), `envoye` (l'heure du serveur, UTC) et `stats` :

| champ | ce que c'est |
|---|---|
| `milles` | chemin fait, traversées comprises |
| `ports` | les ports où l'on est entré (clés) |
| `coups` · `bordees` | pièces tirées · bordées entières |
| `coules` | navires coulés après avoir reçu au moins un de vos boulets |
| `naufrages` · `captures` | vos navires perdus · pris par les pirates |
| `poissons` · `kgPeche` | prises à la ligne, et leur poids |
| `lambis` · `perles` · `ecusRepeches` | ce que le nageur a remonté |
| `coffres` | coffres d'épave saisis à la cloche |
| `gains` | ce que ventes et contrats ont rapporté, en sous |
| `sous` · `ecus` · `navire` · `rang` · `jours` · `caleKg` · `tresors` · `pecheABord` | le relevé du moment de l'enregistrement |

Les compteurs partent de zéro pour les parties d'avant leur ajout.
Plafonds dans `config.php` : 4 Mo par partie, 50 parties par joueur.
