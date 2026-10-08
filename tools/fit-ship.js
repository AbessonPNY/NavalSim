/* CALER UNE FICHE SUR SON MODÈLE — ce qu'on a fait à la main pour le Roebuck (08/10/2026).
 *
 *   node tools/fit-ship.js roebuck                  # dit ce qu'il changerait
 *   node tools/fit-ship.js roebuck --ecrire         # et l'écrit dans la fiche
 *   node tools/fit-ship.js roebuck --flottaison 0.65 --ecrire
 *
 * LA PHYSIQUE NE LIT JAMAIS LE MODÈLE : elle fait flotter le plan de formes de la fiche, et le
 * .glb est posé par-dessus (model.scale, model.offset). Ce qui doit coïncider : la flottaison, le
 * poids qui y mène, la quille, le pont, la mâture. L'outil, dans l'ordre :
 *
 *  1. LA FLOTTAISON, lue sur le modèle (repère du modèle, y) : --flottaison y si on la connaît ;
 *     sinon, s'il porte des pièces de bordée, l'axe de la plus basse à --degagement m au-dessus de
 *     l'eau (1,15 par défaut) ; sinon celle où le modèle déplace le poids que la fiche annonce.
 *  2. LE POIDS : le volume de la coque sous elle, tranche par tranche sur ses triangles, × 1,025.
 *  3. LE PLAN ACCORDÉ : la fiche mise à l'eau par le solveur (NavalSim.Lab « assiette ») ; la quille
 *     saillante (hull.keelExtra, presque sans volume) ramène le fond du plan sur celui du modèle.
 *  4. LE RELÈVEMENT : model.offset[1] pose la flottaison du modèle sur la mer, compte tenu de
 *     l'assiette (l'origine de la fiche flotte un peu au-dessus de l'eau).
 *  5. LE PONT (hull.freeboardMid) sur un maillage nommé pont / deck / tillac, s'il y en a un ; LES
 *     MÂTS (rig.masts[].height) et les centres de voilure (rig.ceHeight, rig.lateen.ceHeight) sur
 *     les espars du modèle au droit de chaque mât de la fiche.
 *
 * Le modèle est lu à l'échelle de la fiche (model.scale, 1 par défaut) ; la coque est le plus gros
 * maillage qui n'est ni une pièce (canon, gun), ni un espar, ni le pont.
 */
'use strict';
const fs = require('fs');
const path = require('path');
const { execFileSync } = require('child_process');

const ROOT = path.join(__dirname, '..');
const args = process.argv.slice(2);
const id = args[0];
if (!id || id.startsWith('--')) {
  console.error('usage : node tools/fit-ship.js <navire> [--flottaison y] [--degagement m] [--ecrire]');
  process.exit(1);
}
const opt = (k, d) => { const i = args.indexOf(k); return i >= 0 && args[i + 1] !== undefined ? Number(args[i + 1]) : d; };
const WRITE = args.includes('--ecrire');
const CLEAR = opt('--degagement', 1.15);
const RHO = 1.025;

// ---------- la fiche ----------
const sheetPath = [path.join(ROOT, 'ships', id, 'fiche.json'), path.join(ROOT, 'mods', 'navires', id, 'fiche.json')]
  .find(f => fs.existsSync(f));
if (!sheetPath) { console.error(`fiche introuvable : ships/${id}/fiche.json`); process.exit(1); }
const sheetDir = path.dirname(sheetPath);
const sheet = JSON.parse(fs.readFileSync(sheetPath, 'utf8'));
const glbRel = sheet.model && sheet.model.glb;
if (!glbRel) { console.error('la fiche ne nomme pas de modèle (model.glb)'); process.exit(1); }
const glbPath = glbRel.includes('/') ? path.join(ROOT, glbRel) : path.join(sheetDir, glbRel);
if (!fs.existsSync(glbPath)) { console.error('modèle introuvable : ' + glbPath); process.exit(1); }
const K = (sheet.model.scale != null) ? sheet.model.scale : 1;
if (sheet.model.scale == null) console.log('model.scale absent : le jeu met le modèle à l\'échelle de lui-même ; l\'outil le lit à 1.');

