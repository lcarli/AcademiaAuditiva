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
 *   A chord note may be an object, { note: 'E4', userPlaced: true,
 *   selected: true }, to colour it on its own.
 * - `prefilled` notes are gray, `userPlaced` ones blue and a `selected` one red.
 * - `clefAnnotation: '8va'` (treble) or `'8vb'` (treble or bass) marks
 *   the clef and draws every note an octave lower or higher than the
 *   pitch given, so very high or low notes need fewer ledger lines.
 * - `autoStem: true` points each stem away from the middle line.
 * - `rhythm: true` draws a one-line (percussion) staff for rhythms written on B4.
 * - With a time signature, eighth notes are beamed by the beat.
 * - `fill: 0.5` spreads the notes over half the staff, so a dictation being
 *   written grows from the left instead of stretching over the whole staff.
 * - render() returns { width, xs, ys }: the SVG width, the x of each note
 *   (null for barlines), so a click on the staff can find the note under it,
 *   and the y of each of its keys (a chord's in the order given).
 *
 * StaffRenderer.figure('q') returns an SVG icon of a note value ('qr' for its
 * rest); every icon shares one viewBox so the values keep their sizes.
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

  // VexFlow's beam grouping reads a note's duration as a number ("2" rather than "h").
  var VEX_DURATION = { w: "1", h: "2", q: "4" };

  var HEIGHT = 150;

  var GIVEN = { fillStyle: "#888", strokeStyle: "#888" };
  var PLACED = { fillStyle: "#0d6efd", strokeStyle: "#0d6efd" };
  var SELECTED = { fillStyle: "#dc3545", strokeStyle: "#dc3545" };

  function colourOf(n) {
    if (n.userPlaced) return PLACED;
    if (n.prefilled) return GIVEN;
    return null;
  }

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
    var members = [];
    var keys;
    var vexDuration = VEX_DURATION[baseDur] || baseDur;
    if (isRest) {
      // VexFlow rest keys: pick a reasonable middle pitch per clef.
      keys = [clef === "bass" ? "d/3" : "b/4"];
      vexDuration += "r";
    } else {
      members = Array.isArray(n.chord)
        ? n.chord.map(function (m) { return typeof m === "string" ? { note: m } : m; })
        : [n];
      names = members.map(function (m) { return m.note; });
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

    var accidentals = [];
    names.forEach(function (name, index) {
      var accidental = Mapping.accidentalOf(name);
      if (!accidental) return;
      accidentals[index] = new Accidental(accidental);
      staveNote.addModifier(accidentals[index], index);
    });

    var style = colourOf(n);
    if (style) staveNote.setStyle(style);
    // A beam takes the color of its notes (render); a selected note keeps it blue.
    staveNote.aaBeamStyle = style;
    if (n.selected) {
      staveNote.setStyle(SELECTED);
      if (VexFlow.Annotation) {
        var annotation = new VexFlow.Annotation("◆");
        if (annotation.setFont) annotation.setFont("Arial", 9);
        staveNote.addModifier(annotation, 0);
      }
    }
    // The notes of a chord can each have their own colour, accidental included.
    if (Array.isArray(n.chord)) {
      members.forEach(function (m, index) {
        var keyStyle = m.selected ? SELECTED : colourOf(m);
        if (!keyStyle) return;
        staveNote.setKeyStyle(index, keyStyle);
        if (accidentals[index]) accidentals[index].setStyle(keyStyle);
      });
    }

    return staveNote;
  }

  // Eighths are beamed by the beat: a quarter, or a dotted quarter in compound meters.
  function beamGroups(VexFlow, timeSig) {
    return /^(6|9|12)\/8$/.test(timeSig) ? [new VexFlow.Fraction(3, 8)] : [new VexFlow.Fraction(2, 8)];
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

    var rhythm = !!opts.rhythm;
    var clef = rhythm ? "treble" : opts.clef || "treble";
    var keySig = opts.keySignature || "C";
    var notes = Array.isArray(opts.notes) ? opts.notes : [];
    var timeSig = opts.timeSignature || null;
    var widthHint = opts.width || 600;

    div.innerHTML = "";

    var playable = notes.filter(function (n) { return n.note !== "barline"; });
    var barCount = notes.length - playable.length;
    var totalWidth = Math.max(widthHint, playable.length * 50 + barCount * 22 + 110);
    var drawn = {
      width: totalWidth,
      xs: notes.map(function () { return null; }),
      ys: notes.map(function () { return null; }),
    };

    var renderer = new Renderer(div, Renderer.Backends.SVG);
    renderer.resize(totalWidth, HEIGHT);
    var ctx = renderer.getContext();
    ctx.setFont("Arial", 10);

    var stave = new Stave(10, 20, totalWidth - 20);
    if (rhythm) {
      stave.setConfigForLines([0, 1, 2, 3, 4].map(function (line) { return { visible: line === 2 }; }));
      stave.addClef("percussion");
    } else {
      stave.addClef(clef, undefined, OCTAVE_SHIFT[opts.clefAnnotation] ? opts.clefAnnotation : undefined)
        .addKeySignature(keySig);
    }
    if (timeSig) stave.addTimeSignature(timeSig);
    stave.setContext(ctx).draw();

    if (notes.length === 0) return drawn;

    var tickables = [];
    var staveNotes = [];
    var totalBeats = 0;
    for (var i = 0; i < notes.length; i++) {
      var n = notes[i];
      if (n.note === "barline") {
        tickables.push(new BarNote());
      } else {
        var vn = buildVexNote(VexFlow, n, clef, opts);
        tickables.push(vn);
        staveNotes.push({ index: i, note: vn });
        var rawDur = n.duration || "q";
        totalBeats += BEAT_MAP[rawDur] != null ? BEAT_MAP[rawDur] : 1;
      }
    }

    if (totalBeats === 0) return drawn;

    var voice = new Voice({ num_beats: totalBeats, beat_value: 4 });
    voice.setStrict(false);
    voice.addTickables(tickables);

    // Beams split at barlines; rhythms keep every stem up.
    var beams = timeSig && VexFlow.Beam && VexFlow.Fraction
      ? VexFlow.Beam.generateBeams(tickables, {
        groups: beamGroups(VexFlow, timeSig),
        stem_direction: rhythm ? 1 : undefined,
      })
      : [];

    var formatter = new Formatter().joinVoices([voice]);
    var justifyWidth = totalWidth - 100;
    if (opts.fill != null && opts.fill < 1) {
      var minWidth = formatter.preCalculateMinTotalWidth([voice]);
      justifyWidth = Math.min(justifyWidth, Math.max(minWidth, justifyWidth * Math.max(0, opts.fill)));
    }
    formatter.format([voice], justifyWidth);
    voice.draw(ctx, stave);
    beams.forEach(function (beam) {
      var style = null;
      beam.getNotes().forEach(function (note) { style = style || note.aaBeamStyle; });
      if (style) beam.setStyle(style);
      beam.setContext(ctx).draw();
    });

    staveNotes.forEach(function (entry) {
      var note = entry.note;
      drawn.xs[entry.index] = note.getNoteHeadBeginX && note.getNoteHeadEndX
        ? (note.getNoteHeadBeginX() + note.getNoteHeadEndX()) / 2
        : note.getAbsoluteX();
      drawn.ys[entry.index] = note.getYs ? note.getYs().slice() : null;
    });
    fitHeight(div, totalWidth);
    return drawn;
  }

  // Shared icon box around the note head on the middle line (x, y = 0, 0):
  // the stem and flag go up, a rest hangs below or sits on the line.
  var FIGURE_BOX = { x: -7, y: -40, width: 36, height: 58 };
  var figureCache = {};

  function drawFigure(duration) {
    var VexFlow = root.VexFlow || (root.Vex && root.Vex.Flow);
    var Mapping = window.StaffMapping;
    var isRest = Mapping.isRestLabel(duration);
    var base = Mapping.baseDuration(duration);
    var holder = document.createElement("div");
    holder.style.cssText = "position:absolute;left:-10000px;top:0;";
    document.body.appendChild(holder);
    try {
      var renderer = new VexFlow.Renderer(holder, VexFlow.Renderer.Backends.SVG);
      renderer.resize(120, 120);
      var ctx = renderer.getContext();
      var stave = new VexFlow.Stave(0, 20, 120, { left_bar: false, right_bar: false });
      stave.setConfigForLines([0, 1, 2, 3, 4].map(function () { return { visible: false }; }));
      stave.setContext(ctx).draw();
      var note = new VexFlow.StaveNote({ keys: ["b/4"], duration: isRest ? base + "r" : base, clef: "treble" });
      VexFlow.Formatter.FormatAndDraw(ctx, stave, [note]);

      var svg = holder.querySelector("svg");
      var x = note.getNoteHeadBeginX ? note.getNoteHeadBeginX() : note.getAbsoluteX();
      var y = stave.getYForLine(2);
      svg.querySelectorAll("[id]").forEach(function (el) { el.removeAttribute("id"); });
      [svg].concat(Array.prototype.slice.call(svg.querySelectorAll("[fill], [stroke]"))).forEach(function (el) {
        ["fill", "stroke"].forEach(function (attr) {
          var value = el.getAttribute(attr);
          if (value && value !== "none") el.setAttribute(attr, "currentColor");
        });
      });
      // Whole and half rests only differ by where they sit, so a rest shows its line.
      if (isRest) {
        var line = document.createElementNS("http://www.w3.org/2000/svg", "line");
        line.setAttribute("x1", String(x + FIGURE_BOX.x + 4));
        line.setAttribute("x2", String(x + FIGURE_BOX.x + FIGURE_BOX.width - 4));
        line.setAttribute("y1", String(y));
        line.setAttribute("y2", String(y));
        line.setAttribute("stroke", "currentColor");
        line.setAttribute("stroke-width", "1.5");
        line.setAttribute("class", "aa-figure-line");
        svg.insertBefore(line, svg.firstChild);
      }
      svg.setAttribute("viewBox", [x + FIGURE_BOX.x, y + FIGURE_BOX.y, FIGURE_BOX.width, FIGURE_BOX.height].join(" "));
      svg.removeAttribute("width");
      svg.removeAttribute("height");
      svg.removeAttribute("style");
      svg.setAttribute("aria-hidden", "true");
      svg.setAttribute("focusable", "false");
      return svg;
    } finally {
      holder.remove();
    }
  }

  function figure(duration) {
    var key = String(duration);
    if (!figureCache[key]) figureCache[key] = drawFigure(key);
    return figureCache[key].cloneNode(true);
  }

  root.StaffRenderer = { render: render, figure: figure };
})(window);
