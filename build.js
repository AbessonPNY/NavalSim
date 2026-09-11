/* Inline the modules into one self-contained page.
 *
 * Why this exists: a published Artifact runs under a CSP that only admits
 * scripts from a short CDN allowlist. A local <script src="js/ocean.js"> is
 * blocked silently — no error, just a blank page. So we develop against the
 * split files and ship a single inlined file.
 *
 *   node build.js   →   dist/naval-sim.html
 */

const fs = require('fs');
const path = require('path');

const ROOT = __dirname;
const SRC = path.join(ROOT, 'naval-sim.html');
const OUT_DIR = path.join(ROOT, 'dist');
const OUT = path.join(OUT_DIR, 'naval-sim.html');

let html = fs.readFileSync(SRC, 'utf8');
const inlined = [];

/* Ship specs are fetched as files in development. A published page cannot fetch
 * local files, so bake them in. Any spec naming a .glb keeps the reference: the
 * model simply will not load in a sandbox, and ShipModel falls back to the
 * procedural hull rather than failing. */
// Whatever specs are in the ships folder, in the order they sort. Dropping a
// file in there is the whole act of adding a vessel — no list to maintain.
const shipList = fs.readdirSync(path.join(ROOT, 'ships'))
  .filter(f => f.endsWith('.json') && f !== 'index.json')
  .sort()
  .map(f => 'ships/' + f);

/* Write a real ships/index.json.
 * The dev server answers that path from a live directory listing, so it never
 * needed a file — but a plain static host (OVH, Apache, nginx, GitHub Pages)
 * has no such endpoint. Without the file the page falls back to the short list
 * hardcoded in config.js, and any vessel added since simply never gets asked
 * for. That is why freshly added ships and their models vanished once deployed. */
fs.writeFileSync(path.join(ROOT, 'ships', 'index.json'),
                 JSON.stringify(shipList, null, 2) + '\n', 'utf8');
console.log('wrote ships/index.json  (' + shipList.length + ' vessels, for static hosting)');

/* And check the last-resort list in config.js still matches the folder.
 * It is reached only when even index.json is absent, which is precisely the
 * case nobody tests — so it drifts unnoticed. It had lost the cutter and the
 * Roter Löwe for exactly that reason. A warning, not an error: the fallback
 * being stale never breaks a build, only a deployment nobody looks at. */
{
  const cfg = fs.readFileSync(path.join(ROOT, 'js', 'config.js'), 'utf8');
  const m = cfg.match(/SHIPS:\s*\[([^\]]*)\]/);
  const listed = m ? (m[1].match(/'([^']+)'/g) || []).map(s => s.slice(1, -1)).sort() : [];
  const missing = shipList.filter(s => !listed.includes(s));
  const extra   = listed.filter(s => !shipList.includes(s));
  if (missing.length || extra.length) {
    console.warn('  WARNING: Naval.Config.SHIPS has drifted from the ships folder.' +
      (missing.length ? '\n    absent de la liste : ' + missing.join(', ') : '') +
      (extra.length   ? '\n    listé mais introuvable : ' + extra.join(', ') : '') +
      '\n    Sans index.json, un hébergeur statique ne demandera jamais ces navires.');
  }
}

