/* LA FLORE DE LA JAMAÏQUE EN 1690, en .glb — des modèles dessinés en code, comme les
 * habitants du fond (reef-glb.js), pour peupler Port-Royal et l'île avant que l'artiste
 * n'en fasse de plus beaux : remplacer un fichier de props/flore/ par le sien suffit,
 * le semis le reprend tel quel (world/*.json → semis ; sa taille y est ramenée).
 *
 *   node tools/flora-glb.js            écrit props/flore/*.glb
 *   node tools/flora-glb.js gaiac      n'écrit que celui-là
 *
 * CE QUI POUSSAIT LÀ EN 1690, et rien d'autre (réalisme demandé) :
 *  - raisinier   : Coccoloba uvifera, le raisinier bord de mer — buisson de grandes
 *                  feuilles rondes, le premier arbre au-dessus de la laisse ;
 *  - paletuvier  : Rhizophora mangle, le palétuvier rouge — la mangrove des eaux
 *                  calmes, campée sur ses racines-échasses arquées (l'intérieur des
 *                  Palisadoes, le fond de la rade de Kingston) ;
 *  - cierge      : Stenocereus, le cierge colonnaire des sables secs ;
 *  - raquette    : Opuntia, la raquette, en tiges de palettes plates ;
 *  - latanier    : Sabal / Thrinax, le palmier en éventail, dont on couvrait les cases ;
 *  - gaiac       : Guaiacum officinale, le gaïac, « lignum vitæ », petit arbre à cime
 *                  dense et sombre des bois secs du sud ;
 *  - fromager    : Ceiba pentandra, le fromager (le « cotton tree » des Anglais), géant
 *                  aux contreforts, couronne plate au-dessus de tout ;
 *  - buisson     : le fourré bas des côtes sèches (acacias, crotons), en touffes.
 * PAS de manguier (1782), d'arbre à pain (1793), d'akée (1778), ni de bois de campêche,
 * qu'on ne plante en Jamaïque qu'en 1715 — Port-Royal le revendait, il venait de la
 * baie de Campêche.
 *
 * Peu de faces : ces modèles se sèment par milliers (une foule, MultiMesh). Chacun a
 * sa hauteur « naturelle » en mètres ; le semis l'échelonne à sa plus grande mesure.
 */
const path = require('path');
const { writeGlb } = require('./glb-write');

const lin = hex => [hex >> 16 & 255, hex >> 8 & 255, hex & 255].map(c => { c /= 255; return c <= 0.04045 ? c / 12.92 : Math.pow((c + 0.055) / 1.055, 2.4); });
const M = (name, hex, r = 0.85, ds = false) => ({ name, c: [...lin(hex), 1], r, ds });
const MAT = {
  ecorce:      M('ecorce', 0x6a5644, 0.95),
  ecorceGrise: M('ecorce_grise', 0x8f8a80, 0.95),
  ecorceRouge: M('ecorce_rouge', 0x5a3c2c, 0.95),
  raisinier:   M('feuilles_raisinier', 0x5f8a3e),
  paletuvier:  M('feuilles_paletuvier', 0x34572c),
  cactus:      M('cactus', 0x5d7a46, 0.7),
  raquette:    M('raquette', 0x6d8a4a, 0.7),
  palme:       M('palme_latanier', 0x6a8a48, 0.8, true),
  gaiac:       M('feuilles_gaiac', 0x2f4a2a),
  fromager:    M('feuilles_fromager', 0x57783c),
  buisson:     M('feuilles_buisson', 0x6a7440)
};

let seed = 11;
const R = () => { seed ^= seed << 13; seed ^= seed >>> 17; seed ^= seed << 5; return ((seed >>> 0) % 1e6) / 1e6; };
const add = (a, b) => [a[0] + b[0], a[1] + b[1], a[2] + b[2]];
const sub = (a, b) => [a[0] - b[0], a[1] - b[1], a[2] - b[2]];
const mul = (a, k) => [a[0] * k, a[1] * k, a[2] * k];
const cross = (a, b) => [a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0]];
const norm = a => { const L = Math.hypot(...a) || 1; return [a[0] / L, a[1] / L, a[2] / L]; };

function model() {
  const by = new Map();
  const g = mat => { if (!by.has(mat)) by.set(mat, { material: MAT[mat], pos: [], nrm: [], uv: [], idx: [] }); return by.get(mat); };
  return { g, prims: () => [...by.values()] };
}

