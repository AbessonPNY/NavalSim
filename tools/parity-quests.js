/* Releve LES QUETES : ou tombe le lieu d une etape, et quand une etape est
   remplie.

   Une quete ne calcule presque rien -- et c est justement pourquoi elle se
   verifie mal a l oeil. Un lieu mal resolu envoie le joueur a deux milles du
   bon endroit sans que rien ne s allume ; une etape qui s accomplit un cran
   trop tot fait sauter un message que personne ne reverra. Le releve prend donc
   les deux : le LIEU de chaque forme de la fiche, et le deroule complet d une
   quete d essai le long d une route ecrite ici.

   La quete d essai est ecrite DANS ce fichier et recopiee dans le releve : les
   deux cotes lisent alors la meme fiche, et une divergence ne peut venir que du
   code. */
const fs = require('fs');
const path = require('path');

const root = path.join(__dirname, '..');
const { decodeGreyPng } = require('./parity-world.js');

// world.js et quests.js tournent dans un navigateur : le minimum de decor.
global.window = global;
eval(fs.readFileSync(path.join(root, 'js', 'world.js'), 'utf8'));
eval(fs.readFileSync(path.join(root, 'js', 'quests.js'), 'utf8'));

const region = JSON.parse(fs.readFileSync(path.join(root, 'world', 'caraibes.json'), 'utf8'));
const img = decodeGreyPng(fs.readFileSync(path.join(root, region.relief.image)));
const W = new Naval.World(region, img);

/* TOUTES LES FORMES DE LIEU que le format accepte, y compris celles qu on
   n ecrirait pas soi-meme : le port inconnu, le champ vide, l ancien nom
   « island ». Ce qu une fiche mal remplie donne doit etre le meme des deux
   cotes, sinon le portage se separe le jour ou quelqu un se trompe. */
const places = [
  { title: 'le ponton', at: { port: 'port-royal' } },
  { title: 'le ponton, autre port', at: { port: 'port-antonio' } },
  { title: 'ancien nom', at: { island: 'petit-goave' } },
  { title: 'deux milles au sud', at: { port: 'port-royal', bearing: 180, miles: 2 } },
  { title: 'un mille a l est', at: { port: 'port-royal', bearing: 90, miles: 1 } },
  { title: 'en metres, plein nord', at: { port: 'yallahs', bearing: 0, distance: 900 } },
  { title: 'relevement sans distance', at: { port: 'negril', bearing: 235 } },
  { title: 'distance sans relevement', at: { port: 'lucea', distance: 700 } },
  { title: 'lat/lon', at: { lat: 19.85, lon: -73.62 } },
  { title: 'metres du monde', at: { x: -4000, z: 9000 } },
  { title: 'port inconnu', at: { port: 'tombouctou' } },
  { title: 'rien du tout', at: {} },
  // les rayons par defaut de chaque objectif, et un rayon donne
  { title: 'rayon par defaut reach', at: { port: 'old-harbour' }, goal: 'reach' },
  { title: 'rayon par defaut leave', at: { port: 'old-harbour' }, goal: 'leave' },
  { title: 'rayon par defaut stop', at: { port: 'old-harbour' }, goal: 'stop' },
  { title: 'rayon par defaut dock', at: { port: 'old-harbour' }, goal: 'dock' },
  { title: 'rayon donne', at: { port: 'old-harbour' }, goal: 'dock', radius: 123.5 }
];

/* LA QUETE D ESSAI : les quatre objectifs, dans l ordre ou ils se compliquent.
   Les rayons et les seuils sont ecrits a la main pour que la route ci-dessous
   les franchisse de peu -- un banc qui passerait a cent metres de la limite ne
   verrait pas un decalage d un cran. */
