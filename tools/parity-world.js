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

/* Le PNG, decode a la main : en-tete, IDAT concatenes, inflate, defiltrage.
   Node porte zlib, et c est tout ce qui manquait. */
function decodeGreyPng(buf) {
  if (buf.readUInt32BE(0) !== 0x89504e47) throw new Error('ce n est pas un PNG');
  let p = 8, w = 0, h = 0, bits = 0, color = 0;
  const idat = [];
  while (p + 8 <= buf.length) {
    const len = buf.readUInt32BE(p);
    const type = buf.toString('ascii', p + 4, p + 8);
    const at = p + 8;
    if (type === 'IHDR') {
      w = buf.readUInt32BE(at); h = buf.readUInt32BE(at + 4);
      bits = buf[at + 8]; color = buf[at + 9];
      if (buf[at + 12] !== 0) throw new Error('PNG entrelace non gere');
      if (bits !== 8) throw new Error('PNG ' + bits + ' bits non gere');
      if (color === 3) throw new Error('PNG palettise non gere');
    } else if (type === 'IDAT') idat.push(buf.subarray(at, at + len));
    else if (type === 'IEND') break;
    p = at + len + 4;
  }
  const raw = zlib.inflateSync(Buffer.concat(idat));
  const bpp = color === 0 ? 1 : color === 2 ? 3 : color === 4 ? 2 : 4;
  const stride = w * bpp;
  const grey = new Uint8Array(w * h);
  let prev = Buffer.alloc(stride);
  for (let y = 0; y < h; y++) {
    const f = raw[y * (stride + 1)];
    const cur = Buffer.from(raw.subarray(y * (stride + 1) + 1, y * (stride + 1) + 1 + stride));
    for (let i = 0; i < stride; i++) {
      const a = i >= bpp ? cur[i - bpp] : 0, b = prev[i], c = i >= bpp ? prev[i - bpp] : 0;
      if (f === 1) cur[i] = (cur[i] + a) & 255;
      else if (f === 2) cur[i] = (cur[i] + b) & 255;
      else if (f === 3) cur[i] = (cur[i] + ((a + b) >> 1)) & 255;
      else if (f === 4) {
        const pp = a + b - c, pa = Math.abs(pp - a), pb = Math.abs(pp - b), pc = Math.abs(pp - c);
        cur[i] = (cur[i] + (pa <= pb && pa <= pc ? a : pb <= pc ? b : c)) & 255;
      } else if (f !== 0) throw new Error('filtre PNG inconnu : ' + f);
    }
    for (let x = 0; x < w; x++) grey[y * w + x] = cur[x * bpp];
    prev = cur;
  }
  return { w, h, data: grey };
}

/* Le decodeur sert aussi au releve des quetes, qui a besoin du monde pour
   resoudre ses lieux : une definition, plusieurs usagers. Requis comme module,
   ce fichier ne fait donc rien de plus que l offrir. */
module.exports = { decodeGreyPng };
if (require.main !== module) return;

const region = JSON.parse(fs.readFileSync(path.join(root, 'world', 'caraibes.json'), 'utf8'));
const img = decodeGreyPng(fs.readFileSync(path.join(root, region.relief.image)));
const W = new Naval.World(region, img);

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
