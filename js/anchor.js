/* Mouiller, et lever l'ancre.
 *
 * AN ANCHOR ON THE BOTTOM IS A MOORING WHOSE BOLLARD CAN MOVE. The berth
 * already holds a ship with a spring that pulls and never pushes, taken to a
 * point in true world metres; lying to an anchor is that line taken to a point
 * on the sea bed, with two differences the solver now carries per line: a long
 * cable is a softer spring than a short warp, and the far end GIVES when it is
 * pulled harder than the anchor can resist. Nothing here pushes the hull.
 *
 * What this file owns is everything the solver does not need to know: the
 * anchor falling from the cathead, the splash, its slow fall through the water,
 * the cable streaming out behind it, and the capstan bringing it home.
 *
 * The HOLDING is stated, not tuned. A bower weighed something like four
 * thousandths of her displacement — a tonne for a 240-ton galleon, four for a
 * ship of the line, which is what they carried — and holds some eight times its
 * weight when it is lying right. It lies right only with SCOPE: the cable must
 * pull along the bottom, not up, so the holding falls away as the cable
 * shortens toward the depth. At the pier-side three to one it bites; in
 * seventy metres of open sea with a hundred and eighty of cable it barely does,
 * and a ship lying there in a breeze drags. That is the real reason ships
 * anchored in roads and not in the ocean, and it comes out of the arithmetic.
 */
window.Naval = window.Naval || {};

