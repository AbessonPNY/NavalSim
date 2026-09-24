/* A starting model for the reef fish, to be opened in Blender and made better.
 *
 *   node tools/fish-glb.js                     # writes creatures/fish.glb
 *   node tools/fish-glb.js autre/chemin.glb
 *
 * ONE fish, in metres, of the size of a real one (about 22 cm): the game makes
 * the shoal, and every fish in it is this same model instanced — so the file
 * must stay small and must be a SINGLE surface. Everything is merged here.
 *
 * The frame is the game's: nose toward +Z, back up (+Y), so the fish is thin
 * along X. The ORIGIN is at the tail stock, where the body stops and the
 * caudal fin begins, because that is the hinge the game beats the tail about:
 * it bends the body about the origin and sweeps the fin with it. Do not move
 * it in Blender.
 *
 * Vertex colours, no texture: a reef fish is a few flat bands, the game tints
 * each one differently, and a texture would be weight for nothing. Keep the
 * colours if you re-model it (COLOR_0), or paint a texture and the game will
 * take it instead.
 */
const fs = require('fs');
const path = require('path');

const out = process.argv[2] || 'creatures/fish.glb';

/* ---- a little geometry ---- */
function normals(pos, idx){
  const n = new Float32Array(pos.length);
  for(let i=0;i<idx.length;i+=3){
    const a = idx[i]*3, b = idx[i+1]*3, c = idx[i+2]*3;
    const ux = pos[b]-pos[a], uy = pos[b+1]-pos[a+1], uz = pos[b+2]-pos[a+2];
    const vx = pos[c]-pos[a], vy = pos[c+1]-pos[a+1], vz = pos[c+2]-pos[a+2];
    const nx = uy*vz - uz*vy, ny = uz*vx - ux*vz, nz = ux*vy - uy*vx;
    for(const k of [a, b, c]){ n[k] += nx; n[k+1] += ny; n[k+2] += nz; }
  }
  for(let i=0;i<n.length;i+=3){
    const l = Math.hypot(n[i], n[i+1], n[i+2]) || 1;
    n[i] /= l; n[i+1] /= l; n[i+2] /= l;
  }
  return Array.from(n);
}

// ear clipping, for the flat outlines of the fins
function triangulate(poly){
  const area = poly.reduce((s, p, i) => { const q = poly[(i+1)%poly.length]; return s + p[0]*q[1] - q[0]*p[1]; }, 0);
  const idx = poly.map((_, i) => i);
  if(area < 0) idx.reverse();
  const tris = [];
  const inside = (p, a, b, c) => {
    const s = (u, v, w) => (v[0]-u[0])*(w[1]-u[1]) - (v[1]-u[1])*(w[0]-u[0]);
    return s(a, b, p) >= 0 && s(b, c, p) >= 0 && s(c, a, p) >= 0;
  };
  let guard = 0;
  while(idx.length > 3 && guard++ < 200){
    for(let i=0;i<idx.length;i++){
      const ia = idx[(i+idx.length-1)%idx.length], ib = idx[i], ic = idx[(i+1)%idx.length];
      const a = poly[ia], b = poly[ib], c = poly[ic];
      const cross = (b[0]-a[0])*(c[1]-a[1]) - (b[1]-a[1])*(c[0]-a[0]);
      if(cross <= 0) continue;
      if(idx.some(j => j !== ia && j !== ib && j !== ic && inside(poly[j], a, b, c))) continue;
      tris.push(ia, ib, ic);
      idx.splice(i, 1);
      break;
    }
  }
  tris.push(idx[0], idx[1], idx[2]);
  return tris;
}

const rotY = a => ([x, y, z]) => [x*Math.cos(a) + z*Math.sin(a), y, -x*Math.sin(a) + z*Math.cos(a)];
const move = (dx, dy, dz) => ([x, y, z]) => [x + dx, y + dy, z + dz];
const chain = (...fs) => p => fs.reduce((q, f) => f(q), p);

