/* Releve LE COMMERCE : le hachage, le cours des epices, les nouvelles des
   autres ports, et la bourse.

   Le cours est une fonction pure du port et de l heure -- donc il n y a aucune
   raison qu il differe d une piece entre la page et Godot, et s il differe
   l une des deux ment au joueur. Le hachage est compare AU BIT : il multiplie
   en double au-dela de 2^53, et un portage en entiers donnerait d autres
   cours sans que rien a l ecran ne le dise. */
const fs = require('fs');
const path = require('path');

const root = path.join(__dirname, '..');
const { decodeGreyPng } = require('./parity-world.js');

global.window = global;
eval(fs.readFileSync(path.join(root, 'js', 'world.js'), 'utf8'));
eval(fs.readFileSync(path.join(root, 'js', 'purse.js'), 'utf8'));

const region = JSON.parse(fs.readFileSync(path.join(root, 'world', 'caraibes.json'), 'utf8'));
const W = new Naval.World(region, decodeGreyPng(fs.readFileSync(path.join(root, region.relief.image))));
const MK = Naval.Market;

// le palier recale sur le monde, comme la page le fait au chargement
const palier = MK.tune(W.longestLeg);

const keys = W.isles.map(i => i.key).concat(['', 'x', 'tombouctou']);
const hashes = [];
for (const k of keys) for (let n = -6; n <= 240; n += 3) hashes.push({ key: k, n, h: MK._h(k, n) });

// des heures prises partout : au repos, aux paliers, juste avant et apres, et dans le passe
const times = [];
for (let i = 0; i < 60; i++) times.push(i * palier * 0.37 + 13.25);
times.push(0, palier, palier - 0.001, palier + 0.001, -palier * 2.5, -1, 1e6 + 0.5);
const prices = [];
for (const isl of W.isles) for (const t of times)
  prices.push({ key: isl.key, t, spice: MK.spice(isl.key, t), buy: MK.buyPrice(isl.key, t), sell: MK.sellPrice(isl.key, t) });

const news = [];
for (const from of W.isles.slice(0, 4)) for (const to of W.isles) if (to !== from)
  for (const t of [0, 5000, 123456.75]) {
    const n = MK.news(from, to, t);
    news.push({ from: from.key, to: to.key, t, lag: n.lag, sell: n.sell });
  }

const ages = [0, 29, 30, 31, 89.9, 3570, 3599, 3600, 3629.9, 7385, 86399, 200000].map(l => ({ lag: l, s: MK.age(l) }));

const rumours = [];
for (const isl of W.isles) for (const t of [0, 7777, 99999]) for (const rnd of [0, 0.31, 0.999]) {
  const r = MK.rumour(isl, t, rnd);
  rumours.push({ key: isl.key, t, rnd, voile: r.voile, port: r.port, mot: r.mot, lie: r.lie, fort: r.fort, faible: r.faible });
}

// la bourse : une suite d operations et ce que chacune a rendu
const P = new Naval.Purse(MK.DEPART);
const ops = [['take', 97.5], ['take', 1e9], ['add', 12.5], ['add', -40], ['take', 0.49], ['take', 23915], ['take', 1], ['add', 3601]];
const purse = ops.map(([op, n]) => {
  const ok = op === 'take' ? P.take(n) : (P.add(n), true);
  return { op, n, ok, sous: P.sous, ecus: P.ecus, pieces: P.pieces };
});

const out = { palier, longestLeg: W.longestLeg, hashes, prices, news, ages, rumours, purse };
const dest = path.join(root, 'core', 'parity-market.json');
fs.writeFileSync(dest, JSON.stringify(out));
console.log('releve du commerce ecrit : ' + dest);
console.log('  palier ' + palier + ' s, ' + hashes.length + ' hachages, ' + prices.length + ' cours, '
          + news.length + ' nouvelles, ' + rumours.length + ' rumeurs');
