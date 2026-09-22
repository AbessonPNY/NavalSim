/* Releve LE MONDE : la geographie, le relief lu dans l image, les ports que la
   fiche fait naitre, le champ de distance au rivage et l abri d un mole.

   Le pendant C# recalcule tout cela avec le portage et compare. C est le meme
   invariant que partout ailleurs dans ce banc : tant qu il passe, la terre que
   Godot dessine est celle contre laquelle la page echouait ses navires. Une
   divergence ici ne se verrait pas a l ecran -- elle se verrait le jour ou une
   coque toucherait un haut-fond qui n existe pas dans l autre version.

   L image est decodee ICI aussi, en pur Node : les deux cotes doivent partir
   des MEMES octets, sinon on comparerait deux reliefs au lieu de deux
   formules. Le releve porte donc une empreinte du gris, qui attrape un
   decodeur fautif avant qu il ne fasse mentir tout le reste. */
const fs = require('fs');
const path = require('path');
const zlib = require('zlib');

const root = path.join(__dirname, '..');

// world.js tourne dans un navigateur : le minimum de decor pour le charger tel
// quel, sans en recopier une ligne. Il ne touche au document que dans
// chartImage(), qui dessine et que ce banc n appelle pas.
global.window = global;
eval(fs.readFileSync(path.join(root, 'js', 'world.js'), 'utf8'));

/* Le PNG : lu par tools/grey-png.js, qui sert aussi aux patches de relief et
   a la refonte des cotes — une definition, plusieurs usagers. Les deux cotes du
   banc doivent partir des MEMES octets, sinon on comparerait deux reliefs au
   lieu de deux formules. */
const { decodeGreyPng } = require('./grey-png.js');

module.exports = { decodeGreyPng };
if (require.main !== module) return;

const region = JSON.parse(fs.readFileSync(path.join(root, 'world', 'caraibes.json'), 'utf8'));
const img = decodeGreyPng(fs.readFileSync(path.join(root, region.relief.image)));
/* Les reliefs LOCAUX : le banc doit les lire comme le jeu, sinon il compare
   deux terres au lieu de deux formules. */
const patches = (region.patches || []).map(p => {
  const f = path.join(root, p.image);
  return fs.existsSync(f) ? Object.assign({}, p, { img: decodeGreyPng(fs.readFileSync(f)) }) : null;
}).filter(Boolean);
const W = new Naval.World(region, img, patches);

/* L empreinte du gris : la somme de tous les octets, et un echantillonnage
   regulier. Si les deux decodeurs ne rendent pas la meme image, le banc le dit
   ICI, au lieu de laisser croire a une divergence de formule. */
let sum = 0;
for (let k = 0; k < img.data.length; k++) sum += img.data[k];
const probes = [];
for (let n = 0; n < 512; n++) probes.push(img.data[Math.floor(n * (img.data.length - 1) / 511)]);

// une grille reguliere sur toute l image, plus le voisinage de chaque port
const pts = [];
const E = W.relief;
for (let j = 0; j < 36; j++) for (let i = 0; i < 36; i++) {
  const lat = E.south + (E.north - E.south) * (j + 0.5) / 36;
  const lon = E.west + (E.east - E.west) * (i + 0.5) / 36;
  const g = Naval.Geo.toXZ(lat, lon);
  pts.push([g.x, g.z]);
}
for (const isl of W.isles)
  for (let a = 0; a < 8; a++) {
    const r = 40 + 55 * a;
    pts.push([isl.x + r * Math.cos(a * 0.9), isl.z + r * Math.sin(a * 0.9)]);
  }

/* DANS CHAQUE RELIEF LOCAL, une grille serree et son BORD : c est la que la
   loi du patch se joue — le gris fin au milieu, le fondu au bord, le grand
   relief au-dela. Sans ces points, le banc ne verrait jamais un patch. */
for (const p of region.patches || [])
  for (let j = -2; j <= 14; j++) for (let i = -2; i <= 14; i++) {
    const lat = p.south + (p.north - p.south) * i / 12;
    const lon = p.west + (p.east - p.west) * j / 12;
    const g = Naval.Geo.toXZ(lat, lon);
    pts.push([g.x, g.z]);
  }

const samples = pts.map(([x, z]) => {
  const g = Naval.Geo.fix(x, z);
  const px = W.pixelAt(x, z);
  const ns = W.nearestShore(x, z);
  return {
    x, z, lat: g.lat, lon: g.lon, pi: px[0], pj: px[1],
    grey: W._grey(x, z),
    island: W._islandHeight(x, z),
    height: W.heightAt(x, z),
    shore: W.shoreDistance(x, z),
    shelter: W.shelter(x, z),
    nsx: ns.x, nsz: ns.z
  };
});

const out = {
  image: { w: img.w, h: img.h, sum, probes },
  geo: { lat0: Naval.Geo.LAT0, lon0: Naval.Geo.LON0, scale: Naval.Geo.SCALE },
  world: { px: W.px, extent: W.extent, longestLeg: W.longestLeg, harbourDepth: W.harbourDepth },
  // la courbe gris -> metres, sur tous les gris entiers
  decode: Array.from({ length: 256 }, (_, v) => W.decode(v)),
  ports: W.isles.map(i => ({
    key: i.key, name: i.name, x: i.x, z: i.z, start: i.start, lat: i.lat, lon: i.lon,
    port: {
      ang: i.port.ang, reach: i.port.reach,
      sx: i.port.sx, sz: i.port.sz, hx: i.port.hx, hz: i.port.hz,
      basin: i.port.basin,
      harbour: i.port.harbour || null
    }
  })),
  // la latitude et la longitude ecrites comme sur une carte
  format: [[17.9375, true], [-76.8411, false], [-0.25, true], [9.999, false]]
    .map(([d, isLat]) => Naval.Geo.format(d, isLat)),
  samples
};

const dest = path.join(root, 'core', 'parity-world.json');
fs.writeFileSync(dest, JSON.stringify(out));
console.log('releve du monde ecrit : ' + dest);
console.log('  image ' + img.w + 'x' + img.h + ', somme ' + sum);
console.log('  ' + W.isles.length + ' port(s), ' + samples.length + ' point(s) releve(s)');
