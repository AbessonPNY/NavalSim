/* The ghost fleet.
 *
 * In one place on the chart — true metres, like the islands — and only at
 * night, a vessel that lingers is shown a battle from another century: four
 * pale ships, two against two, fighting it out as they did. They are real
 * hulls in the fleet, sailed and fought by the same helm and the same guns as
 * any consort, so nothing about how a broadside lands had to be written again;
 * what makes them ghosts is decided around them:
 *
 *   · they are PALE — every material of the model, tinted, lit from within,
 *     half transparent, and faded in and out;
 *   · they are INTANGIBLE to anything that is not a ghost — the page asks
 *     canTouch() before a ball or a collision counts — except the vessel that
 *     comes too close: then one of them turns on her, and its shot is real;
 *   · they END, at the first of three things: daybreak, one side sunk (the
 *     beaten go down, the victors dissolve), or the watcher sailing away.
 *
 * This file keeps the scene; the page lends it the fleet (launch, scuttle),
 * the words and the clock.
 */
window.Naval = window.Naval || {};

/* Game rules, overridden by settings.json → ghosts. */
Naval.GHOSTS = {
  enabled: true,
  name: 'Le Cimetière des Galions',
  x: -6000, z: -14000,     // true metres: open water off the south coast, 394 m deep, 11.6 km from any shore
  radius: 2500,            // the place, as the chart draws it
  lingerBefore: 90,        // seconds of night spent inside it before they come
  aggroRange: 350,         // closer than this to one of them, and it turns on you
  sideA: ['frigate17e', 'frigate17e'],
  sideB: ['pirate', 'pirate'],
  spread: 350,             // metres each line starts from the middle
  fade: 6,                 // seconds to come and to go
  opacity: 0.45,
  victorLinger: 10,        // seconds the winners stay before they dissolve
  sunkLinger: 25           // and the beaten, going down, before they are gone
};

/* TORN CANVAS, drawn rather than loaded: a transparency map where white is
   cloth and black is gone — a ragged foot hanging in tongues, bites out of
   both leeches, and holes with split edges. The sail's texture rows run from
   the head (top of the picture) to the foot (bottom), so the rags are at the
   bottom. A few variants, so a line of ghosts does not wear the same wounds. */
Naval.tornCanvas = function(){
  if(Naval._torn) return Naval._torn[Math.floor(Math.random()*Naval._torn.length)];
  Naval._torn = [];
  for(let v=0; v<3; v++){
    const S = 256, cv = document.createElement('canvas');
    cv.width = cv.height = S;
    const g = cv.getContext('2d');
    g.fillStyle = '#fff'; g.fillRect(0, 0, S, S);
    g.fillStyle = '#000';
    // the foot: tongues of cloth hanging down between deep bites
    g.beginPath();
    g.moveTo(0, S);
    let x = 0;
    while(x < S){
      const w = 10 + Math.random()*28;
      const up = S*(0.12 + Math.random()*0.38);
      g.lineTo(x + w*0.3, S - up*(0.4 + Math.random()*0.6));
      g.lineTo(x + w*0.55, S - up);
      g.lineTo(x + w*0.8, S - up*(0.3 + Math.random()*0.5));
      g.lineTo(x + w, S - Math.random()*S*0.05);
      x += w;
    }
    g.lineTo(S, S);
    g.closePath(); g.fill();
    // bites out of both leeches
    for(const side of [0, 1]){
      for(let k=0;k<4;k++){
        const y = S*(0.15 + Math.random()*0.6), h = 12 + Math.random()*30, d = 6 + Math.random()*26;
        const ex = side ? S : 0, ix = side ? S - d : d;
        g.beginPath();
        g.moveTo(ex, y); g.lineTo(ix, y + h*0.3); g.lineTo(ex + (ix - ex)*0.4, y + h*0.6); g.lineTo(ix*0.9 + ex*0.1, y + h*0.8); g.lineTo(ex, y + h);
        g.closePath(); g.fill();
      }
    }
    // holes, star-split
    for(let k=0;k<9;k++){
      const cx = S*(0.12 + Math.random()*0.76), cy = S*(0.1 + Math.random()*0.55);
      const r = 5 + Math.random()*16, n = 7 + Math.floor(Math.random()*5);
      g.beginPath();
      for(let i=0;i<n;i++){
        const a = i/n*Math.PI*2, rr = r*(i % 2 ? 0.35 + Math.random()*0.3 : 0.8 + Math.random()*0.7);
        const px = cx + Math.cos(a)*rr, py = cy + Math.sin(a)*rr*1.3;
        i ? g.lineTo(px, py) : g.moveTo(px, py);
      }
      g.closePath(); g.fill();
    }
    const tex = new THREE.CanvasTexture(cv);
    Naval._torn.push(tex);
  }
  return Naval._torn[Math.floor(Math.random()*Naval._torn.length)];
};

