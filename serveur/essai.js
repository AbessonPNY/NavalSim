#!/usr/bin/env node
/*
 * Exercise a deployed api.php end to end, with a throwaway player:
 *   node serveur/essai.js https://exemple.fr/naval/api.php
 * Creates the player, sends a game, lists it, fetches it back, erases it.
 * The throwaway player stays in the database (there is no action to delete one).
 */
const crypto = require('crypto');

const url = process.argv[2];
if (!url) { console.error('usage : node serveur/essai.js <adresse de api.php>'); process.exit(2); }

const cle = crypto.randomBytes(32).toString('hex');
const pseudo = 'essai-' + cle.slice(0, 6);

async function call(a, body = {}) {
  const r = await fetch(url, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ a, cle, ...body }),
  });
  const text = await r.text();
  let j;
  try { j = JSON.parse(text); } catch { throw new Error(`${a} : pas du JSON (${r.status}) — ${text.slice(0, 200)}`); }
  if (!j.ok) throw new Error(`${a} : ${r.status} ${j.erreur}`);
  return j;
}

function check(cond, what) {
  if (!cond) throw new Error('échec : ' + what);
  console.log('  ✓ ' + what);
}

(async () => {
  const etat = await call('etat');
  check(etat.version >= 1, `le serveur répond (version ${etat.version})`);

  let refused = false;
  try { await call('liste'); } catch { refused = true; }
  check(refused, 'une clé inconnue est refusée');

  await call('joueur', { pseudo });
  check((await call('moi')).pseudo === pseudo, `joueur créé : ${pseudo}`);

  const partie = {
    id: 'e55a1' + cle.slice(0, 3), nom: 'Essai — Port Royal', mode: 'libre', region: 'caraibes',
    navire: 'sloop.json', ecrit: '2026-10-15 08:00', sous: 1234, carnets: { caraibes: 'x'.repeat(30000) },
    stats: { milles: 12.5, ecus: 41, jours: 3, coules: 1, poissons: 4 },
  };
  await call('envoyer', { partie });
  await call('envoyer', { partie: { ...partie, sous: 999 } });
  const liste = await call('liste');
  check(liste.parties.length === 1, 'un second envoi remplace le premier');
  check(liste.parties[0].stats.milles === 12.5, 'les comptes reviennent avec la liste');

  const back = (await call('partie', { id: partie.id })).partie;
  check(back.sous === 999 && back.carnets.caraibes.length === 30000, 'la partie revient entière');

  await call('effacer', { id: partie.id });
  check((await call('liste')).parties.length === 0, 'effacée');

  console.log('Tout va bien.');
})().catch(e => { console.error(e.message); process.exit(1); });