/* A flat outline in (x, y), given thickness along z, then placed by `xf`. */
function slab(poly, thick, xf, colour){
  const P = [], I = [], UV = [], C = [];
  const t = triangulate(poly);
  const n = poly.length;
  const xs = poly.map(p => p[0]), ys = poly.map(p => p[1]);
  const x0 = Math.min(...xs), x1 = Math.max(...xs), y0 = Math.min(...ys), y1 = Math.max(...ys);
  const put = (x, y, z, u, v) => { P.push(...xf([x, y, z])); UV.push(u, v); C.push(...colour); };
  for(const s of [1, -1]) for(const p of poly)
    put(p[0], p[1], s*thick/2, (p[0]-x0)/(x1-x0 || 1), (p[1]-y0)/(y1-y0 || 1));
  for(let i=0;i<t.length;i+=3){
    I.push(t[i], t[i+1], t[i+2]);                 // front
    I.push(n + t[i], n + t[i+2], n + t[i+1]);     // back
  }
  for(let i=0;i<n;i++){                            // the rim
    const j = (i+1)%n;
    I.push(i, n + i, j,  j, n + i, n + j);
  }
  return { pos:P, idx:I, uv:UV, col:C };
}

/* ---- the body ----
   Stations from the tail stock (the origin, z = 0) to the nose. A fish is TALL
   and THIN: the half-width is a fraction of the half-height, and that is most
   of what makes it read as a fish rather than as a dolphin. */
const FLAT = 0.40;                 // half-width / half-height
const stations = [
  [ 0.000, 0.016],
  [ 0.028, 0.034],
  [ 0.058, 0.050],
  [ 0.092, 0.058],
  [ 0.128, 0.056],
  [ 0.158, 0.046],
  [ 0.180, 0.030],
  [ 0.192, 0.014],
  [ 0.198, 0.000],
];

/* Its dress: a dark back, a pale belly, and two darker bands across — the
   simplest thing that reads as a reef fish from three metres. The game tints
   the whole shoal from this, so keep it light in value. */
function dress(y, h, z){
  const k = Math.max(0, Math.min(1, 0.5 + (y/Math.max(h, 1e-4))*0.62));   // 1 = back
  const band = Math.exp(-Math.pow((z - 0.062)/0.016, 2)) + Math.exp(-Math.pow((z - 0.132)/0.016, 2));
  const dark = Math.min(1, 0.72*k + 0.55*band);
  return [0.95 - 0.70*dark, 0.72 - 0.52*dark, 0.28 + 0.12*dark, 1];
}

function body(){
  const SEG = 14, P = [], I = [], UV = [], C = [];
  for(let j=0;j<stations.length;j++){
    const [z, h] = stations[j];
    for(let i=0;i<=SEG;i++){
      const a = i/SEG*Math.PI*2;
      const y = Math.cos(a)*h, x = Math.sin(a)*h*FLAT;
      P.push(x, y, z);
      UV.push(i/SEG, z/0.198);
      C.push(...dress(y, h, z));
    }
  }
  for(let j=0;j<stations.length-1;j++) for(let i=0;i<SEG;i++){
    const a = j*(SEG+1) + i, b = a + SEG + 1;
    I.push(a, b, a+1,  a+1, b, b+1);
  }
  return { pos:P, idx:I, uv:UV, col:C };
}

const FIN = [0.92, 0.66, 0.24, 1];   // the fins, thinner in colour than the flank

