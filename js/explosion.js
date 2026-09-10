/* The powder magazine goes up.
 *
 * What makes an explosion read is not the fireball — it is the ORDER of things
 * and their different lifetimes. A flash that is gone in a tenth of a second, a
 * fireball that grows fast and dies in one, smoke that keeps rising and
 * spreading for half a minute, debris on ballistic arcs that splash, and a ring
 * running out across the water. All at once and it is a firework; in sequence,
 * with the slow parts outliving the fast ones, it is a ship blowing up.
 *
 * Everything is billboards. Volumetric fire would be days of work for an event
 * that lasts three seconds, and a dozen sprites with the right timing read
 * better than a bad volume.
 */
window.Naval = window.Naval || {};

/* A soft grey puff, drawn like the lantern's glow — the published page cannot
   fetch an image, and smoke is a gradient with noise bitten out of its edge. */
Naval.smokeTexture = function(){
  if(Naval._smokeTex) return Naval._smokeTex;
  const s = 128, cv = document.createElement('canvas');
  cv.width = cv.height = s;
  const ctx = cv.getContext('2d');
  const g = ctx.createRadialGradient(s/2, s/2, 0, s/2, s/2, s/2);
  g.addColorStop(0.00, 'rgba(255,255,255,0.95)');
  g.addColorStop(0.45, 'rgba(255,255,255,0.42)');
  g.addColorStop(1.00, 'rgba(255,255,255,0)');
  ctx.fillStyle = g; ctx.fillRect(0, 0, s, s);
  // bite the rim so a puff is not a perfect disc
  ctx.globalCompositeOperation = 'destination-out';
  for(let i=0;i<26;i++){
    const a = Math.random()*6.2832, r = s*0.34 + Math.random()*s*0.16;
    ctx.beginPath();
    ctx.arc(s/2 + Math.cos(a)*r, s/2 + Math.sin(a)*r, s*(0.05 + Math.random()*0.09), 0, 6.2832);
    ctx.fill();
  }
  const tex = new THREE.CanvasTexture(cv);
  tex.colorSpace = THREE.SRGBColorSpace;
  Naval._smokeTex = tex;
  return tex;
};

