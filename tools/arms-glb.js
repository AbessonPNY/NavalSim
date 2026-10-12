/* LES BRAS DU NAGEUR, en .glb — ce que l'on voit de soi à l'eau, à la première
 * personne : les deux bras, nus, et les mains, ouvertes ou fermées.
 *
 *   node tools/arms-glb.js                 écrit creatures/bras.glb
 *   node tools/arms-glb.js autre/chemin.glb
 *
 * Un plongeur de 1690 — un pêcheur de lambis, un plongeur de perles de la
 * Margarita — allait à l'eau nu ou presque : une chemise de lin trempée pèse et
 * freine. Les bras sont donc nus et hâlés, sans rien d'anachronique.
 *
 * LE JEU POSE CHAQUE PIÈCE LUI-MÊME (ShipDemo.SwimArms.cs), par son NOM, sans
 * armature : il résout l'épaule et le coude (deux os) vers la main qu'il veut.
 *
 *   bras_d,  bras_g          le bras, de l'épaule au coude
 *   avant_bras_d, _g         l'avant-bras, du coude au poignet
 *   main_d,  main_g          la main OUVERTE
 *   poing_d, poing_g         la main FERMÉE (sur le plat-bord, sur un coquillage)
 *
 * Chaque pièce a son ORIGINE À L'ARTICULATION d'où elle part (épaule, coude,
 * poignet) et S'ÉTEND VERS +Y glTF (+Z dans Blender : debout, comme les bras du
 * kraken) ; bras : 0,31 m, avant-bras 0,26 m — le jeu tient à ces longueurs.
 * Les mains : doigts vers +Y, la PAUME tournée vers +Z glTF (−Y dans Blender), le
 * pouce vers +X pour la droite, −X pour la gauche (la gauche est le miroir de la
 * droite). La main fermée serre ce qui est à 7 cm du poignet, devant la paume.
 * Unités : le mètre ; la main mesure 19 cm du poignet au bout du majeur.
 *
 * DANS BLENDER : déplacez les objets à votre guise, mais N'APPLIQUEZ NI LA ROTATION
 * NI L'ÉCHELLE d'un objet tourné (Ctrl A) sans le remettre debout : le jeu lit la
 * maille dans le repère de l'objet, l'origine à l'articulation. Garder l'origine
 * là où elle est (épaule, coude, poignet).
 */
const { writeGlb } = require('./glb-write');

const out = process.argv[2] || 'creatures/bras.glb';

// sRGB → linéaire : glTF lit des couleurs linéaires
const lin = hex => [hex >> 16 & 255, hex >> 8 & 255, hex & 255].map(c => { c /= 255; return c <= 0.04045 ? c / 12.92 : Math.pow((c + 0.055) / 1.055, 2.4); });
const SKIN = { name: 'peau', c: [...lin(0xb98466), 1], r: 0.55 };
const NAIL = { name: 'ongle', c: [...lin(0xc0907c), 1], r: 0.4 };

/* ---- vecteurs ---- */
const add = (a, b) => [a[0] + b[0], a[1] + b[1], a[2] + b[2]];
const sub = (a, b) => [a[0] - b[0], a[1] - b[1], a[2] - b[2]];
const mul = (a, s) => [a[0] * s, a[1] * s, a[2] * s];
const dot = (a, b) => a[0] * b[0] + a[1] * b[1] + a[2] * b[2];
const cross = (a, b) => [a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0]];
const norm = a => { const l = Math.hypot(...a) || 1; return mul(a, 1 / l); };

/** Un maillage en construction : positions, normales lissées à la fin. */
function mesh() { return { pos: [], nrm: [], uv: [], idx: [] }; }

/**
 * UN TUBE le long d'un chemin : des anneaux en ellipse (rx le long de `side`, rz
 * le long de la normale), cadres transportés le long du chemin ; les deux bouts
 * fermés par une calotte arrondie. `side0` donne l'orientation du premier anneau.
 */
