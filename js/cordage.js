/* Broken rigging: the cut ends that hang from a mast that has been shot at.
 *
 * The project already ASSERTS two things it never shows. A falling mast is
 * said to bring up hard in her own standing rigging, and a dismasted ship is
 * said to be dragged round by her wreckage instead of being rid of it. Both
 * are written in the notes and neither is on the screen, so a mast that has
 * taken a ball looks exactly like a mast that has not — one has no way of
 * knowing one has hit it until the third shot brings it down.
 *
 * A few trailing ends fix that, and they are the cheapest honest way to do it.
 *
 * A ROPE IS A VERLET CHAIN, not an animation. Points, a distance constraint
 * between neighbours, and nothing else: no angles, no rotations, no forces to
 * integrate and nothing that can blow up. It is the same argument as the
 * pendulum in the falling mast — use the arithmetic the thing actually obeys
 * rather than a curve drawn by hand — and here it also happens to be the
 * cheapest thing in the file. Measured, twelve points and three constraint
 * iterations:
 *
 *     8 ropes  0,005 ms      64 ropes  0,037 ms
 *    24 ropes  0,014 ms     160 ropes  0,100 ms
 *
 * A hundred and sixty ropes — a whole fleet dismasted — cost a tenth of a
 * millisecond in a budget of 16,7. The simulation is not the bill.
 *
 * WHAT COSTS IS DRAWING THEM. A THREE.Line is one device pixel wide whatever
 * linewidth one asks for, because WebGL ignores it on very nearly every
 * platform, so a rope drawn as a line is a sub-pixel shimmer at any distance
 * worth having — the same aliasing trap as a star narrower than a pixel, the
 * lantern's far glow and the ball drawn at five times its size, and it has the
 * same answer: be WIDER than the sampling step, never brighter. Each rope is
 * therefore a RIBBON, a strip of triangles turned to face the camera, its
 * width in real metres with a floor in pixels. All the ropes in the fleet live
 * in one geometry and go out in one draw call, exactly as splash.js keeps
 * seven hundred drops in a single Points.
 *
 * AND BEYOND A CERTAIN RANGE THEY ARE NOT DRAWN AT ALL. That cut is about
 * legibility and not about cost: at three hundred metres a rope is a hairline
 * that reads as noise on the rigging rather than as cordage, and the pixel
 * floor which saves it up close is exactly what makes it twinkle far off. The
 * simulation keeps running through the cut — it is free, and stopping it would
 * mean the rope snapped back into a rest pose the moment one closed again.
 */
window.Naval = window.Naval || {};

