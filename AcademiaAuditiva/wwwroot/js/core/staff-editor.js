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
 *     allowedDurations: ['w'],          // single → "non-linear" mode (no duration picker)
 *     restDurations: [],
 *     showBarline: false,
 *     totalSlots: 6,                    // hard cap when not in linear mode
 *     prefilledNotes: [{ note: 'C4', duration: 'w', prefilled: true }],
 *     onChange: function (notes) { ... }
 *   });
 *   editor.getValue();    // → array of { note, duration } the user placed
 *   editor.getAnswerString(); // → "D4:w|E4:w|..." canonical form
 *   editor.clear();
 *   editor.destroy();
 */
(function (root) {
  "use strict";

  var NOTE_NAMES = ["C", "D", "E", "F", "G", "A", "B"];

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
    return b;
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
    var allowedDurations = opts.allowedDurations && opts.allowedDurations.length
      ? opts.allowedDurations.slice() : ["w"];
    var restDurations = opts.restDurations || [];
    var showBarline = opts.showBarline === true;
    var totalSlots = opts.totalSlots || 99;
    var prefilledNotes = (opts.prefilledNotes || []).map(function (n) {
      return Object.assign({}, n, { prefilled: true });
    });
    var onChange = typeof opts.onChange === "function" ? opts.onChange : function () { };

    var isLinear = allowedDurations.length > 1;
    var userNotes = [];
    var selectedDuration = allowedDurations[0];

    rootEl.innerHTML = "";
    rootEl.classList.add("staff-editor-root");

    var staffDiv = document.createElement("div");
    staffDiv.className = "staff-editor-staff text-center";
    rootEl.appendChild(staffDiv);

    var paletteDiv = document.createElement("div");
    paletteDiv.className = "staff-editor-palette mt-3 d-flex flex-wrap justify-content-center gap-2";
    rootEl.appendChild(paletteDiv);

    function rerender() {
      var allNotes = prefilledNotes.concat(userNotes.map(function (n) {
        return Object.assign({ userPlaced: true }, n);
      }));
      window.StaffRenderer.render(staffDiv, {
        clef: clef,
        keySignature: keySig,
        timeSignature: timeSig,
        notes: allNotes,
      });
      buildPalette();
      onChange(userNotes.slice());
    }

    function canAddMore() {
      return isLinear ? true : userNotes.length < totalSlots;
    }

    function lastEditableIdx() {
      for (var i = userNotes.length - 1; i >= 0; i--) {
        if (userNotes[i].note !== "barline") return i;
      }
      return -1;
    }

    function buildPalette() {
      paletteDiv.innerHTML = "";

      // Duration picker (linear mode only).
      if (isLinear && canAddMore()) {
        var durRow = document.createElement("div");
        durRow.className = "btn-group";
        allowedDurations.forEach(function (d) {
          var b = makeBtn(d, d === selectedDuration ? "btn-primary" : "btn-outline-primary",
            function () { selectedDuration = d; buildPalette(); });
          durRow.appendChild(b);
        });
        paletteDiv.appendChild(durRow);

        // Rests
        if (restDurations.length) {
          var restRow = document.createElement("div");
          restRow.className = "btn-group";
          restDurations.forEach(function (rd) {
            restRow.appendChild(makeBtn("⏸ " + rd, "btn-outline-warning", function () {
              if (!canAddMore()) return;
              userNotes.push({ note: "rest", duration: rd });
              rerender();
            }));
          });
          paletteDiv.appendChild(restRow);
        }

        // Barline
        if (showBarline) {
          paletteDiv.appendChild(makeBtn("𝄀", "btn-outline-dark", function () {
            userNotes.push({ note: "barline", duration: "barline" });
            rerender();
          }));
        }
      }

      function updateOctaveControls(label, downBtn, upBtn) {
        label.textContent = octaveDisplayLabel + " " + octave;
        downBtn.disabled = octave <= minOctave;
        upBtn.disabled = octave >= maxOctave;
      }

      function appendOctaveRow() {
        var octaveRow = document.createElement("div");
        octaveRow.className = "btn-group align-items-center";
        var label = document.createElement("span");
        var downBtn = makeAriaBtn("−", octaveDownLabel, "btn-outline-secondary", function () {
          octave = Math.max(minOctave, octave - 1);
          updateOctaveControls(label, downBtn, upBtn);
        });
        octaveRow.appendChild(downBtn);
        label.className = "btn btn-sm btn-outline-secondary disabled";
        label.setAttribute("aria-live", "polite");
        label.setAttribute("role", "status");
        octaveRow.appendChild(label);
        var upBtn = makeAriaBtn("+", octaveUpLabel, "btn-outline-secondary", function () {
          octave = Math.min(maxOctave, octave + 1);
          updateOctaveControls(label, downBtn, upBtn);
        });
        octaveRow.appendChild(upBtn);
        updateOctaveControls(label, downBtn, upBtn);
        paletteDiv.appendChild(octaveRow);
      }

      // Note name buttons
      if (canAddMore()) {
        appendOctaveRow();
        var noteRow = document.createElement("div");
        noteRow.className = "btn-group";
        NOTE_NAMES.forEach(function (nn) {
          noteRow.appendChild(makeBtn(nn, "btn-outline-success", function () {
            userNotes.push({ note: nn + octave, duration: selectedDuration });
            rerender();
          }));
        });
        paletteDiv.appendChild(noteRow);
      }

      // Accidentals (apply to last placed editable note, even when no slots remain).
      if (lastEditableIdx() >= 0) {
        var accRow = document.createElement("div");
        accRow.className = "btn-group";
        ["#", "b", ""].forEach(function (acc) {
          var sym = acc === "#" ? "♯" : acc === "b" ? "♭" : "♮";
          accRow.appendChild(makeBtn(sym, "btn-outline-info", function () {
            var idx = lastEditableIdx();
            if (idx < 0) return;
            var n = userNotes[idx];
            userNotes[idx] = Object.assign({}, n, { note: applyAccidental(n.note, acc) });
            rerender();
          }));
        });
        paletteDiv.appendChild(accRow);
      }
      // Undo / Clear
      if (userNotes.length > 0) {
        var ctrlRow = document.createElement("div");
        ctrlRow.className = "btn-group";
        ctrlRow.appendChild(makeBtn("↶", "btn-outline-secondary", function () {
          userNotes.pop();
          rerender();
        }));
        ctrlRow.appendChild(makeBtn("✕", "btn-outline-danger", function () {
          userNotes = [];
          rerender();
        }));
        paletteDiv.appendChild(ctrlRow);
      }
    }

    rerender();

    var instance = {
      getValue: function () { return userNotes.slice(); },
      getAnswerString: function () {
        return userNotes.map(function (n) {
          if (n.note === "barline") return "bar";
          if (n.note === "rest") return "rest:" + n.duration;
          return n.note + ":" + n.duration;
        }).join("|");
      },
      clear: function () { userNotes = []; rerender(); },
      destroy: function () { rootEl.innerHTML = ""; },
    };

    return instance;
  }

  root.StaffEditor = { attach: attach };
})(window);
