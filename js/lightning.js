/* Lightning that finds a ship.
 *
 * The sky already flickers inside a squall (stage.strike) and on the banks one
 * is not in (stage._farLightning); neither ever touches anything. This is the
 * stroke that comes DOWN — to the highest thing standing out of the sea, which
 * at sea is a masthead. It is chosen per vessel, oftener the deeper she is in
 * the depression, and what it does to her is the page's business: this file
 * draws the bolt and says where it landed.
 *
 * The bolt EMITS, so it does not follow the light and wears no haze patch —
 * it is the brightest thing in the scene for a quarter of a second. And it
 * carries no lamp: the scene's light count never changes in play. The deck is
 * lit by the stage's own flash, which is already wired to sky, sea and rig.
 */
window.Naval = window.Naval || {};

/* Game rules, overridden by settings.json → storm.lightning. */
Naval.LIGHTNING = {
  enabled: true,
  minInten: 0.4,          // depression intensity below which nothing comes down
  perMinuteAtCore: 0.5,   // strokes a minute on one ship at the very centre
  splitChance: 0.5,       // a sail blown out of its bolt-ropes
  woundChance: 0.6,       // the mast hurt (three wounds and it goes, as for shot)
  dismastChance: 0.12     // the mast split outright
};

Naval.Lightning = class Lightning {
  constructor(scene){
    this.group = new THREE.Group();
    scene.add(this.group);
    this.mat = new THREE.MeshBasicMaterial({
      color:0xe4ecff, transparent:true, opacity:1,
      blending:THREE.AdditiveBlending, depthWrite:false, fog:false });
    this.bolts = [];
    this._p = new THREE.Vector3();
  }

  /* One stroke from the cloud base down to `top` (local metres). Jagged, with a
     branch or two off its upper half: a straight tube reads as a laser. */
  bolt(top){
    /* A random walk sideways, not noise about a straight line: lightning
       steps, and each step starts where the last one ended. Jittered about
       the line it read as a beam — seen from a cable off, a tube four hundred
       metres long with a few metres of wobble IS straight. */
    const pts = [], n = 30;
    const sx = top.x + (Math.random() - 0.5)*160, sz = top.z + (Math.random() - 0.5)*160;
    const sy = top.y + 420;
    let ox = 0, oz = 0;
    for(let i=0;i<=n;i++){
      const u = i/n;
      if(i && i < n){
        // a zigzag: each step kicks the other way, by eight to thirty metres
        const j = (8 + Math.random()*22)*(i % 2 ? 1 : -1), a = Math.random()*Math.PI;
        ox += j*Math.cos(a); oz += j*Math.sin(a);
      }
      // pulled back onto the masthead over the last stretch
      const k = i === n ? 0 : Math.min(1, (1 - u)*3);
      pts.push(new THREE.Vector3(sx + (top.x - sx)*u + ox*k, sy + (top.y - sy)*u, sz + (top.z - sz)*u + oz*k));
    }
    const parts = [this._tube(pts, 0.3)];
    for(let k=0;k<2;k++){
      const from = pts[3 + Math.floor(Math.random()*12)];
      const br = [from.clone()], m = 7;
      const dx = (Math.random() - 0.5)*70, dz = (Math.random() - 0.5)*70;
      for(let i=1;i<=m;i++){
        const u = i/m;
        br.push(new THREE.Vector3(from.x + dx*u + (Math.random() - 0.5)*14,
                                  from.y - 90*u, from.z + dz*u + (Math.random() - 0.5)*14));
      }
      parts.push(this._tube(br, 0.14));
    }
    this.bolts.push({ parts, age:0 });
  }

  _tube(pts, r){
    const path = new THREE.CurvePath();
    for(let i=1;i<pts.length;i++) path.add(new THREE.LineCurve3(pts[i-1], pts[i]));
    const g = new THREE.TubeGeometry(path, pts.length*2, r, 5, false);
    const m = new THREE.Mesh(g, this.mat);
    m.frustumCulled = false;
    this.group.add(m);
    return m;
  }

  /* `list` holds { entry, inten } for every vessel worth asking about;
     `onStrike(entry)` is called when one of them is hit. */
  update(dt, list, onStrike){
    for(let i=this.bolts.length-1;i>=0;i--){
      const b = this.bolts[i];
      b.age += dt;
      if(b.age > 0.25){          // gone as soon as it has struck: a stroke, not a lamp
        for(const m of b.parts){ this.group.remove(m); m.geometry.dispose(); }
        this.bolts.splice(i, 1);
      }
    }
    /* The same stroke shape as the sky's: a first flash, a dip, a return
       stroke, then gone. One opacity for all bolts, since two at once is
       rare and they would flicker together anyway. */
    if(this.bolts.length){
      const a = this.bolts[this.bolts.length - 1].age;
      this.mat.opacity = a < 0.06 ? 1 : a < 0.10 ? 0.25 : a < 0.16 ? 0.9 : Math.max(0, 0.5*(1 - (a - 0.16)/0.09));
    }

    const L = Naval.LIGHTNING;
    if(!L.enabled || !onStrike) return;
    for(const it of list){
      if(!(it.inten > L.minInten)) continue;
      const k = (it.inten - L.minInten)/(1 - L.minInten);
      if(Math.random() < L.perMinuteAtCore*k*dt/60) onStrike(it.entry);
    }
  }

  rebase(dx, dz){
    for(const b of this.bolts) for(const m of b.parts){ m.position.x -= dx; m.position.z -= dz; }
  }
};
