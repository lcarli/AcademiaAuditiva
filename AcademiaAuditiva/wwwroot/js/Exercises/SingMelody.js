// SingMelody: the student sings back the melody played, in any octave
// (SingExercise.js). Held and repeated notes sound the same to the detector,
// so each run of the same note is sent once, as the server compares them.
document.addEventListener("DOMContentLoaded", () => {
  const loc = AAi18n.localizer();
  const lengthSelect = document.getElementById("melodyLength");

  function names(sung) {
    return sung.map((note) => AASinging.pitchName(note.midi)).join(" ");
  }

  AASingExercise.start({
    name: "SingMelody",
    filters: () => ({ melodyLength: lengthSelect?.value || "4" }),
    sung: (notes) => AASinging.collapse(notes).map((midi) => ({ midi })),
    showResult(data, sung) {
      if (data.isCorrect) {
        AAi18n.result(data, loc);
        return;
      }
      AASinging.showResult(data, [
        AASinging.format(loc.wrongMessageText, AASingExercise.answerNames(data.answer).join(" ")),
        AASinging.format(loc.heardText, names(sung)),
      ]);
    },
    answerView: (answer) => AASingExercise.answerNames(answer).join(" "),
  });
});
