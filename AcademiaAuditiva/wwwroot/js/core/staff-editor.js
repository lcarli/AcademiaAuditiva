/**
 * StaffEditor — interactive note-entry widget that renders a staff
 * (via StaffRenderer) plus a button palette. Returns a controller
 * instance with getValue()/clear()/destroy() so the host view can
 * poll/reset it without leaking event handlers.
 *
 * Usage:
 *   var editor = StaffEditor.attach('#staff-editor', {
 *     clef: 'treble',
 *     keySignature: 'C',
 *     timeSignature: '4/4',
 *     octave: 4,
 *     allowedDurations: ['w'],          // more than one → note value buttons
 *     restDurations: [],
 *     totalSlots: 6,                    // cap on notes when there are no measures
 *     measures: 2,                      // measures to fill; needs timeSignature
 *     rhythm: false,                    // true: note values only, on a one-line staff
 *     chord: false,                     // true: the notes stack as one chord
 *     figureLabels: { q: 'Quarter note', qr: 'Quarter rest' },
 *     prefilledNotes: [{ note: 'C4', duration: 'w', prefilled: true }],
 *     onChange: function (notes) { ... }
 *   });
 *   editor.getValue();    // → array of { note, duration } the user placed
 *   editor.getAnswerString(); // → "D4:w|E4:w|..." canonical form
 *   editor.isComplete();  // → every measure is full (always true without measures)
 *   editor.clear();
 *   editor.destroy();
 *
 * With `measures`, the barlines follow from the note values: a value that no
 * longer fits in the measure can't be chosen, and the answer gets "bar"
 * before each note that starts a measure. A rhythm answer holds the note
 * values only ("q|qr|bar|h").
 *
 * With `chord`, the given notes and those placed stack as one chord, kept
 * from the lowest note up: a click on the staff selects the note nearest in
 * height, the arrow keys the next one up or down, undo drops the note placed
 * last and the answer lists the notes from the lowest ("E4:w|G4:w").
 */
