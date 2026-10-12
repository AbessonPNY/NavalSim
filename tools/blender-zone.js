/* L'ALLER-RETOUR AVEC BLENDER — disposer un port dans Blender, et que le jeu suive.
 *
 *   node tools/blender-zone.js sortir  [port] [côté] [pas]   le jeu → world/blender/<port>.blend (côté : celui du port)
 *   node tools/blender-zone.js rentrer [port] [--essai]       le .blend → world/retouches/<région>.json
 *   node tools/blender-zone.js palette [port]                 les modèles bruts → world/blender/palette.glb
 *   node tools/blender-zone.js terrain [port]                 le relief changé → la terre du .blend, objets gardés
 *
 * SORTIR lance le jeu, qui charge le port et écrit world/blender/<port>.glb — le
 * terrain tel qu'il le lit (terrassement et sol peint compris), chaque objet nommé
 * par sa retouche, une palette des modèles bruts — et son manifeste ; Blender en fait
 * <port>.blend. Un .blend existant n'est JAMAIS écrasé (c'est votre travail) : sans
 * --ecraser, l'outil s'arrête.
 *
 * RENTRER fait relever le .blend par Blender (sans fenêtre), puis lance le jeu, qui
 * applique ce relevé par le registre du mode création et écrit les retouches comme si
 * on avait tout fait à la souris. --essai : le jeu dit ce qu'il ferait, n'écrit rien.
 *
 * Dans Blender : déplacer, tourner (autour de la verticale), mettre à l'échelle ;
 * Maj+D duplique (une copie dans le jeu) ; Suppr retire ; un double d'un modèle de la
 * palette, posé dans la zone, devient un ajout. Ne pas renommer les objets : le nom est
 * la clé. Un NOUVEAU modèle (déposé dans world/models ou props) : « palette », puis dans
 * Blender Fichier › Importer › glTF › world/blender/palette.glb ; les doubles d'une
 * entrée déjà présente prennent un « .001 », c'est sans conséquence. Un fichier
 * importé tel quel n'est pas reconnu : son unité et son nœud racine varient.
 * Format et raisons : world/README.md, « Disposer un port dans Blender ».
 *
 * Exécutables : NAVAL_GODOT et NAVAL_BLENDER, sinon les emplacements connus.
 */
'use strict';
const fs = require('fs');
const path = require('path');
const { spawnSync } = require('child_process');

const root = path.join(__dirname, '..');
const dir = path.join(root, 'world', 'blender');
const args = process.argv.slice(2).filter(a => !a.startsWith('--'));
const flags = new Set(process.argv.slice(2).filter(a => a.startsWith('--')));
const verb = args[0];
const port = args[1] || 'port-royal';

function first(list, what, env) {
  if (process.env[env]) return process.env[env];
  const f = list.find(p => fs.existsSync(p));
  if (!f) { console.error(`${what} introuvable : donnez son chemin dans ${env}`); process.exit(1); }
  return f;
}
const godot = () => first([
  'D:\\Godot_v4.7.2-stable_mono_win64_console.exe',
  'D:\\Godot_v4.7.2-stable_mono_win64.exe'
], 'Godot', 'NAVAL_GODOT');
const blender = () => first([
  'C:\\Program Files\\Blender Foundation\\Blender 5.0\\blender.exe',
  'C:\\Program Files\\Blender Foundation\\Blender 4.3\\blender.exe',
  'C:\\Program Files (x86)\\Steam\\steamapps\\common\\Blender\\blender.exe'
], 'Blender', 'NAVAL_BLENDER');

