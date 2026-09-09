/* Four ways to watch her, and the mouse handling that goes with each.

   0 Proue      — planted ahead on her course; she comes on, and passes
   1 Orbite     — free orbit about the hull
   2 Passerelle — at the wheel; aims in ship-local coords so the view heels with the deck
   3 Fixe       — planted off her quarter; she draws away and out of shot

   Proue replaced a chase camera that trailed astern, lagging and falling back
   as she gathered way. Astern is the one bearing from which a square-rigger
   shows least of herself: the sails are edge-on or hidden behind one another,
   and the wake — the thing the view existed to show — is the part of her that
   moves least. From ahead she presents her whole sail plan, her bow wave and
   her heel, and every one of those answers to the helm.

   Proue and Fixe are the SAME camera and share every line of it: planted in
   the world, position and bearing locked, trained on her once and then left
   alone. Only the station differs — ahead on her course, or off her quarter —
   and that one difference is worth two entries in the menu, since a vessel
   coming at you and a vessel leaving you are not the same shot. Writing them
   as two cameras would have meant keeping two sets of drag handling, two zooms
   and two rebases in step, for no gain whatever. */
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
    this.fixedYaw = 0; this.fixedPitch = 0;      // trainable by dragging, both planted views
    /* A planted camera that has never been planted would sit at the world
       origin staring at nothing. Proue is the view she STARTS on, so the first
       frame has to plant it — there is no cycle() to do it. */
    this._planted = false;
    this.bridgeYaw = 0; this.bridgePitch = 0;    // where the helmsman looks

    this.anchor = new THREE.Vector3();
    this.fixedTgt = new THREE.Vector3();
    this.pos = new THREE.Vector3(0,16,44);
    this.tgt = new THREE.Vector3();
    this._aR = new THREE.Vector3(); this._aF = new THREE.Vector3();
    this._desired = new THREE.Vector3();

    let dragging=false, px=0, py=0;
    canvas.addEventListener('pointerdown', e=>{ dragging=true; px=e.clientX; py=e.clientY; });
    addEventListener('pointerup', ()=> dragging=false);
    addEventListener('pointermove', e=>{
      if(!dragging) return;
      const dx=e.clientX-px, dy=e.clientY-py;
      if(this.mode===0 || this.mode===3){    // train a planted camera by hand
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
      if(this.mode !== 1){                   // zoom the lens, keeping the camera put
        camera.fov = Math.max(12, Math.min(75, camera.fov + e.deltaY*0.02));
        camera.updateProjectionMatrix();
      }else{
        this.orbitDist = Math.max(16, Math.min(140, this.orbitDist + e.deltaY*0.04));
      }
      e.preventDefault();
    }, {passive:false});

    if(btn) btn.addEventListener('click', ()=> this.cycle());

    /* Say out loud which camera she starts on, rather than trusting the markup
       to agree with this file. The button's label and the drag hint both live
       in setMode, so a mode reached by assignment instead of by call arrives
       with last session's caption. */
    this.setMode(0);
  }

  /* Every viewing distance is stated per vessel, so a 60 m frigate is filmed
     from proportionally further off than a 24 m schooner. */
  setSpec(spec){
    this.spec = spec;
    this.cam = spec.camera;
    this.scale = spec.L / 24;
    this.orbitDist = this.cam.orbitDist;
    /* The bow view borrows the chase distance every spec already states, so no
       ship file has to be touched and each vessel is still filmed from
       proportionally as far off. It sits LOWER than the chase did — a camera
       looking back at a bow wants to be near the water, where the sail plan
       stands against the sky instead of being looked down upon. A spec may
       state its own figures when one of them deserves better. */
    this.bowDist = (this.cam.bowDist != null) ? this.cam.bowDist : this.cam.chaseDist;
    this.bowHigh = (this.cam.bowHigh != null) ? this.cam.bowHigh : this.cam.chaseHigh*0.55;
    // a new vessel is not where the old one was: take the station again
    this._planted = false;
  }

  cycle(){ this.setMode(this.mode + 1); }

  /* Go to a named camera rather than stepping to the next one. Losing a ship
     switches to the fixed view, and that must not depend on which camera the
     user happened to be on when she went. */
  setMode(m, aimY){
    const n = this.C.CAM_NAMES.length;
    this.mode = ((m % n) + n) % n;
    if(this.mode===0 || this.mode===3) this.plant(aimY);
    else if(this.mode===2){ this.bridgeYaw=0; this.bridgePitch=0; }
    /* Only the orbit has no zoom of its own, so only the orbit gets the lens
       put back. It used to be "the first two modes", which was true when the
       first of them was a chase camera and is not now. */
    if(this.mode===1 && this.camera.fov!==55){
      this.camera.fov=55; this.camera.updateProjectionMatrix();
    }
    if(this.btn) this.btn.textContent = 'Caméra : ' + this.C.CAM_NAMES[this.mode];
    if(this.note){
      this.note.hidden = (this.mode===1);  // respond at once, not on the next HUD tick
      this.note.textContent = (this.mode===0 || this.mode===3)
        ? 'Glisser — orienter · Molette — zoom · X — replanter ici'
        : 'Glisser — regarder autour · Molette — zoom';
    }
  }

  /* Take a station and hold it. The station is the ONLY thing that separates
     the two planted views:

     · Proue stands ahead on her present course, so she comes on bows-first and
       passes. A little off the line rather than dead on it — she would
       otherwise run the camera down, and passing to one side is what opens her
       from bow-on to broadside, which is the whole of the shot.
     · Fixe stands off her starboard quarter, so she draws away diagonally and
       shrinks, instead of crossing the frame and sliding straight out of it.

     aimY says WHAT height to train on. It defaults to the vessel; when she is
     lost the caller passes the sea surface instead, because trained on the
     wreck the camera would pitch steeply down at a hundred metres of empty
     water rather than hold the patch of sea she went through. */
  plant(aimY){
    const b = this.body, ocean = this.ocean, t = this.time;
    if(!b) return;
    const k = this.scale;
    this._aR.set(1,0,0).applyQuaternion(b.quat);
    this._aF.set(0,0,1).applyQuaternion(b.quat);
    if(this.mode===0){
      this.anchor.copy(b.pos)
        .addScaledVector(this._aF, this.bowDist)
        .addScaledVector(this._aR, this.bowDist*0.16);
      this.anchor.y = ocean.sample(this.anchor.x, this.anchor.z, t) + this.bowHigh;
    }else{
      this.anchor.copy(b.pos).addScaledVector(this._aR, 26*k).addScaledVector(this._aF, -34*k);
      this.anchor.y = ocean.sample(this.anchor.x, this.anchor.z, t) + 12*k;
    }
    // train it on her once; from then on it holds unless the user drags
    const dx = b.pos.x - this.anchor.x, dz = b.pos.z - this.anchor.z;
    const dy = (aimY != null ? aimY : b.pos.y + 2*k) - this.anchor.y;
    this.fixedYaw = Math.atan2(dx, dz);
    this.fixedPitch = Math.atan2(dy, Math.hypot(dx, dz));
    this.pos.copy(this.anchor);            // snap, so the attitude is steady at once
    this._planted = true;
  }

  /* Every position this rig holds is in the world that just moved. The planted
     camera especially: it is the one thing deliberately NOT following the ship,
     so it is the one that would be left a thousand metres adrift. */
  rebase(dx, dz){
    for(const v of [this.anchor, this.pos, this.tgt, this.fixedTgt]){
      v.x -= dx; v.z -= dz;
    }
  }

  update(dt, body, ocean, t){
    const spec = this.spec, k = this.scale;
    this.body = body; this.ocean = ocean; this.time = t;   // for plant()/replant

    this.tgt.copy(body.pos); this.tgt.y += 1.5*k;
    const desired = this._desired;

    // she starts on a planted camera, so the first frame is where it gets planted
    if(!this._planted && (this.mode===0 || this.mode===3)) this.plant();

    if(this.mode===1){                     // orbit
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
    }else{                                 // a planted vantage — Proue or Fixe
      desired.copy(this.anchor);
      const cp = Math.cos(this.fixedPitch), reach = 200*k;
      this.fixedTgt.set(
        this.anchor.x + Math.sin(this.fixedYaw)*cp*reach,
        this.anchor.y + Math.sin(this.fixedPitch)*reach,
        this.anchor.z + Math.cos(this.fixedYaw)*cp*reach
      );
      this.tgt.copy(this.fixedTgt);
    }

    /* Soft spring on the orbit, so acceleration and turns are felt. Every
       other view is rigid — no easing, no drift. The bow view carries its lag
       in its HEADING already, and a second one on top of it would make the
       station wallow behind the turn it is already lagging. */
    const lerp = (this.mode===1) ? 1-Math.pow(0.12, dt) : 1;
    this.pos.lerp(desired, Math.min(1, lerp));
    this.camera.position.copy(this.pos);
    this.camera.lookAt(this.tgt);
  }

  /* How far she has run from the planted viewpoint (shown on the button).
     Both planted views want it: on the bow camera it counts DOWN as she comes
     on, which is the more useful of the two readings. */
  refreshLabel(body){
    if((this.mode===0 || this.mode===3) && this.btn){
      this.btn.textContent = 'Caméra : ' + this.C.CAM_NAMES[this.mode] + ' · '
                           + Math.round(body.pos.distanceTo(this.anchor)) + ' m';
    }
  }
};
