/* A starting model for the men on deck, to be opened in Blender and made better.
 *
 *   node tools/sailor-glb.js                    # writes creatures/sailor.glb
 *   node tools/sailor-glb.js autre/chemin.glb
 *
 * It is the drawn seaman of js/crew.js, written out, in metres: feet on the
 * origin, standing up (+Z in Blender, +Y in glTF), facing the glTF front (+Z;
 * -Y in Blender). The game animates him in the vertex shader, and tells the
 * parts apart by MESH NAME:
 *
 *   jambes   legs, breeches, shoes — the feet part and the knees bend
 *   corps    the shirt — tinted per man; paint it light, the tint multiplies
 *   bras     arms and hands — they swing out from the shoulder (x ±0.235,
 *            height 1.41), so keep the shoulders about there
 *   tete     neck, head, cap — it turns about the vertical axis
 *   anything else moves with the body but has no part of its own
 *
 * The whole figure leans about the feet and breathes above the waist
 * (about 1.0 to 1.45 m). Keep him about 1.75 m tall and few triangles: every
 * man aboard is drawn, the shader runs on each of his vertices.
 *
 * Vertex colours carry the colours; UVs are there for a texture. Pack
 * textures into the .glb. Several materials are fine.
 */
const fs = require('fs');
const path = require('path');

const out = process.argv[2] || 'creatures/sailor.glb';

/* ---- primitives, in the shapes three.js makes them ---- */
function cylinder(rTop, rBot, h, seg){
  const P = [], N = [], UV = [], I = [];
  const slope = (rBot - rTop)/h;
  for(let j = 0; j <= 1; j++){                       // side, two rings
    const y = h/2 - j*h, r = j ? rBot : rTop;
    for(let i = 0; i <= seg; i++){
      const a = i/seg*Math.PI*2, s = Math.sin(a), c = Math.cos(a);
      P.push(r*s, y, r*c);
      const l = Math.hypot(1, slope);
      N.push(s/l, slope/l, c/l);
      UV.push(i/seg, 1 - j);
    }
  }
  for(let i = 0; i < seg; i++){
    const a = i, b = seg + 1 + i;
    I.push(a, b, a + 1,  b, b + 1, a + 1);
  }
  for(const top of [true, false]){                   // caps
    const y = top ? h/2 : -h/2, r = top ? rTop : rBot, ny = top ? 1 : -1;
    const c0 = P.length/3;
    P.push(0, y, 0); N.push(0, ny, 0); UV.push(0.5, 0.5);
    for(let i = 0; i <= seg; i++){
      const a = i/seg*Math.PI*2;
      P.push(r*Math.sin(a), y, r*Math.cos(a)); N.push(0, ny, 0);
      UV.push(0.5 + 0.5*Math.sin(a), 0.5 + 0.5*Math.cos(a));
    }
    for(let i = 0; i < seg; i++)
      if(top) I.push(c0, c0 + 1 + i, c0 + 2 + i); else I.push(c0, c0 + 2 + i, c0 + 1 + i);
  }
  return { P, N, UV, I };
}

function sphere(r, ws, hs){
  const P = [], N = [], UV = [], I = [];
  for(let j = 0; j <= hs; j++){
    const v = j/hs, th = v*Math.PI;
    for(let i = 0; i <= ws; i++){
      const u = i/ws, ph = u*Math.PI*2;
      const x = -Math.cos(ph)*Math.sin(th), y = Math.cos(th), z = Math.sin(ph)*Math.sin(th);
      P.push(r*x, r*y, r*z); N.push(x, y, z); UV.push(u, 1 - v);
    }
  }
  for(let j = 0; j < hs; j++) for(let i = 0; i < ws; i++){
    const a = j*(ws + 1) + i + 1, b = j*(ws + 1) + i, c = (j + 1)*(ws + 1) + i, d = (j + 1)*(ws + 1) + i + 1;
    if(j !== 0) I.push(a, b, d);
    if(j !== hs - 1) I.push(b, c, d);
  }
  return { P, N, UV, I };
}

function box(w, h, d){
  const P = [], N = [], UV = [], I = [];
  const faces = [
    [[1,0,0], [0,0,-1], [0,1,0]], [[-1,0,0], [0,0,1], [0,1,0]],
    [[0,1,0], [1,0,0], [0,0,-1]], [[0,-1,0], [1,0,0], [0,0,1]],
    [[0,0,1], [1,0,0], [0,1,0]], [[0,0,-1], [-1,0,0], [0,1,0]]
  ];
  const half = [w/2, h/2, d/2];
  for(const [n, u, v] of faces){
    const o = P.length/3;
    for(const [su, sv] of [[-1,-1],[1,-1],[1,1],[-1,1]]){
      for(let k = 0; k < 3; k++) P.push((n[k] + u[k]*su + v[k]*sv)*half[k]);
      N.push(...n); UV.push((su + 1)/2, (sv + 1)/2);
    }
    I.push(o, o + 1, o + 2,  o, o + 2, o + 3);
  }
  return { P, N, UV, I };
}

