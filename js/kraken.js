/* The kraken.
 *
 * It lives in the depressions and nowhere else. A vessel that lingers in the
 * heart of one is found: first something dark rolling in the swell a quarter
 * of a mile off, arms breaking the surface and going down again; then, if she
 * stays, it closes; and at a cable it takes hold. Speed keeps it off, leaving
 * the storm sends it down, and enough shot sends it down too. It does not
 * sink her — it holds her, heels her, tears her canvas and her masts.
 *
 * NOTHING HERE IS A NEW KIND OF PHYSICS. An arm that has hold of her is a
 * mooring line whose far end is a point in deep water: a spring, capped at
 * what the animal can hold, and when she pulls harder the point gives and the
 * beast is dragged — the anchor's own rule. So the drag, the heel and the
 * sluggish helm all come out of the solver as it stands, from ShipPhysics.grips.
 *
 * Drawn, not loaded: each arm is a tapered tube laid on a centreline that is
 * rebuilt every frame — an arch rolling out of the sea while it lurks, a reach
 * from under her side up to a mast or the rail, and a few turns round it, when
 * it grips. The mantle is a dark dome with two pale eyes. Everything is in
 * LOCAL metres except the grip points, which are world points like any other
 * mooring, so a rebase moves the drawing and leaves the lines alone.
 */
window.Naval = window.Naval || {};

/* Game rules, overridden by settings.json → storm.kraken. */
Naval.KRAKEN = {
  enabled: true,
  minInten: 0.3,          // how deep in the depression before it stirs
  appearAfter: 60,        // seconds spent that deep before it may show
  appearPerMinute: 0.6,   // chance a minute, once that time is up
  lurkFar: 450,           // where it shows itself, metres
  lingerBefore: 45,       // seconds it keeps its distance
  approachRate: 1.2,      // metres a second it closes after that...
  approachGrow: 0.03,     // ...and faster the longer she stays (m/s per s)
  fleeKnots: 7,           // faster than this and it falls behind
  retreatRate: 4,         // metres a second it loses when she runs
  giveUpAt: 800,          // and past this it goes down
  gripAt: 45,             // metres at which it takes hold
  arms: 6,                // arms that take hold
  gripHold: 0.03,         // what one arm holds, in her own weight (0.07 laid a galleon over to 44°)
  gripStretch: 2.5,       // metres of give before an arm holds her full weight
  gripBrake: 0.25,        // per arm, per second: what it takes off her way (six hold a galleon to ~2 kn)
  damageEvery: 9,         // seconds between an arm tearing something
  hp: 14,                 // shot it takes (a full-size ball is ~1, the mantle counts double)
  cooldown: 900           // seconds before another may come
};

