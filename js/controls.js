/* The helm: keyboard state folded into the control positions the solver reads.
   Throttle and sheets hold where you leave them; the rudder self-centres, as a
   wheel left alone would under the pressure of the water. */
window.Naval = window.Naval || {};

Naval.Controls = class Controls {
  constructor(){
    this.keys = {};
    // sheet = how far the booms are eased from the centreline
    this.state = { throttle:0, rudder:0, sheet:0.7, sailsSet:true };
    this.maxSheet = 1.48;        // replaced per vessel by setSpec()
    this.sternMax = -0.6;        // likewise — how far astern her telegraph rings
    this.onCycleCam = null;      // wired up by main()
    this.onReplant = null;
    this.onTrim = null;          // "border au mieux" — asks the solver for its mark
    this.onBreach = null;        // open a hole — a test command until there is damage
    this.onPumps = null;
    this.onSalvage = null;
    this.onBlowUp = null;         // the powder magazine, for the fun of it
    this.onToggleHud = null;      // clear the instruments off the glass
    this.onToggleCargo = null;    // show or hide the stowage plan
    this.onFire = null;           // (side, held) — +1 starboard, -1 port
    this.onDebug = null;          // show or hide the bench
    this.onShot = null;           // take the screen
    this._salvo = false;          // this hold has already loosed its broadside

    addEventListener('keydown', e=>{
      const k = e.key.toLowerCase();
      this.keys[k] = true;
      if(k===' '){ this.state.throttle = 0; e.preventDefault(); }
      if(k==='c' && this.onCycleCam) this.onCycleCam();
      if(k==='v') this.state.sailsSet = !this.state.sailsSet;   // set or furl
      if(k==='x' && this.onReplant) this.onReplant();
      if(k==='t' && this.onTrim) this.onTrim();
      if(k==='b' && this.onBreach) this.onBreach();
      if(k==='p' && this.onPumps) this.onPumps();
      if(k==='r' && this.onSalvage) this.onSalvage();
      if(k==='k' && this.onBlowUp) this.onBlowUp();
      if(k==='h' && this.onToggleHud) this.onToggleHud();
      if(k==='f' && this.onToggleCargo) this.onToggleCargo();
      /* J, and not D: D is the helm. Picked from what is actually free, and
         clear of the letters an AZERTY keyboard moves about. */
      if(k==='j' && this.onDebug) this.onDebug();
      if(k==='i' && this.onShot) this.onShot();       // I comme image
      /* G to starboard, shift for the other side: one mnemonic, two batteries.
         A TAP is one gun; HOLDING it is the whole broadside. The two are told
         apart by the browser's own auto-repeat flag rather than by a timer of
         our own — the first event of a press has `repeat` false, and every one
         after it true — and a latch keeps a long hold from loosing salvo after
         salvo. */
      if(k==='g' && this.onFire){
        if(!e.repeat) this.onFire(e.shiftKey ? -1 : 1, false);
        else if(!this._salvo){ this._salvo = true; this.onFire(e.shiftKey ? -1 : 1, true); }
      }
    });
    addEventListener('keyup', e=>{
      const k = e.key.toLowerCase();
      this.keys[k] = false;
      if(k==='g') this._salvo = false;      // the next press starts a fresh hold
    });
  }

  update(dt){
    const k = this.keys, s = this.state;
    const tStep = 0.9*dt, rStep = 1.8*dt, sStep = 0.7*dt;

    if(k['w']||k['arrowup'])   s.throttle = Math.min(1, s.throttle + tStep);
    if(k['s']||k['arrowdown']) s.throttle = Math.max(this.sternMax, s.throttle - tStep);

    if(k['a']||k['arrowleft'])       s.rudder = Math.max(-1, s.rudder - rStep);
    else if(k['d']||k['arrowright']) s.rudder = Math.min(1, s.rudder + rStep);
    else s.rudder *= (1 - 4*dt);                    // the wheel comes back amidships

    // Q borde (sheets in, toward the centreline), E choque (eases out)
    if(k['q']) s.sheet = Math.max(0, s.sheet - sStep);
    if(k['e']) s.sheet = Math.min(this.maxSheet, s.sheet + sStep);
  }

  // A square-rigger cannot brace round as far as a boomed gaff sail can swing.
  setSpec(spec){
    this.maxSheet = spec.maxSheet;
    this.state.sheet = Math.min(this.state.sheet, spec.maxSheet);
    /* The telegraph cannot be rung further astern than she can actually push.
       Every spec has stated this all along and nothing read it: the limit was a
       flat -0.6 for every vessel, so a 210-tonne barge and a 2000-tonne frigate
       backed with exactly the same vigour. */
    this.sternMax = spec.sternPower;
    this.state.throttle = Math.max(this.sternMax, this.state.throttle);
  }
};
