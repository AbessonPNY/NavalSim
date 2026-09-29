/* La bourse, le cours des épices, et la poudre.
 *
 * Premier morceau de JEU dans un moteur qui n'en avait aucun. Tout ce qui
 * précède décrit un navire ; ceci décrit une raison de le mener quelque part.
 *
 * UN SEUL NOMBRE, DEUX LECTURES. La bourse est un entier de pièces d'argent et
 * rien d'autre. Les écus d'or sont une manière de le lire, pas une seconde
 * réserve — tenir deux compteurs, c'est garantir qu'ils divergeront le jour où
 * une transaction franchit la retenue, et l'on passerait le reste du projet à
 * les réconcilier. Même règle que la fraction de toile établie, que la part de
 * gréement debout, et que tout ce qui dans ce projet doit être vu et calculé à
 * la fois : un nombre, deux usagers.
 *
 * Soixante pièces pour un écu, ce qui n'est pas arbitraire : un écu valait
 * trois livres et une livre vingt sous. La retenue tombe donc où elle tombait.
 */
window.Naval = window.Naval || {};

Naval.Purse = class Purse {
  constructor(sous){
    this.sous = Math.max(0, sous|0);
  }
  get ecus(){ return Math.floor(this.sous / Naval.Market.SOUS_PAR_ECU); }
  get pieces(){ return this.sous % Naval.Market.SOUS_PAR_ECU; }

  has(n){ return this.sous >= n; }
  /* Rend faux et NE PRÉLÈVE RIEN si la bourse est courte. Un débit partiel est
     la manière classique de perdre de l'argent sans rien recevoir. */
  take(n){
    n = Math.max(0, Math.round(n));
    if(this.sous < n) return false;
    this.sous -= n;
    return true;
  }
  add(n){ this.sous += Math.max(0, Math.round(n)); }
};

