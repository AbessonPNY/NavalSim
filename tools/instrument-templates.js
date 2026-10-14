/* Painting templates for the altitude instruments, so a picture of the Davis
 * quadrant or of the cross-staff lines up with what the game moves over it.
 *
 *   node tools/instrument-templates.js
 *
 * Writes instruments/gabarits/gabarit-quartier.png / .svg and
 * gabarit-arbalestrille.png / .svg, at the size instruments/instruments.json
 * gives. Every number comes from that file — the same one SightInstrument.cs
 * reads — so a template can never disagree with the game: move the centre or
 * an arc there, run this again, and paint over the new lines.
 *
 * What must be respected: the centre of the arcs, the radius each vane rides
 * on, and the graduations (the vanes are read where they stand). Everything
 * else — wood, brass, scrollwork, the maker's name — is free.
 */
const fs = require('fs');
const path = require('path');
const { encodeRgbaPng } = require('./grey-png');

const ROOT = path.resolve(__dirname, '..');
const BOOK = JSON.parse(fs.readFileSync(path.join(ROOT, 'instruments', 'instruments.json'), 'utf8'));
const OUT = path.join(ROOT, 'instruments', 'gabarits');
fs.mkdirSync(OUT, { recursive: true });
const RAD = Math.PI / 180;

/* ---- a tiny raster: anti-aliased thick segments, and digits ---- */
function canvas(W, H){
  const px = Buffer.alloc(W*H*4);
  // a faint checker, transparent enough to sit under a layer
  for(let y=0;y<H;y++) for(let x=0;x<W;x++){
    const c = ((x >> 5) + (y >> 5)) % 2 ? 215 : 190, i = (y*W + x)*4;
    px[i]=c; px[i+1]=c; px[i+2]=c; px[i+3]=70;
  }
  const blend = (x, y, [r, g, b], a) => {
    if(x < 0 || y < 0 || x >= W || y >= H || a <= 0) return;
    const i = (y*W + x)*4, A = px[i+3]/255, o = a + A*(1 - a);
    px[i]   = Math.round((r*a + px[i]*A*(1 - a))/o);
    px[i+1] = Math.round((g*a + px[i+1]*A*(1 - a))/o);
    px[i+2] = Math.round((b*a + px[i+2]*A*(1 - a))/o);
    px[i+3] = Math.round(o*255);
  };
  function line(x0, y0, x1, y1, w, col){
    const r = w/2, dx = x1 - x0, dy = y1 - y0, L2 = dx*dx + dy*dy || 1;
    for(let y=Math.floor(Math.min(y0, y1) - r - 1); y<=Math.ceil(Math.max(y0, y1) + r + 1); y++)
      for(let x=Math.floor(Math.min(x0, x1) - r - 1); x<=Math.ceil(Math.max(x0, x1) + r + 1); x++){
        const t = Math.max(0, Math.min(1, ((x + 0.5 - x0)*dx + (y + 0.5 - y0)*dy)/L2));
        const d = Math.hypot(x + 0.5 - x0 - t*dx, y + 0.5 - y0 - t*dy);
        blend(x, y, col, Math.max(0, Math.min(1, r + 0.5 - d)));
      }
  }
  function arc(cx, cy, R, a0, a1, w, col){
    const n = Math.max(8, Math.ceil(Math.abs(a1 - a0)*R*RAD/6));
    for(let k=0;k<n;k++){
      const u = (a0 + (a1 - a0)*k/n)*RAD, v = (a0 + (a1 - a0)*(k + 1)/n)*RAD;
      line(cx + R*Math.cos(u), cy + R*Math.sin(u), cx + R*Math.cos(v), cy + R*Math.sin(v), w, col);
    }
  }
  const FONT = { '0':'01110100011001110101110011000101110','1':'00100011000010000100001000010001110',
    '2':'01110100010000100010001000100011111','3':'11111000100010000010000011000101110',
    '4':'00010001100101010010111110001000010','5':'11111100001111000001000011000101110',
    '6':'00110010001000011110100011000101110','7':'11111000010001000100010000100001000',
    '8':'01110100011000101110100011000101110','9':'01110100011000101111000010001001100' };
  // digits centred on (cx, cy), each cell s pixels
  function text(str, cx, cy, s, col){
    const w = str.length*6*s - s, x0 = Math.round(cx - w/2), y0 = Math.round(cy - 3.5*s);
    [...str].forEach((ch, j) => {
      const g = FONT[ch]; if(!g) return;
      for(let r=0;r<7;r++) for(let c=0;c<5;c++) if(g[r*5 + c] === '1')
        for(let yy=0;yy<s;yy++) for(let xx=0;xx<s;xx++) blend(x0 + j*6*s + c*s + xx, y0 + r*s + yy, col, 1);
    });
  }
  return { px, line, arc, text, save: f => fs.writeFileSync(f, encodeRgbaPng(W, H, px)) };
}