function run(exe, argv, label, done) {
  console.log(`— ${label}`);
  const r = spawnSync(exe, argv, { cwd: root, encoding: 'utf8', maxBuffer: 1 << 28 });
  const out = (r.stdout || '') + (r.stderr || '');
  /* CE QUI NOUS CONCERNE SEULEMENT. Le bruit connu se tait : Godot qui, en quittant,
     compte les ressources qu'il n'a pas rendues (« RID allocations … leaked ») et ses
     piles d'appel ; V-Ray, qui refuse de démarrer sans fenêtre. Ni l'un ni l'autre
     n'est un échec, mais en rouge ils passaient pour un. Le journal entier, au besoin :
     world/blender/dernier-passage.log. */
  const noise = /leaked at exit|PagedAllocator|never freed|still in use at exit|free_rid|framebuffer cache|V-Ray|is_inside_tree|^\s*\[\d+\] |^\s*at /;
  for (const l of out.split(/\r?\n/))
    if (/\[blender\]|^\s+[~+\-?!] |naval:|ERROR|Error:|Traceback/.test(l) && !noise.test(l)) console.log('  ' + l.replace(/\x1b\[[0-9;]*m/g, '').trim());
  fs.mkdirSync(dir, { recursive: true });
  fs.appendFileSync(path.join(dir, 'dernier-passage.log'), `\n===== ${label}\n${out}`);
  /* LE JEU PEUT TOMBER EN QUITTANT (accès mémoire au moment de tout rendre, vu une fois
     sur Passage Fort) APRÈS avoir écrit son résultat : c est sa ligne [blender] qui dit
     si le travail est fait, pas le code de sortie. */
  if (r.status !== 0) {
    if (done && done.test(out)) console.log(`  (le programme est tombé en quittant, code ${r.status}, après avoir fini : sans conséquence)`);
    else { console.error(`${label} : échec (code ${r.status}) — sortie complète dans world/blender/dernier-passage.log`); process.exit(1); }
  }
}

const glb = path.join(dir, port + '.glb');
const blend = path.join(dir, port + '.blend');
const relevé = path.join(dir, port + '-blender.json');

// un journal par passage, pas un journal qui grossit sans fin
if (['sortir', 'rentrer', 'palette', 'terrain'].includes(verb)) { fs.mkdirSync(dir, { recursive: true }); fs.writeFileSync(path.join(dir, 'dernier-passage.log'), ''); }

if (verb === 'sortir') {
  if (fs.existsSync(blend) && !flags.has('--ecraser')) {
    console.error(`${blend} existe déjà — c'est votre travail, je ne l'écrase pas.\n` +
                  `Pour repartir du jeu : rentrez d'abord vos changements, puis relancez avec --ecraser.`);
    process.exit(1);
  }
  const zone = [port, args[2], args[3]].filter(Boolean).join(',');
  run(godot(), ['--path', path.join(root, 'godot'), '--', '--zone-blender', zone, '--vsync', '0'], 'le jeu charge le port et l’exporte', /\[blender\] .*\.glb : /);
  if (!fs.existsSync(glb)) { console.error(`${glb} n'a pas été écrit`); process.exit(1); }
  run(blender(), ['--background', '--factory-startup', '--python', path.join(__dirname, 'blender', 'zone_ouvrir.py'), '--', glb, blend],
      'Blender en fait un .blend');
  console.log(`\n${blend}\n  à ouvrir dans Blender ; puis « node tools/blender-zone.js rentrer ${port} »`);
} else if (verb === 'terrain') {
  /* LE RELIEF A CHANGÉ (repeint dans Photoshop, terrassé dans le jeu) : seules la terre et
     la mer du .blend sont remplacées, vos objets restent où vous les avez mis. Une copie du
     .blend est gardée avant, et Blender doit être FERMÉ sur ce fichier : sinon votre
     prochain enregistrement y remettrait l'ancienne terre. */
  if (!fs.existsSync(blend)) { console.error(`${blend} n'existe pas — « node tools/blender-zone.js sortir ${port} » d'abord`); process.exit(1); }
  const terre = path.join(dir, port + '-terre.glb');
  const copie = path.join(dir, port + '.avant-terrain.blend');
  fs.copyFileSync(blend, copie);
  console.log(`— copie de sécurité : ${copie}`);
  run(godot(), ['--path', path.join(root, 'godot'), '--', '--zone-terrain', port, '--vsync', '0'], 'le jeu relit le relief et écrit la terre', /\[blender\] .*-terre\.glb : /);
  run(blender(), ['--background', blend, '--python', path.join(__dirname, 'blender', 'zone_terrain.py'), '--', terre],
      'Blender remplace la terre et la mer dans le .blend', /naval: terre et mer remplacées/);
  console.log(`\n${blend}\n  terre neuve sous vos objets ; si quelque chose cloche, la copie d'avant est ${path.basename(copie)}`);
} else if (verb === 'palette') {
  run(godot(), ['--path', path.join(root, 'godot'), '--', '--zone-palette', port, '--vsync', '0'], 'le jeu écrit la palette', /\[blender\] .*palette\.glb : /);
  console.log(`
${path.join(dir, 'palette.glb')}
  dans Blender : Fichier › Importer › glTF 2.0 ; puis Maj+D sur un modèle, et le poser dans la zone`);
} else if (verb === 'rentrer') {
  if (!fs.existsSync(blend)) { console.error(`${blend} n'existe pas — « node tools/blender-zone.js sortir ${port} » d'abord`); process.exit(1); }
  run(blender(), ['--background', '--factory-startup', blend, '--python', path.join(__dirname, 'blender', 'zone_relever.py'), '--', relevé],
      'Blender relève le .blend');
  run(godot(), ['--path', path.join(root, 'godot'), '--', '--zone-retour', port + (flags.has('--essai') ? ',essai' : ''), '--vsync', '0'],
      flags.has('--essai') ? 'le jeu compare (essai : rien d’écrit)' : 'le jeu applique et écrit les retouches', /\[blender\] retour de /);
} else {
  console.log(fs.readFileSync(__filename, 'utf8').split('*/')[0]);
  process.exit(verb ? 1 : 0);
}
