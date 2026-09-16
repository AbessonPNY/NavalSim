/* Painting templates for the flag shapes, so a device can be drawn where it
 * will actually fly.
 *
 *   node tools/flag-template.js            # every shape
 *   node tools/flag-template.js streamer   # just one
 *
 * Writes ships/textures/flags/gabarits/gabarit-<forme>.png and .svg. The image
 * is the WHOLE texture: hoist (the mast side) on the left, fly on the right,
 * top edge at the top. What lies outside the outline is cut away in the game,
 * so paint the arms inside it; the full-height zone left of the blue line is
 * where they keep their proportions.
 *
 * The shapes are read out of js/ship-model.js (Naval.FLAG_SHAPES) rather than
 * written again here, so a template can never disagree with the flag. The cut
 * is the same one _flagAt lays on the grid: a taper narrows the depth about
 * the middle of the hoist, a notch pulls the fly edge back into a V.
 */
const fs = require('fs');
const path = require('path');
const zlib = require('zlib');

const ROOT = path.resolve(__dirname, '..');
const src = fs.readFileSync(path.join(ROOT, 'js', 'ship-model.js'), 'utf8');
const m = src.match(/Naval\.FLAG_SHAPES\s*=\s*(\{[\s\S]*?\n\});/);
if(!m){ console.error('Naval.FLAG_SHAPES introuvable dans js/ship-model.js'); process.exit(1); }
const SHAPES = Function('return ' + m[1])();

const OUT = path.join(ROOT, 'ships', 'textures', 'flags', 'gabarits');
fs.mkdirSync(OUT, { recursive:true });

const H = 512;                               // texture height; width follows the shape's length

// depth left at x (0..1 along the fly), as a fraction of the hoist
const widthAt = (s, x) => x <= s.taper ? 1 : 1 - (1 - s.tip)*(x - s.taper)/(1 - s.taper);
// inside the cut? y is 0..1 from the top edge
function inside(s, x, y){
  const w = widthAt(s, x), a = Math.abs(2*y - 1);
  if(a > w) return false;
  return x <= 1 - s.notch*(1 - a/w);
}
// the outline, as a closed polygon in 0..1 units: top edge out, fork, bottom edge back
function outline(s, n){
  const pts = [];
  // top edge, hoist to fly
  for(let i=0;i<=n;i++){ const x = i/n; pts.push([x, 0.5 - widthAt(s, x)/2]); }
  /* The fly edge. With t = a/w (1 on the edges, 0 on the centreline) the cut
     is x = 1 - notch·(1 - t): a straight V when notch > 0, the plain end when
     it is 0. Walked from the top edge in to the centre and out to the bottom. */
  const steps = 40;
  for(let k=steps;k>=-steps;k--){
    const t = Math.abs(k)/steps, x = 1 - s.notch*(1 - t);
    pts.push([x, 0.5 + Math.sign(-k || 1)*t*widthAt(s, x)/2]);
  }
  // bottom edge, fly back to hoist
  for(let i=n;i>=0;i--){ const x = i/n; pts.push([x, 0.5 + widthAt(s, x)/2]); }
  return pts;
}

/* ---- a PNG with nothing but zlib ---- */
const CRC = (() => {
  const t = new Uint32Array(256);
  for(let n=0;n<256;n++){ let c = n; for(let k=0;k<8;k++) c = c & 1 ? 0xedb88320 ^ (c >>> 1) : c >>> 1; t[n] = c >>> 0; }
  return t;
})();
function crc32(buf){ let c = 0xffffffff; for(const b of buf) c = CRC[(c ^ b) & 0xff] ^ (c >>> 8); return (c ^ 0xffffffff) >>> 0; }
function chunk(type, data){
  const len = Buffer.alloc(4); len.writeUInt32BE(data.length);
  const td = Buffer.concat([Buffer.from(type, 'ascii'), data]);
  const crc = Buffer.alloc(4); crc.writeUInt32BE(crc32(td));
  return Buffer.concat([len, td, crc]);
}
function png(w, h, rgba){
  const raw = Buffer.alloc((w*4 + 1)*h);
  for(let y=0;y<h;y++){ raw[y*(w*4+1)] = 0; rgba.copy(raw, y*(w*4+1) + 1, y*w*4, (y+1)*w*4); }
  const ihdr = Buffer.alloc(13);
  ihdr.writeUInt32BE(w, 0); ihdr.writeUInt32BE(h, 4);
  ihdr[8] = 8; ihdr[9] = 6; ihdr[10] = 0; ihdr[11] = 0; ihdr[12] = 0;
  return Buffer.concat([Buffer.from([137,80,78,71,13,10,26,10]),
    chunk('IHDR', ihdr), chunk('IDAT', zlib.deflateSync(raw, { level:9 })), chunk('IEND', Buffer.alloc(0))]);
}

