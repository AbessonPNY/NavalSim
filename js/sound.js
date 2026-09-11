/* Le bruit d'une bordée, et surtout le TEMPS qu'il met à venir.
 *
 * LE SON MET UNE SECONDE ET DEMIE À FAIRE CINQ CENTS MÈTRES, et c'est cela,
 * bien plus que le timbre, qui fait lire un canon comme lointain. On voit la
 * flamme, on compte, puis on entend — chacun l'a fait sous un orage, et c'est
 * pour cette raison que l'œil sait d'emblée à quelle distance le coup est
 * parti. Un échantillon sourd joué à l'instant du flash sonne comme un canon
 * en sourdine ; le même joué une seconde plus tard sonne comme un canon loin.
 * Le retard est gratuit — une division — et il est l'essentiel de l'effet.
 *
 * TROIS CENT QUARANTE-TROIS MÈTRES PAR SECONDE, ce qui n'est pas un réglage :
 * c'est la vitesse du son dans l'air à quinze degrés. À la portée de plein
 * fouet de ce jeu, deux cents mètres, cela fait six dixièmes de seconde ; à un
 * demi-mille, deux secondes et demie. Les deux se remarquent.
 *
 * L'OREILLE EST À LA CAMÉRA, pas sur le navire, et c'est le choix juste : on
 * entend d'où l'on regarde. La caméra fixe plantée à deux cents mètres retarde
 * donc VOTRE propre bordée, ce qui est exactement ce qui se passerait.
 *
 * Rien n'est chargé depuis un fichier dans la page publiée — la politique de
 * sécurité l'interdit, comme pour les .glb et la voile peinte. Les octets
 * arrivent en base64 dans Naval.SOUND_DATA, décodés ici à la main plutôt que
 * par un fetch() d'URI data:, qui est précisément ce qu'on cherche à éviter.
 */
window.Naval = window.Naval || {};

