/* The world outside the ship: where the land is, and where SHE is.
 *
 * A REAL SEA, READ FROM A PICTURE. The Caribbean of 1690 — Jamaica and
 * Port-Royal, the south coast of Cuba, Hispaniola and La Tortue, the Main from
 * Portobelo to Curaçao — in its real shapes, with distances and sizes cut by
 * ten (region.scale) and heights by four (region.vertical): at full size a
 * passage to Cartagena is four days, and at a tenth it is an evening.
 *
 * Everything comes from two files, and nothing else knows the shape of the
 * land (world/README.md):
 *   world/caraibes.json        the region: where the picture lies in latitude
 *                              and longitude, how grey becomes height, the
 *                              ports, the models placed on the land;
 *   world/caraibes-relief.png  the relief, grey, to be painted on: mid-grey is
 *                              the shore, lighter is land, darker is sea.
 * tools/region-heightmap.js wrote the first one from the real coastlines.
 *
 * Still a pure function of position, and still in TRUE WORLD metres, never in
 * the local coordinates the renderer uses: the floating origin slides local
 * zero as she sails (see Ocean.syncPhase). World zero is Port-Royal.
 *
 * The ports keep the shape the rest of the game already knew as `isles` — a
 * key, a name, a place on the shore and a `port` with its jetty — so the
 * market, the rumours, the encounters and the quests read them unchanged.
 * What an island used to answer by its radius (how far is the shore, where is
 * it) is answered by the picture now: shoreDistance and nearestShore.
 */
window.Naval = window.Naval || {};