/** Un tube lisse le long d'un chemin, peu de côtés. */
function tube(m, mat, pts, sides = 5) {
  const p = m.g(mat), k0 = p.pos.length / 3;
  for (let i = 0; i < pts.length; i++) {
    const a = pts[Math.max(0, i - 1)].p, b = pts[Math.min(pts.length - 1, i + 1)].p;
    const t = norm(sub(b, a));
    const side = Math.abs(t[1]) > 0.97 ? [1, 0, 0] : norm(cross(t, [0, 1, 0]));
    const up = norm(cross(side, t));
    for (let j = 0; j <= sides; j++) {
      const ang = j / sides * Math.PI * 2, c = Math.cos(ang), s = Math.sin(ang);
      const n = add(mul(side, c), mul(up, s));
      p.pos.push(...add(pts[i].p, mul(n, pts[i].r)));
      p.nrm.push(...n); p.uv.push(j / sides, i / (pts.length - 1));
    }
  }
  for (let i = 0; i + 1 < pts.length; i++)
    for (let j = 0; j < sides; j++) {
      const a = k0 + i * (sides + 1) + j, b = a + sides + 1;
      p.idx.push(a, b, a + 1, a + 1, b, b + 1);
    }
}

/** Une masse de feuillage : un ellipsoïde bosselé, peu de faces, normales lisses (ce qui rend un feuillage, pas une boule). */
function blob(m, mat, c, rx, ry, rz, bump = 0.22, rings = 5, segs = 7) {
  const p = m.g(mat), k0 = p.pos.length / 3;
  const jit = [];
  for (let i = 0; i <= rings; i++) for (let j = 0; j <= segs; j++) jit.push(1 + bump * (R() - 0.5) * 2);
  for (let i = 0; i <= rings; i++) {
    const th = i / rings * Math.PI;
    for (let j = 0; j <= segs; j++) {
      const ph = j / segs * Math.PI * 2;
      // la couture fermée : le dernier méridien reprend le bossage du premier
      const jj = j === segs ? 0 : j;
      const k = (i === 0 || i === rings) ? 1 : jit[i * (segs + 1) + jj];
      const n = [Math.sin(th) * Math.cos(ph), Math.cos(th), Math.sin(th) * Math.sin(ph)];
      p.pos.push(c[0] + n[0] * rx * k, c[1] + n[1] * ry * k, c[2] + n[2] * rz * k);
      // un feuillage est plus clair dessus : la normale penche vers le haut
      p.nrm.push(...norm([n[0], n[1] + 0.35, n[2]]));
      p.uv.push(j / segs, i / rings);
    }
  }
  for (let i = 0; i < rings; i++)
    for (let j = 0; j < segs; j++) {
      const a = k0 + i * (segs + 1) + j, b = a + segs + 1;
      p.idx.push(a, a + 1, b, a + 1, b + 1, b);
    }
}

/** Une palette plate (raquette, feuille ronde) : un disque ovale épaissi, dans le plan (u, v). */
function pad(m, mat, c, u, v, ru, rv, th, segs = 9) {
  const p = m.g(mat), k0 = p.pos.length / 3;
  const n = norm(cross(u, v));
  for (const side of [1, -1]) {
    const kc = p.pos.length / 3;
    p.pos.push(...add(c, mul(n, th * side))); p.nrm.push(...mul(n, side)); p.uv.push(0.5, 0.5);
    for (let j = 0; j <= segs; j++) {
      const a = j / segs * Math.PI * 2;
      const q = add(add(c, mul(u, Math.cos(a) * ru)), mul(v, Math.sin(a) * rv));
      p.pos.push(...add(q, mul(n, th * 0.3 * side)));
      p.nrm.push(...norm(add(mul(n, side), mul(add(mul(u, Math.cos(a)), mul(v, Math.sin(a))), 0.6))));
      p.uv.push(0.5 + 0.5 * Math.cos(a), 0.5 + 0.5 * Math.sin(a));
    }
    for (let j = 0; j < segs; j++)
      if (side > 0) p.idx.push(kc, kc + 1 + j, kc + 2 + j); else p.idx.push(kc, kc + 2 + j, kc + 1 + j);
  }
  void k0;
}

// --- les espèces ---

function raisinier() {
  const m = model();
  // trois ou quatre tiges tordues, basses, qui s'écartent
  for (let s = 0; s < 4; s++) {
    const a = s / 4 * Math.PI * 2 + R() * 0.8, lean = 0.5 + R() * 0.5;
    const tip = [Math.cos(a) * lean, 1.3 + R() * 0.6, Math.sin(a) * lean];
    tube(m, 'ecorceGrise', [{ p: [0, 0, 0], r: 0.09 }, { p: mul(tip, 0.5), r: 0.06 }, { p: tip, r: 0.035 }], 5);
    blob(m, 'raisinier', add(tip, [0, 0.35, 0]), 0.75 + R() * 0.3, 0.5, 0.75 + R() * 0.3, 0.3);
  }
  blob(m, 'raisinier', [0, 1.6, 0], 1.0, 0.65, 1.0, 0.28);
  return m;
}

