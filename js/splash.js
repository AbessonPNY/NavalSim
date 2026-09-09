/* Water thrown.
 *
 * Something enters the sea and the sea has to go somewhere. That is the whole
 * of it: a splash is not an effect attached to an event, it is the volume of
 * water the object has just taken the place of, arriving somewhere else.
 *
 * Which settles the two numbers a burst is given, and they are different
 * numbers doing different jobs:
 *
 *   · HOW MUCH water — cubic metres. This is where weight comes in. A heavy
 *     body sinks deeper before the water can stop it, so it displaces more,
 *     and what one reads as "a big splash" is almost entirely quantity: the
 *     number of drops, the width of the sheet, how long it hangs.
 *   · HOW FAST it went in — metres per second. This sets how HIGH the water
 *     goes, and nothing else does. Drop a cannonball gently and a great deal
 *     of water moves a very little way.
 *
 * Weight enters the speed as well, but only through the back door: a light
 * body is stopped by the surface and its splash dies with it, while a heavy
 * one carries on as though the water were not there. So the throw is the
 * impact speed, tempered by how much the water manages to slow the thing down.
 *
 * Everything lives in one Points object with one draw call. Sprites with a
 * material each are perfectly good for a dozen puffs of smoke and quite wrong
 * for two hundred drops.
 */
window.Naval = window.Naval || {};

Naval.dropTexture = function(){
  if(Naval._dropTex) return Naval._dropTex;
  const s = 64, cv = document.createElement('canvas');
  cv.width = cv.height = s;
  const ctx = cv.getContext('2d');
  const g = ctx.createRadialGradient(s/2, s/2, 0, s/2, s/2, s/2);
  g.addColorStop(0.00, 'rgba(255,255,255,1)');
  g.addColorStop(0.35, 'rgba(255,255,255,0.72)');
  g.addColorStop(1.00, 'rgba(255,255,255,0)');
  ctx.fillStyle = g; ctx.fillRect(0, 0, s, s);
  const tex = new THREE.CanvasTexture(cv);
  tex.colorSpace = THREE.SRGBColorSpace;
  Naval._dropTex = tex;
  return tex;
};

