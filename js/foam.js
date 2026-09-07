/* Foam with a memory.
 *
 * Until now foam existed only where a crest was steep AT THIS INSTANT, so it
 * blinked in and out, and the wake was a band welded to the hull that travelled
 * with her. Real foam is left BEHIND: a crest breaks, the froth stays on the
 * water and disperses over tens of seconds, and a ship's track lingers astern
 * long after she has gone.
 *
 * So the foam lives in its own world-space field, ping-ponged between two render
 * targets. Each frame the field is carried forward, faded, and fresh foam is
 * deposited where crests break and where the hull cuts. The sea then reads it.
 *
 * The field is anchored to the vessel and SNAPPED TO WHOLE TEXELS: if the origin
 * drifted by fractions of a texel, every frame would resample the previous one
 * slightly off-grid and the whole field would smear into mush within seconds.
 */
window.Naval = window.Naval || {};

Naval.FoamField = class FoamField {
  constructor(renderer, oceanUniforms){
    const C = Naval.Config;
    this.res = 1024;
    this.size = 620;                       // metres of world covered
    this.texelWorld = this.size / this.res;
    this.tau = 14.0;                       // seconds for foam to fade to 1/e

    // half float keeps the slow decay smooth; 8 bits would quantise it into
    // visible steps and strand faint foam at a fixed value
    let type = THREE.HalfFloatType;
    try{
      const gl = renderer.getContext();
      if(!(gl instanceof WebGL2RenderingContext) && !gl.getExtension('OES_texture_half_float')){
        type = THREE.UnsignedByteType;
      }
    }catch(e){ type = THREE.UnsignedByteType; }

    const opts = { minFilter:THREE.LinearFilter, magFilter:THREE.LinearFilter,
                   type, depthBuffer:false, stencilBuffer:false,
                   wrapS:THREE.ClampToEdgeWrapping, wrapT:THREE.ClampToEdgeWrapping };
    this.targets = [
      new THREE.WebGLRenderTarget(this.res, this.res, opts),
      new THREE.WebGLRenderTarget(this.res, this.res, opts)
    ];
    this.cur = 0;
    this.origin = new THREE.Vector2(-this.size*0.5, -this.size*0.5);
    this._prevOrigin = this.origin.clone();
    this._cleared = false;

    this.uniforms = {
      uPrev:{value:null},
      uOffsetUV:{value:new THREE.Vector2()},
      uDecay:{value:1.0},
      uOrigin:{value:new THREE.Vector2()},
      uSize:{value:this.size},
      uTexel:{value:1/this.res},
      uTime:{value:0},
      uSeed:{value:0},
      // shared with the sea, so waves and hull can never disagree
      uWaveA: oceanUniforms.uWaveA,
      uWaveB: oceanUniforms.uWaveB,
      uShipPos: oceanUniforms.uShipPos,
      uShipFwd: oceanUniforms.uShipFwd,
      uShipHalf: oceanUniforms.uShipHalf,
      uShipSpeed: oceanUniforms.uShipSpeed,
    };

    this.mat = new THREE.ShaderMaterial({
      uniforms:this.uniforms,
      defines:{NW:C.NWAVES_FOAM},
      vertexShader:`
        varying vec2 vUv;
        void main(){ vUv = uv; gl_Position = vec4(position.xy, 0.0, 1.0); }`,
      fragmentShader:`
        precision highp float;
        uniform sampler2D uPrev;
        uniform vec2 uOffsetUV, uOrigin, uShipFwd, uShipHalf;
        uniform float uDecay, uSize, uTexel, uTime, uSeed, uShipSpeed;
        uniform vec3 uShipPos;
        uniform vec4 uWaveA[NW]; uniform vec2 uWaveB[NW];
        varying vec2 vUv;

        void main(){
          vec2 wpos = uOrigin + vUv*uSize;      // world XZ of this texel

          /* Carry the field forward. The fetch is offset by however far the
             anchor moved, so foam stays put in the WORLD while the window
             slides over it. A four-tap spread lets it disperse as it ages. */
          vec2 puv = vUv + uOffsetUV;
          float prev = 0.0;
          if(puv.x > 0.0 && puv.x < 1.0 && puv.y > 0.0 && puv.y < 1.0){
            prev  = texture2D(uPrev, puv).r * 0.60;
            prev += texture2D(uPrev, puv + vec2(uTexel,0.0)).r * 0.10;
            prev += texture2D(uPrev, puv - vec2(uTexel,0.0)).r * 0.10;
            prev += texture2D(uPrev, puv + vec2(0.0,uTexel)).r * 0.10;
            prev += texture2D(uPrev, puv - vec2(0.0,uTexel)).r * 0.10;
          }
          prev *= uDecay;

          // --- fresh foam where a crest is breaking ---
          float steep = 0.0;
          for(int i=0;i<NW;i++){
            vec2 d = uWaveA[i].xy; float amp=uWaveA[i].z; float k=uWaveA[i].w;
            float omega=uWaveB[i].x; float Q=uWaveB[i].y;
            float f = k*dot(d, wpos) - omega*uTime;
            steep += Q*k*amp*max(sin(f), 0.0);
          }
          float breaking = smoothstep(0.66, 1.05, steep) * 0.9;

          // --- and where the hull tears the surface open ---
          vec2 rel = wpos - uShipPos.xz;
          vec2 f2 = normalize(uShipFwd);
          vec2 r2 = vec2(f2.y, -f2.x);
          vec2 loc = vec2(dot(rel,r2)/max(uShipHalf.y,0.01),
                          dot(rel,f2)/max(uShipHalf.x,0.01));
          float ed = length(loc);
          float way = clamp(uShipSpeed/3.0, 0.0, 1.0);
          /* A ring ON her waterline, not a disc filling her whole footprint —
             and it needs way to appear. A vessel lying stopped disturbs almost
             nothing; filling the hull outline regardless left her sitting in a
             permanent white pool. */
          // gap to the waterline in metres, so the ring keeps one thickness all
          // the way round instead of ballooning at bow and stern
          float gapM = length(rel) * (1.0 - 1.0/max(ed, 1e-3));
          float band = 1.0 - smoothstep(0.0, 1.1 + 3.2*way, abs(gapM));
          float hull = band * (0.06 + 1.0*way);

          // deposit, never accumulate past saturation
          gl_FragColor = vec4(clamp(max(prev, max(breaking, hull)), 0.0, 1.0), 0.0, 0.0, 1.0);
        }`
    });

    this.scene = new THREE.Scene();
    this.camera = new THREE.OrthographicCamera(-1,1,1,-1,0,1);
    this.scene.add(new THREE.Mesh(new THREE.PlaneGeometry(2,2), this.mat));
  }

  get texture(){ return this.targets[this.cur].texture; }

  update(renderer, dt, t, centre){
    /* Snap the anchor to whole texels. Without this the resample is off-grid
       every frame and the field blurs itself into nothing in a few seconds. */
    const tw = this.texelWorld;
    const ox = Math.floor(centre.x / tw) * tw - this.size*0.5;
    const oz = Math.floor(centre.z / tw) * tw - this.size*0.5;
    this._prevOrigin.copy(this.origin);
    this.origin.set(ox, oz);

    const prev = this.targets[this.cur];
    const next = this.targets[1 - this.cur];

    this.uniforms.uPrev.value = prev.texture;
    this.uniforms.uOrigin.value.copy(this.origin);
    this.uniforms.uTime.value = t;
    this.uniforms.uDecay.value = Math.exp(-Math.max(dt,0)/this.tau);
    // where to look in the previous frame for the same patch of world
    this.uniforms.uOffsetUV.value.set(
      (this.origin.x - this._prevOrigin.x)/this.size,
      (this.origin.y - this._prevOrigin.y)/this.size
    );

    const prevTarget = renderer.getRenderTarget();
    if(!this._cleared){                    // start from clean water
      const c = renderer.getClearColor(new THREE.Color()).clone();
      const a = renderer.getClearAlpha();
      renderer.setRenderTarget(this.targets[0]); renderer.setClearColor(0x000000,1); renderer.clear();
      renderer.setRenderTarget(this.targets[1]); renderer.clear();
      renderer.setClearColor(c, a);
      this._cleared = true;
    }
    renderer.setRenderTarget(next);
    renderer.render(this.scene, this.camera);
    renderer.setRenderTarget(prevTarget);

    this.cur = 1 - this.cur;
  }

  dispose(){
    this.targets.forEach(t => t.dispose());
    this.mat.dispose();
  }
};
