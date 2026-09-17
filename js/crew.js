/* The men on deck.

 * A handful of hands standing about, arms at their sides — not yet a crew that
 * works the ship, only people aboard her. Drawn by the code, a few hundred
 * triangles each, and all the men of one ship in ONE InstancedMesh: measured
 * (journal, « L'équipage »), thirty skinned figures cost four milliseconds a
 * frame and thirty instanced ones one, because the cost is per draw and per
 * pass, not per bone.
 *
 * So there is no skeleton. The figure is cut into parts (legs, body, arms,
 * head) by a vertex attribute, and the idle is written in the vertex shader:
 * the chest rising and falling, the weight going from one foot to the other
 * every several seconds, the head turning now and then to look at something.
 * Each man has his own phase and his own shirt, held per instance.
 *
 * They also keep their feet (setCrew in ship-model.js, per ship): the body
 * stays nearly upright while the deck heels under it, a beat late; the stance
 * widens and the knees give as the sea gets up; the arms come out from the
 * sides on a sharp roll. Three uniforms per ship, a few lines per vertex.
 *
 * The shadow pass draws the figure unmoved: the sway is a few centimetres,
 * no shadow shows it — but so is the lean, so on a hard heel the shadow
 * leans with the deck. Accepted for now.
 */
window.Naval = window.Naval || {};

/* Overridden by settings.json → crew. */
Naval.CREW = {
  enabled: true,
  count: 4,            // men on deck when a sheet does not say
  minLength: 12,       // metres: a boat shorter than this carries none by default
  farHide: 350,        // metres from the eye beyond which they are not drawn at all
  /* Keeping their feet. A seaman does not stand plumb against a heel: he
     takes most of it in his ankles and knees and lets the rest go. */
  upright: 0.68,       // share of the deck's tilt the body takes back (0 to 1)
  lag: 0.35,           // seconds the body trails the deck
  stanceFrom: 3,       // degrees of tilt (held peak) where the feet start to part
  stanceFull: 14,      // … and are fully apart, knees bent
  braceFrom: 6,        // degrees per second of roll/pitch where the arms come out
  braceFull: 18,
  glb: null            // a model to use instead (tools/sailor-glb.js, creatures/README.md)
};

// one clock for every figure; it is time, not the state of any ship
Naval.CREW_U = { uCrewT: { value: 0 } };

/* Late seventeenth-century seamen: slops and breeches of sailcloth or
   kersey, a shirt checked or plain, a knitted Monmouth cap, mostly red or
   brown. Muted, as dyed wool faded at sea. */
Naval.CREW_SHIRTS = [
  [0.78, 0.76, 0.70], [0.36, 0.44, 0.58], [0.62, 0.60, 0.54],
  [0.48, 0.30, 0.24], [0.70, 0.68, 0.60], [0.30, 0.34, 0.40]
];

Naval.crewFigure = function(){
  if(Naval._crewFig) return Naval._crewFig;
  const P = [], N = [], C = [], A = [], I = [];
  const SKIN = [0.62, 0.45, 0.34], SLOPS = [0.66, 0.62, 0.52],
        SHOE = [0.12, 0.10, 0.09], CAP = [0.46, 0.16, 0.13];
  // part: 0 legs, 1 body, 2 arms, 3 head
  const add = (g, col, part) => {
    const base = P.length/3, p = g.attributes.position, n = g.attributes.normal;
    for(let i=0;i<p.count;i++){
      P.push(p.getX(i), p.getY(i), p.getZ(i));
      N.push(n.getX(i), n.getY(i), n.getZ(i));
      C.push(col[0], col[1], col[2]);
      A.push(part);
    }
    const idx = g.index;
    if(idx) for(let i=0;i<idx.count;i++) I.push(base + idx.getX(i));
    else for(let i=0;i<p.count;i++) I.push(base + i);
    g.dispose();
  };
  const at = (g, x, y, z) => { g.translate(x, y, z); return g; };
  for(const s of [-1, 1]){
    add(at(new THREE.CylinderGeometry(0.075, 0.062, 0.78, 7), s*0.095, 0.47, 0), SLOPS, 0);
    add(at(new THREE.BoxGeometry(0.10, 0.07, 0.24), s*0.095, 0.035, 0.04), SHOE, 0);
    // the arm hangs a little away from the body, the hand at mid-thigh
    const arm = new THREE.CylinderGeometry(0.052, 0.042, 0.62, 6);
    arm.rotateZ(s*0.06);
    add(at(arm, s*0.235, 1.10, 0), SLOPS, 2);
    add(at(new THREE.SphereGeometry(0.048, 6, 4), s*0.255, 0.76, 0.01), SKIN, 2);
  }
  // wide breeches at the hips, then the shirt
  add(at(new THREE.CylinderGeometry(0.19, 0.17, 0.24, 8), 0, 0.90, 0), SLOPS, 0);
  const body = new THREE.CylinderGeometry(0.195, 0.18, 0.52, 8);
  body.scale(1, 1, 0.68);
  add(at(body, 0, 1.25, 0), [1, 1, 1], 1);
  add(at(new THREE.SphereGeometry(0.075, 6, 4), 0, 1.525, 0), SKIN, 3);     // neck and jaw
  add(at(new THREE.SphereGeometry(0.105, 9, 7), 0, 1.62, 0.005), SKIN, 3);
  const cap = new THREE.CylinderGeometry(0.07, 0.108, 0.13, 9);
  cap.rotateX(-0.25);
  add(at(cap, 0, 1.72, -0.02), CAP, 3);

  const g = new THREE.BufferGeometry();
  g.setAttribute('position', new THREE.Float32BufferAttribute(P, 3));
  g.setAttribute('normal', new THREE.Float32BufferAttribute(N, 3));
  g.setAttribute('color', new THREE.Float32BufferAttribute(C, 3));
  g.setAttribute('aPart', new THREE.Float32BufferAttribute(A, 1));
  g.setIndex(I);
  g.computeBoundingSphere();
  return (Naval._crewFig = g);
};

