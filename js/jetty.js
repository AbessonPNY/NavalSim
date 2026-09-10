/* The jetty a port is built round.
 *
 * One of these stands at every island's port, and it is the only thing in this
 * world that is neither sea, weather nor ship — which makes it the first piece
 * of scenery, and worth being careful about.
 *
 * IT IS BUILT, NOT LOADED, until somebody supplies a model. That order matters:
 * a fallback written after the real asset is a fallback nobody ever looks at,
 * and this project has already learnt what that costs — a page published
 * without its .glb keeps its procedural hull rather than failing, and the same
 * rule applies here. Drop a `models/jetty.glb` into `ships/models` and it takes
 * over; take it away and the built one comes back, and neither the port, the
 * chart nor the mooring notices the difference.
 *
 * WHAT MAKES IT READ as a jetty rather than as a plank on the water is that it
 * stands on LEGS in real water of a real depth. The deck is level, because a
 * deck is level; the piles are not, because the bottom is not — each pair is
 * cut to the seabed it stands on, so the structure gets longer-legged as it
 * walks out into the stream, and that taper is the whole silhouette. A row of
 * equal posts reads as a fence.
 */
window.Naval = window.Naval || {};

Naval.Jetty = class Jetty {
  constructor(opts){
    const o = opts || {};
    this.glb = o.glb || 'ships/models/jetty.glb';
    this.width = o.width || 4.2;             // deck, across
    this.deckY = o.deckY || 1.7;             // above mean sea level
    this.model = null;                       // the supplied one, once it lands

    const tone = c => new THREE.MeshStandardMaterial({ color:c, roughness:0.88 });
    this.mats = {
      deck: tone(0x8a7a5e),                  // sun-bleached planking
      pile: tone(0x4f4334),                  // tarred, and wet at the foot
      bitt: tone(0x6b5a3f),
      mole: tone(0x8b8372)                   // pierre sèche, blanchie de sel
    };
  }

  /* The supplied model, if there is one. Awaited ONCE at start-up rather than
     per island, so that `make` can stay synchronous and the land can build a
     port the instant it decides to build the island under it. */
  async load(){
    if(this.model !== null) return this.model;
    try{
      const data = Naval.SHIP_DATA && Naval.SHIP_DATA.__jettyGlbBase64;
      const Loader = await Naval.loadGLTFLoader();
      const loader = new Loader();
      const gltf = data
        ? await loader.parseAsync(Naval.base64ToArrayBuffer(data), '')
        : await loader.loadAsync(this.glb);
      this.model = gltf.scene;
      this.model.traverse(o => { if(o.isMesh){ o.castShadow = true; o.receiveShadow = true; } });
    }catch(e){
      this.model = false;                    // false: asked and answered
      console.info('[ponton] aucun modèle fourni, le ponton est construit ici même');
    }
    return this.model;
  }

  /* THE MOLE, and it is the harbour — the jetty is only what one ties to.

     Built from the SAME four numbers heightAt reads: centre, radius, wall
     thickness and the half-angle of the entrance. That is the plan-of-forms
     rule applied to masonry — what stops the hull is what the eye sees stop
     her, and neither had to be written twice.

     Only where it actually stands ABOVE the ground it is built on. The ring
     runs on into the hillside behind the harbour, where a wall three metres
     high is simply buried; drawing it there would put a stone band up the side
     of a hill. So each segment asks the island what is under it and is dropped
     when the island is higher — which also gives the mole its roots on the
     beach, for nothing. */
  mole(world, isl){
    const H = isl.port && isl.port.harbour;
    if(!H) return null;
    const g = new THREE.Group();
    g.position.set(H.cx - isl.x, 0, H.cz - isl.z);

    const N = 132, R0 = H.r, R1 = H.r + H.wall;
    const pos = [], idx = [];
    let v = 0;
    for(let i=0;i<N;i++){
      const a0 = (i/N)*Math.PI*2, a1 = ((i+1)/N)*Math.PI*2;
      const mid = (a0 + a1)*0.5;
      let da = mid - H.ang;
      while(da >  Math.PI) da -= 2*Math.PI;
      while(da < -Math.PI) da += 2*Math.PI;
      if(Math.abs(da) < H.gap) continue;                 // la passe
      const t = Math.min(1, (Math.abs(da) - H.gap)/0.10);  // musoirs adoucis
      const top = H.top*t;

      // le terrain sous ce tronçon : enterré, on ne le dessine pas
      const cx = H.cx + Math.cos(mid)*(R0 + R1)*0.5;
      const cz = H.cz + Math.sin(mid)*(R0 + R1)*0.5;
      const ground = world._islandHeight(cx, cz);
      if(ground > top - 0.15) continue;

      /* Cinq points par tronçon : le pied extérieur, l'arête extérieure,
         l'arase, l'arête intérieure et le pied intérieur. Un môle est un tas
         de blocs, donc ses flancs fuient — c'est ce qui le distingue d'un mur,
         et c'est aussi ce qui le fait tenir. */
      const foot = Math.max(-9, ground) - 0.4;
      const prof = [[R1 + 7, foot], [R1, top], [R0, top], [R0 - 7, foot]];
      for(const ang of [a0, a1]){
        const c = Math.cos(ang), s2 = Math.sin(ang);
        for(const [r, y] of prof) pos.push(c*r, y, s2*r);
      }
      const b0 = v;
      for(let k=0;k<3;k++)
        idx.push(b0+k, b0+4+k, b0+4+k+1,  b0+k, b0+4+k+1, b0+k+1);
      v += 8;
    }
    if(!pos.length) return null;

    const geo = new THREE.BufferGeometry();
    geo.setAttribute('position', new THREE.Float32BufferAttribute(pos, 3));
    geo.setIndex(idx);
    geo.computeVertexNormals();
    const m = new THREE.Mesh(geo, this.mats.mole);
    m.receiveShadow = true;
    m.castShadow = true;
    g.add(m);
    return g;
  }

  /* A jetty for this island, in the island's OWN frame — the land mesh is what
     gets moved against the floating origin, so a jetty parented to it is
     recentred for nothing and can never drift off its own beach. */
  make(world, isl){
    const p = isl.port;
    if(!p) return null;
    const g = new THREE.Group();
    g.userData.port = p;

    const ca = Math.cos(p.ang), sa = Math.sin(p.ang);
    const r0 = p.shoreR - 6, r1 = p.shoreR + p.reach;
    // into the island's frame: the land mesh already sits at the island centre
    g.position.set(ca*r0, 0, sa*r0);
    g.rotation.y = -p.ang;                   // +x now runs out to sea

    if(this.model){
      /* A supplied model is placed and NOT rescaled. Guessing a scale from a
         bounding box is right for a ship, whose length is declared in her
         spec and can be checked against; nothing here declares how long a
         jetty is meant to be, so the only honest answer is the one the
         modeller drew. */
      const m = this.model.clone(true);
      g.add(m);
      return g;
    }

    const len = r1 - r0, W = this.width, hw = W*0.5;
    /* One pair of piles about every four metres, which is a bay a beam can
       span in timber and roughly what one sees. */
    const bays = Math.max(3, Math.round(len/4.0));

    // ---- the deck, one long box, level from end to end
    const deck = new THREE.Mesh(new THREE.BoxGeometry(len, 0.28, W), this.mats.deck);
    deck.position.set(len*0.5, this.deckY, 0);
    deck.castShadow = true; deck.receiveShadow = true;
    g.add(deck);

    /* Planking, and it runs ACROSS. A jetty is decked athwart its beams so the
       boards are short and a rotten one can be drawn and replaced; laid
       lengthways they would have to be as long as the pier. It is also the
       direction that makes it read at a glance, the lines running square to
       the way one walks. */
    const plank = new THREE.BoxGeometry(0.30, 0.06, W*0.98);
    for(let i=0;i<Math.floor(len/0.42);i++){
      const b = new THREE.Mesh(plank, this.mats.deck);
      b.position.set(0.30 + i*0.42, this.deckY + 0.17, 0);
      b.receiveShadow = true;
      g.add(b);
    }

    // ---- the legs, each cut to the bottom it stands on
    const pileG = new THREE.CylinderGeometry(0.19, 0.22, 1, 7);
    for(let i=0;i<=bays;i++){
      const along = (i/bays)*len;
      for(const side of [-1, 1]){
        const wx = isl.x + ca*(r0 + along) - sa*side*hw;
        const wz = isl.z + sa*(r0 + along) + ca*side*hw;
        /* The real bottom under this leg, from the same heightAt the hull
           grounds on. Nothing is placed by eye: walk the jetty out and the
           water deepens under it because the seabed says so. */
        const bed = Math.min(-0.4, world.heightAt(wx, wz));
        const h = this.deckY - bed + 0.2;
        const pile = new THREE.Mesh(pileG, this.mats.pile);
        pile.scale.y = h;
        pile.position.set(along, bed + h*0.5 - 0.1, side*hw);
        pile.castShadow = true;
        g.add(pile);

        // a brace from the pile head down the outside, which is what stiffens it
        if(i > 0 && h > 2.2){
          const br = new THREE.Mesh(pileG, this.mats.pile);
          br.scale.set(0.55, Math.hypot(4.0, 1.6), 0.55);
          br.position.set(along - 2.0, this.deckY - 0.9, side*(hw + 0.42));
          br.rotation.z = Math.atan2(4.0, 1.6);
          br.rotation.y = side*0.16;
          g.add(br);
        }
      }
    }

    /* Bitts at the head, and they are the point of the whole structure: a
       jetty exists so that something can be made fast to it. Two of them, on
       the seaward end, where a ship lying alongside would take her lines. */
    const bittG = new THREE.CylinderGeometry(0.17, 0.19, 1.0, 8);
    for(const side of [-1, 1]){
      const b = new THREE.Mesh(bittG, this.mats.bitt);
      b.position.set(len - 1.6, this.deckY + 0.65, side*(hw - 0.45));
      b.castShadow = true;
      g.add(b);
    }
    /* And one at the root, so a spring can be run back to the beach. A single
       pair at the head would let her lie there swinging on two lines. */
    const b0 = new THREE.Mesh(bittG, this.mats.bitt);
    b0.position.set(2.2, this.deckY + 0.65, hw - 0.45);
    b0.castShadow = true;
    g.add(b0);

    return g;
  }
};
