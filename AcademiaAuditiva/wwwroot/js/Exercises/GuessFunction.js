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

  // Turning free practice on or off drops the round on screen.
  AAPractice.onReset(() => {
    playToken = null;
    roundId = null;
  });

  const playBtn = document.getElementById("Play");
  if (playBtn) {
    playBtn.addEventListener("click", () => {
      const key = keySelect?.value || "C";
      const scaleType = scaleTypeSelect?.value || "major";

      toggleFunctionButtons(scaleType);

      AAPractice.play({
        exerciseId: exerciseId,
        filters: { keySelect: key, scaleTypeSelect: scaleType },
      }).then((data) => {
        if (AAi18n.serverError(data, loc)) return;
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
      if (!selectedGuess || !roundId) {
        AAi18n.incomplete(loc);
        return;
      }

      AAPractice.validate({
        exerciseId: exerciseId,
        roundId: roundId,
        userGuess: selectedGuess,
        timeSpentSeconds: Math.floor((Date.now() - exerciseStartTime) / 1000),
      }).then((data) => {
        if (AAi18n.serverError(data, loc)) return;
        AAi18n.result(data, loc);

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
