document.addEventListener("DOMContentLoaded", () => {
  AcademiaAuditiva.init();
  AudioEngine.setupWaveform();

  const loc = AAi18n.localizer();
  let playToken = null;
  let roundId = null;
  let selectedGuess = "";

  const exerciseId = document.getElementById("exerciseId")?.value;
  const qualitySelect = document.getElementById("tnQuality");

  const guessButtons = document.querySelectorAll(".guessAnswer");
  guessButtons.forEach((button) => {
    button.addEventListener("click", () => {
      guessButtons.forEach((btn) => btn.classList.remove("selected"));
      button.classList.add("selected");
      selectedGuess = button.value;
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
      AAPractice.play({
        exerciseId: exerciseId,
        filters: { tnQuality: qualitySelect?.value || "both" },
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
});
