// The round of the exercises where the student sings back what is played
// (SingNote, SingInterval and SingMelody): Play asks for a round and plays it,
// Replay plays it again, Record (singing.js) records the student, and Validate
// finds the notes they sang and sends their names. Each page says which notes
// it sends and how it shows the answer:
//
//   AASingExercise.start({
//     name,                 // for the console
//     filters(),            // the round's filters, if it has any
//     onRound(data),        // a round arrived (null: the round on screen is gone)
//     sung(notes),          // the notes to send ({ midi, cents }) of those PitchDetector found
//     needs,                // how many notes sung() must find (1 by default)
//     tooFew(),             // says sung() found too few
//     showResult(data, sung),
//     answerView(answer),   // the answer revealed in free practice
//   });
(function (window, document) {
  "use strict";

  function start(config) {
    const loc = AAi18n.localizer();
    const exerciseId = document.getElementById("exerciseId")?.value;
    const validateBtn = document.getElementById("validateGuess");
    let round = null; // { roundId, playToken }
    let busy = false;

    AudioEngine.setupWaveform();
    AAPractice.setAnswerView(config.answerView);

    function drop() {
      round = null;
      config.onRound?.(null);
    }

    // Turning free practice on or off drops the round on screen, and the
    // recording sung to it. An answer being checked ends its round anyway.
    AAPractice.onReset(() => {
      if (busy) return;
      AudioEngine.stop();
      drop();
      AASinging.cancel();
    });

    document.getElementById("Play")?.addEventListener("click", async () => {
      if (busy || !exerciseId) return;
      try {
        // A recording is for the round it was sung to.
        await AASinging.cancel();
        const body = { exerciseId };
        if (config.filters) body.filters = config.filters();
        const data = await AAPractice.play(body);
        if (AAi18n.serverError(data, loc)) return;

        round = { roundId: data.roundId, playToken: data.playToken };
        config.onRound?.(data);
        await AudioEngine.playToken(round.playToken);
      } catch (err) {
        console.error(`${config.name} request failed:`, err);
      }
    });

    // The microphone would hear what is played (echo cancellation is off), so
    // a recording stops first, and is kept.
    document.getElementById("Replay")?.addEventListener("click", async () => {
      if (!round) {
        AAi18n.noAudio(loc);
        return;
      }
      await AASinging.stop();
      if (round) AudioEngine.playToken(round.playToken).catch((err) => console.error("Replay failed:", err));
    });

    AASinging.attach({ ready: () => round !== null, busy: () => busy });

    validateBtn?.addEventListener("click", async () => {
      if (busy) return;
      if (!round) {
        AAi18n.noAudio(loc);
        return;
      }

      busy = true;
      validateBtn.disabled = true;
      try {
        const notes = await AASinging.transcribe();
        if (!notes) return;
        const sung = config.sung(notes);
        if (sung.length < (config.needs || 1)) {
          await AASinging.hideAnalyzing();
          config.tooFew?.();
          return;
        }

        const data = await AAPractice.validate({
          exerciseId,
          roundId: round.roundId,
          userGuess: sung.map((note) => AASinging.noteName(note.midi)).join("|"),
        });
        await AASinging.hideAnalyzing();
        if (AAi18n.serverError(data, loc)) return;

        // The server forgets the round once it is answered.
        drop();
        config.showResult(data, sung);
      } catch (err) {
        console.error(`${config.name} validation failed:`, err);
        await AASinging.hideAnalyzing();
        AASinging.showError();
      } finally {
        busy = false;
        validateBtn.disabled = false;
      }
    });
  }

  // The notes of an answer ("C4|G4") by their names in the page's language, without octaves.
  function answerNames(answer) {
    return String(answer || "")
      .split("|")
      .map(AASinging.midi)
      .filter((midi) => midi !== null)
      .map(AASinging.pitchName);
  }

  window.AASingExercise = { start, answerNames };
})(window, document);
