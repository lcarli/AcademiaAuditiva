document.addEventListener("DOMContentLoaded", () => {
    //Iniciate Audio
    AcademiaAuditiva.init();
    AudioEngine.setupWaveform();

    //Iniciate Variables
    const exerciseIdInput = document.getElementById("exerciseId");
    const exerciseId = exerciseIdInput ? exerciseIdInput.value : null;
    const rangeSelect = document.querySelector('[name="Range"]');
    const selectedRange = rangeSelect ? rangeSelect.value : "C3-C5";
    const chordTypeSelect = document.getElementById("chordType");
    const chordType = chordTypeSelect ? chordTypeSelect.value : "major";

    let randomRoot = "";
    let randomQuality = "";
    let userRoot = "";
    let userQuality = "major";
    const exerciseStartTime = Date.now();

    //Iniciate Click Events
    const rootButtons = document.querySelectorAll(".guessNote");
    rootButtons.forEach(btn => {
        btn.addEventListener("click", () => {
            userRoot = btn.value;
            rootButtons.forEach(b => b.classList.remove("selected"));
            btn.classList.add("selected");
        });
    });

    const qualityButtons = document.querySelectorAll(".guessQuality");
    qualityButtons.forEach(btn => {
        btn.addEventListener("click", () => {
            userQuality = btn.value;
            qualityButtons.forEach(b => b.classList.remove("selected"));
            btn.classList.add("selected");
        });
    });


    //Inciate Play and Replay Events
    const playBtn = document.getElementById("Play");
    if (playBtn) {
        playBtn.addEventListener("click", () => {
            if (!exerciseId) return;

            fetch("/Exercise/RequestPlay", {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify({
                    exerciseId: exerciseId,
                    filters: {
                        range: selectedRange,
					    chordType: chordType
                    }
                })
            })
            .then(resp => resp.json())
            .then(data => {
                window.currentChordNotes = data.notes;
                randomRoot = data.root;
                randomQuality = data.quality;
                AudioEngine.playChord(data.notes, 1);
            });            
        });
    }

    const replayBtn = document.getElementById("Replay");
    if (replayBtn) {
        replayBtn.addEventListener("click", () => {
            if (!randomRoot || !randomQuality) {
                Swal.fire({
                    icon: "warning",
                    title: "No chord loaded",
                    text: "Click 'Play' first to generate a chord."
                });
                return;
            }
            AudioEngine.playChord(window.currentChordNotes, 1);
        });
    }


    //Inciate Validate Event
    const validateBtn = document.getElementById("validateGuess");
    if (validateBtn) {
        validateBtn.addEventListener("click", () => {
            if(chordType!= "major" && chordType!="minor"){
                if (!userRoot || !userQuality || !randomRoot || !randomQuality) {
                    Swal.fire({
                        icon: "warning",
                        title: "Missing data",
                        text: "Generate a chord and select your root/quality before validating."
                    });
                    return;
                }
            }
            fetch("/Exercise/ValidateExercise", {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify({
                    ExerciseId: exerciseId,
                    UserGuess: userRoot + "|" + userQuality,
                    ActualAnswer: randomRoot + "|" + randomQuality,
                    TimeSpentSeconds: Math.floor((Date.now() - exerciseStartTime) / 1000)
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
                    Swal.fire("Correct!", "You got the chrod right!", "success");
                } else {
                    if (errorCountEl) {
                        errorCountEl.innerText = parseInt(errorCountEl.innerText) + 1;
                    }
                    Swal.fire("Wrong!", `The correct chord was ${randomRoot.replace(/\d/g, "").toUpperCase() + " " + randomQuality}.`, "error");
                }
            
                userRoot = "";
                userQuality = "";
                randomRoot = "";
                randomQuality = "";
                rootButtons.forEach(b => b.classList.remove("selected"));
                qualityButtons.forEach(b => b.classList.remove("selected"));
            });            
        });
    }
});