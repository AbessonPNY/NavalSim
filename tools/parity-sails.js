/* Releve la TOILE telle que ship-model.js la tisse et la forme, et l'ecrit en
   JSON. Le pendant C# (SailCloth, BraceTrim) refait les memes voiles et compare.

   Les methodes sont lues dans ship-model.js par leur texte — le fichier entier
   ne se charge pas sans un three.js complet — et appelees sur un objet qui ne
   porte que ce qu'elles touchent. Pas une ligne du projet recopiee : si l'une
   change, le banc voit la nouvelle.

   Ce qui est recopie, c'est le DECOR three.js : Vector3, les attributs Float32
   et computeVertexNormals, ecrits comme dans la r160 que la page charge. */
const fs = require('fs');
const path = require('path');

const root = path.join(__dirname, '..');
const src = fs.readFileSync(path.join(root, 'js', 'ship-model.js'), 'utf8');

function method(name) {
  const re = new RegExp('\\n  ' + name + '\\(([^)]*)\\)\\s*\\{');
  const m = re.exec(src);
  if (!m) throw new Error(name + ' introuvable dans ship-model.js');
  let depth = 0, end = src.indexOf('{', m.index);
  for (let i = end; i < src.length; i++) {
    if (src[i] === '{') depth++;
    else if (src[i] === '}' && --depth === 0) { end = i + 1; break; }
  }
  return eval('({' + src.slice(m.index + 1, end) + '})')[name];
}

// --- le decor three.js r160, reduit a ce que ces methodes touchent ---
class Vector3 {
  constructor(x = 0, y = 0, z = 0) { this.x = x; this.y = y; this.z = z; }
  set(x, y, z) { this.x = x; this.y = y; this.z = z; return this; }
  clone() { return new Vector3(this.x, this.y, this.z); }
  copy(v) { this.x = v.x; this.y = v.y; this.z = v.z; return this; }
  add(v) { this.x += v.x; this.y += v.y; this.z += v.z; return this; }
  addScaledVector(v, s) { this.x += v.x * s; this.y += v.y * s; this.z += v.z * s; return this; }
  subVectors(a, b) { this.x = a.x - b.x; this.y = a.y - b.y; this.z = a.z - b.z; return this; }
  multiplyScalar(s) { this.x *= s; this.y *= s; this.z *= s; return this; }
  divideScalar(s) { return this.multiplyScalar(1 / s); }
  length() { return Math.sqrt(this.x * this.x + this.y * this.y + this.z * this.z); }
  normalize() { return this.divideScalar(this.length() || 1); }
  distanceTo(v) { const dx = this.x - v.x, dy = this.y - v.y, dz = this.z - v.z; return Math.sqrt(dx * dx + dy * dy + dz * dz); }
  lerpVectors(v1, v2, a) {
    this.x = v1.x + (v2.x - v1.x) * a; this.y = v1.y + (v2.y - v1.y) * a; this.z = v1.z + (v2.z - v1.z) * a; return this;
  }
  cross(v) {
    const ax = this.x, ay = this.y, az = this.z, bx = v.x, by = v.y, bz = v.z;
    this.x = ay * bz - az * by; this.y = az * bx - ax * bz; this.z = ax * by - ay * bx; return this;
  }
  fromBufferAttribute(a, i) { this.x = a.array[i * 3]; this.y = a.array[i * 3 + 1]; this.z = a.array[i * 3 + 2]; return this; }
}
class Float32BufferAttribute {
  constructor(arr, size) { this.array = new Float32Array(arr); this.itemSize = size; this.count = this.array.length / size; }
  setXYZ(i, x, y, z) { this.array[i * 3] = x; this.array[i * 3 + 1] = y; this.array[i * 3 + 2] = z; }
}
class BufferGeometry {
  constructor() { this.attributes = {}; this.index = null; }
  setAttribute(n, a) { this.attributes[n] = a; }
  setIndex(i) { this.index = i; }
  computeVertexNormals() {
    const pos = this.attributes.position;
    let nor = this.attributes.normal;
    if (!nor) { nor = new Float32BufferAttribute(new Float32Array(pos.count * 3), 3); this.attributes.normal = nor; }
    else for (let i = 0; i < nor.count; i++) nor.setXYZ(i, 0, 0, 0);
    const pA = new Vector3(), pB = new Vector3(), pC = new Vector3();
    const nA = new Vector3(), nB = new Vector3(), nC = new Vector3();
    const cb = new Vector3(), ab = new Vector3();
    const idx = this.index;
    for (let i = 0; i < idx.length; i += 3) {
      const vA = idx[i], vB = idx[i + 1], vC = idx[i + 2];
      pA.fromBufferAttribute(pos, vA); pB.fromBufferAttribute(pos, vB); pC.fromBufferAttribute(pos, vC);
      cb.subVectors(pC, pB); ab.subVectors(pA, pB); cb.cross(ab);
      nA.fromBufferAttribute(nor, vA); nB.fromBufferAttribute(nor, vB); nC.fromBufferAttribute(nor, vC);
      nA.add(cb); nB.add(cb); nC.add(cb);
      nor.setXYZ(vA, nA.x, nA.y, nA.z); nor.setXYZ(vB, nB.x, nB.y, nB.z); nor.setXYZ(vC, nC.x, nC.y, nC.z);
    }
    const v = new Vector3();
    for (let i = 0; i < nor.count; i++) { v.fromBufferAttribute(nor, i); v.normalize(); nor.setXYZ(i, v.x, v.y, v.z); }
  }
}
class Mesh { constructor(g, m) { this.geometry = g; this.material = m; this.userData = {}; this.visible = true; } }
global.THREE = { Vector3, Float32BufferAttribute, BufferGeometry, Mesh };

