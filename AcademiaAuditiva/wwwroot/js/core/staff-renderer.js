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
 * - `chord: ['C4', 'E4', 'G4']` (instead of `note`) stacks the notes on
 *   one stem; double sharps and flats ('F##4', 'Bbb3') are drawn too.
 * - `clefAnnotation: '8va'` (treble) or `'8vb'` (treble or bass) marks
 *   the clef and draws every note an octave lower or higher than the
 *   pitch given, so very high or low notes need fewer ledger lines.
 * - `autoStem: true` points each stem away from the middle line.
 */
(function (root) {
  "use strict";

  var BEAT_MAP = {
    w: 4, "w.": 6, h: 2, "h.": 3, q: 1, "q.": 1.5,
    "8": 0.5, "8.": 0.75, "16": 0.25, "8t": 1 / 3,
    wr: 4, hr: 2, qr: 1, "8r": 0.5, "16r": 0.25, "8tr": 1 / 3,
  };

  // VexFlow lowers the drawn note by octave_shift octaves.
  var OCTAVE_SHIFT = { "8va": 1, "8vb": -1 };

  var HEIGHT = 150;

  // A flat on a note far above the staff reaches past the top of the box;
  // grow the box to show it rather than clip it. Normal staves keep 150 px.
  function fitHeight(div, width) {
    var svg = div.querySelector("svg");
    if (!svg || typeof svg.getBBox !== "function") return;
    var box;
    try { box = svg.getBBox(); } catch (e) { return; }
    // A hidden staff has no layout to measure.
    if (!box.width && !box.height) return;
    var top = Math.min(0, Math.floor(box.y) - 2);
    var bottom = Math.max(HEIGHT, Math.ceil(box.y + box.height) + 2);
    if (top === 0 && bottom === HEIGHT) return;
    var height = bottom - top;
    svg.setAttribute("viewBox", "0 " + top + " " + width + " " + height);
    svg.setAttribute("height", String(height));
    svg.style.height = height + "px";
  }

  function buildVexNote(VexFlow, n, clef, opts) {
    var StaveNote = VexFlow.StaveNote;
    var Accidental = VexFlow.Accidental;
    var Dot = VexFlow.Dot;
    var Mapping = window.StaffMapping;

    var rawDur = n.duration || "q";
    var isRest = n.note === "rest" || Mapping.isRestLabel(rawDur);
    var isDotted = Mapping.isDottedLabel(rawDur);
    var baseDur = Mapping.baseDuration(rawDur);

    var names = [];
    var keys;
    var vexDuration = baseDur;
    if (isRest) {
      // VexFlow rest keys: pick a reasonable middle pitch per clef.
      keys = [clef === "bass" ? "d/3" : "b/4"];
      vexDuration = baseDur + "r";
    } else {
      names = Array.isArray(n.chord) ? n.chord : [n.note];
      keys = names.map(function (name) {
        var key = Mapping.noteToVexKey(name);
        if (!key) throw new Error("StaffRenderer: bad note " + name);
        return key;
      });
    }

    var staveNote = new StaveNote({
      keys: keys,
      duration: vexDuration,
      clef: clef,
      dots: isDotted ? 1 : 0,
      octave_shift: isRest ? 0 : OCTAVE_SHIFT[opts.clefAnnotation] || 0,
      auto_stem: !!opts.autoStem,
    });

    if (isDotted) {
      Dot.buildAndAttach([staveNote], { all: true });
    }

    names.forEach(function (name, index) {
      var accidental = Mapping.accidentalOf(name);
      if (accidental) staveNote.addModifier(new Accidental(accidental), index);
    });

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
    renderer.resize(totalWidth, HEIGHT);
    var ctx = renderer.getContext();
    ctx.setFont("Arial", 10);

    var stave = new Stave(10, 20, totalWidth - 20);
    stave.addClef(clef, undefined, OCTAVE_SHIFT[opts.clefAnnotation] ? opts.clefAnnotation : undefined)
      .addKeySignature(keySig);
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
        var vn = buildVexNote(VexFlow, n, clef, opts);
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
    fitHeight(div, totalWidth);
  }

  root.StaffRenderer = { render: render };
})(window);
