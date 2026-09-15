/* La lunette.
 *
 * A spyglass is TWO things, and they are done in two different places.
 *
 * The magnification is the camera's: the world is drawn through a lens a
 * hundred times narrower, by the same renderer, with every pass — sea,
 * reflection, shadows, the hull seen through the water — exactly as it would
 * be at the naked eye. Nothing is enlarged after the fact, so a hull at two
 * miles is really drawn with the detail it has, not blown up from a few pixels.
 *
 * The GLASS is a pass laid over the finished picture. The image on screen is
 * copied into a texture and redrawn through the eyepiece: a disc, a barrel
 * distortion, colour fringes toward the rim, and a lens that has been knocked
 * about at sea. That part cannot live in the scene — barrel distortion bends
 * the picture, and only something holding the picture can bend it.
 *
 * The rule for the power is stated, not tuned. The disc fills 84 % of the
 * screen height; at the naked eye the screen is 55° tall, so the disc spans
 * about 46°. At ×M the same disc must hold 46°/M of the real world, which puts
 * the camera's vertical field at 55°/M. At ×100, 0.55°: a frigate at two
 * miles fills the glass.
 */
window.Naval = window.Naval || {};

Naval.Spyglass = class Spyglass {
  constructor(stage, canvas, rig){
    this.stage = stage;
    this.rig = rig;
    this.on = false;
    this.power = 100;
    this.yaw = 0; this.pitch = 0;           // where the glass points, in the WORLD
    this.onChange = null;                   // (on, power) — for the page to say so
    this._fov = 55;
    this._size = new THREE.Vector2();
    this._zero = new THREE.Vector2();
    this._dir = new THREE.Vector3();
    this._clock = 0;
    this.tex = null;

    this.uniforms = {
      uScene:{value:null}, uGlass:{value:Spyglass.glassTexture()},
      uRes:{value:new THREE.Vector2(1,1)}, uPower:{value:100}
    };
    this.mat = new THREE.ShaderMaterial({
      uniforms:this.uniforms, depthTest:false, depthWrite:false,
      vertexShader:`
        varying vec2 vUv;
        void main(){ vUv = uv; gl_Position = vec4(position.xy, 0.0, 1.0); }`,
      fragmentShader:`
        uniform sampler2D uScene, uGlass;
        uniform vec2 uRes;
        uniform float uPower;
        varying vec2 vUv;

        const float R = 0.42;          // the eyepiece, in screen heights

        // one channel of the scene through the lens, at barrel strength k
        float lensTap(vec2 p, float k, int c, float soft){
          vec2 aspect = vec2(uRes.x/uRes.y, 1.0);
          float r2 = dot(p, p)/(R*R);
          /* Divided by (1+k) so the rim still lands on the rim: undivided, the
             top and bottom of the disc reached past the picture and came back
             black. The barrel is in the SHAPE of the curve — the middle held
             large, the edge crowded in — not in how far it reaches. */
          vec2 q = p*(1.0 + k*r2)/(1.0 + k);
          vec2 uv = q/aspect + 0.5;
          if(uv.x < 0.0 || uv.x > 1.0 || uv.y < 0.0 || uv.y > 1.0) return 0.0;
          vec2 px = soft/uRes;
          vec4 s = texture2D(uScene, uv)*0.4
                 + texture2D(uScene, uv + vec2( px.x, px.y))*0.15
                 + texture2D(uScene, uv + vec2(-px.x, px.y))*0.15
                 + texture2D(uScene, uv + vec2( px.x,-px.y))*0.15
                 + texture2D(uScene, uv + vec2(-px.x,-px.y))*0.15;
          return c == 0 ? s.r : (c == 1 ? s.g : s.b);
        }

        void main(){
          vec2 p = (vUv - 0.5)*vec2(uRes.x/uRes.y, 1.0);
          float r = length(p)/R;                     // 0 at the centre, 1 at the rim
          if(r > 1.02){ gl_FragColor = vec4(0.0, 0.0, 0.0, 1.0); return; }

          vec4 g = texture2D(uGlass, p/R*0.5 + 0.5);  // r scratches, g smudge, b dust, a crack

          /* A crack bends what is behind it: the picture steps sideways across
             the line, which is what makes it read as broken glass rather than
             as a hair on the lens. */
          p += normalize(p + 1e-4)*g.a*0.0035;

          /* BARREL, and lateral colour. A cheap objective is a single lens, so
             blue is bent harder than red and the fringes open toward the rim.
             The edge also goes soft: the field is curved and only the middle
             is in focus. */
          float soft = 0.6 + 2.8*r*r;
          vec3 col = vec3(lensTap(p, 0.30, 0, soft),
                          lensTap(p, 0.33, 1, soft),
                          lensTap(p, 0.37, 2, soft));

          float lum = dot(col, vec3(0.30, 0.59, 0.11));
          // a greasy thumb: the picture goes milky and loses its contrast
          col = mix(col, vec3(lum)*0.85 + 0.10, g.g*0.30);
          // scratches catch whatever light is coming through, so they vanish at night
          col += (g.r + g.a*0.8)*(0.03 + 0.55*lum);
          // and dust is a shadow on it
          col *= 1.0 - g.b*0.75;

          // light falls away toward the stop, cos^4 and then some
          col *= mix(0.45, 1.0, pow(max(0.0, 1.0 - r*r*0.85), 1.6));

          // the rim of the tube: a soft black, with the faint glint of its bevel
          float rim = smoothstep(1.0, 0.975, r);
          float bevel = exp(-pow((r - 0.965)/0.012, 2.0))*0.10;
          col = col*rim + vec3(bevel*(0.4 + lum));
          gl_FragColor = vec4(col, 1.0);
        }`
    });
    this.scene = new THREE.Scene();
    this.ortho = new THREE.OrthographicCamera(-1, 1, 1, -1, 0, 1);
    const quad = new THREE.Mesh(new THREE.PlaneGeometry(2, 2), this.mat);
    quad.frustumCulled = false;
    this.scene.add(quad);

    // aiming: a drag moves the glass, a hundred times more gently at ×100
    let drag = false, px = 0, py = 0;
    canvas.addEventListener('pointerdown', e=>{ if(this.on){ drag = true; px = e.clientX; py = e.clientY; } });
    addEventListener('pointerup', ()=> drag = false);
    addEventListener('pointermove', e=>{
      if(!this.on || !drag) return;
      const k = 0.0045*(10/this.power);
      this.yaw   -= (e.clientX - px)*k;
      this.pitch  = Math.max(-0.6, Math.min(0.6, this.pitch - (e.clientY - py)*k));
      px = e.clientX; py = e.clientY;
    });
    // and the wheel is the draw tube: ×10 to ×100
    canvas.addEventListener('wheel', e=>{
      if(!this.on) return;
      this.power = Math.max(10, Math.min(100, this.power*Math.exp(-e.deltaY*0.0012)));
      if(this.onChange) this.onChange(true, this.power);
      e.preventDefault();
    }, {passive:false});
  }

  toggle(){
    const cam = this.stage.camera;
    this.on = !this.on;
    if(this.on){
      // it goes to the eye pointing where one was already looking
      this._fov = cam.fov;
      cam.getWorldDirection(this._dir);
      this.yaw = Math.atan2(this._dir.x, this._dir.z);
      this.pitch = Math.asin(Math.max(-1, Math.min(1, this._dir.y)));
    }else{
      cam.fov = this._fov;
      cam.updateProjectionMatrix();
    }
    if(this.rig) this.rig.held = this.on;
    if(this.onChange) this.onChange(this.on, this.power);
  }

  /* After the rig has put the camera where it goes: keep its position, take
     over its aim and its lens. Called every frame while the glass is up. */
  aim(dt){
    if(!this.on) return;
    const cam = this.stage.camera;
    this._clock += dt;
    /* No hand holds a glass still. Two slow sines and a faster one, a few
       hundredths of a degree — nothing at the naked eye, a visible breathing
       at ×100, which is exactly how a spyglass betrays magnification. */
    const t = this._clock, s = 0.00016;
    const wy = s*(Math.sin(t*0.9) + 0.6*Math.sin(t*2.3 + 1.7) + 0.25*Math.sin(t*7.1));
    const wp = s*(Math.sin(t*1.1 + 0.4) + 0.5*Math.sin(t*2.9 + 2.1) + 0.25*Math.sin(t*6.3));
    const y = this.yaw + wy, p = this.pitch + wp, cp = Math.cos(p);
    this._dir.set(Math.sin(y)*cp, Math.sin(p), Math.cos(y)*cp).add(cam.position);
    cam.fov = 55/this.power;
    cam.updateProjectionMatrix();
    cam.lookAt(this._dir);
    this.uniforms.uPower.value = this.power;
  }

  /* The eyepiece, drawn over what has just been rendered to the screen. */
  render(){
    if(!this.on) return;
    const r = this.stage.renderer;
    r.getDrawingBufferSize(this._size);
    const w = this._size.x|0, h = this._size.y|0;
    if(!this.tex || this.tex.image.width !== w || this.tex.image.height !== h){
      if(this.tex) this.tex.dispose();
      this.tex = new THREE.FramebufferTexture(w, h);
      this.tex.minFilter = this.tex.magFilter = THREE.LinearFilter;
      this.uniforms.uScene.value = this.tex;
    }
    r.setRenderTarget(null);
    r.copyFramebufferToTexture(this._zero, this.tex);
    this.uniforms.uRes.value.set(w, h);
    r.render(this.scene, this.ortho);
  }

  /* A lens that has been to sea, drawn once on a canvas — a published page
     cannot fetch an image, so like every other picture here it is made rather
     than loaded. Four layers in four channels, each read differently by the
     shader: scratches glint, a smudge milks the picture, dust shades it, and a
     crack both glints and bends it. Seeded, so the same glass every time. */
  static glassTexture(){
    if(Spyglass._glass) return Spyglass._glass;
    const S = 1024;
    let seed = 1597;
    const rnd = () => (seed = (seed*16807) % 2147483647)/2147483647;
    const layer = draw => {
      const c = document.createElement('canvas'); c.width = c.height = S;
      const g = c.getContext('2d');
      g.fillStyle = '#000'; g.fillRect(0, 0, S, S);
      g.strokeStyle = g.fillStyle = '#fff';
      g.lineCap = 'round';
      draw(g);
      return g.getImageData(0, 0, S, S).data;
    };

    const scratches = layer(g => {
      // polishing swirls: short arcs about the centre, all the same way round
      for(let i = 0; i < 220; i++){
        const rad = S*0.05 + rnd()*S*0.47, a0 = rnd()*Math.PI*2;
        g.globalAlpha = 0.05 + rnd()*0.12; g.lineWidth = 0.6 + rnd()*0.8;
        g.beginPath(); g.arc(S/2, S/2, rad, a0, a0 + 0.05 + rnd()*0.35); g.stroke();
      }
      // and the honest ones, straight and anywhere
      for(let i = 0; i < 38; i++){
        const x = rnd()*S, y = rnd()*S, a = rnd()*Math.PI, l = S*(0.03 + rnd()*rnd()*0.5);
        g.globalAlpha = 0.12 + rnd()*0.35; g.lineWidth = 0.7 + rnd()*1.3;
        g.beginPath(); g.moveTo(x, y);
        g.quadraticCurveTo(x + Math.cos(a)*l*0.5 + (rnd() - 0.5)*20, y + Math.sin(a)*l*0.5 + (rnd() - 0.5)*20,
                           x + Math.cos(a)*l, y + Math.sin(a)*l);
        g.stroke();
      }
    });
    const smudge = layer(g => {
      for(let i = 0; i < 7; i++){
        const x = S*(0.2 + rnd()*0.6), y = S*(0.2 + rnd()*0.6), rad = S*(0.08 + rnd()*0.18);
        const gr = g.createRadialGradient(x, y, 0, x, y, rad);
        gr.addColorStop(0, 'rgba(255,255,255,' + (0.25 + rnd()*0.35) + ')');
        gr.addColorStop(1, 'rgba(255,255,255,0)');
        g.globalAlpha = 1; g.fillStyle = gr; g.fillRect(x - rad, y - rad, rad*2, rad*2);
      }
      // a thumbprint's ridges, off to one side where a thumb would be
      g.strokeStyle = '#fff';
      for(let i = 0; i < 16; i++){
        g.globalAlpha = 0.10; g.lineWidth = 3;
        g.beginPath(); g.ellipse(S*0.72, S*0.30, 20 + i*7, 14 + i*5, 0.6, 0, Math.PI*2); g.stroke();
      }
    });
    const dust = layer(g => {
      for(let i = 0; i < 90; i++){
        g.globalAlpha = 0.25 + rnd()*0.6;
        g.beginPath(); g.arc(rnd()*S, rnd()*S, 0.8 + rnd()*rnd()*4, 0, Math.PI*2); g.fill();
      }
    });
    const crack = layer(g => {
      // from a chip in the rim, running in and forking once
      const run = (x, y, a, len, w) => {
        g.lineWidth = w; g.globalAlpha = 0.9;
        g.beginPath(); g.moveTo(x, y);
        for(let s = 0; s < len; s += 14){
          a += (rnd() - 0.5)*0.5;
          x += Math.cos(a)*14; y += Math.sin(a)*14;
          g.lineTo(x, y);
        }
        g.stroke();
        return [x, y, a];
      };
      const a0 = 3.75, rx = S/2 + Math.cos(a0)*S*0.49, ry = S/2 + Math.sin(a0)*S*0.49;
      const [x1, y1, a1] = run(rx, ry, a0 + Math.PI + 0.25, S*0.16, 1.6);
      run(x1, y1, a1 + 0.6, S*0.10, 1.1);
      run(x1, y1, a1 - 0.4, S*0.07, 0.9);
      g.globalAlpha = 1; g.beginPath(); g.arc(rx, ry, 9, 0, Math.PI*2); g.fill();   // the chip
    });

    /* Merged into raw bytes and NOT into a canvas. A canvas stores its pixels
       premultiplied, and the crack lives in the alpha channel as data: wherever
       the glass is uncracked — nearly everywhere — the other three channels
       would come back as zero. Each layer above was drawn opaque on black, so
       reading one channel off it is safe. */
    const d = new Uint8Array(S*S*4);
    for(let i = 0; i < d.length; i += 4){
      d[i] = scratches[i]; d[i+1] = smudge[i]; d[i+2] = dust[i]; d[i+3] = crack[i];
    }
    const t = new THREE.DataTexture(d, S, S, THREE.RGBAFormat);
    t.minFilter = THREE.LinearMipmapLinearFilter; t.magFilter = THREE.LinearFilter;
    t.generateMipmaps = true;
    t.colorSpace = THREE.NoColorSpace;
    t.needsUpdate = true;
    Spyglass._glass = t;
    return t;
  }
};
