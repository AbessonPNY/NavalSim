/* Seeing her through the water.
 *
 * The sea was opaque, so a foundering ship simply stopped existing at the
 * waterline — she did not sink, she was clipped. What is wanted is the thing
 * everyone has actually seen looking over a gunwale: the hull still there a few
 * metres down, going green, then blue, then nothing.
 *
 * That needs one fact the sea cannot know on its own: how far BEHIND the surface
 * the hull is at this pixel. So the vessel is drawn once more on her own, in her
 * own materials, into a colour buffer with a depth texture; the sea then reads
 * the two and works out the thickness of water it is looking through.
 *
 * She is already isolated on a render layer for the occlusion pass, so this
 * costs one extra draw of one model and nothing else — no scene traversal, no
 * hiding and restoring, and the sea keeps its ordinary opaque depth-tested
 * render rather than becoming a transparent object with all the sorting grief
 * that would bring.
 *
 * Extinction is PER CHANNEL, which is the whole reason it reads as water rather
 * than as fog: red is gone within about three metres, green lasts twice that,
 * blue carries furthest. A single grey coefficient would fade her to grey, and
 * grey is what haze does, not what the sea does.
 */
window.Naval = window.Naval || {};

Naval.ShipBuffer = class ShipBuffer {
  constructor(renderer, scene, camera){
    this.renderer = renderer;
    this.scene = scene;
    this.camera = camera;

    this.cam = camera.clone();
    this.cam.layers.set(Naval.SHIP_LAYER);

    this.target = new THREE.WebGLRenderTarget(2, 2, {
      minFilter:THREE.LinearFilter, magFilter:THREE.LinearFilter,
      depthBuffer:true, stencilBuffer:false
    });
    this.target.depthTexture = new THREE.DepthTexture(2, 2);
    this.target.depthTexture.type = THREE.UnsignedIntType;
    this._w = 0; this._h = 0;
  }

  get texture(){ return this.target.texture; }
  get depth(){ return this.target.depthTexture; }

  _resize(){
    const s = this.renderer.getDrawingBufferSize(new THREE.Vector2());
    const w = Math.max(2, s.x), h = Math.max(2, s.y);
    if(w === this._w && h === this._h) return;
    this._w = w; this._h = h;
    this.target.setSize(w, h);
    this.target.depthTexture.image.width = w;
    this.target.depthTexture.image.height = h;
    this.target.depthTexture.needsUpdate = true;
  }

  /* Her colour and her depth, alone against a cleared buffer. Alpha is the mask:
     zero where she is not, because the clear leaves it zero and her own
     materials write one. */
  render(){
    this._resize();
    const r = this.renderer;
    const prevRT = r.getRenderTarget();
    const cc = r.getClearColor(new THREE.Color()).clone(), ca = r.getClearAlpha();

    this.cam.copy(this.camera);
    this.cam.layers.set(Naval.SHIP_LAYER);

    r.setRenderTarget(this.target);
    r.setClearColor(0x000000, 0);
    r.clear();
    r.render(this.scene, this.cam);

    r.setClearColor(cc, ca);
    r.setRenderTarget(prevRT);
  }
};
