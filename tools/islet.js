/* UN ÎLOT AU LARGE, QU'ON N'ATTEINT QU'À LA CHALOUPE.
 *
 *   node tools/islet.js ilot-cocotiers 17.87 -76.72
 *   node tools/islet.js ilot-cocotiers 17.87 -76.72 --sonde   (ne fait que dire ce qu'il y a là)
 *
 * CE QUI L'INTERDIT AU NAVIRE EST LE FOND, ET NON UNE RÈGLE. C'est tout le
 * sujet : on ne barre pas la route au joueur par un interdit, on lui met sous la
 * quille un platier où elle ne passe pas. Un canot en tire quarante centimètres,
 * un galion quatre mètres ; entre les deux il y a de la place pour une couronne
 * de corail à un mètre vingt, et c'est cette place qu'on peint ici.
 *
 * Le profil, en mètres de JEU depuis le centre :
 *
 *      +5 m  ___
 *           /   \___  plage
 *   0 m ---'        \________________ platier à −1,2 m ______
 *                                                            \___ tombant
 *  −22 m                                                          '-------
 *      0     30     45                      175              255
 *
 * Au-delà, il se fond dans le relief existant sur `feather` mètres : aucune
 * marche au bord du carré, et la grande image n'est pas touchée — ce qui veut
 * dire qu'une révision de la carte de la Jamaïque ne l'effacera pas.
 */
const fs = require('fs');
const path = require('path');
const { decodeGreyPng, encodeGreyPng } = require('./grey-png.js');

const root = path.join(__dirname, '..');
global.window = global;
eval(fs.readFileSync(path.join(root, 'js', 'world.js'), 'utf8'));

const key = process.argv[2] || 'ilot-cocotiers';
const LAT = Number(process.argv[3]);
const LON = Number(process.argv[4]);
const sonde = process.argv.includes('--sonde');
const SIDE = 1024, METRES = 1400;           // 1,37 m par pixel : le platier est net

if (!isFinite(LAT) || !isFinite(LON)) {
  console.error('usage : node tools/islet.js <clé> <lat> <lon> [--sonde]');
  process.exit(1);
}

const sheetPath = path.join(root, 'world', 'caraibes.json');
const region = JSON.parse(fs.readFileSync(sheetPath, 'utf8'));
const img = decodeGreyPng(fs.readFileSync(path.join(root, region.relief.image)));
const W = new Naval.World(region, img);

const centre = Naval.Geo.toXZ(LAT, LON);

/* ---- LA LOI DU GRIS, dans les deux sens (world/README.md) ---- */
const E = region.relief;
const maxH = E.maxHeight ?? 1500, maxD = E.maxDepth ?? 400;
const greyOf = h => h >= 0
  ? 128 + 127 * Math.sqrt(Math.min(1, h / maxH))
  : 128 - 128 * Math.sqrt(Math.min(1, -h / maxD));

/* ---- CE QU'IL Y A LÀ AUJOURD'HUI ---- */
const fondNow = W.heightAt(centre.x, centre.z);
console.log(`${key} : ${LAT}° N, ${LON}° O — x ${centre.x.toFixed(0)}, z ${centre.z.toFixed(0)} m de jeu`);
console.log(`  le fond y est aujourd'hui à ${fondNow.toFixed(1)} m`);
let alentour = -1e9;
for (let a = 0; a < 16; a++) {
  const an = a * Math.PI / 8;
  alentour = Math.max(alentour, W.heightAt(centre.x + Math.cos(an) * 700, centre.z + Math.sin(an) * 700));
}
console.log(`  et le plus haut dans les 700 m alentour : ${alentour.toFixed(1)} m`);
if (alentour > -12) {
  console.log('  ATTENTION : il y a de la terre ou un haut-fond tout près — un îlot ici se collerait à la côte.');
}
if (sonde) process.exit(0);
if (fondNow > -18) {
  console.error(`  REFUSÉ : ${fondNow.toFixed(1)} m de fond, il faut du large (−18 m au moins). Choisissez un autre point.`);
  process.exit(1);
}

/* ---- LE PROFIL ---- */
const SOMMET = 5.5;        // la bosse, en mètres de jeu
const R_TERRE = 30;        // le sable finit là
const R_PLAGE = 45;        // et le platier commence ici
const R_PLATIER = 175;     // il court jusque-là
const R_PIED = 255;        // puis le tombant
/* CE QUI INTERDIT LE NAVIRE ET LAISSE PASSER LA CHALOUPE, et c est un chiffre
   qui se calcule et non qui se choisit. Tirants d eau du jeu : chaloupe 0,44 m,
   vedette 1,00, chaland 2,30, Roter Löwe 3,85. À un mètre d eau la chaloupe a
   cinquante-six centimètres sous la quille et tout le reste touche — y compris
   la vedette, de justesse, ce qui est exactement la règle demandée. */
