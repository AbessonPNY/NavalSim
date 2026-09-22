/* LES BÂTIMENTS D'UNE VILLE, écrits en .glb — une église et deux maisons.
 *
 *   node tools/town-glb.js
 *
 * Ce que le jeu attend (world/README.md) : l'ORIGINE AU SOL, au milieu de
 * l'emprise ; les murs vers le haut ; la façade vers +z. Une matière par rôle,
 * et c'est le NOM qui compte, parce que Godot en fait un MultiMesh par matière
 * et allume les fenêtres la nuit :
 *
 *   mur · toit · bois · pierre · fenetre
 *
 * Tout est en mètres, à l'échelle de la Jamaïque du jeu (×0,4 sur les cartes,
 * mais les bâtiments sont à leur taille vraie : c'est la ville qui les espace).
 * Modifiez-les dans Blender si vous voulez, en gardant ces noms de matières.
 */
const fs = require('fs');
const path = require('path');

// ---------------------------------------------------------------- la boîte à outils

const MATS = ['mur', 'toit', 'bois', 'pierre', 'fenetre'];

function builder() {
  const by = new Map();
  for (const m of MATS) by.set(m, { pos: [], idx: [], nrm: [], uv: [] });

  /* Un quadrilatère, dans l'ordre trigonométrique vu de l'extérieur. Normale
     plate : un bâtiment est fait d'arêtes vives, et lisser les normales lui
     donnerait l'air d'un galet. */
  function quad(mat, a, b, c, d) {
    const g = by.get(mat);
    const n = [
      (b[1] - a[1]) * (c[2] - a[2]) - (b[2] - a[2]) * (c[1] - a[1]),
      (b[2] - a[2]) * (c[0] - a[0]) - (b[0] - a[0]) * (c[2] - a[2]),
      (b[0] - a[0]) * (c[1] - a[1]) - (b[1] - a[1]) * (c[0] - a[0])
    ];
    const L = Math.hypot(n[0], n[1], n[2]) || 1;
    const k = g.pos.length / 3;
    for (const [p, u, v] of [[a, 0, 0], [b, 1, 0], [c, 1, 1], [d, 0, 1]]) {
      g.pos.push(p[0], p[1], p[2]);
      g.nrm.push(n[0] / L, n[1] / L, n[2] / L);
      g.uv.push(u, v);
    }
    g.idx.push(k, k + 1, k + 2, k, k + 2, k + 3);
  }

  function tri(mat, a, b, c) { quad(mat, a, b, c, c); }

  /** Une boîte alignée sur les axes : de (x0,y0,z0) à (x1,y1,z1). */
  function box(mat, x0, x1, y0, y1, z0, z1, skip = '') {
    const A = [x0, y0, z0], B = [x1, y0, z0], C = [x1, y1, z0], D = [x0, y1, z0];
    const E = [x0, y0, z1], F = [x1, y0, z1], G = [x1, y1, z1], H = [x0, y1, z1];
    if (!skip.includes('S')) quad(mat, B, A, D, C);   // -z
    if (!skip.includes('N')) quad(mat, E, F, G, H);   // +z
    if (!skip.includes('W')) quad(mat, A, E, H, D);   // -x
    if (!skip.includes('E')) quad(mat, F, B, C, G);   // +x
    if (!skip.includes('T')) quad(mat, D, H, G, C);   // dessus
    if (!skip.includes('B')) quad(mat, A, B, F, E);   // dessous
  }

  /** Un toit à deux pentes : le faîte court selon x, au-dessus de l'emprise. */
  function gable(mat, x0, x1, y0, ridge, z0, z1, over = 0.35) {
    const a0 = x0 - over, a1 = x1 + over, c0 = z0 - over, c1 = z1 + over;
    const zc = (c0 + c1) / 2;
    const R0 = [a0, y0 + ridge, zc], R1 = [a1, y0 + ridge, zc];
    quad(mat, [a0, y0, c1], [a1, y0, c1], R1, R0);      // le pan nord
    quad(mat, [a1, y0, c0], [a0, y0, c0], R0, R1);      // le pan sud
    tri(mat, [a0, y0, c0], [a0, y0, c1], R0);           // le pignon ouest
    tri(mat, [a1, y0, c1], [a1, y0, c0], R1);           // le pignon est
  }

  /** Un toit à quatre pentes : le faîte court selon x, plus court que l'emprise. */
  function hip(mat, x0, x1, y0, ridge, z0, z1, over = 0.3) {
    const a0 = x0 - over, a1 = x1 + over, c0 = z0 - over, c1 = z1 + over;
    const zc = (c0 + c1) / 2, r = (a1 - a0) * 0.22;
    const R0 = [a0 + r, y0 + ridge, zc], R1 = [a1 - r, y0 + ridge, zc];
    quad(mat, [a0, y0, c1], [a1, y0, c1], R1, R0);
    quad(mat, [a1, y0, c0], [a0, y0, c0], R0, R1);
    tri(mat, [a0, y0, c0], [a0, y0, c1], R0);
    tri(mat, [a1, y0, c1], [a1, y0, c0], R1);
  }

  /** Une flèche à quatre pans sur une base carrée. */
  function spire(mat, cx, cz, half, y0, h) {
    const top = [cx, y0 + h, cz];
    const P = [[cx - half, y0, cz - half], [cx + half, y0, cz - half],
               [cx + half, y0, cz + half], [cx - half, y0, cz + half]];
    tri(mat, P[1], P[0], top); tri(mat, P[2], P[1], top);
    tri(mat, P[3], P[2], top); tri(mat, P[0], P[3], top);
  }

  /** Une fenêtre (ou une porte) posée à plat sur une façade, à 2 cm du mur. */
  function pane(mat, face, u0, u1, y0, y1, at) {
    const e = 0.02;
    if (face === 'N') quad(mat, [u0, y0, at + e], [u1, y0, at + e], [u1, y1, at + e], [u0, y1, at + e]);
    if (face === 'S') quad(mat, [u1, y0, at - e], [u0, y0, at - e], [u0, y1, at - e], [u1, y1, at - e]);
    if (face === 'E') quad(mat, [at + e, y0, u1], [at + e, y0, u0], [at + e, y1, u0], [at + e, y1, u1]);
    if (face === 'W') quad(mat, [at - e, y0, u0], [at - e, y0, u1], [at - e, y1, u1], [at - e, y1, u0]);
  }

  return { by, quad, tri, box, gable, hip, spire, pane };
}

