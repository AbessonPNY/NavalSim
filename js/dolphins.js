/* Dolphins.
 *
 * A pod comes, now and then, when the sea is quiet: three to seven animals
 * that find the ship, ride her bow wave if she has way on — they do, for the
 * free push of the pressure wave — or circle her if she lies stopped, and after
 * a few minutes fall astern and are gone.
 *
 * What the eye reads is the ARC: most of the time a dolphin is a shape under
 * the surface, and every few seconds it breaks out, body curved along its
 * path, and goes back in head first. So each animal keeps a place relative to
 * the ship, chases it with a swimmer's speed and turn, and runs its own
 * surfacing cycle — now and then a proper leap — throwing water as it breaks
 * out and again as it goes back in. The sea hides what is under it; nothing here has to.
 *
 * Drawn, not loaded: a turned body, a dorsal fin, two flippers, and flukes on a
 * pivot that beat. Grey above, pale beneath, painted as vertex colours. It
 * REFLECTS, so the page gives its material the haze like everything that does.
 * Positions are LOCAL metres and shift with the floating origin.
 */
window.Naval = window.Naval || {};

/* Game rules, overridden by settings.json → dolphins. */
Naval.DOLPHINS = {
  enabled: true,
  perDay: 4,              // pods a game day, on average, while the sea allows
  calmBelow: 3.5,         // sea state above which they do not come (and leave)
  pod: [3, 7],            // how many
  minutes: [3, 8],        // how long they stay, game minutes
  awayFromShore: 250,     // metres: not in a harbour
  glb: null               // a model to wear instead of the drawn one (see tools/dolphin-glb.js)
};