(function(){
const RINGS = 30, SIDES = 7, MAXARMS = 8;

const _v = new THREE.Vector3(), _w = new THREE.Vector3(), _u = new THREE.Vector3();
const _T = new THREE.Vector3(), _N = new THREE.Vector3(), _B = new THREE.Vector3();

Naval.Kraken = class Kraken {
  constructor(scene, oceanUniforms){
    this.group = new THREE.Group();
    this.group.visible = false;
    scene.add(this.group);

    // wet, dark, and lit like everything else that reflects
    this.mat = new THREE.MeshStandardMaterial({ color:0x4a1b24, roughness:0.34, metalness:0.04 });
    if(oceanUniforms) Naval.applyHaze(this.mat, oceanUniforms);

    this.head = new THREE.Group();
    const mantle = new THREE.Mesh(new THREE.SphereGeometry(1, 22, 14), this.mat);
    mantle.scale.set(5.5, 3.4, 8.5);
    this.head.add(mantle);
    // the eyes GLOW a little — the one thing on it that does not follow the light
    const eyeMat = new THREE.MeshBasicMaterial({ color:0xd9b44a, fog:false });
    for(const sx of [-1, 1]){
      const e = new THREE.Mesh(new THREE.SphereGeometry(0.5, 10, 8), eyeMat);
      e.position.set(sx*3.4, 1.1, 5.9);
      this.head.add(e);
    }
    this.group.add(this.head);

    this.arms = [];
    for(let i=0;i<MAXARMS;i++) this.arms.push(this._makeArm(i));

    this.state = 'absent';
    this.pos = new THREE.Vector3();      // the mantle, local
    this.bearing = 0; this.dist = 0; this.hp = 0;
    this.stormTime = 0; this.stateT = 0; this.cool = 0; this.dmgT = 0;
    this.ph = null; this.ship = null; this.ctx = null;
    this.hits = 0;
    this.roll = 0;
  }

  /* ------------------------------------------------------------ drawing */

  _makeArm(i){
    const g = new THREE.BufferGeometry();
    const pos = new Float32Array(RINGS*SIDES*3);
    g.setAttribute('position', new THREE.BufferAttribute(pos, 3));
    const idx = [];
    for(let r=0;r<RINGS-1;r++) for(let s=0;s<SIDES;s++){
      const a = r*SIDES + s, b = r*SIDES + (s+1)%SIDES;
      const c = a + SIDES, d = b + SIDES;
      idx.push(a, c, b,  b, c, d);
    }
    g.setIndex(idx);
    const mesh = new THREE.Mesh(g, this.mat);
    mesh.frustumCulled = false;       // rebuilt every frame; its bounds would lag
    mesh.visible = false;
    this.group.add(mesh);
    return {
      i, mesh, g, pos,
      C: Array.from({ length:RINGS }, () => new THREE.Vector3()),
      on:false, grow:0, want:0, wait:0, seed:Math.random()*100,
      rad: 0.85 + Math.random()*0.25,
      // lurking
      ang:0,
      // gripping: an anchor on her (local to her), a base in the water (world)
      anchor:null, base:new THREE.Vector3(), line:null, recoil:0, splashed:false
    };
  }

  /* A tube along the centreline, parallel-transported so it never twists. */
  _writeArm(a){
    const C = a.C, P = a.pos;
    _N.set(0, 1, 0);
    for(let r=0;r<RINGS;r++){
      const p0 = C[Math.max(0, r-1)], p1 = C[Math.min(RINGS-1, r+1)];
      _T.subVectors(p1, p0);
      if(_T.lengthSq() < 1e-8) _T.set(0, 1, 0); else _T.normalize();
      if(r === 0){
        _N.set(0, 1, 0);
        if(Math.abs(_T.y) > 0.9) _N.set(1, 0, 0);
      }
      _N.addScaledVector(_T, -_N.dot(_T)).normalize();
      _B.crossVectors(_T, _N);
      const u = r/(RINGS - 1);
      const rad = a.rad*(1 - 0.93*Math.pow(u, 0.85));
      for(let s=0;s<SIDES;s++){
        const ang = s/SIDES*Math.PI*2, cs = Math.cos(ang)*rad, sn = Math.sin(ang)*rad;
        const k = (r*SIDES + s)*3;
        P[k]   = C[r].x + _N.x*cs + _B.x*sn;
        P[k+1] = C[r].y + _N.y*cs + _B.y*sn;
        P[k+2] = C[r].z + _N.z*cs + _B.z*sn;
      }
    }
    a.g.attributes.position.needsUpdate = true;
    a.g.computeVertexNormals();
  }

  /* An arch rolling out of the sea beside the mantle, and back under. */
  _lurkLine(a, t, ocean){
    const e = Math.pow(Math.max(0, Math.sin(t*0.33 + a.seed)), 0.7);
    const ca = Math.cos(a.ang), sa = Math.sin(a.ang);
    const x0 = this.pos.x + ca*5, z0 = this.pos.z + sa*5;
    const span = 13, H = 7.5;
    for(let r=0;r<RINGS;r++){
      const s = r/(RINGS - 1);
      const wig = Math.sin(s*5 + t*1.3 + a.seed)*0.9*e;
      const x = x0 + ca*span*s - sa*wig, z = z0 + sa*span*s + ca*wig;
      const sea = ocean.sample(x, z, t);
      a.C[r].set(x, sea - 3 + (H + 3)*Math.sin(Math.PI*s)*e, z);
    }
    return e;
  }

  /* From the base under her side, up over the rail, and a few turns round the
     anchor. `grow` runs the arm out along that path, so it REACHES. */
  _gripLine(a, t, ocean, q, shipPos){
    const origin = ocean.origin;
    const base = _v.set(a.line ? a.line.wx - origin.x : a.base.x,
                        a.base.y,
                        a.line ? a.line.wz - origin.z : a.base.z);
    const A = _w.set(a.anchor.x, a.anchor.y, a.anchor.z).applyQuaternion(q).add(shipPos);
    const up = _u.set(0, 1, 0).applyQuaternion(q);
    // outward from her centreline, on the anchor's side
    const out = this._out.set(a.anchor.side, 0, 0).applyQuaternion(q);
    const fwd = this._fwd.set(0, 0, 1).applyQuaternion(q);
    const axis = a.anchor.mast ? up : fwd;
    const cr = a.anchor.mast ? 0.75 : 0.55;
    const P0 = base, P3 = this._p3.copy(A).addScaledVector(out, cr);
    // bowed well out and over, so it reads as a limb and not a pole
    const P1 = this._p1.copy(P0).addScaledVector(up, 5).addScaledVector(out, 4.5);
    const P2 = this._p2.copy(P3).addScaledVector(out, 4.5).addScaledVector(up, 4);
    const g = a.grow, SPLIT = 0.68;
    for(let r=0;r<RINGS;r++){
      const s = (r/(RINGS - 1))*g;
      const p = a.C[r];
      if(s <= SPLIT){
        const u = s/SPLIT, m = 1 - u;
        p.set(0, 0, 0)
         .addScaledVector(P0, m*m*m).addScaledVector(P1, 3*m*m*u)
         .addScaledVector(P2, 3*m*u*u).addScaledVector(P3, u*u*u);
        // alive: a slow writhe that dies away toward the hold
        const w = Math.sin(u*6 + t*1.7 + a.seed)*0.6*(1 - u);
        p.addScaledVector(fwd, w);
      }else{
        const phi = (s - SPLIT)/(1 - SPLIT)*Math.PI*3;
        const side = this._side.crossVectors(axis, out).normalize();
        p.copy(A)
         .addScaledVector(out, Math.cos(phi)*cr)
         .addScaledVector(side, Math.sin(phi)*cr)
         .addScaledVector(axis, (a.anchor.mast ? 0.35 : 0.25)*phi);
      }
    }
  }

  /* ----------------------------------------------------------- behaviour */

  /* ctx: { physics, ship, ocean, inten, t, say(msg), splash(pos, water, speed, jet),
            growl(pos), onMast(fallIndex), onSail() } — refreshed by the page. */
  update(dt, ctx){
    const K = Naval.KRAKEN;
    this.ctx = ctx;
    this.cool = Math.max(0, this.cool - dt);
    const ph = ctx.physics, b = ph.body;
    // the helm moved to another hull: it lets the old one go and sounds
    if(this.ph && this.ph !== ph && this.state !== 'absent' && this.state !== 'dive') this._dive('storm');
    const kn = Math.hypot(b.vel.x, b.vel.z)*1.944;

    switch(this.state){
      case 'absent':
        if(!K.enabled || this.cool > 0 || ph.foundered){ this.stormTime = 0; return; }
        if(ctx.inten > K.minInten) this.stormTime += dt;
        else this.stormTime = Math.max(0, this.stormTime - 2*dt);
        if(this.stormTime > K.appearAfter && Math.random() < K.appearPerMinute*dt/60) this.summon();
        if(this.state === 'absent') return;
        break;

      case 'lurk':
        this.stateT += dt;
        if(ctx.inten < K.minInten*0.5){ this._dive('storm'); break; }
        if(kn > K.fleeKnots) this.dist += K.retreatRate*dt;
        else if(this.stateT > K.lingerBefore){
          this.dist -= (K.approachRate + K.approachGrow*(this.stateT - K.lingerBefore))*dt;
          if(!this.warned){ this.warned = true; ctx.say('Le kraken se rapproche !'); }
        }
        if(this.dist > K.giveUpAt){ this._dive('flee'); break; }
        this.bearing += dt*0.025;
        if(this.dist <= K.gripAt) this._grip();
        break;

      case 'grip':
        this.stateT += dt;
        if(ctx.inten < K.minInten*0.5 || ph.foundered){ this._dive('storm'); break; }
        this.dmgT += dt;
        if(this.dmgT > K.damageEvery){ this.dmgT = 0; this._tear(); }
        break;

      case 'dive':
        this.stateT += dt;
        if(this.stateT > 7){
          this.state = 'absent';
          this.group.visible = false;
          for(const a of this.arms){ a.on = false; a.mesh.visible = false; }
          return;
        }
        break;
    }
    this._pose(dt, ctx);
  }

  /* It shows itself — at a distance, on her beam or thereabouts. Callable by
     hand for testing: summon(true) brings it straight alongside. */
  summon(close){
    const K = Naval.KRAKEN, c = this.ctx;
    if(!c || this.state !== 'absent' && this.state !== 'dive') return false;
    this._releaseAll();
    this.ph = c.physics; this.ship = c.ship;
    this.state = 'lurk'; this.stateT = 0; this.warned = false;
    this.hp = K.hp; this.hits = 0;
    this.roll = Math.random()*Math.PI*2;     // when its back comes up, this time
    this.dist = close ? K.gripAt : K.lurkFar;
    this.bearing = Math.random()*Math.PI*2;
    const b = this.ph.body;
    this.pos.set(b.pos.x + Math.sin(this.bearing)*this.dist, -8, b.pos.z + Math.cos(this.bearing)*this.dist);
    this.group.visible = true;
    const n = Math.max(2, Math.min(4, Math.round(K.arms/2)));
    this.arms.forEach((a, i) => {
      a.on = i < n; a.mesh.visible = a.on; a.anchor = null; a.line = null;
      a.ang = this.bearing + Math.PI + (i - (n - 1)/2)*0.9;
      a.grow = 0; a.want = 0;
    });
    c.say('Quelque chose d’énorme remue sous la houle…');
    c.growl(this.pos);
    if(close) this._grip();
    return true;
  }

  /* It takes hold: an arm to each mast still standing, the rest to the rail,
     each rising from under her side one after another. */
  _grip(){
    const K = Naval.KRAKEN, c = this.ctx, ship = this.ship, spec = ship.spec, b = this.ph.body;
    this.state = 'grip'; this.stateT = 0; this.dmgT = 0;
    const st = ship._deckStations();
    const tops = ship.mastTops().filter(m => !m.gone);
    const anchors = [];
    let side = Math.random() < 0.5 ? 1 : -1;
    for(const m of tops){
      const deck = st.deckNear(m.z);
      anchors.push({ x:0, y:deck + (m.y - deck)*0.3, z:m.z, mast:true, fall:m.fall, side, rail:deck + 0.6 });
      side = -side;
    }
    const rails = [0.28, -0.05, -0.32, 0.12, -0.2, 0.4];
    for(let k=0; anchors.length < K.arms && k < rails.length*2; k++){
      const z = rails[k % rails.length]*spec.L;
      anchors.push({ x:side*spec.B*0.42, y:st.deckNear(z) + 0.6, z, mast:false, fall:-1, side });
      side = -side;
    }
    const q = b.quat;
    this.arms.forEach((a, i) => {
      a.on = i < Math.min(K.arms, anchors.length);
      a.mesh.visible = a.on;
      a.anchor = a.on ? anchors[i] : null;
      a.line = null; a.recoil = 0; a.splashed = false;
      a.grow = 0; a.want = a.on ? 1 : 0; a.wait = i*0.45;
      if(!a.on) return;
      _v.set(a.anchor.side*(spec.B/2 + 3.5), 0, a.anchor.z + (Math.random() - 0.5)*4)
        .applyQuaternion(q).add(b.pos);
      a.base.set(_v.x, -7, _v.z);
    });
    this.dist = K.gripAt;
    c.say('Le kraken enlace le navire !');
    c.growl(this.pos);
  }

  /* An arm that has come up takes her: a line from its base to its anchor,
     with a little less length than it has, so it is taut at once. */
  _hold(a){
    const K = Naval.KRAKEN, b = this.ph.body, O = this.ctx.ocean.origin;
    /* THE PULL COMES ON AT THE RAIL, where the arm crosses it, even when the
       arm is drawn wound round a mast. Taken at the mast, a third of the way up,
       the outward pull had a lever of several metres and laid the galleon over
       to 36–48° for as long as it held — the capsize this creature is not meant
       to cause. At the rail it drags and heels her, and no more. */
    const rail = a.anchor.mast
      ? { x:a.anchor.side*this.ship.spec.B*0.42, y:a.anchor.rail, z:a.anchor.z }
      : a.anchor;
    const A = _w.set(rail.x, rail.y, rail.z).applyQuaternion(b.quat).add(b.pos);
    const d = Math.hypot(A.x - a.base.x, A.y - a.base.y, A.z - a.base.z);
    a.line = {
      lx:rail.x, ly:rail.y, lz:rail.z,
      wx:a.base.x + O.x, wy:a.base.y, wz:a.base.z + O.z,
      len:d*0.85, stretch:K.gripStretch,
      hold:K.gripHold*b.mass*this.ph.C.G, brake:K.gripBrake, kraken:true
    };
    this.ph.grips.push(a.line);
  }

  _let(a){
    if(!a.line) return;
    const G = this.ph ? this.ph.grips : null;
    if(G){ const i = G.indexOf(a.line); if(i >= 0) G.splice(i, 1); }
    // the base stays where the line had been dragged to
    const O = this.ctx.ocean.origin;
    a.base.x = a.line.wx - O.x; a.base.z = a.line.wz - O.z;
    a.line = null;
  }

  _releaseAll(){
    for(const a of this.arms) if(a.line) this._let(a);
    if(this.ph) this.ph.grips = this.ph.grips.filter(m => !m.kraken);
  }

  /* One arm does its damage: a mast it holds is wrenched, the rail's arms rip
     canvas. A mast already gone lets the arm go to find the rail. */
  _tear(){
    const holding = this.arms.filter(a => a.on && a.line);
    if(!holding.length) return;
    const a = holding[Math.floor(Math.random()*holding.length)];
    const f = a.anchor.fall >= 0 ? this.ship.falls[a.anchor.fall] : null;
    if(a.anchor.mast && f && !f.userData.fall) this.ctx.onMast(a.anchor.fall);
    else this.ctx.onSail();
    this.ctx.splash(this._w2(a), 6, 5, 1.2);
  }

  _w2(a){
    const b = this.ph.body;
    return new THREE.Vector3(a.anchor.x, a.anchor.y, a.anchor.z).applyQuaternion(b.quat).add(b.pos);
  }

  _dive(why){
    const c = this.ctx;
    this._releaseAll();
    this.state = 'dive'; this.stateT = 0;
    for(const a of this.arms) a.want = 0;
    this.cool = Naval.KRAKEN.cooldown;
    this.stormTime = 0;
    c.say(why === 'beaten' ? 'Touché ! Le kraken lâche prise et sombre dans les profondeurs.'
        : why === 'flee'   ? 'Le kraken renonce : vous l’avez distancé.'
        :                    'Le kraken regagne les profondeurs.');
  }

  /* ------------------------------------------------------------- posing */

  _pose(dt, c){
    this._out = this._out || new THREE.Vector3();
    this._fwd = this._fwd || new THREE.Vector3();
    this._side = this._side || new THREE.Vector3();
    this._p1 = this._p1 || new THREE.Vector3();
    this._p2 = this._p2 || new THREE.Vector3();
    this._p3 = this._p3 || new THREE.Vector3();
    const b = this.ph.body, ocean = c.ocean, t = c.t;
    const K = Naval.KRAKEN;

    // --- the mantle ---
    let tx, tz, ty;
    if(this.state === 'grip'){
      // beside her, low, on the side most of it holds
      let sx = 0, sz = 0, n = 0;
      for(const a of this.arms) if(a.on){
        const bx = a.line ? a.line.wx - ocean.origin.x : a.base.x;
        const bz = a.line ? a.line.wz - ocean.origin.z : a.base.z;
        sx += bx; sz += bz; n++;
      }
      tx = n ? sx/n : b.pos.x; tz = n ? sz/n : b.pos.z;
      const dx = tx - b.pos.x, dz = tz - b.pos.z, d = Math.hypot(dx, dz) || 1;
      tx = b.pos.x + dx/d*Math.max(d, this.ship.spec.B*0.5 + 9);
      tz = b.pos.z + dz/d*Math.max(d, this.ship.spec.B*0.5 + 9);
      ty = ocean.sample(tx, tz, t) - 3.0;   // a hump, not a floating dish
    }else if(this.state === 'dive'){
      tx = this.pos.x; tz = this.pos.z;
      ty = this.pos.y - 3*dt;              // going down, three metres a second
    }else{
      tx = b.pos.x + Math.sin(this.bearing)*this.dist;
      tz = b.pos.z + Math.cos(this.bearing)*this.dist;
      // it rolls up now and then, never quite clear of the water
      const surf = Math.max(0, Math.sin(t*0.21 + this.roll));
      ty = ocean.sample(tx, tz, t) - 1.8 - 4.5*(1 - surf);
    }
    const k = Math.min(1, dt*(this.state === 'lurk' ? 0.6 : 1.5));
    this.pos.x += (tx - this.pos.x)*k;
    this.pos.z += (tz - this.pos.z)*k;
    this.pos.y = this.state === 'dive' ? ty : this.pos.y + (ty - this.pos.y)*Math.min(1, dt*1.2);
    this.head.position.copy(this.pos);
    _v.set(b.pos.x, this.pos.y, b.pos.z);
    this.head.lookAt(_v);

    // --- the arms ---
    for(const a of this.arms){
      if(!a.on){ a.mesh.visible = false; continue; }
      a.mesh.visible = true;
      if(this.state === 'lurk'){
        a.ang += dt*0.04;
        this._lurkLine(a, t, ocean);
        this._writeArm(a);
        continue;
      }
      if(!a.anchor){               // lurking arms caught by a dive
        this._lurkLine(a, t, ocean);
        for(const p of a.C) p.y -= this.stateT*2;
        this._writeArm(a);
        continue;
      }
      if(a.wait > 0){ a.wait -= dt; }
      else{
        if(a.recoil > 0){
          a.recoil -= dt;
          a.want = 0;
          if(a.recoil <= 0 && this.state === 'grip') a.want = 1;
        }
        const rate = a.want > a.grow ? 0.55 : 1.1;
        a.grow += Math.max(-rate*dt, Math.min(rate*dt, a.want - a.grow));
      }
      if(a.grow > 0.05 && !a.splashed){
        a.splashed = true;
        c.splash(_v.set(a.base.x, ocean.sample(a.base.x, a.base.z, t), a.base.z).clone(), 14, 6, 1.6);
      }
      if(a.grow < 0.02) a.splashed = false;
      if(this.state === 'grip' && !a.line && a.grow > 0.9 && a.recoil <= 0){
        // the mast it was going for may have gone over the side meanwhile
        const f = a.anchor.fall >= 0 ? this.ship.falls[a.anchor.fall] : null;
        if(a.anchor.mast && f && f.userData.fall){
          a.anchor = { x:a.anchor.side*this.ship.spec.B*0.42, y:a.anchor.y*0.4, z:a.anchor.z,
                       mast:false, fall:-1, side:a.anchor.side };
        }
        this._hold(a);
      }
      if(a.line && (a.grow < 0.8 || this.state !== 'grip')) this._let(a);
      this._gripLine(a, t, ocean, b.quat, b.pos);
      if(this.state === 'dive') for(const p of a.C) p.y -= this.stateT*1.5*(1 - a.grow);
      this._writeArm(a);
    }
  }

  /* ------------------------------------------------------------ gunnery */

  /* The segment a ball swept, against the arms (as a chain of spheres) and the
     mantle. The first thing it meets is what it hits. */
  hitShot(p0, p1){
    if(this.state === 'absent' || this.state === 'dive') return null;
    let best = null, bu = Infinity;
    const test = (c, r, part, arm) => {
      const u = segSphere(p0, p1, c, r);
      if(u >= 0 && u < bu){ bu = u; best = { part, arm }; }
    };
    test(this.pos, 5.5, 'body', null);
    for(const a of this.arms){
      if(!a.on || !a.mesh.visible) continue;
      for(let r=0;r<RINGS;r+=3) test(a.C[r], a.rad*(1 - 0.9*r/RINGS) + 0.6, 'arm', a);
    }
    if(!best) return null;
    best.point = new THREE.Vector3().lerpVectors(p0, p1, bu);
    return best;
  }

  /* What a hit costs it. `k` is the calibre, ~1 for a full-sized gun. */
  wound(hit, k){
    if(this.state === 'absent' || this.state === 'dive') return;
    this.hp -= (hit.part === 'body' ? 2 : 1)*(k || 1);
    this.hits++;
    if(hit.arm){
      // the arm struck lets go and comes back for her a little later
      hit.arm.recoil = 5 + Math.random()*3;
    }else if(this.state === 'lurk'){
      this.dist += 40;     // flinches away
    }
    if(this.hp <= 0) this._dive('beaten');
  }

  rebase(dx, dz){
    this.pos.x -= dx; this.pos.z -= dz;
    for(const a of this.arms){
      a.base.x -= dx; a.base.z -= dz;
      for(const p of a.C){ p.x -= dx; p.z -= dz; }
    }
  }
};

/* Where along p0→p1 (0..1) the segment first enters the sphere, or -1. */
function segSphere(p0, p1, c, r){
  const dx = p1.x - p0.x, dy = p1.y - p0.y, dz = p1.z - p0.z;
  const fx = p0.x - c.x, fy = p0.y - c.y, fz = p0.z - c.z;
  const A = dx*dx + dy*dy + dz*dz;
  if(A < 1e-12) return -1;
  const B = 2*(fx*dx + fy*dy + fz*dz), Cc = fx*fx + fy*fy + fz*fz - r*r;
  if(Cc <= 0) return 0;
  const D = B*B - 4*A*Cc;
  if(D < 0) return -1;
  const u = (-B - Math.sqrt(D))/(2*A);
  return u >= 0 && u <= 1 ? u : -1;
}
})();