// ---------------------------------------------------------------- les trois bâtiments

/* UNE CASE BASSE, à véranda : un rez-de-chaussée sur solin de pierre, toit à
   deux pentes débordant, quatre poteaux devant. Sept mètres sur cinq, comme les
   maisons de bois des ports des Antilles. */
function maisonA() {
  const b = builder();
  const W = 3.5, D = 2.5, H = 3.0;
  b.box('pierre', -W, W, -0.35, 0.35, -D, D);              // le solin
  b.box('mur', -W, W, 0.3, H, -D, D, 'B');
  b.gable('toit', -W, W, H, 1.9, -D, D, 0.45);
  // la véranda : quatre poteaux et son auvent, devant la façade
  for (const x of [-W + 0.4, -1.2, 1.2, W - 0.4]) b.box('bois', x - 0.09, x + 0.09, 0.3, 2.5, D + 1.1, D + 1.28);
  b.quad('bois', [-W, 2.5, D + 1.4], [W, 2.5, D + 1.4], [W, 2.72, D], [-W, 2.72, D]);
  b.pane('bois', 'N', -0.55, 0.55, 0.3, 2.35, D);           // la porte
  for (const x of [-2.3, 2.3]) b.pane('fenetre', 'N', x - 0.45, x + 0.45, 1.25, 2.35, D);
  for (const z of [-1.2, 1.2]) { b.pane('fenetre', 'E', z - 0.45, z + 0.45, 1.25, 2.35, W); b.pane('fenetre', 'W', z - 0.45, z + 0.45, 1.25, 2.35, -W); }
  return { name: 'maison-a', b, note: 'case basse à véranda, 7 × 5 m' };
}

/* UNE MAISON DE VILLE À ÉTAGE, toit à quatre pentes et balcon de bois : ce
   qu'on bâtissait en pierre autour d'une place, six mètres sur six. */
function maisonB() {
  const b = builder();
  const W = 3.0, D = 3.0, H = 6.2;
  b.box('pierre', -W, W, -0.4, 0.5, -D, D);
  b.box('mur', -W, W, 0.45, H, -D, D, 'B');
  b.hip('toit', -W, W, H, 2.0, -D, D, 0.4);
  // le bandeau qui marque l'étage, et le balcon sur la façade
  b.box('pierre', -W - 0.12, W + 0.12, 3.05, 3.25, -D - 0.12, D + 0.12);
  b.box('bois', -1.7, 1.7, 3.25, 3.35, D, D + 1.0);
  for (const x of [-1.7, 1.7]) b.box('bois', x - 0.07, x + 0.07, 3.35, 4.25, D + 0.85, D + 0.99);
  b.box('bois', -1.7, 1.7, 4.15, 4.25, D + 0.85, D + 0.99);
  b.pane('bois', 'N', -0.6, 0.6, 0.45, 2.6, D);             // la porte cochère
  for (const x of [-1.9, 1.9]) b.pane('fenetre', 'N', x - 0.5, x + 0.5, 1.1, 2.6, D);
  for (const x of [-1.9, 0, 1.9]) b.pane('fenetre', 'N', x - 0.5, x + 0.5, 3.6, 5.3, D);
  for (const z of [-1.5, 1.5]) for (const y of [[1.1, 2.6], [3.6, 5.3]]) {
    b.pane('fenetre', 'E', z - 0.5, z + 0.5, y[0], y[1], W);
    b.pane('fenetre', 'W', z - 0.5, z + 0.5, y[0], y[1], -W);
  }
  return { name: 'maison-b', b, note: 'maison de ville à étage et balcon, 6 × 6 m' };
}

