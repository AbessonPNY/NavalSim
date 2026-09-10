// Minimal static server for this project (a single-page WebGL simulation).
// No dependencies — plain Node, so it runs without an install step.
//
// The charset header is kept although the page now declares its own <meta
// charset> — belt and braces. It did NOT declare one for a long time, on the
// grounds that the Artifact host supplies it, and that held until the page was
// put on an ordinary host: serving it without "charset=utf-8" mangles
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

  /* ÉCRIRE une capture dans le dossier du projet.

     C'est la seule chose que ce serveur fasse en plus de servir des fichiers,
     et il y a une raison : une page ne peut pas écrire sur le disque, alors
     que ce serveur sert déjà ce dossier-là. La capture atterrit donc à côté
     du code qui l'a produite, ce qu'on veut quand elle sert à comparer avec la
     précédente. */
  if (req.method === 'POST' && rel === '/capture') {
    let body = '';
    req.setEncoding('utf8');
    req.on('data', c => {
      body += c;
      if (body.length > 48e6) { req.destroy(); }   // une image, pas un disque dur
    });
    req.on('end', () => {
      try {
        const b64 = body.replace(/^data:image\/png;base64,/, '');
        /* Et le serveur refuse aussi. Une image plausible n'est pas minuscule,
           et le premier essai a écrit un PNG de TROIS octets en annonçant que
           tout allait bien — le canvas n'avait pas de surface. Un outil qui
           ment sur ce qu'il vient d'écrire est pire que pas d'outil. */
        if (b64.length < 1024) {
          res.writeHead(400, {'Content-Type':'text/plain; charset=utf-8'});
          res.end('image vide (' + b64.length + ' octets encodes) - rien ecrit');
          return;
        }
        const dir = path.join(ROOT, 'captures');
        fs.mkdirSync(dir, { recursive: true });
        const d = new Date(), p2 = n => String(n).padStart(2, '0');
        const stamp = d.getFullYear() + p2(d.getMonth()+1) + p2(d.getDate())
                    + '-' + p2(d.getHours()) + p2(d.getMinutes()) + p2(d.getSeconds());
        // deux captures dans la même seconde ne doivent pas s'écraser
        let name = 'naval-' + stamp + '.png', n = 1;
        while (fs.existsSync(path.join(dir, name))) name = 'naval-' + stamp + '-' + (++n) + '.png';
        fs.writeFileSync(path.join(dir, name), Buffer.from(b64, 'base64'));
        console.log('capture : captures/' + name);
        res.writeHead(200, {'Content-Type':'application/json; charset=utf-8'});
        res.end(JSON.stringify({ file: 'captures/' + name }));
      } catch (e) {
        res.writeHead(500, {'Content-Type':'text/plain; charset=utf-8'});
        res.end(String((e && e.message) || e));
      }
    });
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