const INK = [40, 25, 10], RED = [200, 30, 40], BLUE = [40, 110, 220], GOLD = [200, 140, 20];
const hex = c => '#' + c.map(v => v.toString(16).padStart(2, '0')).join('');

/* ---- the Davis quadrant ---- */
function quadrant(q){
  const [W, H] = q.taille, [cx, cy] = q.centre;
  const cv = canvas(W, H), svg = [];
  const at = (R, a) => [cx + R*Math.cos(a*RAD), cy + R*Math.sin(a*RAD)];
  const ang = (arcDef, d) => arcDef.zero + (arcDef.plein - arcDef.zero)*d/arcDef.degres;
  for(const [arcDef, col, name] of [[q.petitArc, GOLD, 'petit arc — pinnule d’ombre'], [q.grandArc, RED, 'grand arc — pinnule de visée']]){
    const R = arcDef.rayon, a0 = ang(arcDef, 0), a1 = ang(arcDef, arcDef.degres);
    // the radius the vane rides on, and the two radii to its ends
    cv.arc(cx, cy, R, a0, a1, 4, col);
    for(const a of [a0, a1]){ const [x, y] = at(R, a); cv.line(cx, cy, x, y, 2, [150, 150, 150]); }
    const big = 4;
    for(let d=0; d<=arcDef.degres; d++){
      const a = ang(arcDef, d), len = d % 10 === 0 ? 50 : d % 5 === 0 ? 34 : 18;
      const [x0, y0] = at(R, a), [x1, y1] = at(R - len, a);
      cv.line(x0, y0, x1, y1, d % 5 === 0 ? 4 : 2, INK);
      if(d % (arcDef.degres > 30 ? 10 : 5) === 0){
        const [tx, ty] = at(R + 30 + 6*big*String(d).length, a);
        cv.text(String(d), tx, ty, big, INK);
        svg.push(`<text x="${tx.toFixed(1)}" y="${(ty + 10).toFixed(1)}" font-family="serif" font-size="30" text-anchor="middle" fill="${hex(INK)}">${d}</text>`);
      }
    }
    const pts = []; for(let k=0;k<=96;k++){ const [x, y] = at(R, a0 + (a1 - a0)*k/96); pts.push(x.toFixed(1) + ',' + y.toFixed(1)); }
    svg.push(`<polyline points="${pts.join(' ')}" fill="none" stroke="${hex(col)}" stroke-width="4"/>`);
    for(const a of [a0, a1]){ const [x, y] = at(R, a); svg.push(`<line x1="${cx}" y1="${cy}" x2="${x.toFixed(1)}" y2="${y.toFixed(1)}" stroke="#969696" stroke-width="2"/>`); }
    for(let d=0; d<=arcDef.degres; d++){
      const a = ang(arcDef, d), len = d % 10 === 0 ? 50 : d % 5 === 0 ? 34 : 18;
      const [x0, y0] = at(R, a), [x1, y1] = at(R - len, a);
      svg.push(`<line x1="${x0.toFixed(1)}" y1="${y0.toFixed(1)}" x2="${x1.toFixed(1)}" y2="${y1.toFixed(1)}" stroke="${hex(INK)}" stroke-width="${d % 5 === 0 ? 4 : 2}"/>`);
    }
    const [lx, ly] = at(R*0.55, (a0 + a1)/2);
    svg.push(`<text x="${lx.toFixed(1)}" y="${ly.toFixed(1)}" font-family="sans-serif" font-size="26" text-anchor="middle" fill="${hex(col)}">${name} · rayon ${R}</text>`);
  }
  // the centre: the horizon vane and its slit
  cv.line(cx - 40, cy, cx + 40, cy, 3, BLUE); cv.line(cx, cy - 40, cx, cy + 40, 3, BLUE);
  svg.push(`<line x1="${cx - 40}" y1="${cy}" x2="${cx + 40}" y2="${cy}" stroke="${hex(BLUE)}" stroke-width="3"/>`,
           `<line x1="${cx}" y1="${cy - 40}" x2="${cx}" y2="${cy + 40}" stroke="${hex(BLUE)}" stroke-width="3"/>`,
           `<text x="${cx + 50}" y="${cy - 50}" font-family="sans-serif" font-size="26" fill="${hex(BLUE)}">centre : pinnule d’horizon (la fente)</text>`);
  cv.save(path.join(OUT, 'gabarit-quartier.png'));
  fs.writeFileSync(path.join(OUT, 'gabarit-quartier.svg'), page(W, H, 'Gabarit du quartier de Davis', svg,
    `quartier de Davis · ${W}×${H} px · les pinnules glissent sur les arcs de couleur, lues sur les traits`));
  console.log(`wrote instruments/gabarits/gabarit-quartier.png / .svg  (${W}×${H})`);
}

