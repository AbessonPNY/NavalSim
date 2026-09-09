/* Renderer, scene, camera, light and sky — the room the vessel sails in. */
window.Naval = window.Naval || {};

/* The sky, as one GLSL function shared by the dome and by the sea's reflection.
   They MUST come from the same code: if the water mirrored a different sky from
   the one overhead, the horizon would show a seam and the whole image would
   read as fake. The dome adds the sun's disc on top; the sea gets its highlight
   from a microfacet term instead, which is what spreads it into a glitter path. */
Naval.SKY_GLSL = `
  /* Declared here, under a guard, because THREE shaders include this file — the
     dome, the sea's reflection and every hazed material — and a uniform declared
     twice in one program fails the whole compile. Same reasoning as
     SUN_UNIFORMS_GLSL. */
  #ifndef NAVAL_SKY_UNIFORMS
  #define NAVAL_SKY_UNIFORMS
  uniform float uCloud, uSkyTime, uStorm;
  uniform vec2 uStormDir;      // world bearing toward the nearest squall
  uniform float uStormLoom;    // how black that quarter of the sky is
  uniform vec2 uStormFlashDir; // where in the bank this particular stroke is
  uniform float uStormFlash;   // and how bright, just now
  #endif

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

  float navalNoise2(vec2 p){
    vec2 i = floor(p), f = fract(p);
    f = f*f*(3.0 - 2.0*f);
    float a = navalHash13(vec3(i, 7.0));
    float b = navalHash13(vec3(i + vec2(1.0, 0.0), 7.0));
    float c = navalHash13(vec3(i + vec2(0.0, 1.0), 7.0));
    float d = navalHash13(vec3(i + vec2(1.0, 1.0), 7.0));
    return mix(mix(a, b, f.x), mix(c, d, f.x), f.y);
  }

  /* Cloud, on a PLANE and not on the dome.

     Projecting the view direction through dir.xz/dir.y puts the noise on a flat
     deck overhead, which is what clouds actually sit on: they crowd together
     toward the horizon and open out at the zenith, all on their own. Wrapping a
     texture round the sphere instead gives an even scatter that reads as
     wallpaper, and no amount of detail rescues it.

     Three octaves and a coverage threshold — below it there is simply blue sky,
     which is what makes a sky read as weather rather than as fog. */
  float navalClouds(vec3 dir, float amt){
    float up = dir.y;
    if(up < 0.02 || amt <= 0.0) return 0.0;
    /* The scale matters more than anything else here. At 0.055 the whole
       visible sky mapped into a few hundredths of a noise unit — the pattern
       was all but constant across it, and what came out was a flat pale veil
       rather than cloud. The noise has features about one unit across, so the
       sky must span a dozen of them. */
    /* Stretched hard along one axis. Isotropic noise gives an even mackerel
       sky — cloud everywhere, all the same size, which is a real sky but a
       heavy one. Fair weather is a few CIRRUS: long thin streaks combed out by
       the wind aloft, with wide blue between them. That length is the whole
       character of them, and it comes from nothing more than reading the noise
       four times finer across the streak than along it. */
    vec2 q = vec2(dir.x, dir.z)/up;
    vec2 p = vec2(q.x*4.2, q.y*17.0) + vec2(uSkyTime*0.012, uSkyTime*0.030);
    float f  = navalNoise2(p)*0.55;
    f += navalNoise2(p*2.13 + 3.7)*0.28;
    f += navalNoise2(p*4.37 + 9.1)*0.17;
    /* Stretch it. Three octaves averaged sit tightly around a half, so a
       threshold across that range switches the WHOLE sky partly on and gives a
       flat pale wash — which is exactly what the first attempt produced. Pulled
       apart, the low ground falls clear of the threshold and there is real blue
       between the clouds, which is what makes a sky read as weather. */
    f = clamp((f - 0.5)*2.2 + 0.5, 0.0, 1.0);
    /* High and to the right of the histogram: only the crests of the noise come
       through, and they come through as wisps. The slider still runs the whole
       way to overcast, but a fine day is the default. */
    float c = smoothstep(0.80 - amt*0.52, 0.99 - amt*0.36, f);
    return c * smoothstep(0.02, 0.11, up);
  }

  /* The cloud amount is a PARAMETER and not simply uCloud, for one caller:
     the sea's mirror asks for none. Cirrus reflected in water are a smear of
     grey that reads as dirt on the surface rather than as sky — the reflection
     is a micro-facet lobe, so anything with fine structure is averaged into
     mush on the way down. The rule that the sky exists only once still holds:
     it is the same function, the same code, the same weather, asked for a
     different coverage. */
  vec3 navalSky(vec3 dir, vec3 sunDir, vec3 zenith, vec3 horizon, float cloudAmt){
    float h = clamp(dir.y, 0.0, 1.0);
    vec3 c = mix(horizon, zenith, pow(h, 0.55));

    /* Cloud goes on BEFORE the sun's halo and before the horizon haze: a cloud
       is lit by the sun and then seen through the same air as everything else,
       so laying it on afterwards would leave it floating in front of the murk. */
    float cl = navalClouds(normalize(dir), cloudAmt) * (1.0 - uStorm);
    if(cl > 0.001){
      float sd0 = max(dot(normalize(dir), sunDir), 0.0);
      vec3 lit = mix(horizon*1.22, vec3(1.0, 0.98, 0.94), 0.30);
      // grey underside, and a bright rim where the sun is behind them
      lit = mix(lit*0.66, lit*1.18, pow(sd0, 3.0));
      c = mix(c, lit, cl*0.62);   // cirrus are thin: the blue shows through
    }

    /* Stars come out as the sun goes under, and they belong UNDER the haze —
       added before the horizon mix below, so they thin out toward the horizon
       the way real ones do rather than sitting on top of the murk. */
    float night = smoothstep(0.05, -0.12, sunDir.y);
    if(night > 0.001){
      c += vec3(0.88, 0.92, 1.00) * 1.7 * (1.0 - uStorm) * navalStars(normalize(dir))
           * night * smoothstep(-0.02, 0.16, dir.y);
    }

    float sd = max(dot(normalize(dir), sunDir), 0.0);
    // broad halo: air scatters the sun's light across a wide arc
    c += vec3(1.00, 0.82, 0.55) * pow(sd, 8.0) * 0.30;
    c += vec3(1.00, 0.90, 0.72) * pow(sd, 64.0) * 0.35;
    // haze thickens toward the horizon, where you look through more atmosphere
    c = mix(c, horizon * 1.04, smoothstep(0.22, -0.02, dir.y));

    /* And then the gale puts its lid on, over everything — the sun's halo and
       the horizon wash included. It has to come LAST. Applied before them it
       was simply undone: nearly all of the sky one actually looks at lies
       within twenty degrees of the horizon, which is exactly the band the haze
       term was busy washing back to white, so the storm came out as a pale
       grey day rather than as a dark one.

       Not a flat colour either. Overcast is darkest overhead and lifts a little
       toward the horizon, where the light gets in under the edge of the cloud —
       and that gradient is most of what stops a storm sky reading as a wall. */
    if(uStorm > 0.001){
      vec3 lid = horizon * mix(0.40, 0.16, smoothstep(0.0, 0.45, dir.y));
      c = mix(c, lid, uStorm*0.96);
    }

    /* A squall one has not yet reached: a black quarter standing on the
       horizon, in its own direction and nowhere else. It is what makes weather
       something one can SEE coming and steer around, rather than something
       that simply arrives — and it costs a dot product.

       Only near the horizon, and only in that bearing: a low seen from outside
       is a wall with sky above it and clear water either side. It hands over to
       the lid above as she enters, there being nothing to point at once one is
       inside the thing. */
    if(uStormLoom > 0.001){
      vec2 hz = dir.xz;
      float hl = length(hz);
      if(hl > 1e-4){
        float al = max(0.0, dot(hz/hl, uStormDir));
        /* Wide, and TALL. The first cut used a narrow cone dying out by fifteen
           degrees of elevation, which confined the whole thing to a strip of
           sky a few dozen pixels high above the horizon — measurable at nine
           per cent of the frame and quite invisible to the eye. A squall seen
           from six miles off is a WALL: it stands well up into the sky and
           spreads across a good part of the horizon, and it has to be drawn
           that way or it reads as a smudge on the sea line. */
        float sect = pow(al, 4.5) * (1.0 - smoothstep(0.03, 0.34, dir.y));
        c = mix(c, horizon*0.09, sect*uStormLoom);

        /* And it flickers. A squall seen from six miles is not a dead grey
           shape: it lights from the inside, briefly, somewhere along its front
           — and that is most of what tells one it is a storm and not a bank of
           fog. ADDED rather than mixed, because a discharge is light arriving
           and not a colour being chosen.

           On a tight lobe about its OWN bearing, not the bank's: a stroke
           happens somewhere in the cloud, so it lights one part of it, and a
           whole front blinking as one reads as a switch being thrown. */
        if(uStormFlash > 0.001){
          float fa = max(0.0, dot(hz/hl, uStormFlashDir));
          /* Faint, and TIGHT. Additive light in a sky the haze then smears
             across the sea does not stay where it is put: at nearly twice the
             horizon colour it came out as a second sun sitting on the water,
             which is a nuclear test and not a squall. A stroke five miles off
             lights a patch of cloud a little brighter than the cloud beside
             it — no more than that. */
          float lobe = pow(fa, 34.0) * (1.0 - smoothstep(0.01, 0.20, dir.y));
          /* Its OWN colour, and not the horizon reduced. That distinction cost
             a version: foam takes its brightness off the sky because foam
             REFLECTS, and the same reflex applied here made the stroke
             proportional to the ambient — so it faded to nothing at night,
             which is the one hour a distant squall lights up like a lamp
             behind a sheet. Lightning EMITS. What it adds does not depend on
             what else is lit; that is what makes it lightning. */
          c += vec3(0.58, 0.66, 0.85) * (uStormFlash * lobe * 0.55 * uStormLoom);
        }
      }
    }
    return c;
  }
  // What the sky is actually wearing today, for every caller but the mirror.
  vec3 navalSky(vec3 dir, vec3 sunDir, vec3 zenith, vec3 horizon){
    return navalSky(dir, sunDir, zenith, horizon, uCloud);
  }
`;

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
    shader.uniforms.uSubmerged = u.uSubmerged;
    shader.uniforms.uAbsorb = u.uAbsorb;
    shader.uniforms.uDeep = u.uDeep;
    shader.uniforms.uCloud = u.uCloud;
    shader.uniforms.uSkyTime = u.uSkyTime;
    shader.uniforms.uStorm = u.uStorm;
    shader.uniforms.uStormDir = u.uStormDir;
    shader.uniforms.uStormLoom = u.uStormLoom;
    shader.uniforms.uStormFlashDir = u.uStormFlashDir;
    shader.uniforms.uStormFlash = u.uStormFlash;

    shader.vertexShader = 'varying vec3 vHazeW;\n' + shader.vertexShader.replace(
      '#include <project_vertex>',
      '#include <project_vertex>\n  vHazeW = (modelMatrix * vec4(transformed,1.0)).xyz;'
    );

    const head = 'varying vec3 vHazeW;\n' + Naval.SUN_UNIFORMS_GLSL
               + 'uniform vec3 uZenith,uHorizon,uAbsorb,uDeep;\nuniform float uSubmerged;\n'
               + Naval.SKY_GLSL + '\n' + Naval.HAZE_GLSL + '\n';
    /* Above water she fades into the SKY; below it she is drowned in the deep,
       per channel — the same extinction the sea already uses to show a sunken
       hull through the surface, turned on the whole world. It is what makes
       going under read as water rather than as a blue filter: red is gone
       within a few metres and the ship goes green, then blue, then nothing. */
    /* At four tenths of the surface figure. Not a fudge: uAbsorb was measured
       for light looking DOWN through the surface onto a sunken hull, where it
       makes the journey twice — down to her and back up to the eye. Looking
       across, it makes it once, so the same water carries about twice as far.
       At full strength a hull twenty metres off was simply not there. */
    const tail = '\n{ vec3 vd = vHazeW - uCam;\n'
               + '  if(uSubmerged > 0.5){\n'
               + '    vec3 T = exp(-uAbsorb*0.4*length(vd));\n'
               + '    gl_FragColor.rgb = gl_FragColor.rgb*T + uDeep*(1.0 - T);\n'
               + '  }else{\n'
               + '    gl_FragColor.rgb = mix(gl_FragColor.rgb,'
               + ' navalSky(normalize(vd), uSun, uZenith, uHorizon), hazeAlong(uCam, vHazeW));\n'
               + '  } }';
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

    /* Shared by the dome, the sea and every hazed material — one object, so the
       clouds overhead and the clouds the sea mirrors can never drift apart. */
    this.skyUniforms = { uCloud:{value:0.12}, uSkyTime:{value:0}, uStorm:{value:0},
                         uStormDir:{value:new THREE.Vector2(0,1)}, uStormLoom:{value:0},
                         uStormFlashDir:{value:new THREE.Vector2(0,1)}, uStormFlash:{value:0} };
    this.storm = 0;
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
    this.declination = 12;           // degrees: late spring, long days
    this.dayTime = 9.5;                // hours; the cycle carries it on from here
    this.setTimeOfDay(this.dayTime);

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
    this._sunBase = (1.1 + 1.0*t)*(1 - night) + 0.28*night;
    this.sun.intensity = this._sunBase * (1 - 0.62*this.storm);
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
    this._hemiBase = ((0.14 + 0.14*t)*(1-night) + 0.04*night) * (1 - 0.55*this.storm);
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

  /* How hard it is blowing, from nought to a full gale, and what that does to
     the light. It is NOT the sun going down — that was the first reading of it
     and it was wrong. A gale at noon is dark because the sky has closed over,
     not because the sun has set: the light stays where it is in the sky and
     simply stops arriving. So the elevation is untouched and what changes is
     the lid above, the murk between, and how much of the sun gets through. */
  setStorm(x){
    x = Math.max(0, Math.min(1, x));
    if(Math.abs(x - this.storm) < 1e-4) return;
    this.storm = x;
    this.skyUniforms.uStorm.value = x;
    if(this._sunBase != null) this.sun.intensity = this._sunBase*(1 - 0.62*x);
    this.setSun(this.sunElev, this.sunBearing);   // colours and ambient follow
  }

  /* Where the sun stands at a given hour of the day.

     Not a sine wave dressed up as a sun. The real thing is three lines of
     spherical trigonometry, it needs a latitude — which this world already has,
     since the chart takes her position in latitude and longitude — and it gets
     for free everything a hand-drawn arc has to be told: the sun rises in the
     east and sets in the west, transits due south in these latitudes, climbs
     higher in summer, and stays below the horizon for the right share of the
     day. A sine in elevation with a fixed bearing has the sun rising and
     setting in the same place, which is the sort of thing one notices without
     being able to say why. */
  setTimeOfDay(hours){
    const rad = Math.PI/180;
    this.dayTime = ((hours % 24) + 24) % 24;
    const phi = (Naval.Geo ? Naval.Geo.LAT0 : 46.2) * rad;
    const decl = this.declination * rad;          // the season
    const H = (this.dayTime - 12)/24 * 2*Math.PI; // hour angle: nil at noon

    const sinAlt = Math.sin(decl)*Math.sin(phi)
                 + Math.cos(decl)*Math.cos(phi)*Math.cos(H);
    const alt = Math.asin(Math.max(-1, Math.min(1, sinAlt)));
    const cosA = (Math.sin(decl) - Math.sin(alt)*Math.sin(phi))
               / Math.max(1e-4, Math.cos(alt)*Math.cos(phi));
    let A = Math.acos(Math.max(-1, Math.min(1, cosA)));
    if(Math.sin(H) > 0) A = 2*Math.PI - A;        // afternoon: she is in the west
    this.setSun(alt/rad, A/rad);
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

  /* Lightning in a squall one is NOT in.

     A cousin of strike() and deliberately not the same thing. A stroke
     overhead lights the deck, the canvas and the whole sky together, which is
     right when the storm is on top of her and quite wrong at six miles: from
     there one sees a patch of cloud glow and nothing else — no light on the
     sails, no shadow moving. So this one never leaves the sky shader.

     Short, too. The near flash carries a four-spike envelope over a second
     because one is inside it and the detail tells; at this distance a stroke is
     a blink, and drawing it longer makes it read as a lamp rather than as
     lightning. */
  _farLightning(dt){
    const u = this.skyUniforms;
    const loom = u.uStormLoom.value;
    this._farT = (this._farT || 0) + dt;

    if(this._farA == null) this._farA = 0;
    if(this._farA > 0){
      // fast decay with a second kick, which is what a return stroke looks like
      this._farA *= Math.exp(-dt*22.0);
      if(this._farT > this._farNext2 && this._farNext2 > 0){
        this._farA = Math.max(this._farA, 0.55);
        this._farNext2 = -1;
      }
      if(this._farA < 0.004) this._farA = 0;
    }

    /* Only from a bank one can actually see, and oftener the blacker it is.
       Roughly one stroke every three seconds off a full-grown squall, which is
       a busy front but not a strobe. */
    if(loom > 0.06 && this._farA === 0 && Math.random() < loom*dt*0.34){
      this._farA = 1.0;
      this._farT = 0;
      this._farNext2 = (Math.random() < 0.55) ? 0.09 + Math.random()*0.10 : -1;
      /* Somewhere along the front rather than dead centre: the stroke gets its
         own bearing, a few tens of degrees off the middle of the bank. */
      const d = u.uStormDir.value, a = (Math.random() - 0.5)*0.95;
      const ca = Math.cos(a), sa = Math.sin(a);
      u.uStormFlashDir.value.set(d.x*ca - d.y*sa, d.x*sa + d.y*ca);
    }
    u.uStormFlash.value = this._farA;
  }

  updateWeather(dt, seaState){
    this._farLightning(dt);

    /* The weather in the sky, from the weather on the water. It comes on late
       and hard: a fresh breeze is a fine day with a lively sea, and there is no
       reason to spoil the view for it. From about force 5 the lid comes down. */
    this.setStorm((seaState - 5.0)/3.2);

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
        uFlash:{value:0},
        uCloud:this.skyUniforms.uCloud, uSkyTime:this.skyUniforms.uSkyTime,
        uStorm:this.skyUniforms.uStorm,
        uStormDir:this.skyUniforms.uStormDir, uStormLoom:this.skyUniforms.uStormLoom,
        uStormFlashDir:this.skyUniforms.uStormFlashDir,
        uStormFlash:this.skyUniforms.uStormFlash,
        /* Under water there is no sky to draw: the vault becomes the deep. */
        uSubmerged:{value:0}, uDeep:{value:new THREE.Color(0x0e3347)}
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
        uniform float uFlash, uSubmerged;
        uniform vec3 uDeep;
        ${Naval.SKY_GLSL}
        void main(){
          vec3 c = navalSky(vDir, uSun, uZenith, uHorizon);
          // the disc itself, which only the dome draws
          c += vec3(1.0,0.95,0.85) * pow(max(dot(normalize(vDir),uSun),0.0), 2200.0) * 6.0;
          // the discharge lights the whole vault, brightest low down
          c += vec3(0.62,0.70,0.92) * uFlash * (1.6 - 0.9*clamp(vDir.y,0.0,1.0));
          /* Submerged, the dome is not sky but the water beyond seeing: dark,
             and darker still looking down into it. Leaving the sky drawn would
             put a horizon inside the sea. */
          if(uSubmerged > 0.5) c = uDeep * (0.55 + 0.45*clamp(vDir.y*0.5+0.5, 0.0, 1.0));
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
      /* uStorm is shared too, so a gale darkens the light the ship is lit BY
         and not merely the backdrop she is lit against. Without it she would
         stand brightly lit under a black sky, which is the one lighting error
         nobody fails to notice. */
      uniforms:{ uSun:u.uSun, uZenith:u.uZenith, uHorizon:u.uHorizon, uFlash:u.uFlash,
                 uStorm:this.skyUniforms.uStorm,
                 uStormDir:this.skyUniforms.uStormDir,
                 uStormLoom:this.skyUniforms.uStormLoom,
                 uStormFlashDir:this.skyUniforms.uStormFlashDir,
                 uStormFlash:this.skyUniforms.uStormFlash },
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
