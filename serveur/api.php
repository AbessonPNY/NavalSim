<?php
/*
 * LES PARTIES EN LIGNE — the one entry point of the save server.
 *
 * Written for shared hosting: plain PHP (7.4+), PDO, no framework, no composer.
 * Every call is a POST of a JSON envelope { "a": action, "cle": key, ... } and
 * gets a JSON answer. The key travels in the body, never in a header or the
 * URL: shared hosts strip unknown headers behind FastCGI, and URLs end up in
 * access logs.
 *
 * Identity is a 256-bit random key drawn by the game on first use and kept in
 * its user:// folder. Only its SHA-256 is stored here, so a leaked database
 * does not let anyone write under someone else's name.
 *
 * Actions:
 *   etat                       -> { ok, version }                 (no key)
 *   joueur   { pseudo }        -> { ok, pseudo }   create or rename
 *   moi                        -> { ok, pseudo, parties }
 *   envoyer  { partie }        -> { ok, id, envoye }
 *   liste                      -> { ok, parties: [ {id, nom, mode, ..., stats} ] }
 *   partie   { id }            -> { ok, partie }
 *   effacer  { id }            -> { ok }
 */

declare(strict_types=1);

const VERSION = 1;

$config = require __DIR__ . '/config.php';

header('Content-Type: application/json; charset=utf-8');
header('Cache-Control: no-store');

function answer(int $code, array $body): void
{
    http_response_code($code);
    echo json_encode($body, JSON_UNESCAPED_UNICODE | JSON_UNESCAPED_SLASHES);
    exit;
}

function fail(int $code, string $why): void
{
    answer($code, ['ok' => false, 'erreur' => $why]);
}

if ($_SERVER['REQUEST_METHOD'] !== 'POST') fail(405, 'POST seulement');

$raw = file_get_contents('php://input', false, null, 0, $config['taille_max'] + 4096);
if ($raw === false || strlen($raw) > $config['taille_max'] + 4000) fail(413, 'partie trop lourde');
$req = json_decode($raw, true);
if (!is_array($req)) fail(400, 'JSON illisible');
$action = (string)($req['a'] ?? '');

if ($action === 'etat') answer(200, ['ok' => true, 'version' => VERSION]);

// ---------------------------------------------------------------- database

function db(array $config): PDO
{
    $dsn = $config['dsn'];
    if (strncmp($dsn, 'sqlite:', 7) === 0) {
        // say which of the two usual shared-hosting failures it is: the message names no path
        if (!in_array('sqlite', PDO::getAvailableDrivers(), true))
            fail(500, 'PHP sans pilote SQLite : passer à MySQL dans config.php');
        $dir = dirname(substr($dsn, 7));
        if (!is_dir($dir)) @mkdir($dir, 0700, true);
        if (!is_dir($dir) || !is_writable($dir))
            fail(500, 'le dossier donnees/ n\'est pas inscriptible par PHP');
    }
    $pdo = new PDO($dsn, $config['utilisateur'], $config['mot_de_passe'], [
        PDO::ATTR_ERRMODE => PDO::ERRMODE_EXCEPTION,
        PDO::ATTR_DEFAULT_FETCH_MODE => PDO::FETCH_ASSOC,
    ]);
    $mysql = $pdo->getAttribute(PDO::ATTR_DRIVER_NAME) === 'mysql';
    if ($mysql) $pdo->exec('SET NAMES utf8mb4');
    // a save is tens of kilobytes and grows with the chart: MEDIUMTEXT on MySQL, whose TEXT stops at 64 KB
    $big = $mysql ? 'MEDIUMTEXT' : 'TEXT';
    $tail = $mysql ? ' ENGINE=InnoDB DEFAULT CHARSET=utf8mb4' : '';
    $auto = $mysql ? 'INTEGER PRIMARY KEY AUTO_INCREMENT' : 'INTEGER PRIMARY KEY AUTOINCREMENT';
    $pdo->exec("CREATE TABLE IF NOT EXISTS joueurs (
        id $auto,
        pseudo VARCHAR(24) NOT NULL UNIQUE,
        cle CHAR(64) NOT NULL UNIQUE,
        cree VARCHAR(20) NOT NULL,
        vu VARCHAR(20) NOT NULL)$tail");
    $pdo->exec("CREATE TABLE IF NOT EXISTS parties (
        joueur INTEGER NOT NULL,
        id VARCHAR(32) NOT NULL,
        nom VARCHAR(200) NOT NULL,
        mode VARCHAR(16) NOT NULL,
        region VARCHAR(40) NOT NULL,
        navire VARCHAR(80) NOT NULL,
        ecrit VARCHAR(20) NOT NULL,
        envoye VARCHAR(20) NOT NULL,
        stats TEXT NOT NULL,
        donnees $big NOT NULL,
        PRIMARY KEY (joueur, id))$tail");
    return $pdo;
}

