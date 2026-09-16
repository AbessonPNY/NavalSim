/* La lueur : ce qui déborde d'une lumière trop vive pour l'image.
 *
 * CE N'EST PAS UN EFFET, C'EST UN ÉCRÊTAGE RENDU LISIBLE. Une fenêtre éclairée
 * de l'intérieur, une bouche à feu, un fanal : tous sortent au-delà du blanc que
 * l'écran sait montrer, et la mesure l'a dit crûment — les deux tiers des pixels
 * d'une vitre allumée saturaient déjà, si bien que multiplier l'émissive par
 * cinq n'ajoutait plus rien. Ce qui manque à une lumière écrêtée n'est pas de
 * l'intensité, c'est la façon dont elle DÉBORDE : l'œil et l'objectif étalent ce
 * trop-plein autour de la source, et c'est ce halo qui se lit comme « ça brille »
 * plutôt que « c'est blanc ».
 *
 * Le chemin est celui du verre de la lunette, et pour la même raison : ce qui
 * s'applique à l'image finie ne peut pas vivre dans la scène. L'écran est copié
 * dans une texture, ce qui dépasse le seuil est gardé, flouté à quart de
 * résolution, puis RAJOUTÉ par-dessus.
 *
 * LE JOUR, ELLE N'EXISTE PAS. C'était la première inquiétude, et c'est la
 * réponse : la passe est sautée dès que la nuit est finie — pas de copie, pas de
 * cible, pas un octet de bande passante. Le prix ne se paie qu'aux heures où il
 * y a quelque chose à faire briller.
 */
window.Naval = window.Naval || {};

