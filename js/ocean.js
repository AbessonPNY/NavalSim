/* The sea: a sum of Gerstner waves.
   One set of parameters drives BOTH the GPU mesh and the CPU height field the
   buoyancy solver samples, so the hull rides the very waves you see. Wave speed
   comes from the deep-water dispersion relation ω = √(gk), so long swells
   genuinely outrun short chop. */
window.Naval = window.Naval || {};

Naval.Ocean = class Ocean {
  constructor(scene, sunDir){
    const C = Naval.Config;
    this.C = C;
    this.waves = [];
    this.windSpeed = 0;                    // m/s, true wind
    this.windVec = new THREE.Vector3();    // true wind velocity (blows toward)

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
        uDeep:{value:new THREE.Color(0x0a2436)},
        uShallow:{value:new THREE.Color(0x1c6d84)},
        uSky:{value:new THREE.Color(0x9fc7dc)},
      }
    );

    const mat = new THREE.ShaderMaterial({
      uniforms:this.uniforms,
      fog:true,
      defines:{NW:C.NWAVES},
      vertexShader:`
        uniform float uTime; uniform vec4 uWaveA[NW]; uniform vec2 uWaveB[NW];
        varying vec3 vN; varying vec3 vW; varying float vFoam;
        #include <fog_pars_vertex>
        void main(){
          // The plane is re-centred on the camera every frame, so the wave phase
          // MUST come from world space — otherwise the swell would be glued to
          // the camera (no sense of headway) and would drift out of step with
          // the CPU height field the buoyancy solver samples.
          vec3 w0 = (modelMatrix * vec4(position,1.0)).xyz;
          vec3 p = w0; vec3 n = vec3(0.0);
          float steep = 0.0;
          for(int i=0;i<NW;i++){
            vec2 d = uWaveA[i].xy; float amp=uWaveA[i].z; float k=uWaveA[i].w;
            float omega=uWaveB[i].x; float Q=uWaveB[i].y;
            float f = k*dot(d, w0.xz) - omega*uTime;
            float c = cos(f), s = sin(f);
            p.x += Q*amp*d.x*c;
            p.z += Q*amp*d.y*c;
            p.y += amp*s;
            float WA = k*amp;
            n.x -= d.x*WA*c; n.z -= d.y*WA*c; n.y -= Q*WA*s;
            steep += Q*WA*max(s,0.0);
          }
          vN = normalize(vec3(n.x, 1.0-n.y, n.z));
          vW = p;
          vFoam = smoothstep(0.55,0.95,steep);
          vec4 mvPosition = viewMatrix*vec4(p,1.0);   // p is already world space
          gl_Position = projectionMatrix*mvPosition;
          #include <fog_vertex>
        }`,
      fragmentShader:`
        precision highp float;
        uniform vec3 uSun,uCam,uDeep,uShallow,uSky;
        varying vec3 vN; varying vec3 vW; varying float vFoam;
        #include <fog_pars_fragment>
        void main(){
          vec3 N = normalize(vN);
          vec3 V = normalize(uCam - vW);
          float fres = pow(1.0 - max(dot(N,V),0.0), 4.0);
          float facing = clamp(dot(N,V),0.0,1.0);
          vec3 base = mix(uDeep, uShallow, facing*0.7);
          vec3 col = mix(base, uSky, clamp(fres,0.0,0.9));
          vec3 H = normalize(uSun + V);
          float spec = pow(max(dot(N,H),0.0), 220.0);
          col += vec3(1.0,0.94,0.78)*spec*1.4;
          float diff = max(dot(N, uSun),0.0);
          col += uShallow*diff*0.08;
          col = mix(col, vec3(0.86,0.93,0.96), clamp(vFoam,0.0,0.75));
          gl_FragColor = vec4(col,1.0);
          #include <fog_fragment>
        }`
    });

    this.mesh = new THREE.Mesh(geo, mat);
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
    }
    this.syncUniforms();
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
};