const quest = {
  id: 'essai',
  title: 'Quete d essai',
  summary: 'Elle ne sert qu au banc.',
  intro: 'On appareille.',
  steps: [
    { title: 'Quitter la rade', at: { port: 'port-royal' }, goal: 'leave', radius: 900,
      brief: 'Sortez du cercle.', message: 'Dehors.' },
    { title: 'Toucher le point', at: { port: 'port-royal', bearing: 90, miles: 1 }, goal: 'reach',
      radius: 260, message: 'Touche.' },
    { title: 'Mettre en panne', at: { port: 'yallahs', bearing: 180, distance: 500 }, goal: 'stop',
      radius: 300, maxSpeed: 1.5, hold: 3.5, message: 'En panne.' },
    { title: 'Accoster', at: { port: 'yallahs' }, goal: 'dock', radius: 400, message: 'A quai.' }
  ],
  outro: 'Fin de l essai.'
};

/* LA ROUTE : des positions absolues, une par pas de 0,1 s. Elle est batie a
   partir des lieux eux-memes pour rester juste si la cote est repeinte -- ce qui
   est la seule facon d ecrire un banc qui ne se perime pas. */
const Q = new Naval.Quests(W);
Q.load([quest]);
const P = quest.steps.map(s => Q.place(s));

const track = [];
function leg(from, to, n, speed, moored) {
  for (let k = 1; k <= n; k++) {
    const t = k / n;
    track.push({ x: from.x + (to.x - from.x) * t, z: from.z + (to.z - from.z) * t,
                 speed, moored, lost: false });
  }
}
// on sort de la rade, on gagne le point, on s y traine, puis on file mouiller
const start = { x: P[0].x, z: P[0].z };
leg(start, { x: P[0].x, z: P[0].z + 1400 }, 40, 6, false);       // sortie du cercle
leg({ x: P[0].x, z: P[0].z + 1400 }, { x: P[1].x, z: P[1].z }, 60, 6, false);
leg({ x: P[1].x, z: P[1].z }, { x: P[2].x, z: P[2].z }, 80, 6, false);
for (let k = 0; k < 60; k++) track.push({ x: P[2].x, z: P[2].z, speed: 0.4, moored: false, lost: false });
leg({ x: P[2].x, z: P[2].z }, { x: P[3].x, z: P[3].z }, 40, 5, false);
for (let k = 0; k < 10; k++) track.push({ x: P[3].x, z: P[3].z, speed: 0.1, moored: true, lost: false });

// le deroule, pas a pas : ce qui s affiche, a quel pas, et ou en est le compteur
const shown = [];
Q.onShow = (title, text) => shown.push({ tick: -1, title, text });
Q.start('essai');
for (const s of shown) s.tick = 0;

const dt = 0.1;
const ticks = [];
for (let k = 0; k < track.length; k++) {
  const before = shown.length;
  Q.update(dt, track[k]);
  for (let i = before; i < shown.length; i++) shown[i].tick = k + 1;
  const aim = Q.objective(track[k].x, track[k].z);
  ticks.push({
    step: Q.active ? Q.step : -1,
    hold: Q.hold,
    dist: aim ? aim.dist : -1,
    bearing: aim ? aim.bearing : -1
  });
}

const out = {
  quest,
  // l etape BRUTE va dans le releve : le C# lit la meme fiche, pas sa copie
  places: places.map(s => {
    const p = Q.place(s);
    return { step: s, x: p.x, z: p.z, r: p.r };
  }),
  // la ou le joueur se tient, pour la ligne d objectif
  from: [[0, 0], [1500, -2200], [-9000, 4000]],
  aims: [[0, 0], [1500, -2200], [-9000, 4000]].map(([x, z]) => {
    Q.active = quest; Q.step = 1;
    const a = Q.objective(x, z);
    return { dist: a.dist, bearing: a.bearing, x: a.x, z: a.z, r: a.r, count: a.count };
  }),
  track, ticks, shown,
  done: [...Q.done]
};

// l etat de sortie du banc : la quete doit s etre terminee
Q.active = null;

const dest = path.join(root, 'core', 'parity-quests.json');
fs.writeFileSync(dest, JSON.stringify(out));
console.log('releve des quetes ecrit : ' + dest);
console.log('  ' + out.places.length + ' lieu(x), ' + track.length + ' pas, '
          + shown.length + ' message(s), quete ' + (out.done.length ? 'terminee' : 'INACHEVEE'));
