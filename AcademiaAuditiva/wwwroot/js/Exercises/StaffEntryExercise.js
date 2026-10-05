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

  function optionsFor(exerciseName, metadata) {
    const octave = Number(metadata.octave || 4);
    const labels = {
      minOctave: 3,
      maxOctave: 6,
      octaveDisplayLabel: AAi18n.localizer().octaveLabel,
      octaveDownLabel: AAi18n.localizer().octaveDownLabel,
      octaveUpLabel: AAi18n.localizer().octaveUpLabel,
      selectedNoteLabel: AAi18n.localizer().selectedNoteLabel,
      noSelectionLabel: AAi18n.localizer().noSelectionLabel,
    };
    if (exerciseName === "CompleteChord") {
      return {
        ...labels,
        clef: "treble",
        keySignature: "C",
        octave,
        allowedDurations: ["w"],
        restDurations: [],
        showBarline: false,
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
        showBarline: false,
        totalSlots: 8,
      };
    }
    const prefilledNotes = exerciseName === "MelodicDictation" && metadata.firstNote
      ? [{ note: metadata.firstNote, duration: metadata.firstDuration || "q" }]
      : [];
    return {
      ...labels,
      clef: "treble",
      keySignature: "C",
      timeSignature: metadata.timeSignature || "4/4",
      octave,
      allowedDurations: ["w", "h", "q", "8"],
      restDurations: ["wr", "hr", "qr", "8r"],
      showBarline: true,
      totalSlots: 64,
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

      // The answer leaves out the notes the editor gives (in gray); rhythms are drawn on C5.
      AAPractice.setAnswerView((answer) => {
        const options = optionsFor(exerciseName, metadata || {});
        const given = (options.prefilledNotes || []).map((note) => ({ ...note, prefilled: true }));
        return {
          staff: {
            clef: options.clef,
            keySignature: options.keySignature,
            timeSignature: options.timeSignature,
            notes: given.concat(AAPractice.staffNotes(answer, exerciseName === "RhythmDictation" ? "C5" : null)),
          },
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
