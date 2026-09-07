/* Emit a small, valid .glb so the model-loading path can actually be tested,
 * and so there is a worked example of the shape a ship model should have:
 * bow toward +z, origin on the design waterline, amidships.
 *
 *   node tools/make-glb.js ships/models/barge.glb
 */
const fs = require('fs');
const path = require('path');

const out = process.argv[2] || 'ships/models/barge.glb';

// A plain lighter/barge: flat sheer, hard chine, a raked stem. Deliberately
// crude — its job is to prove the pipeline, not to be admired.
const HALF = 3.4, DECK = 1.2, KEEL = -1.6, STERN = -12, BOW = 12;
const V = [];
const F = [];
const push = (x,y,z) => { V.push(x,y,z); return V.length/3 - 1; };

const stations = [
  { z: STERN,     hw: HALF*0.86, keel: KEEL*0.80 },
  { z: STERN*0.5, hw: HALF,      keel: KEEL      },
  { z: 0,         hw: HALF,      keel: KEEL      },
  { z: BOW*0.55,  hw: HALF*0.82, keel: KEEL*0.85 },
  { z: BOW*0.88,  hw: HALF*0.40, keel: KEEL*0.45 },
];
const rings = stations.map(s => ({
  dl: push(-s.hw, DECK, s.z),
  dr: push( s.hw, DECK, s.z),
  kl: push(-s.hw*0.35, s.keel, s.z),
  kr: push( s.hw*0.35, s.keel, s.z),
}));
const stem = push(0, DECK*0.9, BOW);
const foot = push(0, KEEL*0.30, BOW*0.97);

const quad = (a,b,c,d) => { F.push(a,b,c, a,c,d); };
for (let i = 0; i < rings.length - 1; i++) {
  const A = rings[i], B = rings[i+1];
  quad(A.dr, B.dr, B.kr, A.kr);   // starboard topside
  quad(A.kl, B.kl, B.dl, A.dl);   // port topside
  quad(A.kr, B.kr, B.kl, A.kl);   // bottom
  quad(A.dl, B.dl, B.dr, A.dr);   // deck
}
const L = rings[rings.length-1];
F.push(L.dr, stem, L.kr,  L.kr, stem, foot);     // starboard bow
F.push(stem, L.dl, foot,  foot, L.dl, L.kl);     // port bow
F.push(L.dl, stem, L.dr);                        // fore deck
const S0 = rings[0];
F.push(S0.dr, S0.kr, S0.kl,  S0.dr, S0.kl, S0.dl);  // transom

// --- pack the buffers ---
const pos = new Float32Array(V);
const idx = new Uint16Array(F);
const pad4 = n => (n + 3) & ~3;
const posLen = pad4(pos.byteLength);
const idxOff = posLen;
const bin = Buffer.alloc(posLen + pad4(idx.byteLength));
Buffer.from(pos.buffer).copy(bin, 0);
Buffer.from(idx.buffer).copy(bin, idxOff);

let min = [Infinity,Infinity,Infinity], max = [-Infinity,-Infinity,-Infinity];
for (let i = 0; i < V.length; i += 3)
  for (let k = 0; k < 3; k++) {
    min[k] = Math.min(min[k], V[i+k]);
    max[k] = Math.max(max[k], V[i+k]);
  }

const gltf = {
  asset: { version: '2.0', generator: 'naval-sim make-glb' },
  scene: 0,
  scenes: [{ nodes: [0] }],
  nodes: [{ mesh: 0, name: 'hull' }],
  meshes: [{ name: 'hull', primitives: [{ attributes: { POSITION: 0 }, indices: 1, material: 0 }] }],
  materials: [{
    name: 'timber',
    pbrMetallicRoughness: { baseColorFactor: [0.36, 0.27, 0.19, 1], metallicFactor: 0, roughnessFactor: 0.85 },
    doubleSided: true
  }],
  buffers: [{ byteLength: bin.length }],
  bufferViews: [
    { buffer: 0, byteOffset: 0,      byteLength: pos.byteLength, target: 34962 },
    { buffer: 0, byteOffset: idxOff, byteLength: idx.byteLength, target: 34963 }
  ],
  accessors: [
    { bufferView: 0, componentType: 5126, count: V.length/3, type: 'VEC3', min, max },
    { bufferView: 1, componentType: 5123, count: F.length,   type: 'SCALAR' }
  ]
};

const jsonBuf = Buffer.from(JSON.stringify(gltf), 'utf8');
const jsonPad = Buffer.alloc(pad4(jsonBuf.length) - jsonBuf.length, 0x20);   // spaces
const binPad  = Buffer.alloc(pad4(bin.length) - bin.length, 0);
const jsonChunk = Buffer.concat([jsonBuf, jsonPad]);
const binChunk  = Buffer.concat([bin, binPad]);

const header = Buffer.alloc(12);
header.write('glTF', 0, 'ascii');
header.writeUInt32LE(2, 4);
header.writeUInt32LE(12 + 8 + jsonChunk.length + 8 + binChunk.length, 8);

const jsonHdr = Buffer.alloc(8);
jsonHdr.writeUInt32LE(jsonChunk.length, 0); jsonHdr.writeUInt32LE(0x4E4F534A, 4);
const binHdr = Buffer.alloc(8);
binHdr.writeUInt32LE(binChunk.length, 0);  binHdr.writeUInt32LE(0x004E4942, 4);

fs.mkdirSync(path.dirname(out), { recursive: true });
fs.writeFileSync(out, Buffer.concat([header, jsonHdr, jsonChunk, binHdr, binChunk]));
console.log('wrote ' + out + '  (' + V.length/3 + ' vertices, ' + F.length/3 + ' triangles)');