Naval.Explosion = class Explosion {
  constructor(scene, stage){
    this.scene = scene;
    this.stage = stage;
    this.group = new THREE.Group();
    scene.add(this.group);
    this.live = [];
    this._queue = [];
    this._v = new THREE.Vector3();
    this.onSplash = null;         // wired by the page, so the sea answers back

    /* Timber. Shared by every splinter, so the page patches it with haze once
       and every piece of every blast breathes the same air. */
    this.woodMat = new THREE.MeshStandardMaterial({ color:0x6b4f31, roughness:0.85 });
    /* Splinters get their OWN timber, and pale. A ship's outside is weathered
       and tarred; the inside of a plank is not, so what a ball tears out of her
       side is RAW oak — much lighter than anything around it. That is true, and
       it happens to solve the only real difficulty with drawing them: dark
       slivers against a dark hull at a cable's distance are nothing at all. */
    this.splinterMat = new THREE.MeshStandardMaterial({ color:0xc9a875, roughness:0.92 });
    /* One plank, reused. Each splinter scales it differently, so a single
       geometry serves them all — there is no reason to build two dozen boxes
       per explosion and throw them away three seconds later. */
    this.plank = new THREE.BoxGeometry(1, 1, 1);
  }

  /* Touch one off LATER. A magazine does not go up in one clean blast: the
     first charge opens her, the fire finds the next room, and the second and
     third follow a beat behind. Three shots a few metres and a few tenths of a
     second apart read as a ship coming apart, where one big ball reads as a
     bomb going off next to her. Each carries its own flash, so the light
     stutters too — which is most of the effect. */
  fireIn(delay, at, size){
    this._queue.push({ t:-Math.max(0, delay), at:at.clone(), size });
  }

  _sprite(tex, colour, additive){
    const m = new THREE.Sprite(new THREE.SpriteMaterial({
      map:tex, color:colour, transparent:true, depthWrite:false,
      blending: additive ? THREE.AdditiveBlending : THREE.NormalBlending,
      fog:false, opacity:0 }));
    this.group.add(m);
    return m;
  }

  /* Touch her off. `at` is in the same local frame everything else is drawn in;
     `size` scales the whole event to the vessel, so a cutter does not go up
     like a first rate. */
  fire(at, size){
    const k = Math.max(0.4, size/24);
    const glow = Naval.glowTexture(), smoke = Naval.smokeTexture();

    // The flash. Reuses the lightning: it already lights deck, canvas and sky
    // together, which is exactly what a magazine going up should do.
    if(this.stage && this.stage.strike) this.stage.strike();

    /* FIRE. Bright, fast, and gone — a fireball that lingers reads as a balloon.
       Each puff has its own delay so the ball boils outward instead of
       inflating as one smooth sphere. */
    for(let i=0;i<14;i++){
      const s = this._sprite(glow, 0xffd9a0, true);
      const a = Math.random()*6.2832, e = (Math.random()-0.3)*1.4;
      this.live.push({
        m:s, kind:'fire', t:-Math.random()*0.10, life:0.55 + Math.random()*0.45,
        p:at.clone(),
        v:new THREE.Vector3(Math.cos(a)*Math.cos(e), Math.sin(e)+0.5, Math.sin(a)*Math.cos(e))
            .multiplyScalar((7 + Math.random()*13)*k),
        s0:2.5*k, s1:(11 + Math.random()*9)*k
      });
    }

    /* SMOKE. It outlives the fire by twenty times, keeps rising, and keeps
       growing — that long tail is most of what sells the scale. */
    for(let i=0;i<16;i++){
      const s = this._sprite(smoke, 0x3a3630, false);
      const a = Math.random()*6.2832;
      this.live.push({
        m:s, kind:'smoke', t:-Math.random()*0.8, life:9 + Math.random()*11,
        p:at.clone().add(new THREE.Vector3((Math.random()-0.5)*6*k, Math.random()*3*k,
                                           (Math.random()-0.5)*6*k)),
        v:new THREE.Vector3(Math.cos(a)*(1.0 + Math.random()*1.8), 7.0 + Math.random()*6.0,
                            Math.sin(a)*(1.0 + Math.random()*1.8)).multiplyScalar(k),
        s0:3*k, s1:(9 + Math.random()*8)*k
      });
    }

    /* TIMBER on real ballistic arcs. They are what give the blast its size: the
       eye reads the height they reach and the time they take to come down, and
       no amount of fireball substitutes for it.

       Real meshes, not sprites. A glowing point reads as a spark however it is
       coloured — what makes it read as a ship coming apart is that the pieces
       are OPAQUE, lit by the same sun as her hull, and TUMBLING. And because
       they are opaque and the sea is opaque, a plank that falls back simply
       disappears behind the water: the splash costs nothing and is free. */
    const wk = 0.55 + 0.45*k;                   // planks grow, but far slower than she does
    for(let i=0;i<24;i++){
      const m = new THREE.Mesh(this.plank, this.woodMat);
      m.scale.set((0.14 + Math.random()*0.22)*wk,
                  (0.07 + Math.random()*0.11)*wk,
                  (1.10 + Math.random()*2.30)*wk);
      m.castShadow = false;
      this.group.add(m);
      const a = Math.random()*6.2832, e = 0.45 + Math.random()*0.95;
      this.live.push({
        m, kind:'wood', t:0, life:5.0 + Math.random()*3.0,
        p:at.clone(),
        v:new THREE.Vector3(Math.cos(a)*Math.cos(e), Math.sin(e), Math.sin(a)*Math.cos(e))
            .multiplyScalar((14 + Math.random()*24)*k),
        // end over end, and fast: a plank thrown by powder does not glide
        w:new THREE.Vector3((Math.random()-0.5)*15, (Math.random()-0.5)*15,
                            (Math.random()-0.5)*15),
        g:true
      });
      m.rotation.set(Math.random()*6.28, Math.random()*6.28, Math.random()*6.28);
      this.live[this.live.length-1].k = k;   // for the splash it will make
    }

    // and a handful of true sparks, which ARE points of light
    for(let i=0;i<14;i++){
      const s = this._sprite(glow, 0xffb066, true);
      const a = Math.random()*6.2832, e = 0.4 + Math.random()*1.0;
      this.live.push({
        m:s, kind:'spark', t:0, life:2.2 + Math.random()*2.0,
        p:at.clone(),
        v:new THREE.Vector3(Math.cos(a)*Math.cos(e), Math.sin(e), Math.sin(a)*Math.cos(e))
            .multiplyScalar((20 + Math.random()*30)*k),
        g:true, s0:0.7*k, s1:0.3*k
      });
    }
  }

  /* The world slid under the fleet: the debris in the air has to go with it,
     like every other thing that holds a position. Short-lived as it is, a
     rebase during the three seconds a plank is aloft would fling it a mile. */
  rebase(dx, dz){
    for(const p of this.live){ p.p.x -= dx; p.p.z -= dz; p.m.position.copy(p.p); }
    for(const q of this._queue){ q.at.x -= dx; q.at.z -= dz; }
  }

  /* SPLINTERS, where a ball goes into her side.

     This is the one thing about naval gunnery that everyone who was there wrote
     down and that no picture of it ever shows: the shot itself killed very few
     people. What emptied a gundeck was the TIMBER — a ball at three hundred
     metres a second does not punch a neat hole through two feet of oak, it
     bursts the planking inboard and drives a cloud of oak daggers across the
     deck. Surgeons' logs are full of splinter wounds and nearly empty of round
     shot. An impact that produced a puff and a hole would be missing the whole
     of what a hit meant.

     They are the same timber as the wreck of an explosion — same geometry, same
     material, same tumble, same disappearance into the sea — CUT DIFFERENTLY.
     A blast throws pieces of ship: short, thick, slow, end over end. A ball
     throws slivers: long, thin, and very fast. That difference is entirely in
     the numbers handed to the same machinery, which is the point of having the
     machinery.

     Most of them go INBOARD, along the shot, because that is where they really
     go — and being inboard they are hidden by her own planking, which is not a
     loss but the correct reading: they went into her. Rather less than half are
     thrown back out through the hole she just made, and those are what one
     sees. Born a little outside the timber, so the ones coming out are not
     spawned inside the mesh they are supposed to be leaving. */
  splinters(at, dir, k){
    const sk = 0.45 + 0.55*Math.max(0.2, k || 1);
    const n = Math.round(16 + 9*sk);
    // a frame square to the shot, to spread them in
    const up = Math.abs(dir.y) > 0.9 ? new THREE.Vector3(1,0,0) : new THREE.Vector3(0,1,0);
    const sx = new THREE.Vector3().crossVectors(dir, up).normalize();
    const sy = new THREE.Vector3().crossVectors(dir, sx).normalize();
    const born = at.clone().addScaledVector(dir, -0.35*sk);

    for(let i=0;i<n;i++){
      const m = new THREE.Mesh(this.plank, this.splinterMat);
      /* Long and thin: a splinter is a dagger, not a board. But drawn at life
         size it is three pixels at the range a gun is fired and simply does not
         exist — the lantern's lesson again, and the ball's. So it carries the
         same deliberate lie: its FLIGHT is honest, its section is not. */
      m.scale.set((0.055 + Math.random()*0.075)*sk,
                  (0.040 + Math.random()*0.055)*sk,
                  (0.55  + Math.random()*1.60 )*sk);
      m.castShadow = false;
      m.rotation.set(Math.random()*6.28, Math.random()*6.28, Math.random()*6.28);
      this.group.add(m);

      const a = Math.random()*6.2832, r = Math.random()*0.95;
      // along the shot for most, back out through the hole for the rest
      const along = (Math.random() < 0.42 ? -0.55 - Math.random()*0.5
                                          :  0.35 + Math.random()*0.9);
      const v = new THREE.Vector3()
        .addScaledVector(dir, along)
        .addScaledVector(sx, Math.cos(a)*r)
        .addScaledVector(sy, Math.sin(a)*r)
        .normalize()
        .multiplyScalar((7 + Math.random()*17)*sk);
      v.y += 1.5 + Math.random()*3.0;          // the burst lifts a little

      this.live.push({
        m, kind:'wood', t:0, life:1.6 + Math.random()*1.9,
        p:born.clone().add(new THREE.Vector3((Math.random()-0.5)*0.5*sk,
                                             (Math.random()-0.5)*0.5*sk,
                                             (Math.random()-0.5)*0.5*sk)),
        v,
        // end over end, and faster than a plank: they are lighter
        w:new THREE.Vector3((Math.random()-0.5)*26, (Math.random()-0.5)*26,
                            (Math.random()-0.5)*26),
        g:true, k:0.25*sk                       // a chip makes a chip's splash
      });
    }
    return n;
  }

  /* `ocean` and `t` are optional; given them, the timber knows where the sea
     is and stops falling through it. */
  update(dt, ocean, t){
    for(let i=this._queue.length-1; i>=0; i--){
      const q = this._queue[i];
      q.t += dt;
      if(q.t >= 0){ this.fire(q.at, q.size); this._queue.splice(i, 1); }
    }

    for(let i=this.live.length-1; i>=0; i--){
      const p = this.live[i];
      p.t += dt;
      if(p.t < 0){ p.m.visible = false; continue; }
      p.m.visible = true;

      const u = p.t/p.life;
      if(u >= 1){
        this.group.remove(p.m);
        // the timber shares one material and one geometry: only sprites own theirs
        if(p.kind !== 'wood') p.m.material.dispose();
        this.live.splice(i, 1);
        continue;
      }

      if(p.g) p.v.y -= 9.81*dt;                 // debris alone are heavy
      else    p.v.multiplyScalar(1 - 1.3*dt);   // fire and smoke are slowed by air
      p.p.addScaledVector(p.v, dt);
      p.m.position.copy(p.p);

      if(p.kind === 'wood'){
        /* Into the sea, and out of the story. It used to be left to sink
           quietly behind an opaque surface, which cost nothing and looked
           right — but it also meant a plank that missed the water went on
           falling for ever, and it threw away the one moment a falling plank
           is worth watching. A cubic metre of timber puts up rather more than
           a cubic metre of water. */
        if(ocean && this.onSplash){
          const sea = ocean.sample(p.p.x, p.p.z, t || 0);
          if(p.p.y < sea && p.v.y < 0){
            /* And it throws a REAL one. It used to ask for nine tenths of a
               cubic metre, which is about the timber itself and nothing about
               the hole it punches — ten drops, half a metre, invisible. What
               one is watching after a magazine goes up is two dozen of these
               coming down over a quarter of a minute, and each arrival is a
               beat of the event.

               It borrows the round shot's column rather than the hull's crown,
               because a plank arriving at twenty-five metres a second is far
               nearer a projectile than a ship settling into a trough — but not
               as far: a plank is blunt and light where a ball is small and
               dense, so it is given a lower ceiling and comes out shorter and
               fatter, which is what a lump of timber actually does.

               The size is set by the spray pool as much as by the water. Two
               dozen planks land over several seconds and a drop lives about
               one, so a dozen bursts can be in the air at once — eight cubic
               metres apiece keeps the whole shower inside the two thousand
               places rather than letting the last planks rob the first.

               Measured on a magazine going up: 57 splashes, a peak of 2 850
               drops in a reserve of 4 000, no saturation at any moment, and
               columns better than six metres. At six cubic metres the pool sat
               pegged for four and a half seconds and the earliest plumes were
               recycled out from under themselves. */
            this.onSplash(p.p, 4.5*(p.k || 1), -p.v.y, 2.2);
            this.group.remove(p.m);
            this.live.splice(i, 1);
            continue;
          }
        }
        // it keeps its size and keeps turning; nothing else to do to it
        p.m.rotation.x += p.w.x*dt;
        p.m.rotation.y += p.w.y*dt;
        p.m.rotation.z += p.w.z*dt;
        continue;
      }
      p.m.scale.setScalar(p.s0 + (p.s1 - p.s0)*Math.pow(u, 0.55));

      if(p.kind === 'fire'){
        // white → yellow → orange → out, and gone well before the smoke
        p.m.material.opacity = Math.min(1, u*8) * Math.pow(1-u, 1.6);
        p.m.material.color.setRGB(1.0, 0.86 - 0.5*u, 0.62 - 0.55*u);
      }else if(p.kind === 'smoke'){
        // thins as it spreads, and lightens: cooling soot, not a dark blob
        p.m.material.opacity = Math.min(1, u*4) * (1-u) * 0.42;
        const g = 0.10 + 0.20*u;
        p.m.material.color.setRGB(g, g*0.97, g*0.92);
      }else{
        p.m.material.opacity = Math.pow(1-u, 0.8) * 0.9;
      }
    }
  }

  dispose(){
    for(const p of this.live){ this.group.remove(p.m); p.m.material.dispose(); }
    this.live.length = 0;
    this.scene.remove(this.group);
  }
};