Naval.Anchors = class Anchors {
  constructor(scene, stage, ocean, world, splash, cordage){
    this.scene = scene; this.stage = stage; this.ocean = ocean; this.world = world;
    this.splash = splash; this.cordage = cordage;
    this.items = new Map();          // physics → what her anchor is doing
    this.onSay = null;
    this.N = 28;                     // points along the cable
    this._v = new THREE.Vector3(); this._w = new THREE.Vector3();
    this._q = new THREE.Quaternion(); this._up = new THREE.Vector3(0, 1, 0);
  }

  _say(m){ if(this.onSay) this.onSay(m); }

  /* Where the anchor hangs and the cable leaves her: the CATHEAD, on the bow,
     to starboard, just under the forecastle rail. Read off the model's own hull
     where there is one, since that is where the eye looks for it; a procedural
     hull IS her solver lines. Starboard is local −x (see the handedness
     invariant).

     OUTBOARD of the planking, which is the whole point of a cathead — it
     carries the anchor clear of the side. First written at half the beam, the
     anchor was let go INSIDE her hull and fell unseen through her own bottom:
     the numbers were all right and nothing was on the screen. */
  _hawse(e){
    const ph = e.physics, S = ph.spec, shell = e.ship && e.ship.shell;
    if(shell){
      const b = shell[shell.length - 1];
      return { x:-(b.half + 0.4 + 0.02*S.L), y:b.deck - 0.8, z:b.z0 + 0.45*(b.z1 - b.z0) };
    }
    const c = ph.comps[ph.comps.length - 1];
    return { x:-(c.halfB + 0.4 + 0.02*S.L), y:c.deckY - 0.4, z:c.mid.z };
  }

  _item(e){
    const ph = e.physics;
    let it = this.items.get(ph);
    if(it) return it;
    const S = ph.spec;
    const size = Math.max(1.2, Math.min(5, 0.09*S.L));
    it = {
      e, ph, state:'stowed',
      mass: Math.max(40, 0.004*S.massKg),
      size,
      cableMax: Math.min(220, 6*S.L),
      p: new THREE.Vector3(), v: new THREE.Vector3(),
      moor: null, floorShip: -S.D, floorAt: 0, tick: 0,
      mesh: this._anchorMesh(size),
      cable: this._cableMesh(Math.max(0.10, Math.min(0.32, 0.006*S.L))),
      px: new Float32Array(this.N), py: new Float32Array(this.N), pz: new Float32Array(this.N),
      ox: new Float32Array(this.N), oy: new Float32Array(this.N), oz: new Float32Array(this.N),
      acc: 0
    };
    it.mesh.visible = it.cable.visible = false;
    this.scene.add(it.mesh); this.scene.add(it.cable);
    this.items.set(ph, it);
    return it;
  }

  /* An Admiralty-pattern bower, drawn in its own proportions: shank, ring,
     a wooden stock across the head at right angles to the arms, and two curved
     arms ending in flukes. The stock is what makes it read as an anchor from any
     side — without it, it is a grapnel. */
  _anchorMesh(s){
    const iron = new THREE.MeshStandardMaterial({ color:0x24211e, roughness:0.55, metalness:0.6 });
    const wood = new THREE.MeshStandardMaterial({ color:0x4a3827, roughness:0.85 });
    if(this.ocean){ Naval.applyHaze(iron, this.ocean.uniforms); Naval.applyHaze(wood, this.ocean.uniforms); }
    const g = new THREE.Group();
    const add = (geo, mat, x, y, z, rx, ry, rz) => {
      const m = new THREE.Mesh(geo, mat);
      m.position.set(x, y, z); m.rotation.set(rx || 0, ry || 0, rz || 0);
      m.castShadow = true;
      /* On the ship's layer as well as the default one, so the pass that shows
         a hull through the water shows the anchor going down through it too. */
      m.layers.enable(Naval.SHIP_LAYER);
      g.add(m);
    };
    add(new THREE.CylinderGeometry(0.035*s, 0.045*s, s, 8), iron, 0, s*0.5, 0);
    add(new THREE.TorusGeometry(0.09*s, 0.018*s, 6, 14), iron, 0, s*1.07, 0);
    add(new THREE.BoxGeometry(0.08*s, 0.08*s, 0.95*s), wood, 0, s*0.86, 0);
    const R = 0.42*s, spread = 0.95;
    add(new THREE.TorusGeometry(R, 0.032*s, 6, 16, 2*spread), iron, 0, R, 0, 0, 0, -Math.PI/2 - spread);
    for(const sg of [-1, 1]){
      const a = spread*sg;
      add(new THREE.BoxGeometry(0.22*s, 0.30*s, 0.03*s), iron,
          Math.sin(a)*R*1.02, R - Math.cos(a)*R + 0.12*s, 0, 0, 0, -a*0.8);
    }
    return g;
  }

  /* The cable is drawn with the SAME ribbon shader as the cut rigging — one
     definition of what a rope looks like — with its own width, because an
     anchor cable is a hawser and not a brace. The uniforms are the rigging's
     own objects save the width, so the haze and the light are shared, not
     copied. */
  _cableMesh(width){
    const N = this.N, V = N*2;
    const g = new THREE.BufferGeometry();
    g.setAttribute('position', new THREE.BufferAttribute(new Float32Array(V*3), 3).setUsage(THREE.DynamicDrawUsage));
    g.setAttribute('tang', new THREE.BufferAttribute(new Float32Array(V*3), 3).setUsage(THREE.DynamicDrawUsage));
    const side = new Float32Array(V), fade = new Float32Array(V).fill(1);
    for(let j = 0; j < N; j++){ side[j*2] = -1; side[j*2 + 1] = 1; }
    g.setAttribute('side', new THREE.BufferAttribute(side, 1));
    g.setAttribute('fade', new THREE.BufferAttribute(fade, 1));
    const idx = new Uint16Array((N - 1)*6);
    for(let j = 0; j < N - 1; j++){
      const a = j*2, k = j*6;
      idx[k] = a; idx[k+1] = a+1; idx[k+2] = a+2; idx[k+3] = a+1; idx[k+4] = a+3; idx[k+5] = a+2;
    }
    g.setIndex(new THREE.BufferAttribute(idx, 1));
    const base = this.cordage.mat;
    const mat = new THREE.ShaderMaterial({
      uniforms: Object.assign({}, this.cordage.uniforms, { uWidth:{value:width} }),
      vertexShader: base.vertexShader, fragmentShader: base.fragmentShader,
      // the same flag as the rigging, or the mirror takes the drowned cable whole
      transparent:true, depthWrite:false, side:THREE.DoubleSide, clipping: base.clipping
    });
    const m = new THREE.Mesh(g, mat);
    m.frustumCulled = false;
    m.renderOrder = 4;
    m.layers.enable(Naval.SHIP_LAYER);
    return m;
  }

  state(e){ const it = this.items.get(e.physics); return it ? it.state : 'stowed'; }

  /* One key for the ground tackle: let go if she has it aboard, heave in if
     it is out. Returns what the bosun would say. */
  toggle(e){
    const it = this._item(e);
    if(it.state === 'stowed') return this._drop(it);
    if(it.state === 'weigh' || it.state === 'up') return 'On vire déjà au cabestan';
    return this._weigh(it);
  }

  _hawseWorld(it, out){
    const h = this._hawse(it.e), b = it.ph.body;
    return out.set(h.x, h.y, h.z).applyQuaternion(b.quat).add(b.pos);
  }

  _drop(it){
    const b = it.ph.body;
    this._hawseWorld(it, it.p);
    it.v.copy(b.vel);
    it.state = 'air';
    for(let j = 0; j < this.N; j++){
      it.px[j] = it.ox[j] = it.p.x; it.py[j] = it.oy[j] = it.p.y; it.pz[j] = it.oz[j] = it.p.z;
    }
    it.mesh.visible = it.cable.visible = true;
    const o = this.ocean.origin;
    it.floorShip = this.world.heightAt(b.pos.x + o.x, b.pos.z + o.z);
    return 'Mouillez !';
  }

  _weigh(it){
    it.state = 'weigh';
    return 'Virez au cabestan';
  }

  _stow(it){
    it.state = 'stowed';
    if(it.moor){ const M = it.ph.moorings, i = M.indexOf(it.moor); if(i >= 0) M.splice(i, 1); }
    it.moor = null;
    it.mesh.visible = it.cable.visible = false;
  }

  rebase(dx, dz){
    for(const it of this.items.values()){
      it.p.x -= dx; it.p.z -= dz;
      for(let j = 0; j < this.N; j++){ it.px[j] -= dx; it.ox[j] -= dx; it.pz[j] -= dz; it.oz[j] -= dz; }
    }
  }

  update(dt, fleet, t){
    if(dt <= 0) return;
    // a hull that has left the fleet takes her anchor and its cable with her
    for(const [ph, it] of this.items){
      if(!fleet.some(e => e.physics === ph)){
        this.scene.remove(it.mesh); this.scene.remove(it.cable);
        this.items.delete(ph);
      }
    }
    for(const it of this.items.values()){
      if(it.state === 'stowed') continue;
      this._step(it, Math.min(dt, 0.25), t);
      this._cable(it, dt, t);
      this._pose(it);
    }
  }

  _step(it, dt, t){
    const ph = it.ph, b = ph.body, o = this.ocean.origin, G = 9.81;
    const hawse = this._hawseWorld(it, this._w);
    const sea = this.ocean.sample(it.p.x, it.p.z, t);

    if(it.state === 'air' || it.state === 'water' || it.state === 'hang'){
      const n = Math.max(1, Math.ceil(dt*60)), h = dt/n;
      for(let k = 0; k < n; k++){
        if(it.p.y > sea){
          it.v.y -= G*h;
        }else{
          if(it.state === 'air'){
            it.state = 'water';
            this._at = this._at || new THREE.Vector3();
            this._at.set(it.p.x, sea, it.p.z);
            /* A tonne of iron at eight metres a second is nearer a cannonball
               than a hull settling, so it takes the column and not the crown. */
            this.splash.burst(this._at, 3 + it.mass/150, Math.abs(it.v.y), 1.6);
          }
          /* Iron with a wooden stock falls through water at a few metres a
             second, and the stock keeps it from tumbling: relaxing toward that
             speed, and bleeding off whatever way it had from the ship. */
          const vt = 2.6 + 0.4*Math.log10(it.mass/100 + 1);
          it.v.y += (-vt - it.v.y)*Math.min(1, h/0.35);
          it.v.x *= Math.max(0, 1 - h/0.8); it.v.z *= Math.max(0, 1 - h/0.8);
        }
        it.p.addScaledVector(it.v, h);
        // the cable runs out freely until there is no more of it
        const d = it.p.distanceTo(hawse);
        if(d > it.cableMax){
          it.p.sub(hawse).multiplyScalar(it.cableMax/d).add(hawse);
          it.v.multiplyScalar(0.5);
          if(it.state !== 'hang'){ it.state = 'hang'; this._say('Tout le câble est dehors — elle ne touche pas le fond'); }
        }
      }
      const floor = this.world.heightAt(it.p.x + o.x, it.p.z + o.z);
      if(it.p.y <= floor + 0.2) this._bottom(it, floor, hawse, sea);
      return;
    }

    if(it.state === 'down' || it.state === 'weigh'){
      const m = it.moor;
      it.p.set(m.wx - o.x, m.wy, m.wz - o.z);
      if(m.dragging){
        it.tick += dt;
        m.wy = this.world.heightAt(m.wx, m.wz) + 0.2;
        /* Not while heaving in: the capstan pulls past the holding on purpose,
           and "she drags" is the right warning at anchor and the wrong one
           when it is the crew bringing her home. */
        if(it.tick > 6 && it.state === 'down'){ it.tick = 0; this._say('L’ancre chasse !'); }
      }
      if(it.state === 'weigh'){
        /* THE CAPSTAN brings in cable and the ship comes to it, because the
           spring is the same one: shorten the line and she is hauled up over
           her anchor. It breaks out once the cable is up and down. */
        m.len = Math.max(0, m.len - 1.2*dt);
        const upDown = hawse.y - m.wy;
        if(m.len <= upDown + 1.5){
          const M = ph.moorings, i = M.indexOf(m); if(i >= 0) M.splice(i, 1);
          it.moor = null;
          it.state = 'up';
          this._say('L’ancre dérape');
        }
      }
      return;
    }

    if(it.state === 'up'){
      this._v.copy(hawse).sub(it.p);
      const d = this._v.length();
      if(d < 0.6){ this._stow(it); this._say('Ancre haute et bossée'); return; }
      it.p.addScaledVector(this._v, Math.min(1, 1.2*dt/d));
    }
  }

  /* On the bottom. The crew veer cable to a proper scope before they belay —
     three and a half times the depth if they have it — and that is the length
     the mooring is given. The holding follows from the scope they got. */
  _bottom(it, floor, hawse, sea){
    const o = this.ocean.origin, depth = Math.max(1, sea - floor);
    const reach = it.p.distanceTo(hawse);
    const len = Math.min(it.cableMax, Math.max(reach*1.02, 3.5*depth));
    const scope = len/depth;
    const hold = it.mass*9.81*8*Math.max(0.15, Math.min(1, (scope - 1)/4));
    it.moor = {
      anchor:true,
      lx:0, ly:0, lz:0,
      wx:it.p.x + o.x, wy:floor + 0.2, wz:it.p.z + o.z,
      len, hold,
      // a long cable takes her weight over a long stretch: it snubs, it does not jerk
      stretch: Math.max(0.8, Math.min(12, 0.06*len))
    };
    const h = this._hawse(it.e);
    it.moor.lx = h.x; it.moor.ly = h.y; it.moor.lz = h.z;
    it.ph.moorings.push(it.moor);
    it.state = 'down';
    it.v.set(0, 0, 0);
    const o2 = this.ocean.origin, b = it.ph.body;
    it.floorShip = this.world.heightAt(b.pos.x + o2.x, b.pos.z + o2.z);
    this._say('Ancre au fond par ' + Math.round(depth) + ' m · ' + Math.round(len) + ' m de câble'
              + (scope < 3 ? ' — trop peu pour bien tenir' : ''));
  }

  /* The cable: a Verlet chain pinned at the hawse and at the ring, heavy in air,
     nearly weightless and dragged hard in water, and never through the bottom. */
  _cable(it, dt, t){
    const N = this.N, H = 1/60;
    const hawse = this._hawseWorld(it, this._w);
    const ring = this._ring(it, this._v);
    const reach = hawse.distanceTo(ring);
    const len = it.moor ? Math.max(it.moor.len, reach) : reach + 0.5;
    const seg = Math.max(len, reach)/(N - 1);
    const sea = this.ocean.sample(hawse.x, hawse.z, t);
    const o = this.ocean.origin, b = it.ph.body;

    it.tick2 = (it.tick2 || 0) + dt;
    if(it.tick2 > 0.5){
      it.tick2 = 0;
      it.floorShip = this.world.heightAt(b.pos.x + o.x, b.pos.z + o.z);
      it.floorAt = this.world.heightAt(ring.x + o.x, ring.z + o.z);
    }

    it.acc = Math.min(it.acc + dt, 4*H);
    while(it.acc >= H){
      it.acc -= H;
      for(let j = 1; j < N - 1; j++){
        const wet = it.py[j] < sea;
        const g = wet ? 1.8 : 9.81, c = wet ? 4.0 : 0.3;
        const vx = (it.px[j] - it.ox[j])/H, vy = (it.py[j] - it.oy[j])/H, vz = (it.pz[j] - it.oz[j])/H;
        const nx = it.px[j] + (it.px[j] - it.ox[j]) - c*vx*H*H;
        const ny = it.py[j] + (it.py[j] - it.oy[j]) + (-g - c*vy)*H*H;
        const nz = it.pz[j] + (it.pz[j] - it.oz[j]) - c*vz*H*H;
        it.ox[j] = it.px[j]; it.oy[j] = it.py[j]; it.oz[j] = it.pz[j];
        it.px[j] = nx; it.py[j] = ny; it.pz[j] = nz;
      }
      it.px[0] = it.ox[0] = hawse.x; it.py[0] = it.oy[0] = hawse.y; it.pz[0] = it.oz[0] = hawse.z;
      const L = N - 1;
      it.px[L] = it.ox[L] = ring.x; it.py[L] = it.oy[L] = ring.y; it.pz[L] = it.oz[L] = ring.z;
      for(let k = 0; k < 14; k++){
        for(let j = 0; j < N - 1; j++){
          const a = j, c2 = j + 1;
          let dx = it.px[c2] - it.px[a], dy = it.py[c2] - it.py[a], dz = it.pz[c2] - it.pz[a];
          const d = Math.sqrt(dx*dx + dy*dy + dz*dz) || 1e-6;
          const f = (d - seg)/d*0.5;
          dx *= f; dy *= f; dz *= f;
          const pinA = a === 0, pinC = c2 === L;
          if(!pinA && !pinC){ it.px[a] += dx; it.py[a] += dy; it.pz[a] += dz; it.px[c2] -= dx; it.py[c2] -= dy; it.pz[c2] -= dz; }
          else if(pinA && !pinC){ it.px[c2] -= 2*dx; it.py[c2] -= 2*dy; it.pz[c2] -= 2*dz; }
          else if(pinC && !pinA){ it.px[a] += 2*dx; it.py[a] += 2*dy; it.pz[a] += 2*dz; }
        }
        // the bottom, as a straight line from under her to under the anchor
        for(let j = 1; j < N - 1; j++){
          const floor = it.floorShip + (it.floorAt - it.floorShip)*j/L + 0.15;
          if(it.py[j] < floor){ it.py[j] = floor; it.oy[j] = floor; }
        }
      }
    }

    const pos = it.cable.geometry.attributes.position.array, tan = it.cable.geometry.attributes.tang.array;
    for(let j = 0; j < N; j++){
      const o1 = j > 0 ? j - 1 : j, c3 = j < N - 1 ? j + 1 : j;
      let tx = it.px[c3] - it.px[o1], ty = it.py[c3] - it.py[o1], tz = it.pz[c3] - it.pz[o1];
      const tl = Math.hypot(tx, ty, tz) || 1; tx /= tl; ty /= tl; tz /= tl;
      for(let s = 0; s < 2; s++){
        const q = (j*2 + s)*3;
        pos[q] = it.px[j]; pos[q+1] = it.py[j]; pos[q+2] = it.pz[j];
        tan[q] = tx; tan[q+1] = ty; tan[q+2] = tz;
      }
    }
    it.cable.geometry.attributes.position.needsUpdate = true;
    it.cable.geometry.attributes.tang.needsUpdate = true;
    // the rigging's shader reads its pixel scale off a uniform the rigging keeps fresh
  }

  // the ring, where the cable is bent on: at the top of the shank, as posed
  _ring(it, out){
    return out.set(0, it.size*1.07, 0).applyQuaternion(it.mesh.quaternion).add(it.p);
  }

  /* Falling, it hangs from its ring with the stock across; on the bottom it
     lies over on one arm, shank laid toward the cable. */
  _pose(it){
    const N = this.N;
    const j = N - 3;
    this._v.set(it.px[j] - it.p.x, it.py[j] - it.p.y, it.pz[j] - it.p.z);
    if(this._v.lengthSq() < 1e-6) this._v.set(0, 1, 0);
    this._v.normalize();
    if(it.state === 'down' || it.state === 'weigh'){
      this._v.y = 0.25; this._v.normalize();     // lying along the bottom, ring lifted a little
    }
    this._q.setFromUnitVectors(this._up, this._v);
    it.mesh.quaternion.slerp(this._q, 0.25);
    it.mesh.position.copy(it.p);
  }
};