// ---------- le modèle, ses maillages posés par leur hiérarchie ----------
const buf = fs.readFileSync(glbPath);
const jl = buf.readUInt32LE(12);
const gltf = JSON.parse(buf.toString('utf8', 20, 20 + jl));
const bin = buf.subarray(20 + jl + 8);
const mul = (a, b) => { const r = new Array(16).fill(0); for (let i = 0; i < 4; i++) for (let j = 0; j < 4; j++) for (let k = 0; k < 4; k++) r[j * 4 + i] += a[k * 4 + i] * b[j * 4 + k]; return r; };
function local(n) {
  if (n.matrix) return n.matrix.slice();
  const [x, y, z, w] = n.rotation || [0, 0, 0, 1], [sx, sy, sz] = n.scale || [1, 1, 1], [tx, ty, tz] = n.translation || [0, 0, 0];
  return [(1 - 2 * (y * y + z * z)) * sx, (2 * (x * y + z * w)) * sx, (2 * (x * z - y * w)) * sx, 0,
          (2 * (x * y - z * w)) * sy, (1 - 2 * (x * x + z * z)) * sy, (2 * (y * z + x * w)) * sy, 0,
          (2 * (x * z + y * w)) * sz, (2 * (y * z - x * w)) * sz, (1 - 2 * (x * x + y * y)) * sz, 0, tx, ty, tz, 1];
}
const meshes = [];
function walk(ni, parent) {
  const n = gltf.nodes[ni], m = mul(parent, local(n));
  if (n.mesh !== undefined) {
    const tris = [], V = [];
    for (const p of gltf.meshes[n.mesh].primitives) {
      const a = gltf.accessors[p.attributes.POSITION], v = gltf.bufferViews[a.bufferView];
      const off = (v.byteOffset || 0) + (a.byteOffset || 0), st = v.byteStride || 12;
      const base = V.length;
      for (let i = 0; i < a.count; i++) {
        const x = bin.readFloatLE(off + i * st), y = bin.readFloatLE(off + i * st + 4), z = bin.readFloatLE(off + i * st + 8);
        V.push([(m[0] * x + m[4] * y + m[8] * z + m[12]) * K, (m[1] * x + m[5] * y + m[9] * z + m[13]) * K, (m[2] * x + m[6] * y + m[10] * z + m[14]) * K]);
      }
      if (p.indices !== undefined) {
        const ia = gltf.accessors[p.indices], iv = gltf.bufferViews[ia.bufferView];
        const io = (iv.byteOffset || 0) + (ia.byteOffset || 0), sz = ia.componentType === 5125 ? 4 : ia.componentType === 5123 ? 2 : 1;
        const rd = q => sz === 4 ? bin.readUInt32LE(io + q * 4) : sz === 2 ? bin.readUInt16LE(io + q * 2) : bin[io + q];
        for (let i = 0; i + 2 < ia.count; i += 3) tris.push([V[base + rd(i)], V[base + rd(i + 1)], V[base + rd(i + 2)]]);
      }
    }
    const mat = gltf.meshes[n.mesh].primitives.map(p => p.material !== undefined ? (gltf.materials[p.material].name || '') : '').join(' ');
    const lo = [0, 1, 2].map(k => Math.min(...V.map(p => p[k]))), hi = [0, 1, 2].map(k => Math.max(...V.map(p => p[k])));
    meshes.push({ name: n.name || '', mat, V, tris, lo, hi });
  }
  for (const c of n.children || []) walk(c, m);
}
const I4 = [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1];
for (const r of gltf.scenes[gltf.scene || 0].nodes) walk(r, I4);
const isGun = m => /canon|cannon|gun/i.test(m.name) || /canon|cannon/i.test(m.mat);
const isSpar = m => /m[aâ]t|mast|vergue|yard|beaupr|bowsprit|antenne|civad|artimon|misaine|spar|hune/i.test(m.name) || /spar/i.test(m.mat);
const isDeck = m => /^(pont|deck|tillac)/i.test(m.name);
const vol3 = m => (m.hi[0] - m.lo[0]) * (m.hi[1] - m.lo[1]) * (m.hi[2] - m.lo[2]);
const hull = meshes.filter(m => !isGun(m) && !isSpar(m) && !isDeck(m) && m.tris.length).sort((a, b) => vol3(b) - vol3(a))[0];
if (!hull) { console.error('pas de coque trouvée dans le modèle'); process.exit(1); }
const deck = meshes.find(isDeck);
const L = sheet.hull.length;

// ---------- la coque tranchée : demi-largeur au point (z, y), volume sous une flottaison ----------
function halfAt(z, y) {
  let hb = 0;
  for (const t of hull.tris) {
    const zs = [t[0][2], t[1][2], t[2][2]];
    if (Math.min(...zs) > z || Math.max(...zs) < z) continue;
    const pts = [];
    for (let k = 0; k < 3; k++) { const a = t[k], c = t[(k + 1) % 3]; if ((a[2] - z) * (c[2] - z) <= 0 && a[2] !== c[2]) { const u = (z - a[2]) / (c[2] - a[2]); pts.push([a[0] + u * (c[0] - a[0]), a[1] + u * (c[1] - a[1])]); } }
    if (pts.length < 2) continue;
    const [p, q] = pts;
    if ((p[1] - y) * (q[1] - y) <= 0 && p[1] !== q[1]) { const u = (y - p[1]) / (q[1] - p[1]); hb = Math.max(hb, Math.abs(p[0] + u * (q[0] - p[0]))); }
  }
  return hb;
}
const yMin = hull.lo[1];
function volumeUnder(yw) {
  let v = 0; const dz = 0.5, dy = 0.1;
  for (let z = hull.lo[2] + dz / 2; z < hull.hi[2]; z += dz) {
    let area = 0;
    for (let y = yMin; y < yw; y += dy) area += 2 * halfAt(z, y + Math.min(dy, yw - y) / 2) * Math.min(dy, yw - y);
    v += area * dz;
  }
  return v;
}

