/* A starting model for the kraken, to be opened in Blender and made better.
 *
 *   node tools/kraken-glb.js                    # writes creatures/kraken.glb
 *   node tools/kraken-glb.js autre/chemin.glb
 *
 * It is the drawn kraken, written out: a mantle, two eyes, and ONE arm. The
 * game reads the model by NAME, so keep the names when you edit it:
 *
 *   bras…    an arm, modelled STRAIGHT. In Blender it stands up the +Z axis,
 *            base at the origin, tip at the top; its height is its length. The
 *            game bends it along the path the arm takes, every frame, so what
 *            you model is the arm laid out flat. Put the SUCKERS on the +X side:
 *            that side is turned toward whatever the arm holds. Several nodes
 *            named bras, bras.001… give several different arms, used in turn.
 *   anything else is the body: the mantle, centred on the origin, eyes toward
 *            -Y in Blender (the glTF front, +Z). Metres, as drawn.
 *
 * Textures go in the .glb (pack them in Blender, export as glTF Binary).
 * Emission on the eyes is kept: they glow at night as they do now.
 */
const fs = require('fs');
const path = require('path');

const out = process.argv[2] || 'creatures/kraken.glb';

// sRGB → linear, which is what glTF colour factors are
const lin = c => { c /= 255; return c <= 0.04045 ? c/12.92 : Math.pow((c + 0.055)/1.055, 2.4); };
const rgb = hex => [lin((hex >> 16) & 255), lin((hex >> 8) & 255), lin(hex & 255), 1];

const meshes = [];   // { name, pos, nrm, uv, idx, material }

/* An ellipsoid, UV-mapped as a globe: u round the waist, v pole to pole. */
function ellipsoid(name, rx, ry, rz, cx, cy, cz, seg, rings, material){
  const pos = [], nrm = [], uv = [], idx = [];
  for(let j=0;j<=rings;j++){
    const v = j/rings, th = v*Math.PI;
    for(let i=0;i<=seg;i++){
      const u = i/seg, ph = u*Math.PI*2;
      const nx = Math.sin(th)*Math.cos(ph), ny = Math.cos(th), nz = Math.sin(th)*Math.sin(ph);
      pos.push(cx + rx*nx, cy + ry*ny, cz + rz*nz);
      const l = Math.hypot(nx/rx, ny/ry, nz/rz) || 1;
      nrm.push(nx/rx/l, ny/ry/l, nz/rz/l);
      uv.push(u, v);
    }
  }
  for(let j=0;j<rings;j++) for(let i=0;i<seg;i++){
    const a = j*(seg+1) + i, b = a + seg + 1;
    idx.push(a, a+1, b,  b, a+1, b+1);
  }
  meshes.push({ name, pos, nrm, uv, idx, material });
}

/* The arm: a tapered tube up +Y (glTF), from 0 to LEN, closed at the tip. The
   taper is the one the game draws (0.95 m at the root, 7 % of it at the tip).
   u goes round, v goes along — a texture painted as a long strip wraps it. */
function arm(name, LEN, R0, rings, sides, material){
  const pos = [], nrm = [], uv = [], idx = [];
  const radius = s => R0*(1 - 0.93*Math.pow(s, 0.85));
  for(let j=0;j<=rings;j++){
    const s = j/rings, y = s*LEN, r = radius(s);
    // slope of the taper, so the normals lean as the surface does
    const dr = (radius(Math.min(1, s + 1e-3)) - radius(Math.max(0, s - 1e-3)))/(2e-3*LEN);
    for(let i=0;i<=sides;i++){
      const u = i/sides, a = u*Math.PI*2;
      const cx = Math.cos(a), cz = Math.sin(a);
      pos.push(cx*r, y, cz*r);
      const l = Math.hypot(1, dr);
      nrm.push(cx/l, -dr/l, cz/l);
      uv.push(u, s);
    }
  }
  for(let j=0;j<rings;j++) for(let i=0;i<sides;i++){
    const a = j*(sides+1) + i, b = a + sides + 1;
    idx.push(a, b, a+1,  a+1, b, b+1);
  }
  // the tip
  const tip = pos.length/3;
  pos.push(0, LEN + radius(1), 0); nrm.push(0, 1, 0); uv.push(0.5, 1);
  const last = rings*(sides+1);
  for(let i=0;i<sides;i++) idx.push(last + i, tip, last + i + 1);
  meshes.push({ name, pos, nrm, uv, idx, material });
}

