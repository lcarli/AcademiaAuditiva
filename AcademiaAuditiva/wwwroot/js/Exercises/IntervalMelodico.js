document.addEventListener("DOMContentLoaded", () => {
  AcademiaAuditiva.init();
  AudioEngine.setupWaveform();

  const loc = AAi18n.localizer();
  const exerciseId = document.getElementById("exerciseId")?.value;
  const answerSelects = ["firstDegreeSelect", "lastDegreeSelect", "startIntervalSelect", "endIntervalSelect"]
    .map((id) => document.getElementById(id));

  // The melody is mixed on the server and addressed by an opaque token, so
  // the browser never learns the notes (or the answer) before validating.
  let playToken = null;
  let roundId = null;
  let roundStartedAt = Date.now();

  function resetSelections() {
    answerSelects.forEach((select) => {
      if (select) select.value = "";
    });
  }

  function showRequestError(err) {
    console.error("IntervalMelodico request failed:", err);
    Swal.fire({ icon: "error", title: loc.validationErrorTitle, text: loc.validationErrorText });
  }

  // Each part of the answer with its localized option label.
  function formatAnswer(answer) {
    const correct = String(answer || "").split("|");
    return correct.length >= 4 && loc.answerFormat
      ? correct.slice(0, 4).reduce(
          (text, part, i) => text.replace(`{${i}}`, AAi18n.answerLabel(part)),
          loc.answerFormat)
      : answer;
  }

  // The format is written to follow "The correct answer was"; alone in the dialog it starts a sentence.
  AAPractice.setAnswerView((answer) => {
    const text = AAi18n.answerLabel(formatAnswer(answer));
    return text.charAt(0).toUpperCase() + text.slice(1);
  });

  // Turning free practice on or off drops the round on screen.
  AAPractice.onReset(() => {
    playToken = null;
    roundId = null;
  });

  const playBtn = document.getElementById("Play");
  if (playBtn) {
    playBtn.addEventListener("click", () => {
      if (!exerciseId) return;

      AAPractice.play({
        exerciseId: exerciseId,
        filters: {
          keySelect: document.getElementById("keySelect")?.value || "C",
          scaleTypeSelect: document.getElementById("scaleTypeSelect")?.value || "major",
        },
      })
        .then((data) => {
          if (AAi18n.serverError(data, loc)) return;
          playToken = data.playToken;
          roundId = data.roundId;
          roundStartedAt = Date.now();
          resetSelections();
          if (playToken) AudioEngine.playToken(playToken);
        })
        .catch(showRequestError);
    });
  }

  const replayBtn = document.getElementById("Replay");
  if (replayBtn) {
    replayBtn.addEventListener("click", () => {
      if (!playToken) {
        AAi18n.noAudio(loc);
        return;
      }
      AudioEngine.playToken(playToken);
    });
  }

  const validateBtn = document.getElementById("validateGuess");
  if (validateBtn) {
    validateBtn.addEventListener("click", () => {
      if (!roundId) {
        AAi18n.noAudio(loc);
        return;
      }

      const parts = answerSelects.map((select) => (select ? select.value : ""));
      if (parts.some((part) => !part)) {
        AAi18n.incomplete(loc);
        return;
      }

      AAPractice.validate({
        exerciseId: exerciseId,
        roundId: roundId,
        userGuess: parts.join("|"),
        timeSpentSeconds: Math.floor((Date.now() - roundStartedAt) / 1000),
      })
        .then((data) => {
          if (AAi18n.serverError(data, loc)) return;

          if (data.isCorrect) {
            AAi18n.result(data, loc);
          } else {
            AAi18n.result({ ...data, answer: formatAnswer(data.answer) }, loc);
          }

          resetSelections();
          playToken = null;
          roundId = null;
        })
        .catch(showRequestError);
    });
  }
});