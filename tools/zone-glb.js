/* LE RELIEF D'UNE ZONE, EN .GLB — de quoi bâtir un port à la main dans Blender
 * sur le terrain EXACT du jeu, plutôt que sur une idée du terrain.
 *
 *   node tools/zone-glb.js                       (Port-Royal, 1800 × 1400 m)
 *   node tools/zone-glb.js port-royal 2400 1800 3
 *   node tools/zone-glb.js kingston              (n'importe quel port ou ville)
 *
 * Arguments : la clé du lieu, la largeur et la profondeur en mètres de jeu, le
 * pas de la grille. Le relief est lu par js/world.js, donc par la MÊME loi que
 * le jeu — image, dragage des rades, môle compris (le banc de parité tient le
 * C# et le JS d'accord là-dessus).
 *
 * Ce que le fichier contient, chacun sa matière :
 *   terre  — le relief, origine au lieu, y vers le haut, mètres de jeu
 *   mer    — un plan au niveau zéro, pour voir où tombe la ligne d'eau
 *   ponton — le tablier du ponton du port, à sa place et à son cap
 *   mole   — le mur d'enceinte, s'il y en a un
 *
 * Rendez-le tel quel : les bâtiments que vous poserez dessus se replaceront
 * dans le jeu aux mêmes coordonnées (voir world/README.md, « les modèles
 * posés »), l'origine du fichier étant le lieu lui-même.
 */
const fs = require('fs');
const path = require('path');
const { decodeGreyPng } = require('./parity-world.js');

const root = path.join(__dirname, '..');
global.window = global;
eval(fs.readFileSync(path.join(root, 'js', 'world.js'), 'utf8'));

const key = process.argv[2] || 'port-royal';
const WIDE = Number(process.argv[3] || 1800);
const DEEP = Number(process.argv[4] || 1400);
let STEP = Number(process.argv[5] || 0);

const region = JSON.parse(fs.readFileSync(path.join(root, 'world', 'caraibes.json'), 'utf8'));
const img = decodeGreyPng(fs.readFileSync(path.join(root, region.relief.image)));
const W = new Naval.World(region, img);

const isle = W.isles.find(i => i.key === key);
const town = (region.towns || []).find(t => t.key === key);
if (!isle && !town) {
  console.error(`lieu inconnu : ${key}. Ports : ${W.isles.map(i => i.key).join(', ')}`);
  process.exit(1);
}
const at = isle ? { x: isle.x, z: isle.z, name: isle.name } : (() => {
  const p = Naval.Geo.toXZ(town.lat, town.lon);
  return { x: p.x, z: p.z, name: town.name };
})();

/* LE PAS, PAR DÉFAUT CELUI DE LA SOURCE : échantillonner plus fin que le relief
   ne donne que des triangles qui interpolent (signalé). S'il y a un relief local
   ici, c'est lui qui décide — sans descendre sous deux mètres, faute de quoi un
   carré de deux kilomètres pèserait deux cents mégaoctets. */
if (!(STEP > 0)) {
  const p = (region.patches || []).find(p =>
    at.x !== undefined && (() => { const g = Naval.Geo.fix(at.x, at.z);
      return g.lon > p.west && g.lon < p.east && g.lat > p.south && g.lat < p.north; })());
  const fine = p ? (p.north - p.south) * Naval.Geo.M_PER_MIN * 60 * region.scale
                   / require('./grey-png.js').decodeGreyPng(fs.readFileSync(path.join(root, p.image))).h
                 : W.px;
  STEP = Math.max(2, Math.round(fine * 100) / 100);
}

// ---------------------------------------------------------------- les maillages

const meshes = [];

/* LE RELIEF, une grille régulière. Les sommets portent leur hauteur vraie ; le
   fichier a son origine AU LIEU, donc x et z y sont des écarts en mètres. */
function terrain() {
  const nx = Math.round(WIDE / STEP) + 1, nz = Math.round(DEEP / STEP) + 1;
  const pos = [], uv = [], idx = [];
  let low = Infinity, high = -Infinity;
  for (let j = 0; j < nz; j++)
    for (let i = 0; i < nx; i++) {
      const dx = -WIDE / 2 + i * STEP, dz = -DEEP / 2 + j * STEP;
      const y = W.heightAt(at.x + dx, at.z + dz);
      pos.push(dx, y, dz);
      uv.push(i / (nx - 1), j / (nz - 1));
      low = Math.min(low, y); high = Math.max(high, y);
    }
  for (let j = 0; j < nz - 1; j++)
    for (let i = 0; i < nx - 1; i++) {
      const a = j * nx + i, b = a + 1, c = a + nx, d = c + 1;
      idx.push(a, c, b, b, c, d);
    }
  meshes.push({ name: 'terre', pos, uv, idx, material: 0, smooth: true });
  return { nx, nz, low, high };
}

