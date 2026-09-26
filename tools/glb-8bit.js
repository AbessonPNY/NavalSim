/*
  LES TEXTURES 16 BITS D'UN .glb, RAMENÉES À HUIT.

  Blender réexporte une image telle qu'elle est entrée, et une carte de normales
  sortie d'un banc de textures arrive volontiers en 16 bits par canal, avec un
  alpha vide : 4 à 6 Mo pour du 1024×1024, là où huit bits en donnent un demi.
  Une normale ni une rugosité n'ont jamais demandé cette précision — c'est de la
  place perdue dans le dépôt, dans la mémoire de la carte, et dans la page
  publiée, qui a une limite de 16 Mo et la franchit sans prévenir.

  Ce que fait l'outil : il relit chaque PNG du fichier, et tout ce qui est en
  16 bits redescend à 8 (couleur en RVB, gris en gris), l'alpha tombant s'il ne
  sert à rien. Le reste du .glb n'est pas touché — mêmes maillages, mêmes
  matières, mêmes noms. À passer après chaque export Blender.

      node tools/glb-8bit.js ships/models/roter_lowe_1597.glb
*/
'use strict';
const fs = require('fs');
const zlib = require('zlib');

/* ------------------------------------------------------------------ PNG */

function readPng(png) {
  let p = 8, w = 0, h = 0, bits = 0, color = 0, inter = 0;
  const idat = [];
  while (p + 8 <= png.length) {
    const len = png.readUInt32BE(p), type = png.toString('ascii', p + 4, p + 8), at = p + 8;
    if (type === 'IHDR') {
      w = png.readUInt32BE(at); h = png.readUInt32BE(at + 4);
      bits = png[at + 8]; color = png[at + 9]; inter = png[at + 12];
    } else if (type === 'IDAT') idat.push(png.subarray(at, at + len));
    else if (type === 'IEND') break;
    p = at + len + 4;
  }
  return { w, h, bits, color, inter, data: Buffer.concat(idat) };
}

/** Les lignes défiltrées, telles quelles (donc encore en `bits` bits). */
function unfilter(im) {
  const chans = { 0: 1, 2: 3, 3: 1, 4: 2, 6: 4 }[im.color];
  const bpp = chans * (im.bits / 8);
  const stride = im.w * bpp;
  const raw = zlib.inflateSync(im.data);
  const out = Buffer.alloc(stride * im.h);
  let prev = Buffer.alloc(stride);
  for (let y = 0, q = 0; y < im.h; y++) {
    const f = raw[q++];
    const cur = Buffer.from(raw.subarray(q, q + stride)); q += stride;
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
    cur.copy(out, y * stride);
    prev = cur;
  }
  return { px: out, chans, bpp, stride };
}

const crcTable = new Int32Array(256).map((_, n) => {
  let c = n;
  for (let k = 0; k < 8; k++) c = c & 1 ? 0xEDB88320 ^ (c >>> 1) : c >>> 1;
  return c;
});
const crc = x => { let c = -1; for (const v of x) c = crcTable[(c ^ v) & 255] ^ (c >>> 8); return (c ^ -1) >>> 0; };

function chunk(type, data) {
  const len = Buffer.alloc(4); len.writeUInt32BE(data.length);
  const td = Buffer.concat([Buffer.from(type, 'ascii'), data]);
  const c = Buffer.alloc(4); c.writeUInt32BE(crc(td));
  return Buffer.concat([len, td, c]);
}

function writePng(w, h, chans, px) {
  const ihdr = Buffer.alloc(13);
  ihdr.writeUInt32BE(w, 0); ihdr.writeUInt32BE(h, 4);
  ihdr[8] = 8; ihdr[9] = chans === 1 ? 0 : chans === 3 ? 2 : chans === 2 ? 4 : 6;
  const stride = w * chans;
  const raw = Buffer.alloc((stride + 1) * h);
  for (let y = 0; y < h; y++) {
    raw[y * (stride + 1)] = 0;                       // sans filtre : le zlib fait le reste
    px.copy(raw, y * (stride + 1) + 1, y * stride, (y + 1) * stride);
  }
  return Buffer.concat([Buffer.from([137, 80, 78, 71, 13, 10, 26, 10]),
    chunk('IHDR', ihdr), chunk('IDAT', zlib.deflateSync(raw, { level: 9 })), chunk('IEND', Buffer.alloc(0))]);
}

/** Moitié par moitié, moyenne de quatre : une carte de normales s'y prête. */
function halve(px, w, h, chans) {
  const W = w >> 1, H = h >> 1;
  const out = Buffer.alloc(W * H * chans);
  for (let y = 0; y < H; y++)
    for (let x = 0; x < W; x++)
      for (let k = 0; k < chans; k++)
        out[(y * W + x) * chans + k] = (
          px[((2 * y) * w + 2 * x) * chans + k] + px[((2 * y) * w + 2 * x + 1) * chans + k] +
          px[((2 * y + 1) * w + 2 * x) * chans + k] + px[((2 * y + 1) * w + 2 * x + 1) * chans + k] + 2) >> 2;
  return { px: out, w: W, h: H };
}

/**
 * Huit bits, et pas un canal de trop. Rend null si l'image est déjà comme il
 * faut — on ne réencode pas pour rien, au risque de perdre autre chose.
 * <c>max</c> borne le côté : une carte trop fine pour ce qu'elle porte se
 * réduit de moitié en moitié.
 */
