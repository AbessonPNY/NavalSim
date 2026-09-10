/* The great guns.
 *
 * A gun going off is not a small explosion, and treating it as one is the way
 * to get it wrong. An explosion is a ball that grows in every direction and
 * RISES. A gun is a JET: the gases leave the muzzle sideways at enormous speed,
 * are stopped dead by the air within a few metres, and then roll up into a fat
 * cloud that goes nowhere at all — except downwind.
 *
 * That last part is the whole thing. Powder smoke is slow, thick and long
 * lived, so the ship SAILS OUT FROM UNDER IT, leaving a line of clouds hanging
 * over the water where each gun spoke. Every contemporary account of a fleet
 * action is about the smoke: it blinded the gundeck, it hid the enemy, and it
 * told you where the wind was. A puff that dies where it was born reads as a
 * special effect; a bank of smoke drifting to leeward reads as gunnery.
 *
 * So the smoke does not decay to a standstill the way the blast's does. It
 * relaxes towards the SPEED OF THE AIR, which is one line and gives the drift,
 * the lee bank and the ship-sails-clear all at once, with no case for any of
 * them.
 */
window.Naval = window.Naval || {};

/* Powder smoke has its OWN texture, and it is worth the twenty lines.

   The blast's puff is one radial gradient with its rim bitten away, which is
   right for it: a fireball's smoke is thin and you see a few of them. Draw
   forty of those on top of each other at full opacity, which is what a
   broadside does, and every rim lands in the same place — the gradients add up
   to a perfectly smooth white lozenge. Measured against the reference the first
   attempt looked like a bar of soap lying along her side.

   The cure is LUMPS in the texture rather than more sprites. Half a dozen blobs
   of different sizes, off centre, each with its own falloff, give a puff a
   ragged silhouette; forty of those, each turned to its own angle, never agree
   with one another and read as billows. It is the same lesson as the sails:
   what the eye reads first is the outline, not the shading inside it. */
Naval.powderTexture = function(){
  if(Naval._powderTex) return Naval._powderTex;
  const s = 160, cv = document.createElement('canvas');
  cv.width = cv.height = s;
  const c = cv.getContext('2d');
  const blob = (x, y, r, a) => {
    const g = c.createRadialGradient(x, y, 0, x, y, r);
    g.addColorStop(0.0, 'rgba(255,255,255,' + a + ')');
    g.addColorStop(0.55, 'rgba(255,255,255,' + (a*0.55).toFixed(3) + ')');
    g.addColorStop(1.0, 'rgba(255,255,255,0)');
    c.fillStyle = g;
    c.beginPath(); c.arc(x, y, r, 0, 6.2832); c.fill();
  };
  blob(s*0.50, s*0.50, s*0.40, 0.72);          // the body of it
  for(let i=0;i<7;i++){                        // and the lumps around the rim
    const a = (i/7)*6.2832 + Math.random()*0.7, d = s*(0.13 + Math.random()*0.15);
    blob(s*0.5 + Math.cos(a)*d, s*0.5 + Math.sin(a)*d,
         s*(0.13 + Math.random()*0.12), 0.42 + Math.random()*0.34);
  }
  // and tear a few notches back out, so nothing about it is a disc
  c.globalCompositeOperation = 'destination-out';
  for(let i=0;i<9;i++){
    const a = Math.random()*6.2832, d = s*(0.30 + Math.random()*0.20);
    c.beginPath();
    c.arc(s*0.5 + Math.cos(a)*d, s*0.5 + Math.sin(a)*d,
          s*(0.06 + Math.random()*0.10), 0, 6.2832);
    c.fill();
  }
  const tex = new THREE.CanvasTexture(cv);
  tex.colorSpace = THREE.SRGBColorSpace;
  Naval._powderTex = tex;
  return tex;
};

