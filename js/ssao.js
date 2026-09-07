/* Ambient occlusion, on the vessel and on nothing else.
 *
 * Screen-space AO wants a depth pass, and three renders one with an override
 * material. The sea is displaced in her own vertex shader, so an override would
 * draw her flat: the occlusion would come out wrong exactly where hull meets
 * water, which is where the eye goes first. Restricting the pass to the ship
 * removes that objection outright — she is ordinary geometry, her depth is her
 * depth — and costs far less, the buffer only ever holding one vessel.
 *
 * She is picked out by a render LAYER, not by hiding everything else: hiding
 * and restoring a scene graph twice a frame is slower and easy to get wrong,
 * whereas a layer costs nothing and cannot be left in a bad state.
 *
 * Half resolution throughout. Occlusion is low-frequency by nature and the
 * blur that follows would throw away the extra detail anyway.
 */
window.Naval = window.Naval || {};

Naval.SHIP_LAYER = 1;                    // the vessel, and only her

Naval.ShipAO = class ShipAO {
  constructor(renderer, scene, camera){
    this.renderer = renderer;
    this.scene = scene;
    this.camera = camera;

    this.scale  = 0.5;                   // of the drawing buffer
    this.radius = 2.4;                   // metres — the scale of what should occlude
    this.bias   = 0.06;                  // keeps a flat surface from occluding itself
    this.samples = 12;

    this.cam = camera.clone();
    this.cam.layers.set(Naval.SHIP_LAYER);
    this.normalMat = new THREE.MeshNormalMaterial();

    const base = { minFilter:THREE.LinearFilter, magFilter:THREE.LinearFilter,
                   depthBuffer:false, stencilBuffer:false };
    this.gbuf = new THREE.WebGLRenderTarget(2, 2,
                  Object.assign({}, base, {depthBuffer:true}));
    this.gbuf.depthTexture = new THREE.DepthTexture(2, 2);
    this.gbuf.depthTexture.type = THREE.UnsignedIntType;
    this.aoRT   = new THREE.WebGLRenderTarget(2, 2, base);
    this.blurRT = new THREE.WebGLRenderTarget(2, 2, base);
    this._w = 0; this._h = 0;

    /* A hemisphere of samples, bunched toward the origin so that near contacts
       — the deck against a bulkhead — carry more weight than distant ones. */
    const kernel = [];
    for(let i=0;i<this.samples;i++){
      const v = new THREE.Vector3(Math.random()*2-1, Math.random()*2-1, Math.random());
      v.normalize().multiplyScalar(0.3 + 0.7*Math.pow((i+1)/this.samples, 2));
      kernel.push(v);
    }

    this.aoMat = new THREE.ShaderMaterial({
      defines:{ NSAMP:this.samples },
      uniforms:{
        tNormal:{value:this.gbuf.texture}, tDepth:{value:this.gbuf.depthTexture},
        uProj:{value:new THREE.Matrix4()}, uInvProj:{value:new THREE.Matrix4()},
        uRes:{value:new THREE.Vector2()}, uKernel:{value:kernel},
        uRadius:{value:this.radius}, uBias:{value:this.bias}
      },
      vertexShader:`
        varying vec2 vUv;
        void main(){ vUv = uv; gl_Position = vec4(position.xy, 0.0, 1.0); }`,
      fragmentShader:`
        precision highp float;
        varying vec2 vUv;
        uniform sampler2D tNormal, tDepth;
        uniform mat4 uProj, uInvProj;
        uniform vec2 uRes;
        uniform vec3 uKernel[NSAMP];
        uniform float uRadius, uBias;

        vec3 viewAt(vec2 uv){
          float d = texture2D(tDepth, uv).x;
          vec4 clip = vec4(uv*2.0 - 1.0, d*2.0 - 1.0, 1.0);
          vec4 v = uInvProj * clip;
          return v.xyz / v.w;
        }
        float hash(vec2 p){ return fract(sin(dot(p, vec2(127.1,311.7)))*43758.5453); }

        void main(){
          vec4 nt = texture2D(tNormal, vUv);
          // nothing of her on this pixel: unoccluded, and the sea keeps its own light
          if(nt.a < 0.5){ gl_FragColor = vec4(1.0); return; }

          vec3 N = normalize(nt.xyz*2.0 - 1.0);
          vec3 P = viewAt(vUv);

          // a per-pixel twist of the kernel, so the error is noise the blur can
          // take out rather than banding it cannot
          float ang = hash(vUv*uRes)*6.2831853;
          vec3 rv = vec3(cos(ang), sin(ang), 0.0);
          vec3 T = normalize(rv - N*dot(rv, N));
          mat3 TBN = mat3(T, cross(N, T), N);

          float occ = 0.0;
          for(int i=0;i<NSAMP;i++){
            vec3 sp = P + TBN*uKernel[i]*uRadius;
            vec4 o = uProj*vec4(sp, 1.0);
            vec2 suv = (o.xy/o.w)*0.5 + 0.5;
            if(suv.x < 0.0 || suv.x > 1.0 || suv.y < 0.0 || suv.y > 1.0) continue;
            if(texture2D(tNormal, suv).a < 0.5) continue;      // no ship there either
            float sz = viewAt(suv).z;
            /* Only count an occluder that is actually near: without this a
               distant mast in front of the hull would shade the whole hull. */
            float range = smoothstep(0.0, 1.0, uRadius/max(abs(P.z - sz), 1e-4));
            occ += step(sp.z + uBias, sz) * range;
          }
          gl_FragColor = vec4(vec3(1.0 - occ/float(NSAMP)), 1.0);
        }`
    });

    this.blurMat = new THREE.ShaderMaterial({
      uniforms:{ tAO:{value:this.aoRT.texture}, uTexel:{value:new THREE.Vector2()} },
      vertexShader:this.aoMat.vertexShader,
      fragmentShader:`
        precision highp float;
        varying vec2 vUv;
        uniform sampler2D tAO;
        uniform vec2 uTexel;
        void main(){
          // 4x4 box: enough to turn the sampling noise into a smooth gradient
          float s = 0.0;
          for(int x=-2;x<=1;x++) for(int y=-2;y<=1;y++)
            s += texture2D(tAO, vUv + vec2(float(x)+0.5, float(y)+0.5)*uTexel).r;
          gl_FragColor = vec4(vec3(s/16.0), 1.0);
        }`
    });

    this.quad = new THREE.Mesh(new THREE.PlaneGeometry(2,2), this.aoMat);
    this.quadScene = new THREE.Scene();
    this.quadScene.add(this.quad);
    this.quadCam = new THREE.OrthographicCamera(-1,1,1,-1,0,1);
  }

  get texture(){ return this.blurRT.texture; }

  _resize(){
    const s = this.renderer.getDrawingBufferSize(new THREE.Vector2());
    const w = Math.max(2, Math.round(s.x*this.scale));
    const h = Math.max(2, Math.round(s.y*this.scale));
    if(w === this._w && h === this._h) return;
    this._w = w; this._h = h;
    this.gbuf.setSize(w, h);
    this.gbuf.depthTexture.image.width = w;
    this.gbuf.depthTexture.image.height = h;
    this.gbuf.depthTexture.needsUpdate = true;
    this.aoRT.setSize(w, h);
    this.blurRT.setSize(w, h);
    this.aoMat.uniforms.uRes.value.set(w, h);
    this.blurMat.uniforms.uTexel.value.set(1/w, 1/h);
  }

  /* One G-buffer pass over the vessel, one occlusion pass, one blur. Leaves the
     renderer exactly as it found it — a stray override material or clear colour
     would show up in the main render as a scene painted in normals. */
  render(){
    this._resize();
    const r = this.renderer;
    const prevRT = r.getRenderTarget();
    const prevOverride = this.scene.overrideMaterial;
    const cc = r.getClearColor(new THREE.Color()).clone(), ca = r.getClearAlpha();

    this.cam.copy(this.camera);
    this.cam.layers.set(Naval.SHIP_LAYER);

    this.scene.overrideMaterial = this.normalMat;
    r.setRenderTarget(this.gbuf);
    r.setClearColor(0x000000, 0);
    r.clear();
    r.render(this.scene, this.cam);
    this.scene.overrideMaterial = prevOverride;

    this.aoMat.uniforms.uProj.value.copy(this.camera.projectionMatrix);
    this.aoMat.uniforms.uInvProj.value.copy(this.camera.projectionMatrixInverse);
    this.aoMat.uniforms.uRadius.value = this.radius;
    this.aoMat.uniforms.uBias.value = this.bias;

    this.quad.material = this.aoMat;
    r.setRenderTarget(this.aoRT);
    r.render(this.quadScene, this.quadCam);

    this.quad.material = this.blurMat;
    r.setRenderTarget(this.blurRT);
    r.render(this.quadScene, this.quadCam);

    r.setClearColor(cc, ca);
    r.setRenderTarget(prevRT);
  }
};
