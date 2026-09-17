/* Snow — the rain's lattice, falling slowly.
 *
 * The same trick as rain.js, for the same reasons: a box of seed points folded
 * about the eye by a modulo, so it costs no CPU and has true parallax wherever
 * she sails. What changes is what the eye reads as snow: a round FLAKE rather
 * than a streak, a fall of about a metre a second, and a sway — each flake
 * wanders sideways on its own slow circle, and the wind carries the whole
 * fall over.
 *
 * It REFLECTS, so it takes the colour of the light (the page hands it the
 * horizon's), grey-blue at night and never glowing.
 */
window.Naval = window.Naval || {};

Naval.Snow = class Snow {
  constructor(scene){
    this.scene = scene;
    this.box = 60;
    this.count = 5200;
    this.fall = 1.1;        // m/s

    const pos = new Float32Array(this.count*3), seed = new Float32Array(this.count);
    for(let i=0;i<this.count;i++){
      pos[i*3] = Math.random()*this.box;
      pos[i*3+1] = Math.random()*this.box;
      pos[i*3+2] = Math.random()*this.box;
      seed[i] = Math.random()*100;
    }
    const g = new THREE.BufferGeometry();
    g.setAttribute('position', new THREE.BufferAttribute(pos, 3));
    g.setAttribute('seed', new THREE.BufferAttribute(seed, 1));

    this.uniforms = {
      uEye:{ value:new THREE.Vector3() },
      uDrift:{ value:new THREE.Vector3(0, -this.fall, 0) },
      uTime:{ value:0 }, uBox:{ value:this.box }, uAmt:{ value:0 },
      uScale:{ value:600 },
      uColor:{ value:new THREE.Color(0xe8eef2) }
    };
    this.mat = new THREE.ShaderMaterial({
      uniforms:this.uniforms, transparent:true, depthWrite:false,
      vertexShader:`
        attribute float seed;
        uniform vec3 uEye, uDrift;
        uniform float uTime, uBox, uAmt, uScale;
        varying float vFade;
        void main(){
          // each flake swings on its own slow circle as it falls
          vec3 sway = vec3(sin(uTime*0.7 + seed), 0.0, cos(uTime*0.55 + seed*1.3)) * 0.6;
          vec3 p = position + uDrift*uTime + sway;
          vec3 rel = mod(p - uEye + uBox*0.5, uBox) - uBox*0.5;
          float d = length(rel);
          vFade = uAmt * (1.0 - smoothstep(uBox*0.24, uBox*0.48, d));
          // fewer flakes in a light fall: the lattice is thinned by seed
          if(fract(seed*0.618) > uAmt) vFade = 0.0;
          vec4 mv = viewMatrix * vec4(uEye + rel, 1.0);
          gl_PointSize = clamp(uScale*(0.045 + 0.03*fract(seed))/max(0.5, -mv.z), 1.0, 14.0);
          gl_Position = projectionMatrix * mv;
        }`,
      fragmentShader:`
        uniform vec3 uColor;
        varying float vFade;
        void main(){
          if(vFade <= 0.003) discard;
          vec2 q = gl_PointCoord*2.0 - 1.0;
          float r = dot(q, q);
          if(r > 1.0) discard;
          gl_FragColor = vec4(uColor, vFade*(1.0 - r)*0.9);
        }`
    });
    this.group = new THREE.Points(g, this.mat);
    this.group.frustumCulled = false;     // drawn about the eye, not where its vertices sit
    this.group.renderOrder = 6;
    this.group.visible = false;
    scene.add(this.group);
  }

  /* amount 0..1, wind the true wind (m/s), light a colour for what it reflects,
     height the drawing buffer's height in pixels (flakes are sized to it). */
  update(dt, t, eye, wind, amount, light, height){
    const u = this.uniforms;
    u.uAmt.value = amount;
    this.group.visible = amount > 0.004;
    if(!this.group.visible) return;
    u.uTime.value = t;
    u.uEye.value.copy(eye);
    // snow is light: the wind carries it most of the way to its own speed
    u.uDrift.value.set(wind.x*0.85, -this.fall, wind.z*0.85);
    if(light) u.uColor.value.copy(light);
    if(height) u.uScale.value = height;
  }
};
