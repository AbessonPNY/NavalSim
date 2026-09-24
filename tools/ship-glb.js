/*
  UN .glb BÂTI SUR LA FICHE, ET NON L'INVERSE.

  `add-ship.js` fait le chemin habituel : on modèle un navire, l'outil en déduit
  une fiche. Celui-ci fait l'autre — on connaît les cotes d'un bâtiment réel, on
  écrit sa fiche, et le modèle en découle. C'est le seul moyen d'être SÛR que ce
  qu'on voit est ce qui flotte : la coque est lofée par `js/hull-lines.js`, le
  MÊME code qui dessine la coque de la page et qui pose la grille de sondes du
  solveur. Aucune proportion ne peut dériver, puisqu'il n'y en a qu'une.

  Ce n'est pas un modèle d'artiste : c'est une maquette juste, à détailler dans
  Blender si on veut. Mais elle porte déjà ce que le moteur sait lire — une
  coque aux cotes de la fiche, des mâts et des vergues au plan de voilure, et une
  batterie NOMMÉE (canonBabord_001…), dont chaque tube a l'âme de son calibre.

      node tools/ship-glb.js speedwell
      node tools/ship-glb.js speedwell --out ships/models/speedwell.glb
*/
'use strict';
const fs = require('fs');
const path = require('path');

const ROOT = path.resolve(__dirname, '..');

/* ------------------------------------------------------------------ */
/*  LE DÉCOR MINIMAL dont hull-lines a besoin : trois classes, pas une */
/*  ligne de three.js — on ne charge pas un moteur de rendu pour lofer */
/*  quatorze couples.                                                   */
/* ------------------------------------------------------------------ */
global.window = global;
global.THREE = {
  Vector3: function (x, y, z) { this.x = x || 0; this.y = y || 0; this.z = z || 0; },
  BufferGeometry: function () {
    this.attributes = {}; this.index = null;
    this.setAttribute = (n, a) => { this.attributes[n] = a; };
    this.setIndex = i => { this.index = { array: i }; };
    this.computeVertexNormals = () => {};
  },
  Float32BufferAttribute: function (arr) { this.array = arr; }
};
require(path.join(ROOT, 'js', 'ship-spec.js'));
require(path.join(ROOT, 'js', 'hull-lines.js'));

/* ------------------------------------------------------------------ */
/*  LES PIÈCES D'UN .glb                                               */
/* ------------------------------------------------------------------ */

/** Un maillage en construction : positions, triangles, et de quoi les poser. */
function Mesh(name, material) {
  return { name, material, V: [], F: [] };
}

/**
 * LES NORMALES, ÉCRITES. Sans attribut NORMAL, un moteur les déduit face par
 * face : tout est à facettes, un mât est un prisme et une coque une taille de
 * diamant (vu à l'écran). Celles-ci sont lissées — la somme des normales des
 * triangles qui touchent chaque sommet —, ce qui arrondit les tubes et adoucit
 * la coque sans rien changer à sa forme.
 */
function normals(m) {
  const n = new Float64Array(m.V.length);
  for (let t = 0; t < m.F.length; t += 3) {
    const a = m.F[t] * 3, b = m.F[t + 1] * 3, c = m.F[t + 2] * 3;
    const ux = m.V[b] - m.V[a], uy = m.V[b + 1] - m.V[a + 1], uz = m.V[b + 2] - m.V[a + 2];
    const vx = m.V[c] - m.V[a], vy = m.V[c + 1] - m.V[a + 1], vz = m.V[c + 2] - m.V[a + 2];
    const nx = uy * vz - uz * vy, ny = uz * vx - ux * vz, nz = ux * vy - uy * vx;
    for (const i of [a, b, c]) { n[i] += nx; n[i + 1] += ny; n[i + 2] += nz; }
  }
  const out = new Float32Array(m.V.length);
  for (let i = 0; i < n.length; i += 3) {
    const l = Math.hypot(n[i], n[i + 1], n[i + 2]) || 1;
    out[i] = n[i] / l; out[i + 1] = n[i + 1] / l; out[i + 2] = n[i + 2] / l;
  }
  return out;
}
const vert = (m, x, y, z) => { m.V.push(x, y, z); return m.V.length / 3 - 1; };
const tri = (m, a, b, c) => m.F.push(a, b, c);
const quad = (m, a, b, c, d) => { tri(m, a, b, c); tri(m, a, c, d); };

