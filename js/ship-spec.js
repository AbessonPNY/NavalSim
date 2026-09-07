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
    this.sternPower = json.engine.sternPower;

    // rudder
    this.rudderK = json.rudder.power * lateralArea;
    this.rudderMax = json.rudder.maxAngle;
    this.rudderZ = json.rudder.postZFrac * this.L;
    this.rudderY = json.rudder.postY;

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
