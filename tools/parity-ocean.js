/* Releve la MER telle que js/ocean.js la calcule, et l'ecrit en JSON pour que le
   portage C# soit compare au bit pres.

   Ocean tient au constructeur a tout three.js -- materiaux, shaders, cibles de
   rendu. On ne le construit donc PAS : on EMPRUNTE ses methodes a son prototype
   et on leur fournit l'etat qu'elles lisent. C'est le point important, et il vaut
   d'etre dit -- recopier ici les formules de setSeaState donnerait un banc qui
   compare le portage a une deuxieme copie, et qui passerait donc en vert le jour
   ou les deux derivent ensemble de l'original.

   Ce qu'on releve :
     - le spectre entier, composante par composante ;
     - la hauteur echantillonnee sur une grille, a plusieurs instants ;
     - et LE RECENTRAGE, qui est l'invariant le plus dur du projet : la mer doit
       etre continue au saut pres de zero quand l'origine glisse. */
const fs = require('fs');
const path = require('path');

const root = path.join(__dirname, '..');

global.window = global;
global.THREE = {
  Vector3: function (x, y, z) {
    this.x = x || 0; this.y = y || 0; this.z = z || 0;
    this.set = (a, b, c) => { this.x = a; this.y = b; this.z = c; return this; };
    this.copy = (v) => { this.x = v.x; this.y = v.y; this.z = v.z; return this; };
    // la vraie normalisation de three : un vecteur nul reste nul
    this.normalize = () => {
      const n = Math.sqrt(this.x * this.x + this.y * this.y + this.z * this.z);
      if (n > 0) { this.x /= n; this.y /= n; this.z /= n; }
      return this;
    };
  }
};
eval(fs.readFileSync(path.join(root, 'js', 'config.js'), 'utf8'));

// ocean.js declare la classe et beaucoup de GLSL ; on ne veut que le prototype,
// donc on le charge avec juste assez de decor pour que le module s'evalue.
global.THREE.ShaderMaterial = function () {};
global.THREE.PlaneGeometry = function () {};
global.THREE.Mesh = function () {};
global.THREE.WebGLRenderTarget = function () {};
global.THREE.PerspectiveCamera = function () {};
global.THREE.Matrix4 = function () { this.set = () => this; this.multiply = () => this; };
global.THREE.Plane = function () {};
global.THREE.Vector2 = function (x, y) {
  this.x = x || 0; this.y = y || 0;
  this.set = (a, b) => { this.x = a; this.y = b; return this; };
};
global.THREE.Vector4 = function (x, y, z, w) {
  this.x = x || 0; this.y = y || 0; this.z = z || 0; this.w = w || 0;
  this.set = (a, b, c, d) => { this.x = a; this.y = b; this.z = c; this.w = d; return this; };
};
global.THREE.Color = function () {};
global.THREE.UniformsLib = { fog: {} };
global.THREE.UniformsUtils = { merge: (a) => Object.assign({}, ...a) };
global.THREE.DoubleSide = 2;
global.THREE.RepeatWrapping = 1000;
global.THREE.LinearFilter = 1006;
global.THREE.RGBAFormat = 1023;
global.THREE.FloatType = 1015;
eval(fs.readFileSync(path.join(root, 'js', 'stage.js'), 'utf8'));
eval(fs.readFileSync(path.join(root, 'js', 'ocean.js'), 'utf8'));

const C = Naval.Config;
const proto = Naval.Ocean.prototype;

