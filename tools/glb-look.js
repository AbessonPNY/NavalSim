/* REGARDER UN .glb SANS OUVRIR BLENDER.
 *
 *   node tools/glb-look.js creatures/fish.glb
 *
 * Dit ce qu'il contient (objets, mailles, sommets, matières, textures), les
 * mesures de chaque maille, son axe principal, et trace sa SILHOUETTE dans les
 * trois plans. Assez pour répondre aux deux questions qui font perdre le plus
 * de temps : « est-ce que le modèle est bien celui que je crois ? » et « de quel
 * côté regarde-t-il ? ».
 *
 * Il a été écrit le jour où un poisson est arrivé dans le jeu avec la mauvaise
 * forme. Le fichier disait tout : un corps de 2,1 × 2,0 × 1,8 — une sphère —
 * là où la capture montrait un poisson quatre fois plus long que haut. La
 * sculpture n'était pas dans l'export. Chercher cela dans le code du jeu aurait
 * pris la journée ; le lire dans le fichier a pris une minute.
 *
 * Le premier export de ce même poisson pesait 132 octets et ne contenait AUCUN
 * objet. Cela aussi, une lecture le dit tout de suite.
 */
const fs = require('fs');

const file = process.argv[2];
if (!file) { console.error('usage : node tools/glb-look.js <fichier.glb>'); process.exit(1); }

const b = fs.readFileSync(file);
if (b.slice(0, 4).toString() !== 'glTF') { console.error(file + ' : ce n\'est pas un .glb'); process.exit(1); }

let o = 12, js = null, bin = null;
while (o + 8 <= b.length) {
  const len = b.readUInt32LE(o), type = b.slice(o + 4, o + 8).toString();
  if (type === 'JSON') js = JSON.parse(b.slice(o + 8, o + 8 + len).toString());
  else bin = b.slice(o + 8, o + 8 + len);
  o += 8 + len;
}
console.log(`${file} — ${(b.length / 1024).toFixed(1)} Ko, glTF ${js.asset.version}, ${js.asset.generator || 'sans generateur'}`);

const n = k => (js[k] || []).length;
console.log(`  ${n('nodes')} objet(s), ${n('meshes')} maille(s), ${n('materials')} matiere(s), ` +
            `${n('images')} image(s), ${n('animations')} animation(s), ${n('skins')} squelette(s)`);
if (!n('meshes')) {
  console.log('\n  AUCUNE MAILLE. Un export sans objet selectionne, le plus souvent :');
  console.log('  dans Blender, File > Export > glTF 2.0, decochez « Limit to: Selected Objects ».');
  process.exit(0);
}
console.log(`  objets : ${(js.nodes || []).map(x => x.name || '(sans nom)').join(', ')}`);

/* ---- lire un accesseur ---- */
function read(i) {
  const a = js.accessors[i], bv = js.bufferViews[a.bufferView];
  const off = (bv.byteOffset || 0) + (a.byteOffset || 0);
  const comp = { 5120: 1, 5121: 1, 5122: 2, 5123: 2, 5125: 4, 5126: 4 }[a.componentType];
  const size = { SCALAR: 1, VEC2: 2, VEC3: 3, VEC4: 4 }[a.type];
  const stride = bv.byteStride || comp * size;
  const out = [];
  for (let k = 0; k < a.count; k++) {
    const p = off + k * stride, v = [];
    for (let c = 0; c < size; c++) {
      const q = p + c * comp;
      v.push(a.componentType === 5126 ? bin.readFloatLE(q)
           : a.componentType === 5125 ? bin.readUInt32LE(q)
           : a.componentType === 5123 ? bin.readUInt16LE(q)
           : bin.readUInt8(q));
    }
    out.push(size === 1 ? v[0] : v);
  }
  return out;
}

/* ---- la silhouette, par les extremes de chaque colonne ---- */
function sil(P, h, v, hn, vn) {
  const W = 74, H = 19;
  const hs = P.map(p => p[h]), vs = P.map(p => p[v]);
  const h0 = Math.min(...hs), h1 = Math.max(...hs), v0 = Math.min(...vs), v1 = Math.max(...vs);
  const lo = Array(W).fill(null), hi = Array(W).fill(null);
  for (const p of P) {
    const x = Math.round((p[h] - h0) / (h1 - h0 || 1) * (W - 1));
    if (lo[x] === null || p[v] < lo[x]) lo[x] = p[v];
    if (hi[x] === null || p[v] > hi[x]) hi[x] = p[v];
  }
  const g = Array.from({ length: H }, () => Array(W).fill(' '));
  for (let x = 0; x < W; x++) {
    if (lo[x] === null) continue;
    const a = Math.round((1 - (hi[x] - v0) / (v1 - v0 || 1)) * (H - 1));
    const c = Math.round((1 - (lo[x] - v0) / (v1 - v0 || 1)) * (H - 1));
    for (let y = a; y <= c; y++) g[y][x] = (y === a || y === c) ? '#' : '.';
  }
  console.log(`\n    ${hn} vers la droite (${h0.toFixed(2)} a ${h1.toFixed(2)}) · ${vn} vers le haut (${v0.toFixed(2)} a ${v1.toFixed(2)})`);
  for (const row of g) console.log('    |' + row.join(''));
}

