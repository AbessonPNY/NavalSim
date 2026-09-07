/* Turn a .glb you dropped into ships/models/ into a working ship spec.
 *
 *   node tools/add-ship.js ships/models/frigate.glb
 *   node tools/add-ship.js ships/models/frigate.glb --nom "Frégate Sirène" --tonnes 1400
 *
 * It reads the model's real bounding box and writes ships/<id>.json with
 * matching dimensions, then the dev server picks it up on the next reload.
 *
 * The tonnage it guesses is a STARTING POINT, not a measurement: a mesh says
 * nothing about how heavily a vessel is laden. Open the file and tune
 * displacementTonnes until she floats on the waterline you want — the panel's
 * "immersion coque" readout is what you are aiming at (30-45% is typical).
 */
const fs = require('fs');
const path = require('path');

const args = process.argv.slice(2);
const glbRel = args[0];
if(!glbRel){
  console.error('usage: node tools/add-ship.js <chemin.glb> [--nom "Nom"] [--tonnes N] [--id slug]');
  process.exit(1);
}
const opt = (flag, def) => {
  const i = args.indexOf(flag);
  return i >= 0 && args[i+1] ? args[i+1] : def;
};

const ROOT = path.resolve(__dirname, '..');
const glbPath = path.join(ROOT, glbRel);
if(!fs.existsSync(glbPath)){ console.error('introuvable : ' + glbRel); process.exit(1); }

// --- read the glTF JSON chunk and union every POSITION accessor's bounds ---
const buf = fs.readFileSync(glbPath);
if(buf.toString('ascii', 0, 4) !== 'glTF'){ console.error('pas un .glb : ' + glbRel); process.exit(1); }
const jsonLen = buf.readUInt32LE(12);
const gltf = JSON.parse(buf.toString('utf8', 20, 20 + jsonLen));

const lo = [Infinity,Infinity,Infinity], hi = [-Infinity,-Infinity,-Infinity];
let found = 0;
for(const mesh of gltf.meshes || []){
  for(const prim of mesh.primitives || []){
    const ai = prim.attributes && prim.attributes.POSITION;
    if(ai == null) continue;
    const acc = gltf.accessors[ai];
    if(!acc || !acc.min || !acc.max) continue;
    found++;
    for(let k=0;k<3;k++){ lo[k]=Math.min(lo[k],acc.min[k]); hi[k]=Math.max(hi[k],acc.max[k]); }
  }
}
if(!found){
  console.error('aucune borne POSITION dans ce .glb — impossible de déduire les dimensions.');
  process.exit(1);
}

const size = [hi[0]-lo[0], hi[1]-lo[1], hi[2]-lo[2]];
// bow along the longer horizontal axis
const lengthAxis = size[2] >= size[0] ? 'z' : 'x';
const L = Math.max(size[2], size[0]);
const B = Math.min(size[2], size[0]);
const H = size[1];

/* Split the height about the model's own origin when it looks deliberate,
   otherwise assume a little under half is below the waterline. */
const belowOrigin = Math.max(0, -lo[1]);
const keel = (belowOrigin > 0.15*H && belowOrigin < 0.85*H) ? belowOrigin : H*0.45;
const freeboard = Math.max(0.4, H - keel);

/* Estimate the displacement that puts her on a sensible waterline.
   Rather than guess a block coefficient, measure the very envelope the solver
   will use: the same station formulas and the same probe grid as ship-physics.
   Then ask for a target immersion — 36% of the envelope is where the schooner
   and the frigate both sit. */
const TARGET_IMMERSION = 0.36;

function envelopeVolume(hull){
  const S = u => { u = u<0?0:(u>1?1:u); return u*u*(3-2*u); };
  const D = hull.freeboardMid + hull.keelDepth + hull.keelExtra;
  const deckY = t => {
    const f = Math.max(0,(t-0.42)/0.58), a = Math.max(0,(0.42-t)/0.42);
    return hull.freeboardMid + hull.sheerBow*f*f + hull.sheerStern*a*a;
  };
  const keelY = t => {
    let d = hull.keelDepth + hull.dragAft*(1-t);
    d *= (1 - hull.forefootLift*S((t-0.80)/0.20));
    d *= (1 - hull.counterLift *S((0.12-t)/0.12));
    return -d;
  };
  const halfB = t => {
    let sh = Math.pow(Math.max(0,Math.sin(Math.PI*Math.pow(t,hull.waterlinePower))), hull.waterlineFull);
    sh = Math.max(sh, hull.transomWidth*Math.exp(-t*14));
    return (hull.beam/2)*Math.min(1,sh);
  };
  const beamFactor = s =>
    Math.pow(1 - S((s - hull.sectionTuck)/(1 - hull.sectionTuck)), hull.sectionPower);

  const nz=11, nx=7, ny=7;
  const cell = (hull.length/nz)*(hull.beam/nx)*(D/ny);
  const base = -(hull.keelDepth + hull.keelExtra);
  let vol = 0;
  for(let iz=0;iz<nz;iz++){
    const tz=(iz+0.5)/nz, hb=halfB(tz), dY=deckY(tz), kY=keelY(tz);
    if(hb < 0.05*hull.beam/5.8) continue;
    for(let iy=0;iy<ny;iy++){
      const y = base + ((iy+0.5)/ny)*D;
      if(y < kY || y > dY) continue;
      const beam = hb * beamFactor((dY-y)/(dY-kY));
      for(let ix=0;ix<nx;ix++){
        const x = -hull.beam/2 + ((ix+0.5)/nx)*hull.beam;
        if(Math.abs(x) > beam) continue;
        vol += cell;
      }
    }
  }
  return vol;
}

