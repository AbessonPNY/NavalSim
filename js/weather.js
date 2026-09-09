/* The weather makes up its own mind.
 *
 * A wind set once and never touched is the most artificial thing left on the
 * water: every sea becomes a photograph of itself, and nothing that happens in
 * the next hour was not already true in the first minute.
 *
 * What makes a real wind alive is not that it is random — it is that it has
 * MEMORY. The next gust resembles the last one; the afternoon resembles the
 * morning, but not exactly. Drawing a fresh number every few seconds gives the
 * opposite of weather: a wind with no past, jumping between unrelated states.
 *
 * So this is an Ornstein-Uhlenbeck wander, on two timescales that do different
 * jobs:
 *
 *   · the SYSTEM — the mean strength and the mean bearing, over some ten
 *     minutes. This is the front going through, and it is what makes an hour
 *     of sailing have a shape.
 *   · the WIND ITSELF about that mean, over some twenty seconds. These are the
 *     gusts and the lulls, and the small backing and veering a helmsman steers
 *     against.
 *
 * The strength reverts to a climate mean, because most days are a moderate
 * breeze and gales are rarer — reversion is what produces that distribution
 * for free, without a table of probabilities. The BEARING does not revert to
 * anything: no compass point is more natural than another, so its mean is a
 * plain random walk and the wind may end the day anywhere on the rose.
 *
 * Two couplings are worth having, both of them true on the water:
 *   · a strong wind is a gustier wind, in absolute terms — so the size of the
 *     gusts follows the mean strength;
 *   · a LIGHT wind is the fickle one. A gale holds its direction for hours; a
 *     force 2 wanders all over the compass. So the veering does the opposite
 *     and shrinks as the wind gets up.
 */
window.Naval = window.Naval || {};