for (let m = 0; m < js.meshes.length; m++) {
  const mesh = js.meshes[m];
  console.log(`\n— maille « ${mesh.name || m} » —`);
  let P = [];
  for (const prim of mesh.primitives) {
    const pts = read(prim.attributes.POSITION);
    P = P.concat(pts);
    const has = Object.keys(prim.attributes).join(', ');
    const mat = prim.material != null ? (js.materials[prim.material].name || prim.material) : 'aucune';
    console.log(`  ${pts.length} sommets, ${prim.indices != null ? js.accessors[prim.indices].count / 3 : '?'} triangles` +
                ` · attributs : ${has} · matiere : ${mat}` +
                (prim.targets ? ` · ${prim.targets.length} formes cles` : ''));
  }
  const ext = [0, 1, 2].map(k => {
    const a = P.map(p => p[k]);
    return { lo: Math.min(...a), hi: Math.max(...a) };
  });
  console.log('  etendue : ' + ['X', 'Y', 'Z'].map((s, k) =>
    `${s} ${(ext[k].hi - ext[k].lo).toFixed(2)} (${ext[k].lo.toFixed(2)} a ${ext[k].hi.toFixed(2)})`).join(' · '));

  /* L'axe principal, par la covariance : c'est lui qui dit si l'objet est
     ALLONGE, et dans quelle direction. Un poisson, une coque, un bras de kraken
     doivent y montrer un axe franc ; une valeur proche de un dans les trois
     directions veut dire que l'objet est un patatoide, et non ce qu'on croit. */
  const c = [0, 1, 2].map(k => P.reduce((s, p) => s + p[k], 0) / P.length);
  const M = [[0,0,0],[0,0,0],[0,0,0]];
  for (const p of P) for (let i = 0; i < 3; i++) for (let j = 0; j < 3; j++) M[i][j] += (p[i]-c[i])*(p[j]-c[j]);
  for (let i = 0; i < 3; i++) for (let j = 0; j < 3; j++) M[i][j] /= P.length;
  let v = [0.7, 0.5, 0.3];
  for (let k = 0; k < 300; k++) {
    const w = M.map(r => r[0]*v[0] + r[1]*v[1] + r[2]*v[2]);
    const nn = Math.hypot(...w) || 1; v = w.map(x => x / nn);
  }
  const sd = [0, 1, 2].map(i => Math.sqrt(M[i][i]));
  console.log(`  centre ${c.map(x => x.toFixed(2)).join(' ')} · axe principal ${v.map(x => x.toFixed(2)).join(' ')}` +
              ` · dispersion ${sd.map(x => x.toFixed(2)).join(' ')}`);
  const el = Math.max(...sd) / Math.min(...sd);
  console.log(`  allongement ${el.toFixed(1)} : 1 — ` +
    (el < 1.6 ? 'PATATOIDE. Rien d\'allonge la-dedans : ce n\'est probablement pas le modele que vous croyez exporter.'
              : 'franc, l\'objet a bien une longueur.'));

  /* DE QUEL COTE EST LE NEZ. Une nageoire caudale est un EVENTAIL, un museau est
     une POINTE : on compare donc l'etalement des sommets aux deux extremites de
     l'axe principal, et le bout le plus large est la queue. C'est une devinette,
     pas une lecture — elle est annoncee comme telle —, mais elle a toujours
     raison sur ce qui nage, et elle epargne de lire une silhouette a l'oeil. */
  {
    const t = P.map(q => (q[0]-c[0])*v[0] + (q[1]-c[1])*v[1] + (q[2]-c[2])*v[2]);
    const lo = Math.min(...t), hi = Math.max(...t), d = hi - lo;
    const bout = frac => {
      const s2 = P.filter((q, i) => frac < 0 ? t[i] < lo + 0.10 * d : t[i] > hi - 0.10 * d);
      if (s2.length < 3) return 0;
      let r = 0;
      for (const q of s2) {
        const e = [0,1,2].map(i => q[i]-c[i]);
        const pr = e[0]*v[0]+e[1]*v[1]+e[2]*v[2];
        r = Math.max(r, Math.hypot(e[0]-pr*v[0], e[1]-pr*v[1], e[2]-pr*v[2]));
      }
      return r;
    };
    const arriere = bout(-1), avant = bout(1);
    const axe = ['X', 'Y', 'Z'][v.map(Math.abs).indexOf(Math.max(...v.map(Math.abs)))];
    const sens = Math.sign(v[['X','Y','Z'].indexOf(axe)]);
    const queueDevant = avant > arriere;
    console.log(`  bouts : ${arriere.toFixed(3)} d'un cote, ${avant.toFixed(3)} de l'autre — ` +
      `le plus large est la QUEUE (un eventail), donc le nez regarde ` +
      `${(queueDevant ? -sens : sens) > 0 ? '+' : '-'}${axe} (devinette)`);
  }

  sil(P, 2, 1, 'Z', 'Y');
  sil(P, 0, 1, 'X', 'Y');
  sil(P, 2, 0, 'Z', 'X');
}