// ---------- 1. la flottaison ----------
const guns = meshes.filter(isGun).map(m => ({ name: m.name, y: (m.lo[1] + m.hi[1]) / 2, z: (m.lo[2] + m.hi[2]) / 2, across: (m.hi[0] - m.lo[0]) >= (m.hi[2] - m.lo[2]) }));
const broadside = guns.filter(g => g.across);
let yw, why;
if (args.includes('--flottaison')) { yw = opt('--flottaison', 0); why = 'donnée (--flottaison)'; }
else if (broadside.length) {
  const low = Math.min(...broadside.map(g => g.y));
  yw = low - CLEAR; why = `l'axe de la plus basse pièce de bordée (y ${low.toFixed(2)}) à ${CLEAR} m au-dessus de l'eau`;
} else {
  const want = sheet.displacementTonnes / RHO;
  let a = yMin, b = hull.hi[1];
  for (let i = 0; i < 24; i++) { const m = (a + b) / 2; if (volumeUnder(m) < want) a = m; else b = m; }
  yw = (a + b) / 2; why = `celle où le modèle déplace les ${sheet.displacementTonnes} t de la fiche`;
}

// ---------- 2. le poids ----------
const vol = volumeUnder(yw);
const tonnes = Math.round(vol * RHO);

// ---------- 3. le plan accordé : la quille saillante, par la mise en eau du solveur ----------
const tmp = path.join(require('os').tmpdir(), `fit-ship-${id}.json`);
function settle(s) {
  fs.writeFileSync(tmp, JSON.stringify(s));
  const out = execFileSync('dotnet', ['run', '--project', path.join(ROOT, 'core', 'NavalSim.Lab'), '-c', 'Release', '--', 'assiette', tmp], { encoding: 'utf8' });
  const m = /assiette y=([-\d.]+) tirant=([-\d.]+) immersion=([-\d.]+)/.exec(out);
  if (!m) throw new Error('assiette illisible :\n' + out);
  return { y: +m[1], draft: +m[2], sub: +m[3] };
}
const next = JSON.parse(JSON.stringify(sheet));
next.displacementTonnes = tonnes;
const modelBottom = yw - yMin;                       // la quille du modèle sous l'eau
let st = settle(next);
for (let i = 0; i < 3; i++) {
  const planBottom = next.hull.keelDepth + next.hull.keelExtra - st.y;
  const diff = planBottom - modelBottom;
  if (Math.abs(diff) < 0.03) break;
  next.hull.keelExtra = +Math.min(1.5, Math.max(0.05, next.hull.keelExtra - diff)).toFixed(2);
  st = settle(next);
}
const planBottom = next.hull.keelDepth + next.hull.keelExtra - st.y;

// ---------- 4. le relèvement ----------
const offY = +(-yw - st.y).toFixed(2);
next.model.offset = [next.model.offset ? next.model.offset[0] : 0, offY, next.model.offset ? next.model.offset[2] : 0];

// ---------- 5. le pont, la mâture ----------
const notes = [];
if (deck) {
  const mid = deck.V.filter(p => Math.abs(p[2]) < L * 0.08 && Math.abs(p[0]) < 1.5).map(p => p[1]).sort((a, b) => a - b);
  if (mid.length) next.hull.freeboardMid = +(mid[(mid.length / 2) | 0] + offY).toFixed(2);
} else notes.push('pas de pont nommé (pont, deck, tillac) : hull.freeboardMid gardé');
const oldDeck = sheet.hull.freeboardMid;
const rises = [];
(sheet.rig && sheet.rig.masts || []).forEach((mt, i) => {
  const z = mt.zFrac * L;
  const spar = meshes.filter(isSpar).filter(m => (m.hi[1] - m.lo[1]) > 4 && Math.abs((m.lo[2] + m.hi[2]) / 2 - z) < Math.max(1.5, L * 0.05))
    .sort((a, b) => (b.hi[1] - b.lo[1]) - (a.hi[1] - a.lo[1]))[0];
  if (!spar) { notes.push(`mât ${i} (z ${z.toFixed(1)}) : pas d'espar au droit dans le modèle`); return; }
  const top = spar.hi[1] + offY;
  rises.push(top - (oldDeck + mt.height));
  next.rig.masts[i].height = +(top - next.hull.freeboardMid).toFixed(2);
});
if (rises.length) {
  const r = rises.reduce((a, b) => a + b, 0) / rises.length;
  if (Math.abs(r) > 0.05) {
    next.rig.ceHeight = +(sheet.rig.ceHeight + r).toFixed(2);
    if (next.rig.lateen) next.rig.lateen.ceHeight = +(sheet.rig.lateen.ceHeight + r).toFixed(2);
  }
}

