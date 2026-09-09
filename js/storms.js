/* Weather that has a PLACE.
 *
 * Written the way the islands are written, and for the same reasons: a hash
 * over a coarse grid, no map file, no state, nothing to store. Sail back to the
 * same water and the same depression is there. The difference is that a storm
 * also has a TIME — a system travels — so this is a pure function of position
 * AND of the clock, which costs nothing extra and buys a sky that comes to meet
 * her as much as she sails into it.
 *
 * The drift is a triangle wave inside the cell rather than a straight line, and
 * that is not laziness. A centre marching off in one direction leaves its own
 * cell within the hour, so a search of the neighbouring cells would stop
 * finding it; taking the drift modulo the cell instead makes it JUMP to the
 * other side, and a jump is a sea state that changes by four Beaufort between
 * two frames. A triangle stays inside, never jumps, and a depression that
 * wanders back and forth over a day is not an outrageous thing for one to do.
 *
 * Everything here is in TRUE WORLD metres, like world.js and for the same
 * reason: the floating origin slides local zero about as she sails.
 */
window.Naval = window.Naval || {};

Naval.Storms = class Storms {
  constructor(seed){
    this.seed = seed == null ? 76314529 : seed;
    /* Eleven kilometres between candidate depressions, two to four across.
       Sized for a ship, not for a weather chart: a real low is hundreds of
       kilometres wide and would take a week to cross under sail. These take ten
       or twenty minutes to pass through, which is long enough to be an ordeal
       and short enough to be one you come out of. */
    this.cell = 11000;
    this.chance = 0.40;
    this.drift = 5.5;            // m/s, the speed the system travels
  }

  _h(i, j, k){
    let n = (i*920419823 + j*195736897 + k*611879891 + this.seed) | 0;
    n = (n ^ (n >>> 13)) * 195736897 | 0;
    n = (n ^ (n >>> 16)) >>> 0;
    return n / 4294967296;
  }

  /* -1 to 1 and back again, continuously: the drift that never jumps. */
  _tri(u){
    const f = u - Math.floor(u);
    return f < 0.5 ? (f*4 - 1) : (3 - f*4);
  }

  /* The depression in cell (i,j) at time t, or null. */
  cellStorm(i, j, t){
    if(this._h(i, j, 0) > this.chance) return null;
    const c = this.cell;
    const r = 1800 + this._h(i, j, 1)*2400;
    const room = c*0.5 - r - 500;
    const spd = this.drift*(0.55 + this._h(i, j, 2));
    // one full sweep of the cell and back, at her own speed
    const per = Math.max(60, 4*room/spd);
    const px = this._h(i, j, 3), pz = this._h(i, j, 4);
    return {
      key: i + ':' + j,
      x: (i + 0.5)*c + room*this._tri(t/per + px),
      z: (j + 0.5)*c + room*this._tri(t/per*0.83 + pz),
      r,
      peak: 7.2 + this._h(i, j, 5)*2.3,           // Beaufort at the centre
      spin: this._h(i, j, 6) < 0.5 ? -1 : 1       // which way she turns
    };
  }

  /* The weather at a world point. Returns the nearest depression, how deep into
     it she is, and what the wind is doing there — or null in clear air.

     Only the nine cells about her are searched: a storm cannot leave its own
     cell, which is the whole point of the triangle above. */
  at(x, z, t){
    const c = this.cell;
    const i0 = Math.floor(x/c), j0 = Math.floor(z/c);
    let best = null, bd = Infinity;
    for(let i=i0-1;i<=i0+1;i++) for(let j=j0-1;j<=j0+1;j++){
      const s = this.cellStorm(i, j, t);
      if(!s) continue;
      const d = Math.hypot(s.x - x, s.z - z);
      if(d < bd){ bd = d; best = s; }
    }
    if(!best || bd > best.r*3.2) return null;

    const s = best, d = bd;
    /* Deepening toward the middle, and not linearly: a depression has a wide
       shoulder and a hard core, so most of the crossing is merely dirty weather
       and the last third is the part one remembers. */
    const u = Math.max(0, 1 - d/s.r);
    const inten = u*u*(3 - 2*u);                  // smooth at both ends

    /* The wind turns AROUND the centre — that is what makes a system read as a
       system rather than as the sea simply getting bigger. Mostly tangential
       with a little draw inward, which is what a real low does, and it means
       she can work out where the middle is from the feel of the wind alone. */
    const nx = d > 1 ? (x - s.x)/d : 0, nz = d > 1 ? (z - s.z)/d : 1;
    const tx = -nz*s.spin, tz = nx*s.spin;
    const wx = tx*0.88 - nx*0.34, wz = tz*0.88 - nz*0.34;
    // the bearing it blows FROM, as a seaman states it and as ocean.setWind wants
    const windDeg = (Math.atan2(-wx, -wz)*180/Math.PI + 360) % 360;

    return {
      storm:s, dist:d, inten,
      force: s.peak*inten,
      windDeg,
      /* How much of the sky ahead should be black. It rises as she closes and
         then hands over to the general gloom once she is inside it, there being
         no point darkening one sector of a sky that is already a lid. */
      loom: Math.max(0, Math.min(1, (s.r*2.6 - d)/(s.r*1.6)))*(1 - inten*0.85),
      // unit bearing toward the middle, for that darkening
      toX: d > 1 ? -nx : 0, toZ: d > 1 ? -nz : 1
    };
  }
};
