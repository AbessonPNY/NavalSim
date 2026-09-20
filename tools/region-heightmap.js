/* The starting relief of a region, written as a greyscale PNG to be painted on.
 *
 *   node tools/region-heightmap.js                 # world/caraibes.json
 *   node tools/region-heightmap.js world/autre.json
 *
 * Reads the region sheet (bounds, scale, how grey maps to height), the real
 * coastlines in world/sources/natural-earth-caraibes.json (Natural Earth,
 * public domain) and the mountain ranges in world/sources/reliefs.json, and
 * writes the image the region sheet names.
 *
 * GREY IS HEIGHT, and it is meant to be painted: mid-grey (region.relief.sea,
 * 128) is the shore line, lighter is land, darker is sea. The scale is not
 * linear — height goes as the square of the distance from mid-grey
 * (relief.curve) — so the few metres that decide whether a ship grounds get
 * many shades and the summits few. The game reads it back through the same
 * formula (Naval.World.decode).
 *
 * Running this again OVERWRITES the image, hand retouches included.
 */
const fs = require('fs');
const path = require('path');
const zlib = require('zlib');

const regionFile = process.argv[2] || 'world/caraibes.json';
const R = JSON.parse(fs.readFileSync(regionFile, 'utf8'));
const E = R.relief;
const coast = JSON.parse(fs.readFileSync('world/sources/natural-earth-caraibes.json', 'utf8'));
const reliefs = JSON.parse(fs.readFileSync('world/sources/reliefs.json', 'utf8'));

/* ---- the grid: square pixels in game metres at the middle latitude ---- */
const M_PER_DEG = 1852*60;                         // Naval.Geo: a minute is a nautical mile
const S = R.scale, V = R.vertical;
const latC = (E.north + E.south)/2, cosC = Math.cos(latC*Math.PI/180);
const H = E.height || 2912;
const W = Math.round(H*(E.east - E.west)*cosC/(E.north - E.south)/16)*16;
const pxZ = (E.north - E.south)*M_PER_DEG*S/H;     // game metres per pixel, north-south
const pxX = (E.east - E.west)*M_PER_DEG*cosC*S/W;
const px = (pxX + pxZ)/2;
console.log('grille ' + W + ' × ' + H + ' px, ' + px.toFixed(1) + ' m de jeu par pixel ('
            + (px/S/1000).toFixed(2) + ' km réels)');

const lonOf = i => E.west + (i + 0.5)/W*(E.east - E.west);
const latOf = j => E.north - (j + 0.5)/H*(E.north - E.south);
// game metres, locally, from a lat/lon (east is -x, north +z, as in the game)
const gx = (lon, lat) => -(lon - E.west)*M_PER_DEG*Math.cos(lat*Math.PI/180)*S;
const gz = lat => (lat - E.south)*M_PER_DEG*S;

/* ---- the land: every ring filled even-odd, row by row ---- */
const land = new Uint8Array(W*H);
// ce que les bandes imposent en plus de la plaine, en mètres
const lift = new Float32Array(W*H);
{
  const edges = [];
  for(const r of coast.rings)
    for(let k = 0; k < r.length; k++){
      const a = r[k], b = r[(k + 1) % r.length];
      if(a[1] !== b[1]) edges.push(a, b);
    }
  const xs = [];
  for(let j = 0; j < H; j++){
    const lat = latOf(j);
    xs.length = 0;
    for(let e = 0; e < edges.length; e += 2){
      const a = edges[e], b = edges[e + 1];
      if((a[1] > lat) === (b[1] > lat)) continue;
      const lon = a[0] + (b[0] - a[0])*(lat - a[1])/(b[1] - a[1]);
      xs.push((lon - E.west)/(E.east - E.west)*W - 0.5);
    }
    xs.sort((p, q) => p - q);
    for(let k = 0; k + 1 < xs.length; k += 2){
      const i0 = Math.max(0, Math.ceil(xs[k])), i1 = Math.min(W - 1, Math.floor(xs[k + 1]));
      for(let i = i0; i <= i1; i++) land[j*W + i] = 1;
    }
  }
  /* Narrow strips of land the coastline data draws too thin to survive a pixel
     (the Palisadoes, a sand spit a few hundred metres wide): laid down with a
     width of at least TWO pixels and a bit, and with a height of their own.

     Both numbers are the price of a 450-metre pixel. The sampler reads the
     picture BILINEARLY, so a one-pixel strip between two pixels of open sea is
     averaged away to almost nothing: the ground under Port-Royal measured one
     metre, and its houses looked afloat. Widening the strip keeps a core the
     blur cannot reach, and `height` in reliefs.json lifts that core clear of
     the water. A real sand spit is two or three metres; here it must be drawn
     taller to READ as two or three, which is the honest way round. */
  for(const st of reliefs.strips || []){
    const P = st.line.map(([lat, lon]) => [(lon - E.west)/(E.east - E.west)*W - 0.5, (E.north - lat)/(E.north - E.south)*H - 0.5]);
    const hw = Math.max(1.15, st.width/px/2);
    for(let s = 0; s + 1 < P.length; s++){
      const [ax, ay] = P[s], [bx, by] = P[s + 1];
      for(let j = Math.floor(Math.min(ay, by) - hw - 1); j <= Math.ceil(Math.max(ay, by) + hw + 1); j++)
        for(let i = Math.floor(Math.min(ax, bx) - hw - 1); i <= Math.ceil(Math.max(ax, bx) + hw + 1); i++){
          if(i < 0 || j < 0 || i >= W || j >= H) continue;
          const dx = bx - ax, dy = by - ay;
          const u = Math.max(0, Math.min(1, ((i - ax)*dx + (j - ay)*dy)/(dx*dx + dy*dy || 1)));
          if(Math.hypot(i - ax - u*dx, j - ay - u*dy) > hw) continue;
          land[j*W + i] = 1;
          // sa hauteur à elle, si elle en demande une : le plus haut l'emporte
          if(st.height) lift[j*W + i] = Math.max(lift[j*W + i], st.height);
        }
    }
  }
  // the small islands the coastline data leaves out
  for(const t of reliefs.islets || []){
    const ci = (t.lon - E.west)/(E.east - E.west)*W - 0.5, cj = (E.north - t.lat)/(E.north - E.south)*H - 0.5;
    const rp = Math.max(0.75, t.radius/px);
    for(let j = Math.floor(cj - rp); j <= Math.ceil(cj + rp); j++)
      for(let i = Math.floor(ci - rp); i <= Math.ceil(ci + rp); i++)
        if(i >= 0 && j >= 0 && i < W && j < H && Math.hypot(i - ci, j - cj) <= rp) land[j*W + i] = 1;
  }
}

