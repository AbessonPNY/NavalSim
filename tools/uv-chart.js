/* A UV reference chart for a square sail.
 *
 *   node tools/uv-chart.js [sortie.png] [taille]
 *
 * Why this exists at all: a sail is not a rectangle. Her leeches are gored in
 * at mid height and her foot is cut UP in the middle, so a device painted in
 * the honest centre of a square image does not arrive in the honest centre of
 * the cloth — and there is no way to find that out except by painting one and
 * looking. This draws the mapping instead.
 *
 * It writes a PNG with no dependency on anything, which is the house rule:
 * zlib is in Node and a PNG is four chunks and a CRC.
 */

const fs = require('fs');
const path = require('path');
const zlib = require('zlib');

const OUT  = process.argv[2] || path.join(__dirname, '..', 'ships', 'textures', 'uv-carree-repere.png');
const SIZE = Math.max(64, parseInt(process.argv[3], 10) || 512);

/* ---------------------------------------------------------------- PNG ---- */
const CRC = (() => {
  const t = new Int32Array(256);
  for (let n = 0; n < 256; n++) {
    let c = n;
    for (let k = 0; k < 8; k++) c = c & 1 ? 0xEDB88320 ^ (c >>> 1) : c >>> 1;
    t[n] = c;
  }
  return buf => {
    let c = -1;
    for (let i = 0; i < buf.length; i++) c = t[(c ^ buf[i]) & 0xFF] ^ (c >>> 8);
    return (c ^ -1) >>> 0;
  };
})();

function chunk(type, data) {
  const len = Buffer.alloc(4);
  len.writeUInt32BE(data.length);
  const body = Buffer.concat([Buffer.from(type, 'ascii'), data]);
  const crc = Buffer.alloc(4);
  crc.writeUInt32BE(CRC(body));
  return Buffer.concat([len, body, crc]);
}

function writePNG(file, w, h, rgb) {
  const ihdr = Buffer.alloc(13);
  ihdr.writeUInt32BE(w, 0); ihdr.writeUInt32BE(h, 4);
  ihdr[8] = 8;    // bit depth
  ihdr[9] = 2;    // colour type: truecolour
  // 10..12: compression, filter, interlace — all zero
  const raw = Buffer.alloc(h * (1 + w*3));
  for (let y = 0; y < h; y++) {
    raw[y*(1 + w*3)] = 0;                        // filter: none
    rgb.copy(raw, y*(1 + w*3) + 1, y*w*3, (y+1)*w*3);
  }
  fs.mkdirSync(path.dirname(file), { recursive: true });
  fs.writeFileSync(file, Buffer.concat([
    Buffer.from([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]),
    chunk('IHDR', ihdr),
    chunk('IDAT', zlib.deflateSync(raw, { level: 9 })),
    chunk('IEND', Buffer.alloc(0))
  ]));
}

/* -------------------------------------------------------------- chart ---- */
const N = SIZE, px = Buffer.alloc(N*N*3);
const put = (x, y, r, g, b) => {
  if (x < 0 || y < 0 || x >= N || y >= N) return;
  const i = (y*N + x)*3;
  px[i] = r; px[i+1] = g; px[i+2] = b;
};

for (let y = 0; y < N; y++) {
  /* v as the SAIL sees it: 0 at the head, 1 at the foot. The image's own y
     already runs that way, which is the whole point of the 1-v in the
     geometry — paint the head at the top of the picture and it arrives at the
     top of the sail. */
  const v = y/(N-1);
  for (let x = 0; x < N; x++) {
    const u = x/(N-1);

    /* Vertical cloths, because that is how a square sail is actually made:
       widths of canvas sewn side by side up and down, never across. Eight of
       them, so the seams fall on the grid. */
    const cloth = Math.floor(u*8) % 2;
    let r = cloth ? 232 : 222, g = cloth ? 226 : 215, b = cloth ? 208 : 196;

    /* The tabling at the head, where she is bent to her yard: that strip is
       hidden against the spar, so nothing worth looking at should be put in
       it. Better to say so on the chart than to let it be discovered. */
    if (v < 0.045) { r = 150; g = 132; b = 104; }

    /* The eight-by-eight grid the cloth is actually built on. Anything finer
       than one of these squares is being drawn for a mesh that cannot hold
       it — the sail has nine rows of vertices and no more. */
    const near = t => Math.min(t*8 % 1, 1 - (t*8 % 1)) < 0.006*8;
    if (near(u) || near(v)) { r = (r*0.62)|0; g = (g*0.62)|0; b = (b*0.62)|0; }

    px[(y*N + x)*3]     = r;
    px[(y*N + x)*3 + 1] = g;
    px[(y*N + x)*3 + 2] = b;
  }
}

/* An arrow toward the head. One glance settles which way up the image goes,
   and a chart that needs a caption to be read is not doing its job. */
const cx = N/2, tip = N*0.30, base = N*0.62, halfW = N*0.115;
for (let y = Math.floor(tip); y < base; y++) {
  const f = (y - tip)/(base - tip);
  const wHere = halfW * (f < 0.55 ? f/0.55 : 0.42);
  for (let x = Math.floor(cx - wHere); x <= cx + wHere; x++) put(x, y, 60, 52, 44);
}

/* Corner marks, each its own colour: the ONE thing a UV chart must make
   impossible to get wrong is a mirrored or rotated image, and four different
   corners settle it at a glance where four identical ones do not. */
const marks = [[0, 0, 200, 46, 46], [1, 0, 60, 170, 70],
               [0, 1, 60, 110, 210], [1, 1, 220, 180, 40]];
const m = Math.round(N*0.075);
for (const [ux, uy, r, g, b] of marks)
  for (let dy = 0; dy < m; dy++) for (let dx = 0; dx < m; dx++)
    put(ux ? N-1-dx : dx, uy ? N-1-dy : dy, r, g, b);

writePNG(OUT, N, N, px);
console.log('wrote ' + path.relative(path.join(__dirname, '..'), OUT) +
            '  (' + N + 'x' + N + ', ' + (fs.statSync(OUT).size/1024).toFixed(1) + ' KB)');
console.log('  rouge = têtière bâbord · vert = têtière tribord');
console.log('  bleu  = point bâbord   · jaune = point tribord');