/**
 * Un cylindre d'un point à l'autre, `r0` au pied et `r1` au bout — un mât
 * s'affine, un tube de canon aussi. `sides` faces autour.
 */
function tube(m, p0, p1, r0, r1, sides = 8) {
  const ax = [p1[0] - p0[0], p1[1] - p0[1], p1[2] - p0[2]];
  const len = Math.hypot(ax[0], ax[1], ax[2]);
  if (len < 1e-6) return;
  const u = ax.map(v => v / len);
  // deux perpendiculaires quelconques : n'importe lesquelles font un cylindre
  let a = Math.abs(u[1]) < 0.9 ? [0, 1, 0] : [1, 0, 0];
  let e1 = [u[1] * a[2] - u[2] * a[1], u[2] * a[0] - u[0] * a[2], u[0] * a[1] - u[1] * a[0]];
  const n1 = Math.hypot(...e1); e1 = e1.map(v => v / n1);
  const e2 = [u[1] * e1[2] - u[2] * e1[1], u[2] * e1[0] - u[0] * e1[2], u[0] * e1[1] - u[1] * e1[0]];

  const ring = (p, r) => {
    const out = [];
    for (let i = 0; i < sides; i++) {
      const a2 = (i / sides) * Math.PI * 2, c = Math.cos(a2), s = Math.sin(a2);
      out.push(vert(m, p[0] + (e1[0] * c + e2[0] * s) * r,
                       p[1] + (e1[1] * c + e2[1] * s) * r,
                       p[2] + (e1[2] * c + e2[2] * s) * r));
    }
    return out;
  };
  /* L'ENROULEMENT DES FLANCS, ET IL ÉTAIT À L'ENVERS (signalé, canons noirs à
     l'écran). glTF tient pour face avant le triangle qui tourne dans le SENS
     DIRECT vu du dehors. Le repère (e1, e2, u) étant direct, aller de i à i+1
     tourne de e1 vers e2 ; la suite A[i] → B[i] → B[i+1] donne alors
     u × tangente = −radiale, soit une normale RENTRANTE. On tourne dans l'autre
     sens. Les deux fonds, eux, étaient justes : vérifié au même calcul. */
  const A = ring(p0, r0), B = ring(p1, r1);
  for (let i = 0; i < sides; i++) quad(m, A[i], A[(i + 1) % sides], B[(i + 1) % sides], B[i]);
  const c0 = vert(m, ...p0), c1 = vert(m, ...p1);
  for (let i = 0; i < sides; i++) {
    tri(m, c0, A[(i + 1) % sides], A[i]);
    tri(m, c1, B[i], B[(i + 1) % sides]);
  }
}

/* ------------------------------------------------------------------ */
/*  LA COQUE, LOFÉE PAR LE PROJET LUI-MÊME                             */
/* ------------------------------------------------------------------ */

function hullMesh(spec) {
  const lines = new Naval.HullLines(spec);
  const geo = lines.buildGeometry();
  const m = Mesh('Coque', 0);
  m.V = Array.from(geo.attributes.position.array);
  m.F = Array.from(geo.index.array);
  return m;
}

/* ------------------------------------------------------------------ */
/*  LE GRÉEMENT, AU PLAN DE VOILURE DE LA FICHE                        */
/* ------------------------------------------------------------------ */

