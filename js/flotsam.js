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

/* What each prop is, when props/Props.json has not said otherwise — the same
   figures the file ships with, so a missing or half-written file changes
   nothing. The file wins field by field. */
Naval.PROPS_DEFAULTS = {
  wreck:  { debrisMin:1, debrisMax:2 },
  plank:  { name:'Planche', scale:1, glb:null, rotation:[0,0,0], draft:0.02, life:900 },
  barrel: { name:'Tonneau', scale:1, glb:null, rotation:[0,0,0], draft:0.12, life:900 },
  bottle: { name:'Bouteille', scale:3, glb:null, rotation:[0,0,0], draft:0.05, life:1800,
            pickupRadius:10, pickupSpeed:1.03,
            halo:{ enabled:true, radius:1.1, color:'#a8ecff', intensity:1.0, mark:0.032, markFrom:60 } },
  cargo:  { name:'Cargaison échouée', scale:1, glb:null, rotation:[0,0,0], claimRadius:15, claimSpeed:1.0, needsBoat:true }
};

Naval.Flotsam = class Flotsam {
  constructor(scene, stage, ocean, world){
    this.scene = scene; this.stage = stage; this.ocean = ocean; this.world = world;
    this.items = [];
    this._seen = new WeakMap();        // hulls whose sinking has already given up its wreckage
    this.onBottle = null;              // (item, wreckEntry) — the page opens it
    this.onCargo = null;               // (item) — the page pays it out
    this.def = JSON.parse(JSON.stringify(Naval.PROPS_DEFAULTS));
    this._tpl = {};                    // a loaded .glb per kind, cloned for each one afloat
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

  /* Read props/Props.json — carried in the page as Naval.PROPS_DATA by the
     build, fetched from the dev server otherwise — and load any model it names.
     Asynchronous and forgiving: until it answers, and for any kind whose model
     will not load, the prop is drawn here as before. Something already afloat
     keeps the look it was born with. */
  async configure(){
    let data = Naval.PROPS_DATA;
    if(!data){
      try{ const r = await fetch('props/Props.json', { cache:'no-cache' }); if(r.ok) data = await r.json(); }
      catch(e){ /* no file, no server: the defaults stand */ }
    }
    if(data) for(const k of Object.keys(this.def)) if(data[k]){
      const halo = this.def[k].halo;
      Object.assign(this.def[k], data[k]);
      // the halo is a block of its own: a file naming one field keeps the others
      if(halo) this.def[k].halo = Object.assign({}, halo, data[k].halo || {});
    }
    for(const kind of ['plank', 'barrel', 'bottle', 'cargo']){
      const d = this.def[kind];
      if(!d.glb && !d.glbBase64) continue;
      try{
        const loader = new (await Naval.loadGLTFLoader())();
        const gltf = d.glbBase64
          ? await new Promise((ok, no) => loader.parse(Naval.base64ToArrayBuffer(d.glbBase64), '', ok, no))
          : await loader.loadAsync(d.glb);
        const tpl = gltf.scene;
        tpl.traverse(o => {
          if(!o.isMesh) return;
          o.castShadow = true;
          for(const m of [].concat(o.material)) if(this.ocean) Naval.applyHaze(m, this.ocean.uniforms);
        });
        this._tpl[kind] = tpl;
      }catch(err){
        console.warn('[props] ' + kind + ' : modèle ' + (d.glb || 'embarqué') + ' illisible — dessiné par le code. ' + (err && err.message || err));
      }
    }
  }

  /* One prop's scene object: its model if the file named one and it loaded,
     the drawing below otherwise — scaled and turned as the file says. */
  _mesh(kind){
    const d = this.def[kind] || {}, outer = new THREE.Group();
    const inner = this._tpl[kind] ? this._tpl[kind].clone(true) : this._draw(kind);
    const r = d.rotation || [0, 0, 0], D = Math.PI/180;
    inner.rotation.set(r[0]*D, r[1]*D, r[2]*D);
    outer.add(inner);
    outer.scale.setScalar(d.scale || 1);
    if(d.halo && d.halo.enabled){
      const h = this._halo(d.halo);
      h.scale.setScalar(1/(d.scale || 1));   // the halo is sized in metres, whatever the prop's scale
      outer.add(h);
      outer.userData.halo = h;
    }
    this.scene.add(outer);
    return outer;
  }

  /* A LITTLE MAGIC AROUND THE BOTTLE, asked for because it was hard to find in
     the swell. A soap bubble rather than a light: a sphere that is nearly
     nothing face-on and bright at its rim (Fresnel), with the rainbow drift of a
     thin film, a glint of the sun and a few sparkles crawling over it. Added
     over the scene and writing no depth, so it never hides what is inside or
     behind it; depth-tested, so the sea cuts it at the waterline. It gives light
     rather than returning it — it is the one thing here that is meant to glow,
     and at night that is exactly when it must still be found.

     And a MARK for far off: the bubble is a metre, a speck at two cables, so a
     soft glow held at a few pixels whatever the distance — the lantern's far
     light — comes in past `markFrom` metres and is gone close to. */
  _halo(H){
    if(!this._haloMat){
      const u = this.ocean && this.ocean.uniforms;
      this._haloU = {
        uTime:{ value:0 },
        uColor:{ value:new THREE.Color(H.color) },
        uIntensity:{ value:H.intensity },
        uSun: u && u.uSun ? u.uSun : { value:new THREE.Vector3(0, 1, 0) }
      };
      this._haloMat = new THREE.ShaderMaterial({
        uniforms:this._haloU, transparent:true, depthWrite:false, side:THREE.DoubleSide,
        blending:THREE.AdditiveBlending, clipping:true,
        vertexShader:`
          #include <clipping_planes_pars_vertex>
          varying vec3 vN; varying vec3 vW;
          void main(){
            vec4 w = modelMatrix*vec4(position, 1.0);
            vW = w.xyz;
            vN = normalize(mat3(modelMatrix)*normal);
            vec4 mvPosition = viewMatrix*w;
            gl_Position = projectionMatrix*mvPosition;
            #include <clipping_planes_vertex>
          }`,
        fragmentShader:`
          #include <clipping_planes_pars_fragment>
          uniform float uTime, uIntensity; uniform vec3 uColor, uSun;
          varying vec3 vN; varying vec3 vW;
          void main(){
            #include <clipping_planes_fragment>
            vec3 V = normalize(cameraPosition - vW);
            vec3 N = normalize(vN);
            float front = gl_FrontFacing ? 1.0 : 0.35;     // the far wall, seen through the near one
            float edge = 1.0 - abs(dot(N, V));
            float rim = pow(edge, 2.6);
            // thin film: the hue walks with the angle and, slowly, with time
            vec3 film = 0.5 + 0.5*cos(6.2832*(vec3(0.0, 0.33, 0.67) + edge*1.4 + uTime*0.04));
            vec3 col = mix(uColor, film, 0.35);
            float pulse = 0.85 + 0.15*sin(uTime*1.7);
            // a glint of the sun and a window-light, on the near wall only
            vec3 R = reflect(-V, N);
            float glint = gl_FrontFacing ? pow(max(dot(R, normalize(uSun)), 0.0), 80.0)*1.6
                                         + pow(max(dot(N, normalize(vec3(-0.45, 0.8, 0.4))), 0.0), 40.0)*0.25*(1.0 - edge) : 0.0;
            // sparkles crawling over the skin
            float sp = sin(vW.x*6.1 + uTime*1.3)*sin(vW.y*7.3 - uTime*1.1)*sin(vW.z*6.7 + uTime*0.9);
            float spark = pow(max(sp, 0.0), 14.0)*1.4;
            vec3 c = col*(rim*1.25*pulse + 0.035)*front + vec3(glint) + uColor*spark*front;
            gl_FragColor = vec4(c*uIntensity, 1.0);
          }`
      });
    }
    const g = new THREE.Group();
    const bubble = new THREE.Mesh(new THREE.SphereGeometry(H.radius, 28, 18), this._haloMat);
    bubble.position.y = H.radius*0.3;          // riding mostly above the water, as a bubble would
    bubble.renderOrder = 5;
    g.add(bubble);
    const mark = new THREE.Sprite(new THREE.SpriteMaterial({
      map:Naval.glowTexture(), color:new THREE.Color(H.color), transparent:true, opacity:0,
      blending:THREE.AdditiveBlending, depthWrite:false, sizeAttenuation:false, fog:false }));
    mark.scale.setScalar(H.mark);
    mark.position.y = H.radius*0.5;
    g.add(mark);
    g.userData = { mark, markFrom:H.markFrom };
    return g;
  }

  _draw(kind){
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
      /* Drawn at its TRUE size here; Props.json enlarges it three times, and on
         purpose — a real bottle is a speck a ship's length off. Same argument
         as the cannonball. */
      const s = 1;
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
    return g;
  }

  /* A hull has gone down: what comes up. */
  _wreck(e, t){
    const b = e.body, o = this.ocean.origin;
    const wx = b.pos.x + o.x, wz = b.pos.z + o.z;
    const W = this.def.wreck;
    const n = W.debrisMin + Math.floor(Math.random()*(W.debrisMax - W.debrisMin + 1));
    for(let i = 0; i < n; i++){
      const kind = Math.random() < 0.6 ? 'plank' : 'barrel';
      this._float(kind, wx + (Math.random() - 0.5)*16, wz + (Math.random() - 0.5)*16, this.def[kind].life, null);
    }
    // a bottle is somebody's last act, so the ship at the helm does not throw one
    // how often is a GAME rule, not a property of the object: the page answers it from settings.json
    const oneIn = this.bottleOneIn ? this.bottleOneIn() : 6;
    if(e.player !== true && oneIn > 0 && Math.random()*oneIn < 1){
      this._float('bottle', wx + (Math.random() - 0.5)*10, wz + (Math.random() - 0.5)*10, this.def.bottle.life,
                  { from:e, t, name:e.spec && e.spec.name, origine:e.origine || null });
    }
  }

  _float(kind, x, z, life, data){
    const it = { kind, x, z, y:-2, yaw:Math.random()*Math.PI*2, age:0,
                 life: life != null ? life : this.def[kind].life, data,
                 draft: this.def[kind].draft,
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
    if(this._haloU) this._haloU.uTime.value += dt;
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
        const C = this.def.cargo;
        // on n'y va qu'en chaloupe : un navire ne passe pas là où elle est posée
        const bateau = !C.needsBoat || !!(player && player.spec && player.spec.oars);
        if(pb && bateau && pv < C.claimSpeed && Math.hypot(it.x - px, it.z - pz) < C.claimRadius){
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

      const halo = it.mesh.userData.halo;
      if(halo){
        // the far mark comes in with distance, and fades with the bottle when it sinks
        const cam = this.stage && this.stage.camera;
        const d = cam ? cam.position.distanceTo(it.mesh.position) : 0;
        const far = Math.min(1, Math.max(0, (d - halo.userData.markFrom)/halo.userData.markFrom));
        halo.userData.mark.material.opacity = 0.9*far*(1 - sinking);
        halo.children[0].visible = sinking < 0.9;
      }

      const B = this.def.bottle;
      if(it.kind === 'bottle' && pb && pv < B.pickupSpeed && Math.hypot(it.x - px, it.z - pz) < B.pickupRadius){
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