/** Un plan au niveau de la mer, un rien sous zéro pour ne pas lutter avec le sable. */
function sea() {
  const x = WIDE / 2, z = DEEP / 2, y = -0.02;
  meshes.push({
    name: 'mer', material: 1, smooth: false,
    pos: [-x, y, -z, x, y, -z, x, y, z, -x, y, z],
    uv: [0, 0, 1, 0, 1, 1, 0, 1],
    idx: [0, 2, 1, 0, 3, 2]
  });
}

/** Une boîte, en mètres, dans le repère du fichier. */
function box(name, material, x0, x1, y0, y1, z0, z1, xf) {
  const P = [], I = [], UV = [];
  const put = p => { const q = xf ? xf(p) : p; P.push(q[0], q[1], q[2]); UV.push(0, 0); };
  const A = [x0, y0, z0], B = [x1, y0, z0], C = [x1, y1, z0], D = [x0, y1, z0];
  const E = [x0, y0, z1], F = [x1, y0, z1], G = [x1, y1, z1], H = [x0, y1, z1];
  for (const p of [A, B, C, D, E, F, G, H]) put(p);
  const q = (a, b, c, d) => I.push(a, b, c, a, c, d);
  q(1, 0, 3, 2); q(4, 5, 6, 7); q(0, 4, 7, 3); q(5, 1, 2, 6); q(3, 7, 6, 2); q(0, 1, 5, 4);
  meshes.push({ name, pos: P, uv: UV, idx: I, material, smooth: false });
}

/* LE PONTON ET LE MÔLE, à leur place : ce sont EUX qui disent où est le quai, et
   une ville bâtie à côté de son ponton ne serait pas une ville de port. */
function works() {
  if (!isle || !isle.port) return;
  const p = isle.port;
  const ca = Math.cos(p.ang), sa = Math.sin(p.ang);
  // le tablier : 7 m de large, 1,7 m au-dessus de l'eau (core/NavalSim.Core/Berth.cs)
  const along = ([u, y, v]) => [ (at.x + ca * u - sa * v) - at.x, y, (at.z + sa * u + ca * v) - at.z ];
  box('ponton', 2, p.shoreR - 6, p.shoreR + p.reach, 1.4, 1.7, -3.5, 3.5, along);
  const h = p.harbour;
  if (!h) return;
  // le mur d'enceinte, en arcs : sa passe reste ouverte, du côté du large
  const P = [], I = [], UV = [];
  const SEG = 96, half = 9;
  for (let i = 0; i <= SEG; i++) {
    const a = h.gap + (2 * Math.PI - 2 * h.gap) * (i / SEG) + p.ang + Math.PI;
    for (const r of [h.r - half, h.r + half])
      for (const y of [0, h.top || 3.4]) {
        P.push(h.cx - at.x + Math.cos(a) * r, y, h.cz - at.z + Math.sin(a) * r);
        UV.push(i / SEG, r > h.r ? 1 : 0);
      }
  }
  for (let i = 0; i < SEG; i++) {
    const k = i * 4, n = k + 4;
    I.push(k + 1, n + 1, k + 3, k + 3, n + 1, n + 3);       // le dessus
    I.push(k, k + 1, n, n, k + 1, n + 1);                   // la face intérieure
    I.push(n + 2, n + 3, k + 2, k + 2, n + 3, k + 3);       // la face extérieure
  }
  meshes.push({ name: 'mole', pos: P, uv: UV, idx: I, material: 3, smooth: false });
}

const grid = terrain();
sea();
works();

// ---------------------------------------------------------------- l'écriture

const materials = [
  { name: 'terre', pbrMetallicRoughness: { baseColorFactor: [0.78, 0.73, 0.52, 1], metallicFactor: 0, roughnessFactor: 0.95 } },
  { name: 'mer', doubleSided: true, pbrMetallicRoughness: { baseColorFactor: [0.12, 0.36, 0.45, 0.55], metallicFactor: 0, roughnessFactor: 0.25 }, alphaMode: 'BLEND' },
  { name: 'ponton', pbrMetallicRoughness: { baseColorFactor: [0.45, 0.35, 0.24, 1], metallicFactor: 0, roughnessFactor: 0.85 } },
  { name: 'mole', pbrMetallicRoughness: { baseColorFactor: [0.62, 0.60, 0.55, 1], metallicFactor: 0, roughnessFactor: 0.9 } }
];

