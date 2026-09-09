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

/* Where the hull actually meets the water, shared by the sea and the foam pass
   so the two can never draw a different ship.

   This used to be an ELLIPSE of spec.L × spec.B, and no hull is an ellipse. On
   the Roter Löwe the collar ran a metre and a half inside her planking over
   half her length, and nearly four metres adrift at her transom — where the
   ellipse has tapered to a point and she is still 3.8 m across. The
   half-breadths now come from the vessel herself: off her own mesh if she is a
   model, off hull-lines.js if she is procedural, sampled into a one-row texture.

   What comes back is a signed distance in METRES to that outline, by the usual
   box formula. Metres, not normalised units — a band of constant normalised
   width is thin abeam and metres thick off the stem, which is exactly what used
   to bloat the collar at her ends. Negative inside, positive outside, so one
   number serves both the collar and the gates that used to test `ed`. */
Naval.HULL_GLSL = `
  /* One ROW of this texture per vessel: her half breadths, stern to stem, as
     fractions of her own greatest half breadth. A row rather than a texture
     each, because GLSL ES 1.0 will not index an array of samplers. */
  uniform sampler2D uHullProf;
  uniform int uShipCount;
  uniform vec3 uShipPos[NSHIP];
  uniform vec2 uShipFwd[NSHIP];
  uniform vec2 uShipHalf[NSHIP];    // (half length overall, greatest half breadth), metres
  uniform vec2 uHullEnds[NSHIP];    // where her waterline body starts and ends, metres
  uniform float uShipSpeed[NSHIP];
  uniform float uShipAfloat[NSHIP];

  /* Signed distance in METRES to one vessel's real waterline.

     Her uniforms are read at the CALL SITE and passed in, rather than passing
     an index: GLSL ES 1.0 only lets a uniform array be indexed by a constant
     expression, and a function parameter is not one — a loop counter is. */
  float hullGap(vec2 rel, vec2 f2, vec2 r2, vec2 halfLB, vec2 ends, float row,
                out float tAlong){
    float along   = dot(rel, f2);
    float athwart = dot(rel, r2);
    float halfL   = max(halfLB.x, 0.01);
    tAlong = along / halfL;                        // -1 astern .. +1 at the stem
    float halfB = texture2D(uHullProf, vec2(clamp(tAlong*0.5 + 0.5, 0.0, 1.0), row)).r
                  * halfLB.y;
    /* Bound the body on its OWN ends, not on her length overall. Her waterline
       is shorter than she is — the stem rakes out above it, the counter
       overhangs it — so measuring the ends at half her length left a hairline
       of foam running on past the stem where the profile had already tapered to
       nothing. Taken about the middle of the body, so a fine bow and a full run
       are handled without assuming she is symmetrical. */
    float mid  = (ends.x + ends.y)*0.5;
    float halfBody = max((ends.y - ends.x)*0.5, 0.01);  // NOT 'half': reserved in GLSL
    vec2 d = vec2(abs(athwart) - halfB, abs(along - mid) - halfBody);
    return length(max(d, 0.0)) + min(max(d.x, d.y), 0.0);
  }`;

/* The fleet's half breadths: one row per vessel, one column per station, given
   as fractions of that vessel's own greatest half breadth.

   RGBA rather than a single-channel format because RedFormat needs WebGL2 and
   this has to work wherever the rest of the page does; four vessels at
   sixty-four stations cost a kilobyte. Linear filtering interpolates between
   stations for free, and sampling at a row's exact texel centre means no
   bleeding between vessels. */
