/* Weather that has a PLACE.
 *
 * Written the way the islands are written, and for the same reasons: a hash
 * over a coarse grid, no map file, no state, nothing to store. Sail back to the
 * same water and the same depression is there. The difference is that a storm
 * also has a TIME — a system travels — so this is a pure function of position
 * AND of the clock, which costs nothing extra and buys a sky that comes to meet
 * her as much as she sails into it.
 *
 * The drift is a TRIANGLE WAVE rather than a straight line, and that is not
 * laziness. A centre marching off in one direction wanders away without bound,
 * so no search of neighbouring cells could ever be sure of finding it; taking
 * the drift modulo something instead makes it JUMP back, and a jump is a sea
 * state that changes by four Beaufort between two frames. A triangle is bounded
 * AND continuous, which is the only combination that serves — the same reason
 * the wave phase is reduced modulo 2π rather than clamped.
 *
 * The wander is set at rather more than a cell, so a depression genuinely
 * passes: confined to its own cell it swung on and off a vessel lying to,
 * for ever, which is not weather but a place.
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
    /* Room to travel, and it has to be a good deal MORE than the cell — which
       was the first mistake. Confined inside its own cell a centre wandered
       three kilometres against a radius of three and a half, under one radius,
       so a vessel lying to found the storm swinging off her and back onto her
       for ever: measured, the intensity went 1 · 0,74 · 0,01 · 0,82 · 0,99 over
       half an hour. Lulls, but no deliverance — and a depression that never
       leaves is not weather, it is a place. Given a wander of better than a
       cell it genuinely passes, and waiting it out becomes a real choice beside
       sailing out of it. */
    const room = c*1.35 - r;
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

     Two rings of cells are searched rather than one, since a centre now
     wanders by better than a cell — twenty-five hashes a frame, which costs
     nothing and is the price of storms that actually travel. */
  /* LA PLUS PROCHE, AUSSI LOIN QU'IL FAUT — et `at` ne sait pas répondre à ça.
     Celui-ci balaie des anneaux de cellules de plus en plus larges jusqu'à
     trouver, parce qu'on veut pouvoir dire « emmène-moi dans le gros temps »
     sans savoir s'il y en a un à onze kilomètres ou à cinquante. Il rend le
     centre en mètres monde VRAIS, comme tout ce que ce fichier manipule.

     Écrit pour l'essai plutôt que pour le jeu : porter de la toile dans un coup
     de vent coûte maintenant de la toile, et on ne règle pas cela en attendant
     qu'une dépression veuille bien passer. */
  nearest(x, z, t, ringsMax, ok){
    const c = this.cell, i0 = Math.floor(x/c), j0 = Math.floor(z/c);
    const R = Math.max(1, ringsMax || 12);
    for(let ring=0; ring<=R; ring++){
      let best = null, bd = Infinity;
      for(let i=i0-ring;i<=i0+ring;i++) for(let j=j0-ring;j<=j0+ring;j++){
        // l'anneau seul : l'intérieur a déjà été vu au tour précédent
        if(ring > 0 && Math.abs(i-i0) !== ring && Math.abs(j-j0) !== ring) continue;
        const s = this.cellStorm(i, j, t);
        if(!s) continue;
        /* TOUTES NE CONVIENNENT PAS, et le filtre est la façon de le dire sans
           que ce fichier ait à connaître la terre : un grain est une fonction
           pure de la position et de l'heure, exactement comme les îles, donc
           rien ne les empêche de tomber au même endroit. Celui qui cherche sait
           ce qu'il veut trouver ; la météo n'a pas à le savoir. */
        if(ok && !ok(s)) continue;
        const d = Math.hypot(s.x - x, s.z - z);
        if(d < bd){ bd = d; best = s; }
      }
      if(best) return { storm:best, dist:bd };
    }
    return null;
  }

  at(x, z, t){
    const c = this.cell;
    const i0 = Math.floor(x/c), j0 = Math.floor(z/c);
    let best = null, bd = Infinity;
    for(let i=i0-2;i<=i0+2;i++) for(let j=j0-2;j<=j0+2;j++){
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
    const windDeg = (Math.atan2(wx, -wz)*180/Math.PI + 360) % 360;

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