/* L'etat que setSeaState / syncPhase / sample lisent, et rien de plus. */
function makeOcean() {
  const o = {
    C,
    waves: [],
    cpuWaves: [],
    swell: 1.35,
    sharp: 1.0,
    origin: new THREE.Vector3(0, 0, 0),
    world: null,
    band: [],
    _raw: [],
    _pool: [],
    uniforms: {
      uTime: { value: 0 },
      uSharp: { value: 1 },
      uAmpMax: { value: 0 },
      uWaveA: { value: [] },
      uWaveB: { value: [] },
      uWavePhase: { value: [] }
    },
    windSpeed: 0,
    windVec: new THREE.Vector3(0, 0, 0),
    syncWind() { /* touche des uniformes de rendu seulement */ }
  };
  for (let i = 0; i < C.NWAVES; i++) {
    o.band.push({ k: 0, dx: 0, dz: 0, omega: 0, corr: 0 });
    o._raw.push({ amp: 0, w: 0, dir: 0 });
    o._pool.push({ dx: 0, dz: 0, amp: 0, k: 1, omega: 0, Q: 0, L: 1, band: i, phase: 0 });
    o.uniforms.uWaveA.value.push(new THREE.Vector4());
    o.uniforms.uWaveB.value.push(new THREE.Vector2());
    o.uniforms.uWavePhase.value.push(0);
  }
  o.setWind = proto.setWind.bind(o);
  o.setSeaState = proto.setSeaState.bind(o);
  o.syncUniforms = proto.syncUniforms.bind(o);
  o.syncPhase = proto.syncPhase.bind(o);
  o.rebase = proto.rebase.bind(o);
  o.sample = proto.sample.bind(o);
  return o;
}

const cases = [];

// --- le spectre et l'echantillonnage, par etat de mer ---
for (const [force, deg, swell, t0] of [
  [0, 0, 1.35, 0], [2, 45, 1.35, 137.5], [4, 200, 1.35, 4210.25],
  [6, 313, 1.0, 91.75], [8, 90, 1.6, 20000.5], [9.5, 270, 2.0, 613.0]
]) {
  const o = makeOcean();
  o.swell = swell;
  o.uniforms.uTime.value = t0;
  o.setSeaState(force, deg);

  const rec = {
    force, deg, swell, t0,
    windSpeed: o.windSpeed,
    windVec: [o.windVec.x, o.windVec.y, o.windVec.z],
    sharp: o.sharp,
    ampMax: o.uniforms.uAmpMax.value,
    cpuWaves: o.cpuWaves.length,
    waves: o.waves.map(w => ({
      dx: w.dx, dz: w.dz, amp: w.amp, k: w.k,
      omega: w.omega, Q: w.Q, L: w.L, band: w.band, phase: w.phase
    })),
    samples: []
  };
  // une grille large, et des instants qui ne tombent pas rond
  const nrm = new THREE.Vector3();
  for (const t of [t0, t0 + 0.0167, t0 + 3.5, t0 + 611.25]) {
    for (const x of [0, 1.5, -37.25, 480.5, -1499.75]) {
      for (const z of [0, -2.25, 88.5, -613.0, 1211.5]) {
        const y = o.sample(x, z, t, nrm);
        rec.samples.push({ t, x, z, y, nx: nrm.x, ny: nrm.y, nz: nrm.z });
      }
    }
  }
  cases.push(rec);
}

/* --- LE RECENTRAGE, qui est l'invariant ---
   On echantillonne un point MONDE, on fait glisser l'origine, puis on
   re-echantillonne le meme point monde dans le nouveau repere local. La hauteur
   doit etre la meme : c'est tout le sujet de l'origine flottante, et le portage
   doit le tenir aussi. */
const rebases = [];
{
  const o = makeOcean();
  o.uniforms.uTime.value = 1234.5;
  o.setSeaState(5, 120);
  const nrm = new THREE.Vector3();
  const t = 1234.5;
  let shifted = 0;
  for (const [dx, dz] of [[0, 0], [1500, 0], [0, -1500], [12000, 7400], [400000, -250000], [1800000, 1500000]]) {
    o.rebase(dx, dz);
    shifted += 0; // l'origine cumule
    const rec = { dx, dz, originX: o.origin.x, originZ: o.origin.z, pts: [] };
    // des points fixes DANS LE MONDE, donc en local ils reculent de l'origine
    for (const [wx, wz] of [[0, 0], [25, -12], [-300, 640]]) {
      const lx = wx - o.origin.x, lz = wz - o.origin.z;
      rec.pts.push({ wx, wz, lx, lz, y: o.sample(lx, lz, t, nrm) });
    }
    rebases.push(rec);
  }
}

const dest = path.join(root, 'core', 'parity-ocean.json');
fs.writeFileSync(dest, JSON.stringify({ cases, rebases }));
console.log(`releve mer ecrit : ${dest}  (${cases.length} etats, ${rebases.length} recentrages)`);