function now(): string
{
    return gmdate('Y-m-d H:i:s');
}

try {
    $pdo = db($config);
} catch (Throwable $e) {
    error_log('[naval] base : ' . $e->getMessage());
    // the driver's NUMBER only: the message itself may carry host and user names
    $code = $e instanceof PDOException ? (int)($e->errorInfo[1] ?? 0) : 0;
    // a failed connection has no errorInfo: its number is the exception code
    if ($code === 0 && is_int($e->getCode())) $code = $e->getCode();
    if ($code === 0 && $e instanceof PDOException && preg_match('/\[(\d{4})\]/', $e->getMessage(), $m)) $code = (int)$m[1];
    $why = [
        1044 => 'cet utilisateur n\'a pas droit à cette base',
        1045 => 'utilisateur ou mot de passe refusé',
        1049 => 'base inconnue : vérifier dbname',
        1142 => 'droit de créer les tables refusé',
        2002 => 'hôte de la base injoignable',
        2005 => 'hôte de la base inconnu',
    ][$code] ?? '';
    fail(500, 'base indisponible' . ($code ? " ($code" . ($why ? " : $why" : '') . ')' : ''));
}

// ---------------------------------------------------------------- identity

$cle = (string)($req['cle'] ?? '');
if (!preg_match('/^[0-9a-f]{64}$/', $cle)) fail(401, 'clé absente ou mal formée');
$hash = hash('sha256', $cle);

$st = $pdo->prepare('SELECT id, pseudo FROM joueurs WHERE cle = ?');
$st->execute([$hash]);
$me = $st->fetch() ?: null;

function pseudoOk(string $p): bool
{
    // letters (accents too), digits, space, dash, underscore, apostrophe; 3 to 24 characters
    return (bool)preg_match("/^[\\p{L}\\p{N} _'\\-]{3,24}$/u", $p) && trim($p) === $p;
}

if ($action === 'joueur') {
    $pseudo = (string)($req['pseudo'] ?? '');
    if (!pseudoOk($pseudo)) fail(400, 'pseudo : 3 à 24 lettres, chiffres, espaces ou tirets');
    $st = $pdo->prepare('SELECT id FROM joueurs WHERE LOWER(pseudo) = LOWER(?)');
    $st->execute([$pseudo]);
    $taken = $st->fetchColumn();
    if ($taken !== false && (!$me || (int)$taken !== (int)$me['id'])) fail(409, 'ce pseudo est déjà pris');
    if ($me) {
        $pdo->prepare('UPDATE joueurs SET pseudo = ?, vu = ? WHERE id = ?')->execute([$pseudo, now(), $me['id']]);
    } else {
        $pdo->prepare('INSERT INTO joueurs (pseudo, cle, cree, vu) VALUES (?, ?, ?, ?)')
            ->execute([$pseudo, $hash, now(), now()]);
    }
    answer(200, ['ok' => true, 'pseudo' => $pseudo]);
}

if (!$me) fail(403, 'joueur inconnu : choisissez d\'abord un pseudo');
$pdo->prepare('UPDATE joueurs SET vu = ? WHERE id = ?')->execute([now(), $me['id']]);
$who = (int)$me['id'];

