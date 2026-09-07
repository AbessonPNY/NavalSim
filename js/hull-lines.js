/* The hull's lines plan, as pure functions of station position.
   Built from a ShipSpec, and shared by the visible mesh and the buoyancy probe
   grid — so both describe exactly one shape. That is the single most important
   invariant in this simulation: what you see is what floats. */
window.Naval = window.Naval || {};

Naval.HullLines = class HullLines {
  constructor(spec){
    this.spec = spec;
    this.h = spec.hull;
  }

  static smooth01(u){ u = u<0?0:(u>1?1:u); return u*u*(3-2*u); }

  // t = 0 at the transom → 1 at the stem
  deckY(t){                                   // the sheer line
    const h = this.h;
    const fwd = Math.max(0,(t-0.42)/0.58), aft = Math.max(0,(0.42-t)/0.42);
    return h.freeboardMid + h.sheerBow*fwd*fwd + h.sheerStern*aft*aft;
  }

  keelY(t){                                   // underside of keel / rabbet
    const h = this.h, S = Naval.HullLines.smooth01;
    let d = h.keelDepth + h.dragAft*(1-t);    // drag: she sits deeper aft
    d *= (1 - h.forefootLift*S((t-0.80)/0.20));  // forefoot sweeps up to the stem
    d *= (1 - h.counterLift *S((0.12-t)/0.12));  // counter lifts at the stern
    return -d;
  }

  halfB(t){                                   // waterline half-breadth
    const h = this.h;
    let sh = Math.pow(Math.max(0,Math.sin(Math.PI*Math.pow(t,h.waterlinePower))), h.waterlineFull);
    sh = Math.max(sh, h.transomWidth*Math.exp(-t*14));   // transom keeps its width aft
    return (h.beam/2)*Math.min(1,sh);
  }

  /* Section shape. s = 0 at the deck edge → 1 at the keel. Topside and bilge
     hold nearly full beam (that is where the buoyancy lives), then the garboard
     tucks into the keel. A low `sectionTuck` gives a wineglass; a high one the
     fuller, boxier body of a warship carrying her guns high. */
  beamFactor(s){
    const h = this.h, S = Naval.HullLines.smooth01;
    return Math.pow(1 - S((s - h.sectionTuck)/(1 - h.sectionTuck)), h.sectionPower);
  }

  // Lofts the stations into a closed hull surface.
  buildGeometry(){
    const L = this.spec.L;
    const stations = 14, sideSteps = 6;
    const rows = [];
    for(let i=0;i<=stations;i++){
      const t = i/stations;
      const z = -L/2 + t*L;
      const hb = this.halfB(t), dY = this.deckY(t), kY = this.keelY(t);
      const sec = [];
      for(let j=0;j<=sideSteps;j++){
        const s = j/sideSteps;
        sec.push(new THREE.Vector3(hb*this.beamFactor(s), dY + (kY - dY)*s, z));
      }
      rows.push(sec);
    }

    const pos = [], idx = [], rowBase = [];
    let vi = 0;
    for(let i=0;i<rows.length;i++){
      rowBase.push(vi);
      const sec = rows[i];
      for(let j=0;j<sec.length;j++){ pos.push(sec[j].x, sec[j].y, sec[j].z); vi++; }
      // port side mirrored, skipping the duplicated keel point
      for(let j=sec.length-2;j>=0;j--){ pos.push(-sec[j].x, sec[j].y, sec[j].z); vi++; }
    }
    const ring = rows[0].length*2 - 1;
    for(let i=0;i<rows.length-1;i++){
      const a = rowBase[i], b = rowBase[i+1];
      for(let j=0;j<ring-1;j++){
        idx.push(a+j, b+j, a+j+1);  idx.push(a+j+1, b+j, b+j+1);
      }
      const aS=a, aP=a+ring-1, bS=b, bP=b+ring-1;   // deck follows the sheer
      idx.push(aS,aP,bS);  idx.push(bS,aP,bP);
    }
    const cap = (base, reverse) => {
      for(let j=1;j<ring-1;j++){
        if(reverse) idx.push(base, base+j, base+j+1);
        else        idx.push(base, base+j+1, base+j);
      }
    };
    cap(rowBase[0], false);                 // transom
    cap(rowBase[rows.length-1], true);      // bow

    const geo = new THREE.BufferGeometry();
    geo.setAttribute('position', new THREE.Float32BufferAttribute(pos,3));
    geo.setIndex(idx);
    geo.computeVertexNormals();
    return geo;
  }
};
