/* Gulls over the islands.
 *
 * Birds are the cheapest thing in the world that says "there is land here".
 * They cost almost nothing and they read at a distance, because what the eye
 * recognises is not the animal but the MOTION: a slow circle over the shore,
 * wings beating in bursts and then held out flat to glide.
 *
 * So the whole of this file is that motion. Each gull is three little meshes —
 * two wings and a body — and everything else is arithmetic: where she is on her
 * circle, how she banks into it, and whether she is beating or gliding just now.
 *
 * Placed the same way the land is: the flock is built around an island in the
 * ISLAND's frame and merely positioned against the current origin, so the
 * floating origin can slide without any of it needing to be rebuilt.
 */
window.Naval = window.Naval || {};

Naval.Gulls = class Gulls {
  constructor(scene, world){
    this.scene = scene;
    this.world = world;

    this.range   = 2600;    // metres: beyond this a gull is under a pixel wide
    this.count   = 12;
    this.span    = 2.2;     // wingspan, a big gull

    this.group = new THREE.Group();
    this.group.visible = false;
    scene.add(this.group);

    /* Grey mantle running out to black tips, laid in as vertex colours. It is
       the one marking that survives being three pixels tall, and it costs
       nothing — a texture could not be fetched by the published page anyway. */
    this.wingMat = new THREE.MeshStandardMaterial({
      vertexColors:true, roughness:0.82, metalness:0.0, side:THREE.DoubleSide
    });
    this.bodyMat = new THREE.MeshStandardMaterial({
      color:0xf2f0ea, roughness:0.85, metalness:0.0
    });
    this.materials = [this.wingMat, this.bodyMat];

    this.wingR = this._wing(+1);
    this.wingL = this._wing(-1);
    this.tailG = this._tail();
    this.bodyG = new THREE.SphereGeometry(1, 7, 5);

    this.birds = [];
    for(let i=0;i<this.count;i++) this.birds.push(this._bird(i));

    this.island = null;
    this._t = 0;
  }

  /* One wing, in the bird's frame: +Z forward, +X to starboard, +Y up.
     Swept back and tapered, because a straight rectangle reads as a paper
     aeroplane however well it flaps. */
  _wing(side){
    /* Long and thin. The first cut had a chord of 0.36 m on a 0.9 m half-span —
       an aspect ratio of five, which is a gliding pigeon. A gull is nearer ten,
       and that narrowness is most of what the eye recognises at any distance. */
    const s = this.span*0.5;
    const P = [
      [0,          0, 0.13], [0,        0, -0.13],
      [s*0.52*side,0, 0.08], [s*0.52*side,0,-0.10],
      [s*side,     0, -0.06],[s*0.90*side,0,-0.17]
    ];
    const idx = side > 0
      ? [0,2,3, 0,3,1, 2,4,5, 2,5,3]
      : [0,3,2, 0,1,3, 2,5,4, 2,3,5];   // wound the other way, since x is mirrored

    const pos = [], col = [];
    const root = new THREE.Color(0xeeece6), mid = new THREE.Color(0x9aa2a8);
    const tip  = new THREE.Color(0x24252a);
    const c = new THREE.Color();
    for(const p of P){
      pos.push(p[0], p[1], p[2]);
      const u = Math.abs(p[0])/s;                 // 0 at the shoulder, 1 at the tip
      if(u < 0.62) c.copy(root).lerp(mid, u/0.62);
      else         c.copy(mid).lerp(tip, (u-0.62)/0.38);
      col.push(c.r, c.g, c.b);
    }
    const g = new THREE.BufferGeometry();
    g.setAttribute('position', new THREE.Float32BufferAttribute(pos, 3));
    g.setAttribute('color',    new THREE.Float32BufferAttribute(col, 3));
    g.setIndex(idx);
    g.computeVertexNormals();
    return g;
  }

  _tail(){
    const P = [[0,0,-0.22], [-0.11,0,-0.46], [0.11,0,-0.46], [0,0,-0.40]];
    const pos = [], col = [];
    for(const p of P){ pos.push(p[0], p[1], p[2]); col.push(0.93, 0.92, 0.89); }
    const g = new THREE.BufferGeometry();
    g.setAttribute('position', new THREE.Float32BufferAttribute(pos, 3));
    g.setAttribute('color',    new THREE.Float32BufferAttribute(col, 3));
    g.setIndex([0,1,3, 0,3,2]);
    g.computeVertexNormals();
    return g;
  }

  _bird(i){
    const g = new THREE.Group();
    const wr = new THREE.Mesh(this.wingR, this.wingMat);
    const wl = new THREE.Mesh(this.wingL, this.wingMat);
    const bd = new THREE.Mesh(this.bodyG, this.bodyMat);
    bd.scale.set(0.075, 0.085, 0.32);
    /* The tail does not flap — it is the one fixed thing in the outline, and
       without it the bird is a body between two wings, which reads as a dart. */
    const tl = new THREE.Mesh(this.tailG, this.wingMat);
    g.add(wr, wl, bd, tl);
    this.group.add(g);

    /* Every constant that makes one bird not another. Fixed at birth: a flock
       whose members are re-randomised each frame flickers instead of flying. */
    const r = (a,b) => a + Math.random()*(b-a);
    /* A third of them come out to the ship. Gulls do follow a vessel, and the
       ones that matter to the eye are these: a bird over the island is three
       pixels at four hundred metres, whereas one turning over the poop is a
       bird. The rest stay on their island so the place still has its own life
       when nothing is passing. */
    const near = i < 4;
    return {
      obj:g, wr, wl, follow:near,
      radius: near ? r(22, 78)      // metres, about the vessel herself
                   : r(0.30, 0.95), // or a fraction of the island's own reach
      alt:    near ? r(11, 34) : r(14, 46),
      omega:  (near ? r(0.055, 0.135) : r(0.028, 0.062)) * (Math.random() < 0.5 ? -1 : 1),
      phase:  Math.random()*Math.PI*2,
      beat:   r(2.4, 3.4),          // wingbeats per second
      bphase: Math.random()*Math.PI*2,
      gust:   r(0.055, 0.10),       // how often she stops beating and glides
      gphase: Math.random()*Math.PI*2,
      bob:    r(0.9, 2.6)
    };
  }

  /* Scatter the flock over a new island. Only the radii need touching: they are
     kept as fractions of the island's reach, so a big island gets a wide circle
     and a rock gets a tight one without a second set of numbers. */
  _settleOn(isl){
    this.island = isl;
    this.reach = this.world._shore(isl, 0) * 0.85;
  }

  /* `centreWorld` is the vessel's TRUE position; `origin` the floating origin. */
  update(dt, centreWorld, origin){
    this._t += dt;

    /* The nearest island, and only if she is close enough for a gull to be more
       than a speck. Islands further off are simply left empty — birds drawn at
       three kilometres are pixels of noise in the haze. */
    const near = this.world.near(centreWorld.x, centreWorld.z, this.range);
    let best = null, bestD = Infinity;
    for(const isl of near){
      const d = Math.hypot(isl.x - centreWorld.x, isl.z - centreWorld.z);
      if(d < bestD){ bestD = d; best = isl; }
    }
    if(!best){ this.group.visible = false; this.island = null; return; }
    if(!this.island || this.island.key !== best.key) this._settleOn(best);
    this.group.visible = true;

    const ix = best.x - origin.x, iz = best.z - origin.z;
    const sx = centreWorld.x - origin.x, sz = centreWorld.z - origin.z;
    const t = this._t;

    for(const b of this.birds){
      const a = b.phase + b.omega*t;
      const cx = b.follow ? sx : ix, cz = b.follow ? sz : iz;
      const R = b.follow ? b.radius : this.reach * b.radius;
      const x = cx + Math.cos(a)*R, z = cz + Math.sin(a)*R;
      const y = b.alt + Math.sin(t*0.31 + b.phase)*b.bob;
      b.obj.position.set(x, y, z);

      /* Heading is the tangent to her circle, and she banks INTO it — the one
         cue that separates a bird on the wing from a cardboard cut-out sliding
         round a ring. The sign follows the direction she is turning. */
      const vx = -Math.sin(a)*b.omega, vz = Math.cos(a)*b.omega;
      b.obj.rotation.set(0, Math.atan2(vx, vz), b.omega > 0 ? 0.30 : -0.30, 'YXZ');

      /* Beating comes in bursts. A gull that flaps without pause looks
         mechanical; the glides are what make her weigh something. */
      const gate = Math.max(0, Math.sin(t*b.gust*Math.PI*2 + b.gphase));
      const drive = Math.pow(gate, 0.6);
      const beat = Math.sin(t*b.beat*Math.PI*2 + b.bphase);
      /* Down faster than up: the recovery stroke is the slow half. */
      const f = (beat > 0 ? beat*0.62 : beat) * drive * 0.85 + 0.06;
      b.wr.rotation.z =  f;
      b.wl.rotation.z = -f;
    }
  }

  dispose(){
    this.scene.remove(this.group);
    this.wingR.dispose(); this.wingL.dispose();
    this.bodyG.dispose(); this.tailG.dispose();
    this.wingMat.dispose(); this.bodyMat.dispose();
  }
};
