/* A helmsman that is not you.
 *
 * She writes into the same `ctrl` a hand at the wheel would: a rudder from -1
 * to 1, a sheet, sails set or handed. Nothing here reaches into the solver, and
 * that is the point — an automatic helm that could push the hull about would be
 * cheating, and would also stop being a test of whether the ship is sailable.
 * If she cannot get to windward, neither can you.
 *
 * Three jobs, in order of how much they matter:
 *
 *   · she cannot sail where she is pointed, if that is into the wind. A chase
 *     dead to windward is not a course but a series of BOARDS, and choosing
 *     which one to be on is most of what a helmsman does;
 *   · she cannot go round the short way either, because nothing in this model
 *     comes about through the eye of the wind. She WEARS — see below; it is the
 *     one decision that makes the difference between a helm that works and a
 *     ship that hangs head to wind for a quarter of an hour;
 *   · and the sheets follow `optSheet`, which the solver already works out in
 *     order to draw the green mark on the console. One definition, two users.
 */
window.Naval = window.Naval || {};

Naval.AutoHelm = class AutoHelm {
  constructor(physics, ctrl, opts){
    this.ph = physics;
    /* A FALLBACK ONLY. The controls she really writes into are the ones handed
       to update() — see there for why holding this one would be a bug. */
    this.ctrl = ctrl;
    const o = opts || {};

    /* The board she lies on going to windward, and it is a MEASURED angle
       rather than a remembered one. What matters is not how close she can point
       but where her velocity made good to windward peaks, which is a different
       question and always a wider angle: pinching gains bearing and loses more
       in speed.

       And made good is taken on her COURSE, not on her head, which is the whole
       difficulty: she makes a great deal of LEEWAY, and a polar that ignores it
       flatters her by half. The pirate with her head fifty degrees off the wind
       is really going sixty-eight. My first table did exactly that and read
       1,23 knots of made good where the truth is 0,73 — I then spent an hour
       wondering why she took thirty-three minutes to gain eight metres.

       Force 4, sheets on optSheet, held to the heading for 150 s. Speed, the
       course she really makes, and made good to windward on that course:

         head off the wind  40°   50°   60°   70°   90°
         pirate, square     1,43  1,94  2,40  2,78  3,26
           course           63°   68°   74°   81°   97°
           made good        0,64  0,73  0,66  0,43  -0,39

         head off the wind  35°   40°   45°   50°   60°
         schooner, gaff     2,25  2,71  3,14  3,54  4,22
           course           52°   55°   58°   62°   70°
           made good        1,39  1,56  1,65  1,66  1,44

       So fifty degrees for square canvas and forty-seven for fore-and-aft.
       Neither is where a ship of that rig really lies — a square-rigger holds
       seventy — and both were arrived at by measurement rather than by
       recollection: seventy would cost the pirate two fifths of her windward
       work in THIS model. The helm sails the ship she has.

       Note the last column. Beam on, the pirate's course is 97° off the wind:
       she goes bodily to LEEWARD while sailing across it. */
    this.closeHauled = o.closeHauled != null ? o.closeHauled
                     : (physics.spec.rig.type === 'square' ? 0.873 : 0.82);  // rad

    /* How near she means to come. A hunter that steers for the very centre of
       her chase rams her, which is not a manoeuvre — she is aimed at a circle
       about the target instead, and closes on the tangent. */
    this.standoff = o.standoff != null ? o.standoff : physics.spec.L*1.6;

    /* Some of them have no canvas at all — the barge is one — and a helm that
       works a beat to windward with nothing aloft simply drifts. She goes under
       power instead, straight at the mark, and none of the sailing applies. */
    this.underPower = !physics.spec.rig.type || physics.spec.rig.type === 'none'
                      || !physics.spec.sailArea;

    /* The shortest board she will stand on. Changing board means wearing, and
       wearing costs the better part of ten minutes and three hundred metres, so
       she had better mean it. */
    this.minLeg = 300;                                                   // s

    this.beatSide = 1;        // which board she is on when working to windward
    this._iErr = 0;           // standing helm, off the wind only — see below
    this._legT = 0;           // how long she has stood on this board
    this._wearDir = 0;        // and which way she is going round, if wearing
    this.target = null;       // a Vector3 in the same local frame as body.pos

    this._fwd = new THREE.Vector3();
    this._to = new THREE.Vector3();
  }

  /* The controls are given at EVERY update, and are deliberately not the ones
     she was built with. Taking the helm of another vessel swaps the two ships'
     control objects — the one being left is handed a copy so she sails on under
     the orders she was given, the one taken over gets the live console — so a
     reference captured at construction goes stale at that instant.

     It went stale in the worst possible way: the helm of the ship you had just
     left went on writing into YOUR wheel, sixty times a second. A barge left
     astern put your engine full ahead and your rudder hard over; a sailing ship
     left astern held your throttle at zero, so the engine would not answer at
     all, and your sheets and your canvas were not yours either. Passing them in
     is what makes that impossible rather than merely fixed: the caller has the
     entry, the entry has the current controls, and there is nothing left to go
     out of date. */
  update(dt, ocean, ctrl){
    const ph = this.ph, c = ctrl || this.ctrl, b = ph.body;
    if(!this.target || ph.foundered){ c.rudder = 0; return; }

    this._fwd.set(0,0,1).applyQuaternion(b.quat);
    const heading = Math.atan2(this._fwd.x, this._fwd.z);

    this._to.copy(this.target).sub(b.pos); this._to.y = 0;
    const range = this._to.length();
    let bearing = Math.atan2(this._to.x, this._to.z);

    /* Aim at the RIM of her standoff rather than at the ship. Once inside it
       she is steered along the tangent, which turns a collision course into a
       circle — and a vessel circling a chase at two cables is a great deal more
       menacing than one grinding into her stern. */
    if(range < this.standoff*2.2){
      const t = Math.min(1, this.standoff/Math.max(range, 1));
      bearing += Math.asin(Math.max(-1, Math.min(1, t)))*0.9;
    }

    // the bearing the wind blows FROM, in the same frame as her heading
    const windFrom = ocean.windDeg*Math.PI/180;
    const off = AutoHelm_wrap(bearing - windFrom);   // 0 = dead to windward

    let want;
    if(this.underPower){
      want = bearing;                   // an engine does not care where the wind is
      this.beating = false;
    }else if(Math.abs(off) < this.closeHauled){
      /* Dead to windward, or nearly: she cannot go there, so she goes as near
         as she can lie on one board or the other. Which board is a decision
         with MEMORY — taken fresh each frame from the side the chase happens to
         lie on, a mark dead upwind flickers from one bow to the other and she
         stands there in irons, changing her mind every second.

         She changes board ON THE LAYLINE: when the mark bears at her
         close-hauled angle on the OTHER side, because that is the first instant
         the other board fetches it. An earlier margin of fifteen degrees looked
         reasonable and was not — it had her going about while she was closing
         perfectly well, and each change cost her more than the board had
         gained: the range sat between 1 190 and 1 500 m for three quarters of
         an hour without ever converging. The layline also carries its own
         hysteresis, the mark bearing the same angle on the other bow the moment
         she is round, which is as far from the threshold as it is possible to
         get.

         Standing on FURTHER than the layline was tried too, on the theory that
         a manoeuvre this expensive ought to be made rarely. It buys nothing: at
         four times the minimum leg she closed to 756 m against 754. */
      if(off*this.beatSide < -(this.closeHauled - 0.10) && this._legT > this.minLeg){
        this.beatSide = -this.beatSide;
        this._legT = 0;
        /* Going about, the standing helm she was carrying is worse than
           useless: it is holding her onto the board she is leaving. */
        this._iErr = 0;
      }
      want = windFrom + this.beatSide*this.closeHauled;
      this.beating = true;
      // a board is time spent SAILING it; going round does not count
      if(!this.wearing) this._legT += dt;
    }else{
      want = bearing;
      this.beating = false;
    }

    const err = AutoHelm_wrap(want - heading);
    const rate = b.angVel.y;

    /* SHE WEARS, SHE DOES NOT TACK — and this is the one decision that makes
       the difference between a helm that works and a ship that hangs head to
       wind until the tide turns.

       The shortest way round to a course is not always a way she can go. Coming
       about crosses the eye of the wind, where the sheets come to the centreline
       and there is no drive at all; she loses her way, and the rudder's
       authority goes as the SQUARE of her speed, so it dies before her head is
       through. It is missing stays, every time.

       And it is not a matter of degree. This was first written as a square-rig
       speciality, on a speed test, which was wrong: NOTHING in this model comes
       about. Ordered onto the other board with wearing forbidden, from the best
       each can do close-hauled:

         schooner, from 4,07 knots   closest 22° off the wind, then falls back
         pirate,   from 2,18 knots   closest 29°, and stalls at 0,96 knots

       Neither gets her head through. The pirate, ordered a hundred and nineteen
       degrees to port at nine tenths of a knot, came round five degrees in ten
       minutes and slowed the whole way.

       So she goes round the OTHER way — away from the wind, three times as far
       but with her sails full the whole distance, gathering speed rather than
       losing it. That is wearing ship, it is what square-riggers really did,
       and it is why they did it.

       It LATCHES. Bearing away makes her fast, which would unmake the very
       condition that started it, and she would round up again half way through
       — a manoeuvre one commits to. */
    let eUse = err;
    const toWind = AutoHelm_wrap(windFrom - heading);
    const crosses = toWind*err > 0 && Math.abs(toWind) < Math.abs(err);
    if(this._wearDir){
      if(Math.abs(err) < 0.25) this._wearDir = 0;
    }else if(!this.underPower && crosses && Math.abs(err) > 0.35){
      this._wearDir = err > 0 ? -1 : 1;
    }
    // the long way round: same destination, opposite hand
    if(this._wearDir && this._wearDir*err < 0) eUse = err - Math.sign(err)*6.2832;
    this.wearing = !!this._wearDir;

    /* The rudder: proportional to the error, damped by the rate she is already
       turning at. Without that second term she hunts — a heavy hull carries her
       swing well past the course, and the helm then fights what it just did.

       Off the wind she also gets a standing term, to hold the few spokes a
       steady course asks for against a hull that never balances exactly. Only
       off the wind: beating she is either lying on a board and needs none, or
       wearing, where a whole turn would wind it to its stop and it would have
       to unwind again on the far side. Clamped for the same reason. */
    if(this.beating) this._iErr = 0;
    else this._iErr = Math.max(-0.55, Math.min(0.55, this._iErr + err*dt*0.40));
    c.rudder = Math.max(-1, Math.min(1, eUse*1.9 + this._iErr - rate*2.6));

    /* Sheets to the mark the solver is already drawing for the console.

       Nothing SPILLS the canvas, and that is deliberate. Taking the drive off
       her so the rudder can have the argument to itself is what a sailor would
       do about a ship pinned head to wind, and it is exactly wrong here: not
       driving is what keeps her pinned, so the condition that fires it confirms
       itself. Written on a speed test it fired at nought knots, before she had
       ever moved, and she sat in irons for the whole of a sixteen-minute run.
       Wearing is the answer to being stuck, not shaking her canvas out. */
    if(ph.optSheet !== null && !this.underPower)
      c.sheet += (ph.optSheet - c.sheet)*Math.min(1, dt*0.9);
    c.sailsSet = true;
    // canvas if she has it, the engine if she has not
    c.throttle = this.underPower ? 1 : 0;
  }
};

/* Kept out of the class so the hot path is a plain call rather than a static
   lookup — this runs for every vessel afloat, every frame. */
function AutoHelm_wrap(a){ return Math.atan2(Math.sin(a), Math.cos(a)); }