const CREW_VERT = `
  // arms out from the shoulder on a sharp roll
  if(aPart > 1.5 && aPart < 2.5){
    vec2 d = transformed.xy - vec2(sign(position.x)*0.235, 1.41);
    float a = sign(position.x)*uCrewBrace*(0.55 + 0.25*aPhase);
    float ca = cos(a), sa = sin(a);
    transformed.xy = vec2(sign(position.x)*0.235, 1.41) + vec2(ca*d.x - sa*d.y, sa*d.x + ca*d.y);
  }
  // feet apart and knees given as the sea gets up
  float leg = 1.0 - smoothstep(0.0, 0.86, position.y);
  transformed.x += sign(position.x)*step(aPart, 0.5)*0.11*uCrewStance*leg;
  transformed.z += 0.07*uCrewStance*sin(3.1416*clamp(position.y/0.9, 0.0, 1.0))*step(aPart, 0.5);
  transformed.y -= 0.06*uCrewStance*smoothstep(0.15, 0.6, position.y);
  // breathing, about four seconds a breath
  float cB = sin(uCrewT*1.5 + aPhase*6.28);
  float chest = smoothstep(0.95, 1.40, position.y);
  if(aPart > 0.5){
    transformed.xz *= 1.0 + vec2(0.012, 0.035)*cB*chest*step(aPart, 1.5);
    transformed.y += 0.006*cB*smoothstep(1.0, 1.45, position.y);
  }
  // now and then he looks round
  if(aPart > 2.5 && aPart < 3.5){
    float cH = sin(uCrewT*0.41 + aPhase*17.0);
    float yaw = 0.55*sign(cH)*smoothstep(0.55, 0.95, abs(cH));
    float cy = cos(yaw), sy = sin(yaw);
    transformed.xz = vec2(cy*transformed.x + sy*transformed.z, -sy*transformed.x + cy*transformed.z);
  }
  // the weight goes from one foot to the other, slowly; the hip moves out
  // and the shoulders lean back over it
  float cW = sin(uCrewT*0.62 + aPhase*11.0);
  cW = sign(cW)*pow(abs(cW), 0.6);
  transformed.x += 0.028*cW*smoothstep(0.1, 0.95, position.y)
                 - 0.018*cW*smoothstep(0.95, 1.6, position.y);
  // upright against the heel, turning about the feet (the lighting keeps the
  // unturned normal: it is computed before this point) — the ship's up, brought
  // into this man's own frame (his yaw is in the instance matrix)
  vec3 cUp = normalize(transpose(mat3(instanceMatrix)) * uCrewUp);
  vec3 cL = normalize(mix(vec3(0.0, 1.0, 0.0), cUp, 0.85 + 0.3*aPhase));
  vec3 cK = vec3(cL.z, 0.0, -cL.x);            // cross(up, lean)
  float cS = length(cK);
  if(cS > 1e-4){
    cK /= cS;
    float cC = cL.y;
    transformed = transformed*cC + cross(cK, transformed)*cS + cK*dot(cK, transformed)*(1.0 - cC);
  }
`;

