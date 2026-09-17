/* The weather one feels: how cold it is, and whether it is coming down.
 *
 * The TEMPERATURE is its own climate, set in settings.json, and deliberately
 * not derived from the sun's latitude: the islands sit in the Windwards for the
 * sun's sake, but a winter with snow was wanted, so the air is given a
 * temperate year — the Channel's, by default — while the sun keeps its
 * Caribbean course. A mean, a seasonal swing coldest at the end of January, a
 * daily swing warmest mid-afternoon, a day-to-day wander that is the same for
 * the same date, and the chill of a gale or a shower on top.
 *
 * SHOWERS come and go at random, in game hours — so pressing time brings them
 * as often per day as ever — each with its own strength and length, easing in
 * and out. What falls is snow below `snowBelow`, rain above.
 *
 * It is spoken, not measured: there was no thermometer at sea in 1598.
 */
window.Naval = window.Naval || {};

Naval.CLIMATE = {
  mean: 9.5,              // °C, the year's average
  seasonal: 9,            // ± over the year: about −2..3 °C at the end of January
  daily: 3,               // ± over the day
  wander: 3,              // ± from one day to the next
  coldestDay: 30,         // day of the year (30 January)
  showersPerDay: 3,       // how many showers a day brings, on average
  showerMinutes: [20, 90],// how long one lasts, in game minutes
  snowBelow: 1.5,         // °C: colder than this, it snows
  words: [                // [upper bound °C, word] — the first that fits
    [-5, 'Froid glacial'], [0, 'Gel'], [5, 'Froid mordant'], [10, 'Frais'],
    [16, 'Doux'], [22, 'Tiède'], [27, 'Chaud'], [99, 'Chaleur lourde']
  ]
};

Naval.Climate = class Climate {
  constructor(){
    this.shower = null;    // { t, len, peak } in game hours
    this.amount = 0;       // 0..1, what the shower puts down just now
    this.temp = 10;
  }

  // a stable pseudo-random number for a given day
  _h(n){
    let x = (n*2654435761) >>> 0;
    x ^= x >>> 13; x = Math.imul(x, 0x5bd1e995) >>> 0; x ^= x >>> 15;
    return x/4294967296;
  }

  /* dtHours: how far the day clock moved. storm: 0..1, how hard the
     depression or the gale is blowing. Returns nothing; read temp, amount. */
  update(dtHours, calendar, hour, storm){
    const K = Naval.CLIMATE;
    // --- showers ---
    if(this.shower){
      this.shower.t += dtHours;
      const u = this.shower.t/this.shower.len;
      if(u >= 1){ this.shower = null; this.amount = 0; }
      else this.amount = this.shower.peak*Math.sin(Math.PI*u);   // in, and out again
    }else if(dtHours > 0 && Math.random() < K.showersPerDay*dtHours/24){
      const [a, b] = K.showerMinutes;
      this.shower = { t:0, len:(a + Math.random()*(b - a))/60, peak:0.35 + Math.random()*0.65 };
    }

    // --- temperature ---
    const D = calendar.date();
    const jan1 = new Date(Date.UTC(2000, 0, 1)); jan1.setUTCFullYear(D.getUTCFullYear());
    const doy = (D.getTime() - jan1.getTime())/86400000 + hour/24;
    const season = -K.seasonal*Math.cos(2*Math.PI*(doy - K.coldestDay)/365);
    const day = K.daily*Math.sin(2*Math.PI*(hour - 9)/24);           // warmest at 15 h
    const n = calendar.t0/86400000 + calendar.day;
    const wander = K.wander*(this._h(n)*2 - 1);
    this.temp = K.mean + season + day + wander - 3*storm - 2*this.amount;
  }

  /* What falls, and how much of it, given what the storm brings too. */
  precipitation(stormWet){
    const amount = Math.max(stormWet, this.amount);
    return { amount, snow: this.temp < Naval.CLIMATE.snowBelow };
  }

  word(){
    for(const [top, w] of Naval.CLIMATE.words) if(this.temp < top) return w;
    return Naval.CLIMATE.words[Naval.CLIMATE.words.length - 1][1];
  }
};