Naval.Market = {
  SOUS_PAR_ECU: 60,

  /* CE QU'ON CHARGE ET CE QU'ON REVEND, lu dans market/marchandises.json.

     EN FICHE ET NON EN DUR : ajouter une denrée ou changer un cours est une
     decision de jeu, pas de programme, et elle doit se prendre sans rebatir
     quoi que ce soit. Les deux moteurs lisent la meme fiche.

     Vide tant que personne ne l'a lue : le comptoir ne montre alors rien, ce
     qui vaut mieux qu'un cours invente. */
  WARES: [],
  MARGE: 0.12,

  charger(data){
    if(!data) return this;
    if(typeof data.marge === 'number') this.MARGE = data.marge;
    this.WARES = (data.marchandises || []).map(w => ({
      key: w.key, nom: w.nom, prix: w.prix, note: w.note || ''
    }));
    return this;
  },

  denree(key){
    for(const w of this.WARES) if(w.key === key) return w;
    return null;
  },

  /* Une charge de poudre : ce que consomme un coup de canon. Une bordée de
     douze pièces coûte donc cinq écus, et cent charges en coûtent quarante —
     de quoi rendre une canonnade décidée plutôt que gratuite. */
  POUDRE: 26,

  /* Ce que la bourse contient au premier armement, et ce n'est pas un chiffre
     rond posé au hasard : il faut qu'il achète une PREMIÈRE CARGAISON. À
     quinze cents, soit vingt-cinq écus, le bouton « acheter 10 t » échouait
     systématiquement au départ — quatre-vingt-dix-sept écus la dizaine de
     tonnes — et un jeu qui commence par un refus n'explique rien à personne.
     Quatre cents écus chargent une quarantaine de tonnes au cours moyen, soit
     le tiers de la cale d'un chaland : de quoi faire une traversée et voir ce
     qu'elle rapporte. */
  DEPART: 24000,

  /* Un port n'achète pas ce qu'il vend, et c'est tout le commerce. La marge du
     négociant est prise sur le prix affiché : on achète un peu plus cher et on
     revend un peu moins cher que le cours, donc faire l'aller-retour à vide
     entre deux ports au même cours PERD de l'argent. Sans cela il suffirait
     d'acheter et de revendre sur place pour tondre la différence. */
  MARGE: 0.12,

  /* LE COURS EST UNE FONCTION PURE DU PORT ET DE L'HEURE, écrite comme les îles
     et comme les dépressions, et pour les mêmes raisons : aucun état à tenir,
     aucun fichier à charger, la même chose sur toutes les machines et d'une
     session à l'autre. Revenir à Port-Royal deux heures plus tard y retrouve le
     cours que deux heures ont fait.

     Et il est CONTINU, ce qui compte autant. Un tirage par palier ferait sauter
     le prix d'un tiers entre deux images, et l'on apprendrait vite à attendre
     devant le comptoir que le chiffre change. On interpole donc entre deux
     paliers avec un smoothstep — même remède que la dérive triangulaire des
     dépressions : borné ET continu, seule combinaison qui serve. */
  _h(key, n){
    let s = n*374761393 | 0;
    for(let i=0;i<key.length;i++) s = (s*31 + key.charCodeAt(i)) | 0;
    s = (s ^ (s >>> 13)) * 1274126177 | 0;
    return ((s ^ (s >>> 16)) >>> 0) / 4294967296;
  },

  /* UN COURS DOIT TENIR LE TEMPS D'UNE TRAVERSÉE, sans quoi le commerce est du
     bruit et non une décision. À neuf cents secondes — un quart d'heure — il
     tournait SIX FOIS pendant un passage : relevé sur Port-Royal → Le Carénage,
     7,8 milles et 93 minutes à cinq nœuds, le cours de vente faisait 671, 607,
     379, 577, 730, 488 puis 368. On appareillait pour 671 et l'on trouvait 368.
     Aucune information, si parfaite fût-elle, n'aurait rattrapé ça : le défaut
     n'était pas de ne pas savoir, c'était que rien ne valait d'être su.

     Trois heures : un passage en couvre moins de la moitié, donc le cours
     qu'on a vu garde son sens à l'arrivée, et il faut plusieurs traversées
     pour qu'une route cesse d'être bonne. C'est la même échelle que la météo
     automatique, dont le SYSTÈME tourne en une dizaine de minutes quand ses
     rafales tournent en vingt secondes — ce qui compte n'est jamais la valeur
     du pas mais son rapport à la durée de ce qu'on entreprend. */
  PALIER: 10800,

  /* Five knots, in metres per second: the pace a passage is reckoned at. */
  ALLURE: 2.572,

  /* AND THE STEP IS TUNED TO THE WORLD, NOT WRITTEN FOR ONE.

     Three hours was right for THIS archipelago and means nothing on its own —
     the whole lesson above is that what counts is the ratio of the step to
     the length of a passage. Move the islands and a hard-coded 10 800 would
     bring the measured fault straight back: at five times the distance the
     price would turn over two and a half times per crossing, which is exactly
     the 671-becomes-368 that made trading noise instead of a decision.

     The longest leg divided by five knots keeps that ratio exactly where it
     was measured good: the shortest passage covers about half a step, the
     longest about one. */
  tune(longestLegM){
    this.PALIER = Math.max(600, Math.round(longestLegM / this.ALLURE));
    return this.PALIER;
  },

  /* LA CLÉ DU HACHAGE : la marchandise ET le port. C'est tout ce qu'il fallait
     pour que le commerce devienne un metier — le sucre peut etre haut a Montego
     Bay quand l'indigo y est bas, et c'est cet ecart-la, et non le niveau
     general d'un port, qui fait choisir une route.

     La regle est UNIFORME : aucune denree n'est le cas particulier d'une autre,
     pas meme l'epice qui fut longtemps la seule. Ses cours ne sont donc plus
     ceux d'avant, et c'est sans consequence : un cours est une fonction pure du
     lieu et de l'heure, rien ne le retient d'une partie a l'autre. */
  _sel(good, port){ return good + '/' + port; },

  /* Le cours d'une denrée à ce port, en pièces la tonne. */
  price(good, port, t){
    const w = this.denree(good);
    if(!w) return 0;
    const T = this.PALIER, n = Math.floor(t/T), u = t/T - n;
    const e = u*u*(3 - 2*u);
    const k = this._sel(good, port);
    const a = this._h(k, n), b = this._h(k, n + 1);
    const f = a + (b - a)*e;
    /* De 0,55 à 1,45 du cours moyen. Un rapport de deux entre le port le moins
       cher et le plus cher, ce qui rend la destination intéressante à choisir
       sans que le mauvais choix soit ruineux. */
    return Math.round(w.prix * (0.55 + 0.90*f));
  },

  /* Le rapport du cours à sa moyenne : 1 est le cours ordinaire, 1,45 le plus haut. */
  ratio(good, port, t){
    const w = this.denree(good);
    return !w || w.prix <= 0 ? 0 : this.price(good, port, t) / w.prix;
  },

  // ce que le négociant demande, et ce qu'il consent à payer
  buyPrice(good, port, t){  return Math.round(this.price(good, port, t) * (1 + this.MARGE)); },
  sellPrice(good, port, t){ return Math.round(this.price(good, port, t) * (1 - this.MARGE)); },

  /* LA DENRÉE QUI S'Y PAIE LE MIEUX — au RAPPORT et non au chiffre. Comparer
     les prix bruts rendrait toujours l'indigo, qui vaut six fois le sucre par
     nature et non par occasion ; le rapport dit ce qui est cher POUR ELLE,
     c'est-a-dire ce qu'il y a a y gagner. */
  best(port, t){
    let best = null, top = -1;
    for(const w of this.WARES){
      const r = this.ratio(w.key, port, t);
      if(r > top){ top = r; best = w; }
    }
    return best;
  },

  /* Écus et pièces, pour l'affichage. Une bourse de 1 500 se lit « 25 ⊙ 0 ». */
  format(sous){
    const e = Math.floor(sous / this.SOUS_PAR_ECU), p = sous % this.SOUS_PAR_ECU;
    return { ecus:e, pieces:p };
  },

  /* ------------------------------------------------------------------ */
  /* CE QU'ON SAIT DES AUTRES PORTS, et c'est le cœur du commerce : savoir où
     vendre vaut autant que savoir naviguer. Deux sortes de renseignement, et
     ils ne se ressemblent pas.

     AU COMPTOIR : exact, et VIEUX. Le négociant sait ce qu'on payait au
     Carénage quand la dernière nouvelle en est partie — un chiffre ferme, mais
     daté. Le retard n'est pas inventé : c'est la distance divisée par la
     vitesse d'un navire porteur de nouvelles, parce que l'information voyageait
     par la mer, à la vitesse de la mer. Le port le plus lointain donne donc la
     nouvelle la plus alléchante ET la plus périmée, ce qui est la tension
     entière du métier.

     Et c'est ENCORE une fonction pure : `spice(key, t − distance/vitesse)`.
     Rien à stocker, rien à tenir à jour, la même chose sur toutes les machines
     — comme les îles, comme les dépressions, comme le cours lui-même. */
  NOUVELLE: 2.6,                     // m/s : cinq nœuds, l'allure d'un aviso

  /* LA NOUVELLE PORTE UNE DENRÉE, et une seule. Quatorze ports fois douze
     denrees font cent soixante-huit chiffres : un tableau que personne ne lit.
     Le negociant dit donc ce qui se paie le mieux la-bas — c'est la phrase
     qu'un homme du metier dirait, et c'est celle qui decide. */
  news(from, to, t){
    const lag = Math.hypot(from.x - to.x, from.z - to.z) / this.NOUVELLE;
    const w = this.best(to.key, t - lag);
    return { key:to.key, name:to.name, lag,
             good: w ? w.key : '', goodName: w ? w.nom : '',
             sell: w ? this.sellPrice(w.key, to.key, t - lag) : 0 };
  },

  /* L'âge d'une nouvelle, dit comme on le dirait. Un chiffre périmé SANS son
     âge est un mensonge ; avec son âge, c'est un renseignement dont on juge
     soi-même. C'est toute la différence, et elle tient en trois mots. */
  age(lag){
    const m = Math.round(lag/60);
    if(m < 60) return 'il y a ' + m + ' min';
    return 'il y a ' + Math.floor(m/60) + ' h ' + String(m%60).padStart(2,'0');
  },

  /* EN MER : frais, et VAGUE. On parle un navire, il dit ce qu'il a vu il y a
     peu — mais un capitaine croisé au large ne récite pas une mercuriale, il
     dit que ça se paie bien ou que ça ne se paie plus. Pas de chiffre, donc, et
     c'est délibéré : un chiffre se compare et se calcule, une appréciation se
     pèse. L'un se met en tableau, l'autre demande de décider.

     Vrai, cependant. Une rumeur fausse est un autre jeu — celui où l'on doute
     de ses sources — et il demande qu'on ait d'abord de quoi les recouper. */
  BANDES: [
    [1.28, 'on y paie des prix d’or'],
    [1.10, 'les cours y sont hauts'],
    [0.92, 'les cours y sont ordinaires'],
    [0.76, 'les cours y sont mous'],
    [0.00, 'on n’y achète presque plus']
  ],
  VOILES: ['un brick', 'une flûte', 'une barque de pêche', 'un aviso',
           'une caravelle', 'un sloop', 'une galiote'],

  /* LA DENRÉE DONT IL PARLE est tiree sur le meme nombre que sa voile : un
     capitaine ne parle pas de tout, il parle de ce qu'il vient de vendre. */
  rumour(isl, t, rnd){
    if(!this.WARES.length) return { voile:'', port:isl.name, good:'', mot:'', lie:'', fort:false, faible:false };
    const w = this.WARES[Math.floor(rnd*this.WARES.length) % this.WARES.length];
    const r = this.ratio(w.key, isl.key, t);
    let mot = this.BANDES[this.BANDES.length-1][1];
    for(const [seuil, texte] of this.BANDES) if(r >= seuil){ mot = texte; break; }
    const v = this.VOILES[Math.floor(rnd*this.VOILES.length) % this.VOILES.length];
    /* « Il dit QU'on y paie » mais « il dit QUE les cours sont hauts » : on
       n'élide que devant une voyelle. La règle vit ici, avec les mots qu'elle
       gouverne, plutôt que dans la page qui les assemble — sans quoi ajouter
       une bande demanderait de se souvenir d'aller corriger une phrase
       ailleurs. */
    const lie = /^[aeiouyàâéèêëîïôöûù]/i.test(mot) ? 'qu’' : 'que ';
    return { voile:v, port:isl.name, good:w.nom, mot, lie, fort:r >= 1.10, faible:r < 0.92 };
  }
};
