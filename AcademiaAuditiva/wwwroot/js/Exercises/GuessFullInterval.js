document.addEventListener("DOMContentLoaded", () => {
  AcademiaAuditiva.init();
  AudioEngine.setupWaveform();

  const loc = AAi18n.localizer();

  let playToken = null;
  let roundId = null;
  let selectedGuess = "";

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

  // Turning free practice on or off drops the round on screen.
  AAPractice.onReset(() => {
    playToken = null;
    roundId = null;
  });

  const playBtn = document.getElementById("Play");
  if (playBtn) {
    playBtn.addEventListener("click", () => {
      const key = keySelect?.value || "C";
      const direction = directionSelect?.value || "asc";

      AAPractice.play({
        exerciseId: exerciseId,
        filters: { keySelect: key, intervalDirection: direction },
      })
        .then((data) => {
          if (AAi18n.serverError(data, loc)) return;
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
        AAi18n.noAudio(loc);
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
