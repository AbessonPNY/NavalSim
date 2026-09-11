/* Releve le SOLVEUR tel que js/ship-physics.js l integre, pas a pas.

   C est la parite la plus exigeante des trois. Le plan de formes et la mer sont
   des fonctions PURES : on les interroge en un point et on compare. Le solveur
   est un INTEGRATEUR — l etat de chaque pas est l entree du suivant — donc un
   ecart d un ulp au premier pas est amplifie par tous ceux d apres. On ne
   compare donc pas seulement le resultat, on compare la TRAJECTOIRE, et on
   regarde a quelle vitesse les deux se separent.

   Le pas est FIXE et le temps avance a la main : la boucle du jeu ne peut pas
   donner deux fois la meme suite de dt, et sans pas fixe il n y aurait rien a
   comparer. C est d ailleurs ce que le CLAUDE.md recommande deja pour toute
   mesure serieuse de ce projet. */
const fs = require('fs');
const path = require('path');

const root = path.join(__dirname, '..');

global.window = global;

// --- le decor three.js dont le solveur a reellement besoin ---
function V3(x, y, z) { this.x = x || 0; this.y = y || 0; this.z = z || 0; }
V3.prototype = {
  set(x, y, z) { this.x = x; this.y = y; this.z = z; return this; },
  copy(v) { this.x = v.x; this.y = v.y; this.z = v.z; return this; },
  clone() { return new V3(this.x, this.y, this.z); },
  add(v) { this.x += v.x; this.y += v.y; this.z += v.z; return this; },
  sub(v) { this.x -= v.x; this.y -= v.y; this.z -= v.z; return this; },
  addScaledVector(v, s) { this.x += v.x * s; this.y += v.y * s; this.z += v.z * s; return this; },
  multiplyScalar(s) { this.x *= s; this.y *= s; this.z *= s; return this; },
  divideScalar(s) { return this.multiplyScalar(1 / s); },
  negate() { this.x = -this.x; this.y = -this.y; this.z = -this.z; return this; },
  dot(v) { return this.x * v.x + this.y * v.y + this.z * v.z; },
  lengthSq() { return this.x * this.x + this.y * this.y + this.z * this.z; },
  length() { return Math.sqrt(this.lengthSq()); },
  normalize() { const l = this.length(); return l === 0 ? this : this.divideScalar(l); },
  setLength(l) { return this.normalize().multiplyScalar(l); },
  cross(v) { return this.crossVectors(this, v); },
  crossVectors(a, b) {
    const ax = a.x, ay = a.y, az = a.z, bx = b.x, by = b.y, bz = b.z;
    this.x = ay * bz - az * by; this.y = az * bx - ax * bz; this.z = ax * by - ay * bx;
    return this;
  },
  applyQuaternion(q) {
    const x = this.x, y = this.y, z = this.z;
    const qx = q.x, qy = q.y, qz = q.z, qw = q.w;
    const ix = qw * x + qy * z - qz * y;
    const iy = qw * y + qz * x - qx * z;
    const iz = qw * z + qx * y - qy * x;
    const iw = -qx * x - qy * y - qz * z;
    this.x = ix * qw + iw * -qx + iy * -qz - iz * -qy;
    this.y = iy * qw + iw * -qy + iz * -qx - ix * -qz;
    this.z = iz * qw + iw * -qz + ix * -qy - iy * -qx;
    return this;
  }
};

function Q4(x, y, z, w) { this.x = x || 0; this.y = y || 0; this.z = z || 0; this.w = w === undefined ? 1 : w; }
Q4.prototype = {
  copy(q) { this.x = q.x; this.y = q.y; this.z = q.z; this.w = q.w; return this; },
  clone() { return new Q4(this.x, this.y, this.z, this.w); },
  identity() { return this.set(0, 0, 0, 1); },
  set(x, y, z, w) { this.x = x; this.y = y; this.z = z; this.w = w; return this; },
  invert() { this.x = -this.x; this.y = -this.y; this.z = -this.z; return this; },
  length() { return Math.sqrt(this.x * this.x + this.y * this.y + this.z * this.z + this.w * this.w); },
  normalize() {
    let l = this.length();
    if (l === 0) { this.x = 0; this.y = 0; this.z = 0; this.w = 1; }
    else { l = 1 / l; this.x *= l; this.y *= l; this.z *= l; this.w *= l; }
    return this;
  },
  setFromAxisAngle(axis, angle) {
    const half = angle / 2, s = Math.sin(half);
    this.x = axis.x * s; this.y = axis.y * s; this.z = axis.z * s; this.w = Math.cos(half);
    return this;
  },
  multiplyQuaternions(a, b) {
    const qax = a.x, qay = a.y, qaz = a.z, qaw = a.w;
    const qbx = b.x, qby = b.y, qbz = b.z, qbw = b.w;
    this.x = qax * qbw + qaw * qbx + qay * qbz - qaz * qby;
    this.y = qay * qbw + qaw * qby + qaz * qbx - qax * qbz;
    this.z = qaz * qbw + qaw * qbz + qax * qby - qay * qbx;
    this.w = qaw * qbw - qax * qbx - qay * qby - qaz * qbz;
    return this;
  },
  premultiply(q) { return this.multiplyQuaternions(q, this); }
};

