/* Six-degree-of-freedom rigid body floating by Archimedes' principle.

   The hull volume is filled with a uniform grid of probes. Each probe below the
   local water surface contributes ρ·g·V upward at its own position; the sum of
   those forces and their moments about the centre of gravity yields heave,
   pitch, roll and metacentric stability for free — none of it scripted.

   On top of that sit the engine, the rudder (a force at the rudder post, never
   a pure couple) and the sails (aerofoils in the apparent wind).

   Every dimension and coefficient comes from the ShipSpec, so the same solver
   carries a 115-tonne schooner and a 1600-tonne frigate without alteration. */
window.Naval = window.Naval || {};

/* The one aerofoil every sail is cut from.

   These numbers are consumed TWICE — once to build the coefficients that make
   the force, once in the closed-form best trim derived from those very
   coefficients (see optimalAoA). Written out inline in both places they would
   eventually drift apart, and the console would then mark a trim the sails do
   not actually want. One definition, two readers. */
Naval.SAIL_FOIL = { KL:1.5, CD0:0.08, KD:1.2 };

Naval.ShipPhysics = class ShipPhysics {
  constructor(spec, lines){
    const C = Naval.Config;
    this.C = C;
    this.spec = spec;
    this.lines = lines;

    this.probes = [];
    this.hullVolume = 0;
    this._buildProbes();
    spec.checkFlotation(this.hullVolume, C.RHO);
    this._buildCompartments();

    /* Flooding, by the added-weight method: water aboard is weight at the place
       it actually lies. Nothing about sinking is scripted — she founders when
       that weight beats what her hull can displace, and she capsizes first if it
       lies badly, which is what usually really happens. */
    this.breaches = [];
    this.floodVol = 0;                     // m³ aboard, all compartments
    this.floodTonnes = 0;
    this.floodRate = 0;                    // m³/s net, + = gaining on the pumps
    this.freeSurfaceRise = 0;              // metres of virtual rise of G
    this.aground = 0;                      // metres her keel is INTO the ground
    this.world = null;                     // set by the page; without it she never touches
    this.foundered = false;
    this.pumpOn = true;
    /* Pumps are sized so that ONE modest hole is just beatable and two are not
       — that is the whole tension, and it has to be measured rather than
       guessed, the inflow depending on how deep she settles onto the hole.
       Scaled off her volume, so a schooner's pumps are not a frigate's.

       Frankly generous against history: a chain pump did something like a ton a
       minute, and real ships foundered because pumping could not keep up. This
       is roughly five of them, which is what makes damage control a decision
       rather than a formality. */
    this.pumpRate = this.hullVolume * 6.0e-5;   // m³/s
    /* Strength of the free surface effect, 1 being the textbook correction.
       Named rather than buried, because it is the one term that decides
       whether she capsizes or merely founders — and setting it to 0 is the
       only way to prove which of the two killed her in a given run. */
    this.freeSurface = 1.0;

    this.body = {
      pos: new THREE.Vector3(0,0,0),
      vel: new THREE.Vector3(),
      quat: new THREE.Quaternion(),
      angVel: new THREE.Vector3(),           // world frame
      mass: spec.massKg,                     // the stated displacement
      // CoG low in the ballast → stiff and self-righting; set aft to sit on the
      // centre of buoyancy so she floats on her designed trim, not nose-down.
      com: new THREE.Vector3(spec.cog.x, spec.cog.y, spec.cog.z),
      Ib: new THREE.Vector3(),               // principal inertia, body frame
    };
    const m = this.body.mass, L = spec.L, B = spec.B, D = spec.D;
    this.body.Ib.set(
      m/12*(D*D + L*L),      // about x (pitch)
      m/12*(B*B + L*L),      // about y (yaw)
      m/12*(B*B + D*D)       // about z (roll)
    );

    // telemetry for the instruments
    this.submergedFrac = 0; this.draft = 0;
    this.appWindAngle = 0; this.appWindSpeed = 0;
    this.tack = 1; this.sailDrive = 0; this.luffing = false;
    this.optSheet = null;                  // null while there is no wind to trim to
    this.sailLoad = 0;                     // Pa on the cloth — what fills or empties it

    // scratch vectors — allocated once, never reused across a live value
    this._fwd=new THREE.Vector3(); this._right=new THREE.Vector3(); this._up=new THREE.Vector3();
    this._pw=new THREE.Vector3(); this._r=new THREE.Vector3(); this._tmp=new THREE.Vector3();
    this._cog=new THREE.Vector3(); this._tmp2=new THREE.Vector3(); this._norm=new THREE.Vector3();
    this._torque=new THREE.Vector3(); this._force=new THREE.Vector3();
    this._qc=new THREE.Quaternion();
    this._app=new THREE.Vector3(); this._lift=new THREE.Vector3(); this._sailF=new THREE.Vector3();
    this._arm=new THREE.Vector3(); this._fVec=new THREE.Vector3(); this._mom=new THREE.Vector3();
    this._ce=new THREE.Vector3(0, spec.ceHeight, spec.ceZ);
    this._dryCom = this.body.com.clone();
    this._down=new THREE.Vector3(); this._wc=new THREE.Vector3();
    this._com=new THREE.Vector3();
  }

  /* Divide her into compartments along her length, out of the probes that
     already describe her volume — so a compartment's capacity is real hull
     volume, measured on the same plan of forms as everything else. */
  _buildCompartments(){
    const n = this.C.NCOMP, L = this.spec.L;
    this.comps = [];
    for(let i=0;i<n;i++) this.comps.push({
      vol:0, cap:0, mid:new THREE.Vector3(),
      halfB:0, deckY:-Infinity, keelY:Infinity
    });
    for(const pr of this.probes){
      const i = Math.min(n-1, Math.max(0,
                  Math.floor(((pr.local.z + L/2)/L)*n)));
      const c = this.comps[i];
      c.cap += pr.vol;
      c.mid.addScaledVector(pr.local, pr.vol);
      c.halfB = Math.max(c.halfB, Math.abs(pr.local.x));
      c.deckY = Math.max(c.deckY, pr.local.y);
      c.keelY = Math.min(c.keelY, pr.local.y);
    }
    for(const c of this.comps) if(c.cap > 0) c.mid.multiplyScalar(1/c.cap);
  }

  /* Open a hole. Area in m², at a height given as a fraction of the
     compartment's depth — 0 at the keel, 1 at the deck. Below the waterline is
     what matters: a hole above it admits nothing until she settles onto it. */
  breach(index, area, heightFrac){
    const c = this.comps[index];
    if(!c || c.cap <= 0) return null;
    const h = heightFrac == null ? 0.25 : heightFrac;
    const br = { comp:index, area:area || 0.35,
                 y: c.keelY + (c.deckY - c.keelY)*h, z:c.mid.z };
    this.breaches.push(br);
    return br;
  }

  /* Water in, water out, and where it lies.

     Inflow is Torricelli through the hole: v = √(2gh), so a hole deep under
     water fills far faster than one near the surface — and as she settles the
     head grows, which is exactly the runaway that drowns a ship. Once a
     compartment's deck edge goes under she downfloods as well, through hatches
     and gunports rather than through the hole, and that is what usually
     finishes it rather than the breach itself. */
  _flooding(dt, ocean, t){
    const C = this.C, b = this.body;
    this.floodRate = 0;
    if(!this.breaches.length && this.floodVol <= 1e-6) return;
    const before = this.floodVol;

    for(const br of this.breaches){
      const c = this.comps[br.comp];
      if(c.vol >= c.cap) continue;
      this._pw.set(0, br.y, br.z).applyQuaternion(b.quat).add(b.pos);
      const head = ocean.sample(this._pw.x, this._pw.z, t) - this._pw.y;
      if(head <= 0) continue;
      c.vol = Math.min(c.cap, c.vol + 0.62*br.area*Math.sqrt(2*C.G*head)*dt);
    }

    for(const c of this.comps){
      if(c.vol >= c.cap) continue;
      // deck edge under: she is taking it green, through every opening at once
      this._pw.set(0, c.deckY, c.mid.z).applyQuaternion(b.quat).add(b.pos);
      const over = ocean.sample(this._pw.x, this._pw.z, t) - this._pw.y;
      if(over > 0) c.vol = Math.min(c.cap, c.vol + 0.25*c.halfB*Math.sqrt(2*C.G*over)*dt);
    }

    if(this.pumpOn){
      // the pumps draw on the fullest compartment first, as a crew would
      let left = this.pumpRate*dt;
      while(left > 1e-9){
        let worst = null;
        for(const c of this.comps) if(c.vol > 1e-9 && (!worst || c.vol > worst.vol)) worst = c;
        if(!worst) break;
        const take = Math.min(left, worst.vol);
        worst.vol -= take; left -= take;
      }
    }

    this._updateMass();
    // net m³/s: positive means the sea is winning, and that is the one number
    // that says whether the situation is under control
    this.floodRate = dt > 0 ? (this.floodVol - before)/dt : 0;
  }

  /* Mass, centre of gravity and inertia, with the water she has aboard.

     Two things fall out of this that are worth not undoing. Water lies at the
     BOTTOM of a compartment, so its centre rises as the compartment fills —
     which means a little water in the bilges is ballast and stiffens her, while
     a lot of it high up is what capsizes her. And a PARTLY full compartment has
     a free surface: the water runs to the low side and stays there, so it fights
     every attempt to right herself. A full compartment cannot do that, which is
     why counter-flooding to fill a space completely is a real remedy. */
  _updateMass(){
    const C = this.C, S = this.spec, b = this.body;
    let vol = 0;
    for(const c of this.comps) vol += c.vol;
    this.floodVol = vol;
    this.floodTonnes = vol*C.RHO/1000;

    const wm = vol*C.RHO;
    b.mass = S.massKg + wm;

    if(wm < 1e-6){ b.com.copy(this._dryCom); }
    else{
      // which way is down, in her own frame — this carries heel AND trim
      this._down.set(0,-1,0).applyQuaternion(this._qc.copy(b.quat).invert());
      const lat = this._down.x, lon = this._down.z;

      this._com.copy(this._dryCom).multiplyScalar(S.massKg);
      for(const c of this.comps){
        if(c.vol <= 1e-9) continue;
        const f = c.vol/c.cap;
        const free = 4*f*(1-f)*this.freeSurface;   // nil when empty or brimful
        this._wc.set(
          c.mid.x + lat*free*c.halfB,
          c.keelY + 0.5*f*(c.deckY - c.keelY),     // it lies in the bottom
          c.mid.z + lon*free*(c.deckY - c.keelY)*0.67
        );
        this._com.addScaledVector(this._wc, c.vol*C.RHO);
      }
      b.com.copy(this._com).multiplyScalar(1/b.mass);

      /* FREE SURFACE, properly this time.

         Loose water in a partly filled compartment does not merely lean to
         leeward — it destroys stability outright, and the textbook correction
         is a VIRTUAL RISE of the centre of gravity by Σ(ρ·i)/Δ, where i is the
         second moment of the free surface area, l·b³/12. It does not depend on
         the angle of heel at all, which is exactly what makes it lethal: she is
         already unstable before she has leaned an inch. And it goes as the CUBE
         of the breadth, so one wide compartment is worse than three narrow ones
         holding the same water — the reason real ships are subdivided
         lengthwise.

         Written at first as a shift of the water's centroid toward the low
         side. That is real, but second order: measured on the schooner it moved
         the centre of gravity two centimetres and made five tonne-metres,
         against a righting moment two orders larger, while the water lying in
         her bilges lowered G by 23 cm and stiffened her. She flooded, grew
         STEADIER, and foundered bolt upright. The centroid shift is kept, but
         it is this term that decides whether she goes over. */
      let fsm = 0;
      const cl = S.L/this.comps.length;
      for(const c of this.comps){
        if(c.vol <= 1e-9 || c.vol >= c.cap*0.995) continue;   // brimful: no surface
        const bw = 2*c.halfB;
        fsm += C.RHO * cl*bw*bw*bw/12;
      }
      this.freeSurfaceRise = this.freeSurface * fsm / b.mass;
      b.com.y += this.freeSurfaceRise;
    }

    const m = b.mass, L = S.L, B = S.B, D = S.D;
    b.Ib.set(m/12*(D*D + L*L), m/12*(B*B + L*L), m/12*(B*B + D*D));
  }

  /* One hole, and it works itself worse.

     A hull does not spring five tidy leaks at once: a seam starts, and the sea
     opens it. Each call widens the same wound, and once it is past about a
     plank's width it reaches into the compartment next door — which is what
     eventually carries her past what any one compartment could hold. */
  worsenBreach(){
    if(!this.breaches.length) return this.breach(1, 0.05, 0.30);
    const br = this.breaches[0];
    br.area *= 2.0;
    // wide enough to have gone through a bulkhead: let it into the next space
    const spread = Math.floor(Math.log2(br.area/0.05));
    for(let k=1; k<=spread && k<this.comps.length; k++){
      const i = br.comp + (k%2 ? k : -k);
      if(i < 0 || i >= this.comps.length) continue;
      if(this.breaches.some(o => o.comp === i)) continue;
      this.breach(i, br.area*0.4, 0.30);
    }
    return br;
  }

  /* She takes the ground.

     The seabed is sampled at THREE points along her keel — stem, midships and
     sternpost — never at every probe. `heightAt` scans the island grid, and
     calling it three hundred times a substep would cost more than the whole
     solver put together. Three points are enough for everything that matters:
     she strands by the bow on a shelving beach, pivots on a shoal that catches
     her amidships, or sits down on an even keel.

     The bottom answers as a stiff spring with heavy damping, applied AT the
     point of contact — so she lifts, heels and slews exactly as the geometry
     dictates. Nothing here decides she is aground; the forces do, the same way
     nothing decides she floats. */
  _ground(dt, force, torque, cog, ocean){
    this.aground = 0;
    if(!this.world || !ocean) return;
    const C = this.C, S = this.spec, b = this.body, O = ocean.origin;
    const keel = -(S.hull.keelDepth + S.hull.keelExtra);
    // supports her whole weight at a third of a metre of penetration
    const kSpring = b.mass*C.G/(0.33*3);

    this._hardAgo = Math.max(0, (this._hardAgo || 0) - dt);
    const spd = Math.hypot(b.vel.x, b.vel.z);

    const stations = [0.42, 0.0, -0.45];
    for(let s=0;s<stations.length;s++){
      const f = stations[s];
      this._pw.set(0, keel, f*S.L).applyQuaternion(b.quat).add(b.pos);
      const bed = this.world.heightAt(O.x + this._pw.x, O.z + this._pw.z);
      const pen = bed - this._pw.y;
      if(pen <= 0) continue;
      this.aground = Math.max(this.aground, pen);

      this._r.copy(this._pw).sub(cog);
      // velocity of this very point, so the damping fights the real motion
      this._tmp.copy(b.angVel).cross(this._r).add(b.vel);

      const up = kSpring*Math.min(pen, 2.5) - this._tmp.y*b.mass*1.2;
      const fy = Math.max(0, up);
      force.y += fy;
      torque.x += -this._r.z * fy;
      torque.z +=  this._r.x * fy;

      // and she drags: sand and rock hold a hull far harder than water does
      this._fVec.set(-this._tmp.x, 0, -this._tmp.z).multiplyScalar(b.mass*0.9);
      force.add(this._fVec);
      torque.add(this._mom.crossVectors(this._r, this._fVec));

      /* Driven on at speed she opens. A hull does not bounce off rock, and the
         hole is where she struck — so running aground finally becomes a real
         cause of the flooding that was already written. */
      if(spd > 2.2 && this._hardAgo <= 0){
        const comp = Math.min(this.comps.length-1, Math.max(0,
                       Math.floor(((f*S.L + S.L/2)/S.L)*this.comps.length)));
        this.breach(comp, Math.min(0.45, 0.06*(spd - 2.0)), 0.06);
        this._hardAgo = 5;              // she cannot be holed twice in a breath
      }
    }
  }

  /* The magazine goes up: her bottom is opened from end to end at once.

     Not a special sinking path — the same flooding as any other, with every
     compartment holed low and wide and the pumps blown to pieces with the rest.
     She goes down in under a minute because the arithmetic says so, not because
     anything here decides she should. */
  blowUp(){
    this.breaches.length = 0;
    for(let i=0;i<this.comps.length;i++) this.breach(i, 2.4, 0.05);
    this.pumpOn = false;
    /* And she is already open to the sea: an explosion does not politely start
       her filling from empty. A fifth of her volume goes in with the blast. */
    for(const c of this.comps) c.vol = Math.max(c.vol, c.cap*0.20);
    this._updateMass();
  }

  /* Pump her dry and plug every hole — what "réparer" means from the console. */
  salvage(){
    this.breaches.length = 0;
    for(const c of this.comps) c.vol = 0;
    this.foundered = false;
    this._updateMass();
  }

  /* Fill the hull envelope with a UNIFORM 3-D grid and keep the cells that land
     inside it. Every cell carries the same volume, so the cloud reproduces the
     hull's true volume distribution — flotation then follows from the shape
     rather than from how the samples happened to be spaced. The grid is refined
     with length so a large vessel is not sampled more coarsely than a small one. */
  _buildProbes(){
    const C = this.C, spec = this.spec, lines = this.lines;
    const L = spec.L, B = spec.B, D = spec.D;
    const nz = C.PN_Z, nx = C.PN_X, ny = C.PN_Y;
    this.probes = []; this.hullVolume = 0;
    const cellVol = (L/nz)*(B/nx)*(D/ny);
    const gridBase = -(spec.hull.keelDepth + spec.hull.keelExtra);
    this.probeH = D / ny;                   // smoothing scale for partial immersion

    for(let iz=0;iz<nz;iz++){
      const tz = (iz+0.5)/nz;
      const z = -L/2 + tz*L;
      const halfB = lines.halfB(tz), deckY = lines.deckY(tz), keelY = lines.keelY(tz);
      if(halfB < 0.05*B/5.8) continue;      // skip a station that has no width
      for(let iy=0;iy<ny;iy++){
        const y = gridBase + ((iy+0.5)/ny)*D;
        if(y < keelY || y > deckY) continue;
        const s = (deckY - y)/(deckY - keelY);
        const beam = halfB * lines.beamFactor(s);
        for(let ix=0;ix<nx;ix++){
          const x = -B/2 + ((ix+0.5)/nx)*B;
          if(Math.abs(x) > beam) continue;
          this.probes.push({local:new THREE.Vector3(x,y,z), vol:cellVol});
          this.hullVolume += cellVol;
        }
      }
    }
  }

  step(dt, ocean, ctrl, t){
    const C = this.C, S = this.spec, b = this.body;
    const fwd=this._fwd, right=this._right, up=this._up;
    const force=this._force, torque=this._torque;

    fwd.set(0,0,1).applyQuaternion(b.quat);
    right.set(1,0,0).applyQuaternion(b.quat);
    up.set(0,1,0).applyQuaternion(b.quat);

    // Water in and out FIRST: it sets the mass and the centre of gravity that
    // everything below — gravity, moments, inertia — is then taken against.
    this._flooding(dt, ocean, t);

    force.set(0,0,0); torque.set(0,0,0);
    force.y -= b.mass * C.G;                    // gravity at the CoG makes no moment

    // world CoG — its own vector, because _tmp is reused inside the probe loop
    const cog = this._cog.copy(b.com).applyQuaternion(b.quat).add(b.pos);

    // --- buoyancy + vertical damping over the submerged probes ---
    let submergedVol = 0, lowestY = Infinity;
    for(let i=0;i<this.probes.length;i++){
      const pr = this.probes[i];
      this._pw.copy(pr.local).applyQuaternion(b.quat).add(b.pos);
      if(this._pw.y < lowestY) lowestY = this._pw.y;
      const depth = ocean.sample(this._pw.x, this._pw.z, t, this._norm) - this._pw.y;
      if(depth <= 0) continue;
      const dispVol = pr.vol * Math.min(1, depth / this.probeH);
      submergedVol += dispVol;

      this._r.copy(this._pw).sub(cog);                   // lever arm from the CoG
      const fb = C.RHO * C.G * dispVol;                  // Archimedes, straight up

      // velocity of this point = v + ω × r ; damp its vertical component
      this._tmp.copy(b.angVel).cross(this._r).add(b.vel);
      const dragF = -this._tmp.y * dispVol * S.heaveDamp;

      const fy = fb + dragF;
      force.y += fy;
      torque.x += -this._r.z * fy;                       // (r × (0,fy,0)).x
      torque.z +=  this._r.x * fy;                       // (r × (0,fy,0)).z
    }
    this.submergedFrac = submergedVol / this.hullVolume;
    this.draft = Math.max(0, ocean.sample(cog.x, cog.z, t) - lowestY);

    // how far under the sea she now lies — the measure by which she is lost
    this.depthBelow = ocean.sample(b.pos.x, b.pos.z, t) - b.pos.y;

    /* How much of her is still working the surface. The collar, the bow wave
       and the wake all belong to a hull CUTTING the water; once she is under
       they have to go, or a ring of foam is left riding over the wreck with
       nothing beneath it. */
    const u = Math.min(1, Math.max(0, (this.submergedFrac - 0.78)/(0.95 - 0.78)));
    this.afloat = 1 - u*u*(3 - 2*u);

    /* Foundered when she stays under, not the instant a sea buries her: a
       boarding wave puts the deck under for a second on any hard day. */
    this._underFor = this.submergedFrac > 0.95 ? (this._underFor||0) + dt : 0;
    if(this._underFor > 3) this.foundered = true;

    const inWater = submergedVol > 0 ? 1 : 0;
    const vFwd = b.vel.dot(fwd);
    const vRight = b.vel.dot(right);

    // --- engine ---
    force.addScaledVector(fwd, ctrl.throttle * S.maxThrust * inWater);

    // --- hull resistance: quadratic ahead; the deep ballast keel grips hard
    //     sideways, which is what limits leeway when she is pressed ---
    force.addScaledVector(fwd, -Math.sign(vFwd)*vFwd*vFwd*S.drag*inWater);
    force.addScaledVector(right,
      (-vRight*Math.abs(vRight)*S.lateralQuad - vRight*S.lateralLinear) * inWater);

    /* --- rudder ---
       A transverse force at the rudder post, NOT a pure couple. Applying it
       where the rudder actually is — right aft — is what makes her swing about
       a point forward of amidships (roughly a quarter of her length abaft the
       stem), the way a vessel making headway really behaves. It reverses
       correctly under sternway too. */
    const delta = ctrl.rudder * S.rudderMax;
    const rudderF = -S.rudderK * vFwd*Math.abs(vFwd) * Math.sin(delta) * inWater;
    this._fVec.copy(right).multiplyScalar(rudderF);
    force.add(this._fVec);
    this._arm.set(0, S.rudderY, S.rudderZ).applyQuaternion(b.quat).add(b.pos).sub(cog);
    torque.add(this._mom.crossVectors(this._arm, this._fVec));

    this._sails(ctrl, ocean, cog, force, torque, fwd, right);
    this._ground(dt, force, torque, cog, ocean);

    // --- integrate linear ---
    b.vel.addScaledVector(force, dt/b.mass);
    b.vel.multiplyScalar(1 - 0.02*dt);                  // faint global damping
    if(b.vel.length() > 40) b.vel.setLength(40);        // safety clamp
    b.pos.addScaledVector(b.vel, dt);

    // --- integrate angular, in the body frame where inertia is diagonal ---
    this._qc.copy(b.quat).invert();
    const Tb = this._tmp.copy(torque).applyQuaternion(this._qc);
    Tb.set(Tb.x/b.Ib.x, Tb.y/b.Ib.y, Tb.z/b.Ib.z);
    Tb.applyQuaternion(b.quat);
    b.angVel.addScaledVector(Tb, dt);

    /* Anisotropic damping: hold roll and pitch down hard for stability, but let
       her yaw freely so the rudder can actually turn her. An isotropic damper
       strangles the turn. */
    const wy = b.angVel.dot(up);
    this._tmp2.copy(up).multiplyScalar(wy);
    b.angVel.sub(this._tmp2)
            .multiplyScalar(1 - 3.0*dt)
            .addScaledVector(up, wy*(1 - 0.5*dt));
    if(b.angVel.length() > 4) b.angVel.setLength(4);

    const wlen = b.angVel.length();
    if(wlen > 1e-8){
      this._tmp.copy(b.angVel).multiplyScalar(1/wlen);
      this._qc.setFromAxisAngle(this._tmp, wlen*dt);
      b.quat.premultiply(this._qc).normalize();
    }

    // NaN guard: recover to a safe upright state rather than freeze the loop
    if(!Number.isFinite(b.pos.x+b.pos.y+b.pos.z+b.vel.x+b.vel.y+b.vel.z+b.quat.x)){
      b.pos.set(b.pos.x||0, 0.4, b.pos.z||0);
      b.vel.set(0,0,0); b.angVel.set(0,0,0);
      b.quat.identity();
    }
  }

  /* The angle of attack that drives her hardest for a given apparent wind angle.

     Drive ∝ CL·sin β − CD·cos β : lift pulls across the wind, so it helps most
     when the wind is abeam, while drag pushes downwind and only helps once she
     is past a beam reach. Substituting the foil above and setting the
     derivative to zero collapses to

         tan 2α = 2·KL·sin β / (KD·cos β)

     — closed form, no search and no lookup table. CD0 falls out, being no
     function of α. It lands where a seaman would put it: hard in on the wind
     (β = 45° → sheets at 11°), and square across the ship before it
     (β = 180° → 90°), passing through 45° on a beam reach. */
  static optimalAoA(beta){
    const F = Naval.SAIL_FOIL;
    return 0.5*Math.atan2(2*F.KL*Math.sin(beta), F.KD*Math.cos(beta));
  }

  /* Apparent wind = true wind seen from a moving deck. The sails are treated as
     aerofoils: lift across the apparent wind, drag along it, both growing with
     the square of the apparent speed. The force acts at the centre of effort,
     high above the waterline — which is exactly why she heels. */
  _sails(ctrl, ocean, cog, force, torque, fwd, right){
    const C = this.C, S = this.spec, b = this.body, F = Naval.SAIL_FOIL;
    this.sailDrive = 0; this.luffing = false; this.optSheet = null;
    this.sailLoad = 0;

    this._app.copy(ocean.windVec).sub(b.vel); this._app.y = 0;
    const vApp = this.appWindSpeed = this._app.length();
    if(vApp <= 0.25) return;

    const fromFwd   = -(this._app.x*fwd.x   + this._app.z*fwd.z)   / vApp;
    const fromRight = -(this._app.x*right.x + this._app.z*right.z) / vApp;
    const beta = Math.atan2(Math.abs(fromRight), fromFwd);   // 0 = head to wind
    this.appWindAngle = beta;
    this.tack = fromRight >= 0 ? 1 : -1;                     // +1 = wind on the starboard bow

    /* Where the sheets ought to be on this heading, for the mark on the console.
       Clamped to what her rig can actually do: a square-rigger cannot brace as
       far round as a boomed gaff sail swings, so before the wind the mark sits
       at her stop rather than at an angle she can never reach. */
    this.optSheet = Math.max(0, Math.min(S.maxSheet,
                      beta - Naval.ShipPhysics.optimalAoA(beta)));

    const aoa = beta - ctrl.sheet;
    if(!ctrl.sailsSet) return;
    if(aoa <= 0.02){ this.luffing = true; return; }          // over-eased, or in irons

    const CL = F.KL*Math.sin(2*aoa);
    const CD = F.CD0 + F.KD*Math.sin(aoa)*Math.sin(aoa);
    const q  = 0.5*C.RHO_AIR*vApp*vApp*S.sailArea;

    this._sailF.copy(this._app).multiplyScalar(CD*q/vApp);   // drag along the wind
    this._lift.set(this._app.z, 0, -this._app.x).normalize();
    if(this._lift.dot(fwd) < 0) this._lift.negate();         // lift drives her forward
    this._sailF.addScaledVector(this._lift, CL*q);
    force.add(this._sailF);

    this._arm.copy(this._ce).applyQuaternion(b.quat).add(b.pos).sub(cog);
    torque.add(this._mom.crossVectors(this._arm, this._sailF));
    this.sailDrive = this._sailF.dot(fwd);
    // pressure on the canvas, which is what makes it belly out
    this.sailLoad = this._sailF.length() / S.sailArea;
  }

  /* Let her find her own flotation in FLAT water, so the recorded equilibrium
     height is exact. A heavy ship has a longer heave period, so the settling
     time is scaled by the square root of her length. */
  /* Find her flotation in a flat calm, then PUT THE SEA BACK as it was.

     Flattening it is necessary — she has to settle on her lines without a swell
     throwing her about — but leaving it flat is a trap. It cost a bug: adding a
     vessel from the fleet panel flattened the sea for good, the console still
     showing force 6 and a full swell over a millpond. commission() happened to
     hide it by calling refreshSea() straight after, so the fault only surfaced
     through the second caller. Restoring it here means no caller has to know. */
  settle(ocean, ctrl){
    const sea = ocean.seaState, deg = ocean.windDeg;
    ocean.setSeaState(0, 0);
    this.body.pos.set(0, 0.4, 0);
    const steps = Math.round(1200 * Math.sqrt(this.spec.L/24));
    let t = 0;
    for(let i=0;i<steps;i++){ this.step(1/120, ocean, ctrl, t); t += 1/120; }
    this.body.vel.set(0,0,0);
    this.body.angVel.set(0,0,0);
    if(sea != null) ocean.setSeaState(sea, deg);
    return this.body.pos.y;
  }
};
