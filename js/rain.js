/* A curtain of rain.
 *
 * Rain is one of the few things in a scene that is genuinely cheap, provided
 * one does not try to simulate it. There is no wind field, no collision and no
 * splash here: there is a LATTICE of drops, infinite in every direction, and
 * the eye simply moves through it.
 *
 * That lattice is the whole idea. Each drop holds a fixed seed point, and the
 * vertex shader folds `seed − eye` back into one box by a modulo before adding
 * the eye again. The result is a box of rain that is always centred on the
 * viewer however far she sails, with true parallax — a drop a metre away sweeps
 * past, a drop thirty metres off barely moves — and no wrapping to keep track
 * of, no positions to rewrite, and not one byte of CPU work per frame. The
 * floating origin costs it nothing for the same reason: everything is relative
 * to an eye that is handed in every frame.
 *
 * Drawn as line segments rather than points, because what the eye recognises as
 * rain is the STREAK. A falling dot reads as snow, or as dirt on the lens; the
 * length and the slant of the streak are what say "rain", and both come free
 * from the fall vector the drop is already using.
 */
window.Naval = window.Naval || {};

Naval.Rain = class Rain {
  constructor(scene){
    this.scene = scene;
    this.box   = 74;        // metres: the cube of rain carried about the eye
    this.count = 6200;
    this.fall  = 7.4;       // m/s — near enough the terminal speed of a raindrop

    const pos = new Float32Array(this.count*2*3);
    const tip = new Float32Array(this.count*2);
    for(let i=0;i<this.count;i++){
      const x = Math.random()*this.box, y = Math.random()*this.box, z = Math.random()*this.box;
      /* Two vertices per drop, on the SAME seed point: the streak is grown
         along the fall vector in the shader, after the wrap, so a drop
         straddling the edge of the box is never torn in half. */
      pos[i*6+0]=x; pos[i*6+1]=y; pos[i*6+2]=z;
      pos[i*6+3]=x; pos[i*6+4]=y; pos[i*6+5]=z;
      tip[i*2+0]=0; tip[i*2+1]=1;
    }
    const g = new THREE.BufferGeometry();
    g.setAttribute('position', new THREE.BufferAttribute(pos, 3));
    g.setAttribute('tip', new THREE.BufferAttribute(tip, 1));

    this.uniforms = {
      uEye:  {value:new THREE.Vector3()},
      uSlant:{value:new THREE.Vector3(0,-this.fall,0)},   // metres per second
      uTime: {value:0},
      uBox:  {value:this.box},
      uLen:  {value:1.2},
      uAmt:  {value:0},
      uColor:{value:new THREE.Color(0xb9c8d4)}
    };

    this.mat = new THREE.ShaderMaterial({
      uniforms:this.uniforms,
      transparent:true, depthWrite:false,
      vertexShader:`
        attribute float tip;
        uniform vec3 uEye, uSlant;
        uniform float uTime, uBox, uLen, uAmt;
        varying float vFade;
        void main(){
          /* Fall first, wrap second. Doing it the other way would let a drop
             leave the box between the two and never come back. */
          vec3 p = position + uSlant*uTime;
          vec3 rel = mod(p - uEye + uBox*0.5, uBox) - uBox*0.5;
          vec3 w = uEye + rel - normalize(uSlant)*(tip*uLen);

          /* Thinned out toward the corners of the box rather than cut off at
             them: a hard edge to the rain is a wall of drops appearing out of
             nothing, and the eye finds it at once. */
          float d = length(rel);
          vFade = uAmt * (1.0 - smoothstep(uBox*0.26, uBox*0.48, d));

          gl_Position = projectionMatrix * viewMatrix * vec4(w, 1.0);
        }`,
      fragmentShader:`
        uniform vec3 uColor;
        varying float vFade;
        void main(){
          if(vFade <= 0.003) discard;
          gl_FragColor = vec4(uColor, vFade*0.5);
        }`
    });

    this.group = new THREE.LineSegments(g, this.mat);
    /* Its vertices sit in a 74 m box at the origin while it is DRAWN around the
       eye, so every bounding volume three could compute about it is a lie.
       Left to cull itself it vanishes the moment the ship sails away from the
       world origin — and being the one object whose geometry means nothing
       without its shader, it is the one object that must never be culled. */
    this.group.frustumCulled = false;
    this.group.renderOrder = 6;
    this.group.visible = false;
    scene.add(this.group);
  }

  /* `amount` runs from nought to one; `wind` is the true wind, in m/s. */
  update(dt, t, eye, wind, amount){
    const u = this.uniforms;
    u.uAmt.value = amount;
    this.group.visible = amount > 0.004;
    if(!this.group.visible) return;

    u.uTime.value = t;
    u.uEye.value.copy(eye);

    /* The slant is not decoration: it is the single cue that says how hard it
       is blowing. Rain falls at about the same speed whatever the weather, so
       in a gale the wind lays it over until it is coming almost horizontally,
       and that angle is read instantly and without thinking. Taken at rather
       less than the full wind, since a drop needs a moment to be carried up to
       the speed of the air about it. */
    u.uSlant.value.set(wind.x*0.62, -this.fall, wind.z*0.62);
    const v = u.uSlant.value.length();
    // a faster drop draws a longer streak, which is what a photograph shows too
    u.uLen.value = 0.8 + v*0.055;
  }

  dispose(){
    this.scene.remove(this.group);
    this.group.geometry.dispose();
    this.mat.dispose();
  }
};
