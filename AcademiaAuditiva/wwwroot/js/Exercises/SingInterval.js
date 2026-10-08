// SingInterval: the student hears a note and sees an interval, then sings the
// note and the note at that interval above or below it (SingExercise.js). The
// two notes they held longest are their answer, in the order sung; the server
// sends the interval and its direction, never the second note.
document.addEventListener("DOMContentLoaded", () => {
  const loc = AAi18n.localizer();
  const prompt = document.getElementById("singIntervalPrompt");
  const levelSelect = document.getElementById("siLevel");
  const directionSelect = document.getElementById("intervalDirection");

  // The semitones of the interval codes of a round (MusicTheoryService.IntervalSemitones).
  const SEMITONES = { "2m": 1, "2M": 2, "3m": 3, "3M": 4, "4J": 5, "4A": 6, "5d": 6, "5J": 7, "6m": 8, "6M": 9, "7m": 10, "7M": 11, "8J": 12 };

  // The interval's name in the page's language, from 0 (unison) to 12 semitones.
  function intervalName(semitones) {
    return document.querySelector(`#intervalNames [data-semitones="${semitones}"]`)?.textContent.trim() || "";
  }

  function directionName(direction) {
    return direction === "desc" ? loc.directionDesc : loc.directionAsc;
  }

  function showPrompt(data) {
    if (!prompt) return;
    const semitones = data ? SEMITONES[data.interval] : undefined;
    if (semitones === undefined) {
      prompt.replaceChildren();
      return;
    }
    const icon = document.createElement("i");
    icon.className = data.direction === "desc" ? "bi bi-arrow-down" : "bi bi-arrow-up";
    icon.setAttribute("aria-hidden", "true");
    const name = document.createElement("strong");
    name.textContent = intervalName(semitones);
    const direction = document.createElement("span");
    direction.textContent = directionName(data.direction);
    prompt.replaceChildren(icon, name, direction);
  }

  // "Perfect 5th, descending": the interval between the two notes sung, when it is one of the names.
  function sungInterval(sung) {
    const steps = sung[1].midi - sung[0].midi;
    const name = intervalName(Math.abs(steps));
    if (!name) return "";
    if (steps === 0) return name;
    const lang = document.documentElement.lang || undefined;
    return `${name}, ${directionName(steps < 0 ? "desc" : "asc").toLocaleLowerCase(lang)}`;
  }

  function names(answer) {
    return AASingExercise.answerNames(answer).join(" → ");
  }

  AASingExercise.start({
    name: "SingInterval",
    filters: () => ({
      siLevel: levelSelect?.value || "easy",
      intervalDirection: directionSelect?.value || "asc",
    }),
    onRound: showPrompt,
    sung: (notes) => AASinging.longest(notes, 2),
    needs: 2,
    tooFew: () => AAi18n.warning(loc.incompleteTitle, loc.oneNoteText),
    showResult(data, sung) {
      const heard = AASinging.format(loc.heardText, sung.map((note) => AASinging.pitchName(note.midi)).join(" → "));
      if (data.isCorrect) {
        AASinging.showResult(data, [heard]);
        return;
      }
      const interval = sungInterval(sung);
      AASinging.showResult(data, [
        AASinging.format(loc.wrongMessageText, names(data.answer)),
        heard,
        interval && AASinging.format(loc.heardIntervalText, interval),
      ]);
    },
    answerView: names,
  });
});
