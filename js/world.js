/* The world outside the ship: where the land is, and where SHE is.
 *
 * FOUR ISLANDS, EACH WITH A PORT, and this is a deliberate change of mind.
 *
 * The islands used to be a pure function of position — a hash over a coarse
 * grid, one island in roughly half the cells — so the sea was endless and cost
 * nothing to store. That was the right shape for a world with nothing in it:
 * an infinity of anonymous land is worth more than four bits of it, right up
 * until the moment there is something to sail BETWEEN. A named port one can
 * leave and return to is worth more than ten thousand nameless bays, and one
 * cannot name what one has not decided on.
 *
 * So the hash is gone and a table of four takes its place. What survives is the
 * part of the old rule that was actually load-bearing: this is still a pure
 * function of position with no state and no file to load, and it still answers
 * in TRUE WORLD metres, never in the local coordinates the renderer uses. The
 * floating origin slides local zero around as she sails (see Ocean.syncPhase);
 * land placed in local coordinates would swim out from under her at every
 * rebase. Callers convert on the way out.
 *
 * The sea beyond them is empty, and that is the price. Sail west from La Tortue
 * and there is nothing, for ever.
 */
window.Naval = window.Naval || {};

/* An archipelago of the buccaneering years, laid out like the Windwards it is
   modelled on: a chain running roughly north and south with one island out to
   the east, ten to twenty kilometres apart — far enough that a passage is a
   passage, near enough that one raises the next before losing the last.

   Every name is a real port of the period. Port-Royal was the great one and is
   where she starts; La Tortue was the buccaneers' own; Saint-Pierre was the
   richest town in the French islands; Le Carénage is what the anchorage at
   Sainte-Lucie was called long before anybody called it Castries — a bay one
   went to in order to careen, which is exactly what a port is for here. */
Naval.ARCHIPELAGO = [
  { key:'port-royal',  name:'Port-Royal',   x:  12000, z: -7000,
    r:4200, h:280, lobes:4, phase:0.9,  rough:0.44 },
  { key:'carenage',    name:'Le Carénage',  x:   1500, z:  3000,
    r:3100, h:520, lobes:3, phase:2.4,  rough:0.52 },
  { key:'saint-pierre',name:'Saint-Pierre', x:  -2000, z: 16000,
    r:3600, h:430, lobes:5, phase:5.1,  rough:0.38 },
  { key:'tortue',      name:'La Tortue',    x:  -9000, z: -9000,
    r:2600, h:240, lobes:3, phase:3.7,  rough:0.60 }
];

/* THE MAP SCALE MOVES THE ISLANDS APART, IT NEVER RESIZES THEM.

   Distance and size are two different questions and only one of them is a
   matter of taste. A shelf is 240 m, a berth wants 9 m of water, a mole is
   18 m thick and a jetty is 86 m long: those are dimensions of SHIPS, and a
   world that shrank them would put a quarter of an island under its own
   harbour and bring back the wading-depth fault this file was written to
   cure. Distance, on the other hand, is nothing but how long a passage takes,
   so it is the one number a player may honestly be given.

   THE FACTOR HAS A FLOOR, AND IT IS THE GEOMETRY THAT SETS IT. Leave the
   islands their size and they eventually touch: Port-Royal and Le Carénage
   have 5 957 m of water between their shores, so below about 0.69 they merge
   into one island shaped like a figure of eight. Asked for a third, the
   honest answer was 0.70 — and `_check` below says so out loud rather than
   letting two coastlines quietly grow together.

   Note the shore is NOT the nominal radius: `_shore` modulates it by a few
   harmonics of the bearing, so real coastlines run 14 to 22 % wider than
   `r`. Measuring the floor against `r` would have promised a channel that
   is not there. */
Naval.MAP_SCALE = 0.70;

