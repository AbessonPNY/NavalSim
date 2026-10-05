/* LES HABITANTS DU FOND, en .glb — des modèles dessinés en code, comme les pièces
 * d'un ponton (jetty-glb.js), pour que la mer des Caraïbes ait de quoi se peupler
 * avant que l'artiste n'en fasse de plus beaux : remplacer un fichier de
 * props/fond/ par le sien suffit, le semis le reprend tel quel (world/*.json →
 * semis ; sa taille est ramenée à celle que le semis demande).
 *
 *   node tools/reef-glb.js        écrit props/fond/*.glb
 *
 * CE QUI VIT DANS UNE RADE DE LA JAMAÏQUE, et rien d'autre (réalisme demandé) :
 *  - corail-cerveau : Diploria, un dôme creusé de méandres, ocre ;
 *  - corail-corne   : Acropora palmata, la corne d'élan — des branches PLATES qui
 *                     s'ouvrent vers la lumière, orangé, dans les hauts-fonds battus ;
 *  - corail-cerf    : Acropora cervicornis, la corne de cerf — des rameaux ronds et
 *                     fins, pâles au bout, un peu plus profond ;
 *  - gorgone        : l'éventail de mer, violet, à plat face au courant ;
 *  - eponge-tube    : des tubes ouverts, violets, en bouquet ;
 *  - herbier        : une touffe de Thalassia, l'herbe à tortues ;
 *  - oursin         : Diadema, noir, aux longs piquants.
 * Pas de laminaires : ce sont des algues d'eau froide.
 * Et ce qu'un nageur ramasse (Finds.cs), à sa taille vraie, en mètres :
 *  - lambi          : Aliger gigas, le « queen conch » — une spire à pointes, la
 *                     lèvre évasée rose, 24 cm ;
 *  - huitre         : Pinctada imbricata, l'huître perlière des Caraïbes — plate,
 *                     brune, son aile droite à la charnière, 7 cm.
 *
 *   node tools/reef-glb.js lambi huitre    n'écrit que ceux-là (un fichier remplacé
 *                                          par l'artiste n'est pas écrasé)
 */
const path = require('path');
const { writeGlb } = require('./glb-write');

// sRGB → linéaire : glTF lit des couleurs linéaires
const lin = hex => [hex >> 16 & 255, hex >> 8 & 255, hex & 255].map(c => { c /= 255; return c <= 0.04045 ? c / 12.92 : Math.pow((c + 0.055) / 1.055, 2.4); });
const MAT = {
  cerveau: { name: 'corail_cerveau', c: [...lin(0xb59a5c), 1], r: 0.9 },
  corne:   { name: 'corail_corne', c: [...lin(0xb0763a), 1], r: 0.85 },
  cerf:    { name: 'corail_cerf', c: [...lin(0xc8a46a), 1], r: 0.85 },
  cerfBout:{ name: 'corail_cerf_bout', c: [...lin(0xe8dcb8), 1], r: 0.85 },
  gorgone: { name: 'gorgone', c: [...lin(0x7a3a7e), 1], r: 0.9, ds: true },
  eponge:  { name: 'eponge', c: [...lin(0x8e4a9c), 1], r: 0.95 },
  epongeIn:{ name: 'eponge_dedans', c: [...lin(0x5a2a66), 1], r: 0.95 },
  herbe:   { name: 'herbier', c: [...lin(0x4f6b2a), 1], r: 0.8, ds: true },
  oursin:  { name: 'oursin', c: [...lin(0x16141a), 1], r: 0.6 },
  lambi:   { name: 'lambi', c: [...lin(0xc4a47c), 1], r: 0.85 },
  levre:   { name: 'lambi_levre', c: [...lin(0xe8a29a), 1], r: 0.35, ds: true },
  huitre:  { name: 'huitre', c: [...lin(0x6e5a48), 1], r: 0.8 },
  nacre:   { name: 'huitre_nacre', c: [...lin(0xb9b0a0), 1], r: 0.3 }
};

// un hasard tenu : le même récif à chaque écriture
let seed = 7;
const R = () => { seed ^= seed << 13; seed ^= seed >>> 17; seed ^= seed << 5; return ((seed >>> 0) % 1e6) / 1e6; };

