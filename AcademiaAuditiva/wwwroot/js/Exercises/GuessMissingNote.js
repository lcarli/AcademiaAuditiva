document.addEventListener("DOMContentLoaded", () => {
  AcademiaAuditiva.init();
  AudioEngine.setupWaveform();

  const loc = document.getElementById("localizer").dataset;

  // Two tokens — one per melody. The user MUST listen to both clips
  // and decide same/different by ear; the front cannot infer the
  // answer from the response (no melody arrays leak any more).
  let melody1Token = null;
  let melody2Token = null;
  let roundId = null;
  let selectedGuess = "";
  let exerciseStartTime = Date.now();

  const exerciseId = document.getElementById("exerciseId")?.value;

  const guessButtons = document.querySelectorAll(".guessAnswer");
  guessButtons.forEach((button) => {
    button.addEventListener("click", (e) => {
      guessButtons.forEach((btn) => btn.classList.remove("selected"));
      e.target.classList.add("selected");
      selectedGuess = e.target.value;
    });
  });

  async function playBoth(gapSeconds) {
    if (!melody1Token || !melody2Token) return;
    await AudioEngine.playToken(melody1Token);
    await new Promise((r) => setTimeout(r, gapSeconds * 1000));
    await AudioEngine.playToken(melody2Token);
  }

  const playBtn = document.getElementById("Play");
  if (playBtn) {
    playBtn.addEventListener("click", () => {
      const length = document.getElementById("melodyLength")?.value || 5;

      fetch("/Exercise/RequestPlay", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          exerciseId: exerciseId,
          filters: { melodyLength: length },
        }),
      })
        .then((resp) => resp.json())
        .then((data) => {
          melody1Token = data.melody1Token;
          melody2Token = data.melody2Token;
          roundId = data.roundId;
          // 1s gap between the two melodies, matching the legacy UX.
          playBoth(1.0);
        })
        .catch((err) => console.error("Erro ao gerar melodias:", err));
    });
  }

  const replayBtn = document.getElementById("Replay");
  if (replayBtn) {
    replayBtn.addEventListener("click", () => {
      if (!melody1Token || !melody2Token) {
        Swal.fire({ icon: "warning", title: loc.incompleteTitle, text: loc.incompleteText });
        return;
      }
      playBoth(0.6);
    });
  }

  const m1Btn = document.getElementById("Melody1");
  if (m1Btn) {
    m1Btn.addEventListener("click", () => {
      if (melody1Token) AudioEngine.playToken(melody1Token);
    });
  }

  const m2Btn = document.getElementById("Melody2");
  if (m2Btn) {
    m2Btn.addEventListener("click", () => {
      if (melody2Token) AudioEngine.playToken(melody2Token);
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
          melody1Token = null;
          melody2Token = null;
          roundId = null;
          guessButtons.forEach((btn) => btn.classList.remove("selected"));
        });
    });
  }
});
