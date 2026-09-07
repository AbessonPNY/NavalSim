/* Renderer, scene, camera, light and sky — the room the vessel sails in. */
window.Naval = window.Naval || {};

Naval.Stage = class Stage {
  constructor(canvas){
    this.renderer = new THREE.WebGLRenderer({canvas, antialias:true, powerPreference:'high-performance'});
    this.renderer.setPixelRatio(Math.min(window.devicePixelRatio||1, 2));
    this.renderer.setClearColor(0x0a1a2b, 1);

    this.scene = new THREE.Scene();
    this.scene.fog = new THREE.FogExp2(0x9fc0d4, 0.0018);
    this.camera = new THREE.PerspectiveCamera(55, 1, 0.5, 6000);

    this.sunDir = new THREE.Vector3(-0.55, 0.62, -0.55).normalize();
    const sun = new THREE.DirectionalLight(0xfff2dc, 2.1);
    sun.position.copy(this.sunDir).multiplyScalar(200);
    this.scene.add(sun);
    this.scene.add(new THREE.HemisphereLight(0xbcd8ec, 0x1a3346, 0.9));

    this._addSky();
    addEventListener('resize', ()=> this.resize());
    this.resize();
  }

  _addSky(){
    const g = new THREE.SphereGeometry(3200, 32, 16);
    const m = new THREE.ShaderMaterial({
      side:THREE.BackSide, depthWrite:false,
      uniforms:{ top:{value:new THREE.Color(0x1e5c86)}, bot:{value:new THREE.Color(0xcfe4ee)},
                 sun:{value:this.sunDir.clone()} },
      vertexShader:`varying vec3 vp; void main(){ vp=normalize(position); gl_Position=projectionMatrix*modelViewMatrix*vec4(position,1.0);}`,
      fragmentShader:`varying vec3 vp; uniform vec3 top,bot,sun;
        void main(){ float h=clamp(vp.y*0.5+0.5,0.0,1.0);
          vec3 c=mix(bot,top,pow(h,0.72));
          float s=pow(max(dot(normalize(vp),normalize(sun)),0.0),140.0);
          c+=vec3(1.0,0.9,0.7)*s*0.6;
          float glow=pow(max(dot(normalize(vp),normalize(sun)),0.0),6.0);
          c+=vec3(1.0,0.86,0.6)*glow*0.16;
          gl_FragColor=vec4(c,1.0);}`
    });
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
