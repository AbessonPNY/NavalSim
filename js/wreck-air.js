/* The air coming up out of a ship that is going down.
 *
 * Nothing here decides that a sinking ought to bubble. The solver already
 * knows how much water enters each compartment, and every cubic metre that
 * comes in pushes one of air out; ship-physics credits it to the compartment
 * whenever the way out is drowned. This file only carries that air to the top.
 *
 * And it TAKES ITS TIME, which is most of what makes it read as coming from
 * something below rather than as a fountain on the surface. A slug of air
 * rises at a speed set by its own size — the Davies-Taylor spherical cap,
 * U ≈ 0.71·√(g·r) — so a cubic metre of it makes about two metres a second,
 * and from a wreck twenty metres down it arrives ten seconds after it left.
 * The bubbling therefore goes on after she is out of sight, and dies away
 * rather than stopping. The same idea as the sound of a gun: the delay is a
 * division, and it is the effect.
 *
 * What arrives does two things, both through machinery that already exists:
 *   · a low burst of spray, through splash.js — the dome of water the slug
 *     shoulders up, torn open as it breaks;
 *   · a boil in the persistent foam field, which is what stays: a churning
 *     white patch that lingers and fades where she went down.
 */
window.Naval = window.Naval || {};

Naval.WreckAir = class WreckAir {
  constructor(splash, foam){
    this.splash = splash;
    this.foam = foam;
    this.rising = [];          // slugs on their way up: {x, z, V, r, at}
    this.boils = [];           // patches breaking the surface: {x, z, r, born, life}
    this.clock = 0;
    this._state = new WeakMap();   // per compartment: what is owed, and the next gulp
    this._at = new THREE.Vector3();
    this._fwd = new THREE.Vector3();
    this._side = new THREE.Vector3();
    this._top = new THREE.Vector3();
    this._last = new THREE.Vector3();
    this._breath = new WeakMap();      // per hull: has she let go yet
    this.onLastBreath = null;          // (where, m³) — for whoever wants to hear it
  }

  /* Air does not leave a hull as a steady stream but in GULPS — it collects
     under a beam until the pocket spills. The size follows the flow, so a
     compartment taking the sea green heaves up big slugs several times a
     second while the last pockets of a wreck come up singly, seconds apart. */
  _nextGulp(flux){
    const base = Math.min(5, Math.max(0.25, 0.35*flux));
    return base*(0.45 + Math.random()*1.1);
  }

  update(dt, fleet, ocean, t){
    if(dt <= 0) return;
    this.clock += dt;

    // --- 1. what each hull breathed out, sent on its way up ---
    for(const e of fleet){
      const ph = e.physics;
      if(!ph || !ph.comps) continue;
      this._lastBreath(e, ocean, t);
      for(const c of ph.comps){
        let s = this._state.get(c);
        if(!s){ s = { owed:0, flux:0, gulp:0.5 }; this._state.set(c, s); }
        const a = c.air; c.air = 0;
        // smoothed over half a second: a frame's worth of flow is too jumpy to size a gulp by
        s.flux += (a/dt - s.flux)*Math.min(1, dt/0.5);
        s.owed += a;
        if(s.owed < s.gulp) continue;

        const depth = Math.max(0.3, ocean.sample(c.vent.x, c.vent.z, t) - c.vent.y);
        let n = 0;
        while(s.owed >= s.gulp && n++ < 8 && this.rising.length < 400){
          const V = s.gulp;
          s.owed -= V;
          s.gulp = this._nextGulp(s.flux);
          const r = Math.cbrt(3*V/(4*Math.PI));
          const rise = 0.71*Math.sqrt(9.81*r);
          /* A plume spreads as it climbs, so the deeper she is the wider the
             patch it breaks over — the boil above a deep wreck is broad and
             slow, the one beside a hull still at the surface is tight. */
          const spread = 0.4 + 0.12*depth;
          const ang = Math.random()*Math.PI*2, rad = spread*Math.sqrt(Math.random());
          let x = c.vent.x + Math.cos(ang)*rad, z = c.vent.z + Math.sin(ang)*rad;

          /* WHILE SHE IS STILL AT THE SURFACE, THE AIR COMES OUT ALONG HER SIDES.
             It leaves through every opening she has — hatches, but gunports and
             the rail as well — and more to the point, anything breaking INSIDE
             her outline is hidden by her own planking. That was measured, not
             guessed: the pirate's solver hull was 98 % under with every boil in
             place over her deck, and not one showed, her model standing three
             metres prouder than her solver lines. So a share of the gulps is
             carried out to her waterline abreast of the vent, the same unit-
             circle trick the bow spray uses, and that share falls away as she
             goes deeper than her own depth and the plume closes over her. */
          const S = ph.spec, deep = Math.min(1, Math.max(0, (depth - S.D)/(S.D + 4)));
          if(Math.random() > deep){
            const q = ph.body.quat;
            this._fwd.set(0, 0, 1).applyQuaternion(q); this._fwd.y = 0; this._fwd.normalize();
            this._side.set(-this._fwd.z, 0, this._fwd.x);    // horizontal abeam, either side will do
            const along = (c.vent.x - ph.body.pos.x)*this._fwd.x + (c.vent.z - ph.body.pos.z)*this._fwd.z;
            const ez = Math.max(-0.95, Math.min(0.95, along/(S.L*0.5)));
            const ex = Math.sqrt(1 - ez*ez)*(Math.random() < 0.5 ? -1 : 1);
            const out = 1.08 + 0.25*Math.random();
            x = ph.body.pos.x + this._fwd.x*ez*S.L*0.5*out + this._side.x*ex*S.B*0.5*out;
            z = ph.body.pos.z + this._fwd.z*ez*S.L*0.5*out + this._side.z*ex*S.B*0.5*out;
          }
          this.rising.push({ x, z, V, r, rise, spread, at:this.clock + depth/rise });
        }
        if(s.owed > 20) s.owed = 20;    // never a backlog that erupts all at once
      }
    }

    // --- 2. what reaches the top ---
    let fired = 0;
    for(let i = this.rising.length - 1; i >= 0; i--){
      const b = this.rising[i];
      if(b.at > this.clock) continue;
      /* A budget per frame, to spare the drop pool: a gulp held back comes up a
         frame or two late, which nobody can see. */
      if(fired >= 6) break;
      fired++;
      this.rising[i] = this.rising[this.rising.length - 1];
      this.rising.pop();

      this._at.set(b.x, ocean.sample(b.x, b.z, t), b.z);
      /* The dome the slug lifts is about twice its own volume of water — the
         cap drags a wake of water up behind it — thrown by its rise speed
         amplified as the air expands into the last metre and bursts: a heave and
         a ragged spit, not a column. At its own volume it read as a fleck. */
      /* Her last breath is not a slug working its way up but the whole upper
         works venting at the surface, so it is thrown harder and taller — the
         jet factor splash.js keeps for exactly the narrow violent case. */
      if(b.big) this.splash.burst(this._at, b.V*2, 4 + 3*b.rise, 1.8);
      else      this.splash.burst(this._at, b.V*2, 2.5 + 3*b.rise);
      /* The patch it breaks over is the width of the PLUME, not of the slug:
         air from a deep wreck arrives spread across metres of water, and each
         slug churns the whole of it. Three seconds, because a boil heaves and
         spreads before the water closes over it. */
      this.boils.push({ x:b.x, z:b.z, r:Math.max(2.5, 1.3*b.spread) + 2.5*b.r,
                        born:this.clock, life:b.big ? 6.0 : 3.0 });
    }

    // --- 3. the boils to lay into the foam this frame, strongest first ---
    const list = [];
    for(let i = this.boils.length - 1; i >= 0; i--){
      const bo = this.boils[i];
      const u = (this.clock - bo.born)/bo.life;
      if(u >= 1){ this.boils[i] = this.boils[this.boils.length - 1]; this.boils.pop(); continue; }
      // up at once as the dome breaks, then spreading and thinning
      bo.w = Math.min(1, u*8)*(1 - u*u);
      list.push({ x:bo.x, z:bo.z, r:bo.r*(0.7 + 0.6*u), w:bo.w });
    }
    list.sort((p, q) => q.w - p.w);
    this.foam.setBoils(list);
  }

  /* HER LAST BREATH, when the last of her upper works goes under.

     Theatre, asked for as such — and it happens to have a real cause, which
     is the only reason it is allowed here. The air in her castles and under
     her upper deck has nowhere to go while any of it is above the sea; the
     moment the highest of it is drowned, it all goes at once, and a ship
     going down is remembered for that great belch of white water far more
     than for anything before it. So it is not a burst laid on top: it is the
     trapped pocket ship-physics already carries, most of it released in one
     go instead of on its slow exponential, at the spot that went under last.

     Read on her VISIBLE hull, not on the solver's: the pirate's model stands
     three metres prouder than her lines, so the solver's deck was under long
     before the eye saw anything go. `ship.shell` is the model's own hull cut
     into the flooding's compartments; a procedural hull has none, and there
     the solver's compartments ARE what is drawn. */
  _lastBreath(e, ocean, t){
    const ph = e.physics;
    let st = this._breath.get(ph);
    if(!ph.foundered){ if(st) this._breath.delete(ph); return; }
    if(st && st.done) return;
    if(!st){ st = { done:false }; this._breath.set(ph, st); }

    const shell = e.ship && e.ship.shell, q = ph.body.quat, P = ph.body.pos;
    const n = shell ? shell.length : ph.comps.length;
    let high = -Infinity, above = false;
    for(let i = 0; i < n; i++){
      if(shell) this._top.set(0, shell[i].deck, 0.5*(shell[i].z0 + shell[i].z1));
      else this._top.set(0, ph.comps[i].deckY, ph.comps[i].mid.z);
      this._top.applyQuaternion(q).add(P);
      const over = this._top.y - ocean.sample(this._top.x, this._top.z, t);
      if(over > -0.3) above = true;                 // a hand's breadth of her still out
      if(this._top.y > high){ high = this._top.y; this._last.copy(this._top); }
    }
    if(above) return;
    st.done = true;

    // most of what is trapped, and never less than a gulp worth watching
    const V = Math.max(6, 0.6*(ph.trappedAir || 0));
    ph.trappedAir = Math.max(0, (ph.trappedAir || 0) - V);

    /* One great heave where she went under last, then two smaller ones along
       her length a moment later: a single burst reads as one thing breaking the
       surface, a stagger reads as a ship letting go of everything at once. Sent
       through the rising queue so the drop-pool budget still applies. */
    this._fwd.set(0, 0, 1).applyQuaternion(q); this._fwd.y = 0; this._fwd.normalize();
    const L = ph.spec.L;
    const shots = [[0, 0.62, 0], [-0.25, 0.22, 0.20], [0.30, 0.16, 0.45]];
    for(const [off, share, delay] of shots){
      const v = V*share, r = Math.cbrt(3*v/(4*Math.PI));
      this.rising.push({ x:this._last.x + this._fwd.x*off*L, z:this._last.z + this._fwd.z*off*L,
                         V:v, r, rise:0.71*Math.sqrt(9.81*r), spread:0.25*L,
                         at:this.clock + delay, big:true });
    }
    if(this.onLastBreath) this.onLastBreath(this._last, V);
  }

  // Everything held is in the local frame, so it moves with the world.
  rebase(dx, dz){
    for(const b of this.rising){ b.x -= dx; b.z -= dz; }
    for(const b of this.boils){ b.x -= dx; b.z -= dz; }
  }
};