// ---------- le rapport ----------
const f2 = v => (v >= 0 ? ' ' : '') + v.toFixed(2);
console.log(`${id} : modèle ${path.relative(ROOT, glbPath).split(path.sep).join('/')}, coque « ${hull.name} » ${(hull.hi[2] - hull.lo[2]).toFixed(2)} × ${(hull.hi[0] - hull.lo[0]).toFixed(2)} m (fiche ${L} × ${sheet.hull.beam})`);
console.log(`flottaison y ${yw.toFixed(2)} du modèle : ${why}`);
console.log(`  quille ${modelBottom.toFixed(2)} m sous l'eau ; ${broadside.length ? `pièce la plus basse à ${(Math.min(...broadside.map(g => g.y)) - yw).toFixed(2)} m au-dessus` : 'pas de pièce de bordée'}`);
const rows = [
  ['displacementTonnes', sheet.displacementTonnes, next.displacementTonnes],
  ['hull.keelExtra', sheet.hull.keelExtra, next.hull.keelExtra],
  ['hull.freeboardMid', sheet.hull.freeboardMid, next.hull.freeboardMid],
  ['model.offset[1]', sheet.model.offset ? sheet.model.offset[1] : 0, offY],
  ...(sheet.rig && sheet.rig.masts || []).map((m, i) => [`rig.masts[${i}].height`, m.height, next.rig.masts[i].height]),
  ...(sheet.rig ? [['rig.ceHeight', sheet.rig.ceHeight, next.rig.ceHeight]] : []),
  ...(sheet.rig && sheet.rig.lateen ? [['rig.lateen.ceHeight', sheet.rig.lateen.ceHeight, next.rig.lateen.ceHeight]] : [])];
for (const [k, a, b] of rows) console.log(`  ${k.padEnd(22)} ${String(a).padStart(8)} → ${String(b).padStart(8)}${a === b ? '' : '   *'}`);
console.log(`mise en eau : origine ${f2(st.y)} m sur l'eau, tirant ${st.draft.toFixed(2)} m, ${(st.sub * 100).toFixed(1)} % immergé ; fond du plan ${planBottom.toFixed(2)} m sous l'eau contre ${modelBottom.toFixed(2)} pour le modèle`);
if (sheet.cargoTonnes) {
  let a = yw, b = hull.hi[1], want = (tonnes + sheet.cargoTonnes) / RHO;
  for (let i = 0; i < 20; i++) { const m = (a + b) / 2; if (volumeUnder(m) < want) a = m; else b = m; }
  console.log(`chargé (+${sheet.cargoTonnes} t de cale) : il enfonce de ${((a + b) / 2 - yw).toFixed(2)} m`);
}
for (const n of notes) console.log('  ! ' + n);
fs.rmSync(tmp, { force: true });

// ---------- l'écriture, dans le style compact des fiches ----------
if (WRITE) {
  function inline(v) { return JSON.stringify(v).replace(/":/g, '": ').replace(/,(?=["{\[\-0-9tfn])/g, ', ').replace(/\{(?! )/g, '{ ').replace(/(?<! )\}/g, ' }'); }
  function fmt(v, ind, depth) {
    const pad = '  '.repeat(ind);
    if (v === null || typeof v !== 'object') return JSON.stringify(v);
    const one = inline(v);
    if (depth >= 2 && one.length + pad.length < 120) return one;
    if (Array.isArray(v)) { if (v.every(x => typeof x !== 'object')) return one; return '[\n' + v.map(x => pad + '  ' + fmt(x, ind + 1, depth + 1)).join(',\n') + '\n' + pad + ']'; }
    return '{\n' + Object.entries(v).map(([k, x]) => pad + '  ' + JSON.stringify(k) + ': ' + fmt(x, ind + 1, depth + 1)).join(',\n') + '\n' + pad + '}';
  }
  fs.writeFileSync(sheetPath, fmt(next, 0, 0) + '\n');
  console.log('écrit : ' + path.relative(ROOT, sheetPath).split(path.sep).join('/'));
} else console.log('(rien d\'écrit — ajoutez --ecrire)');
