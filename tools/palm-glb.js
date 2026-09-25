/* UN COCOTIER, à poser sur un îlot.
 *
 *   node tools/palm-glb.js                       # écrit world/assets/cocotier.glb
 *   node tools/palm-glb.js autre/chemin.glb
 *
 * Écrit comme les maisons et l'église (tools/town-glb.js) : dessiné par le code,
 * couleurs dans les sommets, aucune texture — la page ne pourrait pas en charger
 * une, et un arbre n'en a pas besoin.
 *
 * CE QUI FAIT LE COCOTIER, et qu'on ne peut pas ôter :
 *
 *   — le TRONC PENCHÉ. Un cocotier droit est un poteau ; celui du bord de mer
 *     s'incline vers le large, parce qu'il pousse vers la lumière que l'eau lui
 *     renvoie et que le vent de mer le couche. C'est la silhouette, avant même
 *     les palmes ;
 *   — les PALMES QUI RETOMBENT. Une palme part en l'air, s'arque et redescend
 *     sous l'horizontale ; droites, elles font un parasol de carton ;
 *   — les ANNEAUX du stipe, que les vieilles palmes laissent en tombant. Ils ne
 *     coûtent rien : c'est le rayon du tronc qui ondule.
 *
 * L'avant glTF est +Z, le haut +Y. Unités : le mètre, à l'échelle du jeu.
 */
const fs = require('fs');
const path = require('path');

const out = process.argv[2] || 'world/assets/cocotier.glb';

const H = 9.5;              // la hauteur du stipe, en mètres
const LEAN = 0.16;          // ce qu'il penche, en part de sa hauteur
const R0 = 0.22, R1 = 0.13; // son rayon au pied et sous la couronne

const P = [], C = [], I = [];
const tri = (a, b, c) => { I.push(a, b, c); };

/* ---- LE STIPE : un tube de section circulaire, courbé et annelé ---- */
{
  const SEG = 9, SIDES = 8;
  const base = P.length / 3;
  for (let s = 0; s <= SEG; s++) {
    const u = s / SEG;
    // il penche en douceur, et le haut penche plus que le pied
    const dz = LEAN * H * u * u;
    const y = H * u;
    // les anneaux : le rayon ondule de quelques centimètres
    const r = (R0 + (R1 - R0) * u) * (1 + 0.07 * Math.sin(u * 22));
    for (let i = 0; i <= SIDES; i++) {
      const a = i / SIDES * Math.PI * 2;
      P.push(Math.cos(a) * r, y, dz + Math.sin(a) * r);
      // gris chaud, plus clair en haut où l'écorce est jeune
      const k = 0.55 + 0.25 * u + 0.08 * Math.sin(u * 22);
      C.push(0.42 * k + 0.10, 0.36 * k + 0.09, 0.28 * k + 0.07, 1);
    }
  }
  for (let s = 0; s < SEG; s++)
    for (let i = 0; i < SIDES; i++) {
      const k = base + s * (SIDES + 1) + i;
      tri(k, k + SIDES + 1, k + SIDES + 2);
      tri(k, k + SIDES + 2, k + 1);
    }
}

const TOP = [LEAN * H, H, 0];        // (dz, y) du sommet du stipe

/* ---- UNE PALME : une nervure qui s'arque, et deux rangées de folioles ---- */
function frond(az, tilt, len) {
  const base = P.length / 3;
  const N = 7;
  const ca = Math.cos(az), sa = Math.sin(az);
  const pts = [];
  for (let i = 0; i <= N; i++) {
    const u = i / N;
    /* L'ARC : elle monte d'abord, puis retombe sous l'horizontale. Un cosinus
       décalé donne exactement cela, et un seul nombre le règle (tilt). */
    const rise = Math.sin(u * Math.PI * 0.9) * tilt - u * u * len * 0.55;
    const rad = u * len;
    pts.push([ca * rad, TOP[1] + rise, TOP[0] + sa * rad]);
  }
  // la nervure, en ruban : deux bords écartés de la largeur des folioles
  for (let i = 0; i <= N; i++) {
    const u = i / N;
    const w = (0.55 - 0.45 * u * u) * (u < 0.12 ? u / 0.12 : 1);   // étroite au départ
    const [x, y, z] = pts[i];
    // le ruban est écarté PERPENDICULAIREMENT à la palme, dans le plan horizontal
    const px = -sa * w, pz = ca * w;
    P.push(x + px, y, z + pz);
    P.push(x - px, y, z - pz);
    const k = 0.75 - 0.25 * u;                       // la pointe est plus sombre
    for (let q = 0; q < 2; q++) C.push(0.16 * k + 0.05, 0.34 * k + 0.07, 0.13 * k + 0.04, 1);
  }
  for (let i = 0; i < N; i++) {
    const k = base + i * 2;
    tri(k, k + 1, k + 3);
    tri(k, k + 3, k + 2);
  }
}

const NP = 8;
for (let i = 0; i < NP; i++) {
  const az = i / NP * Math.PI * 2 + 0.3;
  // elles ne sont pas toutes de la même main : longueur et arc varient un peu
  frond(az, 1.15 + 0.35 * Math.sin(i * 2.1), 3.1 + 0.55 * Math.sin(i * 1.3 + 1));
}

