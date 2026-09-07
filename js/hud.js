/* The bridge instruments, and the sea-state console on the right.
   Reads the solver; never writes to it, except through the sea-state controls. */
window.Naval = window.Naval || {};

Naval.HUD = class HUD {
  constructor(ocean, physics, controls, cameraRig, spec, stage){
    this.C = Naval.Config;
    this.ocean = ocean; this.physics = physics;
    this.ctrl = controls.state; this.cam = cameraRig;
    this.spec = spec;

    this.stage = stage;
    this.eqY = 0;
    this.accum = 0;

    const el = id => document.getElementById(id);
    this.el = {
      heading:el('roHeading'), speed:el('roSpeed'), draft:el('roDraft'),
      heave:el('roHeave'), submerged:el('roSubmerged'), gm:el('roGM'),
      roll:el('roRoll'), pitch:el('roPitch'), horizon:el('hzGroup'),
      thrText:el('thrText'), thrBar:el('thrBar'), thrTele:el('thrTele'),
      rudText:el('rudText'), rudBar:el('rudBar'), rudTele:el('rudTele'),
      shtText:el('shtText'), shtBar:el('shtBar'), shtTele:el('shtTele'),
      shtOpt:el('shtOpt'),
      wind:el('roWind'), appWind:el('roAppWind'), point:el('roPoint'),
      dot:el('statusDot'),
      seaState:el('seaState'), windDir:el('windDir'),
      seaVal:el('seaVal'), windVal:el('windVal'),
      beauNum:el('beauNum'), beauDesc:el('beauDesc'),
      shipName:el('shipName'), shipSel:el('shipSel'),
      tonnage:el('roTonnage'), dims:el('roDims'),
      sunElev:el('sunElev'), sunVal:el('sunVal'),
      swell:el('swell'), swellVal:el('swellVal'),
    };

    this._e = new THREE.Euler();
    this._hdg = new THREE.Vector3();

    this.el.seaState.addEventListener('input', ()=> this.refreshSea());
    this.el.windDir .addEventListener('input', ()=> this.refreshSea());
    if(this.el.sunElev){
      this.el.sunElev.addEventListener('input', ()=> this.refreshSun());
    }
    if(this.el.swell){
      this.el.swell.addEventListener('input', ()=>{
        this.ocean.swell = parseFloat(this.el.swell.value);
        this.el.swellVal.textContent = this.ocean.swell.toFixed(2)+'×';
        this.refreshSea();                 // rebuild the spectrum at the new scale
      });
    }
    document.querySelectorAll('.presets button').forEach(btn=>{
      btn.addEventListener('click', ()=>{
        this.el.seaState.value = btn.dataset.s;
        this.refreshSea();
      });
    });
  }

  /* Point the instruments at a different vessel. Her identity, tonnage and
     principal dimensions are stated on the panel, because they are what makes
     her handle the way she does. */
  setSpec(spec, physics){
    this.spec = spec;
    if(physics) this.physics = physics;
    if(this.el.shipName) this.el.shipName.textContent = spec.name;
    if(this.el.tonnage)  this.el.tonnage.textContent = Math.round(spec.tonnes);
    if(this.el.dims){
      this.el.dims.textContent = spec.L.toFixed(0)+' × '+spec.B.toFixed(1)+' m';
    }
  }

  /* Drop the sun toward the horizon and its reflection stretches into the long
     glitter road; raise it and that road collapses to a patch beside the hull.
     It is the same microfacet term either way — only the geometry changes. */
  refreshSun(){
    if(!this.stage || !this.el.sunElev) return;
    const e = parseFloat(this.el.sunElev.value);
    this.stage.setSun(e, this.stage.sunBearing);
    this.el.sunVal.textContent = e < 0 ? 'nuit '+Math.round(e)+'°' : Math.round(e)+'°';
  }

  refreshSea(){
    const s = parseFloat(this.el.seaState.value);
    const w = parseInt(this.el.windDir.value);
    this.ocean.setSeaState(s, w);
    this.el.seaVal.textContent = s.toFixed(1);
    this.el.windVal.textContent = String(w).padStart(3,'0')+'°';
    const b = this.C.BEAUFORT[Math.max(0, Math.min(9, Math.round(s)))];
    this.el.beauNum.textContent = b[0];
    this.el.beauDesc.textContent = b[1]+' · '+b[2]+' m';
    document.querySelectorAll('.presets button').forEach(btn=>{
      btn.classList.toggle('on', Math.abs(parseFloat(btn.dataset.s)-s) < 0.05);
    });
  }

  // Point of sail, from the apparent wind angle off the bow.
  pointOfSail(deg, luffing){
    if(!this.ctrl.sailsSet) return 'voiles ferlées';
    if(deg < 32)  return 'vent debout';
    if(luffing)   return 'voiles qui faseyent';
    if(deg < 60)  return 'au près';
    if(deg < 100) return 'vent de travers';
    if(deg < 150) return 'au largue';
    return 'vent arrière';
  }

  update(dt){
    this.accum += dt;
    if(this.accum < 0.06) return;
    this.accum = 0;

    const C = this.C, e = this.el, p = this.physics, b = p.body, ctrl = this.ctrl;

    this._e.setFromQuaternion(b.quat, 'YXZ');
    /* Compass heading straight off the bow vector: +z is north, +x is east, so
       a turn to starboard raises the reading. (Taking it from the Euler angle
       got the sign wrong, and silently cancelled an error in the old rudder
       model — the two faults hid each other.) */
    this._hdg.set(0,0,1).applyQuaternion(b.quat);
    const headingDeg = ((Math.atan2(this._hdg.x, this._hdg.z)*180/Math.PI)%360+360)%360;
    const roll = this._e.z*180/Math.PI;
    const pitch = this._e.x*180/Math.PI;

    e.heading.textContent = String(Math.round(headingDeg)).padStart(3,'0');
    // speed over ground: horizontal component only, so heave doesn't inflate it
    e.speed.textContent = (Math.hypot(b.vel.x, b.vel.z)*C.MS_TO_KN).toFixed(1);
    e.draft.textContent = p.draft.toFixed(2);
    e.heave.textContent = (b.pos.y - this.eqY).toFixed(2);
    e.submerged.textContent = Math.round(p.submergedFrac*100);
    // indicative GM: lower CoG and wider beam ⇒ stiffer
    const S = this.spec;
    const gm = (S.B*S.B)/(12*(S.D*0.46)) - (b.com.y + S.deckMid);
    e.gm.textContent = Math.max(0, gm).toFixed(1);

    e.roll.textContent = roll.toFixed(1)+'°';
    e.pitch.textContent = pitch.toFixed(1)+'°';
    e.horizon.setAttribute('transform', `rotate(${-roll}) translate(0 ${pitch*1.6})`);

    // --- engine telegraph ---
    const tp = Math.round(ctrl.throttle*100);
    e.thrText.textContent = tp===0 ? 'STOP' : (tp>0 ? '+'+tp+'%' : tp+'%');
    e.thrBar.style.left = ctrl.throttle>=0 ? '50%' : (50+ctrl.throttle*50)+'%';
    e.thrBar.style.width = Math.abs(ctrl.throttle)*50+'%';
    e.thrBar.style.background = ctrl.throttle<0 ? 'var(--warn)' : 'var(--accent)';
    e.thrTele.textContent = tp===0 ? '— chadburn au repos —'
        : tp>0 ? (tp>66?'EN AVANT TOUTE':tp>33?'en avant demie':'en avant lente')
               : (tp<-33?'EN ARRIÈRE':'en arrière lente');

    // --- helm ---
    const rd = Math.round(ctrl.rudder*35);
    e.rudText.textContent = rd+'°';
    e.rudBar.style.left = ctrl.rudder>=0 ? '50%' : (50+ctrl.rudder*50)+'%';
    e.rudBar.style.width = Math.abs(ctrl.rudder)*50+'%';
    e.rudTele.textContent = rd===0 ? '— gouvernail droit —'
        : (rd>0 ? 'la barre à tribord' : 'la barre à bâbord');

    // --- wind & sails ---
    const appDeg = p.appWindAngle*180/Math.PI;
    e.wind.textContent = Math.round(this.ocean.windSpeed*C.MS_TO_KN);
    e.appWind.textContent = Math.round(appDeg) + (p.tack>0 ? ' tb' : ' bd');
    e.point.textContent = this.pointOfSail(appDeg, p.luffing);

    /* The sheet bar carries a mark at the trim that would drive her hardest on
       her present heading. Without it a beginner reads "voiles établies", sees
       a plausible number of kilonewtons, and never learns that several times
       that force was one keypress away (measured at 7.4 kN against 41.8 kN on
       the frigate close-hauled) — the model gives no other feedback. */
    const opt = p.optSheet;
    const known = opt !== null && ctrl.sailsSet;
    e.shtOpt.hidden = !known;
    if(known) e.shtOpt.style.left = (opt/S.maxSheet)*100+'%';

    e.shtText.textContent = ctrl.sailsSet ? Math.round(ctrl.sheet*180/Math.PI)+'°' : 'FERLÉ';
    e.shtBar.style.left = '0%';
    e.shtBar.style.width = (ctrl.sailsSet ? (ctrl.sheet/S.maxSheet)*100 : 0)+'%';
    e.shtBar.style.background = p.luffing ? 'var(--warn)' : 'var(--accent)';

    // positive = eased further than she wants, so the order is to sheet in
    const dTrim = known ? ctrl.sheet - opt : 0;
    e.shtTele.textContent = !ctrl.sailsSet ? '— voiles ferlées —'
        : p.luffing ? 'ça faseye — border !'
        : !known ? '— pas de vent —'
        : Math.abs(dTrim) < 0.05 ? 'au mieux · '+Math.round(p.sailDrive/1000)+' kN'
        : dTrim > 0 ? 'trop choquées — border'
                    : 'trop bordées — choquer';

    this.cam.refreshLabel(b);

    // status lamp by how far she is over
    const sev = Math.abs(roll);
    const col = sev>28 ? 'var(--crit)' : sev>16 ? 'var(--warn)' : 'var(--good)';
    e.dot.style.background = col;
    e.dot.style.boxShadow = '0 0 8px '+col;
  }
};