function paletuvier() {
  const m = model();
  const H = 4.5;
  tube(m, 'ecorceRouge', [{ p: [0, 1.0, 0], r: 0.12 }, { p: [0.1, 2.4, 0], r: 0.1 }, { p: [0, 3.2, 0.1], r: 0.07 }], 5);
  // les racines-échasses : de la tige à un mètre du sol, en arc jusqu'à la vase
  for (let k = 0; k < 9; k++) {
    const a = k / 9 * Math.PI * 2 + R() * 0.3, far = 1.1 + R() * 0.6;
    const pts = [];
    for (let i = 0; i <= 5; i++) {
      const u = i / 5;
      const h = 1.0 + 0.5 * Math.sin(u * Math.PI * 0.85) - u * 1.05;
      pts.push({ p: [Math.cos(a) * far * u, Math.max(-0.1, h), Math.sin(a) * far * u], r: 0.04 - 0.015 * u });
    }
    tube(m, 'ecorceRouge', pts, 4);
  }
  // la couronne sombre, en masses serrées
  for (let k = 0; k < 6; k++) {
    const a = k / 6 * Math.PI * 2 + R();
    blob(m, 'paletuvier', [Math.cos(a) * 0.9, H - 0.6 + R() * 0.5, Math.sin(a) * 0.9], 1.0, 0.6, 1.0, 0.25);
  }
  blob(m, 'paletuvier', [0, H - 0.2, 0], 1.2, 0.7, 1.2, 0.25);
  return m;
}

function cierge() {
  const m = model();
  const col = (base, h, r) => {
    const pts = [];
    for (let i = 0; i <= 6; i++) pts.push({ p: [base[0], base[1] + h * i / 6, base[2]], r: r * (i === 6 ? 0.6 : 1) });
    tube(m, 'cactus', pts, 8);
  };
  col([0, 0, 0], 4.5, 0.16);
  // des bras qui partent de côté puis se redressent
  for (let k = 0; k < 3; k++) {
    const a = k / 3 * Math.PI * 2 + R(), y = 0.8 + R() * 1.2, out = 0.45;
    const base = [Math.cos(a) * out, y + 0.25, Math.sin(a) * out];
    tube(m, 'cactus', [{ p: [0, y, 0], r: 0.13 }, { p: [Math.cos(a) * out * 0.8, y + 0.05, Math.sin(a) * out * 0.8], r: 0.13 }, { p: base, r: 0.13 }], 7);
    col(base, 1.8 + R() * 1.4, 0.13);
  }
  return m;
}

function raquette() {
  const m = model();
  // des palettes ovales, chacune posée sur le bord de la précédente
  const grow = (c, dir, depth) => {
    if (depth > 3) return;
    const u = norm(dir), v = norm(cross(u, [R() - 0.5, 0, R() - 0.5]));
    const ru = 0.22, rv = 0.15;
    const center = add(c, mul(u, ru));
    pad(m, 'raquette', center, u, v, ru, rv, 0.03);
    const n = depth < 2 ? 2 : 1;
    for (let k = 0; k < n; k++) {
      const nd = norm(add(u, [R() - 0.5, 0.4, R() - 0.5]));
      grow(add(center, mul(u, ru * 0.85)), nd, depth + 1);
    }
  };
  for (let k = 0; k < 3; k++) grow([0, 0, 0], [R() - 0.5, 1, R() - 0.5], 0);
  return m;
}

function latanier() {
  const m = model();
  const H = 7;
  const lean = [0.25, 0, 0.1];
  const trunk = [];
  for (let i = 0; i <= 6; i++) trunk.push({ p: [lean[0] * (i / 6) ** 2 * H / 7, H * i / 6, lean[2] * (i / 6) ** 2 * H / 7], r: 0.16 - 0.04 * i / 6 });
  tube(m, 'ecorceGrise', trunk, 6);
  const top = trunk[6].p;
  // les éventails : un pétiole, puis une palme ronde plissée, rayonnant de la tête
  for (let k = 0; k < 13; k++) {
    const a = k / 13 * Math.PI * 2 + R() * 0.3, tilt = -0.2 + R() * 0.9;
    const dir = norm([Math.cos(a), tilt, Math.sin(a)]);
    const stem = add(top, mul(dir, 1.1));
    tube(m, 'ecorceGrise', [{ p: top, r: 0.03 }, { p: stem, r: 0.02 }], 3);
    const u = dir, side = norm(cross(dir, [0, 1, 0]));
    const v = norm(cross(side, u));
    // l'éventail est presque à plat dans le plan (côté, avant), retombant
    pad(m, 'palme', add(stem, mul(u, 0.55)), u, side, 0.65, 0.75, 0.01, 11);
    void v;
  }
  return m;
}

