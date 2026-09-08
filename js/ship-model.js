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

/* A soft round glow, drawn rather than loaded — a published page cannot fetch a
   local image, and this is three lines of canvas. Built once and shared: every
   lantern in the fleet wants the same one. */
Naval.glowTexture = function(){
  if(Naval._glowTex) return Naval._glowTex;
  const s = 128, cv = document.createElement('canvas');
  cv.width = cv.height = s;
  const ctx = cv.getContext('2d');
  const g = ctx.createRadialGradient(s/2, s/2, 0, s/2, s/2, s/2);
  g.addColorStop(0.00, 'rgba(255,255,255,1)');
  g.addColorStop(0.16, 'rgba(255,228,170,0.92)');
  g.addColorStop(0.42, 'rgba(255,170,70,0.32)');
  g.addColorStop(1.00, 'rgba(255,140,40,0)');
  ctx.fillStyle = g;
  ctx.fillRect(0, 0, s, s);
  const tex = new THREE.CanvasTexture(cv);
  tex.colorSpace = THREE.SRGBColorSpace;
  Naval._glowTex = tex;
  return tex;
};

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
        emissive:0x8d866f, emissiveIntensity:0.12}),
      // bunting is lighter and thinner than sailcloth, and a plain white
      // ensign has to stay white against a bright sky rather than go grey
      flag: new THREE.MeshStandardMaterial({
        color:0xf6f4ef, roughness:0.88, side:THREE.DoubleSide,
        emissive:0x7c7a72, emissiveIntensity:0.14})
    };

    this.procedural = new THREE.Group();     // everything we build ourselves
    this.group.add(this.procedural);
    this.rigs = [];                          // what braces or swings when trimmed
    this.canvases = [];                      // the cloth alone — furling hides only this

    this._buildHull();
    this._buildRig();
    this._buildFlag();
    this._buildLantern();
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

  /* The depth of the cloth along one axis of the sail.

     `d` is where she is deepest and `pin0`/`pin1` say whether each end is
     LACED to something. That distinction is the whole point: a laced edge is
     drawn flat against its spar, but a free edge is held only at its two
     corners and keeps most of its fullness right out to the boltrope, so it
     leaves at a fraction `r` of the maximum rather than at nothing. */
  _bellyProfile(x, d, pin0, pin1, r, crown){
    /* sin(π·x^k) is a half-sine whose crest has been slid to d: the exponent is
       chosen so that x = d maps to the half-way point of the sine. */
    const k = Math.log(0.5)/Math.log(d);
    let f = Math.sin(Math.PI*Math.pow(x, k));
    if(!pin1 && x > d) f = 1 - (1-r)*Math.pow((x-d)/(1-d), 2);
    if(!pin0 && x < d) f = 1 - (1-r)*Math.pow((d-x)/d, 2);
    /* And then flattened at the top. A half-sine is a BUMP: rounded at the
       crown and easing away in every direction. Full canvas is a CUSHION —
       broad and near-flat across the middle, and turning down hard only in the
       last of its width, where the boltrope holds it. Raising the profile to a
       power below one does exactly that: it lifts everything off the edges
       without moving the crest. */
    return crown === 1 ? f : Math.pow(f, crown);
  }

  /* One sail, as a surface rather than a sheet.

     A flat quad reads as sheet metal however it is lit, because its normal is
     constant: one flat shade over the whole cloth, and nowhere for the light to
     turn. So a sail is built as a grid across its four corners and pushed out
     along its own normal, deepest where nothing holds it.

     The first cut used sin(πu)·sin(πv), which is a bubble: deepest dead centre
     and nil along all four edges. Two things are wrong with that, and they are
     wrong on every sail in the rig. A filled sail is deepest WELL FORWARD of
     the middle of her chord — about four tenths back — because that is where
     the air turns, not halfway. And only the edges actually bent to a spar or
     a stay are flat: a square sail's foot is held by nothing but her two
     clews, so she bellies right out to the boltrope. That curving foot is the
     most recognisable thing about a square-rigger under canvas, and pinning it
     to zero flattened precisely the part the eye reads.

     So `cut` says, per axis, where she is deepest and which of her edges are
     laced. Its defaults are the old bubble, so a sail that says nothing is
     shaped exactly as before.

     Corners come in cyclic order, as the flat quads took them, so a
     three-cornered sail just repeats its last corner and the grid closes along
     that edge. No depth is baked in here: setSailShape sets it every frame. */
  _sailSurface(corners, dir, cut){
    const c = cut || {};
    const uPeak = c.uPeak !== undefined ? c.uPeak : 0.5;
    const vPeak = c.vPeak !== undefined ? c.vPeak : 0.5;
    const uPin0 = c.uPin0 !== false, uPin1 = c.uPin1 !== false;
    const vPin0 = c.vPin0 !== false, vPin1 = c.vPin1 !== false;
    const free  = c.free !== undefined ? c.free : 0.78;
    /* A free edge does not carry the same share of the belly on both axes: a
       leech swings off to leeward in a long curve, a foot is pulled taut
       between two clews. So the chord gets its own figure. */
    const chordFree = c.freeU !== undefined ? c.freeU : free;
    /* And an edge free to BELLY is not necessarily free to HANG. A leech is
       set up taut between earing and clew by the sheet: she swings off to
       leeward bodily, but she does not sag along her own length. A foot is
       held at its two corners and by nothing else, and does. Hanging
       therefore has its own flags, falling back on the belly's. */
    const hPinU0 = c.hangU0 !== undefined ? c.hangU0 : uPin0;
    const hPinU1 = c.hangU1 !== undefined ? c.hangU1 : uPin1;
    const hPinV0 = c.hangV0 !== undefined ? c.hangV0 : vPin0;
    const hPinV1 = c.hangV1 !== undefined ? c.hangV1 : vPin1;
    /* Below one, the belly reads as a cushion; at one, as the old bump. It is
       a property of her SECTION, so it belongs to the chord and to nothing
       else: applied down the sail as well, it flattened out the very
       difference between a full head and a drawn foot that makes her profile
       readable end-on. */
    const crown = c.crown !== undefined ? c.crown : 1;
    /* The GORE: how far her leeches are cut INSIDE the straight line joining
       earing to clew, and how far her foot is cut up above the line joining
       her two clews — both as fractions of her head and of her drop. Nil
       leaves the old ruled edges. */
    const bow = c.bow !== undefined ? c.bow : 0;
    const roachFoot = c.roachFoot !== undefined ? c.roachFoot : 0;
    /* Eight by eight rather than eight by six: the interesting shape is now the
       one down the sail, and six rows read the deep low belly as facets. */
    const nu = 8, nv = 8;
    /* How free the cloth is along one axis: nil against a laced edge, one at a
       free one. It is what decides where she is allowed to hang. */
    const hangU = x => (hPinU0 ? 0 : (1-x)*(1-x)) + (hPinU1 ? 0 : x*x);
    const hangV = x => (hPinV0 ? 0 : (1-x)*(1-x)) + (hPinV1 ? 0 : x*x);
    const c00=corners[0], c10=corners[1], c11=corners[2], c01=corners[3] || corners[2];

    /* Her edges are CUT, and that is a fact about the cloth, not about the
       wind. A square sail is bent to a straight yard, so her head is a ruled
       line — but every other edge is cut HOLLOW, and sets as a fair curve
       falling inward. That outline is what the eye reads first, and while the
       edges stayed ruled she could be bellied and shaded all we liked and
       still read as a rectangle with a gradient laid on it.

       Hollow, not round, and that is the whole of the difference. The leeches
       are gored in at mid-height so the cloth stands clear of the shrouds and
       the boltrope takes the strain in a straight run; the foot is cut UP in
       the middle — the roach of a course, which exists to clear the stays and
       the mast below. So the widest points of a square sail are her corners,
       and her waist is the narrowest part of her. Cut the other way she swells
       between her spars like a pillowcase on a line.

       Being a matter of the cut, all of it is baked into the base geometry
       rather than driven by the fill: canvas hanging slack keeps her shape,
       she does not lose it when the sheets are started. Nil at the earings and
       nil at the clews, which are hauled taut into their corners. */
    const across = new THREE.Vector3().subVectors(c10, c00);
    const head = across.length();
    if(head > 1e-6) across.divideScalar(head);
    const up = new THREE.Vector3().subVectors(c00, c01);
    const drop = up.length();
    if(drop > 1e-6) up.divideScalar(drop);
    const kRoach = Math.log(0.5)/Math.log(0.58);   // deepest at v = 0.58

    const pos=[], w=[], sag=[], us=[], idx=[];
    const a=new THREE.Vector3(), b=new THREE.Vector3(), p=new THREE.Vector3();
    for(let j=0;j<=nv;j++){
      const v = j/nv;
      a.lerpVectors(c00, c01, v);                 // down one leech
      b.lerpVectors(c10, c11, v);                 // down the other
      for(let i=0;i<=nu;i++){
        const u = i/nu;
        p.lerpVectors(a, b, u);
        if(bow) p.addScaledVector(across,
          -bow*head*(2*u - 1)*Math.sin(Math.PI*Math.pow(v, kRoach)));
        if(roachFoot) p.addScaledVector(up,
          roachFoot*drop*Math.sin(Math.PI*u)*v*v);
        pos.push(p.x, p.y, p.z);
        w.push(this._bellyProfile(u, uPeak, uPin0, uPin1, chordFree, crown)
             * this._bellyProfile(v, vPeak, vPin0, vPin1, free, 1));
        /* Canvas hangs. A free foot is longer than the straight line between
           her clews, so she smiles between them — nil at the corners, which are
           hauled taut, and nil against any edge that is laced to a spar. That
           curve is the line the eye reads on a square-rigger before any other,
           and a foot ruled straight is the giveaway of a sail drawn rather than
           bent. Only the free axis sags: a leech is tensioned, not hung. */
        sag.push(Math.max(hangU(u)*Math.sin(Math.PI*v),
                          hangV(v)*Math.sin(Math.PI*u)));
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
                           sag:Float32Array.from(sag), u:Float32Array.from(us),
                           dir:dir.clone().normalize() };
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
      const base = s.base, w = s.w, sg = s.sag, u = s.u, d = s.dir;
      /* And then she hangs a little, on top of whatever she was cut. The cut
         is the larger of the two by some way — the foot of a course stands
         well above the line of her clews whatever the wind does — so this only
         eases the roach, it never turns it back into a smile. */
      const hang = full*(0.10 + 0.06*press);
      for(let k=0, n=w.length; k<n; k++){
        const i3 = k*3;
        const f = w[k]*(depth + (luffing ? full*0.22*Math.sin(u[k]*7 - t*9) : 0));
        arr[i3  ] = base[i3  ] + d.x*f;
        arr[i3+1] = base[i3+1] + d.y*f - (sg ? sg[k]*hang : 0);
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
      // laced on three sides; deepest four tenths abaft the luff
    ], new THREE.Vector3(1,0,0), { uPeak:0.42, crown:0.80 }));
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
      /* She is deepest HIGH, just under her own yard, and drawn near flat at
         the foot. That is not where a free edge would put it, and it is the
         sheets that decide: the clews of a square sail are hauled down and
         OUT to the yardarms of the yard beneath her, so the foot is stretched
         along a spar it is not bent to, while the cloth immediately under her
         own yard has nothing pulling it anywhere and bags. Seen end-on, that
         is the whole profile of her — full at the top, flat at the bottom. */
      rig.add(this._sailSurface([
        V(-span,      y,      0),
        V( span,      y,      0),
        V( span*0.86, y-drop, 0),
        V(-span*0.86, y-drop, 0)
      ], new THREE.Vector3(0,0,1),
         { vPeak:0.30, vPin1:false, free:0.25, crown:0.55,
           uPin0:false, uPin1:false, freeU:0.35, hangU0:true, hangU1:true,
           bow:0.05, roachFoot:0.11 }));
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
      // hanked to her stay up the luff, but her foot flies free
    ], new THREE.Vector3(1,0,0), { uPeak:0.40, vPeak:0.34, vPin0:false, crown:0.80 }));
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
      this._buildFlag();
      this._buildLantern();
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
        ], new THREE.Vector3(0,0,1),
           { vPeak:0.30, vPin1:false, free:0.25, crown:0.55,
             uPin0:false, uPin1:false, freeU:0.35, hangU0:true, hangU1:true,
             bow:0.05, roachFoot:0.11 }));
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
  applyAtmosphere(oceanUniforms, aoUniforms){
    // Before the haze, which chains onto it and must dim it in its turn.
    Naval.applySailLight(this.mats.canvas, oceanUniforms);
    Naval.applySailLight(this.mats.flag, oceanUniforms);
    const patch = obj => {
      if(!obj.material) return;
      const mats = Array.isArray(obj.material) ? obj.material : [obj.material];
      for(const m of mats){
        if(aoUniforms) Naval.applyShipAO(m, aoUniforms);
        Naval.applyHaze(m, oceanUniforms);
      }
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

  /* A plain white ensign at the main truck.

     It is the one thing aboard that shows the wind itself. The sails only show
     where you have BRACED them; the flag shows where the wind actually is, and
     on the APPARENT wind at that, like everything that flies from a moving deck
     — which is why it swings before the sails do when she rounds up.

     The masthead is found as the yards were, by shape: on a model, the tallest
     piece far higher than it is thick standing on the centreline; on a
     procedural vessel, simply her tallest mast. */
  _buildFlag(){
    if(this.flag){ this.group.remove(this.flag.pivot); this.flag = null; }
    const spec = this.spec;
    let topY, z;

    if(this.modelRoot){
      const parts = this._modelParts();
      let best = null;
      for(const p of parts){
        const thick = Math.max(p.size.x, p.size.z);
        if(p.size.y < 3*thick || p.size.y < 0.15*spec.L) continue;   // not a mast
        if(Math.abs(p.mid.x) > 0.08*spec.B) continue;                // off the centreline
        if(!best || p.box.max.y > best.box.max.y) best = p;
      }
      for(const p of parts) p.geom.dispose();
      if(!best) return;
      topY = best.box.max.y; z = best.mid.z;
    }else{
      let m = null;
      for(const k of spec.masts) if(!m || k.height > m.height) m = k;
      if(!m) return;                       // a vessel under power alone flies none
      topY = spec.deckMid + m.height; z = m.z;
    }

    const hoist = 0.045*spec.L, fly = hoist*1.6;
    const nu = 14, nv = 6;                 // fine along the fly, where it ripples
    const pos = [], us = [], vs = [], idx = [];
    for(let j=0;j<=nv;j++) for(let i=0;i<=nu;i++){
      const u = i/nu, v = j/nv;
      pos.push(0, -v*hoist, u*fly);        // hoist at u=0, streaming down +z
      us.push(u); vs.push(v);
    }
    for(let j=0;j<nv;j++) for(let i=0;i<nu;i++){
      const k = j*(nu+1)+i;
      idx.push(k, k+nu+1, k+nu+2,  k, k+nu+2, k+1);
    }
    const g = new THREE.BufferGeometry();
    g.setAttribute('position', new THREE.Float32BufferAttribute(pos, 3));
    g.setIndex(idx);
    g.computeVertexNormals();

    const pivot = new THREE.Group();
    pivot.position.set(0, topY - hoist*0.18, z);
    pivot.add(new THREE.Mesh(g, this.mats.flag));
    this.group.add(pivot);
    this.flag = { pivot, mesh:pivot.children[0], hoist, fly,
                  base:Float32Array.from(pos), u:Float32Array.from(us), v:Float32Array.from(vs) };
  }

  /* Stream it. The fly points dead downwind, so the pivot's yaw comes straight
     from the apparent wind the solver already knows: its bearing off the bow and
     which tack she is on. Falling light, the ensign stops rippling and hangs —
     it loses its length as it droops, which is what tells you at a glance that
     the breeze has gone, before any instrument does. */
  setFlag(beta, tack, vApp, t){
    const f = this.flag;
    if(!f) return;
    f.pivot.rotation.y = Math.atan2(-tack*Math.sin(beta), -Math.cos(beta));

    const drive = Math.min(1, vApp/8);
    const attr = f.mesh.geometry.attributes.position, arr = attr.array;
    for(let k=0;k<f.u.length;k++){
      const i3 = k*3, u = f.u[k];
      // the ripple starts at nothing on the halyard and builds toward the fly
      arr[i3]   = Math.sin(u*7.0 - t*6.5 + f.v[k]*1.2) * f.hoist*0.42 * drive * Math.pow(u, 1.3);
      arr[i3+1] = f.base[i3+1] - (1-drive)*u*u*f.fly*0.55;
      arr[i3+2] = f.base[i3+2] * (0.80 + 0.20*drive);
    }
    attr.needsUpdate = true;
    f.mesh.geometry.computeVertexNormals();
  }

  /* The poop lantern, and how she is found at night.

     Two glows, not one, and for two different jobs. The near one is a sprite of
     real size in the world, so it grows as you come alongside and reads as a
     lamp hanging over her taffrail. The far one does NOT scale with distance
     (sizeAttenuation off): a lamp of honest size is sub-pixel at two miles and
     simply vanishes, which is the opposite of what a light is for. That one is
     the position mark, and it holds a few pixels however far off she is.

     The texture is drawn on a canvas rather than loaded: the published page
     cannot fetch a local image, and a radial gradient is three lines. */
  _buildLantern(){
    if(this.lantern){ this.group.remove(this.lantern.group); this.lantern = null; }
    const spec = this.spec;
    let y, z;

    if(this.modelRoot){
      const parts = this._modelParts();
      const deckAt = this._deckProfile(parts);
      let hull = parts[0], best = -1;
      for(const p of parts){
        const v = p.size.x*p.size.y*p.size.z;
        if(v > best){ best = v; hull = p; }
      }
      /* Right aft on the taffrail, +z being the bow — and the height taken as
         the HIGHEST point over the after stretch, not the deck at one station.

         A carved stern reads back as a saw: on the Roter Löwe the profile runs
         18.4, then 12.4, then 5.3 metres from one station to the next, the bins
         straddling her galleries and her open rails. Sampling a single station
         dropped the lantern into a trough five metres below her taffrail and a
         little too far forward — she carried it inside her own stern castle. */
      const zA = hull.box.min.z, span = hull.box.max.z - zA;
      z = zA + span*0.02;
      y = -Infinity;
      for(let f=0; f<=0.07; f+=0.01) y = Math.max(y, deckAt(zA + span*f));
      y += 0.10*spec.L/6;
      for(const p of parts) p.geom.dispose();
    }else{
      z = -spec.L*0.45;
      y = this.lines.deckY(0.05) + 0.10*spec.L/6;
    }

    const tex = Naval.glowTexture();
    const group = new THREE.Group();
    group.position.set(0, y, z);

    const k = spec.L/24;
    const mk = (size, atten, op) => {
      const m = new THREE.Sprite(new THREE.SpriteMaterial({
        map:tex, color:0xffcf7a, transparent:true, opacity:op,
        blending:THREE.AdditiveBlending, depthWrite:false,
        sizeAttenuation:atten, fog:false }));
      m.scale.setScalar(size);
      group.add(m);
      return m;
    };
    const halo = mk(3.4*k, true, 0.85);       // the lamp, in metres
    const mark = mk(0.030, false, 0.95);      // the position mark, in screen size

    /* A real flame, not a bulb: she is lit by a wick in a horn lantern, so she
       breathes. Cheap, and it is what stops the mark reading as a HUD marker. */
    const light = new THREE.PointLight(0xffb765, 0, 26*k, 2);
    group.add(light);

    this.group.add(group);
    this.lantern = { group, halo, mark, light, k, seed: Math.random()*100 };
  }

  /* Lit only when it is dark enough to want her. `night` comes from the stage,
     so lantern, sky and the sun's own colour all turn together. */
  setLantern(night, t){
    const L = this.lantern;
    if(!L) return;
    const on = Math.max(0, Math.min(1, night));
    L.group.visible = on > 0.01;
    if(!L.group.visible) return;
    // two slow beats out of phase read as a flame; one alone reads as a pulse
    const flick = 0.86 + 0.14*Math.sin(t*7.3 + L.seed)
                       + 0.06*Math.sin(t*17.1 + L.seed*1.7);
    L.halo.material.opacity = 0.85*on*flick;
    L.mark.material.opacity = 0.95*on*flick;
    L.light.intensity = 2.6*on*flick;
  }

  /* Put her into the lighting: her own shadows, and the layer that the
     occlusion pass renders on its own. She stays on the default layer too, so
     enabling this one changes nothing about how she is normally drawn. */
  enableLighting(){
    this.group.traverse(o => {
      if(!o.isMesh) return;
      o.castShadow = true;
      o.receiveShadow = true;
      o.layers.enable(Naval.SHIP_LAYER);
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