const add = (a, b) => [a[0] + b[0], a[1] + b[1], a[2] + b[2]];
const sub = (a, b) => [a[0] - b[0], a[1] - b[1], a[2] - b[2]];
const mul = (a, k) => [a[0] * k, a[1] * k, a[2] * k];
const dot = (a, b) => a[0] * b[0] + a[1] * b[1] + a[2] * b[2];
const cross = (a, b) => [a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0]];
const norm = a => { const L = Math.hypot(...a) || 1; return [a[0] / L, a[1] / L, a[2] / L]; };
const rot = (v, axis, ang) => {          // Rodrigues
  const k = norm(axis), c = Math.cos(ang), s = Math.sin(ang);
  return add(add(mul(v, c), mul(cross(k, v), s)), mul(k, dot(k, v) * (1 - c)));
};

/** Un modèle : ses primitives par matière. */
function model() {
  const by = new Map();
  const g = mat => { if (!by.has(mat)) by.set(mat, { material: MAT[mat], pos: [], nrm: [], uv: [], idx: [] }); return by.get(mat); };
  return { g, prims: () => [...by.values()] };
}

/**
 * UN TUBE le long d'un chemin, rond ou aplati : `flat` élargit la section à
 * l'horizontale (la corne d'élan est une lame, pas un bâton). Le bout se ferme en
 * pointe mousse.
 */
function tube(m, mat, pts, sides = 7, flat = 1) {
  const p = m.g(mat);
  const k0 = p.pos.length / 3;
  for (let i = 0; i < pts.length; i++) {
    const a = pts[Math.max(0, i - 1)].p, b = pts[Math.min(pts.length - 1, i + 1)].p;
    const t = norm(sub(b, a));
    const side = Math.abs(t[1]) > 0.97 ? [1, 0, 0] : norm(cross(t, [0, 1, 0]));   // horizontale
    const up = norm(cross(side, t));
    const r = pts[i].r;
    for (let j = 0; j <= sides; j++) {
      const ang = j / sides * Math.PI * 2, c = Math.cos(ang), s = Math.sin(ang);
      const off = add(mul(side, c * r * flat), mul(up, s * r));
      p.pos.push(...add(pts[i].p, off));
      p.nrm.push(...norm(add(mul(side, c / flat), mul(up, s))));
      p.uv.push(j / sides, i / (pts.length - 1));
    }
  }
  for (let i = 0; i + 1 < pts.length; i++)
    for (let j = 0; j < sides; j++) {
      const a = k0 + i * (sides + 1) + j, b = a + sides + 1;
      p.idx.push(a, b, a + 1, a + 1, b, b + 1);
    }
  // la pointe
  const n = pts.length, last = pts[n - 1], prev = pts[n - 2];
  const t = norm(sub(last.p, prev.p));
  const tip = p.pos.length / 3;
  p.pos.push(...add(last.p, mul(t, last.r * 0.7))); p.nrm.push(...t); p.uv.push(0.5, 1);
  for (let j = 0; j < sides; j++) {
    const a = k0 + (n - 1) * (sides + 1) + j;
    p.idx.push(a, tip, a + 1);
  }
}

/** Une lame à deux faces (matière double face) le long d'un chemin, de largeur w. */
function ribbon(m, mat, pts, w, faceAxis) {
  const p = m.g(mat);
  const k0 = p.pos.length / 3;
  for (let i = 0; i < pts.length; i++) {
    const a = pts[Math.max(0, i - 1)], b = pts[Math.min(pts.length - 1, i + 1)];
    const t = norm(sub(b, a));
    const across = norm(cross(t, faceAxis));
    const n = norm(cross(across, t));
    const ww = w * (1 - 0.7 * i / (pts.length - 1));
    p.pos.push(...add(pts[i], mul(across, -ww / 2)), ...add(pts[i], mul(across, ww / 2)));
    p.nrm.push(...n, ...n);
    p.uv.push(0, i / (pts.length - 1), 1, i / (pts.length - 1));
  }
  for (let i = 0; i + 1 < pts.length; i++) {
    const a = k0 + i * 2;
    p.idx.push(a, a + 2, a + 1, a + 1, a + 2, a + 3);
  }
}

