/* LES RIDES DE LA MER, TIRÉES D'UN VRAI SPECTRE DE VAGUES DE VENT.
 *
 *   node tools/sea-ripples.js                       # world/textures/mer/rides.png
 *   node tools/sea-ripples.js --vent 7 --taille 10 --images 32 --periode 12
 *
 * La houle du jeu n'a que dix-huit longues vagues ; tout ce qui est plus court que la plus
 * courte d'entre elles (1,7 m par force 3, 6 m par force 5, 23 m par force 9) vit dans la
 * NORMALE de la surface. Le bruit de Perlin la donnait d'une seule échelle. Ici la méthode de
 * Tessendorf (« Simulating Ocean Water », 2001) : un spectre de Phillips orienté par le vent,
 * des amplitudes complexes tirées au hasard (gaussiennes), chacune tournant à la pulsation
 * des vagues d'eau profonde (ω² = g k), et la transformée de Fourier inverse de leurs PENTES.
 *
 * CARRELABLE, puisque la transformée l'est ; BOUCLÉE DANS LE TEMPS : chaque pulsation est
 * arrondie à un multiple de 2π / période, si bien que l'image d'après la dernière est la
 * première. Le jeu interpole entre deux images voisines.
 *
 * CE QUI EST ÉCRIT : les pentes ∂h/∂x (le long du vent) et ∂h/∂z (en travers), en R et G,
 * 128 pour zéro, ± pente_max aux extrêmes. Les images en mosaïque, huit par ligne. Leur force
 * est calée sur Cox et Munk (1954), la mesure de la pente de la mer au soleil : une moyenne
 * des carrés σ² = 0,003 + 0,00512 U — à 7 m/s, une pente moyenne de 0,2. Le jeu la module
 * ensuite avec le vent du moment (u_ripple).
 *
 * Écrit aussi l'entrée « rides » à reporter dans world/materiaux.json → « mer ».
 */
'use strict';
const fs = require('fs');
const path = require('path');
const { encodeRgbaPng } = require('./grey-png.js');

const args = process.argv.slice(2);
const opt = (k, d) => { const i = args.indexOf(k); return i >= 0 ? Number(args[i + 1]) : d; };
const N = opt('--n', 256);                 // côté d'une image, en texels (puissance de deux)
const SIZE = opt('--taille', 10);          // mètres couverts par une répétition
const FRAMES = opt('--images', 32);
const PERIOD = opt('--periode', 12);       // secondes avant que l'image revienne
const WIND = opt('--vent', 7);             // m/s : la mer que l'image fige
const SEED = opt('--graine', 1690);
const OUT = path.join(__dirname, '..', 'world', 'textures', 'mer', 'rides.png');
const G = 9.81;

// un hasard reproductible : la même graine, la même mer
let st = SEED >>> 0;
const rnd = () => { st = (st * 1664525 + 1013904223) >>> 0; return (st + 0.5) / 4294967296; };
const gauss = () => Math.sqrt(-2 * Math.log(rnd())) * Math.cos(2 * Math.PI * rnd());

/* LE SPECTRE DE PHILLIPS : A · e^(−1/(kL)²) / k⁴ · |k̂·ŵ|², L = V²/g la plus grande vague que ce
   vent lève ; les plus courtes que deux centimètres effacées (là commencent les capillaires,
   qu'aucune pente de texture ne rendrait). Le vent souffle vers +x ; ce qui remonte contre lui
   est réduit au quart, et la puissance 4 du cosinus resserre les crêtes en travers du vent. */
const L = WIND * WIND / G, small = 0.02;
function phillips(kx, kz) {
  const k2 = kx * kx + kz * kz;
  if (k2 < 1e-12) return 0;
  const k = Math.sqrt(k2), c = kx / k;
  let dir = c * c * c * c;
  if (c < 0) dir *= 0.25;
  return Math.exp(-1 / (k2 * L * L)) / (k2 * k2) * dir * Math.exp(-k2 * small * small);
}

// h0(k) : les amplitudes complexes, une fois pour toutes
const dk = 2 * Math.PI / SIZE, w0 = 2 * Math.PI / PERIOD;
const H0r = new Float64Array(N * N), H0i = new Float64Array(N * N), OM = new Float64Array(N * N);
const KX = new Float64Array(N * N), KZ = new Float64Array(N * N);
for (let j = 0; j < N; j++)
  for (let i = 0; i < N; i++) {
    const n = i < N / 2 ? i : i - N, m = j < N / 2 ? j : j - N;
    const kx = n * dk, kz = m * dk, id = j * N + i;
    const a = Math.sqrt(phillips(kx, kz) / 2);
    H0r[id] = gauss() * a; H0i[id] = gauss() * a;
    KX[id] = kx; KZ[id] = kz;
    // la pulsation d'eau profonde, arrondie au multiple de 2π/période : la boucle se referme
    OM[id] = Math.round(Math.sqrt(G * Math.hypot(kx, kz)) / w0) * w0;
  }
