/* Prendre l'écran.
 *
 * A page cannot write where it likes — nothing in a browser may reach into a
 * folder on the disk. What CAN is the dev server, which is already serving that
 * very folder: the picture is posted to it and it writes the file. So a capture
 * lands in the project beside the code that made it, which is what one wants
 * when the point of the picture is to compare it with the last one.
 *
 * A published page has no such server, so it falls back to asking the browser
 * to save the file. That is a different thing in a different place, and it says
 * which one happened rather than pretending they are the same.
 *
 * What comes out has NO instruments on it. The dials are DOM elements laid over
 * the canvas, so they are simply not in the drawing buffer — the screenshot is
 * the sea and the ship and nothing else, without having to hide anything first.
 */
window.Naval = window.Naval || {};

Naval.Capture = class Capture {
  constructor(stage){
    this.stage = stage;
    this.onDone = null;      // wired by the page, to say where it went
    this.busy = false;
  }

  async take(){
    if(this.busy) return;    // one at a time: the encode is not instant
    this.busy = true;
    try {
      /* RENDER FIRST, and grab in the same breath. This is the whole trap of
         screenshotting WebGL: three's renderer is built with
         preserveDrawingBuffer false, so the drawing buffer's contents are
         UNDEFINED once the frame has been composited. Called from a keypress —
         which happens between frames, not during one — toDataURL therefore
         returns a blank or torn image, and it does so silently. Drawing again
         immediately before reading is what makes the pixels exist at the moment
         they are asked for. */
      const cv = this.stage.renderer.domElement;

      /* Rien à capturer, plutôt qu'un fichier vide annoncé comme un succès.
         Un canvas sans surface — fenêtre réduite, volet non composité — rend
         une image de trois octets, et c'est exactement ce qui s'est écrit sur
         le disque au premier essai avec un message disant que tout allait
         bien. Un outil de mesure qui ment sur ce qu'il vient d'écrire est pire
         que pas d'outil du tout. */
      if(cv.width < 8 || cv.height < 8){
        this._say('rien à capturer : la fenêtre est sans surface');
        return;
      }

      this.stage.render();
      const url = cv.toDataURL('image/png');

      /* The dev server writes into the project. Tried first because it is the
         one that puts the file where it was asked to go. */
      try {
        const r = await fetch('/capture', { method:'POST', body:url });
        if(r.ok){
          const j = await r.json();
          this._say('écran → ' + j.file);
          return;
        }
      } catch(e){ /* no server behind this page: the browser, then */ }

      const a = document.createElement('a');
      a.href = url;
      a.download = 'naval-' + new Date().toISOString().replace(/[:.]/g,'-') + '.png';
      a.click();
      this._say('écran remis au navigateur');
    } catch(e){
      this._say('capture impossible : ' + (e && e.message ? e.message : e));
    } finally {
      this.busy = false;
    }
  }

  _say(m){ if(this.onDone) this.onDone(m); }
};
