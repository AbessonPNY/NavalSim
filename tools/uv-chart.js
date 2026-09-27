/* A UV reference chart for a sail.
 *
 *   node tools/uv-chart.js [carree|latine] [sortie.png] [taille]
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

/* WHICH CUT. The square sail is the default, so every old command line still
   does what it did. */
const CUT  = /^lat/i.test(process.argv[2] || '') ? 'latine' : 'carree';
const ARGS = /^(lat|car)/i.test(process.argv[2] || '') ? process.argv.slice(3) : process.argv.slice(2);
const OUT  = ARGS[0] || path.join(__dirname, '..', 'ships', 'textures',
                                 CUT === 'latine' ? 'uv-latine-repere.png' : 'uv-carree-repere.png');
const SIZE = Math.max(64, parseInt(ARGS[1], 10) || 512);

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
const LAT = CUT === 'latine';
const put = (x, y, r, g, b) => {
  if (x < 0 || y < 0 || x >= N || y >= N) return;
  const i = (y*N + x)*3;
  px[i] = r; px[i+1] = g; px[i+2] = b;
};

/* LA LATINE, ET ELLE EST À L'ENVERS DE CE QU'ON CROIT.
 *
 * Relevé sur le galion, dans le repère du bord : l'amure est BASSE et en AVANT
 * de l'antenne (y 6,6, z −4,5), le pic est HAUT et en arrière (y 14,2, z −9,2), et le point d'écoute tombe
 * entre les deux au pied du pic (y 8,3). La toile est donc un triangle dont la
 * verticale arrière est la chute, et dont l'antenne court de l'amure au pic.
 *
 * Ce que le maillage en fait, et c'est tout l'objet de cette planche : ses trois
 * coins sont donnés dans l'ordre amure, écoute, pic, et le quatrième MANQUE —
 * la grille referme alors le pic sur lui-même. Comme la texture porte 1 − v :
 *
 *   le HAUT de l'image est la BORDURE, de l'amure (gauche) à l'écoute (droite) ;
 *   le BAS de l'image est le PIC, écrasé sur un seul point ;
 *   la GAUCHE est l'antenne, où la toile est lacée ;
 *   la DROITE est la chute, qui pend libre.
 *
 * Autrement dit la voile est peinte LA TÊTE EN BAS, et le bas de l'image ne
 * couvre presque rien de tissu. Une carrée, elle, a sa têtière en haut de son
 * image : les deux planches ne se ressemblent pas, et c'est exactement pourquoi
 * il en fallait une seconde.
 */
if (LAT) {
  for (let y = 0; y < N; y++) {
    const v = y/(N-1);                      // 0 en haut = bordure, 1 en bas = pic
    /* CE QUE LE PIC ÉCRASE. À la rangée v, la largeur de tissu qui reste va
       comme (1 − v) : à mi-image il n'en reste que la moitié, et au bas plus
       rien. On l'écrit sur la planche plutôt que de laisser le peintre le
       découvrir en peignant. */
    const reste = 1 - v;
    for (let x = 0; x < N; x++) {
      const u = x/(N-1);
      /* LES LAIZES CONVERGENT AU PIC. Sur une voile triangulaire on couche les
         lés PARALLÈLEMENT À LA CHUTE, pour que le bord libre ne travaille pas
         dans le biais — et comme la chute est le bord u = 1, une laize est une
         bande de u constant. Elles se resserrent donc vers le bas de l'image,
         exactement comme le tissu qu'elles représentent. */
      const laize = Math.floor(u*8) % 2;
      let r = laize ? 232 : 222, g = laize ? 226 : 215, b = laize ? 208 : 196;
      // l'antenne, où la toile est lacée : ce qui s'y peint ne se voit pas
      if (u < 0.045) { r = 150; g = 132; b = 104; }
      // et le pic, où le tissu se referme : rien d'utile ne peut y tenir
      if (reste < 0.12) { const f = 0.45 + 0.55*(reste/0.12);
        r = (r*f)|0; g = (g*f*0.92)|0; b = (b*f*0.86)|0; }
      const near = t => Math.min(t*8 % 1, 1 - (t*8 % 1)) < 0.006*8;
      if (near(u) || near(v)) { r = (r*0.62)|0; g = (g*0.62)|0; b = (b*0.62)|0; }
      px[(y*N + x)*3] = r; px[(y*N + x)*3 + 1] = g; px[(y*N + x)*3 + 2] = b;
    }
  }
  /* LES DEUX GUIDES QUI CONVERGENT vers le pic : ils montrent, sans un mot, que
     deux traits parallèles dans l'image arrivent en éventail sur la toile. */
  for (let y = 0; y < N; y++) {
    const v = y/(N-1), reste = 1 - v;
    for (const t of [0.25, 0.75]) {
      const xm = Math.round((0.5 + (t - 0.5)*reste)*(N-1));
      for (let d = -1; d <= 1; d++) put(xm + d, y, 120, 96, 72);
    }
  }
}
else for (let y = 0; y < N; y++) {
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
   and a chart that needs a caption to be read is not doing its job. Pour la
   latine elle pointe vers la BORDURE, qui est en haut : c'est le seul bord de
   l'image qui soit du vrai tissu sur toute sa longueur. */
const cx = N/2, tip = N*0.30, base = N*0.62, halfW = N*0.115;
if (!LAT) for (let y = Math.floor(tip); y < base; y++) {
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
if (LAT) {
  console.log('  rouge = AMURE (basse, avant) · vert = ÉCOUTE (arrière)');
  console.log('  bleu et jaune = le PIC, écrasé sur un point : le bas de l\'image ne couvre presque rien');
  console.log('  bord gauche = l\'antenne, où la toile est lacée · bord droit = la chute');
  console.log('  la voile est peinte LA TÊTE EN BAS : sa bordure est en haut de l\'image');
} else {
  console.log('  rouge = têtière bâbord · vert = têtière tribord');
  console.log('  bleu  = point bâbord   · jaune = point tribord');
}