/* L'ÉGLISE, une par ville : une nef de pierre orientée, son clocher carré en
   façade, sa flèche et sa croix. Neuf mètres sur quatorze, le clocher à
   quinze : elle se voit du large, et c'est à cela qu'elle sert. */
function eglise() {
  const b = builder();
  const W = 4.5, Z0 = -7, Z1 = 5.5, H = 6.0;
  b.box('pierre', -W - 0.2, W + 0.2, -0.4, 0.45, Z0 - 0.2, Z1 + 0.2);
  b.box('mur', -W, W, 0.4, H, Z0, Z1, 'B');
  b.gable('toit', -W, W, H, 3.2, Z0, Z1, 0.5);
  // l'abside, au levant
  b.box('mur', -2.4, 2.4, 0.4, H - 1.2, Z0 - 2.6, Z0, 'BN');
  b.gable('toit', -2.4, 2.4, H - 1.2, 1.6, Z0 - 2.6, Z0, 0.35);
  // le clocher, en façade
  const T = 2.2, TH = 12.5;
  b.box('pierre', -T, T, 0.4, TH, Z1, Z1 + 2 * T, 'B');
  b.box('pierre', -T - 0.25, T + 0.25, TH, TH + 0.4, Z1 - 0.25, Z1 + 2 * T + 0.25);   // la corniche
  b.spire('toit', 0, Z1 + T, T + 0.25, TH + 0.4, 4.2);
  // la croix
  b.box('bois', -0.07, 0.07, TH + 4.6, TH + 5.7, Z1 + T - 0.07, Z1 + T + 0.07);
  b.box('bois', -0.07, 0.07, TH + 5.15, TH + 5.35, Z1 + T - 0.42, Z1 + T + 0.42);
  // la porte sous le clocher, et les baies
  b.pane('bois', 'N', -0.8, 0.8, 0.4, 3.2, Z1 + 2 * T);
  for (const y of [[8.6, 10.6]]) {
    b.pane('fenetre', 'N', -0.7, 0.7, y[0], y[1], Z1 + 2 * T);
    b.pane('fenetre', 'E', Z1 + T - 0.7, Z1 + T + 0.7, y[0], y[1], T);
    b.pane('fenetre', 'W', Z1 + T - 0.7, Z1 + T + 0.7, y[0], y[1], -T);
  }
  for (const z of [-5, -2.4, 0.2, 2.8]) {
    b.pane('fenetre', 'E', z - 0.5, z + 0.5, 2.0, 4.6, W);
    b.pane('fenetre', 'W', z - 0.5, z + 0.5, 2.0, 4.6, -W);
  }
  return { name: 'eglise', b, note: 'église à clocher, nef 9 × 12,5 m, croix à 18 m' };
}

// ---------------------------------------------------------------- l'écriture

const COLOURS = {
  mur:     { c: [0.88, 0.85, 0.77, 1], r: 0.95 },
  toit:    { c: [0.56, 0.27, 0.19, 1], r: 0.88 },
  bois:    { c: [0.36, 0.25, 0.16, 1], r: 0.80 },
  pierre:  { c: [0.74, 0.71, 0.64, 1], r: 0.92 },
  fenetre: { c: [0.07, 0.08, 0.10, 1], r: 0.20 }
};

const pad4 = n => (n + 3) & ~3;

function write(out, name, by) {
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

  const materials = [], meshes = [], nodes = [];
  for (const [mat, g] of by) {
    if (g.pos.length === 0) continue;
    const col = COLOURS[mat];
    materials.push({
      name: mat,
      doubleSided: false,
      pbrMetallicRoughness: { baseColorFactor: col.c, metallicFactor: 0, roughnessFactor: col.r }
    });
    meshes.push({
      name: mat,
      primitives: [{
        attributes: {
          POSITION: accessor(new Float32Array(g.pos), 'VEC3', 34962, true),
          NORMAL: accessor(new Float32Array(g.nrm), 'VEC3', 34962),
          TEXCOORD_0: accessor(new Float32Array(g.uv), 'VEC2', 34962)
        },
        indices: accessor(new Uint32Array(g.idx), 'SCALAR', 34963),
        material: materials.length - 1
      }]
    });
    nodes.push({ name: mat, mesh: meshes.length - 1 });
  }

  const bin = Buffer.concat(chunks);
  const gltf = {
    asset: { version: '2.0', generator: 'naval-sim town-glb' },
    scene: 0, scenes: [{ name, nodes: nodes.map((_, i) => i) }],
    nodes, meshes, materials,
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
  fs.mkdirSync(path.dirname(out), { recursive: true });
  fs.writeFileSync(out, Buffer.concat([header, jh, jsonChunk, bh, binChunk]));
  let n = 0;
  for (const [, g] of by) n += g.pos.length / 3;
  return n;
}

const dir = process.argv[2] || 'world/models';
for (const make of [eglise, maisonA, maisonB]) {
  const m = make();
  const out = path.join(dir, m.name + '.glb');
  const n = write(out, m.name, m.b.by);
  console.log(`${out}  ${n} sommets — ${m.note}`);
}