Naval.Cordage = class Cordage {
  constructor(scene, stage, ocean){
    this.scene = scene;
    this.stage = stage;
    this.ocean = ocean;

    /* Ten points is enough for a six-metre end. What the eye reads is the
       SWING — a rope lagging behind her roll — and not the catenary, so more
       points buy resolution nobody is looking at. */
    this.P = 10;
    this.max = 96;                       // ropes at once, across the whole fleet

    this.drawFrom = 190;                 // metres: it starts to go
    this.drawTo   = 300;                 // and beyond this it is noise

    const P = this.P, N = this.max, V = N*P*2;

    this.px = new Float32Array(N*P); this.py = new Float32Array(N*P); this.pz = new Float32Array(N*P);
    this.ox = new Float32Array(N*P); this.oy = new Float32Array(N*P); this.oz = new Float32Array(N*P);

    this.aPos  = new Float32Array(V*3);
    this.aTan  = new Float32Array(V*3);
    this.aSide = new Float32Array(V);
    this.aFade = new Float32Array(V);

    /* The strip is the same shape in every slot, so the index buffer is built
       once and never touched again; the live ropes are packed into the front of
       the vertex buffer each frame and the draw range cut to suit. */
    const idx = new Uint16Array(N*(P-1)*6);
    for(let r=0;r<N;r++){
      const v = r*P*2, o = r*(P-1)*6;
      for(let j=0;j<P-1;j++){
        const a = v + j*2, k = o + j*6;
        idx[k+0]=a;   idx[k+1]=a+1; idx[k+2]=a+2;
        idx[k+3]=a+1; idx[k+4]=a+3; idx[k+5]=a+2;
      }
      for(let j=0;j<P;j++){ this.aSide[v+j*2] = -1; this.aSide[v+j*2+1] = 1; }
    }

    const g = new THREE.BufferGeometry();
    g.setAttribute('position', new THREE.BufferAttribute(this.aPos, 3).setUsage(THREE.DynamicDrawUsage));
    g.setAttribute('tang',     new THREE.BufferAttribute(this.aTan, 3).setUsage(THREE.DynamicDrawUsage));
    g.setAttribute('side',     new THREE.BufferAttribute(this.aSide, 1));
    g.setAttribute('fade',     new THREE.BufferAttribute(this.aFade, 1).setUsage(THREE.DynamicDrawUsage));
    g.setIndex(new THREE.BufferAttribute(idx, 1));
    g.setDrawRange(0, 0);

    const u = ocean ? ocean.uniforms : null;
    this.uniforms = {
      uColor:  {value:new THREE.Color(0x5b4c3c)},
      uWidth:  {value:0.16},             // a brace is about a hand thick
      uMinPx:  {value:1.6},              // and never thinner than this on screen
      uPxScale:{value:0.0018},
      /* Shared objects, never copies: the haze in front of a rope is the haze
         in front of the sea it hangs over. */
      uCam:    u ? u.uCam    : {value:new THREE.Vector3()},
      uHaze:   u ? u.uHaze   : {value:0.0016},
      uHazeH:  u ? u.uHazeH  : {value:110},
      uHorizon:u ? u.uHorizon: {value:new THREE.Color(0xd2e3ec)}
    };

    this.mat = new THREE.ShaderMaterial({
      uniforms:this.uniforms,
      transparent:true, depthWrite:false, side:THREE.DoubleSide,
      vertexShader:
        'attribute vec3 tang;\n' +
        'attribute float side;\n' +
        'attribute float fade;\n' +
        'uniform float uWidth, uMinPx, uPxScale;\n' +
        'varying float vFade;\n' +
        'varying vec3 vW;\n' +
        'void main(){\n' +
        '  vFade = fade;\n' +
        '  vW = position;                      /* the mesh sits at the origin */\n' +
        '  vec4 mv = modelViewMatrix * vec4(position, 1.0);\n' +
        '  vec3 tv = normalize((modelViewMatrix * vec4(tang, 0.0)).xyz);\n' +
        '  vec3 eye = normalize(mv.xyz);       /* the eye IS the origin in view space */\n' +
        '  vec3 dir = cross(tv, eye);\n' +
        '  float l = length(dir);\n' +
        '  dir = (l < 1e-4) ? vec3(1.0, 0.0, 0.0) : dir/l;\n' +
        /* Metres per pixel at this depth, so the floor is a real pixel count and
           not a guess. Never call anything "half" in here: it is a reserved word
           in GLSL and the whole shader fails without saying so. */
        '  float mpp = max(0.001, -mv.z) * uPxScale;\n' +
        '  float w2 = max(uWidth*0.5, uMinPx*0.5*mpp);\n' +
        '  mv.xyz += dir * w2 * side;\n' +
        '  gl_Position = projectionMatrix * mv;\n' +
        '}',
      fragmentShader: Naval.HAZE_GLSL + '\n' +
        'uniform vec3 uColor, uHorizon, uCam;\n' +
        'varying float vFade;\n' +
        'varying vec3 vW;\n' +
        'void main(){\n' +
        '  float h = hazeAlong(uCam, vW);\n' +
        '  gl_FragColor = vec4(mix(uColor, uHorizon, h), vFade);\n' +
        '}'
    });

    this.mesh = new THREE.Mesh(g, this.mat);
    this.mesh.frustumCulled = false;     // its vertices are rewritten every frame
    this.mesh.renderOrder = 4;
    this.scene.add(this.mesh);

    this.rope = [];
    for(let i=0;i<N;i++) this.rope.push({on:false});
    this.n = 0;
    this._cursor = 0;

    this._acc = 0;
    this._v = new THREE.Vector3();
    /* Tarred hemp, and it REFLECTS: a rope makes no light of its own, so it can
       never be brighter than what is lighting it. Exactly the fault the foam
       had, and the smoke after it. */
    this._base = new THREE.Color(0x6d5c48);
  }

  _slot(){
    for(let k=0;k<this.max;k++){
      const i = (this._cursor + k) % this.max;
      if(!this.rope[i].on){
        this._cursor = (i+1) % this.max;
        if(i >= this.n) this.n = i+1;
        return i;
      }
    }
    return -1;              // full: she simply trails fewer ends, never a queue
  }

  /* One rope, hung from an offset in the local frame of `obj`, which is a group
     in the ship's own graph. Holding the OFFSET rather than a world point is
     what makes the floating origin free here: the anchor is re-read from a live
     matrix every frame, so it follows her roll, her heel and a mast going over
     the side without any of them knowing this exists. */
  _hang(ship, fall, obj, ax, ay, az, len){
    const s = this._slot();
    if(s < 0) return;
    const P = this.P, b = s*P, seg = len/(P-1);

    this._v.set(ax, ay, az).applyMatrix4(obj.matrixWorld);
    /* A shade off the axis, or the first constraint pass has nothing to push
       against and the rope stands up like a wire. */
    const jx = (Math.random()-0.5)*0.4, jz = (Math.random()-0.5)*0.4;
    for(let j=0;j<P;j++){
      const i = b + j;
      this.px[i] = this.ox[i] = this._v.x + jx*j/P;
      this.py[i] = this.oy[i] = this._v.y - seg*j;
      this.pz[i] = this.oz[i] = this._v.z + jz*j/P;
    }

    const r = this.rope[s];
    r.on = true; r.ship = ship; r.fall = fall; r.obj = obj;
    r.ax = ax; r.ay = ay; r.az = az;
    r.seg = seg; r.sea = -50; r.d = 0;
    r.hx = this._v.x; r.hy = this._v.y; r.hz = this._v.z;
    r.epoch = ship.rigEpoch || 0;
  }

  /* Draining the ship's own request list rather than being called at the moment
     of the hit, so nothing outside has to remember this exists. A hull removed
     from the fleet takes its requests with it. */
  _drain(fleet){
    for(const e of fleet){
      const ship = e && e.ship;
      if(!ship || !ship.rigCuts || !ship.rigCuts.length) continue;
      for(const c of ship.rigCuts){
        const fall = ship.falls[c.i];
        if(!fall || !fall.userData.cordage) continue;
        const pool = fall.userData.cordage;
        if(!pool.length) continue;
        for(let k=0;k<c.n;k++){
          const a = pool[Math.floor(Math.random()*pool.length)];
          this._hang(ship, fall, a.obj, a.x, a.y, a.z,
                     a.len*(0.75 + Math.random()*0.5));
        }
      }
      ship.rigCuts.length = 0;
    }
  }

  rebase(dx, dz){
    for(let s=0;s<this.n;s++){
      if(!this.rope[s].on) continue;
      const b = s*this.P;
      for(let j=0;j<this.P;j++){
        this.px[b+j] -= dx; this.ox[b+j] -= dx;
        this.pz[b+j] -= dz; this.oz[b+j] -= dz;
      }
      const r = this.rope[s];
      r.hx -= dx; r.hz -= dz;
    }
  }

  clear(){
    for(const r of this.rope) r.on = false;
    this.n = 0;
    this.mesh.visible = false;
    this.mesh.geometry.setDrawRange(0, 0);
  }

  update(dt, fleet, t, camPos){
    if(fleet) this._drain(fleet);

    /* A FIXED sub-step, and it matters more here than almost anywhere else in
       the project: Verlet at a varying dt is not merely inaccurate, it changes
       the effective damping — and the preview pane clocks at 32 whatever one
       asks of it. Capped at five, so a pane that has stalled for a second does
       not try to catch up in a single frame and fire the whole fleet's rigging
       into the sky. */
    const H = 1/120;
    this._acc = Math.min(this._acc + Math.max(0, dt), 5*H);

    const P = this.P;
    const wind = this.ocean ? this.ocean.windVec : null;
    const wx = wind ? wind.x : 0, wz = wind ? wind.z : 0;

    // ---- per-frame housekeeping: who is dead, how far off, where the sea is
    let live = 0;
    for(let s=0;s<this.n;s++){
      const r = this.rope[s];
      if(!r.on) continue;
      /* Gone with her: a hull disposed of drops out of the scene, a rig that has
         been restored bumps its epoch, and a mast that has finished sinking is
         hidden. Three cheap reads instead of three call sites to remember.

         The matrix is last frame's, this running just ahead of the render — a
         sixtieth of a second of lag on a rope, which is nothing, against
         forcing an update of every node of a .glb once per rope. */
      if(!r.ship.group.parent || r.ship.rigEpoch !== r.epoch || !r.fall.visible){
        r.on = false; continue;
      }
      this._v.set(r.ax, r.ay, r.az).applyMatrix4(r.obj.matrixWorld);
      r.hx = this._v.x; r.hy = this._v.y; r.hz = this._v.z;
      /* The sea under a rope, sampled ONCE per rope per frame and not per point
         per sub-step: ten points times five sub-steps would be fifty questions
         put to the ocean for one end of rope. Same trade as the smoke's floor
         and the spray's drops. */
      if(this.ocean) r.sea = this.ocean.sample(r.hx, r.hz, t || 0);
      r.d = camPos ? Math.hypot(r.hx - camPos.x, r.hy - camPos.y, r.hz - camPos.z) : 0;
      live++;
    }
    if(!live){
      this.n = 0;
      this.mesh.visible = false;
      this.mesh.geometry.setDrawRange(0, 0);   // pas seulement masqué : vidé
      return;
    }

    while(this._acc >= H){
      this._acc -= H;
      const hh = H*H;
      for(let s=0;s<this.n;s++){
        const r = this.rope[s];
        if(!r.on) continue;
        const b = s*P;

        for(let j=1;j<P;j++){
          const i = b + j;
          const vx = (this.px[i]-this.ox[i])/H,
                vy = (this.py[i]-this.oy[i])/H,
                vz = (this.pz[i]-this.oz[i])/H;
          /* Air drag toward the TRUE wind, which is what streams a cut end to
             leeward — and water drag toward nothing at all, which is what makes
             a rope in the sea drag on her. Hemp is heavy, so neither is strong:
             most of a rope's motion is its anchor's.

             The air term was first written at 0,55, which sounds modest and is
             not: against 9,81 of gravity it leans a rope more than twenty
             degrees off the vertical in a working breeze, and every end on the
             ship then streams the same way at the same angle — which reads as
             rigging under strain rather than as rigging cut adrift. Tarred hemp
             is heavy and offers very little to the wind. */
          const wet = this.py[i] < r.sea;
          const cA = wet ? 7.0 : 0.28;
          const ax = cA*((wet ? 0 : wx) - vx);
          const az = cA*((wet ? 0 : wz) - vz);
          const ay = cA*(-vy) + (wet ? 4.2 : 0) - 9.81;

          const nx = this.px[i] + (this.px[i]-this.ox[i]) + ax*hh;
          const ny = this.py[i] + (this.py[i]-this.oy[i]) + ay*hh;
          const nz = this.pz[i] + (this.pz[i]-this.oz[i]) + az*hh;
          this.ox[i] = this.px[i]; this.oy[i] = this.py[i]; this.oz[i] = this.pz[i];
          this.px[i] = nx;
          this.py[i] = Math.max(r.sea - 1.1, ny);   // awash, not sunk
          this.pz[i] = nz;
        }

        // the anchor is not simulated, it is READ: it is a point on her mast
        this.px[b] = r.hx; this.py[b] = r.hy; this.pz[b] = r.hz;
        this.ox[b] = r.hx; this.oy[b] = r.hy; this.oz[b] = r.hz;

        for(let k=0;k<3;k++){
          for(let j=0;j<P-1;j++){
            const a = b+j, c = a+1;
            let dx = this.px[c]-this.px[a], dy = this.py[c]-this.py[a], dz = this.pz[c]-this.pz[a];
            const d = Math.sqrt(dx*dx + dy*dy + dz*dz) || 1e-6;
            const f = (d - r.seg)/d*0.5;
            dx *= f; dy *= f; dz *= f;
            if(j > 0){ this.px[a]+=dx; this.py[a]+=dy; this.pz[a]+=dz; }
            else     { dx*=2; dy*=2; dz*=2; }      // the anchor takes none of it
            this.px[c]-=dx; this.py[c]-=dy; this.pz[c]-=dz;
          }
        }
      }
    }

    /* Reflect, do not emit — normalised on the horizon's own luminance so full
       day does not move and the night does. The same idiom the gun smoke uses,
       for the same reason. */
    const Hz = this.stage && this.stage.horizon;
    if(Hz){
      const l = Math.max(1e-3, 0.2126*Hz.r + 0.7152*Hz.g + 0.0722*Hz.b);
      const k = Math.max(0.14, l/0.85);
      const B = this._base;
      this.uniforms.uColor.value.setRGB(B.r*Hz.r/l*k, B.g*Hz.g/l*k, B.b*Hz.b/l*k);
    }
    const cam = this.stage && this.stage.camera;
    const dom = this.stage && this.stage.renderer ? this.stage.renderer.domElement : null;
    if(cam && dom && dom.height > 0)
      this.uniforms.uPxScale.value = 2*Math.tan(cam.fov*Math.PI/360)/dom.height;

    // ---- pack the drawn ropes into the front of the buffer
    let w = 0, top = 0;
    for(let s=0;s<this.n;s++){
      const r = this.rope[s];
      if(!r.on) continue;
      top = s+1;
      const fade = 1 - Math.min(1, Math.max(0,
                     (r.d - this.drawFrom)/(this.drawTo - this.drawFrom)));
      if(fade <= 0.01) continue;         // still simulated, simply not drawn

      const b = s*P, v = w*P*2;
      for(let j=0;j<P;j++){
        const i = b+j, o = j > 0 ? b+j-1 : b, c = j < P-1 ? b+j+1 : b+j;
        let tx = this.px[c]-this.px[o], ty = this.py[c]-this.py[o], tz = this.pz[c]-this.pz[o];
        const tl = Math.sqrt(tx*tx+ty*ty+tz*tz) || 1;
        tx/=tl; ty/=tl; tz/=tl;
        // a cut end is frayed rather than sawn off, so the last span thins away
        const a = fade * (j === P-1 ? 0.55 : 1);
        for(let e=0;e<2;e++){
          const q = v + j*2 + e;
          this.aPos[q*3] = this.px[i]; this.aPos[q*3+1] = this.py[i]; this.aPos[q*3+2] = this.pz[i];
          this.aTan[q*3] = tx; this.aTan[q*3+1] = ty; this.aTan[q*3+2] = tz;
          this.aFade[q] = a;
        }
      }
      w++;
    }
    this.n = top;

    const g = this.mesh.geometry;
    g.setDrawRange(0, w*(P-1)*6);
    g.attributes.position.needsUpdate = true;
    g.attributes.tang.needsUpdate = true;
    g.attributes.fade.needsUpdate = true;
    this.mesh.visible = w > 0;
  }

  dispose(){
    this.scene.remove(this.mesh);
    this.mesh.geometry.dispose();
    this.mat.dispose();
  }
};