const mirror = id => { const i = id % N, j = (id / N) | 0; return ((N - j) % N) * N + (N - i) % N; };

// la transformée de Fourier inverse, en place, ligne puis colonne (radix 2)
function fft1(re, im, off, stride) {
  for (let i = 1, j = 0; i < N; i++) {
    let bit = N >> 1;
    for (; j & bit; bit >>= 1) j ^= bit;
    j ^= bit;
    if (i < j) {
      const a = off + i * stride, b = off + j * stride;
      [re[a], re[b]] = [re[b], re[a]]; [im[a], im[b]] = [im[b], im[a]];
    }
  }
  for (let len = 2; len <= N; len <<= 1) {
    const ang = 2 * Math.PI / len, wr = Math.cos(ang), wi = Math.sin(ang);
    for (let s = 0; s < N; s += len) {
      let cr = 1, ci = 0;
      for (let k = 0; k < len / 2; k++) {
        const a = off + (s + k) * stride, b = off + (s + k + len / 2) * stride;
        const tr = re[b] * cr - im[b] * ci, ti = re[b] * ci + im[b] * cr;
        re[b] = re[a] - tr; im[b] = im[a] - ti; re[a] += tr; im[a] += ti;
        const nr = cr * wr - ci * wi; ci = cr * wi + ci * wr; cr = nr;
      }
    }
  }
}
function ifft2(re, im) {
  for (let j = 0; j < N; j++) fft1(re, im, j * N, 1);
  for (let i = 0; i < N; i++) fft1(re, im, i, N);
}

// les pentes de chaque image
const slopes = [];
let sum2 = 0;
for (let f = 0; f < FRAMES; f++) {
  const t = f * PERIOD / FRAMES;
  const Xr = new Float64Array(N * N), Xi = new Float64Array(N * N);
  const Zr = new Float64Array(N * N), Zi = new Float64Array(N * N);
  for (let id = 0; id < N * N; id++) {
    const mi = mirror(id), c = Math.cos(OM[id] * t), s = Math.sin(OM[id] * t);
    // h(k,t) = h0(k) e^{iωt} + conj(h0(−k)) e^{−iωt}
    const hr = H0r[id] * c - H0i[id] * s + H0r[mi] * c - H0i[mi] * s;
    const hi = H0r[id] * s + H0i[id] * c - (H0r[mi] * s + H0i[mi] * c);
    // la pente : i k h
    Xr[id] = -KX[id] * hi; Xi[id] = KX[id] * hr;
    Zr[id] = -KZ[id] * hi; Zi[id] = KZ[id] * hr;
  }
  ifft2(Xr, Xi); ifft2(Zr, Zi);
  for (let id = 0; id < N * N; id++) sum2 += Xr[id] * Xr[id] + Zr[id] * Zr[id];
  slopes.push([Xr, Zr]);
}
// la force de Cox et Munk pour ce vent : la moyenne des carrés des pentes
const mss = 0.003 + 0.00512 * WIND;
const gain = Math.sqrt(mss / (sum2 / (FRAMES * N * N)));
const SMAX = 4 * Math.sqrt(mss / 2);       // quatre écarts types par composante : presque rien n'est écrêté
let clipped = 0;

// la mosaïque : huit images par ligne
const COLS = Math.min(8, FRAMES), ROWS = Math.ceil(FRAMES / COLS);
const W = COLS * N, H = ROWS * N;
const px = new Uint8Array(W * H * 4);
for (let f = 0; f < FRAMES; f++) {
  const [X, Z] = slopes[f], ox = (f % COLS) * N, oy = ((f / COLS) | 0) * N;
  for (let j = 0; j < N; j++)
    for (let i = 0; i < N; i++) {
      const id = j * N + i, o = ((oy + j) * W + ox + i) * 4;
      const enc = v => { const q = v * gain / SMAX; if (Math.abs(q) > 1) clipped++; return Math.round(128 + Math.max(-1, Math.min(1, q)) * 127); };
      px[o] = enc(X[id]); px[o + 1] = enc(Z[id]); px[o + 2] = 128; px[o + 3] = 255;
    }
}
fs.mkdirSync(path.dirname(OUT), { recursive: true });
fs.writeFileSync(OUT, encodeRgbaPng(W, H, px));
const rel = path.relative(path.join(__dirname, '..'), OUT).split(path.sep).join('/');
console.log(`${rel} : ${FRAMES} images de ${N}² en ${COLS}×${ROWS}, ${SIZE} m par répétition, boucle de ${PERIOD} s,`);
console.log(`  vent de ${WIND} m/s (plus grande vague ${L.toFixed(1)} m), pente moyenne ${Math.sqrt(mss).toFixed(3)} (Cox et Munk), écrêtés ${(100 * clipped / (2 * FRAMES * N * N)).toFixed(3)} %`);
console.log('  à reporter dans world/materiaux.json → « mer » :');
console.log('  ' + JSON.stringify({ rides: { carte: rel, images: FRAMES, periode: PERIOD, taille: SIZE, pente_max: +SMAX.toFixed(4), vent: WIND } }));
