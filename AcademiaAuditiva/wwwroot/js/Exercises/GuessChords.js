document.addEventListener("DOMContentLoaded", () => {
    AcademiaAuditiva.init();
    AudioEngine.setupWaveform();

    const loc = AAi18n.localizer();
    const exerciseId = document.getElementById("exerciseId")?.value;
    const chordTypeSelect = document.getElementById("chordType");
    let chordType = chordTypeSelect ? chordTypeSelect.value : "major";

    let playToken = null;
    let roundId = null;
    let userRoot = "";
    let userQuality = "major";
    const exerciseStartTime = Date.now();

    const rootButtons = document.querySelectorAll(".guessAnswer");
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
        btn.style.display = "none";
    });

    const playBtn = document.getElementById("Play");
    if (playBtn) {
        playBtn.addEventListener("click", () => {
            if (!exerciseId) return;

            fetch("/Exercise/RequestPlay", {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify({
                    exerciseId: exerciseId,
                    filters: { chordType: chordType }
                })
            })
            .then(resp => resp.json())
            .then(data => {
                playToken = data.playToken;
                roundId = data.roundId;
                if (playToken) AudioEngine.playToken(playToken);
            });
        });
    }

    const replayBtn = document.getElementById("Replay");
    if (replayBtn) {
        replayBtn.addEventListener("click", () => {
            if (!playToken) {
                AAi18n.noAudio(loc);
                return;
            }
            AudioEngine.playToken(playToken);
        });
    }

    const validateBtn = document.getElementById("validateGuess");
    if (validateBtn) {
        validateBtn.addEventListener("click", () => {
            if (chordType != "major" && chordType != "minor") {
                if (!userRoot || !userQuality || !roundId) {
                    AAi18n.incomplete(loc);
                    return;
                }
            }
            fetch("/Exercise/ValidateExercise", {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify({
                    ExerciseId: exerciseId,
                    RoundId: roundId,
                    UserGuess: userRoot + "|" + userQuality,
                    TimeSpentSeconds: Math.floor((Date.now() - exerciseStartTime) / 1000)
                })
            })
            .then(resp => resp.json())
            .then(data => {
                if (AAi18n.serverError(data, loc)) return;
                const correctCountEl = document.getElementById("correctCount");
                const errorCountEl = document.getElementById("errorCount");
                if (data.isCorrect) {
                    if (correctCountEl) {
                        correctCountEl.innerText = parseInt(correctCountEl.innerText) + 1;
                    }
                    AAi18n.result(data, loc);
                } else {
                    if (errorCountEl) {
                        errorCountEl.innerText = parseInt(errorCountEl.innerText) + 1;
                    }
                    AAi18n.result(data, loc);
                }

                userRoot = "";
                playToken = null;
                roundId = null;
                rootButtons.forEach(b => b.classList.remove("selected"));
                qualityButtons.forEach(b => b.classList.remove("selected"));
                if (chordType != "major" && chordType != "minor") { userQuality = ""; }
            });
        });
    }

    if (chordTypeSelect) {
        chordTypeSelect.addEventListener("change", () => {
            const selectedType = chordTypeSelect.value;
            chordType = "";
            qualityButtons.forEach(btn => btn.classList.remove("selected"));

            switch (selectedType) {
                case "major":
                    qualityButtons.forEach(btn => btn.style.display = "none");
                    chordType = selectedType;
                    userQuality = selectedType;
                    break;
                case "minor":
                    qualityButtons.forEach(btn => btn.style.display = "none");
                    chordType = selectedType;
                    userQuality = selectedType;
                    break;
                case "both":
                    qualityButtons.forEach(btn => {
                        const val = btn.value;
                        btn.style.display = (val === "major" || val === "minor") ? "inline-block" : "none";
                    });
                    chordType = selectedType;
                    break;
                case "all":
                default:
                    qualityButtons.forEach(btn => btn.style.display = "inline-block");
                    break;
            }
        });
    }
});