function tube(m, path, rx, rz, side0, seg = 14, capA = true, capB = true) {
  const n = path.length;
  const tan = path.map((_, i) => norm(sub(path[Math.min(n - 1, i + 1)], path[Math.max(0, i - 1)])));
  let s = norm(sub(side0, mul(tan[0], dot(side0, tan[0]))));
  const rings = [];
  for (let i = 0; i < n; i++) {
    if (i > 0) { s = sub(s, mul(tan[i], dot(s, tan[i]))); s = norm(s); }   // transport parallèle
    rings.push({ c: path[i], t: tan[i], s, u: cross(tan[i], s), rx: rx[i], rz: rz[i] });
  }
  // les calottes : quelques anneaux qui se resserrent en quart de cercle
  const cap = (r, dir) => {
    const k = [];
    for (const a of [0.45, 0.8, 1.1, 1.35, 1.5]) {
      const f = Math.cos(a), back = Math.sin(a) * Math.min(r.rx, r.rz);
      k.push({ ...r, c: add(r.c, mul(r.t, dir * back)), rx: r.rx * f + 1e-4, rz: r.rz * f + 1e-4 });
    }
    return k;
  };
  let all = rings;
  if (capA) all = [...cap(rings[0], -1).reverse(), ...all];
  if (capB) all = [...all, ...cap(rings[n - 1], 1)];
  const base = m.pos.length / 3;
  all.forEach((r, j) => {
    for (let i = 0; i <= seg; i++) {
      const a = i / seg * Math.PI * 2;
      const p = add(r.c, add(mul(r.s, Math.cos(a) * r.rx), mul(r.u, Math.sin(a) * r.rz)));
      m.pos.push(...p); m.nrm.push(0, 0, 0); m.uv.push(i / seg, j / (all.length - 1));
    }
  });
  for (let j = 0; j < all.length - 1; j++)
    for (let i = 0; i < seg; i++) {
      const a = base + j * (seg + 1) + i, b = a + seg + 1;
      m.idx.push(a, a + 1, b, b, a + 1, b + 1);
    }
}

/** Les normales lissées, par la somme des faces autour de chaque sommet (et la couture refermée). */
function smooth(m) {
  const P = m.pos, N = new Array(P.length).fill(0);
  const key = i => `${P[3 * i].toFixed(5)},${P[3 * i + 1].toFixed(5)},${P[3 * i + 2].toFixed(5)}`;
  const same = new Map();
  for (let i = 0; i < P.length / 3; i++) { const k = key(i); if (!same.has(k)) same.set(k, []); same.get(k).push(i); }
  for (let f = 0; f < m.idx.length; f += 3) {
    const [a, b, c] = [m.idx[f], m.idx[f + 1], m.idx[f + 2]];
    const pa = P.slice(3 * a, 3 * a + 3), pb = P.slice(3 * b, 3 * b + 3), pc = P.slice(3 * c, 3 * c + 3);
    const fn = cross(sub(pb, pa), sub(pc, pa));
    for (const v of [a, b, c]) for (let k = 0; k < 3; k++) N[3 * v + k] += fn[k];
  }
  for (const list of same.values()) {
    const s = [0, 0, 0];
    for (const v of list) for (let k = 0; k < 3; k++) s[k] += N[3 * v + k];
    const n = norm(s);
    for (const v of list) for (let k = 0; k < 3; k++) m.nrm[3 * v + k] = n[k];
  }
  return m;
}

/** Un segment de membre, debout sur +Y : son profil (hauteur, rayon en travers, rayon d'avant en arrière). */
function limb(profile) {
  const m = mesh();
  const path = [], rx = [], rz = [];
  // le profil est échantillonné serré : la lumière rasante d'un bras vu de près trahit les facettes
  for (let i = 0; i < profile.length - 1; i++) {
    const [y0, x0, z0] = profile[i], [y1, x1, z1] = profile[i + 1];
    for (let k = 0; k < 3; k++) {
      const t = k / 3, e = t * t * (3 - 2 * t);
      path.push([0, y0 + (y1 - y0) * t, 0]); rx.push(x0 + (x1 - x0) * e); rz.push(z0 + (z1 - z0) * e);
    }
  }
  const l = profile[profile.length - 1];
  path.push([0, l[0], 0]); rx.push(l[1]); rz.push(l[2]);
  tube(m, path, rx, rz, [1, 0, 0], 20);
  return smooth(m);
}

/* LE BRAS : deltoïde à l'épaule, biceps en avant (+Z), le coude plus étroit.
   Il dépasse un peu ses deux articulations, pour que le joint ne s'ouvre pas. */