/* ---- LES NOIX, en grappe sous la couronne ---- */
for (let i = 0; i < 4; i++) {
  const a = i / 4 * Math.PI * 2 + 0.8;
  const cx = Math.cos(a) * 0.34, cz = TOP[0] + Math.sin(a) * 0.34, cy = TOP[1] - 0.32;
  const base = P.length / 3;
  const U = 6, V = 4, r = 0.16;
  for (let v = 0; v <= V; v++)
    for (let u = 0; u <= U; u++) {
      const th = Math.PI * v / V, ph = Math.PI * 2 * u / U;
      P.push(cx + Math.sin(th) * Math.cos(ph) * r, cy + Math.cos(th) * r * 1.15, cz + Math.sin(th) * Math.sin(ph) * r);
      C.push(0.38, 0.30, 0.16, 1);
    }
  for (let v = 0; v < V; v++)
    for (let u = 0; u < U; u++) {
      const k = base + v * (U + 1) + u;
      tri(k, k + U + 1, k + U + 2);
      tri(k, k + U + 2, k + 1);
    }
}

/* ---- normales ---- */
function normals(pos, idx) {
  const n = new Float32Array(pos.length);
  for (let i = 0; i < idx.length; i += 3) {
    const a = idx[i] * 3, b = idx[i + 1] * 3, c = idx[i + 2] * 3;
    const ux = pos[b] - pos[a], uy = pos[b + 1] - pos[a + 1], uz = pos[b + 2] - pos[a + 2];
    const vx = pos[c] - pos[a], vy = pos[c + 1] - pos[a + 1], vz = pos[c + 2] - pos[a + 2];
    const nx = uy * vz - uz * vy, ny = uz * vx - ux * vz, nz = ux * vy - uy * vx;
    for (const k of [a, b, c]) { n[k] += nx; n[k + 1] += ny; n[k + 2] += nz; }
  }
  for (let i = 0; i < n.length; i += 3) {
    const l = Math.hypot(n[i], n[i + 1], n[i + 2]) || 1;
    n[i] /= l; n[i + 1] /= l; n[i + 2] /= l;
  }
  return Array.from(n);
}

/* ---- empaqueter ---- */
const pad4 = n => (n + 3) & ~3;
const chunks = [], bufferViews = [], accessors = [];
let offset = 0;
function accessor(typed, type, target, minmax) {
  const b = Buffer.from(typed.buffer, typed.byteOffset, typed.byteLength);
  const padded = Buffer.alloc(pad4(b.length)); b.copy(padded);
  chunks.push(padded);
  bufferViews.push({ buffer: 0, byteOffset: offset, byteLength: b.length, target });
  offset += padded.length;
  const n = { VEC4: 4, VEC3: 3, VEC2: 2, SCALAR: 1 }[type];
  const a = {
    bufferView: bufferViews.length - 1,
    componentType: typed instanceof Float32Array ? 5126 : 5125,
    count: typed.length / n, type,
  };
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
const attrs = {
  POSITION: accessor(new Float32Array(P), 'VEC3', 34962, true),
  NORMAL: accessor(new Float32Array(normals(P, I)), 'VEC3', 34962),
  COLOR_0: accessor(new Float32Array(C), 'VEC4', 34962),
};
const gltf = {
  asset: { version: '2.0', generator: 'naval-sim palm-glb' },
  scene: 0, scenes: [{ name: 'cocotier', nodes: [0] }],
  nodes: [{ name: 'cocotier', mesh: 0 }],
  meshes: [{
    name: 'cocotier',
    primitives: [{ attributes: attrs, indices: accessor(new Uint32Array(I), 'SCALAR', 34963), material: 0 }],
  }],
  materials: [{
    name: 'palme', doubleSided: true,
    pbrMetallicRoughness: { baseColorFactor: [1, 1, 1, 1], metallicFactor: 0, roughnessFactor: 0.85 },
  }],
  buffers: [{ byteLength: 0 }], bufferViews, accessors,
};
const bin = Buffer.concat(chunks);
gltf.buffers[0].byteLength = bin.length;
const jsonBuf = Buffer.from(JSON.stringify(gltf), 'utf8');
const jsonChunk = Buffer.concat([jsonBuf, Buffer.alloc(pad4(jsonBuf.length) - jsonBuf.length, 0x20)]);
const binChunk = Buffer.concat([bin, Buffer.alloc(pad4(bin.length) - bin.length, 0)]);
const header = Buffer.alloc(12);
header.write('glTF', 0, 'ascii'); header.writeUInt32LE(2, 4);
header.writeUInt32LE(12 + 8 + jsonChunk.length + 8 + binChunk.length, 8);
const jh = Buffer.alloc(8); jh.writeUInt32LE(jsonChunk.length, 0); jh.writeUInt32LE(0x4E4F534A, 4);
const bh = Buffer.alloc(8); bh.writeUInt32LE(binChunk.length, 0); bh.writeUInt32LE(0x004E4942, 4);
fs.mkdirSync(path.dirname(out), { recursive: true });
fs.writeFileSync(out, Buffer.concat([header, jh, jsonChunk, bh, binChunk]));
console.log(`wrote ${out}  (${P.length / 3} sommets, ${I.length / 3} triangles, ` +
            `${(fs.statSync(out).size / 1024).toFixed(1)} Ko, ${H} m de haut)`);