/** Les normales d'un maillage lisse, moyennées sur ses faces. */
function smoothNormals(p) {
  const n = new Array(p.pos.length).fill(0);
  for (let i = 0; i < p.idx.length; i += 3) {
    const [a, b, c] = [p.idx[i], p.idx[i + 1], p.idx[i + 2]];
    const A = p.pos.slice(a * 3, a * 3 + 3), B = p.pos.slice(b * 3, b * 3 + 3), C = p.pos.slice(c * 3, c * 3 + 3);
    const f = cross(sub(B, A), sub(C, A));
    for (const v of [a, b, c]) for (let k = 0; k < 3; k++) n[v * 3 + k] += f[k];
  }
  p.nrm = [];
  for (let i = 0; i < n.length; i += 3) p.nrm.push(...norm([n[i], n[i + 1], n[i + 2]]));
}

// ---------------------------------------------------------------------------

/** LE CERVEAU : un dôme écrasé, creusé de méandres qui serpentent. */
function cerveau() {
  const m = model(), p = m.g('cerveau');
  const NT = 28, NP = 56;
  for (let i = 0; i <= NT; i++) {
    const th = i / NT * Math.PI * 0.55;              // un peu plus qu'une demi-sphère
    for (let j = 0; j <= NP; j++) {
      const ph = j / NP * Math.PI * 2;
      const groove = Math.sin(14 * ph + 5 * Math.sin(4 * th + 2 * Math.sin(3 * ph))) * Math.sin(12 * th + 3 * Math.sin(5 * ph));
      const r = 1 - 0.045 * Math.abs(groove) + 0.06 * Math.sin(3 * ph + 1.3) * Math.sin(2 * th);
      const y = Math.cos(th) * r * 0.68 - 0.08;
      p.pos.push(Math.sin(th) * Math.cos(ph) * r, Math.max(y, -0.12), Math.sin(th) * Math.sin(ph) * r);
      p.uv.push(j / NP, i / NT);
    }
  }
  for (let i = 0; i < NT; i++)
    for (let j = 0; j < NP; j++) {
      const a = i * (NP + 1) + j, b = a + NP + 1;
      p.idx.push(a, a + 1, b, a + 1, b + 1, b);
    }
  smoothNormals(p);
  return m;
}

/** LA CORNE D'ÉLAN : un tronc court, puis des lames qui s'ouvrent et fourchent. */
function corne() {
  const m = model();
  tube(m, 'corne', [{ p: [0, -0.1, 0], r: 0.16 }, { p: [0, 0.25, 0], r: 0.13 }, { p: [0, 0.45, 0], r: 0.12 }], 8, 1.2);
  function branch(from, dir, len, r, depth) {
    const pts = [{ p: from, r }];
    let at = from, d = dir;
    for (let s = 1; s <= 4; s++) {
      d = norm(add(d, [0, 0.05, 0]));                 // un peu vers la lumière : elle s'étale plus qu'elle ne monte
      at = add(at, mul(d, len / 4));
      pts.push({ p: at, r: r * (1 + 0.25 * s / 4) });  // la lame s'élargit vers le bout
    }
    tube(m, 'corne', pts, 7, 3.4);
    if (depth > 0) {
      const n = 2;
      for (let k = 0; k < n; k++) {
        const nd = rot(d, [0, 1, 0], (k - 0.5) * (0.6 + 0.3 * R()));
        branch(at, norm(add(nd, [0, 0.02, 0])), len * (0.6 + 0.2 * R()), r * 0.95, depth - 1);
      }
    }
  }
  const N = 5;
  for (let k = 0; k < N; k++) {
    const a = k / N * Math.PI * 2 + R() * 0.5;
    branch([0, 0.42, 0], norm([Math.cos(a), 0.15 + 0.2 * R(), Math.sin(a)]), 0.6 + 0.25 * R(), 0.07, 1 + (R() < 0.6 ? 1 : 0));
  }
  return m;
}

/** LA CORNE DE CERF : des rameaux ronds et fins, fourchus, pâles au bout. */
function cerf() {
  const m = model();
  function twig(from, dir, len, r, depth) {
    const mid = add(from, mul(norm(add(dir, [(R() - 0.5) * 0.3, 0.1, (R() - 0.5) * 0.3])), len * 0.5));
    const to = add(mid, mul(dir, len * 0.5));
    const end = depth === 0;
    tube(m, end ? 'cerfBout' : 'cerf', [{ p: from, r }, { p: mid, r: r * 0.92 }, { p: to, r: r * 0.85 }], 6);
    if (end) return;
    const n = 2 + (R() < 0.35 ? 1 : 0);
    for (let k = 0; k < n; k++) {
      let nd = rot(dir, [0, 1, 0], R() * Math.PI * 2);
      nd = norm(add(mul(dir, 1.4), mul(nd, 0.6 + 0.3 * R())));
      nd = norm(add(nd, [0, 0.15, 0]));
      twig(to, nd, len * (0.75 + 0.2 * R()), r * 0.85, depth - 1);
    }
  }
  for (let k = 0; k < 4; k++) {
    const a = k / 4 * Math.PI * 2 + R();
    twig([0, -0.05, 0], norm([Math.cos(a) * 0.6, 1, Math.sin(a) * 0.6]), 0.32, 0.035, 3);
  }
  return m;
}

