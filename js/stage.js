/* Renderer, scene, camera, light and sky — the room the vessel sails in. */
window.Naval = window.Naval || {};

/* The sky, as one GLSL function shared by the dome and by the sea's reflection.
   They MUST come from the same code: if the water mirrored a different sky from
   the one overhead, the horizon would show a seam and the whole image would
   read as fake. The dome adds the sun's disc on top; the sea gets its highlight
   from a microfacet term instead, which is what spreads it into a glitter path. */
Naval.SKY_GLSL = `
  vec3 navalSky(vec3 dir, vec3 sunDir, vec3 zenith, vec3 horizon){
    float h = clamp(dir.y, 0.0, 1.0);
    vec3 c = mix(horizon, zenith, pow(h, 0.55));
    float sd = max(dot(normalize(dir), sunDir), 0.0);
    // broad halo: air scatters the sun's light across a wide arc
    c += vec3(1.00, 0.82, 0.55) * pow(sd, 8.0) * 0.30;
    c += vec3(1.00, 0.90, 0.72) * pow(sd, 64.0) * 0.35;
    // haze thickens toward the horizon, where you look through more atmosphere
    c = mix(c, horizon * 1.04, smoothstep(0.22, -0.02, dir.y));
    return c;
  }`;

/* The haze, also as one shared GLSL function.
   Everything the eye can see through air must use THIS, not a second fog model:
   the sea and the ship have to dim at the same rate or the vessel stays sharp
   against a washed-out sea and the illusion collapses. */
Naval.HAZE_GLSL = `
  uniform float uHaze, uHazeH;
  float hazeAlong(vec3 from, vec3 to){
    vec3 d = to - from;
    float dist = length(d);
    if(dist < 0.001) return 0.0;
    float y0 = max(from.y, 0.0), y1 = max(to.y, 0.0);
    float dy = y1 - y0;
    float depth;
    if(abs(dy) < 0.01){
      depth = exp(-y0/uHazeH) * dist;
    }else{
      depth = dist * (uHazeH/dy) * (exp(-y0/uHazeH) - exp(-y1/uHazeH));
    }
    return 1.0 - exp(-uHaze * abs(depth));
  }`;

/* Patch any standard material so it breathes the same air as the sea.
   Three's own FogExp2 falls off with the SQUARE of distance while ours is
   Beer-Lambert in the traversed depth, so the two can never agree at more than
   one range — which is exactly why the ship used to stay crisp far away. */
Naval.applyHaze = function(mat, u){
  if(!mat || mat.userData.hazed) return;
  mat.userData.hazed = true;
  mat.fog = false;
  mat.onBeforeCompile = (shader)=>{
    shader.uniforms.uCam = u.uCam;
    shader.uniforms.uSun = u.uSun;
    shader.uniforms.uZenith = u.uZenith;
    shader.uniforms.uHorizon = u.uHorizon;
    shader.uniforms.uHaze = u.uHaze;
    shader.uniforms.uHazeH = u.uHazeH;

    shader.vertexShader = 'varying vec3 vHazeW;\n' + shader.vertexShader.replace(
      '#include <project_vertex>',
      '#include <project_vertex>\n  vHazeW = (modelMatrix * vec4(transformed,1.0)).xyz;'
    );

    const head = 'varying vec3 vHazeW;\nuniform vec3 uCam,uSun,uZenith,uHorizon;\n'
               + Naval.SKY_GLSL + '\n' + Naval.HAZE_GLSL + '\n';
    const tail = '\n{ vec3 vd = normalize(vHazeW - uCam);\n'
               + '  gl_FragColor.rgb = mix(gl_FragColor.rgb,'
               + ' navalSky(vd, uSun, uZenith, uHorizon), hazeAlong(uCam, vHazeW)); }';
    shader.fragmentShader = head + shader.fragmentShader.replace(
      /}\s*$/, tail + '\n}'
    );
  };
  mat.needsUpdate = true;
};

