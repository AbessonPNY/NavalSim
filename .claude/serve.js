// Minimal static server for this project (a single-page WebGL simulation).
// No dependencies — plain Node, so it runs without an install step.
//
// The charset matters: naval-sim.html has no <meta charset> of its own (the
// Artifact host supplies one), so serving it without "charset=utf-8" mangles
// every accented character in the French UI.

const http = require('http');
const fs = require('fs');
const path = require('path');

const ROOT = path.resolve(__dirname, '..');
const ENTRY = 'naval-sim.html';
const PORT = Number(process.argv[2] || process.env.PORT || 8765);

const TYPES = {
  '.html': 'text/html; charset=utf-8',
  '.js':   'text/javascript; charset=utf-8',
  '.css':  'text/css; charset=utf-8',
  '.json': 'application/json; charset=utf-8',
  '.svg':  'image/svg+xml',
  '.png':  'image/png',
  '.jpg':  'image/jpeg',
  '.ico':  'image/x-icon',
};

http.createServer((req, res) => {
  let rel = decodeURIComponent(req.url.split('?')[0]);
  if (rel === '/' || rel === '') rel = '/' + ENTRY;

  /* Live index of the ships folder, so dropping a spec in there is enough —
     no list to edit, no build step. Computed per request, never cached. */
  if (rel === '/ships/index.json') {
    let list = [];
    try {
      list = fs.readdirSync(path.join(ROOT, 'ships'))
               .filter(f => f.endsWith('.json') && f !== 'index.json')
               .sort()
               .map(f => 'ships/' + f);
    } catch (e) { /* no ships folder yet — an empty list is a fine answer */ }
    res.writeHead(200, {'Content-Type':'application/json; charset=utf-8','Cache-Control':'no-store'});
    res.end(JSON.stringify(list));
    return;
  }

  // keep requests inside the project directory
  const file = path.resolve(ROOT, '.' + rel);
  if (!file.startsWith(ROOT)) {
    res.writeHead(403).end('forbidden');
    return;
  }

  fs.readFile(file, (err, data) => {
    if (err) {
      res.writeHead(404, { 'Content-Type': 'text/plain; charset=utf-8' });
      res.end('not found: ' + rel);
      return;
    }
    res.writeHead(200, {
      'Content-Type': TYPES[path.extname(file).toLowerCase()] || 'application/octet-stream',
      'Cache-Control': 'no-store',   // always serve the latest edit
    });
    res.end(data);
  });
}).listen(PORT, () => {
  console.log('naval-sim serving ' + ROOT + ' on http://localhost:' + PORT + '/');
});