Naval.World = class World {
  /* `region` is the sheet, `relief` the picture as { w, h, data } — one byte
     per pixel, the grey. Built by World.load; the constructor does no I/O. */
  constructor(region, relief){
    this.region = region;
    const E = this.relief = Object.assign({ sea:128, maxHeight:1500, maxDepth:400, curve:2 }, region.relief);
    this.img = relief;
    this.harbourDepth = region.harbourDepth || 11;   // a 2000-ton ship draws 6 to 8 m (see _dredge)
    this.deep = 70;                                  // the depth a "deep water" test means

    Naval.Geo.LAT0 = region.origin.lat;
    Naval.Geo.LON0 = region.origin.lon;
    Naval.Geo.SCALE = region.scale;

    // metres of game per pixel, for the distance field and the land tiles
    const latC = (E.north + E.south)/2;
    this.px = (E.north - E.south)*Naval.Geo.M_PER_MIN*60*region.scale/relief.h;
    this._lut = new Float32Array(256);
    for(let v = 0; v < 256; v++) this._lut[v] = this.decode(v);
    this._distanceField();

    this.isles = (region.ports || []).map(p => this._port(p)).filter(Boolean);
    this._measure();
  }

  /* Fetch the sheet and its picture — or take both from the page, where the
     build has put them (a published page may not fetch a local file). */
  static async load(url){
    let region = Naval.REGION_DATA;
    if(!region){
      const r = await fetch(url, { cache:'no-cache' });
      if(!r.ok) throw new Error('région illisible : ' + url);
      region = await r.json();
    }
    const im = new Image();
    im.src = region.relief.image;
    await im.decode();
    const cv = document.createElement('canvas');
    cv.width = im.naturalWidth; cv.height = im.naturalHeight;
    const cx = cv.getContext('2d', { willReadFrequently:true });
    cx.drawImage(im, 0, 0);
    const rgba = cx.getImageData(0, 0, cv.width, cv.height).data;
    const data = new Uint8Array(cv.width*cv.height);
    for(let i = 0, k = 0; k < data.length; i += 4, k++) data[k] = rgba[i];   // the red channel is the grey
    return new Naval.World(region, { w:cv.width, h:cv.height, data });
  }

  /* GREY TO METRES, the one formula — tools/region-heightmap.js writes with its
     inverse. Height goes as the square of the distance from the shore grey,
     so the first metres get many shades and the summits few. */
  decode(v){
    const E = this.relief;
    if(v >= E.sea) return E.maxHeight*Math.pow((v - E.sea)/(255 - E.sea), E.curve);
    return -E.maxDepth*Math.pow((E.sea - v)/E.sea, E.curve);
  }

  /* World metres to picture pixels (fractional, pixel centres at .5). */
  pixelAt(x, z){
    const g = Naval.Geo.fix(x, z), E = this.relief, I = this.img;
    return [(g.lon - E.west)/(E.east - E.west)*I.w, (E.north - g.lat)/(E.north - E.south)*I.h];
  }

  /* The grey at a world point, bilinear between pixel centres. Off the picture
     it is the open sea. */
  _grey(x, z){
    const I = this.img;
    const [pi, pj] = this.pixelAt(x, z);
    const fi = pi - 0.5, fj = pj - 0.5;
    const i = Math.floor(fi), j = Math.floor(fj);
    if(i < 0 || j < 0 || i >= I.w - 1 || j >= I.h - 1) return 0;
    const a = fi - i, b = fj - j, d = I.data, k = j*I.w + i;
    return (d[k]*(1 - a) + d[k + 1]*a)*(1 - b) + (d[k + I.w]*(1 - a) + d[k + I.w + 1]*a)*b;
  }

  /* Height of the land at a world point, in metres relative to sea level.
     Negative offshore, so the same function serves the shoreline, the shoals
     and anything that wants to know if she can float here. The harbour works
     are part of it (THE MOLE IS LAND: a wall that is land is a wall the
     grounding already knows about). */
  heightAt(x, z){
    return this._mole(x, z, this._dredge(x, z, this._islandHeight(x, z)));
  }

  // the relief alone, harbour works excluded — what a mole is measured against
  _islandHeight(x, z){ return this.decode(this._grey(x, z)); }

  /* THE BASIN IS DEEP TO THE QUAY. Around a berth the bottom is held at
     `harbourDepth`, and it meets the shore as a quay wall: the depth is reached
     as soon as there is a metre of water, rather than down a beach. Land is
     never cut. Read by heightAt, so the probes, the jetty legs and the seabed
     all see the same floor. (Port-Royal was exactly that: deep water close
     in, ships of any size alongside.) */
  _dredge(x, z, h){
    if(h >= 0) return h;
    for(const isl of this.isles){
      const B = isl.port && isl.port.basin;
      if(!B || Math.hypot(x - B.x, z - B.z) > B.r) continue;
      const u = Math.min(1, -h/1.0);
      h = Math.min(h, -this.harbourDepth*u*u*(3 - 2*u));
    }
    return h;
  }

  /* The ring wall of a port that asks for one (`"mole": true` in the sheet),
     and the gap in it. A natural harbour — Kingston behind the Palisadoes —
     has none. */
  _mole(x, z, h){
    for(const isl of this.isles){
      const H = isl.port && isl.port.harbour;
      if(!H) continue;
      const dx = x - H.cx, dz = z - H.cz;
      const d = Math.hypot(dx, dz);
      if(d < H.r || d > H.r + H.wall) continue;
      let a = Math.atan2(dz, dx) - H.ang;
      while(a >  Math.PI) a -= 2*Math.PI;
      while(a < -Math.PI) a += 2*Math.PI;
      if(Math.abs(a) < H.gap) continue;              // la passe
      const t = Math.min(1, (Math.abs(a) - H.gap)/0.10);
      if(h < H.top*t) h = H.top*t;
    }
    return h;
  }

  /* WHAT SURVIVES OF THE SEA behind a mole, from nought to one — the twin of
     Naval.SHELTER_GLSL, read by the sea's shader, this sampler and the foam
     pass alike. Only ports with a mole shelter anything yet. */
  shelter(x, z){
    let f = 1;
    for(const isl of this.isles){
      const H = isl.port && isl.port.harbour;
      if(!H) continue;
      const d = Math.hypot(x - H.cx, z - H.cz);
      if(d > H.r + H.wall) continue;
      const dp = Math.hypot(x - H.px, z - H.pz);
      const u = Math.min(1, dp/(1.6*H.r));
      const s = 1 - u*u*(3 - 2*u)*0.88;
      if(s < f) f = s;
    }
    return f;
  }

  // Is there water enough here for a hull drawing `draft` metres?
  navigable(x, z, draft){ return this.heightAt(x, z) < -(draft + 1.5); }

  /* HOW FAR TO THE SHORE, negative ashore — for the gulls, the dolphins, the
     encounters. A signed distance field, worked out once from the picture at a
     quarter of its resolution (180 m cells at the start: plenty for "within
     two kilometres of a coast", not for a berth, which reads heightAt). */
  _distanceField(){
    const I = this.img, K = 4;
    const w = Math.ceil(I.w/K), h = Math.ceil(I.h/K);
    const landAt = new Uint8Array(w*h), seaAt = new Uint8Array(w*h);
    for(let j = 0; j < h; j++) for(let i = 0; i < w; i++){
      const pi = Math.min(I.w - 1, i*K + (K >> 1)), pj = Math.min(I.h - 1, j*K + (K >> 1));
      const l = I.data[pj*I.w + pi] >= this.relief.sea;
      landAt[j*w + i] = l ? 1 : 0; seaAt[j*w + i] = l ? 0 : 1;
    }
    const toSea = Naval.World.edt(seaAt, w, h), toLand = Naval.World.edt(landAt, w, h);
    const cell = this.px*K, sd = new Float32Array(w*h);
    for(let k = 0; k < w*h; k++)
      sd[k] = landAt[k] ? -(Math.sqrt(toSea[k]) - 0.5)*cell : (Math.sqrt(toLand[k]) - 0.5)*cell;
    this._df = { w, h, K, sd };
  }

  shoreDistance(x, z){
    const D = this._df;
    const [pi, pj] = this.pixelAt(x, z);
    const fi = pi/D.K - 0.5, fj = pj/D.K - 0.5;
    const i = Math.max(0, Math.min(D.w - 2, Math.floor(fi))), j = Math.max(0, Math.min(D.h - 2, Math.floor(fj)));
    const a = Math.max(0, Math.min(1, fi - i)), b = Math.max(0, Math.min(1, fj - j)), s = D.sd, k = j*D.w + i;
    const v = (s[k]*(1 - a) + s[k + 1]*a)*(1 - b) + (s[k + D.w]*(1 - a) + s[k + D.w + 1]*a)*b;
    // off the picture: as far out as the edge says, and further
    const out = Math.max(0, -Math.min(0, fi), fi - (D.w - 1), -Math.min(0, fj), fj - (D.h - 1));
    return v + out*D.K*this.px;
  }

  /* The nearest point of shore, by walking down the distance field: where a
     flock of gulls has its roost, where a wreck's cargo fetches up. */
  nearestShore(x, z){
    let px = x, pz = z;
    for(let n = 0; n < 6; n++){
      const d = this.shoreDistance(px, pz), e = 30;
      const gx = this.shoreDistance(px + e, pz) - this.shoreDistance(px - e, pz);
      const gz = this.shoreDistance(px, pz + e) - this.shoreDistance(px, pz - e);
      const l = Math.hypot(gx, gz) || 1;
      px -= gx/l*d; pz -= gz/l*d;
      if(Math.abs(d) < 20) break;
    }
    return { x:px, z:pz };
  }

  /* WHERE THE PORT IS: the sheet gives a place (lat, lon) and the bearing the
     quay faces (`quay`, true degrees: 0 north, 90 east). The shore is found
     along that bearing, and the jetty runs out from it until there is water
     enough under it and NO FURTHER — it is a pier, not a causeway.

     Nine metres at the head, and every bound was found by trying it: a hull
     lies ALONGSIDE and so inside the head, and a significant swell of 1,6 m
     sets a moored hull down more than a metre below her mean draught several
     times a minute. Static clearance is not clearance. The basin round the
     berth is then dredged to harbourDepth (_dredge), so a great ship lies at
     the quay whatever the relief says. */
  _port(P){
    const G = Naval.Geo.toXZ(P.lat, P.lon);
    const b = (P.quay || 0)*Math.PI/180;
    const dx = -Math.sin(b), dz = Math.cos(b);                 // east is -x
    const at = s => this._islandHeight(G.x + dx*s, G.z + dz*s);
    // find the water's edge along the bearing, from wherever the sheet put the town
    let s0 = null;
    if(at(0) >= 0){ for(let s = 0; s < 3000; s += 3) if(at(s) < 0){ s0 = s; break; } }
    else{ for(let s = 0; s > -3000; s -= 3) if(at(s) >= 0){ s0 = s + 3; break; } }
    if(s0 == null){
      console.warn('[monde] ' + P.name + ' : pas de rivage à 3 km dans le relèvement ' + (P.quay || 0) + '° — port ignoré');
      return null;
    }
    const x = G.x + dx*s0, z = G.z + dz*s0;          // the shore, where the jetty's root is
    let reach = 20;
    for(let s = 20; s <= 150; s += 2){ reach = s; if(this._islandHeight(x + dx*s, z + dz*s) <= -9) break; }
    const ang = Math.atan2(dz, dx);
    const port = {
      name: P.name, ang, shoreR: 0, reach,
      sx: x - dx*6, sz: z - dz*6,
      hx: x + dx*reach, hz: z + dz*reach,
      basin: { x: x + dx*reach*0.5, z: z + dz*reach*0.5, r: Math.max(180, reach + 90) }
    };
    if(P.mole){
      const Rb = 170, gap = Math.asin(Math.min(0.95, 65/Rb));
      const cx = x + dx*Rb*0.80, cz = z + dz*Rb*0.80;
      port.harbour = { cx, cz, r:Rb, wall:18, top:3.4, gap, ang,
                       px: cx + dx*(Rb + 9), pz: cz + dz*(Rb + 9) };
    }
    return { key:P.key, name:P.name, x, z, r:600, rShore:0, start:!!P.start, lat:P.lat, lon:P.lon, port };
  }

  _measure(){
    const I = this.isles;
    this.longestLeg = 0;
    for(let i = 0; i < I.length; i++) for(let j = i + 1; j < I.length; j++)
      this.longestLeg = Math.max(this.longestLeg, Math.hypot(I[i].x - I[j].x, I[i].z - I[j].z));
    // how far the chart may zoom out: the whole picture from its middle
    const E = this.relief, a = Naval.Geo.toXZ(E.north, E.west), c = Naval.Geo.toXZ(E.south, E.east);
    this.extent = Math.hypot(a.x - c.x, a.z - c.z)/2;
  }

  byKey(key){ return this.isles.find(i => i.key === key) || null; }
  get startPort(){ return this.isles.find(i => i.start) || this.isles[0] || null; }

  // the ports within `range` of a world point
  near(x, z, range){
    const out = [];
    for(const isl of this.isles) if(Math.hypot(isl.x - x, isl.z - z) < range + isl.r) out.push(isl);
    return out;
  }

  /* THE CHART'S PICTURE of the whole region, drawn once from the relief at
     half its resolution: land in the chart's buff, shaded from the north-west;
     shoal water a pale band; the deep left clear for the chart's own blue. */
  chartImage(){
    if(this._chart) return this._chart;
    const I = this.img, K = 2, w = Math.floor(I.w/K), h = Math.floor(I.h/K);
    const cv = document.createElement('canvas');
    cv.width = w; cv.height = h;
    const cx = cv.getContext('2d'), im = cx.createImageData(w, h), o = im.data;
    const H = (i, j) => this._lut[I.data[Math.min(I.h - 1, j*K)*I.w + Math.min(I.w - 1, i*K)]];
    for(let j = 0; j < h; j++) for(let i = 0; i < w; i++){
      const y = H(i, j), k = (j*w + i)*4;
      if(y >= 0){
        const sh = Math.max(-1, Math.min(1, (H(i - 1, j - 1) - H(i + 1, j + 1))/(K*this.px)*6));
        const f = 1 + 0.35*sh - Math.min(0.25, y/2400);
        o[k] = 184*f; o[k + 1] = 162*f; o[k + 2] = 113*f; o[k + 3] = 255;
      }else if(y > -20){
        o[k] = 90; o[k + 1] = 150; o[k + 2] = 175; o[k + 3] = 80*(1 + y/20) + 20;
      }
    }
    cx.putImageData(im, 0, 0);
    this._chart = { canvas:cv, K };
    return this._chart;
  }
};