(function(){
const MAX = 7;
const _v = new THREE.Vector3(), _f = new THREE.Vector3(), _r = new THREE.Vector3();

function bodyGeometry(){
  // radius along the length, nose (0) to tail stock (2.35 m)
  const prof = [[0.0, 0.0], [0.035, 0.03], [0.05, 0.12], [0.06, 0.22], [0.13, 0.30],
                [0.21, 0.48], [0.26, 0.78], [0.27, 1.05], [0.23, 1.45], [0.15, 1.85],
                [0.08, 2.15], [0.05, 2.35]];
  const pts = prof.map(([r, y]) => new THREE.Vector2(r, y));
  const g = new THREE.LatheGeometry(pts, 14);
  g.rotateX(-Math.PI/2);                    // along -z: nose at 0, tail at -2.35
  g.translate(0, 0, 1.2);                   // nose at +1.2, tail stock at -1.15
  g.scale(0.85, 1, 1);                      // a little narrower than deep
  // countershading: dark back, pale belly
  const p = g.attributes.position, col = [];
  for(let i=0;i<p.count;i++){
    const up = p.getY(i)/0.27;
    const k = Math.max(0, Math.min(1, 0.5 + up*0.9));
    col.push(0.78 - 0.44*k, 0.80 - 0.42*k, 0.82 - 0.38*k);
  }
  g.setAttribute('color', new THREE.Float32BufferAttribute(col, 3));
  g.computeVertexNormals();
  return g;
}

function flat(points, thick, colour){
  const sh = new THREE.Shape(points.map(([x, y]) => new THREE.Vector2(x, y)));
  const g = new THREE.ExtrudeGeometry(sh, { depth:thick, bevelEnabled:false });
  g.translate(0, 0, -thick/2);
  const col = [];
  for(let i=0;i<g.attributes.position.count;i++) col.push(...colour);
  g.setAttribute('color', new THREE.Float32BufferAttribute(col, 3));
  g.computeVertexNormals();
  return g;
}

Naval.Dolphins = class Dolphins {
  constructor(scene){
    this.group = new THREE.Group();
    this.group.visible = false;
    scene.add(this.group);
    this.mat = new THREE.MeshStandardMaterial({ vertexColors:true, roughness:0.32, metalness:0.05 });
    this.materials = [this.mat];

    this.bodyG = bodyGeometry();
    // the dorsal fin, drawn in the (z, y) plane then stood up
    this.finG = flat([[0.18, 0], [-0.22, 0], [-0.28, 0.34], [-0.12, 0.2]], 0.03, [0.33, 0.37, 0.43]);
    this.finG.rotateY(Math.PI/2);
    this.flipG = flat([[0, 0], [0.32, -0.08], [0.36, -0.14], [0, -0.1]], 0.025, [0.36, 0.4, 0.46]);
    this.flukeG = flat([[0, 0], [0.38, -0.22], [0.42, -0.1], [0.12, 0.04], [-0.12, 0.04], [-0.42, -0.1], [-0.38, -0.22]], 0.03, [0.34, 0.38, 0.44]);
    /* Lying flat and trailing AFT. It was turned the other way (-π/2), which
       laid the flukes forward along the tail stock, inside the body. */
    this.flukeG.rotateX(Math.PI/2);

    this.animals = [];
    for(let i=0;i<MAX;i++) this.animals.push(this._animal());
    this.state = 'away';
    this.timer = 0;
    this.left = 0;
    this.t = 0;
  }

  /* The page's air, so a model arriving later is hazed like the drawn one. */
  setAtmosphere(u){
    this._oceanU = u;
    for(const m of this.materials) Naval.applyHaze(m, u);
  }

  /* WEAR A MODEL: a node named queue (tail, caudale) is the flukes, its origin
     the hinge, beaten about its X axis; everything else is the body. Nose
     toward +Z (Blender -Y), metres. Each animal gets its own copy. Any failure
     leaves the drawn dolphin. */
  async loadModel(cfg){
    if(!cfg || (!cfg.glb && !cfg.glbBase64)) return false;
    try{
      const loader = new (await Naval.loadGLTFLoader())();
      const gltf = cfg.glbBase64
        ? await new Promise((ok, no) => loader.parse(Naval.base64ToArrayBuffer(cfg.glbBase64), '', ok, no))
        : await loader.loadAsync(cfg.glb);
      const root = gltf.scene;
      root.updateMatrixWorld(true);
      let tailNode = null;
      root.traverse(o => { if(!tailNode && /^(queue|tail|caudale)/i.test(o.name || '')) tailNode = o; });
      const body = new THREE.Group(), tail = new THREE.Group();
      const hinge = tailNode ? new THREE.Vector3().setFromMatrixPosition(tailNode.matrixWorld) : null;
      const toTail = tailNode ? new THREE.Matrix4().copy(tailNode.matrixWorld).invert() : null;
      const seen = new Set();
      root.traverse(o => {
        if(!o.isMesh) return;
        let inTail = false;
        for(let p = o; p && p !== root; p = p.parent) if(p === tailNode) inTail = true;
        const g = o.geometry.clone();
        // the tail's pieces in the hinge's frame, the rest in the model's
        g.applyMatrix4(inTail ? new THREE.Matrix4().multiplyMatrices(toTail, o.matrixWorld) : o.matrixWorld);
        for(const m of [].concat(o.material)){
          if(!m || seen.has(m)) continue;
          seen.add(m);
          this.materials.push(m);
          if(this._oceanU) Naval.applyHaze(m, this._oceanU);
        }
        (inTail ? tail : body).add(new THREE.Mesh(g, o.material));
      });
      if(!body.children.length) throw new Error('aucun maillage de corps');
      for(const a of this.animals){
        a.obj.remove(...a.obj.children);
        a.obj.add(body.clone());
        a.tail = tail.clone();
        a.tail.position.copy(hinge || new THREE.Vector3(0, 0, -1.15));
        a.obj.add(a.tail);
      }
      console.log('[dauphins] modèle chargé : corps ' + body.children.length + ' pièce(s), queue '
                  + tail.children.length + (tailNode ? '' : ' (pas de nœud « queue » : elle ne battra pas)'));
      return true;
    }catch(err){
      console.warn('[dauphins] modèle illisible, les dauphins restent dessinés : ' + (err && err.message || err));
      return false;
    }
  }

  _animal(){
    const obj = new THREE.Group();
    const body = new THREE.Mesh(this.bodyG, this.mat);
    const fin = new THREE.Mesh(this.finG, this.mat);
    fin.position.set(0, 0.24, -0.1);
    const fl = new THREE.Mesh(this.flipG, this.mat); fl.position.set(0.18, -0.1, 0.55); fl.rotation.y = 0.5;
    const fr = new THREE.Mesh(this.flipG, this.mat); fr.position.set(-0.18, -0.1, 0.55); fr.rotation.y = Math.PI - 0.5;
    const tail = new THREE.Group(); tail.position.set(0, 0, -1.15);
    tail.add(new THREE.Mesh(this.flukeG, this.mat));
    obj.add(body, fin, fl, fr, tail);
    obj.visible = false;
    this.group.add(obj);
    return { obj, tail, pos:new THREE.Vector3(), vel:new THREE.Vector3(), y:-2, vy:0,
             off:new THREE.Vector3(), phase:0, rate:0.3, leap:1, seed:Math.random()*100,
             wasUp:false, scale:1 };
  }

  /* Bring a pod now — for testing, or from the scheduler. */
  summon(c){
    const K = Naval.DOLPHINS;
    c = c || this.ctx;
    if(!c) return false;
    const n = K.pod[0] + Math.floor(Math.random()*(K.pod[1] - K.pod[0] + 1));
    const b = c.body;
    const back = _v.set(0, 0, -1).applyQuaternion(b.quat);
    this.animals.forEach((a, i) => {
      a.on = i < n;
      a.obj.visible = a.on;
      if(!a.on) return;
      // they come up from astern and to one side
      a.pos.copy(b.pos).addScaledVector(back, 60 + Math.random()*40);
      a.pos.x += (Math.random() - 0.5)*40; a.pos.z += (Math.random() - 0.5)*40;
      a.vel.set(0, 0, 0);
      a.y = -2; a.vy = 0;
      a.phase = Math.random();
      a.rate = 0.28 + Math.random()*0.12;       // surfacings per second
      a.scale = 0.85 + Math.random()*0.35;
      a.obj.scale.setScalar(a.scale);
      a.slot = i;
      a.side = i % 2 ? 1 : -1;
    });
    const [m0, m1] = K.minutes;
    this.state = 'here';
    this.left = (m0 + Math.random()*(m1 - m0))/60;   // game hours
    this.group.visible = true;
    if(c.say) c.say('Des dauphins viennent jouer à l’étrave !');
    return true;
  }

  /* c: { body, spec, ocean, sea (state), hours (game hours this frame), t,
          shore (metres to the nearest coast), splash(pos, water, speed, jet),
          say(msg) } */
  update(dt, c){
    const K = Naval.DOLPHINS;
    this.ctx = c;
    this.t = c.t;
    if(this.state === 'away'){
      if(!K.enabled || c.sea > K.calmBelow || c.shore < K.awayFromShore) return;
      if(c.hours > 0 && Math.random() < K.perDay*c.hours/24) this.summon(c);
      if(this.state === 'away') return;
    }
    if(this.state === 'here'){
      this.left -= c.hours;
      if(this.left <= 0 || c.sea > K.calmBelow + 0.5) this.state = 'leaving';
    }

    const b = c.body, L = c.spec.L, B = c.spec.B;
    const fwd = _f.set(0, 0, 1).applyQuaternion(b.quat); fwd.y = 0; fwd.normalize();
    const right = _r.set(-fwd.z, 0, fwd.x);            // any perpendicular will do
    const way = Math.hypot(b.vel.x, b.vel.z);
    let seen = 0;

    for(const a of this.animals){
      if(!a.on) continue;
      const k = a.slot;
      // --- where it wants to be ---
      let tx, tz;
      if(this.state === 'leaving'){
        // falls astern and wide, and goes
        tx = a.pos.x - fwd.x*8 + right.x*a.side*4;
        tz = a.pos.z - fwd.z*8 + right.z*a.side*4;
      }else if(way > 1.5){
        // in the pressure wave off the bow, weaving
        const ahead = L*0.5 + 2 + (k % 3)*3 + Math.sin(this.t*0.4 + a.seed)*2.5;
        const abeam = B*0.5 + 1.5 + ((k >> 1) % 3)*1.8 + Math.sin(this.t*0.33 + a.seed*2)*1.2;
        tx = b.pos.x + fwd.x*ahead + right.x*a.side*abeam;
        tz = b.pos.z + fwd.z*ahead + right.z*a.side*abeam;
      }else{
        // round her, on a slow circle
        const ang = this.t*0.12*(a.side) + k*1.1;
        const rad = L*0.8 + 8 + (k % 3)*5;
        tx = b.pos.x + Math.cos(ang)*rad;
        tz = b.pos.z + Math.sin(ang)*rad;
      }
      // --- swim there: a spring to the place, capped at a dolphin's speed ---
      const vmax = Math.max(6, way + 4);
      a.vel.x += ((tx - a.pos.x)*0.8 - a.vel.x*1.2)*dt;
      a.vel.z += ((tz - a.pos.z)*0.8 - a.vel.z*1.2)*dt;
      // carry the ship's way, so the chase is about the offset and not the whole run
      a.vel.x += (b.vel.x - a.vel.x)*Math.min(1, dt*0.6)*(this.state === 'leaving' ? 0 : 1);
      a.vel.z += (b.vel.z - a.vel.z)*Math.min(1, dt*0.6)*(this.state === 'leaving' ? 0 : 1);
      const hv = Math.hypot(a.vel.x, a.vel.z);
      if(hv > vmax){ a.vel.x *= vmax/hv; a.vel.z *= vmax/hv; }
      a.pos.x += a.vel.x*dt; a.pos.z += a.vel.z*dt;

      // --- the surfacing cycle ---
      a.phase += dt*a.rate;
      if(a.phase >= 1){
        a.phase -= 1;
        a.leap = Math.random() < 0.18 ? 2.2 : 1;        // now and then a proper jump
      }
      const sea = c.ocean.sample(a.pos.x, a.pos.z, c.t);
      const deep = this.state === 'leaving' ? -3.5 : -1.4;
      // above water for the first 28 % of the cycle, an arc; below, gliding
      const up = a.phase < 0.28 ? Math.sin(Math.PI*a.phase/0.28) : 0;
      // clear of the water for most of the arc, and well clear on a leap
      const yWant = sea + deep + up*(-deep + 0.9 + (a.leap > 1 ? 1.4 : 0));
      const yNew = a.y + (yWant - a.y)*Math.min(1, dt*12);
      a.vy = (yNew - a.y)/Math.max(dt, 1e-3);
      a.y = yNew;
      a.obj.position.set(a.pos.x, a.y, a.pos.z);

      // heading along its way, pitched along its arc
      const hvel = Math.max(0.5, Math.hypot(a.vel.x, a.vel.z));
      a.obj.rotation.set(-Math.atan2(a.vy, hvel)*0.9, Math.atan2(a.vel.x, a.vel.z), 0, 'YXZ');
      a.tail.rotation.x = Math.sin(c.t*(5 + hv*0.4) + a.seed)*0.35;

      // back in, head first: a small splash
      const above = a.y > sea + 0.1;
      /* Water off it both ways: a sheet thrown up as it breaks out, and a
         heavier plume as it goes back in head first. A leap throws more. The
         volumes are what the spray pool counts drops by (eleven a cubic
         metre): half a cubic metre was six drops, which nobody could see. */
      if(c.splash && above !== a.wasUp){
        const at = new THREE.Vector3(a.pos.x, sea, a.pos.z);
        if(above) c.splash(at, 1.8*a.leap, 3 + a.leap, 1.5);           // out
        else      c.splash(at, 3.2*a.leap, 3.5 + a.leap, 1.3);         // in
      }
      a.wasUp = above;

      if(this.state === 'leaving' && Math.hypot(a.pos.x - b.pos.x, a.pos.z - b.pos.z) > 160){
        a.on = false; a.obj.visible = false;
      }
      if(a.on) seen++;
    }
    if(!seen){ this.state = 'away'; this.group.visible = false; }
  }

  rebase(dx, dz){
    for(const a of this.animals){ a.pos.x -= dx; a.pos.z -= dz; }
  }
};
})();
