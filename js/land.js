/* The land, drawn.
 *
 * IN TILES, because the land is a coast now and not four islands: a square
 * grid of tiles in true world metres, each built the first time it comes in
 * range and kept afterwards — the relief never changes. Only tiles that hold
 * land or shoal water are built at all; the open sea is the sea's shader.
 * Near her a tile is drawn at the picture's own resolution, further off at a
 * quarter of it; a skirt hangs from every edge so the two never show a crack
 * where they meet.
 *
 * The vital part is still the placement. Each tile is built in its OWN frame
 * and merely POSITIONED at `tile.world − ocean.origin` every frame: the
 * floating origin can slide as far as it likes, and moving a tile is one
 * vector assignment. The ports' works (jetty, mole) live the same way, one
 * group per port.
 */
window.Naval = window.Naval || {};

Naval.Land = class Land {
  constructor(scene, world, stage){
    this.scene = scene;
    this.world = world;
    this.group = new THREE.Group();
    scene.add(this.group);

    this.range = 14000;          // metres: how far land is drawn
    this.nearRange = 4500;       // metres: within this, full detail
    this.tile = 1440;            // metres a side: 32 pixels of the relief at the start
    this.tiles = new Map();      // "i,j" → { near, far, has }
    this.ports = new Map();      // port key → group
    this._seen = new Set();

    /* Sand at the water's edge running up into grass, forest and rock. Vertex
       colours rather than a texture: the published page cannot fetch an
       image, and the band a point belongs to is a function of its height. */
    this.mat = new THREE.MeshStandardMaterial({
      vertexColors:true, roughness:0.94, metalness:0.0, flatShading:false
    });
  }

  _tint(h, out){
    const sand = this._c1 || (this._c1 = new THREE.Color(0xc9b183));
    const gras = this._c2 || (this._c2 = new THREE.Color(0x4f6a3a));
    const wood = this._c3 || (this._c3 = new THREE.Color(0x34502c));
    const rock = this._c4 || (this._c4 = new THREE.Color(0x6c665c));
    if(h < 3){ out.copy(sand); return; }
    if(h < 18){ out.copy(sand).lerp(gras, (h - 3)/15); return; }
    if(h < 120){ out.copy(gras).lerp(wood, (h - 18)/102); return; }
    if(h < 700){ out.copy(wood).lerp(rock, Math.min(1, (h - 120)/580)*0.7); return; }
    out.copy(wood).lerp(rock, 0.7 + 0.3*Math.min(1, (h - 700)/600));
  }

  /* Does this tile hold anything worth drawing? A coarse look, once. */
  _worth(i, j){
    const W = this.world, T = this.tile;
    for(let a = 0; a <= 8; a++) for(let b = 0; b <= 8; b++)
      if(W.heightAt((i + a/8)*T, (j + b/8)*T) > -20) return true;
    return false;
  }

  _build(i, j, n){
    const W = this.world, T = this.tile;
    const pos = [], col = [], idx = [];
    const c = new THREE.Color();
    const x0 = i*T, z0 = j*T;
    for(let b = 0; b <= n; b++) for(let a = 0; a <= n; a++){
      const x = a/n*T, z = b/n*T;
      const h = W.heightAt(x0 + x, z0 + z);
      pos.push(x, h, z);
      this._tint(h, c);
      col.push(c.r, c.g, c.b);
    }
    for(let b = 0; b < n; b++) for(let a = 0; a < n; a++){
      const k = b*(n + 1) + a;
      idx.push(k, k + n + 1, k + n + 2,  k, k + n + 2, k + 1);
    }
    /* The skirt: every edge vertex again, thirty metres lower, stitched to its
       original. Where a fine tile meets a coarse one the gap between their
       edges is closed by this curtain rather than left as a slit of sky. */
    const edge = [];
    for(let a = 0; a <= n; a++) edge.push(a);                           // south
    for(let b = 1; b <= n; b++) edge.push(b*(n + 1) + n);               // east
    for(let a = n - 1; a >= 0; a--) edge.push(n*(n + 1) + a);           // north
    for(let b = n - 1; b >= 1; b--) edge.push(b*(n + 1));               // west
    const base = pos.length/3;
    for(const k of edge){
      pos.push(pos[k*3], pos[k*3 + 1] - 30, pos[k*3 + 2]);
      col.push(col[k*3], col[k*3 + 1], col[k*3 + 2]);
    }
    for(let e = 0; e < edge.length; e++){
      const a = edge[e], b = edge[(e + 1) % edge.length], a2 = base + e, b2 = base + (e + 1) % edge.length;
      idx.push(a, b, a2,  b, b2, a2);          // facing out, like the top faces up
    }
    const g = new THREE.BufferGeometry();
    g.setAttribute('position', new THREE.Float32BufferAttribute(pos, 3));
    g.setAttribute('color', new THREE.Float32BufferAttribute(col, 3));
    g.setIndex(idx);
    g.computeVertexNormals();
    const mesh = new THREE.Mesh(g, this.mat);
    mesh.receiveShadow = true;
    mesh.castShadow = false;         // a coast's shadow map is not worth it
    this.group.add(mesh);
    if(this.onNew) this.onNew(mesh);    // so the page can dress it in haze
    return mesh;
  }

  /* A port's works — the mole, then the jetty — in the port's own frame. */
  _portGroup(isl){
    const g = new THREE.Group();
    if(this.jetty && isl.port){
      const w = this.jetty.mole(this.world, isl);
      if(w){ g.add(w); if(this.onNewProp) this.onNewProp(w); }
      const j = this.jetty.make(this.world, isl);
      if(j){ g.add(j); if(this.onNewProp) this.onNewProp(j); }
    }
    this.group.add(g);
    return g;
  }

  /* THE MODELS PLACED ON THE LAND (region.assets): a fort, a town, a wreck on
     a reef — each a .glb set down at a latitude and longitude, turned to
     `yaw` (true degrees), scaled by `scale`, and sat on the relief unless
     it gives its own height `y`. Loaded once; placed against the origin each
     frame like the tiles. The build carries their bytes (glbBase64). */
  async loadAssets(list){
    this.assets = [];
    if(!list || !list.length) return;
    const Loader = await Naval.loadGLTFLoader();
    for(const a of list){
      try{
        const loader = new Loader();
        const gltf = a.glbBase64
          ? await new Promise((ok, no) => loader.parse(Naval.base64ToArrayBuffer(a.glbBase64), '', ok, no))
          : await loader.loadAsync(a.glb);
        const p = Naval.Geo.toXZ(a.lat, a.lon);
        const g = new THREE.Group();
        g.add(gltf.scene);
        gltf.scene.rotation.y = -(a.yaw || 0)*Math.PI/180;
        if(a.scale) gltf.scene.scale.setScalar(a.scale);
        g.traverse(o => { if(o.isMesh){ o.castShadow = true; o.receiveShadow = true; } });
        const y = a.y != null ? a.y : Math.max(0, this.world.heightAt(p.x, p.z));
        this.group.add(g);
        if(this.onNewProp) this.onNewProp(g);
        this.assets.push({ g, x:p.x, y, z:p.z, name:a.name || a.glb });
      }catch(e){
        console.warn('[monde] modèle ' + (a.name || a.glb) + ' illisible : ' + (e && e.message || e));
      }
    }
  }

  /* Show what is in range, hide what is not, and place it all against the
     current origin. `centreWorld` is the vessel's TRUE world position. */
  update(centreWorld, origin, eager){
    const T = this.tile, R = this.range;
    const i0 = Math.floor((centreWorld.x - R)/T), i1 = Math.floor((centreWorld.x + R)/T);
    const j0 = Math.floor((centreWorld.z - R)/T), j1 = Math.floor((centreWorld.z + R)/T);
    this._seen.clear();
    let built = 0;
    for(let j = j0; j <= j1; j++) for(let i = i0; i <= i1; i++){
      const cx = (i + 0.5)*T, cz = (j + 0.5)*T;
      const d = Math.hypot(cx - centreWorld.x, cz - centreWorld.z);
      if(d > R + T*0.71) continue;
      const key = i + ',' + j;
      let t = this.tiles.get(key);
      if(!t){ t = { has: this._worth(i, j), near:null, far:null }; this.tiles.set(key, t); }
      if(!t.has) continue;
      const fine = d < this.nearRange + T*0.71;
      /* Built a few a frame at most — twenty tiles in one frame is a hitch the
         eye catches — except when asked to be EAGER, at the start, so she
         does not sail out of a harbour whose far shore is still to come. */
      if(fine && !t.near){ if(!eager && built++ > 3) continue; t.near = this._build(i, j, Math.round(T/this.world.px)); }
      if(!fine && !t.far){ if(!eager && built++ > 3) continue; t.far = this._build(i, j, Math.max(4, Math.round(T/this.world.px/4))); }
      const show = fine ? t.near : t.far, hide = fine ? t.far : t.near;
      if(hide) hide.visible = false;
      show.visible = true;
      show.position.set(i*T - origin.x, 0, j*T - origin.z);
      this._seen.add(show);
    }
    for(const [, t] of this.tiles){
      if(t.near && !this._seen.has(t.near)) t.near.visible = false;
      if(t.far && !this._seen.has(t.far)) t.far.visible = false;
    }
    // the ports' works, within sight
    for(const isl of this.world.near(centreWorld.x, centreWorld.z, this.nearRange)){
      let g = this.ports.get(isl.key);
      if(!g){ g = this._portGroup(isl); this.ports.set(isl.key, g); }
      g.userData.seen = true;
    }
    for(const [key, g] of this.ports){
      const isl = this.world.byKey(key);
      g.visible = !!g.userData.seen;
      g.userData.seen = false;
      g.position.set(isl.x - origin.x, 0, isl.z - origin.z);
    }
    for(const a of this.assets || []){
      a.g.visible = Math.hypot(a.x - centreWorld.x, a.z - centreWorld.z) < this.range;
      a.g.position.set(a.x - origin.x, a.y, a.z - origin.z);
    }
  }

  dispose(){
    for(const [, t] of this.tiles){ if(t.near) t.near.geometry.dispose(); if(t.far) t.far.geometry.dispose(); }
    this.tiles.clear();
    this.scene.remove(this.group);
    this.mat.dispose();
  }
};
