document.addEventListener("DOMContentLoaded", () => {
  AcademiaAuditiva.init();
  AudioEngine.setupWaveform();

  const loc = AAi18n.localizer();

  // One token per melody: the page never learns the notes, nor which one changed.
  let melody1Token = null;
  let melody2Token = null;
  let roundId = null;
  let position = "";
  let direction = "";

  const exerciseId = document.getElementById("exerciseId")?.value;
  const positionButtons = document.querySelectorAll(".guessPosition");
  const directionButtons = document.querySelectorAll(".guessDirection");

  function choose(buttons, onChoose) {
    buttons.forEach((button) => {
      button.addEventListener("click", (e) => {
        buttons.forEach((btn) => btn.classList.remove("selected"));
        e.currentTarget.classList.add("selected");
        onChoose(e.currentTarget.value);
      });
    });
  }
  choose(positionButtons, (value) => (position = value));
  choose(directionButtons, (value) => (direction = value));

  function melodyLength() {
    return Number(document.getElementById("melodyLength")?.value) || 5;
  }

  // A button for each note of the melody.
  function showPositions(notes) {
    positionButtons.forEach((button) => {
      const shown = Number(button.value) <= notes;
      button.style.display = shown ? "" : "none";
      if (!shown && button.value === position) {
        button.classList.remove("selected");
        position = "";
      }
    });
  }
  showPositions(melodyLength());
  // A round on screen keeps its notes until the next Play.
  document.getElementById("melodyLength")?.addEventListener("change", () => {
    if (!roundId) showPositions(melodyLength());
  });

  function clearAnswer() {
    position = "";
    direction = "";
    positionButtons.forEach((btn) => btn.classList.remove("selected"));
    directionButtons.forEach((btn) => btn.classList.remove("selected"));
  }

  // "3|up" as "note 3, which went up".
  function formatAnswer(answer) {
    const [note, moved] = String(answer || "").split("|");
    const format = moved === "down" ? loc.answerDown : loc.answerUp;
    return note && moved && format ? format.replace("{0}", note) : answer;
  }

  // The format is written to follow "The correct answer was"; alone in the dialog it starts a sentence.
  AAPractice.setAnswerView((answer) => {
    const text = formatAnswer(answer);
    return text.charAt(0).toUpperCase() + text.slice(1);
  });

  async function playBoth(gapSeconds) {
    if (!melody1Token || !melody2Token) return;
    await AudioEngine.playToken(melody1Token);
    await new Promise((r) => setTimeout(r, gapSeconds * 1000));
    await AudioEngine.playToken(melody2Token);
  }

  // Turning free practice on or off drops the round on screen.
  AAPractice.onReset(() => {
    melody1Token = null;
    melody2Token = null;
    roundId = null;
    showPositions(melodyLength());
  });

  const playBtn = document.getElementById("Play");
  if (playBtn) {
    playBtn.addEventListener("click", () => {
      AAPractice.play({
        exerciseId: exerciseId,
        filters: { melodyLength: String(melodyLength()) },
      })
        .then((data) => {
          if (AAi18n.serverError(data, loc)) return;
          melody1Token = data.melody1Token;
          melody2Token = data.melody2Token;
          roundId = data.roundId;
          showPositions(data.notes || melodyLength());
          playBoth(1.0);
        })
        .catch((err) => console.error("GuessChangedNote request failed:", err));
    });
  }

  const replayBtn = document.getElementById("Replay");
  if (replayBtn) {
    replayBtn.addEventListener("click", () => {
      if (!melody1Token || !melody2Token) {
        AAi18n.noAudio(loc);
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
      if (!position || !direction || !roundId) {
        AAi18n.incomplete(loc);
        return;
      }

      AAPractice.validate({
        exerciseId: exerciseId,
        roundId: roundId,
        userGuess: `${position}|${direction}`,
      })
        .then((data) => {
          if (AAi18n.serverError(data, loc)) return;
          AAi18n.result(data.isCorrect ? data : { ...data, answer: formatAnswer(data.answer) }, loc);

          clearAnswer();
          melody1Token = null;
          melody2Token = null;
          roundId = null;
        })
        .catch((err) => console.error("GuessChangedNote validation failed:", err));
    });
  }
});