Naval.Weather = class Weather {
  constructor(){
    this.on = false;

    // --- the system, over the better part of an afternoon ---
    this.sysTau   = 540;      // s: how long the front takes to change its mind
    this.climate  = 4.0;      // Beaufort it returns to when left alone
    this.sysSpread= 1.7;      // Beaufort: how far it strays from that
    this.veer     = 1.15;     // deg per √s of drift in the mean bearing

    /* --- the wind about it ---
       Strength and bearing get DIFFERENT time constants, and the difference is
       audible on deck. A gust arrives and is gone in under a minute; a wind
       shift is a slower thing, and one that oscillates as fast as the gusts
       does not read as weather, it reads as a broken instrument — the helm
       would be chasing it continuously. */
    this.gustTau  = 22;
    this.veerTau  = 70;

    /* An Ornstein-Uhlenbeck path is continuous but nowhere differentiable:
       its increments are white noise, so sampled once a frame the wind takes a
       fresh random kick sixty times a second. That is not a gust, it is a
       rattle — and the sails, the flag and every readout shake with it. A real
       gust has no energy at thirty hertz. So the process is the TARGET, and
       the wind actually blowing follows it through a short lag: the same
       statistics over twenty seconds, a smooth curve over one. */
    this.smoothTau = 1.1;

    this.meanForce = this.climate;
    this.meanDir   = 45;
    this.tgtForce  = this.climate;   // where the process has got to
    this.tgtDir    = 45;
    this.force     = this.climate;   // and what is actually blowing
    this.dir       = 45;

    this.loForce = 0.4; this.hiForce = 9.0;

    /* How fast the SEA is allowed to follow the wind. This is not a matter of
       taste, it is a measurement. Rebuilding the spectrum moves every
       component's wavenumber, and k appears in the phase multiplied by
       position: sampled over a 400 m patch, a step of one Beaufort displaces
       the surface by 9,8 m rms — against a swell whose own rms is 0,6 m. In
       other words a single step of a tenth of a Beaufort, which is the finest
       the console can even show, reshuffles the sea completely.

       So the step is capped where the disturbance stops being visible: 0,0025
       Beaufort is about 2 cm rms, four per cent of the swell. At five rebuilds
       a second that still allows three quarters of a Beaufort a minute, far
       faster than any weather — the cap costs nothing and buys a sea that
       evolves instead of boiling. */
    this.seaRate  = 0.0125;   // Beaufort per SECOND
    this.veerRate = 0.30;     // degrees per second
  }

  /* Walk a lagging sea toward the wind now blowing.

     Capped by RATE, not per rebuild, and the difference is not academic: a cap
     of so much per call makes the sea move faster on a fast machine, and lands
     its step unevenly the moment the frame time wanders. The rate is the thing
     with a physical meaning; the step is whatever dt makes of it. */
  chaseSea(sea, dt, force, dir){
    /* The target is an argument now, not simply what the weather is doing. A
       local squall wants the same rate limit — the reason for it is the
       spectrum, not the source of the number — and sailing into one is exactly
       the case where an unlimited jump would reshuffle the whole sea. */
    if(force == null) force = this.force;
    if(dir == null) dir = this.dir;
    const mf = this.seaRate*dt, mv = this.veerRate*dt;
    const df = force - sea.force;
    const dd = ((dir - sea.dir + 540) % 360) - 180;         // the shorter way round
    /* Nothing worth rebuilding a spectrum for. A sea that has caught up with a
       steady wind should cost exactly nothing, and saying so here is cheaper
       than saying it at the call site. */
    if(Math.abs(df) < 2e-5 && Math.abs(dd) < 5e-4) return false;
    sea.force += Math.max(-mf, Math.min(mf, df));
    sea.dir += Math.max(-mv, Math.min(mv, dd));
    sea.dir = (sea.dir + 360) % 360;
    return true;
  }

  /* Adopt whatever the console is showing, so switching the weather on does
     not teleport the sea to some other day. */
  sync(force, dir){
    this.force = this.tgtForce = this.meanForce = force;
    this.dir   = this.tgtDir   = this.meanDir   = dir;
  }

  /* Box-Muller. One normal deviate per call; the second is thrown away, which
     costs a sine we do not need to count at four calls a frame. */
  _gauss(){
    const u = 1 - Math.random();
    return Math.sqrt(-2*Math.log(u))*Math.cos(2*Math.PI*Math.random());
  }

  /* One step of an Ornstein-Uhlenbeck process, in its EXACT discretisation
     rather than an Euler step. It matters here: the frame time is not fixed,
     and a naive step would make the wind gustier on a slow frame than on a
     fast one — the weather would depend on the frame rate. This form holds the
     same statistics at any dt. */
  _ou(x, mean, sd, tau, dt){
    const a = Math.exp(-dt/tau);
    return mean + (x - mean)*a + sd*Math.sqrt(1 - a*a)*this._gauss();
  }

  update(dt){
    if(!this.on || !(dt > 0)) return;
    dt = Math.min(dt, 0.25);            // a stalled tab must not lurch the weather

    // the front: strength reverts, bearing wanders
    this.meanForce = this._ou(this.meanForce, this.climate,
                              this.sysSpread, this.sysTau, dt);
    this.meanForce = Math.max(1.0, Math.min(8.2, this.meanForce));
    this.meanDir  += this.veer*Math.sqrt(dt)*this._gauss();

    // the gusts, whose size follows the strength
    const gust = 0.10 + 0.085*this.meanForce;
    this.tgtForce = this._ou(this.tgtForce, this.meanForce, gust, this.gustTau, dt);
    this.tgtForce = Math.max(this.loForce, Math.min(this.hiForce, this.tgtForce));

    /* and the backing and veering, which does the opposite: a gale holds her
       bearing, a light air is all over the compass. */
    const swing = 9 / (0.7 + 0.5*this.meanForce);
    this.tgtDir = this._ou(this.tgtDir, this.meanDir, swing, this.veerTau, dt);

    // and the wind itself, following its target smoothly
    const lag = 1 - Math.exp(-dt/this.smoothTau);
    this.force += (this.tgtForce - this.force)*lag;
    const dd = ((this.tgtDir - this.dir + 540) % 360) - 180;   // the shorter way
    this.dir += dd*lag;

    // keep every bearing on the rose, together, so their differences survive
    while(this.dir < 0){ this.dir += 360; this.tgtDir += 360; this.meanDir += 360; }
    while(this.dir >= 360){ this.dir -= 360; this.tgtDir -= 360; this.meanDir -= 360; }
  }
};
