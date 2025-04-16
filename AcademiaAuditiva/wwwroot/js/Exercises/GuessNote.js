document.addEventListener("DOMContentLoaded", () => {
    //Iniciate Audio
    AcademiaAuditiva.init();
    AudioEngine.setupWaveform();

    //Iniciate Variables
    const exerciseIdInput = document.getElementById("exerciseId");
    const exerciseId = exerciseIdInput ? exerciseIdInput.value : null;

    let randomNote = "";
    let userGuessedNote = "";
    const exerciseStartTime = Date.now();

    //Iniciate Click Events
    const guessButtons = document.querySelectorAll(".guessAnswer");
    guessButtons.forEach(button => {
        button.addEventListener("click", () => {
            userGuessedNote = button.value;
            guessButtons.forEach(btn => btn.classList.remove("selected"));
            button.classList.add("selected");
        });
    });

    //Inciate Play and Replay Events
    const playButton = document.getElementById("Play");
    if (playButton) {
        playButton.addEventListener("click", () => {
            if (!exerciseId) return;
            fetch("/Exercise/RequestPlay", {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify({
                    exerciseId: exerciseId,
                })
            })
            .then(resp => resp.json())
            .then(data => {
                randomNote = data.note;
                AudioEngine.playNote(randomNote, 1);
            });
        });
    }

    const replayButton = document.getElementById("Replay");
    if (replayButton) {
        replayButton.addEventListener("click", () => {
            if (!randomNote) {
                Swal.fire({
                    icon: "warning",
                    title: "No note loaded",
                    text: "Click 'Play' first to generate the note."
                });
                return;
            }
            AudioEngine.playNote(randomNote, 1);
        });
    }

    //Inciate Validate Event
    const validateBtn = document.getElementById("validateGuess");
    if (validateBtn) {
        validateBtn.addEventListener("click", () => {
            if (!userGuessedNote || !randomNote) {
                Swal.fire({
                    icon: "warning",
                    title: "Missing data",
                    text: "Generate a note and select your answer before validating."
                });
                return;
            }
            fetch("/Exercise/ValidateExercise", {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify({
                    ExerciseId: exerciseId,
                    userGuess: userGuessedNote,
                    ActualAnswer: randomNote,
                    timeSpentSeconds: Math.floor((Date.now() - exerciseStartTime) / 1000)
                })
            })
            .then(resp => resp.json())
            .then(data => {
                const correctCountEl = document.getElementById("correctCount");
                const errorCountEl = document.getElementById("errorCount");
                if (data.isCorrect) {
                    if (correctCountEl) {
                        correctCountEl.innerText = parseInt(correctCountEl.innerText) + 1;
                    }
                    Swal.fire("Correct!", "You got the note right!", "success");
                } else {
                    if (errorCountEl) {
                        errorCountEl.innerText = parseInt(errorCountEl.innerText) + 1;
                    }
                    Swal.fire("Wrong!", `The correct note was ${randomNote.replace(/\d/g, "").toUpperCase()}.`, "error");
                }
                userGuessedNote = "";
                randomNote = "";
                guessButtons.forEach(btn => btn.classList.remove("selected"));
            });
        });
    }
});