/* Lightning that finds a ship.
 *
 * The sky already flickers inside a squall (stage.strike) and on the banks one
 * is not in (stage._farLightning); neither ever touches anything. This is the
 * stroke that comes DOWN — to the highest thing standing out of the sea, which
 * at sea is a masthead. It is chosen per vessel, oftener the deeper she is in
 * the depression, and what it does to her is the page's business: this file
 * draws the bolt and says where it landed.
 *
 * DRAWN, THEN HUNG IN THE SKY. A tube along a jagged line was tried three ways
 * and read as a laser every time: seen from a cable off, four hundred metres of
 * stroke with a few tens of metres of wander IS straight. So the bolt is a
 * picture — painted on a canvas the way Arnaud sketched it, wide horizontal
 * zigzags stepping down with hooks and forks, a violet glow round a white core
 * — on a strip that always turns its face to the camera, pinned between the
 * cloud and the masthead. Being in the scene it is depth-tested, lands exactly
 * on the truck, and is in every capture.
 *
 * The bolt EMITS, so it does not follow the light and wears no haze patch. And
 * it carries no lamp: the scene's light count never changes in play. The deck
 * is lit by the stage's own flash, which is already wired to sky, sea and rig.
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

(function(){
const W = 512, H = 1024;          // the picture: tall, and wide enough to swing
const HEIGHT = 300;               // metres from the cloud to the masthead
const WIDTH = HEIGHT*W/H;         // the strip keeps the picture's proportions

/* The stroke as one would sketch it: from somewhere up in the cloud, down in
   wide slanting swings that each end in a little hook, and the last swing
   brought round onto the bottom centre, where the masthead is. */
function paint(){
  const cv = document.createElement('canvas');
  cv.width = W; cv.height = H;
  const g = cv.getContext('2d');
  const pts = [];
  let x = W*(0.45 + Math.random()*0.35), y = 0;
  pts.push([x, y]);
  let dir = Math.random() < 0.5 ? -1 : 1;
  while(y < H*0.84){
    // a long slanting swing across...
    const nx = Math.max(40, Math.min(W - 40, x + dir*(90 + Math.random()*170)));
    y += 30 + Math.random()*50;
    pts.push([nx, y]);
    // ...a short hook back and down...
    y += 25 + Math.random()*45;
    x = nx - dir*(15 + Math.random()*35);
    pts.push([x, y]);
    // ...a jolt the other way
    y += 10 + Math.random()*25;
    x += dir*(10 + Math.random()*20);
    pts.push([x, y]);
    dir = -dir;
  }
  pts.push([W/2 + (Math.random() - 0.5)*30, H*0.94]);
  pts.push([W/2, H]);

  const forks = [];
  for(let k=0;k<2;k++){
    const i = 1 + Math.floor(Math.random()*Math.max(1, pts.length - 4));
    const f = [pts[i].slice()];
    let fx = pts[i][0], fy = pts[i][1], fd = Math.random() < 0.5 ? -1 : 1;
    for(let j=0;j<3;j++){
      fx = Math.max(10, Math.min(W - 10, fx + fd*(30 + Math.random()*60)));
      fy += 25 + Math.random()*40;
      f.push([fx, fy]);
      fd = -fd;
    }
    forks.push(f);
  }

  const stroke = (line, width, style, blur) => {
    g.save();
    g.lineJoin = 'miter'; g.lineCap = 'round';
    g.strokeStyle = style; g.lineWidth = width;
    g.shadowColor = 'rgba(150,120,255,1)'; g.shadowBlur = blur;
    g.beginPath();
    g.moveTo(line[0][0], line[0][1]);
    for(let i=1;i<line.length;i++) g.lineTo(line[i][0], line[i][1]);
    g.stroke();
    g.restore();
  };
  // glow, then colour, then the white-hot core
  for(const f of forks){ stroke(f, 10, 'rgba(120,90,255,0.35)', 18); stroke(f, 3, 'rgba(200,190,255,0.9)', 6); }
  stroke(pts, 26, 'rgba(110,80,255,0.30)', 30);
  stroke(pts, 11, 'rgba(160,130,255,0.85)', 14);
  stroke(pts, 4.5, 'rgba(255,255,255,1)', 4);

  const tex = new THREE.CanvasTexture(cv);
  tex.colorSpace = THREE.SRGBColorSpace;
  return tex;
}

