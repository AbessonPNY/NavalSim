/* The land, drawn.
 *
 * One mesh per island, built the first time she comes within sight of it and
 * kept afterwards — an island never changes, so rebuilding it would be work
 * spent to get the same triangles back.
 *
 * The vital part is the placement. Geometry is built in the ISLAND's own frame,
 * centred on nothing, and the mesh is merely POSITIONED at
 * `island.world − ocean.origin` every frame. The floating origin can therefore
 * slide as far as it likes: the land does not need rebuilding, it needs moving,
 * and moving it is one vector assignment. Building the vertices in local
 * coordinates instead would have meant regenerating every island at every
 * rebase, which is exactly the trap the floating origin exists to avoid.
 */
window.Naval = window.Naval || {};

Naval.Land = class Land {
  constructor(scene, world, stage){
    this.scene = scene;
    this.world = world;
    this.group = new THREE.Group();
    scene.add(this.group);

    this.range = 11000;          // metres: how far land is drawn
    this.meshes = new Map();     // island key → mesh
    this._seen = new Set();

    /* Sand at the water's edge running up into rock and a little grass. Vertex
       colours rather than a texture: the published page cannot fetch an image,
       and the band a point belongs to is a function of its height anyway. */
    this.mat = new THREE.MeshStandardMaterial({
      vertexColors:true, roughness:0.94, metalness:0.0, flatShading:false
    });
  }

  /* The visible band a height belongs to. Sea level is 0, so the sand starts
     just below it — a beach that begins exactly at the waterline shows a hard
     line all round the island, and no shore looks like that. */
  _tint(h, out){
    const sand = new THREE.Color(0xc9b183), rock = new THREE.Color(0x6c665c);
    const gras = new THREE.Color(0x4a5a3a), high = new THREE.Color(0x8d8c86);
    if(h < 6){ out.copy(sand); return; }
    if(h < 26){ out.copy(sand).lerp(gras, (h-6)/20); return; }
    if(h < 95){ out.copy(gras).lerp(rock, (h-26)/69); return; }
    out.copy(rock).lerp(high, Math.min(1, (h-95)/120));
  }

  _build(isl){
    const W = this.world;
    const RA = 96, RR = 40;                 // around, and out from the summit
    const pos = [], col = [], idx = [];
    const c = new THREE.Color();

    for(let j=0;j<=RR;j++){
      /* Rings bunched toward the shore: that is where the shape is, and an even
         spread wastes half the vertices on a summit that is nearly flat. */
      const v = j/RR, t = 1.42*Math.pow(v, 0.72);
      for(let i=0;i<=RA;i++){
        const a = (i/RA)*Math.PI*2;
        const s = W._shore(isl, a);
        const d = t*s;
        const x = Math.cos(a)*d, z = Math.sin(a)*d;
        const h = W.heightAt(isl.x + x, isl.z + z);
        pos.push(x, h, z);
        this._tint(h, c);
        col.push(c.r, c.g, c.b);
      }
    }
    for(let j=0;j<RR;j++) for(let i=0;i<RA;i++){
      const k = j*(RA+1) + i;
      idx.push(k, k+RA+1, k+RA+2,  k, k+RA+2, k+1);
    }

    const g = new THREE.BufferGeometry();
    g.setAttribute('position', new THREE.Float32BufferAttribute(pos, 3));
    g.setAttribute('color', new THREE.Float32BufferAttribute(col, 3));
    g.setIndex(idx);
    g.computeVertexNormals();

    const mesh = new THREE.Mesh(g, this.mat);
    mesh.receiveShadow = true;
    mesh.castShadow = false;         // a whole island's shadow map is not worth it
    mesh.userData.island = isl;

    /* And her PORT goes on as a child of the island, which is what makes the
       floating origin free for it: this mesh is the thing that gets moved
       against the current origin every frame, so anything parented to it is
       recentred for nothing and can never drift off its own beach. Written as
       a sibling it would have needed its own place in the rebase, which is the
       list this project has already forgotten something on twice. */
    if(this.jetty && isl.port){
      /* Le môle d'abord : c'est lui le havre, le ponton n'est que ce à quoi on
         s'amarre dedans. */
      const w = this.jetty.mole(this.world, isl);
      if(w){ mesh.add(w); if(this.onNewProp) this.onNewProp(w); }
      const j = this.jetty.make(this.world, isl);
      if(j){ mesh.add(j); if(this.onNewProp) this.onNewProp(j); }
    }
    return mesh;
  }

  /* Show what is in sight, hide what is not, and place it all against the
     current origin. `centre` is the vessel's TRUE world position. */
  update(centreWorld, origin){
    const near = this.world.near(centreWorld.x, centreWorld.z, this.range);
    this._seen.clear();

    for(const isl of near){
      this._seen.add(isl.key);
      let m = this.meshes.get(isl.key);
      if(!m){
        m = this._build(isl);
        this.meshes.set(isl.key, m);
        this.group.add(m);
        if(this.onNew) this.onNew(m);      // so the page can dress it in haze
      }
      m.visible = true;
      m.position.set(isl.x - origin.x, 0, isl.z - origin.z);
    }

    // out of sight: kept in memory, but not submitted
    for(const [key, m] of this.meshes) if(!this._seen.has(key)) m.visible = false;
  }

  dispose(){
    for(const [, m] of this.meshes) m.geometry.dispose();
    this.meshes.clear();
    this.scene.remove(this.group);
    this.mat.dispose();
  }
};