Naval.Splash = class Splash {
  constructor(scene, stage){
    this.scene = scene;
    this.stage = stage;
    this.max = 2000;

    this.pos   = new Float32Array(this.max*3);
    this.size  = new Float32Array(this.max);
    this.alpha = new Float32Array(this.max);

    const g = new THREE.BufferGeometry();
    g.setAttribute('position', new THREE.BufferAttribute(this.pos, 3).setUsage(THREE.DynamicDrawUsage));
    g.setAttribute('size',     new THREE.BufferAttribute(this.size, 1).setUsage(THREE.DynamicDrawUsage));
    g.setAttribute('alpha',    new THREE.BufferAttribute(this.alpha, 1).setUsage(THREE.DynamicDrawUsage));
    g.setDrawRange(0, 0);

    this.uniforms = {
      uMap:  {value:Naval.dropTexture()},
      uColor:{value:new THREE.Color(0xeef5f8)},
      uScale:{value:600}
    };

    this.mat = new THREE.ShaderMaterial({
      uniforms:this.uniforms,
      transparent:true, depthWrite:false,
      vertexShader:`
        attribute float size;
        attribute float alpha;
        uniform float uScale;
        varying float vA;
        void main(){
          vA = alpha;
          vec4 mv = modelViewMatrix * vec4(position, 1.0);
          /* Real perspective on the point size, so a drop close to the lens is
             genuinely large. A constant size would put the whole sheet at one
             apparent distance and flatten it. */
          gl_PointSize = size * uScale / max(0.1, -mv.z);
          gl_Position = projectionMatrix * mv;
        }`,
      fragmentShader:`
        uniform sampler2D uMap;
        uniform vec3 uColor;
        varying float vA;
        void main(){
          vec4 t = texture2D(uMap, gl_PointCoord);
          gl_FragColor = vec4(uColor, t.a * vA);
        }`
    });

    this.points = new THREE.Points(g, this.mat);
    // its vertices are rewritten every frame, so no bound three computes stays true
    this.points.frustumCulled = false;
    this.points.renderOrder = 5;
    this.scene.add(this.points);

    this.live = [];
    for(let i=0;i<this.max;i++){
      this.live.push({ p:new THREE.Vector3(), v:new THREE.Vector3(),
                       s0:0, s1:0, t:0, life:0, y0:0, on:false });
    }
    this.n = 0;              // how many slots are in use, for a cheap scan
    this._cursor = 0;
  }

  /* A rolling cursor rather than a scan from nought. A big burst asks for two
     hundred and forty slots in one go, and starting each search at the top
     turned that into a quarter of a million comparisons in a single frame —
     for a pool that is very nearly empty. */
  _slot(){
    for(let k=0;k<this.max;k++){
      const i = (this._cursor + k) % this.max;
      const d = this.live[i];
      if(!d.on){ this._cursor = (i+1) % this.max; if(i >= this.n) this.n = i+1; return d; }
    }
    return null;             // full: the burst is simply smaller, never queued
  }

  /* `water` is cubic metres thrown, `speed` the closing speed in m/s.
     `at` is in the same local frame as everything else drawn. */
  burst(at, water, speed){
    water = Math.max(0, water);
    speed = Math.max(0, speed);
    if(water < 0.02 || speed < 0.4) return;

    // the size of the event, in metres: a cube root, since this is a volume
    const R = Math.pow(water, 1/3);

    /* The throw. Water leaves the rim of the cavity rather faster than the body
       went in — it is squeezed out of a closing gap, which is why a belly-flop
       stings.

       And it is scaled by the SIZE of the event, which is what stops a big ship
       looking like a model of one. Gravity fixes the only clock a splash has:
       water thrown at four metres a second is up and down in eight tenths of a
       second whatever it is next to, so beside a sixty-metre frigate it is a
       flicker, and the eye reads the flicker and says "small". Film has known
       this for a century — a miniature betrays itself by its water moving too
       quickly for its apparent size.

       The cure is Froude similitude, which is the real physics of it: for
       gravity-driven motion, geometrically similar flows have speed going as
       the square root of length. So the throw carries a factor √(R/Rref), and
       the rest follows on its own — the crown rises proportionally to R and
       hangs proportionally to √R, under ordinary gravity, with nothing faked.

       It sounds backwards to make a big splash FASTER when the complaint was
       that it looked too fast. It is not: absolute speed rises as √R while the
       size rises as R, so what is seen — speed against size — falls as 1/√R.
       A big splash is a slow splash, and this is the arithmetic that says so. */
    const RREF = 2.5;
    const froude = Math.sqrt(Math.max(0.35, R/RREF));
    /* And then held down to what a cavity that size can actually throw. The
       crown of a splash rises about as far as the cavity is wide — it is the
       same water, folded up — so a plume going three or four times its own
       radius into the air has stopped being water displaced and become a
       firework. The cap binds only in a heavy swell, which is exactly where it
       was wanted. */
    const vMax = Math.sqrt(2*9.81*1.05*R);
    const v0 = Math.min(vMax, (0.6 + speed*0.85) * froude);

    /* Many and small beats few and large. Spray is not a set of objects, it
       is a texture, and the eye reads it by its grain: too few sprites and one
       counts the dots. */
    const count = Math.max(8, Math.min(420, Math.round(water*11)));
    for(let i=0;i<count;i++){
      const d = this._slot();
      if(!d) break;

      /* A splash is a CROWN, not a fountain: the water leaves the rim of the
         cavity, so it goes up and OUTWARD at a steep angle, and the middle is
         comparatively empty. Straight up is what a garden hose does. */
      const sheet = i < count*0.16;
      const a = Math.random()*Math.PI*2;
      const e = sheet ? (0.15 + Math.random()*0.45) : (0.55 + Math.random()*0.85);
      const sp = v0 * (sheet ? 0.20 + Math.random()*0.35 : 0.35 + Math.random()*0.95);

      d.p.set(at.x + Math.cos(a)*R*0.5*Math.random(),
              at.y + Math.random()*R*0.3,
              at.z + Math.sin(a)*R*0.5*Math.random());
      d.v.set(Math.cos(a)*Math.cos(e)*sp, Math.sin(e)*sp, Math.sin(a)*Math.cos(e)*sp);

      /* The sheet is the white curtain at the foot of it — slow, wide, and
         gone quickly. The drops are what carry the height. */
      /* A sprite is not one raindrop, it is a CLUMP of spray — torn water and
         the air in it. Sized as a single drop it disappears at any distance and
         the whole plume reads as dust on the lens; sized as the whole event it
         reads as cotton wool, which is what the first cut of the sheet did. */
      /* Bounded in metres as well as scaled, and the bound is what keeps a
         big event from turning into a handful of boulders: torn water does not
         hold together past something like half a metre, whatever threw it. */
      d.s0 = sheet ? Math.min(2.2, R*0.34) : Math.min(0.45, R*(0.06 + Math.random()*0.10));
      d.s1 = sheet ? Math.min(6.0, R*1.00) : d.s0*1.9;
      // and it hangs for √R longer, which is the same similitude again
      d.life = (sheet ? 0.35 + Math.random()*0.3 : 0.55 + Math.random()*0.75)*froude;
      d.t = 0;
      /* The surface is sampled ONCE, here, and not per drop per frame. Three
         hundred drops asking the ocean where it is would cost more than the
         whole solver, and in the second a drop lives the sea beneath it has
         not moved enough to be worth it. */
      d.y0 = at.y;
      /* Air holds a small drop back hard and a large one hardly at all: drag
         goes with area and mass with volume, so what slows it goes as 1/size.
         Without this every clump decelerates alike and a great sheet of water
         fizzles out as quickly as a droplet, which is the miniature look again
         by another route. */
      d.k = 1.1 * Math.min(2.4, 0.35/Math.max(0.05, d.s0));
      d.on = true;
    }
  }

  /* Every live drop holds a position in the local frame, so it moves with
     the world when the world moves. Miss this and a rebase leaves the spray
     hanging a kilometre and a half astern. */
  rebase(dx, dz){
    for(let i=0;i<this.n;i++){
      const d = this.live[i];
      if(d.on){ d.p.x -= dx; d.p.z -= dz; }
    }
  }

  update(dt){
    if(dt <= 0) return;
    /* Spray is white because it is full of air, so it is as bright as whatever
       is lighting it — and at night that is very little. Taken off the sky's
       own horizon colour, which already carries the hour and the weather. */
    if(this.stage && this.stage.horizon){
      this.uniforms.uColor.value.copy(this.stage.horizon).multiplyScalar(1.12);
    }

    let w = 0, top = 0;
    for(let i=0;i<this.n;i++){
      const d = this.live[i];
      if(!d.on) continue;
      d.t += dt;
      const u = d.t/d.life;
      /* Gone when it has fallen back to where it came from, or when its time
         is up — whichever finds it first. Water that lands is water that has
         stopped being spray. */
      if(u >= 1 || (d.v.y < 0 && d.p.y < d.y0 - 0.35)){ d.on = false; continue; }

      d.v.y -= 9.81*dt;
      d.v.multiplyScalar(Math.max(0, 1 - d.k*dt));   // see the drag note above
      d.p.addScaledVector(d.v, dt);

      this.pos[w*3+0] = d.p.x; this.pos[w*3+1] = d.p.y; this.pos[w*3+2] = d.p.z;
      this.size[w] = d.s0 + (d.s1 - d.s0)*Math.pow(u, 0.6);
      /* Up fast, out slowly: spray appears at once and thins as it falls back.
         Kept well under one on purpose — a plume is dozens of these overlapping,
         and at full opacity they stack into a solid white body instead of
         building up into something one can see through. */
      this.alpha[w] = Math.min(1, u*12) * Math.pow(1 - u, 0.9) * 0.45;
      w++;
      top = i+1;
    }
    this.n = top;

    const g = this.points.geometry;
    g.setDrawRange(0, w);
    g.attributes.position.needsUpdate = true;
    g.attributes.size.needsUpdate = true;
    g.attributes.alpha.needsUpdate = true;
    this.points.visible = w > 0;
  }

  dispose(){
    this.scene.remove(this.points);
    this.points.geometry.dispose();
    this.mat.dispose();
  }
};