const UPPER = limb([[-0.03, 0.050, 0.050], [0.04, 0.050, 0.048], [0.14, 0.043, 0.047], [0.22, 0.040, 0.043], [0.30, 0.036, 0.036], [0.335, 0.034, 0.033]]);
/* L'AVANT-BRAS : charnu près du coude, aplati au poignet (plus large en travers
   de la paume, X, que dans son épaisseur, Z). */
const FORE = limb([[-0.03, 0.036, 0.034], [0.03, 0.042, 0.038], [0.10, 0.039, 0.032], [0.18, 0.032, 0.024], [0.25, 0.028, 0.019], [0.275, 0.028, 0.019]]);

/**
 * LA MAIN DROITE, ouverte (curl ≈ 0,15) ou fermée (curl ≈ 1). Paume vers +Z,
 * doigts vers +Y, pouce vers +X. La paume est un tube aplati ; chaque doigt un
 * tube qui plie trois fois vers la paume.
 */
function hand(curl, thumbCurl) {
  const skin = mesh(), nails = mesh();
  // la paume, du poignet aux jointures
  const pp = [], prx = [], prz = [];
  for (let i = 0; i <= 8; i++) {
    const t = i / 8, y = -0.008 + t * 0.108;
    pp.push([0.002 * t, y, 0.002 - 0.004 * t]);
    prx.push(0.029 + 0.015 * Math.sin(Math.min(1, t * 1.4) * Math.PI / 2));
    prz.push(0.018 - 0.004 * t);
  }
  tube(skin, pp, prx, prz, [1, 0, 0], 20);
  // les quatre doigts : de l'index (côté pouce, +X) à l'auriculaire
  const fingers = [
    { x: 0.029, len: 0.078, r: 0.0098 },
    { x: 0.010, len: 0.087, r: 0.0100 },
    { x: -0.009, len: 0.081, r: 0.0094 },
    { x: -0.027, len: 0.064, r: 0.0084 }
  ];
  for (const f of fingers) {
    const bones = [0.46, 0.30, 0.24].map(k => k * f.len);
    const bends = [0.55, 1.35, 0.85].map(k => k * curl * 1.05 + 0.04);   // la phalange du milieu plie le plus
    let p = [f.x, 0.093, 0.0], dir = [0, 1, 0];
    const path = [p], rx = [f.r * 1.08], rz = [f.r];
    for (let b = 0; b < 3; b++) {
      // plier vers la paume (+Z) autour de l'axe X
      const c = Math.cos(bends[b]), s = Math.sin(bends[b]);
      dir = norm([dir[0], dir[1] * c - dir[2] * s, dir[1] * s + dir[2] * c]);
      for (let k = 1; k <= 3; k++) {
        p = add(p, mul(dir, bones[b] / 3));
        path.push(p);
        const taper = 1 - 0.28 * (path.length / 10);
        rx.push(f.r * 1.05 * taper); rz.push(f.r * 0.92 * taper);
      }
    }
    tube(skin, path, rx, rz, [1, 0, 0], 12);
    // l'ongle : une écaille plate sur le dos de la dernière phalange (côté −Z)
    const tip = path[path.length - 2], back = cross(dir, [1, 0, 0]);
    const nb = add(tip, mul(back, -f.r * 0.62));
    tube(nails, [sub(nb, mul(dir, 0.006)), add(nb, mul(dir, 0.004))], [f.r * 0.7, f.r * 0.7], [f.r * 0.2, f.r * 0.2], [1, 0, 0], 10);
  }
  /* LE POUCE : DEUX phalanges libres, pas trois. Son premier os, le métacarpien, est
     pris dans la paume : il n'en sort que l'éminence, ce gras du pouce qui la prolonge
     vers le poignet. On le dessine donc en un renflement fondu dans le bord de la
     paume, jusqu'à la jointure (la métacarpo-phalangienne), puis les deux phalanges,
     pliées à la jointure et au bout ; l'ongle sur la dernière. Ouvert, il s'écarte en
     avant ; fermé, il passe en travers des doigts repliés. */
  {
    const c = thumbCurl;
    // l'éminence : du talon de la main à la jointure, large et plate, dans le bord de la paume
    const base = [0.016, 0.008, 0.006];
    const joint = [0.040 - 0.004 * c, 0.046 + 0.004 * c, 0.014 + 0.010 * c];
    const thenar = [], trx = [], trz = [];
    for (let k = 0; k <= 4; k++) {
      const t = k / 4;
      thenar.push(add(base, mul(sub(joint, base), t)));
      trx.push(0.019 - 0.006 * t); trz.push(0.015 - 0.003 * t);
    }
    tube(skin, thenar, trx, trz, [0, 0, 1], 14);
    // les deux phalanges
    let dir = norm([0.50 - 1.05 * c, 0.80 - 0.35 * c, 0.30 + 0.45 * c]);
    let q = joint;
    const path = [q], rx = [0.0122], rz = [0.0110];
    const bones = [0.033, 0.027];
    const bend = [0.10 + 0.25 * c, 0.15 + 0.55 * c];    // à la jointure, puis au bout
    const axis = norm([0.3, -0.5, 0.8]);                  // vers la paume et vers l'index
    for (let b = 0; b < 2; b++) {
      const a = bend[b], co = Math.cos(a), si = Math.sin(a);
      dir = norm(add(add(mul(dir, co), mul(cross(axis, dir), si)), mul(axis, dot(axis, dir) * (1 - co))));
      for (let k = 1; k <= 3; k++) {
        q = add(q, mul(dir, bones[b] / 3));
        path.push(q);
        const taper = 1 - 0.18 * (path.length / 7);
        rx.push(0.0122 * taper); rz.push(0.0108 * taper);
      }
    }
    tube(skin, path, rx, rz, [0, 0, 1], 12);
    // l'ongle : sur le dos de la dernière phalange, à l'opposé de la pulpe (qui regarde l'index et la paume)
    const tip = path[path.length - 2];
    const back = norm(sub(mul(axis, -1), mul(dir, dot(mul(axis, -1), dir))));
    const nb = add(tip, mul(back, 0.0068));
    tube(nails, [sub(nb, mul(dir, 0.007)), add(nb, mul(dir, 0.005))], [0.0085, 0.0085], [0.0022, 0.0022], cross(dir, back), 10);
  }
  return [smooth(skin), smooth(nails)];
}

