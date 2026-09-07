/* Four ways to watch her, and the mouse handling that goes with each.

   0 Poursuite  — trails astern, lagging, falling back as she gathers way
   1 Orbite     — free orbit about the hull
   2 Passerelle — at the wheel; aims in ship-local coords so the view heels with the deck
   3 Fixe       — planted in the world, position AND bearing locked; she sails out of shot */
window.Naval = window.Naval || {};

Naval.CameraRig = class CameraRig {
  constructor(camera, canvas, btn, note, spec){
    this.C = Naval.Config;
    this.camera = camera;
    this.btn = btn;
    this.note = note;
    this.mode = 0;
    this.setSpec(spec);

    this.orbitYaw = 2.4; this.orbitPitch = 0.32; this.orbitDist = this.cam.orbitDist;
    this.fixedYaw = 0; this.fixedPitch = 0;      // trainable by dragging
    this.bridgeYaw = 0; this.bridgePitch = 0;    // where the helmsman looks
    this.camHeading = 0;                          // lagged, so turns swing

    this.anchor = new THREE.Vector3();
    this.fixedTgt = new THREE.Vector3();
    this.pos = new THREE.Vector3(0,16,44);
    this.tgt = new THREE.Vector3();
    this._fwd = new THREE.Vector3();
    this._aR = new THREE.Vector3(); this._aF = new THREE.Vector3();
    this._desired = new THREE.Vector3();

    let dragging=false, px=0, py=0;
    canvas.addEventListener('pointerdown', e=>{ dragging=true; px=e.clientX; py=e.clientY; });
    addEventListener('pointerup', ()=> dragging=false);
    addEventListener('pointermove', e=>{
      if(!dragging) return;
      const dx=e.clientX-px, dy=e.clientY-py;
      if(this.mode===3){                     // train the locked camera by hand
        this.fixedYaw -= dx*0.004;
        this.fixedPitch = Math.max(-0.9, Math.min(0.9, this.fixedPitch - dy*0.004));
      }else if(this.mode===2){               // look around from the wheel
        this.bridgeYaw -= dx*0.004;
        this.bridgePitch = Math.max(-0.7, Math.min(0.7, this.bridgePitch - dy*0.004));
      }else{
        this.orbitYaw -= dx*0.006;
        this.orbitPitch = Math.max(-0.15, Math.min(1.2, this.orbitPitch + dy*0.006));
      }
      px=e.clientX; py=e.clientY;
    });
    canvas.addEventListener('wheel', e=>{
      if(this.mode===2 || this.mode===3){    // zoom the lens, keeping the camera put
        camera.fov = Math.max(12, Math.min(75, camera.fov + e.deltaY*0.02));
        camera.updateProjectionMatrix();
      }else{
        this.orbitDist = Math.max(16, Math.min(140, this.orbitDist + e.deltaY*0.04));
      }
      e.preventDefault();
    }, {passive:false});

    if(btn) btn.addEventListener('click', ()=> this.cycle());
  }

  /* Every viewing distance is stated per vessel, so a 60 m frigate is filmed
     from proportionally further off than a 24 m schooner. */
  setSpec(spec){
    this.spec = spec;
    this.cam = spec.camera;
    this.scale = spec.L / 24;
    this.orbitDist = this.cam.orbitDist;
  }

  cycle(){
    this.mode = (this.mode+1) % this.C.CAM_NAMES.length;
    if(this.mode===3) this.plant();
    else if(this.mode===2){ this.bridgeYaw=0; this.bridgePitch=0; }
    if(this.mode<2 && this.camera.fov!==55){
      this.camera.fov=55; this.camera.updateProjectionMatrix();
    }
    if(this.btn) this.btn.textContent = 'Caméra : ' + this.C.CAM_NAMES[this.mode];
    if(this.note){
      this.note.hidden = (this.mode<2);   // respond at once, not on the next HUD tick
      this.note.textContent = (this.mode===3)
        ? 'Glisser — orienter · Molette — zoom · X — replanter ici'
        : 'Glisser — regarder autour · Molette — zoom';
    }
  }

  /* Plant off her starboard quarter: she then draws away diagonally and
     shrinks, instead of crossing the frame and sliding straight out of it. */
  plant(){
    const b = this.body, ocean = this.ocean, t = this.time;
    if(!b) return;
    const k = this.scale;
    this._aR.set(1,0,0).applyQuaternion(b.quat);
    this._aF.set(0,0,1).applyQuaternion(b.quat);
    this.anchor.copy(b.pos).addScaledVector(this._aR, 26*k).addScaledVector(this._aF, -34*k);
    this.anchor.y = ocean.sample(this.anchor.x, this.anchor.z, t) + 12*k;
    // train it on her once; from then on it holds unless the user drags
    const dx = b.pos.x - this.anchor.x, dz = b.pos.z - this.anchor.z;
    const dy = (b.pos.y + 2*k) - this.anchor.y;
    this.fixedYaw = Math.atan2(dx, dz);
    this.fixedPitch = Math.atan2(dy, Math.hypot(dx, dz));
    this.pos.copy(this.anchor);            // snap, so the attitude is steady at once
  }

  update(dt, body, ocean, t){
    const spec = this.spec, k = this.scale;
    this.body = body; this.ocean = ocean; this.time = t;   // for plant()/replant

    this.tgt.copy(body.pos); this.tgt.y += 1.5*k;
    this._fwd.set(0,0,1).applyQuaternion(body.quat);
    const heading = Math.atan2(this._fwd.x, this._fwd.z);
    const desired = this._desired;

    if(this.mode===0){                     // chase astern, lagged
      let dh = heading - this.camHeading;
      dh = Math.atan2(Math.sin(dh), Math.cos(dh));         // the short way round
      this.camHeading += dh * Math.min(1, 1-Math.pow(0.25, dt));
      const spd = Math.hypot(body.vel.x, body.vel.z);
      const dist = this.cam.chaseDist + spd*1.8*k;         // pulls back with speed
      const high = this.cam.chaseHigh + spd*0.45*k;
      desired.set(Math.sin(this.camHeading+Math.PI)*dist, high,
                  Math.cos(this.camHeading+Math.PI)*dist).add(body.pos);
      desired.y = Math.max(desired.y, ocean.sample(desired.x, desired.z, t)+5*k);
      this.tgt.addScaledVector(this._fwd, 10*k).y += 1.5*k; // look past the bow
    }else if(this.mode===1){               // orbit
      const cp = Math.cos(this.orbitPitch);
      desired.set(Math.sin(this.orbitYaw)*this.orbitDist*cp,
                  Math.sin(this.orbitPitch)*this.orbitDist + 8*k,
                  Math.cos(this.orbitYaw)*this.orbitDist*cp).add(body.pos);
    }else if(this.mode===2){               // at the wheel
      // right aft, and clear of the main boom sweeping overhead
      /* Eye height comes from the spec, in metres above the design waterline —
         the way you would actually state it. Specs written before the field
         existed fall back to the old hardcoded rule, so none of them break. */
      const eyeY = (this.cam.helmHeight != null)
                 ? this.cam.helmHeight
                 : spec.deckMid + 2.1*k;
      const eye = new THREE.Vector3(0, eyeY, spec.L*this.cam.helmZFrac)
                    .applyQuaternion(body.quat).add(body.pos);
      desired.copy(eye);
      const cp = Math.cos(this.bridgePitch);
      this.tgt.set(Math.sin(this.bridgeYaw)*cp, Math.sin(this.bridgePitch), Math.cos(this.bridgeYaw)*cp)
              .applyQuaternion(body.quat).multiplyScalar(120*k).add(eye);
    }else{                                 // fixed vantage
      desired.copy(this.anchor);
      const cp = Math.cos(this.fixedPitch), reach = 200*k;
      this.fixedTgt.set(
        this.anchor.x + Math.sin(this.fixedYaw)*cp*reach,
        this.anchor.y + Math.sin(this.fixedPitch)*reach,
        this.anchor.z + Math.cos(this.fixedYaw)*cp*reach
      );
      this.tgt.copy(this.fixedTgt);
    }

    // Soft spring, so acceleration and turns are felt. Bridge and fixed views
    // are rigid — no easing, no drift.
    const lerp = (this.mode===2 || this.mode===3) ? 1 : 1-Math.pow(0.12, dt);
    this.pos.lerp(desired, Math.min(1, lerp));
    this.camera.position.copy(this.pos);
    this.camera.lookAt(this.tgt);
  }

  // How far she has run from the planted viewpoint (shown on the button).
  refreshLabel(body){
    if(this.mode===3 && this.btn){
      this.btn.textContent = 'Caméra : Fixe · ' + Math.round(body.pos.distanceTo(this.anchor)) + ' m';
    }
  }
};