global.THREE = { Vector3: V3, Quaternion: Q4 };
global.THREE.Vector2 = function (x, y) { this.x = x || 0; this.y = y || 0; this.set = (a, b) => { this.x = a; this.y = b; return this; }; };
global.THREE.Vector4 = function (x, y, z, w) { this.x = x || 0; this.y = y || 0; this.z = z || 0; this.w = w || 0; this.set = (a, b, c, d) => { this.x = a; this.y = b; this.z = c; this.w = d; return this; }; };
for (const n of ['ShaderMaterial', 'PlaneGeometry', 'Mesh', 'WebGLRenderTarget',
                 'PerspectiveCamera', 'Plane', 'Color', 'BufferGeometry',
                 'Float32BufferAttribute'])
  global.THREE[n] = function () {};
global.THREE.Matrix4 = function () { this.set = () => this; this.multiply = () => this; };
global.THREE.UniformsLib = { fog: {} };
global.THREE.UniformsUtils = { merge: (a) => Object.assign({}, ...a) };

eval(fs.readFileSync(path.join(root, 'js', 'config.js'), 'utf8'));
eval(fs.readFileSync(path.join(root, 'js', 'ship-spec.js'), 'utf8'));
eval(fs.readFileSync(path.join(root, 'js', 'hull-lines.js'), 'utf8'));
eval(fs.readFileSync(path.join(root, 'js', 'stage.js'), 'utf8'));
eval(fs.readFileSync(path.join(root, 'js', 'ocean.js'), 'utf8'));
eval(fs.readFileSync(path.join(root, 'js', 'ship-physics.js'), 'utf8'));

const C = Naval.Config;
const oproto = Naval.Ocean.prototype;

function makeOcean(force, deg, t0) {
  const o = {
    C, waves: [], cpuWaves: [], swell: 1.35, sharp: 1.0,
    origin: new V3(0, 0, 0), world: null, band: [], _raw: [], _pool: [],
    uniforms: {
      uTime: { value: t0 || 0 }, uSharp: { value: 1 }, uAmpMax: { value: 0 },
      uWaveA: { value: [] }, uWaveB: { value: [] }, uWavePhase: { value: [] }
    },
    windSpeed: 0, windVec: new V3(0, 0, 0), syncWind() {}
  };
  for (let i = 0; i < C.NWAVES; i++) {
    o.band.push({ k: 0, dx: 0, dz: 0, omega: 0, corr: 0 });
    o._raw.push({ amp: 0, w: 0, dir: 0 });
    o._pool.push({ dx: 0, dz: 0, amp: 0, k: 1, omega: 0, Q: 0, L: 1, band: i, phase: 0 });
    o.uniforms.uWaveA.value.push(new THREE.Vector4());
    o.uniforms.uWaveB.value.push(new THREE.Vector2());
    o.uniforms.uWavePhase.value.push(0);
  }
  for (const m of ['setWind', 'setSeaState', 'syncUniforms', 'syncPhase', 'rebase', 'sample'])
    o[m] = oproto[m].bind(o);
  o.setSeaState(force, deg);
  return o;
}

const out = [];

/* Les scenarios. Chacun isole une partie du solveur, parce qu un ecart sur la
   trajectoire entiere ne dirait pas LAQUELLE a devie. */
const scenarios = [
  { id: 'calme',        ship: 'schooner', force: 0, deg: 0,   steps: 600,
    ctrl: { throttle: 0, rudder: 0, sheet: 0.6, sailsSet: false } },
  { id: 'houle',        ship: 'schooner', force: 5, deg: 210, steps: 600,
    ctrl: { throttle: 0, rudder: 0, sheet: 0.6, sailsSet: false } },
  { id: 'machine',      ship: 'barge',    force: 3, deg: 90,  steps: 900,
    ctrl: { throttle: 1, rudder: 0, sheet: 0, sailsSet: false } },
  { id: 'barre',        ship: 'barge',    force: 3, deg: 90,  steps: 900,
    ctrl: { throttle: 1, rudder: 0.8, sheet: 0, sailsSet: false } },
  { id: 'voiles',       ship: 'schooner', force: 4, deg: 200, steps: 900,
    ctrl: { throttle: 0, rudder: 0, sheet: 0.55, sailsSet: true } },
  { id: 'gros-temps',   ship: 'pirate',   force: 8, deg: 45,  steps: 600,
    ctrl: { throttle: 0, rudder: -0.4, sheet: 0.9, sailsSet: true } },
  { id: 'envahissement', ship: 'frigate', force: 4, deg: 120, steps: 900,
    ctrl: { throttle: 0, rudder: 0, sheet: 0, sailsSet: false }, breach: true }
];