/* Exact squared Euclidean distance to the nearest `target` cell (Felzenszwalb
   and Huttenlocher), in cells. Shared with tools/region-heightmap.js in spirit;
   written twice only because one runs in Node and one in the page. */
Naval.World.edt = function(target, W, H){
  const INF = 1e20, n = Math.max(W, H);
  const f = new Float64Array(n), d = new Float64Array(n), z = new Float64Array(n + 1), v = new Int32Array(n);
  const out = new Float64Array(W*H);
  for(let k = 0; k < W*H; k++) out[k] = target[k] ? 0 : INF;
  const pass = (len, off, stride) => {
    for(let q = 0; q < len; q++) f[q] = out[off + q*stride];
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
    for(let q = 0; q < len; q++) out[off + q*stride] = d[q];
  };
  for(let i = 0; i < W; i++) pass(H, i, W);
  for(let j = 0; j < H; j++) pass(W, j*W, 1);
  return out;
};

/* Where she is, in the terms a navigator would use — and the other way.
 *
 * A nautical mile IS one minute of latitude, so northing converts exactly;
 * easting shrinks with the cosine of the latitude, which is real. The world
 * is the real one cut by SCALE (set from the region), so a chart reads the
 * true latitude and longitude of Port-Royal while the passage is a tenth as
 * long. The sun takes its latitude from LAT0: noon near the zenith, short
 * dusks — the tropics.
 */
Naval.Geo = {
  LAT0: 17.9375,                // Port-Royal, until the region says otherwise
  LON0: -76.8411,
  SCALE: 0.1,
  M_PER_MIN: 1852,              // metres in one minute of latitude

  fix(x, z){
    const m = this.M_PER_MIN*60*this.SCALE;
    const lat = this.LAT0 + z/m;
    const c = Math.max(0.02, Math.cos(lat*Math.PI/180));
    return { lat, lon: this.LON0 - x/(m*c) };
  },

  toXZ(lat, lon){
    const m = this.M_PER_MIN*60*this.SCALE;
    return { x: -(lon - this.LON0)*m*Math.max(0.02, Math.cos(lat*Math.PI/180)), z: (lat - this.LAT0)*m };
  },

  // degrees and decimal minutes, as a chart and a log book are written
  format(deg, isLat){
    const hemi = isLat ? (deg >= 0 ? 'N' : 'S') : (deg >= 0 ? 'E' : 'O');
    const a = Math.abs(deg);
    const d = Math.floor(a);
    const m = (a - d)*60;
    return String(d).padStart(isLat ? 2 : 3, '0') + '°'
         + (m < 10 ? '0' : '') + m.toFixed(1) + "' " + hemi;
  }
};