Naval.Stage = class Stage {
  constructor(canvas){
    this.renderer = new THREE.WebGLRenderer({canvas, antialias:true, powerPreference:'high-performance'});
    this.renderer.setPixelRatio(Math.min(window.devicePixelRatio||1, 2));
    this.renderer.setClearColor(0x0a1a2b, 1);

    this.scene = new THREE.Scene();
    // No THREE fog: sea, hull and rig all share Naval.HAZE_GLSL instead, so they
    // dim at one rate. Two fog models can never agree at more than one range.
    this.camera = new THREE.PerspectiveCamera(55, 1, 0.7, 14000);

    this.zenith  = new THREE.Color(0x1e5c86);
    this.horizon = new THREE.Color(0xd2e3ec);

    this.sunDir = new THREE.Vector3();
    this.sun = new THREE.DirectionalLight(0xfff2dc, 2.1);
    this.scene.add(this.sun);
    this.hemi = new THREE.HemisphereLight(0xbcd8ec, 0x1a3346, 0.9);
    this.scene.add(this.hemi);
    this.setSun(38, 225);            // elevation and bearing, in degrees

    this._addSky();
    addEventListener('resize', ()=> this.resize());
    this.resize();
  }

  /* Elevation and bearing in degrees. A low sun is what stretches the sun's
     reflection into the long glitter road you see on a real sea; with the sun
     high overhead that reflection collapses to a patch a few metres from the
     hull, and the effect is invisible. */
  setSun(elevDeg, bearingDeg){
    this.sunElev = elevDeg;
    this.sunBearing = bearingDeg;
    const e = elevDeg*Math.PI/180, b = bearingDeg*Math.PI/180;
    this.sunDir.set(Math.sin(b)*Math.cos(e), Math.sin(e), Math.cos(b)*Math.cos(e)).normalize();
    this.sun.position.copy(this.sunDir).multiplyScalar(200);

    // low sun reddens and dims; the sky's haze warms with it
    const t = Math.max(0, Math.min(1, elevDeg/30));
    /* Below the horizon it is night. Not black — a real sea at night keeps a
       cold sheen off the sky, and you must still be able to make out your own
       vessel. The sun stands in for the moon: dim, blue, and never quite gone. */
    const night = Math.max(0, Math.min(1, -elevDeg/10));
    this.night = night;

    this.sun.color.setRGB(
      1.0 - 0.55*night,
      (0.72 + 0.23*t) - 0.30*night,
      (0.45 + 0.42*t) + 0.28*night);
    this.sun.intensity = (1.1 + 1.0*t)*(1 - night) + 0.28*night;
    this.horizon.setRGB(
      (0.62 + 0.20*t)*(1-night) + 0.055*night,
      (0.70 + 0.19*t)*(1-night) + 0.075*night,
      (0.76 + 0.17*t)*(1-night) + 0.115*night);
    this.zenith.setRGB(
      (0.08 + 0.04*t)*(1-night) + 0.012*night,
      (0.24 + 0.12*t)*(1-night) + 0.020*night,
      (0.42 + 0.10*t)*(1-night) + 0.045*night);
    this._hemiBase = (0.45 + 0.45*t)*(1-night) + 0.10*night;
    this.hemi.intensity = this._hemiBase + (this.flash||0)*2.6;

    if(this.skyMat){
      this.skyMat.uniforms.uSun.value.copy(this.sunDir);
      this.skyMat.uniforms.uZenith.value.copy(this.zenith);
      this.skyMat.uniforms.uHorizon.value.copy(this.horizon);
    }
    if(this.onSunChange) this.onSunChange(this);
  }

  /* Lightning.
     A stroke is not one flash: the leader lights the cloud, then the return
     stroke follows a few hundredths of a second later, often twice. Rendering a
     single square pulse looks like a light switch, so the envelope below fires
     two or three spikes with a fast decay. The flash reaches the sky dome, the
     sea and the rigging together, or the vessel would stay dark under a lit sky. */
  strike(){
    this._flashQueue = [
      {at:0.00, a:1.00}, {at:0.06, a:0.55},
      {at:0.14, a:0.85}, {at:0.26, a:0.30}
    ];
    this._flashT = 0;
  }

  updateWeather(dt, seaState){
    // storms only: below a strong breeze there is nothing to discharge
    const p = Math.max(0, (seaState - 5.2)/3.8);
    if(p > 0 && Math.random() < p*p*dt*0.20) this.strike();   // ~1 per 5 s at full gale

    let f = 0;
    if(this._flashQueue){
      this._flashT += dt;
      for(const s of this._flashQueue){
        const dtt = this._flashT - s.at;
        if(dtt >= 0) f = Math.max(f, s.a*Math.exp(-dtt*16.0));
      }
      if(this._flashT > 1.2) this._flashQueue = null;
    }
    this.flash = f;
    if(this.skyMat) this.skyMat.uniforms.uFlash.value = f;
    if(this.onFlash) this.onFlash(f);
    // the deck and canvas must catch it too
    this.hemi.intensity = this._hemiBase + f*2.6;
  }

  _addSky(){
    const g = new THREE.SphereGeometry(10000, 48, 24);
    const m = new THREE.ShaderMaterial({
      side:THREE.BackSide, depthWrite:false,
      uniforms:{
        uSun:{value:this.sunDir.clone()},
        uZenith:{value:this.zenith.clone()},
        uHorizon:{value:this.horizon.clone()},
        uFlash:{value:0}
      },
      vertexShader:`
        varying vec3 vDir;
        void main(){
          vDir = normalize(position);
          gl_Position = projectionMatrix*modelViewMatrix*vec4(position,1.0);
        }`,
      fragmentShader:`
        precision highp float;
        varying vec3 vDir;
        uniform vec3 uSun, uZenith, uHorizon;
        uniform float uFlash;
        ${Naval.SKY_GLSL}
        void main(){
          vec3 c = navalSky(vDir, uSun, uZenith, uHorizon);
          // the disc itself, which only the dome draws
          c += vec3(1.0,0.95,0.85) * pow(max(dot(normalize(vDir),uSun),0.0), 2200.0) * 6.0;
          // the discharge lights the whole vault, brightest low down
          c += vec3(0.62,0.70,0.92) * uFlash * (1.6 - 0.9*clamp(vDir.y,0.0,1.0));
          gl_FragColor = vec4(c, 1.0);
        }`
    });
    this.skyMat = m;
    this.scene.add(new THREE.Mesh(g,m));
  }

  resize(){
    const w = innerWidth, h = innerHeight;
    this.renderer.setSize(w, h, false);
    this.camera.aspect = w/h;
    this.camera.updateProjectionMatrix();
  }

  render(){ this.renderer.render(this.scene, this.camera); }
};
