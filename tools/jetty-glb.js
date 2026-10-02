/* LES PONTONS, écrits en .glb — un petit (7 m de large, le quai d'un port) et un
 * grand (14 m, les pontons de la fiche).
 *
 *   node tools/jetty-glb.js [dossier]        (world/models par défaut)
 *
 * UN PONTON N'A PAS DE FORME FIXE : sa longueur change d'un lieu à l'autre, et la
 * hauteur de chaque jambe dépend du fond sous elle. Le .glb n'est donc pas un
 * ponton, mais ses PIÈCES, que le jeu assemble (JettyNode.Timber) — et c'est leur
 * NOM qui compte :
 *
 *   travee  quatre mètres de tablier le long de +x (de x = 0 à x = 4), centré en
 *           travers sur z ; répété sur toute la longueur, étiré pour tomber juste.
 *           Le dessus du bordage est à 1,90 m au-dessus de la mer : l'origine est
 *           au niveau moyen de l'eau.
 *   pieu    une jambe d'UN MÈTRE de haut, de y = 0 à y = 1, posée à chaque nœud de
 *           la charpente et étirée du fond jusque sous le tablier.
 *   bitte   posée sur le tablier, deux au musoir et une à la racine.
 *
 * Modifiez-les dans Blender en gardant ces noms (les matières, elles, sont
 * libres). Un fichier absent : le ponton est dessiné par le code, comme avant.
 */
const path = require('path');
const { writeGlb } = require('./glb-write');

// les teintes du code (JettyNode), passées en linéaire : glTF les veut ainsi
const lin = h => [(h >> 16) & 255, (h >> 8) & 255, h & 255].map(v => { v /= 255; return v <= 0.04045 ? v / 12.92 : Math.pow((v + 0.055) / 1.055, 2.4); });
const MAT = {
  bois:    { name: 'bois', c: [...lin(0x8a7a5e), 1], r: 0.88 },     // bordages blanchis de soleil
  goudron: { name: 'goudron', c: [...lin(0x4f4334), 1], r: 0.88 },  // goudronnés, mouillés au pied
  bitte:   { name: 'bitte', c: [...lin(0x6b5a3f), 1], r: 0.88 }
};

const DECK_Y = 1.7;        // Berth.DeckY : le milieu de la caisse du tablier

function part(name) {
  const by = new Map();
  function g(mat) {
    if (!by.has(mat)) by.set(mat, { material: MAT[mat], pos: [], nrm: [], uv: [], idx: [] });
    return by.get(mat);
  }
  function quad(mat, a, b, c, d) {
    const p = g(mat);
    const n = [
      (b[1] - a[1]) * (c[2] - a[2]) - (b[2] - a[2]) * (c[1] - a[1]),
      (b[2] - a[2]) * (c[0] - a[0]) - (b[0] - a[0]) * (c[2] - a[2]),
      (b[0] - a[0]) * (c[1] - a[1]) - (b[1] - a[1]) * (c[0] - a[0])
    ];
    const L = Math.hypot(...n) || 1;
    const k = p.pos.length / 3;
    for (const [q, u, v] of [[a, 0, 0], [b, 1, 0], [c, 1, 1], [d, 0, 1]]) {
      p.pos.push(...q); p.nrm.push(n[0] / L, n[1] / L, n[2] / L); p.uv.push(u, v);
    }
    p.idx.push(k, k + 1, k + 2, k, k + 2, k + 3);
  }
  function box(mat, x0, x1, y0, y1, z0, z1) {
    const A = [x0, y0, z0], B = [x1, y0, z0], C = [x1, y1, z0], D = [x0, y1, z0];
    const E = [x0, y0, z1], F = [x1, y0, z1], G = [x1, y1, z1], H = [x0, y1, z1];
    quad(mat, B, A, D, C); quad(mat, E, F, G, H); quad(mat, A, E, H, D);
    quad(mat, F, B, C, G); quad(mat, D, H, G, C); quad(mat, A, B, F, E);
  }
  /** Un fût à huit pans, debout, de y0 à y1. */
  function post(mat, r, y0, y1) {
    const N = 8;
    for (let i = 0; i < N; i++) {
      const a0 = i / N * Math.PI * 2, a1 = (i + 1) / N * Math.PI * 2;
      const p0 = [Math.cos(a0) * r, Math.sin(a0) * r], p1 = [Math.cos(a1) * r, Math.sin(a1) * r];
      quad(mat, [p0[0], y0, p0[1]], [p0[0], y1, p0[1]], [p1[0], y1, p1[1]], [p1[0], y0, p1[1]]);
      quad(mat, [0, y1, 0], [p1[0], y1, p1[1]], [p0[0], y1, p0[1]], [0, y1, 0]);   // le dessus
    }
  }
  return { name, by, box, post, prims: () => [...by.values()] };
}

/** Les rangées de jambes en travers : la règle du code — deux jusqu'à huit mètres, puis une tous les cinq. */
function rows(W) {
  const n = W <= 8 ? 2 : Math.ceil(W / 5) + 1;
  return [...Array(n)].map((_, r) => -W / 2 + W * r / (n - 1));
}

function jetty(W) {
  const hw = W / 2;
  const t = part('travee');
  // la caisse du tablier, puis le bordage en travers : neuf planches sur quatre mètres
  t.box('bois', 0, 4, DECK_Y - 0.14, DECK_Y + 0.14, -hw, hw);
  for (let i = 0; i < 9; i++) {
    const x = 0.06 + i * 0.4433;
    t.box('bois', x, x + 0.30, DECK_Y + 0.14, DECK_Y + 0.20, -hw * 0.98, hw * 0.98);
  }
  // les longerons sous chaque rangée de jambes, et le chapeau qui les lie au droit des jambes
  for (const z of rows(W)) t.box('goudron', 0, 4, DECK_Y - 0.36, DECK_Y - 0.14, z - 0.13, z + 0.13);
  t.box('goudron', -0.15, 0.15, DECK_Y - 0.40, DECK_Y - 0.14, -hw - 0.2, hw + 0.2);

  const p = part('pieu');
  p.post('goudron', 0.21, 0, 1);

  const b = part('bitte');
  b.post('bitte', 0.18, 0, 0.9);
  b.post('bitte', 0.24, 0.9, 1.0);
  return [t, p, b].map(q => ({ name: q.name, prims: q.prims() }));
}

const dir = process.argv[2] || 'world/models';
for (const [name, W] of [['ponton-petit', 7], ['ponton-grand', 14]]) {
  const out = path.join(dir, name + '.glb');
  const n = writeGlb(out, name, jetty(W), 'naval-sim jetty-glb');
  console.log(`${out}  ${n} sommets — travée de 4 m sur ${W} m, pieu d'un mètre, bitte`);
}