function rigMeshes(spec, lines, out) {
  const r = spec.json.rig;
  if (!r || r.type === 'none') return;
  const L = spec.L;
  const deckAt = z => lines.deckY((z / L) + 0.5);

  /* UN MAILLAGE PAR MÂT, ET CE N'EST PAS UN DÉTAIL. Le moteur prend le maillage
     de PLUS GROS VOLUME pour décider de l'échelle, en pariant que c'est la
     coque. Un gréement modélisé d'un bloc gagne ce pari — vergues d'un bord à
     l'autre, hauteur de trois mâts, beaupré débordant l'étrave : la boîte fait
     trois fois celle de la coque, et le navire se trouve mis à l'échelle sur
     son beaupré. Mesuré ici même avant de le découper. */
  const noms = ['Misaine', 'GrandMat', 'Artimon'];
  (r.masts || []).forEach((mt, i) => {
    const spars = Mesh(noms[i] || ('Mat' + (i + 1)), 1);
    const z = mt.zFrac * L, foot = deckAt(z) - 0.6, top = foot + mt.height;
    tube(spars, [0, foot, z], [0, top, z], 0.26, 0.11, 8);
    // les vergues, en fractions de la hauteur du mât, de part et d'autre
    for (const y of mt.yards || []) {
      const h = foot + mt.height * y, half = (mt.yardSpan || 1) * spec.B * 0.5;
      tube(spars, [-half, h, z], [half, h, z], 0.10, 0.05, 6);
    }
    out.push(spars);
  });

  /* L'ANTENNE DE LA LATINE. Le moteur pend la toile latine sur l'espar EN BIAIS
     près du mât le plus en arrière ; sans antenne trouvée, la voile porte sans
     être dessinée — ce qui se voit, l'artimon paraissant n'avoir qu'un hunier
     carré (signalé). On la croise donc sur l'artimon, pic en haut et en arrière,
     amure en bas et en avant, comme une antenne s'endente. */
  const lat = r.lateen;
  if (lat && r.masts && r.masts.length) {
    const mt = r.masts[r.masts.length - 1];
    const z = mt.zFrac * L, foot = deckAt(z) - 0.6;
    const ant = Mesh('Antenne', 1);
    tube(ant, [0, foot + mt.height * 0.16, z + L * 0.11],
              [0, foot + mt.height * 0.88, z - L * 0.16], 0.09, 0.05, 6);
    out.push(ant);
  }
  if (r.bowsprit) {
    const beaupre = Mesh('Beaupre', 1);
    const z = L * 0.47, y = deckAt(z);
    const s = r.bowsprit.steeve || 0.2, len = r.bowsprit.length;
    const tip = [0, y + Math.sin(s) * len, z + Math.cos(s) * len];
    tube(beaupre, [0, y, z], tip, 0.22, 0.09, 8);
    out.push(beaupre);

    /* LA CIVADIÈRE — la voile d'avant d'avant le foc. Une vergue croisée SOUS le
       beaupré, qui portait une petite voile carrée : c'est ce qu'un navire de
       1690 avait à l'étrave, le foc n'arrivant qu'au siècle suivant. Rien à
       apprendre au moteur : une vergue se reconnaît à sa FORME — longue en
       travers, mince, d'équerre sur l'axe — et celle-ci passe l'épreuve comme
       les autres, si bien que la toile vient s'y pendre toute seule. */
    const cv = r.spritsail;
    if (cv) {
      const civ = Mesh('VergueCivadiere', 1);
      const u = cv.at ?? 0.55;
      const p = [0, y + Math.sin(s) * len * u - (cv.under ?? 0.35), z + Math.cos(s) * len * u];
      const half = (cv.span ?? 0.78) * spec.B * 0.5;
      tube(civ, [-half, p[1], p[2]], [half, p[1], p[2]], 0.09, 0.05, 6);
      out.push(civ);
    }
  }
}

/* ------------------------------------------------------------------ */
/*  LA BATTERIE, NOMMÉE ET AU CALIBRE                                  */
/* ------------------------------------------------------------------ */

/**
 * Le diamètre d'un boulet de fonte, en mètres, pour un poids en livres
 * anglaises : d = 1,923·∛livres en pouces (fonte à 7,2). Le tube fait un peu
 * plus du double, ce qui est la proportion d'une pièce d'époque — et c'est le
 * RAPPORT qui compte, le moteur lisant le calibre relatif à la médiane du bord.
 */
