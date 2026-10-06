/* L'ANCRE EN .glb — props/ancre.glb, que le jeu lit à la place de l'ancre dessinée en
 * code (AnchorNode.cs), et que l'artiste remplace par la sienne.
 *
 *   node tools/anchor-glb.js            écrit props/ancre.glb
 *
 * L'ANCRE D'AMIRAUTÉ DE 1690, en fer forgé à jas de bois :
 *  - la VERGE, octogonale, plus forte au diamant qu'à la tête ;
 *  - les deux BRAS en croissant, partant du DIAMANT, finissant en BEC ;
 *  - les PATTES, des pelles plates sur la face intérieure des bras, larges EN TRAVERS
 *    du plan des bras : c'est elles qui mordent le fond ;
 *  - le JAS, une poutre de bois de la longueur de la verge, effilée vers ses bouts,
 *    cerclée de fer, posée en travers de la tête ET D'ÉQUERRE AVEC LES BRAS : couché
 *    sur le fond, il force l'ancre à basculer pour qu'une patte s'enfonce ;
 *  - l'ORGANEAU, l'anneau de la tête où l'on étalingue le câble.
 *
 * LA CONVENTION (celle que le jeu attend, et celle d'un modèle de remplacement) :
 * la VERGE fait 1 unité de long, le DIAMANT (la croisée des bras) à l'origine, la
 * verge vers +Y, les bras dans le plan X–Y, le jas le long de Z, l'organeau au sommet
 * (vers y = 1,05). Le jeu l'échelonne à la taille de l'ancre du navire (≈ 9 % de sa
 * longueur). Deux matières : « fer » et « bois ».
 */
const path = require('path');
const { writeGlb } = require('./glb-write');

const lin = hex => [hex >> 16 & 255, hex >> 8 & 255, hex & 255].map(c => { c /= 255; return c <= 0.04045 ? c / 12.92 : Math.pow((c + 0.055) / 1.055, 2.4); });
const MAT = {
  fer:  { name: 'fer', c: [...lin(0x2a2622), 1], r: 0.6, ds: true },
  bois: { name: 'bois', c: [...lin(0x5c4630), 1], r: 0.85, ds: true }
};

const add = (a, b) => [a[0] + b[0], a[1] + b[1], a[2] + b[2]];
const sub = (a, b) => [a[0] - b[0], a[1] - b[1], a[2] - b[2]];
const mul = (a, k) => [a[0] * k, a[1] * k, a[2] * k];
const cross = (a, b) => [a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0]];
const norm = a => { const L = Math.hypot(...a) || 1; return [a[0] / L, a[1] / L, a[2] / L]; };

const by = new Map();
const g = mat => { if (!by.has(mat)) by.set(mat, { material: MAT[mat], pos: [], nrm: [], uv: [], idx: [] }); return by.get(mat); };

/** Un tube lisse le long d'un chemin, rayon par point, fermé aux deux bouts. `ref` : un axe pour orienter la section. */
function tube(mat, pts, sides = 8, ref = [0, 0, 1]) {
  const p = g(mat), k0 = p.pos.length / 3;
  for (let i = 0; i < pts.length; i++) {
    const a = pts[Math.max(0, i - 1)].p, b = pts[Math.min(pts.length - 1, i + 1)].p;
    const t = norm(sub(b, a));
    const side = norm(cross(t, ref)), up = norm(cross(side, t));
    for (let j = 0; j <= sides; j++) {
      const ang = (j / sides + 0.5 / sides) * Math.PI * 2, c = Math.cos(ang), s = Math.sin(ang);
      const n = add(mul(side, c), mul(up, s));
      p.pos.push(...add(pts[i].p, mul(n, pts[i].r)));
      p.nrm.push(...n);
      p.uv.push(j / sides, i / (pts.length - 1));
    }
  }
  for (let i = 0; i + 1 < pts.length; i++)
    for (let j = 0; j < sides; j++) {
      const a = k0 + i * (sides + 1) + j, b = a + sides + 1;
      p.idx.push(a, b, a + 1, a + 1, b, b + 1);
    }
  // les deux bouts, fermés en éventail
  for (const [end, dirSign] of [[0, -1], [pts.length - 1, 1]]) {
    const a = pts[end].p, nb = pts[end + (dirSign < 0 ? 1 : -1)].p;
    const t = mul(norm(sub(a, nb)), 1);
    const c = p.pos.length / 3;
    p.pos.push(...add(a, mul(t, pts[end].r * 0.35))); p.nrm.push(...t); p.uv.push(0.5, end ? 1 : 0);
    for (let j = 0; j < sides; j++) {
      const r0 = k0 + end * (sides + 1) + j;
      if (dirSign > 0) p.idx.push(r0, c, r0 + 1); else p.idx.push(r0 + 1, c, r0);
    }
  }
}