/* THE LOWER HULL DISSOLVES. A patch on each of her materials, chained after
   everything else (the haze included, which it follows at the very end of the
   fragment), multiplies the alpha by a ramp on WORLD height: nothing at the
   waterline, whole by the rail. She glides at a fixed height, so it is her
   bottom that is gone; if she sinks, she goes out from below as she goes. */
Naval.applyGhostFade = function(mat, u){
  if(!mat || mat.userData.ghostFade || !mat.isMeshStandardMaterial) return;
  mat.userData.ghostFade = true;
  const prevKey = mat.customProgramCacheKey.bind(mat);
  mat.customProgramCacheKey = () => prevKey() + '|ghost-fade';
  const prev = mat.onBeforeCompile;
  mat.onBeforeCompile = (shader, renderer)=>{
    if(prev) prev(shader, renderer);
    shader.uniforms.uGhostLo = u.uGhostLo;
    shader.uniforms.uGhostHi = u.uGhostHi;
    shader.vertexShader = 'varying float vGhostY;\n' + shader.vertexShader.replace(
      '#include <project_vertex>',
      '#include <project_vertex>\n  vGhostY = (modelMatrix * vec4(transformed, 1.0)).y;');
    shader.fragmentShader = 'varying float vGhostY;\nuniform float uGhostLo, uGhostHi;\n'
      + shader.fragmentShader.replace(/}\s*$/,
        '\n  gl_FragColor.a *= smoothstep(uGhostLo, uGhostHi, vGhostY);\n}');
  };
  mat.needsUpdate = true;
};