/* ---- the cross-staff ---- */
function crossStaff(x){
  const [W, H] = x.taille, [ex, ey] = x.oeil, [fx, fy] = x.bout;
  const L = Math.hypot(fx - ex, fy - ey), dx = (fx - ex)/L, dy = (fy - ey)/L, nx = -dy, ny = dx;
  const half = L*Math.tan(x.de/2*RAD);                          // the transom that puts « de » at the far end
  const pos = d => { const s = half/Math.tan(d/2*RAD); return [ex + dx*s, ey + dy*s]; };
  const cv = canvas(W, H), svg = [];
  cv.line(ex, ey, fx, fy, 3, RED);
  svg.push(`<line x1="${ex}" y1="${ey}" x2="${fx}" y2="${fy}" stroke="${hex(RED)}" stroke-width="3"/>`);
  for(let d=Math.ceil(x.de); d<=x.a; d++){
    const [px, py] = pos(d), len = d % 10 === 0 ? 46 : d % 5 === 0 ? 30 : 16;
    cv.line(px, py, px - nx*len, py - ny*len, d % 5 === 0 ? 4 : 2, INK);
    svg.push(`<line x1="${px.toFixed(1)}" y1="${py.toFixed(1)}" x2="${(px - nx*len).toFixed(1)}" y2="${(py - ny*len).toFixed(1)}" stroke="${hex(INK)}" stroke-width="${d % 5 === 0 ? 4 : 2}"/>`);
    if(d % 10 === 0 || (d % 5 === 0 && d < 30)){
      cv.text(String(d), px + nx*36, py + ny*36, 3, INK);
      svg.push(`<text x="${(px + nx*36).toFixed(1)}" y="${(py + ny*36 + 10).toFixed(1)}" font-family="serif" font-size="28" text-anchor="middle" fill="${hex(INK)}">${d}</text>`);
    }
  }
  // the transom's reach, drawn at the far end where it is longest in the picture
  for(const d of [x.de, 20]){
    const [px, py] = pos(d);
    cv.line(px - nx*half, py - ny*half, px + nx*half, py + ny*half, 2, GOLD);
    svg.push(`<line x1="${(px - nx*half).toFixed(1)}" y1="${(py - ny*half).toFixed(1)}" x2="${(px + nx*half).toFixed(1)}" y2="${(py + ny*half).toFixed(1)}" stroke="${hex(GOLD)}" stroke-width="2" stroke-dasharray="10 8"/>`);
  }
  cv.line(ex, ey - 30, ex, ey + 30, 3, BLUE);
  svg.push(`<line x1="${ex}" y1="${ey - 30}" x2="${ex}" y2="${ey + 30}" stroke="${hex(BLUE)}" stroke-width="3"/>`,
           `<text x="${ex + 10}" y="${ey - 40}" font-family="sans-serif" font-size="24" fill="${hex(BLUE)}">œil</text>`,
           `<text x="${(ex + fx)/2}" y="${H - 16}" font-family="sans-serif" font-size="22" text-anchor="middle" fill="${hex(GOLD)}">en or : le marteau (${(2*half).toFixed(0)} px de long), posé à ${x.de}° et à 20°</text>`);
  cv.save(path.join(OUT, 'gabarit-arbalestrille.png'));
  fs.writeFileSync(path.join(OUT, 'gabarit-arbalestrille.svg'), page(W, H, 'Gabarit de l’arbalestrille', svg,
    `arbalestrille · ${W}×${H} px · l’axe du bâton en rouge, graduations de ${x.de}° à ${x.a}°`));
  console.log(`wrote instruments/gabarits/gabarit-arbalestrille.png / .svg  (${W}×${H})`);
}

