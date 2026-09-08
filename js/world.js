/* The world outside the ship: where the land is, and where SHE is.
 *
 * There is no map file and there never will be one. The islands are a pure
 * function of position — a hash over a coarse grid of cells, one island in
 * roughly half of them — so the sea is endless, identical on every machine and
 * in every session, and costs nothing to store. Sail back to 40° N and the same
 * island is there, with the same bays.
 *
 * Everything here works in TRUE WORLD metres, never in the local coordinates
 * the renderer uses. The floating origin slides local zero around as she sails
 * (see Ocean.syncPhase); if the land were placed in local coordinates it would
 * swim away from under her at every rebase. Callers convert on the way out.
 */
window.Naval = window.Naval || {};

Naval.World = class World {
  constructor(seed){
    this.seed = seed == null ? 20260911 : seed;
    this.cell = 5200;            // metres between candidate islands
    this.chance = 0.46;          // how many cells actually hold one
    this._meshes = new Map();    // island key → built geometry, cached
  }

  /* A stable hash of two cell indices and a channel. Integer-safe over the
     range a ship can reach in any plausible voyage. */
  _h(i, j, k){
    let n = (i*374761393 + j*668265263 + k*1274126177 + this.seed) | 0;
    n = (n ^ (n >>> 13)) * 1274126177 | 0;
    n = (n ^ (n >>> 16)) >>> 0;
    return n / 4294967296;
  }

  /* The island in cell (i,j), or null. Its centre is jittered well inside the
     cell so two neighbours can never touch, whatever their radii. */
  island(i, j){
    if(this._h(i, j, 0) > this.chance) return null;
    const c = this.cell;
    const r = 420 + this._h(i, j, 1)*1350;              // 0.4 to 1.8 km across
    const room = c*0.5 - r - 260;                        // keep clear of the seam
    return {
      key: i + ':' + j,
      x: (i + 0.5)*c + (this._h(i, j, 2) - 0.5)*2*room,
      z: (j + 0.5)*c + (this._h(i, j, 3) - 0.5)*2*room,
      r,
      h: 40 + this._h(i, j, 4)*230,                      // summit, metres
      lobes: 3 + Math.floor(this._h(i, j, 6)*4),
      phase: this._h(i, j, 5)*6.2831853,
      rough: 0.30 + this._h(i, j, 7)*0.45
    };
  }

  /* Every island whose LAND could reach within `range` of a world point. */
  near(x, z, range){
    const c = this.cell, out = [];
    const i0 = Math.floor((x - range)/c), i1 = Math.floor((x + range)/c);
    const j0 = Math.floor((z - range)/c), j1 = Math.floor((z + range)/c);
    for(let i=i0;i<=i1;i++) for(let j=j0;j<=j1;j++){
      const isl = this.island(i, j);
      if(!isl) continue;
      const d = Math.hypot(isl.x - x, isl.z - z);
      if(d < range + isl.r*1.6) out.push(isl);
    }
    return out;
  }

  /* The shore is not a circle. The radius is modulated by a few harmonics of
     the bearing, which is what puts headlands and bays on it — a disc reads as
     a coin dropped in the sea, and no amount of height detail rescues it. */
  _shore(isl, ang){
    const a = ang + isl.phase;
    return isl.r * (1.0
      + 0.20*isl.rough*Math.sin(a*isl.lobes)
      + 0.11*isl.rough*Math.sin(a*(isl.lobes*2 + 1) + 1.7)
      + 0.06*isl.rough*Math.sin(a*(isl.lobes*3 + 2) + 4.1));
  }

  /* Height of the land at a world point, in metres relative to sea level.
     Negative offshore, so the same function serves the shoreline, the shoals
     around it and — later — anything that wants to know if she can float here. */
  heightAt(x, z){
    let best = -1e9;
    for(const isl of this.near(x, z, 60)){
      const dx = x - isl.x, dz = z - isl.z;
      const d = Math.hypot(dx, dz);
      const s = this._shore(isl, Math.atan2(dz, dx));
      const t = d/s;
      let h;
      if(t >= 1.35) h = -70;
      else if(t >= 1.0){
        // the beach runs on under water into a shoal, not off a cliff
        const u = (t - 1.0)/0.35;
        h = -70*u*u;
      }else{
        /* A summit that falls away as a smoothstep, plus a ridge or two so the
           silhouette is not a dome. Beaches are flat: the profile is deliberately
           slack in the last tenth before the shore. */
        const u = 1 - t;
        const base = u*u*(3 - 2*u);
        const ridge = 0.22*Math.sin(Math.atan2(dz, dx)*isl.lobes*2 + isl.phase*2)
                          *Math.sin(Math.PI*u);
        h = isl.h*Math.max(0, base*(0.86 + ridge))*Math.min(1, u*7.0);
      }
      if(h > best) best = h;
    }
    return best === -1e9 ? -70 : best;
  }

  // Is there water enough here for a hull drawing `draft` metres?
  navigable(x, z, draft){ return this.heightAt(x, z) < -(draft + 1.5); }
};

/* Where she is, in the terms a navigator would use.
 *
 * The world's zero is placed at an arbitrary point of open sea. A nautical mile
 * IS one minute of latitude — that is its definition — so northing converts
 * exactly, with no projection and no fudge. Easting shrinks with the cosine of
 * the latitude, which is real: a degree of longitude is 111 km at the equator
 * and nothing at all at the pole. Ignoring it would make every distance read on
 * the chart wrong by that factor.
 */
Naval.Geo = {
  LAT0: 46.20,                  // a patch of the Bay of Biscay, near enough
  LON0: -4.75,
  M_PER_MIN: 1852,              // metres in one minute of latitude

  fix(x, z){
    const lat = this.LAT0 + z/(this.M_PER_MIN*60);
    const c = Math.max(0.02, Math.cos(lat*Math.PI/180));
    return { lat, lon: this.LON0 + x/(this.M_PER_MIN*60*c) };
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