const id = opt('--id', path.basename(glbRel).replace(/\.glb$/i,'')
                        .toLowerCase().replace(/[^a-z0-9]+/g,'-').replace(/^-|-$/g,''));
const nom = opt('--nom', id.charAt(0).toUpperCase() + id.slice(1));
const k = L/24;                                   // scale relative to the schooner

const hull = {
  length: +L.toFixed(2), beam: +B.toFixed(2),
  freeboardMid: +freeboard.toFixed(2),
  sheerBow: +(1.05*k).toFixed(2), sheerStern: +(0.55*k).toFixed(2),
  keelDepth: +keel.toFixed(2), keelExtra: +(0.55*k).toFixed(2),
  dragAft: +(0.55*k).toFixed(2), forefootLift: 0.78, counterLift: 0.42,
  waterlinePower: 0.82, waterlineFull: 0.55, transomWidth: 0.42,
  sectionTuck: 0.38, sectionPower: 0.72
};
const volume = envelopeVolume(hull);
const tonnes = +(opt('--tonnes', (1.025 * volume * TARGET_IMMERSION).toFixed(0)));

const spec = {
  id, name: nom,
  note: 'Fiche générée depuis ' + glbRel + '. Les cotes viennent du maillage ; ' +
        'le tonnage vise ~' + Math.round(TARGET_IMMERSION*100) + '% d\'immersion coque, ' +
        'à ajuster selon la charge voulue.',
  hull,
  displacementTonnes: tonnes,
  cog: { yFracKeel: -0.451, zFracLength: -0.0708 },
  hydro: { resistance: 61, lateralGrip: 654, lateralQuad: 131, heaveDamp: 1100 },
  engine: { topSpeed: +(4.3*Math.sqrt(k)).toFixed(2), sternPower: -0.6 },
  rudder: { power: 91.5, maxAngle: 0.61, postZFrac: -0.46, postY: +(-0.6*k).toFixed(2) },
  rig: {
    type: 'none', sailArea: 0, ceHeight: +(H*0.8).toFixed(1), ceZ: 0,
    maxSheet: 1.48, belly: +(1.65*k).toFixed(2),
    masts: [], jib: null, bowsprit: null
  },
  deckhouse: { beamFrac: 0.42, height: +(0.78*k).toFixed(2), lengthFrac: 0.17, zFrac: -0.11 },
  appearance: { hull:'0x1d2b38', timber:'0x8a6a45', spar:'0xb08c5c',
                house:'0xd9d2c2', canvas:'0xf2ebdc' },
  camera: { chaseDist: +(38*k).toFixed(0), chaseHigh: +(14*k).toFixed(0),
            orbitDist: +(46*k).toFixed(0), helmZFrac: -0.44 },
  model: { glb: glbRel.replace(/\\/g,'/'), lengthAxis, offset:[0,0,0], rotationY: 0 }
};

const out = path.join(ROOT, 'ships', id + '.json');
if(fs.existsSync(out) && !args.includes('--force')){
  console.error('ships/' + id + '.json existe déjà — ajoutez --force pour l\'écraser.');
  process.exit(1);
}
fs.writeFileSync(out, JSON.stringify(spec, null, 2) + '\n', 'utf8');

console.log('modèle    : ' + glbRel + '  (étrave sur ' + lengthAxis + ')');
console.log('dimensions: ' + L.toFixed(1) + ' × ' + B.toFixed(1) + ' × ' + H.toFixed(1) + ' m');
console.log('carène    : ' + volume.toFixed(0) + ' m³ d\'enveloppe');
console.log('tonnage   : ' + tonnes + ' t  (~' + Math.round(TARGET_IMMERSION*100) + '% d\'immersion)');
console.log('écrit     : ships/' + id + '.json');
console.log('\nRechargez la page : elle apparaît dans le sélecteur.');
console.log('Pour publier : node build.js');
