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

Naval.Stage = class Stage {
  constructor(canvas){
    this.renderer = new THREE.WebGLRenderer({canvas, antialias:true, powerPreference:'high-performance'});
    this.renderer.setPixelRatio(Math.min(window.devicePixelRatio||1, 2));
    this.renderer.setClearColor(0x0a1a2b, 1);

    this.scene = new THREE.Scene();
    /* Thin haze rather than a wall of fog. The old density hid everything past
       ~550 m, which left no horizon at all — and a sea with no horizon never
       looks like a sea. */
    this.scene.fog = new THREE.FogExp2(0xb6cfdd, 0.00065);
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
    this.sun.color.setRGB(1.0, 0.72 + 0.23*t, 0.45 + 0.42*t);
    this.sun.intensity = 1.1 + 1.0*t;
    this.horizon.setRGB(0.62 + 0.20*t, 0.70 + 0.19*t, 0.76 + 0.17*t);
    this.zenith.setRGB(0.08 + 0.04*t, 0.24 + 0.12*t, 0.42 + 0.10*t);
    this.hemi.intensity = 0.45 + 0.45*t;

    if(this.skyMat){
      this.skyMat.uniforms.uSun.value.copy(this.sunDir);
      this.skyMat.uniforms.uZenith.value.copy(this.zenith);
      this.skyMat.uniforms.uHorizon.value.copy(this.horizon);
    }
    if(this.scene.fog) this.scene.fog.color.copy(this.horizon).multiplyScalar(0.96);
    if(this.onSunChange) this.onSunChange(this);
  }

  _addSky(){
    const g = new THREE.SphereGeometry(10000, 48, 24);
    const m = new THREE.ShaderMaterial({
      side:THREE.BackSide, depthWrite:false,
      uniforms:{
        uSun:{value:this.sunDir.clone()},
        uZenith:{value:this.zenith.clone()},
        uHorizon:{value:this.horizon.clone()}
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
        ${Naval.SKY_GLSL}
        void main(){
          vec3 c = navalSky(vDir, uSun, uZenith, uHorizon);
          // the disc itself, which only the dome draws
          c += vec3(1.0,0.95,0.85) * pow(max(dot(normalize(vDir),uSun),0.0), 2200.0) * 6.0;
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
