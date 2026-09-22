/* LE PNG EN NIVEAUX DE GRIS, lu et écrit — une définition, plusieurs usagers :
 * le banc de parité (qui doit partir des mêmes octets que le C#), le relevé des
 * quêtes, la refonte du relief et les patches locaux.
 *
 * Node porte zlib, et c'est tout ce qui manquait : le reste est l'en-tête, les
 * IDAT concaténés, l'inflate et le défiltrage.
 */
const zlib = require('zlib');

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
    } else if (type === 'IDAT') idat.push(buf.subarray(at, at + len));
    else if (type === 'IEND') break;
    p = at + len + 4;
  }
  const bpp = color === 0 ? 1 : color === 2 ? 3 : color === 4 ? 2 : color === 6 ? 4 : 0;
  if (!bpp) throw new Error('PNG couleur ' + color + ' non gere');
  const raw = zlib.inflateSync(Buffer.concat(idat));
  const stride = w * bpp;
  const grey = new Uint8Array(w * h);
  let prev = Buffer.alloc(stride);
  for (let y = 0, q = 0; y < h; y++) {
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
    for (let x = 0; x < w; x++) grey[y * w + x] = cur[x * bpp];
    prev = cur;
  }
  return { w, h, data: grey };
}

const crcT = new Int32Array(256).map((_, n) => {
  let c = n; for (let k = 0; k < 8; k++) c = c & 1 ? 0xEDB88320 ^ (c >>> 1) : c >>> 1; return c;
});
const crc = b => { let c = -1; for (const x of b) c = crcT[(c ^ x) & 255] ^ (c >>> 8); return (c ^ -1) >>> 0; };
const chunk = (type, data) => {
  const len = Buffer.alloc(4); len.writeUInt32BE(data.length);
  const td = Buffer.concat([Buffer.from(type, 'ascii'), data]);
  const c = Buffer.alloc(4); c.writeUInt32BE(crc(td));
  return Buffer.concat([len, td, c]);
};

/** Un PNG gris 8 bits, sans filtre : w × h octets, une ligne après l'autre. */
function encodeGreyPng(w, h, bytes) {
  const ihdr = Buffer.alloc(13);
  ihdr.writeUInt32BE(w, 0); ihdr.writeUInt32BE(h, 4);
  ihdr[8] = 8; ihdr[9] = 0; ihdr[10] = 0; ihdr[11] = 0; ihdr[12] = 0;
  const raw = Buffer.alloc((w + 1) * h);
  const img = Buffer.from(bytes.buffer ? bytes.buffer : bytes, bytes.byteOffset || 0, w * h);
  for (let j = 0; j < h; j++) { raw[j * (w + 1)] = 0; img.copy(raw, j * (w + 1) + 1, j * w, (j + 1) * w); }
  return Buffer.concat([Buffer.from([137, 80, 78, 71, 13, 10, 26, 10]),
    chunk('IHDR', ihdr), chunk('IDAT', zlib.deflateSync(raw, { level: 9 })), chunk('IEND', Buffer.alloc(0))]);
}

module.exports = { decodeGreyPng, encodeGreyPng };
