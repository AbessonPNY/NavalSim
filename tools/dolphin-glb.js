/* A starting model for the dolphin, to be opened in Blender and made better.
 *
 *   node tools/dolphin-glb.js                    # writes creatures/dolphin.glb
 *   node tools/dolphin-glb.js autre/chemin.glb
 *
 * It is the drawn dolphin, written out, in metres. The game reads it by NAME:
 *
 *   queue    the flukes. Its ORIGIN is the hinge at the tail stock; the game
 *            beats it about its own X axis. Anything parented to it beats too.
 *   the rest the body — here corps, aileron, nageoire_gauche, nageoire_droite.
 *            Nose toward -Y in Blender (the glTF front, +Z), back up (+Z).
 *
 * The body carries vertex colours (dark back, pale belly) and UVs (u round the
 * body, v nose to tail) for a painted texture. Pack textures into the .glb.
 */
const fs = require('fs');
const path = require('path');

const out = process.argv[2] || 'creatures/dolphin.glb';

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

// ear clipping, for the small flat outlines of the fins
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

/* A flat outline in (x, y), given thickness along z, then placed by `xf`
   (a function mapping [x, y, z] to the model's frame). */
function slab(name, poly, thick, xf, material, colour){
  const P = [], I = [], UV = [], C = [];
  const t = triangulate(poly);
  const n = poly.length;
  const put = (x, y, z, u, v) => { P.push(...xf([x, y, z])); UV.push(u, v); C.push(...colour); };
  const xs = poly.map(p => p[0]), ys = poly.map(p => p[1]);
  const x0 = Math.min(...xs), x1 = Math.max(...xs), y0 = Math.min(...ys), y1 = Math.max(...ys);
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
  return { name, pos:P, idx:I, uv:UV, col:C, material };
}

const rotX = a => ([x, y, z]) => [x, y*Math.cos(a) - z*Math.sin(a), y*Math.sin(a) + z*Math.cos(a)];
const rotY = a => ([x, y, z]) => [x*Math.cos(a) + z*Math.sin(a), y, -x*Math.sin(a) + z*Math.cos(a)];
const move = (dx, dy, dz) => ([x, y, z]) => [x + dx, y + dy, z + dz];
const chain = (...fs) => p => fs.reduce((q, f) => f(q), p);

/* ---- the body: a turned profile, nose (+z) to tail stock ---- */
const prof = [[0.0, 0.0], [0.035, 0.03], [0.05, 0.12], [0.06, 0.22], [0.13, 0.30],
              [0.21, 0.48], [0.26, 0.78], [0.27, 1.05], [0.23, 1.45], [0.15, 1.85],
              [0.08, 2.15], [0.05, 2.35]];
function body(){
  const SEG = 24, P = [], I = [], UV = [], C = [];
  for(let j=0;j<prof.length;j++){
    const [r, s] = prof[j];
    for(let i=0;i<=SEG;i++){
      const a = i/SEG*Math.PI*2;
      const x = Math.sin(a)*r*0.85, y = Math.cos(a)*r, z = 1.2 - s;
      P.push(x, y, z);
      UV.push(i/SEG, s/2.35);
      const k = Math.max(0, Math.min(1, 0.5 + (y/0.27)*0.9));   // dark back, pale belly
      C.push(0.78 - 0.44*k, 0.80 - 0.42*k, 0.82 - 0.38*k, 1);
    }
  }
  for(let j=0;j<prof.length-1;j++) for(let i=0;i<SEG;i++){
    const a = j*(SEG+1) + i, b = a + SEG + 1;
    I.push(a, a+1, b,  b, a+1, b+1);
  }
  return { name:'corps', pos:P, idx:I, uv:UV, col:C, material:0 };
}

const grey = (r, g, b) => [r, g, b, 1];
const meshes = [
  body(),
  slab('aileron', [[0.18, 0], [-0.22, 0], [-0.28, 0.34], [-0.12, 0.2]], 0.03,
       chain(rotY(Math.PI/2), move(0, 0.24, -0.1)), 1, grey(0.33, 0.37, 0.43)),
  slab('nageoire_gauche', [[0, 0], [0.32, -0.08], [0.36, -0.14], [0, -0.1]], 0.025,
       chain(rotY(0.5), move(0.18, -0.1, 0.55)), 1, grey(0.36, 0.40, 0.46)),
  slab('nageoire_droite', [[0, 0], [0.32, -0.08], [0.36, -0.14], [0, -0.1]], 0.025,
       chain(rotY(Math.PI - 0.5), move(-0.18, -0.1, 0.55)), 1, grey(0.36, 0.40, 0.46)),
  // the flukes, in their own frame: hinge at the origin, trailing aft (-z)
  slab('queue', [[0, 0], [0.38, -0.22], [0.42, -0.1], [0.12, 0.04], [-0.12, 0.04], [-0.42, -0.1], [-0.38, -0.22]], 0.03,
       rotX(Math.PI/2), 1, grey(0.34, 0.38, 0.44))
];

const materials = [
  { name:'peau', pbrMetallicRoughness:{ baseColorFactor:[1, 1, 1, 1], metallicFactor:0.05, roughnessFactor:0.32 } },
  { name:'nageoires', doubleSided:true,
    pbrMetallicRoughness:{ baseColorFactor:[1, 1, 1, 1], metallicFactor:0.05, roughnessFactor:0.35 } }
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
const gMeshes = [], nodes = [];
for(const m of meshes){
  const attrs = {
    POSITION: accessor(new Float32Array(m.pos), 'VEC3', 34962, true),
    NORMAL:   accessor(new Float32Array(normals(m.pos, m.idx)), 'VEC3', 34962),
    TEXCOORD_0: accessor(new Float32Array(m.uv), 'VEC2', 34962)
  };
  attrs.COLOR_0 = accessor(new Float32Array(m.col), 'VEC4', 34962);   // RGBA per vertex
  gMeshes.push({ name:m.name, primitives:[{ attributes:attrs, indices:accessor(new Uint32Array(m.idx), 'SCALAR', 34963), material:m.material }] });
  const node = { name:m.name, mesh:gMeshes.length - 1 };
  if(m.name === 'queue') node.translation = [0, 0, -1.15];   // the hinge
  nodes.push(node);
}
const bin = Buffer.concat(chunks);
const gltf = {
  asset:{ version:'2.0', generator:'naval-sim dolphin-glb' },
  scene:0, scenes:[{ name:'dauphin', nodes:nodes.map((_, i) => i) }],
  nodes, meshes:gMeshes, materials,
  buffers:[{ byteLength:bin.length }], bufferViews, accessors
};
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
console.log('wrote ' + out + '  (' + meshes.map(m => m.name + ' ' + m.pos.length/3).join(', ') + ' vertices)');
