// SingNote: the student sings the note played, in any octave (SingExercise.js).
// The note they held longest is their answer, and the result says how close
// to its pitch they sang it.
document.addEventListener("DOMContentLoaded", () => {
  const loc = AAi18n.localizer();

  AASingExercise.start({
    name: "SingNote",
    sung: (notes) => AASinging.longest(notes, 1),
    showResult(data, sung) {
      const heard = AASinging.format(loc.heardText, AASinging.pitchName(sung[0].midi));
      AASinging.showResult(data, data.isCorrect
        ? [heard, AASinging.tuning(sung[0].cents)]
        : [AASinging.format(loc.wrongMessageText, AASingExercise.answerNames(data.answer).join(" ")), heard]);
    },
    answerView: (answer) => AASingExercise.answerNames(answer).join(" "),
  });
});
