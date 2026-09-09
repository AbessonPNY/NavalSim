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
  constructor(scene){
    this.scene = scene;
    this.group = new THREE.Group();
    scene.add(this.group);
    this.live = [];
    this._queue = [];
    this.onRecoil = null;      // wired by the page, so the hull answers back
    this._p = new THREE.Vector3();
    this._d = new THREE.Vector3();
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

  /* A BROADSIDE, gun by gun rather than all together.

     They were fired in ripple down the side, a beat apart, and not for show:
     a whole battery let off at one instant is a single shove, and it both
     sounds and looks like one object breaking. Rippled, the eye follows it
     along her side and reads a row of guns. Same argument as the three charges
     in the magazine, and the masts going over one after another. */
  broadside(muzzles, side, body, spec, wind){
    let n = 0;
    for(const g of muzzles){
      if(g.side !== side) continue;
      this._queue.push({ t:-n*0.09, g, body, spec, wind });
      n++;
    }
    return n;
  }

  /* One gun. `g` is a muzzle in the HULL's frame, so it is carried round by her
     heel and her heading before anything is drawn in the world. */
  fire(g, body, spec, wind){
    const k = Math.max(0.35, spec.L/60);       // a carronade is not a 32-pounder
    const smoke = Naval.powderTexture(), glow = Naval.glowTexture();

    // muzzle and the way it points, both carried into the world by her attitude
    this._p.copy(g.p).applyQuaternion(body.quat).add(body.pos);
    this._d.set(g.side, 0.06, 0).applyQuaternion(body.quat).normalize();
    const at = this._p.clone(), out = this._d.clone();
    // something to spread the jet about, square to the way it points
    const up = new THREE.Vector3(0,1,0);
    const sideV = new THREE.Vector3().crossVectors(out, up).normalize();

    /* FLASH. Gone in a twentieth of a second, which is the point — it is what
       makes the eye believe the smoke was thrown rather than released. It does
       NOT light the sky the way the magazine does: a gun is a bright thing in
       one place, not a second sun. */
    for(let i=0;i<3;i++){
      const s = this._sprite(glow, 0xffe2ae, true);
      this.live.push({
        m:s, t:-i*0.012, life:0.07 + i*0.03, kind:'flash',
        p:at.clone().addScaledVector(out, 0.6*k*(1+i)),
        v:out.clone().multiplyScalar(9*k), drag:6.0, lift:0,
        s0:(1.4 + i*0.9)*k, s1:(3.4 + i*1.6)*k });
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
        m:s, t:-i*0.018, life:5.5 + Math.random()*3.5, kind:'smoke',
        p:at.clone().addScaledVector(out, 0.4*k),
        v:out.clone().multiplyScalar((16 + Math.random()*14)*k)
            .addScaledVector(sideV, spread*4*k)
            .add(new THREE.Vector3(0, (Math.random()-0.2)*2.5*k, 0)),
        drag:3.4, lift:0.55*k, spin:(Math.random()-0.5)*1.1,
        s0:2.4*k, s1:(10 + Math.random()*5)*k });
    }

    /* THE BANK. The bulk of it: slow, wide, and living four times as long as
       the jet. This is what hangs over the water after she has gone by, and it
       is most of what one is looking at. */
    for(let i=0;i<10;i++){
      const s = this._sprite(smoke, 0xe8e3d8, false);
      const a = Math.random()*6.2832;
      this.live.push({
        m:s, t:0.06 + Math.random()*0.5, life:13 + Math.random()*9, kind:'smoke',
        p:at.clone().addScaledVector(out, (2 + Math.random()*7)*k)
            .add(new THREE.Vector3((Math.random()-0.5)*4*k,
                                   (Math.random()-0.4)*3*k,
                                   (Math.random()-0.5)*6*k)),
        v:out.clone().multiplyScalar((2 + Math.random()*5)*k)
            .add(new THREE.Vector3(Math.cos(a)*1.2*k, 1.0 + Math.random()*1.4,
                                   Math.sin(a)*1.2*k)),
        drag:1.5, lift:0.30*k, spin:(Math.random()-0.5)*0.5,
        s0:(3.4 + Math.random()*2.6)*k, s1:(15 + Math.random()*12)*k });
    }

    /* And she FEELS it. Not a shove that moves her — the arithmetic says
       otherwise and it is worth having the arithmetic say it — but a real
       impulse at a real height, which is a real heeling moment. A twelve-pound
       ball leaves at some 440 m/s, so with the gases behind it a gun throws
       about three thousand kilogramme-metres a second back through its
       trunnions, six metres above her centre of gravity. */
    if(this.onRecoil) this.onRecoil(g, out, 3000*k*k);
  }

  update(dt, wind){
    for(let i=this._queue.length-1; i>=0; i--){
      const q = this._queue[i];
      q.t += dt;
      if(q.t >= 0){ this.fire(q.g, q.body, q.spec, q.wind); this._queue.splice(i,1); }
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

      /* It relaxes towards the speed of the AIR, not towards nought. That one
         difference is what makes the bank drift down to leeward and the ship
         sail out from under her own smoke — the thing the whole effect is
         about — and it costs a line. Decayed to nought instead, every cloud
         would sit exactly where it was fired, tied to the water rather than to
         the wind, which is the one thing smoke never does. */
      const kd = Math.min(1, p.drag*dt);
      p.v.x += (wx - p.v.x)*kd;
      p.v.z += (wz - p.v.z)*kd;
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
        p.m.material.opacity = Math.min(1, u*9) * Math.pow(1-u, 1.3) * 0.92;
        const g = 0.90 - 0.20*u;
        p.m.material.color.setRGB(g, g*0.995, g*0.97);
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
  }

  dispose(){
    for(const p of this.live){ this.group.remove(p.m); p.m.material.dispose(); }
    this.live.length = 0;
    this.scene.remove(this.group);
  }
};
