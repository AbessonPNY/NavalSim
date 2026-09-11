/* Releve le plan de formes de CHAQUE fiche navire, station par station, et
   l'ecrit en JSON. Le pendant C# recalcule les memes points avec le portage et
   compare.

   C'est l'invariant « un seul plan de formes » etendu au portage : tant que ce
   banc passe, le C# et le JS decrivent la meme coque. S'ils divergent, ce qu'on
   voit sous Godot cessera de correspondre a ce qui flotte, et rien a l'ecran ne
   le dira. */
const fs = require('fs');
const path = require('path');

const root = path.join(__dirname, '..');
const dir = path.join(root, 'ships');

// hull-lines.js et ship-spec.js tournent dans un navigateur : on leur donne le
// minimum de decor pour les charger tels quels, sans en recopier une ligne.
global.window = global;
global.THREE = {
  Vector3: function (x, y, z) { this.x = x; this.y = y; this.z = z; },
  BufferGeometry: function () {
    this.setAttribute = (n, a) => { if (n === 'position') this.__pos = a.array; };
    this.setIndex = (i) => { this.__idx = i; };
    this.computeVertexNormals = () => {};
  },
  Float32BufferAttribute: function (arr) { this.array = arr; }
};
global.fetch = () => { throw new Error('pas de reseau dans ce banc'); };
eval(fs.readFileSync(path.join(root, 'js', 'config.js'), 'utf8'));
eval(fs.readFileSync(path.join(root, 'js', 'ship-spec.js'), 'utf8'));
eval(fs.readFileSync(path.join(root, 'js', 'hull-lines.js'), 'utf8'));

const out = {};
for (const f of fs.readdirSync(dir).filter(f => f.endsWith('.json') && f !== 'index.json')) {
  const json = JSON.parse(fs.readFileSync(path.join(dir, f), 'utf8'));
  const spec = new Naval.ShipSpec(json);
  const hl = new Naval.HullLines(spec);

  const rec = {
    spec: {
      L: spec.L, B: spec.B, keel: spec.keel, D: spec.D, deckMid: spec.deckMid,
      massKg: spec.massKg, cogX: spec.cog.x, cogY: spec.cog.y, cogZ: spec.cog.z,
      drag: spec.drag, lateralLinear: spec.lateralLinear, lateralQuad: spec.lateralQuad,
      heaveDamp: spec.heaveDamp, topSpeed: spec.topSpeed, maxThrust: spec.maxThrust,
      sternPower: spec.sternPower, rudderK: spec.rudderK, rudderMax: spec.rudderMax,
      rudderZ: spec.rudderZ, rudderY: spec.rudderY, sailArea: spec.sailArea,
      ceHeight: spec.ceHeight, ceZ: spec.ceZ, maxSheet: spec.maxSheet,
      jibFootZ: spec.jibFootZ, mastZ: spec.masts.map(m => m.z)
    },
    deckY: [], keelY: [], halfB: [], beamFactor: []
  };
  const N = 64;
  for (let i = 0; i <= N; i++) {
    const t = i / N;
    rec.deckY.push(hl.deckY(t));
    rec.keelY.push(hl.keelY(t));
    rec.halfB.push(hl.halfB(t));
    rec.beamFactor.push(hl.beamFactor(t));
  }
  // et le maillage entier, qui est ce que l'oeil verra reellement
  const geo = hl.buildGeometry();
  rec.mesh = { positions: Array.from(geo.__pos), indices: Array.from(geo.__idx) };
  out[spec.id] = rec;
}

const dest = path.join(__dirname, '..', 'core', 'parity.json');
fs.writeFileSync(dest, JSON.stringify(out));
console.log(`releve ecrit : ${dest}  (${Object.keys(out).length} navires)`);
