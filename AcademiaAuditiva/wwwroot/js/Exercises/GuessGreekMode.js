document.addEventListener("DOMContentLoaded", () => {
  AcademiaAuditiva.init();
  AudioEngine.setupWaveform();

  const loc = document.getElementById("localizer").dataset;
  const exerciseId = document.getElementById("exerciseId")?.value;

  // Front never sees the actual mode — backend mixes the audio
  // server-side and only sends back an opaque token + roundId.
  let playToken = null;
  let roundId = null;
  let selectedGuess = "";
  const exerciseStartTime = Date.now();

  const guessButtons = document.querySelectorAll(".guessAnswer");
  guessButtons.forEach((button) => {
    button.addEventListener("click", (e) => {
      guessButtons.forEach((btn) => btn.classList.remove("selected"));
      e.currentTarget.classList.add("selected");
      selectedGuess = e.currentTarget.value;
    });
  });

  const playBtn = document.getElementById("Play");
  if (playBtn) {
    playBtn.addEventListener("click", () => {
      if (!exerciseId) return;
      fetch("/Exercise/RequestPlay", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ exerciseId: exerciseId }),
      })
        .then((resp) => resp.json())
        .then((data) => {
          playToken = data.playToken;
          roundId = data.roundId;
          if (playToken) AudioEngine.playToken(playToken);
        })
        .catch((err) => console.error("Erro ao preparar GuessGreekMode:", err));
    });
  }

  const replayBtn = document.getElementById("Replay");
  if (replayBtn) {
    replayBtn.addEventListener("click", () => {
      if (!playToken) {
        Swal.fire({ icon: "warning", title: loc.incompleteTitle, text: loc.incompleteText });
        return;
      }
      AudioEngine.playToken(playToken);
    });
  }

  const validateBtn = document.getElementById("validateGuess");
  if (validateBtn) {
    validateBtn.addEventListener("click", () => {
      if (!selectedGuess || !roundId) {
        Swal.fire({ icon: "warning", title: loc.incompleteTitle, text: loc.incompleteText });
        return;
      }

      fetch("/Exercise/ValidateExercise", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          ExerciseId: exerciseId,
          RoundId: roundId,
          userGuess: selectedGuess,
          timeSpentSeconds: Math.floor((Date.now() - exerciseStartTime) / 1000),
        }),
      })
        .then((resp) => resp.json())
        .then((data) => {
          const correctCountEl = document.getElementById("correctCount");
          const errorCountEl = document.getElementById("errorCount");

          // Translate canonical mode key to a localized label so the
          // post-validation feedback matches the UI vocabulary.
          const labels = {
            ionian: loc.answerIonian,
            dorian: loc.answerDorian,
            phrygian: loc.answerPhrygian,
            lydian: loc.answerLydian,
            mixolydian: loc.answerMixolydian,
            aeolian: loc.answerAeolian,
            locrian: loc.answerLocrian,
          };
          const answerLabel = labels[data.answer] || data.answer || "";

          if (data.isCorrect) {
            if (correctCountEl) {
              correctCountEl.innerText = parseInt(correctCountEl.innerText) + 1;
            }
            Swal.fire(loc.correctMessage, loc.correctMessageText, "success");
          } else {
            if (errorCountEl) {
              errorCountEl.innerText = parseInt(errorCountEl.innerText) + 1;
            }
            Swal.fire(loc.wrongMessage, `${loc.wrongMessageText} ${answerLabel}`, "error");
          }

          selectedGuess = "";
          playToken = null;
          roundId = null;
          guessButtons.forEach((btn) => btn.classList.remove("selected"));
        });
    });
  }
});