/** LA GORGONE : un éventail plat, des rameaux qui se divisent dans son plan. */
function gorgone() {
  const m = model();
  const face = [0, 0, 1];
  function stem(from, ang, len, w, depth) {
    const pts = [from];
    let at = from;
    for (let s = 1; s <= 4; s++) {
      const a = ang + (R() - 0.5) * 0.15;
      at = add(at, [Math.sin(a) * len / 4, Math.cos(a) * len / 4, (R() - 0.5) * 0.01]);
      pts.push(at);
    }
    ribbon(m, 'gorgone', pts, w, face);
    if (depth > 0)
      for (const s of [-1, 1]) stem(at, ang + s * (0.25 + 0.2 * R()), len * 0.8, w * 0.8, depth - 1);
  }
  ribbon(m, 'gorgone', [[0, -0.05, 0], [0, 0.08, 0], [0, 0.18, 0]], 0.045, face);
  for (let k = -2; k <= 2; k++) stem([0, 0.16, 0], k * 0.32, 0.24, 0.03, 3);
  return m;
}

/** L'ÉPONGE EN TUBES : des fûts ouverts, en bouquet. */
function eponge() {
  const m = model();
  const N = 4;
  for (let k = 0; k < N; k++) {
    const a = k / N * Math.PI * 2 + R();
    const base = [Math.cos(a) * 0.1, -0.05, Math.sin(a) * 0.1];
    const h = 0.45 + 0.5 * R(), r = 0.07 + 0.04 * R();
    const lean = norm([Math.cos(a) * 0.25, 1, Math.sin(a) * 0.25]);
    const sides = 10;
    const out = m.g('eponge'), inn = m.g('epongeIn');
    for (const [p, rr, sign] of [[out, r, 1], [inn, r * 0.78, -1]]) {
      const k0 = p.pos.length / 3;
      const rings = 6;
      for (let i = 0; i <= rings; i++) {
        const c = add(base, mul(lean, h * i / rings));
        const ri = rr * (1 + 0.15 * Math.sin(i * 1.7 + k));
        for (let j = 0; j <= sides; j++) {
          const t = j / sides * Math.PI * 2;
          p.pos.push(c[0] + Math.cos(t) * ri, c[1], c[2] + Math.sin(t) * ri);
          p.nrm.push(Math.cos(t) * sign, 0, Math.sin(t) * sign);
          p.uv.push(j / sides, i / rings);
        }
      }
      for (let i = 0; i < rings; i++)
        for (let j = 0; j < sides; j++) {
          const a0 = k0 + i * (sides + 1) + j, b0 = a0 + sides + 1;
          if (sign > 0) p.idx.push(a0, b0, a0 + 1, a0 + 1, b0, b0 + 1);
          else p.idx.push(a0, a0 + 1, b0, a0 + 1, b0 + 1, b0);
        }
    }
    // la lèvre, de l'extérieur à l'intérieur
    const top = add(base, mul(lean, h)), p = m.g('eponge'), k0 = p.pos.length / 3;
    for (let j = 0; j <= sides; j++) {
      const t = j / sides * Math.PI * 2, ro = r * (1 + 0.15 * Math.sin(6 * 1.7 + k));
      p.pos.push(top[0] + Math.cos(t) * ro, top[1], top[2] + Math.sin(t) * ro, top[0] + Math.cos(t) * ro * 0.78, top[1], top[2] + Math.sin(t) * ro * 0.78);
      p.nrm.push(0, 1, 0, 0, 1, 0);
      p.uv.push(j / sides, 0, j / sides, 1);
    }
    for (let j = 0; j < sides; j++) { const a0 = k0 + j * 2; p.idx.push(a0, a0 + 1, a0 + 2, a0 + 1, a0 + 3, a0 + 2); }
  }
  return m;
}

