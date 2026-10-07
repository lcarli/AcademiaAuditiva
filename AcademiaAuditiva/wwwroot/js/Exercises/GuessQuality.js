document.addEventListener("DOMContentLoaded", () => {
    AcademiaAuditiva.init();
    AudioEngine.setupWaveform();

    const loc = AAi18n.localizer();
    let playToken = null;
    let roundId = null;
    let selectedGuess = "";

    const exerciseId = document.getElementById("exerciseId")?.value;
    const chordGroupSelect = document.getElementById("chordGroup");
    let chordGroup = chordGroupSelect?.value || "all";
    // The qualities of each chord type, as the server plays them.
    const qualityGroups = JSON.parse(document.getElementById("aa-quality-groups")?.dataset.groups || "{}");

    const guessButtons = document.querySelectorAll(".guessAnswer");
    guessButtons.forEach(button => {
        button.addEventListener("click", (e) => {
            guessButtons.forEach(btn => btn.classList.remove("selected"));
            e.target.classList.add("selected");
            selectedGuess = e.target.value;
        });
    });

    // Turning free practice on or off drops the round on screen.
    AAPractice.onReset(() => {
        playToken = null;
        roundId = null;
    });

    const playBtn = document.getElementById("Play");
    if (playBtn) {
        playBtn.addEventListener("click", () => {
            chordGroup = chordGroupSelect?.value || "all";

            AAPractice.play({
                exerciseId: exerciseId,
                filters: { chordGroup: chordGroup }
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
            if (!selectedGuess || !roundId) {
                AAi18n.incomplete(loc);
                return;
            }

            AAPractice.validate({
                exerciseId: exerciseId,
                roundId: roundId,
                userGuess: selectedGuess
            })
                .then(data => {
                    if (AAi18n.serverError(data, loc)) return;
                    AAi18n.result(data, loc);

                    selectedGuess = "";
                    playToken = null;
                    roundId = null;
                    guessButtons.forEach((btn) => btn.classList.remove("selected"));
                });
        });
    }

    // Shows the qualities of the chord type picked; a hidden answer can't stay selected.
    function updateVisibleButtons() {
        const allowed = qualityGroups[chordGroup] || qualityGroups.all || [];
        guessButtons.forEach(btn => {
            const visible = allowed.includes(btn.value);
            btn.style.display = visible ? "" : "none";
            if (!visible && btn.classList.contains("selected")) {
                btn.classList.remove("selected");
                selectedGuess = "";
            }
        });
    }

    chordGroupSelect?.addEventListener("change", (e) => {
        chordGroup = e.target.value;
        updateVisibleButtons();
    });
    updateVisibleButtons();
});