const shipData = {};
for (const rel of shipList) {
  const spec = JSON.parse(fs.readFileSync(path.join(ROOT, rel), 'utf8'));
  /* Carry any .glb's bytes into the page as base64. A published page can
   * neither fetch a local file nor host a .glb as an artifact asset (not an
   * accepted type), so embedding is the only way a model survives publishing.
   * ShipModel parses it from memory — no network involved. */
  if (spec.model && spec.model.glb) {
    const p = path.join(ROOT, spec.model.glb);
    if (fs.existsSync(p)) {
      const bytes = fs.readFileSync(p);
      spec.model.glbBase64 = bytes.toString('base64');
      console.log('  embedded ' + spec.model.glb + '  (' + (bytes.length/1024).toFixed(1) + ' KB)');
    } else {
      console.warn('  WARNING: ' + spec.model.glb + ' is missing — ' + spec.id +
                   ' will fall back to its procedural hull');
    }
  }
  /* A PAINTED SAIL is the one picture this project does not draw for itself,
   * so it is the one that has to be carried in by hand. The rule has not
   * changed — a published page cannot fetch a local image — but the answer
   * differs from the .glb's: rather than adding a second field beside the
   * path, the PATH ITSELF is rewritten to a `data:` URI. ShipModel then hands
   * whatever string it finds to a TextureLoader and never learns which of the
   * two it got: a file on the dev server, the bytes in the built page.
   *
   * The warning matters more than usual here. A missing model falls back to
   * the procedural hull and says so on the console; a missing sail texture
   * would leave the cloth plain, which looks exactly like a sail nobody had
   * painted yet — a failure indistinguishable from the intended state is a
   * failure that ships. */
  const maps = spec.appearance && spec.appearance.canvasMap;
  if (maps) for (const kind of Object.keys(maps)) {
    const rel2 = maps[kind];
    if (/^data:/.test(rel2)) continue;                 // already carried
    const p = path.join(ROOT, rel2);
    const mime = { '.png':'image/png', '.jpg':'image/jpeg', '.jpeg':'image/jpeg',
                   '.webp':'image/webp' }[path.extname(rel2).toLowerCase()];
    if (!mime) {
      console.warn('  WARNING: ' + spec.id + ' voile "' + kind + '" : ' + rel2 +
                   " n'est pas une image embarquable (png, jpg, webp)");
      continue;
    }
    if (!fs.existsSync(p)) {
      console.warn('  WARNING: ' + rel2 + ' is missing — ' + spec.id +
                   ' voile "' + kind + '" restera unie');
      continue;
    }
    const bytes = fs.readFileSync(p);
    maps[kind] = 'data:' + mime + ';base64,' + bytes.toString('base64');
    console.log('  embedded ' + rel2 + '  (voile ' + kind + ', ' +
                (bytes.length/1024).toFixed(1) + ' KB)');
  }

  shipData[rel] = spec;
  inlined.push(rel);
}
/* LES SONS, portés dans la page comme tout le reste. Un fetch() local est
   bloqué par la politique de sécurité, donc les octets voyagent en base64 dans
   Naval.SOUND_DATA et sound.js les décode à la main plutôt que de les
   redemander au réseau — même chemin que les .glb et la voile peinte. */
const SOUND_DIR = path.join(ROOT, 'medias', 'sound');
const soundData = {};
const SOUND_KEYS = { 'cannon_fire_001.wav':'pres', 'cannon_far_away.wav':'loin' };
if (fs.existsSync(SOUND_DIR)) {
  for (const f of fs.readdirSync(SOUND_DIR)) {
    const cle = SOUND_KEYS[f];
    if (!cle) continue;
    const bytes = fs.readFileSync(path.join(SOUND_DIR, f));
    const mime = f.endsWith('.wav') ? 'audio/wav'
               : f.endsWith('.ogg') ? 'audio/ogg' : 'audio/mpeg';
    soundData[cle] = 'data:' + mime + ';base64,' + bytes.toString('base64');
    inlined.push('medias/sound/' + f);
    console.log('  embedded medias/sound/' + f + '  (' + (bytes.length/1024).toFixed(0) + ' KB)');
  }
}
const soundBlob = '<script>\nwindow.Naval = window.Naval || {};\n' +
  'Naval.SOUND_DATA = ' + JSON.stringify(soundData) + ';\n</scr' + 'ipt>\n';

const shipBlob = '<script>\nwindow.Naval = window.Naval || {};\n' +
  'Naval.SHIP_DATA = ' + JSON.stringify(shipData, null, 1) + ';\n</scr' + 'ipt>\n';

