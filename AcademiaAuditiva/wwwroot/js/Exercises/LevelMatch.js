document.addEventListener("DOMContentLoaded", () => {
  AcademiaAuditiva.init();

  const loc = AAi18n.localizer();
  const exerciseId = document.getElementById("exerciseId")?.value;
  const levelSelect = document.getElementById("lmLevel");
  const root = document.querySelector("[data-aa-ab]");
  const ab = ABPlayback.create(root);
  const sideButtons = [...document.querySelectorAll(".guessAnswer")];
  const differenceButtons = [...document.querySelectorAll(".differenceAnswer")];
  const differenceSection = document.querySelector("[data-aa-lm-difference]");
  let roundId = null;
  let selectedSide = "";
  let selectedDifference = "";

  function level() {
    return levelSelect?.value || "beginner";
  }

  function requiresDifference() {
    return level() !== "beginner";
  }

  function select(buttons, selected) {
    buttons.forEach((button) => {
      const active = button === selected;
      button.classList.toggle("selected", active);
      button.setAttribute("aria-pressed", active ? "true" : "false");
    });
  }

  function clearAnswers() {
    selectedSide = "";
    selectedDifference = "";
    select(sideButtons, null);
    select(differenceButtons, null);
  }

  function clearRound() {
    roundId = null;
    clearAnswers();
    ab?.clear();
  }

  function showLevel() {
    const activeLevel = level();
    if (differenceSection) differenceSection.hidden = !requiresDifference();
    differenceButtons.forEach((button) => {
      const shown = button.dataset.levels.split(" ").includes(activeLevel);
      button.hidden = !shown;
      if (!shown && button.value === selectedDifference) {
        selectedDifference = "";
        button.classList.remove("selected");
        button.setAttribute("aria-pressed", "false");
      }
    });
  }

  sideButtons.forEach((button) => {
    button.setAttribute("aria-pressed", "false");
    button.addEventListener("click", () => {
      selectedSide = button.value;
      select(sideButtons, button);
    });
  });
  differenceButtons.forEach((button) => {
    button.setAttribute("aria-pressed", "false");
    button.addEventListener("click", () => {
      selectedDifference = button.value;
      select(differenceButtons, button);
    });
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
      filters: { lmLevel: level() },
    }).then((data) => {
      if (AAi18n.serverError(data, loc)) return;
      try {
        ab.setRound(data.clips);
      } catch (error) {
        console.error("Invalid A/B round:", error);
        AAPractice.reset();
        Swal.fire({
          icon: "error",
          title: loc.validationErrorTitle,
          text: loc.validationErrorText,
        });
        return;
      }
      roundId = data.roundId;
      clearAnswers();
      ab.play("A");
    });
  });

  document.getElementById("validateGuess")?.addEventListener("click", () => {
    if (!roundId || !selectedSide || (requiresDifference() && !selectedDifference)) {
      AAi18n.incomplete(loc);
      return;
    }

    const userGuess = requiresDifference()
      ? `${selectedSide}|${selectedDifference}`
      : selectedSide;
    AAPractice.validate({ exerciseId, roundId, userGuess }).then((data) => {
      if (AAi18n.serverError(data, loc)) return;

      const detail = data.detail || {};
      const template = detail.louder === "A" ? loc.feedbackA : loc.feedbackB;
      const text = String(template || "").replace("{0}", String(detail.differenceDb ?? ""));
      const options = AAi18n.withRewards({
        icon: data.isCorrect ? "success" : "error",
        title: data.isCorrect ? loc.correctMessage : loc.wrongMessage,
        text,
      }, data);
      Swal.fire(options);
      clearRound();
    });
  });
});