for (const sc of scenarios) {
  const json = JSON.parse(fs.readFileSync(path.join(root, 'ships', sc.ship + '.json'), 'utf8'));
  const spec = new Naval.ShipSpec(json);
  const lines = new Naval.HullLines(spec);
  const phys = new Naval.ShipPhysics(spec, lines);
  const ocean = makeOcean(sc.force, sc.deg, 0);

  if (sc.breach) { phys.breach(1, 0.30, 0.20); phys.breach(3, 0.18, 0.35); }

  const rec = {
    id: sc.id, ship: sc.ship, force: sc.force, deg: sc.deg, steps: sc.steps,
    ctrl: sc.ctrl, breach: !!sc.breach,
    hullVolume: phys.hullVolume,
    probes: phys.probes.length,
    cargoCapacity: phys.cargoCapacity,
    pumpRate: phys.pumpRate,
    comps: phys.comps.map(c => ({ cap: c.cap, halfB: c.halfB, deckY: c.deckY,
                                  keelY: c.keelY, midZ: c.mid.z })),
    track: []
  };

  /* Pas FIXE, temps avance a la main. Le meme 1/120 que settle() emploie, qui
     est le sous-pas reel du jeu. */
  const dt = 1 / 120;
  let t = 0;
  for (let i = 0; i < sc.steps; i++) {
    phys.step(dt, ocean, sc.ctrl, t);
    t += dt;
    // on releve toutes les 50 images : assez pour voir la divergence s installer,
    // assez peu pour que le fichier reste lisible
    if (i % 50 === 49 || i === sc.steps - 1) {
      const b = phys.body;
      rec.track.push({
        i: i + 1, t,
        px: b.pos.x, py: b.pos.y, pz: b.pos.z,
        vx: b.vel.x, vy: b.vel.y, vz: b.vel.z,
        qx: b.quat.x, qy: b.quat.y, qz: b.quat.z, qw: b.quat.w,
        wx: b.angVel.x, wy: b.angVel.y, wz: b.angVel.z,
        mass: b.mass, comY: b.com.y,
        sub: phys.submergedFrac, draft: phys.draft,
        flood: phys.floodVol, fsr: phys.freeSurfaceRise,
        drive: phys.sailDrive, load: phys.sailLoad,
        beta: phys.appWindAngle, tack: phys.tack,
        opt: phys.optSheet === null ? -99 : phys.optSheet
      });
    }
  }
  out.push(rec);
}

const dest = path.join(root, 'core', 'parity-physics.json');
fs.writeFileSync(dest, JSON.stringify(out));
console.log(`releve solveur ecrit : ${dest}  (${out.length} scenarios)`);

/* --- ET SURTOUT : OU S ASSOIT-ELLE ?

   Tout ce qui precede compare des trajectoires, ce qui est necessaire et pas
   suffisant. La question qu on pose vraiment a un solveur de flottaison est
   celle-ci : lachee dans une eau plate, a quelle hauteur s arrete-t-elle ? Le
   tirant d eau et la fraction immergee sont ce que la fiche PROMET, et ce que
   le CLAUDE.md demande de viser entre 30 et 45 %.

   settle() est en outre le seul endroit qui integre longtemps — 1200 fois la
   racine de sa longueur sur vingt-quatre — donc c est aussi le plus long test
   de derive qu on puisse ecrire. */
const settles = [];
for (const name of ['barge', 'bouee-canard', 'cotre', 'frigate', 'frigate17e', 'pirate', 'schooner']) {
  const json = JSON.parse(fs.readFileSync(path.join(root, 'ships', name + '.json'), 'utf8'));
  const spec = new Naval.ShipSpec(json);
  const lines = new Naval.HullLines(spec);
  const phys = new Naval.ShipPhysics(spec, lines);
  const ocean = makeOcean(5, 140, 0);
  const ctrl = { throttle: 0, rudder: 0, sheet: 0, sailsSet: false };

  const y = phys.settle(ocean, ctrl);
  const b = phys.body;
  settles.push({
    ship: name, y,
    qx: b.quat.x, qy: b.quat.y, qz: b.quat.z, qw: b.quat.w,
    sub: phys.submergedFrac, draft: phys.draft,
    hullVolume: phys.hullVolume, tonnes: spec.tonnes,
    fill: spec.fillFraction,
    // et la mer a-t-elle bien ete remise comme elle etait ?
    seaAfter: ocean.seaState, degAfter: ocean.windDeg
  });
}
fs.writeFileSync(path.join(root, 'core', 'parity-settle.json'), JSON.stringify(settles));
console.log(`releve settle ecrit  (${settles.length} navires)`);
