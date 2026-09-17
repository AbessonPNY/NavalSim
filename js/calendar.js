/* The date.
 *
 * A calendar that turns over at midnight of the day cycle, from a start set in
 * settings.json (calendar.start, "1598-10-08" by default). The Gregorian
 * calendar, which Spain, France and the Italian states had taken up in 1582 —
 * JavaScript's Date runs it backwards that far without complaint.
 *
 * It also gives the SEASON: the sun's declination for the day of the year,
 * which the stage uses for the length of the day and the height of noon. The
 * stage had been held at a fixed late spring; an October voyage now has
 * October's shorter days.
 *
 * Shown as a ship's log would head a page in English: "October 8th, 1598".
 */
window.Naval = window.Naval || {};

Naval.CALENDAR = { start: '1598-10-08' };

Naval.Calendar = class Calendar {
  constructor(start){
    this.setStart(start || Naval.CALENDAR.start);
  }

  setStart(iso){
    const m = /^(\d{3,4})-(\d{1,2})-(\d{1,2})$/.exec(String(iso || '').trim());
    const y = m ? +m[1] : 1598, mo = m ? +m[2] - 1 : 9, d = m ? +m[3] : 8;
    // Date.UTC maps years below 100 onto 1900+, so the year is set on its own
    const t = new Date(Date.UTC(2000, mo, d));
    t.setUTCFullYear(y);
    this.t0 = t.getTime();
    this.day = 0;
    // the page redraws the date and the season on a new start as on a new day
    if(this.onDay) this.onDay(this);
  }

  nextDay(){ this.day++; if(this.onDay) this.onDay(this); }

  date(){ return new Date(this.t0 + this.day*86400000); }

  label(){
    const D = this.date(), d = D.getUTCDate();
    const months = ['January','February','March','April','May','June','July',
                    'August','September','October','November','December'];
    const teen = d % 100 >= 11 && d % 100 <= 13;
    const suf = teen ? 'th' : ({1:'st', 2:'nd', 3:'rd'})[d % 10] || 'th';
    return months[D.getUTCMonth()] + ' ' + d + suf + ', ' + D.getUTCFullYear();
  }

  /* The sun's declination, degrees, for this day of the year — the usual
     cosine approximation, a degree or so off, which no one at sea could tell. */
  declination(){
    const D = this.date();
    const jan1 = new Date(Date.UTC(2000, 0, 1)); jan1.setUTCFullYear(D.getUTCFullYear());
    const doy = Math.floor((D.getTime() - jan1.getTime())/86400000);
    return -23.44*Math.cos(2*Math.PI*(doy + 10)/365);
  }
};
