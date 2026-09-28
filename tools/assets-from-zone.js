/* CE QU'ON A POSÉ DANS BLENDER, RENDU AU JEU — le maillon qui manquait.
 *
 *   node tools/assets-from-zone.js                  (Port-Royal)
 *   node tools/assets-from-zone.js port-royal
 *   node tools/assets-from-zone.js port-royal --liste    (dire, sans écrire)
 *
 * `zone-glb.js` sort le terrain exact du jeu pour qu'on y bâtisse une ville à la
 * main. Restait à faire le chemin inverse : relever où l'on a posé chaque
 * bâtiment et l'écrire dans la fiche de région. Sans cet outil il fallait lire
 * des mètres dans Blender et les traduire en latitude et longitude à la main,
 * un bâtiment à la fois — hors de question pour une ville.
 *
 * CE QU'IL LIT : tout objet du `.glb` de zone qui n'est PAS le décor rendu par
 * l'export — `terre`, `mer`, `ponton`, `mole`. Chacun donne une entrée `assets`.
 *
 * LE NOM DE L'OBJET DIT LE MODÈLE. Un objet nommé `taverne` cherche
 * `world/assets/taverne.glb` ; `taverne.001`, `taverne.002` — ce que Blender
 * écrit quand on duplique — désignent le même. C'est ce qui permet de poser
 * cinquante maisons de trois sortes sans déclarer cinquante modèles : un .glb
 * par TYPE de bâtiment, posé autant de fois qu'on veut.
 *
 * IL COMPLÈTE, IL N'ÉCRASE PAS. Les entrées qu'il a écrites portent une marque
 * (`"from": "<lieu>-zone"`) : au passage suivant il remplace les siennes et
 * laisse intactes celles qu'on a écrites à la main. On peut donc relancer après
 * chaque séance de Blender sans rien perdre, et sans empiler les doublons.
 */
'use strict';
const fs = require('fs');
const path = require('path');

const root = path.join(__dirname, '..');
global.window = global;
eval(fs.readFileSync(path.join(root, 'js', 'world.js'), 'utf8'));
const { decodeGreyPng } = require('./grey-png.js');

const args = process.argv.slice(2).filter(a => a !== '--liste');
const liste = process.argv.includes('--liste');
const key = args[0] || 'port-royal';

/* LE DÉCOR DE L'EXPORT, qui n'est pas un bâtiment. Ce sont les quatre noms que
   zone-glb.js écrit lui-même ; tout le reste vient de vous. */
const DECOR = new Set(['terre', 'mer', 'ponton', 'mole']);

const region = JSON.parse(fs.readFileSync(path.join(root, 'world', 'caraibes.json'), 'utf8'));
const img = decodeGreyPng(fs.readFileSync(path.join(root, region.relief.image)));
const patches = (region.patches || []).map(p => {
  const f = path.join(root, p.image);
  return fs.existsSync(f) ? Object.assign({}, p, { img: decodeGreyPng(fs.readFileSync(f)) }) : null;
}).filter(Boolean);
const W = new Naval.World(region, img, patches);

const isle = W.isles.find(i => i.key === key);
const town = (region.towns || []).find(t => t.key === key);
if (!isle && !town) {
  console.error(`lieu inconnu : ${key}. Ports : ${W.isles.map(i => i.key).join(', ')}`);
  process.exit(1);
}
const at = isle ? { x: isle.x, z: isle.z, name: isle.name }
                : (() => { const p = Naval.Geo.toXZ(town.lat, town.lon);
                           return { x: p.x, z: p.z, name: town.name }; })();

/* ---------------------------------------------------------------- le .glb --- */
const glbPath = path.join(root, 'world', 'models', key + '-zone.glb');
if (!fs.existsSync(glbPath)) {
  console.error(`${glbPath} n'existe pas — lancez d'abord « node tools/zone-glb.js ${key} »`);
  process.exit(1);
}
const buf = fs.readFileSync(glbPath);
if (buf.readUInt32LE(0) !== 0x46546C67) { console.error('ce n est pas un .glb'); process.exit(1); }
let p = 12, js = null;
while (p + 8 <= buf.length) {
  const len = buf.readUInt32LE(p), type = buf.readUInt32LE(p + 4);
  if (type === 0x4E4F534A) { js = JSON.parse(buf.toString('utf8', p + 8, p + 8 + len)); break; }
  p += 8 + len;
}
if (!js) { console.error('pas de partie JSON dans le .glb'); process.exit(1); }

/* ------------------------------------------------------- lire les objets --- */
/* ON NE GARDE QUE CE QU'ON SAIT POSER : une translation, un cap, une échelle.
   Un bâtiment couché ou cisaillé n'a pas de place dans la fiche — on le dit
   plutôt que de le poser de travers en silence. */
const poses = [];
const warn = [];