function gaiac() {
  const m = model();
  const H = 5;
  tube(m, 'ecorceGrise', [{ p: [0, 0, 0], r: 0.18 }, { p: [0.15, 1.6, 0], r: 0.14 }, { p: [0, 2.6, 0.1], r: 0.1 }], 6);
  for (let k = 0; k < 4; k++) {
    const a = k / 4 * Math.PI * 2 + R();
    const tip = [Math.cos(a) * 1.0, 3.0 + R() * 0.6, Math.sin(a) * 1.0];
    tube(m, 'ecorceGrise', [{ p: [0, 2.4, 0], r: 0.08 }, { p: tip, r: 0.04 }], 4);
    blob(m, 'gaiac', add(tip, [0, 0.5, 0]), 1.05, 0.8, 1.05, 0.25);
  }
  blob(m, 'gaiac', [0, H - 0.7, 0], 1.4, 0.9, 1.4, 0.22);
  return m;
}

function fromager() {
  const m = model();
  const H = 22;
  // le fût, droit et lisse, gris, sur ses contreforts
  tube(m, 'ecorceGrise', [{ p: [0, 0, 0], r: 1.1 }, { p: [0, 3, 0], r: 0.75 }, { p: [0, 12, 0], r: 0.6 }, { p: [0, 15, 0], r: 0.5 }], 8);
  for (let k = 0; k < 6; k++) {
    const a = k / 6 * Math.PI * 2 + R() * 0.3;
    const d = [Math.cos(a), 0, Math.sin(a)];
    const p = m.g('ecorceGrise'), k0 = p.pos.length / 3;
    // une lame triangulaire, du fût au sol
    const A = add(mul(d, 0.5), [0, 3.2, 0]), B = add(mul(d, 2.6), [0, 0, 0]), C = add(mul(d, 0.5), [0, -0.1, 0]);
    const side = norm(cross(d, [0, 1, 0]));
    for (const s of [1, -1]) {
      const o = mul(side, 0.12 * s);
      p.pos.push(...add(A, o), ...add(B, o), ...add(C, o));
      p.nrm.push(...mul(side, s), ...mul(side, s), ...mul(side, s));
      p.uv.push(0, 1, 1, 0, 0, 0);
    }
    p.idx.push(k0, k0 + 2, k0 + 1, k0 + 3, k0 + 4, k0 + 5);
  }
  // les maîtresses branches, presque horizontales, en étages
  for (let k = 0; k < 7; k++) {
    const a = k / 7 * Math.PI * 2 + R() * 0.4, y = 13 + R() * 3, L = 6 + R() * 3;
    const tip = [Math.cos(a) * L, y + 2.5 + R() * 1.5, Math.sin(a) * L];
    tube(m, 'ecorceGrise', [{ p: [0, y - 1, 0], r: 0.35 }, { p: [Math.cos(a) * L * 0.5, y + 1.0, Math.sin(a) * L * 0.5], r: 0.22 }, { p: tip, r: 0.1 }], 5);
    blob(m, 'fromager', add(tip, [0, 0.8, 0]), 3.4, 1.4, 3.4, 0.3);
  }
  blob(m, 'fromager', [0, H - 2.5, 0], 4.5, 1.8, 4.5, 0.25);
  return m;
}

function buisson() {
  const m = model();
  for (let k = 0; k < 4; k++) {
    const a = k / 4 * Math.PI * 2 + R(), d = 0.35 + R() * 0.3;
    blob(m, 'buisson', [Math.cos(a) * d, 0.45 + R() * 0.25, Math.sin(a) * d], 0.55, 0.45, 0.55, 0.35, 4, 6);
  }
  blob(m, 'buisson', [0, 0.7, 0], 0.6, 0.5, 0.6, 0.3, 4, 6);
  return m;
}

const out = path.join(__dirname, '..', 'props', 'flore');
const only = process.argv.slice(2);
for (const [name, make] of Object.entries({ raisinier, paletuvier, cierge, raquette, latanier, gaiac, fromager, buisson })) {
  if (only.length && !only.includes(name)) continue;
  seed = 11 + name.length * 97;
  const m = make();
  let tris = 0; for (const p of m.prims()) tris += p.idx.length / 3;
  const n = writeGlb(path.join(out, name + '.glb'), name, [{ name, prims: m.prims() }], 'naval-sim flora-glb');
  console.log(`props/flore/${name}.glb : ${n} sommets, ${tris} triangles`);
}