/* A STYLESHEET MAY POINT AT FILES OF ITS OWN, and they are blocked exactly as
   a local <script src> is. Same remedy as the painted sail: the PATH itself is
   rewritten to a data: URI, so nothing at run time knows the difference between
   the dev server and the published page — the font face asks for
   fonts/estonia-latin.woff2 in one and carries its own bytes in the other.

   This is NOT the only way to carry a face — the page already links Rajdhani
   and IBM Plex Mono straight from fonts.googleapis.com, and remote references
   are deliberately left alone by the guard below. Carrying the bytes is a
   choice made for a TITLE face, which is the one place where arriving late or
   not at all is read as a fault rather than as a substitution.

   Resolved against the STYLESHEET's own directory, which is what url() means. */
const CSS_MIME = { '.woff2':'font/woff2', '.woff':'font/woff', '.ttf':'font/ttf',
                   '.otf':'font/otf', '.png':'image/png', '.jpg':'image/jpeg',
                   '.jpeg':'image/jpeg', '.gif':'image/gif', '.svg':'image/svg+xml' };
function inlineCssUrls(css, href) {
  const dir = path.dirname(path.join(ROOT, href));
  return css.replace(/url\(\s*(['"]?)([^'")]+)\1\s*\)/g, (m, q, ref) => {
    if (/^(?:data:|https?:|\/\/|#)/.test(ref)) return m;
    const ext = path.extname(ref).toLowerCase();
    const mime = CSS_MIME[ext];
    if (!mime) {
      throw new Error(href + ' points at ' + ref + ', a type this build cannot carry');
    }
    const file = path.join(dir, ref);
    const bytes = fs.readFileSync(file);
    console.log('  embedded ' + path.relative(ROOT, file).split(path.sep).join('/') +
                '  (' + (bytes.length / 1024).toFixed(1) + ' KB)');
    return 'url(data:' + mime + ';base64,' + bytes.toString('base64') + ')';
  });
}

// 1. local stylesheet -> <style>, its own url() carried with it
html = html.replace(/<link\s+rel="stylesheet"\s+href="((?:css|js)\/[^"]+)"\s*>/g, (m, href) => {
  const css = inlineCssUrls(fs.readFileSync(path.join(ROOT, href), 'utf8'), href);
  inlined.push(href);
  return '<style>\n' + css.trimEnd() + '\n</style>';
});

// 2. local scripts → inline <script>. Remote ones (the CDN) are left alone.
html = html.replace(/<script\s+src="([^"]+)"\s*><\/script>/g, (m, src) => {
  if (/^https?:\/\//.test(src)) return m;
  const js = fs.readFileSync(path.join(ROOT, src), 'utf8');
  if (js.includes('</script>')) {
    throw new Error(src + ' contains a literal </script>, which would end the tag early');
  }
  inlined.push(src);
  const tag = '<script>\n' + js.trimEnd() + '\n</script>';
  // the ship data must exist before ShipSpec is asked for it
  return src.endsWith('config.js') ? tag + '\n' + shipBlob + soundBlob : tag;
});

fs.mkdirSync(OUT_DIR, { recursive: true });
fs.writeFileSync(OUT, html, 'utf8');

const kb = n => (n / 1024).toFixed(1) + ' KB';
console.log('inlined ' + inlined.length + ' files:');
for (const f of inlined) console.log('  - ' + f);
console.log('wrote dist/naval-sim.html  (' + kb(Buffer.byteLength(html)) + ')');

// A blocked local <script src> is the exact failure this build prevents, so
// refuse to ship a file that still carries one.
const leftover = html.match(/<(?:script\s+src|link[^>]*href)="(?!https?:)[^"]+"/g);
/* Et un url() local SURVIVANT dans la feuille inlinée est exactement la même
   panne, en plus discret : la page se charge sans rien dire, la fonte ne vient
   pas, et le titre retombe sur une cursive système que personne n'a choisie.
   Un <script src> manquant se voit tout de suite ; une fonte manquante, non. */
const cssLeft = html.match(/url\(\s*['"]?(?!data:|https?:|\/\/|#)[^'")]+\)/g);
if (leftover || cssLeft) {
  console.error('ERROR: local references survived the inline step: ' +
                [].concat(leftover || [], cssLeft || []).join(', '));
  process.exit(1);
}
