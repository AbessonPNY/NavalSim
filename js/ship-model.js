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
      /* Sailcloth is thin and translucent. The directional part of that is
         Naval.applySailLight, applied in applyAtmosphere; what stays here is a
         faint emissive for the light the sky sends through the weave from every
         quarter at once, which no single lobe accounts for. It used to carry
         this term alone, at four times the strength, standing in for the whole
         effect — the sail then glowed identically whatever the sun did. */
      canvas: new THREE.MeshStandardMaterial({
        color:hex(A.canvas), roughness:0.95, side:THREE.DoubleSide,
        emissive:0x8d866f, emissiveIntensity:0.12})
    };

    this.procedural = new THREE.Group();     // everything we build ourselves
    this.group.add(this.procedural);
    this.rigs = [];                          // what braces or swings when trimmed
    this.canvases = [];                      // the cloth alone — furling hides only this

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

  /* One sail, as a surface rather than a sheet.

     A flat quad reads as sheet metal however it is lit, because its normal is
     constant: one flat shade over the whole cloth, and nowhere for the light to
     turn. So a sail is built as a grid across its four corners and pushed out
     along its own normal by sin(πu)·sin(πv) — nil along every edge, canvas
     being bent to its spars and hauled down at its clews, and fullest in the
     middle where nothing holds it.

     Corners come in cyclic order, as the flat quads took them, so a
     three-cornered sail just repeats its last corner and the grid closes along
     that edge. No depth is baked in here: setSailShape sets it every frame. */
  _sailSurface(corners, dir, nu, nv){
    nu = nu || 8; nv = nv || 6;
    const c00=corners[0], c10=corners[1], c11=corners[2], c01=corners[3] || corners[2];
    const pos=[], w=[], us=[], idx=[];
    const a=new THREE.Vector3(), b=new THREE.Vector3(), p=new THREE.Vector3();
    for(let j=0;j<=nv;j++){
      const v = j/nv;
      a.lerpVectors(c00, c01, v);                 // down one leech
      b.lerpVectors(c10, c11, v);                 // down the other
      for(let i=0;i<=nu;i++){
        const u = i/nu;
        p.lerpVectors(a, b, u);
        pos.push(p.x, p.y, p.z);
        w.push(Math.sin(Math.PI*u)*Math.sin(Math.PI*v));
        us.push(u);
      }
    }
    for(let j=0;j<nv;j++) for(let i=0;i<nu;i++){
      const k = j*(nu+1)+i;
      idx.push(k, k+nu+1, k+nu+2,  k, k+nu+2, k+1);
    }
    const g = new THREE.BufferGeometry();
    g.setAttribute('position', new THREE.Float32BufferAttribute(pos,3));
    g.setIndex(idx);
    g.computeVertexNormals();
    const mesh = new THREE.Mesh(g, this.mats.canvas);
    mesh.userData.sail = { base:Float32Array.from(pos), w:Float32Array.from(w),
                           u:Float32Array.from(us), dir:dir.clone().normalize() };
    this.canvases.push(mesh);
    return mesh;
  }

  /* Fill the canvas, or empty it.

     The depth is the pressure the cloth is under, which the solver already
     works out in order to push her along — so she fills as she is trimmed and
     goes slack the moment the sheets are started, with no second rule to keep
     in step with the first. A luffing sail holds almost none of it and shivers
     instead, the shake running from luff to leech as it does on the water. */
  setSailShape(load, luffing, t){
    const full = this.spec.rig.belly || 0.8;
    const press = Math.min(1, Math.max(0, load/35));   // Pa — a fresh breeze fills her
    const depth = luffing ? full*0.12 : full*press;
    for(const m of this.canvases){
      const s = m.userData.sail;
      if(!s) continue;
      const attr = m.geometry.attributes.position, arr = attr.array;
      const base = s.base, w = s.w, u = s.u, d = s.dir;
      for(let k=0, n=w.length; k<n; k++){
        const i3 = k*3;
        const f = w[k]*(depth + (luffing ? full*0.22*Math.sin(u[k]*7 - t*9) : 0));
        arr[i3  ] = base[i3  ] + d.x*f;
        arr[i3+1] = base[i3+1] + d.y*f;
        arr[i3+2] = base[i3+2] + d.z*f;
      }
      attr.needsUpdate = true;
      m.geometry.computeVertexNormals();        // the shading is the whole point
    }
  }

  /* Gaff sail on a swinging boom. The group pivots on the mast, so sheeting in
     or out turns the boom and its canvas together. */
  _gaffRig(m, sc){
    const spec = this.spec;
    const V = (x,y,z)=>new THREE.Vector3(x,y,z);
    const tackY = spec.deckMid + m.tackAbove;
    const rig = new THREE.Group();
    rig.position.set(0, 0, m.z);
    const boom = new THREE.Mesh(
      new THREE.CylinderGeometry(0.07*sc, 0.09*sc, m.boom, 8), this.mats.spar);
    boom.rotation.x = Math.PI/2;
    boom.position.set(0, tackY, -m.boom*0.45);
    rig.add(boom);
    // Cut flat, on the centreline; the belly comes from setSailShape, to leeward.
    rig.add(this._sailSurface([
      V(0, tackY+0.05, -0.2*sc),
      V(0, tackY, -m.boom*0.93),
      V(0, spec.deckMid+m.height-1.0*sc, -m.boom*0.70),
      V(0, spec.deckMid+m.height-0.6*sc, -0.2*sc)
    ], new THREE.Vector3(1,0,0)));
    this.procedural.add(rig);
    return rig;
  }

  /* Square rig: yards crossed on the mast, each carrying a rectangular sail.
     The whole group braces round as one, which is how a square-rigger is
     trimmed — you brace the yards, you do not ease a boom. */
  _squareRig(m, sc){
    const spec = this.spec;
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

      // the sail hangs below its yard; setSailShape bellies it forward
      const V = (x,yy,z)=>new THREE.Vector3(x,yy,z);
      rig.add(this._sailSurface([
        V(-span,      y,      0),
        V( span,      y,      0),
        V( span*0.86, y-drop, 0),
        V(-span*0.86, y-drop, 0)
      ], new THREE.Vector3(0,0,1)));
    }
    this.procedural.add(rig);
    return rig;
  }

  // Jib, set flying — pivots on the forestay at the stem.
  _jibRig(){
    const spec = this.spec, L = this.lines, j = spec.rig.jib;
    const V = (x,y,z)=>new THREE.Vector3(x,y,z);
    const rig = new THREE.Group();
    rig.position.set(0, 0, spec.L/2);
    const fore = spec.masts[0];
    const back = fore.z - spec.L/2;              // foremost mast, in this frame
    rig.add(this._sailSurface([
      V(0, L.deckY(1)+j.tackAbove, spec.jibFootZ),
      V(0, spec.deckMid+j.clewAbove, back+0.6),
      V(0, spec.deckMid+fore.height-j.headDrop, back+0.15)
    ], new THREE.Vector3(1,0,0)));
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

      // Scale her HULL to the length the solver is using — never the whole
      // object. See _hullScale.
      const k = (m.scale != null) ? m.scale : this._hullScale(obj, m.lengthAxis);
      obj.scale.setScalar(k);
      if(m.rotationY) obj.rotation.y = m.rotationY;
      const off = m.offset || [0,0,0];
      obj.position.set(off[0], off[1], off[2]);

      this.group.remove(this.procedural);
      this.group.add(obj);
      this.modelRoot = obj;
      this.rigs = []; this.canvases = [];   // the procedural rig went with the hull
      this._rigModel();
      return true;
    }catch(err){
      console.warn('[' + this.spec.id + '] could not load ' + (m.glb || 'embedded model') +
                   ' — keeping the procedural hull. ' + (err && err.message || err));
      return false;
    }
  }

  /* The scale that brings the model's HULL to her stated length.

     Measuring the whole object instead counted her bowsprit and her yards as
     though they were ship: the Roter Löwe came out with 47.5 m of hull where
     the solver was floating 60, and everything derived from her stated
     dimensions then stood proud of the timber — the foam collar most visibly,
     overhanging her stem and stern by better than six metres.

     The hull is picked out as the bulkiest mesh, the same way _deckProfile
     finds it: spars are long but they enclose almost nothing. Called before
     any transform is put on the model, so a mesh's own world box is already in
     the model's frame. */
  _hullScale(obj, lengthAxis){
    const size = new THREE.Vector3();
    let best = -1, along = 0;
    obj.traverse(o => {
      if(!o.isMesh || !o.geometry) return;
      new THREE.Box3().setFromObject(o).getSize(size);
      const vol = size.x*size.y*size.z;
      if(vol > best){ best = vol; along = (lengthAxis === 'x' ? size.x : size.z); }
    });
    return along > 1e-6 ? this.spec.L/along : 1;
  }

  /* Every mesh of the loaded model, measured in the hull's own frame. Runs once
     per commissioning over a handful of meshes, so the geometry clone it costs
     is cheaper than carrying a parallel description of the model around. */
  _modelParts(){
    this.group.updateWorldMatrix(true, true);
    const toLocal = new THREE.Matrix4().copy(this.group.matrixWorld).invert();
    const rel = new THREE.Matrix4(), parts = [];
    this.modelRoot.traverse(o => {
      if(!o.isMesh || !o.geometry) return;
      o.updateWorldMatrix(true, false);
      const g = o.geometry.clone();
      g.applyMatrix4(rel.multiplyMatrices(toLocal, o.matrixWorld));
      g.computeBoundingBox();
      const box = g.boundingBox.clone();
      parts.push({ mesh:o, geom:g, box,        // geom is the caller's to dispose
                   size:box.getSize(new THREE.Vector3()),
                   mid: box.getCenter(new THREE.Vector3()) });
    });
    return parts;
  }

  /* The height of her deck along her length, read off the model's own hull.

     No sail may hang through the ship, and the only thing that knows where the
     deck is, is the model. One number for the whole vessel would not do: a
     full-bodied sixteenth-century hull carries her poop the better part of ten
     metres above her waist, so a single figure would either fly the courses or
     bury them. */
  _deckProfile(parts){
    let hull = parts[0], best = -1;
    for(const p of parts){
      const v = p.size.x*p.size.y*p.size.z;    // the hull is far the bulkiest mesh
      if(v > best){ best = v; hull = p; }
    }
    const N = 24, z0 = hull.box.min.z;
    const step = (hull.box.max.z - z0)/N || 1;
    const top = new Array(N).fill(-Infinity);
    const pos = hull.geom.attributes.position;
    const bin = z => Math.min(N-1, Math.max(0, Math.floor((z-z0)/step)));
    for(let i=0;i<pos.count;i++){
      const b = bin(pos.getZ(i)), y = pos.getY(i);
      if(y > top[b]) top[b] = y;
    }
    // Off the ends of the hull — under the bowsprit — take the nearest station
    // that has any ship under it at all.
    return z => {
      const b = bin(z);
      for(let d=0; d<N; d++){
        if(top[b-d] > -Infinity) return top[b-d];
        if(top[b+d] > -Infinity) return top[b+d];
      }
      return 0;
    };
  }

  /* Hang canvas on an imported model's own spars.

     A .glb arrives with a hull and bare spars but, as a rule, no sails — the
     Roter Löwe has none, and her nodes carry Blender's default names, so there
     is nothing to look them up BY. What she does have is yards, and a yard is
     unmistakable by shape alone: a spar many times wider athwartships than it
     is thick, lying square across the centreline. Reading them off the geometry
     means any square-rigged model dropped into ships/models comes out rigged,
     with not one line of per-vessel data to write — the folder stays the
     authority, as it does for the specs themselves.

     The yards are then re-parented INTO the pivots that carry the sails, so
     bracing swings spar and cloth as one piece. Turning the canvas alone would
     slide it off its own yard. */
  _rigModel(){
    const spec = this.spec;
    if(!(spec.sailArea > 0)) return;         // she is not meant to carry canvas

    const parts = this._modelParts();
    const yards = parts.filter(p => {
      const across = p.size.x, thick = Math.max(p.size.y, p.size.z);
      return across > 4*thick                    // long and thin, and thin the long way
          && across > 0.25*spec.B                // a spar, not a bit of deck gear
          && Math.abs(p.mid.x) < 0.15*across;    // squarely across the centreline
    });
    const deckAt = this._deckProfile(parts);
    for(const p of parts) p.geom.dispose();      // measurements taken; buffers freed

    if(!yards.length){
      console.warn('[' + spec.id + '] no yards found in the model — she sails under ' +
                   'bare poles. A gaff or lateen model needs its own rigging path.');
      return;
    }

    /* Sort them onto masts. Yards cluster tightly in z about their own mast,
       and the spacing between masts is an order of magnitude wider than the
       spread within one, so a single gap test separates them. */
    yards.sort((a,b) => a.mid.z - b.mid.z);
    const masts = [], together = 0.06*spec.L;
    for(const y of yards){
      const last = masts[masts.length-1];
      if(last && Math.abs(y.mid.z - last[0].mid.z) < together) last.push(y);
      else masts.push([y]);
    }

    for(const mast of masts){
      mast.sort((a,b) => b.mid.y - a.mid.y);            // highest yard first
      const z0 = mast.reduce((s,y) => s + y.mid.z, 0) / mast.length;
      const pivot = new THREE.Group();
      pivot.position.set(0, 0, z0);
      this.group.add(pivot);

      for(let i=0;i<mast.length;i++){
        const y = mast[i], half = y.size.x*0.5*0.97;
        pivot.attach(y.mesh);      // braces with the mast, canvas or no canvas

        /* A sail hangs to just short of the yard beneath it. The lowest one has
           nothing below to measure against and borrows the gap above; none may
           be deeper than about half its width; and none may reach past the deck
           under it — a course is sheeted home to the rail, not through it. */
        const above = mast[i-1], below = mast[i+1];
        const gap = below ? (y.mid.y - below.mid.y)
                  : above ? (above.mid.y - y.mid.y)
                  : half*2;
        const dz = y.mid.z - z0, yy = y.mid.y;
        const drop = Math.min(0.82*gap, 1.1*half,
                              yy - deckAt(y.mid.z) - 0.02*spec.L);
        if(drop < 0.2*half) continue;   // too near the deck to be a yard at all

        const V = (x,ay,z)=>new THREE.Vector3(x,ay,z);
        pivot.add(this._sailSurface([
          V(-half,      yy,      dz),
          V( half,      yy,      dz),
          V( half*0.86, yy-drop, dz),
          V(-half*0.86, yy-drop, dz)
        ], new THREE.Vector3(0,0,1)));
      }
      this.rigs.push(pivot);
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
    // Before the haze, which chains onto it and must dim it in its turn.
    Naval.applySailLight(this.mats.canvas, oceanUniforms);
    const patch = obj => {
      if(!obj.material) return;
      const mats = Array.isArray(obj.material) ? obj.material : [obj.material];
      for(const m of mats) Naval.applyHaze(m, oceanUniforms);
    };
    this.group.traverse(patch);
    if(this.wake) patch(this.wake);
  }

  /* Her half breadths along the waterline, stern to stem, as fractions of the
     greatest one — what the sea needs to foam along the real plating instead of
     around an ellipse.

     A model is measured off its own hull mesh, since that is the shape the eye
     actually sees; a procedural vessel is read from hull-lines.js, the one plan
     of forms. Reading a model from hull-lines would foam around a ship that is
     not the one drawn.

     The band runs from just under the waterline to the top of her wale, not
     from the waterline alone. What has to be traced is the line the EYE sees
     her make in the water, and a flared hull stands over her own waterline: on
     the Roter Löwe the difference is 6.35 m against 7.7, so an outline taken at
     the waterline exactly is drawn underneath her own topsides and vanishes.
     Her full beam higher up would be no better — that stands clear of the sea
     altogether. */
  hullProfile(n, waterlineY){
    n = n || 64;
    const halfLen = this.spec.L*0.5;
    const out = new Float32Array(n);

    if(this.modelRoot){
      const parts = this._modelParts();
      let hull = parts[0], best = -1;
      for(const p of parts){
        const v = p.size.x*p.size.y*p.size.z;
        if(v > best){ best = v; hull = p; }
      }
      const lo = waterlineY - 0.02*this.spec.L, hi = waterlineY + 0.06*this.spec.L;
      const pos = hull.geom.attributes.position;
      for(let i=0;i<pos.count;i++){
        const y = pos.getY(i);
        if(y < lo || y > hi) continue;
        const b = Math.min(n-1, Math.max(0,
                    Math.floor((pos.getZ(i) + halfLen)/(2*halfLen)*n)));
        const x = Math.abs(pos.getX(i));
        if(x > out[b]) out[b] = x;
      }
      for(const p of parts) p.geom.dispose();

      // Stations the band missed are bridged between the nearest measured
      // neighbours, read off a copy so a filled station never seeds the next.
      const raw = Float32Array.from(out);
      for(let i=0;i<n;i++){
        if(raw[i] > 0) continue;
        let j = i-1; while(j >= 0 && raw[j] === 0) j--;
        let k = i+1; while(k < n  && raw[k] === 0) k++;
        if(j < 0 && k >= n) continue;
        if(j < 0)       out[i] = raw[k]*(i+1)/(k+1);          // taper in from her end
        else if(k >= n) out[i] = raw[j]*(n-i)/(n-j);
        else            out[i] = raw[j] + (raw[k]-raw[j])*(i-j)/(k-j);
      }
    }else{
      for(let i=0;i<n;i++) out[i] = this.lines.halfB((i + 0.5)/n);
    }

    /* Fair the line. A .glb hull carries only a few hundred vertices, so spread
       over sixty-four stations most receive one or two and the greatest-x rule
       returns them in facets — which is exactly the sawtooth collar. Three
       passes of a 1-2-1 kernel take that out and leave the run of the plating
       alone. The end stations are pinned so she keeps her points. */
    for(let pass=0; pass<3; pass++){
      const prev = Float32Array.from(out);
      for(let i=1;i<n-1;i++) out[i] = (prev[i-1] + 2*prev[i] + prev[i+1])*0.25;
    }

    let max = 0;
    for(let i=0;i<n;i++) if(out[i] > max) max = out[i];
    if(max <= 1e-4){                       // nothing measurable — fall back square
      out.fill(1);
      return { fractions:out, maxHalfB:this.spec.B*0.5, halfLen,
               ends:{ aft:-halfLen, fwd:halfLen } };
    }
    for(let i=0;i<n;i++) out[i] /= max;

    // Where her waterline body actually begins and ends, so the sea does not
    // trace an outline out to her length overall where she has already finished.
    let aft = 0, fwd = n-1;
    while(aft < n-1 && out[aft] < 0.12) aft++;
    while(fwd > 0   && out[fwd] < 0.12) fwd--;
    const stationZ = i => -halfLen + ((i + 0.5)/n)*2*halfLen;
    const ends = (fwd > aft) ? { aft:stationZ(aft), fwd:stationZ(fwd) }
                             : { aft:-halfLen, fwd:halfLen };
    return { fractions:out, maxHalfB:max, halfLen, ends };
  }

  /* Let her cast and take her own shadows. This is where the relief in the
     image comes from: canvas darkening the deck beneath it, the hull shading
     its own lee side, one mast striping the sail behind it. */
  enableShadows(){
    this.group.traverse(o => {
      if(!o.isMesh) return;
      o.castShadow = true;
      o.receiveShadow = true;
    });
  }

  syncTo(body){
    this.group.position.copy(body.pos);
    this.group.quaternion.copy(body.quat);
  }

  setTrim(sheet, tack, sailsSet, luffing, t, load){
    if(!this.rigs.length) return;          // no canvas to trim
    const shake = luffing ? Math.sin(t*11)*0.10 : 0;
    const angle = tack * sheet + shake;
    /* Furling takes in the cloth, not the spars — a vessel under bare poles
       still has her yards crossed and her boom shipped. It matters twice over
       for an imported model, whose own yards now hang in these pivots: hiding
       the group would strip her rig off the masts. */
    for(const c of this.canvases) c.visible = sailsSet;
    for(const rig of this.rigs){
      rig.rotation.y = (rig===this.jibRig ? angle*0.75 : angle);
    }
    // furled canvas is hidden, so there is nothing to reshape
    if(sailsSet) this.setSailShape(load || 0, luffing, t);
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
