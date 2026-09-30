document.addEventListener("DOMContentLoaded", () => {
  AcademiaAuditiva.init();
  AudioEngine.setupWaveform();

  const loc = AAi18n.localizer();
  let playToken = null;
  let roundId = null;
  let selectedGuess = "";
  let exerciseStartTime = Date.now();

  const exerciseId = document.getElementById("exerciseId")?.value;
  const keySelect = document.getElementById("keySelect");
  const scaleTypeSelect = document.getElementById("scaleTypeSelect");

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
      const scaleType = scaleTypeSelect?.value || "major";

      toggleFunctionButtons(scaleType);

      fetch("/Exercise/RequestPlay", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          exerciseId: exerciseId,
          filters: { keySelect: key, scaleTypeSelect: scaleType },
        }),
      })
        .then((resp) => resp.json())
        .then((data) => {
          playToken = data.playToken;
          roundId = data.roundId;
          if (playToken) AudioEngine.playToken(playToken);
        });
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
      if (!selectedGuess) {
        AAi18n.incomplete(loc);
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
          if (AAi18n.serverError(data, loc)) return;
          const correctCountEl = document.getElementById("correctCount");
          const errorCountEl = document.getElementById("errorCount");
          if (data.isCorrect) {
            if (correctCountEl) correctCountEl.innerText = parseInt(correctCountEl.innerText) + 1;
            AAi18n.result(data, loc);
          } else {
            if (errorCountEl) errorCountEl.innerText = parseInt(errorCountEl.innerText) + 1;
            AAi18n.result(data, loc);
          }

          selectedGuess = "";
          playToken = null;
          roundId = null;
          guessButtons.forEach((btn) => btn.classList.remove("selected"));
        });
    });
  }

  function toggleFunctionButtons(scaleType) {
    const majorFunctions = ["I", "ii", "iii", "IV", "V", "vi", "VII°"];
    const minorFunctions = ["i", "II°", "III", "iv", "v", "VI", "VII"];
    const guessButtons = document.querySelectorAll(".guessAnswer");

    guessButtons.forEach((btn) => {
      const label = btn.innerText.trim();
      if (scaleType === "major") {
        btn.style.display = majorFunctions.includes(label) ? "inline-block" : "none";
      } else if (scaleType === "minor") {
        btn.style.display = minorFunctions.includes(label) ? "inline-block" : "none";
      }
    });
  }

  scaleTypeSelect?.addEventListener("change", () => toggleFunctionButtons(scaleTypeSelect.value));
  toggleFunctionButtons(scaleTypeSelect?.value || "major");
});