/** UNE TOUFFE D'HERBIER : des lames étroites qui se couchent un peu. */
function herbier() {
  const m = model();
  const N = 18;
  for (let k = 0; k < N; k++) {
    const a = R() * Math.PI * 2, d = 0.07 * R();
    const base = [Math.cos(a) * d, -0.02, Math.sin(a) * d];
    const len = 0.3 + 0.3 * R(), bend = 0.1 + 0.3 * R(), dirA = R() * Math.PI * 2;
    const pts = [];
    for (let s = 0; s <= 5; s++) {
      const u = s / 5;
      pts.push(add(base, [Math.cos(dirA) * bend * len * u * u, len * u, Math.sin(dirA) * bend * len * u * u]));
    }
    ribbon(m, 'herbe', pts, 0.014, [Math.cos(dirA + Math.PI / 2), 0, Math.sin(dirA + Math.PI / 2)]);
  }
  return m;
}

/** L'OURSIN DIADÈME : une boule noire hérissée de longs piquants. */
function oursin() {
  const m = model();
  const p = m.g('oursin');
  // le test : une petite sphère
  const NT = 8, NP = 12, k0 = 0;
  for (let i = 0; i <= NT; i++)
    for (let j = 0; j <= NP; j++) {
      const th = i / NT * Math.PI, ph = j / NP * Math.PI * 2;
      const n = [Math.sin(th) * Math.cos(ph), Math.cos(th), Math.sin(th) * Math.sin(ph)];
      p.pos.push(n[0] * 0.05, n[1] * 0.04 + 0.03, n[2] * 0.05); p.nrm.push(...n); p.uv.push(j / NP, i / NT);
    }
  for (let i = 0; i < NT; i++)
    for (let j = 0; j < NP; j++) { const a = k0 + i * (NP + 1) + j, b = a + NP + 1; p.idx.push(a, a + 1, b, a + 1, b + 1, b); }
  // les piquants, vers le haut et les côtés
  for (let k = 0; k < 40; k++) {
    const ph = R() * Math.PI * 2, th = Math.acos(1 - 1.3 * R());
    const d = [Math.sin(th) * Math.cos(ph), Math.cos(th), Math.sin(th) * Math.sin(ph)];
    const from = [d[0] * 0.04, 0.03 + d[1] * 0.03, d[2] * 0.04];
    tube(m, 'oursin', [{ p: from, r: 0.004 }, { p: add(from, mul(d, 0.12 + 0.08 * R())), r: 0.0015 }], 3);
  }
  return m;
}

/**
 * LE LAMBI, couché sur le sable comme il vit : la spire vers l'arrière (+x), le
 * canal vers l'avant (−x), ouverture dessous ; une couronne de pointes sur
 * l'épaule, et la grande lèvre évasée, rose, qui le cale sur le côté.
 */
