/*
  OÙ UN SON COMMENCE VRAIMENT, dans un fichier Ogg Vorbis.

  Un échantillon qui « arrive en retard » a trois causes possibles : le jeu le
  déclenche trop tard, le moteur le met en file, ou LE FICHIER COMMENCE PAR DU
  SILENCE. Les deux premières se corrigent dans le code et se mesurent ; la
  troisième est dans le fichier, et on ne la voit pas sans l'ouvrir.

  Cet outil la mesure sans décoder. Vorbis code le silence par des paquets
  MINUSCULES — quelques octets là où un son plein en prend des centaines —, et
  chaque page Ogg porte sa position en échantillons (granulepos). Il suffit donc
  de descendre les pages jusqu'à la première dont les paquets cessent d'être
  ridicules, et de lire où elle en est.

      node tools/ogg-attaque.js medias/sound/water_splash_to_underwater.ogg
*/
'use strict';
const fs = require('fs');

function pages(buf) {
  const out = [];
  let p = 0;
  while (p + 27 <= buf.length) {
    if (buf.toString('ascii', p, p + 4) !== 'OggS') { p++; continue; }
    const granule = buf.readBigInt64LE(p + 6);
    const segs = buf[p + 26];
    const table = buf.subarray(p + 27, p + 27 + segs);
    let body = p + 27 + segs, total = 0;
    const packets = [];
    let cur = 0;
    for (const n of table) {
      cur += n; total += n;
      if (n < 255) { packets.push(cur); cur = 0; }
    }
    out.push({ granule, packets, at: p });
    p = body + total;
  }
  return out;
}

function run(path) {
  const buf = fs.readFileSync(path);
  const pg = pages(buf);
  if (pg.length === 0) { console.error('pas de page Ogg : ' + path); return; }

  // la fréquence, dans l'en-tête d'identification (paquet 1, après « \x01vorbis »)
  const id = buf.indexOf(Buffer.from([0x01, 0x76, 0x6f, 0x72, 0x62, 0x69, 0x73]));
  const rate = id >= 0 ? buf.readUInt32LE(id + 12) : 44100;
  const canaux = id >= 0 ? buf[id + 11] : 2;

  /* LE SEUIL : un paquet de moins de quarante octets ne porte pas de son. C'est
     grossier et c'est suffisant — entre du silence (une poignée d'octets) et une
     attaque (plusieurs centaines), il n'y a pas d'ambiguïté. */
  const MUET = 40;
  let debut = null, dernier = 0n;
  for (const page of pg) {
    // les trois premières pages portent les en-têtes : pas du son
    if (page.granule < 0n) continue;
    const gros = page.packets.filter(n => n >= MUET).length;
    if (gros > 0 && debut === null) { debut = dernier; break; }
    dernier = page.granule;
  }
  const fin = pg[pg.length - 1].granule;
  const t = Number(debut ?? 0n) / rate;

  console.log(`\n${path}`);
  console.log(`  ${rate} Hz, ${canaux} canal/canaux, ${(Number(fin) / rate).toFixed(2)} s, ${(buf.length / 1024).toFixed(0)} Ko`);
  console.log(`  silence de tête : ${(t * 1000).toFixed(0)} ms`
    + (t > 0.05 ? '  ← à rogner : c\'est autant de retard à l\'oreille' : '  (rien à rogner)'));
}

const args = process.argv.slice(2);
if (args.length === 0) { console.error('usage : node tools/ogg-attaque.js <fichier.ogg> [...]'); process.exit(1); }
for (const a of args) run(a);