function make(name){
  const s = SHAPES[name];
  const W = Math.round(H*s.length);
  const px = Buffer.alloc(W*H*4);
  const set = (x, y, r, g, b, a) => { const i = (y*W + x)*4; px[i]=r; px[i+1]=g; px[i+2]=b; px[i+3]=a; };
  const ins = (x, y) => x >= 0 && y >= 0 && x < W && y < H && inside(s, (x + 0.5)/W, (y + 0.5)/H);
  const fullTo = Math.round(s.taper*W);
  for(let y=0;y<H;y++) for(let x=0;x<W;x++){
    if(ins(x, y)){
      // the edge, two pixels wide
      const edge = !ins(x-2, y) || !ins(x+2, y) || !ins(x, y-2) || !ins(x, y+2);
      if(edge) set(x, y, 200, 30, 40, 255);
      else if(s.taper > 0 && Math.abs(x - fullTo) < 2 && (y >> 4) % 2 === 0) set(x, y, 40, 110, 220, 255);
      else if(Math.abs(y - H/2) < 1 && (x >> 4) % 2 === 0) set(x, y, 190, 190, 190, 255);
      else set(x, y, 255, 255, 255, 255);
    }else{
      // cut away: a faint checker, transparent enough to sit under a layer
      const c = ((x >> 5) + (y >> 5)) % 2 ? 205 : 180;
      set(x, y, c, c, c, 90);
    }
  }
  // the hoist, where it is bent to the mast
  for(let y=0;y<H;y++) for(let x=0;x<3;x++) set(x, y, 90, 60, 30, 255);
  fs.writeFileSync(path.join(OUT, 'gabarit-' + name + '.png'), png(W, H, px));

  const pts = outline(s, 200).map(([x, y]) => (x*W).toFixed(1) + ',' + (y*H).toFixed(1)).join(' ');
  const svg = `<svg xmlns="http://www.w3.org/2000/svg" width="${W}" height="${H}" viewBox="0 0 ${W} ${H}">
  <title>Gabarit de pavillon — ${name}</title>
  <rect width="${W}" height="${H}" fill="#bbb" fill-opacity="0.35"/>
  <polygon points="${pts}" fill="#fff" stroke="#c81e28" stroke-width="3"/>
  <line x1="1.5" y1="0" x2="1.5" y2="${H}" stroke="#5a3c1e" stroke-width="3"/>
  ${s.taper > 0 ? `<line x1="${fullTo}" y1="0" x2="${fullTo}" y2="${H}" stroke="#286edc" stroke-width="2" stroke-dasharray="16 16"/>
  <text x="${Math.max(12, fullTo/2)}" y="${H - 16}" font-family="sans-serif" font-size="22" fill="#286edc" text-anchor="middle">pleine hauteur : armoiries ici</text>` : ''}
  <line x1="0" y1="${H/2}" x2="${W}" y2="${H/2}" stroke="#bbb" stroke-dasharray="16 16"/>
  <text x="12" y="30" font-family="sans-serif" font-size="22" fill="#5a3c1e">← guindant (mât) · haut du pavillon</text>
  <text x="${W - 12}" y="30" font-family="sans-serif" font-size="22" fill="#c81e28" text-anchor="end">battant →</text>
  <text x="12" y="${H/2 - 10}" font-family="sans-serif" font-size="16" fill="#888">${name} · ${W}×${H} px · hors du contour : coupé</text>
</svg>
`;
  fs.writeFileSync(path.join(OUT, 'gabarit-' + name + '.svg'), svg);
  console.log('wrote ships/textures/flags/gabarits/gabarit-' + name + '.png / .svg  (' + W + '×' + H + ')');
}

const only = process.argv[2];
if(only && !SHAPES[only]){ console.error('forme inconnue : ' + only + ' (' + Object.keys(SHAPES).join(', ') + ')'); process.exit(1); }
for(const name of only ? [only] : Object.keys(SHAPES)) make(name);