Naval.Bloom = class Bloom {
  constructor(stage){
    this.stage = stage;
    this.enabled = true;
    this.strength = 0.9;        // combien de trop-plein on rend
    this.threshold = 0.72;      // au-dessus de quelle luminance ça déborde
    this.knee = 0.12;           // la bascule est douce, sans quoi le halo a un bord
    this.radius = 1.7;          // largeur du flou, en pixels de la passe
    this.scale = 0.25;          // résolution de la passe, en fraction de l'écran
    this.nightOnly = true;      // rien du tout tant qu'il fait jour

    this._size = new THREE.Vector2();
    this._zero = new THREE.Vector2();
    this.tex = null; this.rtA = null; this.rtB = null;

    const quad = new THREE.PlaneGeometry(2, 2);
    this.ortho = new THREE.OrthographicCamera(-1, 1, 1, -1, 0, 1);
    this.mesh = new THREE.Mesh(quad, null);
    this.mesh.frustumCulled = false;
    this.scene = new THREE.Scene();
    this.scene.add(this.mesh);

    const VERT = `
      varying vec2 vUv;
      void main(){ vUv = uv; gl_Position = vec4(position.xy, 0.0, 1.0); }`;

    /* Le seuil, avec un GENOU : une coupure franche dessine le contour du seuil
       dans le halo, ce qui se lit comme un défaut de compression et non comme
       une lumière. La luminance est celle de l'œil, pas la moyenne des canaux. */
    this.bright = new THREE.ShaderMaterial({
      uniforms:{ uScene:{value:null}, uThresh:{value:0.72}, uKnee:{value:0.12} },
      vertexShader:VERT,
      fragmentShader:`
        uniform sampler2D uScene; uniform float uThresh, uKnee;
        varying vec2 vUv;
        void main(){
          vec3 c = texture2D(uScene, vUv).rgb;
          float l = dot(c, vec3(0.2126, 0.7152, 0.0722));
          float w = smoothstep(uThresh - uKnee, uThresh + uKnee, l);
          gl_FragColor = vec4(c*w, 1.0);
        }`,
      depthTest:false, depthWrite:false });

    // un flou séparable : cinq prises par axe, deux passes, et l'écart double
    this.blur = new THREE.ShaderMaterial({
      uniforms:{ uSrc:{value:null}, uStep:{value:new THREE.Vector2()} },
      vertexShader:VERT,
      fragmentShader:`
        uniform sampler2D uSrc; uniform vec2 uStep;
        varying vec2 vUv;
        void main(){
          vec3 s = texture2D(uSrc, vUv).rgb*0.2270270270;
          s += texture2D(uSrc, vUv + uStep*1.3846153846).rgb*0.3162162162;
          s += texture2D(uSrc, vUv - uStep*1.3846153846).rgb*0.3162162162;
          s += texture2D(uSrc, vUv + uStep*3.2307692308).rgb*0.0702702703;
          s += texture2D(uSrc, vUv - uStep*3.2307692308).rgb*0.0702702703;
          gl_FragColor = vec4(s, 1.0);
        }`,
      depthTest:false, depthWrite:false });

    /* Rendue par ADDITION sur l'image déjà faite : une lueur s'ajoute à ce qui
       est derrière, elle ne le remplace pas. */
    this.add = new THREE.ShaderMaterial({
      uniforms:{ uSrc:{value:null}, uAmount:{value:0.9} },
      vertexShader:VERT,
      fragmentShader:`
        uniform sampler2D uSrc; uniform float uAmount;
        varying vec2 vUv;
        void main(){ gl_FragColor = vec4(texture2D(uSrc, vUv).rgb*uAmount, 1.0); }`,
      transparent:true, blending:THREE.AdditiveBlending,
      depthTest:false, depthWrite:false });
  }

  /* Ce que settings.json en dit, champ par champ. */
  configure(o){ if(o) for(const k of Object.keys(o)) if(k in this) this[k] = o[k]; }

  dispose(){
    if(this.tex) this.tex.dispose();
    if(this.rtA) this.rtA.dispose();
    if(this.rtB) this.rtB.dispose();
    this.tex = this.rtA = this.rtB = null;
  }

  /* Y a-t-il seulement quelque chose à faire ? */
  wanted(){
    if(this._priming) return true;
    return this.enabled && this.strength > 0 &&
           (!this.nightOnly || (this.stage.night || 0) > 0.02);
  }

  /* COMPILER SES PROGRAMMES À L'ARMEMENT, PAS AU CRÉPUSCULE. Un shader se
     compile en quelques dizaines de millisecondes, et cela se voit d'autant
     plus que cela tombe à l'instant précis où l'image change — c'est le même
     à-coup que la lampe du fanal, par une autre porte. On fait donc tourner la
     passe une fois au démarrage, l'addition finale détournée dans une cible
     plutôt que sur l'écran, pour ne pas éclairer l'image du jour. */
  prime(){
    this._priming = true;
    try{ this.render(); }
    catch(e){ console.warn('[lueur] amorçage impossible : ' + (e && e.message || e)); }
    finally{ this._priming = false; }
  }

  render(){
    const r = this.stage.renderer;
    if(!this.wanted()){
      // la nuit finie, on rend la mémoire plutôt que de la garder pour rien
      if(this.tex) this.dispose();
      return;
    }
    r.getDrawingBufferSize(this._size);
    const w = this._size.x|0, h = this._size.y|0;
    if(w < 8 || h < 8) return;
    const bw = Math.max(4, Math.round(w*this.scale)), bh = Math.max(4, Math.round(h*this.scale));

    if(!this.tex || this.tex.image.width !== w || this.tex.image.height !== h){
      this.dispose();
      this.tex = new THREE.FramebufferTexture(w, h);
      this.tex.minFilter = this.tex.magFilter = THREE.LinearFilter;
      const opt = { minFilter:THREE.LinearFilter, magFilter:THREE.LinearFilter,
                    depthBuffer:false, stencilBuffer:false };
      this.rtA = new THREE.WebGLRenderTarget(bw, bh, opt);
      this.rtB = new THREE.WebGLRenderTarget(bw, bh, opt);
      this.rtA.texture.wrapS = this.rtA.texture.wrapT = THREE.ClampToEdgeWrapping;
      this.rtB.texture.wrapS = this.rtB.texture.wrapT = THREE.ClampToEdgeWrapping;
    }

    const prevTarget = r.getRenderTarget(), prevAuto = r.autoClear;
    r.setRenderTarget(null);
    r.copyFramebufferToTexture(this._zero, this.tex);

    r.autoClear = true;
    // 1. ce qui dépasse
    this.bright.uniforms.uScene.value = this.tex;
    this.bright.uniforms.uThresh.value = this.threshold;
    this.bright.uniforms.uKnee.value = this.knee;
    this._pass(r, this.bright, this.rtA);

    // 2. deux passes séparables, l'écart doublé à la seconde : un flou large pour peu de prises
    const px = this.radius/bw, py = this.radius/bh;
    this._blur(r, this.rtA, this.rtB, px, 0);
    this._blur(r, this.rtB, this.rtA, 0, py);
    this._blur(r, this.rtA, this.rtB, px*2, 0);
    this._blur(r, this.rtB, this.rtA, 0, py*2);

    // 3. rendue par-dessus l'image, sans l'effacer
    this.add.uniforms.uSrc.value = this.rtA.texture;
    this.add.uniforms.uAmount.value = this.strength;
    this.mesh.material = this.add;
    if(this._priming){
      r.setRenderTarget(this.rtB);              // on compile, on ne peint pas
      r.render(this.scene, this.ortho);
    }else{
      r.setRenderTarget(null);
      r.autoClear = false;
      r.render(this.scene, this.ortho);
    }

    r.autoClear = prevAuto;
    r.setRenderTarget(prevTarget);
  }

  _blur(r, src, dst, sx, sy){
    this.blur.uniforms.uSrc.value = src.texture;
    this.blur.uniforms.uStep.value.set(sx, sy);
    this._pass(r, this.blur, dst);
  }

  _pass(r, mat, dst){
    this.mesh.material = mat;
    r.setRenderTarget(dst);
    r.render(this.scene, this.ortho);
  }
};