Naval.HullProfiles = class HullProfiles {
  constructor(rows, cols){
    this.rows = rows; this.cols = cols || 64;
    this.data = new Uint8Array(this.rows*this.cols*4);
    this.data.fill(255);                   // a plain rectangle until told otherwise
    this.texture = new THREE.DataTexture(this.data, this.cols, this.rows, THREE.RGBAFormat);
    this.texture.minFilter = this.texture.magFilter = THREE.LinearFilter;
    this.texture.wrapS = this.texture.wrapT = THREE.ClampToEdgeWrapping;
    this.texture.generateMipmaps = false;
    this.texture.needsUpdate = true;
  }

  set(index, fractions){
    if(index < 0 || index >= this.rows) return;
    const n = Math.min(fractions.length, this.cols), base = index*this.cols*4;
    for(let i=0;i<this.cols;i++){
      // a shorter profile is stretched across the row rather than left blank
      const f = fractions[Math.min(n-1, Math.floor(i*n/this.cols))];
      const v = Math.max(0, Math.min(255, Math.round(f*255)));
      const o = base + i*4;
      this.data[o] = this.data[o+1] = this.data[o+2] = v;
      this.data[o+3] = 255;
    }
    this.texture.needsUpdate = true;
  }

  // the V coordinate of a vessel's row, at its exact texel centre
  row(index){ return (index + 0.5)/this.rows; }
};