/* ---- distance to the other side of the shore, exact (Felzenszwalb) ---- */
function edt(target){                 // squared pixel distance to the nearest target pixel
  const INF = 1e20, n = Math.max(W, H);
  const f = new Float64Array(n), d = new Float64Array(n), z = new Float64Array(n + 1), v = new Int32Array(n);
  const out = new Float64Array(W*H);
  for(let k = 0; k < W*H; k++) out[k] = target[k] ? 0 : INF;
  const pass = (len, get, set) => {
    for(let q = 0; q < len; q++) f[q] = get(q);
    let k = 0; v[0] = 0; z[0] = -INF; z[1] = INF;
    for(let q = 1; q < len; q++){
      let s = ((f[q] + q*q) - (f[v[k]] + v[k]*v[k]))/(2*q - 2*v[k]);
      while(s <= z[k]){ k--; s = ((f[q] + q*q) - (f[v[k]] + v[k]*v[k]))/(2*q - 2*v[k]); }
      k++; v[k] = q; z[k] = s; z[k + 1] = INF;
    }
    k = 0;
    for(let q = 0; q < len; q++){
      while(z[k + 1] < q) k++;
      d[q] = (q - v[k])*(q - v[k]) + f[v[k]];
    }
    for(let q = 0; q < len; q++) set(q, d[q]);
  };
  for(let i = 0; i < W; i++) pass(H, q => out[q*W + i], (q, x) => { out[q*W + i] = x; });
  for(let j = 0; j < H; j++) pass(W, q => out[j*W + q], (q, x) => { out[j*W + q] = x; });
  return out;
}
const sea = new Uint8Array(W*H);
for(let k = 0; k < W*H; k++) sea[k] = land[k] ? 0 : 1;
const toSea = edt(sea), toLand = edt(land);

/* ---- a little noise, so the hills are not a smooth blanket ---- */
const hash = (i, j) => { let h = (i*374761393 + j*668265263) | 0; h = (h ^ (h >>> 13))*1274126177 | 0; return ((h ^ (h >>> 16)) >>> 0)/4294967296; };
const vnoise = (x, y) => {
  const i = Math.floor(x), j = Math.floor(y), fx = x - i, fy = y - j;
  const sx = fx*fx*(3 - 2*fx), sy = fy*fy*(3 - 2*fy);
  const a = hash(i, j), b = hash(i + 1, j), c = hash(i, j + 1), d = hash(i + 1, j + 1);
  return a + (b - a)*sx + (c - a)*sy + (a - b - c + d)*sx*sy;
};
const fbm = (x, y) => { let s = 0, a = 0.5, f = 1; for(let o = 0; o < 5; o++){ s += a*vnoise(x*f, y*f); a *= 0.5; f *= 2.03; } return s; };

/* ---- the ranges, as segments in game metres ---- */
const ridges = reliefs.ridges.map(r => {
  const pts = r.line.map(([lat, lon]) => [gx(lon, lat), gz(lat)]);
  const w = r.width*1000*S, h = r.height*V;
  let x0 = Infinity, x1 = -Infinity, z0 = Infinity, z1 = -Infinity;
  for(const [x, z] of pts){ x0 = Math.min(x0, x); x1 = Math.max(x1, x); z0 = Math.min(z0, z); z1 = Math.max(z1, z); }
  const m = 2.6*w;
  return { pts, w, h, box:[x0 - m, x1 + m, z0 - m, z1 + m] };
});
const segDist = (x, z, a, b) => {
  const dx = b[0] - a[0], dz = b[1] - a[1];
  const t = Math.max(0, Math.min(1, ((x - a[0])*dx + (z - a[1])*dz)/(dx*dx + dz*dz || 1)));
  return Math.hypot(x - a[0] - t*dx, z - a[1] - t*dz);
};

