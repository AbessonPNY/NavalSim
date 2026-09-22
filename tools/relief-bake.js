/* CUIRE UNE SCULPTURE DANS LE RELIEF LOCAL — de Blender au jeu.
 *
 *   node tools/relief-bake.js world/models/port-royal-zone.glb port-royal
 *
 * Prend le maillage de terrain d'un .glb (celui que `tools/zone-glb.js` a
 * exporté, sculpté depuis), relit ses hauteurs et les repeint dans l'image du
 * patch (`world/<lieu>-relief.png`). Tout suit ensuite : la flottaison,
 * l'échouage, l'écume du rivage, la carte marine, le sol dessiné.
 *
 * Ce qui est lu : l'objet nommé `terre` s'il existe, sinon le maillage le plus
 * grand. Les autres (mer, ponton, mole, vos bâtiments) sont ignorés — laissez-les
 * ou non, cela ne change rien.
 *
 * Ce qui n'est PAS couvert par le maillage garde le gris qu'il avait : on peut
 * donc sculpter un coin et rendre le reste intact.
 *
 * L'origine du .glb doit rester le lieu, comme à l'export — c'est elle qui dit
 * où tombe la sculpture.
 */
const fs = require('fs');
const path = require('path');
const { decodeGreyPng, encodeGreyPng } = require('./grey-png.js');

const root = path.join(__dirname, '..');
global.window = global;
eval(fs.readFileSync(path.join(root, 'js', 'world.js'), 'utf8'));

const glbPath = process.argv[2] || 'world/models/port-royal-zone.glb';
const key = process.argv[3] || path.basename(glbPath).replace(/-zone\.glb$/, '');

const region = JSON.parse(fs.readFileSync(path.join(root, 'world', 'caraibes.json'), 'utf8'));
const patch = (region.patches || []).find(p => p.key === key);
if (!patch) {
  console.error(`aucun patch « ${key} » dans world/caraibes.json — lancez d'abord tools/relief-patch.js`);
  process.exit(1);
}
const img = decodeGreyPng(fs.readFileSync(path.join(root, region.relief.image)));
const W = new Naval.World(region, img);

const isle = W.isles.find(i => i.key === key);
const town = (region.towns || []).find(t => t.key === key);
const at = isle ? { x: isle.x, z: isle.z }
                : (() => { const p = Naval.Geo.toXZ(town.lat, town.lon); return { x: p.x, z: p.z }; })();

// ---------------------------------------------------------------- lire le .glb

function readGlb(file) {
  const b = fs.readFileSync(file);
  if (b.readUInt32LE(0) !== 0x46546C67) throw new Error('ce n est pas un .glb');
  const jsonLen = b.readUInt32LE(12);
  const gltf = JSON.parse(b.slice(20, 20 + jsonLen));
  const binOff = 20 + jsonLen + 8;
  const bin = b.slice(binOff, binOff + b.readUInt32LE(20 + jsonLen));
  const view = a => {
    const acc = gltf.accessors[a], bv = gltf.bufferViews[acc.bufferView];
    const off = (bv.byteOffset || 0) + (acc.byteOffset || 0);
    const n = { SCALAR: 1, VEC2: 2, VEC3: 3, VEC4: 4 }[acc.type];
    const kind = { 5126: Float32Array, 5125: Uint32Array, 5123: Uint16Array, 5121: Uint8Array }[acc.componentType];
    if (!kind) throw new Error('type de donnee glTF non gere : ' + acc.componentType);
    const out = new kind(acc.count * n);
    const raw = Buffer.from(bin.buffer, bin.byteOffset + off, acc.count * n * kind.BYTES_PER_ELEMENT);
    Buffer.from(out.buffer).set(raw);
    return out;
  };
  /* Le nœud porte sa transformée : une sculpture déplacée ou mise à l'échelle
     dans Blender doit être lue où elle est vraiment. Translation, échelle et
     rotation autour de Y suffisent ici — le reste serait du zèle. */
  const nodeOf = mesh => (gltf.nodes || []).find(n => n.mesh === mesh) || {};
  let best = null;
  gltf.meshes.forEach((m, mi) => {
    const named = (m.name || '').toLowerCase().includes('terre') || (nodeOf(mi).name || '').toLowerCase().includes('terre');
    for (const pr of m.primitives) {
      const pos = view(pr.attributes.POSITION);
      const idx = pr.indices != null ? view(pr.indices) : null;
      const n = nodeOf(mi);
      const t = n.translation || [0, 0, 0], s = n.scale || [1, 1, 1];
      const cand = { name: m.name || n.name || ('mesh' + mi), pos, idx, t, s, count: pos.length / 3, named };
      if (!best || (cand.named && !best.named) || (cand.named === best.named && cand.count > best.count)) best = cand;
    }
  });
  if (!best) throw new Error('aucun maillage dans ' + file);
  return best;
}

