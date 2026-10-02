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

    // Turning free practice on or off drops the round on screen.
    AAPractice.onReset(() => {
        playToken = null;
        roundId = null;
    });

    const playBtn = document.getElementById("Play");
    if (playBtn) {
        playBtn.addEventListener("click", () => {
            if (!exerciseId) return;

            AAPractice.play({
                exerciseId: exerciseId,
                filters: { chordType: chordType }
            })
            .then(data => {
                if (AAi18n.serverError(data, loc)) return;
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
            AAPractice.validate({
                ExerciseId: exerciseId,
                RoundId: roundId,
                UserGuess: userRoot + "|" + userQuality,
                TimeSpentSeconds: Math.floor((Date.now() - exerciseStartTime) / 1000)
            })
            .then(data => {
                if (AAi18n.serverError(data, loc)) return;
                AAi18n.result(data, loc);

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
