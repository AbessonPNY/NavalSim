/* Releve la METEO telle que weather.js et storms.js la font, et l'ecrit en
   JSON. Le pendant C# (Weather, Storms) refait les memes calculs et compare.

   Les deux fichiers sont charges tels quels : ils n'ont besoin que de `window`.

   Le vent tire au hasard (Math.random) : on le remplace ici par un generateur
   a graine, mulberry32, que le banc C# reproduit. Memes tirages, dans le meme
   ordre : les deux cotes doivent suivre le MEME chemin de vent, pas seulement
   les memes statistiques. */
const fs = require('fs');
const path = require('path');

const root = path.join(__dirname, '..');
global.window = global;
for (const f of ['weather.js', 'storms.js', 'calendar.js', 'climate.js'])
  eval(fs.readFileSync(path.join(root, 'js', f), 'utf8'));

function mulberry32(a) {
  return function () {
    a |= 0; a = a + 0x6D2B79F5 | 0;
    let t = Math.imul(a ^ a >>> 15, 1 | a);
    t = t + Math.imul(t ^ t >>> 7, 61 | t) ^ t;
    return ((t ^ t >>> 14) >>> 0) / 4294967296;
  };
}

const out = {};

// --- le vent : une heure de simulation, a pas variables ---
Math.random = mulberry32(20260919);
const w = new Naval.Weather();
w.on = true;
w.sync(3.5, 210);
const dts = [1 / 60, 1 / 30, 0.1, 0.25, 0.5, 1 / 144];
const steps = [];
const sea = { force: 3.5, dir: 210 };
for (let n = 0; n < 20000; n++) {
  const dt = dts[n % dts.length];
  w.update(dt);
  const moved = w.chaseSea(sea, dt, w.force, w.dir);
  if (n % 7 === 0)
    steps.push({ n, force: w.force, dir: w.dir, meanForce: w.meanForce, meanDir: w.meanDir,
                 tgtForce: w.tgtForce, tgtDir: w.tgtDir, seaForce: sea.force, seaDir: sea.dir, moved });
}
out.weather = { seed: 20260919, sync: [3.5, 210], dts, n: 20000, steps };

// --- les depressions : le hachage, les cellules, le temps en un point ---
const st = new Naval.Storms();
const hash = [];
for (let i = -40; i <= 40; i += 3)
  for (let j = -40; j <= 40; j += 3)
    for (let k = 0; k <= 6; k++) hash.push([i, j, k, st._h(i, j, k)]);
out.hash = hash;

const cells = [];
for (const t of [0, 137.5, 3600, 86400 * 3.3])
  for (let i = -8; i <= 8; i++)
    for (let j = -8; j <= 8; j++) {
      const s = st.cellStorm(i, j, t);
      if (s) cells.push({ i, j, t, x: s.x, z: s.z, r: s.r, peak: s.peak, spin: s.spin });
      else cells.push({ i, j, t, none: true });
    }
out.cells = cells;

const at = [];
for (const t of [0, 911, 7200.25])
  for (let x = -60000; x <= 60000; x += 1370)
    for (let z = -60000; z <= 60000; z += 1370) {
      const q = st.at(x, z, t);
      at.push(q ? { x, z, t, i: q.storm.key, dist: q.dist, inten: q.inten, force: q.force,
                    windDeg: q.windDeg, loom: q.loom, toX: q.toX, toZ: q.toZ }
                : { x, z, t, none: true });
    }
out.at = at;

const nearest = [];
for (const [x, z, t] of [[0, 0, 0], [123456, -98765, 5000], [-250000, 40000, 12345.6]]) {
  const f = st.nearest(x, z, t, 12);
  nearest.push(f ? { x, z, t, i: f.storm.key, dist: f.dist } : { x, z, t, none: true });
}
out.nearest = nearest;

// --- le climat : la temperature et les averses, sur des centaines de jours ---
const settings = JSON.parse(fs.readFileSync(path.join(root, 'settings.json'), 'utf8'));
Object.assign(Naval.CLIMATE, settings.climate || {});
Math.random = mulberry32(16901008);
const cal = new Naval.Calendar(settings.calendar && settings.calendar.start);
const cl = new Naval.Climate();
let hour = 6.5;
const climate = [];
const dh = [0.01, 0.05, 0.2, 0.5, 0.03, 1.1];
for (let n = 0; n < 12000; n++) {
  const dtH = dh[n % dh.length];
  const storm = Math.max(0, Math.sin(n * 0.013)) * 0.8;
  const before = hour;
  hour = (hour + dtH) % 24;
  if (hour < before) cal.nextDay();
  cl.update(dtH, cal, hour, storm);
  const p = cl.precipitation(n % 3 === 0 ? 0.4 : 0);
  if (n % 5 === 0)
    climate.push({ n, day: cal.day, hour, temp: cl.temp, amount: cl.amount, showering: !!cl.shower,
                   pAmount: p.amount, snow: p.snow, word: cl.word() });
}
out.climate = { seed: 16901008, start: settings.calendar && settings.calendar.start, hour0: 6.5,
                dh, n: 12000, settings: Naval.CLIMATE, steps: climate };

const dest = path.join(root, 'core', 'parity-weather.json');
fs.writeFileSync(dest, JSON.stringify(out));
console.log(`releve : ${steps.length} pas de vent, ${hash.length} hachages, ${cells.length} cellules, ` +
            `${at.filter(a => !a.none).length}/${at.length} points dans une depression -> ${dest}`);
