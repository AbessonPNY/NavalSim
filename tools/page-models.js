/* LES MODÈLES DE LA PAGE, TIRÉS DE CEUX DE GODOT.
 *
 *   node tools/page-models.js            # tout ce que godot-models/ contient
 *   node tools/page-models.js --liste    # dire ce qui serait fait, sans le faire
 *
 * Godot lit sur le disque et n'a pas de plafond ; la page publiée est UN fichier
 * autonome de 16 Mo au plus, où chaque modèle entre en base64. Un même export
 * ne peut donc pas servir les deux : la frégate en pleine définition pèse 14 Mo
 * à elle seule.
 *
 * D'où la règle : on exporte UNE FOIS, en pleine définition, dans
 * godot-models/<le chemin habituel> — et cet outil en tire la copie de la page,
 * à sa place habituelle, en ramenant les textures à huit bits et, si la fiche
 * d'allègement le demande, en bornant le côté de certaines images.
 *
 * Les allègements sont des DONNÉES, dans godot-models/allegement.json :
 *
 *   { "ships/models/roter_lowe_1597.glb": { "max": { "fabrics": 512 } } }
 *
 * Sans entrée, un modèle n'est que ramené à huit bits — ce qui suffit presque
 * toujours, une carte de normales n'ayant jamais demandé seize.
 *
 * ET « page: false » POUR CE QUI N'ENTRE PAS DU TOUT. La page a un plafond, et
 * deux galions à quatre mégaoctets le franchissent : le second n'y tient pas,
 * quelque effort qu'on fasse sur ses textures, parce que ce qui pèse est sa
 * GÉOMÉTRIE. Un modèle ainsi marqué n'a donc pas de copie de page, et la fiche
 * peut garder son « glb » sans mentir — build.js avertit et ShipModel dessine la
 * coque d'après ses lignes, ce qui est un repli honnête et non une panne.
 *
 *   { "ships/models/hero_ship.glb": { "page": false } }
 *
 * Et s'il en traîne une copie, l'outil l'EFFACE : c'est arrivé une fois, par un
 * export de Blender lâché à la place de la page, et la page est sortie à 21 Mo.
 * Une règle qui laisse survivre ce qu'elle interdit ne sert à rien.
 */
'use strict';
const fs = require('fs');
const path = require('path');
const { execFileSync } = require('child_process');

const racine = path.resolve(__dirname, '..');
const lourd = path.join(racine, 'godot-models');
const liste = process.argv.includes('--liste');

if (!fs.existsSync(lourd)) {
  console.error('godot-models/ n\'existe pas : rien à faire.');
  process.exit(0);
}

let regles = {};
const fiche = path.join(lourd, 'allegement.json');
if (fs.existsSync(fiche)) {
  try { regles = JSON.parse(fs.readFileSync(fiche, 'utf8')); }
  catch (e) { console.error('allegement.json illisible : ' + e.message); process.exit(1); }
}

function marcher(dir) {
  const out = [];
  for (const e of fs.readdirSync(dir, { withFileTypes: true })) {
    const p = path.join(dir, e.name);
    if (e.isDirectory()) out.push(...marcher(p));
    else if (e.name.toLowerCase().endsWith('.glb')) out.push(p);
  }
  return out;
}

const fichiers = marcher(lourd);
if (fichiers.length === 0) { console.log('godot-models/ ne contient aucun .glb.'); process.exit(0); }

let avant = 0, apres = 0, hp = 0;
for (const src of fichiers) {
  const rel = path.relative(lourd, src).split(path.sep).join('/');
  const dst = path.join(racine, rel);
  const r = regles[rel] || {};
  const args = [path.join(__dirname, 'glb-8bit.js'), dst];
  for (const [motif, px] of Object.entries(r.max || {})) args.push('--max', `${motif}=${px}`);

  const poids = fs.statSync(src).size;
  avant += poids;
  const hors = r.page === false;
  if (liste) {
    console.log(`${rel}  ${(poids / 1048576).toFixed(2)} Mo` +
                (hors ? '  (hors page : coque dessinée)' : '') +
                (r.max && !hors ? `  (bornes : ${JSON.stringify(r.max)})` : ''));
    continue;
  }
  if (hors) {
    /* On le DIT, et on efface ce qui traînerait : un silence laisserait croire
       que la copie existe, et une copie oubliée ferait déborder la page. */
    if (fs.existsSync(dst)) { fs.unlinkSync(dst); console.log(`${rel} : copie de page RETIRÉE (page: false)`); }
    else console.log(`${rel} : pas de copie de page (page: false) — la coque sera dessinée`);
    hp++;
    continue;
  }
  fs.mkdirSync(path.dirname(dst), { recursive: true });
  fs.copyFileSync(src, dst);
  /* L'outil des textures travaille EN PLACE : on copie le lourd à sa place de
     page, puis on l'allège. La copie n'est donc jamais la source — on ne peut
     pas abîmer l'export d'origine en se trompant de sens. */
  execFileSync(process.execPath, args, { stdio: 'inherit' });
  apres += fs.statSync(dst).size;
}

if (!liste)
  console.log(`\n${fichiers.length} modèle(s) : ${(avant / 1048576).toFixed(2)} Mo en pleine définition, ` +
              `${(apres / 1048576).toFixed(2)} Mo pour la page` +
              (hp ? `, ${hp} hors page` : '') + '. ' +
              `Vérifiez ensuite avec « node build.js » : la limite est de 16 Mo.`);