// transforms applied to points and normals
function xform(g, f, fn){
  for(let i = 0; i < g.P.length; i += 3){
    const p = f([g.P[i], g.P[i+1], g.P[i+2]]); g.P.splice(i, 3, ...p);
    const n = (fn || f)([g.N[i], g.N[i+1], g.N[i+2]]);
    const l = Math.hypot(...n) || 1; g.N.splice(i, 3, n[0]/l, n[1]/l, n[2]/l);
  }
  return g;
}
const move = (dx, dy, dz) => g => xform(g, ([x, y, z]) => [x + dx, y + dy, z + dz], p => p);
const rotZ = a => g => xform(g, ([x, y, z]) => [x*Math.cos(a) - y*Math.sin(a), x*Math.sin(a) + y*Math.cos(a), z]);
const rotX = a => g => xform(g, ([x, y, z]) => [x, y*Math.cos(a) - z*Math.sin(a), y*Math.sin(a) + z*Math.cos(a)]);
const scale = (sx, sy, sz) => g => xform(g, ([x, y, z]) => [x*sx, y*sy, z*sz], ([x, y, z]) => [x/sx, y/sy, z/sz]);
const chain = (g, ...fs) => fs.reduce((q, f) => f(q), g);

/* ---- the figure, as in js/crew.js ---- */
const SKIN = [0.62, 0.45, 0.34], SLOPS = [0.66, 0.62, 0.52],
      SHOE = [0.12, 0.10, 0.09], CAP = [0.46, 0.16, 0.13], SHIRT = [1, 1, 1];
const parts = { jambes: [], corps: [], bras: [], tete: [] };
const put = (part, g, col) => parts[part].push({ g, col });
for(const s of [-1, 1]){
  put('jambes', chain(cylinder(0.075, 0.062, 0.78, 7), move(s*0.095, 0.47, 0)), SLOPS);
  put('jambes', chain(box(0.10, 0.07, 0.24), move(s*0.095, 0.035, 0.04)), SHOE);
  put('bras', chain(cylinder(0.052, 0.042, 0.62, 6), rotZ(s*0.06), move(s*0.235, 1.10, 0)), SLOPS);
  put('bras', chain(sphere(0.048, 6, 4), move(s*0.255, 0.76, 0.01)), SKIN);
}
put('jambes', chain(cylinder(0.19, 0.17, 0.24, 8), move(0, 0.90, 0)), SLOPS);
put('corps', chain(cylinder(0.195, 0.18, 0.52, 8), scale(1, 1, 0.68), move(0, 1.25, 0)), SHIRT);
put('tete', chain(sphere(0.075, 6, 4), move(0, 1.525, 0)), SKIN);
put('tete', chain(sphere(0.105, 9, 7), move(0, 1.62, 0.005)), SKIN);
put('tete', chain(cylinder(0.07, 0.108, 0.13, 9), rotX(-0.25), move(0, 1.72, -0.02)), CAP);

const meshes = Object.entries(parts).map(([name, list]) => {
  const pos = [], nor = [], uv = [], col = [], idx = [];
  for(const { g, col: c } of list){
    const o = pos.length/3;
    pos.push(...g.P); nor.push(...g.N); uv.push(...g.UV);
    for(let i = 0; i < g.P.length/3; i++) col.push(c[0], c[1], c[2], 1);
    idx.push(...g.I.map(i => i + o));
  }
  return { name, pos, nor, uv, col, idx };
});

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
    for(let i = 0; i < typed.length; i += 3) for(let k = 0; k < 3; k++){
      mn[k] = Math.min(mn[k], typed[i+k]); mx[k] = Math.max(mx[k], typed[i+k]);
    }
    a.min = mn; a.max = mx;
  }
  accessors.push(a);
  return accessors.length - 1;
}
const gMeshes = [], nodes = [];
for(const m of meshes){
  const attributes = {
    POSITION: accessor(new Float32Array(m.pos), 'VEC3', 34962, true),
    NORMAL: accessor(new Float32Array(m.nor), 'VEC3', 34962),
    TEXCOORD_0: accessor(new Float32Array(m.uv), 'VEC2', 34962),
    COLOR_0: accessor(new Float32Array(m.col), 'VEC4', 34962)
  };
  gMeshes.push({ name:m.name, primitives:[{ attributes, indices:accessor(new Uint32Array(m.idx), 'SCALAR', 34963), material:0 }] });
  nodes.push({ name:m.name, mesh:gMeshes.length - 1 });
}
const bin = Buffer.concat(chunks);
const gltf = {
  asset:{ version:'2.0', generator:'naval-sim sailor-glb' },
  scene:0, scenes:[{ name:'marin', nodes:nodes.map((_, i) => i) }],
  nodes, meshes:gMeshes,
  materials:[{ name:'marin', pbrMetallicRoughness:{ baseColorFactor:[1, 1, 1, 1], metallicFactor:0, roughnessFactor:0.88 } }],
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
console.log('wrote ' + out + '  (' + meshes.map(m => m.name + ' ' + m.idx.length/3).join(', ') + ' triangles)');