Naval.Sound = class Sound {
  constructor(){
    this.ctx = null;
    this.master = null;
    this.buf = {};                 // clé → AudioBuffer
    this.on = true;
    /* LES FINS PLUTOT QU UN COMPTEUR, et c est mon propre banc qui l a montre :
       un compteur incremente au depart et decremente sur onended FUIT des qu une
       source ne demarre jamais — et a vingt-quatre fuites le son se tait pour de
       bon, sans rien dire. Une liste d instants de fin ne peut pas deriver : on
       la purge contre l horloge, qui avance toute seule. */
    this.fins = [];                // quand chaque source programmee aura fini

    /* La vitesse du son, et la distance au-delà de laquelle on n'entend plus.
       Deux milles et demi : un coup de canon porte bien plus loin en réalité,
       mais au-delà il n'apprend plus rien et ne fait qu'encombrer. */
    this.C = 343;
    this.PORTEE = 2500;
    /* Où l'on bascule sur l'échantillon lointain. QUATRE CENTS MÈTRES, et le
       chiffre a pu être repoussé parce que le passe-bas ci-dessous porte
       désormais le dégradé : à la bascule la coupure est déjà tombée à 4,3 kHz,
       si bien que les deux échantillons se ressemblent assez pour que le
       passage ne s'entende pas. Sans le filtre il fallait basculer tôt pour que
       le lointain ne surprenne pas ; avec lui, on peut garder le claquement
       aussi longtemps qu'il est vrai. */
    this.LOIN = 400;
    /* La distance de référence de l'atténuation. Une pression acoustique
       décroît en 1/r, donc c'est une division et non une courbe inventée ; le
       plancher évite seulement qu'un coup très proche sature. */
    this.REF = 55;
    /* L AIR MANGE LES AIGUS, et c est ce qui assourdit un coup bien avant
       qu il devienne un grondement. L absorption atmospherique croit avec la
       frequence ET avec la distance, si bien qu un rapport perd son claquement
       en premier et garde son ventre : a cent metres il est deja legerement
       mat, a un demi-mille il n a plus d arete du tout.

       Sans cela le passage d un echantillon a l autre etait une BASCULE — meme
       son en plus faible, puis d un coup un autre son — la ou l oreille attend
       un degrade. Le filtre porte la continuite ; les deux echantillons ne
       font plus que marquer les deux bouts.

       Une exponentielle plutot qu une droite, parce que l absorption est
       exponentielle en distance. ETOUFFE est la distance ou la coupure tombe
       d un facteur e : a 260 m elle passe de 20 kHz a 7,4. */
    this.ETOUFFE = 260;
    this.FC_MIN = 700;             // au-dela, ce n est plus qu un ventre
    this.MAX_VIVANTS = 24;
  }

  /* Un navigateur ne fait aucun bruit tant que l'utilisateur n'a rien touché,
     et c'est une règle qu'on ne contourne pas : on attend le premier geste.
     Appelé autant de fois qu'on veut, il ne construit qu'une fois. */
  wake(){
    if(this.ctx) { if(this.ctx.state === 'suspended') this.ctx.resume(); return this.ctx; }
    const AC = window.AudioContext || window.webkitAudioContext;
    if(!AC) return null;
    this.ctx = new AC();
    this.master = this.ctx.createGain();
    this.master.gain.value = 0.9;
    this.master.connect(this.ctx.destination);
    return this.ctx;
  }

  /* Les octets, d'où qu'ils viennent. Sur le serveur de dev c'est un chemin ;
     dans la page publiée c'est du base64 déjà embarqué, décodé ici — un
     fetch() sur une URI data: serait une requête, et le but est de n'en faire
     aucune. */
  async load(cle, src){
    const ctx = this.wake();
    if(!ctx) return null;
    let octets;
    const embarque = (Naval.SOUND_DATA || {})[cle];
    if(embarque){
      const b64 = embarque.slice(embarque.indexOf(',') + 1);
      const bin = atob(b64);
      const u8 = new Uint8Array(bin.length);
      for(let i=0;i<bin.length;i++) u8[i] = bin.charCodeAt(i);
      octets = u8.buffer;
    }else{
      const r = await fetch(src);
      if(!r.ok) return null;
      octets = await r.arrayBuffer();
    }
    /* decodeAudioData rend une promesse sur les navigateurs récents et prend
       des rappels sur les anciens ; on couvre les deux sans y penser. */
    this.buf[cle] = await new Promise((ok, ko) => {
      const p = ctx.decodeAudioData(octets, ok, ko);
      if(p && p.then) p.then(ok, ko);
    });
    return this.buf[cle];
  }

  /* ------------------------------------------------------------------ */
  /* TOUT CE QUI SONNE PASSE PAR ICI, et c'est la seule raison pour laquelle un
     impact à quatre cents mètres est en retard et mat sans qu'une ligne le
     redise : le retard, l'absorption de l'air, le relief gauche-droite et
     l'atténuation sont des propriétés de la DISTANCE, pas du coup de canon.
     Les écrire une seconde fois pour le bois qui casse, c'était se donner deux
     acoustiques à tenir en accord — la faute que ce projet passe son temps à
     éviter ailleurs.

     L'appelant ne choisit que ce qui lui appartient : quel échantillon, à
     quelle hauteur, et avec quelle force il a frappé. */
  _jouer(b, pos, ecoute, rate, vol){
    if(!this.on || !this.ctx || !b) return;
    const ctx = this.ctx;
    const oreille = ecoute.isCamera ? ecoute.position : ecoute;

    const maintenant = ctx.currentTime;
    this.fins = this.fins.filter(t => t > maintenant);
    if(this.fins.length >= this.MAX_VIVANTS) return;

    const d = pos.distanceTo(oreille);
    if(d > this.PORTEE) return;

    const src = ctx.createBufferSource();
    src.buffer = b;
    src.playbackRate.value = Math.max(0.6, Math.min(1.6, rate));

    const g = ctx.createGain();
    g.gain.value = Math.min(1, vol * this.REF/Math.max(this.REF, d));

    /* Le passe-bas de l'air : la même détonation, privée de son claquement à
       mesure qu'elle vient de loin. */
    const f = ctx.createBiquadFilter();
    f.type = 'lowpass';
    f.frequency.value = Math.max(this.FC_MIN, 20000*Math.exp(-d/this.ETOUFFE));
    f.Q.value = 0.7;

    /* LE RELIEF GAUCHE-DROITE, et c'est la même arithmétique que le choix du
       bord en batterie : le produit scalaire du relèvement par le TRAVERS de la
       caméra en donne le signe et l'ampleur. Un coup droit devant ou droit
       derrière tombe à zéro, ce qui est juste — deux oreilles ne distinguent
       pas non plus l'avant de l'arrière sans tourner la tête. L'amplitude
       s'arrête à 0,9 : un panoramique à fond colle le son à une enceinte. */
    let sortie = f;
    if(ecoute.isCamera && ctx.createStereoPanner){
      const m = ecoute.matrixWorld.elements;         // colonne 0 = son travers
      const dx = (pos.x - oreille.x)/Math.max(1e-6, d);
      const dz = (pos.z - oreille.z)/Math.max(1e-6, d);
      const pan = ctx.createStereoPanner();
      pan.pan.value = Math.max(-1, Math.min(1, (dx*m[0] + dz*m[2]) * 0.9));
      f.connect(pan); sortie = pan;
    }
    src.connect(f); sortie.connect(g); g.connect(this.master);

    // LE RETARD, qui est tout l'intérêt : la distance divisée par la vitesse du son
    const quand = maintenant + d/this.C;
    this.fins.push(quand + b.duration/src.playbackRate.value);
    src.start(quand);
  }

  /* UN COUP DE CANON, entendu d'où l'on regarde.
   *
   * `k` est le calibre relatif que guns.js calcule déjà (spec.L/60) : une
   * caronade n'est pas un trente-deux, et une grosse pièce sonne plus GRAVE.
   * On le rend par la vitesse de lecture plutôt que par un troisième
   * échantillon, ce qui allonge du même coup la détente.
   */
  boom(pos, k, ecoute){
    const oreille = ecoute.isCamera ? ecoute.position : ecoute;
    const d = pos.distanceTo(oreille);
    const b = d > this.LOIN ? (this.buf.loin || this.buf.pres)
                            : (this.buf.pres || this.buf.loin);
    /* Jamais deux fois le même coup : la charge était dosée à la main, ce que
       guns.js éparpille déjà sur la portée. La même irrégularité, à l'oreille. */
    this._jouer(b, pos, ecoute,
                (1.15 - 0.30*k) * (1 + (Math.random() - 0.5)*0.06), 1);
  }

  /* LE BOIS QUI CASSE, là où le boulet a porté — et donc à la distance de la
   * CIBLE, pas du canon. C'est ce qui rend la chose lisible : on entend d'abord
   * la pièce, puis, un instant plus tard s'il y a de la distance, le coup dans
   * la muraille. Les deux voyagent à la même vitesse depuis deux endroits
   * différents, et l'arithmétique s'en occupe toute seule.
   *
   * DEUX ÉCHANTILLONS TIRÉS AU SORT, parce qu'un seul se reconnaît à la
   * troisième touche et cesse d'être un choc pour devenir un bruitage. Même
   * raison que les intervalles irréguliers de la bordée.
   *
   * ET UN MÂT N'EST PAS UNE MURAILLE : c'est le même bois, plus léger et plus
   * sec, donc le même échantillon monté d'un ton. Un troisième fichier dirait
   * mieux, mais deux suffisent à ce que l'oreille sépare les deux événements.
   */
  crash(pos, k, vitesse, quoi, ecoute){
    const b = Math.random() < 0.5 ? (this.buf.bois1 || this.buf.bois2)
                                  : (this.buf.bois2 || this.buf.bois1);
    /* La force du choc porte le volume, et c'est la vitesse restante qui la
       donne : un boulet arrivé à bout de course cogne moins fort. Trois cents
       mètres par seconde est le plein fouet de ce modèle. */
    const fort = Math.max(0.3, Math.min(1, (vitesse || 200)/300));
    const aigu = quoi === 'mast' ? 1.18 : 1.0;
    this._jouer(b, pos, ecoute,
                aigu * (1.15 - 0.30*k) * (1 + (Math.random() - 0.5)*0.10),
                0.85 * fort);
  }
};