const shotBore = lb => 1.923 * Math.cbrt(lb) * 0.0254;

/**
 * `battery` : des rangées { livres, nombre (par bord), y, z0, z1 }. Chaque pièce
 * devient un objet nommé, dans son propre nœud — c'est ce que le moteur attend
 * pour lire une batterie sans rien deviner, et pour la faire reculer au coup.
 */
function gunMeshes(spec, lines, battery, out) {
  const L = spec.L;
  let n = { babord: 0, tribord: 0 };
  for (const row of battery) {
    const bore = shotBore(row.livres), outer = bore * 2.2;
    const barrel = row.barrel;
    for (let i = 0; i < row.nombre; i++) {
      const u = row.nombre === 1 ? 0.5 : i / (row.nombre - 1);
      const z = row.z0 + (row.z1 - row.z0) * u;
      const halfB = lines.halfB((z / L) + 0.5);
      for (const side of [-1, 1]) {
        // tribord est le −x local : c'est la convention de tout le projet
        const key = side < 0 ? 'tribord' : 'babord';
        const name = `canon${key === 'tribord' ? 'Tribord' : 'Babord'}_${String(++n[key]).padStart(3, '0')}`;
        const m = Mesh(name, 2);
        const x0 = side * (halfB * 0.55), x1 = side * (halfB + 0.35);
        tube(m, [x0, row.y, z], [x1, row.y, z], outer * 0.62, outer * 0.5, 8);
        out.push(m);
      }
    }
  }
  return n;
}

/* ------------------------------------------------------------------ */
/*  L'ÉCRITURE                                                          */
/* ------------------------------------------------------------------ */

const MATERIALS = [
  { name: 'hull', pbrMetallicRoughness: { baseColorFactor: [0.17, 0.13, 0.09, 1], metallicFactor: 0, roughnessFactor: 0.85 }, doubleSided: true },
  { name: 'spar', pbrMetallicRoughness: { baseColorFactor: [0.69, 0.55, 0.36, 1], metallicFactor: 0, roughnessFactor: 0.8 }, doubleSided: true },
  { name: 'black_canon', pbrMetallicRoughness: { baseColorFactor: [0.06, 0.06, 0.07, 1], metallicFactor: 0.6, roughnessFactor: 0.45 }, doubleSided: false }
];

