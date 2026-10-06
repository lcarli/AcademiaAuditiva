(function (window, document) {
  "use strict";

  function format(template, values) {
    return String(template || "").replace(/\{\{(\d+)\}\}/g, (_, index) => values[Number(index)] ?? "");
  }

  function scaleLabel(loc, scale) {
    const key = "scale" + String(scale || "").charAt(0).toUpperCase() + String(scale || "").slice(1);
    return loc[key] || scale || "";
  }

  function qualityLabel(loc, quality) {
    const key = "quality" + String(quality || "").charAt(0).toUpperCase() + String(quality || "").slice(1);
    return loc[key] || quality || "";
  }

  function promptFor(exerciseName, loc, metadata) {
    if (exerciseName === "CompleteChord") {
      return format(loc.completeChordPrompt, [qualityLabel(loc, metadata.quality), metadata.promptNotes?.[0] || ""]);
    }
    if (exerciseName === "TransposeScale") {
      return format(loc.transposeScalePrompt, [scaleLabel(loc, metadata.scale), metadata.originalRoot || "", metadata.targetRoot || ""]);
    }
    if (exerciseName === "MelodicDictation") {
      return format(loc.melodicDictationPrompt, [metadata.root || "", metadata.firstNote || "", metadata.timeSignature || "4/4"]);
    }
    if (exerciseName === "RhythmDictation") {
      return format(loc.rhythmDictationPrompt, [metadata.timeSignature || "4/4"]);
    }
    return "";
  }

  function filterValues() {
    const filters = {};
    document.querySelectorAll("#filtersModal select").forEach((select) => {
      filters[select.name || select.id] = select.value;
    });
    return filters;
  }

  // #localizer keys of the note value names (buttons and the answer read-out).
  const FIGURE_KEYS = {
    w: "figureWhole", h: "figureHalf", q: "figureQuarter", "8": "figureEighth",
    wr: "figureWholeRest", hr: "figureHalfRest", qr: "figureQuarterRest", "8r": "figureEighthRest",
  };

  function figureLabels(loc) {
    const labels = {};
    Object.keys(FIGURE_KEYS).forEach((duration) => {
      if (loc[FIGURE_KEYS[duration]]) labels[duration] = loc[FIGURE_KEYS[duration]];
    });
    return labels;
  }

  // The answer staff read out: "E4 quarter note, quarter rest; G4 half note".
  // Note values are named only when the exercise has more than one.
  function spokenNotes(loc, notes, options) {
    const names = figureLabels(loc);
    const named = options.allowedDurations.length > 1;
    const measures = [[]];
    notes.forEach((n) => {
      if (n.note === "barline") {
        measures.push([]);
        return;
      }
      const figure = (names[n.duration] || n.duration).toLowerCase();
      let text = n.note;
      if (n.note === "rest" || options.rhythm) text = figure;
      else if (named) text = `${n.note} ${figure}`;
      measures[measures.length - 1].push(text);
    });
    const text = measures.filter((m) => m.length).map((m) => m.join(", ")).join("; ");
    return text.charAt(0).toUpperCase() + text.slice(1);
  }

  function optionsFor(exerciseName, metadata) {
    const loc = AAi18n.localizer();
    const octave = Number(metadata.octave || 4);
    const labels = {
      minOctave: 3,
      maxOctave: 6,
      octaveDisplayLabel: loc.octaveLabel,
      octaveDownLabel: loc.octaveDownLabel,
      octaveUpLabel: loc.octaveUpLabel,
      selectedNoteLabel: loc.selectedNoteLabel,
      noSelectionLabel: loc.noSelectionLabel,
      sharpLabel: loc.sharpLabel,
      flatLabel: loc.flatLabel,
      naturalLabel: loc.naturalLabel,
      undoLabel: loc.undoLabel,
      clearLabel: loc.clearLabel,
    };
    if (exerciseName === "CompleteChord") {
      return {
        ...labels,
        clef: "treble",
        keySignature: "C",
        octave,
        allowedDurations: ["w"],
        restDurations: [],
        totalSlots: 4,
        prefilledNotes: (metadata.promptNotes || []).map((note) => ({ note, duration: "w" })),
      };
    }
    if (exerciseName === "TransposeScale") {
      return {
        ...labels,
        clef: "treble",
        keySignature: "C",
        octave,
        allowedDurations: ["q"],
        restDurations: [],
        totalSlots: 8,
      };
    }
    // A dictation fills metadata.numMeasures measures with the note values of its level.
    const rhythm = exerciseName === "RhythmDictation";
    const durations = Array.isArray(metadata.durations) && metadata.durations.length
      ? metadata.durations
      : ["w", "h", "q", "8"];
    const rests = metadata.rests === undefined || metadata.rests === true;
    const prefilledNotes = !rhythm && metadata.firstNote
      ? [{ note: metadata.firstNote, duration: metadata.firstDuration || "q" }]
      : [];
    return {
      ...labels,
      clef: "treble",
      keySignature: "C",
      timeSignature: metadata.timeSignature || "4/4",
      octave,
      allowedDurations: durations,
      restDurations: rests ? durations.map((d) => d + "r") : [],
      measures: Number(metadata.numMeasures) || 0,
      rhythm,
      autoStem: !rhythm,
      figureLabels: figureLabels(loc),
      measureLabel: loc.measureLabel,
      completeLabel: loc.completeLabel,
      prefilledNotes,
    };
  }

  window.AAStaffEntryExercise = {
    init() {
      AcademiaAuditiva.init();
      AudioEngine.setupWaveform();

      const loc = AAi18n.localizer();
      const exerciseId = document.getElementById("exerciseId")?.value;
      const exerciseName = document.getElementById("staffExerciseName")?.value;
      const prompt = document.getElementById("staffPrompt");
      const initialPrompt = prompt ? prompt.textContent : "";
      let playToken = null;
      let roundId = null;
      let metadata = null;
      let staffInstance = null;

      function drawEmptyStaff() {
        window.StaffRenderer.render("#staffEditor", {
          clef: "treble",
          keySignature: "C",
          notes: [],
        });
      }

      drawEmptyStaff();

      // The answer leaves out the notes the editor gives (in gray); rhythms go on a one-line staff.
      AAPractice.setAnswerView((answer) => {
        const options = optionsFor(exerciseName, metadata || {});
        const given = (options.prefilledNotes || []).map((note) => ({ ...note, prefilled: true }));
        const notes = given.concat(AAPractice.staffNotes(answer, options.rhythm ? "B4" : null));
        return {
          staff: {
            clef: options.clef,
            keySignature: options.keySignature,
            timeSignature: options.timeSignature,
            rhythm: options.rhythm,
            autoStem: options.autoStem,
            notes,
          },
          label: spokenNotes(loc, notes, options),
        };
      });

      // Turning free practice on or off drops the round on screen.
      AAPractice.onReset(() => {
        playToken = null;
        roundId = null;
        metadata = null;
        if (staffInstance) staffInstance.destroy();
        staffInstance = null;
        if (prompt) prompt.textContent = initialPrompt;
        drawEmptyStaff();
      });

      document.getElementById("Play")?.addEventListener("click", () => {
        if (!exerciseId || !exerciseName) return;
        AAPractice.play({ exerciseId, filters: filterValues() }).then((data) => {
          if (AAi18n.serverError(data, loc)) return;
          playToken = data.playToken;
          roundId = data.roundId;
          metadata = data.metadata || {};
          if (prompt) prompt.textContent = promptFor(exerciseName, loc, metadata);
          staffInstance = window.StaffEditor.attach("#staffEditor", optionsFor(exerciseName, metadata));
          if (playToken) AudioEngine.playToken(playToken);
        });
      });

      document.getElementById("Replay")?.addEventListener("click", () => {
        if (!playToken) {
          AAi18n.noAudio(loc);
          return;
        }
        AudioEngine.playToken(playToken);
      });

      document.getElementById("validateGuess")?.addEventListener("click", () => {
        if (!staffInstance || !roundId) {
          AAi18n.incomplete(loc);
          return;
        }
        const userAnswer = staffInstance.getAnswerString();
        if (!userAnswer) {
          AAi18n.incomplete(loc);
          return;
        }
        // A dictation is checked once every measure is written.
        if (!staffInstance.isComplete()) {
          AAi18n.warning(loc.incompleteTitle, loc.unfinishedMeasuresText || loc.incompleteText);
          return;
        }
        AAPractice.validate({
          ExerciseId: exerciseId,
          RoundId: roundId,
          userGuess: userAnswer,
        }).then((data) => {
          if (AAi18n.serverError(data, loc)) return;
          AAi18n.result(data, loc);
          staffInstance?.clear();
          playToken = null;
          roundId = null;
        });
      });
    },
  };
})(window, document);
