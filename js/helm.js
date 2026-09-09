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
 *   · close-hauled she steers by the WIND and not by the compass, holding her
 *     luff just full. That is how it is really done, and it means a wind shift
 *     is answered before it has cost her anything;
 *   · and the sheets follow `optSheet`, which the solver already works out in
 *     order to draw the green mark on the console. One definition, two users.
 */
window.Naval = window.Naval || {};

Naval.AutoHelm = class AutoHelm {
  constructor(physics, ctrl, opts){
    this.ph = physics;
    this.ctrl = ctrl;
    const o = opts || {};

    /* The board she lies on going to windward, and it is a MEASURED angle
       rather than a remembered one. What matters is not how close she can
       point but where her velocity made good to windward peaks, which is a
       different question and always a wider angle: pinching gains bearing and
       loses more in speed.

       Polars taken at force 4, sheets held on optSheet, ninety seconds to
       settle at each heading — speed in knots, and made good to windward:

         off the wind   40°   50°   60°   70°   90°
         pirate, square 1,44  1,92  2,35  2,69  3,09
           made good    1,10  1,23  1,18  0,92  0
         schooner, gaff 3,73  4,23  4,57  4,81  5,27
           made good    2,86  2,72  2,28  1,65  0

       So fifty degrees for square canvas and forty for fore-and-aft, and the
       first is the one that surprised: seventy degrees, which is what a real
       square-rigger lies at and what this was first written to, costs her a
       quarter of her progress to windward in THIS model. The rig is kinder
       than the real thing; the helm sails the ship she has. */
    this.closeHauled = o.closeHauled != null ? o.closeHauled
                     : (physics.spec.rig.type === 'square' ? 0.873 : 0.698);  // rad

    /* How near she means to come. A hunter that steers for the very centre of
       her chase rams her, which is not a manoeuvre — she is aimed at a circle
       about the target instead, and closes on the tangent. */
    this.standoff = o.standoff != null ? o.standoff : physics.spec.L*1.6;

    /* Some of them have no canvas at all — the barge is one — and a helm that
       works a beat to windward with nothing aloft simply drifts. She goes under
       power instead, straight at the mark, and none of the sailing applies. */
    this.underPower = !physics.spec.rig.type || physics.spec.rig.type === 'none'
                      || !physics.spec.sailArea;

    this.beatSide = 1;        // which board she is on when working to windward
    this.target = null;       // a Vector3 in the same local frame as body.pos

    this._fwd = new THREE.Vector3();
    this._to = new THREE.Vector3();
  }

  /* Shortest way round, in radians. */
  static wrap(a){ return Math.atan2(Math.sin(a), Math.cos(a)); }

  update(dt, ocean){
    const ph = this.ph, c = this.ctrl, b = ph.body;
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
      want = bearing;                     // an engine does not care where the wind is
      this.beating = false;
    }else if(Math.abs(off) < this.closeHauled){
      /* Dead to windward, or nearly: she cannot go there, so she goes as near
         as she can lie on one board or the other.

         Which board is a decision with memory. Taken fresh each frame from the
         side the chase happens to lie on, a target dead upwind flickers from
         one bow to the other and she stands there in irons, tacking every
         second. A margin of fifteen degrees before she will come about is what
         turns that into a proper leg. */
      if(off*this.beatSide < -0.26) this.beatSide = -this.beatSide;
      want = windFrom + this.beatSide*this.closeHauled;
      this.beating = true;
    }else{
      want = bearing;
      this.beating = false;
    }

    /* The rudder: proportional to the error, damped by the rate she is already
       turning at. Without that second term she hunts — a heavy hull carries her
       swing well past the course, and the helm then fights what it just did. */
    const err = AutoHelm_wrap(want - heading);
    const rate = b.angVel.y;
    let r = err*1.9 - rate*2.6;
    c.rudder = Math.max(-1, Math.min(1, r));

    /* Sheets to the mark the solver is already drawing, and eased a little when
       she is being thrown about: a sail sheeted flat in a seaway shakes rather
       than pulls. Followed rather than snapped to, since a crew takes a moment. */
    if(ph.optSheet !== null && !this.underPower){
      const w = Math.min(1, dt*0.9);
      c.sheet += (ph.optSheet - c.sheet)*w;
    }
    c.sailsSet = true;
    // canvas if she has it, the engine if she has not
    c.throttle = this.underPower ? 1 : 0;
  }
};

/* Kept out of the class so the hot path is a plain call rather than a static
   lookup — this runs for every vessel afloat, every frame. */
function AutoHelm_wrap(a){ return Math.atan2(Math.sin(a), Math.cos(a)); }
