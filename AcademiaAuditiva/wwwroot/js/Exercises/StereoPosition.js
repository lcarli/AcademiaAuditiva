document.addEventListener("DOMContentLoaded", () => {
  AcademiaAuditiva.init();

  const loc = AAi18n.localizer();
  const exerciseId = document.getElementById("exerciseId")?.value;
  const levelSelect = document.getElementById("spLevel");
  const ab = ABPlayback.create(document.querySelector("[data-aa-ab]"));
  const answerButtons = [...document.querySelectorAll(".guessAnswer")];
  let roundId = null;
  let selected = "";

  function level() {
    return levelSelect?.value || "beginner";
  }

  function select(button) {
    selected = button?.value || "";
    answerButtons.forEach((candidate) => {
      const active = candidate === button;
      candidate.classList.toggle("selected", active);
      candidate.setAttribute("aria-pressed", active ? "true" : "false");
    });
  }

  function clearRound() {
    roundId = null;
    select(null);
    ab?.clear();
  }

  function showLevel() {
    const activeLevel = level();
    answerButtons.forEach((button) => {
      button.hidden = !button.dataset.levels.split(" ").includes(activeLevel);
    });
    select(null);
  }

  answerButtons.forEach((button) => {
    button.setAttribute("aria-pressed", "false");
    button.addEventListener("click", () => select(button));
  });

  levelSelect?.addEventListener("change", () => {
    AAPractice.reset();
    showLevel();
  });
  AAPractice.onReset(clearRound);
  showLevel();

  document.getElementById("Play")?.addEventListener("click", () => {
    AAPractice.play({
      exerciseId,
      filters: { spLevel: level() },
    }).then((data) => {
      if (AAi18n.serverError(data, loc)) return;
      try {
        ab.setRound(data.clips);
      } catch (error) {
        console.error("Invalid stereo round:", error);
        AAPractice.reset();
        Swal.fire({
          icon: "error",
          title: loc.validationErrorTitle,
          text: loc.validationErrorText,
        });
        return;
      }
      roundId = data.roundId;
      select(null);
      ab.play("A");
    });
  });

  document.getElementById("validateGuess")?.addEventListener("click", () => {
    if (!roundId || !selected) {
      AAi18n.incomplete(loc);
      return;
    }

    AAPractice.validate({ exerciseId, roundId, userGuess: selected }).then((data) => {
      if (AAi18n.serverError(data, loc)) return;

      const answer = data.detail?.answer ?? data.correctAnswer;
      const label = answerButtons.find((button) => button.value === answer)?.textContent.trim() || String(answer ?? "");
      const options = AAi18n.withRewards({
        icon: data.isCorrect ? "success" : "error",
        title: data.isCorrect ? loc.correctMessage : loc.wrongMessage,
        text: String(loc.feedback || "").replace("{0}", label),
      }, data);
      Swal.fire(options);
      clearRound();
    });
  });
});