const PLATIER = -1.0;
const PIED = -22;          // où l'on peut mouiller

function profil(r) {
  if (r < R_TERRE) return SOMMET * (1 - (r / R_TERRE) * (r / R_TERRE) * 0.75);
  if (r < R_PLAGE) {
    const u = (r - R_TERRE) / (R_PLAGE - R_TERRE);
    return SOMMET * 0.25 * (1 - u) + PLATIER * u;       // la plage plonge
  }
  if (r < R_PLATIER) return PLATIER;
  if (r < R_PIED) {
    const u = (r - R_PLATIER) / (R_PIED - R_PLATIER);
    return PLATIER + (PIED - PLATIER) * u * u;          // le tombant s'accélère
  }
  return PIED;
}

/* ---- PEINDRE ---- */
const fix = Naval.Geo.fix(centre.x, centre.z);
const mPerLat = Naval.Geo.M_PER_MIN * 60 * region.scale;
const mPerLon = mPerLat * Math.cos(fix.lat * Math.PI / 180);
const dLat = (METRES / 2) / mPerLat, dLon = (METRES / 2) / mPerLon;
const frame = {
  key, image: `world/${key}-relief.png`,
  west: +(fix.lon - dLon).toFixed(6), east: +(fix.lon + dLon).toFixed(6),
  south: +(fix.lat - dLat).toFixed(6), north: +(fix.lat + dLat).toFixed(6),
  feather: 60,
};

const data = new Uint8Array(SIDE * SIDE);
for (let j = 0; j < SIDE; j++)
  for (let i = 0; i < SIDE; i++) {
    const lon = frame.west + (frame.east - frame.west) * (i + 0.5) / SIDE;
    const lat = frame.north - (frame.north - frame.south) * (j + 0.5) / SIDE;
    const p = Naval.Geo.toXZ(lat, lon);
    const r = Math.hypot(p.x - centre.x, p.z - centre.z);
    const ancien = W.heightAt(p.x, p.z);
    /* AU-DELÀ DU PIED, on rend la main au relief d'origine, en douceur : l'îlot
       ne doit pas creuser une cuvette dans le plateau qu'il occupe. Quatre cents
       mètres de raccord : l'îlot naît par cent mètres de fond, et un raccord court
       en ferait une muraille sous-marine plutôt qu'un pinacle. */
    const k = r <= R_PIED ? 1 : Math.max(0, 1 - (r - R_PIED) / 400);
    const h = profil(Math.min(r, R_PIED)) * k + ancien * (1 - k);
    data[j * SIDE + i] = Math.max(0, Math.min(255, Math.round(greyOf(h))));
  }

fs.writeFileSync(path.join(root, frame.image), encodeGreyPng(SIDE, SIDE, data));

/* ---- POSER L'ENTRÉE, EN L'AJOUTANT et non en remplaçant le tableau ---- */
let text = fs.readFileSync(sheetPath, 'utf8');
const entree = `    { "key": "${key}", "image": "${frame.image}",\n`
  + `      "west": ${frame.west}, "east": ${frame.east}, "south": ${frame.south}, "north": ${frame.north},\n`
  + `      "feather": ${frame.feather} }`;
if (text.includes(`"key": "${key}"`)) {
  // déjà là : on remplace SON entrée, pas le tableau
  const re = new RegExp(`    \\{ "key": "${key}"[\\s\\S]*?\\}`, '');
  text = text.replace(re, entree);
} else {
  const m = text.match(/\n {2}"patches": \[\n([\s\S]*?)\n {2}\],/);
  if (!m) { console.error('pas de tableau « patches » dans la fiche'); process.exit(1); }
  text = text.replace(m[0], `\n  "patches": [\n${m[1]},\n${entree}\n  ],`);
}
fs.writeFileSync(sheetPath, text);

console.log(`  écrit ${frame.image} — ${SIDE}×${SIDE} px pour ${METRES} m (${(METRES / SIDE).toFixed(2)} m/px)`);
console.log(`  profil : sommet +${SOMMET} m, plage jusqu'à ${R_PLAGE} m, platier ${PLATIER} m jusqu'à ${R_PLATIER} m,`);
console.log(`           tombant à ${PIED} m au pied (${R_PIED} m) — on mouille dehors, on finit à l'aviron`);
console.log(`  entrée « patches » ajoutée à world/caraibes.json`);
