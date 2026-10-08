// Sight-singing: the student reads a short melody, plays its first note on
// the piano when they want it, and records themselves singing it. The
// recording is transcribed in the browser (singing.js and pitch-detector.js):
// only the names of the notes sung are sent to the server, and the audio never
// leaves the device (see the privacy policy).
document.addEventListener("DOMContentLoaded", () => {
  const loc = AAi18n.localizer();
  const exerciseId = document.getElementById("exerciseId")?.value;
  const sheet = document.getElementById("output-sheet");
  const generateBtn = document.getElementById("Generate");
  const startingNoteBtn = document.getElementById("playStartingNote");
  const validateBtn = document.getElementById("validateGuess");

  const NATURAL_PITCHES = { C: 0, D: 2, E: 4, F: 5, G: 7, A: 9, B: 11 };
  const DURATIONS = { 4: "w", 2: "h", 1: "q", 0.5: "8", 0.25: "16" };

  let round = null; // { melody, startingNoteToken }
  let busy = false;

  function parseNote(name) {
    const match = /^([A-G])([#b]?)(-?\d+)$/.exec(String(name || "").trim());
    if (!match) return null;
    const octave = parseInt(match[3], 10);
    const shift = match[2] === "#" ? 1 : match[2] === "b" ? -1 : 0;
    return {
      letter: match[1],
      accidental: match[2],
      octave,
      midi: (octave + 1) * 12 + NATURAL_PITCHES[match[1]] + shift,
    };
  }

  function melodyNotes(melody) {
    return melody
      .filter((item) => item.type === "note")
      .map((item) => parseNote(item.note))
      .filter(Boolean);
  }

  // ---------- Sheet music ----------

  function drawStaff(melody) {
    const VF = Vex.Flow;
    sheet.replaceChildren();
    const renderer = new VF.Renderer(sheet, VF.Renderer.Backends.SVG);
    renderer.resize(500, 150);
    const context = renderer.getContext();
    const stave = new VF.Stave(10, 30, 480).addClef("treble");
    if (melody.length > 0) stave.addTimeSignature("4/4");
    stave.setContext(context).draw();

    const names = melodyNotes(melody).map((note) => AASinging.pitchName(note.midi));
    if (names.length === 0) {
      sheet.removeAttribute("role");
      sheet.removeAttribute("aria-label");
      return;
    }

    const tickables = melody.map((item) => {
      const duration = DURATIONS[item.duration] || "q";
      const note = item.type === "note" ? parseNote(item.note) : null;
      if (!note) {
        return new VF.StaveNote({ clef: "treble", keys: ["b/4"], duration: duration + "r" });
      }
      const staveNote = new VF.StaveNote({
        clef: "treble",
        keys: [`${note.letter.toLowerCase()}${note.accidental}/${note.octave}`],
        duration,
      });
      if (note.accidental) staveNote.addModifier(new VF.Accidental(note.accidental), 0);
      return staveNote;
    });

    const voice = new VF.Voice({ num_beats: 4, beat_value: 4 }).setMode(VF.Voice.Mode.SOFT);
    voice.addTickables(tickables);
    new VF.Formatter().joinVoices([voice]).format([voice], 400);
    voice.draw(context, stave);

    sheet.setAttribute("role", "img");
    sheet.setAttribute("aria-label", names.join(", "));
  }

  // ---------- Starting note ----------

  // The server mixes the melody's first note on the piano. It is fetched with
  // the melody but only played when the student asks for it, and stops a
  // recording first: the microphone would hear the piano (echo cancellation
  // is off) and take it for a sung note.
  async function playStartingNote() {
    if (!round) {
      AAi18n.noAudio(loc);
      return;
    }
    await AASinging.stop();
    AudioEngine.playToken(round.startingNoteToken).catch((err) => {
      console.error("Starting note playback failed:", err);
      AASinging.showError();
    });
  }

  // ---------- Answer ----------

  function showWrongAnswer(data, sung) {
    const expected = String(data.answer || "").split("|").map(AASinging.midi).filter((note) => note !== null);
    AASinging.showResult(data, [
      AASinging.format(loc.wrongMessageText, expected.map(AASinging.pitchName).join(" ")),
      AASinging.format(loc.heardText, sung.map(AASinging.pitchName).join(" ")),
    ]);
  }

  // ---------- Buttons ----------

  // Turning free practice on or off drops the round on screen. An answer
  // being checked ends its round anyway, so it is left to finish.
  AAPractice.onReset(() => {
    if (busy) return;
    round = null;
    AudioEngine.stop();
    drawStaff([]);
    AASinging.cancel();
  });

  generateBtn?.addEventListener("click", async () => {
    if (!exerciseId || busy) return;
    // A new melody is silent: its starting note waits for its button.
    AudioEngine.stop();
    generateBtn.disabled = true;
    try {
      const data = await AAPractice.play({ exerciseId });
      if (AAi18n.serverError(data, loc)) return;
      if (!Array.isArray(data.melody) || melodyNotes(data.melody).length === 0 || !data.startingNoteToken) {
        throw new Error("The response has no melody.");
      }

      await AASinging.cancel();
      round = { melody: data.melody, startingNoteToken: data.startingNoteToken };
      drawStaff(round.melody);
      AudioEngine.preload(round.startingNoteToken);
    } catch (err) {
      console.error("SolfegeMelody request failed:", err);
      AASinging.showError();
    } finally {
      generateBtn.disabled = false;
    }
  });

  startingNoteBtn?.addEventListener("click", () => playStartingNote());

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
      const sung = AASinging.collapse(notes);

      const data = await AAPractice.validate({
        exerciseId,
        userGuess: sung.map(AASinging.noteName).join("|"),
      });
      // The server forgets the expected answer after one attempt.
      round = null;
      await AASinging.hideAnalyzing();
      if (AAi18n.serverError(data, loc)) return;

      if (data.isCorrect) {
        AAi18n.result(data, loc);
      } else {
        showWrongAnswer(data, sung);
      }
    } catch (err) {
      console.error("SolfegeMelody validation failed:", err);
      await AASinging.hideAnalyzing();
      AASinging.showError();
    } finally {
      busy = false;
      validateBtn.disabled = false;
    }
  });

  drawStaff([]);
});
