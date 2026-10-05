/* LE VRAI CIEL — world/etoiles.json, tiré du Yale Bright Star Catalogue (BSC5,
 * Hoffleit & Warren 1991, domaine public), tel que le CDS de Strasbourg le publie :
 * https://cdsarc.cds.unistra.fr/ftp/V/50/catalog.gz (format à colonnes fixes, V/50).
 *
 *   node tools/stars.js chemin/vers/bsc5.dat [magnitude limite, 5.5]
 *
 * Ce qu'on garde de chaque étoile, et rien de plus : sa position de J2000 (ascension
 * droite et déclinaison, en degrés), son mouvement propre (secondes d'arc par an — sur
 * trois siècles, Arcturus en fait dix minutes), son éclat (la magnitude V), sa couleur
 * (l'indice B−V), et son nom : la lettre de Bayer et la constellation, plus le nom usuel
 * en français pour les étoiles qu'un pilote connaît. Le jeu les porte à l'année de la
 * partie par la précession (core/NavalSim.Core/Stars.cs).
 */
const fs = require('fs');
const path = require('path');

const src = process.argv[2];
const limit = process.argv[3] ? Number(process.argv[3]) : 5.5;
if (!src) { console.error('usage : node tools/stars.js bsc5.dat [magnitude limite]'); process.exit(1); }

const greek = {
  Alp: 'α', Bet: 'β', Gam: 'γ', Del: 'δ', Eps: 'ε', Zet: 'ζ', Eta: 'η', The: 'θ', Iot: 'ι', Kap: 'κ', Lam: 'λ', Mu: 'μ',
  Nu: 'ν', Xi: 'ξ', Omi: 'ο', Pi: 'π', Rho: 'ρ', Sig: 'σ', Tau: 'τ', Ups: 'υ', Phi: 'φ', Chi: 'χ', Psi: 'ψ', Ome: 'ω'
};
// les étoiles qu'un pilote nomme — par leur lettre de Bayer, pour ne rien deviner
const named = {
  'α UMi': 'la Polaire', 'β UMi': 'Kochab', 'γ UMi': 'Pherkad',
  'α UMa': 'Dubhe', 'β UMa': 'Mérak', 'γ UMa': 'Phecda', 'δ UMa': 'Megrez', 'ε UMa': 'Alioth', 'ζ UMa': 'Mizar', 'η UMa': 'Alkaïd',
  'α CMa': 'Sirius', 'α Car': 'Canopus', 'α Boo': 'Arcturus', 'α Lyr': 'Véga', 'α Aur': 'Capella', 'β Ori': 'Rigel',
  'α CMi': 'Procyon', 'α Ori': 'Bételgeuse', 'α Aql': 'Altaïr', 'α Tau': 'Aldébaran', 'α Vir': "l'Épi", 'α Sco': 'Antarès',
  'β Gem': 'Pollux', 'α Gem': 'Castor', 'α Leo': 'Régulus', 'α Cyg': 'Deneb', 'α PsA': 'Fomalhaut', 'α Eri': 'Achernar',
  'α Cru': 'Acrux', 'β Cru': 'Mimosa', 'γ Cru': 'Gacrux', 'α Cen': 'Rigil Kentaurus', 'β Cen': 'Hadar',
  'α Cas': 'Schedar', 'β Cas': 'Caph', 'γ Ori': 'Bellatrix', 'ε Ori': 'Alnilam', 'ζ Ori': 'Alnitak', 'δ Ori': 'Mintaka',
  'α Ari': 'Hamal', 'α Hya': 'Alphard', 'β Leo': 'Dénébola', 'β Per': 'Algol', 'α Per': 'Mirfak', 'λ Sco': 'Shaula',
  'α And': 'Alphératz', 'α Peg': 'Markab', 'β Peg': 'Scheat', 'γ Peg': 'Algénib'
};

const out = [];
for (const line of fs.readFileSync(src, 'latin1').split(/\r?\n/)) {
  if (line.length < 107) continue;
  const col = (a, b) => line.substring(a - 1, b);
  const rh = col(76, 77).trim();
  if (rh === '') continue;                       // novae et objets sans position
  const v = Number(col(103, 107));
  if (!isFinite(v) || col(103, 107).trim() === '' || v > limit) continue;
  const ra = (Number(rh) + Number(col(78, 79)) / 60 + Number(col(80, 83)) / 3600) * 15;
  const sgn = col(84, 84) === '-' ? -1 : 1;
  const dec = sgn * (Number(col(85, 86)) + Number(col(87, 88)) / 60 + Number(col(89, 90)) / 3600);
  const bvs = col(110, 114).trim();
  const bv = bvs === '' ? 0.6 : Number(bvs);
  const pma = Number(col(149, 154)) || 0, pmd = Number(col(155, 160)) || 0;
  const bay = col(8, 10).trim(), cons = col(12, 14).trim(), fl = col(5, 7).trim();
  let name = '';
  if (bay && greek[bay]) name = `${greek[bay]} ${cons}`;
  else if (fl) name = `${fl} ${cons}`;
  const proper = named[name] || '';
  out.push([+ra.toFixed(5), +dec.toFixed(5), pma, pmd, v, +bv.toFixed(2), proper || name]);
}
out.sort((a, b) => a[4] - b[4]);
const doc = {
  _aide: `LE VRAI CIEL : ${out.length} étoiles jusqu'à la magnitude ${limit}, tirées du Yale Bright Star Catalogue (BSC5, domaine public ; CDS V/50) par tools/stars.js. Chaque étoile : [ascension droite J2000 (°), déclinaison J2000 (°), mouvement propre en ascension droite × cos δ ("/an), en déclinaison ("/an), magnitude V, indice B−V, nom]. Le jeu les porte à l'année de la partie (précession, core/NavalSim.Core/Stars.cs) et les dessine sur le dôme (godot/scripts/StarMap.cs).`,
  etoiles: out
};
const dst = path.join(__dirname, '..', 'world', 'etoiles.json');
fs.writeFileSync(dst, JSON.stringify(doc).replace(/\],\[/g, '],\n[') + '\n');
console.log(`${out.length} étoiles (V ≤ ${limit}) → world/etoiles.json, ${(fs.statSync(dst).size / 1024).toFixed(0)} Ko ; nommées : ${out.filter(s => Object.values(named).includes(s[6])).length} / ${Object.keys(named).length}`);
