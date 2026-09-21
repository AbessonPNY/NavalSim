/* A vessel, as data.
 *
 * The JSON states what a naval architect would state: principal dimensions,
 * the coefficients that shape the lines, a displacement in tonnes, a sail plan.
 * Everything the solver needs is DERIVED from those here, so a bigger ship is a
 * bigger ship in every respect — resistance, steering, inertia — without a
 * single hand-tuned magic number per vessel.
 *
 * Note the hydro coefficients are per unit of area, not absolute forces:
 *   resistance  → N per (m/s)² per m² of midship section (beam × keel depth)
 *   lateralGrip → N per (m/s)  per m² of lateral plane  (length × keel depth)
 * That is what lets one set of numbers serve a 24 m schooner and a 60 m frigate.
 */
window.Naval = window.Naval || {};

Naval.ShipSpec = class ShipSpec {
  constructor(json){
    this.raw = json;
    this.id = json.id;
    this.name = json.name;
    this.note = json.note || '';

    const h = json.hull;
    this.hull = h;
    this.L = h.length;
    this.B = h.beam;
    this.keel = h.keelDepth;
    // moulded depth: the vertical span the probe grid has to cover
    this.D = h.freeboardMid + h.keelDepth + h.keelExtra;
    this.deckMid = h.freeboardMid;

    // displacement is the input; the fill fraction falls out of the hull volume
    this.massKg = json.displacementTonnes * 1000;
    this.tonnes = json.displacementTonnes;

    // centre of gravity, as fractions so it scales with the vessel
    this.cog = {
      x: 0,
      y: json.cog.yFracKeel * this.keel,
      z: json.cog.zFracLength * this.L
    };

    // --- derived hydrodynamics ---
    const midshipArea = this.B * this.keel;       // m², sets fore-and-aft resistance
    const lateralArea = this.L * this.keel;       // m², the keel's grip and rudder leverage
    const hy = json.hydro;
    this.drag = hy.resistance * midshipArea;
    this.lateralLinear = hy.lateralGrip * lateralArea;
    this.lateralQuad = hy.lateralQuad * lateralArea;
    this.heaveDamp = hy.heaveDamp;

    // engine sized to its stated top speed against that resistance
    this.topSpeed = json.engine.topSpeed;
    this.maxThrust = this.drag * this.topSpeed * this.topSpeed;
    /* How much of her ahead thrust she can raise going astern, as a negative
       fraction of it. A screw turned backwards works against its own wash and a
       ship's sweeps are no match for her sails, so no vessel backs as hard as
       she drives: half to two thirds is the usual figure. Clamped, because a
       positive value here would let the astern telegraph drive her forward. */
    const sp = json.engine.sternPower;
    this.sternPower = Math.max(-1, Math.min(0, sp != null ? sp : -0.6));
    // the solver's safety clamp, m/s: 40 unless a modern hull raises it
    this.speedLimit = Math.max(1, json.engine.speedLimit != null ? json.engine.speedLimit : 40);

    /* OARS, for a boat that is pulled rather than sailed or driven. The stroke
       rate is stated; the pull is not — it is the engine's thrust, spent in
       pulses, so `engine.topSpeed` stays the one figure that says how fast she
       goes and one set of numbers still serves every hull. */
    this.oars = json.oars ? {
      pairs: json.oars.pairs || 2,
      period: json.oars.period || 2.2,
      length: json.oars.length || 1.7*json.hull.beam
    } : null;
    /* LES FEUX DE POUPE : où ils pendent et combien il y en a. Une fiche en
       nomme autant qu'elle en porte — un galion en montrait souvent trois au
       couronnement — et chacun donne sa place dans SON repère : `x`/`z` en
       mètres, ou `xFrac`/`zFrac` en fractions du bau et de la longueur, ce qui
       suit le navire quelle que soit sa taille. `y` absent veut dire « sur le
       pont à cette station », lu sur le modèle lui-même. Rien de déclaré rend
       l'ancien comportement : un seul feu au couronnement. */
    this.lanterns = Array.isArray(json.lanterns)
      ? json.lanterns.map(l => Object.assign({}, l)) : null;
    /* Les hommes sur le pont : un nombre (placés tout seuls) ou une liste de
       places {x|xFrac, z|zFrac, y?, yaw? en degrés}. Absent : settings.json. */
    this.crew = Array.isArray(json.crew) ? json.crew.map(c => Object.assign({}, c))
              : (typeof json.crew === 'number' ? json.crew : null);
    /* The colours she flies, and where: see ships/README.md. Nothing declared
       keeps the one ensign at her tallest masthead. */
    this.flags = Array.isArray(json.flags)
      ? json.flags.map(l => Object.assign({}, l)) : null;

    // the ship that carries a boat names it: the id of another sheet
    this.boat = json.boat || null;

    // rudder
    this.rudderK = json.rudder.power * lateralArea;
    this.rudderMax = json.rudder.maxAngle;
    this.rudderZ = json.rudder.postZFrac * this.L;
    this.rudderY = json.rudder.postY;
    /* Seconds from amidships to hard over. A rudder is hauled round by tackle
       and men, and a big one takes its time: by default it grows as the root
       of her length — 3.4 s for 30 m, 4.7 s for 60 m. rudder.hardOver says it
       outright. */
    this.rudderTime = json.rudder.hardOver != null
      ? json.rudder.hardOver : 3*Math.sqrt(this.L/24);

    // rig
    const r = json.rig;
    this.rig = r;
    this.sailArea = r.sailArea;
    this.ceHeight = r.ceHeight;
    this.ceZ = r.ceZ;
    this.maxSheet = r.maxSheet;

    this.camera = json.camera;
    this.appearance = json.appearance;
    this.model = json.model || null;

    // absolute mast positions, from their fractions of length
    this.masts = (r.masts || []).map(m => Object.assign({}, m, { z: m.zFrac * this.L }));
    this.jibFootZ = r.jib ? r.jib.footFrac * this.L : 0;
  }

  /* Which vessels exist. In development the dev server lists the ships folder
     live, so dropping a spec in there is enough. A published page carries the
     list with it, baked in by the build. */
  static async discover(){
    if(Naval.SHIP_DATA) return Object.keys(Naval.SHIP_DATA);
    try{
      const res = await fetch('ships/index.json', {cache:'no-store'});
      if(res.ok){
        const list = await res.json();
        if(Array.isArray(list) && list.length) return list;
      }
    }catch(e){ /* no index — fall back to the built-in list */ }
    return Naval.Config.SHIPS;
  }

  /* In development the specs are fetched as files. The build inlines them into
     Naval.SHIP_DATA instead, because a published page runs under a policy that
     forbids fetching local files — the fetch would fail silently and leave the
     simulation with no vessel at all. */
  static async load(url){
    if(Naval.SHIP_DATA && Naval.SHIP_DATA[url]){
      return new Naval.ShipSpec(Naval.SHIP_DATA[url]);
    }
    const res = await fetch(url, {cache:'no-store'});
    if(!res.ok) throw new Error('cannot read ' + url + ' (' + res.status + ')');
    return new Naval.ShipSpec(await res.json());
  }

  /* Called once the probe grid is built and the true hull volume is known.
     A vessel that cannot float on her stated tonnage is a specification error,
     not a physics one — so say so plainly rather than let her sink silently. */
  checkFlotation(hullVolume, rho){
    const fill = this.massKg / (rho * hullVolume);
    this.hullVolume = hullVolume;
    this.fillFraction = fill;
    if(fill >= 1){
      console.warn('[' + this.id + '] ' + this.tonnes + ' t exceeds the buoyancy of ' +
        hullVolume.toFixed(0) + ' m³ of hull — she will sink. Reduce displacementTonnes.');
    }else if(fill > 0.75){
      console.warn('[' + this.id + '] very deeply laden: ' + (fill*100).toFixed(0) +
        '% of the hull volume displaced. Little reserve buoyancy.');
    }
    return fill;
  }
};
