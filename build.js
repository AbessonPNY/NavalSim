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
  shipData[rel] = spec;
  inlined.push(rel);
}
const shipBlob = '<script>\nwindow.Naval = window.Naval || {};\n' +
  'Naval.SHIP_DATA = ' + JSON.stringify(shipData, null, 1) + ';\n</scr' + 'ipt>\n';

// 1. local stylesheet → <style>
html = html.replace(/<link\s+rel="stylesheet"\s+href="((?:css|js)\/[^"]+)"\s*>/g, (m, href) => {
  const css = fs.readFileSync(path.join(ROOT, href), 'utf8');
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
  return src.endsWith('config.js') ? tag + '\n' + shipBlob : tag;
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
if (leftover) {
  console.error('ERROR: local references survived the inline step: ' + leftover.join(', '));
  process.exit(1);
}