function shrink(png, max) {
  const im = readPng(png);
  const trop = max && Math.max(im.w, im.h) > max;
  if (im.bits !== 16 && !trop) return null;
  if (im.inter) throw new Error('PNG entrelacé : non traité');
  if (im.color === 3) return null;                       // à palette : on n'y touche pas
  const { px, chans, bpp, stride } = unfilter(im);
  const step = im.bits / 8;                              // l'octet de poids fort, s'il y en a deux

  // un alpha plein ne sert à rien : on le laisse tomber
  let keep = chans;
  if (chans === 4 || chans === 2) {
    let plein = true;
    for (let y = 0; y < im.h && plein; y++)
      for (let x = 0; x < im.w; x++)
        if (px[y * stride + x * bpp + (chans - 1) * step] !== 255) { plein = false; break; }
    if (plein) keep = chans - 1;
  }

  let out = Buffer.alloc(im.w * im.h * keep);
  for (let y = 0; y < im.h; y++)
    for (let x = 0; x < im.w; x++)
      for (let k = 0; k < keep; k++)
        out[(y * im.w + x) * keep + k] = px[y * stride + x * bpp + k * step];
  let w = im.w, h = im.h;
  while (max && Math.max(w, h) > max && w > 1 && h > 1 && (w & 1) === 0 && (h & 1) === 0) {
    const r = halve(out, w, h, keep);
    out = r.px; w = r.w; h = r.h;
  }
  return { png: writePng(w, h, keep, out), w: im.w, h: im.h, W: w, H: h, bits: im.bits, chans, keep };
}

/* ------------------------------------------------------------------ glb */

function run(path, bornes) {
  const b = fs.readFileSync(path);
  if (b.toString('ascii', 0, 4) !== 'glTF') throw new Error('pas un .glb : ' + path);
  const jlen = b.readUInt32LE(12);
  const j = JSON.parse(b.slice(20, 20 + jlen));
  const binLen = b.readUInt32LE(20 + jlen);
  const bin = b.slice(20 + jlen + 8, 20 + jlen + 8 + binLen);

  const neuf = new Map();                       // bufferView -> PNG réencodé
  let gagne = 0;
  for (const im of j.images || []) {
    if (im.mimeType !== 'image/png' || im.bufferView == null) continue;
    const v = j.bufferViews[im.bufferView];
    const png = bin.slice(v.byteOffset || 0, (v.byteOffset || 0) + v.byteLength);
    const nom = im.name || '(sans nom)';
    let max = 0;
    for (const [motif, px] of bornes) if (motif === '*' || nom.includes(motif)) max = px;
    const s = shrink(png, max);
    if (!s) continue;
    neuf.set(im.bufferView, s.png);
    gagne += png.length - s.png.length;
    const taille = s.W === s.w ? `${s.w}×${s.h}` : `${s.w}×${s.h} → ${s.W}×${s.H}`;
    console.log(`  ${nom} : ${taille}, ${s.bits} bits ${s.chans} canaux `
      + `${(png.length / 1048576).toFixed(2)} Mo → 8 bits ${s.keep} canaux ${(s.png.length / 1048576).toFixed(2)} Mo`);
  }
  if (neuf.size === 0) { console.log(`${path} : rien à faire`); return; }

  const parts = []; let off = 0;
  j.bufferViews.forEach((v, i) => {
    const data = neuf.get(i) || bin.slice(v.byteOffset || 0, (v.byteOffset || 0) + v.byteLength);
    const pad = (4 - off % 4) % 4;
    if (pad) { parts.push(Buffer.alloc(pad)); off += pad; }
    v.byteOffset = off; v.byteLength = data.length;
    parts.push(data); off += data.length;
  });
  const pad = (4 - off % 4) % 4;
  if (pad) { parts.push(Buffer.alloc(pad)); off += pad; }
  j.buffers[0].byteLength = off;

  let js = Buffer.from(JSON.stringify(j));
  js = Buffer.concat([js, Buffer.alloc((4 - js.length % 4) % 4, 0x20)]);
  const binB = Buffer.concat(parts);
  const head = Buffer.alloc(12);
  head.write('glTF', 0); head.writeUInt32LE(2, 4);
  head.writeUInt32LE(12 + 8 + js.length + 8 + binB.length, 8);
  const c1 = Buffer.alloc(8); c1.writeUInt32LE(js.length, 0); c1.write('JSON', 4);
  const c2 = Buffer.alloc(8); c2.writeUInt32LE(binB.length, 0); c2.writeUInt32LE(0x004E4942, 4);
  fs.writeFileSync(path, Buffer.concat([head, c1, js, c2, binB]));
  console.log(`${path} : ${(b.length / 1048576).toFixed(2)} Mo → `
    + `${((12 + 8 + js.length + 8 + binB.length) / 1048576).toFixed(2)} Mo `
    + `(${(gagne / 1048576).toFixed(2)} Mo de moins)`);
}

/* --max borne le côté d'une image, toutes ou celles dont le nom porte un motif :
   --max 512 pour tout le monde, --max fabrics=512 pour les seules étoffes. Une
   carte de normales de cordage n'a pas besoin de 1024 pixels ; un bordé, si. */
const args = process.argv.slice(2);
const files = [], bornes = [];
for (let i = 0; i < args.length; i++) {
  if (args[i] === '--max') {
    const v = args[++i] || '';
    const eq = v.lastIndexOf('=');
    bornes.push(eq < 0 ? ['*', parseInt(v, 10)] : [v.slice(0, eq), parseInt(v.slice(eq + 1), 10)]);
  } else files.push(args[i]);
}
if (files.length === 0) {
  console.error('usage : node tools/glb-8bit.js <fichier.glb> [...] [--max [motif=]px]');
  process.exit(1);
}
for (const f of files) run(f, bornes);