function lambi() {
  const m = model();
  // le corps : une section qui s'enfle à l'épaule, puis s'effile vers le canal
  const prof = x => {                    // x de +0,12 (apex) à −0,12 (canal)
    const u = (0.12 - x) / 0.24;         // 0 à l'apex, 1 au canal
    return u < 0.3 ? 0.006 + 0.074 * Math.pow(u / 0.3, 0.8) : 0.08 * (1 - 0.72 * Math.pow((u - 0.3) / 0.7, 1.3));
  };
  const pts = [];
  for (let i = 0; i <= 18; i++) { const x = 0.12 - i / 18 * 0.24; pts.push({ p: [x, prof(x) * 0.85, 0], r: prof(x) }); }
  tube(m, 'lambi', pts, 14, 1.05);
  smoothNormals(m.g('lambi'));
  // les pointes de l'épaule, du dessus vers le côté de la spire
  for (let k = 0; k < 9; k++) {
    const x = 0.075 - k * 0.006, ang = -0.4 + k * 0.42, r0 = prof(x);
    const d = [0, Math.cos(ang), Math.sin(ang)];
    const from = add([x, r0 * 0.85, 0], mul(d, r0 * 0.9));
    tube(m, 'lambi', [{ p: from, r: 0.008 }, { p: add(from, mul(add(d, [0.25, 0.35, 0]), 0.03)), r: 0.0015 }], 5);
  }
  // les tours de la spire : des bourrelets sur le cône de l'apex
  for (let k = 0; k < 4; k++) {
    const x = 0.11 - k * 0.012, r0 = prof(x) * 1.08;
    const ring = [];
    for (let j = 0; j <= 12; j++) { const a = j / 12 * Math.PI * 2; ring.push({ p: [x - j * 0.0008, r0 * 0.85 + Math.cos(a) * r0, Math.sin(a) * r0], r: 0.003 }); }
    tube(m, 'lambi', ring, 4);
  }
  // LA LÈVRE : une aile qui part du flanc et s'ouvre vers le bas et le côté
  const p = m.g('levre'), NU = 10, NV = 6, k0 = p.pos.length / 3;
  for (let i = 0; i <= NU; i++) {
    const x = 0.07 - i / NU * 0.17, r0 = prof(x), span = 0.075 * Math.sin(Math.PI * (0.15 + 0.85 * i / NU));
    for (let j = 0; j <= NV; j++) {
      const v = j / NV;
      const z = -(r0 * 0.95 + v * span), y = r0 * 0.85 - r0 * 0.6 - v * (r0 * 0.25) + v * v * 0.02;
      p.pos.push(x, Math.max(0.004, y), z);
      p.uv.push(v, i / NU);
    }
  }
  for (let i = 0; i < NU; i++)
    for (let j = 0; j < NV; j++) { const a = k0 + i * (NV + 1) + j, b = a + NV + 1; p.idx.push(a, a + 1, b, a + 1, b + 1, b); }
  smoothNormals(p);
  return m;
}

/**
 * L'HUÎTRE PERLIÈRE, fermée : deux valves plates et rondes, la charnière droite
 * qui déborde en oreillettes, couchée à plat sur le dur. Le bord des valves,
 * nacré, se voit à peine entre elles.
 */
function huitre() {
  const m = model(), p = m.g('huitre');
  const NT = 10, NP = 28;
  for (const side of [1, -1]) {
    const k0 = p.pos.length / 3;
    for (let i = 0; i <= NT; i++) {
      const th = i / NT;                                     // du centre au bord
      for (let j = 0; j <= NP; j++) {
        const ph = j / NP * Math.PI * 2;
        // une valve ronde, coupée droit à la charnière (z > 0,028), écailleuse
        let x = Math.cos(ph) * 0.034 * th, z = Math.sin(ph) * 0.036 * th - 0.004;
        z = Math.min(z, 0.028);
        const scale = 1 + 0.06 * Math.sin(9 * th * Math.PI) * th;
        const y = 0.006 + side * (0.011 * (1 - th * th)) * scale;
        p.pos.push(x * scale, y, z);
        p.uv.push(j / NP, th);
      }
    }
    for (let i = 0; i < NT; i++)
      for (let j = 0; j < NP; j++) {
        const a = k0 + i * (NP + 1) + j, b = a + NP + 1;
        if (side > 0) p.idx.push(a, a + 1, b, a + 1, b + 1, b); else p.idx.push(a, b, a + 1, a + 1, b, b + 1);
      }
  }
  smoothNormals(p);
  // les oreillettes de la charnière, droites, de part et d'autre
  tube(m, 'huitre', [{ p: [-0.045, 0.006, 0.028], r: 0.003 }, { p: [0.045, 0.006, 0.028], r: 0.003 }], 5, 1.6);
  // le liseré de nacre entre les valves
  const rim = [];
  for (let j = 0; j <= 24; j++) { const ph = j / 24 * Math.PI * 2; rim.push({ p: [Math.cos(ph) * 0.0335, 0.006, Math.min(Math.sin(ph) * 0.0355 - 0.004, 0.0275)], r: 0.0016 }); }
  tube(m, 'nacre', rim, 4);
  return m;
}

const out = path.join(__dirname, '..', 'props', 'fond');
const only = process.argv.slice(2);
for (const [name, make] of Object.entries({
  'corail-cerveau': cerveau, 'corail-corne': corne, 'corail-cerf': cerf,
  gorgone, 'eponge-tube': eponge, herbier, oursin, lambi, huitre
})) {
  if (only.length && !only.includes(name)) continue;
  const m = make();
  const n = writeGlb(path.join(out, name + '.glb'), name, [{ name, prims: m.prims() }], 'naval-sim reef-glb');
  console.log(`props/fond/${name}.glb : ${n} sommets`);
}
