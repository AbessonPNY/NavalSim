/* Renderer, scene, camera, light and sky — the room the vessel sails in. */
window.Naval = window.Naval || {};

/* The sky, as one GLSL function shared by the dome and by the sea's reflection.
   They MUST come from the same code: if the water mirrored a different sky from
   the one overhead, the horizon would show a seam and the whole image would
   read as fake. The dome adds the sun's disc on top; the sea gets its highlight
   from a microfacet term instead, which is what spreads it into a glitter path. */
Naval.SKY_GLSL = `
  float navalHash13(vec3 p){
    p = fract(p*0.1031);
    p += dot(p, p.yzx + 33.33);
    return fract((p.x + p.y)*p.z);
  }

  /* Stars, laid on a lattice of cells in the direction vector rather than on a
     grid of latitude and longitude — the latter would crowd them at the poles
     and leave a bald patch overhead. One cell in a hundred and thirty holds a
     star, jittered inside its cell so the lattice never shows.

     They are given a real WIDTH rather than being drawn as points. A star
     narrower than a pixel is a guaranteed sparkle storm the moment the camera
     turns, which is the same aliasing trap the sea's ripples fell into: the
     cure is to be wider than the sample spacing, not brighter. */
  float navalStars(vec3 dir){
    vec3 d = dir*190.0;
    vec3 id = floor(d), f = fract(d) - 0.5;
    float h = navalHash13(id);
    if(h < 0.986) return 0.0;
    vec3 off = vec3(navalHash13(id + 11.0), navalHash13(id + 23.0),
                    navalHash13(id + 37.0)) - 0.5;
    float r = length(f - off*0.55);
    // a spread of magnitudes: a sky of equal stars reads as a texture, not a sky
    float mag = pow(fract(h*613.0), 1.5);
    return smoothstep(0.17, 0.02, r) * (0.28 + 0.95*mag);
  }

  vec3 navalSky(vec3 dir, vec3 sunDir, vec3 zenith, vec3 horizon){
    float h = clamp(dir.y, 0.0, 1.0);
    vec3 c = mix(horizon, zenith, pow(h, 0.55));

    /* Stars come out as the sun goes under, and they belong UNDER the haze —
       added before the horizon mix below, so they thin out toward the horizon
       the way real ones do rather than sitting on top of the murk. */
    float night = smoothstep(0.05, -0.12, sunDir.y);
    if(night > 0.001){
      c += vec3(0.88, 0.92, 1.00) * 1.7 * navalStars(normalize(dir))
           * night * smoothstep(-0.02, 0.16, dir.y);
    }

    float sd = max(dot(normalize(dir), sunDir), 0.0);
    // broad halo: air scatters the sun's light across a wide arc
    c += vec3(1.00, 0.82, 0.55) * pow(sd, 8.0) * 0.30;
    c += vec3(1.00, 0.90, 0.72) * pow(sd, 64.0) * 0.35;
    // haze thickens toward the horizon, where you look through more atmosphere
    c = mix(c, horizon * 1.04, smoothstep(0.22, -0.02, dir.y));
    return c;
  }`;

/* The eye and the sun, declared once however many patches ask for them.

   Several patches are chained onto the same material and each has to stand on
   its own, not knowing which others ran. Declaring `uniform vec3 uCam, uSun`
   in two of them is a redefinition and the whole fragment shader fails to
   compile, so the guard settles it whichever order they land in. */