function normals(pos, idx, smooth) {
  const n = new Float32Array(pos.length);
  for (let i = 0; i < idx.length; i += 3) {
    const a = idx[i] * 3, b = idx[i + 1] * 3, c = idx[i + 2] * 3;
    const ux = pos[b] - pos[a], uy = pos[b + 1] - pos[a + 1], uz = pos[b + 2] - pos[a + 2];
    const vx = pos[c] - pos[a], vy = pos[c + 1] - pos[a + 1], vz = pos[c + 2] - pos[a + 2];
    const nx = uy * vz - uz * vy, ny = uz * vx - ux * vz, nz = ux * vy - uy * vx;
    for (const k of [a, b, c]) { n[k] += nx; n[k + 1] += ny; n[k + 2] += nz; }
  }
  for (let i = 0; i < n.length; i += 3) {
    const L = Math.hypot(n[i], n[i + 1], n[i + 2]) || 1;
    n[i] /= L; n[i + 1] /= L; n[i + 2] /= L;
  }
  return n;
}

const pad4 = v => (v + 3) & ~3;
const chunks = [], bufferViews = [], accessors = [];
let offset = 0;
function accessor(typed, type, target, minmax) {
  const raw = Buffer.from(typed.buffer, typed.byteOffset, typed.byteLength);
  const padded = Buffer.alloc(pad4(raw.length)); raw.copy(padded);
  chunks.push(padded);
  bufferViews.push({ buffer: 0, byteOffset: offset, byteLength: raw.length, target });
  offset += padded.length;
  const n = { VEC3: 3, VEC2: 2, SCALAR: 1 }[type];
  const a = { bufferView: bufferViews.length - 1, componentType: typed instanceof Float32Array ? 5126 : 5125,
              count: typed.length / n, type };
  if (minmax) {
    const mn = [Infinity, Infinity, Infinity], mx = [-Infinity, -Infinity, -Infinity];
    for (let i = 0; i < typed.length; i += 3) for (let k = 0; k < 3; k++) {
      mn[k] = Math.min(mn[k], typed[i + k]); mx[k] = Math.max(mx[k], typed[i + k]);
    }
    a.min = mn; a.max = mx;
  }
  accessors.push(a);
  return accessors.length - 1;
}

const gMeshes = [], nodes = [];
for (const m of meshes) {
  gMeshes.push({
    name: m.name,
    primitives: [{
      attributes: {
        POSITION: accessor(new Float32Array(m.pos), 'VEC3', 34962, true),
        NORMAL: accessor(normals(m.pos, m.idx, m.smooth), 'VEC3', 34962),
        TEXCOORD_0: accessor(new Float32Array(m.uv), 'VEC2', 34962)
      },
      indices: accessor(new Uint32Array(m.idx), 'SCALAR', 34963),
      material: m.material
    }]
  });
  nodes.push({ name: m.name, mesh: gMeshes.length - 1 });
}

const bin = Buffer.concat(chunks);
const gltf = {
  asset: { version: '2.0', generator: 'naval-sim zone-glb' },
  scene: 0, scenes: [{ name: key, nodes: nodes.map((_, i) => i) }],
  nodes, meshes: gMeshes, materials,
  buffers: [{ byteLength: bin.length }], bufferViews, accessors
};
const jsonBuf = Buffer.from(JSON.stringify(gltf), 'utf8');
const jsonChunk = Buffer.concat([jsonBuf, Buffer.alloc(pad4(jsonBuf.length) - jsonBuf.length, 0x20)]);
const binChunk = Buffer.concat([bin, Buffer.alloc(pad4(bin.length) - bin.length, 0)]);
const header = Buffer.alloc(12);
header.write('glTF', 0, 'ascii'); header.writeUInt32LE(2, 4);
header.writeUInt32LE(12 + 8 + jsonChunk.length + 8 + binChunk.length, 8);
const jh = Buffer.alloc(8); jh.writeUInt32LE(jsonChunk.length, 0); jh.writeUInt32LE(0x4E4F534A, 4);
const bh = Buffer.alloc(8); bh.writeUInt32LE(binChunk.length, 0); bh.writeUInt32LE(0x004E4942, 4);

const out = path.join(root, 'world', 'models', key + '-zone.glb');
fs.mkdirSync(path.dirname(out), { recursive: true });
fs.writeFileSync(out, Buffer.concat([header, jh, jsonChunk, bh, binChunk]));

const fix = Naval.Geo.fix ? Naval.Geo.fix(at.x, at.z) : null;
console.log(`${out}`);
console.log(`  ${at.name} : ${WIDE} × ${DEEP} m au pas de ${STEP} m, ${grid.nx}×${grid.nz} sommets`);
console.log(`  relief de ${grid.low.toFixed(1)} m à ${grid.high.toFixed(1)} m ; origine du fichier = le lieu`);
console.log(`  monde : x ${at.x.toFixed(1)}, z ${at.z.toFixed(1)}${fix ? `, ${fix.lat.toFixed(4)}° ${fix.lon.toFixed(4)}°` : ''}`);
console.log(`  ${(fs.statSync(out).size / 1048576).toFixed(1)} Mo`);