Naval.Ocean = class Ocean {
  constructor(scene, sunDir, stage){
    const C = Naval.Config;
    this.C = C;
    this.waves = [];
    this.swell = 1.35;               // creux multiplier, driven by the console
    this.sharp = 1.0;                // crest peaking exponent
    this.windSpeed = 0;                    // m/s, true wind
    this.windVec = new THREE.Vector3();    // true wind velocity (blows toward)
    this.profiles = new Naval.HullProfiles(C.MAX_SHIPS, 64);
    // where local (0,0,0) actually lies in the world — see syncPhase()
    this.origin = new THREE.Vector3();
    /* What each spectral band looked like the last time she was built, and the
       phase correction that keeps her continuous across a rebuild. Kept per
       BAND rather than per entry of `waves`, which is sorted by energy and so
       reorders itself whenever the wind changes. See setSeaState. */
    this.band = [];
    for(let i=0;i<C.NWAVES;i++) this.band.push({ k:0, dx:0, dz:0, omega:0, corr:0 });
    /* Pools. setSeaState used to build a fresh array of eighteen objects on
       every call, which was nothing at all when a hand on a slider called it
       twice a minute. Automatic weather calls it on every frame, and eleven
       hundred short-lived objects a second is a collection pause — which is
       precisely what a stutter is made of. Nothing is allocated in a rebuild
       now; the fields are written in place. */
    this._raw = [];
    this._pool = [];
    for(let i=0;i<C.NWAVES;i++){
      this._raw.push({ amp:0, w:0, dir:0 });
      this._pool.push({ dx:0, dz:0, amp:0, k:1, omega:0, Q:0, L:1, band:i, phase:0 });
    }
    this.cpuWaves = [];

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
        // phase the floating origin owes each wave, reduced mod 2π
        uWavePhase:{value:new Array(C.NWAVES).fill(0)},
        uSun:{value:sunDir.clone()},
        uSunCol:{value:new THREE.Color(0xfff2dc)},  // the sail's transmitted light follows it
        uCam:{value:new THREE.Vector3()},
        uHalf:{value:half},
        uSeg:{value:C.OCEAN_SEG},
        uSharp:{value:1.0},
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
        /* The fleet, so the sea can foam where each hull cuts it. Arrays, not
           single values: several vessels may be afloat at once. uShipCount says
           how many of the slots are live. */
        uShipCount:{value:0},
        uShipPos:{value:Array.from({length:C.MAX_SHIPS},()=>new THREE.Vector3())},
        uShipFwd:{value:Array.from({length:C.MAX_SHIPS},()=>new THREE.Vector2(0,1))},
        uShipHalf:{value:Array.from({length:C.MAX_SHIPS},()=>new THREE.Vector2(12,3))},
        uHullEnds:{value:Array.from({length:C.MAX_SHIPS},()=>new THREE.Vector2(-12,12))},
        uShipSpeed:{value:new Array(C.MAX_SHIPS).fill(0)},
        // 1 while she rides the surface, 0 once she has gone under: an epave
        // makes no collar, and a foam ring left over a wreck reads as a bug
        uShipAfloat:{value:new Array(C.MAX_SHIPS).fill(1)},
        uHullProf:{value:this.profiles.texture},
        /* Seeing her through the water. Absorption per metre, per channel: red
           dies in about 3 m, blue carries past 10 — that is what turns her
           green then blue then dark, instead of merely grey. */
        uShipTex:{value:null}, uShipDepth:{value:null},
        uShipRes:{value:new THREE.Vector2(1,1)}, uShipOn:{value:0.0},
        uAbsorb:{value:new THREE.Vector3(0.34, 0.13, 0.085)},
        uRefract:{value:1.0},                   // 1 = Snell for sea water; a dial, not a fudge
        uProj:{value:new THREE.Matrix4()},
        uNear:{value:0.7}, uFar:{value:14000},
        // 0 above the surface, 1 beneath it, smoothed across the crossing
        uSubmerged:{value:0.0},
        // the very objects the dome uses, so sea and sky share one weather
        uStorm:(stage && stage.skyUniforms) ? stage.skyUniforms.uStorm : {value:0},
        uStormDir:(stage && stage.skyUniforms) ? stage.skyUniforms.uStormDir : {value:new THREE.Vector2(0,1)},
        uStormLoom:(stage && stage.skyUniforms) ? stage.skyUniforms.uStormLoom : {value:0},
        uCloud:(stage && stage.skyUniforms) ? stage.skyUniforms.uCloud : {value:0.42},
        uSkyTime:(stage && stage.skyUniforms) ? stage.skyUniforms.uSkyTime : {value:0},
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
      // the surface has an UNDERSIDE now: from below she is a ceiling, not a hole
      side:THREE.DoubleSide,
      // the sea carries its own layered haze; Three's flat fog would double it
      fog:false,
      defines:{NW:C.NWAVES, NSHIP:C.MAX_SHIPS},
      vertexShader:`
        uniform float uTime, uHalf, uSeg, uSharp; uniform vec4 uWaveA[NW]; uniform vec2 uWaveB[NW];
        uniform float uWavePhase[NW];
        uniform mat4 uReflMat;
        varying vec3 vN; varying vec3 vW; varying float vFoam; varying float vRel;
        varying vec4 vRefl; varying float vSpacing; varying float vViewZ;
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

            float f = k*dot(d, w0.xz) - omega*uTime + uWavePhase[i];
            float c = cos(f), s = sin(f);

            /* Peak the profile: sign(s)·|s|^p. Odd, so the mean stays zero and
               the waterline does not creep. The slope gains a factor p·|s|^(p-1)
               — the crests are not only taller but steeper-sided, which is what
               reads as a rough sea rather than a swell of round humps. */
            float as = abs(s);
            float sp = pow(max(as, 1e-4), uSharp - 1.0);
            float hs = sign(s) * as * sp;          // the sharpened wave
            float dsl = uSharp * sp;               // d/df of it, over cos(f)

            p.x += Q*amp*d.x*c;
            p.z += Q*amp*d.y*c;
            p.y += amp*hs;
            height += amp*hs;
            float WA = k*amp*dsl;
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
          vec4 mvPosition = viewMatrix*vec4(p,1.0);
          // perpendicular distance, in the same measure as the ship's depth buffer
          vViewZ = -mvPosition.z;
          gl_Position = projectionMatrix*mvPosition;
        }`,
      fragmentShader:`
        precision highp float;
        uniform vec3 uSun,uCam,uDeep,uShallow,uSSS,uZenith,uHorizon;
        uniform float uTime, uAmpMax, uRipple;
        uniform vec2 uWind;
        uniform sampler2D uReflTex; uniform float uReflOn;
        uniform sampler2D uShipTex, uShipDepth;
        uniform vec2 uShipRes; uniform float uShipOn, uNear, uFar, uRefract;
        uniform mat4 uProj;
        uniform vec3 uAbsorb;
        uniform float uSubmerged;
        uniform sampler2D uFoamTex; uniform float uFoamOn, uFoamSize, uFlash;
        uniform vec2 uFoamOrigin;
        varying vec3 vN; varying vec3 vW; varying float vFoam; varying float vRel;
        varying vec4 vRefl; varying float vSpacing; varying float vViewZ;
        ${Naval.SKY_GLSL}
        ${Naval.HAZE_GLSL}
        ${Naval.HULL_GLSL}

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
          /* No cloud in the mirror. The reflection is a microfacet lobe, so
             anything with the fine structure of cirrus comes down as a grey
             smear that reads as dirt on the water rather than as sky. */
          vec3 sky = navalSky(R, uSun, uZenith, uHorizon, 0.0);

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
          /* A gale takes the colour out of the water as surely as out of the
             sky. Most of a sea colour is skylight scattered back up out of it,
             so a lid overhead has to reach the body of the water too — without
             this the sea stayed turquoise under a sky of slate, which reads as
             two pictures pasted together. */
          body *= 1.0 - 0.50*uStorm;

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

          /* Foam where the hull cuts the water: a bright collar right at the
             plating, then a broader band that only shows once she has way on.
             hullGap gives the distance to her REAL waterline, in metres —
             negative inside her, positive outside. */
          float hullFoam = 0.0;
          for(int i=0;i<NSHIP;i++){
          if(i < uShipCount){
          vec2 rel = vW.xz - uShipPos[i].xz;
          vec2 f2 = normalize(uShipFwd[i]);
          vec2 r2 = vec2(f2.y, -f2.x);
          float tAlong;
          float gapM = hullGap(rel, f2, r2, uShipHalf[i], uHullEnds[i],
                               (float(i) + 0.5)/float(NSHIP), tAlong);
          float way = clamp(uShipSpeed[i]/3.0, 0.0, 1.0);

          /* Only the crisp collar and bow wave stay instantaneous — they belong
             to the hull and must move with her. The lingering trail astern now
             comes from the foam field, which leaves it in the water. */
          /* The collar thins away INWARD under her plating instead of stopping
             at a line. Foam banks against a hull and runs out beneath it; a
             hard inner edge reads as a decal laid on the water. */
          float collar = (1.0 - smoothstep(0.0, 0.85, gapM))
                         * smoothstep(-1.3, -0.1, gapM);

          /* The bow wave: not a band across her forward half, but a moustache
             at the stem and two wings sweeping aft. The crest stands off as the
             SQUARE ROOT of the distance abaft the stem, which is what gives a
             bow wave its parabolic wing rather than a straight edge, and it
             needs way — a vessel lying stopped throws none at all.

             The "fast" ramp saturates at twice the speed "way" does, so the
             gerbe keeps building after the collar has stopped growing: that is
             what makes her look driven rather than merely afloat. */
          float fast   = clamp(uShipSpeed[i]/6.0, 0.0, 1.0);
          float along  = tAlong * uShipHalf[i].x;
          float sAft   = max(uHullEnds[i].y - along, 0.0);   // metres abaft the stem
          float spread = 1.7*sqrt(sAft)*fast;
          float wing   = exp(-pow((gapM - spread)/(0.75 + 0.9*fast), 2.0));
          float fade   = exp(-sAft/(5.0 + 30.0*fast));       // dies away astern
          float bow    = wing * fade * way * step(-0.1, gapM);

          /* Vessels do not add foam past saturation, they share it: two hulls
             lying alongside make one white patch, not a doubly white one. */
          hullFoam = max(hullFoam,
            clamp(collar*0.70 + bow*(0.55 + 0.75*fast), 0.0, 1.0) * uShipAfloat[i]);
          }}
          hullFoam *= (0.45 + 0.80*fn);

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

          /* What is under the surface, seen THROUGH it.

             The hull was drawn once on her own into uShipTex with her depth
             alongside; the difference between that depth and this fragment's is
             the thickness of water in the way. Extinction is per channel — red
             gone in about three metres, blue carrying three times further —
             which is what makes her go green, then blue, then nothing, instead
             of merely fading to grey the way haze would.

             Only where she is BEHIND the surface: a hull in front of the water
             was already drawn by the ordinary render and must not be doubled. */
          /* SEEN FROM BENEATH: Snell's window.

             Looking up from under water, the whole hemisphere above is squeezed
             into a cone about 97° across — everything, horizon to horizon, in
             that one bright disc. Outside it nothing gets in at all: the angle
             is past total internal reflection and the surface turns into a
             mirror. That circle, and the hard silvered edge around it, is what
             makes a shot read as UNDER the water rather than merely blue.

             The refract() built-in does the work and returns the zero vector on
             total internal reflection, which is exactly the test for being outside
             the window. The normal is negated because from below the surface
             faces the other way. This must run BEFORE the ship is blended in:
             written after it, it overwrote her, and no vessel could be seen
             from under water at all. */
          float below = (uSubmerged > 0.5 && !gl_FrontFacing) ? 1.0 : 0.0;
          if(below > 0.5){
            vec3 Vv = normalize(vW - uCam);
            vec3 refr = refract(Vv, -N, 1.333);
            if(dot(refr, refr) < 0.0001){
              col = mix(uShallow*0.42, uDeep*1.15, 0.6);      // the mirror
            }else{
              col = navalSky(refr, uSun, uZenith, uHorizon);
              // the disc, which only the dome draws and the dome is not here
              col += vec3(1.0,0.95,0.85)*pow(max(dot(refr,uSun),0.0), 1400.0)*4.0;
            }
            // the rim of the window, where the light piles up before it is shut out
            col += vec3(0.80,0.92,1.00)*pow(1.0 - abs(dot(Vv, N)), 6.0)*0.22;
          }

          if(uShipOn > 0.5){
            vec2 base = gl_FragCoord.xy / uShipRes;

            /* A first, straight look, only to learn how deep she lies. */
            float d0 = texture2D(uShipDepth, base).x*2.0 - 1.0;
            float sz0 = (2.0*uNear*uFar)/(uFar + uNear - d0*(uFar - uNear));
            float thick0 = max(sz0 - vViewZ, 0.0);

            /* REFRACTION — the surface ripple, carried onto what lies under it.

               Snell bends the ray by about (1 - 1/n) of the surface tilt, a
               quarter of it for sea water, and the displacement at the hull is
               that angle times the depth. Rather than guess how that lands on
               screen, the offset is built in WORLD metres and reprojected: the
               shift is the difference between where she is and where the bent
               ray says she is. Aspect, field of view and camera attitude then
               take care of themselves.

               This was first written as a screen-space fudge with a coefficient
               of 26, which was about a hundred times too large: the offset ran
               past half the screen, nearly every sample missed her, and the
               fallback below rebuilt her out of unrelated pixels. It read as
               noise destroying the very thing one was trying to watch, rather
               than as water.

               The depth term SATURATES at five metres. Strictly, a longer ray
               keeps wandering further — but past a few metres the displacement
               is wide enough to smear her outline away, and she is meant to
               stay legible while she fades. */
            float bend = min(thick0, 5.0) * 0.25 * uRefract;
            vec4 c0 = uProj * viewMatrix * vec4(vW, 1.0);
            vec4 c1 = uProj * viewMatrix * vec4(vW + vec3(N.x, 0.0, N.z)*bend, 1.0);
            vec2 shift = (c1.xy/c1.w - c0.xy/c0.w) * 0.5;
            vec2 suv = clamp(base + shift, vec2(0.0), vec2(1.0));

            float d = texture2D(uShipDepth, suv).x*2.0 - 1.0;
            float sz = (2.0*uNear*uFar)/(uFar + uNear - d*(uFar - uNear));
            float thick = sz - vViewZ;

            /* If the bent ray landed on something IN FRONT of the water, it has
               reached her topsides, and dragging those down into the water would
               smear her rail across the sea. Fall back to the straight look. */
            if(thick <= 0.0){
              suv = base; thick = thick0;
            }

            vec4 sc = texture2D(uShipTex, suv);
            if(sc.a > 0.01 && thick > 0.0){
              /* What lies between surface and hull is WATER when looking down
                 at her and AIR when looking up at her from beneath — a boat
                 seen through Snell's window is not behind metres of sea, she is
                 simply above it. Attenuating her as though she were drowned her
                 in blue at ten metres of freeboard. */
              vec3 ab = mix(uAbsorb, uAbsorb*0.02, below);
              vec3 T = exp(-ab*thick) * sc.a;
              col = mix(col, sc.rgb, clamp(T, 0.0, 1.0));
            }
          }

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
    /* No cloud in the mirror — and it takes BOTH halves to mean it. The
       analytic sky is asked for none where the lobe is computed, but the
       planar pass renders the actual scene, dome and all, so the dome would
       put the cirrus straight back into the water through the texture. Cheap
       to say here, and impossible to forget: the two live in one place. */
    const prevCloud = this.uniforms.uCloud.value;
    this.uniforms.uCloud.value = 0;

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
    this.uniforms.uCloud.value = prevCloud;
    this.uniforms.uReflOn.value = 1.0;
  }

  /* seaState is the Beaufort number; windDeg is the bearing the wind blows
     FROM, as a seaman states it, on the same compass as the heading. */
  setSeaState(seaState, windDeg){
    const C = this.C, G = C.G;
    this.waves.length = 0;
    const wr = windDeg*Math.PI/180;
    const s = Math.max(0, seaState);
    // remembered so a caller that must flatten her can put her back exactly
    this.seaState = seaState; this.windDeg = windDeg;

    /* The wind that goes with this sea. A caller wanting a gust the sea has
       not caught up with says so afterwards — see setWind. */
    this.setWind(seaState, windDeg);

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
    const raw = this._raw;
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
      raw[i].amp = amp; raw[i].w = w; raw[i].dir = wr + spread*u;
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

    /* Steepness budget. It must rise with the swell control as well as with the
       sea state: a bigger wave at the same steepness is just a bigger round
       hump, which is exactly how deep troughs used to look. */
    const chop = (0.35 + s*0.075) * (0.75 + 0.42*this.swell);

    /* Crest sharpening. Gerstner alone cannot carry this: its cusping comes from
       the horizontal term Q, and with eighteen components the budget divides so
       far that every one of them is left almost sinusoidal. So the profile is
       peaked directly — sign(s)·|s|^p. Odd symmetry means the mean stays exactly
       zero, which matters: any DC offset here would silently shift the mean
       water level and the whole fleet's flotation with it. */
    this.sharp = 1 + Math.min(0.95, (0.06 + 0.055*s) * (0.55 + 0.62*this.swell));
    this.uniforms.uSharp.value = this.sharp;

    /* --- keeping the sea continuous while the wind changes ---

       The console used to be the only thing that ever called this, and a
       console is used a few times a minute. Automatic weather calls it several
       times a SECOND, and that turns a detail into the whole problem.

       A component's phase is k·(d·r) − ω·t + correction. Rebuild the spectrum
       with a slightly different ω and the term ω·t jumps by Δω·t — and t is
       the running clock, thousands of seconds in. A thousandth of a radian per
       second of frequency drift is a whole radian of jump. The same is true of
       the direction: k·(d·origin) is evaluated against an origin that may be
       hundreds of kilometres out, so a hundredth of a degree of veer moves the
       phase at the ship by a good fraction of a wavelength.

       Neither is a rounding error to be lived with — together they would make
       the sea reshuffle itself on every gust, which reads as boiling.

       Both are absorbed exactly, and for the same reason the floating origin
       works: what changes is a phase, and a phase only matters modulo 2π. The
       correction is set so the total is unchanged AT THE LOCAL ORIGIN, which
       is where the fleet is and therefore where continuity is worth having;
       far from it the two spectra part company slowly, as they must, since
       they are genuinely different seas. */
    const o = this.origin, TAU = Math.PI*2, tNow = this.uniforms.uTime.value;

    let ampSum = 0;
    let band = 0;
    for(const r of raw){
      // sharpening lowers the RMS of the profile; give it back so the stated
      // significant height still holds
      const amp = r.amp*scale*(1 + 0.42*(this.sharp - 1));
      const k = r.w*r.w/G;                // deep-water dispersion, k = ω²/g
      // steepness normalised so the crest never loops (Σ Q·k·A < 1)
      const Q = Math.min(0.85, chop / (k*amp*N + 1e-4));
      // seas run with the wind, i.e. away from the bearing it blows from
      const dx = -Math.sin(r.dir), dz = -Math.cos(r.dir);
      const W = this._pool[band], B = this.band[band];
      if(B.k > 0) B.corr = (B.corr
                    + (B.k*(B.dx*o.x + B.dz*o.z) - k*(dx*o.x + dz*o.z))
                    + (r.w - B.omega)*tNow) % TAU;
      B.k = k; B.dx = dx; B.dz = dz; B.omega = r.w;

      W.dx = dx; W.dz = dz; W.amp = amp; W.k = k;
      W.omega = r.w; W.Q = Q; W.L = 2*Math.PI/k;
      this.waves.push(W);
      ampSum += amp;
      band++;
    }
    /* Order by ENERGY, not by length, and let the solver and the foam pass take
       the head of that list. Taking the longest instead was wrong: in a gale the
       longest components run to hundreds of metres, and a swell far longer than
       the ship merely lifts her bodily — it is the band around the spectral peak
       that actually works her. Sorting by amplitude puts that band first. */
    this.waves.sort((a,b2) => b2.amp - a.amp);
    this.cpuWaves.length = 0;
    for(let i=0;i<C.NWAVES_CPU && i<this.waves.length;i++) this.cpuWaves.push(this.waves[i]);

    // reference height for the crest glow, so it scales with the sea state
    this.uniforms.uAmpMax.value = Math.max(0.05, ampSum*0.8);
    this.syncUniforms();
  }

  /* The wind alone, leaving the spectrum where it stands.

     For a hand on the console the two are the same thing and setSeaState sets
     both. Under weather that runs itself they are NOT: a squall is felt in the
     sails the instant it arrives, while the sea it raises takes minutes to get
     up and minutes more to lie down again. Splitting them is not a licence —
     it is the honest account, and it is also what makes automatic weather
     affordable, since only this half is cheap enough to run every frame. */
  setWind(force, windDeg){
    const wr = windDeg*Math.PI/180;
    const s = Math.max(0, force);
    // true wind from the Beaufort number: v ≈ 0.836·B^1.5 m/s
    this.windSpeed = 0.836*Math.pow(s,1.5) + 0.8;
    this.windVec.set(-Math.sin(wr)*this.windSpeed, 0, -Math.cos(wr)*this.windSpeed);
    this.syncWind();
  }

  syncUniforms(){
    const C = this.C;
    for(let i=0;i<C.NWAVES;i++){
      const w = this.waves[i] || {dx:0,dz:0,amp:0,k:1,omega:0,Q:0};
      this.uniforms.uWaveA.value[i].set(w.dx, w.dz, w.amp, w.k);
      this.uniforms.uWaveB.value[i].set(w.omega, w.Q);
    }
    this.syncPhase();
  }

  /* THE FLOATING ORIGIN, and the one thing that makes it possible.

     Everything is computed near zero and `origin` records where that zero
     actually lies in the world, so a vessel a thousand kilometres out is still
     drawn at coordinates of a few hundred metres. Without it the sea dies well
     before that: the Gerstner phase is k·x, and with k up to 3 rad/m a position
     of a few kilometres already costs most of a 32-bit float's seven digits.
     The waves do not merely jitter, they lose all meaning.

     Shifting the origin would slide the whole sea sideways — unless the phase
     that shift represents is added back. That offset is k·(d·origin), which
     grows without bound and would bring the precision problem straight back,
     EXCEPT that a phase only matters modulo 2π. Reduced here, in JavaScript's
     doubles, it stays a small number the shader can hold exactly. That is the
     whole trick: the sea is perfectly continuous across a rebase, and stays so
     however far she sails. */
  syncPhase(){
    const C = this.C, TAU = Math.PI*2, o = this.origin;
    for(let i=0;i<C.NWAVES;i++){
      const w = this.waves[i];
      let p = 0;
      if(w){
        const B = this.band[w.band];
        p = (w.k*(w.dx*o.x + w.dz*o.z) + (B ? B.corr : 0)) % TAU;
        w.phase = p;                       // the CPU sampler reads it off the wave
      }
      this.uniforms.uWavePhase.value[i] = p;
    }
  }

  /* Move the world under the fleet. Everything else that holds a position —
     the hulls, the foam field, the cameras — must be shifted by the same delta
     in the same frame, or they will disagree with the sea by exactly it. */
  rebase(dx, dz){
    this.origin.x += dx;
    this.origin.z += dz;
    this.syncPhase();
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
      const f = w.k*(w.dx*x + w.dz*z) - w.omega*t + (w.phase || 0);
      const c = Math.cos(f), sn = Math.sin(f);
      // the very same sharpened profile the vertex shader draws
      const as = Math.abs(sn);
      const sp = Math.pow(Math.max(as, 1e-4), this.sharp - 1);
      y += w.amp * Math.sign(sn) * as * sp;
      const WA = w.k * w.amp * this.sharp * sp;
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
    /* Refraction reprojects a world offset, so it needs the CURRENT projection
       — the bridge and fixed cameras zoom the lens rather than move, so this
       changes under us and a matrix copied once would be wrong after a scroll. */
    if(this._cam) this.uniforms.uProj.value.copy(this._cam.projectionMatrix);
    this.uniforms.uSkyTime.value = t;        // the cloud deck drifts with the day
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

  /* Hand the sea this vessel's outline. Until one is given she foams around a
     plain rectangle, which is wrong but never absent. */
  /* Give the sea one vessel's outline, into her own row. */
  setHullProfile(index, prof){
    if(index >= this.C.MAX_SHIPS) return;
    this.profiles.set(index, prof.fractions);
    this.uniforms.uHullEnds.value[index].set(prof.ends.aft, prof.ends.fwd);
    (this._profs = this._profs || [])[index] = prof;
  }

  // Tell the sea where the hull is, so it can foam along her waterline.
  /* Let the sea look through itself at her. Called once; the buffer keeps the
     same texture objects across resizes, so the uniforms stay valid. */
  useShipBuffer(buf, camera){
    const u = this.uniforms;
    u.uShipTex.value = buf.texture;
    u.uShipDepth.value = buf.depth;
    u.uShipOn.value = 1.0;
    if(camera){ u.uNear.value = camera.near; u.uFar.value = camera.far; this._cam = camera; }
    this._shipBuf = buf;
  }

  /* Tell the sea about every hull afloat. One entry per vessel:
     { body, spec, afloat }. Slots past uShipCount are simply not read. */
  trackShips(fleet){
    const u = this.uniforms, profs = this._profs || [];
    const n = Math.min(fleet.length, this.C.MAX_SHIPS);
    u.uShipCount.value = n;
    this._sf = this._sf || new THREE.Vector3();
    for(let i=0;i<n;i++){
      const e = fleet[i], b = e.body;
      u.uShipPos.value[i].copy(b.pos);
      this._sf.set(0,0,1).applyQuaternion(b.quat);
      u.uShipFwd.value[i].set(this._sf.x, this._sf.z).normalize();
      // her greatest half breadth AS MEASURED, which her profile is scaled by;
      // spec.B is only the stated figure and a model rarely matches it exactly
      u.uShipHalf.value[i].set(e.spec.L*0.5,
        profs[i] ? profs[i].maxHalfB : e.spec.B*0.5);
      u.uShipSpeed.value[i] = Math.hypot(b.vel.x, b.vel.z);
      u.uShipAfloat.value[i] = e.afloat == null ? 1 : e.afloat;
    }
  }

  // Ripples travel with the wind, and grow with it.
  syncWind(){
    const w = this.windVec, s = Math.hypot(w.x, w.z);
    if(s > 1e-4) this.uniforms.uWind.value.set(w.x/s, w.z/s);
    this.uniforms.uRipple.value = Math.min(2.6, 0.40 + this.windSpeed*0.105);
    /* Haze IS tied to the sea state, and used not to be.

       The first rule here was that a blow must not shut the horizon down,
       because it did so exactly when the big seas became worth looking at. That
       was right while a gale was the only weather there was: closing the view
       took away the whole reward for raising one. It stops being right once a
       gale brings a sky of its own — a lid overhead, rain across the water —
       since then the murk is not hiding the spectacle, it IS the spectacle, and
       a storm one can see eight kilometres through is no storm at all.

       So it comes on LATE and steeply: nothing at all below force 5, where the
       sea is lively and the day is fine, and then quickly down to about a mile
       of visibility. Fine weather is untouched. */
    const st = Math.max(0, Math.min(1, (this.seaState - 5.0)/3.2));
    this.uniforms.uHaze.value  = 0.00085 * (1 + 6.4*st*st);
    // and it fills the height as well, or the sky stays clear above the murk
    this.uniforms.uHazeH.value = 150.0 * (1 + 2.4*st);
  }
};