// les constantes de brassage, lues dans le fichier elles aussi
global.Naval = {};
for (const k of ['BRACE_RATE', 'BRACE_HOLD', 'BRACE_ACCEL']) {
  const m = new RegExp('Naval\\.' + k + '\\s*=\\s*([0-9.]+)').exec(src);
  if (!m) throw new Error(k + ' introuvable');
  Naval[k] = parseFloat(m[1]);
}

const bellyProfile = method('_bellyProfile');
const sailSurface = method('_sailSurface');
const setSailShape = method('setSailShape');
const setTrim = method('setTrim');

const V = (x, y, z) => new Vector3(x, y, z);
const SQUARE = { kind: 'square', vPeak: 0.30, vPin1: false, free: 0.25, crown: 0.55,
                 uPin0: false, uPin1: false, freeU: 0.35, hangU0: true, hangU1: true,
                 bow: 0.05, roachFoot: 0.11 };
// des voiles de chaque sorte, aux coins que les gréements leur donnent
const sails = [
  { id: 'carree-basse', corners: [V(-7.6, 12.4, 0), V(7.6, 12.4, 0), V(6.536, 4.3, 0), V(-6.536, 4.3, 0)], dir: V(0, 0, 1), cut: SQUARE },
  { id: 'carree-hunier', corners: [V(-4.1, 20.1, 0.3), V(4.1, 20.1, 0.3), V(3.526, 14.9, 0.3), V(-3.526, 14.9, 0.3)], dir: V(0, 0, 1), cut: SQUARE },
  { id: 'aurique', corners: [V(0, 2.55, -0.2), V(0, 2.5, -9.3), V(0, 17.0, -7.0), V(0, 17.4, -0.2)], dir: V(1, 0, 0),
    cut: { kind: 'gaff', uPeak: 0.42, crown: 0.80 } },
  { id: 'foc', corners: [V(0, 3.1, 1.5), V(0, 2.4, -4.4), V(0, 18.2, -4.85)], dir: V(1, 0, 0),
    cut: { kind: 'jib', uPeak: 0.40, vPeak: 0.34, vPin0: false, crown: 0.80 } },
  { id: 'bulle', corners: [V(-2, 6, 0), V(2, 6, 0), V(2, 1, 0), V(-2, 1, 0)], dir: V(0, 0, 1), cut: null }
];
const states = [
  { load: 60, luffing: false, t: 3.7, set: 1 },
  { load: 12, luffing: false, t: 8.2, set: 1 },
  { load: 40, luffing: true, t: 11.35, set: 1 },
  { load: 0, luffing: false, t: 1.0, set: 0 },
  { load: 25, luffing: false, t: 5.5, set: 0.43 },
  { load: 90, luffing: true, t: 42.123, set: 0.8 }
];
const bellies = [1.65, 3.3, 0];   // 0 : l'original le prend pour « absent »

const out = { bellyProfile: [], sails: [], trim: [] };
for (const x of [0, 0.1, 0.3, 0.42, 0.5, 0.77, 1])
  for (const [d, p0, p1, r, c] of [[0.5, true, true, 0.78, 1], [0.3, true, false, 0.25, 1], [0.42, false, false, 0.35, 0.55]])
    out.bellyProfile.push({ x, d, p0, p1, r, c, f: bellyProfile(x, d, p0, p1, r, c) });

