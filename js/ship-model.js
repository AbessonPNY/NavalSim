/* Everything you can see of the vessel: hull, rails, deckhouse, spars, canvas
   and the wake astern. Carries no physics — it is told where to sit and how the
   sheets are trimmed.

   Two rigs are supported. "gaff" hangs a four-sided sail on a swinging boom;
   "square" hangs yards across each mast, which brace round together. Both are
   driven by the same single sheet control, because the solver models one
   equivalent aerofoil, not each sail separately.

   A spec may instead name a .glb: the model then replaces the procedural hull
   and rig, scaled to the vessel's stated length. If it fails to load — a bad
   path, or a sandbox that blocks it — the procedural hull is kept, so the
   simulation always has something to show. */
window.Naval = window.Naval || {};

Naval.ShipModel = class ShipModel {
  constructor(scene, spec, lines){
    this.spec = spec;
    this.lines = lines;
    this.scene = scene;
    this.group = new THREE.Group();
    scene.add(this.group);

    const A = spec.appearance;
    const hex = s => parseInt(s, 16);
    this.mats = {
      hull:   new THREE.MeshStandardMaterial({color:hex(A.hull), metalness:0.05, roughness:0.72, side:THREE.DoubleSide}),
      timber: new THREE.MeshStandardMaterial({color:hex(A.timber), roughness:0.75}),
      spar:   new THREE.MeshStandardMaterial({color:hex(A.spar), roughness:0.62}),
      house:  new THREE.MeshStandardMaterial({color:hex(A.house), roughness:0.7}),
      // Sailcloth is thin and translucent: backlit canvas glows rather than
      // going black, so it carries a little emissive of its own.
      canvas: new THREE.MeshStandardMaterial({
        color:hex(A.canvas), roughness:0.95, side:THREE.DoubleSide,
        emissive:0x8d866f, emissiveIntensity:0.5})
    };

    this.procedural = new THREE.Group();     // everything we build ourselves
    this.group.add(this.procedural);
    this.rigs = [];

    this._buildHull();
    this._buildRig();
    this._buildWake(scene);
    this._fwd = new THREE.Vector3();
  }

  _buildHull(){
    const spec = this.spec, L = this.lines, C = spec.hull;
    this.procedural.add(new THREE.Mesh(L.buildGeometry(), this.mats.hull));

    // bulwark rail following the sheer
    const pts=[], N=28;
    for(let i=0;i<=N;i++){
      const t=i/N, z=-spec.L/2+t*spec.L;
      pts.push(new THREE.Vector3(L.halfB(t)*0.97, L.deckY(t)+0.30*(spec.L/24), z));
    }
    const rg=new THREE.TubeGeometry(new THREE.CatmullRomCurve3(pts), 40, 0.075*(spec.L/24), 6, false);
    const r1=new THREE.Mesh(rg,this.mats.timber), r2=new THREE.Mesh(rg.clone(),this.mats.timber);
    r2.scale.x=-1; this.procedural.add(r1, r2);

    // low deckhouse / companionway
    const d = spec.raw.deckhouse;
    const house = new THREE.Mesh(
      new THREE.BoxGeometry(spec.B*d.beamFrac, d.height, spec.L*d.lengthFrac), this.mats.house);
    house.position.set(0, spec.deckMid + d.height*0.54, spec.L*d.zFrac);
    this.procedural.add(house);

    // bowsprit, steeved up from the stem
    const bs = spec.rig.bowsprit;
    if(!bs) return;
    const b = new THREE.Mesh(
      new THREE.CylinderGeometry(0.07*(spec.L/24), 0.13*(spec.L/24), bs.length, 8), this.mats.spar);
    b.rotation.x = Math.PI/2 - bs.steeve;
    b.position.set(0, L.deckY(1)+0.55*(spec.L/24), spec.L/2 + bs.length*0.42);
    this.procedural.add(b);
  }

  _buildRig(){
    const spec = this.spec, L = this.lines, r = spec.rig;
    if(!spec.masts.length) return;              // a vessel under power alone
    const sc = spec.L/24;                       // spar thickness scales with her
    for(const m of spec.masts){
      const mast = new THREE.Mesh(
        new THREE.CylinderGeometry(0.10*sc, 0.17*sc, m.height, 10), this.mats.spar);
      mast.position.set(0, spec.deckMid + m.height/2, m.z);
      this.procedural.add(mast);
      this.rigs.push(r.type === 'square'
        ? this._squareRig(m, sc)
        : this._gaffRig(m, sc));
    }
    if(r.jib){
      this.jibRig = this._jibRig();
      this.rigs.push(this.jibRig);
    }
  }

  _sailMesh(points){
    const g=new THREE.BufferGeometry();
    const v=[]; for(const p of points) v.push(p.x,p.y,p.z);
    g.setAttribute('position', new THREE.Float32BufferAttribute(v,3));
    g.setIndex(points.length===4?[0,1,2, 0,2,3]:[0,1,2]);
    g.computeVertexNormals();
    return new THREE.Mesh(g, this.mats.canvas);
  }

  /* Gaff sail on a swinging boom. The group pivots on the mast, so sheeting in
     or out turns the boom and its canvas together. */
  _gaffRig(m, sc){
    const spec = this.spec, belly = spec.rig.belly;
    const V = (x,y,z)=>new THREE.Vector3(x,y,z);
    const tackY = spec.deckMid + m.tackAbove;
    const rig = new THREE.Group();
    rig.position.set(0, 0, m.z);
    const boom = new THREE.Mesh(
      new THREE.CylinderGeometry(0.07*sc, 0.09*sc, m.boom, 8), this.mats.spar);
    boom.rotation.x = Math.PI/2;
    boom.position.set(0, tackY, -m.boom*0.45);
    rig.add(boom);
    rig.add(this._sailMesh([
      V(0, tackY+0.05, -0.2*sc),
      V(belly, tackY, -m.boom*0.93),
      V(belly*0.8, spec.deckMid+m.height-1.0*sc, -m.boom*0.70),
      V(0, spec.deckMid+m.height-0.6*sc, -0.2*sc)
    ]));
    this.procedural.add(rig);
    return rig;
  }

  /* Square rig: yards crossed on the mast, each carrying a rectangular sail.
     The whole group braces round as one, which is how a square-rigger is
     trimmed — you brace the yards, you do not ease a boom. */
  _squareRig(m, sc){
    const spec = this.spec, belly = spec.rig.belly;
    const rig = new THREE.Group();
    rig.position.set(0, 0, m.z);
    const halfSpan = spec.B * m.yardSpan * 0.5;

    for(let i=0;i<m.yards.length;i++){
      const frac = m.yards[i];
      const y = spec.deckMid + m.height*frac;
      const span = halfSpan * (1 - i*0.16);            // narrower as you go aloft
      const drop = m.height*(m.yards[i+1] !== undefined
                    ? (m.yards[i+1]-frac)*0.80 : 0.16);

      const yard = new THREE.Mesh(
        new THREE.CylinderGeometry(0.07*sc, 0.05*sc, span*2, 8), this.mats.spar);
      yard.rotation.z = Math.PI/2;                     // lie athwartships
      yard.position.set(0, y, 0);
      rig.add(yard);

      // the sail hangs below its yard, bellying to leeward
      const g = new THREE.BufferGeometry();
      const v = [
        -span, y,        0,
         span, y,        0,
         span*0.86, y-drop, belly,
        -span*0.86, y-drop, belly
      ];
      g.setAttribute('position', new THREE.Float32BufferAttribute(v,3));
      g.setIndex([0,1,2, 0,2,3]);
      g.computeVertexNormals();
      rig.add(new THREE.Mesh(g, this.mats.canvas));
    }
    this.procedural.add(rig);
    return rig;
  }

  // Jib, set flying — pivots on the forestay at the stem.
  _jibRig(){
    const spec = this.spec, L = this.lines, j = spec.rig.jib, belly = spec.rig.belly;
    const V = (x,y,z)=>new THREE.Vector3(x,y,z);
    const rig = new THREE.Group();
    rig.position.set(0, 0, spec.L/2);
    const fore = spec.masts[0];
    const back = fore.z - spec.L/2;              // foremost mast, in this frame
    rig.add(this._sailMesh([
      V(0, L.deckY(1)+j.tackAbove, spec.jibFootZ),
      V(belly*0.7, spec.deckMid+j.clewAbove, back+0.6),
      V(0, spec.deckMid+fore.height-j.headDrop, back+0.15)
    ]));
    this.procedural.add(rig);
    return rig;
  }

  /* Swap the procedural hull for a glTF model. Resolves to true if the model
     was adopted, false if we kept our own hull — never throws, because a
     missing model must not take the simulation down with it. */
  async loadModel(){
    const m = this.spec.model;
    if(!m || (!m.glb && !m.glbBase64)) return false;
    try{
      const loader = new (await Naval.loadGLTFLoader())();
      let gltf;
      if(m.glbBase64){
        /* The build embeds the model's bytes in the page, so it is parsed from
           memory. A published page cannot fetch a local file, and .glb is not
           an uploadable artifact asset — carrying the bytes is the only way a
           model survives publishing. */
        gltf = await new Promise((ok, no) =>
          loader.parse(Naval.base64ToArrayBuffer(m.glbBase64), '', ok, no));
      }else{
        gltf = await loader.loadAsync(m.glb);
      }
      const obj = gltf.scene;

      // Scale the model so its length matches the hull the solver is using.
      const box = new THREE.Box3().setFromObject(obj);
      const size = new THREE.Vector3(); box.getSize(size);
      const along = m.lengthAxis === 'x' ? size.x : size.z;
      const k = (m.scale != null) ? m.scale : (along > 1e-6 ? this.spec.L/along : 1);
      obj.scale.setScalar(k);
      if(m.rotationY) obj.rotation.y = m.rotationY;
      const off = m.offset || [0,0,0];
      obj.position.set(off[0], off[1], off[2]);

      this.group.remove(this.procedural);
      this.group.add(obj);
      this.modelRoot = obj;
      this.rigs = [];                    // the model carries its own canvas
      return true;
    }catch(err){
      console.warn('[' + this.spec.id + '] could not load ' + (m.glb || 'embedded model') +
                   ' — keeping the procedural hull. ' + (err && err.message || err));
      return false;
    }
  }

  // A soft foam trail astern, painted into a canvas so it has no hard edges.
  _buildWake(scene){
    const spec = this.spec;
    const c=document.createElement('canvas'); c.width=64; c.height=160;
    const g=c.getContext('2d');
    g.clearRect(0,0,64,160);
    for(let i=0;i<160;i++){
      const t=(159-i)/159;                 // 0 at the stern → 1 far astern
      const halfW=6+t*26;
      const a=(1-t)*(1-t)*0.5*Math.min(1,t*8);
      const grd=g.createLinearGradient(32-halfW,0,32+halfW,0);
      grd.addColorStop(0,'rgba(255,255,255,0)');
      grd.addColorStop(0.5,'rgba(255,255,255,'+a.toFixed(3)+')');
      grd.addColorStop(1,'rgba(255,255,255,0)');
      g.fillStyle=grd; g.fillRect(32-halfW,i,halfW*2,1);
    }
    this.wake = new THREE.Mesh(
      new THREE.PlaneGeometry(spec.B*2.2, spec.L*1.6),
      new THREE.MeshBasicMaterial({map:new THREE.CanvasTexture(c), color:0xdcecf4,
        transparent:true, opacity:0, depthWrite:false}));
    this.wake.rotation.order = 'YXZ';
    scene.add(this.wake);
  }

  /* Make every part of her breathe the same air as the sea. Called after any
     glTF model is adopted too, so an imported hull fades with the rest instead
     of hanging sharp in the haze. */
  applyAtmosphere(oceanUniforms){
    const patch = obj => {
      if(!obj.material) return;
      const mats = Array.isArray(obj.material) ? obj.material : [obj.material];
      for(const m of mats) Naval.applyHaze(m, oceanUniforms);
    };
    this.group.traverse(patch);
    if(this.wake) patch(this.wake);
  }

  syncTo(body){
    this.group.position.copy(body.pos);
    this.group.quaternion.copy(body.quat);
  }

  setTrim(sheet, tack, sailsSet, luffing, t){
    if(!this.rigs.length) return;          // a glTF model trims itself
    const shake = luffing ? Math.sin(t*11)*0.10 : 0;
    const angle = tack * sheet + shake;
    for(const rig of this.rigs){
      rig.visible = sailsSet;
      rig.rotation.y = (rig===this.jibRig ? angle*0.75 : angle);
    }
  }

  updateWake(body, ocean, t){
    const spec = this.spec;
    const spd = body.vel.length();
    this._fwd.set(0,0,1).applyQuaternion(body.quat);
    this.wake.position.copy(this._fwd).multiplyScalar(-spec.L*1.3).add(body.pos);
    this.wake.position.y = ocean.sample(this.wake.position.x, this.wake.position.z, t) + 0.08;
    this.wake.rotation.set(-Math.PI/2, Math.atan2(this._fwd.x, this._fwd.z), 0);
    this.wake.scale.set(1, 0.55 + Math.min(1.1, spd*0.10), 1);
    this.wake.material.opacity = Math.min(0.32, spd*0.045);
  }

  dispose(){
    this.scene.remove(this.group);
    this.scene.remove(this.wake);
  }
};

/* GLTFLoader is not part of the three.js core bundle. Pull it in only when a
   spec actually names a model, so a session that never loads one pays nothing
   and never touches the network. */
Naval.base64ToArrayBuffer = function(b64){
  const bin = atob(b64);
  const bytes = new Uint8Array(bin.length);
  for(let i=0;i<bin.length;i++) bytes[i] = bin.charCodeAt(i);
  return bytes.buffer;
};

Naval.loadGLTFLoader = async function(){
  if(Naval._GLTFLoader) return Naval._GLTFLoader;
  // Resolved through the import map in the page, so the loader and its "three"
  // dependency agree on one version.
  const mod = await import('three/addons/loaders/GLTFLoader.js');
  Naval._GLTFLoader = mod.GLTFLoader;
  return Naval._GLTFLoader;
};