/* ---- height, then grey ---- */
const encode = h => {
  if(h >= 0) return Math.min(255, Math.round(E.sea + (255 - E.sea)*Math.pow(h/E.maxHeight, 1/E.curve)));
  return Math.max(0, Math.round(E.sea - E.sea*Math.pow(Math.min(1, -h/E.maxDepth), 1/E.curve)));
};
const ss = (a, b, x) => { const t = Math.max(0, Math.min(1, (x - a)/(b - a))); return t*t*(3 - 2*t); };
const img = Buffer.alloc(W*H);
for(let j = 0; j < H; j++){
  const lat = latOf(j), z = gz(lat);
  for(let i = 0; i < W; i++){
    const k = j*W + i;
    let h;
    if(land[k]){
      // metres inland from the shore line, which runs half a pixel out
      const d = Math.sqrt(toSea[k])*px - px/2;
      const x = gx(lonOf(i), lat);
      // a coastal plain, then hills, and the ranges over them
      let r = 0;
      for(const g of ridges){
        if(x < g.box[0] || x > g.box[1] || z < g.box[2] || z > g.box[3]) continue;
        let m = Infinity;
        for(let s = 0; s + 1 < g.pts.length; s++) m = Math.min(m, segDist(x, z, g.pts[s], g.pts[s + 1]));
        r = Math.max(r, g.h*Math.exp(-(m/g.w)*(m/g.w))*(0.78 + 0.44*fbm(x/900, z/900)));
      }
      /* LA BERGE. Elle valait 2,5 m au trait de cote, et vue d en haut la terre
         ne se distinguait pas d un haut-fond : pas d ombre, pas de talus, la
         meme teinte que l eau par-dessus un fond clair. Six metres au bord et
         vingt a sept cents, c est une COTE -- le relief se lit, et la limite de
         l eau devient une ligne et non un degrade. Elle reste sous la garde du
         talus sous-marin, qui plonge a douze metres en quarante : la plage tient
         donc en un pixel, ce qui est deja tout ce que l image peut porter. */
      const plain = 6 + 14*ss(0, 700, d);
      const hills = 45*fbm(x/1400 + 17, z/1400 - 5)*ss(150, 2500, d);
      h = plain + Math.max(hills, r*ss(0, 500, d));
      h = Math.max(6, h, lift[k]);     // above a pixel's blur, or thin land sinks
    }else{
      /* Out from the shore: a steep first forty metres to twelve — so the
         narrow harbours of a reduced map still float a ship — then the shelf,
         then the deep. Depths are NOT scaled: a keel is a keel. */
      const d = Math.sqrt(toLand[k])*px - px/2;
      if(d < 40) h = -12*ss(-20, 40, d) - 0.3;
      else if(d < 400) h = -12 - 58*Math.pow((d - 40)/360, 1.5);
      else h = -70 - 330*(1 - Math.exp(-(d - 400)/3000));
    }
    img[k] = encode(h);
  }
}

/* ---- PNG, greyscale 8 bits ---- */
const crcT = new Int32Array(256).map((_, n) => { let c = n; for(let k = 0; k < 8; k++) c = c & 1 ? 0xEDB88320 ^ (c >>> 1) : c >>> 1; return c; });
const crc = b => { let c = -1; for(const x of b) c = crcT[(c ^ x) & 255] ^ (c >>> 8); return (c ^ -1) >>> 0; };
const chunk = (type, data) => {
  const len = Buffer.alloc(4); len.writeUInt32BE(data.length);
  const td = Buffer.concat([Buffer.from(type, 'ascii'), data]);
  const c = Buffer.alloc(4); c.writeUInt32BE(crc(td));
  return Buffer.concat([len, td, c]);
};
const ihdr = Buffer.alloc(13);
ihdr.writeUInt32BE(W, 0); ihdr.writeUInt32BE(H, 4);
ihdr[8] = 8; ihdr[9] = 0; ihdr[10] = 0; ihdr[11] = 0; ihdr[12] = 0;
const raw = Buffer.alloc((W + 1)*H);
for(let j = 0; j < H; j++){ raw[j*(W + 1)] = 0; img.copy(raw, j*(W + 1) + 1, j*W, (j + 1)*W); }
const png = Buffer.concat([Buffer.from([137, 80, 78, 71, 13, 10, 26, 10]),
  chunk('IHDR', ihdr), chunk('IDAT', zlib.deflateSync(raw, { level:9 })), chunk('IEND', Buffer.alloc(0))]);
fs.mkdirSync(path.dirname(E.image), { recursive:true });
fs.writeFileSync(E.image, png);
let n = 0; for(let k = 0; k < W*H; k++) n += land[k];
console.log('wrote ' + E.image + '  (' + (png.length/1024).toFixed(0) + ' Ko, ' + (100*n/(W*H)).toFixed(1) + ' % de terre)');