Naval.crewMaterial = function(u, proto){
  // a model's own material, cloned per ship; the drawn one otherwise
  const m = proto ? proto.clone()
                  : new THREE.MeshStandardMaterial({ vertexColors:true, roughness:0.88, metalness:0 });
  m.vertexColors = true;
  m.userData.crew = true;
  m.onBeforeCompile = shader => {
    shader.uniforms.uCrewT = Naval.CREW_U.uCrewT;
    shader.uniforms.uCrewUp = u.uCrewUp;
    shader.uniforms.uCrewStance = u.uCrewStance;
    shader.uniforms.uCrewBrace = u.uCrewBrace;
    shader.vertexShader = 'uniform float uCrewT;\nuniform vec3 uCrewUp;\nuniform float uCrewStance;\nuniform float uCrewBrace;\nattribute float aPart;\nattribute float aPhase;\nattribute vec3 aShirt;\n'
      + shader.vertexShader
        .replace('#include <begin_vertex>', '#include <begin_vertex>\n' + CREW_VERT)
        .replace('#include <color_vertex>', '#include <color_vertex>\n  if(aPart > 0.5 && aPart < 1.5) vColor.rgb = aShirt;');
  };
  m.customProgramCacheKey = () => 'crew-idle';
  return m;
};

/* The men of one ship. `spots` are {x, y, z, yaw} in her own frame. */
Naval.crewMesh = function(spots, seed){
  const base = Naval.crewFigure();
  // own geometry object, shared buffers: the per-man attributes are this ship's
  const g = new THREE.BufferGeometry();
  for(const k in base.attributes) g.setAttribute(k, base.attributes[k]);
  g.setIndex(base.index);
  for(const gr of base.groups) g.addGroup(gr.start, gr.count, gr.materialIndex);
  g.boundingSphere = base.boundingSphere;
  const n = spots.length, rnd = Naval.crewRandom(seed + 7);
  const shirt = new Float32Array(n*3), phase = new Float32Array(n);
  // this ship's own: how the deck under these men is moving
  const u = { uCrewUp:{ value:new THREE.Vector3(0, 1, 0) }, uCrewStance:{ value:0 }, uCrewBrace:{ value:0 } };
  const protos = Naval._crewMats;
  const mat = protos ? protos.map(p => Naval.crewMaterial(u, p)) : Naval.crewMaterial(u);
  const mesh = new THREE.InstancedMesh(g, protos && protos.length === 1 ? mat[0] : mat, n);
  mesh.userData.u = u;
  mesh.userData.peak = 0;
  const M = new THREE.Matrix4(), Q = new THREE.Quaternion(), Y = new THREE.Vector3(0, 1, 0),
        V = new THREE.Vector3(), S = new THREE.Vector3();
  const sh = Naval.CREW_SHIRTS;
  for(let i=0;i<n;i++){
    const s = spots[i], k = 0.93 + rnd()*0.12;      // not all of a height
    M.compose(V.set(s.x, s.y, s.z), Q.setFromAxisAngle(Y, s.yaw), S.set(k, k, k));
    mesh.setMatrixAt(i, M);
    const c = sh[Math.floor(rnd()*sh.length)];
    shirt.set(c, i*3);
    phase[i] = rnd();
  }
  g.setAttribute('aShirt', new THREE.InstancedBufferAttribute(shirt, 3));
  g.setAttribute('aPhase', new THREE.InstancedBufferAttribute(phase, 1));
  mesh.userData.crew = true;
  mesh.castShadow = true;
  mesh.receiveShadow = true;
  return mesh;
};

/* THE MODEL, if settings name one. Its meshes are merged into one geometry,
   one group per material, with each vertex told its part by the NAME of the
   mesh it came from. The ships already at sea are given the new figure. */
Naval.CREW_PARTS = [
  [/^(jambe|leg|culotte|breech|chaussure|shoe|pied|foot|feet)/i, 0],
  [/^(corps|body|torse|torso|chemise|shirt)/i, 1],
  [/^(bras|arm|main|hand)/i, 2],
  [/^(tete|tête|head|bonnet|cap|hat|chapeau|cou|neck)/i, 3]
];
Naval.crewShips = new Set();

