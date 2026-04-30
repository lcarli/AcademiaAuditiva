document.addEventListener("DOMContentLoaded", () => {
  AcademiaAuditiva.init();
  AudioEngine.setupWaveform();

  const loc = document.getElementById("localizer").dataset;

  let playToken = null;
  let roundId = null;
  let selectedGuess = "";
  let exerciseStartTime = Date.now();

  const exerciseId = document.getElementById("exerciseId")?.value;
  const keySelect = document.getElementById("keySelect");
  const directionSelect = document.getElementById("intervalDirection");

  const guessButtons = document.querySelectorAll(".guessAnswer");
  guessButtons.forEach((button) => {
    button.addEventListener("click", (e) => {
      guessButtons.forEach((btn) => btn.classList.remove("selected"));
      e.target.classList.add("selected");
      selectedGuess = e.target.value;
    });
  });

  const playBtn = document.getElementById("Play");
  if (playBtn) {
    playBtn.addEventListener("click", () => {
      const key = keySelect?.value || "C";
      const direction = directionSelect?.value || "asc";

      fetch("/Exercise/RequestPlay", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          exerciseId: exerciseId,
          filters: { keySelect: key, intervalDirection: direction },
        }),
      })
        .then((resp) => resp.json())
        .then((data) => {
          playToken = data.playToken;
          roundId = data.roundId;
          if (playToken) AudioEngine.playToken(playToken);
        })
        .catch((err) => console.error("Erro ao gerar intervalo completo:", err));
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

  // See GuessInterval.js: the per-note replay buttons are hidden so
  // the page can no longer leak note identities through repeated
  // single-note playback.
  const n1Btn = document.getElementById("Note1");
  if (n1Btn) n1Btn.style.display = "none";
  const n2Btn = document.getElementById("Note2");
  if (n2Btn) n2Btn.style.display = "none";

  const validateBtn = document.getElementById("validateGuess");
  if (validateBtn) {
    validateBtn.addEventListener("click", () => {
      if (!selectedGuess) {
        Swal.fire({ icon: "warning", title: loc.incompleteTitle, text: loc.incompleteText });
        return;
      }

      fetch("/Exercise/ValidateExercise", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          exerciseId: exerciseId,
          roundId: roundId,
          userGuess: selectedGuess,
          timeSpentSeconds: Math.floor((Date.now() - exerciseStartTime) / 1000),
        }),
      })
        .then((resp) => resp.json())
        .then((data) => {
          const correctCountEl = document.getElementById("correctCount");
          const errorCountEl = document.getElementById("errorCount");
          if (data.isCorrect) {
            if (correctCountEl) correctCountEl.innerText = parseInt(correctCountEl.innerText) + 1;
            Swal.fire("Correct!", "You got it right!", "success");
          } else {
            if (errorCountEl) errorCountEl.innerText = parseInt(errorCountEl.innerText) + 1;
            Swal.fire("Wrong!", `The correct answer was ${data.answer}.`, "error");
          }

          selectedGuess = "";
          playToken = null;
          roundId = null;
          guessButtons.forEach((btn) => btn.classList.remove("selected"));
        });
    });
  }
});
