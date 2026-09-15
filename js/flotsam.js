/* Ce qui remonte d'un navire, et ce qu'on en fait.
 *
 * A ship that goes down leaves something on the water: a plank or two, a
 * barrel, and now and then a bottle somebody had time to throw. The planks and
 * barrels are scenery — they mark where she went, and a patch of sea with a
 * barrel on it reads as a place where something happened. The bottle is the one
 * thing worth fishing out, and what is in it is for the page to decide: this
 * file only floats things, drifts them, sinks them when their time is up, and
 * says when the ship has come close and slow enough to take one aboard.
 *
 * A STRANDED CARGO is the other thing it keeps: the reward a bottle's chart can
 * point to, lying on the shallows of an island's shelf. It does not float — it
 * sits in a metre of water with its top out — and it is claimed by coming near
 * and stopping, as one would to send a boat in, since the ship herself cannot
 * go where it lies without taking the ground.
 *
 * Everything here is held in TRUE world metres and placed against the origin
 * each frame, like the land and the gulls: nothing to shift at a rebase.
 */
window.Naval = window.Naval || {};

Naval.Flotsam = class Flotsam {
  constructor(scene, stage, ocean, world){
    this.scene = scene; this.stage = stage; this.ocean = ocean; this.world = world;
    this.items = [];
    this._seen = new WeakMap();        // hulls whose sinking has already given up its wreckage
    this.onBottle = null;              // (item, wreckEntry) — the page opens it
    this.onCargo = null;               // (item) — the page pays it out
    this.bottleChance = 0.35;
    this._q = new THREE.Quaternion(); this._n = new THREE.Vector3(); this._up = new THREE.Vector3(0, 1, 0);
    this._qy = new THREE.Quaternion(); this._qt = new THREE.Quaternion();

    const hazed = m => { if(ocean) Naval.applyHaze(m, ocean.uniforms); return m; };
    this.mats = {
      wood:  hazed(new THREE.MeshStandardMaterial({ color:0x5e4630, roughness:0.9 })),
      raw:   hazed(new THREE.MeshStandardMaterial({ color:0x8a6a44, roughness:0.85 })),
      hoop:  hazed(new THREE.MeshStandardMaterial({ color:0x2a2522, roughness:0.6, metalness:0.4 })),
      glass: hazed(new THREE.MeshStandardMaterial({ color:0x1f5a2c, roughness:0.12, metalness:0.1 })),
      cork:  hazed(new THREE.MeshStandardMaterial({ color:0x9a7a52, roughness:0.9 }))
    };
  }

  _mesh(kind){
    const g = new THREE.Group(), M = this.mats;
    const add = (geo, mat, x, y, z, rx, ry, rz) => {
      const m = new THREE.Mesh(geo, mat);
      m.position.set(x || 0, y || 0, z || 0); m.rotation.set(rx || 0, ry || 0, rz || 0);
      m.castShadow = true;
      g.add(m);
    };
    if(kind === 'plank'){
      const len = 1.6 + Math.random()*1.4;
      add(new THREE.BoxGeometry(len, 0.08, 0.30), Math.random() < 0.5 ? M.wood : M.raw);
      // and a split end, so it reads as broken off a ship rather than cut at a yard
      add(new THREE.BoxGeometry(0.5, 0.08, 0.14), M.raw, len*0.5 + 0.2, 0, 0.07, 0, 0.3, 0);
    }else if(kind === 'barrel'){
      const pts = [];
      for(let i = 0; i <= 8; i++){ const y = -0.45 + i*0.1125; pts.push(new THREE.Vector2(0.30 + 0.06*Math.cos(y/0.45*Math.PI*0.5), y)); }
      add(new THREE.LatheGeometry(pts, 14), M.wood, 0, 0, 0, 0, 0, Math.PI/2);   // lying on its side
      for(const x of [-0.30, 0.30]) add(new THREE.TorusGeometry(0.335, 0.022, 5, 16), M.hoop, x, 0, 0, 0, Math.PI/2, 0);
    }else if(kind === 'bottle'){
      /* Drawn at three times its size, and on purpose: a real bottle is a
         speck a ship's length off. Same argument as the cannonball. */
      const s = 3;
      add(new THREE.CylinderGeometry(0.055*s, 0.06*s, 0.22*s, 10), M.glass);
      add(new THREE.CylinderGeometry(0.02*s, 0.045*s, 0.09*s, 8), M.glass, 0, 0.155*s, 0);
      add(new THREE.CylinderGeometry(0.022*s, 0.02*s, 0.04*s, 6), M.cork, 0, 0.215*s, 0);
      g.children.forEach(c => { c.position.applyAxisAngle(new THREE.Vector3(0, 0, 1), 1.2); c.rotation.z += 1.2; });
    }else if(kind === 'cargo'){
      // a crate and a barrel beside it, stove in and left by the sea
      add(new THREE.BoxGeometry(1.5, 1.0, 1.1), M.wood, 0, 0, 0, 0, 0.2, 0.08);
      add(new THREE.BoxGeometry(1.56, 0.08, 1.16), M.raw, 0, 0.3, 0, 0, 0.2, 0.08);
      add(new THREE.BoxGeometry(1.56, 0.08, 1.16), M.raw, 0, -0.3, 0, 0, 0.2, 0.08);
      add(new THREE.CylinderGeometry(0.34, 0.34, 0.9, 12), M.wood, 1.3, -0.1, 0.6, 0.3, 0, 0.2);
    }
    this.scene.add(g);
    return g;
  }

  /* A hull has gone down: what comes up. */
  _wreck(e, t){
    const b = e.body, o = this.ocean.origin;
    const wx = b.pos.x + o.x, wz = b.pos.z + o.z;
    const n = 1 + (Math.random() < 0.5 ? 1 : 0);
    for(let i = 0; i < n; i++){
      const kind = Math.random() < 0.6 ? 'plank' : 'barrel';
      this._float(kind, wx + (Math.random() - 0.5)*16, wz + (Math.random() - 0.5)*16, 900, null);
    }
    // a bottle is somebody's last act, so the ship at the helm does not throw one
    if(e.player !== true && Math.random() < this.bottleChance){
      this._float('bottle', wx + (Math.random() - 0.5)*10, wz + (Math.random() - 0.5)*10, 1800,
                  { from:e, t, name:e.spec && e.spec.name, origine:e.origine || null });
    }
  }

  _float(kind, x, z, life, data){
    const it = { kind, x, z, y:-2, yaw:Math.random()*Math.PI*2, age:0, life, data,
                 draft: kind === 'barrel' ? 0.12 : kind === 'bottle' ? 0.05 : 0.02,
                 mesh:this._mesh(kind), rise:true };
    this.items.push(it);
    return it;
  }

  /* A cargo stranded on a shelf: on the shallows off `isl`, not in the lee of
     its harbour, where the bottom comes up to about a metre. Returns the item,
     or null if no stretch of that coast has such water. */
  strand(isl){
    const W = this.world;
    for(let tries = 0; tries < 24; tries++){
      const a = Math.random()*Math.PI*2;
      if(isl.port && Math.abs(Math.atan2(Math.sin(a - isl.port.ang), Math.cos(a - isl.port.ang))) < 0.8) continue;
      const shore = W._shore(isl, a);
      for(let out = 0; out < W.shelf; out += 6){
        const x = isl.x + Math.cos(a)*(shore + out), z = isl.z + Math.sin(a)*(shore + out);
        const h = W.heightAt(x, z);
        if(h < -0.7 && h > -1.6){
          const it = { kind:'cargo', x, z, y:h + 0.45, yaw:Math.random()*Math.PI*2, age:0, life:Infinity,
                       mesh:this._mesh('cargo'), isl, ang:a };
          this.items.push(it);
          return it;
        }
        if(h < -1.6) break;
      }
    }
    return null;
  }

  remove(it){
    const i = this.items.indexOf(it);
    if(i >= 0) this.items.splice(i, 1);
    this.scene.remove(it.mesh);
  }

  update(dt, fleet, t, player){
    if(dt <= 0) return;
    for(const e of fleet){
      const ph = e.physics;
      if(ph && ph.foundered && !this._seen.get(ph)){
        this._seen.set(ph, true);
        e.player = (e === player);
        this._wreck(e, t);
      }
    }

    const oc = this.ocean, o = oc.origin, wind = oc.windVec;
    const pb = player && player.body;
    const px = pb ? pb.pos.x + o.x : 0, pz = pb ? pb.pos.z + o.z : 0;
    const pv = pb ? Math.hypot(pb.vel.x, pb.vel.z) : 99;

    for(let i = this.items.length - 1; i >= 0; i--){
      const it = this.items[i];
      it.age += dt;
      const lx = it.x - o.x, lz = it.z - o.z;

      if(it.kind === 'cargo'){
        it.mesh.position.set(lx, it.y, lz);
        it.mesh.rotation.y = it.yaw;
        if(pb && pv < 1.0 && Math.hypot(it.x - px, it.z - pz) < 180){
          this.remove(it);
          if(this.onCargo) this.onCargo(it);
        }
        continue;
      }

      if(it.age > it.life){ this.remove(it); continue; }
      /* It drifts with the wind, a few hundredths of it — the most of it a
         floating thing ever makes — and slowly turns as it goes. */
      if(wind){ it.x += wind.x*0.025*dt; it.z += wind.z*0.025*dt; }
      it.yaw += 0.02*dt*Math.sin(it.age*0.1 + it.x);

      const sea = oc.sample(lx, lz, t);
      const sx = oc.sample(lx + 0.8, lz, t) - oc.sample(lx - 0.8, lz, t);
      const sz = oc.sample(lx, lz + 0.8, t) - oc.sample(lx, lz - 0.8, t);
      // coming up from the wreck, then riding the surface, and at the end going under
      const sinking = Math.max(0, (it.age - (it.life - 20))/20);
      const target = sea - it.draft - sinking*1.5;
      it.y = it.rise ? Math.min(target, it.y + 1.2*dt) : it.y + (target - it.y)*Math.min(1, dt*6);
      if(it.rise && it.y >= target - 0.01) it.rise = false;

      this._n.set(-sx/1.6, 1, -sz/1.6).normalize();
      this._qt.setFromUnitVectors(this._up, this._n);
      this._qy.setFromAxisAngle(this._up, it.yaw);
      it.mesh.quaternion.copy(this._qt).multiply(this._qy);
      it.mesh.position.set(lx, it.y, lz);

      if(it.kind === 'bottle' && pb && pv < 1.03 && Math.hypot(it.x - px, it.z - pz) < 10){
        this.remove(it);
        if(this.onBottle) this.onBottle(it, it.data && it.data.from);
      }
    }
  }

  // what the chart should mark: the bottles and the cargos, in true metres
  marks(){
    const out = [];
    for(const it of this.items) if(it.kind === 'bottle' || it.kind === 'cargo') out.push({ x:it.x, z:it.z, kind:it.kind });
    return out;
  }
};