function page(W, H, title, body, note){
  return `<svg xmlns="http://www.w3.org/2000/svg" width="${W}" height="${H}" viewBox="0 0 ${W} ${H}">
  <title>${title}</title>
  <rect width="${W}" height="${H}" fill="#bbb" fill-opacity="0.25"/>
  ${body.join('\n  ')}
  <text x="12" y="30" font-family="sans-serif" font-size="22" fill="#666">${note}</text>
</svg>
`;
}

/* ---- the nocturnal: the plate (date ring), the hour disk, the arm ---- */
const MONTHS = ['janvier','février','mars','avril','mai','juin','juillet','août','septembre','octobre','novembre','décembre'];
const MDAYS = [31,28,31,30,31,30,31,31,30,31,30,31];
function nocturnal(n){
  const [W, H] = n.taille, [cx, cy] = n.centre, [r0, r1] = n.dates;
  // the same rule as SightInstrument.Book.DateAngle: screen degrees, the days going anticlockwise
  const dateAngle = doy => n.janvier - doy*360/365.2422;
  const at = (R, a) => [cx + R*Math.cos(a*RAD), cy + R*Math.sin(a*RAD)];
  { // the plate
    const cv = canvas(W, H), svg = [];
    const circle = (R, col, w) => { cv.arc(cx, cy, R, 0, 360, w, col); svg.push(`<circle cx="${cx}" cy="${cy}" r="${R}" fill="none" stroke="${hex(col)}" stroke-width="${w}"/>`); };
    circle(n.rayon, INK, 4); circle(r1, RED, 3); circle(r0, RED, 3); circle(n.rayonHeures, BLUE, 2); circle(26, INK, 3);
    let doy = 0;
    for(let m=0;m<12;m++){
      for(let d=0; d<MDAYS[m]; d++, doy++){
        const a = dateAngle(doy), len = d === 0 ? 1 : (d + 1) % 10 === 0 ? 0.42 : (d + 1) % 5 === 0 ? 0.3 : 0.16;
        const [x0, y0] = at(r1, a), [x1, y1] = at(r1 - (r1 - r0)*len, a);
        cv.line(x0, y0, x1, y1, d === 0 ? 4 : 2, INK);
        svg.push(`<line x1="${x0.toFixed(1)}" y1="${y0.toFixed(1)}" x2="${x1.toFixed(1)}" y2="${y1.toFixed(1)}" stroke="${hex(INK)}" stroke-width="${d === 0 ? 4 : 2}"/>`);
      }
      const [tx, ty] = at((r0 + r1)/2 - 8, dateAngle(doy - MDAYS[m]/2));
      cv.text(String(m + 1), tx, ty, 3, INK);
      svg.push(`<text x="${tx.toFixed(1)}" y="${(ty + 8).toFixed(1)}" font-family="serif" font-size="22" text-anchor="middle" fill="${hex(INK)}">${MONTHS[m]}</text>`);
    }
    // the handle, held at the bottom
    const hw = 70, hy0 = cy + n.rayon*0.8, hy1 = H - 30;
    for(const [x0, y0, x1, y1] of [[cx - hw, hy0, cx - hw, hy1], [cx + hw, hy0, cx + hw, hy1], [cx - hw, hy1, cx + hw, hy1]]){
      cv.line(x0, y0, x1, y1, 3, GOLD);
      svg.push(`<line x1="${x0}" y1="${y0}" x2="${x1}" y2="${y1}" stroke="${hex(GOLD)}" stroke-width="3"/>`);
    }
    svg.push(`<text x="${cx}" y="${hy1 - 20}" font-family="sans-serif" font-size="22" text-anchor="middle" fill="${hex(GOLD)}">le manche</text>`,
             `<text x="${cx}" y="${cy - n.rayonHeures + 40}" font-family="sans-serif" font-size="20" text-anchor="middle" fill="${hex(BLUE)}">en bleu : le disque des heures, posé par-dessus</text>`);
    cv.save(path.join(OUT, 'gabarit-nocturlabe-disque.png'));
    fs.writeFileSync(path.join(OUT, 'gabarit-nocturlabe-disque.svg'), page(W, H, 'Gabarit du nocturlabe — la plaque', svg,
      `nocturlabe, la plaque · ${W}×${H} px · l’anneau des dates entre les cercles rouges, le 1er de chaque mois au trait long`));
  }
  { // the hour disk, midnight up
    const S = Math.ceil(2*(r1 + 10)), c = S/2, cv = canvas(S, S), svg = [];
    const at2 = (R, a) => [c + R*Math.cos(a*RAD), c + R*Math.sin(a*RAD)];
    cv.arc(c, c, n.rayonHeures, 0, 360, 3, BLUE); svg.push(`<circle cx="${c}" cy="${c}" r="${n.rayonHeures}" fill="none" stroke="${hex(BLUE)}" stroke-width="3"/>`);
    for(let q=0;q<96;q++){
      const a = -90 - q*3.75, len = q % 4 === 0 ? 0.13 : q % 2 === 0 ? 0.07 : 0.04;
      const [x0, y0] = at2(n.rayonHeures, a), [x1, y1] = at2(n.rayonHeures*(1 - len), a);
      cv.line(x0, y0, x1, y1, q % 4 === 0 ? 3 : 1.5, INK);
      svg.push(`<line x1="${x0.toFixed(1)}" y1="${y0.toFixed(1)}" x2="${x1.toFixed(1)}" y2="${y1.toFixed(1)}" stroke="${hex(INK)}" stroke-width="${q % 4 === 0 ? 3 : 1.5}"/>`);
      if(q % 4) continue;
      const h = q/4, label = String(h % 12 === 0 ? 12 : h % 12);
      const [tx, ty] = at2(n.rayonHeures*0.76, a);
      cv.text(label, tx, ty, 3, INK);
      svg.push(`<text x="${tx.toFixed(1)}" y="${(ty + 8).toFixed(1)}" font-family="serif" font-size="24" text-anchor="middle" fill="${hex(INK)}">${label}</text>`);
    }
    const [mx, my] = at2(r1 - 4, -90);
    cv.line(c, c - n.rayonHeures, mx, my, 5, RED);
    svg.push(`<line x1="${c}" y1="${c - n.rayonHeures}" x2="${mx}" y2="${my}" stroke="${hex(RED)}" stroke-width="5"/>`,
             `<text x="${c + 16}" y="${my + 30}" font-family="sans-serif" font-size="22" fill="${hex(RED)}">dent de minuit (en haut)</text>`);
    cv.save(path.join(OUT, 'gabarit-nocturlabe-heures.png'));
    fs.writeFileSync(path.join(OUT, 'gabarit-nocturlabe-heures.svg'), page(S, S, 'Gabarit du nocturlabe — le disque des heures', svg,
      `les heures · ${S}×${S} px, centré · minuit en haut, les heures vers la gauche (sens inverse des aiguilles)`));
  }
  { // the arm, pivot at the centre, pointing up
    const S = Math.ceil(2*n.longueurBras + 20), c = S/2, cv = canvas(S, S), svg = [];
    cv.line(c, c, c, c - n.longueurBras, 4, RED);
    cv.line(c - 30, c, c + 30, c, 3, BLUE); cv.line(c, c - 30, c, c + 30, 3, BLUE);
    svg.push(`<line x1="${c}" y1="${c}" x2="${c}" y2="${c - n.longueurBras}" stroke="${hex(RED)}" stroke-width="4"/>`,
             `<circle cx="${c}" cy="${c}" r="26" fill="none" stroke="${hex(BLUE)}" stroke-width="3"/>`,
             `<text x="${c + 20}" y="${c + 60}" font-family="sans-serif" font-size="24" fill="${hex(BLUE)}">le pivot (creux : on y voit la Polaire)</text>`,
             `<text x="${c + 20}" y="${c - n.longueurBras/2}" font-family="sans-serif" font-size="24" fill="${hex(RED)}">le bord de lecture du bras, ${n.longueurBras} px</text>`);
    cv.save(path.join(OUT, 'gabarit-nocturlabe-bras.png'));
    fs.writeFileSync(path.join(OUT, 'gabarit-nocturlabe-bras.svg'), page(S, S, 'Gabarit du nocturlabe — le bras', svg,
      `le bras · ${S}×${S} px · pivot au centre, pointé vers le haut ; le reste de l’image transparent`));
  }
  console.log('wrote instruments/gabarits/gabarit-nocturlabe-disque / -heures / -bras (.png / .svg)');
}

