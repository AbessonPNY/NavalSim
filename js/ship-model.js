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

/* The black flag, DRAWN and not fetched.
 *
 * Not a preference: a published page cannot go and get an image, so the only
 * pictures this project may have are the ones it draws on a canvas — as the
 * lantern, the smoke and the spray already do — or ones living inside a .glb,
 * whose bytes the build carries in base64. A skull on a canvas costs a few
 * dozen lines, embeds itself, and scales to any flag on any vessel.
 *
 * Drawn BOLD on purpose. A device that is legible on a screen at arm's length
 * is a grey smudge on a flag half a mile off: at the size this thing is
 * actually seen, only the largest shapes survive, so the skull is wide, the
 * bones are thick, and there is no detail that will not read as a blob. */
Naval.jollyTexture = function(){
  if(Naval._jollyTex) return Naval._jollyTex;
  const W = 256, H = 160, cv = document.createElement('canvas');
  cv.width = W; cv.height = H;
  const c = cv.getContext('2d');

  c.fillStyle = '#0a0a0c'; c.fillRect(0, 0, W, H);   // the ground
  const cx = W*0.5, cy = H*0.47, s = H/160;
  c.fillStyle = '#eae7df';
  c.strokeStyle = '#eae7df';
  c.lineCap = 'round';

  // --- the two bones, behind ---
  c.lineWidth = 13*s;
  for(const d of [1, -1]){
    c.beginPath();
    c.moveTo(cx - 62*s, cy - d*40*s);
    c.lineTo(cx + 62*s, cy + d*40*s);
    c.stroke();
    // knuckles: a bone end is two lobes, which is what makes it read as bone
    for(const e of [-1, 1]) for(const o of [-1, 1]){
      c.beginPath();
      c.arc(cx + e*62*s, cy + e*d*40*s + o*9*s, 8.5*s, 0, 6.2832);
      c.fill();
    }
  }

  // --- the skull, in front ---
  c.beginPath();
  c.ellipse(cx, cy - 6*s, 40*s, 34*s, 0, 0, 6.2832);
  c.fill();
  // jaw
  c.beginPath();
  c.moveTo(cx - 22*s, cy + 18*s);
  c.lineTo(cx + 22*s, cy + 18*s);
  c.lineTo(cx + 17*s, cy + 40*s);
  c.lineTo(cx - 17*s, cy + 40*s);
  c.closePath();
  c.fill();

  // sockets and nose, punched back out to the ground
  c.fillStyle = '#0a0a0c';
  for(const e of [-1, 1]){
    c.beginPath();
    c.ellipse(cx + e*15*s, cy - 8*s, 11*s, 12.5*s, 0, 0, 6.2832);
    c.fill();
  }
  c.beginPath();
  c.moveTo(cx, cy + 4*s);
  c.lineTo(cx + 7*s, cy + 17*s);
  c.lineTo(cx - 7*s, cy + 17*s);
  c.closePath();
  c.fill();
  // teeth
  c.lineWidth = 3*s;
  c.strokeStyle = '#0a0a0c';
  for(const g of [-11, 0, 11]){
    c.beginPath();
    c.moveTo(cx + g*s, cy + 20*s);
    c.lineTo(cx + g*s, cy + 38*s);
    c.stroke();
  }

  const tex = new THREE.CanvasTexture(cv);
  tex.colorSpace = THREE.SRGBColorSpace;
  Naval._jollyTex = tex;
  return tex;
};

/* A PAINTED SAIL — the one image in this project that is supplied rather than
   drawn, and therefore the one that needs care.

   Every other picture here is made on a canvas element at run time, precisely
   because a published page cannot go and fetch a local file. A device on a
   course cannot be drawn in twenty lines, so this one is an image the spec
   names — and the rule is honoured a different way: `build.js` reads the file
   and rewrites the path in the embedded spec as a `data:` URI, so the string
   that reaches here is a path on the dev server and the bytes themselves in a
   published page. Nothing on this side knows the difference.

   Cached on the source string: two ships sharing a device share one upload.

   ClampToEdge rather than Repeat. The image is a SAIL — one picture over the
   whole cloth, head to foot and leech to leech — not a bolt of material to be
   tiled, and a repeat wrap would smear the outermost row of pixels round to
   the far side the moment a coordinate landed a hair outside. */
Naval.sailTexture = function(src, what){
  Naval._sailTex = Naval._sailTex || {};
  if(Naval._sailTex[src]) return Naval._sailTex[src];
  const t = new THREE.TextureLoader().load(src, undefined, undefined,
    () => console.warn('[' + (what || 'voile') + '] texture introuvable : ' + src +
                       (what ? '' : ' — la toile reste unie')));
  t.colorSpace = THREE.SRGBColorSpace;      // it is artwork, not data
  t.wrapS = t.wrapT = THREE.ClampToEdgeWrapping;
  t.anisotropy = 4;                          // she is seen at a glancing angle
  Naval._sailTex[src] = t;
  return t;
};

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

/* LES NOMS QUI DISENT « CECI ÉCLAIRE » : vitrage, fanal, lampe. Servent deux
   fois — à allumer la nuit, et à REFUSER ces pièces au gréement : une vergue
   est en bois, jamais en verre. Une seule définition, deux usagers. */
Naval.GLOW_NAMES = /fenetre|fen\u00eatre|window|vitre|hublot|glass|verre|lamp|lanterne|lantern|glow/i;

/* LA NUIT TOMBE D'UN COUP SUR LES FEUX, et c'est ce que fait un équipage :
   on allume les fanaux quand il fait nuit et on les souffle à l'aube, on ne les
   baisse pas pendant une heure de crépuscule. `night` du stage monte de 0 au
   coucher à 1 dix degrés plus bas ; on allume au-dessus de `lightAt` et l'on
   éteint sous `snuffAt`, plus bas, ce qui est la même hystérésis que la ligne
   de bord de la barre automatique : sans elle, un soleil qui hésite au seuil
   ferait clignoter tout le bord. `glow` est la force de l'émissive, que
   `model.nightGlow` d'une fiche multiplie encore. Réglable dans settings.json. */
Naval.NIGHT = { glow: 2.6, lightAt: 0.35, snuffAt: 0.25,
                farFrom: 1500, farFade: 1.5, farMinSize: 0.35 };

/* How fast the yards come round, and how long the wind must hold on the other
   side before they are. A little faster than Q/E ease a sheet (0.7 rad/s), so
   the yards never lag the hand that trims them for long; a full swing across
   takes some four seconds, eased at both ends. */
Naval.BRACE_RATE = 0.8;      // rad per second
Naval.BRACE_HOLD = 1.5;      // seconds
Naval.BRACE_ACCEL = 1.0;     // rad per second², both to gather way and to check it

/* The cut of a flag, as the grid sees it: `taper` is how far along the fly it
   keeps its full depth, `tip` the depth left at the end, `notch` how deep the
   fork is cut back into the fly, `length` the fly over the hoist. */
