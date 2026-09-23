/*
  POURQUOI UN MODÈLE SORT PLUS GRAND QU'UN AUTRE À LONGUEUR ÉGALE.

  Le moteur met un .glb à l'échelle de la fiche sans rien demander à l'artiste :
  il cherche le maillage de PLUS GROS VOLUME, mesure son étendue le long de
  l'axe de longueur, et multiplie tout pour que cette étendue vaille la longueur
  de la fiche (ShipNode.HullScale). La supposition est qu'un navire a une coque,
  et que sa coque est la plus grosse chose qu'il porte.

  Elle tient mal. Un modèle dont la coque est découpée en morceaux, ou qui porte
  un gréement modélisé d'un bloc, peut faire gagner autre chose que sa coque — et
  l'échelle se prend alors sur une pièce qui n'a aucune raison de mesurer trente
  mètres. Le navire est juste en longueur et faux en tout le reste.

  Cet outil dit qui gagne, et de combien le reste s'en trouve déplacé.

      node tools/glb-scale.js ships/models/pirateship.glb 30
*/
'use strict';
const fs = require('fs');

function readGlb(path) {
  const b = fs.readFileSync(path);
  const jlen = b.readUInt32LE(12);
  return { j: JSON.parse(b.slice(20, 20 + jlen)), bin: b.slice(20 + jlen + 8) };
}

/** La matrice 4×4 d'un nœud glTF (matrix, ou T·R·S). */
function nodeMatrix(n) {
  if (n.matrix) return n.matrix.slice();
  const t = n.translation || [0, 0, 0];
  const r = n.rotation || [0, 0, 0, 1];
  const s = n.scale || [1, 1, 1];
  const [x, y, z, w] = r;
  const m = [
    1 - 2 * (y * y + z * z), 2 * (x * y + z * w), 2 * (x * z - y * w), 0,
    2 * (x * y - z * w), 1 - 2 * (x * x + z * z), 2 * (y * z + x * w), 0,
    2 * (x * z + y * w), 2 * (y * z - x * w), 1 - 2 * (x * x + y * y), 0,
    0, 0, 0, 1];
  for (let c = 0; c < 3; c++) for (let k = 0; k < 3; k++) m[c * 4 + k] *= s[c];
  m[12] = t[0]; m[13] = t[1]; m[14] = t[2];
  return m;
}

const mul = (a, b) => {                       // a puis b, colonne-majeur
  const o = new Array(16).fill(0);
  for (let c = 0; c < 4; c++)
    for (let r = 0; r < 4; r++)
      for (let k = 0; k < 4; k++) o[c * 4 + r] += b[k * 4 + r] * a[c * 4 + k];
  return o;
};
const apply = (m, p) => [
  m[0] * p[0] + m[4] * p[1] + m[8] * p[2] + m[12],
  m[1] * p[0] + m[5] * p[1] + m[9] * p[2] + m[13],
  m[2] * p[0] + m[6] * p[1] + m[10] * p[2] + m[14]];

function run(path, shipLength) {
  const { j } = readGlb(path);
  const found = [];

  function walk(idx, parent) {
    const n = j.nodes[idx];
    const m = mul(nodeMatrix(n), parent);
    if (n.mesh != null) {
      // la boîte du maillage, coins transformés — c'est ce que fait rel * GetAabb()
      let mn = [1e9, 1e9, 1e9], mx = [-1e9, -1e9, -1e9];
      for (const pr of j.meshes[n.mesh].primitives) {
        const a = j.accessors[pr.attributes.POSITION];
        if (!a.min || !a.max) continue;
        for (let c = 0; c < 8; c++) {
          const p = apply(m, [c & 1 ? a.max[0] : a.min[0], c & 2 ? a.max[1] : a.min[1], c & 4 ? a.max[2] : a.min[2]]);
          for (let k = 0; k < 3; k++) { mn[k] = Math.min(mn[k], p[k]); mx[k] = Math.max(mx[k], p[k]); }
        }
      }
      if (mx[0] > mn[0])
        found.push({
          name: n.name || j.meshes[n.mesh].name || '(sans nom)',
          size: [0, 1, 2].map(k => mx[k] - mn[k]),
          mn, mx
        });
    }
    for (const c of n.children || []) walk(c, m);
  }
  const scene = j.scenes[j.scene || 0];
  const I = [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1];
  for (const r of scene.nodes) walk(r, I);

  for (const f of found) f.vol = f.size[0] * f.size[1] * f.size[2];
  found.sort((a, b) => b.vol - a.vol);
  const king = found[0];
  const k = shipLength / king.size[2];

  console.log(`\n${path}  —  fiche : ${shipLength} m`);
  console.log('  les cinq plus gros maillages (le premier décide de l\'échelle) :');
  for (const f of found.slice(0, 5))
    console.log(`    ${f.name.slice(0, 26).padEnd(26)} ${f.size.map(v => v.toFixed(2).padStart(8)).join(' ')}`
      + `   volume ${f.vol.toFixed(0).padStart(9)}`);
  console.log(`  → échelle = ${shipLength} / ${king.size[2].toFixed(2)} (le z de « ${king.name} ») = ${k.toFixed(4)}`);

  // et ce que l'ensemble devient une fois tout multiplié
  let mn = [1e9, 1e9, 1e9], mx = [-1e9, -1e9, -1e9];
  for (const f of found) for (let c = 0; c < 3; c++) { mn[c] = Math.min(mn[c], f.mn[c]); mx[c] = Math.max(mx[c], f.mx[c]); }
  console.log(`  → une fois à l'échelle : long ${((mx[2] - mn[2]) * k).toFixed(2)} m,`
    + ` large ${((mx[0] - mn[0]) * k).toFixed(2)} m, haut ${((mx[1] - mn[1]) * k).toFixed(2)} m`
    + ` (quille ${(mn[1] * k).toFixed(2)} m)`);
}

const args = process.argv.slice(2);
if (args.length < 1) { console.error('usage : node tools/glb-scale.js <fichier.glb> [longueur de la fiche]'); process.exit(1); }
run(args[0], args[1] ? parseFloat(args[1]) : 30);