/* ---- the pilot's book: where each page is written ---- */
function book(l){
  const [W, H] = l.taille, cv = canvas(W, H), svg = [];
  for(const [r, name] of [[l.gauche, 'page de gauche : la table'], [l.droite, 'page de droite : le calcul']]){
    const [x, y, w, h] = r;
    for(const [x0, y0, x1, y1] of [[x, y, x + w, y], [x + w, y, x + w, y + h], [x + w, y + h, x, y + h], [x, y + h, x, y]]) cv.line(x0, y0, x1, y1, 4, RED);
    svg.push(`<rect x="${x}" y="${y}" width="${w}" height="${h}" fill="none" stroke="${hex(RED)}" stroke-width="4"/>`,
             `<text x="${x + w/2}" y="${y + h/2}" font-family="sans-serif" font-size="30" text-anchor="middle" fill="${hex(RED)}">${name}</text>`);
  }
  cv.save(path.join(OUT, 'gabarit-livre.png'));
  fs.writeFileSync(path.join(OUT, 'gabarit-livre.svg'), page(W, H, 'Gabarit du livre du pilote', svg,
    `livre du pilote · ${W}×${H} px · le jeu écrit dans les rectangles rouges ; tout le reste est à peindre`));
  console.log(`wrote instruments/gabarits/gabarit-livre.png / .svg  (${W}×${H})`);
}

quadrant(BOOK.quartier);
crossStaff(BOOK.arbalestrille);
nocturnal(BOOK.nocturlabe);
book(BOOK.livre);
