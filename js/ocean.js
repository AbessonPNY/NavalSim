/* The sea: a sum of Gerstner waves.
   One set of parameters drives BOTH the GPU mesh and the CPU height field the
   buoyancy solver samples, so the hull rides the very waves you see. Wave speed
   comes from the deep-water dispersion relation ω = √(gk), so long swells
   genuinely outrun short chop.

   The shading is built on what actually makes water read as water:
     · it is a MIRROR first — most of what you see is the sky bent by Fresnel,
       not the water's own colour;
     · the sun does not make one highlight but a GLITTER PATH, because countless
       wave facets each catch it — a microfacet lobe, widened by local chop;
     · crests GLOW when the sun is behind them, light scattering through thin
       water;
     · far away, wave detail must be flattened or it aliases into shimmer. */
window.Naval = window.Naval || {};

Naval.Ocean = class Ocean {
  constructor(scene, sunDir, stage){
    const C = Naval.Config;
    this.C = C;
    this.waves = [];
    this.windSpeed = 0;                    // m/s, true wind
    this.windVec = new THREE.Vector3();    // true wind velocity (blows toward)

    const half = C.OCEAN_SIZE * 0.5;
    const geo = new THREE.PlaneGeometry(C.OCEAN_SIZE, C.OCEAN_SIZE, C.OCEAN_SEG, C.OCEAN_SEG);
    geo.rotateX(-Math.PI/2);

    // Merge Three's fog uniforms (fogColor / fogDensity …) so the injected fog
    // chunks have theirs — Scene.fog then fills them each frame. Omitting this
    // makes the renderer read `uniforms.fogColor.value` off undefined.
    this.uniforms = Object.assign(
      THREE.UniformsUtils.clone(THREE.UniformsLib.fog),
      {
        uTime:{value:0},
        uWaveA:{value:Array.from({length:C.NWAVES},()=>new THREE.Vector4())}, // dx,dz,amp,k
        uWaveB:{value:Array.from({length:C.NWAVES},()=>new THREE.Vector2())}, // omega,Q
        uSun:{value:sunDir.clone()},
        uCam:{value:new THREE.Vector3()},
        uHalf:{value:half},
        uAmpMax:{value:1.0},
        uDeep:{value:new THREE.Color(0x0e3347)},
        uShallow:{value:new THREE.Color(0x2f8fa8)},
        uSSS:{value:new THREE.Color(0x2f9e86)},          // colour light takes through a crest
        uZenith:{value:(stage ? stage.zenith : new THREE.Color(0x1e5c86)).clone()},
        uHorizon:{value:(stage ? stage.horizon : new THREE.Color(0xd2e3ec)).clone()},
        uWind:{value:new THREE.Vector2(0,1)},   // ripples travel with the wind
        uRipple:{value:1.0},                    // ripple strength, grows with sea state
        uHaze:{value:0.0016},                   // extinction per metre at sea level
        uHazeH:{value:110.0},                   // scale height of the haze layer (m)
        // the hull, so the sea can foam where she cuts it
        uShipPos:{value:new THREE.Vector3(0,0,0)},
        uShipFwd:{value:new THREE.Vector2(0,1)},
        uShipHalf:{value:new THREE.Vector2(12,3)},
        uShipSpeed:{value:0},
      }
    );

    const mat = new THREE.ShaderMaterial({
      uniforms:this.uniforms,
      // the sea carries its own layered haze; Three's flat fog would double it
      fog:false,
      defines:{NW:C.NWAVES},
      vertexShader:`
        uniform float uTime, uHalf; uniform vec4 uWaveA[NW]; uniform vec2 uWaveB[NW];
        varying vec3 vN; varying vec3 vW; varying float vFoam; varying float vRel;
        void main(){
          /* Spend vertices where they are seen. The grid is uniform in the
             buffer, so remap it toward the centre: quads are about a metre
             beside the hull and tens of metres out at the horizon, for the same
             triangle count. Cubic in the normalised coordinate, with a linear
             floor so the very centre does not collapse to nothing. */
          vec3 lp = position;
          vec2 q = lp.xz / uHalf;
          vec2 aq = abs(q);
          lp.xz = sign(q) * (aq * (0.06 + 0.94*aq*aq)) * uHalf;

          /* The plane is re-centred on the camera every frame, so the wave
             phase MUST come from world space — otherwise the swell would be
             glued to the camera (no sense of headway) and would drift out of
             step with the CPU height field the buoyancy solver samples. */
          vec3 w0 = (modelMatrix * vec4(lp,1.0)).xyz;
          vec3 p = w0; vec3 n = vec3(0.0);
          float steep = 0.0, height = 0.0;

          for(int i=0;i<NW;i++){
            vec2 d = uWaveA[i].xy; float amp=uWaveA[i].z; float k=uWaveA[i].w;
            float omega=uWaveB[i].x; float Q=uWaveB[i].y;
            float f = k*dot(d, w0.xz) - omega*uTime;
            float c = cos(f), s = sin(f);
            p.x += Q*amp*d.x*c;
            p.z += Q*amp*d.y*c;
            p.y += amp*s;
            height += amp*s;
            float WA = k*amp;
            n.x -= d.x*WA*c; n.z -= d.y*WA*c; n.y -= Q*WA*s;
            steep += Q*WA*max(s,0.0);
          }

          vN = normalize(vec3(n.x, 1.0-n.y, n.z));
          vW = p;
          vFoam = smoothstep(0.55, 0.95, steep);
          vRel = height;                     // signed height, for the crest glow
          vec4 mvPosition = viewMatrix*vec4(p,1.0);   // p is already world space
          gl_Position = projectionMatrix*mvPosition;
        }`,
      fragmentShader:`
        precision highp float;
        uniform vec3 uSun,uCam,uDeep,uShallow,uSSS,uZenith,uHorizon;
        uniform float uTime, uAmpMax, uRipple, uShipSpeed;
        uniform vec2 uWind, uShipFwd, uShipHalf;
        uniform vec3 uShipPos;
        varying vec3 vN; varying vec3 vW; varying float vFoam; varying float vRel;
        ${Naval.SKY_GLSL}
        ${Naval.HAZE_GLSL}

        // cheap value noise, for foam that breaks up instead of banding
        float hash(vec2 p){ return fract(sin(dot(p, vec2(127.1,311.7)))*43758.5453); }
        float noise(vec2 p){
          vec2 i = floor(p), f = fract(p);
          f = f*f*(3.0-2.0*f);
          return mix(mix(hash(i), hash(i+vec2(1,0)), f.x),
                     mix(hash(i+vec2(0,1)), hash(i+vec2(1,1)), f.x), f.y);
        }
        float fbm(vec2 p){
          return noise(p)*0.55 + noise(p*2.03 + 11.0)*0.28 + noise(p*4.11 - 7.0)*0.17;
        }

        /* Ripples. The mesh only carries six long waves; everything finer than a
           couple of metres has to live in the normal, or the sea looks like
           moulded plastic. Three octaves of noise, drifting with the wind at
           different rates, differentiated to give a slope. */
        vec3 rippleNormal(vec2 p, float fade){
          if(fade <= 0.001) return vec3(0.0,1.0,0.0);
          vec2 d1 = uWind*uTime*0.65, d2 = uWind*uTime*0.31;
          float e = 0.35;
          vec2 q = p*0.22;
          float h  = fbm(q + d1*0.22) + fbm(q*2.7 - d2*0.5)*0.5;
          float hx = fbm(q + vec2(e,0.0)*0.22 + d1*0.22) + fbm((q + vec2(e,0.0))*2.7 - d2*0.5)*0.5;
          float hz = fbm(q + vec2(0.0,e)*0.22 + d1*0.22) + fbm((q + vec2(0.0,e))*2.7 - d2*0.5)*0.5;
          float s = 1.35 * uRipple * fade;
          return normalize(vec3(-(hx-h)*s, 1.0, -(hz-h)*s));
        }

        void main(){
          vec3 V = normalize(uCam - vW);
          float dist = length(uCam - vW);

          /* Flatten the normal with distance. Without this, wave detail smaller
             than a pixel turns into a boiling shimmer at the horizon — the
             single ugliest artefact in a naive ocean shader. */
          float far = smoothstep(90.0, 1400.0, dist);
          vec3 N = normalize(mix(normalize(vN), vec3(0.0,1.0,0.0), far*0.92));

          /* Graft the fine ripples onto the wave normal, fading them out with
             distance — below a pixel they would only alias into shimmer. */
          float rFade = 1.0 - smoothstep(25.0, 260.0, dist);
          vec3 rn = rippleNormal(vW.xz, rFade);
          N = normalize(vec3(N.x + rn.x, N.y, N.z + rn.z));

          // --- the mirror ---
          vec3 R = reflect(-V, N);
          R.y = abs(R.y) + 0.003;            // never sample below the horizon
          vec3 sky = navalSky(R, uSun, uZenith, uHorizon);

          // Schlick, with water's real normal-incidence reflectance (~2%).
          // Nearly all of the reflection therefore lives at grazing angles.
          float c = max(dot(N,V), 0.0);
          float F = 0.02 + 0.98*pow(1.0 - c, 5.0);

          /* --- the body of the water ---
             Looking straight down you see into it; at a slant you skim the top.
             Most of a sea's own colour is skylight scattered back up out of it,
             not the water pigment alone — without that term it reads as ink. */
          vec3 body = mix(uDeep, uShallow, pow(c, 1.6));
          body += uHorizon * 0.13;

          /* Subsurface glow: a crest lit from behind passes light through, and
             turns that characteristic green. Strongest on the wave tops, and
             only when the sun is on the far side. */
          float back = pow(max(dot(-V, uSun)*0.5 + 0.5, 0.0), 3.0);
          float crest = smoothstep(0.15, 1.0, vRel / max(uAmpMax, 0.05));
          body += uSSS * crest * back * 0.55 * (1.0 - far);

          vec3 col = mix(body, sky, F);

          /* --- the glitter path ---
             A GGX lobe, not a single mirror point. Roughness grows with local
             chop and with distance, which is exactly what smears the sun into
             the long shimmering road you see on any real sea. */
          vec3 H = normalize(uSun + V);
          float rough = clamp(0.055 + 0.30*vFoam + 0.30*far, 0.03, 0.6);
          float a = rough*rough;
          float nh = max(dot(N,H), 0.0);
          float dd = nh*nh*(a*a - 1.0) + 1.0;
          float D = (a*a) / (3.14159*dd*dd);
          /* Fresnel for a microfacet is taken across the HALF vector, not the
             surface normal. Using N·V here (the earlier mistake) collapsed the
             term to ~2% at every angle you actually look at the sea from, and
             killed the glitter entirely. */
          float voh = max(dot(V,H), 0.0);
          float Fs = 0.02 + 0.98*pow(1.0 - voh, 5.0);
          float shadow = max(dot(N, uSun), 0.0);        // no glitter on a back face
          col += vec3(1.0, 0.96, 0.86) * D * Fs * shadow * 2.4;

          // a little diffuse tint, so troughs are not dead flat
          col += uShallow * max(dot(N, uSun), 0.0) * 0.05 * (1.0-far);

          // --- foam ---
          float fn = fbm(vW.xz*0.55 + uTime*0.05);
          float crestFoam = clamp(vFoam * (0.35 + 1.1*fn), 0.0, 1.0);

          /* Foam where the hull cuts the water. Work in ship-local coordinates
             and measure an elliptical distance to her waterline: a bright collar
             right at the plating, then a broader band that only appears once she
             has way on, and a tail dragged astern. */
          vec2 rel = vW.xz - uShipPos.xz;
          vec2 f2 = normalize(uShipFwd);
          vec2 r2 = vec2(f2.y, -f2.x);
          vec2 loc = vec2(dot(rel, r2)/uShipHalf.y, dot(rel, f2)/uShipHalf.x);
          float ed = length(loc);                        // 1.0 = on the waterline
          float way = clamp(uShipSpeed/3.0, 0.0, 1.0);

          float collar = (1.0 - smoothstep(1.0, 1.45, ed)) * step(0.98, ed);
          float bow    = (1.0 - smoothstep(1.0, 2.1 + way, ed)) * step(1.0, ed)
                         * smoothstep(-0.2, 0.7, loc.y) * way;
          float tail   = (1.0 - smoothstep(0.0, 3.5 + 4.0*way, -loc.y))
                         * (1.0 - smoothstep(0.7, 2.0, abs(loc.x)))
                         * step(loc.y, -0.6) * way * 0.7;
          float hullFoam = clamp(collar*0.70 + bow*0.55 + tail*0.8, 0.0, 1.0)
                           * (0.45 + 0.80*fn);

          float foam = clamp(crestFoam + hullFoam, 0.0, 1.0) * (1.0 - far*0.85);
          col = mix(col, vec3(0.92,0.96,0.98), foam*0.85);

          /* Fade into the sky that lies exactly behind this patch of water,
             not into one flat fog colour. That is what makes the horizon
             dissolve instead of ending on a line: at the limit the sea and the
             sky in that direction are the same colour, so there is no edge left
             to see. */
          vec3 viewDir = normalize(vW - uCam);
          vec3 hazeCol = navalSky(viewDir, uSun, uZenith, uHorizon);
          col = mix(col, hazeCol, hazeAlong(uCam, vW));

          gl_FragColor = vec4(col,1.0);
        }`
    });

    this.mesh = new THREE.Mesh(geo, mat);
    this.mesh.frustumCulled = false;   // it is always under the camera
    scene.add(this.mesh);
  }

  /* seaState is the Beaufort number; windDeg is the bearing the wind blows
     FROM, as a seaman states it, on the same compass as the heading. */
  setSeaState(seaState, windDeg){
    const C = this.C, G = C.G;
    this.waves = [];
    const wr = windDeg*Math.PI/180;
    const s = Math.max(0, seaState);

    // true wind from the Beaufort number: v ≈ 0.836·B^1.5 m/s
    this.windSpeed = 0.836*Math.pow(s,1.5) + 0.8;
    this.windVec.set(-Math.sin(wr)*this.windSpeed, 0, -Math.cos(wr)*this.windSpeed);

    const baseLen = 26 + s*20;          // metres
    const baseAmp = 0.02 + s*0.20;      // amplitude ≈ half the wave height
    const chop = 0.35 + s*0.075;        // steepness budget
    let ampSum = 0;
    for(let i=0;i<C.NWAVES;i++){
      const f = Math.pow(0.62, i);                       // shorter each harmonic
      const L = baseLen * f * (0.85 + (i%2?0.2:0));
      const amp = baseAmp * Math.pow(0.72, i);
      const dir = wr + (i - (C.NWAVES-1)/2) * (0.34 + s*0.03);
      const k = 2*Math.PI / Math.max(2, L);
      const omega = Math.sqrt(G * k);                    // deep-water dispersion
      // steepness normalised so the crest never loops (Σ Q·k·A < 1)
      const Q = Math.min(0.85, chop / (k*amp*C.NWAVES + 1e-4));
      // seas run with the wind, i.e. away from the bearing it blows from
      this.waves.push({ dx:-Math.sin(dir), dz:-Math.cos(dir), amp, k, omega, Q });
      ampSum += amp;
    }
    // reference height for the crest glow, so it scales with the sea state
    this.uniforms.uAmpMax.value = Math.max(0.05, ampSum*0.8);
    this.syncUniforms();
    this.syncWind();
  }

  syncUniforms(){
    const C = this.C;
    for(let i=0;i<C.NWAVES;i++){
      const w = this.waves[i] || {dx:0,dz:0,amp:0,k:1,omega:0,Q:0};
      this.uniforms.uWaveA.value[i].set(w.dx, w.dz, w.amp, w.k);
      this.uniforms.uWaveB.value[i].set(w.omega, w.Q);
    }
  }

  // Surface height at world (x,z). Optionally fills outNormal.
  sample(x, z, t, outNormal){
    let y = 0, nx = 0, nz = 0, ny = 0;
    for(let i=0;i<this.waves.length;i++){
      const w = this.waves[i];
      const f = w.k*(w.dx*x + w.dz*z) - w.omega*t;
      const c = Math.cos(f), sn = Math.sin(f);
      y += w.amp * sn;
      const WA = w.k * w.amp;
      nx -= w.dx * WA * c;
      nz -= w.dz * WA * c;
      ny -= w.Q  * WA * sn;
    }
    if(outNormal) outNormal.set(nx, 1.0 - ny, nz).normalize();
    return y;
  }

  // Keep the plane under the camera (the infinite-sea illusion) and feed time.
  update(t, cameraPos){
    this.mesh.position.x = cameraPos.x;
    this.mesh.position.z = cameraPos.z;
    this.uniforms.uTime.value = t;
    this.uniforms.uCam.value.copy(cameraPos);
  }

  // Tell the sea where the hull is, so it can foam along her waterline.
  trackShip(body, spec){
    const u = this.uniforms;
    u.uShipPos.value.copy(body.pos);
    this._sf = this._sf || new THREE.Vector3();
    this._sf.set(0,0,1).applyQuaternion(body.quat);
    u.uShipFwd.value.set(this._sf.x, this._sf.z).normalize();
    u.uShipHalf.value.set(spec.L*0.5, spec.B*0.5);
    u.uShipSpeed.value = Math.hypot(body.vel.x, body.vel.z);
  }

  // Ripples travel with the wind, and grow with it.
  syncWind(){
    const w = this.windVec, s = Math.hypot(w.x, w.z);
    if(s > 1e-4) this.uniforms.uWind.value.set(w.x/s, w.z/s);
    this.uniforms.uRipple.value = Math.min(1.6, 0.35 + this.windSpeed*0.075);
    /* A blow tears spray off the crests and thickens the air, so the horizon
       closes in as the sea gets up: a calm day sees perhaps 4 km, a gale less
       than one. The scale height rises too — the murk stands taller. */
    this.uniforms.uHaze.value  = 0.00055 + this.windSpeed*0.00023;
    this.uniforms.uHazeH.value = 90 + this.windSpeed*7.0;
  }
};
