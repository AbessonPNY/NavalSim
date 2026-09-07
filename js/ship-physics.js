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
  settle(ocean, ctrl){
    ocean.setSeaState(0, 0);
    this.body.pos.set(0, 0.4, 0);
    const steps = Math.round(1200 * Math.sqrt(this.spec.L/24));
    let t = 0;
    for(let i=0;i<steps;i++){ this.step(1/120, ocean, ctrl, t); t += 1/120; }
    this.body.vel.set(0,0,0);
    this.body.angVel.set(0,0,0);
    return this.body.pos.y;
  }
};
