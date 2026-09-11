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

  /* Le cours moyen d'une tonne d'épices, en pièces. Les épices étaient la
     cargaison la plus chère qu'un navire pût porter — c'est pour elles qu'on
     armait, et c'est pour cela qu'elles font une raison de traverser. */
  EPICE: 620,

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

  PALIER: 900,                       // un quart d'heure de jeu par palier

  /* Le cours des épices à ce port, en pièces la tonne. */
  spice(key, t){
    const T = this.PALIER, n = Math.floor(t/T), u = t/T - n;
    const e = u*u*(3 - 2*u);
    const a = this._h(key, n), b = this._h(key, n + 1);
    const f = a + (b - a)*e;
    /* De 0,55 à 1,45 du cours moyen. Un rapport de deux entre le port le moins
       cher et le plus cher, ce qui rend la destination intéressante à choisir
       sans que le mauvais choix soit ruineux. */
    return Math.round(this.EPICE * (0.55 + 0.90*f));
  },

  // ce que le négociant demande, et ce qu'il consent à payer
  buyPrice(key, t){  return Math.round(this.spice(key, t) * (1 + this.MARGE)); },
  sellPrice(key, t){ return Math.round(this.spice(key, t) * (1 - this.MARGE)); },

  /* Écus et pièces, pour l'affichage. Une bourse de 1 500 se lit « 25 ⊙ 0 ». */
  format(sous){
    const e = Math.floor(sous / this.SOUS_PAR_ECU), p = sous % this.SOUS_PAR_ECU;
    return { ecus:e, pieces:p };
  }
};