const parts = [
  body(),
  // the caudal fin, forked, aft of the stock (-z). Outline in (z, y), so the
  // slab is laid in x and turned a quarter turn.
  slab([[0, 0.012], [-0.052, 0.046], [-0.040, 0.004], [-0.052, -0.046], [0, -0.012]],
       0.004, chain(rotY(Math.PI/2)), FIN),
  // the dorsal, along the back
  slab([[0.030, 0], [0.118, 0], [0.098, 0.030], [0.040, 0.026]],
       0.004, chain(rotY(Math.PI/2), move(0, 0.046, 0)), FIN),
  // the anal fin, under the belly aft
  slab([[0.020, 0], [0.070, 0], [0.056, -0.022], [0.026, -0.020]],
       0.004, chain(rotY(Math.PI/2), move(0, -0.040, 0)), FIN),
  // two pectorals, just abaft the head
  slab([[0, 0], [0.030, -0.006], [0.034, -0.020], [0, -0.014]],
       0.003, chain(rotY(0.6), move(0.020, -0.004, 0.130)), FIN),
  slab([[0, 0], [0.030, -0.006], [0.034, -0.020], [0, -0.014]],
       0.003, chain(rotY(Math.PI - 0.6), move(-0.020, -0.004, 0.130)), FIN),
];

/* ---- merged into ONE surface: a MultiMesh instances a mesh, not a scene ---- */
const pos = [], idx = [], uv = [], col = [];
for(const p of parts){
  const base = pos.length/3;
  pos.push(...p.pos); uv.push(...p.uv); col.push(...p.col);
  for(const i of p.idx) idx.push(base + i);
}

const materials = [
  { name:'ecaille', doubleSided:true,
    pbrMetallicRoughness:{ baseColorFactor:[1, 1, 1, 1], metallicFactor:0.15, roughnessFactor:0.38 } }
];

/* ---- pack ---- */
const pad4 = n => (n + 3) & ~3;
const chunks = [], bufferViews = [], accessors = [];
let offset = 0;
function accessor(typed, type, target, minmax){
  const b = Buffer.from(typed.buffer, typed.byteOffset, typed.byteLength);
  const padded = Buffer.alloc(pad4(b.length)); b.copy(padded);
  chunks.push(padded);
  bufferViews.push({ buffer:0, byteOffset:offset, byteLength:b.length, target });
  offset += padded.length;
  const n = { VEC4:4, VEC3:3, VEC2:2, SCALAR:1 }[type];
  const a = { bufferView:bufferViews.length - 1, componentType: typed instanceof Float32Array ? 5126 : 5125,
              count: typed.length/n, type };
  if(minmax){
    const mn = [Infinity, Infinity, Infinity], mx = [-Infinity, -Infinity, -Infinity];
    for(let i=0;i<typed.length;i+=3) for(let k=0;k<3;k++){ mn[k] = Math.min(mn[k], typed[i+k]); mx[k] = Math.max(mx[k], typed[i+k]); }
    a.min = mn; a.max = mx;
  }
  accessors.push(a);
  return accessors.length - 1;
}
const attrs = {
  POSITION:   accessor(new Float32Array(pos), 'VEC3', 34962, true),
  NORMAL:     accessor(new Float32Array(normals(pos, idx)), 'VEC3', 34962),
  TEXCOORD_0: accessor(new Float32Array(uv), 'VEC2', 34962),
  COLOR_0:    accessor(new Float32Array(col), 'VEC4', 34962)
};
const gltf = {
  asset:{ version:'2.0', generator:'naval-sim fish-glb' },
  scene:0, scenes:[{ name:'poisson', nodes:[0] }],
  nodes:[{ name:'poisson', mesh:0 }],
  meshes:[{ name:'poisson', primitives:[{ attributes:attrs, indices:accessor(new Uint32Array(idx), 'SCALAR', 34963), material:0 }] }],
  materials,
  buffers:[{ byteLength:0 }], bufferViews, accessors
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
fs.mkdirSync(path.dirname(out), { recursive:true });
fs.writeFileSync(out, Buffer.concat([header, jh, jsonChunk, bh, binChunk]));
console.log('wrote ' + out + '  (' + pos.length/3 + ' vertices, ' + idx.length/3 + ' triangles, ' +
            (fs.statSync(out).size/1024).toFixed(1) + ' Ko)');