/** Le miroir en X : la main gauche, le bras gauche ; les triangles retournés pour garder leur face. */
function mirror(m) {
  const r = { pos: m.pos.slice(), nrm: m.nrm.slice(), uv: m.uv.slice(), idx: [] };
  for (let i = 0; i < r.pos.length; i += 3) { r.pos[i] = -r.pos[i]; r.nrm[i] = -r.nrm[i]; }
  for (let f = 0; f < m.idx.length; f += 3) r.idx.push(m.idx[f], m.idx[f + 2], m.idx[f + 1]);
  return r;
}

const prim = (material, m) => ({ material, ...m });
const OPEN = hand(0.12, 0.05), FIST = hand(1.0, 1.0);
const parts = [];
/* POSÉS POUR BLENDER : chaque bras debout, assemblé de l'épaule aux doigts, la droite
   en +X et la gauche en −X, le poing à côté de la main ouverte. Le jeu ne lit PAS
   ces places — il pose chaque pièce lui-même par son origine — : elles ne servent
   qu'à ouvrir le fichier sur des bras entiers plutôt que sur huit pièces empilées. */
for (const [suffix, f, sx] of [['_d', m => m, 1], ['_g', mirror, -1]]) {
  parts.push({ name: 'bras' + suffix, translation: [0.30 * sx, 0, 0], prims: [prim(SKIN, f(UPPER))] });
  parts.push({ name: 'avant_bras' + suffix, translation: [0.30 * sx, 0.31, 0], prims: [prim(SKIN, f(FORE))] });
  parts.push({ name: 'main' + suffix, translation: [0.30 * sx, 0.57, 0], prims: [prim(SKIN, f(OPEN[0])), prim(NAIL, f(OPEN[1]))] });
  parts.push({ name: 'poing' + suffix, translation: [0.55 * sx, 0.57, 0], prims: [prim(SKIN, f(FIST[0])), prim(NAIL, f(FIST[1]))] });
}
const verts = writeGlb(out, 'bras', parts, 'naval-sim arms-glb.js');
console.log(`${out} : ${parts.length} pièces, ${verts} sommets`);
