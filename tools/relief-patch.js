/* UN PATCH DE RELIEF : une seconde image, fine, sur un bout de la région.
 *
 *   node tools/relief-patch.js port-royal 2048 2000
 *
 * Arguments : le lieu (un port, une ville), le côté de l'image en pixels, et la
 * largeur de la zone en mètres de jeu. L'outil écrit `world/<lieu>-relief.png`,
 * peint d'après le relief ACTUEL (donc la côte reste celle qu'on connaît), et
 * ajoute — ou met à jour — l'entrée `patches` de la fiche de région.
 *
 * Ensuite : sculptez cette image (elle obéit à la même loi de gris que la
 * grande, voir world/README.md), ou sculptez le terrain dans Blender et
 * rendez-le avec `node tools/relief-bake.js`.
 *
 * Le gris de départ est INTERPOLÉ : un patch neuf ne change rien au jeu, il ne
 * fait qu'ouvrir la place. C'est voulu — on doit pouvoir le poser sans que la
 * côte bouge d'un pouce.
 */
const fs = require('fs');
const path = require('path');
const { decodeGreyPng, encodeGreyPng } = require('./grey-png.js');

const root = path.join(__dirname, '..');
global.window = global;
eval(fs.readFileSync(path.join(root, 'js', 'world.js'), 'utf8'));

const key = process.argv[2] || 'port-royal';
const SIDE = Math.max(64, Number(process.argv[3] || 2048));
const METRES = Math.max(200, Number(process.argv[4] || 2000));
const sheetPath = path.join(root, 'world', 'caraibes.json');

const region = JSON.parse(fs.readFileSync(sheetPath, 'utf8'));
const img = decodeGreyPng(fs.readFileSync(path.join(root, region.relief.image)));
const W = new Naval.World(region, img);

const isle = W.isles.find(i => i.key === key);
const town = (region.towns || []).find(t => t.key === key);
if (!isle && !town) {
  console.error(`lieu inconnu : ${key}. Ports : ${W.isles.map(i => i.key).join(', ')}`);
  process.exit(1);
}
const at = isle ? { x: isle.x, z: isle.z, name: isle.name }
                : (() => { const p = Naval.Geo.toXZ(town.lat, town.lon); return { x: p.x, z: p.z, name: town.name }; })();

/* LE CADRE, EN DEGRÉS : le carré de METRES mètres autour du lieu, converti par
   la latitude — c'est ainsi que la fiche cadre déjà la grande image. */
const fix = Naval.Geo.fix(at.x, at.z);
const mPerLat = Naval.Geo.M_PER_MIN * 60 * region.scale;
const mPerLon = mPerLat * Math.cos(fix.lat * Math.PI / 180);
const dLat = (METRES / 2) / mPerLat, dLon = (METRES / 2) / mPerLon;
const frame = {
  key, image: `world/${key}-relief.png`,
  west: +(fix.lon - dLon).toFixed(6), east: +(fix.lon + dLon).toFixed(6),
  south: +(fix.lat - dLat).toFixed(6), north: +(fix.lat + dLat).toFixed(6),
  feather: 80
};

// ---- peindre : pour chaque pixel du patch, le gris de la grande image ----
const E = region.relief;
const data = new Uint8Array(SIDE * SIDE);
let lo = 255, hi = 0;
for (let j = 0; j < SIDE; j++)
  for (let i = 0; i < SIDE; i++) {
    const lon = frame.west + (frame.east - frame.west) * (i + 0.5) / SIDE;
    const lat = frame.north - (frame.north - frame.south) * (j + 0.5) / SIDE;
    const p = Naval.Geo.toXZ(lat, lon);
    const v = Math.max(0, Math.min(255, Math.round(W._grey(p.x, p.z))));
    data[j * SIDE + i] = v;
    lo = Math.min(lo, v); hi = Math.max(hi, v);
  }

const out = path.join(root, frame.image);
fs.writeFileSync(out, encodeGreyPng(SIDE, SIDE, data));

// ---- poser l'entrée dans la fiche, SANS la reformater ----
/* La fiche est écrite à la main, alignée à la main : la relire en JSON et la
   réécrire la met à plat d'un bout à l'autre. On touche donc au TEXTE, et à la
   seule ligne qui nous regarde. */
let text = fs.readFileSync(sheetPath, 'utf8');
const line = '  "patches": [\n'
  + `    { "key": "${key}", "image": "${frame.image}",\n`
  + `      "west": ${frame.west}, "east": ${frame.east}, "south": ${frame.south}, "north": ${frame.north},\n`
  + `      "feather": ${frame.feather} }\n`
  + '  ],\n\n';
const had = /\n {2}"patches": \[[\s\S]*?\n {2}\],\n\n/;
if (had.test(text)) text = text.replace(had, '\n' + line);
else text = text.replace(/\n {2}"ports": \[/, '\n' + line + '  "ports": [');
fs.writeFileSync(sheetPath, text);

const px = METRES / SIDE;
console.log(`${out}`);
console.log(`  ${at.name} : ${SIDE}×${SIDE} px pour ${METRES} m — un pixel vaut ${px.toFixed(2)} m (la grande image : ${W.px.toFixed(1)} m)`);
console.log(`  gris de ${lo} à ${hi} ; cadre ${frame.south}..${frame.north}° N, ${frame.west}..${frame.east}° O, fondu ${frame.feather} m`);
console.log(`  entrée « patches » écrite dans world/caraibes.json`);
