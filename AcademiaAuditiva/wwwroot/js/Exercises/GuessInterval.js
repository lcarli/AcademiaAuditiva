document.addEventListener("DOMContentLoaded", () => {
  AcademiaAuditiva.init();
  AudioEngine.setupWaveform();

  const loc = AAi18n.localizer();

  let playToken = null;
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

  // Turning free practice on or off drops the round on screen.
  AAPractice.onReset(() => {
    playToken = null;
    roundId = null;
  });

  const playBtn = document.getElementById("Play");
  if (playBtn) {
    playBtn.addEventListener("click", () => {
      const tonic = document.getElementById("keySelect")?.value || "C";
      const scaleType = document.getElementById("scaleTypeSelect")?.value || "major";

      AAPractice.play({
        exerciseId: exerciseId,
        filters: { keySelect: tonic, scaleTypeSelect: scaleType },
      })
        .then((data) => {
          if (AAi18n.serverError(data, loc)) return;
          playToken = data.playToken;
          roundId = data.roundId;
          if (playToken) AudioEngine.playToken(playToken);
        })
        .catch((err) => console.error("Erro ao preparar intervalo:", err));
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

  // The legacy "play just note 1 / note 2" buttons relied on the
  // front-end knowing both pitches in clear text. With token-based
  // playback, the round only ships one mixed clip — we hide those
  // buttons so the markup stays as-is even if they happen to render.
  const n1Btn = document.getElementById("Note1");
  if (n1Btn) n1Btn.style.display = "none";
  const n2Btn = document.getElementById("Note2");
  if (n2Btn) n2Btn.style.display = "none";

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
});