function writeGlb(out, meshes) {
  const pad4 = n => (n + 3) & ~3;
  const chunks = [], views = [], accs = [], gltfMeshes = [], nodes = [];
  let off = 0;

  for (const m of meshes) {
    const pos = new Float32Array(m.V);
    const idx = m.V.length / 3 > 65535 ? new Uint32Array(m.F) : new Uint16Array(m.F);
    let min = [Infinity, Infinity, Infinity], max = [-Infinity, -Infinity, -Infinity];
    for (let i = 0; i < m.V.length; i += 3)
      for (let k = 0; k < 3; k++) { min[k] = Math.min(min[k], m.V[i + k]); max[k] = Math.max(max[k], m.V[i + k]); }

    const put = (buf, target) => {
      const pad = pad4(off) - off;
      if (pad) { chunks.push(Buffer.alloc(pad)); off += pad; }
      chunks.push(buf);
      views.push({ buffer: 0, byteOffset: off, byteLength: buf.length, target });
      off += buf.length;
      return views.length - 1;
    };
    const nrm = normals(m);
    const vp = put(Buffer.from(pos.buffer, pos.byteOffset, pos.byteLength), 34962);
    const vn = put(Buffer.from(nrm.buffer, nrm.byteOffset, nrm.byteLength), 34962);
    const vi = put(Buffer.from(idx.buffer, idx.byteOffset, idx.byteLength), 34963);
    accs.push({ bufferView: vp, componentType: 5126, count: m.V.length / 3, type: 'VEC3', min, max });
    accs.push({ bufferView: vn, componentType: 5126, count: m.V.length / 3, type: 'VEC3' });
    accs.push({ bufferView: vi, componentType: idx.BYTES_PER_ELEMENT === 4 ? 5125 : 5123, count: m.F.length, type: 'SCALAR' });
    gltfMeshes.push({ name: m.name, primitives: [{ attributes: { POSITION: accs.length - 3, NORMAL: accs.length - 2 }, indices: accs.length - 1, material: m.material }] });
    nodes.push({ mesh: gltfMeshes.length - 1, name: m.name });
  }

  const bin = Buffer.concat(chunks);
  const gltf = {
    asset: { version: '2.0', generator: 'naval-sim ship-glb' },
    scene: 0,
    scenes: [{ nodes: nodes.map((_, i) => i) }],
    nodes, meshes: gltfMeshes, materials: MATERIALS,
    buffers: [{ byteLength: bin.length }],
    bufferViews: views, accessors: accs
  };
  let js = Buffer.from(JSON.stringify(gltf), 'utf8');
  js = Buffer.concat([js, Buffer.alloc(pad4(js.length) - js.length, 0x20)]);
  const bb = Buffer.concat([bin, Buffer.alloc(pad4(bin.length) - bin.length, 0)]);
  const head = Buffer.alloc(12);
  head.write('glTF', 0, 'ascii'); head.writeUInt32LE(2, 4);
  head.writeUInt32LE(12 + 8 + js.length + 8 + bb.length, 8);
  const h1 = Buffer.alloc(8); h1.writeUInt32LE(js.length, 0); h1.writeUInt32LE(0x4E4F534A, 4);
  const h2 = Buffer.alloc(8); h2.writeUInt32LE(bb.length, 0); h2.writeUInt32LE(0x004E4942, 4);
  fs.mkdirSync(path.dirname(out), { recursive: true });
  fs.writeFileSync(out, Buffer.concat([head, h1, js, h2, bb]));
}

/* ------------------------------------------------------------------ */
/*  LES BATTERIES CONNUES, par fiche — l'armement d'un navire réel.    */
/* ------------------------------------------------------------------ */

const BATTERIES = {
  /* HMS Speedwell, état du 3 avril 1690 : 4 pièces de 9 livres en batterie
     basse, 20 de 6 en batterie haute, 4 de 4 sur la dunette. Par BORD : 2, 10
     et 2 — vingt-huit bouches, 86 livres de bordée. */
  speedwell: [
    { livres: 9, nombre: 2, y: 0.95, z0: -8.0, z1: -4.0 },
    { livres: 6, nombre: 10, y: 2.45, z0: -9.5, z1: 9.0 },
    { livres: 4, nombre: 2, y: 4.15, z0: -8.5, z1: -5.0 }
  ]
};

/* ------------------------------------------------------------------ */

const args = process.argv.slice(2);
const id = args[0];
if (!id) { console.error('usage : node tools/ship-glb.js <id de fiche> [--out chemin.glb]'); process.exit(1); }
const sheetPath = path.join(ROOT, 'ships', id + '.json');
if (!fs.existsSync(sheetPath)) { console.error('fiche introuvable : ' + sheetPath); process.exit(1); }

const json = JSON.parse(fs.readFileSync(sheetPath, 'utf8'));
const spec = new Naval.ShipSpec(json);
spec.json = json;
const lines = new Naval.HullLines(spec);

const meshes = [hullMesh(spec)];
rigMeshes(spec, lines, meshes);
const count = gunMeshes(spec, lines, BATTERIES[id] || [], meshes);

const i = args.indexOf('--out');
const out = i >= 0 && args[i + 1] ? path.join(ROOT, args[i + 1])
                                  : path.join(ROOT, json.model?.glb || ('ships/models/' + id + '.glb'));
writeGlb(out, meshes);

const tris = meshes.reduce((n, m) => n + m.F.length / 3, 0);
console.log(`${path.relative(ROOT, out)} — coque ${spec.L} × ${spec.B} m aux cotes de la fiche, `
  + `${meshes.length} maillage(s), ${tris} triangles, `
  + `${count.babord} pièce(s) par bord`);
