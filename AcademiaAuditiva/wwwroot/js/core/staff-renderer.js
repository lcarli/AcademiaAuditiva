/**
 * StaffRenderer — read-only musical staff rendering wrapper around
 * VexFlow 5 (loaded via /js/vexflow.js as window.VexFlow). Mirrors
 * the SonicMind reference at frontend/src/components/staff/StaffRenderer.tsx
 * but in vanilla JS so it can be consumed by any AA exercise view.
 *
 * Usage:
 *   StaffRenderer.render('#staff', {
 *     clef: 'treble',
 *     keySignature: 'C',
 *     timeSignature: '4/4',
 *     notes: [
 *       { note: 'C4', duration: 'q', label: 'q' },
 *       { note: 'D4', duration: 'q' },
 *       { note: 'rest', duration: 'qr' },
 *       { note: 'barline' },
 *       { note: 'E4', duration: 'h' }
 *     ],
 *     width: 600
 *   });
 *
 * Notes:
 * - `note: 'barline'` inserts a VexFlow BarNote.
 * - `note: 'rest'` OR `duration: '<x>r'` renders a rest of base duration.
 * - Dots: any duration ending in `.` (e.g. 'h.') renders a dotted note.
 */
(function (root) {
  "use strict";

  var BEAT_MAP = {
    w: 4, "w.": 6, h: 2, "h.": 3, q: 1, "q.": 1.5,
    "8": 0.5, "8.": 0.75, "16": 0.25, "8t": 1 / 3,
    wr: 4, hr: 2, qr: 1, "8r": 0.5, "16r": 0.25, "8tr": 1 / 3,
  };

  function buildVexNote(VexFlow, n, clef) {
    var StaveNote = VexFlow.StaveNote;
    var Accidental = VexFlow.Accidental;
    var Dot = VexFlow.Dot;
    var Mapping = window.StaffMapping;

    var rawDur = n.duration || "q";
    var isRest = n.note === "rest" || Mapping.isRestLabel(rawDur);
    var isDotted = Mapping.isDottedLabel(rawDur);
    var baseDur = Mapping.baseDuration(rawDur);

    var key;
    var vexDuration = baseDur;
    if (isRest) {
      // VexFlow rest keys: pick a reasonable middle pitch per clef.
      key = clef === "bass" ? "d/3" : "b/4";
      vexDuration = baseDur + "r";
    } else {
      key = Mapping.noteToVexKey(n.note);
      if (!key) throw new Error("StaffRenderer: bad note " + n.note);
    }

    var staveNote = new StaveNote({
      keys: [key],
      duration: vexDuration,
      clef: clef,
      dots: isDotted ? 1 : 0,
    });

    if (isDotted) {
      Dot.buildAndAttach([staveNote], { all: true });
    }

    if (!isRest) {
      var pure = String(n.note).replace(/\d/g, "");
      if (pure.indexOf("#") !== -1) {
        staveNote.addModifier(new Accidental("#"), 0);
      } else if (pure.length > 1 && pure.indexOf("b") !== -1) {
        staveNote.addModifier(new Accidental("b"), 0);
      }
    }

    if (n.prefilled) {
      staveNote.setStyle({ fillStyle: "#888", strokeStyle: "#888" });
    }
    if (n.userPlaced) {
      staveNote.setStyle({ fillStyle: "#0d6efd", strokeStyle: "#0d6efd" });
    }
    if (n.selected) {
      staveNote.setStyle({ fillStyle: "#dc3545", strokeStyle: "#dc3545" });
      if (VexFlow.Annotation) {
        var annotation = new VexFlow.Annotation("◆");
        if (annotation.setFont) annotation.setFont("Arial", 9);
        staveNote.addModifier(annotation, 0);
      }
    }

    return staveNote;
  }

  function render(target, opts) {
    var VexFlow = root.VexFlow || (root.Vex && root.Vex.Flow);
    if (!VexFlow) throw new Error("StaffRenderer: VexFlow global not loaded.");

    var Renderer = VexFlow.Renderer;
    var Stave = VexFlow.Stave;
    var Voice = VexFlow.Voice;
    var Formatter = VexFlow.Formatter;
    var BarNote = VexFlow.BarNote;

    var div = typeof target === "string" ? document.querySelector(target) : target;
    if (!div) throw new Error("StaffRenderer: target not found " + target);

    var clef = opts.clef || "treble";
    var keySig = opts.keySignature || "C";
    var notes = Array.isArray(opts.notes) ? opts.notes : [];
    var timeSig = opts.timeSignature || null;
    var widthHint = opts.width || 600;

    div.innerHTML = "";

    var playable = notes.filter(function (n) { return n.note !== "barline"; });
    var barCount = notes.length - playable.length;
    var totalWidth = Math.max(widthHint, playable.length * 50 + barCount * 22 + 110);

    var renderer = new Renderer(div, Renderer.Backends.SVG);
    renderer.resize(totalWidth, 150);
    var ctx = renderer.getContext();
    ctx.setFont("Arial", 10);

    var stave = new Stave(10, 20, totalWidth - 20);
    stave.addClef(clef).addKeySignature(keySig);
    if (timeSig) stave.addTimeSignature(timeSig);
    stave.setContext(ctx).draw();

    if (notes.length === 0) return;

    var tickables = [];
    var totalBeats = 0;
    for (var i = 0; i < notes.length; i++) {
      var n = notes[i];
      if (n.note === "barline") {
        tickables.push(new BarNote());
      } else {
        var vn = buildVexNote(VexFlow, n, clef);
        tickables.push(vn);
        var rawDur = n.duration || "q";
        totalBeats += BEAT_MAP[rawDur] != null ? BEAT_MAP[rawDur] : 1;
      }
    }

    if (totalBeats === 0) return;

    var voice = new Voice({ num_beats: totalBeats, beat_value: 4 });
    voice.setStrict(false);
    voice.addTickables(tickables);

    new Formatter().joinVoices([voice]).format([voice], totalWidth - 100);
    voice.draw(ctx, stave);
  }

  root.StaffRenderer = { render: render };
})(window);