for (const s of sails) for (const belly of bellies) {
  const self = { canvases: [], spec: { rig: { belly } }, _canvasMat: () => null, _bellyProfile: bellyProfile };
  const mesh = sailSurface.call(self, s.corners, s.dir, s.cut);
  const u = mesh.userData.sail;
  const rec = {
    id: s.id, belly, corners: s.corners.map(c => [c.x, c.y, c.z]), dir: [s.dir.x, s.dir.y, s.dir.z], cut: s.cut,
    base: Array.from(u.base), w: Array.from(u.w), sag: Array.from(u.sag), u: Array.from(u.u),
    swag: Array.from(u.swag), nSwag: u.nSwag, nu1: u.nu1,
    uv: Array.from(mesh.geometry.attributes.uv.array), index: mesh.geometry.index, shapes: []
  };
  for (const st of states) {
    setSailShape.call(self, st.load, st.luffing, st.t, st.set);
    rec.shapes.push({ ...st, pos: Array.from(mesh.geometry.attributes.position.array),
                      nor: Array.from(mesh.geometry.attributes.normal.array) });
  }
  out.sails.push(rec);
}

/* Le brassage : une sequence qui vire de bord, revient avant le delai, faseye,
   borde en route — et le pas qui franchit la marque. */
{
  const rig = { rotation: { y: 0 } }, jib = { rotation: { y: 0 } };
  const self = { rigs: [rig, jib], jibRig: jib, canvases: [], setSailShape() {} };
  let t = 0;
  const seq = [];
  for (let i = 0; i < 900; i++) {
    t += i % 7 === 0 ? 0.031 : 1 / 60;
    const tack = (i > 120 && i < 180) || i > 300 ? -1 : 1;
    const sheet = i < 500 ? 0.6 : 0.6 + 0.4 * Math.sin(i * 0.02);
    const luffing = i > 650 && i < 700;
    setTrim.call(self, sheet, tack, 1, luffing, t, 20);
    seq.push({ sheet, tack, luffing, t, rig: rig.rotation.y, jib: jib.rotation.y });
  }
  out.trim = seq;
}

/* Les pavillons : la grille de chaque coupe (_flagAt), puis quelques images de
   leur ondulation (setFlag) — brise franche, brise mourante, calme. La phase
   tiree au hasard est posee a la main apres coup, pour que le C# ait la meme. */
{
  class Group {
    constructor() { this.position = new Vector3(); this.rotation = { x: 0, y: 0 }; this.children = []; }
    add(o) { this.children.push(o); o.parent = this; }
  }
  THREE.Group = Group;
  const m = /Naval\.FLAG_SHAPES\s*=\s*(\{[\s\S]*?\n\});/.exec(src);
  if (!m) throw new Error('FLAG_SHAPES introuvable');
  Naval.FLAG_SHAPES = eval('(' + m[1] + ')');
  const flagAt = method('_flagAt'), setFlag = method('setFlag');
  const cases = [
    { spec: { L: 60 }, l: {} },
    { spec: { L: 60 }, l: { shape: 'swallowtail', size: 1.6 } },
    { spec: { L: 24 }, l: { shape: 'pennant', length: 2.4 } },
    { spec: { L: 60 }, l: { shape: 'streamer', size: 0.8 } },
    { spec: { L: 38 }, l: { shape: 'nimportequoi', size: 0.6 } }
  ];
  out.flags = [];
  cases.forEach((c, n) => {
    const self = { spec: c.spec, mats: { flag: {} }, _nationMat() { return {}; }, _flagMat() { return {}; } };
    const f = flagAt.call(self, new Group(), 0, 10, 0, c.l, 0);
    f.seed = 0.37 + n * 1.13;
    const g = f.mesh.geometry;
    const rec = { spec: c.spec, l: c.l, seed: f.seed, hoist: f.hoist, fly: f.fly, wave: f.wave, shape: f.shape,
                  base: Array.from(f.base), u: Array.from(f.u), v: Array.from(f.v),
                  uv: Array.from(g.attributes.uv.array), idx: Array.from(g.index), frames: [] };
    for (const [beta, tack, vApp, t] of [[2.4, 1, 9.5, 3.1], [0.7, -1, 4.2, 17.25], [1.9, 1, 0.3, 40.0]]) {
      setFlag.call({ flags: [f] }, beta, tack, vApp, t);
      rec.frames.push({ beta, tack, vApp, t, yaw: f.pivot.rotation.y,
                        pos: Array.from(g.attributes.position.array), nor: Array.from(g.attributes.normal.array) });
    }
    out.flags.push(rec);
  });
}

const dest = path.join(root, 'core', 'parity-sails.json');
fs.writeFileSync(dest, JSON.stringify(out));
console.log('releve des voiles ecrit : ' + dest + '  (' + out.sails.length + ' voiles x ' + states.length + ' etats, ' + out.trim.length + ' pas de brassage)');