/** Une boîte orientée : centre, trois demi-axes. Normales plates. */
function box(mat, c, ax, ay, az) {
  const p = g(mat);
  const faces = [[ax, ay, az], [mul(ax, -1), az, ay], [ay, az, ax], [mul(ay, -1), ax, az], [az, ax, ay], [mul(az, -1), ay, ax]];
  for (const [n, u, v] of faces) {
    const k = p.pos.length / 3, nn = norm(n);
    for (const [su, sv] of [[-1, -1], [1, -1], [1, 1], [-1, 1]]) {
      p.pos.push(...add(add(add(c, n), mul(u, su)), mul(v, sv)));
      p.nrm.push(...nn); p.uv.push((su + 1) / 2, (sv + 1) / 2);
    }
    // l'ordre des sommets donne une face tournée vers n
    const outward = cross(sub(mul(u, 2), [0, 0, 0]), mul(v, 2));
    if (outward[0] * nn[0] + outward[1] * nn[1] + outward[2] * nn[2] > 0) p.idx.push(k, k + 1, k + 2, k, k + 2, k + 3);
    else p.idx.push(k, k + 2, k + 1, k, k + 3, k + 2);
  }
}

/** Un anneau (tore) dans le plan défini par ses axes u et v. */
function ring(mat, c, u, v, R, r, seg = 20, sides = 8) {
  const pts = [];
  for (let i = 0; i <= seg; i++) {
    const a = i / seg * Math.PI * 2;
    pts.push({ p: add(c, add(mul(u, Math.cos(a) * R), mul(v, Math.sin(a) * R))), r });
  }
  tube(mat, pts, sides, cross(u, v));
}

// --- LA VERGE, du diamant à la tête, plus forte en bas ---
const shank = [];
for (let i = 0; i <= 10; i++) { const y = i / 10 * 0.97; shank.push({ p: [0, y, 0], r: 0.048 - 0.014 * i / 10 }); }
tube('fer', shank, 8, [0, 0, 1]);
// la tête carrée où le jas est pris, et l'œil de l'organeau
box('fer', [0, 0.9, 0], [0.04, 0, 0], [0, 0.07, 0], [0, 0, 0.04]);

// --- LES BRAS EN CROISSANT, du diamant au bec, dans le plan X–Y ---
const ARC_C = [0, 0.48, 0], ARC_R = 0.48;
for (const side of [1, -1]) {
  const arm = [];
  // de l'angle du bas (le diamant) à 40° sous l'horizontale
  for (let i = 0; i <= 12; i++) {
    const a = -Math.PI / 2 + side * (i / 12) * (Math.PI / 2 - 0.70);
    const p = add(ARC_C, [Math.cos(a) * ARC_R * (side > 0 ? 1 : 1), Math.sin(a) * ARC_R, 0]);
    arm.push({ p: [side > 0 ? Math.abs(p[0]) : -Math.abs(p[0]), p[1], 0], r: 0.046 - 0.026 * i / 12 });
  }
  tube('fer', arm, 8, [0, 0, 1]);
  // la patte : une pelle sur la face intérieure, près du bec, large en travers (Z)
  const k = 9, a0 = arm[k].p, a1 = arm[11].p;
  const t = norm(sub(a1, a0));
  const inward = norm(sub(ARC_C, add(a0, mul(sub(a1, a0), 0.5))));
  const mid = add(add(a0, mul(sub(a1, a0), 0.3)), mul(inward, 0.03));
  box('fer', mid, mul(t, 0.11), mul(inward, 0.012), [0, 0, 0.11]);
  // et son talon arrondi, vers la verge
  box('fer', add(mid, mul(t, -0.10)), mul(t, 0.03), mul(inward, 0.014), [0, 0, 0.07]);
}
// le diamant, renflé à la croisée
tube('fer', [{ p: [-0.07, 0.01, 0], r: 0.05 }, { p: [0, 0, 0], r: 0.062 }, { p: [0.07, 0.01, 0], r: 0.05 }], 8, [0, 0, 1]);

// --- LE JAS : en bois, le long de Z, d'équerre avec les bras, cerclé de fer ---
const stock = [];
for (let i = 0; i <= 8; i++) {
  const z = -0.5 + i / 8;
  stock.push({ p: [0, 0.86, z], r: 0.062 - 0.03 * Math.abs(z) / 0.5 });
}
tube('bois', stock, 6, [1, 0, 0]);
for (const z of [-0.36, -0.14, 0.14, 0.36]) {
  const r = 0.062 - 0.03 * Math.abs(z) / 0.5 + 0.006;
  ring('fer', [0, 0.86, z], [1, 0, 0], [0, 1, 0], r, 0.007, 14, 5);
}

// --- L'ORGANEAU, dans le plan des bras ---
ring('fer', [0, 1.06, 0], [1, 0, 0], [0, 1, 0], 0.12, 0.018, 24, 8);

const out = path.join(__dirname, '..', 'props', 'ancre.glb');
const n = writeGlb(out, 'ancre', [{ name: 'ancre', prims: [...by.values()] }], 'naval-sim anchor-glb');
console.log(`props/ancre.glb : ${n} sommets`);