Naval.FLAG_SHAPES = {
  rect:        { taper:0,    tip:1,    notch:0,    length:1.6 },
  swallowtail: { taper:0,    tip:1,    notch:0.28, length:1.7 },
  pennant:     { taper:0,    tip:0.06, notch:0,    length:3.0 },
  streamer:    { taper:0.22, tip:0.14, notch:0.10, length:5.0 }
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
      /* Bunting is lighter and thinner than sailcloth, and a plain white
         ensign has to stay white against a bright sky rather than go grey —
         hence the emissive lift. A BLACK flag wants the opposite: lift it the
         same way and it comes out charcoal, which is a dirty flag and not a
         sinister one. So the device brings its own, much smaller. */
      /* A PAINTED ensign when the sheet names one (`appearance.ensignMap`), the
         drawn death's head otherwise. The IMAGE is what flies; whether she is
         hostile is still read from `ensign` alone, so a sheet can change the
         picture without changing sides, and vice versa. */
      flag: (A && (A.ensign === 'jolly' || A.ensignMap))
        ? new THREE.MeshStandardMaterial({
            map: A.ensignMap ? Naval.sailTexture(A.ensignMap, 'pavillon') : Naval.jollyTexture(),
            roughness:0.92, side:THREE.DoubleSide,
            emissive:0x2a2a2e, emissiveIntensity:0.10 })
        : new THREE.MeshStandardMaterial({
            color:(A && A.ensign) ? parseInt(A.ensign) : 0xf6f4ef,
            roughness:0.88, side:THREE.DoubleSide,
            emissive:0x7c7a72, emissiveIntensity:0.14})
    };

    this.procedural = new THREE.Group();     // everything we build ourselves
    this.group.add(this.procedural);
    this.rigs = [];                          // what braces or swings when trimmed
    this.falls = [];                         // and what can come down, mast and all
    /* Cut rigging is HER state, so it lives on her: she asks for the ends, and
       the shared pool in cordage.js hangs them. A hull that leaves the fleet
       takes its requests with it and there is nothing to remember elsewhere. */
    this.rigCuts = [];                       // ends asked for and not yet hung
    this.rigEpoch = 0;                       // a refit voids every end already out
    this.guns = [];                          // where her muzzles poke out, if any
    /* Where she has been hit, in HER frame, and how badly. Hers alone: the
       uniforms below are handed to her own materials only, so two ships in the
       same fight each show their own wounds. */
    this.scars = [];
    /* HER SNOW: 0 bare, towards 1 a white coat on everything that faces the
       sky. Hers alone — a ship that sailed out of the snow keeps it until it
       melts — and set by the page as the weather goes. */
    this._snowU = { uSnow:{ value:0 } };
    this.snowCover = 0;
    this._scarU = {
      uScar:{ value: Array.from({ length:Naval.SCAR_MAX }, () => new THREE.Vector4()) },
      uScarCount:{ value:0 },
      uScarInv:{ value:new THREE.Matrix4() },   // world → her frame, refreshed in syncTo
      uScarK:{ value:Math.max(0.5, spec.L/30) },  // a wound scales with the ship that takes it
      // painted impacts, when her sheet names them: one atlas, a row per variant, a column per stage
      uScarTex:{ value:null },
      uScarGrid:{ value:new THREE.Vector2(1, 1) },  // stages across, variants down
      uScarMaps:{ value:0 }                         // 0 until the images are in: drawn strokes meanwhile
    };
    const maps = spec.appearance && spec.appearance.impactMaps;
    if(maps && maps.length){
      const atlas = Naval.impactAtlas(maps);
      this._scarU.uScarTex.value = atlas.tex;
      this._scarU.uScarGrid.value.set(atlas.stages, atlas.variants);
      atlas.ready.then(ok => { if(ok) this._scarU.uScarMaps.value = 1; });
    }
    this.shell = null;                       // the side a shot has to get through
    this._shareTot = 0;                      // canvas those masts carry between them
    this.canvases = [];                      // the cloth alone — furling hides only this

    this._buildHull();
    this._buildOars();
    this._buildRig();
    this._buildFlags();
    this._buildLantern();
    this._buildRudder();
    this._buildCrew();
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

  /* THE OARS of a pulling boat: a loom and a blade per thole, pivoting on the
     gunwale. Nothing here pulls — the solver does, at the tholes, and `setOars`
     only reads its stroke phase — so what the eye sees swing is what moves her. */
  _buildOars(){
    const spec = this.spec, O = spec.oars;
    this.oars = null;
    if(!O) return;
    const L = this.lines, len = O.length, list = [];
    const loomGeo = new THREE.CylinderGeometry(0.03, 0.04, len, 8);
    loomGeo.rotateZ(Math.PI/2);                        // along x, the way it is shipped
    const bladeGeo = new THREE.BoxGeometry(0.8, 0.022, 0.16);
    for(let i = 0; i < O.pairs; i++){
      const z = spec.L*(O.pairs > 1 ? 0.12 - 0.26*i/(O.pairs - 1) : 0);
      const t = z/spec.L + 0.5;
      for(const s of [0, 1]){
        const sg = s === 0 ? 1 : -1;                  // larboard +x, starboard −x
        const pivot = new THREE.Group();
        pivot.position.set(sg*L.halfB(t)*0.97, L.deckY(t) + 0.30*(spec.L/24), z);
        const loom = new THREE.Mesh(loomGeo, this.mats.spar);
        loom.position.x = sg*0.2*len;                 // a third inboard of the thole, two outboard
        loom.castShadow = true;
        const blade = new THREE.Mesh(bladeGeo, this.mats.spar);
        blade.position.x = sg*(0.7*len - 0.4);
        blade.castShadow = true;
        pivot.add(loom, blade);
        this.procedural.add(pivot);
        list.push({ pivot, blade, s, sg, th:0, dip:0.14, feather:0 });
      }
    }
    this.oars = list;
  }

  /* Swing the looms in time with the solver's stroke: blade forward and SQUARED
     at the catch, drawn aft through the water, then feathered and carried
     forward clear of it. Backing water runs the same stroke the other way.
     Left alone, the oars are held level, out of the water. */
  setOars(ph, dt){
    if(!this.oars || !ph || !ph.oar) return;
    const k = Math.min(1, dt*14);
    for(const o of this.oars){
      const inp = ph.oar.inp[o.s], u = ph.oar.ph[o.s];
      let th = 0, dip = 0.14, feather = 0;
      if(inp){
        const dir = inp > 0 ? 1 : -1;
        if(u < 0.45){
          const e = (1 - Math.cos(Math.PI*u/0.45))/2;
          th = dir*(-0.55 + 1.1*e); dip = -0.17; feather = Math.PI/2;
        }else{
          const e = (1 - Math.cos(Math.PI*(u - 0.45)/0.55))/2;
          th = dir*(0.55 - 1.1*e); dip = 0.08 + 0.07*Math.sin(Math.PI*(u - 0.45)/0.55);
        }
      }
      o.th += (th - o.th)*k; o.dip += (dip - o.dip)*k; o.feather += (feather - o.feather)*k;
      o.pivot.rotation.set(0, o.sg*o.th, o.sg*o.dip);
      o.blade.rotation.x = o.feather;
    }
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

  /* PAINTED CANVAS, one material per KIND of sail rather than one for the ship.
     A device belongs on the courses and topsails and has no business on a jib,
     which is a different sail cut to a different shape — so a spec paints them
     one at a time (`appearance.canvasMap.square`) and anything it does not name
     keeps the plain cloth. The square sails are done; the jib and the lateen
     will be the same line with a different word in it.

     One material per kind and not per SAIL: a galleon carries nine squares, and
     nine materials would be nine programs where one does. */
  _canvasMat(kind){
    const map = this.spec.appearance && this.spec.appearance.canvasMap;
    const src = kind && map && map[kind];
    if(!src) return this.mats.canvas;
    this._painted = this._painted || {};
    if(this._painted[kind]) return this._painted[kind];

    const base = this.mats.canvas;
    const m = new THREE.MeshStandardMaterial({
      map: Naval.sailTexture(src),
      /* The colour STAYS and multiplies the image, rather than going white.
         `appearance.canvas` is the tone of her cloth, and it is what keeps a
         painted sail the same weathered off-white as her unpainted ones — hand
         a device to a white material and it is a bedsheet next to her jib. */
      color: base.color.clone(),
      roughness: base.roughness, side: base.side,
      emissive: base.emissive.clone(), emissiveIntensity: base.emissiveIntensity
    });
    /* And it must be told about the sky by hand. applyHaze finds it on its own,
       the group being traversed — but applySailLight is called on named
       materials, so a new one is invisible to it and the sail would be the one
       thing aboard that does not light through. If the ship is already at sea
       the patch is applied here and now; the model arriving late is the normal
       case and not an edge one. */
    (this._sailMats = this._sailMats || []).push(m);
    if(this._skyU){
      Naval.applySailLight(m, this._skyU);
      if(this._aoU) Naval.applyShipAO(m, this._aoU);
      Naval.applyHaze(m, this._skyU);
    }
    this._painted[kind] = m;
    return m;
  }

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
       one down the sail, and six rows read the deep low belly as facets.

       SIXTEEN across for a square sail, though, and the swags are what forced
       it. A bight needs four columns or so to draw as a loop rather than as a
       notch, and a gasket that falls BETWEEN two columns is never sampled at
       its pinch — measured on the old grid, three swags on a ten-metre yard
       came out 0,58 m deep in the bights against 0,32 under the ties, a ratio
       of not quite two where the arithmetic asks for ten. It was the same
       aliasing that the sea's ripples and the stars have each run into: the
       feature was there and the sampling could not hold it.

       Nothing else on the ship wants the extra columns, so nothing else gets
       them. Measured: 0,051 ms a frame for her five sails at eight columns,
       0,097 at sixteen — a twentieth of a millisecond per square-rigged hull. */
    const nu = c.kind === 'square' ? 16 : 8, nv = 8;
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
    /* HOW SHE IS HANDED, decided here because here is where her head is known.

       A furled square sail is not a sausage. She is bunted up onto her yard and
       passed with gaskets at intervals, so between one gasket and the next the
       cloth hangs in a bight — a row of swags under the spar, pinched hard
       where each gasket is round her and full between them. That row of loops
       is what one actually recognises a handed square-rigger by, at any
       distance; a roll of even thickness reads as a rolled blind.

       The count is DERIVED and not chosen: a gasket about every three metres of
       yard, which is roughly a man's reach, so a long lower yard gets more of
       them than a topsail yard above it and nothing has to be set per ship.

       But it is SNAPPED to a divisor of the column count, and that is not
       tidiness. A gasket has to fall on a column of vertices or it is never
       drawn at its pinch — three swags across eight columns put both ties in
       the gaps between vertices and the pinch came out barely deeper than the
       bights. The spacing therefore goes to the nearest count the grid can
       actually hold, which is what "resolve the feature, do not merely compute
       it" has meant everywhere else in this file.

       Square sails only for now. A gaff sail comes down onto its boom and a jib
       runs down its stay, and neither is handed like this — they keep the plain
       roll until they get a rule of their own. */
    let nSwag = 0;
    if(c.kind === 'square'){
      const yard = c00.distanceTo(c10), want = yard/3.0;   // a gasket to a man's reach
      let bestErr = Infinity;
      for(let d = 2; d <= 4; d *= 2)              // divisors of 16, four columns apiece
        if(Math.abs(d - want) < bestErr){ bestErr = Math.abs(d - want); nSwag = d; }
    }

    const across = new THREE.Vector3().subVectors(c10, c00);
    const head = across.length();
    if(head > 1e-6) across.divideScalar(head);
    const up = new THREE.Vector3().subVectors(c00, c01);
    const drop = up.length();
    if(drop > 1e-6) up.divideScalar(drop);
    const kRoach = Math.log(0.5)/Math.log(0.58);   // deepest at v = 0.58

    const pos=[], w=[], sag=[], us=[], uvs=[], swag=[], idx=[];
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

        /* HER TEXTURE COORDINATES, which were being computed and thrown away —
           exactly the fault the ensign had, and found the same way: `u` was
           kept because furling needs it and `v` existed only as a loop
           variable, so the one thing a painted sail needs was the one thing
           nobody had written down.

           `1 - v` because the rows run from the head DOWN, while a texture's
           v runs up: without the flip a device comes out standing on its
           head, and it is the sort of thing one then blames on the image.

           And they are set on the parametric grid, NOT on the finished
           positions. That matters: the gore, the belly, the sag and the furl
           all move the cloth about — every frame, in the case of the last
           three — and none of them may drag the pattern across it. Canvas is
           painted before it is bent, so the paint travels with the weave. The
           whole unit square lands on the sail; the cut simply deforms it, the
           roach pulling the middle of the foot upward. */
        uvs.push(u, 1 - v);

        /* Where she is in her own bight, nil under a gasket and one in the
           middle of a swag — and worked out ONCE, here, because it depends on
           `u` alone and `u` never changes. Doing it in setSailShape would be a
           sine and a power per vertex per frame for a number that cannot
           possibly have moved. The exponent flattens the top of the loop: cloth
           gathered in a bight is round-bottomed and full across most of its
           span, and only draws in sharply against the gasket itself. */
        swag.push(nSwag ? Math.pow(Math.abs(Math.sin(Math.PI*u*nSwag)), 0.7) : 0);
      }
    }
    for(let j=0;j<nv;j++) for(let i=0;i<nu;i++){
      const k = j*(nu+1)+i;
      idx.push(k, k+nu+1, k+nu+2,  k, k+nu+2, k+1);
    }
    const g = new THREE.BufferGeometry();
    g.setAttribute('position', new THREE.Float32BufferAttribute(pos,3));
    g.setAttribute('uv', new THREE.Float32BufferAttribute(uvs,2));
    g.setIndex(idx);
    g.computeVertexNormals();
    const mesh = new THREE.Mesh(g, this._canvasMat(c.kind));
    /* nu1 is kept because furling needs it: a vertex rolls up onto the one
       directly above it in row nought, and finding that one means knowing the
       width of a row. */
    mesh.userData.sail = { base:Float32Array.from(pos), w:Float32Array.from(w),
                           sag:Float32Array.from(sag), u:Float32Array.from(us),
                           swag:Float32Array.from(swag), nSwag:nSwag,
                           nu1:nu+1, dir:dir.clone().normalize() };
    this.canvases.push(mesh);
    return mesh;
  }

  /* Fill the canvas, or empty it.

     The depth is the pressure the cloth is under, which the solver already
     works out in order to push her along — so she fills as she is trimmed and
     goes slack the moment the sheets are started, with no second rule to keep
     in step with the first. A luffing sail holds almost none of it and shivers
     instead, the shake running from luff to leech as it does on the water. */
  /* `set` is the fraction of canvas spread, from the solver. */
  setSailShape(load, luffing, t, set){
    const full = this.spec.rig.belly || 0.8;
    const press = Math.min(1, Math.max(0, load/35));   // Pa — a fresh breeze fills her
    const depth = luffing ? full*0.12 : full*press;

    /* Rolling up.

       Every vertex travels toward the one directly above it in ROW NOUGHT, and
       that single rule serves all three rigs, which is a happy accident of the
       order their corners were given in. Row nought is the HEAD of a square
       sail, so she gathers up to her yard as her buntlines are hauled; it is
       the FOOT of a gaff sail, so that one comes down onto its boom; and it is
       the tack-to-clew line of a jib, which runs down its stay. Each is what
       the rig actually does.

       Never quite to nothing, though. Collapsed exactly onto the row the sail
       has no area at all and simply vanishes, where a handed sail is a fat roll
       of cloth one can see from a mile off. Six per cent of her drop left over
       is that roll, and the belly still working on it keeps it from being a
       flat ribbon. */
    const sf = (set == null) ? 1 : Math.max(0, Math.min(1, set));
    const stow = 0.06 + 0.94*sf;
    /* And the roll is FATTEST when she is fully handed, which is the opposite
       of the belly. Without it the stowed remnant is a flat ribbon on the spar
       — geometrically a furled sail, visually a strip of tape. Gathered cloth
       is bulky: this is the bunt of it. */
    const bunt = full*0.45*(1 - sf);
    /* And it hangs in SWAGS, which is the thing one recognises a handed square
       sail by. The gaskets pinch her tight to the yard at intervals and the
       cloth bags between them, so the residual drop is not a constant along the
       yard: it swells to twice its average in the middle of each bight and
       draws in to a fifth of it under each gasket. Both ends of that range
       matter — the swelling alone gives a wavy ribbon, and it is the pinch that
       says "tied here".

       It comes on with the FURL and vanishes with it, so a sail fully set is
       untouched to the last decimal: at `furl` nought every factor below is
       exactly one and the arithmetic is the old arithmetic. */
    const furl = 1 - sf;
    for(const m of this.canvases){
      const s = m.userData.sail;
      if(!s) continue;
      const attr = m.geometry.attributes.position, arr = attr.array;
      const base = s.base, w = s.w, sg = s.sag, u = s.u, d = s.dir, nu1 = s.nu1 || 9;
      const sw = s.nSwag ? s.swag : null;        // null: she is not handed this way
      /* And then she hangs a little, on top of whatever she was cut. The cut
         is the larger of the two by some way — the foot of a course stands
         well above the line of her clews whatever the wind does — so this only
         eases the roach, it never turns it back into a smile. */
      const hang = full*(0.10 + 0.06*press);
      for(let k=0, n=w.length; k<n; k++){
        const i3 = k*3, r0 = (k % nu1)*3;          // her own place on row nought
        /* Her bight: 0,20 under a gasket, 2,05 at the belly of a swag, and
           exactly 1 when she is set, so nothing moves until she is handed. */
        const q = sw ? sw[k] : 1;
        const st = sw ? 0.06*(1 + furl*(0.20 + 1.85*q - 1)) + 0.94*sf : stow;
        /* Belly and hang go with the canvas that is out: half spread is half
           the cloth to fill, and a sail half handed does not bag. And the bunt
           swells with the bight — gathered cloth is thickest where there is
           most of it to gather, so the roll is fat between the gaskets and
           squeezed flat under each one. */
        const f = w[k]*((depth + (luffing ? full*0.22*Math.sin(u[k]*7 - t*9) : 0))*sf
                        + bunt*(sw ? 0.35 + 0.85*q : 1));
        const g = (sg ? sg[k]*hang*sf : 0);
        arr[i3  ] = base[r0  ] + (base[i3  ] - base[r0  ])*st + d.x*f;
        arr[i3+1] = base[r0+1] + (base[i3+1] - base[r0+1])*st + d.y*f - g;
        arr[i3+2] = base[r0+2] + (base[i3+2] - base[r0+2])*st + d.z*f;
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
    ], new THREE.Vector3(1,0,0), { kind:'gaff', uPeak:0.42, crown:0.80 }));
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
         { kind:'square',
           vPeak:0.30, vPin1:false, free:0.25, crown:0.55,
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
    ], new THREE.Vector3(1,0,0), { kind:'jib', uPeak:0.40, vPeak:0.34, vPin0:false, crown:0.80 }));
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

      this._reliefFromRoughness(obj, m.relief);

      this.group.remove(this.procedural);
      this.group.add(obj);
      this.modelRoot = obj;
      this.rigs = []; this.canvases = [];   // the procedural rig went with the hull
      this.falls = []; this._shareTot = 0;
      this.rigCuts.length = 0; this.rigEpoch++;   // and so did anything hanging off it
      this._rigModel();
      this._findGuns();
      this._hullShell();
      this._buildFlags();
      this._buildLantern();
      this._buildRudder();
      this._buildCrew();
      this._findNightGlow();
      return true;
    }catch(err){
      console.warn('[' + this.spec.id + '] could not load ' + (m.glb || 'embedded model') +
                   ' — keeping the procedural hull. ' + (err && err.message || err));
      return false;
    }
  }

  /* Give every material that carries a roughness map but no normal map the
     relief painted into that same greyscale. Opt-in per sheet (`model.relief`,
     a slope gain): a roughness map painted as flat zones rather than as a
     height would come out as ridges along every zone border. A material that
     already has a real normal map is left alone. */
  _reliefFromRoughness(root, strength){
    if(!(strength > 0)) return;
    const made = new Map();                 // one normal map per source texture
    root.traverse(o => {
      if(!o.isMesh) return;
      for(const mat of [].concat(o.material)){
        if(!mat || !mat.roughnessMap || mat.normalMap) continue;
        let n = made.get(mat.roughnessMap);
        if(n === undefined){
          n = Naval.normalFromHeight(mat.roughnessMap, strength, 1);  // glTF roughness is green
          made.set(mat.roughnessMap, n);
        }
        if(!n) continue;
        mat.normalMap = n;
        mat.normalScale.set(1, -1);        // no tangents: GLTFLoader's own sign
        mat.needsUpdate = true;
      }
    });
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

    /* CE QUI N'EST PAS DU BOIS N'EST PAS UN ESPAR, et cela s'est signalé à
       l'usage : une fenêtre ajoutée au château arrière — 4,2 m de large pour
       0,44 d'épaisseur, posée en travers et sur l'axe — passe toutes les
       épreuves de forme d'une vergue. Elle se faisait donc reparenter dans un
       mât, et partait brasser derrière la poupe à chaque changement d'écoute.
       La forme ne peut pas trancher ce cas ; la MATIÈRE, oui : une vergue est en
       bois, le vitrage et les fanaux n'en sont pas. Et une fiche peut nommer en
       clair ce qu'elle veut tenir hors du gréement (`model.rigIgnore`). */
    const ignore = (this.spec.model && this.spec.model.rigIgnore) || [];
    const bois = p => {
      if([].concat(p.mesh.material).some(m => m && Naval.GLOW_NAMES.test(m.name || ''))) return false;
      const n = (p.mesh.name || '').toLowerCase();
      return !ignore.some(s => n.includes(String(s).toLowerCase()));
    };
    const formeVergue = p => {
      const across = p.size.x, thick = Math.max(p.size.y, p.size.z);
      return across > 4*thick                    // long and thin, and thin the long way
          && across > 0.25*spec.B                // a spar, not a bit of deck gear
          && Math.abs(p.mid.x) < 0.15*across;    // squarely across the centreline
    };
    /* And the MASTS, by the same shape test stood on end: tall, thin BOTH
       ways, and on the centreline. Thin both ways is what does the work — it
       throws out anything welded to its neighbours, which is the usual state of
       an imported model and the reason a mast may or may not be able to fall.
       On the pirate, one spar of 1,1 x 38,7 x 1,1 m comes through clean while a
       second of 0,9 x 34,4 x 43,1 is two or three masts fused into one mesh and
       is rightly refused: nothing could drop one of those without the others. */
    const formeMat = p => {
      const tall = p.size.y, thick = Math.max(p.size.x, p.size.z);
      return tall > 4*thick
          && tall > 0.20*spec.L
          && Math.abs(p.mid.x) < 0.12*spec.B;
    };
    const yards = parts.filter(p => formeVergue(p) && bois(p));
    const poles = parts.filter(p => formeMat(p) && bois(p));
    const refuses = parts.filter(p => !bois(p) && (formeVergue(p) || formeMat(p)));
    if(refuses.length)
      console.warn('[' + spec.id + '] pièces de la forme d\'un espar tenues hors du gréement ' +
                   '(vitrage, fanal ou model.rigIgnore) : ' +
                   refuses.map(p => p.mesh.name || '?').join(', '));
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

      /* TWO nested groups, and the nesting is the whole trick.

         A mast goes over its HEEL, so the thing that turns must have its origin
         down at the step. Bracing, on the other hand, is a rotation about the
         VERTICAL axis — and a rotation about an axis is the same wherever the
         origin sits along it. So the outer group can be dropped to the heel for
         free, and setTrim goes on turning the inner one exactly as before,
         knowing nothing about any of this.

         The mast spar itself is carried by the outer group; the yards and the
         canvas hang in the inner one, as they already did. Everything that
         belongs to this mast therefore goes over the side together. */
      let pole = null, near = 0.06*spec.L;
      for(const p of poles){
        const d = Math.abs(p.mid.z - z0);
        if(!p.taken && d < near){ near = d; pole = p; }
      }
      if(pole) pole.taken = true;
      const heel = pole ? pole.box.min.y : deckAt(z0);

      const fall = new THREE.Group();
      fall.position.set(0, heel, z0);
      this.group.add(fall);
      // attach, not add: it keeps the spar exactly where the modeller put it
      if(pole) fall.attach(pole.mesh);

      const pivot = new THREE.Group();
      pivot.position.set(0, -heel, 0);
      fall.add(pivot);
      let share = 0;

      /* WHERE A CUT END CAN HANG FROM, recorded here because here is the only
         place that knows. The yards have just been read off the geometry and
         sorted onto their mast; asking a second time later would mean a second
         rule to keep in step with this one, which is the mistake the single
         set of hull lines exists to prevent.

         Nothing per ship, as everywhere else in the rig: a modeller who drops a
         square-rigger into ships/models gets her braces and her shrouds for
         free, and a hull with no mast mesh of its own has no anchors and simply
         trails nothing. */
      const cord = [];

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

        /* Both yardarms, and they carry the LONG ends. A brace runs from the arm
           right aft to the rail and a lift runs from it up to the cap, so either
           one shot away leaves several metres of rope swinging off the tip —
           which is the piece the eye actually catches, being furthest from the
           mast and moving most. Held in the PIVOT, so a cut brace swings round
           with the yard when she braces up, as it must. */
        const armLen = Math.max(2.5, Math.min(7, 0.22*(yy - heel)));
        cord.push({obj:pivot, x:-half*0.98, y:yy, z:dz, len:armLen},
                  {obj:pivot, x: half*0.98, y:yy, z:dz, len:armLen});

        const drop = Math.min(0.82*gap, 1.1*half,
                              yy - deckAt(y.mid.z) - 0.02*spec.L);
        if(drop < 0.2*half) continue;   // too near the deck to be a yard at all
        share += half*2*drop;           // roughly her area, for what she drives

        const V = (x,ay,z)=>new THREE.Vector3(x,ay,z);
        pivot.add(this._sailSurface([
          V(-half,      yy,      dz),
          V( half,      yy,      dz),
          V( half*0.86, yy-drop, dz),
          V(-half*0.86, yy-drop, dz)
        ], new THREE.Vector3(0,0,1),
           { kind:'square',
             vPeak:0.30, vPin1:false, free:0.25, crown:0.55,
             uPin0:false, uPin1:false, freeU:0.35, hangU0:true, hangU1:true,
             bow:0.05, roachFoot:0.11 }));
      }
      /* Only a mast that is its OWN mesh can be brought down. Without one,
         the canvas would fall off a spar still standing in the air, which is
         worse than nothing happening — so she keeps her rig and says so. */
      fall.userData.heel = heel;      // pour l'y ramener si on la répare
      fall.userData.mast = pole ? pole.mesh : null;
      fall.userData.share = share;
      fall.userData.height = pole ? pole.size.y : (mast[0].mid.y - heel);

      /* And the shrouds, which come off the HOUNDS rather than the truck — the
         standing rigging is seized round the masthead just under the top, not
         at the very tip, and an end hanging from the tip reads as a flag
         halyard instead. They live in the FALL and not in the pivot: shrouds do
         not brace round, they belong to the mast itself. */
      /* SHORT, and that was measured on the screen rather than reasoned from
         the rigging. A shroud runs from the hounds all the way to the channel,
         so a parted one really does hang some sixteen metres on a mast of this
         size — and sixteen metres of rope is dead straight, because a free end
         hangs straight whatever it is made of, and a straight line that long
         reads as WIRE. It looked like somebody had stayed her with fencing.

         So the ends are cut to what the eye can take for cordage. It is not the
         whole shroud, it is what is left of it after it has run out through
         everything it was rove through, which is honest enough and reads. */
      const mh = fall.userData.height;
      for(const sx of [-1, 1])
        cord.push({obj:fall, x:sx*Math.min(1.4, 0.05*mh), y:mh*0.78, z:0,
                   len:Math.max(4, Math.min(10, 0.26*mh))});
      fall.userData.cordage = cord;
      this._shareTot += share;
      this.rigs.push(pivot);
      this.falls.push(fall);
    }
  }

  /* ------------------------------------------------------------------------
     A MAST COMES DOWN, and it is a pendulum rather than an animation.

     A spar hinged at its step is a uniform rod on a pin, and that has an
     equation — a" = (3g/2L)·sin a — which is worth using instead of a curve
     drawn by hand for one reason: it carries the ship's SIZE. The rate goes as
     one over the root of the length, so a short stick whips over while a heavy
     one leans a long while first, with no number tuned for either. Measured on
     the pirate's mainmast, 38,7 m: 2 degrees, then 8, 14, 21, 30, 42, 59 and
     over at 3,5 s. A fifteen-metre mast does the same in 2,2. It is the same argument as
     Froude's in the spray, and the same reason a model boat never looks big.

     It also has the right shape in time all by itself: barely moving at first,
     then going with a rush. A mast does not topple, it hangs, leans, and then
     goes — and no eased curve reproduces that, because what makes it is that
     gravity's moment grows with the very angle it is producing.

     She stops at eighty degrees rather than falling flat: a real mast goes over
     the side and brings up hard in her own standing rigging, which is why a
     dismasted ship is dragged round by the wreckage instead of being rid of it.
     Ninety degrees, and she would look like a felled tree. */
  dropMast(i, side, delay){
    const f = this.falls[i];
    if(!f || f.userData.mast === null || f.userData.fall) return false;
    const L = Math.max(4, f.userData.height);
    f.userData.fall = {
      a: 0.03, stop: 1.40, drag: 0.5, sink: 0,
      rate: Math.sqrt(3*9.81/(2*L)),
      side: side || (Math.random() < 0.5 ? -1 : 1),
      w: 0, wait: delay || 0 };
    f.userData.fall.w = 0.30*f.userData.fall.rate;   // the blast does not nudge it
    /* Going over the side is where the whole of it lets go at once — which is
       also what makes the wreck read as being DRAGGED rather than dropped. */
    this.cutRigging(i, 4);
    return true;
  }

  /* ------------------------------------------------------------------ */
  /* LA TOILE EST LE FUSIBLE DU MÂT.

     Porter de la toile dans un coup de vent coûte d'abord de la toile : une
     couture lâche, la voile éclate hors de ses ralingues et s'en va. Et c'est
     une BONNE nouvelle pour le navire, parce qu'une voile qui part emporte avec
     elle la charge qu'elle mettait dans le mât. Un gréement se sauve en perdant
     son tissu, exactement comme un circuit se sauve en perdant son fusible.

     Le mât ne se perd donc que si l'on insiste au-delà de ce que la toile
     elle-même pouvait encaisser — au double de sa résistance, la ferrure part
     avec le tissu — et il se perd alors par le compteur que les boulets
     utilisent déjà : trois blessures et il passe par-dessus bord. Rien
     d'écrit deux fois, et les bouts rompus qui pendent après chaque coup
     disent qu'on est en train de l'user, comme ils le disaient au canon. */
  _canvasOf(f){
    if(f.userData.cloth) return f.userData.cloth;
    const out = [];
    f.traverse(o => { if(this.canvases.indexOf(o) >= 0) out.push(o); });
    f.userData.cloth = out;
    return out;
  }

  /* Fait éclater UNE voile, tirée au sort parmi celles qui tiennent encore —
     pondérée par le tissu, donc un grand mât en perd plus souvent qu'un
     artimon, sans qu'aucune probabilité ait été écrite par navire. */
  splitSail(hard){
    const vivantes = [];
    for(let i=0;i<this.falls.length;i++){
      const f = this.falls[i];
      if(f.userData.fall) continue;                   // celui-là s'en va déjà
      for(const m of this._canvasOf(f))
        if(m.userData.split !== true) vivantes.push([i, m]);
    }
    if(!vivantes.length) return -1;
    const [i, mesh] = vivantes[Math.floor(Math.random()*vivantes.length)];
    mesh.userData.split = true;
    mesh.visible = false;
    this.cutRigging(i, 2);
    if(hard){
      const f = this.falls[i];
      f.userData.wounds = (f.userData.wounds || 0) + 1;
      if(f.userData.wounds >= 3){ this.dropMast(i); return -2 - i; }
    }
    return i;
  }

  /* LE MÂT QUI PORTE LE PLUS, et c'est lui qui casse. Un espar ne se perd pas
     parce qu'on a compté trois voiles éclatées — ce compteur-là est celui des
     boulets, et un mât ne porte que deux ou trois voiles, donc il ne pouvait
     pas l'atteindre. Il se perd parce que ce qui est encore envergué dessus
     tire plus fort que lui, ce qui se lit directement sur la toile qui lui
     reste : celui dont le fusible a sauté ne risque plus rien. */
  heaviestMast(){
    let best = -1, bestShare = 0;
    for(let i=0;i<this.falls.length;i++){
      const f = this.falls[i];
      if(f.userData.fall || f.userData.mast === null) continue;
      const c = this._canvasOf(f);
      if(!c.length) continue;
      let vives = 0;
      for(const m of c) if(m.userData.split !== true) vives++;
      const part = f.userData.share * vives / c.length;
      if(part > bestShare){ bestShare = part; best = i; }
    }
    return { i:best, part: this._shareTot > 0 ? bestShare/this._shareTot : 0 };
  }

  /* La part de toile encore ENTIÈRE, pondérée par la surface comme standing()
     l'est par les mâts. Un seul nombre, deux usagers : le solveur le multiplie
     dans la pression, le modèle cache le tissu correspondant, et l'image ne
     peut pas diverger de ce qui pousse. */
  whole(){
    if(!(this._shareTot > 0)) return 1;
    let s = 0;
    for(const f of this.falls){
      const c = this._canvasOf(f);
      if(!c.length){ s += f.userData.share; continue; }
      let vives = 0;
      for(const m of c) if(m.userData.split !== true) vives++;
      s += f.userData.share * vives / c.length;
    }
    return s/this._shareTot;
  }

  /* Ends shot away, asked for and not yet hung. A count rather than a list of
     which ropes: they are picked at random from the anchors this mast has, so
     no two hits on the same mast look alike and none of it is data. */
  cutRigging(i, n){
    const f = this.falls[i];
    if(!f || !f.userData.cordage || !f.userData.cordage.length) return;
    this.rigCuts.push({i:i, n:Math.max(1, n|0)});
  }

  /* The magazine takes them all — but not together. They go a few tenths of a
     second apart and to alternate sides, for exactly the reason the three
     charges do: at the same instant it reads as one object breaking, and
     staggered it reads as a ship coming to pieces. */
  dropAllMasts(){
    let n = 0, side = Math.random() < 0.5 ? -1 : 1;
    for(let i=0;i<this.falls.length;i++)
      if(this.dropMast(i, side*(i%2 ? -1 : 1), n*0.45)) n++;
    return n;
  }

  /* A BALL IN HER SIDE IS A BALL IN HER GUNDECK. Whatever comes through the
     planking abreast of a gun smashes its carriage, splits its breeching or
     kills its crew, and the piece is out of the fight — which is how a ship
     came to have a whole side silenced while the other was still firing.

     Each piece takes damage by its distance from the hit, inside a bay of
     eight hundredths of her length (a gundeck's pieces stand about that far
     apart), and by the calibre of the ball that did it. It is out at one: a
     ball of her own size squarely on the port does 0,8, so it takes two in the
     same place, or one heavier gun; a near miss a bay away does nothing. The
     damage ACCUMULATES, so a side that is hulled again and again loses its
     guns one by one rather than all at once or never.

     `world` is where the ball struck. Returns the pieces just put out. */
  woundGuns(world, k){
    const out = [];
    if(!world || !this.guns || !this.guns.length) return out;
    this._gq = this._gq || new THREE.Quaternion();
    this._gl = this._gl || new THREE.Vector3();
    const loc = this._gl.copy(world).sub(this.group.position)
                        .applyQuaternion(this._gq.copy(this.group.quaternion).invert());
    const R = Math.max(1.5, 0.08*this.spec.L);
    const blow = 1.6*Math.min(2, k || 1);
    for(const g of this.guns){
      if(g.out) continue;
      const d = loc.distanceTo(g.p);
      if(d >= R) continue;
      g.damage = (g.damage || 0) + (1 - d/R)*blow;
      if(g.damage >= 1){ g.out = true; out.push(g); }
    }
    return out;
  }

  // how many pieces of a group are still fit to fire, and how many she has
  gunCount(side){
    let ok = 0, all = 0;
    for(const g of this.guns) if(g.side === side){ all++; if(!g.out) ok++; }
    return { ok, all };
  }

  restoreMasts(){
    /* A refit re-reeves her rigging, so every end already hanging is void. The
       epoch says so once instead of every caller having to remember it. */
    // and remounts her guns: the same refit, and the same three callers
    for(const g of this.guns || []){ g.out = false; g.damage = 0; g.readyAt = 0; }   // a refit sends her out loaded
    // and new planking: a refit leaves no scars
    this.scars.length = 0; this._scarU.uScarCount.value = 0;
    this.rigCuts.length = 0;
    this.rigEpoch++;
    for(const c of this.canvases) c.userData.split = false;   // et la toile est renvergée
    for(const f of this.falls){
      f.userData.wounds = 0;
      f.userData.fall = null;
      f.rotation.z = 0;
      f.position.x = 0;                  // elle revient où elle était plantée
      f.position.y = f.userData.heel;
      f.visible = true;
    }
  }

  stepRigging(dt){
    for(const f of this.falls){
      const s = f.userData.fall;
      if(!s) continue;

      if(s.a < s.stop){
        if(s.wait > 0){ s.wait -= dt; continue; }
        s.w += s.rate*s.rate*Math.sin(s.a)*dt;

        /* ET LES HAUBANS LE RETIENNENT sur la fin. Le pendule donne un très bon
           départ — immobile, puis d'un coup — mais il arrivait à sa butée à
           pleine vitesse et s'y arrêtait NET, ce qui est le seul endroit du
           mouvement qui trahissait une valeur écrêtée plutôt qu'une chose qui
           s'arrête. Un mât ne rencontre pas un mur : ses rides prennent la
           charge sur le dernier quart et le freinent.

           L'amortissement va donc comme le CARRÉ de ce qu'il a parcouru dans ce
           dernier quart — nul quand il y entre, entier quand il y arrive — et
           il est écrit par seconde et non par image, sans quoi le freinage
           dépendrait de la fréquence d'affichage comme tant d'autres choses
           dans ce projet. */
        const pris = Math.max(0, (s.a - 0.72*s.stop)/(0.28*s.stop));
        if(pris > 0) s.w -= s.w*pris*pris*7.0*dt;

        s.a = Math.min(s.stop, s.a + s.w*dt);
        f.rotation.z = s.side*s.a;
        continue;
      }

      /* ELLE S'EN DÉBARRASSE. Arrêté à quatre-vingts degrés, le mât restait là
         pour toujours, couché en travers de son bord et la suivant partout —
         il avait l'air ACCROCHÉ à elle, ce qui était signalé et juste.

         Ce qui se passe réellement est en deux temps, et le premier était déjà
         là sans le second. Un mât abattu tient d'abord dans ses propres
         haubans et **traîne** : c'est ce qui rend un démâtage si dangereux, le
         navire étant tiré par son épave au lieu d'en être quitte. Puis
         l'équipage prend les haches, coupe les rides, et le tout part par le
         travers et coule — le bois gorgé d'eau avec sa mâture et sa toile ne
         flotte pas longtemps.

         Il s'enfonce dans SON repère à elle plutôt que dans le monde, ce qui
         évite tout à ce morceau d'épave : rien à recentrer quand l'origine
         glisse, rien à sortir du graphe, rien à détruire. Et comme la mer est
         opaque, il disparaît de lui-même en passant dessous — la même gratuité
         que les planches de l'explosion. */
      if(s.drag > 0){ s.drag -= dt; continue; }
      s.sink += dt;

      /* ELLE ROULE PAR-DESSUS LA LISSE, et vite.

         Le premier réglage la faisait traîner six secondes puis s'enfoncer tout
         droit sur huit — dix-huit secondes en tout. Or le navire qu'on fait
         sauter passe sous l'eau au bout de SIX : le mât descendait donc avec
         lui au lieu de s'en aller, ce qui est exactement ce qu'on voulait
         éviter. Le budget n'était pas le bon, et il se mesure sur elle et non
         sur le mât.

         Et il ne suffit pas de raccourcir : descendre tout droit à côté d'une
         coque qui descend aussi ne se lit pas comme un départ. Il faut qu'il
         **roule** — qu'il passe la lisse, tourne au-delà de l'angle où ses
         haubans le tenaient, et parte par le travers. Trois mètres de côté
         suffisent, en quadratique pour qu'il s'écarte d'abord doucement puis
         franchement, comme une chose qui bascule. */
      /* ET RIEN DE TOUT CELA N'EST LINÉAIRE.

         Le roulé était une rampe droite et la descente une vitesse constante :
         deux mouvements qui commencent et finissent à pleine allure, ce que ne
         fait aucune chose qui bascule. Le roulé prend donc un `smoothstep` —
         doux aux deux bouts — parce qu'il part d'un objet en équilibre sur sa
         lisse et finit couché : les deux extrémités sont des états de repos.

         La descente, elle, ne prend QUE l'entrée en douceur. Elle n'a pas de
         fin : le mât ne se pose pas au fond, il s'en va, et l'eau ne le freine
         pas dans le peu qu'on en voit. Lui mettre une sortie douce serait le
         faire ralentir en s'enfonçant, ce qui est joli et faux. Elle va donc
         comme le carré du temps, ce qui est aussi ce que fait un corps qui
         coule. */
      const u = Math.min(1, s.sink/2.4);
      const e = u*u*(3 - 2*u);                       // doux au départ comme à l'arrivée
      f.rotation.z = s.side*(s.stop + 1.10*e);       // il roule au-delà de la lisse
      f.position.x = -s.side*3.6*e;                  // et s'en va par le travers
      f.position.y = f.userData.heel - (2.6*e + 4.2*s.sink*s.sink);
      if(s.sink > 2.6) f.visible = false;
    }
  }

  /* What fraction of her canvas is still ALOFT, weighted by the area each mast
     carries rather than by counting sticks — a mizzen is not a mainmast.

     It falls off with the cosine of the lean instead of switching off, which is
     both continuous and true: a mast forty degrees over still holds her canvas
     to the wind at some angle. But the cosine is REMAPPED to reach nought where
     she brings up, not at ninety — a raw cosine left her eight per cent of her
     drive with the sails already in the water, being pulled through it. This is
     the single number the solver multiplies into her sail force, so what you
     see and what drives her cannot come apart. */
  standing(){
    if(!(this._shareTot > 0)) return 1;
    let s = 0;
    for(const f of this.falls){
      const st = f.userData.fall;
      const k = st ? (Math.cos(st.a) - Math.cos(st.stop))/(1 - Math.cos(st.stop)) : 1;
      s += f.userData.share * Math.max(0, k);
    }
    return s/this._shareTot;
  }

  /* THE SIDE A SHOT HAS TO GET THROUGH, and it is the MODEL's side, not the
     solver's.

     Gunnery is the first thing in this project that made the two disagree out
     loud. The probe grid is built from hull-lines.js — length, beam, freeboard,
     draught, all from her paper — while the .glb is a different object scaled
     only so that its LENGTH matches. On the pirate the solver puts her deck at
     y = 2,8 and the model puts her gunports at y = 6,0: three metres apart. Aim
     at what you can see and the ball sails over a hull the physics thinks is
     lower than the one on the screen. Measured, the first shot passed six
     metres above her.

     One cannot simply prefer the solver — the player aims at the planking he is
     looking at, and a ball that goes through the picture of her side must count.
     So the SHAPE is taken from the model, and what is handed to the flooding is
     a FRACTION of her depth rather than a height in metres. A fraction means
     the same thing in both frames — nought at the keel, one at the deck — which
     is exactly what breach() asks for, so the hole ends up where the eye saw it
     go in without either side having to move.

     Cut into the same compartments the flooding uses, so a hit knows which room
     it has opened without a second division of her length. */
  _hullShell(){
    this.shell = null;
    if(!this.modelRoot) return;
    const parts = this._modelParts();
    let hull = parts[0], best = -1;
    for(const p of parts){
      const v = p.size.x*p.size.y*p.size.z;
      if(v > best){ best = v; hull = p; }
    }
    const N = Naval.Config.NCOMP, L = this.spec.L, half = L*0.5;
    const box = [];
    for(let i=0;i<N;i++)
      box.push({ z0:-half + i*L/N, z1:-half + (i+1)*L/N,
                 half:0, deck:-Infinity, keel:Infinity });
    const pos = hull.geom.attributes.position;
    for(let i=0;i<pos.count;i++){
      const z = pos.getZ(i);
      const b = box[Math.min(N-1, Math.max(0, Math.floor((z + half)/L*N)))];
      const x = Math.abs(pos.getX(i)), y = pos.getY(i);
      if(x > b.half) b.half = x;
      if(y > b.deck) b.deck = y;
      if(y < b.keel) b.keel = y;
    }
    for(const p of parts) p.geom.dispose();
    for(const b of box) if(!(b.half > 0) || b.deck <= b.keel) return;   // unusable
    this.shell = box;
  }

  /* WHERE HER MUZZLES ARE, read off the model rather than written in her paper.

     This one is found by MATERIAL NAME, which is a departure from the yards and
     the masts and wants defending. Those two have a shape a rule can state — a
     spar is long and thin, a mast is that stood on end — and a gun barrel has
     no such luck: it is a short thick cylinder, which describes half the deck
     furniture on a ship. Worse, a battery is almost always one buffer holding
     every gun on board, so there is not even an object per gun to test.

     What there IS, is a modeller who has already told us: the pirate's barrels
     carry a material called "black_canon". So the contract is one word in a
     material name, which is a far smaller thing to ask than a mesh per gun, and
     a ship that says nothing simply has no battery and cannot fire.

     The muzzles themselves are then pure geometry, and the order of the two
     steps is the whole of it. GROUP FIRST, then look outboard: a barrel lies
     across the ship, so it occupies only its own diameter in z while the guns
     stand metres apart — one gap test separates them, the same one that sorts
     yards onto masts — and the muzzle is simply the outboard-most vertex of
     its own group.

     Done the other way about it misses half the battery, which is what the
     first writing did. Taking the vertices near the widest point of the WHOLE
     battery assumes her side is a flat plane; it is not, it curves in towards
     bow and stern, so the after guns sit inboard of the midship ones and fell
     outside the window. Six guns found where there are twelve, all of them
     forward — and the ship's own shape was the reason. */
  /* THE BATTERY, and not only the broadside.

     A piece is read off the model by the WAY ITS BARREL LIES, which is the one
     thing a gun cannot hide. The broadside guns lie athwartships; a stern chaser
     run out through the transom, or a bow chaser through the head, lies fore
     and aft. So `side` is no longer a sign but a GROUP:

         +1 tribord    −1 bâbord    +2 poupe    −2 proue

     chosen so that "the other one" is still the negative — ⇧G turns starboard
     into larboard and the stern chasers into the bow chasers, one rule for both.

     This used to sort vertices to one side of the centreline and group them by
     their gap along the hull, which knew nothing of direction: the two stern
     pieces added to the Roter Löwe, at ±1 m either side of her sternpost, were
     filed one into each broadside and would have fired out of her quarters.
     They are now clustered in three dimensions first, and each cluster is asked
     which way it is long. Measured on her model: twelve pieces 0,42 long across
     and 0,22 along, two at the transom 0,17 across and 0,32 along.

     And every mesh carrying a gun material is read, not the first found: a
     modeller adding chasers is as likely to make them a separate object as to
     weld them into the battery.

     A piece carries its CALIBRE, relative to the broadside's: a chaser is a
     smaller gun, so its ball, flash, smoke and hole all come down with it
     through the one `k` that guns.js already scales them by. */
  _findGuns(){
    this.guns = [];
    if(!this.modelRoot) return;             // a procedural hull carries no battery

    this.group.updateWorldMatrix(true, true);
    const toLocal = new THREE.Matrix4().copy(this.group.matrixWorld).invert();
    const m4 = new THREE.Matrix4(), v = new THREE.Vector3();
    /* Touching cells join, so two vertices up to about twice this apart are one
       piece. Wide enough that a barrel modelled as two bare rings is not cut in
       two at its middle; narrow against the two or three metres between pieces. */
    const cell = 0.45, grid = new Map();
    this.modelRoot.traverse(o => {
      if(!o.isMesh || !o.geometry) return;
      const mats = Array.isArray(o.material) ? o.material : [o.material];
      if(!mats.some(m => m && /canon|cannon|gun/i.test(m.name || ''))) return;
      o.updateWorldMatrix(true, false);
      m4.multiplyMatrices(toLocal, o.matrixWorld);
      const pos = o.geometry.attributes.position;
      for(let i=0;i<pos.count;i++){
        v.fromBufferAttribute(pos, i).applyMatrix4(m4);
        const key = Math.floor(v.x/cell) + ',' + Math.floor(v.y/cell) + ',' + Math.floor(v.z/cell);
        let c = grid.get(key); if(!c){ c = []; grid.set(key, c); }
        c.push(v.x, v.y, v.z);
      }
    });
    if(!grid.size) return;

    // flood the occupied cells: touching cells are one piece, a gap is the next
    const seen = new Set(), pieces = [];
    for(const start of grid.keys()){
      if(seen.has(start)) continue;
      seen.add(start);
      const stack = [start], pts = [];
      while(stack.length){
        const k = stack.pop(), c = grid.get(k);
        for(let i=0;i<c.length;i++) pts.push(c[i]);
        const [x, y, z] = k.split(',').map(Number);
        for(let dx=-1;dx<=1;dx++) for(let dy=-1;dy<=1;dy++) for(let dz=-1;dz<=1;dz++){
          const n = (x+dx) + ',' + (y+dy) + ',' + (z+dz);
          if(grid.has(n) && !seen.has(n)){ seen.add(n); stack.push(n); }
        }
      }
      const lo = [Infinity, Infinity, Infinity], hi = [-Infinity, -Infinity, -Infinity];
      for(let i=0;i<pts.length;i+=3) for(let a=0;a<3;a++){
        if(pts[i+a] < lo[a]) lo[a] = pts[i+a];
        if(pts[i+a] > hi[a]) hi[a] = pts[i+a];
      }
      pieces.push({ pts, lo, hi,
        ex:hi[0]-lo[0], ey:hi[1]-lo[1], ez:hi[2]-lo[2],
        cx:(lo[0]+hi[0])/2, cy:(lo[1]+hi[1])/2, cz:(lo[2]+hi[2])/2 });
    }

    // the broadside's bore sets the scale a chaser is measured against
    const bores = pieces.filter(p => p.ex >= p.ez).map(p => Math.max(p.ey, p.ez)).sort((a,b) => a-b);
    const ref = bores.length ? bores[bores.length >> 1] : 0;

    for(const p of pieces){
      const across = p.ex >= p.ez;
      const bore = across ? Math.max(p.ey, p.ez) : Math.max(p.ey, p.ex);
      const cal = ref > 0 ? Math.max(0.5, Math.min(1.5, bore/ref)) : 1;
      let side, dir, muzzle;
      if(across){
        side = p.cx < 0 ? 1 : -1;                     // starboard is −x
        // the muzzle is where it reaches furthest outboard, on the barrel's axis
        muzzle = new THREE.Vector3(side > 0 ? p.lo[0] : p.hi[0], p.cy, p.cz);
        dir = new THREE.Vector3(-side, 0, 0);
      }else{
        const fore = p.cz > 0;
        side = fore ? -2 : 2;
        muzzle = new THREE.Vector3(p.cx, p.cy, fore ? p.hi[2] : p.lo[2]);
        dir = new THREE.Vector3(0, 0, fore ? 1 : -1);
      }
      this.guns.push({ side, p:muzzle, dir, cal, chase: !across && cal < 0.9 });
    }
    this.guns.sort((a,b) => b.p.z - a.p.z);      // forward gun first, as they fire
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
    /* Kept, because a painted sail may be built LATER than this runs — a model
       arrives from the network long after her hull is at sea — and it would
       then have no way of learning that the sky exists. */
    this._skyU = oceanUniforms;
    this._aoU = aoUniforms;
    // Before the haze, which chains onto it and must dim it in its turn.
    Naval.applySailLight(this.mats.canvas, oceanUniforms);
    for(const m of (this._sailMats || [])) Naval.applySailLight(m, oceanUniforms);
    Naval.applySailLight(this.mats.flag, oceanUniforms);
    const patch = obj => {
      if(!obj.material) return;
      const mats = Array.isArray(obj.material) ? obj.material : [obj.material];
      for(const m of mats){
        // the men get neither her occlusion (they are not in its pass) nor her scars
        const man = !!m.userData.crew;
        if(aoUniforms && !man) Naval.applyShipAO(m, aoUniforms);
        // scars on her timber, never on her canvas — and before the haze, which is the air in front
        if(!m.userData.sailLit && !man && obj !== this.wake) Naval.applyScars(m, this._scarU);
        if(obj !== this.wake) Naval.applySnowCover(m, this._snowU);
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
  _buildFlags(){
    /* De son parent, quel qu'il soit : il pend désormais DANS son mât, donc
       this.group n'est plus forcément celui qui le tient. */
    for(const f of this.flags || []){
      if(f.mount.parent) f.mount.parent.remove(f.mount);
      f.mesh.geometry.dispose();
      if(f.staff){
        if(f.staff.parent) f.staff.parent.remove(f.staff);
        f.staff.geometry.dispose();
      }
    }
    this.flags = [];
    this.flag = null;
    const spec = this.spec;

    /* PLUSIEURS COULEURS, SI LA FICHE LES DÉCLARE — un galion porte son grand
       pavillon sur une hampe au couronnement, une flamme en tête d'artimon et
       une autre au bout du beaupré. Rien de déclaré rend l'ancien usage : un
       seul, en tête du plus grand mât.

       Ceux qui ne nomment pas d'image partagent LA matière du navire, puisqu'il
       n'a qu'une nationalité : hisser le pavillon noir les change tous. Une
       flamme qui porte ses propres armes garde les siennes. */
    const list = spec.flags;
    if(!list){
      const head = this._mastHead(null);
      if(head) this.flags.push(this._flagAt(head.parent, 0, head.topY, head.z, {}));
      this.flag = this.flags[0] || null;
      return;
    }
    let st = null;
    for(const l of list){
      if(l.at === 'stern' || l.at === 'bow'){
        st = st || this._deckStations();
        this.flags.push(this._staffFlag(l, st));
      }else if(l.mast != null){
        const head = this._mastHead(l.mast);
        if(head) this.flags.push(this._flagAt(head.parent, 0, head.topY + (l.above || 0), head.z, l));
      }
    }
    this.flag = this.flags[0] || null;
  }

  /* The head of a mast, in the frame of whatever carries it. `index` counts
     from the bow — 0 the foremast — and a negative one from the stern, so -1
     is always the mizzen whatever she carries forward of it; null asks for the
     tallest, which is where a lone ensign has always flown. */
  _mastHead(index){
    const spec = this.spec;
    if(index != null){
      const nth = (arr, zOf) => {
        const sorted = arr.slice().sort((a, b) => zOf(b) - zOf(a));   // bow first, +z being the stem
        return sorted[index < 0 ? sorted.length + index : index] || null;
      };
      if(this.modelRoot){
        const top = nth(this._sparScan().tops, x => x.z);
        if(!top) return null;
        /* Hung in the fall of that mast when the rig found one there, so it
           goes over the side with it; otherwise from the hull, a mast the rig
           could not take being a mast that cannot come down either. */
        let fall = null, near = 0.06*spec.L;
        for(const fl of this.falls){
          const d = Math.abs(fl.position.z - top.z);
          if(d < near){ near = d; fall = fl; }
        }
        return fall
          ? { parent:fall, topY:top.y - fall.position.y, z:top.z - fall.position.z }
          : { parent:this.group, topY:top.y, z:top.z };
      }
      const m = nth(spec.masts, x => x.z);
      return m ? { parent:this.group, topY:spec.deckMid + m.height, z:m.z } : null;
    }
    let topY, z, parent = this.group;

    if(this.modelRoot){
      /* ON DEMANDE AUX MÂTS DÉJÀ TROUVÉS, plutôt que de les chercher une
         seconde fois — et c'est une correction d'ORDRE autant que de style.

         `_rigModel` tourne avant celui-ci et REPARENTE le fût dans son groupe
         de chute, pour que tout ce qui appartient au mât passe par-dessus bord
         ensemble. `_modelParts` ne le voit donc plus, et un navire dont le
         seul fût propre a été pris par le gréement n'avait pas de drisse :
         relevé sur le galion pirate, trois mâts trouvés dont un avec son fût de
         19,3 m, et ZÉRO candidat pour le pavillon. Ses autres pièces sont des
         mâts fondus avec leurs vergues — 0,5 × 17,2 × 21,6 m — que la règle de
         forme refuse à juste titre, et c'est bien elle qui a raison.

         Signalé à l'usage : « le galion pirate n'a plus son drapeau ». Il ne
         l'avait jamais eu, en réalité, depuis que le gréement lui prend son
         mât.

         Le chercheur de mâts a déjà répondu à la question, et il est le seul à
         pouvoir y répondre puisque c'est lui qui a déplacé la pièce. Poser la
         question deux fois, c'était se donner deux réponses à tenir en accord —
         la faute que l'invariant du plan de formes unique existe pour empêcher.
         La règle de forme reste, mais en repli : un navire sans mât gréé n'a
         pas de drisse, ce qui est le comportement voulu. */
      let mat = null;
      for(const f of this.falls){
        if(!f.userData.mast) continue;
        if(!mat || f.userData.height > mat.userData.height) mat = f;
      }
      if(mat){
        /* Dans le repère de la CHUTE, dont l'origine est au pied : la pomme est
           donc simplement à sa hauteur, et sur son axe. C'est aussi ce qui rend
           inutile de le recentrer, de le faire tomber ou de le détruire — il
           est porté par ce qui tombe. */
        parent = mat;
        topY = mat.userData.height;
        z = 0;
      }else{
        const parts = this._modelParts();
        let best = null;
        for(const p of parts){
          const thick = Math.max(p.size.x, p.size.z);
          if(p.size.y < 3*thick || p.size.y < 0.15*spec.L) continue;   // not a mast
          if(Math.abs(p.mid.x) > 0.08*spec.B) continue;                // off the centreline
          if(!best || p.box.max.y > best.box.max.y) best = p;
        }
        for(const p of parts) p.geom.dispose();
        if(!best) return null;
        topY = best.box.max.y; z = best.mid.z;
      }
    }else{
      let m = null;
      for(const k of spec.masts) if(!m || k.height > m.height) m = k;
      if(!m) return null;                  // a vessel under power alone flies none
      topY = spec.deckMid + m.height; z = m.z;
    }

    return { parent, topY, z };
  }

  /* A flag on a staff of its own — the ensign at the taffrail, the jack at the
     end of the bowsprit. The staff is drawn, and the flag's hoist lies ALONG it:
     hung upright at the truck of a raked staff, it stood off in the air beside
     its own pole — signalled from a capture. Neither belongs to a mast, so a
     dismasting leaves them flying. */
  _staffFlag(l, st){
    const spec = this.spec, sc = spec.L/24, stern = l.at === 'stern';
    const hoist = 0.045*spec.L*(l.size != null ? l.size : 1);
    let z = l.z != null ? l.z : l.zFrac != null ? l.zFrac*spec.L : (stern ? st.zAft : st.zFore);
    let y = l.y != null ? l.y : st.deckNear(z);
    const x = l.x != null ? l.x : (l.xFrac || 0)*spec.B;
    /* The jack is stepped on the bowsprit, not on the deck: out along the spar
       by most of its length and up by its steeve, unless the sheet says where. */
    const bs = spec.rig && spec.rig.bowsprit;
    const sprit = !stern && this.modelRoot ? this._sparScan().sprit : null;
    if(sprit && l.z == null && l.zFrac == null){
      // on a model, the end of the spar as drawn
      z = sprit.z;
      if(l.y == null) y = sprit.y;
    }else if(!stern && bs && l.z == null && l.zFrac == null){
      const out = bs.length*0.85;
      z = st.zFore + out*Math.cos(bs.steeve);
      if(l.y == null) y = st.deckNear(st.zFore) + 0.55*sc + out*Math.sin(bs.steeve);
    }
    y += (l.above || 0);
    const h = l.staff != null ? l.staff : Math.max((stern ? 0.12 : 0.08)*spec.L, hoist*1.5);
    const rake = l.rake != null ? l.rake : (stern ? 0.3 : 0.1);
    const lean = stern ? -1 : 1;             // the head leans outboard
    const g = new THREE.CylinderGeometry(0.035*sc, 0.06*sc, h, 6);
    g.translate(0, h/2, 0);
    const staff = new THREE.Mesh(g, this.mats.spar);
    staff.position.set(x, y, z);
    staff.rotation.x = lean*rake;            // a +x turn carries +y toward +z
    this.group.add(staff);
    const f = this._flagAt(this.group, x, y + h*Math.cos(rake), z + lean*h*Math.sin(rake), l,
                           -lean*rake);
    f.staff = staff;
    return f;
  }

  /* One flag, its hoist at (x, topY, z) in `parent`, cut to its shape.

     It hangs in a MOUNT tilted like whatever it is bent to — `tilt` radians,
     positive with the head leaning aft — and swings to the wind about that
     axis, as bunting does about its staff. The sheet's `tilt` overrides the
     staff's own rake, and gives a masthead flag one too.

     The shape is laid on the same grid the ripple works on, so nothing about
     the waving has to know it: `u` runs out along the fly, `v` down the hoist.
     A TAPER narrows the depth toward the fly about the middle of the hoist; a
     NOTCH pulls the fly edge back into a V, which forks the tail. The texture
     coordinates follow the cut, so a device painted on a rectangle is trimmed
     by the notch rather than squeezed into it. */
  _flagAt(parent, x, topY, z, l, tilt){
    const spec = this.spec;
    const shapeName = Naval.FLAG_SHAPES[l.shape] ? l.shape : 'rect';
    const shape = Naval.FLAG_SHAPES[shapeName];
    /* "nation" looks the image up under the name of the cut; "nation:poupe"
       under a key of the sheet's choosing — so the ensign at the taffrail can
       fly a nation's arms while her mastheads fly its plain colours. */
    const byNation = typeof l.image === 'string' && /^nation(:|$)/.test(l.image);
    const nationKey = byNation ? (l.image.slice(7) || shapeName) : null;
    const len = l.length != null ? l.length : shape.length;
    const hoist = 0.045*spec.L*(l.size != null ? l.size : 1), fly = hoist*len;
    const nu = Math.min(48, Math.round(14*Math.max(1, len/1.6))), nv = 6;
    const pos = [], us = [], vs = [], uvs = [], idx = [];
    for(let j=0;j<=nv;j++) for(let i=0;i<=nu;i++){
      const v = j/nv;
      const u = Math.min(i/nu, 1 - shape.notch*(1 - Math.abs(2*v - 1)));
      const w = u <= shape.taper ? 1
              : 1 - (1 - shape.tip)*(u - shape.taper)/(1 - shape.taper);
      pos.push(0, -hoist*(0.5 + (v - 0.5)*w), u*fly);   // hoist at u=0, streaming down +z
      us.push(u); vs.push(v);
      // the image is CUT by the taper, not squeezed into it: what is painted on a
      // template at a given height is what flies there. v flipped: built downward.
      uvs.push(u, 1 - (0.5 + (v - 0.5)*w));
    }
    for(let j=0;j<nv;j++) for(let i=0;i<nu;i++){
      const k = j*(nu+1)+i;
      idx.push(k, k+nu+1, k+nu+2,  k, k+nu+2, k+1);
    }
    const g = new THREE.BufferGeometry();
    g.setAttribute('position', new THREE.Float32BufferAttribute(pos, 3));
    g.setAttribute('uv', new THREE.Float32BufferAttribute(uvs, 2));
    g.setIndex(idx);
    g.computeVertexNormals();

    const mount = new THREE.Group();
    mount.position.set(x, topY, z);
    const lean = l.tilt != null ? l.tilt : (tilt || 0);
    mount.rotation.x = -lean;                // a +x turn carries the head toward +z, the bow
    parent.add(mount);
    const pivot = new THREE.Group();
    pivot.position.set(0, -hoist*0.18, 0);
    pivot.add(new THREE.Mesh(g, byNation ? this._nationMat(nationKey)
                              : l.image ? this._flagMat(l.image) : this.mats.flag));
    mount.add(pivot);
    return { mount, pivot, mesh:pivot.children[0], hoist, fly, wave: 7.0*len/1.6,
             shape:shapeName, byNation, nationKey,
             seed: Math.random()*6.28,
             base:Float32Array.from(pos), u:Float32Array.from(us), v:Float32Array.from(vs) };
  }

  /* A flag with arms of its own. Built like a painted sail, and told about the
     sky by hand for the same reason: applySailLight is called on named
     materials, and one made after she is at sea would otherwise never learn. */
  _flagMat(src){
    this._flagMats = this._flagMats || {};
    if(this._flagMats[src]) return this._flagMats[src];
    const m = new THREE.MeshStandardMaterial({
      map: Naval.sailTexture(src, 'pavillon'),
      roughness:0.92, side:THREE.DoubleSide,
      emissive:0x2a2a2e, emissiveIntensity:0.10 });
    (this._sailMats = this._sailMats || []).push(m);
    if(this._skyU){
      Naval.applySailLight(m, this._skyU);
      if(this._aoU) Naval.applyShipAO(m, this._aoU);
      Naval.applyHaze(m, this._skyU);
    }
    this._flagMats[src] = m;
    return m;
  }

  /* A flag that FOLLOWS THE NATION (`"image": "nation"` in the sheet) flies the
     image flags.json gives that nation for its cut — `"streamer": "…"` — or
     under the key it names (`"nation:poupe"` → `"poupe": "…"`), and the
     ship's own colours when the nation has none. Read again whenever she
     changes colours, and when a model arriving late rebuilds her flags. */
  _nationMat(key){
    const src = this._nation && this._nation[key];
    return src ? this._flagMat(src) : this.mats.flag;
  }

  _applyNation(){
    for(const f of this.flags || [])
      if(f.byNation) f.mesh.material = this._nationMat(f.nationKey);
  }

  /* THE RUDDER, which turns with the helm. On a model, a piece named
     gouvernail (or rudder, safran) is hung on a pintle at its FORWARD edge —
     that is where a rudder is hinged to the sternpost — and swung about it.
     Without one, a blade is drawn on the sternpost: found at the waterline on
     the model's hull, the last station aft that is under water, since a
     galleon's counter overhangs it by metres. `model.rudder: false` draws
     none; a boat steered by her oars (rudder.power 0) has none either.

     Its angle is the one the solver steers with, ctrl.rudder × maxAngle, and
     the sign is the solver's: a positive helm pushes her stern to port and her
     bow to starboard, so the blade's after edge goes to starboard (−x). */
  _buildRudder(){
    if(this.rudder){
      const r = this.rudder;
      if(r.userData.drawn){
        if(r.parent) r.parent.remove(r);
        r.children[0].geometry.dispose();
      }
      this.rudder = null;
    }
    const spec = this.spec, raw = spec.raw || {};
    if(!(spec.rudderK > 0)) return;

    this._findWheel();
    if(this.modelRoot){
      /* Only a piece that HAS something in it: an empty node of that name —
         which is what an object exported without its mesh becomes — would
         have been hinged and swung with nothing to show, and the drawn blade
         left out for it. */
      const found = this._named(/^(gouvernail|rudder|safran)/i);
      if(found){
        const parent = found.parent;
        parent.updateWorldMatrix(true, true);
        const box = new THREE.Box3().setFromObject(found);
        // the hinge, in the parent's frame: centred, at the forward (+z) edge
        const toParent = new THREE.Matrix4().copy(parent.matrixWorld).invert();
        const a = new THREE.Vector3((box.min.x + box.max.x)/2, (box.min.y + box.max.y)/2, box.max.z).applyMatrix4(toParent);
        const pivot = new THREE.Group();
        pivot.position.copy(a);
        parent.add(pivot);
        pivot.updateWorldMatrix(true, false);
        pivot.attach(found);
        this.rudder = pivot;
        return;
      }
      if(raw.model && raw.model.rudder === false) return;
    }

    // --- drawn: a plank on the sternpost ---
    const sc = spec.L/24;
    const bottom = -(spec.hull.keelDepth + spec.hull.keelExtra)*0.92;
    const top = spec.deckMid + 0.3*sc;
    let zPost = -spec.L/2;
    if(this.modelRoot){
      const parts = this._modelParts();
      let hull = parts[0], best = -1;
      for(const p of parts){
        const v = p.size.x*p.size.y*p.size.z;
        if(v > best){ best = v; hull = p; }
      }
      // the aftmost point of the hull between the keel and just above the water
      let zMin = Infinity;
      const pa = hull.geom.attributes.position;
      for(let i=0;i<pa.count;i++){
        const y = pa.getY(i);
        if(y > bottom && y < 0.3*sc && pa.getZ(i) < zMin) zMin = pa.getZ(i);
      }
      if(isFinite(zMin)) zPost = zMin;
      for(const p of parts) p.geom.dispose();
    }
    const chord = 0.045*spec.L, thick = 0.16*sc;
    const g = new THREE.BoxGeometry(thick, top - bottom, chord);
    // hinged at its forward edge, which sits on the post
    g.translate(0, (top + bottom)/2, -chord/2);
    const blade = new THREE.Mesh(g, this.mats.timber);
    const pivot = new THREE.Group();
    pivot.position.set(0, 0, zPost);
    pivot.add(blade);
    pivot.userData.drawn = true;
    this.group.add(pivot);
    this.rudder = pivot;
  }

  /* The first node of the model whose name matches and which carries a mesh,
     itself or below it. A match with nothing in it is reported, once. */
  _named(re){
    let hit = null, empty = null;
    this.modelRoot.traverse(o => {
      if(hit || !re.test(o.name || '')) return;
      let mesh = false;
      o.traverse(c => { if(c.isMesh) mesh = true; });
      if(mesh) hit = o; else if(!empty) empty = o;
    });
    if(!hit && empty)
      console.warn('[' + this.spec.id + '] « ' + empty.name + ' » est vide dans le .glb (aucun maillage exporté) — ignoré');
    return hit;
  }

  /* THE WHEEL, a piece named barre (or wheel, helm). It turns about its own
     axle, taken as the axis along which the piece is THINNEST in its own
     frame — a wheel is a disc — and it turns a great deal more than the
     rudder does: model.wheelTurns each way (3 by default), six whole turns
     from hard over to hard over, as a big ship's wheel takes. Helm to starboard turns the
     top of the wheel to starboard. */
  _findWheel(){
    this.wheel = null;
    if(!this.modelRoot) return;
    const o = this._named(/^(barre|wheel|helm)/i);
    if(!o) return;
    const box = new THREE.Box3();
    o.traverse(c => {
      if(!c.isMesh) return;
      c.geometry.computeBoundingBox();
      const b = c.geometry.boundingBox.clone();
      if(c !== o){
        c.updateMatrix();
        b.applyMatrix4(new THREE.Matrix4().copy(c.matrix));
      }
      box.union(b);
    });
    const sz = box.getSize(new THREE.Vector3());
    const axis = sz.x <= sz.y && sz.x <= sz.z ? new THREE.Vector3(1, 0, 0)
               : sz.y <= sz.z ? new THREE.Vector3(0, 1, 0) : new THREE.Vector3(0, 0, 1);
    const raw = this.spec.raw || {};
    const turns = raw.model && raw.model.wheelTurns != null ? raw.model.wheelTurns : 3;
    this.wheel = { obj:o, q0:o.quaternion.clone(), axis, turns, q:new THREE.Quaternion() };
  }

  setRudder(helm){
    const h = helm || 0;
    if(this.rudder) this.rudder.rotation.y = h*this.spec.rudderMax;
    const w = this.wheel;
    if(w){
      w.q.setFromAxisAngle(w.axis, h*w.turns*Math.PI*2);
      w.obj.quaternion.copy(w.q0).multiply(w.q);
    }
  }

  /* Her mastheads, in her own frame, with the fall that carries each one (-1
     when none does: a procedural rig, or a mast the rig could not take). A
     mast already gone over the side is marked `gone`. Read off the spars as
     drawn on a model, off the sheet otherwise — the flags' own answer. */
  mastTops(){
    const out = [];
    if(this.modelRoot){
      for(const tp of this._sparScan().tops){
        let fall = -1, near = 0.06*this.spec.L;
        for(let i=0;i<this.falls.length;i++){
          const d = Math.abs(this.falls[i].position.z - tp.z);
          if(d < near){ near = d; fall = i; }
        }
        const gone = fall >= 0 && !!this.falls[fall].userData.fall;
        out.push({ x:0, y:tp.y, z:tp.z, fall, gone });
      }
    }else{
      for(const m of this.spec.masts)
        out.push({ x:0, y:this.spec.deckMid + m.height, z:m.z, fall:-1, gone:false });
    }
    return out;
  }

  /* Colours struck or flying — every flag she carries, together. */
  showColours(on){
    for(const f of this.flags || []) f.pivot.visible = on;
  }

  /* THE MASTHEADS AND THE END OF THE BOWSPRIT, READ OFF THE SPARS AS DRAWN.

     The rig finder answers "which masts carry yards", which is not the same
     question: on the Roter Löwe it found the mainmast's own pole, a foremast
     with no pole of its own, and the spritsail yard — and no mizzen at all,
     hers carrying no square yard. Her fore and mizzen masts are one mesh, and
     her bowsprit ends three metres short of where the sheet's length put it.

     So: every THIN piece standing near the centreline and tall enough to be a
     mast is binned along her length, and a run of stations rising above a
     third of her length is one mast, topped at its highest vertex — a raked
     mast spreads over several bins and is still one. The poles the rig has
     already taken are no longer in the model and are added back from their
     falls. The bowsprit is the thin piece that reaches furthest forward.
     Measured once per model. */
  _sparScan(){
    if(this._spars && this._spars.of === this.modelRoot) return this._spars;
    const spec = this.spec, bin = 0.06*spec.L, minTop = 0.35*spec.L;
    const parts = this._modelParts();
    const cols = new Map();
    let sprit = null;
    for(const p of parts){
      if(p.size.x > 0.08*spec.B || Math.abs(p.mid.x) > 0.08*spec.B) continue;
      const a = p.geom.attributes.position;
      const tall = p.size.y > 0.3*spec.L;
      for(let i=0;i<a.count;i++){
        const y = a.getY(i), z = a.getZ(i);
        if(!sprit || z > sprit.z) sprit = { z, y };
        if(!tall || y < minTop) continue;
        const k = Math.round(z/bin), c = cols.get(k);
        if(!c || y > c.y) cols.set(k, { k, y, z });
      }
    }
    for(const p of parts) p.geom.dispose();
    // adjacent stations are one mast
    const tops = [];
    for(const c of [...cols.values()].sort((a, b) => a.k - b.k)){
      const last = tops[tops.length - 1];
      if(last && c.k - last.k <= 1){
        if(c.y > last.y){ last.y = c.y; last.z = c.z; }
        last.k = c.k;
      }else tops.push({ k:c.k, y:c.y, z:c.z });
    }
    for(const fl of this.falls){
      if(!fl.userData.mast) continue;
      const z = fl.position.z, y = fl.position.y + fl.userData.height;
      const dup = tops.find(t => Math.abs(t.z - z) < 0.06*spec.L);
      if(dup){ if(y > dup.y){ dup.y = y; dup.z = z; } }
      else tops.push({ y, z });
    }
    this._spars = { of:this.modelRoot, tops, sprit };
    return this._spars;
  }

  /* Where her deck is at a station, and where her ends are — read off the
     model when there is one. Shared by the lanterns and the staffs, which both
     stand on the rail at her extremities. */
  _deckStations(){
    const spec = this.spec;
    /* Où est le pont à cette station, lu sur le modèle quand il y en a un, et
       pris au MAXIMUM sur une tranche plutôt qu'en un point : un couronnement
       sculpté se lit en dents de scie — 18,4 puis 12,4 puis 5,3 m d'une station
       à l'autre sur la Roter Löwe — et une station seule avait déjà fait tomber
       le feu cinq mètres sous sa lisse, à l'intérieur de son propre château. */
    let deckNear, zAft, zFore;
    if(this.modelRoot){
      const parts = this._modelParts();
      const deckAt = this._deckProfile(parts);
      let hull = parts[0], best = -1;
      for(const p of parts){
        const v = p.size.x*p.size.y*p.size.z;
        if(v > best){ best = v; hull = p; }
      }
      const z0 = hull.box.min.z, z1 = hull.box.max.z, span = z1 - z0;
      zAft = z0 + span*0.02;                    // tout à l'arrière, +z étant l'étrave
      zFore = z1 - span*0.02;
      deckNear = z => {
        let y = -Infinity;
        for(let f = -0.03; f <= 0.031; f += 0.01)
          y = Math.max(y, deckAt(Math.min(z1, Math.max(z0, z + span*f))));
        return y;
      };
      for(const p of parts) p.geom.dispose();
    }else{
      zAft = -spec.L*0.45;
      zFore = spec.L*0.48;
      deckNear = z => this.lines.deckY(Math.min(1, Math.max(0, z/spec.L + 0.5)));
    }
    return { zAft, zFore, deckNear };
  }

  /* Stream them. The fly points dead downwind, so the pivot's yaw comes straight
     from the apparent wind the solver already knows: its bearing off the bow and
     which tack she is on. Falling light, the ensign stops rippling and hangs —
     it loses its length as it droops, which is what tells you at a glance that
     the breeze has gone, before any instrument does. Each flag keeps its own
     phase, or three would ripple as one; a long streamer carries more waves. */
  setFlag(beta, tack, vApp, t){
    const yaw = Math.atan2(tack*Math.sin(beta), -Math.cos(beta));
    const drive = Math.min(1, vApp/8);
    for(const f of this.flags || []){
      f.pivot.rotation.y = yaw;
      const attr = f.mesh.geometry.attributes.position, arr = attr.array;
      for(let k=0;k<f.u.length;k++){
        const i3 = k*3, u = f.u[k];
        // the ripple starts at nothing on the halyard and builds toward the fly
        arr[i3]   = Math.sin(u*f.wave - t*6.5 + f.v[k]*1.2 + f.seed) * f.hoist*0.42 * drive * Math.pow(u, 1.3);
        arr[i3+1] = f.base[i3+1] - (1-drive)*u*u*f.fly*0.55;
        arr[i3+2] = f.base[i3+2] * (0.80 + 0.20*drive);
      }
      attr.needsUpdate = true;
      f.mesh.geometry.computeVertexNormals();
    }
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
    for(const L of this.lanternList || []){
      L.group.removeFromParent();
      const S = L.swing;
      if(S){
        // the lamp goes back where the model had it, or a rebuild would lose it
        S.pivot.rotation.set(0, 0, 0);
        S.pivot.updateWorldMatrix(true, false);
        if(S.home && S.mesh.parent === S.pivot) S.home.attach(S.mesh);
        S.pivot.removeFromParent();
      }
    }
    this.lanternList = [];
    const spec = this.spec;

    const { zAft, deckNear } = this._deckStations();

    /* Une liste VIDE veut dire aucun feu, et pas le feu par défaut : une fiche
       qui déclare ses lanternes dit tout ce qu'elle porte, y compris rien. */
    const list = spec.lanterns || [{ z:zAft }];
    for(const l of list){
      const x = l.x != null ? l.x : (l.xFrac || 0)*spec.B;
      const z = l.z != null ? l.z : (l.zFrac != null ? l.zFrac*spec.L : zAft);
      const y = l.y != null ? l.y : deckNear(z) + 0.10*spec.L/6 + (l.above || 0);
      const L = this._lanternAt(x, y, z, l);
      if(l.hang) this._hangLantern(L, l.hang);
      this.lanternList.push(L);
    }
    this.lantern = this.lanternList[0] || null;
  }

  /* UNE LANTERNE PENDUE AU BARROT. The sheet names a mesh of the model
     (`hang`, e.g. "cabineLantern"); that mesh is taken off wherever it was
     and hung from a hook in the deckhead — found by a ray straight up from the
     top of its box — on a line drawn here from the hook down to the lamp. The
     flame (a candle here) is moved into it, at the middle of the box. It then
     swings as a real pendulum does (swingLanterns). If the model's own rope
     already reaches the deckhead, there is no line to draw and the hook is the
     top of the box. Without the mesh in the model, the flame stays where the
     sheet put it.

     Its lamp alone casts shadows (a cube map: six renders of what is near),
     kept small and short, and redrawn only while the eye is in the room at
     night — see lanternShadows. Turned on here, once, as the model arrives:
     switching a shadow on later would recompile the scene in play. */
  _hangLantern(L, name){
    if(!this.modelRoot) return;
    const want = String(name).toLowerCase();
    let o = null;
    this.modelRoot.traverse(c => { if(!o && (c.name || '').toLowerCase() === want) o = c; });
    if(!o){
      console.warn('[' + this.spec.id + '] lanterne « ' + name + ' » absente du .glb — la flamme reste à sa place');
      return;
    }
    this.group.updateWorldMatrix(true, true);
    const inv = new THREE.Matrix4().copy(this.group.matrixWorld).invert();
    const box = new THREE.Box3().setFromObject(o).applyMatrix4(inv);   // in her frame
    const mid = box.getCenter(new THREE.Vector3());
    // the deckhead above the lamp: its own meshes left out, and no further than 2 m
    const skip = new Set();
    o.traverse(c => skip.add(c));
    const solid = [];
    this.modelRoot.traverse(c => { if(c.isMesh && !skip.has(c)) solid.push(c); });
    const top = new THREE.Vector3(mid.x, box.max.y - 0.02, mid.z);
    const ray = new THREE.Raycaster(this.group.localToWorld(top.clone()),
      new THREE.Vector3(0, 1, 0).transformDirection(this.group.matrixWorld), 0, 2);
    const hit = ray.intersectObjects(solid, false)[0];
    const line = hit ? Math.max(0, hit.distance - 0.02) : 0;
    const hookY = box.max.y + line;

    const pivot = new THREE.Group();
    pivot.position.set(mid.x, hookY, mid.z);            // the hook
    this.group.add(pivot);
    pivot.updateWorldMatrix(true, false);
    const home = o.parent;
    pivot.attach(o);                                    // keeps where it hangs
    if(line > 0.03){
      // tarred hemp, from the hook to the lamp's ring
      const rope = new THREE.Mesh(new THREE.CylinderGeometry(0.007, 0.007, line, 5),
        new THREE.MeshStandardMaterial({ color:0x3a2e22, roughness:0.9 }));
      rope.position.y = -line/2;
      rope.userData.noCast = true;
      pivot.add(rope);
    }
    /* THE CANDLE STANDS ON THE LANTERN'S FLOOR, not in the middle of its box:
       the box reaches up the ring and whatever hangs it, and the middle of that
       put the flame under the cap. A ray down from the middle finds the floor
       among the lantern's own faces; the wick is a candle's height above it. */
    const own = [];
    o.traverse(c => { if(c.isMesh) own.push(c); });
    const down = new THREE.Raycaster(this.group.localToWorld(mid.clone()),
      new THREE.Vector3(0, -1, 0).transformDirection(this.group.matrixWorld), 0, box.max.y - box.min.y);
    const floor = down.intersectObjects(own, false)[0];
    let flameY = mid.y;
    if(floor){
      const fy = this.group.worldToLocal(floor.point.clone()).y;
      flameY = Math.min(mid.y, fy + 0.16);               // 14 cm of wax, and the wick
    }
    L.group.removeFromParent();
    pivot.add(L.group);
    L.group.position.set(0, flameY - hookY, 0);        // the flame, inside

    /* AND THE LANTERN THROWS NO SHADOW OF ITS OWN. Its panes are faces like any
       other to the shadow map, which knows nothing of glass: the flame was shut
       in a box and got out by four slits in the cap — four bright patches on the
       deckhead and a black cabin. Its own pieces, and the line, are left out of
       the shadow; everything else in the cabin still casts. */
    for(const c of own){ c.castShadow = false; c.userData.noCast = true; }

    const lt = L.light;
    lt.castShadow = true;
    lt.shadow.mapSize.set(256, 256);
    lt.shadow.camera.near = 0.05;
    lt.shadow.camera.far = Naval.LANTERN_SHADOW.range;
    lt.shadow.bias = -0.004;
    lt.shadow.autoUpdate = false;                       // drawn only when looked at
    lt.shadow.needsUpdate = true;

    L.swing = { pivot, mesh: o, home, rope: line > 0.03, len: Math.max(0.15, hookY - flameY),
                o: new THREE.Vector3(), u: new THREE.Vector3(),
                vh: new THREE.Vector3(), ah: new THREE.Vector3(), live: false };
  }

  /* Only when it can be seen: the eye within range of the lamp, the lamp lit.
     Elsewhere the map is left as it was — the six renders cost nothing — and
     the lamp's shadow flag never changes, so nothing recompiles. */
  lanternShadows(camPos){
    for(const L of this.lanternList || []){
      const S = L.swing;
      if(!S || !L.light.castShadow) continue;
      const near = L.light.intensity > 0 && camPos &&
        L.group.getWorldPosition(this._swW || (this._swW = new THREE.Vector3())).distanceTo(camPos)
          < Naval.LANTERN_SHADOW.range;
      L.light.shadow.autoUpdate = !!near;
    }
  }

  /* Each frame, for the lanterns that hang. A REAL PENDULUM: the lamp is a
     weight on a line of fixed length, integrated in the world, and what drives
     it is gravity less the acceleration of the hook. The hook sits metres
     above her centre of roll, so every roll throws it sideways and the lamp
     is left behind, then catches up and swings past; that is the dance, and
     no angle in it is written by hand. The weight is carried relative to the
     hook (`o`, world axes) with its velocity relative to the hook (`u`), and
     the hook's velocity comes from the body's own (v + ω × r), so the
     floating origin's jumps never reach it. A pressed clock (a frame over a
     quarter second) just lets it hang. */
  swingLanterns(body, dt){
    if(!body || !(dt > 0)) return;
    const q = this._swQ || (this._swQ = new THREE.Quaternion());
    const r = this._swR || (this._swR = new THREE.Vector3());
    const vh = this._swH || (this._swH = new THREE.Vector3());
    const a = this._swA || (this._swA = new THREE.Vector3());
    const d = this._swD || (this._swD = new THREE.Vector3());
    const DOWN = Naval.LANTERN_SHADOW.down;
    for(const L of this.lanternList || []){
      const S = L.swing;
      if(!S) continue;
      r.copy(S.pivot.position).applyQuaternion(body.quat);          // hook, from her origin
      vh.copy(body.angVel).cross(r).add(body.vel);                  // the hook's velocity
      if(!S.live || dt > 0.25){
        S.o.set(0, -S.len, 0); S.u.set(0, 0, 0);
        S.vh.copy(vh); S.ah.set(0, 0, 0); S.live = true;
      }else{
        a.copy(vh).sub(S.vh).multiplyScalar(1/dt);
        S.vh.copy(vh);
        S.ah.lerp(a, 1 - Math.exp(-dt/0.04));    // the solver's substeps make it grainy
        const w = Math.sqrt(9.81/S.len), c = 2*0.05*w;   // a lamp on a line barely damps
        const n = Math.ceil(dt/0.008), h = dt/n;
        for(let i = 0; i < n; i++){
          S.u.x -= S.ah.x*h; S.u.y += (-9.81 - S.ah.y)*h; S.u.z -= S.ah.z*h;
          S.u.multiplyScalar(Math.exp(-c*h));
          S.o.addScaledVector(S.u, h).setLength(S.len);
          d.copy(S.o).multiplyScalar(1/S.len);
          S.u.addScaledVector(d, -S.u.dot(d));                   // the line takes the rest
        }
      }
      d.copy(S.o).normalize().applyQuaternion(q.copy(body.quat).invert());   // in her frame
      S.pivot.quaternion.setFromUnitVectors(DOWN, d);
    }
  }

  /* Un feu : sa lueur de près, sa marque de loin, et sa lampe. */
  _lanternAt(x, y, z, l){
    const spec = this.spec, tex = Naval.glowTexture();
    const group = new THREE.Group();
    group.position.set(x, y, z);

    const k = spec.L/24 * ((l && l.size != null) ? l.size : 1);
    const col = (l && l.color != null)
      ? (typeof l.color === 'string' ? parseInt(l.color) : l.color) : 0xffcf7a;
    const mk = (size, atten, op) => {
      const m = new THREE.Sprite(new THREE.SpriteMaterial({
        map:tex, color:col, transparent:true, opacity:op,
        blending:THREE.AdditiveBlending, depthWrite:false,
        sizeAttenuation:atten, fog:false }));
      m.scale.setScalar(size);
      group.add(m);
      return m;
    };
    const halo = mk(3.4*k, true, 0.85);       // la lampe, en mètres
    const mark = mk(0.030, false, 0.95);      // le repère de position, en pixels

    /* UNE BOUGIE n'est pas un feu de position : on ne la voit pas à deux
       milles, donc pas de repère de loin — l'opacité reste à zéro et le sprite
       dans la scène, pour la même raison que la lampe (pas de recompilation).
       Et une flamme seule en l'air ne se lit pas : il lui faut son bâton de
       cire, posé SOUS la flamme, dont y est la hauteur. */
    const candle = !!(l && l.kind === 'candle');
    if(candle){
      const wax = new THREE.Mesh(
        new THREE.CylinderGeometry(0.022, 0.025, 0.14, 10),
        new THREE.MeshStandardMaterial({ color:0xefe6cf, roughness:0.7 }));
      wax.position.y = -0.085;
      group.add(wax);
    }

    /* A real flame, not a bulb: she is lit by a wick in a horn lantern, so she
       breathes. Cheap, and it is what stops the mark reading as a HUD marker. */
    const light = new THREE.PointLight(0xffb765, 0, 26*k, 2);
    group.add(light);

    this.group.add(group);
    return { group, halo, mark, light, k, candle, seed: Math.random()*100 };
  }

  /* LES FENÊTRES S'ALLUMENT AVEC LES FEUX, et rien n'est peint deux fois pour
     ça : une matière du .glb qui porte une carte ÉMISSIVE — ce qu'un nœud
     Émission de Blender exporte en glTF — est une matière qui a une part
     lumineuse, et c'est exactement ce qu'on veut allumer à la nuit tombée. Le
     jour son intensité est à zéro : une vitre au soleil ne luit pas, elle
     reflète.

     Le repli est le contrat des canons, un mot dans un nom de matière : une
     matière nommée « fenetre », « window », « lampe »... reçoit une émissive
     chaude sans qu'aucune image n'ait à être peinte. Une fiche règle la force
     par model.nightGlow (1 par défaut, 0 pour ne rien allumer). */
  _findNightGlow(){
    this.nightMats = [];
    if(!this.modelRoot) return;
    const g = this.spec.model && this.spec.model.nightGlow;
    const gain = (g != null) ? g : 1;
    if(!(gain > 0)) return;
    const named = Naval.GLOW_NAMES;
    const seen = new Set();
    this.modelRoot.traverse(o => {
      if(!o.isMesh) return;
      for(const mat of [].concat(o.material)){
        if(!mat || seen.has(mat)) continue;
        const byName = named.test(mat.name || '');
        if(!mat.emissiveMap && !byName) continue;
        seen.add(mat);
        /* Une carte émissive est MULTIPLIÉE par la couleur émissive, que
           l'exportateur laisse noire quand le facteur est nul : sans ce blanc,
           la carte est là et ne donne rien. */
        if(mat.emissive && mat.emissive.getHex() === 0x000000)
          mat.emissive.setHex(mat.emissiveMap ? 0xffffff : 0xffb765);
        const base = (mat.emissiveIntensity != null && mat.emissiveIntensity > 0)
          ? mat.emissiveIntensity : 1;
        this.nightMats.push({ mat, base: base*gain });
        mat.emissiveIntensity = 0;
        mat.needsUpdate = true;
      }
    });
  }

  /* LOST IN THE HAZE: not drawn at all — hull, rig, shadow, occlusion — and
     it is done on the LAYERS, not on `visible`, for two reasons. `visible`
     already means something on her parts (a split sail, a mast gone by the
     board, colours struck) and toggling it here would undo those. And her
     lanterns must stay exactly as they are: a light the renderer stops seeing
     recompiles the whole scene, and a lamp is the one thing the eye does pick
     out through the murk. So the lantern groups are skipped whole. */
  setHazed(hidden){
    if(this._hazed === hidden) return;
    this._hazed = hidden;
    const keep = new Set((this.lanternList || []).map(L => L.group));
    const walk = o => {
      if(keep.has(o)) return;
      if(o.isMesh || o.isLine || o.isPoints){
        if(hidden){ o.userData.hazeMask = o.layers.mask; o.layers.mask = 0; }
        else if(o.userData.hazeMask != null){
          o.layers.mask = o.userData.hazeMask; o.userData.hazeMask = null;
        }
      }
      for(const c of o.children) walk(c);
    };
    walk(this.group);
  }

  /* Her highest point above her origin, measured once — what the haze test
     looks at, since the rig stands in thinner air than the hull and is the
     last of her to go. */
  tallY(){
    // measured again once a .glb has come in: the drawn stand-in is shorter
    if(this._tallY == null || this._tallOf !== this.modelRoot){
      this._tallOf = this.modelRoot;
      this._tallY = new THREE.Box3().setFromObject(this.group).max.y
                  - this.group.position.y;
    }
    return this._tallY;
  }

  /* Lit only when it is dark enough to want her. `night` comes from the stage,
     so lantern, sky and the sun's own colour all turn together. */
  setLantern(night, t, camPos, hazeU){
    const on = Math.max(0, Math.min(1, night));
    /* ALLUMÉ OU ÉTEINT, jamais à mi-feu : les fenêtres s'allument au crépuscule
       et sont soufflées à l'aube, d'un coup. Deux seuils, sinon un soleil qui
       hésite à la limite ferait battre tout le bord. */
    const N = Naval.NIGHT;
    if(on >= N.lightAt) this._lit = true;
    else if(on <= N.snuffAt) this._lit = false;
    if(this.nightMats)
      for(const n of this.nightMats) n.mat.emissiveIntensity = this._lit ? n.base*N.glow : 0;
    /* AU LOIN, LE FEU S'EFFACE. Le repère de position est à taille d'ÉCRAN
       fixe, et c'est voulu — sans lui un fanal disparaît à deux milles — mais
       il avait aussi un éclat fixe : à 1,5 km comme à 5 le même disque, que le
       bloom élargissait encore, et une voile au loin se lisait comme un
       réverbère. Passé farFrom, l'éclat tombe en (farFrom/d)^farFade et la
       taille comme sa racine, jamais sous farMinSize ; la brume l'éteint à
       son tour, en racine de ce qu'elle laisse passer, une lumière perçant
       mieux la brume qu'une coque. */
    let far = 1, farSize = 1;
    if(camPos){
      const d = this.group.position.distanceTo(camPos);
      if(d > N.farFrom){
        far = Math.pow(N.farFrom/d, N.farFade);
        farSize = Math.max(N.farMinSize, Math.sqrt(far));
      }
      if(hazeU) far *= Math.sqrt(Naval.hazeTransmit(camPos, this.group.position, hazeU));
    }
    for(const L of this.lanternList || []){
      /* ON NE MASQUE PLUS LE GROUPE, et c'est un vrai défaut corrigé : il porte
         une LAMPE, et three compile ses programmes contre le nombre de lumières
         qu'il VOIT. Masquer le fanal le jour puis le rendre à la nuit changeait
         donc ce compte, et toute la scène était recompilée sur place —
         mesuré : 49 programmes le jour, 63 à la première image de nuit, et
         cette image-là durait 106 ms au lieu de 5. Un à-coup à chaque
         crépuscule, signalé à l'usage.

         On commute donc par l'INTENSITÉ et par l'opacité, la lampe restant dans
         la scène et visible à zéro candela — exactement la réserve de lampes
         des bouches à feu, pour exactement la même raison. */
      /* Et les sprites eux-mêmes restent VISIBLES, à opacité nulle : cachés le
         jour, leurs deux programmes se compilaient à la première image de nuit
         — trois de moins que la lampe, mais au même instant, donc dans le même
         à-coup. Deux quadrilatères transparents par feu ne coûtent rien ; une
         compilation au crépuscule, si. */
      const lit = on > 0.01;
      if(!lit){
        L.light.intensity = 0;
        L.halo.material.opacity = L.mark.material.opacity = 0;
        continue;
      }
      // two slow beats out of phase read as a flame; one alone reads as a pulse
      const flick = (L.candle && !L.swing)
        // a bare wick in cabin draughts: quicker and less even than a horn lantern
        // (a candle shut in a hanging lantern burns as a lantern does)
        ? 0.80 + 0.12*Math.sin(t*9.7 + L.seed) + 0.08*Math.sin(t*23.3 + L.seed*1.3)
        : 0.86 + 0.14*Math.sin(t*7.3 + L.seed) + 0.06*Math.sin(t*17.1 + L.seed*1.7);
      L.halo.material.opacity = 0.85*on*flick*far;
      L.mark.material.opacity = L.candle ? 0 : 0.95*on*flick*far;
      L.mark.scale.setScalar(0.030*farSize);
      L.light.intensity = 2.6*on*flick;
    }
  }

  /* THE MEN ON DECK (crew.js). A sheet may place them; otherwise they are
     found places on her deck by casting rays down onto her own meshes — the
     only thing that knows where a deck is flat and clear on a model that
     declares nothing. */
  _buildCrew(){
    if(this.crew){
      this.group.remove(this.crew);
      this.crew.geometry.dispose();
      for(const m of [].concat(this.crew.material)) m.dispose();
      this.crew = null;
    }
    if(Naval.crewShips) Naval.crewShips.add(this);    // to be re-dressed if a model arrives
    const C = Naval.CREW, spec = this.spec, want = spec.crew;
    if(!C || !C.enabled || !Naval.crewMesh) return;
    if(want === 0 || (Array.isArray(want) && !want.length)) return;
    if(want == null && spec.L < C.minLength) return;
    const seed = Naval.crewSeed(spec.id);
    const spots = Array.isArray(want) ? this._crewGiven(want)
                : this._crewSpots(typeof want === 'number' ? want : C.count, seed);
    if(!spots.length) return;
    this.crew = Naval.crewMesh(spots, seed);
    this.group.add(this.crew);
    // rebuilt at sea (a model arrived): into her air at once, as applyAtmosphere would
    if(this._skyU) for(const m of [].concat(this.crew.material)){
      Naval.applySnowCover(m, this._snowU);
      Naval.applyHaze(m, this._skyU);
    }
  }

  _crewCaster(){
    this.group.updateWorldMatrix(true, true);
    const cloth = new Set(this.canvases || []), meshes = [];
    (this.modelRoot || this.procedural).traverse(o => {
      if(o.isMesh && !o.isInstancedMesh && !cloth.has(o)) meshes.push(o);
    });
    const Q = this.group.getWorldQuaternion(new THREE.Quaternion());
    const Qi = Q.clone().invert();
    const ray = new THREE.Raycaster();
    const dir = new THREE.Vector3(), o = new THREE.Vector3(), n = new THREE.Vector3();
    // the first thing met from a local point along a local direction
    return (x, y, z, dx, dy, dz, far) => {
      o.set(x, y, z); this.group.localToWorld(o);
      ray.set(o, dir.set(dx, dy, dz).applyQuaternion(Q));
      ray.far = far;
      const h = ray.intersectObjects(meshes, false)[0];
      if(!h) return null;
      const p = this.group.worldToLocal(h.point.clone());
      n.copy(h.face ? h.face.normal : dir).transformDirection(h.object.matrixWorld).applyQuaternion(Qi);
      return { y:p.y, up:n.y };
    };
  }

  _crewGiven(list){
    const spec = this.spec, cast = this._crewCaster(), st = this._deckStations();
    const out = [];
    for(const c of list){
      const x = c.x != null ? c.x : (c.xFrac || 0)*spec.B;
      const z = c.z != null ? c.z : (c.zFrac || 0)*spec.L;
      let y = c.y;
      if(y == null){
        const h = cast(x, st.deckNear(z) + 2.5, z, 0, -1, 0, 12);
        y = h ? h.y : st.deckNear(z);
      }
      out.push({ x, y, z, yaw: (c.yaw || 0)*Math.PI/180 });
    }
    return out;
  }

  _crewSpots(count, seed){
    const spec = this.spec, cast = this._crewCaster(), st = this._deckStations();
    const rnd = Naval.crewRandom(seed);
    const out = [];
    // a place is good if the deck is level under his feet and nothing stands
    // within an arm's length at knee and at chest height
    const good = (x, z) => {
      const rail = st.deckNear(z), top = rail + 2.5;
      const h = cast(x, top, z, 0, -1, 0, 8);
      if(!h || h.up < 0.85 || h.y > rail + 0.05) return null;
      for(const [ax, az] of [[0.3,0],[-0.3,0],[0,0.3],[0,-0.3]]){
        const k = cast(x + ax, top, z + az, 0, -1, 0, 8);
        if(!k || k.up < 0.85 || Math.abs(k.y - h.y) > 0.12) return null;
      }
      for(const hy of [0.35, 1.2])
        for(const [ax, az] of [[1,0],[-1,0],[0,1],[0,-1],[0.7,0.7],[-0.7,0.7],[0.7,-0.7],[-0.7,-0.7]])
          if(cast(x, h.y + hy, z, ax, 0, az, 0.45)) return null;
      // and not in a gun's recoil
      for(const g of this.guns || []){
        for(let t = 0; t <= 3; t += 0.5){
          const gx = g.p.x - g.dir.x*t, gz = g.p.z - g.dir.z*t;
          if(Math.hypot(gx - x, gz - z) < 0.9) return null;
        }
      }
      return h.y;
    };
    // gather places, then take them far apart: drawn in order, four men had
    // all landed on her forecastle
    const found = [];
    for(let i = 0; i < 240 && found.length < Math.max(12, count*4); i++){
      const x = (rnd() - 0.5)*0.55*spec.B, z = (rnd() - 0.5)*0.8*spec.L;
      const y = good(x, z);
      if(y != null) found.push({ x, y, z, yaw: rnd()*Math.PI*2 });
    }
    const gap = (a, b) => Math.hypot(a.x - b.x, a.z - b.z);
    if(found.length) out.push(found.shift());
    while(out.length < count && found.length){
      let bi = -1, bd = 1.6;
      found.forEach((c, i) => {
        const d = Math.min(...out.map(s => gap(s, c)));
        if(d > bd){ bd = d; bi = i; }
      });
      if(bi < 0) break;
      out.push(found.splice(bi, 1)[0]);
    }
    return out;
  }

  /* Not drawn from afar or in the haze — a man is under a pixel long before —
     nor aboard a ship that has gone down. */
  setCrew(camPos, aboard, body, dt){
    if(!this.crew) return;
    const C = Naval.CREW;
    this.crew.visible = aboard && !this._hazed && !!camPos
      && this.group.position.distanceTo(camPos) < C.farHide;
    if(!this.crew.visible || !body || !(dt > 0)) return;
    /* How the deck moves under them. The true up in her frame, taken back in
       part (C.upright) and followed with a lag, so a sudden roll catches them
       for a moment before they right themselves. */
    const U = this.crew.userData, u = U.u;
    const q = this._crewQ || (this._crewQ = new THREE.Quaternion());
    const up = this._crewV || (this._crewV = new THREE.Vector3());
    up.set(0, 1, 0).applyQuaternion(q.copy(body.quat).invert());
    const tilt = Math.acos(Math.min(1, up.y))*180/Math.PI;
    up.multiplyScalar(C.upright); up.y += 1 - C.upright; up.normalize();
    u.uCrewUp.value.lerp(up, 1 - Math.exp(-dt/Math.max(0.02, C.lag))).normalize();
    // the stance follows the held peak of the tilt: they stay braced between rolls
    U.peak = Math.max(tilt, U.peak*Math.exp(-dt/8));
    const ss = x => x <= 0 ? 0 : x >= 1 ? 1 : x*x*(3 - 2*x);
    const stance = ss((U.peak - C.stanceFrom)/Math.max(0.1, C.stanceFull - C.stanceFrom));
    u.uCrewStance.value += (stance - u.uCrewStance.value)*(1 - Math.exp(-dt/1.5));
    // the arms answer the rate of roll and pitch, quickly out and slowly back
    const w = body.angVel;   // world frame: leaving out y leaves roll and pitch
    const rate = Math.hypot(w.x, w.z)*180/Math.PI;
    const brace = ss((rate - C.braceFrom)/Math.max(0.1, C.braceFull - C.braceFrom));
    const b = u.uCrewBrace.value;
    u.uCrewBrace.value = b + (brace - b)*(1 - Math.exp(-dt/(brace > b ? 0.25 : 1.2)));
  }

  /* Put her into the lighting: her own shadows, and the layer that the
     occlusion pass renders on its own. She stays on the default layer too, so
     enabling this one changes nothing about how she is normally drawn. */
  enableLighting(){
    this.group.traverse(o => {
      if(!o.isMesh) return;
      o.castShadow = !o.userData.noCast;     // a hanging lantern's own glass, see _hangLantern
      o.receiveShadow = true;
      // the men stay off her own passes: measured, each pass is paid per draw
      if(!o.userData.crew) o.layers.enable(Naval.SHIP_LAYER);
    });
  }

  syncTo(body){
    this.group.position.copy(body.pos);
    this.group.quaternion.copy(body.quat);
    // her frame for the scars, from the body itself: the group's matrix is last frame's
    if(this.scars.length){
      this._one = this._one || new THREE.Vector3(1, 1, 1);
      this._scarU.uScarInv.value.compose(body.pos, body.quat, this._one).invert();
    }
  }

  /* Hoist another ensign while she is at sea: an image path (or a data: URI),
     or nothing for the drawn death's head. `nation`, the flags.json entry it
     comes from, gives the flags that follow the nation their own images. The picture only — whether she is
     hostile stays with `appearance.ensign`. In the published page a path must
     have been carried in by the build, which embeds only what the sheets name. */
  setEnsignMap(src, nation){
    const m = this.mats.flag;
    this._nation = nation || null;
    this._applyNation();
    // what the sheet flew, kept once so the colours can be given back
    if(!this._ensign0) this._ensign0 = { map:m.map, color:m.color.getHex(),
      emissive:m.emissive.getHex(), ei:m.emissiveIntensity };
    m.map = src ? Naval.sailTexture(src, 'pavillon') : Naval.jollyTexture();
    m.color.set(0xffffff);                 // the image carries its own colours
    // and the faint lift of a painted flag, not the white one's: that would wash the colours grey
    m.emissive.set(0x2a2a2e); m.emissiveIntensity = 0.10;
    m.needsUpdate = true;
  }

  /* Back to the colours her sheet gave her. */
  resetEnsign(){
    const o = this._ensign0, m = this.mats.flag;
    this._nation = null;
    this._applyNation();
    if(!o) return;
    m.map = o.map; m.color.setHex(o.color);
    m.emissive.setHex(o.emissive); m.emissiveIntensity = o.ei;
    m.needsUpdate = true;
  }

  /* A HIT LEAVES A MARK, AND A SECOND HIT IN THE SAME PLACE MAKES IT WORSE.
     A hit close to an existing scar deepens that scar instead of starting
     another — the same rule as a breach that works rather than multiplies —
     and what the eye reads, from a cable off, is where she has been fought
     hardest. And NOTHING shows for the first two: a single ball through oak is
     a hole the size of a fist, invisible at the range she is looked at. The
     mark comes in from the third, darkens with each one after, opens to raw oak
     past the fifth and only a bay hulled eight times over shows a hole. The
     first cut marked every hit at once, black from the first shot, and read as
     paint thrown at her — signalled from a capture.

     Strength goes with the calibre that did it, and is capped at ten: beyond
     that there is no more ship there to look worse. The list holds twenty-four;
     when it is full the lightest mark gives way, a scorch mattering less than
     a hole. */
  scar(world, k){
    if(!world) return;
    this._gq = this._gq || new THREE.Quaternion();
    this._gl = this._gl || new THREE.Vector3();
    const loc = this._gl.copy(world).sub(this.group.position)
                        .applyQuaternion(this._gq.copy(this.group.quaternion).invert());
    const add = Math.min(2, Math.max(0.6, (k || 0.5)*2));
    const merge = 1.1*this._scarU.uScarK.value;
    let best = null, bd = Infinity;
    for(const s of this.scars){ const d = s.p.distanceTo(loc); if(d < bd){ bd = d; best = s; } }
    if(best && bd < merge){
      best.p.lerp(loc, 1/(best.w + 1));
      best.w = Math.min(10, best.w + add);
    }else if(this.scars.length < Naval.SCAR_MAX){
      this.scars.push({ p:loc.clone(), w:add });
    }else{
      let weak = this.scars[0];
      for(const s of this.scars) if(s.w < weak.w) weak = s;
      weak.p.copy(loc); weak.w = add;
    }
    this._uploadScars();
  }

  _uploadScars(){
    const U = this._scarU, n = Math.min(this.scars.length, Naval.SCAR_MAX);
    for(let i=0;i<n;i++){ const s = this.scars[i]; U.uScar.value[i].set(s.p.x, s.p.y, s.p.z, s.w); }
    U.uScarCount.value = n;
    this._one = this._one || new THREE.Vector3(1, 1, 1);
    this.group.updateMatrix();
    U.uScarInv.value.copy(this.group.matrix).invert();
  }

  /* `set` is the fraction spread, which the solver carries. It used to be the
     boolean order, and the cloth appeared and vanished with it. */
  setTrim(sheet, tack, set, luffing, t, load){
    if(!this.rigs.length) return;          // no canvas to trim
    const shake = luffing ? Math.sin(t*11)*0.10 : 0;
    /* THE YARDS ARE HAULED ROUND, THEY DO NOT JUMP. The solver's tack is the
       bare sign of the wind across her, and it flips the instant the apparent
       wind crosses dead astern — which, running, a yaw of a degree does every
       few seconds. Written straight into the pivots it swung the whole rig
       from one board to the other in a frame, twice the sheet angle at once.
       Nothing in the physics reads that sign (lift is oriented off the stem),
       so the cure lives here: the board SHOWN changes only once the wind has
       stayed on the other side for BRACE_HOLD seconds, and the braces then
       come round at BRACE_RATE radians a second of game time. */
    const dt = Math.max(0, Math.min(0.25, t - (this._trimT != null ? this._trimT : t)));
    this._trimT = t;
    if(this._tackShown == null){ this._tackShown = tack; this._brace = -tack*sheet; }
    if(tack !== this._tackShown){
      this._tackHeld = (this._tackHeld || 0) + dt;
      if(this._tackHeld >= Naval.BRACE_HOLD){ this._tackShown = tack; this._tackHeld = 0; }
    }else this._tackHeld = 0;
    /* Eased in and out: the braces gather way at BRACE_ACCEL and are checked
       just soon enough to stop on the mark — the speed allowed is the one from
       which that deceleration still stops within the angle left, √(2·a·err).
       Written against the error rather than as a timed curve, so a sheet
       trimmed while the yards are still swinging is simply followed. */
    const want = -this._tackShown * sheet;
    const err = want - this._brace;
    const A = Naval.BRACE_ACCEL;
    const vWant = Math.sign(err)*Math.min(Naval.BRACE_RATE, Math.sqrt(2*A*Math.abs(err)));
    const dv = A*dt;
    this._braceV = (this._braceV || 0) + Math.max(-dv, Math.min(dv, vWant - (this._braceV || 0)));
    this._brace += this._braceV*dt;
    // never through the mark: a step that crosses it lands on it, at rest
    if((want - this._brace)*err <= 0){ this._brace = want; this._braceV = 0; }
    const angle = this._brace + shake;
    /* Furling takes in the cloth, not the spars — a vessel under bare poles
       still has her yards crossed and her boom shipped. It matters twice over
       for an imported model, whose own yards now hang in these pivots: hiding
       the group would strip her rig off the masts. */
    /* Always drawn, roll and all. Hiding her at nought was the old behaviour
       and it threw away the very thing the stowed remnant exists for: a handed
       sail is a fat roll of canvas along its spar, not an absence. Eighty-one
       vertices a sail — there is nothing to save by leaving it out. */
    /* — sauf celles qui ont éclaté. setTrim repassait ici soixante fois par
         seconde et remettait tout le monde visible, donc une voile déchirée
         serait revenue à l'image suivante. */
    for(const c of this.canvases) c.visible = (c.userData.split !== true);
    for(const rig of this.rigs){
      rig.rotation.y = (rig===this.jibRig ? angle*0.75 : angle);
    }
    this.setSailShape(load || 0, luffing, t, set);
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

/* A tangent-space normal map, derived from a greyscale height read in one
   channel of an existing texture.

   glTF has no bump map: an image plugged into Blender's Bump node — or a
   greyscale plugged into Normal Map — is dropped by the exporter without a
   word. What does survive is the roughness, packed into the green channel of
   metallicRoughnessTexture. When a modeller paints ONE greyscale for both
   jobs, the relief is therefore still in the file; it only has to be read
   back out as slopes.

   Sobel on the height, then n = (-k·dh/du, -k·dh/dv, 1). Rows run DOWN the
   image while v runs up, hence +dh/dy for the green channel: this is the
   OpenGL convention a glTF normal map uses. The texture keeps the source's
   flipY (false for glTF), and the caller must negate normalScale.y exactly as
   GLTFLoader does for a model without tangents — three then builds the frame
   from screen-space derivatives of the UVs.

   Computed once per texture, on the CPU, rather than as a bump map in the
   shader: a bump map differentiates the height per pixel quad, which
   shimmers on a moving hull, while a normal map gets mipmapped like any
   other image. */
Naval.normalFromHeight = function(src, strength, channel){
  const img = src && src.image;
  const W = img && (img.width || img.videoWidth), H = img && (img.height || img.videoHeight);
  if(!W || !H) return null;
  const c = document.createElement('canvas');
  c.width = W; c.height = H;
  const g = c.getContext('2d', { willReadFrequently:true });
  g.drawImage(img, 0, 0);
  const px = g.getImageData(0, 0, W, H);
  const d = px.data, ch = channel == null ? 1 : channel;

  const h = new Float32Array(W*H);
  for(let i = 0; i < W*H; i++) h[i] = d[i*4 + ch]/255;

  // Sobel's weights sum to 8 per side; k/8 keeps `strength` a plain slope gain
  const k = strength/8;
  for(let y = 0; y < H; y++){
    const ym = (y > 0 ? y-1 : y)*W, y0 = y*W, yp = (y < H-1 ? y+1 : y)*W;
    for(let x = 0; x < W; x++){
      const xm = x > 0 ? x-1 : x, xp = x < W-1 ? x+1 : x;
      const dx = (h[ym+xp] + 2*h[y0+xp] + h[yp+xp]) - (h[ym+xm] + 2*h[y0+xm] + h[yp+xm]);
      const dy = (h[yp+xm] + 2*h[yp+x] + h[yp+xp]) - (h[ym+xm] + 2*h[ym+x] + h[ym+xp]);
      const nx = -dx*k, ny = dy*k;
      const inv = 1/Math.sqrt(nx*nx + ny*ny + 1);
      const o = (y0 + x)*4;
      d[o]   = (nx*inv*0.5 + 0.5)*255;
      d[o+1] = (ny*inv*0.5 + 0.5)*255;
      d[o+2] = (inv*0.5 + 0.5)*255;
      d[o+3] = 255;
    }
  }
  g.putImageData(px, 0, 0);

  const t = new THREE.CanvasTexture(c);
  t.flipY = src.flipY;
  t.wrapS = src.wrapS; t.wrapT = src.wrapT;
  t.channel = src.channel;
  t.anisotropy = src.anisotropy;
  t.colorSpace = THREE.NoColorSpace;     // directions, not colours
  /* The canvas is only a way to get the pixels to the GPU. Kept, it would hold
     another twelve megabytes for the life of the ship, for nothing: three
     uploads once and never reads the image again unless someone bumps the
     texture's version, which nothing here does. So once the upload has
     happened, the backing store is shrunk away and the reference dropped. */
  t.onUpdate = () => {
    t.onUpdate = null;
    c.width = c.height = 0;
    t.image = null;
  };
  t.needsUpdate = true;
  return t;
};

/* How many separate wounds one hull can show at once. */
Naval.SCAR_MAX = 24;

/* PAINTED IMPACTS, gathered into ONE texture.

   The sheet gives a list of variants, each a list of stages from a light graze
   to a torn-open wound (`appearance.impactMaps`). GLSL ES 1.0 cannot index an
   array of samplers, so they are laid out on a canvas, a row per variant and a
   column per stage, and the shader picks its cell by arithmetic — the same way
   the hull profiles share one texture a row per ship. Built once per list and
   shared by every hull that names it.

   Straight alpha, sRGB: they are artwork. Not flipped, so row 0 is the top of
   the canvas and the shader turns each cell upright itself. Until every image
   is in, `ready` is pending and the hulls keep their drawn strokes. */
Naval.impactAtlas = function(list){
  Naval._impactAtlas = Naval._impactAtlas || {};
  const key = JSON.stringify(list);
  if(Naval._impactAtlas[key]) return Naval._impactAtlas[key];
  const variants = list.length;
  const stages = Math.max(...list.map(v => v.length));
  const CELL = 512;
  const cv = document.createElement('canvas');
  cv.width = CELL*stages; cv.height = CELL*variants;
  const tex = new THREE.CanvasTexture(cv);
  tex.flipY = false;
  tex.colorSpace = THREE.SRGBColorSpace;
  tex.anisotropy = 4;
  const load = src => new Promise(res => {
    const im = new Image();
    im.onload = () => res(im);
    im.onerror = () => { console.warn('[impacts] image introuvable : ' + String(src).slice(0, 80)); res(null); };
    im.src = src;
  });
  const ready = Promise.all(list.map((v, vi) => Promise.all(v.map((src, si) =>
    load(src).then(im => ({ im, vi, si }))))))
    .then(rows => {
      const g = cv.getContext('2d');
      let n = 0;
      for(const row of rows) for(const c of row){
        if(!c.im) continue;
        g.drawImage(c.im, c.si*CELL, c.vi*CELL, CELL, CELL);
        n++;
      }
      /* A variant with fewer stages than the others repeats its last one, so
         a shallow list never shows an empty cell at the deep end. */
      list.forEach((v, vi) => {
        for(let si = v.length; si < stages; si++)
          g.drawImage(cv, (v.length - 1)*CELL, vi*CELL, CELL, CELL, si*CELL, vi*CELL, CELL, CELL);
      });
      tex.needsUpdate = true;
      return n > 0;
    });
  return (Naval._impactAtlas[key] = { tex, stages, variants, ready });
};

/* THE WOUNDS, laid over her own materials rather than painted into a texture.

   Painting into her UVs would need a UV at the point of impact — a ray cast
   against the mesh at every hit — and a procedural hull has no UVs at all. So
   the scars are a short list of points in HER frame, and each fragment asks how
   near it is to one: the model's own texture stays untouched, and any hull,
   modelled or built, takes them the same way.

   GOUGES, NOT STAINS. A ball glancing along oak does not scorch it, it rips
   the weathered face off in long pale tears that run WITH the grain — and a
   round dark patch with a ring of flecks round it read as paint thrown at her,
   which is how the first cut was judged, with a photomontage of what was wanted
   instead. So each scar draws a handful of thin strokes of raw wood across the
   planking: mostly along the hull, each a few degrees off the last, jagged
   along its length and tapering at both ends, with a faint dark lip where the
   groove shades. More strokes, and longer, the more often the bay was hit.

   The strokes are laid in the plane of the side they are on — along and up the
   side for a scar on the flank, across and up for one on the transom — which
   is read off the scar's own position in her frame. The colour goes into the
   diffuse term, so it is LIT: fresh oak in the sun, a pale thread at night.

   Chained like every other patch, with its own cache key, and before the haze. */
/* SNOW LYING ON HER. Where a surface faces the sky, its colour goes to snow
   by the depth of the coat: first the flats (deck, tops, the upper faces of
   yards and rails), then the gentler slopes. The edge is broken by noise laid
   in HER frame, so the patches stay where they fell as she rolls. A sail,
   hanging, collects almost none by the same rule. Chained with its own key,
   before the haze. */
Naval.applySnowCover = function(mat, u){
  if(!mat || mat.userData.snowCover || !mat.isMeshStandardMaterial) return;
  mat.userData.snowCover = true;
  const prevKey = mat.customProgramCacheKey.bind(mat);
  mat.customProgramCacheKey = () => prevKey() + '|snow';
  const prev = mat.onBeforeCompile;
  mat.onBeforeCompile = (shader, renderer)=>{
    if(prev) prev(shader, renderer);
    shader.uniforms.uSnow = u.uSnow;
    shader.vertexShader = 'varying vec3 vSnowN;\nvarying vec3 vSnowP;\n'
      + shader.vertexShader.replace('#include <project_vertex>',
        '#include <project_vertex>\n  vSnowN = normalize(mat3(modelMatrix) * objectNormal);\n  vSnowP = transformed;');
    shader.fragmentShader = 'varying vec3 vSnowN;\nvarying vec3 vSnowP;\nuniform float uSnow;\n'
      + 'float navalSnowHash(vec3 p){ return fract(sin(dot(p, vec3(12.9898, 78.233, 37.719)))*43758.5453); }\n'
      + 'float navalSnowNoise(vec3 p){ vec3 i = floor(p), f = fract(p); f = f*f*(3.0-2.0*f);\n'
      + '  float a = mix(mix(navalSnowHash(i), navalSnowHash(i+vec3(1,0,0)), f.x), mix(navalSnowHash(i+vec3(0,1,0)), navalSnowHash(i+vec3(1,1,0)), f.x), f.y);\n'
      + '  float b = mix(mix(navalSnowHash(i+vec3(0,0,1)), navalSnowHash(i+vec3(1,0,1)), f.x), mix(navalSnowHash(i+vec3(0,1,1)), navalSnowHash(i+vec3(1,1,1)), f.x), f.y);\n'
      + '  return mix(a, b, f.z); }\n'
      + shader.fragmentShader.replace('#include <color_fragment>',
        '#include <color_fragment>\n'
      + '  if(uSnow > 0.001){\n'
      + '    float upF = normalize(vSnowN).y;\n'
      + '    float n = navalSnowNoise(vSnowP*1.7)*0.6 + navalSnowNoise(vSnowP*5.3)*0.4;\n'
      + '    // the deeper the coat, the steeper the slopes it holds on\n'
      + '    float reach = mix(0.92, 0.45, uSnow);\n'
      + '    float s = smoothstep(reach, reach + 0.2, upF + (n - 0.5)*0.35) * smoothstep(0.0, 0.35, uSnow + n*0.3 - 0.15);\n'
      + '    diffuseColor.rgb = mix(diffuseColor.rgb, vec3(0.90, 0.92, 0.95), clamp(s, 0.0, 1.0)*0.95);\n'
      + '  }');
  };
  mat.needsUpdate = true;
};

/* The hanging lantern's shadow: how far it reaches (the cabin, no more — the
   cube map's six renders only draw what is inside it). */
Naval.LANTERN_SHADOW = { range: 4, down: new THREE.Vector3(0, -1, 0) };

Naval.applyScars = function(mat, u){
  if(!mat || mat.userData.scars || !mat.isMeshStandardMaterial) return;
  mat.userData.scars = true;
  const prevKey = mat.customProgramCacheKey.bind(mat);
  mat.customProgramCacheKey = () => prevKey() + '|scars';
  const prev = mat.onBeforeCompile;
  mat.onBeforeCompile = (shader, renderer)=>{
    if(prev) prev(shader, renderer);
    shader.uniforms.uScar = u.uScar;
    shader.uniforms.uScarCount = u.uScarCount;
    shader.uniforms.uScarInv = u.uScarInv;
    shader.uniforms.uScarK = u.uScarK;
    shader.uniforms.uScarTex = u.uScarTex;
    shader.uniforms.uScarGrid = u.uScarGrid;
    shader.uniforms.uScarMaps = u.uScarMaps;
    shader.vertexShader = 'uniform mat4 uScarInv;\nvarying vec3 vScarP;\n'
      + shader.vertexShader.replace('#include <project_vertex>',
          '#include <project_vertex>\n  vScarP = (uScarInv * modelMatrix * vec4(transformed, 1.0)).xyz;');
    shader.fragmentShader =
        '#define NSCAR ' + Naval.SCAR_MAX + '\n'
      + 'uniform vec4 uScar[NSCAR];\nuniform int uScarCount;\nuniform float uScarK;\nvarying vec3 vScarP;\n'
      + 'uniform sampler2D uScarTex;\nuniform vec2 uScarGrid;\nuniform float uScarMaps;\n'
      + 'float scarHash(vec3 p){ return fract(sin(dot(p, vec3(12.9898, 78.233, 37.719)))*43758.5453); }\n'
      + shader.fragmentShader
        .replace('#include <map_fragment>', [
          '#include <map_fragment>',
          '{ float gRaw = 0.0, gLip = 0.0;',
          '  for(int i = 0; i < NSCAR; i++){',
          '    if(i < uScarCount){',
          /* THE FIRST BALL SHOWS, a graze, and the scar worsens toward an open wound
             by a weight of about seven (a full-calibre ball weighs 2, a half-calibre 1).
             It used to show only past 2: the Roter Löwe's half-calibre guns had to hit
             three times within a metre, and six hits left nothing (reported). */
          '      float e = clamp((uScar[i].w - 0.5)/6.0, 0.0, 1.0);',
          '      vec3 c = uScar[i].xyz;',
          '      float R = (0.45 + 0.75*e)*uScarK;',
          // the side it is on: the flank runs along z, the transom across x
          '      bool flank = abs(c.x) > 0.18*(abs(c.z) + 1.0);',
          '      vec2 q = flank ? vec2(vScarP.z - c.z, vScarP.y - c.y) : vec2(vScarP.x - c.x, vScarP.y - c.y);',
          '      float sw = uScar[i].w;',
          /* PAINTED: one variant per scar, a few degrees off the grain, its own
             size and handedness, all from hashes of where it is — so it is the
             same mark every frame. Stage 1 from the first ball, stage 2 near a
             weight of 4, stage 3 near 6.5, cross-faded in between. */
          '      if(uScarMaps > 0.5 && sw > 0.5){',
          '        float h1 = scarHash(c*1.7 + vec3(1.0)), h2 = scarHash(c*2.9 + vec3(2.0)), h3 = scarHash(c*3.7 + vec3(3.0));',
          '        float S = (1.1 + 0.5*h1)*uScarK;',
          '        float ang = (h2 - 0.5)*0.45;',
          '        vec2 dir = vec2(cos(ang), sin(ang)), nrm = vec2(-dir.y, dir.x);',
          '        vec2 uv = vec2(dot(q, dir), dot(q, nrm))/S + 0.5;',
          '        if(h3 > 0.5) uv.x = 1.0 - uv.x;',
          '        if(uv.x > 0.0 && uv.x < 1.0 && uv.y > 0.0 && uv.y < 1.0){',
          '          float variant = floor(h1*uScarGrid.y*0.999);',
          '          float st = clamp((sw - 1.5)/2.5, 0.0, uScarGrid.x - 1.0);',
          '          float s0 = floor(st), s1 = min(s0 + 1.0, uScarGrid.x - 1.0), tt = st - s0;',
          '          vec2 cell = 1.0/uScarGrid;',
          // upright in its cell, and kept two texels off the cell border
          '          vec2 inset = vec2(uv.x, 1.0 - uv.y)*(1.0 - 4.0/512.0) + 2.0/512.0;',
          '          vec4 T = mix(texture2D(uScarTex, (vec2(s0, variant) + inset)*cell),',
          '                       texture2D(uScarTex, (vec2(s1, variant) + inset)*cell), tt);',
          '          diffuseColor.rgb = mix(diffuseColor.rgb, T.rgb, T.a*clamp(sw, 0.0, 1.0));',
          '        }',
          '      }',
          '      if(uScarMaps < 0.5 && e > 0.0 && distance(vScarP, c) < R){',
          '        float strokes = 1.0 + floor(e*4.0);',
          '        for(int j = 0; j < 5; j++){',
          '          if(float(j) < strokes){',
          '            float fj = float(j);',
          '            float h1 = scarHash(c*1.7 + vec3(fj*13.1, fj*7.3, 1.0));',
          '            float h2 = scarHash(c*2.3 + vec3(fj*5.9, 3.0, fj*11.7));',
          '            float h3 = scarHash(c*3.1 + vec3(2.0, fj*17.3, fj*3.7));',
          '            float ang = (h1 - 0.5)*0.7;',                     // mostly with the grain
          '            vec2 dir = vec2(cos(ang), sin(ang));',
          '            vec2 nrm = vec2(-dir.y, dir.x);',
          '            vec2 o = nrm*(h2 - 0.5)*0.55*R + dir*(h3 - 0.5)*0.4*R;',
          '            float len = (0.45 + 0.45*h3)*R;',
          '            float u = dot(q - o, dir);',
          '            float v = dot(q - o, nrm) + 0.018*uScarK*sin(u*23.0/uScarK + h2*6.28);',   // a torn, jagged line
          '            float t = abs(u)/len;',
          '            if(t < 1.0){',
          '              float w = 0.04*uScarK*(1.0 - t*t)*(0.7 + 0.6*h1);',    // tapering at both ends; wider than true so it holds a pixel
          '              float a = min(1.0, 0.45 + e);',
          '              gRaw = max(gRaw, (1.0 - smoothstep(0.35*w, w, abs(v)))*a);',
          '              gLip = max(gLip, (1.0 - smoothstep(w, 2.6*w, abs(v)))*a*0.35);',
          '            }',
          '          }',
          '        }',
          '      }',
          '    }',
          '  }',
          '  diffuseColor.rgb *= 1.0 - gLip;',                              // the groove's shadow
          '  diffuseColor.rgb = mix(diffuseColor.rgb, vec3(0.46, 0.34, 0.21), gRaw);',   // fresh oak
          '}'].join('\n'));
  };
  mat.needsUpdate = true;
};

Naval.loadGLTFLoader = async function(){
  if(Naval._GLTFLoader) return Naval._GLTFLoader;
  // Resolved through the import map in the page, so the loader and its "three"
  // dependency agree on one version.
  const mod = await import('three/addons/loaders/GLTFLoader.js');
  Naval._GLTFLoader = mod.GLTFLoader;
  return Naval._GLTFLoader;
};
