/* Quests: a scenario in steps, read from quests/*.json.
 *
 * The open world stays as it is; a quest only lays a thread across it. Each
 * step names a place and what must be done there, and when it is done its
 * message is put on the screen and the next step begins. The format is in
 * quests/README.md.
 *
 * Places are named against the world: a port of the region (world/caraibes.json)
 * — its jetty, or a bearing and distance from it — or a real latitude and
 * longitude, which the reduced map turns into its own metres. Bare metres are
 * accepted too.
 *
 * The module holds the state and the rules; the page shows what it says
 * (onShow for a message, objective() for the line under the date and the ring
 * on the chart) and feeds it the ship's state each frame.
 */
window.Naval = window.Naval || {};

Naval.QUEST_GOALS = {
  // default radius (m) of each kind of objective
  reach: { radius: 300 },
  leave: { radius: 1852 },  // out of the circle: a mile clear of a harbour
  stop:  { radius: 250 },   // lying still there: under maxSpeed knots for `hold` seconds
  dock:  { radius: 450 }    // made fast there: moored or anchored
};

Naval.Quests = class Quests {
  constructor(world){
    this.world = world;
    this.list = [];
    this.active = null;       // the quest under way
    this.step = 0;
    this.hold = 0;            // seconds the current `stop` has been held
    this.done = new Set();    // ids of finished quests
    this.onShow = null;       // (title, text) — a message for the screen
    this.onChange = null;     // () — the objective has changed
    this._key = 'navalsim.quests';
  }

  /* Which quests exist: baked into the published page, listed by the dev
     server (or the index.json the build writes) otherwise. */
  static async discover(){
    if(Array.isArray(Naval.QUESTS_DATA)) return Naval.QUESTS_DATA;
    const out = [];
    try{
      const res = await fetch('quests/index.json', { cache:'no-store' });
      if(!res.ok) return out;
      for(const url of await res.json()){
        try{
          const r = await fetch(url, { cache:'no-store' });
          if(r.ok) out.push(await r.json());
        }catch(e){ console.warn('[quêtes] ' + url + ' illisible : ' + e.message); }
      }
    }catch(e){ /* no quests folder: free sailing only */ }
    return out;
  }

  load(list){
    for(const q of list || []){
      const why = Naval.Quests.check(q);
      if(why){ console.warn('[quêtes] ' + (q && q.id || '?') + ' ignorée : ' + why); continue; }
      this.list.push(q);
    }
  }

  static check(q){
    if(!q || typeof q !== 'object') return 'pas un objet';
    if(!q.id) return 'pas d’id';
    if(!Array.isArray(q.steps) || !q.steps.length) return 'aucune étape';
    for(let i = 0; i < q.steps.length; i++){
      const s = q.steps[i];
      if(!s || !s.at) return 'étape ' + (i + 1) + ' sans lieu (at)';
      if(s.goal && !Naval.QUEST_GOALS[s.goal]) return 'étape ' + (i + 1) + ' : objectif inconnu « ' + s.goal + ' »';
    }
    return null;
  }

  byId(id){ return this.list.find(q => q.id === id) || null; }

  /* A step's place, in true world metres: { x, z, r, name }. */
  place(step){
    const a = step.at, W = this.world, G = Naval.QUEST_GOALS[step.goal || 'reach'];
    let x = 0, z = 0, name = step.title || '';
    // "island" is the old name of the field, when every port was an island
    const key = typeof a.port === 'string' ? a.port : a.island;
    const isl = key ? W.byKey(key) : null;
    if(key && !isl) console.warn('[quêtes] port inconnu : ' + key);
    if(isl && !(a.bearing != null || a.miles != null || a.distance != null) && isl.port){
      /* The head of the jetty, out in the stream: where a ship is made fast.
         A bearing or a distance asks for an OFFSET from the port instead --
         which `a.port ||` used to swallow, so the documented form
         { port, bearing, miles } quietly gave the jetty. Found by porting. */
      x = isl.port.hx; z = isl.port.hz;
    }else if(isl){
      /* A bearing FROM the port, true degrees (0 north, 90 east — east is -x
         here), and a distance in miles of the MAP (or metres). */
      const b = (a.bearing || 0)*Math.PI/180;
      const dx = -Math.sin(b), dz = Math.cos(b);
      const d = a.miles != null ? a.miles*1852 : (a.distance || 0);
      x = isl.x + dx*d; z = isl.z + dz*d;
    }else if(a.lat != null && a.lon != null){
      ({ x, z } = Naval.Geo.toXZ(a.lat, a.lon));
    }else{
      x = a.x || 0; z = a.z || 0;
    }
    return { x, z, r: step.radius || G.radius, name };
  }

  start(id){
    const q = this.byId(id);
    this.active = q; this.step = 0; this.hold = 0;
    this._save();
    if(q){
      if(this.onShow) this.onShow(q.title || q.id, q.intro || q.summary || '');
      this._brief();
    }
    if(this.onChange) this.onChange();
  }

  stop(){
    this.active = null; this.step = 0; this.hold = 0;
    this._save();
    if(this.onChange) this.onChange();
  }

  current(){ return this.active ? this.active.steps[this.step] || null : null; }

  /* What the page shows: the step's title, its place, how far and which way. */
  objective(fromX, fromZ){
    const s = this.current();
    if(!s) return null;
    const p = this.place(s);
    const dx = p.x - fromX, dz = p.z - fromZ;
    const dist = Math.hypot(dx, dz);
    let brg = Math.atan2(-dx, dz)*180/Math.PI;          // east is -x
    if(brg < 0) brg += 360;
    return { quest: this.active, step: s, index: this.step, count: this.active.steps.length,
             x: p.x, z: p.z, r: p.r, dist, bearing: brg, hold: this.hold };
  }

  /* Each frame. `s` = { x, z (true metres), speed (m/s), moored, lost }. */
  update(dt, s){
    const step = this.current();
    if(!step || s.lost) return;
    const p = this.place(step);
    const inside = Math.hypot(p.x - s.x, p.z - s.z) <= p.r;
    const goal = step.goal || 'reach';
    let met = false;
    if(goal === 'reach') met = inside;
    else if(goal === 'leave') met = !inside;
    else if(goal === 'dock') met = inside && s.moored;
    else if(goal === 'stop'){
      const still = inside && s.speed <= (step.maxSpeed != null ? step.maxSpeed : 1)*0.5144;
      this.hold = still ? this.hold + dt : 0;
      met = this.hold >= (step.hold != null ? step.hold : 8);
    }
    if(met) this._advance();
  }

  _advance(){
    const q = this.active, s = this.current();
    if(this.onShow && s.message) this.onShow(s.title || '', s.message);
    this.step++;
    this.hold = 0;
    if(this.step >= q.steps.length){
      this.done.add(q.id);
      if(this.onShow && q.outro) this.onShow(q.title || q.id, q.outro);
      this.active = null; this.step = 0;
    }else{
      this._brief();
    }
    this._save();
    if(this.onChange) this.onChange();
  }

  _brief(){
    const s = this.current();
    if(s && s.brief && this.onShow) this.onShow(s.title || '', s.brief);
  }

  /* Where the player is in their quests survives a reload — in this browser
     only, which is all a sea log needs. */
  _save(){
    try{
      localStorage.setItem(this._key, JSON.stringify({
        active: this.active ? this.active.id : null, step: this.step, done: [...this.done] }));
    }catch(e){ /* private window: progress is simply not kept */ }
  }

  restore(){
    let saved = null;
    try{ saved = JSON.parse(localStorage.getItem(this._key) || 'null'); }catch(e){ saved = null; }
    if(!saved) return false;
    for(const id of saved.done || []) this.done.add(id);
    const q = saved.active && this.byId(saved.active);
    if(q && saved.step < q.steps.length){
      this.active = q; this.step = Math.max(0, saved.step | 0); this.hold = 0;
      if(this.onChange) this.onChange();
      return true;
    }
    return false;
  }
};