(function (root) {
  "use strict";

  var NOTE_NAMES = ["C", "D", "E", "F", "G", "A", "B"];
  // Note values in sixteenths, the unit a measure is counted in.
  var SIXTEENTHS = { w: 16, h: 8, q: 4, "8": 2, "16": 1 };
  var FIGURE_LABELS = {
    w: "Whole note", h: "Half note", q: "Quarter note", "8": "Eighth note",
    wr: "Whole rest", hr: "Half rest", qr: "Quarter rest", "8r": "Eighth rest",
  };
  // A rhythm is written on the middle line of its one-line staff.
  var RHYTHM_PITCH = "B4";

  function sixteenths(duration) {
    var base = String(duration || "").replace(/r$/, "");
    var dotted = /\.$/.test(base);
    var value = SIXTEENTHS[base.replace(/\.$/, "")] || 0;
    return dotted ? value * 1.5 : value;
  }

  // "3/4" → 12 sixteenths; 0 without a time signature.
  function measureLength(timeSig) {
    var m = /^(\d+)\/(\d+)$/.exec(String(timeSig || ""));
    return m ? (Number(m[1]) * 16) / Number(m[2]) : 0;
  }

  function applyAccidental(note, acc) {
    var clean = note.replace("#", "").replace("b", "");
    var m = clean.match(/^([A-G])(\d)$/);
    if (!m) return note;
    if (acc === "") return clean;
    return m[1] + acc + m[2];
  }

  function makeBtn(text, classes, onClick) {
    var b = document.createElement("button");
    b.type = "button";
    b.className = "btn btn-sm " + (classes || "btn-outline-secondary");
    b.textContent = text;
    b.addEventListener("click", onClick);
    return b;
  }

  function makeAriaBtn(text, label, classes, onClick) {
    var b = makeBtn(text, classes, onClick);
    b.setAttribute("aria-label", label);
    b.title = label;
    return b;
  }

  function makeGroup() {
    var group = document.createElement("div");
    group.className = "btn-group";
    group.setAttribute("role", "group");
    return group;
  }

  function lowerFirst(text) {
    return text ? text.charAt(0).toLowerCase() + text.slice(1) : text;
  }

  function attach(target, opts) {
    var rootEl = typeof target === "string" ? document.querySelector(target) : target;
    if (!rootEl) throw new Error("StaffEditor: target not found " + target);

    var clef = opts.clef || "treble";
    var keySig = opts.keySignature || "C";
    var timeSig = opts.timeSignature || null;
    var octave = opts.octave || 4;
    var minOctave = opts.minOctave || 3;
    var maxOctave = opts.maxOctave || 6;
    var octaveDownLabel = opts.octaveDownLabel || "Octave down";
    var octaveUpLabel = opts.octaveUpLabel || "Octave up";
    var octaveDisplayLabel = opts.octaveDisplayLabel || "Octave";
    var selectedNoteLabel = opts.selectedNoteLabel || "Selected note {{0}}: {{1}}";
    var noSelectionLabel = opts.noSelectionLabel || "No note selected";
    var measureLabel = opts.measureLabel || "Measure {{0}} of {{1}}";
    var completeLabel = opts.completeLabel || "All measures are full.";
    var accidentals = [
      { value: "#", name: "sharp", symbol: "♯", label: opts.sharpLabel || "Sharp" },
      { value: "b", name: "flat", symbol: "♭", label: opts.flatLabel || "Flat" },
      { value: "", name: "natural", symbol: "♮", label: opts.naturalLabel || "Natural" },
    ];
    var undoLabel = opts.undoLabel || "Undo";
    var clearLabel = opts.clearLabel || "Clear";
    var figureLabels = Object.assign({}, FIGURE_LABELS);
    Object.keys(opts.figureLabels || {}).forEach(function (key) {
      if (opts.figureLabels[key]) figureLabels[key] = opts.figureLabels[key];
    });
    var allowedDurations = opts.allowedDurations && opts.allowedDurations.length
      ? opts.allowedDurations.slice() : ["w"];
    var restDurations = opts.restDurations || [];
    var totalSlots = opts.totalSlots || 99;
    var rhythm = opts.rhythm === true;
    var chord = opts.chord === true;
    var capacity = measureLength(timeSig);
    var measures = capacity > 0 && opts.measures > 0 ? Math.floor(opts.measures) : 0;
    var prefilledNotes = (opts.prefilledNotes || []).map(function (n) {
      return Object.assign({}, n, { prefilled: true });
    });
    var onChange = typeof opts.onChange === "function" ? opts.onChange : function () { };

    var isLinear = allowedDurations.length > 1;
    var userNotes = [];
    var selectedIndex = -1;
    var selectedDuration = allowedDurations.indexOf("q") >= 0 ? "q" : allowedDurations[0];
    // Where the last render put each note, so a click on the staff selects the nearest one.
    var drawn = null;
    // The notes of a chord are numbered as they are placed, for undo.
    var nextId = 1;

    rootEl.innerHTML = "";
    rootEl.classList.add("staff-editor-root");
    // The staff keeps a light page in both themes (.aa-sheet), so its buttons take the light colours.
    rootEl.setAttribute("data-bs-theme", "light");
    rootEl.tabIndex = 0;

    var staffDiv = document.createElement("div");
    staffDiv.className = "staff-editor-staff text-center";
    rootEl.appendChild(staffDiv);

    var selectionDiv = document.createElement("div");
    selectionDiv.className = "staff-editor-selection text-center small fw-semibold mt-2";
    selectionDiv.setAttribute("role", "status");
    selectionDiv.setAttribute("aria-live", "polite");
    rootEl.appendChild(selectionDiv);

    var paletteDiv = document.createElement("div");
    paletteDiv.className = "staff-editor-palette mt-3 d-flex flex-wrap justify-content-center gap-2";
    rootEl.appendChild(paletteDiv);

    // Which notes start a measure, how many measures are full and what is left in the current one.
    function layout() {
      var used = 0;
      var measure = 0;
      var total = 0;
      var starts = prefilledNotes.concat(userNotes).map(function (n) {
        var value = sixteenths(n.duration);
        var startsMeasure = false;
        if (measures && used >= capacity) {
          measure += 1;
          used = 0;
          startsMeasure = true;
        }
        used += value;
        total += value;
        return startsMeasure;
      });
      var full = measures > 0 && used >= capacity;
      var filled = full ? measure + 1 : measure;
      return {
        starts: starts,
        filled: filled,
        complete: measures > 0 && filled >= measures,
        remaining: full ? capacity : capacity - used,
        current: Math.min(measures, filled + 1),
        fill: measures ? total / (measures * capacity) : 1,
      };
    }

    function fits(duration, lay) {
      if (!measures) return isLinear || userNotes.length < totalSlots;
      return !lay.complete && sixteenths(duration) <= lay.remaining;
    }

    function isEditable(n) {
      return !rhythm && !!n && n.note !== "rest" && n.note !== "barline";
    }

    function editableIndexes() {
      var list = [];
      userNotes.forEach(function (n, i) { if (isEditable(n)) list.push(i); });
      return list;
    }

    function lastEditableIdx() {
      var list = editableIndexes();
      if (!chord) return list.length ? list[list.length - 1] : -1;
      // In a chord, the note placed last.
      return list.reduce(function (latest, i) {
        return latest < 0 || userNotes[i].id > userNotes[latest].id ? i : latest;
      }, -1);
    }

    function pitch(note) {
      return window.StaffMapping.noteToMidi(note) || 0;
    }

    // A chord keeps its notes from the lowest up; the selection stays on its note.
    function sortChord() {
      if (!chord) return;
      var selected = userNotes[selectedIndex];
      userNotes.sort(function (a, b) { return pitch(a.note) - pitch(b.note) || a.id - b.id; });
      selectedIndex = userNotes.indexOf(selected);
    }

    function selectedEditableIdx() {
      if (selectedIndex >= 0 && selectedIndex < userNotes.length && isEditable(userNotes[selectedIndex])) {
        return selectedIndex;
      }
      return lastEditableIdx();
    }

    function selectedText() {
      var idx = selectedEditableIdx();
      if (idx < 0) return noSelectionLabel;
      var n = userNotes[idx];
      var name = isLinear && figureLabels[n.duration]
        ? n.note + " (" + lowerFirst(figureLabels[n.duration]) + ")"
        : n.note;
      return selectedNoteLabel
        .replace("{{0}}", String(idx + 1))
        .replace("{{1}}", name);
    }

    function statusText(lay) {
      if (!measures) return selectedText();
      var text = lay.complete
        ? completeLabel
        : measureLabel.replace("{{0}}", String(lay.current)).replace("{{1}}", String(measures));
      if (selectedEditableIdx() >= 0) text += " · " + selectedText();
      return text;
    }

    function rerender() {
      var lay = layout();
      // When the chosen note value no longer fits, take the longest one that does.
      if (measures && !rhythm && !fits(selectedDuration, lay)) {
        var fitting = allowedDurations.filter(function (d) { return fits(d, lay); });
        if (fitting.length) {
          selectedDuration = fitting.reduce(function (a, b) { return sixteenths(b) > sixteenths(a) ? b : a; });
        }
      }
      drawStaff(lay);
      buildPalette(lay);
      var text = statusText(lay);
      if (selectionDiv.textContent !== text) selectionDiv.textContent = text;
      onChange(userNotes.slice());
    }

    function drawStaff(lay) {
      if (chord) {
        drawChord();
        return;
      }
      var items = [];
      var owners = []; // index in userNotes of each staff item; negative for the rest
      prefilledNotes.concat(userNotes.map(function (n, i) {
        return Object.assign({ userPlaced: true }, n, { selected: i === selectedIndex && isEditable(n) });
      })).forEach(function (n, i) {
        if (lay.starts[i]) {
          items.push({ note: "barline" });
          owners.push(-1);
        }
        items.push(n);
        owners.push(i - prefilledNotes.length);
      });
      // A full measure shows its barline before the next one is begun.
      if (measures && !lay.complete && lay.filled > 0 && lay.remaining === capacity) {
        items.push({ note: "barline" });
        owners.push(-1);
      }
      var result = window.StaffRenderer.render(staffDiv, {
        clef: clef,
        keySignature: keySig,
        timeSignature: timeSig,
        notes: items,
        rhythm: rhythm,
        autoStem: opts.autoStem === true,
        fill: measures && !lay.complete ? lay.fill : undefined,
      });
      drawn = result ? { width: result.width, xs: result.xs, owners: owners } : null;
    }

    // The given notes and those placed, stacked as one chord of their note value.
    function drawChord() {
      var members = prefilledNotes.map(function (n) {
        return { note: n.note, prefilled: true };
      }).concat(userNotes.map(function (n, i) {
        return { note: n.note, userPlaced: true, selected: i === selectedIndex };
      }));
      // The index in userNotes of each note of the chord; negative for the given ones.
      var owners = members.map(function (m, k) { return k - prefilledNotes.length; });
      var result = window.StaffRenderer.render(staffDiv, {
        clef: clef,
        keySignature: keySig,
        timeSignature: timeSig,
        notes: members.length ? [{ chord: members, duration: selectedDuration }] : [],
      });
      drawn = result ? { width: result.width, xs: result.xs, ys: result.ys, owners: owners } : null;
    }

    function addNote(n) {
      if (!fits(n.duration, layout())) return;
      if (chord) {
        // A note already in the chord isn't stacked twice; one placed is selected instead.
        var same = userNotes.map(function (u) { return u.note; }).indexOf(n.note);
        if (same >= 0) {
          selectNote(same);
          return;
        }
        if (prefilledNotes.some(function (p) { return p.note === n.note; })) return;
        n = Object.assign({ id: nextId++ }, n);
      }
      userNotes.push(n);
      if (isEditable(n)) selectedIndex = userNotes.length - 1;
      sortChord();
      rerender();
    }

    function selectNote(idx) {
      if (idx < 0 || idx >= userNotes.length || !isEditable(userNotes[idx])) return;
      selectedIndex = idx;
      rerender();
    }

    function changeSelectedOctave(delta) {
      var idx = selectedEditableIdx();
      if (idx < 0) {
        octave = Math.min(maxOctave, Math.max(minOctave, octave + delta));
        return false;
      }
      var n = userNotes[idx];
      var changed = n.note.replace(/(-?\d+)$/, function (octaveText) {
        return String(Math.min(maxOctave, Math.max(minOctave, parseInt(octaveText, 10) + delta)));
      });
      userNotes[idx] = Object.assign({}, n, { note: changed });
      octave = selectedOctaveForNote(changed);
      selectedIndex = idx;
      sortChord();
      return true;
    }

    function selectedOctaveForNote(note) {
      var m = String(note).match(/(-?\d+)$/);
      return m ? parseInt(m[1], 10) : octave;
    }

    function selectedOctave() {
      var idx = selectedEditableIdx();
      var note = idx >= 0 ? userNotes[idx].note : String(octave);
      return selectedOctaveForNote(note);
    }

    // The palette is rebuilt on every change: give the focus back to the same
    // button, or to the first one still enabled.
    function restoreFocus(selector) {
      setTimeout(function () {
        var btn = paletteDiv.querySelector(selector);
        if (!btn || btn.disabled) btn = paletteDiv.querySelector("button:not(:disabled)");
        if (btn) btn.focus({ preventScroll: true });
      }, 0);
    }

    staffDiv.addEventListener("click", function (ev) {
      var svg = staffDiv.querySelector("svg");
      if (rhythm || !drawn || !svg) return;
      if (chord) {
        selectNote(nearestInChord(svg, ev));
        return;
      }
      var rect = svg.getBoundingClientRect();
      if (!rect.width) return;
      var x = ((ev.clientX - rect.left) * drawn.width) / rect.width;
      var best = -1;
      var bestDistance = Infinity;
      drawn.xs.forEach(function (noteX, k) {
        var idx = drawn.owners[k];
        if (noteX == null || idx < 0 || !isEditable(userNotes[idx])) return;
        var distance = Math.abs(noteX - x);
        if (distance < bestDistance) {
          best = idx;
          bestDistance = distance;
        }
      });
      selectNote(best);
    });

    // The notes of a chord share one x: the placed note nearest the click in height.
    function nearestInChord(svg, ev) {
      var ys = drawn.ys && drawn.ys[0];
      var ctm = svg.getScreenCTM && svg.getScreenCTM();
      if (!ys || !ctm) return -1;
      // The staff may grow above its box (fitHeight), so map the click through the SVG's own transform.
      var point = svg.createSVGPoint();
      point.x = ev.clientX;
      point.y = ev.clientY;
      var y = point.matrixTransform(ctm.inverse()).y;
      var best = -1;
      var bestDistance = Infinity;
      ys.forEach(function (keyY, k) {
        var idx = drawn.owners[k];
        if (keyY == null || idx < 0) return;
        var distance = Math.abs(keyY - y);
        if (distance < bestDistance) {
          best = idx;
          bestDistance = distance;
        }
      });
      return best;
    }

    // In a chord, up and down move the selection as left and right do: its notes go from the lowest up.
    rootEl.addEventListener("keydown", function (ev) {
      var forward = ev.key === "ArrowRight" || (chord && ev.key === "ArrowUp");
      var back = ev.key === "ArrowLeft" || (chord && ev.key === "ArrowDown");
      if (!forward && !back) return;
      var editable = editableIndexes();
      if (!editable.length) return;
      ev.preventDefault();
      var current = editable.indexOf(selectedEditableIdx());
      if (current < 0) current = forward ? -1 : editable.length;
      var next = forward
        ? Math.min(editable.length - 1, current + 1)
        : Math.max(0, current - 1);
      selectNote(editable[next]);
    });

    function makeFigureBtn(duration, classes, onClick) {
      var label = figureLabels[duration] || duration;
      var b = makeAriaBtn("", label, classes, onClick);
      b.className = "btn aa-figure " + classes;
      b.setAttribute("data-figure", duration);
      try {
        b.appendChild(window.StaffRenderer.figure(duration));
      } catch (err) {
        console.warn("Could not draw the note value:", err);
        b.textContent = label;
      }
      return b;
    }

    // Rhythm: each value goes on the staff. Melody: the value is chosen for the next note name.
    function appendFigures(lay) {
      var noteRow = makeGroup();
      allowedDurations.forEach(function (d) {
        var pressed = !rhythm && d === selectedDuration;
        var b = makeFigureBtn(d, pressed ? "btn-primary" : "btn-outline-primary", function () {
          if (rhythm) {
            addNote({ note: RHYTHM_PITCH, duration: d });
          } else {
            selectedDuration = d;
            rerender();
          }
          restoreFocus("[data-figure='" + d + "']");
        });
        if (!rhythm) b.setAttribute("aria-pressed", pressed ? "true" : "false");
        b.disabled = !fits(d, lay);
        noteRow.appendChild(b);
      });
      paletteDiv.appendChild(noteRow);

      if (!restDurations.length) return;
      var restRow = makeGroup();
      restDurations.forEach(function (rd) {
        var b = makeFigureBtn(rd, "btn-outline-warning", function () {
          addNote({ note: "rest", duration: rd });
          restoreFocus("[data-figure='" + rd + "']");
        });
        b.disabled = !fits(rd, lay);
        restRow.appendChild(b);
      });
      paletteDiv.appendChild(restRow);
    }

    function appendOctaveRow() {
      var octaveRow = makeGroup();
      octaveRow.classList.add("align-items-center");
      var label = document.createElement("span");
      function updateOctaveControls() {
        var currentOctave = selectedOctave();
        label.textContent = octaveDisplayLabel + " " + currentOctave;
        downBtn.disabled = currentOctave <= minOctave;
        upBtn.disabled = currentOctave >= maxOctave;
      }
      function step(delta, direction) {
        if (changeSelectedOctave(delta)) {
          rerender();
          restoreFocus("[data-octave='" + direction + "']");
        } else {
          updateOctaveControls();
        }
      }
      var downBtn = makeAriaBtn("−", octaveDownLabel, "btn-outline-secondary", function () {
        step(-1, "down");
      });
      downBtn.setAttribute("data-octave", "down");
      octaveRow.appendChild(downBtn);
      label.className = "btn btn-sm btn-outline-secondary disabled";
      label.setAttribute("aria-live", "polite");
      label.setAttribute("role", "status");
      label.setAttribute("data-octave-status", "true");
      octaveRow.appendChild(label);
      var upBtn = makeAriaBtn("+", octaveUpLabel, "btn-outline-secondary", function () {
        step(1, "up");
      });
      upBtn.setAttribute("data-octave", "up");
      octaveRow.appendChild(upBtn);
      updateOctaveControls();
      paletteDiv.appendChild(octaveRow);
    }

    function appendNoteNames(canAdd) {
      var noteRow = makeGroup();
      NOTE_NAMES.forEach(function (nn) {
        var b = makeBtn(nn, "btn-outline-success", function () {
          addNote({ note: nn + octave, duration: selectedDuration });
          restoreFocus("[data-note-name='" + nn + "']");
        });
        b.setAttribute("data-note-name", nn);
        b.disabled = !canAdd;
        noteRow.appendChild(b);
      });
      paletteDiv.appendChild(noteRow);
    }

    // Accidentals apply to the selected note, even when no more notes fit.
    function appendAccidentals() {
      var accRow = makeGroup();
      var none = selectedEditableIdx() < 0;
      accidentals.forEach(function (acc) {
        var b = makeAriaBtn(acc.symbol, acc.label, "btn-outline-info", function () {
          var idx = selectedEditableIdx();
          if (idx < 0) return;
          var n = userNotes[idx];
          userNotes[idx] = Object.assign({}, n, { note: applyAccidental(n.note, acc.value) });
          selectedIndex = idx;
          sortChord();
          rerender();
          restoreFocus("[data-accidental='" + acc.name + "']");
        });
        b.setAttribute("data-accidental", acc.name);
        b.disabled = none;
        accRow.appendChild(b);
      });
      paletteDiv.appendChild(accRow);
    }

    function appendControls() {
      var ctrlRow = makeGroup();
      var empty = userNotes.length === 0;
      var undo = makeAriaBtn("↶", undoLabel, "btn-outline-secondary", function () {
        if (chord) {
          // A chord drops the note placed last, wherever it sits.
          var selected = userNotes[selectedIndex];
          userNotes.splice(lastEditableIdx(), 1);
          selectedIndex = userNotes.indexOf(selected);
          if (selectedIndex < 0) selectedIndex = lastEditableIdx();
        } else {
          userNotes.pop();
          if (selectedIndex >= userNotes.length) selectedIndex = lastEditableIdx();
        }
        rerender();
        restoreFocus("[data-action='undo']");
      });
      undo.setAttribute("data-action", "undo");
      undo.disabled = empty;
      ctrlRow.appendChild(undo);
      var clearBtn = makeAriaBtn("✕", clearLabel, "btn-outline-danger", function () {
        userNotes = [];
        selectedIndex = -1;
        rerender();
        restoreFocus("[data-action='clear']");
      });
      clearBtn.setAttribute("data-action", "clear");
      clearBtn.disabled = empty;
      ctrlRow.appendChild(clearBtn);
      paletteDiv.appendChild(ctrlRow);
    }

    // With measures or a chord every control stays in place, disabled when it can't be used.
    function buildPalette(lay) {
      paletteDiv.innerHTML = "";
      if (rhythm) {
        appendFigures(lay);
        appendControls();
        return;
      }
      var fixed = measures > 0 || chord;
      var canAdd = fits(selectedDuration, lay);
      if (isLinear) appendFigures(lay);
      if (fixed || canAdd || selectedEditableIdx() >= 0) appendOctaveRow();
      if (fixed || canAdd) appendNoteNames(canAdd);
      if (fixed || selectedEditableIdx() >= 0) appendAccidentals();
      if (fixed || userNotes.length > 0) appendControls();
    }

    rerender();

    var instance = {
      getValue: function () { return userNotes.slice(); },
      getAnswerString: function () {
        var lay = layout();
        var tokens = [];
        userNotes.forEach(function (n, i) {
          if (lay.starts[prefilledNotes.length + i]) tokens.push("bar");
          if (rhythm) tokens.push(n.duration);
          else if (n.note === "rest") tokens.push("rest:" + n.duration);
          else tokens.push(n.note + ":" + n.duration);
        });
        return tokens.join("|");
      },
      isComplete: function () { return !measures || layout().complete; },
      clear: function () { userNotes = []; selectedIndex = -1; rerender(); },
      destroy: function () { rootEl.innerHTML = ""; },
    };

    return instance;
  }

  root.StaffEditor = { attach: attach };
})(window);
