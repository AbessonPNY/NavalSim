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
      flood:el('roFlood'), pumps:el('roPumps'),
      damageRow:el('damageRow'), damage:el('roDamage'),
      wind:el('roWind'), appWind:el('roAppWind'), point:el('roPoint'),
      dot:el('statusDot'),
      seaState:el('seaState'), windDir:el('windDir'),
      seaVal:el('seaVal'), windVal:el('windVal'),
      beauNum:el('beauNum'), beauDesc:el('beauDesc'),
      roseCard:el('roseCard'), roseDeg:el('roseDeg'), roseRhumb:el('roseRhumb'),
      shipName:el('shipName'), shipSel:el('shipSel'),
      tonnage:el('roTonnage'), dims:el('roDims'),
      sunElev:el('sunElev'), sunVal:el('sunVal'),
      swell:el('swell'), swellVal:el('swellVal'),
      cloud:el('cloud'), cloudVal:el('cloudVal'),
    };

    this._e = new THREE.Euler();
    this._hdg = new THREE.Vector3();
    this._buildRose();

    this.el.seaState.addEventListener('input', ()=> this.refreshSea());
    this.el.windDir .addEventListener('input', ()=> this.refreshSea());
    if(this.el.sunElev){
      this.el.sunElev.addEventListener('input', ()=> this.refreshSun());
    }
    /* Cloud is weather like the rest, and deliberately NOT tied to the sea
       state — the same reason the haze is not. A gale that also shut the sky
       would take the fine day away from the one place it is worth having. */
    if(this.el.cloud && this.stage && this.stage.skyUniforms){
      const setCloud = ()=>{
        const v = parseFloat(this.el.cloud.value)/100;
        this.stage.skyUniforms.uCloud.value = v;
        this.el.cloudVal.textContent = Math.round(v*100)+'%';
        this.stage.refreshEnvironment();    // the sky lights the ship, so it must follow
      };
      this.el.cloud.addEventListener('input', setCloud);
      setCloud();
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
    this.showSun(e);
  }

  /* The reading alone. The day cycle drives the stage directly — it has a
     bearing to set as well as an elevation, which no single slider can carry —
     and asks only that the console say so. */
  showSun(e){
    if(!this.el.sunVal) return;
    this.el.sunVal.textContent = e < 0 ? 'nuit '+Math.round(e)+'°' : Math.round(e)+'°';
  }

  /* Draw the compass card once: ticks every ten degrees, the four points
     lettered, and a needle. Built here rather than written out in the markup
     because thirty-six ticks of hand-written SVG is thirty-six chances to get
     one wrong. */
  _buildRose(){
    const g = this.el.roseCard;
    if(!g) return;
    const NS = 'http://www.w3.org/2000/svg';
    const add = (tag, attrs, text) => {
      const n = document.createElementNS(NS, tag);
      for(const k in attrs) n.setAttribute(k, attrs[k]);
      if(text != null) n.textContent = text;
      g.appendChild(n);
      return n;
    };
    for(let d=0; d<360; d+=10){
      const maj = (d % 30) === 0;
      const r = Math.PI*d/180, s = Math.sin(r), c = Math.cos(r);
      // 0° at the top, clockwise, as a compass is lettered
      const r0 = maj ? 68 : 74, r1 = 82;
      add('line', { x1:(s*r0).toFixed(2), y1:(-c*r0).toFixed(2),
                    x2:(s*r1).toFixed(2), y2:(-c*r1).toFixed(2),
                    class:'tick' + (maj ? ' maj' : '') });
    }
    const pts = [['N',0,'card n'], ['E',90,'card'], ['S',180,'card'], ['O',270,'card']];
    for(const [txt, d, cls] of pts){
      const r = Math.PI*d/180;
      add('text', { x:(Math.sin(r)*52).toFixed(2), y:(-Math.cos(r)*52).toFixed(2),
                    class:cls }, txt);
    }
    /* No needle. On a card compass the CARD is the magnet — a needle drawn on
       top of it is both wrong and, with the heading read out in the middle,
       simply clutter across the figures. The red N carries north on its own. */
    add('path', { d:'M0 -62 L6 -50 L-6 -50 Z', class:'needle' });
  }

  // The sixteen points of the compass, lettered the French way — O for ouest.
  rhumb(deg){
    const R = ['N','NNE','NE','ENE','E','ESE','SE','SSE',
               'S','SSO','SO','OSO','O','ONO','NO','NNO'];
    return R[Math.round(((deg%360)+360)%360 / 22.5) % 16];
  }

  refreshSea(){
    const s = parseFloat(this.el.seaState.value);
    const w = parseInt(this.el.windDir.value);
    this.ocean.setSeaState(s, w);
    this.showSea(s, w);
  }

  /* The console's own reading, without touching the sea. Automatic weather
     drives the ocean with finer numbers than a slider can hold — a tenth of a
     Beaufort is already a whole new sea — so it sets the spectrum itself and
     asks only for the display. Keeping the two apart is what lets the console
     go on telling the truth while something else holds the helm. */
  showSea(s, w){
    this.el.seaVal.textContent = s.toFixed(1);
    /* Rounded here rather than by the caller: the console hands whole
       degrees, but automatic weather works in fractions of one and would
       otherwise print a bearing sixteen digits long. */
    this.el.windVal.textContent = String(Math.round(w) % 360).padStart(3,'0')+'°';
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

    /* The card turns, the lubber line does not — the ship's head is always at
       the top of the glass and you read what lies under it. Rotating the ship
       instead would be a map, not a compass. */
    if(e.roseCard){
      e.roseCard.setAttribute('transform', 'rotate(' + (-headingDeg).toFixed(1) + ')');
      e.roseDeg.textContent = String(Math.round(headingDeg)).padStart(3,'0');
      e.roseRhumb.textContent = this.rhumb(headingDeg);
    }
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
    /* Astern orders read against what she HAS astern, not against full ahead:
       a vessel that can only raise half power backing still has a "toute" of
       her own, and the bar beside it already shows how much less that is. */
    const astern = Math.abs(S.sternPower) || 0.6;
    e.thrTele.textContent = tp===0 ? '— chadburn au repos —'
        : tp>0 ? (tp>66?'EN AVANT TOUTE':tp>33?'en avant demie':'en avant lente')
        : ctrl.throttle < -0.66*astern ? 'EN ARRIÈRE TOUTE'
        : ctrl.throttle < -0.33*astern ? 'en arrière demie'
                                       : 'en arrière lente';

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

    /* --- damage control ---
       Water aboard as a fraction of her own displacement is the figure that
       means something: 200 t is nothing to a frigate and the end of a schooner.
       Whether the pumps are gaining or losing is the other half — it is the only
       thing that tells you if the situation is under control, and no instrument
       said it until now. */
    const flood = p.floodTonnes;
    e.flood.textContent = flood < 10 ? flood.toFixed(1) : Math.round(flood);
    e.flood.style.color = flood > S.tonnes*0.10 ? 'var(--crit)'
                        : flood > 0.5 ? 'var(--warn)' : '';
    e.pumps.textContent = p.pumpOn ? 'EN ROUTE' : 'stoppées';
    e.pumps.style.color = p.pumpOn ? 'var(--good)' : 'var(--muted)';

    const gaining = p.floodRate;             // m³/s, + = she is losing the fight
    const show = p.foundered || p.breaches.length > 0 || flood > 0.05 || p.aground > 0;
    e.damageRow.hidden = !show;
    if(show){
      /* Aground comes FIRST, before any tally of water. It is the thing she is
         doing right now and the thing the helm must answer — a leak can wait a
         minute, a hull on the rock cannot. */
      e.damage.textContent = p.foundered ? 'SOMBRÉ'
        : p.aground > 0 ? 'ÉCHOUÉE · ' + p.aground.toFixed(1) + ' m dans le fond'
        : p.breaches.length === 0 ? (flood > 0.05 ? 'voies d\'eau bouchées — assèchement' : '—')
        : gaining > 0.002 ? p.breaches.length + ' voie' + (p.breaches.length>1?'s':'') +
            ' d\'eau — elle embarque ' + (gaining*C.RHO/1000).toFixed(1) + ' t/s'
        : 'voies d\'eau maîtrisées par les pompes';
      e.damage.style.color = (p.foundered || p.aground > 0 || gaining > 0.002) ? 'var(--crit)' : 'var(--warn)';
    }

    this.cam.refreshLabel(b);

    // status lamp by how far she is over
    const sev = Math.abs(roll);
    const col = sev>28 ? 'var(--crit)' : sev>16 ? 'var(--warn)' : 'var(--good)';
    e.dot.style.background = col;
    e.dot.style.boxShadow = '0 0 8px '+col;
  }
};