Naval.SUN_UNIFORMS_GLSL =
  '#ifndef NAVAL_SUN_UNIFORMS\n#define NAVAL_SUN_UNIFORMS\nuniform vec3 uCam, uSun;\n#endif\n';

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
  /* Chain, never replace: the sailcloth already carries its own patch, and
     assigning over it would drop that one silently. Haze must also run LAST of
     the chain — it is the air in front of everything else, so whatever the
     other patches added has to be dimmed by it too. */
  const prev = mat.onBeforeCompile;
  mat.onBeforeCompile = (shader, renderer)=>{
    if(prev) prev(shader, renderer);
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

    const head = 'varying vec3 vHazeW;\n' + Naval.SUN_UNIFORMS_GLSL
               + 'uniform vec3 uZenith,uHorizon;\n'
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

/* Sailcloth is thin, and thin cloth is lit THROUGH.

   With the sun behind a sail, most of what you see never touched the front
   face at all: it came through the weave, and the sail reads brighter than
   anything lit from ahead, with the spars showing as dark bars against it. A
   plain lit material cannot do that — its back face simply goes black — which
   is why the canvas used to carry a flat emissive term just to stay readable.
   That covered the symptom: it glowed the same at noon, at dusk and with the
   sun dead ahead.

   The lobe here is the one the sea already uses for light coming through a
   crest: strongest when eye, cloth and sun line up, and only where the sun is
   on the face we are NOT looking at. The normal is taken raw from the geometry
   rather than the front-facing one, so a sail seen from either side gives the
   same answer. */
Naval.applySailLight = function(mat, u){
  if(!mat || mat.userData.sailLit) return;
  mat.userData.sailLit = true;

  /* Three caches compiled programs by customProgramCacheKey, which by default
     is the SOURCE TEXT of onBeforeCompile. Every material here is patched by
     the same haze closure, so they all hand back the same text — the captured
     variable that makes this one different does not appear in it. Without a key
     of its own the canvas is served the hull's program, and none of the code
     below ever runs: the patch is correct, composes correctly, and is silently
     never compiled. */
  const prevKey = mat.customProgramCacheKey.bind(mat);
  mat.customProgramCacheKey = () => prevKey() + '|sail-translucent';

  const prev = mat.onBeforeCompile;
  mat.onBeforeCompile = (shader, renderer)=>{
    if(prev) prev(shader, renderer);
    shader.uniforms.uCam = u.uCam;
    shader.uniforms.uSun = u.uSun;
    shader.uniforms.uSunCol = u.uSunCol;

    shader.vertexShader = 'varying vec3 vClothW; varying vec3 vClothN;\n'
      + shader.vertexShader.replace('#include <project_vertex>',
          '#include <project_vertex>\n'
        + '  vClothW = (modelMatrix * vec4(transformed,1.0)).xyz;\n'
        + '  vClothN = normalize(mat3(modelMatrix) * objectNormal);');

    shader.fragmentShader =
      'varying vec3 vClothW; varying vec3 vClothN;\n' + Naval.SUN_UNIFORMS_GLSL
      + 'uniform vec3 uSunCol;\n'
      + shader.fragmentShader.replace(/}\s*$/,
          '\n{ vec3 E = normalize(uCam - vClothW);\n'
        + '  vec3 N = normalize(vClothN);\n'
        + '  // the sun has to be on the far face, whichever face we are on\n'
        + '  float thru = max(-sign(dot(N, E)) * dot(N, uSun), 0.0);\n'
        + '  // and we have to be looking into it\n'
        + '  float lobe = pow(max(dot(-E, uSun), 0.0), 3.0);\n'
        + '  gl_FragColor.rgb += uSunCol * diffuseColor.rgb * thru * lobe * 1.4;\n'
        + '}\n}');
  };
  mat.needsUpdate = true;
};

/* Read the vessel's occlusion back into her own materials.

   Inserted at three's <aomap_fragment>, which is where it multiplies INDIRECT
   light and nothing else. That is the whole point: occlusion belongs to ambient
   light, not to the sun. A plank at the bottom of a hatchway that the sun still
   reaches is lit as brightly as one on the open deck — what it loses is the sky,
   and only the sky. Multiplying the final colour instead would paint grey into
   sunlight and look like dirt. */