Naval.World = class World {
  constructor(){
    /* How far the bottom keeps falling away beyond the shore line, in METRES
       and not as a fraction of the island. It used to be 0,35 of the radius,
       which was invisible while every island was a few hundred metres across
       and absurd the moment they became kilometres: a four-kilometre island
       had a mile and a half of wading depth round it, so nothing could lie
       alongside anything and a jetty would have had to be a causeway. A shelf
       is a few hundred metres wide whatever the island behind it is doing. */
    this.shelf = 240;
    this.deep = 70;              // how deep it is once past the shelf

    const k = this.mapScale = Naval.MAP_SCALE || 1;
    this.isles = Naval.ARCHIPELAGO.map(t =>
      Object.assign({}, t, { x: t.x*k, z: t.z*k }));

    /* Real outer shore, sampled rather than assumed — see the note above. It
       is wanted by the floor check, and by the chart, which has to know how
       far out to let itself zoom. */
    for(const isl of this.isles){
      let m = 0;
      for(let i=0;i<360;i++) m = Math.max(m, this._shore(isl, i*Math.PI/180));
      isl.rShore = m;
    }
    for(const isl of this.isles) isl.port = this._port(isl);
    this._measure();
  }

  /* The longest leg, and how far the chart must reach to hold the whole
     archipelago. Both are DERIVED: anything tuned against the size of the
     world — the price step, the chart's zoom stop — has to follow the factor
     or it means something different at every scale. */
  _measure(){
    const I = this.isles;
    let cx = 0, cz = 0;
    for(const a of I){ cx += a.x/I.length; cz += a.z/I.length; }

    this.longestLeg = 0;
    let worst = null;
    for(let i=0;i<I.length;i++) for(let j=i+1;j<I.length;j++){
      const d = Math.hypot(I[i].x-I[j].x, I[i].z-I[j].z);
      if(d > this.longestLeg) this.longestLeg = d;
      const gap = d - I[i].rShore - I[j].rShore;
      if(!worst || gap < worst.gap) worst = { gap, a:I[i].name, b:I[j].name };
    }
    this.closest = worst;

    this.extent = 0;
    for(const a of I)
      this.extent = Math.max(this.extent, Math.hypot(a.x-cx, a.z-cz) + a.rShore);

    /* Two shores closer than a cable is not a strait, it is a modelling
       accident — and one that reads as a single misshapen island rather than
       as an error. Say it. */
    if(worst.gap < 185)
      console.warn("Naval.MAP_SCALE " + this.mapScale.toFixed(2) + " : "
        + worst.a + " et " + worst.b + " ne sont plus qu’à "
        + Math.round(worst.gap) + " m l’une de l’autre.");
  }

  byKey(key){ return this.isles.find(i => i.key === key) || null; }

  /* Every island whose land could reach within `range` of a world point.
     Four of them, so this is a filter rather than a search. */
  near(x, z, range){
    const out = [];
    for(const isl of this.isles)
      if(Math.hypot(isl.x - x, isl.z - z) < range + isl.r*1.6) out.push(isl);
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

  /* WHERE THE PORT IS, and it is not chosen — it is read off the island.

     A harbour is a bight, so the port stands on the bearing where the shore
     comes in CLOSEST to the middle of the island: that is the deepest bite out
     of the coast, the one with land on either hand of it. It follows the same
     rule as everything else here — the shape decides, and the same `_shore`
     the terrain mesh and the chart are drawn from decides it — so the jetty
     can never end up on a headland the eye can plainly see is a headland.

     A margin of a few degrees is kept off the exact minimum on purpose: the
     very bottom of a bight is where the beach is flattest, and a jetty wants a
     little more water under its head than that. */
  _port(isl){
    const p = this._bight(isl);
    return p;
  }

  _bight(isl){
    let bestA = 0, bestS = Infinity;
    for(let i=0;i<360;i++){
      const a = (i/360)*Math.PI*2, s = this._shore(isl, a);
      if(s < bestS){ bestS = s; bestA = a; }
    }
    const ca = Math.cos(bestA), sa = Math.sin(bestA);
    /* The jetty runs out from the beach until there is water enough under it,
       and NO FURTHER: it is a pier, not a causeway. So the length is solved
       for rather than picked — invert the shelf profile for the depth a berth
       wants and that is where the head goes.

       Nine metres, and every bound was found by trying it. Twelve gave a pier of
       eighty-five metres on fourteen-metre legs, which is a viaduct. Six and a
       half looked right at the head and was not: a hull lies ALONGSIDE and
       therefore inside the head, where the bottom is still coming up, so the
       berth itself was in four metres and she touched by forty-five centimetres
       ranging on her lines.

       And seven and a half was not enough either, which is where the SWELL came
       into it. A significant height of 1,6 m means individual waves half again
       as big, so a moored hull is set down more than a metre below her own mean
       draught several times a minute — while ranging on her lines, which walks
       her ends into shallower water than her middle ever sees. Static clearance
       is not clearance. A ship that grounds at her own quay is not a port. */
    const berthDepth = 9.0;
    const reach = Math.min(150, this.shelf*Math.sqrt(berthDepth/this.deep));
    const p = {
      name: isl.name,
      ang: bestA,
      shoreR: bestS,
      // the root, on the beach, and the head, out in the stream
      sx: isl.x + ca*(bestS - 6), sz: isl.z + sa*(bestS - 6),
      hx: isl.x + ca*(bestS + reach), hz: isl.z + sa*(bestS + reach),
      reach
    };
    /* ET UN VRAI HAVRE AUTOUR, parce qu'une échancrure n'abrite rien. Relevé
       sur celle de Port-Royal avant de la construire : 2 043 m d'ouverture pour
       675 m de creux dans la côte, quand la houle qui compte à force 4 fait 23 à
       40 m de longueur d'onde. Le rapport ouverture sur longueur d'onde vaut
       SOIXANTE-DEUX, et c'est lui qui décide de tout : la diffraction n'abrite
       que si la passe est de l'ordre de quelques longueurs d'onde. À
       soixante-deux, la mer entre tout droit sans rien perdre, et aucun
       coefficient d'atténuation n'aurait été autre chose qu'un abri décrété.

       Le havre est donc un BASSIN fermé par un môle, et l'abri en sort par la
       géométrie : 130 m de passe pour 33 m de lame, soit un rapport de quatre.

       Un disque et un anneau, ce qui n'est pas de la paresse mais la condition
       pour que la même forme soit calculable trois fois — au CPU pour le
       solveur, dans le shader de la mer et dans celui de l'écume — sans que les
       trois puissent diverger. Une côte dessinée à la main ne s'écrit pas en
       quatre lignes de GLSL. */
    const Rb = 170;                                  // rayon du bassin
    const gap = Math.asin(Math.min(0.95, 65/Rb));    // demi-passe : 130 m de corde
    const cx = isl.x + ca*(bestS + Rb*0.80);
    const cz = isl.z + sa*(bestS + Rb*0.80);
    p.harbour = {
      cx, cz, r:Rb, wall:18, top:3.4, gap,
      ang: bestA,
      // le milieu de la passe, d'où toute l'énergie qui entre doit venir
      px: cx + ca*(Rb + 9), pz: cz + sa*(Rb + 9)
    };
    return p;
  }

  /* Height of the land at a world point, in metres relative to sea level.
     Negative offshore, so the same function serves the shoreline, the shoals
     around it and anything that wants to know if she can float here.

     THE MOLE IS LAND, which is the whole reason to write it here rather than as
     an object with a collision box: a wall that is land is a wall the grounding
     already knows about. She strikes it, is lifted, slews and opens her side on
     it exactly as she would on a shoal, and not one line of that had to be
     written twice. */
  heightAt(x, z){
    return this._mole(x, z, this._islandHeight(x, z));
  }

  /* The island alone, harbour works excluded — what the terrain mesh is built
     on, and what the mole is measured against so it is not drawn buried in a
     hillside. */
  _islandHeight(x, z){
    let best = -1e9;
    for(const isl of this.near(x, z, 60)){
      const dx = x - isl.x, dz = z - isl.z;
      const d = Math.hypot(dx, dz);
      const s = this._shore(isl, Math.atan2(dz, dx));
      let h;
      const out = d - s;                      // metres beyond the shore line
      if(out >= this.shelf) h = -this.deep;
      else if(out >= 0){
        // the beach runs on under water into a shoal, not off a cliff
        const u = out/this.shelf;
        h = -this.deep*u*u;
      }else{
        /* A summit that falls away as a smoothstep, plus a ridge or two so the
           silhouette is not a dome. Beaches are flat: the profile is deliberately
           slack in the last tenth before the shore. */
        const u = 1 - d/s;
        const base = u*u*(3 - 2*u);
        const ridge = 0.22*Math.sin(Math.atan2(dz, dx)*isl.lobes*2 + isl.phase*2)
                          *Math.sin(Math.PI*u);
        h = isl.h*Math.max(0, base*(0.86 + ridge))*Math.min(1, u*7.0);
      }
      if(h > best) best = h;
    }
    return best === -1e9 ? -this.deep : best;
  }

  /* The ring wall, and the gap in it. One angular test and two radial ones;
     everything else about a harbour follows from where those fall. */
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
      /* Les têtes du môle sont arrondies plutôt que coupées net : une arête
         verticale à l'entrée est ce sur quoi on s'ouvre le flanc en entrant,
         et un musoir se doit d'être franchissable de justesse. */
      const t = Math.min(1, (Math.abs(a) - H.gap)/0.10);
      if(h < H.top*t) h = H.top*t;
    }
    return h;
  }

  /* WHAT SURVIVES OF THE SEA IN HERE, from nought to one — and it is a fraction
     of AMPLITUDE, so it must be read identically by the three things that
     compute the swell or they part company in silence: the sea's vertex shader,
     this sampler, and the foam pass. Its GLSL twin is Naval.SHELTER_GLSL, and
     the two are a matched pair.

     The model is the honest one for a basin behind a wall: everything that gets
     in comes through the passe and spreads from it, so what is left falls off
     with the distance from the mouth. Outside the wall, nothing changes. */
  shelter(x, z){
    let f = 1;
    for(const isl of this.isles){
      const H = isl.port && isl.port.harbour;
      if(!H) continue;
      const d = Math.hypot(x - H.cx, z - H.cz);
      if(d > H.r + H.wall) continue;
      const dp = Math.hypot(x - H.px, z - H.pz);
      const u = Math.min(1, dp/(1.6*H.r));
      const s = 1 - u*u*(3 - 2*u)*0.88;              // 1 à la passe, 0,12 au fond
      if(s < f) f = s;
    }
    return f;
  }

  // Is there water enough here for a hull drawing `draft` metres?
  navigable(x, z, draft){ return this.heightAt(x, z) < -(draft + 1.5); }
};

/* Where she is, in the terms a navigator would use.
 *
 * The world's zero is a patch of open water in the middle of the archipelago. A
 * nautical mile IS one minute of latitude — that is its definition — so
 * northing converts exactly, with no projection and no fudge. Easting shrinks
 * with the cosine of the latitude, which is real: a degree of longitude is
 * 111 km at the equator and nothing at all at the pole. Ignoring it would make
 * every distance read on the chart wrong by that factor.
 *
 * Thirteen degrees north rather than forty-six, the islands having moved to
 * the Windwards. It is not decoration: the sun follows real spherical
 * trigonometry from this latitude, so the day is nearly even all year, noon is
 * very near the zenith, and dusk is short. That is the tropics, and it is what
 * the light should do above these islands.
 */
Naval.Geo = {
  LAT0: 13.60,                  // between Martinique and Sainte-Lucie
  LON0: -61.00,
  M_PER_MIN: 1852,              // metres in one minute of latitude

  fix(x, z){
    const lat = this.LAT0 + z/(this.M_PER_MIN*60);
    const c = Math.max(0.02, Math.cos(lat*Math.PI/180));
    return { lat, lon: this.LON0 - x/(this.M_PER_MIN*60*c) };
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