function count_saves(PDO $pdo, int $who): int
{
    $st = $pdo->prepare('SELECT COUNT(*) FROM parties WHERE joueur = ?');
    $st->execute([$who]);
    return (int)$st->fetchColumn();
}

function idOk($id): bool
{
    return is_string($id) && preg_match('/^[0-9a-zA-Z_-]{1,32}$/', $id);
}

switch ($action) {
    case 'moi':
        answer(200, ['ok' => true, 'pseudo' => $me['pseudo'], 'parties' => count_saves($pdo, $who)]);

    case 'envoyer':
        $p = $req['partie'] ?? null;
        if (!is_array($p) || !idOk($p['id'] ?? null)) fail(400, 'partie sans identifiant');
        $id = $p['id'];
        $data = json_encode($p, JSON_UNESCAPED_UNICODE | JSON_UNESCAPED_SLASHES);
        if (strlen($data) > $config['taille_max']) fail(413, 'partie trop lourde');
        $stats = is_array($p['stats'] ?? null) ? $p['stats'] : new stdClass();
        $cut = fn($v, int $n) => mb_substr(is_string($v) ? $v : '', 0, $n);
        $row = [
            $cut($p['nom'] ?? '', 200), $cut($p['mode'] ?? '', 16), $cut($p['region'] ?? '', 40),
            $cut($p['navire'] ?? '', 80), $cut($p['ecrit'] ?? '', 20), now(),
            json_encode($stats, JSON_UNESCAPED_UNICODE), $data,
        ];
        $pdo->beginTransaction();
        $st = $pdo->prepare('SELECT 1 FROM parties WHERE joueur = ? AND id = ?');
        $st->execute([$who, $id]);
        if ($st->fetchColumn()) {
            $pdo->prepare('UPDATE parties SET nom = ?, mode = ?, region = ?, navire = ?, ecrit = ?, envoye = ?, stats = ?, donnees = ?
                           WHERE joueur = ? AND id = ?')->execute([...$row, $who, $id]);
        } else {
            if (count_saves($pdo, $who) >= $config['parties_max']) {
                $pdo->rollBack();
                fail(409, 'trop de parties en ligne : effacez-en une');
            }
            $pdo->prepare('INSERT INTO parties (joueur, id, nom, mode, region, navire, ecrit, envoye, stats, donnees)
                           VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?)')->execute([$who, $id, ...$row]);
        }
        $pdo->commit();
        answer(200, ['ok' => true, 'id' => $id, 'envoye' => $row[5]]);

    case 'liste':
        $st = $pdo->prepare('SELECT id, nom, mode, region, navire, ecrit, envoye, stats FROM parties
                             WHERE joueur = ? ORDER BY ecrit DESC');
        $st->execute([$who]);
        $out = [];
        foreach ($st->fetchAll() as $r) {
            $r['stats'] = json_decode($r['stats'], true) ?: new stdClass();
            $out[] = $r;
        }
        answer(200, ['ok' => true, 'pseudo' => $me['pseudo'], 'parties' => $out]);

    case 'partie':
        if (!idOk($req['id'] ?? null)) fail(400, 'identifiant mal formé');
        $st = $pdo->prepare('SELECT donnees FROM parties WHERE joueur = ? AND id = ?');
        $st->execute([$who, $req['id']]);
        $d = $st->fetchColumn();
        if ($d === false) fail(404, 'partie introuvable');
        // the stored text is already JSON: splice it in rather than decode and re-encode it
        http_response_code(200);
        echo '{"ok":true,"partie":' . $d . '}';
        exit;

    case 'effacer':
        if (!idOk($req['id'] ?? null)) fail(400, 'identifiant mal formé');
        $pdo->prepare('DELETE FROM parties WHERE joueur = ? AND id = ?')->execute([$who, $req['id']]);
        answer(200, ['ok' => true]);
}

fail(400, 'action inconnue');