function yawOf(q) {
  if (!q) return { yaw: 0, tilt: 0 };
  const [x, y, z, w] = q;
  return { yaw: -2 * Math.atan2(y, w) * 180 / Math.PI, tilt: Math.hypot(x, z) };
}

function walk(i, px, pz, pyaw, pscale) {
  const n = js.nodes[i];
  const t = n.translation || [0, 0, 0];
  const { yaw, tilt } = yawOf(n.rotation);
  const sc = n.scale ? (n.scale[0] + n.scale[1] + n.scale[2]) / 3 : 1;
  /* La position d'un enfant tourne avec son parent ; à ce jour personne ne
     parente ses maisons, mais le jour où cela arrive il vaut mieux que ce soit
     juste que surprenant. */
  const a = -pyaw * Math.PI / 180;
  const x = px + (t[0] * Math.cos(a) - t[2] * Math.sin(a)) * pscale;
  const z = pz + (t[0] * Math.sin(a) + t[2] * Math.cos(a)) * pscale;
  const wyaw = pyaw + yaw, wscale = pscale * sc;

  const nom = (n.name || '').trim();
  const base = nom.toLowerCase().replace(/\.\d+$/, '');       // Blender : taverne.001
  if (nom && !DECOR.has(base) && n.mesh !== undefined) {
    if (tilt > 0.02) warn.push(`${nom} : incliné, seul son cap est gardé`);
    if (n.scale && Math.max(...n.scale) - Math.min(...n.scale) > 0.02)
      warn.push(`${nom} : échelle non uniforme, moyenne prise`);
    poses.push({ nom, base, x, z, yaw: wyaw, scale: wscale, y: t[1] });
  }
  for (const c of n.children || []) walk(c, x, z, wyaw, wscale);
}

const scene = js.scenes[js.scene || 0];
for (const i of scene.nodes) walk(i, 0, 0, 0, 1);

if (poses.length === 0) {
  console.log(`${at.name} : aucun bâtiment posé dans ${key}-zone.glb`);
  console.log(`  (on y cherche tout objet autre que ${[...DECOR].join(', ')})`);
  process.exit(0);
}

/* --------------------------------------------------------- les entrées --- */
const marque = key + '-zone';
const manquants = new Set();
const entrees = poses.map(b => {
  const g = Naval.Geo.fix(at.x + b.x, at.z + b.z);
  const glb = `world/assets/${b.base}.glb`;
  if (!fs.existsSync(path.join(root, glb))) manquants.add(glb);
  let yaw = b.yaw % 360; if (yaw < 0) yaw += 360;
  return { name: b.nom, glb, lat: +g.lat.toFixed(6), lon: +g.lon.toFixed(6),
           yaw: +yaw.toFixed(1), scale: +b.scale.toFixed(3), from: marque };
});

const parType = new Map();
for (const e of entrees) parType.set(e.glb, (parType.get(e.glb) || 0) + 1);
console.log(`${at.name} : ${entrees.length} bâtiment(s) posé(s), ${parType.size} type(s)`);
for (const [g, n] of parType) console.log(`  ${n} × ${g}${fs.existsSync(path.join(root, g)) ? '' : '   ← MANQUANT'}`);
for (const w of warn) console.log(`  ! ${w}`);
if (manquants.size)
  console.log(`\n  ${manquants.size} modèle(s) à fournir : le nom de l'objet dans Blender donne le fichier.`);

if (liste) { console.log('\n(--liste : rien n a été écrit)'); process.exit(0); }

/* --------------------------------------------- écrire dans la fiche --- */
/* LES SIENNES REMPLACÉES, LES VÔTRES INTACTES : on garde toute entrée qui ne
   porte pas la marque de ce lieu, et l'on récrit les autres. */
const gardees = (region.assets || []).filter(a => a.from !== marque);
const toutes = gardees.concat(entrees);

const lignes = toutes.map(a => {
  const bout = a.from ? `, "from": "${a.from}"` : '';
  const y = a.y !== undefined ? `, "y": ${a.y}` : '';
  return `    { "name": ${JSON.stringify(a.name)}, "glb": ${JSON.stringify(a.glb)},\n` +
         `      "lat": ${a.lat}, "lon": ${a.lon}, "yaw": ${a.yaw}, "scale": ${a.scale}${y}${bout} }`;
}).join(',\n');

const src = fs.readFileSync(path.join(root, 'world', 'caraibes.json'), 'utf8');
const bloc = `  "assets": [\n${lignes}\n  ]`;
const had = /\n {2}"assets": \[[\s\S]*?\n {2}\]/;
const out = had.test(src) ? src.replace(had, '\n' + bloc)
                          : src.replace(/\n\}\s*$/, `,\n${bloc}\n}\n`);
JSON.parse(out);                                   // jamais une fiche cassée
fs.writeFileSync(path.join(root, 'world', 'caraibes.json'), out);
console.log(`\n  ${gardees.length} entrée(s) gardée(s), ${entrees.length} récrite(s) dans world/caraibes.json`);