Naval.loadCrewModel = async function(cfg){
  if(!cfg || (!cfg.glb && !cfg.glbBase64)) return false;
  try{
    const loader = new (await Naval.loadGLTFLoader())();
    const gltf = cfg.glbBase64
      ? await new Promise((ok, no) => loader.parse(Naval.base64ToArrayBuffer(cfg.glbBase64), '', ok, no))
      : await loader.loadAsync(cfg.glb);
    const root = gltf.scene;
    root.updateMatrixWorld(true);
    const byMat = new Map(), counts = [0, 0, 0, 0, 0];
    root.traverse(o => {
      if(!o.isMesh || !o.geometry) return;
      let part = 4;                              // moves with him, no part of its own
      for(let p = o; p && p !== root && part === 4; p = p.parent)
        for(const [re, k] of Naval.CREW_PARTS) if(re.test(p.name || '')){ part = k; break; }
      const src = o.geometry.index ? o.geometry.toNonIndexed() : o.geometry.clone();
      src.applyMatrix4(o.matrixWorld);
      const mats = [].concat(o.material);
      const groups = src.groups.length ? src.groups : [{ start:0, count:src.attributes.position.count, materialIndex:0 }];
      for(const gr of groups){
        const m = mats[gr.materialIndex || 0] || mats[0];
        if(!byMat.has(m)) byMat.set(m, { P:[], N:[], C:[], UV:[], A:[] });
        const b = byMat.get(m), a = src.attributes;
        for(let i = gr.start; i < gr.start + gr.count; i++){
          b.P.push(a.position.getX(i), a.position.getY(i), a.position.getZ(i));
          if(a.normal) b.N.push(a.normal.getX(i), a.normal.getY(i), a.normal.getZ(i)); else b.N.push(0, 1, 0);
          if(a.color) b.C.push(a.color.getX(i), a.color.getY(i), a.color.getZ(i)); else b.C.push(1, 1, 1);
          if(a.uv) b.UV.push(a.uv.getX(i), a.uv.getY(i)); else b.UV.push(0, 0);
          b.A.push(part);
        }
        counts[part] += gr.count/3;
      }
      src.dispose();
    });
    if(!byMat.size) throw new Error('aucun maillage');
    const P = [], N = [], C = [], UV = [], A = [], protos = [];
    const g = new THREE.BufferGeometry();
    for(const [m, b] of byMat){
      g.addGroup(P.length/3, b.P.length/3, protos.length);
      protos.push(m);
      // element by element: a spread of a detailed model overflows the call stack
      for(const [to, from] of [[P, b.P], [N, b.N], [C, b.C], [UV, b.UV], [A, b.A]])
        for(let i = 0; i < from.length; i++) to.push(from[i]);
    }
    g.setAttribute('position', new THREE.Float32BufferAttribute(P, 3));
    g.setAttribute('normal', new THREE.Float32BufferAttribute(N, 3));
    g.setAttribute('color', new THREE.Float32BufferAttribute(C, 3));
    g.setAttribute('uv', new THREE.Float32BufferAttribute(UV, 2));
    g.setAttribute('aPart', new THREE.Float32BufferAttribute(A, 1));
    g.computeBoundingSphere();
    Naval._crewFig = g;
    Naval._crewMats = protos;
    for(const ship of Naval.crewShips){
      if(!ship.group.parent){ Naval.crewShips.delete(ship); continue; }
      ship._buildCrew();
    }
    console.log('[équipage] modèle chargé : jambes ' + counts[0] + ', corps ' + counts[1] + ', bras ' + counts[2]
                + ', tête ' + counts[3] + ', autres ' + counts[4] + ' triangles, ' + protos.length + ' matériau(x)');
    return true;
  }catch(err){
    console.warn('[équipage] ' + (cfg.glb || 'modèle embarqué') + ' illisible — silhouette dessinée. '
                 + (err && err.message || err));
    return false;
  }
};

Naval.crewRandom = function(seed){
  let s = (seed >>> 0) || 1;
  return () => { s = (s + 0x6D2B79F5) >>> 0; let t = s;
    t = Math.imul(t ^ t >>> 15, t | 1); t ^= t + Math.imul(t ^ t >>> 7, t | 61);
    return ((t ^ t >>> 14) >>> 0)/4294967296; };
};

Naval.crewSeed = function(str){
  let h = 2166136261;
  for(const c of String(str || '')) h = Math.imul(h ^ c.charCodeAt(0), 16777619);
  return h >>> 0;
};