Naval.Guns = class Guns {
  /* How much of the wind a powder cloud actually takes up. Cold, dense and
     laden, it hangs where it was made and sags away slowly — at the full speed
     of the air the bank left her side before one had finished looking at it. */
  static LAG = 0.30;

  constructor(scene){
    this.scene = scene;
    this.group = new THREE.Group();
    scene.add(this.group);
    this.live = [];
    this._queue = [];
    this.shot = [];            // round shot in the air
    // which gun speaks next, one cursor per side: [babord, tribord]
    this._next = [0, 0];
    this.targets = [];         // fleet entries a ball may find, set by the page
    this.onRecoil = null;      // wired by the page, so the hull answers back
    this.onSplash = null;      // ... and so the sea answers back
    /* ... and so the ship she hits does. Called as
       (entry, 'hull'|'mast', index, frac, speed, calibre, world, dir), where
       `frac` is how far up her depth the hole is and `world`/`dir` are where the
       ball went in and which way it was going. The geometry is found here
       because it is geometry; what it COSTS her is decided by the page, which
       is the half that knows about flooding and rigging.

       The world point was added for the splinters and is worth a word: the
       fraction alone says where the WATER comes in, which is all the flooding
       needs, but it cannot say where the timber flies from. Two different
       questions about the same event, and each wants its own number. */
    this.onStrike = null;
    this._p = new THREE.Vector3();
    this._d = new THREE.Vector3();
    this._a = new THREE.Vector3();
    this._b = new THREE.Vector3();
    this._l0 = new THREE.Vector3();
    this._l1 = new THREE.Vector3();
    this._l2 = new THREE.Vector3();
    this._mn = new THREE.Vector3();
    this._mx = new THREE.Vector3();
    this._q0 = new THREE.Quaternion();

    /* One ball, reused. Drawn WELL over life size, and that is deliberate: a
       twelve-pounder is eleven centimetres across, which is a third of a pixel
       at a cable's distance — it would simply not exist. Same argument as the
       lantern's far glow, and the same answer: the thing has to be the size the
       eye needs, not the size the tables give. Its FLIGHT is exact; only its
       diameter is a lie, and it is the one lie worth telling here. */
    this.ballGeom = new THREE.SphereGeometry(0.55, 10, 7);
    this.ballMat = new THREE.MeshStandardMaterial({ color:0x14120f, roughness:0.62,
                                                    metalness:0.35 });

    /* THE FLASH ON HER OWN PLANKING, and it is a real light rather than a
       painted-on glow. The billboard at the muzzle is the fire one sees; this
       is what that fire DOES — a stab of yellow across her side, her channels
       and the underside of the sail above, gone before one has quite seen it.
       A sprite cannot do that: it is in front of the timber, not on it.

       A POOL, built once and never added to the scene again, and that is the
       whole engineering of it. three.js compiles its shaders against the number
       of lights it can see, so adding a light and removing it a tenth of a
       second later recompiles EVERY material in the scene, twice, per gun — a
       broadside would be a dozen full recompiles and a visible stall. They are
       therefore all created at startup, all left in the scene, all left
       VISIBLE, and switched by their intensity alone: nought is off, and the
       light count never changes, so nothing is ever rebuilt.

       Four of them because a broadside ripples: at a tenth of a second each and
       an interval of about the same, three can overlap and four is comfortable.
       The fifth gun to speak within a tenth of a second steals the oldest lamp,
       which is invisible — a flash that has already begun to die. */
    this.lamps = [];
    for(let i=0;i<4;i++){
      const L = new THREE.PointLight(0xffb454, 0, 1, 2);
      L.castShadow = false;
      scene.add(L);
      this.lamps.push({ L, t:0, life:0, peak:0 });
    }
    this._lamp = 0;
  }

  /* Every puff gets its OWN orientation, and the bank turns slowly as it goes.

     One texture drawn many times at the same angle stops being a cloud: the
     bitten rim that makes a single puff look torn repeats identically in each
     one, and forty of them overlapping average into a smooth sausage — which
     is exactly what the first version looked like. Turned at random they stop
     agreeing, and the same texture reads as forty different pieces of smoke.
     The slow spin afterwards is the rolling: powder smoke boils over itself for
     a long while, and a cloud that only grows and drifts reads as a balloon
     being inflated and towed. */
  _sprite(tex, colour, additive){
    const m = new THREE.Sprite(new THREE.SpriteMaterial({
      map:tex, color:colour, transparent:true, depthWrite:false,
      blending: additive ? THREE.AdditiveBlending : THREE.NormalBlending,
      fog:false, opacity:0, rotation:Math.random()*6.2832 }));
    this.group.add(m);
    return m;
  }

  _battery(muzzles, side){
    const out = [];
    for(const g of muzzles) if(g.side === side) out.push(g);
    return out;
  }

  /* ONE GUN, and the next one next time. A tap on the key is a single piece
     going off, and the battery is walked down from forward to aft press by
     press — which is how a gundeck is actually worked when it is not firing
     together, and gives the player something to do between broadsides. */
  fireOne(muzzles, side, body, spec, wind){
    const g = this._battery(muzzles, side);
    if(!g.length) return 0;
    const s = side > 0 ? 1 : 0;
    const i = this._next[s] % g.length;
    this._next[s] = (i + 1) % g.length;
    this._queue.push({ t:0, g:g[i], body, spec, wind });
    return 1;
  }

  /* THE WHOLE BROADSIDE, gun by gun rather than all together.

     They were fired in ripple down the side, a beat apart, and not for show: a
     whole battery let off at one instant is a single shove, and it both sounds
     and looks like one object breaking. Rippled, the eye follows it along her
     side and reads a row of guns. Same argument as the three charges in the
     magazine, and the masts going over one after another.

     And the ripple is IRREGULAR, which matters more than it sounds. An even
     ninth of a second between guns is a drum roll — a machine, not a crew.
     Every gun has its own captain waiting for his own moment, his own quill,
     his own match; the intervals scatter, and now and then one hangs fire
     altogether and speaks well after its neighbour. That stutter is most of
     what makes a broadside sound crewed, and it costs two lines. */
  broadside(muzzles, side, body, spec, wind){
    const g = this._battery(muzzles, side);
    if(!g.length) return 0;
    const s = side > 0 ? 1 : 0;
    const start = this._next[s] % g.length;
    let delay = 0;
    for(let n = 0; n < g.length; n++){
      this._queue.push({ t:-delay, g:g[(start + n) % g.length], body, spec, wind });
      delay += 0.05 + Math.random()*0.13;
      if(Math.random() < 0.18) delay += 0.12 + Math.random()*0.30;   // one hangs fire
    }
    // every piece has spoken, so the next tap starts where this salvo did
    this._next[s] = start;
    return g.length;
  }

  /* One gun. `g` is a muzzle in the HULL's frame, so it is carried round by her
     heel and her heading before anything is drawn in the world. */
  fire(g, body, spec, wind){
    const k = Math.max(0.35, spec.L/60);       // a carronade is not a 32-pounder
    const smoke = Naval.powderTexture(), glow = Naval.glowTexture();

    // muzzle and the way it points, both carried into the world by her attitude
    this._p.copy(g.p).applyQuaternion(body.quat).add(body.pos);
    /* POINT-BLANK, and it is a DEPRESSION rather than an elevation.

       This was the third laying and the first two were wrong in the same way.
       Three degrees up threw the ball eight metres over her rail; level threw
       it over as well, and the measurement said why: this battery stands six
       and a half metres above the sea, so a gun laid flat from it passes clean
       above a hull whose whole freeboard is less than that. Six hits out of six
       at 160 m, six holes opened — and not a tonne of water in five minutes,
       because every one of them was above her waterline. Which is correct, and
       useless.

       So the gun is laid to put its shot on the SEA at a reference range, which
       is what point-blank actually meant and what the quoin under the breech
       was for: from a high-sided ship at close quarters you must depress, or
       you fire into rigging all afternoon. The angle comes out of the muzzle's
       own height above the water, so a low battery is laid nearly flat and a
       towering one is laid well down, with nothing to set per ship. */
    const overSea = Math.max(0, g.p.y + body.pos.y);
    const elev = -Math.max(0, overSea - 2.9)/300;      // 2,9 m is the drop at 300 m

    /* And it is laid against the HORIZON, not against her deck. A gun captain
       sights along his piece at the enemy's waterline and works the quoin until
       it bears; her heel is his problem, not the ball's.

       Laid on the deck instead — which is what taking the hull's attitude
       whole amounts to — the steady heel she carries under canvas decides
       everything, and the measurement was brutal: from the same ship in the
       same breath, the weather battery threw its shot 830 m and the lee battery
       put it in the water at 45. One side at the moon and the other into her
       own side. Every ship that ever fought under sail carried that heel; what
       they did about it was aim. */
    this._d.set(g.side, 0, 0).applyQuaternion(body.quat);
    this._d.y = 0;                       // her bearing, taken flat
    if(this._d.lengthSq() < 1e-6) this._d.set(g.side, 0, 0);
    this._d.normalize();
    this._d.y = elev;                    // and then the quoin, against the sea
    this._d.normalize();
    const at = this._p.clone(), out = this._d.clone();
    // something to spread the jet about, square to the way it points
    const up = new THREE.Vector3(0,1,0);
    const sideV = new THREE.Vector3().crossVectors(out, up).normalize();

    /* FLASH — a TONGUE of flame, not a spark. It is what makes the eye believe
       the smoke was thrown rather than released, and the first cut had far too
       little of it: three small sprites gone in a twentieth of a second, which
       the smoke covered before one had seen them. A gun throws burning powder
       several metres out of the muzzle and it lasts long enough to be read.

       Drawn strictly OUTWARD along the barrel and growing as it goes, so it is
       a cone and not a ball, and given a render order so the smoke cannot
       swallow it: additive light in front of its own cloud is exactly what a
       muzzle flash is.

       Still not a second sun, mind. It does not light the sky the way the
       magazine does — a gun is a bright thing in one place. */
    for(let i=0;i<6;i++){
      const s = this._sprite(glow, i<2 ? 0xfff4d2 : 0xffc766, true);
      s.renderOrder = 12;
      this.live.push({
        m:s, t:-i*0.010, life:0.10 + i*0.032, kind:'flash',
        p:at.clone().addScaledVector(out, (0.5 + i*0.85)*k),
        v:out.clone().multiplyScalar((14 - i)*k), drag:5.0, lift:0,
        s0:(1.5 + i*0.75)*k, s1:(3.0 + i*1.5)*k });
    }

    /* THE JET. Fast out of the muzzle and stopped almost at once — twenty-five
       metres a second against a drag that kills it in half a second puts the
       nose of it some ten metres outboard, which is what a gun really throws.
       Slower and it dribbles; without the hard drag it flies away like a rocket
       and stops being smoke. */
    for(let i=0;i<7;i++){
      const s = this._sprite(smoke, 0xf0ece2, false);
      const spread = (Math.random()-0.5);
      this.live.push({
        m:s, t:-i*0.018, life:3.4 + Math.random()*2.0, kind:'smoke',
        p:at.clone().addScaledVector(out, 0.4*k),
        v:out.clone().multiplyScalar((16 + Math.random()*14)*k)
            .addScaledVector(sideV, spread*4*k)
            .add(new THREE.Vector3(0, (Math.random()-0.2)*2.5*k, 0)),
        drag:3.4, lift:0.55*k, spin:(Math.random()-0.5)*1.1, flash:1.0,
        s0:2.4*k, s1:(10 + Math.random()*5)*k });
    }

    /* THE BANK. The bulk of it: slow, wide, and living four times as long as
       the jet. This is what hangs over the water after she has gone by, and it
       is most of what one is looking at. */
    for(let i=0;i<10;i++){
      const s = this._sprite(smoke, 0xe8e3d8, false);
      const a = Math.random()*6.2832;
      this.live.push({
        m:s, t:0.06 + Math.random()*0.5, life:7.0 + Math.random()*4.0, kind:'smoke',
        p:at.clone().addScaledVector(out, (2 + Math.random()*7)*k)
            .add(new THREE.Vector3((Math.random()-0.5)*4*k,
                                   (Math.random()-0.4)*3*k,
                                   (Math.random()-0.5)*6*k)),
        v:out.clone().multiplyScalar((1.5 + Math.random()*3)*k)
            .add(new THREE.Vector3(Math.cos(a)*0.9*k, 0.7 + Math.random()*1.0,
                                   Math.sin(a)*0.9*k)),
        drag:0.7, lift:0.22*k, spin:(Math.random()-0.5)*0.5, flash:0.5,
        s0:(3.4 + Math.random()*2.6)*k, s1:(15 + Math.random()*12)*k });
    }

    /* THE SHOT ITSELF, and it flies for real.

       Muzzle velocity about 440 m/s, and then QUADRATIC DRAG, which is the
       whole character of round shot. A sphere is a dreadful projectile: with
       Cd near 0.9 at these speeds, 5,4 kg and eleven centimetres across, the
       deceleration comes to `c·v²` with c = ρ·Cd·A/2m ≈ 0,001 per metre. That
       one number produces, with no case for any of it:

         · a velocity that falls as 1/(1 + c·x) — 296 m/s left at 500 m;
         · a flight time to 500 m of 1,46 s, in which gravity has taken it down
           more than TEN METRES;
         · and therefore point-blank range. Naval gunnery was fought at a couple
           of hundred yards not out of bloodthirstiness but because that is the
           distance at which a flat-laid gun hits what it is pointed at. At
           200 m the drop is 1,2 m; at 500 it is a mast's height.

       The ball is lighter and smaller on a smaller ship, and c goes as one over
       the calibre — mass falls as the cube where area falls as the square — so
       a small vessel's shot loses its way faster, without a second number.

       She also throws it from a moving deck, so the ship's own way goes into
       it. And guns scatter: a smoothbore firing a ball that rattles down the
       barrel was not a precision instrument. */
    /* No two charges alike either. Powder was made by hand, scooped or
       cartridged by hand, and rammed by a man in a hurry: a few per cent either
       way on the charge is generous rather than pessimistic, and it scatters
       the fall of shot on its own without a second rule about accuracy. */
    const charge = 0.94 + Math.random()*0.12;
    const spread = 0.010;
    const v0 = out.clone()
      .applyAxisAngle(sideV, (Math.random()-0.5)*spread)
      .applyAxisAngle(up,    (Math.random()-0.5)*spread)
      .multiplyScalar(440*charge)
      .add(body.vel);
    const m = new THREE.Mesh(this.ballGeom, this.ballMat);
    m.position.copy(at);
    this.group.add(m);
    this.shot.push({ m, p:at.clone(), v:v0, t:0, c:0.00097/k, k, from:body });

    /* And she FEELS it. Not a shove that moves her — the arithmetic says
       otherwise and it is worth having the arithmetic say it — but a real
       impulse at a real height, which is a real heeling moment. A twelve-pound
       ball leaves at some 440 m/s, so with the gases behind it a gun throws
       about three thousand kilogramme-metres a second back through its
       trunnions, six metres above her centre of gravity. */
    /* Light her up. Set a little OUTBOARD of the muzzle, because the piece
       fires through her side and the flash belongs outside the planking — put
       at the muzzle itself it sits inside her and lights her gundeck through
       the hull instead of her topsides.

       Ranged, not global: a point light with a distance falls off to nothing at
       that distance, so a dozen metres lights the strake around the port, the
       channel above and the sea just under it, and leaves the rest of her to
       the sun. That is what "localised" has to mean — a lamp with no range
       would flood her whole side and read as lightning. */
    const lp = this.lamps[this._lamp];
    this._lamp = (this._lamp + 1) % this.lamps.length;
    lp.L.position.copy(at).addScaledVector(out, 1.1*k);
    /* Réglé à l'œil au crépuscule, qui est le seul moment où l'on peut juger :
       en plein soleil la lueur ne dispute rien au jour, de nuit elle est tout
       ce qu'on voit. Neuf mètres de portée sur un navire de trente, c'est moins
       du tiers de sa longueur — le bordé autour du sabord, le livet au-dessus,
       et la mer juste dessous ; l'autre bout d'elle reste dans le noir. */
    lp.L.distance = 18*k;
    lp.peak = 45*k*k;
    lp.life = 0.10;
    lp.t = 0;
    lp.L.intensity = lp.peak;

    if(this.onRecoil) this.onRecoil(g, out, 3000*k*k);
  }

  /* Where a segment first enters a box, as a fraction of itself, or -1.

     Segment and not point, and that matters more here than anywhere else in
     this project: a ball crosses fourteen metres in one frame at sixty images a
     second, which is wider than the hull it is supposed to hit. Tested point by
     point it would pass clean through her every time — the classic way to write
     a projectile that never hits anything. */
  static _slab(p0, p1, min, max){
    let t0 = 0, t1 = 1;
    for(const a of ['x','y','z']){
      const d = p1[a] - p0[a];
      if(Math.abs(d) < 1e-9){
        if(p0[a] < min[a] || p0[a] > max[a]) return -1;
        continue;
      }
      let ta = (min[a] - p0[a])/d, tb = (max[a] - p0[a])/d;
      if(ta > tb){ const s = ta; ta = tb; tb = s; }
      if(ta > t0) t0 = ta;
      if(tb < t1) t1 = tb;
      if(t0 > t1) return -1;
    }
    return t0;
  }

  /* Step every ball in the air, and see what it finds.

     Substepped on DISTANCE rather than on time: at the muzzle it is moving
     fourteen metres a frame and at the end of its flight two, so a fixed number
     of substeps is either wasteful or useless. Four metres a step keeps the
     drag honest where it does its work — the first fifty metres — and costs
     almost nothing once she has slowed. */
  _flight(dt, ocean, t){
    for(let i=this.shot.length-1; i>=0; i--){
      const b = this.shot[i];
      b.t += dt;
      let dead = b.t > 12;                       // nothing carries that long
      const speed = b.v.length();
      const n = Math.max(1, Math.min(16, Math.ceil(speed*dt/4)));
      const h = dt/n;

      for(let k=0; k<n && !dead; k++){
        this._a.copy(b.p);
        const s = b.v.length();
        b.v.addScaledVector(b.v, -b.c*s*h);      // c·v², along the way it is going
        b.v.y -= 9.81*h;
        b.p.addScaledVector(b.v, h);
        this._b.copy(b.p);

        // --- a hull, before the water: she stands above it ---
        for(const e of this.targets){
          if(!e || !e.physics || !e.body || e.body === b.from) continue;
          if(e.afloat === 0) continue;
          const q = e.body.quat, o = e.body.pos, L = e.spec.L;
          const inv = this._q0.copy(q).invert();
          const l0 = this._l0.copy(this._a).sub(o).applyQuaternion(inv);
          const l1 = this._l1.copy(this._b).sub(o).applyQuaternion(inv);
          // her SPARS stand above her hull, so they are asked first
          const falls = (e.ship && e.ship.falls) || [];
          for(let fi=0; fi<falls.length; fi++){
            const f = falls[fi];
            if(!f.userData.mast || f.userData.fall) continue;   // gone already
            const hy = f.position.y, hz = f.position.z;
            this._mn.set(-1.1, hy, hz - 1.1);
            this._mx.set( 1.1, hy + f.userData.height, hz + 1.1);
            const u = Guns._slab(l0, l1, this._mn, this._mx);
            if(u < 0) continue;
            if(this.onStrike){
              const w = this._l2.copy(l0).lerp(l1, u)
                          .applyQuaternion(e.body.quat).add(e.body.pos);
              this.onStrike(e, 'mast', fi, 0.5, b.v.length(), b.k,
                            w, this._b.clone().sub(this._a).normalize());
            }
            dead = true;
            break;
          }
          if(dead) break;

          /* Her side as the EYE has it — the model's shell where she has one,
             and the probe grid only for a procedural hull that has no model.
             The two differ by metres in height, and the ball has to agree with
             the picture. */
          const shell = e.ship && e.ship.shell;
          const comps = e.physics.comps, nc = comps.length;
          for(let ci=0; ci<nc; ci++){
            let frac;
            if(shell){
              const sh = shell[ci];
              this._mn.set(-sh.half, sh.keel, sh.z0);
              this._mx.set( sh.half, sh.deck, sh.z1);
            }else{
              const c = comps[ci];
              if(c.cap <= 0) continue;
              this._mn.set(-c.halfB, c.keelY, -L/2 + ci*L/nc);
              this._mx.set( c.halfB, c.deckY, -L/2 + (ci+1)*L/nc);
            }
            const u = Guns._slab(l0, l1, this._mn, this._mx);
            if(u < 0) continue;
            const hit = this._l2.copy(l0).lerp(l1, u);
            // a fraction of her depth, which means the same in either frame
            frac = (hit.y - this._mn.y)/Math.max(0.5, this._mx.y - this._mn.y);
            if(this.onStrike){
              const w = hit.clone().applyQuaternion(e.body.quat).add(e.body.pos);
              this.onStrike(e, 'hull', ci, frac, b.v.length(), b.k,
                            w, this._b.clone().sub(this._a).normalize());
            }
            dead = true;
            break;
          }
          if(dead) break;
        }
        if(dead) break;

        // --- and failing that, the sea ---
        if(ocean){
          const sea = ocean.sample(b.p.x, b.p.z, t || 0);
          if(b.p.y <= sea){
            /* A ball makes a narrow hole in the water very fast, so what one
               sees is a TALL THIN column and not a wide dome. Three numbers say
               exactly that: a modest volume, so the foot of it stays narrow and
               the drop count stays sensible; a brisk throw; and a raised jet
               ceiling, because the rule that a crown rises about as far as its
               cavity is wide describes a hull settling into a trough and not a
               ball at three hundred metres a second. Handing it the real
               impact speed would be meaningless — spray has its own honest cap
               — what it wants is the SHAPE of the event.

               The volume is set by a constraint that has nothing to do with
               water: the spray pool holds two thousand drops and a burst takes
               eleven per cubic metre, so a broadside of six must fit inside it
               or the last guns rob the first through the rolling cursor.
               Twenty-six cubic metres gives 286 drops apiece, 1 716 for the
               salvo — and the density is what makes a column read at the range
               a gun is fired, far more than its height does. */
            if(this.onSplash) this.onSplash(b.p.clone().setY(sea), 26, 16, 3.0);
            dead = true;
          }
        }
      }

      if(dead){ this.group.remove(b.m); this.shot.splice(i,1); continue; }
      b.m.position.copy(b.p);
    }
  }

  update(dt, wind, ocean, t){
    this._flight(dt, ocean, t);
    /* SHE FIRES ON THE ROLL, and this is not a refinement — without it the
       guns are useless in any sea worth sailing.

       The laying is worth eight hundredths of a degree. Her PITCH in a force 4
       is four, fifty times as much, and it goes straight into the barrel: the
       muzzle sweeps up and down as she works, and a shot loosed on the up-roll
       goes over everything. Measured, the same broadside from the same ship:
       fell at 178 m in flat water — which is the point-blank the laying was
       built for — and at 799 m by force 4. Four times too far, every time.

       So each gun waits for its own moment, as its captain did: once its turn
       in the ripple has come it holds until the piece is COMING DOWN, and only
       then speaks. It is the oldest trick on a gundeck and the reason
       broadsides were ragged — every captain judging his own roll, which is the
       same irregularity the ripple already has, now arising rather than
       imposed. And he cannot wait for ever: two seconds and a half, and he
       fires whatever she is doing.

       Coming DOWN and not level, which took a wrong turn to see. A gun points
       athwartships, so her PITCH barely touches it — rotating about the
       athwartships axis leaves an athwartships barrel where it was. What lifts
       a gun is her HEEL, and under canvas she carries a steady one: the weather
       battery looks at the sky and the lee battery at the water, all day, which
       is true of every ship that ever fought under sail. Waiting for level
       therefore never came true on the weather side at all, and every piece
       went off on the timeout instead — the same 800 m in a flat calm as in a
       gale, which is how the mistake announced itself.

       The rate is the honest test: fire while the muzzle is descending, whatever
       height it is descending from. */
    for(let i=this._queue.length-1; i>=0; i--){
      const q = this._queue[i];
      q.t += dt;
      if(q.t < 0) continue;
      if(q.t < 2.5){
        this._d.set(q.g.side, 0, 0).applyQuaternion(q.body.quat);
        // d/dt of the muzzle's height is (omega x d).y — negative is coming down
        this._a.crossVectors(q.body.angVel, this._d);
        /* Hold only while the muzzle is RISING. Dead still counts as fireable,
           which sounds obvious and was not what this first said: written as
           "wait until it is descending", a ship lying quiet in a flat calm
           never satisfied it and every piece went off on the two-and-a-half
           second timeout instead. A gun captain waits for the downroll when
           there is a roll to wait for; without one he simply fires. */
        if(this._a.y > 0.004) continue;
      }
      this.fire(q.g, q.body, q.spec, q.wind);
      this._queue.splice(i,1);
    }
    /* The flash dies fast and unevenly: a charge does not fade out, it goes.
       The square makes the tail short so what is left is a stab and not a
       lamp being turned down. */
    for(const lp of this.lamps){
      if(lp.life <= 0) continue;
      lp.t += dt;
      const u = lp.t/lp.life;
      lp.L.intensity = u >= 1 ? 0 : lp.peak*(1-u)*(1-u);
      if(u >= 1) lp.life = 0;
    }

    const wx = wind ? wind.x : 0, wz = wind ? wind.z : 0;

    for(let i=this.live.length-1; i>=0; i--){
      const p = this.live[i];
      p.t += dt;
      if(p.t < 0){ p.m.visible = false; continue; }
      p.m.visible = true;

      const u = p.t/p.life;
      if(u >= 1){
        this.group.remove(p.m); p.m.material.dispose();
        this.live.splice(i,1);
        continue;
      }

      /* It relaxes towards the air, NOT towards nought — that is what makes it
         go down to leeward and lets the ship sail out from under her own smoke,
         and decaying to nought would tie every cloud to the water instead,
         which is the one thing smoke never does.

         But only towards a FRACTION of the wind, and that took watching it to
         get right. Given the air's full speed, a bank in a fresh breeze is
         swept off her side before the eye has finished reading it — seven and a
         half metres a second is a cable in twenty seconds. A powder cloud is
         cold and dense and full of unburnt grains: it hangs where the guns
         spoke, sagging away far more slowly than the air around it. Stagnation
         first, drift second. */
      const kd = Math.min(1, p.drag*dt);
      p.v.x += (wx*Naval.Guns.LAG - p.v.x)*kd;
      p.v.z += (wz*Naval.Guns.LAG - p.v.z)*kd;
      p.v.y += (p.lift - p.v.y)*kd;         // barely buoyant: it rolls, it does not tower
      p.p.addScaledVector(p.v, dt);
      p.m.position.copy(p.p);
      p.m.scale.setScalar(p.s0 + (p.s1 - p.s0)*Math.pow(u, 0.5));
      if(p.spin) p.m.material.rotation += p.spin*dt;

      if(p.kind === 'flash'){
        p.m.material.opacity = Math.pow(1-u, 1.4);
      }else{
        /* Powder smoke is PALE and THICK — dirty white, not soot, and you
           cannot see through it. Both were wrong first time and in the same
           direction: at half opacity and seven tenths grey it read as a bank of
           MIST lying along her side. A gun and a magazine part company in
           colour as much as in shape — soot is a fire on board, white is
           gunnery — and the density is not a taste, it is the whole reason
           powder smoke mattered: it BLINDED the gundeck. */
        /* Dense and GREY. White reads as steam; powder smoke is dirty, and it
           is the greyness quite as much as the opacity that makes it look like
           something that was burnt rather than something that was boiled.

           And it goes sooner but always GRADUALLY: the fade is a smooth curve
           over the whole life rather than a hold and a vanish, so a bank thins
           out where it stands instead of switching off. */
        p.m.material.opacity = Math.min(1, u*11) * Math.pow(1-u, 1.15) * 0.95;
        const g = 0.62 - 0.10*u;

        /* THE LIGHT GETS INTO THE CLOUD, and this is most of what makes a gun
           smell of powder rather than look like a smoke machine. The flash is
           inside its own smoke, not in front of it, so for a fraction of a
           second the newborn cloud glows from within — orange at the muzzle,
           cooling outward and away. Lit only from outside, as it was, the flame
           was hidden by the very thing it had just made.

           It rides on the particle's own age rather than on a clock of its own:
           the puffs are born in a ripple down the barrel, so each one lights
           and cools on its own beat and the whole bank does not flare as one
           slab. Three tenths of a second, which is about how long the charge
           goes on burning outside the piece. */
        const hot = p.flash ? p.flash*Math.max(0, 1 - p.t/0.30) : 0;
        if(hot > 0.002){
          const q = hot*hot;                       // it cools fast, then lingers faintly
          p.m.material.color.setRGB(g + (1.00-g)*q, g + (0.66-g)*q, g + (0.22-g)*q);
          p.m.material.opacity *= 1 + 0.75*q;
        }else{
          p.m.material.color.setRGB(g, g*0.99, g*0.95);
        }
      }
    }
  }

  /* Everything here holds a position in the local frame, so it has to move with
     the world when the world moves — the same debt the spray and the timber
     owe. A bank of smoke lives the better part of half a minute, which is ample
     time to cross a rebase. */
  rebase(dx, dz){
    // the queue needs nothing: a shot not yet fired is placed off her hull at
    // the instant it goes, and by then she has moved with the world herself
    for(const p of this.live){ p.p.x -= dx; p.p.z -= dz; p.m.position.copy(p.p); }
    for(const b of this.shot){ b.p.x -= dx; b.p.z -= dz; b.m.position.copy(b.p); }
    // a lamp holds a position too, and lives long enough to cross a rebase
    for(const lp of this.lamps){ lp.L.position.x -= dx; lp.L.position.z -= dz; }
  }

  dispose(){
    for(const p of this.live){ this.group.remove(p.m); p.m.material.dispose(); }
    for(const b of this.shot) this.group.remove(b.m);
    this.live.length = 0; this.shot.length = 0;
    this.scene.remove(this.group);
  }
};
