/* ÉCRIRE UN .glb — la mise en paquet seule, partagée par les outils qui
 * fabriquent des modèles (town-glb.js, jetty-glb.js).
 *
 *   writeGlb(out, sceneName, parts, generator)
 *
 * parts : [{ name, prims: [{ material: { name, c: [r,g,b,a], r: rugosité },
 *                            pos: [], nrm: [], uv: [], idx: [] }] }]
 * Un NŒUD par pièce, nommé comme elle — c'est par ce nom que le jeu la retrouve —,
 * une primitive par matière. Les matières de même nom sont partagées entre les
 * pièces. Rend le nombre de sommets écrits.
 */
const fs = require('fs');
const path = require('path');

const pad4 = n => (n + 3) & ~3;

function writeGlb(out, sceneName, parts, generator = 'naval-sim') {
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

  const materials = [], matIndex = new Map(), meshes = [], nodes = [];
  let verts = 0;
  for (const part of parts) {
    const primitives = [];
    for (const p of part.prims) {
      if (p.pos.length === 0) continue;
      let mi = matIndex.get(p.material.name);
      if (mi === undefined) {
        materials.push({
          name: p.material.name,
          doubleSided: false,
          pbrMetallicRoughness: { baseColorFactor: p.material.c, metallicFactor: 0, roughnessFactor: p.material.r }
        });
        mi = materials.length - 1;
        matIndex.set(p.material.name, mi);
      }
      primitives.push({
        attributes: {
          POSITION: accessor(new Float32Array(p.pos), 'VEC3', 34962, true),
          NORMAL: accessor(new Float32Array(p.nrm), 'VEC3', 34962),
          TEXCOORD_0: accessor(new Float32Array(p.uv), 'VEC2', 34962)
        },
        indices: accessor(new Uint32Array(p.idx), 'SCALAR', 34963),
        material: mi
      });
      verts += p.pos.length / 3;
    }
    if (primitives.length === 0) continue;
    meshes.push({ name: part.name, primitives });
    nodes.push({ name: part.name, mesh: meshes.length - 1 });
  }

  const bin = Buffer.concat(chunks);
  const gltf = {
    asset: { version: '2.0', generator },
    scene: 0, scenes: [{ name: sceneName, nodes: nodes.map((_, i) => i) }],
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
  return verts;
}

module.exports = { writeGlb };