const materials = [
  { name:'peau', pbrMetallicRoughness:{ baseColorFactor:rgb(0x4a1b24), metallicFactor:0.04, roughnessFactor:0.34 } },
  { name:'peau_bras', pbrMetallicRoughness:{ baseColorFactor:rgb(0x4a1b24), metallicFactor:0.04, roughnessFactor:0.34 } },
  { name:'yeux', pbrMetallicRoughness:{ baseColorFactor:rgb(0xd9b44a), metallicFactor:0, roughnessFactor:0.2 },
    emissiveFactor:[lin(0xd9), lin(0xb4), lin(0x4a)] }
];

ellipsoid('corps', 5.5, 3.4, 8.5, 0, 0, 0, 40, 24, 0);
ellipsoid('oeil_gauche', 0.5, 0.5, 0.5,  3.4, 1.1, 5.9, 16, 10, 2);
ellipsoid('oeil_droit',  0.5, 0.5, 0.5, -3.4, 1.1, 5.9, 16, 10, 2);
arm('bras', 16, 0.95, 64, 18, 1);

/* ---- pack ---- */
const pad4 = n => (n + 3) & ~3;
const chunks = [], bufferViews = [], accessors = [];
let offset = 0;
function view(typed, target){
  const b = Buffer.from(typed.buffer, typed.byteOffset, typed.byteLength);
  const padded = Buffer.alloc(pad4(b.length));
  b.copy(padded);
  chunks.push(padded);
  bufferViews.push({ buffer:0, byteOffset:offset, byteLength:b.length, target });
  offset += padded.length;
  return bufferViews.length - 1;
}
function accessor(typed, type, target, minmax){
  const n = { VEC3:3, VEC2:2, SCALAR:1 }[type];
  const a = { bufferView:view(typed, target), componentType: typed instanceof Float32Array ? 5126 : 5125,
              count: typed.length/n, type };
  if(minmax){
    const mn = [Infinity, Infinity, Infinity], mx = [-Infinity, -Infinity, -Infinity];
    for(let i=0;i<typed.length;i+=3) for(let k=0;k<3;k++){
      mn[k] = Math.min(mn[k], typed[i+k]); mx[k] = Math.max(mx[k], typed[i+k]);
    }
    a.min = mn; a.max = mx;
  }
  accessors.push(a);
  return accessors.length - 1;
}

const gMeshes = [], nodes = [];
for(const m of meshes){
  const P = accessor(new Float32Array(m.pos), 'VEC3', 34962, true);
  const N = accessor(new Float32Array(m.nrm), 'VEC3', 34962);
  const T = accessor(new Float32Array(m.uv), 'VEC2', 34962);
  const I = accessor(new Uint32Array(m.idx), 'SCALAR', 34963);
  gMeshes.push({ name:m.name, primitives:[{ attributes:{ POSITION:P, NORMAL:N, TEXCOORD_0:T }, indices:I, material:m.material }] });
  nodes.push({ name:m.name, mesh:gMeshes.length - 1 });
}
const bin = Buffer.concat(chunks);
const gltf = {
  asset:{ version:'2.0', generator:'naval-sim kraken-glb' },
  scene:0, scenes:[{ name:'kraken', nodes:nodes.map((_, i) => i) }],
  nodes, meshes:gMeshes, materials,
  buffers:[{ byteLength:bin.length }], bufferViews, accessors
};

const jsonBuf = Buffer.from(JSON.stringify(gltf), 'utf8');
const jsonChunk = Buffer.concat([jsonBuf, Buffer.alloc(pad4(jsonBuf.length) - jsonBuf.length, 0x20)]);
const binChunk = Buffer.concat([bin, Buffer.alloc(pad4(bin.length) - bin.length, 0)]);
const header = Buffer.alloc(12);
header.write('glTF', 0, 'ascii');
header.writeUInt32LE(2, 4);
header.writeUInt32LE(12 + 8 + jsonChunk.length + 8 + binChunk.length, 8);
const jh = Buffer.alloc(8); jh.writeUInt32LE(jsonChunk.length, 0); jh.writeUInt32LE(0x4E4F534A, 4);
const bh = Buffer.alloc(8); bh.writeUInt32LE(binChunk.length, 0); bh.writeUInt32LE(0x004E4942, 4);

fs.mkdirSync(path.dirname(out), { recursive:true });
fs.writeFileSync(out, Buffer.concat([header, jh, jsonChunk, bh, binChunk]));
console.log('wrote ' + out + '  (' + meshes.map(m => m.name + ' ' + m.pos.length/3).join(', ') + ' vertices)');