Naval.applyShipAO = function(mat, u){
  if(!mat || mat.userData.shipAO) return;
  mat.userData.shipAO = true;
  // its own cache key, or three serves it a program compiled without this
  const prevKey = mat.customProgramCacheKey.bind(mat);
  mat.customProgramCacheKey = () => prevKey() + '|ship-ao';

  const prev = mat.onBeforeCompile;
  mat.onBeforeCompile = (shader, renderer)=>{
    if(prev) prev(shader, renderer);
    shader.uniforms.uAO = u.uAO;
    shader.uniforms.uAORes = u.uAORes;
    shader.fragmentShader = 'uniform sampler2D uAO;\nuniform vec2 uAORes;\n'
      + shader.fragmentShader.replace('#include <aomap_fragment>',
          '{ float ao = texture2D(uAO, gl_FragCoord.xy/uAORes).r;\n'
        + '  reflectedLight.indirectDiffuse *= ao;\n'
        + '  reflectedLight.indirectSpecular *= ao; }');
  };
  mat.needsUpdate = true;
};

Naval.Stage = class Stage {
  constructor(canvas){
    this.renderer = new THREE.WebGLRenderer({canvas, antialias:true, powerPreference:'high-performance'});
    this.renderer.setPixelRatio(Math.min(window.devicePixelRatio||1, 2));
    this.renderer.setClearColor(0x0a1a2b, 1);

    /* Shadows, and deliberately NOT screen-space ambient occlusion.

       SSAO wants a depth pass, which three renders with an override material.
       The sea is displaced in her own vertex shader, so an override would draw
       her flat: the occlusion would be wrong exactly where hull meets water,
       which is where the eye goes first. A cast shadow needs no such pass and
       buys more anyway — canvas darkening the deck, the hull shading its own
       lee side. Only the vessel takes part; the sea neither casts nor receives,
       for the same reason. */
    this.renderer.shadowMap.enabled = true;
    this.renderer.shadowMap.type = THREE.PCFSoftShadowMap;

    this.scene = new THREE.Scene();
    // No THREE fog: sea, hull and rig all share Naval.HAZE_GLSL instead, so they
    // dim at one rate. Two fog models can never agree at more than one range.
    this.camera = new THREE.PerspectiveCamera(55, 1, 0.7, 14000);

    this.zenith  = new THREE.Color(0x1e5c86);
    this.horizon = new THREE.Color(0xd2e3ec);

    this.sunDir = new THREE.Vector3();
    this.sun = new THREE.DirectionalLight(0xfff2dc, 2.1);
    this.sun.castShadow = true;
    this.sun.shadow.mapSize.set(2048, 2048);
    /* Canvas has no thickness, so a depth bias alone either lets the light leak
       through a sail or floats its shadow off the deck. normalBias pushes the
       lookup along the surface normal instead, which a zero-thickness sheet
       survives. */
    this.sun.shadow.bias = -0.0004;
    this.sun.shadow.normalBias = 0.6;
    this.scene.add(this.sun);
    this.scene.add(this.sun.target);          // aimed at the vessel each frame
    this.shadowSpan = 40;
    this.hemi = new THREE.HemisphereLight(0xbcd8ec, 0x1a3346, 0.9);
    this.scene.add(this.hemi);
    this.setSun(38, 225);            // elevation and bearing, in degrees

    this._addSky();
    this._buildEnvSky();
    this.refreshEnvironment(true);

    this.shipAO = new Naval.ShipAO(this.renderer, this.scene, this.camera);
    // her colour and depth on their own, so the sea can be looked through
    this.shipBuf = new Naval.ShipBuffer(this.renderer, this.scene, this.camera);
    // shared with the ship's materials, which read the buffer in screen space
    this.aoUniforms = { uAO:{value:this.shipAO.texture},
                        uAORes:{value:new THREE.Vector2(1,1)} };
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
    /* Cut right back now that scene.environment carries the sky's ambient: at
       its old strength the hemisphere counted that light a second time and
       flattened out exactly the directional shading the environment is there to
       provide. What is left is mostly the carrier for the lightning flash. */
    this._hemiBase = (0.14 + 0.14*t)*(1-night) + 0.04*night;
    this.hemi.intensity = this._hemiBase + (this.flash||0)*2.6;

    if(this.skyMat){
      this.skyMat.uniforms.uSun.value.copy(this.sunDir);
      this.skyMat.uniforms.uZenith.value.copy(this.zenith);
      this.skyMat.uniforms.uHorizon.value.copy(this.horizon);
    }
    // the environment IS that sky, so it has to follow the sun with it
    this.refreshEnvironment();
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

  /* The sky as LIGHT, not merely as a picture.

     She was lit by a two-colour HemisphereLight standing in for a sky the scene
     was already drawing properly a few metres away. Now the ambient comes from
     that sky: rendered to a cubemap, prefiltered, handed to the scene as its
     environment. Her upper works take the zenith's blue, her lee side the warm
     horizon, and a surface turned away from the light goes dark on its own —
     which is the one thing a hemisphere light can never do, having no notion of
     where the sun is.

     It calls the same navalSky and SHARES the dome's uniform OBJECTS rather
     than copies, so there is still exactly one sky and nothing here can drift
     from what is overhead. The sun's disc is deliberately left out: the
     DirectionalLight already stands for it, and baking it in would light her
     twice over. */
  _buildEnvSky(){
    const u = this.skyMat.uniforms;
    const mat = new THREE.ShaderMaterial({
      side:THREE.BackSide, depthWrite:false,
      uniforms:{ uSun:u.uSun, uZenith:u.uZenith, uHorizon:u.uHorizon, uFlash:u.uFlash },
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
          c += vec3(0.62,0.70,0.92) * uFlash * (1.6 - 0.9*clamp(vDir.y,0.0,1.0));
          gl_FragColor = vec4(c, 1.0);
        }`
    });
    this._envScene = new THREE.Scene();
    this._envScene.add(new THREE.Mesh(new THREE.SphereGeometry(10, 32, 16), mat));
    this._pmrem = new THREE.PMREMGenerator(this.renderer);
  }

  /* Rebuild it. Throttled, because the sun slider fires on every pixel of a
     drag and a prefiltered cubemap costs a few milliseconds: the ambient does
     not have to be frame-exact while the slider is still moving. A skipped
     rebuild is remembered and made good in render(). */
  refreshEnvironment(force){
    if(!this._pmrem) return;
    const now = performance.now();
    if(!force && now - (this._envAt || 0) < 120){ this._envDue = true; return; }
    this._envAt = now; this._envDue = false;
    const rt = this._pmrem.fromScene(this._envScene);
    if(this._envRT) this._envRT.dispose();
    this._envRT = rt;
    this.scene.environment = rt.texture;
  }

  resize(){
    const w = innerWidth, h = innerHeight;
    this.renderer.setSize(w, h, false);
    this.camera.aspect = w/h;
    this.camera.updateProjectionMatrix();
  }

  /* Size the shadow box to the vessel. It must clear her trucks as well as her
     length — a frigate's mainmast stands higher above the water than she is
     broad — so the span is taken off her length and squared up. */
  frameVessel(L){
    this.shadowSpan = L*0.95;
    const c = this.sun.shadow.camera;
    c.left = -this.shadowSpan; c.right = this.shadowSpan;
    c.top  =  this.shadowSpan; c.bottom = -this.shadowSpan;
    c.near = 1; c.far = L*8;
    c.updateProjectionMatrix();
  }

  /* Walk the sun along with her. A directional light's shadow only covers the
     box around its own position, and she sails out of a box left at the origin
     within a minute — her shadows would simply stop. */
  aimSun(shipPos){
    this.sun.target.position.copy(shipPos);
    this.sun.position.copy(this.sunDir).multiplyScalar(this.shadowSpan*3).add(shipPos);
  }

  render(){
    // make good a rebuild the throttle skipped, once the slider has settled
    if(this._envDue && performance.now() - this._envAt >= 120) this.refreshEnvironment(true);
    // her occlusion, before she is drawn with it
    this.shipAO.render();
    this.renderer.getDrawingBufferSize(this.aoUniforms.uAORes.value);
    // and her, alone, for the sea to see her through
    this.shipBuf.render();
    this.renderer.render(this.scene, this.camera);
  }
};
