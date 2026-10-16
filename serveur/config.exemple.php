<?php
/*
 * Modèle de config.php : le recopier sous ce nom et le remplir.
 * config.php n'est pas suivi par git, il porte le mot de passe de la base.
 *
 * Par défaut, une base SQLite dans donnees/ (rien à créer chez l'hébergeur).
 * Pour MySQL :
 *   'dsn' => 'mysql:host=localhost;dbname=naval;charset=utf8mb4',
 * et remplir utilisateur et mot_de_passe.
 */
return [
    'dsn' => 'sqlite:' . __DIR__ . '/donnees/parties.sqlite',
    'utilisateur' => null,
    'mot_de_passe' => null,
    // une partie pèse quelques dizaines de Ko ; la carte la fait grossir
    'taille_max' => 4 * 1024 * 1024,
    'parties_max' => 50,
];
