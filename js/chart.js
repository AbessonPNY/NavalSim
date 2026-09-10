/* The chart: where she is, what is around her, and where she has been.
 *
 * Drawn on a 2-D canvas rather than in WebGL. A chart is flat, north-up and
 * made of thin lines and text — everything a canvas does well and a shader does
 * badly — and it costs nothing on a page whose frame is already 98% idle.
 *
 * Everything plotted here is in TRUE WORLD metres, taken from
 * `ocean.origin + body.pos`. Plotting local coordinates would put her back at
 * the middle of the chart every time the floating origin slid, which is the one
 * thing a chart must never do.
 */
window.Naval = window.Naval || {};

Naval.Chart = class Chart {
  constructor(canvas, world, els){
    this.cv = canvas;
    this.ctx = canvas.getContext('2d');
    this.world = world;
    this.els = els || {};

    this.scale = 1.6;            // nautical miles from centre to edge
    this.track = [];             // her wake, in world metres
    this.trackEvery = 60;        // metres between marks
    this.trackMax = 900;
    this._last = null;
    this._islands = new Map();   // key → traced outline, in world metres
  }

  /* An island's shore as a closed polygon, traced once. The outline comes from
     the SAME `_shore` the terrain mesh is built on, so the chart can never show
     a coast the eye does not find — the single-plan-of-forms rule, applied to
     the land. */
  _outline(isl){
    let p = this._islands.get(isl.key);
    if(p) return p;
    p = [];
    for(let i=0;i<72;i++){
      const a = (i/72)*Math.PI*2, s = this.world._shore(isl, a);
      p.push([isl.x + Math.cos(a)*s, isl.z + Math.sin(a)*s]);
    }
    this._islands.set(isl.key, p);
    return p;
  }

  note(worldX, worldZ){
    const t = this.track;
    if(!this._last || Math.hypot(worldX-this._last[0], worldZ-this._last[1]) > this.trackEvery){
      this._last = [worldX, worldZ];
      t.push(this._last);
      if(t.length > this.trackMax) t.shift();
    }
  }

  zoom(f){ this.scale = Math.max(0.25, Math.min(24, this.scale*f)); }

  /* `fleet` entries carry world positions computed by the caller: the chart
     must not guess at the origin convention on its own. */
  draw(centre, headingRad, fleet, squall){
    const ctx = this.ctx, W = this.cv.width, H = this.cv.height;
    const R = Math.min(W, H)/2;
    const M = this.scale*1852;                 // metres from centre to edge
    const k = R/M;                             // pixels per metre
    const px = (x, z) => [W/2 + (x-centre.x)*k, H/2 - (z-centre.z)*k];

    ctx.clearRect(0, 0, W, H);
    ctx.save();
    ctx.beginPath(); ctx.arc(W/2, H/2, R-1, 0, 6.2832); ctx.clip();

    ctx.fillStyle = '#0a2233';
    ctx.fillRect(0, 0, W, H);

    // --- the graticule, at a spacing that keeps four or five lines in view ---
    const steps = [0.05, 0.1, 0.25, 0.5, 1, 2, 5, 10];
    let minutes = steps[steps.length-1];
    for(const s of steps){ if(this.scale*2/s <= 6){ minutes = s; break; } }
    const gm = minutes*1852;
    ctx.strokeStyle = 'rgba(120,170,190,.17)';
    ctx.lineWidth = 1;
    ctx.beginPath();
    const gx0 = Math.ceil((centre.x - M)/gm)*gm, gz0 = Math.ceil((centre.z - M)/gm)*gm;
    for(let x=gx0; x<centre.x+M; x+=gm){ const a=px(x, centre.z-M), b=px(x, centre.z+M);
      ctx.moveTo(a[0], a[1]); ctx.lineTo(b[0], b[1]); }
    for(let z=gz0; z<centre.z+M; z+=gm){ const a=px(centre.x-M, z), b=px(centre.x+M, z);
      ctx.moveTo(a[0], a[1]); ctx.lineTo(b[0], b[1]); }
    ctx.stroke();

    /* --- the depression, if there is one within reach ---
       Drawn UNDER the land, being weather rather than geography, and as a soft
       disc rather than an outline: one knows roughly where a squall is, never
       exactly where it ends. */
    if(squall){
      const q = px(squall.x, squall.z), rp = squall.r*k;
      const g = ctx.createRadialGradient(q[0], q[1], 0, q[0], q[1], Math.max(2, rp));
      g.addColorStop(0.00, 'rgba(20,26,34,.80)');
      g.addColorStop(0.55, 'rgba(28,38,50,.46)');
      g.addColorStop(1.00, 'rgba(30,42,56,0)');
      ctx.fillStyle = g;
      ctx.beginPath(); ctx.arc(q[0], q[1], Math.max(2, rp), 0, 6.2832); ctx.fill();
    }

    // --- land, with a shoal ring outside the shore ---
    for(const isl of this.world.near(centre.x, centre.z, M*1.6)){
      const out = this._outline(isl);
      for(const [w, style] of [[1.30, 'rgba(90,150,175,.30)'], [1.0, '#b8a271']]){
        ctx.beginPath();
        for(let i=0;i<out.length;i++){
          const q = px(isl.x + (out[i][0]-isl.x)*w, isl.z + (out[i][1]-isl.z)*w);
          i ? ctx.lineTo(q[0], q[1]) : ctx.moveTo(q[0], q[1]);
        }
        ctx.closePath();
        ctx.fillStyle = style; ctx.fill();
      }

      /* Her NAME, and a chart without names is a picture of a coast rather
         than a chart. Set on the island itself and not offset to one side: a
         label with a leader line is for a mark too small to write on, and
         these are kilometres across. The port is a ring at the head of its own
         jetty, which is the one thing on the shore one steers for. */
      const c = px(isl.x, isl.z);
      ctx.font = '600 10px var(--disp, system-ui)';
      ctx.textAlign = 'center'; ctx.textBaseline = 'middle';
      ctx.lineWidth = 3; ctx.strokeStyle = 'rgba(18,26,34,.72)';
      ctx.strokeText(isl.name, c[0], c[1]);
      ctx.fillStyle = '#e8ddc2';
      ctx.fillText(isl.name, c[0], c[1]);

      if(isl.port){
        const h = px(isl.port.hx, isl.port.hz);
        ctx.strokeStyle = '#e8ddc2'; ctx.lineWidth = 1.2;
        ctx.beginPath(); ctx.arc(h[0], h[1], 2.6, 0, 6.2832); ctx.stroke();
      }
    }

    // --- her track ---
    if(this.track.length > 1){
      ctx.strokeStyle = 'rgba(70,224,208,.45)';
      ctx.lineWidth = 1.4;
      ctx.beginPath();
      for(let i=0;i<this.track.length;i++){
        const q = px(this.track[i][0], this.track[i][1]);
        i ? ctx.lineTo(q[0], q[1]) : ctx.moveTo(q[0], q[1]);
      }
      ctx.stroke();
    }

    // --- the fleet, then her, so she is never hidden by a consort ---
    for(let i=1;i<fleet.length;i++){
      const q = px(fleet[i].x, fleet[i].z);
      ctx.fillStyle = '#f2b23a';
      ctx.beginPath(); ctx.arc(q[0], q[1], 3.2, 0, 6.2832); ctx.fill();
    }

    const c0 = px(centre.x, centre.z);
    ctx.save();
    ctx.translate(c0[0], c0[1]);
    ctx.rotate(headingRad);                    // north-up: the SHIP turns
    ctx.fillStyle = '#46e0d0';
    ctx.beginPath();
    ctx.moveTo(0, -8); ctx.lineTo(5, 6); ctx.lineTo(0, 3); ctx.lineTo(-5, 6);
    ctx.closePath(); ctx.fill();
    ctx.restore();

    ctx.restore();

    // --- the ring, a north mark, and the scale ---
    ctx.strokeStyle = 'rgba(120,170,190,.45)';
    ctx.lineWidth = 1;
    ctx.beginPath(); ctx.arc(W/2, H/2, R-1, 0, 6.2832); ctx.stroke();
    ctx.fillStyle = '#ff5c5c';
    ctx.font = '600 11px "IBM Plex Mono", monospace';
    ctx.textAlign = 'center';
    ctx.fillText('N', W/2, 13);

    ctx.strokeStyle = 'rgba(220,236,245,.55)';
    ctx.beginPath();
    ctx.moveTo(10, H-10); ctx.lineTo(10 + R/this.scale, H-10);
    ctx.moveTo(10, H-13); ctx.lineTo(10, H-7);
    ctx.moveTo(10 + R/this.scale, H-13); ctx.lineTo(10 + R/this.scale, H-7);
    ctx.stroke();
    ctx.fillStyle = 'rgba(220,236,245,.75)';
    ctx.font = '500 9.5px "IBM Plex Mono", monospace';
    ctx.textAlign = 'left';
    ctx.fillText('1 M', 14 + R/this.scale, H-7);

    // --- the fix, in the terms a log book takes ---
    if(this.els.fix){
      const g = Naval.Geo.fix(centre.x, centre.z);
      this.els.fix.textContent = Naval.Geo.format(g.lat, true) + '   '
                               + Naval.Geo.format(g.lon, false);
    }
    if(this.els.scale) this.els.scale.textContent = this.scale.toFixed(2) + ' M';
  }
};