const mesh = readGlb(path.join(root, glbPath));
console.log(`maillage « ${mesh.name} » : ${mesh.count} sommets${mesh.named ? '' : ' (aucun objet « terre » : le plus grand a été pris)'}`);

// ---------------------------------------------------------------- rasteriser

const im = decodeGreyPng(fs.readFileSync(path.join(root, patch.image)));
const SIDE = im.w, SIDEJ = im.h;
const height = new Float32Array(SIDE * SIDEJ).fill(NaN);

/** Du repère du fichier (mètres autour du lieu) aux pixels du patch. */
function toPixel(fx, fz) {
  const g = Naval.Geo.fix(at.x + fx, at.z + fz);
  return [ (g.lon - patch.west) / (patch.east - patch.west) * SIDE,
           (patch.north - g.lat) / (patch.north - patch.south) * SIDEJ ];
}

const P = mesh.pos, T = mesh.t, S = mesh.s;
const tri = (a, b, c) => {
  const v = [a, b, c].map(k => {
    const fx = P[k * 3] * S[0] + T[0], fy = P[k * 3 + 1] * S[1] + T[1], fz = P[k * 3 + 2] * S[2] + T[2];
    const [px, py] = toPixel(fx, fz);
    return [px, py, fy];
  });
  const x0 = Math.max(0, Math.floor(Math.min(v[0][0], v[1][0], v[2][0])));
  const x1 = Math.min(SIDE - 1, Math.ceil(Math.max(v[0][0], v[1][0], v[2][0])));
  const y0 = Math.max(0, Math.floor(Math.min(v[0][1], v[1][1], v[2][1])));
  const y1 = Math.min(SIDEJ - 1, Math.ceil(Math.max(v[0][1], v[1][1], v[2][1])));
  if (x1 < x0 || y1 < y0) return;
  const d = (v[1][1] - v[2][1]) * (v[0][0] - v[2][0]) + (v[2][0] - v[1][0]) * (v[0][1] - v[2][1]);
  if (Math.abs(d) < 1e-12) return;
  for (let py = y0; py <= y1; py++)
    for (let px = x0; px <= x1; px++) {
      const cx = px + 0.5, cy = py + 0.5;
      const w0 = ((v[1][1] - v[2][1]) * (cx - v[2][0]) + (v[2][0] - v[1][0]) * (cy - v[2][1])) / d;
      const w1 = ((v[2][1] - v[0][1]) * (cx - v[2][0]) + (v[0][0] - v[2][0]) * (cy - v[2][1])) / d;
      const w2 = 1 - w0 - w1;
      if (w0 < -1e-6 || w1 < -1e-6 || w2 < -1e-6) continue;
      const h = w0 * v[0][2] + w1 * v[1][2] + w2 * v[2][2];
      const k = py * SIDE + px;
      // la plus HAUTE l'emporte : un quai posé sur la plage reste un quai
      if (!(height[k] >= h)) height[k] = h;
    }
};

const N = mesh.idx ? mesh.idx.length : mesh.count;
for (let i = 0; i + 2 < N; i += 3)
  tri(mesh.idx ? mesh.idx[i] : i, mesh.idx ? mesh.idx[i + 1] : i + 1, mesh.idx ? mesh.idx[i + 2] : i + 2);

// ---------------------------------------------------------------- écrire le gris

/* L'INVERSE DE LA LOI DE GRIS (js/world.js → decode) : la hauteur va comme le
   carré de l'écart au gris du rivage, donc le gris va comme sa racine. */
const E = Object.assign({ sea: 128, maxHeight: 1500, maxDepth: 400, curve: 2 }, region.relief);
function encode(h) {
  const v = h >= 0
    ? E.sea + (255 - E.sea) * Math.pow(Math.min(1, h / E.maxHeight), 1 / E.curve)
    : E.sea - E.sea * Math.pow(Math.min(1, -h / E.maxDepth), 1 / E.curve);
  return Math.max(0, Math.min(255, Math.round(v)));
}

const out = new Uint8Array(im.data);
let touched = 0, lo = Infinity, hi = -Infinity;
for (let k = 0; k < out.length; k++) {
  const h = height[k];
  if (Number.isNaN(h)) continue;
  out[k] = encode(h);
  touched++; lo = Math.min(lo, h); hi = Math.max(hi, h);
}
if (touched === 0) {
  console.error('le maillage ne tombe sur AUCUN pixel du patch — origine ou cadre à revoir ; rien n a été écrit');
  process.exit(1);
}
fs.writeFileSync(path.join(root, patch.image), encodeGreyPng(SIDE, SIDEJ, out));

let bougé = 0;
for (let k = 0; k < out.length; k++) if (out[k] !== im.data[k]) bougé++;
console.log(`${patch.image}`);
console.log(`  ${touched} pixel(s) couvert(s) sur ${SIDE * SIDEJ} (${(100 * touched / (SIDE * SIDEJ)).toFixed(1)} %), ${bougé} changé(s)`);
console.log(`  sculpture de ${lo.toFixed(1)} m à ${hi.toFixed(1)} m`);
