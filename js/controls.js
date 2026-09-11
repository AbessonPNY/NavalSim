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
    this.onFire = null;           // (autreBord, maintenu)
    this.onCastOff = null;        // larguer les amarres
    this.onToggleKeys = null;     // le mémento des commandes
    this.onCloseKeys = null;      // Échap le referme, et ne fait que ça
    this.onTogglePanel = null;    // (2 à 6) escamoter un panneau
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
      if(k==='m' && this.onCastOff) this.onCastOff();   // M comme amarres
      if(k==='k' && this.onBlowUp) this.onBlowUp();
      if(k==='h' && this.onToggleHud) this.onToggleHud();
      /* F1 ouvre l'aide du NAVIGATEUR si on le laisse faire, ce qui sort du
         jeu et ne se voit pas venir. e.key rend 'F1', que le passage en
         minuscules donne 'f1' — donc aucune collision avec 'f'. */
      if(k==='f1' && this.onToggleKeys){ this.onToggleKeys(); e.preventDefault(); }
      /* F2 à F6 escamotent un panneau chacun, et TOUTES doivent être retenues
         au vol — bien plus que F1. F5 recharge la page : laissée passer, la
         touche « état de la mer » relance la simulation et jette la partie,
         ce qui est la pire manière de découvrir qu'on a oublié un
         preventDefault. F3 ouvre la recherche du navigateur, F6 lui donne la
         barre d'adresse. Le numéro est passé tel quel : quel panneau porte
         quel numéro est une affaire de balisage, donc cela se décide dans la
         page et pas ici. */
      const fn = /^f([2-6])$/.exec(k);
      if(fn && this.onTogglePanel){ this.onTogglePanel(+fn[1]); e.preventDefault(); }
      if(k==='escape' && this.onCloseKeys) this.onCloseKeys();
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
      /* La touche ne dit plus QUEL bord mais si l'on veut l'AUTRE : le bord en
         batterie est un état que la page tient et que le HUD montre. */
      if(k==='g' && this.onFire){
        if(!e.repeat) this.onFire(e.shiftKey, false);
        else if(!this._salvo){ this._salvo = true; this.onFire(e.shiftKey, true); }
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
