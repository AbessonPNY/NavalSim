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
    this.swell = 1.35;               // creux multiplier, driven by the console
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
        uSeg:{value:C.OCEAN_SEG},
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
        // planar reflection of the world above the water
        uReflTex:{value:null},
        uReflMat:{value:new THREE.Matrix4()},
        uReflOn:{value:0.0},
        // the persistent foam field
        uFoamTex:{value:null},
        uFoamOrigin:{value:new THREE.Vector2()},
        uFoamSize:{value:1.0},
        uFoamOn:{value:0.0},
        uFlash:{value:0.0},
      }
    );

    this._initReflection();

    const mat = new THREE.ShaderMaterial({
      uniforms:this.uniforms,
      // the sea carries its own layered haze; Three's flat fog would double it
      fog:false,
      defines:{NW:C.NWAVES},
      vertexShader:`
        uniform float uTime, uHalf, uSeg; uniform vec4 uWaveA[NW]; uniform vec2 uWaveB[NW];
        uniform mat4 uReflMat;
        varying vec3 vN; varying vec3 vW; varying float vFoam; varying float vRel;
        varying vec4 vRefl; varying float vSpacing;
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

          /* How many metres this quad spans, straight from the derivative of
             the remap above. Everything that follows is band-limited against
             THIS, not against distance: one criterion, so no seam can appear
             where two different distance thresholds would have disagreed. */
          vec2 dsp = (vec2(0.06) + 2.82*aq*aq) * (uHalf * 2.0 / uSeg);
          float spacing = max(dsp.x, dsp.y);
          vSpacing = spacing;

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

            /* Drop any wave this quad is too coarse to carry. A wavelength
               needs several vertices across it; below that the mesh samples it
               aliased and the beat between the two frequencies is precisely the
               moire you see. Fading the amplitude out removes the beat instead
               of letting it fight the grid.
               Near the hull spacing is about a metre, so nothing is removed
               there and the visible surface still matches the height field the
               buoyancy solver samples. */
            float lambda = 6.28318530718 / k;
            amp *= smoothstep(2.5, 6.0, lambda / spacing);

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
          /* Where this patch of water lands in the reflection image. Taken from
             the undisplaced plane, not from p: sampling with the displaced
             position drags the reflection sideways with every crest and the
             mirrored hull wobbles like jelly. */
          vRefl = uReflMat * vec4(w0.x, 0.0, w0.z, 1.0);
          vec4 mvPosition = viewMatrix*vec4(p,1.0);   // p is already world space
          gl_Position = projectionMatrix*mvPosition;
        }`,
      fragmentShader:`
        precision highp float;
        uniform vec3 uSun,uCam,uDeep,uShallow,uSSS,uZenith,uHorizon;
        uniform float uTime, uAmpMax, uRipple, uShipSpeed;
        uniform vec2 uWind, uShipFwd, uShipHalf;
        uniform vec3 uShipPos;
        uniform sampler2D uReflTex; uniform float uReflOn;
        uniform sampler2D uFoamTex; uniform float uFoamOn, uFoamSize, uFlash;
        uniform vec2 uFoamOrigin;
        varying vec3 vN; varying vec3 vW; varying float vFoam; varying float vRel;
        varying vec4 vRefl; varying float vSpacing;
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

          /* Graft the fine ripples onto the wave normal. They are band-limited
             against the SAME quad size as the waves: ripples are metre-scale, so
             they must go once a quad spans several metres, or they alias just
             as the waves did. */
          float rFade = (1.0 - smoothstep(0.8, 4.0, vSpacing))
                      * (1.0 - smoothstep(25.0, 260.0, dist));
          vec3 rn = rippleNormal(vW.xz, rFade);
          N = normalize(vec3(N.x + rn.x, N.y, N.z + rn.z));

          // --- the mirror ---
          vec3 R = reflect(-V, N);
          R.y = abs(R.y) + 0.003;            // never sample below the horizon
          vec3 sky = navalSky(R, uSun, uZenith, uHorizon);

          /* The mirrored world, over the analytic sky. The lookup is nudged by
             the surface slope so the reflection ripples with the waves instead
             of sitting flat; the nudge shrinks with distance, or far water
             smears. Outside the mirror's frame there is nothing to sample, so
             fade back to the sky rather than clamp and streak the edges. */
          if(uReflOn > 0.5){
            vec2 ruv = vRefl.xy / max(vRefl.w, 1e-4);
            vec2 wobble = vec2(N.x, N.z) * 0.055 * (1.0 - far);
            vec2 suv = ruv + wobble;
            vec2 g = smoothstep(0.0, 0.06, suv) * (1.0 - smoothstep(0.94, 1.0, suv));
            float inside = g.x * g.y * (1.0 - far);
            vec3 mirrored = texture2D(uReflTex, clamp(suv, 0.001, 0.999)).rgb;
            sky = mix(sky, mirrored, inside);
          }

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
          /* Widen the lobe as the mesh coarsens. A tight highlight riding a
             normal the geometry can no longer resolve is the other half of the
             moire: it sparkles on and off between neighbouring quads. Roughness
             stands in for the wave detail that was removed. */
          float coarse = smoothstep(0.8, 6.0, vSpacing);
          float rough = clamp(0.055 + 0.30*vFoam + 0.24*far + 0.26*coarse, 0.03, 0.6);
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

          /* Only the crisp collar and bow wave stay instantaneous — they belong
             to the hull and must move with her. The lingering trail astern now
             comes from the foam field, which leaves it in the water. */
          /* Measure the gap to the waterline in METRES, not in the normalised
             elliptical units: a fixed band of that distance is thin abeam but
             metres thick ahead of the stem, because the ellipse is far longer
             than it is wide. That is what bloated the collar at the ends. */
          float gapM = length(rel) * (1.0 - 1.0/max(ed, 1e-3));
          float collar = (1.0 - smoothstep(0.0, 0.85, gapM)) * step(0.98, ed);
          float bow    = (1.0 - smoothstep(0.0, 1.6 + 3.0*way, gapM)) * step(1.0, ed)
                         * smoothstep(-0.2, 0.7, loc.y) * way;
          float hullFoam = clamp(collar*0.70 + bow*0.55, 0.0, 1.0)
                           * (0.45 + 0.80*fn);

          /* Foam that was laid down earlier and is still dispersing. Sampled in
             world space, so it stays where it was made while the ship sails on. */
          float persist = 0.0;
          if(uFoamOn > 0.5){
            vec2 fuv = (vW.xz - uFoamOrigin) / uFoamSize;
            if(fuv.x > 0.0 && fuv.x < 1.0 && fuv.y > 0.0 && fuv.y < 1.0){
              float edge = min(min(fuv.x, 1.0-fuv.x), min(fuv.y, 1.0-fuv.y));
              persist = texture2D(uFoamTex, fuv).r
                      * smoothstep(0.0, 0.03, edge);   // no hard border
            }
          }
          // the old foam is torn and streaky, not a flat wash
          persist *= (0.35 + 0.95*fn);

          float foam = clamp(max(crestFoam, persist) + hullFoam, 0.0, 1.0)
                     * (1.0 - far*0.85);
          col = mix(col, vec3(0.92,0.96,0.98), foam*0.85);

          /* Fade into the sky that lies exactly behind this patch of water,
             not into one flat fog colour. That is what makes the horizon
             dissolve instead of ending on a line: at the limit the sea and the
             sky in that direction are the same colour, so there is no edge left
             to see. */
          vec3 viewDir = normalize(vW - uCam);
          vec3 hazeCol = navalSky(viewDir, uSun, uZenith, uHorizon);
          col = mix(col, hazeCol, hazeAlong(uCam, vW));

          // a discharge overhead lights the water as well as the sky

          col += vec3(0.42,0.50,0.68) * uFlash * (0.55 + 0.9*foam);

          gl_FragColor = vec4(col,1.0);
        }`
    });

    this.mesh = new THREE.Mesh(geo, mat);
    this.mesh.frustumCulled = false;   // it is always under the camera
    scene.add(this.mesh);
  }

  /* Planar reflection.
     The analytic sky alone cannot show the hull in the water, and a vessel with
     no reflection reads as pasted onto the image. So each frame the world above
     the waterline is rendered once more from a camera mirrored through that
     plane, and the sea samples it.

     Three points of care:
     · the virtual camera is rebuilt with lookAt from a REFLECTED up vector, not
       by multiplying in a mirror matrix — that would flip handedness and turn
       every hull inside out;
     · a clipping plane keeps the submerged part of the hull out of the
       reflection, where it has no business being;
     · the sea itself is hidden for the pass, or it would reflect itself. */
  _initReflection(){
    const size = 1024;
    this.reflTarget = new THREE.WebGLRenderTarget(size, size, {
      minFilter: THREE.LinearFilter, magFilter: THREE.LinearFilter,
      type: THREE.UnsignedByteType, depthBuffer: true, stencilBuffer: false
    });
    this.uniforms.uReflTex.value = this.reflTarget.texture;
    this.reflCam = new THREE.PerspectiveCamera();
    this._textureMatrix = new THREE.Matrix4();
    this._clip = new THREE.Plane(new THREE.Vector3(0,1,0), 0);
    this._rotM = new THREE.Matrix4();
    this._look = new THREE.Vector3();
    this._view = new THREE.Vector3();
    this._up   = new THREE.Vector3();
    this._normal = new THREE.Vector3(0,1,0);
    this._camWorld = new THREE.Vector3();
  }

  renderReflection(renderer, scene, camera){
    const rc = this.reflCam;
    camera.getWorldPosition(this._camWorld);
    this._rotM.extractRotation(camera.matrixWorld);

    // mirror the eye through the water plane (y = 0)
    rc.position.set(this._camWorld.x, -this._camWorld.y, this._camWorld.z);

    // and mirror what it looks at
    this._look.set(0,0,-1).applyMatrix4(this._rotM).add(this._camWorld);
    this._view.set(this._look.x, -this._look.y, this._look.z);

    // reflect the up vector too, so lookAt rebuilds a proper right-handed
    // basis and the image comes out mirrored without inverted winding
    this._up.set(0,1,0).applyMatrix4(this._rotM).reflect(this._normal);
    rc.up.copy(this._up);
    rc.lookAt(this._view);
    rc.near = camera.near; rc.far = camera.far;
    rc.projectionMatrix.copy(camera.projectionMatrix);
    rc.updateMatrixWorld();

    // world position → reflection texture coordinates, exactly
    this._textureMatrix.set(0.5,0,0,0.5, 0,0.5,0,0.5, 0,0,0.5,0.5, 0,0,0,1);
    this._textureMatrix.multiply(rc.projectionMatrix);
    this._textureMatrix.multiply(rc.matrixWorldInverse);
    this.uniforms.uReflMat.value.copy(this._textureMatrix);

    const seaWasVisible = this.mesh.visible;
    const prevPlanes = renderer.clippingPlanes;
    const prevTarget = renderer.getRenderTarget();
    const prevCam = this.uniforms.uCam.value.clone();

    this.mesh.visible = false;                    // the sea must not mirror itself
    renderer.clippingPlanes = [this._clip];       // keep only what is above water
    this.uniforms.uCam.value.copy(rc.position);   // haze seen from the mirrored eye

    renderer.setRenderTarget(this.reflTarget);
    renderer.clear();
    renderer.render(scene, rc);

    renderer.setRenderTarget(prevTarget);
    renderer.clippingPlanes = prevPlanes;
    this.mesh.visible = seaWasVisible;
    this.uniforms.uCam.value.copy(prevCam);
    this.uniforms.uReflOn.value = 1.0;
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

    /* --- the wave spectrum ---
       The old harmonic series (wavelengths in 0.62^i) was arbitrary: it made a
       tidy but artificial sea, every component a fixed ratio of the last.
       A real wind sea follows a measured spectrum. JONSWAP is the standard one:
       a Pierson-Moskowitz shape sharpened by a peak factor γ, since a fetch-
       limited sea concentrates more of its energy near the peak than a fully
       developed ocean does.

       JONSWAP fixes the SHAPE — which frequencies carry the energy and how they
       spread. The Beaufort table fixes the SCALE, so the significant height the
       console announces is the height you actually get. */
    const U = Math.max(0.6, this.windSpeed);
    /* Peak frequency. Pierson-Moskowitz assumes a fully developed ocean, which
       puts the peak far too low — it produced 800 m swells in a gale, longer
       than any real sea and far too long to disturb a ship at all. Coastal seas
       are fetch-limited, so the peak sits higher: this correction lands the peak
       period near 5 s in a fresh breeze and 11 s in a gale, as observed. */
    const wp = (0.855*G/U) * (1.05 + 0.015*U);
    const gamma = 3.3, alpha = 0.0081;

    const N = C.NWAVES;
    const wLo = wp*0.62, wHi = wp*3.4;    // where the energy actually lives
    const raw = [];
    let m0 = 0;
    for(let i=0;i<N;i++){
      // geometric spacing: the low frequencies deserve the resolution
      const f0 = Math.pow(wHi/wLo, i/N), f1 = Math.pow(wHi/wLo, (i+1)/N);
      const w0 = wLo*f0, w1 = wLo*f1;
      const w = 0.5*(w0+w1), dw = w1-w0;

      const sig = w <= wp ? 0.07 : 0.09;
      const r = Math.exp(-Math.pow(w-wp, 2) / (2*sig*sig*wp*wp));
      const S = (alpha*G*G/Math.pow(w,5)) * Math.exp(-1.25*Math.pow(wp/w,4)) * Math.pow(gamma, r);

      const amp = Math.sqrt(Math.max(0, 2*S*dw));
      m0 += 0.5*amp*amp;

      /* Directional spreading: short waves fan out far more than the long
         swell, which is why a real sea looks confused up close and orderly at
         the horizon. Deterministic offsets, so the CPU and GPU never disagree. */
      const spread = (0.16 + 0.55*Math.min(1, w/wp - 0.4)) * (0.34 + 0.045*s);
      const u = ((i*7)%N)/(N-1)*2 - 1;    // spread the components, no randomness
      raw.push({ amp, w, dir: wr + spread*u });
    }

    // anchor the scale: match the significant height this sea state announces
    const b = C.BEAUFORT;
    const lo = Math.max(0, Math.min(9, Math.floor(s)));
    const hi = Math.max(0, Math.min(9, Math.ceil(s)));
    /* The swell control multiplies the significant height above what the
       Beaufort table states. It exists because a spectrum spread over eighteen
       components and a fan of directions reads lower than six aligned
       harmonics did, even at the same Hs — the crests no longer line up. Past
       about 1.6 a vessel can genuinely be rolled over, which is the point. */
    const hsTarget = (parseFloat(b[lo][2])
                   + (parseFloat(b[hi][2]) - parseFloat(b[lo][2]))*(s - lo)) * this.swell;
    const hsRaw = 4*Math.sqrt(Math.max(m0, 1e-9));
    const scale = hsRaw > 1e-6 ? hsTarget/hsRaw : 0;

    const chop = 0.35 + s*0.075;          // steepness budget
    let ampSum = 0;
    for(const r of raw){
      const amp = r.amp*scale;
      const k = r.w*r.w/G;                // deep-water dispersion, k = ω²/g
      // steepness normalised so the crest never loops (Σ Q·k·A < 1)
      const Q = Math.min(0.85, chop / (k*amp*N + 1e-4));
      // seas run with the wind, i.e. away from the bearing it blows from
      this.waves.push({ dx:-Math.sin(r.dir), dz:-Math.cos(r.dir),
                        amp, k, omega:r.w, Q, L:2*Math.PI/k });
      ampSum += amp;
    }
    /* Order by ENERGY, not by length, and let the solver and the foam pass take
       the head of that list. Taking the longest instead was wrong: in a gale the
       longest components run to hundreds of metres, and a swell far longer than
       the ship merely lifts her bodily — it is the band around the spectral peak
       that actually works her. Sorting by amplitude puts that band first. */
    this.waves.sort((a,b2) => b2.amp - a.amp);
    this.cpuWaves = this.waves.slice(0, C.NWAVES_CPU);

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

  /* Surface height at world (x,z). Optionally fills outNormal.
     Only the long components are integrated here. A JONSWAP spectrum is sharply
     peaked, so these carry nearly all the energy; and a two-metre ripple does
     not heave a hundred-tonne hull — it breaks against her and averages out
     along her length. The swell the vessel visibly sits on is the swell the
     solver feels, which is the part of the invariant that matters. */
  sample(x, z, t, outNormal){
    let y = 0, nx = 0, nz = 0, ny = 0;
    const src = this.cpuWaves || this.waves;
    for(let i=0;i<src.length;i++){
      const w = src[i];
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

  /* Hand the sea the foam field. Its window slides with the vessel, so the
     origin and extent have to travel with it — the lookup is in world space. */
  attachFoam(foam){
    this.foam = foam;
    this.uniforms.uFoamSize.value = foam.size;
    this.uniforms.uFoamOn.value = 1.0;
  }

  syncFoam(){
    if(!this.foam) return;
    this.uniforms.uFoamTex.value = this.foam.texture;
    this.uniforms.uFoamOrigin.value.copy(this.foam.origin);
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
    /* Haze is deliberately NOT tied to the sea state. Physically a blow does
       thicken the air, but it shut the horizon down exactly when the big seas
       became worth looking at. A steady, clear atmosphere serves the view
       better; adjust these two numbers to taste. */
    this.uniforms.uHaze.value  = 0.00085;
    this.uniforms.uHazeH.value = 150.0;
  }
};