Naval.Ghosts = class Ghosts {
  constructor(){
    this.state = 'idle';     // idle · rising · battle · ending
    this.linger = 0;
    this.entries = [];
    this.spent = false;      // one battle a night
    this.warned = false;
    this.aggroSaid = false;
  }

  get active(){ return this.state !== 'idle'; }

  /* Is this a place the chart should name? */
  place(){
    const G = Naval.GHOSTS;
    return G.enabled ? { x:G.x, z:G.z, r:G.radius, name:G.name } : null;
  }

  /* May a ball from `from` (a fleet entry or null), or a hull, touch `to`?
     Ghosts touch ghosts. A ghost touches a living ship only when it is the one
     that ghost has turned on. The living never touch a ghost. */
  canTouch(from, to){
    const fg = !!(from && from.ghost), tg = !!(to && to.ghost);
    if(fg && tg) return true;
    if(tg) return false;
    if(fg) return !!(from.provoquePar && to && from.provoquePar === to.body);
    return true;
  }

  /* c, from the page each frame:
       player         the entry at the helm
       playerTrue     {x, z} her true position
       lit            it is night, by the lanterns' own rule
       origin         ocean.origin
       room()         how many hulls the fleet can still take
       launch(id)     → Promise(entry)   put a hull in the water
       scuttle(entry) take one out
       nations()      [flags.json entries], national ones
       say(msg), t */
  update(dt, c){
    const G = Naval.GHOSTS;
    if(!G.enabled) return;
    if(!c.lit){ this.spent = false; }
    const d = Math.hypot(c.playerTrue.x - G.x, c.playerTrue.z - G.z);

    if(this.state === 'idle'){
      const inside = d < G.radius && c.lit && !this.spent
                  && c.player.physics && !c.player.physics.foundered;
      this.linger = inside ? this.linger + dt : 0;
      if(inside && !this.warned && this.linger > G.lingerBefore*0.5){
        this.warned = true;
        c.say('Une brume froide monte sur ' + G.name + '…');
      }
      if(this.linger > G.lingerBefore) this.rise(c);
      return;
    }
    if(this.state === 'rising') return;

    // --- the battle, and how it ends ---
    const alive = this.entries.filter(e => e.ghost && !e.physics.foundered);
    const sideAlive = [0, 1].map(s => alive.some(e => e.ghost.side === s));
    if(this.state === 'battle'){
      if(!c.lit) this.end('dawn', c);
      else if(d > G.radius*1.3) this.end('left', c);
      else if(!sideAlive[0] || !sideAlive[1]) this.end('victory', c);
    }

    // targets: the nearest enemy ghost — or the watcher, when she is too close
    const pb = c.player.body;
    for(const e of alive){
      let foe = null, best = Infinity;
      if(this.state === 'battle' && pb && !c.player.physics.foundered
         && e.body.pos.distanceTo(pb.pos) < G.aggroRange){
        foe = pb;
        if(!this.aggroSaid){ this.aggroSaid = true; c.say('Les spectres vous ont vu !'); }
      }else{
        for(const o of alive){
          if(o.ghost.side === e.ghost.side) continue;
          const dd = e.body.pos.distanceTo(o.body.pos);
          if(dd < best){ best = dd; foe = o.body; }
        }
      }
      e.provoquePar = this.state === 'battle' ? foe : null;
    }

    // --- fading ---
    for(const e of [...this.entries]){
      const g = e.ghost;
      if(this.state === 'ending' && !g.fadeOnSink){
        g.wait -= dt;
        if(g.wait <= 0) g.want = 0;
      }
      if(e.physics.foundered && g.sunkAt == null && !g.fadeOnSink){
        g.sunkAt = c.t;
        g.wait = G.sunkLinger; g.fadeOnSink = true;
      }
      if(g.fadeOnSink){ g.wait -= dt; if(g.wait <= 0) g.want = 0; }
      const step = dt/Math.max(0.1, G.fade);
      g.fade += Math.max(-step, Math.min(step, g.want - g.fade));
      this._show(e);
      if(g.want === 0 && g.fade <= 0){
        this.entries.splice(this.entries.indexOf(e), 1);
        c.scuttle(e);
      }
    }
    if(this.state === 'ending' && !this.entries.length){
      this.state = 'idle';
      this.linger = 0;
      this.warned = false;
    }
  }

  /* Four hulls, two lines, facing each other across the middle of the place,
     laid broadside-on to the watcher so she sees the whole of it. */
  async rise(c){
    const G = Naval.GHOSTS;
    this.state = 'rising';
    this.spent = true;
    this.aggroSaid = false;
    const want = [...G.sideA.map(id => [id, 0]), ...G.sideB.map(id => [id, 1])];
    const n = Math.min(want.length, c.room());
    if(n < 2){ this.state = 'idle'; return; }
    const nations = c.nations();
    const pick = () => nations.splice(Math.floor(Math.random()*nations.length), 1)[0] || null;
    const colours = [pick(), pick()];

    // the axis the two lines lie along: across the watcher's line of sight
    const cx = G.x - c.origin.x, cz = G.z - c.origin.z;
    let ax = c.playerTrue.x - G.x, az = c.playerTrue.z - G.z;
    const al = Math.hypot(ax, az) || 1;
    [ax, az] = [-az/al, ax/al];

    // taken from the chosen list so each side keeps at least one
    const order = n >= want.length ? want
      : want.filter((w, i) => i % 2 === 0).concat(want.filter((w, i) => i % 2 === 1)).slice(0, n);
    const perSide = [0, 0];
    for(const [id, side] of order){
      let e = null;
      try{ e = await c.launch(id); }catch(err){ e = null; }
      if(!e) continue;
      const k = perSide[side]++;
      const sgn = side === 0 ? -1 : 1;
      const lat = (k - 0.5)*160;                     // abreast, a cable apart
      const px = cx + ax*sgn*G.spread + az*lat, pz = cz + az*sgn*G.spread - ax*lat;
      e.body.pos.set(px, e.body.pos.y, pz);
      // heading at the other line
      e.body.quat.setFromAxisAngle(new THREE.Vector3(0, 1, 0), Math.atan2(-ax*sgn, -az*sgn));
      e.body.vel.set(0, 0, 0); e.body.angVel.set(0, 0, 0);
      e.ship.syncTo(e.body);
      // no pirate's greed and no sheet's colours: a ghost fights under its side's flag
      e.hostile = false; e.pirate = null; e.rencontre = false;
      if(e.helm) e.helm.standoff = Math.max(e.helm.standoff, 185);
      const flag = colours[side];
      if(flag){ e.ship.setEnsignMap(flag.image, flag); e.ship.showColours(true); }
      e.pavillon = flag;
      e.ghost = { side, fade:0, want:1, wait:0, mats:this._ghostify(e.ship), sunkAt:null };
      // it glides: held at its waterline, a little above, and the swell goes through it
      e.physics.glide = (e.eqY || 0) + 0.25;
      e.physics.body.pos.y = e.physics.glide;
      this._show(e);
      this.entries.push(e);
    }
    if(this.entries.length < 2){
      for(const e of this.entries) c.scuttle(e);
      this.entries = [];
      this.state = 'idle';
      return;
    }
    this.state = 'battle';
    c.say('Des voiles pâles sortent de la brume : une bataille d’un autre siècle !');
  }

  end(why, c){
    const G = Naval.GHOSTS;
    this.state = 'ending';
    for(const e of this.entries){
      const g = e.ghost;
      if(g.fadeOnSink) continue;                       // already going down its own way
      // the beaten go on sinking for their while; the rest go after theirs
      if(e.physics.foundered){ g.fadeOnSink = true; g.wait = G.sunkLinger; continue; }
      g.wait = why === 'victory' ? G.victorLinger : 0;
    }
    c.say(why === 'dawn' ? 'Au premier jour, les navires fantômes s’effacent.'
        : why === 'left' ? 'Derrière vous, la bataille s’efface dans la nuit.'
        :                  'Un camp sombre… et le vainqueur se dissout dans la brume.');
  }

  /* Every material she wears, made pale: tinted toward a cold green, lit
     faintly from within — a ghost gives its own light, it does not take the
     moon's — and half transparent. Her materials are her own (each hull is
     built afresh), so they are changed in place. */
  _ghostify(ship){
    const mats = [];
    const seen = new Set();
    /* Her canvas in rags: every sail material gets a torn transparency map,
       cut clean by alphaTest so the holes are holes and not a haze. */
    const cloth = new Set();
    for(const c of ship.canvases || []) for(const m of [].concat(c.material)) if(m) cloth.add(m);
    for(const m of cloth){
      m.alphaMap = Naval.tornCanvas();
      m.alphaTest = 0.45;
      m.side = THREE.DoubleSide;
    }
    ship.group.traverse(o => {
      if(!(o.isMesh || o.isSprite)) return;
      for(const m of [].concat(o.material)){
        if(!m || seen.has(m)) continue;
        seen.add(m);
        if(o.isSprite){
          // her lanterns burn green
          m.color && m.color.set(0x8dffd8);
          mats.push({ m, base: 1, sprite:true });
          continue;
        }
        if(m.color) m.color.lerp(new THREE.Color(0xbfeee2), 0.7);
        // lit from within, bright enough for the night's bloom to take hold of it
        if(m.emissive){ m.emissive.set(0x5fe6c4); m.emissiveIntensity = 0.95; }
        m.transparent = true;
        m.needsUpdate = true;
        mats.push({ m, base: Naval.GHOSTS.opacity, cloth: cloth.has(m) });
      }
    });
    for(const L of ship.lanternList || []) L.light.color.set(0x7dffc8);

    /* Her bottom gone: invisible to half a metre above the sea, whole at a
       seventh of her length above it — about the rail of a galleon. */
    const fadeU = { uGhostLo:{ value:0.5 }, uGhostHi:{ value:0.5 + 0.15*ship.spec.L } };
    for(const it of mats) if(!it.sprite) Naval.applyGhostFade(it.m, fadeU);

    /* THE MIST she rides in: soft puffs of the same pale green at the
       waterline, along and around her, each drifting and breathing on its own.
       The sea hides their lower halves, which is what sets them ON the water. */
    const B = ship.spec.B;
    for(let k=0;k<11;k++){
      const sp = new THREE.Sprite(new THREE.SpriteMaterial({
        map:Naval.smokeTexture(), color:0x6fe8c8, transparent:true, opacity:0,
        blending:THREE.AdditiveBlending, depthWrite:false, fog:false }));
      const along = (k/10 - 0.5)*1.25*ship.spec.L;
      const side = (k % 2 ? 1 : -1)*(B*0.35 + Math.random()*B*0.9);
      sp.position.set(side, 0.8 + Math.random()*1.8, along);
      sp.scale.setScalar(ship.spec.L*(0.35 + Math.random()*0.3));
      sp.material.rotation = Math.random()*Math.PI*2;
      ship.group.add(sp);
      mats.push({ m:sp.material, base:0.22, mist:true, sp,
                  home:sp.position.clone(), seed:Math.random()*100, spin:(Math.random() - 0.5)*0.1 });
    }

    /* AN AURA: three soft green glows along her length, from the lantern's
       own drawn texture, additive. Sprites and no lamp — the light count does
       not change. */
    const L = ship.spec.L;
    for(const zf of [-0.3, 0, 0.3]){
      const sp = new THREE.Sprite(new THREE.SpriteMaterial({
        map:Naval.glowTexture(), color:0x6fffd2, transparent:true, opacity:0,
        blending:THREE.AdditiveBlending, depthWrite:false, fog:false }));
      sp.scale.setScalar(L*0.95);
      sp.position.set(0, L*0.22, zf*L);
      ship.group.add(sp);
      mats.push({ m:sp.material, base:0.2, aura:true });
    }
    return mats;
  }

  _show(e){
    const g = e.ghost;
    for(const it of g.mats){
      if(it.sprite) continue;               // the lantern sets its own each frame
      // the aura flickers a little, as a ghost light does
      if(it.aura){ it.m.opacity = it.base*g.fade*(0.8 + 0.2*Math.sin(performance.now()*0.0021 + g.side*2)); continue; }
      if(it.mist){
        const tm = performance.now()*0.001 + it.seed;
        it.sp.position.set(it.home.x + Math.sin(tm*0.23)*2.5, it.home.y + Math.sin(tm*0.31)*0.4,
                           it.home.z + Math.cos(tm*0.17)*3);
        it.m.rotation += it.spin*0.016;
        it.m.opacity = it.base*g.fade*(0.7 + 0.3*Math.sin(tm*0.5));
        continue;
      }
      it.m.opacity = it.base*g.fade;
      /* The test is made against opacity × map, so a fixed threshold took the
         whole sail away while she faded — the canvas came last and went first.
         Held at half her own opacity, only the holes are cut. */
      if(it.cloth) it.m.alphaTest = Math.max(1e-3, 0.5*it.m.opacity);
    }
    e.ship.group.visible = true;
  }
};