const _axis = new THREE.Vector3(), _view = new THREE.Vector3(), _side = new THREE.Vector3();

Naval.Lightning = class Lightning {
  constructor(scene, camera){
    this.group = new THREE.Group();
    scene.add(this.group);
    this.camera = camera;
    this.bolts = [];
  }

  /* One stroke from the cloud down to `top` (local metres). */
  bolt(top){
    const g = new THREE.BufferGeometry();
    g.setAttribute('position', new THREE.BufferAttribute(new Float32Array(12), 3));
    g.setAttribute('uv', new THREE.BufferAttribute(new Float32Array([0,1, 1,1, 0,0, 1,0]), 2));
    g.setIndex([0, 2, 1,  1, 2, 3]);
    const mat = new THREE.MeshBasicMaterial({
      map:paint(), transparent:true, opacity:1, side:THREE.DoubleSide,
      blending:THREE.AdditiveBlending, depthWrite:false, fog:false });
    const mesh = new THREE.Mesh(g, mat);
    mesh.frustumCulled = false;
    this.group.add(mesh);
    // a little off the vertical, as a stroke comes down out of a moving cloud
    const a = Math.random()*Math.PI*2, off = HEIGHT*0.12*Math.random();
    const b = {
      mesh, age:0,
      bottom: top.clone(),
      top: new THREE.Vector3(top.x + Math.cos(a)*off, top.y + HEIGHT, top.z + Math.sin(a)*off)
    };
    this.bolts.push(b);
    this._face(b);
  }

  /* Turn the strip about its own axis to face the eye. */
  _face(b){
    const p = b.mesh.geometry.attributes.position.array;
    _axis.subVectors(b.top, b.bottom).normalize();
    if(this.camera) _view.subVectors(this.camera.position, b.bottom);
    else _view.set(0, 0, 1);
    _side.crossVectors(_axis, _view);
    if(_side.lengthSq() < 1e-8) _side.set(1, 0, 0);
    _side.normalize().multiplyScalar(WIDTH/2);
    const T = b.top, B = b.bottom;
    p[0] = T.x - _side.x; p[1]  = T.y - _side.y; p[2]  = T.z - _side.z;
    p[3] = T.x + _side.x; p[4]  = T.y + _side.y; p[5]  = T.z + _side.z;
    p[6] = B.x - _side.x; p[7]  = B.y - _side.y; p[8]  = B.z - _side.z;
    p[9] = B.x + _side.x; p[10] = B.y + _side.y; p[11] = B.z + _side.z;
    b.mesh.geometry.attributes.position.needsUpdate = true;
  }

  _drop(b){
    this.group.remove(b.mesh);
    b.mesh.geometry.dispose();
    b.mesh.material.map.dispose();
    b.mesh.material.dispose();
  }

  /* `list` holds { entry, inten } for every vessel worth asking about;
     `onStrike(entry)` is called when one of them is hit. */
  update(dt, list, onStrike){
    for(let i=this.bolts.length-1;i>=0;i--){
      const b = this.bolts[i];
      b.age += dt;
      if(b.age > 0.25){          // gone as soon as it has struck: a stroke, not a lamp
        this._drop(b);
        this.bolts.splice(i, 1);
        continue;
      }
      // the sky's stroke shape: a first flash, a dip, a return stroke, gone
      const a = b.age;
      b.mesh.material.opacity = a < 0.06 ? 1 : a < 0.10 ? 0.25 : a < 0.16 ? 0.9
                              : Math.max(0, 0.5*(1 - (a - 0.16)/0.09));
      this._face(b);
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
    for(const b of this.bolts){
      b.top.x -= dx; b.top.z -= dz;
      b.bottom.x -= dx; b.bottom.z -= dz;
    }
  }
};
})();
